import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(__file__)))
os.environ["DATABASE_URL"] = "sqlite:///:memory:"

import pytest
from fastapi.testclient import TestClient
from sqlalchemy import create_engine
from sqlalchemy.pool import StaticPool

import database as db
import tools.fake_quest as fake_quest


@pytest.fixture()
def client(monkeypatch):
    monkeypatch.delenv("GEMINI_API_KEY", raising=False)
    import main
    eng = create_engine("sqlite://", connect_args={"check_same_thread": False}, poolclass=StaticPool)
    db.Base.metadata.create_all(eng)
    db.SessionLocal.configure(bind=eng)
    return TestClient(main.app)


def test_trace_is_six_objects_two_drops_at_25hz():
    samples, events, elapsed_s = fake_quest.build_trace()
    assert fake_quest.HZ == 25 and fake_quest.DT_MS == 40
    assert len(samples) == 6 * 25
    assert all(samples[i + 1]["t_ms"] - samples[i]["t_ms"] == 40 for i in range(len(samples) - 1))
    assert sum(1 for e in events if e["kind"] == "transfer_success") == 6
    assert sum(1 for e in events if e["kind"] == "drop") == 2
    assert elapsed_s == samples[-1]["t_ms"] / 1000


def test_replay_against_testclient(client):
    report = fake_quest.replay(client)
    assert report["sample_upload"] == {"accepted": 150, "duplicates": 0}
    assert report["event_upload"] == {"accepted": 8, "duplicates": 0}
    metrics = report["metrics"]
    assert metrics["successful_transfers"] == 6
    assert metrics["drops"] == 2
    assert metrics["invalid_releases"] == 0
    assert metrics["elapsed_s"] == report["elapsed_s"]
    assert 0 <= metrics["practice_score"] <= 100
    cost = report["cost"]
    assert cost["total_usd"] > 0 and len(cost["lines"]) == 4
    assert cost["lines"][-1]["assumed"] is True
    coach = report["coach"]
    assert coach["source"] == "fallback"
    tip_ids = {t["event_id"] for t in coach["tips"]}
    assert "drop-2" in tip_ids and "drop-5" in tip_ids
    assert not any(str(i).startswith("xfer-") for i in tip_ids if i)
