// ⚠ EDITOR AND DEVELOPMENT BUILDS ONLY — 2026-08-10.
// This is a QA harness. It was compiling into the SHIPPED iOS player: 44 such
// files, ~13k lines, and the string "dev-only-worker-key" was verified present
// in build/ios global-metadata.dat. Dead weight in the binary, and a surface
// that can register accounts and start fights inside a player's app.
// Nothing in the product references any harness — checked against every
// product script, every scene and every prefab before this guard was added.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using UnityEngine;

namespace RobotBrawl.Phase0
{
/// <summary>Aim-assist regression probe (2026-07-30, owen: 'friction when
/// attaching a beam to another one end to end'). Places a beam flat on the
/// core, holds a second beam, then sweeps the pointer along the first
/// beam's axis - across the tip and beyond - and reports what the ghost
/// resolved to at each sample. Run in play mode: AimProbe.Run().</summary>
public class AimProbe : MonoBehaviour
{
    public static AimProbe Run()
    { return new GameObject("aim_probe").AddComponent<AimProbe>(); }

    public bool finished;

    IEnumerator Start()
    {
        var bm = Object.FindFirstObjectByType<BuilderManager>();
        if (bm == null) { Debug.Log("[AimProbe] no builder"); finished = true; yield break; }
        // Place beam flat on the core top (long axis z), if not already there.
        if (bm.PlacedCount == 1)
        {
            int beamIdx = -1;
            for (int i = 0; i < bm.PaletteCount; i++)
                if (bm.PartLabel(i).StartsWith("Beam")) { beamIdx = i; break; }
            bm.SelectPart(beamIdx);
            Vector3 sp = Camera.main.WorldToScreenPoint(new Vector3(0f, 0.86f, 0f));
            Phase0Input.debugPointer = true;
            Phase0Input.debugMousePos = new Vector3(sp.x, sp.y, 0f);
            yield return null; yield return null;
            Phase0Input.DebugClick(0);
            yield return null; yield return null;
            Debug.Log("[AimProbe] setup placed count = " + bm.PlacedCount);
        }
        if (bm.PlacedCount < 2) { Debug.Log("[AimProbe] setup FAILED"); finished = true; yield break; }
        if (!bm.HasSelection)
        {
            for (int i = 0; i < bm.PaletteCount; i++)
                if (bm.PartLabel(i).StartsWith("Beam")) { bm.SelectPart(i); break; }
        }
        // The placed beam: flat on core, spans z -0.30..+0.30 at y ~0.95.
        // Sweep world z from mid-beam (-0.18) to well past the tip (-0.65).
        int endHits = 0, total = 0;
        string line = "";
        for (float z = -0.18f; z >= -0.65f; z -= 0.03f)
        {
            Vector3 w = new Vector3(0f, 0.95f, z);
            Vector3 sp = Camera.main.WorldToScreenPoint(w);
            Phase0Input.debugPointer = true;
            Phase0Input.debugMousePos = new Vector3(sp.x, sp.y, 0f);
            yield return null; yield return null;
            var g = GameObject.Find("ghost");
            string what = "free";
            if (g != null && g.activeSelf)
            {
                Vector3 gp = g.transform.position;
                if (gp.z < -0.55f && Mathf.Abs(gp.x) < 0.06f && Mathf.Abs(gp.y - 0.95f) < 0.1f) what = "END";
                else if (gp.y > 1.05f) what = "top";
                else if (Mathf.Abs(gp.x) > 0.1f) what = "side";
                else if (gp.y < 0.5f) what = "low";
            }
            total++;
            if (what == "END") endHits++;
            line += string.Format("z={0:F2}:{1}  ", z, what);
        }
        Debug.Log("[AimProbe] sweep: " + line);
        Debug.Log(string.Format("[AimProbe] END-mount acquired at {0}/{1} samples along the tip approach", endHits, total));
        Phase0Input.debugPointer = false;
        finished = true;
    }
}
}
#endif
