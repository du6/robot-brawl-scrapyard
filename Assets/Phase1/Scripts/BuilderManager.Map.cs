// ===========================================================================
// BuilderManager.Map.cs — THE YARD (Robot Brawl: Scrapyard, M0 prototype,
// docs/Scrapyard_Design_2026-09-09.md §3 and §8).
//
// Grown out of StartTest(), not built beside it: the test drive already
// spawned the player's robot under the touch stick with a FollowCamera and
// a target, which is the map's whole drive loop. This file is a partial of
// BuilderManager because everything it needs — `placed`, `driveDir`, `cam`,
// SpawnBot, FloorNet, BackToBuild's sweep — is private state of the one
// object that owns the build, and the point of a fork is not to widen that.
//
// M0 scope, deliberately: an 80 x 80 m plane with a fence, seeded wrecks,
// THREE crates, ONE parked yard bot (SCOUT), DRIVE OUT / GARAGE doors, and
// the `map` / `crate` / `meet` / `challenge` events. Crates are qboxes
// opened where they stand; a challenge is a Quick bout under the
// auto-brain. Zones, the daily cap, real robots from the pool and the
// return-to-map after a fight are M1/M2.
// ===========================================================================
using System.Collections.Generic;
using UnityEngine;

namespace RobotBrawl.Phase0
{
public partial class BuilderManager
{
    public const float YARD_HALF = 40f;      // 80 x 80 m; the arena is 14 x 14
    public const int   YARD_CRATES = 3;      // M0; the design's 8 is M1
    public const float CRATE_REACH = 1.6f;   // drive into it
    public const float CARD_REACH = 4f;      // the encounter card slides up
    public const string YARD_BOT = "scout";  // never the rookie's own build (mirror lock)

    readonly List<GameObject> crates = new List<GameObject>();
    readonly List<int> crateIds = new List<int>();
    CompoundRobot parkedBot;
    Vector3 garageDoor;                      // where you spawn and where GARAGE is
    Vector3 parkedPos;
    bool yardCard;                           // the card is up
    string yardToast = ""; float yardToastT;
    float flippedFor;

    /// <summary>The seam a bench reads: is the encounter card up?</summary>
    public bool YardCardShown { get { return mode == Mode.Map && yardCard; } }
    public int  YardCratesLeft { get { int n = 0; foreach (var c in crates) if (c != null) n++; return n; } }
    public CompoundRobot YardParked { get { return parkedBot; } }
    public Vector3 YardGarageDoor { get { return garageDoor; } }
    /// <summary>Crate positions still standing (a bench reads these to check
    /// the seed and the fence). Opened crates are skipped.</summary>
    public List<Vector3> YardCratePositions()
    {
        var l = new List<Vector3>();
        foreach (var c in crates) if (c != null) l.Add(c.transform.position);
        return l;
    }

    /// <summary>The yard re-rolls at local midnight: the seed is the date.</summary>
    public static int YardSeed()
    {
        var d = System.DateTime.Now;
        return d.Year * 10000 + d.Month * 100 + d.Day;
    }
    static string YardToday() { return System.DateTime.Now.ToString("yyyy-MM-dd"); }

    // ------------------------------------------------------------ the doors
    /// <summary>DRIVE OUT. Legal build required, like every fight.</summary>
    public void EnterMap()
    {
        if (mode == Mode.Fight) return;
        if (mode == Mode.Test) BackToBuild();
        if (mode == Mode.Map) return;
        string err = Validate();
        if (err != null) { message = err; SfxSynth.Deny(); return; }

        ARENA_HALF = YARD_HALF;
        ArenaHazards.Clear();
        TouchControls.Ensure();
        TouchControls.fightActive = true;    // the stick is up; no FIRE on the map
        TouchControls.hasFire = false;

        Deselect();
        SetMatView(false);
        hoverPart = null;
        buildRoot.SetActive(false);
        mode = Mode.Map;
        message = "";
        yardCard = false; yardToast = ""; yardToastT = 0f; flippedFor = 0f;

        BuildYard();

        testRobot = SpawnBot(placed, "PlayerBuild", garageDoor, Quaternion.identity, driveDir, out testDrive);
        if (testRobot == null) { BackToBuild(); message = "the build would not spawn"; return; }
        testRobot.combatEnabled = false;     // nothing on the map fights
        testRobot.controlSource = ControlSource.Keyboard;
        foreach (var act in testRobot.GetComponentsInChildren<Actuator>(true)) act.playerControlled = false;
        combatArmAt = -1f;

        followCam = cam.gameObject.AddComponent<FollowCamera>();
        followCam.target = testRobot.transform;
        followCam.forwardHint = driveDir;
        followCam.distance = 9f; followCam.height = 4.5f;
        followCam.clampHalf = YARD_HALF - 0.8f;
        followCam.SnapNow();
        AddHeadlight(testRobot, driveDir);

        RBTelemetry.Once(RBTelemetry.MAP);
    }

    /// <summary>GARAGE. BackToBuild's sweep already tears down every
    /// CompoundRobot and the sandbox root the yard hangs from.</summary>
    public void LeaveMap()
    {
        if (mode != Mode.Map) return;
        crates.Clear(); crateIds.Clear();
        parkedBot = null; yardCard = false;
        BackToBuild();
        ARENA_HALF = 7f;
    }

    // ------------------------------------------------------------ the yard
    void BuildYard()
    {
        float HALF = YARD_HALF;
        sandboxRoot = new GameObject("yard");
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "yard_floor";
        FloorBoxCollider(floor);
        floor.transform.SetParent(sandboxRoot.transform, false);
        floor.transform.localScale = new Vector3(HALF / 5f, 1f, HALF / 5f);
        floor.GetComponent<Renderer>().sharedMaterial = PartVisualFactory.Mat(new Color(0.30f, 0.29f, 0.27f), 0.1f, 0.25f);

        // the fence: the arena's walls, longer
        for (int i = 0; i < 4; i++)
        {
            bool alongX = i < 2; float sign = (i % 2 == 0) ? 1f : -1f;
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "fence_" + i;
            wall.transform.SetParent(sandboxRoot.transform, false);
            wall.transform.position = alongX ? new Vector3(0f, 1.0f, sign * HALF) : new Vector3(sign * HALF, 1.0f, 0f);
            wall.transform.localScale = alongX ? new Vector3(2f * HALF + 0.5f, 2f, 0.5f) : new Vector3(0.5f, 2f, 2f * HALF + 0.5f);
            wall.GetComponent<Renderer>().sharedMaterial = PartVisualFactory.Mat(new Color(0.22f, 0.22f, 0.25f), 0.5f, 0.4f);
            for (int k = 0; k < 2 * (int)HALF / 4; k++)
            {
                float off = -HALF + 2f + k * 4f;
                PartVisualFactory.Deco(PrimitiveType.Cube, sandboxRoot.transform,
                    alongX ? new Vector3(off, 2.1f, sign * HALF) : new Vector3(sign * HALF, 2.1f, off),
                    alongX ? new Vector3(2f, 0.22f, 0.7f) : new Vector3(0.7f, 0.22f, 2f),
                    Vector3.zero, k % 2 == 0 ? PartVisualFactory.HazardYellow : PartVisualFactory.HazardBlack,
                    "fence_hazard_" + i + "_" + k);
            }
        }

        // the garage door: south fence, middle. You spawn just inside it.
        garageDoor = new Vector3(0f, 0f, -HALF + 6f);
        var door = GameObject.CreatePrimitive(PrimitiveType.Cube);
        door.name = "garage_door";
        door.transform.SetParent(sandboxRoot.transform, false);
        door.transform.position = new Vector3(0f, 1.5f, -HALF + 0.3f);
        door.transform.localScale = new Vector3(6f, 3f, 0.8f);
        door.GetComponent<Renderer>().sharedMaterial = PartVisualFactory.CyanGlow;

        // wrecks from the seed: slabs and pillars in the arena's materials,
        // with colliders, kept off the door lane and off every crate.
        var rng = new System.Random(YardSeed());
        var taken = new List<Vector3>();
        taken.Add(garageDoor);
        for (int w = 0; w < 28; w++)
        {
            Vector3 p = new Vector3((float)(rng.NextDouble() * 2 - 1) * (HALF - 4f), 0f,
                                    (float)(rng.NextDouble() * 2 - 1) * (HALF - 4f));
            if (Mathf.Abs(p.x) < 4f && p.z < -HALF + 16f) continue;     // the door lane
            bool pillar = rng.Next(3) == 0;
            var wreck = GameObject.CreatePrimitive(pillar ? PrimitiveType.Cylinder : PrimitiveType.Cube);
            wreck.name = "wreck_" + w;
            wreck.transform.SetParent(sandboxRoot.transform, false);
            float h = pillar ? 1.2f + (float)rng.NextDouble() * 1.5f : 0.4f + (float)rng.NextDouble() * 0.6f;
            Vector3 sc = pillar ? new Vector3(0.6f, h, 0.6f)
                                : new Vector3(1.5f + (float)rng.NextDouble() * 3f, h, 1f + (float)rng.NextDouble() * 2f);
            wreck.transform.position = new Vector3(p.x, sc.y * (pillar ? 1f : 0.5f), p.z);
            wreck.transform.localScale = sc;
            wreck.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            wreck.GetComponent<Renderer>().sharedMaterial =
                PartVisualFactory.Mat(pillar ? new Color(0.45f, 0.42f, 0.38f) : new Color(0.36f, 0.30f, 0.24f), 0.6f, 0.3f);
            taken.Add(p);
        }

        // crates: the first is EIGHT METRES ahead of the door, in view; the
        // rest are seeded, apart from each other and from every wreck.
        crates.Clear(); crateIds.Clear();
        bool today = Career.Data != null && Career.Data.yardDay == YardToday();
        for (int c = 0; c < YARD_CRATES; c++)
        {
            Vector3 p;
            if (c == 0) p = garageDoor + Vector3.forward * 8f;
            else
            {
                int tries = 0;
                do
                {
                    p = new Vector3((float)(rng.NextDouble() * 2 - 1) * (HALF - 6f), 0f,
                                    (float)(rng.NextDouble() * 2 - 1) * (HALF - 6f));
                    tries++;
                } while (tries < 50 && TooClose(p, taken, 4f));
            }
            taken.Add(p);
            crateIds.Add(c);
            if (today && Career.Data.yardOpened.Contains(c)) { crates.Add(null); continue; }
            var crate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            crate.name = "crate_" + c;
            crate.transform.SetParent(sandboxRoot.transform, false);
            crate.transform.position = new Vector3(p.x, 0.35f, p.z);
            crate.transform.localScale = new Vector3(0.7f, 0.7f, 0.7f);
            crate.transform.rotation = Quaternion.Euler(0f, 30f, 0f);
            crate.GetComponent<Renderer>().sharedMaterial = PartVisualFactory.HazardYellow;
            Object.Destroy(crate.GetComponent<Collider>());     // you drive INTO it, not against it
            var glow = PartVisualFactory.Deco(PrimitiveType.Cylinder, crate.transform, new Vector3(0f, 1.6f, 0f),
                new Vector3(0.12f, 1.2f, 0.12f), Vector3.zero, PartVisualFactory.CyanGlow, "crate_beacon_" + c);
            var gc = glow.GetComponent<Collider>(); if (gc != null) Object.Destroy(gc);
            crates.Add(crate);
        }

        // the one parked yard bot: 30 m past the first crate, facing away,
        // and it is SCOUT, not another rookie.
        parkedPos = garageDoor + Vector3.forward * 38f + Vector3.right * 6f;
        parkedPos.z = Mathf.Min(parkedPos.z, HALF - 6f);
        var entry = EnemyRoster.Find(YARD_BOT);
        RaycastWheelDrive pdrv;
        parkedBot = SpawnBot(EnemyRoster.Recipe(entry.id, palette), entry.label, parkedPos,
                             Quaternion.LookRotation(Vector3.forward), Vector3.forward, out pdrv);
        if (parkedBot != null)
        {
            parkedBot.combatEnabled = false;
            parkedBot.controlSource = ControlSource.AI;   // no controller: it idles
        }
    }

    static bool TooClose(Vector3 p, List<Vector3> taken, float d)
    {
        foreach (var t in taken) if ((t - p).sqrMagnitude < d * d) return true;
        return false;
    }

    // ------------------------------------------------------------ per frame
    void UpdateMap()
    {
        FloorNet(testRobot);
        FloorNet(parkedBot);
        if (Phase0Input.BackDown()) { LeaveMap(); return; }
        if (testRobot == null) { LeaveMap(); return; }
        if (yardToastT > 0f) yardToastT -= Time.deltaTime;

        Vector3 me = testRobot.rb.position;

        // crates: drive into one and it opens where it stands
        for (int i = 0; i < crates.Count; i++)
        {
            var c = crates[i];
            if (c == null) continue;
            Vector3 cp = c.transform.position; cp.y = me.y;
            if ((cp - me).sqrMagnitude < CRATE_REACH * CRATE_REACH) { OpenCrate(i); break; }
        }

        // the encounter card
        bool near = parkedBot != null && (parkedBot.rb.position - me).sqrMagnitude < CARD_REACH * CARD_REACH;
        if (near && !yardCard) RBTelemetry.Once(RBTelemetry.MEET);
        yardCard = near;

        // righting: a machine on its back with the stick held for a second
        // flips back onto its wheels - a visible mercy the fight does not offer
        bool flipped = Vector3.Dot(testRobot.transform.up, Vector3.up) < 0.2f;
        if (flipped && Mathf.Abs(Phase0Input.Throttle()) > 0.3f) flippedFor += Time.deltaTime; else flippedFor = 0f;
        if (flippedFor > 1f)
        {
            flippedFor = 0f;
            Vector3 fwd = testRobot.transform.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            testRobot.rb.position = me + Vector3.up * 0.6f;
            testRobot.rb.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);
            VelUtil.SetLinearVelocity(testRobot.rb, Vector3.zero);
            testRobot.rb.angularVelocity = Vector3.zero;
        }
    }

    void OpenCrate(int i)
    {
        var c = crates[i];
        if (c == null) return;
        crates[i] = null;
        Object.Destroy(c);
        if (Career.Data != null)
        {
            if (Career.Data.yardDay != YardToday()) { Career.Data.yardDay = YardToday(); Career.Data.yardOpened.Clear(); }
            if (!Career.Data.yardOpened.Contains(crateIds[i])) Career.Data.yardOpened.Add(crateIds[i]);
        }
        string[] lines;
        string id = Career.QuickBoxRoll(0, out lines);
        Career.QueueReward(id, "CRATE", "found in the yard", lines);   // editor: granted at once; device: the box opens here
        if (Career.autosave) Career.Save();
        yardToast = "CRATE  ·  " + string.Join("  ·  ", lines);
        yardToastT = 3.5f;
        SfxSynth.Place();
        RBTelemetry.Once(RBTelemetry.CRATE);
    }

    // ------------------------------------------------------------ the challenge
    /// <summary>CHALLENGE, from the card. Leaves the yard and starts a Quick
    /// bout against the parked bot under the auto-brain. M0 returns to the
    /// garage after the bell; back-to-the-map is M1.</summary>
    public void ChallengeParked()
    {
        if (mode != Mode.Map || parkedBot == null) return;
        string opp = YARD_BOT;
        LeaveMap();
        StartYardFight(opp);
    }

    /// <summary>StartQuickFight's shape with a NAMED opponent and no manual
    /// fallback: every robot fights itself here (design §3.6).</summary>
    public void StartYardFight(string oppId)
    {
        Career.fightAutonomous = false;
        if (!Career.active || Career.Data == null) return;
        EndScout();
        string err = Validate();
        if (err != null) { message = err; SfxSynth.Deny(); return; }
        var lack = CareerShortfall();
        if (lack.Count > 0) { message = "YOUR BUILD needs " + string.Join(", ", lack.ToArray()); SfxSynth.Deny(); return; }
        {
            int ar = Career.Data.activeRobot;
            if (ar >= 0 && ar < Career.Data.stable.Count && !SaveWouldDraft)
            {
                string snapNow = SnapshotString();
                if (Career.Data.stable[ar].snapshot != snapNow)
                {
                    Career.Data.stable[ar].snapshot = snapNow;
                    if (Career.autosave) Career.Save();
                }
            }
        }
        var entry = EnemyRoster.Find(oppId);
        if (entry == null) return;
        RobotProgram autoProg = null;
        if (Career.Data.activeRobot >= 0 && Career.Data.activeRobot < Career.Data.stable.Count)
        {
            string aTag; string aWhy = AutonomyBlocker(out aTag);
            if (aWhy == null) autoProg = RobotProgram.FromJson(Career.Data.stable[Career.Data.activeRobot].program);
        }
        if (autoProg == null || autoProg.hats.Count == 0) autoProg = BrainPick(placed);

        Career.fightBuildValue = BuildValueCareer();
        var recipe = EnemyRoster.Recipe(entry.id, palette);
        int ov = 0;
        foreach (var p2 in recipe) ov += CareerDB.PartPrice(p2.def.id, p2.MatName());
        Career.fightOppValue = ov;
        Career.activeLeague = null; Career.activeContest = null;
        Career.targetLeagueIdx = Career.FurthestLeague();
        CrowdAudio.SetVenue(Career.targetLeagueIdx);
        opponentId = entry.id;
        opponentTier = entry.tier;
        quickArmourMat = "";
        Progression.activeRungIndex = -1;
        Progression.activeChallengeIdx = -1;
        quickNext = true;
        RBTelemetry.Once(RBTelemetry.CHALLENGE);
        RBTelemetry.Once(RBTelemetry.QUICK);
        StartFight();
        quickNext = false;
        if (mode != Mode.Fight) { Career.quickFight = false; FightManager.quickBout = false; return; }
        if (testRobot != null)
        {
            var afm = Object.FindFirstObjectByType<FightManager>();
            if (afm != null)
            {
                afm.playerSource = ControlSource.Program;
                var apr = testRobot.gameObject.AddComponent<ProgramRunner>();
                apr.Init(testRobot, testDrive);
                apr.program = autoProg;
                Career.fightAutonomous = true;
            }
        }
    }

    /// <summary>THE AUTO-BRAIN (design §3.6): chosen by what the build carries,
    /// and validated against it, so "needs a Wall sensor" never fires here.
    /// A saved program overrides it - that is the garage upgrade.</summary>
    public static RobotProgram BrainPick(List<PlacedPart> build)
    {
        var ids = new List<string>();
        bool compass = false, wall = false;
        foreach (var p in build)
        {
            ids.Add(p.def.id);
            if (p.def.id == "compass") compass = true;
            if (p.def.id == "wallsensor") wall = true;
        }
        RobotProgram pick = compass && wall ? RobotProgram.RamHunter()
                          : compass        ? RobotProgram.Brawler()
                          : wall           ? RobotProgram.WallShy()
                                           : RobotProgram.FirstSteps();
        if (pick.Validate(ids) != null) pick = RobotProgram.FirstSteps();
        if (pick.Validate(ids) != null) pick = RobotProgram.Statue();
        return pick;
    }
    public string BrainPickTitle { get { var p = BrainPick(placed); return p != null ? p.title : ""; } }

    // ------------------------------------------------------------ the HUD
    void MapHud()
    {
        float s = GuiScale;
        Matrix4x4 saved = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float w = Screen.width / s, h = Screen.height / s;
        float top = 8f + Screen.safeArea.y / s;
        int fs = GUI.skin.button.fontSize;
        GUI.skin.button.fontSize = 16;
        bool garage = GUI.Button(new Rect(w - 106f, top, 96f, 40f), "GARAGE");
        GUI.skin.button.fontSize = fs;

        // the compass strip: bearings to the nearest crate and the parked bot
        var st = new GUIStyle(GUI.skin.label); st.fontSize = 15; st.fontStyle = FontStyle.Bold;
        st.normal.textColor = new Color(0.85f, 0.92f, 1f);
        if (testRobot != null)
        {
            Vector3 me = testRobot.rb.position;
            Vector3 fwd = testRobot.transform.TransformDirection(driveDir); fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            var sb = new System.Text.StringBuilder();
            GameObject nearest = null; float best = float.MaxValue;
            foreach (var c in crates) if (c != null) { float d = (c.transform.position - me).sqrMagnitude; if (d < best) { best = d; nearest = c; } }
            if (nearest != null) sb.Append(Bearing("CRATE", nearest.transform.position - me, fwd));
            else sb.Append("no crates left today");
            if (parkedBot != null) sb.Append("     ").Append(Bearing(EnemyRoster.Find(YARD_BOT).label, parkedBot.rb.position - me, fwd));
            sb.Append("     ").Append(Bearing("GARAGE", garageDoor - me, fwd));
            GUI.Label(new Rect(12f, top + 4f, w - 130f, 24f), sb.ToString(), st);
        }
        if (yardToastT > 0f && yardToast.Length > 0)
        {
            var ts = new GUIStyle(GUI.skin.label); ts.fontSize = 20; ts.fontStyle = FontStyle.Bold; ts.alignment = TextAnchor.MiddleCenter;
            ts.normal.textColor = new Color(1f, 0.87f, 0.46f);
            GUI.Label(new Rect(0f, top + 44f, w, 30f), yardToast, ts);
        }
        bool challenge = false;
        if (yardCard)
        {
            var entry = EnemyRoster.Find(YARD_BOT);
            float cw = Mathf.Min(360f, w - 24f), ch = 112f;
            var box = new Rect((w - cw) * 0.5f, h - ch - 16f - Screen.safeArea.y / s, cw, ch);
            GUI.Box(box, "");
            var hs = new GUIStyle(GUI.skin.label); hs.fontSize = 18; hs.fontStyle = FontStyle.Bold; hs.alignment = TextAnchor.MiddleCenter;
            hs.normal.textColor = Color.white;
            GUI.Label(new Rect(box.x, box.y + 6f, box.width, 26f), entry.label + "   ·   " + entry.tier.ToString().ToUpper() + "   ·   yard bot", hs);
            var cs = new GUIStyle(GUI.skin.label); cs.fontSize = 13; cs.alignment = TextAnchor.MiddleCenter;
            cs.normal.textColor = new Color(0.75f, 0.80f, 0.88f);
            GUI.Label(new Rect(box.x, box.y + 32f, box.width, 20f), "30-second bout  ·  your machine drives itself (" + BrainPickTitle + ")  ·  drive away to decline", cs);
            GUI.skin.button.fontSize = 18;
            challenge = GUI.Button(new Rect(box.x + 24f, box.y + 58f, box.width - 48f, 44f), "CHALLENGE");
            GUI.skin.button.fontSize = fs;
        }
        GUI.matrix = saved;
        if (garage) { LeaveMap(); return; }
        if (challenge) ChallengeParked();
    }

    static string Bearing(string what, Vector3 to, Vector3 fwd)
    {
        to.y = 0f;
        float dist = to.magnitude;
        float ang = Vector3.SignedAngle(fwd, to, Vector3.up);
        string arrow = Mathf.Abs(ang) < 25f ? "↑" : ang > 0f ? (ang > 135f ? "↓" : "→") : (ang < -135f ? "↓" : "←");
        return what + " " + arrow + " " + Mathf.RoundToInt(dist) + " m";
    }
}
}
