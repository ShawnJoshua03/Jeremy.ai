# Jeremy.ai instructor dashboard

The backend REST API is the source of truth. The console reads sessions, events, and annotations from REST first, falling back to the read-only Convex mirror when those requests fail. Dashboard annotations and headset commands are written to REST; the backend can mirror authoritative rows through the shared-secret Convex HTTP action.

## Run locally

```powershell
cd dashboard
npm install
Copy-Item .env.example .env.local
npm run dev
```

The Vite development server proxies `/api` to `http://localhost:8000` by default. Set `VITE_PROXY_TARGET` to another backend origin if needed. `VITE_API_URL` defaults to `/api/v1`; deployed dashboards need backend CORS configured for the dashboard origin.

The Convex project root is this `dashboard/` directory; its functions are in `dashboard/convex/`. Set `VITE_CONVEX_URL` in `dashboard/.env.local` to the public Convex cloud URL. Convex deployment credentials and `CONVEX_INGEST_SHARED_SECRET` are server-side only; never put them in `VITE_*` variables. To push to the configured deployment from PowerShell, set `$env:CONVEX_DEPLOYMENT` and run `npx convex dev --once` here.

Set a random secret of 32–256 characters as the Convex deployment environment variable `CONVEX_INGEST_SHARED_SECRET` and configure the same value in the backend's server-side environment. The backend sends a `POST` to `https://<deployment>.convex.site/v1/mirror` with `Authorization: Bearer <secret>` and a JSON body containing `sessions`, `events`, `annotations`, and `commands` arrays. The JSON uses camelCase fields matching the Convex schema: sessions (`sessionId`, `traineeId`, status/timestamps, optional metrics), events (`sessionId`, `eventId`, `kind`, `tMs`, instrument/object and optional coordinates), annotations (`annotationId`, `sessionId`, `atMs`, text, author, epoch-millisecond `createdAt`), and commands (`commandId`, `sessionId`, type, nullable `atMs`, state, and epoch-millisecond timestamps). Ingestion upserts by stable external IDs, so retrying a batch is safe. Each array is limited to 1,000 records and the request body to 1 MiB. Convex queries are read-only; browser users cannot write mirrored data or call the internal ingest mutation.

The dashboard uses `GET /api/v1/sessions`, `GET /api/v1/sessions/{id}/events`, and `GET /api/v1/sessions/{id}/annotations` for live lists and replay. It also posts annotations to `/api/v1/sessions/{id}/annotations` with `{ "at_ms": number, "text": string, "author": string }` and commands to `/api/v1/sessions/{id}/commands` with `{ "type": "highlight" | "pause" | "retry", "at_ms"?: number }`. The backend must persist these writes and mirror its authoritative records to Convex; until that integration is enabled, the dashboard surfaces backend request errors and Convex can supply the read fallback.

## Data and mocked behavior

- REST session, event, and annotation reads are preferred; Convex supplies a fallback for these lists and the command history. Session details, metrics, and pose samples still use the existing REST read routes. If a REST list fails, the UI identifies the fallback source; if both reads fail, the REST error is shown.
- The “Sample preview” uses synthetic trainees, metrics, pose paths, and error events to demonstrate replay; it never calls the backend or Convex.
- Practice scores are deterministic backend outputs and are not clinically validated. Running-session scores are withheld when the REST metrics endpoint computes them using zero elapsed time.
- The Convex HTTP ingest route is protected by the server-side shared secret. Convex read queries are public, so add instructor authentication and authorization before exposing session data to untrusted users.

## Production build

```powershell
npm run build
```

## Live call (SpacetimeDB)

The home page is a call room. A mentor creates a session, the trainee joins with the code (or the invite link `?room=CODE`), and the trainee shares the Meta Quest Link window. Video and audio go peer-to-peer over WebRTC. SpacetimeDB holds only the room state and the small signaling messages (offers, answers, ICE candidates) and chat. Video is never stored and nothing is recorded.

- The instructor console (live sessions, replay, coach) stays at `#console`.
- Set `VITE_SPACETIMEDB_URI` and `VITE_SPACETIMEDB_DB` in `.env.local` (local: `ws://localhost:3000` and the local database name).
- The module is in `dashboard/spacetimedb/`. Publish with `spacetime publish -s local jeremy-ai-call --module-path spacetimedb -y` (or `-s maincloud` after `spacetime login`), then regenerate the committed bindings with `spacetime generate --lang typescript --out-dir src/module_bindings --module-path spacetimedb`. Rerun both after any change to the module.
- The connection uses public STUN only, with no TURN server. Networks that block peer-to-peer media will not connect; a phone hotspot is the workaround. This is not verified on the target networks.
- The trainee's microphone and shared screen are one-way from the trainee. Screen capture is video only; the mic carries voice.
