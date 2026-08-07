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
    }

    void FixedUpdate()
    {
        if (self == null || drive == null || program == null) return;
        if (self.dead || self.controlSource != ControlSource.Program)
        {
            if (activeHat >= 0) ReleaseHat();
            lastFiredHat = -1;
            return;
        }
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
        int top = -1;
        for (int i = 0; i < program.hats.Count; i++)
            if (CondsTrue(program.hats[i].when)) { top = i; break; }

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
            ReleaseHat();
            if (top >= 0) StartHat(top);
        }
    }

    bool InForever()
    {
        for (int i = 0; i < loops.Count; i++) if (loops[i].forever) return true;
        return false;
    }

    void StartHat(int i)
    {
        ZeroChannels();
        activeHat = i; pc = 0; loops.Clear(); stepEntered = false;
        lastFiredHat = i; activeStep = -1;
    }

    void ReleaseHat()
    {
        ZeroChannels();
        activeHat = -1; pc = 0; loops.Clear(); stepEntered = false;
        lastFiredHat = -1; activeStep = -1;
    }

    // ---- sequence layer (every physics step) ------------------------------

    void Advance(float dt)
    {
        var body = program.hats[activeHat].body;
        int budget = INSTANT_BUDGET;
        while (budget-- > 0)
        {
            if (pc >= body.Count) { ReleaseHat(); return; }
            var b = body[pc];
            activeStep = pc;
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
        // instant budget exhausted — yield to the next physics step. With the
        // loop-needs-a-blocking-step validation this is a defensive backstop.
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
        stepT0 = clock; roundsAcc = 0f;
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
                float bear; bool sig = TargetSignal(b.target, out bear);
                float basePct = Mathf.Abs(b.arg);
                float err = b.arg >= 0f ? bear : Mathf.DeltaAngle(0f, bear + 180f);
                float steer = sig ? Mathf.Clamp(err * STEER_GAIN, -1f, 1f) : 0f;
                WriteSteered(basePct * 0.01f, steer);
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
                    return clock - stepT0 >= RobotProgram.MAX_DUR;   // stall timeout
                }
                return clock - stepT0 + 1e-6f >= b.dur;
            case POp.TurnLR:
                return clock - stepT0 + 1e-6f >= b.dur;
            case POp.TurnBy:
                if (Mathf.Abs(yawAcc) + 0.5f >= Mathf.Abs(b.arg)) return true;
                return clock - stepT0 >= RobotProgram.MAX_DUR;       // stall timeout
            case POp.FaceSide:
            {
                float bear; bool sig = TargetSignal(b.target, out bear);
                if (!sig) return true;   // no signal — the verb cannot aim; move on
                float offset = b.idx == 1 ? 90f : b.idx == 2 ? 180f : b.idx == 3 ? -90f : 0f;
                if (Mathf.Abs(Mathf.DeltaAngle(offset, bear)) < FACE_TOL) return true;
                return clock - stepT0 >= RobotProgram.MAX_DUR;
            }
            case POp.Fire:
                if (CyclesOf(b) - fireBase >= (int)b.arg) return true;
                return clock - stepT0 >= b.arg * FIRE_CYCLE_TIMEOUT;
            default: return true;
        }
    }

    void ExitStep(PBlock b)
    {
        if (b.op == POp.RunMotor) ZeroMotor(b);       // "ran for n" ⇒ stops after n
        else if (b.op == POp.Fire) SetFire(b, false);
        else if (b.op == POp.Move || b.op == POp.TurnLR || b.op == POp.TurnBy
              || b.op == POp.MoveRel || b.op == POp.FaceSide)
            ZeroWheels();                             // timed macros stop what they drove
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

    /// <summary>Forward base with differential steer (steer>0 turns right).</summary>
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

    void ZeroChannels()
    {
        for (int i = 0; i < wheelState.Length; i++) wheelState[i] = 0f;
        for (int i = 0; i < actFire.Length; i++) { actFire[i] = false; actSign[i] = 1f; }
        turnGain = 0f; relTarget = -1;
    }

    void ApplyChannels()
    {
        drive.directWheelCmd = true;
        // V2.2: a live MoveRel SET mode recomputes its steering into the
        // wheel channels every step — the SET persists, the aim stays fresh.
        if (relTarget >= 0)
        {
            float bear; bool sig = TargetSignal(relTarget, out bear);
            float baseP = Mathf.Abs(relPct) * 0.01f;
            float err = relPct >= 0f ? bear : Mathf.DeltaAngle(0f, bear + 180f);
            float st = sig ? Mathf.Clamp(err * STEER_GAIN, -1f, 1f) : 0f;
            foreach (var i in leftCh) if (i < wheelState.Length)
                wheelState[i] = Mathf.Clamp(baseP + st, -1f, 1f);
            foreach (var i in rightCh) if (i < wheelState.Length)
                wheelState[i] = Mathf.Clamp(baseP - st, -1f, 1f);
        }
        float steer = 0f;
        if (turnGain > 0.001f && bus != null && bus.compassValid)
            steer = Mathf.Clamp(bus.enemyBearingDeg * TURN_GAIN * turnGain, -1f, 1f);
        int n = Mathf.Min(wheelState.Length, drive.ChannelCount);
        for (int i = 0; i < n; i++)
        {
            float s = 0f;
            if (steer != 0f)
                s = leftCh.Contains(i) ? steer : (rightCh.Contains(i) ? -steer : 0f);
            drive.SetWheelCmd(i, Mathf.Clamp(wheelState[i] + s, -1f, 1f));
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
            case PCond.HpFrac: ok = bus.busValid; return bus.hpFrac;
            case PCond.PowerFrac: ok = bus.busValid; return bus.powerFrac;
            case PCond.HitRecently: ok = bus.busValid; return bus.hitRecently ? 1f : 0f;
            case PCond.PartsLost: ok = bus.busValid; return bus.partsLost;
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
