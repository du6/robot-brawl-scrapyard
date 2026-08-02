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
        var bm = Object.FindFirstObjectByType<BuilderManager>();
        var ui = MobileBuilderUI.inst;
        if (bm == null || ui == null)
        { Check(false, "builder + mobile UI present"); Finish(); yield break; }

        var savedData = Career.Data;
        bool savedActive = Career.active;
        Career.autosave = false;
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

        int beamP = CareerDB.PartPrice("beam", "Aluminum");
        int sellP = CareerDB.SellPrice("beam", "Aluminum");
        int s0 = Career.Data.scrap;
        TapNamed("shopbuy_1"); yield return null;
        exp -= beamP;
        Check(Career.Data.scrap == s0 - beamP && Career.CountOf("beam", "Aluminum") == 2,
              "buy: scrap -price, owned +1");
        var beamTile = Btn("Beam");
        Check(beamTile != null && beamTile.GetComponentInChildren<Text>().text.Contains("\u00d72"),
              "buy: part tile badge follows to \u00d72");

        // deliberately broke (drain THROUGH the ledger so the audit holds)
        int drain = Career.Data.scrap - 3;
        Career.Txn(-drain, "test drain"); exp -= drain;
        int s1 = Career.Data.scrap;
        TapNamed("shopbuy_1"); yield return null;
        Check(Career.Data.scrap == s1 && Career.CountOf("beam", "Aluminum") == 2
              && Career.shopMsg.Contains("Not enough"),
              "insufficient funds: refused with the amber message, nothing changes");

        TapNamed("shopsell_1"); yield return null;
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
        TapNamed("shopsell_1"); yield return null;
        Check(Career.CountOf("beam", "Aluminum") == owned,
              "in-use sell: first tap arms, does not sell");
        TapNamed("shopsell_1"); yield return null;
        exp += sellP;
        Check(Career.CountOf("beam", "Aluminum") == owned - 1,
              "in-use sell: second tap sells anyway");

        // ---- swap: bracket Steel -> Aluminium, cheapest source auto-picked ----
        Career.AddItem("bracket", "Steel", 1);
        Career.Txn(200, "test grant 2"); exp += 200;
        int bi = -1;
        for (int i = 1; i < bm.PaletteCount; i++)
            if (bm.PartId(i) == "bracket") bi = i;
        int swapC = Career.SwapCost("bracket", "Steel", "Aluminum");
        bool swapTapped = bi > 0 && TapNamed("shopswap_" + bi);
        yield return null;
        exp -= swapC;
        Check(swapTapped && Career.CountOf("bracket", "Steel") == 0
              && Career.CountOf("bracket", "Aluminum") == 1,
              "swap: Steel bracket becomes Aluminium, source auto-picked");

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
        var fakeCap = new CareerDB.League("LX", "Test League", "X", "x", 10f, new Vector3(9f, 9f, 9f), new CareerDB.Contest[0]);
        string capMsg = bm.CareerValidate(fakeCap);
        Check(capMsg != null && capMsg.Contains("over the Test League cap"),
              "over-cap build refused with the specific kg message");
        var fakeBox = new CareerDB.League("LY", "Box League", "Y", "y", 99999f, new Vector3(0.05f, 0.05f, 0.05f), new CareerDB.Contest[0]);
        string boxMsg = bm.CareerValidate(fakeBox);
        Check(boxMsg != null && boxMsg.Contains("Too big for the Box League size box"),
              "over-size build refused with the size-box message");

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

        // re-entry pays the 40% purse
        sBefore = Career.Data.scrap;
        bm.StartCareerFight(0, 0);
        yield return null; yield return null;
        fm = Object.FindFirstObjectByType<FightManager>();
        float d1 = fm != null ? fm.player.dealt : 0f;
        if (fm != null) fm.End(FightManager.Outcome.PlayerWin, "harness re-entry win");
        yield return null; yield return null;
        int expPay2 = CareerDB.WinPay(L1C1, d1, bv, ov2, true, false);
        Check(Career.Data.scrap == sBefore + expPay2 && expPay2 < expPay,
              "re-entry win pays the 40% purse, no first-win bonus");
        bm.BackToBuild(); yield return null; yield return null; yield return null;

        // loss pays the consolation formula
        sBefore = Career.Data.scrap;
        bm.StartCareerFight(0, 1);
        yield return null; yield return null;
        fm = Object.FindFirstObjectByType<FightManager>();
        float d2 = fm != null ? fm.player.dealt : 0f;
        if (fm != null) fm.End(FightManager.Outcome.PlayerLoss, "harness loss");
        yield return null; yield return null;
        Check(Career.Data.scrap == sBefore + CareerDB.LossPay(d2) && !Career.Data.doneContests.Contains("L1C2"),
              "loss pays consolation; the contest stays unbeaten");
        bm.BackToBuild(); yield return null; yield return null; yield return null;

        // entry fee: refused broke, debited when funded
        foreach (var lgx in new[] { CareerDB.Leagues[0], CareerDB.Leagues[1] })
            foreach (var cx in lgx.contests)
                if (!Career.Data.doneContests.Contains(cx.id)) Career.Data.doneContests.Add(cx.id);
        Career.Txn(-(Career.Data.scrap - 10), "c3 drain");
        bm.StartCareerFight(2, 0); yield return null;
        Check(bm.mode == BuilderManager.Mode.Build && bm.LastMessage != null && bm.LastMessage.Contains("Entry fee"),
              "entry fee refused when broke, fight never starts");
        Career.Txn(100, "c3 fee grant");
        bm.StartCareerFight(2, 0);
        yield return null; yield return null;
        fm = Object.FindFirstObjectByType<FightManager>();
        Check(fm != null && Career.Data.scrap == 60,
              "entry fee 50 debited on enrollment (110 - 50 = 60)");
        if (fm != null) fm.End(FightManager.Outcome.PlayerLoss, "harness fee loss");
        yield return null; yield return null;
        bm.BackToBuild(); yield return null; yield return null; yield return null;
        Check(Career.TxnSum() == Career.Data.scrap, "c3 ledger audits after fees + settlements");

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
        Check(rendered == 6, "scouting renders all six roster bots (" + rendered + "/6)");
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
        Check(Btn("LEAGUE") != null && Btn("ROBOTS") != null && Btn("PARTS") != null,
              "workshop tabs: LEAGUE / ROBOTS / PARTS appear in career mode");
        Check(Career.Data.tutorialStep == 0, "fresh career starts at onboarding step 0");

        Tap("ROBOTS"); yield return null;
        var nameGO = GameObject.Find("namein");
        if (nameGO != null) nameGO.GetComponent<InputField>().text = "VICE GRIP";
        TapNamed("stnew"); yield return null;
        Check(Career.Data.stable.Count == 1 && Career.Data.stable[0].name == "VICE GRIP"
              && Career.Data.activeRobot == 0 && Career.Data.tutorialStep == 1,
              "NEW ROBOT founds VICE GRIP, makes it active, onboarding -> step 1");
        TapNamed("stsave"); yield return null;
        Check(Career.Data.stable[0].snapshot.Length > 0 && Career.Data.tutorialStep == 2,
              "SAVE stores the fight-legal build, onboarding -> step 2");

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
        TapNamed("shopbuy_1"); yield return null;
        Check(Career.CountOf("beam", "Aluminum") == beams4 + 1, "shop purchase lands in the shelf");

        // drafting table: place a Tungsten beam we do not own, blueprint it,
        // then one-tap convert buys exactly the missing part
        Tap("ROBOTS"); yield return null;
        TapNamed("stdraft"); yield return null;
        Check(Career.devFreeBuild, "drafting table unlocks everything");
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
        Tap("ROBOTS"); yield return null;
        if (nameGO != null) nameGO.GetComponent<InputField>().text = "DREAM MACHINE";
        TapNamed("bpsave"); yield return null;
        Check(Career.Data.blueprints.Count == 1 && Career.Data.blueprints[0].name == "DREAM MACHINE",
              "blueprint saved from the drafting table");
        int quote4 = bm.ConvertQuote();
        Career.Txn(3000, "c4 convert grant");
        int s5 = Career.Data.scrap;
        TapNamed("bpconv"); yield return null;
        Check(!Career.devFreeBuild && Career.CountOf("beam", "Tungsten") == 1
              && Career.Data.scrap == s5 - quote4 && bm.CareerShortfallItems().Count == 0,
              "CONVERT buys exactly the missing parts and leaves draft mode");

        // parts shelf shows who is borrowing what
        Tap("PARTS"); yield return null;
        var shelfRow = GameObject.Find("shelf_beam|Aluminum");
        Check(shelfRow != null && shelfRow.GetComponentInChildren<Text>().text.Contains("VICE GRIP"),
              "PARTS shelf names the robot borrowing the beams");

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

        // ---- C6.4: telemetry schema landed (§14) ----
        Check(Career.Data.fights > 0, "telemetry: fights counted (" + Career.Data.fights + ")");
        Check(Career.Data.scrapCurve.Count == Career.Data.fights,
              "telemetry: one scrap sample per settle (" + Career.Data.scrapCurve.Count + ")");
        Check(!string.IsNullOrEmpty(Career.Data.lastContest),
              "telemetry: last contest recorded (" + Career.Data.lastContest + ")");

        // ---- restore: career off puts the sandbox back ----
        Career.active = savedActive;
        Career.Data = savedData;
        Career.autosave = true;
        yield return null; yield return null;
        var shopTab2 = Btn("SHOP");
        Check(savedActive || shopTab2 == null || !shopTab2.gameObject.activeInHierarchy,
              "career off: SHOP tab hides again");
        Finish();
    }

    void Finish()
    {
        foreach (var l in log) Debug.Log("[CareerSmoke] " + l);
        Debug.Log(string.Format("[CareerSmoke] RESULT: {0} pass, {1} fail{2}",
                  passed, failed, failed == 0 ? " - ALL GREEN" : " - FIX NEEDED"));
        finished = true;
    }
}
}
