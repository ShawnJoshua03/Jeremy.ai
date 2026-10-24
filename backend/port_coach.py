"""Spoken coaching for lap chole port placement (Veress needle and the four trocars).

Every number here is computed in Python from `port_placed` events (docs/LAP_CHOLE.md). No LLM is
involved: the wording is template text, so it works with no Gemini key, and the same string is both
the displayed text and the Cartesia transcript.

Frame (board-local metres): x toward the feet, -x toward the head, y up, +z the patient's right.
offset = source (actual skin entry) - target (ideal site).

Depth is not in the LAP_CHOLE.md contract yet. Proposed: Unity sends it in the optional `detail`
string as `depth_mm=38` (tip depth past the skin along the shaft, mm), optionally followed by
`;tilt=feet` (which way the shaft leaned too far: head, feet, left, right, flat or upright).
Both keys are optional; a missing or unparsable key means that criterion is not assessed.

PROTOTYPE THRESHOLDS, NOT CLINICAL STANDARDS. The 15 mm and 20 degree limits come from
docs/LAP_CHOLE.md (scoring.PORT_MAX_MM / PORT_MAX_DEG). The depth targets below are guesses for the
simulated abdominal wall and must be tuned to the Unity model; they are not taken from any source.
"""
from __future__ import annotations

import math
import re
from dataclasses import asdict, dataclass
from typing import Any, Iterable

from scoring import PORT_MAX_DEG, PORT_MAX_MM

MAX_CUE_WORDS = 25
MIN_REPORTED_MM = 3.0  # direction components smaller than this are not spoken
MIN_IMPROVEMENT_MM = 2.0  # smaller changes between attempts are treated as "about the same"

# (ideal depth past the skin, tolerance) in mm. GUESSES for the simulated wall, not clinical values.
DEPTH_TARGETS_MM: dict[str, tuple[float, float]] = {
    "veress": (30.0, 8.0),
    "umbilical": (40.0, 10.0),
    "epigastric": (40.0, 10.0),
    "rsc_medial": (35.0, 10.0),
    "rsc_lateral": (35.0, 10.0),
}

PORT_NAMES = {
    "veress": "Veress needle",
    "umbilical": "umbilical port",
    "epigastric": "epigastric port",
    "rsc_medial": "medial subcostal port",
    "rsc_lateral": "lateral subcostal port",
}

DISCLAIMER = "These tolerances are practice targets for this simulator, not clinical standards."

# tilt key -> spoken fix
_TILT_FIX = {
    "head": "lean the shaft back toward the feet",
    "feet": "lean the shaft back toward the head",
    "left": "lean the shaft back toward the patient's right",
    "right": "lean the shaft back toward the patient's left",
    "flat": "angle the trocar more upright",
    "upright": "lay the trocar flatter",
}

_ONES = ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
         "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen"]
_TENS = ["", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"]


def words(n: float) -> str:
    """Whole number in words for speech ("twenty-two"). Rounds; values above 999 stay as digits."""
    n = int(round(abs(n)))
    if n < 20:
        return _ONES[n]
    if n < 100:
        tens, ones = divmod(n, 10)
        return _TENS[tens] + (f"-{_ONES[ones]}" if ones else "")
    if n < 1000:
        hundreds, rest = divmod(n, 100)
        return f"{_ONES[hundreds]} hundred" + (f" and {words(rest)}" if rest else "")
    return str(n)


def mm_words(mm: float) -> str:
    n = int(round(abs(mm)))
    if n == 0:
        return "under one millimetre"
    return f"{words(n)} millimetre" + ("" if n == 1 else "s")


def deg_words(deg: float) -> str:
    n = int(round(abs(deg)))
    return f"{words(n)} degree" + ("" if n == 1 else "s")


def count_words(text: str) -> int:
    return len(text.split())


def parse_detail(detail: str | None) -> dict[str, str]:
    """`depth_mm=38;tilt=feet` -> {"depth_mm": "38", "tilt": "feet"}. Unknown text is ignored."""
    if not detail:
        return {}
    return {k.lower(): v.strip().lower() for k, v in re.findall(r"([A-Za-z_]+)\s*=\s*([^;,\s]+)", detail)}


def _depth(detail: dict[str, str]) -> float | None:
    try:
        v = float(detail["depth_mm"])
    except (KeyError, ValueError):
        return None
    return v if math.isfinite(v) and 0 <= v <= 500 else None


def _attr(e: Any, name: str):
    return e.get(name) if isinstance(e, dict) else getattr(e, name, None)


def _side(axis: str, value_mm: float) -> str:
    if axis == "x":
        return "toward the feet" if value_mm > 0 else "toward the head"
    return "to the patient's right" if value_mm > 0 else "to the patient's left"


@dataclass
class PlacementFinding:
    port: str
    name: str
    t_ms: int
    event_id: str | None
    attempt: int  # 1 for the first placement of this port in the session
    # location
    offset_mm: float | None  # 3D distance, as scoring.port_error
    offset_head_feet_mm: float | None  # +feet / -head
    offset_left_right_mm: float | None  # +patient right / -patient left
    direction: str | None  # where the entry actually was, e.g. "toward the feet and to the patient's left"
    location_ok: bool | None
    # angle
    angle_deg: float | None
    tilt: str | None
    angle_ok: bool | None
    # depth (optional, from detail)
    depth_mm: float | None
    ideal_depth_mm: float | None
    depth_tolerance_mm: float | None
    depth_error_mm: float | None  # + too deep, - too shallow
    depth_ok: bool | None
    # summary
    passed: bool
    worst: str | None  # location | angle | depth, the failed criterion furthest past its limit
    correction: str  # one concrete instruction, plain text

    def to_dict(self) -> dict:
        return asdict(self)


def _location_fix(dx: float, dz: float) -> str:
    """Instruction to move from the actual entry back onto the site (opposite of the offset)."""
    parts = []
    if abs(dx) >= MIN_REPORTED_MM:
        parts.append(f"{mm_words(dx)} {_side('x', -dx)}")
    if abs(dz) >= MIN_REPORTED_MM:
        parts.append(f"{mm_words(dz)} {_side('z', -dz)}")
    if not parts:
        return "move onto the marked site"
    return "move about " + " and ".join(parts)


def _angle_fix(deg: float, tilt: str | None, with_number: bool = True) -> str:
    fix = _TILT_FIX.get(tilt or "", "line the trocar up with the planned path")
    return f"{fix}, you were {deg_words(deg)} off" if with_number else fix


def _depth_fix(err: float) -> str:
    if err < 0:
        return f"go {mm_words(err)} deeper"
    return f"stop {mm_words(err)} sooner, you went too deep"


def analyze(event: Any, attempt: int = 1) -> PlacementFinding:
    """One `port_placed` event (scoring.Event or dict) -> deterministic finding."""
    port = _attr(event, "object_id") or "unknown"
    src, tgt = _attr(event, "source"), _attr(event, "target")
    dx = dz = mm = None
    direction = None
    if src is not None and tgt is not None:
        mm = math.dist(src, tgt) * 1000
        dx = (src[0] - tgt[0]) * 1000
        dz = (src[2] - tgt[2]) * 1000
        sides = [_side(a, v) for a, v in (("x", dx), ("z", dz)) if abs(v) >= MIN_REPORTED_MM]
        direction = " and ".join(sides) or None
    value = _attr(event, "value")
    deg = abs(value) if value is not None else None
    detail = parse_detail(_attr(event, "detail"))
    tilt = detail.get("tilt") if detail.get("tilt") in _TILT_FIX else None
    depth = _depth(detail)
    ideal, tol = DEPTH_TARGETS_MM.get(port, (None, None))
    depth_err = depth - ideal if depth is not None and ideal is not None else None

    location_ok = None if mm is None else mm <= PORT_MAX_MM
    angle_ok = None if deg is None else deg <= PORT_MAX_DEG
    depth_ok = None if depth_err is None else abs(depth_err) <= tol

    ratios = {}
    if location_ok is False:
        ratios["location"] = mm / PORT_MAX_MM
    if angle_ok is False:
        ratios["angle"] = deg / PORT_MAX_DEG
    if depth_ok is False:
        ratios["depth"] = abs(depth_err) / tol
    worst = max(ratios, key=ratios.get) if ratios else None

    if worst == "location":
        correction = _location_fix(dx, dz)
    elif worst == "angle":
        correction = _angle_fix(deg, tilt)
    elif worst == "depth":
        correction = _depth_fix(depth_err)
    else:
        correction = "keep the same setup"

    return PlacementFinding(
        port=port, name=PORT_NAMES.get(port, port.replace("_", " ") + " port"),
        t_ms=_attr(event, "t_ms") or 0, event_id=_attr(event, "event_id"), attempt=attempt,
        offset_mm=_r(mm), offset_head_feet_mm=_r(dx), offset_left_right_mm=_r(dz), direction=direction,
        location_ok=location_ok, angle_deg=_r(deg), tilt=tilt, angle_ok=angle_ok,
        depth_mm=_r(depth), ideal_depth_mm=ideal, depth_tolerance_mm=tol, depth_error_mm=_r(depth_err),
        depth_ok=depth_ok, passed=not ratios, worst=worst, correction=correction,
    )


def _r(v: float | None) -> float | None:
    return None if v is None else round(v, 1)


def _sentence(text: str) -> str:
    text = text.strip()
    return text[0].upper() + text[1:] + ("" if text.endswith(".") else ".")


def _miss(f: PlacementFinding, criterion: str) -> str:
    """What went wrong for one criterion, without the fix."""
    if criterion == "location":
        where = f", {f.direction}" if f.direction else ""
        return f"{f.name} was {mm_words(f.offset_mm)} off the site{where}"
    if criterion == "angle":
        return f"{f.name} angle was {deg_words(f.angle_deg)} off"
    err = f.depth_error_mm
    return f"{f.name} was {mm_words(err)} too {'shallow' if err < 0 else 'deep'}"


def _fix(f: PlacementFinding, criterion: str) -> str:
    if criterion == "location":
        return _location_fix(f.offset_head_feet_mm or 0.0, f.offset_left_right_mm or 0.0)
    if criterion == "angle":  # the miss sentence already says how many degrees
        return _angle_fix(f.angle_deg, f.tilt, with_number=False)
    return _depth_fix(f.depth_error_mm)


def _failed(f: PlacementFinding) -> list[str]:
    """Failed criteria, worst first."""
    order = [f.worst] if f.worst else []
    for c, ok in (("location", f.location_ok), ("angle", f.angle_ok), ("depth", f.depth_ok)):
        if ok is False and c not in order:
            order.append(c)
    return order


def improvement(current: PlacementFinding, previous: PlacementFinding | None,
                label: str = "last try") -> str | None:
    """Compare against an earlier placement of the same port: location first, then angle, then depth."""
    if previous is None:
        return None
    if current.offset_mm is not None and previous.offset_mm is not None:
        delta = previous.offset_mm - current.offset_mm
        if abs(delta) >= MIN_IMPROVEMENT_MM:
            return (f"{mm_words(delta)} closer than {label}" if delta > 0
                    else f"{mm_words(delta)} further off than {label}")
    if current.angle_deg is not None and previous.angle_deg is not None:
        delta = previous.angle_deg - current.angle_deg
        if abs(delta) >= 3:
            return (f"angle {deg_words(delta)} better than {label}" if delta > 0
                    else f"angle {deg_words(delta)} worse than {label}")
    if current.depth_error_mm is not None and previous.depth_error_mm is not None:
        delta = abs(previous.depth_error_mm) - abs(current.depth_error_mm)
        if abs(delta) >= MIN_IMPROVEMENT_MM:
            return (f"depth {mm_words(delta)} nearer target than {label}" if delta > 0
                    else f"depth {mm_words(delta)} further from target than {label}")
    return None


def _fit(sentences: list[str], limit: int = MAX_CUE_WORDS) -> str:
    """Keep sentences in priority order while the cue stays within the word budget (first always kept)."""
    out: list[str] = []
    for s in sentences:
        if not s:
            continue
        s = _sentence(s)
        if out and count_words(" ".join(out + [s])) > limit:
            continue
        out.append(s)
    return " ".join(out)


def cue(f: PlacementFinding, previous: PlacementFinding | None = None, label: str = "last try") -> str:
    """Immediate one-line spoken cue for a placement, at most MAX_CUE_WORDS words."""
    better = improvement(f, previous, label)
    if f.passed:
        bits = [f"{mm_words(f.offset_mm)} from the site" if f.offset_mm is not None else None,
                f"{deg_words(f.angle_deg)} off angle" if f.angle_deg is not None else None]
        bits = [b for b in bits if b]
        head = f"{f.name} is good" + (", " + ", ".join(bits) if bits else "")
        return _fit([head, better])
    failed = _failed(f)
    first = failed[0]
    sentences = [_miss(f, first), _fix(f, first), better]
    for c in failed[1:]:
        sentences.append(f"also {_fix(f, c)}")
    return _fit(sentences)


def _headline_miss(f: PlacementFinding) -> str:
    c = f.worst
    if c == "location":
        return f"the {f.name}, {mm_words(f.offset_mm)}" + (f" {f.direction}" if f.direction else " off the site")
    if c == "angle":
        return f"the {f.name}, {deg_words(f.angle_deg)} off angle"
    err = f.depth_error_mm
    return f"the {f.name}, {mm_words(err)} too {'shallow' if err < 0 else 'deep'}"


def _severity(f: PlacementFinding) -> float:
    r = []
    if f.offset_mm is not None:
        r.append(f.offset_mm / PORT_MAX_MM)
    if f.angle_deg is not None:
        r.append(f.angle_deg / PORT_MAX_DEG)
    if f.depth_error_mm is not None and f.depth_tolerance_mm:
        r.append(abs(f.depth_error_mm) / f.depth_tolerance_mm)
    return max(r, default=0.0)


def debrief(latest: list[PlacementFinding], improvements: list[tuple[PlacementFinding, str]] | None = None) -> dict:
    """Spoken summary after all ports. `latest` = final attempt per port; returns text plus structure."""
    if not latest:
        text = "No port placements were recorded, so there is nothing to coach yet."
        return {"text": text, "lines": [text], "in_tolerance": 0, "total": 0, "worst_port": None,
                "note": DISCLAIMER}
    ok = [f for f in latest if f.passed]
    total = len(latest)
    noun = "ports" if all(f.port != "veress" for f in latest) else "entry sites"
    if total == 1:
        noun = noun[:-1] if noun == "ports" else "entry site"
    lines = []
    failed = sorted([f for f in latest if not f.passed], key=_severity, reverse=True)
    if not failed:
        best = min(latest, key=_severity)
        if total == 1:
            lines.append(f"The {best.name} was in tolerance"
                         + (f", {mm_words(best.offset_mm)} from its site" if best.offset_mm is not None else ""))
        else:
            lines.append(f"All {words(total)} {noun} in tolerance")
            if best.offset_mm is not None:
                lines.append(f"Best was the {best.name}, {mm_words(best.offset_mm)} from its site")
        lines.append("Keep the same setup next time")
        worst = None
    else:
        worst = failed[0]
        lines.append(f"{words(len(ok)).capitalize()} of {words(total)} {noun} in tolerance")
        lines.append(f"Biggest miss was {_headline_miss(worst)}")
        lines.append(f"Next time, {_fix(worst, worst.worst)}")
        if len(failed) > 1:
            lines.append("Also recheck the " + " and the ".join(f.name for f in failed[1:]))
    if improvements:
        f, phrase = improvements[0]
        lines.append(f"Your {f.name} was {phrase}")
    lines.append(DISCLAIMER)
    lines = [_sentence(l) for l in lines]
    return {
        "text": " ".join(lines), "lines": lines, "in_tolerance": len(ok), "total": total,
        "worst_port": worst.port if worst else None, "note": DISCLAIMER,
    }


def _ports(events: Iterable[Any]) -> list[Any]:
    evs = [e for e in events if _attr(e, "kind") == "port_placed"]
    return sorted(evs, key=lambda e: _attr(e, "t_ms") or 0)


def coach_placements(events: Iterable[Any], previous_session_events: Iterable[Any] | None = None) -> dict:
    """All `port_placed` events of a session -> {cues, debrief, findings}.

    A repeated `port_placed` for the same port is a retry; its cue compares with the previous try.
    The first try of each port compares with that port's final placement in the previous session,
    when one is given.
    """
    prior: dict[str, PlacementFinding] = {}
    for e in _ports(previous_session_events or []):
        f = analyze(e)
        prior[f.port] = f
    seen: dict[str, PlacementFinding] = {}
    attempts: dict[str, int] = {}
    cues, findings = [], []
    for e in _ports(events):
        port = _attr(e, "object_id") or "unknown"
        attempts[port] = attempts.get(port, 0) + 1
        f = analyze(e, attempts[port])
        if port in seen:
            prev, label = seen[port], "last try"
        else:
            prev, label = prior.get(port), "last session"
        text = cue(f, prev, label)
        cues.append({"text": text, "port": port, "kind": "praise" if f.passed else "correction",
                     "attempt": f.attempt, "event_id": f.event_id, "t_ms": f.t_ms})
        findings.append(f)
        seen[port] = f
    latest = list(seen.values())
    gains = []
    for f in latest:
        first = next((g for g in findings if g.port == f.port), None)
        base, label = (first, "on your first try") if first is not f else (prior.get(f.port), "last session")
        if base is None or base.offset_mm is None or f.offset_mm is None:
            continue
        delta = base.offset_mm - f.offset_mm
        if delta >= MIN_IMPROVEMENT_MM:
            gains.append((delta, f, f"{mm_words(delta)} closer than {label}"))
    gains.sort(key=lambda g: g[0], reverse=True)
    return {
        "cues": cues,
        "debrief": debrief(latest, [(f, p) for _, f, p in gains]),
        "findings": [f.to_dict() for f in findings],
        "thresholds": {"location_mm": PORT_MAX_MM, "angle_deg": PORT_MAX_DEG,
                       "depth_mm": {p: {"ideal": i, "tolerance": t} for p, (i, t) in DEPTH_TARGETS_MM.items()},
                       "note": DISCLAIMER},
    }


__all__ = ["analyze", "cue", "debrief", "coach_placements", "parse_detail", "words", "DEPTH_TARGETS_MM",
           "PlacementFinding", "DISCLAIMER"]
