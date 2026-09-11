// ===========================================================================
// BuildWebGL.cs - Stage 0 of docs/WebGL_Design_2026-08-31.md.
//
// Modelled on BuildIOS.cs: same scene list, same BuildPlayerOptions shape,
// same "fail loudly and exit non-zero" contract so batch mode is honest.
//
// ! THIS LIVES IN A CLONE ON PURPOSE. Building switches the project's ACTIVE
// build target, and this project's is iOS. build_worker.zsh set the
// precedent (it builds the Linux player from a clone for the same reason);
// the parity decision in the design doc's §6 makes it a hard rule here,
// because "don't touch the mobile app" has to be literally true.
//
// Settings chosen for the SPIKE, and each is a measurement decision:
//   Brotli               - the format the real host (GCS) will serve
//   decompressionFallback- ON, so a dumb static server (python -m http.server,
//                          which is how the phone test is served) can host it
//                          without Content-Encoding headers. Production
//                          should turn this OFF and set the headers instead;
//                          it costs payload.
//   dataCaching          - ON: repeat visits pull from IndexedDB, which is
//                          the difference between a 40 MB visit and a 40 MB
//                          FIRST visit.
//   exceptionSupport None- smallest/fastest. If the build runs but misbehaves,
//                          raise this before blaming game code.
//   stripping High       - IL2CPP managed stripping. If something reflective
//                          breaks at runtime, this is the first suspect and
//                          link.xml is the fix.
//
//   usage: Unity -quit -batchmode -nographics -projectPath <clone> \
//          -buildTarget WebGL -executeMethod RobotBrawl.Editor.BuildWebGL.Build \
//          -logFile -
// ===========================================================================
using System.IO;
using UnityEditor;
using UnityEditor.Build;              // NamedBuildTarget - BuildIOS.cs gets it
                                       // from here too; omitting it fails with
                                       // CS0103 at the stripping-level call.
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RobotBrawl.Editor
{
    public static class BuildWebGL
    {
        const string OUT_DIR = "build/webgl";

        /// <summary>PORTAL BUILD (2026-09-08): adds RB_PORTAL, which drops the
        /// ARENA tab. CrazyGames' account rules forbid an in-game email login
        /// and their Basic Launch table says "no external login options"; our
        /// ladder sign-in is exactly that. The define is restored afterwards
        /// so it can never leak into the next build (the RB_DEV_SERVER lesson,
        /// BuildIOSSim.cs).</summary>
        public static bool Portal;
        /// <summary>The WebGL linker's code optimisation (Build Times / Runtime
        /// Speed / Disk Size, with or without LTO), by reflection: the property is
        /// `codeOptimization` on a static settings class in the WebGL editor
        /// extension, and its enum's type name is not stable across versions.</summary>
        static void SetWasmCodeOptimization(string valueName)
        {
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!asm.GetName().Name.StartsWith("UnityEditor.WebGL")) continue;
                System.Type[] types;
                try { types = asm.GetTypes(); } catch { continue; }
                foreach (var t in types)
                {
                    var p = t.GetProperty("codeOptimization", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    if (p == null || !p.PropertyType.IsEnum) continue;
                    try
                    {
                        var v = System.Enum.Parse(p.PropertyType, valueName);
                        p.SetValue(null, v);
                        Debug.Log("[BuildWebGL] codeOptimization=" + valueName + " via " + t.FullName);
                        return;
                    }
                    catch (System.Exception e) { Debug.LogWarning("[BuildWebGL] codeOptimization " + valueName + " refused on " + t.FullName + ": " + e.Message); }
                }
            }
            Debug.LogWarning("[BuildWebGL] no codeOptimization property found - linker left at its default");
        }
        /// <summary>`-rbClean` on the command line: a clean build, whose log lists every asset by size.</summary>
        static bool Clean { get { return System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-rbClean") >= 0; } }
        public static void BuildPortal() { Portal = true; Build(); }

        public static void Build()
        {
            string outDir = Arg("-rbOutDir") ?? OUT_DIR;
            string priorDefines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.WebGL);
            if (Portal && !priorDefines.Contains("RB_PORTAL"))
                PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.WebGL,
                    string.IsNullOrEmpty(priorDefines) ? "RB_PORTAL" : priorDefines + ";RB_PORTAL");
            try
            {

            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
            { Fail("the WebGL module is not installed in this Unity"); return; }

            var scenes = new System.Collections.Generic.List<string>();
            foreach (var s in EditorBuildSettings.scenes)
                if (s.enabled && File.Exists(s.path)) scenes.Add(s.path);
            if (scenes.Count == 0) { Fail("no enabled scenes in Build Settings"); return; }

            // --- the settings above, applied and then STATED, so the log is
            // --- the record of what produced the number we are about to quote.
            // 2026-09-04 phone-load cut: the Unity splash logo was a 2.7 MB texture
            // in the payload and a beat of dead time before the scene. Unity 6 no
            // longer requires it on any plan.
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            PlayerSettings.WebGL.compressionFormat     = WebGLCompressionFormat.Brotli;
            // ON for our own site, because GitHub Pages cannot send the
            // Content-Encoding header Brotli needs. OFF for a portal build:
            // CrazyGames serves the headers itself, and their validator
            // REJECTS the .unityweb files the fallback produces ("you have the
            // Decompression fallback option activated", measured 2026-09-08).
            // Line runs on every build, so a portal build cannot leave it off.
            PlayerSettings.WebGL.decompressionFallback = !Portal;
            PlayerSettings.WebGL.dataCaching           = true;
            PlayerSettings.WebGL.exceptionSupport      = WebGLExceptionSupport.None;
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL,
                                                    ManagedStrippingLevel.High);
            // SIZE (CrazyGames plan step 5, 2026-09-10): the wasm is 6.6 of the
            // 10.4 MB. IL2CPP for size, and the linker for disk size with LTO.
            PlayerSettings.SetIl2CppCodeGeneration(NamedBuildTarget.WebGL, Il2CppCodeGeneration.OptimizeSize);
            SetWasmCodeOptimization("DiskSizeLTO");
            // ! The stock template hard-codes a 960x600 LANDSCAPE canvas and
            // only goes full-viewport for a phone user agent - so a
            // portrait-only mobile UI rendered as a squeezed box on a white
            // page under a Unity logo footer, titled "Unity Web Player | ...".
            // That was most of what "the UI looks ugly" meant.
            PlayerSettings.WebGL.template = "PROJECT:RobotBrawl";

            Debug.Log("[BuildWebGL] compression=Brotli fallback=" + (Portal ? "off (portal)" : "on") + " dataCaching=on"
                      + " exceptions=None stripping=High");
            Debug.Log("[BuildWebGL] product=" + PlayerSettings.productName
                      + " version=" + PlayerSettings.bundleVersion
                      + " scenes=" + scenes.Count);

            Directory.CreateDirectory(outDir);

            var opts = new BuildPlayerOptions
            {
                scenes           = scenes.ToArray(),
                locationPathName = outDir,
                target           = BuildTarget.WebGL,
                targetGroup      = BuildTargetGroup.WebGL,
                options          = Clean ? BuildOptions.CleanBuildCache : BuildOptions.None,   // release, so the 44 harness
                                                        // files stay compiled OUT; -rbClean rebuilds player data
                                                        // so the log carries the asset size report
            };

            Debug.Log("[BuildWebGL] building " + outDir + " from " + scenes.Count + " scene(s)");
            // SCRAPYARD (2026-09-10): THE MUSIC IS NOT IN THE DATA FILE. This repo
            // serves iOS too, so the six themes live in Assets/Resources for
            // Resources.Load - and the first web build of the fork carried all
            // 21 MB of them into webgl.data (26 MB against a 9 MB budget; the
            // spike simply had no Resources folder). On the web MusicLoader
            // streams music/<name>.mp3 from beside the page AFTER boot, so for
            // this build the folder is hidden behind a `~` (Unity ignores it),
            // restored in `finally`, and the small re-encoded tracks are copied
            // next to index.html.
            string resDir = Path.Combine(Application.dataPath, "Resources");
            string hidDir = Path.Combine(Application.dataPath, "Resources~");
            bool hid = false;
            // A KILLED BUILD LEAVES THE FOLDER HIDDEN (2026-09-10: the system
            // killed a batchmode build for memory and `finally` never ran, so
            // Assets/Resources~ sat there and the next build would have shipped
            // iOS without its music). Recover first, then hide again.
            // ...and Unity recreates an EMPTY Assets/Resources from the meta at
            // the next start (seen 2026-09-10, twice), which hid the leftover
            // from the check below. An empty folder in the way is removed first.
            // (And "empty" is not a safe test either - macOS drops a .DS_Store in
            // it. Seen 2026-09-10, a third time.) So: MERGE. Whatever is in the
            // leftover goes back into Assets/Resources, file by file, and the
            // leftover is removed.
            if (Directory.Exists(hidDir))
            {
                Directory.CreateDirectory(resDir);
                foreach (var f in Directory.GetFiles(hidDir))
                {
                    string dst = Path.Combine(resDir, Path.GetFileName(f));
                    if (File.Exists(dst)) File.Delete(dst);
                    File.Move(f, dst);
                }
                foreach (var d in Directory.GetDirectories(hidDir))
                {
                    string dst = Path.Combine(resDir, Path.GetFileName(d));
                    if (Directory.Exists(dst)) Directory.Delete(dst, true);
                    Directory.Move(d, dst);
                }
                Directory.Delete(hidDir, true);
                if (File.Exists(hidDir + ".meta")) File.Delete(hidDir + ".meta");
                Debug.LogWarning("[BuildWebGL] Assets/Resources~ was left behind by an interrupted build - merged back");
                AssetDatabase.Refresh();
            }
            if (Directory.Exists(resDir) && !Directory.Exists(hidDir))
            {
                Directory.Move(resDir, hidDir);
                if (File.Exists(resDir + ".meta")) File.Move(resDir + ".meta", hidDir + ".meta");
                hid = true;
                AssetDatabase.Refresh();
            }
            BuildReport report;
            try { report = BuildPipeline.BuildPlayer(opts); }
            finally
            {
                if (hid)
                {
                    Directory.Move(hidDir, resDir);
                    if (File.Exists(hidDir + ".meta")) File.Move(hidDir + ".meta", resDir + ".meta");
                    AssetDatabase.Refresh();
                }
            }
            string musicSrc = Arg("-rbMusicDir") ?? Path.Combine(Path.GetDirectoryName(Application.dataPath), "music_web");
            if (Directory.Exists(musicSrc))
            {
                string musicDst = Path.Combine(outDir, "music");
                Directory.CreateDirectory(musicDst);
                int nm = 0;
                foreach (var f in Directory.GetFiles(musicSrc, "*.mp3")) { File.Copy(f, Path.Combine(musicDst, Path.GetFileName(f)), true); nm++; }
                Debug.Log("[BuildWebGL] music/ beside the page: " + nm + " track(s) from " + musicSrc);
            }
            else Debug.LogWarning("[BuildWebGL] no music_web/ (or -rbMusicDir) - the page will have no music");
            var sum = report.summary;
            Debug.Log(string.Format(
                "[BuildWebGL] result={0} size={1} errors={2} warnings={3} time={4}",
                sum.result, sum.totalSize, sum.totalErrors, sum.totalWarnings, sum.totalTime));

            if (sum.result != BuildResult.Succeeded)
            { Fail("build did not succeed: " + sum.result + " (" + sum.totalErrors + " errors)"); return; }

            // The artefact to check for is the loader - a WebGL build that
            // "succeeded" with no index.html is not a thing anyone can open.
            string index = Path.Combine(outDir, "index.html");
            if (!File.Exists(index))
            { Fail("build reported success but produced no index.html at " + index); return; }

            // ! CACHE-BUST THE PAYLOAD URLS, OR EVERY RETURNING VISITOR GETS A
            // CRASH WALL. dataCaching stores the payload in IndexedDB keyed by
            // URL, and our filenames never change between deploys - so after a
            // redeploy the loader pairs a STALE cached .data with FRESH wasm and
            // aborts at ~90% with abort("") / __cxa_begin_catch. Measured on the
            // live site 2026-09-01: build 6 was a hard wall for anyone who had
            // loaded build 5, and deleting UnityCache by hand fixed it instantly.
            //
            // Appending a content hash makes the URL change exactly when the
            // bytes change: new build = new cache entry, identical rebuild =
            // same entry, so caching keeps working and staleness cannot happen.
            // ⚠ THE EXTENSION IS NOT FIXED. With decompressionFallback ON Unity
            // emits .unityweb; with it OFF (the portal build) it emits .br. This
            // was hardcoded to .unityweb and threw FileNotFoundException on the
            // first portal build, AFTER a green build - measured 2026-09-08.
            var dataFiles = Directory.GetFiles(Path.Combine(outDir, "Build"), "webgl.data.*");
            if (dataFiles.Length == 0) { Fail("no webgl.data.* in " + outDir + "/Build"); return; }
            string stamp;
            using (var md5 = System.Security.Cryptography.MD5.Create())
            using (var fs = File.OpenRead(dataFiles[0]))
                stamp = System.BitConverter.ToString(md5.ComputeHash(fs))
                              .Replace("-", "").Substring(0, 10).ToLowerInvariant();

            // One rule, applied once: every payload reference in the emitted
            // HTML ends in a quote, so stamp exactly those. Deliberately not
            // clever - a double-applied replace here ships a broken URL.
            string html = File.ReadAllText(index);
            string ext = Path.GetExtension(dataFiles[0]);   // ".unityweb" or ".br"
            foreach (var name in new[] { "webgl.data" + ext, "webgl.wasm" + ext,
                                         "webgl.framework.js" + ext, "webgl.loader.js" })
                html = html.Replace("/" + name + "\"", "/" + name + "?v=" + stamp + "\"");
            File.WriteAllText(index, html);
            Debug.Log("[BuildWebGL] payload URLs stamped ?v=" + stamp
                      + "  (stale-cache crash guard)");

            long bytes = 0;
            foreach (var f in Directory.GetFiles(outDir, "*", SearchOption.AllDirectories))
                bytes += new FileInfo(f).Length;
            Debug.Log("[BuildWebGL] OK - " + Path.GetFullPath(outDir)
                      + "  on-disk total " + (bytes / 1048576.0).ToString("0.0") + " MB");
            // ⚠ Done() calls EditorApplication.Exit in batchmode, and Exit never
            // returns - so the finally below CANNOT run on the success path.
            // Restore here or RB_PORTAL leaks into the next ordinary web build
            // and silently ships it without the ARENA tab. Measured 2026-09-08:
            // it leaked exactly once, on the first green portal build.
            RestoreDefines(priorDefines);
            Done(0);
            }
            finally
            {
                RestoreDefines(priorDefines);   // the failure path; the success path restored above
            }
        }

        static void RestoreDefines(string priorDefines)
        {
            if (!Portal) return;
            if (PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.WebGL) == priorDefines) return;
            PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.WebGL, priorDefines);
            AssetDatabase.SaveAssets();
        }

        static string Arg(string name)
        {
            var argv = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < argv.Length - 1; i++)
                if (argv[i] == name) return argv[i + 1];
            return null;
        }

        static void Fail(string why)
        {
            Debug.LogError("[BuildWebGL] " + why);
            Done(1);
        }

        static void Done(int code)
        {
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }
}
