# Surgical supply price check

This Apify Actor fetches explicitly supplied HTTPS product pages, checks each site's `robots.txt`, and extracts a plausible USD price for one of the supported surgical supplies. Successful results are dataset rows compatible with `backend/pricing.py`'s `_validated()` contract. It never fills a missing price with a default or estimate.

## Input

By default, the actor checks the two cited URLs currently listed in `backend/data/supply_prices.json`:

```json
{
  "items": [
    {
      "item": "hem_o_lok_cartridge",
      "source_url": "https://www.synergysurgical.com/product/0-in-date/50-teleflex/36-ligating-clips/46207331-weck-hem-o-lok-polymer-ligating-clips-large-6-clipscartridge-544240/"
    },
    {
      "item": "trocar",
      "source_url": "https://www.ciamedical.com/5mm-trocars"
    }
  ],
  "dryRun": false
}
```

An item id must be `hem_o_lok_cartridge`, `trocar`, or `suture_set`. Each item object needs an HTTPS `source_url`; `css_selector` and `regex` can optionally identify a displayed price. The backend currently supplies its item ids as strings; the actor resolves the two IDs with cited default URLs, and warns for `suture_set` because the cited snapshot has no product URL for it. Extraction tries selector/regex overrides first, then JSON-LD `offers.price`, then common price metadata. Use `unitsPerPack` only when the listed amount is explicitly a multi-unit package price; the actor divides by that count. Without it, mentions such as “box of 12” cause the item to be skipped with a warning rather than treated as a unit price.

`dryRun: true` performs the page checks and extraction without pushing dataset rows; successful candidate rows and all warnings are shown in the `OUTPUT` key-value record. Normal runs push successful rows to the default dataset and write warnings to `OUTPUT`.

## Output

Each dataset row has exactly these keys:

```json
{
  "item": "trocar",
  "usd": 107.5,
  "source_url": "https://www.ciamedical.com/5mm-trocars",
  "retrieved_at": "2026-10-25T09:00:00Z"
}
```

`retrieved_at` is the UTC time the page was fetched. A missing, malformed, implausible, or disallowed page produces no dataset row; the `OUTPUT` key-value record contains an item-specific warning. Prices are illustrative, not billing data.

## Limits and responsible use

- Only HTTPS URLs without embedded credentials are accepted. Redirects are not followed.
- The actor checks `robots.txt` before requesting a page; if the robots check fails or disallows access, it skips the page.
- It makes at most one product-page request for each distinct URL per run, uses a polite User-Agent, and times out requests after 15 seconds.
- Prices outside $1–$5,000 USD are rejected. This is a plausibility filter, not validation of a seller's current offer.
- Product pages can change, be unavailable, or render prices with JavaScript; such pages may yield warnings and no row.
- The user must respect each site's terms of service and `robots.txt`, and obtain any permissions required before running this actor.
