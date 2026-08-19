// ===========================================================================
// WorkerBootstrap.cs — start the worker automatically, but ONLY in the worker
// build. 2026-08-09.
//
// A headless container has nobody to press play, so something has to launch
// WorkerHost on its own. The obvious ways to do that are both wrong here:
//
//   * a component dropped in Main.unity would start a worker in EVERY editor
//     play session, including owen's, and every bench that enters play mode —
//     each of them quietly claiming real jobs from whatever RB_API_URL points
//     at. A bench that steals production work is a genuinely bad outcome.
//   * an unconditional RuntimeInitializeOnLoadMethod has the same problem: it
//     runs in the editor too.
//
// So this is gated on the RB_WORKER scripting define, which is set by
// BuildWorker.Build() for the server build and by nothing else. In the editor
// this file compiles to an empty class and does nothing at all.
//
// To run the worker IN the editor on purpose — which is how it was first
// proven against the live cloud — call RobotBrawl.Phase0.WorkerHost.Launch()
// by hand. Deliberate, never automatic.
// ===========================================================================
using UnityEngine;

namespace RobotBrawl.Phase0
{
    public static class WorkerBootstrap
    {
#if RB_WORKER
        /// <summary>AfterSceneLoad, not BeforeSceneLoad: WorkerHost looks for
        /// the BuilderManager in the scene, so the scene has to exist first.
        /// Before-load would find nothing and the host would exit 3 claiming
        /// the scene has no builder, which is a confusing way to say "too
        /// early".</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoStart()
        {
            Debug.Log("[WorkerBootstrap] RB_WORKER build — launching the worker host");

            // ⚠ NOBODY CAN PRESS "START CAREER" IN A CONTAINER, AND THAT IS THE
            // ONLY THING THAT EVER MADE A BuilderManager. Main.unity holds a
            // camera, a light and nothing else — unchanged since 2026-07-29 —
            // and the sole creator of a BuilderManager is StartCareer(), which
            // is reached by clicking a GUI button on ModeSelect. WorkerHost
            // then requires one and Quit(3)s without it, so a worker built
            // from this source booted, found no builder and died:
            //
            //     [WorkerBootstrap] RB_WORKER build — launching the worker host
            //     [WorkerHost] no BuilderManager in the scene; ...
            //     Container called exit(3).
            //
            // Measured 2026-08-19 on a fresh rebuild, which is how it surfaced:
            // the image deployed since 08-12 works, so the worker in production
            // was an artefact THIS REPOSITORY COULD NO LONGER REPRODUCE. That is
            // the real defect; the boot failure was only its symptom.
            //
            // ⚠ NOT StartCareer(). That also runs Career.Load() and
            // EconomySync.Kick() — owner state and a network call the worker has
            // no business touching (hard rule 5). The loops only ever use
            // bm.SnapshotString() and bm.LoadSnapshot(), so a bare component is
            // exactly the dependency, and nothing more.
            //
            // Autosave is pinned off BEFORE the component exists rather than
            // after, because "off for the whole lifetime" has to include the
            // constructor.
            Career.autosave = false;
            if (Object.FindFirstObjectByType<BuilderManager>() == null)
            {
                new GameObject("BuilderManager").AddComponent<BuilderManager>();
                Debug.Log("[WorkerBootstrap] created a headless BuilderManager");
            }

            WorkerHost.Launch();
        }
#endif
    }
}
