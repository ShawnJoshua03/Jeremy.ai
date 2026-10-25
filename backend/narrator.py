"""Deterministic laparoscopic cholecystectomy narration; Claude only selects grounded wording."""
from __future__ import annotations

import json
import math
import threading
from collections import OrderedDict
from dataclasses import asdict, dataclass
from typing import Literal

import llm
import port_coach
import provider_log
import scoring

Severity = Literal["on_track", "slow", "off_track", "critical"]
STEP_SECONDS = scoring.LC_TIME_BUDGET_S / len(scoring.LC_STEPS)
MAX_SESSIONS = 10_000

# Cues follow docs/LAP_CHOLE.md; these are simulator instructions, not a clinical rubric.
STEP_CUES = {
    "veress": "place the Veress needle at the marked umbilical site",
    "insufflate": "hold insufflation within the simulator's target pressure range",
    "port_umbilical": "seat and seal the umbilical trocar before inserting the scope",
    "port_epigastric": "place the epigastric trocar under camera vision",
    "port_rsc_medial": "seat the medial subcostal trocar at its marked site",
    "port_rsc_lateral": "seat the lateral subcostal trocar at its marked site",
    "retract": "hold the gallbladder fundus over the liver",
    "dissect": "clear Calot's triangle to reach the critical view of safety",
    "clip_duct_distal": "place the distal cystic duct clip near the gallbladder",
    "ductotomy": "make the simulated cystic duct opening below the distal clip",
    "cholangiogram": "seat the cholangiogram catheter, review contrast imaging, then remove the catheter",
    "clip_duct_proximal": "place the proximal cystic duct clips below the ductotomy",
    "divide_duct": "divide the cystic duct between the proximal clips and ductotomy",
    "clip_artery": "place the proximal and distal cystic artery clips",
    "divide_artery": "divide the cystic artery between its clips",
}

ERROR_CUES = {
    "injury": "pause the simulation and review the recorded tissue injury with your instructor",
    "cbd_clip": "pause the simulation and review the clip recorded on the common bile duct",
    "cbd_cut": "pause the simulation and review the cut recorded on the common bile duct",
    "unsafe_division": "pause the simulation and review the division recorded without adequate clips",
    "division_before_cvs": "pause the simulation and review the division recorded before the critical view of safety",
    "ductotomy_wrong_structure": "pause the simulation and review the ductotomy recorded on the wrong structure",
    "unsafe_ductotomy": "pause the simulation and review the ductotomy recorded without a distal cystic duct clip",
    "ductotomy_before_cvs": "pause the simulation and review the ductotomy recorded before the critical view of safety",
    "port_offset": "recheck the port against its marked site and planned angle",
    "insufflation_out_of_range": "adjust insufflation to the simulator's target pressure range",
    "clip_crossing": "check that both clip jaws pass the far side of the structure",
    "clip_too_close": "review the recorded clip spacing around the cut",
    "cholangiogram_not_in_duct": "seat the cholangiogram catheter in the cystic duct before injecting",
    "cholangiogram_leak": "recheck catheter seating before injecting contrast",
    "hand_collision": "separate your instruments to avoid another collision",
}


@dataclass(frozen=True)
class Progress:
    step: str
    expected_progress: float
    actual_progress: float
    delta: float
    severity: Severity
    cue: str
    finding_codes: tuple[str, ...] = ()

    def response(self, text: str, repeat: bool) -> dict:
        return {"text": text, "step": self.step, "expected_progress": self.expected_progress,
                "actual_progress": self.actual_progress, "delta": self.delta,
                "severity": self.severity, "repeat": repeat}


def compute_progress(events: list[scoring.Event], elapsed_s: float) -> Progress:
    """Pure model: percentages of all 15 steps; delta = actual minus expected.

    Current step is the earliest incomplete step, even when later steps arrive first.
    A one-step pace deficit is slow. Safety findings outrank pace; major findings
    persist, while minor/placement corrections are relevant for one step interval.
    """
    if not math.isfinite(elapsed_s) or elapsed_s < 0:
        raise ValueError("elapsed_s must be finite and nonnegative")
    ordered = sorted((e for e in events if 0 <= e.t_ms <= elapsed_s * 1000), key=lambda e: e.t_ms)
    completed = {e.object_id for e in ordered if e.kind == "step_completed" and e.object_id in scoring.LC_STEPS}
    step = next((s for s in scoring.LC_STEPS if s not in completed), "complete")
    expected = round(min(1.0, elapsed_s / scoring.LC_TIME_BUDGET_S) * 100, 2)
    actual = round(len(completed) / len(scoring.LC_STEPS) * 100, 2)
    delta = round(actual - expected, 2)
    findings = scoring.lap_chole_findings(ordered)
    active = [f for f in findings if f.severity == "major" or elapsed_s - f.event.t_ms / 1000 <= STEP_SECONDS]
    active.sort(key=lambda f: (f.severity != "major", -f.event.t_ms))
    placements = {}
    for e in ordered:
        if e.kind == "port_placed" and e.object_id in port_coach.PORT_NAMES:
            placements[e.object_id] = port_coach.analyze(e)
    bad_placements = [f for f in placements.values()
                      if not f.passed and elapsed_s - f.t_ms / 1000 <= STEP_SECONDS]
    bad_placements.sort(key=lambda f: -f.t_ms)
    skipped = step != "complete" and any(scoring.LC_STEPS.index(s) > scoring.LC_STEPS.index(step) for s in completed)
    cue = STEP_CUES.get(step, "your laparoscopic cholecystectomy practice sequence is complete")
    severity: Severity = "on_track"
    if active and active[0].severity == "major":
        severity = "critical"
        cue = ERROR_CUES.get(active[0].code, "pause the simulation and review the recorded safety finding with your instructor")
    elif active or bad_placements:
        severity = "off_track"
        if bad_placements and (not active or active[0].code == "port_offset"):
            f = bad_placements[0]
            cue = f"for the {f.name}, {f.correction}"
            if len(cue.split()) > 22:
                cue = f"recheck the {f.name} against the marked site, planned angle and simulator depth target"
        else:
            cue = ERROR_CUES.get(active[0].code, "review the recorded simulator finding before continuing")
    elif skipped:
        severity = "off_track"
        cue = "return to the earliest incomplete step and " + cue
    elif step != "complete" and elapsed_s / STEP_SECONDS - len(completed) >= 1:
        severity = "slow"
        cue = "take your time and " + cue
    return Progress(step, expected, actual, delta, severity, cue, tuple(f.code for f in active))


CVS_CUES = {
    "cvs_false_positive": "stop: this view does not show all three criteria, and calling it safe is how bile duct injuries happen",
    "misidentification": "recheck which tube leaves the gallbladder neck before you commit to a structure",
    "wrong_decision": "review each of the three criteria again before you decide",
    "instructor_requested": "good call asking your instructor; take their guidance before you decide",
}


def compute_cvs_progress(events: list[scoring.Event], elapsed_s: float) -> Progress:
    """CVS trainer (lap_chole_cvs): step = current exercise id; pace target 75 s per exercise.

    Exercises come only from cvs_scoring.EXERCISES. A false "criteria demonstrated" call is critical
    and a wrong decision off_track, each for one pace interval or while that exercise is current.
    Requesting the instructor is never penalised.
    """
    import cvs_scoring
    if not math.isfinite(elapsed_s) or elapsed_s < 0:
        raise ValueError("elapsed_s must be finite and nonnegative")
    ordered = sorted((e for e in events if 0 <= e.t_ms <= elapsed_s * 1000), key=lambda e: e.t_ms)
    known = list(cvs_scoring.EXERCISES)
    a = cvs_scoring.analyze([], ordered, elapsed_s)
    resolved = {r.exercise_id for r in a.exercises if r.known and (r.decision is not None)}
    starts = [e for e in ordered if e.kind == "exercise_started" and e.object_id]
    current = starts[-1].object_id if starts else None
    if current is None or current in resolved:
        current = next((ex for ex in known if ex not in resolved), current if starts else known[0])
    step = "complete" if all(ex in resolved for ex in known) else current
    pace = cvs_scoring.PACE_TARGET_S
    expected = round(min(1.0, elapsed_s / (pace * len(known))) * 100, 2)
    actual = round(sum(1 for ex in known if ex in resolved) / len(known) * 100, 2)
    delta = round(actual - expected, 2)
    spec = cvs_scoring.EXERCISES.get(step)
    cue = spec["cue"] if spec else ("your critical view of safety exercises are complete" if step == "complete"
                                    else "inspect each structure, then submit your decision")
    severity: Severity = "on_track"
    codes: tuple[str, ...] = ()
    now_ms = elapsed_s * 1000
    recent = [r for r in a.exercises if r.error and r.decided_ms is not None
              and (now_ms - r.decided_ms <= pace * 1000 or r.exercise_id == current)]
    recent.sort(key=lambda r: (r.error != "cvs_false_positive", -r.decided_ms))
    asked = [e for e in ordered if (e.kind == "instructor_requested" or
                                    (e.kind == "decision_submitted" and e.detail == cvs_scoring.REQUEST_INSTRUCTOR))
             and now_ms - e.t_ms <= pace * 1000]
    # Pace clock: since the current exercise started, else since the last decision (or session start).
    start_ms = next((e.t_ms for e in reversed(starts) if e.object_id == step),
                    max((r.decided_ms for r in a.exercises if r.decided_ms is not None), default=0))
    if recent:
        severity = "critical" if recent[0].error == "cvs_false_positive" else "off_track"
        cue = CVS_CUES[recent[0].error]
        codes = tuple(r.error for r in recent)
    elif asked:
        cue = CVS_CUES["instructor_requested"]
    elif step != "complete" and now_ms - start_ms >= pace * 1000:
        severity = "slow"
        cue = "take your time and " + cue
    return Progress(step, expected, actual, delta, severity, cue, codes)


def sentences(progress: Progress) -> tuple[str, str]:
    """Two grounded, single-sentence forms, each within the speech word budget."""
    cue = progress.cue
    return (cue[0].upper() + cue[1:] + ".", "For this attempt, " + cue + ".")


def generate(progress: Progress) -> str:
    return generate_sourced(progress)[0]


def generate_sourced(progress: Progress) -> tuple[str, str]:
    """(text, "claude" | "fallback"). Claude picks one of two grounded sentences (schema enum), so it
    cannot invent a step or a number; any failure returns the first template sentence."""
    choices = sentences(progress)
    if not llm.configured():
        return choices[0], "fallback"
    prompt = (
        "You are a calm robot instructor for a simulated laparoscopic cholecystectomy. "
        "Use only these computed facts; never invent numbers, scores, steps or findings. "
        "Choose exactly one of the supplied grounded sentences as the next thing to say, and return it "
        "unchanged in text.\n"
        + json.dumps({"facts": asdict(progress), "sentences": choices})
    )
    schema = {"type": "object", "properties": {"text": {"type": "string", "enum": list(choices)}},
              "required": ["text"], "additionalProperties": False}
    try:
        text = llm.complete_json(prompt, schema, max_tokens=200).get("text")
    except llm.LLMUnavailable:
        return choices[0], "fallback"
    return (text, "claude") if text in choices else (choices[0], "fallback")


class LineStore:
    """Bounded, process-local last-line memory; comparison/update is atomic across routes."""

    def __init__(self, capacity: int = MAX_SESSIONS):
        self.capacity = capacity
        self._lines: OrderedDict[str, tuple[tuple, str, str]] = OrderedDict()  # sid -> (facts key, text, source)
        self._lock = threading.Lock()

    def last(self, sid: str) -> tuple[tuple, str, str] | None:
        with self._lock:
            return self._lines.get(sid)

    def choose(self, sid: str, proposed: str, choices: tuple[str, str],
               key: tuple = (), source: str = "fallback") -> tuple[str, bool]:
        with self._lock:
            prev = self._lines.get(sid)
            last = prev[1] if prev else None
            repeat = proposed == last
            text = next((s for s in choices if s != last), proposed) if repeat else proposed
            self._lines[sid] = (key, text, source)
            self._lines.move_to_end(sid)
            while len(self._lines) > self.capacity:
                self._lines.popitem(last=False)
            return text, repeat


STORE = LineStore()


def line(sid: str, progress: Progress, t_ms: int = 0, reuse_last: bool = False) -> dict:
    """Facts unchanged since the last line: reuse it without calling Claude (repeat=True, nothing new to say).
    reuse_last: the audio route speaks exactly the line the client is already showing."""
    key = (progress.step, progress.severity, progress.finding_codes)
    cached = STORE.last(sid)
    if cached and (reuse_last or cached[0] == key):
        return {**progress.response(cached[1], not reuse_last), "provider": {"text": cached[2]}}
    proposed, source = generate_sourced(progress)
    text, repeat = STORE.choose(sid, proposed, sentences(progress), key, source)
    if source == "claude":
        provider_log.record(sid, "claude", "narrator line written", "used", t_ms)
    elif llm.configured():
        provider_log.record(sid, "claude", "narrator line fell back to template", "fallback", t_ms)
    return {**progress.response(text, repeat), "provider": {"text": source}}
