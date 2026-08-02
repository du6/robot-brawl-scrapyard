using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RobotBrawl.Phase0
{

/// <summary>
/// CAREER VISUAL CAPTURE (2026-07-31).
///
/// The enabling instrument for the career visual-critic loop, and it exists
/// because the obvious approaches do not work here:
///
///   * Unity_Camera_Capture renders a CAMERA. MobileBuilderUI's canvas is
///     RenderMode.ScreenSpaceOverlay and the desktop panel is IMGUI OnGUI -
///     NEITHER goes through a camera, so a camera capture of the Workshop
///     returns the empty build room and nothing else. A critic handed those
///     images would confidently review a screen it never saw.
///   * SceneView captures show the scene, not the game's UI, for the same
///     reason.
///
/// ScreenCapture.CaptureScreenshotAsTexture reads the actual back buffer at
/// end of frame, so it gets the overlay canvas, IMGUI, and the 3D view exactly
/// as the player sees them. That is the only honest source for a review whose
/// entire subject is what things LOOK like.
///
/// Captures are named by screen so a round-N/round-N+1 pair can be diffed by
/// eye, and every run writes an index file listing what was shot and at what
/// resolution - a screenshot with no provenance is not evidence.
/// </summary>
public class CareerShot : MonoBehaviour
{
    public string outDir = "Assets/Phase1/qa_shots";
    public string tag = "r0";
    public bool mobile = true;
    public bool done;
    public string summary = "";
    /// <summary>Also photograph the SCOUT overlay (free - StartScout costs
    /// nothing and EndScout puts the build root back).</summary>
    public bool scout = true;
    /// <summary>Also photograph a live fight frame. Opt-in because it runs a
    /// real career contest: the coroutine forces Career.autosave off and
    /// reloads from disk afterwards, but it still perturbs in-memory state,
    /// so a round that has seeded Career.Data by hand should leave it off.</summary>
    public bool fight;
    public int contestLeague, contestIndex;
    /// <summary>Desktop IMGUI shot only: select this palette index and scroll
    /// the panel to it before shooting. The desktop shop block only draws for
    /// the SELECTED part and sits far below the fold, so without these the
    /// desktop capture photographs the top of the panel and proves nothing
    /// about the shop. -1 / negative = leave both alone.</summary>
    public int desktopSelect = -1;
    public float desktopScroll = -1f;

    readonly List<string> log = new List<string>();

    public void Run() { StartCoroutine(Go()); }

    IEnumerator Go()
    {
        string dir = Path.Combine(Application.dataPath, "Phase1/qa_shots");
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        // FIRST FRAME FIRST. Whatever is on screen at boot is the player's
        // first impression of the game and therefore squarely inside a visual
        // review's remit - shoot it before this harness changes anything.
        yield return Shot(dir, "boot");

        Career.active = true;
        if (Career.Data == null || Career.Data.inventory.Count == 0) Career.Load();

        var bm = Object.FindFirstObjectByType<BuilderManager>();
        if (bm == null)
        {
            foreach (var m in Object.FindObjectsByType<ModeSelect>(FindObjectsSortMode.None))
                Object.Destroy(m.gameObject);
            yield return null;
            bm = new GameObject("BuilderManager").AddComponent<BuilderManager>();
            float tb = Time.realtimeSinceStartup;
            while (bm.placed.Count == 0 && Time.realtimeSinceStartup - tb < 8f) yield return null;
        }

        MobileBuilderUI.forceMobileUI = mobile;
        TouchControls.mouseTest = true;
        yield return null;

        // Let the mobile UI build itself if it is not up yet.
        float t0 = Time.realtimeSinceStartup;
        while (mobile && !MobileBuilderUI.Active && Time.realtimeSinceStartup - t0 < 6f)
            yield return null;

        var ui = MobileBuilderUI.inst;
        log.Add("# CAREER VISUAL CAPTURE  tag=" + tag);
        log.Add("# mobileUI=" + (ui != null) + "  screen=" + Screen.width + "x" + Screen.height);
        log.Add("# career scrap=" + Career.Data.scrap + " stable=" + Career.Data.stable.Count
                + " inventory=" + Career.Data.inventory.Count);

        if (ui != null)
        {
            string[] names = { "build", "league", "robots", "shop", "parts" };
            for (int i = 0; i < names.Length; i++)
            {
                ui.TestShowTab(i);
                // two frames: one to switch, one to lay out
                yield return null; yield return null;
                yield return Shot(dir, names[i]);
            }
            ui.TestShowTab(0);
            yield return null;
        }

        // ---- SCOUT overlay (BuilderManager.ScoutHud, IMGUI) --------------
        if (scout && bm != null && Career.active)
        {
            bm.StartScout(contestLeague, contestIndex);
            float ts0 = Time.realtimeSinceStartup;
            while (!bm.Scouting && Time.realtimeSinceStartup - ts0 < 4f) yield return null;
            // the display bot spawns over a frame or two and the turntable
            // wants a beat before it reads as a presentation, not a pop-in
            for (int f = 0; f < 40; f++) yield return null;
            log.Add("# scout: Scouting=" + bm.Scouting);
            yield return Shot(dir, "scout");
            bm.EndScout();
            yield return null; yield return null;
        }

        // ---- Desktop IMGUI panel -----------------------------------------
        // WHY THIS IS NOT JUST forceMobileUI = false:
        // the canvas belongs to a LIVE MobileBuilderUI component, and
        // BuilderManager.OnGUI() early-returns on `if (MobileBuilderUI.Active)`,
        // which is `inst != null`. Clearing the static flag stops the WATCHER
        // re-creating the UI; it does not remove the instance that already
        // exists, so the desktop panel never drew and rN_desktop_build.png
        // came back a pixel-for-pixel copy of rN_build.png. Every desktop
        // claim made from those images (r0, r1crit) was made from the mobile
        // screen. Destroy the instance, then PROVE Active == false before
        // shooting - MobileBuilderWatch runs every frame and will happily put
        // it back if ShouldActivate() is still true.
        MobileBuilderUI.forceMobileUI = false;
        for (int f = 0; f < 6; f++)
        {
            if (MobileBuilderUI.inst != null) Object.DestroyImmediate(MobileBuilderUI.inst.gameObject);
            yield return null;
        }
        if (bm != null && desktopSelect >= 0)
        {
            bm.SelectPart(desktopSelect);
            log.Add("# desktop: selected palette " + desktopSelect + " (" + bm.PartLabel(desktopSelect) + ")");
        }
        if (bm != null && desktopScroll >= 0f) bm.PanelScrollY = desktopScroll;
        yield return null; yield return null;
        log.Add("# desktop: MobileBuilderUI.Active=" + MobileBuilderUI.Active
                + " (false = the IMGUI panel really drew)");
        if (MobileBuilderUI.Active)
            log.Add("# WARNING desktop shot is NOT the desktop panel - mobile UI respawned");
        yield return Shot(dir, "desktop_build");

        // ---- a live fight frame -------------------------------------------
        if (fight && bm != null && Career.active)
        {
            bool savedAuto = Career.autosave;
            Career.autosave = false;
            bm.StartCareerFight(contestLeague, contestIndex);
            float tf = Time.realtimeSinceStartup;
            while (Object.FindFirstObjectByType<FightManager>() == null
                   && Time.realtimeSinceStartup - tf < 6f) yield return null;
            bool started = Object.FindFirstObjectByType<FightManager>() != null;
            log.Add("# fight: started=" + started + (started ? "" : "  msg=" + bm.LastMessage));
            if (started)
            {
                // let the bots close and the HUD populate - a frame-0 fight
                // shot is two robots standing still on an empty floor
                float tw = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - tw < 4f) yield return null;
                yield return Shot(dir, "fight");
                bm.BackToBuild();
                yield return null; yield return null;
            }
            Career.Load();          // undo the entry fee / result in memory
            Career.autosave = savedAuto;
        }

        // leave the editor the way this harness likes to find it
        MobileBuilderUI.forceMobileUI = mobile;
        Finish("captured " + shots + " screens");
    }

    int shots;

    IEnumerator Shot(string dir, string name)
    {
        yield return new WaitForEndOfFrame();
        Texture2D tex = null;
        try { tex = ScreenCapture.CaptureScreenshotAsTexture(); }
        catch (System.Exception e) { log.Add("# capture failed " + name + ": " + e.Message); yield break; }
        if (tex == null) { log.Add("# null texture for " + name); yield break; }

        string file = Path.Combine(dir, tag + "_" + name + ".png");
        try
        {
            File.WriteAllBytes(file, tex.EncodeToPNG());
            log.Add(string.Format("{0,-16} {1}x{2}  {3}", name, tex.width, tex.height,
                                  Path.GetFileName(file)));
            shots++;
        }
        catch (System.Exception e) { log.Add("# write failed " + name + ": " + e.Message); }
        finally { Object.Destroy(tex); }
    }

    void Finish(string msg)
    {
        summary = msg;
        log.Add("# " + msg);
        try
        {
            File.WriteAllText(Path.Combine(Application.dataPath, "Phase1/qa_shots/index_" + tag + ".txt"),
                              string.Join("\n", log.ToArray()) + "\n");
        }
        catch { }
        done = true;
        Debug.Log("CareerShot: " + msg);
    }
}

}
