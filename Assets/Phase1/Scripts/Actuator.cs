using System.Collections.Generic;
using UnityEngine;

namespace RobotBrawl.Phase0
{

public enum ActuatorKind { Pivot, Spindle, Ram }

/// <summary>
/// PHASE 4 - VIRTUAL ARTICULATION.
///
/// The complaint this exists to answer: "the fighting looks like a bumping
/// test." It did, for four measurable reasons - the player had no weapon input
/// at all, every damage path was contact impulse, the three weapon parts
/// differed only by `edgeHardness`, and nothing on a robot could move (one
/// compound Rigidbody, zero joints in the project).
///
/// An Actuator is a JOINT BOUNDARY. Everything the player bolts past it becomes
/// a LIMB whose pose this component drives. So the weapon is not a part we
/// authored - it is whatever the player built on the far side of a pivot.
/// Pivot + beam + blade is a hammer; pivot + wide plate is a flipper; spindle +
/// beams + blades is a drum the player shaped.
///
/// WHY NOT REAL UNITY JOINTS. Real joints need the limb's parts reparented out
/// from under the robot root, and CompoundRobot reads go.transform.localPosition
/// as root-relative in RecomputeMass, SpawnDebris, NearestPart and the collar
/// geometry. That is surgery on the file section 12 calls the project's biggest
/// risk, bought with emergent floppiness. SpinnerWeapon already proves the
/// cheaper idiom: a virtual rotor with `omega`, `inertia`, a trigger hit volume
/// and energy-drain bites, no rotating rigidbody anywhere. This class is that
/// idiom generalised from "one authored disc" to "any assembly the player made",
/// which means a Spindle actuator SUBSUMES SpinnerWeapon rather than competing.
///
/// WHAT THAT BUYS THE PLAYER. Inertia is summed from the limb's live part masses
/// at their radii about the axis, and the motor is power-limited exactly as the
/// spinner's is (w' = sqrt(w^2 + 2.P.dt/I)). So a long heavy arm stores more
/// energy per swing and takes longer to wind up, a short light one snaps fast
/// and hits for less, and the SAME motor gives a different weapon depending on
/// what got bolted to it. That tradeoff is the feature.
///
/// WHY IT ALSO FIXES THE STANDING CRITICAL. Every critic round found that
/// parking beats fighting: the only way to deal damage was to charge, and
/// RamHit runs on BOTH bodies, so charging costs the attacker what it costs the
/// target. An actuated hit is paid for out of the actuator's own stored energy,
/// not out of closing speed - the first net-positive verb the player has.
/// </summary>
public class Actuator : MonoBehaviour
{
    // ------------------------------------------------------------------ tuning
    /// <summary>Motor rating per actuator, kW, before supplyFrac. Deliberately
    /// larger than SpinnerWeapon.MOTOR_KW (1.0): a disc is allowed thirty
    /// seconds to reach speed because it HOLDS that speed, whereas a hammer that
    /// takes thirty seconds to lift is not a weapon.</summary>
    /// 2.5 kW, not the 6 I first wrote. At 6 kW a hammer reached its rate cap
    /// in ~0.04 s, so there was no wind-up to feel and no meaningful energy
    /// cost. 2.5 kW winds a big arm in ~0.45 s and pulls 7.1 kW off the plant
    /// through EFFICIENCY - real competition with a battery's 8 kW peak, which
    /// is what makes fitting an engine a weapon decision.
    /// SUPERSEDED as a constant by MotorKW() below - kept as the value a
    /// bare, engineless chassis is worth, and as the fallback for any caller
    /// that does not know the engine count.
    public static float MOTOR_KW = 2.5f;

    /// <summary>ROUND-3-CRITIC FIX (CRITICAL 2, "a long heavy arm is strictly
    /// worse than a short cheap one"). Motor rating is no longer a constant -
    /// it is what the player FITTED.
    ///
    /// THE ACTUAL ROOT CAUSE, which is one step behind the tip-speed clamp the
    /// critic measured. For a rotary limb I ~ m.R^2 and w = v_tip/R, so
    /// E = 1/2.I.w^2 = 1/2.m.v_tip^2 - the stored energy never depended on arm
    /// LENGTH at all, only on mass and tip speed. Meanwhile wind-up time is
    /// E / MOTOR_KW, and MOTOR_KW was a flat 2.5 kW for every actuator on every
    /// robot. Energy per swing divided by seconds per swing is therefore
    /// MOTOR_KW for EVERY weapon in the game, and DamageResolver.ApplyHit is
    /// linear in energy, so every weapon did the same damage per second no
    /// matter what was bolted to it. That is exactly what the critic measured:
    /// a 1099 J CarbonFiber whisk (wind-up 0.44 s) and a 13997 J Tungsten hook
    /// (wind-up 5.60 s) landed the same damage rate, so the hook was 33x the
    /// price for none of the output. Raising the tip-speed clamp alone would
    /// not have changed that ratio by one point.
    ///
    /// So the size of your weapon is now a question about the size of your
    /// POWER TRAIN. Base 1.5 kW is what a chassis with no engine gets - enough
    /// that a bare actuator still twitches, not enough to run a real weapon.
    /// Each live engine adds 3.0 kW, capped at 12 kW.
    ///
    /// WHY THESE NUMBERS. An actuator drawing p kW of mechanical power pulls
    /// p / EFFICIENCY(0.35) kW off the plant. At the 12 kW cap that is 34 kW,
    /// and an engine supplies 14 kW peak - so three engines is exactly what it
    /// takes to feed a maxed motor, and PowerPlant's own ceiling enforces the
    /// same build decision from the other side without a second rule. It also
    /// makes weapon use, not locomotion, the dominant draw (critic finding 5):
    /// a maxed motor winding continuously empties a 300 kJ pack in ~9 s, where
    /// driving a 700 kg machine flat out now costs 0.875 kW.
    ///
    /// The engine already cost 14 kW of ceiling, 60 kJ, mass and credits, and
    /// bought nothing a battery did not. Now it is the weapon-rate part.</summary>
    ///
    /// RE-PITCHED IN THE SAME ROUND, FROM MY OWN SWEEP. My first cut was
    /// 1.5 + 3.0/engine, which puts the DEFAULT one-engine build at 4.5 kW -
    /// 1.8x the old flat 2.5 kW. That is a nerf, not a lever, and I measured it
    /// as one. Energy per swing is E/EFFICIENCY no matter what the motor is
    /// rated at, so a faster motor buys more swings per match and therefore
    /// spends MORE energy per match. Sweep S2 (929 kg Aluminium flipper vs
    /// mauler/Veteran, charge, n=4) went flat at 55, 63 and 68 s in three of
    /// four matches on a 300 kJ pack whose DRIVE draw alone would have lasted
    /// 259 s, and sweep S3 (2215 kg Steel flipper) had a power count-out in all
    /// four. Every existing build got quietly worse.
    ///
    /// 1.0 + 1.5/engine instead, chosen so that ONE ENGINE IS EXACTLY 2.5 kW -
    /// the value every measurement in this project was taken at. Nothing
    /// regresses; the change is purely additive above one engine:
    ///   0 engines 1.0 kW | 1: 2.5 | 2: 4.0 | 3: 5.5 | 4: 7.0 | 8+: 12.0 (cap)
    /// A 2.8x throughput swing across the realistic 1-4 engine range, which is
    /// what a big limb needs in order to be worth building, without taxing the
    /// player who did not ask for one.
    public static float MOTOR_KW_BASE = 1.0f;
    public static float MOTOR_KW_PER_ENGINE = 1.5f;
    public static float MOTOR_KW_CAP = 12f;
    /// <summary>Motor rating for a robot carrying this many LIVE engines.
    /// Shearing an engine off mid-fight slows every weapon on the machine,
    /// which is the point: the power train is a target.</summary>
    public static float MotorKW(int liveEngines)
    {
        return Mathf.Min(MOTOR_KW_CAP, MOTOR_KW_BASE + MOTOR_KW_PER_ENGINE * Mathf.Max(0, liveEngines));
    }

    /// <summary>ROUND-3-CRITIC FIX (CRITICAL 2, second half: "every rotary
    /// weapon is clamped to exactly 12.5 m/s tip speed regardless of what you
    /// build"). That number was not a design choice, it was
    /// STEP_DISP_CAP / fixedDeltaTime = 0.25 / 0.02 falling out of the
    /// anti-tunnelling clamp, identical for every limb ever built. The critic
    /// verified it on three limbs (tipR 0.660 -> 18.94 rad/s, 0.980 -> 12.76,
    /// 1.025 -> 12.20, all exactly 12.5/tipR).
    ///
    /// Tip speed is now a MATERIAL property, which is what it is in reality: a
    /// spinning bar fails on rim stress, sigma ~ rho.v^2, so what a bar can be
    /// spun to is set by what it is made of and not by how long it is. Since
    /// E = 1/2.m.v_tip^2, and denser materials also bring more m, this is the
    /// one place where paying for Titanium or Tungsten on the EDGE buys a
    /// strictly better weapon - the critic's finding that expensive materials
    /// are worse was true partly because they bought nothing here.
    ///
    /// sqrt(strengthRel) because energy goes as v^2 and we want E to scale
    /// LINEARLY with strength, not quadratically. Against MatDB that gives:
    /// ABS 0.35 -> 7.4 (floored to 9) | Aluminum 0.6 -> 9.7 | Steel 1.0 -> 12.5
    /// (unchanged, so every existing measurement on a Steel edge still holds)
    /// | CarbonFiber 1.1 -> 13.1 | Tungsten 1.2 -> 13.7 | Titanium 1.4 -> 14.8.
    ///
    /// The 18 m/s ceiling is a TUNNELLING budget, not a balance number: at
    /// 18 m/s a tip covers 0.36 m per fixed step against the old 0.25 m, and
    /// the limb's solid collider is teleported rather than solved. The trigger
    /// is swept to cover the gap (see UpdateSweptVolumes) but the solid box is
    /// not, so this is as far as the idiom stretches without real
    /// joints. The 9 m/s floor exists so an ABS edge is bad, not broken.</summary>
    public static float TIP_SPEED_BASE = 12.5f;
    public static float TIP_SPEED_MIN = 9f;
    public static float TIP_SPEED_MAX = 18f;
    public static float TipSpeedCap(float strengthRel)
    {
        return Mathf.Clamp(TIP_SPEED_BASE * Mathf.Sqrt(Mathf.Max(0.05f, strengthRel)),
                           TIP_SPEED_MIN, TIP_SPEED_MAX);
    }

    /// <summary>ROUND-3-CRITIC FIX (CRITICAL 3, "toppling is 3x to 11x short of
    /// the angular impulse required"). Fraction of a lift-biased edge's drained
    /// swing energy that becomes ROTATION of the victim. See ApplyTopple for
    /// why this is an energy term and not an impulse term any more.
    ///
    /// WHY 0.85, AND WHY NOT THE 0.6 I FIRST WROTE. The bar is fixed by
    /// geometry: rolling the 707 kg reference skeleton over its 0.27 m
    /// half-track lifts its centre of mass 0.118 m, which costs
    /// 707 * 9.81 * 0.118 = 818 J. Measured drain of every flipper on disk,
    /// AFTER the material tip-speed change above (which correctly made an ABS
    /// edge slower and therefore weaker):
    ///   ABS      449.7 kg  E 1153 J  drain  461 J -> lift 294 J   cannot flip
    ///   Aluminium 928.8 kg  E 5484 J  drain 2194 J -> lift 1399 J  flips
    ///   Steel    2215.4 kg  E 14770 J drain 5908 J -> lift 3766 J  flips heavy
    /// At 0.6 the Aluminium flipper offered 987 J against 818 J needed - a 1.2x
    /// margin that wheel scrub, the competing linear shove and an imperfect
    /// contact angle all eat, and no enemy topple was observed in sweeps S2/S3.
    /// 0.85 gives it 1.7x and leaves the ABS flipper still unable to do it,
    /// which is correct and is NOT a tuning failure: 461 J of stored energy
    /// physically cannot lift 818 J of robot at any efficiency below 1. The
    /// verb is meant to be tight - a flipper has to be BUILT, out of something
    /// that holds an edge, not stumbled into.
    ///
    /// It stays strictly below 1.0 on purpose. This term can never manufacture
    /// energy; the worst it can do is spend everything the limb had.</summary>
    public static float LIFT_EFFICIENCY = 0.85f;
    /// <summary>Ceiling on a single topple, N.m.s. Sized off the largest thing
    /// that should ever be flippable: a 2500 kg Tungsten machine with roughly
    /// 300 kg.m2 about its long axis needs ~2900 J of roll energy, i.e.
    /// sqrt(2*300*2900) = 1319 N.m.s. 1500 leaves headroom and still stops any
    /// solver excursion from launching a robot off the arena.</summary>
    public static float TOPPLE_IMP_CAP = 1500f;
    /// <summary>Pivot sweep, degrees. Past this the limb reverses and returns.</summary>
    public static float PIVOT_ARC_DEG = 150f;
    /// <summary>Unpowered return rate, deg/s. Costs nothing - it is a spring.
    ///
    /// ROUND-2-DEV: 200 -> 500. MEASURED WHY. The return stroke assigns
    /// rate = 0 (see the Returning case), so it deals no damage and stores no
    /// energy - its speed is a SPRING STIFFNESS choice, not an energy one, and
    /// nothing in the damage model reads it. At 200 deg/s a 150 deg arc spent
    /// 0.75 s of every cycle inert; measured against the b10 hammer's ~0.35 s
    /// powered sweep that made the return alone 52% of the cycle and was the
    /// single largest term in the critic's "~15% duty cycle" finding.
    /// 500 deg/s = 8.73 rad/s puts the return at 0.30 s. It is clamped in
    /// ReturnRate() so the tip never exceeds STEP_DISP_CAP per fixed step -
    /// the same displacement guarantee the powered stroke has - so a long arm
    /// returns proportionally slower instead of teleporting through things.</summary>
    public static float PIVOT_RETURN_DPS = 500f;
    public static float RAM_STROKE_M = 0.45f;
    public static float RAM_RETURN_MPS = 1.0f;
    public static float SPINDLE_MAX_RPM = 900f;
    /// <summary>Hard cap on how far any limb collider may move in one fixed
    /// step, metres. Colliders are teleported rather than solved, so exceeding
    /// this tunnels through the opponent. It is enforced by clamping angular
    /// rate against the limb's TIP RADIUS, which means long arms are forced to
    /// swing slower - physically right, and free.
    ///
    /// 0.25 rather than 0.12. At 0.12 the cap bound EVERY arm, and because
    /// I ~ m.R^2 while maxRate ~ 1/R, the stored energy came out as
    /// E ~ 1/2.m.cap^2/dt^2 - independent of R. That silently destroyed the
    /// whole "a longer arm hits harder" tradeoff this system exists to create.
    /// At 0.25 short arms bind on the DESIGN ceiling (fast, low inertia) and
    /// long arms bind on displacement (slower, much higher inertia), so length
    /// buys energy again. Known limit: a part thinner than
    /// STEP_DISP_CAP - trigger inflation can still be stepped over by a
    /// full-speed limb.</summary>
    public static float STEP_DISP_CAP = 0.25f;
    /// <summary>Fraction of stored energy spent per bite (SpinnerWeapon parity).</summary>
    public static float DRAIN_FRAC = 0.4f;
    /// <summary>Below this the limb just clonks. J. An absolute floor.</summary>
    public static float MIN_BITE_E = 40f;
    /// <summary>A limb must reach this fraction of its OWN top rate before a
    /// contact counts as a hit.
    ///
    /// MEASURED WHY. The first end-to-end test dealt 32.4 damage over twelve
    /// seconds of continuous hammering - about 3 per swing against an expected
    /// 41. The target was sitting inside the arc at travel 0, so the blade
    /// touched it while barely moving, took a bite worth almost nothing, and
    /// paid the stall (rate *= sqrt(1 - DRAIN_FRAC)) that a real bite costs.
    /// Every cycle died in its first few degrees.
    ///
    /// Below the gate a contact now does NOTHING AT ALL - no damage, no drain,
    /// and it does not burn the per-victim cooldown either. The limb
    /// accelerates through and lands one honest hit at speed. That is also the
    /// right feel: you cannot swing a hammer through something already resting
    /// against it, you have to wind up first.</summary>
    public static float BITE_RATE_FRAC = 0.45f;
    /// <summary>Per-victim-collider re-bite gate, s. Matches DamageResolver.PART_IMMUNITY.</summary>
    public static float HIT_COOLDOWN = 0.5f;
    /// <summary>Dead time after a completed cycle, s.
    ///
    /// ROUND-2-DEV: 0.35 -> 0.10. The cycle now contains an EXPLICIT wind-up
    /// phase (Phase.Winding) whose length is physics, not taste: Emax/motorW,
    /// which is 0.25 s on the b10 Aluminium hammer and 1.15 s on the b11
    /// Titanium one. Before the latch existed, RECOVER_S was the only thing in
    /// the cycle representing "the weapon is not ready yet". Now that the real
    /// charge time is modelled, charging the player 0.35 s again on top is
    /// double-counting. 0.10 s = 5 fixed steps, enough for the latch to reset
    /// and for the arm to settle at rest, and it keeps the cycle from
    /// degenerating into a continuous flail if a limb's wind-up is near-zero.</summary>
    public static float RECOVER_S = 0.10f;
    /// <summary>Cap on the CLOSING-SPEED term added to a limb bite, N.s.
    /// Left at 600 this round on purpose - see the round-2 dev report. It is a
    /// flat term that dilutes the energy ratio between two limbs, but the
    /// dominant cause of "energy does not convert" was that the limb never
    /// carried its stored energy at all (see WIND_RELEASE_FRAC), and changing
    /// both at once would have destroyed the attribution of either.</summary>
    public static float LIN_IMP_CAP = 600f;
    /// <summary>ROUND-2-DEV FIX (critic MAJOR "stored limb energy still does
    /// not convert into damage" + MAJOR "the ram is the worst weapon in the
    /// game by a factor of five per hit" + CRITICAL "the Phase 4 kit is
    /// dominated by the legacy disc").
    ///
    /// THE MECHANISM, DERIVED FROM THIS FILE, NOT GUESSED. The motor used to
    /// integrate THROUGH the sweep: the arm started moving on the first frame
    /// of the trigger and accelerated as it travelled. With w' = sqrt(2P.t/I)
    /// and theta = (2/3).sqrt(2P/I).t^1.5, eliminating t gives the energy the
    /// limb carries WHEN IT ARRIVES AT ANGLE theta:
    ///        E(theta) = 0.5 . (3.P.theta)^(2/3) . I^(1/3)
    /// Read that carefully: it depends on MOTOR POWER and on I^(1/3), and the
    /// limb's stored-energy ceiling 0.5.I.MaxRate^2 DOES NOT APPEAR. An
    /// opponent standing anywhere except the far end of the arc was hit with
    /// motor power, never with the weapon the player built. That is exactly
    /// the observed law: 2.88x stored energy bought 1.21x damage, i.e. damage
    /// ~ E^0.30, and 1/3 is what this formula predicts.
    ///
    /// THE FIX. A Pivot and a Ram now LATCH: the motor winds the limb up in
    /// place (Phase.Winding, travel frozen at rest) and the arm is released
    /// only once it holds its full stored energy. So a swing carries
    /// 0.5.I.MaxRate^2 - the number the build screen has been quoting as
    /// "J a hit" all along, which until now was a fiction worth ~5x its real
    /// value. This also fixes the ram with no ram-specific damage model: a
    /// 0.45 m stroke never had room to accelerate into its target, which is
    /// why 2012 J stored landed 10.8 damage.
    ///
    /// WHY 0.995 AND NOT 1.0. rate is clamped by Mathf.Min(MaxRate, ...) so it
    /// does reach MaxRate exactly, but leaving no float slack risks a limb
    /// that sits latched for an extra step every cycle. 0.995 of the rate is
    /// 0.990 of the energy - unmeasurable - and releases deterministically.
    ///
    /// A Spindle is NOT latched. It is a flywheel that is meant to be held at
    /// speed; it has no arc and no recovery, and the critic explicitly asked
    /// that it stop being gated by an arc-cycle rule.</summary>
    public static float WIND_RELEASE_FRAC = 0.995f;
    /// <summary>Hard ceiling on how long a limb may stay latched, s.
    ///
    /// WHY IT EXISTS. Wind time is Emax/(motorKW.supplyFrac), so a machine in
    /// a brownout could latch forever and never swing, which reads as a broken
    /// weapon rather than a flat battery. WHY 2.0: the heaviest limb any legal
    /// 4000 cr build has produced in this project is the b11 Titanium hammer
    /// at 2875 J, which is 1.15 s at the 2.5 kW single-engine motor. 2.0 s
    /// covers that with 74% headroom and still fires roughly every other
    /// second at half supply.</summary>
    public static float WIND_TIMEOUT_S = 2.0f;
    /// <summary>ROUND-2-DEV, SECOND PATCH. Fraction of the limb's RATE that the
    /// return spring catches and hands back to the next wind-up.
    ///
    /// WHY THIS HAD TO EXIST THE MOMENT THE LATCH DID. MEASURED. With the latch
    /// alone, b10 went W4/L0 dealt 501 -> W1/L2/D1 dealt 320 even though damage
    /// PER BITE rose 1.63x exactly as predicted, because bite COUNT fell 61->20.
    /// Live probe at t=26.8 s: drawKW 6.8 of a 300 kJ pack over a 90 s match.
    /// The reason is one pre-existing line: on reaching the end of its arc the
    /// limb does `rate = 0f`, i.e. it DELETES its entire kinetic energy, hit or
    /// miss. That cost nothing when the limb only ever reached ~20% of its
    /// ceiling; the moment a swing genuinely carries 998 J it is a 926 W
    /// mechanical / 2.65 kW electrical bill paid every single cycle for
    /// swinging at thin air, and it flattens the pack, which kills the drive
    /// as well as the weapon.
    ///
    /// PHYSICALLY, where was that energy going? Into the return spring - the
    /// same spring PIVOT_RETURN_DPS already models pushing the arm back. A
    /// spring that can reverse a moving arm must first absorb it. That was
    /// simply never accounted; the energy was silently discarded. Now the arm
    /// is caught, most of its speed is parked, and the next wind starts from
    /// there instead of from zero.
    ///
    /// WHY 0.90 ON RATE (= 0.81 ON ENERGY). Two constraints bracket it. It has
    /// to be well below 1.0 or a limb becomes a perpetual flywheel and the
    /// wind-up tradeoff disappears. It has to be well above EFFICIENCY (0.35),
    /// or recharging through the spring would be dearer than recharging through
    /// the motor and the mechanism would be pointless. 0.81 energy return is
    /// also an honest number for a steel spring-and-latch. The residual 19% is
    /// 190 J a cycle - about 0.50 kW, comfortably inside a one-engine machine -
    /// so a MISS is cheap and a HIT is what costs: a bite still spends
    /// DRAIN_FRAC of the swing outright and that energy is gone before the
    /// spring ever sees it. "You pay for what you land" is the rule this
    /// produces, and it is the right one.
    ///
    /// Energy dumped into the ARENA is NOT recovered - see the GroundBlocked
    /// branch, which zeroes the store as well as the rate.</summary>
    public static float RECUP_FRAC = 0.90f;
    /// <summary>ROUND-2-DEV, SECOND PATCH. Cap on the chassis reaction torque,
    /// as a multiple of the motor's torque at its RATED rate.
    ///
    /// ApplyReaction computes force = watts / max(rate, 0.05), which is the
    /// right formula (torque = power / omega) attached to the wrong assumption:
    /// as rate goes to zero it diverges. At the first step of a wind-up, a
    /// 2.5 kW motor at rate 0.05 produced 50 000 N.m - a 600 N.m.s impulse into
    /// the player's own chassis, every cycle. It was survivable while the limb
    /// started moving immediately; with a latched wind-up the arm sits at rate
    /// ~0 for several steps and it became the largest single force on the
    /// machine. Measured symptom: player self-flip time went 1.7 s -> 4.4 s a
    /// match under patch 1.
    ///
    /// Real motors have a finite STALL torque, typically 2-3x rated. 3.0 is the
    /// generous end of that, so this only clips the divergence and leaves the
    /// normal operating range untouched.</summary>
    public static float REACTION_STALL_MUL = 3.0f;
    /// <summary>ROUND-2-DEV FIX (critic MAJOR "limb bites almost never
    /// register"). Absolute EDGE SPEED, m/s, at which a contact becomes a
    /// strike. Supersedes BITE_RATE_FRAC as the primary gate.
    ///
    /// WHY THE FRACTION RULE WAS BACKWARDS. BITE_RATE_FRAC gates on a fraction
    /// of the limb's OWN ceiling, and that ceiling is the material's tip-speed
    /// cap (TipSpeedCap). Live values: Aluminium 9.68 m/s, Titanium 14.79.
    /// So the Aluminium arm had to reach 4.36 m/s at the edge to score, and
    /// the BETTER Titanium arm had to reach 6.66 m/s - the good material was
    /// punished with a 53% higher bar, on top of taking longer to get there.
    /// That is a perverse gradient sitting directly under the finding that
    /// better materials do not pay.
    ///
    /// WHY 4.0 m/s. It is 0.45 x TIP_SPEED_MIN (9.0), i.e. EXACTLY the gate the
    /// old rule already imposed on the weakest possible limb. So this rule is
    /// never stricter than the shipped one for any limb, and strictly looser
    /// for every strong one - it cannot regress the wind-up bug the 0.45 was
    /// introduced to fix (a blade resting against a target at rate 0 is still
    /// nowhere near 4 m/s), and MIN_BITE_E still floors the energy underneath.
    ///
    /// The Ram is protected by the min() in the gate itself: its ceiling is
    /// 3.5 m/s, below 4.0, so a flat 4.0 would have made the ram unable to
    /// bite AT ALL. It keeps its old 0.45 x 3.5 = 1.575 m/s gate.</summary>
    public static float BITE_TIP_SPEED_MS = 4.0f;
    /// <summary>Share of the motor's reaction torque fed back into the chassis.
    /// This is what makes a big hammer rock its own robot when it swings, which
    /// is both honest and the main reason a heavy weapon needs a wide stance.</summary>
    public static float REACTION_FRAC = 0.6f;
    /// <summary>Bearing/air loss, 1/s. Only the Spindle holds speed, so only the
    /// Spindle pays this continuously.</summary>
    public static float SPIN_DRAG = 0.02f;
    /// <summary>Efficiency of turning stored kJ into limb kinetic energy.</summary>
    public static float EFFICIENCY = 0.35f;
    /// <summary>ROUND-1 IMPL FIX (critic CRITICAL 3). Moment arm, m, that a
    /// lift-biased edge (the wedge) is credited with when it converts part of
    /// its shove into an ANGULAR impulse on the victim.
    ///
    /// WHY AN ANGULAR TERM AT ALL. liftBias existed but only ever produced a
    /// pure upward push, which Unity's maxDepenetrationVelocity = 4 damps out
    /// almost immediately: measured across 58 matches exactly ONE ended by a
    /// flip, and the dedicated flipper build went 0W/10L having toppled nothing
    /// in ten matches. Getting under something and rolling it over is an
    /// ANGULAR event about the contact line - a wedge does not launch you, it
    /// tips you - so the impulse has to have a torque component or the verb
    /// cannot work no matter how big the number is.
    ///
    /// WHY 0.45. It is the moment arm "under one end of the machine": the
    /// shared roster skeleton puts its wheel line 0.40 m from the core and a
    /// wheel is 0.07 m half-width, so 0.45 m is the far axle. Angular impulse
    /// L = shove * lift * 0.45. With the existing DamageResolver.SHOVE_CAP of
    /// 500 N.s and the wedge's liftBias 0.75 that caps at 169 N.m.s, which on
    /// a ~700 kg skeleton bot (I ~ 70 kg.m2 about its long axis) is ~2.4 rad/s
    /// of roll - enough to go over, not enough to launch. It is capped by the
    /// SAME already-capped shove, so it can never exceed what the linear term
    /// was already allowed to spend.</summary>
    /// SUPERSEDED by ApplyTopple (round-3 critic CRITICAL 3). The critic
    /// showed this whole formulation was 3x short even at DamageResolver's
    /// SHOVE_CAP and 11x short at a real weapon's actual drain, because the
    /// angular impulse was derived from a CAPPED LINEAR IMPULSE and therefore
    /// could never exceed 500 * 0.75 * 0.45 = 169 N.m.s no matter how much
    /// energy the limb had stored. Toppling is an energy problem, so it is now
    /// solved as one. Kept because the value is still the honest "moment arm
    /// under one end of the machine" and other code and comments cite it.
    public static float TIP_ARM_M = 0.45f;

    // ------------------------------------------------------- the ground guard
    /// <summary>ROUND-2-CRITIC FIX (CRITICAL 2: "firing your own actuator flips
    /// your own robot - 28x more time on its back, single-variable").
    ///
    /// MEASURED ROOT CAUSE, AND IT IS NOT THE ONE THE FINDING PROPOSED. The
    /// critic's fix_direction named the reaction torque and the topple term, so
    /// that is what I tested first, single-variable, with no recompile:
    /// REACTION_FRAC 0.6 -> 0 at runtime, re-running the critic's own scenario
    /// (qa_r2c_c1_lowflip_ti vs tipper/Rookie, charge, fire ON). Player
    /// flippedTime came out 53.6 s and 46.3 s in the two full-length matches,
    /// against 54.4/57.2/52.8 s with the reaction at full strength. Deleting
    /// the reaction torque entirely does not fix it. It is not the cause.
    ///
    /// What flips the machine is the limb's SOLID collider being POSED THROUGH
    /// THE DECK. ApplyPose teleports limb colliders; Unity then depenetrates
    /// them, and a wedge driven into a static floor at 13 rad/s levers the
    /// whole chassis about exactly the pivot axis. Sampled live at
    /// timeScale 0.15 on the critic's own build: at t=0.10 s travel is
    /// 0.220 rad and the wedge collider's bounds.min.y is ALREADY -0.014, i.e.
    /// below the floor, with chassis angular velocity -1.04 rad/s about the
    /// pivot axis; by t=1.74 s dot(up) is -0.733 and the machine is on its
    /// back. There is no opponent contact anywhere in that window.
    ///
    /// WHY THE BUILD DOES THAT AT ALL. A pivot's arc direction is decided by
    /// which FACE the player mounted it on - ApplyPose rotates by +travel about
    /// the mount normal, and a rotation about +X maps +Z to -Y - so a flipper
    /// bolted to the +X face scoops DOWNWARD into the deck while the identical
    /// build on the -X face scoops upward. Nothing in the builder says so; that
    /// is the same undiscoverability the critic filed as CRITICAL 4.
    ///
    /// TWO PARTS, both here:
    ///   1. PickArcSign - at wire time, if the built arc digs into the deck
    ///      inside its first ARC_EARLY_DEG and the other direction does not,
    ///      the arc is REVERSED. A flipper now flips whichever face it is
    ///      bolted to, which is what the player thought they built.
    ///   2. GroundBlocked - whichever way it ends up going, the swing STOPS at
    ///      the deck instead of being driven through it. That is what a real
    ///      arm does when it hits the floor, and it is the guarantee: the arc
    ///      picker is a heuristic evaluated once on the spawn pose, this runs
    ///      against the live world every step and is also right when the robot
    ///      is tilted, airborne or already on its side.
    ///
    /// 0.02 m of clearance: one Unity default contact offset (0.01) plus the
    /// same again, so the guard trips BEFORE the solver has a penetration to
    /// resolve rather than after.</summary>
    public static float GROUND_CLEAR = 0.02f;
    /// <summary>Degrees of arc that count as "immediately" for the arc picker.
    /// An arc that only reaches the deck later than this has already swept
    /// through the target zone and has done its job - the runtime guard stops
    /// it at the floor and nothing is lost. Only an arc that digs in before it
    /// has done anything is worth reversing, and reversing more than that would
    /// silently turn an overhead hammer around. 45 deg is under a third of
    /// PIVOT_ARC_DEG (150).</summary>
    public static float ARC_EARLY_DEG = 45f;
    /// <summary>+1 or -1, chosen once by PickArcSign. Multiplies `travel`
    /// everywhere the pose is built, so every rate/energy/inertia number in the
    /// class is untouched by it.</summary>
    public float arcSign = 1f;

    // ------------------------------------------------------------------ wiring
    public CompoundRobot robot;
    public PowerPlant power;
    public int partIdx;
    public ActuatorKind kind;
    /// <summary>Part indices this actuator drives. Empty is legal and means the
    /// player bolted a bare actuator to the frame - it does nothing, and the
    /// log says so.</summary>
    public int[] limb = new int[0];
    /// <summary>Live motor rating, kW - MotorKW(live engines), refreshed every
    /// Recompute() so shearing an engine off slows the weapon immediately.</summary>
    public float motorKW = 2.5f;
    /// <summary>Live tip-speed ceiling, m/s, from the WEAKEST material on the
    /// limb: rim stress fails at the weakest link, and it stops a player from
    /// hanging a Titanium tip off an ABS arm to buy speed.</summary>
    public float tipSpeedCap = 12.5f;
    // Swept-trigger bookkeeping, one entry per limb member (see UpdateSweptVolumes).
    BoxCollider[] limbTrig;
    Vector3[] limbSweep;     // signed unit LOCAL basis axis the part travels along
    float[] limbRadius;      // radius about the actuator axis, m

    Vector3 axisLocal = Vector3.right;   // hinge axis (Pivot/Spindle) or stroke dir (Ram)
    Vector3 pivotLocal;                  // hinge centre, robot-local
    Vector3[] restPos;
    Quaternion[] restRot;

    // ------------------------------------------------------------------- state
    /// <summary>ROUND-2-DEV: Winding is new - see WIND_RELEASE_FRAC. A Pivot
    /// or Ram charges in place before the arm is released; a Spindle skips it.</summary>
    public enum Phase { Ready, Winding, Driving, Returning, Recovering }
    public Phase phase = Phase.Ready;
    /// <summary>Radians (Pivot/Spindle) or metres (Ram).</summary>
    public float travel;
    /// <summary>rad/s or m/s.</summary>
    public float rate;

    // P4c (2026-07-29): moving-target balance-pass counters, summed over ALL
    // actuators, reset by the measurement harness.
    public static int statBites;
    public static float statBiteDmg;
    // R2-CRITIC FIX (finding 2): the critic could not tell WHOSE limb was
    // landing — the tipper's flipper and the player's hammer fed the same
    // totals. Split by playerControlled. Units are RAW energy fed to
    // ApplyHit (pre DMG_K 0.045): 12,480 raw ≈ 561 HP — the "15x
    // disconnect" the critic flagged was these units, not a damage bug.
    public static int statBitesPlayer, statBitesAI;
    public static float statBiteDmgPlayer, statBiteDmgAI;
    /// <summary>kg.m^2 about the axis (Pivot/Spindle) or kg (Ram). Recomputed
    /// live - a limb part sheared off mid-swing must change the weapon.</summary>
    public float inertia = 0.001f;
    /// <summary>Distance from the axis to the outermost limb surface, m.</summary>
    public float tipRadius = 0.001f;
    float recoverT;
    /// <summary>Seconds spent latched in Phase.Winding on this cycle.</summary>
    float windT;
    /// <summary>Rate (rad/s or m/s) parked by the return spring at the end of
    /// the last stroke, handed back to the next wind-up. See RECUP_FRAC.</summary>
    float springStore;

    readonly Dictionary<Collider, float> lastBite = new Dictionary<Collider, float>();

    /// <summary>Is this component still pointing at a live part list?
    ///
    /// A domain reload (any .cs edit while the game is playing) rebuilds the
    /// managed heap but keeps the scene: the GameObjects and this MonoBehaviour
    /// survive, while CompoundRobot.parts - a plain non-serialised List - comes
    /// back EMPTY. Every robot.parts[partIdx] then throws, once per FixedUpdate
    /// and once per trigger callback, which floods the console hard enough to
    /// stall the editor. Found exactly that way. Nothing here can be repaired
    /// from stale state, so the honest move is to notice and switch off.</summary>
    bool Wired
    {
        get
        {
            return robot != null && robot.parts != null
                && partIdx >= 0 && partIdx < robot.parts.Count;
        }
    }

    bool LimbLive(int i)
    {
        return robot != null && robot.parts != null && i >= 0 && i < robot.parts.Count
            && !robot.parts[i].detached && robot.parts[i].go != null;
    }

    /// <summary>Kinetic energy currently stored in the limb, J.</summary>
    public float Energy { get { return 0.5f * inertia * rate * rate; } }
    public bool Rotary { get { return kind != ActuatorKind.Ram; } }
    public bool HasLimb { get { return limb != null && limb.Length > 0; } }

    /// <summary>Rate ceiling. For the rotary kinds this is the LOWER of the
    /// kind's design speed and whatever keeps the tip inside STEP_DISP_CAP.</summary>
    public float MaxRate
    {
        get
        {
            float dt = Mathf.Max(Time.fixedDeltaTime, 1e-4f);
            if (kind == ActuatorKind.Ram) return Mathf.Min(3.5f, STEP_DISP_CAP / dt);
            // Round-3-critic CRITICAL 2: the ceiling is the limb material's tip
            // speed, not a fixed displacement per fixed step. See TIP_SPEED_BASE.
            float safe = tipSpeedCap / Mathf.Max(tipRadius, 0.05f);
            float design = kind == ActuatorKind.Spindle
                         ? SPINDLE_MAX_RPM * Mathf.PI * 2f / 60f
                         : 14f;                       // rad/s, ~800 deg/s pivot ceiling
            return Mathf.Min(design, safe);
        }
    }

    float Limit { get { return kind == ActuatorKind.Ram ? RAM_STROKE_M : PIVOT_ARC_DEG * Mathf.Deg2Rad; } }

    // ================================================================ lifecycle

    /// <summary>Called by Wire() once the limb membership is known.</summary>
    public void Init(CompoundRobot owner, int idx, ActuatorKind k, Vector3 axis, int[] limbIdx)
    {
        robot = owner;
        partIdx = idx;
        kind = k;
        limb = limbIdx != null ? limbIdx : new int[0];
        axisLocal = axis.sqrMagnitude < 0.01f ? Vector3.right : axis.normalized;
        pivotLocal = owner.parts[idx].go.transform.localPosition;

        restPos = new Vector3[limb.Length];
        restRot = new Quaternion[limb.Length];
        for (int i = 0; i < limb.Length; i++)
        {
            var t = owner.parts[limb[i]].go.transform;
            restPos[i] = t.localPosition;
            restRot[i] = t.localRotation;
        }
        Recompute();

        // Round-2-critic CRITICAL 2: which way does this arc swing? Must be
        // decided BEFORE the swept-trigger basis below, which is built from it.
        arcSign = PickArcSign();

        // Hit volumes. Every limb part gets a trigger that reports back here, so
        // damage is delivered by the energy model rather than by contact
        // impulses off a teleported collider - the same reason SpinnerWeapon
        // uses a trigger instead of its solid box.
        limbTrig = new BoxCollider[limb.Length];
        limbSweep = new Vector3[limb.Length];
        limbRadius = new float[limb.Length];
        for (int i = 0; i < limb.Length; i++)
        {
            var go = owner.parts[limb[i]].go;
            var trig = go.AddComponent<BoxCollider>();
            trig.isTrigger = true;
            // Inflated more than the disc's 0.08: a limb tip moves up to
            // STEP_DISP_CAP per step, and the hit volume has to be thick enough
            // that a step cannot jump clean over the thing it is swinging at.
            trig.size = owner.parts[limb[i]].spec.size + Vector3.one * 0.10f;
            go.AddComponent<LimbEdge>().Bind(this, limb[i]);
            limbTrig[i] = trig;

            // Round-3-critic CRITICAL 2. Which way does THIS part travel, in
            // its own local frame? For a rotary limb that is axis x radius; for
            // a ram it is the stroke axis. The part rotates WITH the limb, so
            // this direction is fixed in part-local space for the whole sweep
            // and only has to be computed once. Snapped to the dominant basis
            // axis because a BoxCollider can only be grown along its own axes.
            Vector3 r = restPos[i] - pivotLocal;
            Vector3 tang = Rotary ? Vector3.Cross(axisLocal * arcSign, r) : axisLocal;
            if (tang.sqrMagnitude < 1e-6f) tang = Vector3.forward;
            Vector3 lp = (Quaternion.Inverse(restRot[i]) * tang.normalized);
            float ax = Mathf.Abs(lp.x), ay = Mathf.Abs(lp.y), az = Mathf.Abs(lp.z);
            if (ax >= ay && ax >= az)      limbSweep[i] = new Vector3(Mathf.Sign(lp.x), 0f, 0f);
            else if (ay >= az)             limbSweep[i] = new Vector3(0f, Mathf.Sign(lp.y), 0f);
            else                           limbSweep[i] = new Vector3(0f, 0f, Mathf.Sign(lp.z));
            limbRadius[i] = Rotary ? Vector3.ProjectOnPlane(r, axisLocal).magnitude : 1f;
        }
    }

    /// <summary>Inertia and tip radius for a limb described as plain arrays.
    /// The BUILDER calls this to quote a weapon's energy per swing before the
    /// player commits, and Recompute() calls it for the live limb, so the
    /// number on the build screen is the number the fight uses. `pos` is
    /// relative to the actuator, `half` is each part's largest half-extent.</summary>
    public static void LimbMaths(bool rotary, Vector3 axis, Vector3[] pos, float[] mass,
                                 float[] half, out float inertia, out float tipRadius)
    {
        float I = 0f, tip = 0.001f;
        for (int i = 0; i < pos.Length; i++)
        {
            if (rotary)
            {
                float r = Vector3.ProjectOnPlane(pos[i], axis).magnitude;
                I += mass[i] * (r * r + half[i] * half[i] * 0.33f);
                tip = Mathf.Max(tip, r + half[i]);
            }
            else
            {
                I += mass[i];
                tip = Mathf.Max(tip, half[i]);
            }
        }
        inertia = Mathf.Max(I, 0.001f);
        tipRadius = tip;
    }

    /// <summary>Rate ceiling for a limb of the given tip radius - the static
    /// half of MaxRate, so the builder can quote it too.</summary>
    /// <summary>tipSpeedCapMs &lt;= 0 means "assume a Steel-grade edge", which is
    /// TIP_SPEED_BASE and reproduces the pre-round-3 number exactly.</summary>
    public static float RateCeiling(ActuatorKind kind, float tipRadius, float tipSpeedCapMs)
    {
        float dt = Mathf.Max(Time.fixedDeltaTime, 1e-4f);
        if (kind == ActuatorKind.Ram) return Mathf.Min(3.5f, STEP_DISP_CAP / dt);
        if (tipSpeedCapMs <= 0f) tipSpeedCapMs = TIP_SPEED_BASE;
        float safe = tipSpeedCapMs / Mathf.Max(tipRadius, 0.05f);
        float design = kind == ActuatorKind.Spindle ? SPINDLE_MAX_RPM * Mathf.PI * 2f / 60f : 14f;
        return Mathf.Min(design, safe);
    }
    public static float RateCeiling(ActuatorKind kind, float tipRadius)
    {
        return RateCeiling(kind, tipRadius, TIP_SPEED_BASE);
    }

    /// <summary>Seconds for one full Pivot/Ram cycle at these stats: wind-up,
    /// sweep, spring return, recover. Used for the endurance estimate.</summary>
    public static float CycleSeconds(ActuatorKind kind, float inertia, float tipRadius)
    {
        return CycleSeconds(kind, inertia, tipRadius, MOTOR_KW, TIP_SPEED_BASE);
    }
    /// <summary>Round-3-critic CRITICAL 2: wind-up now depends on the motor the
    /// player fitted, so the builder's endurance quote has to be told which
    /// motor and which edge material it is quoting.</summary>
    public static float CycleSeconds(ActuatorKind kind, float inertia, float tipRadius,
                                     float motorKWin, float tipSpeedCapMs)
    {
        if (kind == ActuatorKind.Spindle) return 1f;
        float rateMax = RateCeiling(kind, tipRadius, tipSpeedCapMs);
        float energy = 0.5f * inertia * rateMax * rateMax;
        float windUp = energy / Mathf.Max(motorKWin * 1000f, 1f);
        float limit = kind == ActuatorKind.Ram ? RAM_STROKE_M : PIVOT_ARC_DEG * Mathf.Deg2Rad;
        float sweep = limit / Mathf.Max(rateMax, 0.01f);
        float back = limit / Mathf.Max(kind == ActuatorKind.Ram ? RAM_RETURN_MPS
                                       : PIVOT_RETURN_DPS * Mathf.Deg2Rad, 0.01f);
        return Mathf.Max(0.2f, windUp + sweep + back + RECOVER_S);
    }

    public static ActuatorKind KindOfId(string id) { return KindOf(id); }

    /// <summary>Re-derive inertia and tip radius from the parts that are STILL
    /// attached. Cheap, and it must not be cached at spawn: shearing the head
    /// off a hammer has to leave the player holding a stick.</summary>
    public void Recompute()
    {
        if (!Wired) { inertia = 0.001f; tipRadius = 0.001f; return; }
        int n = 0;
        if (limb != null)
            for (int i = 0; i < limb.Length; i++)
                if (LimbLive(limb[i])) n++;
        var pos = new Vector3[n];
        var mass = new float[n];
        var half = new float[n];
        int k = 0;
        if (limb != null)
            for (int i = 0; i < limb.Length; i++)
            {
                if (!LimbLive(limb[i])) continue;
                var p = robot.parts[limb[i]];
                pos[k] = p.go.transform.localPosition - pivotLocal;
                mass[k] = p.mass;
                half[k] = Mathf.Max(p.spec.size.x, Mathf.Max(p.spec.size.y, p.spec.size.z)) * 0.5f;
                k++;
            }
        LimbMaths(Rotary, axisLocal, pos, mass, half, out inertia, out tipRadius);

        // Round-3-critic CRITICAL 2. Both of these are re-derived from LIVE
        // parts every Recompute for the same reason inertia is: shearing the
        // engines off has to slow the weapon, and shearing the Titanium tip off
        // an Aluminium arm has to drop its speed ceiling to the Aluminium one.
        int engines = 0;
        if (robot != null && robot.parts != null)
            for (int i = 0; i < robot.parts.Count; i++)
                if (!robot.parts[i].detached && robot.parts[i].spec.id.StartsWith("engine"))
                    engines++;
        motorKW = MotorKW(engines);

        float weakest = -1f;
        if (limb != null)
            for (int i = 0; i < limb.Length; i++)
            {
                if (!LimbLive(limb[i])) continue;
                float s = MatDB.Get(robot.parts[limb[i]].spec.mat).strengthRel;
                if (weakest < 0f || s < weakest) weakest = s;
            }
        tipSpeedCap = weakest < 0f ? TIP_SPEED_BASE : TipSpeedCap(weakest);
    }

    // ================================================================== driving

    void FixedUpdate()
    {
        if (!Wired) { enabled = false; return; }
        if (power == null) power = robot.GetComponent<PowerPlant>();
        float dt = Time.fixedDeltaTime;

        bool live = robot.combatEnabled && !robot.dead
                    && !robot.parts[partIdx].detached && HasLimb;

        Recompute();

        if (!live)
        {
            // A dead or pre-bell actuator relaxes to rest. Nothing is drawn and
            // nothing bites.
            rate = 0f;
            springStore = 0f;          // a dead or pre-bell weapon holds nothing
            if (kind != ActuatorKind.Spindle)
                travel = Mathf.MoveTowards(travel, 0f, ReturnRate() * dt);
            ApplyPose();
            return;
        }

        bool want = Fire();

        switch (phase)
        {
            case Phase.Ready:
                // ROUND-2-DEV: a Pivot/Ram charges before it moves. A Spindle
                // is a flywheel and goes straight to Driving.
                if (want)
                {
                    phase = kind == ActuatorKind.Spindle ? Phase.Driving : Phase.Winding;
                    windT = 0f;
                    // The return spring hands back what it caught. The motor
                    // block below only DRAWS on the increase, so this top-up is
                    // free - which is the whole point.
                    rate = Mathf.Max(rate, springStore);
                    springStore = 0f;
                }
                else if (kind != ActuatorKind.Spindle)
                    travel = Mathf.MoveTowards(travel, 0f, ReturnRate() * dt);
                break;

            case Phase.Winding:
                // The arm is latched at rest and the motor is pouring energy
                // into `rate` (the block below runs for Winding too). Release
                // at the ceiling, or on the timeout, or the moment the trigger
                // comes up - releasing on trigger-up is deliberate: it means a
                // wind is never WASTED, so a player who taps gets a weak swing
                // rather than nothing, and an AI whose target leaves the firing
                // arc mid-charge still throws the punch it paid for.
                windT += dt;
                if (!want || windT >= WIND_TIMEOUT_S
                          || rate >= WIND_RELEASE_FRAC * MaxRate)
                    phase = Phase.Driving;
                break;

            case Phase.Driving:
                // A Spindle is HELD: release the trigger and it coasts down.
                if (kind == ActuatorKind.Spindle && !want) phase = Phase.Returning;
                break;

            case Phase.Returning:
                if (kind == ActuatorKind.Spindle)
                {
                    if (want) phase = Phase.Driving;
                    else if (rate <= 0.01f) phase = Phase.Ready;
                }
                else
                {
                    travel = Mathf.MoveTowards(travel, 0f, ReturnRate() * dt);
                    rate = 0f;
                    if (travel <= 0.0001f) { phase = Phase.Recovering; recoverT = RECOVER_S; }
                }
                break;

            case Phase.Recovering:
                recoverT -= dt;
                if (recoverT <= 0f) phase = Phase.Ready;
                break;
        }

        // ---- passive loss, always, powered or not (SpinnerWeapon parity: this
        //      is what makes HOLDING a disc at speed cost money).
        if (kind == ActuatorKind.Spindle)
            rate = Mathf.Max(0f, rate - SPIN_DRAG * rate * dt);

        // ---- the motor, power-limited. dE = P.dt integrated through
        //      E = 1/2.I.w^2 gives w' = sqrt(w^2 + 2.P.dt/I) exactly, so a
        //      heavier limb costs proportionally longer for every rad/s. This is
        //      the line that turns "what did you bolt on" into "how does it
        //      swing".
        // ROUND-2-DEV: Winding spends exactly the same watts, draws the same
        // power and applies the same chassis reaction as Driving - the only
        // difference is that `travel` is frozen, so the energy ends up in the
        // limb instead of being spread thinly across the arc.
        if (phase == Phase.Driving || phase == Phase.Winding)
        {
            // Round-3-critic CRITICAL 2: motorKW, not the old flat MOTOR_KW.
            float pW = motorKW * 1000f * (power != null ? power.supplyFrac : 1f);
            float before = rate;
            rate = Mathf.Min(MaxRate, Mathf.Sqrt(Mathf.Max(0f,
                       before * before + 2f * pW * dt / Mathf.Max(inertia, 1e-4f))));
            if (rate > before)
            {
                float dE = 0.5f * inertia * (rate * rate - before * before);
                if (power != null)
                    power.Draw(dE / Mathf.Max(0.01f, EFFICIENCY) / 1000f / Mathf.Max(dt, 1e-5f));
                // Newton's third law, and the reason a heavy weapon wants a wide
                // stance: the torque that winds the limb up rocks the chassis the
                // other way.
                ApplyReaction(dE / Mathf.Max(dt, 1e-5f));
            }

            // ROUND-2-DEV: a latched limb does not travel. Everything below is
            // the SWEEP, and only Driving sweeps.
            if (phase != Phase.Driving) { ApplyPose(); return; }

            // Round-2-critic CRITICAL 2: the swing STOPS at the deck. Posing a
            // solid collider through a static floor is what was flipping the
            // player's own machine - see GROUND_CLEAR for the measurement.
            float next = travel + rate * dt;
            if (GroundBlocked(next))
            {
                // The arm hit the floor. That energy went into the arena, not
                // back into the chassis, so it is killed rather than handed to
                // the body - handing it back would be a second way to lever the
                // machine over, which is the bug this is fixing.
                rate = 0f;
                springStore = 0f;      // that energy went into the arena floor
                phase = Phase.Returning;
            }
            else
            {
                travel = next;
                if (kind != ActuatorKind.Spindle && travel >= Limit)
                {
                    travel = Limit;
                    // ROUND-2-DEV: the return spring CATCHES the arm rather
                    // than the arm's energy vanishing. See RECUP_FRAC for the
                    // measurement that made this necessary. Whatever a bite
                    // already drained is long gone by here, so a swing that
                    // connected recharges from a lower rate than one that
                    // whiffed - you pay for what you land.
                    springStore = rate * RECUP_FRAC;
                    phase = Phase.Returning;
                }
            }
        }

        ApplyPose();
    }

    float ReturnRate()
    {
        if (kind == ActuatorKind.Ram) return RAM_RETURN_MPS;
        // ROUND-2-DEV: PIVOT_RETURN_DPS went 200 -> 500 to give the cycle its
        // dead time back. The return stroke does not update the swept trigger
        // volume (rate is 0 through Returning, so UpdateSweptVolumes sees no
        // displacement), which means the SOLID collider must not out-run the
        // step on its own. Clamp the spring so the outermost limb surface
        // never covers more than STEP_DISP_CAP in one fixed step: a long arm
        // springs back proportionally slower, exactly as the powered stroke is
        // limited by tip speed.
        float dt = Mathf.Max(Time.fixedDeltaTime, 1e-4f);
        return Mathf.Min(PIVOT_RETURN_DPS * Mathf.Deg2Rad,
                         STEP_DISP_CAP / dt / Mathf.Max(tipRadius, 0.05f));
    }

    /// <summary>Set by BuilderManager on the player's machine only. P0
    /// (2026-08-05): no longer consulted for input routing — Fire() reads the
    /// robot's single control authority (CompoundRobot.controlSource). The
    /// flag survives purely as the SIDE TAG for the bite stats below, which
    /// also fixes a bench distortion: an AI-driven player bot now still books
    /// its bites as the player side's. Scheduled for rename/deletion once the
    /// stats key off the fight Side instead.</summary>
    public bool playerControlled;
    /// <summary>Written by AIController.</summary>
    public bool aiFire;
    bool Fire()
    {
        if (robot != null)
            return robot.controlSource == ControlSource.Keyboard ? Phase0Input.FireHeld() : aiFire;
        return playerControlled ? Phase0Input.FireHeld() : aiFire;   // ownerless: legacy routing
    }

    /// <summary>Drive every limb part's transform from `travel`. Colliders come
    /// along, so a raised hammer is genuinely in the way.</summary>
    void ApplyPose()
    {
        if (limb == null || restPos == null || !Wired) return;
        if (Rotary)
        {
            Quaternion q = Quaternion.AngleAxis(travel * arcSign * Mathf.Rad2Deg, axisLocal);
            for (int i = 0; i < limb.Length; i++)
            {
                if (!LimbLive(limb[i])) continue;
                var p = robot.parts[limb[i]];
                p.go.transform.localPosition = pivotLocal + q * (restPos[i] - pivotLocal);
                p.go.transform.localRotation = q * restRot[i];
            }
        }
        else
        {
            Vector3 d = axisLocal * travel;
            for (int i = 0; i < limb.Length; i++)
            {
                if (!LimbLive(limb[i])) continue;
                robot.parts[limb[i]].go.transform.localPosition = restPos[i] + d;
            }
        }

        // ROUND-1 IMPL FIX (critic CRITICAL 4: "centre of mass does not move
        // when a limb swings - measured delta is exactly zero"). ApplyPose is
        // the ONLY thing that moves a limb, and CompoundRobot.RecomputeMass
        // already derives rb.mass and rb.centerOfMass from
        // p.go.transform.localPosition - it was simply never invoked from here.
        // Measured by the critic on the Titanium hammer: rb.centerOfMass was
        // (0.06826, 0.10558, -0.01168) to five decimals at every one of eight
        // samples spanning a full 150 deg sweep, delta 0.000000 m, when the
        // limb (137.2 kg whose own CoM sits 0.566 m out) sweeping 1.093 m
        // SHOULD move the body CoM 0.132 m - half the machine's 0.27 m
        // half-track. Without this the whole "a heavy weapon needs a wide
        // stance" premise, and REACTION_FRAC above it, are decoration.
        //
        // Gated on an actual pose change: RecomputeMass calls
        // rb.ResetInertiaTensor(), and a parked actuator must not pay for that
        // 50x a second. NaN start forces the first pose through.
        if (robot != null && !Mathf.Approximately(travel, posedTravel))
        {
            posedTravel = travel;
            robot.RecomputeMass();
        }

        UpdateSweptVolumes();
    }

    /// <summary>ROUND-3-CRITIC FIX (CRITICAL 2). The old rate ceiling existed
    /// so that a limb collider could never move further than STEP_DISP_CAP in
    /// one fixed step and step clean over its target. Raising the ceiling to a
    /// material tip speed means that guarantee has to come from somewhere else,
    /// so it comes from here: the trigger box is stretched BACKWARDS along the
    /// direction this part just travelled, by exactly the distance it travelled,
    /// so the volume Unity tests covers the whole swept path rather than only
    /// where the part ended up.
    ///
    /// Exactly [-disp, 0] and no lookahead: growing the box symmetrically would
    /// make the limb bite things it has not reached yet, which reads as a
    /// phantom hit. BoxCollider.center carries the offset and BoxCollider.size
    /// carries the length; both are in the part's own local frame, which is why
    /// limbSweep is snapped to a basis axis in Init.
    ///
    /// This is the trigger only. The part's SOLID collider is still teleported,
    /// which is why TIP_SPEED_MAX is 18 m/s and not higher.</summary>
    void UpdateSweptVolumes()
    {
        if (limbTrig == null) return;
        float dt = Mathf.Max(Time.fixedDeltaTime, 1e-4f);
        for (int i = 0; i < limbTrig.Length; i++)
        {
            var trig = limbTrig[i];
            if (trig == null) continue;
            float disp = Mathf.Abs(rate) * dt * (Rotary ? limbRadius[i] : 1f);
            // Below one trigger-inflation thickness the base 0.10 already covers
            // it; skip so a parked limb is not rewriting collider geometry 50x
            // a second (re-sizing a trigger re-registers its overlaps).
            if (disp < 0.02f)
            {
                if (trig.center != Vector3.zero)
                {
                    trig.center = Vector3.zero;
                    trig.size = robot.parts[limb[i]].spec.size + Vector3.one * 0.10f;
                }
                continue;
            }
            Vector3 abs = new Vector3(Mathf.Abs(limbSweep[i].x), Mathf.Abs(limbSweep[i].y),
                                      Mathf.Abs(limbSweep[i].z));
            trig.size = robot.parts[limb[i]].spec.size + Vector3.one * 0.10f + abs * disp;
            trig.center = -limbSweep[i] * (disp * 0.5f);
        }
    }

    // ------------------------------------------------------- ground geometry

    static float groundY = float.NaN;
    /// <summary>Deck height, world metres. Phase0Manager builds the arena floor
    /// as a Plane named "arena_floor"; cached because it never moves.</summary>
    public static float GroundY()
    {
        if (float.IsNaN(groundY))
        {
            var g = GameObject.Find("arena_floor");
            groundY = g != null ? g.transform.position.y : 0f;
        }
        return groundY;
    }

    /// <summary>Vertical half-height of a box of half-extents h under rotation
    /// r - the exact vertical support of an OBB. It has to be exact: a wedge's
    /// bounding SPHERE is 0.25 m where the part is 0.06 m thick, and testing
    /// the sphere reads the critic's floor-level flipper as already underground
    /// at rest, which would freeze the arm at travel 0 and quietly delete the
    /// weapon instead of fixing it.</summary>
    static float VertExtent(Quaternion r, Vector3 h)
    {
        return Mathf.Abs((r * Vector3.right).y) * h.x
             + Mathf.Abs((r * Vector3.up).y) * h.y
             + Mathf.Abs((r * Vector3.forward).y) * h.z;
    }

    /// <summary>Would any live limb part be below the deck at this travel?
    /// World space, live rotation, so it is also correct when the robot is
    /// tilted, airborne or on its side. Spindles are exempt: a drum rotates
    /// continuously and cannot be "stopped at the floor" without stalling the
    /// weapon outright - a rotor built to sweep through the deck is a build
    /// error the arc picker cannot fix either, and no measured build does it.
    /// That is a known limit, not an oversight.</summary>
    bool GroundBlocked(float t)
    {
        if (kind == ActuatorKind.Spindle) return false;
        if (limb == null || restPos == null || robot == null || !Wired) return false;
        float deck = GroundY() + GROUND_CLEAR;
        Quaternion q = Rotary ? Quaternion.AngleAxis(t * arcSign * Mathf.Rad2Deg, axisLocal)
                              : Quaternion.identity;
        Vector3 d = Rotary ? Vector3.zero : axisLocal * t;
        for (int i = 0; i < limb.Length; i++)
        {
            if (!LimbLive(limb[i])) continue;
            Vector3 lp = Rotary ? pivotLocal + q * (restPos[i] - pivotLocal) : restPos[i] + d;
            Quaternion wr = robot.transform.rotation * (Rotary ? q * restRot[i] : restRot[i]);
            Vector3 wp = robot.transform.TransformPoint(lp);
            if (wp.y - VertExtent(wr, robot.parts[limb[i]].spec.size * 0.5f) < deck) return true;
        }
        return false;
    }

    /// <summary>Which way should this arc swing? See the GROUND_CLEAR block for
    /// the measurement that made this necessary.
    ///
    /// Worked entirely in ROBOT-LOCAL space against the machine's own lowest
    /// point, for two reasons: Wire() runs before the spawn drop so world Y is
    /// not meaningful yet, and "the deck" for this purpose is exactly the plane
    /// the wheels stand on - which is the plane the build screen draws.
    ///
    /// Conservative on purpose. It keeps the built direction unless that
    /// direction digs in EARLY and the other one is strictly better, so an
    /// overhead hammer - whose arc reaches the floor late, if at all, in both
    /// directions - is left exactly as every previous round measured it.</summary>
    float PickArcSign()
    {
        if (kind != ActuatorKind.Pivot || limb == null || limb.Length == 0) return 1f;
        if (robot == null || robot.parts == null) return 1f;
        float deck = float.MaxValue;
        for (int i = 0; i < robot.parts.Count; i++)
        {
            var p = robot.parts[i];
            if (p.detached || p.go == null) continue;
            float y = p.go.transform.localPosition.y
                    - VertExtent(p.go.transform.localRotation, p.spec.size * 0.5f);
            if (y < deck) deck = y;
        }
        if (deck == float.MaxValue) return 1f;
        float digPos = ArcDig(1f, deck);
        if (digPos <= 0f) return 1f;                 // built direction is clean: keep it
        float digNeg = ArcDig(-1f, deck);
        if (digNeg >= digPos) return 1f;             // no better the other way: keep it
        CompoundRobot.Log(robot.name + ": " + robot.parts[partIdx].spec.id
            + " arc REVERSED - as built it digs " + digPos.ToString("F2")
            + " m into the deck inside " + ARC_EARLY_DEG.ToString("F0")
            + " deg; the other way digs " + digNeg.ToString("F2") + " m.");
        return -1f;
    }

    /// <summary>Deepest penetration below `deck` (robot-local metres) anywhere
    /// in the first ARC_EARLY_DEG of the arc in direction s. 0 = never touches.</summary>
    float ArcDig(float s, float deck)
    {
        float worst = 0f;
        for (int step = 1; step <= 9; step++)
        {
            Quaternion q = Quaternion.AngleAxis(s * ARC_EARLY_DEG * step / 9f, axisLocal);
            for (int i = 0; i < limb.Length; i++)
            {
                if (!LimbLive(limb[i])) continue;
                Vector3 lp = pivotLocal + q * (restPos[i] - pivotLocal);
                float y = lp.y - VertExtent(q * restRot[i], robot.parts[limb[i]].spec.size * 0.5f);
                if (deck - y > worst) worst = deck - y;
            }
        }
        return worst;
    }

    /// <summary>ROUND-2-CRITIC FIX (CRITICAL 1: "the prior session's whole
    /// Phase-4 dataset was measured against a stale actuator; its material
    /// numbers describe code that is not running"). The critic could only catch
    /// that by re-reading LimbReport against a table of expected values - the
    /// sweep files themselves carried no evidence of which build of the code
    /// produced them. Every file this project writes can now stamp the live
    /// constants that produced its numbers, so a stale-regime dataset announces
    /// itself instead of having to be caught by arithmetic.</summary>
    public static string ConstantsStamp()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("# LIVE CONSTANTS  tipSpeedCap m/s:");
        foreach (string m in MatDB.Order)
            sb.Append(" ").Append(m).Append("=")
              .Append(TipSpeedCap(MatDB.Get(m).strengthRel).ToString("F2"));
        sb.Append("  | MotorKW 0/1/2/3 eng ")
          .Append(MotorKW(0).ToString("F1")).Append("/").Append(MotorKW(1).ToString("F1"))
          .Append("/").Append(MotorKW(2).ToString("F1")).Append("/").Append(MotorKW(3).ToString("F1"));
        sb.Append("  | DRIVE_KW_PER_TONNE ").Append(PowerPlant.DRIVE_KW_PER_TONNE.ToString("F2"));
        sb.Append(" | LIFT_EFFICIENCY ").Append(LIFT_EFFICIENCY.ToString("F2"));
        sb.Append(" | REACTION_FRAC ").Append(REACTION_FRAC.ToString("F2"));
        sb.Append(" | GROUND_CLEAR ").Append(GROUND_CLEAR.ToString("F3"));
        sb.Append(" | BITE_RATE_FRAC ").Append(BITE_RATE_FRAC.ToString("F2"));
        sb.Append(" | EXPOSED_MULT ").Append(DamageResolver.EXPOSED_MULT.ToString("F2"));
        sb.Append(" | STEP_DISP_CAP ").Append(STEP_DISP_CAP.ToString("F2"));
        // ROUND-2-DEV (critic CRITICAL "nothing stamps which build of the game
        // produced a dataset"). The stamp covered the actuator only, so a
        // dataset could still be invalidated by a change in FightManager,
        // DamageResolver or the builder without announcing it. It now covers
        // the whole scoring/damage regime AND carries a fingerprint of the
        // SOURCE ITSELF, so a sweep file taken against edited code cannot look
        // identical to one taken before the edit even if every constant
        // happened to be unchanged.
        sb.Append("\n# LIVE CONSTANTS (cycle) WIND_RELEASE_FRAC ").Append(WIND_RELEASE_FRAC.ToString("F3"))
          .Append(" | WIND_TIMEOUT_S ").Append(WIND_TIMEOUT_S.ToString("F2"))
          .Append(" | BITE_TIP_SPEED_MS ").Append(BITE_TIP_SPEED_MS.ToString("F2"))
          .Append(" | RECOVER_S ").Append(RECOVER_S.ToString("F2"))
          .Append(" | PIVOT_RETURN_DPS ").Append(PIVOT_RETURN_DPS.ToString("F0"))
          .Append(" | MIN_BITE_E ").Append(MIN_BITE_E.ToString("F0"))
          .Append(" | DRAIN_FRAC ").Append(DRAIN_FRAC.ToString("F2"))
          .Append(" | LIN_IMP_CAP ").Append(LIN_IMP_CAP.ToString("F0"))
          .Append(" | EFFICIENCY ").Append(EFFICIENCY.ToString("F2"))
          .Append(" | RECUP_FRAC ").Append(RECUP_FRAC.ToString("F2"))
          .Append(" | REACTION_STALL_MUL ").Append(REACTION_STALL_MUL.ToString("F1"));
        sb.Append("\n# LIVE CONSTANTS (damage/judging) DMG_K ").Append(DamageResolver.DMG_K.ToString("F3"))
          .Append(" | HP_K ").Append(DamageResolver.HP_K.ToString("F0"))
          .Append(" | SHOVE_CAP ").Append(DamageResolver.SHOVE_CAP.ToString("F0"))
          .Append(" | PART_IMMUNITY ").Append(DamageResolver.PART_IMMUNITY.ToString("F2"))
          .Append(" | RAM_MIN_J ").Append(DamageResolver.RAM_MIN_J.ToString("F0"))
          .Append(" | RAM_J_CAP ").Append(DamageResolver.RAM_J_CAP.ToString("F0"))
          .Append(" | STRUCT_BAND ").Append(FightManager.STRUCT_BAND.ToString("F3"))
          .Append(" | MIN_STRUCT_PARTS ").Append(FightManager.MIN_STRUCT_PARTS.ToString("F1"))
          .Append(" | DRAW_BAND_FRAC ").Append(FightManager.DRAW_BAND_FRAC.ToString("F3"))
          .Append(" | CONTROL_BAND ").Append(FightManager.CONTROL_BAND.ToString("F3"))
          .Append(" | MATCH_TIME ").Append(FightManager.DEFAULT_MATCH_TIME.ToString("F0"))
          .Append(" | CREDIT_BUDGET ").Append(BuilderManager.CREDIT_BUDGET);
        sb.Append("\n# SOURCE REGIME ").Append(SourceRegime());
        return sb.ToString();
    }

    /// <summary>ROUND-2-DEV (critic CRITICAL 1). A fingerprint of every .cs
    /// file under Assets/Phase0/Scripts and Assets/Phase1/Scripts: name, byte
    /// length and last-write time, folded into one 8-hex-digit value plus a
    /// human-readable count and newest-file stamp.
    ///
    /// WHY MTIME AND LENGTH RATHER THAN A CONTENT HASH. It has to be cheap
    /// enough to run at the head of every sweep file and on the build screen,
    /// it must not need System.Reflection (blocked in this project's tooling),
    /// and it only has to answer ONE question: "is this the same code that
    /// produced the other dataset?". Two consecutive QA rounds were invalidated
    /// because that question could only be answered afterwards by reading file
    /// mtimes off disk by hand. Now the dataset carries the answer.
    ///
    /// .bak files are excluded on purpose - this project keeps a backup beside
    /// every edit, so including them would make the fingerprint change when
    /// nothing that runs has changed.</summary>
    public static string SourceRegime()
    {
        try
        {
            uint h = 2166136261u;              // FNV-1a seed
            int n = 0;
            long newest = 0;
            string[] dirs = {
                Application.dataPath + "/Phase0/Scripts",
                Application.dataPath + "/Phase1/Scripts" };
            foreach (string d in dirs)
            {
                if (!System.IO.Directory.Exists(d)) continue;
                string[] files = System.IO.Directory.GetFiles(d);
                System.Array.Sort(files, System.StringComparer.Ordinal);
                foreach (string f in files)
                {
                    if (!f.EndsWith(".cs")) continue;          // skips every .bak
                    var fi = new System.IO.FileInfo(f);
                    long t = fi.LastWriteTimeUtc.Ticks;
                    if (t > newest) newest = t;
                    string key = fi.Name + ":" + fi.Length + ":" + t;
                    for (int i = 0; i < key.Length; i++)
                    { h ^= key[i]; h *= 16777619u; }
                    n++;
                }
            }
            return h.ToString("x8") + " (" + n + " scripts, newest "
                 + new System.DateTime(newest, System.DateTimeKind.Utc)
                       .ToString("yyyy-MM-dd HH:mm:ss") + "Z)";
        }
        catch (System.Exception e) { return "unavailable (" + e.GetType().Name + ")"; }
    }

    /// <summary>ROUND-3-CRITIC FIX (CRITICAL 3, "toppling is not merely
    /// undemonstrated, it is 3x to 11x short of the angular impulse required").
    ///
    /// WHAT WAS WRONG. The old term was L = shove * liftBias * TIP_ARM_M with
    /// shove = min(drain * 0.3, SHOVE_CAP = 500). That is an ANGULAR impulse
    /// derived from a capped LINEAR impulse, so it could never exceed
    /// 500 * 0.75 * 0.45 = 169 N.m.s however much energy the weapon had stored,
    /// and at a real weapon's drain it delivered 44.5 N.m.s. Rolling a 707 kg
    /// machine over its 0.27 m half-track needs ~495 N.m.s. 0 topples in 28
    /// matches, on top of ~80 in the two rounds before that.
    ///
    /// WHAT IT IS NOW. Toppling is an ENERGY problem - you have to lift the
    /// victim's centre of mass over its own wheel line - so it is solved as
    /// one. Give the victim rotational energy E about the tipping axis:
    ///     E = 1/2 . I . w^2   and   L = I.w   =>   L = sqrt(2.I.E)
    /// where I is the victim's REAL inertia about that world axis, read out of
    /// its own inertia tensor rather than guessed. Two properties fall out of
    /// this for free and both are things the impulse formulation could not do:
    ///   . it is mass-aware - flipping something heavy costs proportionally
    ///     more energy, so a wedge that tosses a 450 kg ABS bot merely rocks a
    ///     2500 kg Tungsten one, with no separate rule;
    ///   . it cannot manufacture energy - E is capped by what the limb actually
    ///     drained, times LIFT_EFFICIENCY, so a bigger flip has to be BUILT.
    ///
    /// The impulse is applied about the victim's centre of mass, so the energy
    /// delivered is exactly E; the ground contact then does the levering, which
    /// is how a real flipper works. GyroStabilizer skips its righting torque
    /// while |angularVelocity| > MAX_SPIN (5 rad/s), so a flip that is fast
    /// enough to be a flip is not fought by the victim's own gyro mid-roll.</summary>
    public static void ApplyTopple(Rigidbody victim, Vector3 axisWorld, float energyJ)
    {
        if (victim == null || energyJ <= 1f) return;
        if (axisWorld.sqrMagnitude < 1e-6f) return;
        Vector3 ax = axisWorld.normalized;
        float I = InertiaAboutWorldAxis(victim, ax);
        float L = Mathf.Min(Mathf.Sqrt(2f * I * energyJ), TOPPLE_IMP_CAP);
        victim.AddTorque(ax * L, ForceMode.Impulse);
    }

    /// <summary>Moment of inertia of a rigidbody about an arbitrary WORLD axis
    /// through its centre of mass. Unity only exposes the principal moments
    /// (inertiaTensor) in the frame given by inertiaTensorRotation, so the axis
    /// is rotated into that frame and recombined as sum(I_k . a_k^2), which is
    /// exact for a unit axis.</summary>
    public static float InertiaAboutWorldAxis(Rigidbody rb, Vector3 axisWorld)
    {
        Vector3 a = Quaternion.Inverse(rb.rotation * rb.inertiaTensorRotation) * axisWorld.normalized;
        Vector3 t = rb.inertiaTensor;
        return Mathf.Max(0.01f, t.x * a.x * a.x + t.y * a.y * a.y + t.z * a.z * a.z);
    }

    /// <summary>`travel` at the last pose handed to RecomputeMass.</summary>
    float posedTravel = float.NaN;

    void ApplyReaction(float watts)
    {
        if (robot.rb == null || rate < 0.05f) return;
        Vector3 worldAxis = transform.parent != null
                          ? transform.parent.TransformDirection(axisLocal)
                          : axisLocal;
        // ROUND-2-DEV: torque = power / omega diverges at omega -> 0, and a
        // latched wind-up sits there for several steps. Clip it at the motor's
        // stall torque. See REACTION_STALL_MUL.
        float f = Mathf.Min(watts / Mathf.Max(rate, 0.05f),
                            REACTION_STALL_MUL * watts / Mathf.Max(MaxRate, 0.05f));
        if (Rotary) robot.rb.AddTorque(-worldAxis * f * REACTION_FRAC, ForceMode.Force);
        else        robot.rb.AddForce(-worldAxis * f * REACTION_FRAC, ForceMode.Force);
    }

    // =================================================================== biting

    /// <summary>Called by a LimbEdge trigger. Mirrors SpinnerWeapon.TryBite -
    /// same gates, same energy-drain accounting, same clamped shove - so an
    /// actuated hit and a disc hit are priced on one scale.</summary>
    /// <summary>RB1-DEV instrument (measurement only, no behaviour): where do
    /// limb bites die? Counted per trigger callback. fnTarget = the callback is
    /// touching a live enemy part; the rest are the gates below it.</summary>
    public static int fnCall, fnTarget, fnGate, fnEnergy, fnCool, fnApply;
    public static int fnNoRb, fnOwnRb, fnNotRobot, fnNoCombat, fnNoIdx;
    public static string fnNoIdxWho = "";
    public static float fnGateWorstFrac;
    public static void FunnelReset()
    { fnCall = fnTarget = fnGate = fnEnergy = fnCool = fnApply = 0; fnGateWorstFrac = 0f;
      fnNoRb = fnOwnRb = fnNotRobot = fnNoCombat = fnNoIdx = 0; fnNoIdxWho = ""; }

    public void Bite(int limbPartIdx, Collider other)
    {
        fnCall++;
        if (!Wired || robot.dead) return;
        if (!LimbLive(limbPartIdx) || robot.parts[partIdx].detached) return;

        Rigidbody orb = other.attachedRigidbody;
        if (orb == null) { fnNoRb++; return; }                         // world geometry
        if (orb == robot.rb) { fnOwnRb++; return; }                    // own body
        CompoundRobot victim = orb.GetComponent<CompoundRobot>();
        if (victim == null || victim == robot || victim.dead) { fnNotRobot++; return; }
        if (!robot.combatEnabled || !victim.combatEnabled) { fnNoCombat++; return; }
        int vIdx = victim.PartIndexOf(other);
        if (vIdx < 0) { fnNoIdx++; fnNoIdxWho = other.name + "/trig=" + other.isTrigger; return; }
        fnTarget++;

        // The gate comes FIRST, before the cooldown stamp: a graze at
        // wind-up speed must not consume the hit that the real swing is about
        // to land half a second later.
        // ROUND-2-DEV: the gate is now an ABSOLUTE edge speed, floored by the
        // old fractional rule so it can never be stricter than what shipped.
        // "You need 4 m/s at the edge, or 45% of everything this limb has,
        // whichever is LESS." See BITE_TIP_SPEED_MS for the measurement that
        // chose 4.0 and for why the fractional rule punished good materials.
        float armM  = Rotary ? Mathf.Max(tipRadius, 0.05f) : 1f;
        float gateV = Mathf.Min(BITE_TIP_SPEED_MS, BITE_RATE_FRAC * MaxRate * armM);
        if (rate * armM < gateV)
        {
            fnGate++;
            float fr = rate * armM / Mathf.Max(gateV, 1e-4f);
            if (fr > fnGateWorstFrac) fnGateWorstFrac = fr;
            return;
        }
        float E = Energy;
        if (E < MIN_BITE_E) { fnEnergy++; return; }
        float t;
        if (lastBite.TryGetValue(other, out t) && Time.time - t < HIT_COOLDOWN) { fnCool++; return; }
        lastBite[other] = Time.time;
        fnApply++;
        statBites++;
        if (playerControlled) statBitesPlayer++; else statBitesAI++;

        float drain = DRAIN_FRAC * E;
        Vector3 pos = other.ClosestPoint(robot.parts[limbPartIdx].go.transform.position);
        Vector3 relV = VelUtil.GetLinearVelocity(robot.rb) - VelUtil.GetLinearVelocity(orb);
        float linImp = Mathf.Min(relV.magnitude * Mathf.Min(robot.rb.mass, orb.mass) * 0.5f,
                                 LIN_IMP_CAP);
        // Same taper the disc uses: a limb barely under way deflects instead of
        // biting, so the opening clash is not decided by whoever twitched first.
        linImp *= Mathf.Clamp01(rate / Mathf.Max(MaxRate, 0.01f));

        string sid = robot.parts[limbPartIdx].spec.id;
        float hardness = robot.parts[limbPartIdx].spec.edgeHardness;
        if (hardness <= 0f) hardness = 1f;
        statBiteDmg += drain + linImp;
        if (playerControlled) statBiteDmgPlayer += drain + linImp; else statBiteDmgAI += drain + linImp;
        DamageResolver.ApplyHit(robot, victim, vIdx, drain + linImp, hardness, pos,
                                DamageResolver.SRC_LIMB);

        // Energy left the limb. E is quadratic in rate, so this both dips the
        // swing and, for a Pivot, can stall it outright against something heavy
        // - which is the correct feel: your hammer stops on armour.
        rate *= Mathf.Sqrt(1f - DRAIN_FRAC);

        // ---- the visible hit.
        float lift = LiftBiasOf(sid);
        bool grab = sid.StartsWith("hook");
        Vector3 dir = orb.worldCenterOfMass - robot.rb.worldCenterOfMass;
        dir.y = 0f;
        dir = dir.sqrMagnitude < 1e-4f ? robot.transform.forward : dir.normalized;
        // Kept before `dir` is bent upward/backward below: the horizontal
        // attacker -> victim line is what the topple axis is perpendicular to.
        Vector3 flat = dir;
        // A hook does the opposite of everything else: it PULLS, so instead of
        // knocking the opponent clear you drag them off their wheels and out of
        // whatever they were lining up. Slightly downward, because a hook that
        // lifted would just be a bad wedge.
        dir = grab ? (-dir + Vector3.down * 0.15f).normalized
                   : (dir * (1f - lift) + Vector3.up * (0.35f + lift * 1.6f)).normalized;
        float shove = Mathf.Min(drain * 0.3f, DamageResolver.SHOVE_CAP);
        orb.AddForceAtPosition(dir * shove, pos, ForceMode.Impulse);
        robot.rb.AddForce(dir * (-shove * 0.5f), ForceMode.Impulse);

        // ROUND-1 IMPL FIX (critic CRITICAL 3), the angular half of the lift.
        // Axis = up x (attacker -> victim), which by the right-hand rule lifts
        // the victim's NEAR edge - the edge the wedge is actually under - and
        // rolls them over backwards. See TIP_ARM_M for the sizing.
        if (lift > 0.01f)
        {
            Vector3 tipAxis = Vector3.Cross(Vector3.up, flat);
            // Round-3-critic CRITICAL 3: the energy the limb just released,
            // times how much of the edge is pointed upward, times how much of
            // that survives the conversion. See ApplyTopple.
            ApplyTopple(orb, tipAxis, drain * lift * LIFT_EFFICIENCY);
        }
    }

    /// <summary>0 = drive the opponent away, 1 = drive them upward. This is what
    /// makes a WEDGE a different weapon from a blade rather than a weaker one:
    /// the same contact goes mostly vertical, so toppling becomes a strategy
    /// instead of an accident. Read by CompoundRobot on plain rams too, so a
    /// wedge bolted straight to the frame still works as a wedge.</summary>
    public static float LiftBiasOf(string id)
    {
        if (id.StartsWith("wedge")) return 0.75f;
        if (id.StartsWith("plow")) return 0.55f;
        return 0f;
    }

    // ============================================================= limb wiring

    public static bool IsActuatorId(string id)
    {
        return id.StartsWith("pivot") || id.StartsWith("spindle") || id.StartsWith("ram");
    }

    static ActuatorKind KindOf(string id)
    {
        if (id.StartsWith("spindle")) return ActuatorKind.Spindle;
        if (id.StartsWith("ram")) return ActuatorKind.Ram;
        return ActuatorKind.Pivot;
    }

    /// <summary>
    /// Partition the part graph at every actuator and attach the components.
    ///
    /// Remove all actuator nodes from the connection graph. Whatever component
    /// still holds the core is the CHASSIS; any component that does not, and
    /// that touches actuator A, is A's LIMB. A component touching two actuators
    /// goes to the first and is logged - daisy-chained actuators are a v1
    /// limitation, not a silent wrong answer.
    /// </summary>
    public static void Wire(CompoundRobot robot, PartSpec[] specs, CompoundRobot.Conn[] conns)
    {
        int n = specs.Length;
        var isAct = new bool[n];
        int actCount = 0;
        for (int i = 0; i < n; i++)
            if (IsActuatorId(specs[i].id)) { isAct[i] = true; actCount++; }
        if (actCount == 0) return;

        var adj = new List<int>[n];
        for (int i = 0; i < n; i++) adj[i] = new List<int>();
        foreach (var c in conns)
        {
            if (c.a < 0 || c.b < 0 || c.a >= n || c.b >= n) continue;
            adj[c.a].Add(c.b);
            adj[c.b].Add(c.a);
        }

        // Component id per non-actuator part, flooding but never crossing an
        // actuator.
        var comp = new int[n];
        for (int i = 0; i < n; i++) comp[i] = -1;
        int nComp = 0;
        var stack = new List<int>();
        for (int i = 0; i < n; i++)
        {
            if (isAct[i] || comp[i] >= 0) continue;
            int id = nComp++;
            stack.Clear();
            stack.Add(i);
            comp[i] = id;
            while (stack.Count > 0)
            {
                int cur = stack[stack.Count - 1];
                stack.RemoveAt(stack.Count - 1);
                foreach (int nb in adj[cur])
                {
                    if (isAct[nb] || comp[nb] >= 0) continue;
                    comp[nb] = id;
                    stack.Add(nb);
                }
            }
        }

        int chassisComp = robot.coreIndex >= 0 && robot.coreIndex < n && !isAct[robot.coreIndex]
                        ? comp[robot.coreIndex] : -1;

        var claimed = new int[nComp];
        for (int i = 0; i < nComp; i++) claimed[i] = -1;

        for (int a = 0; a < n; a++)
        {
            if (!isAct[a]) continue;
            var members = new List<int>();
            var comps = new List<int>();
            foreach (int nb in adj[a])
            {
                if (isAct[nb]) continue;
                int c = comp[nb];
                if (c < 0 || c == chassisComp) continue;     // that side is the frame
                if (comps.Contains(c)) continue;
                if (claimed[c] >= 0)
                {
                    CompoundRobot.Log(robot.name + ": " + specs[a].id + " shares a limb with "
                        + specs[claimed[c]].id + " - daisy-chained actuators are not driven "
                        + "independently yet; the first one owns it.");
                    continue;
                }
                claimed[c] = a;
                comps.Add(c);
            }
            foreach (int c in comps)
                for (int i = 0; i < n; i++)
                    if (!isAct[i] && comp[i] == c) members.Add(i);

            if (members.Count == 0)
                CompoundRobot.Log(robot.name + ": " + specs[a].id
                    + " drives nothing - bolt a weapon to its free face.");

            robot.parts[a].go.AddComponent<Actuator>()
                 .Init(robot, a, KindOf(specs[a].id), ActuatorAxis(specs[a].id), members.ToArray());
        }
    }

    /// <summary>The mount normal rides in the spec id (BuilderManager.AxisCode),
    /// exactly as it already does for wheels and discs. For a Pivot and a
    /// Spindle that normal is the AXLE; for a Ram it is the stroke direction.</summary>
    static Vector3 ActuatorAxis(string id)
    {
        string prefix = id.StartsWith("spindle") ? "spindle" : id.StartsWith("ram") ? "ram" : "pivot";
        return PartVisualFactory.ParseAxis(id, prefix, Vector3.right);
    }
}

/// <summary>Trigger forwarder living on a limb part. Kept separate from
/// Actuator so a limb of five parts still has exactly one driver but five
/// independent hit volumes - the blade at the tip and the beam behind it bite
/// with the same stored energy but their own hardness.</summary>
public class LimbEdge : MonoBehaviour
{
    Actuator act;
    int idx;
    public void Bind(Actuator a, int partIdx) { act = a; idx = partIdx; }
    void OnTriggerEnter(Collider other) { if (act != null) act.Bite(idx, other); }
    void OnTriggerStay(Collider other) { if (act != null) act.Bite(idx, other); }
}

}
