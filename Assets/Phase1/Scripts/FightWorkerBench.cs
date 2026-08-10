// ===========================================================================
// FightWorkerBench — cover for FightWorkerLoop, 2026-08-09.
//
// Split in two on purpose:
//
//   RunPure()  — no scene, no play mode, under a second. Every pure function
//                the fight contract depends on: the verdict mapping, the
//                hand-built JSON, the seeds parser, envelope verification.
//                This is the one to run after any edit near the worker.
//
//   Run()      — play mode. Drives RunOnce through a stub transport. Most of
//                its checks are REFUSAL paths, which refuse before fighting
//                and so cost nothing; exactly one runs a real single-bout
//                MatchRunner fight, because "the loop can actually complete a
//                match" is not provable any cheaper.
//
// The refusal paths are the ones worth having. A fight that runs and posts is
// the happy case anyone would test; a fetch that 404s, a blob swapped at its
// key, and a VALIDATE job landing in the fight loop are where a worker
// strands a job or fights a substituted robot.
// ===========================================================================
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RobotBrawl.Phase0
{
    /// <summary>The fight half of the API, replaced by a lookup table.</summary>
    public class StubFightTransport : IFightTransport
    {
        public Queue<WorkerJob> jobs = new Queue<WorkerJob>();
        public Dictionary<string, string> blobs = new Dictionary<string, string>();
        public string claimError, fetchError, postError, uploadError;
        public bool postSucceeds = true;
        public string replayUrlToReturn = "file:///tmp/replay-stub.json";

        public string lastPostedJson = "", lastReplayDoc = "";
        public long lastPostedJobId;
        public int claims, fetches, posts, uploads, heartbeats;

        public IEnumerator Claim(string workerId, Action<WorkerJob, string> done)
        {
            claims++; yield return null;
            if (!string.IsNullOrEmpty(claimError)) { done(null, claimError); yield break; }
            done(jobs.Count > 0 ? jobs.Dequeue() : null, null);
        }
        public IEnumerator Fetch(string url, Action<string, string> done)
        {
            fetches++; yield return null;
            if (!string.IsNullOrEmpty(fetchError)) { done(null, fetchError); yield break; }
            string body;
            done(blobs.TryGetValue(url ?? "", out body) ? body : null,
                 blobs.ContainsKey(url ?? "") ? null : "404 no such blob");
        }
        public IEnumerator PostValidate(long jobId, string resultJson, Action<bool, string> done)
        { posts++; lastPostedJobId = jobId; lastPostedJson = resultJson; yield return null; done(postSucceeds, null); }
        public IEnumerator Heartbeat(long jobId, string workerId, Action<bool> done)
        { heartbeats++; yield return null; done(true); }
        public IEnumerator UploadReplay(string matchId, string replayJson, Action<string, string> done)
        {
            uploads++; lastReplayDoc = replayJson; yield return null;
            if (!string.IsNullOrEmpty(uploadError)) { done(null, uploadError); yield break; }
            done(replayUrlToReturn, null);
        }
        public IEnumerator PostFight(long jobId, string resultJson, Action<bool, string> done)
        {
            posts++; lastPostedJobId = jobId; lastPostedJson = resultJson; yield return null;
            done(postSucceeds, postSucceeds ? null : (postError ?? "500"));
        }
    }

    public class FightWorkerBench : MonoBehaviour
    {
        static int passed, failed;
        static readonly List<string> log = new List<string>();
        static void Check(bool ok, string what)
        {
            if (ok) { passed++; log.Add("PASS  " + what); }
            else { failed++; log.Add("FAIL  " + what); }
        }
        static void Note(string s) { log.Add("      " + s); }

        // =================================================== the pure half
        public static bool RunPure()
        {
            passed = 0; failed = 0; log.Clear();

            log.Add("== A. the verdict mapping (§5.3) ==");
            // MatchRunner speaks A/B/Draw; matches.verdict's CHECK accepts only
            // CHALLENGER/DEFENDER/DRAW. envA is ALWAYS the challenger — if that
            // ever changes this mapping silently inverts every result.
            Check(FightWorkerLoop.ToApiVerdict("A") == "CHALLENGER", "runner 'A' is the CHALLENGER");
            Check(FightWorkerLoop.ToApiVerdict("B") == "DEFENDER", "runner 'B' is the DEFENDER");
            Check(FightWorkerLoop.ToApiVerdict("Draw") == "DRAW", "runner 'Draw' is a DRAW");
            // Anything unrecognised must not become a win for somebody.
            Check(FightWorkerLoop.ToApiVerdict("") == "DRAW", "an empty verdict is a DRAW, never a win");
            Check(FightWorkerLoop.ToApiVerdict("nonsense") == "DRAW", "an unknown verdict is a DRAW, never a win");

            log.Add("== B. the wire format — where JsonUtility would have lied ==");
            var o = new FightOutcome { matchId = "m-1", workerId = "bench", verdict = "CHALLENGER" };
            o.replayUrls.Add("file:///tmp/a.json");
            o.bouts.Add("A:KO"); o.bouts.Add("B:JD");
            string j = RobotWorker.FightResultJson(o);
            Note(j);
            Check(j.Contains("\"verdict\":\"CHALLENGER\""), "the verdict is a JSON string");
            Check(j.Contains("\"replayUrls\":[\"file:///tmp/a.json\"]"), "replayUrls is a real array");
            Check(j.Contains("\"bouts\":[\"A:KO\",\"B:JD\"]"), "bouts is a real array");
            Check(!j.Contains("\"True\"") && !j.Contains("\"False\""), "no stringified booleans leaked in");

            var empty = new FightOutcome { matchId = "m-2", workerId = "bench", verdict = "DRAW" };
            string je = RobotWorker.FightResultJson(empty);
            Check(je.Contains("\"replayUrls\":[]"), "a match with no replay sends an EMPTY array, not null");
            Check(je.Contains("\"bouts\":[]"), "…and an empty bouts array");

            log.Add("== C. seeds — the one non-scalar in a claim ==");
            // Field() stops at the first delimiter and would hand back "[7".
            var seeds = RobotWorker.IntArrayField("{\"seeds\":[7,8,9],\"arena\":\"league\"}", "seeds");
            Check(seeds.Length == 3 && seeds[0] == 7 && seeds[2] == 9, "seeds parse to 3 ints");
            Check(RobotWorker.IntArrayField("{\"seeds\":[]}", "seeds").Length == 0, "an empty seeds array is empty, not a crash");
            Check(RobotWorker.IntArrayField("{\"kind\":\"VALIDATE\"}", "seeds").Length == 0, "a missing seeds key is empty, not a crash");
            Check(RobotWorker.IntArrayField("{\"seeds\":[ 4 , 5 ]}", "seeds").Length == 2, "whitespace inside the array is tolerated");

            log.Add("== D. a FIGHT claim parses into both sides ==");
            string claim = "{\"id\":42,\"kind\":\"FIGHT\",\"matchId\":\"m-9\",\"snapshotId\":null,\"attempts\":0,"
                         + "\"payloadUrl\":null,\"payloadSha256\":null,"
                         + "\"challengerUrl\":\"file:///c.json\",\"challengerSha256\":\"aa\","
                         + "\"defenderUrl\":\"file:///d.json\",\"defenderSha256\":\"bb\","
                         + "\"arena\":\"league\",\"seeds\":[1,2,3]}";
            var job = RobotWorker.ParseClaim(claim);
            Check(job != null && job.id == 42, "the job id parses");
            Check(job != null && job.IsFight && !job.IsValidate, "it is a FIGHT, not a VALIDATE");
            Check(job != null && job.challengerUrl == "file:///c.json" && job.defenderUrl == "file:///d.json",
                  "BOTH payload locations survive the parse");
            Check(job != null && job.seeds.Length == 3, "and the seeds come with it");
            // A VALIDATE claim must not acquire fight fields by accident.
            var vjob = RobotWorker.ParseClaim("{\"id\":1,\"kind\":\"VALIDATE\",\"snapshotId\":\"s\",\"payloadUrl\":\"file:///p\",\"payloadSha256\":\"cc\"}");
            Check(vjob != null && vjob.IsValidate && !vjob.IsFight, "a VALIDATE claim is still a VALIDATE");
            Check(vjob != null && vjob.challengerUrl == "" && vjob.seeds.Length == 0,
                  "…and carries no fight fields");

            log.Add("== E. both sides are verified BEFORE a fight is spent ==");
            var env = RobotSnapshot.ExportRaw("BenchBot", BuilderManager.SNAP_STAMP + "\ncore|0.000,0.700,0.000|0|0.00,0.00,0.00|Aluminum\n", "");
            string envJson = JsonUtility.ToJson(env);
            SnapshotEnvelope got; string err;
            Check(FightWorkerLoop.OpenVerified(envJson, env.sha256, out got, out err) && got != null,
                  "a good envelope whose sha matches the claim opens");
            // The blob-swapped-at-its-key case: the envelope is internally
            // consistent, but it is not the one the job named.
            Check(!FightWorkerLoop.OpenVerified(envJson, new string('0', 64), out got, out err),
                  "an envelope whose sha does NOT match the claim is refused");
            Check(err != null && err.Contains("does not match"), "…and says storage was altered");
            Check(!FightWorkerLoop.OpenVerified("{\"not\":\"an envelope\"}", "", out got, out err),
                  "an unreadable payload is refused");
            Check(!FightWorkerLoop.OpenVerified("", "", out got, out err),
                  "an empty payload is refused");

            Finish("qa_fight_worker_pure.txt");
            return failed == 0;
        }

        // ============================================== the play-mode half
        public int passedRun, failedRun;
        public bool finished;
        public string report = "";

        public static FightWorkerBench Run()
        {
            return new GameObject("fight_worker_bench").AddComponent<FightWorkerBench>();
        }

        static WorkerJob FightJob(long id, string cUrl, string cSha, string dUrl, string dSha, int[] seeds)
        {
            return new WorkerJob
            {
                id = id, kind = "FIGHT", matchId = "bench-match-" + id,
                challengerUrl = cUrl, challengerSha256 = cSha,
                defenderUrl = dUrl, defenderSha256 = dSha,
                arena = "league", seeds = seeds
            };
        }

        IEnumerator Start()
        {
            passed = 0; failed = 0; log.Clear();
            var bm = UnityEngine.Object.FindFirstObjectByType<BuilderManager>();
            if (bm == null) { Check(false, "BuilderManager in scene"); FinishRun(); yield break; }

            string ownerBay = bm.SnapshotString();
            var savedData = Career.Data;
            bool savedAuto = Career.autosave;

            // Two envelopes built from raw text, so the bench never drives the
            // builder and owen's bay is never touched.
            string build = BuilderManager.SNAP_STAMP
                         + "\ncore|0.000,0.700,0.000|0|0.00,0.00,0.00|Aluminum\n";
            var envA = RobotSnapshot.ExportRaw("Bench-A", build, "");
            var envB = RobotSnapshot.ExportRaw("Bench-B", build, "");
            string aJson = JsonUtility.ToJson(envA), bJson = JsonUtility.ToJson(envB);

            log.Add("== F. refusals, which cost no fight ==");

            // A payload that will not fetch.
            var net = new StubFightTransport();
            net.jobs.Enqueue(FightJob(1, "file:///a", envA.sha256, "file:///b", envB.sha256, new[] { 7 }));
            // no blobs registered -> 404
            yield return FightWorkerLoop.RunOnce(net, bm, null);
            Check(net.posts == 1, "a fetch failure still POSTS, so the job is retired not stranded");
            Check(net.lastPostedJson.Contains("\"verdict\":\"DRAW\""),
                  "…as a DRAW, so the escrow returns to the challenger");
            Check(net.uploads == 0, "…and no replay was uploaded for a fight that never happened");

            // A blob swapped at its key: fetches fine, wrong bytes.
            net = new StubFightTransport();
            net.blobs["file:///a"] = aJson; net.blobs["file:///b"] = bJson;
            net.jobs.Enqueue(FightJob(2, "file:///a", new string('0', 64), "file:///b", envB.sha256, new[] { 7 }));
            yield return FightWorkerLoop.RunOnce(net, bm, null);
            Check(net.posts == 1, "a challenger blob altered at its key is refused, and the job retired");
            Check(FightWorkerLoop.LastError.Contains("challenger"), "…naming the challenger as the bad side");
            Check(net.uploads == 0, "…without spending a fight on it");

            // The defender side must be checked too — verifying only the first
            // payload is the easy half of this bug.
            net = new StubFightTransport();
            net.blobs["file:///a"] = aJson; net.blobs["file:///b"] = bJson;
            net.jobs.Enqueue(FightJob(3, "file:///a", envA.sha256, "file:///b", new string('0', 64), new[] { 7 }));
            yield return FightWorkerLoop.RunOnce(net, bm, null);
            Check(net.posts == 1 && FightWorkerLoop.LastError.Contains("defender"),
                  "a DEFENDER blob altered at its key is refused too");

            log.Add("== G. a job that is not ours ==");
            net = new StubFightTransport();
            net.jobs.Enqueue(new WorkerJob { id = 9, kind = "VALIDATE", snapshotId = "s", payloadUrl = "file:///p" });
            yield return FightWorkerLoop.RunOnce(net, bm, null);
            Check(net.posts == 0, "a VALIDATE job is NOT posted to fight-result");
            Check(net.uploads == 0, "…and not fought");
            Note("a validate job left alone is claimed-and-dropped; the reaper returns it (§5.3)");

            log.Add("== H. an empty queue ==");
            net = new StubFightTransport();
            yield return FightWorkerLoop.RunOnce(net, bm, null);
            Check(net.posts == 0 && net.claims == 1, "an empty queue is not an error and posts nothing");

            log.Add("== I. one real fight, all the way through ==");
            // Single seed: this is the only check that pays for a fight, and
            // best-of-3 would triple the cost to prove the same thing.
            net = new StubFightTransport();
            net.blobs["file:///a"] = aJson; net.blobs["file:///b"] = bJson;
            net.jobs.Enqueue(FightJob(4, "file:///a", envA.sha256, "file:///b", envB.sha256, new[] { 7 }));
            FightWorkerLoop.SpeedMultiplier = 20f;
            yield return FightWorkerLoop.RunOnce(net, bm, null);

            Check(net.posts == 1, "the loop completes a real match and posts exactly once");
            Check(net.uploads == 1, "…having uploaded the replay first");
            string v = RobotWorker.Field(net.lastPostedJson, "verdict");
            Check(v == "CHALLENGER" || v == "DEFENDER" || v == "DRAW",
                  "…with a verdict the API's CHECK accepts (" + v + ")");
            Check(net.lastPostedJson.Contains("\"replayUrls\":[\"" + net.replayUrlToReturn + "\"]"),
                  "…carrying the replay url the upload returned");
            Check(net.lastReplayDoc.Contains("\"replayVersion\":1"), "the replay doc is versioned");
            Check(net.lastReplayDoc.Contains("\"bouts\":["), "…and carries the bouts");

            log.Add("== J. owner state ==");
            Check(Career.Data == savedData, "the career object was never swapped");
            Check(Career.autosave == savedAuto, "…and autosave was left as it was found");
            Check(bm.SnapshotString() == ownerBay, "the bay is still owen's after every path");

            FinishRun();
        }

        void FinishRun()
        {
            passedRun = passed; failedRun = failed;
            Finish("qa_fight_worker_bench.txt");
            report = string.Join("\n", log.ToArray());
            finished = true;
            if (gameObject != null) Destroy(gameObject, 0.5f);
        }

        static void Finish(string file)
        {
            log.Add(" RESULT: " + passed + " pass, " + failed + " fail"
                    + (failed == 0 ? " - ALL GREEN" : ""));
            string text = string.Join("\n", log.ToArray()) + "\n";
            Debug.Log("[FightWorkerBench] RESULT: " + passed + " pass, " + failed + " fail");
            try { System.IO.File.WriteAllText(Application.dataPath + "/Phase1/" + file, text); }
            catch (Exception e) { Debug.LogWarning("could not write " + file + ": " + e.Message); }
        }
    }
}
