"""Optional audio for text already displayed by a coaching client."""
import database as db
from fastapi import APIRouter, HTTPException, Response
from pydantic import BaseModel, ConfigDict, Field, field_validator

from . import cartesia

router = APIRouter(prefix="/api/v1/sessions", tags=["speech"])


class AudioRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")
    text: str = Field(min_length=1, max_length=cartesia.MAX_TEXT_LENGTH)

    @field_validator("text")
    @classmethod
    def nonblank(cls, value: str) -> str:
        if not value.strip():
            raise ValueError("text must not be blank")
        return value


@router.post(
    "/{sid}/coach/audio",
    response_class=Response,
    responses={
        200: {"content": {"audio/wav": {"schema": {"type": "string", "format": "binary"}}}},
        404: {"description": "Session not found"},
        503: {"description": "Speech unavailable; keep displaying text feedback"},
    },
)
def coach_audio(sid: str, body: AudioRequest):
    # Close the database connection before the slow provider call.
    with db.SessionLocal() as session:
        if session.get(db.TrainingSession, sid) is None:
            raise HTTPException(404, "session not found")
    try:
        audio = cartesia.text_to_audio(body.text)
    except cartesia.SpeechUnavailable:
        raise HTTPException(
            503, "Speech is unavailable; use text feedback.",
            headers={"Cache-Control": "no-store"},
        ) from None
    return Response(audio, media_type="audio/wav", headers={"Cache-Control": "no-store"})
