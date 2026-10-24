import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(__file__)))

import scoring


def S(i, t, x, valid=True, inst="left"):
    return scoring.Sample(inst, i, t, (x, 0.0, 0.0), valid)


def test_empty_samples_and_events_score_only_the_baseline():
    m = scoring.compute_metrics([], [], 0)
    assert m.successful_transfers == 0
    assert m.drops == 0 and m.invalid_releases == 0
    assert m.distance_by_instrument == {} and m.total_distance_m == 0.0
    assert m.path_efficiency == 0.0 and m.completion_pct == 0
    assert m.practice_score == 30.0  # time and error budgets unspent


def test_empty_samples_with_events_keeps_error_score():
    ev = [scoring.Event("drop", 1), scoring.Event("invalid_release", 2)]
    m = scoring.compute_metrics([], ev, 600)
    assert m.drops == 1 and m.invalid_releases == 1
    assert m.total_distance_m == 0.0 and m.path_efficiency == 0.0
    assert m.practice_score == round(100 * 0.10 * (1 - 2 / 6), 1)  # only error factor lives


def test_single_sample_counts_no_distance():
    m = scoring.compute_metrics([S(0, 0, 5.0)], [], 0)
    assert m.distance_by_instrument == {"left": 0.0}
    assert m.total_distance_m == 0.0 and m.path_efficiency == 0.0
    assert m.practice_score == round(100 * (0.20 + 0.10), 1)


def test_single_sample_per_instrument():
    m = scoring.compute_metrics([S(0, 0, 0, inst="left"), S(0, 0, 0, inst="right")], [], 60)
    assert m.distance_by_instrument == {"left": 0.0, "right": 0.0}
    assert m.total_distance_m == 0.0


def test_all_invalid_samples_add_no_distance_and_kill_efficiency():
    ev = [scoring.Event("transfer_success", 5, "left", "o1", (0, 0, 0), (0.1, 0, 0))]
    samples = [S(i, i * 40, 0.1 * i, valid=False) for i in range(6)]
    m = scoring.compute_metrics(samples, ev, 60)
    assert m.total_distance_m == 0.0
    assert m.path_efficiency == 0.0  # ideal > 0 but travelled distance is 0
    assert m.completion_pct == round(1 / 6 * 100, 1)
    # completion + unspent time budget + no errors; efficiency 0
    assert m.practice_score == round(100 * (0.45 * (1 / 6) + 0.20 * (1 - 60 / 300) + 0.10), 1)


def test_all_invalid_samples_with_no_transfers():
    samples = [S(i, i * 40, 0.5, valid=False) for i in range(10)]
    m = scoring.compute_metrics(samples, [scoring.Event("drop", 1) for _ in range(3)], 100)
    assert m.total_distance_m == 0.0 and m.drops == 3 and m.path_efficiency == 0.0
    assert m.practice_score == round(100 * (0.20 * (1 - 100 / 300) + 0.10 * (1 - 3 / 6)), 1)


def test_boundary_error_budget_spent_kills_only_error_factor():
    ev = [scoring.Event("drop", i) for i in range(6)]  # exactly ERROR_BUDGET
    m = scoring.compute_metrics([], ev, 0)
    assert m.practice_score == 20.0


def test_boundary_elapsed_at_time_budget():
    m = scoring.compute_metrics([], [], 300)  # exactly TIME_BUDGET_S
    assert m.practice_score == round(100 * (0.20 * (1 - 300 / 300) + 0.10), 1)
