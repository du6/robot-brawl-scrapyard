#!/usr/bin/env zsh
# ===========================================================================
# run_local.sh — one command: build the API, boot it against the local
# database, run api_smoke.sh, stop it again, and leave both logs on disk.
#
# Writes, next to the server tree:
#   qa_api_server.log   the API's own output, build errors and stack traces
#   qa_sql_bench.txt    the data layer's bench
#   qa_api_smoke.txt    the endpoint bench
#   qa_restore_drill.txt  the backup/restore drill (§M4)
#
# Both are files rather than console output on purpose: the Cowork session
# can read files in this folder, so nobody has to copy a terminal into chat.
# ===========================================================================
set -u
HERE="${0:A:h}/.."
cd "$HERE" || exit 2

# Homebrew keeps both of these keg-only, so they are not on a login PATH.
export PATH="/opt/homebrew/opt/dotnet@8/bin:/opt/homebrew/opt/postgresql@16/bin:$PATH"
export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec"

SRV="$PWD/qa_api_server.log"
SQL="$PWD/qa_sql_bench.txt"
OUT="$PWD/qa_api_smoke.txt"

if lsof -nP -iTCP:5000 -sTCP:LISTEN >/dev/null 2>&1; then
  echo "Port 5000 is already in use — stop the API running in your other tab (Ctrl-C) and re-run."
  exit 2
fi

# The dev database was created today for exactly this and holds nothing worth
# keeping. Resetting makes the bench repeatable: a job left READY by a failed
# run would otherwise be the job the next run claims.
echo "resetting the dev database…"
PGPASSWORD=rb psql -h localhost -U rb -d rb -qtA \
  -c "TRUNCATE ledger, match_jobs, matches, snapshots, robots, users, tickets, ratings RESTART IDENTITY CASCADE;" \
  >/dev/null 2>&1 || echo "  (nothing to reset — first run, or the schema is not up yet)"

echo "starting the API (log: qa_api_server.log)…"
( cd RobotBrawl.Api && \
  PG_CONN="Host=localhost;Username=rb;Password=rb;Database=rb" \
  JWT_SECRET="dev-only-change-me-0123456789abcdef" \
  WORKER_KEY="dev-only-worker-key" \
  BLOB_ROOT="/tmp/rb-blobs" \
  dotnet run ) > "$SRV" 2>&1 &
API_PID=$!
trap 'kill $API_PID 2>/dev/null' EXIT INT TERM

# dotnet run compiles first, so allow for a cold build.
echo -n "waiting for /healthz "
for i in {1..90}; do
  if curl -sf http://localhost:5000/healthz >/dev/null 2>&1; then echo " up after ${i}s"; break; fi
  if ! kill -0 $API_PID 2>/dev/null; then
    echo " — the API exited before it listened."
    echo "LAST 40 LINES OF qa_api_server.log:"; tail -40 "$SRV"
    exit 1
  fi
  echo -n .; sleep 1
  [ $i -eq 90 ] && { echo " — timed out."; tail -40 "$SRV"; exit 1; }
done

# 2026-08-09. sql_bench.sh used to be missing from this script, which made
# "one command" a promise it did not keep: a change to RobotBrawl.Api/Sql/*.sql
# — the files sql_bench is the ONLY cover for — could come back all-green from
# here having never been run against the bench that tests it. That is exactly
# what happened to the claim_job.sql rewrite that added payload location to the
# claim: api_smoke went 35/35 -> 37/37 and proved a SINGLE worker's claim,
# while the FOUR-concurrent-worker SKIP LOCKED case sat untested.
#
# It runs AFTER the API boots, because the API's migration is what creates the
# schema, and BEFORE api_smoke, because sql_bench truncates every table and
# api_smoke builds its own fixtures afterwards.
echo "running sql_bench.sh (result: qa_sql_bench.txt)…"
bash tests/sql_bench.sh > "$SQL" 2>&1
SQLRC=$?

echo "running api_smoke.sh (result: qa_api_smoke.txt)…"
bash tests/api_smoke.sh > "$OUT" 2>&1

# The restore drill runs LAST and against the database the benches just
# filled: a backup of an empty schema proves nothing, and by now this one
# holds accounts, matches, ratings, cosmetics and a settled season. It
# restores into a SEPARATE database and drops it again, so it never touches
# what it is checking.
echo "running restore_drill.sh (result: qa_restore_drill.txt)…"
bash tests/restore_drill.sh > "$PWD/qa_restore_drill.txt" 2>&1
RC=$?

kill $API_PID 2>/dev/null; wait $API_PID 2>/dev/null
echo
tail -1 "$SQL"
tail -1 "$OUT"
grep -c 'error' "$SRV" >/dev/null 2>&1 && \
  echo "server log: $(grep -ci 'exception\|error' "$SRV") line(s) mentioning error/exception"
[ $SQLRC -ne 0 ] && exit $SQLRC
exit $RC
