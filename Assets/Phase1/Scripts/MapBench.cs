// ===========================================================================
// MapBench.cs — THE YARD, measured (Robot Brawl: Scrapyard, design §6).
//
// What it proves in one play session, headlessly:
//   1. DRIVE OUT builds the yard: the fence holds every crate and the parked
//      bot, the first crate is 8 m from the door, the parked bot is SCOUT
//      (never the rookie's own build) and far enough away to be a drive;
//   2. the seed is the date: leaving and re-entering gives the same crates;
//   3. a crate opens once, where it stands, and pays (editor: the grant is
//      immediate); it does not respawn on re-entry the same day;
//   4. the encounter card comes up within reach and folds when you leave;
//   5. CHALLENGE enters a Quick bout with the auto-brain driving, settles as
//      a quick fight, and the garage is where you land after the bell;
//   6. BrainPick chooses by the build, and always validates.
//
// OWNER STATE IS SACRED: Career.Data is swapped for a fresh career, autosave
// is held (counted), and everything is restored. Run: BatchSmoke.Map.
// ===========================================================================
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RobotBrawl.Phase0
{
    public class MapBench : MonoBehaviour
    {
        public static bool finished;
        public static int passed, failed;
        static readonly List<string> log = new List<string>();

        public static MapBench Run()
        {
            finished = false; passed = failed = 0; log.Clear();
            return new GameObject("MapBench").AddComponent<MapBench>();
        }
        static void Check(bool ok, string what)
        {
            if (ok) passed++; else failed++;
            log.Add((ok ? "PASS  " : "FAIL  ") + what);
        }
        static bool Inside(Vector3 p) { return Mathf.Abs(p.x) < BuilderManager.YARD_HALF && Mathf.Abs(p.z) < BuilderManager.YARD_HALF; }

        IEnumerator Start()
        {
            var bm = Object.FindFirstObjectByType<BuilderManager>();
            if (bm == null) bm = new GameObject("BuilderManager").AddComponent<BuilderManager>();
            yield return null; yield return null;

            var savedData = Career.Data;
            bool savedActive = Career.active;
            float savedScale = Time.timeScale;
            var hold = Career.SuspendAutosave();
            Career.Data = new CareerData();
            Career.active = true;
            var d = Career.Data;
            d.taskFight = d.taskBolt = d.taskWeld = d.taskBuy = true; d.rescueGranted = true; d.guideDone = true;
            d.stable.Add(new CareerRobot { name = "SCRAPPER", snapshot = BuilderManager.STARTER_SNAPSHOT,
                                           program = "" });   // NO saved program: the auto-brain must drive
            d.activeRobot = 0;
            Career.TopUpForSnapshot(BuilderManager.STARTER_SNAPSHOT);
            bm.LoadSnapshot(BuilderManager.STARTER_SNAPSHOT);
            RBTelemetry.TestReset();
            yield return null;

            // ---- 1. DRIVE OUT ---------------------------------------------------
            bm.EnterMap(); yield return null; yield return null;
            Check(bm.mode == BuilderManager.Mode.Map, "DRIVE OUT enters the yard");
            Check(RBTelemetry.Has(RBTelemetry.MAP), "...and the funnel hears `map`");
            Check(bm.testRobot != null && !bm.testRobot.combatEnabled, "the player's machine is on the map, combat off");
            var crates = bm.YardCratePositions();
            Check(crates.Count == BuilderManager.YARD_CRATES, "three crates stand in the yard (" + crates.Count + ")");
            bool allIn = true; foreach (var c in crates) if (!Inside(c)) allIn = false;
            Check(allIn, "...every crate is inside the fence");
            float d0 = crates.Count > 0 ? Vector3.Distance(new Vector3(crates[0].x, 0f, crates[0].z), bm.YardGarageDoor) : -1f;
            Check(d0 > 7f && d0 < 9f, "the first crate is 8 m from the door, in view (" + d0.ToString("0.0") + ")");
            var parked = bm.YardParked;
            Check(parked != null && Inside(parked.rb.position), "a yard bot is parked inside the fence");
            Check(parked != null && parked.name.ToUpper().Contains("SCOUT"), "...and it is SCOUT, not another rookie (" + (parked != null ? parked.name : "-") + ")");
            Check(parked != null && Vector3.Distance(parked.rb.position, bm.YardGarageDoor) > 20f, "...far enough from the door to be a drive");
            Check(!bm.YardCardShown, "no card at the door");

            // ---- 2. the seed is the date --------------------------------------
            bm.LeaveMap(); yield return null;
            Check(bm.mode == BuilderManager.Mode.Build, "GARAGE returns to the garage");
            bm.EnterMap(); yield return null; yield return null;
            var crates2 = bm.YardCratePositions();
            bool same = crates2.Count == crates.Count;
            for (int i = 0; same && i < crates.Count; i++) if ((crates[i] - crates2[i]).sqrMagnitude > 0.01f) same = false;
            Check(same, "leaving and re-entering gives the same yard (the seed is the date)");

            // ---- 3. a crate opens where it stands ------------------------------
            int scrap0 = d.scrap; int items0 = 0; foreach (var it in d.inventory) items0 += it.count;
            bm.testRobot.rb.position = new Vector3(crates2[0].x, 0.5f, crates2[0].z);
            yield return null; yield return null; yield return null;
            Check(bm.YardCratesLeft == BuilderManager.YARD_CRATES - 1, "driving into a crate opens it (" + bm.YardCratesLeft + " left)");
            int items1 = 0; foreach (var it in d.inventory) items1 += it.count;
            Check(d.scrap > scrap0 && items1 > items0, "...and it paid scrap and a part at once (+" + (d.scrap - scrap0) + " scrap, +" + (items1 - items0) + " part)");
            Check(RBTelemetry.Has(RBTelemetry.CRATE), "...and the funnel hears `crate`");
            Check(d.yardOpened.Contains(0) && d.yardDay.Length > 0, "...and the save remembers which crate, and the day");
            bm.LeaveMap(); yield return null;
            bm.EnterMap(); yield return null; yield return null;
            Check(bm.YardCratesLeft == BuilderManager.YARD_CRATES - 1, "an opened crate does not respawn the same day");

            // ---- 4. the encounter card ------------------------------------------
            parked = bm.YardParked;
            bm.testRobot.rb.position = parked.rb.position + new Vector3(2.5f, 0.5f, 0f);
            yield return null; yield return null;
            Check(bm.YardCardShown, "the card comes up within " + BuilderManager.CARD_REACH + " m of the parked bot");
            Check(RBTelemetry.Has(RBTelemetry.MEET), "...and the funnel hears `meet`");
            bm.testRobot.rb.position = bm.YardGarageDoor + new Vector3(0f, 0.5f, 2f);
            yield return null; yield return null;
            Check(!bm.YardCardShown, "drive away and the card folds - decline is free");

            // ---- 6. the auto-brain, before the fight uses it ----------------------
            Check(BuilderManager.BrainPick(bm.placed).title == "Ram Hunter", "SCRAPPER (compass + wall sensor) gets Ram Hunter");
            var bare = new List<BuilderManager.PlacedPart>();
            foreach (var pp in bm.placed) if (pp.def.id != "compass" && pp.def.id != "wallsensor") bare.Add(pp);
            var bp = BuilderManager.BrainPick(bare);
            var bareIds = new List<string>(); foreach (var pp in bare) bareIds.Add(pp.def.id);
            Check(bp.title == "First Steps" && bp.Validate(bareIds) == null, "no sensors gets First Steps, and it validates (" + bp.title + ")");

            // ---- 5. CHALLENGE ------------------------------------------------------
            bm.testRobot.rb.position = parked.rb.position + new Vector3(2.5f, 0.5f, 0f);
            yield return null; yield return null;
            bm.ChallengeParked();
            yield return null; yield return null;
            var fm = Object.FindFirstObjectByType<FightManager>();
            Check(bm.mode == BuilderManager.Mode.Fight && fm != null, "CHALLENGE enters a fight");
            Check(FightManager.quickBout && Career.quickFight, "...a Quick bout, settled as a quick fight");
            Check(fm != null && fm.playerSource == ControlSource.Program, "...with the auto-brain driving, not the stick");
            Check(bm.opponentId == BuilderManager.YARD_BOT, "...against the parked bot (" + bm.opponentId + ")");
            Check(RBTelemetry.Has(RBTelemetry.CHALLENGE), "...and the funnel hears `challenge`");
            Time.timeScale = 4f;
            float deadline = Time.realtimeSinceStartup + 40f;
            while (fm != null && fm.state != FightManager.State.Ended && Time.realtimeSinceStartup < deadline) yield return null;
            Time.timeScale = savedScale;
            Check(fm != null && fm.state == FightManager.State.Ended, "the bout ended on its own");
            Check(d.quickFights == 1, "...and settled once (quickFights=" + d.quickFights + ")");
            bm.BackToBuild(); yield return null;
            Check(bm.mode == BuilderManager.Mode.Build && Object.FindFirstObjectByType<FightManager>() == null, "after the bell, the garage");
            Check(Career.TxnSum() == d.scrap, "the ledger still audits");

            // ---- restore -------------------------------------------------------------
            Time.timeScale = savedScale;
            Career.Data = savedData;
            Career.active = savedActive;
            hold.Dispose();
            foreach (var l in log) Debug.Log("[MapBench] " + l);
            Debug.Log(string.Format("[MapBench] RESULT: {0} pass, {1} fail{2}", passed, failed, failed == 0 ? " - ALL GREEN" : " - FIX NEEDED"));
            try { System.IO.File.WriteAllText(Application.dataPath + "/Phase1/qa_map_bench.txt", string.Join("\n", log.ToArray()) + "\n"); } catch { }
            finished = true;
        }
    }
}
#endif
