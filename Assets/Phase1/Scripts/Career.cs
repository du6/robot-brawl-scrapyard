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
    // REENTRY_FRAC (0.4) lived here until 2026-08-13 and is GONE, deliberately:
    // owen's first-win rule (docs/Server_Economy_Design_2026-08-13.md §5) -
    // a contest pays on the FIRST win only. Re-entering a beaten contest is a
    // PRACTICE BOUT: no entry fee, no purse, no damage bonus, no consolation.
    // This is what hard-caps the league economy at the purse-table ceiling
    // once scrap is sellable; do not resurrect a repeatable payment here.
    // ---- THE HALVING, 2026-08-12 (owen). CareerBench's surviving verdicts
    // say the league is too easy at the top (CEILING L4 92%, STRETCH failing
    // L3/L4/L5 the same way) and owen has low confidence in tuning the AI
    // itself — so the ECONOMY moved instead: every league WIN payment is
    // half of the doc-section-5 value, and the ARENA (whose purses and
    // season podium did NOT move) becomes the richer place to earn. What was
    // deliberately NOT halved: loss consolation (it pays the struggling
    // player, not the winning one), entry fees (a cost, not a reward), and
    // every fraction/multiplier that scales WITH the purse. Season payouts
    // (300/150/100) were sized against the OLD purses; they now buy
    // relatively more, which is the point.
    // ---- THE ONE-THIRD CUT, 2026-08-15 (owen: "cut league rewards by one
    // third"). Following the HALVING's scope exactly: every WIN-payment
    // component is ×2/3 (purses in the table below, this bonus, the damage
    // slope), while everything that scales WITH the purse (UNDERDOG_K/CAP) is
    // left alone and follows automatically. Loss consolation and entry fees are
    // already gone; season/arena purses are deliberately untouched. The server
    // mirrors this — Program.cs ECON_* constants and migration 013 move in
    // lockstep, and api_smoke's purse-sum check moves with them.
    public const int FIRST_WIN_BONUS = 50;     // one-time, per contest (150 -> 75 halved -> 50, ×2/3)
    public const float WIN_DMG_K = 0.1667f;    // + min(dealt,400) * this on wins (0.5 -> 0.25 -> 0.1667, ×2/3)
    public const float WIN_DMG_CAP = 400f;
    // LOSS CONSOLATION (LOSS_BASE/LOSS_DMG_K/LOSS_DMG_CAP/LOSS_MAX and
    // LossPay) lived here until 2026-08-13 and is GONE (owen: "remove loss
    // payment in leagues"). With entry fees also gone, a repeatable loss
    // payment was the last unbounded faucet in the league: lose on purpose,
    // collect 40-150, forever. The league now pays WINS ONLY, once each,
    // and the entire league exposure is the win-path ceiling (13,850 per
    // account - docs/Server_Economy_Design_2026-08-13.md §5). Do not
    // resurrect a payment on the loss path.

    public class Contest
    {
        public string id; public string oppId; public AiTier tier;
        // entryFee lived here until 2026-08-13 and is GONE (owen: "remove
        // League's entry fee completely"). The league is free to enter at
        // every level; the purse and the first-win rule are the whole
        // economy. Server side matches: migration 012 dropped the column.
        public int purse;
        /// <summary>HARDENED (owen, 2026-08-12: "make higher level league
        /// robots harder to beat"). The opponent spawns with every part
        /// except core and wheels GUSSETED (seams ×1.5) - the same measured
        /// lever players got this evening. Set SURGICALLY: CareerBench's
        /// 3x-sample verdicts say the CEILING fails at the FLAGSHIPS (a
        /// two-below robot beat L4's BASTION 22/24 by SHEDDING its parts —
        /// structFrac counts pieces) while STRETCH already fails LOW at the
        /// cheapest contests — so hardening those would deepen a different
        /// failure. Flagships only.</summary>
        public bool hardened;
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
        public Contest(string i, string o, AiTier t, int p, string armour = null, bool hard = false)
        { id = i; oppId = o; tier = t; purse = p; armourMat = armour; hardened = hard; }
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
        // PURSES CUT ×2/3, 2026-08-15 (owen). Old sum 7125 -> 4750, exactly
        // two-thirds; each value rounded to the nearest scrap. Server migration
        // 013 sets the same integers in league_contests — keep them identical.
        new League("L1", "Scrapyard Open", "The Yard", "yard", 1500f, new[] {
            new Contest("L1C1", "scout",  AiTier.Rookie, 83),
            new Contest("L1C2", "tipper", AiTier.Rookie, 100) }),
        new League("L2", "Garage League", "The Loading Dock", "dock", 2000f, new[] {
            new Contest("L2C1", "mauler", AiTier.Rookie,  133),
            new Contest("L2C2", "scout",  AiTier.Veteran, 150, "Aluminum"),
            new Contest("L2C3", "tipper", AiTier.Veteran, 167, "Aluminum") }),
        new League("L3", "Regional Circuit", "The Sawmill", "sawmill", 2800f, new[] {
            new Contest("L3C1", "bulwark", AiTier.Veteran, 233, "Steel"),
            new Contest("L3C2", "mauler",  AiTier.Veteran, 267, "Steel"),
            new Contest("L3C3", "ripper",  AiTier.Veteran, 300, "Steel"),
            // The first disc a player ever meets. Before this the roster had
            // none until L4, which is exactly why one spinner cleared three
            // leagues unopposed.
            new Contest("L3C4", "millstone", AiTier.Veteran, 317, "Steel") }),
        new League("L4", "National Series", "The Press", "press", 4000f, new[] {
            new Contest("L4C1", "widowmaker", AiTier.Veteran,  400, "Titanium"),
            new Contest("L4C2", "bulwark",    AiTier.Champion, 467, "Titanium"),
            new Contest("L4C3", "ripper",     AiTier.Champion, 533, "Titanium"),
            new Contest("L4C4", "bastion",    AiTier.Champion, 600, "Titanium", hard: true) }),
        new League("L5", "World Championship", "The Crucible", "crucible", 5500f, new[] {
            // L5C1 hardening MEASURED AND REVERTED same evening: CEILING L5 went
            // 1/24 -> 6/24 WORSE with it — WIDOWMAKER is a hunter, and ~+100 kg
            // of gussets blunted the chase more than the seams helped. The
            // flagship whose loss mode IS shedding keeps the flag (L4C4).
            new Contest("L5C1", "widowmaker", AiTier.Champion, 1000, "Titanium") }),
    };

    public class KitItem { public string partId; public string mat; public int count;
        public KitItem(string p, string m, int c) { partId = p; mat = m; count = c; } }

    /// <summary>Doc section 6. Core is NOT inventory: free, unlimited,
    /// unsellable — every robot needs one and the shop never touches it.</summary>
    public static KitItem[] StarterKit()
    {
        return new[]
        {
            // THE KIT IS EXACTLY SCRAPPER (owen, 2026-09-04). Every spare that
            // used to sit greyed on the shelf now arrives as a REWARD BOX with
            // something in it: wedge + gusset for the first bout, beams and
            // plates for the first bolt, long beam + spindle for the first
            // weld, the cube for the first purchase (Career.QueueReward sites).
            // Same parts, same bounded total, deferred - and the boot shelf
            // says one thing: here is your machine, go fight.
            new KitItem("beam",     "Aluminum", 2),   // the two axles
            // bracket x4 removed with the part (2026-08-12). What, if
            // anything, replaces the ~88 kg of kit budget is the register's
            // tutorial-assembly decision, not this line's.
            // chassis x1 removed from the kit with the part's player-side
            // retirement (owen, 2026-08-12) — the block still exists, but only
            // under enemy robots. Kit composition overall remains the
            // register's tutorial-assembly decision.
            // ONE MATERIAL FOR THE WHOLE KIT (owen, 2026-09-02). The chips
            // default to Aluminum, so the kit's Steel wedge/spike and ABS
            // plates rendered as "0 free · 1 in Steel" - which reads as "you
            // don't own this". The 2026-09-01 playtest hit it verbatim
            // ("palette labels read as unavailable when they aren't - I
            // nearly skipped weapons entirely"). Everything the kit grants
            // is now visible and placeable under the default chip on minute
            // one. The wheel stays Rubber: it is PINNED (materialChoice =
            // false) and its tile names its material, so it never lied.
            // Cost accepted: an aluminum starter wedge/spike is lighter and
            // less durable than steel - and a lighter nose on a machine
            // whose measured loss mode is TIPPING is, if anything, a help.
            // Existing careers keep their Steel/ABS parts; no migration.
            new KitItem("wheel",    "Rubber",   4),   // R2: the wheel def PINS itself to Rubber
                                                      // (materialChoice=false) and Rubber is deliberately
                                                      // absent from MatDB.Order, so an "Aluminum" grant was
                                                      // inventory no chip could ever reveal. See ResolveMat.
            new KitItem("battery",  "Aluminum", 1),
            // gyro x1 removed with its player-side retirement (owen, 2026-08-12);
            // a Cube x1 takes the slot so the crate keeps a small block.
            new KitItem("spike",    "Aluminum", 1),
            // SCRAPPER carries a Compass tracker and a Wall sensor so its
            // autopilot HUNTS (RamHunter, StarterBench 9/10 vs SCOUT). Granted
            // so the pre-built machine is fully owned on a fresh career.
            new KitItem("compass",  "Aluminum", 1),
            new KitItem("wallsensor","Aluminum", 1),
            // The starter robot ships with its spike seam WELDED (StarterBench,
            // 2026-09-03: unwelded, the aluminum spike sheared in 10/10 bouts
            // and every fight ended in a mutual-disarm draw; welded, 6/10 wins).
            // The weld consumes one gusset, so the kit must grant one - and a
            // player who removes the spike gets a free weld to re-spend, which
            // is the gusset's own tutorial.
            new KitItem("gusset",   "Steel",    1),
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
    { { "wheel", 60 }, { "battery", 120 }, { "gyro", 110 }, { "pivot", 150 }, { "spindle", 150 }, { "ram", 150 },
      // P1 sensors (design doc v1.1 §4.1): priced as technology. Raw CostOf on
      // a few kg of pinned aluminium would sell a rangefinder for pocket change.
      { "rangefinder", 120 }, { "compass", 180 }, { "tiltsensor", 60 }, { "dmgbus", 70 },
      // V2.2 sensor split: the edge sentinel's two halves. Wall keeps the
      // old 90; trap prices under it — two verb families now cost a little
      // more than the one part that did both jobs.
      { "wallsensor", 90 }, { "trapsensor", 70 } };

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

    /// <summary>Win settlement (doc section 5, amended by the first-win rule
    /// 2026-08-13). buildValue/oppValue are the robots' summed part prices;
    /// reEntry = contest already beaten, which now means PRACTICE: zero pay,
    /// every component. The server's /v1/economy/claims mirrors this
    /// arithmetic and is what actually pays once the wallet is server-side —
    /// keep the two in step.</summary>
    public static int WinPay(Contest c, float dealt, int buildValue, int oppValue, bool reEntry, bool firstWin)
    {
        if (reEntry) return 0;   // practice bout — the purse was won already
        float mult = 1f + UNDERDOG_K * Mathf.Clamp01((float)oppValue / Mathf.Max(1, buildValue) - 1f);
        mult = Mathf.Min(mult, UNDERDOG_CAP);
        int pay = Mathf.RoundToInt(c.purse * mult + Mathf.Min(dealt, WIN_DMG_CAP) * WIN_DMG_K);
        if (firstWin) pay += FIRST_WIN_BONUS;
        return pay;
    }

}

[System.Serializable] public class CareerItem { public string partId; public string mat; public int count; }
[System.Serializable] public class CareerRobot
{
    public string name; public string snapshot;
    /// <summary>P2: RobotProgram JSON. "" = no program — every pre-P2 save
    /// gets exactly that from JsonUtility, which IS the migration.</summary>
    public string program = "";
    public int wins; public int losses; public int titles;
    public float damageDealt;
    public List<string> leagueHistory = new List<string>();
}

/// <summary>V2.5 (owen 2026-08-06): one entry in the program LIBRARY —
/// a program saved as a document, independent of any robot. The canvas's
/// SAVE/SAVE AS write these; LOAD TO ROBOT is what arms one on a machine.</summary>
[System.Serializable] public class SavedProgram
{
    public string name = "";
    public string json = "";
}
[System.Serializable] public class CareerBlueprint { public string name; public string snapshot; public string program = ""; }
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

/// <summary>Client B (docs/Server_Economy_Design_2026-08-13.md): a first-win
/// purse the SERVER has not paid yet. Queued at settle when signed in,
/// flushed by EconomySync when online; the attempt id makes the flush
/// retry-safe (the server answers a replay 409 and the claim is dropped
/// either way). JsonUtility hands every older save an empty list, which IS
/// the migration (the medals precedent).</summary>
[System.Serializable] public class CareerClaim
{ public string contestId; public float dealt; public float mult; public string attempt; }

/// <summary>Client B: a shop purchase the server has not confirmed yet.
/// Applied optimistically at the till, flushed by EconomySync; a refusal
/// REVERSES the local purchase (compensation), so local inventory can never
/// drift from what the server actually charged. Same additive-list
/// migration as everything else.</summary>
[System.Serializable] public class CareerPurchase
{ public string op; public string partId; public string mat; public string idemKey; }

[System.Serializable] public class CareerData
{
    public int scrap;
    public List<CareerItem> inventory = new List<CareerItem>();
    public List<CareerRobot> stable = new List<CareerRobot>();
    public List<CareerBlueprint> blueprints = new List<CareerBlueprint>();
    public List<string> doneContests = new List<string>();   // first-win flags
    /// <summary>P4 (owen 2026-08-06, "shared contests, tracked separately"):
    /// contests EVER won with the autopilot driving — the autonomy mark.
    /// Purses/medals/progression are identical either way; this is the extra
    /// record. JsonUtility hands every pre-P4 save an empty list, which IS
    /// the migration (the medals precedent).</summary>
    public List<string> autoDoneContests = new List<string>();
    public List<CareerTxn> txns = new List<CareerTxn>();
    /// <summary>Client B: purse claims the server has not confirmed yet.
    /// Empty on every signed-out career by construction — SettleFight only
    /// enqueues when a session exists.</summary>
    public List<CareerClaim> pendingClaims = new List<CareerClaim>();
    /// <summary>Client B: shop purchases the server has not confirmed yet.
    /// Empty on every signed-out career by construction.</summary>
    public List<CareerPurchase> pendingPurchases = new List<CareerPurchase>();
    /// <summary>Medals (2026-08-02). Empty on every pre-existing save by
    /// construction - see CareerMedal.</summary>
    public List<CareerMedal> medals = new List<CareerMedal>();
    /// <summary>V2.5: the program library. JsonUtility hands every older
    /// save an empty list, which IS the migration (the medals precedent).</summary>
    public List<SavedProgram> programs = new List<SavedProgram>();
    /// <summary>Rookie warm-up flags (docs/Rookie_Warmup_Design_2026-09-03.md).
    /// All once-per-career, all bounded - honoring the 2026-08-13 no-faucet
    /// rule. JsonUtility hands every older save `false`, which IS the
    /// migration (the medals precedent).</summary>
    public bool rescueGranted;
    public bool taskFight, taskBolt, taskWeld, taskBuy;
    public bool guideDone;
    /// <summary>Rewards QUEUED but not yet OPENED (web QA 2026-09-04: the
    /// scrap and parts used to land the moment a task completed, so the
    /// header spoiled the box and opening it changed nothing). The grant
    /// now happens when the box opens - and, because a box can be skipped,
    /// auto-dismissed or lost to a closed tab, any id still here at the next
    /// boot is granted on Load. Nothing is ever lost; only the reveal moves.</summary>
    public List<string> pendingRewards = new List<string>();
    /// <summary>QUICK FIGHT (docs/CATS_Gap_Analysis_Plan_2026-09-07.md, steps 1-2):
    /// 30-second bouts, 3 wins = a toolbox, 5 in a row = a crown, a DAILY CAP
    /// on boxes instead of a wait timer. JsonUtility hands older saves zeros.</summary>
    public int quickFights, quickWins, quickStreak, quickBestStreak, crowns, quickBoxesToday, quickWinsToBox;
    public string quickBoxDay = "";
    /// <summary>SCRAPYARD: the world is generated from this seed (0 = not yet
    /// rolled; rolled on the first drive out and saved - your world is yours
    /// and it persists), and the crates opened, keyed "cx,cz:i" per chunk, so
    /// they never respawn.</summary>
    public int worldSeed;
    public List<string> worldOpened = new List<string>();
    public int yardStep;              // the first minute: 0 chest, 1 meet, 2 challenge, 3 the trading post, 4 explore
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

    // SCRAPYARD (2026-09-09): its own file in its own folder. persistentDataPath
    // already differs from Robot Brawl's (Unity derives it from company +
    // product name, and the product is "Robot Brawl: Scrapyard"); the file
    // name differs too so the two saves can never be confused for each other.
    static string PathFile { get { return Application.persistentDataPath + "/scrapyard_save.json"; } }

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
        GrantPendingRewards();   // boxes that never got opened last session
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
    /// <summary>3 (V2.2, 2026-08-06): the edge-sentinel split — owned edge
    /// sentinels become one WALL + one TRAP sensor each.</summary>
    public const int KitVersion = 3;
    public static void MigrateInventory()
    {
        if (Data.kitVersion >= KitVersion) return;
        // V2.2 (KitVersion 3): the edge sentinel split into WALL + TRAP
        // sensors. Every owned edge sentinel becomes one of EACH — the old
        // part did both jobs, so the split must not shrink what a player
        // paid for. A saved build that PLACED one loads through
        // LoadSnapshot's id remap and comes back with a wall sensor in the
        // same socket.
        int splitN = 0;
        for (int i = Data.inventory.Count - 1; i >= 0; i--)
        {
            var old = Data.inventory[i];
            if (old.partId != "edgesentinel") continue;
            int n = old.count;
            Data.inventory.RemoveAt(i);
            AddItem("wallsensor", "Aluminum", n);
            AddItem("trapsensor", "Aluminum", n);
            splitN += n;
        }
        if (splitN > 0) Debug.Log("Career: split " + splitN + " edge sentinel(s) into wall + trap sensors (V2.2).");
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

    /// <summary>V2.8c (critic loop 7 R3) — the crate as a flat id set, so a
    /// refusal can say "bolt on" instead of "buy". ONE source: ProgramCanvas
    /// and AutonomyBlocker both read this, because R2 shipped the crate-aware
    /// message on the PROGRAM tab only and the FIGHT tab kept handing out a
    /// bare "— SHOP" for parts the player already had.
    /// KNOWN LIMIT (R3 critic F4): material-blind, and it does not subtract
    /// units already bolted on — so it can say "you own one" about a line
    /// whose material this build will not take. SnapshotShortfall is the exact
    /// accounting; this is the cheap one, and it only has to decide which of
    /// BUILD-tab / SHOP the sentence should point at.</summary>
    public static List<string> OwnedPartIds()
    {
        var ids = new List<string>();
        if (Data == null || Data.inventory == null) return ids;
        foreach (var it in Data.inventory)
        {
            if (it.count <= 0) continue;
            if (!ids.Contains(it.partId)) ids.Add(it.partId);
            if (Actuator.IsActuatorId(it.partId) && !ids.Contains("actuator"))
                ids.Add("actuator");
        }
        return ids;
    }

    public static void Save()
    { System.IO.File.WriteAllText(PathFile, JsonUtility.ToJson(Data)); }

    /// <summary>Rookie checklist grants - each fires once per career and pays
    /// a fixed 10, so the whole checklist is bounded at +40 total. Quiet on
    /// re-fire by design.</summary>
    public static void RookieTaskBolt()
    { if (!active || Data == null || Data.taskBolt) return; Data.taskBolt = true; uiDirtySeq++; QueueReward("bolt", "FIRST PART BOLTED", "Every machine starts with one bolt.", "+10 SCRAP", "2 BEAMS", "2 ARMOR PLATES"); if (autosave) Save(); }
    public static void RookieTaskWeld()
    { if (!active || Data == null || Data.taskWeld) return; Data.taskWeld = true; uiDirtySeq++; QueueReward("weld", "FIRST WELD", "Welded seams hold x4. Your spike will thank you.", "+10 SCRAP", "1 LONG BEAM", "1 SPINDLE (axle)"); if (autosave) Save(); }

    /// <summary>Make sure the inventory can BUILD `snapshot` - grant whatever is
    /// short. owen's phone, 2026-09-04: a career created before the compass,
    /// wall sensor and gusset joined the kit later received SCRAPPER (injected
    /// into any career with no robots and no fights), so the fight gate greyed
    /// AUTONOMY FIGHT with "needs 1x Ram spike, 1x Compass tracker, 1x Wall
    /// sensor, 1x Gusset" while the guide pointed straight at it. The kit is
    /// granted once at creation; the starter can change between builds; this
    /// closes the gap from the robot's side. Parses the snapshot format
    /// (id|pos|yaw|axis|Mat[|G:mask]); the core is exempt from stock, and every
    /// gusset mark consumes one Steel gusset. Returns how many parts it added.</summary>
    public static int TopUpForSnapshot(string snapshot)
    {
        if (Data == null || string.IsNullOrEmpty(snapshot)) return 0;
        var need = new Dictionary<string, int>();
        foreach (var raw in snapshot.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var f = line.Split('|');
            if (f.Length < 5 || f[0] == "core") continue;
            string key = f[0] + "|" + f[4];
            need[key] = need.TryGetValue(key, out var n) ? n + 1 : 1;
            for (int i = 5; i < f.Length; i++)
                if (f[i].StartsWith("G:") && int.TryParse(f[i].Substring(2), out var mask))
                {
                    int bits = 0; for (int m = mask & 63; m != 0; m >>= 1) bits += m & 1;
                    string gk = "gusset|Steel";
                    need[gk] = need.TryGetValue(gk, out var g) ? g + bits : bits;
                }
        }
        int added = 0;
        foreach (var kv in need)
        {
            int bar = kv.Key.IndexOf('|');
            string id = kv.Key.Substring(0, bar), mat = kv.Key.Substring(bar + 1);
            int have = CountOf(id, mat);
            if (have < kv.Value) { AddItem(id, mat, kv.Value - have); added += kv.Value - have; }
        }
        if (added > 0) { uiDirtySeq++; if (autosave) Save(); }
        return added;
    }

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

    /// <summary>Bumped by every inventory mutation, so a UI can repaint what it
    /// owns without a gesture to hang it on. owen, 2026-09-10: "I bought a long
    /// beam ... it is not immediately available in my build package" - the BUILD
    /// palette only re-arranged on a placed-count / material / career change,
    /// and a purchase is none of those.</summary>
    public static int inventorySeq;

    public static void AddItem(string partId, string mat, int n)
    {
        inventorySeq++;
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
                inventorySeq++;
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

    /// <summary>How many harnesses currently require autosave OFF.
    ///
    /// A plain save-and-restore of `autosave` is not safe when two harnesses
    /// overlap, and on 2026-08-10 that wrote the real career: HazardBench,
    /// CareerBench and TouchSmoke were started in the same frame, each
    /// captured the flag as TRUE, and the first one to finish restored it to
    /// TRUE while the others were still running fights. The save came back
    /// with 65 fights where there were 11, 218,361 scrap where there were
    /// 6,513, and the medals and stable wiped.
    ///
    /// Restoring the CAPTURED value instead of a literal `true` fixes the
    /// sequential case and does nothing for that one — every capture said
    /// true, so every restore said true. The only thing that closes it is
    /// counting: the flag goes back on when the LAST holder lets go.
    ///
    ///     using (Career.SuspendAutosave()) { ... }        // preferred
    ///     var h = Career.SuspendAutosave(); ... h.Dispose();
    ///
    /// Hard rule 5 is "owner state is sacred", and a mechanism that only works
    /// when harnesses politely take turns is not a mechanism.</summary>
    static int autosaveHolds;

    /// <summary>Suspend autosave until every holder has released it. Disposing
    /// twice is harmless; the count floors at zero rather than going negative
    /// and re-arming writes early.</summary>
    public static System.IDisposable SuspendAutosave()
    {
        autosaveHolds++;
        autosave = false;
        return new AutosaveHold();
    }

    sealed class AutosaveHold : System.IDisposable
    {
        bool released;
        public void Dispose()
        {
            if (released) return;
            released = true;
            autosaveHolds = Mathf.Max(0, autosaveHolds - 1);
            if (autosaveHolds == 0) autosave = true;
        }
    }

    /// <summary>Test/diagnostic read: how many harnesses are holding autosave
    /// off right now. Non-zero after a bench has finished means that bench
    /// leaked a hold, which is worth failing on.</summary>
    public static int AutosaveHolds { get { return autosaveHolds; } }
    /// <summary>P4: true while the CURRENT contest fight runs under the
    /// autopilot (the player's controlSource is Program and the keyboard is
    /// dead). Written EXPLICITLY by every StartCareerFight call — false for
    /// manual, true for autonomy — so it can never go stale across fights;
    /// SettleFight reads it to stamp the autonomy mark.</summary>
    public static bool fightAutonomous;

    /// <summary>Client B: signed in, the shop needs the server (owen's
    /// offline-shop decision - browse, no buying). "Online" means this
    /// session has reached the wallet at least once; a purchase then applies
    /// OPTIMISTICALLY and queues for the flusher, which reverses it if the
    /// server refuses. Signed out (benches, dev door, a career that never
    /// signed in) the shop is pure local, exactly as it always was.</summary>
    static bool ShopOffline(out string msg)
    {
        msg = null;
        if (!LadderClient.SignedIn) return false;
        if (EconomySync.SessionOnline) return false;
        msg = "OFFLINE - buying needs a connection. Your parts and scrap are safe.";
        return true;
    }

    static void QueuePurchase(string op, string partId, string mat)
    {
        if (!LadderClient.SignedIn) return;
        Data.pendingPurchases.Add(new CareerPurchase
        { op = op, partId = partId, mat = mat, idemKey = System.Guid.NewGuid().ToString("N") });
        EconomySync.Kick();
    }

    public static bool TryBuy(string partId, string mat)
    {
        shopMsg = "";
        if (partId == "core") { shopMsg = "The core is not for sale."; return false; }
        string off; if (ShopOffline(out off)) { shopMsg = off; return false; }
        int price = CareerDB.PartPrice(partId, mat);
        if (Data.scrap < price)
        { shopMsg = "Not enough scrap - " + price + " needed, " + Data.scrap + " held."; return false; }
        AddItem(partId, mat, 1);
        Txn(-price, "buy " + partId + " " + mat);
        if (!Data.taskBuy)
        {
            Data.taskBuy = true;
            uiDirtySeq++;
            QueueReward("buy", "FIRST PURCHASE", "The shop pays you back for shopping. Once.", "+10 SCRAP", "1 CUBE");
        }
        QueuePurchase("buy", partId, mat);
        uiDirtySeq++;   // regression pass: the league board showed stale scrap after purchases
        if (autosave) Save();
        return true;
    }

    /// <summary>! SELLING IS CLOSED (owen, 2026-08-19): "disallow users from
    /// selling parts back." Kept as a function that always refuses rather than
    /// deleted, so any caller left anywhere gets a REASON instead of a compile
    /// error that tempts someone into re-adding the path.
    ///
    /// ⚠ IT REFUSES BEFORE TOUCHING ANY STATE, and that ordering is the point.
    /// The till applies optimistically — TryConsume, Txn, QueuePurchase — and
    /// EconomySync reconciles with the server later. If this consumed the part
    /// first and the server then refused, the player would watch a part vanish
    /// and come back seconds later with an amber message they did not ask for.
    /// Nothing is consumed, nothing is queued, nothing is saved.
    ///
    /// ⚠ AND IT MEANS A MATERIAL CANNOT BE CHANGED ANY MORE. REWORK was removed
    /// 2026-08-05, so SELL+BUY was the only route left: the shop's own hint
    /// still said "to change a material, SELL and BUY" until this change, and
    /// that line is now gone with it. Buying a part is a one-way commitment.</summary>
    public static bool TrySell(string partId, string mat)
    {
        shopMsg = "Parts cannot be sold back — buying a part is a permanent commitment.";
        return false;
    }

    /// <summary>Compensation (EconomySync): the server REFUSED a purchase the
    /// till applied optimistically - undo it and say so. This is what keeps
    /// local inventory from drifting from what the server actually charged.</summary>
    public static void ReversePurchase(CareerPurchase p, string why)
    {
        if (p.op == "buy")
        {
            TryConsume(p.partId, p.mat, 1);
            Txn(CareerDB.PartPrice(p.partId, p.mat), "buy reversed (" + why + ") " + p.partId);
        }
        else
        {
            AddItem(p.partId, p.mat, 1);
            Txn(-CareerDB.SellPrice(p.partId, p.mat), "sell reversed (" + why + ") " + p.partId);
        }
        shopMsg = "The shop could not complete a " + p.op + " - " + why;
        // Bump so the dock REPAINTS: this reversal happens in EconomySync's
        // background flush, and nothing was re-drawing the shop/palette - a part
        // the player saw "bought" (and maybe bolted on) silently vanished and
        // the amber reason above never showed. The UI polls this counter. Found
        // by the UX validation round, 2026-08-15.
        shopReversalSeq++;
    }

    /// <summary>Incremented every time a queued purchase/sale is reversed
    /// server-side (EconomySync flush). The dock watches it to repaint the shop
    /// and surface shopMsg - a background reversal has no user gesture to hang a
    /// redraw on.</summary>
    public static int shopReversalSeq;

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
    /// <summary>The rescue crate landed on THIS settle - the results screen
    /// reads it to show the salvage line, exactly once.</summary>
    public static bool lastRescue;
    /// <summary>Bumped by every grant that changes what the shelf/league board
    /// should show (crate, checklist rewards). MobileBuilderUI polls it - the
    /// shopReversalSeq precedent - because a grant has no user gesture to hang
    /// a repaint on, and warm-up QA caught the gusset tile reading "0 free"
    /// AFTER the crate had granted two.</summary>
    public static int uiDirtySeq;

    /// <summary>A reward the player has EARNED but not yet SEEN handed over
    /// (owen, 2026-09-04: "congrats animation whenever the user completes a
    /// guided task, rewards in a box they can open"). The grant itself is
    /// already ledgered by the time this is queued - the box is the
    /// ceremony, not the transaction, so a skipped or crashed box loses
    /// nothing. Drained by MobileBuilderUI into RewardBox, one at a time.</summary>
    public class RewardPop
    {
        public string title;                       // "FIRST BOUT"
        public string caption;                     // one line under the title
        public List<string> lines = new List<string>();   // "+10 SCRAP", "2 gussets"
        public string id;                          // pendingRewards key, or null for a pure ceremony (medal)
    }
    public static readonly List<RewardPop> rewardQueue = new List<RewardPop>();
    public static void QueueReward(string title, string caption, params string[] lines)
    {
#if !UNITY_EDITOR   // device builds (iOS + web); the editor bench suite must never see a box
        var r = new RewardPop { title = title, caption = caption };
        r.lines.AddRange(lines);
        rewardQueue.Add(r);
#endif
    }
    /// <summary>Queue a reward whose GRANT is deferred to the box opening.
    /// `id` names the grant (see GrantReward). In the editor there is no box,
    /// so the grant lands at once - the benches see exactly what they saw.</summary>
    public static void QueueReward(string id, string title, string caption, params string[] lines)
    {
        if (Data != null && id != null && !Data.pendingRewards.Contains(id)) Data.pendingRewards.Add(id);
#if !UNITY_EDITOR
        var r = new RewardPop { title = title, caption = caption, id = id };
        r.lines.AddRange(lines);
        rewardQueue.Add(r);
#else
        GrantReward(id);
#endif
    }
    /// <summary>Deliver a deferred reward exactly once. Idempotent: the id is
    /// removed from pendingRewards first, so a box that opens AND is destroyed
    /// grants once, and a re-fire finds nothing to do.</summary>
    public static void GrantReward(string id)
    {
        if (Data == null || id == null || !Data.pendingRewards.Remove(id)) return;
        if (id.StartsWith("qbox:"))
        {
            // qbox:<scrap>:<partId>:<mat>:<count> - rolled at queue time, granted here
            var f = id.Split(':');
            int scrap, count;
            if (f.Length == 5 && int.TryParse(f[1], out scrap) && int.TryParse(f[4], out count))
            {
                Txn(scrap, "toolbox");
                AddItem(f[2], f[3], count);
            }
            uiDirtySeq++;
            if (autosave) Save();
            return;
        }
        switch (id)
        {
            case "bolt":   Txn(10, "rookie checklist - first part bolted"); AddItem("beam", "Aluminum", 2); AddItem("plate", "Aluminum", 2); break;
            case "weld":   Txn(10, "rookie checklist - first seam welded"); AddItem("beamlong", "Aluminum", 1); AddItem("spindle", "Aluminum", 1); break;
            case "buy":    Txn(10, "rookie checklist - first purchase"); AddItem("cube", "Aluminum", 1); break;
            case "fight":  Txn(10, "rookie checklist - first bout fought"); AddItem("wedge", "Aluminum", 1); AddItem("gusset", "Steel", 1); break;
            case "rescue": Txn(50, "rookie salvage - one-time"); AddItem("gusset", "Steel", 2); AddItem("plate", "Steel", 1); break;
        }
        uiDirtySeq++;
        if (autosave) Save();
    }
    public static void GrantPendingRewards()
    {
        if (Data == null) return;
        foreach (var id in Data.pendingRewards.ToArray()) GrantReward(id);
    }
    // ---- QUICK FIGHT ------------------------------------------------------
    public const int QUICK_BOX_WINS = 3, QUICK_CROWN_STREAK = 5, QUICK_BOXES_PER_DAY = 6;
    /// <summary>True while the fight on the floor is a Quick Fight (no contest,
    /// 30-s clock, crusher walls). Set by BuilderManager.StartQuickFight.</summary>
    public static bool quickFight;
    public static int lastQuickPay;
    public static string lastQuickLine = "";
    public struct QuickOffer { public string oppId; public AiTier tier; public string armourMat; public string label; }
    static string Today() { return System.DateTime.UtcNow.ToString("yyyy-MM-dd"); }
    /// <summary>The furthest league the player has unlocked (0-based).</summary>
    public static int FurthestLeague()
    {
        int f = 0;
        for (int i = 0; i < CareerDB.Leagues.Length; i++) if (LeagueUnlocked(i)) f = i;
        return f;
    }
    /// <summary>Three opponents to pick from, drawn from the roster around the
    /// player's level and reseeded after every quick fight so the choice moves.</summary>
    public static List<QuickOffer> QuickPool()
    {
        var pool = new List<QuickOffer>();
        if (Data == null) return pool;
        int lvl = FurthestLeague();
        var rng = new System.Random(1000 + Data.quickFights * 7 + Data.quickWins * 3);
        var ids = new List<string>();
        foreach (var e in EnemyRoster.All) ids.Add(e.id);
        string[] mats = { null, "Aluminum", "Steel", "Titanium", "Titanium" };
        for (int k = 0; k < 3 && ids.Count > 0; k++)
        {
            int i = rng.Next(ids.Count);
            string id = ids[i]; ids.RemoveAt(i);
            // tier follows the league; ONE of the three may step up, and only
            // from the Regional Circuit on (a Garage-League player was offered
            // two Champions in three on the live site, 2026-09-07 - the loop
            // wants winnable fights, the ladder is where the wall is)
            int t = Mathf.Clamp(lvl <= 0 ? 0 : lvl <= 2 ? 1 : 2, 0, 2);
            if (k == 2 && lvl >= 2 && rng.Next(2) == 0) t = Mathf.Min(2, t + 1);
            var e = EnemyRoster.Find(id);
            pool.Add(new QuickOffer { oppId = id, tier = (AiTier)t, armourMat = mats[Mathf.Clamp(lvl, 0, 4)],
                                      label = e != null ? e.label : id.ToUpper() });
        }
        return pool;
    }
    /// <summary>Roll a toolbox: scrap by league, one part drop, +1 part per crown.
    /// Encoded into the reward id so the grant is exact and self-describing:
    /// qbox:&lt;scrap&gt;:&lt;partId&gt;:&lt;mat&gt;:&lt;count&gt;.</summary>
    public static string QuickBoxRoll(int crownsUsed, out string[] lines)
    {
        int lvl = FurthestLeague();
        var rng = new System.Random(unchecked(Data.quickFights * 31 + Data.quickWins * 17 + Data.crowns));
        string[] parts = { "beam", "plate", "wedge", "gusset", "spike", "beamlong", "wheel", "battery", "cube" };
        string[] mats  = { "Aluminum", "Aluminum", "Steel", "Steel", "Titanium" };
        string part = parts[rng.Next(parts.Length)];
        string mat = part == "wheel" ? "Rubber" : part == "gusset" ? "Steel" : mats[Mathf.Clamp(lvl, 0, 4)];
        int count = 1 + crownsUsed;
        int scrap = 40 + 30 * lvl + 20 * crownsUsed;
        string id = "qbox:" + scrap + ":" + part + ":" + mat + ":" + count;
        var lbl = P1PartDef.Palette();
        string pname = part;
        foreach (var d in lbl) if (d.id == part) { pname = d.label; break; }
        lines = new[] { "+" + scrap + " SCRAP", count + " x " + pname.ToUpper() + " (" + mat + ")" };
        return id;
    }
    /// <summary>Settle a Quick Fight: small purse, streak, box meter, crowns,
    /// the rookie first-bout box - and the daily box cap, which is the whole
    /// point: the RETURN comes from tomorrow's boxes, not from a timer.</summary>
    public static void SettleQuickFight(bool win, float dealt)
    {
        if (Data == null) return;
        int lvl = FurthestLeague();
        int pay = win ? 20 + 10 * lvl + Mathf.RoundToInt(Mathf.Min(dealt, 300f) * 0.1f) : 5;
        Txn(pay, (win ? "quick win" : "quick loss"));
        lastQuickPay = pay;
        Data.quickFights++;
        Data.fights++;
        if (Today() != Data.quickBoxDay) { Data.quickBoxDay = Today(); Data.quickBoxesToday = 0; }
        string line;
        if (win)
        {
            Data.quickWins++; Data.fightWins++;
            Data.quickStreak++;
            if (Data.quickStreak > Data.quickBestStreak) Data.quickBestStreak = Data.quickStreak;
            Data.quickWinsToBox++;
            bool crown = Data.quickStreak > 0 && Data.quickStreak % QUICK_CROWN_STREAK == 0;
            if (crown) { Data.crowns++; RBTelemetry.Once(RBTelemetry.STREAK); }
            if (Data.quickWinsToBox >= QUICK_BOX_WINS)
            {
                Data.quickWinsToBox = 0;
                if (Data.quickBoxesToday < QUICK_BOXES_PER_DAY)
                {
                    Data.quickBoxesToday++;
                    int use = Data.crowns; Data.crowns = 0;
                    string[] lines;
                    string id = QuickBoxRoll(use, out lines);
                    QueueReward(id, use > 0 ? "CROWNED TOOLBOX" : "TOOLBOX",
                                use > 0 ? "Five in a row. The crown made it heavier." : "Three wins. The Yard pays in parts.", lines);
                    RBTelemetry.Once(RBTelemetry.BOX);
                    line = "TOOLBOX EARNED  ·  " + Data.quickBoxesToday + "/" + QUICK_BOXES_PER_DAY + " today";
                }
                else line = "box cap reached - more toolboxes tomorrow  ·  streak " + Data.quickStreak;
            }
            else line = "streak " + Data.quickStreak + "  ·  " + (QUICK_BOX_WINS - Data.quickWinsToBox) + " more win" + (QUICK_BOX_WINS - Data.quickWinsToBox == 1 ? "" : "s") + " to a toolbox"
                        + (crown ? "  ·  CROWN!" : "");
        }
        else
        {
            Data.quickStreak = 0;
            line = "streak reset  ·  " + (QUICK_BOX_WINS - Data.quickWinsToBox) + " more win" + (QUICK_BOX_WINS - Data.quickWinsToBox == 1 ? "" : "s") + " to a toolbox";
        }
        lastQuickLine = line;
        if (!Data.taskFight)
        {
            Data.taskFight = true;
            QueueReward("fight", "FIRST BOUT", win ? "You fought. You won. Keep going." : "You fought. That is the part that counts.",
                        "+10 SCRAP", "1 WEDGE", "1 GUSSET (weld kit)");
        }
        Data.scrapCurve.Add(Data.scrap);
        if (Data.scrapCurve.Count > 200) Data.scrapCurve.RemoveAt(0);
        uiDirtySeq++;
        if (autosave) Save();
    }
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
        Txn(0, "medal * " + lg.name + " champion \u00b7 " + m.robot);
        // The sweep gets the same ceremony as the checklist (web QA 2026-09-04:
        // the medal was granted, the trophy case updated, and nothing popped).
        QueueReward("LEAGUE SWEPT", lg.name + " - every contest won. The next league is open.",
                    "CHAMPION MEDAL", m.robot + " is champion");
        return m;
    }

    /// <summary>Settle a career contest fight (win purse with underdog
    /// multiplier + first-win bonus; wins only — losses and practice pay 0).
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
        // The league pays WINS ONLY, once each (owen, 2026-08-13, both rules
        // the same day): a re-entry win pays nothing (WinPay returns 0) and
        // a LOSS pays nothing ever — consolation was the last unbounded
        // faucet once fees were gone.
        int pay = win ? CareerDB.WinPay(c, dealt, fightBuildValue, fightOppValue, reEntry, !reEntry) : 0;
        lastSettled = true; lastPay = pay; lastMedal = null;   // round-3: see the field comment
        Txn(pay, (win ? "win " : "loss ") + c.id);
        // ---- rookie warm-up (design 2026-09-03) ---------------------------
        lastRescue = false;
        if (!Data.taskFight)
        {
            Data.taskFight = true;
            uiDirtySeq++;
            // The wedge is what the win-path guide coaches next, and the gusset
            // makes the checklist's "weld a seam" reachable for a WINNER (the
            // crate only gives gussets on a loss; the shop wants 200).
            QueueReward("fight", "FIRST BOUT", win ? "You fought. You won. Keep going." : "You fought. That is the part that counts.",
                        "+10 SCRAP", "1 WEDGE", "1 GUSSET (weld kit)");
        }
        if (!win && !Data.rescueGranted)
        {
            // THE RESCUE CRATE: once per career, EVER, and only on a non-win
            // - winners get the purse, and this exists so a first loss is a
            // plot point instead of a wall (40% of measured first fights).
            // Contents are owen's 2026-09-03 numbers; the STEEL plate is the
            // ballast - with the gyro retired, low heavy mass is the anti-flip
            // lever a player can actually buy into. Ledgered like the kit so
            // the TxnSum == scrap audit stays true. NOT a faucet: this flag
            // never resets.
            Data.rescueGranted = true;
            lastRescue = true;
            uiDirtySeq++;
            QueueReward("rescue", "THE YARD LOOKS AFTER ROOKIES", "One-time salvage. Bolt the heavy plate LOW to stay off your back.",
                        "+50 SCRAP", "2 GUSSETS (weld kit)", "1 STEEL ARMOR PLATE");
            RBTelemetry.Once(RBTelemetry.RESCUE);
        }
        Data.fights++;
        Data.lastContest = c.id;
        Data.scrapCurve.Add(Data.scrap);
        if (Data.scrapCurve.Count > 200) Data.scrapCurve.RemoveAt(0);
        if (win)
        {
            Data.fightWins++;
            if (!reEntry) Data.doneContests.Add(c.id);
            // Client B: a signed-in first win is also a SERVER claim — queued
            // here, flushed by EconomySync when online. The mult is computed
            // exactly as WinPay computed it, because the server recomputes
            // and clamps the same formula and ITS number is the one that
            // stands (server wins). Signed-out careers (benches, the editor
            // dev door) never enqueue, so their saves never carry claims.
            if (!reEntry && LadderClient.SignedIn)
            {
                float cm = 1f + CareerDB.UNDERDOG_K *
                    Mathf.Clamp01((float)fightOppValue / Mathf.Max(1, fightBuildValue) - 1f);
                Data.pendingClaims.Add(new CareerClaim
                {
                    contestId = c.id, dealt = dealt,
                    mult = Mathf.Min(cm, CareerDB.UNDERDOG_CAP),
                    attempt = System.Guid.NewGuid().ToString("N"),
                });
            }
            // P4: the autonomy mark — "ever won this contest with the
            // autopilot driving". Deliberately includes re-entry wins: the
            // mark records the FEAT, not the first-win purse.
            if (fightAutonomous && !Data.autoDoneContests.Contains(c.id))
                Data.autoDoneContests.Add(c.id);
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
        string tag = reEntry ? "practice bout - purse already won"
                   : win     ? "contest win" : "loss - the league pays wins only";
        if (fightAutonomous) tag += " \u00b7 autonomous";
        fightAutonomous = false;   // one fight, one mark - never carries over
        lastResultLine = (reEntry || !win)
            ? string.Format("{0} \u00b7 career scrap {1}", tag, Data.scrap)
            : string.Format("+{0} scrap ({1}) \u00b7 career scrap {2}", pay, tag, Data.scrap);
        Progression.lastRewardLine = lastResultLine;
        if (autosave) Save();
        return true;
    }
}
}
