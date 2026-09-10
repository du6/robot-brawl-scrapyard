// ===========================================================================
// FightWorkerLoop — the FIGHT half of the worker, 2026-08-09.
//
// ValidateWorkerLoop's sibling. It claims a FIGHT job, fetches BOTH robots'
// payloads, verifies both hashes, runs best-of-3 through MatchRunner, uploads
// the replay and posts the verdict.
//
// Design doc §5.3 steps 2-3. Until this existed the server contract had no
// consumer: api_smoke's section K posts a verdict it invents, which proves
// the endpoints and nothing about whether a real worker can produce one.
//
// THREE THINGS THAT WILL BITE, all learned elsewhere in this project:
//
//  1. VERIFY BOTH HASHES BEFORE FIGHTING. A fight is expensive; a swapped
//     blob discovered afterwards has already cost the wall clock. The
//     validate loop verifies one payload, this verifies two, and a mismatch
//     on either is a refusal rather than a fight with a substituted robot.
//
//  2. MATCHRUNNER ALREADY ISOLATES CAREER STATE. It swaps in a scratch
//     CareerData and restores it in a finally (MatchRunner.cs:216-255) -
//     FightManager.End calls Progression.OnMatchEnd unconditionally, so
//     anything running a fight headlessly must. Do NOT add a second layer
//     here: two nested swaps that disagree about what "saved" means is how
//     you lose owner state.
//
//  3. VERDICT NAMES ARE NOT THE RUNNER'S NAMES. MatchRunner speaks "A"/"B"/
//     "Draw" (envA is whoever was passed first). The API's CHECK constraint
//     accepts only CHALLENGER/DEFENDER/DRAW. The mapping is here, in one
//     place, and it is the reason envA is ALWAYS the challenger.
// ===========================================================================
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RobotBrawl.Phase0
{
    public class FightWorkerLoop : MonoBehaviour
    {
        public static string WorkerId = "editor-fight-1";
        public static float SpeedMultiplier = 10f;
        /// <summary>The last completed match, for benches that want to look at
        /// the bouts (clock, cause) rather than only at what was posted.</summary>
        public static MatchRunner.MatchResult LastResult;

        // Pollable from outside — the domain reload makes holding a reference
        // across bridge calls impossible, so the bench and a driving session
        // both read these.
        public static string LastError = "";
        public static string LastPostedJson = "";
        public static string LastVerdict = "";
        public static int JobsHandled, PostsSucceeded, PostsFailed, Refusals;

        /// <summary>One FIGHT job, or nothing if the queue is empty. Returns
        /// without touching the queue when the claimed job is a VALIDATE —
        /// that belongs to the other loop.</summary>
        public static IEnumerator RunOnce(IFightTransport net, BuilderManager bm,
                                          Action<MatchRunner.MatchResult> done)
        {
            LastError = ""; LastPostedJson = ""; LastVerdict = "";

            WorkerJob job = null; string err = null;
            yield return net.Claim(WorkerId, (j, e) => { job = j; err = e; });
            if (!string.IsNullOrEmpty(err)) { LastError = "claim: " + err; if (done != null) done(null); yield break; }
            if (job == null) { if (done != null) done(null); yield break; }   // 204, queue empty
            if (!job.IsFight)
            {
                // Not ours. Say so rather than failing the job: a VALIDATE
                // worker will take it, and failing it here would strand a
                // perfectly good job.
                LastError = "claimed a " + job.kind + " job, which this loop does not run";
                if (done != null) done(null); yield break;
            }
            if (bm == null) { LastError = "no BuilderManager in the scene"; if (done != null) done(null); yield break; }

            // ---- fetch both sides ------------------------------------------
            string aJson = null, bJson = null;
            yield return net.Fetch(job.challengerUrl, (s, e) => { aJson = s; err = e; });
            if (!string.IsNullOrEmpty(err) || string.IsNullOrEmpty(aJson))
            { yield return Refuse(net, job, "challenger payload could not be fetched: " + err, done); yield break; }
            yield return net.Fetch(job.defenderUrl, (s, e) => { bJson = s; err = e; });
            if (!string.IsNullOrEmpty(err) || string.IsNullOrEmpty(bJson))
            { yield return Refuse(net, job, "defender payload could not be fetched: " + err, done); yield break; }

            // ---- verify BOTH before spending a fight on them ---------------
            SnapshotEnvelope envA, envB; string openErr;
            if (!OpenVerified(aJson, job.challengerSha256, out envA, out openErr))
            { yield return Refuse(net, job, "challenger: " + openErr, done); yield break; }
            if (!OpenVerified(bJson, job.defenderSha256, out envB, out openErr))
            { yield return Refuse(net, job, "defender: " + openErr, done); yield break; }

            // ---- run it ----------------------------------------------------
            // envA is the CHALLENGER, always. Trap 3 above depends on it.
            int[] seeds = (job.seeds != null && job.seeds.Length > 0) ? job.seeds : new[] { 1, 2, 3 };
            MatchRunner.MatchResult result = null;
            var runner = MatchRunner.Run(envA, envB, seeds, job.matchId, SpeedMultiplier, true,
                                         r => { result = r; });
            // The claim always carried `arena`; until 2026-09-09 nothing read
            // it. "yard" is Robot Brawl: Scrapyard's ruleset - the Quick clock.
            // Anything else (league, league_night) is the fight as it was.
            runner.quick = job.arena == "yard";
            while (!runner.finished) yield return null;
            result = runner.result;
            LastResult = result;
            if (runner != null && runner.gameObject != null) UnityEngine.Object.Destroy(runner.gameObject);

            if (result == null || !result.Ok)
            {
                yield return Refuse(net, job, "the match did not complete: "
                                    + (result == null ? "no result" : result.error), done);
                yield break;
            }

            // ---- upload the RECORDINGS -------------------------------------
            // One .rbr.gz per bout, raw. These are what a client plays.
            //
            // 2026-08-09: this used to upload ONLY the summary document below,
            // so every replay URL on every match pointed at a scorecard.
            // Nothing noticed because nothing had ever tried to PLAY one — the
            // gap appears the moment you build the launcher, not before.
            var replayUrls = new List<string>();
            for (int i = 0; i < result.bouts.Count; i++)
            {
                string path = result.bouts[i].replayPath;
                if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) continue;
                byte[] bytes = null;
                try { bytes = System.IO.File.ReadAllBytes(path); }
                catch (Exception e) { LastError = "could not read " + path + ": " + e.Message; }
                if (bytes == null || bytes.Length == 0) continue;

                string url = null;
                yield return net.UploadReplayFile(job.matchId, bytes, (u, e) => { url = u; err = e; });
                if (!string.IsNullOrEmpty(url)) replayUrls.Add(url);
                else LastError = "bout " + i + " recording did not upload: " + err;
            }

            // The scorecard rides along AFTER the recordings, so replayUrls[0]
            // is always something playable. A client that takes the first URL
            // and hands it to ReplayPlayer must not get a summary.
            string summaryUrl = null;
            yield return net.UploadReplay(job.matchId, BuildReplayDoc(result, seeds),
                                          (u, e) => { summaryUrl = u; });
            if (!string.IsNullOrEmpty(summaryUrl)) replayUrls.Add(summaryUrl);

            if (replayUrls.Count == 0)
            {
                // The fight really happened; losing the recording must not lose
                // the verdict, so post anyway with no replay rather than
                // discarding a result that cost a full best-of-3.
                LastError = "no replay uploaded, posting the verdict without one: " + err;
            }

            var outcome = new FightOutcome
            {
                matchId = job.matchId,
                workerId = WorkerId,
                verdict = ToApiVerdict(result.verdict),
            };
            outcome.replayUrls.AddRange(replayUrls);
            for (int i = 0; i < result.bouts.Count; i++)
                outcome.bouts.Add(result.bouts[i].winner + ":" + result.bouts[i].outcome);

            string json = RobotWorker.FightResultJson(outcome);
            LastPostedJson = json;
            LastVerdict = outcome.verdict;

            bool posted = false;
            yield return net.PostFight(job.id, json, (okv, e) => { posted = okv; err = e; });
            JobsHandled++;
            if (posted) PostsSucceeded++;
            else { PostsFailed++; LastError = "post: " + err; }

            if (done != null) done(result);
        }

        /// <summary>A refusal still POSTs, so the job is retired rather than
        /// left for the reaper to hand out again — the same livelock the
        /// validate contract had (docs/Validate_Result_Livelock_Fixed).
        /// A DRAW with no replay is the honest shape: no fight happened, so
        /// nobody won, and the escrow returns to the challenger.</summary>
        static IEnumerator Refuse(IFightTransport net, WorkerJob job, string why,
                                  Action<MatchRunner.MatchResult> done)
        {
            Refusals++;
            LastError = why;
            var outcome = new FightOutcome
            {
                matchId = job.matchId, workerId = WorkerId, verdict = "DRAW",
            };
            outcome.bouts.Add("refused:" + why);
            string json = RobotWorker.FightResultJson(outcome);
            LastPostedJson = json;
            LastVerdict = "DRAW";
            bool posted = false; string e2 = null;
            yield return net.PostFight(job.id, json, (okv, e) => { posted = okv; e2 = e; });
            JobsHandled++;
            if (posted) PostsSucceeded++; else { PostsFailed++; LastError = why + " | post also failed: " + e2; }
            if (done != null) done(null);
        }

        /// <summary>Parse the envelope and check the bytes are the ones the
        /// job named. Open() already verifies the envelope's own sha over its
        /// payload; this additionally pins it to the CLAIM, which is what
        /// catches a blob swapped at its key.</summary>
        public static bool OpenVerified(string envJson, string expectedSha,
                                        out SnapshotEnvelope env, out string err)
        {
            env = null; err = null;
            env = SnapshotEnvelope.FromJson(envJson);
            if (env == null) { err = "payload is not a readable envelope"; return false; }
            if (!string.IsNullOrEmpty(expectedSha) &&
                !string.Equals(env.sha256, expectedSha, StringComparison.OrdinalIgnoreCase))
            { err = "sha256 does not match the claim (storage was altered)"; env = null; return false; }
            SnapshotPayload p; string openErr;
            if (!RobotSnapshot.Open(env, out p, out openErr))
            { err = "envelope did not open: " + openErr; env = null; return false; }
            return true;
        }

        /// <summary>"A"/"B"/"Draw" -> the API's CHECK set. envA is always the
        /// challenger; if that ever stops being true this mapping silently
        /// inverts every result, which is why it lives next to the Run call.</summary>
        public static string ToApiVerdict(string runnerVerdict)
        {
            if (runnerVerdict == "A") return "CHALLENGER";
            if (runnerVerdict == "B") return "DEFENDER";
            return "DRAW";
        }

        static string BuildReplayDoc(MatchRunner.MatchResult r, int[] seeds)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("{\"replayVersion\":1,\"matchId\":").Append(RobotWorker.Str(r.matchId));
            sb.Append(",\"verdict\":").Append(RobotWorker.Str(r.verdict));
            sb.Append(",\"decidedBy\":").Append(RobotWorker.Str(r.decidedBy));
            sb.Append(",\"bouts\":[");
            for (int i = 0; i < r.bouts.Count; i++)
            {
                var b = r.bouts[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"bout\":").Append(b.bout)
                  .Append(",\"seed\":").Append(b.seed)
                  .Append(",\"winner\":").Append(RobotWorker.Str(b.winner))
                  .Append(",\"outcome\":").Append(RobotWorker.Str(b.outcome))
                  .Append(",\"cause\":").Append(RobotWorker.Str(b.cause))
                  .Append(",\"simSeconds\":").Append(b.simSeconds.ToString("0.###",
                        System.Globalization.CultureInfo.InvariantCulture))
                  .Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        public IEnumerator RunForever(IFightTransport net, BuilderManager bm)
        {
            while (true)
            {
                yield return RunOnce(net, bm, null);
                yield return new WaitForSeconds(3f);
            }
        }
    }
}
