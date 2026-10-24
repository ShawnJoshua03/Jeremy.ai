"""Headset command poll and acknowledgement.

docs/HANDOFF_MENTORSHIP.md is not in this repo. The JSON matches
quest/Assets/Scripts/Mentorship/CommandPoller.cs and the command example in
docs/ARCHITECTURE.md. Rows stay in SQLite. Convex is not read or written.
"""
from datetime import datetime

from fastapi import APIRouter, HTTPException
from fastapi.responses import JSONResponse
from pydantic import BaseModel, ConfigDict, Field, field_validator
from sqlalchemy import select

import database as db
from mentorship.models import CommandRow

router = APIRouter(prefix="/api/v1/sessions", tags=["mentorship"])

COMMAND_TYPES = (
    "highlight_target",
    "add_annotation",
    "request_pause",
    "request_retry",
    "assign_exercise",
)


def _expiry(value: str) -> str:
    text = value.strip()
    if text.endswith("Z"):
        text = text[:-1] + "+00:00"
    try:
        datetime.fromisoformat(text)
    except ValueError:
        raise ValueError("expiresAt must be an ISO-8601 timestamp") from None
    return value


class CommandIn(BaseModel):
    model_config = ConfigDict(extra="forbid")
    commandId: str = Field(min_length=1, max_length=128, pattern=r"^[A-Za-z0-9][A-Za-z0-9_-]{0,127}$")
    type: str
    targetId: str = Field(default="", max_length=64)
    expiresAt: str
    message: str = Field(default="", max_length=500)
    exerciseId: str = Field(default="", max_length=64)

    @field_validator("type")
    @classmethod
    def known_type(cls, value: str) -> str:
        if value not in COMMAND_TYPES:
            raise ValueError("unsupported command type")
        return value

    @field_validator("expiresAt")
    @classmethod
    def known_expiry(cls, value: str) -> str:
        return _expiry(value)


class AckIn(BaseModel):
    model_config = ConfigDict(extra="forbid")
    status: str
    message: str = Field(default="", max_length=500)

    @field_validator("status")
    @classmethod
    def known_status(cls, value: str) -> str:
        if value not in ("applied", "rejected"):
            raise ValueError("status must be applied or rejected")
        return value


def _require_session(session, sid: str) -> None:
    if session.get(db.TrainingSession, sid) is None:
        raise HTTPException(404, "session not found")


def _body(row: CommandRow) -> dict:
    return {
        "commandId": row.command_id,
        "type": row.type,
        "targetId": row.target_id or "",
        "expiresAt": row.expires_at,
        "message": row.message or "",
        "exerciseId": row.exercise_id or "",
    }


def _same(row: CommandRow, body: CommandIn) -> bool:
    return (
        row.type == body.type
        and (row.target_id or "") == body.targetId
        and row.expires_at == body.expiresAt
        and (row.message or "") == body.message
        and (row.exercise_id or "") == body.exerciseId
    )


def _find(session, sid: str, command_id: str) -> CommandRow | None:
    return session.scalar(
        select(CommandRow).where(CommandRow.session_id == sid, CommandRow.command_id == command_id)
    )


@router.post("/{sid}/commands")
def create_command(sid: str, body: CommandIn):
    with db.SessionLocal() as session:
        _require_session(session, sid)
        existing = _find(session, sid, body.commandId)
        if existing is not None:
            if not _same(existing, body):
                raise HTTPException(409, "command already exists")
            return JSONResponse(_body(existing), status_code=200)
        row = CommandRow(
            session_id=sid,
            command_id=body.commandId,
            type=body.type,
            target_id=body.targetId,
            expires_at=body.expiresAt,
            message=body.message,
            exercise_id=body.exerciseId,
        )
        session.add(row)
        session.commit()
        return JSONResponse(_body(row), status_code=201)


@router.get("/{sid}/commands")
def list_commands(sid: str):
    with db.SessionLocal() as session:
        _require_session(session, sid)
        rows = session.scalars(
            select(CommandRow)
            .where(CommandRow.session_id == sid, CommandRow.ack_status.is_(None))
            .order_by(CommandRow.pk)
            .limit(100)
        ).all()
        return {"commands": [_body(row) for row in rows]}


@router.post("/{sid}/commands/{command_id}/ack")
def acknowledge(sid: str, command_id: str, body: AckIn):
    with db.SessionLocal() as session:
        _require_session(session, sid)
        row = _find(session, sid, command_id)
        if row is None:
            raise HTTPException(404, "command not found")
        if row.ack_status is None:
            row.ack_status = body.status
            row.ack_message = body.message
            session.commit()
        return {"status": row.ack_status, "message": row.ack_message or ""}
