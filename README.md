# Jeremy.ai

CalHacks 13. A trainee practices a focused surgical task on a Meta Quest 3, gets contextual voice coaching from an AI tutor, and can invite an instructor to observe the session and give guidance from a browser. At the end, the program measures the cost of the surgery.

## Stack

- **Meta Quest 3**: passthrough training experience
- **SQLite / Postgres**: session storage (Tiger Data was considered and dropped)
- **Google Gemini API**: interprets structured simulation events into explanations, a personalized debrief, and a structured exercise assessment
- **Cartesia**: text-to-speech for the AI coach (voice input via a separate speech-to-text service later)
- **Vultr**: hosting
- **Convex**: live read-only mirror for the instructor dashboard

## Quick start
```powershell
cd backend
python -m venv .venv; .\.venv\Scripts\pip install -r requirements.txt
.\.venv\Scripts\python -m pytest -q
.\.venv\Scripts\python -m uvicorn main:app --reload
```
Copy `.env.example` to `.env` for Gemini, Cartesia and Convex keys. Full instructions: [docs/RUNBOOK.md](docs/RUNBOOK.md).

## Task list

Status is by code and tests, not headset testing. Nothing has run on a Quest yet. See [docs/STATUS.md](docs/STATUS.md).

- [ ] 1. Launch a passthrough Quest experience with a virtual training board anchored in front of the user (code written, APK builds, not run on a headset)
- [ ] 2. Grab a virtual surgical instrument with a pinch of your hand (hand tracking; controllers also supported) (hand-tracking pinch grab written, not run on a headset)
- [ ] 3. Complete a peg transfer or precision tracing exercise (code written, not run on a headset)
- [ ] 4. Show quantitative results: path efficiency, time, mistakes, smoothness (backend metrics and HUD results written; tested via API)
- [ ] 5. Replay the entire movement in a browser with errors highlighted (dashboard builds; replay endpoints tested; not run on real sessions)
- [ ] 6. Ask Gemini why performance suffered and get a targeted practice recommendation grounded in the recorded events (verified live from a PC (Gemini))
- [ ] 7. Use Gemini to generate a separate structured exercise assessment (coach returns structured tips; tested)
- [ ] 8. Speak the coaching feedback aloud in the headset through Cartesia, with a text fallback (Cartesia WAV verified from a PC; headset playback not wired)
- [ ] 9. Let a remote mentor review the session and send a coaching annotation that appears inside the headset (annotation and command endpoints tested; headset polling off by default)
- [ ] 10. Show the cost of the surgery and an itemized breakdown on screen (cost breakdown endpoint tested; HUD display written)

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the full architecture plan.
- Cost model data and citations: [docs/COST_DATA.md](docs/COST_DATA.md)
- Unity / Quest reference repos: [docs/UNITY_REFERENCES.md](docs/UNITY_REFERENCES.md)
- Handoff: [docs/HANDOFF.md](docs/HANDOFF.md)
- Status and open work: [docs/STATUS.md](docs/STATUS.md)
- All docs: [docs/README.md](docs/README.md)
