"""Itemized cost estimate for an attempt. Rates and citations: docs/COST_DATA.md.

Illustrative estimate, NOT billing data. Only ASSUMED_EVENT_RISK_PER_ERROR is an
assumption; no dataset links a simulator error to a real adverse event.
"""
from __future__ import annotations

from dataclasses import dataclass, field

OR_RATE_PER_MIN = {
    "inpatient": 37.45,  # Childers & Maggard-Gibbons, JAMA Surgery 2018 (California, FY2014)
    "ambulatory": 36.14,  # same
    "cross_study_mean": 46.04,  # J Orthopaedic Business, mean of 14 published estimates
}
INSTRUMENT_COST_PER_CASE = {
    "reusable": 48.80,  # midpoint of $46.92-$50.67, Apelgren et al. 1994
    "disposable": 395.00,  # midpoint of $330-$460, Apelgren et al. 1994
}
ADVERSE_EVENT_EXTRA_COST = 13857.0  # $27,169 - $13,312 median charges, intraoperative AE, abdominal surgery
ASSUMED_EVENT_RISK_PER_ERROR = 0.01  # ASSUMPTION, adjustable, shown on screen

SOURCES = {
    "or_time": "Childers & Maggard-Gibbons, JAMA Surg 2018;153(4):e176233",
    "instruments": "Apelgren et al., Surg Endosc 1994 (PMID 8153862)",
    "adverse_event": "Financial impact of intraoperative adverse events in abdominal surgery (PMID 26026793)",
}


@dataclass
class CostLine:
    label: str
    quantity: float
    unit_usd: float
    total_usd: float
    source: str | None = None
    assumed: bool = False


@dataclass
class CostBreakdown:
    total_usd: float
    lines: list[CostLine] = field(default_factory=list)
    counterfactual_clean_usd: float = 0.0  # same run with no errors and ideal path
    avoidable_usd: float = 0.0
    disclaimer: str = "Illustrative estimate, not billing data."


def estimate_cost(
    elapsed_s: float,
    errors: int,
    path_efficiency: float,
    setting: str = "inpatient",
    instruments: str = "reusable",
    risk_per_error: float = ASSUMED_EVENT_RISK_PER_ERROR,
) -> CostBreakdown:
    rate = OR_RATE_PER_MIN[setting]
    minutes = max(0.0, elapsed_s) / 60
    eff = min(1.0, max(0.0, path_efficiency))

    # Time spent beyond what an ideal path would have needed, priced like any OR minute.
    ideal_minutes = minutes * eff if eff > 0 else minutes
    wasted_minutes = minutes - ideal_minutes

    inst = INSTRUMENT_COST_PER_CASE[instruments]
    error_cost = errors * risk_per_error * ADVERSE_EVENT_EXTRA_COST

    lines = [
        CostLine("OR time (efficient portion)", round(ideal_minutes, 2), rate, round(ideal_minutes * rate, 2), SOURCES["or_time"]),
        CostLine("OR time lost to inefficient movement", round(wasted_minutes, 2), rate, round(wasted_minutes * rate, 2), SOURCES["or_time"]),
        CostLine(f"Instruments ({instruments})", 1, inst, round(inst, 2), SOURCES["instruments"]),
        CostLine(
            "Expected cost of errors", errors, round(risk_per_error * ADVERSE_EVENT_EXTRA_COST, 2),
            round(error_cost, 2), SOURCES["adverse_event"], assumed=True,
        ),
    ]
    total = round(sum(l.total_usd for l in lines), 2)
    clean = round(ideal_minutes * rate + inst, 2)
    return CostBreakdown(total_usd=total, lines=lines, counterfactual_clean_usd=clean, avoidable_usd=round(total - clean, 2))
