// ===========================================================================
// BuilderManager.Pool.cs — OTHER PLAYERS' MACHINES IN THE WORLD
// (Robot Brawl: Scrapyard; the CrazyGames plan, step 3, 2026-09-10).
//
// The design's §3.5/§3.7/§3.8: real players' robots parked in the world
// (the shared server's anonymous, build-only pool), a challenge you DRIVE
// against the snapshot's build under the game's AI, the verdict reported
// for YARD POINTS (POST /v1/yard/bouts, capped server-side), a board of
// accounts by points (GET /v1/yard/board), and the game's only gate: sign
// in to challenge another player's machine. Everything else stays free.
//
// The pool is fetched once per session on the first drive out; a headless
// bench injects one (TestInjectPool) and parks it where it likes
// (TestParkPool). A pool machine is a chunk's `poolBot`, beside its roster
// `enemy`; the compass and the card treat both alike, the card names the
// owner, and CHALLENGE routes here.
// ===========================================================================
using System.Collections.Generic;
using UnityEngine;

namespace RobotBrawl.Phase0
{
public partial class BuilderManager
{
    readonly List<LadderClient.PoolEntry> pool = new List<LadderClient.PoolEntry>();
    bool poolFetching, poolFetched;
    public const float POOL_MIN_DIST = 60f;      // pool machines park beyond this from home
    public const int POOL_CHUNK_PCT = 45;        // ...in this share of chunks out there

    List<PlacedPart> yardOpponentParts; string yardOpponentLabel = "";
    string pendingBoutDefender; bool boutPosted; string pendingToast;
    public string LastBoutPosted { get; private set; }
    public bool LastBoutWon { get; private set; }
    public bool SignInWanted { get; private set; }
    public int PoolCount { get { return pool.Count; } }

    // ---- bench seams
    public void TestInjectPool(List<LadderClient.PoolEntry> entries) { pool.Clear(); pool.AddRange(entries); poolFetched = true; }
    public CompoundRobot TestParkPool(LadderClient.PoolEntry e, Vector3 xz)
    {
        Chunk ch;
        if (!chunks.TryGetValue(Key(ChunkOf(xz.x), ChunkOf(xz.z)), out ch)) return null;
        return SpawnPoolBot(ch, e, xz, 180f);
    }
    public void TestUnparkPool()
    {
        foreach (var c in chunks.Values)
            if (c.poolBot != null) { if (c.poolBot.gameObject != null) { c.poolBot.gameObject.SetActive(false); Destroy(c.poolBot.gameObject); } c.poolBot = null; c.poolEntry = null; }
        if (cardBot == null || cardBot.gameObject == null) { yardCard = false; cardBot = null; }
    }

    /// <summary>Once per session: ask the server for a dozen strangers' machines.
    /// Anonymous. Offline or in the editor with no dev API, the pool is simply
    /// empty and the world is roster machines only.</summary>
    void FetchPoolOnce()
    {
        if (poolFetched || poolFetching) return;
        poolFetching = true;
        StartCoroutine(LadderClient.Pool(null, 12, (rows, err) =>
        {
            poolFetching = false; poolFetched = true;
            if (rows != null) { pool.Clear(); pool.AddRange(rows); }
        }));
    }

    /// <summary>A snapshot's build text as parts, the way LoadSnapshot reads it
    /// (id|x,y,z|yaw|ax,ay,az|Material|G:mask) but without touching the
    /// builder. Null for a build with no core or no wheel - a machine that
    /// cannot stand or roll is not a machine, whatever the server sent.</summary>
    public List<PlacedPart> SnapshotParts(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        if (palette == null) palette = P1PartDef.Palette();
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        const System.Globalization.NumberStyles NumStyle =
            System.Globalization.NumberStyles.AllowLeadingSign | System.Globalization.NumberStyles.AllowDecimalPoint | System.Globalization.NumberStyles.AllowExponent;
        var parts = new List<PlacedPart>();
        bool core = false, wheel = false;
        foreach (var raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var f = line.Split('|');
            if (f.Length < 4) continue;
            if (f[0] == "edgesentinel") f[0] = "wallsensor";
            P1PartDef def = null;
            foreach (var d in palette) if (d.id == f[0]) { def = d; break; }
            if (def == null) continue;
            var c = f[1].Split(','); var ax = f[3].Split(',');
            if (c.Length < 3 || ax.Length < 3) continue;
            float px, py, pz, axx, axy, axz; int yaw;
            if (!float.TryParse(c[0], NumStyle, inv, out px) || !float.TryParse(c[1], NumStyle, inv, out py) || !float.TryParse(c[2], NumStyle, inv, out pz)
             || !int.TryParse(f[2], System.Globalization.NumberStyles.Integer, inv, out yaw)
             || !float.TryParse(ax[0], NumStyle, inv, out axx) || !float.TryParse(ax[1], NumStyle, inv, out axy) || !float.TryParse(ax[2], NumStyle, inv, out axz)) continue;
            if (float.IsNaN(px) || float.IsNaN(py) || float.IsNaN(pz) || float.IsInfinity(px) || float.IsInfinity(py) || float.IsInfinity(pz)) continue;
            string mat = f.Length >= 5 ? f[4].Trim() : null;
            if (!string.IsNullOrEmpty(mat)) { string canon = MatDB.Canon(mat); mat = canon ?? null; }
            var p = new PlacedPart { def = def, pos = new Vector3(px, py, pz), yaw = yaw, wheelAxis = new Vector3(axx, axy, axz), matName = mat };
            if (f.Length >= 6)
            {
                string gf = f[5].Trim(); int mask = 0;
                if (gf == "G") mask = 63; else if (gf.StartsWith("G:") && int.TryParse(gf.Substring(2), out mask)) mask &= 63;
                if (def.id != "core") p.gussetFaces = mask;
            }
            if (def.id == "core") core = true;
            if (def.category == P1Category.Mobility) wheel = true;
            parts.Add(p);
        }
        if (!core || !wheel || parts.Count < 2) return null;
        // the core first: SpawnBot reads build[0] as the core
        parts.Sort((a, b) => (a.def.id == "core" ? 0 : 1) - (b.def.id == "core" ? 0 : 1));
        return parts;
    }

    /// <summary>Park a pool machine in a chunk: the same spawn path as the
    /// roster's, under the owner's robot name, combat off, damped.</summary>
    CompoundRobot SpawnPoolBot(Chunk ch, LadderClient.PoolEntry e, Vector3 xz, float yaw)
    {
        var parts = SnapshotParts(e.build);
        if (parts == null) return null;
        RaycastWheelDrive drv;
        var bot = SpawnBot(parts, string.IsNullOrEmpty(e.robotName) ? "VISITOR" : e.robotName, new Vector3(xz.x, 0f, xz.z), Quaternion.Euler(0f, yaw, 0f), Vector3.forward, out drv);
        if (bot == null) return null;
        bot.combatEnabled = false;
        bot.controlSource = ControlSource.AI;
        LiftToGround(bot, TerrainHeight(xz.x, xz.z) + 0.6f);
        ParkBot(bot);
        ch.poolBot = bot; ch.poolEntry = e;
        return bot;
    }

    /// <summary>Called by BuildChunk after the roster enemy: beyond POOL_MIN_DIST,
    /// some chunks also hold a stranger's machine, chosen by the chunk's seed.</summary>
    void MaybeSpawnPoolBot(Chunk ch, int cx, int cz, float dHome, System.Random rng)
    {
        if (pool.Count == 0 || dHome < POOL_MIN_DIST) return;
        if (rng.Next(100) >= POOL_CHUNK_PCT) return;
        var e = pool[rng.Next(pool.Count)];
        float x0 = cx * CHUNK, z0 = cz * CHUNK;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            float x = x0 + 6f + (float)rng.NextDouble() * (CHUNK - 12f), z = z0 + 6f + (float)rng.NextDouble() * (CHUNK - 12f);
            if (InsidePlace(x, z, 3f)) continue;
            SpawnPoolBot(ch, e, new Vector3(x, 0f, z), (float)rng.NextDouble() * 360f);
            return;
        }
    }

    LadderClient.PoolEntry PoolEntryOf(CompoundRobot bot)
    {
        if (bot == null) return null;
        foreach (var c in chunks.Values) if (c.poolBot == bot) return c.poolEntry;
        return null;
    }
    public LadderClient.PoolEntry CardPoolEntry { get { return yardCard ? PoolEntryOf(cardBot) : null; } }

    /// <summary>CHALLENGE on a stranger's machine: the game's one gate, then the
    /// bout against their build, the verdict reported at the bell.</summary>
    bool ChallengePool()
    {
        var e = CardPoolEntry;
        if (e == null) return false;
        if (!LadderClient.SignedIn)
        {
            SignInWanted = true;
            if (MapHudUI.inst != null) MapHudUI.inst.ShowSignIn();
            return true;
        }
        SignInWanted = false;
        var parts = SnapshotParts(e.build);
        if (parts == null) { yardToast = "that machine would not stand - the server sent a bad build"; yardToastT = 3f; return true; }
        yardOpponentParts = parts; yardOpponentLabel = string.IsNullOrEmpty(e.robotName) ? "VISITOR" : e.robotName;
        pendingBoutDefender = e.snapshotId; boutPosted = false;
        StartYardFight(YARD_BOT);          // the roster id is only the tier's placeholder; the parts above are the machine
        yardOpponentParts = null;
        if (mode != Mode.Fight) pendingBoutDefender = null;
        return true;
    }

    /// <summary>After sign-in from the card: the challenge the player was after.</summary>
    public void OnSignedIn(string displayName)
    {
        yardToast = "signed in as " + displayName; yardToastT = 3f;
        if (SignInWanted && mode == Mode.Map && yardCard) ChallengePool();
        SignInWanted = false;
    }

    /// <summary>At the bell of a pool bout: report the referee's verdict once.
    /// The toast waits for the map (the debrief owns the screen until then).</summary>
    void PumpBoutReport(FightManager fm)
    {
        if (fm == null || fm.state != FightManager.State.Ended || pendingBoutDefender == null || boutPosted) return;
        boutPosted = true;
        bool won = fm.outcome == FightManager.Outcome.PlayerWin;
        LastBoutPosted = pendingBoutDefender; LastBoutWon = won;
        string defender = pendingBoutDefender; pendingBoutDefender = null;
        if (!LadderClient.SignedIn) return;
        StartCoroutine(LadderClient.YardBout(defender, won, (r, err) =>
        {
            if (r == null) pendingToast = "bout not scored: " + err;
            else if (r.awarded > 0) pendingToast = "+" + r.awarded + " YARD POINTS  ·  " + r.points + " total";
            else pendingToast = "bout recorded  ·  today's points are capped";
        }));
    }
    void FlushPendingToast()
    {
        if (string.IsNullOrEmpty(pendingToast)) return;
        yardToast = pendingToast; yardToastT = 4f; pendingToast = null;
    }
}
}
