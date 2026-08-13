using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RobotBrawl.Phase0
{

/// <summary>Phase 4 (design doc §9, §12) — progression & content.
/// Scrap economy, the challenge ladder, material unlocks, budget upgrades and
/// the garage, persisted as JSON at Application.persistentDataPath.
///
/// Design choices, stated so the next agent does not have to re-derive them:
///  · UNLOCKS ARE MATERIALS ONLY (§10 \"at launch\" trio vs the premium three).
///    Locking PARTS would invalidate saved builds and gut the expressive kit
///    Phase 4a shipped; MatDB.Starter already anticipated exactly this split.
///  · The ladder is the grant path; scrap can also buy a material EARLY at
///    roughly two rung-wins of scrap — a dual path, no dead currency.
///  · Budget upgrades are the second scrap sink: 4000 cr → 6000 cr in +500
///    steps. 4000 was set at ~2.2x the dearest roster opponent; 6000 keeps
///    the same order of magnitude, a power curve rather than a cheat.
///  · Exhibition fights (the free opponent picker) pay a small flat purse and
///    never advance the ladder, so grinding stays possible but the ladder is
///    the fastest path — and rung rewards are one-time by construction
///    (advancement moves the pointer past the rung).
///  · DEV unlock-all: owen is mid-development and tests premium materials
///    constantly; a persisted toggle beats polluting the real unlock list.</summary>
[Serializable]
public class GarageSlot { public string name = ""; public string snapshot = ""; }

[Serializable]
public class ProfileData
{
    public int scrap;
    public int rung;
    public int budgetLevel;
    public int tutorialStep;   // Phase 5: 0 place, 1 test, 2 ladder, 3 done/skipped
    public bool devUnlockAll;
    public int fightsFought, fightsWon;
    public List<string> unlockedMats = new List<string>();
    public List<GarageSlot> garage = new List<GarageSlot>();
    public List<string> doneChallenges = new List<string>();   // P4c
}

public static class Progression
{
    public class Rung
    {
        public string oppId; public AiTier tier; public int reward;
        public string unlockMat; public float arenaHalf; public string label;
        public Rung(string o, AiTier t, int r, string u, float a, string l)
        { oppId = o; tier = t; reward = r; unlockMat = u; arenaHalf = a; label = l; }
    }

    /// <summary>The ladder (§9): eight rungs over the authored roster, tiers
    /// ramping Rookie→Champion. Rungs 5+ move to a tighter box (5.5 m half vs
    /// 7) — content variety with zero physics dishonesty: the box changes,
    /// the bots never do. AI pathing is straight-line pursuit, so a smaller
    /// box is strictly harder (less room to run) without new failure modes.</summary>
    // HALVED 2026-08-12 (owen) alongside CareerDB's purses — one economy,
    // one cut. Consolation and costs untouched; see CareerDB's note. The
    // R1-CRITIC anti-grind ordering still holds: max exhibition win is now
    // 75+100=175, loss consolation stays capped at 100.
    public static readonly Rung[] Ladder =
    {
        new Rung("scout",      AiTier.Rookie,    125, null,          7f,   "SCOUT"),
        new Rung("tipper",     AiTier.Rookie,    150, "Titanium",    7f,   "TIPPER"),
        new Rung("mauler",     AiTier.Rookie,    175, null,          7f,   "MAULER"),
        new Rung("ripper",     AiTier.Veteran,   225, "CarbonFiber", 7f,   "RIPPER"),
        new Rung("mauler",     AiTier.Veteran,   250, null,          5.5f, "MAULER"),
        new Rung("bulwark",    AiTier.Veteran,   300, "Tungsten",    5.5f, "BULWARK"),
        new Rung("widowmaker", AiTier.Champion,  400, null,          5.5f, "WIDOWMAKER"),
        new Rung("bulwark",    AiTier.Champion,  500, null,          5f,   "BULWARK"),
    };

    /// <summary>P4c — build-constraint challenges (§9: "beat this bot with a
    /// build under 80 kg"). One-time scrap purses on top of the normal fight
    /// pay. Constraints are validated by BuilderManager.ChallengeBlocker at
    /// the moment of the click, against the CURRENT build; pinned-material
    /// parts are exempt from material constraints.</summary>
    public class Challenge
    {
        public string id, label, desc, oppId, onlyMat;
        public AiTier tier; public int reward, maxMass, maxCost; public bool noWeapons;
    }
    public static readonly Challenge[] Challenges =
    {
        new Challenge { id = "feather", label = "FEATHERWEIGHT", desc = "Beat SCOUT (Rookie) at 400 kg or less — ABS everything and a lean frame gets you there",
                        oppId = "scout", tier = AiTier.Rookie, reward = 200, maxMass = 400 },
        new Challenge { id = "budget", label = "BUDGET BRAWL", desc = "Beat MAULER (Veteran) spending 1500 cr or less",
                        oppId = "mauler", tier = AiTier.Veteran, reward = 300, maxCost = 1500 },
        new Challenge { id = "plastic", label = "PLASTIC CHAMPION", desc = "Beat TIPPER (Rookie) with every choosable part in ABS",
                        oppId = "tipper", tier = AiTier.Rookie, reward = 250, onlyMat = "ABS" },
        new Challenge { id = "unarmed", label = "UNARMED", desc = "Beat SCOUT (Veteran) with wedges as your only weapon — ramp shots and shoves do the scoring",   // R3-CRITIC: text matched to how the fight is actually won (0 flip s measured; damage 93-27)
                        oppId = "scout", tier = AiTier.Veteran, reward = 300, noWeapons = true },
        new Challenge { id = "giant", label = "GIANT KILLER", desc = "Beat WIDOWMAKER (Champion) at 800 kg or less",
                        oppId = "widowmaker", tier = AiTier.Champion, reward = 500, maxMass = 800 },
    };
    public static bool ChallengeDone(string id) { return Data.doneChallenges.Contains(id); }

    // Runtime-only fight context — set by the builder, read by the reward
    // pass, never serialized.
    public static int activeRungIndex = -1;   // -1 = exhibition
    public static bool rewarded = false;      // one settlement per fight; StartFight resets
    public static int activeChallengeIdx = -1;  // P4c: -1 = not a challenge fight
    public static string lastRewardLine = "";

    static ProfileData data;

    static string FilePath()
    { return Path.Combine(Application.persistentDataPath, "robotbrawl_profile.json"); }

    public static ProfileData Data
    {
        get { if (data == null) Load(); return data; }
    }

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath()))
                data = JsonUtility.FromJson<ProfileData>(File.ReadAllText(FilePath()));
        }
        catch (Exception e) { Debug.LogWarning("Progression: profile load failed - " + e.Message); }
        if (data == null) data = new ProfileData();
        if (data.unlockedMats == null) data.unlockedMats = new List<string>();
        if (data.unlockedMats.Count == 0) data.unlockedMats.AddRange(MatDB.Starter);
        if (data.garage == null) data.garage = new List<GarageSlot>();
        while (data.garage.Count < 3) data.garage.Add(new GarageSlot());
        if (data.doneChallenges == null) data.doneChallenges = new List<string>();
    }

    public static void Save()
    {
        try { File.WriteAllText(FilePath(), JsonUtility.ToJson(Data, true)); }
        catch (Exception e) { Debug.LogWarning("Progression: profile save failed - " + e.Message); }
    }

    public static Rung CurrentRung()
    { return Data.rung >= 0 && Data.rung < Ladder.Length ? Ladder[Data.rung] : null; }

    public static bool MatUnlocked(string key)
    {
        // All materials are available from the start (design decision 2026-07-30).
        // Progression now gates opponents and challenges, not the shop shelf.
        return MatDB.Canon(key) != null;
    }

    /// <summary>Scrap price to unlock a material EARLY, before its rung.
    /// Roughly two rung-wins each — real money, not a paywall.</summary>
    public static int MatBuyCost(string key)
    {
        string c = MatDB.Canon(key);
        if (c == "Titanium")    return 900;
        if (c == "CarbonFiber") return 1400;
        if (c == "Tungsten")    return 2000;
        return 99999;
    }

    public static bool TryBuyMat(string key)
    {
        string c = MatDB.Canon(key);
        if (c == null || MatUnlocked(c)) return false;
        int cost = MatBuyCost(c);
        if (Data.scrap < cost) return false;
        Data.scrap -= cost;
        Data.unlockedMats.Add(c);
        Save();
        return true;
    }

    public static string LockHint(string key)
    {
        // R1-CRITIC FIX (finding 4): asked about an already-unlocked material
        // this returned stale "locked" text (measured right after the rung-2
        // Titanium grant). Answer the actual state first.
        if (MatUnlocked(key)) return MatDB.Get(key).name + " is unlocked.";
        string c = MatDB.Canon(key);
        int at = -1;
        for (int i = 0; i < Ladder.Length; i++)
            if (Ladder[i].unlockMat == c) { at = i; break; }
        return string.Format("{0} is locked — beat ladder rung {1}, or buy early for {2} scrap (you have {3}).",
            MatDB.Get(key).name, at >= 0 ? (at + 1).ToString() : "?", MatBuyCost(c), Data.scrap);
    }

    // ---- budget upgrades ---------------------------------------------------
    public static int BudgetFor()
    { return 0; }   // Phase 5: credit limit removed — builds are unconstrained

    public static int BudgetUpgradeCost()
    {
        // R2-CRITIC FIX (finding 3): 1500/2500/3500/4500 = 12,000 total against
        // ~9,200 one-time income starved the endgame. 750/step = 10,500 total,
        // and the post-ladder title-defense purse (1,125/win) makes the top
        // levels ~5 championship defenses away instead of a +25-consolation grind.
        return 1500 + 750 * Data.budgetLevel;
    }

    public static bool TryBuyBudget()
    {
        if (Data.budgetLevel >= 4) return false;
        int cost = BudgetUpgradeCost();
        if (Data.scrap < cost) return false;
        Data.scrap -= cost;
        Data.budgetLevel++;
        Save();
        return true;
    }

    // ---- match settlement --------------------------------------------------
    /// <summary>Called once from FightManager.End. Guarded by `rewarded`
    /// (StartFight resets it), so a rematch settles again but a single match
    /// can never settle twice. Ladder advancement only when THIS fight was
    /// started as the CURRENT rung — replaying an old rung via exhibition
    /// cannot advance anything.</summary>
    public static void OnMatchEnd(bool win, float dealt)
    {
        if (rewarded) return;
        rewarded = true;
        // C3: a career contest settles on the CAREER ledger, not this profile.
        if (Career.active && Career.SettleFight(win, dealt)) return;
        int ri = activeRungIndex;
        Rung r = ri >= 0 && ri < Ladder.Length ? Ladder[ri] : null;
        int basePay = r != null ? r.reward : 75;    // exhibitions: small flat purse (halved 2026-08-12 with the league economy)
        // R1-CRITIC FIX (finding 3): loss consolation used to scale with the
        // rung reward (25%), so AFK-losing to rung 8 (+250) out-earned
        // actively WINNING exhibitions (~223 max) — a degenerate grind.
        // Consolation is now effort-based and capped at 100: 25 flat plus a
        // quarter of the damage you actually dealt (up to 300).
        int pay = win ? basePay + Mathf.RoundToInt(Mathf.Min(dealt, 400f) * 0.25f)   // dmg slope halved with the purses, 2026-08-12
                      : Mathf.Min(100, 25 + Mathf.RoundToInt(Mathf.Min(dealt, 300f) * 0.25f));
        Data.scrap += pay;
        Data.fightsFought++;
        if (win) Data.fightsWon++;
        string extra = "";
        if (win && r != null && ri == Data.rung)
        {
            Data.rung++;
            // Materials are all unlocked from the start; rung material grants retired.
            extra += Data.rung < Ladder.Length
                ? string.Format(" · rung {0} of {1} awaits", Data.rung + 1, Ladder.Length)
                : " · LADDER COMPLETE";
        }

        // P4c: one-time challenge purse on top of the fight pay.
        if (win && activeChallengeIdx >= 0 && activeChallengeIdx < Challenges.Length)
        {
            var ch = Challenges[activeChallengeIdx];
            if (!Data.doneChallenges.Contains(ch.id))
            {
                Data.doneChallenges.Add(ch.id);
                Data.scrap += ch.reward;
                extra += " · CHALLENGE " + ch.label + " +" + ch.reward + " scrap";
            }
            else extra += " · challenge " + ch.label + " (already complete)";
        }
        lastRewardLine = string.Format("+{0} scrap ({1}){2}   ·   scrap total {3}",
            pay,
            r != null ? (win ? "rung victory" : "rung attempt")
              : activeChallengeIdx >= 0 ? (win ? "challenge win" : "challenge attempt")
              : (win ? "exhibition win" : "exhibition"),
            extra, Data.scrap);
        Save();
    }
}
}
