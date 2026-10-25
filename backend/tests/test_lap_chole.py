"""Laparoscopic cholecystectomy (docs/LAP_CHOLE.md): events, DB migration, scoring, cost, coach,
assessment, Convex mirror and the fake Quest."""
import json

import assessment
import coach
import convex_mirror
import cost
import database as db
import pytest
import scoring
from fastapi.testclient import TestClient
from sqlalchemy import create_engine, inspect, text
from sqlalchemy.pool import StaticPool
from tools import fake_quest


@pytest.fixture()
def client(monkeypatch):
    monkeypatch.delenv("ANTHROPIC_API_KEY", raising=False)
    import main
    eng = create_engine("sqlite://", connect_args={"check_same_thread": False}, poolclass=StaticPool)
    db.Base.metadata.create_all(eng)
    db.SessionLocal.configure(bind=eng)
    return TestClient(main.app)


def _running(client, exercise="lap_chole"):
    r = client.post("/api/v1/sessions", json={"trainee_id": "lc", "exercise": exercise})
    assert r.status_code == 201 and r.json()["exercise"] == exercise
    sid = r.json()["id"]
    client.post(f"/api/v1/sessions/{sid}/start")
    return sid


def E(kind, t_ms, object_id=None, source=None, target=None, value=None, detail=None, eid=None):
    return scoring.Event(kind, t_ms, "right", object_id, source, target, value, detail, eid or f"{kind}-{t_ms}")


def steps(n=15, t0=1000):
    return [E("step_completed", t0 + 1000 * i, s, value=t0 / 1000 + i) for i, s in enumerate(scoring.LC_STEPS[:n])]


def clean_events():
    ev = steps()
    ev += [E("port_placed", 100 + i, p, (0, 0.1, 0.001 * i), (0, 0.1, 0), value=5.0) for i, p in
           enumerate(scoring.LC_PORTS)]
    ev += [E("insufflation", 1500, value=13.0), E("critical_view", 8000, value=100.0)]
    ev += [E("clip_applied", 9000 + i, "cystic_duct", (0, 0, 0), value=v, detail="ok") for i, v in
           enumerate((5.0, 8.0, 20.0))]
    ev += [E("clip_applied", 10000 + i, "cystic_artery", (0, 0, 0), value=v, detail="ok") for i, v in
           enumerate((4.0, 7.0, 18.0))]
    ev += [E("structure_cut", 11500, "cystic_duct", (0, 0, 0), value=12.0),
           E("structure_cut", 11600, "cystic_artery", (0, 0, 0), value=12.0)]
    return ev


def codes(events):
    return [f.code for f in scoring.lap_chole_findings(events)]


# ---------- API and storage ----------

def test_lap_chole_session_and_kind_validation(client):
    lc = _running(client)
    wc = _running(client, "wound_closure")
    body = {"events": [{"event_id": "s1", "kind": "step_completed", "t_ms": 5, "object_id": "veress", "value": 4.2}]}
    assert client.post(f"/api/v1/sessions/{wc}/events", json=body).status_code == 422
    r = client.post(f"/api/v1/sessions/{lc}/events", json={"events": [{"event_id": "d", "kind": "drop", "t_ms": 1}]})
    assert r.status_code == 422 and "lap_chole" in r.json()["detail"]
    assert client.post(f"/api/v1/sessions/{lc}/events", json=body).json() == {"accepted": 1, "duplicates": 0}
    assert client.post("/api/v1/sessions", json={"trainee_id": "x", "exercise": "appendectomy"}).status_code == 422


def test_value_and_detail_stored_and_returned(client):
    sid = _running(client)
    client.post(f"/api/v1/sessions/{sid}/events", json={"events": [
        {"event_id": "c1", "kind": "clip_applied", "t_ms": 10, "object_id": "cystic_duct",
         "source": [-0.06, 0.035, 0.03], "value": 6.5, "detail": "crossing"},
        {"event_id": "i1", "kind": "injury", "t_ms": 20, "object_id": "liver", "detail": "grasper tear"}]})
    rows = client.get(f"/api/v1/sessions/{sid}/events").json()
    assert rows[0]["value"] == 6.5 and rows[0]["detail"] == "crossing"
    assert rows[1]["value"] is None and rows[1]["detail"] == "grasper tear"


def test_existing_kinds_unaffected_by_new_fields(client):
    sid = _running(client, "peg_transfer")
    client.post(f"/api/v1/sessions/{sid}/events", json={"events": [
        {"event_id": "x", "kind": "drop", "t_ms": 5, "object_id": "peg_1"}]})
    row = client.get(f"/api/v1/sessions/{sid}/events").json()[0]
    assert row["value"] is None and row["detail"] is None and row["kind"] == "drop"


@pytest.mark.parametrize("extra", [{"value": "nan"}, {"value": "inf"}, {"detail": "x" * 65}])
def test_value_and_detail_validated(client, extra):
    sid = _running(client)
    ev = {"event_id": "e", "kind": "insufflation", "t_ms": 1, "value": 13.0} | extra
    raw = json.dumps({"events": [ev]}).replace('"nan"', "NaN").replace('"inf"', "Infinity")
    r = client.post(f"/api/v1/sessions/{sid}/events", content=raw, headers={"Content-Type": "application/json"})
    assert r.status_code == 422


def test_init_db_adds_value_and_detail_to_an_existing_events_table():
    eng = create_engine("sqlite://", connect_args={"check_same_thread": False}, poolclass=StaticPool)
    with eng.begin() as conn:
        conn.execute(text("CREATE TABLE events (pk INTEGER PRIMARY KEY, session_id VARCHAR, event_id VARCHAR, "
                          "kind VARCHAR, t_ms INTEGER, instrument VARCHAR, object_id VARCHAR, source JSON, "
                          "target JSON, UNIQUE (session_id, event_id))"))
        conn.execute(text("INSERT INTO events (session_id, event_id, kind, t_ms) VALUES ('s', 'old', 'drop', 1)"))
    db.init_db(eng)
    db.init_db(eng)  # idempotent
    columns = {c["name"] for c in inspect(eng).get_columns("events")}
    assert {"value", "detail"} <= columns
    with eng.begin() as conn:
        assert conn.execute(text("SELECT value, detail FROM events WHERE event_id = 'old'")).one() == (None, None)


def test_full_lap_chole_session_via_api(client):
    sid = _running(client)
    events = [
        {"event_id": f"step-{s}", "kind": "step_completed", "t_ms": 1000 * (i + 1), "object_id": s, "value": i + 1.0}
        for i, s in enumerate(scoring.LC_STEPS)
    ] + [
        {"event_id": "p1", "kind": "port_placed", "t_ms": 900, "object_id": "epigastric",
         "source": [-0.15, 0.11, 0.01], "target": [-0.15, 0.11, -0.01], "value": 25.0},  # 20 mm and 25 deg: minor
        {"event_id": "ins", "kind": "insufflation", "t_ms": 2000, "value": 16.0},  # minor
        {"event_id": "cv", "kind": "critical_view", "t_ms": 8000, "value": 100.0},
        {"event_id": "cbd", "kind": "clip_applied", "t_ms": 8500, "object_id": "cbd", "source": [0, 0, 0],
         "value": 15.0, "detail": "ok"},  # major
        {"event_id": "cut", "kind": "structure_cut", "t_ms": 10500, "object_id": "cystic_duct",
         "source": [0, 0, 0], "value": 10.0},  # no clips: unsafe, major
    ]
    assert client.post(f"/api/v1/sessions/{sid}/events", json={"events": events}).json()["accepted"] == 20
    m = client.post(f"/api/v1/sessions/{sid}/complete", json={"elapsed_s": 200}).json()
    assert m["exercise"] == "lap_chole" and m["mean_bite_error_mm"] is None
    assert (m["successful_transfers"], m["completion_pct"], m["drops"], m["invalid_releases"]) == (15, 100.0, 2, 2)
    d = m["details"]
    assert d["cbd_errors"] == 1 and d["unsafe_divisions"] == 1 and d["bile_duct_injury"] is True
    assert d["ports_mean_error_mm"] == pytest.approx(20.0) and d["insufflation_mmhg"] == 16.0
    assert m["practice_score"] <= scoring.LC_MAJOR_CAP
    assert client.get(f"/api/v1/sessions/{sid}").json()["metrics"] == m

    c = client.get(f"/api/v1/sessions/{sid}/cost").json()
    labels = [l["label"] for l in c["lines"]]
    assert labels[2].startswith("Laparoscopic instruments") and c["lines"][2]["assumed"] is False
    assert "Hem-o-lok" in labels[3] and c["lines"][3]["assumed"] is False and c["lines"][3]["source"]
    assert "Trocars" in labels[4] and c["lines"][4]["quantity_assumed"] is True
    assert c["lines"][5]["label"] == "Expected cost of major errors" and c["lines"][5]["quantity"] == 2
    assert "Bile duct injury" in labels[6] and c["lines"][6]["assumed"] is True

    coaching = client.post(f"/api/v1/sessions/{sid}/coach").json()
    texts = " ".join(t["text"] for t in coaching["tips"])
    assert "common bile duct" in texts and "Major error" in texts
    assert {t["event_id"] for t in coaching["tips"]} >= {"cbd", "cut"}
    assert coaching["next_exercise"]["exercise_id"] == "lap_chole"
    assert "15 of 15 steps" in coaching["summary"] and "2 major errors" in coaching["summary"]

    a = client.get(f"/api/v1/sessions/{sid}/assessment").json()
    crit = {c["id"]: c for c in a["criteria"]}
    assert list(crit) == ["completion", "time", "port_accuracy", "insufflation", "critical_view",
                          "clipping_safety", "major_errors", "path_efficiency"]
    assert crit["major_errors"]["level"] == "needs_work" and crit["insufflation"]["value"] == 1.0
    progress = client.get("/api/v1/trainees/lc/progress").json()
    assert progress[0]["exercise"] == "lap_chole" and progress[0]["errors"] == 4
    assert progress[0]["cost_total_usd"] == c["total_usd"]


def test_other_exercises_have_null_details(client):
    for ex in ("peg_transfer", "wound_closure"):
        sid = _running(client, ex)
        m = client.post(f"/api/v1/sessions/{sid}/complete", json={"elapsed_s": 10}).json()
        assert m["details"] is None
        assert "details" not in db.SessionLocal().get(db.TrainingSession, sid).metrics


def test_live_preview_for_running_lap_chole(client):
    sid = _running(client)
    client.post(f"/api/v1/sessions/{sid}/events", json={"events": [
        {"event_id": "s1", "kind": "step_completed", "t_ms": 5, "object_id": "veress", "value": 0.005}]})
    m = client.get(f"/api/v1/sessions/{sid}/metrics").json()
    assert m["successful_transfers"] == 1 and m["completion_pct"] == pytest.approx(6.7)
    assert m["details"]["steps_completed"] == 1


# ---------- scoring ----------

def test_clean_attempt_scores_high_with_no_errors():
    m = scoring.compute_lap_chole_metrics([], clean_events(), 120.0)
    assert (m.successful_transfers, m.drops, m.invalid_releases, m.completion_pct) == (15, 0, 0, 100.0)
    assert m.details["safe_divisions"] == 2 and m.details["critical_view_reached"] is True
    assert m.details["bile_duct_injury"] is False
    # no samples -> efficiency 0
    port_acc = sum(1 - 0.5 * i / 15 - 0.5 * 5 / 20 for i in range(5)) / 5
    expected = 100 * (0.35 + 0.15 * port_acc + 0.10 + 0.10 + 0 + 0.10 * 0.8 + 0.10)
    assert m.practice_score == pytest.approx(expected, abs=0.05)


def test_steps_unique_and_unknown_ignored():
    ev = steps(3) + [E("step_completed", 9000, "veress"), E("step_completed", 9100, "bogus")]
    m = scoring.compute_lap_chole_metrics([], ev, 10)
    assert m.successful_transfers == 3 and m.completion_pct == pytest.approx(20.0)


def test_safe_division_rule():
    def cut_with(clip_mm, cut_mm=10.0):
        ev = [E("critical_view", 0, value=100.0)]
        ev += [E("clip_applied", 10 + i, "cystic_duct", value=v, detail="ok") for i, v in enumerate(clip_mm)]
        return codes(ev + [E("structure_cut", 100, "cystic_duct", value=cut_mm)])
    assert cut_with((5, 8, 15)) == []
    assert cut_with((5, 8, 12)) == []  # exactly 2 mm away still counts
    assert cut_with((5, 15)) == ["unsafe_division"]  # one proximal
    assert cut_with((5, 8)) == ["unsafe_division"]  # no distal
    assert sorted(cut_with((5, 8, 9, 15))) == ["clip_too_close"]  # 1 mm from the cut: minor only
    assert sorted(cut_with((5, 9, 15))) == ["clip_too_close", "unsafe_division"]
    # a clip on another structure or applied after the cut does not count
    ev = [E("critical_view", 0, value=100.0), E("clip_applied", 1, "cystic_artery", value=5),
          E("clip_applied", 2, "cystic_duct", value=6), E("clip_applied", 3, "cystic_duct", value=20),
          E("structure_cut", 100, "cystic_duct", value=10), E("clip_applied", 200, "cystic_duct", value=4)]
    assert codes(ev) == ["unsafe_division"]


def test_division_before_critical_view_is_major():
    ev = [E("clip_applied", 10 + i, "cystic_artery", value=v) for i, v in enumerate((3, 6, 20))]
    ev += [E("critical_view", 50, value=80.0), E("structure_cut", 100, "cystic_artery", value=12),
           E("step_completed", 200, "dissect")]
    assert codes(ev) == ["division_before_cvs"]
    m = scoring.compute_lap_chole_metrics([], ev, 10)
    assert m.drops == 1 and m.details["divisions_before_cvs"] == 1 and m.details["safe_divisions"] == 0
    assert m.details["critical_view_pct"] == 80.0


def test_cbd_clip_cut_and_injury_are_major():
    ev = [E("clip_applied", 10, "cbd", value=10, detail="ok"), E("structure_cut", 20, "cbd", value=12),
          E("injury", 30, "bowel", detail="veress"), E("step_completed", 1, "dissect")]
    m = scoring.compute_lap_chole_metrics([], ev, 10)
    assert codes(ev) == ["cbd_clip", "cbd_cut", "injury"]
    assert m.drops == 3 and m.details["cbd_errors"] == 2 and m.details["injuries"] == 1
    assert m.details["bile_duct_injury"] is True and m.practice_score <= 40
    injury_only = scoring.compute_lap_chole_metrics([], [E("injury", 30, "cbd")], 10)
    assert injury_only.details["bile_duct_injury"] is True


def test_minor_errors_ports_pressure_crossing():
    ev = [E("port_placed", 1, "umbilical", (0, 0, 0.016), (0, 0, 0), value=3.0),  # 16 mm
          E("port_placed", 2, "epigastric", (0, 0, 0.005), (0, 0, 0), value=-21.0),  # 21 deg
          E("port_placed", 3, "rsc_medial", (0, 0, 0.015), (0, 0, 0), value=20.0),  # on the limits: ok
          E("insufflation", 4, value=11.0), E("insufflation", 5, value=14.0),  # final value counts
          E("clip_applied", 6, "cystic_duct", value=5, detail="crossing")]
    assert codes(ev) == ["port_offset", "port_offset", "clip_crossing"]
    m = scoring.compute_lap_chole_metrics([], ev, 10)
    assert (m.drops, m.invalid_releases) == (0, 3)
    assert m.details["insufflation_mmhg"] == 14.0 and m.details["ports_max_error_mm"] == pytest.approx(16.0)
    assert m.details["ports_mean_angle_deg"] == pytest.approx((3 + 21 + 20) / 3, abs=0.01)
    assert codes([E("insufflation", 4, value=15.5)]) == ["insufflation_out_of_range"]


def test_dispatch_and_kinds():
    assert type(scoring.compute_for_exercise("lap_chole", [], [], 1)) is scoring.LapCholeMetrics
    assert scoring.EVENT_KINDS["lap_chole"] == {"step_completed", "port_placed", "insufflation", "critical_view",
                                                "clip_applied", "structure_cut", "ductotomy", "cholangiogram",
                                                "injury", "hand_collision"}
    assert len(scoring.LC_STEPS) == 15 and scoring.LC_STEPS[8:11] == ["clip_duct_distal", "ductotomy", "cholangiogram"]
    collision = E("hand_collision", 10, "abdominal_wall", value=12.0, detail="dissect")
    assert codes([collision]) == ["hand_collision"]
    assert scoring.compute_lap_chole_metrics([], [collision], 1).invalid_releases == 1


def test_ductotomy_rules():
    cv = E("critical_view", 0, value=100.0)
    distal = E("clip_applied", 10, "cystic_duct", value=24.0, detail="ok")
    assert codes([cv, distal, E("ductotomy", 20, "cystic_duct", value=20.0)]) == []
    assert codes([cv, E("ductotomy", 20, "cystic_duct", value=20.0)]) == ["unsafe_ductotomy"]
    assert codes([cv, distal, E("ductotomy", 20, "cystic_duct", value=26.0)]) == ["unsafe_ductotomy"]  # clip below it
    assert codes([cv, distal, E("ductotomy", 20, "cbd", value=20.0)]) == ["ductotomy_wrong_structure"]
    assert codes([cv, distal, E("ductotomy", 20, "cystic_artery", value=20.0)]) == ["ductotomy_wrong_structure"]
    early = [distal, E("ductotomy", 20, "cystic_duct", value=20.0), E("critical_view", 30, value=100.0)]
    assert codes(early) == ["ductotomy_before_cvs"]
    m = scoring.compute_lap_chole_metrics([], [cv, distal, E("ductotomy", 20, "cbd", value=20.0)], 10)
    assert m.drops == 1 and m.details["unsafe_ductotomies"] == 1 and m.details["bile_duct_injury"] is True
    assert m.details["ductotomies"] == 1 and m.practice_score <= 40


def test_video_order_duct_division_uses_the_distal_clip():
    # distal clip, ductotomy, cholangiogram, then two proximal clips and the cut between them
    ev = [E("critical_view", 0, value=100.0), E("clip_applied", 10, "cystic_duct", value=24.0),
          E("ductotomy", 20, "cystic_duct", value=20.0), E("cholangiogram", 30, value=12.0, detail="ok"),
          E("clip_applied", 40, "cystic_duct", value=6.0), E("clip_applied", 50, "cystic_duct", value=9.0),
          E("structure_cut", 60, "cystic_duct", value=16.0)]
    assert codes(ev) == []
    m = scoring.compute_lap_chole_metrics([], ev, 10)
    assert m.details["safe_divisions"] == 1 and m.details["cholangiogram_result"] == "ok"
    assert m.details["cholangiogram_depth_mm"] == 12.0 and m.details["cholangiogram_errors"] == 0
    # without the distal clip the same cut is unsafe
    assert codes([e for e in ev if not (e.kind == "clip_applied" and e.value == 24.0)]) == [
        "unsafe_ductotomy", "unsafe_division"]


@pytest.mark.parametrize("result,code", [("not_in_duct", "cholangiogram_not_in_duct"),
                                         ("leak", "cholangiogram_leak"), ("ok", None)])
def test_cholangiogram_minor_errors(result, code):
    m = scoring.compute_lap_chole_metrics([], [E("cholangiogram", 5, value=8.0, detail=result)], 10)
    assert codes([E("cholangiogram", 5, value=8.0, detail=result)]) == ([code] if code else [])
    assert (m.drops, m.invalid_releases) == (0, 1 if code else 0)


def test_coach_tips_for_ductotomy_and_cholangiogram(monkeypatch):
    monkeypatch.delenv("ANTHROPIC_API_KEY", raising=False)
    ev = [E("ductotomy", 1000, "cbd", value=20.0, eid="dct"), E("cholangiogram", 2000, value=3.0,
                                                                   detail="not_in_duct", eid="gram")]
    errors = coach.lap_chole_errors(scoring.lap_chole_findings(ev))
    c = coach.generate({**LC, "drops": 2, "invalid_releases": 1, "details": {**LC["details"], "cholangiogram_errors": 1}},
                       errors)
    texts = " ".join(t.text for t in c.tips)
    assert "Ductotomy made in the common bile duct" in texts and "not in the cystic duct" in texts
    assert c.next_exercise_id == "lap_chole_clipping"


# ---------- cost ----------

def test_lap_chole_cost_lines(monkeypatch):
    # The original fallback remains available when no sourced supply prices exist.
    monkeypatch.setattr(cost.pricing, "load_prices", lambda: {})
    c = cost.estimate_cost(300, 1, 0.5, exercise="lap_chole", bile_duct_injury=True)
    assert [l.assumed for l in c.lines] == [False, False, False, True, True, True]
    assert c.lines[2].total_usd == 48.80 and c.lines[3].total_usd == cost.ASSUMED_LAP_CHOLE_CLIPS_TROCARS_COST
    assert c.lines[5].total_usd == cost.COMPLICATION_EXTRA_COST
    assert c.counterfactual_clean_usd == pytest.approx(292.42)  # rounded $93.62 OR line + supplies
    plain = cost.estimate_cost(300, 0, 0.5, exercise="lap_chole", instruments="disposable")
    assert len(plain.lines) == 5 and plain.lines[2].total_usd == 395.00
    # the flag does nothing for other exercises
    assert cost.estimate_cost(60, 1, 0.5, bile_duct_injury=True) == cost.estimate_cost(60, 1, 0.5)


# ---------- coach and assessment ----------

LC = {"exercise": "lap_chole", "completion_pct": 100.0, "successful_transfers": 15, "drops": 0,
      "invalid_releases": 0, "elapsed_s": 200.0, "distance_by_instrument": {"left": 0.4, "right": 0.9},
      "total_distance_m": 1.3, "path_efficiency": 0.5, "practice_score": 88.0,
      "details": {"ports_mean_error_mm": 3.0, "insufflation_mmhg": 13.0, "critical_view_pct": 100.0,
                  "clipping_errors": 0, "divisions_before_cvs": 0}}


def _lc(**details):
    return {**LC, "details": {**LC["details"], **details}}


@pytest.mark.parametrize("metrics,drill", [
    (LC, "lap_chole"),
    (_lc(divisions_before_cvs=1), "lap_chole_critical_view"),
    (_lc(critical_view_pct=50.0), "lap_chole_critical_view"),
    (_lc(clipping_errors=1), "lap_chole_clipping"),
    (_lc(ports_mean_error_mm=20.0), "lap_chole_port_placement"),
    (_lc(insufflation_mmhg=17.0), "lap_chole_port_placement"),
])
def test_lap_chole_drill_choice(monkeypatch, metrics, drill):
    monkeypatch.delenv("ANTHROPIC_API_KEY", raising=False)
    c = coach.generate(metrics, [])
    assert c.next_exercise_id == drill and c.next_exercise.exercise_id == "lap_chole"


def test_lap_chole_drills_allowlisted():
    ids = set(coach.drills_for("lap_chole"))
    assert ids == {"lap_chole", "lap_chole_port_placement", "lap_chole_critical_view", "lap_chole_clipping"}


def test_lap_chole_tips_from_findings(monkeypatch):
    monkeypatch.delenv("ANTHROPIC_API_KEY", raising=False)
    ev = [E("port_placed", 1000, "rsc_lateral", (0, 0, 0.018), (0, 0, 0), value=5, eid="port"),
          E("clip_applied", 2000, "cystic_artery", value=4, detail="crossing", eid="cross"),
          E("structure_cut", 3000, "cystic_duct", value=10, eid="early")]
    errors = coach.lap_chole_errors(scoring.lap_chole_findings(ev))
    assert errors[0]["severity"] == "major"  # majors first
    c = coach.generate(_lc(ports_mean_error_mm=18.0, critical_view_pct=60.0), errors, question="was my clipping safe?")
    texts = [t.text for t in c.tips]
    assert "without two proximal and one distal clip" in texts[0]  # the early cut is also unsafe
    assert "divided the cystic duct before reaching the critical view of safety" in texts[1]
    assert any("rsc lateral port was 18 mm" in t for t in texts)
    assert any("did not cross" in t for t in texts) or len(texts) == 4
    assert "clipping and division errors" in c.answer.lower()


def test_lap_chole_prompt_names_exercise_and_steps(claude):
    claude.reply({"summary": "ok", "tips": [], "next_exercise_id": "wound_closure_spacing"})
    c = coach.generate(_lc(clipping_errors=2), [])
    prompt = claude.prompts[0]
    assert "laparoscopic cholecystectomy" in prompt and "critical view of safety" in prompt
    assert all(s in prompt for s in scoring.LC_STEPS)
    enum = claude.calls[0]["schema"]["properties"]["next_exercise_id"]["enum"]
    assert all(e.startswith("lap_chole") for e in enum)
    assert c.source == "claude" and c.next_exercise_id == "lap_chole_clipping"  # other-exercise drill rejected


def test_lap_chole_assessment_levels():
    a = assessment.assess(_lc(ports_mean_error_mm=10.0, insufflation_mmhg=10.5, critical_view_pct=None,
                              clipping_errors=1))
    crit = {c.id: c for c in a.criteria}
    assert crit["port_accuracy"].level == "developing" and crit["port_accuracy"].unit == "mm"
    assert crit["insufflation"].value == 1.5 and crit["insufflation"].level == "developing"
    assert crit["critical_view"].level == "not_assessed"
    assert crit["clipping_safety"].level == "developing" and crit["major_errors"].level == "proficient"
    assert crit["time"].level == "proficient"
    unfinished = assessment.assess({**LC, "completion_pct": 50.0, "drops": 1})
    u = {c.id: c for c in unfinished.criteria}
    assert u["time"].level == "not_assessed" and u["major_errors"].level == "needs_work"
    assert "15 steps" in u["time"].rule


# ---------- Convex mirror ----------

def test_mirror_sends_value_detail_and_details_only_when_present():
    with_fields = convex_mirror._event({"session_id": "s", "event_id": "e", "kind": "clip_applied", "t_ms": 1,
                                        "value": 6.0, "detail": "ok"})
    assert with_fields["value"] == 6.0 and with_fields["detail"] == "ok"
    plain = convex_mirror._event({"session_id": "s", "event_id": "e", "kind": "drop", "t_ms": 1,
                                  "value": None, "detail": None})
    assert "value" not in plain and "detail" not in plain

    class Row:
        id, trainee_id, exercise, status = "s", "t", "lap_chole", "completed"
        created_at = started_at = completed_at = None
        elapsed_s = 1.0
        metrics = {**LC}
    assert convex_mirror._session(Row)["metrics"]["details"] == LC["details"]
    Row.metrics = {k: v for k, v in LC.items() if k != "details"}
    assert "details" not in convex_mirror._session(Row)["metrics"]


# ---------- fake quest ----------

def test_lap_chole_trace_shape():
    samples, events, elapsed = fake_quest.build_lap_chole_trace()
    done = [e["object_id"] for e in events if e["kind"] == "step_completed"]
    assert done == scoring.LC_STEPS  # the 15 steps, in the video's order
    assert sum(1 for e in events if e["kind"] == "port_placed") == 5
    assert [e.get("detail") for e in events if e["kind"] == "clip_applied"].count("crossing") == 1
    assert {s["instrument"] for s in samples} == {"left", "right"} and elapsed == samples[-1]["t_ms"] / 1000
    kinds = [e["kind"] for e in events]
    assert kinds.count("ductotomy") == 1 and kinds.count("cholangiogram") == 1
    unsafe = fake_quest.build_lap_chole_trace(unsafe=True)[1]
    assert any(e["kind"] == "clip_applied" and e["object_id"] == "cbd" for e in unsafe)


def test_lap_chole_replay_against_testclient(client):
    report = fake_quest.replay(client, exercise="lap_chole")
    m = report["metrics"]
    assert (m["successful_transfers"], m["drops"], m["invalid_releases"]) == (15, 0, 1)
    assert m["details"]["safe_divisions"] == 2 and m["details"]["crossing_clips"] == 1
    assert 0 < m["details"]["ports_mean_error_mm"] < 15 and 0 < m["path_efficiency"] < 1
    assert m["practice_score"] > 60
    assert not any("Bile duct" in l["label"] for l in report["cost"]["lines"])
    assert report["coach"]["next_exercise_id"] == "lap_chole_clipping"

    bad = fake_quest.replay(client, exercise="lap_chole", lap_chole_unsafe=True)
    bm = bad["metrics"]
    assert bm["drops"] == 2 and bm["details"]["cbd_errors"] == 1 and bm["practice_score"] <= 40
    assert any("Bile duct" in l["label"] for l in bad["cost"]["lines"])
    assert "common bile duct" in bad["coach"]["tips"][0]["text"]


def test_demo_leaves_out_ductotomy_and_cholangiogram():
    ev = [e for e in clean_events() if not (e.kind == "step_completed" and e.object_id in scoring.DEMO_SKIPPED_STEPS)]
    normal = scoring.compute_lap_chole_metrics([], ev, 120.0)
    demo = scoring.compute_lap_chole_metrics([], ev, 120.0, "demo")
    assert normal.completion_pct == pytest.approx(13 / 15 * 100, abs=0.1)
    assert demo.completion_pct == 100.0
    assert demo.successful_transfers == 13 and demo.details["steps_total"] == 13
