-- Claim one job (§5.1). FOR UPDATE SKIP LOCKED is what makes a plain table a
-- safe multi-worker queue: a row another transaction already holds is skipped
-- rather than waited on, so N workers claim N different jobs with no
-- coordination and no broker. attempts increments ON CLAIM, not on failure,
-- so a worker that dies without ever reporting still burns an attempt and
-- cannot retry forever.
-- $1 = worker id
WITH claimed AS (
    SELECT id FROM match_jobs
     WHERE status = 'READY'
     ORDER BY created_at
     FOR UPDATE SKIP LOCKED
     LIMIT 1
)
UPDATE match_jobs j
   SET status = 'CLAIMED', claimed_by = $1, claimed_at = now(),
       heartbeat_at = now(), attempts = j.attempts + 1
  FROM claimed c
 WHERE j.id = c.id
RETURNING j.id, j.kind, j.match_id, j.snapshot_id, j.attempts
