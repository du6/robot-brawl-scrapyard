// ===========================================================================
// ArenaScreen — the first screen that shows the ladder, 2026-08-09.
//
//   RobotBrawl.Phase0.ArenaScreen.Open();     in play mode
//
// M2's ARENA tab is five surfaces: ladder browser, scouting card, challenge
// flow, inbox, replay launcher. This is TWO of them — the board and the
// launcher — on purpose. Everything server-side has been proven by benches
// nobody can look at, and the fastest way to find out what the other three
// should be is to put the first two in front of owen.
//
// ⚠ THIS IS OnGUI, DELIBERATELY. BuilderManager and FightManager already use
// it, so it is not foreign here, but MobileBuilderUI builds the shipped game's
// look from a Canvas in code (~7k lines). Matching that is a real piece of
// work and it should happen once the CONTENT is settled, not before. Treat
// the styling as a placeholder and the data as real.
// ===========================================================================
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RobotBrawl.Phase0
{
    public class ArenaScreen : MonoBehaviour
    {
        public static ArenaScreen Open()
        {
            var found = FindFirstObjectByType<ArenaScreen>();
            if (found != null) return found;
            return new GameObject("arena_screen").AddComponent<ArenaScreen>();
        }

        static readonly string[] Cats = { "", "FEATHER", "LIGHT", "MIDDLE", "HEAVY", "SUPER" };
        int catIndex = 1;
        string status = "";
        // ⚠ ONE STATUS STRING, FOUR SURFACES — so it carries a SCOPE.
        // docs/ARENA_Judged_2026-08-10.md §2.4 records the symptom: the
        // board's "8 ranked" sat above ENLIST A ROBOT, and the fights list
        // opened reading the board's count. Every surface inherited whatever
        // the last one said. A renderer now shows the line only on the
        // surface that WROTE it, and says its own thing otherwise.
        public const int SC_BOARD = 0, SC_CARD = 1, SC_INBOX = 2, SC_ACCOUNT = 3;
        int statusScope = SC_BOARD;
        public int StatusScope { get { return statusScope; } }
        void Say(string s, int scope) { status = s; statusScope = scope; }
        bool busy;
        List<LadderEntry> board = new List<LadderEntry>();
        List<InboxEntry> inbox = new List<InboxEntry>();
        Vector2 scroll, inboxScroll;
        ReplayPlayer player;
        bool showInbox;

        // Scouting + challenge state. `card` is the selected opponent, `mine`
        // the robots that could answer, `pending` the confirm step.
        ScoutCard card;
        List<MyRobot> mine = new List<MyRobot>();
        int myPick;
        bool pending;
        long balance = -1;
        static readonly string[] Order = { "FEATHER", "LIGHT", "MIDDLE", "HEAVY", "SUPER" };

        // Sign-in. The password is never stored anywhere but this field and is
        // cleared the moment it has been sent — a screen that keeps it around
        // is one screenshot away from leaking it.
        string email = "", password = "", displayName = "";
        bool registering;
        string who = "";

        // ENLIST — the step that puts a build on the ladder. Everything else
        // on this screen consumes robots that some earlier step created, and
        // until 2026-08-10 no such step existed anywhere in the game.
        bool showEnlist;
        string enlistName = "";

        // THE SHOP LEFT THIS SCREEN, 2026-08-10 (owen: "consolidate the shops
        // into a single shop tab"). Cosmetics, the wallet and the one-way
        // deposit valve now live on the career SHOP tab beside the parts
        // shelf, because two shops is a navigation bug — and because the valve
        // is only legible where BOTH balances are on screen, which is there
        // and was never here. The currencies stay separate: §2.3 makes the
        // ladder wallet one-way into career scrap on purpose, and merging them
        // would delete that decision rather than implement it.
        //
        // What is gone with it: showShop, LoadShop, OpenShop, DoBuy, DoDeposit
        // and DrawShop. There is now exactly ONE implementation of buying a
        // cosmetic, which is the point — this project calls a one-side-only
        // fix its signature bug.

        void Start() { StartCoroutine(Refresh()); }

        IEnumerator DoAuth()
        {
            busy = true; Say(registering ? "creating your account…" : "signing in…", SC_ACCOUNT);
            string pw = password;
            password = "";                 // out of the field before the request
            Action<string, string> done = (name, err) =>
            {
                if (err != null) { Say(err, SC_ACCOUNT); return; }
                who = string.IsNullOrEmpty(name) ? email : name;
                Say("signed in as " + who, SC_ACCOUNT);
            };
            if (registering) yield return LadderClient.Register(email, pw, displayName, done);
            else             yield return LadderClient.Login(email, pw, done);
            busy = false;
            if (LadderClient.SignedIn) yield return Refresh();
        }

        /// <summary>How many times Refresh() has actually STARTED. A harness
        /// seam (the ProgramCanvas Test* precedent): the dock is supposed to
        /// refetch when a player comes back to the ARENA and to stay quiet when
        /// they flick in and out, and neither half is observable from the
        /// board's CONTENTS — an unchanged board looks identical whether it was
        /// refetched or never asked for. Counting the asks is the only way a
        /// bench can tell "came back and reloaded" from "came back and showed
        /// you a cached screen", which is precisely the defect that survived a
        /// green suite. See ReturningPlayerBench.</summary>
        public int TestRefreshes { get { return refreshes; } }
        int refreshes;

        IEnumerator Refresh()
        {
            refreshes++;
            busy = true; Say("loading the ladder…", SC_BOARD);
            yield return LadderClient.Leaderboard(Cats[catIndex], (rows, err) =>
            {
                if (err != null) { Say("leaderboard: " + err, SC_BOARD); }
                else { board = rows; Say(rows.Count + " ranked", SC_BOARD); }
            });
            if (!string.IsNullOrEmpty(LadderClient.Token))
            {
                yield return LadderClient.Inbox((rows, err) => { if (err == null) inbox = rows; });
                yield return LadderClient.MyRobots((rows, err) => { if (err == null) mine = rows; });
                yield return LadderClient.Wallet((b, err) => { if (err == null) balance = b; });
            }
            busy = false;
        }

        /// <summary>Give the dock back after a WATCH.
        ///
        /// ⚠ EVERY EXIT FROM Watch MUST COME THROUGH HERE, 2026-08-10. The
        /// dock closes BEFORE the replay starts — otherwise the fight plays
        /// behind an opaque panel and the player watches a UI — and the only
        /// thing that can reopen it is this coroutine. The happy path is the
        /// easy half; the ones that matter are the early-outs, because a
        /// download that 404s used to leave the dock shut with the camera on an
        /// empty arena and NO CONTROL ON SCREEN to get out of it. A failed
        /// fetch must cost you a status line, not the game.
        ///
        /// Null-guarded because ArenaScreen.Open() runs this screen with no
        /// dock at all; there the IMGUI never closed anything.</summary>
        void RestoreDock()
        {
            if (MobileBuilderUI.inst != null) MobileBuilderUI.inst.ReopenDockAfterReplay();
        }

        /// <summary>Fetch the recording and hand it to the game's own player.
        /// replayUrls[0] is the playable one — the worker puts the summary
        /// last precisely so this can take the first without checking.</summary>
        IEnumerator Watch(InboxEntry m)
        {
            // ⚠ RESTORE FIRST, THEN SPEAK — the DoEnlist rule, and the same
            // trap. Reopening the dock runs ShowTab, and ShowTab is a writer of
            // this status line; anything said before it is said into a message
            // that is about to be overwritten.
            if (m.replayUrls.Count == 0)
            { RestoreDock(); Say("that match has no replay", SC_INBOX); yield break; }
            busy = true; Say("downloading the replay…", SC_INBOX);
            string path = null, fetchErr = null;
            yield return LadderClient.FetchReplay(m.replayUrls[0], (p, err) =>
            {
                if (err != null) fetchErr = err; else path = p;
            });
            if (path == null)
            { busy = false; RestoreDock(); Say("replay: " + (fetchErr ?? "no file"), SC_INBOX); yield break; }

            var bm = FindFirstObjectByType<BuilderManager>();
            if (bm == null)
            { busy = false; RestoreDock(); Say("no BuilderManager to play into", SC_INBOX); yield break; }
            if (player != null) { player.Close(); player = null; }
            Say("playing " + m.myRobot + " vs " + m.opponent, SC_INBOX);
            // attachCamera:true — watching a fight you cannot see is not
            // watching it.
            // ⚠ THE NATURAL END OWES EXACTLY WHAT STOP OWES, and it used to pay
            // none of it — 2026-08-10. This callback reopened the dock and spoke,
            // and left `player` non-null forever.
            //
            // `Playing` is `player != null`, so after any replay ran to its own
            // end the dock kept rebuilding a STOP REPLAY button for a replay that
            // was over, and the ARENA believed a fight was still on screen.
            // StopReplay() (below) always cleared it, which is exactly why this
            // survived: pressing STOP exercises the path that already worked, and
            // only letting it finish shows the leak.
            //
            // Close() is OWED, not optional. It is the only caller of
            // bm.BackToBuild(), which is the sole thing pairing the
            // EnterMatchArena() that ReplayPlayer.Play() performed — and
            // BuildArena() is NOT idempotent (CLAUDE.md lists the unpaired pair
            // as a trap). It also Destroys the FightCamera, which would otherwise
            // be left driving Camera.main against transforms nothing updates any
            // more. Leaving a replay to end naturally and then starting a career
            // fight would have stacked a second EnterMatchArena on an arena that
            // was never torn down, and nothing in the career path knows a replay
            // was ever open.
            //
            // ORDER MATTERS. `player` is cleared FIRST because RestoreDock()
            // reopens the dock, which runs ShowTab, which rebuilds the inbox off
            // `Playing`; clearing it afterwards would rebuild the STOP button one
            // last time and leave it on screen — the same "two writers, no
            // ordering rule" trap DoEnlist carries two comments about. Then
            // RESTORE, and only THEN speak.
            //
            // ⚠ TEARDOWN GOES THROUGH StopReplay(), NEVER A RAW player.Close().
            // Close() fires bm.BackToBuild() unconditionally and its null guard
            // lives at the CALL SITE, so every existing caller pairs it with
            // clearing `player` (:180, StopReplay, the IMGUI button). A new site
            // that reaches for Close() directly and forgets double-fires
            // BackToBuild against a non-idempotent BuildArena. One owner.
            //
            // StopReplay() clears the status line as it goes, so the "replay
            // finished" line is written AFTER it and after RestoreDock() — the
            // restore-then-speak rule, for the same reason DoEnlist carries it.
            player = ReplayPlayer.Play(bm, path, 1f, true,
                                       p =>
                                       {
                                           StopReplay();
                                           RestoreDock();
                                           Say("replay finished", SC_INBOX);
                                       });
            busy = false;
        }

        /// <summary>The SAVED career robot is what gets enlisted, and that is
        /// deliberate. RobotSnapshot.ExportRaw has said so in its own doc
        /// comment since M0 — "the path the ARENA tab will use when enlisting
        /// a career robot" — and it is the honest choice: the ladder fights
        /// this build for days without the player present, so it must be the
        /// one they committed to, not whatever half-finished thing is on the
        /// bench when they happen to open this screen.
        ///
        /// The program travels WITH it. `program == ""` means AI-driven to the
        /// worker, NOT "missing", so a robot enlisted without one is a legal
        /// snapshot that stands still in the arena and nothing downstream can
        /// tell that from a choice. It is read from the same saved record as
        /// the build and never defaulted.</summary>
        IEnumerator DoEnlist()
        {
            if (!Career.active || Career.Data == null)
            { status = "enlisting sends your SAVED career robot — start a career first"; yield break; }
            int ari = Career.Data.activeRobot;
            var ar = (ari >= 0 && ari < Career.Data.stable.Count) ? Career.Data.stable[ari] : null;
            if (ar == null || string.IsNullOrEmpty(ar.snapshot))
            { status = "no saved robot — SAVE the build first, then enlist it"; yield break; }

            string name = (enlistName ?? "").Trim();
            if (name.Length == 0) name = (ar.name ?? "").Trim();
            if (name.Length == 0) { status = "give your robot a name first"; yield break; }

            busy = true; status = "enlisting " + name + "…";

            SnapshotEnvelope env = null;
            try { env = RobotSnapshot.ExportRaw(name, ar.snapshot, ar.program); }
            catch (Exception e) { env = null; status = "export: " + e.Message; }
            if (env == null) { busy = false; yield break; }

            // Carried to the SUCCESS line rather than set here — a status
            // written before the upload is overwritten by its result, which
            // is precisely how a warning that matters goes unread.
            string note = string.IsNullOrEmpty(ar.program)
                ? "  ⚠ it has no program, so it will stand still — arm one in the PROGRAM tab and enlist again."
                : "";

            string snapId = null, err = null;
            yield return LadderClient.Enlist(name, env, (id, e) => { snapId = id; err = e; });

            if (err != null || string.IsNullOrEmpty(snapId))
            {
                status = "enlist: " + (err ?? "the server stored no snapshot");
                busy = false; yield break;
            }

            // ⚠ REFRESH FIRST, THEN SPEAK. Refresh() ends by setting status to
            // "<n> ranked", so writing the confirmation before it threw the
            // confirmation away: pressing ENLIST showed "8 ranked" and nothing
            // else. Measured by EnlistUiBench, which is the only thing that
            // has ever pressed this button.
            //
            // This is the SECOND time this function lost a message that way —
            // the no-program warning above is carried down here rather than
            // set early for exactly the same reason. A status line with two
            // writers and no ordering rule loses, every time, the message that
            // mattered.
            yield return Refresh();

            // PENDING, not ranked. A worker decides whether the build is legal
            // and which weight category it lands in, and that is a separate
            // trip through the queue — up to one scheduler period in the
            // cloud. Claiming "you are on the ladder" here would be a lie the
            // player discovers by finding themselves nowhere on the board.
            status = name + " uploaded — a match worker checks it is legal and "
                   + "sets its weight class before it appears on the board." + note;
            busy = false;
        }

        IEnumerator Scout(LadderEntry e)
        {
            if (string.IsNullOrEmpty(e.activeSnapshotId))
            { Say(e.robotName + " has no active snapshot to scout", SC_BOARD); yield break; }
            busy = true; Say("scouting " + e.robotName + "…", SC_BOARD); card = null; pending = false;
            yield return LadderClient.ScoutCard(e.activeSnapshotId, (c, err) =>
            {
                if (err != null) Say("scout: " + err, SC_BOARD); else { card = c; Say("", SC_CARD); }
            });
            busy = false;
        }

        /// <summary>The stake the server WILL charge, computed the same way it
        /// does: 50 x (1 + gap). Shown before the button is pressed, because a
        /// confirm that does not say the price is not a confirm.</summary>
        int StakeFor(MyRobot m, ScoutCard c)
        {
            int a = System.Array.IndexOf(Order, m.category);
            int b = System.Array.IndexOf(Order, c.category);
            if (a < 0 || b < 0) return 0;
            return 50 * (1 + Mathf.Max(0, b - a));
        }

        IEnumerator DoChallenge(MyRobot m, ScoutCard c)
        {
            busy = true; Say("challenging " + c.robotName + "…", SC_CARD);
            yield return LadderClient.Challenge(m.activeSnapshotId, c.snapshotId, (matchId, stake, err) =>
            {
                // The API's own words. It distinguishes punching down, an
                // empty wallet and a spent daily ticket, and a client that
                // flattens those into "failed" throws that away.
                Say(err != null ? err
                    : "challenge accepted — " + stake + " scrap staked, match queued", SC_CARD);
                if (err == null) { pending = false; card = null; }
            });
            yield return LadderClient.Wallet((b, e) => { if (e == null) balance = b; });
            busy = false;
        }

        // The first screenshot of this screen was taken at 2532x1170 and the
        // panel was unreadable: OnGUI's default font is a fixed pixel size, so
        // on a retina game view everything renders at about a third the size
        // it does at 720p. Work in a virtual 720-high space and scale the
        // whole GUI up to fit — one matrix, and every rect below is legible on
        // any display.
        const float VirtualH = 720f;

        // The board row's columns. These are the ONLY widths written down —
        // the panel derives from them (see W in OnGUI), so a new column widens
        // the panel instead of quietly clipping the button on the right.
        const int ROW_RANK = 28, ROW_NAME = 120, ROW_OWNER = 60, ROW_CAT = 62,
                  ROW_RATING = 48, ROW_DEV = 80, ROW_SCOUT = 52;
        const int BoardRowW = ROW_RANK + ROW_NAME + ROW_OWNER + ROW_CAT
                            + ROW_RATING + ROW_DEV + ROW_SCOUT;
        // What the row needs BEYOND its columns: 6 inter-control gaps at the
        // skin's 4px margin, the scroll view's vertical scrollbar, the box's
        // own padding, and a little slack so a font that measures wider than
        // expected does not clip.
        const int ROW_CHROME = 6 * 4 + 16 + 12 + 18;

        // ===================================================================
        // THE UGUI SEAM — 2026-08-10, and the port begins here.
        //
        // owen: "redesign the arena UI to be consistent with other tabs."
        // Every other tab is UGUI in the dock; this screen is IMGUI drawn over
        // the top at a fixed rect, which is why it collides with the tab strip
        // and the tutorial banner, ignores the notch, and is invisible to
        // TouchSmoke — none of which is fixable by moving numbers.
        //
        // ⚠ THE PORT REPLACES THE DRAWING AND NOTHING ELSE. Refresh, Scout,
        // DoChallenge, DoEnlist, Watch and DoAuth stay exactly where they are,
        // and the UGUI renderer drives them through the surface below. A
        // second copy of the challenge flow is the "one-side-only fix" this
        // project calls its signature bug, and a ladder that settles stakes
        // is the worst possible place to keep two of them.
        //
        // So this class becomes the MODEL and the OPERATIONS; who draws it is
        // now a caller's problem. Surfaces are ported one at a time and the
        // IMGUI stays live for whatever is not ported yet.
        // ===================================================================

        /// <summary>Stop drawing, keep working. The dock sets this when it
        /// renders the ARENA itself; unset, the IMGUI fallback still draws,
        /// so a surface that has not been ported is never simply missing.</summary>
        public bool SuppressImgui;

        public IReadOnlyList<LadderEntry> Board { get { return board; } }
        public IReadOnlyList<InboxEntry> Inbox { get { return inbox; } }
        public IReadOnlyList<MyRobot> MyRobots { get { return mine; } }
        public ScoutCard Card { get { return card; } }
        public string Status { get { return status; } }
        public bool Busy { get { return busy; } }
        public string CategoryLabel { get { return Cats[catIndex] == "" ? "P4P" : Cats[catIndex]; } }
        public static string[] Categories { get { return Cats; } }
        public int CategoryIndex { get { return catIndex; } }

        /// <summary>Switch the board's weight class and reload it. Ignores a
        /// repeat of the current class rather than spending a request on it.</summary>
        public void SetCategory(int i)
        {
            if (i < 0 || i >= Cats.Length || i == catIndex || busy) return;
            catIndex = i;
            StartCoroutine(Refresh());
        }

        public void RefreshNow() { if (!busy) StartCoroutine(Refresh()); }
        public void ScoutNow(LadderEntry e) { if (!busy) StartCoroutine(Scout(e)); }

        /// <summary>Start a replay. RETURNS WHETHER IT STARTED, and the caller
        /// has to look: the dock closes itself before calling this so the fight
        /// is not played behind a panel, and Watch is what reopens it. Refused
        /// while busy, the coroutine never runs, nothing reopens the dock, and
        /// the player is left staring at a closed dock over a still arena. The
        /// bool is the caller's cue to undo its own half.</summary>
        public bool WatchNow(InboxEntry m)
        {
            if (busy) return false;
            StartCoroutine(Watch(m));
            return true;
        }
        public void CloseCard() { card = null; pending = false; }

        /// <summary>Write the status line from a renderer. The dock owns
        /// surfaces this class does not draw — the section switches, for one —
        /// and a tap there that is refused has to be able to SAY SO on the one
        /// status line, in the right SCOPE, or the refusal is silent.
        ///
        /// The scope is not decorative: §2.4's rule is that the line shows only
        /// on the surface that wrote it. Signed out, the account panel is the
        /// only surface on screen, so a refusal aimed at anything else must be
        /// written as SC_ACCOUNT or nobody will ever read it.</summary>
        public void SetStatus(string s, int scope) { Say(s, scope); }

        // ---- the challenge gate, in ONE place ------------------------------
        // Surface 2 of the port. The eligibility rule and its three DISTINCT
        // refusals are the valuable part of this screen, so they are lifted
        // out of DrawCard and both renderers ask the same question. Copying
        // "you can punch up, never down" into a second painter is how the two
        // drift until one of them tells a player the wrong reason — which has
        // already happened once here, when a player with NO robots was told
        // they could not punch down.
        //
        // Named for BuilderManager.AutonomyBlocker, the same idiom: null means
        // "nothing is stopping you".

        /// <summary>Your robots that may legally answer this card. §1.2: you
        /// may punch UP, never down, so a robot's class index must be ≤ the
        /// card's.</summary>
        public List<MyRobot> EligibleFor(ScoutCard c)
        {
            var eligible = new List<MyRobot>();
            if (c == null) return eligible;
            int b = System.Array.IndexOf(Order, c.category);
            foreach (var m in mine)
            {
                int a = System.Array.IndexOf(Order, m.category);
                if (m.CanFight && a >= 0 && b >= 0 && a <= b) eligible.Add(m);
            }
            return eligible;
        }

        /// <summary>Why this card cannot be challenged, or null if it can.
        /// The three cases are deliberately different sentences: they are
        /// three different problems with three different fixes.</summary>
        public string ChallengeBlocker(ScoutCard c)
        {
            if (c == null) return "no robot is selected.";
            if (c.mine) return "this is yours.";
            if (!LadderClient.SignedIn) return "sign in to challenge.";
            if (mine.Count == 0)
                return "you have no robot on the ladder yet — use ENLIST to send the build on your bench.";
            if (EligibleFor(c).Count == 0)
            {
                // ⚠ WORK OUT WHICH REFUSAL IT ACTUALLY IS. "You can punch up,
                // never down" was returned for BOTH of these, and for one of
                // them it is a sentence about a rule the player did not break:
                // a robot still waiting on a worker is the RIGHT WEIGHT and
                // simply unjudged. The remedies are opposite — one is "pick a
                // heavier opponent", the other is "wait" — so telling a
                // waiting player to punch up sends them to do the one thing
                // that cannot help. Caught by ChallengeGateBench; it is the
                // third time this screen has answered with the wrong reason.
                bool anyReady = false;
                foreach (var m in mine) if (m.CanFight) { anyReady = true; break; }
                if (!anyReady)
                    return "your robot is still being checked — a match worker sets its "
                         + "weight class before it can fight. try again shortly.";
                return "no robot of yours may fight a " + c.category + " — you can punch up, never down.";
            }
            return null;
        }

        public int StakeForPick(ScoutCard c)
        {
            var el = EligibleFor(c);
            if (el.Count == 0) return 0;
            return StakeFor(el[Mathf.Clamp(myPick, 0, el.Count - 1)], c);
        }

        // ---- surface 3: the inbox and the replay launcher -----------------
        /// <summary>Which list the ARENA is showing. The weight-class filters
        /// belong to the BOARD only — they mean nothing against your own
        /// fights, and a filter row that does nothing is worse than absent.</summary>
        public bool ShowInbox { get { return showInbox; } set { showInbox = value; } }

        /// <summary>A replay is on screen. `Watch` attaches the camera, so
        /// while this is true the player is looking at the arena, not the
        /// dock — which is why the launcher must offer a way to stop.</summary>
        public bool Playing { get { return player != null; } }
        public void StopReplay() { if (player != null) { player.Close(); player = null; Say("", SC_INBOX); } }
        public string ReplayLine
        {
            get
            {
                if (player == null) return "";
                return string.Format("replay t={0:0.0}s  frames={1}  coverage={2:0.0}%",
                                     player.time, player.framesApplied, player.Coverage * 100f);
            }
        }

        // ---- surfaces 4 and 5: enlist, and the account -------------------
        public bool ShowEnlistPanel { get { return showEnlist; } set { showEnlist = value; } }
        public string EnlistName { get { return enlistName; } set { enlistName = value; } }
        public void EnlistNow() { if (!busy) StartCoroutine(DoEnlist()); }

        public bool Registering { get { return registering; } }
        public void ToggleRegistering() { registering = !registering; Say("", SC_ACCOUNT); }
        public string Email { get { return email; } set { email = value; } }
        public string DisplayName { get { return displayName; } set { displayName = value; } }
        public string Who { get { return who; } }

        /// <summary>WRITE-ONLY, and that is the point. The password lives in
        /// one field, is cleared the moment it has been sent, and is never
        /// readable from outside — a screen that hands its password back is
        /// one screenshot, one log line or one careless bench away from
        /// leaking it. The UGUI field pushes into this and never reads back.</summary>
        public void SetPassword(string p) { password = p; }
        public bool HasPassword { get { return !string.IsNullOrEmpty(password); } }

        /// <summary>Everything the sign-in button needs to know, without
        /// exposing what was typed: enough to enable itself, and no more.</summary>
        public bool CanSubmitAuth
        {
            get
            {
                return !busy && !string.IsNullOrEmpty(email) && HasPassword
                       && (!registering || !string.IsNullOrEmpty(displayName));
            }
        }
        public void SubmitAuth() { if (CanSubmitAuth) StartCoroutine(DoAuth()); }

        /// <summary>Sign out, and leave NOTHING of the person behind.
        ///
        /// ⚠ THE CREDENTIAL FIELDS ARE PART OF "EVERYTHING" — added 2026-08-10.
        /// This method cleared seven things and missed `email`, so signing out
        /// and handing someone the phone showed them the previous account's
        /// address sitting in the sign-in form: RefreshArenaAccount() rebuilds
        /// the field from `Email` (MobileBuilderUI, the ArenaField call), so
        /// the model surviving sign-out is enough to render it. Found by a QA
        /// pass reading the screen rather than the bench.
        ///
        /// Not persistence — there is no PlayerPrefs and no saved-email path,
        /// so it never survived an app restart. It survived SIGN-OUT, which is
        /// the case that matters: the same shared-device leak the email
        /// greeting was removed for. Clearing six neighbours and not this one
        /// is the shape of an oversight, not a decision.
        ///
        /// `password` is belt-and-braces: DoAuth already zeroes it before the
        /// request and the UGUI field is always rebuilt with "", so this is not
        /// the line that protects it. It costs nothing and means the invariant
        /// holds here too rather than only at the two places that happen to
        /// enforce it today.</summary>
        public void SignOut()
        {
            LadderClient.Logout(); who = ""; inbox.Clear(); mine.Clear(); balance = -1;
            card = null; pending = false; showInbox = false;
            email = ""; password = ""; displayName = "";
            Say("signed out", SC_ACCOUNT);
        }

        public bool Pending { get { return pending; } }
        public long Balance { get { return balance; } }
        public int Pick { get { return myPick; } }
        public void SetPick(int i) { myPick = Mathf.Max(0, i); }
        public void ArmChallenge() { if (!busy) pending = true; }
        public void CancelChallenge() { pending = false; }

        /// <summary>Confirm. Refuses rather than throws if the gate has moved
        /// under it — the board reloads while this panel is open, and a pick
        /// that was legal a second ago may not be.</summary>
        public void ConfirmChallenge()
        {
            if (busy || card == null) return;
            if (ChallengeBlocker(card) != null) { pending = false; return; }
            var el = EligibleFor(card);
            StartCoroutine(DoChallenge(el[Mathf.Clamp(myPick, 0, el.Count - 1)], card));
        }

        void OnGUI()
        {
            if (SuppressImgui) return;
            float scale = Screen.height / VirtualH;
            var prev = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity,
                                       new Vector3(scale, scale, 1f));
            float vw = Screen.width / scale, vh = VirtualH;

            // The panel is DERIVED from the widest row it has to hold, not
            // picked. It was 460 against a board row of 450 + spacing, which
            // overflowed by ~40: the "scout" button rendered as "sc" and the
            // scroll view grew a horizontal scrollbar that sat across the
            // BUILD tab. Two numbers that have to agree, kept in two places,
            // drifted the moment a column was added — so now only one of them
            // is written down. See ROW_* and BoardRowW.
            const int W = BoardRowW + ROW_CHROME;
            // Down the LEFT, below the game's own top HUD — the first version
            // sat at y=12 and covered the build bar's status line.
            const int TOP = 64;
            GUILayout.BeginArea(new Rect(10, TOP, W, vh - TOP - 90), GUI.skin.box);

            GUILayout.Label("<b>ARENA</b>   " + LadderClient.BaseUrl
                            + (balance >= 0 ? "   ·   " + balance + " scrap" : ""),
                            new GUIStyle(GUI.skin.label) { richText = true });

            // Two rows of three: six categories on one row clipped "SUPER" to
            // "SUPE" at this width, which is exactly the kind of thing only a
            // screenshot tells you.
            for (int row = 0; row < 2; row++)
            {
                GUILayout.BeginHorizontal();
                for (int i = row * 3; i < Mathf.Min(row * 3 + 3, Cats.Length); i++)
                {
                    string label = Cats[i] == "" ? "P4P" : Cats[i];
                    bool on = GUILayout.Toggle(catIndex == i, label, GUI.skin.button);
                    if (on && catIndex != i && !busy) { catIndex = i; StartCoroutine(Refresh()); }
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(showInbox ? "show ladder" : "show my fights")) showInbox = !showInbox;
            if (GUILayout.Button("refresh") && !busy) StartCoroutine(Refresh());
            if (player != null && GUILayout.Button("stop replay")) { player.Close(); player = null; }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (LadderClient.SignedIn && GUILayout.Button(showEnlist ? "close enlist" : "enlist"))
                showEnlist = !showEnlist;
            if (LadderClient.SignedIn && GUILayout.Button("sign out"))
            { LadderClient.Logout(); who = ""; inbox.Clear(); mine.Clear(); balance = -1;
              status = "signed out"; }
            GUILayout.EndHorizontal();
            GUILayout.Label(status);

            if (!LadderClient.SignedIn) { DrawSignIn(); GUILayout.EndArea(); GUI.matrix = prev; return; }
            if (showEnlist) { DrawEnlist(); GUILayout.EndArea(); GUI.matrix = prev; return; }

            if (!showInbox) DrawBoard(); else DrawInbox();
            if (card != null) DrawCard();

            if (player != null)
            {
                GUILayout.Label(string.Format("replay t={0:0.0}s  frames={1}  coverage={2:0.0}%",
                                player.time, player.framesApplied, player.Coverage * 100f));
            }
            GUILayout.EndArea();
            GUI.matrix = prev;
        }

        /// <summary>§M1's login/ENLIST flow. Without this the ladder was
        /// reachable only by setting LadderClient.Token from a bench or a curl
        /// command — there was no way to make or use an account from inside
        /// the game at all.</summary>
        void DrawSignIn()
        {
            GUILayout.Space(6);
            GUILayout.Label(registering ? "<b>CREATE AN ACCOUNT</b>" : "<b>SIGN IN</b>",
                            new GUIStyle(GUI.skin.label) { richText = true });

            GUILayout.BeginHorizontal();
            GUILayout.Label("email", GUILayout.Width(70));
            email = GUILayout.TextField(email ?? "", GUILayout.Width(ROW_NAME + ROW_OWNER + ROW_CAT));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("password", GUILayout.Width(70));
            // PasswordField, not TextField: this screen gets screenshotted.
            password = GUILayout.PasswordField(password ?? "", '*',
                                               GUILayout.Width(ROW_NAME + ROW_OWNER + ROW_CAT));
            GUILayout.EndHorizontal();

            if (registering)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("name", GUILayout.Width(70));
                displayName = GUILayout.TextField(displayName ?? "",
                                                  GUILayout.Width(ROW_NAME + ROW_OWNER + ROW_CAT));
                GUILayout.EndHorizontal();
            }

            GUILayout.BeginHorizontal();
            // Disabled rather than hidden while busy: a second tap on a live
            // button would register the account twice.
            GUI.enabled = !busy && email.Length > 0 && password.Length > 0
                          && (!registering || displayName.Length > 0);
            if (GUILayout.Button(registering ? "create account" : "sign in"))
                StartCoroutine(DoAuth());
            GUI.enabled = !busy;
            if (GUILayout.Button(registering ? "I have an account" : "create one"))
            { registering = !registering; status = ""; }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.Label("the board below is public; signing in is what lets you\nenlist a robot, challenge, and spend what you win.");
        }

        /// <summary>§2.3's sinks and the one-way valve, together — they are the
        /// only two things a player can do with scrap.</summary>
        /// <summary>The enlist surface. It states what is being sent and what
        /// happens next, because both were previously unknowable: there was no
        /// way to enlist at all, and the challenge panel's refusal for a player
        /// with no robot claimed they were trying to punch down.</summary>
        void DrawEnlist()
        {
            GUILayout.Space(6);
            GUILayout.Label("<b>ENLIST A ROBOT</b>",
                            new GUIStyle(GUI.skin.label) { richText = true });

            bool haveCareer = Career.active && Career.Data != null;
            CareerRobot ar = null;
            if (haveCareer)
            {
                int ari = Career.Data.activeRobot;
                if (ari >= 0 && ari < Career.Data.stable.Count) ar = Career.Data.stable[ari];
            }

            if (ar == null || string.IsNullOrEmpty(ar.snapshot))
            {
                GUILayout.Label("enlisting sends your SAVED career robot to the ladder,\n"
                              + "where it fights while you are away.\n\n"
                              + "there is no saved robot yet — build one and SAVE it,\n"
                              + "then come back here.");
                if (GUILayout.Button("close")) showEnlist = false;
                return;
            }

            if (string.IsNullOrEmpty(enlistName)) enlistName = ar.name ?? "";

            GUILayout.Label("sending: <b>" + (ar.name ?? "(unnamed)") + "</b>"
                          + (string.IsNullOrEmpty(ar.program)
                             ? "  — <color=#c88>no program armed</color>"
                             : "  — program armed"),
                            new GUIStyle(GUI.skin.label) { richText = true });

            GUILayout.BeginHorizontal();
            GUILayout.Label("ladder name", GUILayout.Width(90));
            enlistName = GUILayout.TextField(enlistName ?? "", 32);
            GUILayout.EndHorizontal();
            GUILayout.Label("re-enlisting under a name you already use replaces that\n"
                          + "robot's build and keeps its rating. a new name starts at\n"
                          + "placement.");

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("enlist") && !busy) StartCoroutine(DoEnlist());
            if (GUILayout.Button("close")) showEnlist = false;
            GUILayout.EndHorizontal();

            if (mine.Count > 0)
            {
                GUILayout.Space(6);
                GUILayout.Label("already on the ladder:");
                foreach (var m in mine)
                    GUILayout.Label("  " + m.name
                        + (m.CanFight ? "  " + m.category : "  waiting to be checked"));
            }
        }

        void DrawBoard()
        {
            scroll = GUILayout.BeginScrollView(scroll);
            if (board.Count == 0) GUILayout.Label("nobody ranked here yet.");
            foreach (var e in board)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(e.rank + ".", GUILayout.Width(ROW_RANK));
                GUILayout.Label(e.robotName, GUILayout.Width(ROW_NAME));
                GUILayout.Label(e.owner, GUILayout.Width(ROW_OWNER));
                GUILayout.Label(e.category, GUILayout.Width(ROW_CAT));
                // The rating and the confidence in it, together. A 1400 at
                // RD 350 has not earned what a 1400 at RD 60 has.
                GUILayout.Label(Mathf.RoundToInt(e.rating).ToString(), GUILayout.Width(ROW_RATING));
                GUILayout.Label(e.provisional ? "provisional" : "±" + Mathf.RoundToInt(e.deviation),
                                GUILayout.Width(ROW_DEV));
                if (!string.IsNullOrEmpty(e.activeSnapshotId)
                    && GUILayout.Button("scout", GUILayout.Width(ROW_SCOUT)) && !busy)
                    StartCoroutine(Scout(e));
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }

        /// <summary>§1.3's scouting card. Everything here is public by design;
        /// the program is not here and neither is the payload url, which is
        /// the whole point of the rule.</summary>
        void DrawCard()
        {
            GUILayout.Space(6);
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("<b>" + card.robotName + "</b>   " + card.category + "   "
                            + card.massKg + " kg",
                            new GUIStyle(GUI.skin.label) { richText = true });
            GUILayout.Label(card.parts.Count + " parts: " + string.Join(", ", card.parts.ToArray()));
            // Whether they have a program, never WHAT it is.
            GUILayout.Label(card.hasProgram ? "has a program (contents private)" : "no program");

            // ONE gate, two renderers. This used to inline the eligibility
            // rule and its three refusals; they now live in ChallengeBlocker /
            // EligibleFor so the UGUI card asks the same question rather than
            // carrying a second copy that can drift.
            string blocked = ChallengeBlocker(card);
            if (blocked != null) GUILayout.Label(blocked);
            else
            {
                var eligible = EligibleFor(card);
                myPick = Mathf.Clamp(myPick, 0, eligible.Count - 1);
                GUILayout.BeginHorizontal();
                for (int i = 0; i < eligible.Count && i < 4; i++)
                    if (GUILayout.Toggle(myPick == i, eligible[i].name, GUI.skin.button)) myPick = i;
                GUILayout.EndHorizontal();

                int stake = StakeForPick(card);
                if (!pending)
                {
                    if (GUILayout.Button("challenge for " + stake + " scrap") && !busy) ArmChallenge();
                }
                else
                {
                    GUILayout.Label("stake " + stake + " scrap"
                                    + (balance >= 0 ? " of your " + balance : "")
                                    + " — returned if you win or draw, lost if you do not.");
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("confirm") && !busy) ConfirmChallenge();
                    if (GUILayout.Button("cancel")) CancelChallenge();
                    GUILayout.EndHorizontal();
                }
            }
            if (GUILayout.Button("close card")) { card = null; pending = false; }
            GUILayout.EndVertical();
        }

        void DrawInbox()
        {
            inboxScroll = GUILayout.BeginScrollView(inboxScroll);
            if (string.IsNullOrEmpty(LadderClient.Token))
                GUILayout.Label("set LadderClient.Token to see your fights.");
            else if (inbox.Count == 0)
                GUILayout.Label("no fights yet.");
            foreach (var m in inbox)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(m.outcome, GUILayout.Width(64));
                GUILayout.Label(m.myRobot + " vs " + m.opponent, GUILayout.Width(190));
                GUILayout.Label(m.category + (m.gap > 0 ? " +" + m.gap : ""), GUILayout.Width(70));
                if (m.replayUrls.Count > 0)
                {
                    if (GUILayout.Button("watch", GUILayout.Width(60)) && !busy) StartCoroutine(Watch(m));
                }
                else GUILayout.Label("—", GUILayout.Width(60));
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }

        // ---- harness seams -------------------------------------------------
        // The ProgramCanvas Test* precedent: plain accessors, no reflection.
        // The bridge refuses System.Reflection outright, and a UI path that
        // can only be driven by a human is a UI path nothing ever checks —
        // which is exactly how ENLIST shipped with its network half proven
        // 21/21 and the button itself never once pressed.
        /// <summary>Seed the ladder-robot list. The challenge gate reads it,
        /// and a gate that can only be exercised by first enlisting two robots
        /// against a live server is a gate nothing checks cheaply.</summary>
        public void TestSetMine(List<MyRobot> rows)
        { mine.Clear(); if (rows != null) mine.AddRange(rows); }
        public void TestSetCard(ScoutCard c) { card = c; pending = false; }

        public string TestStatus { get { return status; } }
        public bool TestBusy { get { return busy; } }
        public bool TestShowEnlist { get { return showEnlist; } set { showEnlist = value; } }
        public void TestSetEnlistName(string n) { enlistName = n; }
        public void TestEnlist() { StartCoroutine(DoEnlist()); }
        /// <summary>Draw one frame of the enlist panel off-screen, so a bench
        /// can prove it does not throw against whatever career state it is
        /// handed. DrawEnlist reads Career directly and an OnGUI exception is
        /// silent to everything except the console.</summary>
        public void TestDrawEnlistOnce()
        {
            var prev = GUI.matrix;
            GUILayout.BeginArea(new Rect(-4000, -4000, 400, 800));
            DrawEnlist();
            GUILayout.EndArea();
            GUI.matrix = prev;
        }

        void OnDestroy() { if (player != null) player.Close(); }
    }
}
