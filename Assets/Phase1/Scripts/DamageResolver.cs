using UnityEngine;

namespace RobotBrawl.Phase0
{
/// <summary>
/// Phase 2A — the single owner of the HP/damage math (design doc §4.2, §7).
///
/// Every part carries durabilityHP = HP_K × material.strengthRel × volume_m³.
/// Damage ablates HP; at ≤0 the part is DESTROYED (CompoundRobot.DestroyPart:
/// debris shards + connectivity recompute). This runs IN PARALLEL with the
/// Phase 0 joint-stress/shear system, which stays untouched — a clean hit can
/// still shear a part off with zero HP bookkeeping.
///
/// Units are game units ("impulse-equivalent"): the spinner feeds drained
/// rotational energy (J) + a linear impulse term; rams feed the engine's
/// contact impulse (N·s) after a floor and cap. One DMG_K converts either
/// into HP loss.
///
/// Calibration (HP_K = 6000, DMG_K = 0.045):
///   aluminum beam 0.2×0.2×0.6  → 86.4 HP  (spec band 60–120)
///   full-spin spinner bite ≈ (0.4×2913 J + linImp)×1.5×0.045 ≈ 80–95 dmg
///     → ABS parts 1 hit, aluminum beam 1–2, steel beam-class (144 HP) 3–5
///       with re-spin-up between bites
///   6 m/s spike ram ≈ min(J−150, 900)×1.2×0.045 ≤ 48.6 dmg → ≥20% of any
///     aluminum part; sub-150 N·s nudges are free; walls aren't robots → 0.
/// </summary>
public static class DamageResolver
{
    /// <summary>durabilityHP = HP_K × strengthRel × volume_m³.</summary>
    public static float HP_K = 6000f;
    /// <summary>damage = effImpulse × edgeHardness × DMG_K.</summary>
    public static float DMG_K = 0.045f;

    /// <summary>Ram contact impulses at or below this (N·s) are free — resting
    /// contact, curb taps, gentle nudges. Well above CompoundRobot.MIN_IMPULSE
    /// (40) so the stress system keeps its own sensitivity.</summary>
    public static float RAM_MIN_J = 150f;

    /// <summary>=========== BUMPS ARE NOT ATTACKS (owen, 2026-07-27) ==========
    /// "Make sure that simple bumps don't cause damage nor knock off parts.
    ///  Only weapon attack deals HP and potentially knock off parts."
    ///
    /// The striking part's `edgeHardness` already IS this codebase's definition
    /// of "is this a weapon": AIController skips anything &lt;= 1.01 as structure
    /// when it builds its threat model, and HpOf() gives anything above it the
    /// EDGE_MIN_VOL floor. So the gate costs no new concept - a bare chassis
    /// box, a beam, a plate and a battery are 1.0; every catalogue weapon,
    /// INCLUDING a fixed edge with no motor behind it, is above it.
    ///
    /// Note carefully what this does NOT change. A spike bolted to the nose is
    /// a weapon at 1.2 and still bills through this path; so does a wedge at
    /// 1.15 and a frame-bolted disc at 1.5. RAM_MIN_J is the wrong lever for
    /// owen's rule and was measured to be actively harmful (see
    /// Lever_Pass_2026-07-27) precisely because it cannot tell a spike from a
    /// box - it only sees the impulse.
    ///
    /// Both multipliers are static rather than const so a tuning pass can
    /// restore a fraction of hull damage without a recompile.</summary>
    public static float EDGE_MIN_HARDNESS = 1.01f;
    /// <summary>Multiplier on HP damage when the STRIKING part is structure.
    /// 0 = owen's rule as stated: a bump does no HP at all.</summary>
    public static float STRUCT_RAM_DMG = 0f;
    /// <summary>Multiplier on SEAM STRESS when the striking part is structure
    /// and the contact is robot-vs-robot. 0 = a bump cannot shear anything off.
    /// The ENVIRONMENT path is deliberately untouched: a wall or a bad landing
    /// still stresses the frame, already scaled by STRESS_ENV_SCALE.</summary>
    public static float STRUCT_SEAM_MUL = 0f;

    /// <summary>Is this striking part a weapon at all?</summary>
    public static bool IsEdge(float hardness) { return hardness > EDGE_MIN_HARDNESS; }
    /// <summary>Post-floor cap on the ram impulse fed to damage — no physics
    /// spike can one-shot a healthy part.</summary>
    public static float RAM_J_CAP = 900f;
    /// <summary>Round-1 fix 7 (pacing): ONE shared per-victim-part post-hit
    /// immunity window covering rams AND spinner bites — replaces the old
    /// ram-only RAM_COOLDOWN (0.25) and stops brush-contact grinding from
    /// stripping a bot in seconds. Checked and stamped in ApplyHit.</summary>
    public static float PART_IMMUNITY = 0.5f;

    /// <summary>ROUND-2-CRITIC FIX (CRITICAL 3: "being toppled is currently a
    /// DEFENSIVE state - lying on your back cuts damage taken by 62% and stops
    /// structural loss"). Multiplier on damage taken while flipped.
    ///
    /// WHY THE PROTECTION EXISTS AT ALL, which the finding does not say and
    /// which decides what the fix has to be: most damage in this game is
    /// symmetric ram damage - RamHit bills BOTH bodies for a collision - so a
    /// machine that is not driving is not paying for collisions. Being on your
    /// back is therefore an automatic 60% discount on the dominant damage
    /// source, bought with nothing but the offence you were not landing anyway.
    /// The critic measured 226 damage taken across 46.2 s inverted against 601
    /// upright, keeping 13 of 13 pieces every match.
    ///
    /// WHY 1.75. An upright robot presents wheels, armour plate and structural
    /// beams; an inverted one presents its belly pan, its battery bay and its
    /// unarmoured wheel wells, so an exposure term is honest as well as
    /// corrective. 1.75 is sized to CANCEL the discount rather than to invert
    /// it: 226 x 1.75 = 396, still short of the 601 an upright machine eats, so
    /// getting knocked over remains survivable - it just stops being the best
    /// available play. Deliberately not applied to the attacker's own output
    /// (a flipped robot already deals ~0 because it cannot drive), so this is
    /// one lever with one effect and the sweep can attribute it.
    ///
    /// Paired with FightManager's control criterion, which makes time on your
    /// back cost you the judges' card. Neither alone was enough: a multiplier
    /// only bites if the opponent bothers to hit you, and the card only bites
    /// if the match reaches the judges.</summary>
    public static float EXPOSED_MULT = 1.75f;

    /// <summary>Cap (N·s) on the clamped physical shove a spinner bite applies
    /// — the visible knockback can never explode the solver.</summary>
    public static float SHOVE_CAP = 500f;

    /// <summary>========= WEAPON ON WEAPON (owen, 2026-08-08) =============
    /// Multiplier on HP damage when a WEAPON part is struck by a WEAPON part.
    ///
    /// Measured problem (`Ladder_Sweep_Weapon_Trade_2026-08-08.md`, 30 bouts
    /// across every preset pairing): the weapon was the most fragile thing in
    /// the game. 26/30 bouts ended with a weapon destroyed and 15/30 with BOTH
    /// sides disarmed; every mirror match was a deterministic mutual kill in
    /// four hits — two spinners meeting head-on traded exactly their own HP
    /// (121.5 each) and both died inside five seconds. All 11 bouts with an
    /// asymmetric weapon count were won by the side that still had one, with
    /// the loser dismantled 18-19 parts of 19. So the match was decided in the
    /// first 1.4 seconds by approach geometry, and the remaining 85 seconds
    /// were two disarmed robots shoving.
    ///
    /// The fix is deliberately narrow: it does NOT make weapons tougher in
    /// general (losing a weapon to a determined opponent is a legitimate and
    /// readable outcome, and armour-vs-weapon is the sport). It only says that
    /// two hardened edges meeting each other is a glancing, sparking exchange
    /// rather than a mutual kill — which is also what real robot combat looks
    /// like. Weapon-vs-BODY damage is untouched.
    ///
    /// Both sides of the test use `IsEdge`, which is already this codebase's
    /// definition of "is this a weapon" (AIController's threat model and
    /// STRUCT_RAM_DMG both key off it), so there is no second notion of
    /// weapon-ness to drift.
    ///
    /// 0.25 means a spinner survives four exchanges with another spinner
    /// instead of two — swept over the same 30 bouts before and after via
    /// LadderSweepBench, which is how any change to this number should be
    /// argued.</summary>
    public static float WEAPON_VS_WEAPON = 0.25f;

    /// <summary>Floating-damage-number hook for Phase 2B:
    /// (worldPos, amount, destroyedFlag, victimPartKey). Fired once per
    /// applied hit; the key (the victim's Part object) lets the HUD aggregate
    /// rapid ticks on the same part into one growing number (round-1 fix 6).</summary>
    public static System.Action<Vector3, float, bool, object> OnHit = delegate { };

    /// <summary>Diagnostic hook, fired ONLY when a part is actually destroyed:
    /// (victim, attacker, victimPart, attackerHardness, src).
    ///
    /// Why a second hook rather than more arguments on OnHit. OnHit fires on
    /// every applied tick and feeds the HUD; this fires a handful of times per
    /// bout and feeds benches, so it can afford to carry the whole context —
    /// crucially the ATTACKER'S hardness, which OnHit does not have and which
    /// is the difference between "a weapon was traded with another weapon" and
    /// "a weapon was knocked off by a chassis".
    ///
    /// That distinction is the open question. `WEAPON_VS_WEAPON` only applies
    /// when BOTH sides are edges (see the multiply below), and shipping it at
    /// 0.25 moved the ladder's mutual-disarm rate by ZERO bouts — 13/30 before
    /// and after. If the ladder's weapons are mostly being lost to non-edge
    /// contact, that null result is explained and the lever was never the one
    /// that mattered. Nothing could answer it from the outside: the fight
    /// outcome does not record what struck what.
    ///
    /// Defaults to a no-op and is invoked only on destruction, so it changes
    /// no behaviour and costs nothing in a normal fight.</summary>
    public static System.Action<object, object, object, float, int> OnPartDestroyed = delegate { };

    /// ================= ROUND-3-CRITIC MAJOR =================================
    /// "The Phase 4 edge parts are the most fragile things in the game - a blade
    ///  of any material is one-shot by a mid-range hit."
    ///
    /// The cause is right here: hp is HP_K x strengthRel x VOLUME and nothing
    /// else, so a part's durability is decided by how much space it takes up.
    /// That is a reasonable rule for structure and a perverse one for an edge,
    /// whose whole design is to be thin. Catalogue volumes (m3):
    ///   blade 0.0030 | hook 0.0096 | spinner 0.0116 | spinnerSaw 0.0127
    ///   spike 0.0145 | wedge 0.0210        vs chassis 0.0600, core 0.0270
    /// The blade - the hardest edge in the game, edgeHardness 1.7, the part the
    /// player puts on the END of a limb - is a 4x outlier at the bottom, giving
    /// 18 hp in Steel and 25 in Titanium against measured limb hits of 15 to
    /// 229. The critic watched a Titanium blade get HP-DESTROYED inside 1.5 s in
    /// 5 of 6 runs, INCLUDING an AFK run where the player dealt zero damage and
    /// never fired; I reproduced the Steel one dying in my own before-set. Most
    /// of every hammer match is fought with a bare beam and the player is never
    /// told.
    ///
    /// THE FIX IS A FLOOR, NOT A MULTIPLIER, and the volume table is why. A
    /// multiplier would lift every weapon including the wedge (already the
    /// second-toughest part on the machine) and would leave the blade's 4x gap
    /// to the rest of the catalogue exactly where it is. A floor closes the gap
    /// and touches almost nothing else: blade 0.0030 -> 0.0120 (x4.0),
    /// hook 0.0096 -> 0.0120 (x1.25), spinner 0.0116 -> 0.0120 (x1.03), and
    /// spinnerSaw, spike and wedge are unchanged. It is, in effect, a blade fix,
    /// which is what the measurement says is wrong.
    ///
    /// WHY 0.012 m3. It is the smallest value that puts the blade at parity with
    /// the LEGACY DISC CLASS (spinner 0.0116 / spinnerSaw 0.0127) - the game's
    /// own statement of how tough a spinning steel weapon is - rather than an
    /// invented number. Resulting blade hp: ABS 25 / Alu 43 / Steel 72 /
    /// CarbonFiber 79 / Tungsten 86 / Titanium 101, against a chassis 126-504.
    /// An edge is still the softest thing on the machine, as it should be; it
    /// now survives its own second swing, and - the part that was actually
    /// broken - the material the player paid for now decides that, instead of
    /// duty cycle deciding it (the critic's cross-check: a Steel blade at 18 hp
    /// outlived a Titanium blade at 25 hp purely because its heavier arm made
    /// fewer contacts).
    ///
    /// Keyed on edgeHardness > 1.01 because that is ALREADY this codebase's
    /// literal definition of "this part is a weapon" - AIController skips
    /// everything at or below it as structure, and Phase1Parts' own round-1 fix
    /// note calls 1.01 "the line that separates weapon from structure". No new
    /// concept is introduced and no part list has to be kept in sync.
    /// <summary>Floor on the effective volume of a WEAPON part, m3.</summary>
    public static float EDGE_MIN_VOL = 0.012f;

    /// <summary>Per-part durability from the §4.2 formula.</summary>
    public static float HpOf(PartSpec s)
    {
        MatDef m = MatDB.Get(s.mat);
        float vol = s.size.x * s.size.y * s.size.z;
        if (s.edgeHardness > 1.01f) vol = Mathf.Max(vol, EDGE_MIN_VOL);
        return HP_K * m.strengthRel * vol;
    }

    /// <summary>
    /// Ablate HP on victim's part. effImpulse is already floored/capped by the
    /// caller (RamHit) or is the spinner's drained-energy + linear term.
    /// Updates damageDealt/damageTaken, fires OnHit, queues part destruction
    /// at ≤0 HP (CompoundRobot defers the actual split to its FixedUpdate —
    /// never mutate the compound inside a physics callback).
    /// </summary>
    /// <summary>ROUND-1-IMPL instrument: which weapon system produced this hit.
    /// 0 = chassis ram, 1 = actuated limb, 2 = spinner disc. Damage numbers are
    /// unaffected; this only decides which counter on the attacker it lands in.
    /// See CompoundRobot.dealtRam/dealtLimb/dealtDisc for why.</summary>
    /// <summary>SRC_DISC is RETIRED as of the 2026-07-27 disc conversion: the
    /// only writer was SpinnerWeapon, which nothing attaches any more. A disc is
    /// an unpowered rotor edge driven by a spindle, so it reports on SRC_LIMB
    /// like every other actuated weapon. The constant stays so the historical
    /// sweep files still parse against the same numbering.</summary>
    public const int SRC_RAM = 0, SRC_LIMB = 1, SRC_DISC = 2;

    /// <summary>RB1-DEV instrument (measurement only, no behaviour): how many
    /// hits of each source the shared PART_IMMUNITY window swallowed vs passed.
    /// A swallowed limb bite still pays Actuator.Bite's energy drain.</summary>
    public static int immEatLimb, immEatRam, immPassLimb, immPassRam;
    public static void ImmReset() { immEatLimb = immEatRam = immPassLimb = immPassRam = 0; }

    public static void ApplyHit(CompoundRobot attacker, CompoundRobot victim, int victimIdx,
                                float effImpulse, float hardness, Vector3 worldPos, int src = SRC_RAM)
    {
        if (victim == null || victim.dead) return;
        // Spawn protection (round-1 fix 1/5): a settling bot neither takes
        // nor deals damage — nothing before the bell can touch HP or stats.
        if (!victim.combatEnabled) return;
        if (attacker != null && !attacker.combatEnabled) return;
        if (victimIdx < 0 || victimIdx >= victim.parts.Count) return;
        var p = victim.parts[victimIdx];
        if (p.detached) return;
        // Shared post-hit immunity (round-1 fix 7): rams and spinner bites
        // rate-limit through the SAME per-part window.
        if (Time.time - p.lastHitTime < PART_IMMUNITY)
        {
            if (src == SRC_LIMB) immEatLimb++; else immEatRam++;
            return;
        }
        p.lastHitTime = Time.time;
        if (src == SRC_LIMB) immPassLimb++; else immPassRam++;

        float dmg = effImpulse * hardness * DMG_K;
        // Weapon on weapon is a glancing exchange, not a mutual kill (2026-08-08).
        if (IsEdge(hardness) && IsEdge(p.spec.edgeHardness)) dmg *= WEAPON_VS_WEAPON;
        // Round-2-critic CRITICAL 3: the underside is not armour.
        if (victim.Flipped) dmg *= EXPOSED_MULT;
        if (dmg <= 0f) return;

        p.hp -= dmg;
        victim.damageTaken += dmg;
        if (attacker != null)
        {
            attacker.damageDealt += dmg;
            if (src == SRC_LIMB)
            {
                attacker.dealtLimb += dmg; attacker.hitsLimb++;
                if (dmg > attacker.maxLimbHit) attacker.maxLimbHit = dmg;
            }
            else if (src == SRC_DISC) { attacker.dealtDisc += dmg; attacker.hitsDisc++; }
            else                      { attacker.dealtRam  += dmg; attacker.hitsRam++;  }
        }

        bool destroyed = p.hp <= 0f;
        CompoundRobot.Log(victim.name + ": " + p.spec.id + " took " + dmg.ToString("F1")
            + " dmg (hp " + Mathf.Max(0f, p.hp).ToString("F1") + "/" + p.maxHp.ToString("F1")
            + (destroyed ? ") — DESTROYED" : ")"));
        OnHit(worldPos, dmg, destroyed, p);

        // Fired BEFORE DestroyPart so a listener can still read the part.
        if (destroyed) OnPartDestroyed(victim, attacker, p, hardness, src);

        if (destroyed) victim.DestroyPart(victimIdx);
    }

    /// <summary>Rammer path (§7): floor so nudges are free, cap so no
    /// one-shots. attackerHardness = the striking part's edgeHardness
    /// (1.0 structure, 1.2 spike).</summary>
    public static void RamHit(CompoundRobot attacker, float attackerHardness,
                              CompoundRobot victim, int victimIdx, float J, Vector3 worldPos,
                              float causalShare = 1f)
    {
        if (J <= RAM_MIN_J) return;
        // BUMPS ARE NOT ATTACKS. Gated HERE rather than at the one call site so
        // that every future caller of RamHit inherits the rule - the one-side-
        // only fix is this project's signature failure and it has cost four
        // separate rounds.
        if (!IsEdge(attackerHardness))
        {
            if (STRUCT_RAM_DMG <= 0f) return;
            attackerHardness *= STRUCT_RAM_DMG;
        }
        // Round-7 fix 1: causalShare is the fraction of the closing speed the
        // ATTACKER brought to the contact (CompoundRobot.Accumulate). It scales
        // the EFFECTIVE impulse, after the nudge floor and the anti-one-shot
        // cap, so it changes who is billed and not how a ram is calibrated.
        float eff = Mathf.Min(J - RAM_MIN_J, RAM_J_CAP) * Mathf.Clamp01(causalShare);
        // Bail before ApplyHit when nothing lands: ApplyHit stamps the shared
        // per-part immunity window, and a zero-damage contact must not consume
        // the window that a real hit needs.
        if (eff <= 0f) return;
        ApplyHit(attacker, victim, victimIdx, eff, attackerHardness, worldPos);
    }
}

}