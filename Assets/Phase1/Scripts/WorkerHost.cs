// ===========================================================================
// WorkerHost.cs — the thing that was missing. 2026-08-09.
//
// Every piece of the worker existed and was benched: RobotWorker (transport
// and judging), ValidateWorkerLoop, FightWorkerLoop, MatchRunner. All of it
// ran in owen's EDITOR, driven by a bench, one job at a time, against a stub.
//
// Nothing ran it in a loop, and nothing ran it in the cloud. So the live
// ladder had an API, a queue, blob storage and alerting — and no worker. A
// player could upload a robot and it would sit PENDING until the reaper gave
// up and rejected it. §M1's acceptance ("upload two robots, watch the replay
// from the cloud") could not pass. Every production job worked on 2026-08-09
// was claimed by hand with curl.
//
// This is the host: claim, work, repeat, until told to stop.
//
//   RobotBrawl.Phase0.WorkerHost.Launch();      // editor or headless
//
// CONFIGURED BY ENVIRONMENT, because it has to run in a container where there
// is nobody to click anything:
//
//   RB_API_URL      base url          (default http://localhost:5000)
//   RB_WORKER_KEY   shared secret     (required; refuses to start without it)
//   RB_WORKER_ID    identity          (default rb-worker-<random>)
//   RB_WORKER_KINDS VALIDATE|FIGHT|BOTH   (default BOTH)
//   RB_IDLE_SECONDS poll pause        (default 3)
//   RB_MAX_JOBS     stop after N      (default 0 = forever; the bench uses it)
//
// ---------------------------------------------------------------------------
// WHY IT CLAIMS BY KIND RATHER THAN CLAIMING ANYTHING AND SORTING IT OUT.
//
// Both loops refuse a job of the wrong kind AFTER claiming it, and leave it
// CLAIMED for the reaper rather than failing it. That is the polite choice and
// it is still too late: `attempts` increments ON CLAIM. Three wrong claims and
// a job that was never broken is FAILED — and since 2026-08-09 a failed
// VALIDATE also REJECTS the player's snapshot, so a legitimate robot is
// refused with a reason that is not true.
//
// So each loop gets its own transport bound to its own kind (claim_job.sql
// $2), and BOTH mode alternates between two of them. A wrong-kind claim is
// then impossible rather than merely tidied up afterwards.
//
// ---------------------------------------------------------------------------
// ⚠ THE CAREER SAVE. FightManager.End() calls Progression.OnMatchEnd
// unconditionally, so anything that runs a fight writes owen's career unless
// stopped. A headless container has no career worth keeping and the file it
// would write is a container-local scratch file — but this host also runs IN
// THE EDITOR, where the save is real and is hard rule 5. Career.autosave is
// forced off for the host's whole lifetime and restored on shutdown.
// ===========================================================================
using System;
using System.Collections;
using UnityEngine;

namespace RobotBrawl.Phase0
{
    public class WorkerHost : MonoBehaviour
    {
        public static WorkerHost Instance;
        public static bool Running;
        public static string LastError = "";
        public static int Claimed, Validated, Fought, Idle, Errors, LeftForRetry;
        public static string Status = "not started";

        string _url, _key, _id, _kinds;
        float _idleSeconds;
        int _maxJobs;
        bool _savedAutosave;
        bool _stop;

        static string Env(string k, string dflt)
        {
            var v = Environment.GetEnvironmentVariable(k);
            return string.IsNullOrEmpty(v) ? dflt : v;
        }

        /// <summary>Start the host. Safe to call twice — the second call is a
        /// no-op rather than a second poller competing for the same queue.</summary>
        public static WorkerHost Launch()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("rb_worker_host");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<WorkerHost>();
            return Instance;
        }

        public static void Stop()
        {
            if (Instance != null) Instance._stop = true;
        }

        void Awake()
        {
            _url         = Env("RB_API_URL", "http://localhost:5000");
            _key         = Env("RB_WORKER_KEY", "");
            _id          = Env("RB_WORKER_ID", "rb-worker-" + UnityEngine.Random.Range(1000, 9999));
            _kinds       = Env("RB_WORKER_KINDS", "BOTH").Trim().ToUpperInvariant();
            _idleSeconds = float.TryParse(Env("RB_IDLE_SECONDS", "3"), out var s) ? s : 3f;
            _maxJobs     = int.TryParse(Env("RB_MAX_JOBS", "0"), out var m) ? m : 0;
        }

        IEnumerator Start()
        {
            // Refuse to start rather than poll forever getting 401s. A worker
            // that looks alive and can never claim anything is worse than one
            // that never started, because the queue-depth alert stays quiet
            // either way and only one of them is obviously broken.
            if (string.IsNullOrEmpty(_key))
            {
                LastError = "RB_WORKER_KEY is not set; refusing to start";
                Status = "misconfigured";
                Debug.LogError("[WorkerHost] " + LastError);
                Quit(2);
                yield break;
            }
            if (_kinds != "BOTH" && _kinds != "VALIDATE" && _kinds != "FIGHT")
            {
                LastError = "RB_WORKER_KINDS must be VALIDATE, FIGHT or BOTH (got '" + _kinds + "')";
                Status = "misconfigured";
                Debug.LogError("[WorkerHost] " + LastError);
                Quit(2);
                yield break;
            }

            var bm = UnityEngine.Object.FindFirstObjectByType<BuilderManager>();
            if (bm == null)
            {
                LastError = "no BuilderManager in the scene; the worker runs the real game code and needs one";
                Status = "no builder";
                Debug.LogError("[WorkerHost] " + LastError);
                Quit(3);
                yield break;
            }

            _savedAutosave = Career.autosave;
            Career.autosave = false;      // hard rule 5, for the whole lifetime

            bool doValidate = _kinds == "BOTH" || _kinds == "VALIDATE";
            bool doFight    = _kinds == "BOTH" || _kinds == "FIGHT";

            // One transport per kind. See the header for why this is not
            // "claim anything and sort it out".
            var vNet = doValidate ? new HttpWorkerTransport(_url, _key, "VALIDATE") : null;
            var fNet = doFight    ? new HttpWorkerTransport(_url, _key, "FIGHT")    : null;

            ValidateWorkerLoop.WorkerId = _id;
            FightWorkerLoop.WorkerId    = _id;

            Running = true;
            Status  = "polling";
            Debug.Log(string.Format(
                "[WorkerHost] up. id={0} kinds={1} api={2} idle={3}s maxJobs={4}",
                _id, _kinds, _url, _idleSeconds, _maxJobs == 0 ? "unlimited" : _maxJobs.ToString()));

            int handled = 0;
            while (!_stop)
            {
                bool didSomething = false;

                if (doValidate)
                {
                    // Count what the LOOP recorded, not what the callback
                    // handed back. The first version of this incremented
                    // Validated whenever RunOnce produced a JudgeResult — but a
                    // JudgeResult is also produced when the action is
                    // LeaveForRetry, which posts NOTHING on purpose (TRAP 2).
                    // So the very first live run reported "claimed 1,
                    // validated 1, errors 0" while the snapshot was still
                    // PENDING and the job still CLAIMED. The host looked
                    // perfectly healthy and had accomplished nothing.
                    int postsBefore = ValidateWorkerLoop.PostsSucceeded;
                    int retryBefore = ValidateWorkerLoop.JobsLeftForRetry;
                    JudgeResult jr = null;
                    yield return ValidateWorkerLoop.RunOnce(vNet, bm, r => jr = r);
                    if (!string.IsNullOrEmpty(ValidateWorkerLoop.LastError))
                    {
                        Errors++;
                        LastError = ValidateWorkerLoop.LastError;
                        Debug.LogWarning("[WorkerHost] validate: " + LastError);
                    }
                    if (ValidateWorkerLoop.JobsLeftForRetry > retryBefore)
                    {
                        // Not an error and not progress: a deliberate silence
                        // that hands the job back via the visibility timeout.
                        // It MUST be visible, because a worker stuck in a
                        // fetch-fails loop looks identical to an idle one.
                        LeftForRetry++;
                        didSomething = true;   // we did claim; do not sleep on it
                        var note = jr != null && !string.IsNullOrEmpty(jr.note) ? jr.note : "(no note)";
                        LastError = "left for retry: " + note;
                        Debug.LogWarning("[WorkerHost] " + LastError);
                    }
                    else if (ValidateWorkerLoop.PostsSucceeded > postsBefore)
                    { Validated++; Claimed++; handled++; didSomething = true; }
                    else if (jr != null)
                    { Errors++; Claimed++; didSomething = true;
                      Debug.LogWarning("[WorkerHost] validate produced a verdict that did not post"); }
                }

                if (doFight && !_stop)
                {
                    // Same rule as the validate half: a returned MatchResult
                    // is not evidence the result reached the API.
                    int fPostsBefore = FightWorkerLoop.PostsSucceeded;
                    int fRefusalsBefore = FightWorkerLoop.Refusals;
                    MatchRunner.MatchResult mr = null;
                    yield return FightWorkerLoop.RunOnce(fNet, bm, r => mr = r);
                    if (!string.IsNullOrEmpty(FightWorkerLoop.LastError))
                    {
                        Errors++;
                        LastError = FightWorkerLoop.LastError;
                        Debug.LogWarning("[WorkerHost] fight: " + LastError);
                    }
                    if (FightWorkerLoop.PostsSucceeded > fPostsBefore)
                    { Fought++; Claimed++; handled++; didSomething = true; }
                    else if (mr != null || FightWorkerLoop.Refusals > fRefusalsBefore)
                    { LeftForRetry++; Claimed++; didSomething = true; }
                }

                if (_maxJobs > 0 && handled >= _maxJobs)
                {
                    Debug.Log("[WorkerHost] handled " + handled + " job(s); RB_MAX_JOBS reached");
                    break;
                }

                if (!didSomething)
                {
                    // An empty queue is the normal state of a ladder nobody is
                    // using. Pause rather than spin: polling flat out would
                    // bill a Cloud Run CPU to discover nothing, repeatedly.
                    Idle++;
                    Status = "idle";
                    yield return new WaitForSeconds(_idleSeconds);
                }
                else
                {
                    Status = "working";
                    yield return null;
                }
            }

            Shutdown();
        }

        void Shutdown()
        {
            Career.autosave = _savedAutosave;
            Running = false;
            Status = "stopped";
            Debug.Log(string.Format(
                "[WorkerHost] down. claimed={0} validated={1} fought={2} leftForRetry={3} idlePolls={4} errors={5}",
                Claimed, Validated, Fought, LeftForRetry, Idle, Errors));
            Quit(0);
        }

        void OnApplicationQuit()
        {
            // The container gets SIGTERM on a scale-down or a redeploy. Put the
            // career flag back even then; in the editor that flag is owen's.
            Career.autosave = _savedAutosave;
            Running = false;
        }

        /// <summary>Exit the PROCESS when headless, and do nothing in the
        /// editor. Application.Quit() is a no-op in the editor anyway, but
        /// being explicit stops a future reader from "fixing" it into
        /// EditorApplication.Exit and killing owen's editor.</summary>
        void Quit(int code)
        {
            if (Application.isBatchMode) Application.Quit(code);
        }
    }
}
