// ===========================================================================
// BuilderManager.Drive.cs — POINT WHERE YOU WANT TO GO (Robot Brawl: Scrapyard).
// owen, 2026-09-10: "The driving still feels tricky. Can you check popular
// mobile driving games and see how they design the driving experience?"
//
// What the top mobile drivers do (Asphalt 9 TouchDrive, Mario Kart Tour,
// Real Racing 3's assists, Brawl Stars' stick): THE PLAYER PICKS A DIRECTION,
// THE GAME HANDLES THE STEERING. Ours was a tank stick - Y throttle, X a
// steering wheel relative to the nose - so "straight" needed a thumb held
// exactly vertical, and a tilt commanded a TURN RATE the player had to
// integrate against physics lag. Three patches (dead zone, speed gain, a
// heading hold) fought the mapping instead of replacing it.
//
// Now: the stick's ANGLE is a heading in the world, relative to the camera
// (up on the stick = away from the camera); its LENGTH is speed. The machine
// steers itself toward that heading with a damped P-controller and stops
// turning when it gets there; straight ahead is the ordinary case. Pull
// straight back to reverse. The same stick drives a challenge bout (the
// player's side is the AI channel, fed from here; FIRE goes to the
// actuators' aiFire) - one control scheme for the whole game.
//
// The stick's frame is the camera's heading THE MOMENT THE STICK IS PRESSED,
// and holds until it is released. MEASURED the other way first (MapBench
// 2026-09-10): with the frame following the camera, and the camera
// recentring behind the machine, "up" meant "wherever the nose points" -
// the machine's own veer (12 deg in 2 s at speed) was never corrected, and
// a held 40 deg chased the swinging camera into a continuous turn. Latched,
// up is the direction you pressed toward, a held angle is a heading you
// reach and keep, and the camera is free to swing behind you (fast with the
// stick up or released, slowly while it points off-axis). Re-aim by lifting
// the thumb - the frame re-latches to what you now see.
// ===========================================================================
using UnityEngine;

namespace RobotBrawl.Phase0
{
public partial class BuilderManager
{
    public const float STICK_DEAD = 0.12f;      // stick length below which it is centred
    // THE CONTROLLER IS TWO LOOPS. Heading error -> a wanted yaw rate (capped),
    // then a PI on yaw rate -> steer. MEASURED (2026-09-10) why one loop would
    // not do: the skid-steer's response to steer is wildly speed-dependent -
    // full lock at rest pivots at 250-500 deg/s (the ring trace: a heading
    // loop overshot -61 -> +81 -> +112 -> -154), while at 6 m/s 0.50 turns
    // 15 deg/s and 0.55 breaks grip into a spin. A rate loop asks the wheels
    // for a yaw rate and leans on them until they deliver, whatever the speed.
    public const float RATE_PER_DEG = 3.0f;     // wanted yaw rate, deg/s per degree of heading error
    public const float W_MAX = 140f;            // ...capped here (deg/s)
    public const float RATE_KP = 0.003f;        // steer per deg/s of rate error
    public const float RATE_KI = 0.006f;        // steer per (deg/s * s) of rate error
    // STEER AUTHORITY FALLS WITH SPEED. MEASURED (turn trace, 2026-09-10):
    // full lock at 6.3 m/s threw the rookie into a 290 deg/s spin - the
    // differential has no speed scaling (only steerable wheels do, in the
    // shared drive). 1.0 at rest, STEER_CAP_FAST at CAP_SPEED and beyond.
    public const float STEER_CAP_FAST = 0.50f;  // 0.55 peaked at 218 deg/s and overshot 40 -> 70 (measured)
    public const float CAP_SPEED = 6f;          // the map's top speed; the ring allows more, the cap does not
    public const float TURN_THROTTLE = 0.35f;   // throttle floor while turning hard (wheels steer only when rolling)
    public const float TURN_EASE_DEG = 75f;     // throttle eases to the floor as the error approaches this
    public const float BRAKE_ERR = 15f;         // brake into a turn sharper than this... (25 let the speed come back mid-turn and stalled the turn at 17 deg for a second - measured)
    public const float BRAKE_ABOVE = 3.5f;      // ...while faster than this (m/s); the skid-steer turns briskly below it
    public const float BRAKE_THR = -0.25f;
    public const float COAST_BRAKE = 0.35f;  // 0.18 left 0.7 m/s after 6 s from 6.4 m/s (measured)     // a released stick brakes to a stop (coasting ran >6 s from 6 m/s - measured)
    public const float REV_CONE = 35f;          // stick within this of straight back = reverse
    public const float STEER_SLEW = 8f;         // steer units per second toward the target
    public const float CAM_SLEW_FREE = 140f;    // deg/s the camera recentres with the stick up or released
    public const float CAM_SLEW_HELD = 15f;     // deg/s while the stick points off-axis

    Transform camPivot;                          // FollowCamera chases this: robot position, lagged heading
    float camYaw; bool camYawInit;
    bool stickWasHeld, reversing;
    float rateInt;                               // the rate loop's integrator (see RATE_KI)
    float frameYaw;                              // the camera's heading when the stick was pressed
    float driveWantYaw, driveErrDeg, driveStickAng;
    bool yardStickFight;                         // a challenge bout driven from the stick (AI channel)

    // bench seams
    public float DriveWantYaw { get { return driveWantYaw; } }
    public float DriveErrNow { get { return driveErrDeg; } }
    public bool DriveReversing { get { return reversing; } }
    public float CamYawNow { get { return camYaw; } }
    public bool YardStickFight { get { return yardStickFight; } }
    public static float HeadingYaw(Vector3 fwd) { fwd.y = 0f; return fwd.sqrMagnitude < 1e-4f ? 0f : Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg; }

    /// <summary>The stick as one vector: x right, y up, length 0..1, centred
    /// inside STICK_DEAD. Touch writes the debug channels (the 0.001 sentinel
    /// while unheld reads as centred); keys give the eight directions.</summary>
    public static Vector2 StickNow()
    {
        var v = new Vector2(Phase0Input.Steer(), Phase0Input.Throttle());
        if (v.sqrMagnitude > 1f) v.Normalize();
        return v.magnitude < STICK_DEAD ? Vector2.zero : v;
    }

    /// <summary>Drive `bot` from the stick, relative to `camera`: turns it to the
    /// stick's heading, throttle by the stick's length, reverse straight back.</summary>
    void PointDrive(CompoundRobot bot, RaycastWheelDrive drv, Vector3 fwdLocal, Transform camera)
    {
        if (bot == null || drv == null) return;
        Vector2 st = StickNow();
        float dt = Time.deltaTime;
        if (st == Vector2.zero)
        {
            stickWasHeld = false; reversing = false; rateInt = 0f;
            // let go = brake to a stop, both ways
            Vector3 fwd0 = bot.transform.TransformDirection(fwdLocal);
            float along0 = Vector3.Dot(VelUtil.GetLinearVelocity(bot.rb), fwd0.normalized);
            drv.aiThrottle = along0 > 0.3f ? -COAST_BRAKE : along0 < -0.3f ? COAST_BRAKE : 0f;
            drv.aiSteer = Mathf.MoveTowards(drv.aiSteer, 0f, STEER_SLEW * dt);
            driveErrDeg = 0f; driveStickAng = 0f;
            return;
        }
        if (!stickWasHeld) frameYaw = HeadingYaw(camera.forward);    // latched for the hold
        stickWasHeld = true;
        driveStickAng = Mathf.Atan2(st.x, st.y) * Mathf.Rad2Deg;      // 0 up, + right
        driveWantYaw = frameYaw + driveStickAng;
        Vector3 fwd = bot.transform.TransformDirection(fwdLocal);
        float haveYaw = HeadingYaw(fwd);
        float err = Mathf.DeltaAngle(haveYaw, driveWantYaw);            // + : the heading is to the right
        driveErrDeg = err;
        float mag = st.magnitude;
        float yawRate = bot.rb.angularVelocity.y * Mathf.Rad2Deg;      // + : turning right (measured 2026-09-10, MapBench trace)
        float along = Vector3.Dot(VelUtil.GetLinearVelocity(bot.rb), fwd.normalized);

        // straight back = reverse, from a stop or a crawl; a hysteresis so the
        // edge of the cone does not flicker between turning round and backing
        bool backCone = Mathf.Abs(driveStickAng) > 180f - REV_CONE;
        if (!reversing && backCone && along < 1.2f) reversing = true;
        if (reversing && (!backCone && Mathf.Abs(driveStickAng) < 180f - REV_CONE - 20f)) reversing = false;
        if (reversing)
        {
            rateInt = 0f;
            drv.aiThrottle = -mag;
            drv.aiSteer = Mathf.MoveTowards(drv.aiSteer, 0f, STEER_SLEW * dt);
            return;
        }

        // loop 1: heading error -> wanted yaw rate; loop 2: PI on the rate,
        // capped by speed (the integrator is clamped to the cap: no wind-up)
        float cap = Mathf.Lerp(1f, STEER_CAP_FAST, Mathf.Clamp01(Mathf.Abs(along) / CAP_SPEED));
        float wWant = Mathf.Clamp(err * RATE_PER_DEG, -W_MAX, W_MAX);
        float wErr = wWant - yawRate;
        rateInt = Mathf.Clamp(rateInt + wErr * RATE_KI * dt, -cap, cap);
        float steer = Mathf.Clamp(wErr * RATE_KP + rateInt, -cap, cap);
        drv.aiSteer = Mathf.MoveTowards(drv.aiSteer, steer, STEER_SLEW * dt);
        // throttle: the stick's length, eased off as the turn gets sharp -
        // SLOW DOWN TO TURN. MEASURED (turn trace, 2026-09-10): at 6.2 m/s the
        // safe steer (0.50) turns the rookie at 15 deg/s, and a hair more
        // (0.55) breaks grip into a 200 deg/s spin; at 3-4 m/s the same
        // machine turns briskly and never spins. So a sharp error sheds
        // speed first, the cap rises with the loss of speed, and the throttle
        // comes back as the nose comes round. Never below the floor that
        // keeps the wheels rolling to steer.
        float ease = 1f - Mathf.Clamp01(Mathf.Abs(err) / TURN_EASE_DEG);
        drv.aiThrottle = mag * Mathf.Max(TURN_THROTTLE, ease);
        // easing alone sheds nothing at the speed cap (measured: 6.2 m/s held
        // through a 40 deg turn with the throttle at 0.56) - so brake into a
        // sharp turn at speed; the differential keeps its sense under a
        // negative command (right side slowed = still a right turn)
        if (Mathf.Abs(err) > BRAKE_ERR && along > BRAKE_ABOVE) drv.aiThrottle = BRAKE_THR;
    }

    /// <summary>The map camera's pivot: the machine's position, a heading that
    /// recentres behind it fast when the stick is up or released, slowly while
    /// it points off-axis.</summary>
    void PumpCamPivot(CompoundRobot bot, Vector3 fwdLocal)
    {
        if (camPivot == null || bot == null) return;
        float have = HeadingYaw(bot.transform.TransformDirection(fwdLocal));
        if (!camYawInit) { camYaw = have; camYawInit = true; }
        bool offAxis = stickWasHeld && Mathf.Abs(driveStickAng) > 20f && !reversing;
        float slew = offAxis ? CAM_SLEW_HELD : CAM_SLEW_FREE;
        camYaw = Mathf.MoveTowardsAngle(camYaw, have, slew * Time.deltaTime);
        camPivot.position = bot.rb.position;
        camPivot.rotation = Quaternion.Euler(0f, camYaw, 0f);
    }

    Transform EnsureCamPivot(CompoundRobot bot, Vector3 fwdLocal)
    {
        if (camPivot == null) camPivot = new GameObject("cam_pivot").transform;
        camYawInit = false;
        PumpCamPivot(bot, fwdLocal);
        return camPivot;
    }
    void DropCamPivot()
    {
        if (camPivot != null) { Destroy(camPivot.gameObject); camPivot = null; }
        camYawInit = false; stickWasHeld = false; reversing = false;
    }

    /// <summary>A challenge bout: the player's side runs on the AI channel, fed
    /// from the stick each frame once the bell has rung; FIRE to the actuators.</summary>
    void PumpStickFight()
    {
        if (!yardStickFight || mode != Mode.Fight || testRobot == null || testDrive == null) return;
        var fm = Object.FindFirstObjectByType<FightManager>();
        PumpBoutReport(fm);
        if (fm == null || fm.state != FightManager.State.Fighting) return;
        PointDrive(testRobot, testDrive, driveDir, cam.transform);
        bool fire = Phase0Input.FireHeld();
        foreach (var act in testRobot.GetComponentsInChildren<Actuator>()) act.aiFire = fire;
    }
}
}
