using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RobotBrawl.Phase0
{

/// <summary>ROUND-3 IMPLEMENTATION CAPTURE (2026-08-01).
///
/// The round-3 fixes are all on the fight OUTCOME screen, and the whole point
/// of the round is that VICTORY and DEFEAT must stop being the same picture -
/// so the only acceptable evidence is one of each, taken minutes apart on the
/// same code path. CareerShot cannot do it: it shoots a LIVE fight frame and
/// never reaches the results overlay.
///
/// Both bouts are real career enrollments of L5C1 (World Championship / The
/// Crucible, purse 3000, entry fee 200) - StartCareerFight debits the fee and
/// sets Career.activeContest - and are then decided by calling
/// FightManager.End() directly rather than waiting out 90 s of simulation.
/// The settlement path (Progression.OnMatchEnd -> Career.SettleFight) is the
/// real one, so the money on screen is the real money.
///
/// Career.autosave is forced off and Career.Load() re-reads from disk at the
/// end, so nothing here reaches the player's save.</summary>
public class R3Dev : MonoBehaviour
{
    public string tag = "r3dev";
    public bool done;
    public string summary = "";

    readonly List<string> log = new List<string>();
    int shots;
    BuilderManager bm;

    public void Run() { StartCoroutine(Go()); }

    IEnumerator Go()
    {
        string dir = Path.Combine(Application.dataPath, "Phase1/qa_shots");
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        Career.active = true;
        Career.autosave = false;
        if (Career.Data == null || Career.Data.inventory.Count == 0) Career.Load();

        bm = Object.FindFirstObjectByType<BuilderManager>();
        if (bm == null)
        {
            foreach (var m in Object.FindObjectsByType<ModeSelect>(FindObjectsSortMode.None))
                Object.Destroy(m.gameObject);
            yield return null;
            bm = new GameObject("BuilderManager").AddComponent<BuilderManager>();
            float tb = Time.realtimeSinceStartup;
            while (bm.placed.Count == 0 && Time.realtimeSinceStartup - tb < 8f) yield return null;
        }
        // A bare new BuilderManager comes up with a core and no drivetrain
        // ("Needs at least 1 wheel"), so no contest is enterable. Load the
        // 17-part reference build the round-3 critic reviewed. READ ONLY -
        // qa_owen_build_SAFE.txt is never written by anything here.
        string bp = Path.Combine(Application.dataPath, "Phase1/qa_owen_build_SAFE.txt");
        if (File.Exists(bp))
        {
            int n = bm.LoadSnapshot(File.ReadAllText(bp));
            yield return null;
            log.Add("# loaded qa_owen_build_SAFE: rows=" + n + " placed=" + bm.placed.Count);
        }
        else log.Add("# qa_owen_build_SAFE.txt NOT FOUND at " + bp);

        MobileBuilderUI.forceMobileUI = true;
        TouchControls.mouseTest = true;
        float t0 = Time.realtimeSinceStartup;
        while (!MobileBuilderUI.Active && Time.realtimeSinceStartup - t0 < 6f) yield return null;

        // Seed the state the round-3 critic reviewed: every league below the
        // World Championship cleared, and every part of the current build
        // owned, so L5C1 is genuinely enterable.
        Career.Data.scrap = 3000;
        foreach (var lg in CareerDB.Leagues)
            if (lg.id != "L5")
                foreach (var c in lg.contests)
                    if (!Career.Data.doneContests.Contains(c.id)) Career.Data.doneContests.Add(c.id);
        foreach (var it in bm.CareerShortfallItems()) Career.AddItem(it.partId, it.mat, it.count);
        yield return null;
        string v = bm.CareerValidate(CareerDB.Leagues[4]);
        log.Add("# R3 IMPLEMENTATION CAPTURE tag=" + tag + "  screen=" + Screen.width + "x" + Screen.height);
        log.Add("# dpi=" + Screen.dpi + "  BuilderManager.GuiScale=" + BuilderManager.GuiScale
                + "  MobileBuilderUI.Active=" + MobileBuilderUI.Active);
        log.Add("# shortfall=" + bm.CareerShortfall().Count + "  validate(L5)=" + (v == null ? "OK" : v)
                + "  L5 unlocked=" + Career.LeagueUnlocked(4));

        // DEFEAT first: a win writes L5C1 into doneContests and would turn the
        // second bout into a 40% re-entry purse.
        yield return Bout(dir, "defeat", FightManager.Outcome.PlayerLoss,
                          "KO — your core was destroyed");
        yield return Bout(dir, "victory", FightManager.Outcome.PlayerWin,
                          "KO — enemy core destroyed");

        Career.Load();            // undo everything above, from disk
        log.Add("# restored from disk: scrap=" + Career.Data.scrap
                + " fights=" + Career.Data.fights + " done=" + Career.Data.doneContests.Count
                + " stable=" + Career.Data.stable.Count);
        Finish("captured " + shots + " screens");
    }

    IEnumerator Bout(string dir, string name, FightManager.Outcome o, string cause)
    {
        Career.Data.scrap = 3000;
        bm.StartCareerFight(4, 0);
        FightManager fm = null;
        float tf = Time.realtimeSinceStartup;
        while ((fm = Object.FindFirstObjectByType<FightManager>()) == null
               && Time.realtimeSinceStartup - tf < 6f) yield return null;
        if (fm == null) { log.Add("# " + name + ": fight did NOT start - " + bm.LastMessage); yield break; }
        float tw = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - tw < 3.5f) yield return null;
        log.Add("# " + name + ": stamp=" + FightManager.BuildStamp + " lastHudW=" + FightManager.lastHudW + " UIS_dpi=" + Screen.dpi);
        log.Add("# " + name + ": in-fight  league=[" + Career.activeLeague + "] contest=["
                + Career.activeContest + "]  scrap after entry fee=" + Career.Data.scrap
                + "  (started from 3000)");
        // finding 4 evidence: the fight HUD, once, mid-bout
        if (name == "defeat") yield return Shot(dir, "fighthud");
        fm.End(o, cause);
        yield return null; yield return null; yield return null;
        log.Add("# " + name + ": after End  league=[" + Career.activeLeague + "] contest=["
                + Career.activeContest + "]  lastSettled=" + Career.lastSettled
                + " lastPay=" + Career.lastPay + "  scrap=" + Career.Data.scrap
                + "  outcome=" + fm.outcome + "  mobileActive=" + MobileBuilderUI.Active
                + "  reward=" + Progression.lastRewardLine);
        yield return Shot(dir, "results_" + name);
        bm.BackToBuild();
        yield return null; yield return null;
    }

    IEnumerator Shot(string dir, string name)
    {
        yield return new WaitForEndOfFrame();
        Texture2D tex = null;
        try { tex = ScreenCapture.CaptureScreenshotAsTexture(); }
        catch (System.Exception e) { log.Add("# capture failed " + name + ": " + e.Message); yield break; }
        if (tex == null) { log.Add("# null texture for " + name); yield break; }
        string file = Path.Combine(dir, tag + "_" + name + ".png");
        try
        {
            File.WriteAllBytes(file, tex.EncodeToPNG());
            log.Add(string.Format("{0,-18} {1}x{2}  {3}", name, tex.width, tex.height, Path.GetFileName(file)));
            shots++;
        }
        catch (System.Exception e) { log.Add("# write failed " + name + ": " + e.Message); }
        finally { Object.Destroy(tex); }
    }

    void Finish(string msg)
    {
        summary = msg;
        log.Add("# " + msg);
        try
        {
            File.WriteAllText(Path.Combine(Application.dataPath, "Phase1/qa_shots/index_" + tag + ".txt"),
                              string.Join("\n", log.ToArray()) + "\n");
        }
        catch { }
        done = true;
        Debug.Log("R3Dev: " + msg);
    }
}

}
