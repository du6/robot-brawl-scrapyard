# Launch check — 2026-08-19

Run because the previous check missed two things that were **visible only in
the running system**, not in the code: a worker costing ~$51/mo to do nothing,
and that same worker paused since 08-14 so no enlisted robot would ever be
fought. This pass therefore verifies live state and treats every documented
claim as unproven until measured.

Everything below was checked today. Where I could not close something, it says
so — a skip is not a pass.

---

## 1. BLOCKERS — fix before the app is released

### B1. The database cannot serve the API at scale. ⚠ THIS IS THE BIG ONE.

| measured | value |
|---|---|
| Cloud SQL tier | `db-f1-micro`, **no `max_connections` flag set** |
| documented default for that tier | **~25 connections** |
| Cloud Run `maxScale` | **4** |
| Cloud Run `containerConcurrency` | **80** (so up to 320 in-flight requests) |
| connection string pooling keys | **none** → Npgsql default `MaxPoolSize=100` **per process** |

Four instances each willing to open 100 connections against a database that
accepts about 25. Under any real launch traffic this fails as
`too many clients already` / connection timeouts — and it fails on the
*database* side, so every endpoint dies at once, including sign-in.

It has never shown up because traffic has been one developer. Launch is
exactly the event that produces it.

**Fix (cheapest, no tier change):** bound the pool in `rb-pg-conn` so the
fleet cannot exceed the ceiling — `Maximum Pool Size=5` gives 4 × 5 = 20
against ~25, leaving headroom for the worker and for `psql`. Add
`Timeout=15;Command Timeout=30` while there. New secret version + redeploy.

**Alternative:** raise the tier. `db-g1-small` roughly doubles the ceiling and
costs about $18/mo more — worse value than bounding the pool.

⚠ I could not query `max_connections` directly: the instance refuses external
connections (authorized-networks is empty) and the Cloud SQL
`num_backends` / `max_connections` metrics returned **no data** for it, so the
25 is Google's documented default for the tier, not a reading. Bounding the
pool is correct at any plausible value.

### B2. Unsubscribe can be blocked by unrelated signup traffic. Proven today.

`POST /v1/subscribers/unsubscribe` shares the `subscribe` rate-limit bucket
(20/min **per IP**). Measured, on production, by accident:

* 24 rapid signups from one address → the 21st onward correctly `429`
* the 24 unsubscribe calls that followed → **all 429**, none processed

So a burst of signups from one NAT can stop someone else behind that NAT
removing themselves from a mailing list. That is the one action that must
always work: a broken unsubscribe is what gets a sending domain blocklisted,
and the site's own copy promises "one-step unsubscribe".

**Fix:** its own bucket, generous (unsubscribing is not an abuse vector), or
exempt the route from limiting entirely.

---

## 2. HIGH — drift introduced by today's own change

### H1. The "jobs are not being worked" alert now fires before its own safety net.

The alert trips when the oldest READY job exceeds **900 s** for 5 minutes.
Until today the scheduler ran every 5 minutes, so 900 s meant "something is
badly wrong". Now the API starts the worker on demand and the schedule is a
**30-minute** safety net — so a single missed nudge produces a job that waits
up to 30 minutes, trips the alert at ~15, and is then collected automatically.

That is an alert for a condition that self-heals, and alerts that cry wolf get
muted. **Raise the threshold above the safety-net period — 2100 s.**

---

## 3. Documentation that is now false

The class of error that caused the last miss. Each verified against the live
system today.

| doc says | reality |
|---|---|
| bundle id `com.owen.robotbrawl` (`LAUNCH_CHECKLIST_2026-08-10`) | **`club.cyberduck.robotbrawl`** |
| build **8** waiting for review (`HANDOVER_iOS_Launch_2026-08-16`) | **build 9**, uploaded 08-17, attached to version 1.0 |
| "PITR is **OFF** — a restore loses up to 24h" (both docs, `CLAUDE.md` "not done" #1) | **PITR is ON.** Better recovery, and WAL storage is a real cost the docs say was deliberately declined |
| worker cadence "5 min = the settlement latency ceiling" | on-demand nudge; the 30-min schedule is now only a safety net |
| "an always-on worker ≈ $35/mo" was the reason for the 5-min tick | the 5-min tick itself measured **~$51/mo**, worse than the thing it avoided |

---

## 4. Verified good — measured today, not assumed

**App Store (via the ASC API, read-only)**
* Version **1.0 — WAITING_FOR_REVIEW**, build 9 attached, `releaseType=MANUAL`
  → ⚠ **approval does NOT auto-publish; someone must press Release.**
* Age rating 9+; export compliance answered on build 9
  (`usesNonExemptEncryption=false`); build expires 2026-11-15
* Metadata complete: description, keywords, promotional text,
  marketing URL `https://cyberduck.club`, support URL, privacy policy URL
  `https://cyberduck.club/privacy/` (a page updated today to cover the mailing
  list — it is current)
* Screenshots: 5 × iPhone 6.5", 5 × iPad Pro 12.9"
* `whatsNew` empty — correct for a 1.0; no subtitle set (a missed marketing
  slot, not a blocker)

**Monitoring — the chain the handover warns is fragile is intact**
* All 4 alert policies enabled, each with a notification channel
* Channel: email to `leondu167@gmail.com`, enabled
* The alerts are a **regex over log text** (`textPayload:"rbmetrics"`), so the
  real question is whether data is arriving. It is: the API emits `rbmetrics`
  every ~30 s and all four metrics carry **182 points in the last 3 hours**
* Uptime check hits `/healthz/` **with the trailing slash** — the documented
  trap is still respected

**Database**
* Automated backups daily 09:00 UTC, 7 retained; the last three all SUCCESSFUL
* PITR on (see §3)

**API security surface** (unauthenticated, against production)
* `401` on `/v1/inbox`, `/v1/wallet`, `/v1/robots`, `/v1/admin/subscribers`,
  `/v1/admin/metrics`
* Admin export with a wrong worker key → `401`
* Public by design → `200`: `/v1/leaderboard/{cat}`, `/healthz/`
* Subscribe rate limiter fires (`429`) as designed

**The on-demand worker, proven end to end on production**
```
02:27:34  snapshot accepted        → VALIDATE enqueued
02:27:34  [nudge] worker started   ← the API's own log
02:27:34  rb-worker-xzzrd created  ← first execution since 08-14
02:29:03  succeeded
```
Queue `done` 62 → 63, and the snapshot came back **REJECTED — "Needs at least
1 wheel."** That is the real game validator running in the container, not a
stub.

**Website** — every page and asset returns 200: `/`, `/robot-brawl/`,
`/champions/`, `/support/`, `/privacy/`, `/unsubscribe/`, `404.html`, plus
CSS, both scripts, both share images, both data files and the promo video.

**Season** — Season 1 live, `rb-season-rollover` ENABLED, last ran 08-18
09:30. **Season 1 ends 2026-09-09T22:21:38Z — put it on a calendar**; rollover
is two commands in order (`docs/Seasons_Live_2026-08-12.md`).

---

## 5. NOT closed — and nobody can close these from a desk

* **The production URL on a real device.** Still verified by inspection and by
  one Simulator observation. `CLAUDE.md` is explicit that the simulator cannot
  testify about runtime build flags. First device install must confirm the app
  talks to `rb-api-…run.app` and not localhost.
* **The FIGHT path on production since the nudge change.** VALIDATE is proven
  above. The challenge path calls the *identical* nudge line after its own
  commit, and `EnlistLiveBench` covers the full fight against a dev API — but a
  real production fight has not been run today.
* **The Unity benches were not re-run in this pass.** Last recorded state is in
  `CLAUDE.md`. Treat any bench you have not personally seen a PASS COUNT from
  as unmeasured.
* **Nobody has used the site on a real phone.** Layout is verified at 390 px in
  an iframe, which reproduces layout but not WebKit.

---

## 6. Cost, since it started this

Post-fix run rate is **Cloud SQL ~$12/mo and little else**: the worker now
costs nothing while idle, Cloud Run scales to zero, builds sit inside the free
tier, and all storage together is about $0.10.

⚠ **PITR is on and the docs say it is off** (§3). That is WAL storage nobody
has priced. If the bill does not fall as expected after this deploy, look there
first.

Two claims I made during the billing investigation that were **wrong**, kept
here because the reasoning is the useful part:

1. *"Nothing uses the Cloud SQL public IP."* Wrong. Authorized-networks governs
   direct clients; Cloud Run's `/cloudsql/` socket reaches the instance over
   that same public IP through the Auth proxy. GCP refused the change and was
   right to. Removing it needs Private IP or PSC first, and a Serverless VPC
   connector costs ~$8/mo against the ~$2.90 saved. **Leave it.**
2. *"The worker has never run."* Wrong — the scheduler's empty
   `lastAttemptTime` misled me; the job has many successful executions up to
   08-14.
