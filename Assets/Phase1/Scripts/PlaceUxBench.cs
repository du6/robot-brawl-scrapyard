// ⚠ EDITOR AND DEVELOPMENT BUILDS ONLY — see TouchSmoke.cs's header for why.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RobotBrawl.Phase0
{
/// <summary>
/// PLACEMENT UX — the 2026-09-14 play-test round, measured.
///
/// Five asks came out of that round and four of them are in here. What makes
/// them checkable is that each one has a property that is true of the WHOLE
/// machine rather than of a spot someone remembered to look at:
///
///   * the guide never lies      - every face it lights accepts the held part
///   * the ghost never overlaps  - no green ghost stands in an occupied box,
///                                 including the ones the nudge search moved
///   * the nudge is bounded      - and it FIRES, which is the half a
///                                 "does no harm" check cannot see
///   * no refusal is anonymous   - "Blocked by another part" appears NOWHERE in
///                                 a sweep of the whole machine, and every
///                                 "Hits ..." names a part that is really there
///   * the face is steady        - a sub-threshold wobble across a real face
///                                 boundary does not switch it, and a
///                                 deliberate move still does
///
/// ⚠ WHAT THIS BENCH DOES NOT COVER, stated rather than left to be assumed:
/// THE GESTURE THAT ARMS THE PREVIEW. Arming happens on the release edge inside
/// MobileBuilderUI.Pointers - finger down, no swipe, lift - and driving it
/// needs a synthetic Touchscreen device, which this project has never had a
/// harness for. The bench arms through TestArmPreview and then presses ATTACH,
/// ROTATE and CANCEL through their REAL onClick, which is the layer CLAUDE.md's
/// WATCH precedent says the behaviour actually lives in. So: the buttons are
/// covered, the gesture is not, and the gesture is where the next defect will
/// be. Naming it beats a check that reaches past it and proves nothing.
/// </summary>
public class PlaceUxBench : MonoBehaviour
{
    public static PlaceUxBench Run()
    { return new GameObject("place_ux_bench").AddComponent<PlaceUxBench>(); }

    public int passed, failed;
    public bool finished;
    readonly List<string> log = new List<string>();

    void Check(bool ok, string what)
    {
        if (ok) passed++; else failed++;
        log.Add((ok ? "PASS  " : "FAIL  ") + what);
        Debug.Log("[PlaceUxBench] " + (ok ? "PASS  " : "FAIL  ") + what);
    }

    BuilderManager bm;

    IEnumerator Aim(Vector3 world)
    {
        Vector3 sp = bm.TestCam.WorldToScreenPoint(world);
        Phase0Input.debugPointer = true;
        Phase0Input.debugMousePos = new Vector3(sp.x, sp.y, 0f);
        yield return null; yield return null;
    }

    IEnumerator AimScreen(Vector2 sp)
    {
        Phase0Input.debugPointer = true;
        Phase0Input.debugMousePos = new Vector3(sp.x, sp.y, 0f);
        yield return null; yield return null;
    }

    /// <summary>Hold palette part `i`.
    /// ⚠ bm.SelectPart TOGGLES - calling it with the part already held DROPS it
    /// (that is the deselect seam TouchSmoke uses). The first run of this bench
    /// re-selected the Beam it was already holding before every section, so the
    /// builder had nothing in hand and reported no guide, no probes and no
    /// preview: TEN failures, all of them this one line. The check was wrong,
    /// not the product - CLAUDE.md house rule 2, earning its keep again.</summary>
    void Hold(int i)
    {
        if (i < 0) return;
        if (bm.SelectedPart != i) bm.SelectPart(i);
    }

    /// <summary>Screen-space bounding box of the whole machine. Every sweep in
    /// this bench aims at THIS rather than at a fraction of the window: the
    /// builder camera frames against the dock (GarageFitDistance), so a guessed
    /// rectangle misses, and a sweep that misses says "nothing wrong" in the
    /// same words as a sweep that found nothing wrong.</summary>
    bool MachineBox(out Rect box)
    {
        float lox = float.MaxValue, loy = float.MaxValue, hix = float.MinValue, hiy = float.MinValue;
        for (int pi = 0; pi < bm.PlacedCount; pi++)
        {
            Vector3 c = bm.PlacedPos(pi), h = bm.PlacedHalf(pi);
            for (int k = 0; k < 8; k++)
            {
                Vector3 corner = c + new Vector3((k & 1) == 0 ? -h.x : h.x,
                                                 (k & 2) == 0 ? -h.y : h.y,
                                                 (k & 4) == 0 ? -h.z : h.z);
                Vector3 sc = bm.TestCam.WorldToScreenPoint(corner);
                if (sc.z <= 0f) continue;
                lox = Mathf.Min(lox, sc.x); hix = Mathf.Max(hix, sc.x);
                loy = Mathf.Min(loy, sc.y); hiy = Mathf.Max(hiy, sc.y);
            }
        }
        box = new Rect(lox, loy, hix - lox, hiy - loy);
        return hix > lox && hiy > loy;
    }

    int PaletteIndex(string prefix)
    {
        for (int i = 0; i < bm.PaletteCount; i++)
            if (bm.PartLabel(i).StartsWith(prefix)) return i;
        return -1;
    }

    IEnumerator Start()
    {
        // OWNER STATE IS SACRED (CLAUDE.md house rule 5, and the 2026-09-04
        // incident behind it): anything that drives the builder can reach
        // product code that calls Save.
        var hold = Career.SuspendAutosave();
        var ms = GameObject.Find("ModeSelect");
        if (ms != null) Destroy(ms);
        MobileBuilderUI.forceMobileUI = true;
        BuilderManager.bootToYard = false;
        bm = Object.FindFirstObjectByType<BuilderManager>();
        if (bm == null) bm = new GameObject("BuilderManager").AddComponent<BuilderManager>();
        Career.active = false;
        yield return null; yield return null; yield return null;
        if (bm.TestCam == null)
        {
            Check(false, "the builder has a camera to raycast through");
            finished = true; hold.Dispose(); yield break;
        }

        // ---- a machine with neighbours on it -------------------------------
        // The sweep below is only worth anything against a build where parts
        // can actually get in each other's way. A bare core refuses nothing,
        // and a bench run on one would go green on a product that names no
        // blocker at all.
        int beam = PaletteIndex("Beam");
        int plate = PaletteIndex("Plate");
        if (plate < 0) plate = PaletteIndex("Armor");
        int wheel = PaletteIndex("Wheel");
        int spike = PaletteIndex("Spike");
        Check(beam >= 0, "palette offers a Beam to place");
        int before = bm.PlacedCount;
        // Parts CROWDED AROUND THE CORE, on purpose. A sparse machine refuses
        // nothing, and the blocked-socket rules below would then be measured
        // against a build in which they can never fire - a green that means
        // "never asked", which is the failure mode this project has hit three
        // times (docs/Mobile_Round_2026-09-13.md, the three checks that could
        // not fail).
        var seedAt = new List<Vector3> {
            new Vector3(0f, 0.86f, 0f), new Vector3(0.22f, 0.78f, 0f),
            new Vector3(-0.22f, 0.78f, 0f), new Vector3(0f, 0.78f, -0.22f),
            new Vector3(0f, 0.78f, 0.22f), new Vector3(0f, 0.98f, -0.12f),
        };
        foreach (var w in seedAt)
        {
            Hold(beam >= 0 ? beam : 0);
            yield return Aim(w);
            if (bm.TestGhostValid) { Phase0Input.DebugClick(0); yield return null; yield return null; }
        }
        Check(bm.PlacedCount > before, "the bench can place through the real pointer path");
        Debug.Log("[PlaceUxBench] state: screen=" + Screen.width + "x" + Screen.height
                  + " placed=" + bm.PlacedCount + " held=" + bm.SelectedPart
                  + " mobileUI=" + MobileBuilderUI.Active
                  + " core@" + bm.TestCam.WorldToScreenPoint(bm.PlacedPos(0))
                  + " faceSwitchPx=" + BuilderManager.FaceSwitchPx);

        // ================= A. THE GUIDE ====================================
        if (beam >= 0) { Hold(beam); yield return null; yield return null; }
        yield return Aim(new Vector3(0f, 5f, 0f));   // off the machine: free-follow
        Check(bm.TestGuideShown && bm.TestGuideFaceCount > 0,
              "holding a part lights its mounting faces BEFORE you aim at one ("
              + bm.TestGuideFaceCount + " faces)");

        // Every lit face accepts the part. Faces that the camera cannot reach
        // are skipped - and the number actually TESTED is asserted, because a
        // sweep that skipped everything is indistinguishable from a sweep that
        // passed everything (CLAUDE.md: a green with passed==0 is not a pass).
        int lit = bm.TestGuideFaceCount, tested = 0, lied = 0;
        var faces = new List<Vector3>();
        var owners = new List<string>();
        for (int i = 0; i < lit; i++) { faces.Add(bm.TestGuideFace(i)); owners.Add(bm.TestGuideFaceOwner(i)); }
        for (int i = 0; i < faces.Count; i++)
        {
            Vector3 f = faces[i];
            Vector3 sp = bm.TestCam.WorldToScreenPoint(f);
            if (sp.z <= 0f || sp.x < 0f || sp.y < 0f || sp.x > Screen.width || sp.y > Screen.height) continue;
            bm.TestClearFaceLock();
            yield return AimScreen(new Vector2(sp.x, sp.y));
            // ⚠ ONLY JUDGE THE MARKER THE RAY ACTUALLY LANDED ON. Half the
            // markers on any machine have another part standing in front of
            // them, and a ray aimed at one of those resolves to the part in
            // front - a fact about where the camera is, not a lie told by the
            // marker. The first run of this bench counted one of those as a lie
            // ("Hits the top Beam", from a probe that never reached the face it
            // was aimed at). Matching the TARGET, not the distance, is what
            // separates the two.
            if (bm.TestGhostTarget != owners[i]) continue;
            if ((bm.TestGhostPos - f).magnitude > 0.45f) continue;
            tested++;
            if (!bm.TestGhostValid) { lied++; Debug.Log("[PlaceUxBench]   lit-but-refused: " + bm.TestGhostReason); }
        }
        Check(tested > 0, "the lit faces are reachable enough to judge (" + tested + " of " + lit + " probed)");
        Check(lied == 0, "every lit face really does take the part (" + lied + " lied of " + tested + ")");

        // The guide is cached on the machine; a changed machine must rebuild it.
        var was = new List<Vector3>(faces);
        if (beam >= 0)
        {
            yield return Aim(new Vector3(0f, 0.86f, -0.22f));
            Phase0Input.DebugClick(0);
            yield return null; yield return null;
            yield return Aim(new Vector3(0f, 5f, 0f));
        }
        bool moved = bm.TestGuideFaceCount != was.Count;
        if (!moved)
            for (int i = 0; i < bm.TestGuideFaceCount && !moved; i++)
                if ((bm.TestGuideFace(i) - was[i]).sqrMagnitude > 1e-6f) moved = true;
        Check(moved, "placing a part rebuilds the guide instead of leaving stale markers");

        // ================= B. THE SWEEP ====================================
        // Every screen point over the machine, at both a Beam and a Wheel. This
        // is the "measure over EVERYTHING rather than a named list" rule: the
        // properties below are asserted at every point the pointer can be, not
        // at a spot chosen because it was known to be interesting.
        int[] subjects = { beam, wheel >= 0 ? wheel : beam, plate >= 0 ? plate : beam };
        int probes = 0, valid = 0, anonymous = 0, wrongName = 0, overlapped = 0;
        int nudges = 0, overReach = 0, hitsMsgs = 0;
        var labels = new List<string>();
        for (int i = 0; i < bm.PlacedCount; i++) labels.Add(bm.PlacedLabel(i));

        foreach (int subj in subjects)
        {
            if (subj < 0) continue;
            Hold(subj);
            yield return null;
            // Centred on where the machine actually IS on screen, not on a
            // fraction of the window: the builder camera frames against the
            // dock (GarageFitDistance), so a guessed rectangle misses it - and
            // a sweep that misses reports "nothing wrong" in the same words as
            // a sweep that found nothing wrong.
            Rect mbox;
            if (!MachineBox(out mbox)) continue;
            for (int gx = 0; gx <= 10; gx++)
                for (int gy = 0; gy <= 10; gy++)
                {
                    float sx = mbox.xMin + mbox.width * (gx / 10f);
                    float sy = mbox.yMin + mbox.height * (gy / 10f);
                    bm.TestClearFaceLock();
                    yield return AimScreen(new Vector2(sx, sy));
                    if (bm.TestGhostTarget == "none") continue;
                    probes++;
                    string why = bm.TestGhostReason;
                    if (bm.TestGhostValid)
                    {
                        valid++;
                        // ⚠ THE SOUNDNESS CHECK. The nudge search moves the ghost;
                        // if it ever moves it somewhere occupied, the rule it was
                        // added to has been turned inside out.
                        if (!bm.TestGhostSpotClear) overlapped++;
                        if (bm.TestGhostNudged)
                        {
                            nudges++;
                            // ⚠ 1.45, NOT 1.0, AND IT IS NOT A LOOSENED THRESHOLD.
                            // NUDGE_REACH caps each TANGENT independently, so a
                            // candidate that moves the cap along both tangents is
                            // sqrt(2) = 1.414 away in the plane. 1.45 is that
                            // bound plus float slack; anything beyond it means a
                            // tangent cap was breached, which is the thing being
                            // checked. (CLAUDE.md house rule 3: if a threshold has
                            // to be loose, say why here, with the number.)
                            if (bm.TestGhostNudgeDist > BuilderManager.NUDGE_REACH * 1.45f) overReach++;
                        }
                    }
                    else
                    {
                        if (why == "Blocked by another part" || why == "Blocked by another part.") anonymous++;
                        if (why.StartsWith("Hits "))
                        {
                            hitsMsgs++;
                            bool named = false;
                            foreach (var lb in labels) if (lb.Length > 0 && why.Contains(lb)) named = true;
                            if (!named) wrongName++;
                        }
                    }
                }
        }
        Check(probes > 40, "the sweep actually landed on the machine (" + probes + " probes)");
        Check(valid > 0, "...and found placements it accepts (" + valid + ")");
        Check(overlapped == 0, "no green ghost ever stands in an occupied box (" + overlapped + " did)");
        Check(anonymous == 0,
              "no refusal anywhere says 'Blocked by another part' (" + anonymous + " did)");
        Check(hitsMsgs > 0, "the machine does refuse by naming what is in the way (" + hitsMsgs + " times)");
        Check(wrongName == 0, "...and every name it gives is a part that is on the robot (" + wrongName + " wrong)");
        Check(nudges > 0, "the blocked-socket search FIRES rather than merely not hurting (" + nudges + ")");
        Check(overReach == 0, "...and never reaches past NUDGE_REACH to do it (" + overReach + " over)");

        // ================= C. FACE HYSTERESIS ==============================
        // The boundary is FOUND, not assumed, and it is found by scanning with
        // the lock CLEARED at every step - the raw pick is the only thing the
        // hysteresis can be compared against, and a control leg that cannot see
        // the uncontrolled value is not a control leg.
        //
        // ⚠ AND BOTH SIDES MUST BE REAL FACES WITH ROOM EITHER SIDE. The first
        // green here read "beam/YP -> none/YP": the deliberate move had simply
        // walked off the machine into free-follow, which proves the lock lets
        // go but not that it ever switches to the face you moved to. A control
        // that passes for the wrong reason is worse than no control.
        Hold(beam);
        yield return null;
        // Two named faces on the core, used by sections D and E below.
        Vector3 top = bm.PlacedPos(0) + new Vector3(0f, bm.PlacedHalf(0).y, 0f);
        Vector3 front = bm.PlacedPos(0) + new Vector3(0f, 0f, -bm.PlacedHalf(0).z);
        float thr = BuilderManager.FaceSwitchPx;
        // ⚠ ZOOM IN FIRST, AND THE REASON IS THE MEASUREMENT ITSELF. At the
        // headless 640x480 fit the whole machine is ~56 px across, so a single
        // mount face is NARROWER THAN THE 8.6 px THRESHOLD - there is no room
        // to put a wobble inside one face and a deliberate move inside the
        // next, and the bench cannot tell a working lock from a stuck one.
        // That is a property of this window, not of the product: on the phones
        // this ships to the build fills the screen. Zooming buys the pixels the
        // measurement needs and changes nothing it measures.
        bm.ZoomGarageView(0.40f);
        yield return null; yield return null;
        Rect box;
        if (!MachineBox(out box)) box = new Rect(Screen.width * 0.4f, Screen.height * 0.4f, 80f, 80f);
        Vector2 hub = box.center;
        float reach = Mathf.Max(thr * 6f, Mathf.Max(box.width, box.height) * 0.62f);
        int STEPS = 120;
        // Three lines across the machine, because a boundary needs a clear run
        // of pixels on BOTH sides and one line through a crowded build may not
        // offer one. The best boundary across all three is the one tested, and
        // the profile is logged either way - a failure here has to be able to
        // name itself rather than leave the next reader guessing at geometry.
        Vector2[][] lines = {
            new[] { new Vector2(hub.x - reach, hub.y), new Vector2(hub.x + reach, hub.y) },
            new[] { new Vector2(hub.x, hub.y - reach), new Vector2(hub.x, hub.y + reach) },
            new[] { new Vector2(hub.x - reach, hub.y - reach), new Vector2(hub.x + reach, hub.y + reach) },
        };
        int edge = -1, bestRun = -1, bestLine = -1;
        var faceAt = new List<string>();
        Vector2 from = Vector2.zero, to = Vector2.zero;
        for (int li = 0; li < lines.Length; li++)
        {
            var seq = new List<string>();
            for (int step = 0; step <= STEPS; step++)
            {
                bm.TestClearFaceLock();
                yield return AimScreen(Vector2.Lerp(lines[li][0], lines[li][1], step / (float)STEPS));
                seq.Add(bm.TestGhostTarget == "none"
                        ? "none"
                        : bm.TestGhostTarget + "@" + bm.TestGhostFaceKey);
            }
            float pxStep = (lines[li][1] - lines[li][0]).magnitude / STEPS;
            int needL = Mathf.CeilToInt((thr + 4f) / Mathf.Max(0.01f, pxStep));
            string rle = ""; int runN = 1;
            for (int k = 1; k <= STEPS; k++)
            {
                if (seq[k] == seq[k - 1]) { runN++; continue; }
                rle += seq[k - 1] + "x" + runN + " "; runN = 1;
            }
            rle += seq[STEPS] + "x" + runN;
            Debug.Log("[PlaceUxBench] line " + li + " need=" + needL + " px/step=" + pxStep.ToString("F2") + "  " + rle);
            for (int step = 1; step <= STEPS; step++)
            {
                if (seq[step] == seq[step - 1] || seq[step] == "none" || seq[step - 1] == "none") continue;
                int rl = 0; while (step - 1 - rl >= 0 && seq[step - 1 - rl] == seq[step - 1]) rl++;
                int rr = 0; while (step + rr <= STEPS && seq[step + rr] == seq[step]) rr++;
                int worst = Mathf.Min(rl, rr);
                if (worst < needL || worst <= bestRun) continue;
                bestRun = worst; edge = step; bestLine = li;
                faceAt = new List<string>(seq);
                from = lines[li][0]; to = lines[li][1];
            }
        }
        Check(edge > 0,
              "found a real face boundary with room on both sides to test it"
              + (edge > 0 ? " (" + faceAt[edge - 1] + " | " + faceAt[edge] + " on line " + bestLine
                            + ", " + bestRun + " steps clear either side)" : " - see the line profiles above"));
        if (edge > 0)
        {
            string sideA = faceAt[edge - 1], sideB = faceAt[edge];
            Vector2 anchor = Vector2.Lerp(from, to, (edge - 1) / (float)STEPS);
            Vector2 dir = (to - from).normalized;

            bm.TestClearFaceLock();
            yield return AimScreen(anchor);
            string held = bm.TestGhostTarget + "@" + bm.TestGhostFaceKey;
            Check(held == sideA, "the lock starts on the face the raw pick gives (" + held + ")");

            // The involuntary wobble: ACROSS the boundary, but under the
            // threshold. This is the movement a resting finger makes on its own,
            // and it is the whole reason the lock exists.
            yield return AimScreen(anchor + dir * (thr * 0.7f));
            string afterWobble = bm.TestGhostTarget + "@" + bm.TestGhostFaceKey;
            Check(afterWobble == held,
                  "a wobble across the boundary, smaller than the threshold, does not change the face ("
                  + held + " -> " + afterWobble + ", raw pick there is " + sideB + ")");

            // CONTROL LEG: the same direction, past the threshold, landing on a
            // REAL face - not off the machine.
            yield return AimScreen(anchor + dir * (thr + 4f));
            string afterMove = bm.TestGhostTarget + "@" + bm.TestGhostFaceKey;
            Check(afterMove != held, "...and a deliberate move still changes it ("
                  + held + " -> " + afterMove + ")");
            Check(bm.TestGhostTarget != "none",
                  "...onto a real mount face, not off the robot (" + afterMove + ")");

            // AN ORBIT RELEASES THE LOCK with the finger perfectly still. The
            // finger has not travelled, so the travel test cannot see this; what
            // has moved is the world under it.
            //
            // ⚠ AND THE WAY TO CHECK IT IS AGAINST THE RAW PICK, NOT AGAINST THE
            // OLD FACE. "It is different from before" was the first version, and
            // it failed while the product was right: TestGhostTarget is the part's
            // DEF ID, so orbiting from one beam onto another beam reads as
            // "beam -> beam" and looks like nothing happened. The question is
            // whether the lock LET GO - i.e. whether the face after the orbit is
            // the one a fresh, unlocked pick gives at the same pixel.
            int turned = 0;
            string locked = "", afterOrbit = "", rawThere = "";
            for (int deg = 30; deg <= 150 && turned == 0; deg += 30)
            {
                bm.TestClearFaceLock();
                yield return AimScreen(anchor);
                locked = bm.TestGhostFaceKey;
                bm.TestOrbitYaw += deg;
                yield return AimScreen(anchor);          // lock still held from before the turn
                afterOrbit = bm.TestGhostFaceKey;
                bm.TestClearFaceLock();
                yield return AimScreen(anchor);          // what an unlocked pick gives here
                rawThere = bm.TestGhostFaceKey;
                if (rawThere != locked) turned = deg; else { bm.TestOrbitYaw -= deg; yield return null; }
            }
            Check(turned > 0,
                  "an orbit can be found that puts a different face under the finger (turned "
                  + turned + " deg)");
            Check(turned > 0 && afterOrbit == rawThere,
                  "...and turning the robot under a still finger re-picks it rather than holding the old face ("
                  + locked + " -> " + afterOrbit + ", raw pick there is " + rawThere + ")");
            if (turned > 0) bm.TestOrbitYaw -= turned;
            yield return null;
        }
        bm.FitGarageView();
        yield return null; yield return null;

        // ================= D. DIRECTION ARROW ==============================
        if (spike >= 0)
        {
            Hold(spike);
            bm.TestClearFaceLock();
            yield return Aim(front);
            bool arrowOnWeapon = bm.TestArrowShown;
            Hold(beam >= 0 ? beam : spike);
            bm.TestClearFaceLock();
            yield return Aim(front);
            bool arrowOnBeam = bm.TestArrowShown;
            Check(arrowOnWeapon, "a fixed weapon shows which way it will point");
            Check(!arrowOnBeam, "...and a structural part does not (nothing to aim)");
        }

        // ================= E. THE CONFIRM ROW ==============================
        float t0 = Time.realtimeSinceStartup;
        while (!MobileBuilderUI.Active && Time.realtimeSinceStartup - t0 < 8f) yield return null;
        var ui = MobileBuilderUI.inst;
        Check(ui != null, "the touch UI is up to test the confirm row against");
        if (ui != null)
        {
            ui.TestForceTouchPointer(true);
            if (!ui.DockOpen) { ui.SetDockOpen(true); yield return null; }
            ui.ShowTab(0);
            yield return null;
            if (beam >= 0) Hold(beam);
            yield return null;

            Vector2 spot = bm.TestCam.WorldToScreenPoint(front);
            bm.TestClearFaceLock();
            yield return AimScreen(spot);
            bool wasValid = bm.TestGhostValid;
            int had = bm.PlacedCount;

            ui.TestArmPreview(spot);
            yield return null; yield return null;
            Check(ui.TestPreviewArmed && ui.TestConfirmShown,
                  "arming a preview shows ATTACH / ROTATE / CANCEL");
            Check(ui.TestAttachBtn != null && ui.TestPreviewRotateBtn != null
                  && ui.TestPreviewCancelBtn != null, "...all three buttons exist");
            // The row is a UI control over the build area - the class of defect
            // that put TEST DRIVE's bottom 55% under a transparent viewport with
            // every check green. Two things have to be true of it.
            var row = ui.TestConfirmRow;
            Check(row != null && row.sizeDelta.y >= ui.TestTouchRow() - 0.5f,
                  "the confirm row is a full touch row tall ("
                  + (row != null ? row.sizeDelta.y : 0f) + " vs " + ui.TestTouchRow() + ")");
            Check(row != null && ui.TestOverUI(RectTransformUtility.WorldToScreenPoint(null, row.position)),
                  "a tap on the row counts as UI, so ATTACH cannot also re-aim the ghost behind it");
            Check(row != null && ui.TestConfirmRowAboveDock(),
                  "...and it sits clear of the dock and the collapse handle");

            if (wasValid)
            {
                Check(ui.TestAttachBtn.interactable, "ATTACH is pressable when the placement is legal");
                ui.TestAttachBtn.onClick.Invoke();
                yield return null; yield return null; yield return null;
                Check(bm.PlacedCount == had + 1, "pressing ATTACH places the part");
                Check(!ui.TestPreviewArmed && !ui.TestConfirmShown, "...and the row goes away");
            }

            // ROTATE keeps the preview and turns the part.
            if (beam >= 0) Hold(beam);
            yield return null;
            bm.TestClearFaceLock();
            yield return AimScreen(spot);
            ui.TestArmPreview(spot);
            yield return null;
            int yaw0 = bm.TestGhostYaw;
            ui.TestPreviewRotateBtn.onClick.Invoke();
            yield return null; yield return null;
            Check(bm.TestGhostYaw != yaw0, "ROTATE turns the held part without leaving the preview");
            Check(ui.TestPreviewArmed, "...and the preview is still armed after it");

            int held2 = bm.PlacedCount;
            ui.TestPreviewCancelBtn.onClick.Invoke();
            yield return null; yield return null;
            Check(!ui.TestPreviewArmed && !ui.TestConfirmShown, "CANCEL puts the preview away");
            Check(bm.PlacedCount == held2, "...without placing anything");

            // A refused placement must not offer a working ATTACH.
            if (wheel >= 0)
            {
                Hold(wheel);
                bm.TestClearFaceLock();
                yield return Aim(top);            // wheels attach to side faces only
                if (!bm.TestGhostValid)
                {
                    Vector2 tp = bm.TestCam.WorldToScreenPoint(top);
                    ui.TestArmPreview(tp);
                    yield return null; yield return null;
                    Check(!ui.TestAttachBtn.interactable,
                          "ATTACH is not pressable on a refused placement");
                    Check(ui.TestPreviewWhy.Length > 0,
                          "...and the row says why: \"" + ui.TestPreviewWhy + "\"");
                    ui.TestPreviewCancelBtn.onClick.Invoke();
                    yield return null;
                }
            }
        }

        Phase0Input.debugPointer = false;
        hold.Dispose();
        foreach (var l in log) Debug.Log("[PlaceUxBench] " + l);
        Debug.Log("[PlaceUxBench] RESULT: " + passed + " pass, " + failed + " fail - "
                  + (failed == 0 && passed > 0 ? "ALL GREEN" : "FIX NEEDED"));
        finished = true;
    }
}
}
#endif
