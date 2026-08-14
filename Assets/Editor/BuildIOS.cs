// ===========================================================================
// BuildIOS.cs — produce the Xcode project for the TestFlight soft launch.
// 2026-08-10.
//
//   from a live editor (this is how it was first run):
//     RobotBrawl.Editor.BuildIOS.Build();
//
//   from a terminal, against a CLONE:
//     Unity -quit -batchmode -nographics -projectPath <clone> \
//           -executeMethod RobotBrawl.Editor.BuildIOS.Build -logFile -
//
// ⚠ IT DOES NOT CALL EditorApplication.Exit WHEN A HUMAN IS DRIVING, and that
// is the one way this file differs from BuildWorker.cs. BuildWorker exits
// non-zero on failure because a batchmode build that logs an error and exits 0
// gets packaged and deployed. The SAME line run from a live editor CLOSES
// owen's Unity mid-session. So the exit is gated on Application.isBatchMode:
// a script that is correct in one context and destructive in the other has to
// know which one it is in.
//
// ⚠ UNLIKE THE WORKER, THIS DOES NOT NEED A CLONE. BuildWorker's warning is
// about switching the ACTIVE BUILD TARGET — this project's target IS iOS, so
// building it in place switches nothing and reimports nothing. Copying that
// warning here would be cargo cult.
//
// WHAT THIS PRODUCES: an Xcode project, not an .ipa. Signing, archiving and
// the TestFlight upload are Xcode's and need owen's certificates; nothing in
// this repo can do them and nothing in this repo should try.
// ===========================================================================
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RobotBrawl.Editor
{
    public static class BuildIOS
    {
        const string OUT_DIR = "build/ios";

        /// <summary>DEVICE build pointed at the CLOUD DEV ladder (owen,
        /// 2026-08-14: end-to-end testing from real devices). Same define
        /// discipline as BuildIOSSim.BuildDevPointed — set before the try,
        /// restored in the finally, own output dir so it can never be
        /// mistaken for the TestFlight artifact, and the gate prints the dev
        /// URL on screen. Sign and install with the development team; never
        /// upload this one.</summary>
        public static void BuildDevPointed()
        {
            string prior = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.iOS);
            if (!prior.Contains("RB_DEV_SERVER"))
                PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.iOS,
                    string.IsNullOrEmpty(prior) ? "RB_DEV_SERVER" : prior + ";RB_DEV_SERVER");
            try { BuildTo("build/ios-devptd"); }
            finally { PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.iOS, prior); }
        }

        public static void Build() { BuildTo(null); }

        static void BuildTo(string forcedOutDir)
        {
            string outDir = forcedOutDir ?? Arg("-rbOutDir") ?? OUT_DIR;

            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.iOS, BuildTarget.iOS))
            { Fail("the iOS module is not installed in this Unity"); return; }

            var scenes = new System.Collections.Generic.List<string>();
            foreach (var s in EditorBuildSettings.scenes)
                if (s.enabled && File.Exists(s.path)) scenes.Add(s.path);
            if (scenes.Count == 0) { Fail("no enabled scenes in Build Settings"); return; }

            Directory.CreateDirectory(outDir);

            // ⚠ ATS. iOS blocks cleartext HTTP by default, and the ladder client
            // shipped pointing at http://localhost:5000 until 2026-08-10 — so
            // this is worth SAYING OUT LOUD at every build rather than
            // discovering it on a device. PRODUCTION is https, so the correct
            // configuration is arbitrary loads OFF.
            Debug.Log("[BuildIOS] allowsArbitraryLoads=" + PlayerSettings.iOS.allowHTTPDownload
                      + "  (false is correct: production is https)");
            Debug.Log("[BuildIOS] bundle=" + PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.iOS)
                      + "  version=" + PlayerSettings.bundleVersion
                      + "  minOS=" + PlayerSettings.iOS.targetOSVersionString);

            var opts = new BuildPlayerOptions
            {
                scenes           = scenes.ToArray(),
                locationPathName = outDir,
                target           = BuildTarget.iOS,
                targetGroup      = BuildTargetGroup.iOS,
                options          = BuildOptions.None,
            };

            Debug.Log("[BuildIOS] building " + outDir + " from " + scenes.Count + " scene(s)");
            BuildReport report = BuildPipeline.BuildPlayer(opts);
            var sum = report.summary;
            Debug.Log(string.Format(
                "[BuildIOS] result={0} size={1} errors={2} warnings={3} time={4}",
                sum.result, sum.totalSize, sum.totalErrors, sum.totalWarnings, sum.totalTime));

            if (sum.result != BuildResult.Succeeded)
            { Fail("build did not succeed: " + sum.result + " (" + sum.totalErrors + " errors)"); return; }

            // An Xcode project, not a binary — so the artefact to check for is
            // the pbxproj, not an executable.
            string pbx = Path.Combine(outDir, "Unity-iPhone.xcodeproj/project.pbxproj");
            if (!File.Exists(pbx))
            { Fail("build reported success but produced no Xcode project at " + pbx); return; }

            Debug.Log("[BuildIOS] OK — Xcode project at " + Path.GetFullPath(outDir)
                      + "\n[BuildIOS] NEXT: open Unity-iPhone.xcodeproj, set the team, archive, upload.");
            Done(0);
        }

        static void Fail(string why)
        {
            Debug.LogError("[BuildIOS] " + why);
            Done(1);
        }

        /// <summary>Exit ONLY in batchmode. See the header: the same call that
        /// makes a CI build honest closes a live editor.</summary>
        static void Done(int code)
        {
            if (Application.isBatchMode) EditorApplication.Exit(code);
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
