# iOS Launch Handover — monitor & debug after the iPhone/iPad launch
**2026-08-16.** Written the day after submission, while every fact below was
verifiable. This is the operating manual for the launched game: what is live,
how to tell it is healthy, and what to do when it is not. The Android port is
NOT this document — it exists (`build/android/`, `BuildAndroid.cs`) and stops
at "debug-signed .aab + .apk built, no Play Console yet".

---

## 1. What is submitted

| fact | value |
|---|---|
| App | Robot Brawl: Bolt & Blade, **app id 6801680303** |
| ASC account | Yuelin Du (leondu167@gmail.com), team `BMLXB23PHU` |
| Version / build | ⚠ **STALE — this said build 8.** 08-17: build **9** replaced it. 08-18: build **10** replaces build 9, carrying `GUSSET_SEAM_MULT = 4` so the client agrees with the live worker (`docs/Gusset_x4_2026-08-18.md` §6). owen's call, taken 08-18: **swap into the 1.0 submission**, accepting the queue reset, rather than holding ×4 for 1.0.1 |
| Age rating | 9+ (driver was CONTESTS frequency, not violence — set Infrequent, honestly: the ladder is opt-in) |
| Price / regions | Free, 175 regions |
| Review demo account | `appreview@cyberduck.club` / `password123` — exists on production, login verified 200 |
| Encryption | "None of the algorithms mentioned above" — HTTPS-only exempt. **Apple asks this PER BUILD**; answer it in TestFlight → Manage before a new build can attach |
| Build 8 delivery UUID | `88fa136d-aa0b-4dd3-9b04-d2f90d4f80e0` |

**Why build 8 and not 7:** build 7 was originally submitted; it carries the
post-live-fight bug (BACK TO THE ARENA lands on the BUILD tab — the
reviewer would likely have hit it, since the review notes walk them through a
challenge). Build 8 adds the `pendingArenaReturn` deferral fix and the
tip-strip safe-area fix. The swap procedure that was used, for next time:

1. TestFlight tab → new build → **Manage** → answer encryption → *Ready to Submit*.
2. Version page → banner link **"remove this version from review"** → confirm.
   The version flips to **"Developer Rejected" — this is Apple's normal label
   for a developer-pulled submission, not a rejection.** Metadata survives.
3. Build section → hover the row → red ⊖ → **Add Build** → pick the new one → **Save**.
4. **Add for Review** → **Submit for Review**. Queue position resets; if the
   old submission was hours old this costs nothing.

**On approval:** check the version page's release option before assuming
anything — whether 1.0 auto-releases on approval or waits for a manual
"Release" click was not re-verified at submission time. If it is manual,
releasing is owen's press.

---

## 2. Production topology

```
iPhone/iPad app ──HTTPS──▶ rb-api (Cloud Run, us-central1)
                              │ unix socket /cloudsql/…
                              ▼
                           rb-db (Cloud SQL Postgres — NO public IP)
                              ▲
   rb-worker (Cloud Run JOB, │ drains queue & exits)
   nudged on demand by the API; Cloud Scheduler every 30 min is the net
   replays/blobs ──▶ GCS
```

| thing | value |
|---|---|
| GCP project | `robot-brawl-ladder`, region `us-central1` |
| API URL | `https://rb-api-902243335343.us-central1.run.app` |
| Secrets | Secret Manager: `rb-jwt-secret`, `rb-worker-key`, `rb-pg-conn`, `rb-s3-key/secret` |
| Sessions | JWT **30 days** (720h). Client (`LadderClient`) retires the session on any 401 (`SessionExpired`) and re-login restores everything — arena history is server-side (`ReturningPlayerBench` + journey J9 both prove it) |
| DB backups | daily 09:00 UTC, 7 retained; `restore_drill.sh` passed 10/10. ⚠ **THIS ROW SAID "PITR is OFF" AND IT WAS FALSE** — checked against the live instance 08-19: **PITR is ON**. Better recovery than documented, and WAL storage is a real cost that three documents record as deliberately declined. If the bill does not fall as expected, look here first (`docs/Launch_Check_2026-08-19.md` §3) |
| Worker cadence | ⚠ **STALE.** The API now NUDGES the worker on demand (`WorkerTrigger`, DB-backed debounce), and the Cloud Scheduler tick is a **30-minute safety net**, not the latency ceiling. The 5-min tick was avoiding an "always-on worker ≈ $35/mo" and itself measured **~$51/mo** — worse than the thing it avoided. The queue-staleness alert was raised 900s → 2100s to match |
| Season | **Season 1 ends 2026-09-09T22:21:38Z.** Rollover deploy is TWO commands IN ORDER, owen's go — `docs/Seasons_Live_2026-08-12.md`. Put this date on a calendar |

Deploy scripts (`server/scripts/`): `deploy_api.zsh`, `deploy_worker.zsh`,
`deploy_worker_live.zsh`, `deploy_season_scheduler.zsh`, `build_worker.zsh`
(clone-builds the Linux player — NEVER owen's copy), `gcp_bootstrap.zsh`,
`gcp_monitoring.zsh`. All idempotent. **Production deploys are owen-gated —
auto-mode never deploys** (`docs/AUTO_MODE_2026-08-15.md`).

---

## 3. Alerting — what fires and what it means

`gcp_monitoring.zsh` (safe to re-run) created an email channel to
**leondu167@gmail.com**, an uptime check, and four alert policies over
log-based metrics extracted from the reaper's `rbmetrics` stdout line
(`Infra.cs:418`).

⚠ **Those four all watch the QUEUE.** None of them notices the API returning 500s, answering slowly, exhausting the database pool or hitting its instance ceiling — most of what goes wrong on a launch day. **Six more policies were added 08-19 on Cloud Run's and Cloud SQL's BUILT-IN metrics**, which unlike the log-regex ones cannot be silenced by rewording a log line: 5xx rate >5%, p95 >3s, at the maxScale ceiling, DB connections >18, DB CPU >85%, DB disk >85%. Each was verified to have live data. See `docs/Launch_Check_2026-08-19.md` §0.

⚠ **The metrics are a REGEX over log text.** Reword the `rbmetrics` line and
every metric goes FLAT, not red — `api_smoke.sh` section M pins the exact
shape for precisely this reason. A quiet dashboard after an API change is a
thing to distrust, not celebrate.

| alert | threshold | what it actually means |
|---|---|---|
| `rb-api health` uptime check | `/healthz/` (TRAILING SLASH — slashless is unreachable on some networks) over 443 | API down or Cloud Run cold-start storm |
| ladder: jobs are not being worked | oldest READY job > **2100s** for 5 min (was 900s — below the 30-min safety net it would have fired on a condition that self-heals) | Scheduler stopped firing, or worker crashes on start — check `gcloud run jobs executions list` |
| ladder: the reaper is not reaping | oldest heartbeat > 900s | API's internal reaper timer dead — matches will never time out; restart rb-api |
| ladder: jobs are failing | > 5 FAILED over 10 min | Worker validating/fighting is erroring — read the job execution logs; a malformed snapshot from a new client version is the likely cause post-launch |
| ladder: uploaded robots are stuck | oldest PENDING snapshot > 1800s | Enlist pipeline stalled — players enlisted but never appear on the board |

---

## 4. The 5-minute health check (no credentials needed)

The board endpoint is anonymous and exercises API + DB + ratings in one GET:

```sh
curl -s "https://rb-api-902243335343.us-central1.run.app/v1/leaderboard" | python3 -m json.tool | head -30
curl -si "https://rb-api-902243335343.us-central1.run.app/healthz/" | head -1
```

Healthy: HTTP 200s, board has entries, `seasonEndsAt` is in the future.
With gcloud (account leondu167@gmail.com):

```sh
gcloud run services logs read rb-api --project robot-brawl-ladder --region us-central1 --limit 50
gcloud run jobs executions list --job rb-worker --project robot-brawl-ladder --region us-central1 --limit 5
gcloud logging read 'textPayload:"rbmetrics"' --project robot-brawl-ladder --limit 3
```

**Full-loop canary (writes to production — deliberate, uses the dedicated
test accounts):** `PlayerJourneyAgent` plays the entire game — register,
build, career fight, wallet, shop, enlist, board, challenge, live fight,
cloud settle, sign-out/in — through real onClicks. Last run 27/0. In play
mode, Device Simulator on the profile of interest:

```csharp
PlayerJourneyAgent.Email = "test_iphone@cyberduck.club";   // or test_ipad@
PlayerJourneyAgent.Password = "testplayer123";
PlayerJourneyAgent.RobotName = "Piston";                   // Grinder for ipad
PlayerJourneyAgent.ChallengeTarget = "Grinder";            // "" to skip J8
PlayerJourneyAgent.ShotDir = "/tmp/journey";               // screenshots
PlayerJourneyAgent.Run();                                  // read [Journey] lines
```

These two accounts (and `appreview@`) are the ONLY accounts sessions may
write to on production. Everything else refuses:
`EnlistLiveBench`/`EnlistUiBench`/`ArenaShots` stop when
`LadderClient.IsProduction`. `StoreShots` touches production READ-ONLY.

---

## 5. Debug playbook — symptom → subsystem

| player says | look at | notes |
|---|---|---|
| "app won't load / spins forever" | uptime check, `rb-api` logs | Client has a 30s request timeout then degrades; the board shows "offline — showing last known" |
| "my fights never settle" / "opponent never fought back" | worker alerts §3, `rb-worker` executions | 5-min cadence means ≤ ~7 min is NORMAL, tell players that first |
| "I enlisted but I'm not on the board" | stuck-snapshot alert; then the robot itself | Validation legitimately rejects: no wheels, Brawler program without a Compass tracker, parts not flush. A robot with no weight category is a REJECTION, not "unrated" |
| "I lost all my progress after reinstall" | expected for CAREER (local file); ARENA history is server-side and survives | First-win purse ledger has a uniqueness key `(user_id, LEAGUE_PURSE, ref)` — reinstall re-claims are blocked server-side |
| "I was signed out" | 30-day JWT expiry — any 401 retires the session cleanly | Re-login restores robots + history (J9-verified) |
| "buttons do nothing" | The busy-swallow class: `ArenaScreen` buttons no-op while `arena.Busy` | Bit twice (MIDDLE chip, accsubmit). If a new report smells like this, reproduce with PlaytestBench, not by reading code |
| "text overlaps / can't tap something" | PlaytestBench + TouchSmoke on the closest Device Simulator profile | The 44pt floor is met with ZERO margin by design — any occlusion puts a control under it |
| crash reports | ASC → TestFlight → Crashes, or Xcode Organizer | dSYMs upload with each archive |

**Client-side truths that will save you a day each:**
- Career save: `~/Library/Application Support/owen/Robot Brawl_ Bolt & Blade/robotbrawl_career.json` on owen's Mac — baseline md5 `b12cdfdc36cf1eb88ccc6c9fb98dd55e`, backups in `career_backups/`. Fingerprint (md5 + mtime) before/after ANY play-mode work.
- The EDITOR defaults to `LOCAL_DEV` (port **5099**, not 5000 — AirPlay owns 5000); a BUILD defaults to `PRODUCTION`. That asymmetry is load-bearing.
- The `[dev]` badge on the SIMULATOR lies — `isDebugBuild` is true on any sim build. Build identity comes from artifacts, never from the screen.
- To check any UI behaviour, fire `b.onClick.Invoke()` on the named button, never the `Test*` seam — behaviour lives in onClicks (WATCH, REMOVE, THE BOARD all did).

---

## 6. Rolling build 9+ (the whole ritual, proven 7 times)

Auto-mode covers this end-to-end; the human checkpoints are red benches and
the review-swap decision.

1. **Verify first**: PlaytestBench (43), TouchSmoke (55), plus whatever the
   change touches — ONE AT A TIME. Career-save fingerprint before and after.
2. Bump: `PlayerSettings.iOS.buildNumber = "9"` (+ SaveAssets). Confirm
   `RB_DEV_SERVER` absent from iOS defines.
3. `RobotBrawl.Editor.BuildIOS.Build()` — in place; the project's target IS
   iOS so no clone (BuildIOS.cs header explains; Android is the opposite).
   ⚠ Harness scripts that reference `UiShot` must sit behind
   `#if UNITY_EDITOR || DEVELOPMENT_BUILD` — an unguarded one broke the
   build-8 player compile while every editor bench stayed green.
4. Archive → export → upload, one chain (paths relative to project root):
   ```sh
   xcodebuild -project build/ios/Unity-iPhone.xcodeproj -scheme Unity-iPhone \
     -configuration Release -archivePath build/RobotBrawl.xcarchive \
     -destination 'generic/platform=iOS' CODE_SIGN_STYLE=Automatic \
     DEVELOPMENT_TEAM=BMLXB23PHU PROVISIONING_PROFILE_SPECIFIER="" \
     -allowProvisioningUpdates archive
   xcodebuild -exportArchive -archivePath build/RobotBrawl.xcarchive \
     -exportOptionsPlist build/ExportOptions.plist -exportPath build/export \
     -allowProvisioningUpdates -authenticationKeyID PG23GXN3RN \
     -authenticationKeyIssuerID 83e01f1e-c25e-46fe-842d-efa72fd7250d \
     -authenticationKeyPath ~/.appstoreconnect/private_keys/AuthKey_PG23GXN3RN.p8
   xcrun altool --upload-app -f build/export/RobotBrawlBoltBlade.ipa -t ios \
     --apiKey PG23GXN3RN --apiIssuer 83e01f1e-c25e-46fe-842d-efa72fd7250d
   ```
5. TestFlight: answer encryption for the new build. If it should replace an
   in-review build, §1's swap procedure.
6. Push commits to the session side branch (never main; never force).

Server hotfix path: edit `server/`, `server/tests/run_local.sh` green
(sql_bench 53/53 + api_smoke 208/208 at last count), then **STOP — deploy is
owen's**, via `deploy_api.zsh`.

---

## 7. Open items, in the order they will bite

1. **Dev-test names on the public FEATHER board**: ThirdEnlistee / Challenger,
   SecondEnlistee / Rival, BuiltInTheCloud / ContainerTest — plus the E2E
   robots Piston / Grinder. Real players will see them at launch. Retire or
   keep-as-seed is owen's call; there is no admin endpoint, so retirement is
   SQL against rb-db (or an endpoint worth writing first).
2. **Season 1 rollover, 2026-09-09** — two commands, in order, owen's go.
3. **PITR off** — the day launch data starts feeling valuable is the day
   before a restore loses 24h of it.
4. **Sybil / multi-account** — the first-win ledger blocks reinstall
   re-claims but nothing stops 10 accounts on one device. Documented gap;
   matters pre-monetization.
5. **`[dev]` badge on a real TestFlight device** — believed absent on device
   builds, verified on simulator only by compile-time reasoning. First device
   report of a `[dev]` in the ARENA status line = this was wrong.
6. **`MkInput` contrast (1.23:1)** on shipped single-player fields — the
   border fix exists (ARENA fields, 3.89:1); applying it game-wide is owen's
   look-and-feel call.
7. **Mutual disarm 43% / mirror lock** — measured, levers swept
   (`docs/Disarm_Lever_Sweep_2026-08-10.md`), deliberately not changed.
   Players may report it as a bug; it is a design question.

---

## 8. Credentials index (values live elsewhere, never here)

| what | where |
|---|---|
| ASC API key `PG23GXN3RN` | `~/.appstoreconnect/private_keys/AuthKey_PG23GXN3RN.p8`, issuer `83e01f1e-c25e-46fe-842d-efa72fd7250d` |
| GitHub push token | `.gh_token.local` (gitignored). Push by full URL to `du6/robot-brawl`, side branch, token redacted from output |
| GCP secrets | Secret Manager, project `robot-brawl-ladder` |
| Production test accounts | `test_iphone@` / `test_ipad@cyberduck.club` (journey agents), `appreview@cyberduck.club` (Apple's demo login) — test-only accounts, no real user data |
| Upload keystore (Android) | does not exist yet — owen creates it when Play Console work starts |
