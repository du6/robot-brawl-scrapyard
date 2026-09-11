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
    // THE MAP'S STEERING (owen, 2026-09-10: "turning is too sensitive, making
    // it hard to drive straight"). The fight's drive takes the stick raw, and a
    // skid-steer build (SCRAPPER: four fixed wheels, no steering geometry)
    // turns by braking one side at full stick whatever the speed - the
    // drive's speed scaling only touches STEERABLE wheels. So on the map the
    // player's inputs go through the drive's AI channel, which this file
    // owns: a dead zone the stick's jitter cannot cross, a gain, less of it
    // at speed, and a rate so a tap is a nudge and a hold is a turn. The
    // shared wheel model is untouched (fights are byte-identical).
    public const float STEER_DEAD = 0.18f;
    public const float STEER_GAIN = 0.60f;
    public const float STEER_GAIN_FAST = 0.45f;   // at max speed (0.30 turned only 10 deg/s at 6 m/s - sluggish, measured)
    public const float STEER_RATE = 3.0f;         // full deflection in ~0.33 s
    // MEASURED (MapBench trace, flat ground, zero steer): the rookie veers by
    // itself, and the veer grows with speed - 1 deg at 2 m/s, 12 deg at
    // 10 m/s over two seconds. The build is not symmetrical, and a 14 m ring
    // never lets it reach the speed where that shows. So the map caps speed
    // (the design's "try 6") and holds heading: with the stick centred, a
    // little counter-steer against the machine's own yaw rate.
    public const float MAP_MAX_SPEED = 6f;
    public const float HOLD_K = 0.15f;       // per rad/s of yaw rate (damping)
    public const float HOLD_ERR = 0.020f;    // per degree off the held heading (return)
    public const float HOLD_MAX = 0.30f;
    float holdHeading; bool holding;

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
        // CRATERS: one per 160 m cell, half the time, a bowl with a rim.
        float cell = 160f;
        int cx = Mathf.FloorToInt(nx / cell), cz = Mathf.FloorToInt(nz / cell);
        for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int gx = cx + dx, gz = cz + dz;
                float hsh = Mathf.Abs(Mathf.Sin(gx * 127.1f + gz * 311.7f) * 43758.5453f);
                float f = hsh - Mathf.Floor(hsh);
                if (f > 0.5f) continue;
                float rad = 12f + f * 34f;
                float ox = (gx + 0.2f + f * 0.6f) * cell, oz = (gz + 0.25f + (f * 7.3f - Mathf.Floor(f * 7.3f)) * 0.5f) * cell;
                float d = Vector2.Distance(new Vector2(nx, nz), new Vector2(ox, oz)) / rad;
                if (d < 1.15f)
                {
                    float depth = rad * 0.16f;
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

        // places: any whose centre lies in this chunk (Places)
        BuildPlacesInChunk(ch, cx, cz, rng);

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
            if (c.enemy != null && c.enemy == cardBot) continue;     // never under a live card
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
        followCam.target = testRobot.transform;
        followCam.forwardHint = driveDir;
        followCam.distance = 8.5f; followCam.height = 3.4f;   // lower: the horizon in the frame
        followCam.clampHalf = 0f;            // no clamp: the world has no edge
        followCam.SnapNow();
        AddHeadlight(testRobot, driveDir);

        RBTelemetry.Once(RBTelemetry.MAP);
    }

    /// <summary>GARAGE. BackToBuild's sweep tears down every CompoundRobot and
    /// the world root.</summary>
    public void LeaveMap()
    {
        if (mode != Mode.Map) return;
        if (testRobot != null) { lastMapPos = testRobot.rb.position; lastMapYaw = testRobot.transform.eulerAngles.y; resumeSeed = worldSeed; hasResume = true; }
        chunks.Clear();
        cardBot = null; cardBotId = ""; yardCard = false;
        RestoreLook();
        BackToBuild();
        worldRoot = null;
        ARENA_HALF = 7f;
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
            if (c.enemy == null) continue;
            float d = (c.enemy.rb.position - me).sqrMagnitude;
            if (d < bd) { bd = d; best = c.enemy; }
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
        if (show && !yardCard) RBTelemetry.Once(RBTelemetry.MEET);
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
        if (testDrive == null) return;
        float thr = Phase0Input.Throttle();
        float raw = Phase0Input.Steer();
        float mag = Mathf.Abs(raw);
        float shaped = mag < STEER_DEAD ? 0f : Mathf.Sign(raw) * (mag - STEER_DEAD) / (1f - STEER_DEAD);
        float v = VelUtil.GetLinearVelocity(testRobot.rb).magnitude;
        float gain = Mathf.Lerp(STEER_GAIN, STEER_GAIN_FAST, Mathf.Clamp01(v / Mathf.Max(0.1f, testDrive.maxSpeed)));
        float target = shaped * gain;
        float rate = target == 0f ? STEER_RATE * 2f : STEER_RATE;   // let go and it straightens fast
        if (target == 0f && Mathf.Abs(thr) > 0.05f)
        {
            // heading hold: counter the machine's own yaw when you mean straight
            // MEASURED the wrong way first (MapBench trace: yaw -51 deg with the
            // hold pushing +0.10 - a runaway spin at throttle). In this drive
            // POSITIVE steer turns LEFT, i.e. NEGATIVE angular y, so the
            // counter-steer has the same sign as the yaw rate.
            // THE HEADING HOLD. Measured three ways before it was right: the
            // sign first (yaw -51 with the hold pushing +0.10 - a runaway spin;
            // in this drive POSITIVE steer turns LEFT, i.e. NEGATIVE angular
            // y), then the strength (rate damping alone let the machine's own
            // pull during acceleration walk the heading 23 deg and only then
            // stop it). So: remember the heading the moment the stick centres,
            // and steer back toward it - error and rate, both with the sign of
            // the yaw. A moved stick releases the hold.
            float yaw = testRobot.transform.eulerAngles.y;
            if (!holding) { holdHeading = yaw; holding = true; }
            float err = Mathf.DeltaAngle(holdHeading, yaw);     // +: turned right of the held heading
            float yawRate = testRobot.rb.angularVelocity.y;
            // Sign, MEASURED on the moving machine (trace: err +24 with the hold
            // at +0.35 ran away to a spin): positive steer turns RIGHT, positive
            // angular y is RIGHT, positive err is RIGHT - so the hold is the
            // negative of both. (The near-stationary pivot in an earlier trace
            // read the other way and was the wrong case to trust.)
            target = -Mathf.Clamp(err * HOLD_ERR + yawRate * HOLD_K, -HOLD_MAX, HOLD_MAX);
            rate = STEER_RATE * 2f;
        }
        else holding = false;
        testDrive.aiSteer = Mathf.MoveTowards(testDrive.aiSteer, target, rate * Time.deltaTime);
        testDrive.aiThrottle = Mathf.Abs(thr) < 0.05f ? 0f : thr;
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
        SfxSynth.Place();
        RBTelemetry.Once(RBTelemetry.CRATE);
    }

    // ------------------------------------------------------------ the challenge
    /// <summary>CHALLENGE, from the card. Leaves the world and starts a Quick
    /// bout against that enemy under the auto-brain. Fighting in place is the
    /// next pass; for now the bout is in the standard ring.</summary>
    public void ChallengeParked()
    {
        if (mode != Mode.Map || cardBot == null) return;
        string opp = string.IsNullOrEmpty(cardBotId) ? YARD_BOT : cardBotId;
        LeaveMap();
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

        Career.fightBuildValue = BuildValueCareer();
        var recipe = EnemyRoster.Recipe(entry.id, palette);
        int ov = 0;
        foreach (var p2 in recipe) ov += CareerDB.PartPrice(p2.def.id, p2.MatName());
        Career.fightOppValue = ov;
        Career.activeLeague = null; Career.activeContest = null;
        Career.targetLeagueIdx = Career.FurthestLeague();
        CrowdAudio.SetVenue(Career.targetLeagueIdx);
        opponentId = entry.id;
        opponentTier = entry.tier;
        quickArmourMat = "";
        Progression.activeRungIndex = -1;
        Progression.activeChallengeIdx = -1;
        quickNext = true;
        RBTelemetry.Once(RBTelemetry.CHALLENGE);
        RBTelemetry.Once(RBTelemetry.QUICK);
        StartFight();
        quickNext = false;
        if (mode != Mode.Fight) { Career.quickFight = false; FightManager.quickBout = false; return; }
        // YOU DRIVE (owen, 2026-09-10: "replace auto fight with manual fight").
        // FightManager.playerSource stays Keyboard - the stick and FIRE that
        // drove you here drive the bout. No ProgramRunner, no auto-brain.
    }


    // ------------------------------------------------------------ the HUD
    void MapHud()
    {
        float s = GuiScale;
        Matrix4x4 saved = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float w = Screen.width / s, h = Screen.height / s;
        float top = 8f + Screen.safeArea.y / s;
        int fs = GUI.skin.button.fontSize;
        GUI.skin.button.fontSize = 16;
        bool garage = GUI.Button(new Rect(w - 106f, top, 96f, 40f), "GARAGE");
        GUI.skin.button.fontSize = fs;

        // the compass strip: bearings to the nearest crate, the nearest enemy, home
        var st = new GUIStyle(GUI.skin.label); st.fontSize = 20; st.fontStyle = FontStyle.Bold;
        st.normal.textColor = new Color(0.95f, 0.97f, 1f);
        GUI.Box(new Rect(8f, top, w - 124f, 40f), "");
        if (testRobot != null)
        {
            Vector3 me = testRobot.rb.position;
            Vector3 fwd = testRobot.transform.TransformDirection(driveDir); fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            var sb = new System.Text.StringBuilder();
            Vector3 nearestCrate = Vector3.zero; float best = float.MaxValue; bool any = false;
            foreach (var c in chunks.Values) foreach (var g in c.crates) if (g != null)
            { float d = (g.transform.position - me).sqrMagnitude; if (d < best) { best = d; nearestCrate = g.transform.position; any = true; } }
            sb.Append(any ? Bearing("TREASURE", nearestCrate - me, fwd) : "no treasure in sight - drive on");
            var en = NearestEnemy(me);
            if (en != null) sb.Append("     ").Append(Bearing(en.name.ToUpper(), en.rb.position - me, fwd));
            var np = NearestPlaceNow;
            if (np != null) sb.Append("     ").Append(Bearing(np.name, np.centre - me, fwd));
            sb.Append("     ").Append(Bearing("HOME", homePos - me, fwd));
            GUI.Label(new Rect(20f, top + 6f, w - 140f, 28f), sb.ToString(), st);
            // inside a place: its name and what it is for, under the strip
            if (np != null && (yardToastT <= 0f || yardToast.Length == 0))
            {
                float pd = Vector2.Distance(new Vector2(me.x, me.z), new Vector2(np.centre.x, np.centre.z));
                if (pd < np.radius + 6f)
                {
                    var ps = new GUIStyle(GUI.skin.label); ps.fontSize = 16; ps.alignment = TextAnchor.MiddleCenter;
                    ps.normal.textColor = new Color(0.62f, 0.90f, 1f);
                    GUI.Label(new Rect(0f, top + 48f, w, 26f), np.name + "  -  " + np.Hint, ps);
                }
            }
        }
        if (yardToastT > 0f && yardToast.Length > 0)
        {
            var ts = new GUIStyle(GUI.skin.label); ts.fontSize = 20; ts.fontStyle = FontStyle.Bold; ts.alignment = TextAnchor.MiddleCenter;
            ts.normal.textColor = new Color(1f, 0.87f, 0.46f);
            GUI.Label(new Rect(0f, top + 48f, w, 30f), yardToast, ts);
        }
        bool challenge = false;
        if (yardCard && cardBot != null)
        {
            var entry = EnemyRoster.Find(string.IsNullOrEmpty(cardBotId) ? YARD_BOT : cardBotId);
            float cw = Mathf.Min(380f, w - 24f), ch = 112f;
            var box = new Rect((w - cw) * 0.5f, h - ch - 16f - Screen.safeArea.y / s, cw, ch);
            GUI.Box(box, "");
            var hs = new GUIStyle(GUI.skin.label); hs.fontSize = 18; hs.fontStyle = FontStyle.Bold; hs.alignment = TextAnchor.MiddleCenter;
            hs.normal.textColor = Color.white;
            GUI.Label(new Rect(box.x, box.y + 6f, box.width, 26f),
                      (entry != null ? entry.label + "   ·   " + entry.tier.ToString().ToUpper() : cardBot.name), hs);
            var cs = new GUIStyle(GUI.skin.label); cs.fontSize = 13; cs.alignment = TextAnchor.MiddleCenter;
            cs.normal.textColor = new Color(0.75f, 0.80f, 0.88f);
            GUI.Label(new Rect(box.x, box.y + 32f, box.width, 20f), "30-second bout  ·  you drive: stick to move, FIRE for the weapon  ·  drive away to decline", cs);
            GUI.skin.button.fontSize = 18;
            challenge = GUI.Button(new Rect(box.x + 24f, box.y + 58f, box.width - 48f, 44f), "CHALLENGE");
            GUI.skin.button.fontSize = fs;
        }
        GUI.matrix = saved;
        if (garage) { LeaveMap(); return; }
        if (challenge) ChallengeParked();
    }

    static string Bearing(string what, Vector3 to, Vector3 fwd)
    {
        to.y = 0f;
        float dist = to.magnitude;
        float ang = Vector3.SignedAngle(fwd, to, Vector3.up);
        // ASCII on purpose: the arrow glyphs (U+2191 etc.) are not in the IMGUI
        // font and drew as nothing - seen live 2026-09-10, "TREASURE   17 m".
        string arrow = Mathf.Abs(ang) < 25f ? "^" : ang > 0f ? (ang > 135f ? "v" : ">") : (ang < -135f ? "v" : "<");
        return what + " " + arrow + " " + Mathf.RoundToInt(dist) + " m";
    }
}
}
