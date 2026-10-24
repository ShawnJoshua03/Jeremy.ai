# Jeremy.ai instructor dashboard

The instructor console reads its live session, event, annotation, and command views from the read-only Convex mirror. The backend remains the source of truth: dashboard annotations and headset commands are written to REST, and the backend ingests mirrored rows through a shared-secret Convex HTTP action.

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

The backend must expose `POST /api/v1/sessions/{id}/annotations` with `{ "at_ms": number, "text": string, "author": string }` and `POST /api/v1/sessions/{id}/commands` with `{ "type": "highlight" | "pause" | "retry", "at_ms"?: number }`. The backend must persist these writes and mirror its authoritative records to Convex. These REST routes and the backend mirror caller are not present in the current backend and are outside this dashboard/Convex change; until they are implemented, the dashboard surfaces the backend's request errors and live rows will remain absent.

## Data and mocked behavior

- The Convex mirror can list sessions and return per-session events, annotations, and commands. Session details, metrics, and pose samples still use the existing REST read routes. The current backend does not yet provide a session list, event-history read route, annotation/command write routes, or Convex mirror caller.
- The “Sample preview” uses synthetic trainees, metrics, pose paths, and error events to demonstrate replay; it never calls the backend or Convex.
- Practice scores are deterministic backend outputs and are not clinically validated. Running-session scores are withheld when the REST metrics endpoint computes them using zero elapsed time.
- The Convex HTTP ingest route is protected by the server-side shared secret. Convex read queries are public, so add instructor authentication and authorization before exposing session data to untrusted users.

## Production build

```powershell
npm run build
```
