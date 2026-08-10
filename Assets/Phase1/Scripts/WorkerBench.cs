// ⚠ EDITOR AND DEVELOPMENT BUILDS ONLY — 2026-08-10.
// This is a QA harness. It was compiling into the SHIPPED iOS player: 44 such
// files, ~13k lines, and the string "dev-only-worker-key" was verified present
// in build/ios global-metadata.dat. Dead weight in the binary, and a surface
// that can register accounts and start fights inside a player's app.
// Nothing in the product references any harness — checked against every
// product script, every scene and every prefab before this guard was added.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
// ===========================================================================
// WorkerBench.cs — acceptance harness for the VALIDATE worker (M1, §5.2).
//
// Runs the ENTIRE worker loop with no API, no Postgres, no Docker and no
// network, by driving it through a stub transport. That is not a compromise
// forced by the sandbox — it is the better instrument. Every failure this
// worker has to survive (storage down, a truncated payload, a blob swapped
// at its key, a build that loads nothing) is trivial to provoke by handing
// Judge a different string, and nearly impossible to provoke on demand
// against a real server.
//
// What this bench CANNOT prove, said plainly so nobody reads it as more than
// it is: that the API accepts the JSON. The shapes here are written from
// Program.cs's ValidateResult record and the 001_init.sql CHECKs, and
// section A asserts the literal bytes — but only `run_local.sh` with a real
// worker pointed at it closes that loop. api_smoke's 37/37 was green while
// the claim response was unusable, for exactly this reason: an endpoint
// suite that never runs the loop it serves is testing its own reachability.
//
// Run from an editor RunCommand, in play mode:
//     RobotBrawl.Phase0.WorkerBench.Run()
// ===========================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace RobotBrawl.Phase0
{
    /// <summary>The API, replaced by a lookup table. Every response the real
    /// one can give — a job, an empty queue, a 500, a missing blob — is one
    /// field here.</summary>
    public class StubWorkerTransport : IWorkerTransport
    {
        public Queue<WorkerJob> jobs = new Queue<WorkerJob>();
        public Dictionary<string, string> blobs = new Dictionary<string, string>();
        public string claimError, fetchError, postError;
        public bool postSucceeds = true;

        public string lastPostedJson = "";
        public long lastPostedJobId;
        public int claims, fetches, posts, heartbeats;

        public IEnumerator Claim(string workerId, Action<WorkerJob, string> done)
        {
            claims++;
            yield return null;
            if (!string.IsNullOrEmpty(claimError)) { done(null, claimError); yield break; }
            done(jobs.Count > 0 ? jobs.Dequeue() : null, null);
        }

        public IEnumerator Fetch(string url, Action<string, string> done)
        {
            fetches++;
            yield return null;
            if (!string.IsNullOrEmpty(fetchError)) { done(null, fetchError); yield break; }
            string body;
            done(blobs.TryGetValue(url ?? "", out body) ? body : null,
                 blobs.ContainsKey(url ?? "") ? null : "404 no such blob");
        }

        public IEnumerator PostValidate(long jobId, string resultJson, Action<bool, string> done)
        {
            posts++; lastPostedJobId = jobId; lastPostedJson = resultJson;
            yield return null;
            done(postSucceeds, postSucceeds ? null : (postError ?? "500"));
        }

        public IEnumerator Heartbeat(long jobId, string workerId, Action<bool> done)
        {
            heartbeats++;
            yield return null;
            done(true);
        }
    }

    public class WorkerBench : MonoBehaviour
    {
        public static WorkerBench Run()
        {
            return new GameObject("worker_bench").AddComponent<WorkerBench>();
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

        IEnumerator Start()
        {
            var bm = UnityEngine.Object.FindFirstObjectByType<BuilderManager>();
            if (bm == null) { Check(false, "BuilderManager in scene"); Finish(); yield break; }

            string ownerBay = bm.SnapshotString();
            var savedData = Career.Data;
            bool savedAuto = Career.autosave, savedActive = Career.active;
            Career.autosave = false;

            // ==================================== 0. the worker key's blast radius
            // The payload url comes from a SERVER RESPONSE, and the fetch has
            // to send the worker key because /v1/blobs is authenticated. Those
            // two facts together mean a bad or compromised payload url could
            // walk the shared secret to any host it names, so the transport
            // sends it same-origin only. This is cheap to get wrong with
            // StartsWith and expensive to notice.
            log.Add("== 0. the worker key goes to our API and nowhere else ==");
            var probe = new HttpWorkerTransport("https://rb-api.example.com", "secret", "VALIDATE");
            Check(probe.IsOwnApi("https://rb-api.example.com/v1/blobs/snapshots/a.json"),
                  "the key is sent to our own API");
            Check(probe.IsOwnApi("https://RB-API.EXAMPLE.COM/v1/blobs/x"),
                  "...host comparison is case-insensitive, as DNS is");
            Check(!probe.IsOwnApi("https://rb-api.example.com.evil.test/v1/blobs/x"),
                  "a look-alike host that merely STARTS WITH ours is refused");
            Check(!probe.IsOwnApi("https://evil.test/v1/blobs/x"),
                  "an unrelated host is refused");
            Check(!probe.IsOwnApi("http://rb-api.example.com/v1/blobs/x"),
                  "the same host over plain http is refused — that is a downgrade");
            Check(!probe.IsOwnApi("https://rb-api.example.com:8443/v1/blobs/x"),
                  "a different port on the same host is refused");
            Check(!probe.IsOwnApi("not a url"), "an unparseable url is refused rather than assumed ours");
            Check(!probe.IsOwnApi(""), "an empty url is refused");

            // ============================================ A. the result JSON
            log.Add("== A. the wire format — where JsonUtility would have lied ==");

            var o = new ValidateOutcome();
            o.snapshotId = "11111111-2222-3333-4444-555555555555";
            o.workerId = "bench"; o.legal = true; o.massKg = 1234;
            o.aabbX = 1.5f; o.aabbY = 0.75f; o.aabbZ = 2f;
            o.category = "FEATHER"; o.programHash = new string('a', 64);
            o.partsManifest = new List<string> { "core", "beam" };
            string json = RobotWorker.ResultJson(o);
            Note(json);

            Check(json.Contains("\"category\":\"FEATHER\""), "a category is sent as a string");
            Check(json.Contains("\"legal\":true"), "legal is a JSON bool, not \"True\"");
            Check(json.Contains("\"massKg\":1234"), "mass is a bare number");
            Check(json.Contains("\"partsManifest\":[\"core\",\"beam\"]"), "the manifest is a JSON array");

            // The whole reason ResultJson is hand-written. snapshots.category
            // is `CHECK (category IS NULL OR category IN (...))` — NULL passes,
            // "" does not, and JsonUtility emits "" for a null string.
            o.category = "";
            o.legal = false;
            o.failReasons = new List<string> { "1 kg over the heaviest category (5500 kg limit, build is 5501 kg)." };
            string nullCat = RobotWorker.ResultJson(o);
            Note(nullCat);
            Check(nullCat.Contains("\"category\":null"),
                  "NO category is sent as a bare null — the value the CHECK accepts");
            Check(!nullCat.Contains("\"category\":\"\""),
                  "and never as \"\" — the value the CHECK rejects");
            Check(nullCat.Contains("\"legal\":false"), "an over-cap robot is reported illegal");

            // failReasons is validator prose and a build name is player input.
            o.failReasons = new List<string> { "he said \"no\"\nand a tab\there\\done" };
            string esc = RobotWorker.ResultJson(o);
            Check(esc.Contains("\\\"no\\\"") && esc.Contains("\\n") && esc.Contains("\\t") && esc.Contains("\\\\"),
                  "quotes, newlines, tabs and backslashes are escaped, not passed through");

            // A comma decimal separator is not JSON, and the editor's locale
            // is not the server's.
            Check(!json.Contains("1,5") && json.Contains("1.5"),
                  "floats use an invariant decimal point regardless of locale");

            // ================================================ B. parsing a claim
            log.Add("== B. reading the claim response ==");

            string claimJson =
                "{\"id\":42,\"kind\":\"VALIDATE\",\"matchId\":null," +
                "\"snapshotId\":\"9ee74bbf-0000-0000-0000-000000000001\",\"attempts\":1," +
                "\"payloadUrl\":\"file:///tmp/rb-blobs/x.json\",\"payloadSha256\":\"" + new string('e', 64) + "\"," +
                "\"challengerUrl\":null,\"challengerSha256\":null,\"defenderUrl\":null," +
                "\"defenderSha256\":null,\"arena\":null,\"seeds\":null}";
            var job = RobotWorker.ParseClaim(claimJson);
            Check(job != null && job.id == 42, "job id parses");
            Check(job != null && job.IsValidate, "kind VALIDATE is recognised");
            Check(job != null && job.payloadUrl == "file:///tmp/rb-blobs/x.json",
                  "payloadUrl survives its slashes and colons");
            Check(job != null && job.payloadSha256.Length == 64, "payloadSha256 parses whole");
            Check(RobotWorker.Field(claimJson, "matchId") == null,
                  "a JSON null reads as null, not as the string \"null\"");
            Check(RobotWorker.ParseClaim("") == null, "an empty body is not a job");

            // A FIGHT claim carries no payloadUrl. Telling the two apart is
            // the whole reason this parser is hand-written.
            var fight = RobotWorker.ParseClaim(
                "{\"id\":43,\"kind\":\"FIGHT\",\"snapshotId\":null,\"attempts\":0," +
                "\"payloadUrl\":null,\"arena\":\"yard\"}");
            Check(fight != null && !fight.IsValidate && string.IsNullOrEmpty(fight.payloadUrl),
                  "a FIGHT job is distinguishable from a VALIDATE one");

            // ============================================ C. the real fixture
            log.Add("== C. a real robot, end to end through the loop ==");

            int n = bm.LoadSnapshot(VerbBench.ARMED);
            Check(n > 0, "fixture rig loads (" + n + " parts)");
            if (n <= 0) { Restore(bm, ownerBay, savedData, savedAuto, savedActive); Finish(); yield break; }
            yield return null;

            var env = RobotSnapshot.Export(bm, "Worker Fixture", RobotProgram.Brawler());
            string envJson = env.ToJson();
            const string URL = "file:///tmp/rb-blobs/snapshots/bench/fixture.json";

            // The bay is deliberately set to something else first, so "the bay
            // came back" is a real assertion and not a coincidence.
            bm.LoadSnapshot(ownerBay);
            yield return null;
            string bayBefore = bm.SnapshotString();

            var net = new StubWorkerTransport();
            net.blobs[URL] = envJson;
            net.jobs.Enqueue(NewJob(1, env.sha256, URL));

            JudgeResult res = null;
            yield return ValidateWorkerLoop.RunOnce(net, bm, r => res = r);

            Check(res != null && res.action == WorkerAction.PostVerdict, "a readable snapshot produces a verdict");
            Check(res != null && res.outcome.legal, "the fixture validates legal: " + (res != null ? res.note : "no result"));
            Check(res != null && res.outcome.category == "FEATHER",
                  "category came back from the real game code (" + (res != null ? res.outcome.massKg : 0) + " kg)");
            Check(net.posts == 1 && net.lastPostedJobId == 1, "exactly one result was posted, for the right job");
            Check(net.lastPostedJson.Contains("\"category\":\"FEATHER\""), "the posted JSON carries the category");
            Check(net.heartbeats >= 1, "the job was heartbeated before the result went up");
            Check(bm.SnapshotString() == bayBefore, "the bay came back byte-identical after the measurement");

            // ======================================= D. the failures that matter
            log.Add("== D. the four ways this goes wrong ==");

            // D1 — storage unreachable. THE important one: a network blip must
            // not write REJECTED onto a good robot. See RobotWorker TRAP 2.
            var down = new StubWorkerTransport();
            down.jobs.Enqueue(NewJob(2, env.sha256, URL));
            down.fetchError = "Cannot connect to destination host";
            JudgeResult r2 = null;
            yield return ValidateWorkerLoop.RunOnce(down, bm, r => r2 = r);
            Check(r2 != null && r2.action == WorkerAction.LeaveForRetry,
                  "storage being down leaves the job for retry");
            Check(down.posts == 0,
                  "and posts NOTHING — a fetch failure must never reject a snapshot");

            // D2 — the payload was altered after upload. Open() catches it.
            var tampered = new StubWorkerTransport();
            string bad = envJson.Replace("\\\"robotName\\\":\\\"Worker Fixture\\\"",
                                         "\\\"robotName\\\":\\\"Tampered Robot\\\"");
            Check(bad != envJson, "the tamper actually changed the payload (bench self-check)");
            tampered.blobs[URL] = bad;
            tampered.jobs.Enqueue(NewJob(3, env.sha256, URL));
            JudgeResult r3 = null;
            yield return ValidateWorkerLoop.RunOnce(tampered, bm, r => r3 = r);
            Check(r3 != null && r3.action == WorkerAction.PostVerdict && !r3.outcome.legal,
                  "an altered payload is REJECTED, not retried — the bytes will not improve");
            Check(r3 != null && r3.outcome.failReasons.Count > 0 &&
                  r3.outcome.failReasons[0].Contains("sha256"),
                  "and the reason names the hash: " +
                  (r3 != null && r3.outcome.failReasons.Count > 0 ? r3.outcome.failReasons[0] : "none"));
            Check(r3 != null && string.IsNullOrEmpty(r3.outcome.category),
                  "a rejected snapshot carries no category — there is no ladder for it");
            Check(tampered.lastPostedJson.Contains("\"category\":null"),
                  "which reaches the API as null, the value the CHECK accepts");

            // D3 — identity: the blob is a VALID snapshot, but not the one the
            // job named. Open() passes; only the job-vs-envelope comparison
            // catches this. This is the check TRAP 1 exists to get right.
            var swapped = new StubWorkerTransport();
            swapped.blobs[URL] = envJson;
            swapped.jobs.Enqueue(NewJob(4, new string('b', 64), URL));
            JudgeResult r4 = null;
            yield return ValidateWorkerLoop.RunOnce(swapped, bm, r => r4 = r);
            Check(r4 != null && r4.action == WorkerAction.PostVerdict && !r4.outcome.legal,
                  "a valid snapshot at the wrong key is rejected on identity");
            Check(r4 != null && r4.outcome.failReasons.Count > 0 &&
                  r4.outcome.failReasons[0].Contains("different snapshot"),
                  "and says so plainly, rather than blaming the robot");

            // D4 — the claim itself did not resolve. This is the 08-09 bug
            // class: a job with no payloadUrl. It must not be a rejection.
            var noUrl = new StubWorkerTransport();
            noUrl.jobs.Enqueue(NewJob(5, env.sha256, ""));
            JudgeResult r5 = null;
            yield return ValidateWorkerLoop.RunOnce(noUrl, bm, r => r5 = r);
            Check(r5 != null && r5.action == WorkerAction.LeaveForRetry && noUrl.posts == 0,
                  "a claim with no payloadUrl is a server fault, not an illegal robot");

            // ================================================ E. the quiet paths
            log.Add("== E. an empty queue, and a job that is not ours ==");

            var empty = new StubWorkerTransport();
            JudgeResult r6 = new JudgeResult();
            yield return ValidateWorkerLoop.RunOnce(empty, bm, r => r6 = r);
            Check(r6 == null, "an empty queue returns nothing and is not an error");
            Check(empty.posts == 0 && empty.fetches == 0, "and does no work");

            var wrongKind = new StubWorkerTransport();
            var fj = NewJob(7, "", "");
            fj.kind = "FIGHT";
            wrongKind.jobs.Enqueue(fj);
            JudgeResult r7 = new JudgeResult();
            yield return ValidateWorkerLoop.RunOnce(wrongKind, bm, r => r7 = r);
            Check(r7 == null && wrongKind.posts == 0,
                  "a FIGHT job is left alone — the endpoints for it do not exist yet");

            Check(bm.SnapshotString() == bayBefore, "the bay is still owen's after every failure path");
            Check(Career.Data == savedData, "the career object was never swapped");

            Restore(bm, ownerBay, savedData, savedAuto, savedActive);
            yield return null;
            Finish();
        }

        static WorkerJob NewJob(long id, string sha, string url)
        {
            var j = new WorkerJob();
            j.id = id; j.kind = "VALIDATE";
            j.snapshotId = "9ee74bbf-0000-0000-0000-00000000000" + (id % 10);
            j.attempts = 1; j.payloadSha256 = sha; j.payloadUrl = url;
            return j;
        }

        void Restore(BuilderManager bm, string bay, object data, bool auto, bool active)
        {
            Time.timeScale = 1f;
            if (bm != null)
            {
                if (bm.mode != BuilderManager.Mode.Build) bm.BackToBuild();
                bm.LoadSnapshot(bay);
            }
            Career.autosave = auto;
            Career.active = active;
        }

        void Finish()
        {
            var sb = new StringBuilder();
            foreach (var l in log) { Debug.Log("[WorkerBench] " + l); sb.Append(l).Append('\n'); }
            string line = string.Format("RESULT: {0} pass, {1} fail{2}",
                          passed, failed, failed == 0 ? " - ALL GREEN" : " - TUNING NEEDED");
            Debug.Log("[WorkerBench] " + line);
            sb.Append(line).Append('\n');
            report = sb.ToString();
            try { System.IO.File.WriteAllText(
                      Application.dataPath + "/Phase1/qa_worker_bench.txt", report); }
            catch { }
            finished = true;
        }
    }
}
#endif
