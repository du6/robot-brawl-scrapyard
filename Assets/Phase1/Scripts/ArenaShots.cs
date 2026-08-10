// ===========================================================================
// ArenaShots — photograph every ARENA surface. 2026-08-10.
//
//   RobotBrawl.Phase0.ArenaShots.Run("/tmp/arena");   // play mode, API up
//   RobotBrawl.Phase0.ArenaShots.finished / shots / LastError
//
// §7.5 says the ARENA layout is "placeholder and largely unjudged". It could
// not be judged, because it could not be OPENED: ArenaScreen appears in no
// scene, in no prefab, and no product code constructs it. Every surface is
// live code with no entry point — which is also why the missing ENLIST flow
// survived so long. Nobody could get to the screen to notice.
//
// So this builds the screen the way a bench does, drives it into each state,
// and writes a PNG per state. That is enough to JUDGE the layout now, and it
// stays useful afterwards as the before/after when the styling lands.
//
// ⚠ IMGUI IS INVISIBLE TO THE MCP CAPTURE TOOLS — they render from a camera
// and OnGUI is not in it. UiShot.Take grabs the game view after a settle
// delay, which is the only thing that sees this screen.
//
// ⚠ It signs in against a live API and uploads nothing; every state here is a
// GET plus one register. Dev database only.
// ===========================================================================
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RobotBrawl.Phase0
{
    public class ArenaShots : MonoBehaviour
    {
        public static bool finished;
        public static int shots;
        public static string LastError = "";
        static readonly List<string> log = new List<string>();

        public static string Report()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var l in log) sb.Append(l).Append('\n');
            sb.Append("===== ").Append(shots).Append(" shot(s) =====");
            return sb.ToString();
        }

        public static void Run(string dir)
        {
            finished = false; shots = 0; LastError = ""; log.Clear();
            var go = new GameObject("arena_shots");
            go.AddComponent<ArenaShots>().StartCoroutine(All(dir));
        }

        static IEnumerator Shot(string dir, string name)
        {
            string path = dir + "/" + name + ".png";
            UiShot.Take(path, 0.6f);
            int guard = 0;
            while (!UiShot.Done && guard++ < 600) yield return null;
            if (string.IsNullOrEmpty(UiShot.LastError))
            {
                shots++;
                log.Add("  " + name + ".png  " + UiShot.LastWidth + "x" + UiShot.LastHeight
                        + "  " + UiShot.LastBytes + " bytes");
            }
            else
            {
                LastError = UiShot.LastError;
                log.Add("  " + name + " FAILED: " + UiShot.LastError);
            }
        }

        static IEnumerator All(string dir)
        {
            try { System.IO.Directory.CreateDirectory(dir); }
            catch (Exception e) { LastError = e.Message; finished = true; yield break; }

            string savedToken = LadderClient.Token;
            var savedData = Career.Data;
            bool savedActive = Career.active;
            var hold = Career.SuspendAutosave();

            var go = new GameObject("shot_arena_screen");
            var ui = go.AddComponent<ArenaScreen>();
            yield return null;

            try
            {
                // ---- 1. signed OUT: the board a stranger sees --------------
                LadderClient.Logout();
                yield return new WaitForSeconds(1.2f);
                yield return Shot(dir, "01_signed_out_board");

                // ---- 2. the sign-in panel ---------------------------------
                // It is what a new player meets first, and it is the surface
                // that promises "signing in is what lets you enlist a robot".
                yield return Shot(dir, "02_sign_in");

                // ---- 3. signed IN: the board with a wallet ----------------
                string tag = DateTime.UtcNow.ToString("HHmmss") + "-" + UnityEngine.Random.Range(1000, 9999);
                string err = null;
                yield return LadderClient.Register("shots-" + tag + "@example.test",
                                                   "bench-password-1", "Shots " + tag,
                                                   (who, e) => { err = e; });
                if (!string.IsNullOrEmpty(err)) log.Add("  (register failed: " + err + ")");
                yield return new WaitForSeconds(1.5f);
                yield return Shot(dir, "03_board_signed_in");

                // ---- 4. the ENLIST panel, with a saved robot --------------
                Career.active = true;
                Career.Data = new CareerData();
                Career.Data.stable.Add(new CareerRobot
                {
                    name = "Spinner",
                    snapshot = BuilderManager.SNAP_STAMP + "\ncore|0.000,0.700,0.000|0|0.00,0.00,0.00|Aluminum\n",
                    program = RobotProgram.FirstSteps().ToJson(),
                });
                Career.Data.activeRobot = 0;
                ui.TestShowEnlist = true;
                yield return new WaitForSeconds(0.6f);
                yield return Shot(dir, "04_enlist_with_robot");

                // ---- 5. the ENLIST panel with nothing to send -------------
                // The day-one state, and the one most likely to be wrong.
                Career.Data = new CareerData();
                yield return new WaitForSeconds(0.4f);
                yield return Shot(dir, "05_enlist_nothing_saved");
                ui.TestShowEnlist = false;

                // ---- 6. the shop ------------------------------------------
                ui.ShowShop();
                yield return new WaitForSeconds(1.5f);
                yield return Shot(dir, "06_shop");
                ui.ShowLadder();
                yield return new WaitForSeconds(0.5f);
            }
            finally
            {
                Career.Data = savedData;
                Career.active = savedActive;
                LadderClient.Token = savedToken;
                if (go != null) UnityEngine.Object.Destroy(go);
                hold.Dispose();
            }

            finished = true;
            Debug.Log("[ArenaShots] " + Report());
        }
    }
}
