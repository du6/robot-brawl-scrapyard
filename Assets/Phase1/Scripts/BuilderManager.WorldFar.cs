// ===========================================================================
// BuilderManager.WorldFar.cs — THE HORIZON, ROADS, LANDMARKS, LIFE
// (Robot Brawl: Scrapyard). owen, 2026-09-10: "Investigate how other popular
// games design the game map and improve our game's visual experience."
//
// What the best open worlds share (Breath of the Wild's "triangle rule" and
// its urban-planning roots in Kevin Lynch's landmarks; Genshin's massive
// structures kept visible from afar; Disney's "weenies"): you can always SEE
// somewhere worth going; the land has DISTRICTS with their own silhouette
// and palette; PATHS make it read as a place people use; and there is
// LIFE at ground level so the walk between landmarks is never empty.
// Our world had a 116 m fog wall (nothing beyond the loaded chunks), no
// roads, one silhouette (the cube), and bare ground. So:
//   THE HORIZON  a coarse far mesh out to 900 m under lighter fog: hills,
//                mesas and craters on the skyline, not a wall.
//   LANDMARKS    one per 600 m: a 150 m tower, a ring gate, a crashed hull,
//                a spire cluster - built from the seed, seen from 700 m.
//   ROADS        vertex-coloured lanes between neighbouring places.
//   LIFE         biome scatter - boulders and scrap on the rust, tufts and
//                crystal on the steppe, obsidian and vents on the ash - a
//                dozen or two per chunk, static-batched into a few draws.
//   POOLS        the deeper craters hold a glowing coolant pool.
// ===========================================================================
using System.Collections.Generic;
using UnityEngine;

namespace RobotBrawl.Phase0
{
public partial class BuilderManager
{
    // ------------------------------------------------------------ the horizon
    public const float FAR_RADIUS = 900f;        // the far mesh reaches this far from its centre
    public const int   FAR_RES = 72;             // cells across (25 m each at 900 m)
    public const float FAR_RECENTRE = 150f;      // rebuild when the player is this far from its centre
    public const float FAR_SINK = 0.6f;          // sits this far under the true ground (hidden under the near chunks)
    public const float FOG_START = 70f, FOG_END = 640f;
    GameObject farRoot, farGround; Vector3 farCentre; Material matFar;
    Mesh farMesh;

    void EnsureFar(Vector3 at)
    {
        if (farRoot == null) { farRoot = new GameObject("far"); farRoot.transform.SetParent(worldRoot.transform, false); }
        if (matFar == null && matGround != null) { matFar = new Material(matGround); matFar.SetFloat("_GridStrength", 0f); }
        RebuildFar(at);
        landmarks.Clear();
        PumpLandmarks(at);
    }

    void PumpFar(Vector3 me)
    {
        if (farRoot == null) return;
        if (Vector2.Distance(new Vector2(me.x, me.z), new Vector2(farCentre.x, farCentre.z)) > FAR_RECENTRE) RebuildFar(me);
        PumpLandmarks(me);
    }

    /// <summary>The far mesh: FAR_RES x FAR_RES cells centred on a 150 m
    /// multiple, heights from TerrainHeight, colours from GroundColor, no
    /// collider, no grid. One draw.</summary>
    void RebuildFar(Vector3 at)
    {
        farCentre = new Vector3(Mathf.Round(at.x / FAR_RECENTRE) * FAR_RECENTRE, 0f, Mathf.Round(at.z / FAR_RECENTRE) * FAR_RECENTRE);
        int n = FAR_RES + 1;
        float cell = FAR_RADIUS * 2f / FAR_RES;
        var verts = new Vector3[n * n];
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                float x = farCentre.x - FAR_RADIUS + i * cell, z = farCentre.z - FAR_RADIUS + j * cell;
                verts[j * n + i] = new Vector3(x, TerrainHeight(x, z) - FAR_SINK, z);
            }
        var tris = new int[FAR_RES * FAR_RES * 6]; int t = 0;
        for (int j = 0; j < FAR_RES; j++)
            for (int i = 0; i < FAR_RES; i++)
            {
                int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
                tris[t++] = a; tris[t++] = c; tris[t++] = b;
                tris[t++] = b; tris[t++] = c; tris[t++] = d;
            }
        if (farMesh == null) farMesh = new Mesh { name = "far_terrain" };
        farMesh.Clear();
        farMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        farMesh.vertices = verts; farMesh.triangles = tris;
        farMesh.RecalculateNormals(); farMesh.RecalculateBounds();
        var norms = farMesh.normals;
        var cols = new Color[verts.Length];
        for (int k = 0; k < verts.Length; k++) cols[k] = GroundColor(verts[k].x, verts[k].z, verts[k].y + FAR_SINK, norms[k]);
        farMesh.colors = cols;
        if (farGround == null)
        {
            farGround = new GameObject("far_terrain");
            farGround.transform.SetParent(farRoot.transform, false);
            farGround.AddComponent<MeshFilter>().sharedMesh = farMesh;
            var mr = farGround.AddComponent<MeshRenderer>();
            mr.sharedMaterial = matFar != null ? matFar : matGround;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }
    }
    public GameObject FarGround { get { return farGround; } }

    // ------------------------------------------------------------ landmarks
    public const float LANDMARK_CELL = 600f;
    public const float LANDMARK_SEE = 1100f;      // built inside this, dropped beyond it + 200
    readonly Dictionary<long, GameObject> landmarks = new Dictionary<long, GameObject>();
    public int LandmarksBuilt { get { return landmarks.Count; } }

    /// <summary>The landmark of a 600 m cell: kind and position from the seed.
    /// Every cell has one (this is the skyline), never within 120 m of home.</summary>
    public bool LandmarkOf(int lx, int lz, out int kind, out Vector3 pos)
    {
        var rng = new System.Random(unchecked(worldSeed * 15731 ^ lx * 789221 ^ lz * 1376312589));
        kind = rng.Next(4);
        float m = 90f;
        pos = new Vector3(lx * LANDMARK_CELL + m + (float)rng.NextDouble() * (LANDMARK_CELL - 2f * m), 0f,
                          lz * LANDMARK_CELL + m + (float)rng.NextDouble() * (LANDMARK_CELL - 2f * m));
        if (Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(homePos.x, homePos.z)) < 120f)
            pos += new Vector3(140f, 0f, 90f);
        pos.y = TerrainHeight(pos.x, pos.z);
        return true;
    }

    void PumpLandmarks(Vector3 me)
    {
        if (farRoot == null) return;
        int cx = Mathf.FloorToInt(me.x / LANDMARK_CELL), cz = Mathf.FloorToInt(me.z / LANDMARK_CELL);
        for (int dz = -2; dz <= 2; dz++)
            for (int dx = -2; dx <= 2; dx++)
            {
                int lx = cx + dx, lz = cz + dz;
                long k = ((long)lx << 32) ^ (uint)lz;
                if (landmarks.ContainsKey(k)) continue;
                int kind; Vector3 pos;
                LandmarkOf(lx, lz, out kind, out pos);
                if (Vector2.Distance(new Vector2(me.x, me.z), new Vector2(pos.x, pos.z)) > LANDMARK_SEE) continue;
                landmarks[k] = BuildLandmark(kind, pos, new System.Random(unchecked(worldSeed ^ lx * 31 ^ lz * 977)));
            }
        List<long> drop = null;
        foreach (var kv in landmarks)
        {
            if (kv.Value == null) continue;
            if (Vector2.Distance(new Vector2(me.x, me.z), new Vector2(kv.Value.transform.position.x, kv.Value.transform.position.z)) < LANDMARK_SEE + 200f) continue;
            if (drop == null) drop = new List<long>();
            drop.Add(kv.Key);
        }
        if (drop != null) foreach (var k in drop) { Destroy(landmarks[k]); landmarks.Remove(k); }
    }

    GameObject BuildLandmark(int kind, Vector3 pos, System.Random rng)
    {
        EnsureLookMats(); EnsureFarMats();
        var root = new GameObject("landmark_" + kind);
        root.transform.SetParent(farRoot.transform, false);
        root.transform.position = pos;
        var t = root.transform;
        switch (kind)
        {
            case 0:   // THE TOWER: three tapering drums to 150 m, a ring, a beacon
                Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 25f, 0f), new Vector3(22f, 25f, 22f), Quaternion.identity, matBody);
                Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 75f, 0f), new Vector3(14f, 30f, 14f), Quaternion.identity, matBodyLight);
                Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 128f, 0f), new Vector3(7f, 24f, 7f), Quaternion.identity, matBody);
                Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 104f, 0f), new Vector3(30f, 1.2f, 30f), Quaternion.identity, matCyan, false);
                for (int i = 0; i < 4; i++)
                    Prim(PrimitiveType.Cube, t, new Vector3(0f, 50f + i * 12f, 0f), new Vector3(18f, 0.6f, 18f), Quaternion.Euler(0f, i * 22f, 0f), matCyan, false);
                Prim(PrimitiveType.Sphere, t, new Vector3(0f, 156f, 0f), Vector3.one * 6f, Quaternion.identity, matAmber, false);
                break;
            case 1:   // THE GATE: a ring 90 m across standing on edge, sixteen segments
                {
                    float r = 45f; int segs = 16;
                    float yaw = (float)rng.NextDouble() * 180f;
                    for (int i = 0; i < segs; i++)
                    {
                        float a = i * Mathf.PI * 2f / segs;
                        var local = new Vector3(Mathf.Cos(a) * r, r + Mathf.Sin(a) * r - 4f, 0f);
                        Prim(PrimitiveType.Cube, t, Quaternion.Euler(0f, yaw, 0f) * local, new Vector3(r * 2f * Mathf.PI / segs * 1.05f, 6f, 5f),
                             Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg + 90f), i % 4 == 0 ? matCyan : matBodyLight, i % 4 != 0);
                    }
                    Prim(PrimitiveType.Cube, t, new Vector3(0f, 2f, 0f), new Vector3(r * 2.4f, 4f, 16f), Quaternion.Euler(0f, yaw, 0f), matBody);
                }
                break;
            case 2:   // THE HULL: a crashed ship, 140 m, half-buried, nose up
                {
                    float yaw = (float)rng.NextDouble() * 360f;
                    var rot = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(-70f, 0f, 0f);
                    Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 18f, 0f) + rot * new Vector3(0f, 30f, 0f), new Vector3(20f, 60f, 20f), rot, matHull);
                    Prim(PrimitiveType.Sphere, t, new Vector3(0f, 18f, 0f) + rot * new Vector3(0f, 90f, 0f), Vector3.one * 20f, rot, matHull);
                    Prim(PrimitiveType.Cube, t, new Vector3(0f, 18f, 0f) + rot * new Vector3(0f, 40f, -12f), new Vector3(36f, 30f, 2f), rot, matBodyLight);   // a fin
                    for (int i = 0; i < 5; i++)
                        Prim(PrimitiveType.Cube, t, new Vector3(0f, 18f, 0f) + rot * new Vector3(0f, 20f + i * 12f, 10.2f), new Vector3(3f, 6f, 0.4f), rot, matAmber, false);   // lit ports
                    for (int i = 0; i < 6; i++)   // debris field
                    {
                        float a = (float)rng.NextDouble() * 360f, d = 30f + (float)rng.NextDouble() * 50f;
                        var p = Quaternion.Euler(0f, a, 0f) * Vector3.forward * d;
                        p.y = TerrainHeight(pos.x + p.x, pos.z + p.z) - pos.y + 1.5f;
                        Prim(PrimitiveType.Cube, t, p, new Vector3(4f + (float)rng.NextDouble() * 6f, 3f, 4f + (float)rng.NextDouble() * 5f), Quaternion.Euler((float)rng.NextDouble() * 30f, a, (float)rng.NextDouble() * 30f), matHull);
                    }
                }
                break;
            default:  // THE SPIRES: five crystal blades to 110 m, lit at the tips
                for (int i = 0; i < 5; i++)
                {
                    float a = i * 72f + (float)rng.NextDouble() * 30f, d = i == 0 ? 0f : 18f + (float)rng.NextDouble() * 14f;
                    float h = i == 0 ? 110f : 55f + (float)rng.NextDouble() * 35f;
                    var p = Quaternion.Euler(0f, a, 0f) * Vector3.forward * d;
                    var rot = Quaternion.Euler((float)rng.NextDouble() * 10f - 5f, a, (float)rng.NextDouble() * 10f - 5f);
                    Prim(PrimitiveType.Cube, t, p + rot * new Vector3(0f, h * 0.5f, 0f), new Vector3(9f, h, 9f), rot * Quaternion.Euler(0f, 45f, 0f), matCrystal);
                    Prim(PrimitiveType.Sphere, t, p + rot * new Vector3(0f, h + 2f, 0f), Vector3.one * 4f, Quaternion.identity, matViolet, false);
                }
                break;
        }
        return root;
    }

    // ------------------------------------------------------------ roads
    public const float ROAD_HALF = 2.3f;
    /// <summary>The road segments touching cell (px,pz): to the place east and
    /// the place north, when both ends exist. Deterministic, no state.</summary>
    int RoadsOf(int px, int pz, Vector2[] a, Vector2[] b)
    {
        var p = PlaceInCell(px, pz);
        if (p == null) return 0;
        int n = 0;
        var e = PlaceInCell(px + 1, pz); if (e != null) { a[n] = new Vector2(p.centre.x, p.centre.z); b[n] = new Vector2(e.centre.x, e.centre.z); n++; }
        var no = PlaceInCell(px, pz + 1); if (no != null) { a[n] = new Vector2(p.centre.x, p.centre.z); b[n] = new Vector2(no.centre.x, no.centre.z); n++; }
        return n;
    }
    static float SegDist(Vector2 q, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a; float l2 = ab.sqrMagnitude;
        float tt = l2 < 1e-4f ? 0f : Mathf.Clamp01(Vector2.Dot(q - a, ab) / l2);
        return Vector2.Distance(q, a + ab * tt);
    }
    readonly Vector2[] roadA = new Vector2[2], roadB = new Vector2[2];
    /// <summary>0 off the road, 1 on its crown; the nearest road within 3 x 3 cells.</summary>
    public float RoadAt(float x, float z)
    {
        int cx = CellOf(x), cz = CellOf(z);
        float best = float.MaxValue;
        var q = new Vector2(x, z);
        for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int n = RoadsOf(cx + dx, cz + dz, roadA, roadB);
                for (int i = 0; i < n; i++) best = Mathf.Min(best, SegDist(q, roadA[i], roadB[i]));
            }
        return 1f - Mathf.Clamp01((best - ROAD_HALF) / 1.6f);
    }

    // ------------------------------------------------------------ life
    Material matRock, matScrap, matTuft, matCrystal, matObsidian, matVent, matHull, matPool;
    void EnsureFarMats()
    {
        if (matRock != null) return;
        matRock     = PartVisualFactory.Mat(new Color(0.38f, 0.30f, 0.26f), 0.05f, 0.25f);
        matScrap    = PartVisualFactory.Mat(new Color(0.42f, 0.24f, 0.14f), 0.55f, 0.30f);
        matTuft     = PartVisualFactory.Mat(new Color(0.36f, 0.62f, 0.40f), 0.0f, 0.35f);
        matCrystal  = PartVisualFactory.Emissive(new Color(0.58f, 0.42f, 0.95f), new Color(0.35f, 0.18f, 0.9f), 0.1f, 0.8f);
        matObsidian = PartVisualFactory.Mat(new Color(0.10f, 0.10f, 0.13f), 0.3f, 0.85f);
        matVent     = PartVisualFactory.Emissive(new Color(1f, 0.55f, 0.15f), new Color(2.2f, 0.9f, 0.2f), 0f, 0.5f);
        matHull     = PartVisualFactory.Mat(new Color(0.30f, 0.32f, 0.36f), 0.7f, 0.45f);
        matPool     = PartVisualFactory.Emissive(new Color(0.20f, 0.85f, 1f), new Color(0.3f, 1.6f, 2.0f), 0.2f, 0.9f);
    }

    /// <summary>A chunk's ground life by biome, kept off places, roads, home and
    /// the crate lane; static-batched into a handful of draws.</summary>
    void ScatterProps(Chunk ch, int cx, int cz, System.Random rng)
    {
        EnsureFarMats();
        float x0 = cx * CHUNK, z0 = cz * CHUNK;
        float b = Biome(x0 + CHUNK * 0.5f, z0 + CHUNK * 0.5f);
        // SPARSE. The first cut (14-25 a chunk, boulders to 3 m, 16 m off home)
        // read as clutter from the home pad - a field of brown blobs where the
        // eye wanted the ring gate on the skyline (live, 2026-09-10). Readable
        // worlds keep the mid-ground quiet; the props are texture, not scenery.
        int n = 7 + rng.Next(7);
        var made = new List<GameObject>();
        for (int i = 0; i < n; i++)
        {
            float x = x0 + 1f + (float)rng.NextDouble() * (CHUNK - 2f), z = z0 + 1f + (float)rng.NextDouble() * (CHUNK - 2f);
            if (Vector2.Distance(new Vector2(x, z), new Vector2(homePos.x, homePos.z)) < 34f) continue;
            if (InsidePlace(x, z, 3f)) continue;
            if (RoadAt(x, z) > 0f) continue;
            float gy = TerrainHeight(x, z);
            float yaw = (float)rng.NextDouble() * 360f;
            GameObject g;
            if (b < 0.42f)
            {
                if (rng.Next(3) == 0)   // a scrap pile: three rusty slabs
                {
                    var pile = new GameObject("scatter_scrap"); pile.transform.SetParent(ch.root.transform, false); pile.transform.position = new Vector3(x, gy, z);
                    for (int k = 0; k < 3; k++)
                        Prim(PrimitiveType.Cube, pile.transform, new Vector3((float)rng.NextDouble() * 1.4f - 0.7f, 0.25f + k * 0.35f, (float)rng.NextDouble() * 1.4f - 0.7f),
                             new Vector3(0.9f + (float)rng.NextDouble() * 0.7f, 0.22f, 0.6f + (float)rng.NextDouble() * 0.6f), Quaternion.Euler((float)rng.NextDouble() * 16f, yaw + k * 40f, (float)rng.NextDouble() * 12f), matScrap);
                    g = pile;
                }
                else                    // a boulder
                {
                    float s = 0.6f + (float)rng.NextDouble() * 1.3f;
                    g = Prim(PrimitiveType.Sphere, ch.root.transform, new Vector3(x, gy + s * 0.2f, z), new Vector3(s, s * 0.55f, s * (0.7f + (float)rng.NextDouble() * 0.5f)), Quaternion.Euler(0f, yaw, 0f), matRock);
                    g.name = "scatter_rock";
                }
            }
            else if (b < 0.66f)
            {
                if (rng.Next(4) == 0)   // a crystal shard
                {
                    float h = 1.2f + (float)rng.NextDouble() * 2.4f;
                    g = Prim(PrimitiveType.Cube, ch.root.transform, new Vector3(x, gy + h * 0.45f, z), new Vector3(0.5f, h, 0.5f), Quaternion.Euler((float)rng.NextDouble() * 18f - 9f, yaw, (float)rng.NextDouble() * 18f - 9f) * Quaternion.Euler(0f, 45f, 0f), matCrystal, false);
                    g.name = "scatter_crystal";
                }
                else                    // a tuft: five thin blades
                {
                    var tuft = new GameObject("scatter_tuft"); tuft.transform.SetParent(ch.root.transform, false); tuft.transform.position = new Vector3(x, gy, z);
                    for (int k = 0; k < 5; k++)
                        Prim(PrimitiveType.Cylinder, tuft.transform, new Vector3((float)rng.NextDouble() * 0.8f - 0.4f, 0.55f, (float)rng.NextDouble() * 0.8f - 0.4f),
                             new Vector3(0.08f, 0.55f + (float)rng.NextDouble() * 0.5f, 0.08f), Quaternion.Euler((float)rng.NextDouble() * 30f - 15f, k * 72f, (float)rng.NextDouble() * 30f - 15f), matTuft, false);
                    g = tuft;
                }
            }
            else
            {
                if (rng.Next(4) == 0)   // a vent: a low dome with an ember
                {
                    var vent = new GameObject("scatter_vent"); vent.transform.SetParent(ch.root.transform, false); vent.transform.position = new Vector3(x, gy, z);
                    Prim(PrimitiveType.Sphere, vent.transform, new Vector3(0f, -0.4f, 0f), new Vector3(2.4f, 1.6f, 2.4f), Quaternion.identity, matObsidian);
                    Prim(PrimitiveType.Sphere, vent.transform, new Vector3(0f, 0.45f, 0f), Vector3.one * 0.5f, Quaternion.identity, matVent, false);
                    g = vent;
                }
                else                    // an obsidian shard
                {
                    float h = 1.5f + (float)rng.NextDouble() * 3.5f;
                    g = Prim(PrimitiveType.Cube, ch.root.transform, new Vector3(x, gy + h * 0.35f, z), new Vector3(0.7f + (float)rng.NextDouble() * 0.6f, h, 0.5f), Quaternion.Euler((float)rng.NextDouble() * 24f - 12f, yaw, (float)rng.NextDouble() * 24f - 12f), matObsidian);
                    g.name = "scatter_obsidian";
                }
            }
            made.Add(g);
        }
        ch.scatter = made.Count;
        if (made.Count > 0) StaticBatchingUtility.Combine(made.ToArray(), ch.root);
    }

    // ------------------------------------------------------------ pools
    /// <summary>The crater of a 160 m noise-space cell, if any (half the cells).
    /// Shared with RawHeight so the pool sits exactly in the bowl it belongs to.</summary>
    public bool CraterInCell(int gx, int gz, out Vector2 centreWorld, out float rad, out float depth)
    {
        float cell = 160f;
        float hsh = Mathf.Abs(Mathf.Sin(gx * 127.1f + gz * 311.7f) * 43758.5453f);
        float f = hsh - Mathf.Floor(hsh);
        centreWorld = Vector2.zero; rad = 0f; depth = 0f;
        if (f > 0.5f) return false;
        rad = 12f + f * 34f;
        depth = rad * 0.16f;
        float ox = (gx + 0.2f + f * 0.6f) * cell, oz = (gz + 0.25f + (f * 7.3f - Mathf.Floor(f * 7.3f)) * 0.5f) * cell;
        centreWorld = new Vector2(ox - noiseOx, oz - noiseOz);
        return true;
    }
    public const float POOL_MIN_RAD = 26f;
    void SpawnPools(Chunk ch, int cx, int cz)
    {
        EnsureFarMats();
        float x0 = cx * CHUNK, z0 = cz * CHUNK;
        int gx0 = Mathf.FloorToInt((x0 + noiseOx) / 160f), gz0 = Mathf.FloorToInt((z0 + noiseOz) / 160f);
        for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                Vector2 c; float rad, depth;
                if (!CraterInCell(gx0 + dx, gz0 + dz, out c, out rad, out depth)) continue;
                if (rad < POOL_MIN_RAD) continue;
                if (c.x < x0 || c.x >= x0 + CHUNK || c.y < z0 || c.y >= z0 + CHUNK) continue;
                if (HomeFlat(c.x, c.y) < 0.99f) continue;   // the home flat erases its craters
                float floor = TerrainHeight(c.x, c.y);
                float level = floor + depth * 0.45f;
                var pool = Prim(PrimitiveType.Cylinder, ch.root.transform, new Vector3(c.x, level, c.y), new Vector3(rad * 1.3f, 0.04f, rad * 1.3f), Quaternion.identity, matPool, false);
                pool.name = "pool";
                ch.pools++;
            }
    }
    /// <summary>Bench seam: the nearest pool-sized crater to a point, scanning 13 x 13 cells.</summary>
    public bool NearestPool(Vector3 from, out Vector2 centre)
    {
        centre = Vector2.zero; float best = float.MaxValue;
        int gx0 = Mathf.FloorToInt((from.x + noiseOx) / 160f), gz0 = Mathf.FloorToInt((from.z + noiseOz) / 160f);
        for (int dz = -6; dz <= 6; dz++)
            for (int dx = -6; dx <= 6; dx++)
            {
                Vector2 c; float rad, depth;
                if (!CraterInCell(gx0 + dx, gz0 + dz, out c, out rad, out depth) || rad < POOL_MIN_RAD) continue;
                if (HomeFlat(c.x, c.y) < 0.99f) continue;
                float d = Vector2.Distance(new Vector2(from.x, from.z), c);
                if (d < best) { best = d; centre = c; }
            }
        return best < float.MaxValue;
    }
    public int YardPoolsLoaded { get { int n = 0; foreach (var c in chunks.Values) n += c.pools; return n; } }
    public int YardScatterLoaded { get { int n = 0; foreach (var c in chunks.Values) n += c.scatter; return n; } }
}
}
