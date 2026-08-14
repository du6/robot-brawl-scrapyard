#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace RobotBrawl.Editor
{
    /// <summary>Unity 6 sets the iOS DEVICE icon sizes (180/120/167/152/76)
    /// but does NOT write the 1024 "ios-marketing" entry into the generated
    /// AppIcon.appiconset — and App Store Connect REJECTS the upload without
    /// it ("Missing app icon … 1024 by 1024 … Any Appearance well", error
    /// 91111, hit 2026-08-14). This post-process injects the marketing icon
    /// from Assets/RobotBrawlIcon.png (which must stay a 1024, NO-ALPHA PNG —
    /// the App Store forbids alpha) so every build is upload-ready.</summary>
    public static class AppIconMarketingPostProcess
    {
        const string SourceIcon = "Assets/RobotBrawlIcon.png";

        [PostProcessBuild(9000)]
        public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
        {
            if (target != BuildTarget.iOS) return;

            var iconset = Path.Combine(pathToBuiltProject,
                "Unity-iPhone/Images.xcassets/AppIcon.appiconset");
            if (!Directory.Exists(iconset))
            { Debug.LogWarning("[AppIcon] appiconset not found at " + iconset); return; }

            var src = Path.Combine(Directory.GetCurrentDirectory(), SourceIcon);
            if (!File.Exists(src))
            { Debug.LogWarning("[AppIcon] " + SourceIcon + " missing — marketing icon not injected"); return; }

            File.Copy(src, Path.Combine(iconset, "Icon-marketing-1024.png"), true);

            var contents = Path.Combine(iconset, "Contents.json");
            string json = File.ReadAllText(contents);
            if (!json.Contains("ios-marketing"))
            {
                // Insert the marketing entry as the first image so a plain
                // string edit is enough — no JSON library, no reformat of what
                // Unity wrote.
                const string marker = "\"images\" : [";
                int i = json.IndexOf(marker);
                if (i >= 0)
                {
                    string entry = marker + "\n\t\t{ \"filename\" : \"Icon-marketing-1024.png\", "
                                 + "\"idiom\" : \"ios-marketing\", \"scale\" : \"1x\", \"size\" : \"1024x1024\" },";
                    json = json.Substring(0, i) + entry + json.Substring(i + marker.Length);
                    File.WriteAllText(contents, json);
                    Debug.Log("[AppIcon] injected 1024 ios-marketing icon into the asset catalog");
                }
            }
        }
    }
}
#endif
