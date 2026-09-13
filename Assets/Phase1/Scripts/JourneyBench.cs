#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using UnityEngine;

namespace RobotBrawl.Phase0
{
    public class JourneyBench : MonoBehaviour
    {
        public static bool finished;
        public static int passed, failed;
        static void Check(bool ok, string message)
        {
            if (ok) passed++; else failed++;
            Debug.Log("[JourneyBench] " + (ok ? "PASS " : "FAIL ") + message);
        }
        public static void Run()
        {
            finished = false; passed = failed = 0;
            new GameObject("JourneyBench").AddComponent<JourneyBench>();
        }
        IEnumerator Start()
        {
            var original = Career.Data;
            bool originalActive = Career.active, originalBoot = BuilderManager.bootToYard;
            var hold = Career.SuspendAutosave();
            BuilderManager bm = null;
            try
            {
                BuilderManager.bootToYard = false;
                bm = Object.FindFirstObjectByType<BuilderManager>();
                if (bm != null) { if (bm.mode == BuilderManager.Mode.Map) bm.LeaveMap(); Object.Destroy(bm.gameObject); }
                yield return null;
                Career.active = true;
                Career.Data = new CareerData { worldSeed = 4242, yardGuideVersion = 1, taskFight = true, taskBolt = true, taskWeld = true, taskBuy = true };
                var d = Career.Data;
                d.stable.Add(new CareerRobot { name = "QA", snapshot = BuilderManager.STARTER_SNAPSHOT, program = "" });
                d.activeRobot = 0;
                Career.TopUpForSnapshot(BuilderManager.STARTER_SNAPSHOT);
                bm = new GameObject("JourneyBuilder").AddComponent<BuilderManager>();
                yield return null; yield return null;
                bm.LoadSnapshot(BuilderManager.STARTER_SNAPSHOT);
                bm.EnterMap(); yield return null; yield return null;
                Check(bm.YardStep == BuilderManager.STEP_CHEST, "fresh career begins at chest");
                Check(!bm.YardWeaponHint().Contains("FIRE") && bm.YardWeaponHint().Contains("ram"), "passive starter gets ram instructions, no nonexistent FIRE");
                bm.EnterShop(); yield return null;
                Check(bm.YardStep == BuilderManager.STEP_CHEST, "visiting shop first does not skip chest or fight");
                bm.EnterMap(); yield return null; yield return null;
                int scrapBefore = d.scrap;
                bm.TeleportPlayer(bm.YardGarageDoor + Vector3.forward * 8f);
                for (int i = 0; i < 8; i++) yield return null;
                Check(d.worldOpened.Count == 1 && d.scrap > scrapBefore, "actual chest pickup persists and pays");
                Check(bm.YardStep == BuilderManager.STEP_MEET, "chest advances to meeting a machine");
                bm.LeaveMap(); yield return null;
                d.quickFights++;
                bm.RecordYardBoutCompleted();
                Check(bm.YardStep == BuilderManager.STEP_UPGRADE, "finished bout + prior shop visit still requires an upgrade");
                Check(bm.GarageGuidance.Contains("FIT AN UPGRADE"), "garage explains the next improvement");
                bm.RecordYardUpgrade();
                Check(bm.YardStep == BuilderManager.STEP_REMATCH, "install asks for a subsequent bout");
                bm.RecordYardBoutCompleted();
                Check(bm.YardStep == BuilderManager.STEP_REMATCH, "same bout cannot complete the upgrade trial");
                d.quickFights++;
                bm.RecordYardBoutCompleted();
                Check(bm.YardStep == BuilderManager.STEP_EXPLORE, "a subsequent bout completes the lesson");

                bm.EnterMap(); yield return null; yield return null;
                Vector3 destination = bm.YardGarageDoor + new Vector3(17f, 0f, 12f);
                bm.TeleportPlayer(destination);
                for (int i = 0; i < 4; i++) yield return null;
                bm.CaptureExpedition(true);
                Check(d.expeditionHasPosition && d.expeditionWorldSeed == d.worldSeed, "safe location is serialized with its world");
                Vector3 saved = new Vector3(d.expeditionX, d.expeditionY, d.expeditionZ);
                bm.testRobot.rb.rotation = Quaternion.Euler(180f, 0f, 0f);
                bm.CaptureExpedition(true);
                Check(new Vector3(d.expeditionX, d.expeditionY, d.expeditionZ) == saved, "flipped location cannot replace safe checkpoint");
                bm.LeaveMap(); yield return null;
                Object.Destroy(bm.gameObject); yield return null;
                Career.Data = JsonUtility.FromJson<CareerData>(JsonUtility.ToJson(d));
                bm = new GameObject("ReloadedJourneyBuilder").AddComponent<BuilderManager>();
                yield return null; yield return null;
                bm.LoadSnapshot(BuilderManager.STARTER_SNAPSHOT);
                bm.EnterMap(); yield return null; yield return null;
                var resumed = bm.testRobot.rb.position;
                Check(Vector2.Distance(new Vector2(saved.x, saved.z), new Vector2(resumed.x, resumed.z)) < 2f, "fresh manager resumes serialized expedition instead of home");
                Check(Career.Data.worldOpened.Count == 1, "collected chest stays collected after resume");
                var owners = Object.FindObjectsByType<WorldMeshOwner>(FindObjectsSortMode.None);
                Mesh tracked = owners.Length > 0 ? owners[0].ownedMesh : null;
                Check(tracked != null && owners.Length > BuilderManager.VIEW_CHUNKS, "generated terrain and scatter have explicit mesh ownership");
                bm.LeaveMap(); yield return null; yield return null;
                Check(tracked == null && Object.FindObjectsByType<WorldMeshOwner>(FindObjectsSortMode.None).Length == 0, "leaving world releases its generated meshes");
            }
            finally
            {
                if (passed + failed < 16)
                    Check(false, "journey must reach every checkpoint before reporting completion");
                if (bm != null) { if (bm.mode == BuilderManager.Mode.Map) bm.LeaveMap(); Object.Destroy(bm.gameObject); }
                Career.Data = original; Career.active = originalActive; BuilderManager.bootToYard = originalBoot;
                hold.Dispose();
                Debug.Log("[JourneyBench] RESULT: " + passed + " pass, " + failed + " fail");
                finished = true;
            }
        }
    }
}
#endif
