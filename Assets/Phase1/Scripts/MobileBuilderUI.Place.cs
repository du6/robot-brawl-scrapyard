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

    /// <summary>Arm the preview at `where`. Called on the release edge of a tap
    /// that would previously have placed the part.</summary>
    void ArmPreview(Vector2 where)
    {
        previewArmed = true;
        previewAt = where;
        Phase0Input.debugPointer = true;
        Phase0Input.debugMousePos = new Vector3(where.x, where.y, 0f);
    }

    void CancelPreview()
    {
        if (!previewArmed) return;
        previewArmed = false;
        previewAttachQueued = false;
        if (confirmRow != null) confirmRow.gameObject.SetActive(false);
    }

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
        // Losing the part, the tab or the mode ends the preview — an armed
        // confirm row for a placement that can no longer happen is a button
        // that lies.
        if (previewArmed && (!bm.HasSelection || tab != 0
                             || bm.mode != BuilderManager.Mode.Build || bm.Scouting))
            CancelPreview();

        if (previewArmed)
        {
            // Hold the pointer where the tap left it, every frame: UpdateGhost
            // reads Phase0Input.MousePos() and nothing else, so this IS the
            // preview. Re-asserted rather than set once because the hover branch
            // and the touch branch both write the same field.
            Phase0Input.debugPointer = true;
            Phase0Input.debugMousePos = new Vector3(previewAt.x, previewAt.y, 0f);

            if (previewWhy != null)
            {
                // The SAME seam the benches read. A refusal the player is shown
                // and a refusal a bench asserts on cannot drift apart if there
                // is only one of them.
                bool ok = bm.TestGhostValid;
                string why = bm.TestGhostReason;
                previewWhy.text = ok ? "" : why;
                previewWhy.color = new Color(1f, 0.72f, 0.35f);
            }
            if (attachBtn != null)
            {
                bool ok = bm.TestGhostValid;
                attachBtn.interactable = ok;
                attachBtn.GetComponent<Image>().color = ok
                    ? new Color(0.16f, 0.50f, 0.30f, 0.98f)
                    : new Color(0.22f, 0.24f, 0.28f, 0.96f);
            }
        }

        // ⚠ SCRIPT EXECUTION ORDER BETWEEN THIS Update AND BuilderManager's IS
        // UNDEFINED, so a click raised here may not be consumed until the NEXT
        // frame — and by then the finger is up, the preview is disarmed, and
        // every piece of state the click depends on would have been rewritten by
        // whoever got there first. So the three facts the click needs (block
        // off, pointer on, position frozen) are re-asserted for a few frames
        // rather than set once. This is the same defence `clickHold` already
        // gives the REMOVE button, which is where the pattern comes from.
        if (previewCommitHold > 0)
        {
            previewCommitHold--;
            BuilderManager.uiPointerBlocked = false;
            Phase0Input.debugPointer = true;
            Phase0Input.debugMousePos = new Vector3(previewAt.x, previewAt.y, 0f);
        }

        if (previewAttachQueued)
        {
            previewAttachQueued = false;
            BuilderManager.uiPointerBlocked = false;
            Phase0Input.debugPointer = true;
            Phase0Input.debugMousePos = new Vector3(previewAt.x, previewAt.y, 0f);
            Phase0Input.DebugClick(0);
            previewCommitHold = 3;
            clickHold = 3;
            previewArmed = false;
            if (confirmRow != null) confirmRow.gameObject.SetActive(false);
        }
    }

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
