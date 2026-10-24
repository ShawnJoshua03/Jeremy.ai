import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(__file__)))

import cost


def test_clean_efficient_run_has_no_avoidable_cost():
    b = cost.estimate_cost(elapsed_s=600, errors=0, path_efficiency=1.0)
    assert math.isclose(b.total_usd, 10 * 37.45 + 48.80)
    assert b.avoidable_usd == 0


def test_errors_and_inefficiency_raise_cost():
    clean = cost.estimate_cost(600, 0, 1.0)
    bad = cost.estimate_cost(600, 3, 0.5)
    assert bad.total_usd > clean.total_usd
    assert bad.avoidable_usd > 0


def test_error_line_is_flagged_as_assumed_and_priced_from_source():
    b = cost.estimate_cost(60, 2, 1.0)
    line = b.lines[-1]
    assert line.assumed
    assert math.isclose(line.total_usd, round(2 * 0.01 * 13857.0, 2))


def test_total_equals_sum_of_lines_and_rate_toggle():
    b = cost.estimate_cost(300, 1, 0.8, setting="cross_study_mean", instruments="disposable")
    assert math.isclose(b.total_usd, round(sum(l.total_usd for l in b.lines), 2))
    assert b.lines[0].unit_usd == 46.04
