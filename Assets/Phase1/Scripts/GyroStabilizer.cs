using UnityEngine;

namespace RobotBrawl.Phase0
{

/// <summary>
/// Phase 3 — the gyro stabilizer (design doc §7.1, §10: "gyro stabilizer —
/// it's the self-right mechanic").
///
/// AI PARITY (§8, and the §13 risk row "AI feels dumb or unfair"): before
/// Phase 3 the righting torque lived inside AIController, so the Mauler could
/// always recover from a flip and the player never could — the single biggest
/// fairness gap in the Phase 2 test pass. That code is gone. Self-righting is
/// now HARDWARE: this component is attached by SpawnBot to ANY robot whose
/// build contains at least one gyro part, player or AI, and it reads the same
/// live part list, so a gyro that shears off stops working mid-fight.
///
/// The torque is a clamped actuator, not magic:
///   · applies ONLY while the hull is flipped or beached AND resting on
///     something (so it can't be used as a mid-air thruster),
///   · rolls about the body's LONG axis (the smallest-arm rotation),
///   · direction alternates every 2.5 s of continuous unsuccessful effort so
///     a roll blocked against a wall tries the other way,
///   · strength scales with how many gyros survive, capped — two gyros right
///     you faster, ten do not.
/// </summary>
public class GyroStabilizer : MonoBehaviour
{
    /// <summary>Righting moment arm per gyro, in metres of mass·g. 0.45 beats
    /// the worst-case gravity moment (~0.30 m arm) with margin. This is the
    /// value AIController used, so a one-gyro bot rights exactly as well as
    /// the old hard-coded Mauler did.</summary>
    public static float ARM_PER_GYRO = 0.45f;
    /// <summary>Diminishing returns: total arm is capped at this.</summary>
    public static float ARM_CAP = 0.75f;
    /// <summary>SELF-RIGHTING IS HARDWARE AGAIN (owen, 2026-08-02).
    ///
    /// owen: "the robot automatically reset after being flipped over, which
    /// doesn't look realistic. shall we remove that to encourage users design
    /// their own flip over component?" Yes — this is now 0.
    ///
    /// WHAT THIS OVERTURNS, STATED HONESTLY. Round-5 fix 3 set this to 0.40
    /// against a real measurement: 35% of all matches then ended on the same
    /// no-gyro count-out sentence, and 45% of the palette's build space was
    /// non-viable for want of one 0.24 m cube. That was true, and it is the
    /// risk being accepted here.
    ///
    /// WHY IT IS THE RIGHT CALL NOW ANYWAY. That fix predates Phase 4 giving
    /// the player actuators. A free baseline made the gyro a SPEED upgrade over
    /// something you already got — the same trap shape the engine had, where
    /// you pay for a faster version of a free thing. Self-righting is supposed
    /// to be the enabling part (§7.1), and the game already has every piece of
    /// the real discipline: a flipper doubles as a srimech (Actuator.ApplyTopple,
    /// which the victim's own gyro deliberately does not fight mid-roll), the
    /// judges already score time on your back, and the starter kit ships a gyro
    /// so nobody is stranded on match one.
    ///
    /// "Flipped" now means flipped. You right yourself with a gyro, with an arm
    /// you built, or with a hull shape that does not stay over — or you lose the
    /// clock. If the count-out rate climbs back toward that 35%, the lever to
    /// pull is this constant, and the note above is why.</summary>
    public static float BASE_ARM = 0f;
    /// <summary>...and it takes this long of continuous effort to reach full
    /// strength, which is what makes the gyro an upgrade instead of a tax: a
    /// bare chassis lies there for a couple of seconds first, a gyro applies
    /// its arm from frame one. Kept under EPOCH so the baseline reaches full
    /// torque before the direction-alternation flips it.</summary>
    public static float BASE_SPOOL = 2f;
    /// <summary>Above this spin rate the actuator stops adding energy.</summary>
    public static float MAX_SPIN = 5f;
    /// <summary>Ground probe length below the centre of mass.</summary>
    public static float GROUND_PROBE = 0.9f;
    public static float EPOCH = 2.5f;

    public CompoundRobot self;
    public Vector3 forwardLocal = Vector3.forward;
    /// <summary>Part indices of this build's gyros (set by SpawnBot).</summary>
    public int[] gyroParts = new int[0];

    /// <summary>Debug/telemetry: is the actuator firing right now?</summary>
    public bool active;
    /// <summary>Gyros still attached this step.</summary>
    public int liveGyros;

    float activeSince = -1f;
    int epoch = -1;
    float dir = 1f;

    /// <summary>How many gyro parts are still attached (a sheared gyro is a
    /// dead gyro — the whole point of making it hardware).</summary>
    public int LiveGyros()
    {
        if (self == null) return 0;
        int n = 0;
        foreach (int idx in gyroParts)
            if (idx >= 0 && idx < self.parts.Count && !self.parts[idx].detached) n++;
        return n;
    }

    void FixedUpdate()
    {
        active = false;
        liveGyros = LiveGyros();
        // No gyro, no righting (2026-08-02). The early-out is back: with
        // BASE_ARM at 0 the torque below would be exactly zero anyway, and
        // running the grounded probe regardless would leave `active` reporting
        // true for a machine that is doing nothing — a lie to every harness and
        // HUD that reads it.
        if (liveGyros <= 0 && BASE_ARM <= 0f) { activeSince = -1f; epoch = -1; return; }
        if (self == null || self.rb == null || self.dead) { activeSince = -1f; epoch = -1; return; }

        Rigidbody rb = self.rb;
        float upY = Vector3.Dot(transform.up, Vector3.up);
        float spd = VelUtil.GetLinearVelocity(rb).magnitude;
        // Flipped, or beached on its side and not going anywhere. Cornering
        // tilt (fast + only mildly tilted) must never trigger it.
        bool needed = upY < 0.2f || (upY < 0.75f && spd < 0.35f);
        if (!needed) { activeSince = -1f; epoch = -1; return; }
        if (rb.angularVelocity.magnitude > MAX_SPIN) return;

        // Grounded = ANY non-self solid within reach below the CoM. Own parts
        // do not count as ground (a hull lying between the CoM and the floor
        // used to abort the whole check).
        bool grounded = false;
        foreach (var h in Physics.RaycastAll(rb.worldCenterOfMass, Vector3.down, GROUND_PROBE))
        {
            if (h.collider.isTrigger) continue;
            if (h.rigidbody == rb) continue;
            grounded = true;
            break;
        }
        if (!grounded) return;

        if (activeSince < 0f) activeSince = Time.time;

        Vector3 axis = transform.TransformDirection(forwardLocal);
        axis.y = 0f;
        if (axis.sqrMagnitude < 1e-4f) axis = Vector3.forward;
        axis.Normalize();

        Vector3 want = Vector3.Cross(transform.up, Vector3.up);
        float sgn = Vector3.Dot(want, axis) >= 0f ? 1f : -1f;
        int e = (int)((Time.time - activeSince) / EPOCH);
        if (e != epoch) { epoch = e; dir = (e % 2 == 0 ? 1f : -1f) * sgn; }

        // Gyro arm applies instantly; the baseline (now 0 by default — see
        // BASE_ARM) spools in if anyone ever restores it. The decision this
        // presents to the player is once again "can I get back up at all",
        // which is what makes a self-righting design worth building.
        float spool = Mathf.Clamp01((Time.time - activeSince) / BASE_SPOOL);
        float arm = Mathf.Min(ARM_CAP, ARM_PER_GYRO * liveGyros + BASE_ARM * spool);
        rb.AddTorque(axis * (dir * rb.mass * 9.81f * arm), ForceMode.Force);
        active = true;
    }
}

}
