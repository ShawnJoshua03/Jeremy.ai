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
import mistake_cost_router as reports
import scoring
import x402_gate as gate

PAY_TO = "0x" + "1" * 40
PAYER = "0x" + "2" * 40
RECEIPT = {"success": True, "payer": PAYER, "transaction": "0x" + "3" * 64, "network": "base-sepolia"}


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


def gemini_client(entries):
    def handle(request):
        prompt = json.loads(request.content)["contents"][0]["parts"][0]["text"]
        assert "laparoscopic cholecystectomy" in prompt
        assert request.headers["x-goog-api-key"] == "test-key"
        return httpx.Response(200, json={"candidates": [{"content": {"parts": [
            {"text": json.dumps({"explanations": entries})}]}}]})
    return httpx.Client(transport=httpx.MockTransport(handle))


def test_gemini_uses_backend_figures(monkeypatch):
    monkeypatch.setenv("GEMINI_API_KEY", "test-key")
    original = report()
    with gemini_client([{"id": "mistake-1", "text": "This illustrative cost is {total_usd}, including {or_usd} of allocated OR time. Risk is assumed."}]) as client:
        result = reports.explain(copy.deepcopy(original), client)
    m = result["mistakes"][0]
    assert m["explanation_source"] == "gemini"
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
def test_invented_gemini_figures_discarded(monkeypatch, text):
    monkeypatch.setenv("GEMINI_API_KEY", "test-key")
    with gemini_client([{"id": "mistake-1", "text": text}]) as client:
        result = reports.explain(report(), client)
    assert result["mistakes"][0]["explanation_source"] == "fallback"
    assert "999999" not in result["mistakes"][0]["explanation"]


def test_gemini_wrong_ids_and_outage_fall_back(monkeypatch):
    monkeypatch.setenv("GEMINI_API_KEY", "test-key")
    with gemini_client([{"id": "wrong-id", "text": "Total {total_usd}."}]) as client:
        assert reports.explain(report(), client)["mistakes"][0]["explanation_source"] == "fallback"
    with httpx.Client(transport=httpx.MockTransport(lambda r: httpx.Response(400))) as client:
        assert reports.explain(report(), client)["mistakes"][0]["explanation_source"] == "fallback"


@pytest.fixture
def client(monkeypatch):
    import main
    monkeypatch.delenv("GEMINI_API_KEY", raising=False)
    monkeypatch.setenv("X402_PAY_TO", PAY_TO)
    monkeypatch.setenv("X402_FACILITATOR_URL", "https://facilitator.example/x402")
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


def payment(**changes):
    payload = {"x402Version": 1, "scheme": "exact", "network": "base-sepolia", "payload": {
        "signature": "0x" + "a" * 130,
        "authorization": {"from": PAYER, "to": PAY_TO, "value": "10000",
                          "validAfter": "0", "validBefore": "9999999999", "nonce": "0x" + "4" * 64}}}
    payload.update(changes)
    return base64.b64encode(json.dumps(payload).encode()).decode()


def facilitator(monkeypatch, verify=None, settle=None, status=200):
    calls = []
    def handle(request):
        body = json.loads(request.content)
        calls.append((request.url.path, body))
        assert body["paymentRequirements"]["payTo"] == PAY_TO
        assert body["paymentRequirements"]["maxAmountRequired"] == "10000"
        assert body["paymentRequirements"]["asset"] == gate.ASSET
        result = (verify if verify is not None else {"isValid": True, "payer": PAYER}) if request.url.path.endswith("/verify") else (settle if settle is not None else RECEIPT)
        return httpx.Response(status, json=result)
    original = gate.verify_and_settle
    def invoke(header, required):
        with httpx.Client(transport=httpx.MockTransport(handle)) as transport:
            return original(header, required, transport)
    monkeypatch.setattr(gate, "verify_and_settle", invoke)
    return calls


def test_free_summary_and_payment_challenge(client, monkeypatch):
    path = completed(client)
    calls = facilitator(monkeypatch)
    monkeypatch.setattr(reports, "explain", lambda *args: pytest.fail("unpaid call reached Gemini"))
    summary = client.get(path + "/summary")
    assert summary.status_code == 200 and set(summary.json()) == {"total_usd"}
    response = client.get(path + "/report")
    assert response.status_code == 402 and calls == []
    data = response.json()
    assert set(data) == {"x402Version", "error", "accepts"}
    required = data["accepts"][0]
    assert required["resource"] == "http://testserver" + path + "/report"
    assert required["scheme"] == "exact" and required["network"] == "base-sepolia"
    assert required["maxAmountRequired"] == "10000"
    assert required["extra"] == {"name": "USDC", "version": "2"}
    assert "mistakes" not in data


def test_report_requires_settlement_and_returns_receipt(client, monkeypatch):
    path = completed(client)
    calls = facilitator(monkeypatch)
    response = client.get(path + "/report", headers={"X-PAYMENT": payment(), "Origin": "http://localhost:5173"})
    assert response.status_code == 200
    assert [p for p, body in calls] == ["/x402/verify", "/x402/settle"]
    assert calls[0][1] == calls[1][1]
    assert json.loads(base64.b64decode(response.headers["X-PAYMENT-RESPONSE"])) == RECEIPT
    assert response.headers["cache-control"] == "no-store"
    assert response.headers["access-control-expose-headers"] == "X-PAYMENT-RESPONSE"
    assert response.json()["total_usd"] == client.get(path + "/summary").json()["total_usd"]
    assert response.json()["mistakes"][0]["explanation_source"] == "fallback"


@pytest.mark.parametrize("header", ["garbage", "e30=", "bnVsbA==", "a" * 16385,
                                      payment(network="base"), payment(x402Version=2)])
def test_invalid_header_never_calls_facilitator(client, monkeypatch, header):
    path = completed(client)
    calls = facilitator(monkeypatch)
    response = client.get(path + "/report", headers={"X-PAYMENT": header})
    assert response.status_code == 402 and calls == []
    assert "mistakes" not in response.json()


@pytest.mark.parametrize("field,value", [("value", "1"), ("value", 10000),
    ("to", PAYER), ("nonce", "bad-nonce"), ("validBefore", "bad-time")])
def test_wrong_authorization_is_rejected_locally(client, monkeypatch, field, value):
    path = completed(client)
    calls = facilitator(monkeypatch)
    decoded = json.loads(base64.b64decode(payment()))
    decoded["payload"]["authorization"][field] = value
    header = base64.b64encode(json.dumps(decoded).encode()).decode()
    assert client.get(path + "/report", headers={"X-PAYMENT": header}).status_code == 402
    assert calls == []


def test_replayed_payment_does_not_release_another_report(client, monkeypatch):
    path = completed(client)
    requests = []
    def handle(request):
        requests.append(request.url.path)
        if request.url.path.endswith("/verify"):
            return httpx.Response(200, json={"isValid": True})
        settled = requests.count("/x402/settle") == 1
        return httpx.Response(200, json=RECEIPT if settled else {"success": False, "errorReason": "nonce_used"})
    original = gate.verify_and_settle
    def invoke(header, required):
        with httpx.Client(transport=httpx.MockTransport(handle)) as transport:
            return original(header, required, transport)
    monkeypatch.setattr(gate, "verify_and_settle", invoke)
    assert client.get(path + "/report", headers={"X-PAYMENT": payment()}).status_code == 200
    assert client.get(path + "/report", headers={"X-PAYMENT": payment()}).status_code == 402


@pytest.mark.parametrize("verify,settle,status,expected,calls_count", [
    ({"isValid": False}, None, 200, 402, 1),
    ({"isValid": "true"}, None, 200, 402, 1),
    (None, {"success": False}, 200, 402, 2),
    (None, {"success": "true"}, 200, 402, 2),
    (None, {**RECEIPT, "network": "base"}, 200, 503, 2),
    (None, {**RECEIPT, "transaction": ""}, 200, 503, 2),
    (None, {**RECEIPT, "payer": PAY_TO}, 200, 503, 2),
    (None, None, 500, 503, 1),
])
def test_facilitator_failures_never_release_report(client, monkeypatch, verify, settle, status, expected, calls_count):
    path = completed(client)
    calls = facilitator(monkeypatch, verify, settle, status)
    response = client.get(path + "/report", headers={"X-PAYMENT": payment()})
    assert response.status_code == expected and len(calls) == calls_count
    assert "mistakes" not in response.json() and "X-PAYMENT-RESPONSE" not in response.headers


def test_settlement_timeout_is_not_retried(monkeypatch):
    monkeypatch.setenv("X402_PAY_TO", PAY_TO)
    monkeypatch.setenv("X402_FACILITATOR_URL", "https://facilitator.example")
    calls = []
    def handle(request):
        calls.append(request.url.path)
        if request.url.path == "/verify":
            return httpx.Response(200, json={"isValid": True})
        raise httpx.ReadTimeout("unknown settlement outcome")
    with httpx.Client(transport=httpx.MockTransport(handle)) as client:
        with pytest.raises(HTTPException) as error:
            gate.verify_and_settle(payment(), gate.requirements("https://example/report"), client)
    assert error.value.status_code == 503 and calls == ["/verify", "/settle"]


def test_unconfigured_gate_and_invalid_attempts(client, monkeypatch):
    path = completed(client)
    calls = facilitator(monkeypatch)
    monkeypatch.delenv("X402_PAY_TO")
    assert client.get(path + "/report").status_code == 503
    assert client.get(path + "/summary").status_code == 200
    assert client.get("/api/v1/sessions/missing/mistake-cost/report").status_code == 404
    wrong = completed(client, "peg_transfer")
    assert client.get(wrong + "/report").status_code == 409
    sid = client.post("/api/v1/sessions", json={"trainee_id": "new", "exercise": "lap_chole"}).json()["id"]
    assert client.get(f"/api/v1/sessions/{sid}/mistake-cost/summary").status_code == 409
    assert calls == []


def test_existing_api_key_still_required(client, monkeypatch):
    path = completed(client)
    monkeypatch.setenv("API_KEY", "test-api-key")
    assert client.get(path + "/summary").status_code == 401
    assert client.get(path + "/summary", headers={"X-API-Key": "test-api-key"}).status_code == 200
