"""Deterministic laparoscopic cholecystectomy narration; Gemini only selects grounded wording."""
from __future__ import annotations

import json
import math
import os
import threading
from collections import OrderedDict
from dataclasses import asdict, dataclass
from typing import Literal

import httpx

import coach
import port_coach
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


def sentences(progress: Progress) -> tuple[str, str]:
    """Two grounded, single-sentence forms, each within the speech word budget."""
    cue = progress.cue
    return (cue[0].upper() + cue[1:] + ".", "For this attempt, " + cue + ".")


def generate(progress: Progress) -> str:
    """Reuse coach's credentials, model, client/retry budget and offline pattern.

    Constrained wording selection prevents even plausible invented steps or numbers;
    provider free text is never trusted. Calls run in FastAPI's sync thread pool.
    """
    choices = sentences(progress)
    key = os.environ.get("GEMINI_API_KEY", "").strip()
    if not key:
        return choices[0]
    prompt = (
        "You are a calm robot instructor for a simulated laparoscopic cholecystectomy. "
        "Use only these computed facts; never invent numbers, scores, steps or findings. "
        "Turn the facts into ONE spoken sentence of at most 25 words by choosing exactly one "
        "of the supplied grounded sentences; return it unchanged in text.\n"
        + json.dumps({"facts": asdict(progress), "sentences": choices})
    )
    body = {"contents": [{"parts": [{"text": prompt}]}], "generationConfig": {
        "responseMimeType": "application/json", "temperature": 0.2, "thinkingConfig": {"thinkingBudget": 0},
        "responseSchema": {"type": "OBJECT", "properties": {
            "text": {"type": "STRING", "enum": list(choices)}}, "required": ["text"]}}}
    try:
        with httpx.Client(timeout=coach.GEMINI_TIMEOUT_S) as client:
            response = coach._post_with_retry(client, coach.GEMINI_URL.format(
                model=os.environ.get("GEMINI_MODEL", coach.DEFAULT_MODEL)), body, key)
            response.raise_for_status()
            raw = json.loads(response.json()["candidates"][0]["content"]["parts"][0]["text"])
        text = raw.get("text") if isinstance(raw, dict) else None
        return text if text in choices else choices[0]
    except (httpx.HTTPError, KeyError, IndexError, ValueError, TypeError, AttributeError):
        return choices[0]


class LineStore:
    """Bounded, process-local last-line memory; comparison/update is atomic across routes."""

    def __init__(self, capacity: int = MAX_SESSIONS):
        self.capacity = capacity
        self._lines: OrderedDict[str, str] = OrderedDict()
        self._lock = threading.Lock()

    def choose(self, sid: str, proposed: str, choices: tuple[str, str]) -> tuple[str, bool]:
        with self._lock:
            last = self._lines.get(sid)
            repeat = proposed == last
            text = next((s for s in choices if s != last), proposed) if repeat else proposed
            self._lines[sid] = text
            self._lines.move_to_end(sid)
            while len(self._lines) > self.capacity:
                self._lines.popitem(last=False)
            return text, repeat


STORE = LineStore()


def line(sid: str, progress: Progress) -> dict:
    proposed = generate(progress)
    text, repeat = STORE.choose(sid, proposed, sentences(progress))
    return progress.response(text, repeat)
