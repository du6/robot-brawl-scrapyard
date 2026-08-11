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

# ⚠ ONE VARIABLE, TWO CONSUMERS, AND THEY MUST NOT DRIFT — 2026-08-10.
#
# The API reads TRUST_PROXY to decide whether to honour X-Forwarded-For, and
# api_smoke.sh reads THE SAME NAME OUT OF ITS OWN SHELL to decide whether the
# proxy-header section can run at all. This script used to set it only on the
# server's environment, so the section skipped on every one-command run and
# passed only when a human happened to export it by hand first.
#
# That is the failure mode the skip was designed to avoid and fell into
# anyway: `skip` prints one line in a 198-line file and the summary still says
# "failed 0", so a check covering X-Forwarded-For SPOOF RESISTANCE — a
# cloud-only defect class, see docs/Cloud_Only_Defects_2026-08-09.md — looked
# green while never executing. Found by running the bench from a fresh clone
# in a shell that had never exported it: 196 passed, 1 skipped, against the
# working copy's 198.
#
# Exporting it here means the process that STARTS the server and the bench
# that TESTS it can only ever be told the same thing.
export TRUST_PROXY="1"

# ⚠ PORT 5099, NOT 5000 — 2026-08-10, and this was NOT a cosmetic change.
#
# macOS enables AirPlay Receiver by default and it LISTENS ON 5000, on
# loopback. Measured on owen's Mac:
#   curl -i http://localhost:5000/  ->  403 Forbidden, Server: AirTunes/960.13.1
#   bind(127.0.0.1:5000)            ->  EADDRINUSE
# The guard below could not see it either: `lsof` without sudo does not list a
# process owned by another user, so this script sailed past its own port check
# and then sat in the /healthz loop until it timed out.
#
# The Unity client had the same literal (LadderClient.LOCAL_DEV) and there it
# was far worse: the live benches SKIP when no server answers, so
# EnlistLiveBench read 0 passed / 0 failed / 1 skipped and the suite summary
# said "failed 0". Green, and covering nothing.
#
# ONE port for local dev, in one place per side. Override with RB_PORT if 5099
# is ever taken too.
RB_PORT="${RB_PORT:-5099}"
BASE="http://localhost:$RB_PORT"

if lsof -nP -iTCP:"$RB_PORT" -sTCP:LISTEN >/dev/null 2>&1; then
  echo "Port $RB_PORT is already in use — stop the API running in your other tab (Ctrl-C) and re-run."
  exit 2
fi
# lsof cannot see another user's listener, so ASK THE PORT rather than trust the
# scan. This is the check that would have caught AirTunes on 5000.
if curl -s -o /dev/null -m 2 "$BASE/" 2>/dev/null; then
  echo "Something already answers on $BASE — it is not this API. Free the port or set RB_PORT."
  exit 2
fi

# The dev database was created today for exactly this and holds nothing worth
# keeping. Resetting makes the bench repeatable: a job left READY by a failed
# run would otherwise be the job the next run claims.
#
# ⚠ seasons AND ladder_config.current_season must be reset TOGETHER, and this
# is why. TRUNCATE (via CASCADE) empties `seasons`, but `current_season` lives
# in ladder_config and is not a truncation target, so it survives. Every
# api_smoke run rolls the season over and increments it, and the two drift
# apart until the rollover check fails with "season N already exists" against
# an EMPTY seasons table — a state neither bench ever creates on purpose.
#
# It looks like a broken rollover and it is not: the endpoint refusing a
# duplicate season is the idempotency guard doing its job. The bench was not
# repeatable, which is the one thing this block exists to guarantee. Six
# checks failed this way before anyone noticed the counter was the problem.
echo "resetting the dev database…"
PGPASSWORD=rb psql -h localhost -U rb -d rb -qtA \
  -c "TRUNCATE ledger, match_jobs, matches, snapshots, robots, users, tickets, ratings, seasons RESTART IDENTITY CASCADE;" \
  -c "UPDATE ladder_config SET value = '1' WHERE key = 'current_season';" \
  >/dev/null 2>&1 || echo "  (nothing to reset — first run, or the schema is not up yet)"

echo "starting the API (log: qa_api_server.log)…"
( cd RobotBrawl.Api && \
  PG_CONN="Host=localhost;Username=rb;Password=rb;Database=rb" \
  JWT_SECRET="dev-only-change-me-0123456789abcdef" \
  WORKER_KEY="dev-only-worker-key" \
  BLOB_ROOT="/tmp/rb-blobs" \
  TRUST_PROXY="$TRUST_PROXY" \
  Queue__ReapEverySeconds="5" \
  ASPNETCORE_URLS="$BASE" \
  dotnet run ) > "$SRV" 2>&1 &
API_PID=$!

# ⚠ $! IS THE SUBSHELL, NOT THE SERVER — 2026-08-10, and this leaked for months.
#
# `( cd … && dotnet run ) &` backgrounds a SUBSHELL. `dotnet run` then execs a
# second process which builds and launches a THIRD, `RobotBrawl.Api`, and that
# last one is what holds the port. Killing $API_PID reaped the wrapper and left
# the listener running: measured, pid 93703 still serving 5099 long after this
# script had exited 0.
#
# It was invisible before today because a leaked listener on a port nobody
# re-checked just sat there. THE PORT GUARD ABOVE IS WHAT MADE IT MATTER: with
# a guard, run_local.sh run twice in a row failed the second time — "Something
# already answers on …" — because the thing answering was its own previous run.
# The guard is correct and stays; it did not cause this, it EXPOSED it. Blaming
# the guard and deleting it would restore a silent leak in place of a loud one.
#
# Everything holding $RB_PORT right now is OURS, and the guard is what licenses
# that claim: it refused to start unless the port was free, so nothing else can
# have taken it in the seconds since. That is the whole reason this is safe to
# do by port at all — never blanket-kill a port you did not prove was free.
cleanup_api() {
  kill $API_PID 2>/dev/null
  local held
  held="$(lsof -nP -iTCP:"$RB_PORT" -sTCP:LISTEN -t 2>/dev/null | sort -u)"
  [ -n "$held" ] && kill $=held 2>/dev/null
  for _ in {1..10}; do
    lsof -nP -iTCP:"$RB_PORT" -sTCP:LISTEN -t >/dev/null 2>&1 || return 0
    sleep 1
  done
  # Escalate rather than exit quietly. A leak that announces itself is the
  # difference between this bug and the version of it that hid.
  held="$(lsof -nP -iTCP:"$RB_PORT" -sTCP:LISTEN -t 2>/dev/null | sort -u)"
  [ -n "$held" ] && kill -9 $=held 2>/dev/null
  sleep 1
  if lsof -nP -iTCP:"$RB_PORT" -sTCP:LISTEN -t >/dev/null 2>&1; then
    echo "⚠ something is STILL listening on $RB_PORT after cleanup — the next run will refuse to start."
    lsof -nP -iTCP:"$RB_PORT" -sTCP:LISTEN
  fi
}
trap cleanup_api EXIT INT TERM

# dotnet run compiles first, so allow for a cold build.
echo -n "waiting for /healthz "
for i in {1..90}; do
  if curl -sf "$BASE/healthz" >/dev/null 2>&1; then echo " up after ${i}s"; break; fi
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
bash tests/api_smoke.sh "$BASE" > "$OUT" 2>&1

# The restore drill runs LAST and against the database the benches just
# filled: a backup of an empty schema proves nothing, and by now this one
# holds accounts, matches, ratings, cosmetics and a settled season. It
# restores into a SEPARATE database and drops it again, so it never touches
# what it is checking.
echo "running restore_drill.sh (result: qa_restore_drill.txt)…"
bash tests/restore_drill.sh > "$PWD/qa_restore_drill.txt" 2>&1
RC=$?

# One implementation of stopping the server, so the happy path and the trap
# cannot drift. The trap still fires afterwards; cleanup_api is idempotent.
cleanup_api; wait $API_PID 2>/dev/null
echo
tail -1 "$SQL"
tail -1 "$OUT"
grep -c 'error' "$SRV" >/dev/null 2>&1 && \
  echo "server log: $(grep -ci 'exception\|error' "$SRV") line(s) mentioning error/exception"
[ $SQLRC -ne 0 ] && exit $SQLRC
exit $RC
