// ⚠ EDITOR AND DEVELOPMENT BUILDS ONLY — the ArenaShots/VerbBench guard.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RobotBrawl.Phase0
{
/// <summary>PROGRAM PLAYTEST — a player agent for the PROGRAM tab (owen,
/// 2026-08-17: "I haven't fully tested the program tab yet. I always used
/// starter kit... test different program components and make sure all
/// variations of programs work as expected").
///
/// The layering, and why this bench exists when ProgramBench/VerbBench are
/// both green: those install programs IN CODE. Nothing has ever AUTHORED a
/// program the way a finger does — preset picker, op-chip cycling, direction/
/// target/duration chips, + IF / + REPEAT palette, SAVE / LOAD TO ROBOT —
/// and the house rule says a UI path only a human can drive is a UI path
/// nothing checks. So every gesture here is a real onClick on the named chip
/// (the WATCH-button lesson), asserted twice:
///   1. UI CONTRACT — after each tap, TestProg changed exactly as the chip's
///      label promised (the canvas REBUILDS per tap, so chips are re-found
///      by name each time).
///   2. PHYSICS — the program that "LOAD TO ROBOT" actually saved on the
///      robot (the artifact a real fight arms) drives a spawned rig the way
///      the labels promised, world-space, VerbBench style.
///
/// Career state is scratch; autosave held; owner data restored in finally.
/// Run in play mode: ProgramPlaytest.Run(); read [ProgTest] console lines.</summary>
public class ProgramPlaytest : MonoBehaviour
{
    public static ProgramPlaytest Run()
    { return new GameObject("program_playtest").AddComponent<ProgramPlaytest>(); }

    public int passed, failed;
    public bool finished;
    readonly List<string> log = new List<string>();
    void Pass(string w) { passed++; log.Add("PASS  " + w); Debug.Log("[ProgTest] PASS  " + w); }
    void Fail(string w) { failed++; log.Add("FAIL  " + w); Debug.Log("[ProgTest] FAIL  " + w); }
    void Check(bool ok, string w) { if (ok) Pass(w); else Fail(w); }
    static string F(float v) { return v.ToString("0.00"); }

    // wallsensor + compass + 4 wheels — VerbBench's drive body (no weapon).
    const string BODY =
        "core|0.000,0.700,0.000|0|0.00,0.00,0.00|Aluminum\n" +
        "beam|0.000,0.700,0.450|0|0.00,0.00,0.00|Aluminum\n" +
        "beam|0.000,0.700,-0.450|0|0.00,0.00,0.00|Aluminum\n" +
        "battery|0.000,0.975,0.000|0|0.00,0.00,0.00|Aluminum\n" +
        "wallsensor|0.000,0.700,0.840|0|0.00,0.00,1.00|Aluminum\n" +
        "compass|0.000,0.880,-0.450|0|0.00,1.00,0.00|Aluminum\n" +
        "wheel|0.170,0.700,0.450|0|1.00,0.00,0.00|Rubber\n" +
        "wheel|-0.170,0.700,0.450|0|-1.00,0.00,0.00|Rubber\n" +
        "wheel|0.170,0.700,-0.450|0|1.00,0.00,0.00|Rubber\n" +
        "wheel|-0.170,0.700,-0.450|0|-1.00,0.00,0.00|Rubber\n";

    // No program sensors at all — the op cycle must SKIP MoveRel/FaceSide.
    const string SENSORLESS =
        "core|0.000,0.700,0.000|0|0.00,0.00,0.00|Aluminum\n" +
        "beam|0.000,0.700,0.450|0|0.00,0.00,0.00|Aluminum\n" +
        "beam|0.000,0.700,-0.450|0|0.00,0.00,0.00|Aluminum\n" +
        "battery|0.000,0.975,0.000|0|0.00,0.00,0.00|Aluminum\n" +
        "wheel|0.170,0.700,0.450|0|1.00,0.00,0.00|Rubber\n" +
        "wheel|-0.170,0.700,0.450|0|-1.00,0.00,0.00|Rubber\n" +
        "wheel|0.170,0.700,-0.450|0|1.00,0.00,0.00|Rubber\n" +
        "wheel|-0.170,0.700,-0.450|0|-1.00,0.00,0.00|Rubber\n";

    // ---- real-UI finders ---------------------------------------------------
    /// <summary>The n-th ACTIVE chip named `n`, in HIERARCHY order rooted at
    /// the canvas. ⚠ FindObjectsByType returns ARBITRARY order — the first
    /// run tapped a random hat's + BLOCK and cycled a random block's op chip
    /// while reading a different one. GetComponentsInChildren is depth-first
    /// document order, so [count-1] really is the bottom-most chip on screen.
    /// The canvas rebuilds on every MarkDirty — never cache, re-find per tap.</summary>
    Button Chip(string n, int nth = 0)
    {
        if (pc == null) return null;
        int seen = 0;
        foreach (var b in pc.GetComponentsInChildren<Button>(false))
            if (b.gameObject.name == n && b.isActiveAndEnabled)
            { if (seen == nth) return b; seen++; }
        return null;
    }
    int ChipCount(string n)
    {
        if (pc == null) return 0;
        int seen = 0;
        foreach (var b in pc.GetComponentsInChildren<Button>(false))
            if (b.gameObject.name == n && b.isActiveAndEnabled) seen++;
        return seen;
    }
    static string ChipLabel(Button b)
    { var t = b != null ? b.GetComponentInChildren<Text>() : null; return t != null ? t.text : ""; }
    bool Tap(string n, int nth = 0)
    { var b = Chip(n, nth); if (b == null) { Fail("chip '" + n + "'[" + nth + "] not found"); return false; }
      b.onClick.Invoke(); return true; }

    BuilderManager bm;
    MobileBuilderUI ui;
    ProgramCanvas pc;
    string lastSavedProg = "";
    CompoundRobot sr; SensorBus bus; RaycastWheelDrive dr; ProgramRunner runner;

    void Start() { StartCoroutine(All()); }

    IEnumerator ShowProgramTab()
    {
        // The real tab button — dock tab strip buttons carry their label text.
        Button tab = null;
        foreach (var b in Object.FindObjectsByType<Button>(FindObjectsSortMode.None))
        { var t = b.GetComponentInChildren<Text>(); if (t != null && t.text == "PROGRAM") { tab = b; break; } }
        if (tab == null) { Fail("no PROGRAM tab button in the dock"); yield break; }
        tab.onClick.Invoke();
        yield return null; yield return null;
        pc = Object.FindFirstObjectByType<ProgramCanvas>();
    }

    IEnumerator SwitchBuild(string snapshot)
    {
        Career.Data.stable[0].snapshot = BuilderManager.SNAP_STAMP + "\n" + snapshot;
        bm.LoadSnapshot(Career.Data.stable[0].snapshot);
        yield return null;
        if (pc != null) pc.TestDirtyRefresh();
        yield return null;
    }

    // ---- rig physics (the VerbBench pattern) -------------------------------
    IEnumerator SpawnRig()
    {
        Disarm();
        bm.BackToBuild(); yield return null;
        bm.StartTest(); yield return null; yield return null;
        sr = bm.testRobot;
        bus = sr != null ? sr.GetComponent<SensorBus>() : null;
        dr = sr != null ? sr.GetComponent<RaycastWheelDrive>() : null;
        yield return new WaitForSeconds(0.6f);
    }
    IEnumerator AimWallAt(float want)
    {
        var rb = sr != null ? sr.GetComponent<Rigidbody>() : null;
        for (int i = 0; i < 3; i++)
        {
            if (bus == null || !bus.wallValid) yield break;
            sr.transform.Rotate(0f, -Mathf.DeltaAngle(want, bus.wallBearingDeg), 0f);
            if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            yield return new WaitForSeconds(0.25f);
        }
    }
    void ArmSaved()
    {
        // THE ARTIFACT UNDER TEST: the program "LOAD TO ROBOT" wrote onto the
        // career robot — the same JSON a real autonomy fight would arm.
        Disarm();
        var p = RobotProgram.FromJson(Career.Data.stable[0].program);
        if (p == null) { Fail("robot carries no loadable program after LOAD TO ROBOT"); return; }
        runner = sr.gameObject.AddComponent<ProgramRunner>();
        runner.Init(sr, bm.testDrive);
        runner.program = p;
        sr.controlSource = ControlSource.Program;
    }
    void Disarm() { if (runner != null) { DestroyImmediate(runner); runner = null; } }
    float MaxCmd()
    {
        if (dr == null) return 0f;
        float m = 0f;
        for (int c = 0; c < dr.ChannelCount; c++) m = Mathf.Max(m, Mathf.Abs(dr.GetWheelCmd(c)));
        return m;
    }

    // ---- authoring helpers -------------------------------------------------
    PBlock LastBlock()
    {
        var p = pc.TestProg;
        for (int h = p.hats.Count - 1; h >= 0; h--)
            if (p.hats[h].body.Count > 0) return p.hats[h].body[p.hats[h].body.Count - 1];
        return null;
    }
    /// <summary>Fresh single-block program: wipe to one Always hat + one block
    /// via the real chips (+ HAT then + BLOCK on an empty canvas), then cycle
    /// the op chip until the block is `want`. Returns false if `want` never
    /// came around (e.g. deliberately, on a sensorless build).</summary>
    IEnumerator AuthorSingle(POp want, System.Action<bool> done)
    {
        // wipe: delete every hat through its ✕ (the delete chips arm SURE? on
        // first tap in some flows; hat del is immediate)
        int guard = 0;
        while (pc.TestProg.hats.Count > 0 && guard++ < 20)
        { if (!Tap("del")) break; yield return null; }
        Check(pc.TestProg.hats.Count == 0, "canvas wiped through the hat ✕ chips");
        if (!Tap("addhat")) { done(false); yield break; }
        yield return null;
        // ⚠ + HAT SEEDS A RUNNABLE MOVE BLOCK (InsertHatAt: "runnable the
        // moment it lands"). Adding another gave every 'single-block' program
        // a stray 1-second MOVE prologue — the first run's AWAY and STOP ALL
        // 'failures' were this harness authoring a different program than it
        // thought. Cycle the seeded block; only add one if the hat is empty.
        if (pc.TestProg.hats[pc.TestProg.hats.Count - 1].body.Count == 0)
        { if (!Tap("addb")) { done(false); yield break; } yield return null; }
        bool hit = false;
        for (int i = 0; i < 12 && !hit; i++)
        {
            var b = LastBlock();
            if (b != null && b.op == want) { hit = true; break; }
            int nb = ChipCount("bop");
            if (!Tap("bop", nb - 1)) break;
            yield return null;
        }
        done(hit);
    }

    IEnumerator All()
    {
        var hold = Career.SuspendAutosave();
        var savedData = Career.Data; bool savedActive = Career.active;
        bool savedFree = Career.devFreeBuild;
        MobileBuilderUI.forceMobileUI = true;
        try
        {
            var ms = GameObject.Find("ModeSelect"); if (ms != null) Destroy(ms);
            bm = Object.FindFirstObjectByType<BuilderManager>();
            if (bm == null) bm = new GameObject("BuilderManager").AddComponent<BuilderManager>();

            Career.active = true;
            Career.devFreeBuild = true;
            Career.Data = new CareerData();
            Career.GrantStarterKit();
            Career.Data.stable.Add(new CareerRobot { name = "ProgRig",
                snapshot = BuilderManager.SNAP_STAMP + "\n" + BODY, program = "" });
            Career.Data.activeRobot = 0;

            if (MobileBuilderUI.inst != null) { Destroy(MobileBuilderUI.inst.gameObject); yield return null; }
            float t0 = Time.realtimeSinceStartup;
            while (!MobileBuilderUI.Active && Time.realtimeSinceStartup - t0 < 6f) yield return null;
            ui = MobileBuilderUI.inst;
            if (ui == null) { Fail("dock never attached"); yield break; }
            yield return null;
            if (!ui.DockOpen) ui.SetDockOpen(true);
            yield return null;
            bm.LoadSnapshot(Career.Data.stable[0].snapshot);
            yield return null;

            // ---- J1: the PROGRAM tab opens and the canvas is alive ---------
            yield return ShowProgramTab();
            Check(ui.TestVisibleTab() == 5, "PROGRAM tab renders (visible tab == 5)");
            Check(pc != null && pc.TestProg != null, "ProgramCanvas is alive with a program bound");
            if (pc == null) yield break;

            // ---- J2: both presets install through the real picker ----------
            if (Tap("prog_dropdown")) yield return null;
            Check(Chip("open_first") != null && Chip("open_cancel") != null,
                  "the switch list opens with FIRST STEPS and CANCEL");
            if (Tap("open_first")) yield return null;
            var fs = RobotProgram.FirstSteps();
            Check(pc.TestProg.title == fs.title && pc.TestProg.hats.Count == fs.hats.Count,
                  "FIRST STEPS installs via the picker (" + pc.TestProg.hats.Count + " hat(s))");
            if (Tap("prog_dropdown")) yield return null;
            if (Tap("open_preset")) yield return null;
            var sk = RobotProgram.StarterKit();
            Check(pc.TestProg.title == sk.title && pc.TestProg.hats.Count == sk.hats.Count,
                  "STARTER KIT installs via the picker (" + pc.TestProg.hats.Count + " hat(s))");

            // ---- J3: + HAT / + BLOCK and the full op cycle by chip taps ----
            int hats0 = pc.TestProg.hats.Count;
            if (Tap("addhat")) yield return null;
            Check(pc.TestProg.hats.Count == hats0 + 1, "+ HAT adds a hat (" + pc.TestProg.hats.Count + ")");
            int lastHat = pc.TestProg.hats.Count - 1;
            int blocks0 = pc.TestProg.hats[lastHat].body.Count;
            int nAdd = ChipCount("addb");
            if (Tap("addb", nAdd - 1)) yield return null;
            Check(pc.TestProg.hats[lastHat].body.Count == blocks0 + 1,
                  "+ BLOCK adds a block to the new hat");

            // cycle the newest block's op chip through a full revolution and
            // record the order — with compass+wallsensor aboard, EVERY player
            // op must appear, each landing with runnable defaults.
            var seen = new List<POp>();
            for (int i = 0; i < 9; i++)
            {
                int nb = ChipCount("bop");
                if (!Tap("bop", nb - 1)) break;
                yield return null;
                var b = LastBlock();
                if (b == null) break;
                if (seen.Contains(b.op)) break;
                seen.Add(b.op);
                if (b.op == POp.Move) Check(Mathf.Abs(b.arg) > 0f && b.dur > 0f,
                    "MOVE lands runnable (arg " + b.arg + ", dur " + b.dur + ")");
                if (b.op == POp.MoveRel) Check(b.dur > 0f,
                    "MOVE toward/away lands runnable (target " + RobotProgram.TargetLabel(b.target) + ")");
            }
            Check(seen.Contains(POp.Move) && seen.Contains(POp.TurnLR) && seen.Contains(POp.TurnBy)
                  && seen.Contains(POp.Weapon) && seen.Contains(POp.MoveRel) && seen.Contains(POp.FaceSide)
                  && seen.Contains(POp.Wait) && seen.Contains(POp.StopAll),
                  "op chip cycles through all 8 player verbs on a sensored build (" + seen.Count + " seen)");

            // ---- J4: the duration chip walks HOLD -> seconds -> rounds -----
            bool ok = false;
            yield return AuthorSingle(POp.Move, v => ok = v);
            Check(ok, "authored a fresh MOVE block through the chips");
            if (ok)
            {
                var b = LastBlock();
                bool sawRounds = false, backToHold = false;
                var walk = new System.Text.StringBuilder(b.dur + "/" + b.rounds);
                for (int i = 0; i < 14; i++)
                {
                    int nd = ChipCount("bdur");
                    if (!Tap("bdur", nd - 1)) break;
                    yield return null; yield return null;   // let the rebuild land
                    b = LastBlock();
                    walk.Append(" -> " + b.dur + "/" + b.rounds);
                    if (b.rounds > 0) sawRounds = true;
                    if (b.rounds == 0 && b.dur <= 0f) { backToHold = true; break; }
                }
                Check(sawRounds, "duration chip reaches the ROUNDS flavor (walk: " + walk + ")");
                Check(backToHold, "…and wraps back to HOLD (the SET flavor)");
            }

            // ---- J5: structure chips arrive balanced; sensorless skip ------
            int cnt0 = 0; foreach (var h in pc.TestProg.hats) cnt0 += h.body.Count;
            int ni = ChipCount("addif");
            if (ni > 0 && Tap("addif", ni - 1)) yield return null;
            var lastBody = pc.TestProg.hats[pc.TestProg.hats.Count - 1].body;
            bool ifBalanced = false, endsAfterIf = false;
            for (int i = 0; i < lastBody.Count; i++)
                if (lastBody[i].op == POp.If)
                { for (int j = i + 1; j < lastBody.Count; j++) if (lastBody[j].op == POp.EndIf) { ifBalanced = true; endsAfterIf = j > i; } }
            Check(ifBalanced && endsAfterIf, "+ IF inserts a BALANCED IF … END IF span");
            int nr = ChipCount("addrep");
            if (nr > 0 && Tap("addrep", nr - 1)) yield return null;
            lastBody = pc.TestProg.hats[pc.TestProg.hats.Count - 1].body;
            bool repBalanced = false; PBlock repBlock = null;
            for (int i = 0; i < lastBody.Count; i++)
                if (lastBody[i].op == POp.Repeat)
                { repBlock = lastBody[i];
                  for (int j = i + 1; j < lastBody.Count; j++) if (lastBody[j].op == POp.End) repBalanced = true; }
            Check(repBalanced, "+ REPEAT inserts a BALANCED REPEAT … END span");
            if (repBlock != null)
            {
                // the one legal structural swap: REPEAT <-> FOREVER
                int nbop = ChipCount("bop");
                for (int k = 0; k < nbop; k++)
                { var c = Chip("bop", k); if (ChipLabel(c).Contains("REPEAT")) { c.onClick.Invoke(); break; } }
                yield return null;
                bool nowForever = false;
                foreach (var h in pc.TestProg.hats) foreach (var b2 in h.body) if (b2.op == POp.Forever) nowForever = true;
                Check(nowForever, "tapping the REPEAT chip swaps it to FOREVER (the one legal structure cycle)");
            }

            // sensorless build: the op cycle must SKIP the relative verbs.
            yield return SwitchBuild(SENSORLESS);
            bool okS = false;
            yield return AuthorSingle(POp.MoveRel, v => okS = v);
            Check(!okS, "on a build with NO sensors the op cycle SKIPS MOVE toward/away");
            bool okF = false;
            yield return AuthorSingle(POp.FaceSide, v => okF = v);
            Check(!okF, "…and SKIPS FACE side too");
            yield return SwitchBuild(BODY);

            // ---- J6: SAVE to library + reopen picker shows the row ---------
            bool okM = false;
            yield return AuthorSingle(POp.Move, v => okM = v);
            if (okM && Tap("prog_save")) yield return null;
            Check(pc.TestStatus.Contains("saved") || pc.TestStatus.Contains("overwrote"),
                  "SAVE writes to the library (status: '" + pc.TestStatus + "')");
            if (Tap("prog_dropdown")) yield return null;
            // presets are open_first/open_preset; library rows are open_0, open_1…
            bool libRow = Chip("open_0") != null;
            Check(libRow, "the switch list now carries the saved library row");
            if (Tap("open_cancel")) yield return null;

            // ---- J7: PHYSICS — LOAD TO ROBOT then the labels' promises -----
            // Variation A: MOVE forward closes on a wall dead ahead.
            bool okA = false;
            yield return AuthorSingle(POp.Move, v => okA = v);
            if (okA && Tap("prog_load")) yield return null;
            Check(RobotProgram.FromJson(Career.Data.stable[0].program) != null,
                  "LOAD TO ROBOT writes the authored program onto the robot");
            lastSavedProg = Career.Data.stable[0].program;
            yield return SpawnRig();
            if (sr == null || bus == null) { Fail("test rig failed to spawn"); yield break; }
            yield return AimWallAt(0f);
            float d0 = bus.wallDist;
            ArmSaved();
            yield return new WaitForSeconds(1.5f);
            float d1 = bus.wallDist;
            Check(d1 < d0 - 0.2f, "UI-authored MOVE drives forward (" + F(d0) + " -> " + F(d1) + ")");
            Disarm(); bm.BackToBuild(); yield return null;
            yield return ShowProgramTab();

            // Variation B: TURN BY ~90° actually turns the body — then WAIT.
            // ⚠ A lone [TURN BY 90] under an Always hat RE-RUNS when the
            // sequence completes (that is the hat contract, not a bug), so a
            // fixed-time snapshot reads 90×N + partial — three runs measured
            // 63°, 131°, 35° before this was understood. Author the two-step
            // sequence a player would: TURN BY 90, then WAIT 5s to park.
            bool okB = false;
            yield return AuthorSingle(POp.TurnBy, v => okB = v);
            if (okB)
            {
                if (Tap("addb")) yield return null;              // seeded MOVE lands last
                for (int i = 0; i < 9; i++)                       // cycle it to WAIT
                {
                    var lb2 = LastBlock();
                    if (lb2 != null && lb2.op == POp.Wait) break;
                    int nb2 = ChipCount("bop");
                    if (!Tap("bop", nb2 - 1)) break;
                    yield return null;
                }
                Check(LastBlock() != null && LastBlock().op == POp.Wait,
                      "authored the two-step TURN BY 90 → WAIT sequence");
                for (int i = 0; i < 5; i++)                       // WAIT 0.5s -> 5s
                { int nd2 = ChipCount("bdur"); if (!Tap("bdur", nd2 - 1)) break; yield return null; }
            }
            if (okB && Tap("prog_load")) yield return null;
            lastSavedProg = Career.Data.stable[0].program;
            yield return SpawnRig();
            yield return AimWallAt(0f);
            // accumulate signed yaw so a wrap can't fold the measurement, and
            // keep the whole trajectory: a smooth run past 90 is momentum
            // coast, a plateau-then-climb is the sequence re-running.
            float yPrev = sr.transform.eulerAngles.y, cum = 0f;
            var traj = new System.Text.StringBuilder();
            ArmSaved();
            for (float t = 0f; t < 3.0f; t += 0.2f)
            {
                yield return new WaitForSeconds(0.2f);
                float yNow = sr.transform.eulerAngles.y;
                cum += Mathf.DeltaAngle(yPrev, yNow); yPrev = yNow;
                traj.Append(Mathf.RoundToInt(cum)).Append(" ");
            }
            Check(Mathf.Abs(cum) > 50f && Mathf.Abs(cum) < 135f,
                  "UI-authored TURN BY 90° turns once and parks on the WAIT (" + F(cum)
                  + "°; traj " + traj.ToString().TrimEnd() + ")");
            Disarm(); bm.BackToBuild(); yield return null;
            yield return ShowProgramTab();

            // Variation C+D: MOVE toward/away WALL — the btgt and bdir chips.
            bool okC = false;
            yield return AuthorSingle(POp.MoveRel, v => okC = v);
            if (okC)
            {
                // target chip: cycle until WALL (compass makes ENEMY the default)
                for (int i = 0; i < 4; i++)
                {
                    var b = LastBlock();
                    if (b != null && (PTarget)b.target == PTarget.Wall) break;
                    int nt = ChipCount("btgt");
                    if (!Tap("btgt", nt - 1)) break;
                    yield return null;
                }
                var lb = LastBlock();
                Check(lb != null && (PTarget)lb.target == PTarget.Wall,
                      "target chip cycles to WALL");
                // give it time to act
                int ndur = ChipCount("bdur");
                if (Tap("bdur", ndur - 1)) yield return null;   // 1s -> next V_DUR (longer)
                if (Tap("prog_load")) yield return null;
                lastSavedProg = Career.Data.stable[0].program;
                yield return SpawnRig();
                yield return AimWallAt(0f);
                float c0 = bus.wallDist;
                ArmSaved();
                yield return new WaitForSeconds(1.5f);
                float c1 = bus.wallDist;
                Check(c1 < c0 - 0.2f, "UI-authored MOVE TOWARD WALL closes (" + F(c0) + " -> " + F(c1) + ")");
                Disarm(); bm.BackToBuild(); yield return null;
                yield return ShowProgramTab();

                // flip direction with the bdir chip: TOWARD -> AWAY
                var b2 = LastBlock();
                float argBefore = b2 != null ? b2.arg : 0f;
                int ndir = ChipCount("bdir");
                if (Tap("bdir", ndir - 1)) { yield return null; yield return null; }
                b2 = LastBlock();
                Check(b2 != null && Mathf.Sign(b2.arg) != Mathf.Sign(argBefore),
                      "direction chip flips the sign — " + argBefore + " -> "
                      + (b2 != null ? b2.arg.ToString() : "?") + " (the AWAY bug's home ground)");
                if (Tap("prog_load")) yield return null;
                Check(Career.Data.stable[0].program != lastSavedProg,
                      "LOAD TO ROBOT saved the flipped program (status: '" + pc.TestStatus + "')");
                lastSavedProg = Career.Data.stable[0].program;
                yield return SpawnRig();
                yield return AimWallAt(0f);
                // start close-ish so AWAY has room to grow the reading
                float a0 = bus.wallDist;
                ArmSaved();
                yield return new WaitForSeconds(1.5f);
                float a1 = bus.wallDist;
                Check(a1 > a0 + 0.2f, "UI-authored MOVE AWAY FROM WALL opens (" + F(a0) + " -> " + F(a1) + ")");
                Disarm(); bm.BackToBuild(); yield return null;
                yield return ShowProgramTab();
            }

            // Variation E: STOP ALL leaves the drive quiet.
            bool okE = false;
            yield return AuthorSingle(POp.StopAll, v => okE = v);
            if (okE && Tap("prog_load")) yield return null;
            Check(Career.Data.stable[0].program != lastSavedProg,
                  "LOAD TO ROBOT saved the STOP ALL program (status: '" + pc.TestStatus + "')");
            yield return SpawnRig();
            ArmSaved();
            yield return new WaitForSeconds(1.0f);
            Check(MaxCmd() <= 0.05f, "UI-authored STOP ALL keeps every channel quiet (max " + F(MaxCmd()) + ")");
            Disarm(); bm.BackToBuild(); yield return null;
        }
        finally
        {
            Disarm();
            if (bm != null) bm.BackToBuild();
            Career.Data = savedData; Career.active = savedActive;
            Career.devFreeBuild = savedFree;
            hold.Dispose();
            Debug.Log("[ProgTest] RESULT: " + passed + " pass, " + failed + " fail"
                      + (failed == 0 && passed > 0 ? " - ALL GREEN" : passed == 0 ? " - NOTHING RAN, not a pass" : " - FIX NEEDED"));
            finished = true;
        }
    }
}
}
#endif
