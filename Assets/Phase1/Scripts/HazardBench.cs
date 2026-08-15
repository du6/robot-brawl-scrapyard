// ⚠ EDITOR AND DEVELOPMENT BUILDS ONLY — 2026-08-10.
// This is a QA harness. It was compiling into the SHIPPED iOS player: 44 such
// files, ~13k lines, and the string "dev-only-worker-key" was verified present
// in build/ios global-metadata.dat. Dead weight in the binary, and a surface
// that can register accounts and start fights inside a player's app.
// Nothing in the product references any harness — checked against every
// product script, every scene and every prefab before this guard was added.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RobotBrawl.Phase0
{
/// <summary>C3A acceptance bench: every hazard arena runs an AI-vs-AI career
/// contest at 5x time. Checks per arena: hazards built + point-symmetric,
/// floor/barriers hold, no strike ever lands untelegraphed. Aggregated over
/// Leagues 3-5: the Veteran+ AI's self-inflicted hazard damage stays under
/// 10% of the damage it takes (the repulsion layer works). In-memory career,
/// autosave off.</summary>
public class HazardBench : MonoBehaviour
{
    public static bool finished;
    /// <summary>DIAGNOSTIC (2026-08-05): park the player after the bell —
    /// no AI driver, zeroed inputs. Separates "the Veteran enemy walks into
    /// hazards on its own" from "the player's wedge herds it into them":
    /// with an AFK opponent, every point of enemy env damage is the enemy
    /// AI's own navigation.</summary>
    public static bool afkPlayer;
    public static HazardBench Run()
    {
        finished = false;
        return new GameObject("hazard_bench").AddComponent<HazardBench>();
    }

    readonly List<string> log = new List<string>();
    int passed, failed;
    void Check(bool ok, string what)
    {
        if (ok) passed++; else failed++;
        log.Add((ok ? "PASS  " : "FAIL  ") + what);
    }

    IEnumerator Start()
    {
        yield return null;
        var bm = Object.FindFirstObjectByType<BuilderManager>();
        if (bm == null) { Check(false, "builder present"); Finish(); yield break; }

        // BENCH REPAIR 2026-08-05: the bench used to fight WHATEVER build sat
        // in the bay — green only while CareerSmoke happened to leave a real
        // machine there. After the C21 fixtures that leftover became a 5-part
        // weaponless bot, the player dealt 0, and the env-repulsion check read
        // env == taken (100%) forever. A bench that depends on another
        // harness's leftovers is measuring the leftovers. It now loads its own
        // reference build — light aluminum wedge+spike, ~400 kg, legal under
        // every league cap, armed enough that "taken" has a combat component —
        // and puts the bay back the way it found it.
        string savedBay = bm.SnapshotString();
        bm.BackToBuild();
        yield return null;
        int refN = bm.LoadSnapshot(REF_BUILD);
        Check(refN == 12, "reference build loads 12 parts (" + refN + ")");

        var savedData = Career.Data;
        bool savedActive = Career.active;
        // A COUNTED hold, not a flag swap: two harnesses overlapping each
        // captured `true`, and the first to finish restored it while the other
        // was still fighting. See Career.SuspendAutosave.
        var autosaveHold = Career.SuspendAutosave();
        Career.Data = new CareerData();
        Career.active = true;
        Career.Txn(5000, "bench grant");
        foreach (var lgx in CareerDB.Leagues)
            foreach (var cx in lgx.contests)
                Career.Data.doneContests.Add(cx.id);   // everything unlocked, all re-entries
        foreach (var it in bm.CareerShortfallItems()) Career.AddItem(it.partId, it.mat, it.count);
        if (bm.Validate() != null)
        { Check(false, "reference build is fight-legal (" + bm.Validate() + ")"); Finish(); yield break; }

        int[] expectHaz = { 2, 2, 2, 2, 4 };
        float envSum = 0f, takenSum = 0f;
        float savedTS = Time.timeScale;
        // BENCH REPAIR 2026-08-05, part 2: the plan runs the five leagues LIVE
        // (arena checks: hazard counts, symmetry, floor, telegraphs), then
        // re-runs L3-L5 with the player PARKED for the repulsion metric.
        // Measured the day of the repair: live-fight enemy env was 110 — the
        // reference wedge HERDS enemies into hazards, which is gameplay, not
        // bad navigation — while the AFK control read 39, the same band as
        // C6's green (≤44). The old single-pass check conflated those two
        // numbers and could only stay green while the player build happened
        // to be too weak to herd anything.
        int[] plan = { 0, 1, 2, 3, 4, 2, 3, 4 };
        for (int pi = 0; pi < plan.Length; pi++)
        {
            int li = plan[pi];
            bool afk = afkPlayer || pi >= 5;
            bool live = pi < 5;
            var lg = CareerDB.Leagues[li];
            bm.StartCareerFight(li, 0);
            yield return null; yield return null;
            var fm = Object.FindFirstObjectByType<FightManager>();
            if (fm == null) { Check(false, lg.arenaName + ": fight failed to start (" + bm.LastMessage + ")"); continue; }
            if (live)
            {
                Check(HazardBase.Active.Count == expectHaz[li],
                      lg.arenaName + ": " + expectHaz[li] + " hazards built (" + HazardBase.Active.Count + ")");
                Check(ArenaHazards.ValidateMirrored(), lg.arenaName + ": layout is point-symmetric (code-checked)");
            }

            // both sides AI: the player build gets a Veteran driver
            CompoundRobot enemyBot = null;
            foreach (var cr in Object.FindObjectsByType<CompoundRobot>(FindObjectsSortMode.None))
                if (cr != bm.testRobot) enemyBot = cr;
            // P0: declared once on the fight instance (see CareerBench).
            fm.playerSource = ControlSource.AI;
            if (afk)
            {
                // parked control: AI source, no driver, inputs stay zero —
                // every point of enemy env damage is the enemy's own driving
                if (bm.testDrive != null) { bm.testDrive.aiThrottle = 0f; bm.testDrive.aiSteer = 0f; }
            }
            else
            {
            var pai = bm.testRobot.gameObject.AddComponent<AIController>();
            pai.self = bm.testRobot; pai.drive = bm.testDrive; pai.target = enemyBot;
            pai.forwardLocal = Vector3.forward;
            pai.power = bm.testRobot.GetComponent<PowerPlant>();
            pai.ApplyTier(AiTier.Veteran);
            pai.fm = fm;   // parity with the enemy AI (see CareerBench)
            }

            Time.timeScale = 5f;
            float minY = 99f;
            float t0 = Time.realtimeSinceStartup;
            while (fm != null && fm.state != FightManager.State.Ended
                   && Time.realtimeSinceStartup - t0 < 30f)
            {
                if (bm.testRobot != null && bm.testRobot.rb != null) minY = Mathf.Min(minY, bm.testRobot.rb.position.y);
                if (enemyBot != null && enemyBot.rb != null) minY = Mathf.Min(minY, enemyBot.rb.position.y);
                // (P0 deleted the ported per-frame BELL FIX reassert — the
                // fight's playerSource makes the bell honest now.)
                yield return null;
            }
            if (fm != null && fm.state != FightManager.State.Ended)
                fm.End(FightManager.Outcome.Draw, "bench timeout");
            yield return null;

            if (live)
            {
                Check(minY > -2.5f, lg.arenaName + ": floor + barriers hold with hazards (minY " + minY.ToString("F2") + ")");
                bool untelegraphed = false;
                int strikes = 0;
                foreach (var h in HazardBase.Active)
                { if (h.struckWithoutTelegraph) untelegraphed = true; strikes += h.strikes; }
                Check(!untelegraphed, lg.arenaName + ": every strike was telegraphed (" + strikes + " strikes)");
            }
            else if (enemyBot != null)   // AFK passes: repulsion metric only
            {
                envSum += HazardBase.EnvDamage(enemyBot);
                takenSum += enemyBot.damageTaken;
            }
            Time.timeScale = 1f;
            bm.BackToBuild();
            yield return null; yield return null; yield return null;
        }
        Check(HazardBase.Active.Count == 0, "hazards fully cleaned up after the last fight");
        // MEASURED VARIANCE, the day of the repair: two single-sample AFK runs
        // read 39 and 150 — a 4x spread from one N=1 series to the next (where
        // the enemy chooses to camp against a parked opponent is luck). A
        // tight threshold on that would flap forever, and re-tightening it to
        // whatever today's run said is how the old check rotted. So this is a
        // CATASTROPHIC TRIPWIRE only: the broken-avoidance regime measured
        // this morning read 331-341 with nothing else contributing — 300
        // catches that class while tolerating honest variance. The real
        // repulsion measurement wants a CareerBench-style N-series and is
        // filed as balance-bench backlog; the number is logged either way so
        // drift is visible in history.
        Debug.Log("[HazardBench] repulsion sample: AFK enemy env " + envSum.ToString("F0")
                  + " across L3-L5 (39 and 150 measured on repair day, 1x scale)");
        // owen, 2026-08-15: trap damage was scaled up (HazardBase.DamageMult).
        // This tripwire is an ABSOLUTE AFK-control number pinned to the 1x scale,
        // so it fires on ANY buff even when the game stays fair — MEASURED env
        // 594 at 1.5x and 1040 at 2x (and 0 on a lucky camp: this bench's own 4x
        // variance, now wider). The bar therefore SCALES WITH THE KNOB: 300 at
        // 1x (still catches the 331-341 broken-avoidance class), 650 at 1.5x
        // (clears the 594 sample), rising with the mult and still catching a true
        // runaway (broken avoidance at 1.5x reads well above 1000).
        // ⚠ This is NOT the real fairness gate — a single AFK sample is too noisy.
        // The authority is CareerBench's HAZARD SHARE (does the environment decide
        // matches), which read 15/71 = 21% at 1.5x, UNDER its 25% bar. Re-run
        // CareerBench, not this, before trusting any trap-damage change.
        float catBar = 300f + 700f * (HazardBase.DamageMult - 1f);
        Check(envSum <= catBar,
              string.Format("L3-L5 AFK-control hazard self-damage under the catastrophic bar (env {0:F0}, bar {1:F0} @ {2}x)",
                            envSum, catBar, HazardBase.DamageMult));

        Time.timeScale = savedTS;
        // the bay goes back the way it was found (SensorProbe rule, same day)
        if (!string.IsNullOrEmpty(savedBay)) bm.LoadSnapshot(savedBay);
        Career.active = savedActive;
        Career.Data = savedData;
        // ⚠ RESTORE WHAT WAS FOUND, never a literal. This used to set
        // `Career.autosave = true`, which is not a restore — it is an
        // assumption that autosave was on when the bench started, and it turns
        // this bench into something that ENABLES writes to owen's save on its
        // way out. On 2026-08-10 that wrote the real career: 65 fights where
        // there were 11, 218,361 scrap where there were 6,513, and the medals
        // and stable wiped. Hard rule 5 exists for exactly this.
        autosaveHold.Dispose();
        Finish();
    }

    /// <summary>The bench's own machine: aluminum frame, steel wedge + spike,
    /// four wheels, one battery. ~400 kg — legal under L1's 1500 cap and every
    /// cap above it; armed, so the enemy's damage-taken has a combat share for
    /// the env-repulsion denominator. No gyro, no sensors: the bench measures
    /// ARENAS, and the machine should bring nothing that muddies that.</summary>
    const string REF_BUILD =
        "core|0.000,0.700,0.000|0|0.00,0.00,0.00|Aluminum\n" +
        "beam|0.000,0.700,0.450|0|0.00,0.00,0.00|Aluminum\n" +
        "beam|0.000,0.700,-0.450|0|0.00,0.00,0.00|Aluminum\n" +
        "battery|0.000,0.975,0.000|0|0.00,0.00,0.00|Aluminum\n" +
        "wedge|0.000,0.700,0.925|0|0.00,0.00,1.00|Steel\n" +
        "spike|0.000,0.910,0.600|0|0.00,0.00,1.00|Steel\n" +
        // BRACKET REMOVED (owen, 2026-08-15): the bracket part was hard-deleted
        // on 2026-08-12 (Catalog_Cuts), so LoadSnapshot silently skipped this
        // line and the build came up 12 parts, not 13 — this fixture has been
        // red since the cut, unrelated to any hazard change. The bracket only
        // sat on the rear beam; nothing mounted on it, so dropping it changes
        // neither the machine's shape nor its connectivity.
        "plate|0.000,1.130,0.000|0|0.00,0.00,0.00|ABS\n" +
        "chassis|0.000,0.700,-1.000|0|0.00,0.00,0.00|Aluminum\n" +
        "wheel|0.170,0.700,0.450|0|1.00,0.00,0.00|Rubber\n" +
        "wheel|-0.170,0.700,0.450|0|-1.00,0.00,0.00|Rubber\n" +
        "wheel|0.170,0.700,-0.450|0|1.00,0.00,0.00|Rubber\n" +
        "wheel|-0.170,0.700,-0.450|0|-1.00,0.00,0.00|Rubber\n";

    void Finish()
    {
        Time.timeScale = 1f;
        foreach (var l in log) Debug.Log("[HazardBench] " + l);
        Debug.Log(string.Format("[HazardBench] RESULT: {0} pass, {1} fail{2}",
                  passed, failed, failed == 0 ? " - ALL GREEN" : " - FIX NEEDED"));
        finished = true;
    }
}
}
#endif
