using UnityEngine;

namespace RobotBrawl.Phase0
{
/// <summary>
/// Phase 2B — the AI opponent's driver (design doc §7.1, §8).
///
/// AI parity: the Mauler is a normal CompoundRobot spawned through the same
/// path as the player's build — same physics, same damage rules, it tips and
/// sheds parts. This component ONLY writes drive inputs (aiThrottle/aiSteer on
/// its RaycastWheelDrive), exactly the two axes the player has. The single
/// exception is the round-1 self-right assist: a clamped internal righting
/// torque that applies ONLY while the hull is flipped/beached AND resting on
/// the ground — the wheel wiggle alone is physically inert once the
/// suspension rays point at the sky.
///
/// Layered behavior, evaluated at ~5 Hz (DECISION_INTERVAL is the reaction-
/// time cap — outputs hold between decisions):
///   1. Self-preservation: flipped/beached → wiggle + grounded righting torque
///      (long-axis roll, direction alternating every 2.5 s when blocked).
///   2. Unstick: full throttle but not moving ~1.2 s → reverse-and-wiggle 1 s.
///   3. Back-off: after a hard hit — and 1.5× longer when the hit was TAKEN
///      (a spinner bite), so it visibly disengages after eating one.
///   4. Threat-arc combat (round-2 critic fix 2): reads the enemy's live
///      spinner mount directions; inside the ~60° spinner arc it steers to a
///      flank point instead of feeding itself face-first; against a player
///      parked >5 s it orbits to the REAR before charging. First 3 s after
///      the bell it approaches at reduced throttle (round-2 fix 1 — no more
///      deterministic full-speed opening mega-clash).
///   5. Desperation (round-2 fix 6): under 30 s left and >20 dmg behind on
///      the judges' cards, it abandons caution/back-off and charges flat out —
///      losing by KO beats winning by boredom.
/// </summary>
public class AIController : MonoBehaviour
{
    public static float DECISION_INTERVAL = 0.2f;  // s — the reaction-time cap
    // ---- §8 per-INSTANCE difficulty. Statics above are the defaults; a tier
    // overrides these copies only, so two AIs could differ in the same match
    // and nothing leaks between fights.
    public float rtDecision = 0.2f;
    public float rtSteerAggr = 0.10f;
    public float rtOpeningThrottle = 0.55f;
    public float rtAggression = 1f;
    public PowerPlant power;

    /// <summary>Difficulty is reaction time and commitment. It never touches
    /// mass, damage, HP or joint strength (§8, §3.1).</summary>
    /// <summary>ROUND-2-DEV FIX 1. Fraction of the weapon-avoidance layer this
    /// bot runs. See EnemyRoster.WeaponRespect for the measurement.</summary>
    public float rtAvoid = 1f;
    /// <summary>C3A: hazard repulsion gates on tier - Rookies blunder in.</summary>
    public AiTier tier = AiTier.Rookie;
    /// <summary>TEST HOOK ONLY (RB2Dev). &lt; 0 = use the tier's value, which is
    /// what the game always does. Set >= 0 to force one value for both arms of
    /// a controlled sweep so the shipped behaviour and the new one can be
    /// INTERLEAVED in a single batch. Never written by game code.</summary>
    public static float avoidOverride = -1f;

    public void ApplyTier(AiTier t)
    {
        rtDecision = EnemyRoster.DecisionInterval(t);
        rtSteerAggr = EnemyRoster.SteerAggr(t);
        rtOpeningThrottle = EnemyRoster.OpeningThrottle(t);
        rtAggression = EnemyRoster.Aggression(t);
        rtAvoid = EnemyRoster.WeaponRespect(t);
        tier = t;
    }
    public static float BACKOFF_TIME = 1.5f;       // s of disengage after a hard hit
    public static float BACKOFF_DMG = 8f;          // dmg (dealt+taken) that counts as "hard"
    /// <summary>Steer governor strength: steer ×= 1/(1+k·v²). 0.10 at 5 m/s
    /// cuts lock to ~29% ON TOP of the drive's own scale → ~0.25 g lateral.</summary>
    public static float STEER_AGGR = 0.10f;
    public static float STUCK_TIME = 1.2f;         // s of blocked throttle before unstick
    public static float UNSTICK_TIME = 1.0f;       // s of reverse-and-wiggle
    /// <summary>Round-3 fix (critic finding 7): unstick was an ABSORBING state —
    /// reverse 1 s, drive straight back into the same wall/shard, repeat, for
    /// 72% of one measured match. Three unstick attempts inside this window mean
    /// the short reverse is not working, so escalate.</summary>
    public static float UNSTICK_WINDOW = 4f;
    public static int UNSTICK_STREAK = 3;
    /// <summary>The escalation: a long full-lock reverse that CHANGES HEADING,
    /// so the next approach comes in on a different line instead of re-wedging.
    /// Capped by EXTRICATE_CLEAR, which is the real exit condition.</summary>
    public static float EXTRICATE_TIME = 2.5f;
    /// <summary>Round-4 fix 6 (critic finding 6: "the AI spends 65-82% of any
    /// match in unstick/extricate"). Two causes, both here. First, extricate ran
    /// its clock out even after it had already got free, so a bot that reversed
    /// clear in 0.4 s still spent 2.5 s driving backwards. Metres of travel from
    /// the entry point that count as "out of the pocket" — the goal is to have
    /// MOVED, not to have reversed for a while.</summary>
    public static float EXTRICATE_CLEAR = 1.0f;
    /// <summary>Second cause: the escalation re-armed the instant it finished,
    /// so the bot cycled unstick → unstick → unstick → extricate → repeat for
    /// the whole match. After an extricate the bot must fight normally for this
    /// long before the stuck detector may fire again. Grinding forwards is at
    /// least aimed at the opponent; reverse-wiggling is not.</summary>
    public static float UNSTICK_COOLDOWN = 5f;
    /// <summary>Self-right torque = mass·g·this (meters of moment arm). 0.45 m
    /// beats the worst-case gravity moment (~0.30 m arm) with margin but stays
    /// in "powerful actuator", not "magic": ~2 kN·m on the 460 kg Mauler.</summary>
    public static float RIGHT_ARM = 0.45f;
    // ---- round-2 combat shape ----
    public static float OPENING_TIME = 3f;         // s of cautious approach after the bell
    public static float OPENING_THROTTLE = 0.55f;
    public static float THREAT_ARC_DOT = 0.15f;    // inside ~81° of a spinner face = flank
    public static float FLANK_OFFSET = 2.6f;       // m sideways aim offset when flanking
    /// <summary>Attack aim sits this far BEHIND the spinner plane (not target
    /// center) — an AFK-run showed center-aimed charges from 60° off-axis
    /// still sweep through the disc's front corner.</summary>
    public static float REAR_BIAS = 0.9f;
    public static float PARKED_TIME = 5f;          // s of target immobility → rear attack
    public static float DESPERATION_WINDOW = 30f;  // s left on the clock
    public static float DESPERATION_DEFICIT = 20f; // dmg behind on the cards

    // ---- Phase 4 weapon input (ROUND-1 IMPL FIX, critic CRITICAL 5) --------
    // "The AI still cannot fire any Phase 4 weapon." Actuator.aiFire was
    // declared, read by Actuator.Fire(), and written by NOTHING - grep across
    // both script folders returned the declaration, the read, and a comment
    // claiming this class wrote it. So the entire base-component kit was
    // single-player-only: an actuated opponent would have carried a hammer it
    // could never swing. This is the missing half of the player's own
    // Phase0Input.FireHeld(), and it is still only an INPUT - no extra reach,
    // damage or energy (section 8: difficulty is reaction time, never stats).
    // Note the gate is re-evaluated at rtDecision, so a Rookie is genuinely
    // slower on the trigger than a Champion for free.

    /// <summary>Distance, m, centre to centre, at which a Pivot or Ram starts
    /// its cycle. NOT contact range on purpose. A pivot needs ~0.5 s of wind-up
    /// before Actuator.BITE_RATE_FRAC (0.45) will let a contact count at all,
    /// and these bots close at 3-5 m/s, so firing at contact range would land
    /// every AI swing below its own bite gate - the exact failure BITE_RATE_FRAC
    /// documents on the player's side. 3.0 m is ~0.6-1.0 s of closing, i.e. the
    /// swing arrives at speed.</summary>
    public static float FIRE_RANGE = 3.0f;
    /// <summary>cos of the half-arc the target must be inside to fire. 0.35 is
    /// ~70 deg, matched to the 150 deg sweep a pivot actually covers, so the AI
    /// does not burn its pack swinging at empty floor beside it.</summary>
    public static float FIRE_ARC_DOT = 0.35f;
    /// <summary>A Spindle is HELD, not triggered: it stores energy only while
    /// spinning, so it has to be wound up on the approach. 5.5 m is most of the
    /// arena, but not "always on" - a bot that has been knocked to the far wall
    /// stops paying for spin while it drives back.</summary>
    public static float SPIN_HOLD_RANGE = 5.5f;
    Actuator[] acts;

    public CompoundRobot self;
    public CompoundRobot target;
    public RaycastWheelDrive drive;
    /// <summary>Set by StartFight so desperation can read the clock/cards.</summary>
    public FightManager fm;
    /// <summary>Body-space drive direction (the Mauler's spike side = +Z).</summary>
    public Vector3 forwardLocal = Vector3.forward;
    /// <summary>Debug/verification: the layer that produced the current output.</summary>
    public string state = "engage";

    /// <summary>ROUND-2-CRITIC FIX (MAJOR 5: "the enemy AI does not engage a
    /// stationary target: 4 of 6 parked matches ended with the player dealing
    /// exactly 0.0 damage over a full 90 s").
    ///
    /// ROOT CAUSE, from the source rather than from the symptom. The "circle"
    /// branch below aims at tpos + fromT*2.2 + orbit*1.6 - a point roughly
    /// 2.7 m from the target, on the far side of the AI from it. That is a
    /// STANDOFF aim, and it is entered whenever the target has been still for
    /// PARKED_TIME and the AI is not already behind it. Against a target that
    /// never moves, `parked` never clears, so the exit condition for the orbit
    /// is a state the opponent controls and an AFK opponent never grants. The
    /// AI orbits a parked robot for ninety seconds and never closes. That is
    /// also what produced the prior critic session's "parking is close to a
    /// free win": the parked bot was not winning, it was being ignored.
    ///
    /// FIX: the orbit is now a manoeuvre with a deadline, not a state. It gets
    /// CIRCLE_MAX_S to reach the rear; then the AI COMMITS - drives the attack
    /// line for COMMIT_S regardless of where the threat arc is pointing - and
    /// then may orbit again. 2.5 s is roughly one lap of the 2.7 m standoff
    /// circle at the 3-5 m/s these bots close at, i.e. long enough for the
    /// manoeuvre to be worth something; 3.0 s at those speeds is 9-15 m of
    /// travel, comfortably more than the arena's width, so a commit that starts
    /// at standoff range always reaches contact.</summary>
    public static float CIRCLE_MAX_S = 2.5f;
    public static float COMMIT_S = 3f;
    float circleSince = -1f;
    float commitUntil;

    float nextDecision;
    float backoffUntil;
    float pinTimer;
    /// <summary>Fix 2026-07-29 (playtest): mutual standstill in contact range
    /// for this long is a wall-pin grind, not a fight — break off like a hard
    /// hit does. Three playtest matches read 79-85% player immobility while
    /// the AI ground the player against a wall.</summary>
    public static float PIN_TIME = 2.5f;
    float lastDealt, lastTaken;
    float stuckTimer;
    float unstickUntil;
    float unstickThrottle = -1f;
    float lastUnstickAt = -99f;
    int unstickStreak;
    float extricateUntil;
    float extricateSteer = 1f;
    Vector3 extricateFrom;
    float unstickBlockedUntil;
    bool assistNow;
    float assistSince = -1f;   // continuous-assist start (direction epochs)
    int assistEpoch = -1;
    float assistDir = 1f;
    float activeSince = -1f;   // when the bell handed us control
    float targetStillSince;
    Vector3 lastTargetPos;

    void OnEnable()
    {
        // FightManager's bell re-enables us — that's the opening-caution clock.
        activeSince = Time.time;
        targetStillSince = Time.time;
    }

    void FixedUpdate()
    {
        if (self == null || drive == null) return;
        if (Time.time >= nextDecision)
        {
            nextDecision = Time.time + rtDecision;
            Decide();
        }
        // Phase 3: the righting torque MOVED OUT of the AI. Self-righting is
        // hardware now (GyroStabilizer, §7.1) and the Mauler carries a gyro
        // like any player build — so it rights itself with exactly the same
        // component, at exactly the same strength, and loses the ability when
        // the gyro shears off. This is the §8 "same physics inputs" promise.
    }

    /// <summary>Write Actuator.aiFire on every actuator this bot carries.
    /// Called FIRST in Decide so it is set on every path, including the early
    /// returns for dead/flipped/unstick - a flipped bot flailing a live hammer
    /// would be a way to deal damage while incapacitated.</summary>
    void UpdateFire()
    {
        if (acts == null) acts = self.GetComponentsInChildren<Actuator>(true);
        if (acts.Length == 0) return;

        bool live = !self.dead && target != null && !target.dead
                    && Vector3.Dot(transform.up, Vector3.up) > 0.2f;
        // Never fire on an empty pack. Actuator would draw against supplyFrac 0
        // and get nowhere, but this also keeps the AI from holding a spindle
        // trigger down while its drive is dead, which reads as broken.
        if (power != null && power.Flat) live = false;

        float dist = 999f, dot = -1f;
        if (live)
        {
            Vector3 to = target.rb.worldCenterOfMass - self.rb.worldCenterOfMass;
            to.y = 0f;
            dist = to.magnitude;
            Vector3 f = transform.TransformDirection(forwardLocal);
            f.y = 0f;
            if (to.sqrMagnitude > 1e-4f && f.sqrMagnitude > 1e-4f)
                dot = Vector3.Dot(f.normalized, to.normalized);
        }

        for (int i = 0; i < acts.Length; i++)
        {
            if (acts[i] == null) continue;
            acts[i].aiFire = live && (acts[i].kind == ActuatorKind.Spindle
                ? dist < SPIN_HOLD_RANGE
                : (dist < FIRE_RANGE && dot > FIRE_ARC_DOT));
        }
    }

    void Decide()
    {
        UpdateFire();
        if (self.dead) { state = "dead"; assistNow = false; drive.aiThrottle = 0f; drive.aiSteer = 0f; return; }

        // Hard-hit detection: a jump in dealt+taken arms the back-off layer;
        // damage TAKEN (a spinner bite) backs off 1.5× longer — round-2 fix 2:
        // eating a bite must visibly break the engagement, every time.
        float dealt = self.damageDealt, taken = self.damageTaken;
        float dDealt = dealt - lastDealt, dTaken = taken - lastTaken;
        if (dDealt + dTaken >= BACKOFF_DMG && Time.time > backoffUntil)
            backoffUntil = Time.time + BACKOFF_TIME * (dTaken >= BACKOFF_DMG ? 1.5f : 1f);
        lastDealt = dealt; lastTaken = taken;

        // Fix 2026-07-29 (playtest): wall-pin relief. If BOTH bots have been
        // near-stationary in contact range for PIN_TIME, break the engagement
        // exactly like a hard hit does.
        if (target != null && !target.dead && self.rb != null && target.rb != null)
        {
            float pSpd = VelUtil.GetLinearVelocity(self.rb).magnitude;
            float tSpd = VelUtil.GetLinearVelocity(target.rb).magnitude;
            float tDist = Vector3.Distance(self.rb.position, target.rb.position);
            if (pSpd < 0.6f && tSpd < 0.4f && tDist < 1.6f) pinTimer += rtDecision;
            else pinTimer = 0f;
            if (pinTimer >= PIN_TIME && Time.time > backoffUntil)
            { backoffUntil = Time.time + BACKOFF_TIME; pinTimer = 0f; }
        }

        // --- layer 1: self-preservation ---
        // Covers fully-flipped hulls AND the beached case (tilted ~50° against
        // the enemy or debris, wheels off the ground). Tilted-and-immobile
        // gets the clamped grounded roll torque; cornering tilt never triggers.
        float upY = Vector3.Dot(transform.up, Vector3.up);
        float spdNow = VelUtil.GetLinearVelocity(self.rb).magnitude;
        bool flippedNow = upY < 0.2f;
        assistNow = flippedNow || (upY < 0.75f && spdNow < 0.35f);
        if (!assistNow) { assistSince = -1f; assistEpoch = -1; }
        else if (assistSince < 0f) assistSince = Time.time;
        if (flippedNow)
        {
            state = "flipped";
            drive.aiThrottle = Mathf.Sin(Time.time * 6f) > 0f ? 1f : -1f;
            drive.aiSteer = Mathf.Sin(Time.time * 3f) > 0f ? 1f : -1f;
            return;
        }

        if (target == null || target.dead)
        {
            state = "idle";
            drive.aiThrottle = 0f;
            drive.aiSteer = 0f;
            return;
        }

        // --- C3A layer 1.5: hazard repulsion (Veteran and up). A live or
        // telegraphing hazard nearby overrides pursuit for this decision
        // tick; Rookies blunder straight in - honest tier content.
        if (tier != AiTier.Rookie)
        {
            Vector3 hAway;
            if (ArenaHazards.Near(self.rb.position, 2.2f, out hAway))
            {
                state = "hazard-avoid";
                Vector3 hzFwd = transform.TransformDirection(forwardLocal);
                float hSide = Vector3.Dot(transform.right, hAway);
                float hFwd = Vector3.Dot(hzFwd, hAway);
                drive.aiThrottle = hFwd >= -0.3f ? 0.8f : -0.6f;
                drive.aiSteer = Mathf.Clamp(hSide * 2f, -1f, 1f) * Mathf.Sign(drive.aiThrottle);
                return;
            }
        }

        // --- layer 2: unstick (wall-pinned / high-centered while trying) ---
        float spd = spdNow;
        if (Mathf.Abs(drive.aiThrottle) > 0.5f && spd < 0.3f) stuckTimer += rtDecision;
        else stuckTimer = 0f;
        if (stuckTimer >= STUCK_TIME && Time.time >= unstickUntil && Time.time >= extricateUntil
            && Time.time >= unstickBlockedUntil)
        {
            unstickStreak = (Time.time - lastUnstickAt < UNSTICK_WINDOW) ? unstickStreak + 1 : 1;
            lastUnstickAt = Time.time;
            if (unstickStreak >= UNSTICK_STREAK)
            {
                // The short reverse-and-wiggle has failed three times running:
                // back out hard on full lock, alternating side each time, so the
                // bot leaves the pocket on a genuinely different heading.
                unstickStreak = 0;
                extricateUntil = Time.time + EXTRICATE_TIME;
                extricateSteer = -extricateSteer;
                extricateFrom = self.rb.worldCenterOfMass;
                // Fix 6: whichever way this ends (cleared or timed out), the
                // stuck detector is locked out afterwards.
                unstickBlockedUntil = extricateUntil + UNSTICK_COOLDOWN;
            }
            else
            {
                unstickUntil = Time.time + UNSTICK_TIME;
                unstickThrottle = drive.aiThrottle >= 0f ? -1f : 1f;
            }
            stuckTimer = 0f;
        }
        if (Time.time < extricateUntil)
        {
            // Fix 6: exit the moment we have actually moved, not when the
            // timer says so.
            if ((self.rb.worldCenterOfMass - extricateFrom).sqrMagnitude
                > EXTRICATE_CLEAR * EXTRICATE_CLEAR)
            {
                extricateUntil = 0f;
                unstickBlockedUntil = Time.time + UNSTICK_COOLDOWN;
                stuckTimer = 0f;
            }
            else
            {
                state = "extricate";
                drive.aiThrottle = -1f;
                drive.aiSteer = extricateSteer;
                return;
            }
        }
        if (Time.time < unstickUntil)
        {
            state = "unstick";
            drive.aiThrottle = unstickThrottle;
            drive.aiSteer = Mathf.Sin(Time.time * 5f) > 0f ? 1f : -1f;
            return;
        }

        Vector3 selfPos = self.rb.worldCenterOfMass;
        Vector3 tpos = target.rb.worldCenterOfMass;

        // Desperation (round-2 fix 6): clock low + decisively behind → all-in.
        bool desperate = fm != null && fm.state == FightManager.State.Fighting
                      && fm.timer < DESPERATION_WINDOW
                      && target.damageDealt - self.damageDealt > DESPERATION_DEFICIT;

        // --- C6.2 layer 2.7: WARY (Veteran and up) - the authored counter-play
        // for the lesson bots, living in the AI where it belongs (owner call).
        // (a) A target that is TILTING nearby is about to fall on someone:
        // give it room until it rights itself or finishes falling - a downed
        // hull is a target, a FALLING one is a hazard (the tipper's lesson).
        // (b) A spun-up disc on a charged pack is unapproachable: hold range
        // and make the pack pay for the spin, then move in (the widowmaker's
        // lesson). Rookies blunder straight in - honest tier content.
        if (!desperate && tier != AiTier.Rookie && target != null && !target.dead)
        {
            Vector3 wTo = tpos - selfPos; wTo.y = 0f;
            float wDist = wTo.magnitude;
            float tUp = Vector3.Dot(target.transform.up, Vector3.up);
            // TALL targets only (CoM still high while tilting): a bot lifted
            // on your own wedge also tilts, and backing off at the moment of
            // advantage cost floor L4 a third of its wins in the C6 matrix.
            bool towerFalling = tUp < 0.75f && tUp > 0.35f && wDist < 3.5f
                             && target.rb.worldCenterOfMass.y > 0.85f;
            // ROUND-2 (C6.2): the wary-disc branch is GONE - a front-mounted
            // disc is already handled by the threat-arc/flank layer below, and
            // holding the player in FRONT at range measured strictly worse
            // (Ptaken 280-566 -> 287-772). Wariness of discs = don't approach
            // into the arc, which is the flank layer's whole job.
            if (towerFalling)
            {
                state = "wary-topple";
                Vector3 wAway = wDist > 0.01f ? -wTo / wDist : transform.forward;
                Vector3 wFw = transform.TransformDirection(forwardLocal); wFw.y = 0f;
                float wF = wFw.sqrMagnitude > 1e-4f ? Vector3.Dot(wFw.normalized, wAway) : 1f;
                float wS = Vector3.Dot(transform.right, wAway);
                drive.aiThrottle = wF >= -0.2f ? 1f : -1f;
                drive.aiSteer = Mathf.Clamp(wS * 2f, -1f, 1f) * Mathf.Sign(drive.aiThrottle);
                return;
            }
        }

        // --- layer 3: back-off after a hard exchange (skipped when desperate) ---
        float gov = 1f / (1f + rtSteerAggr * spd * spd);
        if (!desperate && Time.time < backoffUntil)
        {
            Vector3 toT = tpos - selfPos; toT.y = 0f;
            Vector3 f0 = transform.TransformDirection(forwardLocal); f0.y = 0f;
            if (toT.sqrMagnitude > 0.01f && f0.sqrMagnitude > 0.01f)
            {
                float angT = Vector3.SignedAngle(f0.normalized, toT.normalized, Vector3.up);
                state = "backoff";
                drive.aiThrottle = -1f;
                drive.aiSteer = (angT > 0f ? -0.6f : 0.6f) * gov;
                return;
            }
        }

        // --- layer 4: threat-arc target selection (round-2 fix 2) ---
        // Track how long the target has been parked.
        if ((tpos - lastTargetPos).magnitude > 0.25f)
        {
            targetStillSince = Time.time;
            lastTargetPos = tpos;
        }
        bool parked = Time.time - targetStillSince > PARKED_TIME;
        // Round-2-critic MAJOR 5: a target that starts moving again cancels the
        // orbit outright - the deadline only exists for the case the AI cannot
        // otherwise get out of.
        if (!parked) circleSince = -1f;

        // The enemy's live business end, summed into one horizontal threat dir.
        // Round-5 fix 5 (critic finding 5): this read SpinnerWeapon components
        // only, so a ram-spike or unarmed opponent switched the whole
        // flank/orbit layer OFF — measured 0 flank and 0 circle decisions in
        // four matches against spike and no-weapon builds, i.e. bringing a
        // spike bought you a strictly stupider AI. Every hardened edge is a
        // threat now, weighted by how hard it hits; a build with no weapon at
        // all still has a front, so fall back to the way it drives.
        Vector3 threatDir = Vector3.zero;
        for (int i = 0; i < target.parts.Count; i++)
        {
            var tp = target.parts[i];
            if (tp.detached || tp.go == null) continue;
            if (tp.spec.edgeHardness <= 1.01f) continue;   // structure is not a weapon
            Vector3 d = tp.go.transform.position - tpos; d.y = 0f;
            if (d.sqrMagnitude > 0.01f) threatDir += d.normalized * tp.spec.edgeHardness;
        }
        if (threatDir.sqrMagnitude > 0.01f) threatDir.Normalize();
        else
        {
            Vector3 f = target.transform.forward; f.y = 0f;
            threatDir = f.sqrMagnitude > 0.01f ? f.normalized : Vector3.zero;
        }

        // ROUND-2-DEV FIX 1: every aim offset below is scaled by `av`. At
        // av = 1 (Champion) this block is arithmetically identical to what
        // shipped; at av = 0 (Rookie) every branch collapses to `aim = tpos`,
        // which is exactly the control arm measured in qa_rb2d_avoid.txt.
        float av = avoidOverride >= 0f ? avoidOverride : rtAvoid;
        Vector3 aim = tpos;
        string mode = desperate ? "desperate" : "charge";
        if (threatDir != Vector3.zero)
        {
            Vector3 fromT = selfPos - tpos; fromT.y = 0f;
            if (fromT.sqrMagnitude > 0.01f)
            {
                fromT.Normalize();
                float inArc = Vector3.Dot(fromT, threatDir);   // 1 = dead ahead of the spinner
                // Round-2-critic MAJOR 5: the orbit has a deadline. See
                // CIRCLE_MAX_S.
                bool wantOrbit = !desperate && parked && inArc > -0.5f && av > 0.25f;
                bool committing = Time.time < commitUntil;
                if (wantOrbit && !committing)
                {
                    if (circleSince < 0f) circleSince = Time.time;
                    if (Time.time - circleSince >= CIRCLE_MAX_S)
                    {
                        circleSince = -1f;
                        commitUntil = Time.time + COMMIT_S;
                        committing = true;
                    }
                }
                if (committing)
                {
                    // Stalemate broken: drive the attack line and keep driving
                    // it. Aimed behind the threat plane, so committing is still
                    // an approach from the least-defended side rather than a
                    // suicide charge into the disc.
                    aim = tpos - threatDir * (REAR_BIAS * av);
                    mode = "commit";
                }
                else if (wantOrbit)
                {
                    // Orbit a parked target toward its rear, sliding AWAY from
                    // the spinner arc at ~2.7 m radius.
                    Vector3 orbit = Vector3.Cross(Vector3.up, fromT);
                    if (Vector3.Dot(orbit, threatDir) > 0f) orbit = -orbit;
                    aim = tpos + fromT * 2.2f + orbit * (1.6f * av);
                    mode = "circle";
                }
                else if (inArc > THREAT_ARC_DOT)
                {
                    // Inside the spinner's face arc: steer to a flank point
                    // first, then turn in (never feed the disc head-on).
                    // Desperation keeps the arc-aware aim — full commitment
                    // means no back-off, NOT charging the disc face-first.
                    Vector3 perp = Vector3.Cross(Vector3.up, threatDir);
                    if (Vector3.Dot(perp, fromT) < 0f) perp = -perp;
                    aim = tpos + perp * (FLANK_OFFSET * av);
                    if (!desperate && av > 0.05f) mode = "flank";
                }
                else
                {
                    // Outside the face arc: attack, but aim a point BEHIND the
                    // spinner plane so the closing line curves around the disc
                    // instead of clipping its front corner.
                    aim = tpos - threatDir * (REAR_BIAS * av);
                }
            }
        }

        Vector3 to = aim - selfPos;
        to.y = 0f;
        Vector3 fwd = transform.TransformDirection(forwardLocal);
        fwd.y = 0f;
        if (to.sqrMagnitude < 0.01f || fwd.sqrMagnitude < 0.01f) return;
        float ang = Vector3.SignedAngle(fwd.normalized, to.normalized, Vector3.up);

        // Opening caution (round-2 fix 1): reduced throttle for the first 3 s
        // after the bell so the opening exchange is a probe, not a mega-clash.
        float thr = (!desperate && activeSince >= 0f && Time.time - activeSince < OPENING_TIME)
                    ? rtOpeningThrottle : 1f;
        thr *= rtAggression;
        // §8 self-preservation: back off when energy is low. Chasing costs the
        // most energy of anything the bot does, so a low tank is a reason to
        // hold position and let the disc meet the player instead. Desperation
        // (behind on the cards, clock running out) overrides it - dying with
        // charge left is worse.
        if (power != null && power.Low && !desperate) thr *= 0.45f;

        if (Mathf.Abs(ang) < 100f)
        {
            state = mode;
            drive.aiThrottle = thr;
            drive.aiSteer = Mathf.Clamp(ang / 40f, -1f, 1f) * gov;
        }
        else
        {
            state = "turn";
            drive.aiThrottle = -0.6f;
            drive.aiSteer = (ang > 0f ? -1f : 1f) * gov;
        }
    }

}

}