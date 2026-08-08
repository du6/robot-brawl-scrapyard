using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RobotBrawl.Phase0
{
/// <summary>VERB BENCH - does each macro verb DO WHAT ITS NAME SAYS?
///
/// Written 2026-08-07 after owen found that MOVE AWAY FROM never
/// reversed at any bearing: its base speed went through Mathf.Abs(), so
/// "away" was really "turn around and drive forward", and nose-on to a
/// wall it ground itself INTO the wall at +0.40 net thrust. Every bench
/// was green through that. They had to be: they all check DATA (does the
/// program validate, does the runner arm, does the channel get written)
/// and none checked the PHYSICAL CLAIM the verb makes to the player.
///
/// So this bench asserts outcomes in world space: after running verb V
/// for N seconds, did the distance/bearing move the way the label
/// promises? WALL is the target of choice - the arena boundary is
/// deterministic and needs no dummy robot.
///
/// Run in play mode: VerbBench.Run(); read [VerbBench] console lines.</summary>
public class VerbBench : MonoBehaviour
{
    public static VerbBench Run()
    { return new GameObject("verb_bench").AddComponent<VerbBench>(); }

    public int passed, failed;
    public bool finished;
    readonly List<string> log = new List<string>();
    void Check(bool ok, string what)
    { if (ok) passed++; else failed++; log.Add((ok ? "PASS  " : "FAIL  ") + what); }

    // wall sensor + compass + 4 wheels. No weapon: these are drive verbs.
    const string BODY =
        "core|0.000,0.700,0.000|0|0.00,0.00,0.00|Aluminum\n" +
        "beam|0.000,0.700,0.450|0|0.00,0.00,0.00|Aluminum\n" +
        "beam|0.000,0.700,-0.450|0|0.00,0.00,0.00|Aluminum\n" +
        "battery|0.000,0.975,0.000|0|0.00,0.00,0.00|Aluminum\n" +
        "wallsensor|0.000,0.700,0.840|0|0.00,0.00,1.00|Aluminum\n" +
        "compass|0.000,0.880,-0.450|0|0.00,1.00,0.00|Aluminum\n" +
        "wheel|0.170,0.700,0.450|0|1.00,0.00,0.00|Rubber\n" +
        "wheel|-0.170,0.700,0.450|0|-1.00,0.00,0.00|Rubber\n" +
        "wheel|0.170,0.700,-0.450|0|1.00,0.00,0.00|Rubber\n" +
        "wheel|-0.170,0.700,-0.450|0|-1.00,0.00,0.00|Rubber\n";

    BuilderManager bm; CompoundRobot sr; SensorBus bus; RaycastWheelDrive dr;
    ProgramRunner runner;
    bool savedFree;

    /// <summary>The drive rig plus a live spinner and a damage bus, for the
    /// checks that need a weapon or a bus. Geometry verified 2026-08-07.</summary>
    const string ARMED =
        "core|0.000,0.700,0.000|0|0.00,0.00,0.00|Aluminum\n" +
        "beam|-0.250,0.700,0.150|0|0.00,0.00,0.00|Aluminum\n" +
        "beam|0.250,0.700,0.150|0|0.00,0.00,0.00|Aluminum\n" +
        "beam|0.000,0.700,-0.250|90|0.00,0.00,0.00|Aluminum\n" +
        "beam|-0.450,0.700,-0.150|270|0.00,0.00,0.00|Aluminum\n" +
        "beam|0.450,0.700,-0.150|270|0.00,0.00,0.00|Aluminum\n" +
        "beam|0.000,0.700,0.450|0|0.00,0.00,0.00|Aluminum\n" +
        "spindle|0.000,0.800,0.900|0|0.00,0.00,1.00|Aluminum\n" +
        "spinner|0.000,0.800,1.100|0|0.00,0.00,1.00|Steel\n" +
        "wheel|0.620,0.700,0.000|90|1.00,0.00,0.00|Rubber\n" +
        "wheel|-0.620,0.700,0.000|90|-1.00,0.00,0.00|Rubber\n" +
        "battery|0.000,0.975,0.000|0|0.00,0.00,0.00|Aluminum\n" +
        "beam|0.000,0.700,-0.650|0|0.00,0.00,0.00|Steel\n" +
        "wheel|0.170,0.700,-0.800|0|1.00,0.00,0.00|Rubber\n" +
        "wheel|-0.170,0.700,-0.800|0|-1.00,0.00,0.00|Rubber\n" +
        "beam|0.225,0.975,0.000|0|0.00,0.00,0.00|ABS\n" +
        "beam|-0.250,0.900,0.150|0|0.00,0.00,0.00|ABS\n" +
        "compass|0.000,0.880,-0.650|0|0.00,1.00,0.00|Aluminum\n" +
        "dmgbus|0.230,0.700,0.000|0|1.00,0.00,0.00|Aluminum\n";

    Actuator[] acts = new Actuator[0];

    IEnumerator SpawnArmed()
    {
        Disarm();
        bm.BackToBuild(); yield return null;
        bm.LoadSnapshot(ARMED);
        bm.StartTest(); yield return null; yield return null;
        sr = bm.testRobot;
        bus = sr != null ? sr.GetComponent<SensorBus>() : null;
        dr = sr != null ? sr.GetComponent<RaycastWheelDrive>() : null;
        acts = sr != null ? sr.GetComponentsInChildren<Actuator>() : new Actuator[0];
        yield return new WaitForSeconds(0.6f);
    }

    bool Firing()
    { foreach (var a in acts) if (a != null && a.aiFire) return true; return false; }

    /// <summary>Shed one leaf beam so PartsLost goes 0 -> 1: a condition we can
    /// flip on demand without a second robot in the arena.</summary>
    void ShedOneBeam()
    {
        for (int i = sr.parts.Count - 1; i >= 0; i--)
            if (sr.parts[i].spec.id.StartsWith("beam") && !sr.parts[i].detached)
            { sr.DestroyPart(i); return; }
    }

    /// <summary>Arm a whole multi-hat program (ArmBody is the one-hat case).</summary>
    void ArmProgram(RobotProgram pr)
    {
        Disarm();
        runner = sr.gameObject.AddComponent<ProgramRunner>();
        runner.Init(sr, bm.testDrive);
        runner.program = pr;
        sr.controlSource = ControlSource.Program;
    }

    IEnumerator Spawn()
    {
        Disarm();
        bm.BackToBuild(); yield return null;
        bm.LoadSnapshot(BODY);
        bm.StartTest(); yield return null; yield return null;
        sr = bm.testRobot;
        bus = sr != null ? sr.GetComponent<SensorBus>() : null;
        dr = sr != null ? sr.GetComponent<RaycastWheelDrive>() : null;
        yield return new WaitForSeconds(0.6f);
    }

    /// <summary>Rotate in place until the nearest wall sits at `want`
    /// degrees off the nose. Iterated because the body settles.</summary>
    IEnumerator AimWallAt(float want)
    {
        var rb = sr != null ? sr.GetComponent<Rigidbody>() : null;
        for (int i = 0; i < 3; i++)
        {
            if (bus == null || !bus.wallValid) yield break;
            sr.transform.Rotate(0f, -Mathf.DeltaAngle(want, bus.wallBearingDeg), 0f);
            if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            yield return new WaitForSeconds(0.25f);
        }
    }

    void Arm(PBlock step)
    {
        Disarm();
        var p = new RobotProgram { title = "verb" };
        var h = new PHat { name = "V" };
        h.when.Add(PCondTerm.Always());
        h.body.Add(step);
        p.hats.Add(h);
        runner = sr.gameObject.AddComponent<ProgramRunner>();
        runner.Init(sr, bm.testDrive);
        runner.program = p;
        sr.controlSource = ControlSource.Program;
    }
    void Disarm()
    { if (runner != null) { DestroyImmediate(runner); runner = null; } }

    /// <summary>Arm a hat with a WHOLE body; Arm() is the one-step case.</summary>
    void ArmBody(params PBlock[] steps)
    {
        Disarm();
        var p = new RobotProgram { title = "verb" };
        var h = new PHat { name = "V" };
        h.when.Add(PCondTerm.Always());
        foreach (var s in steps) h.body.Add(s);
        p.hats.Add(h);
        runner = sr.gameObject.AddComponent<ProgramRunner>();
        runner.Init(sr, bm.testDrive);
        runner.program = p;
        sr.controlSource = ControlSource.Program;
    }

    /// <summary>Largest wheel command standing on the drive right now.</summary>
    float MaxCmd()
    {
        float m = 0f;
        for (int c = 0; c < dr.ChannelCount; c++)
            m = Mathf.Max(m, Mathf.Abs(dr.GetWheelCmd(c)));
        return m;
    }

    /// <summary>Run a single verb for `secs` and report how the wall
    /// distance moved, plus the worst net thrust seen toward it.</summary>
    IEnumerator Verb(PBlock step, float secs)
    {
        d0 = bus.wallDist; b0 = bus.wallBearingDeg; netWorst = 0f;
        Arm(step);
        float t = 0f;
        while (t < secs)
        {
            yield return new WaitForSeconds(0.25f); t += 0.25f;
            float sum = 0f; int n = 0;
            for (int i = 0; i < dr.ChannelCount; i++) { sum += dr.GetWheelCmd(i); n++; }
            float net = n > 0 ? sum / n : 0f;
            // Only while the target is AHEAD. Once the robot has reversed far
            // enough that the nearest wall is BEHIND it, forward thrust is the
            // correct way to keep increasing the distance - and the bearing
            // flips to 180. Sampling that as a violation would fail the fix
            // for doing the right thing.
            if (Mathf.Abs(bus.wallBearingDeg) < 90f && net > netWorst) netWorst = net;
        }
        d1 = bus.wallDist; b1 = bus.wallBearingDeg;
        Disarm();
    }
    float d0, d1, b0, b1, netWorst;
    static string F(float v) { return v.ToString("0.00"); }

    IEnumerator Start()
    {
        bm = Object.FindFirstObjectByType<BuilderManager>();
        if (bm == null) { Finish(); yield break; }
        savedFree = Career.devFreeBuild;
        Career.devFreeBuild = true;

        // ---- A. MOVE TOWARD WALL closes on it ---------------------------
        yield return Spawn();
        if (sr == null || bus == null) { Check(false, "rig spawns with a wall sensor"); Finish(); yield break; }
        Check(true, "rig spawns with a wall sensor");
        yield return AimWallAt(0f);
        yield return Verb(PBlock.MkMoveRel(PTarget.Wall, 70f, 3f, 0), 1.5f);
        Check(d1 < d0 - 0.2f,
              "MOVE TOWARD WALL closes the range (" + F(d0) + " -> " + F(d1) + ")");

        // ---- B. MOVE AWAY FROM WALL, wall DEAD AHEAD --------------------
        // NB: AWAY is the SIGN OF THE POWER ARGUMENT (-80 = away at 80%).
        // owen 2026-08-07. This is the regression test for the reverse bug:
        // the old code commanded +0.40 net thrust INTO the wall here.
        yield return Spawn();
        yield return AimWallAt(0f);
        yield return Verb(PBlock.MkMoveRel(PTarget.Wall, -80f, 3f, 0), 1.5f);
        Check(d1 > d0 + 0.3f,
              "[owen] MOVE AWAY FROM WALL backs out with the wall DEAD AHEAD ("
              + F(d0) + " -> " + F(d1) + ")");
        Check(netWorst <= 0.05f,
              "...and never thrusts forward while the wall is AHEAD (worst "
              + F(netWorst) + ")");

        // ---- C. wall ABEAM: must not close ------------------------------
        yield return Spawn();
        yield return AimWallAt(90f);
        yield return Verb(PBlock.MkMoveRel(PTarget.Wall, -80f, 3f, 0), 1.5f);
        Check(d1 > d0 - 0.2f,
              "MOVE AWAY FROM WALL does not close with the wall ABEAM ("
              + F(d0) + " -> " + F(d1) + ")");

        // ---- D. wall ASTERN: drives forward away ------------------------
        yield return Spawn();
        yield return AimWallAt(180f);
        yield return Verb(PBlock.MkMoveRel(PTarget.Wall, -80f, 3f, 0), 1.5f);
        Check(d1 > d0 + 0.3f,
              "MOVE AWAY FROM WALL drives off with the wall ASTERN ("
              + F(d0) + " -> " + F(d1) + ")");

        // ---- E. SIDE TO WALL: every option must converge on its offset --
        // The controller is WriteTurn(Sign(DeltaAngle(offset, bear))), so
        // each option has a BALANCE POINT at its antipode where the sign
        // flips on a hundredth of a degree. For BACK TO (offset 180) that
        // antipode is the wall DEAD AHEAD - the commonest case there is.
        yield return SideTo(0, 0f, 90f, "FRONT TO");
        yield return SideTo(1, 90f, 0f, "RIGHT TO");
        yield return SideTo(2, 180f, 0f, "BACK TO");
        yield return SideTo(3, -90f, 0f, "LEFT TO");

        // ---- F. plain MOVE: forward is forward, back is back ------------
        yield return Spawn();
        yield return AimWallAt(0f);
        yield return Verb(PBlock.MkMove(70f, 3f, 0), 1.2f);
        Check(d1 < d0 - 0.2f, "MOVE FORWARD moves forward (" + F(d0) + " -> " + F(d1) + ")");
        yield return Spawn();
        yield return AimWallAt(0f);
        yield return Verb(PBlock.MkMove(-70f, 3f, 0), 1.2f);
        Check(d1 > d0 + 0.2f, "MOVE BACK moves backward (" + F(d0) + " -> " + F(d1) + ")");

        // ---- G. a hat with NO blocking step must still DRIVE (F1) -------
        // Behaviour critic 2026-08-07: the body ran to its end inside one
        // physics step, Advance called ReleaseHat, and ReleaseHat's
        // ZeroChannels wiped the SET before ApplyChannels ever saw it. The
        // hat produced NOTHING, forever - and Validate calls it legal, so
        // 'WHEN enemy is near: WEAPON ON' was a green program that did
        // nothing at all. Now the hat re-arms in place instead.
        yield return Spawn();
        yield return AimWallAt(180f);            // open floor ahead
        float g0 = bus.wallDist;
        ArmBody(PBlock.MkMove(60f, 0f, 0));      // SET flavour: no secs, no rounds
        // A freshly armed program waits for the next 5 Hz trigger tick before
        // its first hat fires - up to 0.20 s. That is arming latency, not a
        // hold gap; let it pass before counting or it reads as a 12% dropout.
        yield return new WaitForSeconds(0.35f);
        int live = 0, samples = 0;
        for (int i = 0; i < 75; i++)
        {
            yield return new WaitForFixedUpdate();
            samples++;
            if (MaxCmd() > 0.01f) live++;
        }
        float g1 = bus.wallDist;
        Disarm();
        Check(g1 > g0 + 0.3f,
              "a hat with NO blocking step still drives (" + F(g0) + " -> " + F(g1) + ")");
        Check(samples > 0 && live * 100 / samples >= 95,
              "...and holds its command every step, not 0% (duty "
              + (samples > 0 ? live * 100 / samples : 0) + "%)");

        // ---- H. no dead gap across a loop boundary (F3) -----------------
        // A short blocking body went dark between passes: Advance released
        // the hat and nothing ran until the next 5 Hz trigger tick, so a
        // 0.3 s body ran at 75% duty on a 0.40 s period.
        yield return Spawn();
        yield return AimWallAt(180f);
        ArmBody(PBlock.MkMove(60f, 0.3f, 0));
        // A freshly armed program waits for the next 5 Hz trigger tick before
        // its first hat fires - up to 0.20 s. That is arming latency, not a
        // hold gap; let it pass before counting or it reads as a 12% dropout.
        yield return new WaitForSeconds(0.35f);
        int live2 = 0, s2 = 0;
        for (int i = 0; i < 90; i++)
        {
            yield return new WaitForFixedUpdate();
            s2++;
            if (MaxCmd() > 0.01f) live2++;
        }
        Disarm();
        Check(s2 > 0 && live2 * 100 / s2 >= 95,
              "a 0.3 s body loops with no dead gap between passes (duty "
              + (s2 > 0 ? live2 * 100 / s2 : 0) + "%, was 75%)");

        // ---- I. a robot that DIES must stop commanding (F2) -------------
        // FixedUpdate's early return skipped ApplyChannels, so the zero never
        // reached the drive and directWheelCmd stayed latched true: a dead
        // robot kept driving on its last command. Measured 7.4 m of
        // posthumous travel at w=0.70 before this fix. We assert the COMMAND
        // is zero, not the distance - coasting is a separate finding (F5).
        yield return Spawn();
        yield return AimWallAt(180f);
        ArmBody(PBlock.MkMove(80f, 6f, 0));
        yield return new WaitForSeconds(0.7f);
        sr.dead = true;
        yield return new WaitForSeconds(0.25f);
        bool stillHeld = dr.directWheelCmd;
        Vector3 pk = sr.transform.position;
        float worstCmd = 0f;
        for (int i = 0; i < 75; i++)
        { yield return new WaitForFixedUpdate(); worstCmd = Mathf.Max(worstCmd, MaxCmd()); }
        float rolled = Vector3.Distance(pk, sr.transform.position);
        sr.dead = false;
        Disarm();
        Check(!stillHeld, "a robot that DIES hands the wheels back (directWheelCmd cleared)");
        Check(worstCmd <= 0.01f,
              "...and commands nothing at all once dead (worst " + F(worstCmd)
              + ", coasted " + F(rolled) + " m)");

        // ---- J. MOVE TOWARD from a WIDE bearing must close (F4) ---------
        // TOWARD held a constant FULL forward base while its steer saturated,
        // so past ~46 deg off the nose it flew a circle it could never close:
        // measured 0.59 m -> 5.33 m of range while commanded TOWARD. The base
        // is scaled by cos(bearing) now, so a saturated steer pivots instead.
        yield return Spawn();
        yield return AimWallAt(75f);
        yield return Verb(PBlock.MkMoveRel(PTarget.Wall, 70f, 4f, 0), 2.5f);
        Check(d1 < d0 - 0.3f,
              "MOVE TOWARD WALL closes from 75 deg off the nose ("
              + F(d0) + " -> " + F(d1) + ")");

        // ---- K. a FOREVER behind a FALSE IF must not starve the hats below
        // R3 critic, the case Validate cannot see: the loop spins FOREVER ->
        // IF(false) -> skip -> END forever, and because preemption is strictly
        // higher-hats-only, the WALL!/FLIPPED! safety hat underneath it never
        // ran once. Measured 8.0 s, 0.00 m, 0% wheel command.
        yield return Spawn();
        yield return AimWallAt(180f);
        {
            var sp = new RobotProgram { title = "spin" };
            var h0 = new PHat { name = "SPIN" };
            h0.when.Add(PCondTerm.Always());
            h0.body.Add(PBlock.MkForever());
            h0.body.Add(PBlock.MkIf(PCondTerm.Mk(PCond.EdgeDist, PCmp.Less, -1f)));  // never
            h0.body.Add(PBlock.MkMove(70f, 1f, 0));
            h0.body.Add(PBlock.MkEndIf());
            h0.body.Add(PBlock.MkEnd());
            var h1 = new PHat { name = "SAFETY" };
            h1.when.Add(PCondTerm.Always());
            h1.body.Add(PBlock.MkMove(70f, 1f, 0));
            sp.hats.Add(h0); sp.hats.Add(h1);
            ArmProgram(sp);
            float k0 = bus.wallDist;
            int liveK = 0;
            for (int i = 0; i < 100; i++)
            { yield return new WaitForFixedUpdate(); if (MaxCmd() > 0.01f) liveK++; }
            float k1 = bus.wallDist;
            Disarm();
            Check(k1 > k0 + 0.3f,
                  "a FOREVER behind a FALSE IF does not starve the safety hat under it ("
                  + F(k0) + " -> " + F(k1) + ")");
            Check(liveK >= 50, "...and that safety hat gets the wheels (" + liveK + "/100 steps)");
        }

        // ---- L. a timed step STOPS -- coasting is not stopping (F5) --------
        // R3 critic: MOVE 70% FOR 1.0 s travelled under power and then coasted
        // 1.3x to 2.5x further, so no sequence of timed steps landed where the
        // program said. ExitStep now brakes instead of just zeroing.
        yield return Spawn();
        yield return AimWallAt(180f);
        ArmBody(PBlock.MkMove(70f, 1.0f, 0), PBlock.MkWait(2.0f));
        yield return new WaitForSeconds(0.35f);         // arming latency
        while (runner != null && runner.activeStep == 0) yield return new WaitForFixedUpdate();
        Vector3 c0 = sr.transform.position;
        yield return new WaitForSeconds(1.5f);
        float coast = Vector3.Distance(c0, sr.transform.position);
        Disarm();
        Check(coast < 0.9f,
              "a timed MOVE brakes when its time is up (" + F(coast) + " m of coast)");

        // ---- M. the weapon latch, held to exactly its contract -------------
        // R2 made WEAPON ON survive a hat change (it was dying on every one --
        // 9% duty in a real fight). R3 found that fix too broad in two ways: a
        // RUN WEAPON FOR n that got PREEMPTED never stopped, and the blade kept
        // spinning when no hat was true at all.
        yield return SpawnArmed();
        if (sr == null || acts.Length == 0)
        { Check(false, "armed rig spawns with a weapon"); Finish(); yield break; }
        Check(true, "armed rig spawns with a weapon");
        {
            var wp = new RobotProgram { title = "w" };
            var h0 = new PHat { name = "CUT" };
            h0.when.Add(PCondTerm.Mk(PCond.PartsLost, PCmp.Greater, 0.5f));
            h0.body.Add(PBlock.MkMove(-40f, 1.0f, 0));          // no weapon block at all
            var h1 = new PHat { name = "SEEK" };
            h1.when.Add(PCondTerm.Always());
            h1.body.Add(PBlock.MkWeapon(true));
            h1.body.Add(PBlock.MkMove(40f, 1.0f, 0));
            wp.hats.Add(h0); wp.hats.Add(h1);
            ArmProgram(wp);
            yield return new WaitForSeconds(1.2f);
            ShedOneBeam();
            int on = 0;
            for (int i = 0; i < 80; i++)
            { yield return new WaitForFixedUpdate(); if (Firing()) on++; }
            Disarm();
            Check(on >= 76, "WEAPON ON survives a hat handover (" + on + "/80 steps firing)");
        }
        {
            var wp = new RobotProgram { title = "w2" };
            var h = new PHat { name = "ONLY" };
            h.when.Add(PCondTerm.Mk(PCond.PartsLost, PCmp.Less, 0.5f));   // true until we shed
            h.body.Add(PBlock.MkWeapon(true));
            h.body.Add(PBlock.MkMove(30f, 1.0f, 0));
            wp.hats.Add(h);
            ArmProgram(wp);
            yield return new WaitForSeconds(1.2f);
            bool wasOn = Firing();
            ShedOneBeam();
            yield return new WaitForSeconds(1.0f);
            bool nowOn = Firing();
            Disarm();
            Check(wasOn && !nowOn,
                  "...and stops when NO hat is true at all (was " + wasOn + ", now " + nowOn + ")");
        }

        Finish();
    }

    /// <summary>Aim the wall at `from`, run SIDE TO with `side`, and
    /// require the bearing to converge on that side's offset.</summary>
    IEnumerator SideTo(int side, float offset, float from, string label)
    {
        yield return Spawn();
        yield return AimWallAt(from);
        yield return Verb(PBlock.MkFaceSide(PTarget.Wall, side), 3f);
        float err0 = Mathf.Abs(Mathf.DeltaAngle(offset, b0));
        float err1 = Mathf.Abs(Mathf.DeltaAngle(offset, b1));
        Check(err1 < 15f,
              "SIDE TO WALL / " + label + " converges from " + F(from)
              + " deg: error " + F(err0) + " -> " + F(err1) + " deg");
    }

    void Finish()
    {
        Disarm();
        if (bm != null) bm.BackToBuild();
        Career.devFreeBuild = savedFree;
        foreach (var l in log) Debug.Log("[VerbBench] " + l);
        Debug.Log(string.Format("[VerbBench] RESULT: {0} pass, {1} fail{2}",
                  passed, failed, failed == 0 ? " - ALL GREEN" : " - FIX NEEDED"));
        finished = true;
    }
}
}
