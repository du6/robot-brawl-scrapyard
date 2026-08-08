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
        // BENCH REPAIR 2026-08-05: the device auto-boot builds the touch UI
        // under the OWNER'S career, and C6.5 then (correctly) never builds the
        // sandbox garage tab — so every sandbox check below was measuring a
        // career dock and failing "since the auto-boot shipped", not since any
        // UI change. This smoke tests the SANDBOX dock: put the world in that
        // state and rebuild the UI in it (the watcher re-attaches next frame).
        Career.active = false;
        if (MobileBuilderUI.inst != null)
        {
            Destroy(MobileBuilderUI.inst.gameObject);
            yield return null;
        }
        float t0 = Time.realtimeSinceStartup;
        while (!MobileBuilderUI.Active && Time.realtimeSinceStartup - t0 < 6f) yield return null;
        Check(MobileBuilderUI.Active, "mobile UI attaches");
        var ui = MobileBuilderUI.inst;
        yield return null; yield return null;

        // Layout pass P2 (2026-08-06): at PHONE size the dock STARTS CLOSED
        // (the 08-04 inches rule) — every dock control is INACTIVE until the
        // dock opens, which is why this smoke's sandbox section read as 15
        // "missing" controls on the iPhone sim (probe showed them all present
        // once open; the product was fine, the smoke predates the rule).
        // Open it the way a finger would: the always-active handle chip.
        if (ui != null && !ui.DockOpen)
        {
            var dhGO = GameObject.Find("dockhandle");
            var dhb = dhGO != null ? dhGO.GetComponent<Button>() : null;
            Check(dhb != null, "phone-size closed dock shows its handle");
            if (dhb != null) { dhb.onClick.Invoke(); yield return null; }
            Check(ui.DockOpen, "tapping the handle opens the dock");
            yield return null;
        }

        Check(Btn("BUILD") != null && Btn("FIGHT") != null && Btn("GARAGE") != null, "three tab buttons present");

        // BENCH REPAIR 2026-08-05: the chips moved into a SHEET over the
        // palette (2026-08-04), opened by the material button — current
        // material plus a disclosure caret. The smoke now opens it the way a
        // player does, by FINDING and TAPPING that button: exactly the
        // affordance the old C17 SetMatSheet(true) shortcut never tested (and
        // the reason "no disclosure hint" once went unnoticed). The caret ends
        // the label; the ROBOTS rows lead with theirs, so EndsWith cannot
        // match the wrong thing.
        Button discl = null;
        foreach (var b in Object.FindObjectsByType<Button>(FindObjectsSortMode.None))
        {
            var dt = b.GetComponentInChildren<Text>();
            if (dt != null && (dt.text.EndsWith("▴") || dt.text.EndsWith("▾"))) { discl = b; break; }
        }
        Check(discl != null, "material chooser button carries the disclosure caret");
        if (discl != null) discl.onClick.Invoke();
        yield return null;
        Check(ui != null && ui.MatSheetOpen, "tapping it opens the material sheet");
        string[] chips = { "ABS Plastic", "Aluminum", "Steel", "Titanium", "Carbon Fiber", "Tungsten" };
        bool allChips = true;
        foreach (var c in chips) { var b = Btn(c); if (b == null || !b.interactable) allChips = false; }
        Check(allChips, "all 6 material chips present + tappable");
        Tap("Titanium"); yield return null;
        Check(bm.ActiveMatKey == "Titanium", "tapping Titanium selects it");
        // Selecting a chip closes the sheet (that IS the sheet's contract) —
        // reopen it the same way before the switch-back, as a finger would.
        if (ui != null && !ui.MatSheetOpen && discl != null) { discl.onClick.Invoke(); yield return null; }
        Tap("ABS Plastic"); yield return null;
        Check(bm.ActiveMatKey == "ABS", "tapping ABS switches back");
        if (ui != null && ui.MatSheetOpen && discl != null) { discl.onClick.Invoke(); yield return null; }   // leave it as found

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

        // ---- C1: career inventory asserts. IN-MEMORY ONLY - Career.Save() is
        // never called, so the owner's career file is untouched.
        var savedCareer = Career.Data;
        Career.Data = new CareerData();
        Career.AddItem("beam", "Aluminum", 1);
        Career.active = true;
        bm.ActiveMatKey = "Aluminum";
        int bi = -1;
        for (int i = 1; i < bm.PaletteCount; i++)
            if (bi < 0 && bm.PartLabel(i).StartsWith("Beam")) bi = i;
        yield return null;
        int nc = bm.PlacedCount;
        Tap("Beam"); yield return null;
        Phase0Input.debugPointer = true;
        Phase0Input.debugMousePos = corePos;
        // Real-time wait: this block runs right after a FIGHT->BUILD tab
        // switch, and the builder's click-grace window swallows clicks for
        // a beat after any full-screen transition. Frame-count yields are
        // not enough at editor frame rates.
        yield return new WaitForSeconds(0.6f);
        yield return null; yield return null;
        // Layout pass P2 (v2): corePos was captured back in the SANDBOX
        // phase — after the career fixture swap it no longer holds a valid
        // ghost at every window geometry, and clicks on an invalid ghost
        // are the ordinary silent deny. Hunt a valid spot FIRST (the same
        // pattern the refusal probe below already uses), then click until
        // the placement lands (the click-grace window can still swallow
        // the first tap at editor frame rates).
        bool preValid = false;
        for (int attempt = 0; attempt < 24 && !preValid; attempt++)
        {
            Vector3 cand = attempt % 2 == 0 ? PartOnScreen(true) : PartOnScreen(false);
            if (cand == Vector3.zero) { yield return null; continue; }
            cand.x += (attempt / 4) * 8f * (attempt % 4 < 2 ? 1f : -1f);
            Phase0Input.debugMousePos = cand;
            yield return null; yield return null;
            preValid = bm.TestGhostValid;
        }
        for (int pAttempt = 0; pAttempt < 4 && bm.PlacedCount != nc + 1; pAttempt++)
        {
            Phase0Input.DebugClick(0);
            yield return new WaitForSeconds(0.25f);
            yield return null; yield return null;
        }
        bool placedOne = bm.PlacedCount == nc + 1;
        // The stock gate only speaks on a PLACEABLE spot - an invalid ghost
        // is the ordinary deny (no message). Hunt a valid ghost first: the
        // core face, the placed beam, then small x-offsets around each.
        bool gotValid = false;
        for (int attempt = 0; attempt < 24 && !gotValid; attempt++)
        {
            Vector3 cand = attempt % 2 == 0 ? PartOnScreen(true) : PartOnScreen(false);
            if (cand == Vector3.zero) { yield return null; continue; }
            cand.x += (attempt / 4) * 8f * (attempt % 4 < 2 ? 1f : -1f);
            Phase0Input.debugMousePos = cand;
            yield return null; yield return null;
            gotValid = bm.TestGhostValid;
        }
        Phase0Input.DebugClick(0);   // the shelf is empty now - must refuse
        yield return null; yield return null;
        Debug.Log("[TouchSmoke][diag] placedOne=" + placedOne + " placed=" + bm.PlacedCount
                  + " nc=" + nc + " ghostValid=" + bm.TestGhostValid + " ghostTarget=" + bm.TestGhostTarget
                  + " rem=" + bm.CareerRemaining(bi) + " msg='" + bm.LastMessage + "'");
        Check(placedOne && gotValid && bm.PlacedCount == nc + 1
              && bm.LastMessage != null && bm.LastMessage.Contains("left"),
              "career: placement stops at the owned count with an amber message");
        Phase0Input.debugPointer = false;
        yield return null;
        var beamBtn2 = Btn("Beam");
        Check(bi >= 0 && beamBtn2 != null
              && beamBtn2.GetComponentInChildren<Text>().text.Contains(" 0 free"),
              // BENCH REPAIR 2026-08-05: the badge reads "N free" (remaining
              // stock) since the PARTS-shelf contract change, not "×N owned".
              // The leading space is load-bearing: "10 free" must not match.
              "career: part tile shows the 0-free stock badge");
        Tap("DONE"); yield return null;
        Tap("UNDO"); yield return null; yield return null;
        Check(bm.PlacedCount == nc && bm.CareerRemaining(bi) == 1,
              "career: undo returns the part to stock");
        Career.active = false;
        Career.Data = savedCareer;
        yield return null;
        // ---- CLIP SWEEP -------------------------------------------------
        // owen, 2026-08-07: "in the build tab the text of the bottom rows of
        // buttons looks cutoff a bit." It was: the 4-row part palette needed
        // 156 units of grid and the viewport gave 148, so the bottom row was
        // clipped by 8 of its 34 and lost the second line of every tile — the
        // free-stock count, on exactly the parts you run out of.
        //
        // Every bench was green through it, because they all check DATA and
        // this is GEOMETRY. So the check is the general invariant rather than
        // a note about the palette: CONTENT MAY OVERFLOW A VIEWPORT ONLY
        // ALONG AN AXIS THAT ACTUALLY SCROLLS. Overflow across a scrolling
        // axis is a swipe away; overflow across a FIXED axis is invisible
        // forever. Swept over every ScrollRect in the scene, not a named
        // list, so the next one is caught for free.
        yield return null;
        {
            var bad = new List<string>();
            foreach (var sc in Object.FindObjectsByType<ScrollRect>(FindObjectsSortMode.None))
            {
                if (!sc.gameObject.activeInHierarchy) continue;
                if (sc.viewport == null || sc.content == null) continue;
                var v = new Vector3[4]; sc.viewport.GetWorldCorners(v);
                var c = new Vector3[4]; sc.content.GetWorldCorners(c);
                if (!sc.vertical)
                {
                    float below = v[0].y - c[0].y;
                    float above = c[1].y - v[1].y;
                    float cut = Mathf.Max(below, above);
                    if (cut > 1f)
                        bad.Add(sc.name + " clips " + cut.ToString("0.0")
                              + " units vertically (it does not scroll vertically)");
                }
                if (!sc.horizontal)
                {
                    float left = v[0].x - c[0].x;
                    float right = c[3].x - v[3].x;
                    float cut = Mathf.Max(left, right);
                    if (cut > 1f)
                        bad.Add(sc.name + " clips " + cut.ToString("0.0")
                              + " units horizontally (it does not scroll horizontally)");
                }
            }
            Check(bad.Count == 0, bad.Count == 0
                  ? "no scroller clips its content across a FIXED axis"
                  : "a scroller clips content across a fixed axis: " + string.Join(" | ", bad));
        }


        foreach (var l in log) Debug.Log("[TouchSmoke] " + l);
        Debug.Log(string.Format("[TouchSmoke] RESULT: {0} pass, {1} fail{2}",
                  passed, failed, failed == 0 ? " - ALL GREEN" : " - FIX NEEDED"));
        finished = true;
    }
}
}
