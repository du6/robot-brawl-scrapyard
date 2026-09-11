// ===========================================================================
// MapBench.cs — THE YARD, measured (Robot Brawl: Scrapyard, design §6).
//
// What it proves in one play session, headlessly:
//   1. DRIVE OUT builds the yard: the fence holds every crate and the parked
//      bot, the first crate is 8 m from the door, the parked bot is SCOUT
//      (never the rookie's own build) and far enough away to be a drive;
//   2. the seed is the date: leaving and re-entering gives the same crates;
//   3. a crate opens once, where it stands, and pays (editor: the grant is
//      immediate); it does not respawn on re-entry the same day;
//   4. the encounter card comes up within reach and folds when you leave;
//   5. CHALLENGE enters a Quick bout that YOU drive (stick, not a program),
//      settles as a quick fight, and the garage is where you land after the
//      bell;
//   6. PLACES: the fixed three near home, flat ground, the shop pad, the
//      arena that fights itself.
//
// OWNER STATE IS SACRED: Career.Data is swapped for a fresh career, autosave
// is held (counted), and everything is restored. Run: BatchSmoke.Map.
// ===========================================================================
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RobotBrawl.Phase0
{
    public class MapBench : MonoBehaviour
    {
        public static bool finished;
        public static int passed, failed;
        static readonly List<string> log = new List<string>();

        public static MapBench Run()
        {
            finished = false; passed = failed = 0; log.Clear();
            return new GameObject("MapBench").AddComponent<MapBench>();
        }
        static void Check(bool ok, string what)
        {
            if (ok) passed++; else failed++;
            log.Add((ok ? "PASS  " : "FAIL  ") + what);
        }
        static bool OnGround(BuilderManager bm, Vector3 p) { return Mathf.Abs(p.y - bm.TerrainHeight(p.x, p.z)) < 2.5f; }

        IEnumerator Start()
        {
            var bm = Object.FindFirstObjectByType<BuilderManager>();
            if (bm == null) bm = new GameObject("BuilderManager").AddComponent<BuilderManager>();
            yield return null; yield return null;

            var savedData = Career.Data;
            bool savedActive = Career.active;
            float savedScale = Time.timeScale;
            var hold = Career.SuspendAutosave();
            Career.Data = new CareerData();
            Career.active = true;
            var d = Career.Data;
            d.taskFight = d.taskBolt = d.taskWeld = d.taskBuy = true; d.rescueGranted = true; d.guideDone = true;
            d.stable.Add(new CareerRobot { name = "SCRAPPER", snapshot = BuilderManager.STARTER_SNAPSHOT,
                                           program = "" });   // NO saved program: nothing here needs one
            d.activeRobot = 0;
            d.worldSeed = 4242;   // one world, every run - a rolled seed made the crate layout a coin flip
            Career.TopUpForSnapshot(BuilderManager.STARTER_SNAPSHOT);
            bm.LoadSnapshot(BuilderManager.STARTER_SNAPSHOT);
            RBTelemetry.TestReset();
            yield return null;

            // ---- 0. THE MAP IS THE FRONT DOOR ----------------------------------
            // A fresh BuilderManager with bootToYard drives out by itself.
            BuilderManager.bootToYard = true;
            Object.Destroy(bm.gameObject); yield return null;
            bm = new GameObject("BuilderManager").AddComponent<BuilderManager>();
            for (int i = 0; i < 6 && bm.mode != BuilderManager.Mode.Map; i++) yield return null;
            Check(bm.mode == BuilderManager.Mode.Map, "a fresh boot lands IN THE YARD, not the workshop (" + bm.mode + ")");
            Check(bm.testRobot != null, "...with the rookie under the stick");
            bm.LeaveMap(); yield return null;
            BuilderManager.bootToYard = false;
            Check(bm.mode == BuilderManager.Mode.Build, "GARAGE is the door back to the workshop");

            // ---- 1. DRIVE OUT ---------------------------------------------------
            bm.EnterMap(); yield return null; yield return null;
            Check(bm.mode == BuilderManager.Mode.Map, "DRIVE OUT enters the yard");
            Check(RBTelemetry.Has(RBTelemetry.MAP), "...and the funnel hears `map`");
            Check(bm.testRobot != null && !bm.testRobot.combatEnabled, "the player's machine is on the map, combat off");
            int loaded = bm.WorldChunksLoaded, want = (2 * BuilderManager.VIEW_CHUNKS + 1) * (2 * BuilderManager.VIEW_CHUNKS + 1);
            Check(loaded == want, "the world around home is loaded: " + loaded + " chunks of " + want);
            Check(bm.WorldSeedNow != 0 && d.worldSeed == bm.WorldSeedNow, "the world's seed was rolled and saved (" + d.worldSeed + ")");
            var crates = bm.YardCratePositions();
            int cratesAtBoot = crates.Count;
            Check(crates.Count >= 2, "crates stand in the loaded world (" + crates.Count + ")");
            bool allOn = true; foreach (var c in crates) if (!OnGround(bm, c)) allOn = false;
            Check(allOn, "...every crate sits on the terrain");
            float d0 = -1f;
            foreach (var c in crates) { float dd = Vector3.Distance(new Vector3(c.x, 0f, c.z), bm.YardGarageDoor); if (dd > 7f && dd < 9f) d0 = dd; }
            Check(d0 > 0f, "a crate stands 8 m from home, in view (" + d0.ToString("0.0") + ")");
            var parked = bm.YardParked;
            Check(parked != null && OnGround(bm, parked.rb.position), "an enemy is parked on the terrain");
            Check(parked != null && parked.name.ToUpper().Contains("SCOUT"), "...the nearest is SCOUT, not another rookie (" + (parked != null ? parked.name : "-") + ")");
            Check(parked != null && Vector3.Distance(parked.rb.position, bm.YardGarageDoor) > 20f, "...far enough from home to be a drive");
            Check(!bm.YardCardShown, "no card at home");
            // ---- the look (WorldLook): a planet, not a plane ----------------------
            Check(Shader.Find("Scrapyard/PlanetGround") != null && Shader.Find("Scrapyard/SkyBody") != null, "both planet shaders exist");
            Check(bm.GroundMaterial != null && bm.GroundMaterial.shader.name == "Scrapyard/PlanetGround", "the ground draws with the planet shader");
            var hpl = bm.YardGarageDoor;
            var groundGo = GameObject.Find("chunk_" + Mathf.FloorToInt(hpl.x / BuilderManager.CHUNK) + "_" + Mathf.FloorToInt(hpl.z / BuilderManager.CHUNK) + "/ground");
            var gmesh = groundGo != null ? groundGo.GetComponent<MeshFilter>().sharedMesh : null;
            Check(gmesh != null && gmesh.colors.Length == gmesh.vertexCount, "...with a colour on every vertex");
            Check(groundGo != null && groundGo.GetComponent<MeshCollider>() != null, "...and a collider to drive on");
            Check(bm.PlanetLookOn && RenderSettings.fog && RenderSettings.fogMode == FogMode.Linear, "the sky is the planet's, with fog to the edge of the loaded world");
            Check(GameObject.Find("sister_planet") != null && GameObject.Find("moon") != null, "a sister planet and a moon hang on the horizon");
            int structures = 0;
            if (groundGo != null) foreach (var tr in groundGo.transform.parent.GetComponentsInChildren<Transform>()) if (tr.name.StartsWith("structure_")) structures++;
            Check(structures >= 3, "the home chunk has its plaza and structures (" + structures + ")");
            float mesaH = -1f, craterH = 1f;
            for (int sx = -600; sx <= 600 && (mesaH < 3.5f || craterH > -2f); sx += 40)
                for (int sz = -600; sz <= 600; sz += 40)
                { float hh = bm.TerrainHeight(sx, sz); if (hh > mesaH) mesaH = hh; if (hh < craterH) craterH = hh; }
            Check(mesaH > 3.5f && craterH < -2f, "the land has mesas and craters (high " + mesaH.ToString("0.0") + " m, low " + craterH.ToString("0.0") + " m)");
            var hp = bm.YardGarageDoor;
            Check(Mathf.Abs(bm.TerrainHeight(hp.x, hp.z)) < 0.01f && Mathf.Abs(bm.TerrainHeight(hp.x + 10f, hp.z + 10f)) < 0.01f, "home is flat");
            float hA = bm.TerrainHeight(300f, 300f), hB = bm.TerrainHeight(-260f, 410f);
            Check(Mathf.Abs(hA - hB) > 0.05f || Mathf.Abs(hA) > 0.05f, "...and the world is not (" + hA.ToString("0.0") + " m, " + hB.ToString("0.0") + " m)");

            // ---- 2. the seed is the date --------------------------------------
            bm.LeaveMap(); yield return null;
            Check(bm.mode == BuilderManager.Mode.Build, "GARAGE returns to the garage");
            Check(!bm.PlanetLookOn && !RenderSettings.fog, "...and the garage gets its own sky and no fog back");
            bm.EnterMap(); yield return null; yield return null;
            var crates2 = bm.YardCratePositions();
            bool same = crates2.Count == crates.Count;
            for (int i = 0; same && i < crates.Count; i++) if ((crates[i] - crates2[i]).sqrMagnitude > 0.01f) same = false;
            Check(same && crates2.Count == cratesAtBoot, "leaving and re-entering gives the same world (the seed persists)");

            // ---- 3. a crate opens where it stands ------------------------------
            int scrap0 = d.scrap; int items0 = 0; foreach (var it in d.inventory) items0 += it.count;
            int before = bm.YardCratesLeft;
            bm.TeleportPlayer(new Vector3(crates2[0].x, 0f, crates2[0].z));
            yield return null; yield return null; yield return null;
            Check(bm.YardCratesLeft == before - 1, "driving into a crate opens it (" + bm.YardCratesLeft + " of " + before + " left)");
            int items1 = 0; foreach (var it in d.inventory) items1 += it.count;
            Check(d.scrap > scrap0 && items1 > items0, "...and it paid scrap and a part at once (+" + (d.scrap - scrap0) + " scrap, +" + (items1 - items0) + " part)");
            Check(RBTelemetry.Has(RBTelemetry.CRATE), "...and the funnel hears `crate`");
            var burst = GameObject.Find("treasure_burst");
            int coins = 0; if (burst != null) foreach (var tr in burst.GetComponentsInChildren<Transform>()) if (tr.name == "coin") coins++;
            Check(burst != null && coins == 10, "...coins fly from the chest where it stood (" + coins + ")");
            Check(burst != null && burst.GetComponentInChildren<BuilderManager.RiseAndTurn>() != null, "...and the part rises out of it");
            var thumb = RewardThumb.Render("beam", "Aluminum", 64);
            Check(thumb != null && thumb.width == 64 && thumb.height == 64, "a reward's picture renders to a 64 px texture (the part built by the game's own visual factory)");
            var coinsTex = RewardThumb.Render("coins", null, 64);
            Check(coinsTex != null && coinsTex.width == 64, "...and scrap's picture is a coin stack");
            if (thumb != null) Object.Destroy(thumb); if (coinsTex != null) Object.Destroy(coinsTex);
            Check(GameObject.Find("reward_studio") == null, "...and the studio is torn down after the photograph");
            Check(d.worldOpened.Count == 1 && d.worldOpened[0].Contains(":"), "...and the save remembers which crate, by chunk (" + d.worldOpened[0] + ")");
            bm.LeaveMap(); yield return null;
            bm.EnterMap(); yield return null; yield return null;
            Check(bm.YardCratesLeft == before - 1, "an opened crate never respawns (" + bm.YardCratesLeft + ")");
            // drive far: chunks stream in ahead and drop behind
            bm.TeleportPlayer(new Vector3(hp.x, 0f, hp.z + 400f));
            for (int i = 0; i < 40; i++) yield return null;
            Check(bm.WorldChunksLoaded == want, "400 m out, the world is still " + want + " chunks around you (" + bm.WorldChunksLoaded + ")");
            Vector3 far = bm.testRobot.rb.position;
            Check(OnGround(bm, far), "...and you are on the ground there (y " + far.y.ToString("0.0") + " vs ground " + bm.TerrainHeight(far.x, far.z).ToString("0.0") + ")");
            bm.TeleportPlayer(new Vector3(hp.x, 0f, hp.z));
            for (int i = 0; i < 40; i++) yield return null;

            // ---- 4. the encounter card ------------------------------------------
            parked = bm.YardParked;
            bm.TeleportPlayer(parked.rb.position + new Vector3(2.5f, 0f, 0f));
            yield return null; yield return null;
            Check(bm.YardCardShown, "the card comes up within " + BuilderManager.CARD_REACH + " m of the parked bot");
            Check(RBTelemetry.Has(RBTelemetry.MEET), "...and the funnel hears `meet`");
            bm.TeleportPlayer(bm.YardGarageDoor + new Vector3(0f, 0f, -6f));
            yield return null; yield return null;
            Check(!bm.YardCardShown, "drive away and the card folds - decline is free");

            // ---- 5b. the drive: straight when you mean straight, and it still turns ---
            // owen, 2026-09-10: "turning is too sensitive, making it hard to
            // drive straight". Full throttle, no steer, for two seconds must hold
            // a heading; a small stick wobble inside the dead zone must not
            // steer; a held full stick must still turn.
            bm.TeleportPlayer(new Vector3(hp.x - 9f, 0f, hp.z - 8f));   // FLAT and WRECK-FREE (inside 12 m of home), off the crate lane
            for (int i = 0; i < 30; i++) yield return null;                 // settle after the drop
            Vector3 fwd0 = bm.testRobot.transform.TransformDirection(bm.TestDriveDir); fwd0.y = 0f;
            Vector3 pos0 = bm.testRobot.rb.position;
            Phase0Input.debugThrottle = 1f; Phase0Input.debugSteer = 0.12f;   // a wobble inside the dead zone
            float tEnd = Time.time + 2.0f; float tNext = Time.time;
            var trace = new System.Text.StringBuilder();
            while (Time.time < tEnd)
            {
                if (Time.time >= tNext)
                {
                    Vector3 f = bm.testRobot.transform.TransformDirection(bm.TestDriveDir); f.y = 0f;
                    trace.Append(Time.time.ToString("0.0")).Append("s yaw ").Append(Vector3.SignedAngle(fwd0, f, Vector3.up).ToString("0")).Append(" steer ").Append(bm.MapSteerNow.ToString("0.00")).Append(" thr ").Append(bm.testDrive.CurrentThrottle().ToString("0.00")).Append(" v ").Append(VelUtil.GetLinearVelocity(bm.testRobot.rb).magnitude.ToString("0.0")).Append(" | ");
                    tNext += 0.4f;
                }
                yield return null;
            }
            log.Add("      trace: " + trace);
            Vector3 fwd1 = bm.testRobot.transform.TransformDirection(bm.TestDriveDir); fwd1.y = 0f;
            float turned = Vector3.Angle(fwd0, fwd1);
            float went = Vector3.Distance(pos0, bm.testRobot.rb.position);
            Check(went > 3f, "full throttle for 2 s moves the machine (" + went.ToString("0.0") + " m)");
            Check(turned < 8f, "...and with the stick inside the dead zone it holds its heading (" + turned.ToString("0.0") + " deg)");
            Check(Mathf.Abs(bm.MapSteerNow) <= BuilderManager.HOLD_MAX + 0.001f, "...the dead zone reads as zero stick; only the heading hold steers (" + bm.MapSteerNow.ToString("0.00") + ")");
            Phase0Input.debugSteer = 1f;
            tEnd = Time.time + 1.5f;
            while (Time.time < tEnd) yield return null;
            Vector3 fwd2 = bm.testRobot.transform.TransformDirection(bm.TestDriveDir); fwd2.y = 0f;
            Check(Vector3.Angle(fwd1, fwd2) > 15f, "a held full stick still turns it (" + Vector3.Angle(fwd1, fwd2).ToString("0") + " deg in 1.5 s)");
            Check(bm.MapSteerNow > 0f && bm.MapSteerNow <= BuilderManager.STEER_GAIN + 0.001f, "...at no more than the map's gain (" + bm.MapSteerNow.ToString("0.00") + ")");
            Check(TouchControls.RANGE >= 130f && Mathf.Approximately(TouchControls.RING, 2f * TouchControls.RANGE) && Mathf.Approximately(TouchControls.KNOB_TRAVEL, TouchControls.RANGE) && TouchControls.MouseAccepted,
                  "the stick is big and honest: the ring's edge is full lock (travel " + TouchControls.RANGE + ", ring " + TouchControls.RING + "), and it takes a mouse on every platform");
            Phase0Input.debugThrottle = 0f; Phase0Input.debugSteer = 0f;
            for (int i = 0; i < 30; i++) yield return null;

            // ---- 6. PLACES -----------------------------------------------------------
            // owen, 2026-09-10: "it looks like a desert with some cubes ... can we
            // add buildings, shops, robot parks, toys, arenas with robots fighting
            // each other". A place flattens its ground, the first three are fixed
            // near home, the shop's pad opens the workshop, the arena fights itself.
            float pd; var np = bm.NearestPlace(hp, out pd);
            Check(np != null && np.kind == BuilderManager.PlaceKind.Shop && pd < 80f, "the nearest place to home is a TRADING POST, under 80 m (" + (np != null ? np.name + " " + pd.ToString("0") : "none") + ")");
            int hcx = Mathf.FloorToInt(hp.x / BuilderManager.PLACE_CELL), hcz = Mathf.FloorToInt(hp.z / BuilderManager.PLACE_CELL);
            var parkP = bm.PlaceInCell(hcx + 1, hcz); var arenaP = bm.PlaceInCell(hcx, hcz + 1);
            Check(parkP != null && parkP.kind == BuilderManager.PlaceKind.Park, "the next cell east is a ROBOT PARK");
            Check(arenaP != null && arenaP.kind == BuilderManager.PlaceKind.Arena, "the next cell north is an ARENA");
            int nPlaces = 0; var kinds = new HashSet<BuilderManager.PlaceKind>();
            for (int dz = -4; dz <= 4; dz++) for (int dx = -4; dx <= 4; dx++) { var q = bm.PlaceInCell(hcx + dx, hcz + dz); if (q != null) { nPlaces++; kinds.Add(q.kind); } }
            Check(nPlaces >= 35 && nPlaces <= 70, "81 cells around home hold 35..70 places (" + nPlaces + ")");
            Check(kinds.Count == 5, "...of all five kinds (" + kinds.Count + ")");
            Check(bm.PlaceInCell(hcx + 3, hcz - 2) == bm.PlaceInCell(hcx + 3, hcz - 2), "a cell's place is one object, asked twice");
            bool flatAll = true; string flatWhy = "";
            foreach (var q in new[] { np, parkP, arenaP })
            {
                if (q == null) { flatAll = false; continue; }
                float lo = float.MaxValue, hi = float.MinValue;
                for (int i = 0; i < 12; i++)
                {
                    float a = i * Mathf.PI / 6f, r = (i % 2 == 0) ? q.radius * 0.85f : q.radius * 0.4f;
                    float hh = bm.TerrainHeight(q.centre.x + Mathf.Cos(a) * r, q.centre.z + Mathf.Sin(a) * r);
                    lo = Mathf.Min(lo, hh); hi = Mathf.Max(hi, hh);
                }
                if (hi - lo > 0.05f) { flatAll = false; flatWhy += q.name + " spread " + (hi - lo).ToString("0.00") + "; "; }
            }
            Check(flatAll, "the ground under each of the three is flat to 5 cm " + flatWhy);
            // the shop pad
            var pads = bm.YardShopPads();
            Check(pads.Count >= 1, "the trading post has a lit pad (" + pads.Count + ")");
            Vector3 pad0 = pads.Count > 0 ? pads[0] : hp;
            Check(Vector2.Distance(new Vector2(pad0.x, pad0.z), new Vector2(np.centre.x, np.centre.z)) < 12f, "...at the shopfront");
            bm.TeleportPlayer(new Vector3(pad0.x, 0f, pad0.z - 9f)); for (int i = 0; i < 6; i++) yield return null;
            Check(bm.mode == BuilderManager.Mode.Map, "9 m short of the pad, still on the map");
            bm.TeleportPlayer(new Vector3(pad0.x, 0f, pad0.z)); for (int i = 0; i < 4; i++) yield return null;
            Check(bm.mode == BuilderManager.Mode.Build && bm.LastShopOpened, "drive onto the pad and the workshop opens (" + bm.mode + ")");
            if (MobileBuilderUI.inst != null) Check(MobileBuilderUI.inst.Tab == 3 && MobileBuilderUI.inst.DockOpen, "...on the SHOP tab, dock open (tab " + MobileBuilderUI.inst.Tab + ")");
            bm.EnterMap(); for (int i = 0; i < 8; i++) yield return null;
            Vector3 back = bm.testRobot.rb.position;
            Check(bm.mode == BuilderManager.Mode.Map && Vector2.Distance(new Vector2(back.x, back.z), new Vector2(pad0.x, pad0.z)) < 3f, "DRIVE OUT puts you back at the pad, not home (" + Vector2.Distance(new Vector2(back.x, back.z), new Vector2(pad0.x, pad0.z)).ToString("0.0") + " m)");
            Check(bm.mode == BuilderManager.Mode.Map, "...and standing on the pad does not walk you straight back in");
            bm.TeleportPlayer(new Vector3(pad0.x, 0f, pad0.z - 9f)); for (int i = 0; i < 4; i++) yield return null;
            bm.TeleportPlayer(new Vector3(pad0.x, 0f, pad0.z)); for (int i = 0; i < 4; i++) yield return null;
            Check(bm.mode == BuilderManager.Mode.Build, "leave the pad and return: the door works again");
            bm.EnterMap(); for (int i = 0; i < 4; i++) yield return null;
            // the arena: two machines, fighting each other
            bm.TeleportPlayer(new Vector3(arenaP.centre.x, 0f, arenaP.centre.z - 19f));
            for (int i = 0; i < 40; i++) yield return null;
            Check(bm.NearestPlaceNow != null && bm.NearestPlaceNow.kind == BuilderManager.PlaceKind.Arena && (bm.NearestPlaceNow.centre - arenaP.centre).sqrMagnitude < 1f, "by the stands the compass names the ARENA (" + (bm.NearestPlaceNow != null ? bm.NearestPlaceNow.name : "none") + ")");
            var fighters = bm.YardArenaFighters();
            Check(fighters.Count == 2, "the arena holds two machines (" + fighters.Count + ")");
            bool wired = fighters.Count == 2;
            if (wired)
            {
                var aiA = fighters[0].GetComponent<AIController>(); var aiB = fighters[1].GetComponent<AIController>();
                wired = aiA != null && aiB != null && aiA.target == fighters[1] && aiB.target == fighters[0] && fighters[0].combatEnabled && fighters[1].combatEnabled;
            }
            Check(wired, "...each driven by an AIController aimed at the other, combat armed");
            Vector3 fa0 = fighters.Count > 0 ? fighters[0].rb.position : Vector3.zero, fb0 = fighters.Count > 1 ? fighters[1].rb.position : Vector3.zero;
            for (int i = 0; i < 90; i++) yield return null;
            var f2 = bm.YardArenaFighters();
            float movedA = f2.Count > 0 && f2[0] != null ? Vector3.Distance(f2[0].rb.position, fa0) : 0f, movedB = f2.Count > 1 && f2[1] != null ? Vector3.Distance(f2[1].rb.position, fb0) : 0f;
            Check(movedA > 0.5f || movedB > 0.5f, "...and they fight: a machine moved in 1.5 s (" + movedA.ToString("0.0") + " m, " + movedB.ToString("0.0") + " m)");
            bool inRing = true; foreach (var f in f2) if (f != null && Vector3.Distance(f.rb.position, arenaP.centre) > 16f) inRing = false;
            Check(inRing, "...inside the ring");
            Check(bm.testRobot.combatEnabled == false, "the spectator is not in the fight");
            Check(bm.YardPlaceBots >= 2, "the loaded world's places carry machines (" + bm.YardPlaceBots + ")");
            bm.TeleportPlayer(new Vector3(hp.x, 0f, hp.z)); for (int i = 0; i < 40; i++) yield return null;
            // (another arena may sit inside home's view - a random cell - so the
            // check is that THESE two are gone, not that none are loaded)
            bool gone = true; foreach (var f in f2) if (f != null) gone = false;
            Check(gone, "drive home and the arena's machines unload with their chunk");
            parked = bm.YardParked;

            // ---- 5. CHALLENGE ------------------------------------------------------
            bm.TeleportPlayer(parked.rb.position + new Vector3(2.5f, 0f, 0f));
            yield return null; yield return null;
            bm.ChallengeParked();
            yield return null; yield return null;
            var fm = Object.FindFirstObjectByType<FightManager>();
            Check(bm.mode == BuilderManager.Mode.Fight && fm != null, "CHALLENGE enters a fight");
            Check(FightManager.quickBout && Career.quickFight, "...a Quick bout, settled as a quick fight");
            // owen, 2026-09-10: "replace auto fight with manual fight" - the
            // player's side is the stick, and no program sits on the robot.
            Check(fm != null && fm.playerSource == ControlSource.Keyboard, "...with YOU at the stick, not a program (" + (fm != null ? fm.playerSource.ToString() : "-") + ")");
            Check(bm.testRobot != null && bm.testRobot.GetComponent<ProgramRunner>() == null, "...and no ProgramRunner on your machine");
            Check(bm.opponentId == BuilderManager.YARD_BOT, "...against the parked bot (" + bm.opponentId + ")");
            Check(RBTelemetry.Has(RBTelemetry.CHALLENGE), "...and the funnel hears `challenge`");
            Check(TouchControls.fightActive, "...the stick is up for the bout");
            // the stick actually drives: wait for the bell, hold the throttle
            // a second, and the machine has moved. Control leg: released, the
            // next second moves it much less.
            float bellBy = Time.realtimeSinceStartup + 8f;
            while (fm != null && fm.state == FightManager.State.Settling && Time.realtimeSinceStartup < bellBy) yield return null;
            Check(fm != null && fm.state == FightManager.State.Fighting, "the bell rings (" + (fm != null ? fm.state.ToString() : "-") + ")");
            Check(bm.testRobot != null && bm.testRobot.controlSource == ControlSource.Keyboard, "...and at the bell your machine takes the keyboard/stick source (" + (bm.testRobot != null ? bm.testRobot.controlSource.ToString() : "-") + ")");
            Vector3 f0 = bm.testRobot != null ? bm.testRobot.rb.position : Vector3.zero;
            Phase0Input.debugThrottle = 1f; Phase0Input.debugSteer = 0f;
            for (int i = 0; i < 60; i++) yield return null;
            float drove = bm.testRobot != null ? Vector3.Distance(bm.testRobot.rb.position, f0) : 0f;
            Phase0Input.debugThrottle = 0f;
            Check(drove > 1.0f, "a held throttle drives your machine in the ring (" + drove.ToString("0.0") + " m in 1 s)");
            Time.timeScale = 4f;
            float deadline = Time.realtimeSinceStartup + 40f;
            while (fm != null && fm.state != FightManager.State.Ended && Time.realtimeSinceStartup < deadline) yield return null;
            Time.timeScale = savedScale;
            Check(fm != null && fm.state == FightManager.State.Ended, "the bout ended on its own");
            Check(d.quickFights == 1, "...and settled once (quickFights=" + d.quickFights + ")");
            bm.BackToBuild(); yield return null;
            Check(bm.mode == BuilderManager.Mode.Build && Object.FindFirstObjectByType<FightManager>() == null, "after the bell, the garage");
            Check(Career.TxnSum() == d.scrap, "the ledger still audits");

            // ---- restore -------------------------------------------------------------
            Time.timeScale = savedScale;
            Career.Data = savedData;
            Career.active = savedActive;
            hold.Dispose();
            foreach (var l in log) Debug.Log("[MapBench] " + l);
            Debug.Log(string.Format("[MapBench] RESULT: {0} pass, {1} fail{2}", passed, failed, failed == 0 ? " - ALL GREEN" : " - FIX NEEDED"));
            try { System.IO.File.WriteAllText(Application.dataPath + "/Phase1/qa_map_bench.txt", string.Join("\n", log.ToArray()) + "\n"); } catch { }
            finished = true;
        }
    }
}
#endif
