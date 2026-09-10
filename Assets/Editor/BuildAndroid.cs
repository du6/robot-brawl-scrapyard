// ===========================================================================
// BuildAndroid.cs — produce the Play Store .aab (and a sideloadable .apk)
// for the Android launch. 2026-08-16.
//
//   from a terminal, against a CLONE (the only supported way):
//     Unity -quit -batchmode -nographics -projectPath <clone> \
//           -buildTarget Android \
//           -executeMethod RobotBrawl.Editor.BuildAndroid.Build -logFile -
//
//   scripts/build_android.zsh does the clone + invocation.
//
// ⚠ UNLIKE BuildIOS, THIS NEEDS A CLONE — BuildWorker's warning applies in
// full. This project's active target is iOS (that is where the App Store
// submission lives); building Android in owen's open project would switch
// the target, reimport everything twice, and leave the editor on the wrong
// platform if anything failed in between. BuildIOS.cs:20 documents why iOS
// is exempt; Android is exactly the case the warning exists for.
//
// SIGNING: with no keystore configured Unity signs with its DEBUG keystore.
// A debug-signed .aab is fine for local install and CI verification and is
// REJECTED by Play Console — creating the upload keystore is owen's (it is
// a credential). When it exists, set the four Android keystore fields in
// Player Settings (or via -rbKeystore* args here) and rebuild.
// ===========================================================================
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RobotBrawl.Editor
{
    public static class BuildAndroid
    {
        const string OUT_DIR = "build/android";

        /// <summary>Play Store artifact: release .aab, IL2CPP, ARM64.</summary>
        public static void Build() { BuildTo(null, aab: true); }

        /// <summary>Sideloadable .apk for real-device testing — same code,
        /// same defines, just a package a phone will take over adb.</summary>
        public static void BuildApk() { BuildTo(null, aab: false); }

        static void BuildTo(string forcedOutDir, bool aab)
        {
            string outDir = forcedOutDir ?? Arg("-rbOutDir") ?? OUT_DIR;

            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
            { Fail("the Android module is not installed in this Unity"); return; }

            // ⚠ The dev-server define leaking into a store build is the exact
            // incident BuildIOS.BuildDevPointed documents (2026-08-14). Android
            // has no dev-pointed variant yet, so ANY occurrence here is a leak.
            string defines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Android);
            if (defines.Contains("RB_DEV_SERVER"))
            { Fail("RB_DEV_SERVER is set on the Android defines — this would ship a dev-pointed build"); return; }

            var scenes = new System.Collections.Generic.List<string>();
            foreach (var s in EditorBuildSettings.scenes)
                if (s.enabled && File.Exists(s.path)) scenes.Add(s.path);
            if (scenes.Count == 0) { Fail("no enabled scenes in Build Settings"); return; }

            // Set rather than assert: the clone's settings ARE the settings,
            // and Google requires 64-bit; Mono cannot produce ARM64.
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            EditorUserBuildSettings.buildAppBundle = aab;

            bool debugSigned = string.IsNullOrEmpty(PlayerSettings.Android.keystoreName);
            Debug.Log("[BuildAndroid] package=" + PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android)
                      + "  version=" + PlayerSettings.bundleVersion
                      + "  versionCode=" + PlayerSettings.Android.bundleVersionCode
                      + "  minSdk=" + (int)PlayerSettings.Android.minSdkVersion
                      + "  targetSdk=" + (int)PlayerSettings.Android.targetSdkVersion + " (0 = highest installed)");
            Debug.Log("[BuildAndroid] defines='" + defines + "'  (RB_DEV_SERVER absent — production URL)");
            if (debugSigned)
                Debug.Log("[BuildAndroid] ⚠ DEBUG KEYSTORE — installable, benchable, NOT uploadable to Play.");

            Directory.CreateDirectory(outDir);
            string artifact = Path.Combine(outDir, aab ? "scrapyard.aab" : "scrapyard.apk");

            var opts = new BuildPlayerOptions
            {
                scenes           = scenes.ToArray(),
                locationPathName = artifact,
                target           = BuildTarget.Android,
                targetGroup      = BuildTargetGroup.Android,
                options          = BuildOptions.None,
            };

            Debug.Log("[BuildAndroid] building " + artifact + " from " + scenes.Count + " scene(s)");
            BuildReport report = BuildPipeline.BuildPlayer(opts);
            var sum = report.summary;
            Debug.Log(string.Format(
                "[BuildAndroid] result={0} size={1} errors={2} warnings={3} time={4}",
                sum.result, sum.totalSize, sum.totalErrors, sum.totalWarnings, sum.totalTime));

            if (sum.result != BuildResult.Succeeded)
            { Fail("build did not succeed: " + sum.result + " (" + sum.totalErrors + " errors)"); return; }

            if (!File.Exists(artifact))
            { Fail("build reported success but produced no artifact at " + artifact); return; }

            Debug.Log("[BuildAndroid] OK — " + Path.GetFullPath(artifact)
                      + (debugSigned ? "\n[BuildAndroid] NEXT: create the upload keystore before any Play upload." : ""));
            Done(0);
        }

        static void Fail(string why)
        {
            Debug.LogError("[BuildAndroid] " + why);
            Done(1);
        }

        /// <summary>Exit ONLY in batchmode — BuildIOS.cs:12's lesson: the same
        /// call that makes a CI build honest closes a live editor.</summary>
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
