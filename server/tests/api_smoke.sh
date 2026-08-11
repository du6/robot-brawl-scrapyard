#!/usr/bin/env bash
# ===========================================================================
# api_smoke.sh — the ladder API's end-to-end bench.
#
# sql_bench.sh proves the data layer without a line of C#. This proves the
# half that only exists once the service is running: authn, the authz
# boundary in §1.3, the opaque-upload rule in §5.2, and the worker round
# trip in §5.3. It talks to a REAL server over HTTP, because the questions
# it asks — "does an anonymous scout see the payload URL", "does the second
# upload supersede the first" — are not answerable from either half alone.
#
# Usage:  bash tests/api_smoke.sh [base-url]      (default http://localhost:5099)
#
# ⚠ 5099, NOT 5000. macOS AirPlay Receiver listens on 5000 by default, answers
# 403 as AirTunes, and holds the bind — so the API cannot start there and this
# bench cannot reach it. run_local.sh boots on the same port and passes it in.
#
# TWO RULES LEARNED THE HARD WAY, on this bench's first run (08-09):
#
#   * A check that compares two variables MUST refuse to pass when they are
#     empty. The first cut asserted [ "$JSNAP" = "$SNAP1" ] and reported PASS
#     while both were the empty string, because the step that should have
#     filled them had already failed. A bench that passes on nothing is worse
#     than one that fails, because it is believed.
#   * A failed prerequisite SKIPs its dependants rather than failing them.
#     One 500 on /v1/robots produced seventeen failures on the first run and
#     buried its own cause. Skips are counted and printed, never silent.
#
# NOTE: the "auth" rate-limit policy allows 10 requests/min per IP and this
# bench spends 6. A re-run inside the same minute shows 429s rather than the
# expected codes — that is the limiter working, not a break.
# ===========================================================================
set -uo pipefail

BASE="${1:-http://localhost:5099}"
WKEY="${WORKER_KEY:-dev-only-worker-key}"
PY=$(command -v python3 || command -v python) || { echo "python3 is required"; exit 2; }

pass=0; fail=0; skipped=0
SKIP_LOG=/tmp/rb_skips.$$; : > "$SKIP_LOG"
ok()   { pass=$((pass+1));       printf 'PASS  %s\n' "$1"; }
no()   { fail=$((fail+1));       printf 'FAIL  %s\n' "$1"; }
# ⚠ A SKIP COUNT UNDERSTATES WHAT IS MISSING, and the summary must not hide
# it — 2026-08-10. `skipped` counts CALLS, not checks: section L's single skip
# stands in for FOURTEEN checks, so "skipped 1" beside "failed 0" can mean a
# seventh of this bench never executed and the run still reads as a pass.
#
# Measured, and this is why the banner exists: the proxy section skipped on
# every one-command run for a day because run_local.sh set TRUST_PROXY on the
# SERVER's environment and not on this script's. Nothing said so. The reasons
# are replayed at the end so what did not run is stated, not merely counted.
skip() { skipped=$((skipped+1)); printf 'SKIP  %s -- %s\n' "$1" "$2"
         printf '  %s -- %s\n' "$1" "$2" >> "$SKIP_LOG"; }
note() { printf '      %s\n' "$1"; }

BODY=/tmp/rb_body.$$
req() { # <method> <path> [json-body] [header...]
  local m="$1" p="$2" d="${3:-}"; shift 3 2>/dev/null || shift 2
  local args=(-s -o "$BODY" -w '%{http_code}' -X "$m" "$BASE$p")
  [ -n "$d" ] && args+=(-H 'Content-Type: application/json' -d "$d")
  for h in "$@"; do args+=(-H "$h"); done
  curl "${args[@]}"
}
jget() { "$PY" -c "import json,sys;d=json.load(open('$BODY'));k='$1'.split('.');
[d:=(d[int(x)] if isinstance(d,list) else d.get(x)) for x in k];print('' if d is None else d)" 2>/dev/null; }

expect() { # <label> <got-status> <want-status>
  [ "$2" = "$3" ] && ok "$1" || no "$1 -- HTTP $2, wanted $3: $(head -c 140 "$BODY" | tr -d '\n')"
}
# same() refuses to pass on empty. This is the fix for the vacuous-PASS bug.
same() { # <label> <got> <want>
  if [ -z "$2" ] || [ -z "$3" ]; then no "$1 -- compared empty values ('$2' vs '$3'); an earlier step did not produce one"
  elif [ "$2" = "$3" ]; then ok "$1"
  else no "$1 -- got '$2', wanted '$3'"; fi
}
is() { # <label> <got> <want>   (single value, must be non-empty)
  if [ -z "$2" ]; then no "$1 -- value was empty, wanted '$3'"
  elif [ "$2" = "$3" ]; then ok "$1"
  else no "$1 -- got '$2', wanted '$3'"; fi
}

# An envelope the way RobotSnapshot.Wrap builds one: the payload is a JSON
# STRING, and sha256 is over that string's UTF-8 bytes. Built here from first
# principles rather than copied from a fixture — if the two implementations
# ever disagree about what is hashed, this bench notices.
envelope() { # <name> [break-the-hash]
  "$PY" - "$1" "${2:-}" <<'PYEOF'
import json,hashlib,sys
name, broken = sys.argv[1], sys.argv[2]
payload = json.dumps({"payloadVersion":1,"robotName":name,
                      "parts":[{"id":"chassis_a","x":0,"z":0}],"program":""},
                     separators=(',',':'))
sha = hashlib.sha256(payload.encode('utf-8')).hexdigest()
if broken: sha = ('0'*63) + ('1' if sha[-1] != '1' else '2')
print(json.dumps({"payload":payload,"clientVersion":"0.9+smoke+p1","sha256":sha}))
PYEOF
}
body_upload() { "$PY" -c "import json,sys;print(json.dumps({'robotId':sys.argv[1],'envelope':sys.argv[2]}))" "$1" "$2"; }

# Ask the database directly. Some contracts are invisible from the HTTP side:
# a torn write still answers 200, and SQL NULL and the empty string are the
# same three characters in a JSON response. Same credentials run_local.sh uses.
# PGPORT is a parameter because the compose stack publishes Postgres on 5433,
# NOT 5432 — a Mac running its own Postgres holds 127.0.0.1:5432 and shadows
# Docker's forward. Hardcoding 5432 here meant the db checks silently read the
# HOST's database while the API wrote to the container's: every comparison
# against live data was against the wrong rows. docker-compose.yml documents
# the same trap on the port mapping.
#
#   against compose:  PGPORT=5433 bash tests/api_smoke.sh http://localhost:8080
#
# RB_DB is a parameter for a different reason: concurrent AGENTS, not concurrent
# ports. See the note in sql_bench.sh — it must name the same database the API
# under test is connected to, or every db-side assertion reads an empty schema
# and the HTTP checks pass while the SQL checks quietly compare nothing.
RB_PGPORT="${PGPORT:-5432}"
RB_DB="${RB_DB:-rb}"
dbq() { PGPASSWORD=rb psql -h localhost -p "$RB_PGPORT" -U rb -d "$RB_DB" -qtA -c "$1" 2>/dev/null | tr -d '[:space:]'; }

# Post a validate-result carrying a RAW JSON category value, on its own fresh
# snapshot and job, and echo the HTTP status. The category argument is spliced
# in unquoted so a caller can pass a bare null, an empty string, or a bad
# name — which is the whole point: JsonUtility on the Unity side serialises a
# null string as "", so "" is what a worker written the obvious way sends.
# Records the snapshot it made in $VRC_SNAP_FILE so the caller can read the
# row back. It goes through a FILE, not a variable: every caller invokes this
# as S=$(vr_category ...), which runs it in a subshell, and a variable set in
# a subshell does not survive. The first cut used a plain variable and lost
# both storage checks to SKIP.
VRC_SNAP_FILE="/tmp/rb_vrcsnap.$$"
VRC_JOB_FILE="/tmp/rb_vrcjob.$$"
vr_category() { # <raw-json-value> [legal=true]
  local rawcat="$1" legal="${2:-true}" snap job got i
  : > "$VRC_SNAP_FILE"; : > "$VRC_JOB_FILE"
  req POST /v1/snapshots "$(body_upload "$ROBOT" "$(envelope "Cat-$RANDOM-$RANDOM")")" "$AUTH" >/dev/null
  snap=$(jget id); [ -z "$snap" ] && { echo "NO-SNAPSHOT"; return; }
  # Claim until we hold the job for OUR snapshot: earlier sections may have
  # left a pending job, and claim() hands out the oldest one first.
  job=""
  for i in 1 2 3 4 5; do
    req POST /v1/worker/jobs/claim '{"workerId":"smoke-cat"}' "X-Worker-Key: $WKEY" >/dev/null
    got=$(jget id); [ -z "$got" ] && break
    if [ "$(jget snapshotId)" = "$snap" ]; then job="$got"; break; fi
    # not ours — retire it so the queue advances, then try again
    req POST "/v1/worker/jobs/$got/validate-result" \
      "{\"snapshotId\":\"$(jget snapshotId)\",\"workerId\":\"smoke-cat\",\"legal\":false,\"massKg\":1,\"aabbX\":0.1,\"aabbY\":0.1,\"aabbZ\":0.1,\"category\":null,\"partsManifest\":[],\"programHash\":\"\",\"failReasons\":[\"drained by vr_category\"]}" \
      "X-Worker-Key: $WKEY" >/dev/null
  done
  [ -z "$job" ] && { echo "NO-JOB"; return; }
  printf '%s' "$snap" > "$VRC_SNAP_FILE"
  printf '%s' "$job"  > "$VRC_JOB_FILE"
  req POST "/v1/worker/jobs/$job/validate-result" \
    "{\"snapshotId\":\"$snap\",\"workerId\":\"smoke-cat\",\"legal\":$legal,\"massKg\":9,
      \"aabbX\":0.4,\"aabbY\":0.3,\"aabbZ\":0.5,\"category\":$rawcat,
      \"partsManifest\":[\"chassis_a\"],\"programHash\":\"\",\"failReasons\":[]}" \
    "X-Worker-Key: $WKEY"
}

STAMP=$($PY -c "import time;print(int(time.time()))")
EMAIL="smoke-$STAMP@example.com"
PW="correct-horse-battery"
TOK=""; ROBOT=""; SNAP1=""; SNAP2=""; SNAP3=""; JOB=""; JOB2=""; JOB3=""

echo "== A. health =="
S=$(req GET /healthz); expect "healthz answers, which means the API reached Postgres" "$S" 200

echo
echo "== B. registration (§2.3) =="
S=$(req POST /v1/auth/register "{\"email\":\"$EMAIL\",\"password\":\"$PW\",\"displayName\":\"Smoke\"}")
expect "a new account registers" "$S" 200
TOK=$(jget token); USERID=$(jget userId)
[ -n "$TOK" ] && ok "…and comes back with a token" || no "no token in the register response"
S=$(req POST /v1/auth/register "{\"email\":\"$EMAIL\",\"password\":\"$PW\",\"displayName\":\"Twin\"}")
expect "the same email cannot register twice" "$S" 409
S=$(req POST /v1/auth/register "{\"email\":\"x-$STAMP@example.com\",\"password\":\"short\",\"displayName\":\"Sh\"}")
expect "a 5-character password is refused" "$S" 400

# 2026-08-09 (relay 003). Register was two INSERTs — the user and the
# 500-scrap SIGNING_BONUS — sharing one connection with NO transaction. §2.3
# makes a wallet balance SUM(ledger.delta) over an append-only table, so a
# tear between them is not a transient glitch: it is a permanently wrong
# balance whose only repair is another row. The checks above could not see
# it, because every one of them asks the API a question and the API answered
# 200 either way. This asks the DATABASE.
if ! command -v psql >/dev/null 2>&1; then
  skip "the signing bonus is atomic with the account (2 checks)" "psql is not on PATH"
elif [ -z "$USERID" ]; then
  skip "the signing bonus is atomic with the account (2 checks)" "registration returned no userId"
else
  is "a new account has exactly one ledger row" "$(dbq "SELECT count(*) FROM ledger WHERE user_id='$USERID';")" 1
  is "…and it is the 500-scrap SIGNING_BONUS" \
     "$(dbq "SELECT reason||':'||delta FROM ledger WHERE user_id='$USERID';")" "SIGNING_BONUS:500"
fi

echo
echo "== C. login is not an account-enumeration oracle =="
S=$(req POST /v1/auth/login "{\"email\":\"$EMAIL\",\"password\":\"$PW\"}")
expect "the right password logs in" "$S" 200
S=$(req POST /v1/auth/login "{\"email\":\"$EMAIL\",\"password\":\"wrong-wrong-wrong\"}"); WRONGPW="$S"
S=$(req POST /v1/auth/login "{\"email\":\"nobody-$STAMP@example.com\",\"password\":\"$PW\"}")
if [ "$WRONGPW" = "401" ] && [ "$S" = "401" ]; then
  ok "a wrong password and an unknown account are indistinguishable (both 401)"
else
  no "login leaks account existence: wrong-password $WRONGPW, unknown-account $S"
fi

echo
echo "== D. robots =="
if [ -z "$TOK" ]; then
  skip "a robot is created" "no token from registration"
  skip "…but not without a token" "no token from registration"
else
  AUTH="Authorization: Bearer $TOK"
  S=$(req POST /v1/robots '{"name":"Smoky"}' "$AUTH")
  expect "a robot is created" "$S" 200
  ROBOT=$(jget id); [ -n "$ROBOT" ] && note "robot $ROBOT"
  S=$(req POST /v1/robots '{"name":"Nope"}'); expect "…but not without a token" "$S" 401
fi

echo
echo "== E. snapshot upload stays opaque (§5.2) =="
if [ -z "$ROBOT" ]; then
  skip "snapshot upload (4 checks)" "no robot id -- section D did not complete"
else
  S=$(req POST /v1/snapshots "$(body_upload "$ROBOT" "$(envelope Smoky)")" "$AUTH")
  expect "a well-formed snapshot uploads" "$S" 200
  SNAP1=$(jget id); is "…and lands PENDING, waiting on a worker" "$(jget status)" PENDING
  S=$(req POST /v1/snapshots "$(body_upload "$ROBOT" "$(envelope Smoky break)")" "$AUTH")
  expect "a payload whose sha256 does not match is refused" "$S" 400
  S=$(req POST /v1/snapshots "$(body_upload "$ROBOT" '{"nonsense":true}')" "$AUTH")
  expect "an unreadable envelope is refused" "$S" 400
fi

echo
echo "== F. the worker path is key-gated (§5.5) =="
S=$(req POST /v1/worker/jobs/claim '{"workerId":"smoke-1"}')
expect "claiming without the worker key is refused" "$S" 401
S=$(req POST /v1/worker/jobs/claim '{"workerId":"smoke-1"}' "X-Worker-Key: wrong-key")
expect "…and a wrong key is refused too" "$S" 401
if [ -z "$SNAP1" ]; then
  skip "the worker claim round trip (5 checks)" "no snapshot was uploaded, so there is no job to claim"
else
  S=$(req POST /v1/worker/jobs/claim '{"workerId":"smoke-1"}' "X-Worker-Key: $WKEY")
  expect "a keyed worker claims a job" "$S" 200
  JOB=$(jget id); is "…and the job is the VALIDATE the upload enqueued" "$(jget kind)" VALIDATE
  same "…pointing at the snapshot just uploaded" "$(jget snapshotId)" "$SNAP1"
  # 2026-08-09. THIS is the assertion whose absence let the worker contract
  # ship unfinishable. Every other check in section F confirms the endpoint
  # ANSWERS; none confirmed the answer was usable. A claimed VALIDATE job that
  # does not say where its payload lives cannot be worked, and the suite was
  # 35/35 green while that was true.
  PURL=$(jget payloadUrl); PSHA=$(jget payloadSha256)
  [ -n "$PURL" ] && ok "…and carries a payload location the worker can resolve ($PURL)" \
                 || no "a claimed VALIDATE job carries no payloadUrl — the worker cannot fetch what it was given"
  case "$PSHA" in
    ????????????????????????????????????????????????????????????????)
      ok "…and the payload sha256 rides along, so the worker can verify storage" ;;
    *) no "payloadSha256 is '$PSHA', not 64 hex chars — Open() cannot verify the envelope" ;;
  esac
  if [ -z "$JOB" ]; then skip "heartbeat ownership (2 checks)" "no job id"; else
    S=$(req POST "/v1/worker/jobs/$JOB/heartbeat" '{"workerId":"smoke-9"}' "X-Worker-Key: $WKEY")
    expect "another worker cannot heartbeat a job it does not hold" "$S" 404
    S=$(req POST "/v1/worker/jobs/$JOB/heartbeat" '{"workerId":"smoke-1"}' "X-Worker-Key: $WKEY")
    expect "…the holder can" "$S" 200
  fi
fi

echo
echo "== G. the worker decides legality, the API records it (§5.2) =="
if [ -z "$JOB" ] || [ -z "$SNAP1" ]; then
  skip "validate-result round trip (5 checks)" "no claimed job -- section F did not complete"
else
  VR="{\"snapshotId\":\"$SNAP1\",\"workerId\":\"smoke-1\",\"legal\":true,\"massKg\":12,
       \"aabbX\":0.5,\"aabbY\":0.3,\"aabbZ\":0.6,\"category\":\"LIGHT\",
       \"partsManifest\":[\"chassis_a\"],\"programHash\":\"\",\"failReasons\":[]}"
  S=$(req POST "/v1/worker/jobs/$JOB/validate-result" "$VR" "X-Worker-Key: $WKEY")
  expect "a validate result is accepted" "$S" 200
  S=$(req GET "/v1/snapshots/$SNAP1" "" "$AUTH"); expect "the snapshot reads back" "$S" 200
  is "…now ACTIVE" "$(jget status)" ACTIVE
  is "…carrying the worker's mass" "$(jget massKg)" 12
  is "…and its category" "$(jget category)" LIGHT
fi

echo
echo "== H. the second upload supersedes the first (§1.2) =="
if [ -z "$SNAP1" ]; then
  skip "supersede round trip (3 checks)" "no first snapshot to supersede"
else
  # The end-to-end form of the statement-order bug fixed on 08-09: before that
  # fix, THIS is the request that died on 23505.
  S=$(req POST /v1/snapshots "$(body_upload "$ROBOT" "$(envelope Smoky-v2)")" "$AUTH")
  expect "a second snapshot uploads for the same robot" "$S" 200
  SNAP2=$(jget id)
  req POST /v1/worker/jobs/claim '{"workerId":"smoke-1"}' "X-Worker-Key: $WKEY" >/dev/null; JOB2=$(jget id)
  if [ -z "$SNAP2" ] || [ -z "$JOB2" ]; then skip "supersede (2 checks)" "second upload or claim produced no id"; else
    VR2="{\"snapshotId\":\"$SNAP2\",\"workerId\":\"smoke-1\",\"legal\":true,\"massKg\":13,
          \"aabbX\":0.5,\"aabbY\":0.3,\"aabbZ\":0.6,\"category\":\"LIGHT\",
          \"partsManifest\":[\"chassis_a\"],\"programHash\":\"\",\"failReasons\":[]}"
    S=$(req POST "/v1/worker/jobs/$JOB2/validate-result" "$VR2" "X-Worker-Key: $WKEY")
    expect "validating it does NOT collide with the incumbent" "$S" 200
    req GET "/v1/snapshots/$SNAP2" "" "$AUTH" >/dev/null; NEW=$(jget status)
    req GET "/v1/snapshots/$SNAP1" "" "$AUTH" >/dev/null; OLD=$(jget status)
    if [ "$NEW" = "ACTIVE" ] && [ "$OLD" = "SUPERSEDED" ]; then
      ok "the new snapshot is ACTIVE and the old one SUPERSEDED"
    else
      no "supersede did not happen: new '$NEW', old '$OLD'"
    fi
  fi
fi

echo
echo "== I. the privacy boundary (§1.3) =="
if [ -z "$SNAP2" ]; then
  skip "the whole privacy boundary (7 checks)" "no ACTIVE snapshot to scout"
else
  S=$(req GET "/v1/snapshots/$SNAP2")
  expect "an anonymous scout can read the scouting card" "$S" 200
  if [ "$S" != "200" ]; then
    skip "payload-leak checks (3)" "the scouting card did not return 200; an empty body proves nothing"
  else
    SCOUT=$(cat "$BODY")
    grep -q 'storage_url\|storageUrl\|file://\|gs://' <<<"$SCOUT" \
      && no "the payload URL is in the public response -- that is the build, handed over" \
      || ok "the payload URL is NOT in the response, for anyone"
    grep -q '"payload"' <<<"$SCOUT" \
      && no "the payload itself is in the public response" || ok "the payload itself is not there either"
    M=$(jget mine)
    { [ "$M" = "False" ] || [ "$M" = "false" ]; } && ok "…and it is not marked as the scout's own" \
      || no "mine='$M' for an anonymous reader"
  fi

  # A rejected upload's reasons are the validator describing the build. The
  # owner may read them; a scout may not.
  S=$(req POST /v1/snapshots "$(body_upload "$ROBOT" "$(envelope Smoky-bad)")" "$AUTH")
  SNAP3=$(jget id)
  req POST /v1/worker/jobs/claim '{"workerId":"smoke-1"}' "X-Worker-Key: $WKEY" >/dev/null; JOB3=$(jget id)
  if [ -z "$SNAP3" ] || [ -z "$JOB3" ]; then
    skip "fail-reason visibility (6 checks)" "could not upload or claim the rejected snapshot"
  else
    VR3="{\"snapshotId\":\"$SNAP3\",\"workerId\":\"smoke-1\",\"legal\":false,\"massKg\":99,
          \"aabbX\":9.0,\"aabbY\":9.0,\"aabbZ\":9.0,\"category\":null,
          \"partsManifest\":[],\"programHash\":null,\"failReasons\":[\"overweight\",\"unanchored part\"]}"
    req POST "/v1/worker/jobs/$JOB3/validate-result" "$VR3" "X-Worker-Key: $WKEY" >/dev/null
    S=$(req GET "/v1/snapshots/$SNAP3" "" "$AUTH")
    { [ "$S" = "200" ] && grep -q 'overweight' "$BODY"; } \
      && ok "the owner is told why their own upload was rejected" \
      || no "the owner cannot see their own fail reasons (HTTP $S)"
    S=$(req GET "/v1/snapshots/$SNAP3")
    if [ "$S" != "200" ]; then no "the scouting card for a rejected snapshot did not load (HTTP $S)"
    elif grep -q 'overweight' "$BODY"; then no "a scout can read the validator's reasons -- that describes the build"
    else ok "a scout cannot read the fail reasons"; fi
    req GET "/v1/snapshots/$SNAP2" "" "$AUTH" >/dev/null
    is "a REJECTED upload left the incumbent ACTIVE" "$(jget status)" ACTIVE

    # ── GET /v1/robots has to tell REJECTED from PENDING ────────────────
    # It used to join ACTIVE only, so a rejected robot came back with
    # activeSnapshotId = NULL and nothing else — byte-identical to a robot
    # still in the queue. The dock could only say "waiting to be checked",
    # and said it forever. That was a missing COLUMN, not a missing label,
    # which is why no client-side change could have fixed it.
    #
    # Pull the field off THIS robot's row by id: index 0 is right today and
    # wrong the first time someone adds a robot to this section.
    rfield() { "$PY" -c "import json
rows=json.load(open('$BODY'))
m=[x for x in rows if str(x.get('id'))=='$1']
v=m[0].get('$2') if m else None
print('' if v is None else (' '.join(v) if isinstance(v,list) else v))" 2>/dev/null; }

    S=$(req GET /v1/robots "" "$AUTH")
    if [ "$S" != "200" ]; then no "the robots list did not load (HTTP $S)"
    else
      # ⚠ THE ACTIVE CASE IS ASSERTED FIRST, DELIBERATELY. POST /v1/challenges
      # resolves its challenger through activeSnapshotId, so widening this
      # projection to reach the LATEST snapshot must not change which snapshot
      # comes back here. If this line ever fails, the rejected-case checks
      # below are meaningless — a challenger would be offered a PENDING or
      # REJECTED build.
      same "the challenge path still resolves the ACTIVE snapshot" \
           "$(rfield "$ROBOT" activeSnapshotId)" "$SNAP2"
      is "a rejected robot reports its LATEST snapshot status" \
         "$(rfield "$ROBOT" snapshotStatus)" REJECTED
      case "$(rfield "$ROBOT" failReasons)" in
        *overweight*"unanchored part"*)
          ok "the owner's robots list carries EVERY fail reason, in the validator's order" ;;
        *) no "the robots list lost the fail reasons -- got '$(rfield "$ROBOT" failReasons)'" ;;
      esac
    fi
  fi
fi

echo
echo "== J. the category contract, pinned from the server side (§5.2) =="
# 2026-08-09 (relay 003). snapshots.category is
#   TEXT CHECK (category IS NULL OR category IN ('FEATHER','LIGHT','MIDDLE','HEAVY','SUPER'))
# and a robot with no weight category is a REJECTION, not "unrated" —
# ratings.category is NOT NULL. So the API has exactly four cases to honour,
# and the third is the one that bites: Unity's JsonUtility serialises a null
# string as "", so "" is precisely what a worker written the obvious way
# sends, and it fails INSIDE the UPDATE, after the job has done all its work.
#
# These assert the CONTRACT, not today's behaviour. A refusal that arrives as
# an unhandled 23514 is still a refusal, but it is a 500 with a stack trace
# where a 400 belongs — if that is what happens, this section fails loudly and
# names the bug rather than being loosened to accommodate it.
if [ -z "${ROBOT:-}" ] || [ -z "${AUTH:-}" ]; then
  skip "the category contract (6 checks)" "no robot or token -- an earlier section did not complete"
else
  S=$(vr_category '"FEATHER"'); VRC_SNAP=$(cat "$VRC_SNAP_FILE" 2>/dev/null)
  expect "a valid category is accepted" "$S" 200
  if [ -n "$VRC_SNAP" ]; then
    is "…and stored verbatim" "$(dbq "SELECT category FROM snapshots WHERE id='$VRC_SNAP';")" FEATHER
  else skip "…and stored verbatim" "no snapshot from the FEATHER case"; fi

  # A validated robot must JOIN THE LADDER. Ratings rows used to be created
  # only by the first fight, but the leaderboard reads `ratings` and the
  # leaderboard is how one player discovers another — so every enlisted robot
  # was invisible to everyone but its owner, nobody could issue the first
  # challenge, and the ladder could never start. League nights could not break
  # the tie either; they draw from the top 8 of `ratings`.
  if [ -n "$VRC_SNAP" ]; then
    is "a validated robot gets a placement rating, so the ladder can start" \
       "$(dbq "SELECT count(*) FROM ratings ra JOIN snapshots s ON s.robot_id = ra.robot_id
                WHERE s.id='$VRC_SNAP' AND ra.category='FEATHER';")" 1
    is "…and it is provisional: RD 350, the placement default" \
       "$(dbq "SELECT round(ra.deviation)::int FROM ratings ra JOIN snapshots s ON s.robot_id = ra.robot_id
                WHERE s.id='$VRC_SNAP' AND ra.category='FEATHER';")" 350
    # The point of the placement row is DISCOVERABILITY, so assert the
    # leaderboard's own join rather than counting rows a second time.
    is "…so it is now visible on the board to OTHER players" \
       "$(dbq "SELECT count(*) FROM ratings ra
                 JOIN robots r ON r.id = ra.robot_id
                 JOIN snapshots s ON s.robot_id = r.id AND s.status = 'ACTIVE'
                WHERE s.id='$VRC_SNAP' AND ra.category = 'FEATHER';")" 1
    # The property that matters more than creation: revalidating must not
    # reset a rating that has been earned. A player re-uploading a tweaked
    # build would otherwise be handed back to 1200 every time.
    dbq "UPDATE ratings SET rating = 1650 WHERE robot_id =
           (SELECT robot_id FROM snapshots WHERE id='$VRC_SNAP') AND category='FEATHER';" >/dev/null
    S2=$(vr_category '"FEATHER"'); VRC_SNAP2=$(cat "$VRC_SNAP_FILE" 2>/dev/null)
    is "revalidating does NOT reset an earned rating" \
       "$(dbq "SELECT round(rating)::int FROM ratings WHERE robot_id =
                 (SELECT robot_id FROM snapshots WHERE id='$VRC_SNAP') AND category='FEATHER';")" 1650
  else
    skip "the placement rating (4 checks)" "no snapshot from the FEATHER case"
  fi

  S=$(vr_category 'null' false); VRC_SNAP=$(cat "$VRC_SNAP_FILE" 2>/dev/null)
  expect "an illegal robot may report no category" "$S" 200
  if [ -n "$VRC_SNAP" ]; then
    # -qtA prints SQL NULL as the empty string, so count the NULLs instead of
    # comparing text — otherwise NULL and '' are indistinguishable here, which
    # is the exact confusion this section exists to rule out.
    is "…and it is stored as SQL NULL, not the empty string" \
       "$(dbq "SELECT count(*) FROM snapshots WHERE id='$VRC_SNAP' AND category IS NULL;")" 1
  else skip "…and it is stored as SQL NULL" "no snapshot from the null case"; fi

  # The two that must be refused. 4xx is the contract: the worker sent
  # something the schema forbids and deserves to be told so.
  S=$(vr_category '""' false); EMPTY_JOB=$(cat "$VRC_JOB_FILE" 2>/dev/null)
  case "$S" in
    4??) ok "the empty-string category is refused cleanly (HTTP $S)" ;;
    5??) no "the empty-string category is refused as a SERVER error (HTTP $S) -- the CHECK fires inside the UPDATE and nothing catches it; a worker's verdict is lost to a stack trace" ;;
    *)   no "the empty-string category was ACCEPTED (HTTP $S) -- snapshots.category's CHECK should have forbidden it" ;;
  esac

  # THE LIVELOCK CHECK. Refusing the result is only half the fix. Before
  # 2026-08-09 the job was never completed either, so the reaper returned it
  # to the queue on the visibility timeout and the next worker failed on it
  # identically — forever. A refused result must RETIRE the job, and it must
  # go to FAILED rather than READY: the payload is deterministic, so a retry
  # buys nothing. Asserting "not READY" alone would pass while the job sat
  # CLAIMED and leaked a worker slot a slower way.
  if [ -z "$EMPTY_JOB" ]; then
    skip "…and the job is retired, not left to spin (2 checks)" "no job id from the empty-string case"
  else
    is "…and the job is marked FAILED, so the reaper cannot hand it out again" \
       "$(dbq "SELECT status FROM match_jobs WHERE id=$EMPTY_JOB;")" FAILED
    if [ -n "$(dbq "SELECT last_error FROM match_jobs WHERE id=$EMPTY_JOB;")" ]; then
      ok "…carrying a last_error a human can act on"
    else
      no "the job failed with an empty last_error -- nothing says why, so it cannot be triaged"
    fi
  fi

  S=$(vr_category '"BANTAM"')
  case "$S" in
    4??) ok "an unknown category name is refused cleanly (HTTP $S)" ;;
    5??) no "an unknown category name is refused as a SERVER error (HTTP $S) -- same unhandled 23514 as the empty string" ;;
    *)   no "an unknown category name was ACCEPTED (HTTP $S) -- the CHECK constraint is not doing its job" ;;
  esac

  # legal:true with no category is nonsense: ratings.category is NOT NULL, so
  # such a snapshot can never be placed on a ladder. The Cowork worker cannot
  # produce it, but the API should not depend on being the only client.
  S=$(vr_category 'null' true)
  case "$S" in
    4??) ok "a legal robot with no category is refused (HTTP $S)" ;;
    *)   no "a legal robot with NO category was accepted (HTTP $S) -- ratings.category is NOT NULL, so this snapshot can never be rated; it is unplaceable the moment it is stored" ;;
  esac
fi

echo
echo "== K. the fight lifecycle, walked end to end (§5.3) =="
# 2026-08-09. §5.2's warning, taken literally: api_smoke sat at 37/37 green
# while the claim response was unusable, because every endpoint answered
# correctly in isolation and no consumer had ever COMPLETED a job. So this
# section does not test endpoints - it walks the loop, and every assertion
# is about state the previous step left behind.
#
# challenge -> claim -> upload replay -> post result -> settle -> job DONE
if [ -z "${ROBOT:-}" ] || [ -z "${AUTH:-}" ] || ! command -v psql >/dev/null 2>&1; then
  skip "the fight lifecycle (14 checks)" "needs a robot, a token and psql"
else
  CHSNAP=$(dbq "SELECT id FROM snapshots WHERE robot_id='$ROBOT' AND status='ACTIVE';")
  CHCAT=$(dbq "SELECT category FROM snapshots WHERE robot_id='$ROBOT' AND status='ACTIVE';")

  # A defender must belong to SOMEONE ELSE, so the section makes its own
  # account rather than borrowing one - a challenge against your own robot is
  # a different test (below).
  DEMAIL="def-$STAMP@example.com"
  S=$(req POST /v1/auth/register "{\"email\":\"$DEMAIL\",\"password\":\"$PW\",\"displayName\":\"Defender\"}")
  DTOK=$(jget token); DUSER=$(jget userId)
  if [ -z "$DTOK" ] || [ -z "$CHSNAP" ]; then
    skip "the fight lifecycle (14 checks)" "no defender token (HTTP $S) or no ACTIVE challenger snapshot"
  else
    DAUTH="Authorization: Bearer $DTOK"
    req POST /v1/robots '{"name":"Defiant"}' "$DAUTH" >/dev/null; DROBOT=$(jget id)
    req POST /v1/snapshots "$(body_upload "$DROBOT" "$(envelope Defiant)")" "$DAUTH" >/dev/null
    DSNAP=$(jget id)
    req POST /v1/worker/jobs/claim '{"workerId":"smoke-fight"}' "X-Worker-Key: $WKEY" >/dev/null
    DJOB=$(jget id)
    # Same category as the challenger => gap 0, so the arithmetic below is the
    # base case and a wrong gap shows up as a wrong stake rather than hiding.
    req POST "/v1/worker/jobs/$DJOB/validate-result" \
      "{\"snapshotId\":\"$DSNAP\",\"workerId\":\"smoke-fight\",\"legal\":true,\"massKg\":10,\"aabbX\":0.4,\"aabbY\":0.3,\"aabbZ\":0.5,\"category\":\"$CHCAT\",\"partsManifest\":[\"chassis_a\"],\"programHash\":\"\",\"failReasons\":[]}" \
      "X-Worker-Key: $WKEY" >/dev/null
    is "a defender snapshot is ACTIVE and ready to be challenged" \
       "$(dbq "SELECT status FROM snapshots WHERE id='$DSNAP';")" ACTIVE

    BAL0=$(dbq "SELECT COALESCE(SUM(delta),0) FROM ledger WHERE user_id='$USERID';")

    # --- step 1: the challenge -------------------------------------------
    S=$(req POST /v1/challenges "{\"challengerSnapshotId\":\"$CHSNAP\",\"defenderSnapshotId\":\"$DSNAP\"}" "$AUTH")
    expect "a challenge is accepted" "$S" 200
    MATCH=$(jget matchId); STAKE=$(jget stake)
    is "…priced at the base stake for a same-category fight" "$STAKE" 50
    is "…and the match is QUEUED" "$(jget status)" QUEUED

    if [ -z "$MATCH" ]; then
      skip "the rest of the lifecycle (9 checks)" "the challenge returned no matchId"
    else
      # The escrow debit and the match row are one transaction. Checking the
      # balance moved is what proves the debit happened at all - the endpoint
      # would answer 200 either way.
      BAL1=$(dbq "SELECT COALESCE(SUM(delta),0) FROM ledger WHERE user_id='$USERID';")
      is "…and the stake left the wallet into escrow" "$BAL1" "$((BAL0 - 50))"
      is "…recorded as a STAKE row against this match" \
         "$(dbq "SELECT reason FROM ledger WHERE match_id='$MATCH' AND user_id='$USERID';")" STAKE

      # --- step 2: the worker claims the FIGHT job ------------------------
      S=$(req POST /v1/worker/jobs/claim '{"workerId":"smoke-fight"}' "X-Worker-Key: $WKEY")
      FJOB=$(jget id)
      is "a worker claims the FIGHT job the challenge enqueued" "$(jget kind)" FIGHT
      # The lesson from the VALIDATE contract: a claim that does not resolve to
      # bytes cannot be worked, and the suite was green while that was true.
      CU=$(jget challengerUrl); DU=$(jget defenderUrl)
      if [ -n "$CU" ] && [ -n "$DU" ]; then
        ok "…carrying BOTH robots' payload locations, so the fight can be run"
      else
        no "a claimed FIGHT job is missing a payload url (challenger '$CU', defender '$DU') — unworkable"
      fi

      # --- step 3: upload the replay -------------------------------------
      S=$(req POST "/v1/worker/matches/$MATCH/replay" '{"replay":"{\"bouts\":3,\"recording\":\"opaque\"}"}' "X-Worker-Key: $WKEY")
      expect "the worker uploads a replay" "$S" 200
      RURL=$(jget url)

      # --- step 4: post the result ---------------------------------------
      if [ -z "$FJOB" ] || [ -z "$RURL" ]; then
        skip "fight-result and settlement (6 checks)" "no fight job or replay url"
      else
        S=$(req POST "/v1/worker/jobs/$FJOB/fight-result" \
            "{\"matchId\":\"$MATCH\",\"workerId\":\"smoke-fight\",\"verdict\":\"CHALLENGER\",\"replayUrls\":[\"$RURL\"],\"bouts\":[\"C\",\"D\",\"C\"]}" \
            "X-Worker-Key: $WKEY")
        expect "the worker posts the verdict" "$S" 200
        is "…the match is COMPLETE" "$(dbq "SELECT status FROM matches WHERE id='$MATCH';")" COMPLETE
        is "…with the verdict recorded" "$(dbq "SELECT verdict FROM matches WHERE id='$MATCH';")" CHALLENGER
        is "…and the replay attached" \
           "$(dbq "SELECT CASE WHEN array_length(replay_urls,1) >= 1 THEN 'yes' ELSE 'no' END FROM matches WHERE id='$MATCH';")" yes
        # Scrap must be conserved: the stake came back and the purse was paid.
        is "…the winner's wallet is stake-refunded and paid the purse" \
           "$(dbq "SELECT COALESCE(SUM(delta),0) FROM ledger WHERE user_id='$USERID';")" "$((BAL0 + 100))"
        is "…and the FIGHT job is retired, not left to spin" \
           "$(dbq "SELECT status FROM match_jobs WHERE id=$FJOB;")" DONE

        # §2.1: the ladder must actually MOVE. Before 2026-08-09 a fight
        # settled scrap and nobody's rank changed - matches.rating_deltas was
        # NULL and `ratings` had never held a row.
        CHROBOT=$(dbq "SELECT r.id FROM robots r JOIN snapshots s ON s.robot_id=r.id WHERE s.id='$CHSNAP';")
        DFROBOT=$(dbq "SELECT r.id FROM robots r JOIN snapshots s ON s.robot_id=r.id WHERE s.id='$DSNAP';")
        CHRATE=$(dbq "SELECT round(rating) FROM ratings WHERE robot_id='$CHROBOT';")
        DFRATE=$(dbq "SELECT round(rating) FROM ratings WHERE robot_id='$DFROBOT';")
        if [ -z "$CHRATE" ] || [ -z "$DFRATE" ]; then
          no "a settled match left no rating rows -- the ladder did not move (challenger '$CHRATE', defender '$DFRATE')"
        elif [ "$CHRATE" -gt 1200 ] && [ "$DFRATE" -lt 1200 ]; then
          ok "…the winner climbed above the 1200 placement and the loser fell below ($CHRATE vs $DFRATE)"
        else
          no "same-category ratings moved the wrong way: winner $CHRATE, loser $DFRATE (placement is 1200)"
        fi
        # Deviation must SHRINK once a robot has actually fought: that is the
        # whole reason §2.1 chose Glicko-2 over Elo.
        # Filter by category: ratings are per (robot, category, season), so an
        # unfiltered read returns one row per class the robot is placed in and
        # -qtA concatenates them. This read "315350" the moment robots started
        # getting a placement row at validation.
        CHRD=$(dbq "SELECT round(deviation) FROM ratings WHERE robot_id='$CHROBOT' AND category='$CHCAT';")
        if [ -n "$CHRD" ] && [ "$CHRD" -lt 350 ]; then
          ok "…and the winner's deviation tightened from the 350 placement to $CHRD"
        else
          no "deviation did not tighten after a fight (got '$CHRD', placement 350)"
        fi
        is "…with the deltas recorded on the match for the fight report" \
           "$(dbq "SELECT CASE WHEN rating_deltas IS NOT NULL THEN 'yes' ELSE 'no' END FROM matches WHERE id='$MATCH';")" yes

        # Settling twice mints scrap from nothing. The job is no longer
        # CLAIMED, so ownership refuses it before the money moves.
        S=$(req POST "/v1/worker/jobs/$FJOB/fight-result" \
            "{\"matchId\":\"$MATCH\",\"workerId\":\"smoke-fight\",\"verdict\":\"CHALLENGER\",\"replayUrls\":[\"$RURL\"],\"bouts\":[]}" \
            "X-Worker-Key: $WKEY")
        expect "the same result cannot be settled twice" "$S" 409
        is "…and the wallet did not move on the second attempt" \
           "$(dbq "SELECT COALESCE(SUM(delta),0) FROM ledger WHERE user_id='$USERID';")" "$((BAL0 + 100))"
      fi
    fi

    S=$(req POST /v1/challenges "{\"challengerSnapshotId\":\"$CHSNAP\",\"defenderSnapshotId\":\"$CHSNAP\"}" "$AUTH")
    expect "a robot cannot challenge itself" "$S" 400

    # ---- gap pricing, the squared purse, and punching down --------------
    # Everything above ran at gap 0, where 50*(1+gap) and 100*(1+0.5*gap)^2
    # both collapse to their base. A swept variable producing no variance has
    # usually not been swept: at gap 0 a wrong formula is indistinguishable
    # from a right one. So this block fights the heaviest class there is.
    cat_idx() { case "$1" in FEATHER) echo 0;; LIGHT) echo 1;; MIDDLE) echo 2;; HEAVY) echo 3;; SUPER) echo 4;; *) echo -1;; esac; }
    GAP=$(( $(cat_idx SUPER) - $(cat_idx "$CHCAT") ))
    WANT_STAKE=$(( 50 * (1 + GAP) ))
    # (1 + 0.5*gap)^2 in integer arithmetic: 100*(2+gap)^2/4
    WANT_PURSE=$(( 100 * (2 + GAP) * (2 + GAP) / 4 ))

    req POST /v1/robots '{"name":"Colossus"}' "$DAUTH" >/dev/null; HROBOT=$(jget id)
    req POST /v1/snapshots "$(body_upload "$HROBOT" "$(envelope Colossus)")" "$DAUTH" >/dev/null
    HSNAP=$(jget id)
    req POST /v1/worker/jobs/claim '{"workerId":"smoke-fight"}' "X-Worker-Key: $WKEY" >/dev/null
    HJOB=$(jget id)
    req POST "/v1/worker/jobs/$HJOB/validate-result" \
      "{\"snapshotId\":\"$HSNAP\",\"workerId\":\"smoke-fight\",\"legal\":true,\"massKg\":1400,\"aabbX\":2,\"aabbY\":2,\"aabbZ\":2,\"category\":\"SUPER\",\"partsManifest\":[\"chassis_a\"],\"programHash\":\"\",\"failReasons\":[]}" \
      "X-Worker-Key: $WKEY" >/dev/null

    if [ "$GAP" -le 0 ] || [ "$(dbq "SELECT status FROM snapshots WHERE id='$HSNAP';")" != "ACTIVE" ]; then
      skip "gap pricing and the squared purse (5 checks)" "no SUPER defender, or the challenger is already SUPER (gap $GAP)"
    else
      BAL2=$(dbq "SELECT COALESCE(SUM(delta),0) FROM ledger WHERE user_id='$USERID';")
      S=$(req POST /v1/challenges "{\"challengerSnapshotId\":\"$CHSNAP\",\"defenderSnapshotId\":\"$HSNAP\"}" "$AUTH")
      expect "a $CHCAT may punch up to a SUPER" "$S" 200
      is "…at a gap of $GAP" "$(jget gap)" "$GAP"
      is "…staking 50 x (1 + gap)" "$(jget stake)" "$WANT_STAKE"
      BIGMATCH=$(jget matchId)
      req POST /v1/worker/jobs/claim '{"workerId":"smoke-fight"}' "X-Worker-Key: $WKEY" >/dev/null
      BIGJOB=$(jget id)
      req POST "/v1/worker/jobs/$BIGJOB/fight-result" \
        "{\"matchId\":\"$BIGMATCH\",\"workerId\":\"smoke-fight\",\"verdict\":\"CHALLENGER\",\"replayUrls\":[\"file:///dev/null\"],\"bouts\":[]}" \
        "X-Worker-Key: $WKEY" >/dev/null
      # Won it: stake back plus a purse that grows with the SQUARE of the gap.
      is "…and winning pays 100 x (1 + 0.5 x gap)^2 = $WANT_PURSE" \
         "$(dbq "SELECT COALESCE(SUM(delta),0) FROM ledger WHERE user_id='$USERID';")" "$((BAL2 + WANT_PURSE))"
      # §2.1's anti-farming rule, and the one most likely to be got wrong:
      # "The defender's rating is untouched by cross-category fights."
      # A heavy must not be able to farm rating by squashing lightweights, nor
      # lose its rank to a swarm of speculative punch-ups. The challenger IS
      # rated (it just beat someone $GAP classes up); the defender is not.
      HROBOT=$(dbq "SELECT r.id FROM robots r JOIN snapshots s ON s.robot_id=r.id WHERE s.id='$HSNAP';")
      # Two distinct claims, because "count the defender's rating rows" stopped
      # being a valid proxy once every validated robot got a placement row —
      # and that proxy would ALSO have passed if ratings were never created at
      # all, which is the vacuous-pass this project keeps re-learning.
      HRATE=$(dbq "SELECT count(*) FROM ratings WHERE robot_id='$HROBOT' AND category='$CHCAT';")
      is "a punch-up creates NO rating for the defender in the challenger's class" "$HRATE" 0
      # And its own-class rating is untouched: still sitting at the placement
      # values, because a cross-category fight must not move it.
      HOWN=$(dbq "SELECT round(rating)::int || '/' || round(deviation)::int FROM ratings
                   WHERE robot_id='$HROBOT' AND category='SUPER';")
      is "…and the defender's own-class rating is untouched by the punch-up" "$HOWN" "1200/350"
      CHSUPER=$(dbq "SELECT count(*) FROM ratings WHERE robot_id='$CHROBOT' AND category='SUPER';")
      is "…while the challenger IS rated, in the class it reached up into" "$CHSUPER" 1
      is "…and the match records that the defender was deliberately not rated" \
         "$(dbq "SELECT rating_deltas->>'defenderUnrated' FROM matches WHERE id='$BIGMATCH';")" true

      # §1.2, the rule that actually protects the ladder.
      S=$(req POST /v1/challenges "{\"challengerSnapshotId\":\"$HSNAP\",\"defenderSnapshotId\":\"$CHSNAP\"}" "$DAUTH")
      expect "a SUPER cannot punch DOWN to a $CHCAT" "$S" 400
    fi

    # ---- the defender-wins branch ---------------------------------------
    # Untested above: every settlement so far went to the challenger, so the
    # DEFENSE purse and the stake-forfeit path had never run.
    DBAL0=$(dbq "SELECT COALESCE(SUM(delta),0) FROM ledger WHERE user_id='$DUSER';")
    CBAL0=$(dbq "SELECT COALESCE(SUM(delta),0) FROM ledger WHERE user_id='$USERID';")
    S=$(req POST /v1/challenges "{\"challengerSnapshotId\":\"$CHSNAP\",\"defenderSnapshotId\":\"$DSNAP\"}" "$AUTH")
    LOSTMATCH=$(jget matchId)
    if [ "$S" != "200" ] || [ -z "$LOSTMATCH" ]; then
      skip "the defender-wins branch (3 checks)" "second challenge did not open (HTTP $S)"
    else
      req POST /v1/worker/jobs/claim '{"workerId":"smoke-fight"}' "X-Worker-Key: $WKEY" >/dev/null
      LJOB=$(jget id)
      S=$(req POST "/v1/worker/jobs/$LJOB/fight-result" \
          "{\"matchId\":\"$LOSTMATCH\",\"workerId\":\"smoke-fight\",\"verdict\":\"DEFENDER\",\"replayUrls\":[\"file:///dev/null\"],\"bouts\":[]}" \
          "X-Worker-Key: $WKEY")
      expect "a defender win settles" "$S" 200
      is "…paying the defender the flat 40-scrap defense purse" \
         "$(dbq "SELECT COALESCE(SUM(delta),0) FROM ledger WHERE user_id='$DUSER';")" "$((DBAL0 + 40))"
      is "…and the challenger's stake is forfeit, not refunded" \
         "$(dbq "SELECT COALESCE(SUM(delta),0) FROM ledger WHERE user_id='$USERID';")" "$((CBAL0 - 50))"
    fi
  fi
fi

echo
echo "== L. defender protection (§2.2) =="
# 2026-08-09. Glicko-2 made ratings worth attacking; these are the three rules
# that price the attacks. Each is seeded through the DATABASE rather than by
# driving the API ten times: the rule is what is under test, not the
# accumulation, and ten real challenges would exhaust the 20/min upload
# limiter and turn a rule failure into a 429 nobody can read.
if [ -z "${CHSNAP:-}" ] || [ -z "${DSNAP:-}" ] || ! command -v psql >/dev/null 2>&1; then
  skip "defender protection (8 checks)" "section K did not leave two ACTIVE snapshots"
else
  LCHROBOT=$(dbq "SELECT robot_id FROM snapshots WHERE id='$CHSNAP';")
  LDFROBOT=$(dbq "SELECT robot_id FROM snapshots WHERE id='$DSNAP';")

  # ---- challenge tickets ---------------------------------------------
  CAP=$(dbq "SELECT value::int FROM ladder_config WHERE key='challenge_tickets_per_day';")
  is "the ticket cap is configured" "$CAP" 10
  dbq "INSERT INTO tickets (robot_id, day, used) VALUES ('$LCHROBOT', CURRENT_DATE, $CAP)
       ON CONFLICT (robot_id, day) DO UPDATE SET used = $CAP;" >/dev/null
  S=$(req POST /v1/challenges "{\"challengerSnapshotId\":\"$CHSNAP\",\"defenderSnapshotId\":\"$DSNAP\"}" "$AUTH")
  expect "a robot out of tickets cannot initiate another challenge" "$S" 429
  # Defending must stay free: the cap is on INITIATING. Prove the same robot
  # can still be challenged BY someone else while its own tickets are spent.
  dbq "INSERT INTO tickets (robot_id, day, used) VALUES ('$LDFROBOT', CURRENT_DATE, 0)
       ON CONFLICT (robot_id, day) DO UPDATE SET used = 0;" >/dev/null
  S=$(req POST /v1/challenges "{\"challengerSnapshotId\":\"$DSNAP\",\"defenderSnapshotId\":\"$CHSNAP\"}" "$DAUTH")
  expect "…but defending is unlimited and free (they can still be challenged)" "$S" 200
  FREEMATCH=$(jget matchId)
  # Retire that job so it does not sit READY and confuse a later claim.
  if [ -n "$FREEMATCH" ]; then
    req POST /v1/worker/jobs/claim '{"workerId":"smoke-prot"}' "X-Worker-Key: $WKEY" >/dev/null
    FJ=$(jget id)
    req POST "/v1/worker/jobs/$FJ/fight-result" \
      "{\"matchId\":\"$FREEMATCH\",\"workerId\":\"smoke-prot\",\"verdict\":\"DRAW\",\"replayUrls\":[],\"bouts\":[]}" \
      "X-Worker-Key: $WKEY" >/dev/null
  fi
  dbq "UPDATE tickets SET used = 0 WHERE robot_id='$LCHROBOT' AND day=CURRENT_DATE;" >/dev/null

  # ---- repeat-opponent taper -----------------------------------------
  # Seed three prior wins by this exact pair inside the 24h window. The taper
  # counts COMPLETE matches, so these are indistinguishable from real ones.
  TAP=$(dbq "SELECT value::int FROM ladder_config WHERE key='repeat_win_taper_after';")
  for i in 1 2 3; do
    dbq "INSERT INTO matches (challenger_snapshot_id, defender_snapshot_id, category, gap, arena, seeds, status, verdict, completed_at)
         VALUES ('$CHSNAP','$DSNAP','$CHCAT',0,'synthetic','{1,2,3}','COMPLETE','CHALLENGER', now());" >/dev/null
  done
  PRIOR=$(dbq "SELECT count(*) FROM matches m JOIN snapshots sc ON sc.id=m.challenger_snapshot_id JOIN snapshots sd ON sd.id=m.defender_snapshot_id WHERE m.verdict='CHALLENGER' AND m.status='COMPLETE' AND sc.robot_id='$LCHROBOT' AND sd.robot_id='$LDFROBOT' AND m.completed_at > now() - interval '24 hours';")
  note "prior wins by this pair in the window: $PRIOR (taper after $TAP)"

  WBEFORE=$(dbq "SELECT COALESCE(SUM(delta),0) FROM ledger WHERE user_id='$USERID';")
  RBEFORE=$(dbq "SELECT round(rating) FROM ratings WHERE robot_id='$LCHROBOT' AND category='$CHCAT';")
  S=$(req POST /v1/challenges "{\"challengerSnapshotId\":\"$CHSNAP\",\"defenderSnapshotId\":\"$DSNAP\"}" "$AUTH")
  TMATCH=$(jget matchId)
  if [ "$S" != "200" ] || [ -z "$TMATCH" ]; then
    skip "the repeat-opponent taper (4 checks)" "the tapered challenge did not open (HTTP $S)"
  else
    req POST /v1/worker/jobs/claim '{"workerId":"smoke-prot"}' "X-Worker-Key: $WKEY" >/dev/null
    TJOB=$(jget id)
    req POST "/v1/worker/jobs/$TJOB/fight-result" \
      "{\"matchId\":\"$TMATCH\",\"workerId\":\"smoke-prot\",\"verdict\":\"CHALLENGER\",\"replayUrls\":[],\"bouts\":[]}" \
      "X-Worker-Key: $WKEY" >/dev/null
    is "a 4th win vs the same opponent is flagged tapered" \
       "$(dbq "SELECT rating_deltas->>'tapered' FROM matches WHERE id='$TMATCH';")" true
    # The stake still comes back — the taper zeroes GAINS, not the escrow.
    is "…the stake is still refunded, so the wallet is exactly level" \
       "$(dbq "SELECT COALESCE(SUM(delta),0) FROM ledger WHERE user_id='$USERID';")" "$WBEFORE"
    is "…no PURSE row was written for it" \
       "$(dbq "SELECT count(*) FROM ledger WHERE match_id='$TMATCH' AND reason='PURSE';")" 0
    is "…and the winner's rating did not move" \
       "$(dbq "SELECT round(rating) FROM ratings WHERE robot_id='$LCHROBOT' AND category='$CHCAT';")" "$RBEFORE"
  fi

  # ---- defense loss floor --------------------------------------------
  # Seed a day's worth of defending that already cost 70 of the 75 allowance,
  # then take one more loss and prove the drop is clamped to the remaining 5.
  # Remove the taper fixtures, and strip the defender key from every REAL
  # match this robot already defended today. Without that reset the allowance
  # is already spent by section K's fights, the clamp lands on 0, and the
  # check below passes without proving anything - which is exactly what the
  # first version of it did.
  dbq "DELETE FROM matches WHERE arena='synthetic';" >/dev/null
  dbq "UPDATE matches SET rating_deltas = rating_deltas - 'defender'
        WHERE defender_snapshot_id='$DSNAP' AND rating_deltas ? 'defender';" >/dev/null
  FLOOR=$(dbq "SELECT value::int FROM ladder_config WHERE key='defense_daily_floor';")
  SPENT=70
  dbq "INSERT INTO matches (challenger_snapshot_id, defender_snapshot_id, category, gap, arena, seeds, status, verdict, completed_at, rating_deltas)
       VALUES ('$CHSNAP','$DSNAP','$CHCAT',0,'synthetic','{1,2,3}','COMPLETE','CHALLENGER', now(),
               '{\"defender\":{\"before\":1300,\"after\":1230}}'::jsonb);" >/dev/null
  ALLOWED=$(( FLOOR - SPENT ))
  # An ESTABLISHED rank: deviation below the provisional threshold. This is
  # what the floor exists to protect. The provisional case is checked below,
  # and it must behave the OPPOSITE way.
  PROVAT=$(dbq "SELECT value::int FROM ladder_config WHERE key='floor_applies_below_deviation';")
  is "the provisional threshold is configured" "$PROVAT" 200
  dbq "UPDATE ratings SET rating = 1300, deviation = 120 WHERE robot_id='$LDFROBOT' AND category='$CHCAT';" >/dev/null
  DBEFORE=$(dbq "SELECT round(rating) FROM ratings WHERE robot_id='$LDFROBOT' AND category='$CHCAT';")
  S=$(req POST /v1/challenges "{\"challengerSnapshotId\":\"$CHSNAP\",\"defenderSnapshotId\":\"$DSNAP\"}" "$AUTH")
  FMATCH=$(jget matchId)
  if [ "$S" != "200" ] || [ -z "$FMATCH" ]; then
    skip "the defense loss floor (2 checks)" "the floor challenge did not open (HTTP $S)"
  else
    req POST /v1/worker/jobs/claim '{"workerId":"smoke-prot"}' "X-Worker-Key: $WKEY" >/dev/null
    FJOB2=$(jget id)
    req POST "/v1/worker/jobs/$FJOB2/fight-result" \
      "{\"matchId\":\"$FMATCH\",\"workerId\":\"smoke-prot\",\"verdict\":\"CHALLENGER\",\"replayUrls\":[],\"bouts\":[]}" \
      "X-Worker-Key: $WKEY" >/dev/null
    is "the defender's daily floor is reported as reached" \
       "$(dbq "SELECT rating_deltas->>'defenderFloorReached' FROM matches WHERE id='$FMATCH';")" true
    DAFTER=$(dbq "SELECT round(rating) FROM ratings WHERE robot_id='$LDFROBOT' AND category='$CHCAT';")
    DROP=$(( DBEFORE - DAFTER ))
    # EXACTLY the remaining allowance, not "at most". A range that includes 0
    # passes when the rating never moved at all, which cannot distinguish a
    # working floor from a broken one. The raw Glicko drop here is ~75+, so a
    # correct clamp lands on precisely $ALLOWED.
    is "…and the drop is exactly the remaining allowance ($SPENT of $FLOOR already spent today)" \
       "$DROP" "$ALLOWED"
    is "…the API reports how much of the day's allowance was already gone" \
       "$(dbq "SELECT round((rating_deltas->>'defenderDroppedToday')::numeric) FROM matches WHERE id='$FMATCH';")" "$SPENT"
    is "…and records that this rank was floor-protected" \
       "$(dbq "SELECT rating_deltas->>'defenderFloorProtected' FROM matches WHERE id='$FMATCH';")" true
  fi

  # ---- ...and who it does NOT protect (migration 005) ------------------
  # The floor guards an EARNED rank. A placement rating is a guess with a
  # high deviation attached, and the whole point of that deviation is to move
  # fast. Measured: at RD 350 one loss is worth 162 points, more than double
  # the entire daily allowance - so a protected provisional robot would sit
  # above its true rating for days and mislead everyone who challenges it.
  #
  # This check is the guard on the exemption itself: if it ever passes while
  # the ESTABLISHED case above also passes, the floor is working on exactly
  # the ranks it should and none of the ones it should not.
  dbq "DELETE FROM matches WHERE arena='synthetic';" >/dev/null
  dbq "UPDATE matches SET rating_deltas = rating_deltas - 'defender'
        WHERE defender_snapshot_id='$DSNAP' AND rating_deltas ? 'defender';" >/dev/null
  dbq "UPDATE ratings SET rating = 1300, deviation = 350 WHERE robot_id='$LDFROBOT' AND category='$CHCAT';" >/dev/null
  PBEFORE=$(dbq "SELECT round(rating) FROM ratings WHERE robot_id='$LDFROBOT' AND category='$CHCAT';")
  S=$(req POST /v1/challenges "{\"challengerSnapshotId\":\"$CHSNAP\",\"defenderSnapshotId\":\"$DSNAP\"}" "$AUTH")
  PMATCH=$(jget matchId)
  if [ "$S" != "200" ] || [ -z "$PMATCH" ]; then
    skip "the provisional exemption (3 checks)" "the provisional challenge did not open (HTTP $S)"
  else
    req POST /v1/worker/jobs/claim '{"workerId":"smoke-prot"}' "X-Worker-Key: $WKEY" >/dev/null
    PJOB=$(jget id)
    req POST "/v1/worker/jobs/$PJOB/fight-result" \
      "{\"matchId\":\"$PMATCH\",\"workerId\":\"smoke-prot\",\"verdict\":\"CHALLENGER\",\"replayUrls\":[],\"bouts\":[]}" \
      "X-Worker-Key: $WKEY" >/dev/null
    is "a PROVISIONAL defender is not floor-protected" \
       "$(dbq "SELECT rating_deltas->>'defenderFloorProtected' FROM matches WHERE id='$PMATCH';")" false
    is "…and is reported as provisional, so nobody thinks the floor broke" \
       "$(dbq "SELECT rating_deltas->>'defenderProvisional' FROM matches WHERE id='$PMATCH';")" true
    PAFTER=$(dbq "SELECT round(rating) FROM ratings WHERE robot_id='$LDFROBOT' AND category='$CHCAT';")
    PDROP=$(( PBEFORE - PAFTER ))
    if [ "$PDROP" -gt "$FLOOR" ]; then
      ok "…so it takes its full natural loss, $PDROP points, past the ${FLOOR}/day floor"
    else
      no "a provisional defender only fell $PDROP points — the floor is still clamping a rank that has not been earned"
    fi
  fi
fi

echo
echo "== M. the read side: leaderboard, match view, inbox, wallet (§2.1/§5.3) =="
# 2026-08-09. Everything before this WRITES the ladder. Until now nothing let
# anyone SEE it: ratings were computed, protected and stored, and no client
# could ask for them.
if [ -z "${CHSNAP:-}" ] || [ -z "${AUTH:-}" ]; then
  skip "the read side (11 checks)" "section K did not leave a rated robot"
else
  # ---- leaderboard ----------------------------------------------------
  S=$(req GET "/v1/leaderboard/$CHCAT")
  expect "the leaderboard is public — no token needed to scout standings" "$S" 200
  LBCOUNT=$(jget count)
  [ -n "$LBCOUNT" ] && [ "$LBCOUNT" -ge 1 ] \
    && ok "…and lists at least one rated robot ($LBCOUNT in $CHCAT)" \
    || no "the $CHCAT leaderboard came back empty, but section K rated robots in it"
  is "…ranked from 1" "$(jget entries.0.rank)" 1
  # Ranked by rating DESC: entry 1 must not be below entry 2.
  R1=$(jget entries.0.rating); R2=$(jget entries.1.rating)
  if [ -z "$R2" ]; then
    note "only one entry in $CHCAT; ordering not exercised"
    ok "…with a rating attached ($R1)"
  elif [ "$(echo "$R1 >= $R2" | bc -l 2>/dev/null || echo 1)" = "1" ]; then
    ok "…and ordered by rating, best first ($R1 >= $R2)"
  else
    no "the leaderboard is out of order: rank 1 is $R1 but rank 2 is $R2"
  fi
  # A high-deviation robot is soft, not authoritative. The board must say so.
  is "…flagging a placement-deviation robot as provisional" \
     "$(req GET "/v1/leaderboard/$CHCAT" >/dev/null; jget entries.0.provisional)" \
     "$(dbq "SELECT CASE WHEN deviation > 200 THEN 'True' ELSE 'False' END FROM ratings ra JOIN robots r ON r.id=ra.robot_id JOIN snapshots s ON s.robot_id=r.id WHERE s.id='$CHSNAP' AND ra.category='$CHCAT' LIMIT 1;")"
  S=$(req GET "/v1/leaderboard/BANTAM")
  expect "an unknown category is refused rather than returning an empty board" "$S" 400

  # ---- the match view, and M2's authz acceptance ----------------------
  # "a third account can scout both and watch the replay but cannot fetch
  # either program payload". The strongest form of that test is ANONYMOUS.
  if [ -n "${MATCH:-}" ]; then
    S=$(req GET "/v1/matches/$MATCH")
    expect "a match is publicly viewable, so a scout can watch a fight" "$S" 200
    is "…showing the verdict" "$(jget verdict)" CHALLENGER
    if grep -q 'replayUrls' "$BODY" && ! grep -qi 'payloadUrl\|storage_url\|storageUrl' "$BODY"; then
      ok "…carrying the replay but NO payload url — the program stays private (§1.3)"
    else
      no "the public match view leaked a payload url, or carried no replay at all"
    fi
  else
    skip "the public match view (3 checks)" "no settled match from section K"
  fi
  # The scouting card must not leak a payload either, to anyone, ever.
  S=$(req GET "/v1/snapshots/$CHSNAP")
  if [ "$S" = "200" ] && ! grep -qi 'payloadUrl\|storageUrl\|"payload"' "$BODY"; then
    ok "an anonymous scouting card carries no payload url"
  else
    no "the anonymous scouting card leaked a payload (HTTP $S)"
  fi

  # ---- inbox ----------------------------------------------------------
  S=$(req GET /v1/inbox "" "$AUTH")
  expect "the inbox loads for a signed-in player" "$S" 200
  INCOUNT=$(jget count)
  [ -n "$INCOUNT" ] && [ "$INCOUNT" -ge 1 ] \
    && ok "…and carries this account's matches ($INCOUNT)" \
    || no "the inbox is empty for an account that has fought"
  # The outcome must be stated from THIS account's point of view, not left
  # for the client to re-derive from a verdict enum.
  OUT0=$(jget matches.0.outcome)
  case "$OUT0" in
    WON|LOST|DRAW|PENDING) ok "…stating the outcome from this account's side ($OUT0)" ;;
    *) no "the inbox gave an outcome of '$OUT0', which no client can render" ;;
  esac
  S=$(req GET /v1/inbox)
  expect "…and an inbox is private — no token, no inbox" "$S" 401

  # ---- wallet ---------------------------------------------------------
  S=$(req GET /v1/wallet "" "$AUTH")
  expect "the wallet loads, so a client can show a balance before staking" "$S" 200
  is "…and its balance equals the sum of the ledger, not a cached column" \
     "$(jget balance)" "$(dbq "SELECT COALESCE(SUM(delta),0) FROM ledger WHERE user_id='$USERID';")"

  # ---- DEPOSIT TO CAREER, the one-way valve (§2.3) --------------------
  # Wallet scrap may move into the local career save and can NEVER come back.
  # That direction is the whole reason a hacked save is worthless online.
  BAL0=$(dbq "SELECT COALESCE(SUM(delta),0) FROM ledger WHERE user_id='$USERID';")
  IDEM="dep-$STAMP-1"
  S=$(req POST /v1/wallet/deposit "{\"amount\":40,\"idemKey\":\"$IDEM\"}" "$AUTH")
  expect "scrap deposits from the wallet to the career save" "$S" 200
  is "…debiting the wallet by exactly that much" \
     "$(dbq "SELECT COALESCE(SUM(delta),0) FROM ledger WHERE user_id='$USERID';")" "$((BAL0 - 40))"
  is "…as a NEGATIVE DEPOSIT_TO_CAREER row, which the schema also enforces" \
     "$(dbq "SELECT delta FROM ledger WHERE idem_key='$IDEM';")" -40
  # §8/M3 acceptance: "a deposited balance replayed from a tampered client is
  # rejected (idempotency key per deposit)". Replay the EXACT same request.
  S=$(req POST /v1/wallet/deposit "{\"amount\":40,\"idemKey\":\"$IDEM\"}" "$AUTH")
  expect "…and replaying that exact deposit is refused" "$S" 409
  is "…with the wallet unmoved by the replay" \
     "$(dbq "SELECT COALESCE(SUM(delta),0) FROM ledger WHERE user_id='$USERID';")" "$((BAL0 - 40))"
  is "…and exactly one row carrying that key, refused by the database" \
     "$(dbq "SELECT count(*) FROM ledger WHERE idem_key='$IDEM';")" 1
  # The valve only opens one way: there is no endpoint that credits a wallet
  # from a client-reported career balance, and the CHECK makes one
  # unrepresentable even if somebody wrote it.
  S=$(req POST /v1/wallet/deposit "{\"amount\":-100,\"idemKey\":\"dep-$STAMP-neg\"}" "$AUTH")
  expect "a NEGATIVE deposit cannot be used to mint scrap into the wallet" "$S" 400
  S=$(req POST /v1/wallet/deposit "{\"amount\":999999,\"idemKey\":\"dep-$STAMP-big\"}" "$AUTH")
  expect "…and you cannot deposit scrap you do not have" "$S" 400
  S=$(req POST /v1/wallet/deposit "{\"amount\":10}" "$AUTH")
  expect "…and a deposit without an idempotency key is refused outright" "$S" 400
fi

echo
echo "== O. league night: the ladder moves with nobody watching (§M3) =="
# Server-initiated round-robin. The acceptance clause names 28 matches, which
# is 8 robots paired every way; this bench has fewer robots, so it checks the
# ARITHMETIC (n*(n-1)/2) rather than the literal 28 - a check that only passes
# at one field size would be a check that never runs again.
#
# The dangerous part is not generating matches, it is SETTLING them. Nobody
# staked, so a settlement that refunds a recomputed stake would credit scrap
# against a debit that never happened. That is what migration 006 exists for
# and what the last two checks here are really testing.
if ! command -v psql >/dev/null 2>&1; then
  skip "league night (7 checks)" "psql is not on PATH"
else
  S=$(req POST "/v1/admin/league-night/$CHCAT" '{}' "X-Worker-Key: $WKEY")
  expect "a league night is generated for $CHCAT" "$S" 200
  FIELD=$(jget field); MADE=$(jget matches)
  if [ -n "$FIELD" ] && [ "$FIELD" -ge 2 ]; then
    WANT=$(( FIELD * (FIELD - 1) / 2 ))
    is "…pairing every robot in the field exactly once ($FIELD robots)" "$MADE" "$WANT"
  else
    no "the league night found a field of '$FIELD' — needs at least 2 rated robots with an ACTIVE snapshot"
  fi
  is "…every match staked ZERO, because nobody challenged" \
     "$(dbq "SELECT count(*) FROM matches WHERE arena='league_night' AND stake <> 0;")" 0
  is "…and each one queued a FIGHT job" \
     "$(dbq "SELECT count(*) FROM match_jobs j JOIN matches m ON m.id=j.match_id WHERE m.arena='league_night';")" "$MADE"
  # A league night must not consume anybody's daily challenge allowance:
  # §2.2 caps what a robot INITIATES, and a robot did not initiate this.
  TICKETS_BEFORE=$(dbq "SELECT COALESCE(SUM(used),0) FROM tickets WHERE day=CURRENT_DATE;")
  req POST "/v1/admin/league-night/$CHCAT" '{}' "X-Worker-Key: $WKEY" >/dev/null
  is "…and spends no challenge tickets, since no robot initiated it" \
     "$(dbq "SELECT COALESCE(SUM(used),0) FROM tickets WHERE day=CURRENT_DATE;")" "$TICKETS_BEFORE"

  # Settle one, unattended, and prove no scrap appeared from nowhere.
  WALL_BEFORE=$(dbq "SELECT COALESCE(SUM(delta),0) FROM ledger;")
  LNJOB=""
  for i in 1 2 3 4 5 6; do
    req POST /v1/worker/jobs/claim '{"workerId":"smoke-league"}' "X-Worker-Key: $WKEY" >/dev/null
    G=$(jget id); [ -z "$G" ] && break
    LNM=$(jget matchId)
    if [ -n "$LNM" ] && [ "$(dbq "SELECT arena FROM matches WHERE id='$LNM';")" = "league_night" ]; then
      LNJOB="$G"; break
    fi
  done
  if [ -z "$LNJOB" ]; then
    skip "settling a league-night match (2 checks)" "could not claim one"
  else
    S=$(req POST "/v1/worker/jobs/$LNJOB/fight-result" \
        "{\"matchId\":\"$LNM\",\"workerId\":\"smoke-league\",\"verdict\":\"CHALLENGER\",\"replayUrls\":[],\"bouts\":[]}" \
        "X-Worker-Key: $WKEY")
    expect "a league-night match settles unattended" "$S" 200
    # THE ONE THAT MATTERS. No stake went in, so nothing may come out.
    is "…without creating a single scrap of currency from nothing" \
       "$(dbq "SELECT COALESCE(SUM(delta),0) FROM ledger;")" "$WALL_BEFORE"
    is "…while still moving the ladder, which is the entire point" \
       "$(dbq "SELECT CASE WHEN rating_deltas IS NOT NULL THEN 'yes' ELSE 'no' END FROM matches WHERE id='$LNM';")" yes
  fi
fi

echo
echo "== P. season rollover (§2.4) =="
# "ratings compress 50% toward 1200, deviation resets high, wallet scrap
# persists, season badges + payouts awarded."
#
# M3's acceptance asks for a forced rollover on a staging DB producing correct
# compression and payouts. This is that, on the dev DB, at the end of a run
# that has already produced real ratings to compress.
if ! command -v psql >/dev/null 2>&1; then
  skip "season rollover (8 checks)" "psql is not on PATH"
else
  S0=$(dbq "SELECT value::int FROM ladder_config WHERE key='current_season';")
  # Pick a rating that is NOT 1200, or compression is unobservable - halving
  # the distance to 1200 from 1200 is 1200, and the check would pass on a
  # no-op.
  PICK=$(dbq "SELECT robot_id||'|'||category||'|'||round(rating) FROM ratings WHERE season_id=$S0 AND abs(rating-1200) > 20 ORDER BY abs(rating-1200) DESC LIMIT 1;")
  PR=${PICK%%|*}; REST=${PICK#*|}; PC=${REST%%|*}; PRATE=${REST##*|}
  WALLET_BEFORE=$(dbq "SELECT COALESCE(SUM(delta),0) FROM ledger;")

  S=$(req POST /v1/admin/season/rollover '{}' "X-Worker-Key: $WKEY")
  expect "a season rolls over" "$S" 200
  S1=$(jget toSeason)
  is "…into the next season" "$S1" "$((S0 + 1))"
  is "…and the config now points at it" \
     "$(dbq "SELECT value::int FROM ladder_config WHERE key='current_season';")" "$S1"
  is "…with a seasons row to point at" "$(dbq "SELECT count(*) FROM seasons WHERE id=$S1;")" 1

  if [ -z "$PR" ]; then
    skip "compression and deviation reset (2 checks)" "no rating far enough from 1200 to observe"
  else
    # 1200 + (r - 1200) * 0.5, rounded — halfway back to placement.
    WANT=$(( 1200 + (PRATE - 1200) / 2 ))
    GOT=$(dbq "SELECT round(rating) FROM ratings WHERE robot_id='$PR' AND category='$PC' AND season_id=$S1;")
    if [ -n "$GOT" ] && [ "$GOT" -ge "$((WANT - 1))" ] && [ "$GOT" -le "$((WANT + 1))" ]; then
      ok "…compressing $PRATE halfway toward 1200 -> $GOT"
    else
      no "compression is wrong: $PRATE should become about $WANT, got '$GOT'"
    fi
    is "…and resetting deviation HIGH, so the new season is genuinely open" \
       "$(dbq "SELECT round(deviation) FROM ratings WHERE robot_id='$PR' AND category='$PC' AND season_id=$S1;")" 350
  fi

  # §2.4: "wallet scrap persists". The only ledger movement may be the
  # SEASON payouts themselves — nothing is reset, nothing is cleared.
  PAID=$(dbq "SELECT COALESCE(SUM(delta),0) FROM ledger WHERE reason='SEASON';")
  is "…leaving wallets intact apart from the season payouts" \
     "$(dbq "SELECT COALESCE(SUM(delta),0) FROM ledger;")" "$((WALLET_BEFORE + PAID))"
  # Re-running THE SAME rollover would pay every podium again out of a faucet.
  # Calling the endpoint a second time is NOT that - it advances to the next
  # season, which is legitimate. The real test is to point the config back at
  # the season just closed and try to close it again, which is what a retried
  # cron job or a double-clicked operator button actually does.
  dbq "UPDATE ladder_config SET value=$S0 WHERE key='current_season';" >/dev/null
  S=$(req POST /v1/admin/season/rollover '{}' "X-Worker-Key: $WKEY")
  expect "…and closing the SAME season twice is refused" "$S" 409
  dbq "UPDATE ladder_config SET value=$S1 WHERE key='current_season';" >/dev/null
  is "…so nobody was paid for that season twice" \
     "$(dbq "SELECT count(*) FROM (SELECT idem_key FROM ledger WHERE reason='SEASON' GROUP BY idem_key HAVING count(*)>1) d;")" 0
fi

echo
echo "== Q. cosmetics: the economy finally has a sink (§M3) =="
# Every faucet here is live and the only way scrap could LEAVE a wallet was a
# stake you usually got back or a one-way career deposit. An economy with
# faucets and no sinks inflates until the numbers stop meaning anything.
#
# "no balance impact" is enforced by there being nowhere to put a stat: the
# catalogue has a name, a kind and a price, and no column that could affect a
# fight.
if [ -z "${AUTH:-}" ] || ! command -v psql >/dev/null 2>&1; then
  skip "cosmetics (9 checks)" "needs a token and psql"
else
  S=$(req GET /v1/cosmetics)
  expect "the catalogue is public — you can window-shop signed out" "$S" 200
  is "…and v1 stocks six items" "$(jget count)" 6
  is "…none of which carries a stat, because the table has no column for one" \
     "$(dbq "SELECT count(*) FROM information_schema.columns WHERE table_name='cosmetics' AND column_name NOT IN ('id','kind','name','price');")" 0

  CBAL=$(dbq "SELECT COALESCE(SUM(delta),0) FROM ledger WHERE user_id='$USERID';")
  S=$(req POST /v1/cosmetics/title_scrapper/buy '{}' "$AUTH")
  expect "a title can be bought" "$S" 200
  is "…debiting exactly its price" \
     "$(dbq "SELECT COALESCE(SUM(delta),0) FROM ledger WHERE user_id='$USERID';")" "$((CBAL - 60))"
  is "…as a NEGATIVE COSMETIC row, which the schema also enforces" \
     "$(dbq "SELECT count(*) FROM ledger WHERE user_id='$USERID' AND reason='COSMETIC' AND delta > 0;")" 0
  # Charging twice for something you already own is the failure that actually
  # costs a player money, and the primary key refuses it rather than a code
  # path that could be skipped.
  S=$(req POST /v1/cosmetics/title_scrapper/buy '{}' "$AUTH")
  expect "…and buying it twice is refused" "$S" 409
  is "…with the wallet unmoved by the second attempt" \
     "$(dbq "SELECT COALESCE(SUM(delta),0) FROM ledger WHERE user_id='$USERID';")" "$((CBAL - 60))"
  # Affordability, tested independently of how rich this account happens to
  # be. The first version bought the most expensive item in the catalogue and
  # expected a refusal - then season payouts landed, the wallet hit 1400, and
  # the purchase legitimately succeeded. A check that depends on the tester
  # being poor is a check that breaks when the economy works.
  NOW=$(dbq "SELECT COALESCE(SUM(delta),0) FROM ledger WHERE user_id='$USERID';")
  dbq "INSERT INTO cosmetics (id,kind,name,price) VALUES ('test_unaffordable','PLATE','Priced Beyond You',$((NOW + 1)))
       ON CONFLICT (id) DO UPDATE SET price=$((NOW + 1));" >/dev/null
  S=$(req POST /v1/cosmetics/test_unaffordable/buy '{}' "$AUTH")
  expect "…and one you cannot afford is refused, whatever you happen to hold" "$S" 400
  dbq "DELETE FROM cosmetics WHERE id='test_unaffordable';" >/dev/null

  # Equipping: you may only wear what you bought. A cosmetic you do not own
  # is not a display bug, it is a free item.
  S=$(req POST "/v1/robots/$ROBOT/equip" '{"titleId":"title_scrapper"}' "$AUTH")
  expect "an owned title equips to your robot" "$S" 200
  S=$(req POST "/v1/robots/$ROBOT/equip" '{"titleId":"title_ironclad"}' "$AUTH")
  expect "…but one you do not own does not" "$S" 400
  is "…and the title shows on the leaderboard, which is what makes it worth buying" \
     "$(req GET "/v1/leaderboard/$CHCAT" >/dev/null; dbq "SELECT c.name FROM robots r JOIN cosmetics c ON c.id=r.title_id WHERE r.id='$ROBOT';")" "Scrapper"
fi

echo
echo "== R. account deletion (§M4, and a store requirement) =="
# Apple and Google both require an in-app delete path; "email us" does not
# satisfy it. The hard part is not deleting - it is what must SURVIVE. The
# ledger is the audit trail a real-money phase needs (§2.3) and a match is
# also the OTHER player's history, so this is a redaction, not a DELETE.
#
# It runs on the DEFENDER account rather than the main one, because deleting
# the account every earlier section depends on would make the rest of this
# file untestable.
if [ -z "${DAUTH:-}" ] || [ -z "${DUSER:-}" ] || ! command -v psql >/dev/null 2>&1; then
  skip "account deletion (7 checks)" "no second account to delete"
else
  LEDGER_BEFORE=$(dbq "SELECT count(*) FROM ledger WHERE user_id='$DUSER';")
  MATCHES_BEFORE=$(dbq "SELECT count(*) FROM matches;")
  # Irreversible, so it takes an explicit confirmation rather than a bare
  # DELETE that a mis-wired client could send by accident.
  S=$(req DELETE /v1/account '{"confirm":"nope"}' "$DAUTH")
  expect "deletion without the confirmation phrase is refused" "$S" 400
  S=$(req DELETE /v1/account '{"confirm":"DELETE MY ACCOUNT"}' "$DAUTH")
  expect "…and with it, the account is deleted" "$S" 200

  is "…the email is gone" \
     "$(dbq "SELECT CASE WHEN email LIKE 'deleted+%@deleted.invalid' THEN 'redacted' ELSE 'STILL THERE' END FROM users WHERE id='$DUSER';")" redacted
  is "…so is the display name" \
     "$(dbq "SELECT display_name FROM users WHERE id='$DUSER';")" "deletedplayer"
  is "…their robots are retired, so nothing of theirs can be fought again" \
     "$(dbq "SELECT count(*) FROM robots WHERE user_id='$DUSER' AND NOT retired;")" 0
  is "…and off the ladder entirely" \
     "$(dbq "SELECT count(*) FROM ratings ra JOIN robots r ON r.id=ra.robot_id WHERE r.user_id='$DUSER';")" 0
  # The half that is easy to get wrong: a hard DELETE would cascade through
  # robots and snapshots and take other players' match history with it.
  is "…but the ledger survives, because it is an audit trail" \
     "$(dbq "SELECT count(*) FROM ledger WHERE user_id='$DUSER';")" "$LEDGER_BEFORE"
  is "…and every match still exists, because each is also somebody else's" \
     "$(dbq "SELECT count(*) FROM matches;")" "$MATCHES_BEFORE"
fi

echo
echo "== S. the blob read path (§1.3, §5.1) =="
# Stored blob URIs are not fetchable; /v1/blobs is where bytes come out, and
# §1.3 splits the authorisation by PREFIX: replays public, payloads not.
#
# These run against whichever store is configured. Locally that is
# FileBlobStore, whose URIs stay file:// and are not served here at all — so
# the prefix rules are asserted directly, which is what actually needs
# proving and is true of either store.
if [ -z "${WKEY:-}" ]; then
  skip "the blob read path (5 checks)" "no worker key"
else
  # A payload must never be readable without the worker key. This is the one
  # check that would let a scout read somebody's program.
  S=$(req GET "/v1/blobs/snapshots/whatever/x.json")
  expect "a payload blob is refused without the worker key" "$S" 401
  S=$(req GET "/v1/blobs/snapshots/whatever/x.json" "" "X-Worker-Key: wrong-key")
  expect "…and with a WRONG worker key" "$S" 401
  # With the right key it gets as far as looking, and 404s because that key
  # does not exist — which is the correct answer and proves authorisation
  # passed rather than short-circuited.
  S=$(req GET "/v1/blobs/snapshots/whatever/x.json" "" "X-Worker-Key: $WKEY")
  expect "…and with the RIGHT key it looks, and honestly reports nothing there" "$S" 404
  # Replays are public by §1.3 and M2's acceptance tests it anonymously.
  S=$(req GET "/v1/blobs/replays/whatever/x.rbr.gz")
  expect "a replay needs no token at all — a scout may watch any fight" "$S" 404
  # Traversal, before it reaches a store that might resolve it.
  S=$(req GET "/v1/blobs/replays/../../etc/passwd")
  case "$S" in
    404|400) ok "a traversal key is refused (HTTP $S)" ;;
    *) no "a traversal key returned HTTP $S — it should never resolve" ;;
  esac
fi

echo
echo "== N. ledger conservation, over everything this run just did (§8/M3) =="
# M3's acceptance: "every scrap created is accounted to a config'd faucet".
# This runs LAST on purpose - by now the bench has registered accounts, fought
# matches, tapered a win, hit a defense floor and made a deposit, so these are
# invariants over ~100 real operations rather than over a fixture.
#
# Every check here is a question the ledger should be able to answer about
# itself. If one fails, scrap was minted or destroyed by a path nobody meant
# to write.
if ! command -v psql >/dev/null 2>&1; then
  skip "ledger conservation (8 checks)" "psql is not on PATH"
else
  is "every CREDIT is a faucet the design doc names" \
     "$(dbq "SELECT count(*) FROM ledger WHERE delta > 0 AND reason NOT IN
              ('SIGNING_BONUS','PURSE','DEFENSE','FIRST_BLOOD','SEASON','STAKE_REFUND','ADJUSTMENT');")" 0
  is "every DEBIT is a stake or the one-way career valve" \
     "$(dbq "SELECT count(*) FROM ledger WHERE delta < 0 AND reason NOT IN
              ('STAKE','DEPOSIT_TO_CAREER','ADJUSTMENT','COSMETIC');")" 0
  is "no ledger row is worth zero scrap" \
     "$(dbq "SELECT count(*) FROM ledger WHERE delta = 0;")" 0
  is "every ledger row that names a match points at a real one" \
     "$(dbq "SELECT count(*) FROM ledger l LEFT JOIN matches m ON m.id = l.match_id
              WHERE l.match_id IS NOT NULL AND m.id IS NULL;")" 0
  # One stake per challenge. Two would mean a challenge was billed twice; the
  # settlement path refunds against the stake, so a double stake is a real
  # way to lose money.
  is "no match was staked more than once" \
     "$(dbq "SELECT count(*) FROM (SELECT match_id FROM ledger WHERE reason='STAKE'
              GROUP BY match_id HAVING count(*) > 1) d;")" 0
  is "no refund ever exceeds the stake it returns" \
     "$(dbq "SELECT count(*) FROM (
               SELECT match_id,
                      SUM(CASE WHEN reason='STAKE' THEN -delta ELSE 0 END) AS staked,
                      SUM(CASE WHEN reason='STAKE_REFUND' THEN delta ELSE 0 END) AS refunded
                 FROM ledger WHERE match_id IS NOT NULL GROUP BY match_id) t
              WHERE refunded > staked;")" 0
  # A forfeit is the point of losing: if the defender won, the challenger's
  # stake must NOT have come back.
  is "a challenger who lost never got their stake back" \
     "$(dbq "SELECT count(*) FROM ledger l JOIN matches m ON m.id = l.match_id
              WHERE m.verdict = 'DEFENDER' AND l.reason = 'STAKE_REFUND';")" 0
  is "a drawn match never paid a purse" \
     "$(dbq "SELECT count(*) FROM ledger l JOIN matches m ON m.id = l.match_id
              WHERE m.verdict = 'DRAW' AND l.reason IN ('PURSE','DEFENSE');")" 0
  # The invariant that catches everything else: a wallet is SUM(delta), and a
  # negative one means somebody staked or deposited scrap they never had.
  is "no wallet is overdrawn" \
     "$(dbq "SELECT count(*) FROM (SELECT user_id, SUM(delta) b FROM ledger
              GROUP BY user_id) w WHERE b < 0;")" 0
  note "ledger: $(dbq "SELECT count(*) FROM ledger;") rows, net $(dbq "SELECT COALESCE(SUM(delta),0) FROM ledger;") scrap across $(dbq "SELECT count(DISTINCT user_id) FROM ledger;") wallets"
fi

# --------------------------------------------------------------- section W
# The claim's kind filter. A specialised worker must be able to ask only for
# work it can run — otherwise it claims, refuses, and leaves the job CLAIMED
# with an attempt already burned, and three of those FAIL a job that was never
# broken. sql_bench proves the SQL; this proves the endpoint plumbs it through
# and validates it.
echo
echo "--- W. the claim kind filter ---"
c=$(req POST /v1/worker/jobs/claim '{"workerId":"kindtest","kind":"BANANA"}' "X-Worker-Key: $WKEY")
expect "a nonsense kind is refused, not silently ignored" "$c" "400"
c=$(req POST /v1/worker/jobs/claim '{"workerId":"kindtest","kind":"fight"}' "X-Worker-Key: $WKEY")
if [ "$c" = "200" ]; then
  same "a lowercase kind is accepted and returns that kind" "$(jget kind)" "FIGHT"
elif [ "$c" = "204" ]; then
  ok "a lowercase kind is accepted (no FIGHT work queued right now)"
else
  no "a lowercase kind is accepted -- HTTP $c"
fi
c=$(req POST /v1/worker/jobs/claim '{"workerId":"kindtest"}' "X-Worker-Key: $WKEY")
case "$c" in
  200|204) ok "omitting kind still claims any job (the old behaviour is intact)" ;;
  *) no "omitting kind broke the claim -- HTTP $c" ;;
esac

# --------------------------------------------------------------- section M
# §M4's monitoring surface.
echo
echo "--- M. metrics ---"
c=$(req GET /v1/admin/metrics "")
expect "metrics are refused without the worker key" "$c" "401"
c=$(req GET /v1/admin/metrics "" "X-Worker-Key: $WKEY")
if [ "$c" != "200" ]; then
  no "metrics are served to a worker -- HTTP $c"
  skip "the metric fields (5 checks)" "endpoint did not answer"
else
  ok "metrics are served to a worker"
  # Each field is asserted by NAME. A renamed or dropped field would
  # otherwise show up only as a Cloud Monitoring chart that quietly went
  # flat, which is the failure mode alerting exists to prevent.
  for f in ready claimed failed oldestReadyAgeS oldestHeartbeatAgeS pendingSnapshots; do
    v=$(jget "$f")
    [ -n "$v" ] && ok "metrics carry $f" || no "metrics are missing '$f'; a log-based metric built on it goes flat, not red"
  done
  # And the numbers must be real, not zeros from a query that silently
  # matched nothing. Compared against the database directly.
  same "pendingSnapshots agrees with the database" "$(jget pendingSnapshots)" \
       "$(dbq "SELECT count(*) FROM snapshots WHERE status='PENDING';")"
  same "failed agrees with the database" "$(jget failed)" \
       "$(dbq "SELECT count(*) FROM match_jobs WHERE status='FAILED';")"
fi
# The log line the Cloud Monitoring metrics parse. Its shape is load-bearing:
# the extractor is a regex, so a reworded message breaks every metric built on
# it and nothing goes red. Checked here because nothing else would notice.
SRVLOG="${SRVLOG:-$PWD/qa_api_server.log}"
if [ ! -f "$SRVLOG" ]; then
  skip "the rbmetrics log line keeps its shape" "no server log at $SRVLOG"
else
  # [0-9]+ and not [0-9]* — the first cut used * , which matches the EMPTY
  # string, so "ready= claimed=" would have passed as well-formed.
  RE='rbmetrics ready=[0-9]+ claimed=[0-9]+ failed=[0-9]+ done=[0-9]+ oldest_ready_s=[0-9]+ oldest_heartbeat_s=[0-9]+ pending_snapshots=[0-9]+ oldest_pending_s=[0-9]+'
  L=$(grep -Eo "$RE" "$SRVLOG" | tail -1)
  [ -n "$L" ] && ok "the rbmetrics log line keeps its shape" \
              || no "no well-formed rbmetrics line in $SRVLOG -- the log-based metrics parse this with a regex and would silently flatline"
  [ -n "$L" ] && note "$L"
  # …and that it reports the world rather than a row of zeros. The first
  # version of this check passed on "ready=0 … pending_snapshots=0", which a
  # query matching nothing at all would also produce. run_local.sh sets
  # Queue__ReapEverySeconds=5 so several passes land inside a 13-second run.
  NZ=$(grep -Eo "$RE" "$SRVLOG" | grep -Ec '(ready|claimed|failed|done|pending_snapshots)=[1-9]')
  [ "${NZ:-0}" -gt 0 ] && ok "…and reports real counts, not a row of zeros" \
    || no "every rbmetrics line was all zeros while the database held $(dbq "SELECT count(*) FROM match_jobs;") jobs -- a query matching nothing would look identical"
fi

# --------------------------------------------------------------- section B
# §2.4's season badges and §M3's "season history on robot cards". The payouts
# shipped with migration 007; the badges did not, so a season result was a
# ledger row nobody ever looks at again and nothing a scouting card could show.
echo
echo "--- B. season badges ---"
BADGES=$(dbq "SELECT count(*) FROM season_badges;")
if [ "${BADGES:-0}" = "0" ]; then
  skip "season badges (5 checks)" "no rollover has run in this session, so there is no podium to record"
else
  ok "the rollover awarded $BADGES badge(s)"
  # A badge must pair with the payout that bought it: same robot, same season,
  # same category. A podium paid but not recorded is a season result that
  # vanishes; recorded but not paid is worse.
  is "every badge has a matching SEASON payout" \
     "$(dbq "SELECT count(*) FROM season_badges b
              WHERE NOT EXISTS (SELECT 1 FROM ledger l
                                 WHERE l.reason = 'SEASON'
                                   AND l.idem_key = 'season:' || b.season_id || ':' || b.category || ':' || b.robot_id);")" 0
  is "…and no place is 0 or negative" \
     "$(dbq "SELECT count(*) FROM season_badges WHERE place < 1;")" 0
  is "…and no robot holds two badges in one category in one season" \
     "$(dbq "SELECT count(*) FROM (SELECT robot_id, season_id, category FROM season_badges
              GROUP BY 1,2,3 HAVING count(*) > 1) d;")" 0
  # The read path: a badge is only worth writing if a scouting card shows it.
  BROBOT=$(dbq "SELECT robot_id FROM season_badges LIMIT 1;")
  BSNAP=$(dbq "SELECT id FROM snapshots WHERE robot_id='$BROBOT' ORDER BY uploaded_at DESC LIMIT 1;")
  if [ -z "$BSNAP" ]; then
    skip "the scouting card shows season history" "the badged robot has no snapshot"
  else
    c=$(req GET "/v1/snapshots/$BSNAP" "")
    if [ "$c" != "200" ]; then
      no "the scouting card shows season history -- HTTP $c"
    else
      # Anonymous on purpose: a past placing is public, and this is the check
      # that it is visible to a SCOUT rather than only to the owner.
      N=$("$PY" -c "import json;d=json.load(open('$BODY'));print(len(d.get('seasonHistory') or []))" 2>/dev/null)
      if [ "${N:-0}" -gt 0 ]; then ok "an anonymous scout sees the robot's season history ($N entry/entries)"
      else no "seasonHistory is empty on a robot that holds a badge — the badge is unreadable"; fi
    fi
  fi
fi

# --------------------------------------------------------------- section P
# The proxy headers. THIS SECTION IS LAST ON PURPOSE: it deliberately
# exhausts an auth rate-limit bucket, and if the partitioning it is testing
# is broken then the bucket it exhausts is the GLOBAL one. Anything placed
# after this would fail with 429s that have nothing to do with it.
#
# Both defects here were measured against the live Cloud Run service, and
# neither is reproducible on a local run without the forwarded headers,
# because there the connection really is the client.
echo
echo "--- P. behind a proxy ---"
if [ "${TRUST_PROXY:-}" = "" ]; then
  skip "proxy header handling" "server not started with TRUST_PROXY=1"
else
  # The rate limiter must bucket by forwarded client, not by the proxy.
  # Ten per minute is the policy, so eleven from one client must be refused
  # — and a DIFFERENT client must still be served. That second half is the
  # whole check: without it this passes even if the limiter is global.
  A="X-Forwarded-For: 203.0.113.7"
  B="X-Forwarded-For: 198.51.100.4"
  PXY="X-Forwarded-Proto: https"
  saw429=""
  for i in $(seq 1 12); do
    c=$(req POST /v1/auth/register \
        "{\"email\":\"pxy-a-$$-$i@example.com\",\"password\":\"correct-horse-battery\",\"displayName\":\"PxyA$i\"}" \
        "$A" "$PXY")
    [ "$c" = "429" ] && { saw429="yes"; break; }
  done
  is "one client can be rate-limited to exhaustion" "${saw429:-no}" "yes"

  cB=$(req POST /v1/auth/register \
       "{\"email\":\"pxy-b-$$@example.com\",\"password\":\"correct-horse-battery\",\"displayName\":\"PxyB\"}" \
       "$B" "$PXY")
  if [ "$cB" = "429" ]; then
    no "a different client keeps its own bucket -- HTTP 429; the limiter is bucketing every caller together, which behind a proxy is one global 10/min budget for the whole world"
  else
    expect "a different client keeps its own bucket" "$cB" "200"
  fi

  # And the scheme. Only observable when blobs are rewritten into absolute
  # URLs, which needs an object store — file:// rows are returned unchanged.
  if [ "${BLOB_S3_BUCKET:-}" = "" ]; then
    skip "blob URLs are built with the forwarded scheme" "no object store configured (BLOB_S3_BUCKET unset); file:// rows are passed through verbatim"
  else
    cJ=$(req POST /v1/worker/jobs/claim '{"workerId":"proxy-probe"}' \
         "X-Worker-Key: $WKEY" "$PXY")
    if [ "$cJ" != "200" ]; then
      skip "blob URLs are built with the forwarded scheme" "no job to claim (HTTP $cJ)"
    else
      # Whichever job comes back. A VALIDATE carries payloadUrl; a FIGHT
      # carries the two robots instead, and by this point in the bench the
      # VALIDATEs are all worked, so FIGHT is the usual case. Checking only
      # payloadUrl made this skip itself on every run — a check that never
      # runs is not cover.
      PU=$(jget payloadUrl); [ -z "$PU" ] && PU=$(jget challengerUrl)
      [ -z "$PU" ] && PU=$(jget defenderUrl)
      case "$PU" in
        https://*) ok "blob URLs are built with the forwarded scheme" ;;
        http://*)  no "blob URLs are built with the forwarded scheme -- got '$PU'; an http:// URL on an HTTPS-only deployment answers 302, and the redirect drops X-Worker-Key" ;;
        *)         skip "blob URLs are built with the forwarded scheme" "payloadUrl was not an absolute URL ('$PU')" ;;
      esac
    fi
  fi
fi

rm -f "$BODY" "$VRC_SNAP_FILE" "$VRC_JOB_FILE"
echo
if [ "$skipped" -gt 0 ]; then
  echo "----- NOT COVERED BY THIS RUN ($skipped) -----"
  cat "$SKIP_LOG"
  echo "----- a skip is not a pass; each line above is cover this run did not provide -----"
  echo
fi
rm -f "$SKIP_LOG"
echo "===== passed $pass  failed $fail  skipped $skipped ====="
[ "$fail" -eq 0 ] || exit 1
