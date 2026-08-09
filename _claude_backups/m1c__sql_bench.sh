#!/usr/bin/env bash
# ===========================================================================
# sql_bench.sh — the data layer's bench.
#
# §5.3 calls the match lifecycle "the one flow that must be airtight", and
# almost all of it is SQL: the escrow transaction, the SKIP LOCKED claim, the
# visibility timeout, the append-only ledger. Those are testable without a
# line of C#, and they are the parts where being wrong is expensive and quiet.
#
# It runs the SAME query text the API ships (RobotBrawl.Api/Sql/*.sql, loaded
# here via PREPARE) so a passing bench cannot drift from a broken server.
#
# House style: every check prints PASS/FAIL with what it measured.
# ===========================================================================
set -uo pipefail

API_SQL="$(dirname "$0")/../RobotBrawl.Api/Sql"
export PGPASSWORD=${PGPASSWORD:-rb}
PSQL=(psql -h 127.0.0.1 -U rb -d rb -v ON_ERROR_STOP=0 -qtA)

pass=0; fail=0
ok()   { pass=$((pass+1)); printf 'PASS  %s\n' "$1"; }
no()   { fail=$((fail+1)); printf 'FAIL  %s\n' "$1"; }
note() { printf '      %s\n' "$1"; }

# check_ok  <label> <sql>     -- expects success
# check_err <label> <sql>     -- expects the statement to be REFUSED
check_ok()  { local out; out=$("${PSQL[@]}" -c "$2" 2>&1); if [ $? -eq 0 ] && ! grep -q '^ERROR' <<<"$out"; then ok "$1"; else no "$1 -- $(head -1 <<<"$out")"; fi; }
check_err() { local out; out=$("${PSQL[@]}" -c "$2" 2>&1); if grep -q '^ERROR' <<<"$out"; then ok "$1 ($(head -1 <<<"$out" | cut -c1-72))"; else no "$1 -- statement was ACCEPTED and should not have been"; fi; }
q()         { "${PSQL[@]}" -c "$1" 2>&1; }

echo "== resetting fixtures =="
q "TRUNCATE ledger, match_jobs, matches, snapshots, robots, users, tickets, ratings RESTART IDENTITY CASCADE;" >/dev/null

U1=$(q "INSERT INTO users (email, pw_hash, display_name) VALUES ('Owen@Example.com','x','Owen') RETURNING id;")
U2=$(q "INSERT INTO users (email, pw_hash, display_name) VALUES ('rival@example.com','x','Rival') RETURNING id;")
R1=$(q "INSERT INTO robots (user_id, name) VALUES ('$U1','Alpha') RETURNING id;")
R2=$(q "INSERT INTO robots (user_id, name) VALUES ('$U2','Beta') RETURNING id;")
H='0000000000000000000000000000000000000000000000000000000000000000'
S1=$(q "INSERT INTO snapshots (robot_id,storage_url,sha256,client_version,status) VALUES ('$R1','gs://x/1','$H','t',   'ACTIVE') RETURNING id;")
S2=$(q "INSERT INTO snapshots (robot_id,storage_url,sha256,client_version,status) VALUES ('$R2','gs://x/2','$H','t',   'ACTIVE') RETURNING id;")
note "users $U1 / $U2 ; robots $R1 / $R2"

echo
echo "== A. identity and snapshots =="
check_err "case-insensitive email is unique (OWEN@example.com is Owen)" \
  "INSERT INTO users (email,pw_hash,display_name) VALUES ('OWEN@EXAMPLE.COM','x','Twin');"
check_err "sha256 must be 64 hex chars" \
  "INSERT INTO snapshots (robot_id,storage_url,sha256,client_version) VALUES ('$R1','gs://x/3','nothex','t');"
check_err "a robot cannot have two ACTIVE snapshots" \
  "INSERT INTO snapshots (robot_id,storage_url,sha256,client_version,status) VALUES ('$R1','gs://x/4','$H','t','ACTIVE');"
check_ok  "…but it can have any number of superseded ones" \
  "INSERT INTO snapshots (robot_id,storage_url,sha256,client_version,status) VALUES ('$R1','gs://x/5','$H','t','SUPERSEDED'), ('$R1','gs://x/6','$H','t','SUPERSEDED');"
check_err "an unknown category is refused" \
  "INSERT INTO snapshots (robot_id,storage_url,sha256,client_version,category) VALUES ('$R1','gs://x/7','$H','t','CRUISERWEIGHT');"

echo
echo "== B. the ledger is append-only, in the database =="
q "INSERT INTO ledger (user_id,delta,reason) VALUES ('$U1',500,'SIGNING_BONUS');" >/dev/null
check_err "UPDATE on the ledger is refused" "UPDATE ledger SET delta = 999999 WHERE user_id='$U1';"
check_err "DELETE on the ledger is refused" "DELETE FROM ledger WHERE user_id='$U1';"
check_err "a zero-delta row is meaningless and refused" \
  "INSERT INTO ledger (user_id,delta,reason) VALUES ('$U1',0,'ADJUSTMENT');"
check_err "DEPOSIT_TO_CAREER cannot be POSITIVE — the valve is one-way (§2.3)" \
  "INSERT INTO ledger (user_id,delta,reason) VALUES ('$U1',250,'DEPOSIT_TO_CAREER');"
check_ok  "…the same reason as a withdrawal is fine" \
  "INSERT INTO ledger (user_id,delta,reason,idem_key) VALUES ('$U1',-200,'DEPOSIT_TO_CAREER','dep-1');"
check_err "a replayed deposit is refused by the idempotency key" \
  "INSERT INTO ledger (user_id,delta,reason,idem_key) VALUES ('$U1',-200,'DEPOSIT_TO_CAREER','dep-1');"
BAL=$(q "SELECT balance FROM wallet_balances WHERE user_id='$U1';")
[ "$BAL" = "300" ] && ok "balance is SUM(delta): 500 signing - 200 deposit = 300 (read $BAL)" \
                   || no "balance should be 300, read '$BAL'"
check_err "an unknown ledger reason is refused" \
  "INSERT INTO ledger (user_id,delta,reason) VALUES ('$U1',10,'MYSTERY');"

echo
echo "== C. matches and job shape =="
check_err "a snapshot cannot fight itself" \
  "INSERT INTO matches (challenger_snapshot_id,defender_snapshot_id,category,seeds) VALUES ('$S1','$S1','LIGHT','{1}');"
M1=$(q "INSERT INTO matches (challenger_snapshot_id,defender_snapshot_id,category,seeds) VALUES ('$S1','$S2','LIGHT','{1,2,3}') RETURNING id;")
check_err "a FIGHT job carrying a snapshot_id is not representable" \
  "INSERT INTO match_jobs (kind,match_id,snapshot_id) VALUES ('FIGHT','$M1','$S1');"
check_err "a VALIDATE job carrying a match_id is not representable" \
  "INSERT INTO match_jobs (kind,match_id) VALUES ('VALIDATE','$M1');"
check_ok  "a well-formed FIGHT job inserts" \
  "INSERT INTO match_jobs (kind,match_id) VALUES ('FIGHT','$M1');"
check_err "…and a second live job for the same match is refused" \
  "INSERT INTO match_jobs (kind,match_id) VALUES ('FIGHT','$M1');"

echo
echo "== D. SKIP LOCKED under real contention =="
# Build the runner files by CONCATENATION, never by shell interpolation: the
# query text contains $1/$2 placeholders and an unquoted heredoc silently eats
# them, which is exactly how the first version of this bench "found" a double
# claim that was never there. Worker ids come in as psql variables instead.
mk_runner() {  # $1 = query file, $2 = out file, $3 = extra lines after EXECUTE
  { printf 'PREPARE st AS '; grep -v '^--' "$1"; printf ';\n'; printf '%s\n' "$3"; } > "$2"
}
mk_runner "$API_SQL/claim_job.sql" /tmp/claim_run.sql \
  "BEGIN;
EXECUTE st(:'worker');
SELECT pg_sleep(0.6);
COMMIT;"

q "DELETE FROM match_jobs;" >/dev/null
for i in 1 2 3 4; do
  MX=$(q "INSERT INTO matches (challenger_snapshot_id,defender_snapshot_id,category,seeds) VALUES ('$S1','$S2','LIGHT','{$i}') RETURNING id;")
  q "INSERT INTO match_jobs (kind,match_id) VALUES ('FIGHT','$MX');" >/dev/null
done
rm -f /tmp/claim_*.out
for w in 1 2 3 4; do
  ( "${PSQL[@]}" -v worker="worker-$w" -f /tmp/claim_run.sql > "/tmp/claim_$w.out" 2>&1 ) &
done
wait
GOT=$(cat /tmp/claim_*.out | grep -E '^[0-9]+\|' | cut -d'|' -f1 | sort)
NGOT=$(grep -c . <<<"$GOT")
NUNIQ=$(sort -u <<<"$GOT" | grep -c .)
note "job ids claimed: $(tr '\n' ' ' <<<"$GOT")"
[ "$NGOT" = "4" ] && ok "4 concurrent workers each claimed a job" || no "expected 4 claims, got $NGOT"
[ "$NGOT" = "$NUNIQ" ] && ok "no job was claimed twice ($NUNIQ distinct of $NGOT)" \
                       || no "DOUBLE CLAIM: $NUNIQ distinct of $NGOT"
LEFT=$(q "SELECT count(*) FROM match_jobs WHERE status='READY';")
[ "$LEFT" = "0" ] && ok "queue drained to 0 READY" || no "expected 0 READY, found $LEFT"
ATT=$(q "SELECT DISTINCT attempts FROM match_jobs WHERE status='CLAIMED';")
[ "$ATT" = "1" ] && ok "claiming burns exactly one attempt (read $ATT)" || no "attempts should be 1, read '$ATT'"

# A fifth worker against an empty queue must come back empty, not block.
"${PSQL[@]}" -v worker="worker-5" -f /tmp/claim_run.sql > /tmp/claim_empty.out 2>&1
EMP=$(grep -cE '^[0-9]+\|' /tmp/claim_empty.out)
[ "$EMP" = "0" ] && ok "a worker finding an empty queue returns nothing and does not block" \
                 || no "empty-queue claim returned $EMP rows"

echo
echo "== E. ownership: a worker only touches its own job =="
JID=$(q "SELECT id FROM match_jobs WHERE claimed_by='worker-1';")
mk_runner "$API_SQL/heartbeat_job.sql" /tmp/hb_run.sql "EXECUTE st(:jid, :'worker');"
mk_runner "$API_SQL/complete_job.sql" /tmp/cp_run.sql "EXECUTE st(:jid, :'worker');"
R=$("${PSQL[@]}" -v jid="$JID" -v worker="worker-9" -f /tmp/hb_run.sql 2>&1 | grep -cE '^[0-9]+$')
[ "$R" = "0" ] && ok "worker-9 cannot heartbeat worker-1's job (0 rows)" || no "foreign heartbeat updated $R rows"
R=$("${PSQL[@]}" -v jid="$JID" -v worker="worker-9" -f /tmp/cp_run.sql 2>&1 | grep -cE '^[0-9]+$')
[ "$R" = "0" ] && ok "worker-9 cannot complete worker-1's job (0 rows)" || no "foreign completion updated $R rows"
R=$("${PSQL[@]}" -v jid="$JID" -v worker="worker-1" -f /tmp/hb_run.sql 2>&1 | grep -E '^[0-9]+$' | head -1)
[ "$R" = "$JID" ] && ok "worker-1 can heartbeat its own job" || no "own heartbeat returned '$R'"

echo
echo "== F. visibility timeout and retry exhaustion =="
mk_runner "$API_SQL/reap_stale_jobs.sql" /tmp/reap_run.sql "EXECUTE st(:stale, :maxatt);"
mk_runner "$API_SQL/claim_job.sql" /tmp/claim1.sql "EXECUTE st(:'worker');"
q "UPDATE match_jobs SET heartbeat_at = now() - interval '10 minutes' WHERE status='CLAIMED';" >/dev/null
R=$("${PSQL[@]}" -v stale=300 -v maxatt=3 -f /tmp/reap_run.sql 2>&1)
NBACK=$(grep -c '|READY|' <<<"$R")
[ "$NBACK" = "4" ] && ok "4 stale jobs returned to READY (attempt 1 of 3)" || no "expected 4 READY, got $NBACK -- $(head -2 <<<"$R")"
for round in 2 3; do
  for w in 1 2 3 4; do "${PSQL[@]}" -v worker="worker-$w" -f /tmp/claim1.sql >/dev/null 2>&1; done
  q "UPDATE match_jobs SET heartbeat_at = now() - interval '10 minutes' WHERE status='CLAIMED';" >/dev/null
  "${PSQL[@]}" -v stale=300 -v maxatt=3 -f /tmp/reap_run.sql >/dev/null 2>&1
done
NF=$(q "SELECT count(*) FROM match_jobs WHERE status='FAILED';")
NR=$(q "SELECT count(*) FROM match_jobs WHERE status='READY';")
[ "$NF" = "4" ] && ok "after 3 attempts the jobs FAIL rather than retry forever" \
               || no "expected 4 FAILED, got $NF (READY $NR)"
note "last_error: $(q "SELECT DISTINCT last_error FROM match_jobs WHERE status='FAILED';")"
LIVE=$(q "SELECT count(*) FROM match_jobs WHERE status IN ('READY','CLAIMED');")
[ "$LIVE" = "0" ] && ok "nothing is left live after exhaustion" || no "$LIVE jobs still live"
check_ok "a FAILED job does not block re-queueing its match" \
  "INSERT INTO match_jobs (kind,match_id) SELECT 'FIGHT', match_id FROM match_jobs WHERE status='FAILED' LIMIT 1;"

echo
echo "== G. escrow is one transaction (§5.3 step 1) =="
q "DELETE FROM match_jobs; " >/dev/null
BEFORE=$(q "SELECT balance FROM wallet_balances WHERE user_id='$U1';")
OUT=$( "${PSQL[@]}" -v ON_ERROR_STOP=1 <<SQL 2>&1
BEGIN;
INSERT INTO ledger (user_id,delta,reason) VALUES ('$U1',-50,'STAKE');
INSERT INTO matches (challenger_snapshot_id,defender_snapshot_id,category,seeds)
     VALUES ('$S1','$S2','LIGHT','{9,9,9,9,9,9}');
COMMIT;
SQL
)
AFTER=$(q "SELECT balance FROM wallet_balances WHERE user_id='$U1';")
if grep -q 'ERROR' <<<"$OUT" && [ "$BEFORE" = "$AFTER" ]; then
  ok "an illegal match rolls the stake back with it (balance stayed $AFTER)"
else
  no "escrow did not roll back: before $BEFORE after $AFTER"
fi

echo
echo "===== passed $pass  failed $fail ====="
[ "$fail" -eq 0 ] || exit 1
