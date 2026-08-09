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
vr_category() { # <raw-json-value> [legal=true]
  local rawcat="$1" legal="${2:-true}" snap job got i
  : > "$VRC_SNAP_FILE"
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
  S=$(vr_category '""' false)
  case "$S" in
    4??) ok "the empty-string category is refused cleanly (HTTP $S)" ;;
    5??) no "the empty-string category is refused as a SERVER error (HTTP $S) -- the CHECK fires inside the UPDATE and nothing catches it; a worker's verdict is lost to a stack trace" ;;
    *)   no "the empty-string category was ACCEPTED (HTTP $S) -- snapshots.category's CHECK should have forbidden it" ;;
  esac

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

rm -f "$BODY" "$VRC_SNAP_FILE"
echo
echo "===== passed $pass  failed $fail  skipped $skipped ====="
[ "$fail" -eq 0 ] || exit 1
