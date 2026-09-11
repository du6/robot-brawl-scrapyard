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
using UnityEngine.UI;

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
            // ---- the HUD is UGUI, in the dock's style (MapHudUI) ---------------------
            yield return null;
            var hud = MapHudUI.inst;
            Check(hud != null && GameObject.Find("map_hud_canvas") != null, "the map's HUD is a UGUI canvas, not IMGUI");
            Check(hud != null && hud.ChipsShown >= 3 && hud.ChipText(0).StartsWith("TREASURE") && hud.ChipText(hud.ChipsShown - 1).StartsWith("HOME"),
                  "...compass chips: TREASURE first, HOME last (" + (hud != null ? hud.ChipsShown : 0) + " chips: " + (hud != null ? hud.ChipText(0) + " | " + hud.ChipText(1) : "") + ")");
            Check(hud != null && hud.ChipText(0).EndsWith(" m"), "...each with a distance in metres");
            Check(hud != null && hud.GarageButton != null && hud.GarageButton.GetComponentInChildren<Text>().text == "GARAGE", "...and a GARAGE button");
            Check(hud != null && !hud.CardShown, "...no encounter card at home");
            Check(hud != null && hud.ChipHasDrawnNeedle(0), "...the needle is drawn, not a glyph (a glyph rendered as nothing on the web)");
            float needle0 = hud != null ? hud.ChipNeedleDeg(0) : 0f, want0 = Mathf.Repeat(-bm.HudModel().compass[0].angle, 360f);
            Check(hud != null && Mathf.Abs(Mathf.DeltaAngle(needle0, want0)) < 1f, "...and it points at the bearing (" + needle0.ToString("0") + " vs " + want0.ToString("0") + ")");
            // ---- the first minute: objective line, chip, marker (CrazyGames step 1) ----
            Check(bm.YardStep == BuilderManager.STEP_CHEST && hud != null && hud.ObjectiveText.Contains("chest"), "a fresh career's first objective is the chest (" + (hud != null ? hud.ObjectiveText : "") + ")");
            Check(hud != null && hud.ChipHighlighted(0), "...the TREASURE chip is highlighted");
            var crates0 = bm.YardCratePositions();
            Check(bm.ObjectiveMarkerShown && crates0.Count > 0 && Vector2.Distance(new Vector2(bm.ObjectiveMarkerPos.x, bm.ObjectiveMarkerPos.z), new Vector2(crates0[0].x, crates0[0].z)) < 0.5f,
                  "...and a ring and beam stand on the nearest chest");
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
            // ...and it stays parked (live, 2026-09-10: the SCOUT crept 10 m downhill in 10 s)
            Vector3 parked0 = parked != null ? parked.rb.position : Vector3.zero;
            for (int i = 0; i < 180; i++) yield return null;
            float crept = parked != null ? Vector2.Distance(new Vector2(parked.rb.position.x, parked.rb.position.z), new Vector2(parked0.x, parked0.z)) : 99f;
            Check(crept < 0.5f, "...and stays put for 3 s (" + crept.ToString("0.00") + " m)");
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
            // ---- the horizon, landmarks, roads, life, pools (WorldFar) --------------
            // owen, 2026-09-10: "investigate how other popular games design the
            // game map and improve our game's visual experience"
            var farG = bm.FarGround;
            var farMesh = farG != null ? farG.GetComponent<MeshFilter>().sharedMesh : null;
            Check(farMesh != null && farMesh.vertexCount == (BuilderManager.FAR_RES + 1) * (BuilderManager.FAR_RES + 1) && farG.GetComponent<Collider>() == null,
                  "a far mesh carries the land to " + BuilderManager.FAR_RADIUS + " m, one draw, no collider (" + (farMesh != null ? farMesh.vertexCount : 0) + " verts)");
            Check(RenderSettings.fogEndDistance >= 500f && RenderSettings.fogStartDistance >= 60f, "...under haze, not a wall (fog " + RenderSettings.fogStartDistance + " -> " + RenderSettings.fogEndDistance + " m)");
            var farGo = GameObject.Find("far");
            int lmCount = 0; if (farGo != null) foreach (Transform tr in farGo.transform) if (tr.name.StartsWith("landmark_")) lmCount++;
            Check(bm.LandmarksBuilt >= 4 && lmCount == bm.LandmarksBuilt, "landmarks stand on the skyline, one per 600 m cell in view (" + lmCount + ")");
            var hp0 = bm.YardGarageDoor;
            int hcx0 = Mathf.FloorToInt(hp0.x / BuilderManager.PLACE_CELL), hcz0 = Mathf.FloorToInt(hp0.z / BuilderManager.PLACE_CELL);
            var shopR = bm.PlaceInCell(hcx0, hcz0); var parkR = bm.PlaceInCell(hcx0 + 1, hcz0);
            Vector3 midR = (shopR.centre + parkR.centre) * 0.5f;
            Vector3 dirR = (parkR.centre - shopR.centre).normalized; Vector3 sideR = Vector3.Cross(Vector3.up, dirR);
            Check(bm.RoadAt(midR.x, midR.z) > 0.95f, "a road runs from the trading post to the park (crown " + bm.RoadAt(midR.x, midR.z).ToString("0.00") + ")");
            Check(bm.RoadAt(midR.x + sideR.x * 30f, midR.z + sideR.z * 30f) == 0f, "...and 30 m beside it is open ground");
            Check(bm.YardScatterLoaded >= 100 && bm.YardScatterLoaded <= 400, "the loaded world carries ground life, sparse (7-13 a chunk) - boulders, tufts, shards, vents (" + bm.YardScatterLoaded + " over " + bm.WorldChunksLoaded + " chunks)");
            bool batched = false; int scatterR = 0;
            foreach (var mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                if (mr.transform.name.StartsWith("scatter_") || (mr.transform.parent != null && mr.transform.parent.name.StartsWith("scatter_"))) { scatterR++; if (mr.isPartOfStaticBatch) batched = true; }
            Check(scatterR > 0 && batched, "...static-batched into a few draws (" + scatterR + " renderers)");
            Vector2 poolC;
            bool anyPool = bm.NearestPool(hp0, out poolC);
            Check(anyPool, "a pool-sized crater exists within 13 x 13 cells of home");
            if (anyPool)
            {
                bm.TeleportPlayer(new Vector3(poolC.x, 0f, poolC.y + 10f));
                for (int i = 0; i < 40; i++) yield return null;
                var poolGo = GameObject.Find("pool");
                Check(bm.YardPoolsLoaded >= 1 && poolGo != null, "...and it holds a glowing pool once its chunk is in (" + bm.YardPoolsLoaded + ")");
                Check(poolGo != null && poolGo.transform.position.y > bm.TerrainHeight(poolC.x, poolC.y) + 0.5f && poolGo.transform.position.y < bm.TerrainHeight(poolC.x, poolC.y) + 8f,
                      "...its surface above the crater floor, below the rim");
                bm.TeleportPlayer(new Vector3(hp0.x, 0f, hp0.z));
                for (int i = 0; i < 40; i++) yield return null;
            }
            var hp = bm.YardGarageDoor;
            Check(Mathf.Abs(bm.TerrainHeight(hp.x, hp.z)) < 0.01f && Mathf.Abs(bm.TerrainHeight(hp.x + 10f, hp.z + 10f)) < 0.01f, "home is flat");
            float hA = bm.TerrainHeight(300f, 300f), hB = bm.TerrainHeight(-260f, 410f);
            Check(Mathf.Abs(hA - hB) > 0.05f || Mathf.Abs(hA) > 0.05f, "...and the world is not (" + hA.ToString("0.0") + " m, " + hB.ToString("0.0") + " m)");

            // ---- 2. the seed is the date --------------------------------------
            MapHudUI.inst.GarageButton.onClick.Invoke(); yield return null;   // the BUTTON, not the seam
            Check(bm.mode == BuilderManager.Mode.Build, "GARAGE returns to the garage");
            Check(MapHudUI.inst == null, "...and the map's HUD is gone with the map");
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
            yield return null;
            Check(bm.YardStep == BuilderManager.STEP_MEET && MapHudUI.inst.ObjectiveText.Contains("parked robot"), "the chest opened: the objective moves to the parked robot (" + MapHudUI.inst.ObjectiveText + ")");
            var en0 = bm.YardParked;
            Check(en0 != null && bm.ObjectiveMarkerShown && Vector2.Distance(new Vector2(bm.ObjectiveMarkerPos.x, bm.ObjectiveMarkerPos.z), new Vector2(en0.rb.position.x, en0.rb.position.z)) < 0.5f,
                  "...and the marker stands on it");
            Check(MapHudUI.inst.ChipHighlighted(1), "...with the enemy's chip highlighted");
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
            Check(MapHudUI.inst != null && MapHudUI.inst.CardShown && MapHudUI.inst.ChallengeButton.GetComponentInChildren<Text>().text == "CHALLENGE", "...on the HUD, with a CHALLENGE button");
            Check(bm.YardStep == BuilderManager.STEP_CHALLENGE && MapHudUI.inst.ObjectiveText.Contains("CHALLENGE"), "the card came up: the objective says tap CHALLENGE (" + MapHudUI.inst.ObjectiveText + ")");
            Check(RBTelemetry.Has(RBTelemetry.MEET), "...and the funnel hears `meet`");
            bm.TeleportPlayer(bm.YardGarageDoor + new Vector3(0f, 0f, -6f));
            yield return null; yield return null;
            Check(!bm.YardCardShown, "drive away and the card folds - decline is free");

            // ---- 5b. the drive: point where you want to go -------------------------
            // owen, 2026-09-10: "the driving still feels tricky" -> the stick's
            // angle is a heading relative to the camera, its length is speed
            // (Drive.cs). Up = straight away from the camera and it holds; a
            // held 40 deg turns to 40 deg and STOPS turning; the dead zone is
            // centred; straight back reverses; let go and it coasts.
            // ON THE TRADING POST'S PLAZA, FACING EAST: flat by construction and
            // nothing random spawns inside a place (Places.cs), the building sits
            // north of the run-out. Two home-plaza spots failed first: one turned
            // into the left monolith ("stalled at 0.0 m/s"), the next drove south
            // straight through the relay hub at (0,-9) - a 25 deg kick at 1.2 s
            // that read as a heading drift (measured 2026-09-10, both).
            float pdd; var shopP = bm.NearestPlace(hp, out pdd);
            Vector3 runOut = shopP != null ? shopP.centre + new Vector3(-12f, 0f, -14f) : new Vector3(hp.x + 2f, 0f, hp.z - 6f);
            bm.TeleportPlayer(runOut, 90f);
            float settleBy = Time.time + 4f;   // the camera swings behind the new facing (140 deg/s) - wait for it
            while (Time.time < settleBy && Mathf.Abs(Mathf.DeltaAngle(bm.CamYawNow, BuilderManager.HeadingYaw(bm.testRobot.transform.TransformDirection(bm.TestDriveDir)))) > 2f) yield return null;
            for (int i = 0; i < 20; i++) yield return null;
            Vector3 pos0 = bm.testRobot.rb.position;
            Phase0Input.debugThrottle = 1f; Phase0Input.debugSteer = 0.08f;   // up, with a wobble inside the dead zone
            yield return null;
            float camYaw0 = bm.DriveWantYaw;   // the frame latched at the press: the camera's heading then
            float tEnd = Time.time + 2.0f; float tNext = Time.time;
            var trace = new System.Text.StringBuilder();
            while (Time.time < tEnd)
            {
                if (Time.time >= tNext)
                {
                    trace.Append(Time.time.ToString("0.0")).Append("s err ").Append(bm.DriveErrNow.ToString("0")).Append(" steer ").Append(bm.MapSteerNow.ToString("0.00")).Append(" | ");
                    tNext += 0.4f;
                }
                yield return null;
            }
            log.Add("      trace: " + trace);
            float yaw1 = BuilderManager.HeadingYaw(bm.testRobot.transform.TransformDirection(bm.TestDriveDir));
            float went = Vector3.Distance(pos0, bm.testRobot.rb.position);
            Check(went > 3f, "stick up for 2 s moves the machine (" + went.ToString("0.0") + " m)");
            Check(Mathf.Abs(Mathf.DeltaAngle(camYaw0, yaw1)) < 8f, "...straight away from the camera (" + Mathf.DeltaAngle(camYaw0, yaw1).ToString("0.0") + " deg off)");
            Check(BuilderManager.StickNow().y > 0.9f && Mathf.Abs(BuilderManager.StickNow().x) < 0.1f, "...the wobble reads as part of one stick vector, not a steer");
            // a held angle: turn TO it and stop turning
            float camYaw1 = bm.CamYawNow;
            Phase0Input.debugThrottle = Mathf.Cos(40f * Mathf.Deg2Rad); Phase0Input.debugSteer = Mathf.Sin(40f * Mathf.Deg2Rad);
            tEnd = Time.time + 1.6f; tNext = Time.time; var tr2 = new System.Text.StringBuilder(); float peakW = 0f;
            while (Time.time < tEnd)
            {
                peakW = Mathf.Max(peakW, Mathf.Abs(bm.testRobot.rb.angularVelocity.y * Mathf.Rad2Deg));
                if (Time.time >= tNext)
                {
                    float yy = BuilderManager.HeadingYaw(bm.testRobot.transform.TransformDirection(bm.TestDriveDir));
                    tr2.Append((Time.time - tEnd + 1.6f).ToString("0.0")).Append("s yaw ").Append(yy.ToString("0")).Append(" err ").Append(bm.DriveErrNow.ToString("0")).Append(" st ").Append(bm.MapSteerNow.ToString("0.00")).Append(" thr ").Append(bm.testDrive.aiThrottle.ToString("0.00")).Append(" w ").Append((bm.testRobot.rb.angularVelocity.y * Mathf.Rad2Deg).ToString("0")).Append(" v ").Append(VelUtil.GetLinearVelocity(bm.testRobot.rb).magnitude.ToString("0.0")).Append(" | ");
                    tNext += 0.15f;
                }
                yield return null;
            }
            log.Add("      turn trace: " + tr2);
            float yawA = BuilderManager.HeadingYaw(bm.testRobot.transform.TransformDirection(bm.TestDriveDir));
            tEnd = Time.time + 0.8f; while (Time.time < tEnd) yield return null;
            float yawB = BuilderManager.HeadingYaw(bm.testRobot.transform.TransformDirection(bm.TestDriveDir));
            float wantYaw = Mathf.DeltaAngle(0f, bm.DriveWantYaw);
            log.Add("      40 deg hold: cam " + camYaw1.ToString("0") + " want " + wantYaw.ToString("0") + " yaw " + yawA.ToString("0") + " -> " + yawB.ToString("0") + " err " + bm.DriveErrNow.ToString("0"));
            Check(Mathf.Abs(bm.DriveErrNow) < 6f, "a held 40 deg stick turns the machine TO the heading, within 6 deg by 2.4 s (" + bm.DriveErrNow.ToString("0") + " deg left)");
            Check(peakW > 25f && peakW < 150f, "...briskly, and without breaking grip into a spin (peak " + peakW.ToString("0") + " deg/s; a spin measured 200-290)");
            Check(Mathf.Abs(Mathf.DeltaAngle(yawA, yawB)) < 12f, "...and stops turning once there, apart from the camera's slow recentre (" + Mathf.DeltaAngle(yawA, yawB).ToString("0") + " deg in 0.8 s)");
            Check(Mathf.DeltaAngle(camYaw1, yawA) > 15f, "...to the RIGHT of where the camera looked (" + Mathf.DeltaAngle(camYaw1, yawA).ToString("0") + " deg)");
            // let go: it coasts, no steer input
            Phase0Input.debugThrottle = 0f; Phase0Input.debugSteer = 0f;
            for (int i = 0; i < 30; i++) yield return null;
            Check(bm.testDrive.aiThrottle <= 0f && Mathf.Abs(bm.MapSteerNow) < 0.05f, "let go and the throttle is off (braking while rolling), the steer centred (" + bm.testDrive.aiThrottle.ToString("0.00") + ", " + bm.MapSteerNow.ToString("0.00") + ")");
            Check(BuilderManager.StickNow() == Vector2.zero, "...a centred stick is zero (the touch layer's 0.001 sentinel included)");
            // straight back: reverse - FROM REST (a fixed 60-frame wait pressed
            // back while still coasting at speed, and 0.75 s of braking read as
            // "does not back up"; the check's premise, not the drive)
            float restBy = Time.time + 6f;
            while (VelUtil.GetLinearVelocity(bm.testRobot.rb).magnitude > 0.3f && Time.time < restBy) yield return null;
            Check(VelUtil.GetLinearVelocity(bm.testRobot.rb).magnitude <= 0.3f, "released, the machine brakes to rest inside 6 s (" + VelUtil.GetLinearVelocity(bm.testRobot.rb).magnitude.ToString("0.0") + " m/s)");
            Vector3 fwdR = bm.testRobot.transform.TransformDirection(bm.TestDriveDir); fwdR.y = 0f;
            Vector3 posR = bm.testRobot.rb.position;
            Phase0Input.debugThrottle = -1f; Phase0Input.debugSteer = 0f;
            for (int i = 0; i < 45; i++) yield return null;
            Check(bm.DriveReversing, "stick straight back from rest = reverse");
            float backed = Vector3.Dot(bm.testRobot.rb.position - posR, fwdR.normalized);
            Check(backed < -0.5f, "...and the machine backs up (" + backed.ToString("0.0") + " m along its nose)");
            Phase0Input.debugThrottle = 0f; Phase0Input.debugSteer = 0f;
            Check(TouchControls.RANGE >= 130f && Mathf.Approximately(TouchControls.RING, 2f * TouchControls.RANGE) && Mathf.Approximately(TouchControls.KNOB_TRAVEL, TouchControls.RANGE) && TouchControls.MouseAccepted,
                  "the stick is big and honest: the ring's edge is full lock (travel " + TouchControls.RANGE + ", ring " + TouchControls.RING + "), and it takes a mouse on every platform");
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
            Check(d.yardStep == BuilderManager.STEP_EXPLORE, "...the first minute is done and the save says so (step " + d.yardStep + ")");
            if (MobileBuilderUI.inst != null) Check(MobileBuilderUI.inst.Tab == 3 && MobileBuilderUI.inst.DockOpen, "...on the SHOP tab, dock open (tab " + MobileBuilderUI.inst.Tab + ")");
            bm.EnterMap(); for (int i = 0; i < 8; i++) yield return null;
            Check(MapHudUI.inst != null && MapHudUI.inst.ObjectiveText == "" && !bm.ObjectiveMarkerShown, "...no objective line, no marker: the world is yours");
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
            Vector3 chalAt = bm.testRobot.rb.position;
            Check(MapHudUI.inst != null && MapHudUI.inst.CardShown, "the card is up before CHALLENGE");
            MapHudUI.inst.ChallengeButton.onClick.Invoke();   // the BUTTON, not the seam
            yield return null; yield return null;
            var fm = Object.FindFirstObjectByType<FightManager>();
            Check(bm.mode == BuilderManager.Mode.Fight && fm != null, "CHALLENGE enters a fight");
            Check(FightManager.quickBout && Career.quickFight, "...a Quick bout, settled as a quick fight");
            // owen, 2026-09-10: "replace auto fight with manual fight" - the
            // player's side is the stick, and no program sits on the robot.
            Check(fm != null && fm.playerSource == ControlSource.AI && bm.YardStickFight, "...with YOU at the stick, on the AI channel, not a program (" + (fm != null ? fm.playerSource.ToString() : "-") + ")");
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
            Check(bm.testRobot != null && bm.testRobot.controlSource == ControlSource.AI, "...and at the bell your machine is on the AI channel the stick feeds (" + (bm.testRobot != null ? bm.testRobot.controlSource.ToString() : "-") + ")");
            Vector3 f0 = bm.testRobot != null ? bm.testRobot.rb.position : Vector3.zero;
            Phase0Input.debugThrottle = 1f; Phase0Input.debugSteer = 0f;   // "up" = away from the fight camera, which looks from the side: a turn first
            var ftr = new System.Text.StringBuilder(); float fErrLate = 999f;
            for (int i = 0; i < 90; i++)
            {
                if (i >= 45 && bm.testRobot != null) fErrLate = Mathf.Min(fErrLate, Mathf.Abs(bm.DriveErrNow));   // the SCOUT rams at the bell; the best of the last 0.75 s
                if (i % 15 == 0 && bm.testRobot != null)
                    ftr.Append((i / 60f).ToString("0.00")).Append("s src ").Append(bm.testRobot.controlSource).Append(" err ").Append(bm.DriveErrNow.ToString("0")).Append(" st ").Append(bm.testDrive.aiSteer.ToString("0.00")).Append(" thr ").Append(bm.testDrive.aiThrottle.ToString("0.00")).Append(" w ").Append((bm.testRobot.rb.angularVelocity.y * Mathf.Rad2Deg).ToString("0")).Append(" v ").Append(VelUtil.GetLinearVelocity(bm.testRobot.rb).magnitude.ToString("0.0")).Append(" up ").Append(Vector3.Dot(bm.testRobot.transform.up, Vector3.up).ToString("0.00")).Append(bm.aiRobot != null ? " enemy " + Vector3.Distance(bm.aiRobot.rb.position, bm.testRobot.rb.position).ToString("0.0") + " m" : "").Append(" | ");
                yield return null;
            }
            log.Add("      fight trace: " + ftr);
            float drove = bm.testRobot != null ? Vector3.Distance(bm.testRobot.rb.position, f0) : 0f;
            Phase0Input.debugThrottle = 0f;
            Check(drove > 1.0f, "a held stick drives your machine in the ring (" + drove.ToString("0.0") + " m in 1.5 s)");
            Check(fErrLate < 30f, "...toward the stick's heading, camera-relative, as on the map (best " + fErrLate.ToString("0") + " deg off in the last 0.75 s)");
            Time.timeScale = 4f;
            float deadline = Time.realtimeSinceStartup + 40f;
            while (fm != null && fm.state != FightManager.State.Ended && Time.realtimeSinceStartup < deadline) yield return null;
            Time.timeScale = savedScale;
            Check(fm != null && fm.state == FightManager.State.Ended, "the bout ended on its own");
            Check(d.quickFights == 1, "...and settled once (quickFights=" + d.quickFights + ")");
            // the debrief's loud button is CONTINUE EXPLORING: back to the map,
            // where the challenge began (owen, 2026-09-10)
            Check(FightManager.quickNextLabel.StartsWith("CONTINUE EXPLORING") && FightManager.quickNext != null, "the debrief's loud button reads CONTINUE EXPLORING (" + FightManager.quickNextLabel + ")");
            FightManager.quickNext(bm); yield return null; yield return null;
            Vector3 backAt = bm.testRobot != null ? bm.testRobot.rb.position : Vector3.zero;
            Check(bm.mode == BuilderManager.Mode.Map && Object.FindFirstObjectByType<FightManager>() == null, "...and it puts you back on the map (" + bm.mode + ")");
            Check(Vector2.Distance(new Vector2(backAt.x, backAt.z), new Vector2(chalAt.x, chalAt.z)) < 6f, "...where the challenge began, not at home (" + Vector2.Distance(new Vector2(backAt.x, backAt.z), new Vector2(chalAt.x, chalAt.z)).ToString("0.0") + " m off)");
            bm.LeaveMap(); yield return null;
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
