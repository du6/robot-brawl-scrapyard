// ⚠ EDITOR AND DEVELOPMENT BUILDS ONLY — 2026-08-10.
// This is a QA harness. It was compiling into the SHIPPED iOS player: 44 such
// files, ~13k lines, and the string "dev-only-worker-key" was verified present
// in build/ios global-metadata.dat. Dead weight in the binary, and a surface
// that can register accounts and start fights inside a player's app.
// Nothing in the product references any harness — checked against every
// product script, every scene and every prefab before this guard was added.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.IO;
using UnityEngine;

namespace RobotBrawl.Phase0
{
/// <summary>Measures how the PLAYER'S robot actually turns (owen, 2026-08-05:
/// "it is hard to make a turn").
///
/// Drives the real control path - Phase0Input.debugThrottle / debugSteer, the
/// same two floats the keyboard and the touch stick feed - so whatever this
/// reports is what the player's hands get. Reports steady-state yaw rate and
/// the turn radius that implies (r = v / yawRate), at several speeds, plus the
/// two cases a driver actually complains about: a full-lock turn while
/// accelerating, and trying to pivot on the spot.</summary>
public class TurnProbe : MonoBehaviour
{
    public static bool done;
    public static string report = "";

    BuilderManager bm;
    void W(string s) { report += s + "\n"; Debug.Log("[TurnProbe] " + s); }

    public static TurnProbe Run()
    {
        done = false; report = "";
        return new GameObject("turn_probe").AddComponent<TurnProbe>();
    }

    Rigidbody Body()
    {
        return bm != null && bm.testRobot != null ? bm.testRobot.rb : null;
    }

    /// <summary>Hold throttle/steer for `secs`, then average the yaw rate over
    /// the LAST half - the first half is the transient while steerCur slews and
    /// the body takes a set, and averaging it in would flatter the result.</summary>
    IEnumerator Hold(float throttle, float steer, float secs,
                     System.Action<float, float> report_)
    {
        Phase0Input.debugThrottle = throttle;
        Phase0Input.debugSteer = steer;
        float t = 0f, yawSum = 0f, spdSum = 0f; int n = 0;
        while (t < secs)
        {
            yield return new WaitForFixedUpdate();
            t += Time.fixedDeltaTime;
            var rb = Body();
            if (rb == null) continue;
            if (t > secs * 0.5f)
            {
                yawSum += Mathf.Abs(rb.angularVelocity.y);
                spdSum += VelUtil.GetLinearVelocity(rb).magnitude;
                n++;
            }
        }
        report_(n > 0 ? yawSum / n : 0f, n > 0 ? spdSum / n : 0f);
    }

    IEnumerator Settle(float secs)
    {
        Phase0Input.debugThrottle = 0f;
        Phase0Input.debugSteer = 0f;
        float t = 0f;
        while (t < secs) { yield return new WaitForFixedUpdate(); t += Time.fixedDeltaTime; }
    }

    /// <summary>Put the machine back in the middle, facing +Z, stopped.
    ///
    /// The first run of this probe did not, and the numbers were garbage: the
    /// bot reached the +Z wall inside the first trial and every later trial
    /// measured a robot pinned against it at 0 m/s. A turn probe with no reset
    /// measures the arena, not the steering.</summary>
    IEnumerator Reset()
    {
        var rb = Body();
        if (rb == null) yield break;
        Phase0Input.debugThrottle = 0f;
        Phase0Input.debugSteer = 0f;
        // RESPAWN rather than teleport. Setting rb.position directly left the
        // machine sitting at a height the wheel raycasts did not agree with -
        // no ground hit, no drive force, 0.02 m/s at full throttle while the
        // power plant reported supplyFrac 1.0 and 234 of 240 kJ in the pack.
        // StartTest puts it down the way the game does, which is the only
        // placement whose wheel contacts are known to be right.
        bm.StartTest();
        float t = 0f;
        while (t < 1.0f) { yield return new WaitForFixedUpdate(); t += Time.fixedDeltaTime; }
    }

    IEnumerator Start()
    {
        bm = Object.FindFirstObjectByType<BuilderManager>();
        if (bm == null) { W("FATAL no BuilderManager"); done = true; yield break; }

        bm.LoadSnapshot(File.ReadAllText(Path.Combine(
            Path.Combine(Application.dataPath, "Phase1"), "qa_owen_build_SPINDLE.txt")));
        yield return null;
        string v = bm.Validate();
        W("build: " + bm.PlacedCount + " parts, " + bm.BuildMassInt + " kg, validate=" + (v ?? "OK"));

        bm.StartTest();
        yield return null; yield return null; yield return null;
        var rb0 = Body();
        if (rb0 == null) { W("FATAL no test robot"); done = true; yield break; }
        W("");
        W("throttle  steer   speed m/s   yaw deg/s   turn radius m   180 deg in s");

        // ONE continuous run, no respawns between samples. Separate trials
        // needed a respawn each and StartTest did not always put the wheels
        // back on the ground in time - two of three trials read 0.01 m/s at
        // full throttle. A single accelerating turn sweeps the speed range by
        // itself and cannot suffer that, so the speed column below is the
        // machine's own acceleration curve rather than three staged setups.
        yield return Settle(0.6f);
        Phase0Input.debugThrottle = 1f;
        Phase0Input.debugSteer = 1f;
        W("   t      speed m/s   yaw deg/s   radius m   steerScale");
        float t2 = 0f, acc = 0f; int k = 0;
        float sSum = 0f, ySum = 0f;
        while (t2 < 9f)
        {
            yield return new WaitForFixedUpdate();
            t2 += Time.fixedDeltaTime; acc += Time.fixedDeltaTime;
            var rbn = Body(); if (rbn == null) continue;
            sSum += VelUtil.GetLinearVelocity(rbn).magnitude;
            ySum += Mathf.Abs(rbn.angularVelocity.y);
            k++;
            if (acc >= 0.5f)
            {
                float sp = sSum / k, yw = (ySum / k) * Mathf.Rad2Deg;
                W(string.Format("{0,5:F1}   {1,9:F2}   {2,9:F1}   {3,8:F2}   {4,10:F2}",
                    t2, sp, yw, yw > 0.5f ? sp / (yw * Mathf.Deg2Rad) : 999f,
                    1f / (1f + 0.045f * sp * sp)));
                acc = 0f; sSum = 0f; ySum = 0f; k = 0;
            }
        }

        // The case a driver notices first: can it rotate on the spot at all?
        yield return Settle(1.2f);
        float pyaw = 0f, pspd = 0f;
        yield return Hold(0f, 1f, 2.5f, (y, s) => { pyaw = y; pspd = s; });
        W("");
        W(string.Format("PIVOT on the spot (throttle 0, full lock): {0:F1} deg/s at {1:F2} m/s",
                        pyaw * Mathf.Rad2Deg, pspd));

        Phase0Input.debugThrottle = 0f; Phase0Input.debugSteer = 0f;
        try
        {
            File.WriteAllText(Path.Combine(Path.Combine(Application.dataPath, "Phase1"),
                              "qa_turn_probe.txt"), report);
        }
        catch (System.Exception e) { Debug.LogWarning("[TurnProbe] write failed: " + e.Message); }
        done = true;
    }
}
}
#endif
