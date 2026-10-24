"""Deterministic laparoscopic cholecystectomy costs with paid explanations."""
from __future__ import annotations

import json
import os
import re
from dataclasses import asdict
from decimal import Decimal

import httpx
from fastapi import APIRouter, HTTPException, Request
from fastapi.responses import JSONResponse
from sqlalchemy import select

import coach
import cost
import database as db
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


def _stored_report(sid: str) -> dict:
    with db.SessionLocal() as session:
        row = session.get(db.TrainingSession, sid)
        if row is None:
            raise HTTPException(404, "session not found")
        if row.exercise != "lap_chole":
            raise HTTPException(409, "mistake costs require a laparoscopic cholecystectomy attempt")
        if row.status != "completed" or row.metrics is None:
            raise HTTPException(409, "session must be completed")
        events = [scoring.Event(kind=e.kind, t_ms=e.t_ms, instrument=e.instrument,
                               object_id=e.object_id, source=e.source, target=e.target,
                               value=e.value, detail=e.detail, event_id=e.event_id)
                  for e in session.scalars(select(db.EventRow).where(db.EventRow.session_id == sid)
                                          .order_by(db.EventRow.t_ms, db.EventRow.pk))]
        return build_report(sid, events, row.elapsed_s or 0.0, row.metrics["path_efficiency"])


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
        raise ValueError("Gemini supplied a figure or unsupported placeholder")
    sentences = [s for s in re.split(r"[.!?]+(?:\s|$)", prose.strip()) if s.strip()]
    if not 1 <= len(sentences) <= 2:
        raise ValueError("expected one or two sentences")
    for token in TOKENS:
        text = text.replace("{" + token + "}", f"${mistake[token]:,.2f}")
    return text


def _fallback(mistake: dict) -> str:
    return (f"{mistake['label']} has an illustrative cost of ${mistake['total_usd']:,.2f}: "
            f"${mistake['or_usd']:,.2f} allocated OR time, ${mistake['adverse_event_usd']:,.2f} "
            f"assumed adverse-event risk, and ${mistake['complication_usd']:,.2f} complication proxy. "
            "These assumptions do not predict a patient outcome.")


def explain(report: dict, client: httpx.Client | None = None) -> dict:
    mistakes = report["mistakes"]
    for mistake in mistakes:
        mistake["explanation"] = _fallback(mistake)
        mistake["explanation_source"] = "fallback"
    key = os.getenv("GEMINI_API_KEY", "").strip()
    if not key or not mistakes:
        return report
    if client is None:
        with httpx.Client(timeout=coach.GEMINI_TIMEOUT_S) as owned:
            return explain(report, owned)
    prompt = (
        "Explain the already-computed illustrative mistake costs of a laparoscopic cholecystectomy. "
        "Return JSON {\"explanations\": [{\"id\": \"mistake id\", \"text\": \"prose\"}]} in input order. "
        "Write one or two plain-language sentences per mistake. Never calculate, change, or invent any figure. "
        "Use {total_usd} for its total and optionally {or_usd}, {adverse_event_usd}, {complication_usd} "
        "for the respective components. Include {total_usd} in each text. Use no literal numbers, currency "
        "symbols, number words, or percentages in the text; the server fills those placeholders. "
        "Acknowledge that this is illustrative and risk/allocation are assumptions. Data:\n" +
        json.dumps([{k: v for k, v in m.items() if not k.startswith("explanation")} for m in mistakes])
    )
    try:
        response = coach._post_with_retry(client, coach.GEMINI_URL.format(
            model=os.getenv("GEMINI_MODEL") or coach.DEFAULT_MODEL),
            {"contents": [{"parts": [{"text": prompt}]}],
             "generationConfig": {"responseMimeType": "application/json", "temperature": 0.2}}, key)
        response.raise_for_status()
        entries = json.loads(response.json()["candidates"][0]["content"]["parts"][0]["text"])["explanations"]
        if not isinstance(entries, list) or len(entries) != len(mistakes):
            raise ValueError("wrong explanation count")
        rendered = []
        for entry, mistake in zip(entries, mistakes):
            if entry["id"] != mistake["id"]:
                raise ValueError("wrong mistake id")
            rendered.append(_render_explanation(entry["text"], mistake))
        # Validate the entire batch before accepting any model text.
        for mistake, text in zip(mistakes, rendered):
            mistake.update(explanation=text, explanation_source="gemini")
    except (httpx.HTTPError, ValueError, KeyError, IndexError, TypeError, AttributeError):
        pass
    return report


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
    return JSONResponse(explain(report), headers={
        "X-PAYMENT-RESPONSE": x402_gate.receipt_header(receipt),
        "Access-Control-Expose-Headers": "X-PAYMENT-RESPONSE",
        "Cache-Control": "no-store", "Vary": "X-PAYMENT",
    })
