# Launch checklist — 2026-08-10

Written because the question "are we ready to launch?" deserves a list rather
than a feeling. Everything below is either **measured**, **decided**, or
**neither** — and the third category is the useful one.

---

## READY, and verified today

| | evidence |
|---|---|
| API live and **running current code** | last API-code commit `1cf7092` 07:45:32Z, deployed revision `rb-api-00009-jwj` 07:46:58Z — 86 s later. No redeploy needed. |
| Server benches | `sql_bench` 53/53 · `api_smoke` 198/198 (1 honest skip) · `restore_drill` 10/10 |
| Fresh clone builds and boots | done today; ships every `Sql/*.sql` and migration |
| Worker runs unattended | Cloud Run Job + Scheduler, drains and exits |
| Alerting | 4 policies + uptime check; delivery confirmed by drill. ⚠ the uptime check needs `/healthz/` **with the trailing slash** — `/healthz` 404s, and that is documented, not broken |
| **The app knows where the server is** | was `http://localhost:5000` with no caller — fixed today. Editor → local, build → production |
| Benches cannot write to production | they refuse; proven by aiming one at production on purpose |
| **The iOS app builds** | Succeeded, 0 errors, Xcode project at `build/ios`, `com.owen.robotbrawl` 2.1.1, min iOS 15 |
| ATS correct | `allowsArbitraryLoads=False`, and production is https |
| No QA harnesses in the player | `dev-only-worker-key` 1 → 0 in the shipped metadata |
| Owner state | career save `18614d0e`, mtime unchanged across ~600 real fights today |

---

## BLOCKED ON A DEVICE — nobody can close these from a desk

1. **Run it on a phone.** Every UI judgement made today was made in an editor
   game view. The whole ARENA was rewritten in one session and has never been
   touched by a finger.
2. **Confirm which server the build points at.** The il2cpp output proves the
   `#if` resolved to a single unconditional return; it does not prove *which*
   branch, because the literal is a metadata index. So the app now **prints
   its base URL in the ARENA status line in a development build** — one
   glance on the device settles it. (Release builds show a player nothing.)
3. Signing, archiving, TestFlight. Needs Xcode and owen's certificates;
   nothing in this repo can or should do them.

---

## BLOCKED ON A DECISION — owen's, and none of them are close calls I can make

1. **Point-in-time recovery on `rb-db`.** Currently OFF: daily backups, 7
   retained, so a restore loses up to 24 h. The least replaceable data this
   game will ever hold is the first week of ratings and wallets. Costs WAL
   storage against a $25/mo budget. **Decide before real players exist, not
   after.**
2. **CEILING L4 — a robot two value-classes BELOW the flagship contest wins
   22/24 = 92%.** Measured at 3× sample. This is a live single-player balance
   defect, not a tuning nudge. `docs/CareerBench_Sample_Size_2026-08-10.md`.
3. **STRETCH fails L3/L4/L5 in the same direction** (17%, 11%, 6% vs ≥25%) —
   systematic, not noise.
4. **The disarm lever.** Table ready: `docs/Disarm_Lever_Sweep_2026-08-10.md`.
   `WEAPON_VS_STRUCT` does nothing; `BREAK_K ×1.5` takes 40% → 27%; a shorter
   weapon arm fixes the mechanism and makes the rate worse. And the question
   nobody has asked: **is 43% actually wrong?**
5. **Field contrast.** Every text field in the dock is a dark box on a dark
   row, near-invisible until focused — and the first one a new player meets is
   the sign-in box. Small fix, touches shipped single-player UI.
6. **Should ARENA stay career-only?** It is, matching what TROPHIES was, so a
   sandbox player cannot find multiplayer at all. Defensible; also possibly
   wrong.
7. **Worker latency.** 5-minute scheduling; instant fights are a scheduler
   change and a bill.

---

## KNOWN AND DELIBERATELY NOT DONE

- `UP1Sweep`, `UP2Fight`, `UP2Path`, `UserPathTest` and siblings look like
  harnesses and are still compiled into the player. I could not cheaply prove
  they are not product, and guessing wrong breaks the shipped game to save a
  few kilobytes. Worth ten minutes with someone who remembers what they are.
- **Mirror lock** (`CLAUDE.md` §7.6) is called "a behaviour problem, not a
  damage constant". Today's sweep was **105 mirror matches**, and geometry
  moved it more than any constant did — so that note is incomplete. Geometry
  is neither behaviour nor a damage constant.

---

## The honest summary

**The server is ready. The app is built but unproven.**

Nothing on the blocked lists is a surprise or a half-finished piece of work —
they are a device you need in your hand, and six decisions that are yours to
make. The single riskiest item is not on any list as a defect: **an entire
multiplayer UI was written today and no human has used it.** Benches measure
geometry and data. They do not measure whether it feels like anything.

If only one thing happens next, it is putting `build/ios` on a phone.
