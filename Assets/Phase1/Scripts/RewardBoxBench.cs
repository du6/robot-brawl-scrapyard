// ===========================================================================
// RewardBoxBench.cs - THE REWARD BOX, AT A PHONE'S GEOMETRY.
//
// The reward box is what a player taps after every chest, so it is one of the
// most-seen screens in the game, and until 2026-09-13 NOTHING had ever run it.
// Not an oversight in the bench suite - a structural gap: Career.QueueReward
// is wrapped in `#if !UNITY_EDITOR`, so no editor bench can queue a reward,
// and no editor bench can therefore make a RewardBox. BatchSmoke.Map drives a
// real chest and still never sees one.
//
// This bench goes round that by building the box directly through
// RewardBox.TestMake, which runs the real Build() against a screen handed in
// rather than the one headless reports (640x480 at dpi 266, immovable - see
// PhoneLayoutBench's header for the two things that were tried). Every number
// below is read back off the REAL RectTransforms, not recomputed, so the
// layout arithmetic and the hierarchy that consumes it are checked together.
//
// PURE: no play mode, no scene, no BuilderManager, no Career.Data. The
// fixtures are still built under a counted Career.SuspendAutosave() hold,
// because RewardBox.OnDestroy calls Career.GrantReward and owner state is
// sacred even when the id cannot match anything (house rule 5).
//
// ---- WHAT IT IS CHECKING, AND THE TWO DEFECTS IT WAS WRITTEN AGAINST ------
// Both measured 2026-09-13 on a 932x430 CSS-pt landscape phone, which the web
// template renders into an 1864x860 framebuffer (devicePixelRatio capped at 2).
//
//   FINDING 10 - the reward pictures overlapped on EVERY chest. Pitch was
//   38*k*v, picture was 44*k: bigger than the pitch before the missing `v` is
//   counted, and QuickBoxRoll always emits exactly two lines, so the coins
//   always sat on the part. 15.6 framebuffer px of overlap at dpr 3.
//
//   FINDING 9 - the canvas has no CanvasScaler, so a unit is a framebuffer
//   pixel (0.5 CSS px at dpr 2+), while `k` came from the UNCAPPED dpr. CLAIM
//   measured 38.9 CSS px at dpr 3 and 25.9 at dpr 2 - smaller on the better
//   screen, never reaching the 44 pt touch floor - and the hint was 8.8 CSS px
//   against a 14 CSS px readable floor.
//
// PREDICTION, written before the first run (house rule 6): with RewardBox's
// `k` derived from a touch row instead of dpi, CLAIM is 52 CSS px on every
// density and the pictures clear each other by ~18% of a pitch, so all four
// cases pass; the one thing I expected to have to argue about is the SHORT
// case, where v hits its 0.45 clamp - see its note below.
// ===========================================================================
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RobotBrawl.Phase0
{
    public static class RewardBoxBench
    {
        public static int passed, failed;
        public static string report = "";
        static readonly List<string> log = new List<string>();
        static void Check(bool ok, string label)
        {
            if (ok) passed++; else failed++;
            log.Add((ok ? "PASS  " : "FAIL  ") + label);
        }
        static void Note(string s) { log.Add("      " + s); }

        /// <summary>Apple's floor for a tap target, and the floor MobileBuilderUI
        /// holds the dock to: TouchRow() is 44 pt exactly, with zero margin.</summary>
        const float TOUCH_FLOOR = 44f;
        /// <summary>MobileBuilderUI.DesktopFontUnits() floors browser type at 14
        /// CSS px; on a handheld the unit is the point and Apple's smallest
        /// recommended size is 11.</summary>
        const float READABLE_CSS = 14f, READABLE_PT = 11f;

        struct Case
        {
            public string name;
            public RewardBox.Metrics m;
        }

        static Case Web(string name, float cssW, float cssH, float dpr)
        {
            // The template caps the framebuffer at devicePixelRatio 2
            // (Assets/WebGLTemplates/RobotBrawl/index.html), and the jslib
            // reports the ratio AFTER that cap, so this is what the game sees.
            float pr = Mathf.Min(dpr, 2f);
            var c = new Case { name = name };
            c.m = new RewardBox.Metrics
            {
                screenW = Mathf.Round(cssW * pr),
                screenH = Mathf.Round(cssH * pr),
                pixelRatio = pr,
                dpi = 96f * dpr,          // what Unity's WebGL Screen.dpi reports: uncapped
                physical = false
            };
            return c;
        }

        static Case Device(string name, float pxW, float pxH, float dpi)
        {
            return new Case
            {
                name = name,
                m = new RewardBox.Metrics { screenW = pxW, screenH = pxH, pixelRatio = 1f, dpi = dpi, physical = true }
            };
        }

        /// <summary>One physical unit of this screen, in canvas units: a CSS
        /// pixel in a browser, a point on a handheld.</summary>
        static float UnitsPerPhysical(Case c) { return c.m.physical ? c.m.dpi / 163f : c.m.pixelRatio; }
        static float Phys(Case c, float units) { return units / UnitsPerPhysical(c); }
        static string U(Case c) { return c.m.physical ? " pt" : " CSS px"; }

        public static bool RunPure()
        {
            passed = failed = 0; log.Clear();
            var hold = Career.SuspendAutosave();
            try
            {
                var cases = new[]
                {
                    // The measurement basis: owen's landscape phone in the
                    // browser. dpr 3 and dpr 2 are the SAME framebuffer because
                    // of the template's cap - which is exactly the asymmetry
                    // that made the old dpi-derived `k` shrink CLAIM.
                    Web("web phone 932x430 CSS @dpr3", 932f, 430f, 3f),
                    Web("web phone 932x430 CSS @dpr2", 932f, 430f, 2f),
                    Web("web phone 932x430 CSS @dpr1", 932f, 430f, 1f),
                    Web("desktop 1440x820 CSS @dpr1", 1440f, 820f, 1f),
                    // owen's iPhone in landscape; the numbers are PhoneLayoutBench's.
                    Device("iPhone landscape 2532x1170 @460dpi", 2532f, 1170f, 460f),
                    // THE SHORTEST SCREEN THE COMPRESSION SUPPORTS, and the case
                    // that matters most: a phone in a browser with a toolbar
                    // eating the screen. v's floor is 0.45, which it reaches at
                    // a half-screen of 24k + 0.45*237k = 130.65k units, i.e.
                    // 617.6 framebuffer px = 308.8 CSS pt at this k. 300 CSS pt
                    // is at that floor. Below it v saturates, the composition
                    // stops shrinking, and the title leaves the top of the
                    // screen - which is what the clamp exists to say, and is
                    // outside what this layout claims to support.
                    Web("SHORT web phone 932x300 CSS @dpr2 (v at its floor)", 932f, 300f, 2f)
                };
                foreach (var c in cases) Measure(c);
                Density();
            }
            catch (Exception e) { Check(false, "unexpected exception: " + e); }
            finally { hold.Dispose(); }
            report = string.Join("\n", log) + "\nRESULT: " + passed + " pass, " + failed + " fail"
                   + (passed == 0 ? "  - NOTHING RAN, this is not a pass" : "");
            Debug.Log("[RewardBoxBench] " + report);
            return failed == 0 && passed > 0;
        }

        /// <summary>The reward a Quick Fight actually pays: QuickBoxRoll always
        /// emits exactly these two lines, the first a scrap line (coins) and the
        /// second the part (its own picture), which is why their two pictures
        /// are always adjacent and why the overlap fired on every chest.</summary>
        static Career.RewardPop Fixture()
        {
            var p = new Career.RewardPop
            {
                title = "TOOLBOX",
                caption = "Prised open in the yard.",
                id = "qbox:70:plate:Steel:1"
            };
            p.lines.Add("+70 SCRAP");
            p.lines.Add("1 x ARMOR PLATE (Steel)");
            return p;
        }

        static void Measure(Case c)
        {
            RewardBox box = null;
            try
            {
                box = RewardBox.TestMake(Fixture(), c.m);
                var L = box.BuiltLayout;
                var root = box.transform.GetChild(0);          // RewardBoxCanvas
                log.Add("--- " + c.name + "  (framebuffer " + c.m.screenW + "x" + c.m.screenH
                        + ", " + UnitsPerPhysical(c).ToString("0.000") + " units per" + U(c)
                        + ", k=" + L.k.ToString("0.000") + " v=" + L.v.ToString("0.000") + ")");

                // ---- FINDING 9: the tap target ------------------------------
                var claimRt = Rt(root, "claim");
                Check(claimRt != null, c.name + ": CLAIM exists");
                if (claimRt == null) return;
                float claimPt = Phys(c, claimRt.sizeDelta.y);
                Note("CLAIM " + claimRt.sizeDelta.x.ToString("0.0") + "x" + claimRt.sizeDelta.y.ToString("0.0")
                     + " units = " + claimPt.ToString("0.00") + U(c) + " tall");
                Check(claimPt >= TOUCH_FLOOR - 0.01f,
                      c.name + ": CLAIM is at least one touch row (" + claimPt.ToString("0.00") + U(c) + " >= " + TOUCH_FLOOR + ")");
                Check(Mathf.Abs(claimRt.sizeDelta.y - L.claimH) < 0.01f && Mathf.Abs(claimRt.sizeDelta.x - L.claimW) < 0.01f,
                      c.name + ": the CLAIM rect is the measured layout, not a second literal");

                // On screen, and clear of the crate it sits under.
                float claimTop = claimRt.anchoredPosition.y + claimRt.sizeDelta.y * 0.5f;
                float claimBottom = claimRt.anchoredPosition.y - claimRt.sizeDelta.y * 0.5f;
                Check(claimBottom >= -c.m.screenH * 0.5f,
                      c.name + ": CLAIM's bottom edge is on the screen (" + claimBottom.ToString("0.0")
                      + " >= " + (-c.m.screenH * 0.5f).ToString("0.0") + ")");
                var boxRt = Rt(root, "box");
                Check(boxRt != null, c.name + ": the crate exists");
                if (boxRt != null)
                {
                    float crateBottom = boxRt.anchoredPosition.y - boxRt.sizeDelta.y * 0.5f;
                    Note("crate bottom " + crateBottom.ToString("0.0") + ", CLAIM top " + claimTop.ToString("0.0"));
                    Check(claimTop <= crateBottom + 0.01f,
                          c.name + ": CLAIM does not sit on top of the crate");
                }

                // ---- FINDING 10: the reward pictures -------------------------
                Check(L.thumbUnits < L.linePitch,
                      c.name + ": a reward picture is smaller than the pitch it sits in ("
                      + L.thumbUnits.ToString("0.0") + " < " + L.linePitch.ToString("0.0") + " units)");
                var thumbs = new List<RectTransform>();
                var thumbY = new List<float>();
                for (int i = 0; i < L.lineY.Length; i++)
                {
                    var line = Rt(root, "line" + i);
                    if (line == null) continue;
                    var th = Rt(line, "thumb" + i);
                    if (th == null) continue;
                    thumbs.Add(th);
                    // The picture is centred on its line, and the line settles at
                    // its home; the reveal lerp only ever makes it smaller.
                    thumbY.Add(L.lineY[i]);
                    Check(Mathf.Abs(th.sizeDelta.y - L.thumbUnits) < 0.01f
                          && Mathf.Abs(th.sizeDelta.x - L.thumbUnits) < 0.01f,
                          c.name + ": picture " + i + "'s rect is the measured layout, not a second literal");
                }
                Check(thumbs.Count >= 2, c.name + ": both reward lines got a picture (the adjacent pair that overlapped)");
                for (int i = 0; i + 1 < thumbs.Count; i++)
                {
                    float gap = Mathf.Abs(thumbY[i] - thumbY[i + 1])
                              - (thumbs[i].sizeDelta.y + thumbs[i + 1].sizeDelta.y) * 0.5f;
                    Note("pictures " + i + "/" + (i + 1) + " clear each other by " + gap.ToString("0.0")
                         + " units = " + Phys(c, gap).ToString("0.00") + U(c));
                    Check(gap > 0f, c.name + ": reward pictures " + i + " and " + (i + 1) + " do not overlap");
                }

                // ---- FINDING 9: the type ------------------------------------
                float floor = c.m.physical ? READABLE_PT : READABLE_CSS;
                CheckType(c, root, "hint", floor);
                CheckType(c, root, "caption", floor);
                CheckType(c, root, "title", floor);
                for (int i = 0; i < L.lineY.Length; i++) CheckType(c, root, "line" + i, floor);

                // ---- THE INVARIANT: nothing overlaps anything ----------------
                Stack(c, root, L);

                // (the composition's fit on the screen is Stack()'s last two
                // checks - it measures the real line box, not fontSize*0.5)
            }
            finally
            {
                if (box != null) UnityEngine.Object.DestroyImmediate(box.gameObject);
            }
        }

        /// <summary>THE INVARIANT, not the instance: walk the whole composition
        /// top to bottom and assert that no element overlaps the one below it,
        /// and that the stack fits between the screen's edges.
        ///
        /// This is the check that had to exist. Every one of the four defects
        /// found in this file on 2026-09-13 is a factor applied to some of a
        /// group and not the rest, and every one of them is INVISIBLE at v near
        /// 1 - a bench written against owen's phone alone passes all four. Run
        /// against v's 0.45 floor it caught a fifth: the authored offsets put the
        /// title 12.9 units into the caption and the caption 3.3 into the first
        /// reward line, because the composition compresses with v while the type
        /// is held at a physical floor. Those two numbers are the control leg
        /// below, so they are re-measured rather than remembered.
        ///
        /// HEIGHTS ARE UNITY'S OWN, not the layout's model of them:
        /// Text.preferredHeight is the real generated line box, so this check
        /// cannot agree with Measure() by sharing its assumption. Where a line
        /// also carries a picture the taller of the two is its extent, and the
        /// picture's rect is read off the RectTransform.
        ///
        /// The hint and CLAIM are ONE slot on purpose: they occupy the same y,
        /// the hint fading out as the lid opens and CLAIM fading in after, so
        /// they are never both drawn. Treating them as two elements would
        /// assert a separation the design does not want.</summary>
        static void Stack(Case c, Transform root, RewardBox.Layout L)
        {
            var names = new List<string>();
            var tops = new List<float>();
            var bottoms = new List<float>();
            Action<string, float, float> add = (name, centre, half) =>
            { names.Add(name); tops.Add(centre + half); bottoms.Add(centre - half); };

            add("title", L.titleY, Extent(root, "title", 0f) * 0.5f);
            add("caption", L.captionY, Extent(root, "caption", 0f) * 0.5f);
            for (int i = 0; i < L.lineY.Length; i++)
            {
                var line = Rt(root, "line" + i);
                var th = line != null ? Rt(line, "thumb" + i) : null;
                add("line" + i, L.lineY[i], Extent(root, "line" + i, th != null ? th.sizeDelta.y : 0f) * 0.5f);
            }
            var boxRt = Rt(root, "box");
            if (boxRt != null) add("crate", boxRt.anchoredPosition.y, boxRt.sizeDelta.y * 0.5f);
            var claimRt = Rt(root, "claim");
            if (claimRt != null) add("hint/CLAIM", claimRt.anchoredPosition.y, claimRt.sizeDelta.y * 0.5f);

            for (int i = 0; i + 1 < names.Count; i++)
            {
                float clear = bottoms[i] - tops[i + 1];
                Note(names[i] + " -> " + names[i + 1] + ": " + clear.ToString("0.0") + " units clear");
                Check(clear >= 0f, c.name + ": " + names[i] + " does not overlap " + names[i + 1]
                      + " (" + clear.ToString("0.0") + " units)");
            }
            // CONTROL LEG (house rule 4): the same measured extents against the
            // AUTHORED offsets - 215 / 182 / 140 design units times v, which is
            // where these three sat before the stack was built out of gaps. A
            // negative number here is the defect the gaps exist to close, and a
            // control that never goes negative would mean this fix moved nothing.
            if (names.Count >= 3)
            {
                float titleHalf = (tops[0] - bottoms[0]) * 0.5f;
                float capHalf = (tops[1] - bottoms[1]) * 0.5f;
                float lineHalf = (tops[2] - bottoms[2]) * 0.5f;
                Note("control, authored offsets: title/caption " + ((215f - 182f) * L.c - titleHalf - capHalf).ToString("0.0")
                     + ", caption/line0 " + ((182f - 140f) * L.c - capHalf - lineHalf).ToString("0.0") + " units clear");
            }

            float half = c.m.screenH * 0.5f;
            Note("stack spans " + tops[0].ToString("0.0") + " to " + bottoms[bottoms.Count - 1].ToString("0.0")
                 + " against a half-screen of " + half.ToString("0.0"));
            Check(tops[0] <= half, c.name + ": the top of the composition is on the screen");
            Check(bottoms[bottoms.Count - 1] >= -half, c.name + ": the bottom of the composition is on the screen");
        }

        /// <summary>An element's real vertical extent in canvas units: Unity's
        /// generated line box for the label, or the picture beside it if that is
        /// taller. preferredHeight needs a generated mesh; if it comes back at
        /// zero the bench says so rather than passing on a height of nothing.</summary>
        static float Extent(Transform root, string name, float pictureUnits)
        {
            var rt = Rt(root, name);
            var tx = rt != null ? rt.GetComponent<Text>() : null;
            float h = 0f;
            if (tx != null)
            {
                h = tx.preferredHeight;
                // No font mesh in this environment: fall back to the layout's own
                // line-box ratio and SAY SO, so a silent zero cannot read green.
                if (h < 1f) { h = tx.fontSize * RewardBox.TEXT_BOX; Note(name + ": preferredHeight was 0, using fontSize * TEXT_BOX"); }
            }
            return Mathf.Max(h, pictureUnits);
        }

        static void CheckType(Case c, Transform root, string name, float floor)
        {
            var rt = Rt(root, name);
            var tx = rt != null ? rt.GetComponent<Text>() : null;
            if (tx == null) { Check(false, c.name + ": " + name + " has no Text to measure"); return; }
            float pt = Phys(c, tx.fontSize);
            Note(name + " " + tx.fontSize + " units = " + pt.ToString("0.00") + U(c));
            Check(pt >= floor - 0.01f,
                  c.name + ": " + name + " clears the readable floor (" + pt.ToString("0.00") + U(c) + " >= " + floor + ")");
        }

        /// <summary>THE REGRESSION THAT DEFINED FINDING 9: the same physical
        /// screen used to give a DIFFERENT physical CLAIM on every density,
        /// because `k` came from the uncapped device pixel ratio while the
        /// framebuffer was capped at 2. One screen, three densities, one answer.</summary>
        static void Density()
        {
            log.Add("--- density independence (932x430 CSS at dpr 1, 2 and 3)");
            float[] want = new float[3];
            float[] dprs = { 1f, 2f, 3f };
            for (int i = 0; i < dprs.Length; i++)
            {
                var c = Web("d" + i, 932f, 430f, dprs[i]);
                var L = RewardBox.Measure(c.m, 2);
                want[i] = Phys(c, L.claimH);
                Note("dpr " + dprs[i] + ": CLAIM " + want[i].ToString("0.00") + " CSS px");
            }
            Check(Mathf.Abs(want[0] - want[1]) < 0.01f && Mathf.Abs(want[1] - want[2]) < 0.01f,
                  "CLAIM is the same physical size at dpr 1, 2 and 3 on one screen");
        }

        static RectTransform Rt(Transform root, string name)
        {
            var t = root.Find(name);
            return t != null ? t as RectTransform : null;
        }
    }
}
#endif
