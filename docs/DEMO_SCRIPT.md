# Demo script

About three minutes. The scenario is a **laparoscopic cholecystectomy** (gallbladder removal), exercise id `lap_chole`, fifteen steps in [LAP_CHOLE.md](LAP_CHOLE.md). Say "illustrative" for every dollar figure and "practice score" for the rubric. Say "not verified" for the talking companion, the robot avatar, and the x402 mistake-cost report. Do not say the score predicts surgical skill. Do not say a sponsor integration works unless that row in [SPONSOR_EVIDENCE.md](SPONSOR_EVIDENCE.md) is verified.

Two paths you can offer, plus one designed path you do not perform. Use path A only for beats you can show on the machine in front of the judge. Path B is the Quest story. Path C is the companion and the paid report. As of this docs pass (2026-10-24), paths B and C are **not verified**. This pass did not re-run path A.

## Path A — laptop, routes that already exist

Setup, before the pitch:

```powershell
cd backend
.\.venv\Scripts\python -m uvicorn main:app --reload
```

Open `http://127.0.0.1:8000/docs`. Leave `GEMINI_API_KEY` unset so coaching stays on the text fallback. Leave `CARTESIA_API_KEY` unset unless you are about to play a WAV you just received. Do not invent a spoken reply.

1. **What it is (20 seconds).** "A trainee practices a laparoscopic cholecystectomy, gallbladder removal, fifteen steps from access through dividing the cystic duct and artery. The headset is meant to score the drill locally. This laptop is the backend a headset would upload to. The Quest build is not in this demo."
2. **Session (40 seconds).** Replay a synthetic attempt (no headset):

   ```powershell
   .\.venv\Scripts\python -m tools.fake_quest --exercise lap_chole
   ```

   Point the server at the uvicorn you started (`--base-url` if it is not the default). In `/docs` the same flow is `POST /api/v1/sessions` with `{"trainee_id":"judge","exercise":"lap_chole"}`, then `POST /sessions/{id}/start`, then lap-chole events (`step_completed`, `port_placed`, `insufflation`, `critical_view`, `clip_applied`, `structure_cut`, `ductotomy`, `cholangiogram`, `injury`). Read the response. A peg-transfer `drop` does not belong on this exercise.
3. **Score (40 seconds).** `POST /sessions/{id}/complete`. Read `successful_transfers` (steps of 15), `drops` (major errors), `invalid_releases` (minor errors), `path_efficiency`, `practice_score`, and `details`. Say the score is a fixed formula in `backend/scoring.py`. Any major error caps that score at 40. The formula is a practice rubric.
4. **Case cost (40 seconds).** `GET /sessions/{id}/cost`. Walk the lines: OR time, time lost to an inefficient path, laparoscopic instruments (Apelgren 1994), Hem-o-lok clips and trocars (assumed $150, `assumed: true`), expected cost of major errors (assumed risk 0.01). If the attempt clipped or cut the common bile duct, there is a bile-duct-injury line at the general-surgery complication figure ($19,626), and applying that figure here is an assumption. Say the rates that have sources are in [COST_DATA.md](COST_DATA.md). The link from a simulator error to a real adverse event is an assumption.
5. **Coach fallback (30 seconds).** `POST /sessions/{id}/coach`. Read `summary` and `tips`. Say `source` is `fallback` when no Gemini key is configured. A live Gemini call is a separate, already recorded PC check in [STATUS.md](STATUS.md). It is not part of this demo unless you configure a key and the response says `source: gemini`.

A previous local replay is written up in [API.md](API.md) (2026-10-24, fallback coach, port 8771): 15/15 steps, 0 major, 1 minor, practice score 82.1, illustrative cost $285.16. The unsafe variant in that note scored 40.0 and $20,188.30 with the assumed bile-duct line. Quote those only as that recorded run. Read today's response if you run the command again.

Stop. Offer `/docs` if they want a rejected body (a position past 10 meters returns 422).

## Path B — headset story, not verified

Do not perform this path until a Quest build has been run and [SPONSOR_EVIDENCE.md](SPONSOR_EVIDENCE.md) is updated.

1. Passthrough operating room. Steps 1–6 are on the abdomen. From the umbilical port onward the laparoscope view is the main view.
2. The trainee works the fifteen steps. The HUD shows time, progress, and errors without waiting on the network.
3. Finish. The HUD shows the same metrics and the itemized case cost from `/cost`.
4. Debrief text appears. `/coach/audio` plays only if Cartesia returned bytes. Otherwise the text stays up. Headset playback of that route is **not verified**.
5. A laptop dashboard shows the same session. An instructor highlight or pause arrives on the headset. This beat needs Convex and the dashboard with a real session, which are **not verified**.

## Path C — companion and mistake-cost report, not verified

Do not call these routes in front of a judge until they exist and a run is written into [SPONSOR_EVIDENCE.md](SPONSOR_EVIDENCE.md). They were not in this worktree when this script was written.

1. During the procedure a robot head near the field polls `POST /api/v1/sessions/{sid}/narrator/line` about every 6–10 seconds. The backend computes how far along the current step should be versus how far along it is, and a severity (`on_track`, `slow`, `off_track`, `critical`). Gemini returns one sentence of at most 25 words. Cartesia speaks it from `POST .../narrator/line/audio`.
2. If `repeat` is true, the avatar stays quiet. If audio returns 503, the sentence stays on screen as a subtitle. Eye color follows severity. The headset never holds a Cartesia or Gemini key.
3. After the attempt, `GET /api/v1/sessions/{sid}/mistake-cost/summary` is free and shows the total. `GET .../mistake-cost/report` returns 402 until a Base Sepolia USDC payment (scheme `exact`, about $0.01, payee from `X402_PAY_TO`) is verified. The paid body is an itemized per-mistake breakdown plus a short explanation of numbers the backend already computed.
4. Say the payment is testnet. Say Apify price refresh and facilitator settlement are **not verified**. The clip and trocar price on the case-cost endpoint is still an assumed $150.

## Lines to avoid

- "This is clinically validated."
- "Gemini gave this score" or "Gemini set this dollar amount."
- "The avatar spoke" or "we settled an x402 payment" or "Apify refreshed the prices," unless you just ran that call and can show the response.
- "We won / we qualify for [sponsor]."
- Any dollar figure that [COST_DATA.md](COST_DATA.md) lists as unsupported, stated as a hospital bill.
