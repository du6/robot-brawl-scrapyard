// ===========================================================================
// DisarmBench.cs — WHY does the ladder disarm both sides in ~43% of bouts?
//
//   RobotBrawl.Phase0.DisarmBench.Run();     // play mode; poll .finished
//
// THE QUESTION, AND WHY THE OBVIOUS ANSWER IS ALREADY RULED OUT.
//
// `Ladder_Sweep_Weapon_Trade_2026-08-08.md` found weapons were the most
// fragile things in the game: 15/30 bouts ended with BOTH sides disarmed, and
// every mirror match was a deterministic mutual kill in four hits. The fix
// was `DamageResolver.WEAPON_VS_WEAPON = 0.25` — two hardened edges meeting
// each other became a glancing exchange instead of a mutual kill.
//
// It moved the ladder by ZERO BOUTS. 13/30 before, 13/30 after; 26/30
// either-disarmed before and after. The opening-ram fix, which took the
// TUTORIAL's disarm from 42% to 0%, also moved it by zero. Three cheap
// explanations are closed (`SESSION_HANDOVER_2026-08-09_evening.md`).
//
// THE HYPOTHESIS THIS BENCH EXISTS TO TEST, written before the first run:
//
//     WEAPON_VS_WEAPON only applies when BOTH sides of the contact are edges.
//     If the ladder's weapons are mostly being knocked off by NON-edge
//     contact — a chassis, a beam, a ram — then that multiplier could never
//     have helped, and its null result is not a mystery but a measurement of
//     the wrong thing.
//
//     Predicted: MOST weapon losses on the ladder have a non-edge attacker.
//
// A prediction that can fail is the point (hard rule 6). If most losses turn
// out to BE weapon-on-weapon, the hypothesis is dead and the real finding is
// that 0.25 was simply not low enough — which is equally useful and equally
// unavailable from the outside.
//
// WHY THE EXISTING BENCHES CANNOT ANSWER IT. LadderSweepBench reports
// `aWeaponsAlive` at the bell and OpeningBench reports "weapons left" at a
// cutoff. Both are the OUTCOME. Neither records what struck what, so neither
// can distinguish a weapon traded against another weapon from a weapon
// swatted off by a chassis. Measuring the EVENT rather than the outcome is
// exactly what took the opening disarm from 42% to 0%, and it is the standing
// instruction in the handover.
//
// This is a MEASUREMENT bench. It asserts only things that are broken
// regardless of balance — a bout that never simulated, a match that errored.
// The balance call is owen's and gets made from the table.
//
// Same 15 pairings x 2 seeds as LadderSweepBench, and the same one-chassis
// fixture, so its numbers are comparable to that bench's line by line.
// ===========================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace RobotBrawl.Phase0
{
    public class DisarmBench : MonoBehaviour
    {
        public static DisarmBench Run()
        {
            return new GameObject("disarm_bench").AddComponent<DisarmBench>();
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
        static string Pct(int a, int b) { return b == 0 ? "n/a" : (100f * a / b).ToString("F0") + "%"; }

        class Preset
        {
            public string name;
            public RobotProgram prog;
            public Preset(string n, RobotProgram p) { name = n; prog = p; }
        }

        static readonly int[] SEEDS = { 101, 202 };

        // ---- what the hook collects ---------------------------------------
        // Counts only; no per-bout grouping, deliberately. The question is
        // "what kills weapons", which is answered by classifying every loss.
        // Attributing losses to bouts would need a bout-boundary callback that
        // does not exist, and inventing one from timestamps would be a guess
        // dressed as data.
        int wpnLost, wpnLostByEdge, wpnLostByRam, wpnLostByLimb;
        int bodyLost, bodyLostByEdge;
        readonly Dictionary<string, int> wpnLostByVictimPart = new Dictionary<string, int>();
        readonly Dictionary<string, int> wpnLostByAttackerKind = new Dictionary<string, int>();

        void Bump(Dictionary<string, int> d, string k)
        {
            int v; d.TryGetValue(k, out v); d[k] = v + 1;
        }

        void OnPartDestroyed(object victim, object attacker, object part,
                             float attackerHardness, int src)
        {
            // PartSpec is a STRUCT — it is never null, so only the Part itself
            // is worth checking.
            var p = part as CompoundRobot.Part;
            if (p == null) return;

            bool victimIsEdge   = DamageResolver.IsEdge(p.spec.edgeHardness);
            bool attackerIsEdge = DamageResolver.IsEdge(attackerHardness);

            if (!victimIsEdge)
            {
                bodyLost++;
                if (attackerIsEdge) bodyLostByEdge++;
                return;
            }

            wpnLost++;
            if (attackerIsEdge) wpnLostByEdge++;
            if (src == DamageResolver.SRC_LIMB) wpnLostByLimb++; else wpnLostByRam++;
            Bump(wpnLostByVictimPart, p.spec.id);
            // The attacker's hardness bucketed into the only distinction the
            // damage rule actually makes.
            Bump(wpnLostByAttackerKind,
                 attackerIsEdge ? "edge (h=" + attackerHardness.ToString("F2") + ")"
                                : "structure (h=" + attackerHardness.ToString("F2") + ")");
        }

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
            // Hard rule 5. FightManager.End() calls Progression.OnMatchEnd
            // unconditionally, so anything that runs a fight headlessly must
            // isolate the career or it writes owen's save.
            bool savedAuto = Career.autosave;
            Career.autosave = false;

            int n = bm.LoadSnapshot(VerbBench.ARMED);
            Check(n > 0, "fixture rig loads (" + n + " parts, one chassis both sides)");
            if (n <= 0) { Career.autosave = savedAuto; Finish(); yield break; }
            yield return null;

            var envs = new Dictionary<string, SnapshotEnvelope>();
            foreach (var p in presets) envs[p.name] = RobotSnapshot.Export(bm, p.name, p.prog);

            // Subscribe for the duration of the sweep only, and restore the
            // delegate afterwards — a bench that leaves a hook attached
            // changes the next thing that runs in this play session.
            var savedHook = DamageResolver.OnPartDestroyed;
            DamageResolver.OnPartDestroyed = OnPartDestroyed;

            // The engine already keeps a ledger of every part that LEAVES a
            // body and why. Turning it on is what separates "the weapon was
            // destroyed" from "the weapon fell off because the arm holding it
            // was" — and only the first is something WEAPON_VS_WEAPON can
            // reach. Off by default, so this costs a shipped build nothing.
            bool savedDetachLog = CompoundRobot.detachLogOn;
            CompoundRobot.detachLogOn = true;
            CompoundRobot.detachLog.Clear();

            int bouts = 0, boutsBothDisarmed = 0, boutsEitherDisarmed = 0;

            for (int i = 0; i < presets.Count; i++)
            for (int j = i; j < presets.Count; j++)
            {
                string an = presets[i].name, bn = presets[j].name;
                MatchRunner.MatchResult res = null;
                var mr = MatchRunner.Run(envs[an], envs[bn], SEEDS,
                                         "disarm_" + an + "_" + bn, 10f, false,
                                         r => { res = r; });
                mr.stopWhenDecided = false;
                float t0 = Time.realtimeSinceStartup;
                while (res == null && Time.realtimeSinceStartup - t0 < 400f) yield return null;
                if (mr != null) Destroy(mr.gameObject);

                if (res == null) { Check(false, an + " vs " + bn + " completed"); continue; }
                if (!res.Ok) { Check(false, an + " vs " + bn + " ran clean: " + res.error); continue; }

                foreach (var b in res.bouts)
                {
                    bouts++;
                    bool aDis = b.aWeaponsAlive == 0, bDis = b.bWeaponsAlive == 0;
                    if (aDis && bDis) boutsBothDisarmed++;
                    if (aDis || bDis) boutsEitherDisarmed++;
                    Check(b.simSeconds > 0.5f, an + " v " + bn + " seed " + b.seed + " simulated");
                }
            }

            DamageResolver.OnPartDestroyed = savedHook;

            // ---- how weapons actually LEAVE ------------------------------
            // The count above only sees weapons killed by HP. This sees every
            // weapon that left a body by any route, which is the number that
            // has to match the disarm rate.
            int wLeft = 0, wHp = 0, wStruct = 0, wSeam = 0, wOther = 0;
            int wLeftWithHp = 0; float hpSum = 0f;
            var causeCount = new Dictionary<string, int>();
            foreach (var line in CompoundRobot.detachLog)
            {
                if (!line.EndsWith("|EDGE")) continue;
                wLeft++;
                var f = line.Split('|');
                string cause = f.Length > 2 ? f[2] : "?";
                string key = cause.StartsWith("SEAM") ? "SEAM SHEARED" : cause;
                Bump(causeCount, key);
                if (cause.StartsWith("HP-DESTROYED")) wHp++;
                else if (cause.StartsWith("STRUCTURAL")) wStruct++;
                else if (cause.StartsWith("SEAM")) wSeam++;
                else wOther++;
                // "(NN%)" is the fourth field; a weapon that left with HP in
                // hand was never beaten, it was dropped.
                if (f.Length > 3)
                {
                    int o = f[3].IndexOf('('), c = f[3].IndexOf('%');
                    float pctHp;
                    if (o >= 0 && c > o &&
                        float.TryParse(f[3].Substring(o + 1, c - o - 1), out pctHp) && pctHp > 1f)
                    { wLeftWithHp++; hpSum += pctHp; }
                }
            }

            // ---- the table ------------------------------------------------
            log.Add("");
            Note("bouts run: " + bouts + " over " + SEEDS.Length + " seeds x 15 pairings");
            Note("bouts ending with BOTH sides disarmed:   " + boutsBothDisarmed + "/" + bouts
                 + "  (" + Pct(boutsBothDisarmed, bouts) + ")   <- the 43% under investigation");
            Note("bouts ending with EITHER side disarmed:  " + boutsEitherDisarmed + "/" + bouts
                 + "  (" + Pct(boutsEitherDisarmed, bouts) + ")");
            log.Add("");
            log.Add("  ---- WHAT ACTUALLY KILLS A WEAPON --------------------------------");
            Note("weapon parts destroyed, total ..... " + wpnLost);
            Note("  by an EDGE attacker ............. " + wpnLostByEdge
                 + "  (" + Pct(wpnLostByEdge, wpnLost) + ")   <- the only case WEAPON_VS_WEAPON touches");
            Note("  by STRUCTURE (chassis/beam/ram) .. " + (wpnLost - wpnLostByEdge)
                 + "  (" + Pct(wpnLost - wpnLostByEdge, wpnLost) + ")   <- untouched by that lever");
            Note("  via SRC_LIMB ..................... " + wpnLostByLimb
                 + "  (" + Pct(wpnLostByLimb, wpnLost) + ")");
            Note("  via SRC_RAM ...................... " + wpnLostByRam
                 + "  (" + Pct(wpnLostByRam, wpnLost) + ")");
            log.Add("");
            Note("non-weapon parts destroyed ........ " + bodyLost
                 + "  (of which " + bodyLostByEdge + " by an edge)");

            if (wpnLostByVictimPart.Count > 0)
            {
                log.Add("");
                log.Add("  which weapon part dies:");
                foreach (var kv in wpnLostByVictimPart)
                    Note("    " + kv.Key.PadRight(16) + kv.Value);
            }
            if (wpnLostByAttackerKind.Count > 0)
            {
                log.Add("");
                log.Add("  what struck it:");
                foreach (var kv in wpnLostByAttackerKind)
                    Note("    " + kv.Key.PadRight(28) + kv.Value);
            }

            log.Add("");
            if (wpnLost == 0)
            {
                // A sweep with no weapon losses at all is not a clean bill of
                // health, it is a broken bench — say so rather than printing
                // a tidy table of zeros.
                Check(false, "the sweep destroyed at least one weapon (it destroyed none — "
                           + "the hook is not wired, or the fixture has no weapons)");
            }
            else
            {
                Check(true, "the sweep destroyed " + wpnLost + " weapon part(s) to classify");
                Note("PREDICTION 1 (most weapon losses have a NON-edge attacker): "
                     + ((wpnLost - wpnLostByEdge) * 2 > wpnLost ? "HELD" : "REFUTED")
                     + " — " + Pct(wpnLostByEdge, wpnLost) + " of HP kills came from an edge.");
                log.Add("");
                log.Add("  ---- BUT THAT IS NOT HOW MOST WEAPONS ARE LOST -------------------");
                Note("weapon parts that LEFT a body, by any route ... " + wLeft);
                foreach (var kv in causeCount)
                    Note("    " + kv.Key.PadRight(20) + kv.Value + "  (" + Pct(kv.Value, wLeft) + ")");
                Note("weapons that left while STILL HAVING HP ....... " + wLeftWithHp
                     + "  (" + Pct(wLeftWithHp, wLeft) + ")"
                     + (wLeftWithHp > 0 ? ", mean " + F(hpSum / wLeftWithHp) + "% hp remaining" : ""));
                log.Add("");
                // THE ACTUAL QUESTION. WEAPON_VS_WEAPON scales HP damage, so
                // it can only ever save a weapon that dies of HP. Every weapon
                // that leaves with hit points still in it was never beaten —
                // it was dropped, because the thing carrying it failed.
                bool mostlyNotHp = wLeft > 0 && wHp * 2 < wLeft;
                Note("PREDICTION 2 (most weapons are lost WITHOUT being beaten on HP): "
                     + (mostlyNotHp ? "HELD" : "REFUTED"));
                Note(mostlyNotHp
                    ? "  => WEAPON_VS_WEAPON only scales HP DAMAGE, so it cannot reach "
                      + Pct(wLeft - wHp, wLeft) + " of these losses at ANY value. That is why "
                      + "0.25 moved the ladder by zero bouts, and why lowering it further will "
                      + "not help either. The weapon is not being beaten — it is being dropped "
                      + "when the limb carrying it fails."
                    : "  => most weapons really are beaten on HP, so the lever is right and "
                      + "the value is not.");
            }

            CompoundRobot.detachLogOn = savedDetachLog;
            Career.autosave = savedAuto;
            if (!string.IsNullOrEmpty(ownerBuild)) bm.LoadSnapshot(ownerBuild);
            Finish();
        }

        void Finish()
        {
            var sb = new StringBuilder();
            foreach (var l in log) { Debug.Log("[DisarmBench] " + l); sb.Append(l).Append('\n'); }
            report = sb.ToString();
            Debug.Log("[DisarmBench] ===== passed " + passed + " failed " + failed + " =====");
            try
            {
                File.WriteAllText(Application.dataPath + "/Phase1/qa_disarm_bench.txt",
                                  "passed " + passed + " failed " + failed + "\n" + report);
            }
            catch { }
            finished = true;
        }
    }
}
