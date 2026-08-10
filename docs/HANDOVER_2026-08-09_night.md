# HANDOVER — end of 2026-08-09. Read this first.

**Supersedes `HANDOVER_TO_CLI.md` on everything below. That document is still
correct on the hard rules and the editor discipline — read §2 and §3 of it,
skip its status tables, which this replaces.**

42 commits today, `651d567..HEAD`. Working tree clean apart from
`_claude_backups/` and `career_backups/`. Career save
`18614d0e6603869f28fb65992b7d1484`, mtime 2026-08-05 21:57 — **untouched all
day, across dozens of play-mode runs**.

---

## 0. The one thing to do first

**Verify the editor, exactly as the previous handover said.** It takes ten
seconds and it is the gate on everything Unity-side:

```
RobotBrawl.Phase0.CategoryBench.RunPure()      // expect 44 pass, 0 fail
```

Both edit mode and **play mode** are proven from a CLI session now. Play-mode
method and its traps: `docs/Play_Mode_From_CLI_Proven_2026-08-09.md`.

---

## 1. What is live

**https://rb-api-902243335343.us-central1.run.app** — the ladder API is
deployed and working.

| | |
|---|---|
| Cloud Run | `rb-api`, rev `00001-r8z`, min 0 / max 4, ~$0 idle |
| image | `us-central1-docker.pkg.dev/robot-brawl-ladder/rb/api:20260809-205026`, amd64 |
| database | Cloud SQL `rb-db`, POSTGRES_16, `db-f1-micro`, **unix socket, no public IP** |
| secrets | `rb-jwt-secret`, `rb-worker-key`, `rb-pg-conn` in Secret Manager |
| budget | `robot-brawl 25/mo`, armed at 50/90/100% + 100% forecast |
| **cost** | **~$10–15/mo, all of it the database.** Nothing else is always-on |

`docs/GCP_Live_2026-08-09.md` has the deploy detail and the three IAM gaps
that each failed once.

---

## 2. Green, measured today

| bench | |
|---|---|
| `sql_bench.sh` | **45/45** |
| `api_smoke.sh` | **166/166**, 0 skipped |
| `restore_drill.sh` | **10/10** |
| `CategoryBench` | 44/44 |
| `WorkerBench` | 39/39 |
| `FuzzBench` | 25/25 |
| `FightWorkerBench` | 21/21 play-mode + 26/26 pure |
| `LadderClientBench` | 18/18 |
| `glicko_check` | matches Glickman's published example |

`zsh server/tests/run_local.sh` runs the first three in one command and leaves
the reports on disk.

---

## 3. THE NEXT TASK, and it is not optional

**Write `GcsBlobStore`.** Everything else is polish; this one makes the
deployment usable.

`Program.cs:59` registers `FileBlobStore` writing to `BLOB_ROOT`. On Cloud
Run that path is **ephemeral and per-instance**:

- `min-instances=0` → the disk vanishes when the service scales down. An
  uploaded robot's payload is **gone**, and its VALIDATE job can never be
  worked.
- more than one instance → a worker may fetch from an instance that never had
  the payload.

**The upload returns 200. That is the whole problem** — it looks like it
worked, and the failure appears later, somewhere else, to somebody else.

`gcp_bootstrap.zsh` already created `robot-brawl-ladder-snapshots` and
`robot-brawl-ladder-replays`. **Nothing uses them.** The `IBlobStore`
interface (`Infra.cs:126`) is the seam — implement it over GCS, register it
when a bucket env var is present, keep `FileBlobStore` for local runs so
`run_local.sh` needs no network.

Replays also need **signed URLs** (§5.1), not public objects. Do not add
`allUsers` to the replays bucket.

---

## 4. Then, in the order I would take them

1. **`deploy_api.zsh` is wrong** and has never run end to end. It lacks
   `--service-account`, `--add-cloudsql-instances`, and the three IAM grants.
   Fix it by running it, not by reading it.
2. **ARENA styling.** Four of five surfaces exist and work
   (`ArenaScreen.cs`); it is OnGUI placeholder and nobody has judged the
   layout. Screenshot with `UiShot.Take(path)` — see §6.
3. **The end-to-end runs are hand-run, not benches.** The worker↔API path has
   no regression cover.
4. **`docker compose up`** needs `brew install docker-compose`; the compose
   `db` service is unproven.
5. Monitoring/alerting (queue depth, job failure rate, heartbeat age), season
   badges, client deposit/shop flows, season history.

---

## 5. Open questions that are owen's, not the next session's

- **Season payout figures** (300/150/100) are mine, not the doc's. §2.3 gives
  no numbers. All four are `ladder_config` dials.
- **Cosmetic prices** (60–400 against a 500 signing bonus) likewise.
- **The taper is a cliff, not a ramp** — §2.2 is ambiguous; recorded in
  `docs/Defender_Protection_Shipped_2026-08-09.md`.

---

## 6. Traps that cost real time today

- **`/healthz` is intercepted on this network.** It returns a Google 404 with
  no `server` header while `/healthz/` and `/HEALTHZ` return the app's real
  200. Not the app, not Cloud Run. `deploy_api.zsh` prints that exact curl as
  its success check.
- **OnGUI never appears in the MCP capture tools** — they render from a
  camera and IMGUI is not drawn by one. Use
  `RobotBrawl.Phase0.UiShot.Take("/tmp/x.png")` in play mode, then read the
  file. It must run after `WaitForEndOfFrame` or it returns black.
- **A script edit made during play mode does not compile until play mode
  ends.** Two bench runs reported stale results before I noticed.
- **`api_smoke` re-run inside a minute shows 429s** — the auth limiter is
  10/min per IP. Its own header says so. This cost two false diagnoses.
- **`set -o pipefail` + `cmd | grep -q ERROR`** returns the *left* command's
  status. psql exits 1 exactly when a statement is correctly refused, so the
  check failed when the database was healthy.
- **zsh does not word-split unquoted variables.** `for t in $TABLES` under
  zsh iterates once. Use arrays.

---

## 7. The pattern worth carrying

Six defects today were found by something other than the feature under test,
and **four red results were the check's fault, not the product's**:

- the replay URL pointed at a scorecard — found by building the launcher
- `partsManifest` shipped as a quoted string — found by rendering it
- a snapshot with `abc,def,ghi` crashed any worker — found by fuzzing
- settlement recomputed the stake — found by needing league nights
- a zero-value refund, whose rollback **hid** a purse paying out unstaked
- the season payout key collided across categories — caught by a unique index

House rule 2 earned its place four times over. **Before believing a red
result, verify the check.** And before believing a green one, ask what it
would have done if the feature were absent — the vacuous-pass trap appeared
twice, once in a floor check that passed on `drop = 0`, once in an
affordability check that broke the moment the economy started paying out.
