"""Replay a synthetic peg-transfer session against a running API.

No headset required. Builds a 6-object transfer with two drops and left-hand
tip samples at 25 Hz, uploads them, then prints metrics, cost, and coach JSON.

    python tools/fake_quest.py --base-url http://127.0.0.1:8000

Live Gemini and Cartesia are not called by this script. Coach output is whatever
the server returns (the rule-based fallback when GEMINI_API_KEY is unset).
"""
from __future__ import annotations

import argparse
import json
import urllib.error
import urllib.request

HZ = 25
DT_MS = 1000 // HZ  # 40 ms
OBJECTS = 6
DROP_OBJECTS = (1, 4)  # zero-based indexes; two drops


def build_trace() -> tuple[list[dict], list[dict], float]:
    """Straight transfers in the board's local frame. Each object takes one second."""
    samples: list[dict] = []
    events: list[dict] = []
    t_ms = 0
    idx = 0
    for i in range(OBJECTS):
        source = [round(0.04 * i, 4), 0.0, 0.0]
        target = [round(0.04 * i, 4), 0.12, 0.0]
        for step in range(HZ):
            u = step / (HZ - 1)
            pos = [
                round(source[0] + (target[0] - source[0]) * u, 4),
                round(source[1] + (target[1] - source[1]) * u, 4),
                0.0,
            ]
            samples.append({
                "instrument": "left",
                "idx": idx,
                "t_ms": t_ms,
                "pos": pos,
                "valid": True,
                "interaction": "holding" if step else "idle",
            })
            if i in DROP_OBJECTS and step == HZ // 2:
                events.append({
                    "event_id": f"drop-{i + 1}",
                    "kind": "drop",
                    "t_ms": t_ms,
                    "instrument": "left",
                    "object_id": f"peg-{i + 1}",
                })
            idx += 1
            t_ms += DT_MS
        events.append({
            "event_id": f"xfer-{i + 1}",
            "kind": "transfer_success",
            "t_ms": t_ms - DT_MS,
            "instrument": "left",
            "object_id": f"peg-{i + 1}",
            "source": source,
            "target": target,
        })
    elapsed_s = (samples[-1]["t_ms"]) / 1000
    return samples, events, elapsed_s


class UrllibClient:
    """Minimal client matching TestClient.post/get for a running server."""

    def __init__(self, base_url: str):
        self.base = base_url.rstrip("/")

    def post(self, path: str, json: dict | None = None):
        return self._send("POST", path, json)

    def get(self, path: str):
        return self._send("GET", path, None)

    def _send(self, method: str, path: str, body: dict | None):
        data = None if body is None else json.dumps(body).encode()
        req = urllib.request.Request(
            self.base + path,
            data=data,
            method=method,
            headers={"Content-Type": "application/json", "Accept": "application/json"} if data else {"Accept": "application/json"},
        )
        try:
            with urllib.request.urlopen(req) as resp:
                raw = resp.read().decode()
                status = resp.status
        except urllib.error.HTTPError as exc:
            raw = exc.read().decode()
            status = exc.code
        payload = json.loads(raw) if raw else None

        class Response:
            status_code = status

            def json(self):
                return payload

        return Response()


def _ok(resp):
    if resp.status_code not in (200, 201):
        raise SystemExit(f"HTTP {resp.status_code}: {resp.json()}")
    return resp.json()


def replay(client, trainee_id: str = "fake-quest") -> dict:
    created = _ok(client.post("/api/v1/sessions", json={"trainee_id": trainee_id, "exercise": "peg_transfer"}))
    sid = created["id"]
    _ok(client.post(f"/api/v1/sessions/{sid}/start"))
    samples, events, elapsed_s = build_trace()
    uploaded_samples = _ok(client.post(f"/api/v1/sessions/{sid}/samples", json={"samples": samples}))
    uploaded_events = _ok(client.post(f"/api/v1/sessions/{sid}/events", json={"events": events}))
    metrics = _ok(client.post(f"/api/v1/sessions/{sid}/complete", json={"elapsed_s": elapsed_s}))
    cost = _ok(client.get(f"/api/v1/sessions/{sid}/cost"))
    coach = _ok(client.post(f"/api/v1/sessions/{sid}/coach"))
    return {
        "session_id": sid,
        "elapsed_s": elapsed_s,
        "sample_upload": uploaded_samples,
        "event_upload": uploaded_events,
        "metrics": metrics,
        "cost": cost,
        "coach": coach,
    }


def main() -> None:
    parser = argparse.ArgumentParser(description="Replay a synthetic 6-object peg transfer")
    parser.add_argument("--base-url", default="http://127.0.0.1:8000", help="Running API origin")
    parser.add_argument("--trainee-id", default="fake-quest")
    args = parser.parse_args()
    report = replay(UrllibClient(args.base_url), trainee_id=args.trainee_id)
    print(json.dumps(
        {"metrics": report["metrics"], "cost": report["cost"], "coach": report["coach"]},
        indent=2,
    ))


if __name__ == "__main__":
    main()
