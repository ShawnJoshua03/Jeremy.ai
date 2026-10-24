"""Deterministic exercise assessment derived only from stored metrics. No AI.

PROTOTYPE THRESHOLDS. These cut-offs were picked for the CalHacks demo so the
levels read sensibly on a virtual peg transfer. They are NOT clinically
validated, NOT FLS standards, and must not be used to certify anyone.
Every threshold lives in THRESHOLDS below; change them here and nowhere else.

Levels: "needs_work" | "developing" | "proficient". A criterion with no data
(for example no tracked motion) gets "not_assessed" and value None. Time is
"not_assessed" unless all 6 transfers were completed.
Smoothness is not reported: scoring.py does not compute it yet.
"""
from __future__ import annotations

from typing import Literal

from pydantic import BaseModel

Level = Literal["needs_work", "developing", "proficient", "not_assessed"]

# name -> (proficient cut-off, developing cut-off, higher_is_better)
THRESHOLDS: dict[str, tuple[float, float, bool]] = {
    "completion": (100.0, 50.0, True),       # completion_pct, percent of 6 objects
    "time": (120.0, 200.0, False),           # elapsed_s, seconds
    "errors": (0.0, 2.0, False),             # drops + invalid_releases, count
    "path_efficiency": (0.7, 0.5, True),     # ideal / travelled, 0..1
    "bimanual_balance": (0.6, 0.3, True),    # shorter / longer instrument distance, 0..1
}

_LABELS = {
    "completion": ("Completion", "%"),
    "time": ("Time", "s"),
    "errors": ("Errors (drops + invalid releases)", "count"),
    "path_efficiency": ("Path efficiency", "ratio"),
    "bimanual_balance": ("Bimanual balance", "ratio"),
}


class Criterion(BaseModel):
    id: str
    label: str
    value: float | None
    unit: str
    level: Level
    rule: str


class Assessment(BaseModel):
    practice_score: float
    criteria: list[Criterion]
    thresholds_note: str = "Prototype thresholds, not clinically validated."


def level_for(name: str, value: float | None) -> Level:
    if value is None:
        return "not_assessed"
    good, ok, higher = THRESHOLDS[name]
    if higher:
        return "proficient" if value >= good else "developing" if value >= ok else "needs_work"
    return "proficient" if value <= good else "developing" if value <= ok else "needs_work"


def _rule(name: str) -> str:
    good, ok, higher = THRESHOLDS[name]
    op_good, op_ok = (">=", ">=") if higher else ("<=", "<=")
    return f"proficient if {op_good} {good:g}, developing if {op_ok} {ok:g}, else needs_work"


def bimanual_balance(metrics: dict) -> float | None:
    dist = metrics.get("distance_by_instrument") or {}
    left, right = float(dist.get("left", 0.0)), float(dist.get("right", 0.0))
    longer = max(left, right)
    return round(min(left, right) / longer, 3) if longer > 0 else None


def values(metrics: dict) -> dict[str, float | None]:
    return {
        "completion": float(metrics["completion_pct"]) if "completion_pct" in metrics else None,
        "time": float(metrics["elapsed_s"]),
        "errors": float(metrics["drops"] + metrics["invalid_releases"]),
        "path_efficiency": round(float(metrics["path_efficiency"]), 3)
        if metrics.get("total_distance_m", 1) else None,
        "bimanual_balance": bimanual_balance(metrics),
    }


def assess(metrics: dict) -> Assessment:
    criteria = []
    for name, value in values(metrics).items():
        label, unit = _LABELS[name]
        rule = _rule(name)
        level = level_for(name, value)
        if name in ("path_efficiency", "bimanual_balance") and value is None:
            rule += " (no tracked instrument motion)"
        if name == "time":
            rule += "; rated only when all 6 transfers are complete"
            if metrics.get("completion_pct", 0) < 100:
                level = "not_assessed"  # a fast but unfinished run is not a fast run
        criteria.append(Criterion(id=name, label=label, value=value, unit=unit, level=level, rule=rule))
    return Assessment(practice_score=float(metrics["practice_score"]), criteria=criteria)
