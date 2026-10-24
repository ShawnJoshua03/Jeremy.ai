"""Live headset view: publish from Quest, watch from the instructor dashboard.

Auth. HTTP routes sit behind ApiKeyGate (X-API-Key). ApiKeyGate does not check
WebSocket scopes, so each WebSocket checks auth itself:
- publish: X-API-Key header, compared with API_KEY (required when API_KEY is set)
- watch: a short-lived token from POST /live/viewer-token, passed as ?token=
  Browsers cannot set headers on WebSocket, so the token is what keeps the
  dashboard's key out of the URL. When API_KEY is unset everything is open,
  the same as the rest of the API.
A watch connection that sends an Origin header must come from CORS_ORIGINS.
"""
from __future__ import annotations

import asyncio
import contextlib
import hashlib
import hmac
import json
import os
import time

from fastapi import APIRouter, WebSocket, WebSocketDisconnect

import database as db
from live.hub import (
    CLOSE_FORBIDDEN,
    CLOSE_FULL,
    CLOSE_NOT_FOUND,
    CLOSE_REPLACED,
    CLOSE_TOO_LARGE,
    CLOSE_UNAUTHORIZED,
    HUB,
    KIND_JPEG,
    KIND_PCM16,
    MAX_FRAME_BYTES,
)
from mentorship.router import _require_session

router = APIRouter(prefix="/api/v1/sessions", tags=["live"])

TOKEN_TTL_S = 300
_DEFAULT_ORIGIN = "http://localhost:5173"
_HEADER_MIN = 9  # kind (1) + t_ms (8)


def _api_key() -> str:
    return os.environ.get("API_KEY", "").strip()


def _allowed_origins() -> list[str]:
    raw = os.environ.get("CORS_ORIGINS") or ""
    text = raw if raw.strip() else _DEFAULT_ORIGIN
    return [part.strip() for part in text.split(",") if part.strip()]


def _sign(sid: str, expires: int, key: str) -> str:
    return hmac.new(key.encode(), f"{sid}:{expires}".encode(), hashlib.sha256).hexdigest()


def make_viewer_token(sid: str, key: str, now: float | None = None) -> str:
    expires = int((now if now is not None else time.time()) + TOKEN_TTL_S)
    return f"{expires}.{_sign(sid, expires, key)}"


def check_viewer_token(sid: str, token: str, key: str, now: float | None = None) -> bool:
    expires_text, _, signature = token.partition(".")
    if not expires_text.isdigit() or not signature:
        return False
    expires = int(expires_text)
    if expires < (now if now is not None else time.time()):
        return False
    return hmac.compare_digest(signature, _sign(sid, expires, key))


def _session_exists(sid: str) -> bool:
    with db.SessionLocal() as session:
        return session.get(db.TrainingSession, sid) is not None


def _status_text(sid: str) -> str:
    return json.dumps({"type": "status", **HUB.status(sid)})


@router.get("/{sid}/live")
def live_status(sid: str):
    with db.SessionLocal() as session:
        _require_session(session, sid)
    return HUB.status(sid)


@router.post("/{sid}/live/viewer-token")
def viewer_token(sid: str):
    with db.SessionLocal() as session:
        _require_session(session, sid)
    key = _api_key()
    token = make_viewer_token(sid, key) if key else ""
    return {"token": token, "expiresIn": TOKEN_TTL_S if key else 0}


@router.websocket("/{sid}/live/publish")
async def publish(ws: WebSocket, sid: str):
    await ws.accept()
    key = _api_key()
    if key and ws.headers.get("x-api-key", "") != key:
        await ws.close(CLOSE_UNAUTHORIZED)
        return
    if not _session_exists(sid):
        await ws.close(CLOSE_NOT_FOUND)
        return

    previous = HUB.rooms[sid].publisher if sid in HUB.rooms else None
    if previous is not None:
        with contextlib.suppress(RuntimeError):
            await previous.close(CLOSE_REPLACED)
    room = HUB.claim_publisher(sid, ws)
    room.broadcast(("text", _status_text(sid)))
    try:
        while room.publisher is ws:
            msg = await ws.receive()
            if msg["type"] == "websocket.disconnect":
                break
            text = msg.get("text")
            if text is not None:
                if len(text) > 2048:
                    continue
                try:
                    body = json.loads(text)
                except ValueError:
                    continue
                if isinstance(body, dict) and body.get("type") == "hello":
                    room.hello = text
                    room.broadcast(("text", text))
                continue
            data = msg.get("bytes")
            if data is None:
                continue
            if len(data) > MAX_FRAME_BYTES:
                await ws.close(CLOSE_TOO_LARGE)
                break
            if len(data) < _HEADER_MIN:
                continue
            kind = data[0]
            if kind == KIND_JPEG:
                room.last_frame = data
                room.broadcast(("bytes", data))
            elif kind == KIND_PCM16:
                room.broadcast(("bytes", data))
    except WebSocketDisconnect:
        pass
    finally:
        HUB.release_publisher(sid, ws)
        if sid in HUB.rooms:
            HUB.rooms[sid].broadcast(("text", _status_text(sid)))


async def _pump(ws: WebSocket, queue: asyncio.Queue) -> None:
    while True:
        kind, data = await queue.get()
        if kind == "text":
            await ws.send_text(data)
        else:
            await ws.send_bytes(data)


async def _until_closed(ws: WebSocket) -> None:
    while True:
        msg = await ws.receive()
        if msg["type"] == "websocket.disconnect":
            return


@router.websocket("/{sid}/live/watch")
async def watch(ws: WebSocket, sid: str, token: str = ""):
    await ws.accept()
    origin = ws.headers.get("origin")
    if origin is not None and origin not in _allowed_origins():
        await ws.close(CLOSE_FORBIDDEN)
        return
    key = _api_key()
    if key and not check_viewer_token(sid, token, key):
        await ws.close(CLOSE_UNAUTHORIZED)
        return
    if not _session_exists(sid):
        await ws.close(CLOSE_NOT_FOUND)
        return
    viewer = HUB.join(sid)
    if viewer is None:
        await ws.close(CLOSE_FULL)
        return
    room = HUB.room(sid)
    try:
        await ws.send_text(_status_text(sid))
        if room.hello is not None:
            await ws.send_text(room.hello)
        if room.last_frame is not None:
            await ws.send_bytes(room.last_frame)
        sender = asyncio.create_task(_pump(ws, viewer.queue))
        watcher = asyncio.create_task(_until_closed(ws))
        await asyncio.wait({sender, watcher}, return_when=asyncio.FIRST_COMPLETED)
        for task in (sender, watcher):
            task.cancel()
            with contextlib.suppress(asyncio.CancelledError, WebSocketDisconnect, RuntimeError):
                await task
    except (WebSocketDisconnect, RuntimeError):
        pass
    finally:
        HUB.leave(sid, viewer)
