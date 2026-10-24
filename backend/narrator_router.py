"""Polling endpoints for the laparoscopic cholecystectomy robot avatar."""
from datetime import datetime, timezone
from typing import Literal

from fastapi import APIRouter, HTTPException, Response
from fastapi.responses import JSONResponse
from pydantic import BaseModel
from sqlalchemy import select

import database as db
import narrator
import ratelimit
import scoring
from speech import cartesia

router = APIRouter(prefix="/api/v1/sessions", tags=["narrator"])
NO_STORE = {"Cache-Control": "no-store"}


class NarratorLine(BaseModel):
    text: str
    step: str
    expected_progress: float
    actual_progress: float
    delta: float
    severity: Literal["on_track", "slow", "off_track", "critical"]
    repeat: bool


def _progress(sid: str) -> narrator.Progress:
    with db.SessionLocal() as session:
        row = session.get(db.TrainingSession, sid)
        if row is None:
            raise HTTPException(404, "session not found")
        if row.exercise != "lap_chole":
            raise HTTPException(409, "narration is only for laparoscopic cholecystectomy sessions")
        events = [scoring.Event(
            r.kind, r.t_ms, r.instrument, r.object_id,
            tuple(r.source) if r.source is not None else None,
            tuple(r.target) if r.target is not None else None,
            r.value, r.detail, r.event_id,
        ) for r in session.scalars(select(db.EventRow).where(db.EventRow.session_id == sid)
                                  .order_by(db.EventRow.t_ms, db.EventRow.pk))]
        if row.elapsed_s is not None:
            elapsed = row.elapsed_s
        elif row.started_at is not None:
            end = row.completed_at or datetime.now(timezone.utc)
            elapsed = max(0.0, (end - row.started_at).total_seconds())
        else:
            elapsed = 0.0
        # Headset telemetry can be ahead of server wall time after upload/replay.
        elapsed = max(elapsed, max((e.t_ms / 1000 for e in events), default=0.0))
    return narrator.compute_progress(events, elapsed)


def _line(sid: str, audio: bool = False) -> dict:
    progress = _progress(sid)  # Release DB connection before either provider call.
    ratelimit.check("narrator_line", sid)  # Shared across JSON/audio to prevent bypass.
    if audio:
        ratelimit.check("coach_audio", sid)  # Existing shared Cartesia quota.
    return narrator.line(sid, progress)


@router.post("/{sid}/narrator/line", response_model=NarratorLine)
def narrator_line(sid: str, response: Response):
    response.headers.update(NO_STORE)
    return _line(sid)


@router.post("/{sid}/narrator/line/audio", response_class=Response, responses={
    200: {"content": {"audio/wav": {"schema": {"type": "string", "format": "binary"}}}},
    503: {"description": "Speech unavailable; JSON contains the complete text-only narrator line"},
})
def narrator_audio(sid: str):
    out = _line(sid, audio=True)
    try:
        audio = cartesia.text_to_audio(out["text"])
    except cartesia.SpeechUnavailable:
        return JSONResponse(status_code=503, headers=NO_STORE, content={
            "detail": "Speech is unavailable; use text feedback.", **out})
    return Response(audio, media_type="audio/wav", headers=NO_STORE)
