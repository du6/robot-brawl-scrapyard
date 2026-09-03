// Batch-mode driver for StarterBench: enter play mode headlessly, run the
// bench, exit with its verdict. Exists because the fight has to be MEASURED
// (house rule: a claim without a measurement is not a result) and the editor
// GUI is not always open.
//
//   Unity -batchmode -nographics -projectPath . \
//         -executeMethod RobotBrawl.EditorTools.BatchStarterBench.Run -logFile -
//   (NO -quit: play mode needs the editor loop alive; we Exit() ourselves.)
//
// ⚠ EnterPlaymode triggers a DOMAIN RELOAD, which wipes every delegate — so
// the play-mode half is re-armed by [InitializeOnLoad] reading a SessionState
// flag, which survives the reload. Exit codes: 0 bench ran, 2 timeout.
using UnityEditor;
using UnityEngine;

namespace RobotBrawl.EditorTools
{
    public static class BatchStarterBench
    {
        public static void Run()
        {
            SessionState.SetBool("rb_starterbench", true);
            Debug.Log("[BatchStarterBench] entering play mode");
            EditorApplication.EnterPlaymode();
        }
    }

    [InitializeOnLoad]
    static class BatchStarterBenchBoot
    {
        static bool launched;
        static double armedAt;

        static BatchStarterBenchBoot()
        {
            if (!SessionState.GetBool("rb_starterbench", false)) return;
            armedAt = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (Application.isPlaying && !launched)
            {
                launched = true;
                Debug.Log("[BatchStarterBench] play mode up - launching bench");
                RobotBrawl.Phase0.StarterBench.Run();
            }
            if (launched && RobotBrawl.Phase0.StarterBench.finished)
            {
                SessionState.SetBool("rb_starterbench", false);
                Debug.Log("[BatchStarterBench] bench finished - exiting");
                if (Application.isBatchMode) EditorApplication.Exit(0);
                EditorApplication.update -= Tick;
            }
            // A hung fight must not hold the shell forever.
            if (EditorApplication.timeSinceStartup - armedAt > 900.0)
            {
                Debug.LogError("[BatchStarterBench] TIMEOUT after 15 min");
                SessionState.SetBool("rb_starterbench", false);
                if (Application.isBatchMode) EditorApplication.Exit(2);
                EditorApplication.update -= Tick;
            }
        }
    }
}
