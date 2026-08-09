# M1 — the worker contract: one blocker fixed, two gaps open — 2026-08-09

Took up the evening handover's M1 thread ("the sim worker — the last piece
before an end-to-end cloud fight"). Reading the API's actual worker
contract against `MatchRunner`, `RobotSnapshot` and
`Multiplayer_V3_Design_Doc.md` §5.2/§5.3 turned up three gaps before a
line of worker code was written. **Gap 1 was blocking; it is now fixed and
proven. Gaps 2 and 3 are open.**

## Where M1 stands

| M1 deliverable (design doc §8) | state |
|---|---|
| API v0 — register/login, `POST /robots`, `POST /snapshots`, status | **done**, local only |
| Postgres queue, SKIP LOCKED + heartbeat + retry | **done**, and it is good work |
| `/v1/worker/jobs/claim`, `/heartbeat`, `/validate-result` | **done** |
| Claim response resolves to a payload | **FIXED TODAY** — see below |
| Category assignment | **missing on both sides** (gap 2) |
| FIGHT endpoints — match create, match result, replay upload | **do not exist** (gap 3) |
| Worker image (Linux Dedicated Server + MatchRunner, Docker) | not started |
| Docker image built | never — owen has no Docker |
| GCP project + billing alert + deploy | scripts exist; Secret Manager entries do not |
| Client login + ENLIST UI | not started — zero `UnityWebRequest` in the client |

M0 is complete (snapshot envelope, replay recorder/player, MatchRunner,
ReplayBench) — though ReplayBench has not run since 08-08.

## ✅ GAP 1 — FIXED: the claim response did not say what to fetch

`Sql/claim_job.sql` returned `id, kind, match_id, snapshot_id, attempts`
and nothing else, so a worker claiming a VALIDATE job was handed **a UUID
and no way to resolve it**:

- `snapshots.storage_url` was written at upload and **read by no code path**.
- `IBlobStore.GetAsync` existed, was correct, and **was called by nothing**.
- `GET /v1/snapshots/{id}` withholds the payload URL on purpose, and says
  so — *"the only thing that ever reads a payload is a worker"* — while no
  worker endpoint served one.

**The worker could not fetch the thing it had been asked to validate.**
Not a bug in any one file; every piece was individually right. The seam
between them was never closed, and nothing noticed because
**`api_smoke.sh` tested each endpoint alone and no consumer had ever tried
to complete a job.** 35/35 green, and the first real consumer unable to
function — the morning handover's own lesson (*"a bench that passes on
nothing is worse than one that fails"*) arriving from the other direction.
An endpoint suite that never runs the loop it exists to serve is testing
its own reachability.

### The fix, shipped

Backups: `_claude_backups/m1claim__{claim_job.sql,Infra.cs,api_smoke.sh,run_local.sh}`.

**`Sql/claim_job.sql`** — the `UPDATE … RETURNING` moved into a
data-modifying CTE, joined out to storage. §5.1 has workers fetch from
storage rather than through the API, so the claim carries the **location**,
not the bytes:

```
SELECT t.id, t.kind, t.match_id, t.snapshot_id, t.attempts,
       vs.storage_url, vs.sha256,        -- VALIDATE
       cs.storage_url, cs.sha256,        -- FIGHT challenger
       ds.storage_url, ds.sha256,        -- FIGHT defender
       m.arena, m.seeds
```

Three deliberate choices:

- **Columns 0–4 unchanged and in the same order.** `sql_bench.sh` parses
  `^[0-9]+\|` and cuts field 1; `api_smoke.sh` does `jget id`. The change
  is purely additive so neither bench needed adjusting to keep passing —
  which is what makes their passing evidence rather than accommodation.
- **`LEFT` joins, not inner.** The `match_jobs` CHECK guarantees a
  VALIDATE job has no `match_id` and a FIGHT job no `snapshot_id`, so
  exactly one side is NULL by construction. An inner join would return
  **no row at all**, which a worker reads as "queue empty" — a silent
  stall, the worst available failure mode.
- **`sha256` rides along.** The envelope carries its own hash and
  `RobotSnapshot.Open()` already refuses a payload whose hash does not
  match, so the worker can verify what storage handed it without trusting
  storage *or* the API. That check existed and nothing could reach it.

FIGHT jobs get both snapshots plus `arena` and `seeds` in the same edit —
written while the shape was obvious, though gap 3 means nothing can use
them yet.

**`Infra.cs`** — `ClaimedJob` gains eight fields; reader updated
(`seeds` via `GetFieldValue<int[]>`).

**`tests/api_smoke.sh`** — the two checks whose absence let this ship:
a claimed VALIDATE job must carry a `payloadUrl`, and a `payloadSha256`
of 64 hex chars. Every other check in section F confirms the endpoint
*answers*; none confirmed the answer was *usable*.

### ⚠ And the tooling gap the fix exposed

`run_local.sh` — advertised as *"one command: reset DB, build, boot, run
smoke"* — **did not run `sql_bench.sh`**. So a change to
`RobotBrawl.Api/Sql/*.sql`, the files sql_bench is the only cover for,
could come back all-green from the one command having never been tested
where it counts. The first `run_local.sh` after this patch did exactly
that: api_smoke 35/35 → **37/37**, proving a *single* worker's claim,
while the **four-concurrent-worker SKIP LOCKED** case — the one a CTE
rewrite could plausibly disturb — sat untested.

`sql_bench.sh` is now part of `run_local.sh`: after boot, because the
API's migration creates the schema; before api_smoke, because sql_bench
truncates every table and api_smoke builds its own fixtures.

### Verification, prediction first

Predicted: build clean · `sql_bench` still 34/34 · `api_smoke` 37/37 with
both new checks passing, because the upload path already wrote
`storage_url` and `sha256` — only the plumbing was missing.

**Measured, all three: exactly that.**

- `qa_sql_bench.txt` — **34/34, 0 failed**, including section D:
  *"4 concurrent workers each claimed a job"* and *"no job was claimed
  twice (4 distinct of 4)"*. The CTE rewrite left claim behaviour under
  contention untouched.
- `qa_api_smoke.txt` — **37/37, 0 failed, 0 skipped**, with a real
  resolvable location:
  `file:///tmp/rb-blobs/snapshots/9ee74bbf-…/ef9d9cc7….json`.
- `qa_api_server.log` — **0 lines mentioning exception or error.**

## ⚠ GAP 2 — nothing computes a robot's category, on either side

`ValidateResult` requires `Category`; §1.2 requires it be server-computed
from the validated snapshot, never client-reported. But
`RobotSnapshot.Describe()` returns legal / massKg / aabb / partCount /
programHash / partsManifest / failReasons — **and no category**, with a
comment saying *"category assignment lives server-side in M1"*.

The worker **is** the server side, and has nothing to compute from. The
schema names the categories — `snapshots.category` CHECKs against
`('FEATHER','LIGHT','MIDDLE','HEAVY','SUPER')` — but the **caps and size
boxes exist only in the design doc** (§1.2: Feather 1,500 → Super 5,500).
The client's `CareerDB` has league `weightCap`s under different names and
**no size boxes at all**.

So this is a new build, not a port: an authoritative table plus "smallest
category satisfying BOTH cap and box". Small, fully specified, benchable
in-editor with no server, on the critical path for every validate job.
**It is the piece of the Unity half that is unblocked right now.**

Trap while building it: category must derive from the **validated** mass
and aabb, and a re-upload that changes category **resets rating to
placement** (§1.2). A wrong boundary silently wipes ratings.

## ⚠ GAP 3 — the FIGHT half of the contract does not exist

The schema has `matches` and `match_jobs.kind = 'FIGHT'` with a CHECK
enforcing job shape. The API has **no** match-create endpoint, **no**
match-result endpoint, and no replay upload path. §5.3's four-step
lifecycle is written prose; none of steps 1–4 exists in code.

Even with gap 1 fixed, a worker can run **VALIDATE jobs only** — which is
the right first target anyway: it is the half the API fully supports, and
it is §5.2's *one implementation, zero drift* argument made real.

## Recommended order

1. ~~Patch the claim contract~~ — **done and proven.**
2. **Category assignment + bench** in Unity. Unblocked.
3. **The VALIDATE worker loop** — claim → read storage → `Open()` (hash
   verified) → `Describe()` → category → POST validate-result →
   heartbeat. Provable in-editor against the local API. No Docker, no GCP.
4. Only then: FIGHT endpoints, replay upload, Docker, deploy.

## Method notes

**Read the consumer's contract before building the consumer.** "The sim
worker is the last piece" was a statement about *intent*, not about
whether the pieces connect. Twenty minutes reading the claim SQL,
`ClaimedJob`, `IBlobStore`'s call sites and `Describe()`'s return type
found one blocker and two missing halves. Writing the worker first would
have found the same things later, with code already built around the
wrong shape.

**"Done and proven" needs to name what it was proven to do.** The API is
well built and its 35/35 was genuinely green. What that number proved is
that each endpoint answers correctly *in isolation*. It was never evidence
that a worker could complete a job — and it was reasonable to read it that
way only because nobody had written down which claim it supports.

**A green suite that had to be edited to stay green is weaker evidence.**
Keeping the claim's first five columns in place meant `sql_bench` and
`api_smoke` passed *unmodified* except for the two assertions that were
missing. Had the parsing needed adjusting, the passes would have been
partly a statement about the edit.

**One command should mean one command.** `run_local.sh` excluded the only
bench covering the files most likely to be edited by hand. That is not a
missing test — the test existed and was green — it is a missing *route to*
the test, which is harder to notice and just as load-bearing.
