"""Itemized cost estimate for an attempt. Rates and citations: docs/COST_DATA.md.

Illustrative estimate, NOT billing data. ASSUMED_EVENT_RISK_PER_ERROR is an
assumption; no dataset links a simulator error to a real adverse event.

Wound closure reuses the same OR rates and error pricing (errors = missed stitches
+ tissue contacts). Its instrument line is a suture + needle-driver set priced at
ASSUMED_SUTURE_SET_COST, an ASSUMPTION: docs/COST_DATA.md has no suture source,
so the line is flagged assumed and the instruments option does not apply to it.

Laparoscopic cholecystectomy (lap_chole) keeps the OR time lines and the sourced
laparoscopic instrument line (Apelgren 1994, reusable or disposable). Hem-o-lok
clips and trocars are a separate line at ASSUMED_LAP_CHOLE_CLIPS_TROCARS_COST, an
ASSUMPTION with no source in docs/COST_DATA.md (it may overlap the disposable set).
Only major errors are priced as adverse events (the caller passes them as `errors`).
If a common bile duct error occurred, one more line prices a bile duct injury at the
sourced general-surgery complication cost (+$19,626); applying that figure to a bile
duct injury is an ASSUMPTION, so the line is flagged assumed.
"""
from __future__ import annotations

from dataclasses import dataclass, field
from scoring import difficulty_note

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
ASSUMED_SUTURE_SET_COST = 30.00  # ASSUMPTION, no cited source; suture + needle driver per case
ASSUMED_LAP_CHOLE_CLIPS_TROCARS_COST = 150.00  # ASSUMPTION, no cited source; Hem-o-lok clips + trocars per case
COMPLICATION_EXTRA_COST = 19626.0  # mean hospital cost increase with a complication, general surgery (JAMA Surg 2016)

SOURCES = {
    "or_time": "Childers & Maggard-Gibbons, JAMA Surg 2018;153(4):e176233",
    "instruments": "Apelgren et al., Surg Endosc 1994 (PMID 8153862)",
    "adverse_event": "Financial impact of intraoperative adverse events in abdominal surgery (PMID 26026793)",
    "complication": "Hospital and Payer Costs Associated With Surgical Complications, JAMA Surg 2016 (PMID 27168356)",
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
    difficulty: str = "normal"


def estimate_cost(
    elapsed_s: float,
    errors: int,
    path_efficiency: float,
    setting: str = "inpatient",
    instruments: str = "reusable",
    risk_per_error: float = ASSUMED_EVENT_RISK_PER_ERROR,
    exercise: str = "peg_transfer",
    bile_duct_injury: bool = False,
    difficulty: str = "normal",
) -> CostBreakdown:
    rate = OR_RATE_PER_MIN[setting]
    minutes = max(0.0, elapsed_s) / 60
    eff = min(1.0, max(0.0, path_efficiency))

    # Time spent beyond what an ideal path would have needed, priced like any OR minute.
    ideal_minutes = minutes * eff if eff > 0 else minutes
    wasted_minutes = minutes - ideal_minutes

    inst = INSTRUMENT_COST_PER_CASE[instruments]
    inst_line = CostLine(f"Instruments ({instruments})", 1, inst, round(inst, 2), SOURCES["instruments"])
    if exercise == "wound_closure":
        inst = ASSUMED_SUTURE_SET_COST
        inst_line = CostLine("Suture and needle-driver set (assumed price)", 1, inst, round(inst, 2), None, assumed=True)
    error_cost = errors * risk_per_error * ADVERSE_EVENT_EXTRA_COST
    extra: list[CostLine] = []
    error_label = "Expected cost of errors"
    if exercise == "lap_chole":
        inst_line = CostLine(f"Laparoscopic instruments ({instruments})", 1, inst, round(inst, 2),
                             SOURCES["instruments"])
        clips = ASSUMED_LAP_CHOLE_CLIPS_TROCARS_COST
        extra.append(CostLine("Hem-o-lok clips and trocars (assumed price)", 1, clips, clips, None, assumed=True))
        inst += clips
        error_label = "Expected cost of major errors"

    lines = [
        CostLine("OR time (efficient portion)", round(ideal_minutes, 2), rate, round(ideal_minutes * rate, 2), SOURCES["or_time"]),
        CostLine("OR time lost to inefficient movement", round(wasted_minutes, 2), rate, round(wasted_minutes * rate, 2), SOURCES["or_time"]),
        inst_line,
        *extra,
        CostLine(
            error_label, errors, round(risk_per_error * ADVERSE_EVENT_EXTRA_COST, 2),
            round(error_cost, 2), SOURCES["adverse_event"], assumed=True,
        ),
    ]
    if exercise == "lap_chole" and bile_duct_injury:
        lines.append(CostLine(
            "Bile duct injury (assumed: priced at the general-surgery complication cost)", 1,
            COMPLICATION_EXTRA_COST, COMPLICATION_EXTRA_COST, SOURCES["complication"], assumed=True,
        ))
    total = round(sum(l.total_usd for l in lines), 2)
    clean = round(ideal_minutes * rate + inst, 2)
    result = CostBreakdown(total_usd=total, lines=lines, counterfactual_clean_usd=clean,
                           avoidable_usd=round(total - clean, 2), difficulty=difficulty)
    if difficulty_note(difficulty):
        result.disclaimer += " " + difficulty_note(difficulty) + "."
    return result
