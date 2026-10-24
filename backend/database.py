"""SQLAlchemy models. Uses DATABASE_URL (Tiger Data / Postgres+Timescale in prod), SQLite locally."""
import os
import uuid
from datetime import datetime, timezone

from sqlalchemy import JSON, Boolean, DateTime, Float, Integer, String, UniqueConstraint, create_engine
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column, sessionmaker

DATABASE_URL = os.getenv("DATABASE_URL", "sqlite:///./surgimentor.db")


def make_engine(url: str = DATABASE_URL):
    kwargs = {"connect_args": {"check_same_thread": False}} if url.startswith("sqlite") else {}
    return create_engine(url, **kwargs)


engine = make_engine()
SessionLocal = sessionmaker(bind=engine, expire_on_commit=False)


class Base(DeclarativeBase):
    pass


def _now():
    return datetime.now(timezone.utc)


class TrainingSession(Base):
    __tablename__ = "sessions"
    id: Mapped[str] = mapped_column(String, primary_key=True, default=lambda: uuid.uuid4().hex)
    trainee_id: Mapped[str] = mapped_column(String, index=True)
    exercise: Mapped[str] = mapped_column(String, default="peg_transfer")
    status: Mapped[str] = mapped_column(String, default="ready")
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=_now)
    started_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True)
    completed_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True)
    elapsed_s: Mapped[float | None] = mapped_column(Float, nullable=True)
    metrics: Mapped[dict | None] = mapped_column(JSON, nullable=True)


class SampleRow(Base):
    __tablename__ = "samples"
    __table_args__ = (UniqueConstraint("session_id", "instrument", "idx"),)
    pk: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=True)
    session_id: Mapped[str] = mapped_column(String, index=True)
    instrument: Mapped[str] = mapped_column(String)
    idx: Mapped[int] = mapped_column(Integer)
    t_ms: Mapped[int] = mapped_column(Integer)
    x: Mapped[float] = mapped_column(Float)
    y: Mapped[float] = mapped_column(Float)
    z: Mapped[float] = mapped_column(Float)
    qx: Mapped[float] = mapped_column(Float, default=0.0)
    qy: Mapped[float] = mapped_column(Float, default=0.0)
    qz: Mapped[float] = mapped_column(Float, default=0.0)
    qw: Mapped[float] = mapped_column(Float, default=1.0)
    valid: Mapped[bool] = mapped_column(Boolean, default=True)
    interaction: Mapped[str] = mapped_column(String, default="idle")


class EventRow(Base):
    __tablename__ = "events"
    __table_args__ = (UniqueConstraint("session_id", "event_id"),)
    pk: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=True)
    session_id: Mapped[str] = mapped_column(String, index=True)
    event_id: Mapped[str] = mapped_column(String)
    kind: Mapped[str] = mapped_column(String)
    t_ms: Mapped[int] = mapped_column(Integer)
    instrument: Mapped[str | None] = mapped_column(String, nullable=True)
    object_id: Mapped[str | None] = mapped_column(String, nullable=True)
    source: Mapped[list | None] = mapped_column(JSON, nullable=True)
    target: Mapped[list | None] = mapped_column(JSON, nullable=True)


def init_db(eng=None):
    Base.metadata.create_all(eng or engine)
    # Tiger Data: promote samples to a hypertable when running on Timescale (done in migrations/deploy).
