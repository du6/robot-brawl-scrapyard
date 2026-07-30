using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RobotBrawl.Phase0
{
/// <summary>Scripted smoke test for the touch builder. Boots the mobile UI
/// and drives EVERY dock control through the same seams a finger uses,
/// asserting state after each step. Born 2026-07-30, after the UNDO/REMOVE
/// buttons shipped broken: the hands-on playtest only walked the happy path.
/// Rule now: if a button exists, a test taps it - in the state a player
/// would actually be in (UNDO is pressed with NOTHING held, because that is
/// exactly when players reach for it and exactly where the bug hid).
/// Run in play mode: TouchSmoke.Run(); read [TouchSmoke] console lines.</summary>
public class TouchSmoke : MonoBehaviour
{
    public static TouchSmoke Run()
    { return new GameObject("touch_smoke").AddComponent<TouchSmoke>(); }

    public int passed, failed;
    public bool finished;
    readonly List<string> log = new List<string>();

    void Check(bool ok, string what)
    {
        if (ok) passed++; else failed++;
        log.Add((ok ? "PASS  " : "FAIL  ") + what);
    }

    static Button Btn(string prefix)
    {
        foreach (var b in Object.FindObjectsByType<Button>(FindObjectsSortMode.None))
        {
            var t = b.GetComponentInChildren<Text>();
            if (t != null && t.text.StartsWith(prefix)) return b;
        }
        return null;
    }

    static bool Tap(string prefix)
    { var b = Btn(prefix); if (b == null) return false; b.onClick.Invoke(); return true; }

    static Vector3 PartOnScreen(bool core)
    {
        foreach (var col in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
        {
            string nm = col.gameObject.name;
            if (nm == "builder_floor") continue;
            bool isCore = nm.StartsWith("core");
            if (isCore != core) continue;
            return Camera.main.WorldToScreenPoint(col.bounds.center);
        }
        return Vector3.zero;
    }

    IEnumerator Start()
    {
        var ms = GameObject.Find("ModeSelect");
        if (ms != null) Destroy(ms);
        MobileBuilderUI.forceMobileUI = true;
        var bm = Object.FindFirstObjectByType<BuilderManager>();
        if (bm == null) bm = new GameObject("BuilderManager").AddComponent<BuilderManager>();
        float t0 = Time.realtimeSinceStartup;
        while (!MobileBuilderUI.Active && Time.realtimeSinceStartup - t0 < 6f) yield return null;
        Check(MobileBuilderUI.Active, "mobile UI attaches");
        var ui = MobileBuilderUI.inst;
        yield return null; yield return null;

        Check(Btn("BUILD") != null && Btn("FIGHT") != null && Btn("GARAGE") != null, "three tab buttons present");

        string[] chips = { "ABS Plastic", "Aluminum", "Steel", "Titanium", "Carbon Fiber", "Tungsten" };
        bool allChips = true;
        foreach (var c in chips) { var b = Btn(c); if (b == null || !b.interactable) allChips = false; }
        Check(allChips, "all 6 material chips present + tappable");
        Tap("Titanium"); yield return null;
        Check(bm.ActiveMatKey == "Titanium", "tapping Titanium selects it");
        Tap("ABS Plastic"); yield return null;
        Check(bm.ActiveMatKey == "ABS", "tapping ABS switches back");

        Check(Btn("Beam") != null, "Beam part button present");
        Tap("Beam"); yield return null;
        Check(bm.HasSelection, "tapping a part button selects it");

        Vector3 corePos = PartOnScreen(true);
        Check(corePos != Vector3.zero, "core visible for placement");
        int n0 = bm.PlacedCount;
        Phase0Input.debugPointer = true;
        Phase0Input.debugMousePos = corePos;
        yield return null; yield return null;
        var yaw0 = bm.TestGhostYaw;
        Phase0Input.DebugRotate();
        yield return null; yield return null;
        Check(!yaw0.Equals(bm.TestGhostYaw), "ROTATE turns the ghost");
        Phase0Input.DebugClick(0);
        yield return null; yield return null;
        Check(bm.PlacedCount == n0 + 1, "tap places the part on the core");

        Tap("DONE"); yield return null;
        Check(!bm.HasSelection, "DONE deselects");
        Phase0Input.debugPointer = false;
        int n1 = bm.PlacedCount;
        Tap("UNDO"); yield return null; yield return null;
        Check(bm.PlacedCount == n1 - 1, "UNDO with nothing held reverts the placement");

        Tap("Beam"); yield return null;
        Phase0Input.debugPointer = true;
        Phase0Input.debugMousePos = corePos;
        yield return null; yield return null;
        Phase0Input.DebugClick(0);
        yield return null; yield return null;
        Check(bm.PlacedCount == n1, "re-place for the REMOVE test");
        Tap("DONE"); yield return null;

        Check(Btn("REMOVE") != null, "REMOVE button present");
        Tap("REMOVE"); yield return null;
        Check(ui.RemoveArmed, "REMOVE arms on tap");
        Check(ui.StatsLine.Contains("REMOVE armed"), "stats bar shows the armed instruction");
        Vector3 beamPos = PartOnScreen(false);
        Check(beamPos != Vector3.zero, "placed part visible for removal");
        int n2 = bm.PlacedCount;
        Phase0Input.debugPointer = true;
        Phase0Input.debugMousePos = beamPos;
        yield return null;
        Phase0Input.DebugClick(1);
        yield return null; yield return null;
        Check(bm.PlacedCount == n2 - 1, "armed tap removes the part");
        if (ui.RemoveArmed) Tap("REMOVE");
        yield return null;
        Check(!ui.RemoveArmed, "REMOVE disarms");

        Tap("GARAGE"); yield return null;
        Check(Btn("SAVE") != null && Btn("LOAD") != null, "garage SAVE/LOAD buttons present (not tapped: would overwrite owner slots)");

        Tap("FIGHT"); yield return null;
        bool tapped = Tap("TEST");
        yield return null; yield return null;
        Check(tapped && bm.LastMessage != null && bm.LastMessage.Contains("wheel"), "invalid build blocked with a visible message");
        Tap("BUILD"); yield return null;

        foreach (var l in log) Debug.Log("[TouchSmoke] " + l);
        Debug.Log(string.Format("[TouchSmoke] RESULT: {0} pass, {1} fail{2}",
                  passed, failed, failed == 0 ? " - ALL GREEN" : " - FIX NEEDED"));
        finished = true;
    }
}
}
