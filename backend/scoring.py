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
                             "clip_applied", "structure_cut", "injury"})
EVENT_KINDS = {"peg_transfer": PEG_TRANSFER_KINDS, "wound_closure": WOUND_CLOSURE_KINDS,
               "lap_chole": LAP_CHOLE_KINDS}


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


def compute_for_exercise(exercise: str, samples: list[Sample], events: list[Event], elapsed_s: float) -> Metrics:
    if exercise == "wound_closure":
        return compute_wound_metrics(samples, events, elapsed_s)
    if exercise == "lap_chole":
        return compute_lap_chole_metrics(samples, events, elapsed_s)
    return compute_metrics(samples, events, elapsed_s)


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
#   minor: a port > 15 mm from its site or > 20 degrees off (once per port event); a final
#          insufflation pressure outside 12-15 mmHg (last `insufflation` event); a `crossing`
#          clip; a clip on the cut structure closer than 2 mm to the cut (once per clip).
#   Crossing clips still count toward the 2 + 1 clip rule (the contract does not exclude them).
#   Critical view reached = the earlier of `step_completed` dissect and a `critical_view`
#   event with value >= 100.
#
#     completion      = unique known steps completed / 11
#     port_accuracy   = mean over port events of clamp(1 - 0.5*err_mm/15 - 0.5*|angle|/20, 0, 1); 0 if none
#     insufflation_ok = 1 if the final pressure is within 12-15 mmHg, else 0 (0 if not measured)
#     critical_view   = max critical_view value / 100 (0 if none)
#     ideal path      = straight-line polyline through the positional events in time order (port
#                       sites, clip and cut positions); efficiency = clamp(ideal / travelled, 0, 1)
#     time_factor     = clamp(1 - elapsed_s / 600, 0, 1)
#     minor_factor    = clamp(1 - minor / 5, 0, 1)
#     score = 100 * (0.35*completion + 0.15*port_accuracy + 0.10*insufflation_ok + 0.10*critical_view
#                    + 0.10*path_efficiency + 0.10*time_factor + 0.10*minor_factor)
#     Any major error caps the score at LC_MAJOR_CAP (40).
#
# Mapping: successful_transfers = steps completed, drops = major errors,
# invalid_releases = minor errors. Exercise-specific numbers go in `details`.

LC_STEPS = ["veress", "insufflate", "port_umbilical", "port_epigastric", "port_rsc_medial", "port_rsc_lateral",
            "retract", "dissect", "clip_duct", "clip_artery", "divide"]
LC_PORTS = ("veress", "umbilical", "epigastric", "rsc_medial", "rsc_lateral")
LC_STRUCTURES = ("cystic_duct", "cystic_artery", "cbd")
PORT_MAX_MM = 15.0
PORT_MAX_DEG = 20.0
INSUFFLATION_MMHG = (12.0, 15.0)
CUT_CLIP_MARGIN_MM = 2.0
CVS_REACHED_PCT = 100.0
LC_TIME_BUDGET_S = 600.0
LC_MINOR_BUDGET = 5
LC_MAJOR_CAP = 40.0
CLIPPING_CODES = ("cbd_clip", "cbd_cut", "unsafe_division", "division_before_cvs", "clip_crossing", "clip_too_close")


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
    #            insufflation_out_of_range | clip_crossing | clip_too_close
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


def lap_chole_findings(events: list[Event]) -> list[Finding]:
    """Every major and minor error, in time order. The single source for counts, coach and cost."""
    ordered = sorted(events, key=lambda e: e.t_ms)
    out: list[Finding] = []
    cvs_ms = cvs_reached_ms(ordered)
    clips = [e for e in ordered if e.kind == "clip_applied"]
    too_close: set[int] = set()
    for e in ordered:
        if e.kind == "injury":
            out.append(Finding("major", "injury", e))
        elif e.kind == "port_placed":
            mm, deg = port_error(e)
            if (mm is not None and mm > PORT_MAX_MM) or (deg is not None and deg > PORT_MAX_DEG):
                out.append(Finding("minor", "port_offset", e, {"error_mm": mm, "angle_deg": deg}))
        elif e.kind == "clip_applied":
            if e.object_id == "cbd":
                out.append(Finding("major", "cbd_clip", e))
            if (e.detail or "").strip().lower() == "crossing":
                out.append(Finding("minor", "clip_crossing", e))
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


def compute_lap_chole_metrics(samples: list[Sample], events: list[Event], elapsed_s: float) -> LapCholeMetrics:
    ordered = sorted(events, key=lambda e: e.t_ms)
    done = {e.object_id for e in ordered if e.kind == "step_completed" and e.object_id in LC_STEPS}
    findings = lap_chole_findings(ordered)
    codes = [f.code for f in findings]
    major = sum(1 for f in findings if f.severity == "major")
    minor = len(findings) - major

    ports = [e for e in ordered if e.kind == "port_placed"]
    errs = [port_error(e) for e in ports]
    port_mm = [mm for mm, _ in errs if mm is not None]
    port_deg = [deg for _, deg in errs if deg is not None]
    per_port = [_clamp(1 - 0.5 * (mm or 0) / PORT_MAX_MM - 0.5 * (deg or 0) / PORT_MAX_DEG, 0.0, 1.0)
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
        elif e.kind in ("clip_applied", "structure_cut") and e.source:
            pts.append(e.source)
    ideal = sum(math.dist(a, b) for a, b in zip(pts, pts[1:]))
    efficiency = _clamp(ideal / total, 0.0, 1.0) if total > 0 else 0.0

    completion = _clamp(len(done) / len(LC_STEPS), 0.0, 1.0)
    time_factor = _clamp(1 - elapsed_s / LC_TIME_BUDGET_S, 0.0, 1.0)
    minor_factor = _clamp(1 - minor / LC_MINOR_BUDGET, 0.0, 1.0)
    score = 100 * (0.35 * completion + 0.15 * port_accuracy + 0.10 * (1.0 if insufflation_ok else 0.0)
                   + 0.10 * ((cv_pct or 0.0) / 100) + 0.10 * efficiency + 0.10 * time_factor
                   + 0.10 * minor_factor)
    score = _clamp(score, 0, 100)
    if major:
        score = min(score, LC_MAJOR_CAP)

    details: dict = {"steps_completed": len(done), "steps_total": len(LC_STEPS), "ports_placed": len(ports)}
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
    details["bile_duct_injury"] = bool(details["cbd_errors"]) or any(
        f.code == "injury" and f.event.object_id == "cbd" for f in findings)
    details["major_errors"] = major
    details["minor_errors"] = minor

    return LapCholeMetrics(
        completion_pct=round(completion * 100, 1),
        successful_transfers=len(done),
        drops=major,
        invalid_releases=minor,
        elapsed_s=elapsed_s,
        distance_by_instrument=dist,
        total_distance_m=total,
        path_efficiency=efficiency,
        practice_score=round(score, 1),
        details=details,
    )
