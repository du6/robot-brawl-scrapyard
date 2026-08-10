// ⚠ EDITOR AND DEVELOPMENT BUILDS ONLY — 2026-08-10.
// This is a QA harness. It was compiling into the SHIPPED iOS player: 44 such
// files, ~13k lines, and the string "dev-only-worker-key" was verified present
// in build/ios global-metadata.dat. Dead weight in the binary, and a surface
// that can register accounts and start fights inside a player's app.
// Nothing in the product references any harness — checked against every
// product script, every scene and every prefab before this guard was added.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
// ===========================================================================
// EnlistUiBench — the ENLIST BUTTON, not the client under it. 2026-08-10.
//
//   RobotBrawl.Phase0.EnlistUiBench.Run();      // play mode, API running
//   RobotBrawl.Phase0.EnlistUiBench.finished / passed / failed / skipped
//
// EnlistLiveBench proves LadderClient.Enlist over the real transport, 21/21.
// It proves NOTHING about ArenaScreen.DoEnlist, because it deliberately
// bypasses it: DoEnlist reads Career.Data.stable[activeRobot], and a bench
// that touched owner state to test a button would be trading the thing this
// project protects for the thing it was measuring.
//
// So the button shipped with its network half green and the glue a player
// actually presses never once executed. That glue is where the interesting
// mistakes live — it decides WHICH build is sent, whether a program rides
// along, and what the player is told afterwards.
//
// ⚠ HOW THIS IS SAFE, and it is the §5 mechanism, used as intended:
//   * a COUNTED Career.SuspendAutosave() hold for the whole run, so nothing
//     can write the save even if a code path under test asks it to;
//   * Career.Data swapped for a SCRATCH CareerData and restored in a finally;
//   * Career.active restored to whatever it was, never assumed true;
//   * the save fingerprinted by the caller either side, mtime included.
// The hold is counted precisely so this can nest inside anything else that
// suspends — a save/restore of one global is not safe when two holders
// overlap, which is what overwrote owen's career on 2026-08-10.
//
// ⚠ It WRITES TO THE SERVER. Dev database only.
// ===========================================================================
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RobotBrawl.Phase0
{
    public class EnlistUiBench : MonoBehaviour
    {
        public static bool finished;
        public static int passed, failed, skipped;
        static readonly List<string> log = new List<string>();

        static void Pass(string w) { passed++; log.Add("PASS  " + w); }
        static void Fail(string w) { failed++; log.Add("FAIL  " + w); }
        static void Skip(string w, string why) { skipped++; log.Add("SKIP  " + w + " -- " + why); }
        static void Note(string w) { log.Add("      " + w); }
        static void Check(bool c, string w) { if (c) Pass(w); else Fail(w); }

        public static void Run()
        {
            finished = false; passed = 0; failed = 0; skipped = 0; log.Clear();
            var go = new GameObject("enlist_ui_bench");
            go.AddComponent<EnlistUiBench>().StartCoroutine(All());
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

        static CareerData Scratch(string robotName, bool withProgram, bool withBuild)
        {
            var d = new CareerData();
            var r = new CareerRobot
            {
                name = robotName,
                snapshot = withBuild ? BuilderManager.SNAP_STAMP + "\n" + LEGAL_BUILD : "",
                program = withProgram ? RobotProgram.FirstSteps().ToJson() : "",
            };
            d.stable.Add(r);
            d.activeRobot = 0;
            return d;
        }

        // ---- driving a DRAW from a real GUI context -------------------------
        // ⚠ GUILayout OUTSIDE OnGUI THROWS: "You can only call GUI functions
        // from inside OnGUI." The first version of this bench called the draw
        // seam straight from the coroutine and got three identical exceptions,
        // which looked exactly like three broken panel states and were one
        // broken bench. So the request is queued here and serviced by this
        // component's OWN OnGUI, which is a legitimate GUI context.
        static ArenaScreen drawTarget;
        static int drawRequests, drawsDone;
        static string drawError = "";

        void OnGUI()
        {
            if (drawRequests <= drawsDone || drawTarget == null) return;
            try { drawTarget.TestDrawEnlistOnce(); }
            catch (Exception e) { drawError = e.GetType().Name + ": " + e.Message; }
            drawsDone++;
        }

        static IEnumerator DrawOnce(ArenaScreen ui)
        {
            drawTarget = ui;
            int want = drawsDone + 1;
            drawRequests = want;
            int guard = 0;
            while (drawsDone < want && guard++ < 240) yield return null;
        }

        static IEnumerator WaitIdle(ArenaScreen ui, float seconds)
        {
            float t = 0f;
            while (ui.TestBusy && t < seconds) { t += Time.deltaTime; yield return null; }
            yield return null;
        }

        static IEnumerator All()
        {
            Note("server: " + LadderClient.BaseUrl);

            // ⚠ NEVER AGAINST PRODUCTION — it registers accounts and uploads
            // robots. See EnlistLiveBench for the argument.
            if (LadderClient.IsProduction)
            {
                Fail("REFUSED: BaseUrl is PRODUCTION. This bench writes accounts.");
                Done(); yield break;
            }

            // ---- is anyone home? -------------------------------------------
            List<LadderEntry> board = null; string err = null;
            yield return LadderClient.Leaderboard("", (r, e) => { board = r; err = e; });
            if (!string.IsNullOrEmpty(err))
            {
                Skip("the whole bench", "no server at " + LadderClient.BaseUrl + " (" + err + ")");
                Done(); yield break;
            }
            Pass("the API answers");

            // ---- take the ground away from every writer ---------------------
            var savedData = Career.Data;
            bool savedActive = Career.active;
            string savedToken = LadderClient.Token;
            int holdsBefore = Career.AutosaveHolds;
            var hold = Career.SuspendAutosave();
            Check(Career.AutosaveHolds == holdsBefore + 1,
                  "the autosave hold is COUNTED, not a boolean anyone can stamp on");

            var go = new GameObject("bench_arena_screen");
            ArenaScreen ui = go.AddComponent<ArenaScreen>();
            yield return null;                       // let Start() run

            bool restored = false;
            try
            {
                string tag = DateTime.UtcNow.ToString("HHmmss") + "-" + UnityEngine.Random.Range(1000, 9999);

                // ============================================================
                // A. the refusals, which cost no upload
                // ============================================================
                log.Add("== A. what it refuses, and whether it says why ==");

                Career.active = false;
                Career.Data = new CareerData();
                ui.TestEnlist();
                yield return WaitIdle(ui, 3f);
                Check(ui.TestStatus.IndexOf("career", StringComparison.OrdinalIgnoreCase) >= 0,
                      "with no career it names the CAREER as the missing thing -- got: " + ui.TestStatus);

                // A saved robot with no build is the day-one state: the player
                // has a career and has never pressed SAVE.
                Career.active = true;
                Career.Data = Scratch("Unsaved-" + tag, true, false);
                ui.TestSetEnlistName("");
                ui.TestEnlist();
                yield return WaitIdle(ui, 3f);
                Check(ui.TestStatus.IndexOf("SAVE", StringComparison.OrdinalIgnoreCase) >= 0,
                      "with no saved build it tells the player to SAVE -- got: " + ui.TestStatus);

                // Neither refusal may have touched the network.
                List<MyRobot> none = null;
                yield return LadderClient.MyRobots((r, e) => { none = r; });
                Check(none == null || none.Count == 0,
                      "no robot was uploaded by either refusal");

                // ============================================================
                // B. the real button, against a real server
                // ============================================================
                log.Add("== B. the button a player actually presses ==");

                string email = "bench-ui-" + tag + "@example.test";
                yield return LadderClient.Register(email, "bench-password-1", "Bench UI",
                                                   (who, e) => { err = e; });
                if (!string.IsNullOrEmpty(err))
                {
                    Skip("the upload half", "register failed: " + err);
                }
                else
                {
                    Pass("signed in");
                    string name = "UiBot-" + tag;
                    Career.Data = Scratch(name, true, true);

                    // The name field is left EMPTY on purpose: DoEnlist must
                    // fall back to the saved robot's own name rather than
                    // refusing, because the panel pre-fills it and a player
                    // who clears it still means "this robot".
                    ui.TestSetEnlistName("");
                    ui.TestEnlist();
                    yield return WaitIdle(ui, 20f);

                    List<MyRobot> mine = null;
                    yield return LadderClient.MyRobots((r, e) => { mine = r; });
                    bool up = mine != null && mine.Count == 1 && mine[0].name == name;
                    Check(up, "the BUTTON uploaded the saved career robot under its own name"
                              + (up ? "" : " -- status: " + ui.TestStatus));

                    // It must not claim the robot is ranked. It is PENDING, and
                    // a player told "you're on the ladder" goes looking for
                    // themselves on a board they are not on yet.
                    string st = ui.TestStatus ?? "";
                    Check(st.IndexOf("worker", StringComparison.OrdinalIgnoreCase) >= 0
                       || st.IndexOf("checks", StringComparison.OrdinalIgnoreCase) >= 0,
                          "…and says a worker still has to check it -- got: " + st);

                    // ---- the no-program warning ------------------------------
                    // program "" is AI-DRIVEN to the worker, not missing, so a
                    // statue is indistinguishable from a choice downstream.
                    // The player has to be told at the only moment it is fixable.
                    string name2 = "UiBotNoProg-" + tag;
                    Career.Data = Scratch(name2, false, true);
                    ui.TestSetEnlistName(name2);
                    ui.TestEnlist();
                    yield return WaitIdle(ui, 20f);
                    string st2 = ui.TestStatus ?? "";
                    Check(st2.IndexOf("no program", StringComparison.OrdinalIgnoreCase) >= 0,
                          "a robot with NO PROGRAM is warned it will stand still -- got: " + st2);
                    Check(st2.IndexOf("uploaded", StringComparison.OrdinalIgnoreCase) >= 0,
                          "…and it is uploaded anyway, because \"\" is a legal choice");
                }

                // ============================================================
                // C. the panel draws against every state it can be handed
                // ============================================================
                log.Add("== C. the panel itself ==");
                ui.TestShowEnlist = true;
                drawError = "";

                Career.active = false; Career.Data = new CareerData();
                yield return DrawOnce(ui);
                Check(drawError == "", "the panel draws with NO CAREER -- " + drawError);

                drawError = "";
                Career.active = true; Career.Data = Scratch("Draw-A", true, false);
                yield return DrawOnce(ui);
                Check(drawError == "", "the panel draws with a career but NO SAVED BUILD -- " + drawError);

                drawError = "";
                Career.Data = Scratch("Draw-B", true, true);
                yield return DrawOnce(ui);
                Check(drawError == "", "the panel draws with a saved robot -- " + drawError);
            }
            finally
            {
                Career.Data = savedData;
                Career.active = savedActive;
                LadderClient.Token = savedToken;
                if (go != null) UnityEngine.Object.Destroy(go);
                hold.Dispose();
                restored = true;
            }

            log.Add("== D. owner state ==");
            Check(restored, "career state was restored in a finally, not on the happy path");
            Check(Career.Data == savedData, "Career.Data is the object it was before");
            Check(Career.active == savedActive, "Career.active is what it was, restored not assumed");
            Check(Career.AutosaveHolds == holdsBefore,
                  "the autosave hold was released — count is back to " + holdsBefore);

            Done();
        }

        static void Done()
        {
            finished = true;
            Debug.Log("[EnlistUi] " + Report());
        }
    }
}
#endif
