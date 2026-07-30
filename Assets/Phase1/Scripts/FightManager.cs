using UnityEngine;

namespace RobotBrawl.Phase0
{
/// <summary>
/// Phase 2B — match flow and win/lose (design doc §7, §7.1, §8, §12).
///
/// Owns the 90 s match: HUD (timer + both bots' HP bars), the three win
/// conditions applied symmetrically to BOTH bots, and the full-screen results
/// overlay.
///   · KO: core destroyed (CompoundRobot.dead / body gone).
///   · Count-out: flipped-or-immobile continuously for 2 s starts a visible
///     10 s count; reaching 0 loses. Self-recovery cancels it.
///   · Timeout: judges compare damageDealt; surviving mobility (grounded
///     wheels) breaks ties; still even = draw.
/// On end, controls freeze via the drives' per-instance input path (physics
/// keeps running so wreckage settles behind the overlay).
/// Stats are cached every frame so a KO'd (destroyed) body still reports its
/// damage numbers on the results screen.
/// </summary>
public class FightManager : MonoBehaviour
{
    public enum State { Settling, Fighting, Ended }
    public enum Outcome { None, PlayerWin, PlayerLoss, Draw }

    public class Side
    {
        public string label;
        public CompoundRobot bot;        // null once a KO'd body destroys itself
        public RaycastWheelDrive drive;
        public AIController ai;          // player side: null
        public float incapTimer;         // continuous flipped/immobile time
        public float countOut = -1f;     // <0 = not counting
        public bool koDead;
        // Cached stats (survive the bot GameObject's destruction).
        public float dealt, taken;
        /// <summary>Round-4 fix 5: PIECES, not body parts — wheels count.
        /// Wheels live in RaycastWheelDrive, not CompoundRobot.parts, so a bot
        /// that had lost all four of them still read "2/6 parts" with a full
        /// green bar. Losing your drivetrain is the most decisive structural
        /// loss in the game; it is in the count now, and in structFrac, and in
        /// the judges' tie-break, and it has its own pips on the bar.</summary>
        public int startParts, partsNow;
        public int startBody, bodyNow;
        public int startWheels, wheelsNow;
        /// <summary>Round-4 fix 7: the final numbers were captured at death, so
        /// nothing may overwrite them with the null-bot placeholder afterwards.</summary>
        public bool finalCaptured;
        public float hpFrac = 1f;
        public float maxHpTotal;
        public int groundedWheels;
        /// <summary>Round-3 fix 3: this build's gyro hardware, so the results
        /// screen can say whether the player HAD one rather than assume he did not.</summary>
        public GyroStabilizer gyro;
        /// <summary>Fix 2026-07-29 (playtest): gyros fitted AT THE BELL. The
        /// component dies with its part, and the results screen then read
        /// "none fitted" for a build that fitted one and lost it.</summary>
        public int startGyros;
        /// <summary>Round-3 fix 3: the measured reason, recorded at the instant
        /// the count started — not a canned sentence written at author time.</summary>
        public string incapReason = "";
        /// <summary>Round-3 fix 4: structure retained, kept SEPARATE from hp.</summary>
        public float structFrac = 1f;
        public int brokenSeams;
        /// <summary>Round-3 fix 5: post-match attribution — how much of the
        /// match this build spent on its back / not moving.</summary>
        public float flippedTime, immobileTime;
        /// <summary>Round-6 fixes 1 and 3: the section 6.2 energy budget,
        /// cached like every other stat so the live HUD can read it without a
        /// GetComponent per OnGUI and so the numbers survive the body's
        /// destruction. startCapKJ is sampled AT THE BELL, which is what makes
        /// "how much of my pack is still bolted on" answerable after a battery
        /// shears off mid-match.</summary>
        public PowerPlant power;
        public float startCapKJ, capKJ, storedKJ;
        public float pwrFrac = 1f;
        public bool pwrFlat, pwrStrained;
    }

    public static float INCAP_GRACE = 2f;   // s flipped/immobile before the count starts
    public static float COUNT_OUT = 10f;    // s of visible count-out
    public static float DEFAULT_MATCH_TIME = 90f;
    /// <summary>Round-1 fix 1: pre-bell settle window. Both bots spawn with
    /// damage + joint-stress DISABLED (CompoundRobot.combatEnabled), drop the
    /// few cm onto their suspension, and only at the bell do velocities zero,
    /// damage arm, stats re-baseline and the AI/clock start. Guarantees 100%
    /// HP and full part counts at the bell for ANY legal build.</summary>
    public static float SETTLE_TIME = 1.2f;
    /// <summary>Round-3 fix 1: seconds of mutual, in-contact, zero-progress
    /// shoving before the referee separates the bots. A scrum is a stalemate,
    /// not a defeat for whichever side the update loop evaluates first.</summary>
    public static float SHOVE_BREAK = 3f;
    /// <summary>m/s of separation the referee break imparts to each bot. Applied
    /// as a velocity change, never as a contact impulse, so it cannot shear.</summary>
    public static float SHOVE_PUSH = 2.2f;

    public BuilderManager bm;
    public Side player = new Side();
    public Side enemy = new Side();
    /// <summary>§8: set by StartFight from the chosen roster entry.</summary>
    public string enemyName = "";
    /// <summary>Seconds remaining. Public so tests can shorten the match.</summary>
    public float timer;
    public float elapsed;
    public float settleLeft;
    float bellFlash;
    public State state = State.Settling;
    public Outcome outcome = Outcome.None;
    public string causeLine = "";

    GUIStyle hudStyle, nameStyle, bigStyle, medStyle, smallStyle, btnStyle, tinyStyle;

    public void Setup(BuilderManager owner, CompoundRobot pBot, RaycastWheelDrive pDrive,
                      CompoundRobot eBot, RaycastWheelDrive eDrive, AIController eAi)
    {
        bm = owner;
        player.label = "YOU";
        player.bot = pBot; player.drive = pDrive;
        enemy.label = string.IsNullOrEmpty(enemyName) ? "MAULER" : enemyName;
        enemy.bot = eBot; enemy.drive = eDrive; enemy.ai = eAi;
        timer = DEFAULT_MATCH_TIME;
        InitSide(player);
        InitSide(enemy);
        // Pre-bell settle: protect both bots and freeze all controls.
        state = State.Settling;
        settleLeft = SETTLE_TIME;
        Protect(player);
        Protect(enemy);
    }

    void Protect(Side s)
    {
        if (s.bot != null) s.bot.combatEnabled = false;
        if (s.ai != null) s.ai.enabled = false;
        if (s.drive != null)
        {
            s.drive.useAI = true;   // zeroed AI inputs = control freeze
            s.drive.aiThrottle = 0f;
            s.drive.aiSteer = 0f;
        }
    }

    /// <summary>The bell: zero residual settle velocity, arm damage on both
    /// bots, re-baseline every stat from the (guaranteed-intact) settled
    /// state, hand controls back, start the clock.</summary>
    void Bell()
    {
        foreach (var s in new[] { player, enemy })
        {
            if (s.bot == null) continue;
            VelUtil.SetLinearVelocity(s.bot.rb, Vector3.zero);
            s.bot.rb.angularVelocity = Vector3.zero;
            s.bot.combatEnabled = true;
            s.bot.damageDealt = 0f;   // fix 5: nothing pre-bell counts as combat damage
            s.bot.damageTaken = 0f;
            InitSide(s);
            Poll(s);
        }
        if (player.drive != null) player.drive.useAI = false;  // keyboard back
        if (enemy.ai != null) enemy.ai.enabled = true;
        state = State.Fighting;
        bellFlash = 1.0f;
        CompoundRobot.Log(string.Format(
            "BELL — fight live. YOU parts {0} hp {1:F0}% · MAULER parts {2} hp {3:F0}%",
            player.partsNow, player.hpFrac * 100f, enemy.partsNow, enemy.hpFrac * 100f));
    }

    void InitSide(Side s)
    {
        s.startBody = s.bodyNow = CountParts(s.bot);
        s.startWheels = s.wheelsNow = s.drive != null ? s.drive.WheelCount() : 0;
        s.startParts = s.partsNow = s.startBody + s.startWheels;
        s.finalCaptured = false;
        s.maxHpTotal = 0f;
        foreach (var p in s.bot.parts) s.maxHpTotal += p.maxHp;
        // Round-3 fix 3: SpawnBot attaches the stabilizer to any build carrying
        // a gyro part, so its presence/absence IS the player's build decision.
        s.gyro = s.bot != null ? s.bot.GetComponent<GyroStabilizer>() : null;
        s.startGyros = (s.gyro != null && s.gyro.gyroParts != null) ? s.gyro.gyroParts.Length : 0;
        s.power = s.bot != null ? s.bot.GetComponent<PowerPlant>() : null;
        s.startCapKJ = s.capKJ = s.power != null ? s.power.capacityKJ : 0f;
        s.storedKJ = s.power != null ? s.power.storedKJ : 0f;
        s.pwrFrac = 1f; s.pwrFlat = false; s.pwrStrained = false;
        s.flippedTime = s.immobileTime = 0f;
        s.incapReason = "";
        s.structFrac = 1f;
    }

    static int CountParts(CompoundRobot b)
    {
        if (b == null) return 0;
        int n = 0;
        foreach (var p in b.parts) if (!p.detached) n++;
        return n;
    }

    void Update()
    {
        if (state == State.Ended) return;
        float dt = Time.deltaTime;
        if (state == State.Settling)
        {
            settleLeft -= dt;
            if (settleLeft <= 0f) Bell();
            return;
        }
        if (bellFlash > 0f) bellFlash -= dt;
        elapsed += dt;
        timer -= dt;

        Poll(player);
        Poll(enemy);

        // --- KO ---
        if (player.koDead && enemy.koDead) { End(Outcome.Draw, "Double KO — both cores destroyed"); return; }
        if (enemy.koDead) { End(Outcome.PlayerWin, "KO — enemy core destroyed"); return; }
        if (player.koDead) { End(Outcome.PlayerLoss, "KO — your core was destroyed"); return; }

        // --- shoving deadlock: a referee break, never a one-sided loss ---
        TickShove(dt);

        // --- count-out (both sides, decided TOGETHER) ---
        // Round-3 fix 1: the old code ticked the player first and returned on
        // any state change, so a genuinely symmetric stall always resolved
        // against the player purely by evaluation order.
        bool pOut = TickCountOut(player, dt);
        bool eOut = TickCountOut(enemy, dt);
        if (pOut && eOut) { End(Outcome.Draw, "Double count-out — neither bot could continue"); return; }
        if (eOut) { End(Outcome.PlayerWin, "Enemy counted out — " + enemy.incapReason); return; }
        if (pOut) { End(Outcome.PlayerLoss, "Counted out — " + player.incapReason); return; }

        // --- timeout → judges ---
        if (timer <= 0f) Judge();
    }

    /// <summary>Round-4 fix 7 (critic finding 7: "on a KO the results screen
    /// shows the destroyed robot at 0 damage taken and full part count"). The
    /// core dies inside a physics step; by the next Update the GameObject is
    /// gone and Poll's null branch left every cached number at whatever it was
    /// BEFORE the killing blow — a 346-damage saw KO reported the victim as
    /// untouched and 6/6 intact. CompoundRobot announces the death, so we take
    /// the last honest sample from the body while it still exists.</summary>
    void OnBotDead(CompoundRobot r)
    {
        foreach (var s in new[] { player, enemy })
        {
            if (s.bot != r || s.finalCaptured) continue;
            s.dealt = r.damageDealt;
            s.taken = r.damageTaken;
            int bs = 0;
            foreach (var e in r.edges) if (e.broken) bs++;
            s.brokenSeams = bs;
            // A dead robot is wreckage: no pieces, no structure, no health.
            s.bodyNow = s.wheelsNow = s.partsNow = 0;
            s.structFrac = 0f;
            s.hpFrac = 0f;
            s.groundedWheels = 0;
            s.capKJ = s.storedKJ = 0f; s.pwrFrac = 0f; s.pwrFlat = true; s.pwrStrained = false;
            s.koDead = true;
            s.finalCaptured = true;
        }
    }

    void OnEnable()  { CompoundRobot.OnRobotDead += OnBotDead; }
    void OnDisable() { CompoundRobot.OnRobotDead -= OnBotDead; }

    void Poll(Side s)
    {
        if (s.finalCaptured) return;   // fix 7: death numbers are final
        if (s.bot == null)
        {
            s.koDead = true; s.hpFrac = 0f; s.groundedWheels = 0;
            s.bodyNow = s.wheelsNow = s.partsNow = 0; s.structFrac = 0f;
            s.capKJ = s.storedKJ = 0f; s.pwrFrac = 0f; s.pwrFlat = true; s.pwrStrained = false;
            s.finalCaptured = true;
            return;
        }
        s.dealt = s.bot.damageDealt;
        s.taken = s.bot.damageTaken;
        s.bodyNow = CountParts(s.bot);
        s.wheelsNow = s.drive != null ? s.drive.WheelCount() : 0;
        s.partsNow = s.bodyNow + s.wheelsNow;
        // Round-3 fix 4: HP is damage to what is STILL BOLTED ON. Dividing
        // attached hp by the whole build's max hp made every sheared part read
        // as 100% damage, so the bar fell hardest exactly when nothing was
        // being hurt (measured 42.5% shown against 100.0% true). Structure lost
        // is its own number now — see structFrac and the pip row in DrawBar.
        float hp = 0f, mx = 0f;
        foreach (var p in s.bot.parts)
            if (!p.detached) { hp += Mathf.Max(0f, p.hp); mx += p.maxHp; }
        s.hpFrac = mx > 0f ? hp / mx : 0f;
        s.structFrac = s.startParts > 0 ? (float)s.partsNow / s.startParts : 0f;
        int bs = 0;
        foreach (var e in s.bot.edges) if (e.broken) bs++;
        s.brokenSeams = bs;
        s.groundedWheels = s.drive != null ? s.drive.GroundedCount() : 0;
        if (s.power != null)
        {
            s.capKJ = s.power.capacityKJ;
            s.storedKJ = s.power.storedKJ;
            s.pwrFrac = s.power.Frac;
            s.pwrFlat = s.power.Flat;
            s.pwrStrained = s.power.Strained;
        }
        if (s.bot.dead) { s.koDead = true; s.hpFrac = 0f; }
    }

    /// <summary>Per-side count-out tick. Returns TRUE on the frame the count
    /// hits zero; the caller decides the outcome so a symmetric stall is a draw
    /// rather than a loss for whoever is evaluated first.</summary>
    bool TickCountOut(Side s, float dt)
    {
        if (s.bot == null) return false;
        // Round-3 fix 5: attribution samples, collected whether or not a count
        // is running, so the results screen can say what the build actually did.
        if (Vector3.Dot(s.bot.transform.up, Vector3.up) < 0.2f) s.flippedTime += dt;
        // R2-CRITIC FIX (finding 5): a bot at full throttle grinding against
        // the opponent was logged "immobile" and then scored as the PASSIVE
        // side by the aggression judge — the exact player the judge is meant
        // to reward. Zero speed while shoving IN CONTACT now counts as
        // aggression; wall-pushing far from the opponent stays immobile.
        if (VelUtil.GetLinearVelocity(s.bot.rb).magnitude < 0.3f)
        {
            Side o = s == player ? enemy : player;
            bool shoving = s.drive != null && Mathf.Abs(s.drive.CurrentThrottle()) > 0.5f
                        && o.bot != null && o.bot.rb != null
                        && Vector3.Distance(s.bot.rb.position, o.bot.rb.position) < 1.8f;
            if (!shoving) s.immobileTime += dt;
        }

        string why;
        if (!Incap(s, out why))
        {
            // Self-recovery cancels the count.
            s.incapTimer = 0f;
            s.countOut = -1f;
            s.incapReason = "";
            return false;
        }

        s.incapTimer += dt;
        if (s.incapTimer < INCAP_GRACE) return false;
        if (s.countOut < 0f)
        {
            s.countOut = COUNT_OUT;
            // Round-3 fix 3: freeze the MEASURED reason at the instant the count
            // starts. The old screen printed one canned string ("add a self-right
            // mechanism or lower your center of mass") for every count-out; it
            // was false in 7 of 8 measured losses, including bots that were
            // upright, 4/4 wheels down, and carrying a live gyro.
            s.incapReason = why;
        }
        s.countOut -= dt;
        return s.countOut <= 0f;
    }

    /// <summary>What "incapacitated" means, plus the sentence that says so.
    ///
    /// Round-3 fix 1. The old test was `flipped || (speed &lt; 0.3 &amp;&amp; throttle
    /// &gt; 0.3)` — "trying to move and not moving" — which is precisely what
    /// SHOVING looks like. It ended 8 of 12 measured matches on a bot that was
    /// upright, had every wheel on the ground and was AHEAD on damage. A robot
    /// is incapacitated only when it has no way left to influence the match:
    ///   · upside down and not rolling back out of it,
    ///   · beached — not one wheel touching, so the drive does nothing,
    ///   · immobile at full throttle while touching NOTHING, i.e. no wall,
    ///     debris or opponent explains the stall.
    /// Pushing a wall or the opponent is legitimate and winnable; a mutual
    /// scrum is handled by TickShove instead.</summary>
    bool Incap(Side s, out string why)
    {
        why = "";
        var bot = s.bot;
        float speed = VelUtil.GetLinearVelocity(bot.rb).magnitude;
        bool flipped = Vector3.Dot(bot.transform.up, Vector3.up) < 0.2f;

        if (flipped && speed < 1.0f)
        {
            // Round-5 fix 3b: a bot that is flipped but DECISIVELY AHEAD is not
            // out of the fight, it is winning one it cannot defend from its
            // back. Counting it out handed matches to the side that was behind
            // (measured: 9 of 26 ended on this one sentence). Flipped-and-ahead
            // now runs the clock down to the judges, where the lead counts.
            if (Ahead(s)) return false;
            int live = s.gyro != null ? s.gyro.LiveGyros() : 0;
            bool fitted = s.gyro != null && s.gyro.gyroParts.Length > 0;
            why = !fitted
                ? "flipped onto its back — the emergency struts could not roll a hull this shape back over; a gyro rights you in under a second"
                : (live == 0
                    ? "flipped onto its back — its gyro had already been sheared off, leaving only the slow emergency struts"
                    : string.Format("flipped onto its back — {0} gyro(s) still live, but not enough righting torque for a hull this shape", live));
            return true;
        }

        int wheels = s.drive != null ? s.drive.WheelCount() : 0;
        // Round-4 fix 8: total wheel loss had no branch of its own, so it fell
        // through to the throttle test and the game blamed the player's MASS
        // BUDGET ("this drivetrain cannot move 179 kg") for a bot that was
        // upright with all four wheels torn off. Checked before "beached",
        // which is gated on wheels > 0 and can never see this case.
        if (wheels == 0 && speed < 0.5f)
        {
            why = string.Format("every one of its {0} wheels had been torn off — nothing left to drive with",
                                Mathf.Max(1, s.startWheels));
            return true;
        }
        if (wheels > 0 && s.groundedWheels == 0 && speed < 0.5f)
        {
            why = string.Format("beached — 0 of {0} wheels touching the ground, so the drive had nothing to push on", wheels);
            return true;
        }

        // Round-6 fix 1: a DEAD POWER PLANT had no branch of its own either,
        // so it fell through to the throttle test and the game told the player
        // his MASS BUDGET was at fault for a machine whose battery was simply
        // empty. Measured: 13 of 17 timeScale-1 count-outs printed "this
        // drivetrain cannot move N kg" and 13 of 13 of those had the losing
        // side's PowerPlant at storedKJ = 0. The advice was the exact inverse
        // of the truth - shed mass, when the answer was carry more pack or
        // spend less. Checked BEFORE the mass test, the same way wheels == 0
        // is, and it names WHICH failure it was: spent, or torn off.
        if (s.power != null && s.power.Flat && speed < 0.5f)
        {
            // ROUND-1 IMPL FIX (critic CRITICAL 1: "the energy system, not the
            // fight, decides the match - and it takes the win from the side
            // that is winning"). MEASURED over 58 matches: 25 (43.1%) were
            // decided by a power count-out, and 7 of 31 losses were suffered by
            // the side that was AHEAD on damage - every one of them by this
            // branch. Worst cases dealt 446 while taking 38 (11.7:1) and dealt
            // 579 while taking 22 (26.3:1), and lost.
            //
            // This is EXACTLY the asymmetry Round-5 fix 3b already fixed one
            // line up for the flipped case, and the argument is identical: a
            // bot that is decisively ahead is not out of the fight, it is
            // winning one it can no longer press. Running the clock down to the
            // judges, where the lead counts, is the honest resolution. A bot
            // that is behind or level still loses on a flat pack, so carrying
            // enough battery is still a build decision - it is just no longer
            // able to overrule a 26:1 scoreline.
            //
            // Note what this deliberately does NOT do: it does not give a flat
            // machine a limp mode. supplyFrac stays 0, the drive stays dead and
            // the actuators still refuse to fire - PowerPlant's header calls a
            // limp mode "exactly the kind of hidden rescue 3.1 forbids" and
            // that reasoning still holds. Only the VERDICT changes.
            if (Ahead(s)) return false;
            float lost = s.startCapKJ - s.power.capacityKJ;
            why = lost > 1f
                ? string.Format("out of power — {0:F0} kJ of its {1:F0} kJ pack was torn off the chassis, and what was left ran dry after {2:F0} s",
                                lost, s.startCapKJ, elapsed)
                : string.Format("out of power — all {0:F0} kJ spent in {1:F0} s; carry more battery, or spend less on driving and spinning",
                                s.startCapKJ, elapsed);
            return true;
        }

        // DELIVERED throttle, not requested. The mass sentence may only fire
        // when the drivetrain really is the limiting factor; CurrentThrottle()
        // still reads 1.0 on a browned-out machine that is being handed a
        // quarter of what it asked for.
        bool trying = s.drive != null && Mathf.Abs(s.drive.DeliveredThrottle()) > 0.3f;
        bool contact = Time.time - Mathf.Max(bot.lastRobotContact, bot.lastEnvContact) < 0.35f;
        if (trying && speed < 0.3f && !contact)
        {
            why = string.Format("immobile at full throttle for {0:F0} s with nothing in contact — this drivetrain cannot move {1:F0} kg",
                                INCAP_GRACE + COUNT_OUT, bot.ActiveMass());
            return true;
        }
        return false;
    }

    float shoveTimer;

    /// <summary>Round-3 fix 1b: two bots shoving each other to a standstill is a
    /// stalemate. After SHOVE_BREAK seconds of mutual, in-contact, zero-progress
    /// pushing the referee separates them (a velocity change, not a contact
    /// impulse, so it cannot shear anything) and both count clocks reset.</summary>
    void TickShove(float dt)
    {
        if (player.bot == null || enemy.bot == null) { shoveTimer = 0f; return; }
        bool stalled = VelUtil.GetLinearVelocity(player.bot.rb).magnitude < 0.4f
                    && VelUtil.GetLinearVelocity(enemy.bot.rb).magnitude < 0.4f;
        bool touching = Time.time - player.bot.lastRobotContact < 0.35f
                     || Time.time - enemy.bot.lastRobotContact < 0.35f;
        if (stalled && touching) shoveTimer += dt;
        else shoveTimer = Mathf.Max(0f, shoveTimer - 2f * dt);
        if (shoveTimer < SHOVE_BREAK) return;
        shoveTimer = 0f;

        Vector3 d = player.bot.rb.worldCenterOfMass - enemy.bot.rb.worldCenterOfMass;
        d.y = 0f;
        if (d.sqrMagnitude < 0.01f) d = Vector3.right;
        d.Normalize();
        VelUtil.SetLinearVelocity(player.bot.rb, VelUtil.GetLinearVelocity(player.bot.rb) + d * SHOVE_PUSH);
        VelUtil.SetLinearVelocity(enemy.bot.rb, VelUtil.GetLinearVelocity(enemy.bot.rb) - d * SHOVE_PUSH);
        player.incapTimer = enemy.incapTimer = 0f;
        player.countOut = enemy.countOut = -1f;
        CompoundRobot.Log("REFEREE BREAK — shoving deadlock, bots separated.");
    }

    /// <summary>Round-5 fix 4: half-width of the judges' draw band on damage
    /// dealt. Ram-only fights are near-symmetric BY CONSTRUCTION — collision
    /// damage lands on both bodies — so measured cards ran 227-221, 40-38 and
    /// 291-291. A 2.7% margin is a tie, not a win.</summary>
    public static float DRAW_BAND_FRAC = 0.08f;
    public static float DRAW_BAND_MIN = 25f;
    /// <summary>Structure needs a band for exactly the same reason damage does.
    /// Measured in the round-5 repeat sweep: run 4 of OWEN|charge was called a
    /// LOSS on 0.889 vs 0.900 pieces intact — a 1.2% difference, one bracket,
    /// well inside the noise the sweep exists to measure.</summary>
    public static float STRUCT_BAND = 0.06f;
    /// <summary>ROUND-1-IMPL FIX (critic MAJOR 6b: "on an 11-part robot the 6%
    /// STRUCT_BAND is finer than the quantisation of the metric it bands - one
    /// part is 9.1%").
    ///
    /// structFrac can only take values k/startParts, so a purely FRACTIONAL
    /// band on a small robot bands nothing at all: on 11 parts the smallest
    /// possible non-zero difference is 9.1%, already outside 6%, so structure -
    /// which Judge() checks FIRST and which short-circuits the damage column
    /// entirely - decides every card where the two machines differ by so much
    /// as a single bracket. Measured consequence in the critic's sweeps 03+04
    /// (n=8): the player out-damaged the enemy by 31.1% and LOST on 9/11 pieces
    /// against 10/11.
    ///
    /// The band is now the LARGER of the 6% fractional band (which still
    /// governs large builds, where it is coarser than one part) and the
    /// fraction that MIN_STRUCT_PARTS pieces represent on the SMALLER of the
    /// two machines. Two pieces is the critic's own suggested figure and it is
    /// the right one: one piece is inside the run-to-run noise the repeat
    /// harness exists to measure, two is a difference a spectator can point at.
    /// The 0.5 is a half-step, so a difference of EXACTLY two pieces clears the
    /// band rather than landing on it.
    ///
    /// Worked on 11 parts: band = max(0.06, 1.5/11) = 0.136. One part (0.091)
    /// now falls through to damage; two (0.182) still decides on structure.
    /// On 16 parts (owen's build): band = max(0.06, 0.094) = 0.094, so one part
    /// (0.063) falls through and two (0.125) decides. Nothing changes for a
    /// machine large enough that 6% was already coarser than one part
    /// (>= 25 parts), which is the regime the original band was written for.</summary>
    public static float MIN_STRUCT_PARTS = 2f;
    float StructBand()
    {
        int n = Mathf.Max(1, Mathf.Min(player.startParts, enemy.startParts));
        return Mathf.Max(STRUCT_BAND, (MIN_STRUCT_PARTS - 0.5f) / n);
    }
    static float DrawBand(float a, float b)
    { return Mathf.Max(DRAW_BAND_MIN, DRAW_BAND_FRAC * Mathf.Max(a, b)); }

    /// <summary>Is this side clear of the draw band? Used by both the judges and
    /// the count-out, so "ahead" means the same thing to the referee as it does
    /// on the scorecard.</summary>
    bool Ahead(Side s)
    {
        Side o = s == player ? enemy : player;
        return s.dealt - o.dealt >= DrawBand(s.dealt, o.dealt);
    }

    /// <summary>ROUND-2-CRITIC FIX (CRITICAL 3 "being toppled is currently a
    /// DEFENSIVE state" and MAJOR 6 "nothing ends in a KO; 19 of my 20 matches
    /// went to the judges' card"). Fraction-of-match difference in time spent
    /// on your back that the judges will act on. 0.10 of a 90 s match is 9 s -
    /// well outside the 0.4-4.8 s of incidental knock-downs measured in the
    /// round-1 sweeps, and far inside the 46-55 s a machine spends inverted
    /// when being flipped is its actual strategy.</summary>
    public static float CONTROL_BAND = 0.10f;
    /// <summary>R1-CRITIC FIX (2026-07-29, finding 1): how different the two
    /// sides' mobility shares (1 − immobile/elapsed) must be before the
    /// judges decide a damage-even match on AGGRESSION. Measured trigger: a
    /// bot 88% immobile vs an opponent 12% immobile is a 0.76 gap — far past
    /// this band; two normal brawlers within ~20% of each other never are.</summary>
    public static float AGGRESSION_BAND = 0.20f;

    /// <summary>Phase 5: IMGUI scale for high-DPI (phone) screens — 1 on
    /// desktop, up to 2.5x at phone DPI so the fight HUD stays readable.</summary>
    static float UIS { get { float d = Screen.dpi; return d > 250f ? Mathf.Min(2.5f, d / 160f) : 1f; } }

    /// <summary>ROUND-2-CRITIC FIX: the card is re-ordered and gains a third
    /// criterion.
    ///
    /// STRUCTURE NOW OUTRANKS DAMAGE (critic MAJOR 6's own fix_direction).
    /// Damage that never removes a piece is scratch: the critic's sweep C kept
    /// 13 of 13 pieces in all four matches while several hundred points of
    /// damage changed hands, and the card called those matches on the scratch.
    /// Pieces are the thing a spectator can see and the thing a build decision
    /// is actually about, so a side that is outside the structure band wins on
    /// it regardless of the damage column. Inside the band, damage decides
    /// exactly as before - both bands are unchanged and every previous verdict
    /// where structure was even reproduces.
    ///
    /// TIME ON YOUR BACK IS NOW SCORED (critic CRITICAL 3). With both bands
    /// even, the side that spent less of the match inverted takes the decision
    /// instead of the match being called a draw. This is the half of the topple
    /// fix that the damage multiplier cannot do: a robot that lies on its back
    /// and runs the clock down can no longer bank the draw, and a wedge bot
    /// that puts its opponent down now has something to show for it on the
    /// card. Third, not first, because it is a tie-break, not the sport.</summary>
    void Judge()
    {
        float hi = Mathf.Max(player.dealt, enemy.dealt);
        float diff = player.dealt - enemy.dealt;
        float pct = hi > 0.5f ? 100f * Mathf.Abs(diff) / hi : 0f;
        float span = Mathf.Max(1f, elapsed);
        float pFlip = player.flippedTime / span, eFlip = enemy.flippedTime / span;
        // ROUND-UP2 FIX E (round-2 critic MODERATE: "judges' damage margin is
        // unsigned, so the result readout misleads"). pct is |diff|/hi, so the
        // card printed "damage 81 vs 162 (50.1% margin)" on a match the player
        // WON and "damage 203 vs 77 (62.0% margin)" on another - the same
        // phrasing whether you dealt double or took double, on a line whose
        // whole job is to explain the verdict. The margin now names the side it
        // favours. Wording, not scoring: nothing below this reads `cmp`.
        string lead = Mathf.Abs(diff) < 0.5f ? "level"
                    : diff > 0f ? "your way" : "their way";
        // R1-CRITIC FIX (finding 1): matches were won by a bot that landed one
        // exchange then sat immobile 88% of the clock. Real robot-combat
        // judging scores aggression after damage; ours now does too — printed
        // on the card, and used as the criterion between damage and control.
        float pMob = 1f - Mathf.Clamp01(player.immobileTime / span);
        float eMob = 1f - Mathf.Clamp01(enemy.immobileTime / span);
        string cmp = string.Format(
            "Judges' decision — damage {0:F0} vs {1:F0} ({2:F1}% margin {9}) · pieces {3}/{4} vs {5}/{6}"
            + " · aggression {10:F0}% vs {11:F0}% · on its back {7:F0}% vs {8:F0}%",
            player.dealt, enemy.dealt, pct,
            player.partsNow, player.startParts, enemy.partsNow, enemy.startParts,
            100f * pFlip, 100f * eFlip, lead, 100f * pMob, 100f * eMob);

        if (Mathf.Abs(player.structFrac - enemy.structFrac) > StructBand())
        {
            bool ps = player.structFrac > enemy.structFrac;
            End(ps ? Outcome.PlayerWin : Outcome.PlayerLoss,
                cmp + " — decided on structure destroyed");
            return;
        }
        if (Mathf.Abs(diff) >= DrawBand(player.dealt, enemy.dealt))
        {
            End(diff > 0f ? Outcome.PlayerWin : Outcome.PlayerLoss,
                cmp + " — structure even, decided on damage");
            return;
        }
        if (Mathf.Abs(pMob - eMob) > AGGRESSION_BAND)
        {
            bool pa = pMob > eMob;
            End(pa ? Outcome.PlayerWin : Outcome.PlayerLoss,
                cmp + " — structure and damage even, decided on aggression: "
                + (pa ? "you" : "the enemy") + " kept moving and hunting");
            return;
        }
        if (Mathf.Abs(pFlip - eFlip) > CONTROL_BAND)
        {
            bool pc = pFlip < eFlip;
            End(pc ? Outcome.PlayerWin : Outcome.PlayerLoss,
                cmp + " — structure and damage even, decided on control: "
                + (pc ? "the enemy" : "you") + " spent the match on its back");
            return;
        }
        // All three even: the fight WAS even, and saying so is more honest than
        // resolving it on a grounded-wheel count sampled on the single expiry
        // frame — which a bot can lose to a mid-bounce, and which is a coin
        // flip dressed as a verdict.
        End(Outcome.Draw, cmp + " — too close to call, scored a draw");
    }

    public void End(Outcome o, string cause)
    {
        if (state == State.Ended) return;
        state = State.Ended;
        outcome = o;
        causeLine = cause;
        Poll(player);
        Poll(enemy);
        Freeze(player);
        Freeze(enemy);
        // Phase 4: settle scrap + ladder advancement, exactly once per fight.
        Progression.OnMatchEnd(o == Outcome.PlayerWin, player.dealt);
        // Round-2 fix 3: cut to the fixed safe overview BEFORE the results
        // overlay draws — the chase camera could end a count-out wedged in a
        // wall corner and render the results screen into wall geometry.
        var fc = Object.FindAnyObjectByType<FightCamera>();
        if (fc != null) fc.EndOverview();
        CompoundRobot.Log("MATCH OVER — " + o + ": " + cause);
    }

    /// <summary>Freeze CONTROLS, not physics: route the drive to the AI input
    /// path with zeroed inputs. Wreckage keeps settling behind the overlay.</summary>
    void Freeze(Side s)
    {
        if (s.ai != null) s.ai.enabled = false;
        if (s.drive != null)
        {
            s.drive.useAI = true;
            s.drive.aiThrottle = 0f;
            s.drive.aiSteer = 0f;
        }
    }

    // ------------------------------------------------------------------- GUI

    void OnGUI()
    {
        GUI.matrix = Matrix4x4.Scale(new Vector3(UIS, UIS, 1f));   // Phase 5: high-DPI scale
        EnsureStyles();
        if (state == State.Ended) DrawResults();
        else DrawHud();
    }

    /// <summary>Fraction of the pack seam's effective break threshold at which
    /// the HUD starts shouting. See the warning itself for why 0.70.</summary>
    public static float VITAL_WARN_FRAC = 0.70f;

    void DrawHud()
    {
        float w = 620f;
        float x = (Screen.width / UIS - w) * 0.5f;
        GUI.Box(new Rect(x, 8, w, 120), "");
        int t = Mathf.Max(0, Mathf.CeilToInt(timer));
        GUI.Label(new Rect(x, 12, w, 26), string.Format("{0}:{1:00}", t / 60, t % 60), hudStyle);
        DrawBar(x + 16, 40, 270, player);
        DrawBar(x + w - 286, 40, 270, enemy);

        if (state == State.Settling)
        {
            BigLine("READY...", new Color(1f, 0.85f, 0.25f), 0.30f);
            return;
        }
        if (bellFlash > 0f)
            BigLine("FIGHT!", new Color(0.35f, 1f, 0.45f), 0.30f);

        float ty = 0.30f;
        // ROUND-1-IMPL FIX (critic CRITICAL 3, second half): the pack seam has
        // a voice now. The critic's point was that the ONE failure that ends a
        // match outright arrived with no signal whatsoever - the player watched
        // a full-HP battery fall off and had nothing to have done differently.
        // CompoundRobot.VITAL_SEAM_MUL makes it survivable; this makes it
        // legible. 0.70 of the effective threshold, because the seam records a
        // PEAK: by the time a bay has seen 70% of what would part it, another
        // exchange of the same kind will, and "back off / turn the other side
        // to him" is still a play the player can make.
        if (player.bot != null && !player.bot.dead && player.bot.VitalSeamLoad() > VITAL_WARN_FRAC)
        {
            BigLine("BATTERY MOUNT STRESSED — TAKE HITS ON YOUR OTHER SIDE",
                    new Color(1f, 0.55f, 0.15f), ty);
            ty += 0.09f;
        }
        // Round-6 fix 3: the energy state gets the same visual weight as the
        // count-out text, because it is what starts most count-outs.
        if (player.pwrFlat)
        {
            BigLine("YOUR BATTERY IS FLAT", new Color(1f, 0.42f, 0.15f), ty);
            ty += 0.09f;
        }
        else if (player.pwrStrained)
        {
            BigLine("STRAINING — OVER YOUR POWER CEILING", new Color(1f, 0.78f, 0.2f), ty);
            ty += 0.09f;
        }
        if (player.countOut > 0f)
        {
            BigLine(string.Format("YOUR BOT COUNT-OUT: {0:F1}", player.countOut), new Color(1f, 0.32f, 0.22f), ty);
            ty += 0.09f;
        }
        if (enemy.countOut > 0f)
            BigLine(string.Format("{0} COUNT-OUT: {1:F1}", enemy.label, enemy.countOut), new Color(0.35f, 1f, 0.45f), ty);
    }

    /// <summary>Round-3 fix 4: TWO readouts, because they are two different
    /// things. The bar is DAMAGE to what is still bolted on; the pip row is
    /// STRUCTURE. Conflating them made both unreadable and hid the fact that
    /// shear, not damage, is what was actually taking robots apart.</summary>
    void DrawBar(float x, float y, float w, Side s)
    {
        GUI.Label(new Rect(x, y, w, 18),
            string.Format("{0}   {1}/{2} parts   ·   {3}/{4} wheels   ·   HP {5:F0}%",
                          s.label, s.bodyNow, s.startBody, s.wheelsNow, s.startWheels, s.hpFrac * 100f),
            nameStyle);
        Color old = GUI.color;
        GUI.color = new Color(0.12f, 0.12f, 0.14f, 0.95f);
        GUI.DrawTexture(new Rect(x, y + 20, w, 10), Texture2D.whiteTexture);
        // Round-6 fix 5: the big bar is STRUCTURE now, not hp-on-attached-parts.
        // Across 69 measured matches hpFrac never fell below 0.49 and typically
        // ended 0.82-0.99, so two thirds of its colour range was unreachable and
        // the largest element on screen tracked the quantity that decided the
        // fewest fights. structFrac is what Judge() actually scores, and it
        // measurably ranges down to 0.55; the thresholds now sit where fights
        // really live. Damage-to-attached-parts is still printed, as a number.
        GUI.color = s.structFrac > 0.85f ? new Color(0.25f, 0.9f, 0.35f)
                  : s.structFrac > 0.65f ? new Color(1f, 0.8f, 0.2f)
                  : new Color(1f, 0.3f, 0.2f);
        GUI.DrawTexture(new Rect(x, y + 20, w * Mathf.Clamp(s.structFrac, 0f, 1f), 10), Texture2D.whiteTexture);

        // Round-4 fix 5: the pip row is every PIECE — body parts in blue, then
        // the drivetrain in amber. A wheel-less wreck used to read as intact.
        int np = Mathf.Max(1, s.startBody + s.startWheels);
        float pw = Mathf.Max(1.5f, (w - (np - 1) * 2f) / np);
        for (int i = 0; i < np; i++)
        {
            bool isWheel = i >= s.startBody;
            bool live = isWheel ? (i - s.startBody) < s.wheelsNow : i < s.bodyNow;
            GUI.color = live ? (isWheel ? new Color(1f, 0.72f, 0.25f) : new Color(0.55f, 0.75f, 1f))
                             : new Color(0.42f, 0.16f, 0.14f);
            GUI.DrawTexture(new Rect(x + i * (pw + 2f), y + 34, pw, 6), Texture2D.whiteTexture);
        }

        // Round-6 fix 3: the section 6.2 ENERGY BUDGET, live. It appeared
        // nowhere in DrawHud - the player first met the number on the defeat
        // screen, attached to a cause line that blamed his mass. A build could
        // be pinned at its power ceiling for 94% of a match with nothing on
        // screen saying so. Every number here is already computed each
        // FixedUpdate; only the readout was missing.
        GUI.color = new Color(0.10f, 0.10f, 0.13f, 0.95f);
        GUI.DrawTexture(new Rect(x, y + 46, w, 8), Texture2D.whiteTexture);
        bool blink = (Time.unscaledTime % 0.6f) < 0.3f;
        GUI.color = s.pwrFlat     ? (blink ? new Color(1f, 0.30f, 0.22f) : new Color(0.45f, 0.12f, 0.10f))
                  : s.pwrStrained ? new Color(1f, 0.62f, 0.12f)
                  : s.pwrFrac <= PowerPlant.LOW_FRAC ? new Color(1f, 0.85f, 0.25f)
                  : new Color(0.30f, 0.80f, 1f);
        GUI.DrawTexture(new Rect(x, y + 46, w * (s.pwrFlat ? 1f : Mathf.Clamp01(s.pwrFrac)), 8),
                        Texture2D.whiteTexture);
        GUI.color = old;
        GUI.Label(new Rect(x, y + 55, w, 16),
            s.startCapKJ <= 0.01f ? "no power part fitted"
          : s.pwrFlat ? "BATTERY FLAT"
          : string.Format("{0:F0} / {1:F0} kJ{2}{3}", s.storedKJ, s.capKJ,
                          s.capKJ < s.startCapKJ - 1f ? "  (pack damaged)" : "",
                          s.pwrStrained ? "   STRAINING" : ""),
            tinyStyle);
    }

    void DrawResults()
    {
        Color old = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.78f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = old;

        string title = outcome == Outcome.PlayerWin ? "VICTORY"
                     : outcome == Outcome.PlayerLoss ? "DEFEAT" : "DRAW";
        bigStyle.normal.textColor = outcome == Outcome.PlayerWin ? new Color(0.35f, 1f, 0.45f)
                                  : outcome == Outcome.PlayerLoss ? new Color(1f, 0.3f, 0.25f)
                                  : new Color(1f, 0.85f, 0.3f);
        float H = Screen.height / UIS, W = Screen.width / UIS;
        GUI.Label(new Rect(0, H * 0.15f, W, 90), title, bigStyle);
        GUI.Label(new Rect(0, H * 0.29f, W, 40), causeLine, medStyle);

        int tsec = Mathf.RoundToInt(elapsed);
        // Round-3 fix 5: attribute the result to build decisions. The sim
        // already computes every one of these numbers — a Titanium build losing
        // 0 parts where the Aluminium twin lost 3 was invisible to the player
        // because both matches printed the same one-line verdict.
        GUI.Label(new Rect(0, H * 0.375f, W, 26), SideLine("YOU", player), smallStyle);
        GUI.Label(new Rect(0, H * 0.415f, W, 26), SideDetail(player), smallStyle);
        GUI.Label(new Rect(0, H * 0.465f, W, 26), SideLine(enemy.label, enemy), smallStyle);
        GUI.Label(new Rect(0, H * 0.505f, W, 26), SideDetail(enemy), smallStyle);
        GUI.Label(new Rect(0, H * 0.555f, W, 26),
            string.Format("Match time {0}:{1:00}", tsec / 60, tsec % 60), smallStyle);
        // Phase 4: what this fight paid, and what it unlocked.
        if (Progression.lastRewardLine.Length > 0)
            GUI.Label(new Rect(0, H * 0.598f, W, 26), Progression.lastRewardLine, smallStyle);

        float bw = 220f, bh = 46f, by = H * 0.645f;
        float cx = W * 0.5f;
        if (GUI.Button(new Rect(cx - bw - 12f, by, bw, bh), "REMATCH  (R)", btnStyle))
            bm.ResetFight();
        if (GUI.Button(new Rect(cx + 12f, by, bw, bh), "BACK TO BUILDER  (B)", btnStyle))
            bm.BackToBuild();
    }

    string SideLine(string label, Side s)
    {
        return string.Format("{0} — dealt {1:F0} ({2:F1}/s) · taken {3:F0} · HP {4:F0}% · parts {5}/{6} · wheels {7}/{8}",
            label, s.dealt, s.dealt / Mathf.Max(1f, elapsed), s.taken,
            s.hpFrac * 100f, s.bodyNow, s.startBody, s.wheelsNow, s.startWheels);
    }

    string SideDetail(Side s)
    {
        float span = Mathf.Max(1f, elapsed);
        var pp = s.bot != null ? s.bot.GetComponent<PowerPlant>() : null;
        string pwr = pp == null ? "n/a"
                   : pp.Flat ? "FLAT"
                   : string.Format("{0:F0}%{1}", pp.Frac * 100f, pp.Strained ? " (straining)" : "");
        return string.Format("      seams sheared {0} · flipped {1:F0}% of the match · immobile {2:F0}% · gyros {3} · battery {4}",
            s.brokenSeams, 100f * s.flippedTime / span, 100f * s.immobileTime / span,
            s.startGyros == 0 ? "none fitted"
                : ((s.gyro == null ? 0 : s.gyro.LiveGyros()) + " of " + s.startGyros + " live"), pwr);
    }

    void BigLine(string text, Color c, float yFrac)
    {
        float y = Screen.height / UIS * yFrac;
        medStyle.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
        GUI.Label(new Rect(3f, y + 3f, Screen.width / UIS, 60f), text, medStyle);
        medStyle.normal.textColor = c;
        GUI.Label(new Rect(0f, y, Screen.width / UIS, 60f), text, medStyle);
    }

    void EnsureStyles()
    {
        if (hudStyle != null) return;
        hudStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        nameStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
        bigStyle = new GUIStyle(GUI.skin.label) { fontSize = 64, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        medStyle = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true };
        smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 17, alignment = TextAnchor.MiddleCenter };
        btnStyle = new GUIStyle(GUI.skin.button) { fontSize = 17, fontStyle = FontStyle.Bold };
        tinyStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold };
    }
}

/// <summary>Scripted-test telemetry (dev/verification only — attached by the
/// automated test pipeline, never by gameplay code): 1 Hz samples of both
/// bots' HP/parts/viewport positions, camera position, AI state and count-out
/// timers, plus a BELL line the instant the match goes live and every
/// CompoundRobot.Log line. Read via Dump().</summary>
public class FightTelemetry : MonoBehaviour
{
    public FightManager fm;
    public float interval = 1f;
    public System.Text.StringBuilder sb = new System.Text.StringBuilder();
    float next;
    bool bellSeen;
    System.Action<string> hook;

    public string Dump() { return sb.ToString(); }

    void OnEnable()
    {
        hook = s => sb.Append("  LOG ").Append(s).Append('\n');
        CompoundRobot.Log += hook;
    }

    void OnDisable() { if (hook != null) CompoundRobot.Log -= hook; }

    void Update()
    {
        if (fm == null) fm = Object.FindAnyObjectByType<FightManager>();
        if (fm == null) return;
        if (!bellSeen && fm.state == FightManager.State.Fighting)
        {
            bellSeen = true;
            Sample("BELL");
        }
        if (Time.time < next) return;
        next = Time.time + interval;
        Sample(fm.state.ToString());
    }

    void Sample(string tag)
    {
        var line = new System.Text.StringBuilder();
        line.Append(string.Format("[{0:F1}] {1} t={2:F1} P(hp={3:F1}% parts={4} dealt={5:F1}) E(hp={6:F1}% parts={7} dealt={8:F1})",
            Time.time, tag, fm.timer,
            fm.player.hpFrac * 100f, fm.player.partsNow, fm.player.dealt,
            fm.enemy.hpFrac * 100f, fm.enemy.partsNow, fm.enemy.dealt));
        var cam = Camera.main;
        if (cam != null)
        {
            Vector3 cp = cam.transform.position;
            line.Append(string.Format(" cam=({0:F2},{1:F2},{2:F2})", cp.x, cp.y, cp.z));
            if (fm.player.bot != null)
            {
                Vector3 v = cam.WorldToViewportPoint(fm.player.bot.transform.position);
                line.Append(string.Format(" vpP=({0:F2},{1:F2},{2:F1})", v.x, v.y, v.z));
            }
            if (fm.enemy.bot != null)
            {
                Vector3 v = cam.WorldToViewportPoint(fm.enemy.bot.transform.position);
                line.Append(string.Format(" vpE=({0:F2},{1:F2},{2:F1})", v.x, v.y, v.z));
            }
        }
        if (fm.enemy.ai != null) line.Append(" ai=").Append(fm.enemy.ai.state);
        if (fm.player.countOut > 0f || fm.enemy.countOut > 0f)
            line.Append(string.Format(" co=({0:F1},{1:F1})", fm.player.countOut, fm.enemy.countOut));
        sb.Append(line).Append('\n');
    }
}

}