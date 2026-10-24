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
EVENT_KINDS = {"peg_transfer": PEG_TRANSFER_KINDS, "wound_closure": WOUND_CLOSURE_KINDS}


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
    return compute_metrics(samples, events, elapsed_s)
