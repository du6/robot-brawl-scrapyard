using System.Collections.Generic;
using UnityEngine;

namespace RobotBrawl.Phase0
{
/// <summary>Difficulty comes from REACTION TIME, AGGRESSION and BUILD QUALITY
/// only (design doc §8) - never from stat cheating. Every bot on every tier is
/// spawned through the identical SpawnBot path with the identical physics; a
/// Champion opponent is not tougher, it is quicker to decide and it commits.</summary>
public enum AiTier { Rookie, Veteran, Champion }

/// <summary>
/// Phase 3 (§8) - the authored enemy roster and the difficulty tiers.
///
/// Five hand-designed bots, all on the same proven chassis skeleton the Mauler
/// uses (core + two chassis blocks + four wheels flush at +/-0.27), because the
/// round-4 critic finding was that an authored opponent with hand-guessed
/// geometry is a BROKEN opponent - wheels touching nothing, a gyro on the most
/// exposed seam on the machine. What varies between them is what should vary:
/// material, mass, weapon and how much armour they carry.
///
///   MAULER      aluminium wedge-brawler, the tuned mid-tier baseline
///   BULWARK     steel juggernaut - roof plates, heaviest, slowest, ram spike
///   SCOUT       ABS/carbon featherweight - fast, fragile, cheap to run
///   TIPPER      gimmick: a tall battery tower and NO gyro. Teaches the player
///               that a high centre of mass loses fights by itself
///   WIDOWMAKER  gimmick: carbon frame, TUNGSTEN spinner. Enormous bite, and a
///               disc whose inertia flattens its battery in well under a match
///               (§6.2) - teaches that a weapon has a running cost
/// </summary>
public static class EnemyRoster
{
    public class Entry
    {
        public string id;
        public string label;
        public string blurb;
        public AiTier tier;
    }

    public static readonly Entry[] All =
    {
        new Entry { id = "mauler",     label = "MAULER",     tier = AiTier.Veteran,
                    blurb = "Aluminium wedge-brawler. The fair fight." },
        new Entry { id = "bulwark",    label = "BULWARK",    tier = AiTier.Champion,
                    blurb = "Steel juggernaut. Slow, armoured, hits like a truck." },
        new Entry { id = "scout",      label = "SCOUT",      tier = AiTier.Rookie,
                    blurb = "ABS featherweight. Quick, cheap to run, made of paper." },
        new Entry { id = "tipper",     label = "TIPPER",     tier = AiTier.Rookie,
                    blurb = "Tall battery tower, no gyro. Beats itself if you let it." },
        new Entry { id = "widowmaker", label = "WIDOWMAKER", tier = AiTier.Champion,
                    blurb = "Carbon frame, tungsten disc. Devastating until the battery dies." },
        // ROUND-1 IMPL FIX (critic CRITICAL 5, second half: "put at least one
        // actuated bot on the roster so the feature is tested from both
        // sides"). Until now every Phase 4 verb existed only on the player's
        // machine, so nothing the AI did could ever exercise Actuator.aiFire,
        // the limb-vs-chassis partition on a spawned recipe, or the reaction
        // torque. RIPPER is the same proven skeleton as everything else on the
        // roster with a pivot hammer on the front roof - it is the archetype
        // the base-component kit is supposed to teach, shown to the player by
        // an opponent that uses it.
        new Entry { id = "ripper",     label = "RIPPER",     tier = AiTier.Veteran,
                    blurb = "Pivot hammer on a light frame. Swings when you get close." },
        // OWEN 2026-08-03: "I beat all robots so far with a simple spinner
        // robot." He had - 8-1, three leagues - and the bench says why: the
        // damage ratio across the roster tracks exactly ONE variable, whether
        // the opponent owns a disc. WIDOWMAKER is the only bot that did, it is
        // the only bot that beat him (1/3), and it does not appear until L4.
        // Everything before it lost between 3:1 and 11:1.
        //
        // These two are the answers, and both are derived from the damage
        // model rather than from theme:
        //
        //   HP = HP_K * strengthRel * volume,  so HP per kg = 6*strength/density
        //     CarbonFiber 4.13   ABS 2.00   Titanium 1.86   Aluminium 1.33
        //     Steel 0.76         Tungsten 0.37
        //
        // BULWARK is the "juggernaut" and it is STEEL - the second-worst
        // armour per kilogram in the game. That is why it takes 1929 and deals
        // 381. Under a weight cap, carbon fibre buys 5.4x the hit points steel
        // does, which is the whole of BASTION's design.
        new Entry { id = "millstone",  label = "MILLSTONE",  tier = AiTier.Veteran,
                    blurb = "Steel disc on an aluminium frame. Trades bite for bite." },
        new Entry { id = "bastion",    label = "BASTION",    tier = AiTier.Champion,
                    blurb = "Carbon-fibre fortress. Outlasts a disc instead of duelling it." },
    };

    public static Entry Find(string id)
    {
        foreach (var e in All) if (e.id == id) return e;
        return All[0];
    }

    /// <summary>Round-6 fix 4b - the standing recipe check the round-5 critic
    /// asked for: "add an automated recipe check that fails any roster bot
    /// whose power parts sit on a single minimum-threshold seam". A shipped
    /// opponent that disables itself is not a difficulty setting; it is a bug
    /// that hides every other measurement behind its own variance.
    ///
    /// Returns null when every non-gimmick recipe mounts each of its POWER
    /// parts on at least two independent seams, or a description of the
    /// offenders. `tipper` is exempt on purpose: its entire authored lesson is
    /// a battery tower that falls over.</summary>
    public static string AuditPowerMounting(P1PartDef[] pal)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var e in All)
        {
            if (e.id == "tipper") continue;
            var L = Recipe(e.id, pal);
            for (int i = 0; i < L.Count; i++)
            {
                if (L[i].def.category != P1Category.Power) continue;
                int seams = 0;
                for (int j = 0; j < L.Count; j++)
                    if (j != i && BuilderManager.SharedSockets(L[i], L[j]) > 0) seams++;
                if (seams < 2)
                    sb.Append(e.id).Append('.').Append(L[i].def.id)
                      .Append(" hangs on ").Append(seams).Append(" seam(s); ");
            }
        }
        return sb.Length == 0 ? null : sb.ToString();
    }

    // ---- tier knobs -------------------------------------------------------
    // Reaction time and commitment. Nothing here touches mass, damage, HP or
    // joint strength - the Rookie's disadvantage is that it re-aims slowly and
    // opens cautiously, which is a disadvantage a human player also has.

    /// <summary>ROUND-3-DEV instrument (measurement only when -1, which is the
    /// shipped value). -1 = off. 0/1/2 forces that tier's SPEED knobs
    /// (DecisionInterval / SteerAggr / OpeningThrottle / Aggression) onto EVERY
    /// tier while leaving WeaponRespect per-tier. This is the exact mirror of
    /// the round-3 critic's M2, which held AVOIDANCE constant and let speed
    /// vary; running the other half is the only way to test its causal claim
    /// ("the cause is the speed knobs") rather than infer it.</summary>
    public static int speedTierOverride = -1;
    static AiTier Sp(AiTier t)
    { return speedTierOverride < 0 ? t : (AiTier)Mathf.Clamp(speedTierOverride, 0, 2); }

    /// <summary>ROUND-3-DEV instrument, part 2. Per-knob overrides so ONE of
    /// the four speed knobs can be moved while the other three are held. -1 =
    /// off = shipped. "Veteran's whole set beats Champion's whole set" is not
    /// the same claim as "OpeningThrottle is the culprit", and fitting a
    /// constant that was never isolated is this project's recurring mistake.
    /// All four are shipped at -1, so this is a no-op in the game.</summary>
    public static float dIntOverride = -1f, steerOverride = -1f,
                        openThrOverride = -1f, aggrOverride = -1f;

    /// <summary>ROUND-3-DEV FIX 2 (round-3 critic CRITICAL "round 2's
    /// tier-inversion fix targets the wrong axis; inversion fully intact", and
    /// round-2 critic CRITICAL 1 before it).
    ///
    /// FLAT ACROSS TIERS ON PURPOSE. This used to be 0.45 / 0.20 / 0.10, and
    /// that ladder ran BACKWARDS: contact is the only gate on damage in this
    /// game, and re-aiming faster makes the bot overshoot and lose the contact
    /// it just won. Measured (qa_rb3d_M2_knobisolate.txt, Champion tier vs
    /// mauler, avoidance 1.00 in every arm, n=4 interleaved, this knob the ONLY
    /// thing that moves): 0.10 -> 0.20 takes enemy hits landed on the player
    /// from 24 to 40 and damage taken per match from 50 to 183. Relaxing this
    /// one knob recovers essentially the whole gap on its own.
    /// Kept as a per-tier function, and kept reading Sp(t), so a future tuning
    /// pass can re-open the ladder without re-plumbing the call sites - but do
    /// not re-open it in the "faster = harder" direction without re-measuring
    /// contact, because that is the direction this project has now been wrong
    /// in twice.</summary>
    public static float DecisionInterval(AiTier t)
    {
        if (dIntOverride >= 0f) return dIntOverride;
        AiTier u = Sp(t);
        return u == AiTier.Rookie ? 0.20f : u == AiTier.Veteran ? 0.20f : 0.20f;
    }
    /// <summary>ROUND-3-DEV FIX 2 KEPT THIS ONE PER-TIER, and that is a
    /// measurement, not an oversight. It was the control arm in M2: moving
    /// Champion's 0.06 to Veteran's 0.10 with everything else held changed
    /// enemy hits landed 24 -> 23 and pTaken 50 -> 64, i.e. nothing, inside the
    /// 1.04-1.21x replicate noise of the same batch. So the ladder keeps its
    /// one honest speed dimension - the Champion really does corner more
    /// precisely than the Rookie - at a measured cost of zero. RESIDUAL RISK,
    /// stated: only the Champion end (0.06 vs 0.10) was isolated. The Rookie's
    /// 0.22 is a bigger step and was never tested alone; M3 checks the whole
    /// shipped ladder end to end, which is the number that matters.</summary>
    public static float SteerAggr(AiTier t)
    {
        // Higher = the speed governor bites sooner = sloppier cornering.
        if (steerOverride >= 0f) return steerOverride;
        AiTier u = Sp(t);
        return u == AiTier.Rookie ? 0.22f : u == AiTier.Veteran ? 0.10f : 0.06f;
    }
    /// <summary>ROUND-3-DEV FIX 2. Was 0.40 / 0.55 / 0.80. Same story and the
    /// larger of the two terms: measured alone at Champion tier with everything
    /// else held, 0.80 -> 0.55 moves enemy hits landed 24 -> 42 and damage
    /// taken per match 50 -> 183 (3.7x, clear of the 2.4x noise floor). The
    /// harder tier was throttling itself out of its own contacts.</summary>
    public static float OpeningThrottle(AiTier t)
    {
        if (openThrOverride >= 0f) return openThrOverride;
        AiTier u = Sp(t);
        return u == AiTier.Rookie ? 0.55f : u == AiTier.Veteran ? 0.55f : 0.55f;
    }
    /// <summary>Throttle multiplier while closing. ROUND-3-DEV FIX 2: was
    /// 0.75 for Rookie, 1 above. Flattened with the other two throttle terms.
    /// It is a throttle multiplier, so on the measured mechanism a LOWER value
    /// makes the Rookie MORE dangerous, not less - the opposite of what the old
    /// comment "Rookie hesitates" intended. Flattened rather than re-fitted:
    /// I measured OpeningThrottle and DecisionInterval individually and did NOT
    /// measure this one individually, so I am not shipping a number for it that
    /// I cannot defend. Flat is the configuration M1's FLAT arms ran.</summary>
    public static float Aggression(AiTier t)
    {
        if (aggrOverride >= 0f) return aggrOverride;
        AiTier u = Sp(t);
        return u == AiTier.Rookie ? 1f : u == AiTier.Veteran ? 1f : 1f;
    }

    /// <summary>ROUND-2-DEV FIX 1 (round-2 critic CRITICAL 1 "the tier is
    /// inverted BY the tier knobs" and CRITICAL 2 "the weapon does not fire").
    ///
    /// How much of AIController's weapon-avoidance layer this tier gets: it
    /// scales the flank offset, the rear bias and the orbit stand-off, so 0
    /// means "drives at you and eats your weapon" and 1 is exactly the
    /// behaviour that shipped for every tier.
    ///
    /// WHY THIS KNOB EXISTS. Measured (qa_rb2d_avoid.txt, 19 matches, the two
    /// arms INTERLEAVED in one batch, owen's own SPINDLE build parked with the
    /// trigger held vs mauler/Veteran): with the avoidance layer on, the disc's
    /// hit volume overlapped a live enemy part for a MEDIAN OF 0.34 SECONDS PER
    /// 90 SECOND MATCH and landed 15 bites in 5 matches. With it off - nothing
    /// else changed - the same build landed 46 bites, dealt 427 damage a match
    /// instead of 160, took 31 enemy pieces off instead of 6, and went W4/L1
    /// instead of W1/D1/L3. It did that on LESS contact time (13.0 s vs 17.5 s
    /// inside 1.3 m), which is the whole point: the bottleneck was never how
    /// long you are near the enemy, it is whether the enemy ever meets the part
    /// you built to hurt it with.
    ///
    /// Every previous difficulty knob here raises APPROACH SPEED, and the
    /// critic's controlled ladder showed speed destroys contact, so the whole
    /// ladder ran backwards (Champion 4 ram hits where Rookie lands 17, same
    /// machine). This knob moves difficulty along an axis that is not speed:
    /// a Rookie has not learned to respect your weapon, a Champion has.
    ///
    /// Kept at 1.0 for Champion ON PURPOSE - the top of the ladder keeps
    /// exactly the shipped behaviour, so this change can only make the game
    /// easier at the bottom, never harder at the top.</summary>
    public static float WeaponRespect(AiTier t)
    {
        return t == AiTier.Rookie ? 0f : t == AiTier.Veteran ? 0.55f : 1f;
    }

    // ---- recipes ----------------------------------------------------------

    static P1PartDef D(P1PartDef[] palette, string id)
    {
        foreach (var d in palette) if (d.id == id) return d;
        return null;
    }

    static BuilderManager.PlacedPart P(P1PartDef def, float x, float y, float z, string mat)
    {
        return new BuilderManager.PlacedPart { def = def, pos = new Vector3(x, y, z), matName = mat };
    }

    static BuilderManager.PlacedPart W(P1PartDef def, float x, float y, float z, float ax)
    {
        return new BuilderManager.PlacedPart { def = def, pos = new Vector3(x, y, z),
                                               wheelAxis = new Vector3(ax, 0f, 0f) };
    }

    /// <summary>The shared skeleton: core + fore/aft chassis blocks + four
    /// wheels flush on the blocks' side faces. Chassis half-extents are
    /// 0.20,0.15,0.25, so |z| = 0.15 + 0.25 = 0.40 is flush on the core and
    /// |x| = 0.20 + 0.07 = 0.27 is flush for a wheel.</summary>
    static List<BuilderManager.PlacedPart> Skeleton(P1PartDef[] pal, string frameMat)
    {
        var core = D(pal, "core"); var chassis = D(pal, "chassis"); var wheel = D(pal, "wheel");
        return new List<BuilderManager.PlacedPart>
        {
            P(core,    0f, 0.70f,  0f,    frameMat),
            P(chassis, 0f, 0.70f,  0.40f, frameMat),
            P(chassis, 0f, 0.70f, -0.40f, frameMat),
            W(wheel,  0.27f, 0.70f,  0.40f,  1f),
            W(wheel, -0.27f, 0.70f,  0.40f, -1f),
            W(wheel,  0.27f, 0.70f, -0.40f,  1f),
            W(wheel, -0.27f, 0.70f, -0.40f, -1f),
        };
    }

    /// <summary>Recipe with STRUCTURE and ARMOUR rebuilt in `armourMat`, which
    /// is how a league scales its opponents (Contest.armourMat).
    ///
    /// WEAPONS are skipped on purpose. Disc damage is rotational energy, so a
    /// rim wants DENSITY; hit points are strengthRel * volume, so armour wants
    /// STRENGTH PER KILOGRAM. A blanket one-material pass - which is what the
    /// bench's MakeRef does for its value-class test - would drop WIDOWMAKER's
    /// tungsten rim to titanium and make the flagship weaker the harder the
    /// league got. Mobility is skipped too: wheels are rubber-pinned.
    ///
    /// One implementation, called by the fight spawn AND the scout preview, so
    /// SCOUT cannot show you a machine other than the one you will fight.</summary>
    public static List<BuilderManager.PlacedPart> Recipe(string id, P1PartDef[] pal, string armourMat, bool hardened = false)
    {
        var L = Recipe(id, pal);
        if (!string.IsNullOrEmpty(armourMat))
            foreach (var p in L)
            {
                if (p.def == null || !p.def.materialChoice) continue;   // pinned stays pinned
                if (p.def.category == P1Category.Weapon) continue;
                if (p.def.category == P1Category.Mobility) continue;
                p.matName = p.def.EffectiveMat(armourMat);
            }
        // HARDENED (Contest.hardened, 2026-08-12): gusset everything except
        // the core (no parent joint) and the wheels (raycast anchors, no
        // seams). This is the disarm sweep's measured x1.5 row applied to ONE
        // opponent — the CEILING loss mode is a two-below robot SHEDDING the
        // flagship's parts until "decided on structure destroyed", and
        // structFrac counts PIECES. Same pass as the armour reskin, so the
        // scout preview shows the machine you will actually fight.
        if (hardened)
            foreach (var p in L)
            {
                if (p.def == null || p.def.id == "core") continue;
                if (p.def.category == P1Category.Mobility) continue;
                p.reinforced = true;
            }
        return L;
    }

    /// <summary>Build one roster entry. `palette` is BuilderManager's own array,
    /// so these bots are made of exactly the parts the player can buy.</summary>
    public static List<BuilderManager.PlacedPart> Recipe(string id, P1PartDef[] pal)
    {
        var engine = D(pal, "engine"); var battery = D(pal, "battery"); var gyro = D(pal, "gyro");
        var spike = D(pal, "spike"); var spinner = D(pal, "spinner"); var plate = D(pal, "plate");
        var spindle = D(pal, "spindle");
        var beam = D(pal, "beam");

        // Mount heights used below, all derived, none guessed:
        //   core top      0.70 + 0.15 = 0.85
        //   chassis top   0.70 + 0.15 = 0.85
        //   battery on a 0.85 roof     -> 0.85 + 0.125 = 0.975
        //   engine  on a 0.85 roof     -> 0.85 + 0.125 = 0.975
        //   gyro    on a 0.85 roof     -> 0.85 + 0.12  = 0.97
        //   plate   on a 0.85 roof     -> 0.85 + 0.03  = 0.88
        //   nose of the front block    -> 0.40 + 0.25  = 0.65
        //   spike  half along its axis = 0.15 -> z 0.80
        //   spinner half along its axle = 0.05 -> z 0.70

        if (id == "bulwark")
        {
            var L = Skeleton(pal, "Steel");
            L.Add(P(plate,   0f, 0.88f,  0.40f, "Steel"));   // roof armour, front
            L.Add(P(plate,   0f, 0.88f, -0.40f, "Steel"));   // roof armour, rear
            L.Add(P(battery, 0f, 0.975f, 0f,    "Aluminum"));// core roof
            L.Add(P(engine,  0f, 0.70f, -0.875f, "Steel"));  // off the rear block's back face
            L.Add(P(gyro,    0f, 1.03f,  0.40f, "Aluminum"));// on the front roof plate
            L.Add(new BuilderManager.PlacedPart { def = spike, pos = new Vector3(0f, 0.70f, 0.80f),
                                                  wheelAxis = Vector3.forward, matName = "Steel" });
            return L;
        }
        if (id == "scout")
        {
            var L = Skeleton(pal, "ABS");
            L.Add(P(battery, 0f, 0.975f,  0f,    "Aluminum"));
            L.Add(P(engine,  0f, 0.975f, -0.40f, "Aluminum"));
            L.Add(P(gyro,    0f, 0.97f,   0.40f, "Aluminum"));
            L.Add(new BuilderManager.PlacedPart { def = spike, pos = new Vector3(0f, 0.70f, 0.80f),
                                                  wheelAxis = Vector3.forward, matName = "CarbonFiber" });
            return L;
        }
        if (id == "tipper")
        {
            // The gimmick IS the geometry: a stack of stood-up beams putting the
            // battery 2.2 m up, and deliberately NO gyro. Beam yaw 180 has
            // half-extents 0.10,0.30,0.10, so each one adds 0.60 m of height.
            var L = Skeleton(pal, "Aluminum");
            L.Add(new BuilderManager.PlacedPart { def = beam, pos = new Vector3(0f, 1.15f, 0f),
                                                  yaw = 180, matName = "Aluminum" });
            L.Add(new BuilderManager.PlacedPart { def = beam, pos = new Vector3(0f, 1.75f, 0f),
                                                  yaw = 180, matName = "Aluminum" });
            L.Add(P(battery, 0f, 2.175f, 0f,    "Aluminum"));
            L.Add(P(engine,  0f, 0.975f, -0.40f, "Aluminum"));
            // DISC CONVERSION (2026-07-27): a disc no longer spins by itself,
            // so the spindle that drives it is now part of the recipe. Derived
            // the same way every other height here is, not guessed:
            //   nose of the front block   z = 0.40 + 0.25 = 0.65
            //   spindle half 0.15         -> z 0.80, spans 0.65..0.95
            //   disc half along its axle 0.05 -> z 1.00, spans 0.95..1.05
            // The disc is COAXIAL with the spindle, so the limb turns in the
            // disc's own plane - a vertical spinner, which is exactly what this
            // bot presented before. Nothing but the spindle touches it, so the
            // spindle is the only route to the core and the limb is a limb.
            // ROTOR HEIGHT (2026-07-27, from round-1 dev's RotorFloorDip).
            // y was 0.70 and the rotor ground through the deck. The disc's
            // COLLIDER is a 0.34 x 0.34 BOX, not the 0.17-radius cylinder it
            // draws, so its corners sweep a 0.24 m radius: at 45 deg the box
            // reaches 0.24 m below the axle, not 0.17. Deck = wheel bottom
            // 0.52, GROUND_CLEAR 0.02 -> floor 0.54, and 0.70 - 0.24 = 0.46,
            // i.e. 0.08 m INSIDE it. 0.80 puts the worst sweep at 0.56, clear
            // by 0.02. Measured back as rotorDip 0.080 -> 0.000.
            L.Add(new BuilderManager.PlacedPart { def = spindle, pos = new Vector3(0f, 0.80f, 0.80f),
                                                  wheelAxis = Vector3.forward, matName = "Aluminum" });
            // C6 tune (owner-approved direction): the disc matches the rest of
            // the tower - ALUMINUM, not Steel. A steel disc on the SECOND
            // contest of the career out-classed every value-appropriate build
            // (C5/C6 probes: 0-1/6 for the same-class wedge, 300-800 dmg per
            // fight taken). The tipper's authored identity is the topple
            // gimmick; the disc is set dressing, and it now bites like the
            // Scrap-class bot it is. The CHAMPION widowmaker keeps tungsten -
            // flagships are supposed to be scary.
            L.Add(new BuilderManager.PlacedPart { def = spinner, pos = new Vector3(0f, 0.80f, 1.00f),
                                                  wheelAxis = Vector3.forward, matName = "Aluminum" });
            return L;
        }
        if (id == "widowmaker")
        {
            var L = Skeleton(pal, "CarbonFiber");
            L.Add(P(battery, 0f, 0.975f,  0f,    "Aluminum"));
            L.Add(P(engine,  0f, 0.975f, -0.40f, "Aluminum"));
            L.Add(P(gyro,    0f, 0.97f,   0.40f, "Aluminum"));
            // DISC CONVERSION (2026-07-27) - same derivation as tipper above.
            // The tungsten rim is still the point of this machine; it just has
            // to carry the axle that turns it now, like the player does.
            // Same rotor lift as tipper - see the derivation there.
            L.Add(new BuilderManager.PlacedPart { def = spindle, pos = new Vector3(0f, 0.80f, 0.80f),
                                                  wheelAxis = Vector3.forward, matName = "Aluminum" });
            L.Add(new BuilderManager.PlacedPart { def = spinner, pos = new Vector3(0f, 0.80f, 1.00f),
                                                  wheelAxis = Vector3.forward, matName = "Tungsten" });
            return L;
        }

        if (id == "millstone")
        {
            // The roster's missing mid-game answer: a DISC before L4. Steel,
            // not tungsten - tungsten is WIDOWMAKER's flagship identity and
            // the C6 probes showed a steel disc out-classing value-appropriate
            // builds when it arrived as early as L2. At L3, against a player
            // who has already fielded a disc of their own, it is a mirror
            // rather than an ambush.
            //
            // Aluminium frame: HP/kg 1.33, four times steel's. This machine is
            // meant to trade, so it cannot spend its whole budget on the rim.
            var Ms = Skeleton(pal, "Aluminum");
            Ms.Add(P(battery, 0f, 0.975f,  0f,    "Aluminum"));
            // Roof, not the rear face: AuditPowerMounting caught this hanging
            // on ONE seam off the back block, which is the exact defect the
            // roster exists to avoid - a power part one break from gone.
            // WIDOWMAKER mounts here for the same reason.
            Ms.Add(P(engine,  0f, 0.975f, -0.40f, "Aluminum"));
            // Gyro is NOT optional on a disc bot - the rotor's reaction torque
            // is what puts TIPPER on its roof, and this one is supposed to
            // survive its own weapon.
            Ms.Add(P(gyro,    0f, 0.97f,   0.40f, "Aluminum"));
            Ms.Add(new BuilderManager.PlacedPart { def = spindle, pos = new Vector3(0f, 0.80f, 0.80f),
                                                   wheelAxis = Vector3.forward, matName = "Aluminum" });
            Ms.Add(new BuilderManager.PlacedPart { def = spinner, pos = new Vector3(0f, 0.80f, 1.00f),
                                                   wheelAxis = Vector3.forward, matName = "Steel" });
            return Ms;
        }
        if (id == "bastion")
        {
            // The OTHER answer to a disc, and the one the roster never had: do
            // not out-bite it, outlast it.
            //
            // TITANIUM, and the correction is worth recording because I got it
            // wrong first. Carbon fibre wins HP PER KILOGRAM (4.13 vs steel's
            // 0.76) and that is the number I reached for - but HP is
            // strengthRel * VOLUME, and reskinning a fixed recipe does not add
            // volume. So carbon fibre bought +10% hit points and -65% mass:
            // a LIGHTER bot, not a tougher one. Weight is not the binding
            // constraint here (this sits at ~1.3 t under a 4 t cap), so what
            // matters is absolute strength, and titanium leads it at 1.4.
            // HP/kg is the right metric only when you spend the saved weight
            // on more armour, which a fixed recipe cannot.
            //
            // Four roof plates rather than BULWARK's two, on BULWARK's own
            // proven mounts - authored geometry that has never been physically
            // verified is how you ship an opponent with parts touching nothing.
            //
            // Its weapon is a TUNGSTEN spike rather than a disc on purpose:
            // this machine should never win a damage race. It should still be
            // standing, which under the fight rules is enough.
            var B = Skeleton(pal, "Titanium");
            B.Add(P(plate,   0f, 0.88f,  0.40f, "Titanium"));   // roof, front
            B.Add(P(plate,   0f, 0.88f, -0.40f, "Titanium"));   // roof, rear
            B.Add(P(plate,  0.28f, 0.88f, 0f,   "Titanium"));   // roof, left spine
            B.Add(P(plate, -0.28f, 0.88f, 0f,   "Titanium"));   // roof, right spine
            B.Add(P(battery, 0f, 0.975f, 0f,    "Aluminum"));
            B.Add(P(engine,  0f, 0.70f, -0.875f, "Titanium"));
            B.Add(P(gyro,    0f, 1.03f,  0.40f, "Aluminum"));      // on the front roof plate
            // STAGE B (2026-08-12): the PASSIVE spike is now a POWERED LANCE.
            // Measured first: gussets alone took CEILING L4 only 92% -> 79%,
            // because the two-below chip build mostly wins on the DAMAGE
            // criterion, which seam strength cannot touch — BASTION's "still
            // standing is enough" premise loses to the verdict cascade when
            // its weapon never lands. A ram-driven tungsten spike keeps the
            // identity (never wins a damage RACE) but punches back hard
            // enough that 90 s of free chipping is no longer free.
            var ramA = D(pal, "ram");
            B.Add(new BuilderManager.PlacedPart { def = ramA, pos = new Vector3(0f, 0.70f, 0.80f),
                                                  wheelAxis = Vector3.forward, matName = "Aluminum" });
            B.Add(new BuilderManager.PlacedPart { def = spike, pos = new Vector3(0f, 0.70f, 1.10f),
                                                  wheelAxis = Vector3.forward, matName = "Tungsten" });
            return B;
        }

        if (id == "ripper")
        {
            // Phase 4 archetype opponent. Every height here is derived from the
            // skeleton the same way the others are, and the limb geometry is
            // chosen so the PIVOT IS THE ONLY ROUTE TO THE CORE - the one rule
            // that decides whether an actuated build works at all:
            //   front chassis roof y = 0.85, pivot half 0.15   -> pivot y 1.00
            //   pivot spans y 0.85..1.15, z 0.25..0.55, x +/-0.15
            //   beam half (0.10,0.10,0.30) on the pivot's +Z face -> z 0.85,
            //     spanning y 0.90..1.10 - i.e. 0.05 m CLEAR of the 0.85 roof,
            //     so it touches the pivot and nothing else
            //   blade half (0.25,0.025,0.06) on the beam's +Z face -> z 1.21
            // Axis is the mount normal (+Y, the roof), so this is a horizontal
            // 150 deg sweep across the front - it meets the opponent's roof
            // furniture, which on this skeleton is where the battery lives.
            // Aluminium frame and a Steel blade keep it a mid-weight machine:
            // this is meant to be a fair fight, not a new hardest opponent.
            var pivot = D(pal, "pivot"); var blade = D(pal, "blade");
            var R = Skeleton(pal, "Aluminum");
            R.Add(P(battery, 0f, 0.975f,  0f,     "Aluminum"));
            R.Add(P(gyro,    0f, 0.97f,  -0.40f,  "Aluminum"));
            R.Add(P(engine,  0f, 0.70f,  -0.875f, "Aluminum"));   // rear block's back face
            R.Add(new BuilderManager.PlacedPart { def = pivot, pos = new Vector3(0f, 1.00f, 0.40f),
                                                  wheelAxis = Vector3.up, matName = "Aluminum" });
            R.Add(P(beam,    0f, 1.00f,   0.85f,  "Aluminum"));
            R.Add(new BuilderManager.PlacedPart { def = blade, pos = new Vector3(0f, 1.00f, 1.21f),
                                                  wheelAxis = Vector3.forward, matName = "Steel" });
            return R;
        }

        // mauler (default): the Phase 2B baseline.
        var M = Skeleton(pal, "Aluminum");
        M.Add(P(engine,  0f, 0.975f,  0.40f, "Aluminum"));
        // ROUND-6 FIX 4. The battery used to sit alone on the REAR chassis roof
        // at z -0.40, where its footprint abutted exactly ONE part, so it hung
        // on a single 900 N.s seam - the weakest joint this game can make -
        // while carrying 80% of the machine's stored energy. Measured: it
        // sheared off in 17 of 17 timeScale-1 matches and every timeScale-3
        // match, taking capacity 300 kJ -> 60 kJ, usually before t = 10 s. The
        // "fair fight" reference opponent was therefore a motionless rock for
        // most of every match, and the apparent difficulty of a tier was really
        // just how fast the AI killed itself.
        //
        // A battery's own face is 0.25 m, BELOW one SOCKET_PITCH plus its
        // margin, so it can never earn a multi-socket seam no matter what it is
        // bolted to - re-siting it onto a wider face would not have helped. The
        // only honest lever is REDUNDANCY. The core roof and both chassis roofs
        // share the y = 0.85 plane, so a battery centred there abuts the core
        // AND both chassis blocks: three independent seams, no interpenetration,
        // still exactly on the socket grid, and no change to mass, HP, damage or
        // joint strength (section 8: difficulty may never come from stats). The
        // gyro moves back one socket to make room and picks up the same
        // redundancy - 3 seams instead of 2.
        M.Add(P(battery, 0f, 0.975f,  0f,    "Aluminum"));
        M.Add(P(gyro,    0f, 0.97f,  -0.25f, "Aluminum"));
        M.Add(new BuilderManager.PlacedPart { def = spike, pos = new Vector3(0f, 0.70f, 0.80f),
                                              wheelAxis = Vector3.forward, matName = "Steel" });
        return M;
    }
}
}
