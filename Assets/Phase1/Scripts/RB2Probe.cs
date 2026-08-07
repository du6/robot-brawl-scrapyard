using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace RobotBrawl.Phase0
{

/// <summary>
/// ROUND-2 CRITIC instrument. One file, one domain reload.
///
/// Everything here is a CONTROLLED comparison: the player build is held
/// constant and exactly one thing varies per case (the opponent recipe, the
/// TIER KNOBS, or the player's own policy). It reports the ENEMY's damage
/// split by source, which round 1 explicitly flagged as the missing column in
/// its tier measurement ("eDealt is not source-split; some of widowmaker's 335
/// is ram"), plus behaviour telemetry (mean range, time in contact band, mean
/// commanded throttle) so "the tier changed the AI" can be tested directly
/// instead of inferred from an outcome.
///
/// cases record format, records separated by a line "@@":
///   label|opponentId|tier|policy|snapshot
/// tier   : Rookie | Veteran | Champion | -   ("-" = the roster's own tier)
/// policy : afk | charge | hitrun
/// snapshot may be empty (= whatever is already loaded); "~" means newline.
/// </summary>
public class RB2Probe : MonoBehaviour
{
    public BuilderManager bm;
    public string cases = "";
    public int reps = 1;
    public float seconds = 24f;
    public bool toKO = false;
    public float speed = 3f;
    public bool fire = true;
    public string outPath = "Assets/Phase1/qa_rb2_out.txt";
    public bool done;
    public string summary = "";

    readonly List<string> rows = new List<string>();
    readonly List<string> lab = new List<string>();
    readonly List<string> opp = new List<string>();
    readonly List<string> tir = new List<string>();
    readonly List<string> pol = new List<string>();
    readonly List<string> snp = new List<string>();

    public void Run() { StartCoroutine(Go()); }

    void Parse()
    {
        string[] recs = cases.Split(new string[] { "\n@@\n" }, System.StringSplitOptions.RemoveEmptyEntries);
        foreach (string r in recs)
        {
            string[] f = r.Split(new char[] { '|' }, 5);   // snapshot itself contains '|'
            if (f.Length < 5) continue;
            lab.Add(f[0].Trim()); opp.Add(f[1].Trim()); tir.Add(f[2].Trim());
            pol.Add(f[3].Trim()); snp.Add(f[4].Trim().Replace("~", "\n"));
        }
    }

    static AiTier TierOf(string s, string oppId)
    {
        if (s == "Rookie") return AiTier.Rookie;
        if (s == "Veteran") return AiTier.Veteran;
        if (s == "Champion") return AiTier.Champion;
        return EnemyRoster.Find(oppId).tier;
    }

    IEnumerator Go()
    {
        if (bm == null) { Finish("no BuilderManager"); yield break; }
        Parse();
        rows.Add("# RB2Probe cases=" + lab.Count + " reps=" + reps + " seconds=" + seconds
                 + " toKO=" + toKO + " speed=" + speed + " fire=" + fire);
        rows.Add(Actuator.ConstantsStamp());
        for (int i = 0; i < lab.Count; i++)
            for (int r = 0; r < reps; r++)
                yield return One(i, r);
        Time.timeScale = 1f;
        Phase0Input.debugFire = false;
        if (bm.mode != BuilderManager.Mode.Build) bm.BackToBuild();
        Finish("done " + lab.Count + " cases x " + reps);
    }

    IEnumerator One(int i, int rep)
    {
        if (bm.mode != BuilderManager.Mode.Build) bm.BackToBuild();
        yield return null;
        if (snp[i].Length > 0)
        {
            bm.LoadSnapshot(snp[i]);
            string err = bm.Validate();
            if (err != null) { rows.Add(lab[i] + " r" + rep + ": INVALID " + err); yield break; }
        }
        bm.opponentId = opp[i];
        bm.opponentTier = TierOf(tir[i], opp[i]);
        bm.StartFight();
        yield return null;
        var fm = bm.fight;
        if (fm == null) { rows.Add(lab[i] + " r" + rep + ": no fight"); yield break; }
        bool fw = Phase0Input.debugFire;
        Phase0Input.debugFire = fire;
        Time.timeScale = speed;

        float t = 0f, distSum = 0f, thrSum = 0f, nearT = 0f, closeT = 0f;
        int nSamp = 0;
        float hitrun = 0f;
        float lim = toKO ? 180f : seconds;
        while (t < lim && fm.state != FightManager.State.Ended)
        {
            if (fm.state == FightManager.State.Fighting)
            {
                Drive(fm, i, ref hitrun);
                var pbb = fm.player.bot; var ebb = fm.enemy.bot;
                if (pbb != null && ebb != null)
                {
                    float d = Vector3.Distance(pbb.rb.worldCenterOfMass, ebb.rb.worldCenterOfMass);
                    distSum += d; nSamp++;
                    if (d < 2.0f) nearT += Time.fixedDeltaTime;
                    if (d < 1.3f) closeT += Time.fixedDeltaTime;
                    if (fm.enemy.drive != null) thrSum += Mathf.Abs(fm.enemy.drive.aiThrottle);
                }
            }
            yield return new WaitForFixedUpdate();
            t += Time.fixedDeltaTime;
        }
        Time.timeScale = 1f;
        Phase0Input.debugFire = fw;

        var pb = fm.player.bot; var eb = fm.enemy.bot;
        string pS = pb == null ? "gone" : string.Format(CultureInfo.InvariantCulture,
            "ram {0:F0}/{1} limb {2:F0}/{3}", pb.dealtRam, pb.hitsRam, pb.dealtLimb, pb.hitsLimb);
        string eS = eb == null ? "gone" : string.Format(CultureInfo.InvariantCulture,
            "ram {0:F0}/{1} limb {2:F0}/{3}", eb.dealtRam, eb.hitsRam, eb.dealtLimb, eb.hitsLimb);
        rows.Add(string.Format(CultureInfo.InvariantCulture,
            "{0,-16} r{1} tier={2,-8} pol={3,-6} t={4:F1} {5,-10} | pDealt {6,5:F0} pTaken {7,5:F0} "
            + "| P[{8}] | E[{9}] | parts {10}/{11} vs {12}/{13} | dist {14:F2} near {15:F1}s close {16:F1}s thr {17:F2}",
            lab[i], rep, bm.opponentTier, pol[i], fm.elapsed,
            fm.state == FightManager.State.Ended ? fm.outcome.ToString() : "TIMEBOX",
            fm.player.dealt, fm.player.taken, pS, eS,
            fm.player.partsNow, fm.player.startParts, fm.enemy.partsNow, fm.enemy.startParts,
            nSamp > 0 ? distSum / nSamp : -1f, nearT, closeT,
            nSamp > 0 ? thrSum / nSamp : -1f));
        bm.BackToBuild();
        yield return null;
    }

    void Drive(FightManager fm, int i, ref float phase)
    {
        var d = fm.player.drive;
        if (d == null || fm.player.bot == null) return;
        fm.player.bot.controlSource = ControlSource.AI;   // P2: direct, the compat property is gone
        if (pol[i] == "afk") { d.aiThrottle = 0f; d.aiSteer = 0f; return; }
        Vector3 self = fm.player.bot.rb.worldCenterOfMass;
        Vector3 tgt = fm.enemy.bot != null ? fm.enemy.bot.rb.worldCenterOfMass : Vector3.zero;
        Vector3 to = tgt - self; to.y = 0f;
        Vector3 fwd = fm.player.bot.transform.TransformDirection(
            d.WheelCount() > 0 ? d.RollOf(0) : Vector3.forward);
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-4f || to.sqrMagnitude < 1e-4f) return;
        float ang = Vector3.SignedAngle(fwd.normalized, to.normalized, Vector3.up);
        float thr = 1f;
        if (pol[i] == "hitrun")
        {
            phase += Time.fixedDeltaTime;
            if (phase > 5f) phase -= 5f;
            if (phase > 3f) { thr = -1f; ang = -ang; }
        }
        if (Mathf.Abs(ang) >= 100f)
        {
            d.aiThrottle = -0.6f * Mathf.Sign(thr == 0f ? 1f : thr);
            d.aiSteer = ang > 0f ? -1f : 1f;
            return;
        }
        d.aiThrottle = thr;
        d.aiSteer = Mathf.Clamp(ang / 45f, -1f, 1f);
    }

    void Finish(string msg)
    {
        summary = msg;
        rows.Add("# " + msg);
        try { File.WriteAllText(outPath, string.Join("\n", rows.ToArray()) + "\n"); }
        catch (System.Exception ex) { Debug.LogWarning("RB2Probe write failed: " + ex.Message); }
        done = true;
        Debug.Log("RB2Probe: " + msg);
    }
}

}
