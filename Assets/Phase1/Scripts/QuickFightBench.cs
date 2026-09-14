// ===========================================================================
// QuickFightBench.cs — the Quick Fight loop, measured (plan step 1-2,
// docs/CATS_Gap_Analysis_Plan_2026-09-07.md).
//
// What it proves, in one play session:
//   1. a quick bout ENDS within its 30-s clock (+ a settle margin) - the
//      crusher walls spawned and the fight did not drift to the judges;
//   2. the settle counts: quickFights, streak, the box meter, a toolbox on
//      the third win, a crown on the fifth, the daily cap, and the day reset;
//   3. the box's grant is exact and self-describing (qbox:<scrap>:<part>:<mat>:<n>).
//   4. THE FIGHT HUD'S GEOMETRY ON A PHONE (2026-09-13): the IMGUI scale,
//      the three layout thresholds and the QUIT button measured in CSS
//      pixels on a simulated 932x430 phone at dpr 1, 2 and 3 - with the
//      desktop-browser control leg beside them, because the whole claim of
//      the GuiScale change is that a desktop is left where it was. The notch
//      and the home indicator are POSED through SafeAreaWeb's forcing seam,
//      so QUIT and the debrief's exit buttons are checked against a real
//      cut-out rather than against an estimate of one.
//
// OWNER STATE IS SACRED: Career.Data is swapped for a fresh career, autosave
// is held for the whole run (counted hold), and the original is restored.
// Run: BatchSmoke.Quick (headless) or QuickFightBench.Run() in play mode.
// ===========================================================================
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RobotBrawl.Phase0
{
    public class QuickFightBench : MonoBehaviour
    {
        public static bool finished;
        public static int passed, failed;
        static readonly List<string> log = new List<string>();

        public static QuickFightBench Run()
        {
            finished = false; passed = failed = 0; log.Clear();
            var go = new GameObject("QuickFightBench");
            return go.AddComponent<QuickFightBench>();
        }

        static void Check(bool ok, string what)
        {
            if (ok) passed++; else failed++;
            log.Add((ok ? "PASS  " : "FAIL  ") + what);
        }

        IEnumerator Start()
        {
            var bm = Object.FindFirstObjectByType<BuilderManager>();
            BuilderManager.bootToYard = false;   // this bench asserts against the workshop
            if (bm == null) bm = new GameObject("BuilderManager").AddComponent<BuilderManager>();
            yield return null; yield return null;

            var savedData = Career.Data;
            bool savedActive = Career.active;
            var hold = Career.SuspendAutosave();
            Career.Data = new CareerData();
            Career.active = true;
            var d = Career.Data;
            d.taskFight = d.taskBolt = d.taskWeld = d.taskBuy = true; d.rescueGranted = true; d.guideDone = true;
            // a fightable machine the career owns: the starter, topped up
            d.stable.Add(new CareerRobot { name = "SCRAPPER", snapshot = BuilderManager.STARTER_SNAPSHOT,
                                           program = RobotProgram.RamHunter().ToJson() });
            d.activeRobot = 0;
            Career.TopUpForSnapshot(BuilderManager.STARTER_SNAPSHOT);
            bm.LoadSnapshot(BuilderManager.STARTER_SNAPSHOT);
            yield return null;

            // ---- 1. a bout that ends on its own clock -------------------------
            var pool = Career.QuickPool();
            Check(pool.Count == 3, "the pool offers three opponents (" + pool.Count + ")");
            Check(pool[0].oppId != pool[1].oppId && pool[1].oppId != pool[2].oppId, "...all different");
            float t0 = Time.time;
            bm.StartQuickFight(0);
            var fm = Object.FindFirstObjectByType<FightManager>();
            Check(fm != null && bm.mode == BuilderManager.Mode.Fight, "StartQuickFight enters a fight");
            Check(FightManager.quickBout && Career.quickFight, "...flagged as a quick bout");
            Check(fm != null && fm.timer <= FightManager.QUICK_MATCH_TIME + 0.01f, "...on the 30-s clock (" + (fm != null ? fm.timer.ToString("0.0") : "-") + ")");
            bool crushSeen = false;
            float deadline = Time.time + FightManager.QUICK_MATCH_TIME + 15f;
            while (fm != null && fm.state != FightManager.State.Ended && Time.time < deadline)
            {
                if (fm.CrushStarted) crushSeen = true;
                yield return null;
            }
            float took = Time.time - t0;
            Check(fm != null && fm.state == FightManager.State.Ended, "the bout ended (" + took.ToString("0.0") + " s)");
            Check(took <= FightManager.QUICK_MATCH_TIME + 8f, "...inside the clock plus a settle margin");
            Check(crushSeen || took < FightManager.QUICK_MATCH_TIME - FightManager.QUICK_CRUSH_AT,
                  crushSeen ? "the walls closed in for the last 10 s" : "it ended before the walls were due");
            Check(d.quickFights == 1 && d.fights == 1, "settled once: quickFights=" + d.quickFights + " fights=" + d.fights);
            Check(!Career.quickFight && !FightManager.quickBout, "the quick flags clear at the bell");
            bm.BackToBuild(); yield return null;

            // ---- 2. the meter, without fighting 20 more bouts -------------------
            Career.Data = new CareerData();
            d = Career.Data;
            d.taskFight = d.taskBolt = d.taskWeld = d.taskBuy = true; d.rescueGranted = true; d.guideDone = true;
            int scrap0 = d.scrap;
            Career.SettleQuickFight(true, 100f);
            Check(d.quickWins == 1 && d.quickStreak == 1 && d.quickWinsToBox == 1, "win 1: streak 1, meter 1/3");
            Check(d.scrap > scrap0, "a win pays (" + (d.scrap - scrap0) + ")");
            Career.SettleQuickFight(true, 100f);
            Check(d.pendingRewards.Count == 0, "no box before the third win");
            // In the editor a queued reward is GRANTED at once (no box), so the
            // third win must land the toolbox's scrap on top of the win's pay.
            int scrapBefore3 = d.scrap; int items3 = 0; foreach (var it in d.inventory) items3 += it.count;
            Career.SettleQuickFight(true, 100f);
            int items4 = 0; foreach (var it in d.inventory) items4 += it.count;
            Check(d.scrap - scrapBefore3 > Career.lastQuickPay && items4 > items3,
                  "win 3: the toolbox paid scrap and a part on top of the win (+" + (d.scrap - scrapBefore3) + " scrap, +" + (items4 - items3) + " part)");
            Check(d.quickBoxesToday == 1 && d.quickWinsToBox == 0, "...meter reset, 1 box today");
            // The id is self-describing: roll one, queue it by hand, grant it.
            string[] lines;
            string boxId = Career.QuickBoxRoll(1, out lines);
            var f = boxId.Split(':');
            int bScrap = 0, bCount = 0;
            bool wellFormed = f.Length == 5 && int.TryParse(f[1], out bScrap) && int.TryParse(f[4], out bCount);
            Check(wellFormed && bCount == 2, "the box id is self-describing and a crown adds a part (" + boxId + ")");
            d.pendingRewards.Add(boxId);
            int before = d.scrap; int had = wellFormed ? Career.CountOf(f[2], f[3]) : 0;
            Career.GrantReward(boxId);
            Check(wellFormed && d.scrap == before + bScrap && Career.CountOf(f[2], f[3]) == had + bCount,
                  "opening it grants exactly what it says");
            Career.GrantReward(boxId);
            Check(d.scrap == before + bScrap && d.pendingRewards.Count == 0, "...and it is granted once");
            Career.SettleQuickFight(false, 0f);
            Check(d.quickStreak == 0 && d.quickWinsToBox == 0, "a loss resets the streak, not the box meter");
            for (int i = 0; i < 5; i++) Career.SettleQuickFight(true, 100f);
            Check(d.crowns >= 1 || d.pendingRewards.Exists(x => x.Contains("CROWN") || x.StartsWith("qbox:")), "five in a row: a crown was earned or spent on a box");
            // the daily cap
            d.pendingRewards.Clear(); d.quickWinsToBox = 0; d.crowns = 0;
            d.quickBoxesToday = Career.QUICK_BOXES_PER_DAY;
            for (int i = 0; i < 3; i++) Career.SettleQuickFight(true, 100f);
            Check(d.pendingRewards.Count == 0, "at the daily cap the third win queues no box");
            Check(Career.lastQuickLine.Contains("tomorrow"), "...and the card says why");
            d.quickBoxDay = "2000-01-01";
            for (int i = 0; i < 3; i++) Career.SettleQuickFight(true, 100f);
            Check(d.quickBoxesToday == 1 && Career.lastQuickLine.Contains("TOOLBOX"), "a new day resets the cap");
            Check(Career.TxnSum() == d.scrap, "the ledger still audits (sum of txns == scrap)");

            // ---- 4. THE FIGHT HUD ON A PHONE ------------------------------------
            // Three defects reached a tester's phone on 2026-09-13 and every
            // bench in this project stayed green, because nothing had ever
            // asked what the fight's IMGUI units come out at PHYSICALLY.
            // FightManager scales by BuilderManager.GuiScale, whose two inputs
            // (Screen.dpi, Screen.height) are both meaningless in a browser -
            // see GuiScale's R10 note for the arithmetic. The checks below are
            // pure geometry: no fight, no career, no yields inside the forced
            // window, so the forcing cannot leak into another frame's OnGUI.
            //
            // The phone is the tester's: 932 x 430 CSS points landscape. The
            // WebGL template's fit() makes the drawing buffer innerWidth and
            // innerHeight times min(devicePixelRatio, 2), so ForceBrowser
            // builds Screen.width/height the same way.
            const float EPS = 0.01f;
            float dockFloorCss = 14f;   // MobileBuilderUI.DesktopFontUnits' own text floor
            // The landscape iPhone this was measured on: a 47 CSS px notch
            // and a 21 CSS px home indicator. Posed, not read - the CSS probe
            // SafeAreaWeb uses only answers in a browser.
            const float notchCss = 47f, homeCss = 21f;
            foreach (float dpr in new[] { 1f, 2f, 3f })
            {
                BuilderManager.ForceBrowser(932f, 430f, dpr);
                float uw = FightManager.HudUnitsW, uh = FightManager.HudUnitsH;
                // (a) THE UNIT SPACE IS THE CSS VIEWPORT, WHATEVER THE DPR.
                // This is the invariant the whole fix rests on: a fixed
                // 1010-unit HUD box can only be reasoned about if the screen's
                // size in units is a property of the PHONE and not of its
                // pixel density. Before the fix the same phone measured 932,
                // 1035.6 and 1553 units wide at dpr 1, 2 and 3.
                Check(Mathf.Abs(uw - 932f) < 1f && Mathf.Abs(uh - 430f) < 1f,
                      "dpr " + dpr + ": the HUD viewport is the CSS viewport ("
                      + uw.ToString("0.0") + "x" + uh.ToString("0.0") + " units)");
                // (b) the three layout thresholds pick the phone's branches
                Check(uw < 1030f, "...narrow fires (the 1010-unit HUD box does not fit)");
                Check(uh < 560f, "...shortH fires (the tall debrief does not fit - iOS QA 2026-09-05)");
                // (c) type lands in a physical band a person can read. The
                // floor is the dock's own: DesktopFontUnits holds every piece
                // of dock text to max(14, pt*1.1) CSS px. The ceiling is the
                // tall-window clamp, 2.2 css per unit, which a 430-tall phone
                // can never reach - so on this screen the band is one number
                // and the assertion is tight, not generous.
                float tiny = FightManager.UnitsToCss(FightManager.HUD_TINY_UNITS);
                float name = FightManager.UnitsToCss(FightManager.HUD_NAME_UNITS);
                Check(tiny >= dockFloorCss - EPS && tiny <= FightManager.HUD_TINY_UNITS * 2.2f + EPS,
                      "...the kJ readout is " + tiny.ToString("0.0") + " CSS px (was 7.0 at dpr 2)");
                Check(name >= FightManager.HUD_NAME_UNITS - EPS && name <= FightManager.HUD_NAME_UNITS * 2.2f + EPS,
                      "...the name/HP line is " + name.ToString("0.0") + " CSS px (was 9.0 at dpr 2)");
                // (d) QUIT is the only touch exit from a running fight.
                Rect q = FightManager.QuitButtonRect();
                float qw = FightManager.UnitsToCss(q.width), qh = FightManager.UnitsToCss(q.height);
                Check(qh >= 44f - EPS && qw >= 44f - EPS,
                      "...QUIT is at least a 44 pt touch row (" + qw.ToString("0") + "x" + qh.ToString("0")
                      + " CSS px, was 44x18 at dpr 2)");
                // (e) THE NOTCH, posed with SafeAreaWeb's forcing seam rather
                // than estimated. Values are FRAMEBUFFER pixels, so a 47 CSS
                // px cut-out is 47*dpr of them - which is the whole reason
                // this is forced in the units the property returns and
                // asserted in the units a finger works in.
                SafeAreaWeb.forcedLeft = notchCss * dpr;
                SafeAreaWeb.forcedBottom = homeCss * dpr;
                Rect qn = FightManager.QuitButtonRect();
                Check(FightManager.UnitsToCss(qn.x) >= notchCss - EPS,
                      "...with a " + notchCss + " CSS px notch, QUIT starts clear of it at "
                      + FightManager.UnitsToCss(qn.x).ToString("0") + " CSS px (was 9)");
                // ...and the Quick debrief's two exit buttons clear the home
                // indicator. They were pinned 8 units off the raw bottom edge,
                // which on a landscape iPhone is inside it.
                foreach (bool stacked in new[] { false, true })
                    Check(FightManager.QuickFooterBottom(stacked) <= FightManager.HudSafeBottom + EPS,
                          "...and the " + (stacked ? "stacked" : "side-by-side")
                          + " debrief buttons end above the home indicator ("
                          + FightManager.UnitsToCss(FightManager.HudUnitsH - FightManager.QuickFooterBottom(stacked)).ToString("0")
                          + " CSS px of clearance, floor " + homeCss + ")");
                // ...and both screens' exit buttons are a touch row tall. The
                // Quick pair were a flat 48 units, which the old arbitrary
                // scale rendered at 43.2 CSS px; the career row's 46 clears 44
                // only by arithmetic nobody chose.
                Check(FightManager.UnitsToCss(FightManager.QuickButtonH) >= 44f - EPS
                      && FightManager.UnitsToCss(FightManager.ResultsButtonH(true)) >= 44f - EPS,
                      "...and both results screens' exit buttons are a touch row tall ("
                      + FightManager.UnitsToCss(FightManager.QuickButtonH).ToString("0") + " and "
                      + FightManager.UnitsToCss(FightManager.ResultsButtonH(true)).ToString("0") + " CSS px)");
                // (f) A PHONE WITH NO CUT-OUT AND A WORKING PROBE PAYS NOTHING.
                // Until SafeAreaWeb grew `Measured` this state and the one
                // below were the same reading, and the fallback had to fire for
                // both: a notch-less phone was handed 47 px it did not need in
                // order to protect a notched one whose probe had failed. These
                // two checks are here because they used to be one.
                SafeAreaWeb.forcedLeft = 0f; SafeAreaWeb.forcedBottom = 0f;
                Check(Mathf.Abs(FightManager.QuitButtonRect().x - 10f) < EPS
                      && Mathf.Abs(FightManager.QuickFooterBottom(false) - (FightManager.HudUnitsH - 8f)) < EPS,
                      "...a phone that MEASURES no cut-out pays nothing for one");
                SafeAreaWeb.forcedLeft = -1f; SafeAreaWeb.forcedBottom = -1f;
                // (g) A PHONE WHOSE PROBE NEVER ANSWERED gets the constant.
                // Posed with SafeAreaWeb.forcedMeasured, which exists because
                // this state is otherwise UNREACHABLE from any bench: off the
                // web Read takes its native leg and sets Measured true, and
                // forcing an inset sets it true too, so the fallback branch was
                // code no measurement had ever entered.
                SafeAreaWeb.forcedMeasured = false;
                Check(Mathf.Abs(FightManager.UnitsToCss(FightManager.QuitButtonRect().x) - (10f + notchCss)) < 1f
                      && FightManager.UnitsToCss(FightManager.HudUnitsH - FightManager.QuickFooterBottom(false)) >= homeCss - EPS,
                      "...a phone whose probe NEVER ANSWERED falls back to the posed cut-out (QUIT at "
                      + FightManager.UnitsToCss(FightManager.QuitButtonRect().x).ToString("0") + " CSS px)");
                float keepN = FightManager.NOTCH_FALLBACK_CSS, keepH = FightManager.HOME_FALLBACK_CSS;
                FightManager.NOTCH_FALLBACK_CSS = 0f; FightManager.HOME_FALLBACK_CSS = 0f;
                Check(Mathf.Abs(FightManager.QuitButtonRect().x - 10f) < EPS
                      && Mathf.Abs(FightManager.QuickFooterBottom(false) - (FightManager.HudUnitsH - 8f)) < EPS,
                      "...and zeroing the two constants removes even that");
                FightManager.NOTCH_FALLBACK_CSS = keepN; FightManager.HOME_FALLBACK_CSS = keepH;
                SafeAreaWeb.forcedMeasured = null;   // null is the shipped behaviour
            }
            // (h) a PORTRAIT phone is the narrow case the Quick debrief stacks
            // its two buttons for. 430 css wide is the same phone turned round.
            BuilderManager.ForceBrowser(430f, 932f, 3f);
            Check(FightManager.HudUnitsW < 620f && FightManager.HudUnitsH >= 560f,
                  "portrait 430x932 css: stacked fires, shortH does not ("
                  + FightManager.HudUnitsW.ToString("0") + "x" + FightManager.HudUnitsH.ToString("0") + " units)");
            // (i) THE CONTROL LEG, and it is the point of the whole change: a
            // DESKTOP browser must be left where it was. 1920x950 at dpr 1
            // scored clamp(950/900) = 1.0556 under the old rule and has to
            // score it still, with no notch inset and no oversized QUIT.
            BuilderManager.ForceBrowser(1920f, 950f, 1f);
            Check(Mathf.Abs(BuilderManager.GuiScale - 950f / 900f) < 0.001f,
                  "desktop 1920x950 dpr 1: the scale is unchanged at "
                  + BuilderManager.GuiScale.ToString("0.000"));
            Check(Mathf.Abs(FightManager.SafeLeftUnits) < EPS && FightManager.HudUnitsW >= 1030f,
                  "...no cut-out and the desktop HUD branch, as before");
            Check(Mathf.Abs(FightManager.UnitsToCss(FightManager.QuitButtonRect().height) - 44f) < 0.5f,
                  "...QUIT is still exactly one touch row, not a phone-sized one");
            // (j) and NOTHING is forced any more: the native rule, untouched.
            BuilderManager.ClearForcedBrowser();
            float nDpi = Screen.dpi > 250f ? Mathf.Min(2.5f, Screen.dpi / 160f) : 1f;
            float nH = Mathf.Clamp(Screen.height / 900f, 1f, 2.2f);
            Check(Mathf.Abs(BuilderManager.GuiScale - Mathf.Max(nDpi, nH)) < 0.0001f,
                  "off the web the old rule still decides the scale ("
                  + BuilderManager.GuiScale.ToString("0.000") + " at dpi " + Screen.dpi
                  + ", " + Screen.width + "x" + Screen.height + ")");

            // ---- restore ---------------------------------------------------------
            Career.Data = savedData;
            Career.active = savedActive;
            hold.Dispose();
            foreach (var l in log) Debug.Log("[QuickFightBench] " + l);
            Debug.Log(string.Format("[QuickFightBench] RESULT: {0} pass, {1} fail{2}", passed, failed,
                      failed == 0 ? " - ALL GREEN" : " - FIX NEEDED"));
            finished = true;
        }
    }
}
#endif
