"""Live telemetry relay: the headset can reach this server over the internet but often not the instructor's PC
(venue Wi-Fi isolates devices). The headset posts the same SpacetimeDB reducer calls here; a bridge on the PC
(scripts/live-relay-bridge.py) long-polls them and replays them into its local SpacetimeDB.

Paths mirror SpacetimeDB's HTTP API under /api/v1/live so the headset only swaps its base URL:
  POST /api/v1/live/v1/database/{db}/call/{reducer}   body: the reducer's JSON argument array
  GET  /api/v1/live/v1/database/{db}/schema           reachability probe
  GET  /api/v1/live/poll?db=...&after=N&wait=20       bridge: calls with seq > N (waits up to `wait` s)
In memory only; the last MAX_ITEMS calls per database are kept. Nothing is scored or stored here.
"""
from __future__ import annotations

import asyncio
import re
import time
from collections import deque

from fastapi import APIRouter, HTTPException, Request

router = APIRouter(prefix="/api/v1/live", tags=["live"])

MAX_ITEMS = 4000
MAX_BODY = 64 * 1024
_NAME = re.compile(r"^[A-Za-z0-9_\-]{1,64}$")


class _Stream:
    def __init__(self) -> None:
        self.items: deque[tuple[int, str, str, float]] = deque(maxlen=MAX_ITEMS)
        self.seq = 0
        self.changed = asyncio.Event()


_streams: dict[str, _Stream] = {}


def _stream(db: str) -> _Stream:
    if not _NAME.match(db):
        raise HTTPException(400, "bad database name")
    s = _streams.get(db)
    if s is None:
        if len(_streams) > 50:
            raise HTTPException(429, "too many live databases")
        s = _streams[db] = _Stream()
    return s


@router.get("/v1/database/{db}/schema")
def probe(db: str):
    _stream(db)
    return {"relay": True}


@router.post("/v1/database/{db}/call/{reducer}")
async def call(db: str, reducer: str, request: Request):
    if not _NAME.match(reducer):
        raise HTTPException(400, "bad reducer name")
    body = await request.body()
    if len(body) > MAX_BODY:
        raise HTTPException(413, "too large")
    text = body.decode("utf-8", errors="replace").strip()
    if not text.startswith("["):
        raise HTTPException(400, "body must be the reducer's JSON argument array")
    s = _stream(db)
    s.seq += 1
    s.items.append((s.seq, reducer, text, time.time()))
    s.changed.set()
    return {"seq": s.seq}


@router.get("/poll")
async def poll(db: str, after: int = 0, wait: float = 20.0):
    s = _stream(db)
    wait = max(0.0, min(wait, 25.0))
    if after > s.seq:
        after = 0  # the server restarted: start again from what is buffered
    if not any(seq > after for seq, *_ in s.items) and wait > 0:
        s.changed.clear()
        try:
            await asyncio.wait_for(s.changed.wait(), timeout=wait)
        except asyncio.TimeoutError:
            pass
    items = [{"seq": seq, "reducer": r, "args": a, "t": t} for seq, r, a, t in s.items if seq > after]
    return {"seq": s.seq, "items": items[:500]}
