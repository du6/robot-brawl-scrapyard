// ===========================================================================
// RobotWorker.cs — Multiplayer v3, M1: the VALIDATE worker.
//
// Design doc §5.2 and §5.3. This is the Unity half of "one implementation,
// zero drift": the worker runs the REAL game code against an uploaded
// payload, so the same Validate() that refuses a robot in the builder
// refuses it on the server. The API stores what this returns and does not
// second-guess it (Program.cs, /v1/worker/jobs/{id}/validate-result).
//
// The loop: claim -> fetch the payload from storage -> Open() (hash
// verified) -> load it into the builder -> Describe() -> category ->
// POST validate-result, with heartbeats throughout.
//
// ---------------------------------------------------------------------------
// ⚠ TRAP 1 — payloadSha256 IS NOT THE HASH OF THE BYTES YOU FETCH.
//
// Found by reading the upload path before writing this file, which is the
// only reason it is not a bug. `POST /v1/snapshots` stores the WHOLE
// ENVELOPE as the blob, but records `snapshots.sha256` = the envelope's own
// `sha256` FIELD, which is the hash of `env.payload` — the inner string, not
// the outer document:
//
//     blob bytes      = {"kind":…,"payload":"{…}","sha256":"abc…"}
//     job.PayloadSha  = abc…  =  SHA256(env.payload)
//     SHA256(blob)    = something else entirely, and nothing records it
//
// So the obvious check — hash what you downloaded, compare to the job —
// FAILS ON EVERY LEGITIMATE SNAPSHOT. The correct verification is two
// separate claims, and it needs both:
//
//   INTEGRITY  RobotSnapshot.Open() already refuses a payload whose hash
//              does not match the envelope's own sha256. Free, and it was
//              unreachable until this worker existed.
//   IDENTITY   env.sha256 == job.PayloadSha256 — the blob really is the
//              snapshot this job names, not a different one swapped into
//              storage at that key.
//
// Together: SHA256(payload) equals what the database recorded at upload.
// Neither alone is enough, and integrity alone is what you get if you only
// call Open() and move on.
//
// ---------------------------------------------------------------------------
// ⚠ TRAP 2 — A FETCH FAILURE IS NOT AN ILLEGAL ROBOT.
//
// The single most damaging thing this worker could do is answer a question
// it was not asked. If storage is unreachable, or the URL 404s, or the
// process dies mid-download, the robot is not illegal — we simply do not
// know yet. Posting `legal:false` in that case writes REJECTED onto a
// perfectly good snapshot, permanently, on the strength of a network blip,
// and the player is told their robot is bad.
//
// So Judge() returns an ACTION as well as a verdict:
//   PostVerdict     we read the payload and reached a conclusion about it
//   LeaveForRetry   we never got to read it — post NOTHING, stop
//                   heartbeating, and let the 5-minute visibility timeout
//                   return the job to the queue (§5.3)
//
// Doing nothing is a real answer, and the queue is already built to handle
// it. A sha mismatch is deliberately NOT in this category: the bytes at that
// key hash to the wrong thing and will still hash to the wrong thing on the
// next attempt, so retrying is an infinite loop. That is a verdict.
//
// ---------------------------------------------------------------------------
// ⚠ TRAP 3 — JsonUtility CANNOT SEND null, AND THIS ENDPOINT NEEDS IT.
//
// `SnapshotMeta.category` is "" when a robot has no weight category.
// `snapshots.category` is `TEXT CHECK (category IS NULL OR category IN (…))`
// — it accepts NULL and REJECTS "". Unity's JsonUtility serializes a null
// string as "", so the one tool you would reach for produces exactly the
// value the database refuses, and it fails inside the API's UPDATE at the
// far end of a completed job. The result JSON is therefore hand-built
// below, and `category` is emitted as a bare null. WorkerBench asserts the
// literal bytes.
// ===========================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace RobotBrawl.Phase0
{
    /// <summary>The subset of the API's ClaimedJob a VALIDATE worker needs.
    /// FIGHT jobs fill Challenger*/Defender*/Arena/Seeds instead; they are
    /// carried here so a FIGHT worker has them, and ignored for now.</summary>
    [Serializable]
    public class WorkerJob
    {
        public long id;
        public string kind = "";
        public string snapshotId = "";
        public string matchId = "";
        public int attempts;
        public string payloadUrl = "";
        public string payloadSha256 = "";

        // A FIGHT job fills these instead — exactly one side is populated,
        // enforced server-side by the match_jobs CHECK. See ClaimedJob in
        // Infra.cs: a VALIDATE job fills Payload*, a FIGHT job fills
        // Challenger*/Defender* plus arena and seeds.
        public string challengerUrl = "";
        public string challengerSha256 = "";
        public string defenderUrl = "";
        public string defenderSha256 = "";
        public string arena = "";
        public int[] seeds = new int[0];

        public bool IsValidate { get { return kind == "VALIDATE"; } }
        public bool IsFight { get { return kind == "FIGHT"; } }
    }

    /// <summary>What gets POSTed to fight-result. Mirrors the API's
    /// FightResult record field for field. Same hand-built-JSON rule as
    /// ValidateOutcome: JsonUtility cannot emit a bare null and would send ""
    /// where the server expects nothing.</summary>
    public class FightOutcome
    {
        public string matchId = "";
        public string workerId = "";
        /// <summary>"CHALLENGER" | "DEFENDER" | "DRAW" — the API rejects
        /// anything else with a 400 before it touches the database.</summary>
        public string verdict = "";
        public List<string> replayUrls = new List<string>();
        public List<string> bouts = new List<string>();
    }

    /// <summary>What gets POSTed to validate-result. Mirrors the API's
    /// ValidateResult record field for field.</summary>
    public class ValidateOutcome
    {
        public string snapshotId = "";
        public string workerId = "";
        public bool legal;
        public int massKg;
        public float aabbX, aabbY, aabbZ;
        /// <summary>"" here becomes a bare null on the wire. See TRAP 3.</summary>
        public string category = "";
        public List<string> partsManifest = new List<string>();
        public string programHash = "";
        public List<string> failReasons = new List<string>();
    }

    public enum WorkerAction
    {
        /// <summary>We read the payload and reached a conclusion. Post it.</summary>
        PostVerdict,
        /// <summary>We never got to read it. Post nothing; let the job time
        /// out and come back to the queue. See TRAP 2.</summary>
        LeaveForRetry,
    }

    public class JudgeResult
    {
        public WorkerAction action;
        public ValidateOutcome outcome;
        /// <summary>Always set — the operator-facing reason, including for
        /// LeaveForRetry, where nothing is posted and this log line is the
        /// only trace the attempt ever happened.</summary>
        public string note = "";
    }

    public static class RobotWorker
    {
        public const string HEADER_WORKER_KEY = "X-Worker-Key";

        // ------------------------------------------------------------ judge
        /// <summary>The whole decision, with no I/O in it. Everything that can
        /// go wrong with a snapshot can be provoked by passing a different
        /// string here, which is why WorkerBench needs no server.
        ///
        /// ⚠ This CLOBBERS the builder's current bay — it loads the payload's
        /// build to measure it. The caller saves and restores; see
        /// ValidateWorkerLoop.</summary>
        public static JudgeResult Judge(BuilderManager bm, WorkerJob job,
                                        string blobText, string fetchError,
                                        string workerId)
        {
            var r = new JudgeResult();
            var o = new ValidateOutcome();
            o.snapshotId = job != null ? job.snapshotId : "";
            o.workerId = workerId ?? "";
            r.outcome = o;

            // TRAP 2: never answer a question we could not read.
            if (!string.IsNullOrEmpty(fetchError))
            {
                r.action = WorkerAction.LeaveForRetry;
                r.note = "could not fetch the payload (" + fetchError +
                         ") — leaving the job for retry, NOT rejecting the snapshot";
                return r;
            }
            if (string.IsNullOrEmpty(blobText))
            {
                r.action = WorkerAction.LeaveForRetry;
                r.note = "storage returned an empty body — leaving the job for retry";
                return r;
            }

            r.action = WorkerAction.PostVerdict;

            // INTEGRITY: Open() verifies SHA256(env.payload) == env.sha256 and
            // refuses anything half-readable. See TRAP 1.
            var env = SnapshotEnvelope.FromJson(blobText);
            SnapshotPayload payload;
            string err;
            if (!RobotSnapshot.Open(env, out payload, out err))
            {
                o.legal = false;
                o.failReasons.Add(err ?? "snapshot: unreadable envelope");
                r.note = "rejected: " + (err ?? "unreadable envelope");
                return r;
            }

            // IDENTITY: is this the snapshot the job actually named? Compared
            // case-insensitively because the API lowercases on the way in and
            // Sha256Hex emits lowercase, so a mismatch of case would be a
            // false rejection rather than a real one.
            if (!string.IsNullOrEmpty(job.payloadSha256) &&
                !string.Equals(env.sha256, job.payloadSha256, StringComparison.OrdinalIgnoreCase))
            {
                o.legal = false;
                o.failReasons.Add("snapshot: storage holds a different snapshot than the job names " +
                                  "(envelope " + Short(env.sha256) + ", job " + Short(job.payloadSha256) + ")");
                r.note = "rejected: blob/job hash identity mismatch";
                return r;
            }

            int n = bm.LoadSnapshot(payload.build);
            if (n <= 0)
            {
                o.legal = false;
                o.failReasons.Add("snapshot: build loaded 0 parts");
                r.note = "rejected: build loaded 0 parts";
                return r;
            }

            // The real game code, against the real builder state. This is the
            // whole point of the worker being Unity (§5.2).
            var meta = RobotSnapshot.Describe(bm, payload);
            o.legal = meta.legal;
            o.massKg = meta.massKg;
            o.aabbX = meta.aabb.x; o.aabbY = meta.aabb.y; o.aabbZ = meta.aabb.z;
            o.category = meta.category;
            o.programHash = meta.programHash;
            o.partsManifest = meta.partsManifest ?? new List<string>();
            o.failReasons = meta.failReasons ?? new List<string>();

            r.note = meta.legal
                ? "legal: " + meta.massKg + " kg, " + meta.category + ", " + n + " parts"
                : "rejected: " + string.Join(" / ", o.failReasons.ToArray());
            return r;
        }

        static string Short(string sha)
        {
            if (string.IsNullOrEmpty(sha)) return "(none)";
            return sha.Length <= 12 ? sha : sha.Substring(0, 12) + "…";
        }

        // ------------------------------------------------------------- json
        /// <summary>Hand-built because JsonUtility cannot emit null, and this
        /// endpoint requires it for `category`. See TRAP 3.</summary>
        public static string ResultJson(ValidateOutcome o)
        {
            var sb = new StringBuilder(512);
            sb.Append('{');
            sb.Append("\"snapshotId\":").Append(Str(o.snapshotId)).Append(',');
            sb.Append("\"workerId\":").Append(Str(o.workerId)).Append(',');
            sb.Append("\"legal\":").Append(o.legal ? "true" : "false").Append(',');
            sb.Append("\"massKg\":").Append(o.massKg.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"aabbX\":").Append(Num(o.aabbX)).Append(',');
            sb.Append("\"aabbY\":").Append(Num(o.aabbY)).Append(',');
            sb.Append("\"aabbZ\":").Append(Num(o.aabbZ)).Append(',');
            // The one field this whole function exists for.
            sb.Append("\"category\":").Append(string.IsNullOrEmpty(o.category) ? "null" : Str(o.category)).Append(',');
            sb.Append("\"partsManifest\":").Append(Arr(o.partsManifest)).Append(',');
            sb.Append("\"programHash\":").Append(string.IsNullOrEmpty(o.programHash) ? "\"\"" : Str(o.programHash)).Append(',');
            sb.Append("\"failReasons\":").Append(Arr(o.failReasons));
            sb.Append('}');
            return sb.ToString();
        }

        static string Num(float f)
        {
            // Invariant culture on purpose: a comma decimal separator is not
            // JSON, and the editor's locale is not the server's.
            return f.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        }

        static string Arr(List<string> items)
        {
            if (items == null || items.Count == 0) return "[]";
            var sb = new StringBuilder();
            sb.Append('[');
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Str(items[i]));
            }
            sb.Append(']');
            return sb.ToString();
        }

        /// <summary>JSON string escaping. failReasons carry validator prose
        /// with quotes and apostrophes in it, and a build name is player
        /// input, so this is a real requirement rather than a formality.</summary>
        public static string Str(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        /// <summary>Parse the claim response. Written by hand rather than with
        /// JsonUtility because the API omits nulls and JsonUtility would turn
        /// a missing payloadUrl into "" indistinguishably from a present empty
        /// one — and telling those apart is how a FIGHT job is recognised.</summary>
        public static WorkerJob ParseClaim(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var j = new WorkerJob();
            long id;
            if (!long.TryParse(Field(json, "id"), out id)) return null;
            j.id = id;
            j.kind = Field(json, "kind") ?? "";
            j.snapshotId = Field(json, "snapshotId") ?? "";
            j.matchId = Field(json, "matchId") ?? "";
            int att; int.TryParse(Field(json, "attempts"), out att); j.attempts = att;
            j.payloadUrl = Field(json, "payloadUrl") ?? "";
            j.payloadSha256 = Field(json, "payloadSha256") ?? "";
            j.challengerUrl = Field(json, "challengerUrl") ?? "";
            j.challengerSha256 = Field(json, "challengerSha256") ?? "";
            j.defenderUrl = Field(json, "defenderUrl") ?? "";
            j.defenderSha256 = Field(json, "defenderSha256") ?? "";
            j.arena = Field(json, "arena") ?? "";
            j.seeds = IntArrayField(json, "seeds");
            return j;
        }

        /// <summary>Top-level int array, e.g. "seeds":[7,8,9]. Field() stops at
        /// the first delimiter and would return "[7" — seeds are the one
        /// non-scalar in the claim response, so they get their own reader
        /// rather than a JSON library.</summary>
        public static int[] IntArrayField(string json, string key)
        {
            string needle = "\"" + key + "\"";
            int k = json.IndexOf(needle, StringComparison.Ordinal);
            if (k < 0) return new int[0];
            int open = json.IndexOf('[', k + needle.Length);
            if (open < 0) return new int[0];
            int close = json.IndexOf(']', open);
            if (close < 0) return new int[0];
            string body = json.Substring(open + 1, close - open - 1).Trim();
            if (body.Length == 0) return new int[0];
            var parts = body.Split(',');
            var outv = new List<int>();
            for (int i = 0; i < parts.Length; i++)
            {
                int v;
                if (int.TryParse(parts[i].Trim(), out v)) outv.Add(v);
            }
            return outv.ToArray();
        }

        /// <summary>Hand-built for the same reason ResultJson is: a verdict is
        /// a bare string and replayUrls is a real array, and JsonUtility's
        /// idea of both is wrong on the wire.</summary>
        public static string FightResultJson(FightOutcome o)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"matchId\":").Append(Str(o.matchId)).Append(',');
            sb.Append("\"workerId\":").Append(Str(o.workerId)).Append(',');
            sb.Append("\"verdict\":").Append(Str(o.verdict)).Append(',');
            sb.Append("\"replayUrls\":[");
            for (int i = 0; i < o.replayUrls.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Str(o.replayUrls[i]));
            }
            sb.Append("],\"bouts\":[");
            for (int i = 0; i < o.bouts.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Str(o.bouts[i]));
            }
            sb.Append("]}");
            return sb.ToString();
        }

        /// <summary>Value of a top-level key; null when absent or JSON null.
        /// Deliberately small — the claim response is a flat object of
        /// scalars, and pulling in a JSON library for it would be the larger
        /// risk.</summary>
        public static string Field(string json, string key)
        {
            string needle = "\"" + key + "\"";
            int k = json.IndexOf(needle, StringComparison.Ordinal);
            if (k < 0) return null;
            int i = json.IndexOf(':', k + needle.Length);
            if (i < 0) return null;
            i++;
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
            if (i >= json.Length) return null;
            if (json[i] == 'n' && i + 4 <= json.Length && json.Substring(i, 4) == "null") return null;
            if (json[i] == '"')
            {
                var sb = new StringBuilder();
                i++;
                while (i < json.Length && json[i] != '"')
                {
                    if (json[i] == '\\' && i + 1 < json.Length)
                    {
                        i++;
                        switch (json[i])
                        {
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'u':
                                if (i + 4 < json.Length)
                                {
                                    sb.Append((char)Convert.ToInt32(json.Substring(i + 1, 4), 16));
                                    i += 4;
                                }
                                break;
                            default: sb.Append(json[i]); break;
                        }
                        i++;
                    }
                    else sb.Append(json[i++]);
                }
                return sb.ToString();
            }
            int e = i;
            while (e < json.Length && json[e] != ',' && json[e] != '}' && !char.IsWhiteSpace(json[e])) e++;
            return json.Substring(i, e - i);
        }
    }

    // ------------------------------------------------------------- transport
    /// <summary>Everything that touches the network, behind one seam, so the
    /// loop above can be driven by a stub in a bench with no API, no
    /// Postgres, and no Docker.</summary>
    public interface IWorkerTransport
    {
        /// <summary>job is null when the queue is empty (the API answers 204).</summary>
        IEnumerator Claim(string workerId, Action<WorkerJob, string> done);
        IEnumerator Fetch(string url, Action<string, string> done);
        IEnumerator PostValidate(long jobId, string resultJson, Action<bool, string> done);
        IEnumerator Heartbeat(long jobId, string workerId, Action<bool> done);
    }

    /// <summary>The FIGHT half, added 2026-08-09. Deliberately a SEPARATE
    /// interface that extends the validate one rather than two more methods on
    /// it: StubWorkerTransport implements IWorkerTransport and is what
    /// WorkerBench's 39/39 runs against. Widening the base interface would
    /// have broken that bench to add a feature it does not test, and a green
    /// bench you had to edit to keep compiling is a bench you have stopped
    /// trusting.</summary>
    public interface IFightTransport : IWorkerTransport
    {
        /// <summary>done(url, err). The replay is opaque bytes to the API
        /// (§5.4: it is a recording, never re-simulated).</summary>
        IEnumerator UploadReplay(string matchId, string replayJson, Action<string, string> done);

        /// <summary>The RECORDING, as raw bytes — a .rbr.gz ReplayRecorder
        /// wrote. This is the one a client can actually play; the JSON form
        /// above is a scorecard. Sent raw rather than base64'd into the JSON
        /// body because a real bout is ~200 KB and base64 would add a third
        /// to every one of them for nothing.</summary>
        IEnumerator UploadReplayFile(string matchId, byte[] bytes, Action<string, string> done);
        IEnumerator PostFight(long jobId, string resultJson, Action<bool, string> done);
    }

    /// <summary>The real one. First UnityWebRequest in this client.</summary>
    public class HttpWorkerTransport : IFightTransport
    {
        readonly string _baseUrl, _workerKey;
        public HttpWorkerTransport(string baseUrl, string workerKey)
        {
            _baseUrl = (baseUrl ?? "").TrimEnd('/');
            _workerKey = workerKey ?? "";
        }

        UnityWebRequest Post(string path, string json)
        {
            var req = new UnityWebRequest(_baseUrl + path, "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json ?? "{}"));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader(RobotWorker.HEADER_WORKER_KEY, _workerKey);
            return req;
        }

        public IEnumerator Claim(string workerId, Action<WorkerJob, string> done)
        {
            using (var req = Post("/v1/worker/jobs/claim",
                                  "{\"workerId\":" + RobotWorker.Str(workerId) + "}"))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                { done(null, req.error ?? "claim failed"); yield break; }
                // 204 No Content is the queue being empty — not an error, and
                // not a job. Treating it as either is a busy loop or a crash.
                if (req.responseCode == 204 || string.IsNullOrEmpty(req.downloadHandler.text))
                { done(null, null); yield break; }
                done(RobotWorker.ParseClaim(req.downloadHandler.text), null);
            }
        }

        public IEnumerator Fetch(string url, Action<string, string> done)
        {
            using (var req = UnityWebRequest.Get(url))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                { done(null, req.error ?? "fetch failed"); yield break; }
                done(req.downloadHandler.text, null);
            }
        }

        public IEnumerator PostValidate(long jobId, string resultJson, Action<bool, string> done)
        {
            using (var req = Post("/v1/worker/jobs/" + jobId + "/validate-result", resultJson))
            {
                yield return req.SendWebRequest();
                bool ok = req.result == UnityWebRequest.Result.Success;
                done(ok, ok ? null : (req.downloadHandler != null ? req.downloadHandler.text : req.error));
            }
        }

        public IEnumerator UploadReplay(string matchId, string replayJson, Action<string, string> done)
        {
            // The replay travels as a JSON STRING inside the request, the same
            // shape a snapshot payload uses: the API stores bytes and never
            // parses them.
            string body = "{\"replay\":" + RobotWorker.Str(replayJson) + "}";
            using (var req = Post("/v1/worker/matches/" + matchId + "/replay", body))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                { done(null, req.downloadHandler != null ? req.downloadHandler.text : req.error); yield break; }
                done(RobotWorker.Field(req.downloadHandler.text, "url"), null);
            }
        }

        public IEnumerator UploadReplayFile(string matchId, byte[] bytes, Action<string, string> done)
        {
            var req = new UnityWebRequest(_baseUrl + "/v1/worker/matches/" + matchId + "/replay", "POST");
            req.uploadHandler = new UploadHandlerRaw(bytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            // NOT application/json — the API branches on this, and sending
            // gzip under a json content type would store the bytes as a
            // "summary" nothing can play.
            req.SetRequestHeader("Content-Type", "application/octet-stream");
            req.SetRequestHeader(RobotWorker.HEADER_WORKER_KEY, _workerKey);
            using (req)
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                { done(null, req.downloadHandler != null ? req.downloadHandler.text : req.error); yield break; }
                done(RobotWorker.Field(req.downloadHandler.text, "url"), null);
            }
        }

        public IEnumerator PostFight(long jobId, string resultJson, Action<bool, string> done)
        {
            using (var req = Post("/v1/worker/jobs/" + jobId + "/fight-result", resultJson))
            {
                yield return req.SendWebRequest();
                bool ok = req.result == UnityWebRequest.Result.Success;
                done(ok, ok ? null : (req.downloadHandler != null ? req.downloadHandler.text : req.error));
            }
        }

        public IEnumerator Heartbeat(long jobId, string workerId, Action<bool> done)
        {
            using (var req = Post("/v1/worker/jobs/" + jobId + "/heartbeat",
                                  "{\"workerId\":" + RobotWorker.Str(workerId) + "}"))
            {
                yield return req.SendWebRequest();
                done(req.result == UnityWebRequest.Result.Success);
            }
        }
    }
}
