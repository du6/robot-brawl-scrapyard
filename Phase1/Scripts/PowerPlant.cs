using UnityEngine;

namespace RobotBrawl.Phase0
{
/// <summary>
/// Phase 3 (design doc §6.2) — the energy budget.
///
/// Batteries hold energy; engines set how fast you may draw it. Everything the
/// robot DOES spends from that budget:
///   · driving      — DRIVE_KW_PER_TONNE x mass x |throttle|, so a heavy
///                    machine is expensive to move, continuously;
///   · spinning up  — the ACTUAL mechanical energy entering the disc,
///                    dE = d(1/2 I w^2), divided by SPIN_EFFICIENCY. This is
///                    derived, not invented: a tungsten rim has ~2.5x the
///                    inertia of a steel one and therefore costs ~2.5x to
///                    spin, and every bite that drains the disc has to be
///                    paid for again out of the battery.
///
/// Two failure modes, both honest (§3.1: never fake the outcome of a design):
///   · DEMAND > SUPPLY — draw is capped at the sum of the live power parts'
///     powerKW, and everything is scaled by the shortfall. An underpowered
///     machine strains: it drives slower and its disc will not reach speed.
///   · STORED = 0 — throttle goes to zero and the disc spins down. You are a
///     rock, the count-out clock starts, and the cause line says why. No limp
///     mode: a limp mode would be exactly the kind of hidden rescue §3.1
///     forbids.
///
/// Losing your battery mid-fight kills you the same way losing your gyro does,
/// which is the point: power is a part with a location, not a global.
///
/// ORDER INDEPENDENCE. Consumers read supplyFrac (settled LAST step) and post
/// their demand for THIS step; FixedUpdate here settles and clears. Neither
/// side needs a defined script execution order, and the one-step lag at 50 Hz
/// is invisible.
/// </summary>
public class PowerPlant : MonoBehaviour
{
    /// <summary>Fraction of battery energy that reaches the disc as rotational
    /// KE. The rest is motor, gearing and windage loss. Set so a tungsten
    /// spinner grinding continuously flattens one battery in ~15 s and a steel
    /// one lasts most of a match — the §6.2 design tension.</summary>
    public static float SPIN_EFFICIENCY = 0.35f;
    /// <summary>kW per tonne at full throttle.
    ///
    /// ROUND-6 REBALANCE: was 9. At 9 kW/t the budget was unaffordable for
    /// every machine in the buildable space, not just for extravagant ones.
    /// One battery is 240 kJ and permits 8 kW, so ANY build over ~890 kg was
    /// asking for more than its own pack could deliver, and a 90 s match at
    /// full throttle cost 810 kJ at one tonne - 3.4 batteries. Measured
    /// consequence: 22-42% of all robot-time spent as an immobile rock, both
    /// sides flat simultaneously in most matches, and TRIPLING a build's
    /// battery made its record no better, because being flat costs nothing
    /// when the opponent is flat too. A budget that every legal build fails is
    /// not a budget, it is a countdown.
    ///
    /// At 3.5 kW/t the affordability line sits INSIDE the buildable space:
    /// ~1 tonne is what one battery carries through a full 90 s match at
    /// continuous full throttle. A light machine is solvent, a heavyweight has
    /// to buy a second pack, and running dry becomes the consequence of a
    /// build decision instead of the default outcome. This rescues no
    /// particular design - it is the same number for every machine, and
    /// heavier still costs strictly, linearly more.</summary>
    ///
    /// ROUND-3-CRITIC FIX (CRITICAL 1 "a hard 952 kg solvency line", CRITICAL 4
    /// "89% of matches are decided by a clock or a battery", MAJOR 5
    /// "parking-vs-driving is entirely a function of mass"). 3.5 -> 1.25.
    ///
    /// THE ARITHMETIC THAT CHOSE IT. The stock pack is core 60 kJ + battery
    /// 240 kJ = 300 kJ. A 90 s match at continuous full throttle costs
    /// 0.09 * DRIVE_KW_PER_TONNE * mass_kg kJ, so the mass at which a stock
    /// pack lasts exactly one match is m* = 300000 / (90 * DRIVE_KW_PER_TONNE).
    /// At 3.5 that is 952 kg. Measured mass of the SAME 10-14 part chassis by
    /// material (critic, three rounds): ABS 450 | CarbonFiber 509 |
    /// Aluminum 707-770 | Steel 1642-1924 | Tungsten 2501-3856. The line fell
    /// BETWEEN Aluminum and Steel, so four of the seven materials in the
    /// picker were an automatic loss on the energy budget alone, independent
    /// of how well the machine fought: the critic's Steel plow lost ZERO
    /// pieces in 4 of 4 matches - the most durable build measured in three
    /// rounds - and still went 1W/3L, three of the four losses being power
    /// count-outs. Predicted time-to-flat t = 300/(0.0035*m) matched every
    /// measurement on disk to within a few seconds across 2501, 1994, 1642
    /// and 3856 kg. A material picker whose top half is an unmarked trap is
    /// worse than no material picker.
    ///
    /// WHY 1.25 EXACTLY. Solve m* = 90 s for the heaviest chassis a player can
    /// actually assemble out of the palette: the critic's all-Tungsten frames
    /// came in at 2501 kg. 300000 / (90 * 2501) = 1.33, so 1.25 puts the line
    /// at 2667 kg - just clear of a fully Tungsten machine, and still well
    /// under the 3856 kg Tungsten-frame-plus-Tungsten-arm extreme, which SHOULD
    /// run dry. Every material in the picker is now solvent as a frame; going
    /// heavier still costs strictly and linearly more, it just no longer costs
    /// you the match before the weapon is relevant.
    ///
    /// WHAT ELSE THIS IS DOING. It is deliberately paired with the Actuator
    /// motor becoming a build decision (Actuator.MotorKW): driving used to be
    /// ~4x the draw of holding a weapon at speed, which is why parking beat
    /// fighting for anything over the line and why 35.7% of matches ended in a
    /// power count-out. Locomotion is now the cheap load and the weapon is the
    /// expensive one, so standing still with a spun-up limb is what empties the
    /// pack. That is the same rebalance the critic asked for under finding 5,
    /// done from this end rather than by taxing the weapon into uselessness.
    public static float DRIVE_KW_PER_TONNE = 1.25f;
    /// <summary>Below this fraction the AI disengages to conserve (§8).</summary>
    public static float LOW_FRAC = 0.18f;

    public CompoundRobot robot;

    // Parallel arrays: part index -> that part's contribution.
    int[] cellIdx;
    float[] cellKJ;
    float[] cellKW;

    public float capacityKJ;     // live capacity (drops when a battery shears off)
    public float storedKJ;
    public float peakKW;         // live draw ceiling
    public float drawKW;         // what was actually delivered last step
    public float demandKW;       // what was asked for last step (may exceed peak)
    /// <summary>delivered / requested last step, 0..1. Consumers multiply by
    /// this. 0 means flat.</summary>
    public float supplyFrac = 1f;

    float pending;               // demand accumulating for the current step
    bool everInit;

    public float Frac { get { return capacityKJ > 0.0001f ? storedKJ / capacityKJ : 0f; } }
    public bool Flat { get { return storedKJ <= 0.0001f; } }
    public bool Low  { get { return Frac <= LOW_FRAC; } }
    /// <summary>True when the machine is asking for more than it can supply -
    /// the readable "straining" state, distinct from being flat.</summary>
    public bool Strained { get { return !Flat && demandKW > peakKW + 0.01f; } }

    public void Init(CompoundRobot r, int[] idx, float[] kj, float[] kw)
    {
        robot = r;
        cellIdx = idx; cellKJ = kj; cellKW = kw;
        Recompute();
        storedKJ = capacityKJ;
        everInit = true;
    }

    /// <summary>Capacity and ceiling from the power parts that are STILL
    /// ATTACHED. Called every step: shearing a battery off must take its
    /// energy with it, or "kill the power pack" would not be a strategy.</summary>
    public void Recompute()
    {
        capacityKJ = 0f; peakKW = 0f;
        if (cellIdx == null || robot == null) return;
        for (int i = 0; i < cellIdx.Length; i++)
        {
            if (!robot.PartAlive(cellIdx[i])) continue;
            capacityKJ += cellKJ[i];
            peakKW += cellKW[i];
        }
        if (storedKJ > capacityKJ) storedKJ = capacityKJ;
    }

    /// <summary>Post demand for this step, in kW. Additive across consumers.</summary>
    public void Draw(float kW)
    {
        if (kW > 0f) pending += kW;
    }

    /// <summary>Drive draw for a given mass and throttle, in kW.</summary>
    public static float DriveKW(float massKg, float throttle)
    {
        return DRIVE_KW_PER_TONNE * (massKg / 1000f) * Mathf.Abs(throttle);
    }

    void FixedUpdate()
    {
        if (!everInit || robot == null) return;
        Recompute();

        // Nothing is spent before the bell - the settle must not cost the
        // player a fifth of the tank (same gate the spinner ramp uses).
        if (!robot.combatEnabled || robot.dead)
        {
            pending = 0f; demandKW = 0f; drawKW = 0f;
            supplyFrac = 1f;
            return;
        }

        demandKW = pending;
        pending = 0f;

        float dt = Time.fixedDeltaTime;
        float able = Mathf.Min(demandKW, peakKW);            // engine ceiling
        float haveKW = storedKJ / Mathf.Max(dt, 1e-5f);       // what is left in the tank
        drawKW = Mathf.Min(able, haveKW);                     // tank floor

        storedKJ = Mathf.Max(0f, storedKJ - drawKW * dt);
        supplyFrac = demandKW > 0.0001f ? Mathf.Clamp01(drawKW / demandKW) : 1f;
        if (Flat) supplyFrac = 0f;
    }

    /// <summary>Seconds of life left at the current draw. Infinity when idle.
    /// Used by the builder panel and the fight HUD.</summary>
    public float SecondsLeft()
    {
        if (drawKW <= 0.01f) return float.PositiveInfinity;
        return storedKJ / drawKW;
    }
}
}
