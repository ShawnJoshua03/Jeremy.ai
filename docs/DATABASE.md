# Database

SQLite is the database this repo runs. Postgres is accepted when `DATABASE_URL` names one. A `postgres` process was listening on `127.0.0.1:5432` on 2026-10-24. This repo has no `DATABASE_URL`, and a passwordless connection to that server was refused. **Postgres was not run.**

## Schema

`init_db` calls `create_all`. It does not alter columns on a table that already exists. It does add `ix_samples_session_t_ms` when that index is missing.

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

Sample and event uploads insert with `ON CONFLICT DO NOTHING` on those unique keys. A batch is not deduped by loading every key already stored for the session. Keys repeated inside one request are dropped before the insert. The response `duplicates` count is the rows the request sent minus the rows this statement stored.

## Pointing DATABASE_URL at Postgres

Unset, the URL is `sqlite:///./surgimentor.db`, relative to the process working directory. Tests build their own SQLite engine and do not use that file.

Install the driver from `backend/requirements.txt` (`psycopg[binary]`). Then set either of these:

```
DATABASE_URL=postgres://USER:PASSWORD@HOST:5432/DBNAME
DATABASE_URL=postgresql://USER:PASSWORD@HOST:5432/DBNAME
```

`postgres://` and a bare `postgresql://` are rewritten to `postgresql+psycopg://` so SQLAlchemy loads psycopg 3. An URL that already starts with `postgresql+psycopg://` is left as it is. The password stays in the environment, not in this file.

Start the API from `backend/` so the default SQLite path, when you are not using Postgres, is `backend/surgimentor.db`.

## Timescale hypertable

This code does not create a hypertable. `samples.t_ms` is an integer offset inside one attempt. Timescale can partition on an integer only when the table also has an `integer_now` function, and the usual choice is a `timestamptz` column instead.

Timescale requires the partitioning column to be part of the primary key and part of every unique index. Today the primary key is `pk` alone, and the dedupe key is `(session_id, instrument, idx)`. Neither includes a time column. Putting `t_ms` into that unique key would store the same instrument and index twice when the timestamp differs, which is not the current dedupe rule.

A later migration would add a real time column, include it in the primary key and in every unique index, and keep `(session_id, instrument, idx)` as the dedupe identity. That migration is not in this repo.
