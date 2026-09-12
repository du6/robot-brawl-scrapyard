// ===========================================================================
// BuilderManager.Map.cs — THE WORLD (Robot Brawl: Scrapyard,
// docs/Scrapyard_Design_2026-09-09.md §3).
//
// owen, 2026-09-10: "The map is not an arena. It should be a global map like
// real world. There is no leagues/program. It is a brand new experience like
// minecraft, where users explore the world and collect parts and fight
// enemies."
//
// So: an OPEN WORLD, generated as you drive. Terrain is chunked and made from
// layered noise on a per-player seed (code, not data — the web budget), with
// biomes by region, wrecks and ruins scattered from each chunk's own seed,
// crates to drive into and enemy robots parked by their wrecks. Chunks load
// on approach and unload behind you. There is no fence; there is no end.
//
// Grown out of StartTest(): the player's robot under the touch stick with a
// FollowCamera is the drive loop, and this is a partial of BuilderManager
// because `placed`, `driveDir`, `cam`, SpawnBot and BackToBuild's sweep are
// its private state. The GARAGE button opens the workshop wherever you are.
//
// Still owed after this pass (said plainly): a challenge cuts to the
// standard ring for the bout; fighting IN PLACE, where you met the enemy,
// with no walls, is the next pass. Enemies idle; roaming is after that.
// ===========================================================================
using System.Collections.Generic;
using UnityEngine;

namespace RobotBrawl.Phase0
{
public partial class BuilderManager
{
    // ------------------------------------------------------------ tunables
    public const float CHUNK = 48f;          // metres per chunk side
    public const int   CHUNK_RES = 24;       // quads per side (2 m) -> 625 verts, 1152 tris
    public const int   VIEW_CHUNKS = 2;      // load radius: (2*2+1)^2 = 25 chunks around you
    public const float CRATE_REACH = 1.6f;   // drive into it
    public const float CARD_REACH = 4f;      // the encounter card slides up
    public const string YARD_BOT = "scout";  // the spawn chunk's guaranteed first enemy
    public const float SPAWN_FLAT = 22f;     // the world is flat this far from home
    // THE MAP'S STEERING lives in BuilderManager.Drive.cs (point where you
    // want to go, camera-relative, 2026-09-10). This file only caps speed:
    // MEASURED (MapBench trace, flat ground, zero steer): the rookie veers by
    // itself, and the veer grows with speed - 1 deg at 2 m/s, 12 deg at
    // 10 m/s over two seconds. The build is not symmetrical, and a 14 m ring
    // never lets it reach the speed where that shows.
    public const float MAP_MAX_SPEED = 6f;

    /// <summary>THE MAP IS THE FRONT DOOR. A fresh BuilderManager drives out on
    /// its first frames; GARAGE is the door back. Benches set this false
    /// before creating one; MapBench sets it true to prove it.</summary>
    public static bool bootToYard = true;
    bool bootedToYard;

    // ------------------------------------------------------------ state
    class Chunk
    {
        public int cx, cz;
        public GameObject root;
        public readonly List<GameObject> crates = new List<GameObject>();
        public readonly List<string> crateKeys = new List<string>();
        public CompoundRobot enemy; public string enemyId = "";
        public readonly List<CompoundRobot> extraBots = new List<CompoundRobot>();   // a place's machines (Places)
        public readonly List<Vector3> shopPads = new List<Vector3>();
        public ArenaShow arena;
        public int scatter, pools, scatterDraws;                                // WorldFar
        public CompoundRobot poolBot; public LadderClient.PoolEntry poolEntry;  // Pool: another player's machine
    }
    readonly Dictionary<long, Chunk> chunks = new Dictionary<long, Chunk>();
    GameObject worldRoot;
    int worldSeed;
    int resumeSeed, placeSeed; float lastMapYaw; bool padArmed;
    float noiseOx, noiseOz;                  // seed-derived offsets into the noise field
    Vector3 homePos = Vector3.zero;          // where you spawn; the compass calls it HOME
    CompoundRobot cardBot; string cardBotId = "";
    bool yardCard;
    string yardToast = ""; float yardToastT;
    float flippedFor;
    Material matRust, matSteppe, matAsh, matWreck, matPillar;

    static long Key(int cx, int cz) { return ((long)cx << 32) ^ (uint)cz; }
    static int ChunkOf(float v) { return Mathf.FloorToInt(v / CHUNK); }
    static string CrateKey(int cx, int cz, int i) { return cx + "," + cz + ":" + i; }

    // ------------------------------------------------------------ seams (benches)
    public bool YardCardShown { get { return mode == Mode.Map && yardCard; } }
    public int  YardCratesLeft { get { int n = 0; foreach (var c in chunks.Values) foreach (var g in c.crates) if (g != null) n++; return n; } }
    public CompoundRobot YardParked { get { return cardBot != null ? cardBot : NearestEnemy(testRobot != null ? testRobot.rb.position : homePos); } }
    public Vector3 YardGarageDoor { get { return homePos; } }
    public int WorldChunksLoaded { get { return chunks.Count; } }
    public int WorldSeedNow { get { return worldSeed; } }
    public Vector3 TestDriveDir { get { return driveDir; } }   // the build's drive axis, for a bench's heading check
    public List<Vector3> YardCratePositions()
    {
        var l = new List<Vector3>();
        foreach (var c in chunks.Values) foreach (var g in c.crates) if (g != null) l.Add(g.transform.position);
        l.Sort((a, b) => (a - homePos).sqrMagnitude.CompareTo((b - homePos).sqrMagnitude));
        return l;
    }

    // ------------------------------------------------------------ the terrain
    /// <summary>Ground height at a world position: three octaves of noise on
    /// the player's seed, flattened around home so the first minute is a
    /// drive and not a climb. Deterministic: the same seed is the same world.</summary>
    public float TerrainHeight(float x, float z)
    {
        float h = RawHeight(x, z) * HomeFlat(x, z);
        return FlattenForPlaces(x, z, h);
    }
    /// <summary>The world is flat this far from home (0 at home, 1 beyond SPAWN_FLAT+30).</summary>
    float HomeFlat(float x, float z)
    {
        float dHome = Vector2.Distance(new Vector2(x, z), new Vector2(homePos.x, homePos.z));
        return Mathf.Clamp01((dHome - SPAWN_FLAT) / 30f);
    }
    /// <summary>The land before home and the places flatten it.</summary>
    float RawHeight(float x, float z)
    {
        float nx = x + noiseOx, nz = z + noiseOz;
        float h = 6.0f * Mathf.PerlinNoise(nx / 220f, nz / 220f)
                + 2.5f * Mathf.PerlinNoise(nx / 60f + 3.7f, nz / 60f + 1.3f)
                + 0.8f * Mathf.PerlinNoise(nx / 17f + 9.1f, nz / 17f + 4.2f);
        h -= 4.6f;   // roughly zero-mean
        // MESAS: where a slow noise runs high the ground steps up onto a
        // plateau, with an edge you can drive (4 m over ~14 m).
        float mesa = Mathf.PerlinNoise(nx / 150f + 31f, nz / 150f + 17f);
        h += Mathf.SmoothStep(0f, 1f, (mesa - 0.62f) / 0.10f) * 4.2f;
        // CRATERS: one per 160 m cell, half the time, a bowl with a rim
        // (CraterInCell in WorldFar is the one definition; the pools use it).
        float cell = 160f;
        int cx = Mathf.FloorToInt(nx / cell), cz = Mathf.FloorToInt(nz / cell);
        for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                Vector2 cw; float rad, depth;
                if (!CraterInCell(cx + dx, cz + dz, out cw, out rad, out depth)) continue;
                float d = Vector2.Distance(new Vector2(x, z), cw) / rad;
                if (d < 1.15f)
                {
                    if (d < 1f) h -= depth * (1f - d * d);
                    float rim = (d - 0.98f) / 0.10f;
                    h += depth * 0.35f * Mathf.Exp(-rim * rim);
                }
            }
        return h;
    }
    /// <summary>0..1: rust flats, scrap steppe, ash fields.</summary>
    float Biome(float x, float z) { return Mathf.PerlinNoise((x + noiseOx) / 300f + 11f, (z + noiseOz) / 300f + 5f); }
    Material BiomeMat(float b) { return b < 0.42f ? matRust : b < 0.66f ? matSteppe : matAsh; }

    void EnsureWorldMats()
    {
        if (matRust != null) return;
        matRust   = PartVisualFactory.Mat(new Color(0.44f, 0.30f, 0.20f), 0.05f, 0.20f);
        matSteppe = PartVisualFactory.Mat(new Color(0.33f, 0.36f, 0.28f), 0.05f, 0.22f);
        matAsh    = PartVisualFactory.Mat(new Color(0.24f, 0.24f, 0.26f), 0.05f, 0.25f);
        matWreck  = PartVisualFactory.Mat(new Color(0.36f, 0.30f, 0.24f), 0.6f, 0.3f);
        matPillar = PartVisualFactory.Mat(new Color(0.45f, 0.42f, 0.38f), 0.6f, 0.3f);
    }

    Chunk BuildChunk(int cx, int cz)
    {
        var ch = new Chunk { cx = cx, cz = cz };
        ch.root = new GameObject("chunk_" + cx + "_" + cz);
        ch.root.transform.SetParent(worldRoot.transform, false);
        float x0 = cx * CHUNK, z0 = cz * CHUNK;

        // the ground: a mesh with a collider, coloured by the biome at its centre
        int n = CHUNK_RES + 1;
        var verts = new Vector3[n * n]; var uvs = new Vector2[n * n];
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                float x = x0 + i * (CHUNK / CHUNK_RES), z = z0 + j * (CHUNK / CHUNK_RES);
                verts[j * n + i] = new Vector3(x, TerrainHeight(x, z), z);
                uvs[j * n + i] = new Vector2(i / (float)CHUNK_RES, j / (float)CHUNK_RES);
            }
        var tris = new int[CHUNK_RES * CHUNK_RES * 6]; int t = 0;
        for (int j = 0; j < CHUNK_RES; j++)
            for (int i = 0; i < CHUNK_RES; i++)
            {
                int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
                tris[t++] = a; tris[t++] = c; tris[t++] = b;
                tris[t++] = b; tris[t++] = c; tris[t++] = d;
            }
        var mesh = new Mesh { name = "ground_" + cx + "_" + cz };
        mesh.vertices = verts; mesh.uv = uvs; mesh.triangles = tris;
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        // colour per vertex: biome, height band, slope, the home plaza
        var norms = mesh.normals;
        var cols = new Color[verts.Length];
        for (int k = 0; k < verts.Length; k++) cols[k] = GroundColor(verts[k].x, verts[k].z, verts[k].y, norms[k]);
        mesh.colors = cols;
        var ground = new GameObject("ground");
        ground.transform.SetParent(ch.root.transform, false);
        ground.AddComponent<MeshFilter>().sharedMesh = mesh;
        ground.AddComponent<MeshRenderer>().sharedMaterial = matGround != null ? matGround : BiomeMat(Biome(x0 + CHUNK * 0.5f, z0 + CHUNK * 0.5f));
        ground.AddComponent<MeshCollider>().sharedMesh = mesh;

        // this chunk's own seed: the world's, mixed with where it is
        var rng = new System.Random(unchecked(worldSeed * 73856093 ^ cx * 19349663 ^ cz * 83492791));
        bool spawnChunk = cx == ChunkOf(homePos.x) && cz == ChunkOf(homePos.z);
        float dHome = Vector2.Distance(new Vector2(x0 + CHUNK * 0.5f, z0 + CHUNK * 0.5f), new Vector2(homePos.x, homePos.z));

        // structures: the planet's own architecture (WorldLook), fewer and
        // larger than the old wrecks, never within 12 m of home
        EnsureLookMats();
        if (spawnChunk) SpawnHomePlaza(ch.root.transform, rng);
        int structures = 3 + rng.Next(4);
        for (int w = 0; w < structures; w++)
        {
            float x = x0 + 4f + (float)rng.NextDouble() * (CHUNK - 8f), z = z0 + 4f + (float)rng.NextDouble() * (CHUNK - 8f);
            if (Vector2.Distance(new Vector2(x, z), new Vector2(homePos.x, homePos.z)) < 13f) continue;
            if (InsidePlace(x, z, 5f)) continue;
            int kind = rng.Next(6);
            SpawnStructure(ch.root.transform, kind, x, z, TerrainHeight(x, z), rng);
        }

        // a stranger's machine, sometimes, out past the first minute (Pool)
        MaybeSpawnPoolBot(ch, cx, cz, dHome, rng);

        // places: any whose centre lies in this chunk (Places)
        BuildPlacesInChunk(ch, cx, cz, rng);
        // life and pools (WorldFar)
        ScatterProps(ch, cx, cz, rng);
        SpawnPools(ch, cx, cz);

        // crates: the spawn chunk's first is 8 m from home, in view; others by chance
        int crates = spawnChunk ? 2 : rng.Next(3);
        for (int i = 0; i < crates; i++)
        {
            float x, z;
            if (spawnChunk && i == 0) { x = homePos.x; z = homePos.z + 8f; }
            else { x = x0 + 3f + (float)rng.NextDouble() * (CHUNK - 6f); z = z0 + 3f + (float)rng.NextDouble() * (CHUNK - 6f); }
            string key = CrateKey(cx, cz, i);
            ch.crateKeys.Add(key);
            if (Career.Data != null && Career.Data.worldOpened.Contains(key)) { ch.crates.Add(null); continue; }
            ch.crates.Add(MakePod(ch.root.transform, x, TerrainHeight(x, z), z, key));
        }

        // an enemy: the spawn chunk always parks SCOUT 30 m out; elsewhere by
        // chance, tougher the further from home
        bool wantEnemy = spawnChunk || rng.Next(100) < 55;
        if (wantEnemy)
        {
            string id;
            float ex, ez;
            if (spawnChunk) { id = YARD_BOT; ex = homePos.x + 6f; ez = homePos.z + 30f; }
            else
            {
                string[] rookies = { "scout", "tipper" }, veterans = { "mauler", "ripper", "millstone" }, champs = { "bulwark", "widowmaker", "bastion" };
                var pool = dHome < 120f ? rookies : dHome < 300f ? veterans : champs;
                id = pool[rng.Next(pool.Length)];
                ex = x0 + 6f + (float)rng.NextDouble() * (CHUNK - 12f); ez = z0 + 6f + (float)rng.NextDouble() * (CHUNK - 12f);
                if (InsidePlace(ex, ez, 3f)) wantEnemy = false;
            }
            var entry = wantEnemy ? EnemyRoster.Find(id) : null;
            if (entry != null)
            {
                RaycastWheelDrive edrv;
                var bot = SpawnBot(EnemyRoster.Recipe(entry.id, palette), entry.label, new Vector3(ex, 0f, ez),
                                   Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), Vector3.forward, out edrv);
                if (bot != null)
                {
                    bot.combatEnabled = false;
                    bot.controlSource = ControlSource.AI;      // no controller: it idles by its wreck
                    LiftToGround(bot, TerrainHeight(ex, ez) + 0.6f);
                    ParkBot(bot);
                    ch.enemy = bot; ch.enemyId = entry.id;
                }
            }
        }
        return ch;
    }

    GameObject MakeCrate(Transform parent, float x, float gy, float z, string key)
    {
        var crate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        crate.name = "crate " + key;
        crate.transform.SetParent(parent, false);
        crate.transform.position = new Vector3(x, gy + 0.35f, z);
        crate.transform.localScale = new Vector3(0.7f, 0.7f, 0.7f);
        crate.transform.rotation = Quaternion.Euler(0f, 30f, 0f);
        crate.GetComponent<Renderer>().sharedMaterial = PartVisualFactory.HazardYellow;
        Object.Destroy(crate.GetComponent<Collider>());     // you drive INTO it
        var glow = PartVisualFactory.Deco(PrimitiveType.Cylinder, crate.transform, new Vector3(0f, 2.2f, 0f),
            new Vector3(0.12f, 1.8f, 0.12f), Vector3.zero, PartVisualFactory.CyanGlow, "beacon");
        var gc = glow.GetComponent<Collider>(); if (gc != null) Object.Destroy(gc);
        return crate;
    }

    /// <summary>Move the player's whole machine (every part's rigidbody, not
    /// just the core) and drop it on the ground there. A bench's teleport
    /// through rb.position alone tore the robot in half.</summary>
    public void TeleportPlayer(Vector3 xz)
    {
        if (mode != Mode.Map || testRobot == null) return;
        var t = testRobot.transform;
        Vector3 to = new Vector3(xz.x, TerrainHeight(xz.x, xz.z) + 0.6f, xz.z);
        Vector3 d = to - testRobot.rb.position;
        t.position += d;
        foreach (var rb in testRobot.GetComponentsInChildren<Rigidbody>())
        { rb.position += Vector3.zero; rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        Physics.SyncTransforms();
        LoadAround(to);
    }
    /// <summary>...and facing a world heading (deg, 0 = +z), for a bench that
    /// needs a known run-out.</summary>
    public void TeleportPlayer(Vector3 xz, float faceYaw)
    {
        TeleportPlayer(xz);
        if (mode != Mode.Map || testRobot == null) return;
        var t = testRobot.transform;
        float have = HeadingYaw(t.TransformDirection(driveDir));
        t.RotateAround(t.position, Vector3.up, Mathf.DeltaAngle(have, faceYaw));
        foreach (var rb in testRobot.GetComponentsInChildren<Rigidbody>()) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        Physics.SyncTransforms();
    }

    /// <summary>A parked machine stays parked. Seen live (2026-09-10): the
    /// SCOUT at 30 m sat where the home flat begins to roll and crept 10 m
    /// downhill in ten seconds, the compass counting it away. Heavy damping on
    /// every body; a challenge spawns fresh machines, so nothing is lost.</summary>
    static void ParkBot(CompoundRobot bot)
    {
        foreach (var rb in bot.GetComponentsInChildren<Rigidbody>()) { rb.linearDamping = 6f; rb.angularDamping = 6f; }
    }

    static void LiftToGround(CompoundRobot bot, float y)
    {
        var t = bot.transform;
        Vector3 d = new Vector3(0f, y - t.position.y, 0f);
        t.position += d;
        foreach (var rb in bot.GetComponentsInChildren<Rigidbody>())
        { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
    }

    /// <summary>Load what is near, drop what is far. One new chunk per call,
    /// nearest first, so a fast drive never hitches for 25 meshes at once.</summary>
    void PumpChunks(Vector3 at)
    {
        int pcx = ChunkOf(at.x), pcz = ChunkOf(at.z);
        int bestCx = 0, bestCz = 0, bestD = int.MaxValue; bool want = false;
        for (int dz = -VIEW_CHUNKS; dz <= VIEW_CHUNKS; dz++)
            for (int dx = -VIEW_CHUNKS; dx <= VIEW_CHUNKS; dx++)
            {
                if (chunks.ContainsKey(Key(pcx + dx, pcz + dz))) continue;
                int dd = dx * dx + dz * dz;
                if (dd < bestD) { bestD = dd; bestCx = pcx + dx; bestCz = pcz + dz; want = true; }
            }
        if (want) chunks[Key(bestCx, bestCz)] = BuildChunk(bestCx, bestCz);
        // unload beyond the view + 1 ring
        List<long> drop = null;
        foreach (var kv in chunks)
        {
            var c = kv.Value;
            if (Mathf.Abs(c.cx - pcx) <= VIEW_CHUNKS + 1 && Mathf.Abs(c.cz - pcz) <= VIEW_CHUNKS + 1) continue;
            if (cardBot != null && (c.enemy == cardBot || c.poolBot == cardBot)) continue;     // never under a live card
            if (drop == null) drop = new List<long>();
            drop.Add(kv.Key);
        }
        if (drop != null) foreach (var k in drop) UnloadChunk(k);
    }

    void UnloadChunk(long k)
    {
        Chunk c;
        if (!chunks.TryGetValue(k, out c)) return;
        chunks.Remove(k);
        if (c.enemy != null && c.enemy.gameObject != null) { c.enemy.gameObject.SetActive(false); Destroy(c.enemy.gameObject); }
        if (c.poolBot != null && c.poolBot.gameObject != null) { c.poolBot.gameObject.SetActive(false); Destroy(c.poolBot.gameObject); }
        foreach (var b in c.extraBots) if (b != null && b.gameObject != null) { b.gameObject.SetActive(false); Destroy(b.gameObject); }
        foreach (var pad in c.shopPads) shopPads.Remove(pad);
        if (c.root != null) Destroy(c.root);
    }

    /// <summary>All chunks around a point, synchronously (the boot, and a bench).</summary>
    void LoadAround(Vector3 at)
    {
        for (int i = 0; i < (2 * VIEW_CHUNKS + 1) * (2 * VIEW_CHUNKS + 1); i++) PumpChunks(at);
    }

    // ------------------------------------------------------------ the doors
    /// <summary>DRIVE OUT / the boot. Legal build required, like every fight.</summary>
    public void EnterMap()
    {
        if (mode == Mode.Fight) return;
        if (mode == Mode.Test) BackToBuild();
        if (mode == Mode.Map) return;
        string err = Validate();
        if (err != null) { message = err; SfxSynth.Deny(); return; }

        // the world's seed lives in the save: your world is yours, and it persists
        if (Career.Data != null)
        {
            if (Career.Data.worldSeed == 0)
            {
                Career.Data.worldSeed = new System.Random().Next(1, int.MaxValue);
                if (Career.autosave) Career.Save();
            }
            worldSeed = Career.Data.worldSeed;
        }
        else worldSeed = 12345;
        var srng = new System.Random(worldSeed);
        noiseOx = (float)srng.NextDouble() * 4000f; noiseOz = (float)srng.NextDouble() * 4000f;
        // HOME IS A CHUNK CENTRE, NOT A CORNER. At (0,0) a hair of drift put
        // the player in chunk -1 and the load window loaded two extra chunks
        // before the far ones dropped (MapBench: "27 chunks of 25").
        homePos = new Vector3(CHUNK * 0.5f, 0f, CHUNK * 0.5f);
        if (placeSeed != worldSeed) { placeCache.Clear(); placeSeed = worldSeed; }   // the same world keeps its places
        shopPads.Clear(); LastShopOpened = false;
        // GARAGE and the shop bring you back where you left, not home
        Vector3 spawnAt = hasResume && resumeSeed == worldSeed ? lastMapPos : homePos;
        Quaternion spawnRot = hasResume && resumeSeed == worldSeed ? Quaternion.Euler(0f, lastMapYaw, 0f) : Quaternion.identity;
        padArmed = false;

        ARENA_HALF = 100000f;                // no fence: FloorNet is a fall net only
        ArenaHazards.Clear();
        TouchControls.Ensure();
        TouchControls.fightActive = true;    // the stick is up; no FIRE on the map
        TouchControls.hasFire = false;

        Deselect();
        SetMatView(false);
        hoverPart = null;
        buildRoot.SetActive(false);
        mode = Mode.Map;
        message = "";
        yardCard = false; cardBot = null; cardBotId = ""; yardToast = ""; yardToastT = 0f; flippedFor = 0f;

        EnsureWorldMats();
        EnsureLookMats();
        worldRoot = new GameObject("world");
        sandboxRoot = worldRoot;             // BackToBuild's sweep destroys it
        SetupPlanetLook();
        EnsureFar(spawnAt);                  // the horizon and the landmarks (WorldFar)
        // home: a glowing pad you can find again
        var pad = PartVisualFactory.Deco(PrimitiveType.Cylinder, worldRoot.transform, homePos + new Vector3(0f, 0.02f, 0f),
            new Vector3(3.5f, 0.02f, 3.5f), Vector3.zero, PartVisualFactory.CyanGlow, "home_pad");
        var pc = pad.GetComponent<Collider>(); if (pc != null) Object.Destroy(pc);
        LoadAround(spawnAt);

        testRobot = SpawnBot(placed, "PlayerBuild", spawnAt, spawnRot, driveDir, out testDrive);
        if (testRobot == null) { BackToBuild(); message = "the build would not spawn"; return; }
        testRobot.combatEnabled = false;     // nothing on the map fights until you challenge
        testRobot.controlSource = ControlSource.AI;   // the map feeds aiThrottle/aiSteer itself (see STEER_*)
        if (testDrive != null) { testDrive.aiThrottle = 0f; testDrive.aiSteer = 0f; testDrive.maxSpeed = MAP_MAX_SPEED; }
        foreach (var act in testRobot.GetComponentsInChildren<Actuator>(true)) act.playerControlled = false;
        combatArmAt = -1f;
        LiftToGround(testRobot, TerrainHeight(spawnAt.x, spawnAt.z) + 0.8f);

        followCam = cam.gameObject.AddComponent<FollowCamera>();
        followCam.target = EnsureCamPivot(testRobot, driveDir);   // lagged heading, not the nose (Drive.cs)
        followCam.forwardHint = Vector3.forward;
        followCam.distance = 8.5f; followCam.height = 3.4f;   // lower: the horizon in the frame
        followCam.clampHalf = 0f;            // no clamp: the world has no edge
        followCam.SnapNow();
        AddHeadlight(testRobot, driveDir);

        MapHudUI.Ensure(this);
        FetchPoolOnce();
        FlushPendingToast();
        RBTelemetry.Once(RBTelemetry.MAP);
    }

    /// <summary>GARAGE. The world comes down, then BackToBuild's sweep takes
    /// every CompoundRobot with it.</summary>
    public void LeaveMap()
    {
        if (mode != Mode.Map) return;
        if (testRobot != null) { lastMapPos = testRobot.rb.position; lastMapYaw = testRobot.transform.eulerAngles.y; resumeSeed = worldSeed; hasResume = true; }
        TeardownWorld();
        BackToBuild();
    }

    // ---- FIGHT WHERE YOU STAND (CrazyGames plan, step 4, 2026-09-10). The
    // ring's code assumes the origin - FloorNet, the crush walls, the camera
    // clamp, BuildArena - so the WORLD moves to the fight: the world root
    // shifts so the encounter is at (0,0,0) with the highest ground under the
    // ring at y=0, the planet stays as the backdrop (sky, fog, horizon,
    // landmarks), the map's machines go, and the ring rises as a pad on the
    // terrain. After the bell BackToBuild tears the world down; CONTINUE
    // EXPLORING rebuilds it where you stood.
    Vector3 worldShift; bool fightInWorld;
    public Vector3 WorldShiftNow { get { return worldShift; } }
    public bool FightInWorld { get { return fightInWorld; } }
    void LeaveMapForFight()
    {
        if (mode != Mode.Map) return;
        Vector3 at = testRobot != null ? testRobot.rb.position : homePos;
        lastMapPos = at; lastMapYaw = testRobot != null ? testRobot.transform.eulerAngles.y : 0f; resumeSeed = worldSeed; hasResume = true;
        MapHudUI.Drop(); DropCamPivot();
        // the pad sits on the highest ground inside the ring
        float pad = TerrainHeight(at.x, at.z);
        for (int i = 0; i < 16; i++) { float a = i * Mathf.PI / 8f; pad = Mathf.Max(pad, TerrainHeight(at.x + Mathf.Cos(a) * (ARENA_FIGHT_HALF + 0.5f), at.z + Mathf.Sin(a) * (ARENA_FIGHT_HALF + 0.5f))); }
        // ...and a hair ABOVE it. owen, 2026-09-11: "the arena ground looks
        // blurry" - on the home flat the pad and the terrain were coplanar
        // (both y=0) and z-fought, which reads as a shimmering floor.
        worldShift = new Vector3(-at.x, -(pad + PAD_LIFT), -at.z);
        if (worldRoot != null) worldRoot.transform.position += worldShift;
        // the map's machines are not under the world root: they go now (the
        // fight spawns its own pair, and the world is rebuilt after the bell)
        foreach (var c in chunks.Values)
        {
            if (c.enemy != null && c.enemy.gameObject != null) { c.enemy.gameObject.SetActive(false); Destroy(c.enemy.gameObject); }
            if (c.poolBot != null && c.poolBot.gameObject != null) { c.poolBot.gameObject.SetActive(false); Destroy(c.poolBot.gameObject); }
            foreach (var b in c.extraBots) if (b != null && b.gameObject != null) { b.gameObject.SetActive(false); Destroy(b.gameObject); }
            c.enemy = null; c.poolBot = null; c.extraBots.Clear();
            if (c.arena != null) { c.arena.a = null; c.arena.b = null; }
        }
        cardBot = null; cardBotId = ""; yardCard = false;
        if (followCam != null) { followCam.enabled = false; Destroy(followCam); followCam = null; }
        if (testRobot != null) { testRobot.gameObject.SetActive(false); Destroy(testRobot.gameObject); testRobot = null; testDrive = null; }
        sandboxRoot = null;      // the ring gets its own sandbox; the world is not swept with it
        fightInWorld = true;
        mode = Mode.Build;       // StartFight's precondition; it hides the build root itself
        ARENA_HALF = ARENA_FIGHT_HALF;
    }
    public const float ARENA_FIGHT_HALF = 7f;
    public const float PAD_LIFT = 0.18f;   // the ring's floor stands this far above the highest ground inside it

    /// <summary>The world comes down: the root (chunks, far mesh, landmarks,
    /// marker) and every reference into it; the garage's look returns.</summary>
    void TeardownWorld()
    {
        MapHudUI.Drop();
        DropCamPivot();
        objectiveMarker = null;
        farRoot = null; farGround = null; landmarks.Clear();
        chunks.Clear();
        cardBot = null; cardBotId = ""; yardCard = false;
        if (worldRoot != null) { Destroy(worldRoot); }
        worldRoot = null;
        RestoreLook();
        ARENA_HALF = 7f;
        fightInWorld = false; worldShift = Vector3.zero;
    }

    /// <summary>Called from Update while in Build mode: the one-shot boot.</summary>
    void PumpBootToYard()
    {
        if (bootedToYard || !bootToYard) return;
        if (!Career.active || placed.Count == 0) return;
        bootedToYard = true;
        EnterMap();
    }

    // ------------------------------------------------------------ per frame
    CompoundRobot NearestEnemy(Vector3 me)
    {
        CompoundRobot best = null; float bd = float.MaxValue;
        foreach (var c in chunks.Values)
        {
            if (c.enemy != null) { float d = (c.enemy.rb.position - me).sqrMagnitude; if (d < bd) { bd = d; best = c.enemy; } }
            if (c.poolBot != null) { float d = (c.poolBot.rb.position - me).sqrMagnitude; if (d < bd) { bd = d; best = c.poolBot; } }
        }
        return best;
    }
    string EnemyIdOf(CompoundRobot bot)
    {
        foreach (var c in chunks.Values) if (c.enemy == bot) return c.enemyId;
        return YARD_BOT;
    }

    void UpdateMap()
    {
        if (Phase0Input.BackDown()) { LeaveMap(); return; }
        if (testRobot == null) { LeaveMap(); return; }
        if (yardToastT > 0f) yardToastT -= Time.deltaTime;

        Vector3 me = testRobot.rb.position;
        PumpChunks(me);
        PumpFar(me);
        PumpObjective(me);
        MapSteer();
        PlaceSkyBodies();
        PumpArenas();
        float placeD; NearestPlaceNow = NearestPlace(me, out placeD);
        PumpShopPads(me);
        if (mode != Mode.Map) return;   // the shop took us

        // the fall net: under the ground (a seam between chunks, a bad landing)
        // puts you back on it
        float gy = TerrainHeight(me.x, me.z);
        if (me.y < gy - 3f) { LiftToGround(testRobot, gy + 1.0f); me = testRobot.rb.position; }
        foreach (var c in chunks.Values)
            if (c.enemy != null && c.enemy.rb.position.y < TerrainHeight(c.enemy.rb.position.x, c.enemy.rb.position.z) - 3f)
                LiftToGround(c.enemy, TerrainHeight(c.enemy.rb.position.x, c.enemy.rb.position.z) + 1.0f);

        // crates: drive into one and it opens where it stands
        bool opened = false;
        foreach (var c in chunks.Values)
        {
            if (opened) break;
            for (int i = 0; i < c.crates.Count; i++)
            {
                var g = c.crates[i];
                if (g == null) continue;
                Vector3 cp = g.transform.position; cp.y = me.y;
                if ((cp - me).sqrMagnitude < CRATE_REACH * CRATE_REACH) { OpenCrate(c, i); opened = true; break; }
            }
        }

        // the encounter card: the nearest enemy within reach
        var near = NearestEnemy(me);
        bool show = near != null && (near.rb.position - me).sqrMagnitude < CARD_REACH * CARD_REACH;
        if (show && !yardCard) { RBTelemetry.Once(RBTelemetry.MEET); AdvanceYardStep(STEP_CHALLENGE); }
        yardCard = show;
        cardBot = show ? near : null;
        cardBotId = show ? EnemyIdOf(near) : "";

        // righting: on your back with the stick held for a second flips you
        // back onto your wheels - a visible mercy the fight does not offer
        bool flipped = Vector3.Dot(testRobot.transform.up, Vector3.up) < 0.2f;
        if (flipped && Mathf.Abs(Phase0Input.Throttle()) > 0.3f) flippedFor += Time.deltaTime; else flippedFor = 0f;
        if (flippedFor > 1f)
        {
            flippedFor = 0f;
            Vector3 fwd = testRobot.transform.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            testRobot.rb.position = new Vector3(me.x, TerrainHeight(me.x, me.z) + 0.8f, me.z);
            testRobot.rb.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);
            VelUtil.SetLinearVelocity(testRobot.rb, Vector3.zero);
            testRobot.rb.angularVelocity = Vector3.zero;
        }
    }

    /// <summary>Stick and keys -> the drive's AI inputs, shaped for a world
    /// rather than a ring. Public seam so a bench can read the result.</summary>
    public float MapSteerNow { get { return testDrive != null ? testDrive.aiSteer : 0f; } }
    void MapSteer()
    {
        // point where you want to go (BuilderManager.Drive.cs)
        PointDrive(testRobot, testDrive, driveDir, cam.transform);
        PumpCamPivot(testRobot, driveDir);
    }

    void OpenCrate(Chunk c, int i)
    {
        var g = c.crates[i];
        if (g == null) return;
        c.crates[i] = null;
        Vector3 chestAt = g.transform.position;
        Object.Destroy(g);
        if (Career.Data != null && !Career.Data.worldOpened.Contains(c.crateKeys[i])) Career.Data.worldOpened.Add(c.crateKeys[i]);
        string[] lines;
        string id = Career.QuickBoxRoll(0, out lines);
        var idf = id.Split(':');
        SpawnTreasureBurst(chestAt, idf.Length == 5 ? idf[2] : null, idf.Length == 5 ? idf[3] : null);
        Career.QueueReward(id, "TREASURE", "found in the wastes", lines);   // editor: granted at once; device: the box opens here
        if (Career.autosave) Career.Save();
        yardToast = "TREASURE  ·  " + string.Join("  ·  ", lines);
        yardToastT = 3.5f;
        AdvanceYardStep(STEP_MEET);
        SfxSynth.Place();
        RBTelemetry.Once(RBTelemetry.CRATE);
    }

    // ------------------------------------------------------------ the challenge
    /// <summary>CHALLENGE, from the card. Leaves the world and starts a Quick
    /// bout against that enemy, driven from the stick, where you stand.</summary>
    public void ChallengeParked()
    {
        if (ChallengePool()) return;   // a stranger's machine: the sign-in gate, then their build (Pool.cs)
        if (mode != Mode.Map || cardBot == null) return;
        string opp = string.IsNullOrEmpty(cardBotId) ? YARD_BOT : cardBotId;
        // no LeaveMap: StartFight leaves the map FOR THE FIGHT and keeps the
        // world standing around the ring (LeaveMapForFight)
        StartYardFight(opp);
    }

    /// <summary>StartQuickFight's shape with a NAMED opponent and no manual
    /// fallback: every robot fights itself here (design §3.6).</summary>
    public void StartYardFight(string oppId)
    {
        Career.fightAutonomous = false;
        if (!Career.active || Career.Data == null) return;
        EndScout();
        string err = Validate();
        if (err != null) { message = err; SfxSynth.Deny(); return; }
        var lack = CareerShortfall();
        if (lack.Count > 0) { message = "YOUR BUILD needs " + string.Join(", ", lack.ToArray()); SfxSynth.Deny(); return; }
        {
            int ar = Career.Data.activeRobot;
            if (ar >= 0 && ar < Career.Data.stable.Count && !SaveWouldDraft)
            {
                string snapNow = SnapshotString();
                if (Career.Data.stable[ar].snapshot != snapNow)
                {
                    Career.Data.stable[ar].snapshot = snapNow;
                    if (Career.autosave) Career.Save();
                }
            }
        }
        var entry = EnemyRoster.Find(oppId);
        if (entry == null) return;
        bool poolBout = yardOpponentParts != null;   // a stranger's build (Pool.cs): the roster id is only the tier's placeholder

        Career.fightBuildValue = BuildValueCareer();
        var recipe = EnemyRoster.Recipe(entry.id, palette);
        int ov = 0;
        foreach (var p2 in recipe) ov += CareerDB.PartPrice(p2.def.id, p2.MatName());
        Career.fightOppValue = ov;
        Career.activeLeague = null; Career.activeContest = null;
        Career.targetLeagueIdx = Career.FurthestLeague();
        CrowdAudio.SetVenue(Career.targetLeagueIdx);
        opponentId = entry.id;
        opponentTier = poolBout ? AiTier.Veteran : entry.tier;
        quickArmourMat = "";
        Progression.activeRungIndex = -1;
        Progression.activeChallengeIdx = -1;
        quickNext = true;
        AdvanceYardStep(STEP_SHOP);
        RBTelemetry.Once(RBTelemetry.CHALLENGE);
        RBTelemetry.Once(RBTelemetry.QUICK);
        StartFight();
        quickNext = false;
        if (mode != Mode.Fight) { Career.quickFight = false; FightManager.quickBout = false; return; }
        // YOU DRIVE (owen, 2026-09-10: "replace auto fight with manual fight"),
        // and with the SAME stick as the map: the player's side takes the AI
        // channel at the bell and PumpStickFight feeds it (Drive.cs). No
        // ProgramRunner, no auto-brain.
        var afm = Object.FindFirstObjectByType<FightManager>();
        if (afm != null) { afm.playerSource = ControlSource.AI; yardStickFight = true; }
    }


    // ------------------------------------------------------------ the HUD
    /// <summary>What the HUD shows this frame (MapHudUI draws it): compass
    /// chips for the nearest crate, the nearest enemy, the nearest place and
    /// home, each with a bearing from the machine's nose; a toast or a place
    /// banner; the encounter card.</summary>
    public struct HudEntry { public string label; public float angle; public float dist; public Color tint; }
    public class MapHudModel
    {
        public readonly List<HudEntry> compass = new List<HudEntry>();
        public string toast = "", banner = "";
        public bool card; public string cardTitle = "", cardSub = "";
        public string objective = ""; public int objectiveChip = -1;
    }
    readonly MapHudModel hudModel = new MapHudModel();

    // ---- THE FIRST MINUTE (CrazyGames plan, step 1). A stranger must do the
    // loop without thinking: the objective line says what, the highlighted
    // chip says which way, the ring and beam in the world say where.
    public const int STEP_CHEST = 0, STEP_MEET = 1, STEP_CHALLENGE = 2, STEP_SHOP = 3, STEP_EXPLORE = 4;
    public int YardStep { get { return Career.Data != null ? Career.Data.yardStep : STEP_EXPLORE; } }
    GameObject objectiveMarker; Vector3 objectivePos; bool objectiveHas;
    public bool ObjectiveMarkerShown { get { return objectiveMarker != null && objectiveMarker.activeSelf; } }
    public Vector3 ObjectiveMarkerPos { get { return objectiveMarker != null ? objectiveMarker.transform.position : Vector3.zero; } }
    /// <summary>Steps only advance (max), so a player who does things out of
    /// order - the pad before the challenge - is never asked to go back.</summary>
    void AdvanceYardStep(int reached)
    {
        if (Career.Data == null || Career.Data.yardStep >= reached) return;
        Career.Data.yardStep = reached;
        if (Career.autosave) Career.Save();
    }
    static string ObjectiveLine(int step)
    {
        switch (step)
        {
            case STEP_CHEST:     return "NEXT  ·  drive into the treasure chest";
            case STEP_MEET:      return "NEXT  ·  find the parked robot";
            case STEP_CHALLENGE: return "NEXT  ·  tap CHALLENGE - you drive the bout";
            case STEP_SHOP:      return "NEXT  ·  drive onto the trading post's lit pad";
            default:             return "";
        }
    }
    /// <summary>Where the objective is this frame (a crate, the parked
    /// machine, a shop pad), and the chip that points at it.</summary>
    void PumpObjective(Vector3 me)
    {
        int step = YardStep;
        objectiveHas = false; int chip = -1;
        if (step == STEP_CHEST)
        {
            float best = float.MaxValue;
            foreach (var c in chunks.Values) foreach (var g in c.crates) if (g != null)
            { float d = (g.transform.position - me).sqrMagnitude; if (d < best) { best = d; objectivePos = g.transform.position; objectiveHas = true; } }
            chip = 0;
        }
        else if (step == STEP_MEET || step == STEP_CHALLENGE)
        {
            var en = NearestEnemy(me);
            if (en != null) { objectivePos = en.rb.position; objectiveHas = true; }
        }
        else if (step == STEP_SHOP)
        {
            float best = float.MaxValue;
            foreach (var pad in shopPads) { float d = (pad - me).sqrMagnitude; if (d < best) { best = d; objectivePos = pad; objectiveHas = true; } }
            if (!objectiveHas) { float pd; var np = NearestPlace(me, out pd); if (np != null && np.kind == PlaceKind.Shop) { objectivePos = np.centre; objectiveHas = true; } }
        }
        objectiveChip = chip;
        if (objectiveHas && step != STEP_CHEST)
        {
            // the chip that points at the objective: the enemy's, or the place's
            var m = HudModel();
            for (int i = 0; i < m.compass.Count; i++)
            {
                bool enemyChip = (step == STEP_MEET || step == STEP_CHALLENGE) && i == 1 && m.compass.Count > 2;
                bool placeChip = step == STEP_SHOP && m.compass[i].label == "TRADING POST";
                if (enemyChip || placeChip) { objectiveChip = i; break; }
            }
        }
        if (objectiveMarker == null && worldRoot != null)
        {
            EnsureLookMats();
            objectiveMarker = new GameObject("objective_marker");
            objectiveMarker.transform.SetParent(worldRoot.transform, false);
            Prim(PrimitiveType.Cylinder, objectiveMarker.transform, new Vector3(0f, 0.06f, 0f), new Vector3(3.6f, 0.03f, 3.6f), Quaternion.identity, matCyan, false);
            Prim(PrimitiveType.Cylinder, objectiveMarker.transform, new Vector3(0f, 0.10f, 0f), new Vector3(2.6f, 0.03f, 2.6f), Quaternion.identity, matBody, false);
            Prim(PrimitiveType.Cylinder, objectiveMarker.transform, new Vector3(0f, 9f, 0f), new Vector3(0.55f, 9f, 0.55f), Quaternion.identity, BeaconMat(true), false);   // the beam (Scrapyard/Beacon: light, not a rod). Narrow: you stand right next to this one.
        }
        if (objectiveMarker != null)
        {
            if (objectiveMarker.activeSelf != objectiveHas) objectiveMarker.SetActive(objectiveHas);
            if (objectiveHas) objectiveMarker.transform.position = new Vector3(objectivePos.x, TerrainHeight(objectivePos.x, objectivePos.z), objectivePos.z);
        }
    }
    int objectiveChip = -1;
    public static readonly Color HUD_TREASURE = new Color(1f, 0.84f, 0.40f), HUD_ENEMY = new Color(1f, 0.55f, 0.50f),
                                 HUD_PLACE = new Color(0.55f, 0.90f, 1f), HUD_HOME = new Color(0.92f, 0.94f, 1f);
    public MapHudModel HudModel()
    {
        var m = hudModel;
        m.compass.Clear(); m.toast = ""; m.banner = ""; m.card = false; m.objective = ""; m.objectiveChip = -1;
        if (mode != Mode.Map || testRobot == null) return m;
        m.objective = ObjectiveLine(YardStep); m.objectiveChip = objectiveChip;
        Vector3 me = testRobot.rb.position;
        Vector3 fwd = testRobot.transform.TransformDirection(driveDir); fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        Vector3 nearestCrate = Vector3.zero; float best = float.MaxValue; bool any = false;
        foreach (var c in chunks.Values) foreach (var g in c.crates) if (g != null)
        { float d = (g.transform.position - me).sqrMagnitude; if (d < best) { best = d; nearestCrate = g.transform.position; any = true; } }
        if (any) m.compass.Add(Entry("TREASURE", nearestCrate - me, fwd, HUD_TREASURE));
        var en = NearestEnemy(me);
        if (en != null) m.compass.Add(Entry(en.name.ToUpper(), en.rb.position - me, fwd, HUD_ENEMY));
        var np = NearestPlaceNow;
        if (np != null) m.compass.Add(Entry(np.name, np.centre - me, fwd, HUD_PLACE));
        m.compass.Add(Entry("HOME", homePos - me, fwd, HUD_HOME));
        if (yardToastT > 0f && yardToast.Length > 0) m.toast = yardToast;
        else if (np != null)
        {
            float pd = Vector2.Distance(new Vector2(me.x, me.z), new Vector2(np.centre.x, np.centre.z));
            if (pd < np.radius + 6f) m.banner = np.name + "  -  " + np.Hint;
        }
        if (yardCard && cardBot != null)
        {
            var pe = PoolEntryOf(cardBot);
            m.card = true;
            if (pe != null)
            {
                m.cardTitle = cardBot.name + "   ·   by " + (string.IsNullOrEmpty(pe.owner) ? "another player" : pe.owner);
                m.cardSub = PortalBuild ? "another player's machine  ·  30-second bout, you drive  ·  drive away to decline"
                          : LadderClient.SignedIn ? "another player's machine  ·  30-second bout, you drive  ·  points at the bell"
                                                  : "another player's machine  ·  sign in to challenge  ·  drive away to decline";
            }
            else
            {
                var entry = EnemyRoster.Find(string.IsNullOrEmpty(cardBotId) ? YARD_BOT : cardBotId);
                m.cardTitle = entry != null ? entry.label + "   ·   " + entry.tier.ToString().ToUpper() : cardBot.name;
                m.cardSub = "30-second bout  ·  you drive: stick to move, FIRE for the weapon  ·  drive away to decline";
            }
        }
        return m;
    }
    static HudEntry Entry(string label, Vector3 to, Vector3 fwd, Color tint)
    {
        to.y = 0f;
        return new HudEntry { label = label, dist = to.magnitude, angle = Vector3.SignedAngle(fwd, to, Vector3.up), tint = tint };
    }

}
}
