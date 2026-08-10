-- Claim one job (§5.1). FOR UPDATE SKIP LOCKED is what makes a plain table a
-- safe multi-worker queue: a row another transaction already holds is skipped
-- rather than waited on, so N workers claim N different jobs with no
-- coordination and no broker. attempts increments ON CLAIM, not on failure,
-- so a worker that dies without ever reporting still burns an attempt and
-- cannot retry forever.
--
-- 2026-08-09 — THE CLAIM NOW SAYS WHERE THE PAYLOAD IS.
-- Until today this returned (id, kind, match_id, snapshot_id, attempts) and
-- nothing else, which meant a worker was handed a UUID and no way to resolve
-- it: snapshots.storage_url was written at upload and read by NO code path,
-- IBlobStore.GetAsync was called by nothing, and GET /v1/snapshots/{id}
-- withholds the payload URL on purpose ("the only thing that ever reads a
-- payload is a worker") — while no worker endpoint served one. The worker
-- could not fetch the thing it had been asked to validate. Every file was
-- individually right; the seam between them was never closed, and nothing
-- noticed because api_smoke.sh tested each endpoint alone and no consumer had
-- ever tried to COMPLETE a job.
--
-- §5.1 has workers fetch snapshots from storage rather than through the API,
-- which keeps payloads off the request path — so the claim carries the
-- LOCATION, not the bytes. sha256 rides along on purpose: the envelope
-- carries its own hash and RobotSnapshot.Open() already refuses a payload
-- whose hash does not match, so the worker can verify what storage handed it
-- without trusting either storage or the API. That check exists today and
-- nothing could reach it.
--
-- FIGHT jobs get BOTH snapshots by the same rule. Written now, while the
-- shape is obvious, even though the FIGHT half of the API does not exist yet
-- (no match-create, no match-result, no replay upload — see
-- M1_Worker_Contract_Gaps_2026-08-09.md).
--
-- Columns 0-4 are unchanged and in the same order, so sql_bench.sh's
-- `^[0-9]+\|` / cut -f1 parsing and api_smoke.sh's `jget id` keep working:
-- this is additive.
--
-- 2026-08-09 — $2 FILTERS BY KIND, AND IT PREVENTS A REAL DATA-LOSS BUG.
--
-- Until now a claim took a worker id and nothing else, so every worker
-- claimed indiscriminately. Both loops REFUSE a job of the wrong kind after
-- claiming it (FightWorkerLoop: "claimed a VALIDATE job, which this loop does
-- not run") and leave it CLAIMED for the reaper rather than failing it, which
-- is the polite thing to do — but the attempt has already been burned, because
-- attempts increments ON CLAIM.
--
-- So in any fleet running both kinds, a job repeatedly claimed by the wrong
-- worker hits maxAttempts and is FAILED while nothing was ever wrong with it.
-- Since 2026-08-09 a failed VALIDATE also REJECTS the player's snapshot, so
-- the visible result is a legitimate robot refused with a reason that is not
-- true. It has never fired because no worker has ever been deployed — this is
-- being fixed on the way in, not after.
--
-- $2 NULL keeps the old behaviour exactly (any kind), so every existing caller
-- and every existing bench is unaffected.
-- $1 = worker id
-- $2 = kind filter: NULL for any, else 'VALIDATE' or 'FIGHT'
WITH claimed AS (
    SELECT id FROM match_jobs
     WHERE status = 'READY'
       AND ($2::text IS NULL OR kind = $2::text)
     ORDER BY created_at
     FOR UPDATE SKIP LOCKED
     LIMIT 1
),
took AS (
    UPDATE match_jobs j
       SET status = 'CLAIMED', claimed_by = $1, claimed_at = now(),
           heartbeat_at = now(), attempts = j.attempts + 1
      FROM claimed c
     WHERE j.id = c.id
    RETURNING j.id, j.kind, j.match_id, j.snapshot_id, j.attempts
)
SELECT t.id, t.kind, t.match_id, t.snapshot_id, t.attempts,
       vs.storage_url, vs.sha256,
       cs.storage_url, cs.sha256,
       ds.storage_url, ds.sha256,
       m.arena, m.seeds
  FROM took t
  -- LEFT joins, not inner: the match_jobs CHECK guarantees a VALIDATE job has
  -- no match_id and a FIGHT job has no snapshot_id, so exactly one of these
  -- two sides is NULL by construction. An inner join would return no row at
  -- all and read to the worker as "queue empty" — a silent stall rather than
  -- an error, which is the worst way for this to fail.
  LEFT JOIN snapshots vs ON vs.id = t.snapshot_id
  LEFT JOIN matches   m  ON m.id  = t.match_id
  LEFT JOIN snapshots cs ON cs.id = m.challenger_snapshot_id
  LEFT JOIN snapshots ds ON ds.id = m.defender_snapshot_id
