// DEAD CODE AS OF 2026-07-27 - NOT ATTACHED BY ANYTHING.
//
// spinner and spinnerSaw used to be self-powered: CompoundRobot.Build bolted
// this component onto them and they spun on their own. They are now UNPOWERED
// ROTOR EDGES that only turn when the player bolts them past a spindle, so the
// game has exactly one spinning system (Actuator) instead of two that had to
// be kept in agreement about energy, drain, cooldown and windage.
//
// The file is kept because its constants are still quoted in Actuator's design
// notes and because the round-2..6 measurements referenced here are the record
// of how the disc was tuned. If you ever re-attach it, be aware that Actuator
// will ALSO be driving that part if it sits in a limb, and the two will fight.
using System.Collections.Generic;
using UnityEngine;

namespace RobotBrawl.Phase0
{
/// <summary>
/// Phase 2A virtual spinner energy model (design doc §6.4 — solver survival).
///
/// The disc does NOT physically rotate at 900 RPM (PhysX would need sub-ms
/// steps to keep a 0.34 m disc at 94 rad/s stable against contacts). Instead:
///   · ω is tracked here, ramping linearly to maxRPM over ~3 s;
///   · E = ½·I·ω² with I = ½·m·r² from the disc's real mass/radius;
///   · the VISUAL disc spins at min(ω, VIS_OMEGA_CAP) so rotation stays
///     readable instead of strobing;
///   · a trigger box slightly larger than the disc is the hit volume. On
///     overlap with an ENEMY robot's part: 40% of E drains into damage
///     (plus a small relative-velocity linear-impulse term), a CLAMPED
///     impulse shoves both bodies for the visible hit, and ω dips
///     (E left the disc) then re-spins-up.
/// Same-robot and non-robot (walls, floor, debris) contacts are ignored.
/// </summary>
public class SpinnerWeapon : MonoBehaviour
{
    public static float MAX_RPM = 900f;
    public static float SPIN_UP_TIME = 3f;       // s, spin-DOWN rate reference
    /// <summary>ROUND-7 FIX 2 - the spin motor delivers POWER, not a fixed
    /// rad/s ramp.
    ///
    /// Before this, `step = MaxOmega / SPIN_UP_TIME * dt` gave every disc the
    /// same 3 s to reach 900 rpm regardless of its inertia, so a tungsten rim
    /// got 2.5x a steel rim's bite AND recovered from each bite just as fast.
    /// Measured consequence (round-6 critic): the widowmaker's tungsten disc
    /// went 7W/0L against seven distinct, validated player archetypes, six of
    /// them core-KO'd between 6.6 s and 40.8 s, damage ratios 3:1 to 40:1. It
    /// was the only roster entry no legal build survived.
    ///
    /// A real spin motor has a power rating. Integrating dE = P.dt through
    /// E = 1/2.I.w^2 makes recovery time fall straight out of the rim's own
    /// inertia - no per-material constant, no per-opponent tuning, and the
    /// energy bill per bite is UNCHANGED (the same dE still goes on the
    /// PowerPlant). What changes is that heavy rims now buy their bite with
    /// TIME as well as with joules, which is the trade section 6.2 always
    /// claimed and never implemented:
    ///     Aluminium  I 0.23  E 1.0 kJ   0 -> full  1.0 s   re-bite 0.4 s
    ///     Steel      I 0.66  E 2.9 kJ   0 -> full  2.9 s   re-bite 1.2 s
    ///     Tungsten   I 1.61  E 7.2 kJ   0 -> full  7.2 s   re-bite 2.9 s
    /// (re-bite = regaining the DRAIN_FRAC of E that one bite takes out.)
    ///
    /// The value is not free-tuned: it is set so the REFERENCE disc - the
    /// 0.34 m steel rim the DamageResolver calibration band is written
    /// against - still takes SPIN_UP_TIME to reach MAX_RPM, so the shipped
    /// steel spinner behaves as it always did and only the extremes move.</summary>
    public static float MOTOR_KW = 1.0f;
    public static float DRAIN_FRAC = 0.4f;       // E_drain = 0.4·E per bite
    public static float MIN_BITE_E = 60f;        // J — below this the disc just tinks
    public static float HIT_COOLDOWN = 0.5f;     // s per victim collider (round-1 fix 7: was 0.3 — pacing, matches DamageResolver.PART_IMMUNITY)
    public static float VIS_OMEGA_CAP = 10f;     // rad/s (~570°/s) readable visual spin
    public static float LIN_IMP_CAP = 600f;      // cap on the linear-impulse damage term
    /// <summary>Round-6 fix 2: bearing friction + windage, as a first-order
    /// drag rate (rad/s shed per rad/s, per second). A disc used to cost
    /// energy ONLY while accelerating, so once it was at speed a coasting rim
    /// kept biting for free - and on a FLAT pack the ramp step went to zero
    /// and the disc held full speed forever, which is the exact opposite of
    /// what the class doc promised. A real rim bleeds; holding one at speed is
    /// a continuous bill.
    ///
    /// The bill is derived, never hand-tuned: dE/dt = 2*k*E, so it scales with
    /// the disc's own inertia and nothing else. MEASURED in the editor at
    /// timeScale 1, throttle held at zero so the whole demand is the disc:
    /// the 0.34 m spinner runs I = 0.66 kg m2, E = 2.9 kJ at 900 rpm, and
    /// draws a standing 0.33 kW - 18 kJ of a 480 kJ pack over 20 s of simply
    /// carrying a disc that is up to speed. A bigger, heavier rim pays
    /// proportionally more, automatically.
    ///
    /// The material axis rides on this for free. Measured after round-6 fix 6
    /// (see BuilderManager.SpawnBot) on the same 0.34 m disc:
    ///     Aluminium  I 0.23   E 1.0 kJ   standing 0.11 kW
    ///     Steel      I 0.66   E 2.9 kJ   standing 0.33 kW
    ///     Tungsten   I 1.61   E 7.2 kJ   standing 0.82 kW
    /// so a tungsten rim really does cost ~2.5x a steel one to carry, and
    /// ~7x an aluminium one, without a single per-material constant anywhere
    /// in this file. That ratio is section 6.2's premise, and until fix 6 it
    /// was not implemented at all.</summary>
    public static float SPIN_DRAG = 0.02f;       // 1/s

    public CompoundRobot robot;
    public int partIdx;
    /// <summary>§6.2. Spinning up is bought, not free: the cost is the actual
    /// mechanical energy entering the disc, dE = d(1/2 I w^2), over
    /// PowerPlant.SPIN_EFFICIENCY. That makes a tungsten rim genuinely
    /// expensive to run without a single hand-tuned number - its inertia
    /// already says so - and it makes every bite, which drains 40% of E, a
    /// bill the battery has to pay again.</summary>
    public PowerPlant power;
    /// <summary>Virtual angular speed, rad/s.</summary>
    public float omega;

    float inertia;      // ½·m·r² (kg·m²)
    float radiusM;
    Transform disc;     // the "spin_disc" visual child
    float visAngle;
    readonly Dictionary<Collider, float> lastBite = new Dictionary<Collider, float>();

    public float MaxOmega { get { return MAX_RPM * Mathf.PI * 2f / 60f; } }
    public float Energy { get { return 0.5f * inertia * omega * omega; } }
    public float Inertia { get { return inertia; } }

    /// <summary>Called by CompoundRobot.Build right after the part is added.</summary>
    public void Init(CompoundRobot owner, int idx)
    {
        robot = owner;
        partIdx = idx;
        var part = robot.parts[idx];
        Vector3 s = part.spec.size;
        radiusM = Mathf.Max(s.x, Mathf.Max(s.y, s.z)) * 0.5f;
        inertia = 0.5f * part.mass * radiusM * radiusM;

        // Hit volume: trigger box slightly larger than the disc's AABB.
        // Triggers are invisible to the wheel suspension raycasts (filtered)
        // and never produce contact impulses — the joint-stress system only
        // ever sees the solid collider.
        var trig = gameObject.AddComponent<BoxCollider>();
        trig.isTrigger = true;
        trig.size = s + Vector3.one * 0.08f;

        foreach (var t in gameObject.GetComponentsInChildren<Transform>(true))
            if (t.name == "spin_disc") { disc = t; break; }
    }

    void FixedUpdate()
    {
        if (robot == null) return;
        // combatEnabled gate (round-1 fix 1): no pre-bell spin-up — the disc
        // starts its 3 s ramp when the fight actually starts.
        bool live = robot.combatEnabled && !robot.dead && !robot.parts[partIdx].detached;
        if (power == null) power = robot.GetComponent<PowerPlant>();

        float dt = Time.fixedDeltaTime;

        // (1) PASSIVE LOSS FIRST, always, powered or not: bearings and air do
        //     not care whether the battery is alive. This is what makes
        //     holding a disc at speed cost money, and it is what actually
        //     spins a disc down when the pack dies (round-6 fix 2).
        omega = Mathf.Max(0f, omega - SPIN_DRAG * omega * dt);

        // (2) Then the motor, rate-limited by the supply the plant could meet
        //     last step. Charging for the delta ACROSS THE MOTOR STEP - not
        //     across the whole frame - is what puts the standing draw on the
        //     bill: at equilibrium the motor buys back exactly the energy the
        //     drag took, every step, for as long as the disc is up.
        float step = MaxOmega / SPIN_UP_TIME * dt;   // spin-DOWN rate
        if (live)
        {
            // Round-7 fix 2: a power-limited motor. dE = P.dt, and
            // w' = sqrt(w^2 + 2.P.dt/I) is the exact integral of that through
            // E = 1/2.I.w^2 - so the heavier the rim, the longer every rad/s
            // costs, automatically. supplyFrac still throttles it, so a
            // straining machine still cannot get its disc up.
            float pW = MOTOR_KW * 1000f * (power != null ? power.supplyFrac : 1f);
            float before = omega;
            omega = Mathf.Min(MaxOmega, Mathf.Sqrt(Mathf.Max(0f,
                        before * before + 2f * pW * dt / Mathf.Max(inertia, 1e-4f))));
            if (power != null && omega > before)
            {
                float dE = 0.5f * inertia * (omega * omega - before * before);   // joules
                power.Draw(dE / Mathf.Max(0.01f, PowerPlant.SPIN_EFFICIENCY)
                           / 1000f / Mathf.Max(dt, 1e-5f));                      // kJ/s = kW
            }
        }
        else omega = Mathf.MoveTowards(omega, 0f, step);
        if (disc != null)
        {
            visAngle += Mathf.Min(omega, VIS_OMEGA_CAP) * Mathf.Rad2Deg * Time.fixedDeltaTime;
            disc.localRotation = Quaternion.AngleAxis(visAngle, Vector3.up);
        }
    }

    void OnTriggerEnter(Collider other) { TryBite(other); }
    void OnTriggerStay(Collider other) { TryBite(other); }

    void TryBite(Collider other)
    {
        if (robot == null || robot.dead || robot.parts[partIdx].detached) return;

        Rigidbody orb = other.attachedRigidbody;
        if (orb == null || orb == robot.rb) return;              // world / own body
        CompoundRobot victim = orb.GetComponent<CompoundRobot>();
        if (victim == null || victim == robot || victim.dead) return; // debris, walls
        if (!robot.combatEnabled || !victim.combatEnabled) return;    // pre-bell settle
        int vIdx = victim.PartIndexOf(other);
        if (vIdx < 0) return;

        float E = Energy;
        if (E < MIN_BITE_E) return;
        float t;
        if (lastBite.TryGetValue(other, out t) && Time.time - t < HIT_COOLDOWN) return;
        lastBite[other] = Time.time;

        // --- damage: drained energy + relative linear impulse term ---
        float drain = DRAIN_FRAC * E;
        Vector3 pos = other.ClosestPoint(transform.position);
        Vector3 relV = VelUtil.GetLinearVelocity(robot.rb) - VelUtil.GetLinearVelocity(orb);
        float linImp = Mathf.Min(relV.magnitude * Mathf.Min(robot.rb.mass, orb.mass) * 0.5f,
                                 LIN_IMP_CAP);
        // Round-2 fix 1: the linear term scales with spin fraction — a barely
        // spun-up disc DEFLECTS a ram instead of biting it. Measured: at 1 s
        // post-bell (ω = ⅓ max) the capped linear term alone dealt ~40 dmg,
        // which made the deterministic opening clash a 21-35% HP event.
        linImp *= Mathf.Clamp01(omega / MaxOmega);
        float hardness = robot.parts[partIdx].spec.edgeHardness;
        if (hardness <= 0f) hardness = 1f;
        // ROUND-2-DEV FIX (critic MAJOR "the damage card's 'disc' column is
        // dead code - all spinner damage is booked as ramming"). This was the
        // only ApplyHit call site in the project that never passed a source
        // tag, so it silently defaulted to SRC_RAM. Measured consequence the
        // critic found: a PARKED disc robot with throttle 0 dealt 481-1051
        // damage and the card read "disc 0/0", filing all of it as ramming -
        // which a stationary machine cannot do. Damage numbers are unchanged;
        // only the counter it lands in changes.
        DamageResolver.ApplyHit(robot, victim, vIdx, drain + linImp, hardness, pos,
                                DamageResolver.SRC_DISC);

        // Energy left the disc: ω dip (E ∝ ω²), then FixedUpdate re-spins-up.
        omega *= Mathf.Sqrt(1f - DRAIN_FRAC);

        // --- clamped physical shove (the visible hit) ---
        Vector3 dir = orb.worldCenterOfMass - robot.rb.worldCenterOfMass;
        dir.y = 0f;
        dir = dir.sqrMagnitude < 1e-4f ? Vector3.forward : dir.normalized;
        dir = (dir + Vector3.up * 0.35f).normalized;
        float shove = Mathf.Min(drain * 0.3f, DamageResolver.SHOVE_CAP);
        orb.AddForceAtPosition(dir * shove, pos, ForceMode.Impulse);
        robot.rb.AddForce(dir * (-shove * 0.5f), ForceMode.Impulse);
    }
}

}