#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RobotBrawl.Phase0
{
    /// <summary>Prediction: chest contents vary without a fight; distant wins
    /// and chests pay more without any league progress; interrupted writes
    /// recover exact validated data and unrecoverable files stay untouched.
    /// All disk fixtures live in a unique OS temp directory. Career.Data is
    /// swapped only under a counted autosave hold and restored in finally.</summary>
    public static class CareerRewardsBench
    {
        public static int passed, failed;
        public static string report = "";
        static readonly List<string> log = new List<string>();
        static void Check(bool ok, string label)
        {
            if (ok) passed++; else failed++;
            log.Add((ok ? "PASS  " : "FAIL  ") + label);
        }
        static CareerData Fresh()
        {
            return new CareerData { kitGranted = true, kitVersion = Career.KitVersion, taskFight = true, worldSeed = 4242 };
        }

        public static bool RunPure()
        {
            passed = failed = 0; log.Clear();
            var savedData = Career.Data;
            bool savedActive = Career.active, savedAutosave = Career.autosave;
            int savedContext = Career.yardRewardLevel, savedPay = Career.lastQuickPay;
            int savedDirty = Career.uiDirtySeq, savedInventory = Career.inventorySeq;
            string savedLine = Career.lastQuickLine;
            var savedQueue = new List<Career.RewardPop>(Career.rewardQueue);
            string directory = Path.Combine(Path.GetTempPath(), "scrapyard-rewards-" + Guid.NewGuid().ToString("N"));
            var hold = Career.SuspendAutosave();
            try
            {
                Career.Data = Fresh(); Career.active = true;
                Career.ClearYardRewardContext();
                Check(!Career.autosave && Career.AutosaveHolds > 0, "owner autosave is held before any fixture mutation");
                Career.Save(); // Explicit saves must also respect the hold.
                TestLoot();
                TestSettlement();
                TestStorage(directory);
            }
            catch (Exception e) { Check(false, "unexpected exception: " + e); }
            finally
            {
                Career.Data = savedData; Career.active = savedActive;
                Career.yardRewardLevel = savedContext; Career.lastQuickPay = savedPay; Career.lastQuickLine = savedLine;
                Career.uiDirtySeq = savedDirty; Career.inventorySeq = savedInventory;
                Career.rewardQueue.Clear(); Career.rewardQueue.AddRange(savedQueue);
                hold.Dispose();
                // Preserve an explicit caller suspension in addition to counted holds.
                if (!savedAutosave) Career.autosave = false;
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
            report = string.Join("\n", log) + "\nRESULT: " + passed + " pass, " + failed + " fail";
            Debug.Log("[CareerRewardsBench] " + report);
            return failed == 0;
        }

        static void TestLoot()
        {
            string[] lines;
            string first = Career.ChestBoxRoll(4242, "0,0:0", 8f, 0, out lines);
            string second = Career.ChestBoxRoll(4242, "0,0:1", 20f, 1, out lines);
            string third = Career.ChestBoxRoll(4242, "1,0:0", 50f, 2, out lines);
            Check(first.Split(':')[2] == "wedge" && second.Split(':')[2] == "gusset" && third.Split(':')[2] == "plate",
                "the first three chests provide a wedge, weld kit and armor");
            Check(second.Split(':')[3] == "Steel" && third.Split(':')[4] == "2", "guided loot resolves pinned material and gives two plates");
            var initial = Career.ChestBoxRoll(4242, "9,-8:2", 333f, 9, out lines);
            Career.Data.quickFights = 231; Career.Data.quickWins = 111; Career.Data.crowns = 9;
            foreach (var league in CareerDB.Leagues) foreach (var contest in league.contests) Career.Data.doneContests.Add(contest.id);
            Check(Career.ChestBoxRoll(4242, "9,-8:2", 333f, 9, out lines) == initial,
                "chest roll is independent of fight counters, crowns and obsolete leagues");
            var contents = new HashSet<string>();
            var keys = new HashSet<string>();
            bool valid = true;
            for (int i = 0; i < 100; i++)
            {
                var fields = Career.ChestBoxRoll(4242, i + ",-1:0", 70f, i + 3, out lines).Split(':');
                keys.Add(fields[5]); contents.Add(fields[2] + ":" + fields[3]);
                var def = CareerDB.Def(fields[2]);
                valid &= def != null && fields[3] == CareerDB.ResolveMat(fields[2], fields[3]) && int.Parse(fields[4]) > 0;
            }
            Check(contents.Count >= 6 && keys.Count == 100, "100 unfought chests have diverse contents and unique source keys (" + contents.Count + " part types)");
            Check(valid, "all chest drops are usable catalog parts with valid pinned materials");
            Check(Career.YardRewardTier(119f) == 0 && Career.YardRewardTier(120f) == 1 && Career.YardRewardTier(300f) == 2
                && Career.YardRewardTier(500f) == 3 && Career.YardRewardTier(800f) == 4, "salvage tiers follow the map danger bands");
            int previousScrap = -1;
            bool improves = true;
            foreach (float distance in new[] { 8f, 120f, 300f, 500f, 800f })
            {
                var fields = Career.ChestBoxRoll(4242, "5,5:0", distance, 5, out lines).Split(':');
                int scrap = int.Parse(fields[1]);
                improves &= scrap > previousScrap; previousScrap = scrap;
            }
            Check(improves, "the same chest roll earns more scrap in every farther danger tier");

            Career.Data = Fresh();
            string a = "qbox:40:wheel:Rubber:1:chest-A", b = "qbox:40:wheel:Rubber:1:chest-B";
            Career.QueueReward(a, "TREASURE", "", "+40 SCRAP");
            Career.QueueReward(b, "TREASURE", "", "+40 SCRAP");
            Career.GrantPendingRewards();
            Check(Career.Data.scrap == 80 && Career.CountOf("wheel", "Rubber") == 2, "two chests with equal contents both grant");
            Career.GrantReward(a); Career.GrantReward(b);
            Check(Career.Data.scrap == 80 && Career.CountOf("wheel", "Rubber") == 2, "reopening already granted rewards is idempotent");
            string legacy = Career.QuickBoxRoll(1, out lines);
            Check(legacy.Split(':').Length == 5, "legacy five-field Quick reward IDs retain their contract");
            Career.Data.pendingRewards.Add("qbox:40:wheel:Rubber:1");
            Career.GrantPendingRewards();
            Check(Career.Data.scrap == 120 && Career.CountOf("wheel", "Rubber") == 3, "pending rewards from old saves still grant");
            Check(Career.TxnSum() == Career.Data.scrap, "reward ledger reconciles to inventory grants and scrap");
        }

        static void TestSettlement()
        {
            Career.Data = Fresh(); Career.SetYardRewardContext(8f, AiTier.Rookie);
            Career.SettleQuickFight(true, 100f);
            int nearPay = Career.lastQuickPay;
            Career.Data = Fresh(); Career.SetYardRewardContext(800f, AiTier.Champion);
            Career.SettleQuickFight(true, 100f);
            Check(Career.lastQuickPay > nearPay && Career.lastQuickLine.Contains("SALVAGE TIER 5"),
                "farther yard wins pay more before any league has been beaten");
            Career.SettleQuickFight(true, 100f);
            Check(Career.lastQuickPay == nearPay && !Career.lastQuickLine.Contains("SALVAGE TIER"), "yard settlement consumes context without leaking into legacy Quick fights");
            Career.Data = Fresh(); Career.Data.quickWinsToBox = 2;
            Career.SetYardRewardContext(800f, AiTier.Champion);
            Career.SettleQuickFight(true, 100f); Career.GrantPendingRewards();
            Check(Career.Data.scrap - Career.lastQuickPay == 160 && Career.Data.quickBoxesToday == 1,
                "the third yard win earns a region-tier toolbox (160 scrap at tier 5)");
            Career.Data = Fresh(); Career.Data.quickWinsToBox = 2;
            Career.Data.quickBoxDay = DateTime.UtcNow.ToString("yyyy-MM-dd");
            Career.Data.quickBoxesToday = Career.QUICK_BOXES_PER_DAY;
            Career.SetYardRewardContext(800f, AiTier.Champion); Career.SettleQuickFight(true, 100f);
            Check(Career.Data.scrap == Career.lastQuickPay && Career.lastQuickLine.Contains("tomorrow"), "the existing daily fight-box cap still applies to yard rewards");
            Career.SetYardRewardContext(800f, AiTier.Champion); Career.SettleQuickFight(false, 100f);
            Check(Career.lastQuickPay == 5, "distance does not enlarge the repeatable loss consolation");

            // owen, 2026-09-12: "looks like I can keep challenging the same robot
            // and keep getting rewards". The toolboxes were capped per day; the
            // purse was not, so one machine was an unlimited scrap printer.
            Career.Data = Fresh();
            Career.SetYardRewardContext(800f, AiTier.Champion, "y:3:7");
            Career.SettleQuickFight(true, 100f);
            int firstPay = Career.lastQuickPay, afterFirst = Career.Data.scrap;
            int winsToBox = Career.Data.quickWinsToBox, streak = Career.Data.quickStreak;
            Check(firstPay > 0 && Career.Data.yardBeaten.Contains("y:3:7"), "beating a named machine pays, and the machine is marked");
            Career.SetYardRewardContext(800f, AiTier.Champion, "y:3:7");
            Career.SettleQuickFight(true, 100f);
            Check(Career.lastQuickPay == 0 && Career.Data.scrap == afterFirst, "beating the SAME machine again pays nothing");
            Check(Career.Data.quickWinsToBox == winsToBox && Career.Data.quickStreak == streak, "...and does not tick the toolbox meter or the streak");
            Check(Career.lastQuickLine.Contains("already beat this machine"), "...and says why (" + Career.lastQuickLine + ")");
            Check(Career.YardAlreadyBeaten("y:3:7") && !Career.YardAlreadyBeaten("y:4:7"), "the mark names ONE machine, not the yard");
            Career.SetYardRewardContext(800f, AiTier.Champion, "y:4:7");
            Career.SettleQuickFight(true, 100f);
            Check(Career.lastQuickPay > 0, "the machine over the next hill still pays");
            Career.Data.quickBoxDay = "1999-01-01";          // a new day
            Career.SetYardRewardContext(800f, AiTier.Champion, "y:3:7");
            Career.SettleQuickFight(true, 100f);
            Check(Career.lastQuickPay > 0 && Career.Data.yardBeaten.Count == 1, "tomorrow the whole yard is worth fighting again");
            Career.Data = Fresh();
            Career.SetYardRewardContext(8f, AiTier.Rookie, null);
            Career.SettleQuickFight(true, 100f);
            Check(Career.lastQuickPay > 0, "an unnamed opponent (a legacy Quick fight) is unaffected");
        }

        static void TestStorage(string directory)
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "scrapyard_save.json"), error;
            var missing = CareerSaveStore.Read(path);
            Check(missing.data == null && !missing.blocked, "a genuinely new install can start a career");
            var data = Fresh(); data.scrap = 100;
            data.expeditionHasPosition = true; data.expeditionWorldSeed = 4242;
            data.expeditionX = 123f; data.expeditionY = 4f; data.expeditionZ = -234f; data.expeditionYaw = 75f;
            data.worldOpened.Add("0,0:0"); data.pendingRewards.Add("qbox:40:wheel:Rubber:1:chest-A");
            Check(CareerSaveStore.Write(path, data, out error), "initial save commits a validated staged file: " + error);
            var loaded = CareerSaveStore.Read(path);
            Check(loaded.data != null && loaded.data.expeditionX == 123f && loaded.data.expeditionZ == -234f && loaded.data.expeditionYaw == 75f
                && loaded.data.expeditionWorldSeed == 4242 && loaded.data.expeditionHasPosition && loaded.data.worldOpened.Count == 1 && loaded.data.pendingRewards.Count == 1,
                "checkpoint, collected chests and unclaimed rewards survive a save round trip");
            data.scrap = 150;
            Check(CareerSaveStore.Write(path, data, out error) && !File.Exists(path + ".tmp"), "a replacement save commits and removes its staged file");
            CareerData backup;
            Check(CareerSaveStore.TryParse(File.ReadAllText(path + ".bak"), out backup) && backup.scrap == 100, "backup contains the exact previous valid progression");
            File.WriteAllText(path, "{truncated");
            loaded = CareerSaveStore.Read(path);
            Check(!loaded.blocked && loaded.data != null && loaded.data.scrap == 100 && loaded.notice.Contains("Recovered"), "corrupt primary recovers valid backup with an explicit notice");
            Check(File.ReadAllText(path) == "{truncated", "recovery does not silently erase the corrupt original");
            data.scrap = 125;
            Check(CareerSaveStore.Write(path, data, out error) && Directory.GetFiles(directory, "*.corrupt-*").Length == 1,
                "next successful save archives the damaged original");
            Check(CareerSaveStore.TryParse(File.ReadAllText(path + ".bak"), out backup) && backup.scrap == 100, "recovering does not replace a good backup with corrupted bytes");

            File.Move(path, Path.Combine(directory, "previous.json"));
            data.scrap = 200; File.WriteAllText(path + ".tmp", JsonUtility.ToJson(data));
            loaded = CareerSaveStore.Read(path);
            Check(!loaded.blocked && loaded.data != null && loaded.data.scrap == 200, "interruption between portable renames recovers the complete staged save");
            TestRecoveredStage(directory, false);
            TestRecoveredStage(directory, true);

            string blockedPath = Path.Combine(directory, "blocked-backup.json");
            string stagedJson = JsonUtility.ToJson(data);
            File.WriteAllText(blockedPath + ".tmp", stagedJson);
            Directory.CreateDirectory(blockedPath + ".bak"); // deterministic backup failure
            data.scrap = 250;
            Check(!CareerSaveStore.Write(blockedPath, data, out error) && File.ReadAllText(blockedPath + ".tmp") == stagedJson,
                "failed recovery backup leaves the sole valid staged save byte-identical");
            File.WriteAllText(path, "{broken-main"); File.WriteAllText(path + ".tmp", "{broken-stage"); File.WriteAllText(path + ".bak", "{broken-backup");
            loaded = CareerSaveStore.Read(path);
            Check(loaded.blocked && loaded.data == null && loaded.notice.Contains("will not save"), "unrecoverable progress blocks autosaving and explains the temporary session");
            Check(File.ReadAllText(path) == "{broken-main" && File.ReadAllText(path + ".bak") == "{broken-backup", "all unrecoverable originals remain byte-identical");
            CareerData old;
            Check(CareerSaveStore.TryParse("{\"scrap\":0,\"inventory\":[],\"stable\":[],\"worldOpened\":null}", out old)
                && old.worldOpened != null && !old.expeditionHasPosition && old.yardUpgradeAtFight == -1, "old schema migrates additive collections and checkpoint defaults");
            bool repairedBeaten = CareerSaveStore.TryParse("{\"scrap\":0,\"inventory\":[],\"stable\":[],\"yardBeaten\":null,\"taskFight\":true,\"quickBoxDay\":\"1999-01-01\"}", out old);
            Check(repairedBeaten && old.yardBeaten != null, "explicit null beaten-machine history is repaired on load");
            if (repairedBeaten && old.yardBeaten != null)
            {
                Career.Data = old;
                Career.SetYardRewardContext(120f, AiTier.Veteran, "loaded-null-history");
                Career.SettleQuickFight(true, 100f);
                int paid = Career.lastQuickPay, total = old.scrap;
                Career.SetYardRewardContext(120f, AiTier.Veteran, "loaded-null-history");
                Career.SettleQuickFight(true, 100f);
                Check(paid > 0 && old.yardBeaten.Contains("loaded-null-history") && Career.lastQuickPay == 0 && old.scrap == total,
                    "repaired history survives day rollover and retains the existing same-machine reward rule");
            }
            Check(!CareerSaveStore.TryParse("{}", out old) && !CareerSaveStore.TryParse("{\"scrap\":0,\"inventory\":[],\"stable\":[]", out old),
                "empty objects and truncated JSON are rejected instead of accepted as new careers");
            Check(!CareerSaveStore.TryParse("{\"unrelated\":{\"scrap\":0,\"inventory\":[],\"stable\":[]}}", out old),
                "required save fields inside an unrelated nested object cannot masquerade as a career");
            Check(!CareerSaveStore.TryParse("{\"scrap\":0,\"inventory\":\"lost\",\"stable\":[]}", out old),
                "inventory with a wrong JSON type cannot silently become an empty collection");
            string before = File.ReadAllText(path);
            data.scrap = -1;
            Check(!CareerSaveStore.Write(path, data, out error) && File.ReadAllText(path) == before, "invalid new data cannot overwrite existing saves");
            data.scrap = 250;
            Check(!CareerSaveStore.Write(Path.Combine(path, "impossible.json"), data, out error) && !string.IsNullOrEmpty(error),
                "filesystem write errors return a recoverable failure without throwing into gameplay");
            Career.Data = new CareerData(); Career.Data.inventory.Add(new CareerItem { partId = "edgesentinel", mat = "Aluminum", count = 2 });
            Career.MigrateInventory();
            Check(Career.CountOf("wallsensor", "Aluminum") == 2 && Career.CountOf("trapsensor", "Aluminum") == 2
                && Career.Data.kitVersion == Career.KitVersion, "existing inventory migration remains intact under autosave isolation");
        }

        static void TestRecoveredStage(string directory, bool corruptPrimary)
        {
            string path = Path.Combine(directory, corruptPrimary ? "corrupt-primary-stage.json" : "missing-primary-stage.json");
            var prior = Fresh(); prior.scrap = 200;
            File.WriteAllText(path + ".tmp", JsonUtility.ToJson(prior));
            if (corruptPrimary) File.WriteAllText(path, "{broken-primary");
            var next = Fresh(); next.scrap = 250;
            string error;
            string label = corruptPrimary ? "corrupt" : "missing";
            Check(CareerSaveStore.Write(path, next, out error), "saving after staged recovery with " + label + " primary succeeds: " + error);
            File.WriteAllText(path, "{interrupted-next-write");
            var recovered = CareerSaveStore.Read(path);
            Check(!recovered.blocked && recovered.data != null && recovered.data.scrap == 200,
                "the previously sole staged save survives a later failed primary with " + label + " original");
        }
    }
}
#endif
