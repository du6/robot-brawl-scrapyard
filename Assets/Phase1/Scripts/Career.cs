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
        /// <summary>OWEN 2026-08-03: the material the opponent's STRUCTURE and
        /// ARMOUR is rebuilt in for this contest. null = the recipe's own.
        ///
        /// Until now StartCareerFight called EnemyRoster.Recipe with no league,
        /// no tier and no material, so the BULWARK in the World Championship
        /// was byte-for-byte the BULWARK in Garage League. The player's cap
        /// went 1500 -> 5500 kg and their materials ran to tungsten; the
        /// opponent never changed at all. ApplyTier only ever touched DRIVING.
        ///
        /// WEAPONS are deliberately NOT reskinned. A blanket one-material pass
        /// is right for the bench's value-class test and wrong here: it would
        /// drop WIDOWMAKER's tungsten rim to titanium and make the flagship
        /// weaker as the league got harder. Disc damage is rotational energy,
        /// which wants density; armour wants strength per kilogram. They are
        /// different jobs and they take different metals.</summary>
        public string armourMat;
        public Contest(string i, string o, AiTier t, int p, int f, string armour = null)
        { id = i; oppId = o; tier = t; purse = p; entryFee = f; armourMat = armour; }
    }

    public class League
    {
        public string id; public string name; public string arenaName; public string arenaId;
        public float weightCap;
        public Contest[] contests;
        // OWEN 2026-08-03: "we already have the weight limit. why do we also
        // need size limit?" - and after seeing the numbers, "drop it, weight
        // only". The size box is GONE, not merely unenforced: a field that no
        // longer means anything is how a rule gets half-resurrected later by
        // someone who assumes it is still live.
        public League(string i, string n, string an, string aid, float cap, Contest[] c)
        { id = i; name = n; arenaName = an; arenaId = aid; weightCap = cap; contests = c; }
    }

    /// <summary>Doc sections 4 + 4b, verbatim. Arena ids consumed by C3A.</summary>
    public static readonly League[] Leagues =
    {
        // ARMOUR CLASS PER LEAGUE (owen 2026-08-03). Structure and plate only;
        // weapons keep whatever the recipe chose.
        //
        // The ladder runs by ABSOLUTE strengthRel - aluminium 0.6, steel 1.0,
        // titanium 1.4 - because HP is strengthRel * VOLUME and a reskin does
        // not change volume. My first pass climbed the HP-PER-KILOGRAM table
        // instead and ended at carbon fibre, which would have bought +10% hit
        // points and -65% mass: a lighter opponent, not a tougher one. HP/kg
        // only decides anything when weight is the binding constraint, and at
        // ~1.3 t under a 4 t cap it is not. This progression is +133% HP from
        // L2 to L4 on every structural part.
        new League("L1", "Scrapyard Open", "The Yard", "yard", 1500f, new[] {
            new Contest("L1C1", "scout",  AiTier.Rookie, 250, 0),
            new Contest("L1C2", "tipper", AiTier.Rookie, 300, 0) }),
        new League("L2", "Garage League", "The Loading Dock", "dock", 2000f, new[] {
            new Contest("L2C1", "mauler", AiTier.Rookie,  400, 0),
            new Contest("L2C2", "scout",  AiTier.Veteran, 450, 0, "Aluminum"),
            new Contest("L2C3", "tipper", AiTier.Veteran, 500, 0, "Aluminum") }),
        new League("L3", "Regional Circuit", "The Sawmill", "sawmill", 2800f, new[] {
            new Contest("L3C1", "bulwark", AiTier.Veteran, 700, 50, "Steel"),
            new Contest("L3C2", "mauler",  AiTier.Veteran, 800, 50, "Steel"),
            new Contest("L3C3", "ripper",  AiTier.Veteran, 900, 50, "Steel"),
            // The first disc a player ever meets. Before this the roster had
            // none until L4, which is exactly why one spinner cleared three
            // leagues unopposed.
            new Contest("L3C4", "millstone", AiTier.Veteran, 950, 50, "Steel") }),
        new League("L4", "National Series", "The Press", "press", 4000f, new[] {
            new Contest("L4C1", "widowmaker", AiTier.Veteran,  1200, 100, "Titanium"),
            new Contest("L4C2", "bulwark",    AiTier.Champion, 1400, 100, "Titanium"),
            new Contest("L4C3", "ripper",     AiTier.Champion, 1600, 100, "Titanium"),
            new Contest("L4C4", "bastion",    AiTier.Champion, 1800, 100, "Titanium") }),
        new League("L5", "World Championship", "The Crucible", "crucible", 5500f, new[] {
            new Contest("L5C1", "widowmaker", AiTier.Champion, 3000, 200, "Titanium") }),
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
    // OWEN 2026-08-03 tutorial: SKIP TIPS used to set tutorialStep = 3, which
    // silenced the tips by CLAIMING the player had finished onboarding. The
    // tips now run past step 3, and more to the point a skip should not lie
    // about progress. New field, so JsonUtility hands every existing save
    // false and nobody's career changes.
    public bool tipsOff;
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
    /// <summary>Is a DESIGN open? This is the real thing, and it is what every
    /// GATE must read - the fight blocker, StableCreate, the banner.
    ///
    /// OWEN 2026-08-03 (found while benching his spinner): this used to return
    /// true whenever devFreeBuild was set, conflating two unrelated ideas -
    /// "the harness is ignoring inventory" and "the player is editing a
    /// design". CareerBench sets devFreeBuild, so from the moment the fight
    /// gate learned to refuse drafts (2026-08-02) the entire balance harness
    /// could not start a single fight. It reported 0/3 for every pairing and
    /// read like a balance result. Same failure as the shop harness: a test
    /// that produces numbers while doing nothing.</summary>
    public static bool Drafting
    {
        get
        {
            if (!active || Data == null) return false;
            return Data.activeBlueprint >= 0 && Data.activeBlueprint < Data.blueprints.Count;
        }
    }

    /// <summary>Are parts free right now - either because a design is open or
    /// because a harness said so? This is what the INVENTORY checks want, and
    /// the only thing devFreeBuild was ever meant to influence.</summary>
    public static bool FreeParts { get { return devFreeBuild || Drafting; } }
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
    /// <summary>R4 (critic finding 5) took this to 2 to grandfather saves
    /// against the shared pool. The pool is gone (2026-08-05) and the grant
    /// with it, but the number stays 2: it is a HIGH-WATER MARK, not a
    /// description. Lowering it would re-run the material repair on every
    /// already-migrated save for no reason.</summary>
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
        // The R4 top-up that used to sit here is GONE with the shared pool
        // (owen, 2026-08-05). It granted every saved robot's parts so an old
        // save would still "fit the pool" after R4 made it not fit.
        //
        // Under the restored rule it buys nothing. A pre-R4 save is legal
        // exactly as it always was: each robot is fieldable iff you own ITS
        // parts, which is the same test that applied before R4 existed - back
        // then a robot needing six beams you did not own already failed
        // CareerValidate the moment you loaded it. So there is nothing to
        // grandfather. R4 made saves illegal; undoing R4 makes them legal
        // again, without a grant.
        //
        // Saves that ALREADY took the top-up keep those parts. KitVersion
        // stays 2 so the migration does not re-run, and no path anywhere claws
        // granted stock back - taking a player's parts away to correct our own
        // bookkeeping is never the right trade.
        Data.kitVersion = KitVersion;
        if (moved > 0) Debug.Log("Career: moved " + moved + " unreachable inventory unit(s) onto their pinned material.");
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

    // SwapCost / TrySwap ("REWORK") lived here and are gone (owen, 2026-08-05:
    // "Let's remove rework"). The shop is BUY and SELL.
    //
    // The claim in the old doc comment - "upgrades stay cheaper than
    // sell-at-50% + rebuy" - turned out to hold only sometimes, and the
    // boundary was not stated anywhere the player could see it. Sell-back is
    // 50% of the SOURCE; the workshop fee was 10% of the TARGET. So rework won
    // only while the target cost under 5x the source, and lost outright above
    // that: an ABS beam reworked to Steel cost 153 against 145 for sell+rebuy.
    // Every DOWNGRADE was far worse - Tungsten beam to Aluminium cost 6 scrap
    // where selling and rebuying PAID 1325 - and the button took that trade
    // without comment.
    //
    // Careers written before today may hold "swap ..." lines in Data.txns.
    // Those are inert history: a cause string and a delta that TxnSum still
    // adds up, so the ledger audit is unaffected and no migration is needed.

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

    // ---- OWEN 2026-08-05: designs do NOT hold parts --------------------

    /// <summary>REVERSES R4 (critic finding 5), deliberately.
    ///
    /// R4 implemented doc section 7 - "inventory is a shared pool across garage
    /// slots: parts are owned, builds borrow them, two saved builds can't use
    /// the same six beams simultaneously" - by making every OTHER saved robot
    /// consume stock. CommittedUsage, CommittedCount and their StableSig cache
    /// are deleted with this comment.
    ///
    /// owen, 2026-08-05: "I saved two robots. I don't own the parts for both
    /// together, but I do own any one of them ... as long as they own the
    /// current robot, they can fight with it. otherwise user always needs to
    /// retire and rebuild."
    ///
    /// He is right, and the reason is that the shared pool enforced a fiction
    /// the game never delivers. It says your robots stand assembled in a
    /// garage - but you can only ever field ONE, and the game already tracks
    /// which (activeRobot). Under that fiction keeping a second design costs
    /// you the hardware to build it, so the player pays for STORAGE in parts
    /// and the only way to try an idea is to retire the thing that works. That
    /// is a tax with no gameplay in it, and it is the same "wastes time"
    /// complaint that produced cascade removal.
    ///
    /// The model now: you own a BOX OF PARTS and a FOLDER OF DESIGNS. Whichever
    /// design is loaded is the machine currently bolted together, and it is the
    /// only one that has to be affordable. Switching is free, because in the
    /// fiction it is just unbolting one and building the other - which is what
    /// the player is doing anyway.
    ///
    /// The accounting is UNCHANGED in kind: still derived, still owned minus
    /// used, and building still never mutates the inventory. The sum simply
    /// runs over the loaded build again instead of over the whole stable.
    ///
    /// This deletes three claims with it - the "over the parts pool" save
    /// warning, the per-robot breakdown on the PARTS shelf, and "retire a robot
    /// to free its parts" - because all three asserted an allocation that no
    /// longer exists. It ADDS the readiness badge below, because "which of my
    /// robots can I field right now?" has to stay answerable BEFORE you load
    /// one. A rule you can only discover by being refused is exactly the
    /// 2026-08-02 FIGHT-button defect, and it would have walked straight back
    /// in through this door.</summary>
    public static List<CareerItem> SnapshotShortfall(string snapshot)
    {
        var lack = new List<CareerItem>();
        foreach (var kv in SnapshotUsage(snapshot))
        {
            var f = kv.Key.Split('|');
            var d = CareerDB.Def(f[0]);
            if (d == null) continue;
            // EffectiveMat first, then Canon: a pinned part ignores whatever
            // material the snapshot carries, and the inventory is keyed on the
            // canonical spelling. Skipping either step looks up a row that
            // exists under a different name and reports a phantom shortage.
            string mat = d.EffectiveMat(f[1]);
            string c = MatDB.Canon(mat);
            if (c != null) mat = c;
            int miss = kv.Value - CountOf(f[0], mat);
            if (miss > 0) lack.Add(new CareerItem { partId = f[0], mat = mat, count = miss });
        }
        return lack;
    }

    /// <summary>"2x Aluminum Beam, 1x Steel Blade bar". ONE phrasing, shared by
    /// the desktop stable list, the touch ROBOTS card and CareerSmoke, so the
    /// three can never describe the same shortfall differently.</summary>
    public static string ShortfallText(List<CareerItem> lack)
    {
        string s = "";
        for (int i = 0; i < lack.Count; i++)
        {
            var d = CareerDB.Def(lack[i].partId);
            s += (s.Length > 0 ? ", " : "") + lack[i].count + "\u00d7 "
               + MatDB.Get(lack[i].mat).name + " " + (d != null ? d.label : lack[i].partId);
        }
        return s;
    }

    /// <summary>Can stable slot `i` be bolted together and fought RIGHT NOW?
    /// This is the question the ROBOTS list has to answer on every row.</summary>
    public static bool RobotReady(int i)
    {
        if (i < 0 || i >= Data.stable.Count) return false;
        return SnapshotShortfall(Data.stable[i].snapshot).Count == 0;
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
