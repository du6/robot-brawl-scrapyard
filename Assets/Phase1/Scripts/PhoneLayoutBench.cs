// ===========================================================================
// PhoneLayoutBench.cs — THE PHONE'S GEOMETRY, ON A MACHINE WITH NO PHONE.
//
// Born 2026-09-13, after four mobile layout defects reached owen's phone in
// one day and every existing bench stayed green through all four. The reason
// they stayed green is not that they check the wrong things - it is that they
// check the right things AT THE WRONG SCALE.
//
// ---- WHAT HEADLESS ACTUALLY REPORTS (measured, not assumed) ---------------
// `-batchmode -nographics` on this Mac, 2026-09-13:
//     Screen 640x480, dpi 266, safeArea = the whole screen,
//     Application.isMobilePlatform = False, SystemInfo.deviceType = Desktop.
// So `PhysicalTouchSizing` is FALSE and TouchRow()/FontUnits() take the
// DESKTOP branch: a 52-unit row and near-1:1 type. Nothing about that screen
// resembles the one the bugs live on.
//
// Two things were tried and DO NOT WORK, so nobody need try them again:
//   - Screen.SetResolution(932, 430, false) in play mode: Screen stayed
//     640x480, measured across nine frames.
//   - `-screen-width 932 -screen-height 430` on the editor command line:
//     Screen stayed 640x480.
// The headless screen is immovable, and a ScreenSpaceOverlay canvas derives
// its rect from that screen, so its aspect is locked at 4:3. Hence the two
// forcings below.
//
// ---- TWO PATHS, AND WHY BOTH PASSES EXIST ---------------------------------
// THE SIZING CODE HAS TWO BRANCHES AND THE ONE A PLAYER GETS DEPENDS ON THE
// PLATFORM, SO A BENCH THAT MEASURES ONLY ONE IS A FALSE GREEN ON THE OTHER.
// `PhysicalTouchSizing` is HARD-FALSE under WebGL (MobileBuilderUI.GarageUX.cs
// - browsers expose CSS pixels and the reported DPI is not physical DPI), so:
//
//   pass "web"  DesktopRow()       = 52 * dpr / scaleFactor
//               DesktopFontUnits() = ceil(max(14, 1.1*pt) * dpr / scaleFactor)
//               THIS REPO SHIPS THIS ONE. Scrapyard is a WebGL build.
//               932x430 CSS pt; the template caps devicePixelRatio at 2
//               (index.html:276) so the framebuffer is 1864x860, scaleFactor
//               1.3189, the canvas 1413x652 units and one unit 0.659 CSS px.
//               A touch row is 52*2/1.3189 = 78.9 units = 52 CSS px.
//
//   pass "ios"  TouchRow()  = clamp(44/163 * dpi / scaleFactor, 34, 110)
//               FontUnits() = pt * (dpi/163) / scaleFactor
//               THE PARENT REPO SHIPS THIS ONE (Bolt & Blade, iOS). Kept
//               because those numbers are real for that build, and deleting
//               the pass would lose the only cover it has.
//               2532x1170 at 460 dpi -> scaleFactor 1.7929, canvas 1412x653,
//               a touch row 69.3 units = 44.0 pt.
//
// 78.9 units and 69.3 units are the same finger on two platforms. They are
// not a discrepancy to reconcile - they are two different arithmetics, and
// the reason each pass carries its platform in every line it logs.
//
// ---- THE iOS PHONE, AND WHERE ITS NUMBERS COME FROM -----------------------
// owen's iPhone in landscape, the device every one of these defects was seen
// on (its pixel size and dpi are recorded in MobileBuilderUI's R7/R9 notes):
//     2532 x 1170 px at 460 dpi
//     CanvasScaler: reference 1280x720, MatchWidthOrHeight 0.5
//     scaleFactor = sqrt(2532/1280 * 1170/720) = 1.793
//     canvas      = 2532/1.793 x 1170/1.793   = 1412 x 653 UNITS
//     TouchRow    = clamp(44/163 * 460 / 1.793, 34, 110) = 69.3 units
//     FontUnits   = pt * (460/163) / 1.793               = pt * 1.574
// Read the row back the other way as a check, because I got it wrong once:
// 69.3 units * 1.793 = 124.2 px, / 460 dpi = 0.270 in, * 163 = 44.0 pt. The
// clamp is not in play - 69.3 sits well inside [34, 110].
//
// ⚠ THE BRIEF FOR THIS BENCH SAID "about 932x430 canvas units" WITH "about a
// 90-unit row", AND THOSE TWO NUMBERS ARE NOT THE SAME SCREEN. 932x430 is the
// iPhone's size in POINTS; the canvas is not 1:1 with points, it is pixels
// over the scale factor, so the same screen is 1412x653 UNITS and a finger is
// 69.3 of them, not 90. Taken literally the brief's pair packs rows into 21%
// of the screen height where the device packs them into 11% - roughly twice
// as harsh - and a harsher viewport does not find more real defects, it
// manufactures fake ones. The constants below are the device's, with the
// arithmetic written out above so the next person can check it rather than
// trust it.
//
// ---- WHAT IS FORCED, AND THE ONE THING THAT IS NOT FAITHFUL ---------------
//   1. MobileBuilderUI.forcedRowUnits / forcedFontScale - the row and the type
//      (and, through PhysicalTouchSizing, the whole phone branch: no UI-scale
//      button, no desktop type register, the phone's dock-open rule).
//   2. The canvas is taken out of ScreenSpaceOverlay and given the phone's
//      rect directly. UGUI layout - anchors, layout groups, fitters, masks -
//      is driven entirely by the RectTransform, so every measurement below is
//      the layout the phone gets. What is NOT faithful in WorldSpace is
//      anything that maps canvas units back to SCREEN pixels: OverUI(), the
//      GraphicRaycaster, Screen-derived hit tests. This bench therefore never
//      synthesises a pointer - it fires onClick on the real control, which is
//      this project's rule anyway (CLAUDE.md), and it asserts nothing about
//      hit testing. TouchSmoke still owns that half.
//
// ---- THE SAFE AREA: COVERED NOW, AND IT WAS NOT BEFORE --------------------
// Both passes pose as a NOTCHED phone, via SafeAreaWeb's forced insets. This
// was impossible until SafeAreaWeb existed: Screen.safeArea is the whole
// screen in a WebGL build, so every safe-area mechanism computed zero and
// PlaytestBench.CheckSafeArea - the one assertion whose entire job is
// catching a control under the notch - could not fail on the platform this
// game ships to. A check that cannot fail is not cover.
//
// ---- PREDICTIONS, WRITTEN BEFORE THE FIRST RUN (house rule 6) -------------
//   a. The dock's screens (BUILD, ROBOTS, SHOP) PASS the touch-row check:
//      every row in MobileBuilderUI is sized from TouchRow().
//   f. (added with the modal gate) The gate FAILS on both passes until the
//      product hides the three lines - it is asserting a fix that has been
//      asked for, not describing one that landed. A gate failure with the
//      CONTROL lines passing means the requirement is simply not built yet.
//   e. (added before the two-pass split) The WEB pass finds FEWER touch-row
//      failures than the iOS pass, because its row is 78.9 units against
//      69.3 - a control that is short by a fixed 4 units of padding is short
//      on both, but anything sized from a literal is relatively worse on the
//      pass with the bigger row.
//   b. The save card PASSES all three: it was rebuilt as a self-sizing stack
//      on 2026-09-13 for exactly this complaint.
//   c. The map HUD's sign-in and board panels FAIL the touch-row check: they
//      are built once from the literal `MapHudUI.ROW = 44f` and never
//      re-sized, so on the phone they are 44 units - 28 pt - where a
//      finger needs 69.3 units, i.e. 44 pt.
//   d. The map HUD's literal font sizes (13..22 units, never FontUnits) are
//      the same defect one layer down. Measured and reported, not asserted -
//      the brief names three invariants and a fourth would muddy the count.
//
// OWNER STATE IS SACRED: Career.Data is swapped for a fresh career, autosave
// is held (counted), and everything - including the forced metrics - is put
// back in a finally. Run: BatchSmoke.Phone.
//
// ---- WHAT THE TOUR REACHES, AND ONE THING IT WILL NOT --------------------
// BUILD (plain, and with the material sheet open), ROBOTS (plain, and in
// DRAFT mode), SHOP (collapsed, and with a part expanded so its BUY rows
// render), both faces of the save card, and the map HUD's chips, objective,
// encounter card, board and sign-in.
//
// ⚠ DELIBERATELY NOT REACHED: the ARENA account surface. It carries the same
// single-row-padded-inward container as the rest, and Scrapyard HIDES that
// tab - so covering it would mean forcing open a screen this fork does not
// ship. A bench that covers a surface nobody can see is worse than no cover,
// because it reads as cover. That site stays verified by inspection, on
// purpose, and the parent repo is where it should be measured.
//
// ---- WHY THERE ARE CONTROL LEGS, AND WHERE ------------------------------
// Three assertions here are about something being ABSENT - the toast hidden
// behind a modal, no tap target under the notch. An absence check passes
// perfectly against a screen where the thing never existed, which is a false
// green wearing a new costume, so each one is paired with a leg proving the
// thing is there to be absent:
//   the modal gate   <- a toast IS shown, and the objective IS up, with no
//                       modal open (house rule 4, and the reason the fixture
//                       posts a toast rather than hoping for one)
//   the notch check  <- a phone with NO cut-out pays nothing for one, so an
//                       unconditional inset cannot pass by being applied
//                       everywhere
//   the forced row   <- the product's own TouchRow() equals the row being
//                       swept against, on BOTH canvases
//   the rotation leg <- the metrics change really moved the row, so "the row
//                       followed" cannot pass by nothing having happened
// If you add a fourth absence check, add its leg in the same commit.
//
// ⚠ EVERY LINE THIS BENCH LOGS BEGINS WITH ITS PASS ("web" or "ios"). A
// failure that does not name a platform is a failure you cannot act on,
// because the fix for one path is frequently not the fix for the other.
// ===========================================================================
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RobotBrawl.Phase0
{
public class PhoneLayoutBench : MonoBehaviour
{
    // TWO PATHS, TWO SETS OF NUMBERS. Derived in the header; do not round
    // these to look tidy and do not merge them - they really do disagree.
    //
    // WEB, which is what THIS repo ships to a phone (WebGL). 932x430 CSS pt,
    // the template caps devicePixelRatio at 2 (index.html:276) -> framebuffer
    // 1864x860 -> scaleFactor 1.3189 -> canvas 1413x652 units.
    public const float WEB_W = 1413f;
    public const float WEB_H = 652f;
    public const float WEB_SF = 1.31887f;
    public const float WEB_DPR = 2f;
    // iOS, which the PARENT repo ships. 2532x1170 at 460 dpi.
    public const float IOS_W = 1412f;
    public const float IOS_H = 653f;
    public const float IOS_SF = 1.79289f;
    public const float IOS_ROW = 69.3f;
    public const float IOS_FONT = 1.574f;

    // THE NOTCH AND THE HOME INDICATOR. One physical phone, two pixel units,
    // because SafeAreaWeb reports in whatever units Screen.width is on that
    // platform. owen's landscape iPhone, measured and recorded in
    // MobileBuilderUI's R9 note: 141 device px bitten out of each side of
    // 2532, and 63 px off the bottom of 1170.
    //   ios: device px as-is                      -> 141 / 63
    //   web: 141 device px / dpr 3 = 47 CSS px, and the framebuffer runs at
    //        dpr 2, so 47 * 2 = 94 framebuffer px; 63 / 3 * 2 = 42.
    // Same strip of glass both times; 94/1.31887 = 71.3 units and
    // 141/1.79289 = 78.6 units are the same notch in two coordinate systems.
    public const float IOS_INSET_SIDE = 141f, IOS_INSET_BOTTOM = 63f;
    public const float WEB_INSET_SIDE = 94f, WEB_INSET_BOTTOM = 42f;

    /// <summary>The WEB row, from the shipped formula rather than a literal:
    /// DesktopRow() is `52 * browserPixelRatio * userUiScale / scaleFactor`
    /// (MobileBuilderUI.GarageUX.cs). 52 * 2 / 1.31887 = 78.9 units, which is
    /// 52 CSS px - comfortably over the 44 pt floor, and a DIFFERENT number
    /// from the iOS path's 69.3. Computed here so that if the product formula
    /// changes, this bench's expectation moves with it instead of quietly
    /// asserting last month's arithmetic.</summary>
    public static float WebRow { get { return 52f * WEB_DPR * 1f / WEB_SF; } }

    // A hairline. Rect maths in units accumulates a fraction through a layout
    // group's rounding, and the existing SaveDialogOverlapUnits check uses the
    // same 1-unit figure. This is a float tolerance, not a relaxed threshold:
    // 1 unit on this phone is 1.79 px, about a third of a hair.
    const float TOL = 1f;

    public static PhoneLayoutBench Run()
    { return new GameObject("phone_layout_bench").AddComponent<PhoneLayoutBench>(); }

    public int passed, failed;
    public bool finished;
    public string report = "";
    readonly List<string> log = new List<string>();

    void Check(bool ok, string what)
    {
        if (ok) passed++; else failed++;
        log.Add((ok ? "PASS  " : "FAIL  ") + what);
    }
    void Note(string what) { log.Add("NOTE  " + what); }

    static Button Btn(string prefix)
    {
        foreach (var b in Object.FindObjectsByType<Button>(FindObjectsSortMode.None))
        {
            if (!b.gameObject.activeInHierarchy) continue;
            var t = b.GetComponentInChildren<Text>();
            if (t != null && t.text.StartsWith(prefix)) return b;
        }
        return null;
    }

    // ---- the phone's geometry, forced -------------------------------------

    /// <summary>Give a canvas the phone's rect. See the header for why this
    /// cannot be done by moving Screen.</summary>
    static void MakePhoneCanvas(Canvas c, float w, float h, float sf)
    {
        if (c == null) return;
        var scaler = c.GetComponent<CanvasScaler>();
        if (scaler != null) scaler.enabled = false;   // it would fight us every Update
        c.renderMode = RenderMode.WorldSpace;
        // ⚠ scaleFactor MATTERS on the web pass and is not decoration. With the
        // CanvasScaler off, Canvas.scaleFactor is ours to set, and DesktopRow()
        // and DesktopFontUnits() both DIVIDE BY IT - so a canvas given the
        // phone's rect but the editor's scale factor would report a phone-shaped
        // screen with desktop-sized rows on it. Setting the rect without this
        // line is the subtle way to get a false green out of this bench.
        c.scaleFactor = sf;
        var rt = c.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(w, h);
        rt.localScale = Vector3.one;
        rt.position = Vector3.zero;
        rt.rotation = Quaternion.identity;
    }

    static IEnumerable<Canvas> PhoneCanvases()
    {
        foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (!c.isRootCanvas) continue;
            if (c.name == "mobile_builder_canvas" || c.name == "map_hud_canvas") yield return c;
        }
    }

    // ---- the three invariants ---------------------------------------------
    //
    // All three walk the LIVE hierarchy rather than a named list of controls,
    // because a named list is how the 44 pt floor check in the other project
    // ended up covering six controls and missing four whole tabs (CLAUDE.md).
    // A control added tomorrow is measured tomorrow, for free.

    /// <summary>A child's OWN box, expressed in its parent's space.
    ///
    /// ⚠ NOT CalculateRelativeRectTransformBounds, and the first draft of this
    /// bench used it and was wrong. That call unions in every DESCENDANT, so a
    /// panel containing a scroller inherits the scroller's whole content: it
    /// reported the dock "spilling 1680 units" out of the canvas on the SHOP
    /// tab, when the dock's own rect sits neatly inside it and the 1680 units
    /// were the shop catalogue behind a mask, exactly where it belongs. A
    /// descendant that really is out of place is caught when the walk reaches
    /// ITS parent, one level down, which is the level that can say so.</summary>
    static Rect Own(RectTransform parent, RectTransform child, bool drawnExtent)
    {
        Rect r = drawnExtent ? DrawnRect(child) : child.rect;
        var p = new Vector3[4];
        p[0] = new Vector3(r.xMin, r.yMin); p[1] = new Vector3(r.xMin, r.yMax);
        p[2] = new Vector3(r.xMax, r.yMax); p[3] = new Vector3(r.xMax, r.yMin);
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        for (int i = 0; i < 4; i++)
        {
            var v = parent.InverseTransformPoint(child.TransformPoint(p[i]));
            x0 = Mathf.Min(x0, v.x); y0 = Mathf.Min(y0, v.y);
            x1 = Mathf.Max(x1, v.x); y1 = Mathf.Max(y1, v.y);
        }
        return new Rect(x0, y0, x1 - x0, y1 - y0);
    }

    /// <summary>What a label actually PAINTS, not the box it was given.
    ///
    /// A Text's rect is a slot, and MkText hands out generous ones: the map
    /// HUD's chip gives its name the top half of the chip and its distance the
    /// bottom half, each offset 2 units into the other's territory. The rects
    /// therefore overlap by 4 units at every row height - 44 or 110 - while
    /// the glyphs, centred in their slots, are 25 units apart and have never
    /// touched. Measuring the slot reports that as a defect on every screen
    /// with a chip on it. Measuring the paint is both the honest question and
    /// the one a player can see the answer to.</summary>
    static Rect DrawnRect(RectTransform rt)
    {
        var r = rt.rect;
        var t = rt.GetComponent<Text>();
        if (t == null) return r;
        float w = Mathf.Min(r.width, Mathf.Max(0f, t.preferredWidth));
        float h = Mathf.Min(r.height, Mathf.Max(0f, t.preferredHeight));
        int a = (int)t.alignment;                 // 0..8, row = a/3 (upper/middle/lower)
        float x = a % 3 == 0 ? r.xMin : a % 3 == 1 ? r.center.x - w * 0.5f : r.xMax - w;
        float y = a / 3 == 0 ? r.yMax - h : a / 3 == 1 ? r.center.y - h * 0.5f : r.yMin;
        return new Rect(x, y, w, h);
    }

    static bool IsBox(RectTransform rt)
    {
        if (rt == null || !rt.gameObject.activeInHierarchy) return false;
        if (rt.GetComponent<Selectable>() != null) return true;
        var g = rt.GetComponent<Graphic>();
        return g != null && g.enabled && g.color.a > 0.02f;
    }

    /// <summary>Scroll furniture: a scrollbar, a fade, a chevron. These are
    /// DRAWN OVER the list on purpose - the whole point of AddListOverflow is
    /// to announce the cliff at the edge of the content - and the vertical and
    /// horizontal bars cross at the corner the way they do in every scroller
    /// ever built. A scrollbar is also already out of the tap-target check for
    /// the same reason: it indicates position beside a list that is dragged
    /// directly, it is not something a finger is asked to grab.</summary>
    static bool IsScrollFurniture(RectTransform rt)
    {
        if (rt.GetComponent<Scrollbar>() != null) return true;
        return rt.GetComponentInParent<Scrollbar>() != null;
    }

    /// <summary>Something drawn rather than read or touched - it takes no
    /// pointer, it carries no type.
    ///
    /// Two kinds live here and both are deliberate overlays. The compass
    /// needle is a bar, a rotated diamond tip and a centre dot, and they
    /// overlap BECAUSE that is what makes it an arrow. The list fade and the
    /// edge chevron are drawn ON TOP of their viewport on purpose - announcing
    /// the cliff at the edge of scrollable content is the entire job of
    /// AddListOverflow. Neither is a collision, and both recur on every screen
    /// that has a list or a chip on it, so leaving them in buries the real
    /// finding under a dozen copies of a decision somebody made on purpose.
    ///
    /// ⚠ WHAT THIS GIVES UP, SAID PLAINLY: a decoration that COVERS a control
    /// is now invisible to this check. That is the TEST DRIVE defect (its
    /// bottom 55% eaten by a transparent viewport, CLAUDE.md) and it is worth
    /// naming that no bench in this project sees it - but a sibling sweep
    /// never could, because that viewport and the buttons it ate were not
    /// siblings. Nothing is lost here that was ever covered.</summary>
    static bool IsDecoration(RectTransform rt)
    {
        if (rt.GetComponent<Selectable>() != null) return false;
        if (rt.GetComponent<Text>() != null) return false;
        var g = rt.GetComponent<Graphic>();
        return g != null && !g.raycastTarget;
    }

    static bool Contains(Rect outer, Rect inner)
    {
        return outer.xMin <= inner.xMin + TOL && outer.yMin <= inner.yMin + TOL
            && outer.xMax >= inner.xMax - TOL && outer.yMax >= inner.yMax - TOL;
    }

    /// <summary>A full-parent backdrop - scrim, panel fill, input border. It
    /// legitimately lies under everything, so it is not an overlap partner.</summary>
    static bool IsBackdrop(Rect r, Rect parent)
    {
        float a = Mathf.Max(1f, r.width * r.height), pa = Mathf.Max(1f, parent.width * parent.height);
        return a >= pa * 0.95f;
    }

    /// <summary>I1 - no two visible sibling controls overlap.
    ///
    /// CONTAINMENT IS NOT OVERLAP: a label inside its button, a fill behind a
    /// row, an input's 2px border along its bottom edge are all one control
    /// drawn in layers, and flagging them would bury the real finding in a
    /// hundred of them. What is flagged is a PARTIAL intersection between two
    /// siblings, neither of which contains the other, neither of which is
    /// scroll furniture or decoration (see below) - which leaves exactly the
    /// shape of "the note ran through the buttons".</summary>
    static List<string> Overlaps(Transform root)
    {
        var bad = new List<string>();
        foreach (var p in root.GetComponentsInChildren<RectTransform>(false))
        {
            if (p.childCount < 2) continue;
            var kids = new List<RectTransform>();
            for (int i = 0; i < p.childCount; i++)
            {
                var c = p.GetChild(i) as RectTransform;
                if (IsBox(c)) kids.Add(c);
            }
            if (kids.Count < 2) continue;
            var pr = p.rect;
            var rects = new List<Rect>();
            foreach (var k in kids) rects.Add(Own(p, k, true));
            for (int i = 0; i < kids.Count; i++)
            {
                if (IsBackdrop(rects[i], pr) || IsScrollFurniture(kids[i])) continue;
                for (int j = i + 1; j < kids.Count; j++)
                {
                    if (IsBackdrop(rects[j], pr) || IsScrollFurniture(kids[j])) continue;
                    if (IsDecoration(kids[i]) || IsDecoration(kids[j])) continue;
                    float w = Mathf.Min(rects[i].xMax, rects[j].xMax) - Mathf.Max(rects[i].xMin, rects[j].xMin);
                    float h = Mathf.Min(rects[i].yMax, rects[j].yMax) - Mathf.Max(rects[i].yMin, rects[j].yMin);
                    if (w <= TOL || h <= TOL) continue;
                    if (Contains(rects[i], rects[j]) || Contains(rects[j], rects[i])) continue;
                    bad.Add(Path(p) + ": " + kids[i].name + " x " + kids[j].name
                          + " overlap " + w.ToString("0.0") + "x" + h.ToString("0.0") + " units");
                }
            }
        }
        return bad;
    }

    /// <summary>I2 - nothing extends outside its parent's rect.
    ///
    /// A ScrollRect's CONTENT is exempt against its own viewport, because
    /// overflowing a scroller is what a scroller is for; TouchSmoke already
    /// owns the harder half of that (overflow across an axis that does NOT
    /// scroll). Everything else, including every row inside that content, is
    /// measured.
    ///
    /// This one measures the RECT, not the drawn extent, where I1 measures the
    /// drawn extent - on purpose. Two controls on top of each other is a
    /// question about PAINT, so a label's slot does not count; a box outside
    /// its container is a question about LAYOUT, and a slot placed off the
    /// edge is the fault whether or not this particular string fills it.
    /// ⚠ The limit that leaves: glyphs escaping their own rect (a Text set to
    /// HorizontalWrapMode.Overflow) are invisible to both. Nothing in this
    /// project measures those yet.</summary>
    static List<string> Overflows(Transform root)
    {
        var bad = new List<string>();
        foreach (var p in root.GetComponentsInChildren<RectTransform>(false))
        {
            var sr = p.GetComponent<ScrollRect>();
            var pr = p.rect;
            if (pr.width < 1f || pr.height < 1f) continue;
            for (int i = 0; i < p.childCount; i++)
            {
                var c = p.GetChild(i) as RectTransform;
                if (!IsBox(c)) continue;
                if (sr != null && (c == sr.content || c == sr.viewport)) continue;
                // A rotated rect's axis-aligned box is not its shape: the
                // needle's tip is a 7x7 square turned 45 degrees, so its
                // corners reach 1.9 units past a box it visually fits inside.
                if (Quaternion.Angle(c.localRotation, Quaternion.identity) > 0.5f) continue;
                var r = Own(p, c, false);
                float over = Mathf.Max(Mathf.Max(pr.xMin - r.xMin, r.xMax - pr.xMax),
                                       Mathf.Max(pr.yMin - r.yMin, r.yMax - pr.yMax));
                if (over > TOL)
                    bad.Add(Path(p) + " > " + c.name + " spills " + over.ToString("0.0") + " units");
            }
        }
        return bad;
    }

    /// <summary>I3 - every control meant to be tapped is at least one touch
    /// row tall.
    ///
    /// Scrollbars are excluded and that is not a loophole: a scrollbar in this
    /// UI is a position INDICATOR beside a list that is dragged directly, not
    /// something a finger is asked to grab. Everything a player is asked to
    /// hit - buttons, tab chips, the dock handle, text fields - is in.</summary>
    static List<string> ShortTargets(Transform root, float row)
    {
        var bad = new List<string>();
        foreach (var s in root.GetComponentsInChildren<Selectable>(false))
        {
            if (s is Scrollbar || s is Slider) continue;
            var rt = s.GetComponent<RectTransform>();
            if (rt == null) continue;
            var r = rt.rect;
            if (r.width < 1f || r.height < 1f) continue;   // a collapsed control is not a short one
            if (r.height < row - TOL)
            {
                // Report the CONTAINER beside the control. The fix for this
                // defect is "container = row + its own vertical padding", so
                // the two numbers together say which half is wrong: a
                // container still at `row` means the sizing was not changed; a
                // container at row+pad with a short control means something
                // between the LayoutElement and the button is eating the
                // difference. Without both, every failure looks identical and
                // the reader has to go measure it by hand.
                var par = rt.parent as RectTransform;
                string ctx = "";
                if (par != null)
                {
                    var le = par.GetComponent<LayoutElement>();
                    var hv = par.GetComponent<HorizontalOrVerticalLayoutGroup>();
                    ctx = "  [container " + par.name + " rect " + par.rect.height.ToString("0.0")
                        + (le != null ? ", LayoutElement pref " + le.preferredHeight.ToString("0.0") : ", no LayoutElement")
                        + (hv != null ? ", pad " + hv.padding.top + "/" + hv.padding.bottom
                                      + ", controlH " + hv.childControlHeight : "")
                        + "]";
                }
                bad.Add(Path(rt) + " is " + r.height.ToString("0.0") + " units, needs " + row.ToString("0") + ctx);
            }
        }
        return bad;
    }

    static string Path(Transform t)
    {
        string s = t.name;
        for (var p = t.parent; p != null; p = p.parent)
        {
            s = p.name + "/" + s;
            if (p.GetComponent<Canvas>() != null && p.GetComponent<Canvas>().isRootCanvas) break;
        }
        return s;
    }

    static string Join(List<string> l, int max)
    {
        if (l.Count <= max) return string.Join(" | ", l.ToArray());
        return string.Join(" | ", l.GetRange(0, max).ToArray()) + " | ...and " + (l.Count - max) + " more";
    }

    /// <summary>I4 - no tap target lands under the notch or in the home
    /// indicator.
    ///
    /// The insets are the SAME numbers the product reads (SafeAreaWeb, forced
    /// to a landscape iPhone's), divided by the pass's scale factor to reach
    /// canvas units. Until SafeAreaWeb existed this could not be measured on
    /// the web at all: Screen.safeArea is the WHOLE SCREEN in a WebGL build,
    /// so PlaytestBench.CheckSafeArea - the assertion whose entire job is this
    /// - could not fail on the platform that ships. A check that cannot fail
    /// is not cover, which is why it is asserted here rather than noted.</summary>
    /// <summary>A control's rect in canvas units, CLIPPED to every mask it sits
    /// inside - the same thing PlaytestBench.VisibleRect does, and for the same
    /// reason.
    ///
    /// ⚠ WITHOUT THE CLIP THIS CHECK INVENTS DEFECTS. A shop row scrolled below
    /// its viewport still has a rect, and that rect sits in the home-indicator
    /// band at the bottom of the screen - so an unclipped test reports a row
    /// nobody can see as a row under the home indicator. Only the part that is
    /// actually on screen can be under anything.</summary>
    static Rect VisibleInCanvas(RectTransform rt, RectTransform canvasRt)
    {
        Rect r = ToCanvas(rt, canvasRt);
        for (var p = rt.parent as RectTransform; p != null; p = p.parent as RectTransform)
        {
            if (p.GetComponent<RectMask2D>() == null && p.GetComponent<Mask>() == null) continue;
            Rect m = ToCanvas(p, canvasRt);
            float x0 = Mathf.Max(r.xMin, m.xMin), y0 = Mathf.Max(r.yMin, m.yMin);
            float x1 = Mathf.Min(r.xMax, m.xMax), y1 = Mathf.Min(r.yMax, m.yMax);
            r = new Rect(x0, y0, Mathf.Max(0f, x1 - x0), Mathf.Max(0f, y1 - y0));
            if (r.width <= 0f || r.height <= 0f) return r;   // fully clipped away
        }
        return r;
    }

    static Rect ToCanvas(RectTransform rt, RectTransform canvasRt)
    {
        var c = new Vector3[4]; rt.GetWorldCorners(c);
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        for (int i = 0; i < 4; i++)
        {
            var v = canvasRt.InverseTransformPoint(c[i]);
            x0 = Mathf.Min(x0, v.x); y0 = Mathf.Min(y0, v.y);
            x1 = Mathf.Max(x1, v.x); y1 = Mathf.Max(y1, v.y);
        }
        return new Rect(x0, y0, x1 - x0, y1 - y0);
    }

    static List<string> InInset(Transform root, Rect safe)
    {
        var bad = new List<string>();
        var canvasRt = root.GetComponent<RectTransform>();
        foreach (var sel in root.GetComponentsInChildren<Selectable>(false))
        {
            if (sel is Scrollbar || sel is Slider) continue;
            var rt = sel.GetComponent<RectTransform>();
            if (rt == null || rt.rect.width < 1f || rt.rect.height < 1f) continue;
            var r = VisibleInCanvas(rt, canvasRt);
            if (r.width <= TOL || r.height <= TOL) continue;   // scrolled out of sight
            // Named per side, because the fix differs: a left/right hit is the
            // notch, a bottom hit is the home indicator's swipe strip.
            float l = safe.xMin - r.xMin, rr = r.xMax - safe.xMax;
            float b = safe.yMin - r.yMin, t = r.yMax - safe.yMax;
            string side = ""; float into = 0f;
            if (l > into) { into = l; side = "left (notch)"; }
            if (rr > into) { into = rr; side = "right (notch)"; }
            if (b > into) { into = b; side = "bottom (home indicator)"; }
            if (t > into) { into = t; side = "top"; }
            if (into > TOL)
                bad.Add(Path(rt) + " reaches " + into.ToString("0.0") + " units into the " + side + " inset");
        }
        return bad;
    }

    /// <summary>One screen, four invariants, one line of report each.</summary>
    void Sweep(string screen, Transform root, float row, Rect safe)
    {
        var ov = Overlaps(root);
        Check(ov.Count == 0, screen + ": no two visible sibling controls overlap"
              + (ov.Count == 0 ? "" : " — " + Join(ov, 6)));
        var of = Overflows(root);
        Check(of.Count == 0, screen + ": nothing extends outside its parent"
              + (of.Count == 0 ? "" : " — " + Join(of, 6)));
        var st = ShortTargets(root, row);
        Check(st.Count == 0, screen + ": every tap target is a full touch row (" + row.ToString("0") + " units)"
              + (st.Count == 0 ? "" : " — " + Join(st, 6)));
        var si = InInset(root, safe);
        Check(si.Count == 0, screen + ": every tap target clears the notch and the home indicator"
              + (si.Count == 0 ? "" : " — " + Join(si, 6)));
    }

    /// <summary>THE COUNTERPART TO POSING A NOTCH: a phone with NO cut-out
    /// must not PAY for one.
    ///
    /// Posing a notch proves the inset is applied. On its own that is half a
    /// measurement, and the wrong half to stop at: an inset that is applied
    /// UNCONDITIONALLY also passes it, and would quietly eat 71 units off both
    /// edges of every flat-screen phone and every desktop browser - a bug that
    /// makes the notch check greener the worse it gets. So the leg drops the
    /// insets to zero and asserts the layout MOVES BACK OUT by exactly what
    /// the notch was costing.
    ///
    /// Measured as a DIFFERENCE, which is what makes it exact rather than
    /// approximate: the tab strip's own constant padding - whatever it is, and
    /// it is not under test here - appears in both readings and cancels. What
    /// is left is the part that depends on the cut-out, and that is the part
    /// that has to be conditional.</summary>
    IEnumerator NoNotchLeg(string label, float insetUnits, RectTransform canvasRt)
    {
        var tab = Btn("BUILD");
        if (tab == null) { Check(false, label + ": CONTROL - no BUILD tab to measure the notch against"); yield break; }
        float withNotch = ToCanvas(tab.GetComponent<RectTransform>(), canvasRt).xMin;

        SafeAreaWeb.forcedLeft = SafeAreaWeb.forcedRight = SafeAreaWeb.forcedBottom = SafeAreaWeb.forcedTop = 0f;
        yield return Settle();
        tab = Btn("BUILD");
        float without = tab != null ? ToCanvas(tab.GetComponent<RectTransform>(), canvasRt).xMin : withNotch;
        float moved = withNotch - without;
        Check(Mathf.Abs(moved - insetUnits) < 2f,
              label + ": CONTROL - a phone with no cut-out pays nothing for one (the strip moves out "
              + moved.ToString("0.0") + " units, the notch was " + insetUnits.ToString("0.0") + ")");
        yield return null;
    }

    /// <summary>THE MODAL GATE: while the sign-in or the board is up, the
    /// objective, the toast and the banner must all be HIDDEN.
    ///
    /// ⚠ THIS IS DELIBERATELY NOT A NON-OVERLAP CHECK, and the difference is
    /// the whole point. A non-overlap assertion is satisfied by nudging ONE of
    /// the three lines twenty units and leaving the other two where they are -
    /// which is how this defect comes back the next time a metrics change
    /// moves the bar. It is also wrong on product grounds before geometry
    /// comes into it: coaching somebody toward a treasure chest while they are
    /// typing a password is wrong at any row height.
    ///
    /// The three are read through the HUD's own text seams, which return ""
    /// when the object is inactive - so this bench POPULATES the toast first
    /// and asserts it is visible with no modal up (the control leg below).
    /// Without that leg, a HUD that never renders a toast at all would satisfy
    /// "hidden while a modal is up" perfectly.</summary>
    void GateCheck(string screen, MapHudUI hud)
    {
        bool obj = hud.ObjectiveRect != null && hud.ObjectiveRect.gameObject.activeInHierarchy;
        bool toast = hud.ToastText.Length > 0;
        bool banner = hud.BannerText.Length > 0;
        Check(!obj && !toast && !banner,
              screen + ": the objective, toast and banner are all hidden behind the modal"
              + (!obj && !toast && !banner ? "" :
                 " — still up:" + (obj ? " objective" : "") + (toast ? " toast(\"" + hud.ToastText + "\")" : "")
                 + (banner ? " banner(\"" + hud.BannerText + "\")" : "")));
    }

    /// <summary>The top band and the dock, in one line per screen.
    ///
    /// Not a check - context. DockH() for a list tab is `canvasH - TopInset()
    /// - 8 - HANDLE_H`, so the dock's height and the band's depth are two
    /// halves of one sum, and when they disagree the handle lands on whatever
    /// is above it. Printing both is what turns "tipbar x dockhandle overlap"
    /// from a symptom into an arithmetic error somebody can read.</summary>
    void NoteBand(string screen, MobileBuilderUI ui, RectTransform canvasRt, float row)
    {
        Note(screen + ": canvas " + canvasRt.rect.height.ToString("0.0")
             + " = top cover " + ui.TopCoverUnits.ToString("0.0")
             + " + dock " + ui.TestDockHeight.ToString("0.0")
             + " + handle " + row.ToString("0.0")
             + "  (sum " + (ui.TopCoverUnits + ui.TestDockHeight + row).ToString("0.0") + ")");
    }

    /// <summary>The type size finding (prediction d): measured, reported, not
    /// asserted. Point size = units * scaleFactor / dpi * 163.</summary>
    void MeasureType(string screen, Transform root, float sf, bool physical)
    {
        // The two paths do not share a unit, so neither does this line.
        //   iOS: units -> px (x scaleFactor) -> inches (/ 460 dpi) -> points
        //        (x 163). Apple's floor is 11 pt.
        //   web: units -> framebuffer px (x scaleFactor) -> CSS px (/ dpr).
        //        The dock's own floor is 14 CSS px (DesktopFontUnits), and a
        //        CSS px is close enough to a point on a phone to compare.
        float worst = 9999f; string who = "";
        foreach (var t in root.GetComponentsInChildren<Text>(false))
        {
            if (t.color.a < 0.02f || t.text == null || t.text.Length == 0) continue;
            float v = physical ? t.fontSize * sf / 460f * 163f : t.fontSize * sf / WEB_DPR;
            if (v < worst) { worst = v; who = Path(t.rectTransform); }
        }
        if (who.Length > 0)
            Note(screen + ": smallest live label is " + worst.ToString("0.0")
                 + (physical ? " pt" : " CSS px") + " (" + who
                 + ")  [floor " + (physical ? "11 pt (Apple)" : "14 CSS px (DesktopFontUnits)")
                 + "; measured, not asserted - see the header]");
    }

    // ---- the run -----------------------------------------------------------

    IEnumerator Start()
    {
        var savedData = Career.Data;
        bool savedActive = Career.active;
        bool? savedForcePhone = MobileBuilderUI.autoHideForcePhone;
        bool savedAutoHide = MobileBuilderUI.autoHidePanelOnPick;
        bool savedBootToYard = BuilderManager.bootToYard;
        var hold = Career.SuspendAutosave();
        try
        {
            yield return StartCoroutine(Body());
        }
        finally
        {
            // Every static this bench moved goes back, in the order it was
            // taken. A bench that leaves forcedRowUnits set turns every LATER
            // bench in the same play session into a phone bench without
            // saying so.
            MobileBuilderUI.ClearForcedMetrics();
            SafeAreaWeb.forcedLeft = SafeAreaWeb.forcedRight
                = SafeAreaWeb.forcedTop = SafeAreaWeb.forcedBottom = -1f;   // -1 = not forced
            MobileBuilderUI.autoHideForcePhone = savedForcePhone;
            MobileBuilderUI.autoHidePanelOnPick = savedAutoHide;
            BuilderManager.bootToYard = savedBootToYard;
            Career.Data = savedData;
            Career.active = savedActive;
            hold.Dispose();
            report = string.Join("\n", log.ToArray());
            foreach (var l in log) Debug.Log("[PhoneLayoutBench] " + l);
            Debug.Log(string.Format("[PhoneLayoutBench] RESULT: {0} pass, {1} fail{2}",
                      passed, failed, failed == 0 ? " - ALL GREEN" : " - FIX NEEDED"));
            finished = true;
        }
    }

    IEnumerator Body()
    {
        yield return StartCoroutine(Career_());
        // WEB FIRST, because it is the path this repo actually ships. A green
        // iOS pass on its own would be the "green endpoint, no caller" mistake
        // in layout form: correct arithmetic about a branch no player reaches
        // from THIS build.
        yield return StartCoroutine(Pass("web", WEB_W, WEB_H, WEB_SF, false));
        yield return StartCoroutine(Pass("ios", IOS_W, IOS_H, IOS_SF, true));
    }

    /// <summary>One career, built once and shared by both passes.</summary>
    IEnumerator Career_()
    {
        var ms = GameObject.Find("ModeSelect");
        if (ms != null) Destroy(ms);
        MobileBuilderUI.forceMobileUI = true;
        BuilderManager.bootToYard = false;
        var bm = Object.FindFirstObjectByType<BuilderManager>();
        if (bm == null) bm = new GameObject("BuilderManager").AddComponent<BuilderManager>();

        Career.Data = new CareerData();
        Career.active = true;
        var d = Career.Data;
        d.taskFight = d.taskBolt = d.taskWeld = d.taskBuy = true; d.rescueGranted = true; d.guideDone = true;
        d.stable.Add(new CareerRobot { name = "SCRAPPER", snapshot = BuilderManager.STARTER_SNAPSHOT, program = "" });
        d.activeRobot = 0;
        d.worldSeed = 4242;   // one world every run; a rolled seed makes the yard a coin flip
        Career.TopUpForSnapshot(BuilderManager.STARTER_SNAPSHOT);
        bm.LoadSnapshot(BuilderManager.STARTER_SNAPSHOT);

        // The panel must not fold itself away under us: this bench keeps
        // driving dock controls after a pick. Same hold TouchSmoke takes.
        MobileBuilderUI.autoHideForcePhone = false;
        yield return null;
    }

    /// <summary>One whole tour of the game's screens at one platform's
    /// metrics.
    ///
    /// `physical` picks WHICH SIZING BRANCH runs, and that is the entire point
    /// of having two passes:
    ///   false -> forcedPixelRatio only. PhysicalTouchSizing stays FALSE, so
    ///            TouchRow() falls through to DesktopRow() and FontUnits() to
    ///            DesktopFontUnits() - the branch a phone BROWSER takes,
    ///            because PhysicalTouchSizing is hard-false under WebGL
    ///            (MobileBuilderUI.GarageUX.cs). This is what ships from this
    ///            repo.
    ///   true  -> forcedRowUnits/forcedFontScale. PhysicalTouchSizing is
    ///            forced TRUE, so the dpi arithmetic runs - the branch an iOS
    ///            player takes. That build ships from the PARENT repo, which
    ///            is why this pass is kept rather than deleted.
    /// The dock is destroyed and rebuilt between passes: half these sizes are
    /// baked at build time, so re-using one pass's UI for the other measures a
    /// screen that is half one platform and half the other.</summary>
    IEnumerator Pass(string label, float w, float h, float sf, bool physical)
    {
        MobileBuilderUI.ClearForcedMetrics();
        if (physical)
        {
            MobileBuilderUI.forcedRowUnits = IOS_ROW;
            MobileBuilderUI.forcedFontScale = IOS_FONT;
        }
        else
        {
            MobileBuilderUI.forcedPixelRatio = WEB_DPR;
            MobileBuilderUI.forcedCoarsePointer = true;   // a phone browser: no hover, no UI-scale button
        }
        float row = physical ? IOS_ROW : WebRow;

        // POSE AS A NOTCHED PHONE. Forced before the UI is built, because the
        // insets feed DockH() and the bar stack, not just a final nudge.
        float insetSide = physical ? IOS_INSET_SIDE : WEB_INSET_SIDE;
        float insetBottom = physical ? IOS_INSET_BOTTOM : WEB_INSET_BOTTOM;
        SafeAreaWeb.forcedLeft = insetSide;
        SafeAreaWeb.forcedRight = insetSide;
        SafeAreaWeb.forcedBottom = insetBottom;
        SafeAreaWeb.forcedTop = 0f;      // landscape: the notch is on the sides, not the top
        // The same numbers in canvas units, which is what every rect below is in.
        float iu = insetSide / sf, ib = insetBottom / sf;
        Rect safe = new Rect(-w * 0.5f + iu, -h * 0.5f + ib, w - 2f * iu, h - ib);

        var bm = Object.FindFirstObjectByType<BuilderManager>();
        if (bm == null) bm = new GameObject("BuilderManager").AddComponent<BuilderManager>();
        if (bm.mode == BuilderManager.Mode.Map) { bm.LeaveMap(); yield return null; }

        if (MobileBuilderUI.inst != null) { Destroy(MobileBuilderUI.inst.gameObject); yield return null; }
        // The map HUD bakes panel sizes at Build() too, so it gets the same
        // clean slate. Ensure() rebuilds it on the next EnterMap.
        MapHudUI.Drop();
        yield return null;
        float t0 = Time.realtimeSinceStartup;
        while (!MobileBuilderUI.Active && Time.realtimeSinceStartup - t0 < 6f) yield return null;
        Check(MobileBuilderUI.Active, label + ": the mobile dock attaches");
        var ui = MobileBuilderUI.inst;
        if (ui == null) yield break;
        yield return null; yield return null;

        var dockCanvas = GameObject.Find("mobile_builder_canvas");
        Check(dockCanvas != null, label + ": ...on its own canvas");
        if (dockCanvas == null) yield break;
        MakePhoneCanvas(dockCanvas.GetComponent<Canvas>(), w, h, sf);
        yield return null; yield return null; yield return null;

        var canvasRt = dockCanvas.GetComponent<RectTransform>();
        Check(Mathf.Abs(canvasRt.rect.width - w) < 1f && Mathf.Abs(canvasRt.rect.height - h) < 1f,
              label + ": the canvas is " + w + "x" + h + " units (" +
              canvasRt.rect.width.ToString("0") + "x" + canvasRt.rect.height.ToString("0") + ")");
        // ⚠ THE CHECK THAT STOPS THIS BENCH LYING. It asserts that the row the
        // PRODUCT computed equals the row this pass is about to measure
        // against. If the forcing ever stops reaching the sizing path - a
        // renamed hook, a new early return, a branch that no longer consults
        // it - every sweep below would still run and still pass, against a
        // desktop-sized UI. That is the false green the whole two-pass split
        // exists to prevent, so it is asserted, not noted.
        Check(Mathf.Abs(ui.TouchRowUnits - row) < 0.5f,
              label + ": ...and the product's own touch row is " + row.ToString("0.0")
              + " units (" + ui.TouchRowUnits.ToString("0.0") + ")"
              + (physical ? "  [dpi path]" : "  [52 * dpr / scaleFactor = browser path]"));

        // The phone's rule closes the dock on a screen this short; open it the
        // way a finger does, through the handle's own onClick.
        if (!ui.DockOpen)
        {
            var dh = GameObject.Find("dockhandle");
            var dhb = dh != null ? dh.GetComponent<Button>() : null;
            Check(dhb != null, label + ": a closed dock shows its handle");
            if (dhb != null) { dhb.onClick.Invoke(); yield return null; yield return null; }
        }
        Check(ui.DockOpen, label + ": the dock is open for the sweep");

        // ---- 1. BUILD --------------------------------------------------------
        var bt = Btn("BUILD");
        Check(bt != null, label + ": the BUILD tab is on the strip");
        if (bt != null) { bt.onClick.Invoke(); yield return null; yield return null; }
        yield return Settle();
        Check(ui.TestTab == 0, label + ": BUILD is the live tab");
        Sweep(label + " BUILD", dockCanvas.transform, row, safe);
        MeasureType(label + " BUILD", dockCanvas.transform, sf, physical);
        NoteBand(label + " BUILD", ui, canvasRt, row);
        Note(label + " BUILD: part shelf viewport is " + ui.PaletteViewportUnits.ToString("0.0")
             + " units = " + (ui.PaletteViewportUnits / row).ToString("0.00") + " touch rows");

        // ---- 2. BUILD with the material sheet open ---------------------------
        // Opened through the disclosure button, never through SetMatSheet:
        // the affordance is the thing under test everywhere else in this suite.
        Button discl = null;
        foreach (var b in Object.FindObjectsByType<Button>(FindObjectsSortMode.None))
        {
            if (!b.gameObject.activeInHierarchy) continue;
            var dt = b.GetComponentInChildren<Text>();
            if (dt != null && (dt.text.EndsWith("▴") || dt.text.EndsWith("▾"))) { discl = b; break; }
        }
        Check(discl != null, label + ": the material chip carries its disclosure caret");
        if (discl != null)
        {
            discl.onClick.Invoke();
            yield return Settle();
            Check(ui.MatSheetOpen, label + ": ...and tapping it opens the material sheet");
            Sweep(label + " BUILD + material sheet", dockCanvas.transform, row, safe);
            discl.onClick.Invoke();
            yield return Settle();
        }

        // ---- 3. ROBOTS -------------------------------------------------------
        var rb = Btn("ROBOTS");
        Check(rb != null, label + ": the ROBOTS tab is on the strip");
        if (rb != null)
        {
            rb.onClick.Invoke();
            yield return Settle();
            Check(ui.TestTab == 2, label + ": ROBOTS is the live tab");
            Sweep(label + " ROBOTS", dockCanvas.transform, row, safe);
            MeasureType(label + " ROBOTS", dockCanvas.transform, sf, physical);
            NoteBand(label + " ROBOTS", ui, canvasRt, row);

            // ---- 3b. A METRICS CHANGE ON AN ALREADY-BUILT LIST ------------
            // THE ROTATION CASE, and the one class this bench was blind to in
            // both directions until now. Every other screen in the tour is
            // built AFTER the metrics are forced, so nothing here ever asked
            // the question a real device asks: the interface is standing, and
            // THEN the row changes underneath it.
            //
            // The trigger is not literally a rotation, and that is worth
            // knowing before anyone "fixes" this by rotating something: under
            // MatchWidthOrHeight 0.5 the scale factor is a function of
            // width*height, which a rotation leaves alone - so a rotation
            // changes the canvas SHAPE and not the row. What does move the row
            // on a live interface is the UI-scale button, a browser zoom, a
            // devicePixelRatio change, or switching device in the simulator.
            // Driven here through the same static the product reads, so the
            // relayout path taken is the product's own: MobileBuilderUI.Update
            // watches a signature that includes browserPixelRatio and
            // userUiScale, and calls ApplyTouchSizes when it moves.
            //
            // ⚠ The rows under test are built by RefreshRobots with their
            // height baked into a LayoutElement at build time. Nothing obliges
            // ApplyTouchSizes to revisit them, so this leg failing is a REAL
            // defect on any device whose metrics can change mid-session -
            // which is all of them - not a bench artifact.
            {
                var probe = Btn("LOAD");
                Check(probe != null, label + ": CONTROL - a stable row is on screen to re-measure");
                if (probe != null)
                {
                    float rowBefore = ui.TouchRowUnits;
                    float hBefore = probe.GetComponent<RectTransform>().rect.height;
                    if (physical) MobileBuilderUI.forcedRowUnits = IOS_ROW * 1.3f;
                    else MobileBuilderUI.forcedPixelRatio = WEB_DPR * 1.3f;
                    yield return Settle();
                    yield return Settle();
                    float rowAfter = ui.TouchRowUnits;
                    var probe2 = Btn("LOAD");
                    float hAfter = probe2 != null ? probe2.GetComponent<RectTransform>().rect.height : -1f;
                    // The leg is worthless unless the metric really moved.
                    Check(Mathf.Abs(rowAfter - rowBefore) > 1f,
                          label + ": CONTROL - the metrics change actually moved the row ("
                          + rowBefore.ToString("0.0") + " -> " + rowAfter.ToString("0.0") + ")");
                    // Name the ELEMENT, not just the number. If the re-apply
                    // sweep is not reaching a list, which list it is is the
                    // whole of the actionable information.
                    string who = probe2 != null ? Path(probe2.GetComponent<RectTransform>())
                               : Path(probe.GetComponent<RectTransform>());
                    Check(hAfter >= rowAfter - TOL,
                          label + ": a list row built BEFORE a metrics change follows it (row "
                          + rowBefore.ToString("0.0") + " -> " + rowAfter.ToString("0.0")
                          + ", " + who + " is " + hAfter.ToString("0.0") + ")");
                    if (physical) MobileBuilderUI.forcedRowUnits = IOS_ROW;
                    else MobileBuilderUI.forcedPixelRatio = WEB_DPR;
                    yield return Settle();
                }
            }

            // ---- 3a. ROBOTS IN DRAFT MODE --------------------------------
            // `draftrow` ("DRAFT MODE - everything unlocked") renders only
            // while Career.Drafting, which is activeBlueprint pointing at a
            // real blueprint. It is one of the containers sized to exactly one
            // touch row and then padded inward, and no screen in the tour
            // reached it - so it was real-by-inspection rather than measured.
            var savedBp = Career.Data.activeBlueprint;
            Career.Data.blueprints.Add(new CareerBlueprint { name = "DRAFT SUBJECT", snapshot = BuilderManager.STARTER_SNAPSHOT });
            Career.Data.activeBlueprint = Career.Data.blueprints.Count - 1;
            rb.onClick.Invoke();
            yield return Settle();
            Check(Career.Drafting, label + ": CONTROL - the career is in draft mode");
            Check(GameObject.Find("draftrow") != null,
                  label + ": CONTROL - ...so the DRAFT MODE row is on the ROBOTS tab");
            Sweep(label + " ROBOTS (draft)", dockCanvas.transform, row, safe);
            MeasureType(label + " ROBOTS (draft)", dockCanvas.transform, sf, physical);
            Career.Data.activeBlueprint = savedBp;
            Career.Data.blueprints.RemoveAt(Career.Data.blueprints.Count - 1);
            rb.onClick.Invoke();
            yield return Settle();
        }

        // ---- 4. SHOP ---------------------------------------------------------
        var sb = Btn("SHOP");
        Check(sb != null, label + ": the SHOP tab is on the strip");
        if (sb != null)
        {
            sb.onClick.Invoke();
            yield return Settle();
            Check(ui.TestTab == 3, label + ": SHOP is the live tab");
            Sweep(label + " SHOP", dockCanvas.transform, row, safe);
            MeasureType(label + " SHOP", dockCanvas.transform, sf, physical);
            NoteBand(label + " SHOP", ui, canvasRt, row);

            // ---- 4a. SHOP WITH A PART EXPANDED ---------------------------
            // The collapsed shelf shows only the part HEADS. The material
            // rows under them - `shopmat_*`, each carrying a BUY button - are
            // built for every part but only shown once a head is tapped, so a
            // sweep of the collapsed tab never measures a single buy button.
            // Expanded through the head's own onClick, never through `pr.open`.
            Button head = null;
            foreach (var b in Object.FindObjectsByType<Button>(FindObjectsSortMode.None))
            {
                if (!b.gameObject.activeInHierarchy) continue;
                if (b.gameObject.name.StartsWith("shophead_")) { head = b; break; }
            }
            Check(head != null, label + ": SHOP has a part head to expand");
            if (head != null)
            {
                head.onClick.Invoke();
                yield return Settle();
                int buys = 0;
                foreach (var b in Object.FindObjectsByType<Button>(FindObjectsSortMode.None))
                    if (b.gameObject.activeInHierarchy && b.gameObject.name.StartsWith("buy_")) buys++;
                Check(buys > 0, label + ": CONTROL - expanding a part reveals its BUY rows (" + buys + ")");
                Sweep(label + " SHOP (part expanded)", dockCanvas.transform, row, safe);
                MeasureType(label + " SHOP (part expanded)", dockCanvas.transform, sf, physical);
                head.onClick.Invoke();
                yield return Settle();
            }
        }

        // ---- 4b. the no-notch control leg ------------------------------------
        yield return StartCoroutine(NoNotchLeg(label, insetSide / sf, canvasRt));
        SafeAreaWeb.forcedLeft = SafeAreaWeb.forcedRight = insetSide;
        SafeAreaWeb.forcedBottom = insetBottom; SafeAreaWeb.forcedTop = 0f;
        yield return Settle();

        // ---- 5. the save card, both faces ------------------------------------
        // The two faces are selected through the product's own public entry
        // points rather than by reaching for `saveDlgConfirm`: which face a
        // given gesture reaches is TouchSmoke's question, and the layout of
        // each face is this bench's.
        if (bt != null) { bt.onClick.Invoke(); yield return Settle(); }
        ui.OpenSaveDialog();
        yield return Settle();
        Check(ui.SaveDialogShown, label + ": the save card opens on its naming face");
        Sweep(label + " save card (name)", dockCanvas.transform, row, safe);
        MeasureType(label + " save card (name)", dockCanvas.transform, sf, physical);
        var cancel = Btn("CANCEL");
        if (cancel != null) { cancel.onClick.Invoke(); yield return Settle(); }

        bm.SaveActive();        // something is open, so SAVE now has a choice to offer
        yield return null;
        ui.OpenSaveConfirm();
        yield return Settle();
        Check(ui.SaveDialogShown, label + ": the save card opens on its confirm face");
        Check(Btn("OVERWRITE") != null, label + ": ...offering OVERWRITE");
        Sweep(label + " save card (confirm)", dockCanvas.transform, row, safe);
        cancel = Btn("CANCEL");
        if (cancel != null) { cancel.onClick.Invoke(); yield return Settle(); }

        // ---- 6. the map HUD --------------------------------------------------
        bm.EnterMap();
        for (int i = 0; i < 8 && bm.mode != BuilderManager.Mode.Map; i++) yield return null;
        Check(bm.mode == BuilderManager.Mode.Map, label + ": DRIVE OUT enters the yard");
        yield return null; yield return null;
        var hudGO = GameObject.Find("map_hud_canvas");
        Check(hudGO != null, label + ": the map HUD is a UGUI canvas");
        if (hudGO == null) yield break;
        MakePhoneCanvas(hudGO.GetComponent<Canvas>(), w, h, sf);
        yield return Settle();
        var hud = MapHudUI.inst;
        // ⚠ THE SECOND HALF OF THE ROW GUARD, AND THE HALF THAT ALMOST GOT
        // AWAY. The assertion up in the dock section reads
        // MobileBuilderUI.TouchRowUnits, which is a live call into TouchRow()
        // - a real guard for the DOCK canvas and no guard at all for this one,
        // because MapHudUI computes its row independently in its own Row().
        // A forcing hook that stops reaching MapHudUI - exactly what
        // PixelRatio() did before it was wired up - would sail past the dock
        // guard and then sweep a desktop-sized map HUD, which is the canvas
        // half this run is about.
        //
        // Asserted as AGREEMENT rather than against a literal, deliberately:
        // "the two canvases agree about what a finger is" cannot be satisfied
        // by updating one expected number and forgetting the other.
        //
        // ⚠ And it is asserted AFTER Settle(), not before. RowUnits returns a
        // CACHED field, written only inside ApplyMetrics when LateUpdate sees
        // metricSig move - read too early it returns a stale 44 and this fails
        // on a perfectly good UI, sending the next reader hunting a hook bug
        // that is not there. (TypeFloorUnits calls FontPt live and has no such
        // problem; the two seams do not behave the same way.)
        Check(hud != null && ui != null && Mathf.Abs(hud.RowUnits - ui.TouchRowUnits) < 0.5f,
              label + ": the map HUD and the dock agree on the touch row (hud "
              + (hud != null ? hud.RowUnits.ToString("0.0") : "?") + " vs dock "
              + ui.TouchRowUnits.ToString("0.0") + ")");
        Check(hud != null && hud.ChipsShown >= 3, label + ": ...with compass chips (" + (hud != null ? hud.ChipsShown : 0) + ")");
        Check(hud != null && hud.ObjectiveText.Length > 0, label + ": ...and an objective line (" + (hud != null ? hud.ObjectiveText : "") + ")");
        Sweep(label + " map HUD (chips + objective)", hudGO.transform, row, safe);
        MeasureType(label + " map HUD (chips + objective)", hudGO.transform, sf, physical);

        // the encounter card: drive up to the parked machine
        var parked = bm.YardParked;
        if (parked != null)
        {
            bm.TeleportPlayer(parked.rb.position + new Vector3(2.5f, 0f, 0f));
            yield return Settle();
            Check(hud != null && hud.CardShown, label + ": the encounter card comes up beside the parked machine");
            Sweep(label + " map HUD (encounter card)", hudGO.transform, row, safe);
            MeasureType(label + " map HUD (encounter card)", hudGO.transform, sf, physical);
            bm.TeleportPlayer(bm.YardGarageDoor + new Vector3(0f, 0f, -6f));
            yield return Settle();
        }

        // the board, through the BOARD button's own onClick
        if (hud != null && hud.BoardButton != null)
        {
            // THE FIXTURE THAT WAS MISSING. The first version of this bench
            // measured the modals with the toast and the banner EMPTY, which
            // is the unusual state, not the normal one - Career.SaveNotice is
            // force-pushed into the toast unconditionally (HudModel), and the
            // banner carries the region hint whenever nothing else is
            // speaking. With them empty the rect tests never saw the deepest
            // line on the screen, and the overlap this bench reported was the
            // objective's alone - the smaller half.
            //
            // The toast is the worst case of the three and the HUD shows it in
            // preference to the banner (hasBanner = !hasToast), so a live
            // toast is what a modal has to clear.
            bm.TestYardToast("TREASURE  ·  +40 scrap  ·  steel plate", 600f);
            yield return Settle();
            Check(hud.ToastText.Length > 0,
                  label + ": CONTROL - a toast IS shown on the open map (" + hud.ToastText + ")");
            Check(hud.ObjectiveRect != null && hud.ObjectiveRect.gameObject.activeInHierarchy,
                  label + ": CONTROL - and the objective line is up too");

            hud.BoardButton.onClick.Invoke();
            yield return Settle();
            Check(hud.BoardShown, label + ": BOARD opens the yard board");
            // The board's body arrives from the network; offline it settles on
            // an error line within a moment. Measuring it mid-fetch would
            // measure a one-line panel that never ships.
            float dead = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < dead && hud.BoardText == "reading the board...") yield return null;
            yield return Settle();
            Sweep(label + " map HUD (board)", hudGO.transform, row, safe);
            MeasureType(label + " map HUD (board)", hudGO.transform, sf, physical);
            GateCheck(label + " map HUD (board)", hud);

            // and the sign-in face the board offers when signed out
            if (hud.BoardSignInShown && hud.BoardSignInButton != null)
            {
                hud.BoardSignInButton.onClick.Invoke();
                yield return Settle();
                Check(hud.SignInShown, label + ": the board's SIGN IN opens the sign-in panel");
                Sweep(label + " map HUD (sign-in)", hudGO.transform, row, safe);
                MeasureType(label + " map HUD (sign-in)", hudGO.transform, sf, physical);
                GateCheck(label + " map HUD (sign-in)", hud);
                if (hud.SignInCancelButton != null) { hud.SignInCancelButton.onClick.Invoke(); yield return Settle(); }
            }
            hud.HideBoard();
            bm.TestYardToast("", 0f);
        }
        bm.LeaveMap();
        yield return null;
    }

    /// <summary>Let the layout finish. MobileBuilderUI relayouts from a
    /// SIGNATURE watched in Update (scale factor + canvas rect + safe area),
    /// so a changed rect takes a frame to reach the controls, and a layout
    /// group takes another to reach their children. Measuring earlier reads
    /// the previous screen - which is the trap FitPaletteRows fell into twice.</summary>
    static IEnumerator Settle()
    {
        yield return null; yield return null;
        Canvas.ForceUpdateCanvases();
        yield return null;
    }
}
}
#endif
