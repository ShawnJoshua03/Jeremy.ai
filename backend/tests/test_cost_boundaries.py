import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(__file__)))

import cost


def test_zero_elapsed_s():
    b = cost.estimate_cost(elapsed_s=0, errors=0, path_efficiency=1.0)
    assert math.isclose(b.total_usd, round(48.80, 2))
    assert b.avoidable_usd == 0
    assert all(ineff.total_usd == 0 for ineff in b.lines[:2])


def test_zero_elapsed_with_errors_still_prices_errors():
    b = cost.estimate_cost(0, 2, 1.0)
    err = b.lines[-1]
    assert math.isclose(err.total_usd, round(2 * 0.01 * 13857.0, 2))
    assert math.isclose(b.avoidable_usd, err.total_usd)


def test_negative_elapsed_is_clamped_to_zero_minutes():
    b = cost.estimate_cost(-600, 0, 1.0)
    assert b.lines[0].quantity == 0 and b.lines[0].total_usd == 0
    assert b.lines[1].total_usd == 0
    assert math.isclose(b.total_usd, round(48.80, 2))


def test_many_errors_price_linearly():
    one = cost.estimate_cost(60, 1, 1.0)
    many = cost.estimate_cost(60, 1000, 1.0)
    unit = one.lines[-1].unit_usd
    assert math.isclose(many.lines[-1].total_usd, round(1000 * unit, 2))
    assert many.total_usd == round(sum(l.total_usd for l in many.lines), 2)


def test_risk_per_error_zero_removes_expected_error_cost():
    b = cost.estimate_cost(600, 5, 0.5, risk_per_error=0.0)
    err = b.lines[-1]
    assert err.assumed and err.total_usd == 0
    assert math.isclose(b.avoidable_usd, b.lines[1].total_usd)
    assert b.counterfactual_clean_usd < b.total_usd


def test_risk_per_error_one_prices_every_error_as_full_event():
    b = cost.estimate_cost(600, 3, 1.0, risk_per_error=1.0)
    err = b.lines[-1]
    assert math.isclose(err.unit_usd, 13857.0)
    assert math.isclose(err.total_usd, 3 * 13857.0)
    assert math.isclose(b.avoidable_usd, round(3 * 13857.0, 2))


def test_risk_per_error_extremes_keep_total_additive():
    zero = cost.estimate_cost(120, 4, 0.0, risk_per_error=0.0)
    one = cost.estimate_cost(120, 4, 0.0, risk_per_error=1.0)
    assert math.isclose(zero.total_usd, zero.counterfactual_clean_usd)
    assert one.total_usd == round(sum(l.total_usd for l in one.lines), 2)
    assert one.avoidable_usd == round(one.total_usd - one.counterfactual_clean_usd, 2)
