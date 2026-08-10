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
# Usage:  bash tests/api_smoke.sh [base-url]      (default http://localhost:5000)
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

BASE="${1:-http://localhost:5000}"
WKEY="${WORKER_KEY:-dev-only-worker-key}"
PY=$(command -v python3 || command -v python) || { echo "python3 is required"; exit 2; }

pass=0; fail=0; skipped=0
ok()   { pass=$((pass+1));       printf 'PASS  %s\n' "$1"; }
no()   { fail=$((fail+1));       printf 'FAIL  %s\n' "$1"; }
skip() { skipped=$((skipped+1)); printf 'SKIP  %s -- %s\n' "$1" "$2"; }
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
dbq() { PGPASSWORD=rb psql -h localhost -U rb -d rb -qtA -c "$1" 2>/dev/null | tr -d '[:space:]'; }

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
    skip "fail-reason visibility (3 checks)" "could not upload or claim the rejected snapshot"
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
        CHRD=$(dbq "SELECT round(deviation) FROM ratings WHERE robot_id='$CHROBOT';")
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
      HRATE=$(dbq "SELECT count(*) FROM ratings WHERE robot_id='$HROBOT';")
      is "a punch-up leaves the heavier defender UNRATED, so heavies cannot be farmed" "$HRATE" 0
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
              ('STAKE','DEPOSIT_TO_CAREER','ADJUSTMENT');")" 0
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

rm -f "$BODY" "$VRC_SNAP_FILE" "$VRC_JOB_FILE"
echo
echo "===== passed $pass  failed $fail  skipped $skipped ====="
[ "$fail" -eq 0 ] || exit 1
