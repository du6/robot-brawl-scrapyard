using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

namespace RobotBrawl.Phase0
{
// ============================================================================
// PlaytestBench — the harness that PLAYS the game instead of reading it.
//
// Owen, 2026-08-15, after four device-caught escapes in a row (ENLIST no-op,
// THE BOARD no-op, results overlap, material-after-undo): "a user agent should
// be able to play with the game and capture these bugs." The retrospective:
// every other bench in this project either reads code or calls a Test* seam
// BELOW the render layer, so a button that changes state but shows nothing —
// or a control drawn on top of another — is invisible to all of them. This
// project's own CLAUDE.md has said so in bold for weeks.
//
// This bench is different in exactly one way that matters: after every real
// onClick it asks WHAT A USER WOULD SEE — which panel is actually on screen,
// whether two tappable controls overlap, whether anything sits under the notch.
// It drives the SAME buttons a finger drives (button.onClick.Invoke(), the
// WATCH-button lesson) and asserts on the RENDER DECISION (panel roots' active
// state, screen-space RectTransforms), never on the flags underneath.
//
// It is deliberately NON-DESTRUCTIVE: signed-in ARENA nav is faked with a
// throwaway token and an unreachable BaseUrl, so no account is touched and no
// network wait stalls it; the career is scratch and autosave is held.
//
// Run in play mode: PlaytestBench.Run(); read [Playtest] console lines. To test
// a specific aspect ratio (the results-overlap class only shows on a short
// landscape screen), run it under the Device Simulator on that device profile —
// the geometry checks read the live Screen size.
//
// v1 scope: dock-tab nav, ARENA sub-tab nav (the THE-BOARD / ENLIST class),
// per-surface overlap + safe-area checks, and a captured frame per surface.
// Owed next: the IMGUI fight-results layout (its overlap needs the layout-math
// lint, not a RectTransform scan) and a vision pass over the captured frames.
// ============================================================================
public class PlaytestBench : MonoBehaviour
{
    public static PlaytestBench Run()
    { return new GameObject("playtest_bench").AddComponent<PlaytestBench>(); }

    public int passed, failed;
    public bool finished;
    readonly List<string> log = new List<string>();

    readonly List<string> fails = new List<string>();
    void Pass(string w) { passed++; log.Add("PASS  " + w); }
    void Fail(string w) { failed++; fails.Add(w); log.Add("FAIL  " + w); }
    void Note(string w) { log.Add("      " + w); }
    void Check(bool ok, string w) { if (ok) Pass(w); else Fail(w); }

    // ------------------------------------------------------------- primitives
    static Button ByName(string name)
    {
        foreach (var b in Object.FindObjectsByType<Button>(FindObjectsSortMode.None))
            if (b.gameObject.name == name) return b;
        return null;
    }
    static Button ByLabel(string label)
    {
        foreach (var b in Object.FindObjectsByType<Button>(FindObjectsSortMode.None))
        {
            var t = b.GetComponentInChildren<Text>();
            if (t != null && t.text == label) return b;
        }
        return null;
    }
    /// <summary>Fire a button's REAL onClick — the behaviour lives there, not in
    /// any seam below it (the WATCH-button lesson). Returns false if not found.</summary>
    static bool Fire(Button b) { if (b == null) return false; b.onClick.Invoke(); return true; }

    /// <summary>Screen-space rect of a control. For a ScreenSpaceOverlay canvas
    /// the world corners ARE pixel coordinates.</summary>
    static Rect ScreenRect(RectTransform rt)
    {
        var c = new Vector3[4];
        rt.GetWorldCorners(c);
        float xmin = Mathf.Min(c[0].x, c[2].x), xmax = Mathf.Max(c[0].x, c[2].x);
        float ymin = Mathf.Min(c[0].y, c[2].y), ymax = Mathf.Max(c[0].y, c[2].y);
        return new Rect(xmin, ymin, xmax - xmin, ymax - ymin);
    }
    static Rect Intersect(Rect a, Rect b)
    {
        float xmin = Mathf.Max(a.xMin, b.xMin), xmax = Mathf.Min(a.xMax, b.xMax);
        float ymin = Mathf.Max(a.yMin, b.yMin), ymax = Mathf.Min(a.yMax, b.yMax);
        return new Rect(xmin, ymin, Mathf.Max(0f, xmax - xmin), Mathf.Max(0f, ymax - ymin));
    }
    /// <summary>The rect the user can ACTUALLY see: the control clipped by every
    /// ancestor scroll mask (RectMask2D / Mask). A list row scrolled to the edge
    /// of its viewport has a full rect poking past the screen but a clipped,
    /// on-screen visible rect — checking the full rect was v1's safe-area false
    /// positive.</summary>
    static Rect VisibleRect(RectTransform rt)
    {
        Rect r = ScreenRect(rt);
        for (var p = rt.parent; p != null; p = p.parent)
        {
            bool masks = p.GetComponent<RectMask2D>() != null
                      || (p.GetComponent<Mask>() != null && p.GetComponent<Mask>().enabled);
            if (masks && p is RectTransform prt) r = Intersect(r, ScreenRect(prt));
        }
        return r;
    }
    static float OverlapArea(Rect a, Rect b)
    {
        float w = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
        float h = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
        return (w > 0 && h > 0) ? w * h : 0f;
    }
    static bool IsAncestor(Transform maybeAncestor, Transform t)
    {
        for (var p = t.parent; p != null; p = p.parent) if (p == maybeAncestor) return true;
        return false;
    }

    // The tappable controls actually on screen right now.
    static List<Button> LiveButtons()
    {
        var outl = new List<Button>();
        foreach (var b in Object.FindObjectsByType<Button>(FindObjectsSortMode.None))
        {
            if (!b.isActiveAndEnabled) continue;
            if (!b.gameObject.activeInHierarchy) continue;
            var rt = b.transform as RectTransform;
            if (rt == null) continue;
            var r = VisibleRect(rt);   // clipped by scroll masks
            if (r.width < 2f || r.height < 2f) continue;   // collapsed / fully scrolled out
            // Centre of the VISIBLE part must be on screen.
            if (r.center.x < 0f || r.center.x > Screen.width || r.center.y < 0f || r.center.y > Screen.height) continue;
            outl.Add(b);
        }
        return outl;
    }

    void Frame(string tag)
    {
        // Best-effort composited capture (uGUI overlay + IMGUI), the UiShot way.
        try
        {
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            if (tex != null) { Note("frame[" + tag + "] " + tex.width + "x" + tex.height); Destroy(tex); }
        }
        catch (System.Exception e) { Note("frame[" + tag + "] capture skipped: " + e.Message); }
    }

    // --------------------------------------------------------------- checks
    /// <summary>No two DISTINCT leaf tap targets should sit on top of each other.
    /// Conservative: only a heavy overlap (>55% of the smaller) between two
    /// buttons where neither contains the other — that is the "drawn on top of"
    /// defect (TEST DRIVE's dead half, the REMOVE-armed eaten taps), not a chip
    /// nested in its row.</summary>
    void CheckNoOverlap(string surface)
    {
        var bs = LiveButtons();
        int hits = 0;
        for (int i = 0; i < bs.Count; i++)
            for (int j = i + 1; j < bs.Count; j++)
            {
                if (IsAncestor(bs[i].transform, bs[j].transform)) continue;
                if (IsAncestor(bs[j].transform, bs[i].transform)) continue;
                var ri = VisibleRect(bs[i].transform as RectTransform);
                var rj = VisibleRect(bs[j].transform as RectTransform);
                float ov = OverlapArea(ri, rj);
                if (ov <= 0f) continue;
                float smaller = Mathf.Min(ri.width * ri.height, rj.width * rj.height);
                if (smaller > 0f && ov / smaller > 0.55f)
                {
                    hits++;
                    Note("overlap: '" + bs[i].gameObject.name + "' vs '" + bs[j].gameObject.name
                         + "' (" + Mathf.RoundToInt(100f * ov / smaller) + "% of smaller)");
                }
            }
        Check(hits == 0, surface + ": no two tap targets overlap (" + bs.Count + " live buttons)");
    }

    /// <summary>Every live tap target is inside the safe area — nothing under the
    /// notch or in the home-indicator strip.</summary>
    void CheckSafeArea(string surface)
    {
        Rect sa = Screen.safeArea;
        var bs = LiveButtons();
        int outside = 0;
        foreach (var b in bs)
        {
            var r = VisibleRect(b.transform as RectTransform);   // clipped to what's actually shown
            // 2px grace for rounding
            if (r.xMin < sa.xMin - 2f || r.xMax > sa.xMax + 2f ||
                r.yMin < sa.yMin - 2f || r.yMax > sa.yMax + 2f)
            { outside++; Note("outside safe area: '" + b.gameObject.name + "' rect=" + r); }
        }
        Check(outside == 0, surface + ": every tap target is within the safe area");
    }

    // --------------------------------------------------------------- journeys
    static void OpenDock(MobileBuilderUI ui)
    {
        if (ui != null && !ui.DockOpen)
        {
            var dh = ByName("dockhandle");
            if (dh != null) dh.onClick.Invoke();
        }
    }

    IEnumerator All()
    {
        // ---- isolation: hold the career + fake the session -----------------
        var hold = Career.SuspendAutosave();
        var savedData = Career.Data; bool savedActive = Career.active;
        string savedTok = LadderClient.Token; string savedUrl = LadderClient.BaseUrl;
        bool savedExp = LadderClient.SessionExpired;
        MobileBuilderUI.forceMobileUI = true;

        try
        {
            var ms = GameObject.Find("ModeSelect"); if (ms != null) Destroy(ms);
            var bm = Object.FindFirstObjectByType<BuilderManager>();
            if (bm == null) bm = new GameObject("BuilderManager").AddComponent<BuilderManager>();

            // Career mode so all six tabs (incl. ARENA) exist; scratch data.
            Career.active = true;
            Career.Data = new CareerData();
            Career.Data.stable.Add(new CareerRobot { name = "Playtest", snapshot = "", program = "" });
            Career.Data.activeRobot = 0;

            if (MobileBuilderUI.inst != null) { Destroy(MobileBuilderUI.inst.gameObject); yield return null; }
            float t0 = Time.realtimeSinceStartup;
            while (!MobileBuilderUI.Active && Time.realtimeSinceStartup - t0 < 6f) yield return null;
            Check(MobileBuilderUI.Active, "the mobile dock attaches");
            var ui = MobileBuilderUI.inst;
            if (ui == null) { yield break; }
            yield return null; yield return null;
            OpenDock(ui);
            yield return null;
            Check(ui.DockOpen, "the dock opens");

            // ================================================================
            // JOURNEY 1 — every dock tab actually SHOWS its panel when tapped.
            // (The dead-tab / no-op class: a tap that changes nothing on screen.)
            // ================================================================
            log.Add("== J1. dock tabs ==");
            // Iterate the REAL tab buttons (tab0, tab1, …) until they run out —
            // TabCount counts panels, of which there are more than tabs.
            int nTabs = 0;
            while (ByName("tab" + nTabs) != null && nTabs < 8) nTabs++;
            Check(nTabs >= 5, "the dock built its tab buttons (" + nTabs + ")");
            for (int i = 0; i < nTabs; i++)
            {
                var tb = ByName("tab" + i);
                string label = tb.GetComponentInChildren<Text>() != null ? tb.GetComponentInChildren<Text>().text : ("tab" + i);
                if (!ui.DockOpen) OpenDock(ui);
                tb.onClick.Invoke();
                yield return null; yield return null;
                Check(ui.Tab == i, "tapping " + label + " selects tab " + i);
                Check(ui.TestVisibleTab() == i, "…and " + label + "'s content is actually on screen -- got tab " + ui.TestVisibleTab());
                Check(ui.TestActivePanelCount() == 1, "…and exactly one tab panel is showing (no stale panel under it) -- " + ui.TestActivePanelCount());
                Frame("tab_" + label);
                CheckNoOverlap("tab " + label);
                CheckSafeArea("tab " + label);
            }

            // ================================================================
            // JOURNEY 2 — ARENA sub-tabs switch the visible surface.
            // This is THE BOARD's exact bug: a signed-in player on ENLIST taps
            // THE BOARD and the board must appear. Faked signed-in (throwaway
            // token, unreachable URL) so surface switching — which is LOCAL —
            // is exercised without an account or a network wait.
            // ================================================================
            log.Add("== J2. ARENA sub-tabs (faked signed-in) ==");
            LadderClient.BaseUrl = "http://127.0.0.1:1";   // refused instantly; no 30s hang
            LadderClient.Token = "playtest-fake-session";  // SignedIn == true
            // open the ARENA tab
            int arenaIdx = -1;
            for (int i = 0; i < nTabs; i++) { var tb = ByName("tab" + i); var t = tb != null ? tb.GetComponentInChildren<Text>() : null; if (t != null && t.text == "ARENA") { arenaIdx = i; break; } }
            Check(arenaIdx >= 0, "the ARENA tab exists");
            if (arenaIdx >= 0)
            {
                if (!ui.DockOpen) OpenDock(ui);
                ByName("tab" + arenaIdx).onClick.Invoke();
                yield return null; yield return null; yield return null;

                // ENLIST → account surface
                Check(Fire(ByName("arenasec_account")), "ENLIST sub-tab button exists");
                yield return null; yield return null;
                Check(ui.TestArenaSurface == "account", "tapping ENLIST shows the ENLIST/account surface -- got '" + ui.TestArenaSurface + "'");
                Frame("arena_enlist");

                // THE BOARD → board surface  (the escaped bug)
                Check(Fire(ByName("arenasec_board")), "THE BOARD sub-tab button exists");
                yield return null; yield return null;
                Check(ui.TestArenaSurface == "board",
                      "tapping THE BOARD from ENLIST actually shows the board -- got '" + ui.TestArenaSurface + "'");
                Frame("arena_board");
                CheckNoOverlap("arena board");
                CheckSafeArea("arena board");

                // MY FIGHTS → inbox surface
                Check(Fire(ByName("arenasec_inbox")), "MY FIGHTS sub-tab button exists");
                yield return null; yield return null;
                Check(ui.TestArenaSurface == "inbox", "tapping MY FIGHTS shows the inbox -- got '" + ui.TestArenaSurface + "'");

                // and back to THE BOARD once more — round-trips must not stick
                Fire(ByName("arenasec_board"));
                yield return null; yield return null;
                Check(ui.TestArenaSurface == "board", "THE BOARD works again after MY FIGHTS -- got '" + ui.TestArenaSurface + "'");
            }
        }
        finally
        {
            LadderClient.Token = savedTok; LadderClient.BaseUrl = savedUrl; LadderClient.SessionExpired = savedExp;
            Career.Data = savedData; Career.active = savedActive;
            if (hold != null) hold.Dispose();
            foreach (var l in log) Debug.Log("[Playtest] " + l);
            Debug.Log(string.Format("[Playtest] RESULT: {0} pass, {1} fail{2}",
                      passed, failed, failed == 0 ? " - ALL GREEN" : " - FIX NEEDED"));
            if (fails.Count > 0) Debug.Log("[Playtest] FAILS >>> " + string.Join("  ||  ", fails));
            if (failed == 0 && passed == 0) Debug.Log("[Playtest] NOTHING RAN — this is not a pass.");
            finished = true;
        }
    }

    void Start() { StartCoroutine(All()); }
}
}
