// ===========================================================================
// QuickFightBench.cs — the Quick Fight loop, measured (plan step 1-2,
// docs/CATS_Gap_Analysis_Plan_2026-09-07.md).
//
// What it proves, in one play session:
//   1. a quick bout ENDS within its 30-s clock (+ a settle margin) - the
//      crusher walls spawned and the fight did not drift to the judges;
//   2. the settle counts: quickFights, streak, the box meter, a toolbox on
//      the third win, a crown on the fifth, the daily cap, and the day reset;
//   3. the box's grant is exact and self-describing (qbox:<scrap>:<part>:<mat>:<n>).
//
// OWNER STATE IS SACRED: Career.Data is swapped for a fresh career, autosave
// is held for the whole run (counted hold), and the original is restored.
// Run: BatchSmoke.Quick (headless) or QuickFightBench.Run() in play mode.
// ===========================================================================
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RobotBrawl.Phase0
{
    public class QuickFightBench : MonoBehaviour
    {
        public static bool finished;
        public static int passed, failed;
        static readonly List<string> log = new List<string>();

        public static QuickFightBench Run()
        {
            finished = false; passed = failed = 0; log.Clear();
            var go = new GameObject("QuickFightBench");
            return go.AddComponent<QuickFightBench>();
        }

        static void Check(bool ok, string what)
        {
            if (ok) passed++; else failed++;
            log.Add((ok ? "PASS  " : "FAIL  ") + what);
        }

        IEnumerator Start()
        {
            var bm = Object.FindFirstObjectByType<BuilderManager>();
            if (bm == null) bm = new GameObject("BuilderManager").AddComponent<BuilderManager>();
            yield return null; yield return null;

            var savedData = Career.Data;
            bool savedActive = Career.active;
            var hold = Career.SuspendAutosave();
            Career.Data = new CareerData();
            Career.active = true;
            var d = Career.Data;
            d.taskFight = d.taskBolt = d.taskWeld = d.taskBuy = true; d.rescueGranted = true; d.guideDone = true;
            // a fightable machine the career owns: the starter, topped up
            d.stable.Add(new CareerRobot { name = "SCRAPPER", snapshot = BuilderManager.STARTER_SNAPSHOT,
                                           program = RobotProgram.RamHunter().ToJson() });
            d.activeRobot = 0;
            Career.TopUpForSnapshot(BuilderManager.STARTER_SNAPSHOT);
            bm.LoadSnapshot(BuilderManager.STARTER_SNAPSHOT);
            yield return null;

            // ---- 1. a bout that ends on its own clock -------------------------
            var pool = Career.QuickPool();
            Check(pool.Count == 3, "the pool offers three opponents (" + pool.Count + ")");
            Check(pool[0].oppId != pool[1].oppId && pool[1].oppId != pool[2].oppId, "...all different");
            float t0 = Time.time;
            bm.StartQuickFight(0);
            var fm = Object.FindFirstObjectByType<FightManager>();
            Check(fm != null && bm.mode == BuilderManager.Mode.Fight, "StartQuickFight enters a fight");
            Check(FightManager.quickBout && Career.quickFight, "...flagged as a quick bout");
            Check(fm != null && fm.timer <= FightManager.QUICK_MATCH_TIME + 0.01f, "...on the 30-s clock (" + (fm != null ? fm.timer.ToString("0.0") : "-") + ")");
            bool crushSeen = false;
            float deadline = Time.time + FightManager.QUICK_MATCH_TIME + 15f;
            while (fm != null && fm.state != FightManager.State.Ended && Time.time < deadline)
            {
                if (fm.CrushStarted) crushSeen = true;
                yield return null;
            }
            float took = Time.time - t0;
            Check(fm != null && fm.state == FightManager.State.Ended, "the bout ended (" + took.ToString("0.0") + " s)");
            Check(took <= FightManager.QUICK_MATCH_TIME + 8f, "...inside the clock plus a settle margin");
            Check(crushSeen || took < FightManager.QUICK_MATCH_TIME - FightManager.QUICK_CRUSH_AT,
                  crushSeen ? "the walls closed in for the last 10 s" : "it ended before the walls were due");
            Check(d.quickFights == 1 && d.fights == 1, "settled once: quickFights=" + d.quickFights + " fights=" + d.fights);
            Check(!Career.quickFight && !FightManager.quickBout, "the quick flags clear at the bell");
            bm.BackToBuild(); yield return null;

            // ---- 2. the meter, without fighting 20 more bouts -------------------
            Career.Data = new CareerData();
            d = Career.Data;
            d.taskFight = d.taskBolt = d.taskWeld = d.taskBuy = true; d.rescueGranted = true; d.guideDone = true;
            int scrap0 = d.scrap;
            Career.SettleQuickFight(true, 100f);
            Check(d.quickWins == 1 && d.quickStreak == 1 && d.quickWinsToBox == 1, "win 1: streak 1, meter 1/3");
            Check(d.scrap > scrap0, "a win pays (" + (d.scrap - scrap0) + ")");
            Career.SettleQuickFight(true, 100f);
            Check(d.pendingRewards.Count == 0, "no box before the third win");
            // In the editor a queued reward is GRANTED at once (no box), so the
            // third win must land the toolbox's scrap on top of the win's pay.
            int scrapBefore3 = d.scrap; int items3 = 0; foreach (var it in d.inventory) items3 += it.count;
            Career.SettleQuickFight(true, 100f);
            int items4 = 0; foreach (var it in d.inventory) items4 += it.count;
            Check(d.scrap - scrapBefore3 > Career.lastQuickPay && items4 > items3,
                  "win 3: the toolbox paid scrap and a part on top of the win (+" + (d.scrap - scrapBefore3) + " scrap, +" + (items4 - items3) + " part)");
            Check(d.quickBoxesToday == 1 && d.quickWinsToBox == 0, "...meter reset, 1 box today");
            // The id is self-describing: roll one, queue it by hand, grant it.
            string[] lines;
            string boxId = Career.QuickBoxRoll(1, out lines);
            var f = boxId.Split(':');
            int bScrap = 0, bCount = 0;
            bool wellFormed = f.Length == 5 && int.TryParse(f[1], out bScrap) && int.TryParse(f[4], out bCount);
            Check(wellFormed && bCount == 2, "the box id is self-describing and a crown adds a part (" + boxId + ")");
            d.pendingRewards.Add(boxId);
            int before = d.scrap; int had = wellFormed ? Career.CountOf(f[2], f[3]) : 0;
            Career.GrantReward(boxId);
            Check(wellFormed && d.scrap == before + bScrap && Career.CountOf(f[2], f[3]) == had + bCount,
                  "opening it grants exactly what it says");
            Career.GrantReward(boxId);
            Check(d.scrap == before + bScrap && d.pendingRewards.Count == 0, "...and it is granted once");
            Career.SettleQuickFight(false, 0f);
            Check(d.quickStreak == 0 && d.quickWinsToBox == 0, "a loss resets the streak, not the box meter");
            for (int i = 0; i < 5; i++) Career.SettleQuickFight(true, 100f);
            Check(d.crowns >= 1 || d.pendingRewards.Exists(x => x.Contains("CROWN") || x.StartsWith("qbox:")), "five in a row: a crown was earned or spent on a box");
            // the daily cap
            d.pendingRewards.Clear(); d.quickWinsToBox = 0; d.crowns = 0;
            d.quickBoxesToday = Career.QUICK_BOXES_PER_DAY;
            for (int i = 0; i < 3; i++) Career.SettleQuickFight(true, 100f);
            Check(d.pendingRewards.Count == 0, "at the daily cap the third win queues no box");
            Check(Career.lastQuickLine.Contains("tomorrow"), "...and the card says why");
            d.quickBoxDay = "2000-01-01";
            for (int i = 0; i < 3; i++) Career.SettleQuickFight(true, 100f);
            Check(d.quickBoxesToday == 1 && Career.lastQuickLine.Contains("TOOLBOX"), "a new day resets the cap");
            Check(Career.TxnSum() == d.scrap, "the ledger still audits (sum of txns == scrap)");

            // ---- restore ---------------------------------------------------------
            Career.Data = savedData;
            Career.active = savedActive;
            hold.Dispose();
            foreach (var l in log) Debug.Log("[QuickFightBench] " + l);
            Debug.Log(string.Format("[QuickFightBench] RESULT: {0} pass, {1} fail{2}", passed, failed,
                      failed == 0 ? " - ALL GREEN" : " - FIX NEEDED"));
            finished = true;
        }
    }
}
#endif
