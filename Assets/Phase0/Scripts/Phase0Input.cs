using UnityEngine;
#if !ENABLE_LEGACY_INPUT_MANAGER && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

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

    public static bool ResetDown() { return Input.GetKeyDown(KeyCode.R); }
    public static bool ToggleDown() { return Input.GetKeyDown(KeyCode.T); }
    public static bool FlipDown() { return Input.GetKeyDown(KeyCode.F); }
    public static bool RotateDown() { return Input.GetKeyDown(KeyCode.R); }
    public static bool TestDown() { return Input.GetKeyDown(KeyCode.T); }
    public static bool BackDown() { return Input.GetKeyDown(KeyCode.B); }

    public static float OrbitAxis()
    {
        float o = 0f;
        if (Input.GetKey(KeyCode.E)) o += 1f;
        if (Input.GetKey(KeyCode.Q)) o -= 1f;
        return o;
    }

    public static Vector3 MousePos() { return Input.mousePosition; }
    public static bool MouseDown(int button) { return Input.GetMouseButtonDown(button); }
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

    public static bool ResetDown() { var kb = Keyboard.current; return kb != null && kb.rKey.wasPressedThisFrame; }
    public static bool ToggleDown() { var kb = Keyboard.current; return kb != null && kb.tKey.wasPressedThisFrame; }
    public static bool FlipDown() { var kb = Keyboard.current; return kb != null && kb.fKey.wasPressedThisFrame; }
    public static bool RotateDown() { var kb = Keyboard.current; return kb != null && kb.rKey.wasPressedThisFrame; }
    public static bool TestDown() { var kb = Keyboard.current; return kb != null && kb.tKey.wasPressedThisFrame; }
    public static bool BackDown() { var kb = Keyboard.current; return kb != null && kb.bKey.wasPressedThisFrame; }

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
        var m = Mouse.current;
        if (m == null) return Vector3.zero;
        Vector2 p = m.position.ReadValue();
        return new Vector3(p.x, p.y, 0f);
    }

    public static bool MouseDown(int button)
    {
        var m = Mouse.current;
        if (m == null) return false;
        if (button == 0) return m.leftButton.wasPressedThisFrame;
        if (button == 1) return m.rightButton.wasPressedThisFrame;
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
    public static bool ResetDown() { return false; }
    public static bool ToggleDown() { return false; }
    public static bool FlipDown() { return false; }
    public static bool RotateDown() { return false; }
    public static bool TestDown() { return false; }
    public static bool BackDown() { return false; }
    public static float OrbitAxis() { return 0f; }
    public static Vector3 MousePos() { return Vector3.zero; }
    public static bool MouseDown(int button) { return false; }
    public static float Scroll() { return 0f; }
#endif
}
