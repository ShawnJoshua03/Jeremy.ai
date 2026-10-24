# Database

The default database is SQLite: `sqlite:///./surgimentor.db`, relative to the process working directory. Tests use their own SQLite engine.

Postgres is optional. Set `DATABASE_URL` to `postgres://USER:PASSWORD@HOST:5432/DBNAME` or `postgresql://USER:PASSWORD@HOST:5432/DBNAME`. Both are rewritten to `postgresql+psycopg://`. The driver is `psycopg[binary]` in `backend/requirements.txt`. **Postgres was not run.**

## Schema

`init_db` calls `create_all`. It does not rewrite existing columns. It adds `client_request_id` on `sessions` when that column is missing, and it adds `ix_samples_session_t_ms` and `ix_sessions_client_request_id` when those indexes are missing.

`sessions`

| Column | Notes |
|---|---|
| `id` | Primary key, hex string |
| `trainee_id` | Indexed |
| `exercise` | Default `peg_transfer` |
| `status` | `ready`, `running`, or `completed` |
| `created_at`, `started_at`, `completed_at` | Timestamps. `started_at` and `completed_at` are null until those steps |
| `elapsed_s` | Null until complete |
| `metrics` | JSON null until complete |
| `client_request_id` | Optional, unique, null when omitted. Multiple nulls are allowed. The API does not return it |

`samples`

| Column | Notes |
|---|---|
| `pk` | Integer primary key |
| `session_id` | Indexed |
| `instrument`, `idx` | Unique together with `session_id` |
| `t_ms` | Milliseconds from the attempt, not a wall-clock time |
| `x`, `y`, `z`, `qx`, `qy`, `qz`, `qw` | Board-local pose |
| `valid`, `interaction` | Tracking flag and `grasping` or `idle` |

Index `ix_samples_session_t_ms` is `(session_id, t_ms)`.

`events`

| Column | Notes |
|---|---|
| `pk` | Integer primary key |
| `session_id`, `event_id` | Unique together. `session_id` is indexed |
| `kind`, `t_ms`, `instrument`, `object_id` | Event payload |
| `source`, `target` | JSON length-3 coordinates, or null |

`commands`

| Column | Notes |
|---|---|
| `pk` | Integer primary key |
| `session_id`, `command_id` | Unique together. `session_id` is indexed |
| `type`, `target_id`, `expires_at`, `message`, `exercise_id` | Headset command fields |
| `ack_status`, `ack_message` | Null until the first acknowledgement |
| `created_at` | Insert time |

`annotations`

| Column | Notes |
|---|---|
| `pk` | Integer primary key |
| `annotation_id` | Unique hex string |
| `session_id` | Indexed |
| `at_ms`, `text`, `author` | Instructor note |
| `created_at` | Insert time |

Sample and event uploads insert with `ON CONFLICT DO NOTHING` on those unique keys. A batch is not deduped by loading every key already stored for the session. Keys repeated inside one request are dropped before the insert. The response `duplicates` count is the rows the request sent minus the rows this statement stored.

## Convex mirror

Quest polls the mentorship routes. SQLite is the source of truth. After an annotation, command, or event is stored, `backend/convex_mirror.py` POSTs the row to Convex. `CONVEX_URL` is the HTTP actions origin, such as `https://<deployment>.convex.site`. The path `/v1/mirror` is added when the URL does not already end with it. `CONVEX_INGEST_SHARED_SECRET` (old name `CONVEX_DEPLOY_KEY`) is the bearer token and must match the Convex deployment variable of the same name. A non-2xx reply is logged as a warning. If either variable is unset, the mirror does nothing. A network or HTTP failure leaves the API response unchanged.
