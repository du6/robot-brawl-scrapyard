// Batch-mode driver for the headless benches, one per invocation (they
// clobber each other when run together - CLAUDE.md, Bench notes):
//   Unity -batchmode -nographics -projectPath . \
//         -executeMethod RobotBrawl.EditorTools.BatchSmoke.Touch -logFile -
//   Unity ... -executeMethod RobotBrawl.EditorTools.BatchSmoke.Quick / .FightWorker
//   Unity ... -executeMethod RobotBrawl.EditorTools.BatchSmoke.Phone
//     (.Phone is PhoneLayoutBench: the same screens at owen's PHONE geometry
//      rather than the 640x480 desktop one -batchmode actually reports.)
// (Scrapyard: CareerSmoke and its .Career entry were deleted with the
// leagues' boot rules, 2026-09-09.)
// Same shape as BatchStarterBench: EnterPlaymode, re-armed across the domain
// reload by a SessionState flag, exit 0 when the bench reports finished.
using UnityEditor;
using UnityEngine;

namespace RobotBrawl.EditorTools
{
    public static class BatchSmoke
    {
        public static void Quick()  { Arm("quick"); }
        public static void FightWorker() { Arm("fightworker"); }
        public static void Map() { Arm("map"); }
        public static void Touch()  { Arm("touch"); }
        public static void Phone() { Arm("phone"); }
        public static void Place() { Arm("place"); }
        public static void Journey() { Arm("journey"); }
        public static void ReviewPure()
        {
            bool rewards = RobotBrawl.Phase0.CareerRewardsBench.RunPure();
            bool combat = RobotBrawl.Phase0.CombatReadabilityBench.RunPure();
            bool garage = RobotBrawl.Phase0.GarageUXBench.RunPure();
            bool boxes = RobotBrawl.Phase0.RewardBoxBench.RunPure();
            Debug.Log(RobotBrawl.Phase0.CareerRewardsBench.report);
            Debug.Log(RobotBrawl.Phase0.CombatReadabilityBench.report);
            Debug.Log(RobotBrawl.Phase0.GarageUXBench.report);
            Debug.Log(RobotBrawl.Phase0.RewardBoxBench.report);
            Debug.Log("[ReviewPure] rewards=" + rewards + " combat=" + combat + " garage=" + garage + " boxes=" + boxes);
            if (Application.isBatchMode) EditorApplication.Exit(rewards && combat && garage && boxes ? 0 : 1);
        }
        /// <summary>CareerRewardsBench is PURE - no scene, no play mode - so it
        /// runs and reports in one call instead of arming the frame loop.</summary>
        public static void Rewards()
        {
            bool ok = RobotBrawl.Phase0.CareerRewardsBench.RunPure();
            Debug.Log(RobotBrawl.Phase0.CareerRewardsBench.report);
            Debug.Log("[CareerRewardsBench] RESULT: " + RobotBrawl.Phase0.CareerRewardsBench.passed + " pass, "
                      + RobotBrawl.Phase0.CareerRewardsBench.failed + " fail - " + (ok ? "ALL GREEN" : "FIX NEEDED"));
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }
        static void Arm(string which)
        {
            SessionState.SetString("rb_smoke", which);
            Debug.Log("[BatchSmoke] entering play mode for " + which);
            EditorApplication.EnterPlaymode();
        }
    }

    [InitializeOnLoad]
    static class BatchSmokeBoot
    {
        static bool launched; static double armedAt; static string which;
        static RobotBrawl.Phase0.TouchSmoke touch;
        static RobotBrawl.Phase0.FightWorkerBench fw;
        static RobotBrawl.Phase0.MapBench mb;
        static RobotBrawl.Phase0.PhoneLayoutBench phone;
        static RobotBrawl.Phase0.PlaceUxBench place;
        static BatchSmokeBoot()
        {
            which = SessionState.GetString("rb_smoke", "");
            if (string.IsNullOrEmpty(which)) return;
            armedAt = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
        }
        static bool Finished()
        {
            return which == "place" ? (place != null && place.finished)
                                     : which == "phone" ? (phone != null && phone.finished)
                                     : which == "journey" ? RobotBrawl.Phase0.JourneyBench.finished
                                     : which == "quick" ? RobotBrawl.Phase0.QuickFightBench.finished
                                     : which == "fightworker" ? (fw != null && fw.finished)
                                     : which == "map" ? RobotBrawl.Phase0.MapBench.finished
                                     : (touch != null && touch.finished);
        }
        static void Tick()
        {
            // The cap that -batchmode honours: hold each editor-loop frame to
            // ~16 ms while the bench runs, so "yield return null" means what
            // it means on a phone and the 20 ms physics step actually happens
            // between a placement and the tap that raycasts at it.
            if (launched && Application.isPlaying) System.Threading.Thread.Sleep(16);
            // isPlaying can turn true before the runtime scene reload finishes.
            // Objects created then are discarded, leaving a bench waiting forever.
            if (Application.isPlaying && Time.frameCount >= 2 && !launched)
            {
                launched = true;
                Debug.Log("[BatchSmoke] play mode up - launching " + which);
                // CareerSmoke RIDES THE REAL AUTO-BOOT (it asserts the boot rules)
                // and walks the login gate's dev door itself; all it needs from
                // us is the touch flag, so ModeSelect.ShouldAutoBoot says yes
                // instead of drawing the desktop chooser and waiting for a click
                // that never comes in -batchmode. TouchSmoke builds its own world.
                RobotBrawl.Phase0.MobileBuilderUI.forceMobileUI = true;
                // A DEVICE-LIKE FRAME RATE. -nographics has no vsync and runs
                // frames as fast as the CPU allows, so a bench's "yield two
                // frames" can pass in under a physics step (20 ms) - a part
                // placed on frame N has no collider the raycast can see on
                // frame N+2, and every tap that raycasts (REMOVE, the gusset
                // applique) is silently eaten. Measured 2026-09-04: the
                // applique tap landed on the beam's exact screen centre and
                // RaycastAll saw only the floor. Real devices run at 60.
                Application.targetFrameRate = 60;   // ignored in -batchmode (measured 3-4k fps); the Sleep below is what caps
                // -batchmode -nographics has no Device Simulator, so the editor's
                // DeviceWantsTouch() says no and ModeSelect draws the desktop
                // chooser forever. Take the exact path the touch button takes.
                if ((which == "quick" || which == "map" || which == "phone" || which == "place") && Object.FindFirstObjectByType<RobotBrawl.Phase0.BuilderManager>() == null)
                    RobotBrawl.Phase0.ModeSelect.StartCareer(true);
                // Every headless bench asserts against the workshop; MapBench flips this back to prove the boot.
                RobotBrawl.Phase0.BuilderManager.bootToYard = false;
                if (which == "fightworker" && Object.FindFirstObjectByType<RobotBrawl.Phase0.BuilderManager>() == null)
                    new GameObject("BuilderManager").AddComponent<RobotBrawl.Phase0.BuilderManager>();
                if (which == "journey") RobotBrawl.Phase0.JourneyBench.Run();
                else if (which == "quick") RobotBrawl.Phase0.QuickFightBench.Run();
                else if (which == "map") mb = RobotBrawl.Phase0.MapBench.Run();
                else if (which == "phone") phone = RobotBrawl.Phase0.PhoneLayoutBench.Run();
                else if (which == "place") place = RobotBrawl.Phase0.PlaceUxBench.Run();
                else if (which == "fightworker") { RobotBrawl.Phase0.FightWorkerBench.RunPure(); fw = RobotBrawl.Phase0.FightWorkerBench.Run(); }
                else touch = RobotBrawl.Phase0.TouchSmoke.Run();
            }
            if (launched && Finished())
            {
                Debug.Log("[BatchSmoke] " + which + " finished - see the bench's own summary lines above");
                SessionState.SetString("rb_smoke", "");
                int failures = which == "place" ? (place != null ? place.failed : 0)
                             : which == "phone" ? (phone != null ? phone.failed : 0)
                             : which == "map" ? RobotBrawl.Phase0.MapBench.failed
                             : which == "journey" ? RobotBrawl.Phase0.JourneyBench.failed
                             : which == "quick" ? RobotBrawl.Phase0.QuickFightBench.failed
                             : touch != null ? touch.failed : 0;
                if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
                EditorApplication.update -= Tick;
            }
            if (EditorApplication.timeSinceStartup - armedAt > 1200.0)
            {
                Debug.LogError("[BatchSmoke] TIMEOUT after 20 min");
                SessionState.SetString("rb_smoke", "");
                if (Application.isBatchMode) EditorApplication.Exit(2);
                EditorApplication.update -= Tick;
            }
        }
    }
}
