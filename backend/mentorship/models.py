"""SQLite command and annotation rows. Quest reads commands from here."""
from datetime import UTC, datetime

import database as db
from sqlalchemy import DateTime, Integer, String, UniqueConstraint
from sqlalchemy.orm import Mapped, mapped_column


def _now():
    return datetime.now(UTC)


class CommandRow(db.Base):
    __tablename__ = "commands"
    __table_args__ = (UniqueConstraint("session_id", "command_id"),)
    pk: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=True)
    session_id: Mapped[str] = mapped_column(String, index=True)
    command_id: Mapped[str] = mapped_column(String)
    type: Mapped[str] = mapped_column(String)
    target_id: Mapped[str | None] = mapped_column(String, nullable=True)
    expires_at: Mapped[str] = mapped_column(String)
    message: Mapped[str | None] = mapped_column(String, nullable=True)
    exercise_id: Mapped[str | None] = mapped_column(String, nullable=True)
    ack_status: Mapped[str | None] = mapped_column(String, nullable=True)
    ack_message: Mapped[str | None] = mapped_column(String, nullable=True)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=_now)


class AnnotationRow(db.Base):
    __tablename__ = "annotations"
    pk: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=True)
    annotation_id: Mapped[str] = mapped_column(String, unique=True)
    session_id: Mapped[str] = mapped_column(String, index=True)
    at_ms: Mapped[int] = mapped_column(Integer)
    text: Mapped[str] = mapped_column(String)
    author: Mapped[str] = mapped_column(String)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=_now)
