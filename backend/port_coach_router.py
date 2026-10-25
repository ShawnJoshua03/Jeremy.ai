"""Spoken port-placement coaching for lap chole sessions (port_coach.py).

POST /api/v1/sessions/{sid}/coach/placement        -> JSON cues + debrief, from stored events. No paid call.
POST /api/v1/sessions/{sid}/coach/placement/audio  -> WAV of one cue via Cartesia; 503 means show the text.
"""
from __future__ import annotations

import os
from typing import Literal

import database as db
import port_coach
import ratelimit
from fastapi import APIRouter, HTTPException, Response
from pydantic import BaseModel, ConfigDict
from sqlalchemy import select
from speech import cartesia

COACHED_EXERCISES = {"lap_chole", "port_placement"}
router = APIRouter(prefix="/api/v1/sessions", tags=["coaching"])


class PlacementIn(BaseModel):
    model_config = ConfigDict(extra="forbid")
    compare_previous_session: bool = True


class PlacementAudioIn(BaseModel):
    model_config = ConfigDict(extra="forbid")
    # "latest" = cue for the most recent placement, "debrief" = the summary, or a cue index.
    cue: Literal["latest", "debrief"] | int = "latest"
    compare_previous_session: bool = True


def _events(s, sid: str) -> list[dict]:
    rows = s.scalars(select(db.EventRow)
                     .where(db.EventRow.session_id == sid, db.EventRow.kind == "port_placed")
                     .order_by(db.EventRow.t_ms, db.EventRow.pk))
    return [{"event_id": r.event_id, "kind": r.kind, "t_ms": r.t_ms, "object_id": r.object_id,
             "source": tuple(r.source) if r.source else None, "target": tuple(r.target) if r.target else None,
             "value": r.value, "detail": r.detail} for r in rows]


def _previous_session_events(s, row: db.TrainingSession) -> list[dict]:
    """Port events of this trainee's most recent earlier session of the same exercise that placed any port."""
    earlier = s.scalars(select(db.TrainingSession)
                        .where(db.TrainingSession.trainee_id == row.trainee_id,
                               db.TrainingSession.exercise == row.exercise,
                               db.TrainingSession.difficulty == row.difficulty,
                               db.TrainingSession.id != row.id,
                               db.TrainingSession.created_at <= row.created_at)
                        .order_by(db.TrainingSession.created_at.desc())
                        .limit(5))
    for prev in earlier:
        events = _events(s, prev.id)
        if events:
            return events
    return []


def _coaching(sid: str, compare_previous: bool) -> dict:
    with db.SessionLocal() as s:
        row = s.get(db.TrainingSession, sid)
        if row is None:
            raise HTTPException(404, "session not found")
        if row.exercise not in COACHED_EXERCISES:
            raise HTTPException(409, "placement coaching is only for lap_chole and port_placement sessions")
        previous = _previous_session_events(s, row) if compare_previous else []
        return port_coach.coach_placements(_events(s, sid), previous, difficulty=row.difficulty)


@router.post("/{sid}/coach/placement")
def placement_coaching(sid: str, body: PlacementIn | None = None):
    out = _coaching(sid, body.compare_previous_session if body else True)
    # Clients speak via /coach/placement/audio when this is true; otherwise they show the text only.
    out["speech_available"] = bool(os.getenv("CARTESIA_API_KEY", "").strip())
    out["source"] = "template"  # no LLM; every number comes from port_coach.py
    return out


@router.post(
    "/{sid}/coach/placement/audio",
    response_class=Response,
    responses={
        200: {"content": {"audio/wav": {"schema": {"type": "string", "format": "binary"}}}},
        404: {"description": "Session or cue not found"},
        409: {"description": "Not a lap_chole or port_placement session"},
        429: {"description": "Too many audio requests for this session"},
        503: {"description": "Speech unavailable; keep displaying text feedback"},
    },
)
def placement_audio(sid: str, body: PlacementAudioIn | None = None):
    body = body or PlacementAudioIn()
    out = _coaching(sid, body.compare_previous_session)  # DB closed before the slow provider call
    if body.cue == "debrief":
        text = out["debrief"]["text"]
    elif not out["cues"]:
        raise HTTPException(404, "no port placements yet")
    elif body.cue == "latest":
        text = out["cues"][-1]["text"]
    elif 0 <= body.cue < len(out["cues"]):
        text = out["cues"][body.cue]["text"]
    else:
        raise HTTPException(404, "cue not found")
    ratelimit.check("coach_audio", sid)  # shares the Cartesia budget with /coach/audio
    try:
        audio = cartesia.text_to_audio(text)
    except cartesia.SpeechUnavailable:
        raise HTTPException(
            503, "Speech is unavailable; use text feedback.",
            headers={"Cache-Control": "no-store"},
        ) from None
    return Response(audio, media_type="audio/wav", headers={"Cache-Control": "no-store"})
