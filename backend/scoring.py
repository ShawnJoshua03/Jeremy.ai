"""Deterministic performance metrics. No AI involved; see docs in README of this module.

Practice score (illustrative, NOT clinically validated), clamped to [0, 100]:
    score = 100 * (0.45*completion + 0.25*path_efficiency + 0.20*time_factor + 0.10*error_factor)
    time_factor  = clamp(1 - elapsed_s / TIME_BUDGET_S, 0, 1)
    error_factor = clamp(1 - errors / ERROR_BUDGET, 0, 1)

Path efficiency = ideal / actual, where ideal is the sum of straight-line
source->target distances of successful transfers and actual is the travelled
distance of the instrument tips (valid samples only, gaps not bridged).
"""
from __future__ import annotations

import math
from dataclasses import dataclass, field

TOTAL_OBJECTS = 6
TIME_BUDGET_S = 300.0
ERROR_BUDGET = 6
MAX_GAP_MS = 250  # larger gaps between consecutive samples are not bridged


@dataclass
class Sample:
    instrument: str
    index: int
    t_ms: int
    pos: tuple[float, float, float]
    valid: bool = True


@dataclass
class Event:
    kind: str  # transfer_success | drop | invalid_release
    t_ms: int
    instrument: str | None = None
    object_id: str | None = None
    source: tuple[float, float, float] | None = None
    target: tuple[float, float, float] | None = None
    value: float | None = None  # lap_chole only (docs/LAP_CHOLE.md)
    detail: str | None = None
    event_id: str | None = None


@dataclass
class Metrics:
    completion_pct: float
    successful_transfers: int
    drops: int
    invalid_releases: int
    elapsed_s: float
    distance_by_instrument: dict[str, float] = field(default_factory=dict)
    total_distance_m: float = 0.0
    path_efficiency: float = 0.0
    practice_score: float = 0.0
    difficulty: str = "normal"


def _clamp(v: float, lo: float, hi: float) -> float:
    return max(lo, min(hi, v))


def travel_distance(samples: list[Sample]) -> float:
    """Sum of Euclidean steps between consecutive valid samples of one instrument."""
    ordered = sorted(samples, key=lambda s: s.index)
    total = 0.0
    prev: Sample | None = None
    for s in ordered:
        if not s.valid:
            prev = None  # tracking loss breaks the path
            continue
        if prev is not None and 0 < s.t_ms - prev.t_ms <= MAX_GAP_MS:
            total += math.dist(prev.pos, s.pos)
        prev = s
    return total


def compute_metrics(samples: list[Sample], events: list[Event], elapsed_s: float) -> Metrics:
    transfers = [e for e in events if e.kind == "transfer_success"]
    # an object can never be scored twice
    seen: set[str] = set()
    unique = []
    for e in transfers:
        key = e.object_id or f"anon-{e.t_ms}"
        if key not in seen:
            seen.add(key)
            unique.append(e)
    drops = sum(1 for e in events if e.kind == "drop")
    invalid = sum(1 for e in events if e.kind == "invalid_release")

    by_inst: dict[str, list[Sample]] = {}
    for s in samples:
        by_inst.setdefault(s.instrument, []).append(s)
    dist = {k: travel_distance(v) for k, v in by_inst.items()}
    total = sum(dist.values())

    ideal = sum(math.dist(e.source, e.target) for e in unique if e.source and e.target)
    efficiency = _clamp(ideal / total, 0.0, 1.0) if total > 0 else 0.0

    completion = _clamp(len(unique) / TOTAL_OBJECTS, 0.0, 1.0)
    time_factor = _clamp(1 - elapsed_s / TIME_BUDGET_S, 0.0, 1.0)
    error_factor = _clamp(1 - (drops + invalid) / ERROR_BUDGET, 0.0, 1.0)
    score = 100 * (0.45 * completion + 0.25 * efficiency + 0.20 * time_factor + 0.10 * error_factor)

    return Metrics(
        completion_pct=round(completion * 100, 1),
        successful_transfers=len(unique),
        drops=drops,
        invalid_releases=invalid,
        elapsed_s=elapsed_s,
        distance_by_instrument=dist,
        total_distance_m=total,
        path_efficiency=efficiency,
        practice_score=round(_clamp(score, 0, 100), 1),
    )


# ---------- wound closure ----------
# Wound closure (docs/WOUND_CLOSURE.md). Prototype rubric, NOT clinically validated.
#
# Geometry is board-local metres and must match Unity's WoundPad. A site scores once
# (first stitch_placed per site). Bite error is computed here from each placed
# stitch's source (entry hit) and target (exit hit) against the site points; each
# entry and each exit is one bite, and mean_bite_error_mm averages all bites.
#
#     spacing_sd_mm   = population SD of the z-ordered distances between consecutive
#                       placed stitch centres (midpoint of entry/exit hits); null if
#                       fewer than 3 stitches are placed (fewer than 2 gaps).
#     ideal path      = sum of entry->exit bite lengths
#                       + n_hands * sum of travel from one stitch's exit to the next
#                         stitch's entry (time order), n_hands = tracked instruments
#                         (1 or 2): the forceps follows the needle driver along the wound.
#     path_efficiency = clamp(ideal / travelled, 0, 1), travelled as in peg transfer.
#     bite_accuracy   = clamp(1 - mean_bite_error_mm / BITE_MAX_MM, 0, 1); 0 with no stitches.
#     score = 100 * (0.40*completion + 0.20*bite_accuracy + 0.15*path_efficiency
#                    + 0.15*time_factor + 0.10*error_factor)
#     errors = missed stitches + tissue contacts, budget WC_ERROR_BUDGET.
#
# Mapping into the shared metric fields: successful_transfers = stitches placed,
# drops = tissue contacts, invalid_releases = missed stitches.
#

WC_SITES = 5
WC_PAD_TOP_Y = 0.012
WC_SITE_Z = [round(-0.05 + 0.025 * i, 6) for i in range(WC_SITES)]
WC_ENTRY = [(-0.008, WC_PAD_TOP_Y, z) for z in WC_SITE_Z]
WC_EXIT = [(0.008, WC_PAD_TOP_Y, z) for z in WC_SITE_Z]
BITE_GOOD_MM = 4.0
BITE_MAX_MM = 8.0
WC_TIME_BUDGET_S = 300.0
WC_ERROR_BUDGET = 5

WOUND_CLOSURE_KINDS = frozenset({"stitch_placed", "stitch_missed", "tissue_contact"})
PEG_TRANSFER_KINDS = frozenset({"transfer_success", "drop", "invalid_release"})
LAP_CHOLE_KINDS = frozenset({"step_completed", "port_placed", "insufflation", "critical_view",
                             "clip_applied", "structure_cut", "ductotomy", "cholangiogram", "injury", "hand_collision"})
PORT_PLACEMENT_KINDS = frozenset({"port_placed", "landmark"})
# CVS trainer (docs/CVS_SCORING.md). The procedure kinds are reserved for upcoming steps: accepted,
# recorded and replayed, not scored yet.
LAP_CHOLE_CVS_KINDS = frozenset({"exercise_started", "mode_changed", "structure_hovered", "structure_selected",
                                 "decision_submitted", "instructor_requested",
                                 "step_completed", "clip_applied", "structure_cut", "injury", "critical_view"})
EVENT_KINDS = {"peg_transfer": PEG_TRANSFER_KINDS, "wound_closure": WOUND_CLOSURE_KINDS,
               "lap_chole": LAP_CHOLE_KINDS, "port_placement": PORT_PLACEMENT_KINDS,
               "lap_chole_cvs": LAP_CHOLE_CVS_KINDS}


@dataclass
class WoundClosureMetrics(Metrics):
    exercise: str = "wound_closure"
    mean_bite_error_mm: float | None = None
    spacing_sd_mm: float | None = None


def site_index(object_id: str | None, source: tuple[float, float, float] | None) -> int | None:
    """site_N -> N-1; otherwise the site nearest the entry hit; None if neither is known."""
    if object_id and object_id.startswith("site_"):
        try:
            n = int(object_id[5:])
        except ValueError:
            n = 0
        if 1 <= n <= WC_SITES:
            return n - 1
    if source is not None:
        return min(range(WC_SITES), key=lambda i: math.dist(source, WC_ENTRY[i]))
    return None


def _sd(values: list[float]) -> float:
    mean = sum(values) / len(values)
    return math.sqrt(sum((v - mean) ** 2 for v in values) / len(values))


def compute_wound_metrics(samples: list[Sample], events: list[Event], elapsed_s: float) -> WoundClosureMetrics:
    placed: dict[int, Event] = {}
    anon = 0
    for e in sorted((e for e in events if e.kind == "stitch_placed"), key=lambda e: e.t_ms):
        i = site_index(e.object_id, e.source)
        if i is None:
            anon += 1  # placed but unlocatable: counts, no geometry
            continue
        placed.setdefault(i, e)
    n_placed = min(WC_SITES, len(placed) + anon)
    missed = sum(1 for e in events if e.kind == "stitch_missed")
    contacts = sum(1 for e in events if e.kind == "tissue_contact")

    bites: list[float] = []
    for i, e in placed.items():
        if e.source:
            bites.append(math.dist(e.source, WC_ENTRY[i]) * 1000)
        if e.target:
            bites.append(math.dist(e.target, WC_EXIT[i]) * 1000)
    mean_bite = round(sum(bites) / len(bites), 2) if bites else None

    def centre(i: int, e: Event) -> tuple[float, float, float]:
        if e.source and e.target:
            return tuple((a + b) / 2 for a, b in zip(e.source, e.target))
        return tuple((a + b) / 2 for a, b in zip(WC_ENTRY[i], WC_EXIT[i]))

    centres = sorted((centre(i, e) for i, e in placed.items()), key=lambda p: p[2])
    gaps = [math.dist(a, b) * 1000 for a, b in zip(centres, centres[1:])]
    spacing_sd = round(_sd(gaps), 2) if len(gaps) >= 2 else None

    by_inst: dict[str, list[Sample]] = {}
    for s in samples:
        by_inst.setdefault(s.instrument, []).append(s)
    dist = {k: travel_distance(v) for k, v in by_inst.items()}
    total = sum(dist.values())
    hands = max(1, min(2, sum(1 for d in dist.values() if d > 0)))
    in_time = sorted(placed.items(), key=lambda kv: kv[1].t_ms)
    pts = [(e.source or WC_ENTRY[i], e.target or WC_EXIT[i]) for i, e in in_time]
    ideal = sum(math.dist(a, b) for a, b in pts)
    ideal += hands * sum(math.dist(prev[1], nxt[0]) for prev, nxt in zip(pts, pts[1:]))
    efficiency = _clamp(ideal / total, 0.0, 1.0) if total > 0 else 0.0

    completion = _clamp(n_placed / WC_SITES, 0.0, 1.0)
    accuracy = _clamp(1 - mean_bite / BITE_MAX_MM, 0.0, 1.0) if mean_bite is not None else 0.0
    time_factor = _clamp(1 - elapsed_s / WC_TIME_BUDGET_S, 0.0, 1.0)
    error_factor = _clamp(1 - (missed + contacts) / WC_ERROR_BUDGET, 0.0, 1.0)
    score = 100 * (0.40 * completion + 0.20 * accuracy + 0.15 * efficiency
                   + 0.15 * time_factor + 0.10 * error_factor)

    return WoundClosureMetrics(
        completion_pct=round(completion * 100, 1),
        successful_transfers=n_placed,
        drops=contacts,
        invalid_releases=missed,
        elapsed_s=elapsed_s,
        distance_by_instrument=dist,
        total_distance_m=total,
        path_efficiency=efficiency,
        practice_score=round(_clamp(score, 0, 100), 1),
        mean_bite_error_mm=mean_bite,
        spacing_sd_mm=spacing_sd,
    )


def compute_for_exercise(exercise: str, samples: list[Sample], events: list[Event], elapsed_s: float,
                         difficulty: str = "normal") -> Metrics:
    if exercise == "wound_closure":
        result = compute_wound_metrics(samples, events, elapsed_s)
    elif exercise == "lap_chole":
        result = compute_lap_chole_metrics(samples, events, elapsed_s, difficulty)
    elif exercise == "port_placement":
        result = compute_port_placement_metrics(samples, events, elapsed_s, difficulty)
    elif exercise == "lap_chole_cvs":
        import cvs_scoring  # imports this module
        result = cvs_scoring.compute_metrics(samples, events, elapsed_s)
    else:
        result = compute_metrics(samples, events, elapsed_s)
    result.difficulty = difficulty
    return result


# ---------- laparoscopic cholecystectomy ----------
# docs/LAP_CHOLE.md. Prototype rubric, NOT clinically validated.
#
# Events carry both the actual and the ideal position, so no anatomy lives here.
# Safety is decided from the event sequence (lap_chole_findings):
#   major: every `injury`; every clip or cut on `cbd`; a cut of the cystic duct or artery
#          without >= 2 proximal clips (smaller distance) and >= 1 distal clip (larger distance),
#          each at least 2 mm from the cut and applied before it ("unsafe_division"); a cystic
#          cut before the critical view of safety was reached ("division_before_cvs"). One cut
#          can be both unsafe and early (two major errors). A CBD cut counts once (cbd_cut).
#          A `ductotomy` (partial cut for the cholangiogram, not a division) on anything but the
#          cystic duct ("ductotomy_wrong_structure"), without an earlier cystic-duct clip distal to
#          it, i.e. a larger distance ("unsafe_ductotomy"), or before the critical view
#          ("ductotomy_before_cvs"). The distal clip from clip_duct_distal counts toward the
#          division rule; the ductotomy sits between it and the cut.
#   minor: a port > 15 mm from its site or > 20 degrees off (once per port event); a final
#          insufflation pressure outside 12-15 mmHg (last `insufflation` event); a `crossing`
#          clip; a clip on the cut structure closer than 2 mm to the cut (once per clip); a
#          `cholangiogram` with detail `not_in_duct` or `leak`.
#   Crossing clips still count toward the 2 + 1 clip rule (the contract does not exclude them).
#   Critical view reached = the earlier of `step_completed` dissect and a `critical_view`
#   event with value >= 100.
#
#     completion      = unique known steps completed / 15
#     port_accuracy   = mean over port events of clamp(1 - 0.5*err_mm/15 - 0.5*|angle|/20, 0, 1); 0 if none
#     insufflation_ok = 1 if the final pressure is within 12-15 mmHg, else 0 (0 if not measured)
#     critical_view   = max critical_view value / 100 (0 if none)
#     ideal path      = straight-line polyline through the positional events in time order (port
#                       sites, clip, ductotomy and cut positions); efficiency = clamp(ideal / travelled, 0, 1)
#     time_factor     = clamp(1 - elapsed_s / 600, 0, 1)
#     minor_factor    = clamp(1 - minor / 5, 0, 1)
#     score = 100 * (0.35*completion + 0.15*port_accuracy + 0.10*insufflation_ok + 0.10*critical_view
#                    + 0.10*path_efficiency + 0.10*time_factor + 0.10*minor_factor)
#     Any major error caps the score at LC_MAJOR_CAP (40).
#
# Mapping: successful_transfers = steps completed, drops = major errors,
# invalid_releases = minor errors. Exercise-specific numbers go in `details`.

LC_STEPS = ["veress", "insufflate", "port_umbilical", "port_epigastric", "port_rsc_medial", "port_rsc_lateral",
            "retract", "dissect", "clip_duct_distal", "ductotomy", "cholangiogram", "clip_duct_proximal",
            "divide_duct", "clip_artery", "divide_artery"]
LC_PORTS = ("veress", "umbilical", "epigastric", "rsc_medial", "rsc_lateral")
LC_STRUCTURES = ("cystic_duct", "cystic_artery", "cbd")
PORT_MAX_MM = 15.0
PORT_MAX_DEG = 20.0
DIFFICULTY = {"normal": 1.0, "demo": 2.0}
DEMO_NOTE = "demo tolerances: twice as forgiving"
# Demo also leaves out the optional cholangiogram (and the ductotomy it needs); completion is out of the remaining steps.
DEMO_SKIPPED_STEPS = ("ductotomy", "cholangiogram")


def lap_chole_steps(difficulty: str = "normal") -> list[str]:
    return [s for s in LC_STEPS if not (difficulty == "demo" and s in DEMO_SKIPPED_STEPS)]


def tolerance_multiplier(difficulty: str = "normal") -> float:
    return DIFFICULTY[difficulty]


def port_limits(difficulty: str = "normal") -> tuple[float, float]:
    multiplier = tolerance_multiplier(difficulty)
    return PORT_MAX_MM * multiplier, PORT_MAX_DEG * multiplier


def difficulty_note(difficulty: str = "normal") -> str:
    return DEMO_NOTE if difficulty == "demo" else ""


INSUFFLATION_MMHG = (12.0, 15.0)
CUT_CLIP_MARGIN_MM = 2.0
CVS_REACHED_PCT = 100.0
LC_TIME_BUDGET_S = 600.0
LC_MINOR_BUDGET = 5
LC_MAJOR_CAP = 40.0
CLIPPING_CODES = ("cbd_clip", "cbd_cut", "unsafe_division", "division_before_cvs", "clip_crossing", "clip_too_close",
                  "ductotomy_wrong_structure", "unsafe_ductotomy", "ductotomy_before_cvs")
CHOLANGIOGRAM_ERRORS = ("not_in_duct", "leak")


@dataclass
class LapCholeMetrics(Metrics):
    exercise: str = "lap_chole"
    mean_bite_error_mm: float | None = None
    spacing_sd_mm: float | None = None
    details: dict | None = None


@dataclass
class Finding:
    severity: str  # major | minor
    code: str  # injury | cbd_clip | cbd_cut | unsafe_division | division_before_cvs | port_offset |
    #            insufflation_out_of_range | clip_crossing | clip_too_close | ductotomy_wrong_structure |
    #            unsafe_ductotomy | ductotomy_before_cvs | cholangiogram_not_in_duct | cholangiogram_leak
    event: Event
    info: dict = field(default_factory=dict)


def port_error(e: Event) -> tuple[float | None, float | None]:
    """(distance from the ideal site in mm, |angle error| in degrees); None where not sent."""
    mm = math.dist(e.source, e.target) * 1000 if e.source and e.target else None
    deg = abs(e.value) if e.value is not None else None
    return mm, deg


def cvs_reached_ms(events: list[Event]) -> int | None:
    times = [e.t_ms for e in events if e.kind == "step_completed" and e.object_id == "dissect"]
    times += [e.t_ms for e in events
              if e.kind == "critical_view" and e.value is not None and e.value >= CVS_REACHED_PCT]
    return min(times) if times else None


def lap_chole_findings(events: list[Event], difficulty: str = "normal") -> list[Finding]:
    """Every major and minor error, in time order. The single source for counts, coach and cost."""
    max_mm, max_deg = port_limits(difficulty)
    ordered = sorted(events, key=lambda e: e.t_ms)
    out: list[Finding] = []
    cvs_ms = cvs_reached_ms(ordered)
    clips = [e for e in ordered if e.kind == "clip_applied"]
    too_close: set[int] = set()
    for e in ordered:
        if e.kind == "injury":
            out.append(Finding("major", "injury", e))
        elif e.kind == "hand_collision":
            out.append(Finding("minor", "hand_collision", e,
                               {"penetration_mm": e.value if e.value is not None else 0}))
        elif e.kind == "port_placed":
            mm, deg = port_error(e)
            if (mm is not None and mm > max_mm) or (deg is not None and deg > max_deg):
                out.append(Finding("minor", "port_offset", e, {"error_mm": mm, "angle_deg": deg}))
        elif e.kind == "clip_applied":
            if e.object_id == "cbd":
                out.append(Finding("major", "cbd_clip", e))
            if (e.detail or "").strip().lower() == "crossing":
                out.append(Finding("minor", "clip_crossing", e))
        elif e.kind == "ductotomy":
            if e.object_id != "cystic_duct":
                out.append(Finding("major", "ductotomy_wrong_structure", e))
            else:
                distal = sum(1 for c in clips if c.object_id == "cystic_duct" and c.t_ms <= e.t_ms
                             and c.value is not None and e.value is not None and c.value > e.value)
                if not distal:
                    out.append(Finding("major", "unsafe_ductotomy", e))
            if cvs_ms is None or e.t_ms < cvs_ms:
                out.append(Finding("major", "ductotomy_before_cvs", e))
        elif e.kind == "cholangiogram":
            result = (e.detail or "").strip().lower()
            if result in CHOLANGIOGRAM_ERRORS:
                out.append(Finding("minor", f"cholangiogram_{result}", e))
        elif e.kind == "structure_cut":
            if e.object_id == "cbd":
                out.append(Finding("major", "cbd_cut", e))
                continue
            before = [c for c in clips
                      if c.object_id == e.object_id and c.t_ms <= e.t_ms and c.value is not None]
            proximal = distal = 0
            if e.value is not None:
                proximal = sum(1 for c in before if c.value <= e.value - CUT_CLIP_MARGIN_MM)
                distal = sum(1 for c in before if c.value >= e.value + CUT_CLIP_MARGIN_MM)
                for c in before:
                    if abs(c.value - e.value) < CUT_CLIP_MARGIN_MM and id(c) not in too_close:
                        too_close.add(id(c))
                        out.append(Finding("minor", "clip_too_close", c, {"cut_t_ms": e.t_ms}))
            if proximal < 2 or distal < 1:
                out.append(Finding("major", "unsafe_division", e, {"proximal": proximal, "distal": distal}))
            if cvs_ms is None or e.t_ms < cvs_ms:
                out.append(Finding("major", "division_before_cvs", e))
    pressures = [e for e in ordered if e.kind == "insufflation" and e.value is not None]
    if pressures:
        last = pressures[-1]
        if not INSUFFLATION_MMHG[0] <= last.value <= INSUFFLATION_MMHG[1]:
            out.append(Finding("minor", "insufflation_out_of_range", last))
    return sorted(out, key=lambda f: f.event.t_ms)


def compute_lap_chole_metrics(samples: list[Sample], events: list[Event], elapsed_s: float,
                              difficulty: str = "normal") -> LapCholeMetrics:
    max_mm, max_deg = port_limits(difficulty)
    ordered = sorted(events, key=lambda e: e.t_ms)
    done = {e.object_id for e in ordered if e.kind == "step_completed" and e.object_id in LC_STEPS}
    findings = lap_chole_findings(ordered, difficulty)
    codes = [f.code for f in findings]
    major = sum(1 for f in findings if f.severity == "major")
    minor = len(findings) - major

    ports = [e for e in ordered if e.kind == "port_placed"]
    errs = [port_error(e) for e in ports]
    port_mm = [mm for mm, _ in errs if mm is not None]
    port_deg = [deg for _, deg in errs if deg is not None]
    per_port = [_clamp(1 - 0.5 * (mm or 0) / max_mm - 0.5 * (deg or 0) / max_deg, 0.0, 1.0)
                for mm, deg in errs]
    port_accuracy = sum(per_port) / len(per_port) if per_port else 0.0

    pressures = [e.value for e in ordered if e.kind == "insufflation" and e.value is not None]
    pressure = pressures[-1] if pressures else None
    insufflation_ok = pressure is not None and INSUFFLATION_MMHG[0] <= pressure <= INSUFFLATION_MMHG[1]
    cv_values = [e.value for e in ordered if e.kind == "critical_view" and e.value is not None]
    cv_pct = _clamp(max(cv_values), 0.0, 100.0) if cv_values else None

    cuts = [e for e in ordered if e.kind == "structure_cut" and e.object_id in ("cystic_duct", "cystic_artery")]
    flagged = {id(f.event) for f in findings if f.code in ("unsafe_division", "division_before_cvs")}

    by_inst: dict[str, list[Sample]] = {}
    for s in samples:
        by_inst.setdefault(s.instrument, []).append(s)
    dist = {k: travel_distance(v) for k, v in by_inst.items()}
    total = sum(dist.values())
    pts = []
    for e in ordered:
        if e.kind == "port_placed" and (e.target or e.source):
            pts.append(e.target or e.source)
        elif e.kind in ("clip_applied", "ductotomy", "structure_cut") and e.source:
            pts.append(e.source)
    ideal = sum(math.dist(a, b) for a, b in zip(pts, pts[1:]))
    efficiency = _clamp(ideal / total, 0.0, 1.0) if total > 0 else 0.0

    steps = lap_chole_steps(difficulty)
    done_in_scope = {d for d in done if d in steps}
    completion = _clamp(len(done_in_scope) / len(steps), 0.0, 1.0)
    time_factor = _clamp(1 - elapsed_s / LC_TIME_BUDGET_S, 0.0, 1.0)
    minor_factor = _clamp(1 - minor / LC_MINOR_BUDGET, 0.0, 1.0)
    score = 100 * (0.35 * completion + 0.15 * port_accuracy + 0.10 * (1.0 if insufflation_ok else 0.0)
                   + 0.10 * ((cv_pct or 0.0) / 100) + 0.10 * efficiency + 0.10 * time_factor
                   + 0.10 * minor_factor)
    score = _clamp(score, 0, 100)
    if major:
        score = min(score, LC_MAJOR_CAP)

    details: dict = {"steps_completed": len(done_in_scope), "steps_total": len(steps), "ports_placed": len(ports)}
    if port_mm:
        details["ports_mean_error_mm"] = round(sum(port_mm) / len(port_mm), 2)
        details["ports_max_error_mm"] = round(max(port_mm), 2)
    if port_deg:
        details["ports_mean_angle_deg"] = round(sum(port_deg) / len(port_deg), 2)
    if pressure is not None:
        details["insufflation_mmhg"] = round(pressure, 2)
        details["insufflation_in_range"] = insufflation_ok
    if cv_pct is not None:
        details["critical_view_pct"] = round(cv_pct, 1)
    details["critical_view_reached"] = cvs_reached_ms(ordered) is not None
    details["clips_cystic_duct"] = sum(1 for e in ordered if e.kind == "clip_applied" and e.object_id == "cystic_duct")
    details["clips_cystic_artery"] = sum(1 for e in ordered
                                         if e.kind == "clip_applied" and e.object_id == "cystic_artery")
    details["crossing_clips"] = codes.count("clip_crossing")
    details["safe_divisions"] = sum(1 for e in cuts if id(e) not in flagged)
    details["unsafe_divisions"] = codes.count("unsafe_division")
    details["divisions_before_cvs"] = codes.count("division_before_cvs")
    details["cbd_errors"] = codes.count("cbd_clip") + codes.count("cbd_cut")
    details["injuries"] = codes.count("injury")
    details["clipping_errors"] = sum(1 for c in codes if c in CLIPPING_CODES)
    grams = [e for e in ordered if e.kind == "cholangiogram"]
    details["ductotomies"] = sum(1 for e in ordered if e.kind == "ductotomy")
    details["unsafe_ductotomies"] = sum(codes.count(c) for c in
                                        ("ductotomy_wrong_structure", "unsafe_ductotomy", "ductotomy_before_cvs"))
    if grams:
        last = grams[-1]
        if last.value is not None:
            details["cholangiogram_depth_mm"] = round(last.value, 1)
        details["cholangiogram_result"] = (last.detail or "ok").strip().lower()[:64]
    details["cholangiogram_errors"] = codes.count("cholangiogram_not_in_duct") + codes.count("cholangiogram_leak")
    details["bile_duct_injury"] = bool(details["cbd_errors"]) or any(
        f.event.object_id == "cbd" and f.code in ("injury", "ductotomy_wrong_structure") for f in findings)
    details["major_errors"] = major
    details["minor_errors"] = minor

    return LapCholeMetrics(
        difficulty=difficulty,
        completion_pct=round(completion * 100, 1),
        successful_transfers=len(done_in_scope),
        drops=major,
        invalid_releases=minor,
        elapsed_s=elapsed_s,
        distance_by_instrument=dist,
        total_distance_m=total,
        path_efficiency=efficiency,
        practice_score=round(score, 1),
        details=details,
    )


# ---------- port placement ----------
# docs/PORT_PLACEMENT.md. Prototype rubric, NOT clinically validated. Only two numbers are cited:
#   Hanna, Shimi, Cuschieri. Surg Endosc 1997;11(4):397-401: a manipulation angle (between the two
#     working instruments) of 45-75 degrees with equal azimuth angles; 60 degrees performed best.
#   Saber et al. Ann Surg 2004;239(2):182-185: the epigastric vessels usually lie 4-8 cm from the midline.
# Every other tolerance, weight, budget and the cap is a prototype choice.
#
# Ports follow the standard four-port layout with the surgeon on the patient's left: umbilical = camera,
# epigastric = surgeon's right hand, rsc_medial = surgeon's left hand, rsc_lateral = assistant.
# The last port_placed per port counts (a retry replaces the earlier placement); sequence checks use
# the first. Landmarks arrive as `landmark` events: object_id `target` (Calot's triangle, inside the
# abdomen) and `midline` (any point on the midline; only its z is used, +z is the patient's right).
#
# Findings (port_placement_findings):
#   major: a skin entry 40-80 mm from the midline (epigastric_vessel_zone); a working port placed
#          before the camera port (working_port_before_camera); a working port sent with detail
#          "blind", i.e. not under camera view (blind_working_port).
#   minor: port_offset (> 15 mm or > 20 degrees, as lap_chole, once per port); two ports closer than
#          50 mm (ports_crowded, once per pair).
#
#     manipulation_deg = 3D angle at the target between target->epigastric and target->rsc_medial
#     azimuth_*_deg    = 3D angle at the target between the camera port and that working port. Hanna
#                        measures it in the horizontal plane; with ports around a deep target (lap chole)
#                        that projection is meaningless, so only the left/right difference is scored.
#                        The absolute ~30 degrees of the "baseball diamond" is not reachable here.
#     elevation_*_deg  = angle of target->port above the x-z plane (reported only, not scored)
#     site_accuracy    = mean over placed ports of clamp(1 - 0.5*err_mm/15 - 0.5*|angle|/20, 0, 1)
#     completion       = required ports placed / 4
#     triangulation    = 1 inside 45-75 degrees, falling linearly to 0 at 30 and 90; 0 if not measurable
#     azimuth_symmetry = clamp(1 - |azimuth_right - azimuth_left| / 30, 0, 1); 0 if not measurable
#     time_factor      = clamp(1 - elapsed_s / 300, 0, 1)
#     minor_factor     = clamp(1 - minor / 3, 0, 1)
#     score = 100 * (0.35*site_accuracy + 0.20*completion + 0.20*triangulation + 0.10*azimuth_symmetry
#                    + completion*(0.10*minor_factor + 0.05*time_factor))
#     The last two terms scale with completion so an empty or quick, unfinished attempt earns nothing
#     for being fast and error-free. Any major error caps the score at PP_MAJOR_CAP (40).
#     ideal path = straight-line polyline through the final port entries in placement order.
#
# Mapping: successful_transfers = ports placed, drops = major errors, invalid_releases = minor errors.

PP_PORTS = ("umbilical", "epigastric", "rsc_medial", "rsc_lateral")
PP_CAMERA = "umbilical"
PP_RIGHT_HAND = "epigastric"
PP_LEFT_HAND = "rsc_medial"
MANIPULATION_DEG = (45.0, 75.0)  # Hanna 1997
MANIPULATION_ZERO_DEG = (30.0, 90.0)  # prototype: score reaches 0 at the worst angles Hanna tested
AZIMUTH_MAX_DIFF_DEG = 30.0
EPIGASTRIC_ZONE_MM = (40.0, 80.0)  # Saber 2004
PORT_MIN_SPACING_MM = 50.0
PP_TIME_BUDGET_S = 300.0
PP_MINOR_BUDGET = 3
PP_MAJOR_CAP = 40.0


@dataclass
class PortPlacementMetrics(Metrics):
    exercise: str = "port_placement"
    mean_bite_error_mm: float | None = None
    spacing_sd_mm: float | None = None
    details: dict | None = None


def _angle_deg(u: tuple[float, ...], v: tuple[float, ...]) -> float | None:
    nu, nv = math.hypot(*u), math.hypot(*v)
    if nu == 0 or nv == 0:
        return None
    cos = sum(a * b for a, b in zip(u, v)) / (nu * nv)
    return math.degrees(math.acos(_clamp(cos, -1.0, 1.0)))


def _sub(a: tuple[float, float, float], b: tuple[float, float, float]) -> tuple[float, float, float]:
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def _landmark(events: list[Event], name: str) -> tuple[float, float, float] | None:
    found = [e.source for e in events if e.kind == "landmark" and e.object_id == name and e.source]
    return found[-1] if found else None


def final_ports(events: list[Event]) -> dict[str, Event]:
    """Port id -> its last port_placed event with a skin entry, for the four required ports."""
    out: dict[str, Event] = {}
    for e in sorted(events, key=lambda e: e.t_ms):
        if e.kind == "port_placed" and e.object_id in PP_PORTS and e.source:
            out[e.object_id] = e
    return out


def port_geometry(events: list[Event]) -> dict[str, float]:
    """Triangulation angles in degrees; a key is missing when its ports or the target are missing."""
    ports = final_ports(events)
    target = _landmark(events, "target")
    out: dict[str, float] = {}
    if target is None:
        return out
    vec = {p: _sub(e.source, target) for p, e in ports.items()}
    if PP_RIGHT_HAND in vec and PP_LEFT_HAND in vec:
        a = _angle_deg(vec[PP_RIGHT_HAND], vec[PP_LEFT_HAND])
        if a is not None:
            out["manipulation_angle_deg"] = a
    if PP_CAMERA in vec:
        for side, p in (("right", PP_RIGHT_HAND), ("left", PP_LEFT_HAND)):
            if p in vec:
                a = _angle_deg(vec[PP_CAMERA], vec[p])
                if a is not None:
                    out[f"azimuth_{side}_deg"] = a
        if "azimuth_right_deg" in out and "azimuth_left_deg" in out:
            out["azimuth_difference_deg"] = abs(out["azimuth_right_deg"] - out["azimuth_left_deg"])
    for side, p in (("right", PP_RIGHT_HAND), ("left", PP_LEFT_HAND)):
        if p in vec and math.hypot(*vec[p]) > 0:
            out[f"elevation_{side}_deg"] = math.degrees(math.atan2(vec[p][1], math.hypot(vec[p][0], vec[p][2])))
    return out


def triangulation_score(manipulation_deg: float | None) -> float:
    if manipulation_deg is None:
        return 0.0
    lo, hi = MANIPULATION_DEG
    zero_lo, zero_hi = MANIPULATION_ZERO_DEG
    if lo <= manipulation_deg <= hi:
        return 1.0
    if manipulation_deg < lo:
        return _clamp((manipulation_deg - zero_lo) / (lo - zero_lo), 0.0, 1.0)
    return _clamp((zero_hi - manipulation_deg) / (zero_hi - hi), 0.0, 1.0)


def port_placement_findings(events: list[Event], difficulty: str = "normal") -> list[Finding]:
    """Every major and minor port-placement error, in time order. The single source for counts and coach."""
    max_mm, max_deg = port_limits(difficulty)
    ordered = sorted(events, key=lambda e: e.t_ms)
    out: list[Finding] = []
    ports = final_ports(ordered)
    midline = _landmark(ordered, "midline")

    camera_ms = next((e.t_ms for e in ordered if e.kind == "port_placed" and e.object_id == PP_CAMERA), None)
    flagged_early: set[str] = set()
    for e in ordered:
        if e.kind != "port_placed" or e.object_id not in PP_PORTS or e.object_id == PP_CAMERA:
            continue
        if (camera_ms is None or e.t_ms < camera_ms) and e.object_id not in flagged_early:
            flagged_early.add(e.object_id)
            out.append(Finding("major", "working_port_before_camera", e))
        if (e.detail or "").strip().lower() == "blind":
            out.append(Finding("major", "blind_working_port", e))

    for e in ports.values():
        if midline is not None:
            lateral_mm = abs(e.source[2] - midline[2]) * 1000
            if EPIGASTRIC_ZONE_MM[0] < lateral_mm < EPIGASTRIC_ZONE_MM[1]:
                out.append(Finding("major", "epigastric_vessel_zone", e, {"lateral_mm": round(lateral_mm, 1)}))
        mm, deg = port_error(e)
        if (mm is not None and mm > max_mm) or (deg is not None and deg > max_deg):
            out.append(Finding("minor", "port_offset", e, {"error_mm": mm, "angle_deg": deg}))

    placed = sorted(ports.values(), key=lambda e: e.t_ms)
    for i, a in enumerate(placed):
        for b in placed[i + 1:]:
            gap_mm = math.dist(a.source, b.source) * 1000
            if gap_mm < PORT_MIN_SPACING_MM:
                out.append(Finding("minor", "ports_crowded", b, {"other": a.object_id, "gap_mm": round(gap_mm, 1)}))
    return sorted(out, key=lambda f: f.event.t_ms)


def compute_port_placement_metrics(samples: list[Sample], events: list[Event],
                                   elapsed_s: float, difficulty: str = "normal") -> PortPlacementMetrics:
    max_mm, max_deg = port_limits(difficulty)
    ordered = sorted(events, key=lambda e: e.t_ms)
    ports = final_ports(ordered)
    findings = port_placement_findings(ordered, difficulty)
    codes = [f.code for f in findings]
    major = sum(1 for f in findings if f.severity == "major")
    minor = len(findings) - major
    geo = port_geometry(ordered)

    errs = {p: port_error(e) for p, e in ports.items()}
    port_mm = [mm for mm, _ in errs.values() if mm is not None]
    port_deg = [deg for _, deg in errs.values() if deg is not None]
    per_port = [_clamp(1 - 0.5 * (mm or 0) / max_mm - 0.5 * (deg or 0) / max_deg, 0.0, 1.0)
                for mm, deg in errs.values()]
    site_accuracy = sum(per_port) / len(per_port) if per_port else 0.0

    triangulation = triangulation_score(geo.get("manipulation_angle_deg"))
    diff = geo.get("azimuth_difference_deg")
    symmetry = _clamp(1 - diff / AZIMUTH_MAX_DIFF_DEG, 0.0, 1.0) if diff is not None else 0.0

    by_inst: dict[str, list[Sample]] = {}
    for s in samples:
        by_inst.setdefault(s.instrument, []).append(s)
    dist = {k: travel_distance(v) for k, v in by_inst.items()}
    total = sum(dist.values())
    pts = [e.source for e in sorted(ports.values(), key=lambda e: e.t_ms)]
    ideal = sum(math.dist(a, b) for a, b in zip(pts, pts[1:]))
    efficiency = _clamp(ideal / total, 0.0, 1.0) if total > 0 else 0.0

    completion = _clamp(len(ports) / len(PP_PORTS), 0.0, 1.0)
    time_factor = _clamp(1 - elapsed_s / PP_TIME_BUDGET_S, 0.0, 1.0)
    minor_factor = _clamp(1 - minor / PP_MINOR_BUDGET, 0.0, 1.0)
    score = 100 * (0.35 * site_accuracy + 0.20 * completion + 0.20 * triangulation + 0.10 * symmetry
                   + completion * (0.10 * minor_factor + 0.05 * time_factor))
    score = _clamp(score, 0, 100)
    if major:
        score = min(score, PP_MAJOR_CAP)

    details: dict = {"ports_placed": len(ports), "ports_total": len(PP_PORTS)}
    if port_mm:
        details["ports_mean_error_mm"] = round(sum(port_mm) / len(port_mm), 2)
        details["ports_max_error_mm"] = round(max(port_mm), 2)
    if port_deg:
        details["ports_mean_angle_deg"] = round(sum(port_deg) / len(port_deg), 2)
    for p, (mm, _) in errs.items():
        if mm is not None:
            details[f"{p}_error_mm"] = round(mm, 2)
    for k, v in geo.items():
        details[k] = round(v, 1)
    if len(pts) >= 2:
        details["min_port_spacing_mm"] = round(
            min(math.dist(a, b) for i, a in enumerate(pts) for b in pts[i + 1:]) * 1000, 1)
    details["triangulation"] = round(triangulation, 3)
    details["azimuth_symmetry"] = round(symmetry, 3)
    details["epigastric_zone_ports"] = codes.count("epigastric_vessel_zone")
    details["sequence_errors"] = codes.count("working_port_before_camera") + codes.count("blind_working_port")
    details["crowded_pairs"] = codes.count("ports_crowded")
    details["major_errors"] = major
    details["minor_errors"] = minor

    return PortPlacementMetrics(
        difficulty=difficulty,
        completion_pct=round(completion * 100, 1),
        successful_transfers=len(ports),
        drops=major,
        invalid_releases=minor,
        elapsed_s=elapsed_s,
        distance_by_instrument=dist,
        total_distance_m=total,
        path_efficiency=efficiency,
        practice_score=round(score, 1),
        details=details,
    )
