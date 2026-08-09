<!-- Mirrored from the claude.ai Project (path: claude/M1_API_And_Schema_2026-08-08.md) on 2026-08-08.
     The Project copy is the source of truth; edit there and re-mirror. -->

# Multiplayer v3 — M1: schema, queue and API v0 (2026-08-08)

owen created the GCP project and installed Linux Dedicated Server Build
Support, unblocking both halves of M1. This session built the **server**
half. Everything lives in the repo under **`server/`**.

**`tests/sql_bench.sh` — 31/31 green** against a real PostgreSQL 16.

## The honest status line, first

| piece | state |
|---|---|
| Postgres schema (§6) | **written and applied to a real database** |
| Queue + ledger semantics (§5.3) | **31 checks green, including real concurrency** |
| API v0 C# source | **written, NOT compiled** — see below |
| Dockerfile / compose / deploy script | written, not built |
| Sim worker | not started; the Linux module is installed, so it is unblocked |
| Client ARENA tab | not started |

**The C# has never been compiled.** NuGet is unreachable from this cloud
sandbox (`api.nuget.org` refuses the connection), so `Npgsql` and the JWT
package cannot be restored here. The Dockerfile does its `dotnet restore`
*inside* the image build, which is where it was always going to happen
(§7 rule 1) — so the first real compile is `docker compose build` or the
Cloud Build in `scripts/deploy_api.zsh`. **Expect to fix compile errors
on that first build.** Treat the C# as a careful draft; treat the SQL as
tested.

That split is deliberate rather than a consolation: §5.3 calls the match
lifecycle "the one flow that must be airtight", and nearly all of it is
SQL. The parts that are expensive and quiet when wrong are the parts that
are proven.

## What the data layer proves

`tests/sql_bench.sh` runs **the same query text the API ships** — the
four files in `RobotBrawl.Api/Sql/` are loaded by `JobQueue` at boot and
by the bench via `PREPARE`, so a passing bench cannot drift from a broken
server. 31 checks:

**Identity and snapshots.** Case-insensitive email uniqueness (a
generated `email_lower` column, not application lowercasing) · sha256
must be 64 hex chars · **a robot cannot have two ACTIVE snapshots**
(partial unique index, §1.2) but may have any number of superseded ones ·
unknown categories refused.

**The ledger is append-only in the database, not by convention.** UPDATE
and DELETE both raise from a trigger. §2.3 says balance is `SUM(delta)`
and M5 will need this as an audit trail; a rule that lives only in
application code is a rule one bad endpoint deletes. Also proven:
zero-delta rows refused · **`DEPOSIT_TO_CAREER` cannot be positive**, so
the one-way valve is a CHECK constraint rather than a hope · a replayed
deposit is refused by a UNIQUE idempotency key (the M3 acceptance
criterion, enforced now) · balance reads back as 500 − 200 = 300.

**Job shape.** A FIGHT job carrying a `snapshot_id`, or a VALIDATE job
carrying a `match_id`, is **not representable** — CHECK constraint. A
snapshot cannot fight itself. A second *live* job for the same target is
refused by a partial unique index, so a retry reuses the row instead of
racing a twin.

**SKIP LOCKED under real contention.** Four concurrent psql sessions, each
holding its transaction open for 600 ms so the overlap is genuine: all
four claimed, **all four claimed different jobs**, the queue drained to
zero, and each claim burned exactly one attempt. A fifth worker against an
empty queue returned nothing rather than blocking.

**Ownership.** `worker-9` can neither heartbeat nor complete `worker-1`'s
job — the check is in the SQL `WHERE` clause, so a stolen worker key
still cannot steal another worker's result.

**Visibility timeout and retry exhaustion.** Stale jobs return to READY;
after 3 attempts they go FAILED with `last_error` rather than looping
forever; nothing is left live; and a FAILED job does not block re-queueing
its match.

**Escrow is one transaction** (§5.3 step 1): an illegal match rolls the
stake back with it — the balance did not move.

## Design decisions worth keeping

**Attempts increment ON CLAIM, not on failure.** A worker that dies
without ever reporting still burns an attempt, so a job that crashes a
worker cannot retry forever. That is also the fuzz oracle §8/M4 wants.

**Statuses are TEXT + CHECK, not PG ENUMs.** Adding a value to an enum is
a migration; adding one to a CHECK is a migration you can also run
backwards.

**The API reads exactly three fields out of an uploaded envelope** —
`sha256`, `clientVersion`, and the `payload` string it hashes to verify
the first. It understands none of them. Everything else about a robot
comes back from the Unity worker (§5.2).

**Only the owner is told why their own upload was rejected.** Handing a
scout the validator's failure reasons is handing them the build. The
scouting fields (mass, size box, category, parts manifest, program *hash*)
are public; the payload URL is public to nobody — the only thing that ever
reads a payload is a worker.

**Login is constant-work.** A missing account still runs a decoy PBKDF2
verify, so the endpoint is not an account-enumeration oracle.

**The API refuses to boot without `JWT_SECRET` and `WORKER_KEY`.** A
default signing key is a key everyone has.

**Migrations run at boot.** A container that starts is a container with
the right schema; there is no separate deploy step to forget.

## Endpoints in v0

```
GET  /healthz
POST /v1/auth/register        -> token, and the §2.3 signing bonus in the
                                 same breath, so "registered" and "can
                                 afford a challenge" are one state
POST /v1/auth/login           -> token
POST /v1/robots               -> create        GET /v1/robots -> mine
POST /v1/snapshots            -> stores the opaque payload, inserts the
                                 snapshot row AND its VALIDATE job in one
                                 transaction
GET  /v1/snapshots/{id}       -> the scouting card (anonymous-readable)
POST /v1/worker/jobs/claim            (X-Worker-Key)
POST /v1/worker/jobs/{id}/heartbeat   (X-Worker-Key)
POST /v1/worker/jobs/{id}/validate-result  (X-Worker-Key)
```

Rate limits are in place on the auth and upload paths (§5.5), and the
snapshot upload is capped at 256 KB — about 10× what the v2 program caps
allow, and small enough that no upload can tie up a worker.

## A bug this session caught in its own test, not its product

The first run of `sql_bench.sh` reported a **double claim** — two workers
taking the same job, which would have been a serious defect. It was the
bench: an unquoted bash heredoc had eaten the `$1`/`$2` placeholders in
the prepared statements, so every worker ran a broken query. The harness
now builds its runner files by concatenation and passes worker ids as psql
variables, and there is a comment in the file saying why.

This is the house rule again — *when a check fails, ask whether the CHECK
is wrong before changing the product* — and it is now three for three this
week.

## Next

1. **`docker compose up`** on owen's Mac (it has Docker and a network).
   That is the first real compile of the C#; expect and fix errors there.
   Then `bash tests/sql_bench.sh` against the composed database to confirm
   the schema applies through the boot-time migrator too.
2. **The sim worker** — now genuinely unblocked. Linux Dedicated Server
   build of the game with a `MatchRunner` boot scene, in Docker, running
   the validate-job and fight-job loops against these endpoints. Start on
   **Mono**, measure, and only reach for IL2CPP if managed time turns out
   to be material — the worker is CPU-bound on PhysX either way, and
   `MatchRunner` already reports `simSeconds` per bout to measure it with.
3. **Client login + ENLIST**, once there is a URL to point it at.
4. `scripts/deploy_api.zsh` expects three Secret Manager secrets —
   `rb-jwt-secret`, `rb-worker-key`, `rb-pg-conn`. They do not exist yet;
   creating them is a `gcloud secrets create` away and deliberately not in
   the bootstrap script, because secrets should not be created by a script
   that gets re-run.
