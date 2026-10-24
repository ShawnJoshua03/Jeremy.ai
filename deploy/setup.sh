#!/usr/bin/env bash
# Run on a Vultr Ubuntu VM: sudo bash deploy/setup.sh /absolute/path/to/checkout
set -euo pipefail
umask 022

if [[ ${EUID} -ne 0 ]]; then
    echo "Run with sudo on the target Ubuntu VM." >&2
    exit 1
fi
if [[ $# -gt 1 ]]; then
    echo "Usage: sudo bash deploy/setup.sh [/path/to/checkout]" >&2
    exit 1
fi
source /etc/os-release
if [[ ${ID} != ubuntu ]]; then
    echo "This installer targets Ubuntu (24.04 LTS recommended)." >&2
    exit 1
fi
repo=$(realpath -- "${1:-$(dirname -- "${BASH_SOURCE[0]}")/..}")
if [[ ! -f "$repo/backend/main.py" || ! -f "$repo/backend/requirements.txt" ||
      ! -f "$repo/deploy/jeremy-api.service" || ! -f "$repo/deploy/backend.env.example" ]]; then
    echo "Expected a repository checkout with backend/ and deploy/." >&2
    exit 1
fi
# The fixed destination must never overlap the source of rsync --delete.
if [[ "$repo" == /opt/jeremy || "$repo" == /opt/jeremy/* ]]; then
    echo "Keep the source checkout outside /opt/jeremy." >&2
    exit 1
fi
if [[ -L /opt/jeremy || -L /opt/jeremy/backend || -L /opt/jeremy/venv ]]; then
    echo "Deployment directories must not be symlinks." >&2
    exit 1
fi

export DEBIAN_FRONTEND=noninteractive
apt-get update
apt-get install -y python3 python3-venv python3-pip rsync curl ca-certificates
if ! id jeremy >/dev/null 2>&1; then
    useradd --system --user-group --home-dir /var/lib/jeremy --no-create-home --shell /usr/sbin/nologin jeremy
fi
install -d -o root -g root -m 0755 /opt/jeremy /opt/jeremy/backend
install -d -o root -g root -m 0700 /etc/jeremy
install -d -o jeremy -g jeremy -m 0700 /var/lib/jeremy
if [[ ! -e /etc/jeremy/backend.env ]]; then
    install -o root -g root -m 0600 "$repo/deploy/backend.env.example" /etc/jeremy/backend.env
fi
chown root:root /etc/jeremy/backend.env
chmod 0600 /etc/jeremy/backend.env

# Stop before updating the shared venv/code. Failures leave the service stopped
# rather than serving a partially updated installation; rerun after fixing.
if systemctl is-active --quiet jeremy-api.service; then
    systemctl stop jeremy-api.service
fi
rsync -rlptgo --delete --chown=root:root --chmod=D755,F644 \
    --exclude='.venv/' --exclude='__pycache__/' --exclude='.pytest_cache/' \
    --exclude='.env*' --exclude='*.db*' --exclude='*.sqlite*' \
    "$repo/backend/" /opt/jeremy/backend/
python3 -m venv /opt/jeremy/venv
/opt/jeremy/venv/bin/python -m pip install -r /opt/jeremy/backend/requirements.txt
install -o root -g root -m 0644 "$repo/deploy/jeremy-api.service" /etc/systemd/system/jeremy-api.service
systemctl daemon-reload
systemctl enable jeremy-api.service
systemctl restart jeremy-api.service

for attempt in {1..15}; do
    if curl --fail --silent --show-error --max-time 2 http://127.0.0.1:8000/health; then
        printf '\nBackend ready. Edit /etc/jeremy/backend.env, then restart jeremy-api.\n'
        exit 0
    fi
    sleep 2
done
echo "Health check failed. Inspect: journalctl -u jeremy-api -n 100 --no-pager" >&2
exit 1
