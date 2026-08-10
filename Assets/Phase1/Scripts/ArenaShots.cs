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

        /// <summary>Reach the ARENA the way a PLAYER does — through the mobile
        /// dock's tab — and photograph it. `Run` builds ArenaScreen itself,
        /// which proves the screen and says nothing about whether anything can
        /// open it. That distinction is the whole reason this file exists.
        ///
        /// Career state is held and restored: forcing career mode to see a
        /// career-only tab is still touching owner state.</summary>
        public static void RunMobileTab(string dir)
        {
            finished = false; shots = 0; LastError = ""; log.Clear();
            var go = new GameObject("arena_tab_shots");
            go.AddComponent<ArenaShots>().StartCoroutine(MobileTab(dir));
        }

        static IEnumerator MobileTab(string dir)
        {
            try { System.IO.Directory.CreateDirectory(dir); }
            catch (Exception e) { LastError = e.Message; finished = true; yield break; }

            var savedData = Career.Data;
            bool savedActive = Career.active;
            bool savedForce = MobileBuilderUI.forceMobileUI;
            var hold = Career.SuspendAutosave();
            try
            {
                Career.active = true;
                if (Career.Data == null) Career.Data = new CareerData();
                MobileBuilderUI.forceMobileUI = true;

                float t = 0f;
                while (!MobileBuilderUI.Active && t < 8f) { t += Time.deltaTime; yield return null; }
                var ui = MobileBuilderUI.inst;
                if (ui == null) { LastError = "the mobile dock never came up"; yield break; }

                // ⚠ OPEN THE DOCK FIRST. Every panel is gated on `dockOpen`,
                // ARENA included, so TestShowTab against a collapsed dock
                // switches the tab and shows nothing — the first run of this
                // photographed a correct ARENA tab strip over an empty screen
                // and would have been filed as proof. Tapping the tab for real
                // opens the dock (the button handler does it); a direct
                // TestShowTab does not.
                ui.SetDockOpen(true);
                yield return null;

                // Index 4. If ARENA is not there, this photographs whatever is,
                // which is why the log records the tab it actually landed on.
                ui.TestShowTab(4);
                yield return null; yield return null;
                log.Add("  dock tab=" + ui.TestTab + " (4 = ARENA) dockOpen=" + ui.DockOpen);
                yield return new WaitForSeconds(1.2f);
                yield return Shot(dir, "07_arena_tab");

                // The dock builds ArenaScreen on first open, so it only exists
                // now — grabbing it earlier would find nothing.
                var arena = UnityEngine.Object.FindFirstObjectByType<ArenaScreen>();

                // The SCOUTING CARD, surface 2. Scouted through the board's own
                // button rather than by poking state, because "the card opens
                // when you tap SCOUT" is the half a screenshot of the panel
                // alone would not prove.
                if (arena != null && arena.Board.Count > 0)
                {
                    LadderEntry target = null;
                    foreach (var e in arena.Board)
                        if (!string.IsNullOrEmpty(e.activeSnapshotId)) { target = e; break; }
                    if (target != null)
                    {
                        arena.ScoutNow(target);
                        float t2 = 0f;
                        while (arena.Card == null && t2 < 8f) { t2 += Time.deltaTime; yield return null; }
                        yield return new WaitForSeconds(0.8f);
                        yield return Shot(dir, "12_arena_scout_card");
                        log.Add("  card=" + (arena.Card != null ? arena.Card.robotName : "(none)")
                                + " blocker=" + (arena.ChallengeBlocker(arena.Card) ?? "(none — can challenge)"));
                        arena.CloseCard();
                        yield return null;
                    }
                }

                ui.TestShowTab(1);
                yield return null; yield return null;
                yield return new WaitForSeconds(0.6f);
                yield return Shot(dir, "08_league_with_trophy_case");

                // The one SHOP, both shelves. COSMETICS is the half that used
                // to be a second shop inside the ARENA.
                ui.TestShowTab(3);
                yield return null; yield return null;
                yield return new WaitForSeconds(0.5f);
                yield return Shot(dir, "09_shop_parts");

                // SIGNED OUT first: cosmetics need an account, and that state
                // has to read as an instruction rather than an error.
                LadderClient.Logout();
                ui.TestShowShopSection(1);
                yield return new WaitForSeconds(1.5f);
                yield return Shot(dir, "10_shop_cosmetics_signed_out");

                // ...then SIGNED IN, which is the state the consolidation was
                // actually for: the shelf, the wallet, and the one-way valve
                // that had been buried in a shop with no entry point. A
                // screenshot of the signed-out panel proves none of that.
                string tag = DateTime.UtcNow.ToString("HHmmss") + "-" + UnityEngine.Random.Range(1000, 9999);
                string rerr = null;
                yield return LadderClient.Register("shopshots-" + tag + "@example.test",
                                                   "bench-password-1", "Shop " + tag,
                                                   (who, e) => { rerr = e; });
                if (!string.IsNullOrEmpty(rerr)) log.Add("  (register failed: " + rerr + ")");
                ui.TestShowShopSection(0);
                yield return null;
                ui.TestShowShopSection(1);
                yield return new WaitForSeconds(2.0f);
                yield return Shot(dir, "11_shop_cosmetics_signed_in");
                ui.TestShowShopSection(0);

                ui.TestShowTab(0);
                yield return null;
            }
            finally
            {
                MobileBuilderUI.forceMobileUI = savedForce;
                Career.Data = savedData;
                Career.active = savedActive;
                hold.Dispose();
            }
            finished = true;
            Debug.Log("[ArenaShots] " + Report());
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

                // The shop used to be shot here. It left this screen on
                // 2026-08-10 — cosmetics, the wallet and the deposit valve now
                // live on the career SHOP tab — so it is photographed through
                // the dock in RunMobileTab instead, where it now is.
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
