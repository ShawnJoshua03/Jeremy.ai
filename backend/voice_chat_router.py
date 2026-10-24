"""Push-to-talk conversation with the port-placement coach (voice_chat.py).

POST /api/v1/sessions/{sid}/coach/talk
    Body: JSON {"text": "...", "audio": false} OR raw WAV bytes with Content-Type: audio/wav
    (?audio=true works for both). Returns {transcript, intent, port, reply_text, speak,
    audio_available, turn, source} and, when audio was requested and speech is configured,
    audio_url (POST it with {"turn": n} to get the WAV).
POST /api/v1/sessions/{sid}/coach/talk/audio
    JSON {"turn": n} or {} -> WAV of a stored reply (no new turn, no second transcription), or the same
    input as /coach/talk -> runs a turn and returns its WAV, with the turn in X-Coach-* headers.
    503 keeps the text: the JSON body carries reply_text when there is one.

Status codes: 404 unknown session, 409 not lap_chole/port_placement, 413 audio too large,
415 other content types, 422 bad/empty input or empty transcript, 429 rate limit,
503 STT or TTS unavailable (text still returned where there is text).
Raw audio is never logged or stored; only the transcript text is kept in the in-memory history.
"""
from __future__ import annotations

import json
import os
from urllib.parse import quote

import database as db
import port_coach_router
import ratelimit
import voice_chat
from fastapi import APIRouter, HTTPException, Request, Response
from fastapi.responses import JSONResponse
from starlette.concurrency import run_in_threadpool
from speech import cartesia, stt

router = APIRouter(prefix="/api/v1/sessions", tags=["coaching"])
WAV_TYPES = {"audio/wav", "audio/x-wav", "audio/wave", "audio/vnd.wave"}
MAX_JSON_BYTES = 4096
NO_STORE = {"Cache-Control": "no-store"}
STT_DOWN = "Speech recognition is unavailable; type the question or try again."
TTS_DOWN = "Speech is unavailable; use text feedback."


def _speech_on() -> bool:
    return bool(os.getenv("CARTESIA_API_KEY", "").strip())


def _exercise(sid: str) -> str:
    with db.SessionLocal() as s:
        row = s.get(db.TrainingSession, sid)
        if row is None:
            raise HTTPException(404, "session not found")
        if row.exercise not in port_coach_router.COACHED_EXERCISES:
            raise HTTPException(409, "voice coaching is only for lap_chole and port_placement sessions")
        return row.exercise


async def _read_body(request: Request, limit: int) -> bytes:
    """Read at most `limit` bytes; 413 beyond that (checked while streaming, not after)."""
    declared = request.headers.get("content-length")
    if declared and declared.isdigit() and int(declared) > limit:
        raise HTTPException(413, f"body larger than {limit} bytes")
    buf = bytearray()
    async for chunk in request.stream():
        buf.extend(chunk)
        if len(buf) > limit:
            raise HTTPException(413, f"body larger than {limit} bytes")
    return bytes(buf)


async def _parse(request: Request) -> tuple[str | None, bytes | None, dict]:
    """-> (text, wav, json body). Exactly one of text/wav is set, unless the JSON has neither."""
    ctype = (request.headers.get("content-type") or "").split(";")[0].strip().lower()
    if ctype in WAV_TYPES:
        return None, await _read_body(request, stt.MAX_AUDIO_BYTES), {}
    if ctype in ("application/json", ""):
        raw = await _read_body(request, MAX_JSON_BYTES)
        if not raw.strip():
            return None, None, {}
        try:
            body = json.loads(raw)
        except ValueError:
            raise HTTPException(422, "body must be JSON") from None
        if not isinstance(body, dict) or set(body) - {"text", "audio", "turn"}:
            raise HTTPException(422, "allowed fields: text, audio, turn")
        text = body.get("text")
        if text is not None and not isinstance(text, str):
            raise HTTPException(422, "text must be a string")
        if "audio" in body and not isinstance(body["audio"], bool):
            raise HTTPException(422, "audio must be a boolean")
        if "turn" in body and body["turn"] is not None and (
                isinstance(body["turn"], bool) or not isinstance(body["turn"], int)):
            raise HTTPException(422, "turn must be an integer")
        return text, None, body
    raise HTTPException(415, "send JSON {\"text\": ...} or audio/wav")


async def _transcript(text: str | None, wav: bytes | None) -> str:
    if wav is not None:
        try:
            stt.inspect_wav(wav)
        except stt.AudioTooLarge:
            raise HTTPException(413, "audio too large") from None
        except stt.InvalidAudio as e:
            raise HTTPException(422, f"invalid audio: {e}") from None
        try:
            text = await run_in_threadpool(stt.transcribe, wav)
        except stt.SpeechUnavailable:
            return None  # caller answers 503
        finally:
            del wav  # never kept
    text = voice_chat.clean_text(text)
    if not text:
        raise HTTPException(422, "empty transcript; nothing was heard")
    return text


def _stt_down() -> JSONResponse:
    return JSONResponse(status_code=503, headers=NO_STORE, content={
        "detail": STT_DOWN, "transcript": None, "intent": None, "reply_text": "Sorry, I could not hear that. "
        "Please try again or type the question.", "speak": False, "audio_available": _speech_on(), "turn": None})


def _facts(sid: str, exercise: str) -> voice_chat.Facts:
    return voice_chat.Facts.from_coaching(port_coach_router._coaching(sid, True), exercise)


async def _turn(sid: str, exercise: str, text: str) -> dict:
    facts = await run_in_threadpool(_facts, sid, exercise)
    out = await run_in_threadpool(voice_chat.converse, sid, facts, text)
    return {"transcript": out["transcript"], "intent": out["intent"], "port": out["port"],
            "reply_text": out["reply_text"], "speak": bool(out["reply_text"]),
            "audio_available": _speech_on(), "turn": out["turn"], "source": out["source"]}


@router.post(
    "/{sid}/coach/talk",
    responses={404: {"description": "Session not found"}, 409: {"description": "Not a placement session"},
               413: {"description": "Audio too large"}, 422: {"description": "Empty or invalid input"},
               429: {"description": "Rate limited"},
               503: {"description": "Speech recognition unavailable; send text instead"}},
    openapi_extra={"requestBody": {"content": {
        "application/json": {"schema": {"type": "object", "properties": {
            "text": {"type": "string", "maxLength": voice_chat.MAX_TEXT_CHARS}, "audio": {"type": "boolean"}}}},
        "audio/wav": {"schema": {"type": "string", "format": "binary"}}}}},
)
async def talk(sid: str, request: Request, audio: bool = False):
    exercise = await run_in_threadpool(_exercise, sid)
    ratelimit.check("coach_talk", sid)
    text, wav, body = await _parse(request)
    if text is None and wav is None:
        raise HTTPException(422, "send {\"text\": ...} or an audio/wav body")
    want_audio = audio or bool(body.get("audio"))
    transcript = await _transcript(text, wav)
    if transcript is None:
        return _stt_down()
    out = await _turn(sid, exercise, transcript)
    if want_audio and out["audio_available"]:
        out["audio_url"] = f"/api/v1/sessions/{sid}/coach/talk/audio"
        out["audio_request"] = {"turn": out["turn"]}
    return JSONResponse(out, headers=NO_STORE)


@router.post(
    "/{sid}/coach/talk/audio",
    response_class=Response,
    responses={200: {"content": {"audio/wav": {"schema": {"type": "string", "format": "binary"}}}},
               404: {"description": "Session or turn not found"}, 409: {"description": "Not a placement session"},
               413: {"description": "Audio too large"}, 422: {"description": "Empty or invalid input"},
               429: {"description": "Rate limited"},
               503: {"description": "STT or TTS unavailable; the JSON body keeps reply_text"}},
)
async def talk_audio(sid: str, request: Request, turn: int | None = None):
    exercise = await run_in_threadpool(_exercise, sid)
    text, wav, body = await _parse(request)
    meta: dict = {}
    if text is None and wav is None:  # speak a stored reply
        reply = voice_chat.reply_for_turn(sid, body.get("turn", turn))
        if reply is None:
            raise HTTPException(404, "no such turn; conversation history is in memory and may have expired")
    else:
        ratelimit.check("coach_talk", sid)
        transcript = await _transcript(text, wav)
        if transcript is None:
            return _stt_down()
        meta = await _turn(sid, exercise, transcript)
        reply = meta["reply_text"]
    ratelimit.check("coach_audio", sid)  # shares the Cartesia budget with /coach/audio and /coach/placement/audio
    try:
        audio_bytes = await run_in_threadpool(cartesia.text_to_audio, reply)
    except (cartesia.SpeechUnavailable, ValueError):
        return JSONResponse(status_code=503, headers=NO_STORE, content={
            "detail": TTS_DOWN, "reply_text": reply, **{k: meta[k] for k in ("transcript", "intent", "turn")
                                                         if k in meta}})
    headers = dict(NO_STORE)
    if meta:  # percent-encoded: headers are latin-1 and the transcript is untrusted
        headers.update({"X-Coach-Turn": str(meta["turn"]), "X-Coach-Intent": meta["intent"],
                        "X-Coach-Reply": quote(reply, safe=" ,.'-:"),
                        "X-Coach-Transcript": quote(meta["transcript"], safe=" ,.'-:?")})
    return Response(audio_bytes, media_type="audio/wav", headers=headers)
