// ===========================================================================
// UiShot — screenshot the GAME view, including IMGUI. 2026-08-09.
//
//   RobotBrawl.Phase0.UiShot.Take("/tmp/shot.png");        // in play mode
//
// WHY THIS EXISTS. The Unity MCP bridge's capture tools render from a CAMERA
// into a RenderTexture. IMGUI (OnGUI) is never drawn by a camera — it is
// composited into the Game view during the GUI phase — so a camera capture of
// an OnGUI screen comes back showing the scene and none of the interface. A
// session looking at that concludes it cannot screenshot its own UI, which is
// wrong, and then ships a layout nobody has ever seen. That happened, on the
// day this file was written.
//
// ScreenCapture.CaptureScreenshotAsTexture reads the composited frame, which
// is the one surface IMGUI reaches. It MUST run after rendering — from a
// coroutine, after WaitForEndOfFrame. Called any earlier it returns black,
// which looks like a broken screen rather than a mistimed capture.
// ===========================================================================
using System;
using System.Collections;
using UnityEngine;

namespace RobotBrawl.Phase0
{
    public class UiShot : MonoBehaviour
    {
        /// <summary>Set true when the file is on disk — poll this from
        /// outside, since the domain reload rules out holding a reference.</summary>
        public static bool Done;
        public static string LastPath = "", LastError = "";
        public static int LastBytes, LastWidth, LastHeight;

        /// <summary>Grab the game view after `settle` seconds and write a PNG.
        /// The delay is not decoration: a screen that fetches its own data
        /// draws empty on frame one, and a screenshot of a half-loaded UI is
        /// worse than none because it looks like a bug.</summary>
        public static void Take(string path, float settle = 1.0f)
        {
            Done = false; LastError = ""; LastPath = path; LastBytes = 0;
            var go = new GameObject("ui_shot");
            var s = go.AddComponent<UiShot>();
            s.StartCoroutine(s.Run(path, settle));
        }

        IEnumerator Run(string path, float settle)
        {
            if (settle > 0f) yield return new WaitForSeconds(settle);
            yield return new WaitForEndOfFrame();

            Texture2D tex = null;
            try
            {
                tex = ScreenCapture.CaptureScreenshotAsTexture();
                byte[] png = tex.EncodeToPNG();
                string dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllBytes(path, png);
                LastBytes = png.Length; LastWidth = tex.width; LastHeight = tex.height;
            }
            catch (Exception e) { LastError = e.Message; }
            finally
            {
                if (tex != null) Destroy(tex);
                Done = true;
                Destroy(gameObject);
            }

            Debug.Log(string.IsNullOrEmpty(LastError)
                ? "[UiShot] " + LastBytes + " bytes " + LastWidth + "x" + LastHeight + " -> " + path
                : "[UiShot] FAILED: " + LastError);
        }
    }
}
