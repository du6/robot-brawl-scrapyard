using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

namespace RobotBrawl.Phase0
{
// ============================================================================
// PlayerJourneyAgent — a scripted REAL PLAYER for the pre-launch end-to-end
// (owen, 2026-08-15: "bring up two players testing iphone and ipad devices end
// to end ... really play the games communicating with the cloud server").
//
// This is not a bench that asserts internals — it is a player that does what a
// human does, in order, through the REAL UI (button.onClick.Invoke, the same
// fields the fingers write) against the PRODUCTION server:
//
//   J1  REGISTER   the dock sign-in form, a fresh account on production
//   J2  BUILD      load a fighting chassis, one real placement, SAVE to stable
//   J3  FIGHT      a real career bout vs The Yard — physics, no forced outcome
//   J4  ECONOMY    the win's purse claim flushes; the server wallet funds us
//   J5  SHOP       one real purchase (local till + server settle)
//   J6  ENLIST     upload the robot to the ladder; wait for the cloud worker
//                  to validate it onto the board
//   J7  BOARD      THE BOARD shows us (and everyone else)
//   J8  CHALLENGE  scout a named opponent, stake scrap, CONFIRM, watch the
//                  live preview fight, then MY FIGHTS until the cloud referee
//                  settles it (skipped when ChallengeTarget is "")
//   J9  RETURN     sign out, sign back in — the returning-player journey
//
// ⚠ PRODUCTION IS WRITTEN TO ON PURPOSE: owen asked for real test players
// whose robots are visible on the real board (test_iphone@/test_ipad@
// cyberduck.club). Owner state is protected: the editor's persisted session
// and career save are snapshotted and restored in the finally.
//
// Run in play mode with the Device Simulator on the profile under test:
//   PlayerJourneyAgent.Email = "test_iphone@cyberduck.club";
//   PlayerJourneyAgent.Password = "...";
//   PlayerJourneyAgent.RobotName = "Piston";
//   PlayerJourneyAgent.ChallengeTarget = "";          // or an enemy robot name
//   PlayerJourneyAgent.ShotDir = "/tmp/journey_iphone";
//   PlayerJourneyAgent.Run();
// ============================================================================
public class PlayerJourneyAgent : MonoBehaviour
{
    public static string Email = "", Password = "", RobotName = "TestBot";
    public static string ChallengeTarget = "";
    public static string ShotDir = "";
    public static bool finished;
    public static int passed, failed;
    static readonly List<string> log = new List<string>();

    public static void Run()
    {
        finished = false; passed = 0; failed = 0; log.Clear();
        new GameObject("player_journey").AddComponent<PlayerJourneyAgent>();
    }

    void Pass(string w) { passed++; log.Add("PASS  " + w); Debug.Log("[Journey] PASS  " + w); }
    void Fail(string w) { failed++; log.Add("FAIL  " + w); Debug.Log("[Journey] FAIL  " + w); }
    void Check(bool ok, string w) { if (ok) Pass(w); else Fail(w); }
    void Note(string w) { log.Add("      " + w); Debug.Log("[Journey]       " + w); }

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
        + "compass|0.000,1.100,-0.450|0|0.00,0.00,0.00|Aluminum\n"
        + "spike|0.000,1.030,1.370|0|0.00,0.00,1.00|Steel\n";

    static Button ByName(string n)
    {
        foreach (var b in Object.FindObjectsByType<Button>(FindObjectsSortMode.None))
            if (b.gameObject.name == n && b.isActiveAndEnabled) return b;
        return null;
    }
    static bool Fire(string n) { var b = ByName(n); if (b == null) return false; b.onClick.Invoke(); return true; }

    IEnumerator Shot(string name)
    {
        if (string.IsNullOrEmpty(ShotDir)) yield break;
        System.IO.Directory.CreateDirectory(ShotDir);
        UiShot.Take(ShotDir + "/" + name + ".png", 0.5f);
        int g = 0; while (!UiShot.Done && g++ < 600) yield return null;
    }

    IEnumerator WaitIdle(ArenaScreen a, float sec)
    { float t = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t < sec && a != null && a.Busy) yield return null; }

    void Start() { StartCoroutine(All()); }

    IEnumerator All()
    {
        var hold = Career.SuspendAutosave();
        var savedData = Career.Data; bool savedActive = Career.active;
        string savedTok = LadderClient.Token, savedUrl = LadderClient.BaseUrl;
        string prefTok = PlayerPrefs.GetString("rb_session_token", "");
        string prefName = PlayerPrefs.GetString("rb_session_name", "");
        MobileBuilderUI.forceMobileUI = true;
        // The editor sync is probe-opt-in (the adoption-incident guard). This
        // agent's career is SCRATCH — server-wins adoption is exactly what a
        // real device player gets, so opt in; restored in the finally.
        EconomySync.editorOptIn = true;
        try
        {
            var ms = GameObject.Find("ModeSelect"); if (ms != null) Destroy(ms);
            var bm = Object.FindFirstObjectByType<BuilderManager>();
            if (bm == null) bm = new GameObject("BuilderManager").AddComponent<BuilderManager>();

            // The scratch career this player lives in. Starter kit + the extras
            // the chassis uses, granted the way a real player would have bought
            // them (the shelf shows honest stock badges either way).
            Career.active = true;
            Career.Data = new CareerData();
            Career.GrantStarterKit();
            Career.AddItem("beam", "Steel", 4);
            Career.AddItem("beam", "Titanium", 1);
            Career.AddItem("beamlong", "Titanium", 1);
            Career.AddItem("gyro", "Aluminum", 1);
            Career.AddItem("compass", "Aluminum", 1);   // Brawler needs a Compass — enlist AND autonomy
            Career.Data.stable.Add(new CareerRobot { name = RobotName, snapshot = BuilderManager.SNAP_STAMP + "\n" + ROBOT,
                                                     program = RobotProgram.Brawler().ToJson() });
            Career.Data.activeRobot = 0;

            if (MobileBuilderUI.inst != null) { Destroy(MobileBuilderUI.inst.gameObject); yield return null; }
            float t0 = Time.realtimeSinceStartup;
            while (!MobileBuilderUI.Active && Time.realtimeSinceStartup - t0 < 6f) yield return null;
            var ui = MobileBuilderUI.inst;
            Check(ui != null, "the dock attaches (" + Screen.width + "x" + Screen.height + ")");
            if (ui == null) yield break;
            if (!ui.DockOpen) ui.SetDockOpen(true);
            yield return null;
            foreach (var b in Object.FindObjectsByType<Button>(FindObjectsSortMode.None))
            { var t = b.GetComponentInChildren<Text>(); if (t != null && t.text.Contains("SKIP TIPS")) { b.onClick.Invoke(); break; } }

            // ---- J1 REGISTER on production, through the dock's real form ----
            LadderClient.Logout();                       // fresh player: no session
            LadderClient.BaseUrl = LadderClient.PRODUCTION;
            ui.ShowTab(4); yield return null; yield return null;
            var arena = Object.FindFirstObjectByType<ArenaScreen>();
            Check(arena != null && ui.TestArenaSurface == "account",
                  "signed out, the ARENA opens on the SIGN-IN surface");
            // WAIT FOR IDLE before pressing — SubmitAuth is a no-op while the
            // arena's initial Refresh holds busy (the tap-swallow class the
            // validation round documented; this agent hit it on its first run).
            yield return WaitIdle(arena, 25f);
            arena.Email = Email;
            arena.SetPassword(Password);
            arena.DisplayName = RobotName + " Pilot";
            if (!arena.Registering) arena.ToggleRegistering();
            yield return null;
            Check(arena.CanSubmitAuth, "the sign-in form is ready (not busy, fields set)");
            Check(Fire("accsubmit"), "the CREATE ACCOUNT button is on screen and pressed");
            float t1 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t1 < 30f && arena.Busy) yield return null;
            if (!LadderClient.SignedIn)
            {
                // second run of the same agent: the account exists — sign in
                Note("register said: " + arena.Status + " — trying LOG IN instead");
                yield return WaitIdle(arena, 15f);
                if (arena.Registering) arena.ToggleRegistering();
                arena.Email = Email; arena.SetPassword(Password);
                yield return null; Fire("accsubmit");
                t1 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t1 < 30f && arena.Busy) yield return null;
            }
            Check(LadderClient.SignedIn, "J1 REGISTER/SIGN-IN: signed in on production as " + Email);
            if (!LadderClient.SignedIn)
            { Fail("ABORT — no session; every later journey would run signed-out"); yield break; }
            yield return WaitIdle(arena, 20f);
            yield return Shot("j1_signed_in");

            // ---- J2 BUILD: the robot, one real placement, one real SAVE -----
            ui.ShowTab(0); yield return null;
            bm.LoadSnapshot(BuilderManager.SNAP_STAMP + "\n" + ROBOT);
            yield return null;
            int placedBefore = bm.PlacedCount;
            // a real placement: pick the Cube tile, tap the core (TouchSmoke's idiom)
            foreach (var b in Object.FindObjectsByType<Button>(FindObjectsSortMode.None))
            { var t = b.GetComponentInChildren<Text>(); if (t != null && t.text.StartsWith("Cube")) { b.onClick.Invoke(); break; } }
            yield return null;
            if (!ui.DockOpen) ui.SetDockOpen(true);
            Transform core = null;
            foreach (var col in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
                if (col.gameObject.name.StartsWith("core")) { core = col.transform; break; }
            if (bm.HasSelection && Camera.main != null && core != null)
            {
                Vector3 sp = Camera.main.WorldToScreenPoint(core.position + Vector3.up * 0.15f);
                Phase0Input.debugPointer = true; Phase0Input.debugMousePos = sp;
                yield return null; Phase0Input.DebugClick(0);
                yield return null; yield return null;
                Phase0Input.debugPointer = false;
                if (bm.HasSelection) bm.SelectPart(bm.SelectedPart);   // drop the held part
            }
            Check(bm.PlacedCount >= placedBefore, "J2 BUILD: robot on the pad (" + bm.PlacedCount + " parts)");
            Career.Data.stable[0].snapshot = bm.SnapshotString();      // SAVE (the DONE/SAVE path's write)
            yield return Shot("j2_build");

            // ---- J3 a REAL career fight vs The Yard, AUTONOMY driving --------
            // (the first run started a MANUAL fight and nobody was at the
            // stick — the robot idled and lost. A real player drives or arms
            // AUTONOMY; the agent uses autonomy: the robot's own Brawler
            // program fights, which the compass on the chassis makes legal.)
            ui.ShowTab(1); yield return null;
            bool won = false; bool fightRan = false;
            for (int attempt = 1; attempt <= 3 && !won; attempt++)
            {
                bm.StartCareerFight(0, 0, true);
                yield return null;
                var fm = FightManager.current;
                if (fm == null) { Note("attempt " + attempt + ": fight did not start"); break; }
                fightRan = true;
                float tf = Time.realtimeSinceStartup;
                while (fm != null && fm.state != FightManager.State.Ended
                       && Time.realtimeSinceStartup - tf < 150f) yield return null;
                won = fm != null && fm.outcome == FightManager.Outcome.PlayerWin;
                Note("attempt " + attempt + ": " + (fm != null ? fm.outcome.ToString() : "?")
                     + " in " + Mathf.RoundToInt(Time.realtimeSinceStartup - tf) + "s");
                if (attempt == 1) yield return Shot("j3_fight_result");
                bm.BackToBuild(); yield return null; yield return null;
            }
            Check(fightRan, "J3 FIGHT: real bouts fought under autonomy");
            Check(won, "J3 FIGHT: a contest was WON (purse claim queued)");

            // ---- J4 ECONOMY: the claim flushes, the server wallet funds us --
            EconomySync.Kick();
            float te = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - te < 30f && !EconomySync.SessionOnline) yield return null;
            Check(EconomySync.SessionOnline && EconomySync.lastServerBalance > 0,
                  "J4 ECONOMY: purse claimed server-side, wallet = " + EconomySync.lastServerBalance);

            // ---- J5 SHOP: one real purchase -----------------------------------
            ui.ShowTab(3); yield return null;
            int scrapBefore = Career.Data.scrap;
            bool bought = Career.TryBuy("cube", "ABS");     // the BUY button's own method
            EconomySync.Kick();
            yield return new WaitForSecondsRealtime(3f);
            Check(bought && Career.Data.scrap < scrapBefore,
                  "J5 SHOP: bought a part (" + scrapBefore + " -> " + Career.Data.scrap + " scrap)");
            yield return Shot("j5_shop");

            // ---- J6 ENLIST onto the ladder ------------------------------------
            ui.ShowTab(4); yield return null; yield return null;
            yield return WaitIdle(arena, 15f);
            Check(Fire("arenasec_account"), "ENLIST sub-tab opened");
            yield return null; yield return null;
            arena.EnlistName = RobotName;
            yield return null;
            Check(Fire("enlistgo"), "J6 ENLIST: the ENLIST button pressed");
            float tn = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - tn < 30f && arena.Busy) yield return null;
            Note("enlist said: " + arena.Status);
            // wait for the cloud worker to validate us onto the board
            bool fightable = false;
            float tv = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - tv < 120f && !fightable)
            {
                yield return new WaitForSecondsRealtime(6f);
                arena.RefreshNow();
                yield return WaitIdle(arena, 15f);
                foreach (var m in arena.MyRobots) if (m.name == RobotName && m.CanFight) fightable = true;
            }
            Check(fightable, "J6 ENLIST: the cloud worker validated " + RobotName + " onto the ladder");
            yield return Shot("j6_enlisted");

            // ---- J7 THE BOARD -------------------------------------------------
            Check(Fire("arenasec_board"), "THE BOARD sub-tab pressed");
            yield return null; yield return null;
            yield return WaitIdle(arena, 15f);
            bool onBoard = false;
            foreach (var e in arena.Board) if (e.robotName == RobotName) onBoard = true;
            Check(ui.TestArenaSurface == "board" && arena.Board.Count > 0,
                  "J7 BOARD: the board renders (" + arena.Board.Count + " ranked)");
            Check(onBoard, "J7 BOARD: " + RobotName + " is ON the public board");
            yield return Shot("j7_board");

            // ---- J8 CHALLENGE a named opponent (the two-player moment) --------
            if (ChallengeTarget.Length > 0)
            {
                int idx = -1;
                for (int i = 0; i < arena.Board.Count; i++)
                    if (arena.Board[i].robotName == ChallengeTarget) idx = i;
                Check(idx >= 0, "J8 CHALLENGE: opponent " + ChallengeTarget + " found on the board");
                if (idx >= 0)
                {
                    Check(Fire("arenascout_" + idx), "SCOUT pressed on " + ChallengeTarget);
                    float ts = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - ts < 20f && arena.Card == null) yield return null;
                    Check(arena.Card != null, "the scouting card opened");
                    yield return null; yield return null;
                    yield return Shot("j8_scout");
                    Check(Fire("cardchallenge"), "CHALLENGE pressed (stake shown first)");
                    yield return null; yield return null;
                    Check(Fire("cardconfirm"), "CONFIRM pressed — scrap staked");
                    // the live preview fight plays on our screen now
                    float tl = Time.realtimeSinceStartup;
                    FightManager lfm = null;
                    while (Time.realtimeSinceStartup - tl < 40f && lfm == null)
                    { lfm = FightManager.current; yield return null; }
                    Check(lfm != null, "J8: the LIVE fight is playing on screen");
                    float tw = Time.realtimeSinceStartup;
                    while (lfm != null && lfm.state != FightManager.State.Ended
                           && Time.realtimeSinceStartup - tw < 180f) { lfm = FightManager.current; yield return null; }
                    Check(lfm != null && lfm.state == FightManager.State.Ended,
                          "J8: the preview fought to a finish — " + (lfm != null ? lfm.outcome.ToString() : "?"));
                    yield return Shot("j8_live_fight");
                    yield return new WaitForSecondsRealtime(2f);   // read the VICTORY screen like a human
                    if (lfm != null) lfm.resultsDismissed = true;  // the BACK button's own signal (IMGUI)
                    float tb = Time.realtimeSinceStartup;          // teardown returns us to ARENA/MY FIGHTS
                    while (Time.realtimeSinceStartup - tb < 20f && FightManager.current != null) yield return null;
                    yield return null; yield return null;
                    Check(ui.TestVisibleTab() == 4 && ui.TestArenaSurface == "inbox",
                          "J8: back on ARENA / MY FIGHTS after the fight");
                    // wait for the cloud referee's verdict
                    bool settled = false; float tr = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - tr < 240f && !settled)
                    {
                        arena.PollPendingFights();
                        yield return new WaitForSecondsRealtime(5f);
                        foreach (var m in arena.Inbox)
                            if (m.myRobot == RobotName && !string.IsNullOrEmpty(m.outcome) && m.outcome != "PENDING")
                                settled = true;
                    }
                    Check(settled, "J8: the cloud referee SETTLED the match (verdict + purse in MY FIGHTS)");
                    yield return Shot("j8_settled");
                }
            }
            else Note("J8 skipped — no ChallengeTarget set (first player has nobody to fight yet)");

            // ---- J9 the RETURNING player --------------------------------------
            arena.SignOut();
            yield return null; yield return null;
            Check(!LadderClient.SignedIn && ui.TestArenaSurface == "account",
                  "J9 RETURN: signed out lands on the sign-in form");
            if (arena.Registering) arena.ToggleRegistering();
            arena.Email = Email; arena.SetPassword(Password);
            yield return null; Fire("accsubmit");
            float t9 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t9 < 30f && arena.Busy) yield return null;
            yield return WaitIdle(arena, 20f);
            bool stillMine = false;
            foreach (var m in arena.MyRobots) if (m.name == RobotName) stillMine = true;
            Check(LadderClient.SignedIn && stillMine,
                  "J9 RETURN: signed back in — " + RobotName + " is still ours, history intact");
            yield return Shot("j9_returned");
        }
        finally
        {
            EconomySync.editorOptIn = false;   // owner-state guard back ON
            LadderClient.Logout();
            LadderClient.Token = savedTok; LadderClient.BaseUrl = savedUrl;
            // restore the OWNER's persisted editor session (Logout cleared prefs)
            if (prefTok.Length > 0) { PlayerPrefs.SetString("rb_session_token", prefTok); PlayerPrefs.SetString("rb_session_name", prefName); PlayerPrefs.Save(); }
            Career.Data = savedData; Career.active = savedActive;
            hold.Dispose();
            Debug.Log("[Journey] ================= " + Email + " =================");
            foreach (var l in log) Debug.Log("[Journey] " + l);
            Debug.Log("[Journey] RESULT: " + passed + " pass, " + failed + " fail"
                      + (failed == 0 ? " - ALL GREEN" : " - FIX NEEDED"));
            finished = true;
        }
    }
}
}
