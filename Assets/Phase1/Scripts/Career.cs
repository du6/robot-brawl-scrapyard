using System.Collections.Generic;
using UnityEngine;

namespace RobotBrawl.Phase0
{
// ============================================================================
// CAREER MODE — Phase C0 data foundations (2026-07-31).
// Design source: claude/Career_Mode_Design_Doc.md v1.5. CareerDB is the ONE
// place every economy/league constant lives, so a tuning round edits a table,
// not a codebase. No UI in this file, ever.
// ============================================================================

public static class CareerDB
{
    // ---- reward math (doc section 5) ----
    public const float UNDERDOG_K = 0.6f;      // bonus slope vs value gap
    public const float UNDERDOG_CAP = 1.6f;    // never above 1.6x (owen, resolved)
    public const float REENTRY_FRAC = 0.4f;    // re-entering a beaten contest
    public const int FIRST_WIN_BONUS = 150;    // one-time, per contest
    public const float WIN_DMG_K = 0.5f;       // + min(dealt,400) * this on wins
    public const float WIN_DMG_CAP = 400f;
    public const int LOSS_BASE = 40;           // consolation floor
    public const float LOSS_DMG_K = 0.3f;
    public const float LOSS_DMG_CAP = 300f;
    public const int LOSS_MAX = 150;

    public class Contest
    {
        public string id; public string oppId; public AiTier tier;
        public int purse; public int entryFee;
        public Contest(string i, string o, AiTier t, int p, int f)
        { id = i; oppId = o; tier = t; purse = p; entryFee = f; }
    }

    public class League
    {
        public string id; public string name; public string arenaName; public string arenaId;
        public float weightCap; public Vector3 sizeBox;
        public Contest[] contests;
        public League(string i, string n, string an, string aid, float cap, Vector3 box, Contest[] c)
        { id = i; name = n; arenaName = an; arenaId = aid; weightCap = cap; sizeBox = box; contests = c; }
    }

    /// <summary>Doc sections 4 + 4b, verbatim. Arena ids consumed by C3A.</summary>
    public static readonly League[] Leagues =
    {
        new League("L1", "Scrapyard Open", "The Yard", "yard", 1500f, new Vector3(2.0f, 1.5f, 2.0f), new[] {
            new Contest("L1C1", "scout",  AiTier.Rookie, 250, 0),
            new Contest("L1C2", "tipper", AiTier.Rookie, 300, 0) }),
        new League("L2", "Garage League", "The Loading Dock", "dock", 2000f, new Vector3(2.0f, 1.5f, 2.0f), new[] {
            new Contest("L2C1", "mauler", AiTier.Rookie,  400, 0),
            new Contest("L2C2", "scout",  AiTier.Veteran, 450, 0),
            new Contest("L2C3", "tipper", AiTier.Veteran, 500, 0) }),
        new League("L3", "Regional Circuit", "The Sawmill", "sawmill", 2800f, new Vector3(2.5f, 1.8f, 2.5f), new[] {
            new Contest("L3C1", "bulwark", AiTier.Veteran, 700, 50),
            new Contest("L3C2", "mauler",  AiTier.Veteran, 800, 50),
            new Contest("L3C3", "ripper",  AiTier.Veteran, 900, 50) }),
        new League("L4", "National Series", "The Press", "press", 4000f, new Vector3(2.5f, 1.8f, 2.5f), new[] {
            new Contest("L4C1", "widowmaker", AiTier.Veteran,  1200, 100),
            new Contest("L4C2", "bulwark",    AiTier.Champion, 1400, 100),
            new Contest("L4C3", "ripper",     AiTier.Champion, 1600, 100) }),
        new League("L5", "World Championship", "The Crucible", "crucible", 5500f, new Vector3(3.0f, 2.0f, 3.0f), new[] {
            new Contest("L5C1", "widowmaker", AiTier.Champion, 3000, 200) }),
    };

    public class KitItem { public string partId; public string mat; public int count;
        public KitItem(string p, string m, int c) { partId = p; mat = m; count = c; } }

    /// <summary>Doc section 6. Core is NOT inventory: free, unlimited,
    /// unsellable — every robot needs one and the shop never touches it.</summary>
    public static KitItem[] StarterKit()
    {
        return new[]
        {
            new KitItem("beam",     "Aluminum", 6),
            new KitItem("beamlong", "Aluminum", 1),
            new KitItem("bracket",  "Aluminum", 4),
            new KitItem("chassis",  "Aluminum", 1),
            new KitItem("plate",    "ABS",      2),
            new KitItem("wheel",    "Rubber",   4),   // R2: the wheel def PINS itself to Rubber
                                                      // (materialChoice=false) and Rubber is deliberately
                                                      // absent from MatDB.Order, so an "Aluminum" grant was
                                                      // inventory no chip could ever reveal. See ResolveMat.
            new KitItem("battery",  "Aluminum", 1),
            new KitItem("gyro",     "Aluminum", 1),
            new KitItem("spindle",  "Aluminum", 1),
            new KitItem("wedge",    "Steel",    1),
            new KitItem("spike",    "Steel",    1),
        };
    }

    /// <summary>R2 (critic finding 2): a kit line naming a material the part
    /// cannot actually hold grants stock that the palette, the shop and the
    /// placement gate all look up under the PINNED key and correctly read as
    /// 0 - while the PARTS tab lists the raw inventory row and reads 4. That
    /// is how a brand-new player came to own four wheels they could not see,
    /// place or replace. Every grant now resolves through the def's own rules,
    /// so the data cannot drift out of the part table again.</summary>
    public static string ResolveMat(string partId, string mat)
    {
        var d = Def(partId);
        return d != null ? d.EffectiveMat(mat) : mat;
    }

    static Dictionary<string, P1PartDef> defs;
    public static P1PartDef Def(string partId)
    {
        if (defs == null)
        {
            defs = new Dictionary<string, P1PartDef>();
            foreach (var d in P1PartDef.Palette()) defs[d.id] = d;
        }
        return defs.ContainsKey(partId) ? defs[partId] : null;
    }

    /// <summary>Shop price: the existing physics-derived CostOf. Pinned parts
    /// resolve their own material internally. Core is priceless (0).</summary>
    /// <summary>Tech floors: pinned/functional parts are TECHNOLOGY, not
    /// tonnage - a wheel is a motor and hub, not 8 scrap of rubber. Raw
    /// CostOf undervalued them (wheel 8, battery 42); floors restore the
    /// doc section 6 economy.</summary>
    static readonly Dictionary<string, int> TechFloor = new Dictionary<string, int>
    { { "wheel", 60 }, { "battery", 120 }, { "gyro", 110 }, { "pivot", 150 }, { "spindle", 150 }, { "ram", 150 } };

    public static int PartPrice(string partId, string mat)
    {
        if (partId == "core") return 0;
        var d = Def(partId);
        int basePrice = d != null ? d.CostOf(mat) : 0;
        int floor;
        if (TechFloor.TryGetValue(partId, out floor)) return Mathf.Max(basePrice, floor);
        return basePrice;
    }

    public static int SellPrice(string partId, string mat)
    { return Mathf.RoundToInt(PartPrice(partId, mat) * 0.5f); }

    public static int KitValue()
    {
        int total = 0;
        foreach (var k in StarterKit()) total += PartPrice(k.partId, k.mat) * k.count;
        return total;
    }

    /// <summary>Win settlement (doc section 5). buildValue/oppValue are the
    /// robots' summed part prices; reEntry = contest already beaten.</summary>
    public static int WinPay(Contest c, float dealt, int buildValue, int oppValue, bool reEntry, bool firstWin)
    {
        float mult = 1f + UNDERDOG_K * Mathf.Clamp01((float)oppValue / Mathf.Max(1, buildValue) - 1f);
        mult = Mathf.Min(mult, UNDERDOG_CAP);
        float purse = c.purse * (reEntry ? REENTRY_FRAC : 1f);
        int pay = Mathf.RoundToInt(purse * mult + Mathf.Min(dealt, WIN_DMG_CAP) * WIN_DMG_K);
        if (firstWin) pay += FIRST_WIN_BONUS;
        return pay;
    }

    public static int LossPay(float dealt)
    { return Mathf.Min(LOSS_MAX, Mathf.RoundToInt(LOSS_BASE + LOSS_DMG_K * Mathf.Min(dealt, LOSS_DMG_CAP))); }
}

[System.Serializable] public class CareerItem { public string partId; public string mat; public int count; }
[System.Serializable] public class CareerRobot
{
    public string name; public string snapshot;
    public int wins; public int losses; public int titles;
    public float damageDealt;
    public List<string> leagueHistory = new List<string>();
}
[System.Serializable] public class CareerBlueprint { public string name; public string snapshot; }
/// <summary>MEDALS (2026-08-02, owen: "user should win medal awards for each
/// league campaign, and be able to check their medals in history"). ONE
/// criterion, by his choice: league champion = every contest in the league
/// won at least once. He explicitly declined retroactive backfill, perfect-
/// campaign medals and title-defence medals - do not add them here.
///
/// This is a plain [System.Serializable] class hanging off a new List on
/// CareerData, so JsonUtility gives every save written before today an empty
/// list. That IS the migration: no version bump, no repair pass, and no
/// backfill - which is exactly what owen asked for.</summary>
[System.Serializable] public class CareerMedal
{
    public int leagueIndex;          // index into CareerDB.Leagues at the time
    public string leagueId;          // "L2" - survives a re-ordered table
    public string leagueName;        // "Garage League"
    public string arenaName;         // "The Loading Dock"
    public string robot;             // the robot that campaigned
    public string when;              // ISO yyyy-MM-dd
    public int wins, losses;         // that robot's record when it was won
    public int contests;             // how many contests the campaign was
}
[System.Serializable] public class CareerTxn { public string when; public int delta; public string cause; }

[System.Serializable] public class CareerData
{
    public int scrap;
    public List<CareerItem> inventory = new List<CareerItem>();
    public List<CareerRobot> stable = new List<CareerRobot>();
    public List<CareerBlueprint> blueprints = new List<CareerBlueprint>();
    public List<string> doneContests = new List<string>();   // first-win flags
    public List<CareerTxn> txns = new List<CareerTxn>();
    /// <summary>Medals (2026-08-02). Empty on every pre-existing save by
    /// construction - see CareerMedal.</summary>
    public List<CareerMedal> medals = new List<CareerMedal>();
    public int fights; public int fightWins; public int sessions;   // telemetry
    public int tutorialStep;
    /// <summary>C4: index into stable of the robot being edited; -1 = none.</summary>
    public int activeRobot = -1;
    /// <summary>OWEN 2026-08-02: index into blueprints of the draft being
    /// edited; -1 = none. Blueprints had no such notion, which is why
    /// BlueprintSave could only ever APPEND - opening a draft and saving it
    /// twice produced two copies of it. Mutually exclusive with activeRobot:
    /// you are editing a machine or a design, never both.
    ///
    /// The -1 initialiser is load-bearing. JsonUtility runs the constructor
    /// before populating, so a save written before this field existed keeps -1
    /// rather than defaulting to 0 and silently "opening" blueprint zero.
    /// activeRobot has always relied on the same thing.</summary>
    public int activeBlueprint = -1;
    public bool kitGranted;
    public int kitVersion;     // R2: 0 on every save older than Career.KitVersion
    // ---- C6.4 telemetry (design doc §14): LOCAL ONLY, no network - the
    // schema is the point. fights/fightWins/sessions above predate this;
    // these two complete the v1 set.
    public List<int> scrapCurve = new List<int>();   // scrap after each settle (cap 200)
    public string lastContest = "";                  // last contest attempted
}

/// <summary>Career profile lifecycle + inventory arithmetic. UI lives in
/// later phases; this class only knows facts. `devFreeBuild` preserves the
/// old unrestricted boot for TouchSmoke/AimProbe/critic runs.</summary>
public static class Career
{
    /// <summary>HARNESS OVERRIDE ONLY. CareerBench and MedalDev force unlimited
    /// parts for a whole run; nothing the player can touch sets this any more.
    /// Read `Drafting`, not this.</summary>
    public static bool devFreeBuild;

    /// <summary>OWEN 2026-08-02: "given that there is a 'new robot' and 'new
    /// draft' buttons, should we remove the 'draft mode' button?" - yes, and
    /// the reason is that the toggle was the ONLY thing that could decouple two
    /// ideas the rest of the game keeps in lockstep: WHAT you are editing (a
    /// machine or a design) and WHETHER parts are unlimited.
    ///
    /// Both states it uniquely reached were bugs:
    ///   draft ON  + editing a robot     -> a fieldable machine silently
    ///       accepting parts you do not own. This is how a stable filled with
    ///       robots that refuse at the LEAGUE tab.
    ///   draft OFF + editing a blueprint -> editing a design without the parts
    ///       that make it one.
    ///
    /// So the mode is now DERIVED and cannot drift: you are drafting exactly
    /// when a design is open. NEW DRAFT and OPEN put you there, NEW ROBOT and
    /// EDIT take you out, CONVERT buys the difference.</summary>
    public static bool Drafting
    {
        get
        {
            if (devFreeBuild) return true;                 // harness override
            if (!active || Data == null) return false;
            return Data.activeBlueprint >= 0 && Data.activeBlueprint < Data.blueprints.Count;
        }
    }
        /// <summary>C1: the builder consults the career inventory only when
        /// this is on. Off = today's sandbox behaviour, untouched.</summary>
        public static bool active;
    public static CareerData Data = new CareerData();

    static string PathFile { get { return Application.persistentDataPath + "/robotbrawl_career.json"; } }

    public static void Load()
    {
        try
        {
            if (System.IO.File.Exists(PathFile))
                Data = JsonUtility.FromJson<CareerData>(System.IO.File.ReadAllText(PathFile)) ?? new CareerData();
            else Data = new CareerData();
        }
        catch (System.Exception e) { Debug.LogWarning("Career.Load failed: " + e.Message); Data = new CareerData(); }
        if (!Data.kitGranted) GrantStarterKit();
        MigrateInventory();
    }

    /// <summary>R2 (critic finding 2). Runs on EVERY load, OUTSIDE the
    /// kitGranted gate, because the saves that need repairing are exactly
    /// the ones that already granted their kit. kitVersion is a new int on
    /// a plain [System.Serializable] class, so it deserialises to 0 on every
    /// JSON written before today - that 0 is the migration trigger.
    /// Any inventory row whose material the part cannot actually hold is
    /// moved onto the key the rest of the game looks it up under, so the
    /// stock stops being invisible instead of being deleted.</summary>
    /// <summary>R4 (critic finding 5) took this to 2: saved robots now hold
    /// their parts, so a career written under the old rule has to be
    /// grandfathered on load or it becomes illegal the moment it opens.</summary>
    public const int KitVersion = 2;
    public static void MigrateInventory()
    {
        if (Data.kitVersion >= KitVersion) return;
        int moved = 0;
        for (int i = Data.inventory.Count - 1; i >= 0; i--)
        {
            var it = Data.inventory[i];
            var d = CareerDB.Def(it.partId);
            if (d == null) continue;
            string pinned = d.EffectiveMat(it.mat);
            if (pinned == it.mat) continue;
            Data.inventory.RemoveAt(i);
            AddItem(it.partId, pinned, it.count);   // iterating downward, so appending is safe
            moved += it.count;
        }
        // R4 (critic finding 5). Building used to consume nothing but the
        // robot open in the editor, so a legal old save can have two robots
        // sharing one battery. Under the section-7 shared pool that save is
        // suddenly short. Do NOT let a rule change take a player's robots
        // away: top the pool up to whatever the existing stable already
        // holds, once, and let every purchase after this be honest.
        var need = new Dictionary<string, int>();
        for (int r = 0; r < Data.stable.Count; r++)
            foreach (var kv in SnapshotUsage(Data.stable[r].snapshot))
            {
                var f = kv.Key.Split('|');
                var d2 = CareerDB.Def(f[0]); if (d2 == null) continue;
                string k2 = f[0] + "|" + d2.EffectiveMat(f[1]);
                int n2; need.TryGetValue(k2, out n2); need[k2] = n2 + kv.Value;
            }
        int granted = 0;
        foreach (var kv in need)
        {
            var f = kv.Key.Split('|');
            int have = CountOf(f[0], f[1]);
            if (have < kv.Value) { AddItem(f[0], f[1], kv.Value - have); granted += kv.Value - have; }
        }
        usageSig = "\u0000";   // the stable just changed shape under the cache
        Data.kitVersion = KitVersion;
        if (moved > 0) Debug.Log("Career: moved " + moved + " unreachable inventory unit(s) onto their pinned material.");
        if (granted > 0) Debug.Log("Career: granted " + granted + " part(s) so the existing stable still fits the shared pool (R4 finding 5 migration).");
        if (autosave) Save();
    }

    public static void Save()
    { System.IO.File.WriteAllText(PathFile, JsonUtility.ToJson(Data)); }

    public static void GrantStarterKit()
    {
        foreach (var k in CareerDB.StarterKit())
            AddItem(k.partId, CareerDB.ResolveMat(k.partId, k.mat), k.count);
        Data.kitGranted = true;
        Txn(0, "starter kit granted");
        if (autosave) Save();
    }

    public static void Txn(int delta, string cause)
    {
        Data.scrap += delta;
        Data.txns.Add(new CareerTxn { when = System.DateTime.UtcNow.ToString("s"), delta = delta, cause = cause });
    }

    public static int CountOf(string partId, string mat)
    {
        foreach (var it in Data.inventory)
            if (it.partId == partId && it.mat == mat) return it.count;
        return 0;
    }

    public static void AddItem(string partId, string mat, int n)
    {
        foreach (var it in Data.inventory)
            if (it.partId == partId && it.mat == mat) { it.count += n; return; }
        Data.inventory.Add(new CareerItem { partId = partId, mat = mat, count = n });
    }

    public static bool TryConsume(string partId, string mat, int n)
    {
        foreach (var it in Data.inventory)
            if (it.partId == partId && it.mat == mat)
            {
                if (it.count < n) return false;
                it.count -= n;
                return true;
            }
        return false;
    }

    // ---- C2: shop operations - the ONLY code that mutates the inventory
    // (building uses derived accounting and never touches it). Every scrap
    // movement goes through Txn, so the ledger always audits: for a fresh
    // career, TxnSum() == scrap at all times.

    /// <summary>Why the last shop action was refused - the UI turns this amber.</summary>
    public static string shopMsg = "";
    /// <summary>Harness seam: CareerSmoke turns this off so test purchases
    /// never touch the owner's career file.</summary>
    public static bool autosave = true;

    public static bool TryBuy(string partId, string mat)
    {
        shopMsg = "";
        if (partId == "core") { shopMsg = "The core is not for sale."; return false; }
        int price = CareerDB.PartPrice(partId, mat);
        if (Data.scrap < price)
        { shopMsg = "Not enough scrap \u2014 " + price + " needed, " + Data.scrap + " held."; return false; }
        AddItem(partId, mat, 1);
        Txn(-price, "buy " + partId + " " + mat);
        if (autosave) Save();
        return true;
    }

    public static bool TrySell(string partId, string mat)
    {
        shopMsg = "";
        if (partId == "core") { shopMsg = "The core cannot be sold."; return false; }
        if (!TryConsume(partId, mat, 1)) { shopMsg = "None owned to sell."; return false; }
        Txn(CareerDB.SellPrice(partId, mat), "sell " + partId + " " + mat);
        if (autosave) Save();
        return true;
    }

    /// <summary>C2: converting one owned unit to another material costs the
    /// price delta (never negative - no refund on downgrades) plus a 10%
    /// workshop fee on the TARGET price, minimum 3. Upgrades stay cheaper
    /// than sell-at-50% + rebuy; downgrades are deliberately not free money.</summary>
    public static int SwapCost(string partId, string fromMat, string toMat)
    {
        int oldP = CareerDB.PartPrice(partId, fromMat);
        int newP = CareerDB.PartPrice(partId, toMat);
        return Mathf.Max(0, newP - oldP) + Mathf.Max(3, Mathf.RoundToInt(newP * 0.10f));
    }

    public static bool TrySwap(string partId, string fromMat, string toMat)
    {
        shopMsg = "";
        if (partId == "core") { shopMsg = "The core cannot be reworked."; return false; }
        if (fromMat == toMat) { shopMsg = "Already that material."; return false; }
        if (CountOf(partId, fromMat) <= 0) { shopMsg = "None owned in " + fromMat + " to rework."; return false; }
        int cost = SwapCost(partId, fromMat, toMat);
        if (Data.scrap < cost)
        { shopMsg = "Not enough scrap \u2014 " + cost + " needed, " + Data.scrap + " held."; return false; }
        TryConsume(partId, fromMat, 1);
        AddItem(partId, toMat, 1);
        Txn(-cost, "swap " + partId + " " + fromMat + ">" + toMat);
        if (autosave) Save();
        return true;
    }

    /// <summary>Ledger audit: sum of every transaction delta. Equals scrap
    /// exactly on any career whose scrap only ever moved through Txn.</summary>
    public static int TxnSum()
    {
        int t = 0;
        foreach (var x in Data.txns) t += x.delta;
        return t;
    }

    // ---- C3: contests. Transient fight context + settlement on the CAREER
    // ledger; Progression.OnMatchEnd routes here when a contest is live, so
    // a contest fight never touches the sandbox profile.
    public static string activeLeague, activeContest;   // null = no contest live
    public static int fightBuildValue, fightOppValue;   // captured at enrollment
    /// <summary>Which league the builder's weight readout targets - set by
    /// the last FIGHT/SCOUT tap.</summary>
    public static int targetLeagueIdx = 0;
    public static string lastResultLine = "";
    /// <summary>ROUND-3 FIX (critic CRITICAL 2b, the money): what the last
    /// contest settlement actually paid, so the results screen can print the
    /// arithmetic (purse / bonus / entry fee / net) instead of a bare "+N"
    /// that hid a 200-scrap entry fee and turned a -160 match into "+40".
    /// FightManager.End() clears `lastSettled` immediately before calling
    /// Progression.OnMatchEnd, so a stale contest can never leak onto an
    /// exhibition result.</summary>
    public static bool lastSettled;
    public static int lastPay;
    /// <summary>The medal SettleFight just minted, or null. Same one-shot
    /// handoff contract as lastSettled/lastPay: FightManager.End() clears it
    /// immediately before Progression.OnMatchEnd() and reads it straight
    /// after, so the results screen can announce a championship without
    /// re-deriving one (and without announcing an old one).</summary>
    public static CareerMedal lastMedal;

    /// <summary>C4: per-(part|mat) usage of a saved snapshot - the PARTS
    /// shelf's "who is borrowing what". Core excluded; v1 snapshots (no
    /// material field) are skipped rather than guessed.</summary>
    public static Dictionary<string, int> SnapshotUsage(string snapshot)
    {
        var u = new Dictionary<string, int>();
        if (string.IsNullOrEmpty(snapshot)) return u;
        foreach (var line in snapshot.Split('\n'))
        {
            var f = line.Split('|');
            if (f.Length < 5 || f[0] == "core") continue;
            string key = f[0] + "|" + f[4].Trim();
            int n; u.TryGetValue(key, out n);
            u[key] = n + 1;
        }
        return u;
    }

    // ---- R4 (critic finding 5): the inventory is a SHARED POOL ----------
    static string usageSig = "\u0000";
    static readonly Dictionary<string, int> usageCache = new Dictionary<string, int>();

    /// <summary>Cache key. Cheap to build and derived from the data itself, so
    /// there is no invalidation call for a future round to forget - a stable
    /// that changed shape can never be served a stale total.</summary>
    static string StableSig(int exceptSlot)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(exceptSlot).Append('/').Append(Data.stable.Count);
        for (int i = 0; i < Data.stable.Count; i++)
        {
            string sn = Data.stable[i].snapshot;
            sb.Append('|').Append(sn == null ? 0 : sn.Length).Append(':').Append(sn == null ? 0 : sn.GetHashCode());
        }
        return sb.ToString();
    }

    /// <summary>Doc section 7: "Inventory is a shared pool across garage slots:
    /// parts are owned, builds borrow them. Two saved builds can't use the same
    /// six beams simultaneously." Until R4 nothing but the build open in the
    /// editor consumed anything, so the PARTS shelf rendered `own 1` against
    /// `Rustbucket x1 . Ledger x1 . editing x1` - three exclusive claims on one
    /// battery, an arithmetic impossibility printed as a feature.
    ///
    /// The accounting is still DERIVED. The inventory list is never mutated by
    /// building, so place / remove / undo / load / retire cannot drift the
    /// counts and no new mutable state enters the save file - the only thing
    /// that changed is that the sum now runs over the WHOLE stable instead of
    /// over the editor alone. (Deducting on save and refunding on RETIRE was
    /// the other option and is strictly worse here: every crash, every undo and
    /// every abandoned edit becomes a way to leak or duplicate parts.)
    ///
    /// exceptSlot is the stable slot currently loaded in the editor. Its saved
    /// snapshot is SUPERSEDED by the live build, so counting both is exactly
    /// the double claim that put "editing x1" on top of the robot being edited.
    /// Pass -1 when nothing is loaded.</summary>
    public static Dictionary<string, int> CommittedUsage(int exceptSlot)
    {
        string sig = StableSig(exceptSlot);
        if (sig == usageSig) return usageCache;
        usageCache.Clear();
        for (int i = 0; i < Data.stable.Count; i++)
        {
            if (i == exceptSlot) continue;
            foreach (var kv in SnapshotUsage(Data.stable[i].snapshot))
            {
                var f = kv.Key.Split('|');
                string c = MatDB.Canon(f[1]);
                string k = f[0] + "|" + (c == null ? f[1] : c);
                int n; usageCache.TryGetValue(k, out n);
                usageCache[k] = n + kv.Value;
            }
        }
        usageSig = sig;
        return usageCache;
    }

    /// <summary>Units of (part | material) held by SAVED robots other than the
    /// one open in the editor. The editor's own build is counted by the caller
    /// straight off `placed[]`.</summary>
    public static int CommittedCount(string partId, string mat, int exceptSlot)
    {
        string c = MatDB.Canon(mat);
        int n;
        return CommittedUsage(exceptSlot).TryGetValue(partId + "|" + (c == null ? mat : c), out n) ? n : 0;
    }

    public static CareerDB.League FindLeague(string id)
    { foreach (var l in CareerDB.Leagues) if (l.id == id) return l; return null; }
    public static CareerDB.Contest FindContest(CareerDB.League lg, string id)
    { if (lg == null) return null; foreach (var c in lg.contests) if (c.id == id) return c; return null; }

    /// <summary>League 1 is open; league N opens when every contest of league
    /// N-1 has been beaten at least once.</summary>
    public static bool LeagueUnlocked(int li)
    {
        if (li <= 0) return true;
        if (li >= CareerDB.Leagues.Length) return false;
        foreach (var c in CareerDB.Leagues[li - 1].contests)
            if (!Data.doneContests.Contains(c.id)) return false;
        return true;
    }

    /// <summary>True when league `li` already has a medal. The ONLY guard
    /// against double-awarding, and it is checked before anything is written,
    /// so a settle path that somehow runs twice still mints exactly one.</summary>
    public static bool HasMedal(int li)
    {
        for (int i = 0; i < Data.medals.Count; i++)
            if (Data.medals[i].leagueIndex == li) return true;
        return false;
    }

    public static CareerMedal MedalFor(int li)
    {
        for (int i = 0; i < Data.medals.Count; i++)
            if (Data.medals[i].leagueIndex == li) return Data.medals[i];
        return null;
    }

    /// <summary>Award the league-champion medal for league `li` IF the league
    /// is now complete and has no medal yet. Returns the new medal, or null
    /// when nothing was awarded - the caller uses that to decide whether to
    /// announce anything.
    ///
    /// Called from SettleFight on a FIRST win only. That gate is not
    /// decoration: without it, a player whose save already completed a league
    /// before medals existed would mint one the next time they re-entered a
    /// contest there, which is retroactive backfill by the back door and owen
    /// declined backfill.</summary>
    public static CareerMedal AwardLeagueMedal(int li)
    {
        if (li < 0 || li >= CareerDB.Leagues.Length) return null;
        var lg = CareerDB.Leagues[li];
        foreach (var cc in lg.contests)
            if (!Data.doneContests.Contains(cc.id)) return null;   // campaign unfinished
        if (HasMedal(li)) return null;                             // never twice
        var m = new CareerMedal();
        m.leagueIndex = li; m.leagueId = lg.id;
        m.leagueName = lg.name; m.arenaName = lg.arenaName;
        m.contests = lg.contests.Length;
        m.when = System.DateTime.Now.ToString("yyyy-MM-dd");
        // Credit the robot that was campaigning. activeRobot is -1 on a career
        // that never founded a stable, and an index into a list the player can
        // retire from - both are range-checked here rather than trusted.
        if (Data.activeRobot >= 0 && Data.activeRobot < Data.stable.Count)
        {
            var rob = Data.stable[Data.activeRobot];
            m.robot = rob.name; m.wins = rob.wins; m.losses = rob.losses;
            rob.titles++;
            string line = lg.name + " champion";
            if (!rob.leagueHistory.Contains(line)) rob.leagueHistory.Add(line);
        }
        else
        {
            m.robot = "(unnamed build)";
            m.wins = Data.fightWins; m.losses = Mathf.Max(0, Data.fights - Data.fightWins);
        }
        Data.medals.Add(m);
        // Ledger it the way the starter kit grant does: 0 scrap, but the audit
        // trail is where "when did this happen" is answered.
        Txn(0, "medal \u2605 " + lg.name + " champion \u00b7 " + m.robot);
        return m;
    }

    /// <summary>Settle a career contest fight (win purse with underdog
    /// multiplier / re-entry 40% / first-win bonus, or loss consolation).
    /// Returns false when no contest is live - exhibitions fall through to
    /// the sandbox settlement.</summary>
    public static bool SettleFight(bool win, float dealt)
    {
        if (activeContest == null) return false;
        var lg = FindLeague(activeLeague);
        var c = FindContest(lg, activeContest);
        activeLeague = null; activeContest = null;
        if (c == null) return false;
        bool reEntry = Data.doneContests.Contains(c.id);
        int pay = win ? CareerDB.WinPay(c, dealt, fightBuildValue, fightOppValue, reEntry, !reEntry)
                      : CareerDB.LossPay(dealt);
        lastSettled = true; lastPay = pay; lastMedal = null;   // round-3: see the field comment
        Txn(pay, (win ? "win " : "loss ") + c.id);
        Data.fights++;
        Data.lastContest = c.id;
        Data.scrapCurve.Add(Data.scrap);
        if (Data.scrapCurve.Count > 200) Data.scrapCurve.RemoveAt(0);
        if (win)
        {
            Data.fightWins++;
            if (!reEntry) Data.doneContests.Add(c.id);
        }
        // C4: the record belongs to the ROBOT that fought, not just the wallet.
        if (Data.activeRobot >= 0 && Data.activeRobot < Data.stable.Count)
        {
            var rob = Data.stable[Data.activeRobot];
            if (win) rob.wins++; else rob.losses++;
            rob.damageDealt += dealt;
            if (!rob.leagueHistory.Contains(c.id)) rob.leagueHistory.Add(c.id);
            // The old `c.id.StartsWith("L5") -> titles++` line lived here. It is
            // GONE, deliberately: World Championship is a one-contest league, so
            // winning L5C1 both fired that line AND completes the league, and a
            // medal that also bumps titles would have paid the same title twice.
            // Medals are now the single source of `titles`; L5 still scores
            // exactly +1, via AwardLeagueMedal below.
        }
        // MEDALS (2026-08-02): a first win can complete a league campaign. This
        // sits AFTER the robot block on purpose - the medal records the robot's
        // record INCLUDING the fight that won it.
        if (win && !reEntry)
        {
            int li = -1;
            for (int i = 0; i < CareerDB.Leagues.Length; i++)
                if (CareerDB.Leagues[i] == lg) { li = i; break; }
            lastMedal = AwardLeagueMedal(li);
        }
        if (Data.tutorialStep < 3) Data.tutorialStep = 3;
        string tag = win ? (reEntry ? "re-entry win \u00b7 40% purse" : "contest win") : "loss consolation";
        lastResultLine = string.Format("+{0} scrap ({1}) \u00b7 career scrap {2}", pay, tag, Data.scrap);
        Progression.lastRewardLine = lastResultLine;
        if (autosave) Save();
        return true;
    }
}
}
