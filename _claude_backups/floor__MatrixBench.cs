using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace RobotBrawl.Phase0
{
/// <summary>V2.3 — the P5 balance matrix, run on the v2 MACRO presets
/// (design doc §9 P5 / V2 doc §3 V2.3). Two claims, both measured on the
/// REAL autonomy-fight path (StartCareerFight(li,ci,autonomy:true) — the
/// same gates, fees, tiers, armour materials and hazards a player hits):
///
///   FLOOR — the tutorial promise: the Brawler preset on a modest spinner
///   body clears League 1 (≥70% over an L1 sample, N=10). If this fails,
///   the on-ramp is broken: the game hands you a preset that loses.
///
///   SWEEP — the authoring promise: NO preset wins every upper-league
///   cell (L3–L5 sample, each preset). If a preset sweeps, authoring is
///   dead — why program when the built-in wins? — and opponent tuning is
///   wrong (design doc §11 risk table).
///
/// The bench does NOT tune. It measures and reports per-cell W/L/D with
/// damage diags; a failed claim is an owner decision (the C5 precedent:
/// options, not unilateral tunes). In-memory career only (autosave off,
/// owner statics restored); lower leagues pre-marked done so upper
/// contests unlock; medals pre-minted so settles never touch the medal
/// path. Fights run to the FightManager's own verdict at 10× — a bench
/// that caps the clock measures its cap.
///
/// Run in play mode: MatrixBench.Run(); read [MatrixBench] lines.</summary>
public class MatrixBench : MonoBehaviour
{
    public static MatrixBench Run()
    { return new GameObject("matrix_bench").AddComponent<MatrixBench>(); }

    public int passed, failed;
    public bool finished;
    readonly List<string> log = new List<string>();

    void Check(bool ok, string what)
    {
        if (ok) passed++; else failed++;
        log.Add((ok ? "PASS  " : "FAIL  ") + what);
    }
    void Note(string s) { log.Add("      " + s); }

    // AutonomyBench's proven front-spinner body + every sensor the three
    // presets need between them (compass/dmgbus for Brawler+Matador, wall
    // for WallShy, tilt for good measure). ONE body across every cell, so
    // the measured variable is preset × opponent, not chassis.
    const string FIXTURE =
        "core|0.000,0.700,0.000|0|0.00,0.00,0.00|Aluminum\n" +
        "beam|0.000,0.700,0.450|0|0.00,0.00,0.00|Aluminum\n" +
        "beam|0.000,0.700,-0.450|0|0.00,0.00,0.00|Aluminum\n" +
        "battery|0.000,0.975,0.000|0|0.00,0.00,0.00|Aluminum\n" +
        "spindle|0.000,0.800,0.900|0|0.00,0.00,1.00|Aluminum\n" +
        "spinner|0.000,0.800,1.100|0|0.00,0.00,1.00|Steel\n" +
        "wheel|0.170,0.700,0.450|0|1.00,0.00,0.00|Rubber\n" +
        "wheel|-0.170,0.700,0.450|0|-1.00,0.00,0.00|Rubber\n" +
        "wheel|0.170,0.700,-0.450|0|1.00,0.00,0.00|Rubber\n" +
        "wheel|-0.170,0.700,-0.450|0|-1.00,0.00,0.00|Rubber\n" +
        "compass|0.000,0.880,-0.450|0|0.00,1.00,0.00|Aluminum\n" +
        "tiltsensor|0.000,1.180,0.000|0|0.00,1.00,0.00|Aluminum\n" +
        "wallsensor|0.000,0.700,-0.840|0|0.00,0.00,-1.00|Aluminum\n" +
        "dmgbus|0.230,0.700,0.000|0|1.00,0.00,0.00|Aluminum\n";

    struct Cell { public string name; public int li, ci, n; }

    // The ladder sample. L1 floor cells are Brawler-only (the tutorial
    // claim); the upper sample runs for every preset (the sweep claim).
    static readonly Cell[] FLOOR = {
        new Cell { name = "L1C1 scout-R",  li = 0, ci = 0, n = 5 },
        new Cell { name = "L1C2 tipper-R", li = 0, ci = 1, n = 5 },
    };
    static readonly Cell[] UPPER = {
        new Cell { name = "L3C2 mauler-V/Steel",     li = 2, ci = 1, n = 3 },
        new Cell { name = "L4C1 widowmaker-V/Ti",    li = 3, ci = 0, n = 3 },
        new Cell { name = "L4C4 bastion-C/Ti",       li = 3, ci = 3, n = 3 },
        new Cell { name = "L5C1 widowmaker-C/Ti",    li = 4, ci = 0, n = 3 },
    };

    BuilderManager bm;
    int noStarts, srcViolations;

    IEnumerator Start()
    {
        var ms = GameObject.Find("ModeSelect");
        if (ms != null) Destroy(ms);
        bm = Object.FindFirstObjectByType<BuilderManager>();
        Check(bm != null, "BuilderManager present");
        if (bm == null) { Finish(null, false, false, false); yield break; }
        float tb = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - tb < 3f) yield return null;   // boot settle

        var savedData = Career.Data; bool savedActive = Career.active; bool savedAuto = Career.autosave;
        Career.autosave = false;
        Career.fightAutonomous = false;
        Career.Data = new CareerData();
        Career.Data.stable.Add(new CareerRobot { name = "MATRIXBOT", snapshot = "", program = "" });
        Career.Data.activeRobot = 0;
        Career.active = true;
        Career.Data.scrap = 999999;   // fees are not the measurement
        // Own the fixture (the C6.5 no-unowned-parts gate is passed honestly)
        Career.AddItem("beam", "Aluminum", 2);
        Career.AddItem("battery", "Aluminum", 1);
        Career.AddItem("spindle", "Aluminum", 1);
        Career.AddItem("spinner", "Steel", 1);
        Career.AddItem("wheel", "Rubber", 4);
        Career.AddItem("compass", "Aluminum", 1);
        Career.AddItem("tiltsensor", "Aluminum", 1);
        Career.AddItem("wallsensor", "Aluminum", 1);
        Career.AddItem("dmgbus", "Aluminum", 1);
        // Unlock the whole ladder (league N needs league N-1 fully done) and
        // pre-mint every medal so no settle touches the award path.
        foreach (var lg in CareerDB.Leagues)
            foreach (var c in lg.contests)
                Career.Data.doneContests.Add(c.id);
        for (int i = 0; i < CareerDB.Leagues.Length; i++)
            Career.Data.medals.Add(new CareerMedal { leagueIndex = i });

        MobileBuilderUI.forceMobileUI = true;
        if (MobileBuilderUI.inst != null) { Destroy(MobileBuilderUI.inst.gameObject); yield return null; }
        float t0 = Time.realtimeSinceStartup;
        while (!MobileBuilderUI.Active && Time.realtimeSinceStartup - t0 < 6f) yield return null;
        yield return null; yield return null;

        if (bm.mode != BuilderManager.Mode.Build) { bm.BackToBuild(); yield return null; }
        int nfix = bm.LoadSnapshot(FIXTURE);
        yield return null;
        Check(nfix == 14, "fixture loads (14 parts, got " + nfix + ")");
        if (nfix != 14) { Finish(savedData, savedActive, savedAuto, true); yield break; }

        // sanity: every preset validates on this body
        var ids = new List<string>();
        foreach (var p in bm.placed) ids.Add(p.def.id);
        Check(RobotProgram.Brawler().Validate(ids) == null
              && RobotProgram.WallShy().Validate(ids) == null
              && RobotProgram.Matador().Validate(ids) == null,
              "all three presets validate on the matrix body");

        // ---- FLOOR: Brawler vs the L1 sample ------------------------------
        int floorW = 0, floorN = 0;
        foreach (var cell in FLOOR)
        {
            var r = new int[3]; var d = new float[2];
            yield return StartCoroutine(RunCell(RobotProgram.Brawler(), cell, r, d));
            floorW += r[0]; floorN += cell.n;
            Note("FLOOR Brawler " + cell.name + "  W" + r[0] + "/L" + r[1] + "/D" + r[2]
                 + "  dealt~" + d[0].ToString("F0") + " taken~" + d[1].ToString("F0"));
        }
        Check(floorW >= 7, "FLOOR: Brawler clears the L1 sample " + floorW + "/" + floorN + " (need >=7/10)");

        // ---- SWEEP: every preset vs the upper sample ----------------------
        var presets = new System.Func<RobotProgram>[] { RobotProgram.Brawler, RobotProgram.WallShy, RobotProgram.Matador };
        foreach (var mk in presets)
        {
            var prog = mk();
            int cellsWon = 0, wTot = 0, nTot = 0;
            foreach (var cell in UPPER)
            {
                var r = new int[3]; var d = new float[2];
                yield return StartCoroutine(RunCell(prog, cell, r, d));
                bool won = r[0] * 2 > cell.n;   // majority of the cell
                if (won) cellsWon++;
                wTot += r[0]; nTot += cell.n;
                Note("UPPER " + prog.title + " " + cell.name + "  W" + r[0] + "/L" + r[1] + "/D" + r[2]
                     + "  dealt~" + d[0].ToString("F0") + " taken~" + d[1].ToString("F0") + (won ? "  [cell won]" : ""));
            }
            Check(cellsWon < UPPER.Length,
                  "SWEEP: " + prog.title + " does not sweep the upper sample ("
                  + cellsWon + "/" + UPPER.Length + " cells, " + wTot + "/" + nTot + " fights)");
        }

        Check(noStarts == 0, "every scheduled fight started (" + noStarts + " no-starts)");
        Check(srcViolations == 0, "player controlSource==Program held during sampled frames ("
              + srcViolations + " violations)");

        Finish(savedData, savedActive, savedAuto, true);
    }

    /// <summary>One matrix cell: n autonomy fights of `prog` in contest
    /// (li,ci). r = {wins, losses, draws}; d = {mean dealt, mean taken}.</summary>
    IEnumerator RunCell(RobotProgram prog, Cell cell, int[] r, float[] d)
    {
        float sumDealt = 0f, sumTaken = 0f; int measured = 0;
        for (int k = 0; k < cell.n; k++)
        {
            if (bm.mode != BuilderManager.Mode.Build) { bm.BackToBuild(); yield return null; }
            bm.LoadSnapshot(FIXTURE);
            yield return null;
            Career.Data.stable[0].program = prog.ToJson();
            Career.fightAutonomous = false;
            bm.StartCareerFight(cell.li, cell.ci, true);
            yield return null; yield return null;
            var fm = Object.FindFirstObjectByType<FightManager>();
            if (fm == null || bm.mode != BuilderManager.Mode.Fight)
            {
                noStarts++;
                Note("NO-START " + prog.title + " " + cell.name + " ('" + bm.LastMessage + "')");
                continue;
            }
            Time.timeScale = 10f;
            float t0 = Time.realtimeSinceStartup;
            int frame = 0;
            while (fm != null && fm.state != FightManager.State.Ended
                   && Time.realtimeSinceStartup - t0 < 60f)
            {
                // sample the single-authority invariant every ~10th frame
                if (++frame % 10 == 0 && fm.state == FightManager.State.Fighting
                    && fm.player.bot != null && !fm.player.bot.dead
                    && fm.player.bot.controlSource != ControlSource.Program)
                    srcViolations++;
                yield return null;
            }
            if (fm != null && fm.state != FightManager.State.Ended)
                fm.End(FightManager.Outcome.Draw, "matrix wall timeout");
            yield return null;
            if (fm != null)
            {
                if (fm.outcome == FightManager.Outcome.PlayerWin) r[0]++;
                else if (fm.outcome == FightManager.Outcome.PlayerLoss) r[1]++;
                else r[2]++;
                sumDealt += fm.player.dealt; sumTaken += fm.player.taken;
                measured++;
            }
            Time.timeScale = 1f;
            bm.BackToBuild();
            yield return null; yield return null;
        }
        Time.timeScale = 1f;
        if (measured > 0) { d[0] = sumDealt / measured; d[1] = sumTaken / measured; }
    }

    void Finish(CareerData savedData, bool savedActive, bool savedAuto, bool restore)
    {
        Time.timeScale = 1f;
        if (restore)
        {
            Career.Data = savedData; Career.active = savedActive; Career.autosave = savedAuto;
            Career.fightAutonomous = false;
        }
        var sb = new StringBuilder();
        foreach (var l in log) { Debug.Log("[MatrixBench] " + l); sb.Append(l).Append('\n'); }
        Debug.Log(string.Format("[MatrixBench] RESULT: {0} pass, {1} fail{2}",
                  passed, failed, failed == 0 ? " - ALL GREEN" : " - OWNER DECISION NEEDED"));
        try { System.IO.File.WriteAllText(
                  Application.dataPath + "/Phase1/qa_matrix_bench.txt", sb.ToString()); }
        catch { }
        finished = true;
    }
}
}
