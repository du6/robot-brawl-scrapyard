using System.Collections.Generic;
using UnityEngine;

namespace RobotBrawl.Phase0
{
/// <summary>V2 — the program data model (V2 design doc §1, owen 2026-08-06).
///
/// A program is a priority list of up to 12 HATS. A hat's WHEN is a
/// conjunction of ≤3 sensor threshold terms (unchanged from v1). Its BODY is
/// now a SEQUENCE — ordered steps that unfold over time, Scratch style:
/// motor steps address INDIVIDUAL components (each wheel, each actuator, or
/// the L/R/ALL groups) with signed direction, either instantly (SET, which
/// persists) or blocking (RUN … for n seconds / n rounds — rounds measured
/// from real physics rotation). Control steps: WAIT, IF/ELSE, REPEAT n,
/// FOREVER. Topmost true hat runs; sequences run to completion; a higher hat
/// preempts (sequence resets); FOREVER bodies release when their WHEN goes
/// false. The v1 latch is RETIRED — commitment is the sequence itself.
///
/// Serialization stays flat JsonUtility markers (If/Else/EndIf +
/// Repeat/Forever/End). version == 2; version-1 payloads are DISCARDED at
/// load (FromJson returns null) — only harness programs ever existed in v1
/// saves, which is exactly why the break ships now.
///
/// Lint(): the compile-phase conflict detection owen asked for — exact
/// interval intersection over WHEN terms × static motor write-sets. A lint,
/// never a refusal: overlaps are how overrides work; the message names who
/// wins.</summary>
public enum PCond
{
    Always = 0,
    EnemyRange = 1, EnemyBearingAbs = 2,
    RangeHit = 3, RangeEnemy = 4,
    TiltUpY = 5, Flipped = 6,
    EdgeDist = 7, HazardNear = 8,   // V2.2: EdgeDist reads the WALL sensor, HazardNear the TRAP sensor
    HpFrac = 9, PowerFrac = 10, HitRecently = 11, PartsLost = 12,
    TrapDist = 13                   // V2.2: distance to the nearest live hazard (TRAP sensor)
}

/// <summary>V2.2 — what a relative macro verb aims at. Each target is
/// unlocked by its sensor part: enemy by the compass, wall/trap by the
/// split halves of the old edge sentinel.</summary>
public enum PTarget { Enemy = 0, Wall = 1, Trap = 2 }

public enum PCmp { Less = 0, Greater = 1 }

[System.Serializable]
public class PCondTerm
{
    public PCond kind;
    public PCmp cmp;
    public float value;
    public static PCondTerm Mk(PCond k, PCmp c, float v)
    { return new PCondTerm { kind = k, cmp = c, value = v }; }
    public static PCondTerm Always() { return Mk(PCond.Always, PCmp.Greater, 0f); }
}

/// <summary>Motor target. Wheel/Actuator use PBlock.idx (0-based, in
/// placement order — the same W1…Wn the builder badges show).</summary>
public enum PPart
{
    AllWheels = 0, LeftWheels = 1, RightWheels = 2, Wheel = 3,
    AllActuators = 4, Actuator = 5
}

public enum POp
{
    SetMotor = 0,     // part±arg% — instant, persists until changed/handover
    RunMotor = 1,     // part±arg% for dur s (rounds==0) OR rounds (dur==0) — blocking
    Fire = 2,         // actuator trigger for arg cycles (1..5) — blocking
    Wait = 3,         // dur s — blocking (SET values keep holding)
    StopAll = 4,      // zero every channel at this position — instant
    TurnToward = 5,   // compass steer assist at arg% gain — instant, persists
    If = 6, Else = 7, EndIf = 8,
    Repeat = 9,       // arg = n (1..10)
    Forever = 10,
    End = 11,         // closes the nearest Repeat/Forever

    // ---- V2.2 MACRO VERBS (the player palette; owen's pivot 2026-08-06).
    // Everything below compiles onto the channel layer at runtime — the ops
    // above stay valid as the internal target but are CUT from the palette.
    Move = 12,        // blind: arg=±power% (+fwd/−back); dur>0 XOR rounds>0 = timed RUN, both 0 = SET (persists)
    TurnLR = 13,      // blind: arg=±power% (+right/−left); dur>0 = timed, 0 = SET
    TurnBy = 14,      // blind dead-reckoning: arg=±degrees (+right/−left) — blocking
    Weapon = 15,      // arg>=0.5 = ON (all weapons), else OFF — instant
    MoveRel = 16,     // target: arg=±power% (+toward/−away); dur/rounds timed, 0/0 = SET steering mode
    FaceSide = 17     // target: idx = side (0 front/1 right/2 back/3 left) — blocking until aligned
}

[System.Serializable]
public class PBlock
{
    public POp op;
    public PPart part;
    public int idx;          // Wheel/Actuator index; FaceSide: side 0..3
    public float arg;        // signed % / gain % / cycles / repeat n / TurnBy degrees
    public float dur;        // seconds (RunMotor/Move/TurnLR/MoveRel time-mode, Wait)
    public int rounds;       // RunMotor/Move/MoveRel rounds-mode (>0)
    public int target;       // V2.2 MoveRel/FaceSide: (int)PTarget
    public PCondTerm cond;   // If only

    public static PBlock Set(PPart p, int i, float pct)
    { return new PBlock { op = POp.SetMotor, part = p, idx = i, arg = pct }; }
    public static PBlock RunS(PPart p, int i, float pct, float seconds)
    { return new PBlock { op = POp.RunMotor, part = p, idx = i, arg = pct, dur = seconds }; }
    public static PBlock RunR(PPart p, int i, float pct, int nRounds)
    { return new PBlock { op = POp.RunMotor, part = p, idx = i, arg = pct, rounds = nRounds }; }
    public static PBlock MkFire(PPart p, int i, int cycles)
    { return new PBlock { op = POp.Fire, part = p, idx = i, arg = cycles }; }
    public static PBlock MkWait(float seconds)
    { return new PBlock { op = POp.Wait, dur = seconds }; }
    public static PBlock MkStop() { return new PBlock { op = POp.StopAll }; }
    public static PBlock MkTurn(float gainPct)
    { return new PBlock { op = POp.TurnToward, arg = gainPct }; }
    public static PBlock MkIf(PCondTerm c) { return new PBlock { op = POp.If, cond = c }; }
    public static PBlock MkElse() { return new PBlock { op = POp.Else }; }
    public static PBlock MkEndIf() { return new PBlock { op = POp.EndIf }; }
    public static PBlock MkRepeat(int n) { return new PBlock { op = POp.Repeat, arg = n }; }
    public static PBlock MkForever() { return new PBlock { op = POp.Forever }; }
    public static PBlock MkEnd() { return new PBlock { op = POp.End }; }

    // ---- V2.2 macro factories ---------------------------------------------
    public static PBlock MkMove(float pct, float seconds, int nRounds)
    { return new PBlock { op = POp.Move, arg = pct, dur = seconds, rounds = nRounds }; }
    public static PBlock MkTurnLR(float pct, float seconds)
    { return new PBlock { op = POp.TurnLR, arg = pct, dur = seconds }; }
    public static PBlock MkTurnBy(float degrees)
    { return new PBlock { op = POp.TurnBy, arg = degrees }; }
    public static PBlock MkWeapon(bool on)
    { return new PBlock { op = POp.Weapon, arg = on ? 100f : 0f }; }
    public static PBlock MkMoveRel(PTarget t, float pct, float seconds, int nRounds)
    { return new PBlock { op = POp.MoveRel, target = (int)t, arg = pct, dur = seconds, rounds = nRounds }; }
    public static PBlock MkFaceSide(PTarget t, int side)
    { return new PBlock { op = POp.FaceSide, target = (int)t, idx = side }; }

    /// <summary>Does this step hold the sequence? Macro Move/TurnLR/MoveRel
    /// block only in their TIMED flavor — the SET flavor (no duration, no
    /// rounds) takes effect and lets the sequence continue, exactly like the
    /// internal SET.</summary>
    public bool Blocking
    { get { return op == POp.RunMotor || op == POp.Wait || op == POp.Fire
                 || op == POp.TurnBy || op == POp.FaceSide
                 || ((op == POp.Move || op == POp.MoveRel) && (dur > 0f || rounds > 0))
                 || (op == POp.TurnLR && dur > 0f); } }
    public bool MotorWrite
    { get { return op == POp.SetMotor || op == POp.RunMotor || op == POp.Fire
                 || op == POp.StopAll || op == POp.TurnToward
                 || op == POp.Move || op == POp.TurnLR || op == POp.TurnBy
                 || op == POp.Weapon || op == POp.MoveRel || op == POp.FaceSide; } }
}

[System.Serializable]
public class PHat
{
    public string name = "";
    public string note = "";   // V2.4: read-only tutorial caption rendered above the header
    public List<PCondTerm> when = new List<PCondTerm>();
    public List<PBlock> body = new List<PBlock>();
}

[System.Serializable]
public class RobotProgram
{
    public const int MAX_HATS = 12, MAX_BLOCKS = 64, MAX_TERMS = 3, MAX_DEPTH = 3;
    public const int MAX_REPEAT = 10, MAX_ROUNDS = 20, MAX_CYCLES = 5;
    public const float MIN_DUR = 0.1f, MAX_DUR = 10f;

    public int version = 2;
    public string title = "";
    public List<PHat> hats = new List<PHat>();

    public string ToJson() { return JsonUtility.ToJson(this); }

    /// <summary>null for empty/bad/OLD-FORMAT payloads. v1 programs (version
    /// < 2) are deliberately discarded — the canvas shows "old format, start
    /// fresh" when this returns null for a non-empty string.</summary>
    public static RobotProgram FromJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        try
        {
            var p = JsonUtility.FromJson<RobotProgram>(s);
            return (p != null && p.version >= 2 && p.hats != null) ? p : null;
        }
        catch { return null; }
    }

    public int BlockCount()
    {
        int n = 0;
        foreach (var h in hats) n += h.body.Count;
        return n;
    }

    public static string SensorIdOf(PCond k)
    {
        switch (k)
        {
            case PCond.EnemyRange: case PCond.EnemyBearingAbs: return "compass";
            case PCond.RangeHit: case PCond.RangeEnemy: return "rangefinder";
            case PCond.TiltUpY: case PCond.Flipped: return "tiltsensor";
            case PCond.EdgeDist: return "wallsensor";               // V2.2 split
            case PCond.HazardNear: case PCond.TrapDist: return "trapsensor";
            case PCond.HpFrac: case PCond.PowerFrac:
            case PCond.HitRecently: case PCond.PartsLost: return "dmgbus";
            default: return null;
        }
    }

    /// <summary>V2.2: which sensor part a relative macro verb needs.</summary>
    public static string SensorIdOfTarget(int target)
    {
        switch ((PTarget)target)
        {
            case PTarget.Enemy: return "compass";
            case PTarget.Wall: return "wallsensor";
            default: return "trapsensor";
        }
    }

    public static string TargetLabel(int target)
    {
        switch ((PTarget)target)
        {
            case PTarget.Enemy: return "ENEMY";
            case PTarget.Wall: return "WALL";
            default: return "TRAP";
        }
    }

    static string SensorLabel(string id)
    {
        switch (id)
        {
            case "compass": return "Compass tracker";
            case "rangefinder": return "Rangefinder";
            case "tiltsensor": return "Tilt sensor";
            case "wallsensor": return "Wall sensor";
            case "trapsensor": return "Trap sensor";
            case "dmgbus": return "Damage bus";
            default: return id;
        }
    }

    static bool IsWheelTarget(PPart p)
    { return p == PPart.AllWheels || p == PPart.LeftWheels || p == PPart.RightWheels || p == PPart.Wheel; }

    /// <summary>Validation against a BUILD. null = ok, else ONE amber naming
    /// the first problem (the CareerValidate pattern). partIds = placed part
    /// ids in placement order (wheel/actuator indices resolve against it).</summary>
    public string Validate(ICollection<string> partIds)
    {
        if (hats.Count == 0) return "program is empty — add a WHEN hat";
        if (hats.Count > MAX_HATS) return "too many hats (" + hats.Count + "/" + MAX_HATS + ")";
        if (BlockCount() > MAX_BLOCKS) return "too many blocks (" + BlockCount() + "/" + MAX_BLOCKS + ")";
        int wheels = 0, acts = 0;
        var have = new HashSet<string>();
        if (partIds != null)
            foreach (var id in partIds)
            {
                have.Add(id);
                if (id == "wheel") wheels++;
                if (Actuator.IsActuatorId(id)) acts++;
            }
        for (int hi = 0; hi < hats.Count; hi++)
        {
            var h = hats[hi];
            string at = "hat " + (hi + 1) + (string.IsNullOrEmpty(h.name) ? "" : " (" + h.name + ")");
            if (h.when.Count == 0) return at + ": WHEN needs at least one condition";
            if (h.when.Count > MAX_TERMS) return at + ": too many WHEN terms (max " + MAX_TERMS + ")";
            foreach (var t in h.when)
            {
                string need = SensorIdOf(t.kind);
                if (need != null && !have.Contains(need))
                    return at + ": needs a " + SensorLabel(need) + " — SHOP";
            }
            if (h.body.Count == 0) return at + ": the sequence is empty — add a step";

            // one walk: marker balance + depth + loop-blocking rule + per-step checks
            int depth = 0;
            var loopHasBlocking = new List<bool>();   // stack, one per open Repeat/Forever
            var openKind = new List<POp>();           // stack of open markers (If/Repeat/Forever)
            foreach (var b in h.body)
            {
                switch (b.op)
                {
                    case POp.If:
                        depth++; openKind.Add(POp.If);
                        if (depth > MAX_DEPTH) return at + ": nesting deeper than " + MAX_DEPTH;
                        if (b.cond == null) return at + ": IF without a condition";
                        string needC = SensorIdOf(b.cond.kind);
                        if (needC != null && !have.Contains(needC))
                            return at + ": needs a " + SensorLabel(needC) + " — SHOP";
                        break;
                    case POp.Else:
                        if (openKind.Count == 0 || openKind[openKind.Count - 1] != POp.If)
                            return at + ": ELSE outside an IF";
                        break;
                    case POp.EndIf:
                        if (openKind.Count == 0 || openKind[openKind.Count - 1] != POp.If)
                            return at + ": END IF without an IF";
                        openKind.RemoveAt(openKind.Count - 1); depth--;
                        break;
                    case POp.Repeat:
                    case POp.Forever:
                        depth++; openKind.Add(b.op); loopHasBlocking.Add(false);
                        if (depth > MAX_DEPTH) return at + ": nesting deeper than " + MAX_DEPTH;
                        if (b.op == POp.Repeat && (b.arg < 1f || b.arg > MAX_REPEAT))
                            return at + ": REPEAT count must be 1–" + MAX_REPEAT;
                        break;
                    case POp.End:
                        if (openKind.Count == 0
                            || (openKind[openKind.Count - 1] != POp.Repeat && openKind[openKind.Count - 1] != POp.Forever))
                            return at + ": END without a REPEAT/FOREVER";
                        if (!loopHasBlocking[loopHasBlocking.Count - 1])
                            return at + ": a REPEAT/FOREVER needs a RUN, WAIT or FIRE inside (else it spins the clock)";
                        openKind.RemoveAt(openKind.Count - 1);
                        loopHasBlocking.RemoveAt(loopHasBlocking.Count - 1);
                        depth--;
                        break;
                    case POp.RunMotor:
                        if ((b.rounds > 0) == (b.dur > 0f))
                            return at + ": RUN needs exactly one of seconds or rounds";
                        if (b.rounds > 0 && b.rounds > MAX_ROUNDS)
                            return at + ": RUN rounds must be 1–" + MAX_ROUNDS;
                        if (b.dur > 0f && (b.dur < MIN_DUR || b.dur > MAX_DUR))
                            return at + ": RUN seconds must be " + MIN_DUR + "–" + MAX_DUR;
                        break;
                    case POp.Wait:
                        if (b.dur < MIN_DUR || b.dur > MAX_DUR)
                            return at + ": WAIT must be " + MIN_DUR + "–" + MAX_DUR + " s";
                        break;
                    case POp.Fire:
                        if (b.arg < 1f || b.arg > MAX_CYCLES)
                            return at + ": FIRE cycles must be 1–" + MAX_CYCLES;
                        break;

                    // ---- V2.2 macro verbs ---------------------------------
                    case POp.Move:
                    case POp.MoveRel:
                        if (b.rounds > 0 && b.dur > 0f)
                            return at + ": MOVE takes seconds OR rounds, not both";
                        if (b.rounds > 0 && b.rounds > MAX_ROUNDS)
                            return at + ": MOVE rounds must be 1–" + MAX_ROUNDS;
                        if (b.dur > 0f && (b.dur < MIN_DUR || b.dur > MAX_DUR))
                            return at + ": MOVE seconds must be " + MIN_DUR + "–" + MAX_DUR;
                        if (Mathf.Abs(b.arg) < 1f || Mathf.Abs(b.arg) > 100f)
                            return at + ": MOVE power is 1–100%";
                        break;
                    case POp.TurnLR:
                        if (b.dur > 0f && (b.dur < MIN_DUR || b.dur > MAX_DUR))
                            return at + ": TURN seconds must be " + MIN_DUR + "–" + MAX_DUR;
                        if (Mathf.Abs(b.arg) < 1f || Mathf.Abs(b.arg) > 100f)
                            return at + ": TURN power is 1–100%";
                        break;
                    case POp.TurnBy:
                        if (Mathf.Abs(b.arg) < 1f || Mathf.Abs(b.arg) > 180f)
                            return at + ": TURN BY needs 1–180 degrees either way";
                        break;
                    case POp.FaceSide:
                        if (b.idx < 0 || b.idx > 3)
                            return at + ": SIDE TO needs a side (front/right/back/left)";
                        break;
                }
                if (b.Blocking)
                    for (int li = 0; li < loopHasBlocking.Count; li++) loopHasBlocking[li] = true;

                // motor target existence
                if (b.op == POp.SetMotor || b.op == POp.RunMotor)
                {
                    if (IsWheelTarget(b.part))
                    {
                        if (wheels == 0) return at + ": motor steps need wheels on the build";
                        if (b.part == PPart.Wheel && (b.idx < 0 || b.idx >= wheels))
                            return at + ": W" + (b.idx + 1) + " doesn't exist (build has " + wheels + " wheels)";
                        if (Mathf.Abs(b.arg) > 100f) return at + ": motor power is −100…100%";
                    }
                    else
                    {
                        if (acts == 0) return at + ": weapon steps need a weapon on the build";
                        if (b.part == PPart.Actuator && (b.idx < 0 || b.idx >= acts))
                            return at + ": weapon " + (b.idx + 1) + " doesn't exist (build has " + acts + ")";
                    }
                }
                if (b.op == POp.Fire)
                {
                    if (acts == 0) return at + ": FIRE needs a weapon on the build";
                    if (b.part == PPart.Actuator && (b.idx < 0 || b.idx >= acts))
                        return at + ": weapon " + (b.idx + 1) + " doesn't exist (build has " + acts + ")";
                }
                if (b.op == POp.TurnToward && !have.Contains("compass"))
                    return at + ": TURN TOWARD needs a Compass tracker — SHOP";

                // V2.2: macro hardware gates. Wheel verbs need wheels; the
                // weapon verb needs a weapon; a relative verb needs ITS
                // sensor — same amber shape as the P4 autonomy gate, so a
                // program that references a since-removed sensor fails with
                // the part named and the SHOP pointed at.
                if ((b.op == POp.Move || b.op == POp.TurnLR || b.op == POp.TurnBy
                     || b.op == POp.MoveRel || b.op == POp.FaceSide) && wheels == 0)
                    return at + ": drive verbs need wheels on the build";
                if (b.op == POp.Weapon && acts == 0)
                    return at + ": WEAPON needs a weapon on the build";
                if (b.op == POp.MoveRel || b.op == POp.FaceSide)
                {
                    string needT = SensorIdOfTarget(b.target);
                    if (!have.Contains(needT))
                        return at + ": " + TargetLabel(b.target) + " verbs need a "
                             + SensorLabel(needT) + " — SHOP";
                }
            }
            if (openKind.Count > 0)
                return at + ": " + (openKind[openKind.Count - 1] == POp.If ? "IF without END IF"
                                                                            : "REPEAT/FOREVER without END");
        }
        return null;
    }

    // =======================================================================
    // THE CONFLICT LINT (V2 design §1.4). Exact where it can be, conservative
    // where timing blurs it, and NEVER a refusal.
    // =======================================================================

    /// <summary>Sensor-value interval a hat's terms allow for one kind.</summary>
    struct KindInterval { public float lo, hi; public bool bounded; }

    static Dictionary<PCond, KindInterval> Intervals(PHat h)
    {
        var m = new Dictionary<PCond, KindInterval>();
        foreach (var t in h.when)
        {
            if (t.kind == PCond.Always) continue;
            KindInterval iv;
            if (!m.TryGetValue(t.kind, out iv))
                iv = new KindInterval { lo = float.NegativeInfinity, hi = float.PositiveInfinity, bounded = true };
            if (t.cmp == PCmp.Less) iv.hi = Mathf.Min(iv.hi, t.value);
            else iv.lo = Mathf.Max(iv.lo, t.value);
            m[t.kind] = iv;
        }
        return m;
    }

    /// <summary>Exact: can hats a and b be true at the same instant? False
    /// only when some sensor kind is bounded by BOTH with disjoint intervals.</summary>
    static bool CanCoFire(Dictionary<PCond, KindInterval> a, Dictionary<PCond, KindInterval> b)
    {
        foreach (var kv in a)
        {
            KindInterval other;
            if (!b.TryGetValue(kv.Key, out other)) continue;
            float lo = Mathf.Max(kv.Value.lo, other.lo);
            float hi = Mathf.Min(kv.Value.hi, other.hi);
            if (lo >= hi) return false;
        }
        return true;
    }

    /// <summary>Motor write tokens for a body (static union over all steps,
    /// loops included). Wheel-family overlap rule: everything overlaps except
    /// LEFT vs RIGHT and W i vs W j (i≠j) — an indexed wheel's side is
    /// geometry the lint can't see, so it stays conservative.</summary>
    static List<PBlock> Writes(PHat h)
    {
        var w = new List<PBlock>();
        foreach (var b in h.body) if (b.MotorWrite) w.Add(b);
        return w;
    }

    /// <summary>V2.2: macros classify by FAMILY, not by the (defaulted) part
    /// field — every drive macro writes the whole wheel set, WEAPON writes
    /// every actuator. The lint therefore stays exact on compiled write-sets
    /// without ever seeing the compilation.</summary>
    static bool IsDriveMacro(POp o)
    { return o == POp.Move || o == POp.TurnLR || o == POp.TurnBy
          || o == POp.MoveRel || o == POp.FaceSide; }

    static bool WheelWrite(PBlock b)
    { return b.op == POp.StopAll || b.op == POp.TurnToward || IsDriveMacro(b.op)
          || ((b.op == POp.SetMotor || b.op == POp.RunMotor) && IsWheelTarget(b.part)); }

    static bool ActWrite(PBlock b)
    { return b.op == POp.StopAll || b.op == POp.Fire || b.op == POp.Weapon
          || ((b.op == POp.SetMotor || b.op == POp.RunMotor) && !IsWheelTarget(b.part)); }

    static bool TokensOverlap(PBlock x, PBlock y)
    {
        bool xw = WheelWrite(x), yw = WheelWrite(y);
        bool xa = ActWrite(x), ya = ActWrite(y);
        // wheel-family collision
        if (xw && yw)
        {
            bool xWhole = x.op != POp.SetMotor && x.op != POp.RunMotor;
            bool yWhole = y.op != POp.SetMotor && y.op != POp.RunMotor;
            PPart px = xWhole ? PPart.AllWheels : x.part;
            PPart py = yWhole ? PPart.AllWheels : y.part;
            if (px == PPart.LeftWheels && py == PPart.RightWheels) { } // disjoint
            else if (px == PPart.RightWheels && py == PPart.LeftWheels) { }
            else if (px == PPart.Wheel && py == PPart.Wheel && x.idx != y.idx) { }
            else return true;
        }
        // actuator-family collision
        if (xa && ya)
        {
            bool xWhole = x.op == POp.StopAll || x.op == POp.Weapon;
            bool yWhole = y.op == POp.StopAll || y.op == POp.Weapon;
            PPart px = xWhole ? PPart.AllActuators : x.part;
            PPart py = yWhole ? PPart.AllActuators : y.part;
            if (px == PPart.Actuator && py == PPart.Actuator && x.idx != y.idx) { }
            else return true;
        }
        return false;
    }

    static string TokenName(PBlock b)
    {
        if (b.op == POp.StopAll) return "all motors";
        if (b.op == POp.TurnToward || IsDriveMacro(b.op)) return "the wheels";
        if (b.op == POp.Weapon) return "the weapons";
        switch (b.part)
        {
            case PPart.AllWheels: return "the wheels";
            case PPart.LeftWheels: return "the left wheels";
            case PPart.RightWheels: return "the right wheels";
            case PPart.Wheel: return "W" + (b.idx + 1);
            case PPart.AllActuators: return "the weapons";
            default: return "weapon " + (b.idx + 1);
        }
    }

    /// <summary>The save-time lint. One line per finding: dead hats (self-
    /// contradictory WHEN) and may-conflict pairs, each naming the winner.</summary>
    public List<string> Lint()
    {
        var outp = new List<string>();
        var ivs = new List<Dictionary<PCond, KindInterval>>();
        for (int i = 0; i < hats.Count; i++)
        {
            var iv = Intervals(hats[i]);
            ivs.Add(iv);
            foreach (var kv in iv)
                if (kv.Value.lo >= kv.Value.hi)
                { outp.Add("hat " + (i + 1) + " can never trigger (its own WHEN contradicts itself)"); break; }
        }
        for (int a = 0; a < hats.Count; a++)
            for (int b = a + 1; b < hats.Count; b++)
            {
                if (!CanCoFire(ivs[a], ivs[b])) continue;
                string tok = null;
                foreach (var wx in Writes(hats[a]))
                {
                    foreach (var wy in Writes(hats[b]))
                        if (TokensOverlap(wx, wy)) { tok = TokenName(wx); break; }
                    if (tok != null) break;
                }
                if (tok != null)
                    outp.Add("hats " + (a + 1) + " and " + (b + 1) + " can both trigger and both drive "
                             + tok + " — " + (a + 1) + " wins (higher); reorder if you meant otherwise");
            }
        return outp;
    }

    // =======================================================================
    // Presets, rebuilt in v2 vocabulary (§1.5). Still the tutorial on-ramp.
    // =======================================================================

    /// <summary>V2.4: the three tutorial presets merged into ONE on-ramp —
    /// four hats, one lesson each, priority top-down (Brawler was a subset
    /// of Wall-shy; Matador's STRIKE/STALK duplicated Brawler's pair, so the
    /// union dedups to a clean ladder). The single STARTER KIT button on the
    /// canvas installs this; Brawler/WallShy/Matador stay for benches and
    /// the balance matrix. Needs compass + wall sensor + dmgbus + weapon +
    /// wheels; Validate ambers name whatever is missing.</summary>
    public static RobotProgram StarterKit()
    {
        var p = new RobotProgram { title = "Starter Kit" };
        var shy = new PHat { name = "WALL!",
            note = "1. SAFETY - the top hat always wins. Near a wall? Back away from it before doing anything else." };
        shy.when.Add(PCondTerm.Mk(PCond.EdgeDist, PCmp.Less, 1.2f));
        shy.body.Add(PBlock.MkMoveRel(PTarget.Wall, -80f, 0.8f, 0));
        p.hats.Add(shy);
        var dodge = new PHat { name = "TOO HOT",
            note = "2. DODGE - just got hit up close while still healthy? Back off and swerve instead of trading." };
        dodge.when.Add(PCondTerm.Mk(PCond.EnemyRange, PCmp.Less, 3.0f));
        dodge.when.Add(PCondTerm.Mk(PCond.HpFrac, PCmp.Greater, 0.35f));
        dodge.when.Add(PCondTerm.Mk(PCond.HitRecently, PCmp.Greater, 0.5f));
        dodge.body.Add(PBlock.MkMoveRel(PTarget.Enemy, -90f, 0.8f, 0));
        dodge.body.Add(PBlock.MkTurnLR(30f, 0.4f));
        p.hats.Add(dodge);
        var strike = new PHat { name = "STRIKE",
            note = "3. ATTACK - enemy in range and no hat above fired? Weapon ON and charge." };
        strike.when.Add(PCondTerm.Mk(PCond.EnemyRange, PCmp.Less, 2.2f));
        strike.body.Add(PBlock.MkWeapon(true));
        strike.body.Add(PBlock.MkMoveRel(PTarget.Enemy, 100f, 1.5f, 0));
        p.hats.Add(strike);
        var seek = new PHat { name = "SEEK",
            note = "4. CHASE - when nothing else applies, steer toward the enemy and keep checking." };
        seek.when.Add(PCondTerm.Always());
        seek.body.Add(PBlock.MkMoveRel(PTarget.Enemy, 70f, 0f, 0));
        seek.body.Add(PBlock.MkForever());
        seek.body.Add(PBlock.MkWait(0.2f));
        seek.body.Add(PBlock.MkEnd());
        p.hats.Add(seek);
        return p;
    }

    /// <summary>Spin up, chase, charge when close; stalk otherwise. Needs a
    /// compass + a weapon + wheels. (V2.2: macro vocabulary — the presets
    /// are the tutorial, so they speak the palette the player sees.)</summary>
    public static RobotProgram Brawler()
    {
        var p = new RobotProgram { title = "Brawler" };
        var close = new PHat { name = "IN RANGE" };
        close.when.Add(PCondTerm.Mk(PCond.EnemyRange, PCmp.Less, 2.2f));
        close.body.Add(PBlock.MkWeapon(true));
        close.body.Add(PBlock.MkMoveRel(PTarget.Enemy, 100f, 1.0f, 0));
        p.hats.Add(close);
        var seek = new PHat { name = "SEEK" };
        seek.when.Add(PCondTerm.Always());
        seek.body.Add(PBlock.MkMoveRel(PTarget.Enemy, 70f, 0f, 0));   // SET: chase, steering live
        seek.body.Add(PBlock.MkForever());
        seek.body.Add(PBlock.MkWait(0.2f));
        seek.body.Add(PBlock.MkEnd());
        p.hats.Add(seek);
        return p;
    }

    /// <summary>Brawler plus a wall-retreat hat ON TOP — the priority lesson.
    /// Needs compass + WALL sensor (+ weapon, wheels).</summary>
    public static RobotProgram WallShy()
    {
        var p = Brawler(); p.title = "Wall-shy Brawler";
        var shy = new PHat { name = "WALL!" };
        shy.when.Add(PCondTerm.Mk(PCond.EdgeDist, PCmp.Less, 1.2f));
        shy.body.Add(PBlock.MkMoveRel(PTarget.Wall, -80f, 0.8f, 0));
        p.hats.Insert(0, shy);
        return p;
    }

    /// <summary>Back off while the enemy is hot, commit after — the
    /// widowmaker lesson as content. Needs compass + damage bus.</summary>
    public static RobotProgram Matador()
    {
        var p = new RobotProgram { title = "Matador" };
        var dodge = new PHat { name = "TOO HOT" };
        dodge.when.Add(PCondTerm.Mk(PCond.EnemyRange, PCmp.Less, 3.0f));
        dodge.when.Add(PCondTerm.Mk(PCond.HpFrac, PCmp.Greater, 0.35f));
        dodge.when.Add(PCondTerm.Mk(PCond.HitRecently, PCmp.Greater, 0.5f));
        dodge.body.Add(PBlock.MkMoveRel(PTarget.Enemy, -90f, 0.8f, 0));
        dodge.body.Add(PBlock.MkTurnLR(30f, 0.4f));
        p.hats.Add(dodge);
        var strike = new PHat { name = "STRIKE" };
        strike.when.Add(PCondTerm.Mk(PCond.EnemyRange, PCmp.Less, 2.2f));
        strike.body.Add(PBlock.MkWeapon(true));
        strike.body.Add(PBlock.MkMoveRel(PTarget.Enemy, 100f, 1.5f, 0));
        p.hats.Add(strike);
        var stalk = new PHat { name = "STALK" };
        stalk.when.Add(PCondTerm.Always());
        stalk.body.Add(PBlock.MkMoveRel(PTarget.Enemy, 55f, 0f, 0));
        stalk.body.Add(PBlock.MkForever());
        stalk.body.Add(PBlock.MkWait(0.2f));
        stalk.body.Add(PBlock.MkEnd());
        p.hats.Add(stalk);
        return p;
    }

    /// <summary>Sensor-free: straight ahead, weapon hot. Legal content and
    /// the mirror fixture.</summary>
    public static RobotProgram Rusher()
    {
        var p = new RobotProgram { title = "Rusher" };
        var go = new PHat { name = "GO" };
        go.when.Add(PCondTerm.Always());
        go.body.Add(PBlock.MkWeapon(true));
        go.body.Add(PBlock.MkMove(100f, 0f, 0));   // SET: full ahead, forever
        go.body.Add(PBlock.MkForever());
        go.body.Add(PBlock.MkWait(0.2f));
        go.body.Add(PBlock.MkEnd());
        p.hats.Add(go);
        return p;
    }

    /// <summary>Parked forever — the determinism fixture's target.</summary>
    public static RobotProgram Statue()
    {
        var p = new RobotProgram { title = "Statue" };
        var sit = new PHat { name = "SIT" };
        sit.when.Add(PCondTerm.Always());
        sit.body.Add(PBlock.MkForever());
        sit.body.Add(PBlock.MkStop());
        sit.body.Add(PBlock.MkWait(0.5f));
        sit.body.Add(PBlock.MkEnd());
        p.hats.Add(sit);
        return p;
    }
}
}
