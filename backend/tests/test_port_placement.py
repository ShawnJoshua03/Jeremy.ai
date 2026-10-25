"""Port placement exercise (docs/PORT_PLACEMENT.md): scoring, findings, assessment, coach. No API here."""
import assessment
import coach
import pytest
import scoring

E = scoring.Event
TARGET = (-0.10, -0.08, 0.05)  # Calot's triangle: 10 cm cephalad, 8 cm deep, 5 cm to the patient's right
SITES = {"umbilical": (0.0, 0.0, 0.0), "epigastric": (-0.19, 0.0, 0.01),
         "rsc_medial": (-0.15, 0.0, 0.10), "rsc_lateral": (-0.07, 0.0, 0.17)}


def landmarks(target=TARGET, midline=(0.0, 0.0, 0.0)):
    out = []
    if target is not None:
        out.append(E("landmark", 0, None, "target", target, event_id="lm_target"))
    if midline is not None:
        out.append(E("landmark", 0, None, "midline", midline, event_id="lm_midline"))
    return out


def port(pid, t_ms, src=None, ideal=None, angle=0.0, detail=None, eid=None):
    return E("port_placed", t_ms, None, pid, src or SITES[pid], ideal or SITES[pid], angle, detail,
             eid or f"{pid}_{t_ms}")


def ideal_attempt():
    """Two landmarks, then umbilical, epigastric, rsc_medial, rsc_lateral at indexes 2-5."""
    return landmarks() + [port(p, 1000 * (i + 1)) for i, p in enumerate(scoring.PP_PORTS)]


def metrics(events, elapsed_s=100.0):
    return scoring.compute_for_exercise("port_placement", [], events, elapsed_s)


def codes(events):
    return [f.code for f in scoring.port_placement_findings(events)]


# ---------- geometry ----------

def test_angle_helper():
    assert scoring._angle_deg((1, 0, 0), (0, 1, 0)) == pytest.approx(90)
    assert scoring._angle_deg((1, 0, 0), (1, 0, 0)) == pytest.approx(0)
    assert scoring._angle_deg((0, 0, 0), (1, 0, 0)) is None


def test_ideal_layout_angles():
    geo = scoring.port_geometry(ideal_attempt())
    assert 45 <= geo["manipulation_angle_deg"] <= 75  # Hanna 1997 range
    assert geo["azimuth_difference_deg"] < 10
    assert geo["elevation_right_deg"] > 0 and geo["elevation_left_deg"] > 0  # ports above a deep target


def test_geometry_needs_target_and_ports():
    assert scoring.port_geometry(landmarks(target=None) + ideal_attempt()[2:]) == {}
    only_camera = landmarks() + [port("umbilical", 1000)]
    assert "manipulation_angle_deg" not in scoring.port_geometry(only_camera)


@pytest.mark.parametrize("deg,expected", [
    (None, 0.0), (20, 0.0), (30, 0.0), (37.5, 0.5), (45, 1.0), (60, 1.0), (75, 1.0), (82.5, 0.5), (90, 0.0), (120, 0.0),
])
def test_triangulation_score(deg, expected):
    assert scoring.triangulation_score(deg) == pytest.approx(expected)


# ---------- findings ----------

def test_ideal_attempt_has_no_findings():
    assert codes(ideal_attempt()) == []


def test_working_port_before_camera_is_major_once_per_port():
    ev = landmarks() + [port("epigastric", 500), port("epigastric", 600), port("umbilical", 1000),
                        port("rsc_medial", 2000), port("rsc_lateral", 3000)]
    early = [f for f in scoring.port_placement_findings(ev) if f.code == "working_port_before_camera"]
    assert [(f.severity, f.event.object_id, f.event.t_ms) for f in early] == [("major", "epigastric", 500)]


def test_no_camera_port_flags_every_working_port():
    ev = landmarks() + [port(p, 1000 * i) for i, p in enumerate(("epigastric", "rsc_medial", "rsc_lateral"), 1)]
    assert codes(ev).count("working_port_before_camera") == 3


def test_blind_working_port_is_major():
    ev = ideal_attempt()
    ev[3] = port("epigastric", 2000, detail="blind")
    assert "blind_working_port" in codes(ev)
    ev[3] = port("epigastric", 2000, detail="under_view")
    assert "blind_working_port" not in codes(ev)


@pytest.mark.parametrize("z,flagged", [(0.039, False), (0.041, True), (0.06, True), (0.079, True), (0.081, False),
                                       (-0.06, True)])
def test_epigastric_vessel_zone(z, flagged):
    """Saber 2004: the vessels usually lie 4-8 cm from the midline, either side."""
    ev = ideal_attempt()
    ev[4] = port("rsc_medial", 3000, src=(-0.15, 0.0, z), ideal=(-0.15, 0.0, z))
    assert ("epigastric_vessel_zone" in codes(ev)) is flagged


def test_vessel_zone_uses_midline_landmark_and_skips_without_it():
    # midline moved to z=0.10: umbilical (100 mm), epigastric (90 mm) and rsc_medial (0 mm) are outside the
    # band; only rsc_lateral (70 mm) is inside
    shifted = landmarks(midline=(0.0, 0.0, 0.10)) + ideal_attempt()[2:]
    flagged = [f.event.object_id for f in scoring.port_placement_findings(shifted)
               if f.code == "epigastric_vessel_zone"]
    assert flagged == ["rsc_lateral"]
    no_midline = landmarks(midline=None) + ideal_attempt()[2:]
    assert "epigastric_vessel_zone" not in codes(no_midline)


def test_port_offset_and_retry_replaces_placement():
    off = (-0.19, 0.0, 0.03)  # 20 mm from the epigastric site
    ev = ideal_attempt() + [port("epigastric", 9000, src=off)]
    found = [f for f in scoring.port_placement_findings(ev) if f.code == "port_offset"]
    assert len(found) == 1 and found[0].info["error_mm"] == pytest.approx(20)
    ev.append(port("epigastric", 9500))  # a retry back onto the site clears it
    assert "port_offset" not in codes(ev)
    assert "port_offset" in codes(ideal_attempt() + [port("rsc_lateral", 9000, angle=25)])


def test_ports_crowded_once_per_pair():
    ev = ideal_attempt()
    ev[5] = port("rsc_lateral", 4000, src=(-0.15, 0.0, 0.13), ideal=(-0.15, 0.0, 0.13))  # 30 mm from rsc_medial
    found = [f for f in scoring.port_placement_findings(ev) if f.code == "ports_crowded"]
    assert len(found) == 1 and found[0].info["other"] == "rsc_medial"


# ---------- metrics ----------

def test_ideal_attempt_metrics():
    m = metrics(ideal_attempt(), elapsed_s=60)
    assert (m.exercise, m.successful_transfers, m.drops, m.invalid_releases) == ("port_placement", 4, 0, 0)
    assert m.completion_pct == 100.0
    assert m.details["ports_mean_error_mm"] == 0.0
    assert m.details["triangulation"] == 1.0
    assert m.practice_score > 90


def test_empty_attempt_scores_zero():
    m = metrics([], elapsed_s=5)
    assert m.practice_score == 0.0
    assert m.details["ports_placed"] == 0 and "ports_mean_error_mm" not in m.details


def test_major_error_caps_score():
    ev = ideal_attempt()
    ev[3] = port("epigastric", 2000, detail="blind")
    m = metrics(ev, elapsed_s=60)
    assert m.drops == 1 and m.practice_score == scoring.PP_MAJOR_CAP


def test_score_formula_by_hand():
    ev = landmarks() + [port("umbilical", 1000), port("epigastric", 2000, src=(-0.19, 0.0, 0.0175))]  # 7.5 mm off
    m = metrics(ev, elapsed_s=150)
    site = (1.0 + (1 - 0.5 * 7.5 / 15)) / 2
    completion = 0.5
    # no triangulation or azimuth without rsc_medial; no minor errors; time factor 0.5
    expected = 100 * (0.35 * site + 0.20 * completion + completion * (0.10 * 1 + 0.05 * 0.5))
    assert m.practice_score == pytest.approx(round(expected, 1))


def test_score_stays_in_range():
    wild = landmarks() + [port(p, 1000 * i, src=(1.0, 0.0, 1.0), angle=90) for i, p in enumerate(scoring.PP_PORTS)]
    assert 0 <= metrics(wild, elapsed_s=3600).practice_score <= 100


def test_details_are_flat_scalars():
    for v in metrics(ideal_attempt()).details.values():
        assert isinstance(v, (bool, int, float, str))


def test_other_exercises_unchanged():
    assert type(scoring.compute_for_exercise("peg_transfer", [], [], 0)) is scoring.Metrics
    assert scoring.EVENT_KINDS["port_placement"] == {"port_placed", "landmark"}


# ---------- assessment ----------

def test_assessment_levels():
    m = metrics(ideal_attempt(), elapsed_s=60).__dict__
    a = {c.id: c for c in assessment.assess(m).criteria}
    assert set(a) == {"completion", "time", "port_site_error", "manipulation_angle", "azimuth_symmetry",
                      "safety_errors"}
    assert all(c.level == "proficient" for c in a.values())


def test_assessment_not_assessed_without_data():
    m = metrics(landmarks(target=None) + [port("umbilical", 1000)], elapsed_s=30).__dict__
    a = {c.id: c for c in assessment.assess(m).criteria}
    assert a["manipulation_angle"].level == "not_assessed"
    assert a["time"].level == "not_assessed"  # unfinished
    assert "all 4 ports" in a["time"].rule


def test_manipulation_band_matches_hanna():
    assert assessment.level_for("manipulation_angle", 15, "port_placement") == "proficient"  # 45 or 75 degrees
    assert assessment.level_for("manipulation_angle", 16, "port_placement") == "developing"


# ---------- coach ----------

def test_coach_fallback_and_drills(monkeypatch):
    monkeypatch.delenv("ANTHROPIC_API_KEY", raising=False)
    ev = ideal_attempt()
    ev[3] = port("epigastric", 2000, detail="blind", eid="blind1")
    m = metrics(ev).__dict__
    errors = coach.port_placement_errors(scoring.port_placement_findings(ev))
    c = coach.generate(m, errors, exercise="port_placement")
    assert c.source == "fallback"
    assert c.next_exercise_id == "port_placement_safety"
    assert c.tips[0].event_id == "blind1"
    assert "4 ports" in c.summary
    assert coach.choose_drill(metrics(ideal_attempt()).__dict__, "port_placement") == "port_placement"


def test_coach_answer_points_at_metric(monkeypatch):
    monkeypatch.delenv("ANTHROPIC_API_KEY", raising=False)
    m = metrics(ideal_attempt()).__dict__
    c = coach.generate(m, [], question="Was my angle ok?", exercise="port_placement")
    assert "manipulation angle" in c.answer.lower()


def test_coach_validate_rejects_other_exercise_drill():
    m = metrics(ideal_attempt()).__dict__
    c = coach.validate({"summary": "ok", "tips": [], "next_exercise_id": "peg_transfer"}, [], m,
                       exercise="port_placement")
    assert c.next_exercise_id.startswith("port_placement")
