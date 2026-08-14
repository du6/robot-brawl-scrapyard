using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RobotBrawl.Phase0
{
    /// <summary>THE LOAD-BEARING ASSUMPTION, MEASURED (launch audit round 2,
    /// 2026-08-14). Live arena fights show the player an ending computed on
    /// their device; the Linux worker referees the same seed for settlement.
    /// The whole design assumes those two fights END THE SAME WAY — and Unity
    /// physics is not promised to be bit-identical across Apple arm64 and
    /// Linux x86. This probe is the measurement: N real challenges against
    /// the dev ladder's house robots, each fought LOCALLY at the device's
    /// speed 1 with the server's seed, then compared against the cloud
    /// referee's settled verdict. Anything under N/N agreement is a
    /// launch-blocking finding, not a tuning note.
    ///
    /// Dev ladder only (registers an account, stakes scrap, fights real
    /// matches). Run: DeterminismProbe.Run() in play mode.</summary>
    public class DeterminismProbe : MonoBehaviour
    {
        public static bool finished;
        public static string report = "";
        const int FIGHTS = 6;

        public static void Run()
        {
            finished = false; report = "";
            var go = new GameObject("DeterminismProbe");
            go.AddComponent<DeterminismProbe>().StartCoroutine(go.GetComponent<DeterminismProbe>().All());
        }

        /// <summary>Batch 2 (set before Run): a heavier FEATHER bruiser that
        /// should BEAT the ABS house robots — the first batch's mirror-ish
        /// matchup drew six times out of six, which agreed on the judges'
        /// margin arithmetic but compared no decisive verdicts.</summary>
        public static bool strongMode;

        // HouseSeed's proven-legal chassis in light materials: a fair FEATHER
        // fighter, so wins AND losses both occur and both verdict directions
        // get compared. strongMode swaps the structure to Titanium — still
        // FEATHER, near double the mass and armour of the ABS house pair.
        static string Build()
        {
            string s = strongMode ? "Titanium" : "ABS";
            return
              "core|0.000,0.700,0.000|0|0.00,0.00,0.00|Aluminum\n"
            + "beam|-0.250,0.700,0.000|0|0.00,0.00,0.00|" + s + "\n"
            + "beam|-0.250,0.700,-0.600|0|0.00,0.00,0.00|" + s + "\n"
            + "beam|0.250,0.700,0.000|0|0.00,0.00,0.00|" + s + "\n"
            + "beam|0.250,0.700,-0.600|0|0.00,0.00,0.00|" + s + "\n"
            + "wheel|0.420,0.700,0.150|0|1.00,0.00,0.00|Rubber\n"
            + "wheel|0.420,0.700,-0.750|0|1.00,0.00,0.00|Rubber\n"
            + "wheel|-0.420,0.700,0.150|0|-1.00,0.00,0.00|Rubber\n"
            + "wheel|-0.420,0.700,-0.750|0|-1.00,0.00,0.00|Rubber\n"
            + "beam|0.000,0.700,-0.450|0|0.00,0.00,0.00|Aluminum\n"
            + "battery|0.000,0.925,-0.450|0|0.00,0.00,0.00|Aluminum\n"
            + "spindle|0.000,1.000,0.000|0|0.00,1.00,0.00|Aluminum\n"
            + "beam|0.000,1.250,0.000|0|0.00,0.00,0.00|" + s + "\n"
            + "beamlong|0.000,1.250,0.800|0|0.00,0.00,0.00|Aluminum\n"
            + "spike|0.000,1.030,1.370|0|0.00,0.00,1.00|Steel\n";
        }

        IEnumerator All()
        {
            var log = new List<string>();
            Action<string> say = s => { log.Add(s); Debug.Log("[DeterminismProbe] " + s); };

            if (LadderClient.IsProduction)
            { say("REFUSED: production"); Finish(log); yield break; }
            say("server: " + LadderClient.BaseUrl);

            string savedToken = LadderClient.Token;
            string tag = DateTime.UtcNow.ToString("HHmmss");
            string err = null;

            yield return LadderClient.Register("determinism-" + tag + "@example.com",
                "probe-password-long", "Determinism", (who, e) => err = e);
            if (err != null) { say("FAIL register: " + err); Restore(savedToken, log); yield break; }

            var env = RobotSnapshot.ExportRaw("Probe-" + tag,
                BuilderManager.SNAP_STAMP + "\n" + Build(),
                RobotProgram.FirstSteps().ToJson());
            string snapId = null;
            yield return LadderClient.Enlist("Probe-" + tag, env, (id, e) => { snapId = id; err = e; });
            if (err != null) { say("FAIL enlist: " + err); Restore(savedToken, log); yield break; }

            // The dev worker validates in seconds; poll until ACTIVE.
            string mySnap = null;
            for (int w = 0; w < 30 && mySnap == null; w++)
            {
                yield return new WaitForSeconds(2f);
                yield return LadderClient.MyRobots((rows, e) =>
                {
                    if (e == null) foreach (var m in rows)
                        if (m.CanFight) mySnap = m.activeSnapshotId;
                });
            }
            if (mySnap == null) { say("FAIL: robot never validated"); Restore(savedToken, log); yield break; }
            say("probe robot ACTIVE");

            // The FEATHER house robots are the opponents; alternate them.
            var defenders = new List<LadderEntry>();
            yield return LadderClient.Leaderboard("FEATHER", (rows, e) =>
            {
                if (e == null) foreach (var b in rows)
                    if (b.robotName.StartsWith("HOUSE") && !string.IsNullOrEmpty(b.activeSnapshotId))
                        defenders.Add(b);
            });
            if (defenders.Count == 0) { say("FAIL: no house robots on FEATHER"); Restore(savedToken, log); yield break; }

            var matchIds = new List<string>();
            var localVerdicts = new List<string>();   // WON / LOST / DRAW, my POV
            for (int i = 0; i < FIGHTS; i++)
            {
                var d = defenders[i % defenders.Count];
                string matchId = null; int[] seeds = null;
                yield return LadderClient.Challenge(mySnap, d.activeSnapshotId,
                    (id, stake, sds, e) => { matchId = id; seeds = sds; err = e; });
                if (matchId == null) { say("fight " + i + ": challenge refused: " + err + " — stopping here"); break; }

                SnapshotEnvelope you = null; string oppName = null, oppBuild = null;
                yield return LadderClient.MatchEnvelopes(matchId, (mine, on, ob, e) =>
                    { you = mine; oppName = on; oppBuild = ob; err = e; });
                if (you == null) { say("fight " + i + ": envelopes: " + err); break; }
                var df = RobotSnapshot.ExportRaw(oppName, oppBuild, "");

                // DEVICE-FAITHFUL: speed 1, exactly what an iPad runs. NOTE: as
                // of the launch-audit fix the preview runs the opponent on AI
                // (its program is secret), so this is no longer a determinism
                // test of the SAME fight — it is a preview-runs-and-settles
                // smoke. The wall-cap fix is what it now really exercises: a
                // full-distance preview must reach a real verdict, not a
                // timeout draw.
                bool done = false; MatchRunner.MatchResult res = null;
                var mr = MatchRunner.Run(you, df, seeds, matchId, 1f, false, r => { res = r; done = true; });
                float t0 = Time.realtimeSinceStartup;
                while (!done && Time.realtimeSinceStartup - t0 < 240f) yield return null;
                string local = res == null || !string.IsNullOrEmpty(res.error) ? "ERROR"
                    : res.aWins > res.bWins ? "WON"
                    : res.bWins > res.aWins ? "LOST" : "DRAW";
                matchIds.Add(matchId);
                localVerdicts.Add(local);
                say("fight " + i + " vs " + d.robotName + " seed " + (seeds != null && seeds.Length > 0 ? seeds[0].ToString() : "?")
                    + " -> local " + local);
            }

            // Now the referee. Poll the inbox until every match settles.
            say("waiting for the cloud referee (" + matchIds.Count + " matches)…");
            var refVerdicts = new Dictionary<string, string>();
            float deadline = Time.realtimeSinceStartup + 1500f;
            while (refVerdicts.Count < matchIds.Count && Time.realtimeSinceStartup < deadline)
            {
                yield return new WaitForSeconds(15f);
                yield return LadderClient.Inbox((rows, e) =>
                {
                    if (e == null) foreach (var m in rows)
                        if (matchIds.Contains(m.matchId) && !string.IsNullOrEmpty(m.outcome)
                            && m.outcome != "PENDING")
                            refVerdicts[m.matchId] = m.outcome;
                });
            }

            int agree = 0, disagree = 0, unsettled = 0;
            for (int i = 0; i < matchIds.Count; i++)
            {
                string local = localVerdicts[i];
                string remote = refVerdicts.TryGetValue(matchIds[i], out var rv) ? rv : "UNSETTLED";
                bool ok = local == remote;
                if (remote == "UNSETTLED") unsettled++;
                else if (ok) agree++; else disagree++;
                say("match " + i + ": local " + local + " vs referee " + remote + (ok ? "  AGREE" : "  *** DISAGREE ***"));
            }
            say("RESULT: " + agree + " agree, " + disagree + " DISAGREE, " + unsettled + " unsettled of " + matchIds.Count
                + (disagree == 0 && unsettled == 0 && matchIds.Count > 0 ? " — determinism HOLDS" : " — INVESTIGATE"));
            Restore(savedToken, log);
        }

        void Restore(string token, List<string> log)
        {
            LadderClient.Token = token;
            Finish(log);
        }

        void Finish(List<string> log)
        {
            report = string.Join("\n", log);
            finished = true;
            Destroy(gameObject);
        }
    }
}
