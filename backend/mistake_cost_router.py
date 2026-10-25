"""Deterministic laparoscopic cholecystectomy costs with paid explanations."""
from __future__ import annotations

import json
import re
from dataclasses import asdict
from decimal import Decimal

from fastapi import APIRouter, HTTPException, Request
from fastapi.responses import JSONResponse
from sqlalchemy import select

import cost
import database as db
import llm
import provider_log
import scoring
import x402_gate

router = APIRouter(prefix="/api/v1/sessions", tags=["mistake cost"])
DISCLAIMER = "Illustrative estimates, not billing data or a prediction of patient outcomes."
ASSUMPTIONS = [
    "Each major finding uses an assumed adverse-event probability of 0.01; minor findings use zero.",
    "Lost OR time is split equally across findings; this allocation is assumed, not measured causation.",
    "The general-surgery complication proxy is charged once per attempt, on the first bile-duct finding.",
    "Supply quantities and the representative trocar model are assumed; sourced prices are listing snapshots.",
    "Historical OR costs, instrument costs and adverse-event charges are not inflation-adjusted and may overlap.",
]
LABELS = {
    "cbd_clip": "Clip applied to the common bile duct",
    "cbd_cut": "Common bile duct cut",
    "injury": "Tissue injury/contact",
    "clip_crossing": "Crossing clips",
    "clip_too_close": "Clip too close to the cut",
    "hand_collision": "Hand collision",
}


def _cents(value: float) -> int:
    return int(Decimal(str(value)).quantize(Decimal("0.01")) * 100)


def build_report(sid: str, events: list[scoring.Event], elapsed_s: float, path_efficiency: float) -> dict:
    findings = scoring.lap_chole_findings(events)
    bile_index = next((i for i, f in enumerate(findings)
                       if f.code in ("cbd_clip", "cbd_cut") or
                       (f.event.object_id == "cbd" and f.code in ("injury", "ductotomy_wrong_structure"))), None)
    major = sum(f.severity == "major" for f in findings)
    base = cost.estimate_cost(elapsed_s, major, path_efficiency, exercise="lap_chole",
                              bile_duct_injury=bile_index is not None)
    wasted = _cents(base.lines[1].total_usd)
    # Integer cents preserve reconciliation, including fractional-cent allocations.
    portion, remainder = divmod(wasted, len(findings)) if findings else (0, 0)
    risk_line = next(line for line in base.lines if line.label == "Expected cost of major errors")
    risk_portion, risk_remainder = divmod(_cents(risk_line.total_usd), major) if major else (0, 0)
    mistakes = []
    major_index = 0
    for i, finding in enumerate(findings):
        or_cents = portion + (i < remainder)
        risk_cents = 0
        if finding.severity == "major":
            risk_cents = risk_portion + (major_index < risk_remainder)
            major_index += 1
        complication = cost.COMPLICATION_EXTRA_COST if i == bile_index else 0.0
        mistakes.append({
            "id": f"mistake-{i + 1}", "event_id": finding.event.event_id,
            "t_ms": finding.event.t_ms, "code": finding.code, "severity": finding.severity,
            "label": LABELS.get(finding.code, finding.code.replace("_", " ").capitalize()),
            "or_minutes": round(or_cents / 100 / cost.OR_RATE_PER_MIN["inpatient"], 6),
            "or_usd": or_cents / 100, "adverse_event_risk": cost.ASSUMED_EVENT_RISK_PER_ERROR if risk_cents else 0.0,
            "adverse_event_usd": risk_cents / 100, "complication_usd": complication,
            "total_usd": (or_cents + risk_cents + _cents(complication)) / 100,
            "assumed": True,
        })
    unallocated = wasted / 100 if not findings else 0.0
    return {
        "session_id": sid, "exercise": "lap_chole", "procedure": "Laparoscopic cholecystectomy",
        "currency": "USD", "total_usd": (sum(_cents(m["total_usd"]) for m in mistakes) + _cents(unallocated)) / 100,
        "mistakes": mistakes, "unallocated_inefficiency_usd": unallocated,
        "procedure_cost": asdict(base), "sources": cost.SOURCES,
        "assumptions": ASSUMPTIONS, "disclaimer": DISCLAIMER,
    }


CVS_ASSUMPTIONS = [
    "A simulator decision is not a patient event; each priced mistake uses an assumed adverse-event probability of 0.01.",
    "A false 'criteria demonstrated' call is priced as that probability times the general-surgery complication "
    "cost, applied to a bile duct injury as an assumption.",
    "A misidentified structure is priced as that probability times the intraoperative adverse-event cost.",
    "Requesting the instructor costs nothing; it is the safe choice when a view is unclear.",
]


def build_cvs_report(sid: str, analysis) -> dict:
    """lap_chole_cvs (docs/CVS_SCORING.md): one line per priced judgement, in exercise order."""
    risk = cost.ASSUMED_EVENT_RISK_PER_ERROR
    mistakes = []
    for r in analysis.exercises:
        if r.error == "cvs_false_positive":
            complication = round(risk * cost.COMPLICATION_EXTRA_COST, 2)
            item = {"code": r.error, "severity": "major", "complication_usd": complication, "adverse_event_usd": 0.0,
                    "label": f"Bile duct injury risk: {r.title} called criteria demonstrated",
                    "source": cost.SOURCES["complication"], "adverse_event_risk": risk}
        elif r.error == "misidentification":
            item = {"code": r.error, "severity": "minor", "complication_usd": 0.0,
                    "adverse_event_usd": round(risk * cost.ADVERSE_EVENT_EXTRA_COST, 2),
                    "label": f"Misidentification: {r.title}, {r.decision.split(':', 1)[-1].replace('_', ' ')} "
                             "selected", "source": cost.SOURCES["adverse_event"], "adverse_event_risk": risk}
        elif r.unclear and r.known:
            item = {"code": "instructor_requested", "severity": "none", "complication_usd": 0.0,
                    "adverse_event_usd": 0.0, "label": f"Instructor review requested: {r.title}",
                    "source": None, "adverse_event_risk": 0.0}
        else:
            continue
        priced = item["severity"] != "none"
        mistakes.append({"id": f"mistake-{len(mistakes) + 1}", "exercise_id": r.exercise_id,
                         "t_ms": r.decided_ms if r.decided_ms is not None else (r.started_ms or 0),
                         **item, "or_minutes": 0.0, "or_usd": 0.0,
                         "total_usd": round(item["complication_usd"] + item["adverse_event_usd"], 2),
                         "assumed": priced})
    return {
        "session_id": sid, "exercise": "lap_chole_cvs",
        "procedure": "Laparoscopic cholecystectomy: critical view of safety", "currency": "USD",
        "total_usd": sum(_cents(m["total_usd"]) for m in mistakes) / 100, "mistakes": mistakes,
        "sources": cost.SOURCES, "assumptions": CVS_ASSUMPTIONS, "disclaimer": DISCLAIMER,
    }


def _cvs_report(session, row) -> dict:
    import cvs_scoring
    samples = [scoring.Sample(r.instrument, r.idx, r.t_ms, (r.x, r.y, r.z), r.valid)
               for r in session.scalars(select(db.SampleRow).where(db.SampleRow.session_id == row.id))]
    analysis = cvs_scoring.analyze(samples, _events(session, row.id), row.elapsed_s or 0.0)
    return build_cvs_report(row.id, analysis)


def _events(session, sid: str) -> list[scoring.Event]:
    return [scoring.Event(kind=e.kind, t_ms=e.t_ms, instrument=e.instrument,
                          object_id=e.object_id, source=e.source, target=e.target,
                          value=e.value, detail=e.detail, event_id=e.event_id)
            for e in session.scalars(select(db.EventRow).where(db.EventRow.session_id == sid)
                                     .order_by(db.EventRow.t_ms, db.EventRow.pk))]


def _stored_report(sid: str) -> dict:
    with db.SessionLocal() as session:
        row = session.get(db.TrainingSession, sid)
        if row is None:
            raise HTTPException(404, "session not found")
        if row.exercise not in ("lap_chole", "lap_chole_cvs"):
            raise HTTPException(409, "mistake costs require a laparoscopic cholecystectomy attempt")
        if row.status != "completed" or row.metrics is None:
            raise HTTPException(409, "session must be completed")
        if row.exercise == "lap_chole_cvs":
            return _cvs_report(session, row)
        return build_report(sid, _events(session, sid), row.elapsed_s or 0.0, row.metrics["path_efficiency"])


TOKENS = ("total_usd", "or_usd", "adverse_event_usd", "complication_usd")
# The model supplies prose with money placeholders. Only the backend renders numbers.
# This deliberately rejects even an otherwise-correct literal dollar figure.
FORBIDDEN_NUMBERS = re.compile(
    r"\d|[$€£¥%]|\b(?:usd|dollars?|cents?|zero|one|two|three|four|five|six|seven|eight|nine|ten|"
    r"eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen|twenty|thirty|"
    r"forty|fifty|sixty|seventy|eighty|ninety|hundred|thousand|million|billion|percent)\b", re.I)


def _render_explanation(text: object, mistake: dict) -> str:
    if not isinstance(text, str) or not 1 <= len(text) <= 800 or "{total_usd}" not in text:
        raise ValueError("missing explanation or total placeholder")
    prose = text
    for token in TOKENS:
        prose = prose.replace("{" + token + "}", "")
    if "{" in prose or "}" in prose or FORBIDDEN_NUMBERS.search(prose):
        raise ValueError("Claude supplied a figure or unsupported placeholder")
    sentences = [s for s in re.split(r"[.!?]+(?:\s|$)", prose.strip()) if s.strip()]
    if not 1 <= len(sentences) <= 2:
        raise ValueError("expected one or two sentences")
    for token in TOKENS:
        text = text.replace("{" + token + "}", f"${mistake[token]:,.2f}")
    return text


EXPLANATION_SCHEMA = {
    "type": "object",
    "properties": {"explanations": {"type": "array", "items": {
        "type": "object", "properties": {"id": {"type": "string"}, "text": {"type": "string"}},
        "required": ["id", "text"], "additionalProperties": False}}},
    "required": ["explanations"], "additionalProperties": False,
}


def _fallback(mistake: dict) -> str:
    return (f"{mistake['label']} has an illustrative cost of ${mistake['total_usd']:,.2f}: "
            f"${mistake['or_usd']:,.2f} allocated OR time, ${mistake['adverse_event_usd']:,.2f} "
            f"assumed adverse-event risk, and ${mistake['complication_usd']:,.2f} complication proxy. "
            "These assumptions do not predict a patient outcome.")


def explain(report: dict) -> dict:
    mistakes = report["mistakes"]
    for mistake in mistakes:
        mistake["explanation"] = _fallback(mistake)
        mistake["explanation_source"] = "fallback"
    if not llm.configured() or not mistakes:
        return report
    prompt = (
        "Explain the already-computed illustrative mistake costs of a laparoscopic cholecystectomy. "
        "Return explanations as a list of {id, text} in input order. "
        "Write one or two plain-language sentences per mistake. Never calculate, change, or invent any figure. "
        "Use {total_usd} for its total and optionally {or_usd}, {adverse_event_usd}, {complication_usd} "
        "for the respective components. Include {total_usd} in each text. Use no literal numbers, currency "
        "symbols, number words, or percentages in the text; the server fills those placeholders. "
        "Acknowledge that this is illustrative and risk/allocation are assumptions. Data:\n" +
        json.dumps([{k: v for k, v in m.items() if not k.startswith("explanation")} for m in mistakes])
    )
    try:
        entries = llm.complete_json(prompt, EXPLANATION_SCHEMA, max_tokens=1500)["explanations"]
        if not isinstance(entries, list) or len(entries) != len(mistakes):
            raise ValueError("wrong explanation count")
        rendered = []
        for entry, mistake in zip(entries, mistakes):
            if entry["id"] != mistake["id"]:
                raise ValueError("wrong mistake id")
            rendered.append(_render_explanation(entry["text"], mistake))
        # Validate the entire batch before accepting any model text.
        for mistake, text in zip(mistakes, rendered):
            mistake.update(explanation=text, explanation_source="claude")
    except (llm.LLMUnavailable, ValueError, KeyError, IndexError, TypeError, AttributeError):
        pass
    return report


def _end_ms(sid: str) -> int:
    with db.SessionLocal() as session:
        row = session.get(db.TrainingSession, sid)
        return int((row.elapsed_s or 0) * 1000) if row is not None else 0


@router.get("/{sid}/mistake-cost/summary")
def summary(sid: str):
    report = _stored_report(sid)
    return JSONResponse({"total_usd": report["total_usd"]}, headers={"Cache-Control": "no-store"})


@router.get("/{sid}/mistake-cost/report", responses={402: {"description": "x402 v1 payment required"}})
def paid_report(sid: str, request: Request):
    report = _stored_report(sid)  # Validate the attempt before accepting a payment.
    required = x402_gate.requirements(str(request.url.replace(query="")))
    try:
        receipt = x402_gate.verify_and_settle(request.headers.get("X-PAYMENT"), required)
    except x402_gate.PaymentRequired:
        return x402_gate.payment_required(required)
    provider_log.record(sid, "x402", "itemized report paid", "paid", _end_ms(sid))
    return JSONResponse(explain(report), headers={
        "X-PAYMENT-RESPONSE": x402_gate.receipt_header(receipt),
        "Access-Control-Expose-Headers": "X-PAYMENT-RESPONSE",
        "Cache-Control": "no-store", "Vary": "X-PAYMENT",
    })
