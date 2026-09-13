// Camera safety and result wording regressions. No bouts are run, no career
// state is read or written, and all temporary objects are removed in finally.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using UnityEngine;

namespace RobotBrawl.Phase0
{
public static class CombatReadabilityBench
{
    public static int passed, failed;
    public static string report;
    static readonly List<string> lines = new List<string>();
    static void Check(bool ok, string what)
    {
        if (ok) passed++; else failed++;
        lines.Add((ok ? "PASS  " : "FAIL  ") + what);
    }

    static bool InFrame(Camera camera, Vector3 point)
    {
        Vector3 p = camera.WorldToViewportPoint(point + Vector3.up * 0.3f);
        return p.z > 0f && p.x > 0.04f && p.x < 0.96f && p.y > 0.04f && p.y < 0.96f;
    }

    static bool WallBlocks(Collider wall, Vector3 target, Vector3 lens)
    {
        Vector3 start = target + Vector3.up * 0.3f;
        return wall.Raycast(new Ray(start, (lens - start).normalized), out _, Vector3.Distance(start, lens));
    }

    public static bool RunPure()
    {
        passed = failed = 0; lines.Clear();
        string mobility = FightManager.JudgedSummary("mobility", false, 48f, 99f, "Scout");
        Check(mobility.StartsWith("Scout won on mobility: 99% vs 48%"), "mobility names the actual winner and winning value first");
        Check(!mobility.Contains("aggression") && !mobility.Contains("hunting"), "mobility makes no unsupported claim about attacking");
        Check(mobility.Contains("Damage and surviving pieces were close"), "explains why the damage lead was insufficient");
        Check(FightManager.JudgedSummary("control", true, 5f, 40f, "Scout").StartsWith("You won on stability: 5% vs 40%"), "stability's smaller value is correctly presented as the winner");
        Check(FightManager.JudgedSummary("damage", false, 48f, 99f, "Scout").StartsWith("Scout won on damage: 99 vs 48"), "damage uses points, not percentages");
        Check(FightManager.JudgedSummary("structure", true, 90f, 50f, "Scout").Contains("90% vs 50% intact"), "structure explains the normalized surviving-pieces criterion");

        // Far from any open editor scene, and unclamped, to test only the
        // camera under test. Prediction: static AND kinematic slabs require
        // a climb, but the same geometry registered as loose debris does not.
        var root = new GameObject("combat_readability_bench");
        GameObject debris = null;
        try
        {
            Vector3 origin = new Vector3(10000f, 0f, 10000f);
            var a = new GameObject("bench_robot_a"); a.transform.SetParent(root.transform);
            var b = new GameObject("bench_robot_b"); b.transform.SetParent(root.transform);
            var lens = new GameObject("bench_camera"); lens.transform.SetParent(root.transform);
            var camera = lens.AddComponent<Camera>(); camera.enabled = false;
            var follow = lens.AddComponent<FightCamera>(); follow.enabled = false;
            follow.a = a.transform; follow.b = b.transform; follow.clampHalf = 0f;
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "bench_closing_wall"; wall.transform.SetParent(root.transform);
            wall.transform.position = origin + new Vector3(2f, 0.8f, 0f);
            wall.transform.localScale = new Vector3(0.6f, 1.6f, 16f);
            var wallCollider = wall.GetComponent<Collider>();
            wall.SetActive(false);
            a.transform.position = origin + new Vector3(0f, 0f, -1f);
            b.transform.position = origin + new Vector3(0f, 0f, 1f);
            camera.aspect = 1.78f; follow.SnapNow();
            float clearHeight = lens.transform.position.y;
            wall.SetActive(true); Physics.SyncTransforms(); follow.SnapNow();
            Check(lens.transform.position.y > clearHeight + 0.5f, "static wall makes the camera climb instead of zooming into one robot");
            Check(!WallBlocks(wallCollider, a.transform.position, lens.transform.position)
                  && !WallBlocks(wallCollider, b.transform.position, lens.transform.position), "both sightlines clear the static wall");
            var wallBody = wall.AddComponent<Rigidbody>(); wallBody.isKinematic = true;
            Physics.SyncTransforms(); follow.SnapNow();
            Check(lens.transform.position.y > clearHeight + 0.5f, "a kinematic crusher is still an obstruction");
            foreach (float aspect in new[] { 0.65f, 1f, 1.78f, 2.4f })
            foreach (float separation in new[] { 0.8f, 4f, 12f })
            {
                camera.aspect = aspect;
                a.transform.position = origin - Vector3.forward * separation * 0.5f;
                b.transform.position = origin + Vector3.forward * separation * 0.5f;
                follow.SnapNow();
                Check(InFrame(camera, a.transform.position) && InFrame(camera, b.transform.position)
                      && !WallBlocks(wallCollider, a.transform.position, lens.transform.position)
                      && !WallBlocks(wallCollider, b.transform.position, lens.transform.position),
                      "both robots visible above moving wall: aspect=" + aspect + ", separation=" + separation);
            }
            wall.SetActive(false);
            debris = GameObject.CreatePrimitive(PrimitiveType.Cube);
            debris.transform.position = wall.transform.position; debris.transform.localScale = wall.transform.localScale;
            debris.AddComponent<Rigidbody>().isKinematic = true;
            CompoundRobot.Spawned.Add(debris);
            camera.aspect = 1.78f;
            a.transform.position = origin - Vector3.forward; b.transform.position = origin + Vector3.forward;
            Physics.SyncTransforms(); follow.SnapNow();
            Check(Mathf.Abs(lens.transform.position.y - clearHeight) < 0.01f, "registered robot debris is excluded by identity, even when frozen");
            debris.SetActive(false);
            var robotPart = GameObject.CreatePrimitive(PrimitiveType.Cube);
            robotPart.transform.SetParent(a.transform); robotPart.transform.position = wall.transform.position;
            robotPart.transform.localScale = wall.transform.localScale;
            // Reassign once to model the initial SnapNow after a complete bot
            // spawn. Its detached wheel retains exactly this transform later.
            follow.a = null; follow.SnapNow(); follow.a = a.transform;
            Physics.SyncTransforms(); follow.SnapNow();
            Check(Mathf.Abs(lens.transform.position.y - clearHeight) < 0.01f, "live robot geometry is excluded by child identity");
            robotPart.transform.SetParent(root.transform, true);
            Physics.SyncTransforms(); follow.SnapNow();
            Check(Mathf.Abs(lens.transform.position.y - clearHeight) < 0.01f, "detaching a wheel does not make it a camera wall");
        }
        finally
        {
            if (debris != null) { CompoundRobot.Spawned.Remove(debris); Object.DestroyImmediate(debris); }
            Object.DestroyImmediate(root);
        }
        report = string.Join("\n", lines) + string.Format("\nRESULT: {0} pass, {1} fail", passed, failed);
        Debug.Log("[CombatReadabilityBench] " + report);
        return failed == 0;
    }
}
}
#endif
