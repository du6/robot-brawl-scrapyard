using UnityEngine;

namespace RobotBrawl.Phase0
{
/// <summary>Where the screen's cut-outs are, on the web.
///
/// Unity's `Screen.safeArea` is the WHOLE SCREEN in a WebGL build - it has no
/// way to know about a notch - while this game's page deliberately opts the
/// canvas into the cut-out region with `viewport-fit=cover`
/// (`Assets/WebGLTemplates/RobotBrawl/index.html:5`). The result, until this
/// existed, was that every safe-area mechanism in the codebase computed zero:
/// the dock's inset reader, the map HUD's inset, the fight's QUIT
/// compensation and the dock's bottom clamp. On a landscape iPhone that put
/// the leftmost dock tab and the only exit from a running fight under a
/// roughly 47 CSS px notch, and the build action row inside the home
/// indicator.
///
/// CSS `env(safe-area-inset-*)` is the only source of the real numbers, and it
/// is readable only from CSS, so the plugin measures a hidden probe element.
/// Values come back in FRAMEBUFFER pixels, the same units as `Screen.width`,
/// so a caller can treat this exactly like `Screen.safeArea` elsewhere.
///
/// Off the web this returns Unity's own safe area unchanged, so iOS keeps the
/// behaviour it ships with.</summary>
public static class SafeAreaWeb
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("__Internal")] static extern float ScrapyardUiSafeLeft();
    [System.Runtime.InteropServices.DllImport("__Internal")] static extern float ScrapyardUiSafeRight();
    [System.Runtime.InteropServices.DllImport("__Internal")] static extern float ScrapyardUiSafeTop();
    [System.Runtime.InteropServices.DllImport("__Internal")] static extern float ScrapyardUiSafeBottom();
#endif

    /// <summary>Test seam: a bench can pose as a notched phone. Negative means
    /// "not forced", which is the shipped state.</summary>
    public static float forcedLeft = -1f, forcedRight = -1f, forcedTop = -1f, forcedBottom = -1f;

    /// <summary>Pose "the browser never answered", which is otherwise
    /// UNREACHABLE from any bench this project can run: off the web `Read`
    /// takes its native leg and sets Measured true, and forcing an inset sets
    /// it true as well — so the whole fallback branch of every consumer was
    /// code no measurement had ever entered. Found while wiring the fight's
    /// notch fallback, which needed the state to be testable at all.</summary>
    public static bool? forcedMeasured;

    /// <summary>Did the browser actually ANSWER?
    ///
    /// This exists because a caller could not previously tell three different
    /// situations apart - a missing probe element, a browser without env(), and
    /// a phone with NO NOTCH all read as zero. A fallback guess keyed on that
    /// zero therefore had to be wrong in one direction or the other: either it
    /// fired on a notch-less phone and shoved a control 47 px along an edge it
    /// owns outright, or it stayed quiet when the probe was broken and left the
    /// only exit from a fight under a notch.
    ///
    /// With this, neither is necessary. A zero from a working probe means
    /// exactly what it says - this phone has no cut-out - and a guess is only
    /// reasonable when the probe did not answer at all.</summary>
    /// <remarks>This reports the LAST read, and the four edges are always read
    /// together (ReadSafeArea takes all four in one pass), so in practice it is
    /// all-four-answered or none. A mixed result would mean the probe broke
    /// between two reads of the same frame, which cannot happen - but if this
    /// is ever read for a single edge in isolation, that is the assumption to
    /// re-check rather than trust.</remarks>
    public static bool Measured { get; private set; }

    static float Read(float forced, System.Func<float> web, float native)
    {
        if (forcedMeasured.HasValue && !forcedMeasured.Value) { Measured = false; return 0f; }
        if (forced >= 0f) { Measured = forcedMeasured ?? true; return forced; }
#if UNITY_WEBGL && !UNITY_EDITOR
        try { float v = Mathf.Max(0f, web()); Measured = forcedMeasured ?? true; return v; }
        catch (System.Exception) { Measured = false; return 0f; }
#else
        Measured = forcedMeasured ?? true;
        return native;
#endif
    }

    public static float Left
    {
        get
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return Read(forcedLeft, ScrapyardUiSafeLeft, Screen.safeArea.x);
#else
            return Read(forcedLeft, null, Screen.safeArea.x);
#endif
        }
    }
    public static float Right
    {
        get
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return Read(forcedRight, ScrapyardUiSafeRight, Screen.width - Screen.safeArea.xMax);
#else
            return Read(forcedRight, null, Screen.width - Screen.safeArea.xMax);
#endif
        }
    }
    public static float Top
    {
        get
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return Read(forcedTop, ScrapyardUiSafeTop, Screen.height - Screen.safeArea.yMax);
#else
            return Read(forcedTop, null, Screen.height - Screen.safeArea.yMax);
#endif
        }
    }
    public static float Bottom
    {
        get
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return Read(forcedBottom, ScrapyardUiSafeBottom, Screen.safeArea.y);
#else
            return Read(forcedBottom, null, Screen.safeArea.y);
#endif
        }
    }

    /// <summary>The same thing as a Rect, for callers that already speak
    /// `Screen.safeArea`.</summary>
    public static Rect Area
    {
        get
        {
            float l = Left, r = Right, t = Top, b = Bottom;
            return new Rect(l, b, Mathf.Max(1f, Screen.width - l - r),
                                  Mathf.Max(1f, Screen.height - t - b));
        }
    }
}
}
