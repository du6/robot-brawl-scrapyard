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
    /// <summary>The fight in progress, or null.
    ///
    /// R9 (owen 2026-08-04, smoothness pass): MobileBuilderUI.Update asked
    /// "is a fight running?" with FindFirstObjectByType EVERY FRAME - a
    /// scene-wide type scan, measured at 0.087 ms and 49 bytes per call in a
    /// 244-object scene, for an answer that changes about twice a minute. It
    /// also scales with scene size, so it costs most exactly when a big robot
    /// is on the floor and the frame is already busy.
    ///
    /// Assigned in Awake and cleared in OnDestroy rather than by whoever
    /// happens to start a fight - the object's own lifetime is the only thing
    /// that is always right, including when a fight ends by being destroyed.</summary>
    public static FightManager current;

    void Awake() { current = this; }
    void OnDestroy() { if (current == this) current = null; programBannerNow = false; }

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

    /// <summary>========== END A BOUT THAT IS OVER (owen, 2026-08-08) =======
    /// The ladder sweep measured a mean of 53.0 s of dead air in a 66.8 s bout
    /// — 79% of what §1.1 of the multiplayer design doc asks players to watch.
    ///
    /// This rule is NOT "it looks boring". It is stricter than that, and that
    /// is what makes it safe: STRUCT_RAM_DMG is 0, so a part with no weapon
    /// edge cannot deal HP damage at all (DamageResolver, "bumps are not
    /// attacks"). Once BOTH machines have lost every edge part, no further
    /// damage is PHYSICALLY POSSIBLE between them — the remaining clock cannot
    /// change the scorecard, only pad it. The quiet window is what keeps it
    /// honest in a hazard arena, where the environment can still deal damage:
    /// any damage at all, from any source, resets it.
    ///
    /// It ends through the existing judges, so the verdict is decided by the
    /// same criteria as any timeout — nothing new scores a fight.</summary>
    public static float STALL_QUIET = 12f;
    /// <summary>...and the other tail: nobody has landed anything at all.
    /// Every first hit in the 30-bout sweep landed by 5.3 s, so 30 s is far
    /// outside the distribution of a fight that is merely slow to start.</summary>
    public static float STALL_NO_CONTACT = 30f;
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

    /// <summary>P0 (2026-08-05): who drives the PLAYER side when the bell
    /// hands controls back. Keyboard for every human fight; a bench sets this
    /// to AI on the fight INSTANCE right after starting the fight (and a P2+
    /// Autonomy League fight will set Program for both sides). Per-instance on
    /// purpose: it dies with the fight, so a crashed bench can never leak AI
    /// control into a human's next match — the static-leak hazard the old
    /// per-frame useAI reasserts existed to fight. This field plus
    /// Protect/Bell/Freeze below are THE ONLY writers of
    /// CompoundRobot.controlSource during a fight.</summary>
    public ControlSource playerSource = ControlSource.Keyboard;
    /// <summary>P3c harness seam: true while the HUD is drawing the
    /// PROGRAM ARMED banner (bannerNow precedent — state, not pixels).</summary>
    public static bool programBannerNow;
    /// <summary>P2: the enemy side's grant, same contract. AI for every
    /// roster fight (the spawn default), Program for an Autonomy League bout
    /// or a ProgramBench series — both sides through the single authority.</summary>
    public ControlSource enemySource = ControlSource.AI;

    public BuilderManager bm;
    AudioSource music;   // fight theme, started in Setup, stopped in End
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
    /// <summary>Build stamp - bumped by hand whenever this file changes, so a
    /// harness can PROVE which assembly the editor actually loaded rather than
    /// inferring it from DLL timestamps (which lied once during round 3).</summary>
    public static readonly string BuildStamp = "r4-b1";
    /// <summary>Round-3 probe: the HUD header width DrawHud last used, so a
    /// harness can prove from inside play mode which build is really drawing
    /// instead of measuring pixels in a screenshot.</summary>
    public static float lastHudW;
    public Outcome outcome = Outcome.None;
    public string causeLine = "";
    // ROUND-3 (critic CRITICAL 2c): the contest identity and its money, cached
    // in End() because Career.SettleFight() nulls activeLeague/activeContest
    // before DrawResults() ever runs. Read nowhere else.
    bool cIsContest; string cLeague = "", cArena = ""; int cPurse, cPay;
    /// <summary>MEDALS (2026-08-02): the championship line, cached in End()
    /// from Career.lastMedal for the same reason every other field here is -
    /// SettleFight clears its own handoff state, so reading it from
    /// DrawResults() would yield nothing.</summary>
    string cMedal = "";

    GUIStyle hudStyle, nameStyle, bigStyle, medStyle, smallStyle, btnStyle, tinyStyle;
    GUIStyle idStyle, hudIdStyle, moneyStyle, moneySmall, medalStyle;

    public void Setup(BuilderManager owner, CompoundRobot pBot, RaycastWheelDrive pDrive,
                      CompoundRobot eBot, RaycastWheelDrive eDrive, AIController eAi)
    {
        bm = owner;
        player.label = "YOU";
        player.bot = pBot; player.drive = pDrive;
        enemy.label = string.IsNullOrEmpty(enemyName) ? "MAULER" : enemyName;
        enemy.bot = eBot; enemy.drive = eDrive; enemy.ai = eAi;
        timer = DEFAULT_MATCH_TIME;
        // P1: the compass tracker's "match beacon" — each side's SensorBus
        // (if the build carries sensors) tracks the OTHER machine.
        if (pBot != null)
        {
            var pb = pBot.GetComponent<SensorBus>();
            if (pb != null) pb.target = eBot;
        }
        if (eBot != null)
        {
            var eb = eBot.GetComponent<SensorBus>();
            if (eb != null) eb.target = pBot;
        }
        InitSide(player);
        InitSide(enemy);
        // Pre-bell settle: protect both bots and freeze all controls.
        state = State.Settling;
        settleLeft = SETTLE_TIME;
        Protect(player);
        Protect(enemy);

        // Fight music. Lives in Resources so it ships in device builds; loops
        // for the match, stops at the verdict.
        //
        // OWEN 2026-08-04: "randomly pick one of the three songs as background
        // music when user enters fight mode." Uniform pick per match - note
        // that with three tracks a back-to-back repeat lands one time in
        // three, which tends to read as broken shuffle; say the word and this
        // becomes an exclude-the-last pick instead.
        //
        // ChromeWar was converted on 2026-08-02 and never wired (see
        // Battle_Music_ChromeWar_2026-08-02.md, "NOT done - the wiring"), so
        // this is the commit that actually puts it in the game.
        string pick = FIGHT_THEMES[UnityEngine.Random.Range(0, FIGHT_THEMES.Length)];
        var musicClip = Resources.Load<AudioClip>(pick);
        // One missing file must not mean SILENCE while the other two are
        // sitting there - fall through the list rather than trusting one Load.
        if (musicClip == null)
            foreach (var alt in FIGHT_THEMES)
            {
                musicClip = Resources.Load<AudioClip>(alt);
                if (musicClip != null) { pick = alt; break; }
            }
        lastFightTheme = musicClip != null ? pick : "";
        if (musicClip != null)
        {
            music = gameObject.AddComponent<AudioSource>();
            music.clip = musicClip;
            music.loop = true;
            music.volume = 0.55f;
            music.spatialBlend = 0f;   // 2D: same in both ears, everywhere
            music.Play();
        }
        else CompoundRobot.Log("no fight theme found in Resources - fight is silent");
    }

    /// <summary>The three fight tracks, by Resources name. A harness can assert
    /// every one of them still loads - a renamed or missing file would
    /// otherwise just quietly shrink the pool and nobody would notice which
    /// song stopped appearing.</summary>
    public static readonly string[] FIGHT_THEMES =
    { "FightTheme", "FightTheme_ChromeWar", "FightTheme_AshAndWire" };
    /// <summary>Which one this match drew. "" = none loaded.</summary>
    public static string lastFightTheme = "";

    void Protect(Side s)
    {
        if (s.bot != null) s.bot.combatEnabled = false;
        if (s.ai != null) s.ai.enabled = false;
        // P0: control freeze = route to the AI path with zeroed inputs, said
        // once through the single authority instead of the old useAI boolean.
        if (s.bot != null) s.bot.controlSource = ControlSource.AI;
        if (s.drive != null)
        {
            s.drive.aiThrottle = 0f;
            s.drive.aiSteer = 0f;
            s.drive.ClearWheelCmd();               // P2: a Program bot freezes
            s.drive.directWheelCmd = false;        // like any other machine
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
        // The house comes up on the bell and goes quiet at the verdict.
        CrowdAudio.Begin();
        // P0: the historic bell-bug line. The player side now gets whatever
        // this FIGHT says it gets — Keyboard for humans, AI for a bench,
        // Program for an Autonomy League bout — through the single authority.
        if (player.bot != null) player.bot.controlSource = playerSource;
        // P2: the enemy side gets the same treatment — its AIController only
        // wakes when this fight actually routes the enemy to AI.
        if (enemy.bot != null) enemy.bot.controlSource = enemySource;
        if (enemy.ai != null && enemySource == ControlSource.AI) enemy.ai.enabled = true;
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

        // --- the fight is over before the clock is (2026-08-08) ---
        if (TickStalemate()) return;

        // --- timeout → judges ---
        if (timer <= 0f) Judge();
    }

    /// <summary>Live weapon parts on this side. `IsEdge` is the codebase's
    /// own definition of weapon-ness (PartSpec.edgeHardness), the same one
    /// DamageResolver gates bump damage on.</summary>
    int WeaponsAlive(Side s)
    {
        if (s.bot == null) return 0;
        int n = 0;
        foreach (var p in s.bot.parts)
            if (!p.detached && DamageResolver.IsEdge(p.spec.edgeHardness)) n++;
        return n;
    }

    float damageSeen;
    float lastDamageAt;
    bool anyDamage;

    /// <summary>See STALL_QUIET. Returns true if it called the fight.
    /// Watches damage TAKEN, not dealt, so environmental damage with no
    /// attacker (hazards) still counts as the fight being alive.</summary>
    bool TickStalemate()
    {
        float total = player.taken + enemy.taken;
        if (total > damageSeen + 0.01f) { damageSeen = total; lastDamageAt = elapsed; anyDamage = true; }

        if (!anyDamage)
        {
            if (elapsed >= STALL_NO_CONTACT)
            {
                Judge(string.Format("Called early — no contact in {0:F0} s", STALL_NO_CONTACT));
                return true;
            }
            return false;
        }
        if (elapsed - lastDamageAt >= STALL_QUIET &&
            WeaponsAlive(player) == 0 && WeaponsAlive(enemy) == 0)
        {
            Judge(string.Format(
                "Called early — both machines disarmed, no damage possible for {0:F0} s", STALL_QUIET),
                true);
            return true;
        }
        return false;
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
                ? "flipped onto its back with nothing aboard to right it — fit a gyro, or build an arm that can push you back over"
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
    public static float DrawBand(float a, float b)
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

    /// <summary>IMGUI scale for the fight HUD AND the results screen. R4
    /// (critic finding 3): this used to be its own copy of the dpi rule, so at
    /// the dpi this editor reports it evaluated to exactly 1 and round 3's
    /// results screen shipped at a scale it was not designed for while the
    /// career panel next door scaled. One rule, one place.</summary>
    static float UIS { get { return BuilderManager.GuiScale; } }

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
    void Judge() { Judge(null); }

    void Judge(string earlyNote, bool bothDisarmed = false)
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

        if (!string.IsNullOrEmpty(earlyNote)) cmp = earlyNote + " · " + cmp;

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
        // MEASURED 2026-08-09 (see Opening_Disarm_Tutorial_Floor doc). When the
        // opening exchange shears BOTH weapons, neither machine can score
        // again - and this tie-break then hands the bout to whichever robot is
        // lighter, because it is the one still moving. Over 20 sampled L1
        // bouts the disarmed player went 0-for-6, and every single loss read
        // "decided on aggression". That is not a tie-break, it is a penalty
        // for having been disarmed by the other robot's weapon.
        //
        // Structure and damage above STILL decide a disarmed bout: if you
        // broke more of them before the trade, you won it. Only "who looked
        // busier once nobody could hurt anybody" is struck out. CONTROL below
        // is deliberately KEPT - spending the match on your back is a real
        // failure, weapons or no weapons, not an artefact of mass.
        if (!bothDisarmed && Mathf.Abs(pMob - eMob) > AGGRESSION_BAND)
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
        End(Outcome.Draw, cmp + (bothDisarmed
            ? " — both machines disarmed with nothing left to settle it, scored a draw"
            : " — too close to call, scored a draw"));
    }

    public void End(Outcome o, string cause)
    {
        if (state == State.Ended) return;
        state = State.Ended;
        if (music != null) music.Stop();   // the verdict gets silence
        // One last roar for a decisive finish, then the arena empties. A draw
        // or a count-out gets no roar - the crowd is not impressed by a clock.
        if (o == Outcome.PlayerWin || o == Outcome.PlayerLoss) CrowdAudio.Surge(1f);
        CrowdAudio.Stop();
        outcome = o;
        causeLine = cause;
        Poll(player);
        Poll(enemy);
        Freeze(player);
        Freeze(enemy);
        // ROUND-3 FIX (critic CRITICAL 2c): cache the contest identity HERE.
        // Career.SettleFight() nulls activeLeague/activeContest as its third
        // statement, long before DrawResults() draws, so reading them from the
        // results screen yields two empty strings and an unnamed title fight.
        cIsContest = false; cLeague = ""; cArena = ""; cPurse = 0; cPay = 0;
        if (Career.active && !string.IsNullOrEmpty(Career.activeContest))
        {
            var clg = Career.FindLeague(Career.activeLeague);
            var ccon = Career.FindContest(clg, Career.activeContest);
            if (clg != null && ccon != null)
            {
                cIsContest = true;
                cLeague = clg.name.ToUpper();
                cArena = clg.arenaName.ToUpper();
                // First-win rule (owen, 2026-08-13): a beaten contest shows
                // purse 0 — the results screen tells the same story the
                // settlement pays. (Entry fees are gone league-wide.)
                bool re = Career.Data.doneContests.Contains(ccon.id);
                cPurse = re ? 0 : ccon.purse;
            }
        }
        Career.lastSettled = false;
        Career.lastMedal = null; cMedal = "";
        // Phase 4: settle scrap + ladder advancement, exactly once per fight.
        Progression.OnMatchEnd(o == Outcome.PlayerWin, player.dealt);
        // ...and what the settlement actually paid. If it did not run as a
        // contest (already rewarded, exhibition), drop the contest framing
        // entirely rather than print a purse that was never won.
        if (Career.lastSettled) cPay = Career.lastPay; else cIsContest = false;
        // MEDALS: SettleFight mints at most one per settle and nulls the field
        // on every settle, so this is never a stale championship.
        if (Career.lastMedal != null)
            cMedal = "\u2605  " + Career.lastMedal.leagueName.ToUpper() + " CHAMPION  \u2605";
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
        if (s.bot != null) s.bot.controlSource = ControlSource.AI;   // P0: single authority
        if (s.drive != null)
        {
            s.drive.aiThrottle = 0f;
            s.drive.aiSteer = 0f;
            s.drive.ClearWheelCmd();               // P2: a Program bot freezes
            s.drive.directWheelCmd = false;        // like any other machine
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
        // Critic round 1 (mobile): no touch way to leave a running fight
        // (B is a keyboard key). Small corner button, far from the pads.
        if (MobileBuilderUI.Active && bm != null)
            if (GUI.Button(new Rect(10f, 8f, 88f, 36f), "QUIT")) { bm.BackToBuild(); return; }
        // ROUND-3 FIX (critic MAJOR 4): 620 was too narrow for the side
        // lines, which clipped mid-string - the player's row ended on an
        // orphan separator and the opponent's lost the word "wheels".
        // R5 finding 2: the side lines are 18pt now, not 14. Measured against
        // the longest real string the HUD prints - "WIDOWMAKER · 8/8 parts ·
        // 4/4 wheels · HP 88%" - which clipped its own percentage at 900/425.
        float w = 1010f;
        lastHudW = w;
        float x = (Screen.width / UIS - w) * 0.5f;
        // ...and the fight now says WHICH fight it is. Arriving at the World
        // Championship in The Crucible for a 3000 purse looked exactly like an
        // exhibition: no league, no arena, no purse anywhere on screen.
        string cid = ContestHudLine();
        float dy = cid == null ? 0f : 22f;
        GUI.Box(new Rect(x, 8, w, 130 + dy), "");
        if (cid != null) GUI.Label(new Rect(x, 11, w, 26), cid, hudIdStyle);
        int t = Mathf.Max(0, Mathf.CeilToInt(timer));
        GUI.Label(new Rect(x, 12 + dy, w, 26), string.Format("{0}:{1:00}", t / 60, t % 60), hudStyle);
        DrawBar(x + 16, 40 + dy, 482, player);
        DrawBar(x + w - 498, 40 + dy, 482, enemy);

        // P3c: the armed banner (design §6) — a fight the autopilot drives
        // says so, loudly, for the WHOLE fight (persistent mode banner, the
        // ModeBanner pattern): the player's hands are off the sticks and the
        // screen must never let them forget why. playerSource is the single
        // authority the bell writes; programBannerNow is the harness seam.
        programBannerNow = playerSource == ControlSource.Program;
        if (programBannerNow)
        {
            var abst = new GUIStyle(GUI.skin.box);
            abst.fontSize = 15; abst.fontStyle = FontStyle.Bold;
            var apc = GUI.color;
            GUI.color = new Color(1f, 0.8f, 0.25f, 0.95f);
            GUI.Box(new Rect(x + (w - 520f) * 0.5f, 142f + dy, 520f, 28f),
                    "PROGRAM ARMED — autopilot drives this fight", abst);
            GUI.color = apc;
        }

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
        GUI.Label(new Rect(x, y, w, 23),
            string.Format("{0} · {1}/{2} parts · {3}/{4} wheels · HP {5:F0}%",
                          s.label, s.bodyNow, s.startBody, s.wheelsNow, s.startWheels, s.hpFrac * 100f),
            nameStyle);
        Color old = GUI.color;
        GUI.color = new Color(0.12f, 0.12f, 0.14f, 0.95f);
        GUI.DrawTexture(new Rect(x, y + 24, w, 10), Texture2D.whiteTexture);
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
        GUI.DrawTexture(new Rect(x, y + 24, w * Mathf.Clamp(s.structFrac, 0f, 1f), 10), Texture2D.whiteTexture);

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
            GUI.DrawTexture(new Rect(x + i * (pw + 2f), y + 38, pw, 6), Texture2D.whiteTexture);
        }

        // Round-6 fix 3: the section 6.2 ENERGY BUDGET, live. It appeared
        // nowhere in DrawHud - the player first met the number on the defeat
        // screen, attached to a cause line that blamed his mass. A build could
        // be pinned at its power ceiling for 94% of a match with nothing on
        // screen saying so. Every number here is already computed each
        // FixedUpdate; only the readout was missing.
        GUI.color = new Color(0.10f, 0.10f, 0.13f, 0.95f);
        GUI.DrawTexture(new Rect(x, y + 50, w, 8), Texture2D.whiteTexture);
        bool blink = (Time.unscaledTime % 0.6f) < 0.3f;
        GUI.color = s.pwrFlat     ? (blink ? new Color(1f, 0.30f, 0.22f) : new Color(0.45f, 0.12f, 0.10f))
                  : s.pwrStrained ? new Color(1f, 0.62f, 0.12f)
                  : s.pwrFrac <= PowerPlant.LOW_FRAC ? new Color(1f, 0.85f, 0.25f)
                  : new Color(0.30f, 0.80f, 1f);
        GUI.DrawTexture(new Rect(x, y + 50, w * (s.pwrFlat ? 1f : Mathf.Clamp01(s.pwrFrac)), 8),
                        Texture2D.whiteTexture);
        GUI.color = old;
        GUI.Label(new Rect(x, y + 61, w, 20),
            s.startCapKJ <= 0.01f ? "no power part fitted"
          : s.pwrFlat ? "BATTERY FLAT"
          : string.Format("{0:F0} / {1:F0} kJ{2}{3}", s.storedKJ, s.capKJ,
                          s.capKJ < s.startCapKJ - 1f ? "  (pack damaged)" : "",
                          s.pwrStrained ? "   STRAINING" : ""),
            tinyStyle);
    }

    void DrawResults()
    {
        programBannerNow = false;   // P3c: the fight is over; so is the banner
        Color old = GUI.color;
        float H = Screen.height / UIS, W = Screen.width / UIS;
        Color accent = outcome == Outcome.PlayerWin  ? new Color(0.35f, 1f, 0.45f)
                     : outcome == Outcome.PlayerLoss ? new Color(1f, 0.32f, 0.26f)
                     :                                 new Color(1f, 0.85f, 0.30f);

        // ROUND-3 FIX (critic CRITICAL 2a): winning the World Championship and
        // being destroyed used to be the SAME PICTURE apart from one word. The
        // backdrop and a band behind the verdict now carry the outcome as
        // colour, so the two read apart before a single glyph is parsed.
        // These look absurdly dark as numbers because the project renders in
        // LINEAR colour space: GUI.color is a linear value, so 0.013 lands on
        // screen at roughly sRGB 0.14. The first attempt used sRGB-looking
        // numbers and produced a pale wash the arena showed straight through.
        // R5 (critic finding 3): at 0.93 the arena's emissive floor paint - the
        // 24-segment centre circle and the two full-width radial lines - still
        // burned straight through the backdrop and lay across `NET -160 SCRAP`
        // and both buttons, which reads as debug geometry over the single most
        // important line in the career loop. 0.985 is opaque to an emissive
        // without going flat black.
        GUI.color = outcome == Outcome.PlayerWin  ? new Color(0.0012f, 0.0110f, 0.0028f, 0.985f)
                  : outcome == Outcome.PlayerLoss ? new Color(0.0130f, 0.0011f, 0.0011f, 0.985f)
                  :                                 new Color(0.0100f, 0.0070f, 0.0006f, 0.985f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = new Color(accent.r, accent.g, accent.b, 0.20f);
        GUI.DrawTexture(new Rect(0, H * 0.145f, W, 96f), Texture2D.whiteTexture);
        GUI.color = accent;
        GUI.DrawTexture(new Rect(0, H * 0.145f, W, 4f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(0, H * 0.145f + 92f, W, 4f), Texture2D.whiteTexture);
        GUI.color = old;

        // (2c) The contest gets a name. Cached in End(); see the field comment.
        if (cIsContest)
        {
            idStyle.normal.textColor = new Color(0.88f, 0.90f, 0.95f);
            GUI.Label(new Rect(0, H * 0.095f, W, 30), cLeague + "   ·   " + cArena, idStyle);
        }

        string title = outcome == Outcome.PlayerWin ? "VICTORY"
                     : outcome == Outcome.PlayerLoss ? "DEFEAT" : "DRAW";
        bigStyle.normal.textColor = accent;
        GUI.Label(new Rect(0, H * 0.15f, W, 90), title, bigStyle);
        // (2a) medStyle is SHARED with BigLine's mid-fight toasts, which write
        // it and used not to restore it - so the colour of the line explaining
        // how the match ended was decided by whichever warning fired last.
        // Set it explicitly here AND restore it in BigLine; either alone is a
        // fix, both together mean nothing can leak in future.
        medStyle.normal.textColor = accent;
        GUI.Label(new Rect(0, H * 0.29f, W, 40), causeLine, medStyle);

        int tsec = Mathf.RoundToInt(elapsed);
        // Round-3 fix 5: attribute the result to build decisions. The sim
        // already computes every one of these numbers — a Titanium build losing
        // 0 parts where the Aluminium twin lost 3 was invisible to the player
        // because both matches printed the same one-line verdict.
        GUI.Label(new Rect(0, H * 0.375f, W, 30), SideLine("YOU", player), smallStyle);
        GUI.Label(new Rect(0, H * 0.415f, W, 30), SideDetail(player), smallStyle);
        GUI.Label(new Rect(0, H * 0.465f, W, 30), SideLine(enemy.label, enemy), smallStyle);
        GUI.Label(new Rect(0, H * 0.505f, W, 30), SideDetail(enemy), smallStyle);
        GUI.Label(new Rect(0, H * 0.555f, W, 30),
            string.Format("Match time {0}:{1:00}", tsec / 60, tsec % 60), smallStyle);
        // ---------------------------------------------------------- MONEY
        // ROUND-3 FIX (critic CRITICAL 2b) - this was a TRUST bug, not a
        // hierarchy one. The screen printed the gross award with a plus sign
        // and never mentioned the entry fee, so a World Championship loss that
        // cost the player 160 scrap net was reported to them as "+40". Show
        // the arithmetic: purse, bonus, fee, net. A player must never be told
        // they gained money in a match where they lost money.
        float my = H * 0.585f;
        if (cIsContest)
        {
            // Entry fees are gone (owen, 2026-08-13): the arithmetic line is
            // purse + bonus = net, nothing subtracted, and net can no longer
            // go negative on a win.
            int net = cPay;
            string line1 = outcome == Outcome.PlayerWin
                ? string.Format("PURSE {0}      BONUS {1}{2}",
                                cPurse, cPay - cPurse < 0 ? "−" : "+", Mathf.Abs(cPay - cPurse))
                : string.Format("PURSE {0} NOT WON      CONSOLATION +{1}",
                                cPurse, cPay);
            moneySmall.normal.textColor = new Color(0.72f, 0.74f, 0.80f);
            GUI.Label(new Rect(0, my, W, 26), line1, moneySmall);
            moneyStyle.normal.textColor = net >= 0 ? new Color(0.40f, 1f, 0.50f)
                                                   : new Color(1f, 0.42f, 0.34f);
            GUI.Label(new Rect(0, my + 27f, W, 36),
                string.Format("NET {0}{1} SCRAP          BALANCE {2}",
                              net >= 0 ? "+" : "−", Mathf.Abs(net), Career.Data.scrap),
                moneyStyle);
        }
        else if (Progression.lastRewardLine.Length > 0)
        {
            GUI.Label(new Rect(0, my + 14f, W, 30), Progression.lastRewardLine, smallStyle);
        }

        // ------------------------------------------------------- CHAMPIONSHIP
        // MEDALS (2026-08-02, owen). Winning a whole league campaign and being
        // told nothing is a wasted moment, so it gets its own gold ribbon under
        // the money block - the last thing read before the buttons.
        //
        // COLOUR LAW: amber (1, 0.82, 0.25) is this project's WARNING (over
        // weight cap, SHORT inventory) and red is live damage, so gold has to
        // hold itself apart from amber. Three things do that: it is paler and
        // warmer (1, 0.87, 0.46), it sits inside a ribbon rather than being
        // loose text, and it can only ever appear on a VICTORY screen, whose
        // accent is green - the amber accent belongs to a DRAW, which by
        // definition wins no contest and therefore no medal.
        //
        // Y is clamped above the button row instead of being a bare fraction:
        // the money block is the only thing between it and the buttons, and at
        // a short window height a literal offset would have drawn the
        // championship underneath BACK TO WORKSHOP.
        // The button row moves DOWN when a championship is on screen. First
        // pass clamped the ribbon above a fixed button row instead and the two
        // ended up 1px apart with the ribbon jammed under NET - photographed,
        // and it read as an overlap bug rather than an award. There is a large
        // dead band below the buttons; spend it.
        float medalDrop = cMedal.Length > 0 ? 52f : 0f;
        if (cMedal.Length > 0)
        {
            float ry = my + 72f;
            GUI.color = new Color(1f, 0.72f, 0.18f, 0.26f);
            GUI.DrawTexture(new Rect(W * 0.5f - 330f, ry - 4f, 660f, 40f), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 0.80f, 0.32f, 0.85f);
            GUI.DrawTexture(new Rect(W * 0.5f - 330f, ry - 4f, 660f, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(W * 0.5f - 330f, ry + 34f, 660f, 2f), Texture2D.whiteTexture);
            GUI.color = old;
            medalStyle.normal.textColor = new Color(1f, 0.87f, 0.46f);
            GUI.Label(new Rect(0, ry, W, 32), cMedal, medalStyle);
        }

        // -------------------------------------------------------- BUTTONS
        // (2d) The (R)/(B) hints were shown on the touch build, where there is
        // no keyboard. They are drawn only when the desktop keys are the real
        // input path. BACK is first and coloured: after a contest the play is
        // to go spend the purse or rebuild, never to rematch.
        bool touch = MobileBuilderUI.Active;
        float bw = 264f, bh = 54f, by = H * 0.705f + medalDrop;
        float cx = W * 0.5f;
        GUI.backgroundColor = new Color(0.30f, 0.62f, 0.88f);
        bool back = GUI.Button(new Rect(cx - bw - 10f, by, bw, bh),
                               touch ? "BACK TO WORKSHOP" : "BACK TO WORKSHOP  (B)", btnStyle);
        GUI.backgroundColor = Color.white;
        // REMATCH re-runs the same builds as an EXHIBITION: ResetFight() goes
        // through StartFight(), not StartCareerFight(), and SettleFight() has
        // already cleared the contest - so it charges no entry fee, pays
        // sandbox scrap, and cannot re-win the purse. Label it as what it is.
        bool again = GUI.Button(new Rect(cx + 10f, by, bw, bh),
                                cIsContest ? (touch ? "REMATCH · EXHIBITION" : "REMATCH · EXHIBITION  (R)")
                                           : (touch ? "REMATCH" : "REMATCH  (R)"), btnStyle);
        if (back) { if (bm != null) bm.BackToBuild(); return; }
        if (again && bm != null) bm.ResetFight();
    }

    string SideLine(string label, Side s)
    {
        // R5 (critic finding 8): "dealt 0" sat directly above the opponent's
        // "taken 170" and the two lines flatly contradicted each other. `dealt`
        // has always counted WEAPON damage only; `taken` includes hazards and
        // impacts. Say which is which instead of making the player guess.
        return string.Format("{0} — weapon dmg {1:F0} ({2:F1}/s) · total taken {3:F0} · HP {4:F0}% · parts {5}/{6} · wheels {7}/{8}",
            label, s.dealt, s.dealt / Mathf.Max(1f, elapsed), s.taken,
            s.hpFrac * 100f, s.bodyNow, s.startBody, s.wheelsNow, s.startWheels);
    }

    string SideDetail(Side s)
    {
        float span = Mathf.Max(1f, elapsed);
        // ⚠ THE BATTERY LINE READS THE BELL, NOT THE WRECKAGE — 2026-08-10, and
        // this is the 07-29 gyro fix finally applied to its second path.
        //
        // It used to be `GetComponent<PowerPlant>()` right here, and three
        // different states collapsed into two lies:
        //   · a build that NEVER fitted a battery has a PowerPlant anyway (it is
        //     added unconditionally to the robot root, capacity 0), so `pp` was
        //     non-null and `Flat` was true — it reported "FLAT", as though a
        //     pack had run down;
        //   · a build whose battery was SHEARED OFF has capacityKJ recomputed to
        //     0 every step ("shearing a battery off must take its energy with
        //     it"), so it ALSO reported "FLAT" — the strategy that killed the
        //     pack left no trace on the results screen;
        //   · "none fitted" only ever appeared when the whole BOT was gone,
        //     which is the one case where it says nothing useful.
        //
        // The datum needed to tell them apart was already here and already
        // sampled AT THE BELL — startCapKJ, whose own doc says it exists so that
        // "how much of my pack is still bolted on" is answerable after a battery
        // shears off. The LIVE HUD twenty lines up has used it correctly the
        // whole time ("no power part fitted" / "(pack damaged)"); only this
        // results line was left on the live component. Two renderings of one
        // fact, and the one nobody re-read went wrong — the same shape as the
        // gyro line directly below, which was fixed on 07-29 and is the
        // precedent this now matches.
        //
        // Cached Side fields rather than a fresh GetComponent, deliberately:
        // they are updated every step (:488-491), zeroed on death (:447, :463),
        // and the field comment says they are cached precisely so the numbers
        // SURVIVE THE BODY'S DESTRUCTION — which is the situation this line is
        // rendered in.
        string pwr = s.startCapKJ <= 0.01f ? "none fitted"
                   : s.capKJ      <= 0.01f ? "destroyed"
                   : s.pwrFlat             ? "FLAT"
                   : string.Format("{0:F0}%{1}{2}", s.pwrFrac * 100f,
                                   s.capKJ < s.startCapKJ - 1f ? " (pack damaged)" : "",
                                   s.pwrStrained ? " (straining)" : "");
        return string.Format("      seams sheared {0} · flipped {1:F0}% of the match · immobile {2:F0}% · gyros {3} · battery {4}",
            s.brokenSeams, 100f * s.flippedTime / span, 100f * s.immobileTime / span,
            s.startGyros == 0 ? "none fitted"
                : ((s.gyro == null ? 0 : s.gyro.LiveGyros()) + " of " + s.startGyros + " live"), pwr);
    }

    void BigLine(string text, Color c, float yFrac)
    {
        // ROUND-3 FIX (critic CRITICAL 2a): medStyle is shared with the
        // results cause line. This used to leave whatever colour the last
        // toast used sitting in the style, which is how "KO - enemy core
        // destroyed" came out amber on a VICTORY screen.
        Color keep = medStyle.normal.textColor;
        float y = Screen.height / UIS * yFrac;
        medStyle.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
        GUI.Label(new Rect(3f, y + 3f, Screen.width / UIS, 60f), text, medStyle);
        medStyle.normal.textColor = c;
        GUI.Label(new Rect(0f, y, Screen.width / UIS, 60f), text, medStyle);
        medStyle.normal.textColor = keep;
    }

    void EnsureStyles()
    {
        if (hudStyle != null) return;
        hudStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        nameStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };   // R5 finding 2
        bigStyle = new GUIStyle(GUI.skin.label) { fontSize = 64, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        medStyle = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true };
        smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleCenter };   // R5 finding 2: the two lines that answer "why did I lose" were the smallest text on the results screen
        btnStyle = new GUIStyle(GUI.skin.button) { fontSize = 17, fontStyle = FontStyle.Bold };
        tinyStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };   // R5 finding 2
        // Round-3: the identity and money readouts get their OWN styles, so
        // nothing they draw shares a mutable style with the fight toasts.
        idStyle = new GUIStyle(GUI.skin.label) { fontSize = 21, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        hudIdStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };   // R5 finding 2
        hudIdStyle.normal.textColor = new Color(1f, 0.82f, 0.32f);
        moneyStyle = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        moneySmall = new GUIStyle(GUI.skin.label) { fontSize = 19, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        // Sits between moneySmall (19) and moneyStyle (28) on the existing type
        // scale - a championship outranks the purse breakdown and is outranked
        // by the net, which is still the number the player acts on.
        medalStyle = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
    }

    /// <summary>ROUND-3 FIX (critic MAJOR 4): which fight this is, for the
    /// fight HUD. Null for exhibitions, test drives and ladder rungs, where
    /// there is no contest to name and the header stays as it was.</summary>
    string ContestHudLine()
    {
        if (!Career.active || string.IsNullOrEmpty(Career.activeContest)) return null;
        var lg = Career.FindLeague(Career.activeLeague);
        var c = Career.FindContest(lg, Career.activeContest);
        if (lg == null || c == null) return null;
        bool re = Career.Data.doneContests.Contains(c.id);
        // First-win rule (owen, 2026-08-13): re-entry is practice, and the
        // HUD must not promise a purse the settlement will not pay. Entry
        // fees no longer exist (owen, same day), so the line says nothing
        // about them.
        if (re) return lg.name.ToUpper() + "   ·   " + lg.arenaName.ToUpper()
                     + "   ·   PRACTICE — PURSE ALREADY WON";
        return lg.name.ToUpper() + "   ·   " + lg.arenaName.ToUpper()
             + "   ·   PURSE " + c.purse;
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