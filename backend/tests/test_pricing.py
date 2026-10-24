import json

import httpx
import pytest

import cost
import pricing


def price(item="trocar", usd=20.0):
    return {"item": item, "usd": usd, "source_url": "https://example.com/product",
            "retrieved_at": "2026-01-01T00:00:00Z"}


@pytest.fixture
def snapshot(tmp_path, monkeypatch):
    path = tmp_path / "supplies.json"
    path.write_text(json.dumps([price()]), encoding="utf-8")
    monkeypatch.setattr(pricing, "SNAPSHOT_PATH", path)
    monkeypatch.setenv("APIFY_TOKEN", "test-token")
    monkeypatch.setenv("APIFY_PRICING_ACTOR", "test-owner/pricing")
    return path


def test_committed_snapshot_is_cited_and_units_are_explicit():
    prices = pricing.load_prices()
    assert prices["hem_o_lok_cartridge"]["usd"] == 49.0
    assert prices["trocar"]["usd"] == 107.5
    assert "suture_set" not in prices
    result = cost.estimate_cost(60, 0, 1, exercise="lap_chole")
    supplies = result.lines[3:5]
    assert [s.quantity for s in supplies] == [1, 5]
    assert [s.total_usd for s in supplies] == [49.0, 537.5]
    assert all(not s.assumed and s.quantity_assumed and s.source and s.price_retrieved_at for s in supplies)


def test_partial_snapshot_falls_back_only_for_missing_price(snapshot):
    result = cost.estimate_cost(60, 0, 1, exercise="lap_chole")
    assert result.lines[3].assumed and result.lines[3].total_usd == 50
    assert not result.lines[4].assumed and result.lines[4].total_usd == 100


def test_successful_apify_refresh_merges_and_persists(snapshot):
    def handle(request):
        assert request.url.path == "/v2/actors/test-owner~pricing/run-sync-get-dataset-items"
        assert request.headers["authorization"] == "Bearer test-token"
        assert "test-token" not in str(request.url)
        assert json.loads(request.content)["currency"] == "USD"
        return httpx.Response(200, json=[price("hem_o_lok_cartridge", 30)])
    with httpx.Client(transport=httpx.MockTransport(handle)) as client:
        refreshed = pricing.refresh_with_apify(client)
    assert set(refreshed) == {"trocar", "hem_o_lok_cartridge"}
    assert pricing.load_prices() == refreshed
    assert cost.estimate_cost(0, 0, 1, exercise="lap_chole").lines[3].total_usd == 30


@pytest.mark.parametrize("rows", [[], {}, [price(usd=-2)], [price(usd=True)],
    [{**price(), "source_url": ""}], [{**price(), "retrieved_at": "not-a-date"}],
    [{**price(), "retrieved_at": "2999-01-01T00:00:00Z"}],
    [price("unknown")], [price(), price()], [{"item": "trocar", "usd": 12}]])
def test_bad_apify_data_preserves_snapshot(snapshot, rows):
    original = snapshot.read_bytes()
    with httpx.Client(transport=httpx.MockTransport(lambda r: httpx.Response(200, json=rows))) as client:
        assert pricing.refresh_with_apify(client) == {"trocar": price()}
    assert snapshot.read_bytes() == original


def test_offline_and_unconfigured_refresh_preserve_snapshot(snapshot, monkeypatch):
    def offline(request):
        raise httpx.ConnectError("offline")
    with httpx.Client(transport=httpx.MockTransport(offline)) as client:
        assert pricing.refresh_with_apify(client) == {"trocar": price()}
    monkeypatch.delenv("APIFY_TOKEN")
    assert pricing.refresh_with_apify() == {"trocar": price()}


def test_corrupt_snapshot_uses_original_assumption(snapshot):
    snapshot.write_text("broken", encoding="utf-8")
    result = cost.estimate_cost(60, 0, 1, exercise="lap_chole")
    assert result.lines[3].assumed and result.lines[3].total_usd == 150


def test_sourced_suture_price_replaces_assumption(snapshot):
    snapshot.write_text(json.dumps([price("suture_set", 42)]), encoding="utf-8")
    line = cost.estimate_cost(60, 0, 1, exercise="wound_closure").lines[2]
    assert line.unit_usd == 42 and not line.assumed
