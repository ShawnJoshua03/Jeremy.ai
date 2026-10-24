"""Deterministic exercise assessment derived only from stored metrics. No AI.

PROTOTYPE THRESHOLDS. These cut-offs were picked for the CalHacks demo so the
levels read sensibly on a virtual peg transfer. They are NOT clinically
validated, NOT FLS standards, and must not be used to certify anyone.
Every threshold lives in THRESHOLDS below; change them here and nowhere else.

Levels: "needs_work" | "developing" | "proficient". A criterion with no data
(for example no tracked motion) gets "not_assessed" and value None. Time is
"not_assessed" unless all 6 transfers were completed.
Smoothness is not reported: scoring.py does not compute it yet.

Wound closure (docs/WOUND_CLOSURE.md) has its own WOUND_CLOSURE_THRESHOLDS, also
PROTOTYPE values picked for the demo, not clinical standards. Bite accuracy is
the mean bite error in mm (contract: 4 mm good, 8 mm still placed); spacing is the
SD of the gaps between placed stitches. Time is rated only when all 5 are placed.

Laparoscopic cholecystectomy (docs/LAP_CHOLE.md) uses LAP_CHOLE_THRESHOLDS, also
PROTOTYPE values. Values come from metrics["details"]: port accuracy is the mean
port distance from its site in mm, insufflation is how far the final pressure sat
outside 12-15 mmHg (0 inside), clipping safety counts clip and division errors
(CBD clip/cut, unsafe or early division or ductotomy, crossing clip, clip too close to a cut),
and any major error rates "needs_work". Time is rated only when all 15 steps are done.

Port placement (docs/PORT_PLACEMENT.md) has PORT_PLACEMENT_THRESHOLDS. Only the
manipulation-angle band is cited (Hanna et al. 1997); the rest are PROTOTYPE values.
Time is rated only when all 4 ports are placed.
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

# PROTOTYPE wound-closure cut-offs, same (proficient, developing, higher_is_better) shape
WOUND_CLOSURE_THRESHOLDS: dict[str, tuple[float, float, bool]] = {
    "completion": (100.0, 60.0, True),       # completion_pct, percent of 5 sites
    "time": (180.0, 300.0, False),           # elapsed_s, seconds
    "bite_accuracy": (4.0, 8.0, False),      # mean_bite_error_mm
    "spacing": (2.0, 5.0, False),            # spacing_sd_mm
    "tissue_contacts": (0.0, 1.0, False),    # drops field = tissue contacts, count
    "path_efficiency": (0.5, 0.3, True),     # ideal / travelled, 0..1
    "bimanual_balance": (0.4, 0.2, True),    # shorter / longer instrument distance, 0..1
}
# Port placement (docs/PORT_PLACEMENT.md). The manipulation-angle bands come from Hanna et al. 1997
# (45-75 degrees, best 60, so within 15 of 60 is proficient); every other cut-off is a PROTOTYPE value.
PORT_PLACEMENT_THRESHOLDS: dict[str, tuple[float, float, bool]] = {
    "completion": (100.0, 75.0, True),       # completion_pct, percent of 4 ports
    "time": (120.0, 240.0, False),           # elapsed_s, seconds
    "port_site_error": (10.0, 15.0, False),  # details.ports_mean_error_mm; 15 = scoring.PORT_MAX_MM
    "manipulation_angle": (15.0, 30.0, False),  # |details.manipulation_angle_deg - 60|
    "azimuth_symmetry": (10.0, 20.0, False),  # details.azimuth_difference_deg
    "safety_errors": (0.0, 0.0, False),      # drops field = major errors, count
}
# PROTOTYPE lap-chole cut-offs
LAP_CHOLE_THRESHOLDS: dict[str, tuple[float, float, bool]] = {
    "completion": (100.0, 80.0, True),       # completion_pct, percent of 15 steps
    "time": (300.0, 600.0, False),           # elapsed_s, seconds
    "port_accuracy": (5.0, 15.0, False),     # details.ports_mean_error_mm
    "insufflation": (0.0, 2.0, False),       # mmHg outside 12-15
    "critical_view": (100.0, 80.0, True),    # details.critical_view_pct
    "clipping_safety": (0.0, 1.0, False),    # details.clipping_errors, count
    "major_errors": (0.0, 0.0, False),       # drops field = major errors, count
    "path_efficiency": (0.4, 0.2, True),     # ideal / travelled, 0..1
}
THRESHOLDS_BY_EXERCISE = {
    "peg_transfer": THRESHOLDS,
    "wound_closure": WOUND_CLOSURE_THRESHOLDS,
    "port_placement": PORT_PLACEMENT_THRESHOLDS,
    "lap_chole": LAP_CHOLE_THRESHOLDS,
}
BEST_MANIPULATION_DEG = 60.0  # Hanna 1997
INSUFFLATION_RANGE_MMHG = (12.0, 15.0)

_LABELS = {
    "completion": ("Completion", "%"),
    "time": ("Time", "s"),
    "errors": ("Errors (drops + invalid releases)", "count"),
    "path_efficiency": ("Path efficiency", "ratio"),
    "bimanual_balance": ("Bimanual balance", "ratio"),
    "bite_accuracy": ("Bite accuracy (mean error)", "mm"),
    "spacing": ("Stitch spacing (SD)", "mm"),
    "tissue_contacts": ("Tissue contacts", "count"),
    "port_site_error": ("Port site error (mean)", "mm"),
    "manipulation_angle": ("Manipulation angle (distance from 60°)", "deg"),
    "azimuth_symmetry": ("Azimuth symmetry (left vs right)", "deg"),
    "safety_errors": ("Safety errors (vessel zone, sequence, blind entry)", "count"),
    "port_accuracy": ("Port placement (mean distance from site)", "mm"),
    "insufflation": ("Insufflation (mmHg outside 12-15)", "mmHg"),
    "critical_view": ("Critical view of safety (cleared)", "%"),
    "clipping_safety": ("Clipping and division errors", "count"),
    "major_errors": ("Major errors", "count"),
}
_NO_MOTION = " (no tracked instrument motion)"
_NO_PORT = " (no port placed)"
_NO_TRIANGLE = " (needs the camera and both working ports and the target landmark)"
_NO_DATA_NOTE = {  # appended to the rule when a criterion has no value
    "path_efficiency": _NO_MOTION,
    "bimanual_balance": _NO_MOTION,
    "bite_accuracy": " (no stitch placed)",
    "spacing": " (needs at least 3 placed stitches)",
    "port_site_error": _NO_PORT,
    "manipulation_angle": _NO_TRIANGLE,
    "azimuth_symmetry": _NO_TRIANGLE,
    "port_accuracy": _NO_PORT,
    "insufflation": " (no pressure reported)",
    "critical_view": " (no critical view reported)",
}
_TIME_NOTE = {
    "peg_transfer": "; rated only when all 6 transfers are complete",
    "wound_closure": "; rated only when all 5 stitches are placed",
    "port_placement": "; rated only when all 4 ports are placed",
    "lap_chole": "; rated only when all 15 steps are complete",
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


def exercise_of(metrics: dict, exercise: str | None = None) -> str:
    ex = exercise or metrics.get("exercise") or "peg_transfer"
    return ex if ex in THRESHOLDS_BY_EXERCISE else "peg_transfer"


def level_for(name: str, value: float | None, exercise: str = "peg_transfer") -> Level:
    if value is None:
        return "not_assessed"
    good, ok, higher = THRESHOLDS_BY_EXERCISE[exercise][name]
    if higher:
        return "proficient" if value >= good else "developing" if value >= ok else "needs_work"
    return "proficient" if value <= good else "developing" if value <= ok else "needs_work"


def _rule(name: str, exercise: str = "peg_transfer") -> str:
    good, ok, higher = THRESHOLDS_BY_EXERCISE[exercise][name]
    op_good, op_ok = (">=", ">=") if higher else ("<=", "<=")
    return f"proficient if {op_good} {good:g}, developing if {op_ok} {ok:g}, else needs_work"


def bimanual_balance(metrics: dict) -> float | None:
    dist = metrics.get("distance_by_instrument") or {}
    left, right = float(dist.get("left", 0.0)), float(dist.get("right", 0.0))
    longer = max(left, right)
    return round(min(left, right) / longer, 3) if longer > 0 else None


def _opt(v) -> float | None:
    return None if v is None else round(float(v), 2)


def _insufflation_deviation(pressure) -> float | None:
    if pressure is None:
        return None
    lo, hi = INSUFFLATION_RANGE_MMHG
    p = float(pressure)
    return round(max(lo - p, p - hi, 0.0), 2)


def _path_efficiency(metrics: dict) -> float | None:
    return round(float(metrics["path_efficiency"]), 3) if metrics.get("total_distance_m", 1) else None


def values(metrics: dict, exercise: str | None = None) -> dict[str, float | None]:
    ex = exercise_of(metrics, exercise)
    d = metrics.get("details") or {}
    shared = {
        "completion": float(metrics["completion_pct"]) if "completion_pct" in metrics else None,
        "time": float(metrics["elapsed_s"]),
    }
    if ex == "port_placement":
        angle = d.get("manipulation_angle_deg")
        return {
            **shared,
            "port_site_error": _opt(d.get("ports_mean_error_mm")),
            "manipulation_angle": None if angle is None else round(abs(float(angle) - BEST_MANIPULATION_DEG), 1),
            "azimuth_symmetry": _opt(d.get("azimuth_difference_deg")),
            "safety_errors": float(metrics["drops"]),
        }
    if ex == "lap_chole":
        return {
            **shared,
            "port_accuracy": _opt(d.get("ports_mean_error_mm")),
            "insufflation": _insufflation_deviation(d.get("insufflation_mmhg")),
            "critical_view": _opt(d.get("critical_view_pct")),
            "clipping_safety": float(d.get("clipping_errors", 0)),
            "major_errors": float(metrics["drops"]),
            "path_efficiency": _path_efficiency(metrics),
        }
    if ex == "wound_closure":
        return {
            **shared,
            "bite_accuracy": _opt(metrics.get("mean_bite_error_mm")),
            "spacing": _opt(metrics.get("spacing_sd_mm")),
            "tissue_contacts": float(metrics["drops"]),
            "path_efficiency": _path_efficiency(metrics),
            "bimanual_balance": bimanual_balance(metrics),
        }
    return {
        **shared,
        "errors": float(metrics["drops"] + metrics["invalid_releases"]),
        "path_efficiency": _path_efficiency(metrics),
        "bimanual_balance": bimanual_balance(metrics),
    }


def assess(metrics: dict, exercise: str | None = None) -> Assessment:
    ex = exercise_of(metrics, exercise)
    criteria = []
    for name, value in values(metrics, ex).items():
        label, unit = _LABELS[name]
        rule = _rule(name, ex)
        level = level_for(name, value, ex)
        if value is None:
            rule += _NO_DATA_NOTE.get(name, "")
        if name == "time":
            rule += _TIME_NOTE[ex]
            if metrics.get("completion_pct", 0) < 100:
                level = "not_assessed"  # a fast but unfinished run is not a fast run
        criteria.append(Criterion(id=name, label=label, value=value, unit=unit, level=level, rule=rule))
    return Assessment(practice_score=float(metrics["practice_score"]), criteria=criteria)
