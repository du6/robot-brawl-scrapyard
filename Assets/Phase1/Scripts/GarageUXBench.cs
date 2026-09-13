// Projection and text sizing regressions. No career state or player preferences
// are touched. RunPure may run in edit mode; its camera is always destroyed.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

namespace RobotBrawl.Phase0
{
public static class GarageUXBench
{
    public static int passed, failed;
    public static string report;
    static void Check(bool condition, string message)
    {
        if (condition) passed++; else { failed++; report += "FAIL " + message + "\n"; }
    }

    public static bool RunPure()
    {
        passed = failed = 0; report = "";
        // Prediction: native, Retina, and downscaled framebuffers all retain
        // at least 14 CSS px at 100%, and 18.2 CSS px at 130%.
        foreach (float sf in new[] { 0.6f, 1f, 1.5f, 2.4f })
        foreach (float ratio in new[] { 0.75f, 1f, 2f, 3f })
        foreach (float scale in new[] { 1f, 1.15f, 1.3f })
        foreach (float nominal in new[] { 8f, 11f, 14f, 20f })
        {
            int units = MobileBuilderUI.DesktopFontUnits(nominal, sf, ratio, scale);
            Check(units * sf / ratio >= 14f * scale - 0.001f,
                  "CSS font floor at scale=" + sf + ", DPR=" + ratio + ", UI=" + scale);
        }
        var go = new GameObject("garage_projection_bench");
        try
        {
            var camera = go.AddComponent<Camera>(); camera.enabled = false;
            camera.nearClipPlane = 0.05f; camera.farClipPlane = 1000f;
            foreach (Vector3 size in new[] { new Vector3(1.2f, 0.6f, 1.5f), new Vector3(6f, 1f, 2f), new Vector3(1f, 5f, 1f) })
            foreach (float aspect in new[] { 0.65f, 1.78f, 2.4f })
            foreach (float pitch in new[] { -65f, 16f, 40f, 80f })
            foreach (float bottom in new[] { 0.15f, 0.45f, 0.68f })
            {
                const float top = 0.10f;
                var bounds = new Bounds(new Vector3(2f, 1f, -3f), size);
                var rotation = Quaternion.Euler(pitch, 35f, 0f);
                camera.aspect = aspect; camera.fieldOfView = 60f;
                float distance = BuilderManager.GarageFitDistance(bounds, rotation, camera.fieldOfView, aspect, 1f - bottom - top);
                camera.transform.SetPositionAndRotation(bounds.center - rotation * Vector3.forward * distance, rotation);
                // The production camera centres the clear band with a shifted
                // projection. Verify every actual projected corner, including
                // near-side perspective, rather than comparing fit formulas.
                float y = camera.nearClipPlane * Mathf.Tan(30f * Mathf.Deg2Rad);
                float shift = y * (bottom - top);
                camera.projectionMatrix = Matrix4x4.Frustum(-y * aspect, y * aspect, -y - shift, y - shift,
                                                           camera.nearClipPlane, camera.farClipPlane);
                bool inside = true;
                for (int corner = 0; corner < 8; corner++)
                {
                    var offset = Vector3.Scale(bounds.extents, new Vector3((corner & 1) == 0 ? -1f : 1f,
                        (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                    Vector3 p = camera.WorldToViewportPoint(bounds.center + offset);
                    inside &= p.z > camera.nearClipPlane && p.x > 0.04f && p.x < 0.96f
                           && p.y > bottom && p.y < 1f - top;
                }
                Check(inside, "whole build visible: bounds=" + size + ", aspect=" + aspect + ", pitch=" + pitch + ", dock=" + bottom);
            }
        }
        finally { Object.DestroyImmediate(go); }
        report = "GarageUXBench: " + passed + " passed, " + failed + " failed\n" + report;
        Debug.Log(report);
        return failed == 0;
    }
}
}
#endif
