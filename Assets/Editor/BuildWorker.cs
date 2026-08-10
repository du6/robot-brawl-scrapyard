// ===========================================================================
// BuildWorker.cs — build the headless Linux worker. 2026-08-09.
//
//   Unity -quit -batchmode -nographics \
//         -projectPath <clone> \
//         -executeMethod RobotBrawl.Editor.BuildWorker.Build \
//         -logFile -
//
// ⚠ RUN THIS FROM A CLONE OF THE PROJECT, NOT FROM owen's WORKING COPY.
// Two reasons, and the second is the expensive one:
//
//   * Unity holds a per-project lock. A batchmode build against a project the
//     editor already has open either fails or fights it.
//   * Building switches the ACTIVE BUILD TARGET, and this project's target is
//     iOS — that is where the TestFlight soft launch lives (§M4). Switching to
//     Linux and back reimports every asset TWICE, and leaves owen's editor on
//     the wrong platform if anything goes wrong in between.
//
// scripts/build_worker.zsh makes the clone and invokes this. It exists so the
// answer to "how do I build the worker" is a command, not a paragraph.
//
// The build is a DEDICATED SERVER subtarget: no graphics, no audio, no input.
// That is not an optimisation — a Cloud Run container has no display, and a
// normal player build would try to open one.
// ===========================================================================
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RobotBrawl.Editor
{
    public static class BuildWorker
    {
        const string OUT_DIR  = "build/worker";
        const string EXE_NAME = "RobotBrawlWorker";

        public static void Build()
        {
            string outDir = Arg("-rbOutDir") ?? OUT_DIR;
            string exe    = Path.Combine(outDir, EXE_NAME);

            // Main is the only scene the worker needs: it carries the
            // BuilderManager the worker loads payloads into. GetStarted_Scene
            // is the tutorial and would just be dead weight in the image.
            string[] scenes = { "Assets/Scenes/Main.unity" };
            foreach (var s in scenes)
            {
                if (!File.Exists(s))
                {
                    Fail("scene not found: " + s);
                    return;
                }
            }

            Directory.CreateDirectory(outDir);

            // Dedicated Server subtarget. StandaloneLinux64 alone would be a
            // PLAYER build, which expects a display.
            var opts = new BuildPlayerOptions
            {
                scenes           = scenes,
                locationPathName = exe,
                target           = BuildTarget.StandaloneLinux64,
                subtarget        = (int)StandaloneBuildSubtarget.Server,
                options          = BuildOptions.None,
            };

            // The worker must come up running, with nobody to press anything.
            // A bootstrap component in the scene would drag the worker into
            // every editor play session too, so it is injected here instead —
            // the build defines RB_WORKER, and WorkerBootstrap only arms itself
            // under that define.
            var group = BuildTargetGroup.Standalone;
            var named = UnityEditor.Build.NamedBuildTarget.Server;
            string defines = PlayerSettings.GetScriptingDefineSymbols(named);
            if (!defines.Split(';').Contains("RB_WORKER"))
                PlayerSettings.SetScriptingDefineSymbols(
                    named, string.IsNullOrEmpty(defines) ? "RB_WORKER" : defines + ";RB_WORKER");

            Debug.Log("[BuildWorker] building " + exe + " (Linux64 dedicated server)");
            BuildReport report = BuildPipeline.BuildPlayer(opts);
            var sum = report.summary;

            Debug.Log(string.Format("[BuildWorker] result={0} size={1} bytes errors={2} warnings={3} time={4}",
                sum.result, sum.totalSize, sum.totalErrors, sum.totalWarnings, sum.totalTime));

            if (sum.result != BuildResult.Succeeded)
            {
                Fail("build did not succeed: " + sum.result + " (" + sum.totalErrors + " errors)");
                return;
            }
            if (!File.Exists(exe))
            {
                Fail("build reported success but produced no executable at " + exe);
                return;
            }
            Debug.Log("[BuildWorker] OK " + exe);
            EditorApplication.Exit(0);
        }

        /// <summary>Exit NON-ZERO. A batchmode build that logs an error and
        /// exits 0 gets packaged into a container and deployed, and the first
        /// sign of trouble is a crash loop in the cloud.</summary>
        static void Fail(string why)
        {
            Debug.LogError("[BuildWorker] " + why);
            EditorApplication.Exit(1);
        }

        static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++)
                if (a[i] == name) return a[i + 1];
            return null;
        }
    }
}
