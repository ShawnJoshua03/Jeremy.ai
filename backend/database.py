"""SQLAlchemy models. SQLite locally. Postgres when DATABASE_URL says so."""
import os
import uuid
from datetime import datetime, timezone

from sqlalchemy import (
    JSON, Boolean, DateTime, Float, Index, Integer, String, UniqueConstraint, create_engine, inspect, text,
)
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column, sessionmaker


def normalize_database_url(url: str) -> str:
    """Use the psycopg v3 driver for a URL that does not name one."""
    if url.startswith("postgres://"):
        return "postgresql+psycopg://" + url[len("postgres://"):]
    if url.startswith("postgresql://"):
        return "postgresql+psycopg://" + url[len("postgresql://"):]
    return url


DATABASE_URL = normalize_database_url(os.getenv("DATABASE_URL", "sqlite:///./surgimentor.db"))


def make_engine(url: str = DATABASE_URL):
    url = normalize_database_url(url)
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
    client_request_id: Mapped[str | None] = mapped_column(String(64), nullable=True)


Index("ix_sessions_client_request_id", TrainingSession.client_request_id, unique=True)


class SampleRow(Base):
    __tablename__ = "samples"
    __table_args__ = (
        UniqueConstraint("session_id", "instrument", "idx"),
        Index("ix_samples_session_t_ms", "session_id", "t_ms"),
    )
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


def _ensure_index(bind, table, index_name: str):
    for index in table.indexes:
        if index.name == index_name:
            index.create(bind=bind, checkfirst=True)


def init_db(eng=None):
    target = eng or engine
    Base.metadata.create_all(target)
    # create_all leaves an already-created table alone, so add columns and indexes when they are missing.
    if inspect(target).has_table("sessions"):
        columns = {col["name"] for col in inspect(target).get_columns("sessions")}
        if "client_request_id" not in columns:
            with target.begin() as conn:
                conn.execute(text("ALTER TABLE sessions ADD COLUMN client_request_id VARCHAR(64)"))
    _ensure_index(target, TrainingSession.__table__, "ix_sessions_client_request_id")
    _ensure_index(target, SampleRow.__table__, "ix_samples_session_t_ms")
