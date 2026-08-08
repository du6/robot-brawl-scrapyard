// ===========================================================================
// MatchRunner.cs — Multiplayer v3, Phase M0 piece 3: snapshot vs snapshot.
//
// Design doc: Multiplayer_V3_Design_Doc.md §1.4, §3.2, §5.1 and §5.4.
//
// This is the product path the sim worker will run (§5.1): take two uploaded
// snapshots and three seeds, fight best-of-3, emit results and replays. In
// M0 it runs in the editor, which is exactly the point — §9's risk table says
// any divergence between the headless build and the editor is a bug to find
// BEFORE the server exists, and you cannot find it without an editor-side
// runner to compare against.
//
// THREE THINGS THIS DOES THAT NO EXISTING PATH DOES
//
// 1. BOTH SIDES ARE PROGRAMS. StartFight always spawns a roster AI opponent;
//    StartCareerFight then hands only the PLAYER to a ProgramRunner. Two
//    programmed robots pushing each other has never been run in this project
//    (2026-08-07 handover, next-direction item 3). Here it is the default.
//
// 2. SEEDED SPAWNS. Nothing in the project seeds anything today. §1.4 wants
//    three different spawn seeds per challenge so a lucky wall-slam cannot
//    decide a ladder position. The seed rotates the engagement axis and
//    varies the separation — symmetrically, so a seed can never favour a side.
//
// 3. CAREER ISOLATION. FightManager.End calls Progression.OnMatchEnd
//    unconditionally. A ladder bout must not be able to pay owen's career, so
//    the runner swaps in a scratch CareerData and puts it back afterwards —
//    the same seam CareerSmoke and MatrixBench use.
//
// SPEED: via Time.timeScale, the only precedent in this codebase. The design
// doc's stepped Physics.Simulate is NOT used here and the reason is recorded
// in the M0 doc: Physics.autoSimulation is never touched anywhere in this
// project, every bench rides the automatic FixedUpdate loop, and swapping the
// whole game to script-driven stepping is new infrastructure that belongs
// with the headless worker in M1 — not smuggled into the phase whose job is
// to prove the fight loop and the replay. The seam is here: change Pace().
// ===========================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RobotBrawl.Phase0
{
    public class MatchRunner : MonoBehaviour
    {
        // ------------------------------------------------------------ result
        [Serializable]
        public class BoutResult
        {
            public int bout;
            public int seed;
            public string winner = "";     // "A" | "B" | "Draw"
            public string outcome = "";    // FightManager.Outcome name
            public string cause = "";
            public float aDealt, bDealt;
            public float simSeconds;
            /// <summary>Damage exchanges, and the sim time of the last one.
            /// deadAir = simSeconds - lastHitT is how long the bout ran after
            /// the fighting stopped.</summary>
            public int hits;
            public float lastHitT = -1f;
            public float DeadAir { get { return lastHitT < 0f ? simSeconds : simSeconds - lastHitT; } }
            public string replayPath = "";
        }

        [Serializable]
        public class MatchResult
        {
            public string matchId = "";
            public string verdict = "";    // "A" | "B" | "Draw"
            public int aWins, bWins, draws;
            public string decidedBy = "";
            public string error = "";
            public List<BoutResult> bouts = new List<BoutResult>();
            public bool Ok { get { return string.IsNullOrEmpty(error); } }
        }

        // ------------------------------------------------------------ inputs
        public SnapshotEnvelope envA, envB;
        public int[] seeds = { 1, 2, 3 };
        public float speed = 10f;
        public bool record = true;
        public bool stopWhenDecided = true;
        public float arenaHalf = 7f;
        public float wallClockCapPerBout = 90f;
        public string matchId = "m0";
        public MatchResult result;
        public bool finished;

        Action<MatchResult> onDone;

        public static MatchRunner Run(SnapshotEnvelope a, SnapshotEnvelope b, int[] seeds,
                                      string matchId, float speed, bool record,
                                      Action<MatchResult> onDone)
        {
            var go = new GameObject("match_runner");
            var mr = go.AddComponent<MatchRunner>();
            mr.envA = a; mr.envB = b;
            if (seeds != null && seeds.Length > 0) mr.seeds = seeds;
            mr.matchId = string.IsNullOrEmpty(matchId) ? "m0" : matchId;
            mr.speed = speed <= 0f ? 1f : speed;
            mr.record = record;
            mr.onDone = onDone;
            return mr;
        }

        // ---------------------------------------------------- build plumbing
        /// <summary>Parse a build snapshot into a DETACHED PlacedPart list.
        /// LoadSnapshot forces build mode and rebuilds `placed` in place, so
        /// two builds cannot both be live in the builder — but SpawnBot reads
        /// only PlacedPart data and never its build-space GameObject (verified
        /// against BuilderManager.cs 3846-4025), so a detached copy spawns
        /// exactly like the original. That is what lets one arena hold two
        /// independently-uploaded robots.</summary>
        public static bool ParseBuild(BuilderManager bm, string buildText,
                                      out List<BuilderManager.PlacedPart> list,
                                      out Vector3 driveAxis, out string err)
        {
            list = null; driveAxis = Vector3.forward; err = null;
            if (string.IsNullOrEmpty(buildText)) { err = "build text is empty"; return false; }
            int n = bm.LoadSnapshot(buildText);
            if (n <= 0) { err = "build loaded 0 parts"; return false; }
            driveAxis = bm.DriveDir;
            list = new List<BuilderManager.PlacedPart>(bm.placed.Count);
            foreach (var p in bm.placed)
            {
                var c = new BuilderManager.PlacedPart();
                c.def = p.def;
                c.pos = p.pos;
                c.yaw = p.yaw;
                c.wheelAxis = p.wheelAxis;
                c.matName = p.matName;
                c.go = null;               // detached: the build-space object dies on the next load
                list.Add(c);
            }
            return true;
        }

        /// <summary>Seeded, SYMMETRIC opening positions. Both sides get the
        /// same separation and the same facing error budget; only the shared
        /// engagement axis rotates. A seed changes the fight, never the odds.</summary>
        public static void SpawnPoses(int seed, out Vector3 posA, out Quaternion faceA,
                                      out Vector3 posB, out Quaternion faceB,
                                      Vector3 axisA, Vector3 axisB)
        {
            UnityEngine.Random.InitState(seed);
            float yaw = UnityEngine.Random.Range(0f, 360f);
            float sep = UnityEngine.Random.Range(3.6f, 4.4f);
            float jitter = UnityEngine.Random.Range(-8f, 8f);

            Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            posA = -dir * sep;
            posB = dir * sep;
            // Face the opponent, then apply the SAME magnitude of aim error to
            // both sides in opposite senses.
            faceA = Quaternion.Euler(0f, jitter, 0f) * Quaternion.FromToRotation(axisA, dir);
            faceB = Quaternion.Euler(0f, -jitter, 0f) * Quaternion.FromToRotation(axisB, -dir);
        }

        // ------------------------------------------------------------- drive
        IEnumerator Start()
        {
            result = new MatchResult();
            result.matchId = matchId;

            var bm = UnityEngine.Object.FindFirstObjectByType<BuilderManager>();
            if (bm == null) { Fail("no BuilderManager in the scene"); yield break; }

            SnapshotPayload pa, pb; string err;
            if (!RobotSnapshot.Open(envA, out pa, out err)) { Fail("A: " + err); yield break; }
            if (!RobotSnapshot.Open(envB, out pb, out err)) { Fail("B: " + err); yield break; }

            // ---- owner state: everything we are about to stamp on ----------
            string ownerBuild = bm.SnapshotString();
            var savedData = Career.Data;
            bool savedActive = Career.active, savedAuto = Career.autosave, savedAuton = Career.fightAutonomous;
            string savedLeague = Career.activeLeague, savedContest = Career.activeContest;
            int savedRung = Progression.activeRungIndex;
            float savedScale = Time.timeScale;

            Career.Data = new CareerData();
            Career.active = false;
            Career.autosave = false;
            Career.activeLeague = null;
            Career.activeContest = null;
            Career.fightAutonomous = false;
            Progression.activeRungIndex = -1;

            try
            {
                for (int i = 0; i < seeds.Length; i++)
                {
                    var bout = new BoutResult();
                    bout.bout = i; bout.seed = seeds[i];
                    yield return RunBout(bm, pa, pb, bout);
                    result.bouts.Add(bout);
                    if (!string.IsNullOrEmpty(result.error)) break;
                    if (bout.winner == "A") result.aWins++;
                    else if (bout.winner == "B") result.bWins++;
                    else result.draws++;
                    if (stopWhenDecided && (result.aWins * 2 > seeds.Length || result.bWins * 2 > seeds.Length))
                        break;
                }
            }
            finally
            {
                Time.timeScale = savedScale;
                Career.Data = savedData;
                Career.active = savedActive;
                Career.autosave = savedAuto;
                Career.fightAutonomous = savedAuton;
                Career.activeLeague = savedLeague;
                Career.activeContest = savedContest;
                Progression.activeRungIndex = savedRung;
                bm.BackToBuild();
                if (!string.IsNullOrEmpty(ownerBuild)) bm.LoadSnapshot(ownerBuild);
            }

            Decide();
            finished = true;
            var cb = onDone; onDone = null;
            if (cb != null) cb(result);
        }

        void Decide()
        {
            if (result.aWins > result.bWins) { result.verdict = "A"; result.decidedBy = "bouts"; return; }
            if (result.bWins > result.aWins) { result.verdict = "B"; result.decidedBy = "bouts"; return; }
            float a = 0f, b = 0f;
            foreach (var x in result.bouts) { a += x.aDealt; b += x.bDealt; }
            if (a > b + 0.01f) { result.verdict = "A"; result.decidedBy = "damage"; return; }
            if (b > a + 0.01f) { result.verdict = "B"; result.decidedBy = "damage"; return; }
            result.verdict = "Draw"; result.decidedBy = "tied";
        }

        void Fail(string err)
        {
            if (result == null) result = new MatchResult();
            result.error = err;
            result.verdict = "Draw";
            finished = true;
            Debug.LogWarning("[MatchRunner] " + err);
            var cb = onDone; onDone = null;
            if (cb != null) cb(result);
        }

        // -------------------------------------------------------------- bout
        IEnumerator RunBout(BuilderManager bm, SnapshotPayload pa, SnapshotPayload pb, BoutResult bout)
        {
            List<BuilderManager.PlacedPart> listA, listB;
            Vector3 axisA, axisB; string err;

            bm.BackToBuild();
            if (!ParseBuild(bm, pa.build, out listA, out axisA, out err)) { Fail("A: " + err); yield break; }
            if (!ParseBuild(bm, pb.build, out listB, out axisB, out err)) { Fail("B: " + err); yield break; }

            var progA = string.IsNullOrEmpty(pa.program) ? null : RobotProgram.FromJson(pa.program);
            var progB = string.IsNullOrEmpty(pb.program) ? null : RobotProgram.FromJson(pb.program);
            if (!string.IsNullOrEmpty(pa.program) && progA == null) { Fail("A: program did not parse"); yield break; }
            if (!string.IsNullOrEmpty(pb.program) && progB == null) { Fail("B: program did not parse"); yield break; }

            Vector3 posA, posB; Quaternion faceA, faceB;
            SpawnPoses(bout.seed, out posA, out faceA, out posB, out faceB, axisA, axisB);

            bm.EnterMatchArena(arenaHalf);

            RaycastWheelDrive dA, dB;
            var botA = bm.SpawnBot(listA, NameOf(pa, "A"), posA, faceA, axisA, out dA);
            var botB = bm.SpawnBot(listB, NameOf(pb, "B"), posB, faceB, axisB, out dB);
            if (botA == null || botB == null) { Fail("a build failed to spawn"); yield break; }

            RobotVisuals.Install(botA, true);
            RobotVisuals.Install(botB, false);

            // Control routing. SpawnBot has already added the SensorBus, which
            // ProgramRunner.Init picks up — order matters and this is the right
            // side of it.
            var fgo = new GameObject("match_fight_manager");
            var fm = fgo.AddComponent<FightManager>();
            fm.enemyName = NameOf(pb, "B");

            AIController aiB = null;
            fm.playerSource = Arm(botA, dA, progA, null) ? ControlSource.Program : ControlSource.AI;
            if (progB != null) fm.enemySource = Arm(botB, dB, progB, null) ? ControlSource.Program : ControlSource.AI;
            else
            {
                aiB = botB.gameObject.AddComponent<AIController>();
                aiB.self = botB; aiB.drive = dB; aiB.target = botA;
                aiB.forwardLocal = axisB; aiB.power = botB.GetComponent<PowerPlant>();
                fm.enemySource = ControlSource.AI;
            }
            if (progA == null)
            {
                var aiA = botA.gameObject.AddComponent<AIController>();
                aiA.self = botA; aiA.drive = dA; aiA.target = botB;
                aiA.forwardLocal = axisA; aiA.power = botA.GetComponent<PowerPlant>();
                fm.playerSource = ControlSource.AI;
            }

            fm.Setup(bm, botA, dA, botB, dB, aiB);
            if (aiB != null) aiB.fm = fm;

            ReplayRecorder rec = null;
            float simStart = Time.time;

            // Record from the bell, not from Setup: the settle second is not
            // part of the fight and FightManager zeroes damage at the bell.
            float t0 = Time.realtimeSinceStartup;
            Time.timeScale = speed;
            while (fm.state == FightManager.State.Settling &&
                   Time.realtimeSinceStartup - t0 < wallClockCapPerBout)
                yield return null;

            if (record && fm.state != FightManager.State.Ended)
            {
                rec = ReplayRecorder.Attach(gameObject, botA, NameOf(pa, "A"), pa.build,
                                            botB, NameOf(pb, "B"), pb.build,
                                            matchId, bout.bout, bout.seed, arenaHalf);
                simStart = Time.time;
            }

            while (fm.state != FightManager.State.Ended &&
                   Time.realtimeSinceStartup - t0 < wallClockCapPerBout)
                yield return null;

            if (fm.state != FightManager.State.Ended)
                fm.End(FightManager.Outcome.Draw, "match wall timeout");

            bout.outcome = fm.outcome.ToString();
            bout.cause = fm.causeLine ?? "";
            bout.aDealt = fm.player.dealt;
            bout.bDealt = fm.enemy.dealt;
            bout.simSeconds = Time.time - simStart;
            bout.winner = fm.outcome == FightManager.Outcome.PlayerWin ? "A"
                        : fm.outcome == FightManager.Outcome.PlayerLoss ? "B" : "Draw";

            if (rec != null)
            {
                bout.hits = rec.hitCount;
                bout.lastHitT = rec.lastHitT;
                rec.Verdict(bout.winner, bout.cause);
                bout.replayPath = rec.Finish(ReplayRecorder.PathFor(matchId, bout.bout)) ?? "";
                Destroy(rec);
            }

            Time.timeScale = 1f;
            Teardown(bm, fgo);
            yield return null;
        }

        static string NameOf(SnapshotPayload p, string fallback)
        {
            return string.IsNullOrEmpty(p.robotName) ? fallback : p.robotName;
        }

        static bool Arm(CompoundRobot bot, RaycastWheelDrive drive, RobotProgram prog, object _)
        {
            if (prog == null) return false;
            var pr = bot.gameObject.AddComponent<ProgramRunner>();
            pr.Init(bot, drive);
            pr.program = prog;
            return true;
        }

        /// <summary>Everything this bout put in the scene comes back out.
        /// CompoundRobots spawned here are not bm.testRobot/aiRobot, so
        /// BackToBuild does not know about them — and RebuildIslands can have
        /// added more of them mid-fight, which is why this sweeps by type
        /// rather than destroying two remembered references.</summary>
        void Teardown(BuilderManager bm, GameObject fightGo)
        {
            if (fightGo != null) Destroy(fightGo);
            foreach (var cr in UnityEngine.Object.FindObjectsByType<CompoundRobot>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (cr != null && cr.gameObject != null) Destroy(cr.gameObject);
            var fc = Camera.main != null ? Camera.main.GetComponent<FightCamera>() : null;
            if (fc != null) Destroy(fc);
            bm.BackToBuild();
        }
    }
}
