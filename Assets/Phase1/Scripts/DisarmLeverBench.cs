// ⚠ EDITOR AND DEVELOPMENT BUILDS ONLY — 2026-08-10.
// This is a QA harness. It was compiling into the SHIPPED iOS player: 44 such
// files, ~13k lines, and the string "dev-only-worker-key" was verified present
// in build/ios global-metadata.dat. Dead weight in the binary, and a surface
// that can register accounts and start fights inside a player's app.
// Nothing in the product references any harness — checked against every
// product script, every scene and every prefab before this guard was added.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
// ===========================================================================
// DisarmLeverBench — which lever actually moves the 43% mutual disarm.
// 2026-08-10.
//
//   RobotBrawl.Phase0.DisarmLeverBench.Run();     // play mode
//   RobotBrawl.Phase0.DisarmLeverBench.finished / Report()
//
// ⚠ THIS IS A MEASUREMENT BENCH. No pass/fail, no thresholds, and it CHANGES
// NOTHING: every constant it moves is restored in a finally. It exists to
// turn `CLAUDE.md` §7.5's "which structural constant moves is unmade" into a
// table owen can choose from, which is what
// docs/Mutual_Disarm_Root_Cause_2026-08-09.md said the next step was:
//
//     "It does not license a balance change. Balance is owen's call and gets
//      made from the table, not from a threshold invented by whoever ran the
//      bench."
//
// THE FINDING IT BUILDS ON. 69% of weapons leave a body with a mean 78% of
// their HP still in them — shed when the structure carrying them fails. So
// WEAPON_VS_WEAPON cannot reach them at ANY value, and two fixes aimed there
// moved the number by zero bouts. The lever has to act on STRUCTURE.
//
// THE ARMS, and why each one is here:
//
//   baseline          what ships today, re-measured in the same session as
//                     the others so the comparison is not against a number
//                     from a different build.
//   BREAK_K x1.5/x2   the SEAM. Candidate 1 in the doc: "the seam carrying a
//                     weapon-bearing limb, so an arm survives contact that
//                     currently sheds it."
//   WEAPON_VS_STRUCT  candidate 3: weapon-on-structure damage, which no
//     0.5 / 0.25      weapon rule touched at all until today. 90 structural
//                     parts died to edges over 30 bouts.
//   SHORT LIMB        NOT a constant — the CHASSIS. The doc's closing
//                     prediction is that "limb geometry matters more than
//                     weapon material", and it names varying the chassis as
//                     "the next sweep, and it is now the obvious one". If
//                     this arm dominates the constants, then tuning a global
//                     number is the WRONG lever and the answer is a build
//                     rule, which is a very different conversation.
//
// ⚠ SAMPLE, STATED HONESTLY. DisarmBench uses 5 programs (15 pairings) x 2
// seeds = 30 bouts. Seven arms of that is 210 real fights, which is hours.
// This uses the SAME 5 programs (15 pairings) x 1 seed = 15 bouts per arm, 105
// total.
// That is a SMALLER sample than the 30-bout figure everything else quotes, so
// the per-arm percentages carry more noise and the baseline arm is the only
// honest thing to compare them against — NOT the historical 43%. The report
// says so in its own output, because a number that gets quoted out of a log
// needs its caveat attached to it, not filed next to it.
//
// Career: MatchRunner isolates Career.Data itself, and this takes a counted
// SuspendAutosave hold on top because it runs 84 real fights.
// ===========================================================================
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RobotBrawl.Phase0
{
    public class DisarmLeverBench : MonoBehaviour
    {
        public static bool finished;
        static readonly List<string> log = new List<string>();
        public static string Report()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var l in log) sb.Append(l).Append('\n');
            return sb.ToString();
        }

        public static DisarmLeverBench Run()
        {
            finished = false; log.Clear();
            return new GameObject("disarm_lever_bench").AddComponent<DisarmLeverBench>();
        }

        // ONE seed, not two: five programs is 15 pairings, and 15 bouts x 7 arms
        // is already 105 real fights. The pairing matrix buys more variety per
        // bout than a second seed does.
        static readonly int[] SEEDS = { 101 };

        class Arm
        {
            public string name;
            public System.Action apply;      // set the constants for this arm
            public bool shortLimb;
        }

        class Result
        {
            public string name;
            public int bouts, both, either;
            public int wpnLost, shed, hpKilled, wrecked;
            public int bodyLost;
            public int leftWithHp; public float hpSum;
        }

        // ---- the chassis, and a shorter-armed twin -------------------------
        // VerbBench.ARMED carries its spinner at z=1.100 on a spindle at 0.900
        // over a beam at 0.450 — an arm about 1.1 m long. SHORT moves the
        // weapon in by 0.45 and keeps everything else identical, so the only
        // variable is the moment arm the seam has to hold.
        const string SHORT_LIMB =
            "core|0.000,0.700,0.000|0|0.00,0.00,0.00|Aluminum\n" +
            "beam|-0.250,0.700,0.150|0|0.00,0.00,0.00|Aluminum\n" +
            "beam|0.250,0.700,0.150|0|0.00,0.00,0.00|Aluminum\n" +
            "beam|0.000,0.700,-0.250|90|0.00,0.00,0.00|Aluminum\n" +
            "beam|-0.450,0.700,-0.150|270|0.00,0.00,0.00|Aluminum\n" +
            "beam|0.450,0.700,-0.150|270|0.00,0.00,0.00|Aluminum\n" +
            "beam|0.000,0.700,0.450|0|0.00,0.00,0.00|Aluminum\n" +
            "spindle|0.000,0.800,0.450|0|0.00,0.00,1.00|Aluminum\n" +
            "spinner|0.000,0.800,0.650|0|0.00,0.00,1.00|Steel\n" +
            "wheel|0.620,0.700,0.000|90|1.00,0.00,0.00|Rubber\n" +
            "wheel|-0.620,0.700,0.000|90|-1.00,0.00,0.00|Rubber\n" +
            "battery|0.000,0.975,0.000|0|0.00,0.00,0.00|Aluminum\n" +
            "beam|0.000,0.700,-0.650|0|0.00,0.00,0.00|Steel\n" +
            "wheel|0.170,0.700,-0.800|0|1.00,0.00,0.00|Rubber\n" +
            "wheel|-0.170,0.700,-0.800|0|-1.00,0.00,0.00|Rubber\n" +
            "beam|0.225,0.975,0.000|0|0.00,0.00,0.00|ABS\n" +
            "beam|-0.250,0.900,0.150|0|0.00,0.00,0.00|ABS\n" +
            "compass|0.000,0.880,-0.650|0|0.00,1.00,0.00|Aluminum\n" +
            "dmgbus|0.230,0.700,0.000|0|1.00,0.00,0.00|Aluminum\n";

        // Per-arm counters, filled by the destruction hook.
        int wpnLost, shed, hpKilled, wrecked, bodyLost;

        void OnPartDestroyed(object victim, object attacker, object part,
                             float attackerHardness, int src)
        {
            var p = part as CompoundRobot.Part;
            if (p == null) return;
            // Only STRUCTURE is counted here. Weapon departures come from the
            // detach ledger, which sees every route out of a body — counting
            // them in both places would double the ones that overlap.
            if (!DamageResolver.IsEdge(p.spec.edgeHardness)) bodyLost++;
        }

        IEnumerator Start()
        {
            var bm = FindFirstObjectByType<BuilderManager>();
            if (bm == null) { log.Add("no BuilderManager in the scene"); finished = true; yield break; }

            // Three programs rather than five. Named so the sample is legible
            // in the output: this is a SIX-pairing matrix, not fifteen.
            // ⚠ THE SAME FIVE PROGRAMS DisarmBench USES, and that matters.
            // The first run of this sweep used three (Brawler/Rusher/WallShy)
            // and the BASELINE came back 12/12 — 100% mutual disarm against a
            // documented 43%. All three are aggressive, so the subset was not
            // merely noisier, it was BIASED, and saturated at the ceiling
            // where no arm can be told from another going up.
            // Matador and Statue are what pull it off the ceiling.
            var presets = new List<KeyValuePair<string, RobotProgram>>
            {
                new KeyValuePair<string, RobotProgram>("Brawler", RobotProgram.Brawler()),
                new KeyValuePair<string, RobotProgram>("WallShy", RobotProgram.WallShy()),
                new KeyValuePair<string, RobotProgram>("Matador", RobotProgram.Matador()),
                new KeyValuePair<string, RobotProgram>("Rusher",  RobotProgram.Rusher()),
                new KeyValuePair<string, RobotProgram>("Statue",  RobotProgram.Statue()),
            };

            var arms = new List<Arm>
            {
                new Arm { name = "baseline (shipped)",     apply = () => {} },
                new Arm { name = "BREAK_K x1.5 (seam)",    apply = () => CompoundRobot.BREAK_K = 1500f * 1.5f },
                new Arm { name = "BREAK_K x2.0 (seam)",    apply = () => CompoundRobot.BREAK_K = 1500f * 2.0f },
                new Arm { name = "WEAPON_VS_STRUCT 0.50",  apply = () => DamageResolver.WEAPON_VS_STRUCT = 0.5f },
                new Arm { name = "WEAPON_VS_STRUCT 0.25",  apply = () => DamageResolver.WEAPON_VS_STRUCT = 0.25f },
                new Arm { name = "SHORT LIMB (chassis)",   apply = () => {}, shortLimb = true },
                new Arm { name = "SHORT LIMB + BREAK_K x1.5",
                          apply = () => CompoundRobot.BREAK_K = 1500f * 1.5f, shortLimb = true },
            };

            float savedBreakK = CompoundRobot.BREAK_K;
            float savedWvS = DamageResolver.WEAPON_VS_STRUCT;
            var savedHook = DamageResolver.OnPartDestroyed;
            bool savedDetach = CompoundRobot.detachLogOn;
            string ownerBay = bm.SnapshotString();
            var hold = Career.SuspendAutosave();

            var results = new List<Result>();
            try
            {
                DamageResolver.OnPartDestroyed = OnPartDestroyed;
                CompoundRobot.detachLogOn = true;

                foreach (var arm in arms)
                {
                    // Restore, THEN apply: an arm must never inherit the
                    // previous arm's constants. This is the whole reason a
                    // sweep can quietly measure nonsense.
                    CompoundRobot.BREAK_K = savedBreakK;
                    DamageResolver.WEAPON_VS_STRUCT = savedWvS;
                    arm.apply();

                    int n = bm.LoadSnapshot(arm.shortLimb ? SHORT_LIMB : VerbBench.ARMED);
                    yield return null;
                    if (n <= 0) { log.Add("ARM " + arm.name + ": fixture failed to load"); continue; }

                    var envs = new Dictionary<string, SnapshotEnvelope>();
                    foreach (var p in presets)
                        envs[p.Key] = RobotSnapshot.Export(bm, p.Key, p.Value);

                    wpnLost = shed = hpKilled = wrecked = bodyLost = 0;
                    CompoundRobot.detachLog.Clear();
                    var r = new Result { name = arm.name };

                    for (int i = 0; i < presets.Count; i++)
                    for (int j = i; j < presets.Count; j++)
                    {
                        MatchRunner.MatchResult res = null;
                        var mr = MatchRunner.Run(envs[presets[i].Key], envs[presets[j].Key], SEEDS,
                                                 "lever_" + presets[i].Key + "_" + presets[j].Key,
                                                 10f, false, x => { res = x; });
                        mr.stopWhenDecided = false;
                        float t0 = Time.realtimeSinceStartup;
                        while (res == null && Time.realtimeSinceStartup - t0 < 400f) yield return null;
                        if (mr != null && mr.gameObject != null) Destroy(mr.gameObject);
                        if (res == null || !res.Ok) continue;

                        foreach (var b in res.bouts)
                        {
                            r.bouts++;
                            bool aD = b.aWeaponsAlive == 0, bD = b.bWeaponsAlive == 0;
                            if (aD && bD) r.both++;
                            if (aD || bD) r.either++;
                        }
                    }

                    // The DETACH ledger is what separates "the weapon was
                    // destroyed" from "the arm holding it failed" — the whole
                    // distinction this bench exists to move.
                    //
                    // ⚠ PARSED EXACTLY AS DisarmBench PARSES IT. The line is
                    // name|partId|cause|hp X/Y (Z%)|t T|EDGE, so a weapon is
                    // "line ENDS WITH |EDGE" and the cause is field 2 —
                    // substring-matching "SEAM" anywhere in the line would also
                    // hit a part whose id or robot name contained it, and would
                    // miscount silently. One format, one reader.
                    foreach (var line in CompoundRobot.detachLog)
                    {
                        if (line == null || !line.EndsWith("|EDGE")) continue;
                        r.wpnLost++;
                        var f = line.Split('|');
                        string cause = f.Length > 2 ? f[2] : "?";
                        if (cause.StartsWith("HP-DESTROYED")) r.hpKilled++;
                        else if (cause.StartsWith("STRUCTURAL")) r.shed++;
                        else if (cause.StartsWith("SEAM")) r.shed++;
                        else r.wrecked++;

                        // The 69% figure: a weapon that left with hit points
                        // still in it was never beaten, it was DROPPED. This is
                        // the number a working lever has to move.
                        if (f.Length > 3)
                        {
                            int o = f[3].IndexOf('('), c = f[3].IndexOf('%');
                            float pctHp;
                            if (o >= 0 && c > o &&
                                float.TryParse(f[3].Substring(o + 1, c - o - 1), out pctHp) && pctHp > 1f)
                            { r.leftWithHp++; r.hpSum += pctHp; }
                        }
                    }
                    r.bodyLost = bodyLost;
                    results.Add(r);
                    log.Add("  ran " + arm.name + ": bouts=" + r.bouts + " both=" + r.both
                            + " either=" + r.either + " shed=" + r.shed + " hpKill=" + r.hpKilled
                            + " struct=" + r.bodyLost + " leftWithHp=" + r.leftWithHp + "/" + r.wpnLost
                            + (r.leftWithHp > 0 ? " meanHp=" + Mathf.RoundToInt(r.hpSum / r.leftWithHp) + "%" : ""));
                    yield return null;
                }
            }
            finally
            {
                CompoundRobot.BREAK_K = savedBreakK;
                DamageResolver.WEAPON_VS_STRUCT = savedWvS;
                DamageResolver.OnPartDestroyed = savedHook;
                CompoundRobot.detachLogOn = savedDetach;
                hold.Dispose();
            }

            bm.LoadSnapshot(ownerBay);
            yield return null;

            // ---- the table ------------------------------------------------
            log.Add("");
            log.Add("DISARM LEVER SWEEP — 5 programs x 15 pairings x 1 seed = 15 bouts per arm");
            log.Add("⚠ 15 bouts, NOT the 30 every other disarm figure quotes. Compare arms to");
            log.Add("  the BASELINE ROW, never to the historical 43% — different sample.");
            log.Add("");
            // ⚠ {n,W} — NOT {n,>W}. There is no '>' in .NET alignment, and
            // the first version of this line threw FormatException AFTER all
            // 84 fights had run, taking the entire table with it. The per-arm
            // lines below are logged AS EACH ARM FINISHES for exactly that
            // reason: an hour of measurement must not live or die by one
            // format string at the end of it.
            log.Add(string.Format("{0,-30} {1,5} {2,9} {3,9} {4,5} {5,5} {6,6} {7,13} {8,6}",
                                  "arm", "bout", "BOTH", "either", "shed", "hpK", "struct",
                                  "left w/ hp", "meanHp"));
            foreach (var r in results)
                log.Add(string.Format("{0,-30} {1,5} {2,9} {3,9} {4,5} {5,5} {6,6} {7,13} {8,6}",
                    r.name, r.bouts,
                    r.bouts == 0 ? "-" : r.both + " (" + Mathf.RoundToInt(100f * r.both / r.bouts) + "%)",
                    r.bouts == 0 ? "-" : r.either + " (" + Mathf.RoundToInt(100f * r.either / r.bouts) + "%)",
                    r.shed, r.hpKilled, r.bodyLost,
                    r.wpnLost == 0 ? "-" : r.leftWithHp + "/" + r.wpnLost
                        + " (" + Mathf.RoundToInt(100f * r.leftWithHp / r.wpnLost) + "%)",
                    r.leftWithHp == 0 ? "-" : Mathf.RoundToInt(r.hpSum / r.leftWithHp) + "%"));
            log.Add("");
            log.Add("BOTH   = bouts where both sides ended with no weapon — the 43% number");
            log.Add("shed   = weapons that LEFT via a seam (structure failed under them)");
            log.Add("hpKill = weapons actually beaten on hit points");
            log.Add("struct = non-weapon parts destroyed");
            log.Add("");
            log.Add("left w/ hp = weapons that left with hit points still in them (the 69%)");
            log.Add("");
            log.Add("A lever is working if BOTH falls AND 'left w/ hp' falls. A lever that drops");
            log.Add("BOTH while 'left w/ hp' holds has changed something else and wants");
            log.Add("explaining before it is believed."); 

            bool bayOk = bm.SnapshotString() == ownerBay;
            log.Add("owner's bay restored byte-identical: " + bayOk);

            finished = true;
            Debug.Log("[DisarmLever] " + Report());
        }
    }
}
#endif
