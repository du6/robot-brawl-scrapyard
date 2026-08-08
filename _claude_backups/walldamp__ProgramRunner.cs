using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace RobotBrawl.Phase0
{
/// <summary>V2 — the program SEQUENCER (V2 design doc §1.2). Replaces the v1
/// per-tick reactive interpreter outright, per owen's call.
///
/// Two clocks, one contract:
/// - TRIGGER EVALUATION runs at 5 Hz (TICK) on the fixed clock — the same
///   perceive-decide cadence as the roster AI, fairness by construction.
///   Each eval: the topmost hat whose WHEN is true takes charge if it
///   outranks the hat in charge (preemption — the interrupted sequence
///   RESETS, no resume); a FOREVER body additionally releases when its own
///   WHEN goes false; otherwise the running sequence keeps its commitment
///   (run-to-completion, the v1 latch's replacement).
/// - SEQUENCE ADVANCE runs every physics step, so RUN durations and rounds
///   are honest to 0.02 s. Instant steps (SET / TURN TOWARD / STOP / IF /
///   loop markers) chain in one step, bounded by an instant-step budget;
///   Validate's "a loop needs a blocking step" rule is what makes the budget
///   a backstop rather than a semantic.
///
/// Channel rule: values SET by the sequence in charge persist between its
/// steps; ANY handover (preempt / complete / release / control loss) zeroes
/// everything — motors coast, triggers drop. RUN zeroes its own target on
/// completion ("ran for n" means it stops after n).
///
/// Rounds are integrated from the world, not the command: wheel rounds from
/// the body's actual velocity at the wheel's anchor projected on its roll
/// direction (a stalled machine makes no rounds), rotor rounds from the
/// Actuator's live rate. Rounds-mode RUN carries a MAX_DUR stall timeout so
/// a jammed motor can't deadlock a hat.</summary>
public class ProgramRunner : MonoBehaviour
{
    public const float TICK = 0.2f;
    public const float TURN_GAIN = 0.022f;   // per deg of bearing, at gain 100
    const int INSTANT_BUDGET = 64;
    const float FIRE_CYCLE_TIMEOUT = 4f;     // s per requested cycle, fallback

    public CompoundRobot self;
    public RaycastWheelDrive drive;
    public SensorBus bus;
    public RobotProgram program;

    /// <summary>Hat in charge, −1 none — the P3c debug strip reads this.</summary>
    public int lastFiredHat = -1;
    /// <summary>Program counter of the step in charge (−1 none) — the V2.2
    /// canvas step-highlight hook.</summary>
    public int activeStep = -1;

    /// <summary>Determinism instrument: one line per 5 Hz eval when on.</summary>
    public bool traceOn;
    public StringBuilder trace = new StringBuilder();

    float evalAcc, clock;
    int activeHat = -1, pc;
    struct LoopFrame { public int startPc; public int left; public bool forever; }
    readonly List<LoopFrame> loops = new List<LoopFrame>();
    bool stepEntered; float stepT0, roundsAcc; int fireBase;

    // ---- stall watch (F8) and bus hold (R2 major 4) ---------------------
    // ---- brake (F5), behaviour critic R3 --------------------------------
    // Full retreat authority at or inside this clearance; tapering beyond it.
    const float WALL_CLEAR = 3.0f;

    const float SPUN_COOLDOWN = 0.5f;  // s a spinning hat is passed over
    int spunHat = -1; float spunUntil;

    const float BRAKE_TIME  = 1.00f;   // s ceiling on the counter-thrust
    const float BRAKE_POWER = 1.00f;   // full reverse thrust: a brake, not a nudge
    const float BRAKE_STOP  = 0.35f;   // m/s below which coasting is fine
    const float BRAKE_YAW   = 0.60f;   // rad/s (~34 deg/s) below which spin is fine
    float brakeUntil, brakeFwd, brakeSteer;

    const float STALL_GRACE = 1.2f;    // no measurable progress for this long
    const float STALL_POS   = 0.15f;   // metres
    const float STALL_YAW   = 8f;      // degrees
    Vector3 stallPos; float stallYaw, stallMark;

    // A sensor that goes dark must not read as GOOD NEWS. The damage bus dies
    // exactly when you are being taken apart, and CondsTrue treats "cannot
    // read" as "term false" -- so WHEN hp < 50%: RETREAT quietly became WHEN
    // NEVER, and the robot charged instead. Measured R2: shoot the dmgbus off
    // at hp 0.30 and the command flipped from -0.70 (retreat) to +0.40
    // (charge), on a body whose hp had not changed. Hold the last reading
    // each bus channel gave while it was still alive.
    float lastHp = 1f, lastPower = 1f, lastParts = 0f;
    bool everBus;

    void SampleBus()
    {
        if (bus == null || !bus.busValid) return;
        lastHp = bus.hpFrac; lastPower = bus.powerFrac; lastParts = bus.partsLost;
        everBus = true;
    }
    // V2.2 macro state: TurnBy dead-reckoning accumulator, and the MoveRel
    // SET steering mode (persists like any SET until another wheel write,
    // STOP ALL, or a handover clears it).
    float yawAcc, lastYaw;
    int relTarget = -1; float relPct;
    const float FACE_TOL = 8f;              // deg — FaceSide "aligned"
    const float STEER_GAIN = 0.022f;        // per deg, macro steering P-gain
    const float TURNBY_MIN = 0.25f, TURNBY_MAX = 0.7f;

    float[] wheelState = new float[0];
    float turnGain;
    bool[] actFire = new bool[0];
    float[] actSign = new float[0];

    Actuator[] actuators = new Actuator[0];
    readonly List<int> leftCh = new List<int>();
    readonly List<int> rightCh = new List<int>();
    int wheelCount;

    public void Init(CompoundRobot r, RaycastWheelDrive d)
    {
        self = r; drive = d;
        bus = r != null ? r.GetComponent<SensorBus>() : null;
        actuators = r != null ? r.GetComponentsInChildren<Actuator>(true) : new Actuator[0];
        leftCh.Clear(); rightCh.Clear();
        // V2 fix (bench-caught): spawned robots carry NO wheel entries in
        // `parts` (spec ids are core_0/beam_1/… — the wheels live only in
        // the drive). The parts-list loop counted ZERO wheels, sized the
        // channel state empty, and every motor write vanished — the bot
        // crawled on spindle reaction alone. The DRIVE is the single honest
        // source for both the channel count and each channel's side.
        wheelCount = d != null ? d.ChannelCount : 0;
        for (int ch = 0; ch < wheelCount; ch++)
        {
            if (d.WheelSideX(ch) < 0f) leftCh.Add(ch); else rightCh.Add(ch);
        }
        wheelState = new float[Mathf.Max(wheelCount, 0)];
        actFire = new bool[actuators.Length];
        actSign = new float[actuators.Length];
        for (int i = 0; i < actSign.Length; i++) actSign[i] = 1f;
        evalAcc = 0f; clock = 0f;
        activeHat = -1; pc = 0; loops.Clear(); stepEntered = false;
        lastFiredHat = -1; activeStep = -1;
        turnGain = 0f; relTarget = -1; relPct = 0f;
        lastHp = 1f; lastPower = 1f; lastParts = 0f; everBus = false;
        spunHat = -1; spunUntil = 0f; yawAcc = 0f;
        brakeUntil = 0f; brakeFwd = 0f; brakeSteer = 0f; heldDrive = false;
    }

    bool heldDrive;   // true while WE own drive.directWheelCmd

    void FixedUpdate()
    {
        if (self == null || drive == null || program == null) return;
        if (self.dead || self.controlSource != ControlSource.Program)
        {
            if (activeHat >= 0) ReleaseHat();
            lastFiredHat = -1;
            // ReleaseHat zeroes our own channels, but this early return used
            // to skip ApplyChannels, so the zero never reached the drive and
            // directWheelCmd stayed latched true: a robot that died mid-step
            // kept driving on its last command forever. Push the zero through
            // exactly once, then hand the wheels back.
            if (heldDrive)
            {
                ZeroChannels();
                ApplyChannels();
                drive.directWheelCmd = false;
                heldDrive = false;
            }
            return;
        }
        heldDrive = true;
        SampleBus();
        float dt = Time.fixedDeltaTime;
        clock += dt;
        evalAcc += dt;
        if (evalAcc + 1e-6f >= TICK)
        {
            evalAcc -= TICK;
            TriggerEval();
            if (traceOn) Trace();
        }
        if (activeHat >= 0) Advance(dt);
        ApplyChannels();
    }

    // ---- trigger layer (5 Hz) ---------------------------------------------

    void TriggerEval()
    {
        int top;
        top = TopHat();

        if (activeHat < 0)
        {
            if (top >= 0) StartHat(top);
            return;
        }
        // preemption: strictly higher hats only
        if (top >= 0 && top < activeHat) { StartHat(top); return; }
        // FOREVER release: inside a forever loop AND own WHEN false
        if (InForever() && !CondsTrue(program.hats[activeHat].when))
        {
            ReleaseHat(top < 0);
            if (top >= 0) StartHat(top);
        }
    }

    /// <summary>Pure loop/branch scaffolding: executing it produces no
    /// command. A hat that can only ever reach these is spinning.</summary>
    static bool IsStructural(POp o)
    {
        return o == POp.If || o == POp.Else || o == POp.EndIf
            || o == POp.Repeat || o == POp.Forever || o == POp.End;
    }

    /// <summary>Topmost hat whose WHEN is true, skipping any hat we caught
    /// SPINNING in the last SPUN_COOLDOWN seconds. Without the skip the 5 Hz
    /// trigger handed control straight back to the spinner every 0.2 s, so the
    /// safety hat underneath held the wheels 81% of the time yet never kept a
    /// command long enough to move the robot -- it drifted 0.75 m the WRONG
    /// way. The cooldown is short on purpose: a hat that spins only while some
    /// condition is false must recover the moment that condition flips.</summary>
    int TopHat()
    {
        for (int i = 0; i < program.hats.Count; i++)
        {
            if (i == spunHat && clock < spunUntil) continue;
            if (CondsTrue(program.hats[i].when)) return i;
        }
        return -1;
    }

    bool InForever()
    {
        for (int i = 0; i < loops.Count; i++) if (loops[i].forever) return true;
        return false;
    }

    /// <summary>Give up whatever the active hat was doing. R3 critic caught
    /// the R2 weapon latch being too broad: a RUN WEAPON FOR 5 s that got
    /// PREEMPTED never stopped -- measured still firing 2.8 s past its own
    /// duration and on past the end of the fight -- because ExitStep only runs
    /// when a step finishes on its own, and StartHat no longer cleaned up
    /// after it. An in-flight blocking step OWNS its channels and must release
    /// them however it ends. `disarm` additionally drops the WEAPON ON latch:
    /// a handover keeps it (a program that never says WEAPON OFF should keep
    /// spinning), but going idle with no hat true at all is the program saying
    /// nothing, and a robot parked with its blade at full for the rest of the
    /// fight -- 0.005 PowerFrac/s, 30-45% of the reserve -- is not what
    /// anybody wrote.</summary>
    void AbandonHat(bool disarm)
    {
        if (stepEntered && activeHat >= 0 && activeHat < program.hats.Count)
        {
            var body = program.hats[activeHat].body;
            if (pc >= 0 && pc < body.Count) ExitStep(body[pc], false);
        }
        ZeroDrive();
        if (disarm) DisarmWeapons();
    }

    void DisarmWeapons()
    { for (int i = 0; i < actFire.Length; i++) { actFire[i] = false; actSign[i] = 1f; } }

    void StartHat(int i)
    {
        AbandonHat(false);
        activeHat = i; pc = 0; loops.Clear(); stepEntered = false;
        lastFiredHat = i; activeStep = -1;
    }

    void ReleaseHat() { ReleaseHat(true); }

    /// <summary>disarm:false only when another hat takes over the same step.</summary>
    void ReleaseHat(bool disarm)
    {
        AbandonHat(disarm);
        activeHat = -1; pc = 0; loops.Clear(); stepEntered = false;
        lastFiredHat = -1; activeStep = -1;
    }

    // ---- sequence layer (every physics step) ------------------------------

    void Advance(float dt)
    {
        var body = program.hats[activeHat].body;
        bool didWork = false;
        int budget = INSTANT_BUDGET;
        while (budget-- > 0)
        {
            if (pc >= body.Count)
            {
                // Body finished. If this hat is STILL the top-true hat, re-arm
                // it in place: do not ZeroChannels, and do not idle until the
                // next 5 Hz trigger pass. That gap wiped every SET the hat had
                // made -- a hat whose body holds no blocking step produced
                // literally nothing -- and cost up to 0.20 s of dead time on
                // every single loop of every other hat.
                int topNow;
                topNow = TopHat();
                if (topNow == activeHat)
                {
                    if (body.Count == 0) return;   // nothing to run: hold, don't spin
                    pc = 0; loops.Clear(); stepEntered = false; activeStep = -1;
                    continue;
                }
                // Keep the weapon latch when another hat takes over; drop it
                // when the program has nothing true to say at all.
                ReleaseHat(topNow < 0);
                if (topNow < 0) return;
                // Run the incoming hat's first step NOW. Returning here cost
                // one physics step of zero channels on every handover (R2
                // minor 1) -- the TriggerEval path never had that hole.
                StartHat(topNow);
                body = program.hats[activeHat].body;
                continue;
            }
            var b = body[pc];
            activeStep = pc;
            if (!IsStructural(b.op)) didWork = true;
            switch (b.op)
            {
                case POp.SetMotor: WriteMotor(b); pc++; continue;
                case POp.TurnToward: turnGain = Mathf.Clamp(b.arg, 0f, 100f) * 0.01f; pc++; continue;
                case POp.StopAll: ZeroChannels(); pc++; continue;

                // ---- V2.2 macro instants ----------------------------------
                case POp.Weapon:
                {
                    bool on = b.arg >= 0.5f;
                    for (int i = 0; i < actFire.Length; i++)
                    { actFire[i] = on; actSign[i] = 1f; }
                    pc++; continue;
                }
                case POp.Move:
                    if (!b.Blocking)   // SET flavor: straight drive, persists
                    { WriteAllWheels(b.arg); pc++; continue; }
                    goto case POp.RunMotor;
                case POp.TurnLR:
                    if (!b.Blocking)   // SET flavor: pivot in place, persists
                    { WriteTurn(b.arg); pc++; continue; }
                    goto case POp.RunMotor;
                case POp.MoveRel:
                    if (!b.Blocking)   // SET flavor: live steering MODE
                    { relTarget = b.target; relPct = b.arg; pc++; continue; }
                    goto case POp.RunMotor;
                case POp.TurnBy:
                case POp.FaceSide:
                    goto case POp.RunMotor;
                case POp.If:
                    if (b.cond != null && CondsTrue(oneTerm(b.cond))) pc++;
                    else pc = SkipFromIf(body, pc);
                    continue;
                case POp.Else: pc = SkipToEndIf(body, pc); continue;
                case POp.EndIf: pc++; continue;
                case POp.Repeat:
                    loops.Add(new LoopFrame { startPc = pc, left = Mathf.Max(1, (int)b.arg), forever = false });
                    pc++; continue;
                case POp.Forever:
                    loops.Add(new LoopFrame { startPc = pc, forever = true });
                    pc++; continue;
                case POp.End:
                    if (loops.Count == 0) { pc++; continue; }   // unbalanced: validated against, defensive
                    var f = loops[loops.Count - 1];
                    if (f.forever) { pc = f.startPc + 1; continue; }
                    f.left--;
                    if (f.left > 0) { loops[loops.Count - 1] = f; pc = f.startPc + 1; }
                    else { loops.RemoveAt(loops.Count - 1); pc++; }
                    continue;
                case POp.RunMotor:
                case POp.Wait:
                case POp.Fire:
                    if (!stepEntered) { EnterStep(b); stepEntered = true; }
                    StepTick(b, dt);   // V2.2: steering macros re-aim every physics step
                    if (StepDone(b, dt)) { ExitStep(b); stepEntered = false; pc++; continue; }
                    return;   // blocked here until done (or preempted)
            }
        }
        // INSTANT BUDGET EXHAUSTED. If the hat did any WORK on the way -- any
        // step that is not pure loop scaffolding -- that is just a SET-only
        // hat holding its command, which is correct and must not be disturbed.
        if (didWork) return;
        //
        // step without ever reaching a blocking one: it is spinning. The R3
        // critic built the case Validate cannot see --
        //     FOREVER { IF (something false) { MOVE 70% FOR 1.0 s } }
        // which walks FOREVER -> IF -> skip -> END -> FOREVER forever. Measured
        // 8.0 s, 0.00 m, 0% wheel command, and -- the real damage -- the WALL!
        // safety hat sitting UNDER it never ran once, because preemption is
        // strictly higher hats only. A hat that cannot reach a blocking step
        // this step yields to the hats below it.
        spunHat = activeHat; spunUntil = clock + SPUN_COOLDOWN;
        int lower = TopHat();
        ReleaseHat(lower < 0);
        if (lower >= 0) StartHat(lower);
    }

    readonly List<PCondTerm> oneTermList = new List<PCondTerm>(1);
    List<PCondTerm> oneTerm(PCondTerm t) { oneTermList.Clear(); oneTermList.Add(t); return oneTermList; }

    static int SkipFromIf(List<PBlock> body, int ifPc)
    {
        int depth = 0;
        for (int i = ifPc; i < body.Count; i++)
        {
            if (body[i].op == POp.If) depth++;
            else if (body[i].op == POp.Else && depth == 1) return i + 1;
            else if (body[i].op == POp.EndIf) { depth--; if (depth == 0) return i + 1; }
        }
        return body.Count;
    }

    static int SkipToEndIf(List<PBlock> body, int elsePc)
    {
        int depth = 1;
        for (int i = elsePc + 1; i < body.Count; i++)
        {
            if (body[i].op == POp.If) depth++;
            else if (body[i].op == POp.EndIf) { depth--; if (depth == 0) return i + 1; }
        }
        return body.Count;
    }

    void EnterStep(PBlock b)
    {
        stepT0 = clock; roundsAcc = 0f; StallReset();
        if (b.op == POp.RunMotor) WriteMotor(b);
        else if (b.op == POp.Fire)
        {
            SetFire(b, true);
            fireBase = CyclesOf(b);
        }
        else if (b.op == POp.Move) WriteAllWheels(b.arg);
        else if (b.op == POp.TurnLR) WriteTurn(b.arg);
        else if (b.op == POp.TurnBy)
        {
            yawAcc = 0f;
            lastYaw = self != null ? self.transform.eulerAngles.y : 0f;
        }
        // MoveRel/FaceSide write nothing at enter — StepTick aims them.
    }

    /// <summary>V2.2: the per-physics-step body of the steering macros. Runs
    /// every step while the macro blocks, so TOWARD/AWAY and SIDE TO track a
    /// moving target instead of a stale bearing.</summary>
    void StepTick(PBlock b, float dt)
    {
        switch (b.op)
        {
            case POp.TurnBy:
            {
                float yaw = self != null ? self.transform.eulerAngles.y : lastYaw;
                yawAcc += Mathf.DeltaAngle(lastYaw, yaw);
                lastYaw = yaw;
                float remain = Mathf.Abs(b.arg) - Mathf.Abs(yawAcc);
                float p = Mathf.Clamp(remain * 0.02f, TURNBY_MIN, TURNBY_MAX);
                WriteTurn(Mathf.Sign(b.arg) * p * 100f);
                break;
            }
            case POp.MoveRel:
            {
                float drive, steer; RelCommand(b.target, b.arg, out drive, out steer);
                WriteSteered(drive, steer);
                break;
            }
            case POp.FaceSide:
            {
                float bear; bool sig = TargetSignal(b.target, out bear);
                if (!sig) break;
                float offset = b.idx == 1 ? 90f : b.idx == 2 ? 180f : b.idx == 3 ? -90f : 0f;
                float err = Mathf.DeltaAngle(offset, bear);
                float p = Mathf.Clamp(Mathf.Abs(err) * 0.02f, 0.2f, 0.65f);
                WriteTurn(Mathf.Sign(err) * p * 100f);
                break;
            }
        }
    }

    /// <summary>Has this blocking step moved the robot AT ALL lately? F8
    /// (behaviour critic R2): the only escape from a jammed TURN BY or rounds
    /// step was the 10 s MAX_DUR, so a robot wedged against a wall burned the
    /// full 10.00 s for 0.0 degrees of turn, then re-armed the same hat and
    /// wedged again -- 51% of one measured 24 s fight. A step that cannot
    /// move the robot is finished, whatever the clock says. WAIT and FIRE are
    /// exempt on purpose: neither is supposed to move anything.</summary>
    bool Stalled()
    {
        if (self == null) return false;
        Vector3 pos = self.transform.position;
        float yaw = self.transform.eulerAngles.y;
        if (Vector3.Distance(pos, stallPos) > STALL_POS
         || Mathf.Abs(Mathf.DeltaAngle(yaw, stallYaw)) > STALL_YAW)
        { stallPos = pos; stallYaw = yaw; stallMark = clock; return false; }
        return clock - stallMark >= STALL_GRACE;
    }

    void StallReset()
    {
        if (self != null)
        { stallPos = self.transform.position; stallYaw = self.transform.eulerAngles.y; }
        stallMark = clock;
    }

    bool StepDone(PBlock b, float dt)
    {
        switch (b.op)
        {
            case POp.Wait: return clock - stepT0 + 1e-6f >= b.dur;
            case POp.RunMotor:
            case POp.Move:
            case POp.MoveRel:
                if (b.rounds > 0)
                {
                    roundsAcc += RoundsDelta(b, dt);
                    if (roundsAcc >= b.rounds) return true;
                    return clock - stepT0 >= RobotProgram.MAX_DUR || Stalled();
                }
                return clock - stepT0 + 1e-6f >= b.dur;
            case POp.TurnLR:
                return clock - stepT0 + 1e-6f >= b.dur;
            case POp.TurnBy:
                if (Mathf.Abs(yawAcc) + 0.5f >= Mathf.Abs(b.arg)) return true;
                return clock - stepT0 >= RobotProgram.MAX_DUR || Stalled();
            case POp.FaceSide:
            {
                float bear; bool sig = TargetSignal(b.target, out bear);
                if (!sig) return true;   // no signal — the verb cannot aim; move on
                float offset = b.idx == 1 ? 90f : b.idx == 2 ? 180f : b.idx == 3 ? -90f : 0f;
                if (Mathf.Abs(Mathf.DeltaAngle(offset, bear)) < FACE_TOL) return true;
                return clock - stepT0 >= RobotProgram.MAX_DUR || Stalled();
            }
            case POp.Fire:
                if (CyclesOf(b) - fireBase >= (int)b.arg) return true;
                return clock - stepT0 >= b.arg * FIRE_CYCLE_TIMEOUT;
            default: return true;
        }
    }

    /// <summary>F5 (behaviour critic R2, quantified R3): a timed step ended
    /// its COMMAND, but nothing ever stopped the robot. MOVE 70% FOR 1.0 s
    /// travelled 2.10 m under power and then coasted 3.19 m further -- every
    /// timed step landed 1.3x to 2.5x beyond what it read, so no sequence of
    /// them went where the program said. Zeroing the wheels is not braking.
    /// We command a short counter-thrust against whatever the body is still
    /// doing, linear AND angular, and stand it down the instant a later step
    /// drives. Keyboard feel is untouched: this is the program path only.</summary>
    void BeginBrake()
    {
        ZeroWheels();
        brakeUntil = 0f; brakeFwd = 0f; brakeSteer = 0f;
        var rb = self != null ? self.GetComponent<Rigidbody>() : null;
        if (rb == null) return;
        float fwd = Vector3.Dot(rb.linearVelocity, self.transform.forward);
        if (Mathf.Abs(fwd) >= BRAKE_STOP) brakeFwd   = -Mathf.Sign(fwd) * BRAKE_POWER;
        if (brakeFwd != 0f) brakeUntil = clock + BRAKE_TIME;
    }

    void ExitStep(PBlock b) { ExitStep(b, true); }

    /// <summary>brake:false when the step is being ABANDONED rather than
    /// finishing on its own. A handover must NOT brake -- the incoming hat
    /// wants to drive, and braking every preemption left a safety hat unable
    /// to accelerate at all (0.14 m in 2 s, measured). Aiming verbs (TURN BY,
    /// SIDE TO) do not brake either: an angular counter-thrust overshot the
    /// other way worse than the coast it was meant to cancel (SIDE TO landed
    /// 28-54 deg out with one, 1-18 deg without).</summary>
    void ExitStep(PBlock b, bool brake)
    {
        if (b.op == POp.RunMotor) ZeroMotor(b);       // "ran for n" => stops after n
        else if (b.op == POp.Fire) SetFire(b, false);
        else if (b.op == POp.Move || b.op == POp.MoveRel)
        {
            if (brake) BeginBrake();                  // a timed MOVE STOPS
            else ZeroWheels();
        }
        else if (b.op == POp.TurnLR || b.op == POp.TurnBy || b.op == POp.FaceSide)
            ZeroWheels();
    }

    // ---- channel state ----------------------------------------------------

    // ---- V2.2 macro channel helpers ---------------------------------------

    /// <summary>All wheels one signed power. Any explicit wheel write ends a
    /// live MoveRel steering mode — last writer wins, like every channel.</summary>
    void WriteAllWheels(float pct)
    {
        relTarget = -1;
        float p = Mathf.Clamp(pct, -100f, 100f) * 0.01f;
        for (int i = 0; i < wheelState.Length; i++) wheelState[i] = p;
    }

    /// <summary>Pivot in place: +pct turns RIGHT (left wheels forward).</summary>
    void WriteTurn(float pct)
    {
        relTarget = -1;
        float p = Mathf.Clamp(pct, -100f, 100f) * 0.01f;
        foreach (var i in leftCh) if (i < wheelState.Length) wheelState[i] = p;
        foreach (var i in rightCh) if (i < wheelState.Length) wheelState[i] = -p;
    }

    /// <summary>The drive+steer pair for a relative move. ONE copy: this math
    /// lived TWICE — StepTick's blocking flavour and ApplyChannels' live SET
    /// mode — and both copies carried the same defect, so they get one
    /// definition now.
    ///
    /// THE BUG (owen, 2026-08-07): AWAY FROM was "rotate 180 degrees, then
    /// drive FORWARD" — the base was Mathf.Abs() and never went negative, so
    /// the robot NEVER reversed, at any bearing. Nose-on to a wall is the
    /// worst case in that scheme: the heading error is exactly 180, the
    /// BALANCE POINT of the steer controller, whose sign flips on a hundredth
    /// of a degree of jitter. Measured: +0.40 net thrust INTO the wall while
    /// twitching left-right. The machine ground itself into the wall and the
    /// WALL! safety hat — the one that exists to prevent exactly that — was
    /// what held it there.
    ///
    /// AWAY now means what it says: INCREASE THE DISTANCE. The drive term is
    /// -cos(bearing) — full reverse with the target dead ahead, full forward
    /// with it dead astern, a pure pivot abeam — and the steer comes off the
    /// LATERAL component (-sin), which is zero both dead ahead and dead
    /// astern, so there is no balance point anywhere on the circle. Reversing
    /// mirrors which way the nose swings, hence the sign flip on a negative
    /// drive. TOWARD keeps the same bearing error, but its forward base is
    /// now scaled by cos as well, so a steer that saturates pivots the nose
    /// in rather than flying a circle the robot can never close.</summary>
    /// <summary>Drive + steer for one MOVE TOWARD/AWAY step.
    ///
    /// AWAY FROM WALL is the reason this wrapper exists. Every other target
    /// is a THING with a position; the arena is not. "Get away from the
    /// nearest plane" is unsatisfiable inside a bounded box -- there is
    /// always another wall behind you -- and the nearest plane changes
    /// IDENTITY at the mid-line, so the command reversed at full magnitude
    /// with nothing to damp it. Behaviour critic R3, measured: wallDist
    /// ping-ponging 1.02-6.91 m forever, five 180-degree flips in 11 s, peak
    /// 7.5 m/s, ending up closer to a wall than the 1.2 m threshold the WALL!
    /// hat exists to defend; and a stationary robot at the arena centre
    /// chattering 40 full reversals in 18 s.
    ///
    /// Two changes, and it needed both:
    ///   1. STEER ON THE FIELD, not the nearest plane. SensorBus sums an
    ///      inverse-square repulsion over all four walls -- continuous
    ///      everywhere, no antipode, zero at the centre.
    ///   2. GOVERN THE THRUST by how pinned the robot actually is. Full
    ///      authority at or inside WALL_CLEAR, tapering to nothing at the
    ///      centre. That is the verb's missing SET POINT: "open the range"
    ///      is now a goal it can reach and stop at, not an appetite it can
    ///      never satisfy -- and it is the speed limit that stops a retreat
    ///      crossing the whole arena and arriving at the far wall 0.9 s
    ///      later.
    ///
    /// The verb still means exactly what it says: increase the clearance.
    /// It just knows when it has.</summary>
    void RelCommand(int target, float pct, out float drive01, out float steer)
    {
        if (pct < 0f && target == (int)PTarget.Wall && bus != null && bus.wallValid)
        {
            float gov = WallUrgency();
            if (gov <= 0.001f || !bus.wallFieldValid)
            { drive01 = 0f; steer = 0f; return; }      // clear of every wall
            // Driving AWAY along the escape field is the same thing as
            // driving TOWARD (escape + 180), which is exactly what RelDrive's
            // AWAY branch already computes. One controller, two framings.
            RelDrive(pct, bus.wallEscapeDeg + 180f, true, out drive01, out steer);
            drive01 *= gov; steer *= gov;
            return;
        }
        float bear; bool sig = TargetSignal(target, out bear);
        RelDrive(pct, bear, sig, out drive01, out steer);
    }

    /// <summary>1 while pinned at or inside WALL_CLEAR, falling linearly to 0
    /// at the arena centre, where the escape field is zero as well -- so the
    /// robot arrives, stops, and stays stopped instead of hunting.</summary>
    float WallUrgency()
    {
        float half = BuilderManager.ARENA_HALF;
        float span = Mathf.Max(half - WALL_CLEAR, 0.01f);
        return Mathf.Clamp01((half - bus.wallDist) / span);
    }

    static void RelDrive(float argPct, float bearingDeg, bool signal,
                         out float drive01, out float steer)
    {
        bool away = argPct < 0f;
        float rad = bearingDeg * Mathf.Deg2Rad;
        float errDeg = away ? -Mathf.Sin(rad) * 90f : bearingDeg;
        steer = signal ? Mathf.Clamp(errDeg * STEER_GAIN, -1f, 1f) : 0f;
        // With no signal we cannot know where it is: drive straight on, as
        // before. With a signal, scale thrust by ALIGNMENT so a saturated
        // steer pivots in place instead of orbiting -- AWAY reverses out
        // (-cos), TOWARD closes (cos, floored so it never fully stalls).
        float fwd = 1f;
        if (signal) fwd = away ? -Mathf.Cos(rad)
                               : Mathf.Max(0.15f, Mathf.Cos(rad));
        drive01 = Mathf.Abs(argPct) * 0.01f * fwd;
        if (drive01 < 0f) steer = -steer;
    }

    /// <summary>SIGNED base with differential steer (steer>0 turns right).</summary>
    void WriteSteered(float base01, float steer)
    {
        relTarget = -1;
        foreach (var i in leftCh) if (i < wheelState.Length)
            wheelState[i] = Mathf.Clamp(base01 + steer, -1f, 1f);
        foreach (var i in rightCh) if (i < wheelState.Length)
            wheelState[i] = Mathf.Clamp(base01 - steer, -1f, 1f);
    }

    void ZeroWheels()
    {
        relTarget = -1;
        for (int i = 0; i < wheelState.Length; i++) wheelState[i] = 0f;
    }

    /// <summary>Signed bearing to a macro target off the bus. false = that
    /// sensor is absent/dead — the caller drives straight or finishes.</summary>
    bool TargetSignal(int target, out float bearingDeg)
    {
        bearingDeg = 0f;
        if (bus == null) return false;
        switch ((PTarget)target)
        {
            case PTarget.Enemy:
                if (!bus.compassValid) return false;
                bearingDeg = bus.enemyBearingDeg; return true;
            case PTarget.Wall:
                if (!bus.wallValid) return false;
                bearingDeg = bus.wallBearingDeg; return true;
            default:
                if (!bus.trapValid || bus.trapDist > 90f) return false;
                bearingDeg = bus.trapBearingDeg; return true;
        }
    }

    void WriteMotor(PBlock b)
    {
        float pct = Mathf.Clamp(b.arg, -100f, 100f) * 0.01f;
        if (b.op == POp.SetMotor || b.op == POp.RunMotor)
        {
            // explicit wheel writes end a live steering mode (last writer wins)
            if (b.part == PPart.AllWheels || b.part == PPart.LeftWheels
                || b.part == PPart.RightWheels || b.part == PPart.Wheel) relTarget = -1;
        }
        switch (b.part)
        {
            case PPart.AllWheels: for (int i = 0; i < wheelState.Length; i++) wheelState[i] = pct; break;
            case PPart.LeftWheels: foreach (var i in leftCh) if (i < wheelState.Length) wheelState[i] = pct; break;
            case PPart.RightWheels: foreach (var i in rightCh) if (i < wheelState.Length) wheelState[i] = pct; break;
            case PPart.Wheel: if (b.idx >= 0 && b.idx < wheelState.Length) wheelState[b.idx] = pct; break;
            case PPart.AllActuators:
                for (int i = 0; i < actFire.Length; i++)
                { actFire[i] = Mathf.Abs(pct) >= 0.05f; actSign[i] = pct < 0f ? -1f : 1f; }
                break;
            case PPart.Actuator:
                if (b.idx >= 0 && b.idx < actFire.Length)
                { actFire[b.idx] = Mathf.Abs(pct) >= 0.05f; actSign[b.idx] = pct < 0f ? -1f : 1f; }
                break;
        }
    }

    void ZeroMotor(PBlock b)
    {
        var z = new PBlock { op = POp.SetMotor, part = b.part, idx = b.idx, arg = 0f };
        WriteMotor(z);
    }

    void SetFire(PBlock b, bool on)
    {
        if (b.part == PPart.Actuator)
        { if (b.idx >= 0 && b.idx < actFire.Length) actFire[b.idx] = on; }
        else
            for (int i = 0; i < actFire.Length; i++) actFire[i] = on;
    }

    int CyclesOf(PBlock b)
    {
        if (b.part == PPart.Actuator)
            return (b.idx >= 0 && b.idx < actuators.Length && actuators[b.idx] != null)
                 ? actuators[b.idx].cyclesDone : 0;
        int s = 0;
        foreach (var a in actuators) if (a != null) s += a.cyclesDone;
        return s;
    }

    /// <summary>DRIVE channels only. A hat handover must NOT touch the weapon
    /// latch. Behaviour critic R2 measured a real fight where a program that
    /// says WEAPON ON in two hats and never says WEAPON OFF anywhere ran its
    /// spinner for 9% of the fight, because every StartHat/ReleaseHat wiped
    /// actFire. Coasting the wheels on handover is right; silently disarming
    /// the weapon is not -- there was no way to express "keep spinning".</summary>
    void ZeroDrive()
    {
        for (int i = 0; i < wheelState.Length; i++) wheelState[i] = 0f;
        turnGain = 0f; relTarget = -1;
    }

    /// <summary>Everything, weapon latch included. STOP ALL and death.</summary>
    void ZeroChannels()
    {
        ZeroDrive();
        brakeUntil = 0f; brakeFwd = 0f; brakeSteer = 0f;
        for (int i = 0; i < actFire.Length; i++) { actFire[i] = false; actSign[i] = 1f; }
    }

    void ApplyChannels()
    {
        drive.directWheelCmd = true;
        // V2.2: a live MoveRel SET mode recomputes its steering into the
        // wheel channels every step — the SET persists, the aim stays fresh.
        if (relTarget >= 0)
        {
            float baseP, st; RelCommand(relTarget, relPct, out baseP, out st);
            foreach (var i in leftCh) if (i < wheelState.Length)
                wheelState[i] = Mathf.Clamp(baseP + st, -1f, 1f);
            foreach (var i in rightCh) if (i < wheelState.Length)
                wheelState[i] = Mathf.Clamp(baseP - st, -1f, 1f);
        }
        float steer = 0f;
        if (turnGain > 0.001f && bus != null && bus.compassValid)
            steer = Mathf.Clamp(bus.enemyBearingDeg * TURN_GAIN * turnGain, -1f, 1f);
        // A brake only stands while NOTHING ELSE is driving: the moment the
        // next step writes a wheel command the brake stands down, so a REPEAT
        // of back-to-back moves never fights itself.
        if (clock >= brakeUntil) { brakeFwd = 0f; brakeSteer = 0f; }
        else
        {
            bool idle = relTarget < 0 && Mathf.Abs(steer) < 0.001f;
            if (idle)
                for (int i = 0; i < wheelState.Length; i++)
                    if (Mathf.Abs(wheelState[i]) > 0.01f) { idle = false; break; }
            // Closed loop: a fixed pulse was hopeless on a fast build (0.20 s
            // at 75% barely dented 8.4 m/s). Push against the motion until it
            // is actually gone, capped by BRAKE_TIME so a jam cannot hold the
            // wheels, and stand down the moment a later step drives.
            var rbb = idle && self != null ? self.GetComponent<Rigidbody>() : null;
            float v = rbb != null ? Vector3.Dot(rbb.linearVelocity, self.transform.forward) : 0f;
            if (!idle || rbb == null || Mathf.Abs(v) < BRAKE_STOP)
            { brakeUntil = 0f; brakeFwd = 0f; }
            else brakeFwd = -Mathf.Sign(v) * BRAKE_POWER;
        }
        int n = Mathf.Min(wheelState.Length, drive.ChannelCount);
        for (int i = 0; i < n; i++)
        {
            float s = 0f;
            if (steer != 0f)
                s = leftCh.Contains(i) ? steer : (rightCh.Contains(i) ? -steer : 0f);
            float bs = brakeSteer == 0f ? 0f
                     : (leftCh.Contains(i) ? brakeSteer
                                           : (rightCh.Contains(i) ? -brakeSteer : 0f));
            drive.SetWheelCmd(i, Mathf.Clamp(wheelState[i] + s + brakeFwd + bs, -1f, 1f));
        }
        for (int i = 0; i < actuators.Length; i++)
        {
            if (actuators[i] == null) continue;
            actuators[i].aiFire = actFire[i];
            actuators[i].aiSpinSign = actSign[i];
        }
    }

    // ---- rounds: measured from the world, not the command -----------------

    float RoundsDelta(PBlock b, float dt)
    {
        if (b.part == PPart.AllActuators || b.part == PPart.Actuator)
        {
            float sum = 0f; int c = 0;
            if (b.part == PPart.Actuator)
            {
                if (b.idx >= 0 && b.idx < actuators.Length && actuators[b.idx] != null)
                { sum = actuators[b.idx].rate; c = 1; }
            }
            else
                foreach (var a in actuators) if (a != null) { sum += a.rate; c++; }
            if (c == 0) return 0f;
            return (sum / c) * dt / (2f * Mathf.PI);
        }
        // wheel groups: mean |roll speed| / circumference
        float circ = drive.WheelCircumference();
        if (circ < 1e-4f) return 0f;
        float v = 0f; int k = 0;
        switch (b.part)
        {
            case PPart.Wheel:
                v = Mathf.Abs(drive.WheelRollSpeed(b.idx)); k = 1; break;
            case PPart.LeftWheels:
                foreach (var i in leftCh) { v += Mathf.Abs(drive.WheelRollSpeed(i)); k++; } break;
            case PPart.RightWheels:
                foreach (var i in rightCh) { v += Mathf.Abs(drive.WheelRollSpeed(i)); k++; } break;
            default:
                for (int i = 0; i < wheelCount; i++) { v += Mathf.Abs(drive.WheelRollSpeed(i)); k++; }
                break;
        }
        if (k == 0) return 0f;
        return (v / k) / circ * dt;
    }

    // ---- conditions (unchanged from v1) -----------------------------------

    bool CondsTrue(List<PCondTerm> terms)
    {
        for (int i = 0; i < terms.Count; i++)
        {
            var t = terms[i];
            if (t.kind == PCond.Always) continue;
            bool ok; float v = Read(t.kind, out ok);
            if (!ok) return false;
            bool pass = t.cmp == PCmp.Less ? v < t.value : v > t.value;
            if (!pass) return false;
        }
        return true;
    }

    float Read(PCond k, out bool ok)
    {
        ok = false;
        if (bus == null) return 0f;
        switch (k)
        {
            case PCond.EnemyRange: ok = bus.compassValid; return bus.enemyRange;
            case PCond.EnemyBearingAbs: ok = bus.compassValid; return Mathf.Abs(bus.enemyBearingDeg);
            case PCond.RangeHit: ok = bus.rangeValid; return bus.rangeDist;
            case PCond.RangeEnemy: ok = bus.rangeValid && bus.rangeTag == "enemy"; return bus.rangeDist;
            case PCond.TiltUpY: ok = bus.tiltValid; return bus.upY;
            case PCond.Flipped: ok = bus.tiltValid; return bus.flipped ? 1f : 0f;
            case PCond.EdgeDist: ok = bus.wallValid; return bus.wallDist;         // V2.2: wall sensor
            case PCond.HazardNear: ok = bus.trapValid; return bus.trapNear ? 1f : 0f;
            case PCond.TrapDist: ok = bus.trapValid; return bus.trapDist;
            case PCond.HpFrac:    ok = everBus; return bus.busValid ? bus.hpFrac : 0f;
            case PCond.PowerFrac: ok = everBus; return bus.busValid ? bus.powerFrac : 0f;
            case PCond.HitRecently: ok = bus.busValid; return bus.hitRecently ? 1f : 0f;
            case PCond.PartsLost: ok = everBus; return bus.busValid ? bus.partsLost : lastParts;
            default: return 0f;
        }
    }

    void Trace()
    {
        trace.Append("t=").Append(clock.ToString("F1"))
             .Append(" h=").Append(activeHat)
             .Append(" pc=").Append(activeHat >= 0 ? pc : -1);
        int n = Mathf.Min(wheelState.Length, drive != null ? drive.ChannelCount : 0);
        for (int i = 0; i < n; i++)
            trace.Append(i == 0 ? " w=" : ",").Append(wheelState[i].ToString("F2"));
        trace.Append(" f=");
        for (int i = 0; i < actFire.Length; i++) trace.Append(actFire[i] ? '1' : '0');
        trace.Append('\n');
    }
}
}
