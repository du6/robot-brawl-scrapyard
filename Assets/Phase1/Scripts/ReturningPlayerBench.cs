// ⚠ EDITOR AND DEVELOPMENT BUILDS ONLY — 2026-08-10.
// This is a QA harness. It registers accounts and uploads robots; it has no
// business in a shipped player. Same guard as every other bench here.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
// ===========================================================================
// ReturningPlayerBench — the player who LEAVES AND COMES BACK. 2026-08-10.
//
//   RobotBrawl.Phase0.ReturningPlayerBench.Run();     // play mode, API on 5099
//   RobotBrawl.Phase0.ReturningPlayerBench.finished / passed / failed / skipped
//
// WHY THIS EXISTS, and it is a hole in the SHAPE of the suite rather than in
// any one bench. Counted on 2026-08-10:
//
//     bench                calls Register   calls Login
//     EnlistLiveBench            2               0
//     EnlistUiBench              1               0
//     LadderLiveBench            0               0
//     LadderClientBench          0               0
//
// NOT ONE BENCH IN THIS PROJECT HAD EVER CALLED LadderClient.Login. Every one
// of them makes a BRAND NEW ACCOUNT, does its errand inside a single session,
// and exits. So the only player ever exercised was a player in their first
// thirty seconds — against a ladder whose entire premise is that you enlist, go
// away for hours, and come back to find out what happened. The returning player
// is the product, and nothing had ever been one.
//
// That is not a coverage statistic, it is the direct cause of a launch blocker:
// the ARENA never refetched after its first open. ArenaScreen.Start() was the
// only thing in the game that ever asked the server for the board, and Start()
// runs once. Re-entering the tab called RefreshArena(), which REPAINTS the
// model already in hand. A bench that never comes back cannot see a board that
// never updates — every surface was green and the screen was dead.
//
// So this bench is deliberately built out of the two verbs the suite lacked:
//
//   B. SIGN OUT, then SIGN BACK IN with the same credentials, and prove the
//      returning session still owns what the first one enlisted.
//   C. Open the ARENA, go away, let something land on the board while you are
//      gone, come back — and prove the dock ASKED THE SERVER AGAIN. It asserts
//      on ArenaScreen.TestRefreshes rather than on the board's contents,
//      because a stale board and a freshly-reloaded identical board look
//      exactly the same, which is the whole reason this hid.
//
// ⚠ IT IS THE REGRESSION TEST FOR MobileBuilderUI's arenaFetchAt GATE. Delete
// that gate and section C goes red on "the board was REFETCHED"; it was run
// both ways on purpose, because a regression test nobody has seen fail is a
// hope, not a test.
//
// ⚠ OWNER STATE. Career.Data is swapped for a scratch career under a COUNTED
// Career.SuspendAutosave() hold and restored in a finally — the EnlistUiBench
// mechanism, used the same way. It rebuilds MobileBuilderUI (the TouchSmoke
// precedent) and never drives the builder, so owen's bay is untouched;
// it is fingerprinted either side anyway.
//
// ⚠ It WRITES TO THE SERVER. Dev database only — it refuses production.
// ===========================================================================
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RobotBrawl.Phase0
{
    public class ReturningPlayerBench : MonoBehaviour
    {
        public static bool finished;
        public static int passed, failed, skipped;
        static readonly List<string> log = new List<string>();

        public static string WorkerKey = "dev-only-worker-key";

        static void Pass(string w) { passed++; log.Add("PASS  " + w); }
        static void Fail(string w) { failed++; log.Add("FAIL  " + w); }
        static void Skip(string w, string why) { skipped++; log.Add("SKIP  " + w + " -- " + why); }
        static void Note(string w) { log.Add("      " + w); }
        static void Check(bool c, string w) { if (c) Pass(w); else Fail(w); }

        public static void Run()
        {
            finished = false; passed = 0; failed = 0; skipped = 0; log.Clear();
            var go = new GameObject("returning_player_bench");
            go.AddComponent<ReturningPlayerBench>().StartCoroutine(All());
        }

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

        // The same real chassis EnlistLiveBench uses — a lone core is rejected
        // by the validator ("Needs at least 1 wheel"), and a fixture that cannot
        // survive the thing under test proves nothing. Validates to FEATHER,
        // which is the board the ARENA tab opens on.
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
            // FIRST STEPS is the sensor-free preset, so it is legal on any legal
            // chassis; Brawler wants a compass this build does not carry.
            return RobotSnapshot.ExportRaw(name, BuilderManager.SNAP_STAMP + "\n" + LEGAL_BUILD,
                                           RobotProgram.FirstSteps().ToJson());
        }

        static CareerData Scratch(string robotName)
        {
            var d = new CareerData();
            d.stable.Add(new CareerRobot
            {
                name = robotName,
                snapshot = BuilderManager.SNAP_STAMP + "\n" + LEGAL_BUILD,
                program = RobotProgram.FirstSteps().ToJson(),
            });
            d.activeRobot = 0;
            return d;
        }

        /// <summary>Enlist under the CURRENT token and drain a real VALIDATE
        /// worker until it is fightable. Returns the category, or "" if it never
        /// became ACTIVE — the caller decides whether that is a fail or a skip.</summary>
        static IEnumerator EnlistAndValidate(BuilderManager bm, string name,
                                             System.Action<string> done)
        {
            string err = null, snap = null;
            yield return LadderClient.Enlist(name, Fixture(name), (id, e) => { snap = id; err = e; });
            if (string.IsNullOrEmpty(snap)) { done(""); yield break; }

            var vnet = new HttpWorkerTransport(LadderClient.BaseUrl, WorkerKey, "VALIDATE");
            int guard = 0;
            List<MyRobot> m = null;
            while (guard++ < 12)
            {
                yield return ValidateWorkerLoop.RunOnce(vnet, bm, null);
                yield return LadderClient.MyRobots((r, e) => { m = r; });
                if (m != null && m.Count > 0 && m[0].CanFight) break;
            }
            done(m != null && m.Count > 0 && m[0].CanFight ? m[0].category : "");
        }

        static bool BoardHas(IReadOnlyList<LadderEntry> board, string name)
        {
            if (board == null) return false;
            foreach (var e in board) if (e.robotName == name) return true;
            return false;
        }

        const string PW = "bench-password-1";

        static IEnumerator All()
        {
            Note("server: " + LadderClient.BaseUrl);

            // ⚠ NEVER AGAINST PRODUCTION. It registers accounts and uploads
            // robots; pointed at the live ladder it would write junk into real
            // players' boards and pass while doing it.
            if (LadderClient.IsProduction)
            {
                Fail("REFUSED: BaseUrl is PRODUCTION. This bench writes accounts and robots.");
                Done(); yield break;
            }

            var bm = FindFirstObjectByType<BuilderManager>();
            if (bm == null) { Fail("a BuilderManager is in the scene"); Done(); yield break; }
            string ownerBay = bm.SnapshotString();

            string savedToken = LadderClient.Token;
            var savedData = Career.Data;
            bool savedActive = Career.active;
            int holdsBefore = Career.AutosaveHolds;

            List<LadderEntry> probe = null; string err = null;
            yield return LadderClient.Leaderboard("", (r, e) => { probe = r; err = e; });
            if (!string.IsNullOrEmpty(err))
            {
                // The 5000-vs-5099 lesson: say the URL. A skip that does not
                // name what it could not reach is a skip nobody acts on.
                Skip("the whole bench", "no server at " + LadderClient.BaseUrl + " (" + err + ")");
                Done(); yield break;
            }
            Pass("the API answers");
            Note("pound-for-pound board holds " + (probe == null ? 0 : probe.Count) + " row(s)");

            string tag = DateTime.UtcNow.ToString("HHmmss") + "-" + UnityEngine.Random.Range(1000, 9999);

            var hold = Career.SuspendAutosave();
            Check(Career.AutosaveHolds == holdsBefore + 1,
                  "the autosave hold is COUNTED, not a boolean anyone can stamp on");

            // ⚠ THE BODY IS ITS OWN ITERATOR, and that is not style. The session
            // below gives up early in six different places, and a `yield break`
            // inside this try would skip everything after the finally —
            // including Done(), which is the flag every caller polls. A bench
            // that abandons its own completion signal looks like a hung editor.
            bool restored = false;
            try
            {
                yield return Session(bm, tag);
            }
            finally
            {
                Career.Data = savedData;
                Career.active = savedActive;
                LadderClient.Token = savedToken;
                hold.Dispose();
                restored = true;
            }

            log.Add("== D. owner state ==");
            Check(restored, "career state was restored in a finally, not on the happy path");
            Check(Career.Data == savedData, "Career.Data is the object it was before");
            Check(Career.active == savedActive, "Career.active is what it was, restored not assumed");
            Check(Career.AutosaveHolds == holdsBefore,
                  "the autosave hold was released — count is back to " + holdsBefore);
            Check(bm.SnapshotString() == ownerBay, "the bay is byte-identical to before the bench");

            Done();
        }

        static IEnumerator Session(BuilderManager bm, string tag)
        {
            string err = null;
            string emailR = "bench-return-" + tag + "@example.test";
            string nameR = "ReturnBot-" + tag;

            {
                // ============================================================
                // A. a first session: make an account, enlist, get validated
                // ============================================================
                log.Add("== A. the first session ==");

                yield return LadderClient.Register(emailR, PW, "Returning Player", (who, e) => { err = e; });
                if (!string.IsNullOrEmpty(err))
                {
                    Skip("everything after registration", "register failed: " + err);
                    yield break;
                }
                Pass("an account can be created");

                string catR = "";
                yield return EnlistAndValidate(bm, nameR, c => { catR = c; });
                Check(catR.Length > 0,
                      "the first session enlists a robot and a worker validates it ("
                      + (catR.Length > 0 ? catR : "never became ACTIVE") + ")");
                if (catR.Length == 0)
                {
                    Skip("the returning-player half", "nothing was ever put on the board");
                    yield break;
                }

                // ============================================================
                // B. SIGN OUT, SIGN BACK IN — the verb no bench had ever used
                // ============================================================
                log.Add("== B. signing back in ==");

                LadderClient.Logout();
                Check(!LadderClient.SignedIn, "signing out clears the token");

                // The control leg, and it is not decorative: if a WRONG password
                // also "succeeded", the check below would pass for the wrong
                // reason and this whole section would be measuring nothing.
                string badWho = "x", badErr = null;
                yield return LadderClient.Login(emailR, PW + "-wrong", (w, e) => { badWho = w; badErr = e; });
                Check(!string.IsNullOrEmpty(badErr),
                      "the WRONG password is refused -- "
                      + (badErr ?? ("IT WAS ACCEPTED, as \"" + badWho + "\"")));
                Check(!LadderClient.SignedIn, "…and a refused login leaves you signed OUT, holding no token");

                string who = null;
                yield return LadderClient.Login(emailR, PW, (w, e) => { who = w; err = e; });
                Check(string.IsNullOrEmpty(err) && LadderClient.SignedIn,
                      "a RETURNING player can sign back in with the same credentials"
                      + (err != null ? " -- " + err : ""));
                if (!LadderClient.SignedIn)
                {
                    Skip("everything after sign-in", "the returning session has no token");
                    yield break;
                }
                Check(who == "Returning Player",
                      "…and the server hands back who they are (" + (who ?? "null") + ")");

                List<MyRobot> mine = null;
                yield return LadderClient.MyRobots((r, e) => { mine = r; });
                bool ownsIt = false;
                if (mine != null) foreach (var m in mine) if (m.name == nameR && m.CanFight) ownsIt = true;
                Check(ownsIt, "the returning session still owns the robot the first session enlisted");

                // ------------------------------------------------------------
                // B2. a DEAD token retires the session — the "app update wiped
                // my fights" bug. The records are safe server-side; an expired
                // session used to leave the dock looking signed IN over an empty
                // board, with no token that works and no prompt to re-auth.
                // Tamper the token so the server 401s, make one authed call, and
                // the client must sign us OUT and raise SessionExpired so the
                // dock can say why — not fail silently to a blank MY FIGHTS.
                // ------------------------------------------------------------
                LadderClient.SessionExpired = false;
                string tampered = LadderClient.Token + "-tampered";   // invalid signature -> 401
                LadderClient.Token = tampered;
                List<InboxEntry> deadInbox = null; string deadErr = null;
                yield return LadderClient.Inbox((rows, e) => { deadInbox = rows; deadErr = e; });
                Check(!LadderClient.SignedIn,
                      "a 401 on a token-bearing call RETIRES the dead session "
                      + "(no silent empty screen over a signed-in-looking dock)");
                Check(LadderClient.SessionExpired,
                      "…and raises SessionExpired so the dock shows WHY the sign-in form is back");

                // coming back the honest way clears the flag and restores state
                yield return LadderClient.Login(emailR, PW, (w, e) => { err = e; });
                Check(LadderClient.SignedIn && !LadderClient.SessionExpired,
                      "signing back in clears the expiry and restores the session"
                      + (err != null ? " -- " + err : ""));

                // ============================================================
                // C. the DOCK, which is where the blocker actually lived
                // ============================================================
                log.Add("== C. leaving the ARENA and coming back ==");

                Career.active = true;
                Career.Data = Scratch(nameR);
                MobileBuilderUI.forceMobileUI = true;
                if (MobileBuilderUI.inst != null)
                {
                    // Rebuild under CAREER state — the ARENA tab is career-only,
                    // so a dock built in the sandbox has no tab 4 at all.
                    UnityEngine.Object.Destroy(MobileBuilderUI.inst.gameObject);
                    yield return null;
                }
                float t0 = Time.realtimeSinceStartup;
                while (!MobileBuilderUI.Active && Time.realtimeSinceStartup - t0 < 6f) yield return null;
                var ui = MobileBuilderUI.inst;
                Check(ui != null, "the mobile dock attaches");
                if (ui == null) { Skip("the ARENA half", "no dock"); yield break; }
                yield return null; yield return null;

                ui.SetDockOpen(true);
                ui.ShowTab(4);                                   // ARENA
                yield return null;
                var arena = ui.TestArenaScreen;
                Check(arena != null, "opening the ARENA tab builds the ladder screen");
                if (arena == null) { Skip("the refetch checks", "no ArenaScreen"); yield break; }

                // Let the first load land.
                t0 = Time.realtimeSinceStartup;
                while (arena.Busy && Time.realtimeSinceStartup - t0 < 15f) yield return null;
                Check(arena.TestRefreshes >= 1, "…and it loads the board once on first open");
                Check(BoardHas(arena.Board, nameR),
                      "the first session's own robot is on the board it is looking at");
                int fetchesAfterOpen = arena.TestRefreshes;
                Note("board on first open: " + arena.Board.Count + " row(s) in " + arena.CategoryLabel);

                // ---- flicking in and out must NOT spend a request -----------
                // The gate is elapsed realtime and this is the half that keeps
                // it from becoming "GET the ladder on every tab tap".
                ui.ShowTab(0); yield return null;
                ui.ShowTab(4); yield return null;
                ui.ShowTab(0); yield return null;
                ui.ShowTab(4); yield return null;
                Check(arena.TestRefreshes == fetchesAfterOpen,
                      "re-entering within the window spends NO request ("
                      + arena.TestRefreshes + " vs " + fetchesAfterOpen + ")");

                // ---- something lands while the player is away ---------------
                ui.ShowTab(0); yield return null;

                string emailS = "bench-rival-" + tag + "@example.test";
                string nameS = "RivalBot-" + tag;
                LadderClient.Logout();
                yield return LadderClient.Register(emailS, PW, "Rival", (w, e) => { err = e; });
                string catS = "";
                if (string.IsNullOrEmpty(err))
                    yield return EnlistAndValidate(bm, nameS, c => { catS = c; });

                // Back to the returning player's own session before touching the
                // dock again — the board the DOCK reads must be read as them.
                LadderClient.Logout();
                yield return LadderClient.Login(emailR, PW, (w, e) => { err = e; });

                // THE CONTROL LEG. Before asserting the dock cannot see the new
                // row, prove the row is actually visible to a client at all: the
                // board is LIMIT 50 by rating, so on a busy dev database a fresh
                // placement can legitimately be off the end of it. Without this,
                // a full board would read as a broken refetch.
                List<LadderEntry> control = null;
                yield return LadderClient.Leaderboard(catR, (r, e) => { control = r; });
                bool rivalIsPublic = catS.Length > 0 && BoardHas(control, nameS);
                Note("rival " + nameS + " category=" + (catS.Length > 0 ? catS : "(none)")
                     + "  visible on the public " + catR + " board: " + rivalIsPublic
                     + "  (board holds " + (control == null ? 0 : control.Count) + ")");

                // ---- come back ----------------------------------------------
                // Wait out the product's OWN window rather than a copy of the
                // number, then re-enter exactly as a returning player does.
                float wait = MobileBuilderUI.ARENA_REFETCH_S + 1.5f;
                Note("waiting " + wait.ToString("0.0") + "s — the dock's refetch window");
                t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < wait) yield return null;

                int before = arena.TestRefreshes;
                ui.ShowTab(4); yield return null;
                Check(arena.TestRefreshes > before,
                      "coming back after the window REFETCHES the board ("
                      + before + " -> " + arena.TestRefreshes + ")");

                t0 = Time.realtimeSinceStartup;
                while (arena.Busy && Time.realtimeSinceStartup - t0 < 15f) yield return null;

                if (!rivalIsPublic)
                {
                    Skip("the row that landed while away appears",
                         catS.Length == 0
                           ? "the rival never became ACTIVE, so nothing landed"
                           : "the rival is not on the public " + catR
                             + " board either (LIMIT 50) — nothing to see, and that is not the dock's fault");
                }
                else
                {
                    // ⚠ THIS IS THE CHECK THAT FAILS WITHOUT THE FIX. The row
                    // did not exist when this screen last loaded, so it can only
                    // be here if re-entering the tab asked the server again.
                    Check(BoardHas(arena.Board, nameS),
                          "the row that landed SERVER-SIDE while the player was away is now on their board");
                }
            }
        }

        static void Done()
        {
            finished = true;
            Debug.Log("[Returning] " + Report());
        }
    }
}
#endif
