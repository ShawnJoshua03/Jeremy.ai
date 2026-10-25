from __future__ import annotations

import asyncio
from datetime import datetime, timezone
from urllib.parse import urlparse
from urllib.robotparser import RobotFileParser

import httpx

from src.pricing import ITEMS, extract_unit_price

USER_AGENT = "SurgicalSupplyPriceCheck/1.0 (+https://apify.com; polite research crawler)"
TIMEOUT_SECONDS = 15.0
DEFAULT_ITEMS = [
    {
        "item": "hem_o_lok_cartridge",
        "source_url": "https://www.synergysurgical.com/product/0-in-date/50-teleflex/36-ligating-clips/46207331-weck-hem-o-lok-polymer-ligating-clips-large-6-clipscartridge-544240/",
    },
    {"item": "trocar", "source_url": "https://www.ciamedical.com/5mm-trocars"},
]
DEFAULT_URLS = {entry["item"]: entry["source_url"] for entry in DEFAULT_ITEMS}


def _utc_now() -> str:
    return datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def _valid_source_url(value: object) -> bool:
    if not isinstance(value, str):
        return False
    parsed = urlparse(value)
    return parsed.scheme == "https" and bool(parsed.hostname) and not parsed.username and not parsed.password


def _normalize_entry(entry: object) -> tuple[dict | None, str, str | None]:
    if isinstance(entry, str):
        source_url = DEFAULT_URLS.get(entry)
        if source_url is None:
            return None, entry, "No cited default product URL; provide an item object with source_url."
        return {"item": entry, "source_url": source_url}, entry, None
    if isinstance(entry, dict):
        return entry, str(entry.get("item", "unknown")), None
    return None, "unknown", "Item entry must be an object or a supported item id string."


async def _robots_allowed(client: httpx.AsyncClient, url: str) -> tuple[bool, str | None]:
    parsed = urlparse(url)
    robots_url = f"{parsed.scheme}://{parsed.netloc}/robots.txt"
    try:
        response = await client.get(robots_url)
    except httpx.HTTPError as exc:
        return False, f"Could not check robots.txt ({exc}); skipping the page."
    if response.status_code == 404:
        return True, None
    if response.status_code < 200 or response.status_code >= 300:
        return False, f"robots.txt returned HTTP {response.status_code}; skipping the page."
    parser = RobotFileParser()
    parser.set_url(robots_url)
    parser.parse(response.text.splitlines())
    if not parser.can_fetch(USER_AGENT, url):
        return False, "robots.txt disallows this page; skipping it."
    return True, None


async def _run() -> None:
    from apify import Actor

    await Actor.init()
    try:
        actor_input = await Actor.get_input() or {}
        items = actor_input.get("items", DEFAULT_ITEMS)
        dry_run = bool(actor_input.get("dryRun", False))
        if not isinstance(items, list):
            raise ValueError("Input 'items' must be an array.")

        warnings: list[dict[str, str]] = []
        rows: list[dict[str, object]] = []
        seen_urls: dict[str, tuple[str | None, str | None]] = {}
        robots_cache: dict[str, tuple[bool, str | None]] = {}
        seen_items: set[str] = set()

        async with httpx.AsyncClient(
            headers={"User-Agent": USER_AGENT, "Accept": "text/html,application/xhtml+xml"},
            timeout=TIMEOUT_SECONDS,
            follow_redirects=False,
        ) as client:
            for entry in items:
                entry, input_item, input_warning = _normalize_entry(entry)
                if input_warning:
                    warnings.append({"item": input_item, "warning": input_warning})
                    continue
                item = entry.get("item")
                url = entry.get("source_url")
                if not isinstance(item, str) or item not in ITEMS:
                    warnings.append({"item": str(item), "warning": "Unknown item id; skipped."})
                    continue
                if item in seen_items:
                    warnings.append({"item": item, "warning": "Duplicate item id; skipped to preserve the dataset contract."})
                    continue
                seen_items.add(item)
                if not _valid_source_url(url):
                    warnings.append({"item": item, "warning": "source_url must be an HTTPS URL without credentials."})
                    continue

                if url not in seen_urls:
                    origin = f"{urlparse(url).scheme}://{urlparse(url).netloc}"
                    if origin not in robots_cache:
                        robots_cache[origin] = await _robots_allowed(client, url)
                    allowed, robots_warning = robots_cache[origin]
                    if not allowed:
                        seen_urls[url] = (None, robots_warning or "robots.txt denied this page.")
                    else:
                        try:
                            response = await client.get(url)
                            if response.status_code < 200 or response.status_code >= 300:
                                seen_urls[url] = (None, f"Product page returned HTTP {response.status_code}; skipped.")
                            elif urlparse(str(response.url)).scheme != "https":
                                seen_urls[url] = (None, "Product page did not resolve to HTTPS; skipped.")
                            else:
                                seen_urls[url] = (response.text, None)
                        except httpx.HTTPError as exc:
                            seen_urls[url] = (None, f"Could not fetch product page ({exc}); skipped.")

                page, fetch_warning = seen_urls[url]
                if fetch_warning:
                    warnings.append({"item": item, "warning": fetch_warning})
                    continue
                price, warning = extract_unit_price(
                    page or "",
                    selector=entry.get("css_selector"),
                    regex=entry.get("regex"),
                    units_per_pack=entry.get("unitsPerPack"),
                )
                if warning:
                    warnings.append({"item": item, "warning": warning})
                    continue
                rows.append(
                    {
                        "item": item,
                        "usd": price,
                        "source_url": url,
                        "retrieved_at": _utc_now(),
                    }
                )

        if not dry_run:
            for row in rows:
                await Actor.push_data(row)
        await Actor.set_value(
            "OUTPUT",
            {
                "dryRun": dry_run,
                "rows": rows if dry_run else [],
                "rowsPushed": 0 if dry_run else len(rows),
                "warnings": warnings,
            },
        )
    finally:
        await Actor.exit()


def main() -> None:
    asyncio.run(_run())


if __name__ == "__main__":
    main()
