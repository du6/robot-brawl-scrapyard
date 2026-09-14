// ===========================================================================
// RewardBox.cs - the ceremony for an earned reward.
//
// owen, 2026-09-04: "add some congrats animations whenever the user completes
// a guided task and give them some rewards in a box. User can open the box
// and see the rewards."
//
// The GRANT is already ledgered by Career before anything is queued (the
// checklist's +10s, the rescue crate) - this is the handover moment, not the
// transaction. So the box can be skipped, reloaded through, or crash without
// losing a single scrap, and it never touches Career.Data.
//
// Everything is procedural UGUI, the RookieGuide pattern: a burst of confetti
// squares, a banner, a crate whose lid swings open on a tap, and the reward
// lines rising out of it. No assets, no animator, no video.
//
// WEB ONLY like the rest of the warm-up: Career.QueueReward compiles to a
// no-op outside the WebGL player, so benches and iOS never see one.
//
// ---- THE SHAPE OF EVERY BUG THIS SCREEN HAS HAD --------------------------
// Four defects were found here on 2026-09-13, a fifth fell out of writing the
// check for them, and all five are ONE defect wearing five hats: A FACTOR
// APPLIED TO SOME OF A GROUP AND NOT THE REST.
//
//   the reward pictures overlapped   pitch got `v`, the picture did not
//   CLAIM fell off the bottom edge   every offset got `v`, CLAIM's did not
//   CLAIM shrank as density rose     `k` came off the UNCAPPED pixel ratio
//                                    while the framebuffer was capped at 2
//   the hint and CLAIM drifted       same missing `v`, seen from the other side
//     out of their shared slot
//   title into caption, caption      the composition compresses with `v`, the
//     into line 0, at v's floor      type is held at a physical floor
//
// None of these is a wrong number. Each is a number that was right where it
// was written and then stopped agreeing with a number somewhere else, which
// is why every one of them survived reading the line it lived on. THIS FILE
// IS NOW WRITTEN TO MAKE THAT CLASS IMPOSSIBLE TO WRITE AGAIN: one pure
// Measure() owns every length, each derives from the one it must agree with
// (the picture from the pitch, CLAIM from the crate, each gap from the two
// things it separates), and RewardBoxBench asserts the INVARIANT - nothing
// overlaps anything, at the shortest screen the compression supports - rather
// than the instance. A check written at v near 1 passes all five of these,
// which is how they got here.
//
// So: if you add a length here, derive it from whatever it has to agree with.
// A second literal is the bug, every time.
//
// ---- PHONE SIZING, 2026-09-13 --------------------------------------------
// Every number below used to be a design literal times `k`, where `k` was
// `clamp(Screen.dpi/163, 1, 2.5)` on a canvas with NO CanvasScaler - so one
// canvas unit was one FRAMEBUFFER pixel. The web template caps the
// framebuffer at devicePixelRatio 2 (Assets/WebGLTemplates/RobotBrawl,
// `Math.min(window.devicePixelRatio || 1, 2)`) while Unity's Screen.dpi on
// WebGL reports the UNCAPPED ratio as 96*dpr, so the two disagreed and the
// whole ceremony shrank as the phone got denser. Measured on a 932x430 CSS-pt
// landscape phone, which is a 1864x860 framebuffer at dpr 2 and above:
//
//   dpr 3: k = 288/163 = 1.767 -> CLAIM 44*k = 77.8 fb px = 38.9 CSS px
//   dpr 2: k = 192/163 = 1.178 -> CLAIM 44*k = 51.8 fb px = 25.9 CSS px
//
// - smaller on the better screen, and never once reaching the 44 pt touch
// floor. The caption and the "TAP THE BOX TO OPEN" hint at 15*k landed at
// 13.5 and 8.8 CSS px against the dock's 14 CSS px readable floor.
//
// The fix is `Measure()`: ONE pure function that turns the real screen into
// the layout, sizing from the same physical basis the dock uses -
// MobileBuilderUI.DesktopRow()'s 52 CSS px row and DesktopFontUnits()'s type
// - so the two surfaces cannot drift apart. `k` is now "canvas units per
// DESIGN unit", derived from a touch row rather than from dpi, and every
// design literal in this file is authored against a 44-unit row (CLAIM is
// 220x44, the lid is 44 tall). At the basis above CLAIM is 52 CSS px on
// every density and the hint is 16.5 CSS px.
//
// ⚠ WHY THERE IS STILL NO CanvasScaler, though one would be the obvious fix.
// CanvasScaler publishes its scaleFactor from Canvas.preWillRenderCanvases,
// i.e. AFTER Start - the exact trap MobileBuilderUI.cs documents at its
// ApplyTouchSizes(). This box builds its entire hierarchy once, in Start, and
// never lays out again, so every literal would be placed against a
// scaleFactor of 1 that then became 1.32 at the measurement basis
// (sqrt(1864/1280 * 860/720)), leaving the whole ceremony 32% too small with
// no second pass to correct it. Adding a scaler therefore means adding a
// re-layout pass and a scaleFactor-change watcher; deriving `k` from a
// physical row needs neither and lands the identical physical size, because
// with no scaler one canvas unit IS one framebuffer pixel and pixelRatio
// converts it straight to CSS px. The confetti, the crate and the burst all
// scale off the same factor, so none of that geometry moves either.
// ===========================================================================
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RobotBrawl.Phase0
{
    public class RewardBox : MonoBehaviour
    {
        public static RewardBox active;

        /// <summary>Drain one queued reward into a box. Called from
        /// MobileBuilderUI.Update; no-op while a box is up or a fight is on.</summary>
        public static void Tick()
        {
            if (active != null) return;
            if (Career.rewardQueue.Count == 0) return;
            if (FightManager.current != null) return;
            var r = Career.rewardQueue[0];
            Career.rewardQueue.RemoveAt(0);
            var go = new GameObject("RewardBox");
            active = go.AddComponent<RewardBox>();
            active.pop = r;
        }

        Career.RewardPop pop;
        enum Phase { Appear, Closed, Opening, Revealed, Dismiss }
        Phase phase = Phase.Appear;
        float t;                 // seconds in the current phase
        float k = 1f;            // canvas units per DESIGN unit (a touch row is 44 of them)
        float c = 1f;            // k * v: the crate ceremony's uniform scale

        Canvas canvas;
        Image backdrop;
        RectTransform boxRt, lidRt, bodyRt;
        Text title, caption, tapHint;
        Button claim, tapButton;
        readonly List<Text> lines = new List<Text>();
        readonly List<Vector2> lineHome = new List<Vector2>();
        // SCRAPYARD (owen, 2026-09-10): a PICTURE beside each line - the part
        // itself, photographed by RewardThumb, or a coin stack for scrap.
        readonly List<RawImage> lineImgs = new List<RawImage>();
        readonly List<Texture2D> lineTex = new List<Texture2D>();
        Font font;
        static Sprite white;

        // AUTO was 9 s: a box that vanished under a slow finger put the tap on
        // whatever sat beneath (web QA 2026-09-05: it collapsed the dock). A
        // reward waits for its player.
        const float APPEAR = 0.45f, OPEN = 0.5f, REVEAL = 0.6f, DISMISS = 0.3f, AUTO = 30f;

        static readonly Color[] PALETTE = {
            new Color(1f, 0.84f, 0.40f), new Color(0.40f, 0.80f, 1f), new Color(1f, 0.45f, 0.45f),
            new Color(0.55f, 1f, 0.60f), new Color(1f, 1f, 1f), new Color(1f, 0.62f, 0.24f) };

        // ---- the measured layout ---------------------------------------------

        /// <summary>What the screen is, in the only three terms this layout
        /// needs. Kept as a value so a bench can hand over a phone's numbers on
        /// a machine that has no phone (see forcedMetrics).</summary>
        public struct Metrics
        {
            /// <summary>FRAMEBUFFER pixels. This canvas has no CanvasScaler, so
            /// one canvas unit is one of these.</summary>
            public float screenW, screenH;
            /// <summary>Framebuffer pixels per CSS pixel: 1 outside a browser,
            /// and in one the CAPPED ratio (canvas.width / its CSS width), which
            /// is what the jslib measures and what the template caps at 2.</summary>
            public float pixelRatio;
            /// <summary>Physical dots per inch, when `physical` is true.</summary>
            public float dpi;
            /// <summary>True on a real handheld, where a point is a physical
            /// thing and dpi is honest. False in a browser, where the reported
            /// dpi is not physical and CSS pixels are the unit that matters -
            /// the same split MobileBuilderUI.PhysicalTouchSizing makes.</summary>
            public bool physical;
        }

        /// <summary>Every position and size this box draws, in canvas units,
        /// derived in one place so the bench measures what Build() consumes.</summary>
        public struct Layout
        {
            public float k;            // canvas units per design unit
            public float v;            // short-screen compression, 0.45 .. 1
            public float c;            // k * v
            public float rowUnits;     // one touch row, in canvas units - NEVER compressed
            public float cssPerUnit;   // canvas units -> CSS px
            public float boxW, boxBodyH, boxLidH, boxY;
            public float titleY, captionY, hintY, claimY;
            public float claimW, claimH;
            public float linePitch;    // the SMALLEST gap between two reward lines
            public float thumbUnits;   // one reward picture, square
            public int thumbPx;        // the texture RewardThumb renders into
            public float[] lineY;
            public int[] lineFont;
            public int titleFont, captionFont, hintFont, claimFont;
        }

        /// <summary>The design is authored against a 44-unit touch row: CLAIM is
        /// 220x44 design units and the crate's lid is 44 tall.</summary>
        const float DESIGN_ROW = 44f;
        /// <summary>MobileBuilderUI.DesktopRow() is 52 * pixelRatio / scaleFactor
        /// canvas units, which is 52 CSS px on any density. Same number here so a
        /// CLAIM button and a dock button are the same size on the same screen.</summary>
        const float DESKTOP_ROW_CSS = 52f;
        /// <summary>The authored gap between two reward lines, in design units.</summary>
        const float LINE_PITCH = 38f;
        /// <summary>A reward picture, as a fraction of the pitch it sits in.
        ///
        /// THE DEFECT THIS REPLACES (measured 2026-09-13, 932x430 CSS pt): the
        /// picture was a flat `44f * k` while the pitch was `38f * k * v`. 44 is
        /// larger than 38 before the missing `v` is counted at all, and
        /// Career.QuickBoxRoll always emits exactly two lines - a scrap line and
        /// a part line - so the two pictures are ALWAYS adjacent. At dpr 3 the
        /// pitch was 62.15 fb px against a 77.75 fb px picture: 15.6 px of
        /// overlap, the coins sitting on top of the part, on every chest. At
        /// dpr 2 it was ~7, and because `v` shrinks the pitch and not the
        /// picture it got WORSE on shorter screens.
        ///
        /// Sizing from the pitch makes overlap impossible for any value under
        /// 1; 0.82 is under it with ~18% of a pitch left as a visible gap
        /// between two pictures, which is what keeps a coin stack reading as a
        /// separate object from the part under it.</summary>
        const float THUMB_OF_PITCH = 0.82f;
        /// <summary>Unity's Text line box against LegacyRuntime.ttf is roughly
        /// this many times the point size. An APPROXIMATION, and deliberately
        /// only used to WIDEN the pitch when two lines' type would not fit the
        /// authored 38 - the exact, checkable geometry is the picture's rect,
        /// which the same pitch sets. Inert at the measurement basis and only
        /// just: 24 pt over 17 pt needs 59.3 units there and the authored pitch
        /// is 59.9, so it takes over on anything shorter (measured on the 300
        /// CSS-pt case in RewardBoxBench).</summary>
        public const float TEXT_BOX = 1.2f;
        /// <summary>Clear air between two stacked elements, in design units, so
        /// a widened gap still reads as a gap rather than as two things touching.</summary>
        const float STACK_GAP = 2f;

        /// <summary>Turn a screen into a layout. Pure: no Unity singletons, no
        /// scene, no frame - RewardBoxBench calls it with a phone's numbers.</summary>
        public static Layout Measure(Metrics m, int lineCount)
        {
            var L = new Layout();
            float pr = Mathf.Clamp(m.pixelRatio > 0.01f ? m.pixelRatio : 1f, 0.25f, 4f);
            L.cssPerUnit = 1f / pr;

            // ONE TOUCH ROW, in canvas units - the whole layout's unit of length.
            // The two branches are the same split MobileBuilderUI makes: on a
            // handheld a point is physical and dpi is honest; in a browser it is
            // not, and CSS pixels are what the finger actually gets.
            float row = m.physical && m.dpi >= 1f
                      ? (DESIGN_ROW / 163f) * m.dpi          // 44 pt, Apple's floor
                      : DESKTOP_ROW_CSS * pr;                // 52 CSS px, the dock's row
            if (row < 1f) row = DESIGN_ROW;                  // unknown screen: previous behaviour
            L.rowUnits = row;
            L.k = row / DESIGN_ROW;

            // A short screen (iPhone landscape) cannot hold the tall layout: the
            // title sat on the status line and the reward lines on the tab strip
            // (iOS QA 2026-09-05). 237 is the design's half-height above centre
            // (the title's 215 centre plus its 22 half-box); 24 is the margin
            // left above it.
            //
            // ⚠ v scales the COMPOSITION, not the touch targets. It used to
            // multiply positions only, which is what put a 44-unit picture in a
            // 38*v-unit pitch and what dropped CLAIM off the bottom edge (its y
            // was the one offset on this screen that never got a `v` at all).
            // Sizes and offsets now compress together; CLAIM and the type are
            // the deliberate exceptions, because a floor that compresses is not
            // a floor.
            L.v = Mathf.Clamp((m.screenH * 0.5f - 24f * L.k) / (237f * L.k), 0.45f, 1f);
            L.c = L.k * L.v;

            L.titleFont = Pt(m, 30f * Mathf.Max(0.8f, L.v));
            L.captionFont = Pt(m, 15f);
            L.hintFont = Pt(m, 15f);
            L.claimFont = Pt(m, 17f);

            L.boxW = 220f * L.c; L.boxBodyH = 150f * L.c; L.boxLidH = DESIGN_ROW * L.c;
            L.boxY = -45f * L.c;

            // CLAIM keeps its physical height whatever v does, and is placed
            // UNDER the crate rather than at a literal offset, so the two can
            // never collide the way they did once the button grew to a real
            // touch row. 4.5 rows is the width the word plus its padding needs.
            L.claimH = row;
            L.claimW = Mathf.Max(220f * L.c, 4.5f * row);
            float boxBottom = L.boxY - (L.boxBodyH + L.boxLidH) * 0.5f;
            L.claimY = boxBottom - 6f * L.k - L.claimH * 0.5f;
            float lowest = -m.screenH * 0.5f + 8f * L.k + L.claimH * 0.5f;
            if (L.claimY < lowest) L.claimY = lowest;       // the edge wins over the gap
            L.hintY = L.claimY;                              // they alternate: hint closed, CLAIM revealed

            int n = Mathf.Max(0, lineCount);
            L.lineY = new float[n];
            L.lineFont = new int[n];
            for (int i = 0; i < n; i++) L.lineFont[i] = Pt(m, i == 0 ? 24f : 17f);

            // THE PICTURE IS SIZED FROM THE AUTHORED PITCH, not from the gap
            // that is about to be widened to hold it - sizing it from the final
            // gap would be a feedback loop (a bigger picture needs a bigger gap
            // needs a bigger picture). Off the authored pitch it is 0.82 of the
            // SMALLEST gap the stack can ever produce, so the 18% of clear air
            // holds at every v without the two ever chasing each other.
            L.thumbUnits = LINE_PITCH * L.c * THUMB_OF_PITCH;
            // One canvas unit is one framebuffer pixel here, so the picture is
            // rendered 1:1 with the rect it is drawn into.
            L.thumbPx = Mathf.Clamp(Mathf.RoundToInt(L.thumbUnits), 48, 192);

            // THE WHOLE STACK IS GAPS, NOT OFFSETS. Each gap is the authored one
            // compressed by v, OR what the two elements' own heights need,
            // whichever is larger - because v compresses the composition while
            // the TYPE is held at a physical floor, so on a short screen the
            // authored gaps stop being big enough for the type inside them. That
            // is finding 11's shape one level up. At v's 0.45 floor the authored
            // offsets put the title 12.9 units INTO the caption and the caption
            // 3.3 into the first reward line - measured 2026-09-13 at 932x300 CSS
            // pt against Unity's own generated line box, and re-measured on every
            // run by RewardBoxBench's control leg - for exactly the reason CLAIM
            // used to fall off the bottom edge: a factor applied to some of a
            // group and not the rest. Everywhere roomier the authored gap is
            // already the larger of the two and nothing here changes it.
            //
            // The stack grows UPWARD from the top reward line, so a gap that has
            // to widen widens away from the crate rather than into it. That it
            // still fits the shortest screen v supports is not an argument, it
            // is RewardBoxBench's "nothing overlaps anything" check.
            float[] half = new float[n];
            for (int i = 0; i < n; i++) half[i] = Mathf.Max(L.lineFont[i] * TEXT_BOX, L.thumbUnits) * 0.5f;
            // linePitch ends as the SMALLEST gap between two lines - the one the
            // picture has to fit inside. With a single line there is no gap, so
            // the authored pitch stands in.
            L.linePitch = LINE_PITCH * L.c;
            float y = 140f * L.c;
            bool anyGap = false;
            for (int i = 0; i < n; i++)
            {
                L.lineY[i] = y;
                if (i + 1 >= n) break;
                float pitch = Gap(LINE_PITCH, L, half[i], half[i + 1]);
                if (!anyGap || pitch < L.linePitch) { L.linePitch = pitch; anyGap = true; }
                y -= pitch;
            }
            // 42 is the authored caption-to-first-line gap (182 - 140) and 33 the
            // authored title-to-caption gap (215 - 182).
            float capHalf = L.captionFont * TEXT_BOX * 0.5f;
            float titleHalf = L.titleFont * TEXT_BOX * 0.5f;
            L.captionY = (n > 0 ? L.lineY[0] : 140f * L.c) + Gap(42f, L, n > 0 ? half[0] : 0f, capHalf);
            L.titleY = L.captionY + Gap(33f, L, capHalf, titleHalf);
            return L;
        }

        /// <summary>One gap in the vertical stack: the authored distance under
        /// the screen's compression, or the two elements' own half-heights plus
        /// a strip of clear air, whichever is larger.</summary>
        static float Gap(float authored, Layout L, float halfBelow, float halfAbove)
        {
            return Mathf.Max(authored * L.c, halfBelow + halfAbove + STACK_GAP * L.k);
        }

        /// <summary>A physical type size, in canvas units. The browser branch is
        /// MobileBuilderUI's own public seam, passed canvasScale 1 because this
        /// canvas has no CanvasScaler - it floors at 14 CSS px, which is the
        /// readable floor the dock's labels are held to.</summary>
        static int Pt(Metrics m, float points)
        {
            if (m.physical && m.dpi >= 1f) return Mathf.Max(8, Mathf.RoundToInt(points * (m.dpi / 163f)));
            float pr = Mathf.Clamp(m.pixelRatio > 0.01f ? m.pixelRatio : 1f, 0.25f, 4f);
            return MobileBuilderUI.DesktopFontUnits(points, 1f, pr, 1f);
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        // The SAME jslib entry the dock reads (Assets/Plugins/WebGL/scrapyardui.jslib):
        // canvas.width / its CSS width, i.e. the ratio AFTER the template's cap,
        // which is the number that converts framebuffer px to CSS px.
        // MobileBuilderUI keeps its copy in a private static; this is a second
        // declaration of one extern, not a second source of truth.
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern float ScrapyardUiPixelRatio();
#endif

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>The layout this instance was built with, so a bench can
        /// check the arithmetic AND that the hierarchy actually consumed it.</summary>
        public Layout BuiltLayout { get { return lay; } }
        /// <summary>BUILD A REAL HIERARCHY AT A GIVEN SCREEN, with no play mode
        /// and no scene, so RewardBoxBench can measure the rects a phone gets on
        /// a machine that has no phone. Headless reports 640x480 at dpi 266 with
        /// deviceType Desktop and will not move - PhoneLayoutBench's header has
        /// the two things that were tried - so the screen has to be handed in.
        /// Start() is not involved and neither is the real Screen; tear the
        /// result down with DestroyImmediate.</summary>
        public static RewardBox TestMake(Career.RewardPop p, Metrics m)
        {
            var go = new GameObject("RewardBoxTest");
            var b = go.AddComponent<RewardBox>();
            b.pop = p;
            b.Build(m);
            return b;
        }
#endif
        Layout lay;

        static Metrics Read()
        {
            var m = new Metrics();
            m.screenW = Screen.width; m.screenH = Screen.height;
            // UnityEngine.Device.Screen for the dpi, not Screen - under the
            // Device Simulator the plain one reports the editor window.
            m.dpi = UnityEngine.Device.Screen.dpi;
#if UNITY_WEBGL && !UNITY_EDITOR
            m.pixelRatio = ScrapyardUiPixelRatio();
            m.physical = false;    // browsers expose CSS pixels; reported dpi is not physical
#else
            m.pixelRatio = 1f;
            m.physical = UnityEngine.Device.Application.isMobilePlatform
                      || UnityEngine.Device.SystemInfo.deviceType == DeviceType.Handheld;
#endif
            return m;
        }

        void Start()
        {
            Build(Read());
            Burst(26, 0f);
            SfxSynth.Place();
        }

        void Build(Metrics m)
        {
            lay = Measure(m, pop != null ? pop.lines.Count : 0);
            k = lay.k; c = lay.c;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            var cgo = new GameObject("RewardBoxCanvas", typeof(Canvas), typeof(GraphicRaycaster));
            cgo.transform.SetParent(transform, false);
            canvas = cgo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 6000;   // above the guide's 5000

            // Backdrop - dims the game and swallows stray taps.
            backdrop = MkImage("backdrop", cgo.transform, new Color(0f, 0f, 0f, 0f));
            var brt = backdrop.rectTransform;
            brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one; brt.offsetMin = brt.offsetMax = Vector2.zero;
            backdrop.raycastTarget = true;

            // The crate: a body and a lid, lid pivoting on its bottom edge.
            float bw = lay.boxW, bh = lay.boxBodyH, lh = lay.boxLidH;
            var boxGo = new GameObject("box", typeof(RectTransform));
            boxGo.transform.SetParent(cgo.transform, false);
            boxRt = boxGo.GetComponent<RectTransform>();
            boxRt.anchorMin = boxRt.anchorMax = new Vector2(0.5f, 0.5f);
            boxRt.sizeDelta = new Vector2(bw, bh + lh);
            boxRt.anchoredPosition = new Vector2(0f, lay.boxY);
            boxRt.localScale = Vector3.zero;

            var body = MkImage("body", boxRt, new Color(0.55f, 0.36f, 0.16f));
            bodyRt = body.rectTransform;
            bodyRt.anchorMin = bodyRt.anchorMax = new Vector2(0.5f, 0f);
            bodyRt.pivot = new Vector2(0.5f, 0f);
            bodyRt.sizeDelta = new Vector2(bw, bh);
            bodyRt.anchoredPosition = Vector2.zero;
            // a strap and rivets so it reads as a crate, not a brown rect
            var strap = MkImage("strap", bodyRt, new Color(0.30f, 0.20f, 0.09f));
            strap.rectTransform.anchorMin = new Vector2(0f, 0.42f); strap.rectTransform.anchorMax = new Vector2(1f, 0.58f);
            strap.rectTransform.offsetMin = strap.rectTransform.offsetMax = Vector2.zero;
            var plate = MkImage("plate", bodyRt, new Color(0.85f, 0.70f, 0.30f));
            plate.rectTransform.anchorMin = plate.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            plate.rectTransform.sizeDelta = new Vector2(46f * c, 34f * c);
            body.raycastTarget = true;               // a Button needs a hit-testable graphic (verified: with it false the box never opened)
            var tapBtn = body.gameObject.AddComponent<Button>();
            tapBtn.onClick.AddListener(Open);
            tapButton = tapBtn;

            var lid = MkImage("lid", boxRt, new Color(0.68f, 0.46f, 0.20f));
            lidRt = lid.rectTransform;
            lidRt.anchorMin = lidRt.anchorMax = new Vector2(0.5f, 0f);
            lidRt.pivot = new Vector2(0.5f, 0f);     // hinge on the back edge, visually the bottom
            lidRt.sizeDelta = new Vector2(bw + 12f * c, lh);
            lidRt.anchoredPosition = new Vector2(0f, bh);

            // Words: title above, caption below the title, hint where CLAIM will
            // be - the hint fades out as the lid opens and CLAIM fades in, so the
            // two deliberately share one slot.
            float textW = Mathf.Min(900f * lay.k, Mathf.Max(120f, m.screenW));
            title = MkText("title", cgo.transform, "", lay.titleFont, TextAnchor.MiddleCenter);
            title.fontStyle = FontStyle.Bold; title.color = new Color(1f, 0.84f, 0.40f);
            Place(title.rectTransform, 0f, lay.titleY, textW, lay.titleFont * 1.6f);
            caption = MkText("caption", cgo.transform, "", lay.captionFont, TextAnchor.MiddleCenter);
            caption.color = new Color(0.86f, 0.90f, 0.98f);
            Place(caption.rectTransform, 0f, lay.captionY, textW, lay.captionFont * 1.6f);
            tapHint = MkText("hint", cgo.transform, "TAP THE BOX TO OPEN", lay.hintFont, TextAnchor.MiddleCenter);
            tapHint.color = new Color(1f, 1f, 1f, 0.85f);
            Place(tapHint.rectTransform, 0f, lay.hintY, Mathf.Min(500f * lay.k, textW), lay.hintFont * 1.6f);

            // Reward lines start INSIDE the box and rise out of it on open.
            int n = pop != null ? pop.lines.Count : 0;
            for (int i = 0; i < n; i++)
            {
                var l = MkText("line" + i, cgo.transform, pop.lines[i], lay.lineFont[i], TextAnchor.MiddleCenter);
                l.fontStyle = FontStyle.Bold;
                l.color = new Color(1f, 0.92f, 0.55f, 0f);
                Place(l.rectTransform, 0f, 10f * c, Mathf.Min(700f * lay.k, textW), lay.lineFont[i] * 1.6f);
                lines.Add(l);
                // first (biggest) line highest; the rest stack downward toward the lid
                lineHome.Add(new Vector2(0f, lay.lineY[i]));
                // which picture: "+N SCRAP" is coins; a part line is that part
                string thumbId = null, thumbMat = null;
                string ln = pop.lines[i];
                if (ln.StartsWith("+") && ln.Contains("SCRAP")) thumbId = "coins";
                else if (pop.id != null && pop.id.StartsWith("qbox:"))
                {
                    var f = pop.id.Split(':');
                    if (f.Length == 5) { thumbId = f[2]; thumbMat = f[3]; }
                }
                RawImage img = null; Texture2D tex = null;
                if (thumbId != null)
                {
                    tex = RewardThumb.Render(thumbId, thumbMat, lay.thumbPx);
                    if (tex != null)
                    {
                        var igo = new GameObject("thumb" + i, typeof(RectTransform));
                        igo.transform.SetParent(l.transform, false);
                        img = igo.AddComponent<RawImage>();
                        img.texture = tex; img.raycastTarget = false;
                        img.color = new Color(1f, 1f, 1f, 0f);
                        var irt = img.rectTransform;
                        irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 0.5f);
                        // SIZED FROM THE PITCH, so two adjacent pictures cannot
                        // touch whatever v does. See THUMB_OF_PITCH.
                        irt.sizeDelta = new Vector2(lay.thumbUnits, lay.thumbUnits);
                        irt.anchoredPosition = Vector2.zero;   // set beside the text at reveal, once its width is known
                    }
                }
                lineImgs.Add(img); lineTex.Add(tex);
            }

            // CLAIM - appears once revealed. Its height is a touch row and never
            // compresses; Measure() put it clear of the crate and inside the
            // bottom edge.
            var cgoBtn = MkImage("claim", cgo.transform, new Color(0.30f, 0.62f, 0.88f, 0f));
            cgoBtn.raycastTarget = true;
            Place(cgoBtn.rectTransform, 0f, lay.claimY, lay.claimW, lay.claimH);
            claim = cgoBtn.gameObject.AddComponent<Button>();
            claim.onClick.AddListener(() => { if (phase == Phase.Revealed) { phase = Phase.Dismiss; t = 0f; } });
            var ct = MkText("claimtxt", cgoBtn.transform, "CLAIM", lay.claimFont, TextAnchor.MiddleCenter);
            ct.fontStyle = FontStyle.Bold; ct.color = new Color(1f, 1f, 1f, 0f);
            var ctr = ct.rectTransform; ctr.anchorMin = Vector2.zero; ctr.anchorMax = Vector2.one; ctr.offsetMin = ctr.offsetMax = Vector2.zero;
            claim.interactable = false;

            if (pop != null) { title.text = pop.title; caption.text = pop.caption; }
        }

        void OnDestroy()
        {
            foreach (var tx in lineTex) Discard(tx);
            if (pop != null) Career.GrantReward(pop.id);   // auto-dismissed or skipped: still paid
            if (active == this) active = null;
        }

        /// <summary>Object.Destroy THROWS outside play mode, and RewardBoxBench
        /// tears its fixtures down with DestroyImmediate in a plain editor run -
        /// which fires this OnDestroy. Without the split the bench's own cleanup
        /// raises out of DestroyImmediate.</summary>
        static void Discard(UnityEngine.Object o)
        {
            if (o == null) return;
#if UNITY_EDITOR
            if (!Application.isPlaying) { DestroyImmediate(o); return; }
#endif
            Destroy(o);
        }

        void Update()
        {
            t += Time.unscaledDeltaTime;
            switch (phase)
            {
                case Phase.Appear:
                {
                    float p = Mathf.Clamp01(t / APPEAR);
                    backdrop.color = new Color(0f, 0f, 0f, 0.62f * p);
                    // ease-out-back: overshoots to ~1.1 then settles
                    float s = 1f + 2.7f * Mathf.Pow(p - 1f, 3f) + 1.7f * Mathf.Pow(p - 1f, 2f);
                    boxRt.localScale = Vector3.one * s;
                    if (p >= 1f) { phase = Phase.Closed; t = 0f; }
                    break;
                }
                case Phase.Closed:
                {
                    // a gentle breathing pulse says "this is the thing to tap"
                    float pulse = 1f + 0.04f * Mathf.Sin(t * 5f);
                    boxRt.localScale = Vector3.one * pulse;
                    tapHint.color = new Color(1f, 1f, 1f, 0.55f + 0.35f * Mathf.Abs(Mathf.Sin(t * 3f)));
                    break;
                }
                case Phase.Opening:
                {
                    float p = Mathf.Clamp01(t / OPEN);
                    float e = 1f - Mathf.Pow(1f - p, 3f);
                    // A Z-rotation swept the lid THROUGH the body (seen 2026-09-04).
                    // Flip it over the back edge instead: scale-Y through zero, so
                    // it foreshortens, vanishes edge-on, and reappears inverted as a
                    // short slab behind the rim - an open lid seen from the front.
                    float sy = Mathf.Lerp(1f, -0.45f, e);
                    lidRt.localScale = new Vector3(1f, sy, 1f);
                    var lidImg = lidRt.GetComponent<Image>();
                    if (lidImg != null) lidImg.color = sy >= 0f ? new Color(0.68f, 0.46f, 0.20f) : new Color(0.42f, 0.28f, 0.12f);
                    boxRt.localScale = Vector3.one * (1f + 0.06f * Mathf.Sin(p * Mathf.PI));
                    tapHint.color = new Color(1f, 1f, 1f, 0.85f * (1f - p));
                    if (p >= 1f) { phase = Phase.Revealed; t = 0f; Burst(40, 30f * c); SfxSynth.Place(); }
                    break;
                }
                case Phase.Revealed:
                {
                    float p = Mathf.Clamp01(t / REVEAL);
                    float e = 1f - Mathf.Pow(1f - p, 3f);
                    for (int i = 0; i < lines.Count; i++)
                    {
                        float pi = Mathf.Clamp01((t - 0.08f * i) / REVEAL);
                        float ei = 1f - Mathf.Pow(1f - pi, 3f);
                        var rt = lines[i].rectTransform;
                        rt.anchoredPosition = Vector2.Lerp(new Vector2(0f, 10f * c), lineHome[i], ei);
                        lines[i].color = new Color(1f, 0.92f, 0.55f, ei);
                        rt.localScale = Vector3.one * (0.6f + 0.4f * ei);
                        var im = i < lineImgs.Count ? lineImgs[i] : null;
                        if (im != null)
                        {
                            // beside the text, a gap of its own half-width away -
                            // derived from the picture so it follows its size.
                            im.rectTransform.anchoredPosition =
                                new Vector2(-(lines[i].preferredWidth * 0.5f + lay.thumbUnits * 0.5f + 8f * lay.k), 0f);
                            im.color = new Color(1f, 1f, 1f, ei);
                        }
                    }
                    var ci = claim.GetComponent<Image>();
                    ci.color = new Color(0.30f, 0.62f, 0.88f, e);
                    var ct = claim.GetComponentInChildren<Text>(); if (ct != null) ct.color = new Color(1f, 1f, 1f, e);
                    claim.interactable = p >= 1f;
                    if (t > AUTO) { phase = Phase.Dismiss; t = 0f; }
                    break;
                }
                case Phase.Dismiss:
                {
                    float p = Mathf.Clamp01(t / DISMISS);
                    var cg = GetComponentInChildren<CanvasGroup>();
                    if (cg == null) cg = canvas.gameObject.AddComponent<CanvasGroup>();
                    cg.alpha = 1f - p;
                    if (p >= 1f) { Destroy(gameObject); return; }
                    break;
                }
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>Capture seam (PromoAutopilot): fire the BUTTON, never the
        /// method behind it, so the recorded footage takes the same path a
        /// finger takes. Never compiled into a release player.</summary>
        public void TestTapBody() { if (tapButton != null) tapButton.onClick.Invoke(); }
        public void TestTapClaim() { if (claim != null) claim.onClick.Invoke(); }
#endif
        void Open()
        {
            if (phase != Phase.Closed) return;
            phase = Phase.Opening; t = 0f;
            if (pop != null) Career.GrantReward(pop.id);   // the reveal IS the grant now
            RBTelemetry.Once("reward", "&o=1");
        }

        // ---- confetti --------------------------------------------------------
        void Burst(int n, float yOffset)
        {
            for (int i = 0; i < n; i++)
            {
                var img = MkImage("confetti", canvas.transform, PALETTE[Random.Range(0, PALETTE.Length)]);
                img.raycastTarget = false;
                var rt = img.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                float sz = Random.Range(6f, 12f) * c;
                rt.sizeDelta = new Vector2(sz, sz * Random.Range(0.5f, 1f));
                rt.anchoredPosition = new Vector2(0f, yOffset);
                var bit = img.gameObject.AddComponent<ConfettiBit>();
                float ang = Random.Range(35f, 145f) * Mathf.Deg2Rad;
                float spd = Random.Range(380f, 720f) * c;
                bit.vel = new Vector2(Mathf.Cos(ang) * spd, Mathf.Sin(ang) * spd);
                bit.spin = Random.Range(-540f, 540f);
                bit.life = Random.Range(1.1f, 1.7f);
                bit.k = c;
            }
        }

        class ConfettiBit : MonoBehaviour
        {
            public Vector2 vel; public float spin, life, k = 1f; float age;
            void Update()
            {
                float dt = Time.unscaledDeltaTime; age += dt;
                if (age >= life) { Destroy(gameObject); return; }
                vel += new Vector2(0f, -1400f * k) * dt;
                vel *= 1f - 1.6f * dt;                       // drag
                var rt = (RectTransform)transform;
                rt.anchoredPosition += vel * dt;
                rt.localRotation = Quaternion.Euler(0f, 0f, spin * age);
                var img = GetComponent<Image>(); var c = img.color;
                c.a = 1f - Mathf.Pow(age / life, 3f); img.color = c;
            }
        }

        // ---- tiny UI kit -----------------------------------------------------
        static Sprite White()
        {
            if (white != null) return white;
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var px = new Color32[16]; for (int i = 0; i < 16; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px); tex.Apply();
            white = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            return white;
        }
        Image MkImage(string name, Transform parent, Color c)
        {
            var go = new GameObject(name, typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>(); img.sprite = White(); img.color = c; img.raycastTarget = false;
            return img;
        }
        Text MkText(string name, Transform parent, string s, int size, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(Text));
            go.transform.SetParent(parent, false);
            var tx = go.GetComponent<Text>();
            tx.font = font; tx.fontSize = size; tx.alignment = anchor; tx.text = s;
            tx.horizontalOverflow = HorizontalWrapMode.Overflow; tx.verticalOverflow = VerticalWrapMode.Overflow;
            tx.raycastTarget = false;
            return tx;
        }
        static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, y);
        }
    }
}
