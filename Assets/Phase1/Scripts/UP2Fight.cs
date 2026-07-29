
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RobotBrawl.Phase0
{
// UP2 fight pass. Two purposes:
//  1. Fight the machine I BUILT BY POINTER against the whole roster, so the
//     builder claim ends in an actual match rather than a Validate() call.
//  2. A/B the trigger. Round-1 reported that RepeatHarness never touches
//     Phase0Input.debugFire, so every historical sweep on this project was run
//     with the player's weapon switched off. This runs the same build against
//     the same opponent with the trigger held and with it released, and reports
//     the difference in damage dealt.
public class UP2Fight : MonoBehaviour
{
    public string outFile = "qa_up2_fight.txt";
    public string snapFile = "qa_up2_pointerbot.txt";
    BuilderManager bm;
    string path;
    public bool done;

    void W(string s) { File.AppendAllText(path, s + "\n"); }

    IEnumerator Start()
    {
        path = Path.Combine(Path.Combine(Application.dataPath, "Phase1"), outFile);
        File.WriteAllText(path, "# UP2 CRITIC fight pass\n");
        bm = Object.FindFirstObjectByType<BuilderManager>();
        if (bm == null) { W("FATAL"); done = true; yield break; }
        if (bm.mode != BuilderManager.Mode.Build) { bm.BackToBuild(); yield return null; }

        string snapPath = Path.Combine(Path.Combine(Application.dataPath, "Phase1"), snapFile);
        if (!File.Exists(snapPath)) { W("FATAL no " + snapFile); done = true; yield break; }
        string snap = File.ReadAllText(snapPath);
        bm.LoadSnapshot(snap); yield return null;
        W("build = the pointer-built robot; parts=" + bm.placed.Count + " cost=" + bm.BuildCost()
          + " Validate=" + (bm.Validate() ?? "null"));
        var lr = bm.LimbReport();
        W("limb: " + (lr.Count > 0 ? "parts=" + lr[0].parts + " blocked=" + (lr[0].blockedBy ?? "null") : "none"));

        W("\n## ROSTER, trigger HELD (debugFire=true), 1 match each");
        foreach (string id in new[] { "scout", "tipper", "mauler", "ripper", "bulwark", "widowmaker" })
            yield return Match(snap, id, true);

        W("\n## A/B THE TRIGGER vs MAULER - 3 held, 3 released");
        for (int i = 0; i < 3; i++) yield return Match(snap, "mauler", true);
        for (int i = 0; i < 3; i++) yield return Match(snap, "mauler", false);

        W("\n## FIGHT DONE");
        Phase0Input.debugFire = false; Phase0Input.debugThrottle = 0f; Phase0Input.debugSteer = 0f;
        Time.timeScale = 1f;
        if (bm.mode != BuilderManager.Mode.Build) bm.BackToBuild();
        done = true;
    }

    IEnumerator Match(string snap, string enemy, bool fire)
    {
        if (bm.mode != BuilderManager.Mode.Build) { bm.BackToBuild(); yield return null; }
        bm.LoadSnapshot(snap); yield return null;
        bm.opponentId = enemy;
        var e = EnemyRoster.Find(enemy);
        if (e != null) bm.opponentTier = e.tier;
        bm.StartFight();
        yield return null;
        var fm = bm.fight;
        if (fm == null) { W(enemy + ": no FightManager"); yield break; }
        Phase0Input.debugFire = fire;
        Time.timeScale = 3f;
        float wall = 0f;
        float maxLimbHit = 0f;
        while (fm.state != FightManager.State.Ended && wall < 60f)
        {
            // scripted CHARGE policy: drive at the enemy, same as RepeatHarness
            if (fm.enemy.bot != null && bm.testRobot != null && bm.testDrive != null)
            {
                Vector3 me = bm.testRobot.rb.worldCenterOfMass;
                Vector3 tg = fm.enemy.bot.rb.worldCenterOfMass;
                Vector3 to = tg - me; to.y = 0f;
                float ang = Vector3.SignedAngle(bm.testRobot.transform.forward, to.normalized, Vector3.up);
                Phase0Input.debugSteer = Mathf.Clamp(ang / 35f, -1f, 1f);
                Phase0Input.debugThrottle = 1f;
            }
            yield return null;
            wall += Time.unscaledDeltaTime;
        }
        Time.timeScale = 1f;
        Phase0Input.debugFire = false; Phase0Input.debugThrottle = 0f; Phase0Input.debugSteer = 0f;
        W(string.Format("{0,-12} fire={1,-5} {2,-11} {3:F0}s  dealt={4,6:F0} taken={5,6:F0}  parts {6}/{7} vs {8}/{9}  hp {10:F2}/{11:F2}  {12}",
            enemy, fire, fm.outcome.ToString(), fm.elapsed,
            fm.player.dealt, fm.player.taken,
            fm.player.partsNow, fm.player.startParts, fm.enemy.partsNow, fm.enemy.startParts,
            fm.player.hpFrac, fm.enemy.hpFrac, fm.causeLine));
        bm.BackToBuild();
        yield return null;
        yield return null;
    }
}
}
