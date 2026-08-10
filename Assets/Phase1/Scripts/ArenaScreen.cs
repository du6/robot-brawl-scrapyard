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

        void Start() { StartCoroutine(Refresh()); }

        IEnumerator Refresh()
        {
            busy = true; status = "loading the ladder…";
            yield return LadderClient.Leaderboard(Cats[catIndex], (rows, err) =>
            {
                if (err != null) { status = "leaderboard: " + err; }
                else { board = rows; status = rows.Count + " ranked"; }
            });
            if (!string.IsNullOrEmpty(LadderClient.Token))
            {
                yield return LadderClient.Inbox((rows, err) => { if (err == null) inbox = rows; });
                yield return LadderClient.MyRobots((rows, err) => { if (err == null) mine = rows; });
                yield return LadderClient.Wallet((b, err) => { if (err == null) balance = b; });
            }
            busy = false;
        }

        /// <summary>Fetch the recording and hand it to the game's own player.
        /// replayUrls[0] is the playable one — the worker puts the summary
        /// last precisely so this can take the first without checking.</summary>
        IEnumerator Watch(InboxEntry m)
        {
            if (m.replayUrls.Count == 0) { status = "that match has no replay"; yield break; }
            busy = true; status = "downloading the replay…";
            string path = null;
            yield return LadderClient.FetchReplay(m.replayUrls[0], (p, err) =>
            {
                if (err != null) status = "replay: " + err; else path = p;
            });
            if (path == null) { busy = false; yield break; }

            var bm = FindFirstObjectByType<BuilderManager>();
            if (bm == null) { status = "no BuilderManager to play into"; busy = false; yield break; }
            if (player != null) { player.Close(); player = null; }
            status = "playing " + m.myRobot + " vs " + m.opponent;
            // attachCamera:true — watching a fight you cannot see is not
            // watching it.
            player = ReplayPlayer.Play(bm, path, 1f, true,
                                       p => { status = "replay finished"; });
            busy = false;
        }

        IEnumerator Scout(LadderEntry e)
        {
            if (string.IsNullOrEmpty(e.activeSnapshotId))
            { status = e.robotName + " has no active snapshot to scout"; yield break; }
            busy = true; status = "scouting " + e.robotName + "…"; card = null; pending = false;
            yield return LadderClient.ScoutCard(e.activeSnapshotId, (c, err) =>
            {
                if (err != null) status = "scout: " + err; else { card = c; status = ""; }
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
            busy = true; status = "challenging " + c.robotName + "…";
            yield return LadderClient.Challenge(m.activeSnapshotId, c.snapshotId, (matchId, stake, err) =>
            {
                // The API's own words. It distinguishes punching down, an
                // empty wallet and a spent daily ticket, and a client that
                // flattens those into "failed" throws that away.
                status = err != null ? err
                       : "challenge accepted — " + stake + " scrap staked, match queued";
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

        void OnGUI()
        {
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
            GUILayout.Label(status);

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

            if (card.mine) GUILayout.Label("this is yours.");
            else if (string.IsNullOrEmpty(LadderClient.Token)) GUILayout.Label("sign in to challenge.");
            else
            {
                var eligible = new List<MyRobot>();
                foreach (var m in mine)
                    if (m.CanFight && System.Array.IndexOf(Order, m.category) >= 0
                        && System.Array.IndexOf(Order, m.category) <= System.Array.IndexOf(Order, card.category))
                        eligible.Add(m);

                if (eligible.Count == 0)
                    // §1.2: you may punch up, never down. Say which it is
                    // rather than greying a button with no explanation.
                    GUILayout.Label("no robot of yours may fight a " + card.category
                                    + " — you can punch up, never down.");
                else
                {
                    myPick = Mathf.Clamp(myPick, 0, eligible.Count - 1);
                    GUILayout.BeginHorizontal();
                    for (int i = 0; i < eligible.Count && i < 4; i++)
                        if (GUILayout.Toggle(myPick == i, eligible[i].name, GUI.skin.button)) myPick = i;
                    GUILayout.EndHorizontal();

                    var me = eligible[myPick];
                    int stake = StakeFor(me, card);
                    if (!pending)
                    {
                        if (GUILayout.Button("challenge for " + stake + " scrap") && !busy) pending = true;
                    }
                    else
                    {
                        GUILayout.Label("stake " + stake + " scrap"
                                        + (balance >= 0 ? " of your " + balance : "")
                                        + " — returned if you win or draw, lost if you do not.");
                        GUILayout.BeginHorizontal();
                        if (GUILayout.Button("confirm") && !busy) StartCoroutine(DoChallenge(me, card));
                        if (GUILayout.Button("cancel")) pending = false;
                        GUILayout.EndHorizontal();
                    }
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

        void OnDestroy() { if (player != null) player.Close(); }
    }
}
