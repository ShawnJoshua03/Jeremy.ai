"""Laparoscopic cholecystectomy CVS trainer (exercise `lap_chole_cvs`). Deterministic; no AI involved.

Formula and constants: docs/CVS_SCORING.md. Prototype rubric, NOT a validated assessment.

EXERCISES is the backend's own copy of the Unity ExerciseLibrary (quest/Assets/GallbladderTrainer/
Runtime/TrainingModels.cs) and the only place exercise ids appear. Add a row to add an exercise.
Unknown exercise ids and unknown decision ids are recorded and reported but score as not assessed
(correct = None), never as wrong.
"""
from __future__ import annotations

import math
from dataclasses import dataclass, field

import scoring

EXERCISES: dict[str, dict] = {
    "ex_identify": {
        "title": "Anatomy identification", "kind": "identify", "target": "cystic_duct",
        "cue": "follow the narrow tube leaving the gallbladder neck to find the cystic duct",
        "feedback_match": "The cystic duct leaves the gallbladder neck and joins the larger common hepatic duct.",
        "feedback_mismatch": "Follow the narrow tube from the gallbladder neck toward the larger common hepatic "
                             "duct before you commit.",
    },
    "ex_view_a": {
        "title": "Prepared view A", "kind": "cvs", "expected": "criteria_not_demonstrated",
        "cue": "check all three criteria of the critical view of safety before you decide",
        "feedback_match": "The lower third still lies on the liver, so the cystic plate is not exposed.",
        "feedback_mismatch": "Check where the lower third of the gallbladder meets the liver: the cystic plate "
                             "must be exposed before the view counts.",
    },
    "ex_view_b": {
        "title": "Prepared view B", "kind": "cvs", "expected": "criteria_demonstrated",
        "cue": "check all three criteria of the critical view of safety before you decide",
        "feedback_match": "The triangle is cleared, the cystic plate is exposed, and two structures enter "
                          "the gallbladder.",
        "feedback_mismatch": "Count the structures entering the gallbladder and check the cystic plate; all "
                             "three criteria are shown in this view.",
    },
}

STRUCTURES = ("gallbladder", "cystic_duct", "cystic_artery", "cystic_plate")
DECISIONS = {
    "criteria_demonstrated": "criteria demonstrated",
    "criteria_not_demonstrated": "criteria not demonstrated",
    "request_instructor": "unclear, instructor requested",
}
REQUEST_INSTRUCTOR = "request_instructor"
IDENTIFY_PREFIX = "identify:"

PACE_TARGET_S = 75.0  # per exercise
HESITATION_GRACE_S = 5.0
HESITATION_ZERO_S = 15.0  # hesitation beyond the grace at which decisiveness reaches 0
JERK_REF_M_S3 = 50.0  # mean |jerk| at which smoothness reaches 0 (prototype)
IDLE_BIN_MS = 500
IDLE_SPEED_M_S = 0.02  # a hand slower than this over a bin's tracked steps is still
HANDS = ("left", "right")
FALSE_POSITIVE_CAP = 40.0
WEIGHTS = {"accuracy": 0.60, "pace": 0.15, "decisiveness": 0.10, "smoothness": 0.15}
EXCELLENT, COMPETENT = 85.0, 65.0


def _clamp(v: float, lo: float, hi: float) -> float:
    return max(lo, min(hi, v))


def decision_label(decision: str | None) -> str:
    if decision is None:
        return "no decision"
    if decision.startswith(IDENTIFY_PREFIX):
        return "selected " + decision[len(IDENTIFY_PREFIX):].replace("_", " ")
    return DECISIONS.get(decision, decision.replace("_", " "))


def expected_of(ex_id: str) -> str | None:
    spec = EXERCISES.get(ex_id)
    if spec is None:
        return None
    return IDENTIFY_PREFIX + spec["target"] if spec["kind"] == "identify" else spec["expected"]


def judge(ex_id: str, decision: str) -> tuple[bool | None, str | None]:
    """(correct, error code). Unknown exercise or decision ids are not assessed."""
    spec = EXERCISES.get(ex_id)
    if spec is None:
        return None, None
    if spec["kind"] == "identify":
        chosen = decision[len(IDENTIFY_PREFIX):] if decision.startswith(IDENTIFY_PREFIX) else decision
        if not chosen:
            return None, None
        return (True, None) if chosen == spec["target"] else (False, "misidentification")
    if decision not in DECISIONS:
        return None, None
    if decision == spec["expected"]:
        return True, None
    if decision == "criteria_demonstrated" and spec["expected"] == "criteria_not_demonstrated":
        return False, "cvs_false_positive"  # the classic cause of bile duct injury
    return False, "wrong_decision"


@dataclass
class ExerciseResult:
    exercise_id: str
    title: str
    decision: str | None
    expected: str | None
    correct: bool | None
    unclear: bool
    time_to_decision_s: float | None
    structures_inspected: list[str]
    hover_dwell_s: dict[str, float]
    hand_path_m: float
    head_path_m: float
    hesitation_s: float
    error: str | None = None
    started_ms: int | None = None
    decided_ms: int | None = None
    known: bool = True


@dataclass
class Analysis:
    score: float
    grade: str
    exercises: list[ExerciseResult]
    movement: dict
    counts: dict = field(default_factory=dict)


def segments(events: list[scoring.Event], end_ms: int) -> list[tuple[str, int, int]]:
    """(exercise id, start, end) for every exercise_started; a segment ends at the next start."""
    starts = [e for e in sorted(events, key=lambda e: e.t_ms) if e.kind == "exercise_started" and e.object_id]
    out = []
    for i, e in enumerate(starts):
        nxt = starts[i + 1].t_ms if i + 1 < len(starts) else max(end_ms, e.t_ms)
        out.append((e.object_id, e.t_ms, nxt))
    return out


def _in(segs: list[tuple[str, int, int]], t: int) -> bool:
    return any(a <= t < b or (a == b == t) for _, a, b in segs)


def _path(samples: list[scoring.Sample], segs: list[tuple[str, int, int]] | None = None) -> float:
    by_inst: dict[str, list[scoring.Sample]] = {}
    for s in samples:
        if segs is None or _in(segs, s.t_ms):
            by_inst.setdefault(s.instrument, []).append(s)
    return sum(scoring.travel_distance(v) for v in by_inst.values())


def smoothness(samples: list[scoring.Sample]) -> float | None:
    """1 - mean |jerk| / JERK_REF over hand samples (finite differences, gaps not bridged); None if no data."""
    total_dt = 0.0
    total = 0.0
    for hand in HANDS:
        run: list[scoring.Sample] = []
        for s in sorted((s for s in samples if s.instrument == hand), key=lambda s: s.index):
            if not s.valid:
                run = []
                continue
            if run and not 0 < s.t_ms - run[-1].t_ms <= scoring.MAX_GAP_MS:
                run = []
            run.append(s)
            if len(run) < 4:
                continue
            p = [r.pos for r in run[-4:]]
            t = [r.t_ms / 1000 for r in run[-4:]]
            v = [tuple((p[i + 1][k] - p[i][k]) / (t[i + 1] - t[i]) for k in range(3)) for i in range(3)]
            a = [tuple((v[i + 1][k] - v[i][k]) / ((t[i + 2] - t[i]) / 2) for k in range(3)) for i in range(2)]
            dt = (t[3] - t[0]) / 3
            j = math.sqrt(sum(((a[1][k] - a[0][k]) / dt) ** 2 for k in range(3)))
            total += j * dt
            total_dt += dt
    if total_dt <= 0:
        return None
    return _clamp(1 - (total / total_dt) / JERK_REF_M_S3, 0.0, 1.0)


def idle_seconds(samples: list[scoring.Sample]) -> float:
    """Half-second bins with tracked hand steps in which every hand moved slower than IDLE_SPEED_M_S (a step counts in the bin of its later sample)."""
    bins: dict[int, dict[str, float]] = {}
    for hand in HANDS:
        prev = None
        for s in sorted((s for s in samples if s.instrument == hand), key=lambda s: s.index):
            if not s.valid:
                prev = None
                continue
            if prev is not None and 0 < s.t_ms - prev.t_ms <= scoring.MAX_GAP_MS:
                b = bins.setdefault(s.t_ms // IDLE_BIN_MS, {}).setdefault(hand, [0.0, 0.0])
                b[0] += math.dist(prev.pos, s.pos)
                b[1] += (s.t_ms - prev.t_ms) / 1000
            prev = s
    idle = sum(1 for b in bins.values() if all(d / t < IDLE_SPEED_M_S for d, t in b.values()))
    return round(idle * IDLE_BIN_MS / 1000, 2)


def _end_ms(samples: list[scoring.Sample], events: list[scoring.Event], elapsed_s: float) -> int:
    return max([int(elapsed_s * 1000)] + [s.t_ms for s in samples] + [e.t_ms for e in events])


def analyze(samples: list[scoring.Sample], events: list[scoring.Event], elapsed_s: float) -> Analysis:
    ordered = sorted(events, key=lambda e: e.t_ms)
    end = _end_ms(samples, ordered, elapsed_s)
    segs = segments(ordered, end)
    seen_ids: list[str] = []
    for e in ordered:
        ex = e.object_id if e.kind in ("exercise_started", "decision_submitted", "instructor_requested") else None
        if e.kind == "structure_selected" and e.detail:
            ex = e.detail
        if ex and ex not in seen_ids:
            seen_ids.append(ex)
    order = [ex for ex in EXERCISES if ex in seen_ids] + [ex for ex in seen_ids if ex not in EXERCISES]

    hands = [s for s in samples if s.instrument in HANDS]
    heads = [s for s in samples if s.instrument == "head"]
    results = []
    for ex in order:
        spec = EXERCISES.get(ex)
        mine = [s for s in segs if s[0] == ex]
        start = mine[0][1] if mine else None
        decisions = [e for e in ordered if e.kind == "decision_submitted" and e.object_id == ex and e.detail]
        graded = next((e for e in decisions if e.detail != REQUEST_INSTRUCTOR), None)
        unclear = any(e.detail == REQUEST_INSTRUCTOR for e in decisions) or any(
            e.kind == "instructor_requested" and e.object_id == ex for e in ordered)
        correct, error = judge(ex, graded.detail) if graded else (None, None)
        cutoff = graded.t_ms if graded else end
        inspected: list[str] = []
        dwell: dict[str, float] = {}
        first_select = None
        for e in ordered:
            if e.kind not in ("structure_hovered", "structure_selected") or not e.object_id:
                continue
            belongs = e.detail == ex if e.kind == "structure_selected" and e.detail else _in(mine, e.t_ms)
            if not belongs:
                continue
            if e.object_id not in inspected:
                inspected.append(e.object_id)
            if e.kind == "structure_hovered" and e.value is not None and e.value > 0:
                dwell[e.object_id] = round(dwell.get(e.object_id, 0.0) + e.value, 2)
            if e.kind == "structure_selected" and first_select is None and e.t_ms <= cutoff:
                first_select = e.t_ms
        hesitation = 0.0
        if graded and first_select is not None:
            hesitation = round(max(0.0, (graded.t_ms - first_select) / 1000 - HESITATION_GRACE_S), 2)
        ttd = round((graded.t_ms - start) / 1000, 2) if graded and start is not None and graded.t_ms >= start else None
        results.append(ExerciseResult(
            exercise_id=ex, title=spec["title"] if spec else ex,
            decision=graded.detail if graded else (REQUEST_INSTRUCTOR if unclear else None),
            expected=expected_of(ex), correct=correct, unclear=unclear, time_to_decision_s=ttd,
            structures_inspected=inspected, hover_dwell_s=dwell,
            hand_path_m=round(_path(hands, mine), 3) if mine else 0.0,
            head_path_m=round(_path(heads, mine), 3) if mine else 0.0,
            hesitation_s=hesitation, error=error, started_ms=start,
            decided_ms=graded.t_ms if graded else None, known=spec is not None,
        ))

    smooth = smoothness(samples)
    movement = {
        "hand_path_m": round(_path(hands), 3), "head_path_m": round(_path(heads), 3),
        "smoothness": round(smooth, 3) if smooth is not None else None,
        "idle_s": idle_seconds(samples), "samples": len(samples),
    }
    score, counts = _score(results, smooth)
    false_pos = counts["false_positive_cvs"]
    grade = ("excellent" if score >= EXCELLENT and not false_pos else
             "competent" if score >= COMPETENT else "needs_practice")
    return Analysis(score=score, grade=grade, exercises=results, movement=movement, counts=counts)


def _score(results: list[ExerciseResult], smooth: float | None) -> tuple[float, dict]:
    graded = [r for r in results if r.correct is not None]
    correct = sum(1 for r in graded if r.correct)
    accuracy = correct / len(graded) if graded else 0.0
    pace = (sum(_clamp(1 - max(0.0, (r.time_to_decision_s or 0.0) - PACE_TARGET_S) / PACE_TARGET_S, 0.0, 1.0)
                for r in graded) / len(graded)) if graded else 0.0
    decisive = (sum(_clamp(1 - r.hesitation_s / HESITATION_ZERO_S, 0.0, 1.0) for r in graded)
                / len(graded)) if graded else 0.0
    parts = {"accuracy": accuracy, "pace": pace, "decisiveness": decisive, "smoothness": smooth}
    used = {k: w for k, w in WEIGHTS.items() if parts[k] is not None}
    score = 100 * sum(w * parts[k] for k, w in used.items()) / sum(used.values())
    score = _clamp(score, 0.0, 100.0)
    codes = [r.error for r in results]
    if "cvs_false_positive" in codes:
        score = min(score, FALSE_POSITIVE_CAP)
    counts = {
        "exercises": len(results), "known": sum(1 for r in results if r.known), "graded": len(graded),
        "correct": correct, "wrong": len(graded) - correct,
        "unclear": sum(1 for r in results if r.unclear),
        "false_positive_cvs": codes.count("cvs_false_positive"),
        "misidentification": codes.count("misidentification"),
        "wrong_decision": codes.count("wrong_decision"),
        "not_assessed": sum(1 for r in results if r.correct is None),
    }
    return round(score, 1), counts


@dataclass
class CvsMetrics(scoring.Metrics):
    exercise: str = "lap_chole_cvs"
    details: dict | None = None


def compute_metrics(samples: list[scoring.Sample], events: list[scoring.Event], elapsed_s: float) -> CvsMetrics:
    """Map the analysis onto the shared metric fields (see docs/CVS_SCORING.md)."""
    a = analyze(samples, events, elapsed_s)
    c = a.counts
    by_inst: dict[str, list[scoring.Sample]] = {}
    for s in samples:
        by_inst.setdefault(s.instrument, []).append(s)
    dist = {k: scoring.travel_distance(v) for k, v in by_inst.items()}
    details: dict = {"grade": a.grade, **c, "hand_path_m": a.movement["hand_path_m"],
                     "head_path_m": a.movement["head_path_m"], "idle_s": a.movement["idle_s"],
                     "samples": a.movement["samples"]}
    if a.movement["smoothness"] is not None:
        details["smoothness"] = a.movement["smoothness"]
    known = len(EXERCISES)
    resolved = sum(1 for r in a.exercises if r.known and (r.correct is not None or r.unclear))
    return CvsMetrics(
        completion_pct=round(_clamp(resolved / known, 0.0, 1.0) * 100, 1),
        successful_transfers=c["correct"],
        drops=c["false_positive_cvs"],
        invalid_releases=c["misidentification"],
        elapsed_s=elapsed_s,
        distance_by_instrument=dist,
        total_distance_m=sum(dist.values()),
        path_efficiency=1.0,  # no ideal path for a decision exercise; charges no inefficient OR time
        practice_score=a.score,
        details=details,
    )
