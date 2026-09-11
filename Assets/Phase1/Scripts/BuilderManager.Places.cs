// ===========================================================================
// BuilderManager.Places.cs — PLACES WORTH DRIVING TO (Robot Brawl: Scrapyard).
// owen, 2026-09-10: "Can we make the planet look more realistic? Now it looks
// like a desert with some cubes and other shapes, which feels boring to
// explore. For example, can we add buildings, shops, robot parks, toys,
// arenas with robots fighting each other, etc.?"
//
// A PLACE is a composition, not a prop: it flattens the ground under it,
// gets a plaza and roads, shows on the compass, and does something.
//   TOWN        a dozen buildings with lit windows around a beacon
//   TRADING POST a shop - drive onto its pad and the workshop opens on SHOP
//   ROBOT PARK  a fountain, benches, and three machines on pedestals
//   ARENA       two AI robots fighting each other, live, in a ring with
//               stands; the referee respawns the pair when one goes down
//   PLAYGROUND  ramps to jump, balls to push, a turnstile that shoves
// One place per 150 m cell, most of the time, from the world's seed; the
// cell around home always holds a trading post, the next ones a park and an
// arena, so the first ten minutes have somewhere to go.
// ===========================================================================
using System.Collections.Generic;
using UnityEngine;

namespace RobotBrawl.Phase0
{
public partial class BuilderManager
{
    public enum PlaceKind { Town, Shop, Park, Arena, Playground }
    public class Place
    {
        public PlaceKind kind; public Vector3 centre; public float radius; public float baseH; public string name; public int px, pz;
        public string Hint
        {
            get
            {
                switch (kind)
                {
                    case PlaceKind.Shop: return "drive onto the lit pad to trade";
                    case PlaceKind.Park: return "machines on show - the fountain is the meeting place";
                    case PlaceKind.Arena: return "two machines, no rules - watch from the stands";
                    case PlaceKind.Playground: return "jump the ramps, shove the balls";
                    default: return "streets and lit windows";
                }
            }
        }
    }
    class ArenaShow
    {
        public Vector3 centre; public float radius;
        public CompoundRobot a, b; public string aId = "", bId = "";
        public float downSince = -1f; public float flipA, flipB;
    }

    public const float PLACE_CELL = 150f;
    const float PLACE_EDGE = 14f;              // metres over which the ground meets a plaza
    readonly Dictionary<long, Place> placeCache = new Dictionary<long, Place>();
    readonly List<Vector3> shopPads = new List<Vector3>();      // every lit pad in the loaded world
    Vector3 lastMapPos; bool hasResume;
    public bool LastShopOpened { get; private set; }
    public Place NearestPlaceNow { get; private set; }

    static int CellOf(float v) { return Mathf.FloorToInt(v / PLACE_CELL); }
    static long CellKey(int px, int pz) { return ((long)px << 32) ^ (uint)pz; }

    /// <summary>The place in a 150 m cell, or null. Deterministic from the
    /// world's seed. The three cells nearest home are fixed: a trading post,
    /// a park, an arena.</summary>
    public Place PlaceInCell(int px, int pz)
    {
        long k = CellKey(px, pz);
        Place p;
        if (placeCache.TryGetValue(k, out p)) return p;
        int hpx = CellOf(homePos.x), hpz = CellOf(homePos.z);
        var rng = new System.Random(unchecked(worldSeed * 40503 ^ px * 668265263 ^ pz * 374761393));
        p = null;
        if (px == hpx && pz == hpz)
            p = new Place { kind = PlaceKind.Shop, centre = new Vector3(homePos.x + 46f, 0f, homePos.z + 34f), radius = 18f, name = "TRADING POST" };
        else if (px == hpx + 1 && pz == hpz)
            p = new Place { kind = PlaceKind.Park, centre = new Vector3((px + 0.5f) * PLACE_CELL, 0f, homePos.z + 40f), radius = 26f, name = "ROBOT PARK" };
        else if (px == hpx && pz == hpz + 1)
            p = new Place { kind = PlaceKind.Arena, centre = new Vector3(homePos.x + 30f, 0f, (pz + 0.35f) * PLACE_CELL), radius = 30f, name = "ARENA" };
        else if (rng.Next(100) < 62)
        {
            int roll = rng.Next(100);
            PlaceKind kind = roll < 30 ? PlaceKind.Town : roll < 45 ? PlaceKind.Shop : roll < 65 ? PlaceKind.Park : roll < 84 ? PlaceKind.Arena : PlaceKind.Playground;
            float radius = kind == PlaceKind.Town ? 42f : kind == PlaceKind.Shop ? 18f : kind == PlaceKind.Park ? 26f : kind == PlaceKind.Arena ? 30f : 28f;
            float m = radius + PLACE_EDGE + 6f;
            var c = new Vector3(px * PLACE_CELL + m + (float)rng.NextDouble() * (PLACE_CELL - 2f * m), 0f,
                                pz * PLACE_CELL + m + (float)rng.NextDouble() * (PLACE_CELL - 2f * m));
            string[] townNames = { "RUST HARBOUR", "VOLT CITY", "GEAR TOWN", "IRON REACH", "SOLDER FALLS", "COBALT CROSS" };
            p = new Place { kind = kind, centre = c, radius = radius,
                            name = kind == PlaceKind.Town ? townNames[rng.Next(townNames.Length)]
                                 : kind == PlaceKind.Shop ? "TRADING POST" : kind == PlaceKind.Park ? "ROBOT PARK"
                                 : kind == PlaceKind.Arena ? "ARENA" : "PLAYGROUND" };
        }
        if (p != null)
        {
            p.px = px; p.pz = pz;
            p.baseH = RawHeight(p.centre.x, p.centre.z) * HomeFlat(p.centre.x, p.centre.z);
            p.centre.y = p.baseH;
        }
        placeCache[k] = p;
        return p;
    }

    /// <summary>Ground under a place is flat at its base height; the edge
    /// meets the land over PLACE_EDGE metres. Called by TerrainHeight.</summary>
    float FlattenForPlaces(float x, float z, float h)
    {
        int cx = CellOf(x), cz = CellOf(z);
        for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                var p = PlaceInCell(cx + dx, cz + dz);
                if (p == null) continue;
                float d = Vector2.Distance(new Vector2(x, z), new Vector2(p.centre.x, p.centre.z));
                if (d > p.radius + PLACE_EDGE) continue;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((d - p.radius) / PLACE_EDGE));
                h = Mathf.Lerp(p.baseH, h, t);
            }
        return h;
    }

    /// <summary>The nearest place to a point, scanning the 5 x 5 cells around it.</summary>
    public Place NearestPlace(Vector3 at, out float dist)
    {
        Place best = null; dist = float.MaxValue;
        int cx = CellOf(at.x), cz = CellOf(at.z);
        for (int dz = -2; dz <= 2; dz++)
            for (int dx = -2; dx <= 2; dx++)
            {
                var p = PlaceInCell(cx + dx, cz + dz);
                if (p == null) continue;
                float d = Vector2.Distance(new Vector2(at.x, at.z), new Vector2(p.centre.x, p.centre.z));
                if (d < dist) { dist = d; best = p; }
            }
        return best;
    }

    /// <summary>True inside a place's plaza (plus a margin): nothing random spawns there.</summary>
    bool InsidePlace(float x, float z, float margin)
    {
        float d; var p = NearestPlace(new Vector3(x, 0f, z), out d);
        return p != null && d < p.radius + margin;
    }

    // ------------------------------------------------------------ building them
    void BuildPlacesInChunk(Chunk ch, int cx, int cz, System.Random rng)
    {
        // every place whose CENTRE falls in this chunk is built under it
        float x0 = cx * CHUNK, z0 = cz * CHUNK;
        for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                var p = PlaceInCell(CellOf(x0 + CHUNK * 0.5f) + dx, CellOf(z0 + CHUNK * 0.5f) + dz);
                if (p == null) continue;
                if (p.centre.x < x0 || p.centre.x >= x0 + CHUNK || p.centre.z < z0 || p.centre.z >= z0 + CHUNK) continue;
                BuildPlace(ch, p, new System.Random(unchecked(worldSeed * 7919 ^ p.px * 92821 ^ p.pz * 68917)));
            }
    }

    void BuildPlace(Chunk ch, Place p, System.Random rng)
    {
        EnsureLookMats();
        var root = new GameObject("place_" + p.kind + "_" + p.name.Replace(' ', '_'));
        root.transform.SetParent(ch.root.transform, false);
        root.transform.position = p.centre;
        var t = root.transform;
        var matPlaza = PartVisualFactory.Mat(new Color(0.20f, 0.21f, 0.25f), 0.3f, 0.5f);
        var matRoad = PartVisualFactory.Mat(new Color(0.15f, 0.16f, 0.19f), 0.2f, 0.4f);
        // the plaza and four roads out of it
        Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0.03f, 0f), new Vector3(p.radius * 1.1f, 0.03f, p.radius * 1.1f), Quaternion.identity, matPlaza, false);
        for (int i = 0; i < 4; i++)
        {
            float a = i * 90f + 45f;
            var dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
            Prim(PrimitiveType.Cube, t, dir * (p.radius * 0.55f + PLACE_EDGE * 0.5f) + Vector3.up * 0.02f,
                 new Vector3(3.2f, 0.04f, p.radius * 0.9f + PLACE_EDGE), Quaternion.Euler(0f, a, 0f), matRoad, false);
        }
        switch (p.kind)
        {
            case PlaceKind.Town: BuildTown(t, p, rng); break;
            case PlaceKind.Shop: BuildShop(ch, t, p, rng); break;
            case PlaceKind.Park: BuildPark(ch, t, p, rng); break;
            case PlaceKind.Arena: BuildArenaShow(ch, t, p, rng); break;
            default: BuildPlayground(t, p, rng); break;
        }
    }

    void BuildTown(Transform t, Place p, System.Random rng)
    {
        int n = 9 + rng.Next(6);
        for (int i = 0; i < n; i++)
        {
            float a = i * 360f / n + (float)rng.NextDouble() * 18f;
            float r = 11f + (float)rng.NextDouble() * (p.radius - 18f);
            Vector3 pos = Quaternion.Euler(0f, a, 0f) * Vector3.forward * r;
            float w = 5f + (float)rng.NextDouble() * 4f, d = 5f + (float)rng.NextDouble() * 4f, h = 5f + (float)rng.NextDouble() * 13f;
            var body = Prim(PrimitiveType.Cube, t, pos + Vector3.up * h * 0.5f, new Vector3(w, h, d), Quaternion.Euler(0f, a, 0f), rng.Next(3) == 0 ? matBodyLight : matBody);
            // lit windows: a band per storey on the plaza-facing side, some dark
            int storeys = Mathf.FloorToInt(h / 3f);
            for (int s = 0; s < storeys; s++)
            {
                if (rng.Next(4) == 0) continue;
                var band = Prim(PrimitiveType.Cube, body.transform, new Vector3(0f, -0.5f + (s + 0.6f) / storeys, -0.5f - 0.02f / d),
                                new Vector3(0.82f, 0.32f / h, 0.05f / d), Quaternion.identity, rng.Next(3) == 0 ? matAmber : matCyan, false);
                band.name = "windows";
            }
            if (rng.Next(3) == 0)
            {
                Prim(PrimitiveType.Cylinder, t, pos + Vector3.up * (h + 1.5f), new Vector3(0.15f, 1.5f, 0.15f), Quaternion.identity, matBodyLight, false);
                Prim(PrimitiveType.Sphere, t, pos + Vector3.up * (h + 3.1f), Vector3.one * 0.5f, Quaternion.identity, matAmber, false);
            }
        }
        // the beacon in the middle of the plaza
        Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 7f, 0f), new Vector3(0.7f, 7f, 0.7f), Quaternion.identity, matBodyLight);
        Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 14.2f, 0f), new Vector3(1.4f, 0.08f, 1.4f), Quaternion.identity, matCyan, false);
        Prim(PrimitiveType.Sphere, t, new Vector3(0f, 14.9f, 0f), Vector3.one * 0.9f, Quaternion.identity, matAmber, false);
    }

    void BuildShop(Chunk ch, Transform t, Place p, System.Random rng)
    {
        // the building, its sign, and the lit pad you drive onto
        Prim(PrimitiveType.Cube, t, new Vector3(0f, 2.5f, 4f), new Vector3(12f, 5f, 8f), Quaternion.identity, matBodyLight);
        Prim(PrimitiveType.Cube, t, new Vector3(0f, 6.0f, 0.2f), new Vector3(9f, 1.8f, 0.3f), Quaternion.identity, matAmber, false);   // the sign
        for (int i = 0; i < 3; i++)   // three lit shopfront windows
            Prim(PrimitiveType.Cube, t, new Vector3(-3.5f + i * 3.5f, 2.2f, -0.05f), new Vector3(2.4f, 2.2f, 0.06f), Quaternion.identity, matCyan, false);
        var pad = Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0.06f, -6.5f), new Vector3(3.2f, 0.05f, 3.2f), Quaternion.identity, matCyan, false);
        pad.name = "shop_pad";
        Prim(PrimitiveType.Cube, t, new Vector3(-7f, 0.6f, -4f), new Vector3(1.4f, 1.2f, 1.0f), Quaternion.Euler(0f, 20f, 0f), matPod, true);  // stock outside
        Prim(PrimitiveType.Cube, t, new Vector3(7f, 0.5f, -3f), new Vector3(1.2f, 1.0f, 1.2f), Quaternion.Euler(0f, -15f, 0f), matPod, true);
        shopPads.Add(pad.transform.position);
        ch.shopPads.Add(pad.transform.position);
    }

    void BuildPark(Chunk ch, Transform t, Place p, System.Random rng)
    {
        int posts = 22;
        for (int i = 0; i < posts; i++)
        {
            float a = i * Mathf.PI * 2f / posts;
            if (i % 11 == 0) continue;   // two gaps: the gates
            Prim(PrimitiveType.Cube, t, new Vector3(Mathf.Cos(a) * (p.radius - 3f), 0.6f, Mathf.Sin(a) * (p.radius - 3f)), new Vector3(0.3f, 1.2f, 0.3f),
                 Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f), matBodyLight);
        }
        // the fountain
        Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0.35f, 0f), new Vector3(3.4f, 0.35f, 3.4f), Quaternion.identity, matBodyLight);
        Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0.75f, 0f), new Vector3(3.0f, 0.05f, 3.0f), Quaternion.identity, matCyan, false);
        Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 2.2f, 0f), new Vector3(0.5f, 1.6f, 0.5f), Quaternion.identity, matCyan, false);
        Prim(PrimitiveType.Sphere, t, new Vector3(0f, 4.0f, 0f), Vector3.one * 1.1f, Quaternion.identity, matCyan, false);
        // benches
        for (int i = 0; i < 4; i++)
        {
            float a = i * 90f + 45f;
            Prim(PrimitiveType.Cube, t, Quaternion.Euler(0f, a, 0f) * Vector3.forward * 7f + Vector3.up * 0.4f, new Vector3(2.2f, 0.3f, 0.7f), Quaternion.Euler(0f, a, 0f), matBody);
        }
        // three machines on pedestals, on show
        string[] show = { "scout", "tipper", "mauler", "ripper", "millstone", "bulwark" };
        for (int i = 0; i < 3; i++)
        {
            float a = i * 120f + 60f;
            Vector3 pos = Quaternion.Euler(0f, a, 0f) * Vector3.forward * (p.radius - 10f);
            Prim(PrimitiveType.Cylinder, t, pos + Vector3.up * 0.15f, new Vector3(4.2f, 0.15f, 4.2f), Quaternion.identity, matPlazaLight());
            var entry = EnemyRoster.Find(show[rng.Next(show.Length)]);
            if (entry == null) continue;
            RaycastWheelDrive drv;
            var bot = SpawnBot(EnemyRoster.Recipe(entry.id, palette), entry.label, t.position + pos, Quaternion.Euler(0f, a + 180f, 0f), Vector3.forward, out drv);
            if (bot != null) { bot.combatEnabled = false; bot.controlSource = ControlSource.AI; LiftToGround(bot, p.baseH + 0.9f); ch.extraBots.Add(bot); }
        }
    }
    Material matPlazaLightCached;
    Material matPlazaLight() { if (matPlazaLightCached == null) matPlazaLightCached = PartVisualFactory.Mat(new Color(0.34f, 0.36f, 0.42f), 0.4f, 0.5f); return matPlazaLightCached; }

    void BuildArenaShow(Chunk ch, Transform t, Place p, System.Random rng)
    {
        float r = 12f;
        Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0.05f, 0f), new Vector3(r * 2f, 0.05f, r * 2f), Quaternion.identity, matBody, false);
        int walls = 22;
        for (int i = 0; i < walls; i++)
        {
            float a = i * Mathf.PI * 2f / walls;
            Prim(PrimitiveType.Cube, t, new Vector3(Mathf.Cos(a) * r, 0.7f, Mathf.Sin(a) * r), new Vector3(3.6f, 1.4f, 0.5f),
                 Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f), i % 2 == 0 ? matBodyLight : matBody);
            Prim(PrimitiveType.Cube, t, new Vector3(Mathf.Cos(a) * r, 1.45f, Mathf.Sin(a) * r), new Vector3(3.6f, 0.08f, 0.55f),
                 Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f), matCyan, false);
        }
        // stands on two sides, three rows each
        for (int side = 0; side < 2; side++)
            for (int row = 0; row < 3; row++)
            {
                float z = (r + 3f + row * 1.6f) * (side == 0 ? 1f : -1f);
                Prim(PrimitiveType.Cube, t, new Vector3(0f, 0.4f + row * 0.8f, z), new Vector3(22f - row * 2f, 0.8f + row * 1.6f, 1.4f), Quaternion.identity, row == 1 ? matBodyLight : matBody);
            }
        // floodlights
        for (int i = 0; i < 4; i++)
        {
            float a = i * 90f + 45f;
            Vector3 pos = Quaternion.Euler(0f, a, 0f) * Vector3.forward * (r + 5f);
            Prim(PrimitiveType.Cylinder, t, pos + Vector3.up * 5f, new Vector3(0.3f, 5f, 0.3f), Quaternion.identity, matBodyLight);
            Prim(PrimitiveType.Sphere, t, pos + Vector3.up * 10.3f, Vector3.one * 1.2f, Quaternion.identity, matAmber, false);
        }
        var show = new ArenaShow { centre = t.position, radius = r };
        ch.arena = show;
        SpawnFighters(ch, show, rng);
    }

    void SpawnFighters(Chunk ch, ArenaShow show, System.Random rng)
    {
        float dHome = Vector2.Distance(new Vector2(show.centre.x, show.centre.z), new Vector2(homePos.x, homePos.z));
        string[] pool = dHome < 200f ? new[] { "scout", "tipper", "mauler" } : dHome < 400f ? new[] { "mauler", "ripper", "millstone" } : new[] { "bulwark", "widowmaker", "bastion", "ripper" };
        show.aId = pool[rng.Next(pool.Length)];
        show.bId = pool[rng.Next(pool.Length)];
        var ea = EnemyRoster.Find(show.aId); var eb = EnemyRoster.Find(show.bId);
        if (ea == null || eb == null) return;
        RaycastWheelDrive da, db;
        var a = SpawnBot(EnemyRoster.Recipe(ea.id, palette), ea.label, show.centre + new Vector3(-5f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f), Vector3.forward, out da);
        var b = SpawnBot(EnemyRoster.Recipe(eb.id, palette), eb.label, show.centre + new Vector3(5f, 0f, 0f), Quaternion.Euler(0f, -90f, 0f), Vector3.forward, out db);
        if (a == null || b == null) return;
        LiftToGround(a, show.centre.y + 0.8f); LiftToGround(b, show.centre.y + 0.8f);
        a.combatEnabled = true; b.combatEnabled = true;
        a.controlSource = ControlSource.AI; b.controlSource = ControlSource.AI;
        var aiA = a.gameObject.AddComponent<AIController>();
        aiA.self = a; aiA.drive = da; aiA.target = b; aiA.forwardLocal = Vector3.forward; aiA.power = a.GetComponent<PowerPlant>();
        var aiB = b.gameObject.AddComponent<AIController>();
        aiB.self = b; aiB.drive = db; aiB.target = a; aiB.forwardLocal = Vector3.forward; aiB.power = b.GetComponent<PowerPlant>();
        show.a = a; show.b = b; show.downSince = -1f; show.flipA = show.flipB = 0f;
        ch.extraBots.Add(a); ch.extraBots.Add(b);
    }

    /// <summary>The referee: when a fighter is dead, out of the ring or on its
    /// back for six seconds, a fresh pair is in five seconds later.</summary>
    void PumpArenas()
    {
        foreach (var ch in chunks.Values)
        {
            var s = ch.arena;
            if (s == null) continue;
            bool over = false;
            if (s.a == null || s.b == null || s.a.dead || s.b.dead) over = true;
            else
            {
                if (Vector3.Distance(s.a.rb.position, s.centre) > s.radius + 3f || Vector3.Distance(s.b.rb.position, s.centre) > s.radius + 3f) over = true;
                s.flipA = Vector3.Dot(s.a.transform.up, Vector3.up) < 0.2f ? s.flipA + Time.deltaTime : 0f;
                s.flipB = Vector3.Dot(s.b.transform.up, Vector3.up) < 0.2f ? s.flipB + Time.deltaTime : 0f;
                if (s.flipA > 6f || s.flipB > 6f) over = true;
            }
            if (!over) { s.downSince = -1f; continue; }
            if (s.downSince < 0f) { s.downSince = Time.time; continue; }
            if (Time.time - s.downSince < 5f) continue;
            foreach (var bot in new[] { s.a, s.b })
                if (bot != null && bot.gameObject != null) { ch.extraBots.Remove(bot); bot.gameObject.SetActive(false); Destroy(bot.gameObject); }
            s.a = null; s.b = null;
            SpawnFighters(ch, s, new System.Random(unchecked((int)(Time.time * 977f) ^ worldSeed)));
        }
    }

    void BuildPlayground(Transform t, Place p, System.Random rng)
    {
        // two ramps: a long wedge you can take at speed
        for (int i = 0; i < 2; i++)
        {
            float a = i == 0 ? 0f : 180f;
            Vector3 pos = Quaternion.Euler(0f, a, 0f) * Vector3.forward * 9f;
            Prim(PrimitiveType.Cube, t, pos + Vector3.up * 0.9f, new Vector3(5f, 0.4f, 8f), Quaternion.Euler(-13f, a, 0f), matPanel);
        }
        // balls to shove
        for (int i = 0; i < 3; i++)
        {
            float a = 60f + i * 120f;
            Vector3 pos = Quaternion.Euler(0f, a, 0f) * Vector3.forward * 12f;
            var ball = Prim(PrimitiveType.Sphere, t, pos + Vector3.up * 1.2f, Vector3.one * 2.2f, Quaternion.identity, i == 1 ? matViolet : matCyan);
            var rb = ball.AddComponent<Rigidbody>(); rb.mass = 12f; rb.linearDamping = 0.4f; rb.angularDamping = 0.5f;
            ball.name = "ball";
        }
        // the turnstile: a slow bar on a pivot that shoves what it meets
        var pivot = Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0.9f, 0f), new Vector3(0.8f, 0.9f, 0.8f), Quaternion.identity, matBodyLight);
        var bar = Prim(PrimitiveType.Cube, t, new Vector3(0f, 0.7f, 0f), new Vector3(9f, 0.5f, 0.5f), Quaternion.identity, matAmber);
        var brb = bar.AddComponent<Rigidbody>(); brb.isKinematic = true;
        bar.AddComponent<Turnstile>().speed = 35f;
        bar.name = "turnstile";
        // domes to bounce over
        for (int i = 0; i < 5; i++)
        {
            float a = (float)rng.NextDouble() * 360f, r = 16f + (float)rng.NextDouble() * 8f;
            Vector3 pos = Quaternion.Euler(0f, a, 0f) * Vector3.forward * r;
            Prim(PrimitiveType.Sphere, t, pos - Vector3.up * 1.2f, new Vector3(4f, 3f, 4f), Quaternion.identity, matPlazaLight());
        }
    }

    public class Turnstile : MonoBehaviour
    {
        public float speed = 35f; Rigidbody rb;
        void Start() { rb = GetComponent<Rigidbody>(); }
        void FixedUpdate()
        {
            if (rb == null) return;
            rb.MoveRotation(rb.rotation * Quaternion.Euler(0f, speed * Time.fixedDeltaTime, 0f));
        }
    }

    // ------------------------------------------------------------ the shop pad
    /// <summary>Drive onto a trading post's pad and the workshop opens on
    /// SHOP where you stand; DRIVE OUT brings you back to the same spot.</summary>
    void PumpShopPads(Vector3 me)
    {
        // armed only once you are clear of every pad, so coming back from the
        // shop (you resume ON the pad) does not walk you straight back in
        bool onAny = false, near = false;
        foreach (var pad in shopPads)
        {
            float d2 = new Vector2(pad.x - me.x, pad.z - me.z).sqrMagnitude;
            if (d2 < 2.2f * 2.2f) onAny = true;
            if (d2 < 5f * 5f) near = true;
        }
        if (!padArmed) { if (!near) padArmed = true; return; }
        if (onAny) EnterShop();
    }

    // bench seams
    public List<CompoundRobot> YardArenaFighters()
    {
        var l = new List<CompoundRobot>();
        foreach (var ch in chunks.Values) if (ch.arena != null) { if (ch.arena.a != null) l.Add(ch.arena.a); if (ch.arena.b != null) l.Add(ch.arena.b); }
        return l;
    }
    public List<Vector3> YardShopPads() { return new List<Vector3>(shopPads); }
    public int YardPlaceBots { get { int n = 0; foreach (var ch in chunks.Values) n += ch.extraBots.Count; return n; } }

    public void EnterShop()
    {
        if (mode != Mode.Map) return;
        LastShopOpened = true;
        RBTelemetry.Once("shop");
        LeaveMap();
        if (MobileBuilderUI.inst != null) { MobileBuilderUI.inst.SetDockOpen(true); MobileBuilderUI.inst.ShowTab(3); }
    }
}
}
