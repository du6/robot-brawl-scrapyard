using System.Collections;
using System.IO;
using UnityEngine;

namespace RobotBrawl.Phase0
{
/// <summary>Walks the ROTOR-SWEEP refusal down the player's own path
/// (debugPointer -> UpdateBuild -> UpdateGhost -> ghost), 2026-08-05.
///
/// WHY THIS EXISTS AND CareerSmoke C20 IS NOT ENOUGH. C20 drives PlaceByFace,
/// which is the programmatic seam. The 2026-08-04 session shipped four defects
/// that every API-level check passed: a chooser whose button could not be
/// found, chips painted over by the palette, a screen made entirely of IMGUI
/// that no uGUI sweep can see. The lesson written down at the time was "after
/// any UI change, take the screen and use the thing". A refusal the player
/// never sees is not a refusal, so this hovers the real ghost and reads what
/// the real ghost says.</summary>
public class RotorProbe : MonoBehaviour
{
    public static bool done;
    public static string report = "";

    const string BASE = BuilderManager.SNAP_STAMP
                      + "\ncore|0.000,0.700,0.000|0|0.00,0.00,0.00|Aluminum";

    BuilderManager bm;
    int checks, failures;

    public static RotorProbe Run()
    {
        done = false; report = "";
        return new GameObject("rotor_probe").AddComponent<RotorProbe>();
    }

    void W(string s) { report += s + "\n"; Debug.Log("[RotorProbe] " + s); }

    /// <summary>Swing the orbit until `dir` of placed[idx] is the face the
    /// BUILDER'S OWN raycast reports, then leave the pointer sitting on it.
    /// Lifted from UP2Dev.ReachPart - a probe that aims with its own arithmetic
    /// instead of the builder's is testing a machine nobody is looking at.</summary>
    IEnumerator ReachPart(int idx, Vector3 dir, System.Action<bool> report_)
    {
        if (idx < 0 || idx >= bm.placed.Count) { report_(false); yield break; }
        var p = bm.placed[idx];
        Vector3 world = p.pos + Vector3.Scale(dir, p.Half()) * 1.02f;
        string wantId = p.def.id;
        float[] pitches = dir.y > 0.5f ? new[] { 55f, 30f, 70f } : new[] { 20f, 30f, 0f };
        foreach (float pitch in pitches)
        foreach (float yaw in new[] { 35f, 125f, 215f, 305f })
        {
            bm.TestOrbitYaw = yaw;
            bm.TestOrbitPitch = pitch;
            yield return null;
            var cam = bm.TestCam;
            if (cam == null) { report_(false); yield break; }
            Phase0Input.debugMousePos = cam.WorldToScreenPoint(world);
            yield return null; yield return null;
            if (bm.TestGhostTarget == wantId
                && Vector3.Dot(bm.TestGhostNormal, dir) > 0.9f) { report_(true); yield break; }
        }
        report_(false);
    }

    void Expect(bool ok, string what)
    {
        checks++;
        if (!ok) failures++;
        W((ok ? "PASS  " : "FAIL  ") + what);
    }

    IEnumerator Start()
    {
        bm = Object.FindFirstObjectByType<BuilderManager>();
        if (bm == null) { W("FATAL no BuilderManager"); done = true; yield break; }
        Phase0Input.debugPointer = true;
        // CAREER OFF for the duration. The first run of this probe failed its
        // own control - a GREEN ghost that placed nothing - and the rule was
        // innocent: the career shelf holds 1 spindle and NO spinner blade, so
        // the click landed on the stock gate ("No Steel Spinner blade left"),
        // which is checked before the placement branch and is not a ghost rule
        // at all. A rule test that a shop can veto is measuring the shop.
        bool savedCareer = Career.active;
        Career.active = false;
        int iSpindle = bm.PaletteIndexOf("spindle");
        int iSpinner = bm.PaletteIndexOf("spinner");
        yield return null;

        // ---- A. THE BUG, through the pointer. Spindle on the core's flank,
        //         turned to a VERTICAL axle, so its rotor swings a full circle
        //         back through the core.
        bm.LoadSnapshot(BASE);
        yield return null; yield return null;
        bm.selected = iSpindle;
        bool got = false;
        yield return ReachPart(0, Vector3.right, r => got = r);
        Expect(got, "A: the core's +X face is reachable by pointer");
        if (got)
        {
            // yaw 180 = Y axle. Three R presses from 0 -> 90 -> 180.
            while (bm.TestGhostYaw != 180)
            { Phase0Input.DebugRotate(); yield return null; yield return null; }
            Expect(bm.TestGhostValid, "A: the spindle itself is accepted (" + bm.TestGhostReason + ")");
            Phase0Input.DebugClick();
            yield return null; yield return null; yield return null;
        }
        Expect(bm.placed.Count == 2, "A: spindle placed (" + bm.placed.Count + " parts)");

        bm.selected = iSpinner;
        bool got2 = false;
        yield return ReachPart(1, Vector3.right, r => got2 = r);
        Expect(got2, "A: the spindle's outer face is reachable with a rotor held");
        string why = bm.TestGhostReason;
        Expect(!bm.TestGhostValid, "A: THE GHOST IS RED for a rotor that would saw the core");
        Expect(why != null && why.ToLower().Contains("sweep"),
               "A: and the ghost says why -> \"" + why + "\"");
        Expect(bm.TestGhostShown, "A: the ghost is still SHOWN, so the player can see what is refused");
        // A red ghost that still places would be the worst of both worlds.
        Phase0Input.DebugClick();
        yield return null; yield return null; yield return null;
        Expect(bm.placed.Count == 2, "A: clicking a red ghost places nothing (" + bm.placed.Count
               + " parts)");
        // The reason has to reach the player on the TOUCH UI too, which pumps
        // LastMessage and cannot see the IMGUI panel ghostReason used to live in.
        Expect(bm.LastMessage != null && bm.LastMessage.ToLower().Contains("sweep"),
               "A: the refused click says why in the status channel -> \"" + bm.LastMessage + "\"");

        // ---- B. CONTROL, same two parts on the roof: a horizontal circle over
        //         the machine, which is the single most-built weapon in the
        //         game and must stay green.
        bm.LoadSnapshot(BASE);
        yield return null; yield return null;
        bm.selected = iSpindle;
        bool got3 = false;
        yield return ReachPart(0, Vector3.up, r => got3 = r);
        Expect(got3, "B: the core's roof is reachable");
        if (got3) { Phase0Input.DebugClick(); yield return null; yield return null; yield return null; }
        bm.selected = iSpinner;
        bool got4 = false;
        yield return ReachPart(1, Vector3.up, r => got4 = r);
        Expect(got4, "B: the spindle's top face is reachable with a rotor held");
        Expect(bm.TestGhostValid, "B CONTROL: a rotor that clears the machine is still GREEN ("
                                  + bm.TestGhostReason + ")");
        Phase0Input.DebugClick();
        yield return null; yield return null; yield return null;
        Expect(bm.placed.Count == 3, "B CONTROL: and it places (" + bm.placed.Count
               + " parts, msg=\"" + bm.LastMessage + "\")");

        // Leave the fouling case on screen for the screenshot.
        bm.LoadSnapshot(BASE);
        yield return null; yield return null;
        bm.selected = iSpindle;
        bool g5 = false;
        yield return ReachPart(0, Vector3.right, r => g5 = r);
        if (g5)
        {
            while (bm.TestGhostYaw != 180)
            { Phase0Input.DebugRotate(); yield return null; yield return null; }
            Phase0Input.DebugClick();
            yield return null; yield return null; yield return null;
        }
        bm.selected = iSpinner;
        bool g6 = false;
        yield return ReachPart(1, Vector3.right, r => g6 = r);
        W("screenshot state: ghostShown=" + bm.TestGhostShown + " valid=" + bm.TestGhostValid
          + " reason=\"" + bm.TestGhostReason + "\" reached=" + g6);

        Career.active = savedCareer;
        // Unlatch the synthetic pointer, or the builder keeps aiming at
        // wherever the probe left it and the mouse appears dead.
        Phase0Input.debugPointer = false;
        bm.selected = -1;
        W(string.Format("RESULT: {0} checks, {1} failures{2}",
                        checks, failures, failures == 0 ? " - ALL GREEN" : " - FIX NEEDED"));
        try
        {
            File.WriteAllText(Path.Combine(Path.Combine(Application.dataPath, "Phase1"),
                              "qa_rotor_probe.txt"), report);
        }
        catch (System.Exception e) { Debug.LogWarning("[RotorProbe] write failed: " + e.Message); }
        done = true;
    }
}
}
