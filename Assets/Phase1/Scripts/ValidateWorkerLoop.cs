// ===========================================================================
// ValidateWorkerLoop.cs — M1: the worker's outer loop.
//
// RobotWorker.Judge is the decision and has no I/O. This is the part that
// talks: claim, fetch, heartbeat, post. Kept separate so the decision can be
// benched exhaustively with a stub and no server (WorkerBench).
//
// ⚠ THIS BORROWS THE BUILDER'S BAY. Judge loads the payload's build in order
// to measure it, which clobbers whatever was on the bench. Every path out of
// RunOnce restores it — a harness that changes the bay restores the bay, and
// on a real worker VM the bay is nobody's, but in the editor it is owen's.
// ===========================================================================

using System;
using System.Collections;
using UnityEngine;

namespace RobotBrawl.Phase0
{
    public class ValidateWorkerLoop : MonoBehaviour
    {
        public static string WorkerId = "editor-worker-1";
        /// <summary>Seconds between claims when the queue is empty. §5.3 gives
        /// a job 5 minutes before it is reaped, so this is nowhere near
        /// tight enough to matter — it exists so an idle worker is not a
        /// busy loop.</summary>
        public static float IdlePollSeconds = 3f;

        public static JudgeResult LastResult;
        public static string LastPostedJson = "";
        public static string LastError = "";
        public static int JobsHandled, JobsLeftForRetry, PostsSucceeded, PostsFailed;

        /// <summary>Claim one job and see it through. Returns via `done` with
        /// null when the queue was empty.</summary>
        public static IEnumerator RunOnce(IWorkerTransport net, BuilderManager bm,
                                          Action<JudgeResult> done)
        {
            LastResult = null; LastPostedJson = ""; LastError = "";

            WorkerJob job = null; string claimErr = null;
            yield return net.Claim(WorkerId, (j, e) => { job = j; claimErr = e; });

            if (!string.IsNullOrEmpty(claimErr))
            { LastError = "claim: " + claimErr; if (done != null) done(null); yield break; }
            if (job == null)                       // 204 — queue empty, not an error
            { if (done != null) done(null); yield break; }

            if (!job.IsValidate)
            {
                // A FIGHT job is not ours. Leave it rather than failing it:
                // the endpoints for it do not exist yet (§5.3), and a worker
                // that completes a job it cannot do is worse than one that
                // lets the timeout hand it to a worker that can.
                LastError = "claimed a " + job.kind + " job; this worker only does VALIDATE";
                JobsLeftForRetry++;
                if (done != null) done(null);
                yield break;
            }

            yield return net.Heartbeat(job.id, WorkerId, ok => { });

            string blob = null, fetchErr = null;
            if (string.IsNullOrEmpty(job.payloadUrl))
                fetchErr = "the claim carried no payloadUrl";
            else
                yield return net.Fetch(job.payloadUrl, (t, e) => { blob = t; fetchErr = e; });

            // Save the bay across the measurement, and put it back on every
            // path out of here — including the ones that throw.
            string savedBay = bm != null ? bm.SnapshotString() : null;
            bool savedAuto = Career.autosave;
            Career.autosave = false;

            JudgeResult r;
            try
            {
                r = RobotWorker.Judge(bm, job, blob, fetchErr, WorkerId);
            }
            finally
            {
                if (bm != null && savedBay != null) bm.LoadSnapshot(savedBay);
                Career.autosave = savedAuto;
            }

            LastResult = r;

            if (r.action == WorkerAction.LeaveForRetry)
            {
                // Post NOTHING and stop heartbeating. The job's visibility
                // timeout returns it to the queue. See RobotWorker's TRAP 2 —
                // this silence is the correct answer, not a dropped ball.
                JobsLeftForRetry++;
                Debug.Log("[worker] job " + job.id + ": " + r.note);
                if (done != null) done(r);
                yield break;
            }

            yield return net.Heartbeat(job.id, WorkerId, ok => { });

            string json = RobotWorker.ResultJson(r.outcome);
            LastPostedJson = json;
            bool posted = false; string postErr = null;
            yield return net.PostValidate(job.id, json, (ok, e) => { posted = ok; postErr = e; });

            if (posted) { PostsSucceeded++; JobsHandled++; }
            else { PostsFailed++; LastError = "validate-result: " + (postErr ?? "failed"); }

            Debug.Log("[worker] job " + job.id + ": " + r.note + (posted ? " — posted" : " — POST FAILED"));
            if (done != null) done(r);
        }

        /// <summary>Keep claiming until stopped. Not used by the bench, which
        /// drives RunOnce directly so it can assert one job at a time.</summary>
        public IEnumerator RunForever(IWorkerTransport net, BuilderManager bm)
        {
            while (true)
            {
                bool idle = true;
                yield return RunOnce(net, bm, r => { idle = r == null; });
                if (idle) yield return new WaitForSeconds(IdlePollSeconds);
            }
        }
    }
}
