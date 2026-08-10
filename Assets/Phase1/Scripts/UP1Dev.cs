// ⚠ EDITOR AND DEVELOPMENT BUILDS ONLY — 2026-08-10.
// This is a QA harness. It was compiling into the SHIPPED iOS player: 44 such
// files, ~13k lines, and the string "dev-only-worker-key" was verified present
// in build/ios global-metadata.dat. Dead weight in the binary, and a surface
// that can register accounts and start fights inside a player's app.
// Nothing in the product references any harness — checked against every
// product script, every scene and every prefab before this guard was added.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RobotBrawl.Phase0
{

/// <summary>
/// ROUND-UP1 IMPLEMENTATION harness. Runs the ROUND-1 CRITIC's exact scenarios
/// so each fix has a controlled before/after number rather than an assertion.
/// Same two honesty rules as UserPathTest / UP1Sweep: verify the hit before
/// believing a probe, and isolate the fixture.
///
/// Every builder claim in here goes through the POINTER (debugPointer ->
/// UpdateBuild -> ghost -> DebugClick). LoadSnapshot is used only to stand a
/// fixture up quickly, never as the basis for a claim.
/// </summary>
public class UP1Dev : MonoBehaviour
{
    public string outFile = "qa_up1_dev_run.txt";
    public string runTag = "BEFORE";
    public bool done;
    public int checks, failures;

    BuilderManager bm;
    P1PartDef[] pal;
    string path;
    const float PANEL = 280f;

    // Proven skeleton. Deck (lowest wheel bottom) = 0.700 - 0.180 = 0.520.
    // Chassis bottom = 0.550, i.e. this machine has 30 mm of ground clearance.
    const string SK =
        "core|0.000,0.700,0.000|0|0.00,0.00,0.00|Steel\n" +
        "chassis|0.000,0.700,0.400|0|0.00,0.00,0.00|Aluminum\n" +
        "chassis|0.000,0.700,-0.400|0|0.00,0.00,0.00|Aluminum\n" +
        "wheel|0.270,0.700,0.400|0|1.00,0.00,0.00|Rubber\n" +
        "wheel|-0.270,0.700,0.400|0|-1.00,0.00,0.00|Rubber\n" +
        "wheel|0.270,0.700,-0.400|0|1.00,0.00,0.00|Rubber\n" +
        "wheel|-0.270,0.700,-0.400|0|-1.00,0.00,0.00|Rubber\n" +
        "battery|0.000,0.975,0.000|0|0.00,0.00,0.00|ABS\n" +
        "engine|0.000,0.975,0.400|0|0.00,0.00,0.00|Steel\n";

    // Same machine with the body raised 0.20 m on its wheels: deck still
    // 0.520, chassis bottom now 0.750, so there is 230 mm of real clearance
    // under the belly. The POSITIVE control for the floor rule - a part CAN
    // legally go under this one's chassis, and the rule must still say yes.
    const string SKHI =
        "core|0.000,0.900,0.000|0|0.00,0.00,0.00|Steel\n" +
        "chassis|0.000,0.900,0.400|0|0.00,0.00,0.00|Aluminum\n" +
        "chassis|0.000,0.900,-0.400|0|0.00,0.00,0.00|Aluminum\n" +
        "wheel|0.270,0.700,0.400|0|1.00,0.00,0.00|Rubber\n" +
        "wheel|-0.270,0.700,0.400|0|-1.00,0.00,0.00|Rubber\n" +
        "wheel|0.270,0.700,-0.400|0|1.00,0.00,0.00|Rubber\n" +
        "wheel|-0.270,0.700,-0.400|0|-1.00,0.00,0.00|Rubber\n" +
        "battery|0.000,1.175,0.000|0|0.00,0.00,0.00|ABS\n" +
        "engine|0.000,1.175,0.400|0|0.00,0.00,0.00|Steel\n";

    // Skeleton + a vertical-hinge pivot on the battery roof carrying a blade
    // arm. The blade's ONLY route to the core is through the pivot, so
    // LimbReport must report it unblocked - checked, not assumed.
    const string SKACT = SK +
        "pivot|0.000,1.250,0.000|0|0.00,1.00,0.00|Steel\n" +
        "blade|0.000,1.250,0.210|0|0.00,0.00,1.00|Steel\n";

    const string CORE = "core|0.000,0.700,0.000|0|0.00,0.00,0.00|Steel\n";

    void W(string s) { File.AppendAllText(path, s + "\n"); }
    void OK(string s)  { checks++; W("   ok    " + s); }
    void BAD(string s) { checks++; failures++; W("   FAIL  " + s); }
    int Idx(string id) { for (int i = 0; i < pal.Length; i++) if (pal[i].id == id) return i; return -1; }

    IEnumerator Start()
    {
        path = Path.Combine(Path.Combine(Application.dataPath, "Phase1"), outFile);
        File.WriteAllText(path, "# UP1 DEV HARNESS  tag=" + runTag + "\n");
        bm = Object.FindFirstObjectByType<BuilderManager>();
        if (bm == null) { W("FATAL no BuilderManager"); done = true; yield break; }
        pal = P1PartDef.Palette();
        Phase0Input.debugThrottle = 0f; Phase0Input.debugSteer = 0f; Phase0Input.debugFire = false;
        Phase0Input.debugPointer = true;
        yield return null;
        W("screen " + Screen.width + "x" + Screen.height);

        yield return A_ActuatorInTest();
        yield return B_BellyPointer();
        yield return C_FloorPositiveControl();
        yield return D_RotateCycle();
        yield return E_RemoveAndRepaint();

        W(string.Format("\n## DONE tag={0} checks={1} failures={2}", runTag, checks, failures));
        Phase0Input.debugPointer = false;
        Phase0Input.debugFire = false;
        Phase0Input.debugThrottle = 0f; Phase0Input.debugSteer = 0f;
        bm.selected = -1; bm.Deselect();
        done = true;
    }

    // ---------------------------------------------------------------- A
    /// <summary>CRITIC CRITICAL 1: is the player's weapon alive in TEST DRIVE?
    /// Identical measurement is run in FIGHT as the control, because FIGHT is
    /// the mode that was known-good.</summary>
    IEnumerator A_ActuatorInTest()
    {
        W("\n## A. ACTUATOR UNDER THE PLAYER TRIGGER, TEST vs FIGHT");
        bm.LoadSnapshot(SKACT);
        yield return null;
        string err = bm.Validate();
        if (err != null) { BAD("SKACT fixture illegal: " + err); yield break; }
        var lr = bm.LimbReport();
        if (lr.Count != 1 || lr[0].blockedBy != null || lr[0].parts < 1)
        { BAD("SKACT limb is not a real limb - measurement would be meaningless"); yield break; }
        W(string.Format("   fixture: 1 pivot limb, parts={0} blockedBy=null tipR={1:F3} rateCeiling={2:F2}",
            lr[0].parts, lr[0].tipRadius, lr[0].rateMax));

        for (int m = 0; m < 2; m++)
        {
            bool fight = m == 1;
            if (fight) bm.StartFight(); else bm.StartTest();
            yield return new WaitForSeconds(1.5f);
            var acts = bm.testRobot != null ? bm.testRobot.GetComponentsInChildren<Actuator>(true) : new Actuator[0];
            int nPC = 0; foreach (var a in acts) if (a.playerControlled) nPC++;
            Phase0Input.debugFire = true;
            float maxRate = 0f, maxTravel = 0f; int cycles = 0;
            bool wasDriving = false;
            float t0 = Time.time;
            while (Time.time - t0 < 9f)
            {
                foreach (var a in acts)
                {
                    if (a.rate > maxRate) maxRate = a.rate;
                    if (a.travel > maxTravel) maxTravel = a.travel;
                    bool d = a.phase == Actuator.Phase.Driving;
                    if (d && !wasDriving) cycles++;
                    wasDriving = d;
                }
                yield return null;
            }
            Phase0Input.debugFire = false;
            W(string.Format("   {0,-5} actuators={1} playerControlled={2}/{1}  maxRate={3:F2} rad/s  maxTravel={4:F3}  cycles={5}",
                fight ? "FIGHT" : "TEST", acts.Length, nPC, maxRate, maxTravel, cycles));
            checks++;
            if (nPC == acts.Length && acts.Length > 0 && maxRate > 1f && cycles > 0)
                W("   ok    weapon LIVE in " + (fight ? "FIGHT" : "TEST"));
            else { failures++; W("   FAIL  weapon DEAD in " + (fight ? "FIGHT" : "TEST")); }
            bm.BackToBuild();
            yield return new WaitForSeconds(0.5f);
        }
    }

    // ---------------------------------------------------------------- B
    /// <summary>CRITIC CRITICAL 2: aim the pointer at the CORE'S UNDERSIDE on a
    /// machine with 30 mm of belly clearance and ask the builder what it thinks.
    /// Then, whatever it says, measure what the machine does.</summary>
    IEnumerator B_BellyPointer()
    {
        W("\n## B. POINTER AT THE CORE UNDERSIDE (30 mm of clearance under the belly)");
        float baseDist = 0f;
        bm.LoadSnapshot(SK); yield return null;
        yield return Drive6(r => baseDist = r);
        W(string.Format("   baseline (no belly part): drove {0:F2} m in 6 s at full throttle", baseDist));

        bm.LoadSnapshot(SK); yield return null;
        int iBr = Idx("bracket");
        bm.selected = iBr;
        bool got = false;
        yield return Reach(0, Vector3.down, r => got = r);
        if (!got) { BAD("core underside never resolved under the pointer - cannot test"); yield break; }
        W(string.Format("   ghost: target={0} normal={1} valid={2} reason=\"{3}\" pos={4} bottom={5:F3} deck=0.520",
            bm.TestGhostTarget, bm.TestGhostNormal.ToString("F2"), bm.TestGhostValid,
            bm.TestGhostReason, bm.TestGhostPos.ToString("F3"), bm.TestGhostPos.y - 0.10f));
        checks++;
        if (bm.TestGhostValid)
        {
            failures++;
            W("   FAIL  builder says GREEN on a part 0.170 m below the wheel line");
            Phase0Input.DebugClick();
            yield return null; yield return null; yield return null;
            float d2 = 0f;
            yield return Drive6(r => d2 = r);
            W(string.Format("   consequence: with the belly part it drove {0:F2} m (baseline {1:F2} m)", d2, baseDist));
        }
        else W("   ok    builder REFUSES it: \"" + bm.TestGhostReason + "\"");
    }

    // ---------------------------------------------------------------- C
    /// <summary>The floor rule must not become "reject everything downward".
    /// Same face, same part, two heights, on a machine with 230 mm of real
    /// belly clearance: the one that clears the wheel line must stay LEGAL.</summary>
    IEnumerator C_FloorPositiveControl()
    {
        W("\n## C. POSITIVE CONTROL - raised body, 230 mm of belly clearance");
        bm.LoadSnapshot(SKHI); yield return null;
        string err = bm.Validate();
        W("   SKHI fixture parts=" + bm.placed.Count + " Validate=" + (err == null ? "null (legal)" : err));
        if (err != null) { BAD("SKHI fixture illegal, positive control unusable"); yield break; }

        // fore chassis is index 1; its underside is at y 0.750, deck is 0.520.
        bm.selected = Idx("bracket");
        bool got = false;
        yield return Reach(1, Vector3.down, r => got = r);
        if (!got) { BAD("chassis underside never resolved under the pointer"); yield break; }
        float bot = bm.TestGhostPos.y - 0.10f;
        W(string.Format("   bracket under the raised chassis: pos={0} bottom={1:F3} deck=0.520 valid={2} reason=\"{3}\"",
            bm.TestGhostPos.ToString("F3"), bot, bm.TestGhostValid, bm.TestGhostReason));
        checks++;
        if (bm.TestGhostValid && bot >= 0.519f) W("   ok    legal low placement still ALLOWED (clears the wheel line)");
        else if (!bm.TestGhostValid) { failures++; W("   FAIL  over-strict: refused a part that clears the wheel line"); }
        else { failures++; W("   FAIL  accepted a part BELOW the wheel line"); }

        if (!bm.TestGhostValid) yield break;
        Phase0Input.DebugClick(); yield return null; yield return null; yield return null;
        int added = bm.placed.Count;
        W("   clicked; parts now " + added);
        // Now hang a SECOND bracket off the first one's underside: bottom
        // 0.350, i.e. 0.170 below the wheel line. This one must be refused.
        bm.selected = Idx("bracket");
        got = false;
        yield return Reach(added - 1, Vector3.down, r => got = r);
        if (!got) { W("   (second bracket face not reachable - skipped)"); yield break; }
        float bot2 = bm.TestGhostPos.y - 0.10f;
        W(string.Format("   second bracket below it: bottom={0:F3} valid={1} reason=\"{2}\"",
            bot2, bm.TestGhostValid, bm.TestGhostReason));
        checks++;
        if (!bm.TestGhostValid) W("   ok    below-the-wheel-line placement REFUSED");
        else { failures++; W("   FAIL  accepted a part 0.170 m below the wheel line"); }
    }

    // ---------------------------------------------------------------- D
    /// <summary>CRITIC MINOR: does every R press change something the player
    /// can see? Counts DISTINCT rendered mesh sizes over a full cycle.</summary>
    IEnumerator D_RotateCycle()
    {
        W("\n## D. R CYCLE - distinct yaw states vs distinct DRAWN meshes");
        string[] ids = { "beam", "beamlong", "plate", "chassis" };
        foreach (string id in ids)
        {
            int pi = Idx(id);
            if (pi < 0) { W("   " + id + " not in palette"); continue; }
            // Bare core: the skeleton has a battery on the core roof, which
            // would make every +Y probe "Blocked by another part" - true, and
            // nothing to do with rotation (UserPathTest fixture-isolation rule).
            bm.LoadSnapshot(CORE); yield return null;
            bm.selected = pi;
            bool got = false;
            yield return Reach(0, Vector3.up, r => got = r);
            if (!got) { W("   " + id + ": core roof not reachable, skipped"); continue; }

            var yaws = new List<int>();
            var meshes = new List<Vector3>();
            string trail = "";
            for (int i = 0; i < 5; i++)
            {
                int y = bm.TestGhostYaw;
                Vector3 ms = bm.TestGhostMeshSize();
                trail += (i == 0 ? "" : " -> ") + y;
                if (!yaws.Contains(y)) yaws.Add(y);
                bool seen = false;
                foreach (var m in meshes) if ((m - ms).sqrMagnitude < 1e-6f) seen = true;
                if (!seen) meshes.Add(ms);
                Phase0Input.DebugRotate();
                yield return null; yield return null; yield return null;
            }
            checks++;
            bool clean = yaws.Count == meshes.Count;
            W(string.Format("   {0,-9} yaw trail {1,-22}  distinct yaws={2}  distinct meshes={3}  {4}",
                id, trail, yaws.Count, meshes.Count, clean ? "ok - every press changes the drawing" : "!! DEAD PRESS"));
            if (!clean) failures++;
        }
    }

    // ---------------------------------------------------------------- E
    /// <summary>The builder's other two mouse verbs. Until this round neither
    /// could be driven by a test at all; right-click REMOVE and middle-click
    /// REPAINT are the two destructive ones, so they are the last place you
    /// want an untested path. Hit verification is indirect but sound: with a
    /// part selected, TestGhostTarget names whatever the builder's own raycast
    /// resolved, and RemovePart/SetPartMaterial call the SAME PartUnderMouse()
    /// raycast in the same frame.</summary>
    IEnumerator E_RemoveAndRepaint()
    {
        W("\n## E. RIGHT-CLICK REMOVE and MIDDLE-CLICK REPAINT through the pointer");
        bm.LoadSnapshot(SK); yield return null;
        bm.selected = Idx("bracket");   // arms the ghost pipeline so Reach can verify the hit

        // AFT chassis (index 2 in SK): a structural part with a free roof and
        // no material restriction. The battery roof carries nothing either but
        // power parts can veto a material via P1PartDef.EffectiveMat, which
        // would make a null result ambiguous.
        int bat = 2;
        if (bat >= bm.placed.Count || bm.placed[bat].def.id != "chassis")
        { BAD("fixture order changed - expected chassis at index 2"); yield break; }
        string was = bm.placed[bat].MatName();
        bool got = false;
        yield return Reach(bat, Vector3.up, r => got = r);
        if (!got) { BAD("aft chassis roof never resolved under the pointer"); yield break; }
        bm.activeMat = "Titanium";
        Phase0Input.DebugClick(2);
        yield return null; yield return null; yield return null;
        string now = bm.placed[bat].MatName();
        checks++;
        W("   middle-click on the aft chassis: material " + was + " -> " + now + " (activeMat=Titanium)");
        if (now == "Titanium") W("   ok    REPAINT works through the pointer");
        else { failures++; W("   FAIL  repaint did nothing"); }

        int eng = PlacedIdx("engine");
        if (eng < 0) { BAD("no engine in fixture"); yield break; }
        int before = bm.placed.Count;
        got = false;
        yield return Reach(eng, Vector3.up, r => got = r);
        if (!got) { BAD("engine roof never resolved under the pointer"); yield break; }
        Phase0Input.DebugClick(1);
        yield return null; yield return null; yield return null;
        checks++;
        W("   right-click on the engine: parts " + before + " -> " + bm.placed.Count);
        if (bm.placed.Count == before - 1 && PlacedIdx("engine") < 0)
            W("   ok    REMOVE works through the pointer");
        else { failures++; W("   FAIL  remove did nothing"); }

        // And the guard that matters: right-clicking the CORE must be refused,
        // because removing it would orphan the whole build.
        before = bm.placed.Count;
        got = false;
        // +X face: the core roof carries the battery on this fixture.
        yield return Reach(0, Vector3.right, r => got = r);
        if (got)
        {
            Phase0Input.DebugClick(1);
            yield return null; yield return null; yield return null;
            checks++;
            W("   right-click on the CORE: parts " + before + " -> " + bm.placed.Count);
            if (bm.placed.Count == before) W("   ok    core is protected from removal");
            else { failures++; W("   FAIL  the core was removed"); }
        }
        else W("   (core roof not reachable on this fixture - guard untested)");
    }

    int PlacedIdx(string id)
    { for (int i = 0; i < bm.placed.Count; i++) if (bm.placed[i].def.id == id) return i; return -1; }

    // ---------------------------------------------------------------- utils
    /// <summary>Full throttle for 6 s in TEST DRIVE, horizontal distance.</summary>
    IEnumerator Drive6(System.Action<float> report)
    {
        if (bm.Validate() != null) { report(-1f); yield break; }
        bm.StartTest();
        yield return new WaitForSeconds(1.2f);
        if (bm.testRobot == null) { report(-1f); yield break; }
        Vector3 p0 = bm.testRobot.transform.position;
        Phase0Input.debugThrottle = 1f;
        yield return new WaitForSeconds(6f);
        Phase0Input.debugThrottle = 0f;
        Vector3 p1 = bm.testRobot.transform.position;
        report(new Vector2(p1.x - p0.x, p1.z - p0.z).magnitude);
        bm.BackToBuild();
        yield return new WaitForSeconds(0.5f);
    }

    /// <summary>Swing the builder camera until its OWN raycast resolves the
    /// wanted face of the wanted part. Copied from UP1Sweep: projecting a face
    /// centre to screen does NOT guarantee you hit that face.</summary>
    IEnumerator Reach(int targetIdx, Vector3 dir, System.Action<bool> report)
    {
        if (targetIdx < 0 || targetIdx >= bm.placed.Count) { report(false); yield break; }
        var p = bm.placed[targetIdx];
        string wantId = p.def.id;
        int ax = Mathf.Abs(dir.x) > 0.5f ? 0 : (Mathf.Abs(dir.y) > 0.5f ? 1 : 2);
        float sgn = dir[ax] >= 0f ? 1f : -1f;
        Vector3 world = p.pos + Vector3.Scale(dir, p.Half()) * 1.02f;
        float faceCoord = p.pos[ax] + sgn * p.Half()[ax];

        float[] pitches = dir.y < -0.5f ? new[] { -45f, -20f, -65f, -35f, -55f, -75f }
                        : dir.y > 0.5f  ? new[] { 55f, 30f, 70f, 40f }
                                        : new[] { 20f, 30f, 0f, 45f, 10f };
        float[] yaws = { 35f, 125f, 215f, 305f, 80f, 170f, 260f, 350f, 5f, 95f };
        foreach (float pitch in pitches)
        foreach (float yaw in yaws)
        {
            bm.TestOrbitYaw = yaw; bm.TestOrbitPitch = pitch;
            yield return null;
            var cam = bm.TestCam;
            if (cam == null) { report(false); yield break; }
            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0f || sp.x < PANEL + 8f || sp.x > Screen.width - 4f || sp.y < 4f || sp.y > Screen.height - 4f) continue;
            Phase0Input.debugMousePos = sp;
            yield return null; yield return null;
            if (bm.TestGhostTarget != wantId) continue;
            if (Vector3.Dot(bm.TestGhostNormal.normalized, dir) < 0.9f) continue;
            float d = bm.TestGhostPos[ax] - faceCoord;
            if (d * sgn <= 0.001f || Mathf.Abs(d) > 0.7f) continue;
            report(true); yield break;
        }
        report(false);
    }
}

}
#endif
