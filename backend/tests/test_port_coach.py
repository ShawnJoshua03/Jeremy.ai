"""Spoken port-placement coaching (port_coach.py, port_coach_router.py). Cartesia is mocked; no network."""
import database as db
import httpx
import port_coach
import pytest
from fastapi.testclient import TestClient
from speech import cartesia
from sqlalchemy import create_engine
from sqlalchemy.pool import StaticPool

SITE = (-0.15, 0.11, -0.01)  # epigastric site from tools/fake_quest.py


def placed(port="epigastric", dx_mm=0.0, dz_mm=0.0, angle=5.0, detail=None, t_ms=1000, eid=None):
    src = (SITE[0] + dx_mm / 1000, SITE[1], SITE[2] + dz_mm / 1000)
    return {"event_id": eid or f"p-{port}-{t_ms}", "kind": "port_placed", "t_ms": t_ms, "object_id": port,
            "instrument": "right", "source": list(src), "target": list(SITE), "value": angle, "detail": detail}


@pytest.fixture(autouse=True)
def _no_network(monkeypatch):
    def boom(*_a, **_k):
        raise AssertionError("network call attempted")
    import main  # noqa: F401  main runs load_dotenv(override=True) on import; clear keys after it
    monkeypatch.setattr(httpx.HTTPTransport, "handle_request", boom)  # real transport only; TestClient has its own
    for key in ("CARTESIA_API_KEY", "GEMINI_API_KEY", "CONVEX_URL"):
        monkeypatch.delenv(key, raising=False)


@pytest.fixture()
def client():
    import main
    eng = create_engine("sqlite://", connect_args={"check_same_thread": False}, poolclass=StaticPool)
    db.Base.metadata.create_all(eng)
    db.SessionLocal.configure(bind=eng)
    return TestClient(main.app)


def _session(client, events, trainee="pc", exercise="lap_chole"):
    sid = client.post("/api/v1/sessions", json={"trainee_id": trainee, "exercise": exercise}).json()["id"]
    client.post(f"/api/v1/sessions/{sid}/start")
    if events:
        r = client.post(f"/api/v1/sessions/{sid}/events", json={"events": events})
        assert r.status_code == 200, r.text
    return sid


# ---------- direction and sign ----------
@pytest.mark.parametrize("dx,dz,where,fix", [
    (20, 0, "toward the feet", "toward the head"),
    (-20, 0, "toward the head", "toward the feet"),
    (0, 20, "to the patient's right", "to the patient's left"),
    (0, -20, "to the patient's left", "to the patient's right"),
])
def test_direction_signs(dx, dz, where, fix):
    f = port_coach.analyze(placed(dx_mm=dx, dz_mm=dz))
    assert f.offset_mm == 20.0 and f.location_ok is False and f.worst == "location"
    assert f.direction == where
    assert f.correction == f"move about twenty millimetres {fix}"
    text = port_coach.cue(f)
    assert f"twenty millimetres off the site, {where}" in text and fix in text.split(".")[1]


def test_combined_direction_names_both_axes():
    f = port_coach.analyze(placed(dx_mm=-12, dz_mm=9))
    assert f.direction == "toward the head and to the patient's right"
    assert f.correction == "move about twelve millimetres toward the feet and nine millimetres to the patient's left"
    assert f.offset_head_feet_mm == -12.0 and f.offset_left_right_mm == 9.0


def test_small_component_is_not_spoken():
    f = port_coach.analyze(placed(dx_mm=18, dz_mm=2))
    assert f.direction == "toward the feet"


# ---------- depth ----------
def test_depth_too_shallow_and_too_deep():
    ideal, tol = port_coach.DEPTH_TARGETS_MM["epigastric"]
    shallow = port_coach.analyze(placed(detail=f"depth_mm={ideal - 18:g}"))
    assert shallow.depth_error_mm == -18.0 and shallow.depth_ok is False and shallow.worst == "depth"
    assert shallow.correction == "go eighteen millimetres deeper"
    assert "eighteen millimetres too shallow" in port_coach.cue(shallow)

    deep = port_coach.analyze(placed(detail=f"depth_mm={ideal + 15:g};tilt=feet"))
    assert deep.depth_error_mm == 15.0 and deep.depth_ok is False and deep.tilt == "feet"
    assert "fifteen millimetres too deep" in port_coach.cue(deep)
    assert "stop fifteen millimetres sooner" in deep.correction

    inside = port_coach.analyze(placed(detail=f"depth_mm={ideal + tol:g}"))
    assert inside.depth_ok is True and inside.passed


@pytest.mark.parametrize("detail", [None, "", "ok", "depth_mm=abc", "depth_mm=nan", "tilt=sideways"])
def test_missing_or_bad_depth_is_not_assessed(detail):
    f = port_coach.analyze(placed(detail=detail))
    assert f.depth_mm is None and f.depth_ok is None and f.depth_error_mm is None
    assert f.passed and f.tilt is None
    assert port_coach.cue(f)


# ---------- angle ----------
def test_angle_fix_uses_tilt_when_sent():
    flat = port_coach.analyze(placed(angle=25, detail="tilt=flat"))
    assert flat.worst == "angle" and flat.correction == "angle the trocar more upright, you were twenty-five degrees off"
    text = port_coach.cue(flat)
    assert "angle was twenty-five degrees off" in text and "more upright" in text
    plain = port_coach.analyze(placed(angle=-25))  # sign of value ignored, as scoring.port_error
    assert plain.angle_deg == 25.0 and "planned path" in plain.correction


def test_worst_criterion_first_and_threshold_boundary():
    f = port_coach.analyze(placed(dx_mm=16, angle=40))  # 16/15 < 40/20
    assert f.worst == "angle"
    assert port_coach.cue(f).startswith("Epigastric port angle was forty degrees off")
    edge = port_coach.analyze(placed(dx_mm=15, angle=20))
    assert edge.location_ok and edge.angle_ok and edge.passed


# ---------- spoken form ----------
def test_number_words():
    assert [port_coach.words(n) for n in (0, 7, 13, 20, 22, 99, 105)] == [
        "zero", "seven", "thirteen", "twenty", "twenty-two", "ninety-nine", "one hundred and five"]
    assert port_coach.mm_words(0.3) == "under one millimetre" and port_coach.mm_words(1) == "one millimetre"


def test_cues_stay_short_and_have_no_digits():
    cases = [placed(dx_mm=-31, dz_mm=27, angle=33, detail="depth_mm=70;tilt=upright"),
             placed(dz_mm=4, detail="depth_mm=12"), placed(), placed(dx_mm=150, angle=80)]
    for e in cases:
        f = port_coach.analyze(e)
        for prev in (None, port_coach.analyze(placed(dx_mm=60))):
            text = port_coach.cue(f, prev)
            assert text.strip() and port_coach.count_words(text) <= port_coach.MAX_CUE_WORDS, text
            assert not any(ch.isdigit() for ch in text), text


# ---------- session-level (pure) ----------
def test_all_in_tolerance_praise():
    events = [placed(p, dx_mm=2, angle=4, detail=f"depth_mm={port_coach.DEPTH_TARGETS_MM[p][0]:g}", t_ms=100 * i)
              for i, p in enumerate(port_coach.DEPTH_TARGETS_MM)]
    out = port_coach.coach_placements(events)
    assert all(c["kind"] == "praise" and " is good" in c["text"] for c in out["cues"])
    d = out["debrief"]
    assert d["in_tolerance"] == d["total"] == 5 and d["worst_port"] is None
    assert d["text"].startswith("All five entry sites in tolerance.")
    assert port_coach.DISCLAIMER in d["text"]


def test_debrief_names_biggest_miss_and_fix():
    events = [placed("umbilical", dx_mm=3, t_ms=1), placed("epigastric", dx_mm=22, t_ms=2),
              placed("rsc_medial", dz_mm=16, t_ms=3), placed("rsc_lateral", t_ms=4)]
    d = port_coach.coach_placements(events)["debrief"]
    assert d["lines"][0] == "Two of four ports in tolerance."
    assert d["lines"][1] == "Biggest miss was the epigastric port, twenty-two millimetres toward the feet."
    assert d["lines"][2] == "Next time, move about twenty-two millimetres toward the head."
    assert d["worst_port"] == "epigastric"
    assert all(port_coach.count_words(line) <= port_coach.MAX_CUE_WORDS for line in d["lines"])


def test_no_placements():
    out = port_coach.coach_placements([])
    assert out["cues"] == [] and out["debrief"]["total"] == 0 and out["debrief"]["text"]


def test_retry_improvement_wording():
    out = port_coach.coach_placements([placed(dx_mm=24, t_ms=1), placed(dx_mm=12, t_ms=2), placed(dx_mm=20, t_ms=3)])
    assert [c["attempt"] for c in out["cues"]] == [1, 2, 3]
    assert "Twelve millimetres closer than last try." in out["cues"][1]["text"]
    assert "Eight millimetres further off than last try." in out["cues"][2]["text"]
    assert "Your epigastric port was four millimetres closer than on your first try." in out["debrief"]["text"]


def test_previous_session_comparison():
    out = port_coach.coach_placements([placed(dx_mm=10)], [placed(dx_mm=22)])
    assert "Twelve millimetres closer than last session." in out["cues"][0]["text"]


# ---------- API ----------
def test_api_placement_from_stored_events(client):
    sid = _session(client, [placed(dx_mm=-20, detail="depth_mm=40", t_ms=1),
                            placed(dx_mm=-5, detail="depth_mm=41", t_ms=2)])
    r = client.post(f"/api/v1/sessions/{sid}/coach/placement")
    assert r.status_code == 200
    body = r.json()
    assert [c["kind"] for c in body["cues"]] == ["correction", "praise"]
    assert "toward the head" in body["cues"][0]["text"] and "toward the feet" in body["cues"][0]["text"]
    assert "Fifteen millimetres closer than last try." in body["cues"][1]["text"]
    assert all(c["text"] and c["port"] == "epigastric" for c in body["cues"])
    assert body["debrief"]["text"] and body["speech_available"] is False and body["source"] == "template"
    assert body["thresholds"]["location_mm"] == 15.0 and body["thresholds"]["angle_deg"] == 20.0


def test_api_compares_with_previous_session(client):
    _session(client, [placed(dz_mm=30)], trainee="repeat")
    sid = _session(client, [placed(dz_mm=8)], trainee="repeat")
    cue = client.post(f"/api/v1/sessions/{sid}/coach/placement").json()["cues"][0]["text"]
    assert "Twenty-two millimetres closer than last session." in cue
    off = client.post(f"/api/v1/sessions/{sid}/coach/placement", json={"compare_previous_session": False})
    assert "last session" not in off.json()["cues"][0]["text"]


def test_api_errors(client):
    assert client.post("/api/v1/sessions/nope/coach/placement").status_code == 404
    peg = _session(client, [], exercise="peg_transfer")
    assert client.post(f"/api/v1/sessions/{peg}/coach/placement").status_code == 409
    empty = _session(client, [])
    assert client.post(f"/api/v1/sessions/{empty}/coach/placement").json()["cues"] == []
    assert client.post(f"/api/v1/sessions/{empty}/coach/placement/audio").status_code == 404


def test_audio_503_without_key_keeps_text(client):
    sid = _session(client, [placed(dx_mm=20)])
    r = client.post(f"/api/v1/sessions/{sid}/coach/placement/audio", json={"cue": "latest"})
    assert r.status_code == 503
    assert r.json()["detail"] == "Speech is unavailable; use text feedback."
    assert r.headers["cache-control"] == "no-store"
    assert client.post(f"/api/v1/sessions/{sid}/coach/placement").json()["cues"][0]["text"]


def test_audio_speaks_the_chosen_cue(client, monkeypatch):
    spoken = []
    monkeypatch.setattr(cartesia, "text_to_audio", lambda text: spoken.append(text) or b"RIFF....WAVEfake")
    sid = _session(client, [placed(dx_mm=20, t_ms=1), placed(dz_mm=-18, t_ms=2)])
    body = client.post(f"/api/v1/sessions/{sid}/coach/placement").json()
    for which, expected in ((0, body["cues"][0]["text"]), ("latest", body["cues"][1]["text"]),
                            ("debrief", body["debrief"]["text"])):
        r = client.post(f"/api/v1/sessions/{sid}/coach/placement/audio", json={"cue": which})
        assert r.status_code == 200 and r.headers["content-type"] == "audio/wav"
        assert spoken[-1] == expected
    assert client.post(f"/api/v1/sessions/{sid}/coach/placement/audio", json={"cue": 9}).status_code == 404


def test_audio_rate_limited(client, monkeypatch):
    monkeypatch.setenv("COACH_RATE_LIMIT_PER_MIN", "1")
    monkeypatch.setattr(cartesia, "text_to_audio", lambda text: b"RIFF....WAVEfake")
    sid = _session(client, [placed()])
    assert client.post(f"/api/v1/sessions/{sid}/coach/placement/audio").status_code == 200
    assert client.post(f"/api/v1/sessions/{sid}/coach/placement/audio").status_code == 429


def test_port_placement_session_is_coached_and_blind_tag_is_tolerated(client):
    # port_placement uses detail for "under_view"/"blind" (docs/PORT_PLACEMENT.md); depth_mm rides alongside it.
    ev = placed(dx_mm=20)
    ev["detail"] = "blind;depth_mm=26"
    sid = _session(client, [ev], exercise="port_placement")
    out = client.post(f"/api/v1/sessions/{sid}/coach/placement").json()
    assert out["cues"] and "too shallow" in out["cues"][0]["text"]
    ev2 = placed(dx_mm=20)
    ev2["detail"] = "under_view"
    sid2 = _session(client, [ev2], exercise="port_placement")
    assert client.post(f"/api/v1/sessions/{sid2}/coach/placement").status_code == 200


@pytest.mark.parametrize("dx,dz", [(-18, 18), (21, -12), (150, 130), (40, -30)])
def test_two_axis_miss_still_speaks_the_fix_within_the_word_budget(dx, dz):
    text = port_coach.cue(port_coach.analyze(placed(dx_mm=dx, dz_mm=dz, angle=5)))
    assert "move" in text.lower() and port_coach.count_words(text) <= port_coach.MAX_CUE_WORDS
