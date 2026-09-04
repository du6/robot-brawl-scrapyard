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
                                     : (touch != null && touch.finished);
        }
        static void Tick()
        {
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
                // -batchmode -nographics has no Device Simulator, so the editor's
                // DeviceWantsTouch() says no and ModeSelect draws the desktop
                // chooser forever. Take the exact path the touch button takes.
                if (which == "career" && Object.FindFirstObjectByType<RobotBrawl.Phase0.BuilderManager>() == null)
                    RobotBrawl.Phase0.ModeSelect.StartCareer(true);
                if (which == "career") RobotBrawl.Phase0.CareerSmoke.Run(); else touch = RobotBrawl.Phase0.TouchSmoke.Run();
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
