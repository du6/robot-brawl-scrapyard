using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace RobotBrawl.Phase0
{

/// <summary>
/// ROUND-1 CRITIC (re-baseline + disc conversion). One file, section switch,
/// because every new .cs costs a domain reload.
///   section "wire" - build the fixture, print what the BUILDER quotes and what
///                    the ARENA actually wired, so a bad fixture is caught
///                    before any damage number is believed.
///   section "bite" - fixed-duration charge match, per-source damage ledger.
///                    Fixed duration (not to-KO) so the comparison is a RATE,
///                    which is far less noisy than a match total.
/// builds format: label|snapshotText, records separated by a line "@@".
/// </summary>
public class RB1Probe : MonoBehaviour
{
    public BuilderManager bm;
    public string section = "wire";
    public string outPath = "Assets/Phase1/qa_rb1_out.txt";
    public string opponent = "mauler";
    public float seconds = 25f;
    public float speed = 3f;
    public bool fire = true;
    public int reps = 1;
    public string builds = "";
    public bool done;
    public string summary = "";

    readonly List<string> rows = new List<string>();
    readonly List<string> labels = new List<string>();
    readonly List<string> snaps = new List<string>();

    public void Run() { StartCoroutine(Go()); }

    void Parse()
    {
        string[] recs = builds.Split(new string[] { "\n@@\n" }, System.StringSplitOptions.RemoveEmptyEntries);
        foreach (string r in recs)
        {
            int p = r.IndexOf('|');
            if (p < 0) continue;
            labels.Add(r.Substring(0, p).Trim());
            snaps.Add(r.Substring(p + 1).Trim().Replace("~", "\n"));
        }
    }

    IEnumerator Go()
    {
        if (bm == null) { Finish("no BuilderManager"); yield break; }
        Parse();
        rows.Add("# section=" + section + " opponent=" + opponent + " seconds=" + seconds
                 + " speed=" + speed + " fire=" + fire + " reps=" + reps + " builds=" + labels.Count);
        rows.Add(Actuator.ConstantsStamp());
        for (int i = 0; i < labels.Count; i++)
        {
            if (section == "wire") yield return WireOne(labels[i], snaps[i]);
            else { for (int r = 0; r < reps; r++) yield return BiteOne(labels[i], snaps[i], r); }
        }
        Time.timeScale = 1f;
        Phase0Input.debugFire = false;
        if (bm.mode != BuilderManager.Mode.Build) bm.BackToBuild();
        Finish("done " + labels.Count + " builds");
    }

    // ------------------------------------------------------------ wire dump
    IEnumerator WireOne(string label, string snap)
    {
        if (bm.mode != BuilderManager.Mode.Build) bm.BackToBuild();
        yield return null;
        int n = bm.LoadSnapshot(snap);
        string err = bm.Validate();
        int cost = bm.BuildCost();
        rows.Add("=== " + label + "  parts=" + n + " cost=" + cost + " validate=" + (err == null ? "OK" : err));
        var limbs = bm.LimbReport();
        int undriven = 0, discs = 0;
        foreach (var li in limbs)
        {
            var sb = new StringBuilder();
            if (li.memberParts != null)
                foreach (var mp in li.memberParts) sb.Append(mp.def.id).Append(" ");
            rows.Add(string.Format(CultureInfo.InvariantCulture,
                "  quote {0,-8} parts={1} I={2:F3} tipR={3:F3} kJ={4:F2} blocked={5} members=[{6}]",
                li.act.def.id, li.parts, li.inertia, li.tipRadius, li.kjPerSwing,
                li.blockedBy == null ? "-" : li.blockedBy, sb.ToString().Trim()));
        }
        foreach (var p in bm.placed)
        {
            if (!p.def.id.StartsWith("spinner")) continue;
            discs++;
            bool driven = false, onAnyLimb = false;
            foreach (var li in limbs)
            {
                if (li.memberParts == null || !li.memberParts.Contains(p)) continue;
                onAnyLimb = true;
                if (li.act.def.id.StartsWith("spindle")) driven = true;
            }
            if (!driven) undriven++;
            rows.Add("  disc " + p.def.id + " spindleDriven=" + driven + " onSomeLimb=" + onAnyLimb);
        }
        rows.Add("  BUILDER-WARN undrivenDiscs=" + undriven + " of " + discs);
        if (err != null) yield break;

        bm.opponentId = opponent;
        bm.StartFight();
        yield return null;
        yield return new WaitForSeconds(0.7f);
        var pr = bm.testRobot;
        if (pr == null) { rows.Add("  ARENA: no robot"); yield break; }
        rows.Add("  arena mass=" + pr.rb.mass.ToString("F1") + " parts=" + pr.parts.Count);
        for (int i = 0; i < pr.parts.Count; i++)
        {
            var a = pr.parts[i].go.GetComponent<Actuator>();
            if (a == null) continue;
            var sb = new StringBuilder();
            if (a.limb != null)
                foreach (int li2 in a.limb) sb.Append(pr.parts[li2].spec.id.Split('_')[0]).Append(" ");
            Vector3 ax = PartVisualFactory.ParseAxis(pr.parts[i].spec.id,
                pr.parts[i].spec.id.StartsWith("spindle") ? "spindle"
                : pr.parts[i].spec.id.StartsWith("ram") ? "ram" : "pivot", Vector3.right);
            rows.Add(string.Format(CultureInfo.InvariantCulture,
                "  arena {0,-8} kind={1} axis=({2:F2},{3:F2},{4:F2}) limbN={5} I={6:F3} tipR={7:F3} maxRate={8:F1} members=[{9}]",
                pr.parts[i].spec.id.Split('_')[0], a.kind, ax.x, ax.y, ax.z,
                a.limb == null ? 0 : a.limb.Length, a.inertia, a.tipRadius, a.MaxRate, sb.ToString().Trim()));
        }
        // spin-quality: does the disc turn in its OWN plane, or wobble?
        Phase0Input.debugFire = true;
        float tw = Time.timeScale; Time.timeScale = 2f;
        var idx = new List<int>();
        for (int i = 0; i < pr.parts.Count; i++)
            if (pr.parts[i].spec.id.StartsWith("spinner")) idx.Add(i);
        var last = new List<Quaternion>();
        var revs = new List<float>();
        var alig = new List<float>();
        var cnt = new List<float>();
        foreach (int i in idx) { last.Add(pr.parts[i].go.transform.localRotation); revs.Add(0f); alig.Add(0f); cnt.Add(0f); }
        float t = 0f;
        while (t < 6f)
        {
            yield return null;
            t += Time.deltaTime;
            for (int k = 0; k < idx.Count; k++)
            {
                var tf = pr.parts[idx[k]].go.transform;
                Quaternion now = tf.localRotation;
                Quaternion dq = now * Quaternion.Inverse(last[k]);
                float da; Vector3 dax;
                dq.ToAngleAxis(out da, out dax);
                if (da > 180f) { da = 360f - da; dax = -dax; }
                if (da > 0.5f)
                {
                    Vector3 own = now * Vector3.up;   // disc face normal, in parent frame
                    alig[k] += Mathf.Abs(Vector3.Dot(dax.normalized, own.normalized)) * da;
                    cnt[k] += da;
                    revs[k] += da / 360f;
                }
                last[k] = now;
            }
        }
        Time.timeScale = tw;
        Phase0Input.debugFire = false;
        for (int k = 0; k < idx.Count; k++)
            rows.Add(string.Format(CultureInfo.InvariantCulture,
                "  spin disc[{0}] revs6s={1:F1} axisAlignToOwnFace={2:F3} (1.0=clean spin, 0=tumble)",
                pr.parts[idx[k]].spec.id.Split('_')[0], revs[k], cnt[k] > 0.01f ? alig[k] / cnt[k] : -1f));
        bm.BackToBuild();
        yield return null;
    }

    // ------------------------------------------------------------ bite ledger
    IEnumerator BiteOne(string label, string snap, int rep)
    {
        if (bm.mode != BuilderManager.Mode.Build) bm.BackToBuild();
        yield return null;
        if (snap.Length > 0)
        {
            bm.LoadSnapshot(snap);
            string err = bm.Validate();
            if (err != null) { rows.Add(label + " r" + rep + ": INVALID " + err); yield break; }
        }
        bm.opponentId = opponent;
        bm.StartFight();
        yield return null;
        var fm = bm.fight;
        if (fm == null) { rows.Add(label + " r" + rep + ": no fight"); yield break; }
        bool fw = Phase0Input.debugFire;
        Phase0Input.debugFire = fire;
        Actuator.FunnelReset();
        DamageResolver.ImmReset();
        Time.timeScale = speed;
        float t = 0f;
        while (t < seconds && fm.state != FightManager.State.Ended)
        {
            if (fm.state == FightManager.State.Fighting) Drive(fm);
            yield return new WaitForFixedUpdate();
            t += Time.fixedDeltaTime;
        }
        Time.timeScale = 1f;
        Phase0Input.debugFire = fw;
        var pb = fm.player.bot;
        var eb = fm.enemy.bot;
        if (pb == null) { rows.Add(label + " r" + rep + ": player gone"); yield break; }
        float perRam = pb.hitsRam > 0 ? pb.dealtRam / pb.hitsRam : 0f;
        float perLimb = pb.hitsLimb > 0 ? pb.dealtLimb / pb.hitsLimb : 0f;
        rows.Add(string.Format(CultureInfo.InvariantCulture,
            "{0,-12} r{1} t={2:F0}s ended={3} | dealt {4:F0} taken {5:F0} | ram {6:F0}/{7} ({8:F1}/bite) "
            + "limb {9:F0}/{10} ({11:F1}/bite max {12:F0}) | pParts {13}/{14} eParts {15}/{16} | {17}",
            label, rep, fm.elapsed, fm.state == FightManager.State.Ended ? fm.outcome.ToString() : "TIMEBOX",
            fm.player.dealt, fm.player.taken, pb.dealtRam, pb.hitsRam, perRam,
            pb.dealtLimb, pb.hitsLimb, perLimb, pb.maxLimbHit,
            fm.player.partsNow, fm.player.startParts, fm.enemy.partsNow, fm.enemy.startParts,
            eb == null ? "enemyGone" : ("eDealt " + eb.damageDealt.ToString("F0"))));
        rows.Add(string.Format(CultureInfo.InvariantCulture,
            "    funnel {0} call | rejected: noRb {1} ownRb {2} notRobot {3} noCombat {4} noIdx {5}"
            + " | onTarget {6} -> gate {7} (bestFrac {8:F2}) energy {9} cool {10} APPLY {11}"
            + " || immunity ate limb {12} ram {13}, passed limb {14} ram {15} | noIdxWho {16}",
            Actuator.fnCall, Actuator.fnNoRb, Actuator.fnOwnRb, Actuator.fnNotRobot,
            Actuator.fnNoCombat, Actuator.fnNoIdx,
            Actuator.fnTarget, Actuator.fnGate, Actuator.fnGateWorstFrac,
            Actuator.fnEnergy, Actuator.fnCool, Actuator.fnApply,
            DamageResolver.immEatLimb, DamageResolver.immEatRam,
            DamageResolver.immPassLimb, DamageResolver.immPassRam, Actuator.fnNoIdxWho));
        bm.BackToBuild();
        yield return null;
    }

    void Drive(FightManager fm)
    {
        var d = fm.player.drive;
        if (d == null || fm.player.bot == null) return;
        d.useAI = true;
        Vector3 self = fm.player.bot.rb.worldCenterOfMass;
        Vector3 tgt = fm.enemy.bot != null ? fm.enemy.bot.rb.worldCenterOfMass : Vector3.zero;
        Vector3 to = tgt - self; to.y = 0f;
        Vector3 fwd = fm.player.bot.transform.TransformDirection(
            d.WheelCount() > 0 ? d.RollOf(0) : Vector3.forward);
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-4f || to.sqrMagnitude < 1e-4f) return;
        float ang = Vector3.SignedAngle(fwd.normalized, to.normalized, Vector3.up);
        if (Mathf.Abs(ang) >= 100f)
        {
            d.aiThrottle = -0.6f;
            d.aiSteer = ang > 0f ? -1f : 1f;
            return;
        }
        d.aiThrottle = 1f;
        d.aiSteer = Mathf.Clamp(ang / 45f, -1f, 1f);
    }

    void Finish(string msg)
    {
        summary = msg;
        rows.Add("# " + msg);
        try { File.WriteAllText(outPath, string.Join("\n", rows.ToArray()) + "\n"); }
        catch (System.Exception ex) { Debug.LogWarning("RB1Probe write failed: " + ex.Message); }
        done = true;
        Debug.Log("RB1Probe: " + msg);
    }
}

}
