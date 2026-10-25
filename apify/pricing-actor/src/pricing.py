"""Offline HTML price extraction for the Surgical supply price check actor."""
from __future__ import annotations

import json
import math
import re
from html.parser import HTMLParser
from typing import Any

try:
    from bs4 import BeautifulSoup
except ImportError:
    BeautifulSoup = None

ITEMS = {"hem_o_lok_cartridge", "trocar", "suture_set"}
MIN_USD = 1.0
MAX_USD = 5000.0

_PRICE_NUMBER = re.compile(
    r"(?<![\w.])(?:-\s*)?(?:USD\s*)?\$?\s*((?:\d{1,3}(?:,\d{3})+|\d+)(?:\.\d{1,2})?)(?![\w.])",
    re.IGNORECASE,
)
_PACK_COUNTS = (
    re.compile(r"\b(?:pack|box|case|carton|package|pkg)\s*(?:of\s*)?(\d+)\b", re.I),
    re.compile(r"\b(\d+)\s*[- ]?(?:pack|count|ct)\b", re.I),
)
_PRICE_META_KEYS = {
    "product:price:amount",
    "og:price:amount",
    "price",
    "product:price",
    "twitter:data1",
}


class _Document(HTMLParser):
    def __init__(self) -> None:
        super().__init__(convert_charrefs=True)
        self.source = ""
        self.text_parts: list[str] = []
        self.scripts: list[tuple[str, str]] = []
        self.metas: list[dict[str, str]] = []
        self.elements: list[tuple[str, dict[str, str], str]] = []
        self._script_type = ""
        self._script_data: list[str] = []
        self._element_attrs: list[tuple[str, dict[str, str], list[str]]] = []

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        attributes = {key.lower(): value or "" for key, value in attrs}
        if tag.lower() == "meta":
            self.metas.append(attributes)
        if tag.lower() == "script":
            self._script_type = attributes.get("type", "").lower()
            self._script_data = []
        if tag.lower() not in {"meta", "link", "img", "br", "hr", "input", "source", "area", "base", "embed", "param", "track", "wbr"}:
            self._element_attrs.append((tag.lower(), attributes, []))

    def handle_startendtag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        self.handle_starttag(tag, attrs)
        self.handle_endtag(tag)

    def handle_endtag(self, tag: str) -> None:
        tag = tag.lower()
        if tag == "script":
            self.scripts.append((self._script_type, "".join(self._script_data)))
            self._script_type = ""
            self._script_data = []
        for index in range(len(self._element_attrs) - 1, -1, -1):
            element_tag, attributes, parts = self._element_attrs[index]
            if element_tag == tag:
                text = " ".join(" ".join(parts).split())
                self.elements.append((element_tag, attributes, text))
                del self._element_attrs[index:]
                break

    def handle_data(self, data: str) -> None:
        stripped = data.strip()
        if stripped:
            self.text_parts.append(stripped)
            for _, _, parts in self._element_attrs:
                parts.append(stripped)
        if self._script_type:
            self._script_data.append(data)


def _parse_document(html: str) -> _Document:
    document = _Document()
    document.source = html
    document.feed(html)
    document.close()
    return document


def _number(value: object) -> float | None:
    if isinstance(value, bool):
        return None
    if isinstance(value, (int, float)):
        amount = float(value)
    elif isinstance(value, str):
        match = _PRICE_NUMBER.search(value.strip())
        if not match or match.group(0).lstrip().startswith("-"):
            return None
        try:
            amount = float(match.group(1).replace(",", ""))
        except ValueError:
            return None
    else:
        return None
    if not math.isfinite(amount) or not MIN_USD <= amount <= MAX_USD:
        return None
    return amount


def _css_matches(document: _Document, selector: str) -> list[str]:
    """Use full CSS selection when BeautifulSoup is installed; retain an offline fallback."""
    if not isinstance(selector, str):
        return []
    if BeautifulSoup is not None:
        soup = BeautifulSoup(document.source, "html.parser")
        return [
            element.get("content") or element.get("value") or element.get_text(" ", strip=True)
            for element in soup.select(selector)
        ]
    match = re.fullmatch(r"(?:(?P<tag>[A-Za-z][\w-]*))?(?P<qualifier>[.#][\w-]+)?", selector.strip())
    if not match:
        return []
    tag = match.group("tag").lower() if match.group("tag") else None
    qualifier = match.group("qualifier")
    found = []
    for element_tag, attributes, text in document.elements:
        if tag and element_tag != tag:
            continue
        if qualifier and qualifier.startswith("#") and attributes.get("id") != qualifier[1:]:
            continue
        if qualifier and qualifier.startswith(".") and qualifier[1:] not in attributes.get("class", "").split():
            continue
        found.append(attributes.get("content") or attributes.get("value") or text)
    return found


def _walk_offers(value: Any, in_offer: bool = False):
    if isinstance(value, dict):
        lowered = {str(key).lower(): item for key, item in value.items()}
        if in_offer and "price" in lowered:
            currency = lowered.get("pricecurrency")
            if currency is None or str(currency).upper() == "USD":
                yield lowered["price"]
        for key, child in value.items():
            yield from _walk_offers(child, in_offer or str(key).lower() == "offers")
    elif isinstance(value, list):
        for child in value:
            yield from _walk_offers(child, in_offer)


def _extract_price(document: _Document, selector: str | None, regex: str | None) -> float | None:
    if selector:
        for candidate in _css_matches(document, selector):
            amount = _number(candidate)
            if amount is not None:
                return amount
    text = " ".join(document.text_parts)
    if regex and isinstance(regex, str):
        try:
            match = re.search(regex, text, re.IGNORECASE)
        except (re.error, TypeError):
            match = None
        if match:
            candidate = match.group(1) if match.lastindex else match.group(0)
            amount = _number(candidate)
            if amount is not None:
                return amount
    for content_type, script in document.scripts:
        if "ld+json" not in content_type:
            continue
        try:
            data = json.loads(script)
        except (json.JSONDecodeError, TypeError):
            continue
        for candidate in _walk_offers(data):
            amount = _number(candidate)
            if amount is not None:
                return amount
    for meta in document.metas:
        key = (meta.get("property") or meta.get("name") or meta.get("itemprop") or "").lower()
        if key in _PRICE_META_KEYS:
            amount = _number(meta.get("content", ""))
            if amount is not None:
                return amount
    return None


def _pack_count(text: str) -> int | None:
    for pattern in _PACK_COUNTS:
        match = pattern.search(text)
        if match:
            count = int(match.group(1))
            if count > 1:
                return count
    return None


def extract_unit_price(
    html: str,
    *,
    selector: str | None = None,
    regex: str | None = None,
    units_per_pack: int | None = None,
) -> tuple[float | None, str | None]:
    """Return (unit USD price, warning); no unsupported price is fabricated."""
    document = _parse_document(html)
    text = " ".join(document.text_parts)
    listed_price = _extract_price(document, selector, regex)
    if listed_price is None:
        return None, "No plausible USD price (between $1 and $5,000) was found."
    count = _pack_count(text)
    if units_per_pack is not None:
        if isinstance(units_per_pack, bool) or not isinstance(units_per_pack, int) or units_per_pack < 1:
            return None, "unitsPerPack must be a positive integer."
        listed_price /= units_per_pack
    elif count is not None:
        return None, (
            f"Page mentions a pack of {count}; refusing to treat its listed price as a unit price. "
            "Set unitsPerPack explicitly to divide the pack price."
        )
    if not math.isfinite(listed_price) or not MIN_USD <= listed_price <= MAX_USD:
        return None, "The resulting unit price is outside the plausible $1-$5,000 USD range."
    return listed_price, None


__all__ = ["ITEMS", "extract_unit_price"]
