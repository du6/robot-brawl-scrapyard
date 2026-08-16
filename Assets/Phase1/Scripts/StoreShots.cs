using UnityEngine;
using UnityEngine.UI;
using System.Collections;

namespace RobotBrawl.Phase0
{
// ============================================================================
// StoreShots — the App Store screenshot set, photographed from the live game
// (owen, 2026-08-15, launch day). Five product moments, captured at whatever
// resolution the Device Simulator is showing, written as PNGs to `dir`:
//
//   01_build    BUILD tab, a real fighting robot on the pad, palette open
//   02_fight    an exhibition bout mid-action (sandbox — no career gate)
//   03_arena    THE BOARD, signed in to PRODUCTION read-only (the real ladder)
//   04_shop     the parts shop
//   05_program  the PROGRAM tab
//
// ⚠ PRODUCTION IS TOUCHED READ-ONLY. Unlike ArenaShots (which REGISTERS
// accounts and refuses production), this harness only LOGS IN with the
// dedicated review account and GETs the board — the same requests the shipped
// app makes on its ARENA tab. Nothing is enlisted, challenged or written.
// Set Email/Password before Run; with them empty the arena shot is skipped.
//
// Owner state: career held via SuspendAutosave and restored in the finally;
// LadderClient token/BaseUrl restored; Login here never persists a session
// (only LoginGate.Done / ArenaScreen.DoAuth call SaveSession).
//
// Run in play mode:
//   StoreShots.Email = "..."; StoreShots.Password = "...";
//   StoreShots.Run("/path/to/dir");   // read [StoreShots] console lines
// ============================================================================
public class StoreShots : MonoBehaviour
{
    public static bool finished;
    public static int shots;
    public static string LastError = "";
    public static string Email = "", Password = "";

    public static void Run(string dir)
    {
        finished = false; shots = 0; LastError = "";
        new GameObject("store_shots").AddComponent<StoreShots>().StartCoroutine(All(dir));
    }

    const string ROBOT =
          "core|0.000,0.700,0.000|0|0.00,0.00,0.00|Aluminum\n"
        + "beam|-0.250,0.700,0.000|0|0.00,0.00,0.00|Steel\n"
        + "beam|-0.250,0.700,-0.600|0|0.00,0.00,0.00|Steel\n"
        + "beam|0.250,0.700,0.000|0|0.00,0.00,0.00|Steel\n"
        + "beam|0.250,0.700,-0.600|0|0.00,0.00,0.00|Steel\n"
        + "wheel|0.420,0.700,0.150|0|1.00,0.00,0.00|Rubber\n"
        + "wheel|0.420,0.700,-0.750|0|1.00,0.00,0.00|Rubber\n"
        + "wheel|-0.420,0.700,0.150|0|-1.00,0.00,0.00|Rubber\n"
        + "wheel|-0.420,0.700,-0.750|0|-1.00,0.00,0.00|Rubber\n"
        + "beam|0.000,0.700,-0.450|0|0.00,0.00,0.00|Aluminum\n"
        + "battery|0.000,0.925,-0.450|0|0.00,0.00,0.00|Aluminum\n"
        + "spindle|0.000,1.000,0.000|0|0.00,1.00,0.00|Aluminum\n"
        + "beam|0.000,1.250,0.000|0|0.00,0.00,0.00|Titanium\n"
        + "beamlong|0.000,1.250,0.800|0|0.00,0.00,0.00|Titanium\n"
        + "gyro|0.000,1.030,1.100|0|0.00,0.00,0.00|Aluminum\n"
        + "spike|0.000,1.030,1.370|0|0.00,0.00,1.00|Steel\n";

    static IEnumerator Shot(string dir, string name)
    {
        string path = dir + "/" + name + ".png";
        UiShot.Take(path, 0.6f);
        int guard = 0;
        while (!UiShot.Done && guard++ < 600) yield return null;
        if (string.IsNullOrEmpty(UiShot.LastError))
        { shots++; Debug.Log("[StoreShots] " + name + ".png  " + UiShot.LastWidth + "x" + UiShot.LastHeight); }
        else { LastError = UiShot.LastError; Debug.Log("[StoreShots] " + name + " FAILED: " + UiShot.LastError); }
    }

    static IEnumerator All(string dir)
    {
        var hold = Career.SuspendAutosave();
        var savedData = Career.Data; bool savedActive = Career.active;
        string savedTok = LadderClient.Token, savedUrl = LadderClient.BaseUrl;
        MobileBuilderUI.forceMobileUI = true;
        try
        {
            System.IO.Directory.CreateDirectory(dir);
            var ms = GameObject.Find("ModeSelect"); if (ms != null) Destroy(ms);
            var bm = Object.FindFirstObjectByType<BuilderManager>();
            if (bm == null) bm = new GameObject("BuilderManager").AddComponent<BuilderManager>();

            // Scratch career with the starter shelf, so tiles carry stock badges.
            Career.active = true;
            Career.Data = new CareerData();
            Career.GrantStarterKit();   // operates on Career.Data; fills the shelf badges
            // Own every part the showcase robot uses, or the amber "parts you
            // don't own" bar sits across every screenshot. And a lived-in scrap
            // balance via Txn (never a bare Data.scrap= — TxnSum==scrap is an
            // audited invariant).
            Career.AddItem("beam", "Steel", 4);
            Career.AddItem("beam", "Titanium", 1);
            Career.AddItem("beamlong", "Titanium", 1);
            Career.AddItem("gyro", "Aluminum", 1);
            Career.Txn(2485, "storeshot fixture");
            Career.Data.stable.Add(new CareerRobot { name = "Ironclad", snapshot = BuilderManager.SNAP_STAMP + "\n" + ROBOT,
                                                     program = RobotProgram.Brawler().ToJson() });
            Career.Data.activeRobot = 0;

            if (MobileBuilderUI.inst != null) { Destroy(MobileBuilderUI.inst.gameObject); yield return null; }
            float t0 = Time.realtimeSinceStartup;
            while (!MobileBuilderUI.Active && Time.realtimeSinceStartup - t0 < 6f) yield return null;
            var ui = MobileBuilderUI.inst;
            if (ui == null) { LastError = "no dock"; yield break; }
            yield return null; yield return null;
            if (!ui.DockOpen) ui.SetDockOpen(true);
            yield return null;

            // Dismiss the onboarding tip bar — a store shot should show the
            // game, not tip 4/7. Real button, real onClick (the house rule).
            foreach (var b in Object.FindObjectsByType<Button>(FindObjectsSortMode.None))
            { var t = b.GetComponentInChildren<Text>(); if (t != null && t.text.Contains("SKIP TIPS")) { b.onClick.Invoke(); break; } }
            yield return null;

            // ---- 01 BUILD: the robot on the pad, palette open ---------------
            bm.LoadSnapshot(BuilderManager.SNAP_STAMP + "\n" + ROBOT);
            ui.ShowTab(0);
            yield return null; yield return null; yield return null;
            yield return Shot(dir, "01_build");

            // ---- 04 SHOP (while career state is hot) -------------------------
            ui.ShowTab(3);
            yield return null; yield return null;
            yield return Shot(dir, "04_shop");

            // ---- 05 PROGRAM ---------------------------------------------------
            ui.ShowTab(5);
            yield return null; yield return null;
            yield return Shot(dir, "05_program");

            // ---- 03 ARENA: the real production board, read-only ---------------
            if (Email.Length > 0)
            {
                LadderClient.BaseUrl = LadderClient.PRODUCTION;
                string err = null;
                yield return LadderClient.Login(Email, Password, (w, e) => { err = e; });
                if (err == null)
                {
                    ui.ShowTab(4);
                    var arena = Object.FindFirstObjectByType<ArenaScreen>();
                    float t1 = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - t1 < 12f
                           && (arena == null || arena.Board.Count == 0))
                    { arena = Object.FindFirstObjectByType<ArenaScreen>(); yield return null; }
                    // MIDDLE board: HOUSE bots only — the FEATHER class carries
                    // dev-test enlistee names that don't belong in a store shot.
                    foreach (var b in Object.FindObjectsByType<Button>(FindObjectsSortMode.None))
                    { var t = b.GetComponentInChildren<Text>(); if (t != null && t.text == "MIDDLE") { b.onClick.Invoke(); break; } }
                    float t2m = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - t2m < 8f && (arena == null || arena.Busy)) yield return null;
                    yield return null; yield return null;
                    // The "[dev] <url>" suffix is the EDITOR's debug badge
                    // (isDebugBuild is always true here); a production build
                    // never renders it, so stripping it makes the shot MORE
                    // faithful to the shipped app, not less. Freeze the dock's
                    // Update (it repaints the line every frame), edit, shoot.
                    ui.enabled = false;
                    foreach (var tx in Object.FindObjectsByType<Text>(FindObjectsSortMode.None))
                    {
                        int di = tx.text.IndexOf(" · [dev]");
                        if (di < 0) di = tx.text.IndexOf("[dev]");
                        if (di > 0) tx.text = tx.text.Substring(0, di).TrimEnd(' ', '·');
                    }
                    yield return null;
                    yield return Shot(dir, "03_arena");
                    ui.enabled = true;
                }
                else Debug.Log("[StoreShots] arena skipped — login failed: " + err);
                LadderClient.Logout();
                LadderClient.BaseUrl = savedUrl;
            }
            else Debug.Log("[StoreShots] arena skipped — no credentials set");

            // ---- 02 FIGHT: sandbox exhibition, mid-action ----------------------
            Career.active = false;                       // no career gate, no owner risk
            yield return null; yield return null;        // dock watcher reacts
            bm.LoadSnapshot(BuilderManager.SNAP_STAMP + "\n" + ROBOT);
            yield return null;
            Progression.activeRungIndex = -1; Progression.activeChallengeIdx = -1;
            bm.StartFight();
            // Let the bout ENGAGE — at 4.5s the machines were still crossing
            // the arena and the first pass photographed two distant dots. ~9s
            // in they are trading hits mid-frame.
            float t2 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t2 < 9f) yield return null;
            yield return Shot(dir, "02_fight");
            bm.BackToBuild();
            yield return null;
        }
        finally
        {
            LadderClient.Token = savedTok; LadderClient.BaseUrl = savedUrl;
            Career.Data = savedData; Career.active = savedActive;
            hold.Dispose();
            Debug.Log("[StoreShots] RESULT: " + shots + " shot(s)"
                      + (LastError.Length > 0 ? "  LastError=" + LastError : "  ALL OK"));
            finished = true;
        }
    }
}
}
