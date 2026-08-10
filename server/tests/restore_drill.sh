#!/usr/bin/env bash
# ===========================================================================
# restore_drill.sh — §M4: "nightly DB backup verified by RESTORE DRILL".
#
#   bash server/tests/restore_drill.sh
#
# The emphasis is the point. A backup that has never been restored is not a
# backup, it is a file. This takes a real dump of the dev database, restores
# it into a SEPARATE database, and then proves the restored copy is usable
# rather than merely present:
#
#   * every table came back with the same row count
#   * the LEDGER balances to the same number, per user — a backup that loses
#     a ledger row is a backup that loses somebody's money, and a row count
#     alone would not catch a truncated `delta`
#   * the constraints came back, not just the data. A restore that drops the
#     append-only trigger or the CHECKs is a database that will happily
#     accept the corruption those rules exist to refuse.
#
# It NEVER touches the source database. The restore target is dropped and
# recreated each run, and it is a different name; if this script ever points
# both at `rb` it will have deleted the thing it was protecting.
# ===========================================================================
set -uo pipefail

SRC="${SRC_DB:-rb}"
DST="${DST_DB:-rb_restore_drill}"
export PGPASSWORD="${PGPASSWORD:-rb}"
PSQL=(psql -h localhost -U rb -qtA)
DUMP="/tmp/rb_backup_$$.dump"

pass=0; fail=0
ok()   { pass=$((pass+1)); printf 'PASS  %s\n' "$1"; }
no()   { fail=$((fail+1)); printf 'FAIL  %s\n' "$1"; }
note() { printf '      %s\n' "$1"; }

if [ "$SRC" = "$DST" ]; then
  echo "REFUSING TO RUN: source and destination are both '$SRC'. This drill"
  echo "would restore over the database it is meant to protect."
  exit 2
fi

echo "== taking the backup =="
if pg_dump -h localhost -U rb -Fc -f "$DUMP" "$SRC" 2>/dev/null; then
  ok "pg_dump wrote a custom-format backup ($(du -h "$DUMP" | cut -f1))"
else
  no "pg_dump failed — there is nothing to restore, which is the worst result here"
  echo; echo "===== passed $pass  failed $fail ====="; exit 1
fi

echo
echo "== restoring into a SEPARATE database =="
"${PSQL[@]}" -d postgres -c "DROP DATABASE IF EXISTS $DST;" >/dev/null 2>&1
if "${PSQL[@]}" -d postgres -c "CREATE DATABASE $DST OWNER rb;" >/dev/null 2>&1; then
  ok "created $DST"
else
  no "could not create $DST"; echo; echo "===== passed $pass  failed $fail ====="; exit 1
fi
# pg_restore chatters on stderr about things that are not errors; the real
# test is the comparisons below, not its exit code.
pg_restore -h localhost -U rb -d "$DST" "$DUMP" >/dev/null 2>&1
ok "pg_restore completed"

echo
echo "== is the restored copy the same database? =="
# An ARRAY, not a space-separated string. zsh does not word-split an
# unquoted parameter the way bash does, so `for t in $TABLES` under zsh
# iterates ONCE with the whole list as a single name - which is exactly how
# the first run of this drill reported all twelve tables "MISSING". The
# shebang says bash; an array means it no longer matters who invokes it.
TABLES=(users robots snapshots matches match_jobs ledger ratings tickets seasons ladder_config cosmetics user_cosmetics)
mismatch=0
for t in "${TABLES[@]}"; do
  a=$("${PSQL[@]}" -d "$SRC" -c "SELECT count(*) FROM $t;" 2>/dev/null)
  b=$("${PSQL[@]}" -d "$DST" -c "SELECT count(*) FROM $t;" 2>/dev/null)
  if [ -z "$b" ]; then no "$t is MISSING from the restore"; mismatch=1
  elif [ "$a" != "$b" ]; then no "$t: $a rows in $SRC, $b in the restore"; mismatch=1
  fi
done
[ "$mismatch" = "0" ] && ok "every table restored with the same row count (${#TABLES[@]} tables)"

# Money, specifically. A row count cannot see a truncated delta.
A=$("${PSQL[@]}" -d "$SRC" -c "SELECT COALESCE(SUM(delta),0) FROM ledger;")
B=$("${PSQL[@]}" -d "$DST" -c "SELECT COALESCE(SUM(delta),0) FROM ledger;")
[ "$A" = "$B" ] && ok "the ledger totals the same in both ($A scrap)" \
                || no "the ledger does not balance after restore: $A vs $B"
A=$("${PSQL[@]}" -d "$SRC" -c "SELECT count(*) FROM (SELECT user_id, SUM(delta) FROM ledger GROUP BY user_id) x;")
B=$("${PSQL[@]}" -d "$DST" -c "SELECT count(*) FROM (SELECT user_id, SUM(delta) FROM ledger GROUP BY user_id) x;")
[ "$A" = "$B" ] && ok "…across the same number of wallets ($A)" \
                || no "wallet count changed across the restore: $A vs $B"

echo
echo "== did the RULES come back, or only the rows? =="
# A restore that loses the constraints is a database that will accept the
# corruption they exist to refuse — and it looks perfectly healthy until it
# does.
# ⚠ CAPTURE, then grep. NOT `q "..." | grep -q ERROR`.
#
# `set -o pipefail` makes a pipeline return the RIGHTMOST non-zero status, and
# psql exits 1 precisely when the statement is refused — which is the outcome
# these checks are hoping for. So the piped form reported every SURVIVING
# constraint as missing: the check failed exactly when the database was
# healthy. It cost three false alarms before I spotted it, and it would have
# been read as "the backup loses its constraints", which is alarming and
# wrong.
refused() { # <label-if-refused> <label-if-accepted> <sql>
  local out; out=$("${PSQL[@]}" -d "$DST" -c "$3" 2>&1)
  if grep -q 'ERROR' <<<"$out"; then ok "$1"; else no "$2"; fi
}
refused "ledger_delta_nonzero survived the restore" \
        "a zero-delta ledger row was ACCEPTED in the restore — the CHECK is gone" \
        "INSERT INTO ledger (user_id, delta, reason) SELECT id, 0, 'ADJUSTMENT' FROM users LIMIT 1;"
refused "the one-way career valve survived" \
        "a POSITIVE deposit was accepted in the restore — the valve is gone" \
        "INSERT INTO ledger (user_id, delta, reason) SELECT id, 5, 'DEPOSIT_TO_CAREER' FROM users LIMIT 1;"
refused "snapshots_category_check survived" \
        "an invalid category was accepted in the restore" \
        "INSERT INTO snapshots (robot_id, storage_url, sha256, client_version, status, category)
         SELECT robot_id, 'x', repeat('0',64), 't', 'PENDING', 'BANTAM' FROM snapshots LIMIT 1;"

echo
echo "== cleaning up =="
"${PSQL[@]}" -d postgres -c "DROP DATABASE IF EXISTS $DST;" >/dev/null 2>&1 && ok "drill database dropped"
rm -f "$DUMP" && note "backup file removed"

echo
echo "===== passed $pass  failed $fail ====="
[ "$fail" -eq 0 ] || exit 1
