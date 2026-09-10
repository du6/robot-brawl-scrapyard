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
        static bool OnGround(BuilderManager bm, Vector3 p) { return Mathf.Abs(p.y - bm.TerrainHeight(p.x, p.z)) < 2.5f; }

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

            // ---- 0. THE MAP IS THE FRONT DOOR ----------------------------------
            // A fresh BuilderManager with bootToYard drives out by itself.
            BuilderManager.bootToYard = true;
            Object.Destroy(bm.gameObject); yield return null;
            bm = new GameObject("BuilderManager").AddComponent<BuilderManager>();
            for (int i = 0; i < 6 && bm.mode != BuilderManager.Mode.Map; i++) yield return null;
            Check(bm.mode == BuilderManager.Mode.Map, "a fresh boot lands IN THE YARD, not the workshop (" + bm.mode + ")");
            Check(bm.testRobot != null, "...with the rookie under the stick");
            bm.LeaveMap(); yield return null;
            BuilderManager.bootToYard = false;
            Check(bm.mode == BuilderManager.Mode.Build, "GARAGE is the door back to the workshop");

            // ---- 1. DRIVE OUT ---------------------------------------------------
            bm.EnterMap(); yield return null; yield return null;
            Check(bm.mode == BuilderManager.Mode.Map, "DRIVE OUT enters the yard");
            Check(RBTelemetry.Has(RBTelemetry.MAP), "...and the funnel hears `map`");
            Check(bm.testRobot != null && !bm.testRobot.combatEnabled, "the player's machine is on the map, combat off");
            int loaded = bm.WorldChunksLoaded, want = (2 * BuilderManager.VIEW_CHUNKS + 1) * (2 * BuilderManager.VIEW_CHUNKS + 1);
            Check(loaded == want, "the world around home is loaded: " + loaded + " chunks of " + want);
            Check(bm.WorldSeedNow != 0 && d.worldSeed == bm.WorldSeedNow, "the world's seed was rolled and saved (" + d.worldSeed + ")");
            var crates = bm.YardCratePositions();
            int cratesAtBoot = crates.Count;
            Check(crates.Count >= 2, "crates stand in the loaded world (" + crates.Count + ")");
            bool allOn = true; foreach (var c in crates) if (!OnGround(bm, c)) allOn = false;
            Check(allOn, "...every crate sits on the terrain");
            float d0 = crates.Count > 0 ? Vector3.Distance(new Vector3(crates[0].x, 0f, crates[0].z), bm.YardGarageDoor) : -1f;
            Check(d0 > 7f && d0 < 9f, "the first crate is 8 m from home, in view (" + d0.ToString("0.0") + ")");
            var parked = bm.YardParked;
            Check(parked != null && OnGround(bm, parked.rb.position), "an enemy is parked on the terrain");
            Check(parked != null && parked.name.ToUpper().Contains("SCOUT"), "...the nearest is SCOUT, not another rookie (" + (parked != null ? parked.name : "-") + ")");
            Check(parked != null && Vector3.Distance(parked.rb.position, bm.YardGarageDoor) > 20f, "...far enough from home to be a drive");
            Check(!bm.YardCardShown, "no card at home");
            var hp = bm.YardGarageDoor;
            Check(Mathf.Abs(bm.TerrainHeight(hp.x, hp.z)) < 0.01f && Mathf.Abs(bm.TerrainHeight(hp.x + 10f, hp.z + 10f)) < 0.01f, "home is flat");
            float hA = bm.TerrainHeight(300f, 300f), hB = bm.TerrainHeight(-260f, 410f);
            Check(Mathf.Abs(hA - hB) > 0.05f || Mathf.Abs(hA) > 0.05f, "...and the world is not (" + hA.ToString("0.0") + " m, " + hB.ToString("0.0") + " m)");

            // ---- 2. the seed is the date --------------------------------------
            bm.LeaveMap(); yield return null;
            Check(bm.mode == BuilderManager.Mode.Build, "GARAGE returns to the garage");
            bm.EnterMap(); yield return null; yield return null;
            var crates2 = bm.YardCratePositions();
            bool same = crates2.Count == crates.Count;
            for (int i = 0; same && i < crates.Count; i++) if ((crates[i] - crates2[i]).sqrMagnitude > 0.01f) same = false;
            Check(same && crates2.Count == cratesAtBoot, "leaving and re-entering gives the same world (the seed persists)");

            // ---- 3. a crate opens where it stands ------------------------------
            int scrap0 = d.scrap; int items0 = 0; foreach (var it in d.inventory) items0 += it.count;
            int before = bm.YardCratesLeft;
            bm.TeleportPlayer(new Vector3(crates2[0].x, 0f, crates2[0].z));
            yield return null; yield return null; yield return null;
            Check(bm.YardCratesLeft == before - 1, "driving into a crate opens it (" + bm.YardCratesLeft + " of " + before + " left)");
            int items1 = 0; foreach (var it in d.inventory) items1 += it.count;
            Check(d.scrap > scrap0 && items1 > items0, "...and it paid scrap and a part at once (+" + (d.scrap - scrap0) + " scrap, +" + (items1 - items0) + " part)");
            Check(RBTelemetry.Has(RBTelemetry.CRATE), "...and the funnel hears `crate`");
            Check(d.worldOpened.Count == 1 && d.worldOpened[0].Contains(":"), "...and the save remembers which crate, by chunk (" + d.worldOpened[0] + ")");
            bm.LeaveMap(); yield return null;
            bm.EnterMap(); yield return null; yield return null;
            Check(bm.YardCratesLeft == before - 1, "an opened crate never respawns (" + bm.YardCratesLeft + ")");
            // drive far: chunks stream in ahead and drop behind
            bm.TeleportPlayer(new Vector3(hp.x, 0f, hp.z + 400f));
            for (int i = 0; i < 40; i++) yield return null;
            Check(bm.WorldChunksLoaded == want, "400 m out, the world is still " + want + " chunks around you (" + bm.WorldChunksLoaded + ")");
            Vector3 far = bm.testRobot.rb.position;
            Check(OnGround(bm, far), "...and you are on the ground there (y " + far.y.ToString("0.0") + " vs ground " + bm.TerrainHeight(far.x, far.z).ToString("0.0") + ")");
            bm.TeleportPlayer(new Vector3(hp.x, 0f, hp.z));
            for (int i = 0; i < 40; i++) yield return null;

            // ---- 4. the encounter card ------------------------------------------
            parked = bm.YardParked;
            bm.TeleportPlayer(parked.rb.position + new Vector3(2.5f, 0f, 0f));
            yield return null; yield return null;
            Check(bm.YardCardShown, "the card comes up within " + BuilderManager.CARD_REACH + " m of the parked bot");
            Check(RBTelemetry.Has(RBTelemetry.MEET), "...and the funnel hears `meet`");
            bm.TeleportPlayer(bm.YardGarageDoor + new Vector3(0f, 0f, -6f));
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
            bm.TeleportPlayer(parked.rb.position + new Vector3(2.5f, 0f, 0f));
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
