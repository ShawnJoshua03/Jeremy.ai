# Demo script

About three minutes. Say "illustrative" for every dollar figure and "practice score" for the rubric. Do not say the score predicts surgical skill or that a sponsor integration works unless that row in [SPONSOR_EVIDENCE.md](SPONSOR_EVIDENCE.md) is verified.

Two paths. Use path A only for beats you can actually show on the machine in front of the judge. Path B is the Quest story. As of 2026-10-24, path B is **not verified**.

## Path A — what the tests actually exercise

Setup, before the pitch:

```powershell
cd backend
.\.venv\Scripts\python -m uvicorn main:app --reload
```

Open `http://127.0.0.1:8000/docs`. Leave `GEMINI_API_KEY` unset so coaching stays on the text fallback. Do not invent a spoken reply.

1. **What it is (20 seconds).** "A trainee practices peg transfer. The headset is meant to score the drill locally. This laptop is the backend a headset would upload to. The Quest build is not in this demo."
2. **Session (40 seconds).** In `/docs`: `POST /api/v1/sessions` with `{"trainee_id":"judge"}`, then `POST /sessions/{id}/start`. Post one `transfer_success` and one `drop` to `/events`, and two tip samples to `/samples`. Post the same batch again and point at `duplicates`.
3. **Score (40 seconds).** `POST /sessions/{id}/complete` with an elapsed time under 300 seconds. Read `successful_transfers`, `drops`, `path_efficiency`, and `practice_score`. Say the score is a fixed formula in `backend/scoring.py`, not a model judgment.
4. **Cost (40 seconds).** `GET /sessions/{id}/cost`. Walk the four lines: OR time, time lost to an inefficient path, instruments, expected cost of errors. The error line is flagged assumed (`risk` 0.01). Say the rates are published figures from `docs/COST_DATA.md`, and the link from a dropped peg to a real adverse event is not.
5. **Coach fallback (30 seconds).** `POST /sessions/{id}/coach`. Read `summary` and `tips`. Say `source` is `fallback` because no Gemini key is configured, and that a live Gemini call was not part of this demo. If a tip cites an event, it is one of the uploaded event IDs.

Stop. Offer `/docs` if they want to retry a bad coordinate (a position past 10 meters returns 422).

## Path B — headset story, not verified

Do not perform this path until a Quest build has been run and [SPONSOR_EVIDENCE.md](SPONSOR_EVIDENCE.md) is updated.

1. Passthrough, board about 0.5 m in front of the trainee, controller grasps a peg.
2. Move pegs from the six source posts to the matching targets. The HUD shows time, progress, and errors without waiting on the network.
3. Finish. The HUD shows the same metrics and the itemized cost from `/cost`.
4. Coaching text appears. Audio plays only if Cartesia returned bytes; otherwise the text stays up.
5. A laptop dashboard shows the same session. An instructor highlight or pause arrives on the headset. This beat needs Convex and the dashboard, which are **not verified**.

## Lines to avoid

- "This is clinically validated."
- "Gemini gave this score."
- "We won / we qualify for [sponsor]."
- Any dollar figure that [COST_DATA.md](COST_DATA.md) lists as unsupported.
