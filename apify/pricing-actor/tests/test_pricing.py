import importlib.util
import sys
from datetime import datetime, timezone
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[3]
ACTOR_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ACTOR_ROOT))
sys.path.insert(0, str(ROOT / "backend"))

from src.pricing import extract_unit_price
from src.main import DEFAULT_URLS, _normalize_entry


def _fixture(name: str) -> str:
    return (ACTOR_ROOT / "tests" / "fixtures" / name).read_text(encoding="utf-8")


def _backend_pricing_module():
    module_path = ROOT / "backend" / "pricing.py"
    spec = importlib.util.spec_from_file_location("backend_pricing_for_actor_test", module_path)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def test_extracts_json_ld_offer_price():
    price, warning = extract_unit_price(_fixture("json_ld.html"))
    assert price == 107.5
    assert warning is None


def test_extracts_css_selector_price():
    price, warning = extract_unit_price(_fixture("selector.html"), selector=".product-price")
    assert price == 49.0
    assert warning is None


def test_unparseable_page_produces_no_price():
    price, warning = extract_unit_price(_fixture("unparseable.html"))
    assert price is None
    assert warning and "No plausible USD price" in warning


def test_pack_price_is_blocked_unless_unit_count_is_explicit():
    page = _fixture("box_price.html")
    price, warning = extract_unit_price(page, selector=".price")
    assert price is None
    assert warning and "pack of 12" in warning

    unit_price, warning = extract_unit_price(page, selector=".price", units_per_pack=12)
    assert unit_price == 10.0
    assert warning is None


def test_backend_item_id_input_resolves_cited_urls():
    cartridge, _, warning = _normalize_entry("hem_o_lok_cartridge")
    assert warning is None
    assert cartridge == {
        "item": "hem_o_lok_cartridge",
        "source_url": DEFAULT_URLS["hem_o_lok_cartridge"],
    }
    suture_set, _, warning = _normalize_entry("suture_set")
    assert suture_set is None
    assert warning and "No cited default product URL" in warning


def test_output_row_matches_backend_validation_contract():
    pricing = _backend_pricing_module()
    usd, warning = extract_unit_price(_fixture("selector.html"), selector=".product-price")
    assert warning is None
    row = {
        "item": "hem_o_lok_cartridge",
        "usd": usd,
        "source_url": "https://www.synergysurgical.com/product/hem-o-lok",
        "retrieved_at": datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z"),
    }
    validated = pricing._validated([row])
    assert validated == {row["item"]: row}
    assert set(validated[row["item"]]) == {"item", "usd", "source_url", "retrieved_at"}


@pytest.mark.parametrize("amount", [0, -1, float("nan"), float("inf"), 5001])
def test_price_outside_plausible_range_is_not_emitted(amount):
    html = f"<html><body><span class='price'>${amount}</span></body></html>"
    price, warning = extract_unit_price(html, selector=".price")
    assert price is None
    assert warning
