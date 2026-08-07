using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RobotBrawl.Phase0
{
/// <summary>P3c acceptance bench — the TEST DRIVE live-debug loop, driven
/// end to end the way a player runs it: author a program BY HAND on the
/// canvas (real taps + one real drag — the standing §9 P3c rule), SAVE it,
/// take it to TEST DRIVE, arm the autopilot, and verify the machine thinks
/// (ProgramRunner fires the right hat, the drive goes direct, the actuators
/// leave the trigger). In-memory career only (autosave off, owner statics
/// restored); the bay is whatever the auto-boot loaded (owen's build has
/// wheels, which is all the bench program needs).
///
/// Covers: no-program ⇒ no runner and AUTO is a no-op · saved program ⇒
/// runner armed, autopilot OFF by default · AUTO ⇒ controlSource=Program,
/// direct wheel commands, topmost Always hat fires, actuators surrender
/// playerControlled · MANUAL ⇒ keyboard restored, mixer restored · RESET
/// re-arms fresh · by-hand authoring: + HAT, + BLOCK, op cycling, drag
/// reorder, SAVE writes the robot and the status points at TEST DRIVE.
///
/// Run in play mode: TestDebugBench.Run(); read [DebugBench] lines.</summary>
public class TestDebugBench : MonoBehaviour
{
    public static TestDebugBench Run()
    { return new GameObject("test_debug_bench").AddComponent<TestDebugBench>(); }

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

    static RectTransform FindRT(Component root, string name)
    {
        foreach (var rt in root.GetComponentsInChildren<RectTransform>(true))
            if (rt.name == name) return rt;
        return null;
    }
    static Button LastBtnInCard(ProgramCanvas pc, string cardName, string label)
    {
        var card = FindRT(pc, cardName);
        if (card == null) return null;
        Button last = null;
        foreach (var b in card.GetComponentsInChildren<Button>(true))
        {
            var t = b.GetComponentInChildren<Text>();
            if (t != null && t.text == label) last = b;
        }
        return last;
    }

    /// <summary>V2.2: the op chip of a specific body row, by widget name —
    /// labels change as the chip cycles, so find it structurally.</summary>
    static Button OpChipOf(ProgramCanvas pc, string cardName, int row)
    {
        var card = FindRT(pc, cardName);
        if (card == null) return null;
        var r = FindRT(card, "brow_" + row);
        if (r == null) return null;
        foreach (var b in r.GetComponentsInChildren<Button>(true))
            if (b.name == "bop") return b;
        return null;
    }
    static string BtnLabel(Button b)
    {
        var t = b != null ? b.GetComponentInChildren<Text>() : null;
        return t != null ? t.text : "";
    }

    // ProgramBench's validation fixture: core + beams + battery + ALL FIVE
    // sensors + 4 wheels. Everything the debug HUD can show, on one body.
    const string FIXTURE =
        "core|0.000,0.700,0.000|0|0.00,0.00,0.00|Aluminum\n" +
        "beam|0.000,0.700,0.450|0|0.00,0.00,0.00|Aluminum\n" +
        "beam|0.000,0.700,-0.450|0|0.00,0.00,0.00|Aluminum\n" +
        "battery|0.000,0.975,0.000|0|0.00,0.00,0.00|Aluminum\n" +
        "rangefinder|0.000,0.700,0.840|0|0.00,0.00,1.00|Aluminum\n" +
        "compass|0.000,0.880,-0.450|0|0.00,1.00,0.00|Aluminum\n" +
        "tiltsensor|0.000,1.180,0.000|0|0.00,1.00,0.00|Aluminum\n" +
        "wallsensor|0.000,0.700,-0.840|0|0.00,0.00,-1.00|Aluminum\n" +
        "trapsensor|-0.230,0.700,0.000|0|-1.00,0.00,0.00|Aluminum\n" +
        "dmgbus|0.230,0.700,0.000|0|1.00,0.00,0.00|Aluminum\n" +
        "wheel|0.170,0.700,0.450|0|1.00,0.00,0.00|Rubber\n" +
        "wheel|-0.170,0.700,0.450|0|-1.00,0.00,0.00|Rubber\n" +
        "wheel|0.170,0.700,-0.450|0|1.00,0.00,0.00|Rubber\n" +
        "wheel|-0.170,0.700,-0.450|0|-1.00,0.00,0.00|Rubber\n";

    static RobotProgram BenchGo()
    {
        var p = new RobotProgram { title = "BenchGo" };
        var go = new PHat { name = "GO" };
        go.when.Add(PCondTerm.Always());
        go.body.Add(PBlock.MkForever());
        go.body.Add(PBlock.Set(PPart.AllWheels, 0, 100f));
        go.body.Add(PBlock.MkWait(0.2f));
        go.body.Add(PBlock.MkEnd());
        p.hats.Add(go);
        var shadow = new PHat { name = "SHADOW" };   // never fires: GO is topmost
        shadow.when.Add(PCondTerm.Always());
        shadow.body.Add(PBlock.RunS(PPart.AllWheels, 0, -50f, 1f));
        p.hats.Add(shadow);
        return p;
    }

    IEnumerator Start()
    {
        var ms = GameObject.Find("ModeSelect");
        if (ms != null) Destroy(ms);
        var bm = Object.FindFirstObjectByType<BuilderManager>();
        Check(bm != null, "BuilderManager present");
        if (bm == null) { Finish(null, false, false, false); yield break; }
        float tb = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - tb < 3f) yield return null;   // let boot settle

        // in-memory career FIRST, then respawn the UI under it (C6.5 gate)
        var savedData = Career.Data; bool savedActive = Career.active; bool savedAuto = Career.autosave;
        Career.autosave = false;
        Career.Data = new CareerData();
        Career.Data.stable.Add(new CareerRobot { name = "TESTBOT", snapshot = "", program = "" });
        Career.Data.activeRobot = 0;
        Career.active = true;
        MobileBuilderUI.forceMobileUI = true;
        if (MobileBuilderUI.inst != null) { Destroy(MobileBuilderUI.inst.gameObject); yield return null; }
        float t0 = Time.realtimeSinceStartup;
        while (!MobileBuilderUI.Active && Time.realtimeSinceStartup - t0 < 6f) yield return null;
        Check(MobileBuilderUI.Active, "mobile UI attaches under in-memory career");
        yield return null; yield return null;

        // First run found the bay nearly empty (auto-boot raced the bench to
        // a 1-part bay and StartTest correctly refused). The ProgramBench
        // rule applies: a bench LOADS its fixture, it never trusts the bay —
        // and it loads AFTER the career swap, so the owner's pool is never
        // in the accounting path (autosave is off here besides).
        if (bm.mode != BuilderManager.Mode.Build) { bm.BackToBuild(); yield return null; }
        int nfix = bm.LoadSnapshot(FIXTURE);
        yield return null;
        Check(nfix == 14, "fixture loads under the bench career (14 parts, got " + nfix + ")");
        if (nfix == 0) { Finish(savedData, savedActive, savedAuto, true); yield break; }

        // ---- A. no program saved ⇒ TEST DRIVE arms nothing ----------------
        if (bm.mode != BuilderManager.Mode.Build) { bm.BackToBuild(); yield return null; }
        bm.StartTest();
        yield return null; yield return null;
        Check(bm.mode == BuilderManager.Mode.Test,
              "TEST DRIVE starts on the auto-booted bay (msg='" + bm.LastMessage + "')");
        if (bm.mode != BuilderManager.Mode.Test || bm.testRobot == null)
        { Finish(savedData, savedActive, savedAuto, true); yield break; }
        Check(bm.testRunner == null && !bm.testAutopilot, "no saved program ⇒ no runner armed");
        bm.SetTestAutopilot(true);
        Check(!bm.testAutopilot
              && bm.testRobot != null && bm.testRobot.controlSource == ControlSource.Keyboard,
              "AUTO with nothing armed is a no-op (keyboard keeps the bot)");
        bm.BackToBuild(); yield return null;

        // ---- B. saved program ⇒ armed, off by default ---------------------
        Career.Data.stable[0].program = BenchGo().ToJson();
        bm.StartTest();
        yield return null; yield return null;
        if (bm.mode != BuilderManager.Mode.Test || bm.testRobot == null)
        {
            Check(false, "phase B StartTest refused (msg='" + bm.LastMessage + "')");
            Finish(savedData, savedActive, savedAuto, true); yield break;
        }
        Check(bm.testRunner != null && bm.testRunner.program != null
              && bm.testProgramTitle == "BenchGo",
              "saved program arms a runner (title read through)");
        Check(!bm.testAutopilot && bm.testRobot.controlSource == ControlSource.Keyboard,
              "autopilot starts OFF — the toggle is the player's");
        if (bm.testRunner == null)
        { Finish(savedData, savedActive, savedAuto, true); yield break; }

        // ---- C. AUTO: the machine thinks ----------------------------------
        bm.SetTestAutopilot(true);
        Check(bm.testAutopilot && bm.testRobot.controlSource == ControlSource.Program,
              "AUTO hands the bot to the program (single authority)");
        yield return new WaitForSeconds(1.2f);   // ≥5 program ticks
        Check(bm.testRunner.lastFiredHat == 0,
              "topmost Always hat fires (lastFiredHat=" + bm.testRunner.lastFiredHat + ")");
        Check(bm.testDrive != null && bm.testDrive.directWheelCmd,
              "program writes per-wheel commands (directWheelCmd on)");
        bool actsOk = true;
        foreach (var a in bm.testRobot.GetComponentsInChildren<Actuator>(true))
            if (a.playerControlled) actsOk = false;
        Check(actsOk, "actuators surrender the trigger under autopilot");

        // ---- D. MANUAL: clean handback ------------------------------------
        bm.SetTestAutopilot(false);
        Check(!bm.testAutopilot && bm.testRobot.controlSource == ControlSource.Keyboard,
              "MANUAL returns the keyboard");
        Check(bm.testDrive != null && !bm.testDrive.directWheelCmd,
              "mixer restored (directWheelCmd off) — keyboard is not dead");
        bool actsBack = true;
        foreach (var a in bm.testRobot.GetComponentsInChildren<Actuator>(true))
            if (!a.playerControlled) actsBack = false;
        Check(actsBack, "actuators take the trigger back");

        // ---- E. RESET re-arms fresh ---------------------------------------
        bm.SetTestAutopilot(true);
        bm.ResetTest();
        yield return null; yield return null;
        Check(bm.testRunner != null && !bm.testAutopilot
              && bm.testRobot.controlSource == ControlSource.Keyboard,
              "RESET rebuilds the run: armed again, autopilot OFF again");
        bm.BackToBuild(); yield return null; yield return null;

        // ---- F. author a program BY HAND (the standing §9 P3c rule) -------
        Career.Data.stable[0].program = "";
        Tap("PROGRAM"); yield return null;
        var pc = Object.FindFirstObjectByType<ProgramCanvas>();
        if (pc == null) { Tap("PROGRAM"); yield return null; pc = Object.FindFirstObjectByType<ProgramCanvas>(); }
        Check(pc != null, "PROGRAM tab opens for the by-hand pass");
        if (pc != null)
        {
            pc.TestDirtyRefresh();   // drop any stale prog view, start clean
            var p = pc.TestProg;
            p.hats.Clear(); pc.TestDirtyRefresh();
            yield return null; Canvas.ForceUpdateCanvases();
            Tap("+ HAT"); yield return null; Canvas.ForceUpdateCanvases();
            Tap("+ BLOCK"); yield return null; Canvas.ForceUpdateCanvases();
            p = pc.TestProg;
            Check(p.hats.Count == 1 && p.hats[0].body.Count == 2
                  && p.hats[0].body[0].op == POp.Move,
                  "by hand: + HAT then + BLOCK build the stack (default is MOVE)");
            // V2.2: cycle block 2's op chip through the MACRO palette to
            // WAIT, recording the verbs it offers on the way — the palette
            // must speak macros (and, with all sensors mounted, the
            // relative verbs must appear) and never a per-motor op.
            var seen = new List<string>();
            bool sawInternal = false;
            for (int ci = 0; ci < 16; ci++)
            {
                var op = OpChipOf(pc, "hat_0", 1);
                if (op == null) break;
                string lbl = BtnLabel(op);
                if (lbl == "WAIT") break;
                seen.Add(lbl);
                if (lbl == "SET" || lbl == "RUN" || lbl == "FIRE" || lbl == "TURN TOWARD")
                    sawInternal = true;
                op.onClick.Invoke(); yield return null; Canvas.ForceUpdateCanvases();
            }
            p = pc.TestProg;
            Check(p.hats[0].body[1].op == POp.Wait,
                  "by hand: op chip cycles the macro palette to WAIT (saw: " + string.Join(">", seen) + ")");
            Check(seen.Contains("WEAPON") && seen.Contains("MOVE TO") && seen.Contains("SIDE TO"),
                  "palette offers the macro verbs (sensor-gated ones included — all sensors mounted)");
            Check(!sawInternal, "per-motor ops are CUT from the palette (none offered)");
            // one real drag: TANK-TURN R to row 1
            ProgramDragHandle h = null;
            foreach (var x in pc.GetComponentsInChildren<ProgramDragHandle>(true))
                if (x.kind == ProgramDragHandle.BLOCK && x.hat == 0 && x.block == 1) h = x;
            var card = FindRT(pc, "hat_0");
            var row0 = FindRT(card, "brow_0");
            if (h != null && row0 != null)
            {
                var e = new PointerEventData(EventSystem.current);
                e.position = h.transform.position;
                h.OnBeginDrag(e); yield return null;
                var c = new Vector3[4]; row0.GetWorldCorners(c);
                e.position = new Vector2((c[0].x + c[2].x) * 0.5f, c[2].y - 2f);
                h.OnDrag(e); yield return null;
                h.OnEndDrag(e); yield return null; Canvas.ForceUpdateCanvases();
            }
            p = pc.TestProg;
            Check(p.hats[0].body[0].op == POp.Wait && p.hats[0].body[1].op == POp.Move,
                  "by hand: drag reorders the blocks");
            Tap("SAVE"); yield return null;
            var saved = RobotProgram.FromJson(Career.Data.stable[0].program);
            Check(saved != null && saved.hats.Count == 1 && saved.hats[0].body.Count == 2
                  && saved.hats[0].body[0].op == POp.Wait,
                  "by hand: SAVE writes the program to the robot");
            Check(pc.TestStatus.Contains("TEST DRIVE"),
                  "SAVE status points at TEST DRIVE (the payoff hint)");

            // ---- F2. V2.2 step highlight: the sequencer's program counter
            // (lastFiredHat + activeStep) carries into the canvas. Injection
            // seam here; the live path reads bm.testRunner identically.
            var fake = gameObject.AddComponent<ProgramRunner>();
            fake.lastFiredHat = 0; fake.activeStep = 1;
            pc.TestHighlightRunner = fake;
            yield return null; yield return null;
            Check(pc.TestHiHat == 0 && pc.TestHiStep == 1,
                  "step highlight tracks the runner's hat + step on the canvas");
            fake.lastFiredHat = -1; fake.activeStep = -1;
            yield return null; yield return null;
            Check(pc.TestHiHat == -1, "step highlight clears when the runner goes idle");
            pc.TestHighlightRunner = null;
            Destroy(fake);

            // ---- G. ...and watch it think -----------------------------------
            bm.StartTest();
            yield return null; yield return null;
            Check(bm.testRunner != null && bm.testProgramTitle == "TESTBOT",
                  "hand-authored program arms in TEST DRIVE (title falls back to robot name)");
            bm.SetTestAutopilot(true);
            // v2: the hand-authored sequence completes and re-triggers every
            // ~1.7 s, with a sub-tick gap between — POLL rather than sample
            // (a one-shot read lands in the gap ~10% of runs).
            float hw = Time.realtimeSinceStartup; bool fired = false;
            while (Time.realtimeSinceStartup - hw < 2.0f)
            {
                if (bm.testRunner != null && bm.testRunner.lastFiredHat == 0) { fired = true; break; }
                yield return null;
            }
            Check(fired, "hand-authored hat fires under autopilot");
            bm.SetTestAutopilot(false);
            bm.BackToBuild(); yield return null;
        }

        Finish(savedData, savedActive, savedAuto, true);
    }

    void Finish(CareerData savedData, bool savedActive, bool savedAuto, bool restore)
    {
        if (restore)
        { Career.Data = savedData; Career.active = savedActive; Career.autosave = savedAuto; }
        foreach (var l in log) Debug.Log("[DebugBench] " + l);
        Debug.Log(string.Format("[DebugBench] RESULT: {0} pass, {1} fail{2}",
                  passed, failed, failed == 0 ? " - ALL GREEN" : " - FIX NEEDED"));
        finished = true;
    }
}
}
