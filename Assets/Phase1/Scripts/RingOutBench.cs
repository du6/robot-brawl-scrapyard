#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace RobotBrawl.Phase0
{
    /// <summary>Does a bot thrown out of the arena actually end the fight?
    ///
    /// owen, 2026-08-20: his flipper threw Spinner1 clean out and the fight did
    /// not stop — 1:01 still on the clock, both bots healthy, the view stuck on
    /// an empty arena. Cause: NOTHING in the whole fight watched height or
    /// bounds. The floor is a Plane covering the arena and nothing else, so a
    /// bot that clears the 6 m barrier falls forever; falling it is neither
    /// flipped-and-slow nor immobile, so `Incap()` never sees it, no count-out
    /// starts, and only the timer can end the match — as a JUDGES' decision.
    ///
    /// ⚠ THIS BENCH DRIVES A REAL MATCH, NOT THE PREDICATE. Testing
    /// `OutOfArena` as arithmetic would have passed on the broken build too:
    /// the rule was never wrong, it did not EXIST, and a seam-level test cannot
    /// tell those apart. This runs MatchRunner, waits for a live fight, shoves
    /// one side under the floor and asserts the REFEREE stopped — which is the
    /// half that was actually missing. Same lesson as the WATCH button.
    ///
    /// ⚠ Hard rule 5: FightManager.End() calls Progression.OnMatchEnd
    /// unconditionally, so this holds autosave down for the whole run.</summary>
    public class RingOutBench : MonoBehaviour
    {
        public static RingOutBench Run()
        {
            return new GameObject("ringout_bench").AddComponent<RingOutBench>();
        }

        public int passed, failed;
        public bool finished;
        public static string failLines = "";
        readonly List<string> log = new List<string>();

        void Check(bool ok, string what)
        {
            if (ok) { passed++; log.Add("PASS  " + what); }
            else { failed++; log.Add("FAIL  " + what); }
        }
        void Note(string s) { log.Add("      " + s); }

        /// <summary>Which side to shove out, and what the referee must then say.</summary>
        enum Victim { Enemy, Player, Both }

        IEnumerator Start()
        {
            yield return null;

            var bm = Object.FindFirstObjectByType<BuilderManager>();
            if (bm == null || bm.PaletteCount <= 0)
            {
                if (bm != null) Destroy(bm.gameObject);
                yield return null;
                bm = new GameObject("BuilderManager").AddComponent<BuilderManager>();
                Note("created a headless BuilderManager (Main.unity carries none)");
                yield return null;
            }

            bool savedAuto = Career.autosave;
            Career.autosave = false;

            int n = 0;
            for (int w = 0; w < 120 && n <= 0; w++)
            { n = bm.LoadSnapshot(VerbBench.ARMED); if (n <= 0) yield return null; }
            Check(n > 0, "fixture rig loads (" + n + " parts)");
            if (n <= 0) { Career.autosave = savedAuto; Finish(); yield break; }

            // ExportRaw, not Export: the latter takes a RobotProgram object.
            // Both sides are the SAME rig on purpose — this bench is about the
            // referee, and identical fighters keep the fixture from deciding
            // the bout before the shove lands.
            string rig = bm.SnapshotString();
            var envA = RobotSnapshot.ExportRaw("RingA", rig, "");
            var envB = RobotSnapshot.ExportRaw("RingB", rig, "");

            yield return Leg(envA, envB, Victim.Enemy,
                "the side thrown out LOSES", "PlayerWin");
            yield return Leg(envA, envB, Victim.Player,
                "and it is symmetric — throwing YOURSELF out loses", "PlayerLoss");
            yield return Leg(envA, envB, Victim.Both,
                "both out at once is a draw, not a race", "Draw");

            Career.autosave = savedAuto;
            Finish();
        }

        /// <summary>One bout: start it, wait for a live fight, put `who` under
        /// the floor, and read what the referee decided.</summary>
        IEnumerator Leg(SnapshotEnvelope a, SnapshotEnvelope b, Victim who,
                        string claim, string wantOutcome)
        {
            MatchRunner.MatchResult res = null;
            var mr = MatchRunner.Run(a, b, new[] { 4242 },
                                     "ringout_" + who, 10f, false,
                                     r => res = r);

            bool shoved = false;
            float t0 = Time.realtimeSinceStartup;
            while (res == null && Time.realtimeSinceStartup - t0 < 180f)
            {
                if (!shoved)
                {
                    var fm = Object.FindFirstObjectByType<FightManager>();
                    // Wait for a fight that is actually RUNNING — shoving during
                    // Settling would be testing the spawn, not the referee.
                    if (fm != null && fm.state == FightManager.State.Fighting
                        && fm.player.bot != null && fm.enemy.bot != null
                        && fm.player.bot.rb != null && fm.enemy.bot.rb != null)
                    {
                        // Straight down, well clear of the floor: this is the
                        // state a thrown bot reaches about a second into its
                        // fall, reproduced without needing a throw that big.
                        var under = new Vector3(0f, -12f, 0f);
                        if (who == Victim.Enemy || who == Victim.Both)
                            fm.enemy.bot.rb.position = under;
                        if (who == Victim.Player || who == Victim.Both)
                            fm.player.bot.rb.position = under + new Vector3(2f, 0f, 0f);
                        shoved = true;
                        Note(who + ": put under the floor at t=" + fm.timer.ToString("F1") + " left");
                    }
                }
                yield return null;
            }

            if (mr != null) Destroy(mr.gameObject);

            if (res == null || res.bouts.Count == 0)
            { Check(false, claim + " — bout never returned a result"); yield break; }
            if (!shoved)
            { Check(false, claim + " — never reached a live fight to shove"); yield break; }

            var bout = res.bouts[0];
            Note("outcome=" + bout.outcome + "  cause=" + bout.cause
                 + "  simSeconds=" + bout.simSeconds.ToString("F1"));

            Check(bout.outcome == wantOutcome, claim + " (got " + bout.outcome + ")");
            // ⚠ THE CAUSE MATTERS AS MUCH AS THE OUTCOME. Before the fix this
            // bout still ended — at the timer, by judges — so an outcome-only
            // assertion could pass on the broken build. "Ring-out" is what
            // proves the new rule fired rather than the clock running out.
            Check(bout.cause != null && bout.cause.Contains("Ring-out")
                  || wantOutcome == "Draw" && bout.cause != null && bout.cause.Contains("left the arena"),
                  "…and the referee says so, rather than reaching the judges");
            // A ring-out is INSTANT. The fixture bout runs ~90 s to judges; if
            // this took that long the rule did not fire, whatever the label.
            Check(bout.simSeconds < 60f,
                  "…and it ends immediately, not at the final bell (" + bout.simSeconds.ToString("F1") + " s)");
        }

        void Finish()
        {
            failLines = "";
            foreach (var l in log) if (l.StartsWith("FAIL")) failLines += l + "\n";
            var sb = new StringBuilder();
            foreach (var l in log) sb.Append(l).Append('\n');
            sb.Append("===== passed ").Append(passed).Append("  failed ").Append(failed).Append(" =====");
            Debug.Log("[RingOut] " + sb);
            finished = true;
        }
    }
}
#endif
