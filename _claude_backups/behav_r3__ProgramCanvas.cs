using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RobotBrawl.Phase0
{
/// <summary>P3b — the drag handle. A ≡ chip on every hat card and body row
/// (and on the + HAT / + BLOCK palette chips) that owns the uGUI drag
/// events, so a drag STARTED ON A HANDLE moves program structure while a
/// drag anywhere else still scrolls the list — no long-press, no gesture
/// ambiguity, and the ScrollRect never fights the canvas. All logic lives
/// in ProgramCanvas; this forwards.</summary>
public class ProgramDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public const int HAT = 0, BLOCK = 1, NEW_HAT = 2, NEW_BLOCK = 3;
    public ProgramCanvas owner;
    public int kind;
    public int hat, block;
    public void OnBeginDrag(PointerEventData e) { if (owner != null) owner.DragBegin(this, e); }
    public void OnDrag(PointerEventData e) { if (owner != null) owner.DragMove(e); }
    public void OnEndDrag(PointerEventData e) { if (owner != null) owner.DragEnd(e); }
}

/// <summary>Layout pass P1 (2026-08-06) — a WRAPPING chip row.
/// HorizontalLayoutGroup never wraps, so a 3-term WHEN row inflated its
/// card's minWidth to ~2.5× the viewport at iPhone width and everything
/// right of the screen edge clipped (captions, ENEMY chips — the smoke-
/// pass finding). ChipFlow lays chips left→right and starts a new line
/// when the next chip would cross the row's width; preferred height
/// reports the lines actually used, so rows grow instead of overflowing
/// and card minWidth collapses to the widest single chip.</summary>
public class ChipFlow : LayoutGroup
{
    public float spacingX = 4f, spacingY = 4f;

    public override void CalculateLayoutInputHorizontal()
    {
        base.CalculateLayoutInputHorizontal();
        float minW = 0f;
        foreach (var c in rectChildren)
            minW = Mathf.Max(minW, LayoutUtility.GetMinSize(c, 0));
        minW += padding.horizontal;
        SetLayoutInputForAxis(minW, minW, -1f, 0);
    }
    public override void CalculateLayoutInputVertical()
    {
        float h = Arrange(false);
        SetLayoutInputForAxis(h, h, -1f, 1);
    }
    public override void SetLayoutHorizontal() { Arrange(true); }
    public override void SetLayoutVertical() { Arrange(true); }

    float Arrange(bool apply)
    {
        float availW = rectTransform.rect.width - padding.horizontal;
        if (availW <= 0f) availW = float.MaxValue;   // pre-layout pass safety
        var xs = new List<float>(); var lineOf = new List<int>();
        var ws = new List<float>(); var hs = new List<float>();
        var lineH = new List<float>();
        float x = 0f; int line = 0;
        for (int i = 0; i < rectChildren.Count; i++)
        {
            var c = rectChildren[i];
            float cw = LayoutUtility.GetPreferredSize(c, 0);
            float ch = LayoutUtility.GetPreferredSize(c, 1);
            if (x > 0f && x + cw > availW) { x = 0f; line++; }
            if (lineH.Count <= line) lineH.Add(0f);
            lineH[line] = Mathf.Max(lineH[line], ch);
            xs.Add(x); lineOf.Add(line); ws.Add(cw); hs.Add(ch);
            x += cw + spacingX;
        }
        var ly = new List<float>(); float acc = 0f;
        for (int l = 0; l < lineH.Count; l++)
        { ly.Add(acc); acc += lineH[l] + (l < lineH.Count - 1 ? spacingY : 0f); }
        if (apply)
            for (int i = 0; i < rectChildren.Count; i++)
            {
                var c = rectChildren[i];
                float yOff = ly[lineOf[i]] + (lineH[lineOf[i]] - hs[i]) * 0.5f;
                SetChildAlongAxis(c, 0, padding.left + xs[i], ws[i]);
                SetChildAlongAxis(c, 1, padding.top + yOff, hs[i]);
            }
        return acc + padding.vertical;
    }
}

/// <summary>P3a — the Program Bench canvas, tap-authorable scaffolding
/// (design doc §6, §9 P3a). Career-only PROGRAM dock tab. Every edit is a
/// tap: chips cycle values, ▲▼ reorder hats, ✕ deletes.
///
/// P3b — drag-and-drop on top of the tap canvas (design §9 P3b): every hat
/// card and body row carries a ≡ handle; dragging it lifts a GHOST of the
/// item, a cyan DROP LINE opens the gap it would land in (physical-inch
/// slots — the rows it lands between are already TouchRow-sized), dropping
/// between an IF and its END IF nests (the flat-marker encoding makes
/// nesting a POSITION, not a mode), dragging an IF moves its whole
/// IF..END IF span, and dragging OFF the list deletes (span-deletes an
/// IF). + HAT / + BLOCK are draggable palette sources: drop them where the
/// new item should go (tap still appends, as in 3a). The ▲▼ fallback stays
/// forever — accessibility + the drag-risk hedge. Dragging near the list's
/// top/bottom edge auto-scrolls.
///
/// Widget rules inherited from the 2026-08-04 mobile lessons: physical-inch
/// sizing via the TouchRow/FontUnits formulas (UnityEngine.Device.Screen),
/// zeroed sizeDelta on every hand-built scroll content, no free-text
/// anywhere. The program being edited belongs to the ACTIVE STABLE ROBOT
/// (Career.Data.activeRobot) — programming is building: the program rides
/// with the robot, saved into CareerRobot.program as RobotProgram JSON.</summary>
public class ProgramCanvas : MonoBehaviour
{
    BuilderManager bm;
    Canvas canvas;
    RobotProgram prog = new RobotProgram();
    bool dirty;

    Text header, status, lockedHint;
    Transform content;
    ScrollRect scroll;
    RectTransform viewportRT;

    // ---- P3b drag state ---------------------------------------------------
    ProgramDragHandle drag;          // null = no drag in flight
    GameObject ghost, dropLine;
    Image ghostImg;
    bool dropDelete;
    int dropHat = -1, dropIdx = -1;  // resolved target: hat insert index, or (hat, body index)

    // harness seams (the TouchSmoke Test* precedent — no reflection)
    public RobotProgram TestProg { get { return prog; } }
    public bool TestDragActive { get { return drag != null; } }
    public bool TestDropDelete { get { return dropDelete; } }
    public GameObject TestDropLine { get { return dropLine; } }
    public string TestStatus { get { return status != null ? status.text : ""; } }
    public void TestDirtyRefresh() { dirty = true; Refresh(); }
    public void TestInstall(RobotProgram p) { InstallPreset(p); }   // bench seam (presets now install via the one STARTER KIT button)

    // ---- V2.2 step highlight ---------------------------------------------
    // The sequencer's program counter (ProgramRunner.activeStep, the shipped
    // V2.1 hook) carried into the canvas: while a live runner drives, the
    // firing hat's card and its active step row tint green. Reads
    // bm.testRunner; TestHighlightRunner is the bench's injection seam.
    public ProgramRunner TestHighlightRunner;
    public int TestHiHat = -1, TestHiStep = -1;
    int hiHat = -1, hiStep = -1;

    ProgramRunner LiveRunner()
    {
        if (TestHighlightRunner != null) return TestHighlightRunner;
        return bm != null ? bm.testRunner : null;
    }

    void Update()
    {
        // Scale-settle guard (08-06): BuildUI can run on the respawn frame
        // BEFORE the CanvasScaler settles for a new device/window, baking the
        // chrome (header/status/action row) at the wrong TouchRow/FontUnits —
        // measured 2× too big after an iPad switch while the LIST, rebuilt
        // later by Refresh, was right. MobileBuilderUI re-applies fonts on
        // scale change (the C19 pass); the canvas's answer is a full rebuild
        // at the settled scale. Skipped mid-drag.
        if (canvas != null && builtSf > 0f && !TestDragActive
            && Mathf.Abs(canvas.scaleFactor - builtSf) > builtSf * 0.02f)
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                Destroy(transform.GetChild(i).gameObject);
            saveAsDlg = null; openDlg = null; openListContent = null;
            saveAsInput = null; saveAsErr = null; progDropLabel = null;
            hiHat = hiStep = -1;
            BuildUI();
            Refresh();
            return;
        }
        var r = LiveRunner();
        int h = r != null ? r.lastFiredHat : -1;
        int s = r != null ? r.activeStep : -1;
        if (h == hiHat && s == hiStep) return;
        SetRowTint(hiHat, hiStep, false);
        hiHat = h; hiStep = s;
        TestHiHat = h; TestHiStep = s;
        SetRowTint(hiHat, hiStep, true);
    }

    void SetRowTint(int hat, int step, bool on)
    {
        if (hat < 0 || content == null) return;
        Transform card = null;
        for (int i = 0; i < content.childCount; i++)
            if (content.GetChild(i).name == "hat_" + hat) { card = content.GetChild(i); break; }
        if (card == null) return;
        var img = card.GetComponent<Image>();
        if (img != null) img.color = on ? new Color(0.10f, 0.24f, 0.16f, 0.97f)
                                        : new Color(0.12f, 0.14f, 0.19f, 0.95f);
        if (step < 0) return;
        var row = card.Find("brow_" + step);
        if (row == null) return;
        var rimg = row.GetComponent<Image>();
        if (rimg != null) rimg.color = on ? new Color(0.16f, 0.45f, 0.28f, 0.55f)
                                          : new Color(0f, 0f, 0f, 0f);
    }

    // V2.2 — the MACRO PALETTE (owen's pivot; V2 design doc §5). The player
    // authors in high-level verbs; the per-motor ops stay runtime-only and
    // are CUT from this list (confirmed 2026-08-06, ADVANCED toggle
    // declined). A program that still carries internal ops (old harness
    // data) renders read-compatibly, but the op chip cycles it into the
    // macro vocabulary on the first tap.
    static readonly POp[] PLAYER_OPS = {
        POp.Move, POp.TurnLR, POp.TurnBy, POp.Weapon, POp.MoveRel, POp.FaceSide,
        POp.Wait, POp.StopAll };

    // V2.7 - STRUCTURE LEFT THE CHIP CYCLE. IF/ELSE/END IF/REPEAT/FOREVER/END
    // used to sit in this list, so one stray tap on a working block turned it
    // into an unmatched marker (Validate: IF without END IF) and walking back
    // cost six more taps. Structure now arrives BALANCED from the + IF and
    // + REPEAT palette chips, and CycleOp refuses to convert between
    // structure and action in either direction.
    static bool IsStructural(POp o)
    {
        return o == POp.If || o == POp.Else || o == POp.EndIf
            || o == POp.Repeat || o == POp.Forever || o == POp.End;
    }

    // per-kind value chips cycle through these (no free text, §6)
    static readonly float[] V_RANGE = { 0.5f, 1f, 1.5f, 2f, 2.2f, 3f, 4f, 6f, 8f, 12f };
    static readonly float[] V_POWER = { 25f, 40f, 55f, 70f, 85f, 100f };
    static readonly float[] V_TURNDEG = { 15f, 30f, 45f, 60f, 90f, 120f, 180f };
    static readonly float[] V_DEG = { 5f, 10f, 20f, 30f, 45f, 90f, 135f };
    static readonly float[] V_FRAC = { 0.1f, 0.2f, 0.35f, 0.5f, 0.65f, 0.8f, 0.9f };
    static readonly float[] V_UPY = { 0.2f, 0.45f, 0.75f, 0.9f };
    static readonly float[] V_BOOL = { 0.5f };
    static readonly float[] V_COUNT = { 1f, 2f, 3f, 5f };
    static readonly float[] V_PCT = { 0f, 25f, 40f, 55f, 70f, 85f, 100f };
    static readonly float[] V_SIGNED = { -100f, -50f, -25f, 25f, 50f, 100f };
    static readonly float[] V_DUR = { 0.2f, 0.5f, 1f, 1.5f, 2f, 3f, 5f };
    static readonly float[] V_REPEAT = { 2f, 3f, 4f, 5f, 10f };
    static readonly float[] V_CYCLES = { 1f, 2f, 3f };

    // V2: the part chip cycles through concrete motor targets.
    struct PartOpt { public PPart part; public int idx; public string label; }
    static readonly PartOpt[] PART_OPTS = {
        new PartOpt { part = PPart.AllWheels, idx = 0, label = "ALL WHEELS" },
        new PartOpt { part = PPart.LeftWheels, idx = 0, label = "LEFT WHEELS" },
        new PartOpt { part = PPart.RightWheels, idx = 0, label = "RIGHT WHEELS" },
        new PartOpt { part = PPart.Wheel, idx = 0, label = "W1" },
        new PartOpt { part = PPart.Wheel, idx = 1, label = "W2" },
        new PartOpt { part = PPart.Wheel, idx = 2, label = "W3" },
        new PartOpt { part = PPart.Wheel, idx = 3, label = "W4" },
        new PartOpt { part = PPart.AllActuators, idx = 0, label = "ALL WEAPONS" },
        new PartOpt { part = PPart.Actuator, idx = 0, label = "WEAPON 1" },
        new PartOpt { part = PPart.Actuator, idx = 1, label = "WEAPON 2" },
    };
    static string PartLabel(PPart p, int idx)
    {
        foreach (var o in PART_OPTS) if (o.part == p && (p != PPart.Wheel && p != PPart.Actuator || o.idx == idx)) return o.label;
        return p == PPart.Wheel ? "W" + (idx + 1) : p == PPart.Actuator ? "WEAPON " + (idx + 1) : p.ToString();
    }

    public void Init(BuilderManager builder)
    {
        bm = builder;
        canvas = GetComponentInParent<Canvas>();
        BuildUI();
        Refresh();
    }

    // ---- sizing (MobileBuilderUI formulas, replicated) --------------------
    float TouchRow()
    {
        float sf = canvas != null ? canvas.scaleFactor : 0f;
        float dpi = UnityEngine.Device.Screen.dpi;
        if (sf < 0.01f || dpi < 1f) return 44f;
        return Mathf.Clamp((44f / 163f) * dpi / sf, 34f, 110f);
    }
    int FontUnits(float pt)
    {
        float sf = canvas != null ? canvas.scaleFactor : 0f;
        float dpi = UnityEngine.Device.Screen.dpi;
        if (sf < 0.01f || dpi < 1f) return Mathf.RoundToInt(pt);
        return Mathf.Max(8, Mathf.RoundToInt(pt * (dpi / 163f) / sf));
    }

    // ---- tiny widget factory (MobileBuilderUI vocabulary) -----------------
    static Font fnt;
    static Font Fnt()
    {
        if (fnt == null) fnt = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return fnt;
    }
    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }
    GameObject MkPanel(string name, Transform parent, Color c)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>(); img.color = c; img.raycastTarget = c.a > 0.001f;
        return go;
    }
    Text MkText(string name, Transform parent, string s, float pt, TextAnchor anchor)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.font = Fnt(); t.text = s; t.fontSize = FontUnits(pt); t.alignment = anchor;
        t.color = Color.white; t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Truncate;
        return t;
    }
    Button MkChip(string name, Transform parent, string label, float pt, Color bg,
                  UnityEngine.Events.UnityAction onTap, float minW = 0f)
    {
        var go = MkPanel(name, parent, bg);
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = TouchRow() * 0.82f; le.preferredHeight = TouchRow() * 0.82f;
        // First-look critic: fixed widths cramped long labels (WALL-SHY spilled
        // its chip). Width now follows the label at the ACTUAL FontUnits size,
        // floored by minW/TouchRow so short labels stay tappable squares.
        float byText = label.Length * FontUnits(pt) * 0.62f + FontUnits(pt) * 1.2f;
        le.minWidth = Mathf.Max(minW > 0f ? minW : TouchRow() * 0.9f, byText);
        le.flexibleWidth = 0f;
        var b = go.AddComponent<Button>();
        b.targetGraphic = go.GetComponent<Image>();
        b.onClick.AddListener(onTap);
        var t = MkText("lbl", go.transform, label, pt, TextAnchor.MiddleCenter);
        Stretch(t.rectTransform);
        return b;
    }
    /// <summary>V2.2 — the macro HOLD/seconds(/rounds) chip. HOLD = the SET
    /// flavor (takes effect, sequence continues); a duration or a rounds
    /// count = the timed RUN flavor (blocks, then stops what it drove).</summary>
    void AddHoldDurChip(Transform row, PBlock b, bool allowRounds)
    {
        string lbl = b.rounds > 0 ? b.rounds + " rnd"
                   : b.dur > 0f ? b.dur.ToString("0.#") + "s" : "HOLD";
        MkChip("bdur", row, lbl, 11f,
               new Color(0.24f, 0.2f, 0.28f, 0.9f), () =>
        {
            if (b.rounds > 0)
            {
                b.rounds = b.rounds == 2 ? 5 : b.rounds == 5 ? 10 : 0;
                if (b.rounds == 0) b.dur = 0f;                   // …back to HOLD
            }
            else if (b.dur <= 0f) b.dur = V_DUR[0];              // HOLD → first duration
            else
            {
                int i = System.Array.IndexOf(V_DUR, b.dur);
                if (i == V_DUR.Length - 1)
                { b.dur = 0f; if (allowRounds) b.rounds = 2; }   // → rounds (or HOLD)
                else b.dur = V_DUR[(i + 1 + V_DUR.Length) % V_DUR.Length];
            }
            MarkDirty();
        }, 62f);
    }

    GameObject MkRow(Transform parent, float indent = 0f)
    {
        var row = MkPanel("row", parent, new Color(0f, 0f, 0f, 0f));
        // Layout pass P1: rows WRAP (ChipFlow) instead of clipping off the
        // right edge; height floor keeps single-line rows at the touch size.
        var h = row.AddComponent<ChipFlow>();
        h.spacingX = 4f; h.spacingY = 4f;
        h.padding = new RectOffset(Mathf.RoundToInt(indent), 0, 0, 0);
        var le = row.AddComponent<LayoutElement>();
        le.minHeight = TouchRow() * 0.9f;   // no fixed preferredHeight - ChipFlow grows it
        return row;
    }

    // ---- labels -----------------------------------------------------------
    static string CondName(PCond k)
    {
        switch (k)
        {
            case PCond.Always: return "ALWAYS";
            case PCond.EnemyRange: return "ENEMY RANGE";
            case PCond.EnemyBearingAbs: return "|BEARING|";
            case PCond.RangeHit: return "RANGEFINDER";
            case PCond.RangeEnemy: return "RF: ENEMY";
            case PCond.TiltUpY: return "TILT upY";
            case PCond.Flipped: return "FLIPPED";
            case PCond.EdgeDist: return "WALL DIST";
            case PCond.HazardNear: return "TRAP NEAR";
            case PCond.TrapDist: return "TRAP DIST";
            case PCond.HpFrac: return "HP %";
            case PCond.PowerFrac: return "BATTERY %";
            case PCond.HitRecently: return "JUST HIT";
            case PCond.PartsLost: return "PARTS LOST";
            default: return k.ToString();
        }
    }
    static string OpName(POp o)
    {
        switch (o)
        {
            case POp.SetMotor: return "SET";
            case POp.RunMotor: return "RUN";
            case POp.Fire: return "FIRE";
            case POp.Wait: return "WAIT";
            case POp.StopAll: return "STOP ALL";
            case POp.TurnToward: return "TURN TOWARD";
            case POp.If: return "IF";
            case POp.Else: return "ELSE";
            case POp.EndIf: return "END IF";
            case POp.Repeat: return "REPEAT";
            case POp.Forever: return "FOREVER";
            case POp.End: return "END";
            case POp.Move: return "MOVE";
            case POp.TurnLR: return "TURN";
            case POp.TurnBy: return "TURN BY";
            case POp.Weapon: return "WEAPON";
            case POp.MoveRel: return "MOVE TO";
            case POp.FaceSide: return "SIDE TO";
            default: return o.ToString();
        }
    }

    // ---- V2.2 target availability (the sensor gate on the canvas) ---------
    static bool TargetAvailable(int t, List<string> ids)
    { return ids.Contains(RobotProgram.SensorIdOfTarget(t)); }
    static int FirstTarget(List<string> ids)
    { for (int t = 0; t < 3; t++) if (TargetAvailable(t, ids)) return t; return -1; }
    static int NextTarget(int cur, List<string> ids)
    {
        for (int step = 1; step <= 3; step++)
        { int cand = (cur + step) % 3; if (TargetAvailable(cand, ids)) return cand; }
        return cur;
    }

    /// <summary>The op chip's cycle: PLAYER_OPS only, relative verbs skipped
    /// while no directional sensor is mounted (an unmounted sensor's verbs
    /// don't appear — the §5 gate). Internal ops enter the cycle at MOVE.</summary>
    void CycleOp(PBlock b, List<string> ids)
    {
        // V2.7: structure never cycles into action - that orphans its partner
        // marker. REPEAT<->FOREVER is the one legal swap: both close on END,
        // so the span stays balanced through it.
        if (IsStructural(b.op))
        {
            if (b.op == POp.Repeat) { b.op = POp.Forever; b.arg = 0f; }
            else if (b.op == POp.Forever) { b.op = POp.Repeat; b.arg = 3f; }
            return;
        }
        int i = System.Array.IndexOf(PLAYER_OPS, b.op);   // internal ops: -1
        for (int step = 1; step <= PLAYER_OPS.Length; step++)
        {
            var cand = PLAYER_OPS[(i + step + PLAYER_OPS.Length) % PLAYER_OPS.Length];
            if ((cand == POp.MoveRel || cand == POp.FaceSide) && FirstTarget(ids) < 0)
                continue;
            b.op = cand;
            break;
        }
        // fresh defaults per verb — a cycled chip must land runnable
        b.part = PPart.AllWheels; b.rounds = 0;
        switch (b.op)
        {
            case POp.Move: b.arg = 70f; b.dur = 1f; break;
            case POp.TurnLR: b.arg = 50f; b.dur = 0.5f; break;
            case POp.TurnBy: b.arg = 90f; b.dur = 0f; break;
            case POp.Weapon: b.arg = 100f; b.dur = 0f; break;
            case POp.MoveRel: b.target = Mathf.Max(0, FirstTarget(ids)); b.arg = 70f; b.dur = 1f; break;
            case POp.FaceSide: b.target = Mathf.Max(0, FirstTarget(ids)); b.idx = 0; b.dur = 0f; break;
            case POp.Wait: b.dur = 0.5f; break;
            case POp.Repeat: b.arg = 3f; b.dur = 0f; break;
            case POp.If: if (b.cond == null) b.cond = PCondTerm.Mk(PCond.Always, PCmp.Greater, 0f); break;
            default: b.dur = 0f; break;
        }
    }
    static float[] ValuesOf(PCond k)
    {
        switch (k)
        {
            case PCond.EnemyRange: case PCond.RangeHit: case PCond.RangeEnemy:
            case PCond.EdgeDist: case PCond.TrapDist: return V_RANGE;
            case PCond.EnemyBearingAbs: return V_DEG;
            case PCond.TiltUpY: return V_UPY;
            case PCond.HpFrac: case PCond.PowerFrac: return V_FRAC;
            case PCond.PartsLost: return V_COUNT;
            default: return V_BOOL;   // Flipped/HazardNear/JustHit/Always
        }
    }
    static float[] ValuesOfOp(POp o)
    {
        switch (o)
        {
            case POp.SetMotor: case POp.RunMotor: return V_SIGNED;
            case POp.TurnToward: return V_PCT;
            case POp.Repeat: return V_REPEAT;
            case POp.Fire: return V_CYCLES;
            default: return null;   // Wait uses the dur chip; markers/STOP have no arg
        }
    }
    static bool HasPartChip(POp o) { return o == POp.SetMotor || o == POp.RunMotor || o == POp.Fire; }
    static bool HasDurChip(POp o) { return o == POp.RunMotor || o == POp.Wait; }

    // ---- build availability ----------------------------------------------
    /// <summary>V2.8b (critic loop 7 R2, finding 1) — what the player OWNS
    /// but has not bolted on. BuildIds() is the BAY; this is the CRATE. A
    /// refusal that cannot tell them apart sends a fresh career shopping for
    /// the four wheels it was already granted.</summary>
    List<string> OwnedIds() { return Career.OwnedPartIds(); }   // R3: one source, in Career

    List<string> BuildIds()
    {
        var ids = new List<string>();
        if (bm != null && bm.placed != null)
            foreach (var p in bm.placed) ids.Add(p.def.id);
        return ids;
    }
    bool CondAvailable(PCond k, List<string> ids)
    {
        string need = RobotProgram.SensorIdOf(k);
        return need == null || ids.Contains(need);
    }
    PCond NextCond(PCond k, List<string> ids)
    {
        var all = (PCond[])System.Enum.GetValues(typeof(PCond));
        int i = System.Array.IndexOf(all, k);
        for (int step = 1; step <= all.Length; step++)
        {
            var cand = all[(i + step) % all.Length];
            if (CondAvailable(cand, ids)) return cand;
        }
        return PCond.Always;
    }

    // ---- UI skeleton ------------------------------------------------------
    void BuildUI()
    {
        // Re-entrant for the scale-settle rebuild: LayoutGroup is
        // [DisallowMultipleComponent], so a second AddComponent returns null.
        var v = gameObject.GetComponent<VerticalLayoutGroup>() ?? gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = 4f; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
        v.padding = new RectOffset(4, 4, 4, 4);

        // First-look critic: fixed 20/18-unit heights TRUNCATED FontUnits-scaled
        // text to nothing on high-DPI screens (the sizeDelta lesson's cousin:
        // hand-set metrics must scale with the text they hold).
        header = MkText("pheader", transform, "PROGRAM", 15f, TextAnchor.MiddleLeft);
        var hle = header.gameObject.AddComponent<LayoutElement>();
        hle.minHeight = FontUnits(15f) * 1.5f; hle.preferredHeight = FontUnits(15f) * 1.5f;

        status = MkText("pstatus", transform, "", 12f, TextAnchor.MiddleLeft);
        var sle = status.gameObject.AddComponent<LayoutElement>();
        sle.minHeight = FontUnits(12f) * 1.5f; sle.preferredHeight = FontUnits(12f) * 1.5f;

        // presets + actions row
        var actions = MkRow(transform);
        // V2.6 (owen): ONE row — a program DROPDOWN (label = the open
        // program; tap = the switch list: STARTER KIT preset + the library)
        // plus SAVE / SAVE AS / LOAD TO ROBOT. The bottom document bar is
        // GONE — the reclaimed height goes to the code. The row is a
        // ChipFlow, so it wraps at narrow widths instead of clipping.
        var dd = MkChip("prog_dropdown", actions.transform, "STARTER KIT  ▾", 11f,
               new Color(0.15f, 0.22f, 0.30f, 0.95f), OpenPicker, 150f);
        progDropLabel = dd.GetComponentInChildren<Text>();
        MkChip("prog_save", actions.transform, "SAVE", 12f,
               new Color(0.16f, 0.34f, 0.18f, 0.95f), OnSaveTapped, 84f);
        MkChip("prog_saveas", actions.transform, "SAVE AS", 12f,
               new Color(0.15f, 0.22f, 0.30f, 0.95f), OpenSaveAs, 100f);
        MkChip("prog_load", actions.transform, "LOAD TO ROBOT", 12f,
               new Color(0.36f, 0.27f, 0.10f, 0.95f), SaveProgram, 150f);
        // V2.5: SAVE moved to the bottom document bar (owen) — top row is presets only.

        // scroll (the SHOP pattern; sizeDelta ZERO — the R1 lesson by name)
        var scrollGO = MkPanel("progscroll", transform, new Color(0f, 0f, 0f, 0f));
        var scle = scrollGO.AddComponent<LayoutElement>();
        scle.flexibleHeight = 1f; scle.minHeight = 96f;
        scroll = scrollGO.AddComponent<ScrollRect>();
        scroll.horizontal = false; scroll.vertical = true;
        var viewport = MkPanel("progviewport", scrollGO.transform, new Color(0f, 0f, 0f, 0.15f));
        var vp = viewport.GetComponent<RectTransform>(); Stretch(vp);
        viewportRT = vp;
        viewport.AddComponent<Mask>().showMaskGraphic = true;
        var contentGO = MkPanel("progcontent", viewport.transform, new Color(0f, 0f, 0f, 0f));
        var crt = contentGO.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0f, 1f); crt.anchorMax = new Vector2(1f, 1f);
        crt.pivot = new Vector2(0.5f, 1f); crt.anchoredPosition = Vector2.zero;
        crt.sizeDelta = Vector2.zero;
        var clg = contentGO.AddComponent<VerticalLayoutGroup>();
        clg.spacing = 6f; clg.childForceExpandWidth = true; clg.childForceExpandHeight = false;
        clg.padding = new RectOffset(4, 4, 4, 4);
        contentGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = vp; scroll.content = crt;
        content = contentGO.transform;

        lockedHint = MkText("plocked", transform, "", 11f, TextAnchor.MiddleLeft);
        lockedHint.color = new Color(1f, 0.75f, 0.3f);
        var lle = lockedHint.gameObject.AddComponent<LayoutElement>();
        lle.minHeight = FontUnits(11f) * 1.5f; lle.preferredHeight = FontUnits(11f) * 1.5f;

        // V2.6: bottom document bar removed — everything lives on the top row.
        builtSf = canvas != null ? canvas.scaleFactor : -1f;
    }

    // ---- V2.5 program library (SAVE / SAVE AS / LOAD TO ROBOT) ------------
    GameObject saveAsDlg; InputField saveAsInput; Text saveAsErr;

    void OnSaveTapped()
    {
        if (string.IsNullOrEmpty(prog.title)) { OpenSaveAs(); return; }
        SaveToLibrary(prog.title);
    }

    void SaveToLibrary(string name)
    {
        if (Career.Data == null) return;
        prog.title = name;
        SavedProgram e = null;
        foreach (var s in Career.Data.programs) if (s.name == name) { e = s; break; }
        bool existed = e != null;
        if (e == null) { e = new SavedProgram { name = name }; Career.Data.programs.Add(e); }
        e.json = prog.ToJson();
        if (Career.autosave) Career.Save();
        // NOTE: dirty stays as-is — a library save does NOT put the program
        // on the robot, and clearing dirty would make Refresh() reload the
        // robot's program over the canvas. LOAD TO ROBOT is what clears it.
        status.text = (existed ? "overwrote \"" : "saved \"") + name + "\" in your library ("
                    + prog.hats.Count + " hats) — LOAD TO ROBOT arms it on this machine";
        status.color = new Color(0.55f, 0.95f, 0.55f);

    }

    void OpenSaveAs()
    {
        if (saveAsDlg == null) BuildSaveAsDlg();
        saveAsInput.text = prog.title ?? "";
        saveAsErr.text = "";
        saveAsDlg.SetActive(true);
        saveAsDlg.transform.SetAsLastSibling();
    }

    void BuildSaveAsDlg()
    {
        // V2.7 polish: this is a real MODAL now. It used to be a 0.98-alpha
        // card floating over a live canvas - the program rows ghosted through
        // it (owen's catch), and a tap anywhere OUTSIDE the card still reached
        // the chips underneath, so 'tap away to dismiss' quietly cycled an op
        // instead. The root is a full-bleed scrim that eats those taps; the
        // card inside it is opaque.
        saveAsDlg = MkPanel("saveas_dlg", transform, new Color(0.02f, 0.02f, 0.03f, 0.72f));
        saveAsDlg.AddComponent<LayoutElement>().ignoreLayout = true;   // overlay, not a row
        Stretch((RectTransform)saveAsDlg.transform);
        var card = MkPanel("saveas_card", saveAsDlg.transform, new Color(0.07f, 0.08f, 0.11f, 1f));
        var rt = (RectTransform)card.transform;
        rt.anchorMin = new Vector2(0.04f, 0.30f); rt.anchorMax = new Vector2(0.96f, 0.70f);
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        var v = card.AddComponent<VerticalLayoutGroup>();
        v.spacing = 6f; v.padding = new RectOffset(10, 10, 10, 10);
        v.childForceExpandWidth = true; v.childForceExpandHeight = false;
        MkText("t", card.transform, "SAVE PROGRAM AS", 12f, TextAnchor.MiddleLeft);
        var ig = MkPanel("saveas_name", card.transform, new Color(0.13f, 0.14f, 0.18f, 1f));
        var ile = ig.AddComponent<LayoutElement>();
        ile.minHeight = TouchRow() * 0.9f; ile.preferredHeight = TouchRow() * 0.9f;
        saveAsInput = ig.AddComponent<InputField>();
        var itxt = MkText("txt", ig.transform, "", 12f, TextAnchor.MiddleLeft);
        itxt.raycastTarget = false; itxt.supportRichText = false;
        itxt.rectTransform.anchorMin = Vector2.zero; itxt.rectTransform.anchorMax = Vector2.one;
        itxt.rectTransform.offsetMin = new Vector2(8f, 2f); itxt.rectTransform.offsetMax = new Vector2(-8f, -2f);
        var ph = MkText("ph", ig.transform, "program name…", 12f, TextAnchor.MiddleLeft);
        ph.color = new Color(1f, 1f, 1f, 0.35f);
        ph.rectTransform.anchorMin = Vector2.zero; ph.rectTransform.anchorMax = Vector2.one;
        ph.rectTransform.offsetMin = new Vector2(8f, 2f); ph.rectTransform.offsetMax = new Vector2(-8f, -2f);
        saveAsInput.textComponent = itxt; saveAsInput.placeholder = ph;
        saveAsInput.characterLimit = 24;
        saveAsErr = MkText("saveas_err", card.transform, "", 10f, TextAnchor.MiddleLeft);
        saveAsErr.color = new Color(1f, 0.6f, 0.4f);
        var row = MkRow(card.transform);
        MkChip("saveas_ok", row.transform, "SAVE", 12f,
               new Color(0.16f, 0.34f, 0.18f, 0.95f), SaveAsConfirm, 84f);
        MkChip("saveas_cancel", row.transform, "CANCEL", 12f,
               new Color(0.24f, 0.24f, 0.30f, 0.9f), () => saveAsDlg.SetActive(false), 92f);
        saveAsDlg.SetActive(false);
    }

    void SaveAsConfirm()
    {
        string n = saveAsInput.text.Trim();
        if (n.Length == 0) { saveAsErr.text = "name it first"; return; }
        saveAsDlg.SetActive(false);
        SaveToLibrary(n);
    }

    // ---- V2.5b saved-programs picker (OPEN) ------------------------------
    GameObject openDlg; Transform openListContent; int openDelArm = -1;
    Text progDropLabel;   // V2.6: dropdown shows the open program's name
    float builtSf = -1f;   // scaleFactor the chrome was built at (Update guard)

    void OpenPicker()
    {
        if (openDlg == null) BuildOpenDlg();
        openDelArm = -1;
        RefreshOpenList();
        openDlg.SetActive(true);
        openDlg.transform.SetAsLastSibling();
    }

    void BuildOpenDlg()
    {
        // V2.7: modal, same reasoning as the SAVE AS dialog above.
        openDlg = MkPanel("open_dlg", transform, new Color(0.02f, 0.02f, 0.03f, 0.72f));
        openDlg.AddComponent<LayoutElement>().ignoreLayout = true;   // overlay
        Stretch((RectTransform)openDlg.transform);
        var card = MkPanel("open_card", openDlg.transform, new Color(0.07f, 0.08f, 0.11f, 1f));
        var rt = (RectTransform)card.transform;
        rt.anchorMin = new Vector2(0.04f, 0.12f); rt.anchorMax = new Vector2(0.96f, 0.88f);
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        var v = card.AddComponent<VerticalLayoutGroup>();
        v.spacing = 6f; v.padding = new RectOffset(10, 10, 10, 10);
        v.childForceExpandWidth = true; v.childForceExpandHeight = false;
        MkText("t", card.transform, "SWITCH PROGRAM", 12f, TextAnchor.MiddleLeft);
        var scGO = MkPanel("open_scroll", card.transform, new Color(0f, 0f, 0f, 0.25f));
        var sle2 = scGO.AddComponent<LayoutElement>(); sle2.flexibleHeight = 1f; sle2.minHeight = 60f;
        var sr2 = scGO.AddComponent<ScrollRect>(); sr2.horizontal = false; sr2.vertical = true;
        var vpGO = MkPanel("open_vp", scGO.transform, new Color(0f, 0f, 0f, 0f));
        var vprt = vpGO.GetComponent<RectTransform>(); Stretch(vprt);
        vpGO.AddComponent<RectMask2D>();   // Mask on an alpha-0 Image writes no stencil and clips everything
        var cGO = MkPanel("open_list", vpGO.transform, new Color(0f, 0f, 0f, 0f));
        var crt2 = cGO.GetComponent<RectTransform>();
        crt2.anchorMin = new Vector2(0f, 1f); crt2.anchorMax = new Vector2(1f, 1f);
        crt2.pivot = new Vector2(0.5f, 1f); crt2.anchoredPosition = Vector2.zero; crt2.sizeDelta = Vector2.zero;
        var clg2 = cGO.AddComponent<VerticalLayoutGroup>();
        clg2.spacing = 4f; clg2.childForceExpandWidth = true; clg2.childForceExpandHeight = false;
        cGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        sr2.viewport = vprt; sr2.content = crt2;
        openListContent = cGO.transform;
        var row = MkRow(card.transform);
        MkChip("open_cancel", row.transform, "CANCEL", 12f,
               new Color(0.24f, 0.24f, 0.30f, 0.9f), () => openDlg.SetActive(false), 92f);
        openDlg.SetActive(false);
    }

    /// <summary>V2.8 — one switch-list row for a built-in program, carrying
    /// its own shortfall. A row that cannot run on this bay says so on its
    /// face and reads amber instead of green.</summary>
    void AddPresetRow(string name, string label, RobotProgram p, List<string> ids)
    {
        var row = MkRow(openListContent);
        string shop = p.MissingPartsLine(ids, OwnedIds());
        var b = MkChip(name, row.transform,
            label + (shop == null ? "" : "   ·   " + shop), 11f,
            shop == null ? new Color(0.18f, 0.30f, 0.22f, 0.95f)
                         : new Color(0.30f, 0.24f, 0.16f, 0.95f), () =>
        { openDlg.SetActive(false); InstallPreset(p); }, 200f);
        b.GetComponent<LayoutElement>().flexibleWidth = 1f;
    }

    void RefreshOpenList()
    {
        for (int i = openListContent.childCount - 1; i >= 0; i--)
            Destroy(openListContent.GetChild(i).gameObject);
        // V2.6: the preset leads the switch list; library rows follow.
        // V2.8 (critic loop 7, F1a/F1c): TWO presets now, and each row states
        // its own hardware price ON ITS FACE, before the canvas is replaced.
        // The old list offered one sensor-heavy program to a career whose
        // granted parts crate (CareerDB.StarterKit) holds no sensor at all —
        // and you found that out only after LOAD had overwritten the canvas.
        var presetIds = BuildIds();
        AddPresetRow("open_first", "FIRST STEPS  (preset)", RobotProgram.FirstSteps(), presetIds);
        AddPresetRow("open_preset", "STARTER KIT  (preset)", RobotProgram.StarterKit(), presetIds);
        var lib = Career.Data != null ? Career.Data.programs : null;
        if (lib == null || lib.Count == 0)
        {
            var none = MkText("open_none", openListContent,
                "no saved programs yet — SAVE puts the canvas program here", 10f, TextAnchor.MiddleLeft);
            none.color = new Color(1f, 1f, 1f, 0.5f);
            return;
        }
        for (int i = 0; i < lib.Count; i++)
        {
            int iC = i;
            var e = lib[i];
            var row = MkRow(openListContent);
            var back = RobotProgram.FromJson(e.json);
            string hatWord = (back != null && back.hats.Count == 1) ? " hat)" : " hats)";
            string label = e.name + (back != null ? "  (" + back.hats.Count + hatWord : "  (unreadable)");
            var ob = MkChip("open_" + i, row.transform, label, 11f,
                new Color(0.15f, 0.22f, 0.30f, 0.95f), () => OpenEntry(iC), 200f);
            ob.GetComponent<LayoutElement>().flexibleWidth = 1f;
            bool armed = openDelArm == iC;
            MkChip("odel_" + i, row.transform, armed ? "SURE?" : "✕", 12f,
                armed ? new Color(0.55f, 0.12f, 0.12f, 0.95f) : new Color(0.32f, 0.14f, 0.14f, 0.9f),
                () => DeleteEntry(iC), 48f);
        }
    }

    void OpenEntry(int i)
    {
        var lib = Career.Data.programs;
        if (i < 0 || i >= lib.Count) return;
        var e = lib[i];
        var p2 = RobotProgram.FromJson(e.json);
        if (p2 == null)
        { status.text = "couldn't read \"" + e.name + "\" — old format"; status.color = new Color(1f, 0.6f, 0.4f); return; }
        prog = p2;
        prog.title = e.name;
        dirty = true;   // canvas != robot until LOAD TO ROBOT
        openDlg.SetActive(false);
        status.text = "opened \"" + e.name + "\" (" + prog.hats.Count + " hats) — LOAD TO ROBOT arms it on this machine";
        status.color = new Color(0.6f, 0.9f, 1f);
        Refresh();
    }

    void DeleteEntry(int i)
    {
        var lib = Career.Data.programs;
        if (i < 0 || i >= lib.Count) return;
        if (openDelArm != i) { openDelArm = i; RefreshOpenList(); return; }   // arm first
        string gone = lib[i].name;
        lib.RemoveAt(i);
        openDelArm = -1;
        if (Career.autosave) Career.Save();
        status.text = "deleted \"" + gone + "\" from your library";
        status.color = new Color(1f, 0.75f, 0.3f);
        RefreshOpenList();
    }

    // ---- data lifecycle ---------------------------------------------------
    CareerRobot ActiveRobot()
    {
        if (!Career.active || Career.Data == null) return null;
        int i = Career.Data.activeRobot;
        return (i >= 0 && i < Career.Data.stable.Count) ? Career.Data.stable[i] : null;
    }

    public void Refresh()
    {
        var r = ActiveRobot();
        if (!dirty)
        {
            prog = (r != null ? RobotProgram.FromJson(r.program) : null) ?? new RobotProgram();
        }
        header.text = r != null
            ? "PROGRAM — " + r.name + (dirty ? "  (unsaved)" : "")
            : "PROGRAM — no robot open (SAVE a robot first; its program rides with it)";
        if (progDropLabel != null)
            progDropLabel.text = (string.IsNullOrEmpty(prog.title) ? "PROGRAMS" : prog.title.ToUpper()) + "  ▾";
        RebuildList();
        RefreshLockedHint();
        // V2.8 (critic loop 7, F3): an empty canvas used to say NOTHING —
        // blank status, and four of its five controls are document
        // operations on a document that does not exist yet. Point at the door.
        if (prog.hats.Count == 0 && string.IsNullOrEmpty(status.text))
        {
            status.text = "no program yet — tap PROGRAMS ▾ and pick a preset to start";
            status.color = new Color(0.6f, 0.9f, 1f);
        }
    }

    void MarkDirty()
    {
        dirty = true;
        status.text = "edited — SAVE to keep";
        status.color = new Color(0.95f, 0.9f, 0.5f);
        Refresh();
    }

    void InstallPreset(RobotProgram p)
    {
        prog = p;
        dirty = true;
        status.text = p.title + " installed — tap any chip to edit, SAVE to keep";
        status.color = new Color(0.6f, 0.9f, 1f);
        Refresh();
    }

    void SaveProgram()
    {
        var r = ActiveRobot();
        if (r == null)
        { status.text = "no robot open — SAVE the build as a robot first"; status.color = new Color(1f, 0.6f, 0.4f); return; }
        string err = prog.Validate(BuildIds());
        if (err != null)
        {
            // V2.8 (critic loop 7, F1b): when the refusal is about missing
            // hardware, name ALL of it at once — one shopping trip, not three.
            // V2.8b (critic loop 7 R2, finding 2): ONLY hardware refusals get
            // swapped for the whole shopping list. Round 1 wrote `shop ?? err`
            // unconditionally, so "the sequence is empty — add a step" or an
            // unbalanced IF was silently replaced by a parts list: the player
            // bought the parts, came back, and only THEN met the real problem.
            // Two trips pointing the other way — the exact defect round 1 set
            // out to remove.
            bool hardware = err.EndsWith("— SHOP") || err.Contains("on the build");
            string shop = hardware ? prog.MissingPartsLine(BuildIds(), OwnedIds()) : null;
            status.text = shop ?? err;
            status.color = new Color(1f, 0.6f, 0.4f); return;
        }
        r.program = prog.ToJson();
        if (Career.autosave) Career.Save();
        dirty = false;
        // P3c: point at the payoff — the saved program is what TEST DRIVE's
        // AUTO button arms (unsaved edits deliberately don't).
        status.text = "loaded to " + r.name + " (" + prog.hats.Count + " hats, "
                    + prog.BlockCount() + " blocks) — TEST DRIVE → AUTO to watch it";
        status.color = new Color(0.55f, 0.95f, 0.55f);
        Refresh();
    }

    void RefreshLockedHint()
    {
        var ids = BuildIds();
        var missing = new List<string>();
        foreach (var sid in new[] { "rangefinder", "compass", "tiltsensor", "wallsensor", "trapsensor", "dmgbus" })
            if (!ids.Contains(sid)) missing.Add(sid);
        lockedHint.text = missing.Count == 0 ? ""
            : "more blocks with: " + string.Join(", ", missing) + " — SHOP";
    }

    // ---- the canvas itself ------------------------------------------------
    void RebuildList()
    {
        // DestroyImmediate, not Destroy: deferred destruction leaves the old
        // cards alive until frame end, so every tap's rebuild stacked old+new
        // for one frame (measured: 4 hat cards after a tab away/back) — a
        // visible flicker on device and a lie to any same-frame test.
        for (int i = content.childCount - 1; i >= 0; i--)
            DestroyImmediate(content.GetChild(i).gameObject);
        hiHat = hiStep = -1;   // V2.2: rows are fresh — let Update re-tint

        var ids = BuildIds();
        for (int hi = 0; hi < prog.hats.Count; hi++)
            BuildHatCard(hi, ids);

        if (prog.hats.Count < RobotProgram.MAX_HATS)
        {
            var addRow = MkRow(content);
            addRow.name = "addhatrow";
            var ah = MkChip("addhat", addRow.transform, "+ HAT", 12f,
                   new Color(0.14f, 0.25f, 0.16f, 0.95f), () =>
            {
                var h = new PHat { name = "" };
                h.when.Add(PCondTerm.Always());
                h.body.Add(PBlock.MkMove(70f, 1f, 0));
                prog.hats.Add(h);
                MarkDirty();
            }, 140f);
            // P3b: + HAT is also a palette SOURCE — drag it to where the new
            // hat should sit in the priority order (tap still appends).
            AddHandle(ah.gameObject, ProgramDragHandle.NEW_HAT, -1, -1);
        }
    }

    void AddHandle(GameObject go, int kind, int hat, int block)
    {
        var h = go.AddComponent<ProgramDragHandle>();
        h.owner = this; h.kind = kind; h.hat = hat; h.block = block;
    }

    void BuildHatCard(int hi, List<string> ids)
    {
        var hat = prog.hats[hi];
        var card = MkPanel("hat_" + hi, content, new Color(0.12f, 0.14f, 0.19f, 0.95f));
        var cv = card.AddComponent<VerticalLayoutGroup>();
        cv.spacing = 2f; cv.childForceExpandWidth = true; cv.childForceExpandHeight = false;
        cv.padding = new RectOffset(4, 4, 4, 4);
        card.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // V2.4: tutorial caption — read-only, above the header row. INSIDE
        // the card so content's hat_ sibling maths (drag/drop) is untouched;
        // wrap + Overflow so it can never clip at iPhone width (C19 lesson).
        if (!string.IsNullOrEmpty(hat.note))
        {
            var nt = MkText("note", card.transform, hat.note, 10f, TextAnchor.MiddleLeft);
            nt.color = new Color(0.87f, 0.83f, 0.55f, 1f);
            nt.verticalOverflow = VerticalWrapMode.Overflow;
        }

        // header row: drag handle + priority controls + WHEN terms + latch
        var head = MkRow(card.transform);
        head.name = "headrow";
        int hiC = hi;
        // P3b: the hat's drag handle — drag to reorder priority, off-list to
        // delete. Its own chip (not the whole card) so chips stay tappable
        // and empty card space still scrolls the list.
        var hh = MkChip("hdrag", head.transform, "≡", 13f,
                        new Color(0.24f, 0.24f, 0.30f, 0.9f), () => { }, 40f);
        AddHandle(hh.gameObject, ProgramDragHandle.HAT, hi, -1);
        MkChip("up", head.transform, "▲", 13f, new Color(0.2f, 0.2f, 0.26f, 0.9f), () =>
        { if (hiC > 0) { var t = prog.hats[hiC]; prog.hats[hiC] = prog.hats[hiC - 1]; prog.hats[hiC - 1] = t; MarkDirty(); } }, 40f);
        MkChip("dn", head.transform, "▼", 13f, new Color(0.2f, 0.2f, 0.26f, 0.9f), () =>
        { if (hiC < prog.hats.Count - 1) { var t = prog.hats[hiC]; prog.hats[hiC] = prog.hats[hiC + 1]; prog.hats[hiC + 1] = t; MarkDirty(); } }, 40f);
        MkChip("del", head.transform, "✕", 13f, new Color(0.32f, 0.14f, 0.14f, 0.9f), () =>
        { prog.hats.RemoveAt(hiC); MarkDirty(); }, 40f);
        var wl = MkText("when", head.transform, (hi + 1) + " WHEN", 12f, TextAnchor.MiddleCenter);
        var wle = wl.gameObject.AddComponent<LayoutElement>(); wle.minWidth = 64f;

        for (int ti = 0; ti < hat.when.Count; ti++)
        {
            var term = hat.when[ti];
            int tiC = ti;
            MkChip("ck", head.transform, CondName(term.kind), 10f,
                   new Color(0.18f, 0.24f, 0.32f, 0.95f), () =>
            { term.kind = NextCond(term.kind, BuildIds()); term.value = ValuesOf(term.kind)[0]; MarkDirty(); }, 96f);
            if (term.kind != PCond.Always)
            {
                MkChip("cc", head.transform, term.cmp == PCmp.Less ? "<" : ">", 13f,
                       new Color(0.2f, 0.2f, 0.26f, 0.9f), () =>
                { term.cmp = term.cmp == PCmp.Less ? PCmp.Greater : PCmp.Less; MarkDirty(); }, 36f);
                MkChip("cv", head.transform, term.value.ToString("0.##"), 11f,
                       new Color(0.2f, 0.2f, 0.26f, 0.9f), () =>
                {
                    var vals = ValuesOf(term.kind);
                    int i = System.Array.IndexOf(vals, term.value);
                    term.value = vals[(i + 1 + vals.Length) % vals.Length];
                    MarkDirty();
                }, 52f);
            }
            if (hat.when.Count > 1)
                MkChip("cx", head.transform, "–", 12f, new Color(0.3f, 0.16f, 0.16f, 0.9f), () =>
                { hat.when.RemoveAt(tiC); MarkDirty(); }, 32f);
        }
        if (hat.when.Count < RobotProgram.MAX_TERMS)
            MkChip("addc", head.transform, "+", 12f, new Color(0.14f, 0.25f, 0.16f, 0.9f), () =>
            { hat.when.Add(PCondTerm.Always()); MarkDirty(); }, 36f);
        // V2: the latch chip is GONE — commitment is the sequence itself
        // (run-to-completion; see the V2 design doc §1.2).

        // body rows, indented by IF/REPEAT/FOREVER depth (V2)
        int depth = 0;
        for (int bi = 0; bi < hat.body.Count; bi++)
        {
            var b = hat.body[bi];
            if (b.op == POp.Else || b.op == POp.EndIf || b.op == POp.End)
                depth = Mathf.Max(0, depth - 1);
            var row = MkRow(card.transform, 24f + depth * 26f);
            row.name = "brow_" + bi;
            int biC = bi;
            // P3b: block drag handle. Closers/ELSE move with their opener
            // (dragging an opener moves the whole span) — no handle.
            if (b.op != POp.Else && b.op != POp.EndIf && b.op != POp.End)
            {
                var bh = MkChip("bdrag", row.transform, "≡", 11f,
                                new Color(0.24f, 0.24f, 0.30f, 0.9f), () => { }, 34f);
                AddHandle(bh.gameObject, ProgramDragHandle.BLOCK, hi, bi);
            }
            MkChip("bx", row.transform, "✕", 11f, new Color(0.3f, 0.16f, 0.16f, 0.9f), () =>
            { DeleteBlock(hat, biC); MarkDirty(); }, 34f);
            // V2.7: a structural marker's op chip no longer cycles into an
            // action verb (IsStructural). REPEAT/FOREVER keeps a live chip -
            // it swaps between those two, both of which close on END - and
            // reads amber; the pure markers (IF/ELSE/END IF/END) go grey and
            // inert, because there is nothing safe for a tap to do to them.
            bool struc = IsStructural(b.op);
            bool swapLoop = b.op == POp.Repeat || b.op == POp.Forever;
            MkChip("bop", row.transform, OpName(b.op), 11f,
                   struc ? (swapLoop ? new Color(0.30f, 0.24f, 0.16f, 0.95f)
                                     : new Color(0.22f, 0.22f, 0.27f, 0.95f))
                         : new Color(0.17f, 0.28f, 0.22f, 0.95f), () =>
            { if (!struc || swapLoop) { CycleOp(b, BuildIds()); MarkDirty(); } }, 96f);
            // V2.7: an IF with no ELSE yet offers one, inserted at its OWN
            // matching END IF - balanced by construction, like + IF itself.
            if (b.op == POp.If && !SpanHasElse(hat, bi))
                MkChip("belse", row.transform, "+ ELSE", 10f,
                       new Color(0.20f, 0.26f, 0.34f, 0.95f), () =>
                { InsertElse(hat, biC); MarkDirty(); }, 70f);
            if (b.op == POp.If && b.cond != null)
            {
                var c = b.cond;
                MkChip("ick", row.transform, CondName(c.kind), 10f,
                       new Color(0.18f, 0.24f, 0.32f, 0.95f), () =>
                { c.kind = NextCond(c.kind, BuildIds()); c.value = ValuesOf(c.kind)[0]; MarkDirty(); }, 96f);
                if (c.kind != PCond.Always)
                {
                    MkChip("icc", row.transform, c.cmp == PCmp.Less ? "<" : ">", 13f,
                           new Color(0.2f, 0.2f, 0.26f, 0.9f), () =>
                    { c.cmp = c.cmp == PCmp.Less ? PCmp.Greater : PCmp.Less; MarkDirty(); }, 36f);
                    MkChip("icv", row.transform, c.value.ToString("0.##"), 11f,
                           new Color(0.2f, 0.2f, 0.26f, 0.9f), () =>
                    {
                        var vals = ValuesOf(c.kind);
                        int i = System.Array.IndexOf(vals, c.value);
                        c.value = vals[(i + 1 + vals.Length) % vals.Length];
                        MarkDirty();
                    }, 52f);
                }
            }
            else if (b.op == POp.Move || b.op == POp.MoveRel)
            {
                // V2.2: MOVE — direction chip (blind FORWARD/BACKWARD flips
                // sign; relative TOWARD/AWAY flips sign; tapping past the
                // pair crosses between blind and relative when a directional
                // sensor is mounted), target chip (relative only), power,
                // and the HOLD/seconds/rounds chip.
                bool rel = b.op == POp.MoveRel;
                string dirLbl = rel ? (b.arg >= 0f ? "TOWARD" : "AWAY FROM")
                                    : (b.arg >= 0f ? "FORWARD" : "BACKWARD");
                MkChip("bdir", row.transform, dirLbl, 10f,
                       new Color(0.20f, 0.26f, 0.34f, 0.95f), () =>
                {
                    // FORWARD → BACKWARD → TOWARD → AWAY → FORWARD…
                    var ids2 = BuildIds();
                    bool r2 = b.op == POp.MoveRel;
                    if (b.arg >= 0f) b.arg = -Mathf.Abs(b.arg);         // fwd→back / toward→away
                    else if (!r2 && FirstTarget(ids2) >= 0)
                    { b.op = POp.MoveRel; b.target = FirstTarget(ids2); b.arg = Mathf.Abs(b.arg); }
                    else { b.op = POp.Move; b.arg = Mathf.Abs(b.arg); }
                    MarkDirty();
                }, 96f);
                if (rel)
                    MkChip("btgt", row.transform, RobotProgram.TargetLabel(b.target), 10f,
                           new Color(0.26f, 0.22f, 0.34f, 0.95f), () =>
                    { b.target = NextTarget(b.target, BuildIds()); MarkDirty(); }, 76f);
                MkChip("bpow", row.transform, Mathf.Abs(b.arg).ToString("0") + "%", 11f,
                       new Color(0.2f, 0.2f, 0.26f, 0.9f), () =>
                {
                    int i = System.Array.IndexOf(V_POWER, Mathf.Abs(b.arg));
                    float nx = V_POWER[(i + 1 + V_POWER.Length) % V_POWER.Length];
                    b.arg = Mathf.Sign(b.arg == 0f ? 1f : b.arg) * nx;
                    MarkDirty();
                }, 56f);
                AddHoldDurChip(row.transform, b, true);
            }
            else if (b.op == POp.TurnLR)
            {
                // V2.2: TURN — LEFT/RIGHT (sign; +.. is right), power,
                // HOLD/seconds. One more direction tap reaches BY n° and
                // SIDE TO (the other members of the TURN family).
                MkChip("bdir", row.transform, b.arg >= 0f ? "RIGHT" : "LEFT", 10f,
                       new Color(0.20f, 0.26f, 0.34f, 0.95f), () =>
                { b.arg = -b.arg; MarkDirty(); }, 76f);
                MkChip("bpow", row.transform, Mathf.Abs(b.arg).ToString("0") + "%", 11f,
                       new Color(0.2f, 0.2f, 0.26f, 0.9f), () =>
                {
                    int i = System.Array.IndexOf(V_POWER, Mathf.Abs(b.arg));
                    float nx = V_POWER[(i + 1 + V_POWER.Length) % V_POWER.Length];
                    b.arg = Mathf.Sign(b.arg == 0f ? 1f : b.arg) * nx;
                    MarkDirty();
                }, 56f);
                AddHoldDurChip(row.transform, b, false);
            }
            else if (b.op == POp.TurnBy)
            {
                MkChip("bdir", row.transform, b.arg >= 0f ? "RIGHT" : "LEFT", 10f,
                       new Color(0.20f, 0.26f, 0.34f, 0.95f), () =>
                { b.arg = -b.arg; MarkDirty(); }, 76f);
                MkChip("bdeg", row.transform, Mathf.Abs(b.arg).ToString("0") + "°", 11f,
                       new Color(0.2f, 0.2f, 0.26f, 0.9f), () =>
                {
                    int i = System.Array.IndexOf(V_TURNDEG, Mathf.Abs(b.arg));
                    float nx = V_TURNDEG[(i + 1 + V_TURNDEG.Length) % V_TURNDEG.Length];
                    b.arg = Mathf.Sign(b.arg == 0f ? 1f : b.arg) * nx;
                    MarkDirty();
                }, 56f);
            }
            else if (b.op == POp.Weapon)
            {
                MkChip("bon", row.transform, b.arg >= 0.5f ? "ON" : "OFF", 11f,
                       b.arg >= 0.5f ? new Color(0.16f, 0.34f, 0.18f, 0.95f)
                                     : new Color(0.3f, 0.2f, 0.16f, 0.95f), () =>
                { b.arg = b.arg >= 0.5f ? 0f : 100f; MarkDirty(); }, 56f);
            }
            else if (b.op == POp.FaceSide)
            {
                string[] sides = { "FRONT", "RIGHT", "BACK", "LEFT" };
                MkChip("bside", row.transform, sides[Mathf.Clamp(b.idx, 0, 3)] + " SIDE", 10f,
                       new Color(0.20f, 0.26f, 0.34f, 0.95f), () =>
                { b.idx = (b.idx + 1) % 4; MarkDirty(); }, 96f);
                MkChip("btgt", row.transform, RobotProgram.TargetLabel(b.target), 10f,
                       new Color(0.26f, 0.22f, 0.34f, 0.95f), () =>
                { b.target = NextTarget(b.target, BuildIds()); MarkDirty(); }, 76f);
            }
            else
            {
                // internal ops (SET/RUN/FIRE/TURN TOWARD — render-compat for
                // old harness programs) + WAIT/REPEAT keep their chips
                if (HasPartChip(b.op))
                    MkChip("bpart", row.transform, PartLabel(b.part, b.idx), 10f,
                           new Color(0.20f, 0.26f, 0.34f, 0.95f), () =>
                    {
                        int cur = 0;
                        for (int oi = 0; oi < PART_OPTS.Length; oi++)
                            if (PART_OPTS[oi].part == b.part
                                && (b.part != PPart.Wheel && b.part != PPart.Actuator || PART_OPTS[oi].idx == b.idx))
                            { cur = oi; break; }
                        var nx = PART_OPTS[(cur + 1) % PART_OPTS.Length];
                        b.part = nx.part; b.idx = nx.idx;
                        MarkDirty();
                    }, 100f);
                var vals = ValuesOfOp(b.op);
                if (vals != null)
                {
                    string suffix = b.op == POp.Repeat ? "×" : (b.op == POp.Fire ? " cyc" : "%");
                    MkChip("barg", row.transform, b.arg.ToString("0") + suffix, 11f,
                           new Color(0.2f, 0.2f, 0.26f, 0.9f), () =>
                    {
                        int i = System.Array.IndexOf(vals, b.arg);
                        b.arg = vals[(i + 1 + vals.Length) % vals.Length];
                        MarkDirty();
                    }, 56f);
                }
                if (HasDurChip(b.op))
                    MkChip("bdur", row.transform,
                           b.rounds > 0 ? b.rounds + " rnd" : b.dur.ToString("0.#") + "s", 11f,
                           new Color(0.24f, 0.2f, 0.28f, 0.9f), () =>
                    {
                        // cycles the duration list; one more tap past the end
                        // flips RUN into rounds mode (2/5/10 rnd) and back
                        if (b.op == POp.RunMotor && b.rounds > 0)
                        {
                            b.rounds = b.rounds == 2 ? 5 : b.rounds == 5 ? 10 : 0;
                            if (b.rounds == 0) { b.dur = V_DUR[0]; }
                        }
                        else
                        {
                            int i = System.Array.IndexOf(V_DUR, b.dur);
                            if (i == V_DUR.Length - 1 && b.op == POp.RunMotor)
                            { b.dur = 0f; b.rounds = 2; }
                            else b.dur = V_DUR[(i + 1 + V_DUR.Length) % V_DUR.Length];
                        }
                        MarkDirty();
                    }, 62f);
            }
            // V2.7: ELSE dedents to its IF above, and then the ELSE BRANCH
            // BODY has to re-indent. Without this the two arms of one IF drew
            // at different depths and the else arm read as code AFTER the IF.
            if (b.op == POp.If || b.op == POp.Repeat || b.op == POp.Forever
                || b.op == POp.Else) depth++;
        }

        if (prog.BlockCount() < RobotProgram.MAX_BLOCKS)
        {
            var addRow = MkRow(card.transform, 24f);
            addRow.name = "addblockrow";
            var ab = MkChip("addb", addRow.transform, "+ BLOCK", 11f,
                   new Color(0.14f, 0.25f, 0.16f, 0.9f), () =>
            { hat.body.Add(PBlock.MkMove(70f, 1f, 0)); MarkDirty(); }, 110f);
            // P3b: + BLOCK is a palette source too — drag it to the exact row
            // (any hat, any depth) the new block should occupy.
            AddHandle(ab.gameObject, ProgramDragHandle.NEW_BLOCK, hi, -1);
            // V2.7: structure arrives as a MATCHED SPAN with a body already
            // inside it - three blocks, balanced, runnable the moment it
            // lands. Before this the only door to an IF was cycling a chip
            // through the op list, which meant authoring your way back out of
            // an invalid program every single time.
            if (prog.BlockCount() + 3 <= RobotProgram.MAX_BLOCKS)
            {
                MkChip("addif", addRow.transform, "+ IF", 11f,
                       new Color(0.18f, 0.24f, 0.32f, 0.9f), () =>
                {
                    hat.body.Add(PBlock.MkIf(PCondTerm.Always()));
                    hat.body.Add(PBlock.MkMove(70f, 1f, 0));
                    hat.body.Add(PBlock.MkEndIf());
                    MarkDirty();
                }, 66f);
                MkChip("addrep", addRow.transform, "+ REPEAT", 11f,
                       new Color(0.30f, 0.24f, 0.16f, 0.9f), () =>
                {
                    hat.body.Add(PBlock.MkRepeat(3));
                    hat.body.Add(PBlock.MkMove(70f, 1f, 0));
                    hat.body.Add(PBlock.MkEnd());
                    MarkDirty();
                }, 96f);
            }
        }
    }

    // =======================================================================
    // P3b — drag-and-drop. A drag lifts a GHOST (screen-space clone following
    // the pointer), resolves a drop target every move (cyan DROP LINE opens
    // the gap in the actual layout — the same physical-inch rows a finger
    // taps), and applies ONE mutation on release through the public seams
    // below (which the ▲▼/✕ taps could also use, and the bench DOES use).
    // Off-list = delete for canvas items, cancel for palette items. uGUI
    // clears eligibleForClick when a drag passes the threshold, so handle
    // chips that are also Buttons never fire a stray tap after a drag.
    // =======================================================================

    static Rect WorldRect(RectTransform rt)
    {
        var c = new Vector3[4];
        rt.GetWorldCorners(c);   // ScreenSpaceOverlay: world == screen px
        return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
    }
    static float CenterY(RectTransform rt) { var r = WorldRect(rt); return (r.yMin + r.yMax) * 0.5f; }

    List<RectTransform> HatCards()
    {
        var list = new List<RectTransform>();
        if (content == null) return list;
        for (int i = 0; i < content.childCount; i++)
        {
            var c = content.GetChild(i);
            if (c.name.StartsWith("hat_")) list.Add((RectTransform)c);
        }
        return list;
    }

    string DragLabel(ProgramDragHandle d)
    {
        switch (d.kind)
        {
            case ProgramDragHandle.HAT:
                if (d.hat >= 0 && d.hat < prog.hats.Count)
                {
                    var h = prog.hats[d.hat];
                    return "≡ " + (d.hat + 1) + " WHEN" + (string.IsNullOrEmpty(h.name) ? "" : " · " + h.name);
                }
                return "≡ hat";
            case ProgramDragHandle.BLOCK:
                if (d.hat >= 0 && d.hat < prog.hats.Count && d.block >= 0 && d.block < prog.hats[d.hat].body.Count)
                {
                    var b = prog.hats[d.hat].body[d.block];
                    return "≡ " + OpName(b.op) + (b.op == POp.If ? " … END IF" : b.op == POp.Repeat || b.op == POp.Forever ? " … END" : "");
                }
                return "≡ block";
            case ProgramDragHandle.NEW_HAT: return "+ HAT";
            default: return "+ BLOCK";
        }
    }

    public void DragBegin(ProgramDragHandle d, PointerEventData e)
    {
        if (drag != null) DragEnd(e);   // stale drag (focus loss): settle it first
        drag = d; dropDelete = false; dropHat = dropIdx = -1;

        ghost = MkPanel("dragghost", canvas != null ? canvas.transform : transform,
                        new Color(0.16f, 0.20f, 0.30f, 0.88f));
        ghostImg = ghost.GetComponent<Image>();
        ghostImg.raycastTarget = false;
        var grt = ghost.GetComponent<RectTransform>();
        string lbl = DragLabel(d);
        grt.sizeDelta = new Vector2(
            Mathf.Max(TouchRow() * 2.2f, lbl.Length * FontUnits(11f) * 0.62f + FontUnits(11f) * 1.6f),
            TouchRow() * 0.9f);
        var gt = MkText("lbl", ghost.transform, lbl, 11f, TextAnchor.MiddleCenter);
        Stretch(gt.rectTransform);
        ghost.transform.position = e.position;

        dropLine = MkPanel("dropline", null, new Color(0.30f, 0.85f, 1f, 0.95f));
        dropLine.GetComponent<Image>().raycastTarget = false;
        var dle = dropLine.AddComponent<LayoutElement>();
        dle.minHeight = 8f; dle.preferredHeight = 8f; dle.flexibleWidth = 1f;
        dropLine.SetActive(false);

        status.text = d.kind <= ProgramDragHandle.BLOCK
            ? "drag — drop to move, drag OFF the list to delete"
            : "drag — drop to place";
        status.color = new Color(0.6f, 0.9f, 1f);
        DragMove(e);
    }

    public void DragMove(PointerEventData e)
    {
        if (drag == null || ghost == null) return;
        ghost.transform.position = e.position;

        // edge auto-scroll: a long program must be traversable mid-drag
        if (scroll != null && viewportRT != null)
        {
            var vr = WorldRect(viewportRT);
            float edge = TouchRow() * 0.9f;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            if (e.position.y > vr.yMax - edge)
                scroll.verticalNormalizedPosition = Mathf.Clamp01(scroll.verticalNormalizedPosition + 1.4f * dt);
            else if (e.position.y < vr.yMin + edge)
                scroll.verticalNormalizedPosition = Mathf.Clamp01(scroll.verticalNormalizedPosition - 1.4f * dt);
        }
        ResolveDrop(e.position);
    }

    bool OutsideList(Vector2 p)
    {
        if (viewportRT == null) return false;
        var r = WorldRect(viewportRT);
        float m = TouchRow() * 0.35f;
        return p.x < r.xMin - m || p.x > r.xMax + m || p.y < r.yMin - m || p.y > r.yMax + m;
    }

    void ResolveDrop(Vector2 p)
    {
        dropDelete = OutsideList(p);
        bool canvasItem = drag.kind <= ProgramDragHandle.BLOCK;
        if (dropDelete)
        {
            dropHat = dropIdx = -1;
            if (dropLine != null) dropLine.SetActive(false);
            ghostImg.color = canvasItem ? new Color(0.45f, 0.12f, 0.12f, 0.9f)
                                        : new Color(0.25f, 0.25f, 0.25f, 0.7f);
            status.text = canvasItem
                ? (drag.kind == ProgramDragHandle.BLOCK && DragIsIfSpan() ? "release to DELETE the whole IF…END IF" : "release to DELETE")
                : "release to cancel";
            status.color = canvasItem ? new Color(1f, 0.55f, 0.45f) : new Color(0.8f, 0.8f, 0.8f);
            return;
        }
        ghostImg.color = new Color(0.16f, 0.20f, 0.30f, 0.88f);
        status.color = new Color(0.6f, 0.9f, 1f);

        var cards = HatCards();
        if (drag.kind == ProgramDragHandle.HAT || drag.kind == ProgramDragHandle.NEW_HAT)
        {
            int idx = 0;
            foreach (var c in cards) if (CenterY(c) > p.y) idx++;
            dropHat = Mathf.Clamp(idx, 0, prog.hats.Count); dropIdx = -1;
            dropLine.SetActive(true);
            dropLine.transform.SetParent(content, false);
            int sib = idx < cards.Count ? cards[idx].GetSiblingIndex()
                    : (cards.Count > 0 ? cards[cards.Count - 1].GetSiblingIndex() + 1 : 0);
            dropLine.transform.SetSiblingIndex(sib);
            status.text = (drag.kind == ProgramDragHandle.HAT ? "move hat → priority " : "new hat → priority ")
                        + (dropHat + 1);
        }
        else
        {
            // which hat card is under (or nearest to) the pointer?
            int ci = -1; float best = float.MaxValue;
            for (int i = 0; i < cards.Count; i++)
            {
                var r = WorldRect(cards[i]);
                if (p.y <= r.yMax && p.y >= r.yMin) { ci = i; break; }
                float dd = Mathf.Min(Mathf.Abs(p.y - r.yMax), Mathf.Abs(p.y - r.yMin));
                if (dd < best) { best = dd; ci = i; }
            }
            if (ci < 0 || ci >= prog.hats.Count)
            { dropHat = dropIdx = -1; dropLine.SetActive(false); return; }
            var card = cards[ci];
            var rows = new List<RectTransform>();
            for (int i = 0; i < card.childCount; i++)
            {
                var c = card.GetChild(i);
                if (c.name.StartsWith("brow_")) rows.Add((RectTransform)c);
            }
            int bi = 0;
            foreach (var r in rows) if (CenterY(r) > p.y) bi++;
            dropHat = ci; dropIdx = Mathf.Clamp(bi, 0, prog.hats[ci].body.Count);
            dropLine.SetActive(true);
            dropLine.transform.SetParent(card, false);
            int sib = bi < rows.Count ? rows[bi].GetSiblingIndex()
                    : (rows.Count > 0 ? rows[rows.Count - 1].GetSiblingIndex() + 1 : 1);
            dropLine.transform.SetSiblingIndex(sib);
            status.text = (drag.kind == ProgramDragHandle.BLOCK ? "move → hat " : "new block → hat ")
                        + (ci + 1) + ", row " + (dropIdx + 1);
        }
    }

    bool DragIsIfSpan()
    {
        return drag != null && drag.kind == ProgramDragHandle.BLOCK
            && drag.hat >= 0 && drag.hat < prog.hats.Count
            && drag.block >= 0 && drag.block < prog.hats[drag.hat].body.Count
            && (prog.hats[drag.hat].body[drag.block].op == POp.If
            || prog.hats[drag.hat].body[drag.block].op == POp.Repeat
            || prog.hats[drag.hat].body[drag.block].op == POp.Forever);
    }

    public void DragEnd(PointerEventData e)
    {
        if (drag == null) return;
        var d = drag; drag = null;
        bool del = dropDelete; int dh = dropHat, di = dropIdx;
        if (ghost != null) DestroyImmediate(ghost); ghost = null; ghostImg = null;
        if (dropLine != null) DestroyImmediate(dropLine); dropLine = null;

        if (del)
        {
            if (d.kind == ProgramDragHandle.HAT) DeleteHatAt(d.hat);
            else if (d.kind == ProgramDragHandle.BLOCK) DeleteBlockSpanAt(d.hat, d.block);
            else { status.text = "cancelled"; status.color = new Color(0.8f, 0.8f, 0.8f); Refresh(); }
            return;
        }
        if (dh < 0)
        { status.text = "drag cancelled"; status.color = new Color(0.8f, 0.8f, 0.8f); Refresh(); return; }
        if (d.kind == ProgramDragHandle.HAT) MoveHat(d.hat, dh);
        else if (d.kind == ProgramDragHandle.NEW_HAT) InsertHatAt(dh);
        else if (d.kind == ProgramDragHandle.BLOCK) MoveBlockSpan(d.hat, d.block, dh, di);
        else InsertBlockAt(dh, di);
    }

    // ---- the mutations (public: drag calls them, the bench calls them) ----

    /// <summary>Inclusive end of the block span starting at `start`: an IF
    /// takes everything through its matching END IF (unbalanced IF takes the
    /// rest — Validate flags that on SAVE); anything else is just itself.</summary>
    static int SpanEnd(PHat h, int start)
    {
        var op0 = h.body[start].op;
        bool isIf = op0 == POp.If;
        bool isLoop = op0 == POp.Repeat || op0 == POp.Forever;
        if (!isIf && !isLoop) return start;
        int depth = 0;
        for (int i = start; i < h.body.Count; i++)
        {
            var o = h.body[i].op;
            if (isIf)
            {
                if (o == POp.If) depth++;
                else if (o == POp.EndIf) { depth--; if (depth == 0) return i; }
            }
            else
            {
                if (o == POp.Repeat || o == POp.Forever) depth++;
                else if (o == POp.End) { depth--; if (depth == 0) return i; }
            }
        }
        return h.body.Count - 1;
    }

    /// <summary>V2.7 - inclusive START of the span index `i` belongs to: a
    /// closer or an ELSE resolves back to its opener; anything else is its
    /// own span. An orphan marker resolves to itself.</summary>
    static int SpanStart(PHat h, int i)
    {
        var op = h.body[i].op;
        bool toIf = op == POp.Else || op == POp.EndIf;
        bool toLoop = op == POp.End;
        if (!toIf && !toLoop) return i;
        int depth = 0;
        for (int k = i - 1; k >= 0; k--)
        {
            var o = h.body[k].op;
            if (toIf)
            {
                if (o == POp.EndIf) depth++;
                else if (o == POp.If) { if (depth == 0) return k; depth--; }
            }
            else
            {
                if (o == POp.End) depth++;
                else if (o == POp.Repeat || o == POp.Forever)
                { if (depth == 0) return k; depth--; }
            }
        }
        return i;
    }

    /// <summary>V2.7 - what the row's X means now. Deleting one marker used
    /// to leave its partner orphaned somewhere off-screen; X on ANY part of a
    /// C-block now takes the whole span, exactly like the off-list drag has
    /// always done. ELSE is the exception - it can leave alone and the IF is
    /// still valid without it.</summary>
    public void DeleteBlock(PHat h, int i)
    {
        if (h == null || i < 0 || i >= h.body.Count) return;
        if (h.body[i].op == POp.Else) { h.body.RemoveAt(i); return; }
        int a = SpanStart(h, i);
        int e = SpanEnd(h, a);
        if (e < a) e = a;
        h.body.RemoveRange(a, e - a + 1);
    }

    /// <summary>V2.7 - does the IF opening at `open` already carry an ELSE at
    /// its OWN level? (a nested IF's ELSE does not count)</summary>
    static bool SpanHasElse(PHat h, int open)
    {
        int end = SpanEnd(h, open);
        int depth = 0;
        for (int k = open + 1; k <= end && k < h.body.Count; k++)
        {
            var o = h.body[k].op;
            if (o == POp.If) depth++;
            else if (o == POp.EndIf) depth--;
            else if (o == POp.Else && depth == 0) return true;
        }
        return false;
    }

    /// <summary>V2.7 - drop an ELSE in just before the IF's own END IF.</summary>
    public void InsertElse(PHat h, int open)
    {
        if (h == null || open < 0 || open >= h.body.Count) return;
        if (h.body[open].op != POp.If || SpanHasElse(h, open)) return;
        int end = SpanEnd(h, open);
        if (end >= h.body.Count || h.body[end].op != POp.EndIf) return;
        h.body.Insert(end, PBlock.MkElse());
    }

    public void MoveHat(int from, int to)
    {
        if (from < 0 || from >= prog.hats.Count) { Refresh(); return; }
        var h = prog.hats[from];
        prog.hats.RemoveAt(from);
        if (to > from) to--;
        prog.hats.Insert(Mathf.Clamp(to, 0, prog.hats.Count), h);
        MarkDirty();
    }

    public void MoveBlockSpan(int fromHat, int fromIdx, int toHat, int toIdx)
    {
        if (fromHat < 0 || fromHat >= prog.hats.Count) { Refresh(); return; }
        var src = prog.hats[fromHat];
        if (fromIdx < 0 || fromIdx >= src.body.Count) { Refresh(); return; }
        if (toHat < 0 || toHat >= prog.hats.Count) { Refresh(); return; }
        int end = SpanEnd(src, fromIdx), len = end - fromIdx + 1;
        // into its own span (or exactly where it already is): no-op
        if (toHat == fromHat && toIdx >= fromIdx && toIdx <= end + 1) { Refresh(); return; }
        var span = src.body.GetRange(fromIdx, len);
        src.body.RemoveRange(fromIdx, len);
        if (toHat == fromHat && toIdx > end) toIdx -= len;
        var dst = prog.hats[toHat];
        dst.body.InsertRange(Mathf.Clamp(toIdx, 0, dst.body.Count), span);
        MarkDirty();
    }

    public void DeleteHatAt(int i)
    {
        if (i < 0 || i >= prog.hats.Count) { Refresh(); return; }
        prog.hats.RemoveAt(i);
        MarkDirty();
    }

    /// <summary>Drag-off delete. Unlike the row ✕ (which removes ONE marker
    /// and lets Validate complain), deleting an IF this way takes its whole
    /// span — you dragged the C-block off, not one bracket.</summary>
    public void DeleteBlockSpanAt(int hat, int idx)
    {
        if (hat < 0 || hat >= prog.hats.Count) { Refresh(); return; }
        var h = prog.hats[hat];
        if (idx < 0 || idx >= h.body.Count) { Refresh(); return; }
        int end = SpanEnd(h, idx);
        h.body.RemoveRange(idx, end - idx + 1);
        MarkDirty();
    }

    public void InsertHatAt(int i)
    {
        if (prog.hats.Count >= RobotProgram.MAX_HATS)
        { status.text = "too many hats (" + RobotProgram.MAX_HATS + " max)"; status.color = new Color(1f, 0.75f, 0.3f); RebuildList(); return; }
        var h = new PHat { name = "" };
        h.when.Add(PCondTerm.Always());
        h.body.Add(PBlock.MkMove(70f, 1f, 0));
        prog.hats.Insert(Mathf.Clamp(i, 0, prog.hats.Count), h);
        MarkDirty();
    }

    public void InsertBlockAt(int hat, int idx)
    {
        if (hat < 0 || hat >= prog.hats.Count) { Refresh(); return; }
        if (prog.BlockCount() >= RobotProgram.MAX_BLOCKS)
        { status.text = "too many blocks (" + RobotProgram.MAX_BLOCKS + " max)"; status.color = new Color(1f, 0.75f, 0.3f); RebuildList(); return; }
        var h = prog.hats[hat];
        h.body.Insert(Mathf.Clamp(idx, 0, h.body.Count), PBlock.MkMove(70f, 1f, 0));
        MarkDirty();
    }
}
}
