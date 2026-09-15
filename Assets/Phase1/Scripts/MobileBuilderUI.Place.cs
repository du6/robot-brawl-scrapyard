using UnityEngine;
using UnityEngine.UI;

namespace RobotBrawl.Phase0
{
/// <summary>
/// TAP TO PREVIEW, THEN ATTACH — the touch half of part placement.
///
/// The defect this answers, from the 2026-09-14 play-test: on a phone a tap
/// COMMITTED a part, and the ghost it committed was drawn under the finger
/// doing the aiming. So the preview existed and was unreadable, the decision
/// was made before it could be read, and the only correction was UNDO. Nor
/// could the player nudge: past SwipeVsTapPx a drag becomes an ORBIT (owen,
/// 2026-08-14, and that is right — you have to be able to turn the robot to
/// reach its far side), which leaves no gesture that means "not there, HERE".
///
/// The flow now: tap to aim → lift → the ghost stays exactly where it was, with
/// nothing on top of it → ATTACH, ROTATE or CANCEL. Tapping the robot again
/// re-aims, so "move it a bit" is the same gesture as "aim", not a new one to
/// learn.
///
/// ⚠ THIS IS TOUCH-ONLY, DELIBERATELY. A mouse aims to the pixel and never
/// covers its own cursor, so the confirm step there is a second click bought
/// for nothing. `PreviewEligible` is the gate, and it reads the same
/// coarse-pointer signal the type sizing already uses rather than inventing a
/// second answer to "is this a phone".
/// </summary>
public partial class MobileBuilderUI
{
    /// <summary>Gap between the confirm row and the collapse handle below it.</summary>
    const float CONFIRM_GAP = 8f;

    void BuildConfirmRow()
    {
        confirmRow = MkPanel("confirm", canvas.transform, new Color(0f, 0f, 0f, 0f))
                     .GetComponent<RectTransform>();
        confirmRow.anchorMin = new Vector2(0.5f, 0f);
        confirmRow.anchorMax = new Vector2(0.5f, 0f);
        confirmRow.pivot = new Vector2(0.5f, 0f);

        // The reason line sits ABOVE the buttons, not inside one, so a long
        // refusal ("Rotor would sweep through the long beam — clear its circle")
        // can wrap without changing the size of the thing you have to hit.
        previewWhy = MkText("why", confirmRow.transform, "", 14, TextAnchor.MiddleCenter);
        var wrt = previewWhy.rectTransform;
        wrt.anchorMin = new Vector2(0f, 1f); wrt.anchorMax = new Vector2(1f, 1f);
        wrt.pivot = new Vector2(0.5f, 0f);
        wrt.sizeDelta = new Vector2(0f, 22f);
        wrt.anchoredPosition = new Vector2(0f, 2f);

        var bar = MkPanel("cbar", confirmRow.transform, new Color(0f, 0f, 0f, 0f))
                  .GetComponent<RectTransform>();
        bar.anchorMin = Vector2.zero; bar.anchorMax = Vector2.one;
        bar.offsetMin = Vector2.zero; bar.offsetMax = Vector2.zero;
        var lay = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
        lay.spacing = 6f; lay.childForceExpandWidth = true; lay.childForceExpandHeight = true;
        lay.childControlWidth = true; lay.childControlHeight = true;

        // ATTACH is first and stays the widest — it is the verb, and the whole
        // point of this row is that the player has already decided by the time
        // they reach it. CANCEL is last and quietest: the recoverable choice
        // does not need to compete with the one being confirmed.
        attachBtn = MkButton("cattach", bar.transform, "ATTACH", 17, () => { previewAttachQueued = true; });
        attachBtn.GetComponent<Image>().color = new Color(0.16f, 0.50f, 0.30f, 0.98f);
        var alay = attachBtn.gameObject.AddComponent<LayoutElement>();
        alay.flexibleWidth = 1.6f;

        // NEXT MOUNT (play-test 2026-09-14: "offer Next mount when several
        // sockets are close together"). Sockets sit 15 cm apart and the machine
        // can be small on screen, so the nearest one to a tap is not always the
        // one meant. Rather than inflate hit areas - the tap does not hit a
        // marker, it raycasts and then snaps, so there is no hit area to
        // inflate - this steps the pin to the next candidate on the same face.
        previewNextBtn = MkButton("cnext", bar.transform, "NEXT MOUNT", 15, () =>
        {
            if (previewArmed && previewVerb == Verb.Place && bm != null) bm.PinNextSocket();
        });

        previewRotBtn = MkButton("crot", bar.transform, "ROTATE", 17, () =>
        {
            // Rotate re-computes the ghost at the SAME frozen point, so the
            // player sees the new orientation in place instead of having to
            // re-aim to find out what R did. The preview stays armed.
            if (previewArmed) { Phase0Input.debugMousePos = new Vector3(previewAt.x, previewAt.y, 0f); }
            Phase0Input.DebugRotate();
        });

        previewCancelBtn = MkButton("ccancel", bar.transform, "CANCEL", 17, CancelPreview);
        previewCancelBtn.GetComponent<Image>().color = new Color(0.20f, 0.14f, 0.16f, 0.96f);

        confirmRow.gameObject.SetActive(false);
    }

    /// <summary>Arm a preview for whatever verb the held part has.
    ///
    /// Returns false when there is nothing under the tap to preview - a tap that
    /// missed the machine arms nothing, rather than popping a row whose only
    /// working button is CANCEL.
    ///
    /// ⚠ TWO VERBS, NOT ONE. A weld kit is not a placement: it produces no ghost
    /// at all, so the first version of this armed a placement preview that could
    /// never resolve and left the weld kit unusable on a phone. The verb is
    /// decided here, once, and everything downstream reads it.</summary>
    bool ArmPreview(Vector2 where)
    {
        previewAt = where;
        Phase0Input.debugPointer = true;
        Phase0Input.debugMousePos = new Vector3(where.x, where.y, 0f);
        if (bm == null || !bm.HasSelection) return false;

        if (bm.SelectedIsApplique)
        {
            BuilderManager.PlacedPart wp; int wb;
            if (!bm.WeldAim(out wp, out wb)) return false;
            weldPart = wp; weldBit = wb;
            previewVerb = Verb.Weld;
            previewArmed = true;
            return true;
        }

        // A PLACEMENT PIN, NOT A PIXEL. See BuilderManager.PinGhost - the mount
        // and an aim point in the part's own frame, so the camera is free.
        if (!bm.PinGhost()) return false;
        weldPart = null;
        previewVerb = Verb.Place;
        previewArmed = true;
        return true;
    }

    void CancelPreview()
    {
        if (!previewArmed) return;
        previewArmed = false;
        previewAttachQueued = false;
        weldPart = null;
        if (bm != null) { bm.UnpinGhost(); bm.HideWeldTarget(); }
        if (confirmRow != null) confirmRow.gameObject.SetActive(false);
    }

    enum Verb { Place, Weld }
    Verb previewVerb = Verb.Place;
    BuilderManager.PlacedPart weldPart;
    int weldBit = -1;

    /// <summary>Frames left re-asserting the click's preconditions. See
    /// PumpPreview.</summary>
    int previewCommitHold;

    bool ConfirmRowHit(Vector2 point)
    {
        return confirmRow != null && confirmRow.gameObject.activeInHierarchy
            && RectTransformUtility.RectangleContainsScreenPoint(confirmRow, point, null);
    }

    /// <summary>Place the row above the collapse handle and size it to one touch
    /// row. Called from ApplyDockH, which is the one place that knows how tall
    /// the dock and the handle currently are — a literal here is the R1 fix 2
    /// defect (a stale 210 let taps fall through into the builder), one control
    /// further out.
    ///
    /// ⚠ IT IS NOT ADDED TO PublishCover, AND THAT IS THE TRADE. Reserving space
    /// for it would re-frame the build camera at the exact moment the player is
    /// trying to inspect a placement, so the robot would jump every time they
    /// lifted a finger. Instead it overlaps the lowest strip of the build area —
    /// which is empty on every framing this camera produces, because
    /// GarageFitDistance centres the machine in the band the dock leaves.</summary>
    void LayoutConfirmRow()
    {
        if (confirmRow == null || dockRt == null) return;
        bool show = previewArmed && bm != null && bm.HasSelection
                 && bm.mode == BuilderManager.Mode.Build && !bm.Scouting && tab == 0;
        if (confirmRow.gameObject.activeSelf != show) confirmRow.gameObject.SetActive(show);
        if (!show) return;

        // A weld has no orientation and no second socket, so it shows neither
        // ROTATE nor NEXT MOUNT. A button that does nothing is worse than a
        // missing one - it invites a tap and answers with silence.
        bool place = previewVerb == Verb.Place;
        if (attachBtn != null)
        {
            var at = attachBtn.GetComponentInChildren<Text>();
            if (at != null) at.text = place ? "ATTACH" : "APPLY WELD";
        }
        if (previewRotBtn != null && previewRotBtn.gameObject.activeSelf != place)
            previewRotBtn.gameObject.SetActive(place);
        if (previewNextBtn != null)
        {
            bool showNext = place && bm != null && bm.PinSocketCount > 1;
            if (previewNextBtn.gameObject.activeSelf != showNext)
                previewNextBtn.gameObject.SetActive(showNext);
        }

        float row = TouchRow();
        float px = !PhysicalTouchSizing
                 ? browserPixelRatio * userUiScale / Mathf.Max(0.01f, canvas.scaleFactor)
                 : row / 44f;
        float safeWidth = Mathf.Max(120f, CanvasW - safeL - safeR);
        confirmRow.sizeDelta = new Vector2(Mathf.Min(safeWidth - 16f, 430f * px), row);
        confirmRow.anchoredPosition = new Vector2(
            (safeL - safeR) * 0.5f,
            dockRt.sizeDelta.y + (handleRt != null ? handleRt.sizeDelta.y : 0f) + CONFIRM_GAP);
        if (previewWhy != null)
        {
            previewWhy.fontSize = FontUnits(14f);
            previewWhy.rectTransform.sizeDelta = new Vector2(0f, FontUnits(14f) * 1.6f);
        }
        SetFont(confirmRow, 15f);
    }

    /// <summary>Per-frame: keep the frozen ghost alive, show why it is refused,
    /// and consume an ATTACH press.
    ///
    /// ATTACH cannot fire Phase0Input.DebugClick from its own onClick. The press
    /// happens while the finger is over the row, so `uiPointerBlocked` is true
    /// that frame and UpdateBuild's very first gate (`overPanel`) throws the
    /// click away — the placement would silently do nothing, which is the
    /// WATCH-button class of defect in reverse (behaviour in an onClick that the
    /// surrounding state cancels). So the button only raises a flag and the
    /// click is issued from here, after Pointers() has released the block.</summary>
    void PumpPreview()
    {
        if (bm == null) return;
        if (previewArmed && (!bm.HasSelection || tab != 0
                             || bm.mode != BuilderManager.Mode.Build || bm.Scouting))
            CancelPreview();

        if (previewArmed)
        {
            // ⚠ NOTHING HERE TOUCHES debugMousePos ANY MORE. The first version
            // re-asserted a screen pixel every frame, which is only correct
            // while the camera holds still: orbit or pinch and the world slides
            // under the pixel, so a "locked" preview quietly became a placement
            // somewhere else. The mount is pinned in the builder now, in the
            // target part's own frame, so the camera is free.
            bool ok;
            string why;
            if (previewVerb == Verb.Weld)
            {
                why = bm.WeldRefusal(weldPart, weldBit);
                ok = why == null;
                bm.ShowWeldTarget(weldPart);
            }
            else
            {
                ok = bm.TestGhostValid;
                why = bm.TestGhostReason;
            }
            if (previewWhy != null)
            {
                previewWhy.text = ok ? "" : (why ?? "");
                previewWhy.color = new Color(1f, 0.72f, 0.35f);
            }
            if (attachBtn != null)
            {
                attachBtn.interactable = ok;
                attachBtn.GetComponent<Image>().color = ok
                    ? new Color(0.16f, 0.50f, 0.30f, 0.98f)
                    : new Color(0.22f, 0.24f, 0.28f, 0.96f);
            }
        }

        if (previewCommitHold > 0)
        {
            previewCommitHold--;
            BuilderManager.uiPointerBlocked = false;
            Phase0Input.debugPointer = true;
        }

        if (previewAttachQueued)
        {
            previewAttachQueued = false;
            if (previewVerb == Verb.Weld)
            {
                // The weld is a direct call, not a synthesised click: it has its
                // own verb and its own validation, so routing it back through
                // the pointer would only re-introduce the gates that broke it.
                var wp = weldPart; int wb = weldBit;
                CancelPreview();
                if (wp != null) bm.WeldApply(wp, wb);
            }
            else
            {
                // ⚠ SCRIPT EXECUTION ORDER BETWEEN THIS Update AND
                // BuilderManager's IS UNDEFINED, so a click raised here may not
                // be consumed until the NEXT frame. The pin holds the placement
                // across that gap on its own - which is the other thing the pin
                // bought: the commit no longer depends on a pixel surviving.
                BuilderManager.uiPointerBlocked = false;
                Phase0Input.debugPointer = true;
                Phase0Input.DebugClick(0);
                previewCommitHold = 3;
                clickHold = 3;
                previewArmed = false;
                if (confirmRow != null) confirmRow.gameObject.SetActive(false);
                // The pin is released AFTER the click is consumed, not now -
                // see PumpPreviewLate.
                previewUnpinIn = 3;
            }
        }
        else if (previewUnpinIn > 0 && --previewUnpinIn == 0)
        {
            bm.UnpinGhost();
        }
    }

    int previewUnpinIn;

    // ---- TEST SEAM ---------------------------------------------------------
    // The confirm row is UI BEHAVIOUR, and this project's standing rule is that
    // behaviour living in an onClick is invisible to a harness that drives the
    // method underneath it (the WATCH precedent, CLAUDE.md). These expose the
    // STATE; a bench must still press the real buttons through onClick.Invoke.
    public bool TestPreviewArmed { get { return previewArmed; } }
    public bool TestConfirmShown { get { return confirmRow != null && confirmRow.gameObject.activeSelf; } }
    public Button TestAttachBtn { get { return attachBtn; } }
    public Button TestPreviewRotateBtn { get { return previewRotBtn; } }
    public Button TestPreviewCancelBtn { get { return previewCancelBtn; } }
    public string TestPreviewWhy { get { return previewWhy != null ? previewWhy.text : ""; } }
    public RectTransform TestConfirmRow { get { return confirmRow; } }
    /// <summary>Force the touch path on for a bench. Headless has no
    /// Touchscreen and no browser, so PreviewEligible is false and the confirm
    /// step would never arm — a bench measuring the branch the phone never
    /// takes is the exact false green this project has now hit three times
    /// (docs/Mobile_Round_2026-09-13.md).</summary>
    public void TestForceTouchPointer(bool v) { pointerIsTouch = v; }
    public void TestArmPreview(Vector2 at) { ArmPreview(at); }
    /// <summary>Arm and report whether it took - a tap that finds nothing to
    /// preview must not arm, and a bench has to be able to see the difference.</summary>
    public bool TestArmPreviewAt(Vector2 at) { return ArmPreview(at); }
    public string TestPreviewVerb() { return previewVerb.ToString(); }
    public string TestAttachLabel()
    {
        if (attachBtn == null) return "";
        var t = attachBtn.GetComponentInChildren<Text>();
        return t != null ? t.text : "";
    }
    public Button TestPreviewNextBtn { get { return previewNextBtn; } }
    public float TestTouchRow() { return TouchRow(); }
    /// <summary>Does the confirm row clear the dock and the collapse handle?
    /// Asked of the LIVE rects rather than of the constants that produced them -
    /// this dock is per-tab and collapsible, and a geometry check computed from
    /// literals is the R1 fix 2 defect one layer up. Nothing in this project
    /// asks whether one control is drawn on top of another (CLAUDE.md, the TEST
    /// DRIVE viewport), so this answers it for the one control this round adds.</summary>
    public bool TestConfirmRowAboveDock()
    {
        if (confirmRow == null || dockRt == null) return false;
        float floor = dockRt.sizeDelta.y + (handleRt != null ? handleRt.sizeDelta.y : 0f);
        return confirmRow.anchoredPosition.y >= floor - 0.5f;
    }
}
}
