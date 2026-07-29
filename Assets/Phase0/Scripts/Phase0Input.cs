using UnityEngine;
#if !ENABLE_LEGACY_INPUT_MANAGER && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RobotBrawl.Phase0
{
/// <summary>
/// Input shim so the prototypes run under either input backend without touching
/// Project Settings: the legacy Input Manager when available, otherwise the
/// new Input System package (Keyboard.current / Mouse.current).
/// </summary>
public static class Phase0Input
{
    /// <summary>Scripted-test override: nonzero replaces keyboard throttle.</summary>
    public static float debugThrottle = 0f;
    /// <summary>Scripted-test override: nonzero replaces keyboard steer.</summary>
    public static float debugSteer = 0f;
    /// <summary>Scripted-test override for the Phase 4 weapon trigger. The
    /// headless backend has no keyboard at all, so harness runs drive it from
    /// here exactly as they drive throttle and steer.</summary>
    public static bool debugFire = false;

    // ---- SCRIPTED POINTER / KEY SEAM -------------------------------------
    // Why this exists: every automated test of this project until now drove the
    // builder through LoadSnapshot -> AddPart, which SKIPS UpdateBuild's entire
    // ghost pipeline - face pick, socket rule, snap, overlap test, rotate, click.
    // A bug that made the blade unplaceable BY MOUSE therefore survived five QA
    // rounds and ~300 matches, because nothing automated had ever moved a mouse.
    // These overrides let a harness drive the real player path, in exactly the
    // idiom debugThrottle / debugSteer / debugFire already established.
    //
    // The *Down flags are ONE-SHOT: the accessor consumes them, which is exactly
    // the edge semantics GetKeyDown / wasPressedThisFrame have, so a test cannot
    // accidentally hold a button down for many frames.
    /// <summary>Master switch. Off = the real devices, untouched.</summary>
    public static bool debugPointer = false;
    /// <summary>Screen-space pointer used while debugPointer is on.</summary>
    public static Vector3 debugMousePos = Vector3.zero;
    static bool dClick, dClickR, dClickM, dRotate, dEsc, dUndo;
    public static void DebugClick()  { dClick = true; }
    /// <summary>ROUND-UP1: the seam now covers ALL THREE mouse buttons.
    /// It used to drive button 0 only, so RemovePart (right-click) and
    /// SetPartMaterial (middle-click) - the builder's two DESTRUCTIVE verbs -
    /// could not be exercised by any automated test. That is precisely the
    /// blind spot that hid the unplaceable blade for five QA rounds, and it
    /// was hiding a live one: see MouseDown below.</summary>
    public static void DebugClick(int button)
    {
        if (button == 1) dClickR = true;
        else if (button == 2) dClickM = true;
        else dClick = true;
    }
    public static void DebugRotate() { dRotate = true; }
    /// <summary>Scripted seam for the builder's UNDO key, added with cascade
    /// removal (2026-07-29). Right-click now deletes a whole branch, so undo
    /// stopped being a convenience and became the safety net that makes the
    /// cascade safe to try - and a safety net no test can press is not one.</summary>
    public static void DebugUndo()   { dUndo = true; }
    public static void DebugEsc()    { dEsc = true; }
    static bool TakeClick(int button)
    {
        if (!debugPointer) return false;
        if (button == 1) { if (!dClickR) return false; dClickR = false; return true; }
        if (button == 2) { if (!dClickM) return false; dClickM = false; return true; }
        if (!dClick) return false; dClick = false; return true;
    }
    static bool TakeClick()  { return TakeClick(0); }
    static bool TakeRotate() { if (!debugPointer || !dRotate) return false; dRotate = false; return true; }
    static bool TakeUndo()   { if (!debugPointer || !dUndo)   return false; dUndo   = false; return true; }
    static bool TakeEsc()    { if (!debugPointer || !dEsc) return false;    dEsc = false;    return true; }

#if ENABLE_LEGACY_INPUT_MANAGER
    public static float Throttle()
    {
        if (debugThrottle != 0f) return debugThrottle;
        float t = 0f;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) t += 1f;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) t -= 1f;
        return t;
    }

    public static float Steer()
    {
        if (debugSteer != 0f) return debugSteer;
        float s = 0f;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) s += 1f;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) s -= 1f;
        return s;
    }

    /// <summary>PHASE 4: the weapon trigger. Before this the player's entire
    /// vocabulary was throttle and steer, which is exactly why the fighting read
    /// as a bumping test - there was no verb for using a weapon. Held rather
    /// than tapped, so a Spindle spins while you hold it and a Pivot fires once
    /// per press-and-recover.</summary>
    public static bool FireHeld() { return debugFire || Input.GetKey(KeyCode.Space); }
    public static bool ResetDown() { return Input.GetKeyDown(KeyCode.R); }
    public static bool ToggleDown() { return Input.GetKeyDown(KeyCode.T); }
    public static bool FlipDown() { return Input.GetKeyDown(KeyCode.F); }
    public static bool RotateDown() { return TakeRotate() || Input.GetKeyDown(KeyCode.R); }
    public static bool UndoDown() { return TakeUndo() || Input.GetKeyDown(KeyCode.Z); }
    public static bool TestDown() { return Input.GetKeyDown(KeyCode.T); }
    public static bool BackDown() { return Input.GetKeyDown(KeyCode.B); }
    public static bool EscDown() { return TakeEsc() || Input.GetKeyDown(KeyCode.Escape); }

    public static float OrbitAxis()
    {
        float o = 0f;
        if (Input.GetKey(KeyCode.E)) o += 1f;
        if (Input.GetKey(KeyCode.Q)) o -= 1f;
        return o;
    }

    public static Vector3 MousePos() { return debugPointer ? debugMousePos : Input.mousePosition; }
    public static bool MouseDown(int button)
    { return TakeClick(button) || Input.GetMouseButtonDown(button); }
    public static float Scroll() { return Input.mouseScrollDelta.y; }
#elif ENABLE_INPUT_SYSTEM
    public static float Throttle()
    {
        if (debugThrottle != 0f) return debugThrottle;
        var kb = Keyboard.current;
        if (kb == null) return 0f;
        float t = 0f;
        if (kb.wKey.isPressed || kb.upArrowKey.isPressed) t += 1f;
        if (kb.sKey.isPressed || kb.downArrowKey.isPressed) t -= 1f;
        return t;
    }

    public static float Steer()
    {
        if (debugSteer != 0f) return debugSteer;
        var kb = Keyboard.current;
        if (kb == null) return 0f;
        float s = 0f;
        if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) s += 1f;
        if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) s -= 1f;
        return s;
    }

    public static bool FireHeld() { var kb = Keyboard.current; return debugFire || (kb != null && kb.spaceKey.isPressed); }
    public static bool ResetDown() { var kb = Keyboard.current; return kb != null && kb.rKey.wasPressedThisFrame; }
    public static bool ToggleDown() { var kb = Keyboard.current; return kb != null && kb.tKey.wasPressedThisFrame; }
    public static bool FlipDown() { var kb = Keyboard.current; return kb != null && kb.fKey.wasPressedThisFrame; }
    public static bool RotateDown() { if (TakeRotate()) return true; var kb = Keyboard.current; return kb != null && kb.rKey.wasPressedThisFrame; }
    public static bool UndoDown() { if (TakeUndo()) return true; var kb = Keyboard.current; return kb != null && kb.zKey.wasPressedThisFrame; }
    public static bool TestDown() { var kb = Keyboard.current; return kb != null && kb.tKey.wasPressedThisFrame; }
    public static bool BackDown() { var kb = Keyboard.current; return kb != null && kb.bKey.wasPressedThisFrame; }
    public static bool EscDown() { if (TakeEsc()) return true; var kb = Keyboard.current; return kb != null && kb.escapeKey.wasPressedThisFrame; }

    public static float OrbitAxis()
    {
        var kb = Keyboard.current;
        if (kb == null) return 0f;
        float o = 0f;
        if (kb.eKey.isPressed) o += 1f;
        if (kb.qKey.isPressed) o -= 1f;
        return o;
    }

    public static Vector3 MousePos()
    {
        if (debugPointer) return debugMousePos;
        var m = Mouse.current;
        if (m == null) return Vector3.zero;
        Vector2 p = m.position.ReadValue();
        return new Vector3(p.x, p.y, 0f);
    }

    /// <summary>ROUND-UP1 FIX C - a REAL bug, not just a missing test seam.
    /// This branch handled buttons 0 and 1 and then `return false`, so
    /// MouseDown(2) could NEVER be true. BuilderManager.UpdateBuild drives the
    /// middle button and nothing else:
    ///     if (... Phase0Input.MouseDown(2)) SetPartMaterial(hitPart, activeMat);
    /// MEASURED: ProjectSettings.asset says `activeInputHandler: 1`, i.e.
    /// Input System Package (New) ONLY - so this branch, not the legacy one, is
    /// the branch that ships. The middle-click REPAINT tool has therefore been
    /// dead for the player the entire time. It was invisible because the
    /// legacy branch above (compiled out) forwards every button to
    /// Input.GetMouseButtonDown and looks correct on a code read, and because
    /// no test could press a middle button.</summary>
    public static bool MouseDown(int button)
    {
        if (TakeClick(button)) return true;
        var m = Mouse.current;
        if (m == null) return false;
        if (button == 0) return m.leftButton.wasPressedThisFrame;
        if (button == 1) return m.rightButton.wasPressedThisFrame;
        if (button == 2) return m.middleButton.wasPressedThisFrame;
        return false;
    }

    public static float Scroll()
    {
        var m = Mouse.current;
        if (m == null) return 0f;
        // Platforms disagree wildly on scroll units: ±120 per notch on some,
        // ±1 on others. The old flat ×0.01 turned ±1-unit notches into a
        // 0.005 m zoom step — i.e. "scroll does nothing" (round-3 fix).
        // Scale down only big-unit deltas, then clamp to sane notch counts.
        float y = m.scroll.ReadValue().y;
        if (Mathf.Abs(y) > 10f) y *= 0.01f;
        return Mathf.Clamp(y, -3f, 3f);
    }
#else
    // No input backend enabled — everything inert, nothing throws.
    public static float Throttle() { return debugThrottle; }
    public static float Steer() { return debugSteer; }
    public static bool FireHeld() { return debugFire; }
    public static bool ResetDown() { return false; }
    public static bool ToggleDown() { return false; }
    public static bool FlipDown() { return false; }
    public static bool RotateDown() { return TakeRotate(); }
    public static bool UndoDown() { return TakeUndo(); }
    public static bool TestDown() { return false; }
    public static bool BackDown() { return false; }
    public static bool EscDown() { return TakeEsc(); }
    public static float OrbitAxis() { return 0f; }
    public static Vector3 MousePos() { return debugPointer ? debugMousePos : Vector3.zero; }
    public static bool MouseDown(int button) { return TakeClick(button); }
    public static float Scroll() { return 0f; }
#endif
}

}