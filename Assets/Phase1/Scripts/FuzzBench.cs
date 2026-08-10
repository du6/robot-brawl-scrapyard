// ===========================================================================
// FuzzBench — M4's malformed-snapshot corpus. 2026-08-09.
//
//   RobotBrawl.Phase0.FuzzBench.Run();      // play mode: Judge borrows the bay
//
// §M4 acceptance: "a deliberately malformed snapshot corpus (10+ cases) all
// land REJECTED with reasons", and the sentence that defines the whole test:
//
//     "malformed payloads must land in REJECTED, never crash a worker —
//      THE WORKER'S CRASH IS THE FUZZ ORACLE"
//
// So this bench asserts three things per case, and the first is the one that
// matters:
//
//   1. Judge did not THROW. An exception here is a worker that dies on a
//      payload a stranger uploaded, which takes the queue with it.
//   2. The snapshot was not accepted as legal. A malformed build that judges
//      LEGAL is worse than a crash — it goes on the ladder.
//   3. There is a REASON. "REJECTED" with an empty fail_reasons list tells
//      the owner nothing and cannot be acted on.
//
// ⚠ Judge CLOBBERS the builder's bay (it loads each payload to measure it),
// so this saves and restores exactly as ValidateWorkerLoop does. Every case
// below is checked against owen's bay afterwards.
//
// A case that is REFUSED EARLIER — at upload, by the API's sha/envelope
// checks — never reaches a worker at all. Those live in api_smoke; these are
// the ones that get past the front door.
// ===========================================================================
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RobotBrawl.Phase0
{
    public class FuzzBench : MonoBehaviour
    {
        static int passed, failed;
        static readonly List<string> log = new List<string>();
        public int passedRun, failedRun;
        public bool finished;

        static void Check(bool ok, string what)
        {
            if (ok) { passed++; log.Add("PASS  " + what); }
            else { failed++; log.Add("FAIL  " + what); }
        }
        static void Note(string s) { log.Add("      " + s); }

        public static FuzzBench Run()
        {
            return new GameObject("fuzz_bench").AddComponent<FuzzBench>();
        }

        /// <summary>An envelope carrying an arbitrary payload string, with a
        /// CORRECT sha over it — these have to survive the API's integrity
        /// gate to be interesting. The point is a well-formed envelope around
        /// a hostile payload.</summary>
        static string Env(string payload)
        {
            return "{\"kind\":\"robotbrawl.snapshot\",\"envelopeVersion\":1,"
                 + "\"clientVersion\":\"fuzz\",\"payload\":" + RobotWorker.Str(payload)
                 + ",\"sha256\":" + RobotWorker.Str(RobotSnapshot.Sha256Hex(payload)) + "}";
        }

        static readonly string Stamp = BuilderManager.SNAP_STAMP;

        static List<KeyValuePair<string, string>> Corpus()
        {
            var c = new List<KeyValuePair<string, string>>();
            void Add(string name, string envelope) =>
                c.Add(new KeyValuePair<string, string>(name, envelope));

            // ---- the payload is not a snapshot at all --------------------
            Add("empty payload", Env(""));
            Add("not json", Env("this is not json at all"));
            Add("truncated json", Env("{\"payloadVersion\":1,\"robotName\":\"x\""));
            Add("json array, not an object", Env("[1,2,3]"));
            Add("json null", Env("null"));

            // ---- it is a snapshot, but a hostile one ---------------------
            Add("no build field", Env("{\"payloadVersion\":1,\"robotName\":\"NoBuild\",\"program\":\"\"}"));
            Add("empty build", Env("{\"payloadVersion\":1,\"robotName\":\"E\",\"build\":\"\",\"program\":\"\"}"));
            Add("build with no stamp", Env("{\"payloadVersion\":1,\"robotName\":\"NS\",\"build\":\"core|0,0,0|0|0,0,0|Aluminum\",\"program\":\"\"}"));
            Add("unknown part id", Env("{\"payloadVersion\":1,\"robotName\":\"UP\",\"build\":" + RobotWorker.Str(Stamp + "\nnotapart|0.000,0.700,0.000|0|0.00,0.00,0.00|Aluminum\n") + ",\"program\":\"\"}"));
            Add("unknown material", Env("{\"payloadVersion\":1,\"robotName\":\"UM\",\"build\":" + RobotWorker.Str(Stamp + "\ncore|0.000,0.700,0.000|0|0.00,0.00,0.00|Unobtanium\n") + ",\"program\":\"\"}"));
            Add("garbage coordinates", Env("{\"payloadVersion\":1,\"robotName\":\"GC\",\"build\":" + RobotWorker.Str(Stamp + "\ncore|abc,def,ghi|0|0,0,0|Aluminum\n") + ",\"program\":\"\"}"));
            Add("NaN coordinates", Env("{\"payloadVersion\":1,\"robotName\":\"NAN\",\"build\":" + RobotWorker.Str(Stamp + "\ncore|NaN,NaN,NaN|0|0,0,0|Aluminum\n") + ",\"program\":\"\"}"));
            Add("absurd coordinates", Env("{\"payloadVersion\":1,\"robotName\":\"BIG\",\"build\":" + RobotWorker.Str(Stamp + "\ncore|1e30,1e30,1e30|0|0,0,0|Aluminum\n") + ",\"program\":\"\"}"));
            Add("future payloadVersion", Env("{\"payloadVersion\":9999,\"robotName\":\"FV\",\"build\":" + RobotWorker.Str(Stamp + "\ncore|0.000,0.700,0.000|0|0.00,0.00,0.00|Aluminum\n") + ",\"program\":\"\"}"));
            Add("truncated build line", Env("{\"payloadVersion\":1,\"robotName\":\"TB\",\"build\":" + RobotWorker.Str(Stamp + "\ncore|0.000,0.700\n") + ",\"program\":\"\"}"));
            Add("control characters in name", Env("{\"payloadVersion\":1,\"robotName\":\"\\u0000\\u0007bad\",\"build\":" + RobotWorker.Str(Stamp + "\ncore|0.000,0.700,0.000|0|0.00,0.00,0.00|Aluminum\n") + ",\"program\":\"\"}"));

            // ---- size, the classic worker-killer -------------------------
            var many = new System.Text.StringBuilder(Stamp);
            for (int i = 0; i < 4000; i++) many.Append("\ncore|0.000,0.700,0.000|0|0.00,0.00,0.00|Aluminum");
            Add("4000 parts", Env("{\"payloadVersion\":1,\"robotName\":\"HUGE\",\"build\":"
                                  + RobotWorker.Str(many.ToString()) + ",\"program\":\"\"}"));

            // ---- the envelope itself is wrong ---------------------------
            // These would normally be refused at upload, but a worker must not
            // assume the front door was locked.
            Add("envelope is not json", "not an envelope");
            Add("envelope missing payload", "{\"kind\":\"robotbrawl.snapshot\",\"sha256\":\"\"}");
            Add("sha does not match payload",
                "{\"kind\":\"robotbrawl.snapshot\",\"envelopeVersion\":1,\"clientVersion\":\"fuzz\","
                + "\"payload\":\"{}\",\"sha256\":\"" + new string('0', 64) + "\"}");
            return c;
        }

        IEnumerator Start()
        {
            passed = 0; failed = 0; log.Clear();
            var bm = UnityEngine.Object.FindFirstObjectByType<BuilderManager>();
            if (bm == null) { Check(false, "BuilderManager in scene"); Finish(); yield break; }

            string ownerBay = bm.SnapshotString();
            var savedData = Career.Data;
            bool savedAuto = Career.autosave;
            Career.autosave = false;

            var corpus = Corpus();
            log.Add("== the corpus: " + corpus.Count + " malformed snapshots ==");
            Note("§M4 asks for 10+; a worker that survives 20 is the claim being made.");

            int crashed = 0, accepted = 0, reasonless = 0;
            foreach (var kv in corpus)
            {
                var job = new WorkerJob { id = 1, kind = "VALIDATE", snapshotId = "fuzz-" + kv.Key };
                JudgeResult r = null;
                string threw = null;
                try { r = RobotWorker.Judge(bm, job, kv.Value, null, "fuzz"); }
                catch (Exception e) { threw = e.GetType().Name + ": " + e.Message; }

                if (threw != null)
                {
                    crashed++;
                    Check(false, kv.Key + " — Judge THREW (" + threw + "). This is a worker a stranger can kill.");
                    continue;
                }
                bool legal = r != null && r.outcome != null && r.outcome.legal;
                if (legal) { accepted++; Check(false, kv.Key + " — judged LEGAL. A malformed build on the ladder is worse than a crash."); continue; }

                bool hasReason = r != null && r.outcome != null
                              && ((r.outcome.failReasons != null && r.outcome.failReasons.Count > 0)
                                  || !string.IsNullOrEmpty(r.note));
                if (!hasReason) { reasonless++; Check(false, kv.Key + " — refused with NO reason, which nobody can act on."); continue; }

                Check(true, kv.Key + " — refused, with a reason");
            }

            log.Add("== the oracle ==");
            Check(crashed == 0, "not one payload crashed the worker (" + corpus.Count + " cases)");
            Check(accepted == 0, "not one malformed payload was judged legal");
            Check(reasonless == 0, "every refusal carries a reason");

            log.Add("== owner state ==");
            // Judge CLOBBERS the bay by design — it loads each payload in
            // order to measure it, and says so in its own doc comment. The
            // CALLER restores, exactly as ValidateWorkerLoop does at line 82.
            // The first version of this bench asserted Judge preserved the bay
            // and failed; the check was wrong, not the loader. What actually
            // matters is that the bay is owen's again once the worker is done.
            if (ownerBay != null) bm.LoadSnapshot(ownerBay);
            Check(Career.Data == savedData, "the career object was never swapped");
            Check(bm.SnapshotString() == ownerBay,
                  "the bay is owen's again after 20 hostile payloads, once restored the way the worker restores it");
            Career.autosave = savedAuto;

            Finish();
        }

        void Finish()
        {
            passedRun = passed; failedRun = failed;
            log.Add(" RESULT: " + passed + " pass, " + failed + " fail"
                    + (failed == 0 ? " - ALL GREEN" : ""));
            Debug.Log("[FuzzBench] RESULT: " + passed + " pass, " + failed + " fail");
            try
            {
                System.IO.File.WriteAllText(Application.dataPath + "/Phase1/qa_fuzz_bench.txt",
                                            string.Join("\n", log.ToArray()) + "\n");
            }
            catch (Exception e) { Debug.LogWarning("could not write report: " + e.Message); }
            finished = true;
            if (gameObject != null) Destroy(gameObject, 0.5f);
        }
    }
}
