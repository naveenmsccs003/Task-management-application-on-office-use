#!/usr/bin/env bash
# End-to-end browser check of the login system.
# Requires: .NET 10 SDK, Node.js, frontend dependencies installed, and a Chromium binary in
# E2E_CHROMIUM (e.g. /usr/bin/chromium or a Playwright-managed chromium).
set -euo pipefail
cd "$(dirname "$0")"
ROOT="$(cd .. && pwd)"
: "${E2E_CHROMIUM:?Set E2E_CHROMIUM to a Chromium/Chrome executable}"
mkdir -p output
[ -d node_modules ] || npm install --no-audit --no-fund

export ASPNETCORE_TEST_CONTENTROOT_TASKMANAGEMENT_API="$ROOT/backend/src/TaskManagement.Api"
dotnet build DevHost -v q >/dev/null
# Each server runs in its own process group so the whole tree is stopped on exit.
setsid dotnet run --no-build --project DevHost > output/devhost.log 2>&1 &
HOST_PID=$!
(cd "$ROOT/frontend" && exec setsid npx ng serve --port 4200) > output/ngserve.log 2>&1 &
NG_PID=$!
trap 'kill -- -$HOST_PID -$NG_PID 2>/dev/null || true' EXIT

for _ in $(seq 1 90); do
  if grep -q READY output/devhost.log && curl -sf -o /dev/null http://localhost:4200; then break; fi
  sleep 2
done

node flow.mjs "$PWD/output/devhost.log"
