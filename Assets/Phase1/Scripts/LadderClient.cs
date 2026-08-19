// ===========================================================================
// LadderClient — the client half of M2's read side, 2026-08-09.
//
// The ladder has been computed, protected and stored server-side since this
// morning and no part of the GAME could ask for any of it. This is the seam
// that changes that: leaderboard, match, inbox, wallet.
//
// WHY HAND-PARSED. Same reason RobotWorker hand-builds its result JSON:
// JsonUtility cannot round-trip the shapes these endpoints actually return.
// It has no top-level array support, it turns a missing string into "" rather
// than null, and it silently drops fields it does not know. The API's replies
// are flat objects inside one array, which is small enough to read directly
// and specific enough that a general JSON library would be the larger risk.
// (RobotWorker.Field already handles a scalar; what was missing was splitting
// an array of OBJECTS, which is the one thing added here.)
//
// EVERY READ IS ANONYMOUS-CAPABLE. §1.3 makes standings, cards and replays
// public; only the inbox and wallet need a token. Pass one or do not.
// ===========================================================================
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace RobotBrawl.Phase0
{
    public class LadderEntry
    {
        public int rank;
        public string category = "", robotName = "", owner = "", robotId = "", activeSnapshotId = "";
        public float rating, deviation;
        /// <summary>Deviation above the server's threshold — the rank is not
        /// yet earned. Shown, never hidden: a board that hides its own
        /// confidence lies about it.</summary>
        public bool provisional;
    }

    public class InboxEntry
    {
        public string matchId = "", status = "", outcome = "", role = "";
        public string opponent = "", myRobot = "", category = "";
        public int gap;
        /// <summary>replayUrls[0] is the playable recording; the summary is
        /// last. See FightWorkerLoop.</summary>
        public List<string> replayUrls = new List<string>();
    }

    public class ScoutCard
    {
        public string snapshotId = "", status = "", robotName = "", category = "", programHash = "";
        public int massKg;
        public bool hasProgram, mine;
        /// <summary>§M3's "season history on robot cards": past PODIUM places,
        /// public by design — the server has sent `seasonHistory` on every
        /// scouting card since the badges shipped, and this client dropped it
        /// unparsed until 2026-08-12. A card that says "1st in FEATHER at
        /// 1574" two seasons on is what the badges exist FOR.</summary>
        public struct SeasonBadge { public int season, place; public string category; public float rating; }
        public List<SeasonBadge> badges = new List<SeasonBadge>();
        /// <summary>Part IDS only — §1.3 makes the design public and the code
        /// private, so this is the closest a scout gets to the build.</summary>
        public List<string> parts = new List<string>();

        /// <summary>Why the validator refused it. The SERVER decides who may
        /// read these — GET /v1/snapshots/{id} sends them only when `mine` is
        /// true — so a scout simply receives an empty list here and there is
        /// nothing for this client to gate. Do NOT add a client-side check that
        /// implies otherwise, and do not render a placeholder when it is empty:
        /// "REJECTED" with no reason tells the player nothing, which FuzzBench
        /// documents as a known bad state.</summary>
        public List<string> failReasons = new List<string>();
    }

    public class MyRobot
    {
        public string id = "", name = "", activeSnapshotId = "", category = "";
        public bool CanFight { get { return !string.IsNullOrEmpty(activeSnapshotId); } }

        /// <summary>Status of the LATEST snapshot, whatever it is — "PENDING",
        /// "ACTIVE", "REJECTED", "SUPERSEDED" — or "" for a robot that has
        /// never been enlisted.
        ///
        /// ⚠ THIS IS NOT DERIVABLE FROM CanFight, WHICH IS WHY IT EXISTS. The
        /// endpoint used to join ACTIVE only, so a REJECTED robot and a PENDING
        /// one both came back with activeSnapshotId = "" and were byte-
        /// identical here. The dock could only say "waiting to be checked", and
        /// said it forever. No client-side change could have fixed that: it was
        /// a missing COLUMN, not a missing label.</summary>
        public string snapshotStatus = "";

        /// <summary>Why the validator refused this build, in ITS order. Owner-
        /// only by construction: GET /v1/robots filters on the caller's user id,
        /// so every row here is your own robot. Never render these for anyone
        /// else — handing a scout the validator's reasons is handing them the
        /// build, which is why GET /v1/snapshots/{id} guards them behind
        /// `mine`.</summary>
        public List<string> failReasons = new List<string>();

        /// <summary>Rejected AND we know why. Kept as one place so the two
        /// renderers cannot disagree about what counts as rejected.</summary>
        public bool Rejected { get { return snapshotStatus == "REJECTED"; } }

        /// <summary>Where this robot stands, in one line, for the MY ROBOTS row.
        /// Lives on the MODEL because there are two renderers — the dock's UGUI
        /// list and ArenaScreen's standalone OnGUI — and every other ARENA
        /// surface shares one model for exactly this reason. Two copies of this
        /// sentence would drift, and the half nobody looks at would be the one
        /// that goes wrong.
        ///
        /// ONE line and the FIRST reason only: these rows are a single touch
        /// row high, and the validator emits full prose. The whole list has a
        /// home — the scouting card receives failReasons for the owner already.
        ///
        /// ⚠ An empty reasons list still renders as plain "not accepted" rather
        /// than a dangling dash: FuzzBench records "REJECTED with an empty
        /// fail_reasons list tells the player nothing" as a known bad state, and
        /// inventing a placeholder would imply a reason the server never
        /// sent.</summary>
        public string StatusText
        {
            get
            {
                if (CanFight) return category;
                if (Rejected)
                    return failReasons.Count > 0 && !string.IsNullOrEmpty(failReasons[0])
                         ? "not accepted — " + failReasons[0]
                         : "not accepted";
                // No status at all means it has never been uploaded; PENDING and
                // SUPERSEDED both mean the worker has yet to speak for the
                // build that matters. Neither is a rejection and neither may
                // read like one.
                if (string.IsNullOrEmpty(snapshotStatus)) return "not enlisted yet";
                return "waiting to be checked";
            }
        }
    }

    /// <summary>A shop item (§2.3's scrap sinks). Cosmetic only, by design:
    /// nothing here touches a fight.</summary>
    public class Cosmetic
    {
        public string id = "", kind = "", name = "";
        public int price;
        public bool owned;
    }

    public static class LadderClient
    {
        // ===================================================================
        // WHICH SERVER, and this was a LAUNCH BLOCKER until 2026-08-10.
        //
        // This field was `= "http://localhost:5000"` (see the port note below;
        // that number was wrong too) and NOTHING in the game
        // ever assigned it. The production URL lived in the deploy scripts and
        // five documents and in ZERO lines of game code, so a shipped iOS
        // build would have reached for localhost ON THE PHONE, found nothing,
        // and rendered an empty ladder — which looks exactly like a ladder
        // nobody has joined. It is also http://, which iOS App Transport
        // Security blocks outright.
        //
        // Every bench points at localhost, which is precisely why no bench
        // caught it: the whole suite agreed with the bug.
        //
        // ⚠ THE EDITOR DEFAULTS TO LOCAL AND A BUILD DEFAULTS TO PRODUCTION,
        // and that asymmetry is deliberate and load-bearing. EnlistLiveBench
        // REGISTERS ACCOUNTS, uploads robots and starts matches; ArenaShots
        // registers accounts to photograph a populated shelf. If the editor
        // defaulted to production, running the bench suite would write junk
        // accounts and junk ladder rows into the live database — and it would
        // do it silently, because everything would pass. A default that is
        // safe in the editor and correct in a build is worth the asymmetry.
        //
        // To aim the editor at production deliberately:
        //     LadderClient.BaseUrl = LadderClient.PRODUCTION;
        // The setter is the override; it is not a secret and not a toggle a
        // player can reach.
        // ⚠ PORT 5099, NOT 5000 — and the number is the whole finding, 2026-08-10.
        //
        // macOS ships AirPlay Receiver LISTENING ON PORT 5000, on loopback,
        // enabled by default. Measured on owen's Mac:
        //     curl -i http://localhost:5000/   ->  HTTP/1.1 403 Forbidden
        //                                          Server: AirTunes/960.13.1
        //     bind(127.0.0.1:5000)             ->  EADDRINUSE
        // So on a stock Mac the dev API cannot even BIND 5000, and every
        // UnityWebRequest to it either fails or is answered by AirTunes.
        //
        // WHY THAT MATTERED MORE THAN A WRONG NUMBER USUALLY DOES. Every live
        // bench treats "no server" as a SKIP, on the LadderLiveBench argument
        // that a red for a missing Postgres teaches people to ignore reds. With
        // LOCAL_DEV pointing at a port nothing could ever serve, EnlistLiveBench
        // reported **0 passed / 0 failed / 1 skipped** — zero failures, zero
        // coverage — and a suite summary that says "failed 0" reads green. That
        // is how a board that never refetches survived a fully green run.
        // (Report() has always appended "NOTHING RAN — this is not a pass"; the
        // line was true, present, and read by nobody.)
        //
        // 5099 is verified free and is now the ONE local-dev port: the API's
        // launch line, server/tests/run_local.sh and server/tests/api_smoke.sh
        // all say 5099 too. Two numbers that have to agree, kept in two places,
        // is the shape of this bug — so there is only one.
        public const string PRODUCTION = "https://rb-api-902243335343.us-central1.run.app";
        public const string LOCAL_DEV  = "http://localhost:5099";
        /// <summary>The CLOUD dev environment (2026-08-14, owen: "connect all
        /// builds to the dev server"): rb-api-dev on Cloud Run, its own
        /// database (rb_dev), blob bucket (-dev) and worker-key/JWT secrets —
        /// prod data is unreachable from it by construction. Exists because a
        /// REAL DEVICE cannot reach the Mac's localhost, and end-to-end device
        /// testing needs a server that is not production. Fights are refereed
        /// by rb-worker-dev on a 5-minute tick (run it by hand for instant).</summary>
        public const string CLOUD_DEV  = "https://rb-api-dev-902243335343.us-central1.run.app";

        static string _baseUrl;
        public static string BaseUrl
        {
            get { return string.IsNullOrEmpty(_baseUrl) ? DefaultBaseUrl : _baseUrl; }
            set { _baseUrl = value; }
        }

        /// <summary>Where an un-configured client points. See the note above
        /// for why these two differ.</summary>
        public static string DefaultBaseUrl
        {
            get
            {
#if UNITY_EDITOR
                // CLOUD_DEV since 2026-08-14 (owen's call) — the editor, the
                // sims and real devices all exercise ONE shared dev ladder.
                // Benches that need the Mac-local API set BaseUrl = LOCAL_DEV
                // themselves (run_local.sh still serves 5099).
                return CLOUD_DEV;
#elif RB_DEV_SERVER
                // A DEV-POINTED PLAYER (BuildIOSSim.BuildDevPointed): a real
                // il2cpp build that talks to the cloud dev ladder — how a
                // simulator OR a real device tests past the login gate
                // without touching production. The define is set by that
                // build entry alone and restored in its finally; a TestFlight
                // build can never carry it silently because the gate's status
                // line prints the URL whenever it is not production.
                return CLOUD_DEV;
#else
                return PRODUCTION;
#endif
            }
        }

        /// <summary>True when this client is talking to the live ladder.
        /// Surfaced so a bench can REFUSE to write to production rather than
        /// trust that somebody remembered.</summary>
        public static bool IsProduction
        {
            get { return BaseUrl.TrimEnd('/') == PRODUCTION.TrimEnd('/'); }
        }

        public static string Token = "";          // empty = anonymous

        public static string LastError = "";

        /// <summary>Set true when a token-bearing request comes back 401 — the
        /// stored session expired or was revoked server-side. RestoreSession has
        /// promised since it was written that a dead token "surfaces as a 401 on
        /// the first online call, which signs the player out" — Send() below is
        /// the code that finally keeps that promise, and this is the flag the UI
        /// reads to say WHY the sign-in form is back instead of MY FIGHTS. It is
        /// the bug a player reported as "the app update wiped my fights": the
        /// records were safe server-side, but the expired session left the dock
        /// looking signed in over an empty board with no way to know to re-auth.
        /// Cleared on a fresh sign-in (Auth) and on any Logout.</summary>
        public static bool SessionExpired = false;

        /// <summary>The single send point for EVERY request. When a call we made
        /// WITH a token comes back 401, the token is dead: retire the session
        /// (so it is never reused) and raise SessionExpired. One choke point
        /// turns "the server rejected our token" into "sign in again" across all
        /// nineteen call sites, instead of each one failing to an empty screen.
        /// A 401 with no token is just an anonymous call hitting an authed route
        /// and must NOT sign anyone out — hence the Token guard.</summary>
        // UnityWebRequest defaults to timeout 0 = INFINITE. On mobile a captive
        // portal or a half-open connection then black-holes the request forever;
        // every ARENA/gate op sets busy=true before the call and clears it only
        // after, so the coroutine never resumes, busy latches, and the whole
        // screen freezes with every button dead until the app is killed. One
        // ceiling here covers all 19 call sites. A timeout surfaces as a normal
        // Result != Success, which every caller already turns into an error line.
        // Found by the UX validation round, 2026-08-15.
        const int REQ_TIMEOUT_S = 30;

        static IEnumerator Send(UnityWebRequest req)
        {
            if (req != null && req.timeout <= 0) req.timeout = REQ_TIMEOUT_S;
            yield return req.SendWebRequest();
            if (req != null && req.responseCode == 401 && !string.IsNullOrEmpty(Token))
            { Logout(); SessionExpired = true; }
        }

        static UnityWebRequest Get(string path)
        {
            var req = UnityWebRequest.Get(BaseUrl.TrimEnd('/') + path);
            if (!string.IsNullOrEmpty(Token)) req.SetRequestHeader("Authorization", "Bearer " + Token);
            return req;
        }

        /// <summary>The board's season identity, set by every successful
        /// Leaderboard() fetch. 0 until the first board arrives. EndsAt is the
        /// server's ISO timestamp, "" while season 1's clock has not been
        /// started by the rollover scheduler's first tick.</summary>
        public static int BoardSeason;
        public static string BoardSeasonEndsAt = "";

        /// <summary>"season 2 · ends in 6d", or "" before any board has been
        /// fetched. One producer for the two board renderers (OnGUI + dock),
        /// so their headers cannot disagree.</summary>
        public static string SeasonLabel()
        {
            if (BoardSeason <= 0) return "";
            string s = "season " + BoardSeason;
            System.DateTime end;
            // RoundtripKind ALONE — combining it with AdjustToUniversal is an
            // ArgumentException by contract, thrown on the first real date
            // this ever parsed (caught by LadderClientBench, not a player).
            // ToUniversalTime() normalises the offset forms ("+00:00" parses
            // as Local-adjusted) and is the identity for a trailing Z.
            if (System.DateTime.TryParse(BoardSeasonEndsAt, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out end))
            {
                double d = (end.ToUniversalTime() - System.DateTime.UtcNow).TotalDays;
                // The pre-rollover scaffolding (003_ratings) ends season 1 at
                // a 2099 SENTINEL, and "ends in 4500d" is a bug report waiting
                // to be filed. Anything over a year is treated as "no clock".
                if (d <= 1.0) s += " · ends today";
                else if (d <= 366.0) s += " · ends in " + (int)System.Math.Ceiling(d) + "d";
            }
            return s;
        }

        public static IEnumerator Leaderboard(string category, Action<List<LadderEntry>, string> done)
        {
            string path = "/v1/leaderboard" + (string.IsNullOrEmpty(category) ? "" : "/" + category);
            using (var req = Get(path))
            {
                yield return Send(req);
                if (req.result != UnityWebRequest.Result.Success)
                { LastError = req.error; done(null, req.error); yield break; }

                // Top-level fields, read off the whole body: "season" is an
                // exact quoted-key probe, so it cannot half-match
                // "seasonEndsAt", and no entry object carries either key.
                int.TryParse(RobotWorker.Field(req.downloadHandler.text, "season"), out BoardSeason);
                BoardSeasonEndsAt = RobotWorker.Field(req.downloadHandler.text, "seasonEndsAt") ?? "";

                var rows = new List<LadderEntry>();
                foreach (string obj in Objects(req.downloadHandler.text, "entries"))
                {
                    var e = new LadderEntry();
                    int.TryParse(RobotWorker.Field(obj, "rank"), out e.rank);
                    float.TryParse(RobotWorker.Field(obj, "rating"),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out e.rating);
                    float.TryParse(RobotWorker.Field(obj, "deviation"),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out e.deviation);
                    e.category = RobotWorker.Field(obj, "category") ?? "";
                    e.robotName = RobotWorker.Field(obj, "robotName") ?? "";
                    e.owner = RobotWorker.Field(obj, "owner") ?? "";
                    e.robotId = RobotWorker.Field(obj, "robotId") ?? "";
                    e.activeSnapshotId = RobotWorker.Field(obj, "activeSnapshotId") ?? "";
                    e.provisional = string.Equals(RobotWorker.Field(obj, "provisional"), "true",
                                                  StringComparison.OrdinalIgnoreCase);
                    rows.Add(e);
                }
                done(rows, null);
            }
        }

        public static IEnumerator Inbox(Action<List<InboxEntry>, string> done)
        {
            using (var req = Get("/v1/inbox"))
            {
                yield return Send(req);
                if (req.result != UnityWebRequest.Result.Success)
                { LastError = req.error; done(null, req.error); yield break; }

                var rows = new List<InboxEntry>();
                foreach (string obj in Objects(req.downloadHandler.text, "matches"))
                {
                    var m = new InboxEntry();
                    m.matchId = RobotWorker.Field(obj, "matchId") ?? "";
                    m.status = RobotWorker.Field(obj, "status") ?? "";
                    m.outcome = RobotWorker.Field(obj, "outcome") ?? "";
                    m.role = RobotWorker.Field(obj, "role") ?? "";
                    m.opponent = RobotWorker.Field(obj, "opponent") ?? "";
                    m.myRobot = RobotWorker.Field(obj, "myRobot") ?? "";
                    m.category = RobotWorker.Field(obj, "category") ?? "";
                    int.TryParse(RobotWorker.Field(obj, "gap"), out m.gap);
                    m.replayUrls.AddRange(StringArray(obj, "replayUrls"));
                    rows.Add(m);
                }
                done(rows, null);
            }
        }

        /// <summary>§1.3's scouting card: design public, code private. Mass,
        /// size, category and the part manifest are here; the program and the
        /// payload url are not, for anyone.</summary>
        public static IEnumerator ScoutCard(string snapshotId, Action<ScoutCard, string> done)
        {
            using (var req = Get("/v1/snapshots/" + snapshotId))
            {
                yield return Send(req);
                if (req.result != UnityWebRequest.Result.Success)
                { LastError = req.error; done(null, req.error); yield break; }
                string j = req.downloadHandler.text;
                var card = new ScoutCard
                {
                    snapshotId = RobotWorker.Field(j, "id") ?? "",
                    status = RobotWorker.Field(j, "status") ?? "",
                    robotName = RobotWorker.Field(j, "robotName") ?? "",
                    category = RobotWorker.Field(j, "category") ?? "",
                    programHash = RobotWorker.Field(j, "programHash") ?? "",
                };
                int.TryParse(RobotWorker.Field(j, "massKg"), out card.massKg);
                card.hasProgram = string.Equals(RobotWorker.Field(j, "hasProgram"), "true",
                                                StringComparison.OrdinalIgnoreCase);
                card.mine = string.Equals(RobotWorker.Field(j, "mine"), "true",
                                          StringComparison.OrdinalIgnoreCase);
                card.parts.AddRange(StringArray(j, "partsManifest"));
                // Owner-only at the SERVER: a scout's response simply has none.
                card.failReasons.AddRange(StringArray(j, "failReasons"));
                foreach (string b in Objects(j, "seasonHistory"))
                {
                    var sb = new ScoutCard.SeasonBadge();
                    int.TryParse(RobotWorker.Field(b, "season"), out sb.season);
                    int.TryParse(RobotWorker.Field(b, "place"), out sb.place);
                    sb.category = RobotWorker.Field(b, "category") ?? "";
                    float.TryParse(RobotWorker.Field(b, "rating"),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out sb.rating);
                    card.badges.Add(sb);
                }
                done(card, null);
            }
        }

        public static IEnumerator MyRobots(Action<List<MyRobot>, string> done)
        {
            using (var req = Get("/v1/robots"))
            {
                yield return Send(req);
                if (req.result != UnityWebRequest.Result.Success)
                { LastError = req.error; done(null, req.error); yield break; }
                var rows = new List<MyRobot>();
                // A BARE top-level array, unlike every other endpoint here.
                foreach (string obj in Objects(req.downloadHandler.text, null))
                {
                    var m = new MyRobot
                    {
                        id = RobotWorker.Field(obj, "id") ?? "",
                        name = RobotWorker.Field(obj, "name") ?? "",
                        activeSnapshotId = RobotWorker.Field(obj, "activeSnapshotId") ?? "",
                        category = RobotWorker.Field(obj, "category") ?? "",
                        snapshotStatus = RobotWorker.Field(obj, "snapshotStatus") ?? "",
                    };
                    // Same helper the scouting card uses for partsManifest — a
                    // real JSON array, not a quoted string, so no second parse.
                    m.failReasons.AddRange(StringArray(obj, "failReasons"));
                    rows.Add(m);
                }
                done(rows, null);
            }
        }

        /// <summary>Take a robot off the ladder: its rating rows are deleted,
        /// its ACTIVE snapshot is stood down, and it is flagged retired. Past
        /// matches survive, because each is also the other player's history.
        ///
        /// ⚠ REFUSED (409) WHILE A FIGHT OF ITS OWN IS STILL QUEUED OR RUNNING,
        /// and the server's message says so. That is not a race the client
        /// should retry around: rating settlement UPSERTS, so retiring mid-fight
        /// would have the result recreate the rating and put the robot back on
        /// the board minutes later with nothing to explain it.
        ///
        /// Reversible: re-enlisting under the same name un-retires the robot —
        /// and starts it at a fresh placement rating.
        /// done(err) — null on success, including when it was already retired.</summary>
        public static IEnumerator Retire(string robotId, Action<string> done)
        {
            if (string.IsNullOrEmpty(robotId)) { done("no robot to retire"); yield break; }
            using (var req = PostJson("/v1/robots/" + robotId + "/retire", "{}"))
            {
                yield return Send(req);
                if (req.result != UnityWebRequest.Result.Success)
                {
                    string text = req.downloadHandler != null ? req.downloadHandler.text : "";
                    // The API explains itself — "'x' has 1 fight(s) still to
                    // settle". Show that, not a status code.
                    string why = RobotWorker.Field(text, "error") ?? req.error;
                    LastError = why; done(why); yield break;
                }
                done(null);
            }
        }

        /// <summary>done(matchId, stake, err). A 429 here is the daily ticket
        /// cap (§2.2), not a network problem, and the body says which.</summary>
        public static IEnumerator Challenge(string challengerSnapshotId, string defenderSnapshotId,
                                            Action<string, int, string> done)
        { return Challenge(challengerSnapshotId, defenderSnapshotId,
                           (id, stake, seeds, err) => done(id, stake, err)); }

        /// <summary>The four-arg form carries the server-chosen SEEDS — one
        /// per bout, and since 2026-08-14 that is ONE: the client plays the
        /// fight live with the same seed the worker referees with.</summary>
        public static IEnumerator Challenge(string challengerSnapshotId, string defenderSnapshotId,
                                            Action<string, int, int[], string> done)
        {
            string body = "{\"challengerSnapshotId\":" + RobotWorker.Str(challengerSnapshotId)
                        + ",\"defenderSnapshotId\":" + RobotWorker.Str(defenderSnapshotId) + "}";
            var req = new UnityWebRequest(BaseUrl.TrimEnd('/') + "/v1/challenges", "POST");
            req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            if (!string.IsNullOrEmpty(Token)) req.SetRequestHeader("Authorization", "Bearer " + Token);
            using (req)
            {
                yield return Send(req);
                string text = req.downloadHandler != null ? req.downloadHandler.text : "";
                if (req.result != UnityWebRequest.Result.Success)
                {
                    // The API's own words, not "HTTP 400" — it explains
                    // punching down, an empty wallet and a spent ticket, and
                    // the player deserves to read that.
                    string why = RobotWorker.Field(text, "error") ?? req.error;
                    LastError = why; done(null, 0, null, why); yield break;
                }
                int stake; int.TryParse(RobotWorker.Field(text, "stake"), out stake);
                done(RobotWorker.Field(text, "matchId"), stake,
                     RobotWorker.IntArrayField(text, "seeds"), null);
            }
        }

        /// <summary>The live-fight preview feed. Returns the CALLER'S own
        /// snapshot envelope (build + program — their own robot) and the
        /// OPPONENT'S BUILD ONLY. The opponent's program is secret IP and never
        /// leaves the server (launch audit 2026-08-14); the on-device fight is
        /// an exhibition preview with the opponent on generic AI, while the
        /// cloud referee settles the real match with the real programs.</summary>
        public static IEnumerator MatchEnvelopes(string matchId,
            Action<SnapshotEnvelope, string, string, string> done)
        {
            using (var req = Get("/v1/matches/" + matchId + "/envelopes"))
            {
                yield return Send(req);
                string text = req.downloadHandler != null ? req.downloadHandler.text : "";
                if (req.result != UnityWebRequest.Result.Success)
                { done(null, null, null, RobotWorker.Field(text, "error") ?? req.error); yield break; }
                var you = SnapshotEnvelope.FromJson(RobotWorker.Field(text, "you"));
                string oppName = RobotWorker.Field(text, "opponentName");
                string oppBuild = RobotWorker.Field(text, "opponentBuild");
                if (you == null || string.IsNullOrEmpty(oppBuild))
                { done(null, null, null, "the match feed did not parse"); yield break; }
                done(you, oppName, oppBuild, null);
            }
        }

        // ---------------------------------------------------------------
        // ENLIST — 2026-08-10, and it is the other half of the line below.
        //
        // The comment under this one has said "login UI + ENLIST flow" since
        // the day it was written, and only the login half was ever built.
        // Nothing in Assets/ has ever called POST /v1/robots or
        // POST /v1/snapshots: `RobotSnapshot.Export` was written, benched and
        // never invoked by the product, and MyRobots therefore returned an
        // empty list for every real player, forever.
        //
        // WHAT THAT MEANT, and it is worse than a missing button. ArenaScreen
        // takes its challenger from MyRobots -> activeSnapshotId, so the
        // challenge UI had nothing to select; the board could only ever show
        // robots that arrived by curl. The ladder was live, autonomous,
        // alerted, benched at 198/198 — and unreachable from inside the game.
        // It is the cold-start deadlock of HANDOVER_2026-08-10 §3 one level
        // further out, and it hid for the same reason: every endpoint worked,
        // every bench was green, and the only thing missing was the caller.
        //
        // Uploading is TWO round trips because the server models it as two
        // things: a robot is a durable identity that owns a rating and a name,
        // a snapshot is one immutable build of it. Re-enlisting an existing
        // robot must reuse its id — a second robot row would start a second
        // rating at placement, which is how you launder a bad record into a
        // fresh one.

        /// <summary>done(robotId, err). The durable identity, created once.</summary>
        public static IEnumerator CreateRobot(string name, Action<string, string> done)
        {
            using (var req = PostJson("/v1/robots", "{\"name\":" + RobotWorker.Str(name) + "}"))
            {
                yield return Send(req);
                string text = req.downloadHandler != null ? req.downloadHandler.text : "";
                if (req.result != UnityWebRequest.Result.Success)
                {
                    string why = RobotWorker.Field(text, "error") ?? req.error;
                    LastError = why; done(null, why); yield break;
                }
                done(RobotWorker.Field(text, "id"), null);
            }
        }

        /// <summary>done(snapshotId, err). <paramref name="envelopeJson"/> is
        /// the WHOLE envelope as text, and it travels as a JSON *string* —
        /// the API stores it opaquely and reads exactly three fields out of it
        /// (§5.2). Embedding it as an object instead is the mistake that looks
        /// right: the server would still find sha256 and clientVersion, and
        /// the worker would then be handed a payload that never matched its
        /// own hash.</summary>
        public static IEnumerator UploadSnapshot(string robotId, string envelopeJson,
                                                 Action<string, string> done)
        {
            string body = "{\"robotId\":" + RobotWorker.Str(robotId)
                        + ",\"envelope\":" + RobotWorker.Str(envelopeJson) + "}";
            using (var req = PostJson("/v1/snapshots", body))
            {
                yield return Send(req);
                string text = req.downloadHandler != null ? req.downloadHandler.text : "";
                if (req.result != UnityWebRequest.Result.Success)
                {
                    // The API explains a truncated upload, an oversized build
                    // and a robot that is not yours in its own words.
                    string why = RobotWorker.Field(text, "error") ?? req.error;
                    LastError = why; done(null, why); yield break;
                }
                done(RobotWorker.Field(text, "id"), null);
            }
        }

        /// <summary>Reuse-or-create the robot named <paramref name="robotName"/>
        /// and upload <paramref name="env"/> against it. done(snapshotId, err).
        ///
        /// Takes a FINISHED envelope rather than a BuilderManager on purpose:
        /// which build gets enlisted is a game decision (the saved career
        /// robot, or whatever is on the bench) and this client has no business
        /// knowing about Career. ArenaScreen picks; this uploads.
        ///
        /// The snapshot lands PENDING and a worker decides whether it is
        /// legal — enlisting is not the same as being on the board, and the
        /// caller must say so rather than implying the robot is ranked.</summary>
        public static IEnumerator Enlist(string robotName, SnapshotEnvelope env,
                                         Action<string, string> done)
        {
            string name = (robotName ?? "").Trim();
            if (name.Length < 1 || name.Length > 32)
            { done(null, "a robot name must be 1-32 characters"); yield break; }
            if (env == null) { done(null, "this build could not be exported"); yield break; }

            // Reuse before create. See the note above on laundering a rating.
            string robotId = null; string err = null;
            List<MyRobot> mine = null;
            yield return MyRobots((rows, e) => { mine = rows; err = e; });
            if (err != null) { done(null, err); yield break; }
            if (mine != null)
                foreach (var m in mine)
                    if (string.Equals(m.name, name, StringComparison.OrdinalIgnoreCase))
                    { robotId = m.id; break; }

            if (string.IsNullOrEmpty(robotId))
            {
                yield return CreateRobot(name, (id, e) => { robotId = id; err = e; });
                if (err != null || string.IsNullOrEmpty(robotId))
                { done(null, err ?? "the server created no robot"); yield break; }
            }

            string snapId = null;
            yield return UploadSnapshot(robotId, env.ToJson(), (id, e) => { snapId = id; err = e; });
            if (err != null) { done(null, err); yield break; }
            done(snapId, null);
        }

        // ---------------------------------------------------------------
        // §M1's "client: login UI + ENLIST flow". Until now Token was a field
        // somebody set by hand, so there was no way to make or use an account
        // from inside the game — the ladder was reachable only from a bench or
        // a curl command.
        //
        // Register and Login are the same shape and deliberately kept
        // separate: a player who mistypes an existing email should be told
        // "that email is taken", not silently logged into someone else's
        // account by a helpful upsert.

        static UnityWebRequest PostJson(string path, string body)
        {
            var req = new UnityWebRequest(BaseUrl.TrimEnd('/') + path, "POST");
            req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            if (!string.IsNullOrEmpty(Token)) req.SetRequestHeader("Authorization", "Bearer " + Token);
            return req;
        }

        /// <summary>done(displayName, err). On success the token is stored in
        /// Token, so a caller never handles it — one less place to leak it.</summary>
        public static IEnumerator Register(string email, string password, string displayName,
                                           Action<string, string> done)
        {
            yield return Auth("/v1/auth/register",
                "{\"email\":" + RobotWorker.Str(email)
              + ",\"password\":" + RobotWorker.Str(password)
              + ",\"displayName\":" + RobotWorker.Str(displayName) + "}", done);
        }

        public static IEnumerator Login(string email, string password, Action<string, string> done)
        {
            yield return Auth("/v1/auth/login",
                "{\"email\":" + RobotWorker.Str(email)
              + ",\"password\":" + RobotWorker.Str(password) + "}", done);
        }

        static IEnumerator Auth(string path, string body, Action<string, string> done)
        {
            // Re-authenticating: never carry a stale bearer into the login call,
            // and this attempt is a sign-in, not an expiry — so a wrong-password
            // 401 here is the server's "email or password is wrong" (surfaced by
            // done below), NOT the Send() expiry path. Clearing Token first keeps
            // that 401 out of Send's Token-guarded branch.
            Token = ""; SessionExpired = false;
            using (var req = PostJson(path, body))
            {
                yield return Send(req);
                string text = req.downloadHandler != null ? req.downloadHandler.text : "";
                if (req.result != UnityWebRequest.Result.Success)
                {
                    // The API explains itself — "that email is already
                    // registered", "email or password is wrong". Show that,
                    // not a status code.
                    string why = RobotWorker.Field(text, "error") ?? req.error;
                    LastError = why; done(null, why); yield break;
                }
                string tok = RobotWorker.Field(text, "token");
                if (string.IsNullOrEmpty(tok))
                {
                    // A 200 with no token is a contract break, not a login.
                    // Saying so beats appearing to sign in and then 401ing on
                    // every subsequent call.
                    LastError = "the server accepted the login but returned no token";
                    done(null, LastError); yield break;
                }
                Token = tok;
                done(RobotWorker.Field(text, "displayName") ?? "", null);
            }
        }

        // ---- session persistence (client B, docs/Server_Economy_Design_2026-08-13.md) ----
        // The login GATE calls SaveSession after a human signs in; nothing
        // else does, ON PURPOSE: LadderClient.Auth itself must not persist,
        // or every BENCH login (EnlistLiveBench, ReturningPlayerBench) would
        // write a bench account's token into the editor's PlayerPrefs and the
        // next human play session would silently restore it. PlayerPrefs
        // (NSUserDefaults on iOS) is the prototype store; moving the token to
        // the Keychain is a named pre-launch hardening item, not a surprise.
        const string PREF_TOK = "rb_session_token";
        const string PREF_NAME = "rb_session_name";

        /// <summary>Persist the CURRENT session (call only from the login
        /// gate, after a human signed in).</summary>
        public static void SaveSession(string displayName)
        {
            if (string.IsNullOrEmpty(Token)) return;
            PlayerPrefs.SetString(PREF_TOK, Token);
            PlayerPrefs.SetString(PREF_NAME, displayName ?? "");
            PlayerPrefs.Save();
        }

        /// <summary>Restore a persisted session at boot. True when a token
        /// was found — the gate skips itself and the career plays offline,
        /// which is the whole point of persisting. The token may have
        /// expired server-side; that surfaces as a 401 on the first online
        /// call, which signs the player out rather than failing silently.</summary>
        public static bool RestoreSession(out string displayName)
        {
            displayName = PlayerPrefs.GetString(PREF_NAME, "");
            var tok = PlayerPrefs.GetString(PREF_TOK, "");
            if (string.IsNullOrEmpty(tok)) return false;
            Token = tok;
            return true;
        }

        static void ClearSession()
        {
            PlayerPrefs.DeleteKey(PREF_TOK);
            PlayerPrefs.DeleteKey(PREF_NAME);
            PlayerPrefs.Save();
        }

        /// <summary>Sign out. Clears the token AND the persisted session —
        /// a sign-out that survives a restart is not a sign-out. No server
        /// call: the JWT is stateless, it simply stops being sent.</summary>
        public static void Logout() { Token = ""; SessionExpired = false; ClearSession(); }

        /// <summary>Permanently delete the signed-in account (App Store
        /// 5.1.1(v) — mandatory once accounts gate the app). The server
        /// tombstones: robots retire, snapshots stand down, the email is
        /// redacted; ledger and match history stay, anonymous. On success the
        /// local session is cleared too — a deleted account that stays signed
        /// in is a ghost.</summary>
        public static IEnumerator DeleteAccount(Action<string> done)
        {
            var req = new UnityWebRequest(BaseUrl + "/v1/account", "DELETE");
            byte[] body = System.Text.Encoding.UTF8.GetBytes("{\"confirm\":\"DELETE MY ACCOUNT\"}");
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            if (!string.IsNullOrEmpty(Token)) req.SetRequestHeader("Authorization", "Bearer " + Token);
            using (req)
            {
                yield return Send(req);
                string text = req.downloadHandler != null ? req.downloadHandler.text : "";
                if (req.result != UnityWebRequest.Result.Success)
                { done(RobotWorker.Field(text, "error") ?? req.error); yield break; }
                Logout();
                done(null);
            }
        }

        // ---- economy (client B, docs/Server_Economy_Design_2026-08-13.md) ----

        /// <summary>done(balance, err). The server's wallet balance — the
        /// number the career ADOPTS on sync, because the wallet is
        /// server-truth and the local save is a cache.</summary>
        public static IEnumerator GetWallet(Action<long, string> done)
        {
            using (var req = UnityWebRequest.Get(BaseUrl + "/v1/wallet"))
            {
                if (!string.IsNullOrEmpty(Token)) req.SetRequestHeader("Authorization", "Bearer " + Token);
                yield return Send(req);
                string text = req.downloadHandler != null ? req.downloadHandler.text : "";
                if (req.result != UnityWebRequest.Result.Success)
                { done(0, RobotWorker.Field(text, "error") ?? req.error); yield break; }
                long bal;
                if (!long.TryParse(RobotWorker.Field(text, "balance") ?? "", out bal))
                { done(0, "the wallet answered without a balance"); yield break; }
                done(bal, null);
            }
        }

        /// <summary>Post a first-win purse claim. done(paid, alreadyPaid, err):
        /// a 200 pays, a 409 means the server already paid this contest —
        /// which is SUCCESS for the flusher (the claim is settled either
        /// way); anything else is a real error and the claim stays queued.</summary>
        public static IEnumerator ClaimPurse(CareerClaim cl, Action<int, bool, string> done)
        {
            string body = "{\"kind\":\"purse\",\"contestId\":" + RobotWorker.Str(cl.contestId)
                        + ",\"dealt\":" + cl.dealt.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        + ",\"mult\":" + cl.mult.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}";
            using (var req = PostJson("/v1/economy/claims", body))
            {
                yield return Send(req);
                string text = req.downloadHandler != null ? req.downloadHandler.text : "";
                if (req.responseCode == 409) { done(0, true, null); yield break; }
                if (req.result != UnityWebRequest.Result.Success)
                { done(0, false, RobotWorker.Field(text, "error") ?? req.error); yield break; }
                int paid; int.TryParse(RobotWorker.Field(text, "paid") ?? "0", out paid);
                done(paid, false, null);
            }
        }

        /// <summary>Post a shop purchase. done(settled, refused, err): 200 and
        /// 409 (replay) are both SETTLED; a 400/404 is a REFUSAL the caller
        /// must compensate for; anything else (network) is a transient error
        /// and the purchase stays queued.</summary>
        public static IEnumerator Purchase(CareerPurchase p, Action<bool, string, string> done)
        {
            string body = "{\"op\":" + RobotWorker.Str(p.op)
                        + ",\"partId\":" + RobotWorker.Str(p.partId)
                        + ",\"mat\":" + RobotWorker.Str(p.mat)
                        + ",\"idemKey\":" + RobotWorker.Str(p.idemKey) + "}";
            using (var req = PostJson("/v1/economy/purchase", body))
            {
                yield return Send(req);
                string text = req.downloadHandler != null ? req.downloadHandler.text : "";
                if (req.responseCode == 200 || req.responseCode == 409) { done(true, null, null); yield break; }
                if (req.responseCode == 400 || req.responseCode == 404)
                { done(false, RobotWorker.Field(text, "error") ?? ("HTTP " + req.responseCode), null); yield break; }
                done(false, null, RobotWorker.Field(text, "error") ?? req.error);
            }
        }

        public static bool SignedIn { get { return !string.IsNullOrEmpty(Token); } }

        /// <summary>§2.3's one-way valve: ladder scrap into the career wallet.
        /// idemKey is REQUIRED by the API and that is the point — a dropped
        /// response must be retryable without paying twice. The caller passes
        /// a stable key (not a fresh guid per attempt) or the guarantee is
        /// worthless.</summary>
        public static IEnumerator Deposit(int amount, string idemKey, Action<long, string> done)
        {
            using (var req = PostJson("/v1/wallet/deposit",
                       "{\"amount\":" + amount + ",\"idemKey\":" + RobotWorker.Str(idemKey) + "}"))
            {
                yield return Send(req);
                string text = req.downloadHandler != null ? req.downloadHandler.text : "";
                if (req.result != UnityWebRequest.Result.Success)
                {
                    string why = RobotWorker.Field(text, "error") ?? req.error;
                    LastError = why; done(-1, why); yield break;
                }
                long bal; long.TryParse(RobotWorker.Field(text, "balance"), out bal);
                done(bal, null);
            }
        }

        /// <summary>The shop (§2.3's scrap sinks). Cosmetics only — they are
        /// deliberately balance-free.</summary>
        public static IEnumerator Cosmetics(Action<List<Cosmetic>, string> done)
        {
            using (var req = Get("/v1/cosmetics"))
            {
                yield return Send(req);
                if (req.result != UnityWebRequest.Result.Success)
                { LastError = req.error; done(new List<Cosmetic>(), req.error); yield break; }
                done(ParseCosmetics(req.downloadHandler.text), null);
            }
        }

        public static IEnumerator BuyCosmetic(string id, Action<long, string> done)
        {
            using (var req = PostJson("/v1/cosmetics/" + UnityWebRequest.EscapeURL(id) + "/buy", "{}"))
            {
                yield return Send(req);
                string text = req.downloadHandler != null ? req.downloadHandler.text : "";
                if (req.result != UnityWebRequest.Result.Success)
                { string why = RobotWorker.Field(text, "error") ?? req.error;
                  LastError = why; done(-1, why); yield break; }
                long bal; long.TryParse(RobotWorker.Field(text, "balance"), out bal);
                done(bal, null);
            }
        }

        public static IEnumerator Equip(string robotId, string plateId, string titleId,
                                        Action<bool, string> done)
        {
            // null means "leave alone", "" means "take it off" — so the two
            // cannot be collapsed. RobotWorker.Str renders null as the JSON
            // literal null, which is exactly the distinction the API reads.
            string body = "{\"plateId\":" + (plateId == null ? "null" : RobotWorker.Str(plateId))
                        + ",\"titleId\":" + (titleId == null ? "null" : RobotWorker.Str(titleId)) + "}";
            using (var req = PostJson("/v1/robots/" + robotId + "/equip", body))
            {
                yield return Send(req);
                string text = req.downloadHandler != null ? req.downloadHandler.text : "";
                if (req.result != UnityWebRequest.Result.Success)
                { string why = RobotWorker.Field(text, "error") ?? req.error;
                  LastError = why; done(false, why); yield break; }
                done(true, null);
            }
        }

        public static IEnumerator Wallet(Action<long, string> done)
        {
            using (var req = Get("/v1/wallet"))
            {
                yield return Send(req);
                if (req.result != UnityWebRequest.Result.Success)
                { LastError = req.error; done(0, req.error); yield break; }
                long bal; long.TryParse(RobotWorker.Field(req.downloadHandler.text, "balance"), out bal);
                done(bal, null);
            }
        }

        /// <summary>Download a replay to a local file and hand back the path.
        /// ReplayPlayer.Play takes a PATH, and the recording lives behind a
        /// url — so something has to bridge the two, and it is here rather
        /// than in the player, which should not know the ladder exists.</summary>
        public static IEnumerator FetchReplay(string url, Action<string, string> done)
        {
            if (string.IsNullOrEmpty(url)) { done(null, "no replay url"); yield break; }
            using (var req = UnityWebRequest.Get(url))
            {
                yield return Send(req);
                if (req.result != UnityWebRequest.Result.Success)
                { LastError = req.error; done(null, req.error); yield break; }
                byte[] bytes = req.downloadHandler.data;
                if (bytes == null || bytes.Length == 0) { done(null, "replay was empty"); yield break; }
                string dir = System.IO.Path.Combine(Application.persistentDataPath, "ladder_replays");
                try
                {
                    System.IO.Directory.CreateDirectory(dir);
                    string path = System.IO.Path.Combine(dir, SafeName(url));
                    System.IO.File.WriteAllBytes(path, bytes);
                    done(path, null);
                }
                catch (Exception e) { LastError = e.Message; done(null, e.Message); }
            }
        }

        static string SafeName(string url)
        {
            int slash = url.LastIndexOf('/');
            string name = slash >= 0 && slash + 1 < url.Length ? url.Substring(slash + 1) : "replay.rbr.gz";
            foreach (char c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }

        // ------------------------------------------------------------- json
        /// <summary>Split "key":[ {...}, {...} ] into its object bodies.
        /// Brace-counting rather than a regex, because a nested object inside
        /// an entry (rating deltas, an aabb) makes any regex wrong. Strings
        /// are skipped so a brace inside a robot NAME cannot end an object.</summary>
        /// <summary>key == null means the body IS the array — GET /v1/robots
        /// returns a bare one while everything else wraps it.</summary>
        static List<Cosmetic> ParseCosmetics(string json)
        {
            var rows = new List<Cosmetic>();
            foreach (string obj in Objects(json, "cosmetics"))
            {
                var x = new Cosmetic();
                x.id    = RobotWorker.Field(obj, "id")   ?? "";
                x.kind  = RobotWorker.Field(obj, "kind") ?? "";
                x.name  = RobotWorker.Field(obj, "name") ?? "";
                int.TryParse(RobotWorker.Field(obj, "price"), out x.price);
                x.owned = (RobotWorker.Field(obj, "owned") ?? "") == "true";
                if (!string.IsNullOrEmpty(x.id)) rows.Add(x);
            }
            return rows;
        }

        public static List<string> Objects(string json, string key)
        {
            var outv = new List<string>();
            if (string.IsNullOrEmpty(json)) return outv;
            int k = 0;
            if (key != null)
            {
                k = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
                if (k < 0) return outv;
            }
            int open = json.IndexOf('[', k);
            if (open < 0) return outv;

            int depth = 0, start = -1;
            bool inStr = false, esc = false;
            for (int i = open; i < json.Length; i++)
            {
                char ch = json[i];
                if (inStr)
                {
                    if (esc) esc = false;
                    else if (ch == '\\') esc = true;
                    else if (ch == '"') inStr = false;
                    continue;
                }
                if (ch == '"') { inStr = true; continue; }
                if (ch == '{') { if (depth == 0) start = i; depth++; }
                else if (ch == '}')
                {
                    depth--;
                    if (depth == 0 && start >= 0) { outv.Add(json.Substring(start, i - start + 1)); start = -1; }
                }
                else if (ch == ']' && depth == 0) break;
            }
            return outv;
        }

        /// <summary>"key":["a","b"] -> the strings. Field() stops at the first
        /// delimiter and would return "[\"a".</summary>
        public static List<string> StringArray(string json, string key)
        {
            var outv = new List<string>();
            if (string.IsNullOrEmpty(json)) return outv;
            int k = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (k < 0) return outv;
            int open = json.IndexOf('[', k);
            if (open < 0) return outv;
            int close = open;
            bool inStr = false, esc = false;
            for (int i = open; i < json.Length; i++)
            {
                char ch = json[i];
                if (inStr) { if (esc) esc = false; else if (ch == '\\') esc = true; else if (ch == '"') inStr = false; continue; }
                if (ch == '"') { inStr = true; continue; }
                if (ch == ']') { close = i; break; }
            }
            string body = json.Substring(open + 1, Math.Max(0, close - open - 1));
            int p = 0;
            while (p < body.Length)
            {
                int q1 = body.IndexOf('"', p);
                if (q1 < 0) break;
                var sb = new System.Text.StringBuilder();
                int i2 = q1 + 1;
                while (i2 < body.Length && body[i2] != '"')
                {
                    if (body[i2] == '\\' && i2 + 1 < body.Length) { i2++; sb.Append(body[i2]); }
                    else sb.Append(body[i2]);
                    i2++;
                }
                outv.Add(sb.ToString());
                p = i2 + 1;
            }
            return outv;
        }
    }
}
