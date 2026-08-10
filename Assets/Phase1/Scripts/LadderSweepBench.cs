// ⚠ EDITOR AND DEVELOPMENT BUILDS ONLY — 2026-08-10.
// This is a QA harness. It was compiling into the SHIPPED iOS player: 44 such
// files, ~13k lines, and the string "dev-only-worker-key" was verified present
// in build/ios global-metadata.dat. Dead weight in the binary, and a surface
// that can register accounts and start fights inside a player's app.
// Nothing in the product references any harness — checked against every
// product script, every scene and every prefab before this guard was added.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
// ===========================================================================
// LadderSweepBench.cs — Multiplayer v3: does the mirror-match disarm
// generalise, or was it one matchup?
//
// M0's first real match (Brawler vs WallShy, three seeds) came back as three
// draws in which both robots destroyed each other's weapon in the first five
// seconds and then shoved for eighty-five. One matchup is an anecdote. The
// house rule (CriticLoop6) is: where a quality is measurable, measure it over
// EVERYTHING rather than over a named list. So this runs every unordered
// pairing of the shipped presets over two seeds and reports, per bout:
//
//   contact      how many damage exchanges, and when the first and last landed
//   dead air     sim seconds after the last hit — the part nobody wants to watch
//   attrition    parts lost per side, and WEAPONS still alive at the bell
//
// It is a MEASUREMENT, not an assertion bench: the only things it fails on are
// things that are broken regardless of balance (a bout that never simulated, a
// match that errored). Balance is owen's call and gets made from the table,
// not from a threshold I invented.
//
// SCOPE, STATED HONESTLY: one chassis (VerbBench.ARMED) on both sides, so the
// only variable is the PROGRAM. That is the right first cut — a program cannot
// change part geometry, so if the weapon trade happens across every program
// pairing, the trade is structural and no amount of programming avoids it.
// Varying the chassis is the next sweep, not this one.
// ===========================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace RobotBrawl.Phase0
{
    public class LadderSweepBench : MonoBehaviour
    {
        public static LadderSweepBench Run()
        {
            return new GameObject("ladder_sweep_bench").AddComponent<LadderSweepBench>();
        }

        public int passed, failed;
        public bool finished;
        public string report = "";

        readonly List<string> log = new List<string>();
        void Check(bool ok, string what)
        {
            if (ok) { passed++; log.Add("PASS  " + what); }
            else { failed++; log.Add("FAIL  " + what); }
        }
        void Note(string s) { log.Add("      " + s); }
        static string F(float v) { return v.ToString("F1"); }

        class Preset
        {
            public string name;
            public RobotProgram prog;
            public Preset(string n, RobotProgram p) { name = n; prog = p; }
        }

        static readonly int[] SEEDS = { 101, 202 };

        IEnumerator Start()
        {
            var bm = UnityEngine.Object.FindFirstObjectByType<BuilderManager>();
            if (bm == null) { Check(false, "BuilderManager in scene"); Finish(); yield break; }

            var presets = new List<Preset>
            {
                new Preset("Brawler", RobotProgram.Brawler()),
                new Preset("WallShy", RobotProgram.WallShy()),
                new Preset("Matador", RobotProgram.Matador()),
                new Preset("Rusher",  RobotProgram.Rusher()),
                new Preset("Statue",  RobotProgram.Statue()),
            };

            string ownerBuild = bm.SnapshotString();
            bool savedAuto = Career.autosave;
            Career.autosave = false;

            int n = bm.LoadSnapshot(VerbBench.ARMED);
            Check(n > 0, "fixture rig loads (" + n + " parts, one chassis both sides)");
            if (n <= 0) { Finish(); yield break; }
            yield return null;

            var envs = new Dictionary<string, SnapshotEnvelope>();
            foreach (var p in presets) envs[p.name] = RobotSnapshot.Export(bm, p.name, p.prog);

            // aggregate accumulators
            int bouts = 0, boutsNoContact = 0, boutsBothDisarmed = 0, boutsEitherDisarmed = 0;
            float deadAirSum = 0f, lastHitSum = 0f, simSum = 0f;
            int hitsSum = 0;
            var rows = new List<string>();

            log.Add("");
            log.Add("  matchup                    seed  verdict   hits  1st    last   deadair  lost A/B  wpn A/B");
            log.Add("  ---------------------------------------------------------------------------------------");

            for (int i = 0; i < presets.Count; i++)
            for (int j = i; j < presets.Count; j++)
            {
                string an = presets[i].name, bn = presets[j].name;
                MatchRunner.MatchResult res = null;
                var mr = MatchRunner.Run(envs[an], envs[bn], SEEDS,
                                         "sweep_" + an + "_" + bn, 10f, false,
                                         r => { res = r; });
                mr.stopWhenDecided = false;   // we want every seed's data, not a verdict
                float t0 = Time.realtimeSinceStartup;
                while (res == null && Time.realtimeSinceStartup - t0 < 400f) yield return null;
                if (mr != null) Destroy(mr.gameObject);

                if (res == null) { Check(false, an + " vs " + bn + " completed"); continue; }
                if (!res.Ok) { Check(false, an + " vs " + bn + " ran clean: " + res.error); continue; }

                foreach (var b in res.bouts)
                {
                    bouts++;
                    hitsSum += b.hits;
                    simSum += b.simSeconds;
                    if (b.hits == 0) boutsNoContact++;
                    else { deadAirSum += b.DeadAir; lastHitSum += b.lastHitT; }
                    bool aDis = b.aWeaponsAlive == 0, bDis = b.bWeaponsAlive == 0;
                    if (aDis && bDis) boutsBothDisarmed++;
                    if (aDis || bDis) boutsEitherDisarmed++;

                    string row = string.Format(
                        "  {0,-10} v {1,-10}  {2,4}  {3,-7}  {4,3}  {5,5}  {6,5}  {7,6}   {8}/{9}      {10}/{11}",
                        an, bn, b.seed, b.winner, b.hits,
                        b.firstHitT < 0f ? "  -" : F(b.firstHitT),
                        b.lastHitT < 0f ? "  -" : F(b.lastHitT),
                        F(b.DeadAir), b.aPartsLost, b.bPartsLost,
                        b.aWeaponsAlive, b.bWeaponsAlive);
                    rows.Add(row);
                    log.Add(row);

                    Check(b.simSeconds > 0.5f, an + " v " + bn + " seed " + b.seed + " simulated");
                }
            }

            log.Add("  ---------------------------------------------------------------------------------------");
            log.Add("");
            Note("bouts run: " + bouts + " over " + SEEDS.Length + " seeds x 15 pairings");
            if (bouts > 0)
            {
                Note("mean hits per bout ......... " + (hitsSum / (float)bouts).ToString("F1"));
                Note("mean bout length .......... " + F(simSum / bouts) + " s");
                int withContact = bouts - boutsNoContact;
                if (withContact > 0)
                {
                    Note("mean LAST hit at .......... " + F(lastHitSum / withContact) + " s");
                    Note("mean DEAD AIR ............. " + F(deadAirSum / withContact) + " s  (" +
                         F(100f * (deadAirSum / withContact) / (simSum / bouts)) + "% of the bout)");
                }
                Note("bouts with NO contact at all: " + boutsNoContact + "/" + bouts);
                Note("bouts ending with BOTH sides disarmed: " + boutsBothDisarmed + "/" + bouts +
                     "  (" + F(100f * boutsBothDisarmed / bouts) + "%)");
                Note("bouts ending with EITHER side disarmed: " + boutsEitherDisarmed + "/" + bouts +
                     "  (" + F(100f * boutsEitherDisarmed / bouts) + "%)");
            }

            Career.autosave = savedAuto;
            if (!string.IsNullOrEmpty(ownerBuild)) bm.LoadSnapshot(ownerBuild);
            Finish();
        }

        void Finish()
        {
            var sb = new StringBuilder();
            foreach (var l in log) { Debug.Log("[LadderSweep] " + l); sb.Append(l).Append('\n'); }
            report = sb.ToString();
            Debug.Log("[LadderSweep] ===== passed " + passed + " failed " + failed + " =====");
            try
            {
                File.WriteAllText(Application.dataPath + "/Phase1/qa_ladder_sweep.txt",
                                  "passed " + passed + " failed " + failed + "\n" + report);
            }
            catch { }
            finished = true;
        }
    }
}
#endif
