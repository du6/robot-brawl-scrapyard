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
using System.IO;
using System.Text;
using UnityEngine;

namespace RobotBrawl.Phase0
{

/// <summary>
/// DISC CONVERSION VERIFICATION (2026-07-27).
///
/// spinner/spinnerSaw stopped being self-powered and became unpowered rotor
/// edges driven only by a spindle. Three things have to be true for that to be
/// a shipped feature rather than a removed one, and NONE of them is visible
/// from the build screen:
///
///   1. the disc TURNS - measured as accumulated revolutions of the disc part's
///      own transform about the spindle axle, not as "the actuator says it has
///      a rate";
///   2. the seam HOLDS - a disc mates through a single centre socket (its
///      0.10 m thickness is below one socket pitch), which is exactly the x1
///      seam Phase1Parts' own comment warns "would shear off on the first
///      contact"; and
///   3. the machine still HURTS things.
///
/// Everything here is sampled from live state during a real match, and the
/// enemy is sampled too, so a roster bot that lost its weapon in the conversion
/// announces itself instead of quietly becoming a punching bag.
/// </summary>
public class DiscProbe : MonoBehaviour
{
    public BuilderManager bm;
    public string snapshot = "";
    public string label = "BUILD";
    public string opponent = "widowmaker";
    public float seconds = 12f;
    public float speed = 2f;
    public bool fire = true;
    public string outPath = "Assets/Phase1/qa_discprobe.txt";

    public bool done;
    public string summary = "";

    readonly List<string> rows = new List<string>();

    public void Run() { StartCoroutine(Go()); }

    class DiscTrack
    {
        public CompoundRobot robot;
        public string who;
        public int partIdx = -1;
        public Transform tf;
        public Quaternion last;
        public float revs;
        public float peakRate;
        public bool detached;
    }

    IEnumerator Go()
    {
        if (bm == null) { Finish("no BuilderManager"); yield break; }

        if (snapshot.Length > 0)
        {
            int n = bm.LoadSnapshot(snapshot);
            string err = bm.Validate();
            if (err != null) { Finish("build invalid: " + err); yield break; }
            rows.Add("# " + label + " parts=" + n + " cost=" + bm.BuildCost() + " vs " + opponent);
        }
        else rows.Add("# " + label + " (in builder) vs " + opponent);

        bm.opponentId = opponent;
        bm.StartFight();
        yield return null;
        yield return new WaitForSeconds(0.5f);

        var player = bm.testRobot;
        var enemy = bm.aiRobot;
        if (player == null || enemy == null) { Finish("no robots"); yield break; }

        bool fireWas = Phase0Input.debugFire;
        Phase0Input.debugFire = fire;
        float speedWas = Time.timeScale;
        Time.timeScale = speed;

        var tracks = new List<DiscTrack>();
        AddDiscs(tracks, player, "PLAYER");
        AddDiscs(tracks, enemy, "ENEMY");

        var acts = new List<Actuator>();
        acts.AddRange(player.GetComponentsInChildren<Actuator>(true));
        acts.AddRange(enemy.GetComponentsInChildren<Actuator>(true));

        rows.Add("# discs tracked=" + tracks.Count + "  actuators=" + acts.Count
                 + "  fire=" + fire + "  timeScale=" + speed);
        foreach (var a in acts)
            rows.Add(string.Format("#   actuator {0} kind={1} limb={2} I={3:F3} tipR={4:F3} maxRate={5:F1}",
                     a.robot != null ? a.robot.name : "?", a.kind, a.limb != null ? a.limb.Length : 0,
                     a.inertia, a.tipRadius, a.MaxRate));

        rows.Add("t     pRate  eRate  pRevs  eRevs  dDealt dTaken pParts eParts");

        float t = 0f, sample = 0f;
        while (t < seconds && !player.dead && !enemy.dead)
        {
            yield return null;
            float dt = Time.deltaTime;
            t += dt;
            sample += dt;

            foreach (var d in tracks)
            {
                if (d.tf == null || d.partIdx < 0) continue;
                var part = d.robot.parts[d.partIdx];
                if (part.detached) { d.detached = true; continue; }
                Quaternion now = d.tf.localRotation;
                float ang = Quaternion.Angle(d.last, now);
                d.last = now;
                d.revs += ang / 360f;
            }
            foreach (var a in acts)
                foreach (var d in tracks)
                    if (a.robot == d.robot) d.peakRate = Mathf.Max(d.peakRate, Mathf.Abs(a.rate));

            if (sample >= 1f)
            {
                sample = 0f;
                rows.Add(string.Format("{0,4:F1}  {1,5:F1}  {2,5:F1}  {3,5:F1}  {4,5:F1}  {5,6:F0} {6,6:F0} {7,6} {8,6}",
                    t, RateOf(acts, player), RateOf(acts, enemy),
                    RevsOf(tracks, player), RevsOf(tracks, enemy),
                    player.damageDealt, player.damageTaken,
                    LiveParts(player), LiveParts(enemy)));
            }
        }

        rows.Add("# ---- result ----");
        rows.Add(string.Format("# elapsed {0:F1}s  playerDead={1} enemyDead={2}", t, player.dead, enemy.dead));
        foreach (var d in tracks)
            rows.Add(string.Format("# disc {0,-6} revs={1,7:F1}  peakActRate={2,5:F1} rad/s  SHEARED={3}",
                     d.who, d.revs, d.peakRate, d.detached));
        rows.Add(string.Format("# damage dealt={0:F0} taken={1:F0}", player.damageDealt, player.damageTaken));

        // Seam evidence: how hard was the disc's own joint pushed, against what
        // it was allowed to take? This is the x1-centre-socket worry, measured.
        SeamRows(player, "PLAYER");
        SeamRows(enemy, "ENEMY");

        Time.timeScale = speedWas;
        Phase0Input.debugFire = fireWas;

        int pd = 0, ed = 0;
        foreach (var d in tracks) { if (d.who == "PLAYER") pd += Mathf.RoundToInt(d.revs); else ed += Mathf.RoundToInt(d.revs); }
        Finish(string.Format("playerDiscRevs={0} enemyDiscRevs={1} dealt={2:F0} taken={3:F0}",
                             pd, ed, player.damageDealt, player.damageTaken));
    }

    void AddDiscs(List<DiscTrack> into, CompoundRobot r, string who)
    {
        if (r == null || r.parts == null) return;
        for (int i = 0; i < r.parts.Count; i++)
        {
            if (!r.parts[i].spec.id.StartsWith("spinner")) continue;
            var d = new DiscTrack { robot = r, who = who, partIdx = i, tf = r.parts[i].go.transform };
            d.last = d.tf.localRotation;
            into.Add(d);
        }
    }

    float RateOf(List<Actuator> acts, CompoundRobot r)
    {
        float m = 0f;
        foreach (var a in acts) if (a.robot == r) m = Mathf.Max(m, Mathf.Abs(a.rate));
        return m;
    }

    float RevsOf(List<DiscTrack> t, CompoundRobot r)
    {
        float s = 0f;
        foreach (var d in t) if (d.robot == r) s += d.revs;
        return s;
    }

    int LiveParts(CompoundRobot r)
    {
        int n = 0;
        foreach (var p in r.parts) if (!p.detached) n++;
        return n;
    }

    void SeamRows(CompoundRobot r, string who)
    {
        if (r == null || r.edges == null) return;
        foreach (var e in r.edges)
        {
            if (e.label == null) continue;
            if (e.label.IndexOf("spinner") < 0 && e.label.IndexOf("spindle") < 0) continue;
            rows.Add(string.Format("# seam {0,-6} {1,-34} peak={2,7:F0} threshold={3,7:F0} broken={4}",
                     who, e.label, e.peak, e.threshold, e.broken));
        }
    }

    void Finish(string msg)
    {
        summary = msg;
        rows.Add("# " + msg);
        try { File.WriteAllText(outPath, string.Join("\n", rows.ToArray()) + "\n"); }
        catch (System.Exception ex) { Debug.LogWarning("DiscProbe write failed: " + ex.Message); }
        done = true;
        Debug.Log("DiscProbe: " + msg);
    }
}

}
#endif
