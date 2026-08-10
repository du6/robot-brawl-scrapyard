// ⚠ EDITOR AND DEVELOPMENT BUILDS ONLY — 2026-08-10.
// This is a QA harness. It was compiling into the SHIPPED iOS player: 44 such
// files, ~13k lines, and the string "dev-only-worker-key" was verified present
// in build/ios global-metadata.dat. Dead weight in the binary, and a surface
// that can register accounts and start fights inside a player's app.
// Nothing in the product references any harness — checked against every
// product script, every scene and every prefab before this guard was added.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
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
public class RB2Dev : MonoBehaviour
{
    public BuilderManager bm;
    public string cases = "";
    public int reps = 1;
    public float seconds = 24f;
    public bool toKO = false;
    public float speed = 3f;
    public bool fire = true;
    public bool interleave = true;   // reps OUTER, cases INNER (round-2 critic M7)
    public string outPath = "Assets/Phase1/qa_rb2d_out.txt";
    public bool done;
    public string summary = "";

    readonly List<string> rows = new List<string>();
    readonly List<string> lab = new List<string>();
    readonly List<string> opp = new List<string>();
    readonly List<string> tir = new List<string>();
    readonly List<string> pol = new List<string>();
    readonly List<string> snp = new List<string>();

    public void Run() { StartCoroutine(Go()); }

    // ---- ROUND-2-DEV: per-case AI knob overrides, so the shipped behaviour and
    // the experimental one can be INTERLEAVED inside one batch instead of being
    // run as two blocks (round-2 critic M7). Encoded as a "+token" suffix on the
    // policy field. Restored after every run.
    static readonly string[] StNames = { "charge", "flank", "circle", "commit",
        "desperate", "backoff", "turn", "extricate", "unstick", "engage", "other" };
    float kArc, kFlank, kRear, kParked, kCircle;
    // LEVER PASS (2026-07-27): the ram-damage floor is the one constant that
    // separates 'a box bumped you' from 'a box SLAMMED you'. Saved/restored
    // like every other knob so a ram arm and the shipped arm interleave.
    float kRamMin;
    void KnobsSave()
    {
        kArc = AIController.THREAT_ARC_DOT; kFlank = AIController.FLANK_OFFSET;
        kRear = AIController.REAR_BIAS; kParked = AIController.PARKED_TIME;
        kCircle = AIController.CIRCLE_MAX_S;
        kRamMin = DamageResolver.RAM_MIN_J;
    }
    void KnobsRestore()
    {
        AIController.THREAT_ARC_DOT = kArc; AIController.FLANK_OFFSET = kFlank;
        AIController.REAR_BIAS = kRear; AIController.PARKED_TIME = kParked;
        AIController.CIRCLE_MAX_S = kCircle;
        AIController.avoidOverride = -1f;
        DamageResolver.RAM_MIN_J = kRamMin;
        EnemyRoster.speedTierOverride = -1;
        EnemyRoster.dIntOverride = EnemyRoster.steerOverride =
        EnemyRoster.openThrOverride = EnemyRoster.aggrOverride = -1f;
    }
    /// <summary>noavoid: every aim branch collapses to "aim at the target".
    /// This is the CONTROL for "does the AI weapon-avoidance layer explain the
    /// weapon never landing?" - nothing else about the AI changes.</summary>
    void KnobsApply(string tok)
    {
        KnobsRestore();
        if (tok == "noavoid")
        {
            AIController.FLANK_OFFSET = 0f;
            AIController.REAR_BIAS = 0f;
            AIController.PARKED_TIME = 1e9f;   // never "parked" -> orbit layer off
        }
        else if (tok == "halfavoid")
        {
            AIController.FLANK_OFFSET = kFlank * 0.5f;
            AIController.REAR_BIAS = kRear * 0.5f;
        }
        // fullavoid = the SHIPPED-BEFORE-FIX-1 behaviour at every tier, so a
        // before/after tier ladder can be run INTERLEAVED in one batch.
        else if (tok == "fullavoid") AIController.avoidOverride = 1f;
        // ROUND-3-DEV. flatspeedV/R/C force ONE tier's speed knobs onto every
        // tier, leaving the avoidance knob per-tier. Mirror of "fullavoid".
        else if (tok == "flatspeedR") EnemyRoster.speedTierOverride = (int)AiTier.Rookie;
        else if (tok == "flatspeedV") EnemyRoster.speedTierOverride = (int)AiTier.Veteran;
        else if (tok == "flatspeedC") EnemyRoster.speedTierOverride = (int)AiTier.Champion;
        // ROUND-3-DEV. CHAMPION speed set with exactly ONE knob relaxed to the
        // Veteran value - the isolation arms for M2.
        else if (tok == "cThrV")
        { EnemyRoster.speedTierOverride = (int)AiTier.Champion; EnemyRoster.openThrOverride = 0.55f; }
        else if (tok == "cDintV")
        { EnemyRoster.speedTierOverride = (int)AiTier.Champion; EnemyRoster.dIntOverride = 0.20f; }
        // LEVER PASS. "ram<J>" sets the ram-damage floor for this run only.
        // ram150 = shipped. Raising it deletes light box-on-box bumping
        // without touching weapons, which are on a different code path
        // (Actuator.Bite -> SRC_LIMB) and never see RAM_MIN_J at all.
        else if (tok.StartsWith("ram"))
        {
            float v;
            if (float.TryParse(tok.Substring(3), System.Globalization.NumberStyles.Float,
                               CultureInfo.InvariantCulture, out v))
                DamageResolver.RAM_MIN_J = v;
        }
        else if (tok == "cSteerV")
        { EnemyRoster.speedTierOverride = (int)AiTier.Champion; EnemyRoster.steerOverride = 0.10f; }
    }
    static string BasePol(string p) { int i = p.IndexOf('+'); return i < 0 ? p : p.Substring(0, i); }
    static string TokOf(string p) { int i = p.IndexOf('+'); return i < 0 ? "" : p.Substring(i + 1); }

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
        rows.Add("# RB2Dev cases=" + lab.Count + " reps=" + reps + " interleave=" + interleave + " seconds=" + seconds
                 + " toKO=" + toKO + " speed=" + speed + " fire=" + fire);
        rows.Add(Actuator.ConstantsStamp());
        KnobsSave();
        rows.Add(string.Format(CultureInfo.InvariantCulture,
            "# SHIPPED AI KNOBS  THREAT_ARC_DOT {0:F2} FLANK_OFFSET {1:F2} REAR_BIAS {2:F2}"
            + " PARKED_TIME {3:F2} CIRCLE_MAX_S {4:F2}", kArc, kFlank, kRear, kParked, kCircle));
        rows.Add(string.Format(CultureInfo.InvariantCulture,
            "# SHIPPED DAMAGE  RAM_MIN_J {0:F0} RAM_J_CAP {1:F0} DMG_K {2:F4}",
            DamageResolver.RAM_MIN_J, DamageResolver.RAM_J_CAP, DamageResolver.DMG_K));
        if (interleave)
        {
            for (int r = 0; r < reps; r++)
                for (int i = 0; i < lab.Count; i++)
                    yield return One(i, r);
        }
        else
        {
            for (int i = 0; i < lab.Count; i++)
                for (int r = 0; r < reps; r++)
                    yield return One(i, r);
        }
        Time.timeScale = 1f;
        Phase0Input.debugFire = false;
        // LEVER PASS: KnobsApply() calls KnobsRestore() at the START of each run,
        // so after the LAST run every knob is left on whatever that case set -
        // measured live: a sweep ending on a "+ram400" case left RAM_MIN_J at
        // 400 for the whole editor session. Any measurement taken afterwards
        // silently inherits it. Restore once more on the way out.
        KnobsRestore();
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
        KnobsApply(TokOf(pol[i]));
        bm.StartFight();
        yield return null;
        var fm = bm.fight;
        if (fm == null) { rows.Add(lab[i] + " r" + rep + ": no fight"); yield break; }
        bool fw = Phase0Input.debugFire;
        Phase0Input.debugFire = fire;
        Time.timeScale = speed;
        Actuator.FunnelReset();
        DamageResolver.ImmReset();
        var acts = new List<Actuator>();
        var pbot = fm.player.bot;
        if (pbot != null)
            for (int k = 0; k < pbot.parts.Count; k++)
            {
                var aa = pbot.parts[k].go != null ? pbot.parts[k].go.GetComponent<Actuator>() : null;
                if (aa != null) acts.Add(aa);
            }
        int nA = acts.Count;
        float[] rSum = new float[nA], rMax = new float[nA], tGate = new float[nA];
        int[] nSr = new int[nA];
        var wIdx = new List<int>();
        for (int k = 0; k < nA; k++)
            if (acts[k].limb != null)
                foreach (int m in acts[k].limb) if (!wIdx.Contains(m)) wIdx.Add(m);
        float[] wLost = new float[wIdx.Count];
        for (int k = 0; k < wIdx.Count; k++) wLost[k] = -1f;
        float supMin = 1f, storeMin = 1f;
        int[] stH = new int[StNames.Length];

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
                for (int k = 0; k < nA; k++)
                {
                    var aa = acts[k];
                    if (aa == null) continue;
                    float armM = aa.Rotary ? Mathf.Max(aa.tipRadius, 0.05f) : 1f;
                    float edge = aa.rate * armM;
                    float gateV = Mathf.Min(Actuator.BITE_TIP_SPEED_MS,
                                            Actuator.BITE_RATE_FRAC * aa.MaxRate * armM);
                    rSum[k] += edge; nSr[k]++;
                    if (edge > rMax[k]) rMax[k] = edge;
                    if (edge >= gateV) tGate[k] += Time.fixedDeltaTime;
                }
                if (fm.enemy.ai != null)
                {
                    string st = fm.enemy.ai.state;
                    int si = StNames.Length - 1;
                    for (int k = 0; k < StNames.Length; k++) if (StNames[k] == st) { si = k; break; }
                    stH[si]++;
                }
                var pw0 = fm.player.bot != null ? fm.player.bot.GetComponent<PowerPlant>() : null;
                if (pw0 != null)
                {
                    if (pw0.supplyFrac < supMin) supMin = pw0.supplyFrac;
                    if (pw0.Frac < storeMin) storeMin = pw0.Frac;
                }
                if (fm.player.bot != null)
                    for (int k = 0; k < wIdx.Count; k++)
                        if (wLost[k] < 0f && wIdx[k] < fm.player.bot.parts.Count
                            && fm.player.bot.parts[wIdx[k]].detached)
                            wLost[k] = fm.elapsed;
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
            lab[i] + "/ramJ" + DamageResolver.RAM_MIN_J.ToString("F0"),
            rep, bm.opponentTier, pol[i], fm.elapsed,
            fm.state == FightManager.State.Ended ? fm.outcome.ToString() : "TIMEBOX",
            fm.player.dealt, fm.player.taken, pS, eS,
            fm.player.partsNow, fm.player.startParts, fm.enemy.partsNow, fm.enemy.startParts,
            nSamp > 0 ? distSum / nSamp : -1f, nearT, closeT,
            nSamp > 0 ? thrSum / nSamp : -1f));
        rows.Add(string.Format(CultureInfo.InvariantCulture,
            "    funnel {0} call | rej noRb {1} ownRb {2} notRobot {3} noCombat {4} noIdx {5}"
            + " | onTarget {6} -> gate {7} (bestFrac {8:F2}) energy {9} cool {10} APPLY {11}"
            + " || imm ate limb {12} ram {13} passed limb {14} ram {15}",
            Actuator.fnCall, Actuator.fnNoRb, Actuator.fnOwnRb, Actuator.fnNotRobot,
            Actuator.fnNoCombat, Actuator.fnNoIdx,
            Actuator.fnTarget, Actuator.fnGate, Actuator.fnGateWorstFrac,
            Actuator.fnEnergy, Actuator.fnCool, Actuator.fnApply,
            DamageResolver.immEatLimb, DamageResolver.immEatRam,
            DamageResolver.immPassLimb, DamageResolver.immPassRam));
        var tsb = new StringBuilder("    rotor");
        for (int k = 0; k < nA; k++)
        {
            var aa = acts[k];
            string aid = (aa != null && aa.robot != null && aa.robot.parts != null
                          && aa.partIdx >= 0 && aa.partIdx < aa.robot.parts.Count)
                       ? aa.robot.parts[aa.partIdx].spec.id.Split('_')[0] : "?";
            tsb.Append(string.Format(CultureInfo.InvariantCulture,
                " [{0} kind={1} edgeMean {2:F2} edgeMax {3:F2} aboveGate {4:F1}s tipR {5:F3} maxRate {6:F1}]",
                aid, aa == null ? "-" : aa.kind.ToString(),
                nSr[k] > 0 ? rSum[k] / nSr[k] : -1f, rMax[k], tGate[k],
                aa == null ? -1f : aa.tipRadius, aa == null ? -1f : aa.MaxRate));
        }
        tsb.Append(string.Format(CultureInfo.InvariantCulture,
            " | supplyFracMin {0:F2} storeFracMin {1:F2} | weaponPartLostAt", supMin, storeMin));
        int lostN = 0;
        for (int k = 0; k < wIdx.Count; k++)
            if (wLost[k] >= 0f)
            { lostN++; tsb.Append(string.Format(CultureInfo.InvariantCulture, " {0:F1}s", wLost[k])); }
        if (lostN == 0) tsb.Append(" none");
        rows.Add(tsb.ToString());
        int stTot = 0; foreach (int v in stH) stTot += v;
        var ssb = new StringBuilder("    aiState");
        for (int k = 0; k < StNames.Length; k++)
            if (stH[k] > 0)
                ssb.Append(string.Format(CultureInfo.InvariantCulture, " {0} {1:F0}%",
                    StNames[k], 100f * stH[k] / Mathf.Max(stTot, 1)));
        ssb.Append(" | knob=").Append(TokOf(pol[i]) == "" ? "SHIPPED" : TokOf(pol[i]));
        ssb.Append(" avoid=").Append((fm.enemy.ai != null ? fm.enemy.ai.rtAvoid : -1f).ToString("F2"))
           .Append(" ovr=").Append(AIController.avoidOverride.ToString("F2"))
           .Append(" spdOvr=").Append(EnemyRoster.speedTierOverride)
           .Append(" dInt=").Append(EnemyRoster.DecisionInterval(bm.opponentTier).ToString("F2"))
           .Append(" openThr=").Append(EnemyRoster.OpeningThrottle(bm.opponentTier).ToString("F2"))
           .Append(" steer=").Append(EnemyRoster.SteerAggr(bm.opponentTier).ToString("F2"));
        rows.Add(ssb.ToString());
        KnobsRestore();
        try { File.WriteAllText(outPath, string.Join("\n", rows.ToArray()) + "\n"); }
        catch (System.Exception) { }
        bm.BackToBuild();
        yield return null;
    }

    void Drive(FightManager fm, int i, ref float phase)
    {
        var d = fm.player.drive;
        if (d == null || fm.player.bot == null) return;
        fm.player.bot.controlSource = ControlSource.AI;   // P2: direct, the compat property is gone
        string bp = BasePol(pol[i]);
        if (bp == "afk") { d.aiThrottle = 0f; d.aiSteer = 0f; return; }
        Vector3 self = fm.player.bot.rb.worldCenterOfMass;
        Vector3 tgt = fm.enemy.bot != null ? fm.enemy.bot.rb.worldCenterOfMass : Vector3.zero;
        Vector3 to = tgt - self; to.y = 0f;
        Vector3 fwd = fm.player.bot.transform.TransformDirection(
            d.WheelCount() > 0 ? d.RollOf(0) : Vector3.forward);
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-4f || to.sqrMagnitude < 1e-4f) return;
        float ang = Vector3.SignedAngle(fwd.normalized, to.normalized, Vector3.up);
        float thr = 1f;
        if (bp == "hitrun")
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
        catch (System.Exception ex) { Debug.LogWarning("RB2Dev write failed: " + ex.Message); }
        done = true;
        Debug.Log("RB2Dev: " + msg);
    }
}

}
#endif
