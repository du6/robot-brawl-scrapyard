-- $1 = job id, $2 = worker id. Ownership is in the WHERE clause: a worker
-- cannot keep alive a job the reaper has already taken from it.
UPDATE match_jobs SET heartbeat_at = now()
 WHERE id = $1 AND claimed_by = $2 AND status = 'CLAIMED'
RETURNING id
