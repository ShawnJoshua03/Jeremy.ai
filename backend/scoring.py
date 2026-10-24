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
