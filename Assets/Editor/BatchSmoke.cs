// Batch-mode driver for the two UI smokes, one per invocation (they clobber
// each other when run together - CLAUDE.md, Bench notes):
//   Unity -batchmode -nographics -projectPath . \
//         -executeMethod RobotBrawl.EditorTools.BatchSmoke.Career -logFile -
//   Unity ... -executeMethod RobotBrawl.EditorTools.BatchSmoke.Touch -logFile -
// Same shape as BatchStarterBench: EnterPlaymode, re-armed across the domain
// reload by a SessionState flag, exit 0 when the bench reports finished.
using UnityEditor;
using UnityEngine;

namespace RobotBrawl.EditorTools
{
    public static class BatchSmoke
    {
        public static void Quick()  { Arm("quick"); }
        public static void Career() { Arm("career"); }
        public static void Touch()  { Arm("touch"); }
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
        static BatchSmokeBoot()
        {
            which = SessionState.GetString("rb_smoke", "");
            if (string.IsNullOrEmpty(which)) return;
            armedAt = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
        }
        static bool Finished()
        {
            return which == "career" ? RobotBrawl.Phase0.CareerSmoke.finished
                                     : which == "quick" ? RobotBrawl.Phase0.QuickFightBench.finished
                                     : (touch != null && touch.finished);
        }
        static void Tick()
        {
            // The cap that -batchmode honours: hold each editor-loop frame to
            // ~16 ms while the bench runs, so "yield return null" means what
            // it means on a phone and the 20 ms physics step actually happens
            // between a placement and the tap that raycasts at it.
            if (launched && Application.isPlaying) System.Threading.Thread.Sleep(16);
            if (Application.isPlaying && !launched)
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
                if (which == "career" && Object.FindFirstObjectByType<RobotBrawl.Phase0.BuilderManager>() == null)
                    RobotBrawl.Phase0.ModeSelect.StartCareer(true);
                if (which == "quick" && Object.FindFirstObjectByType<RobotBrawl.Phase0.BuilderManager>() == null)
                    RobotBrawl.Phase0.ModeSelect.StartCareer(true);
                if (which == "career") RobotBrawl.Phase0.CareerSmoke.Run();
                else if (which == "quick") RobotBrawl.Phase0.QuickFightBench.Run();
                else touch = RobotBrawl.Phase0.TouchSmoke.Run();
            }
            if (launched && Finished())
            {
                Debug.Log("[BatchSmoke] " + which + " finished - see the bench's own summary lines above");
                SessionState.SetString("rb_smoke", "");
                if (Application.isBatchMode) EditorApplication.Exit(0);
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
