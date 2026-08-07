using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RobotBrawl.Phase0
{
/// <summary>P4 acceptance bench — AUTONOMY FIGHTS: the same career ladder,
/// fought with the autopilot driving. Verifies the whole contract owen set
/// on 2026-08-06: the gate stacks on the manual gate and refuses BEFORE any
/// fee (no program → amber naming the PROGRAM tab; no sensor → amber naming
/// the SHOP), a passing gate starts the SAME contest with
/// playerSource=Program (keyboard dead by the P0 single authority, asserted
/// per frame through the bell), the ENEMY stays roster AI (not a program),
/// a win settles the normal purse AND stamps the autonomy mark
/// (autoDoneContests, re-entry included), the result line says
/// "autonomous", the flag never leaks into the next manual fight, and the
/// board rows carry ⚙ + the split MANUAL FIGHT / AUTONOMY FIGHT buttons.
///
/// In-memory career only (autosave off, owner statics restored); fights end
/// by harness verdict (fm.End — the CareerSmoke pattern), so no clock is
/// capped and no balance is implied. Run in play mode: AutonomyBench.Run();
/// read [AutonomyBench] console lines.</summary>
public class AutonomyBench : MonoBehaviour
{
    public static AutonomyBench Run()
    { return new GameObject("autonomy_bench").AddComponent<AutonomyBench>(); }

    public int passed, failed;
    public bool finished;
    readonly List<string> log = new List<string>();

    void Check(bool ok, string what)
    {
        if (ok) passed++; else failed++;
        log.Add((ok ? "PASS  " : "FAIL  ") + what);
    }

    // ProgramBench's proven front-spinner geometry, sensors optional:
    const string BODY =
        "core|0.000,0.700,0.000|0|0.00,0.00,0.00|Aluminum\n" +
        "beam|0.000,0.700,0.450|0|0.00,0.00,0.00|Aluminum\n" +
        "beam|0.000,0.700,-0.450|0|0.00,0.00,0.00|Aluminum\n" +
        "battery|0.000,0.975,0.000|0|0.00,0.00,0.00|Aluminum\n" +
        "spindle|0.000,0.800,0.900|0|0.00,0.00,1.00|Aluminum\n" +
        "spinner|0.000,0.800,1.100|0|0.00,0.00,1.00|Steel\n" +
        "wheel|0.170,0.700,0.450|0|1.00,0.00,0.00|Rubber\n" +
        "wheel|-0.170,0.700,0.450|0|-1.00,0.00,0.00|Rubber\n" +
        "wheel|0.170,0.700,-0.450|0|1.00,0.00,0.00|Rubber\n" +
        "wheel|-0.170,0.700,-0.450|0|-1.00,0.00,0.00|Rubber\n";
    const string SENSORS =
        "compass|0.000,0.880,-0.450|0|0.00,1.00,0.00|Aluminum\n" +
        "tiltsensor|0.000,1.180,0.000|0|0.00,1.00,0.00|Aluminum\n";

    static RectTransform FindRT(Component root, string name)
    {
        foreach (var rt in root.GetComponentsInChildren<RectTransform>(true))
            if (rt.name == name) return rt;
        return null;
    }
    /// <summary>MobileBuilderUI's CANVAS is its own scene root — children of
    /// the component's GameObject are NOT the UI tree (measured: 0 rects
    /// under inst). Board lookups must be scene-wide.</summary>
    static RectTransform FindRTGlobal(string name)
    {
        foreach (var rt in Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (rt.name == name) return rt;
        return null;
    }
    static bool TapContaining(string fragment)
    {
        foreach (var b in Object.FindObjectsByType<Button>(FindObjectsSortMode.None))
        {
            var t = b.GetComponentInChildren<Text>();
            if (t != null && t.text.Contains(fragment)) { b.onClick.Invoke(); return true; }
        }
        return false;
    }

    IEnumerator Start()
    {
        var ms = GameObject.Find("ModeSelect");
        if (ms != null) Destroy(ms);
        var bm = Object.FindFirstObjectByType<BuilderManager>();
        Check(bm != null, "BuilderManager present");
        if (bm == null) { Finish(null, false, false, false); yield break; }
        float tb = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - tb < 3f) yield return null;   // boot settle

        var savedData = Career.Data; bool savedActive = Career.active; bool savedAuto = Career.autosave;
        Career.autosave = false;
        Career.fightAutonomous = false;
        Career.Data = new CareerData();
        Career.Data.stable.Add(new CareerRobot { name = "TESTBOT", snapshot = "", program = "" });
        Career.Data.activeRobot = 0;
        Career.active = true;
        Career.Txn(500, "bench grant");
        // Own every fixture part — CareerFightBlocker audits the bay against
        // the inventory (the C6.5 no-unowned-parts rule) and the bench must
        // pass THAT gate honestly to reach the autonomy gate behind it.
        Career.AddItem("beam", "Aluminum", 2);
        Career.AddItem("battery", "Aluminum", 1);
        Career.AddItem("spindle", "Aluminum", 1);
        Career.AddItem("spinner", "Steel", 1);
        Career.AddItem("wheel", "Rubber", 4);
        Career.AddItem("compass", "Aluminum", 1);
        Career.AddItem("tiltsensor", "Aluminum", 1);
        MobileBuilderUI.forceMobileUI = true;
        if (MobileBuilderUI.inst != null) { Destroy(MobileBuilderUI.inst.gameObject); yield return null; }
        float t0 = Time.realtimeSinceStartup;
        while (!MobileBuilderUI.Active && Time.realtimeSinceStartup - t0 < 6f) yield return null;
        Check(MobileBuilderUI.Active, "mobile UI attaches under in-memory career");
        yield return null; yield return null;

        // ---- A. gate: no saved program --------------------------------------
        if (bm.mode != BuilderManager.Mode.Build) { bm.BackToBuild(); yield return null; }
        int n1 = bm.LoadSnapshot(BODY + SENSORS);
        yield return null;
        Check(n1 == 12, "fixture loads (12 parts incl. weapon + 2 sensors, got " + n1 + ")");
        int scrap0 = Career.Data.scrap;
        bm.StartCareerFight(0, 0, true);
        yield return null;
        Check(bm.mode == BuilderManager.Mode.Build
              && bm.LastMessage != null && bm.LastMessage.Contains("PROGRAM"),
              "no saved program → refused, amber names the PROGRAM tab ('" + bm.LastMessage + "')");
        Check(Career.Data.scrap == scrap0 && !Career.fightAutonomous,
              "refusal costs nothing and arms nothing");

        // ---- B. gate: program but no sensor ---------------------------------
        Career.Data.stable[0].program = RobotProgram.Brawler().ToJson();
        int n2 = bm.LoadSnapshot(BODY);
        yield return null;
        Check(n2 == 10, "sensor-less body loads (10 parts)");
        bm.StartCareerFight(0, 0, true);
        yield return null;
        Check(bm.mode == BuilderManager.Mode.Build
              && bm.LastMessage != null && bm.LastMessage.Contains("sensor"),
              "no sensor → refused, amber names the SHOP ('" + bm.LastMessage + "')");

        // ---- C. the autonomy fight ------------------------------------------
        bm.LoadSnapshot(BODY + SENSORS);
        yield return null;
        int sBefore = Career.Data.scrap;
        bm.StartCareerFight(0, 0, true);
        yield return null; yield return null;
        var fm = Object.FindFirstObjectByType<FightManager>();
        Check(fm != null && Career.activeContest == "L1C1" && bm.mode == BuilderManager.Mode.Fight,
              "autonomy gate passes → the SAME L1C1 fight starts");
        if (fm == null) { Finish(savedData, savedActive, savedAuto, true); yield break; }
        Check(fm.playerSource == ControlSource.Program && Career.fightAutonomous,
              "playerSource=Program at the bell contract, flag armed");
        var runner = bm.testRobot != null ? bm.testRobot.GetComponent<ProgramRunner>() : null;
        Check(runner != null && runner.program != null && runner.program.title == "Brawler",
              "ProgramRunner carries the robot's SAVED program");

        // through the bell: the player must be Program EVERY frame, the
        // enemy must be AI every frame (the standing controlSource invariant)
        yield return new WaitForSeconds(2.0f);   // settle + bell
        int violations = 0; bool enemyAI = true; bool sawEnemy = false;
        float tf = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - tf < 1.5f)
        {
            if (fm.player.bot != null && fm.player.bot.controlSource != ControlSource.Program) violations++;
            if (fm.enemy.bot != null)
            {
                sawEnemy = true;
                if (fm.enemy.bot.controlSource != ControlSource.AI) enemyAI = false;
            }
            yield return null;
        }
        Check(violations == 0, "keyboard verifiably dead: player controlSource==Program every frame ("
              + violations + " violations)");
        Check(sawEnemy && enemyAI, "the enemy stays roster AI — opponents are not programs");
        Check(bm.testDrive != null && bm.testDrive.directWheelCmd,
              "the program is actually driving (direct wheel commands)");

        // ---- D. settlement: purse + the autonomy mark -----------------------
        float dealt = fm.player.dealt;
        fm.End(FightManager.Outcome.PlayerWin, "autonomy harness win");
        yield return null; yield return null;
        Check(Career.Data.scrap > sBefore, "win settles a normal purse (+"
              + (Career.Data.scrap - sBefore) + ")");
        Check(Career.Data.doneContests.Contains("L1C1")
              && Career.Data.autoDoneContests.Contains("L1C1"),
              "win stamps BOTH the first-win flag and the autonomy mark");
        Check(Career.lastResultLine != null && Career.lastResultLine.Contains("autonomous"),
              "result line says autonomous ('" + Career.lastResultLine + "')");
        Check(!Career.fightAutonomous, "flag cleared at settlement — never carries over");
        bm.BackToBuild(); yield return null; yield return null; yield return null;

        // ---- E. manual fight untouched by all of the above ------------------
        bm.StartCareerFight(0, 1);
        yield return null; yield return null;
        fm = Object.FindFirstObjectByType<FightManager>();
        Check(fm != null && fm.playerSource == ControlSource.Keyboard && !Career.fightAutonomous,
              "manual fight still starts under the keyboard, flag stays down");
        if (fm != null) { fm.End(FightManager.Outcome.PlayerWin, "manual harness win"); yield return null; yield return null; }
        Check(Career.Data.doneContests.Contains("L1C2")
              && !Career.Data.autoDoneContests.Contains("L1C2"),
              "a manual win does NOT stamp the autonomy mark");
        bm.BackToBuild(); yield return null; yield return null; yield return null;

        // ---- F. the board: split buttons + the ⚙ mark -----------------------
        var ui = MobileBuilderUI.inst;
        if (ui != null) { ui.SetDockOpen(true); ui.ShowTab(1); }
        yield return null; Canvas.ForceUpdateCanvases(); yield return null;
        var row = FindRTGlobal("contest_L1C1");
        if (row == null)
        {
            // the board is an accordion — contest rows only exist under an
            // OPEN league header; open league 1 the way a finger would
            TapContaining(CareerDB.Leagues[0].name);
            yield return null; Canvas.ForceUpdateCanvases(); yield return null;
            row = FindRTGlobal("contest_L1C1");
        }
        Check(row != null, "L1C1 contest row present on the board");
        if (row != null)
        {
            var lbl = row.GetComponentInChildren<Text>();
            Check(lbl != null && lbl.text.Contains("[AUTO]"), "row carries the [AUTO] autonomy mark");
            bool manualBtn = FindRT(row, "cfight_L1C1") != null;
            bool autoBtn = FindRT(row, "cauto_L1C1") != null;
            Check(manualBtn && autoBtn, "row offers MANUAL FIGHT and AUTONOMY FIGHT");
        }
        var row2 = FindRTGlobal("contest_L1C2");
        if (row2 != null)
        {
            var lbl2 = row2.GetComponentInChildren<Text>();
            Check(lbl2 != null && !lbl2.text.Contains("[AUTO]"),
                  "manually-won contest shows NO autonomy mark");
        }

        Finish(savedData, savedActive, savedAuto, true);
    }

    void Finish(CareerData savedData, bool savedActive, bool savedAuto, bool restore)
    {
        if (restore)
        { Career.Data = savedData; Career.active = savedActive; Career.autosave = savedAuto; Career.fightAutonomous = false; }
        foreach (var l in log) Debug.Log("[AutonomyBench] " + l);
        Debug.Log(string.Format("[AutonomyBench] RESULT: {0} pass, {1} fail{2}",
                  passed, failed, failed == 0 ? " - ALL GREEN" : " - FIX NEEDED"));
        finished = true;
    }
}
}
