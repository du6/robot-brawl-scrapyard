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
using System.IO;
using System.Text;
using UnityEngine;

namespace RobotBrawl.Phase0
{

/// <summary>
/// MEDAL PROOF HARNESS (2026-08-02).
///
/// Proves the league-champion medal end to end through the REAL settle path:
/// seeds a throwaway career one contest short of sweeping League 1, runs that
/// last contest as a real career fight, ends it with FightManager.End() (what
/// a real KO calls), and photographs the RESULTS ribbon, the mobile LEAGUE
/// board (which carries the trophy case since 2026-08-10), the mobile ROBOTS
/// card and the desktop IMGUI trophy block.
///
/// SAFETY: the player's real save files are read into memory as BYTES before
/// anything is touched, Career.autosave is forced off for the whole run, and
/// both files are written back verbatim at the end with an md5 check logged.
/// </summary>
public class MedalDev : MonoBehaviour
{
    public string tag = "med1";
    public bool done;
    public string summary = "";
    public float desktopScroll = 900f;

    readonly List<string> log = new List<string>();
    int shots;

    static string CareerPath { get { return Path.Combine(Application.persistentDataPath, "robotbrawl_career.json"); } }
    static string ProfilePath { get { return Path.Combine(Application.persistentDataPath, "robotbrawl_profile.json"); } }

    byte[] careerBak, profileBak;
    bool careerExisted, profileExisted, savedAutosave, savedFree;

    public void Run() { StartCoroutine(Go()); }

    static string Hash(byte[] b)
    {
        if (b == null) return "(absent)";
        using (var md5 = System.Security.Cryptography.MD5.Create())
        {
            var h = md5.ComputeHash(b);
            var sb = new StringBuilder();
            foreach (var x in h) sb.Append(x.ToString("x2"));
            return sb.ToString() + " (" + b.Length + " bytes)";
        }
    }

    IEnumerator Go()
    {
        string dir = Path.Combine(Application.dataPath, "Phase1/qa_shots");
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        careerExisted = File.Exists(CareerPath);
        profileExisted = File.Exists(ProfilePath);
        careerBak = careerExisted ? File.ReadAllBytes(CareerPath) : null;
        profileBak = profileExisted ? File.ReadAllBytes(ProfilePath) : null;
        log.Add("# MEDAL PROOF  tag=" + tag);
        log.Add("# save BEFORE  career  = " + Hash(careerBak));
        log.Add("# save BEFORE  profile = " + Hash(profileBak));
        savedAutosave = Career.autosave;
        savedFree = Career.devFreeBuild;
        Career.autosave = false;

        Career.active = true;
        var bm = Object.FindFirstObjectByType<BuilderManager>();
        if (bm == null)
        {
            foreach (var m in Object.FindObjectsByType<ModeSelect>(FindObjectsSortMode.None))
                Object.DestroyImmediate(m.gameObject);
            yield return null;
            bm = new GameObject("BuilderManager").AddComponent<BuilderManager>();
            float tb = Time.realtimeSinceStartup;
            while (bm.placed.Count == 0 && Time.realtimeSinceStartup - tb < 10f) yield return null;
        }
        MobileBuilderUI.forceMobileUI = true;
        TouchControls.mouseTest = true;
        float t0 = Time.realtimeSinceStartup;
        while (!MobileBuilderUI.Active && Time.realtimeSinceStartup - t0 < 8f) yield return null;

        // One contest short of sweeping League 1: L1C1 beaten, L1C2 is the
        // campaign decider. That is the exact state the medal must fire in.
        Career.Data = new CareerData();
        Career.Data.scrap = 4000;
        Career.Data.doneContests.Add("L1C1");
        Career.Data.fights = 3; Career.Data.fightWins = 1;
        Career.Data.stable.Add(new CareerRobot { name = "Rustbucket", wins = 1, losses = 2, snapshot = "" });
        Career.Data.activeRobot = 0;
        Career.Data.kitGranted = true; Career.Data.kitVersion = Career.KitVersion;

        var recipe = EnemyRoster.Recipe("scout", P1PartDef.Palette());
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var sb2 = new StringBuilder();
        sb2.Append("medaldev\n");
        foreach (var p in recipe)
            sb2.Append(p.def.id).Append('|')
               .Append(p.pos.x.ToString("F3", inv)).Append(',')
               .Append(p.pos.y.ToString("F3", inv)).Append(',')
               .Append(p.pos.z.ToString("F3", inv)).Append('|')
               .Append(p.yaw).Append('|')
               .Append(p.wheelAxis.x.ToString("F2", inv)).Append(',')
               .Append(p.wheelAxis.y.ToString("F2", inv)).Append(',')
               .Append(p.wheelAxis.z.ToString("F2", inv)).Append('|')
               .Append(p.MatName()).Append('\n');
        int loaded = bm.LoadSnapshot(sb2.ToString());
        yield return null;
        foreach (var kv in Career.SnapshotUsage(bm.SnapshotString()))
        {
            var f = kv.Key.Split('|');
            Career.AddItem(f[0], f[1], kv.Value * 2);
        }
        Career.Data.stable[0].snapshot = bm.SnapshotString();
        log.Add("# seeded: loaded " + loaded + " parts, mass " + bm.BuildMassInt
                + " kg, Validate=" + (bm.Validate() ?? "ok")
                + ", CareerValidate=" + (bm.CareerValidate(CareerDB.Leagues[0]) ?? "ok"));
        log.Add("# seeded: doneContests=" + string.Join(",", Career.Data.doneContests.ToArray())
                + " medals=" + Career.Data.medals.Count
                + " LeagueUnlocked(1)=" + Career.LeagueUnlocked(1));

        bm.StartCareerFight(0, 1);
        yield return null; yield return null;
        var fm = Object.FindFirstObjectByType<FightManager>();
        log.Add("# fight: started=" + (fm != null) + " activeContest=" + (Career.activeContest ?? "null")
                + (fm == null ? "  msg=" + bm.LastMessage : ""));
        if (fm == null) { Restore(); Finish("FIGHT DID NOT START - see msg above"); yield break; }
        float tw = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - tw < 3.5f) yield return null;
        fm.End(FightManager.Outcome.PlayerWin, "KO — enemy core destroyed");
        yield return null; yield return null;

        log.Add("# AFTER SETTLE  medals=" + Career.Data.medals.Count);
        foreach (var m in Career.Data.medals)
            log.Add("#   medal: li=" + m.leagueIndex + " " + m.leagueId + " " + m.leagueName
                    + " / " + m.arenaName + " / " + m.robot + " " + m.wins + "-" + m.losses
                    + " / " + m.contests + " contests / " + m.when);
        log.Add("# robot titles=" + Career.Data.stable[0].titles
                + " leagueHistory=[" + string.Join(" ; ", Career.Data.stable[0].leagueHistory.ToArray()) + "]");
        foreach (var t in Career.Data.txns)
            if (t.cause.Contains("medal")) log.Add("# ledger: " + t.when + "  " + t.delta + "  " + t.cause);
        log.Add("# Career.lastMedal=" + (Career.lastMedal == null ? "null" : Career.lastMedal.leagueName));

        yield return Shot(dir, "results");

        int before = Career.Data.medals.Count;
        Career.activeLeague = "L1"; Career.activeContest = "L1C2";
        Career.SettleFight(true, 100f);
        Career.activeLeague = "L1"; Career.activeContest = "L1C2";
        Career.SettleFight(true, 100f);
        log.Add("# DOUBLE-AWARD GUARD: medals before=" + before + " after two more L1C2 wins=" + Career.Data.medals.Count
                + "  titles=" + Career.Data.stable[0].titles
                + (Career.Data.medals.Count == before ? "  PASS" : "  FAIL"));

        bm.BackToBuild();
        yield return null; yield return null; yield return null;

        MobileBuilderUI.forceMobileUI = true;
        float t2 = Time.realtimeSinceStartup;
        while (!MobileBuilderUI.Active && Time.realtimeSinceStartup - t2 < 8f) yield return null;
        var ui = MobileBuilderUI.inst;
        if (ui != null)
        {
            // ⚠ THIS SAID 5 AND HAD BEEN WRONG SINCE 2026-08-05. Removing the
            // PARTS tab renumbered everything above index 4, and this call was
            // one of three sites the pass missed (TabHint's cases 4 and 5 were
            // the others) — so every "trophies.png" produced for five days was
            // a photograph of the PROGRAM tab, filed under the wrong name.
            // Nothing failed: a screenshot of the wrong screen is still a
            // screenshot.
            //
            // It is 1 now, LEAGUE, because that is where the trophy case went
            // when ARENA took index 4 (owen, 2026-08-10).
            ui.TestShowTab(1);
            yield return null; yield return null;
            log.Add("# mobile: tab=" + ui.TestTab + " dockH=" + ui.TestDockHeight);
            yield return Shot(dir, "trophies");
            ui.TestShowTab(2);
            yield return null; yield return null;
            yield return Shot(dir, "robots");
            ui.TestShowTab(0);
            yield return null;
        }
        else log.Add("# WARNING mobile UI not up - trophies shot is not the touch UI");

        MobileBuilderUI.forceMobileUI = false;
        for (int f = 0; f < 8; f++)
        {
            if (MobileBuilderUI.inst != null) Object.DestroyImmediate(MobileBuilderUI.inst.gameObject);
            yield return null;
        }
        log.Add("# desktop: MobileBuilderUI.Active=" + MobileBuilderUI.Active
                + " (false = the IMGUI panel really drew)");
        float[] scrolls = { 480f, 600f, 720f };
        for (int si = 0; si < scrolls.Length; si++)
        {
            bm.PanelScrollY = scrolls[si];
            yield return null; yield return null;
            yield return Shot(dir, "desktop_trophies" + si);
        }

        Restore();
        MobileBuilderUI.forceMobileUI = true;
        Finish("captured " + shots + " screens");
    }

    void Restore()
    {
        try
        {
            if (careerExisted) File.WriteAllBytes(CareerPath, careerBak);
            if (profileExisted) File.WriteAllBytes(ProfilePath, profileBak);
            var nowCareer = File.Exists(CareerPath) ? File.ReadAllBytes(CareerPath) : null;
            var nowProfile = File.Exists(ProfilePath) ? File.ReadAllBytes(ProfilePath) : null;
            log.Add("# save AFTER   career  = " + Hash(nowCareer));
            log.Add("# save AFTER   profile = " + Hash(nowProfile));
            log.Add("# RESTORED career=" + (Hash(nowCareer) == Hash(careerBak) ? "IDENTICAL" : "DIFFERENT")
                    + " profile=" + (Hash(nowProfile) == Hash(profileBak) ? "IDENTICAL" : "DIFFERENT"));
        }
        catch (System.Exception e) { log.Add("# RESTORE FAILED: " + e.Message); }
        Career.autosave = savedAutosave;
        Career.devFreeBuild = savedFree;
        Career.Load();
        log.Add("# in-memory career reloaded: scrap=" + Career.Data.scrap
                + " stable=" + Career.Data.stable.Count
                + " done=" + Career.Data.doneContests.Count
                + " medals=" + Career.Data.medals.Count);
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
        Debug.Log("MedalDev: " + msg);
    }
}

}
#endif
