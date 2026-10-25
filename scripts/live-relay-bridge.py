"""Bridge: replay the headset's relayed live telemetry into the local SpacetimeDB.

The headset posts SpacetimeDB reducer calls to the internet backend when it cannot reach this PC directly
(venue Wi-Fi often isolates devices). This script long-polls those calls and replays them, in order, into the
local database, so the live dashboard works on any network.

  python scripts/live-relay-bridge.py [--relay https://jeremy-api.onrender.com] [--local http://127.0.0.1:3000] [--db jeremy-ai-live]
"""
import argparse
import json
import time
import urllib.error
import urllib.request


def http(method, url, body=None, timeout=30):
    data = body.encode("utf-8") if isinstance(body, str) else None
    req = urllib.request.Request(url, data=data, method=method, headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=timeout) as r:
        return r.status, r.read().decode("utf-8", errors="replace")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--relay", default="https://jeremy-api.onrender.com")
    ap.add_argument("--local", default="http://127.0.0.1:3000")
    ap.add_argument("--db", default="jeremy-ai-live")
    a = ap.parse_args()
    after, sent, failed, last_report = 0, 0, 0, time.time()
    print(f"bridge: {a.relay} -> {a.local}/{a.db}", flush=True)
    while True:
        try:
            _, text = http("GET", f"{a.relay}/api/v1/live/poll?db={a.db}&after={after}&wait=20", timeout=30)
            got = json.loads(text)
        except (urllib.error.URLError, TimeoutError, ValueError, OSError) as e:
            print("bridge: relay unreachable:", e, flush=True)
            time.sleep(3)
            continue
        if got.get("seq", 0) < after:
            after = 0  # the relay restarted
        for item in got.get("items", []):
            after = max(after, item["seq"])
            try:
                http("POST", f"{a.local}/v1/database/{a.db}/call/{item['reducer']}", item["args"], timeout=10)
                sent += 1
            except (urllib.error.URLError, TimeoutError, OSError) as e:
                failed += 1
                if failed % 20 == 1:
                    print("bridge: local SpacetimeDB rejected/unreachable:", e, flush=True)
        if time.time() - last_report > 15:
            print(f"bridge: replayed {sent} calls ({failed} failed), at seq {after}", flush=True)
            last_report = time.time()


if __name__ == "__main__":
    main()
