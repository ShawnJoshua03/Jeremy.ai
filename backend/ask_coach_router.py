"""POST /api/v1/sessions/{sid}/coach/ask: one round trip from a spoken question to a spoken answer.

Body (JSON): {"audio_wav_b64": "<16-bit PCM WAV, base64>"} or {"text": "..."}, plus "context" (the live sim
snapshot, see ask_coach.SimContext) and "speak" (default true).
200: {"transcript", "reply", "intent", "source", "audio_wav_b64" (null when speech is unavailable)}.
422: nothing usable was heard or typed. 503: speech recognition unavailable for an audio question.
"""
from __future__ import annotations

import base64
import binascii

import database as db
import ratelimit
from fastapi import APIRouter, HTTPException

import ask_coach
import provider_log
from speech import cartesia, stt

router = APIRouter(prefix="/api/v1/sessions", tags=["coach"])


@router.post("/{sid}/coach/ask")
def ask(sid: str, body: ask_coach.AskBody):
    with db.SessionLocal() as session:
        if session.get(db.TrainingSession, sid) is None:
            raise HTTPException(404, "session not found")
    ratelimit.check("coach_ask", sid)
    question = (body.text or "").strip()
    if not question and body.audio_wav_b64:
        try:
            wav = base64.b64decode(body.audio_wav_b64, validate=True)
        except (binascii.Error, ValueError):
            raise HTTPException(422, "audio_wav_b64 is not valid base64") from None
        try:
            question = stt.transcribe(wav)
        except stt.InvalidAudio as e:
            raise HTTPException(422, str(e) or "invalid audio") from None
        except stt.SpeechUnavailable:
            raise HTTPException(503, "Speech recognition is unavailable; try again.") from None
    if not question:
        raise HTTPException(422, "I didn't catch that.")
    if body.hands_free and not ask_coach.addressed_to_coach(question):
        return {"transcript": question, "reply": "", "intent": "ignored", "source": "filter", "audio_wav_b64": None}
    if body.audio_wav_b64 and not (body.text or "").strip():
        # Only questions meant for the coach pop the crew; room chatter the filter drops does not.
        provider_log.record(sid, "cartesia", "heard your question", "used")
    reply, source = ask_coach.answer(question, body.context)
    if source == "claude":
        provider_log.record(sid, "claude", "answered your question", "used")
    audio = None
    if body.speak:
        try:
            # 16 kHz speech is a quarter of the CD-rate size: it reaches the headset over Wi-Fi in a fraction of the time.
            audio = base64.b64encode(cartesia.text_to_audio(reply[: cartesia.MAX_TEXT_LENGTH], sample_rate=16000)).decode("ascii")
            provider_log.record(sid, "cartesia", "spoke the answer", "used")
        except (cartesia.SpeechUnavailable, ValueError):
            audio = None
    return {"transcript": question, "reply": reply, "intent": ask_coach.intent_of(question), "source": source,
            "audio_wav_b64": audio}
