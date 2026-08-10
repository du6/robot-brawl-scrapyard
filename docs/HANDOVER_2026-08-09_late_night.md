# HANDOVER — late night, 2026-08-09. Read this first.

**Supersedes `HANDOVER_2026-08-09_night.md` entirely.** That document's §3
("THE NEXT TASK, and it is not optional: write GcsBlobStore") is **done**.
Its §6 traps are all still true and still worth reading.

49 commits on the day, `651d567..HEAD`. Working tree clean apart from
`_claude_backups/` and `career_backups/`. Career save
`18614d0e6603869f28fb65992b7d1484`, **mtime still 2026-08-05 21:57** — the
stronger check, and it says the file was never written across every play-mode
run today.

---

## 0. Do this first

```
RobotBrawl.Phase0.CategoryBench.RunPure()      // expect 44 pass, 0 fail
```

Ten seconds, and it gates everything Unity-side. Verified green at the end of
this session.

⚠ **Reflection is blocked by the MCP bridge.** `System.Reflection.BindingFlags`
is refused as an unauthorized namespace — call bench methods directly. And
`Unity_GetConsoleLogs` came back empty for a bench that had just logged 44
lines; capture output with `Application.logMessageReceived` inside the command
instead.

---

## 1. What is live

**https://rb-api-902243335343.us-central1.run.app** — API, database, blobs and
alerting are all up.

| | |
|---|---|
| Cloud Run | `rb-api`, rev `00005-95x` |
| blobs | **GCS**, `robot-brawl-ladder-snapshots`, via S3 interop + HMAC |
| database | Cloud SQL `rb-db`, unix socket, no public IP, **deletion protection ON** |
| backups | daily 09:00 UTC, 7 retained. **PITR is OFF** — see §4 |
| alerting | 4 policies + an uptime check, `scripts/gcp_monitoring.zsh` |
| deploy | `scripts/deploy_api.zsh` — **now runs end to end**; it never had |

Both scripts are **idempotent** — proven by running each twice and checking
the second pass creates nothing.

---

## 2. Green, measured today

| bench | |
|---|---|
| `api_smoke.sh` | **185/185** local · **174/174 against real GCS**, 0 skipped |
| `sql_bench.sh` | **48/48** |
| `restore_drill.sh` | **10/10** |
| `CategoryBench` | 44/44 |
| `LadderLiveBench` | **11/11** (new) |
| `WorkerBench` · `FuzzBench` · `FightWorkerBench` · `LadderClientBench` | 39 · 25 · 21+26 · 18 |

`TRUST_PROXY=1 zsh server/tests/run_local.sh` runs the first three in one
command, in **13 seconds**, and leaves the reports on disk.

---

## 3. The thing worth carrying: green is not evidence

Four defects shipped today that **every bench passed**, because all four
depended on the process being the whole world — true on a laptop, false in
the cloud. `docs/Cloud_Only_Defects_2026-08-09.md` is the record. In short:
ephemeral disk that returns 200 and loses the bytes; `Request.Scheme` lying
behind TLS termination so workers got `http://` URLs that 302 and drop their
auth header; a rate limiter bucketing **every caller on earth together**
because the proxy is the only address it can see; and a snapshot left PENDING
forever when its job died.

⚠ **`ForwardLimit = 1` reads the RIGHTMOST header value on purpose.** Cloud
Run appends the real client to whatever the caller sent, so the rightmost is
the platform's and cannot be forged. Reading the leftmost would let anyone
set their own IP and evade the limiter entirely. Do not "fix" this.

And **four of my own checks were wrong before the product was**:

- a log-shape regex using `[0-9]*`, which matches the **empty string**
- that same check then passing on a row of **all zeros**, while the database
  held 15 DONE and 4 FAILED jobs
- `?category=FEATHER` on an endpoint routed as `/{category?}` — I very nearly
  filed a working category filter as a bug
- `psql` truncating a table named `jobs` that does not exist (`match_jobs`),
  silently, because I had hidden its stderr

House rule 2 four more times. **Before believing a red result, verify the
check — and before believing a green one, ask what it would do if the feature
were absent.**

---

## 4. Owen's calls, not the next session's

1. **Point-in-time recovery on `rb-db` is OFF.** Daily backups are on and the
   restore drill passes, but a restore loses up to 24h. It costs WAL storage
   against the $25/mo budget, which is why it was not simply switched on.
2. ~~**Did the alert email arrive?**~~ ✅ **RESOLVED — owen confirmed
   delivery.** The whole chain is now proven end to end by a real drill: a
   deliberately stuck job → the reaper's `rbmetrics` line → the regex
   extractor → the log-based metric → 900s threshold held for 8 minutes →
   the policy → the email channel → owen's inbox. **Cloud Monitoring does
   not expose incidents through its public API**, so this last link cannot be
   checked from a shell — it needs a human, and it got one. The fixture has
   been retired and the production queue is clean.

   ⚠ **Keep this in mind before "improving" the alerting**: the only way to
   know a policy actually delivers is to make something fail on purpose and
   ask whoever owns the inbox. A policy that exists, is enabled, and has a
   channel attached is *not* evidence that anyone will ever be told.
3. **ARENA layout.** A clipped `scout` button and a stray scrollbar are fixed;
   the panel is still OnGUI placeholder, still tall and mostly empty, still
   shows the base URL in its header, and shows two robots both called "Smoky"
   with nothing to tell them apart. Screenshots were sent.

---

## 5. Then, in the order I would take them

1. **`docker compose up` is still unproven** — needs `brew install
   docker-compose`. The image itself builds and deploys fine.
2. **The worker↔API path still has no end-to-end bench.** `LadderLiveBench`
   covers the *client* against a live server; the *worker* half is still
   hand-run.
3. **The 43% mutual disarm** — the biggest open single-player problem, three
   cheap explanations already ruled out. `OpeningBench` is the template.
4. Season badges, client deposit/shop flows, season history.

---

## 6. Traps added today, on top of the night handover's §6

- **`run_local.sh` finishes in 13 seconds.** Anything on a timer slower than
  that emits exactly once, at startup, against a freshly truncated database —
  which is how a metrics check came to pass on a row of zeros. It now sets
  `Queue__ReapEverySeconds=5`.
- **A dirty dev database silently poisons `api_smoke`.** Running it directly
  rather than through `run_local.sh` had it claim a leftover FIGHT job and
  report 10 failures whose real cause was one stale row.
- **`gcloud builds submit` needs `--service-account` AND
  `--default-buckets-behavior`** on a new project — the legacy build account
  no longer exists, and the error names a principal you do not have.
- **`unit: 1` in a log-metric YAML parses as an int** and crashes gcloud
  inside its own validator. Quote it.
- **`ALIGN_MAX` is refused on a DELTA distribution**, which is exactly what a
  log-based metric with a value extractor produces. Use `ALIGN_PERCENTILE_99`.
