from __future__ import annotations

import math
from datetime import datetime, timezone
from typing import Literal

from fastapi import Depends, FastAPI, HTTPException, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse
from pydantic import BaseModel, Field, field_validator
from sqlalchemy import select
from sqlalchemy.dialects.postgresql import insert as pg_insert
from sqlalchemy.dialects.sqlite import insert as sqlite_insert
from sqlalchemy.orm import Session

from dotenv import load_dotenv

load_dotenv(override=True)  # .env wins over stale shell variables

import coach as coachmod
import convex_mirror
import cost as costmod
import database as db
import scoring

MAX_BATCH = 1000
MAX_COORD_M = 10.0

app = FastAPI(title="SurgiMentor XR API", version="0.1.0")
app.include_router(__import__("speech.router", fromlist=["router"]).router)
app.include_router(__import__("mentorship.router", fromlist=["router"]).router)


@app.exception_handler(RequestValidationError)
async def _validation_error(_: Request, exc: RequestValidationError):
    # Drop echoed input: NaN/Inf there cannot be JSON-encoded and would turn a 422 into a 500.
    errors = [{k: v for k, v in e.items() if k not in ("input", "ctx", "url")} for e in exc.errors()]
    return JSONResponse(status_code=422, content={"detail": jsonable_errors(errors)})


def jsonable_errors(errors):
    return [{k: (v if isinstance(v, (str, int, float, bool, list, type(None))) and v == v else str(v))
             for k, v in e.items()} for e in errors]


@app.on_event("startup")
def _startup():
    db.init_db()


def get_db():
    s = db.SessionLocal()
    try:
        yield s
    finally:
        s.close()


# ---------- schemas ----------
Vec3 = tuple[float, float, float]


class SessionCreate(BaseModel):
    trainee_id: str = Field(min_length=1, max_length=64)
    exercise: Literal["peg_transfer"] = "peg_transfer"


class SampleIn(BaseModel):
    instrument: Literal["left", "right"]
    idx: int = Field(ge=0)
    t_ms: int = Field(ge=0)
    pos: Vec3
    quat: tuple[float, float, float, float] = (0, 0, 0, 1)
    valid: bool = True
    interaction: str = "idle"


class SampleBatch(BaseModel):
    samples: list[SampleIn] = Field(max_length=MAX_BATCH)


class EventIn(BaseModel):
    event_id: str = Field(min_length=1, max_length=64)
    kind: Literal["transfer_success", "drop", "invalid_release"]
    t_ms: int = Field(ge=0)
    instrument: Literal["left", "right"] | None = None
    object_id: str | None = None
    source: Vec3 | None = None
    target: Vec3 | None = None

    @field_validator("source", "target")
    @classmethod
    def _finite_in_range(cls, v):
        if v is not None and not all(math.isfinite(c) and abs(c) <= MAX_COORD_M for c in v):
            raise ValueError("position out of range or non-finite")
        return v


class EventBatch(BaseModel):
    events: list[EventIn] = Field(max_length=MAX_BATCH)


class CompleteIn(BaseModel):
    elapsed_s: float = Field(ge=0, le=3600)


class SessionOut(BaseModel):
    id: str
    trainee_id: str
    exercise: str
    status: str
    created_at: datetime
    started_at: datetime | None = None
    completed_at: datetime | None = None
    elapsed_s: float | None = None


class MetricsOut(BaseModel):
    completion_pct: float
    successful_transfers: int
    drops: int
    invalid_releases: int
    elapsed_s: float
    distance_by_instrument: dict[str, float]
    total_distance_m: float
    path_efficiency: float
    practice_score: float


def _get(s: Session, sid: str) -> db.TrainingSession:
    row = s.get(db.TrainingSession, sid)
    if not row:
        raise HTTPException(404, "session not found")
    return row


def _out(row: db.TrainingSession) -> SessionOut:
    return SessionOut.model_validate(row, from_attributes=True)


def _insert_new(s: Session, table, rows: list[dict], index_elements: list[str]) -> int:
    if not rows:
        return 0
    dialect = s.get_bind().dialect.name
    if dialect == "postgresql":
        stmt = pg_insert(table)
    elif dialect == "sqlite":
        stmt = sqlite_insert(table)
    else:
        raise RuntimeError(f"unsupported database dialect {dialect}")
    result = s.execute(stmt.values(rows).on_conflict_do_nothing(index_elements=index_elements))
    if result.rowcount is None or result.rowcount < 0:
        raise RuntimeError("insert did not report how many rows were stored")
    return result.rowcount


def _compute(s: Session, row: db.TrainingSession, elapsed_s: float) -> scoring.Metrics:
    samples = [
        scoring.Sample(r.instrument, r.idx, r.t_ms, (r.x, r.y, r.z), r.valid)
        for r in s.scalars(select(db.SampleRow).where(db.SampleRow.session_id == row.id))
    ]
    events = [
        scoring.Event(
            r.kind, r.t_ms, r.instrument, r.object_id,
            tuple(r.source) if r.source else None, tuple(r.target) if r.target else None,
        )
        for r in s.scalars(select(db.EventRow).where(db.EventRow.session_id == row.id))
    ]
    return scoring.compute_metrics(samples, events, elapsed_s)


# ---------- routes ----------
@app.get("/health")
def health():
    return {"status": "ok"}


@app.post("/api/v1/sessions", response_model=SessionOut, status_code=201)
def create_session(body: SessionCreate, s: Session = Depends(get_db)):
    row = db.TrainingSession(trainee_id=body.trainee_id, exercise=body.exercise)
    s.add(row)
    s.commit()
    return _out(row)


@app.post("/api/v1/sessions/{sid}/start", response_model=SessionOut)
def start_session(sid: str, s: Session = Depends(get_db)):
    row = _get(s, sid)
    if row.status == "running":
        return _out(row)  # timer begins only once
    if row.status != "ready":
        raise HTTPException(409, f"cannot start from {row.status}")
    row.status = "running"
    row.started_at = datetime.now(timezone.utc)
    s.commit()
    return _out(row)


@app.post("/api/v1/sessions/{sid}/samples")
def add_samples(sid: str, body: SampleBatch, s: Session = Depends(get_db)):
    row = _get(s, sid)
    if row.status not in ("running", "ready"):
        raise HTTPException(409, "session not accepting samples")
    for smp in body.samples:
        if not all(math.isfinite(v) and abs(v) <= MAX_COORD_M for v in smp.pos):
            raise HTTPException(422, "position out of range or non-finite")
    seen: set[tuple[str, int]] = set()
    rows = []
    for smp in body.samples:
        key = (smp.instrument, smp.idx)
        if key in seen:
            continue
        seen.add(key)
        rows.append({
            "session_id": sid, "instrument": smp.instrument, "idx": smp.idx, "t_ms": smp.t_ms,
            "x": smp.pos[0], "y": smp.pos[1], "z": smp.pos[2],
            "qx": smp.quat[0], "qy": smp.quat[1], "qz": smp.quat[2], "qw": smp.quat[3],
            "valid": smp.valid, "interaction": smp.interaction,
        })
    added = _insert_new(s, db.SampleRow, rows, ["session_id", "instrument", "idx"])
    s.commit()
    return {"accepted": added, "duplicates": len(body.samples) - added}


@app.get("/api/v1/sessions/{sid}/samples")
def list_samples(sid: str, s: Session = Depends(get_db)):
    _get(s, sid)
    rows = s.scalars(
        select(db.SampleRow).where(db.SampleRow.session_id == sid).order_by(db.SampleRow.t_ms, db.SampleRow.idx)
    )
    return [
        {"instrument": r.instrument, "idx": r.idx, "t_ms": r.t_ms, "pos": [r.x, r.y, r.z],
         "valid": r.valid, "interaction": r.interaction}
        for r in rows
    ]


@app.post("/api/v1/sessions/{sid}/events")
def add_events(sid: str, body: EventBatch, s: Session = Depends(get_db)):
    row = _get(s, sid)
    if row.status == "completed":
        raise HTTPException(409, "session already completed")
    seen: set[str] = set()
    rows = []
    for e in body.events:
        if e.event_id in seen:
            continue
        seen.add(e.event_id)
        rows.append({
            "session_id": sid, "event_id": e.event_id, "kind": e.kind, "t_ms": e.t_ms,
            "instrument": e.instrument, "object_id": e.object_id,
            "source": list(e.source) if e.source else None,
            "target": list(e.target) if e.target else None,
        })
    added = _insert_new(s, db.EventRow, rows, ["session_id", "event_id"])
    s.commit()
    if added:
        convex_mirror.mirror_events(row, rows)
    return {"accepted": added, "duplicates": len(body.events) - added}


@app.post("/api/v1/sessions/{sid}/complete", response_model=MetricsOut)
def complete_session(sid: str, body: CompleteIn, s: Session = Depends(get_db)):
    row = _get(s, sid)
    if row.status == "completed":
        return MetricsOut(**row.metrics)  # idempotent
    if row.status != "running":
        raise HTTPException(409, f"cannot complete from {row.status}")
    m = _compute(s, row, body.elapsed_s)
    row.status = "completed"
    row.completed_at = datetime.now(timezone.utc)
    row.elapsed_s = body.elapsed_s
    row.metrics = m.__dict__
    s.commit()
    return MetricsOut(**row.metrics)


@app.get("/api/v1/sessions/{sid}", response_model=SessionOut)
def get_session(sid: str, s: Session = Depends(get_db)):
    return _out(_get(s, sid))


@app.get("/api/v1/sessions/{sid}/metrics", response_model=MetricsOut)
def get_metrics(sid: str, s: Session = Depends(get_db)):
    row = _get(s, sid)
    if row.metrics:
        return MetricsOut(**row.metrics)
    if row.status == "running":
        return MetricsOut(**_compute(s, row, 0.0).__dict__)  # live preview
    raise HTTPException(409, "no metrics yet")


@app.get("/api/v1/trainees/{trainee_id}/progress")
def trainee_progress(trainee_id: str, s: Session = Depends(get_db)):
    rows = s.scalars(
        select(db.TrainingSession)
        .where(db.TrainingSession.trainee_id == trainee_id, db.TrainingSession.status == "completed")
        .order_by(db.TrainingSession.completed_at)
    )
    return [
        {"session_id": r.id, "completed_at": r.completed_at, "practice_score": r.metrics["practice_score"],
         "elapsed_s": r.elapsed_s, "path_efficiency": r.metrics["path_efficiency"]}
        for r in rows
    ]


@app.get("/api/v1/sessions/{sid}/cost")
def get_cost(
    sid: str,
    setting: Literal["inpatient", "ambulatory", "cross_study_mean"] = "inpatient",
    instruments: Literal["reusable", "disposable"] = "reusable",
    risk_per_error: float = costmod.ASSUMED_EVENT_RISK_PER_ERROR,
    s: Session = Depends(get_db),
):
    row = _get(s, sid)
    if not row.metrics:
        raise HTTPException(409, "session not completed")
    m = row.metrics
    errors = m["drops"] + m["invalid_releases"]
    return costmod.estimate_cost(row.elapsed_s or 0, errors, m["path_efficiency"], setting, instruments, risk_per_error)


@app.post("/api/v1/sessions/{sid}/coach", response_model=coachmod.Coaching)
def get_coaching(sid: str, s: Session = Depends(get_db)):
    row = _get(s, sid)
    if not row.metrics:
        raise HTTPException(409, "session not completed")
    errors = [
        {"event_id": r.event_id, "kind": r.kind, "t_ms": r.t_ms, "instrument": r.instrument}
        for r in s.scalars(
            select(db.EventRow)
            .where(db.EventRow.session_id == sid, db.EventRow.kind != "transfer_success")
            .order_by(db.EventRow.t_ms)
        )
    ]
    return coachmod.generate(row.metrics, errors)
