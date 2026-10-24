"""Cited USD supply prices, with an explicit, offline-safe Apify refresh."""
from __future__ import annotations

import json
import math
import os
import tempfile
from datetime import datetime, timezone
from pathlib import Path
from urllib.parse import quote, urlparse

import httpx

SNAPSHOT_PATH = Path(__file__).with_name("data") / "supply_prices.json"
# Units are part of the actor contract: one six-clip cartridge, one trocar,
# or one suture/needle-driver set. Never interpret a box price as a unit price.
ITEMS = frozenset({"hem_o_lok_cartridge", "trocar", "suture_set"})


def _validated(rows: object) -> dict[str, dict]:
    if not isinstance(rows, list):
        raise ValueError("pricing dataset must be a list")
    prices = {}
    for row in rows:
        if not isinstance(row, dict) or row.get("item") not in ITEMS:
            raise ValueError("unknown supply item")
        amount = row.get("usd")
        if isinstance(amount, bool) or not isinstance(amount, (int, float)) or not math.isfinite(amount) or amount <= 0:
            raise ValueError("price must be a positive finite USD number")
        url = urlparse(row["source_url"])
        if url.scheme != "https" or not url.hostname or url.username or url.password:
            raise ValueError("price needs an HTTPS source URL")
        retrieved = datetime.fromisoformat(row["retrieved_at"].replace("Z", "+00:00"))
        if retrieved.tzinfo is None or retrieved > datetime.now(timezone.utc):
            raise ValueError("retrieved_at must be a past UTC/offset timestamp")
        if row["item"] in prices:
            raise ValueError("duplicate supply item")
        prices[row["item"]] = {key: row[key] for key in ("item", "usd", "source_url", "retrieved_at")}
    return prices


def load_prices(path: Path | None = None) -> dict[str, dict]:
    """An absent/corrupt snapshot leaves callers on visibly assumed prices."""
    try:
        return _validated(json.loads((path or SNAPSHOT_PATH).read_text(encoding="utf-8")))
    except (OSError, ValueError, KeyError, TypeError, AttributeError):
        return {}


def refresh_with_apify(client: httpx.Client | None = None, path: Path | None = None) -> dict[str, dict]:
    """Run an explicitly configured actor; atomically merge validated sourced rows.

    No request-time scraping. Missing config, outages, empty/malformed datasets,
    and write failures return the previous snapshot without altering it.
    """
    path = path or SNAPSHOT_PATH
    previous = load_prices(path)
    token = os.getenv("APIFY_TOKEN", "").strip()
    actor = os.getenv("APIFY_PRICING_ACTOR", "").strip()
    if not token or not actor:
        return previous
    if client is None:
        with httpx.Client(timeout=45) as owned:
            return refresh_with_apify(owned, path)
    temporary = None
    try:
        response = client.post(
            f"https://api.apify.com/v2/actors/{quote(actor.replace('/', '~'), safe='~')}/run-sync-get-dataset-items",
            params={"timeout": 30, "format": "json", "clean": "true"},
            headers={"Authorization": f"Bearer {token}"},
            json={"items": sorted(ITEMS), "currency": "USD"}, timeout=45,
        )
        response.raise_for_status()
        updated = _validated(response.json())
        if not updated:
            return previous
        merged = {**previous, **updated}
        path.parent.mkdir(parents=True, exist_ok=True)
        with tempfile.NamedTemporaryFile(mode="w", encoding="utf-8", dir=path.parent, delete=False) as out:
            temporary = Path(out.name)
            json.dump(list(merged.values()), out, indent=2, allow_nan=False)
            out.write("\n")
        temporary.replace(path)
        return merged
    except (httpx.HTTPError, OSError, ValueError, KeyError, TypeError, AttributeError):
        return previous
    finally:
        if temporary is not None:
            try:
                temporary.unlink(missing_ok=True)
            except OSError:
                pass  # Cleanup must not break the promised offline fallback.
