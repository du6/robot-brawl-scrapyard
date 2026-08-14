// ===========================================================================
// BuildIOSSim.cs — build the game for APPLE'S iOS SIMULATOR, 2026-08-10.
//
//   RobotBrawl.Editor.BuildIOSSim.Build();
//
// WHY THIS EXISTS, AND WHY IT IS NOT THE DEVICE SIMULATOR.
//
// Unity's Device Simulator (which is installed, and which was active while the
// ARENA was being judged: Screen.dpi 460, safeArea inset 141/63) simulates the
// SCREEN — resolution, dpi, safe area, orientation. It does not simulate the
// PLATFORM. Measured in this editor on 2026-08-10:
//
//     TouchScreenKeyboard.isSupported = False
//
// so "does the on-screen keyboard cover the sign-in field" cannot be asked
// there at all. Worse, and more quietly: the Device Simulator runs EDITOR code,
// so `#if UNITY_EDITOR` is defined and LadderClient.DefaultBaseUrl resolves to
// LOCAL_DEV. The Device Simulator can never answer which server a BUILD talks
// to, because it is not a build. That is item 2 on the launch checklist and it
// is structurally out of reach from the editor — not merely untested.
//
// A Simulator-SDK player is a real iOS player: il2cpp, UIKit, the real on-screen
// keyboard, real App Transport Security, real app lifecycle. UNITY_EDITOR is
// NOT defined, so it takes the same branch the TestFlight build takes.
//
// ⚠ WHAT IT STILL IS NOT. It runs on the Mac's CPU with no thermal envelope and
// no phone GPU, so it says nothing about frame rate, battery or heat. And no
// finger has touched it: occlusion (your thumb covers the control you tap),
// one-handed reach and gesture conflicts with iOS's own edge swipes are
// properties of a hand holding a slab of glass. Those still need the phone.
//
// ⚠ IT MUTATES A PROJECT SETTING AND MUST PUT IT BACK. Building for the
// simulator flips PlayerSettings.iOS.sdkVersion, which lives in the tracked
// file ProjectSettings/ProjectSettings.asset. The restore is in a `finally`, on
// purpose: if it were after the build, a failed build would leave owen's
// project configured for a simulator it cannot ship to — the same class of
// mistake as a bench that leaves the career save rewritten. Baseline md5 before
// this ran: db18d7c6e050d8b3653ca8f031642966.
// ===========================================================================
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RobotBrawl.Editor
{
    public static class BuildIOSSim
    {
        const string OUT_DIR = "build/ios-sim";

        /// <summary>Set true (in the SAME synchronous bridge command that then
        /// calls Build() — a domain reload would reset it) to produce a
        /// RELEASE-configuration player in build/ios-sim-rel: no
        /// BuildOptions.Development, so no on-screen dev console. Needed
        /// 2026-08-10: the dev console occludes product UI in four places
        /// (BUILD tab button, LEAGUE locked rows, a sensor label, the ARENA
        /// sign-in caption + button), which both blocked a store capture and
        /// made "the sign-in fields don't focus" ambiguous between a broken
        /// InputField and an overlay eating touches.</summary>
        public static bool ReleaseMode = false;

        /// <summary>Batchmode entry point: `-executeMethod ...BuildRelease`.
        /// -executeMethod cannot set a static field first, so the flag is set
        /// here, in the same invocation, which is the same discipline the
        /// bridge path uses (same synchronous call, no domain reload between).
        /// </summary>
        public static void BuildRelease() { ReleaseMode = true; Build(); }

        /// <summary>DEV-POINTED release player (2026-08-13, UX validation
        /// phase two): identical to BuildRelease except the RB_DEV_SERVER
        /// define makes LadderClient target LOCAL_DEV — the Mac's own API,
        /// which the simulator reaches as localhost. This is the ONLY way to
        /// validate past the login gate on a simulator without registering
        /// production accounts. The define is set for this build alone and
        /// restored in the finally below; output goes to its own directory so
        /// it can never be mistaken for the TestFlight-shaped artifact.</summary>
        public static bool DevServer = false;
        public static void BuildDevPointed() { ReleaseMode = true; DevServer = true; Build(); }

        public static void Build()
        {
            string outDir = DevServer ? "build/ios-sim-devptd"
                          : ReleaseMode ? "build/ios-sim-rel" : OUT_DIR;
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.iOS, BuildTarget.iOS))
            { Debug.LogError("[BuildIOSSim] the iOS module is not installed"); return; }

            // ⚠ RELEASE BUILDS REFUSE A NON-EMPTY OUTPUT DIR — 2026-08-10. The
            // release path exists to produce MATCHED PAIRS (a BEFORE and an
            // AFTER differing by one change), and the second build of a pair
            // overwrote the first IN PLACE: build/ios-sim-rel silently became
            // the BEFORE build, so anyone checking its UnityClassRegistration
            // for the link.xml fix found it absent and would conclude the fix
            // failed — the right file for the wrong build. Caught only because
            // one agent had read the file before the overwrite (17025 bytes ->
            // 16863, the registration line gone). An experiment's control must
            // not be deletable by running the experiment again.
            // The DEV path stays overwritable on purpose: incremental append
            // is how Unity iOS builds work, and that path carries no pairs.
            if (ReleaseMode && System.IO.Directory.Exists(outDir)
                && System.IO.Directory.GetFileSystemEntries(outDir).Length > 0)
            {
                Debug.LogError("[BuildIOSSim] " + outDir + " is non-empty — it may hold a matched pair's other half. Move it aside first; refusing to overwrite a control.");
                return;
            }

            var scenes = new System.Collections.Generic.List<string>();
            foreach (var s in EditorBuildSettings.scenes)
                if (s.enabled && File.Exists(s.path)) scenes.Add(s.path);
            if (scenes.Count == 0) { Debug.LogError("[BuildIOSSim] no enabled scenes"); return; }

            var priorSdk = PlayerSettings.iOS.sdkVersion;
            // The dev-server define follows the sdkVersion discipline exactly:
            // set before the try, restored in the finally, because a failed
            // build must never leave the project silently dev-pointed.
            string priorDefines = PlayerSettings.GetScriptingDefineSymbols(
                UnityEditor.Build.NamedBuildTarget.iOS);
            if (DevServer && !priorDefines.Contains("RB_DEV_SERVER"))
                PlayerSettings.SetScriptingDefineSymbols(
                    UnityEditor.Build.NamedBuildTarget.iOS,
                    string.IsNullOrEmpty(priorDefines) ? "RB_DEV_SERVER"
                                                       : priorDefines + ";RB_DEV_SERVER");
            int priorSimArch = -1;
            Debug.Log("[BuildIOSSim] sdkVersion was " + priorSdk);

            try
            {
                PlayerSettings.iOS.sdkVersion = iOSSdkVersion.SimulatorSDK;

                // ⚠ THE ARCHITECTURE SETTING IS READ DIFFERENTLY UNDER THE
                // SIMULATOR SDK, AND THE DEFAULT IS WRONG ON APPLE SILICON.
                //
                // First run of this script produced an x86_64 player on an
                // arm64 Mac. Both the build AND `xcodebuild` reported SUCCESS;
                // it failed at `simctl install`, with "Needs to Be Updated /
                // This app needs to be updated by the developer" — a message
                // about the DEVELOPER, for what is a host-architecture
                // mismatch. Forcing ARCHS=arm64 in xcodebuild alone cannot fix
                // it: Unity ships PREBUILT binaries (Frameworks/UnityRuntime
                // and Libraries/baselib.a) and had already copied the x86_64
                // ones, so the link failed on thousands of "found architecture
                // x86_64, required architecture arm64".
                //
                // Unity has the right ones on disk — Trampoline/ holds
                // UnityRuntime-sim-arm64 and baselib-sim-arm64.a beside the
                // -sim-x64 pair. Picking between them is a SETTING, so it
                // belongs here rather than in a flag passed to Xcode after the
                // fact, which cannot relink a prebuilt binary.
                //
                // ⚠ IT IS NOT `SetArchitecture`. That one already read ARM64
                // (it governs DEVICE builds) and changing it did nothing —
                // measured, twice. The simulator has its OWN field, found by
                // reading ProjectSettings.asset rather than guessing at the
                // API: `iOSSimulatorArchitecture`, 0=x86_64 1=ARM64
                // 2=Universal, matching the three baselib variants on disk.
                // `PlayerSettings.iOS.simulatorArchitecture` does not exist;
                // the compiler was asked and said so. Hence SerializedObject
                // against the name the asset actually uses.
                var so = new SerializedObject(
                    AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
                var simArch = so.FindProperty("iOSSimulatorArchitecture");
                if (simArch == null)
                { Debug.LogError("[BuildIOSSim] iOSSimulatorArchitecture not found — Unity renamed it"); return; }
                priorSimArch = simArch.intValue;
                simArch.intValue = 1;                       // ARM64
                so.ApplyModifiedProperties();
                Debug.Log("[BuildIOSSim] iOSSimulatorArchitecture " + priorSimArch + " -> 1 (ARM64)");
                Directory.CreateDirectory(outDir);

                var opts = new BuildPlayerOptions
                {
                    scenes           = scenes.ToArray(),
                    locationPathName = outDir,
                    target           = BuildTarget.iOS,
                    targetGroup      = BuildTargetGroup.iOS,
                    // Development by default so the ARENA status line prints its
                    // base URL. ReleaseMode drops it — no console overlay, no
                    // player-connection: what a tester would actually see.
                    options          = ReleaseMode ? BuildOptions.None
                                                   : BuildOptions.Development,
                };

                Debug.Log("[BuildIOSSim] building " + outDir + " (release=" + ReleaseMode + ") from " + scenes.Count + " scene(s)");
                BuildReport report = BuildPipeline.BuildPlayer(opts);
                var sum = report.summary;
                Debug.Log(string.Format("[BuildIOSSim] result={0} size={1} errors={2} time={3}",
                                        sum.result, sum.totalSize, sum.totalErrors, sum.totalTime));
            }
            finally
            {
                PlayerSettings.iOS.sdkVersion = priorSdk;
                // -1 means we never got as far as changing it; writing that
                // back would invent a value the project never held.
                if (priorSimArch >= 0)
                {
                    var so = new SerializedObject(
                        AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
                    var p = so.FindProperty("iOSSimulatorArchitecture");
                    if (p != null) { p.intValue = priorSimArch; so.ApplyModifiedProperties(); }
                }
                PlayerSettings.SetScriptingDefineSymbols(
                    UnityEditor.Build.NamedBuildTarget.iOS, priorDefines);
                AssetDatabase.SaveAssets();
                Debug.Log("[BuildIOSSim] restored sdkVersion=" + priorSdk + " simArch=" + priorSimArch
                          + " defines='" + priorDefines + "' — verify ProjectSettings.asset with git diff");
            }
        }
    }
}
