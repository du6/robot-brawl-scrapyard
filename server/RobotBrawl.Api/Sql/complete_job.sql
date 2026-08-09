-- $1 = job id, $2 = worker id. Same ownership rule: a late result from a
-- worker whose job was already reclaimed updates nothing and returns no row,
-- which is how the API knows to discard it (§5.3 step 3).
UPDATE match_jobs
   SET status = 'DONE', claimed_by = NULL, claimed_at = NULL, heartbeat_at = NULL
 WHERE id = $1 AND claimed_by = $2 AND status = 'CLAIMED'
RETURNING id
