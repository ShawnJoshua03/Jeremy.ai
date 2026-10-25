"""Difficulty contract, deterministic scoring, spoken thresholds and legacy SQLite migration."""
import pytest
from fastapi.testclient import TestClient
from sqlalchemy import create_engine, inspect, text
from sqlalchemy.orm import sessionmaker
from sqlalchemy.pool import StaticPool

import database as db
import port_coach
import scoring
import voice_chat


@pytest.fixture
def client(monkeypatch):
    import main
    engine = create_engine("sqlite://", connect_args={"check_same_thread": False}, poolclass=StaticPool)
    db.init_db(engine)
    monkeypatch.setattr(db, "SessionLocal", sessionmaker(bind=engine, expire_on_commit=False))
    with TestClient(main.app) as api:
        yield api
    engine.dispose()


@pytest.mark.parametrize("extra,expected", [({}, "normal"), ({"difficulty": None}, "normal"),
                                           ({"difficulty": "normal"}, "normal"), ({"difficulty": "demo"}, "demo")])
def test_create_get_and_persist_difficulty(client, extra, expected):
    response = client.post("/api/v1/sessions", json={"trainee_id": "difficulty", "exercise": "lap_chole", **extra})
    assert response.status_code == 201
    row = response.json()
    assert row["difficulty"] == expected
    assert client.get(f"/api/v1/sessions/{row['id']}").json()["difficulty"] == expected
    assert client.get("/api/v1/sessions").json()[0]["difficulty"] == expected
    with db.SessionLocal() as session:
        assert session.get(db.TrainingSession, row["id"]).difficulty == expected


@pytest.mark.parametrize("bad", ["easy", "Demo", "", " normal ", 2, True, [], {}])
def test_invalid_difficulty_is_422(client, bad):
    assert client.post("/api/v1/sessions", json={"trainee_id": "difficulty", "difficulty": bad}).status_code == 422


def test_idempotent_create_includes_difficulty(client):
    body = {"trainee_id": "difficulty", "exercise": "lap_chole", "client_request_id": "same"}
    first = client.post("/api/v1/sessions", json=body).json()
    replay = client.post("/api/v1/sessions", json={**body, "difficulty": None})
    assert replay.status_code == 200 and replay.json()["id"] == first["id"]
    assert client.post("/api/v1/sessions", json={**body, "difficulty": "demo"}).status_code == 409


def placement(mm=20, deg=25, port="umbilical", depth=55):
    return {"kind": "port_placed", "event_id": "port1", "t_ms": 1000, "object_id": port,
            "source": [mm / 1000, 0, 0], "target": [0, 0, 0], "value": deg, "detail": f"depth_mm={depth}"}


@pytest.mark.parametrize("exercise", ["lap_chole", "port_placement"])
def test_same_offset_scoring_coaching_reports_and_cost(client, exercise):
    scores = {}
    for difficulty, max_mm, max_deg, minor in (("normal", 15, 20, 1), ("demo", 30, 40, 0)):
        sid = client.post("/api/v1/sessions", json={"trainee_id": "difficulty", "exercise": exercise,
                                                   "difficulty": difficulty}).json()["id"]
        url = f"/api/v1/sessions/{sid}"
        assert client.post(url + "/start").status_code == 200
        assert client.post(url + "/events", json={"events": [placement()]}).status_code == 200
        live = client.get(url + "/metrics").json()
        metrics = client.post(url + "/complete", json={"elapsed_s": 10}).json()
        assert live["difficulty"] == difficulty and live["invalid_releases"] == minor
        assert metrics["difficulty"] == difficulty
        assert metrics["invalid_releases"] == minor
        scores[difficulty] = metrics["practice_score"]
        assert client.post(url + "/complete", json={"elapsed_s": 99}).json() == metrics
        assert client.get(url).json()["metrics"]["difficulty"] == difficulty

        coaching = client.post(url + "/coach/placement").json()
        assert coaching["difficulty"] == difficulty
        assert coaching["thresholds"]["location_mm"] == max_mm
        assert coaching["thresholds"]["angle_deg"] == max_deg
        assert coaching["thresholds"]["depth_mm"]["umbilical"] == {"ideal": 40, "tolerance": 10 if minor else 20}
        finding = coaching["findings"][0]
        for key in ("passed", "location_ok", "angle_ok", "depth_ok"):
            assert finding[key] is (difficulty == "demo")

        assessment = client.get(url + "/assessment").json()
        criterion_id = "port_accuracy" if exercise == "lap_chole" else "port_site_error"
        criterion = next(c for c in assessment["criteria"] if c["id"] == criterion_id)
        assert f"developing if <= {max_mm}" in criterion["rule"]
        assert (criterion["level"] == "needs_work") is (difficulty == "normal")
        cost = client.get(url + "/cost").json()
        report = client.post(url + "/coach").json()
        for payload in (assessment, cost, report, coaching["debrief"]):
            assert payload["difficulty"] == difficulty
        for report_text in (assessment["thresholds_note"], cost["disclaimer"], report["summary"], coaching["debrief"]["text"]):
            assert (scoring.DEMO_NOTE in report_text) is (difficulty == "demo")
        assert "Illustrative estimate, not billing data" in cost["disclaimer"]
        talk = client.post(url + "/coach/talk", json={"text": "what are the tolerances"}).json()
        assert f"within {port_coach.mm_words(max_mm)}" in talk["reply_text"]
        assert f"within {port_coach.deg_words(max_deg)}" in talk["reply_text"]
    assert scores["demo"] > scores["normal"]


@pytest.mark.parametrize("difficulty,mm,deg", [("normal", 15, 20), ("demo", 30, 40)])
@pytest.mark.parametrize("port", list(port_coach.DEPTH_TARGETS_MM))
def test_inclusive_limits_and_depth_tolerances(difficulty, mm, deg, port):
    ideal, normal_tol = port_coach.DEPTH_TARGETS_MM[port]
    tolerance = normal_tol * scoring.tolerance_multiplier(difficulty)
    for depth in (ideal - tolerance, ideal + tolerance):
        assert port_coach.analyze(placement(mm, deg, port, depth), difficulty=difficulty).passed
    assert not port_coach.analyze(placement(mm + 0.1, deg, port, ideal), difficulty=difficulty).passed
    assert not port_coach.analyze(placement(mm, deg + 0.1, port, ideal), difficulty=difficulty).passed
    assert not port_coach.analyze(placement(mm, deg, port, ideal + tolerance + 0.1), difficulty=difficulty).passed


def test_demo_voice_limits_without_placements_and_for_failed_placement():
    empty = voice_chat.Facts.from_coaching(port_coach.coach_placements([], difficulty="demo"), "lap_chole")
    reply = voice_chat.answer(empty, "what are the tolerances", voice_chat.ConversationState())["reply_text"]
    assert "thirty millimetres" in reply and "forty degrees" in reply
    assert scoring.DEMO_NOTE in empty.debrief["text"]
    failed = port_coach.analyze(placement(35, 5), difficulty="demo")
    assert "thirty millimetre limit" in voice_chat._limit_phrase(failed, "location")
    assert "forty degree limit" in voice_chat._limit_phrase(failed, "angle")


def test_demo_preserves_major_safety_errors():
    events = [scoring.Event("injury", 1000, object_id="cbd"), scoring.Event("port_placed", 2000,
              object_id="umbilical", source=(0.02, 0, 0), target=(0, 0, 0), value=25)]
    for difficulty in ("normal", "demo"):
        result = scoring.compute_for_exercise("lap_chole", [], events, 10, difficulty)
        assert result.drops == 1
        assert result.practice_score <= scoring.LC_MAJOR_CAP


def test_previous_session_comparison_requires_same_difficulty(client):
    for difficulty, mm in (("normal", 90), ("demo", 25), ("normal", 80), ("demo", 20)):
        sid = client.post("/api/v1/sessions", json={"trainee_id": "comparison", "exercise": "lap_chole",
                                                   "difficulty": difficulty}).json()["id"]
        assert client.post(f"/api/v1/sessions/{sid}/events", json={"events": [placement(mm)]}).status_code == 200
    coaching = client.post(f"/api/v1/sessions/{sid}/coach/placement").json()
    assert "five millimetres closer than last session" in coaching["debrief"]["text"]


def test_report_tip_and_validated_text_use_demo_difficulty():
    import coach
    event = scoring.Event("port_placed", 1000, object_id="umbilical", source=(0.05, 0, 0),
                          target=(0, 0, 0), value=45, event_id="bad-port")
    metrics = scoring.compute_for_exercise("lap_chole", [], [event], 10, "demo").__dict__
    errors = coach.lap_chole_errors(scoring.lap_chole_findings([event], "demo"))
    report = coach.fallback(metrics, errors)
    assert "within 30 mm and 40 degrees" in report.tips[0].text
    assert any("under 10 mm" in tip.text for tip in report.tips)
    # Validation labels generated text deterministically; no provider request is made.
    validated = coach.validate({"summary": "Practice score reviewed.", "tips": []}, errors, metrics)
    assert validated.difficulty == "demo" and scoring.DEMO_NOTE in validated.summary


def test_existing_sqlite_rows_backfilled_and_migration_is_repeatable(tmp_path):
    engine = create_engine(f"sqlite:///{tmp_path / 'legacy.db'}")
    with engine.begin() as connection:
        connection.execute(text("CREATE TABLE sessions (id VARCHAR PRIMARY KEY, trainee_id VARCHAR, "
                                "exercise VARCHAR, status VARCHAR, created_at DATETIME, started_at DATETIME, "
                                "completed_at DATETIME, elapsed_s FLOAT, metrics JSON)"))
        connection.execute(text("INSERT INTO sessions (id, trainee_id, exercise, status, created_at) "
                                "VALUES ('old', 'trainee', 'lap_chole', 'ready', '2026-01-01')"))
    db.init_db(engine)
    db.init_db(engine)
    column = next(c for c in inspect(engine).get_columns("sessions") if c["name"] == "difficulty")
    assert not column["nullable"]
    with sessionmaker(bind=engine)() as session:
        assert session.get(db.TrainingSession, "old").difficulty == "normal"
        session.add(db.TrainingSession(trainee_id="new", exercise="lap_chole"))
        session.commit()
        assert session.query(db.TrainingSession).filter_by(trainee_id="new").one().difficulty == "normal"
    with engine.begin() as connection:
        connection.execute(text("INSERT INTO sessions (id, trainee_id) VALUES ('raw', 'raw')"))
        assert connection.scalar(text("SELECT difficulty FROM sessions WHERE id='raw'")) == "normal"
    engine.dispose()
