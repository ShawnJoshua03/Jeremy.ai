from __future__ import annotations

import hmac
import math
import os
import re
from datetime import datetime, timezone
from typing import Literal

from fastapi import Depends, FastAPI, HTTPException, Query, Request
from fastapi.exceptions import RequestValidationError
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse
from starlette.types import ASGIApp, Receive, Scope, Send
from pydantic import BaseModel, Field, field_validator
from sqlalchemy import select
from sqlalchemy.dialects.postgresql import insert as pg_insert
from sqlalchemy.dialects.sqlite import insert as sqlite_insert
from sqlalchemy.exc import IntegrityError
from sqlalchemy.orm import Session

from dotenv import load_dotenv

load_dotenv(override=True)  # .env wins over stale shell variables

import assessment as assessmentmod
import coach as coachmod
import convex_mirror
import cost as costmod
import ratelimit
import database as db
import scoring

MAX_BATCH = 1000
MAX_COORD_M = 10.0
_CLIENT_REQUEST_ID = re.compile(r"^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$")

app = FastAPI(title="SurgiMentor XR API", version="0.1.0")
app.include_router(__import__("speech.router", fromlist=["router"]).router)
app.include_router(__import__("mentorship.router", fromlist=["router"]).router)

_DEFAULT_CORS_ORIGINS = "http://localhost:5173"
_cors_by_env: dict[tuple[str, int], CORSMiddleware] = {}


def _cors_origins(raw: str | None) -> tuple[str, list[str]]:
    text = raw if raw and raw.strip() else _DEFAULT_CORS_ORIGINS
    origins = [part.strip() for part in text.split(",") if part.strip()]
    return text, origins


def _header(scope: Scope, name: bytes) -> str:
    for key, value in scope.get("headers") or []:
        if key == name:
            return value.decode("latin-1")
    return ""


class ApiKeyGate:
    """Require the key on /api/v1 when API_KEY is set, as X-API-Key or Authorization: Bearer.

    The env is read per request. Quest and the dashboard proxy send X-API-Key.
    """

    def __init__(self, app: ASGIApp):
        self.app = app

    async def __call__(self, scope: Scope, receive: Receive, send: Send) -> None:
        if scope["type"] != "http":
            await self.app(scope, receive, send)
            return
        path = scope.get("path") or ""
        method = scope.get("method") or ""
        expected = os.environ.get("API_KEY", "")
        if (
            method == "OPTIONS"
            or (method == "GET" and path == "/health")
            or not path.startswith("/api/v1")
            or not expected.strip()
        ):
            await self.app(scope, receive, send)
            return
        bearer = _header(scope, b"authorization")
        bearer = bearer[7:] if bearer[:7].lower() == "bearer " else ""
        supplied = _header(scope, b"x-api-key") or bearer
        if not hmac.compare_digest(supplied.encode(), expected.encode()):
            await JSONResponse({"detail": "invalid api key"}, status_code=401)(scope, receive, send)
            return
        await self.app(scope, receive, send)


class OriginEnvCORS:
    """Read CORS_ORIGINS on each request so tests can change it after import."""

    def __init__(self, app: ASGIApp):
        self.app = app

    async def __call__(self, scope: Scope, receive: Receive, send: Send) -> None:
        if scope["type"] != "http":
            await self.app(scope, receive, send)
            return
        raw, origins = _cors_origins(os.environ.get("CORS_ORIGINS"))
        key = (raw, id(self.app))
        cors = _cors_by_env.get(key)
        if cors is None:
            cors = CORSMiddleware(
                ApiKeyGate(self.app),
                allow_origins=origins,
                allow_methods=["*"],
                allow_headers=["*"],
                allow_credentials=False,
            )
            _cors_by_env[key] = cors
        await cors(scope, receive, send)


app.add_middleware(OriginEnvCORS)


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
    exercise: Literal["peg_transfer", "wound_closure", "lap_chole", "port_placement"] = "peg_transfer"
    client_request_id: str | None = Field(default=None, max_length=64)

    @field_validator("client_request_id")
    @classmethod
    def _normalize_client_request_id(cls, value: str | None) -> str | None:
        if value is None:
            return None
        value = value.strip()
        if not value:
            return None
        if _CLIENT_REQUEST_ID.fullmatch(value) is None:
            raise ValueError("invalid client_request_id")
        return value


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
    kind: Literal[
        "transfer_success", "drop", "invalid_release",
        "stitch_placed", "stitch_missed", "tissue_contact",
        "step_completed", "port_placed", "insufflation", "critical_view", "clip_applied", "structure_cut", "injury",
        "landmark",
    ]
    t_ms: int = Field(ge=0)
    instrument: Literal["left", "right"] | None = None
    object_id: str | None = None
    source: Vec3 | None = None
    target: Vec3 | None = None
    value: float | None = None  # lap_chole: seconds, degrees, mmHg, percent or mm (docs/LAP_CHOLE.md)
    detail: str | None = Field(default=None, max_length=64)

    @field_validator("source", "target")
    @classmethod
    def _finite_in_range(cls, v):
        if v is not None and not all(math.isfinite(c) and abs(c) <= MAX_COORD_M for c in v):
            raise ValueError("position out of range or non-finite")
        return v

    @field_validator("value")
    @classmethod
    def _finite_value(cls, v):
        if v is not None and not math.isfinite(v):
            raise ValueError("value must be finite")
        return v


class EventBatch(BaseModel):
    events: list[EventIn] = Field(max_length=MAX_BATCH)


class CompleteIn(BaseModel):
    elapsed_s: float = Field(ge=0, le=3600)


class CoachIn(BaseModel):
    question: str | None = Field(default=None, max_length=coachmod.MAX_QUESTION_CHARS)

    @field_validator("question", mode="before")
    @classmethod
    def _strip_question(cls, value):
        if isinstance(value, str):
            value = value.strip()
            return value or None
        return value


class SessionOut(BaseModel):
    id: str
    trainee_id: str
    exercise: str
    status: str
    created_at: datetime
    started_at: datetime | None = None
    completed_at: datetime | None = None
    elapsed_s: float | None = None
    metrics: MetricsOut | None = None  # stored once the session is completed


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
    # wound closure only; null for peg transfer (docs/WOUND_CLOSURE.md)
    exercise: str | None = None
    mean_bite_error_mm: float | None = None
    spacing_sd_mm: float | None = None
    # lap_chole only; null for the other exercises (docs/LAP_CHOLE.md)
    details: dict[str, bool | int | float | str] | None = None


SessionOut.model_rebuild()


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


def _scoring_events(s: Session, sid: str) -> list[scoring.Event]:
    return [
        scoring.Event(
            r.kind, r.t_ms, r.instrument, r.object_id,
            tuple(r.source) if r.source else None, tuple(r.target) if r.target else None,
            r.value, r.detail, r.event_id,
        )
        for r in s.scalars(select(db.EventRow).where(db.EventRow.session_id == sid).order_by(db.EventRow.t_ms, db.EventRow.pk))
    ]


def _compute(s: Session, row: db.TrainingSession, elapsed_s: float) -> scoring.Metrics:
    samples = [
        scoring.Sample(r.instrument, r.idx, r.t_ms, (r.x, r.y, r.z), r.valid)
        for r in s.scalars(select(db.SampleRow).where(db.SampleRow.session_id == row.id))
    ]
    return scoring.compute_for_exercise(row.exercise, samples, _scoring_events(s, row.id), elapsed_s)


# ---------- routes ----------
@app.get("/health")
def health():
    return {"status": "ok"}


def _session_for_request(s: Session, request_id: str) -> db.TrainingSession | None:
    return s.scalar(select(db.TrainingSession).where(db.TrainingSession.client_request_id == request_id))


def _same_create(row: db.TrainingSession, body: SessionCreate) -> bool:
    return row.trainee_id == body.trainee_id and row.exercise == body.exercise


def _existing_session(row: db.TrainingSession):
    return JSONResponse(_out(row).model_dump(mode="json"), status_code=200)


@app.post("/api/v1/sessions", response_model=SessionOut, status_code=201)
def create_session(body: SessionCreate, s: Session = Depends(get_db)):
    if body.client_request_id:
        existing = _session_for_request(s, body.client_request_id)
        if existing is not None:
            if not _same_create(existing, body):
                raise HTTPException(409, "client_request_id already exists")
            return _existing_session(existing)
    row = db.TrainingSession(
        trainee_id=body.trainee_id,
        exercise=body.exercise,
        client_request_id=body.client_request_id,
    )
    s.add(row)
    try:
        s.commit()
        convex_mirror.mirror_session(row)
    except IntegrityError:
        s.rollback()
        if not body.client_request_id:
            raise
        existing = _session_for_request(s, body.client_request_id)
        if existing is None or not _same_create(existing, body):
            raise HTTPException(409, "client_request_id already exists")
        return _existing_session(existing)
    return _out(row)


@app.get("/api/v1/sessions", response_model=list[SessionOut])
def list_sessions(
    trainee_id: str | None = None,
    status: str | None = None,
    limit: int = Query(default=50, ge=1, le=100),
    s: Session = Depends(get_db),
):
    if trainee_id is not None and not trainee_id.strip():
        trainee_id = None
    if status is not None and not status.strip():
        status = None
    if status is not None and status not in ("ready", "running", "completed", "cancelled"):
        raise HTTPException(422, "status must be ready, running, completed, or cancelled")
    stmt = select(db.TrainingSession)
    if trainee_id:
        stmt = stmt.where(db.TrainingSession.trainee_id == trainee_id)
    if status:
        stmt = stmt.where(db.TrainingSession.status == status)
    stmt = stmt.order_by(db.TrainingSession.created_at.desc(), db.TrainingSession.id.desc()).limit(limit)
    return [_out(row) for row in s.scalars(stmt)]


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
    convex_mirror.mirror_session(row)
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
    if row.status in ("completed", "cancelled"):
        raise HTTPException(409, f"session already {row.status}")
    allowed = scoring.EVENT_KINDS.get(row.exercise, scoring.PEG_TRANSFER_KINDS)
    wrong = sorted({e.kind for e in body.events} - allowed)
    if wrong:
        raise HTTPException(422, f"event kind {', '.join(wrong)} not valid for exercise {row.exercise}")
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
            "value": e.value, "detail": e.detail,
        })
    added = _insert_new(s, db.EventRow, rows, ["session_id", "event_id"])
    s.commit()
    if added:
        convex_mirror.mirror_events(row, rows)
    return {"accepted": added, "duplicates": len(body.events) - added}


@app.get("/api/v1/sessions/{sid}/events")
def list_events(sid: str, s: Session = Depends(get_db)):
    _get(s, sid)
    rows = s.scalars(
        select(db.EventRow).where(db.EventRow.session_id == sid).order_by(db.EventRow.t_ms, db.EventRow.pk)
    )
    return [
        {
            "event_id": r.event_id, "kind": r.kind, "t_ms": r.t_ms,
            "instrument": r.instrument, "object_id": r.object_id,
            "source": r.source, "target": r.target, "value": r.value, "detail": r.detail,
        }
        for r in rows
    ]


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
    convex_mirror.mirror_session(row)
    return MetricsOut(**row.metrics)


@app.post("/api/v1/sessions/{sid}/cancel", response_model=SessionOut)
def cancel_session(sid: str, s: Session = Depends(get_db)):
    """Close an attempt the trainee abandoned (reset, retry command, app restart). Idempotent."""
    row = _get(s, sid)
    if row.status == "cancelled":
        return _out(row)
    if row.status == "completed":
        raise HTTPException(409, "cannot cancel a completed session")
    row.status = "cancelled"
    row.completed_at = datetime.now(timezone.utc)
    s.commit()
    convex_mirror.mirror_session(row)
    return _out(row)


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


def _cost(row: db.TrainingSession, setting: str = "inpatient", instruments: str = "reusable",
          risk_per_error: float = costmod.ASSUMED_EVENT_RISK_PER_ERROR) -> costmod.CostBreakdown:
    """Lap chole and port placement price only major errors as adverse events, plus an assumed bile duct
    injury line for lap chole. A port a few mm off its site is not an adverse event."""
    m = row.metrics
    errors = m["drops"] + m["invalid_releases"]
    details = m.get("details") or {}
    if row.exercise in ("lap_chole", "port_placement"):
        errors = m["drops"]
    return costmod.estimate_cost(row.elapsed_s or 0, errors, m["path_efficiency"], setting, instruments,
                                 risk_per_error, exercise=row.exercise,
                                 bile_duct_injury=bool(details.get("bile_duct_injury")))


@app.get("/api/v1/trainees/{trainee_id}/progress")
def trainee_progress(trainee_id: str, s: Session = Depends(get_db)):
    rows = s.scalars(
        select(db.TrainingSession)
        .where(db.TrainingSession.trainee_id == trainee_id, db.TrainingSession.status == "completed")
        .order_by(db.TrainingSession.completed_at)
    )
    out = []
    for r in rows:
        m = r.metrics
        errors = m["drops"] + m["invalid_releases"]
        cost = _cost(r)  # default rates, as GET /cost
        out.append({
            "session_id": r.id, "completed_at": r.completed_at, "practice_score": m["practice_score"],
            "elapsed_s": r.elapsed_s, "path_efficiency": m["path_efficiency"],
            "successful_transfers": m["successful_transfers"], "errors": errors, "exercise": r.exercise,
            "cost_total_usd": cost.total_usd,
        })
    return out


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
    return _cost(row, setting, instruments, risk_per_error)


@app.post("/api/v1/sessions/{sid}/coach", response_model=coachmod.Coaching)
def get_coaching(sid: str, body: CoachIn | None = None, s: Session = Depends(get_db)):
    row = _get(s, sid)
    if not row.metrics:
        raise HTTPException(409, "session not completed")
    ratelimit.check("coach", sid)
    if row.exercise == "lap_chole":
        errors = coachmod.lap_chole_errors(scoring.lap_chole_findings(_scoring_events(s, sid)))
        return coachmod.generate(row.metrics, errors, question=body.question if body else None, exercise=row.exercise)
    if row.exercise == "port_placement":
        errors = coachmod.port_placement_errors(scoring.port_placement_findings(_scoring_events(s, sid)))
        return coachmod.generate(row.metrics, errors, question=body.question if body else None, exercise=row.exercise)
    errors = [
        {"event_id": r.event_id, "kind": r.kind, "t_ms": r.t_ms, "instrument": r.instrument}
        for r in s.scalars(
            select(db.EventRow)
            .where(db.EventRow.session_id == sid, db.EventRow.kind.not_in(("transfer_success", "stitch_placed")))
            .order_by(db.EventRow.t_ms)
        )
    ]
    return coachmod.generate(row.metrics, errors, question=body.question if body else None, exercise=row.exercise)


@app.get("/api/v1/sessions/{sid}/assessment", response_model=assessmentmod.Assessment)
def get_assessment(sid: str, s: Session = Depends(get_db)):
    """Deterministic, metric-only assessment. No Gemini call."""
    row = _get(s, sid)
    if not row.metrics:
        raise HTTPException(409, "session not completed")
    return assessmentmod.assess(row.metrics, exercise=row.exercise)
