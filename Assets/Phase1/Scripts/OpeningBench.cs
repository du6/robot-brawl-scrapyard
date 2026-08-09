using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace RobotBrawl.Phase0
{
/// <summary>OPENING BENCH — 2026-08-09. Measures the ONE event the FLOOR check
/// could only ever see as noise: the opening spinner-on-spinner exchange that
/// shears both weapons and inverts the heavier robot
/// (see Opening_Disarm_Tutorial_Floor_2026-08-09.md).
///
/// WHY A NEW BENCH. MatrixBench.RunFloor() is a 90 s-per-bout WIN-RATE bench
/// with N=10 and a true disarm rate near 1/3, so its output swings 6/10 - 8/10
/// with no code change. You cannot tune against it: the measurement noise is
/// larger than any plausible effect. This bench measures the DISARM RATE
/// directly, at N per arm, and stops each bout at CUTOFF seconds — the event
/// it cares about is over by t=3 s, so 83 of every 90 seconds RunFloor spends
/// are spent on a question this bench is not asking.
///
/// WHY INTERLEAVED ARMS. Run A of the floor bench scored bouts 4 and 5 of a
/// cell BYTE-IDENTICAL (19 dealt / 10 taken / 14 s), which means something
/// latches across bouts inside one play session. A before/after comparison run
/// as two separate batches would confound that drift with the change. Every
/// arm therefore runs bout k back-to-back with every other arm's bout k, the
/// same way AIController.avoidOverride interleaves its sweep.
///
/// WHAT IT REPORTS, per arm: how often the player ended the opening with no
/// weapon left, how often it ended on its back, and how much damage it had
/// managed to deal. It does NOT pass or fail — it prints rates. A rate is what
/// a bimodal process has; a threshold on one is the mistake the floor check
/// already made once.
///
/// Run in play mode: OpeningBench.Run(); read [OpeningBench] lines and
/// Assets/Phase1/qa_opening.txt.</summary>
public class OpeningBench : MonoBehaviour
{
    public static OpeningBench Run()
    { return new GameObject("opening_bench").AddComponent<OpeningBench>(); }

    /// <summary>Bouts per arm. 12 puts the 95% interval on a ~30% rate at
    /// roughly +/-26 points — still wide, but the arms share their noise
    /// because they are interleaved, so the DIFFERENCE is far better resolved
    /// than either rate.</summary>
    public static int N_PER_ARM = 12;
    /// <summary>Seconds of FIGHT time (post-bell) to observe. The disarm lands
    /// at ~2 s; 7 s leaves room for the recoil to settle and for the flip to
    /// either happen or not.</summary>
    public static float CUTOFF = 7f;
    /// <summary>L1C1 scout-R — the tutorial's first fight, and the cell where
    /// the floor bench recorded 4 disarms in 15 bouts.</summary>
    public static int LI = 0, CI = 0;

    public bool finished;
    readonly List<string> log = new List<string>();
    void Note(string s) { log.Add(s); }

    // Same fixture as MatrixBench, so the two benches are talking about the
    // same robot. ONE body across every arm: the measured variable is the
    // program, not the chassis.
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

    // ---- the arms ---------------------------------------------------------

    /// <summary>RUN 1 RESULT (qa_opening_run1_arms.txt), N=12/arm interleaved:
    ///
    ///   A shipped (ram 100%)   disarmed 42%   flipped 17%   dealt@7s   54
    ///   B no-ram  (ram  45%)   disarmed  0%   flipped  8%   dealt@7s  378
    ///   C angled  (ram 100%)   disarmed 42%   flipped 17%   dealt@7s   77
    ///   D both    (ram  45%)   disarmed  0%   flipped  0%   dealt@7s  389
    ///
    /// The whole effect is the CLOSING THROTTLE. Arm C isolated the geometry —
    /// an approach that is deliberately not pure pursuit, so the two machines
    /// arrive off each other's centre line — and it moved nothing: 42% and 42%.
    /// The handover's "do not meet nose-to-nose at full closing speed" named
    /// two things, and the measurement says only the second half was load
    /// bearing. Arm C is retired; it earned its retirement by being run.
    ///
    /// RUN 2 (this one) sweeps the one variable that mattered, because 45% was
    /// a guess and a guess that works is still a guess. The control arm at
    /// 100% stays in the sweep so run 2 can be compared to run 1 directly.
    /// </summary>
    /// RUN 2 RESULT (qa_opening_run2_sweep.txt), the closing-throttle sweep:
    ///
    ///   ram 100%  disarmed 42% (5/12, the SAME 5 as run 1)  dealt@7s 107
    ///   ram  70%  disarmed  0%   flipped 17%                dealt@7s 248
    ///   ram  55%  disarmed  0%   flipped  8%                dealt@7s 357
    ///   ram  45%  disarmed  0%   flipped  0%                dealt@7s 358
    ///   ram  30%  disarmed  0%   flipped  0%                dealt@7s 382
    ///
    /// Cliff between 100 and 70, damage plateau at 45-30. SHIPPED: Brawler's
    /// IN RANGE is 45f, the fastest closing speed scoring 0% on both failure
    /// modes. Owner kept it after seeing the ladder trade.
    ///
    /// RUN 3 (MATADOR = true) asks the same question of the other preset that
    /// rams: Matador's STRIKE commands 100% for 1.5 s inside 2.2 m. It was
    /// deliberately left alone during runs 1-2 so it could serve as the
    /// untouched control — which earned its keep, because Matador's upper
    /// league fights moved 4/12 -> 7/12 with NO code change and that is what
    /// proved MatrixBench's UPPER sample cannot resolve a change this size.
    /// Its duration stays at the shipped 1.5 s: one variable.
    /// </summary>
    /// RUN 3 (Matador, 7 s cutoff) — THROWN AWAY. All 60 bouts byte-identical
    /// at every throttle: the 7 s window was tuned to Brawler, which makes
    /// contact at ~2 s. Matador stalks in at 55% and had not finished its
    /// first bite, so every arm was measured before the difference could
    /// express. A swept variable that produces NO variance has usually not
    /// been swept. CUTOFF is per-subject; say which value produced the numbers.
    ///
    /// RUN 4 (Matador, 25 s cutoff) — the retraction. Matador NEVER disarms:
    /// 0/60 at every throttle, and lowering it is strictly worse (dealt
    /// 307 -> 223, flipped 0% -> 33% by 30%). Matador rams at 100% for LONGER
    /// than Brawler did and shears nothing, so "a 100% ram shears your mount"
    /// is false. The rule that fits both presets:
    ///
    ///   A closing ram is dangerous when NOTHING DISENGAGES AFTER THE BITE.
    ///
    /// Matador has TOO HOT and leaves after every hit. Brawler had no
    /// disengage at all: it rammed and HELD, summing the disc's contact
    /// impulse and the hull's ram impulse at the mount for as long as contact
    /// lasted. Cutting Brawler's throttle worked because it cut the sustained
    /// press — not because 100% is intrinsically wrong.
    ///
    /// RUN 5 (this one) tests that rule directly, and it is a real test
    /// because it can fail: if the disengage does NOT drop the disarm rate at
    /// 100%, the rule is wrong and closing throttle was the whole story after
    /// all. Two factors, five arms, with BOTH shipped-Brawler controls in the
    /// batch so the effect size is measured against known numbers rather than
    /// against a previous run.
    /// </summary>
    public static float[] RAM_PCTS  = { 45f,   100f,  100f,  70f,   45f  };
    public static bool[]  DISENGAGE = { false, false, true,  true,  true };

    /// <summary>Which preset's ram is under test. Brawler is shipped-fixed at
    /// 45%; flip this to sweep Matador instead (needs CUTOFF >= 25).</summary>
    public static bool MATADOR = false;

    /// <summary>Matador's TOO HOT, transplanted verbatim onto Brawler: after a
    /// bite lands, reverse at 90% for 0.8 s and turn away. Inserted at index 0
    /// so it outranks IN RANGE, exactly as it does in Matador.</summary>
    static PHat BreakOffHat()
    {
        var h = new PHat { name = "BREAK OFF" };
        h.when.Add(PCondTerm.Mk(PCond.EnemyRange, PCmp.Less, 3.0f));
        h.when.Add(PCondTerm.Mk(PCond.HpFrac, PCmp.Greater, 0.35f));
        h.when.Add(PCondTerm.Mk(PCond.HitRecently, PCmp.Greater, 0.5f));
        h.body.Add(PBlock.MkMoveRel(PTarget.Enemy, -90f, 0.8f, 0));
        h.body.Add(PBlock.MkTurnLR(30f, 0.4f));
        return h;
    }

    /// <summary>The shipped preset with ONE number changed: the throttle its
    /// close-range hat commands once the enemy is inside 2.2 m. At 100% each
    /// is byte-identical to the RobotProgram factory it mirrors.</summary>
    static RobotProgram ArmRam(float pct) { return ArmRam(pct, false); }

    static RobotProgram ArmRam(float pct, bool disengage)
    {
        if (MATADOR) return ArmMatadorStrike(pct);
        var p = new RobotProgram { title = "ram" + pct.ToString("F0") };
        var close = new PHat { name = "IN RANGE" };
        close.when.Add(PCondTerm.Mk(PCond.EnemyRange, PCmp.Less, 2.2f));
        close.body.Add(PBlock.MkWeapon(true));
        close.body.Add(PBlock.MkMoveRel(PTarget.Enemy, pct, 1.0f, 0));
        p.hats.Add(close);
        var seek = new PHat { name = "SEEK" };
        seek.when.Add(PCondTerm.Always());
        seek.body.Add(PBlock.MkMoveRel(PTarget.Enemy, 70f, 0f, 0));
        seek.body.Add(PBlock.MkForever());
        seek.body.Add(PBlock.MkWait(0.2f));
        seek.body.Add(PBlock.MkEnd());
        p.hats.Add(seek);
        if (disengage) { p.hats.Insert(0, BreakOffHat()); p.title += "b"; }
        return p;
    }

    /// <summary>RobotProgram.Matador() with STRIKE's throttle swept. Every
    /// other clause — the TOO HOT dodge, the 55% stalk, STRIKE's 1.5 s — is
    /// the shipped program verbatim.</summary>
    static RobotProgram ArmMatadorStrike(float pct)
    {
        var p = new RobotProgram { title = "mat" + pct.ToString("F0") };
        var dodge = new PHat { name = "TOO HOT" };
        dodge.when.Add(PCondTerm.Mk(PCond.EnemyRange, PCmp.Less, 3.0f));
        dodge.when.Add(PCondTerm.Mk(PCond.HpFrac, PCmp.Greater, 0.35f));
        dodge.when.Add(PCondTerm.Mk(PCond.HitRecently, PCmp.Greater, 0.5f));
        dodge.body.Add(PBlock.MkMoveRel(PTarget.Enemy, -90f, 0.8f, 0));
        dodge.body.Add(PBlock.MkTurnLR(30f, 0.4f));
        p.hats.Add(dodge);
        var strike = new PHat { name = "STRIKE" };
        strike.when.Add(PCondTerm.Mk(PCond.EnemyRange, PCmp.Less, 2.2f));
        strike.body.Add(PBlock.MkWeapon(true));
        strike.body.Add(PBlock.MkMoveRel(PTarget.Enemy, pct, 1.5f, 0));
        p.hats.Add(strike);
        var stalk = new PHat { name = "STALK" };
        stalk.when.Add(PCondTerm.Always());
        stalk.body.Add(PBlock.MkMoveRel(PTarget.Enemy, 55f, 0f, 0));
        stalk.body.Add(PBlock.MkForever());
        stalk.body.Add(PBlock.MkWait(0.2f));
        stalk.body.Add(PBlock.MkEnd());
        p.hats.Add(stalk);
        return p;
    }

    struct Arm { public string name; public System.Func<RobotProgram> mk;
                 public int disarmed, flipped, enemyDisarmed, ko, n;
                 public float dealt, taken; }

    BuilderManager bm;
    int noStarts;

    IEnumerator Start()
    {
        var ms = GameObject.Find("ModeSelect");
        if (ms != null) Destroy(ms);
        bm = Object.FindFirstObjectByType<BuilderManager>();
        if (bm == null) { Note("FATAL no BuilderManager"); Finish(null, false, false, false); yield break; }
        float tb = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - tb < 3f) yield return null;

        var savedData = Career.Data; bool savedActive = Career.active; bool savedAuto = Career.autosave;
        Career.autosave = false;
        Career.fightAutonomous = false;
        Career.Data = new CareerData();
        Career.Data.stable.Add(new CareerRobot { name = "OPENBOT", snapshot = "", program = "" });
        Career.Data.activeRobot = 0;
        Career.active = true;
        Career.Data.scrap = 999999;
        Career.AddItem("beam", "Aluminum", 2);
        Career.AddItem("battery", "Aluminum", 1);
        Career.AddItem("spindle", "Aluminum", 1);
        Career.AddItem("spinner", "Steel", 1);
        Career.AddItem("wheel", "Rubber", 4);
        Career.AddItem("compass", "Aluminum", 1);
        Career.AddItem("tiltsensor", "Aluminum", 1);
        Career.AddItem("wallsensor", "Aluminum", 1);
        Career.AddItem("dmgbus", "Aluminum", 1);
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
        if (nfix != 14) { Note("FATAL fixture loaded " + nfix + "/14"); Finish(savedData, savedActive, savedAuto, true); yield break; }

        var arms = new Arm[RAM_PCTS.Length];
        for (int a = 0; a < arms.Length; a++)
        {
            float pct = RAM_PCTS[a];   // captured per iteration, not by reference
            bool dis = !MATADOR && a < DISENGAGE.Length && DISENGAGE[a];
            arms[a] = new Arm { name = (MATADOR ? "mat " : "ram ") + pct.ToString("F0")
                                       + (MATADOR ? "%" : (dis ? "% break" : "% hold")),
                                mk = delegate { return ArmRam(pct, dis); } };
        }

        // Every arm's program must be legal on this body, or the bench would be
        // silently measuring a program the game would have refused to run.
        var ids = new List<string>();
        foreach (var pp in bm.placed) ids.Add(pp.def.id);
        for (int a = 0; a < arms.Length; a++)
        {
            string bad = arms[a].mk().Validate(ids);
            if (bad != null)
            { Note("FATAL arm " + arms[a].name + " invalid: " + bad); Finish(savedData, savedActive, savedAuto, true); yield break; }
        }

        Note("cell L" + (LI + 1) + "C" + (CI + 1) + "  N=" + N_PER_ARM + "/arm  cutoff=" + CUTOFF + "s  interleaved");
        Note("");

        for (int k = 0; k < N_PER_ARM; k++)
            for (int a = 0; a < arms.Length; a++)
            {
                var res = new float[7];
                yield return StartCoroutine(RunBout(arms[a].mk(), res));
                if (res[6] < 0.5f) { noStarts++; continue; }
                arms[a].n++;
                if (res[0] < 0.5f) arms[a].disarmed++;      // res0 = player weapons left
                if (res[1] < 0.5f) arms[a].enemyDisarmed++; // res1 = enemy weapons left
                if (res[2] < 0.3f) arms[a].flipped++;       // res2 = player upY
                arms[a].dealt += res[3];
                arms[a].taken += res[4];
                if (res[5] > 0.5f) arms[a].ko++;            // res5 = fight already ended
                Note(string.Format("  k{0,-2} {1,-10} weap {2}/{3}  upY {4:F2}  dealt {5,4:F0} taken {6,4:F0}{7}",
                     k + 1, arms[a].name, (int)res[0], (int)res[1], res[2], res[3], res[4],
                     res[5] > 0.5f ? "  [ended early]" : ""));
            }

        Note("");
        Note("ARM              n   disarmed        flipped        enemy-disarmed   dealt@" + CUTOFF + "s");
        for (int a = 0; a < arms.Length; a++)
        {
            var x = arms[a];
            if (x.n == 0) { Note(x.name + "  no bouts"); continue; }
            Note(string.Format("{0,-14} {1,3}   {2,2}/{3,-2} {4,5:F0}%    {5,2}/{6,-2} {7,5:F0}%    {8,2}/{9,-2} {10,5:F0}%      {11,6:F0}",
                 x.name, x.n, x.disarmed, x.n, 100f * x.disarmed / x.n,
                 x.flipped, x.n, 100f * x.flipped / x.n,
                 x.enemyDisarmed, x.n, 100f * x.enemyDisarmed / x.n,
                 x.dealt / x.n));
        }
        Note("");
        Note("no-starts: " + noStarts);
        Note("This bench does not pass or fail. A lower disarm rate at a similar");
        Note("or higher dealt figure is the fix working; a lower disarm rate at a");
        Note("much LOWER dealt figure is a robot that stopped fighting.");

        Finish(savedData, savedActive, savedAuto, true);
    }

    /// <summary>One opening. res = {playerWeapons, enemyWeapons, playerUpY,
    /// dealt, taken, endedEarly, started}.</summary>
    IEnumerator RunBout(RobotProgram prog, float[] res)
    {
        if (bm.mode != BuilderManager.Mode.Build) { bm.BackToBuild(); yield return null; }
        bm.LoadSnapshot(FIXTURE);
        yield return null;
        Career.Data.stable[0].program = prog.ToJson();
        Career.fightAutonomous = false;
        bm.StartCareerFight(LI, CI, true);
        yield return null; yield return null;
        var fm = Object.FindFirstObjectByType<FightManager>();
        if (fm == null || bm.mode != BuilderManager.Mode.Fight)
        {
            res[6] = 0f;
            yield break;
        }
        res[6] = 1f;

        Time.timeScale = 10f;
        float t0 = Time.realtimeSinceStartup;
        // Settle, then watch the opening only.
        while (fm != null && fm.state == FightManager.State.Settling
               && Time.realtimeSinceStartup - t0 < 15f) yield return null;
        while (fm != null && fm.state == FightManager.State.Fighting
               && fm.elapsed < CUTOFF && Time.realtimeSinceStartup - t0 < 30f)
            yield return null;

        if (fm != null)
        {
            res[0] = Weapons(fm.player.bot);
            res[1] = Weapons(fm.enemy.bot);
            res[2] = fm.player.bot != null
                   ? Vector3.Dot(fm.player.bot.transform.up, Vector3.up) : -1f;
            res[3] = fm.player.dealt;
            res[4] = fm.player.taken;
            res[5] = fm.state == FightManager.State.Ended ? 1f : 0f;
            if (fm.state != FightManager.State.Ended)
                fm.End(FightManager.Outcome.Draw, "opening bench cutoff");
        }
        yield return null;
        Time.timeScale = 1f;
        bm.BackToBuild();
        yield return null; yield return null;
    }

    /// <summary>Live edges still bolted on. Same test FightManager's own
    /// "both machines disarmed" call uses, so the two agree by construction.
    /// A KO'd body is gone, and a robot that no longer exists has no weapon.</summary>
    static float Weapons(CompoundRobot bot)
    {
        if (bot == null) return 0f;
        int n = 0;
        for (int i = 0; i < bot.parts.Count; i++)
        {
            var p = bot.parts[i];
            if (!p.detached && DamageResolver.IsEdge(p.spec.edgeHardness)) n++;
        }
        return n;
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
        foreach (var l in log) { Debug.Log("[OpeningBench] " + l); sb.Append(l).Append('\n'); }
        try { System.IO.File.WriteAllText(
                  Application.dataPath + "/Phase1/qa_opening.txt", sb.ToString()); }
        catch { }
        finished = true;
    }
}
}
