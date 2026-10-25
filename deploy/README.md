# Vultr Ubuntu deployment

This installs one uvicorn process as an unprivileged systemd service, listening
on `127.0.0.1:8000`. No Docker is needed. Target: Ubuntu 24.04 LTS with systemd,
Python 3.12+, outbound HTTPS, and sudo access. No VM has been provisioned or
deployed by this change.

1. Create an Ubuntu VM in Vultr and SSH into it. Clone this private repository
   using your own Git credentials to a directory outside `/opt/jeremy` (for
   example `/home/ubuntu/Jeremy.ai`). Check out the commit you intend to deploy.
2. From the repository root, run:

   ```sh
   sudo bash deploy/setup.sh "$PWD"
   sudoedit /etc/jeremy/backend.env
   sudo systemctl restart jeremy-api
   curl --fail http://127.0.0.1:8000/health
   ```

   Put keys in the root-owned, mode-0600 environment file. It is loaded by
   systemd, not sourced as a shell script. Missing Cartesia credentials allow
   startup and make audio requests return 503 for text-only operation.
3. For remote HTTPS, point a DNS name to the VM, install Caddy using its
   [official Ubuntu instructions](https://caddyserver.com/docs/install#debian-ubuntu-raspbian),
   and adapt `Caddyfile.example` into `/etc/caddy/Caddyfile`. Then run:

   ```sh
   sudo caddy validate --config /etc/caddy/Caddyfile
   sudo systemctl reload caddy
   curl --fail https://YOUR_DOMAIN/health
   ```

   Allow inbound TCP 80/443 in the Vultr and OS firewalls for Caddy's HTTPS
   setup; retain SSH access. Port 8000 stays private. Caddy manages certificates
   as described in its [automatic HTTPS docs](https://caddyserver.com/docs/automatic-https).
   Set `API_KEY` in `/etc/jeremy/backend.env` before exposing the API. Clients
   must then send it as `X-API-Key` (or `Authorization: Bearer`). On the Quest,
   set it in the `PegTransferRig` "Backend Api Key" field. For the dashboard,
   build it (`cd dashboard && npm run build`), copy `dist/` to
   `/srv/jeremy-dashboard`, and use the dashboard block in `Caddyfile.example`,
   which adds the key server-side and puts the page behind basic auth. There is
   still no rate limiting.

The installer preserves `/etc/jeremy/backend.env` on repeat runs, copies only
the backend (excluding local credentials, virtual environments, caches and
databases), installs dependencies in `/opt/jeremy/venv`, installs/enables the
unit, and checks `/health`. It does not install a dashboard or reverse proxy,
change firewalls, or transfer a local database. `/health` checks application
availability, not Cartesia, Claude, or database readiness.

SQLite defaults to `/var/lib/jeremy/surgimentor.db`, outside the deployed code.
The unit permits writes there and runs with a read-only application directory.
An external `DATABASE_URL` requires its SQLAlchemy driver to be installed into
the service venv separately. Keep this single-worker setup for the SQLite demo.

To update, check out a new revision in the source checkout and rerun setup.
The installer stops the active service before replacing code/dependencies;
expect downtime. It preserves configuration and database files. On failure,
fix the reported error and rerun; there is no automatic rollback. For rollback,
check out a previous compatible revision and rerun (database migrations, if
added later, need their own rollback plan). Back up the SQLite file while the
service is stopped, or use SQLite's backup API.

```sh
sudo systemctl status jeremy-api --no-pager
sudo journalctl -u jeremy-api -n 100 --no-pager
sudo systemctl stop jeremy-api
sudo systemctl start jeremy-api
```

Audio smoke test after setting a real key (this invokes paid synthesis):

```sh
sid=$(curl --fail --silent http://127.0.0.1:8000/api/v1/sessions \
  -H 'Content-Type: application/json' -d '{"trainee_id":"deploy-smoke"}' \
  | python3 -c 'import json,sys; print(json.load(sys.stdin)["id"])')
curl --fail --show-error "http://127.0.0.1:8000/api/v1/sessions/$sid/coach/audio" \
  -H 'Content-Type: application/json' -d '{"text":"Focus on controlled placement."}' \
  --output /tmp/jeremy-coach.wav
```

Local verification covers Python tests and Bash syntax. Installation, systemd
startup/hardening, HTTPS issuance, Vultr networking, and real Cartesia playback
must still be verified on the VM/headset. Backend requirements have version
ranges but no lock file, so dependency resolution is not fully reproducible.
