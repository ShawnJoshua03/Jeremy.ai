# Jeremy.ai

CalHacks 13. A trainee practices a focused surgical task on a Meta Quest 3, gets contextual voice coaching from an AI tutor, and can invite an instructor to observe the session and give guidance from a browser. At the end, the program measures the cost of the surgery.

## Stack

- **Meta Quest 3**: passthrough training experience
- **TigerData**: time-series storage for motion data
- **Google Gemini API**: interprets structured simulation events into explanations, a personalized debrief, and a structured exercise assessment
- **Cartesia**: text-to-speech for the AI coach (voice input via a separate speech-to-text service later)
- **Vultr**: hosting
- **Convex**: realtime sync between headset and browser

## Task list

Status as of 2026-10-24. "Code done" means implemented and tested off-device; nothing has run on a Quest yet.

| # | Task | Status |
|---|---|---|
| 1 | Passthrough Quest experience with a board anchored in front of the user | Code done (`PegTransferRig`, `PegBoard`); Quest build not run |
| 2 | Grab a virtual instrument with a controller (or hand pinch) | Code done (`InstrumentController`); not run on device |
| 3 | Complete a peg transfer | Code done; editor harness passes |
| 4 | Quantitative results: path efficiency, time, mistakes | Done (`backend/scoring.py`, HUD, dashboard). Smoothness is not computed |
| 5 | Replay the movement in a browser with errors highlighted | Done (dashboard replay, checked in a browser against a live backend) |
| 6 | Ask why performance suffered and get a targeted practice recommendation | Done (`POST /coach` with `question`, `next_exercise`). Live Gemini **not verified**; rule-based fallback works |
| 7 | Separate structured exercise assessment | Done (`GET /assessment`, deterministic) |
| 8 | Speak coaching aloud through Cartesia, with a text fallback | Code done (`/coach/audio`, `CoachAudioPlayer`); live Cartesia **not verified** |
| 9 | Remote mentor annotation appears in the headset | Code done: dashboard notes and highlight/pause/retry are queued and polled by the headset. Headset side not run on device |
| 10 | Cost of the surgery with an itemized breakdown | Done (`GET /cost`, HUD) |

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the full architecture plan.
- Cost model data and citations: [docs/COST_DATA.md](docs/COST_DATA.md)
- Unity / Quest reference repos: [docs/UNITY_REFERENCES.md](docs/UNITY_REFERENCES.md)
- Handoff / transition doc: [docs/HANDOFF.md](docs/HANDOFF.md)
