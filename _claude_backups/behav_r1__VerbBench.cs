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
        yield return SideTo(1, 90f, 0f, "LEFT TO");
        yield return SideTo(2, 180f, 0f, "BACK TO");
        yield return SideTo(3, -90f, 0f, "RIGHT TO");

        // ---- F. plain MOVE: forward is forward, back is back ------------
        yield return Spawn();
        yield return AimWallAt(0f);
        yield return Verb(PBlock.MkMove(70f, 3f, 0), 1.2f);
        Check(d1 < d0 - 0.2f, "MOVE FORWARD moves forward (" + F(d0) + " -> " + F(d1) + ")");
        yield return Spawn();
        yield return AimWallAt(0f);
        yield return Verb(PBlock.MkMove(-70f, 3f, 0), 1.2f);
        Check(d1 > d0 + 0.2f, "MOVE BACK moves backward (" + F(d0) + " -> " + F(d1) + ")");

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
        Check(err1 < 35f,
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
