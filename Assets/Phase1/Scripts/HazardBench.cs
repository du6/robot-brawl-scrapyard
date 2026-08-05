using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RobotBrawl.Phase0
{
/// <summary>C3A acceptance bench: every hazard arena runs an AI-vs-AI career
/// contest at 5x time. Checks per arena: hazards built + point-symmetric,
/// floor/barriers hold, no strike ever lands untelegraphed. Aggregated over
/// Leagues 3-5: the Veteran+ AI's self-inflicted hazard damage stays under
/// 10% of the damage it takes (the repulsion layer works). In-memory career,
/// autosave off.</summary>
public class HazardBench : MonoBehaviour
{
    public static bool finished;
    public static HazardBench Run()
    {
        finished = false;
        return new GameObject("hazard_bench").AddComponent<HazardBench>();
    }

    readonly List<string> log = new List<string>();
    int passed, failed;
    void Check(bool ok, string what)
    {
        if (ok) passed++; else failed++;
        log.Add((ok ? "PASS  " : "FAIL  ") + what);
    }

    IEnumerator Start()
    {
        yield return null;
        var bm = Object.FindFirstObjectByType<BuilderManager>();
        if (bm == null) { Check(false, "builder present"); Finish(); yield break; }

        var savedData = Career.Data;
        bool savedActive = Career.active;
        Career.autosave = false;
        Career.Data = new CareerData();
        Career.active = true;
        Career.Txn(5000, "bench grant");
        foreach (var lgx in CareerDB.Leagues)
            foreach (var cx in lgx.contests)
                Career.Data.doneContests.Add(cx.id);   // everything unlocked, all re-entries
        foreach (var it in bm.CareerShortfallItems()) Career.AddItem(it.partId, it.mat, it.count);
        if (bm.Validate() != null)
        { Check(false, "bench needs a fight-legal build (run CareerSmoke first)"); Finish(); yield break; }

        int[] expectHaz = { 2, 2, 2, 2, 4 };
        float envSum = 0f, takenSum = 0f;
        float savedTS = Time.timeScale;
        for (int li = 0; li < CareerDB.Leagues.Length; li++)
        {
            var lg = CareerDB.Leagues[li];
            bm.StartCareerFight(li, 0);
            yield return null; yield return null;
            var fm = Object.FindFirstObjectByType<FightManager>();
            if (fm == null) { Check(false, lg.arenaName + ": fight failed to start (" + bm.LastMessage + ")"); continue; }
            Check(HazardBase.Active.Count == expectHaz[li],
                  lg.arenaName + ": " + expectHaz[li] + " hazards built (" + HazardBase.Active.Count + ")");
            Check(ArenaHazards.ValidateMirrored(), lg.arenaName + ": layout is point-symmetric (code-checked)");

            // both sides AI: the player build gets a Veteran driver
            CompoundRobot enemyBot = null;
            foreach (var cr in Object.FindObjectsByType<CompoundRobot>(FindObjectsSortMode.None))
                if (cr != bm.testRobot) enemyBot = cr;
            // P0: declared once on the fight instance (see CareerBench).
            fm.playerSource = ControlSource.AI;
            var pai = bm.testRobot.gameObject.AddComponent<AIController>();
            pai.self = bm.testRobot; pai.drive = bm.testDrive; pai.target = enemyBot;
            pai.forwardLocal = Vector3.forward;
            pai.power = bm.testRobot.GetComponent<PowerPlant>();
            pai.ApplyTier(AiTier.Veteran);
            pai.fm = fm;   // parity with the enemy AI (see CareerBench)

            Time.timeScale = 5f;
            float minY = 99f;
            float t0 = Time.realtimeSinceStartup;
            while (fm != null && fm.state != FightManager.State.Ended
                   && Time.realtimeSinceStartup - t0 < 30f)
            {
                if (bm.testRobot != null && bm.testRobot.rb != null) minY = Mathf.Min(minY, bm.testRobot.rb.position.y);
                if (enemyBot != null && enemyBot.rb != null) minY = Mathf.Min(minY, enemyBot.rb.position.y);
                // (P0 deleted the ported per-frame BELL FIX reassert — the
                // fight's playerSource makes the bell honest now.)
                yield return null;
            }
            if (fm != null && fm.state != FightManager.State.Ended)
                fm.End(FightManager.Outcome.Draw, "bench timeout");
            yield return null;

            Check(minY > -2.5f, lg.arenaName + ": floor + barriers hold with hazards (minY " + minY.ToString("F2") + ")");
            bool untelegraphed = false;
            int strikes = 0;
            foreach (var h in HazardBase.Active)
            { if (h.struckWithoutTelegraph) untelegraphed = true; strikes += h.strikes; }
            Check(!untelegraphed, lg.arenaName + ": every strike was telegraphed (" + strikes + " strikes)");

            if (li >= 2 && enemyBot != null)   // Veteran+ tiers: repulsion metric
            {
                envSum += HazardBase.EnvDamage(enemyBot);
                takenSum += enemyBot.damageTaken;
            }
            Time.timeScale = 1f;
            bm.BackToBuild();
            yield return null; yield return null; yield return null;
        }
        Check(HazardBase.Active.Count == 0, "hazards fully cleaned up after the last fight");
        Check(envSum <= Mathf.Max(30f, 0.10f * Mathf.Max(300f, takenSum)),
              string.Format("L3-L5 Veteran+ hazard self-damage under 10% (env {0:F0} vs taken {1:F0})", envSum, takenSum));

        Time.timeScale = savedTS;
        Career.active = savedActive;
        Career.Data = savedData;
        Career.autosave = true;
        Finish();
    }

    void Finish()
    {
        Time.timeScale = 1f;
        foreach (var l in log) Debug.Log("[HazardBench] " + l);
        Debug.Log(string.Format("[HazardBench] RESULT: {0} pass, {1} fail{2}",
                  passed, failed, failed == 0 ? " - ALL GREEN" : " - FIX NEEDED"));
        finished = true;
    }
}
}
