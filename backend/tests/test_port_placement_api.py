"""Port placement through the HTTP API (docs/PORT_PLACEMENT.md)."""
import database as db
import pytest
from fastapi.testclient import TestClient
from sqlalchemy import create_engine
from sqlalchemy.pool import StaticPool

SITES = {"umbilical": [0.0, 0.0, 0.0], "epigastric": [-0.19, 0.0, 0.01],
         "rsc_medial": [-0.15, 0.0, 0.10], "rsc_lateral": [-0.07, 0.0, 0.17]}


@pytest.fixture()
def client(monkeypatch):
    monkeypatch.delenv("GEMINI_API_KEY", raising=False)
    import main
    eng = create_engine("sqlite://", connect_args={"check_same_thread": False}, poolclass=StaticPool)
    db.Base.metadata.create_all(eng)
    db.SessionLocal.configure(bind=eng)
    return TestClient(main.app)


def _running(client):
    r = client.post("/api/v1/sessions", json={"trainee_id": "pp", "exercise": "port_placement"})
    assert r.status_code == 201 and r.json()["exercise"] == "port_placement"
    sid = r.json()["id"]
    client.post(f"/api/v1/sessions/{sid}/start")
    return sid


def _events(blind_epigastric=False):
    events = [
        {"event_id": "lm_t", "kind": "landmark", "t_ms": 0, "object_id": "target", "source": [-0.10, -0.08, 0.05]},
        {"event_id": "lm_m", "kind": "landmark", "t_ms": 0, "object_id": "midline", "source": [0.0, 0.0, 0.0]},
    ]
    for i, (pid, pos) in enumerate(SITES.items()):
        e = {"event_id": f"p_{pid}", "kind": "port_placed", "t_ms": 1000 * (i + 1), "object_id": pid,
             "source": pos, "target": pos, "value": 0.0}
        if pid == "epigastric" and blind_epigastric:
            e["detail"] = "blind"
        events.append(e)
    return events


def test_port_placement_rejects_other_kinds(client):
    sid = _running(client)
    r = client.post(f"/api/v1/sessions/{sid}/events", json={"events": [{"event_id": "d", "kind": "drop", "t_ms": 0}]})
    assert r.status_code == 422 and "port_placement" in r.json()["detail"]


def test_full_port_placement_session(client):
    sid = _running(client)
    assert client.post(f"/api/v1/sessions/{sid}/events", json={"events": _events()}).json()["accepted"] == 6
    m = client.post(f"/api/v1/sessions/{sid}/complete", json={"elapsed_s": 60}).json()
    assert m["exercise"] == "port_placement" and m["successful_transfers"] == 4 and m["drops"] == 0
    assert m["practice_score"] > 90 and m["details"]["triangulation"] == 1.0
    a = client.get(f"/api/v1/sessions/{sid}/assessment").json()
    assert {c["id"] for c in a["criteria"]} >= {"port_site_error", "manipulation_angle", "safety_errors"}
    c = client.post(f"/api/v1/sessions/{sid}/coach").json()
    assert c["source"] == "fallback" and c["next_exercise_id"].startswith("port_placement")
    assert client.get(f"/api/v1/sessions/{sid}/cost").status_code == 200


def test_blind_port_caps_score_and_reaches_coach(client):
    sid = _running(client)
    client.post(f"/api/v1/sessions/{sid}/events", json={"events": _events(blind_epigastric=True)})
    m = client.post(f"/api/v1/sessions/{sid}/complete", json={"elapsed_s": 60}).json()
    assert m["drops"] == 1 and m["practice_score"] == 40.0
    c = client.post(f"/api/v1/sessions/{sid}/coach").json()
    assert c["next_exercise_id"] == "port_placement_safety"
    assert c["tips"][0]["event_id"] == "p_epigastric"
