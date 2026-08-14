// ⚠ EDITOR AND DEVELOPMENT BUILDS ONLY — 2026-08-10.
// This is a QA harness. It was compiling into the SHIPPED iOS player: 44 such
// files, ~13k lines, and the string "dev-only-worker-key" was verified present
// in build/ios global-metadata.dat. Dead weight in the binary, and a surface
// that can register accounts and start fights inside a player's app.
// Nothing in the product references any harness — checked against every
// product script, every scene and every prefab before this guard was added.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RobotBrawl.Phase0
{
/// <summary>C2 harness - TouchSmoke's sibling for the career economy:
/// buy / sell / swap / guard rails / ledger audit, driven through the same
/// uGUI buttons a player taps. Runs on an IN-MEMORY career with autosave
/// off - the owner's career file is never touched.</summary>
public class CareerSmoke : MonoBehaviour
{
    public static bool finished;
    public static CareerSmoke Run()
    {
        finished = false;
        return new GameObject("career_smoke").AddComponent<CareerSmoke>();
    }

    readonly List<string> log = new List<string>();
    int passed, failed;
    bool tipStale, tipMisnumbered, tipEmpty;
    void Check(bool ok, string what)
    {
        if (ok) passed++; else failed++;
        log.Add((ok ? "PASS  " : "FAIL  ") + what);
    }

    static Button Btn(string prefix)
    {
        // Include inactive: the BUILD tab's tiles are hidden while the SHOP
        // tab is up, but their labels (the stock badges) still update.
        foreach (var b in Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var t = b.GetComponentInChildren<Text>();
            if (t != null && t.text.StartsWith(prefix)) return b;
        }
        return null;
    }
    static bool Tap(string prefix)
    {
        var b = Btn(prefix);
        if (b == null) return false;
        b.onClick.Invoke();
        return true;
    }
    /// <summary>Row-targeted tap by GameObject name (BUY/SELL/SWAP labels
    /// repeat per row, so prefix search cannot address a specific part).</summary>
    static bool TapNamed(string goName)
    {
        var go = GameObject.Find(goName);
        if (go == null) return false;
        var b = go.GetComponent<Button>();
        if (b == null) return false;
        b.onClick.Invoke();
        return true;
    }
    /// <summary>Like TapNamed's lookup but sees INACTIVE objects too -
    /// GameObject.Find does not, and half the gated buttons live on tabs that
    /// are not currently up.</summary>
    static Button ByName(string goName)
    {
        foreach (var b in Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (b.gameObject.name == goName) return b;
        return null;
    }
    /// <summary>The LABEL on tab N of the dock strip, or "" if that tab does
    /// not exist. Addresses the tab by its GameObject name ("tab4"), because
    /// a label search cannot tell a TAB called PARTS from a SHOP SECTION
    /// called PARTS — which is precisely how this bench once reported a
    /// deleted tab as resurrected.</summary>
    static string TabLabel(int i)
    {
        var b = ByName("tab" + i);
        if (b == null) return "";
        var t = b.GetComponentInChildren<Text>(true);
        return t != null ? t.text : "";
    }

    /// <summary>Screen position of the core (true) or the most recently
    /// placed part (false); zero when unavailable.</summary>
    static Vector3 PartOnScreen(bool core)
    {
        var bmx = Object.FindFirstObjectByType<BuilderManager>();
        if (bmx == null || bmx.placed.Count == 0 || Camera.main == null) return Vector3.zero;
        var px = core ? bmx.placed[0] : bmx.placed[bmx.placed.Count - 1];
        if (px.go == null) return Vector3.zero;
        Vector3 sp = Camera.main.WorldToScreenPoint(px.go.transform.position);
        return sp.z <= 0f ? Vector3.zero : sp;
    }

    IEnumerator Start()
    {
        yield return null;
        // This bench rides the REAL auto-boot on purpose (it asserts the boot
        // rules further down), so unlike the self-booting benches it meets
        // the LOGIN GATE when no session is stored. Walk through the gate's
        // editor-only dev door — which also makes this bench the cover for
        // that door: if DEV SKIP stops resuming the boot, this fails here.
        if (LoginGate.inst != null)
        {
            LoginGate.inst.OnDevSkip();
            yield return null; yield return null;
        }
        // SIGN OUT IN MEMORY for the whole run — found 2026-08-13, five shop
        // legs red: owen's editor carried a PERSISTED session (he signed in
        // through the gate while testing), so the bench booted signed in and
        // the shop's offline gate — working exactly as designed — refused
        // every bench purchase ("buying needs a connection"). This bench
        // asserts the SIGNED-OUT local shop, so it must run signed out. The
        // token is stashed and restored in Finish, NOT Logout()ed — clearing
        // owen's persisted session would be the bench mutating owner state.
        stashedToken = LadderClient.Token;
        LadderClient.Token = "";
        var bm = Object.FindFirstObjectByType<BuilderManager>();
        var ui = MobileBuilderUI.inst;
        if (bm == null || ui == null)
        { Check(false, "builder + mobile UI present"); Finish(); yield break; }

        // R6 (owen 2026-08-04): the dock now starts CLOSED on a screen too
        // small to afford it, which is a phone. Nearly every assertion below
        // taps something inside the content panel, so pin it open - a suite
        // that passes or fails by which simulator was last selected is not
        // measuring the game. C15 below tests the collapse itself, deliberately
        // and in one place.
        ui.SetDockOpen(true);
        yield return null;

        var savedData = Career.Data;
        bool savedActive = Career.active;
        // A COUNTED hold, not a flag swap: two harnesses overlapping each
        // captured `true`, and the first to finish restored it while the other
        // was still fighting. See Career.SuspendAutosave.
        var autosaveHold = Career.SuspendAutosave();
        Career.Data = new CareerData();
        Career.active = true;
        int exp = 0;
        Career.Txn(500, "test grant"); exp += 500;
        Career.AddItem("beam", "Aluminum", 1);
        bm.ActiveMatKey = "Aluminum";
        yield return null; yield return null;

        var shopTab = Btn("SHOP");
        Check(shopTab != null && shopTab.gameObject.activeInHierarchy,
              "SHOP tab appears in career mode");
        Tap("SHOP"); yield return null;

        // CUBE (2026-08-12) sits before Beam in the palette, so Beam's
        // accordion section is no longer the default-open first row. Open it
        // by id-scan, the same idiom the later shop blocks already use — the
        // renumbering trap, caught on the BENCH side this time.
        if (GameObject.Find("buy_beam_Aluminum") == null)
        {
            int beamSec0 = -1;
            for (int i = 1; i < bm.PaletteCount; i++)
                if (bm.PartId(i) == "beam") beamSec0 = i;
            TapNamed("shophead_" + beamSec0); yield return null;
        }
        int beamP = CareerDB.PartPrice("beam", "Aluminum");
        int sellP = CareerDB.SellPrice("beam", "Aluminum");
        int s0 = Career.Data.scrap;
        // OWEN 2026-08-02: addressed by part+material now. The old
        // "shopbuy_1" was a palette-index name that stopped existing when the
        // shop became one row per part PER MATERIAL; TapNamed returned false
        // and every assertion below it had been failing silently since.
        Check(TapNamed("buy_beam_Aluminum"), "shop rows are reachable by part+material");
        yield return null;
        exp -= beamP;
        Check(Career.Data.scrap == s0 - beamP && Career.CountOf("beam", "Aluminum") == 2,
              "buy: scrap -price, owned +1");
        // The tile prints REMAINING stock ("2 free"), not a xN owned badge -
        // it changed when the palette started showing what is still spare
        // rather than what is owned. The assertion follows the UI, and the
        // thing under test is unchanged: buying updates the palette.
        var beamTile = Btn("Beam");
        Check(beamTile != null && beamTile.GetComponentInChildren<Text>().text.Contains("2 free"),
              "buy: part tile stock badge follows to 2 free");

        // deliberately broke (drain THROUGH the ledger so the audit holds)
        int drain = Career.Data.scrap - 3;
        Career.Txn(-drain, "test drain"); exp -= drain;
        int s1 = Career.Data.scrap;
        TapNamed("buy_beam_Aluminum"); yield return null;
        Check(Career.Data.scrap == s1 && Career.CountOf("beam", "Aluminum") == 2
              && Career.shopMsg.Contains("Not enough"),
              "insufficient funds: refused with the amber message, nothing changes");

        TapNamed("sell_beam_Aluminum"); yield return null;
        exp += sellP;
        Check(Career.CountOf("beam", "Aluminum") == 1 && Career.Data.scrap == s1 + sellP,
              "sell: owned -1, scrap +50% of price");

        // ---- in-use guard rail: bolt the last owned beam onto the build ----
        Tap("BUILD"); yield return null;
        Tap("Beam"); yield return null;
        Phase0Input.debugPointer = true;
        Phase0Input.debugMousePos = Camera.main.WorldToScreenPoint(new Vector3(0f, 0.85f, 0f));
        yield return new WaitForSeconds(0.6f);   // outlive the tab-switch click grace
        int guard = 0;
        while (!bm.TestGhostValid && guard++ < 30) yield return null;
        int np = bm.PlacedCount;
        Phase0Input.DebugClick(0);
        yield return null; yield return null;
        Check(bm.PlacedCount == np + 1, "setup: owned beam placed for the in-use test");
        Phase0Input.debugPointer = false;
        Tap("DONE"); yield return null;
        Tap("SHOP"); yield return null;

        int owned = Career.CountOf("beam", "Aluminum");
        TapNamed("sell_beam_Aluminum"); yield return null;
        Check(Career.CountOf("beam", "Aluminum") == owned,
              "in-use sell: first tap arms, does not sell");
        TapNamed("sell_beam_Aluminum"); yield return null;
        exp += sellP;
        Check(Career.CountOf("beam", "Aluminum") == owned - 1,
              "in-use sell: second tap sells anyway");

        // The REWORK round-trip that lived here is gone with the button
        // (owen, 2026-08-05). Its grant and its cost both came off `exp`, so
        // removing the pair leaves the running total below untouched - which
        // is exactly what the next two checks are for.

        Check(Career.Data.scrap == exp, "round-trip: scrap equals the expected running total (" + exp + ")");
        Check(Career.TxnSum() == Career.Data.scrap, "ledger audits: sum of all txns == scrap");

        // ==== C3: leagues, contests, enrollment, settlement ====
        Career.Data = new CareerData();
        Career.Txn(1000, "c3 grant");
        // own everything already bolted to the build (the C2 section left a
        // beam placed), or CareerShortfall fires before every C3 check
        Career.AddItem("beam", "Aluminum", 1);
        Career.lastResultLine = "";
        // give the build a wheel + battery so Validate() passes
        int wi = -1, bati = -1;
        for (int i = 1; i < bm.PaletteCount; i++)
        {
            if (bm.PartId(i) == "wheel") wi = i;
            if (bm.PartId(i) == "battery") bati = i;
        }
        Career.AddItem("wheel", bm.PartMatKey(wi), 2);
        Career.AddItem("battery", bm.PartMatKey(bati), 1);
        Tap("BUILD"); yield return null;
        bool placedAll = true;
        foreach (int pi in new[] { wi, bati })
        {
            bm.SelectPart(pi); yield return null;
            Phase0Input.debugPointer = true;
            yield return new WaitForSeconds(0.5f);
            bool ok2 = false;
            for (int attempt = 0; attempt < 24 && !ok2; attempt++)
            {
                Vector3 cand = attempt % 2 == 0 ? PartOnScreen(true) : PartOnScreen(false);
                if (cand == Vector3.zero) { yield return null; continue; }
                cand.x += (attempt / 4) * 9f * (attempt % 4 < 2 ? 1f : -1f);
                cand.y += (attempt / 8) * 7f;
                Phase0Input.debugMousePos = cand;
                yield return null; yield return null;
                ok2 = bm.TestGhostValid;
            }
            int np2 = bm.PlacedCount;
            Phase0Input.DebugClick(0);
            yield return null; yield return null;
            if (bm.PlacedCount != np2 + 1) placedAll = false;
            bm.SelectPart(bm.SelectedPart); yield return null;   // deselect
        }
        Phase0Input.debugPointer = false;
        Check(placedAll && bm.Validate() == null, "c3 setup: wheel + battery make the build fight-legal");

        // over-cap + size-box refusals, message wording included
        var fakeCap = new CareerDB.League("LX", "Test League", "X", "x", 10f, new CareerDB.Contest[0]);
        string capMsg = bm.CareerValidate(fakeCap);
        Check(capMsg != null && capMsg.Contains("over the Test League cap"),
              "over-cap build refused with the specific kg message");
        // OWEN 2026-08-03: the size box is gone - weight is the only
        // enrollment rule now. What replaces those assertions is the one that
        // matters after a rule is deleted: that a build which would have been
        // refused for SIZE is now accepted, so the removal is proven rather
        // than merely uncalled.
        var roomyCap = new CareerDB.League("LZ", "Roomy", "Z", "z", 99999f, new CareerDB.Contest[0]);
        Check(bm.CareerValidate(roomyCap) == null,
              "with a huge cap nothing else refuses the build \u2014 weight is the only rule");

        bm.StartCareerFight(2, 0); yield return null;
        Check(bm.LastMessage != null && bm.LastMessage.Contains("locked"),
              "locked league refused with the unlock hint");

        // enroll L1C1 for real and win it (AI-driven fight, harness verdict)
        var L1C1 = CareerDB.Leagues[0].contests[0];
        int sBefore = Career.Data.scrap;
        bm.StartCareerFight(0, 0);
        yield return null; yield return null;
        var fm = Object.FindFirstObjectByType<FightManager>();
        Check(fm != null && Career.activeContest == "L1C1", "enrollment starts the L1C1 fight");
        int bv = Career.fightBuildValue, ov2 = Career.fightOppValue;
        float d0 = fm != null ? fm.player.dealt : 0f;
        if (fm != null) fm.End(FightManager.Outcome.PlayerWin, "harness win");
        yield return null; yield return null;
        int expPay = CareerDB.WinPay(L1C1, d0, bv, ov2, false, true);
        Check(Career.Data.scrap == sBefore + expPay && Career.Data.doneContests.Contains("L1C1"),
              "first win pays WinPay to the credit (underdog mult + first-win bonus)");
        bm.BackToBuild(); yield return null; yield return null; yield return null;

        // FIRST-WIN RULE (owen, 2026-08-13): re-entry is a practice bout.
        // Zero pay on a WIN, zero consolation on a LOSS, and the scrap total
        // is checked for byte-equality — "less than before" would also pass
        // an accidental partial payment, and a partial payment is the bug.
        sBefore = Career.Data.scrap;
        bm.StartCareerFight(0, 0);
        yield return null; yield return null;
        fm = Object.FindFirstObjectByType<FightManager>();
        if (fm != null) fm.End(FightManager.Outcome.PlayerWin, "harness re-entry win");
        yield return null; yield return null;
        Check(Career.Data.scrap == sBefore,
              "a re-entry WIN pays nothing — practice, the purse was won already");
        Check(Career.lastResultLine != null && Career.lastResultLine.Contains("practice"),
              "…and the result line says practice, not a purse");
        bm.BackToBuild(); yield return null; yield return null; yield return null;

        sBefore = Career.Data.scrap;
        bm.StartCareerFight(0, 0);
        yield return null; yield return null;
        fm = Object.FindFirstObjectByType<FightManager>();
        if (fm != null) fm.End(FightManager.Outcome.PlayerLoss, "harness re-entry loss");
        yield return null; yield return null;
        Check(Career.Data.scrap == sBefore,
              "a re-entry LOSS pays no consolation — or losing practice would out-earn winning it");
        bm.BackToBuild(); yield return null; yield return null; yield return null;

        // LOSSES PAY NOTHING (owen, 2026-08-13: consolation removed — the
        // last unbounded faucet once fees were gone). The league pays wins
        // only, and a loss leaves the contest unbeaten so its purse remains.
        sBefore = Career.Data.scrap;
        bm.StartCareerFight(0, 1);
        yield return null; yield return null;
        fm = Object.FindFirstObjectByType<FightManager>();
        if (fm != null) fm.End(FightManager.Outcome.PlayerLoss, "harness loss");
        yield return null; yield return null;
        Check(Career.Data.scrap == sBefore && !Career.Data.doneContests.Contains("L1C2"),
              "a loss pays NOTHING and the contest stays unbeaten (its purse remains winnable)");
        Check(Career.lastResultLine != null && Career.lastResultLine.Contains("wins only"),
              "…and the result line says the league pays wins only");
        bm.BackToBuild(); yield return null; yield return null; yield return null;

        // ENTRY FEES ARE GONE (owen, 2026-08-13). The old checks here proved
        // fees were charged and refused correctly; the new contract is the
        // opposite and is proven from both sides: a BROKE player enrolls
        // anywhere, and no enrollment ever moves scrap.
        foreach (var lgx in new[] { CareerDB.Leagues[0], CareerDB.Leagues[1] })
            foreach (var cx in lgx.contests)
                if (!Career.Data.doneContests.Contains(cx.id)) Career.Data.doneContests.Add(cx.id);
        Career.Txn(-(Career.Data.scrap - 10), "c3 drain");
        bm.StartCareerFight(2, 0);
        yield return null; yield return null;
        fm = Object.FindFirstObjectByType<FightManager>();
        Check(fm != null && Career.activeContest == "L3C1",
              "a player with 10 scrap enrolls in L3 — no fee can block an enrollment");
        Check(Career.Data.scrap == 10, "…and enrollment moved no scrap");
        if (fm != null) fm.End(FightManager.Outcome.PlayerWin, "harness L3C1 win");
        yield return null; yield return null;
        Check(Career.Data.doneContests.Contains("L3C1") && Career.Data.scrap > 10,
              "the broke player's first win still pays the purse");
        bm.BackToBuild(); yield return null; yield return null; yield return null;

        // …and the practice half survives the fee removal: re-entry moves
        // nothing at enrollment AND nothing at settlement, either outcome.
        sBefore = Career.Data.scrap;
        bm.StartCareerFight(2, 0);
        yield return null; yield return null;
        fm = Object.FindFirstObjectByType<FightManager>();
        Check(fm != null && Career.Data.scrap == sBefore,
              "re-entering a beaten contest moves no scrap at enrollment");
        if (fm != null) fm.End(FightManager.Outcome.PlayerLoss, "harness practice loss");
        yield return null; yield return null;
        Check(Career.Data.scrap == sBefore, "…and the practice loss pays nothing");
        bm.BackToBuild(); yield return null; yield return null; yield return null;
        Check(Career.TxnSum() == Career.Data.scrap, "the ledger audits with fees gone");

        // scouting renders every roster bot on the turntable
        var seenOpp = new List<string>();
        int rendered = 0;
        for (int li = 0; li < CareerDB.Leagues.Length; li++)
            for (int ci = 0; ci < CareerDB.Leagues[li].contests.Length; ci++)
            {
                var cx = CareerDB.Leagues[li].contests[ci];
                if (seenOpp.Contains(cx.oppId)) continue;
                seenOpp.Add(cx.oppId);
                bm.StartScout(li, ci);
                yield return null;
                if (bm.Scouting) rendered++;
                bm.EndScout();
                yield return null;
            }
        // Count the ROSTER, do not hard-code it. This read "all six" and
        // failed at 8/6 the moment MILLSTONE and BASTION joined - i.e. it
        // failed for succeeding, which is the least useful kind of red.
        int rosterN = EnemyRoster.All.Length;
        Check(rendered == rosterN,
              "scouting renders every roster bot (" + rendered + "/" + rosterN + ")");
        Career.targetLeagueIdx = 0;
        bm.LoadSnapshot(bm.SnapshotString());   // clears the stale amber message
        Tap("BUILD"); yield return null; yield return null;
        Check(ui.StatsLine.Contains("/1500 kg"), "builder stats bar shows the kg/cap readout");

        // ==== C4: the workshop - hands-on run from a fresh kit ====
        Career.Data = new CareerData();
        Career.lastResultLine = "";
        foreach (var kk in CareerDB.StarterKit()) Career.AddItem(kk.partId, kk.mat, kk.count);
        Career.Txn(0, "kit granted (test)");
        // own whatever the current build already uses (mat pinning can differ
        // from the kit's nominal entries), then clear the stale message
        foreach (var it in bm.CareerShortfallItems()) Career.AddItem(it.partId, it.mat, it.count);
        bm.LoadSnapshot(bm.SnapshotString());
        yield return null; yield return null;
        // ARENA replaced TROPHIES at index 4 (owen, 2026-08-10). TROPHIES and
        // PARTS are asserted ABSENT rather than just dropped from the list: a
        // tab that comes back by accident is exactly what this check is for.
        //
        // ⚠ ADDRESSED BY TAB, NOT BY LABEL, and this check FAILED the moment
        // it was not. Btn() finds ANY button whose text starts with the
        // prefix, so the SHOP tab's new PARTS/COSMETICS section switch matched
        // `Btn("PARTS")` and this read as "the deleted PARTS tab is back".
        // It was not — a different control simply shares a word with it.
        // TapNamed's own comment says exactly this one row down ("labels
        // repeat per row, so prefix search cannot address a specific part");
        // it is just as true of a tab as of a shop row.
        Check(TabLabel(1) == "LEAGUE" && TabLabel(2) == "ROBOTS"
              && TabLabel(3) == "SHOP" && TabLabel(4) == "ARENA"
              && TabLabel(5) == "PROGRAM" && ByName("tab6") == null,
              "workshop tabs: LEAGUE / ROBOTS / SHOP / ARENA / PROGRAM in career,"
              + " and no PARTS or TROPHIES  (got " + TabLabel(4) + " at index 4)");
        Check(Career.Data.tutorialStep == 0, "fresh career starts at onboarding step 0");

        Tap("ROBOTS"); yield return null;
        var nameGO = GameObject.Find("namein");
        if (nameGO != null) nameGO.GetComponent<InputField>().text = "VICE GRIP";
        TapNamed("stnew"); yield return null;
        Check(Career.Data.stable.Count == 1 && Career.Data.stable[0].name == "VICE GRIP"
              && Career.Data.activeRobot == 0 && Career.Data.tutorialStep == 1,
              "NEW ROBOT founds VICE GRIP, makes it active, onboarding -> step 1");
        // OWEN 2026-08-02: SAVE moved off ROBOTS onto the build bar, where the
        // work happens, so the harness has to go where the player now goes.
        Tap("BUILD"); yield return null;
        // OWEN 2026-08-03: SAVE asks overwrite-or-new when a robot is open, so
        // the tap that used to commit now only opens the window. The harness
        // has to answer it, exactly as a player does - this assertion went red
        // the moment the confirmation shipped and it was RIGHT to.
        TapNamed("bsave"); yield return null;
        bool c4Confirmed = TapNamed("savedlg_over"); yield return null;
        Tap("ROBOTS"); yield return null;
        Check(c4Confirmed && Career.Data.stable[0].snapshot.Length > 0
              && Career.Data.tutorialStep == 2,
              "SAVE \u2192 OVERWRITE stores the fight-legal build, onboarding -> step 2");

        int s4 = Career.Data.scrap;
        bm.StartCareerFight(0, 0);
        yield return null; yield return null;
        fm = Object.FindFirstObjectByType<FightManager>();
        float d4 = fm != null ? fm.player.dealt : 0f;
        if (fm != null) fm.End(FightManager.Outcome.PlayerWin, "c4 harness win");
        yield return null; yield return null;
        int expPay4 = CareerDB.WinPay(CareerDB.Leagues[0].contests[0], d4, Career.fightBuildValue, Career.fightOppValue, false, true);
        Check(Career.Data.scrap == s4 + expPay4 && Career.Data.stable[0].wins == 1
              && Career.Data.tutorialStep == 3,
              "first fight: purse to the credit, VICE GRIP's record 1-0, onboarding done");
        bm.BackToBuild(); yield return null; yield return null; yield return null;

        Tap("SHOP"); yield return null;
        bm.ActiveMatKey = "Aluminum";
        yield return null;
        int beams4 = Career.CountOf("beam", "Aluminum");
        Career.Txn(200, "c4 shop grant");
        // The accordion is SINGLE-open and its state is UI state, not career
        // state - resetting Career.Data above did not reopen anything. A
        // section opened earlier closes beam, and a closed section
        // SetActive(false)s its material rows, so
        // GameObject.Find could not see buy_beam_Aluminum and TapNamed
        // returned false. The purchase never happened; the assertion was
        // reporting an untouched shelf, not a broken shop.
        // ASK, do not toggle. Removing the REWORK test upstream stopped the
        // bracket section being opened, which meant beam was still open and
        // this tap CLOSED it - the purchase then found nothing and the check
        // reported a broken shop. That is the third time this accordion has
        // caught a test out, so this now uses the same rule C8 already does:
        // tap the header only when the row is not already reachable.
        if (GameObject.Find("buy_beam_Aluminum") == null)
        {
            int beamSec = -1;
            for (int i = 1; i < bm.PaletteCount; i++)
                if (bm.PartId(i) == "beam") beamSec = i;
            TapNamed("shophead_" + beamSec); yield return null;
        }
        // Assert the TAP, not just its effect. A silent false here is exactly
        // how the whole shop section stayed dead without anyone noticing.
        bool buy4 = TapNamed("buy_beam_Aluminum"); yield return null;
        Check(buy4 && Career.CountOf("beam", "Aluminum") == beams4 + 1, "shop purchase lands in the shelf");

        // drafting table: place a Tungsten beam we do not own, blueprint it,
        // then one-tap convert buys exactly the missing part
        // OWEN 2026-08-02: the stdraft toggle no longer exists - drafting is
        // derived from having a design open, so NEW DRAFT is the way in.
        Tap("ROBOTS"); yield return null;
        if (nameGO != null) nameGO.GetComponent<InputField>().text = "DREAM MACHINE";
        TapNamed("stnewbp"); yield return null;
        Check(Career.Data.blueprints.Count == 1 && Career.Data.blueprints[0].name == "DREAM MACHINE",
              "NEW DRAFT saves the design and opens it");
        Check(Career.Drafting, "editing a design unlocks everything");
        Check(BuilderManager.bannerNow == "draft",
              "C6.5: DRAFT banner up while drafting (" + BuilderManager.bannerNow + ")");
        Tap("BUILD"); yield return null;
        bm.ActiveMatKey = "Tungsten";
        int bi4 = -1;
        for (int i = 1; i < bm.PaletteCount; i++)
            if (bi4 < 0 && bm.PartId(i) == "beam") bi4 = i;
        bm.SelectPart(bi4); yield return null;
        Phase0Input.debugPointer = true;
        yield return new WaitForSeconds(0.5f);
        bool okT = false;
        for (int attempt = 0; attempt < 24 && !okT; attempt++)
        {
            Vector3 cand = attempt % 2 == 0 ? PartOnScreen(true) : PartOnScreen(false);
            if (cand == Vector3.zero) { yield return null; continue; }
            cand.x += (attempt / 4) * 9f * (attempt % 4 < 2 ? 1f : -1f);
            cand.y += (attempt / 8) * 7f;
            Phase0Input.debugMousePos = cand;
            yield return null; yield return null;
            okT = bm.TestGhostValid;
        }
        int np4 = bm.PlacedCount;
        Phase0Input.DebugClick(0);
        yield return null; yield return null;
        Phase0Input.debugPointer = false;
        bm.SelectPart(bm.SelectedPart); yield return null;
        Check(bm.PlacedCount == np4 + 1 && Career.CountOf("beam", "Tungsten") == 0,
              "draft mode places an unowned Tungsten beam");
        // The build bar's SAVE writes back into the OPEN design. Saving twice
        // must update it, not append a second copy - that was the whole reason
        // activeBlueprint exists.
        TapNamed("bsave"); yield return null;
        TapNamed("bsave"); yield return null;
        Check(Career.Data.blueprints.Count == 1,
              "SAVE updates the open draft instead of duplicating it");
        Tap("ROBOTS"); yield return null;
        int quote4 = bm.ConvertQuote();
        Career.Txn(3000, "c4 convert grant");
        int s5 = Career.Data.scrap;
        TapNamed("bpconv"); yield return null;
        Check(!Career.Drafting && Career.CountOf("beam", "Tungsten") == 1
              && Career.Data.scrap == s5 - quote4 && bm.CareerShortfallItems().Count == 0,
              "CONVERT buys exactly the missing parts and leaves draft mode");

        // ---- the SHOP row is the one place a part explains itself ----
        // The PARTS tab is gone (owen, 2026-08-05), and with it the shelf
        // rows this section used to walk. The claims it protected did not go
        // with it, they MOVED: ownership is on the shop row, and the row now
        // also carries the part's description and its HP per kg - the figure a
        // weight cap actually spends. Asserting the CONTENT rather than the
        // tab is what stops this becoming a test of where a number lives.
        Tap("SHOP"); yield return null;
        if (GameObject.Find("buy_beam_Aluminum") == null)
        {
            int beamSecP = -1;
            for (int i = 1; i < bm.PaletteCount; i++) if (bm.PartId(i) == "beam") beamSecP = i;
            TapNamed("shophead_" + beamSecP); yield return null;
        }
        yield return null;
        var descGO = GameObject.Find("shopdesc_" + bm.PaletteIndexOf("beam"));
        string descTxt = descGO != null ? descGO.GetComponentInChildren<Text>().text : "";
        Check(descGO != null && descTxt == bm.PartDesc(bm.PaletteIndexOf("beam"))
              && descTxt.Length > 0,
              "an open SHOP part shows its description, which touch could never reach before");
        // And it must FIT. The description row is the newest thing on this
        // screen and the palette tiles already proved that a row which merely
        // renders is not a row the player can read.
        var descT = descGO != null ? descGO.GetComponentInChildren<Text>() : null;
        Check(descT != null && descT.preferredHeight <= descT.rectTransform.rect.height + 1f,
              "the description row is tall enough for its own text");
        // ...and it must not be BLOATED either. The check above is
        // one-sided, so it passed all through owen's 2026-08-08 report:
        // on the FIRST open a description row had never been laid out,
        // measured its wrap at the default 100-wide rect and latched a
        // 142-unit height for a 14-unit line. House rule (CriticLoop6):
        // where a quality is measurable, measure it over EVERYTHING.
        // Every part below is a genuine first open.
        var bloated = new List<string>();
        for (int pi = 1; pi < bm.PaletteCount; pi++)
        {
            if (GameObject.Find("shopdesc_" + pi) != null)
            { TapNamed("shophead_" + pi); yield return null; }
            if (!TapNamed("shophead_" + pi)) continue;
            yield return null; yield return null;
            var dgo = GameObject.Find("shopdesc_" + pi);
            if (dgo == null) continue;
            var dtx = dgo.GetComponentInChildren<Text>();
            var drc = (RectTransform)dgo.transform;
            if (dtx == null) continue;
            float need = Mathf.Max(34f, dtx.preferredHeight + 8f);
            if (Mathf.Abs(drc.rect.height - need) > 2f)
                bloated.Add(bm.PartLabel(pi) + " row=" + drc.rect.height.ToString("F0") + " needs=" + need.ToString("F0"));
            TapNamed("shophead_" + pi);
            yield return null;
        }
        Check(bloated.Count == 0, "every SHOP description row is sized for its own text on the FIRST open" + (bloated.Count == 0 ? "" : " - " + string.Join("; ", bloated.ToArray())));
        // The sweep above closed every part behind it. The checks that
        // follow read the beam's MATERIAL rows, which only exist while
        // that part is open - so put the accordion back the way this
        // block found it before handing over.
        if (GameObject.Find("shopdesc_" + bm.PaletteIndexOf("beam")) == null)
        {
            TapNamed("shophead_" + bm.PaletteIndexOf("beam"));
            yield return null; yield return null;
        }

        var beamRow = GameObject.Find("shopmat_" + bm.PaletteIndexOf("beam") + "_Aluminum");
        string rowTxt = beamRow != null ? beamRow.GetComponentInChildren<Text>().text : "";
        Check(rowTxt.Contains("HP") && rowTxt.Contains("/kg"),
              "the SHOP row prices strength per kilogram (" + rowTxt + ")");
        Check(rowTxt.Contains("own "), "and still says how many you own");

        // HP/kg must actually SEPARATE the materials, or the column is a
        // decoration. Carbon fibre against tungsten is the widest real gap.
        int bIdx = bm.PaletteIndexOf("beam");
        float cfPerKg = bm.PartHP(bIdx, "CarbonFiber")
                      / Mathf.Max(1f, CareerDB.Def("beam").MassOf("CarbonFiber"));
        float wPerKg  = bm.PartHP(bIdx, "Tungsten")
                      / Mathf.Max(1f, CareerDB.Def("beam").MassOf("Tungsten"));
        Check(cfPerKg > wPerKg * 3f,
              string.Format("HP/kg separates materials: CarbonFiber {0:F1} vs Tungsten {1:F1}",
                            cfPerKg, wPerKg));

        // retire flow: found a second robot, arm + confirm retires it
        Tap("ROBOTS"); yield return null;
        if (nameGO != null) nameGO.GetComponent<InputField>().text = "TIN CAN";
        TapNamed("stnew"); yield return null;
        TapNamed("stret_1"); yield return null;
        bool armedKept = Career.Data.stable.Count == 2;
        TapNamed("stret_1"); yield return null;
        Check(armedKept && Career.Data.stable.Count == 1 && Career.Data.stable[0].name == "VICE GRIP",
              "retire arms on the first tap and retires on the second");
        Check(Career.TxnSum() == Career.Data.scrap, "c4 ledger audits after the whole run");

        Check(BuilderManager.bannerNow == "",
              "C6.5: no banner in plain career play (" + BuilderManager.bannerNow + ")");

        // ==== C7: SAVE names its own build (owen 2026-08-02) ====
        // "The SAVE button on the build tab is disabled by default, and
        // requires the user to create a new robot or draft from the ROBOT tab
        // first." The first build a player ever makes was the one SAVE refused.
        Career.Data.activeRobot = -1;
        Career.Data.activeBlueprint = -1;
        foreach (var it in bm.CareerShortfallItems()) Career.AddItem(it.partId, it.mat, it.count);
        Tap("BUILD"); yield return null;
        Check(bm.NothingOpen, "C7 setup: nothing is open, so SAVE has nothing to commit to");
        int stable7 = Career.Data.stable.Count;

        TapNamed("bsave"); yield return null;
        var dlg = GameObject.Find("savedlg");
        Check(dlg != null && dlg.activeSelf, "SAVE with nothing open opens the naming window");
        Check(bm.SaveAsNewNote().Contains("Founds a robot"),
              "the window says it will found a robot when every part is owned");

        // empty name is refused IN the window - it must not close and lose it
        TapNamed("savedlg_ok"); yield return null;
        var errT = GameObject.Find("savedlg_err");
        Check(GameObject.Find("savedlg") != null && Career.Data.stable.Count == stable7
              && errT != null && errT.GetComponent<Text>().text.Length > 0,
              "empty name: refused in place, window stays open, nothing created");

        // CANCEL commits nothing
        TapNamed("savedlg_cancel"); yield return null;
        Check(GameObject.Find("savedlg_ok") == null && Career.Data.stable.Count == stable7,
              "CANCEL closes the window and creates nothing");

        // named + affordable -> a ROBOT
        TapNamed("bsave"); yield return null;
        var dlgName = GameObject.Find("savedlg_name");
        if (dlgName != null) dlgName.GetComponent<InputField>().text = "BOLT CUTTER";
        yield return null;
        TapNamed("savedlg_ok"); yield return null;
        Check(GameObject.Find("savedlg_ok") == null
              && Career.Data.stable.Count == stable7 + 1
              && Career.Data.stable[Career.Data.stable.Count - 1].name == "BOLT CUTTER"
              && !Career.Drafting,
              "named save founds a ROBOT when the build is fully owned");

        // same button, unaffordable build -> a DRAFT, not a robot that would
        // only fail later at the LEAGUE tab
        Career.Data.activeRobot = -1;
        Career.Data.activeBlueprint = -1;
        Career.Data.inventory.Clear();
        yield return null;
        Check(bm.SaveWouldDraft && bm.SaveAsNewNote().Contains("Keeps a draft"),
              "the window switches to draft when parts are missing");
        int bp7 = Career.Data.blueprints.Count;
        int st7b = Career.Data.stable.Count;
        TapNamed("bsave"); yield return null;
        var dlgName2 = GameObject.Find("savedlg_name");
        if (dlgName2 != null) dlgName2.GetComponent<InputField>().text = "WISH LIST";
        yield return null;
        TapNamed("savedlg_ok"); yield return null;
        Check(Career.Data.blueprints.Count == bp7 + 1
              && Career.Data.stable.Count == st7b
              && Career.Data.blueprints[Career.Data.blueprints.Count - 1].name == "WISH LIST",
              "named save keeps a DRAFT when parts are missing");

        // and with something open again, SAVE commits straight through with no
        // window - the dialog is for the empty case only
        Career.Data.activeBlueprint = -1;
        Career.Data.activeRobot = 0;
        yield return null;
        TapNamed("bsave"); yield return null;
        Check(GameObject.Find("savedlg_ok") == null,
              "SAVE with a robot open commits silently, no window");

        // ==== C7b: SAVE asks before it overwrites (owen 2026-08-03) ====
        // "pop up a confirmation window when clicking save to confirm
        // overwrite vs save a new robot." The failure that matters is the one
        // where SAVE AS NEW silently overwrites anyway - you only notice after
        // the robot you wanted is gone.
        Career.Data.activeBlueprint = -1;
        Career.Data.activeRobot = 0;
        foreach (var it in bm.CareerShortfallItems()) Career.AddItem(it.partId, it.mat, it.count);
        yield return null;
        int stableB4 = Career.Data.stable.Count;
        string keepName = Career.Data.stable[0].name;
        string keepSnap = Career.Data.stable[0].snapshot;
        Tap("BUILD"); yield return null;

        bool askedFirst = TapNamed("bsave"); yield return null;
        Check(askedFirst && GameObject.Find("savedlg_over") != null
              && GameObject.Find("savedlg_new") != null,
              "SAVE asks overwrite-or-new when a robot is open");
        // The naming field has no business on the confirm face.
        Check(GameObject.Find("savedlg_name") == null,
              "the confirm face hides the name box \u2014 one question at a time");

        // route: SAVE AS NEW -> the naming face -> a NEW robot
        TapNamed("savedlg_new"); yield return null;
        Check(GameObject.Find("savedlg_name") != null && GameObject.Find("savedlg_over") == null,
              "SAVE AS NEW switches the window to naming");
        var sawName = GameObject.Find("savedlg_name");
        if (sawName != null) sawName.GetComponent<InputField>().text = "FORK ONE";
        yield return null;
        TapNamed("savedlg_ok"); yield return null;
        Check(Career.Data.stable.Count == stableB4 + 1
              && Career.Data.stable[Career.Data.stable.Count - 1].name == "FORK ONE",
              "SAVE AS NEW founds a NEW robot rather than overwriting");
        Check(Career.Data.stable[0].name == keepName && Career.Data.stable[0].snapshot == keepSnap,
              "SAVE AS NEW leaves the original robot untouched");
        Check(Career.Data.activeRobot == Career.Data.stable.Count - 1,
              "the fork becomes the open robot, so the next SAVE lands on it");

        // route: OVERWRITE -> commits, creates nothing
        int beforeOver = Career.Data.stable.Count;
        TapNamed("bsave"); yield return null;
        bool overTapped = TapNamed("savedlg_over"); yield return null;
        Check(overTapped && Career.Data.stable.Count == beforeOver
              && GameObject.Find("savedlg_over") == null,
              "OVERWRITE commits in place and closes, creating nothing");

        // nothing open -> no question worth asking, straight to naming
        Career.Data.activeRobot = -1;
        Career.Data.activeBlueprint = -1;
        yield return null;
        TapNamed("bsave"); yield return null;
        Check(GameObject.Find("savedlg_name") != null && GameObject.Find("savedlg_over") == null,
              "with nothing open SAVE skips the question and just asks for a name");
        TapNamed("savedlg_cancel"); yield return null;

        // ==== C8: a disabled button explains itself (owen 2026-08-03) ====
        // "whenever a button is disabled, it should show hint to user on why it
        // is disabled when hovering or being clicked."
        //
        // Button.interactable = false swallows the pointer outright - no click,
        // no hover, no EventTrigger - so a button in that state can never do
        // either thing owen asked for. That makes "none of the gated buttons
        // are interactable = false" the load-bearing assertion here: it is the
        // one property that would silently take the hint away again.
        Career.Data.inventory.Clear();       // own nothing, so SELL is the dead one
        Tap("SHOP"); yield return null;
        // The accordion header TOGGLES. C4 already left the beam section open,
        // so tapping it "to open it" closed it instead and the two taps below
        // landed on nothing - the identical failure the c4 shop assertion had.
        // Ask whether the row is reachable rather than toggling blind.
        if (GameObject.Find("sell_beam_Aluminum") == null)
        {
            int beamSec8 = -1;
            for (int i = 1; i < bm.PaletteCount; i++) if (bm.PartId(i) == "beam") beamSec8 = i;
            TapNamed("shophead_" + beamSec8); yield return null;
        }
        yield return null;

        var sellB = ByName("sell_beam_Aluminum");
        var prevB = ByName("tipprev");
        var nextB = ByName("tipnext");
        Check(sellB != null && sellB.interactable
              && prevB != null && prevB.interactable
              && nextB != null && nextB.interactable,
              "gated buttons stay clickable so they can explain themselves");

        // An unavailable action used to blank its label and go fully
        // transparent - not a disabled button, an unexplained gap.
        Check(sellB != null && sellB.GetComponentInChildren<Text>().text == "SELL",
              "an unavailable SELL keeps its label instead of vanishing");

        // Assert the TAP as well as its effect, every time. A silent false is
        // how the whole shop section stayed dead for weeks.
        bool sellTapped8 = TapNamed("sell_beam_Aluminum"); yield return null;
        var shdr8 = GameObject.Find("shopheader");
        Check(sellTapped8 && shdr8 != null && shdr8.GetComponent<Text>().text.Contains("\u26a0"),
              "tapping the dead SELL answers with the amber reason");

        // ==== C10: the tutorial (owen 2026-08-03) ====
        // The old 3-tip list had rotted into misinformation - it opened with
        // "open the ROBOTS tab" and told you to "SAVE on ROBOTS", describing a
        // flow deleted earlier the same day. So the load-bearing assertion is
        // not that tips EXIST, it is that no tip names a control that is gone.
        for (int i = 0; i < BuilderManager.TIP_COUNT; i++)
        {
            string t = bm.CareerTip(i, "build");
            if (t.Contains("SAVE on ROBOTS") || t.Contains("NEW ROBOT to found")) tipStale = true;
            if (!t.StartsWith("TIP " + (i + 1) + "/" + BuilderManager.TIP_COUNT)) tipMisnumbered = true;
            if (t.Length < 30) tipEmpty = true;
        }
        Check(!tipStale, "no tip points at a control that has been moved or removed");
        Check(!tipMisnumbered && !tipEmpty,
              "all " + BuilderManager.TIP_COUNT + " tips are numbered and non-empty");

        // The four systems owen asked for are actually covered, by name.
        string allTips = "";
        for (int i = 0; i < BuilderManager.TIP_COUNT; i++) allTips += bm.CareerTip(i, "") + "\n";
        Check(allTips.Contains("wheel") && allTips.Contains("battery"), "tips teach what makes a machine legal");
        Check(allTips.Contains("Tungsten"), "tips teach the material trade-off");
        Check(allTips.Contains("SHOP") && allTips.Contains("SELL"), "tips teach where scrap goes");
        Check(allTips.Contains("core is the KO target"), "tips teach how you lose");

        // Progress-driven, so it never sits telling you to do what you did.
        // On a SCRATCH career, not this one: zeroing Data.fights here to fake a
        // new player is exactly what broke the telemetry assertions below on
        // the first run of this block - a test that quietly edits state later
        // tests depend on is its own kind of bug.
        var c10Saved = Career.Data;
        Career.Data = new CareerData();
        yield return null;
        int stepBuilt = bm.CareerTipStep();
        Career.Data.tipsOff = true;
        yield return null;
        bool silenced = bm.CareerTipStep() == BuilderManager.TIP_COUNT;
        Career.Data = c10Saved;
        yield return null;
        Check(stepBuilt >= 0 && stepBuilt <= 3,
              "a fresh career sits in the first four tips (" + stepBuilt + ")");
        Check(silenced, "SKIP TIPS silences the row without faking progress");

        // ==== C11: devFreeBuild is not a draft (owen 2026-08-03) ====
        // Career.Drafting used to return true whenever devFreeBuild was set,
        // so CareerBench - which sets it to test fights rather than inventory -
        // tripped the fight gate's draft refusal and could not start a single
        // match. It reported 0/3 for every pairing and read like a balance
        // result for a full day. The two ideas are now separate and this is
        // the guard: freeing PARTS must never imply a design is OPEN.
        bool savedFree = Career.devFreeBuild;
        int savedBp = Career.Data.activeBlueprint;
        Career.Data.activeBlueprint = -1;
        Career.devFreeBuild = true;
        bool freeUnderHarness = Career.FreeParts;
        bool draftUnderHarness = Career.Drafting;
        Career.devFreeBuild = savedFree;
        Career.Data.activeBlueprint = savedBp;
        yield return null;
        Check(freeUnderHarness && !draftUnderHarness,
              "devFreeBuild frees parts without pretending a design is open");

        // ==== C12: three fight tracks, all present (owen 2026-08-04) ====
        // The pick is random, so a missing file does not fail the fight - it
        // just quietly shrinks the pool and that song stops turning up. The
        // only way to catch it is to assert every name still resolves.
        int themesFound = 0;
        string missingTheme = "";
        foreach (var nm in FightManager.FIGHT_THEMES)
        {
            if (Resources.Load<AudioClip>(nm) != null) themesFound++;
            else missingTheme += nm + " ";
        }
        Check(themesFound == FightManager.FIGHT_THEMES.Length,
              "all " + FightManager.FIGHT_THEMES.Length + " fight tracks load from Resources"
              + (missingTheme.Length > 0 ? " (missing: " + missingTheme.Trim() + ")" : ""));
        // A fight ran back in C3/C4, so the picker has been exercised for real.
        Check(FightManager.lastFightTheme.Length > 0,
              "a fight drew a track (" + FightManager.lastFightTheme + ")");
        bool drewKnown = false;
        foreach (var nm in FightManager.FIGHT_THEMES)
            if (nm == FightManager.lastFightTheme) drewKnown = true;
        Check(drewKnown, "the track it drew is one of the three, not a stale name");

        // ==== C15: the dock gets out of the way (owen 2026-08-04) ====
        // The handle floats ABOVE the dock, so the dock's own height - which is
        // what OverUI measures - does not cover it. The first version of this
        // control would therefore have placed a part on the robot every time
        // you tapped SHOW PANEL. That is the same fall-through the R1 note in
        // OverUI warns about, reintroduced by a control living outside the band
        // the warning is about, which is exactly how that class of bug comes
        // back.
        // Pin the BUILD tab first. By the time this section runs the suite has
        // navigated to SHOP and PARTS, so "the panel came back" was asserted
        // against a panel that was never showing - the test failed a working
        // collapse because it was looking at the wrong tab.
        ui.TestShowTab(0);
        yield return null;
        int openH = Mathf.RoundToInt(ui.DockHeightForTest);
        ui.SetDockOpen(false);
        yield return null; yield return null;
        int shutH = Mathf.RoundToInt(ui.DockHeightForTest);
        bool panelGone = GameObject.Find("partscroll") == null;
        bool tabsStay = GameObject.Find("tab0") != null;
        Check(shutH < openH / 2, "collapsing the dock at least halves it ("
              + openH + " -> " + shutH + " units)");
        Check(panelGone, "collapsed: the content panel is gone");
        Check(tabsStay, "collapsed: the tab strip stays, so you can still navigate");

        // The camera must be told, or it reframes the robot for a dock that is
        // no longer there and centres it into empty space.
        float coverShut = MobileBuilderUI.coverBottom;
        ui.SetDockOpen(true);
        yield return null; yield return null;
        float coverOpen = MobileBuilderUI.coverBottom;
        Check(coverOpen > coverShut + 0.05f,
              "the camera is told the dock moved (cover " + coverShut.ToString("F2")
              + " -> " + coverOpen.ToString("F2") + ")");
        Check(GameObject.Find("partscroll") != null, "re-opening restores the panel");

        // ---- C16: the build controls are finger-sized (owen 2026-08-04) ----
        // Measured before this change, on owen's landscape iPhone: materials
        // 25.4 pt, action row 26.7, tabs 28.0, palette tiles 31.8 - every one
        // of them under Apple's 44 pt floor, while the dock covered 41% of the
        // screen. Row heights were literals in canvas units, which are pixels
        // over a scale factor that tracks resolution rather than physical size,
        // so a high-dpi phone shrank every control relative to the finger
        // holding it. 40 rather than 44 here is rounding tolerance, not a
        // negotiated-down target.
        ui.SetDockOpen(true);
        ui.TestShowTab(0);
        yield return null; yield return null;
        string[] tapNames = { "tab0", "matbtn", "rot", "bsave", "part_0", "dockhandle" };
        string tooSmall = "";
        foreach (var nm in tapNames)
        {
            float pt = ui.TapTargetPt(nm);
            if (pt >= 0f && pt < 40f) tooSmall += nm + "=" + pt.ToString("F1") + "pt ";
        }
        // ⚠ THE MESSAGE NAMES THE THRESHOLD IT ACTUALLY APPLIES. It used to say
        // "clear the 44 pt touch floor" while testing < 40f, and the sentence
        // explaining that 40 is rounding tolerance rather than a negotiated-down
        // target lives in the comment above — which a bench reader never sees.
        // A reader of the OUTPUT would have believed six controls had cleared
        // 44 pt when what was proven was 40. House rule 3 is satisfied in the
        // file; this makes the printed line satisfy it too.
        //
        // ⚠ AND THE COVERAGE IS NARROWER THAN THIS LINE SOUNDS: tapNames is six
        // BUILD-tab controls. TEST DRIVE, the tip strip and five whole tabs are
        // outside it, so "build controls" is doing load-bearing work in that
        // sentence — see #25. Nothing here measures whether one control is drawn
        // ON TOP OF another either, which is how TEST DRIVE's bottom 55% was
        // dead while its rect measured 69.9 pt.
        Check(tooSmall.Length == 0,
              "the six checked build controls clear 40 pt (44 pt floor, 40 = rounding tolerance)"
              + (tooSmall.Length > 0 ? " (too small: " + tooSmall.Trim() + ")" : ""));

        // The whole palette must fit its viewport. The original R1 critic
        // finding was that nine of 19 parts fitted and the tenth - Wheel - was
        // the first one hidden, so every wheel and every weapon lived
        // off-screen with nothing saying so. The fix was a hand-fitted cell
        // width, which then overflowed by 85 units on an iPad, whose canvas is
        // NARROWER in units than the phone's. Derived now; asserted here so the
        // next hand-fitted number fails in the suite rather than on a tablet.
        var pcont = GameObject.Find("content");
        var pview = GameObject.Find("viewport");
        float overflow = 0f;
        if (pcont != null && pview != null)
        {
            var a1 = new Vector3[4]; pcont.GetComponent<RectTransform>().GetWorldCorners(a1);
            var a2 = new Vector3[4]; pview.GetComponent<RectTransform>().GetWorldCorners(a2);
            overflow = (a1[2].x - a1[0].x) - (a2[2].x - a2[0].x);
        }
        Check(pcont != null && overflow <= 4f,
              "the whole palette fits its viewport (overflow " + overflow.ToString("F0") + " px)");

        // ---- C17: the material chooser (owen 2026-08-04) ----
        // The chips gave up their permanent row and became a sheet over the
        // palette. Three things have to hold: the sheet is reachable, the chips
        // in it are still finger-sized, and picking one CLOSES it - leaving it
        // open would cover the palette at the moment you go to choose the part
        // the material applies to.
        ui.SetMatSheet(true);
        yield return null; yield return null;
        bool sheetOpens = GameObject.Find("mat_Aluminum") != null;
        float chipPt = ui.TapTargetPt("mat_Aluminum");
        // The sheet must be ON TOP of the palette, not merely present. It
        // shipped underneath: uGUI paints siblings in order and the sheet is
        // built before the palette scroll, so every part tile painted over it
        // and the chips showed through the gaps. The old assertion - that the
        // chips exist - stayed green throughout.
        Check(ui.MatSheetDrawsOnTop, "the open material sheet paints over the palette");
        Check(sheetOpens && chipPt >= 40f,
              "the material sheet opens with finger-sized chips (" + chipPt.ToString("F1") + "pt)");
        TapNamed("mat_Steel");
        yield return null; yield return null;
        Check(!ui.MatSheetOpen, "picking a material closes the sheet");

        // The chooser must LOOK like one, and must still name the material
        // while it is open - the first version blanked the name to "CLOSE",
        // which put the only readout of the selection off screen exactly while
        // you were changing it. Caret up when closed, down when open, name
        // present in both.
        string shutLbl = ui.MatButtonLabel;
        ui.SetMatSheet(true);
        yield return null;
        string openLbl = ui.MatButtonLabel;
        ui.SetMatSheet(false);
        yield return null;
        Check(shutLbl.Contains("\u25b4") && openLbl.Contains("\u25be"),
              "the material button shows a disclosure caret that flips (" + shutLbl
              + " / " + openLbl + ")");
        Check(shutLbl.ToUpper().Contains("STEEL") && openLbl.ToUpper().Contains("STEEL"),
              "the material stays named whether the sheet is open or shut");

        Check(MatDB.Canon(bm.ActiveMatKey) == MatDB.Canon("Steel"),
              "picking a material actually selects it (" + bm.ActiveMatKey + ")");
        bm.ActiveMatKey = "Aluminum";
        ui.SetMatSheet(false);
        yield return null;

        // ---- C18: nothing you must read or hit is under the notch ----
        // Measured on owen's landscape iPhone: screen 2532x1170, safe area
        // x=141 w=2250 y=63 - 141 px bitten out of each side and 63 off the
        // bottom. The UI drew edge to edge, so BUILD and TROPHIES ran under the
        // notch and the whole action row sat in the home-indicator strip, which
        // on iOS also swallows the swipe that would have hit them. Asserted on
        // the real corners: a device with no notch passes trivially, which is
        // exactly why this has to be checked against the DEVICE's safe area
        // rather than the editor window's.
        var sa = UnityEngine.Device.Screen.safeArea;
        float dw = UnityEngine.Device.Screen.width, dh2 = UnityEngine.Device.Screen.height;
        string outside = "";
        if (sa.width > 1f && sa.height > 1f && dw > 1f)
        {
            string[] mustClear = { "tab0", "tab5", "rot", "bsave", "matbtn", "dockhandle" };
            foreach (var nm in mustClear)
            {
                var go = GameObject.Find(nm);
                if (go == null) continue;
                var rt = go.GetComponent<RectTransform>();
                var cc = new Vector3[4];
                rt.GetWorldCorners(cc);
                // world corners are device pixels for an overlay canvas
                if (cc[0].x < sa.x - 1f || cc[2].x > sa.x + sa.width + 1f
                    || cc[0].y < sa.y - 1f)
                    outside += nm + " ";
            }
        }
        Check(outside.Length == 0,
              "every control clears the notch and the home indicator"
              + (outside.Length > 0 ? " (outside: " + outside.Trim() + ")" : ""));

        // ---- C19: legibility sweep (owen 2026-08-04) ----
        // owen: "why didn't you capture the aluminum UI bug in your previous
        // test iterations?" Because every check written before this one
        // measured RECTANGLES - point sizes of tap targets, dock percentages,
        // safe-area corners - and none of them looked at what a person actually
        // does with a screen, which is read it. The boxes were finger-sized
        // while the type inside them was 8.3 pt, and nothing failed.
        //
        // This sweeps every VISIBLE label and asks three things a rectangle
        // check cannot: is it big enough to read, does it fit its box, and does
        // it fit without being cut off. It runs over whatever happens to be on
        // screen, so it covers controls nobody thought to name.
        // ⚠ THIS SWEEP ONLY EVER SAW THE BUILD TAB, AND SAID SO NOWHERE — fixed
        // 2026-08-10. It was `TestShowTab(0)` and then a scan of every ACTIVE
        // label; every other tab's panel is inactive, so `activeInHierarchy`
        // silently filtered five of the six tabs out. The check passed for
        // months while measuring one screen.
        //
        // That is the same disease as the rest of this file's history: a check
        // positioned where it cannot see the thing it checks. It is also a
        // direct breach of house rule 1 — measure over EVERYTHING rather than
        // over a named list — which is exactly what `ApplyTouchSizes` does
        // wrong, and this sweep is supposed to be the thing that catches it.
        //
        // Now it walks all six tabs. Findings are PREFIXED WITH THE TAB, because
        // "lbl(8.3pt)" tells you a label is too small and nothing about where to
        // go and look at it.
        //
        // Every dock panel is gated on dockOpen, so SetDockOpen(true) is
        // re-asserted inside the loop rather than once before it: a tab switch
        // against a collapsed dock scans an empty screen and reports a clean
        // pass, which is the failure this whole comment is about.
        string[] tabName = { "BUILD", "LEAGUE", "ROBOTS", "SHOP", "ARENA", "PROGRAM" };

        float sfL = ui.CanvasScaleForTest;
        float pxPerPtL = UnityEngine.Device.Screen.dpi / 163f;
        string tooSmallTxt = "", overflowTxt = "", cutTxt = "";
        int scanned = 0;
        string perTab = "";

        for (int tabIx = 0; tabIx < tabName.Length; tabIx++)
        {
        ui.SetDockOpen(true);
        ui.TestShowTab(tabIx);
        yield return null; yield return null;

        int scannedHere = 0;
        if (sfL > 0.01f && pxPerPtL > 0.01f)
        {
            foreach (var t in Object.FindObjectsByType<UnityEngine.UI.Text>(FindObjectsSortMode.None))
            {
                if (!t.gameObject.activeInHierarchy || string.IsNullOrEmpty(t.text)) continue;
                scanned++; scannedHere++;
                string who = tabName[tabIx] + ":"
                           + (t.transform.parent != null ? t.transform.parent.name : t.name);
                float pt = t.fontSize * sfL / pxPerPtL;
                if (pt < 10.5f) tooSmallTxt += who + "(" + pt.ToString("F1") + "pt) ";
                bool wraps = t.horizontalOverflow == HorizontalWrapMode.Wrap;
                if (wraps && t.preferredHeight > t.rectTransform.rect.height + 1f)
                    // NAME THE CONTENT, not just the object. "part_8 part_9"
                    // tells you three boxes are wrong and nothing about why;
                    // half an hour went into guessing which of two changes did
                    // it when the text itself would have said so immediately.
                    // bestFit matters because Unity reports preferredHeight for
                    // the UNSHRUNK font, so a best-fit label can be flagged here
                    // and still render perfectly - that distinction is the first
                    // thing anyone reading this failure needs.
                    overflowTxt += string.Format("{0}[\"{1}\" {2:F0}>{3:F0}px{4}] ",
                        who, t.text.Replace("\n", " / "), t.preferredHeight,
                        t.rectTransform.rect.height,
                        t.resizeTextForBestFit ? " bestFit" : "") ;
                if (!wraps && t.preferredWidth > t.rectTransform.rect.width + 1f)
                    cutTxt += who + " ";
            }
        }
        perTab += tabName[tabIx] + "=" + scannedHere + " ";
        }   // end tab loop

        // Per-tab counts, not just the total: a tab that contributed ZERO
        // labels was not measured, and a total alone cannot tell you which.
        log.Add("      legibility sweep coverage: " + perTab.Trim());
        Check(scanned > 10, "the legibility sweep actually saw the UI (" + scanned + " labels)");
        Check(tooSmallTxt.Length == 0,
              "every label is at least 10.5 pt"
              + (tooSmallTxt.Length > 0 ? " (too small: " + tooSmallTxt.Trim() + ")" : ""));
        Check(overflowTxt.Length == 0,
              "no wrapped label is taller than its box"
              + (overflowTxt.Length > 0 ? " (" + overflowTxt.Trim() + ")" : ""));
        Check(cutTxt.Length == 0,
              "no label is cut off by its box"
              + (cutTxt.Length > 0 ? " (" + cutTxt.Trim() + ")" : ""));

        // Affordance, as far as it can be automated: a control that OPENS
        // something has to say so. This cannot check that the caret is the
        // right idea - owen had to tell me that - but it can check the
        // convention stays applied once chosen, which is the part that rots.
        string noHint = "";
        var mbLbl = ui.MatButtonLabel;
        if (!(mbLbl.Contains("\u25b4") || mbLbl.Contains("\u25be"))) noHint += "matbtn ";
        var dhGo = GameObject.Find("dockhandle");
        var dhTxt = dhGo != null ? dhGo.GetComponentInChildren<UnityEngine.UI.Text>() : null;
        string dhLbl = dhTxt != null ? dhTxt.text : "";
        if (!(dhLbl.Contains("\u25b2") || dhLbl.Contains("\u25bc"))) noHint += "dockhandle ";
        Check(noHint.Length == 0,
              "controls that open a panel carry a disclosure mark"
              + (noHint.Length > 0 ? " (bare: " + noHint.Trim() + ")" : ""));

        var handle = GameObject.Find("dockhandle");
        Check(handle != null && handle.activeInHierarchy, "the handle is always reachable");
        Check(ui.HandleIsUiForTest, "taps on the handle count as UI, not as taps on the robot");

        // ==== C13: the build playlist rotates (owen 2026-08-04) ====
        // "randomly shuffle the three songs in build mode and play them one by
        // one" - so unlike the fight picker there are two claims to hold: every
        // track must be REACHABLE, and a pass must actually cover all of them
        // rather than favouring one. Both fail silently in play: you just
        // notice, three sessions later, that one song never turns up.
        int bThemes = 0;
        string bMissing = "";
        foreach (var nm in BuilderManager.BUILD_THEMES)
        {
            if (Resources.Load<AudioClip>(nm) != null) bThemes++;
            else bMissing += nm + " ";
        }
        Check(bThemes == BuilderManager.BUILD_THEMES.Length,
              "all " + BuilderManager.BUILD_THEMES.Length + " build tracks load from Resources"
              + (bMissing.Length > 0 ? " (missing: " + bMissing.Trim() + ")" : ""));

        // The workshop has been open for this whole run, so the pump has picked
        // a track for real - no harness poke required for this one.
        bool bKnown = false;
        foreach (var nm in BuilderManager.BUILD_THEMES)
            if (nm == BuilderManager.buildTrackNow) bKnown = true;
        Check(bKnown, "the workshop is playing one of the build tracks ("
              + (BuilderManager.buildTrackNow.Length > 0 ? BuilderManager.buildTrackNow : "silent") + ")");

        // Two full passes, driven through the real shuffle - starting from a
        // pass BOUNDARY. The workshop has been playing for the whole run, so
        // the playlist is mid-pass here; six advances from an arbitrary offset
        // straddle the seam and the per-pass claim would be untestable against
        // them. Walk forward until a reshuffle lands us at position 0, then
        // measure. (First version of this test skipped that and failed the
        // implementation for a fault in itself.)
        int span = BuilderManager.BUILD_THEMES.Length;
        var seq = new List<string>();
        bool atBoundary = false;
        for (int i = 0; i < span + 1 && !atBoundary; i++)
        {
            string nm = bm.DebugNextBuildTrack();
            yield return null;
            if (bm.DebugBuildPos == 0) { seq.Add(nm); atBoundary = true; }
        }
        Check(atBoundary, "the playlist reaches a fresh pass within one pass of advances");
        while (seq.Count < span * 2) { seq.Add(bm.DebugNextBuildTrack()); yield return null; }

        bool noRepeat = true;
        for (int i = 1; i < seq.Count; i++) if (seq[i] == seq[i - 1]) noRepeat = false;
        Check(noRepeat, "the playlist never plays the same track twice in a row ["
              + string.Join(", ", seq.ToArray()) + "]");

        // Each window of `span` is one pass and must be a permutation - that is
        // the difference between a shuffled playlist and rolling a die each
        // time, which is what fight mode does and what this must NOT do.
        bool passesCover = true;
        for (int w = 0; w + span <= seq.Count; w += span)
            foreach (var nm in BuilderManager.BUILD_THEMES)
            {
                bool hit = false;
                for (int i = w; i < w + span; i++) if (seq[i] == nm) hit = true;
                if (!hit) passesCover = false;
            }
        Check(passesCover, "each pass plays every track once before repeating any");

        // ==== C9: one device rule (owen 2026-08-03) ====
        // The chooser used to ASK which UI you wanted and ShouldActivate used
        // to ignore the answer - on an iPad, "START CAREER - Desktop" handed
        // you the touch UI regardless. Now the boot mode and the UI attach read
        // the same function, and this is the guard: re-adding a
        // touchSupported branch to one of them fails here instead of on
        // somebody's Surface.
        bool savedForce = MobileBuilderUI.forceMobileUI;
        MobileBuilderUI.forceMobileUI = false;
        bool agree = MobileBuilderUI.ShouldActivate() == MobileBuilderUI.DeviceWantsTouch();
        MobileBuilderUI.forceMobileUI = true;
        bool overrides = MobileBuilderUI.ShouldActivate();
        MobileBuilderUI.forceMobileUI = savedForce;
        yield return null;
        Check(agree, "boot mode and UI attach read ONE device rule");
        Check(overrides, "forceMobileUI still overrides detection, so the editor can test touch");

        // ==== C14: the chooser is only skipped when it is safe to (owen 2026-08-04) ====
        // Under the Device Simulator the mouse device is off, so IMGUI - which
        // is the ENTIRE desktop builder - cannot be clicked at all. Skipping
        // the chooser is therefore only safe if the touch UI is what comes up;
        // skipping it into the desktop builder would strand you exactly the way
        // the chooser itself did, one screen later and harder to diagnose.
        bool savedForce2 = MobileBuilderUI.forceMobileUI;
        MobileBuilderUI.forceMobileUI = false;
        bool autoBoot = ModeSelect.ShouldAutoBoot();
        bool touchWanted = MobileBuilderUI.DeviceWantsTouch();
        MobileBuilderUI.forceMobileUI = savedForce2;
        yield return null;
        Check(!Application.isEditor || !autoBoot || touchWanted,
              "the editor only skips the chooser when the touch UI will be the UI");
        Check(autoBoot == (!Application.isEditor || touchWanted),
              "auto-boot and the device rule read the same answer");

        // ---- C20: a rotor may not sweep through its own machine (owen, 2026-08-05) ----
        // The arena cannot fix this and should not try. A rotor's hit volume is
        // a TRIGGER, which is what makes it cut instead of shove; a solid one
        // would launch both robots and take the damage model with it. So the
        // rule lives at PLACEMENT, and this drives it through PlaceByFace -
        // the same gate the pointer's ghost consults, not a private back door.
        string savedBuild = bm.SnapshotString();
        int iSpindle = bm.PaletteIndexOf("spindle");
        int iSpinner = bm.PaletteIndexOf("spinner");
        int iBattery = bm.PaletteIndexOf("battery");
        Check(iSpindle >= 0 && iSpinner >= 0 && iBattery >= 0,
              "C20: the fixture's parts are all in the palette");
        const string BARE = BuilderManager.SNAP_STAMP + "\ncore|0,0.7,0|0|0,0,0";

        // (a) THE BUG. A disc on a VERTICAL axle, bolted out on the core's
        //     flank, swings a full circle straight back through the core.
        bm.LoadSnapshot(BARE);
        yield return null;
        bool spOk = bm.PlaceByFace(iSpindle, 0, Vector3.right, 180);
        bool discRefused = !bm.PlaceByFace(iSpinner, 1, Vector3.right, 0);
        string discWhy = bm.LastMessage;
        int afterRefusal = bm.PlacedCount;
        yield return null;
        Check(spOk, "C20: the spindle itself still places");
        Check(discRefused, "C20: a rotor that would saw the core is REFUSED");
        Check(discWhy != null && discWhy.ToLower().Contains("sweep"),
              "C20: and the refusal says why (" + discWhy + ")");
        Check(afterRefusal == 2,
              "C20: the refused rotor was not placed anyway (" + afterRefusal + " parts)");

        // (b) CONTROL. The same two parts on the ROOF sweep a horizontal circle
        //     over the machine and are perfectly legal. Without this, (a) would
        //     pass just as happily if the rule refused every rotor ever built -
        //     which is the failure mode C19 was written to catch elsewhere.
        bm.LoadSnapshot(BARE);
        yield return null;
        bm.PlaceByFace(iSpindle, 0, Vector3.up, 0);
        bool roofOk = bm.PlaceByFace(iSpinner, 1, Vector3.up, 0);
        int afterRoof = bm.PlacedCount;
        yield return null;
        Check(roofOk && afterRoof == 3,
              "C20 control: a rotor that clears the machine still places ("
              + afterRoof + " parts)");

        // (c) NOT RETROACTIVE, in both halves of that promise. A build saved
        //     before the rule existed loads unharmed and still passes Validate;
        //     and because the refusal is BASELINED against the build as it
        //     stands, it also stays EDITABLE. A save that went permanently
        //     read-only would be the same retroactive punishment in a hat.
        const string SAWYER = BuilderManager.SNAP_STAMP
                            + "\ncore|0,0.7,0|0|0,0,0"
                            + "\nspindle|0.3,0.7,0|180|1,0,0"
                            + "\nspinner|0.5,0.7,0|0|1,0,0";
        int loaded = bm.LoadSnapshot(SAWYER);
        yield return null;
        Check(loaded == 3, "C20: an offending build still LOADS (" + loaded + " parts)");
        string vw = bm.Validate();
        Check(vw == null || !vw.ToLower().Contains("sweep"),
              "C20: FIGHT never refuses it - the rule is placement-time only");
        bool editable = bm.PlaceByFace(iBattery, 0, Vector3.left, 0);
        yield return null;
        Check(editable, "C20: an offending build is still editable away from the circle");

        // (d) ...and that baseline does not switch the rule off wholesale: a
        //     NEW part put INSIDE the existing circle is still refused.
        int loaded2 = bm.LoadSnapshot(SAWYER);
        yield return null;
        bool intoCircle = !bm.PlaceByFace(iBattery, 0, Vector3.forward, 0);   // was the bracket; removed 2026-08-12 — the battery is the same small-cube probe
        yield return null;
        Check(loaded2 == 3, "C20: fixture reloaded for the last case");
        Check(intoCircle, "C20: a NEW part inside an existing circle is still refused");

        bm.LoadSnapshot(savedBuild);
        yield return null;

        // ---- GUSSET stock gate (2026-08-12): career mode is where "No
        // Gusset left" exists (free build is unlimited — TouchSmoke owns that
        // half). The career kit grants none, so the refusal leg is real; one
        // granted = one applied = zero remaining. In-memory only, like C1.
        {
            string gSaved = bm.SnapshotString();
            int gIdx = bm.PaletteIndexOf("gusset");
            Check(gIdx >= 0, "GUSSET: tile present in career mode");
            var gTarget = bm.placed.Count > 1 ? bm.placed[1] : null;
            bm.selected = gIdx;
            bm.ApplyGusset(gTarget);
            Check(gTarget != null && !gTarget.reinforced
                  && bm.LastMessage != null && bm.LastMessage.Contains("No Gusset"),
                  "GUSSET: with no stock the career refuses in words");
            Career.AddItem("gusset", "Steel", 1);
            bm.ApplyGusset(gTarget);
            Check(gTarget != null && gTarget.reinforced, "GUSSET: one granted, one applied");
            Check(bm.CareerRemaining(gIdx) == 0, "GUSSET: …and the shelf reads zero");
            bm.ApplyGusset(bm.placed.Count > 2 ? bm.placed[2] : gTarget);
            Check(bm.LastMessage != null
                  && (bm.LastMessage.Contains("No Gusset") || bm.LastMessage.Contains("already")),
                  "GUSSET: the next application is refused (no stock / no stacking)");
            bm.LoadSnapshot(gSaved); yield return null;
        }

        // ---- C6.4: telemetry schema landed (§14) ----
        Check(Career.Data.fights > 0, "telemetry: fights counted (" + Career.Data.fights + ")");
        Check(Career.Data.scrapCurve.Count == Career.Data.fights,
              "telemetry: one scrap sample per settle (" + Career.Data.scrapCurve.Count + ")");
        Check(!string.IsNullOrEmpty(Career.Data.lastContest),
              "telemetry: last contest recorded (" + Career.Data.lastContest + ")");

        // ---- C21: two robots, one battery (owen, 2026-08-05) ----
        // "I saved two robots. I don't own the parts for both together, but I
        // do own any one of them ... as long as they own the current robot,
        // they can fight with it. otherwise user always needs to retire and
        // rebuild."
        //
        // Under R4's shared pool every OTHER saved robot held its parts, so
        // loading ALPHA found the one battery already claimed by BETA and the
        // fight was refused for a part sitting in the box. The fixture is the
        // smallest thing that shows it: two designs wanting the SAME battery.
        string savedBuild21 = bm.SnapshotString();
        int savedActive21 = Career.Data.activeRobot;
        var savedStable21 = new List<CareerRobot>(Career.Data.stable);
        var savedInv21 = new List<CareerItem>();
        foreach (var it in Career.Data.inventory)
            savedInv21.Add(new CareerItem { partId = it.partId, mat = it.mat, count = it.count });

        const string ALPHA21 = "core|0,0.7,0|0|0,0,0|Aluminum"
                             + "\nbattery|0,0.975,0|0|0,0,0|Aluminum";
        const string BETA21  = "core|0,0.7,0|0|0,0,0|Aluminum"
                             + "\nbattery|0,0.975,0|0|0,0,0|Aluminum"
                             + "\nbeam|0,0.7,0.45|0|0,0,0|Aluminum";
        Career.Data.inventory.Clear();
        Career.AddItem("battery", "Aluminum", 1);
        Career.AddItem("beam", "Aluminum", 1);
        Career.Data.stable.Clear();
        Career.Data.stable.Add(new CareerRobot { name = "ALPHA", snapshot = ALPHA21 });
        Career.Data.stable.Add(new CareerRobot { name = "BETA",  snapshot = BETA21 });
        Career.Data.activeRobot = -1;
        yield return null;

        Check(Career.RobotReady(0) && Career.RobotReady(1),
              "C21: two robots that cannot coexist each read READY on their own");

        bm.StableEdit(0);
        yield return null; yield return null;
        var lackA21 = bm.CareerShortfall();
        Check(lackA21.Count == 0, "C21: ALPHA loaded is fully owned ("
              + (lackA21.Count == 0 ? "clear" : string.Join(", ", lackA21.ToArray())) + ")");
        bm.StableEdit(1);
        yield return null; yield return null;
        var lackB21 = bm.CareerShortfall();
        Check(lackB21.Count == 0,
              "C21: BETA too - the other design makes no claim on the battery ("
              + (lackB21.Count == 0 ? "clear" : string.Join(", ", lackB21.ToArray())) + ")");

        // THE DISCRIMINATOR. Own TWO batteries, load ALPHA which uses one:
        // remaining must read 1. Under the shared pool BETA's battery was
        // subtracted here too and this read 0 - a part you own, in stock, that
        // the builder refused to let you place. Without this assertion the two
        // checks above would pass just as happily if nothing were counted.
        Career.AddItem("battery", "Aluminum", 1);
        bm.StableEdit(0);
        yield return null; yield return null;
        int rem21 = bm.CareerRemainingMat(bm.PaletteIndexOf("battery"), "Aluminum");
        Check(rem21 == 1, "C21: 2 owned minus 1 in the loaded build = 1 spare, not 0 (read "
              + rem21 + ")");

        // Sell every battery out from under both designs. Readiness has to flip
        // AND name the part: a badge that only ever says READY measures nothing.
        Career.Data.inventory.Clear();
        Career.AddItem("beam", "Aluminum", 1);
        yield return null;
        string txt21 = Career.ShortfallText(Career.SnapshotShortfall(ALPHA21));
        Check(!Career.RobotReady(0) && !Career.RobotReady(1),
              "C21: sell the batteries and both robots stop reading ready");
        Check(txt21.Contains("Battery"),
              "C21: and the badge names what to buy -> \"" + txt21 + "\"");

        Career.Data.inventory.Clear();
        foreach (var it in savedInv21) Career.AddItem(it.partId, it.mat, it.count);
        Career.Data.stable.Clear();
        foreach (var r21 in savedStable21) Career.Data.stable.Add(r21);
        Career.Data.activeRobot = savedActive21;
        bm.LoadSnapshot(savedBuild21);
        yield return null;

        // ---- restore: career off puts the sandbox back ----
        Career.active = savedActive;
        Career.Data = savedData;
        // Restore what was found, not a literal true — see HazardBench's note.
        // A bench that ENABLES autosave on its way out is a bench that arms
        // the next thing to write owen's save.
        autosaveHold.Dispose();
        yield return null; yield return null;
        var shopTab2 = Btn("SHOP");
        Check(savedActive || shopTab2 == null || !shopTab2.gameObject.activeInHierarchy,
              "career off: SHOP tab hides again");
        Finish();
    }

    /// <summary>The FAIL lines, as a string a harness caller can read.
    ///
    /// Unity's console buffer truncates from the HEAD, so a suite this long
    /// pushes its early failures out of reach exactly when they are the ones
    /// you need - I burned several attempts reading the tail before adding
    /// this. The result line alone tells you a number, not which number.</summary>
    public static string failLines = "";

    /// <summary>The counts, readable from outside. `failLines` already told a
    /// harness WHICH checks failed but never HOW MANY ran, and the result line
    /// goes to Debug.Log, which a bridge session cannot read back — so the one
    /// number this project insists on ("look at the pass COUNT, not just the
    /// fail count") was the one number this bench could not hand over. Every
    /// other bench here exposes these; this one did not, and it was found the
    /// obvious way, by needing it. `notes` carries the non-check lines, which
    /// is where the sweep's per-tab coverage lives.</summary>
    public static int lastPassed, lastFailed;
    public static string notes = "";

    string stashedToken;

    void Finish()
    {
        // Hand owen's in-memory session back exactly as found (see the stash
        // at the top of Start — the bench runs signed out on purpose).
        if (!string.IsNullOrEmpty(stashedToken)) LadderClient.Token = stashedToken;
        failLines = "";
        lastPassed = passed; lastFailed = failed;
        notes = "";
        foreach (var l in log) if (!l.StartsWith("FAIL") && !l.StartsWith("PASS")) notes += l.Trim() + "\n";
        foreach (var l in log) if (l.StartsWith("FAIL")) failLines += l + "\n";
        foreach (var l in log) Debug.Log("[CareerSmoke] " + l);
        Debug.Log(string.Format("[CareerSmoke] RESULT: {0} pass, {1} fail{2}",
                  passed, failed, failed == 0 ? " - ALL GREEN" : " - FIX NEEDED"));
        finished = true;
    }
}
}
#endif
