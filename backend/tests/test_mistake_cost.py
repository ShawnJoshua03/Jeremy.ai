import base64
import copy
import json

import httpx
import pytest
from fastapi import HTTPException
from fastapi.testclient import TestClient
from sqlalchemy import create_engine
from sqlalchemy.orm import sessionmaker
from sqlalchemy.pool import StaticPool

import cost
import database as db
import llm
import mistake_cost_router as reports
import scoring



def event(kind, t=0, object_id=None, **kwargs):
    return scoring.Event(kind, t, object_id=object_id, event_id=f"{kind}-{t}", **kwargs)


def report(events=None, elapsed=60, efficiency=0.5):
    return reports.build_report("session", events if events is not None else [event("injury")], elapsed, efficiency)


def test_itemization_reconciles_and_counts_complication_once():
    events = [event("clip_applied", 10, "cbd", detail="crossing"), event("structure_cut", 20, "cbd"),
              event("injury", 30, "cbd"), event("hand_collision", 40)]
    result = report(events, elapsed=61.2, efficiency=0.431)
    findings = scoring.lap_chole_findings(events)
    assert [m["code"] for m in result["mistakes"]] == [f.code for f in findings]
    assert len(result["mistakes"]) == 5
    assert sum(m["complication_usd"] for m in result["mistakes"]) == cost.COMPLICATION_EXTRA_COST
    assert result["mistakes"][0]["complication_usd"] == cost.COMPLICATION_EXTRA_COST
    assert sum(m["adverse_event_usd"] for m in result["mistakes"]) == pytest.approx(3 * 138.57)
    assert all(m["adverse_event_usd"] == 0 for m in result["mistakes"] if m["severity"] == "minor")
    assert round(sum(m["total_usd"] for m in result["mistakes"]), 2) == result["total_usd"]
    assert result["total_usd"] == result["procedure_cost"]["avoidable_usd"]
    assert round(sum(m["or_usd"] for m in result["mistakes"]), 2) == result["procedure_cost"]["lines"][1]["total_usd"]
    assert reports.build_report("session", events, 61.2, 0.431) == result


@pytest.mark.parametrize("kind,object_id,kwargs", [
    ("injury", "cbd", {}), ("ductotomy", "cbd", {}), ("clip_applied", "cbd", {}),
    ("structure_cut", "cbd", {}),
])
def test_all_bile_duct_findings_use_one_proxy(kind, object_id, kwargs):
    result = report([event(kind, 10, object_id, **kwargs)])
    assert sum(m["complication_usd"] for m in result["mistakes"]) == cost.COMPLICATION_EXTRA_COST


def test_clean_and_unallocated_inefficiency():
    clean = report([], efficiency=1)
    assert clean["total_usd"] == 0 and clean["mistakes"] == []
    inefficient = report([])
    assert inefficient["total_usd"] == inefficient["unallocated_inefficiency_usd"] == 18.73
    assert inefficient["total_usd"] == inefficient["procedure_cost"]["avoidable_usd"]
    assert inefficient["procedure_cost"]["total_usd"] > inefficient["total_usd"]


@pytest.mark.parametrize("elapsed,efficiency", [(60, 0.5), (300, 0.5), (61.2, 0.431), (0, 0), (180, 1)])
def test_fractional_cent_costs_reconcile(elapsed, efficiency):
    result = report([event("injury", 1), event("injury", 2), event("hand_collision", 3)], elapsed, efficiency)
    assert result["total_usd"] == result["procedure_cost"]["avoidable_usd"]
    assert result["total_usd"] == round(sum(m["total_usd"] for m in result["mistakes"]), 2)


def claude_explains(claude, entries):
    def answer(prompt, schema):
        assert "laparoscopic cholecystectomy" in prompt
        assert schema is reports.EXPLANATION_SCHEMA
        return {"explanations": entries}
    return claude.reply(answer)


def test_claude_uses_backend_figures(claude):
    original = report()
    claude_explains(claude, [{"id": "mistake-1", "text": "This illustrative cost is {total_usd}, including {or_usd} of allocated OR time. Risk is assumed."}])
    result = reports.explain(copy.deepcopy(original))
    m = result["mistakes"][0]
    assert m["explanation_source"] == "claude"
    assert f"${original['mistakes'][0]['total_usd']:,.2f}" in m["explanation"]
    assert m["total_usd"] == original["mistakes"][0]["total_usd"]


@pytest.mark.parametrize("text", [
    "This costs $999999.99 and totals {total_usd}.",
    "This costs USD 999999 and totals {total_usd}.",
    "This costs 999999 dollars and totals {total_usd}.",
    "This costs nine million dollars and totals {total_usd}.",
    "This costs {made_up_usd} and totals {total_usd}.",
    "The total is {total_usd}. More text. Extra sentence.",
    "No computed total provided.",
])
def test_invented_claude_figures_discarded(claude, text):
    claude_explains(claude, [{"id": "mistake-1", "text": text}])
    result = reports.explain(report())
    assert result["mistakes"][0]["explanation_source"] == "fallback"
    assert "999999" not in result["mistakes"][0]["explanation"]


def test_claude_wrong_ids_and_outage_fall_back(claude):
    claude_explains(claude, [{"id": "wrong-id", "text": "Total {total_usd}."}])
    assert reports.explain(report())["mistakes"][0]["explanation_source"] == "fallback"
    claude.reply(llm.LLMUnavailable("BadRequestError"))
    assert reports.explain(report())["mistakes"][0]["explanation_source"] == "fallback"
    claude.reply({"wrong": []})
    assert reports.explain(report())["mistakes"][0]["explanation_source"] == "fallback"


@pytest.fixture
def client(monkeypatch):
    import main
    monkeypatch.delenv("ANTHROPIC_API_KEY", raising=False)
    engine = create_engine("sqlite://", connect_args={"check_same_thread": False}, poolclass=StaticPool)
    db.Base.metadata.create_all(engine)
    monkeypatch.setattr(db, "SessionLocal", sessionmaker(bind=engine, expire_on_commit=False))
    with TestClient(main.app) as test_client:
        yield test_client
    engine.dispose()


def completed(client, exercise="lap_chole"):
    sid = client.post("/api/v1/sessions", json={"trainee_id": "paid-report-test", "exercise": exercise}).json()["id"]
    client.post(f"/api/v1/sessions/{sid}/start")
    if exercise == "lap_chole":
        assert client.post(f"/api/v1/sessions/{sid}/events", json={"events": [
            {"kind": "injury", "t_ms": 100, "object_id": "cbd", "event_id": "injury-1"}]}).status_code == 200
    assert client.post(f"/api/v1/sessions/{sid}/complete", json={"elapsed_s": 60}).status_code == 200
    return f"/api/v1/sessions/{sid}/mistake-cost"


def test_free_summary_and_itemized_report(client):
    path = completed(client)
    summary = client.get(path + "/summary")
    assert summary.status_code == 200 and set(summary.json()) == {"total_usd"}
    response = client.get(path + "/report")
    assert response.status_code == 200 and response.json()["mistakes"]
    assert client.get("/api/v1/sessions/missing/mistake-cost/report").status_code == 404
    assert client.get(completed(client, "peg_transfer") + "/report").status_code == 409
    sid = client.post("/api/v1/sessions", json={"trainee_id": "new", "exercise": "lap_chole"}).json()["id"]
    assert client.get(f"/api/v1/sessions/{sid}/mistake-cost/summary").status_code == 409



def test_existing_api_key_still_required(client, monkeypatch):
    path = completed(client)
    monkeypatch.setenv("API_KEY", "test-api-key")
    assert client.get(path + "/summary").status_code == 401
    assert client.get(path + "/summary", headers={"X-API-Key": "test-api-key"}).status_code == 200
