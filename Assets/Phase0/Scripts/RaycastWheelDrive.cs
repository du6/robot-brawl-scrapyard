using System.Collections.Generic;
using UnityEngine;

namespace RobotBrawl.Phase0
{
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
        public int mountIdx = -1;  // CompoundRobot part this wheel is bolted to (-1 = none tracked)
        public float mass;         // wheel mass (leaves the body when the wheel falls off)
        // Mount-tracking BACKSTOP (fix 3): body-space distance from this
        // anchor to the nearest ATTACHED part's surface, measured at spawn
        // plus SUPPORT_MARGIN. Once no attached part is within this radius
        // the wheel has nothing left to be bolted to and falls off. This
        // covers wheels whose mountIdx never resolved (hand-authored recipes
        // whose wheels do not sit flush against their beam).
        public float supportDist = 0.25f;
    }

    public float radius = 0.18f;
    public float travel = 0.30f;         // suspension travel (m)
    /// <summary>Fraction of travel the suspension sits compressed under the
    /// build's own weight. SizeSuspension() picks the spring rate FOR this
    /// number, so it must be the same constant in both places or the wheel
    /// would be drawn somewhere the physics does not put it.</summary>
    public const float STATIC_COMP_FRAC = 0.35f;
    /// <summary>How far below the top of the strut the wheel hangs when the
    /// robot is simply standing still: travel minus the static compression.
    /// The builder draws a wheel AT ITS MOUNT POINT, so the mount point is
    /// what must coincide with the resting wheel centre - which means the
    /// strut top sits this far ABOVE the mount, not on it. Before this, the
    /// mount was treated as the strut top and every wheel was drawn ~0.20 m
    /// below where it was bolted; on a 0.20 m beam it hung clear underneath.</summary>
    public float RestExtension { get { return travel * (1f - STATIC_COMP_FRAC); } }
    public float maxSteerDeg = 35f;
    public float frictionCoeff = 1.3f;   // μ — clamps drive + lateral force per wheel
    public float maxSpeed = 10f;         // m/s
    /// <summary>Extra clearance (m) added to each wheel's MEASURED spawn-time
    /// support distance. Measuring at spawn (instead of using a fixed radius)
    /// means no legal build can ever shed a wheel on frame one.</summary>
    public static float SUPPORT_MARGIN = 0.04f;

    // ---- Phase 2B: per-instance input source. useAI = true routes throttle
    // and steer from aiThrottle/aiSteer (set by AIController, or zeroed as a
    // control freeze at match end) so the AI bot never reads the keyboard —
    // and Phase0Input (keyboard + debugThrottle test override) drives ONLY
    // the player's bot.
    public bool useAI = false;
    public float aiThrottle = 0f;
    /// <summary>§6.2. Null = unmetered (the Phase 0 sandbox). When set, the
    /// throttle that actually reaches the wheels is scaled by what the power
    /// plant could deliver last step, and this step's demand is posted back.</summary>
    public PowerPlant power;
    public float aiSteer = 0f;

    public float CurrentThrottle() { return useAI ? aiThrottle : Phase0Input.Throttle(); }
    /// <summary>Round-6 fix 1: what the wheels ACTUALLY get, after the power
    /// plant has had its say. CurrentThrottle() is the REQUEST; on a flat pack
    /// it still reads 1.0 while the machine sits motionless, which is how the
    /// referee came to blame a player's mass budget for an empty battery.</summary>
    public float DeliveredThrottle()
    { return CurrentThrottle() * (power != null ? power.supplyFrac : 1f); }
    public float CurrentSteer() { return useAI ? aiSteer : Phase0Input.Steer(); }

    Rigidbody rb;
    CompoundRobot owner;   // set for arena bots — enables mount tracking
    readonly List<Wheel> wheels = new List<Wheel>();
    float spring, damper, drivePerWheel;
    float steerCur;

    /// <summary>
    /// axes (optional): each wheel's axle direction in body space — the same
    /// outward face normal the builder mounted it with, so builder and arena
    /// visuals match. Null = classic Phase 0 behavior (axle along local X).
    /// </summary>
    public void Init(Rigidbody body, Vector3[] anchors, bool[] steerable, Vector3[] axes = null,
                     int[] mounts = null, float[] wheelMasses = null, CompoundRobot ownerRobot = null)
    {
        rb = body;
        owner = ownerRobot;
        int n = anchors.Length;

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
                mountIdx = mounts != null ? mounts[i] : -1,
                mass = wheelMasses != null ? wheelMasses[i] : 0f,
            };
            // Compound wheel visual (fat tire + hub + lugs + axle); every
            // child primitive's collider is destroyed inside the factory —
            // visual only, never seen by the suspension raycasts.
            var vis = new GameObject("wheel_visual_" + i);
            vis.transform.SetParent(transform, false);
            // Frame zero already looks right; FixedUpdate then owns it.
            // POSITION: the mount point IS the resting wheel centre (see
            // RestExtension), so the anchor is already the right place.
            // ROTATION: this line used to be missing, and the comment above was
            // a lie for every viewer that never reaches FixedUpdate. A fresh
            // wheel_visual carries IDENTITY rotation, and the tire cylinder is
            // thin along its LOCAL Y - so an unrotated wheel lies FLAT in the
            // ground plane. In a fight nobody could see it: FixedUpdate assigns
            // baseRot on the first physics step, ~8 ms later. But
            // BuilderManager.StartScout DESTROYS this component immediately
            // after SpawnBot (the preview must not drive), so the correction
            // never ran and every scouted opponent was displayed with four
            // horizontal wheels. MEASURED, same recipe, same SpawnBot:
            //   scout preview  wheel axle = (0.00, 1.00, 0.00)  -> flat
            //   arena fight    wheel axle = (0.99, 0.00, 0.10)  -> upright
            // baseRot is the single source of truth for this and it was already
            // computed above; frame zero just has to USE it. Any other frozen
            // viewer (a paused spawn, a photo rig, a future showcase screen)
            // is fixed by the same line.
            vis.transform.localPosition = anchors[i];
            vis.transform.localRotation = w.baseRot;
            // Axle stub toward the body (-axis side) when we know the mount
            // side; symmetric stubs otherwise (Phase 0 hard-coded bots).
            PartVisualFactory.BuildWheel(vis.transform, radius, radius * 0.78f, axes != null ? -1 : 0);
            w.visual = vis.transform;
            // Fix 3: measure what this wheel actually rests against.
            w.supportDist = NearestPartDist(w.localAnchor) + SUPPORT_MARGIN;
            // Fix 4: a wheel that touches NO part is the failure mode that let
            // the Mauler keep four undroppable wheels after its spine was gone.
            if (owner != null && w.mountIdx < 0)
                Debug.LogWarning(string.Format(
                    "[wheel-mount] {0}: wheel {1} at {2} touches NO part - mountIdx unresolved. "
                    + "Falling back to a {3:F2} m support radius. Check this recipe's track width.",
                    owner.name, i, w.localAnchor, w.supportDist));
            wheels.Add(w);
        }
        // Round-4 fix 1a: hand the shear model a way to price a wheeled limb.
        if (owner != null) owner.WheelsOnPart = WheelsOn;
        SizeSuspension();
    }

    /// <summary>Distance (m, body space) from a point to the nearest ATTACHED
    /// part's box surface; float.MaxValue once every part has detached. Part
    /// boxes are axis-aligned in body space (the builder bakes yaw into
    /// PartSpec.size) and share the wheel anchors' core-relative origin, so a
    /// plain AABB point-distance is exact.</summary>
    float NearestPartDist(Vector3 localPoint)
    {
        if (owner == null) return 0f;
        float best = float.MaxValue;
        foreach (var p in owner.parts)
        {
            if (p.detached) continue;
            Vector3 h = p.spec.size * 0.5f;
            Vector3 c = p.spec.localPos;
            float dx = Mathf.Max(0f, Mathf.Abs(localPoint.x - c.x) - h.x);
            float dy = Mathf.Max(0f, Mathf.Abs(localPoint.y - c.y) - h.y);
            float dz = Mathf.Max(0f, Mathf.Abs(localPoint.z - c.z) - h.z);
            float d = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
            if (d < best) best = d;
        }
        return best;
    }

    /// <summary>Auto-size suspension to the build's mass so any robot rides at
    /// roughly one-third compression. Re-run after a wheel falls off so the
    /// survivors carry the load (stiffer springs, more drive per wheel).</summary>
    void SizeSuspension()
    {
        int n = Mathf.Max(1, wheels.Count);
        float g = -Physics.gravity.y;
        spring = rb.mass * g / (n * STATIC_COMP_FRAC * travel);
        damper = 2f * Mathf.Sqrt(spring * rb.mass / n) * 0.4f;
        drivePerWheel = rb.mass * 9f / n;
    }

    /// <summary>Mount-tracking fix: when the body part a wheel was bolted to
    /// is destroyed or sheared off, the wheel FALLS OFF — its visual becomes
    /// physical debris with inherited velocity, its mass leaves the body, and
    /// the remaining suspension re-sizes. Before this, the wheel kept driving
    /// while hovering where its beam used to be.</summary>
    void DropWheel(int i)
    {
        var w = wheels[i];
        Vector3 anchor = transform.TransformPoint(w.localAnchor);

        // Free the visual as debris: inherited velocity + a tumble, cleaned
        // up like shear shards.
        w.visual.SetParent(null, true);
        var col = w.visual.gameObject.AddComponent<SphereCollider>();
        col.radius = radius;
        var drb = w.visual.gameObject.AddComponent<Rigidbody>();
        drb.mass = Mathf.Max(1f, w.mass);
        VelUtil.SetLinearVelocity(drb, rb.GetPointVelocity(anchor));
        drb.angularVelocity = new Vector3(4f, 1.5f, 4f);
        Object.Destroy(w.visual.gameObject, 8f);

        wheels.RemoveAt(i);
        // The wheel's mass leaves the body; survivors carry the rest.
        if (owner != null && w.mass > 0f)
        {
            owner.extraMass = Mathf.Max(0f, owner.extraMass - w.mass);
            owner.RecomputeMass();
        }
        SizeSuspension();
        // Readable callout, same channel as shears ("PART RIPPED OFF").
        CompoundRobot.OnPartsLost(anchor, 1);
    }

    void FixedUpdate()
    {
        if (rb == null) return;

        // Wheels whose mount part died fall off (checked before forces so a
        // dead mount never drives for even one step).
        if (owner != null)
            for (int i = wheels.Count - 1; i >= 0; i--)
            {
                var w = wheels[i];
                // (a) the part we were bolted to is gone, or (b) nothing attached
                // is left within reach of the anchor. (b) is the fail-CLOSED
                // backstop: mountIdx == -1 previously meant the wheel could never
                // fall off AT ALL, so any bot whose wheels were not flush against
                // a part (e.g. the Mauler after its track was widened to +/-0.24)
                // kept a fully working drivetrain with its spine destroyed.
                bool mountGone = w.mountIdx >= 0 && !owner.PartAlive(w.mountIdx);
                if (mountGone || NearestPartDist(w.localAnchor) > w.supportDist)
                    DropWheel(i);
            }

        float throttle = CurrentThrottle();
        // §6.2: driving costs energy, continuously, in proportion to what you
        // are pushing. A flat battery is not a slow bot, it is a stopped one.
        if (power != null)
        {
            power.Draw(PowerPlant.DriveKW(rb.mass, throttle));
            throttle *= power.supplyFrac;
        }
        float steerTarget = CurrentSteer();
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
            // The mount is where the wheel RESTS, not where the strut starts.
            // Raycast from RestExtension above it so a standing robot draws
            // its wheels exactly where the builder showed them; the wheel then
            // rises off the mount over a bump and droops below it in the air.
            Vector3 strutTop = anchor + up * RestExtension;
            Vector3 down = -up;
            float rayLen = travel + radius;

            RaycastHit best = default(RaycastHit);
            bool found = false;
            foreach (var h in Physics.RaycastAll(strutTop, down, rayLen))
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
                PlaceVisual(w, strutTop, down, travel);
                continue;
            }

            // --- suspension ---
            float comp = Mathf.Clamp(rayLen - best.distance, 0f, travel);
            float compVel = (comp - w.prevComp) / dt;
            w.prevComp = comp;
            float force = Mathf.Max(0f, spring * comp + damper * compVel);
            w.load = force;
            // Push along the GROUND normal, not body up (Phase 2A fix): body-up
            // springs leak a lateral force component whenever the body tilts,
            // and a nose-heavy build (front-mounted spinner) enters a positive
            // feedback loop — creep → grip drags the nose down at ground level
            // → more tilt → more leak — that accelerated a resting bot into
            // the arena wall. Ground-normal springs can't leak on flat ground;
            // fall back to body up on implausible normals (wall grazes).
            Vector3 springDir = Vector3.Dot(best.normal, up) > 0.5f ? best.normal : up;
            rb.AddForceAtPosition(springDir * force, anchor);

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

            PlaceVisual(w, strutTop, down, best.distance - radius);

            // Visual steer yaw around the body's up axis, on top of the
            // mounted axle orientation.
            if (w.steerable)
                w.visual.localRotation = Quaternion.AngleAxis(steerAngle, Vector3.up) * w.baseRot;
        }
    }

    /// <summary>Place the wheel <paramref name="dist"/> below the STRUT TOP.
    /// dist == RestExtension puts it exactly on its mount point.</summary>
    void PlaceVisual(Wheel w, Vector3 strutTop, Vector3 down, float dist)
    {
        w.visual.position = strutTop + down * Mathf.Clamp(dist, 0f, travel);
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

    /// <summary>Round-4 fix 1a: how many live wheels are bolted to body part
    /// <paramref name="idx"/>. CompoundRobot's shear model asks, so the seam
    /// holding a wheeled limb is judged against everything that limb takes with
    /// it rather than against the single beam it can see.</summary>
    public int WheelsOn(int idx)
    {
        int n = 0;
        foreach (var w in wheels) if (w.mountIdx == idx) n++;
        return n;
    }

    /// <summary>Diagnostics: each wheel's body-space roll direction (drive dir).</summary>
    public Vector3 RollOf(int i) { return wheels[i].rollLocal; }
}

}