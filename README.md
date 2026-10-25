# Jeremy.ai

CalHacks 13. A trainee practices a focused surgical task on a Meta Quest 3, gets contextual voice coaching from an AI tutor, and can invite an instructor to observe the session and give guidance from a browser. At the end, the program measures the cost of the surgery.

## Stack

- **Meta Quest 3**: passthrough training experience
- **SQLite / Postgres**: session storage (Tiger Data was considered and dropped)
- **Google Gemini API**: interprets structured simulation events into explanations, a personalized debrief, and a structured exercise assessment
- **Cartesia**: text-to-speech for the AI coach (voice input via a separate speech-to-text service later)
- **Render**: hosting (free tier), configured by [render.yaml](render.yaml)
- **Convex**: live read-only mirror for the instructor dashboard

## Live deployment

- **Instructor dashboard:** https://jeremywantstobeasurgeon.us/ (static site on Render)
- **API:** https://api.jeremywantstobeasurgeon.us (health: `/health`, docs: `/docs`), the same service as `https://jeremy-api.onrender.com`
- Deployed from `main` on every push. Render's free tier sleeps after about 15 minutes idle, so the first request can take 50 seconds, and the SQLite data resets on restart.

## Quick start
```powershell
cd backend
python -m venv .venv; .\.venv\Scripts\pip install -r requirements.txt
.\.venv\Scripts\python -m pytest -q
.\.venv\Scripts\python -m uvicorn main:app --reload
```
Copy `.env.example` to `.env` for Gemini, Cartesia and Convex keys. Full instructions: [docs/RUNBOOK.md](docs/RUNBOOK.md).
