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

        // The shop (§2.3's scrap sinks) and the one-way deposit valve.
        bool showShop;
        List<Cosmetic> shop = new List<Cosmetic>();
        Vector2 shopScroll;
        string depositText = "";
        // A STABLE idempotency key per deposit attempt. Regenerating it per
        // retry would defeat the guarantee entirely: the API uses it to make a
        // dropped response safe to retry, so the retry must carry the SAME
        // key. Rolled only after a deposit actually succeeds.
        string depositKey = Guid.NewGuid().ToString();

        void Start() { StartCoroutine(Refresh()); }

        /// <summary>Open the ARENA directly on the shop. The career's own SHOP
        /// tab is the obvious caller — "spend your ladder scrap" should not
        /// require finding a button inside another screen — and it is also how
        /// the surface gets screenshotted without a synthetic click.</summary>
        public void ShowShop()
        {
            showShop = true;
            StartCoroutine(OpenShop());
        }

        /// <summary>The wallet is loaded with the stock, and that is not
        /// incidental. Affordability greys out the BUY buttons, and `balance`
        /// starts at -1 meaning "unknown" — so a shop opened without a wallet
        /// read shows every item disabled with no explanation. Caught by
        /// screenshotting the deep-link path: six items, six dead buttons.</summary>
        IEnumerator OpenShop()
        {
            if (LadderClient.SignedIn)
                yield return LadderClient.Wallet((b, err) => { if (err == null) balance = b; });
            yield return LoadShop();
        }

        /// <summary>Back to the board.</summary>
        public void ShowLadder() { showShop = false; }

        IEnumerator DoAuth()
        {
            busy = true; status = registering ? "creating your account…" : "signing in…";
            string pw = password;
            password = "";                 // out of the field before the request
            Action<string, string> done = (name, err) =>
            {
                if (err != null) { status = err; return; }
                who = string.IsNullOrEmpty(name) ? email : name;
                status = "signed in as " + who;
            };
            if (registering) yield return LadderClient.Register(email, pw, displayName, done);
            else             yield return LadderClient.Login(email, pw, done);
            busy = false;
            if (LadderClient.SignedIn) yield return Refresh();
        }

        IEnumerator DoDeposit()
        {
            int amt;
            if (!int.TryParse(depositText, out amt) || amt <= 0)
            { status = "enter a positive amount of scrap to deposit"; yield break; }
            busy = true; status = "depositing " + amt + "…";
            yield return LadderClient.Deposit(amt, depositKey, (bal, err) =>
            {
                if (err != null) { status = "deposit: " + err; return; }
                balance = bal;
                depositText = "";
                // Only now is a new key correct — the old one is spent.
                depositKey = Guid.NewGuid().ToString();
                status = "deposited " + amt + " scrap to your career; " + bal + " left on the ladder";
            });
            busy = false;
        }

        IEnumerator LoadShop()
        {
            busy = true; status = "loading the shop…";
            yield return LadderClient.Cosmetics((rows, err) =>
            {
                if (err != null) status = "shop: " + err;
                else { shop = rows; status = rows.Count + " items"; }
            });
            busy = false;
        }

        IEnumerator DoBuy(Cosmetic c)
        {
            busy = true; status = "buying " + c.name + "…";
            yield return LadderClient.BuyCosmetic(c.id, (bal, err) =>
            {
                if (err != null) { status = "buy: " + err; return; }
                balance = bal;
                status = "bought " + c.name + "; " + bal + " scrap left";
            });
            busy = false;
            if (string.IsNullOrEmpty(LadderClient.LastError)) yield return LoadShop();
        }

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

            // PENDING, not ranked. A worker decides whether the build is legal
            // and which weight category it lands in, and that is a separate
            // trip through the queue — up to one scheduler period in the
            // cloud. Claiming "you are on the ladder" here would be a lie the
            // player discovers by finding themselves nowhere on the board.
            status = name + " uploaded — a match worker checks it is legal and "
                   + "sets its weight class before it appears on the board." + note;
            busy = false;
            yield return Refresh();
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

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(showShop ? "close shop" : "shop") && !busy)
            { showShop = !showShop; if (showShop) StartCoroutine(LoadShop()); }
            if (LadderClient.SignedIn && GUILayout.Button(showEnlist ? "close enlist" : "enlist"))
                showEnlist = !showEnlist;
            if (LadderClient.SignedIn && GUILayout.Button("sign out"))
            { LadderClient.Logout(); who = ""; inbox.Clear(); mine.Clear(); balance = -1;
              status = "signed out"; }
            GUILayout.EndHorizontal();
            GUILayout.Label(status);

            if (!LadderClient.SignedIn) { DrawSignIn(); GUILayout.EndArea(); GUI.matrix = prev; return; }
            if (showShop) { DrawShop(); GUILayout.EndArea(); GUI.matrix = prev; return; }
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

        void DrawShop()
        {
            GUILayout.Space(4);
            GUILayout.Label("<b>SHOP</b>   " + (balance >= 0 ? balance + " scrap" : ""),
                            new GUIStyle(GUI.skin.label) { richText = true });

            GUILayout.BeginHorizontal();
            GUILayout.Label("deposit", GUILayout.Width(56));
            depositText = GUILayout.TextField(depositText ?? "", GUILayout.Width(64));
            GUI.enabled = !busy;
            if (GUILayout.Button("to career", GUILayout.Width(80))) StartCoroutine(DoDeposit());
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            // Say it out loud. §2.3 makes this one-way on purpose and a player
            // who learns that afterwards has been robbed by the interface.
            GUILayout.Label("scrap moved to your career CANNOT come back.");

            if (balance < 0)
                GUILayout.Label("(wallet not loaded — buying is disabled until it is)");

            GUILayout.Space(4);
            shopScroll = GUILayout.BeginScrollView(shopScroll);
            if (shop.Count == 0) GUILayout.Label(busy ? "loading…" : "nothing for sale.");
            foreach (var c in shop)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(c.name, GUILayout.Width(ROW_NAME));
                GUILayout.Label(c.kind, GUILayout.Width(ROW_OWNER));
                GUILayout.Label(c.price + "", GUILayout.Width(ROW_RATING));
                if (c.owned) GUILayout.Label("owned", GUILayout.Width(ROW_SCOUT + 20));
                else
                {
                    // Affordability is the server's call, but showing an
                    // enabled BUY the wallet cannot cover just invites a
                    // refusal the player has to decode.
                    // balance < 0 means "not read yet", which is NOT the same
                    // as "cannot afford". Disabling on unknown is right, but it
                    // has to be legible or the shop just looks broken.
                    GUI.enabled = !busy && balance >= c.price;
                    if (GUILayout.Button("buy", GUILayout.Width(ROW_SCOUT + 20))) StartCoroutine(DoBuy(c));
                    GUI.enabled = true;
                    if (balance >= 0 && balance < c.price)
                        GUILayout.Label("need " + (c.price - balance), GUILayout.Width(70));
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
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

                if (mine.Count == 0)
                    // NOT the same refusal, and saying the wrong one is how
                    // the missing enlist flow stayed invisible: a player with
                    // no robot at all was told they could not punch down,
                    // which is the one thing that cannot be their problem.
                    GUILayout.Label("you have no robot on the ladder yet — "
                                    + "use ENLIST to send the build on your bench.");
                else if (eligible.Count == 0)
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
