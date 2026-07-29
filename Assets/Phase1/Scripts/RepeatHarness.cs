using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace RobotBrawl.Phase0
{

/// <summary>
/// Round-5 fix 1 — the repeat harness, committed as code (critic finding 1:
/// "commit that harness; the dev ran it as a throwaway probe last round and it
/// is the only instrument that can tell you whether the game is converging").
///
/// WHY THIS EXISTS. Every other measurement in this project reports what
/// happened in ONE match. That cannot distinguish "the player's input decided
/// it" from "the physics rolled dice", and the whole question of whether the
/// game is a game hangs on that difference. This runs the SAME snapshot under
/// the SAME scripted policy N times and reports the two numbers that answer it:
///   · outcome flips  — how many of the N disagree with the majority verdict,
///   · time spread    — longest match / shortest match.
/// The bar the critic set: flips &lt;= 1 and spread &lt; 1.6x at n &gt;= 8.
///
/// It deliberately does NOT seed Random. Seeding would make the repeats
/// byte-identical and the instrument would always read zero; the variance under
/// test is exactly the natural variance a player is subjected to.
///
/// It also records the number that finding 2 is about: MAX PIECES LOST BY ONE
/// BODY IN ONE PHYSICS STEP, sampled every FixedUpdate.
///
/// Usage (from a Unity_RunCommand or an editor hook):
///     var h = new GameObject("RepeatHarness").AddComponent&lt;RepeatHarness&gt;();
///     h.bm = builderManager; h.snapshot = text; h.policy = "charge";
///     h.runs = 8; h.outPath = "Assets/Phase1/qa_repeat.txt"; h.Run();
/// Poll h.done, then read h.summary or the file.
/// </summary>
public class RepeatHarness : MonoBehaviour
{
    public BuilderManager bm;
    /// <summary>Snapshot text of the build under test. Empty = whatever is
    /// already in the builder.</summary>
    public string snapshot = "";
    public string label = "BUILD";
    /// <summary>"afk" | "charge" | "hitrun" | "wall".</summary>
    public string policy = "charge";
    /// <summary>Hold the weapon trigger for the scripted player.
    ///
    /// DEFAULT TRUE, AND THIS IS A CORRECTION, NOT A FEATURE. Until now this
    /// harness never touched Phase0Input.debugFire, so the scripted "player" it
    /// stands in for drove around with its weapon switched OFF - in every sweep
    /// this project has ever run. Its own header printed `debugFire False` and
    /// its own damage cards printed `limb 0/0` the whole time, and nobody read
    /// them. Three separate agents found this and each reasonably declined it as
    /// harness plumbing rather than a game bug.
    ///
    /// The consequence is not small: every conclusion drawn about Phase 4
    /// weapons from a RepeatHarness sweep - including "the legacy pre-made disc
    /// beats the whole base-component kit", which was the most damning result of
    /// the previous five-round loop - was measured on machines whose actuators
    /// never once fired. Those conclusions are not wrong so much as untested.
    ///
    /// A scripted stand-in for a human must use the verbs a human has. Set this
    /// false ONLY to measure the no-weapon baseline deliberately, and say so.</summary>
    public bool fire = true;
    public int runs = 8;
    bool fireWasOn;
    public string outPath = "Assets/Phase1/qa_repeat.txt";
    /// <summary>Fixed timestep is untouched, so physics is identical.</summary>
    public float speed = 3f;
    /// <summary>Wall-clock ceiling per match, so a wedged bot cannot hang the
    /// whole sweep.</summary>
    public float maxMatchSeconds = 120f;

    public bool done;
    public string summary = "";

    readonly List<string> rows = new List<string>();
    readonly List<float> lengths = new List<float>();
    readonly List<int> results = new List<int>();   // +1 win, -1 loss, 0 draw

    // per-step structural attribution (finding 2)
    int lastP = -1, lastE = -1;
    int worstStepP, worstStepE;

    public void Run() { StartCoroutine(Sweep()); }

    IEnumerator Sweep()
    {
        float t0 = Time.realtimeSinceStartup;
        if (bm == null) { Finish("no BuilderManager"); yield break; }

        if (snapshot.Length > 0)
        {
            int n = bm.LoadSnapshot(snapshot);
            string err = bm.Validate();
            if (err != null) { Finish("build invalid: " + err); yield break; }
            rows.Add("# build " + label + " parts=" + n + " policy=" + policy + " runs=" + runs);
        }
        else rows.Add("# build " + label + " (in builder) policy=" + policy + " runs=" + runs);

        // ROUND-2-CRITIC FIX (CRITICAL 1: "the prior session's whole Phase-4
        // dataset was measured against a stale actuator; its material numbers
        // describe code that is not running"). Every sweep file now carries the
        // constants that produced it, so a stale-regime dataset announces
        // itself in its own header instead of having to be caught later by
        // arithmetic on somebody else's table.
        rows.Add(Actuator.ConstantsStamp());
        // Arm the trigger BEFORE the header is written, so the header records
        // what the sweep actually ran with rather than what it inherited.
        fireWasOn = Phase0Input.debugFire;
        Phase0Input.debugFire = fire;
        rows.Add("# fixedDeltaTime " + Time.fixedDeltaTime.ToString("F4")
                 + "  timeScale " + speed.ToString("F1")
                 + "  debugFire " + Phase0Input.debugFire
                 + (fire ? "  (trigger HELD for the scripted player)"
                         : "  (trigger OFF - deliberate no-weapon baseline)"));

        Time.timeScale = speed;
        for (int r = 0; r < runs; r++)
        {
            yield return RunOne(r);
        }
        Time.timeScale = 1f;
        Phase0Input.debugFire = fireWasOn;    // leave the editor as we found it
        if (bm.mode != BuilderManager.Mode.Build) bm.BackToBuild();

        // ---- the two numbers the harness exists to produce ----
        int win = 0, loss = 0, draw = 0;
        foreach (int v in results) { if (v > 0) win++; else if (v < 0) loss++; else draw++; }
        int major = Mathf.Max(win, Mathf.Max(loss, draw));
        int flips = results.Count - major;
        float lo = float.MaxValue, hi = 0f;
        foreach (float L in lengths) { if (L < lo) lo = L; if (L > hi) hi = L; }
        float spread = (lo > 0.01f && lengths.Count > 0) ? hi / lo : 0f;

        summary = string.Format(CultureInfo.InvariantCulture,
            "{0}|{1}  n={2}  W{3}/L{4}/D{5}  flips={6}  len {7:F1}-{8:F1}s spread={9:F2}x  "
            + "worst single-step piece loss: player {10} enemy {11}  [{12}] ({13:F0}s wall)",
            label, policy, results.Count, win, loss, draw, flips, lo, hi, spread,
            worstStepP, worstStepE,
            (flips <= 1 && spread < 1.6f) ? "CONVERGED" : "NOISY",
            Time.realtimeSinceStartup - t0);
        rows.Add(summary);
        Finish(null);
    }

    IEnumerator RunOne(int r)
    {
        if (bm.mode != BuilderManager.Mode.Build) bm.BackToBuild();
        yield return null;
        bm.StartFight();
        yield return null;
        var fm = bm.fight;
        if (fm == null) { rows.Add("run " + r + ": no FightManager"); yield break; }

        lastP = lastE = -1;
        if (CompoundRobot.detachLogOn) CompoundRobot.detachLog.Clear();
        float wall = Time.realtimeSinceStartup;
        float hitrunPhase = 0f;

        while (fm.state != FightManager.State.Ended)
        {
            SampleStructure(fm);
            if (fm.state == FightManager.State.Fighting) Drive(fm, ref hitrunPhase);
            if (Time.realtimeSinceStartup - wall > maxMatchSeconds) break;
            yield return new WaitForFixedUpdate();
        }

        int res = fm.outcome == FightManager.Outcome.PlayerWin ? 1
                : fm.outcome == FightManager.Outcome.PlayerLoss ? -1 : 0;
        results.Add(res);
        lengths.Add(fm.elapsed);
        // Round-2-critic CRITICAL 2/3: ENEMY flipped time is recorded too. The
        // whole topple verb was measured last round from one side only, which
        // cannot tell "my flipper works" from "my flipper flips me".
        rows.Add(string.Format(CultureInfo.InvariantCulture,
            "run {0}: {1} at {2:F1}s  dealt {3:F0} taken {4:F0}  pieces {5}/{6} vs {7}/{8}  "
            + "flipped self {9:F1}s enemy {10:F1}s  | {11}",
            r, fm.outcome, fm.elapsed, fm.player.dealt, fm.player.taken,
            fm.player.partsNow, fm.player.startParts,
            fm.enemy.partsNow, fm.enemy.startParts,
            fm.player.flippedTime, fm.enemy.flippedTime, fm.causeLine));
        // ROUND-1-IMPL: the damage card, split by the system that produced it.
        // A whole-match "dealt 485" cannot answer "does limb energy convert
        // into damage"; ram, disc and limb are three different weapons sharing
        // one number. Also the pack-seam headroom and the per-run detach ledger,
        // which is what the r1 critic had to reconstruct by hand.
        var pb = fm.player.bot;
        if (pb != null)
            rows.Add(string.Format(CultureInfo.InvariantCulture,
                // DISC CONVERSION (2026-07-27): the disc channel is RETIRED and
                // now reads 0 in every run. Only SpinnerWeapon ever wrote it, and
                // nothing attaches SpinnerWeapon any more - a disc is a rotor edge
                // on a spindle, so its damage arrives on the LIMB channel with
                // every other actuated weapon. The column is kept, labelled, so
                // that reading an old sweep file next to a new one does not look
                // like the discs stopped working.
                "   src: ram {0:F0}/{1} · limb {2:F0}/{3} (max hit {4:F1}) · disc[retired] {5:F0}/{6}"
                + "   packSeam {7:F2}",
                pb.dealtRam, pb.hitsRam, pb.dealtLimb, pb.hitsLimb, pb.maxLimbHit,
                pb.dealtDisc, pb.hitsDisc, pb.VitalSeamLoad()));
        if (CompoundRobot.detachLogOn)
        {
            foreach (string d in CompoundRobot.detachLog) rows.Add("   det " + d);
            CompoundRobot.detachLog.Clear();
        }
        yield return null;
    }

    /// <summary>Attached-part count per body, differenced each FixedUpdate.
    /// This is the metric finding 2 is stated in.</summary>
    void SampleStructure(FightManager fm)
    {
        int p = Attached(fm.player.bot), e = Attached(fm.enemy.bot);
        if (lastP >= 0 && p >= 0 && lastP - p > worstStepP) worstStepP = lastP - p;
        if (lastE >= 0 && e >= 0 && lastE - e > worstStepE) worstStepE = lastE - e;
        lastP = p; lastE = e;
    }

    static int Attached(CompoundRobot b)
    {
        if (b == null) return -1;
        int n = 0;
        foreach (var p in b.parts) if (!p.detached) n++;
        return n;
    }

    void Drive(FightManager fm, ref float phase)
    {
        var d = fm.player.drive;
        if (d == null || fm.player.bot == null) return;
        d.useAI = true;                       // scripted stand-in for the human
        if (policy == "afk") { d.aiThrottle = 0f; d.aiSteer = 0f; return; }

        Vector3 self = fm.player.bot.rb.worldCenterOfMass;
        Vector3 tgt;
        if (policy == "wall") tgt = new Vector3(6f, self.y, 0f);
        else if (fm.enemy.bot != null) tgt = fm.enemy.bot.rb.worldCenterOfMass;
        else tgt = Vector3.zero;

        Vector3 to = tgt - self; to.y = 0f;
        Vector3 fwd = fm.player.bot.transform.TransformDirection(
            d.WheelCount() > 0 ? d.RollOf(0) : Vector3.forward);
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-4f || to.sqrMagnitude < 1e-4f) return;

        float ang = Vector3.SignedAngle(fwd.normalized, to.normalized, Vector3.up);
        float thr = 1f;
        if (policy == "hitrun")
        {
            // 3 s in, 2 s out — the archetype a player actually uses against a
            // spinner, and the one that most needs input to matter.
            phase += Time.fixedDeltaTime;
            if (phase > 5f) phase -= 5f;
            if (phase > 3f) { thr = -1f; ang = -ang; }
        }
        // ROUND-7 FIX 3 - the scripted driver can now turn around.
        //
        // Until this, every policy here held throttle at +-1 and clamped steer,
        // with no reverse branch, so a machine that ended up pointing the wrong
        // way had to come about on forward lock alone - and RaycastWheelDrive
        // shrinks the lock with speed on purpose. MEASURED at timeScale 1 with
        // the 1451 kg PB_STEEL: throttle 1.00, steer saturated at 1.00, and
        // dot(nose, directionToEnemy) = -0.72 - the "charging" bot was driving
        // AWAY from the fight at full power, for seconds at a time, for as long
        // as it took to scrub round. Its whole-match damage card read 9.
        //
        // AIController has had exactly this branch since round 2 (|ang| >= 100
        // -> reverse and swing the tail), so "charge" was never a stand-in for
        // a player: it was a driver strictly worse than the opponent it was
        // being scored against, and that gap sat underneath every AFK-vs-CHARGE
        // number this project has reported. The instrument has to be at least
        // as competent as the AI before the comparison means anything.
        if (Mathf.Abs(ang) >= 100f)
        {
            d.aiThrottle = -0.6f * Mathf.Sign(thr == 0f ? 1f : thr);
            d.aiSteer = ang > 0f ? -1f : 1f;
            return;
        }
        d.aiThrottle = thr;
        d.aiSteer = Mathf.Clamp(ang / 45f, -1f, 1f);
    }

    void Finish(string err)
    {
        Time.timeScale = 1f;
        if (err != null) { summary = "HARNESS ERROR: " + err; rows.Add(summary); }
        var sb = new StringBuilder();
        foreach (string s in rows) sb.AppendLine(s);
        try { File.WriteAllText(outPath, sb.ToString()); } catch { }
        done = true;
    }
}

}
