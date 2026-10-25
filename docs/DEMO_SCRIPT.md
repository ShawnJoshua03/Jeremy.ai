# Demo script

Two minutes. The operation is a **laparoscopic cholecystectomy** (gallbladder removal). The drill is the Critical View of Safety trainer: name the cystic duct, then judge two prepared views. Session exercise id: `lap_chole_cvs`. Contract: [CONTRACT_CVS.md](CONTRACT_CVS.md).

Say "practice score" for the rubric and "illustrative" for every dollar. Do not say the score predicts surgical skill.

This script was not performed. Unity does not upload a session, and the backend does not accept `lap_chole_cvs` or serve `GET /sessions/{id}/report`. Speech, the critical warning, the holographic report, replay, the dashboard CVS report, and the x402 charge are **not verified**. Show a beat only when the response is on screen. Otherwise say "not verified" and move on.

Setup, if you are about to try the local scene. Still **not verified**:

```powershell
cd backend
.\.venv\Scripts\python -m uvicorn main:app --reload
```

Open `quest` in Unity, open `Assets/GallbladderTrainer/Scenes/GallbladderTraining.unity`, press Play. Steps that need the mouse are in [TESTING_WITHOUT_HEADSET.md](TESTING_WITHOUT_HEADSET.md).

## The two minutes

1. **What it is (15 seconds).** "The trainee practices a laparoscopic cholecystectomy, gallbladder removal. The safety check is the Critical View of Safety: identify the cystic duct, then decide whether two prepared views actually demonstrate the criteria. A wrong 'demonstrated' on an incomplete view is how a bile duct gets cut."
2. **Cystic duct (25 seconds).** The guide talks through exercise 1. Click the narrow tube off the gallbladder neck and submit. The guide names the duct from the backend line. The headset holds no Gemini or Cartesia key. If audio is 503, the sentence stays on screen.
3. **View A, on purpose (30 seconds).** Prepared view A does not demonstrate the criteria. The lower third is still on the liver. Call it **Criteria demonstrated**. The guide returns `severity: critical` and says to stop. Requesting the instructor is the good action and is not penalised.
4. **View B (15 seconds).** The second view shows the three criteria. Call it demonstrated. The guide stays off the critical path.
5. **Close (35 seconds).** Complete the session. The headset shows the holographic report: practice score, the three exercises, and a short replay of the hands and the head. The instructor dashboard shows the same report. The free total is on screen. The itemized mistake cost, including the bile-duct line for the view A call, waits on an x402 testnet payment. Say the dollars are illustrative.

Stop. If they want a route, open `http://127.0.0.1:8000/docs` and read the body you just received.

## What is on this machine today

The GallbladderTrainer scene can show the three exercises and local authored feedback. The feedback header in that scene says the text is local, not live AI. There is no robot in the scene. The old avatar script is only in git at `868bafcf`.

The dashboard **Sample preview** control loads synthetic peg-transfer rows. It is labeled Sample preview. It is not this report.

The fifteen-step `lap_chole` replay (`tools/fake_quest.py --exercise lap_chole`) is a different exercise. Do not present it as the CVS trainer.

## Lines to avoid

- "This is clinically validated."
- "Gemini gave this score" or "Gemini set this dollar amount."
- "The guide spoke", "the headset showed the report", "we settled an x402 payment", or "Apify refreshed the prices", unless that response is on screen.
- "We won / we qualify for [sponsor]."
- Any dollar figure stated as a hospital bill. Sources and assumptions are in [COST_DATA.md](COST_DATA.md).
