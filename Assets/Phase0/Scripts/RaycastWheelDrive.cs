using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Custom raycast wheels (design doc §6.4): Unity's WheelCollider is tuned for
/// cars with known mass distributions and misbehaves under arbitrary
/// player-built masses, so Phase 0 settles the wheel approach immediately.
///
/// Each wheel: a raycast from an anchor, spring+damper suspension force along
/// the body's up axis, then drive and lateral-grip forces applied AT THE
/// CONTACT POINT — which is what makes a tall bot genuinely roll in corners
/// (the tippy-bot demo depends on this being honest).
/// </summary>
public class RaycastWheelDrive : MonoBehaviour
{
    class Wheel
    {
        public Vector3 localAnchor;
        public bool steerable;
        public float prevComp;
        public Transform visual;
        public bool grounded;
        public float load;      // current suspension force (N), for friction clamps
        public Quaternion baseRot; // aligns visual cylinder Y to the wheel's axle axis
        public Vector3 axleLocal;  // mounted axle axis in body space (grip direction)
        public Vector3 rollLocal;  // rolling direction in body space (drive direction)
    }

    public float radius = 0.18f;
    public float travel = 0.30f;         // suspension travel (m)
    public float maxSteerDeg = 35f;
    public float frictionCoeff = 1.3f;   // μ — clamps drive + lateral force per wheel
    public float maxSpeed = 10f;         // m/s

    Rigidbody rb;
    readonly List<Wheel> wheels = new List<Wheel>();
    float spring, damper, drivePerWheel;
    float steerCur;

    /// <summary>
    /// axes (optional): each wheel's axle direction in body space — the same
    /// outward face normal the builder mounted it with, so builder and arena
    /// visuals match. Null = classic Phase 0 behavior (axle along local X).
    /// </summary>
    public void Init(Rigidbody body, Vector3[] anchors, bool[] steerable, Vector3[] axes = null)
    {
        rb = body;
        int n = anchors.Length;

        // Auto-size suspension to the build's mass so any robot rides at
        // roughly one-third compression (a Phase-0 convenience; a real build
        // mode would surface these as suspension part stats).
        float g = -Physics.gravity.y;
        spring = rb.mass * g / (n * 0.35f * travel);
        damper = 2f * Mathf.Sqrt(spring * rb.mass / n) * 0.4f;
        drivePerWheel = rb.mass * 9f / n;

        for (int i = 0; i < n; i++)
        {
            Vector3 axis = (axes != null && axes[i].sqrMagnitude > 0.01f) ? axes[i] : new Vector3(1f, 0f, 0f);
            axis.Normalize();

            // Rolling direction = perpendicular to the axle in the ground
            // plane. THIS is what the wheel physics pushes along — so a bot
            // moves the way its wheels actually point, never sideways.
            Vector3 roll = Vector3.Cross(axis, Vector3.up);
            if (roll.sqrMagnitude < 0.01f) roll = Vector3.forward; // degenerate mount
            roll.Normalize();
            // Canonicalize the sign so W always means "the bot's forward-ish":
            // side-mounted wheels (axle ±X) all roll +Z; front/back-mounted
            // wheels (axle ±Z) all roll +X, so throttle can't fight itself.
            if (roll.z < -0.01f || (Mathf.Abs(roll.z) <= 0.01f && roll.x < 0f)) roll = -roll;

            var w = new Wheel
            {
                localAnchor = anchors[i],
                steerable = steerable[i],
                // Cylinder axis (local Y) → the mounted axle axis, exactly as
                // the builder oriented it. Fixes the 90° builder/arena flip.
                baseRot = Quaternion.FromToRotation(Vector3.up, axis),
                axleLocal = axis,
                rollLocal = roll,
            };
            // Compound wheel visual (fat tire + hub + lugs + axle); every
            // child primitive's collider is destroyed inside the factory —
            // visual only, never seen by the suspension raycasts.
            var vis = new GameObject("wheel_visual_" + i);
            vis.transform.SetParent(transform, false);
            // Axle stub toward the body (-axis side) when we know the mount
            // side; symmetric stubs otherwise (Phase 0 hard-coded bots).
            PartVisualFactory.BuildWheel(vis.transform, radius, radius * 0.78f, axes != null ? -1 : 0);
            w.visual = vis.transform;
            wheels.Add(w);
        }
    }

    void FixedUpdate()
    {
        if (rb == null) return;

        float throttle = Phase0Input.Throttle();
        float steerTarget = Phase0Input.Steer();
        steerCur = Mathf.MoveTowards(steerCur, steerTarget, 3.5f * Time.fixedDeltaTime);

        // Speed-sensitive steering authority (round-3 fix): telemetry showed a
        // full-lock turn at ~5 m/s barrel-rolls even the squat reference bot —
        // the tire grip cap (mu=1.3 g) exceeds its static rollover threshold
        // (~0.88 g). Shrink the lock with speed so steady-state lateral accel
        // stays under ~0.75 g: squat bots corner hard, TALL builds still trip
        // (their threshold is far lower) — stability stays a design outcome.
        float vMag = VelUtil.GetLinearVelocity(rb).magnitude;
        float steerScale = 1f / (1f + 0.045f * vMag * vMag);

        Vector3 up = transform.up;
        float dt = Time.fixedDeltaTime;

        foreach (var w in wheels)
        {
            Vector3 anchor = transform.TransformPoint(w.localAnchor);
            Vector3 down = -up;
            float rayLen = travel + radius;

            RaycastHit best = default(RaycastHit);
            bool found = false;
            foreach (var h in Physics.RaycastAll(anchor, down, rayLen))
            {
                if (h.collider.isTrigger) continue;
                if (h.rigidbody == rb) continue; // never collide with ourselves
                if (!found || h.distance < best.distance) { best = h; found = true; }
            }

            w.grounded = found;
            if (!found)
            {
                w.prevComp = 0f;
                w.load = 0f;
                PlaceVisual(w, anchor, down, travel);
                continue;
            }

            // --- suspension ---
            float comp = Mathf.Clamp(rayLen - best.distance, 0f, travel);
            float compVel = (comp - w.prevComp) / dt;
            w.prevComp = comp;
            float force = Mathf.Max(0f, spring * comp + damper * compVel);
            w.load = force;
            rb.AddForceAtPosition(up * force, anchor);

            // --- tire forces at the contact point ---
            // Drive along the wheel's OWN rolling direction and grip along its
            // OWN axle (both mounted in the builder), not body forward/right —
            // otherwise a bot with rotated wheels slides sideways across them.
            float steerAngle = w.steerable ? steerCur * maxSteerDeg * steerScale : 0f;
            Quaternion steerRot = Quaternion.AngleAxis(steerAngle, up);
            Vector3 wheelFwd = steerRot * transform.TransformDirection(w.rollLocal);
            Vector3 wheelRight = steerRot * transform.TransformDirection(w.axleLocal);

            Vector3 pointVel = rb.GetPointVelocity(best.point);
            float fwdVel = Vector3.Dot(pointVel, wheelFwd);
            float latVel = Vector3.Dot(pointVel, wheelRight);
            float maxFriction = frictionCoeff * force;

            // Lateral grip: oppose sideways slip, clamped by μ·N. Exceed the
            // clamp and the bot slides (or, if tall, trips over its own grip).
            // 1.4 (was 2.0): still bites hard, but sideways shoves in combat
            // read as a shove instead of hitting glue.
            float latF = Mathf.Clamp(-latVel * rb.mass * 1.4f / wheels.Count, -maxFriction, maxFriction);

            // Drive / rolling drag, also clamped by μ·N.
            float driveF;
            if (Mathf.Abs(throttle) > 0.01f)
            {
                driveF = throttle * drivePerWheel;
                if (Mathf.Abs(fwdVel) > maxSpeed && Mathf.Sign(fwdVel) == Mathf.Sign(throttle))
                    driveF = 0f; // top speed reached
            }
            else
            {
                driveF = -fwdVel * rb.mass * 0.07f / wheels.Count; // gentle rolling drag (measured coast: 6→5.45 m/s over 1.6 s at 0.15; freer still at 0.07)
            }
            driveF = Mathf.Clamp(driveF, -maxFriction, maxFriction);

            rb.AddForceAtPosition(wheelFwd * driveF + wheelRight * latF, best.point);

            PlaceVisual(w, anchor, down, best.distance - radius);

            // Visual steer yaw around the body's up axis, on top of the
            // mounted axle orientation.
            if (w.steerable)
                w.visual.localRotation = Quaternion.AngleAxis(steerAngle, Vector3.up) * w.baseRot;
        }
    }

    void PlaceVisual(Wheel w, Vector3 anchor, Vector3 down, float dist)
    {
        w.visual.position = anchor + down * Mathf.Clamp(dist, 0f, travel);
        if (!w.steerable) w.visual.localRotation = w.baseRot;
    }

    public float SpeedKmh()
    {
        return rb == null ? 0f : VelUtil.GetLinearVelocity(rb).magnitude * 3.6f;
    }

    /// <summary>Diagnostics: how many wheels currently touch the ground.</summary>
    public int GroundedCount()
    {
        int n = 0;
        foreach (var w in wheels) if (w.grounded) n++;
        return n;
    }

    public int WheelCount() { return wheels.Count; }

    /// <summary>Diagnostics: each wheel's body-space roll direction (drive dir).</summary>
    public Vector3 RollOf(int i) { return wheels[i].rollLocal; }
}
