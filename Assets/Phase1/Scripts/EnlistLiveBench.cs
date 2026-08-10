// ===========================================================================
// EnlistLiveBench — the whole ladder, through the REAL transport. 2026-08-10.
//
//   RobotBrawl.Phase0.EnlistLiveBench.Run();     // play mode, API running
//   RobotBrawl.Phase0.EnlistLiveBench.finished / passed / failed / skipped
//
// Boot a server first — server/tests/run_local.sh leaves one, or start it by
// hand. With no server every check SKIPS rather than fails, for the reason
// LadderLiveBench gives: a bench that goes red because a developer did not
// happen to have Postgres running teaches people to ignore it.
//
// WHAT THIS COVERS THAT NOTHING ELSE DID, and there were two holes:
//
//  1. ENLIST. Nothing in Assets/ had ever called POST /v1/robots or
//     POST /v1/snapshots. RobotSnapshot.Export was written, benched, and
//     never invoked by the product; ArenaScreen takes its challenger from
//     MyRobots -> activeSnapshotId, so the challenge UI had nothing to
//     select and the board could only show robots that arrived by curl.
//     The ladder was live, autonomous, alerted and 198/198 green — and
//     unreachable from inside the game.
//
//  2. THE WORKER'S REAL TRANSPORT. FightWorkerBench drives the fight loop
//     through a STUB and even runs a real MatchRunner bout, which proves the
//     loop and nothing about HttpWorkerTransport. The cloud half was proven
//     BY HAND with curl (docs/HANDOVER_2026-08-10 §1) and never by a bench —
//     CLAUDE.md's "the worker fight path has no BENCH".
//
// So this drives the actual path a player takes: register, enlist, let a real
// worker validate it over HTTP, appear on the board, challenge, let a real
// worker fight it over HTTP, and read the result back. Every hop is
// UnityWebRequest against a live API.
//
// ⚠ IT NEVER TOUCHES OWNER STATE. Envelopes are built from raw text with
// ExportRaw (the FightWorkerBench precedent) so the builder is never driven
// and owen's bay is never written. MatchRunner isolates Career.Data itself
// (MatchRunner.cs:216-255) and this adds no second layer — two nested swaps
// that disagree about what "saved" means is how you lose a career save.
// The bay is fingerprinted either side anyway, because "it should not touch
// it" is an assumption and this project has been bitten by exactly that.
//
// ⚠ It WRITES TO THE SERVER — accounts, robots, snapshots, matches. Point it
// at a dev database, never at production.
// ===========================================================================
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RobotBrawl.Phase0
{
    public class EnlistLiveBench : MonoBehaviour
    {
        public static bool finished;
        public static int passed, failed, skipped;
        static readonly List<string> log = new List<string>();

        // A worker key must match the server's. run_local.sh boots with this.
        public static string WorkerKey = "dev-only-worker-key";

        static void Pass(string w) { passed++; log.Add("PASS  " + w); }
        static void Fail(string w) { failed++; log.Add("FAIL  " + w); }
        static void Skip(string w, string why) { skipped++; log.Add("SKIP  " + w + " -- " + why); }
        static void Note(string w) { log.Add("      " + w); }
        static void Check(bool c, string w) { if (c) Pass(w); else Fail(w); }

        public static void Run()
        {
            finished = false; passed = 0; failed = 0; skipped = 0; log.Clear();
            var go = new GameObject("enlist_live_bench");
            go.AddComponent<EnlistLiveBench>().StartCoroutine(All());
        }

        /// <summary>The whole report, for a session reading over the bridge —
        /// result.Log only substitutes the first two {n} arguments, so callers
        /// build the string themselves.</summary>
        public static string Report()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var l in log) sb.Append(l).Append('\n');
            sb.Append("===== passed ").Append(passed)
              .Append("  failed ").Append(failed)
              .Append("  skipped ").Append(skipped).Append(" =====");
            if (skipped > 0 && passed == 0) sb.Append("\nNOTHING RAN — this is not a pass.");
            return sb.ToString();
        }

        /// <summary>One LEGAL robot, as raw text, so the builder is never
        /// driven and owen's bay is never touched.
        ///
        /// ⚠ IT HAS TO BE A REAL BUILD. The first version of this bench used
        /// FightWorkerBench's fixture — a lone core — because that is enough
        /// for MatchRunner to construct something. The VALIDATOR is a
        /// different judge and rejected it: "Needs at least 1 wheel." The
        /// worker was right, the fixture was wrong, and the bench correctly
        /// reported a red rather than a pass.
        ///
        /// So this is an actual robot lifted from a career save — chassis,
        /// four wheels, battery, and a spinner — which validates to FEATHER.
        /// A fixture that cannot survive the thing under test proves nothing.</summary>
        const string LEGAL_BUILD =
              "core|0.000,0.700,0.000|0|0.00,0.00,0.00|Aluminum\n"
            + "beam|-0.250,0.700,0.000|0|0.00,0.00,0.00|Aluminum\n"
            + "beam|-0.250,0.700,-0.600|0|0.00,0.00,0.00|Aluminum\n"
            + "beam|0.250,0.700,0.000|0|0.00,0.00,0.00|Aluminum\n"
            + "beam|0.250,0.700,-0.600|0|0.00,0.00,0.00|Aluminum\n"
            + "wheel|0.420,0.700,0.150|0|1.00,0.00,0.00|Rubber\n"
            + "wheel|0.420,0.700,-0.750|0|1.00,0.00,0.00|Rubber\n"
            + "wheel|-0.420,0.700,0.150|0|-1.00,0.00,0.00|Rubber\n"
            + "wheel|-0.420,0.700,-0.750|0|-1.00,0.00,0.00|Rubber\n"
            + "beam|0.000,0.700,-0.450|0|0.00,0.00,0.00|Aluminum\n"
            + "battery|0.000,0.925,-0.450|0|0.00,0.00,0.00|Aluminum\n"
            + "spindle|0.000,1.000,0.000|0|0.00,1.00,0.00|Aluminum\n"
            + "beam|0.000,1.250,0.000|0|0.00,0.00,0.00|Aluminum\n"
            + "beamlong|0.000,1.250,0.800|0|0.00,0.00,0.00|Aluminum\n"
            + "gyro|0.000,1.030,1.100|0|0.00,0.00,0.00|Aluminum\n"
            + "spike|0.000,1.030,1.370|0|0.00,0.00,1.00|Steel\n";

        static SnapshotEnvelope Fixture(string name)
        {
            // A program, so the robot MOVES. "" is AI-driven and legal, but a
            // fight between two statues decides on a timeout and would prove
            // nothing about the fight path.
            //
            // ⚠ FIRST STEPS, not Brawler, and this is the SECOND fixture
            // rejection — validation checks the PROGRAM against the BUILD as
            // well as the build itself. Brawler opens with an IN RANGE hat and
            // the validator answered "hat 1 (IN RANGE): needs a Compass
            // tracker — SHOP", because this chassis carries a gyro and no
            // compass. FIRST STEPS is the sensor-free preset (BuilderManager
            // says so where it refuses to gate it behind a sensor), so it is
            // legal on any legal chassis. Arming a preset whose sensors the
            // fixture lacks is a fixture bug that reads exactly like a broken
            // validator.
            return RobotSnapshot.ExportRaw(name, BuilderManager.SNAP_STAMP + "\n" + LEGAL_BUILD,
                                           RobotProgram.FirstSteps().ToJson());
        }

        static IEnumerator All()
        {
            Note("server: " + LadderClient.BaseUrl);

            var bm = FindFirstObjectByType<BuilderManager>();
            if (bm == null) { Fail("a BuilderManager is in the scene"); Done(); yield break; }
            string ownerBay = bm.SnapshotString();
            string savedToken = LadderClient.Token;

            // ---- is anyone home? -------------------------------------------
            List<LadderEntry> board = null; string err = null;
            yield return LadderClient.Leaderboard("", (r, e) => { board = r; err = e; });
            if (!string.IsNullOrEmpty(err))
            {
                Skip("the whole bench", "no server at " + LadderClient.BaseUrl + " (" + err + ")");
                Done(); yield break;
            }
            Pass("the API answers");

            // Unique per run: this bench writes real rows and must not collide
            // with its own previous run.
            string tag = DateTime.UtcNow.ToString("HHmmss") + "-" + UnityEngine.Random.Range(1000, 9999);

            // ================================================================
            // A. ENLIST — the path that did not exist.
            // ================================================================
            log.Add("== A. enlist ==");

            string emailA = "bench-a-" + tag + "@example.test";
            yield return LadderClient.Register(emailA, "bench-password-1", "Bench A",
                                               (who, e) => { err = e; });
            if (!string.IsNullOrEmpty(err))
            {
                Skip("everything after registration", "register failed: " + err);
                LadderClient.Token = savedToken; Done(); yield break;
            }
            Pass("an account can be created from inside the game");

            var envA = Fixture("BenchBot-A-" + tag);
            string snapA = null;
            yield return LadderClient.Enlist("BenchBot-A-" + tag, envA, (id, e) => { snapA = id; err = e; });
            Check(string.IsNullOrEmpty(err) && !string.IsNullOrEmpty(snapA),
                  "a robot can be ENLISTED from inside the game" + (err != null ? " -- " + err : ""));
            if (string.IsNullOrEmpty(snapA))
            {
                Skip("everything after enlisting", "no snapshot id");
                LadderClient.Token = savedToken; Done(); yield break;
            }

            // It must be MINE and it must not be fightable yet: a worker has
            // not seen it. A client that showed it as ready here would offer a
            // challenge the server refuses.
            List<MyRobot> mine = null;
            yield return LadderClient.MyRobots((r, e) => { mine = r; err = e; });
            Check(mine != null && mine.Count == 1, "the enlisted robot comes back from GET /v1/robots");
            if (mine != null && mine.Count == 1)
                Check(!mine[0].CanFight, "…and it is NOT fightable yet — no worker has judged it");

            // Re-enlisting the same NAME must reuse the robot id, or every
            // upload would start a fresh rating at placement.
            string snapA2 = null;
            yield return LadderClient.Enlist("BenchBot-A-" + tag, Fixture("BenchBot-A-" + tag),
                                             (id, e) => { snapA2 = id; err = e; });
            List<MyRobot> mine2 = null;
            yield return LadderClient.MyRobots((r, e) => { mine2 = r; });
            Check(mine2 != null && mine2.Count == 1,
                  "re-enlisting the same name reuses the robot rather than starting a second rating");

            // ================================================================
            // B. VALIDATE over the REAL transport.
            // ================================================================
            log.Add("== B. a real worker validates it over HTTP ==");

            var vnet = new HttpWorkerTransport(LadderClient.BaseUrl, WorkerKey, "VALIDATE");
            int before = ValidateWorkerLoop.PostsSucceeded;
            int guard = 0;
            // The queue may hold jobs from an earlier run; drain until ours is
            // done rather than assuming the first claim is it.
            //
            // ⚠ KEEP THE LAST NON-EMPTY VERDICT. RunOnce clears LastPostedJson
            // on entry, so the iteration that finds an empty queue erases the
            // verdict the previous one wrote — which is how the first red here
            // reported "worker posted: " with nothing after it, and the reason
            // (a rejected fixture) had to be dug out of Postgres by hand.
            string lastVerdict = "";
            while (guard++ < 12)
            {
                yield return ValidateWorkerLoop.RunOnce(vnet, bm, null);
                if (!string.IsNullOrEmpty(ValidateWorkerLoop.LastPostedJson))
                    lastVerdict = ValidateWorkerLoop.LastPostedJson;
                List<MyRobot> m = null;
                yield return LadderClient.MyRobots((r, e) => { m = r; });
                if (m != null && m.Count > 0 && m[0].CanFight) break;
            }

            Check(ValidateWorkerLoop.PostsSucceeded > before,
                  "the worker posted a validate result over the real HTTP transport");

            List<MyRobot> mineV = null;
            yield return LadderClient.MyRobots((r, e) => { mineV = r; });
            bool activeA = mineV != null && mineV.Count > 0 && mineV[0].CanFight;
            Check(activeA, "the enlisted robot is now ACTIVE and fightable");
            if (activeA)
                Check(!string.IsNullOrEmpty(mineV[0].category),
                      "…and the worker gave it a weight category (" +
                      (mineV != null && mineV.Count > 0 ? mineV[0].category : "?") + ")");

            if (!activeA)
            {
                // LastError is EMPTY on a rejection — the post SUCCEEDED, the
                // verdict was just REJECT. Reading only LastError here said
                // "never became ACTIVE: " and stopped, which hid the reason
                // the worker had already written down. Show the verdict.
                Skip("the fight half", "the robot never became ACTIVE. worker posted: "
                     + (string.IsNullOrEmpty(lastVerdict) ? "(nothing)" : lastVerdict)
                     + (string.IsNullOrEmpty(ValidateWorkerLoop.LastError)
                        ? "" : " | error: " + ValidateWorkerLoop.LastError));
                Check(bm.SnapshotString() == ownerBay, "the bay is still owen's");
                LadderClient.Token = savedToken; Done(); yield break;
            }

            // The cold-start fix (HANDOVER_2026-08-10 §3): validation creates
            // the placement rating, so a validated robot is VISIBLE TO STRANGERS.
            // Without it two players can both enlist and neither can see the
            // other, and no first challenge is ever possible.
            string nameA = "BenchBot-A-" + tag;
            string catA = mineV[0].category;
            LadderClient.Token = "";                       // become a stranger
            List<LadderEntry> pub = null;
            yield return LadderClient.Leaderboard(catA, (r, e) => { pub = r; err = e; });
            bool seen = false;
            if (pub != null)
                foreach (var e2 in pub) if (e2.robotName == nameA) { seen = true; break; }
            Check(seen, "a STRANGER can see it on the board — the cold-start placement rating exists");

            // ================================================================
            // C. A second player, a challenge, and a real fight over HTTP.
            // ================================================================
            log.Add("== C. challenge and fight ==");

            string emailB = "bench-b-" + tag + "@example.test";
            yield return LadderClient.Register(emailB, "bench-password-1", "Bench B", (who, e) => { err = e; });
            if (!string.IsNullOrEmpty(err))
            {
                Skip("the fight half", "second account failed: " + err);
                Check(bm.SnapshotString() == ownerBay, "the bay is still owen's");
                LadderClient.Token = savedToken; Done(); yield break;
            }
            Pass("a second account can be created");

            string snapB = null;
            yield return LadderClient.Enlist("BenchBot-B-" + tag, Fixture("BenchBot-B-" + tag),
                                             (id, e) => { snapB = id; err = e; });
            Check(!string.IsNullOrEmpty(snapB), "the second player enlists");

            guard = 0;
            List<MyRobot> mineB = null;
            while (guard++ < 12)
            {
                yield return ValidateWorkerLoop.RunOnce(vnet, bm, null);
                yield return LadderClient.MyRobots((r, e) => { mineB = r; });
                if (mineB != null && mineB.Count > 0 && mineB[0].CanFight) break;
            }
            bool activeB = mineB != null && mineB.Count > 0 && mineB[0].CanFight;
            Check(activeB, "the second robot is validated too");
            if (!activeB)
            {
                Skip("the fight", "the second robot never became ACTIVE");
                Check(bm.SnapshotString() == ownerBay, "the bay is still owen's");
                LadderClient.Token = savedToken; Done(); yield break;
            }

            // B challenges A. The challenger is whoever is signed in.
            //
            // ⚠ THE DEFENDER'S SNAPSHOT IS RE-READ FROM THE BOARD, not
            // remembered from section B, and that is not tidiness. Section A
            // enlists twice on purpose; the second upload sits PENDING until
            // some later drain validates it, and when it does it SUPERSEDES
            // the first. An id captured earlier goes stale underneath the
            // bench — measured: "their snapshot is SUPERSEDED, not ACTIVE".
            // Reading the board is also what the product does, because that is
            // the only place a challenger learns an opponent's snapshot.
            List<LadderEntry> board2 = null;
            yield return LadderClient.Leaderboard(catA, (r, e) => { board2 = r; });
            string defender = "";
            if (board2 != null)
                foreach (var e2 in board2) if (e2.robotName == nameA) { defender = e2.activeSnapshotId; break; }
            if (string.IsNullOrEmpty(defender))
            {
                Skip("the fight", "the defender is no longer on the board");
                Check(bm.SnapshotString() == ownerBay, "the bay is still owen's");
                LadderClient.Token = savedToken; Done(); yield break;
            }

            string matchId = null; int stake = 0;
            yield return LadderClient.Challenge(mineB[0].activeSnapshotId, defender,
                                                (mid, st, e) => { matchId = mid; stake = st; err = e; });
            Check(!string.IsNullOrEmpty(matchId),
                  "a challenge can be issued from inside the game" + (err != null ? " -- " + err : ""));
            if (string.IsNullOrEmpty(matchId))
            {
                Skip("the fight", "no match was created: " + err);
                Check(bm.SnapshotString() == ownerBay, "the bay is still owen's");
                LadderClient.Token = savedToken; Done(); yield break;
            }
            Note("match " + matchId + " for " + stake + " scrap");

            // ---- the real fight worker, over real HTTP ---------------------
            //
            // ⚠ DRAIN TO *OUR* MATCH. A single RunOnce claims whatever is at
            // the head of the queue, and on a dev database that is usually
            // somebody else's leftovers — measured: it claimed a FIGHT job
            // left by an earlier api_smoke run, whose payloads are synthetic
            // by design, refused it ("payload is not a readable snapshot"),
            // posted the DRAW that retires it, and our match sat READY with
            // attempts=0 while the bench reported a red against the worker.
            //
            // The worker was right every step of that: refusing an unreadable
            // payload rather than fighting a substituted robot is the point of
            // OpenVerified, and posting rather than dropping is what stops the
            // livelock. The bench was wrong to assume the first claim was its.
            var fnet = new HttpWorkerTransport(LadderClient.BaseUrl, WorkerKey, "FIGHT");
            int fBefore = FightWorkerLoop.PostsSucceeded;
            MatchRunner.MatchResult res = null;
            bool ranOurs = false;
            int fguard = 0;
            while (fguard++ < 10)
            {
                MatchRunner.MatchResult r0 = null;
                yield return FightWorkerLoop.RunOnce(fnet, bm, r => { r0 = r; });
                string posted = FightWorkerLoop.LastPostedJson ?? "";
                if (posted.Contains(matchId)) { res = r0; ranOurs = true; break; }
                if (string.IsNullOrEmpty(posted) && string.IsNullOrEmpty(FightWorkerLoop.LastError))
                    break;                                   // queue is empty
            }

            Check(FightWorkerLoop.PostsSucceeded > fBefore,
                  "the fight worker posted a verdict over the real HTTP transport"
                  + (FightWorkerLoop.PostsSucceeded > fBefore ? "" : " -- " + FightWorkerLoop.LastError));
            Check(ranOurs, "the worker reached THIS bench's match"
                  + (ranOurs ? "" : " -- drained " + (fguard - 1) + " job(s) without finding it"));
            Check(res != null && res.Ok,
                  "a real best-of-3 ran to completion"
                  + (res == null ? " -- no result: " + FightWorkerLoop.LastError : ""));
            Check(FightWorkerLoop.LastVerdict == "CHALLENGER"
               || FightWorkerLoop.LastVerdict == "DEFENDER"
               || FightWorkerLoop.LastVerdict == "DRAW",
                  "the verdict is one the API's CHECK constraint accepts (" + FightWorkerLoop.LastVerdict + ")");

            // ---- and the player can read it back ---------------------------
            List<InboxEntry> box = null;
            yield return LadderClient.Inbox((r, e) => { box = r; err = e; });
            InboxEntry found = null;
            if (box != null) foreach (var m in box) if (m.matchId == matchId) { found = m; break; }
            Check(found != null, "the match appears in the challenger's inbox");
            if (found != null)
                Check(found.replayUrls.Count > 0,
                      "…with at least one replay URL, so WATCH has something to play");

            // ================================================================
            // D. owner state, which is the one thing that must not move.
            // ================================================================
            log.Add("== D. owner state ==");
            Check(bm.SnapshotString() == ownerBay, "the bay is byte-identical to before the bench");

            LadderClient.Token = savedToken;
            Done();
        }

        static void Done()
        {
            finished = true;
            Debug.Log("[EnlistLive] " + Report());
        }
    }
}
