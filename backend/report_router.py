"""Review report and replay for the CVS trainer (lap_chole_cvs). Contract: cvs_contract v1.

Every number is computed by cvs_scoring and mistake_cost_router. Claude (one short request) writes only the prose fields from those facts; prose that is malformed or contains any
number not in the facts is discarded for the deterministic template. A completed session cannot
change, so the generated report is cached per session; `providers` and `provider_events` are
recomputed on every GET because later activity (payment, speech) must show up.
"""
from __future__ import annotations

import json
import os
import re
import threading
from collections import OrderedDict
from concurrent.futures import Future, ThreadPoolExecutor

from fastapi import APIRouter, HTTPException, Response
from fastapi.responses import JSONResponse
from sqlalchemy import select

import cvs_scoring
import database as db
import llm
import mistake_cost_router
import provider_log
import ratelimit
import scoring
from speech import cartesia

router = APIRouter(prefix="/api/v1/sessions", tags=["report"])
NO_STORE = {"Cache-Control": "no-store"}
DISCLAIMER = ("Educational prototype, not a validated assessment of surgical competence. "
              "Costs are illustrative, not billing data.")
REPORT_TIMEOUT_S = 8.0
REPLAY_MIN_STEP_MS = 100  # <= 10 Hz
MAX_CACHED = 1_000
MAX_PROSE_CHARS = 400
NUMBER = re.compile(r"\d+(?:\.\d+)?")

_cache: OrderedDict[str, dict] = OrderedDict()
_cache_lock = threading.Lock()
_build_locks: dict[str, threading.Lock] = {}
# Cartesia can take ~25 s for a 500-character summary, so synthesis starts as soon as the report
# exists and "play summary" usually finds the audio ready.
_speech_pool = ThreadPoolExecutor(max_workers=2, thread_name_prefix="report-speech")
_audio: dict[str, Future] = {}
AUDIO_WAIT_S = 40.0


def _prewarm_audio(sid: str, summary: str) -> None:
    if os.environ.get("CARTESIA_API_KEY", "").strip():
        _audio[sid] = _speech_pool.submit(cartesia.text_to_audio, summary)


def _load(sid: str, completed: bool = True) -> tuple[dict, list[scoring.Sample], list[scoring.Event]]:
    with db.SessionLocal() as session:
        row = session.get(db.TrainingSession, sid)
        if row is None:
            raise HTTPException(404, "session not found")
        if completed and row.exercise != "lap_chole_cvs":
            raise HTTPException(409, "the review report is for critical view of safety (lap_chole_cvs) sessions")
        if completed and (row.status != "completed" or row.metrics is None):
            raise HTTPException(409, "session must be completed")
        info = {"session_id": row.id, "exercise": row.exercise, "trainee_id": row.trainee_id,
                "completed_at": row.completed_at.isoformat() if row.completed_at else None,
                "elapsed_s": row.elapsed_s or 0.0, "status": row.status}
        samples = [scoring.Sample(r.instrument, r.idx, r.t_ms, (r.x, r.y, r.z), r.valid)
                   for r in session.scalars(select(db.SampleRow).where(db.SampleRow.session_id == sid)
                                            .order_by(db.SampleRow.t_ms, db.SampleRow.idx))]
        events = mistake_cost_router._events(session, sid)
    return info, samples, events


# ---------- facts and prose ----------
def _exercise_out(r: cvs_scoring.ExerciseResult) -> dict:
    return {"exercise_id": r.exercise_id, "title": r.title, "decision": r.decision, "expected": r.expected,
            "correct": r.correct, "unclear": r.unclear, "time_to_decision_s": r.time_to_decision_s,
            "structures_inspected": r.structures_inspected, "hover_dwell_s": r.hover_dwell_s,
            "hand_path_m": r.hand_path_m, "head_path_m": r.head_path_m, "hesitation_s": r.hesitation_s}


def facts(analysis: cvs_scoring.Analysis) -> dict:
    """The only data Claude sees. Money is left out: the model never writes a dollar figure."""
    return {
        "procedure": "laparoscopic cholecystectomy, critical view of safety judgement",
        "score": analysis.score, "score_max": 100, "grade": analysis.grade, "counts": analysis.counts,
        "pace_target_s": cvs_scoring.PACE_TARGET_S, "hesitation_grace_s": cvs_scoring.HESITATION_GRACE_S,
        "movement": analysis.movement,
        "exercises": [{**_exercise_out(r), "decision_label": cvs_scoring.decision_label(r.decision),
                       "error": r.error, "assessed": r.correct is not None} for r in analysis.exercises],
    }


def _numbers(value, out: set[float]) -> set[float]:
    if isinstance(value, bool):
        return out
    if isinstance(value, (int, float)):
        for digits in (0, 1, 2):
            out.add(round(float(value), digits))
        out.add(float(value))
    elif isinstance(value, str):
        out.update(round(float(n), 2) for n in NUMBER.findall(value))
    elif isinstance(value, dict):
        for k, v in value.items():
            _numbers(k, out)
            _numbers(v, out)
    elif isinstance(value, list):
        for v in value:
            _numbers(v, out)
    return out


def number_guard(texts: list[str], fact_data: dict) -> bool:
    """True when every number in the prose appears in the facts (as given or rounded)."""
    allowed = {round(n, 2) for n in _numbers(fact_data, set())}
    return all(round(float(n), 2) in allowed for t in texts for n in NUMBER.findall(t))


def _secs(v: float | None) -> str:
    return f"{v:.0f} s" if v is not None else "no recorded time"


def template(analysis: cvs_scoring.Analysis) -> dict:
    exercises = []
    for r in analysis.exercises:
        spec = cvs_scoring.EXERCISES.get(r.exercise_id)
        label = cvs_scoring.decision_label(r.decision)
        if r.correct is True:
            review = f"You chose {label}, which matches this prepared view, in {_secs(r.time_to_decision_s)}."
            tip = spec["feedback_match"]
        elif r.error == "cvs_false_positive":
            review = ("You called this view criteria demonstrated when it is not; acting on that view is the "
                      "classic route to a bile duct injury.")
            tip = spec["feedback_mismatch"]
        elif r.correct is False:
            review = f"You chose {label}, which does not match this prepared view."
            tip = spec["feedback_mismatch"]
        elif r.unclear:
            review = "You asked for instructor review, which is the safe choice when a view is unclear."
            tip = spec["feedback_match"] if spec else "Talk the view through with your instructor."
        else:
            review = "This exercise was recorded but not assessed."
            tip = "Submit a decision for each exercise so it can be reviewed."
        if r.hesitation_s > 0 and r.correct is not None:
            review += f" You hesitated {r.hesitation_s:.0f} s beyond the grace period after your first selection."
        exercises.append({"exercise_id": r.exercise_id, "review": review, "tip": tip})
    c, m = analysis.counts, analysis.movement
    summary = [f"You scored {analysis.score:.0f} out of 100 ({analysis.grade.replace('_', ' ')})."]
    summary.append(f"You answered {c['correct']} of {c['graded']} assessed exercises correctly"
                   + (f" and asked your instructor {c['unclear']} time(s)." if c["unclear"] else "."))
    if c["false_positive_cvs"]:
        summary.append("You called a view safe when the critical view of safety was not demonstrated; "
                       "that call is what leads to bile duct injuries in real operations.")
    else:
        summary.append("You never called an incomplete view safe.")
    summary.append(f"Your hands travelled {m['hand_path_m']:.1f} m and you were still for {m['idle_s']:.0f} s.")
    strengths, focus = [], []
    if c["graded"] and c["correct"] == c["graded"]:
        strengths.append("Every assessed decision matched the prepared view.")
    if not c["false_positive_cvs"]:
        strengths.append("No incomplete view was called safe.")
    if c["unclear"]:
        strengths.append("You asked for help when a view was unclear.")
    if c["false_positive_cvs"]:
        focus.append("Confirm all three criteria, including the exposed cystic plate, before calling a view safe.")
    if c["misidentification"]:
        focus.append("Trace the cystic duct from the gallbladder neck before selecting it.")
    if c["wrong_decision"]:
        focus.append("Review each criterion again when a complete view looks doubtful.")
    if any(r.hesitation_s > 0 for r in analysis.exercises):
        focus.append("Commit sooner once you have inspected the structures.")
    if not focus:
        focus.append("Repeat the set and keep each decision within the pace target.")
    return {"exercises": exercises, "summary": " ".join(summary),
            "strengths": strengths or ["You completed the session."], "focus_next": focus}


def _valid(raw: object, analysis: cvs_scoring.Analysis) -> dict:
    if not isinstance(raw, dict):
        raise ValueError("not an object")
    entries = raw.get("exercises")
    if not isinstance(entries, list) or [e.get("exercise_id") if isinstance(e, dict) else None for e in entries] \
            != [r.exercise_id for r in analysis.exercises]:
        raise ValueError("exercise list does not match")

    def text(v, limit=MAX_PROSE_CHARS) -> str:
        if not isinstance(v, str) or not v.strip() or len(v) > limit:
            raise ValueError("bad text")
        return v.strip()

    def items(v) -> list[str]:
        if not isinstance(v, list) or not 1 <= len(v) <= 3:
            raise ValueError("bad list")
        return [text(x, 200) for x in v]

    return {"exercises": [{"exercise_id": e["exercise_id"], "review": text(e.get("review")),
                           "tip": text(e.get("tip"), 200)} for e in entries],
            "summary": text(raw.get("summary"), 700), "strengths": items(raw.get("strengths")),
            "focus_next": items(raw.get("focus_next"))}


def generate_prose(analysis: cvs_scoring.Analysis) -> tuple[dict, str]:
    """(prose, "claude" | "fallback" | "off")."""
    fallback = template(analysis)
    if not llm.configured():
        return fallback, "off"
    fact_data = facts(analysis)
    prompt = (
        "You are a calm surgical instructor reviewing a trainee's simulated laparoscopic cholecystectomy "
        "critical view of safety exercises. Use ONLY these computed facts. Never invent or change a number, "
        "score, grade, time, distance, structure or finding; you may repeat numbers that appear in the facts. "
        "Never mention money. For each exercise, in the same order, write `review` (1-2 sentences) and "
        "`tip` (one sentence). Then write `summary` (3-4 plain-language sentences), `strengths` (1-3 short "
        "items) and `focus_next` (1-3 short items). A false 'criteria demonstrated' call is the classic cause "
        "of bile duct injury; requesting the instructor is good behaviour, never a fault. Facts:\n"
        + json.dumps(fact_data)
    )
    try:
        prose = _valid(llm.complete_json(prompt, PROSE_SCHEMA, max_tokens=2000, timeout_s=REPORT_TIMEOUT_S), analysis)
        texts = [prose["summary"], *prose["strengths"], *prose["focus_next"]]
        texts += [t for e in prose["exercises"] for t in (e["review"], e["tip"])]
        if not number_guard(texts, fact_data):
            raise ValueError("Claude wrote a number that is not in the facts")
        return prose, "claude"
    except (llm.LLMUnavailable, KeyError, IndexError, ValueError, TypeError, AttributeError):
        return fallback, "fallback"


_STRING = {"type": "string"}
PROSE_SCHEMA = {
    "type": "object",
    "properties": {
        "exercises": {"type": "array", "items": {
            "type": "object", "properties": {"exercise_id": _STRING, "review": _STRING, "tip": _STRING},
            "required": ["exercise_id", "review", "tip"], "additionalProperties": False}},
        "summary": _STRING,
        "strengths": {"type": "array", "items": _STRING},
        "focus_next": {"type": "array", "items": _STRING},
    },
    "required": ["exercises", "summary", "strengths", "focus_next"],
    "additionalProperties": False,
}


# ---------- report ----------
def mistake_cost(sid: str, analysis: cvs_scoring.Analysis) -> dict:
    full = mistake_cost_router.build_cvs_report(sid, analysis)
    return {"total_usd": full["total_usd"],
            "lines": [{"label": m["label"], "usd": m["total_usd"], "source": m["source"], "assumed": m["assumed"]}
                      for m in full["mistakes"]],
            "report_url": f"/api/v1/sessions/{sid}/mistake-cost/report"}


def build(info: dict, samples: list[scoring.Sample], events: list[scoring.Event]) -> dict:
    analysis = cvs_scoring.analyze(samples, events, info["elapsed_s"])
    prose, source = generate_prose(analysis)
    t_ms = int(info["elapsed_s"] * 1000)
    if source == "claude":
        provider_log.record(info["session_id"], "claude", "review report written", "used", t_ms)
    elif source == "fallback":
        provider_log.record(info["session_id"], "claude", "review report fell back to template", "fallback", t_ms)
    by_id = {e["exercise_id"]: e for e in prose["exercises"]}
    return {
        "session_id": info["session_id"], "exercise": info["exercise"], "trainee_id": info["trainee_id"],
        "completed_at": info["completed_at"], "score": analysis.score, "grade": analysis.grade,
        "exercises": [{**_exercise_out(r), "review": by_id[r.exercise_id]["review"],
                       "tip": by_id[r.exercise_id]["tip"]} for r in analysis.exercises],
        "movement": analysis.movement, "summary": prose["summary"], "strengths": prose["strengths"],
        "focus_next": prose["focus_next"], "mistake_cost": mistake_cost(info["session_id"], analysis),
        "source": "claude" if source == "claude" else "fallback", "disclaimer": DISCLAIMER,
        "_t_ms": t_ms,
    }


def report(sid: str) -> dict:
    with _cache_lock:
        cached = _cache.get(sid)
        lock = _build_locks.setdefault(sid, threading.Lock())
    if cached is None:
        with lock:  # one Claude call per session even under concurrent GETs
            with _cache_lock:
                cached = _cache.get(sid)
            if cached is None:
                cached = build(*_load(sid))
                with _cache_lock:
                    _cache[sid] = cached
                    _cache.move_to_end(sid)
                    while len(_cache) > MAX_CACHED:
                        old, _ = _cache.popitem(last=False)
                        _build_locks.pop(old, None)
                        _audio.pop(old, None)
                _prewarm_audio(sid, cached["summary"])
    else:
        _load_check(sid)
    return {**cached, "providers": provider_log.providers(sid), "provider_events": provider_log.events(sid)}


def _public(out: dict) -> dict:
    return {k: v for k, v in out.items() if not k.startswith("_")}


def _load_check(sid: str) -> None:
    with db.SessionLocal() as session:
        if session.get(db.TrainingSession, sid) is None:  # a deleted row must not be served from cache
            raise HTTPException(404, "session not found")


def reset_cache() -> None:
    with _cache_lock:
        _cache.clear()
        _build_locks.clear()


@router.get("/{sid}/report")
def get_report(sid: str):
    return JSONResponse(_public(report(sid)), headers=NO_STORE)


@router.post("/{sid}/report/audio", response_class=Response, responses={
    200: {"content": {"audio/wav": {"schema": {"type": "string", "format": "binary"}}}},
    503: {"description": "Speech unavailable; JSON contains the summary text"},
})
def report_audio(sid: str):
    out = report(sid)
    ratelimit.check("coach_audio", sid)  # Existing shared Cartesia quota.
    t_ms = out["_t_ms"]
    pending = _audio.get(sid)
    try:
        try:
            audio = pending.result(timeout=AUDIO_WAIT_S) if pending else cartesia.text_to_audio(out["summary"])
        except cartesia.SpeechUnavailable:
            if pending is None:
                raise
            _audio.pop(sid, None)  # a failed prewarm is retried once, synchronously
            audio = cartesia.text_to_audio(out["summary"])
    except (cartesia.SpeechUnavailable, TimeoutError):
        provider_log.record(sid, "cartesia", "report summary speech unavailable", "unavailable", t_ms)
        return JSONResponse(status_code=503, headers=NO_STORE, content={
            "detail": "Speech is unavailable; use text feedback.", "summary": out["summary"]})
    provider_log.record(sid, "cartesia", "report summary spoken", "used", t_ms)
    return Response(audio, media_type="audio/wav", headers={**NO_STORE, "X-Speech-Provider": "cartesia",
                                                           "Access-Control-Expose-Headers": "X-Speech-Provider"})


# ---------- replay ----------
def downsample(samples: list[scoring.Sample], min_step_ms: int = REPLAY_MIN_STEP_MS) -> dict[str, list[list[float]]]:
    """Valid samples per instrument, keeping one at most every min_step_ms (<= 10 Hz)."""
    tracks: dict[str, list[list[float]]] = {"left": [], "right": [], "head": []}
    last: dict[str, int] = {}
    for s in sorted(samples, key=lambda s: (s.instrument, s.t_ms, s.index)):
        if not s.valid:
            continue
        if s.instrument in last and s.t_ms - last[s.instrument] < min_step_ms:
            continue
        last[s.instrument] = s.t_ms
        tracks.setdefault(s.instrument, []).append([s.t_ms, *(round(c, 4) for c in s.pos)])
    return tracks


def replay(info: dict, samples: list[scoring.Sample], events: list[scoring.Event]) -> dict:
    duration = max([int(info["elapsed_s"] * 1000)] + [s.t_ms for s in samples] + [e.t_ms for e in events])
    return {"duration_ms": duration, "tracks": downsample(samples),
            "events": [{"t_ms": e.t_ms, "kind": e.kind, "object_id": e.object_id, "detail": e.detail}
                       for e in sorted(events, key=lambda e: e.t_ms)]}


@router.get("/{sid}/replay")
def get_replay(sid: str):
    """Any exercise and any status; unknown instruments, kinds and ids pass through."""
    return JSONResponse(replay(*_load(sid, completed=False)), headers=NO_STORE)
