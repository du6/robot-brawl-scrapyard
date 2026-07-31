using System.Collections.Generic;
using UnityEngine;

namespace RobotBrawl.Phase0
{

/// <summary>
/// Spec for one hard-coded part. Phase 0 skips ScriptableObjects on purpose
/// (design doc §12, Phase 0) — parts are code-defined boxes.
/// </summary>
public struct PartSpec
{
    public string id;
    public Vector3 size;      // meters
    public Vector3 localPos;  // relative to robot root
    public string mat;        // key into MatDB
    public bool isCore;       // the KO / connectivity anchor part
    // Phase 2A (§7): damage multiplier when this part is the striking one
    // (1.0 structure, 1.2 spike, 1.5 spinner).
    public float edgeHardness;
    // Phase 2A: >0 overrides the solid-box mass (weapons are billed at half a
    // solid block in the builder — the arena must agree).
    public float massOverride;

    public PartSpec(string id, Vector3 size, Vector3 localPos, string mat, bool isCore = false,
                    float edgeHardness = 1f, float massOverride = 0f)
    {
        this.id = id; this.size = size; this.localPos = localPos; this.mat = mat; this.isCore = isCore;
        this.edgeHardness = edgeHardness; this.massOverride = massOverride;
    }
}

/// <summary>
/// THE Phase 0 system under test (design doc §5.2–5.3).
///
/// The fused structure is ONE compound Rigidbody (child box colliders, no
/// physics joints between structural parts). Connections are "virtual joints"
/// in a graph: each fixed step we take the engine's contact impulses on the
/// compound body, attribute stress to nearby connections with a distance
/// falloff, and when a connection's stress exceeds its threshold we break it
/// and dynamically split the body — severed parts re-spawn as their own
/// rigidbodies inheriting velocity (linear + ω×r), and the survivor's
/// mass/center-of-mass are recomputed (so losing a chunk changes handling).
/// </summary>
public class CompoundRobot : MonoBehaviour
{
    // ------------------------------------------------------------------ tuning
    // Static (not const) so scripted tuning passes can iterate live; values
    // below are the round-2 MEASURED calibration (see dev notes): a ~5-6 m/s
    // cruise ram into the dummy costs at most one peripheral part, a ~10 m/s
    // full-throttle wall crash still shears convincingly.
    // Impulse threshold (N·s) per unit of relative material strength.
    // THE key Phase 0 tuning knob: lower = parts tear off easier.
    public static float BREAK_K = 1500f;
    // How sharply stress attribution falls off with distance (m) from a
    // contact point to a connection midpoint. Higher = more localized damage.
    public static float STRESS_FALLOFF = 6f;
    // Ignore tiny contact impulses (rolling, resting contact).
    public static float MIN_IMPULSE = 40f;
    // Round-1 fix 7 (pacing): the contact impulse feeding the STRESS system is
    // capped before attribution. Measured: a mutual full-speed ram produced
    // depenetration impulse spikes so large that joints a full METER from the
    // contact read 1200-2600 N·s (thresholds ~900) — one exchange sheared 20
    // of 21 parts. The cap keeps a big hit breaking the joints NEAR the
    // contact (2200 at d=0 still beats any ×1 aluminum seam) while distant
    // seams survive the falloff — damage localizes instead of totaling the bot.
    // Measured pacing: 2200 still cost 6 parts + 30% HP in the opening mutual
    // ram; 1400 breaks only single-socket seams within ~0.3 m of the contact.
    public static float STRESS_J_CAP = 1400f;
    // ================= ROUND-3-CRITIC CRITICAL 1 =============================
    // "Seam-stress is saturated: 87% of all structural failures occur at exactly
    //  STRESS_J_CAP, so damage above 1400 N.s is invisible to structure."
    // Reproduced before touching anything: b6 (Steel hammer) vs mauler/Veteran,
    // n=4, 21 of the 26 shear lines in the ledger read EXACTLY 1400 (robot) or
    // EXACTLY 1050 (= 1400 x STRESS_ENV_SCALE, environment), and all four runs
    // opened with a byte-identical pair of MAULER seam failures. The clamp
    // WAS the physics.
    //
    // WHY THE CLAMP CANNOT SIMPLY BE RAISED. Measured raw contact impulses over
    // those same four matches (CompoundRobot.telemetry*): 8372 contacts above
    // MIN_IMPULSE, mean 286.5 N.s, and a PEAK OF 29251 N.s - 20.9x the clamp,
    // from a solver depenetration spike (PlayerBuild vs an arena cube). Any
    // constant large enough to let a heavy weapon express itself is also large
    // enough to let that spike dismember a machine. The clamp is holding back a
    // 20x outlier, so what is needed is a compressor, not a bigger number.
    //
    // THE SHAPE. Identity below the knee - every load this project has ever
    // calibrated (seam thresholds, HOP_ATTEN, STRESS_ENV_SCALE, the 0.35/900
    // pacing work) sits under 1400 and is therefore bit-identical to before
    // this change. Above the knee, load grows as a square root, so impulse
    // keeps mattering but with sharply diminishing returns, and a hard ceiling
    // catches the tail.
    //   J    1400 -> 1400   (knee, continuous)
    //   J    2800 -> 1980   (2x the impulse buys 1.41x the seam load)
    //   J    5600 -> 2800
    //   J   12600 -> 4200   (ceiling)
    //   J   29251 -> 4200   (the measured solver spike, fully caught)
    //
    // WHY THIS IS SAFE NOW AND WAS NOT WHEN 1400 WAS CHOSEN. The 1400 comment
    // above records a calibration against EUCLIDEAN stress attribution - one
    // contact loading every seam inside a sphere. Round-3 fix 2 replaced that
    // with hop attenuation, round-4 fix 1 made a collision event fail AT MOST
    // ITS SINGLE MOST-OVERLOADED SEAM, and BREAK_COOLDOWN rate-limits the body.
    // Cascade extent is therefore controlled by those three rules, not by
    // amplitude, and the amplitude clamp has been over-doing a job it no longer
    // has. Checked against the surviving amplitude-sensitive path, HOP_ATTEN:
    // at the 4200 ceiling one hop out sees 1470 (can fail a 900 single-socket
    // seam - a colossal hit SHOULD travel one joint) and two hops out sees 514,
    // under even ABS's 525 floor, so nothing propagates past the second joint.
    //
    // WHY 3.0x FOR THE CEILING. The strongest seam class in the project is the
    // 3-socket 2700 (see the STRESS_ENV_SCALE calibration note). A ceiling of
    // 4200 is the smallest round multiple of the knee that puts a genuinely
    // enormous contact ABOVE that class, which is precisely the outcome the
    // finding says is missing: today no hit of any size can fail a well-braced
    // seam, so bracing is free and weapon energy is irrelevant to structure.
    /// <summary>Knee exponent above STRESS_J_CAP. 0.5 = square root.</summary>
    public static float STRESS_SOFT_EXP = 0.5f;
    /// <summary>Hard ceiling on compressed seam load. 3.0x the knee.</summary>
    public static float STRESS_J_HARD = 4200f;

    /// <summary>Round-3 fix: raw contact impulse -> seam load. Identity below
    /// the knee, sqrt-compressed above it, hard-ceilinged at STRESS_J_HARD.</summary>
    public static float SeamLoad(float J)
    {
        if (J <= STRESS_J_CAP) return J;
        return Mathf.Min(STRESS_J_HARD,
                         STRESS_J_CAP * Mathf.Pow(J / STRESS_J_CAP, STRESS_SOFT_EXP));
    }

    // ================= ROUND-3-CRITIC CRITICAL 3 =============================
    // "Both robots shed parts on spawn, every match, with nobody touching them."
    // Reproduced 3/3 in an AFK cell (throttle 0, steer 0, fire off, player dealt
    // 0.00 in every run): MAULER chassis_1<->spikeZP_6 SHEARS at 1050 vs eff 900
    // and the spike leaves at hp 87/87 - ONE HUNDRED PERCENT HEALTH - inside the
    // first second of every match. 1050 is the clamp times STRESS_ENV_SCALE,
    // i.e. the spawn drop's floor impact arriving already saturated, and it
    // clears the 900 threshold of any single-socket Aluminium seam. Structure is
    // ranked ABOVE damage by FightManager.Judge, so essentially every match in
    // this game was being seeded with a piece differential nobody earned.
    //
    // SETTLE_TIME (1.2 s) was supposed to cover this and does not: it protects
    // the drop, then hands over a body that is still settling on its raycast
    // suspension, and the impact that matters lands just after the bell.
    //
    // WHY A RAMP AND NOT A LONGER SETTLE. A conditional bell ("start when both
    // bots are at rest") makes the start of the match build-dependent and adds
    // a second knob to attribute; this is one constant with one effect. It
    // scales the ENVIRONMENT term ONLY - robot-vs-robot stress is live from the
    // first frame after the bell - so it cannot make an opening exchange free,
    // and it is 0.8% of a 90 s match.
    //
    // WHY 0.75 s. The measured shear lands ~0.4 s after the bell (the enemy's
    // own first ram on the parked player follows at ~2.8 s, and is unaffected).
    // 0.75 is ~2x that margin, chosen to cover a heavier build settling slower
    // without reaching the first exchange. Verified by re-running the same AFK
    // cell after the change.
    /// <summary>Seconds after combat arms over which ENVIRONMENT stress ramps
    /// from 0 to STRESS_ENV_SCALE. The floor is not an opponent.
    ///
    /// HONEST NOTE, because the reasoning above was written before the
    /// measurement: this ramp DID NOT fix the finding it was written for. A/B at
    /// n=6, 0 s vs 0.75 s, changed nothing (6/6 sheds either way) because the
    /// impact that removes the part lands ~2.0 s after the bell and is a wall
    /// crash, not the spawn drop - see the STRESS_ENV_SCALE note below, which is
    /// the change that actually fixed it. It is kept because it is still correct
    /// on its own terms (a body that is still settling has not been hit by
    /// anything and should not be stressed for it), it costs one Clamp01 per
    /// environment contact, and it is the guard that stops a future heavier
    /// build from re-introducing a genuine settle shear. It is NOT load-bearing
    /// for any result in this round's report.</summary>
    public static float ENV_ARM_RAMP_S = 0.75f;
    // Round-3 fix 2 (critic finding 2: "a single impact strips a third of the
    // robot while dealing ~1% damage"). Stress used to be attributed by
    // EUCLIDEAN distance from the contact point to each seam midpoint, so one
    // contact loaded every joint inside a ~0.3 m sphere to near-full impulse and
    // a whole cluster popped at once (measured: 14 parts -> 9 in ONE frame from
    // ONE broken seam, survivors at 98.8% HP). Load now enters at the part that
    // was actually struck and is attenuated once per JOINT it crosses, which is
    // the load path the player built.
    // Per-joint attenuation: 0.35 means the next seam out sees a third of what
    // the struck seam sees, so a hit fails the weak seam, not its neighbours.
    public static float HOP_ATTEN = 0.35f;
    /// <summary>ROUND-7 FIX 1. Below this delivered push NEITHER body is held
    /// to have caused the contact, so neither is credited with ram damage: a
    /// resting or coasting touch is not a ram. See ramEffort.</summary>
    public static float RAM_MIN_EFFORT = 0.05f;
    // The arena is not a weapon. Walls, floor and loose debris deal ZERO hp
    // damage, so shear from them was loss the player could neither see coming
    // nor attribute to a build decision (a 9.7 m/s wall crash cost 6 of 14 parts
    // at 100% HP). They still stress the structure, at a fraction: you can shake
    // a badly-braced build apart on the boards, you cannot be dismembered by them.
    // MEASURED calibration (owen's 971 kg build, 10.4 m/s full-throttle wall
    // crash, all seams 900 N.s except three 3-socket seams at 2700):
    //   old euclidean code -> 6 of 14 parts gone
    //   0.55 -> 0 parts, 0 seams (peak seam stress 770, under every threshold)
    //   0.75 -> 1 part, 1 seam  (peak 1050: the leading spinner mount, i.e. the
    //                            part that actually hit the wall)
    //   0.95 -> 1 part, 1 seam  (peak 1330 - same seam, no cascade)
    // 0.75 is the value that makes a full-speed crash COST something specific
    // and attributable without dismembering the machine.
    // ROUND-3 RE-DERIVATION (critic CRITICAL 3, and forced by the compressor
    // above). 0.75 was calibrated when EVERY environment contact above 1400 N.s
    // produced the SAME clamped 1050, so the constant was fitted against a
    // single saturated value and could not tell a 3.5 m/s bump from a 10.4 m/s
    // crash. With SeamLoad in place the environment band opened up and 0.75
    // became a dismemberment machine.
    //
    // MEASURED, live probe at timeScale 0.15, AFK cell (both robots untouched by
    // the player, player dealt 0.00): the MAULER drives into an ARENA WALL about
    // 2.0 s after the bell at ~3.5 m/s. Raw impulse J = 4806 N.s. SeamLoad(4806)
    // = 2594, x 0.75 = 1945 - and the ledger for that exact event reads
    // "chassis_1<->spikeZP_6 SHEARED at 1945 vs eff 900". The spike leaves at
    // hp 87/87, ONE HUNDRED PERCENT HEALTH, in 6 of 6 runs. That is the critic's
    // "both robots shed parts on spawn" finding, and its real cause is not the
    // spawn drop at all: it is the boards. Neither a longer SETTLE_TIME (3.0 s,
    // n=6, 6/6 still shed) nor a post-bell environment ramp (n=6, 6/6 still
    // shed) touched it, because the impact is 2 s after the bell and is a crash.
    //
    // BRACKETED, same cell, one constant moved, n=6 each:
    //   0.75 -> spike SHED at 100% HP in 6/6, enemy finishes 10 of 11 pieces
    //   0.28 -> spike SHED in 0/6, enemy 11/11, but the seam still SHEARED
    //           (971-1282) so the part was left hanging by a thread
    //   0.18 -> no environment shear at all; the only seams that still fail are
    //           the runs where the MAULER actually rams the parked player
    //           (player "taken" 41-56); the one run with no contact finished 11/11
    //
    // WHY 0.20 AND NOT A FITTED NUMBER. It is derived, not tuned: it is the
    // largest round value satisfying STRESS_J_HARD x scale < 900, i.e.
    // 4200 x 0.20 = 840, so the arena's HARDEST POSSIBLE contact - a solver
    // spike included - lands at 93% of the weakest x1 single-socket seam and
    // therefore can never, by itself, remove a healthy part from a legal
    // machine. That is exactly the sentence this constant's original comment
    // was written to guarantee ("the arena is not a weapon... you cannot be
    // dismembered by them") and which it had stopped delivering.
    //
    // WHAT IS DELIBERATELY KEPT. ABS seams (525) still fail on the boards, and
    // so does any seam whose effective threshold has already been degraded, so
    // "you can shake a badly-braced build apart on the boards" survives. What
    // is given up is the round-5 goal that a full-speed crash cost a x1 machine
    // exactly one part: under a compressor a routine 3.5 m/s bump and a 10.4 m/s
    // crash both land near the ceiling, so no scale can separate them, and I
    // would rather lose the crash tax than keep taxing every bump at the same rate.
    public static float STRESS_ENV_SCALE = 0.20f;
    // Hard cap on seams severed per physics step, across ALL contacts on this
    // body. Structural failure has to be readable on camera; losing a third of
    // the body inside one frame is not.
    public static int MAX_BREAKS_PER_STEP = 2;
    // Round-4 fix 1 (critic finding 1: "one opening collision destroys 40-90%
    // of both machines in a single frame" — measured 15 parts gone between two
    // robots in 1/30 s, 1.5 s into a 90 s match). The per-step cap was no
    // protection because ONE seam sheds an entire SUBTREE: a beam plus its two
    // wheels and its weapon is four pieces off one bolt circle. Two rules:
    //
    // (a) A seam is judged against the load it actually carries. Its break
    //     threshold scales with how many PIECES would separate from the core if
    //     it failed (parts + the wheels bolted to them), because a joint holding
    //     a whole limb is a limb-sized load path and a leaf bracket is not.
    //     Consequence at 0.6: a 1-socket aluminium seam (900 N·s) holding a
    //     2-piece limb needs 1440 N·s — above STRESS_J_CAP — so shear PEELS the
    //     outermost piece and works inward instead of amputating a quarter of
    //     the machine per contact. Weak materials still lose chunks: the same
    //     seam in ABS is 300 N·s and still fails at 480 with a limb on it.
    public static float SUBTREE_K = 0.6f;
    // (b) A body may fail at most one seam per this many seconds. A mutual ram
    //     registers on several colliders inside one physics step, so the old
    //     per-step cap still let two seams (and both their subtrees) go before
    //     a single frame had rendered. Dismemberment the player cannot see
    //     happen is dismemberment he cannot learn from.
    public static float BREAK_COOLDOWN = 0.35f;
    // Round-5 fix 2 (critic finding 2: "one physics step still deletes ~45% of
    // the robot"). MAX_BREAKS_PER_STEP caps SEAMS, not PIECES — and one seam,
    // or one hub part dying on HP, orphans everything behind it. Measured: 6-7
    // pieces off owen's build inside a single 1/50 s step, of which exactly ONE
    // had taken any damage. The seam-vs-hub distinction is invisible from the
    // player's chair; what he sees is half his machine evaporating. So an
    // orphaned subtree is no longer CUT, it is left HANGING — still bolted on,
    // still carrying its mass — and peels off a couple of pieces at a time.
    public static int MAX_SHED_PER_STEP = 2;
    // How long a hanging chunk rides along if nothing touches it.
    public static float HANG_LIFE = 5f;
    // ...and the multiplier on that clock while the body is in contact, so a
    // chunk being dragged or punched goes in ~1.2 s. Punishment finishes it.
    public static float HANG_CONTACT_DRAIN = 4f;
    // A hanging chunk's own seams are weakened, so a follow-up hit fragments it
    // instead of dropping the whole limb at once.
    public static float HANG_SEAM_MUL = 0.45f;
    // Pieces that did not fit inside this step's shed budget wait this long, so
    // the peel spreads over the following seconds rather than the next frame.
    public static float HANG_REPRIEVE = 0.4f;
    // ROUND-1-IMPL FIX (r1 critic CRITICAL 3: "the battery detaches at FULL HP
    // via the seam/stress solver, which instantly loses the match with no
    // counterplay" - 6 of 40 matches, 15%, ended on a pack that had taken ZERO
    // damage; the critic caught one at [battery_3 56/56] one sample before it
    // separated).
    //
    // WHY A SPECIAL CASE IS JUSTIFIED HERE AND NOWHERE ELSE. Every other part
    // in this game is worth what it does: lose a wheel and you steer worse,
    // lose a blade and you hit softer, and either way you keep playing. The
    // pack is the ONE part whose loss ENDS the match - PowerPlant.Recompute
    // drops capacityKJ to whatever the core carries, the bot goes flat and is
    // counted out - so it is the one part where a purely structural failure is
    // not a setback the player can play through. A failure mode with no
    // counterplay is not difficulty.
    //
    // WHAT THIS DOES AND WHAT IT DELIBERATELY DOES NOT DO. A seam holding a
    // match-ending part is judged at up to VITAL_SEAM_MUL x its normal break
    // threshold, and that bonus is SCALED BY THAT PART'S OWN REMAINING HP. An
    // undamaged bay holds; a pack that has been beaten on holds progressively
    // worse and can still be torn clean off, which IS the counterplay that was
    // missing. The HP path (DamageResolver -> DestroyPartNow) is untouched:
    // shooting the pack out still works exactly as it did, and is now the
    // intended way to do it.
    //
    // WHY 1.60, and it is picked from a cap rather than from taste. Contact
    // impulse feeding the stress system is clamped at STRESS_J_CAP = 1400 N.s,
    // so a seam whose EFFECTIVE threshold exceeds 1400 cannot be sheared by any
    // single contact at any speed. Every measured build (owen's and all six of
    // the critic's) carries the pack on 1-socket Aluminium seams:
    // 0.6 x BREAK_K(1500) x 1 socket = 900 N.s. 900 x 1.60 = 1440, just over
    // the cap. So a full-HP pack is shear-proof, a pack at 62% HP
    // (900 x 1.372 = 1235) is not, and nothing else in the machine moves.
    public static float VITAL_SEAM_MUL = 1.60f;
    // ROUND-1-IMPL instrument. Opt-in ledger of every part that leaves a body,
    // with WHY it left and the HP it still had when it left. The r1 critic
    // could only establish "the battery came off at 56/56" by sampling part HP
    // every 45 fixed steps and catching it in the act; that is a lot of work to
    // answer a question the engine already knows. Off by default - a sweep
    // turns it on, so it costs a shipped build nothing.
    public static bool detachLogOn = false;
    public static readonly List<string> detachLog = new List<string>();

    public class Part
    {
        public PartSpec spec;
        public GameObject go;
        public float mass;
        public MatDef mat;
        public bool detached;
        // Phase 2A durability (§4.2): HP_K × strengthRel × volume. Ablated by
        // DamageResolver; ≤0 → the part is pulverized (DestroyPart).
        public float hp;
        public float maxHp;
        public float lastHitTime;  // ram-damage rate limit
        // Round-5 fix 2: orphaned from the core but not yet shed. Physically
        // still part of the compound body (so it still collides, still carries
        // mass, and wheels bolted to it still drive) on a countdown.
        public bool hanging;
        public float hangTimer;
    }

    public class Edge
    {
        public int a, b;
        public float threshold;
        public bool broken;
        public float peak;      // max stress ever seen (for the HUD / tuning)
        public string label;
    }

    public Rigidbody rb;
    /// <summary>ROUND-7 FIX 1 — the push this machine is DELIVERING, as a world
    /// vector of magnitude 0..1: drive direction x throttle x supplyFrac x the
    /// fraction of its wheels on the ground. Recomputed every FixedUpdate,
    /// which runs before the solver and therefore before this step's contact
    /// callbacks, so a contact is judged against the effort in force when it
    /// happened.
    ///
    /// WHY IT EXISTS. One collision runs Accumulate on BOTH robots with the
    /// same impulse J, so before this a machine standing perfectly still was
    /// credited with the full damage of a ram it did not initiate. The round-6
    /// critic measured the consequence: AFK damage cards of 275.5 against a
    /// charger's 290.4 — inside FightManager.DRAW_BAND_FRAC, i.e. scored a
    /// draw — and across 30 controlled matches parking went 50% non-loss where
    /// charging went 0%. Not touching the controls was the dominant strategy,
    /// which is the one outcome a fighting game may not have.
    ///
    /// WHY EFFORT AND NOT VELOCITY. The obvious fix is to split the damage by
    /// each side's approach speed along the contact normal. I implemented that
    /// first and MEASURED it: a parked 451.6 kg bot still carded 155 dealt to
    /// 95 taken, and a parked 1451.2 kg bot 186/163 — because a body that has
    /// just been shoved is genuinely moving into the contact on the next step,
    /// and velocity cannot tell momentum a machine EARNED from momentum an
    /// opponent LENT it. Effort can: it is zero unless this machine's own
    /// drivetrain is pushing, this machine's own pack is supplying it, and this
    /// machine's own wheels are on the ground.
    ///
    /// It is also the honest §6.2 coupling the energy budget was missing. Ram
    /// damage now costs throttle, and throttle costs kilowatts, so offence is
    /// paid for out of the pack — where idling buys nothing at all.</summary>
    public Vector3 ramEffort;
    RaycastWheelDrive driveCache;

    /// <summary>Recompute ramEffort. One-step-lagged inputs (last step's
    /// throttle, supplyFrac and grounded count) are deliberate and the same
    /// argument PowerPlant's class doc makes: no script execution order to
    /// define, and 20 ms at 50 Hz is invisible.</summary>
    void UpdateRamEffort()
    {
        ramEffort = Vector3.zero;
        if (rb == null || dead || !combatEnabled) return;
        if (driveCache == null) driveCache = GetComponent<RaycastWheelDrive>();
        var d = driveCache;
        if (d == null) return;
        int wc = d.WheelCount();
        if (wc <= 0) return;
        float thr = Mathf.Clamp(d.DeliveredThrottle(), -1f, 1f);
        if (Mathf.Abs(thr) < 0.0001f) return;
        float grip = Mathf.Clamp01((float)d.GroundedCount() / wc);
        if (grip <= 0.0001f) return;
        Vector3 fwd = transform.TransformDirection(d.RollOf(0));
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-6f) return;
        ramEffort = fwd.normalized * thr * grip;
    }
    public List<Part> parts = new List<Part>();
    public List<Edge> edges = new List<Edge>();
    public int coreIndex;

    // Phase 2A combat bookkeeping.
    public float damageDealt;
    public float damageTaken;
    /// <summary>Core destroyed ⇒ robot dead (§4.2). BuilderManager/Phase 2B
    /// read the flag or subscribe to the event.</summary>
    public bool dead;
    public static System.Action<CompoundRobot> OnRobotDead = delegate { };

    /// <summary>Round-1 combat fix 1 — spawn protection: while FALSE, this
    /// body accumulates NO joint stress and takes/deals NO HP damage (the
    /// DamageResolver checks it too). Arena spawns start protected so the
    /// settle drop can never shear parts or bleed HP before the bell;
    /// FightManager's bell / the test-mode settle timer arm it. Phase 0
    /// sandbox bots keep the default (armed).</summary>
    public bool combatEnabled = true;
    // Round-3 fix (CRITICAL 3): when combat last ARMED on this body, so the
    // environment-stress ramp above has something to measure from. Tracked by
    // edge-detecting the public field in FixedUpdate rather than by converting
    // it to a property: three call sites outside this file assign it, and a
    // field-to-property change is the kind of silent blast radius this project
    // has been burned by. -99 = never armed, which yields a full-scale ramp.
    float combatArmedAt = -99f;
    bool combatWasArmed = true;
    /// <summary>0..1 ramp on ENVIRONMENT-sourced seam stress after the bell.</summary>
    public float EnvArmScale()
    {
        if (ENV_ARM_RAMP_S <= 0f) return 1f;
        return Mathf.Clamp01((Time.time - combatArmedAt) / ENV_ARM_RAMP_S);
    }

    /// <summary>ROUND-2-CRITIC FIX (CRITICAL 3) - on its back, by the same test
    /// FightManager.TickCountOut uses for the count-out, so "flipped" means one
    /// thing to the referee, the judges and the damage model. 0.2 rather than 0
    /// because a machine resting on its flank is out of the fight just as much
    /// as one exactly inverted.</summary>
    public bool Flipped
    {
        get { return Vector3.Dot(transform.up, Vector3.up) < 0.2f; }
    }

    /// <summary>Round-1 combat fix 6: fired when a connected chunk SHEARS off
    /// (worldPos of the chunk, part count) — HP-destroyed parts already
    /// announce through DamageResolver.OnHit's destroyed flag, but shears had
    /// no readable callout at all.</summary>
    public static System.Action<Vector3, int> OnPartsLost = delegate { };

    /// <summary>Round-3 fix 1: when this body was last TOUCHING another robot /
    /// the arena (walls, floor, debris). FightManager's count-out needs to tell
    /// a bot that is shoving something — a legitimate, winnable state — apart
    /// from one that is genuinely incapacitated. Stamped before the impulse
    /// gate on purpose: a sustained push registers only tiny per-step impulses,
    /// well under MIN_IMPULSE, yet it is exactly the case that must not lose.</summary>
    [System.NonSerialized] public float lastRobotContact = -99f;
    [System.NonSerialized] public float lastEnvContact = -99f;

    // Mass carried by the body that is NOT a structural part (Phase 1: the
    // raycast wheels — they have no colliders/parts of their own but their
    // mass must load the suspension, the HUD, and the CoM). Survives
    // RecomputeMass so part breaks don't erase it.
    public float extraMass;
    public Vector3 extraMassLocalCoM = Vector3.zero;

    public static System.Action<string> Log = delegate { };
    public static readonly List<GameObject> Spawned = new List<GameObject>();

    // ---- diagnosis telemetry (scripted tests only; off by default) ----
    public static bool telemetryOn = false;
    public static int telemetryHits;      // impulses >= MIN_IMPULSE
    public static int telemetrySubMin;    // impulses filtered out
    public static float telemetryMaxJ;
    public static float telemetrySumJ;
    public static string telemetryMaxInfo = "";
    /// <summary>Round-3 instrument: contacts whose RAW impulse exceeded the
    /// compressor knee, and the largest compressed seam load produced.</summary>
    public static int telemetryOverKnee;
    public static float telemetryMaxSeamLoad;
    public static void TelemetryReset()
    { telemetryHits = 0; telemetrySubMin = 0; telemetryMaxJ = 0f; telemetrySumJ = 0f; telemetryMaxInfo = "";
      telemetryOverKnee = 0; telemetryMaxSeamLoad = 0f; }

    readonly List<Edge> pendingBreaks = new List<Edge>();
    // Round-4 fix 1b: when this body last severed a seam (see BREAK_COOLDOWN).
    float lastBreakTime = -99f;

    // ROUND-1-IMPL instrument (r1 critic CRITICAL 2: "stored limb energy does
    // not convert into damage - 17x the energy buys at most 1.34x the damage").
    // That finding compares WHOLE-MATCH damage cards, which mix three unrelated
    // sources: chassis rams, spinner-disc bites and actuated-limb bites. Only
    // the third is a function of limb energy, so the card cannot answer the
    // question it is being asked. These split it, per attacker, per source.
    // damageDealt is unchanged and is still the sum of all three.
    public float dealtRam, dealtLimb, dealtDisc;
    public int hitsRam, hitsLimb, hitsDisc;
    public float maxLimbHit;

    /// <summary>Round-4 fix 1a: how many live wheels are bolted to part i.
    /// Set by RaycastWheelDrive — wheels are not parts, but a beam carrying two
    /// of them is a three-piece load path and its seam must be judged as one.
    /// Null (Phase 0 sandbox bots) → wheels simply don't count.</summary>
    [System.NonSerialized] public System.Func<int, int> WheelsOnPart;

    // ------------------------------------------------------------------ build

    public struct Conn
    {
        public int a, b;
        // Socket-count strength multiplier: how many socket pairs actually
        // mate on this seam (BuilderManager.SharedSockets). LINEAR and
        // UNCAPPED by design — a 9-socket plate seam should be near-
        // unbreakable; engineering your load paths is the game. Global feel
        // still tunes through BREAK_K. 2-arg ctor keeps legacy conns at ×1.
        public float mult;
        public Conn(int a, int b) { this.a = a; this.b = b; this.mult = 1f; }
        public Conn(int a, int b, float mult) { this.a = a; this.b = b; this.mult = mult; }
    }

    public static CompoundRobot Build(string name, PartSpec[] specs, Conn[] conns,
                                      Vector3 pos, Quaternion rot)
    {
        var root = new GameObject(name);
        root.transform.SetPositionAndRotation(pos, rot);
        Spawned.Add(root);

        var rb = root.AddComponent<Rigidbody>();
        var robot = root.AddComponent<CompoundRobot>();
        robot.rb = rb;

        for (int i = 0; i < specs.Length; i++)
        {
            var s = specs[i];
            var mat = MatDB.Get(s.mat);
            // Unscaled part root with an explicit BoxCollider (physically
            // identical to the old scaled cube primitive); the full compound
            // visual is built by the SAME factory the builder uses, so parts
            // keep their identity in the arena. Decorative children carry no
            // colliders and travel with the part GameObject when it becomes
            // debris.
            var go = new GameObject(s.id);
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = s.localPos;
            var box = go.AddComponent<BoxCollider>();
            box.size = s.size;
            // Phase 3: the part carries its material's FINISH into the arena too,
            // so a steel chassis reads glossy and an ABS one matte in the fight,
            // not just in the builder. The core keeps its yellow bias.
            PartVisualFactory.BuildPart(s.id, go.transform, s.size, s.isCore,
                s.isCore ? Color.Lerp(mat.color, Color.yellow, 0.5f) : mat.color,
                mat.metallic, mat.smoothness);

            var part = new Part { spec = s, go = go, mat = mat, mass = MatDB.MassOf(s.size, mat) };
            if (s.massOverride > 0f) part.mass = s.massOverride;
            part.hp = part.maxHp = DamageResolver.HpOf(s);
            robot.parts.Add(part);
            if (s.isCore) robot.coreIndex = i;
            // Phase 4 (2026-07-27): a disc has NO motor of its own any more.
            // spinner/spinnerSaw are unpowered ROTOR EDGES - lumps of hardened
            // steel that only turn when they are bolted PAST A SPINDLE, exactly
            // like a blade bar. Two consequences, both intended:
            //   * there is now ONE spinning system in the game (Actuator)
            //     instead of two that had to be kept in agreement, and
            //   * the spindle finally earns its price. A pre-made spinner cost
            //     EXACTLY the same 36 cr as a bare spindle while bundling a
            //     motor AND a rotor, so building your own drum (spindle + beam
            //     + blade = 110 cr) could never compete. That single fact is
            //     what "the legacy disc dominates the Phase 4 kit" kept meaning
            //     every time a critic round rediscovered it.
            // A disc bolted straight to the frame is still a perfectly good
            // FIXED edge - DamageResolver prices it at edgeHardness 1.5/1.6 on
            // ram contact - it just does not spin. SpinnerWeapon is retained
            // for reference but is no longer attached by anything; see its
            // header before you wire it back up.
        }

        // Connector collars on every connection seam (visual only).
        foreach (var c in conns)
            PartVisualFactory.BuildCollar(robot.parts[c.a].go.transform,
                specs[c.a].localPos, specs[c.a].size * 0.5f,
                specs[c.b].localPos, specs[c.b].size * 0.5f);

        foreach (var c in conns)
        {
            robot.edges.Add(new Edge
            {
                a = c.a,
                b = c.b,
                // Break threshold = the WEAKER of the two mated parts (§5.2)
                // × the seam's mated-socket count (Phase 2C: more bolts =
                // stronger joint; guard keeps default-constructed conns at ×1).
                threshold = Mathf.Min(robot.parts[c.a].mat.strengthRel,
                                      robot.parts[c.b].mat.strengthRel) * BREAK_K
                            * (c.mult > 0f ? c.mult : 1f),
                label = specs[c.a].id + "↔" + specs[c.b].id,
            });
        }

        // Solver survival kit basics (§6.4).
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.maxAngularVelocity = 50f;
        // Phase 4: cap how fast PhysX may push two overlapping bodies apart.
        // MEASURED: a hammer landing on a parked 700 kg Mauler threw it EIGHT
        // METRES across the arena. That was not the shove - the shove is a
        // 162 N.s impulse, which on 700 kg is 0.23 m/s. It was depenetration:
        // an Actuator limb moves its colliders by teleport rather than through
        // the solver, so a swing that lands ends the step deeply interpenetrated
        // and the default (effectively unbounded) recovery velocity fires the
        // victim off like a spring. 4 m/s still clears an overlap in a couple of
        // frames without launching anything, and it takes some of the comedy out
        // of the existing shear/debris pop-outs too.
        rb.maxDepenetrationVelocity = 4f;

        // Phase 4: partition the graph at every actuator and hand each one the
        // limb it drives. No-op on a build with no actuators, which is every
        // build that existed before this line.
        Actuator.Wire(robot, specs, conns);

        robot.RecomputeMass();
        return robot;
    }

    /// <summary>
    /// §4.4: per-part masses aggregate into the body's mass and center of mass.
    /// We set mass and CoM explicitly from part masses (Unity's automatic CoM
    /// assumes uniform density across colliders, which would ignore materials).
    /// Inertia tensor stays engine-computed from colliders — a known Phase 0
    /// approximation, good enough to prove the loop.
    /// </summary>
    public void RecomputeMass()
    {
        float total = 0f;
        Vector3 weighted = Vector3.zero;
        foreach (var p in parts)
        {
            if (p.detached) continue;
            total += p.mass;
            weighted += p.mass * p.go.transform.localPosition;
        }
        if (total <= 0f) { Destroy(gameObject); return; }
        rb.mass = total + extraMass;
        rb.centerOfMass = (weighted + extraMass * extraMassLocalCoM) / (total + extraMass);
        rb.ResetInertiaTensor();
    }

    public float ActiveMass()
    {
        float t = 0f;
        foreach (var p in parts) if (!p.detached) t += p.mass;
        return t + extraMass;
    }

    /// <summary>True while part i is still attached to this body. Wheels use
    /// this to notice their MOUNT part is gone (destroyed or sheared off) and
    /// fall off with it — before this, a wheel whose beam died kept hovering
    /// in place, attached to nothing (the wheel-anchor shortcut bug).</summary>
    public bool PartAlive(int i)
    {
        return i >= 0 && i < parts.Count && !parts[i].detached;
    }

    // --------------------------------------------------- stress from contacts

    void OnCollisionEnter(Collision c) { Accumulate(c); }
    void OnCollisionStay(Collision c) { Accumulate(c); }

    void Accumulate(Collision c)
    {
        // Contact bookkeeping runs BEFORE every gate below (see lastRobotContact).
        {
            Rigidbody orb0 = c.collider != null ? c.collider.attachedRigidbody : null;
            CompoundRobot ocr = orb0 != null ? orb0.GetComponent<CompoundRobot>() : null;
            if (ocr != null && ocr != this) lastRobotContact = Time.time;
            else lastEnvContact = Time.time;   // static geometry OR loose debris
        }
        float J = c.impulse.magnitude;
        if (telemetryOn)
        {
            telemetrySumJ += J;
            if (J < MIN_IMPULSE) telemetrySubMin++; else telemetryHits++;
            // Round-3 instrument: how often the compressor's knee is actually
            // reached, and how far past it. Without this "the cap is saturated"
            // can only be inferred from the detach ledger after the fact.
            if (J > STRESS_J_CAP)
            {
                telemetryOverKnee++;
                float sl = SeamLoad(J);
                if (sl > telemetryMaxSeamLoad) telemetryMaxSeamLoad = sl;
            }
            if (J > telemetryMaxJ)
            {
                telemetryMaxJ = J;
                telemetryMaxInfo = name + " vs " + (c.collider != null ? c.collider.name : "?")
                                 + " J=" + J.ToString("F0");
            }
        }
        if (J < MIN_IMPULSE) return;
        // Spawn protection: a settling bot neither shears nor takes damage.
        if (!combatEnabled) return;

        // Average contact point is representative enough for box parts.
        Vector3 p = Vector3.zero;
        int n = c.contactCount;
        if (n == 0) return;
        for (int i = 0; i < n; i++) p += c.GetContact(i).point;
        p /= n;

        var contact = c.GetContact(0);
        Collider otherCol = contact.otherCollider;
        Rigidbody orb = otherCol != null ? otherCol.attachedRigidbody : null;
        CompoundRobot attacker = orb != null ? orb.GetComponent<CompoundRobot>() : null;
        bool fromRobot = attacker != null && attacker != this;

        // ---- Round-3 fix 2: stress travels along the STRUCTURE, not the air.
        // See HOP_ATTEN / STRESS_ENV_SCALE / MAX_BREAKS_PER_STEP above for why.
        int hitIdx = PartIndexOf(contact.thisCollider);
        if (hitIdx < 0) hitIdx = NearestPart(p);
        // Round-3 fix: SeamLoad is the old Mathf.Min below the knee and a
        // sqrt compressor above it (CRITICAL 1), and the environment term
        // ramps in after the bell so the spawn drop cannot shear (CRITICAL 3).
        // BUMPS ARE NOT ATTACKS (owen, 2026-07-27). What struck us decides
        // whether this contact is allowed to shear anything off. Resolved here,
        // BEFORE the stress block, because the HP block further down needed the
        // same lookup and used to be the only place that did it - which is
        // exactly how the builder and the arena have disagreed three times.
        //
        // SCAN THE WHOLE CONTACT MANIFOLD, NOT GetContact(0). MEASURED: with the
        // structure gate on and this lookup reading only contact 0, a FIXED
        // SPIKE dealt 0 damage in 9 of 9 matches and the shipped MAULER became
        // completely harmless - because PhysX's first reported contact for a
        // spiked nose is usually a chassis box, and the spike behind it was
        // never the part asked about. Before the gate that was invisible: a
        // 1.0 hull hit still billed. Now it decides everything, so it has to be
        // right. If ANY part in the manifold is an edge, the edge is in the
        // collision, and the hardest one is what struck.
        float strikerHardness = 1f;
        int aIdxHard = -1;
        if (fromRobot)
        {
            for (int ci = 0; ci < n; ci++)
            {
                int ai = attacker.PartIndexOf(c.GetContact(ci).otherCollider);
                if (ai < 0) continue;
                float hh = attacker.parts[ai].spec.edgeHardness;
                if (hh <= 0f) hh = 1f;
                if (aIdxHard < 0 || hh > strikerHardness) { strikerHardness = hh; aIdxHard = ai; }
            }
        }
        float structMul = (fromRobot && !DamageResolver.IsEdge(strikerHardness))
                        ? DamageResolver.STRUCT_SEAM_MUL : 1f;
        float Js = SeamLoad(J) * (fromRobot ? structMul
                                            : STRESS_ENV_SCALE * EnvArmScale());
        if (hitIdx >= 0 && pendingBreaks.Count < MAX_BREAKS_PER_STEP
            && Time.time - lastBreakTime >= BREAK_COOLDOWN)
        {
            int[] hop = HopsFrom(hitIdx);
            // One collision event fails at most its single most-overloaded seam.
            // Everything else keeps its recorded peak and stays loaded, so a
            // structure comes apart hit by hit instead of all at once.
            Edge worst = null;
            float worstRatio = 1f;
            foreach (var e in edges)
            {
                if (e.broken) continue;
                int ha = hop[e.a], hb = hop[e.b];
                int h = ha < 0 ? hb : (hb < 0 ? ha : Mathf.Min(ha, hb));
                if (h < 0) continue;   // already severed from the struck cluster
                float stress = Js * Mathf.Pow(HOP_ATTEN, h);
                if (stress > e.peak) e.peak = stress;
                if (e.threshold <= 0f) continue;
                // Cheap reject first: a seam under its BASE threshold cannot
                // fail at any load factor, and LoadFactor costs a flood fill.
                if (stress <= e.threshold) continue;
                float ratio = stress / (e.threshold * LoadFactor(e));
                if (ratio > worstRatio) { worstRatio = ratio; worst = e; }
            }
            if (worst != null)
            {
                // ROUND-1-IMPL instrument: log the seam BEFORE it is marked
                // broken - LoadFactor floods over live edges, so reading it
                // after would report the post-failure structure.
                if (detachLogOn)
                    detachLog.Add(string.Format("{0}|SEAM {1}|SHEARED at {2:F0} vs eff {3:F0}|t {4:F1}",
                        name, worst.label, worst.peak, worst.threshold * LoadFactor(worst), Time.time));
                worst.broken = true;
                lastBreakTime = Time.time;
                pendingBreaks.Add(worst);
            }
        }

        // ---- Phase 2A rammer HP damage (§7) — runs IN PARALLEL with the
        // joint-stress system above. Robot-vs-robot only: walls, floor and
        // debris shards deal no HP damage. Each robot processes damage TO ITS
        // OWN struck part (the other robot's own callback handles the mirror).
        if (fromRobot && !dead && J > DamageResolver.RAM_MIN_J)
        {
            int vIdx = PartIndexOf(contact.thisCollider);
            if (vIdx >= 0)
            {
                // Rate limiting moved INTO DamageResolver.ApplyHit (round-1
                // fix 7): one shared per-part post-hit immunity window covers
                // rams AND spinner bites, and the stamp happens only when a
                // hit actually applies.
                // Same manifold scan the seam gate above already did - reused so
                // the two systems can never disagree about what hit us.
                int aIdx = aIdxHard;
                float hardness = strikerHardness;
                // ---- ROUND-7 FIX 1: damage dealt is bought with your OWN speed.
                // One collision runs this callback on BOTH robots with the same
                // impulse J, so a machine standing perfectly still was credited
                // with the full damage of a ram it did not initiate. Measured by
                // the round-6 critic: an AFK bot's card read 275.5 against the
                // charger's 290.4 - inside FightManager.DRAW_BAND_FRAC, i.e. a
                // draw - and across 30 controlled matches parking went 50%
                // non-loss where charging went 0%. Not touching the controls was
                // the dominant strategy, which is the one outcome a fighting
                // game may not have.
                //
                // The split is the fraction of the CLOSING speed each side
                // actually brought to the contact point. It is a pure
                // re-attribution, never an amplification: both shares are
                // clamped to <= 1, a head-on collision where both machines bring
                // the same speed still scores exactly as it did before, and the
                // side that is merely being hit scores nothing. No build is
                // rescued and no build is nerfed - the same rule reads off the
                // velocities every machine already has.
                // Orient the contact axis from the ATTACKER's body toward this
                // one, from the geometry, so the split never depends on which
                // body PhysX decided to call "this" or on its normal
                // convention.
                Vector3 axis = contact.normal;
                Vector3 sep = rb.worldCenterOfMass - orb.worldCenterOfMass;
                if (Vector3.Dot(sep, axis) < 0f) axis = -axis;
                float intoA = Mathf.Max(0f,  Vector3.Dot(attacker.ramEffort, axis));
                float intoV = Mathf.Max(0f, -Vector3.Dot(ramEffort, axis));
                float refE  = Mathf.Max(intoA, intoV);
                // A pure re-attribution: both shares are <= 1, so a head-on
                // collision where both machines are driving in at full throttle
                // still scores exactly as it did before this fix, and the side
                // that is only being hit scores nothing.
                float causal = refE <= RAM_MIN_EFFORT ? 0f : Mathf.Clamp01(intoA / refE);
                if (causal > 0f)
                {
                    DamageResolver.RamHit(attacker, hardness, this, vIdx, J, p, causal);
                    // Phase 4: a WEDGE turns the same contact upward instead of
                    // away. Applied here, in the victim's own callback, because
                    // this is the one place that already knows which part struck
                    // it - and it means a wedge bolted straight to the frame is
                    // a real weapon without needing an actuator behind it.
                    // Toppling is a win path (count-out: flipped), so this gives
                    // low-damage builds something to actually do.
                    float lb = aIdx >= 0 ? Actuator.LiftBiasOf(attacker.parts[aIdx].spec.id) : 0f;
                    if (lb > 0f)
                    {
                        float liftImp = Mathf.Min(J, DamageResolver.SHOVE_CAP) * lb * causal;
                        rb.AddForceAtPosition(Vector3.up * liftImp, p, ForceMode.Impulse);
                        // ROUND-1 IMPL FIX (critic CRITICAL 3): the same
                        // angular term Actuator.Bite now applies, so a wedge
                        // bolted straight to the frame topples for the same
                        // reason an actuated one does. A pure upward shove is
                        // damped out by maxDepenetrationVelocity; toppling is a
                        // rotation about the contact line. `sep` above is
                        // victim - attacker, so up x (-sep) lifts the near
                        // edge, the one the ramp is under. See
                        // Actuator.TIP_ARM_M for the 0.45 m arm.
                        Vector3 flat = -sep; flat.y = 0f;
                        Vector3 tipAxis = Vector3.Cross(Vector3.up, flat);
                        // ROUND-3-CRITIC FIX (CRITICAL 3). Same energy-based
                        // topple the actuated wedge now uses, so a plow bolted
                        // to the nose and a wedge on a pivot obey one rule.
                        // The energy a normal collision of impulse J dissipates
                        // between two free bodies is J^2/(2.mu) with mu the
                        // reduced mass - that is the honest budget a RAM has to
                        // spend on flipping, and it is why charging harder is
                        // what makes a static plow work. Measured against the
                        // critic's 1642 kg Steel plow vs the 707 kg skeleton:
                        // a 3 m/s shunt yields 729 J (short of the 818 J that
                        // machine needs to go over) and a 4 m/s one yields
                        // 1296 J (over). The plow now has a speed it works at.
                        float mu = (rb.mass * orb.mass) / Mathf.Max(1f, rb.mass + orb.mass);
                        float eColl = (J * J) / (2f * Mathf.Max(mu, 1f));
                        Actuator.ApplyTopple(rb, tipAxis,
                                             eColl * lb * causal * Actuator.LIFT_EFFICIENCY);
                    }
                }
            }
        }
    }

    readonly HashSet<int> loadSeen = new HashSet<int>();
    readonly List<int> loadQueue = new List<int>();

    /// <summary>Round-4 fix 1a: the strength multiplier a seam earns for the
    /// load it carries — 1 for a leaf, 1 + SUBTREE_K·(pieces−1) for a limb.
    /// This is why a big hit peels the outside of a machine instead of
    /// amputating it: the deeper the seam, the more it is holding, the more it
    /// takes to part.</summary>
    float LoadFactor(Edge e)
    {
        return (1f + SUBTREE_K * Mathf.Max(0, DetachPieces(e) - 1)) * VitalFactor(e);
    }

    /// <summary>ROUND-1-IMPL FIX (critic CRITICAL 3). Extra seam strength for a
    /// seam that holds a MATCH-ENDING part, tapering linearly to nothing as
    /// that part loses HP. See VITAL_SEAM_MUL for the sizing and the argument
    /// for why exactly one class of part gets this.</summary>
    float VitalFactor(Edge e)
    {
        return Mathf.Max(VitalOf(e.a), VitalOf(e.b));
    }

    float VitalOf(int i)
    {
        if (i < 0 || i >= parts.Count) return 1f;
        var p = parts[i];
        if (!IsVital(p.spec.id)) return 1f;
        float hpFrac = p.maxHp > 0.0001f ? Mathf.Clamp01(p.hp / p.maxHp) : 0f;
        return 1f + (VITAL_SEAM_MUL - 1f) * hpFrac;
    }

    /// <summary>Parts whose loss ENDS the match rather than degrading it. Only
    /// the battery qualifies today: PowerPlant capacity IS the pack, and a bot
    /// with no pack runs flat and is counted out. The core is deliberately not
    /// listed - losing it is already an explicit, legible KO rather than a
    /// structural surprise.</summary>
    public static bool IsVital(string id) { return id != null && id.StartsWith("battery"); }

    /// <summary>ROUND-1-IMPL FIX (critic CRITICAL 3, second half: "add a HUD
    /// warning when its seam stress crosses a threshold - right now the player
    /// gets no signal at all"). Worst recorded stress on any live seam holding
    /// a match-ending part, as a fraction of that seam's EFFECTIVE break
    /// threshold. >= 1 would have parted it; FightManager warns well below.</summary>
    public float VitalSeamLoad()
    {
        float worst = 0f;
        foreach (var e in edges)
        {
            if (e.broken) continue;
            if (e.a < 0 || e.b < 0 || e.a >= parts.Count || e.b >= parts.Count) continue;
            if (!IsVital(parts[e.a].spec.id) && !IsVital(parts[e.b].spec.id)) continue;
            float th = e.threshold * LoadFactor(e);
            if (th > 0.0001f && e.peak / th > worst) worst = e.peak / th;
        }
        return worst;
    }

    /// <summary>ROUND-1-IMPL instrument: one line per part that leaves.</summary>
    void LogDetach(int idx, string cause)
    {
        if (!detachLogOn || idx < 0 || idx >= parts.Count) return;
        var p = parts[idx];
        detachLog.Add(string.Format("{0}|{1}|{2}|hp {3:F0}/{4:F0} ({5:F0}%)|t {6:F1}",
            name, p.spec.id, cause, Mathf.Max(0f, p.hp), p.maxHp,
            p.maxHp > 0.0001f ? 100f * Mathf.Clamp01(p.hp / p.maxHp) : 0f, Time.time));
    }

    /// <summary>How many PIECES (attached parts + the wheels bolted to them)
    /// would leave the core-side body if seam <paramref name="skip"/> failed
    /// right now. Flood from the core over unbroken edges minus that seam;
    /// everything the flood misses is what the seam is carrying.</summary>
    int DetachPieces(Edge skip)
    {
        if (coreIndex < 0 || coreIndex >= parts.Count || parts[coreIndex].detached) return 1;
        loadSeen.Clear();
        loadQueue.Clear();
        loadSeen.Add(coreIndex);
        loadQueue.Add(coreIndex);
        for (int qi = 0; qi < loadQueue.Count; qi++)
        {
            int cur = loadQueue[qi];
            foreach (var ed in edges)
            {
                if (ed.broken || ed == skip) continue;
                int other = ed.a == cur ? ed.b : (ed.b == cur ? ed.a : -1);
                if (other < 0 || parts[other].detached || loadSeen.Contains(other)) continue;
                loadSeen.Add(other);
                loadQueue.Add(other);
            }
        }
        int pieces = 0;
        for (int i = 0; i < parts.Count; i++)
        {
            if (parts[i].detached || loadSeen.Contains(i)) continue;
            pieces += 1 + (WheelsOnPart != null ? WheelsOnPart(i) : 0);
        }
        return pieces;
    }

    int[] hopScratch;
    readonly List<int> hopQueue = new List<int>();

    /// <summary>Hop distance (joints crossed) from <paramref name="start"/> to
    /// every attached part, over UNBROKEN edges only; -1 = unreachable. This is
    /// the structural load path — the thing the player actually designed — and
    /// it is what a contact impulse is now attributed along.</summary>
    int[] HopsFrom(int start)
    {
        if (hopScratch == null || hopScratch.Length != parts.Count) hopScratch = new int[parts.Count];
        for (int i = 0; i < hopScratch.Length; i++) hopScratch[i] = -1;
        if (start < 0 || start >= parts.Count || parts[start].detached) return hopScratch;
        hopQueue.Clear();
        hopScratch[start] = 0;
        hopQueue.Add(start);
        for (int qi = 0; qi < hopQueue.Count; qi++)
        {
            int cur = hopQueue[qi];
            foreach (var e in edges)
            {
                if (e.broken) continue;
                int other = e.a == cur ? e.b : (e.b == cur ? e.a : -1);
                if (other < 0 || parts[other].detached || hopScratch[other] >= 0) continue;
                hopScratch[other] = hopScratch[cur] + 1;
                hopQueue.Add(other);
            }
        }
        return hopScratch;
    }

    /// <summary>Fallback entry point when the contact's collider is not one of
    /// my parts (trigger volumes, decorative children): the nearest attached
    /// part to the contact carries the load in.</summary>
    int NearestPart(Vector3 worldPos)
    {
        int best = -1;
        float bestD = float.MaxValue;
        for (int i = 0; i < parts.Count; i++)
        {
            if (parts[i].detached || parts[i].go == null) continue;
            float d = (parts[i].go.transform.position - worldPos).sqrMagnitude;
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    /// <summary>Which of my (attached) parts owns this collider? -1 = none
    /// (triggers on a part GameObject count — the spinner's hit volume IS the
    /// part). Cheap linear scan; part counts are tiny.</summary>
    public int PartIndexOf(Collider col)
    {
        if (col == null) return -1;
        for (int i = 0; i < parts.Count; i++)
            if (!parts[i].detached && parts[i].go == col.gameObject) return i;
        return -1;
    }

    void FixedUpdate()
    {
        // Round-3 fix (CRITICAL 3): stamp the moment combat arms. See
        // ENV_ARM_RAMP_S / EnvArmScale.
        if (combatEnabled != combatWasArmed)
        {
            combatWasArmed = combatEnabled;
            if (combatEnabled) combatArmedAt = Time.time;
        }
        UpdateRamEffort();   // round-7 fix 1: before the solver, before contacts
        if (pendingDestroys.Count > 0)
        {
            var todo = new List<int>(pendingDestroys);
            pendingDestroys.Clear();
            foreach (int idx in todo) DestroyPartNow(idx);
        }
        TickHanging(Time.fixedDeltaTime);
        if (pendingBreaks.Count == 0) return;
        foreach (var e in pendingBreaks)
            Log(name + ": connection BROKE — " + e.label +
                "  (stress " + e.peak.ToString("F0") + " > " + e.threshold.ToString("F0") + " N·s)");
        pendingBreaks.Clear();
        RebuildIslands();
    }

    // -------------------------------------------------- Phase 2A destruction

    readonly List<int> pendingDestroys = new List<int>();

    /// <summary>Queue a part whose HP hit zero. Deferred to FixedUpdate —
    /// DamageResolver calls this from physics callbacks (OnCollision/OnTrigger)
    /// where mutating the compound is unsafe.</summary>
    public void DestroyPart(int idx)
    {
        if (parts[idx].detached || pendingDestroys.Contains(idx)) return;
        pendingDestroys.Add(idx);
    }

    /// <summary>§4.2: the part is pulverized, not severed intact — 2-3 debris
    /// shards spawn in its place, every edge through it breaks, and the same
    /// flood-fill that handles shears severs anything it was holding on.
    /// Core destroyed ⇒ dead: the flood from the (gone) core reaches nothing,
    /// so every survivor detaches and the empty body destroys itself.</summary>
    void DestroyPartNow(int idx)
    {
        var p = parts[idx];
        if (p.detached) return;
        LogDetach(idx, "HP-DESTROYED");
        p.detached = true;
        foreach (var e in edges)
            if (!e.broken && (e.a == idx || e.b == idx)) e.broken = true;

        Vector3 vel = rb.GetPointVelocity(p.go.transform.position);
        SpawnShards(p, vel);
        Log(name + ": " + p.spec.id + " DESTROYED — blown into shards.");
        p.go.SetActive(false);   // deferred Destroy must not leave live colliders
        Destroy(p.go);

        if (idx == coreIndex && !dead)
        {
            dead = true;
            Log(name + ": CORE DESTROYED — robot dead.");
            OnRobotDead(this);
        }
        RebuildIslands();
    }

    void SpawnShards(Part p, Vector3 vel)
    {
        Vector3 s = p.spec.size;
        int n = 3;
        for (int i = 0; i < n; i++)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name + "_debris_shard";   // swept by the arena litter sweep
            go.transform.position = p.go.transform.position + new Vector3(
                Random.Range(-0.06f, 0.06f), Random.Range(0f, 0.08f), Random.Range(-0.06f, 0.06f));
            go.transform.rotation = Random.rotation;
            go.transform.localScale = s * 0.32f;
            go.GetComponent<Renderer>().sharedMaterial = MatDB.MakeRenderMat(p.mat.color);
            var srb = go.AddComponent<Rigidbody>();
            srb.mass = Mathf.Max(0.5f, p.mass / (n * 2f)); // half the mass "vaporizes"
            VelUtil.SetLinearVelocity(srb, vel + new Vector3(
                Random.Range(-1.5f, 1.5f), Random.Range(0.5f, 2f), Random.Range(-1.5f, 1.5f)));
            srb.angularVelocity = new Vector3(
                Random.Range(-6f, 6f), Random.Range(-6f, 6f), Random.Range(-6f, 6f));
            Spawned.Add(go);
            Destroy(go, 6f);     // shards are dressing, not permanent litter
        }
    }

    // ------------------------------------------------------ dynamic splitting

    /// <summary>
    /// §5.3 steps 2–4: connectivity from the core part; any subgraph no longer
    /// connected is severed and re-spawned as its own rigidbody with inherited
    /// velocity; the survivor's mass/CoM are recomputed.
    /// </summary>
    void RebuildIslands()
    {
        var reachable = FloodFrom(coreIndex, null);

        var orphans = new List<int>();
        for (int i = 0; i < parts.Count; i++)
            if (!parts[i].detached && !reachable.Contains(i)) orphans.Add(i);
        if (orphans.Count == 0) { RecomputeMass(); return; }

        // Round-5 fix 2: newly orphaned pieces HANG (see MAX_SHED_PER_STEP).
        // A dead body is wreckage — there is nothing left to hang from, so it
        // still comes apart all at once.
        bool wreck = dead || parts[coreIndex].detached;

        var ready = new List<int>();
        foreach (int i in orphans)
        {
            if (wreck) { ready.Add(i); continue; }
            if (!parts[i].hanging) StartHanging(i);
            else if (parts[i].hangTimer <= 0f) ready.Add(i);
        }
        if (ready.Count > 0) ShedSome(ready, wreck);
        RecomputeMass();
    }

    /// <summary>Round-5 fix 2: a newly orphaned part — flagged, tinted so the
    /// player can see which side of his machine is about to leave, put on the
    /// clock, and its remaining seams weakened so punishment finishes the job
    /// faster than the clock would.</summary>
    void StartHanging(int i)
    {
        var p = parts[i];
        p.hanging = true;
        p.hangTimer = HANG_LIFE;
        foreach (var e in edges)
            if (!e.broken && (e.a == i || e.b == i)) e.threshold *= HANG_SEAM_MUL;
        if (p.go != null)
        {
            var r = p.go.GetComponent<Renderer>();
            if (r != null)
                r.sharedMaterial = MatDB.MakeRenderMat(
                    Color.Lerp(p.mat.color, new Color(1f, 0.25f, 0.10f), 0.55f));
        }
        Log(name + ": " + p.spec.id + " TORN LOOSE — hanging by a thread.");
    }

    /// <summary>Drains every hanging part's clock and sheds the expired ones.
    /// Called every FixedUpdate, not only on a break, because a chunk left
    /// hanging by a hit that never repeats still has to come off.</summary>
    void TickHanging(float dt)
    {
        bool touching = Time.time - Mathf.Max(lastRobotContact, lastEnvContact) < 0.3f;
        float drain = dt * (touching ? HANG_CONTACT_DRAIN : 1f);
        List<int> ready = null;
        for (int i = 0; i < parts.Count; i++)
        {
            var p = parts[i];
            if (p.detached || !p.hanging) continue;
            p.hangTimer -= drain;
            if (p.hangTimer <= 0f) (ready ?? (ready = new List<int>())).Add(i);
        }
        if (ready == null) return;
        ShedSome(ready, false);
        RecomputeMass();
    }

    /// <summary>Spawns expired hanging parts as debris, outermost first, at most
    /// MAX_SHED_PER_STEP pieces per physics step — the same reason
    /// MAX_BREAKS_PER_STEP exists: structural loss the player cannot see happen
    /// is structural loss he cannot learn from.</summary>
    void ShedSome(List<int> ready, bool wreck)
    {
        // Outermost first = fewest live seams to other doomed parts, so a limb
        // comes apart from its tip inward rather than from the middle out.
        ready.Sort(delegate(int x, int y)
        { return LiveSeams(x, ready).CompareTo(LiveSeams(y, ready)); });
        int budget = wreck ? ready.Count : MAX_SHED_PER_STEP;
        int shed = 0;
        foreach (int i in ready)
        {
            if (shed >= budget) break;
            if (parts[i].detached) continue;
            foreach (var e in edges)
                if (!e.broken && (e.a == i || e.b == i)) e.broken = true;
            parts[i].hanging = false;
            var one = new HashSet<int>();
            one.Add(i);
            SpawnDebris(one);
            SfxSynth.Shear();
            shed++;
        }
        foreach (int i in ready)
            if (!parts[i].detached && parts[i].hanging && parts[i].hangTimer <= 0f)
                parts[i].hangTimer = HANG_REPRIEVE;
        if (shed > 0)
            Log(name + ": lost " + shed + " part(s) — mass now " +
                ActiveMass().ToString("F0") + " kg, handling will change.");
    }

    /// <summary>Live seams from part i to other parts inside `within`.</summary>
    int LiveSeams(int i, List<int> within)
    {
        int n = 0;
        foreach (var e in edges)
        {
            if (e.broken) continue;
            int other = e.a == i ? e.b : (e.b == i ? e.a : -1);
            if (other < 0 || parts[other].detached) continue;
            if (!within.Contains(other)) continue;
            n++;
        }
        return n;
    }

    HashSet<int> FloodFrom(int start, HashSet<int> restrictTo)
    {
        var seen = new HashSet<int>();
        var stack = new Stack<int>();
        if (!parts[start].detached) { stack.Push(start); seen.Add(start); }
        while (stack.Count > 0)
        {
            int cur = stack.Pop();
            foreach (var e in edges)
            {
                if (e.broken) continue;
                int other = e.a == cur ? e.b : (e.b == cur ? e.a : -1);
                if (other < 0 || seen.Contains(other) || parts[other].detached) continue;
                if (restrictTo != null && !restrictTo.Contains(other)) continue;
                seen.Add(other);
                stack.Push(other);
            }
        }
        return seen;
    }

    void SpawnDebris(HashSet<int> component)
    {
        // World CoM of the severed chunk.
        float total = 0f;
        Vector3 worldCoM = Vector3.zero;
        foreach (int i in component)
        {
            total += parts[i].mass;
            worldCoM += parts[i].mass * parts[i].go.transform.position;
        }
        worldCoM /= total;

        // Capture inherited motion BEFORE touching the hierarchy (§5.3 step 3):
        // linear velocity at the chunk's CoM (= v + ω×r) plus the body's ω.
        Vector3 vel = rb.GetPointVelocity(worldCoM);
        Vector3 angVel = rb.angularVelocity;

        var go = new GameObject(name + "_debris");
        go.transform.SetPositionAndRotation(worldCoM, transform.rotation);
        Spawned.Add(go);
        var drb = go.AddComponent<Rigidbody>();

        foreach (int i in component)
        {
            LogDetach(i, dead ? "STRUCTURAL-WRECK" : "STRUCTURAL-SHED");
            parts[i].detached = true;
            parts[i].go.transform.SetParent(go.transform, true); // keep world pose
        }

        // Mass properties of the debris body from its parts.
        float t = 0f; Vector3 weighted = Vector3.zero;
        foreach (int i in component)
        {
            t += parts[i].mass;
            weighted += parts[i].mass * parts[i].go.transform.localPosition;
        }
        drb.mass = t;
        drb.centerOfMass = weighted / t;
        drb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        drb.maxAngularVelocity = 50f;
        VelUtil.SetLinearVelocity(drb, vel);
        drb.angularVelocity = angVel;

        // Readable shear callout (round-1 fix 6): big part-rips used to show
        // no number at all — announce the chunk so the arena HUD can.
        OnPartsLost(worldCoM, component.Count);
    }

    public static void ClearAll()
    {
        foreach (var go in Spawned)
        {
            if (go == null) continue;
            // Deactivate first: Destroy is deferred to end of frame, and a
            // respawn in the same frame would overlap the old colliders and
            // cause a depenetration explosion that breaks every joint.
            go.SetActive(false);
            Destroy(go);
        }
        Spawned.Clear();
    }
}

/// <summary>Rigidbody.velocity was renamed linearVelocity in Unity 6.</summary>
public static class VelUtil
{
    public static void SetLinearVelocity(Rigidbody r, Vector3 v)
    {
#if UNITY_6000_0_OR_NEWER
        r.linearVelocity = v;
#else
        r.velocity = v;
#endif
    }

    public static Vector3 GetLinearVelocity(Rigidbody r)
    {
#if UNITY_6000_0_OR_NEWER
        return r.linearVelocity;
#else
        return r.velocity;
#endif
    }
}
}
