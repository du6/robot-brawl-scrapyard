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

        // ---- the map HUD's sizing (section 4b) ---------------------------------

        /// <summary>The smallest label anywhere in the HUD, hidden panels
        /// included - a panel nobody has opened yet is still shipped.</summary>
        static int SmallestLabel(GameObject hud, out string who)
        {
            who = ""; int worst = int.MaxValue;
            if (hud == null) return 0;
            foreach (var t in hud.GetComponentsInChildren<Text>(true))
            {
                if (t.fontSize >= worst) continue;
                worst = t.fontSize; who = t.transform.parent != null ? t.transform.parent.name + "/" + t.name : t.name;
            }
            return worst == int.MaxValue ? 0 : worst;
        }

        /// <summary>Every control a finger is asked to hit, shorter than one
        /// touch row. Walks the live subtree rather than a named list: a named
        /// list is how the dock's 44 pt check ended up covering six controls
        /// and missing four whole tabs (CLAUDE.md).</summary>
        static string ShortControls(RectTransform panel, float row)
        {
            if (panel == null) return "no panel";
            var bad = new List<string>();
            foreach (var s in panel.GetComponentsInChildren<Selectable>(true))
            {
                var rt = s.GetComponent<RectTransform>();
                if (rt == null || rt.rect.height < 1f) continue;
                if (rt.rect.height < row - 1f) bad.Add(rt.name + " " + rt.rect.height.ToString("0") + "u");
            }
            return bad.Count == 0 ? "" : string.Join(", ", bad.ToArray());
        }

        /// <summary>A child's box in an ancestor's own space. Includes the
        /// child's descendants, deliberately - a label running out of its panel
        /// is what a player sees.</summary>
        static Rect In(RectTransform outer, RectTransform inner)
        {
            var b = RectTransformUtility.CalculateRelativeRectTransformBounds(outer, inner);
            return new Rect(b.min.x, b.min.y, b.size.x, b.size.y);
        }

        static bool Overlap(Rect a, Rect b)
        {
            float w = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
            float h = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
            return w > 1f && h > 1f;
        }

        /// <summary>Finding 5: the encounter card's sub-line inside its card.
        ///
        /// ⚠ Text.preferredWidth is the UNWRAPPED width - Unity forces Overflow
        /// inside GetPreferredWidth - so comparing it to the box and calling a
        /// smaller number a pass would be measuring the wrong thing. The honest
        /// invariant is "either it wraps, or it fits on one line", and that is
        /// exactly what failed: Overflow, 485 units of text in a 404-unit box.
        /// preferredHeight IS taken at the rect's width with wrapping on, so it
        /// is the right number for "did the card grow to hold it".</summary>
        static void CardWraps(MapHudUI hud, string tag)
        {
            var subRt = hud.CardSubRect; var cardRt = hud.CardRect;
            if (subRt == null || cardRt == null) { Check(false, "card sub-line " + tag + ": no card"); return; }
            float oneLine = hud.CardSubWidthUnits, box = subRt.rect.width, drawn = hud.CardSubHeightUnits;
            Check(hud.CardSubWraps || oneLine <= box + 1f,
                  "card sub-line, " + tag + ": it wraps, or it fits on one line (one line is "
                  + oneLine.ToString("0") + " units in a " + box.ToString("0") + "-unit box, wraps=" + hud.CardSubWraps + ")");
            Check(drawn <= subRt.rect.height + 1f,
                  "...and the card grew for the wrapped text (" + drawn.ToString("0")
                  + " of " + subRt.rect.height.ToString("0") + " units)");
            var chR = In(cardRt, hud.ChallengeButton.GetComponent<RectTransform>());
            var sR = In(cardRt, subRt);
            Check(sR.yMin >= chR.yMax - 1f, "...and it stays clear of CHALLENGE ("
                  + sR.yMin.ToString("0.0") + " vs " + chR.yMax.ToString("0.0") + ")");
        }

        /// <summary>The three invariants findings 2-4 broke, measured off the
        /// live rects at whatever metrics are in force.</summary>
        static void HudSizing(MapHudUI hud, GameObject hudGo, string tag)
        {
            if (hud == null || hudGo == null) { Check(false, "map HUD sizing " + tag + ": no HUD"); return; }
            float row = hud.RowUnits;
            int floor = hud.TypeFloorUnits;
            string tiny; int small = SmallestLabel(hudGo, out tiny);
            // FINDING 2. The floor is the dock's own: DesktopFontUnits clamps at
            // 14 CSS px, and TypeFloorUnits is that clamp in this canvas's units.
            Check(small >= floor, "map HUD type, " + tag + ": every label is at or above the dock's floor ("
                  + small + " units at " + tiny + ", floor " + floor + ")");
            // FINDING 3. The sign-in fields and the board's buttons were built
            // once from the ROW literal and never re-sized.
            string shortSign = ShortControls(hud.SignInPanel, row);
            Check(shortSign == "", "map HUD sign-in, " + tag + ": every control is a full touch row ("
                  + row.ToString("0") + " units)" + (shortSign == "" ? "" : " - " + shortSign));
            string shortBoard = ShortControls(hud.BoardPanel, row);
            Check(shortBoard == "", "map HUD board, " + tag + ": every control is a full touch row ("
                  + row.ToString("0") + " units)" + (shortBoard == "" ? "" : " - " + shortBoard));
            // ...and a panel that grows with the row must still fit the screen,
            // or the fix for finding 3 would trade a small button for a
            // SIGN IN button below the bottom edge.
            var safe = hud.SafeRoot;
            var signR = In(safe, hud.SignInPanel); var boardR = In(safe, hud.BoardPanel);
            var safeR = safe.rect;
            Check(signR.height <= safeR.height + 1f && signR.width <= safeR.width + 1f,
                  "...and the sign-in panel still fits the safe area (" + signR.width.ToString("0") + "x" + signR.height.ToString("0")
                  + " in " + safeR.width.ToString("0") + "x" + safeR.height.ToString("0") + ")");
            Check(boardR.height <= safeR.height + 1f && boardR.width <= safeR.width + 1f,
                  "...and so does the board (" + boardR.width.ToString("0") + "x" + boardR.height.ToString("0") + ")");
            // FINDING 4. The objective's Y was the ROW literal while the bar's
            // height was recomputed and can only grow, so at a higher reported
            // pixel ratio the compass bar came down over the one line that tells
            // a new player what to do.
            var barR = In(safe, hud.BarRect); var objR = In(safe, hud.ObjectiveRect);
            Check(objR.yMax <= barR.yMin + 0.5f, "map HUD objective, " + tag + ": the line clears the compass bar (top "
                  + objR.yMax.ToString("0.0") + " vs bar bottom " + barR.yMin.ToString("0.0") + ")");
        }

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

            // ---- 1b. THE FUNNEL'S MISSING MIDDLE -------------------------------
            // ⚠ `map` FIRES ON BOOT, NOT ON AN ACTION (PumpBootToYard), and the
            // next event needed a chest driven into. So the live funnel could not
            // tell "never touched the controls" from "drove and found nothing" -
            // the two answers that would have led to completely different fixes.
            // The CONTROL LEG is the point of this block: `moved` and `roam` must
            // be ABSENT before anyone drives, or they are measuring the boot and
            // would read 100% forever.
            Check(!RBTelemetry.Has(RBTelemetry.MOVED),
                  "arriving in the world is NOT `moved` — nobody has touched the stick yet");
            Check(!RBTelemetry.Has(RBTelemetry.ROAM),
                  "...nor `roam` — nobody has travelled yet");
            Check(bm.TestRoamMetres < 0.5f,
                  "...and the odometer starts at zero (" + bm.TestRoamMetres.ToString("0.0") + " m)");

            // THE KEYBOARD HINT, BOTH LEGS. A desktop visitor was handed a phone
            // joystick and no word that WASD drives - 5 of the 8 desktop sessions
            // that reached the world on 2026-09-17 never touched a control. The
            // hint answers that, and it must answer it ONLY there: on a finger it
            // is noise over the one control that does work.
            // Forced metrics come in PAIRS - forcedCoarsePointer is only read
            // when forcedPixelRatio is set (ReadBrowserMetrics) - so a bench that
            // sets one and not the other silently measures the default.
            TouchControls.TestResetDriven();
            MobileBuilderUI.forcedPixelRatio = 1f;
            MobileBuilderUI.forcedCoarsePointer = false;
            Check(TouchControls.KeyHintWanted,
                  "a fine pointer that has never driven is offered the keys");
            MobileBuilderUI.forcedCoarsePointer = true;
            Check(!TouchControls.KeyHintWanted,
                  "...and a finger is not - the stick is already the answer there");
            MobileBuilderUI.forcedCoarsePointer = false;
            TouchControls.everDriven = true;
            Check(!TouchControls.KeyHintWanted,
                  "...nor is a desktop player who has already driven once");
            TouchControls.TestResetDriven();
            MobileBuilderUI.ClearForcedMetrics();

            // The positive half is asserted after the bench drives to a crate
            // below - it rides the real gameplay path rather than a drive staged
            // for the test, and driving here would open the first chest and
            // invalidate the five checks that follow about a pristine world.
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
            // owen, 2026-09-12: the chest beam drew as a matte rod ("a vertical bar
            // on top of every item") because emissive needs a bloom nothing does.
            // It is a light shaft now: additive, fading out with height.
            Check(Shader.Find("Scrapyard/Beacon") != null, "the beacon shader exists");
            var beamMat = bm.BeaconMat(false);
            Check(beamMat != null && beamMat.shader.name == "Scrapyard/Beacon", "...and the chest's beam uses it (" + (beamMat != null ? beamMat.shader.name : "-") + ")");
            Check(beamMat != null && beamMat.GetTag("Queue", false, "") != "Geometry", "...drawn as transparent light, not solid geometry");
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
            yield return null;   // the originals' renderers are destroyed at end of frame
            int scatterR = 0;
            foreach (var mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                if (mr.transform.name.StartsWith("scatter_") || (mr.transform.parent != null && mr.transform.parent.name.StartsWith("scatter_"))) scatterR++;
            Check(scatterR == bm.YardScatterDraws && scatterR <= bm.WorldChunksLoaded * 4, "...combined into a few draws per chunk that move with the world, not static batches (" + scatterR + " renderers for " + bm.YardScatterLoaded + " props)");
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

            // ---- the funnel's missing middle, measured on a REAL drive --------
            // ⚠ THIS BELONGS HERE AND NOWHERE EARLIER. The crate section
            // TELEPORTS the machine onto a chest (TeleportPlayer), so it drives
            // nothing: asserting there read 0.0 m and failed, correctly. The
            // odometer deliberately ignores teleport-sized jumps, because a
            // respawn is not distance a player drove. By this line the bench has
            // held the stick up for two seconds, turned, and reversed.
            Check(RBTelemetry.Has(RBTelemetry.MOVED), "a real drive fires `moved`");
            Check(bm.TestRoamMetres >= BuilderManager.ROAM_METRES,
                  "...and the odometer counts only driven metres (" + bm.TestRoamMetres.ToString("0.0")
                  + " m, threshold " + BuilderManager.ROAM_METRES + ")");
            Check(RBTelemetry.Has(RBTelemetry.ROAM), "...so `roam` fires once the machine has gone somewhere");
            // The hint retires off the SAME line that raises `moved`, so these two
            // can never disagree about what counts as driving.
            Check(TouchControls.everDriven, "...and that same drive retires the keyboard hint");

            // ⚠ THE OLD CHECK ASSERTED `RANGE >= 130`, A NUMBER IN A UNIT THAT
            // NO LONGER MEANS WHAT IT DID. It was written when one unit was one
            // framebuffer pixel; after GuiScale was fixed, one unit is one CSS
            // pixel, so 130 units went from a sensible stick to a ring covering
            // 60% of a phone. The check passed the whole way through, because it
            // was measuring the number and not the thing. Assert the PHYSICAL
            // size and the invariants instead.
            float ringCss = TouchControls.RING;                       // GuiScale units == CSS px on the web
            float shortEdge = Mathf.Min(Screen.width, Screen.height) / Mathf.Max(0.01f, BuilderManager.GuiScale);
            Check(ringCss >= TouchControls.RING_MIN - 0.5f && ringCss <= TouchControls.RING_MAX + 0.5f,
                  "the stick's ring stays inside its bounds (" + ringCss.ToString("0") + " in ["
                  + TouchControls.RING_MIN + ".." + TouchControls.RING_MAX + "])");
            Check(shortEdge < 1f || ringCss <= shortEdge * 0.42f,
                  "...and never eats the screen it steers (" + (100f * ringCss / Mathf.Max(1f, shortEdge)).ToString("0")
                  + "% of the short edge, was 65% on owen's phone)");
            Check(Mathf.Approximately(TouchControls.RING, 2f * TouchControls.RANGE)
                  && Mathf.Approximately(TouchControls.KNOB_TRAVEL, TouchControls.RANGE) && TouchControls.MouseAccepted,
                  "...the ring's edge is still full lock, and it still takes a mouse on every platform");
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
            Check(d.yardStep == BuilderManager.STEP_CHALLENGE, "...visiting the shop does not skip the first bout (step " + d.yardStep + ")");
            if (MobileBuilderUI.inst != null) Check(MobileBuilderUI.inst.Tab == 3 && MobileBuilderUI.inst.DockOpen, "...on the SHOP tab, dock open (tab " + MobileBuilderUI.inst.Tab + ")");
            bm.EnterMap(); for (int i = 0; i < 8; i++) yield return null;
            Check(MapHudUI.inst != null && MapHudUI.inst.ObjectiveText.Contains("CHALLENGE") && bm.ObjectiveMarkerShown, "...the pending fight remains marked after the shop");
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
            // ---- 7. OTHER PLAYERS (CrazyGames plan, step 3) --------------------------
            // A stranger's machine from the pool parks in the world, the card
            // names the owner, CHALLENGE is the game's one sign-in gate, the
            // bout is fought against THEIR build and the verdict is reported.
            // Headless: the pool is injected (no server), the token is faked,
            // and the report's intent is what is measured.
            var visitor = new LadderClient.PoolEntry { snapshotId = "11111111-1111-1111-1111-111111111111", robotName = "VISITOR", owner = "someone",
                                                       build = BuilderManager.STARTER_SNAPSHOT, category = "LIGHT", game = "scrapyard" };
            bm.TestInjectPool(new List<LadderClient.PoolEntry> { visitor });
            var vparts = bm.SnapshotParts(visitor.build);
            Check(bm.PoolCount == 1 && vparts != null && vparts.Count >= 5 && vparts[0].def.id == "core", "a stranger's build parses into parts, core first (" + (vparts != null ? vparts.Count : 0) + ")");
            Check(bm.SnapshotParts("core|0,0.7,0|0|0,0,0|Aluminum") == null && bm.SnapshotParts("") == null, "...a build with no wheel, or nothing, is refused");
            var pv = bm.TestParkPool(visitor, new Vector3(hp.x - 12f, 0f, hp.z + 12f));
            Check(pv != null && pv.name == "VISITOR", "...and it parks in the world under its name");
            bm.TeleportPlayer(pv.rb.position + new Vector3(2.5f, 0f, 0f));
            yield return null; yield return null; yield return null;
            Check(bm.YardCardShown && bm.CardPoolEntry == visitor, "the card comes up for the stranger's machine");
            Check(MapHudUI.inst.CardTitle.Contains("VISITOR") && MapHudUI.inst.CardTitle.Contains("by someone"), "...naming the machine and its owner (" + MapHudUI.inst.CardTitle + ")");
            string tokenSave = LadderClient.Token; LadderClient.Token = "";
            MapHudUI.inst.ChallengeButton.onClick.Invoke(); yield return null; yield return null;
            Check(bm.mode == BuilderManager.Mode.Map && MapHudUI.inst.SignInShown, "CHALLENGE without an account asks you to sign in - the game's one gate");
            Check(Object.FindFirstObjectByType<FightManager>() == null, "...and no fight starts");
            MapHudUI.inst.HideSignIn();
            LadderClient.Token = "bench-token";
            MapHudUI.inst.ChallengeButton.onClick.Invoke(); yield return null; yield return null;
            var fmp = Object.FindFirstObjectByType<FightManager>();
            Check(bm.mode == BuilderManager.Mode.Fight && fmp != null && bm.aiRobot != null && bm.aiRobot.name == "VISITOR", "signed in, CHALLENGE fights the stranger's own build (" + (bm.aiRobot != null ? bm.aiRobot.name : "-") + ")");
            Check(fmp != null && fmp.enemyName == "VISITOR" && fmp.playerSource == ControlSource.AI && bm.YardStickFight, "...named on the fight, driven from the stick");
            Time.timeScale = 4f;
            float pdeadline = Time.realtimeSinceStartup + 40f;
            while (fmp != null && fmp.state != FightManager.State.Ended && Time.realtimeSinceStartup < pdeadline) yield return null;
            yield return null; yield return null;
            Time.timeScale = savedScale;
            Check(fmp != null && fmp.state == FightManager.State.Ended, "the bout ends");
            Check(bm.LastBoutPosted == visitor.snapshotId, "...and the verdict is reported for the stranger's snapshot (" + bm.LastBoutPosted + ")");
            Check(fmp != null && bm.LastBoutWon == (fmp.outcome == FightManager.Outcome.PlayerWin), "...as the referee called it (" + (fmp != null ? fmp.outcome.ToString() : "-") + ")");
            bm.BackToBuild(); yield return null;
            LadderClient.Token = tokenSave;
            bm.EnterMap(); yield return null; yield return null;
            MapHudUI.inst.BoardButton.onClick.Invoke(); yield return null;
            Check(MapHudUI.inst.BoardShown, "BOARD opens the yard board");
            float bdead = Time.realtimeSinceStartup + 6f;
            while (Time.realtimeSinceStartup < bdead && MapHudUI.inst.BoardText == "reading the board...") yield return null;
            Check(MapHudUI.inst.BoardText.Length > 0 && MapHudUI.inst.BoardText != "reading the board...", "...and says what it can (offline here: " + MapHudUI.inst.BoardText.Split('\n')[0] + ")");
            MapHudUI.inst.HideBoard();

            // owen, 2026-09-12: "it asked me to sign in to get on the board, but
            // where is the sign in button". The board asked for something only a
            // challenge could give. Signed out, the board carries its own.
            string tokenHold = LadderClient.Token; LadderClient.Token = null;
            MapHudUI.inst.BoardButton.onClick.Invoke(); yield return null;
            Check(MapHudUI.inst.BoardSignInShown, "signed out, the board offers SIGN IN");
            MapHudUI.inst.BoardSignInButton.onClick.Invoke(); yield return null;   // the BUTTON, not the seam
            Check(MapHudUI.inst.SignInShown && !MapHudUI.inst.BoardShown, "...and it opens the sign-in panel over the board");
            Check(MapHudUI.inst.SignInTitle.Contains("BOARD"), "...worded for the board, not for a challenge (" + MapHudUI.inst.SignInTitle + ")");
            MapHudUI.inst.SignInCancelButton.onClick.Invoke(); yield return null;
            Check(!MapHudUI.inst.SignInShown && MapHudUI.inst.BoardShown, "...and NOT NOW puts you back on the board, not on the map");
            MapHudUI.inst.HideBoard();
            // this leg needs a token of its OWN: tokenHold is whatever the bench
            // started with, which headless is nothing, so restoring it would test
            // the signed-OUT board twice and read as a product failure.
            LadderClient.Token = "bench-session-token";
            MapHudUI.inst.BoardButton.onClick.Invoke(); yield return null;
            Check(!MapHudUI.inst.BoardSignInShown, "signed in, the board does not ask again");
            MapHudUI.inst.HideBoard();
            LadderClient.Token = tokenHold;

            // ---- 4b. THE HUD IS SIZED BY THE DOCK'S RULE, AND RE-SIZED -------------
            //
            // Four mobile defects reached a tester on 2026-09-13 and every check
            // in this file stayed green through all of them, because three of
            // the four are one mistake: the HUD's literals (ROW = 44 units, a
            // fontSize per label) are REFERENCE numbers at 1280x720 and were
            // written straight into the live UI. On a 932x430 CSS-pt landscape
            // phone one canvas unit is 0.659 CSS px, so 44 units is 29 CSS px -
            // 66% of the 44 pt touch floor - and the labels landed between 8.6
            // and 12.5 CSS px against the dock's 14 px floor.
            //
            // CONTROL LEG, run 2026-09-13 rather than assumed: ApplyMetrics was
            // temporarily replaced with the pre-fix sizing (the bar's row
            // recomputed, everything else from the ROW literal and the literal
            // fontSizes) and this bench went 176/0 -> 168/8. What failed:
            //   type       13 units at card/sub against a floor of 25
            //   sign-in    email/password/name/login/create/cancel all 44u,
            //              needing 124u on this screen
            //   board      close and board_signin both 44u
            //   objective  top 357.7 against a bar bottom of 283.3 — the bar
            //              came down 74 units OVER the line
            //   re-apply   13 -> 13 units: the type never re-ran at all
            // Two stayed green in the control and should have: the bar's own
            // row WAS already recomputed (the half that worked), and the type
            // floor under forced metrics is 8, which 13 clears.
            //
            // Measured on this headless screen (640x480, scaleFactor 0.577):
            // the dock's row is 90 units against the literal ROW = 44, and its
            // type floor is 25 units against literals of 13 to 22.
            var hudGo = GameObject.Find("map_hud_canvas");
            Check(hudGo != null, "the map HUD's canvas is up for the sizing checks");
            HudSizing(MapHudUI.inst, hudGo, "at this screen's metrics");
            string tinyBefore; int unitsBefore = SmallestLabel(hudGo, out tinyBefore);

            // ...and again on owen's phone. The constants are the device's, not
            // round numbers: 2532x1170 at 460 dpi against a 1280x720 reference
            // at match 0.5 is scaleFactor 1.793, so TouchRow clamps to 110 units
            // and FontUnits is pt * (460/163) / 1.793 = pt * 1.574. The phone is
            // not really the point - the point is that NOTHING RE-RAN when the
            // metrics moved, which is findings 2, 3 and 4 in one sentence.
            try
            {
                MobileBuilderUI.forcedRowUnits = 110f;
                MobileBuilderUI.forcedFontScale = 1.574f;
                yield return null; yield return null;
                Check(MapHudUI.inst.RowUnits > 109f, "the HUD picks up a changed touch row with no rebuild (" + MapHudUI.inst.RowUnits.ToString("0") + " units)");
                string tinyAfter; int unitsAfter = SmallestLabel(hudGo, out tinyAfter);
                // CHANGED, not larger. This check first read `>` and failed at
                // 25 -> 20, and the check was the thing that was wrong: canvas
                // units are not a size. The headless canvas is 640x480, so
                // scaleFactor is 0.577 and one point is 1.73 units; the phone's
                // is 1.793, so one point is 1.574 units. The phone's type is
                // FEWER units and MORE millimetres. The invariant is that the
                // HUD re-ran at all - a direction would be asserting which of
                // two screens is bigger, which is not what broke.
                Check(unitsAfter != unitsBefore, "...and re-applies the type with it (" + unitsBefore + " -> " + unitsAfter + " units)");
                HudSizing(MapHudUI.inst, hudGo, "on owen's phone");
            }
            finally
            {
                MobileBuilderUI.ClearForcedMetrics();
            }
            yield return null; yield return null;
            Check(Mathf.Abs(MapHudUI.inst.RowUnits - 110f) > 0.5f, "...and back to the real screen when the forcing is released (" + MapHudUI.inst.RowUnits.ToString("0") + " units)");

            // CONTROL LEG for sections 4c-4f, run 2026-09-13 rather than
            // assumed: the four fixes were backed out of MapHudUI together (the
            // gate off, the sub-line back to Overflow with a literal card
            // height, the board asking 20 while sized for 8 with "you:" back in
            // the truncating body, and Screen.safeArea back in place). This
            // bench went 200/0 -> 190/10. What failed, and it is every one of
            // the four:
            //   gate    objective and toast still shown behind BOTH modals
            //   card    925 units of text in a 404-unit box (707 at phone
            //           type), wraps=False - and note the type fix makes the
            //           string WIDER, so the two fixes only work together
            //   board   asked 20, fits 8; the "you:" line gone entirely;
            //           9 lines drawn into a box that holds 8
            //   notch   root not inset at all (1109 of 1109 units), GARAGE 228
            //           units into the cut-out, CHALLENGE 83 below the floor
            // TWO PASSED IN THE CONTROL AND SHOULD HAVE, which is worth knowing
            // before anyone reads them as cover: "the card grew for the wrapped
            // text" and "it stays clear of CHALLENGE" both pass against the OLD
            // code, because with Overflow the preferred height IS one line.
            // They guard the new mechanism; the wrap check is the one that
            // catches the original defect.

            // ---- 4c. THE MAP LINES ARE GATED BEHIND THE MODALS --------------------
            //
            // A REGRESSION THIS BENCH'S OWN FIX CAUSED, which is why the check
            // is here. Growing the bar's row pushed the objective, toast and
            // banner down into the two centred modals - measured by the phone
            // bench: objective x board_panel 440 x 10.5 units, objective x
            // signin 524 x 19.4, and with a live toast up 440 x 40.2 and
            // 604.9 x 40.2. Both modals SetAsLastSibling, so they draw on top
            // and slice the amber line mid-glyph, which reads as a z-order bug.
            //
            // ASSERT HIDDEN, NOT NON-OVERLAPPING. A non-overlap assertion is
            // satisfied by nudging the objective twenty units, which leaves the
            // toast exactly where it was and lets the next metrics change put
            // it back. The three lines are hidden together or the gate is not
            // a gate.
            var hud2 = MapHudUI.inst;
            bm.TestYardToast("TREASURE  ·  3 scrap  ·  a spike", 30f);
            yield return null; yield return null;
            Check(hud2.ToastShown, "a live toast is up on the open map (control: the gate below has something to hide)");
            Check(hud2.ObjectiveShown, "...and so is the objective line");
            hud2.BoardButton.onClick.Invoke(); yield return null; yield return null;   // the BUTTON, not the seam
            Check(hud2.BoardShown, "BOARD opens over the map");
            Check(!hud2.ObjectiveShown && !hud2.ToastShown && !hud2.BannerShown,
                  "...and the objective, toast and banner are HIDDEN behind it, not moved (obj " + hud2.ObjectiveShown
                  + " toast " + hud2.ToastShown + " banner " + hud2.BannerShown + ")");
            hud2.HideBoard(); yield return null; yield return null;
            Check(hud2.ObjectiveShown && hud2.ToastShown, "...and both come back when the board closes");
            hud2.ShowSignIn(); yield return null; yield return null;
            Check(hud2.SignInShown && !hud2.ObjectiveShown && !hud2.ToastShown && !hud2.BannerShown,
                  "the sign-in panel hides all three the same way");
            hud2.SignInCancelButton.onClick.Invoke(); yield return null; yield return null;
            Check(hud2.ObjectiveShown && hud2.ToastShown, "...and NOT NOW brings them back");
            bm.TestYardToast("", 0f); yield return null;

            // ---- 4d. THE ENCOUNTER CARD'S SUB-LINE WRAPS INSIDE THE CARD ----------
            //
            // Finding 5. MkText defaults to HorizontalWrapMode.Overflow and the
            // card never changed it, so "another player's machine · 30-second
            // bout, you drive · points at the bell" (77 characters) measured
            // ~485 units in a 404-unit box and spilled ~40 units past each edge
            // of the dark panel onto lit terrain. Scaling the type up for
            // finding 2 makes the same string WIDER, so the two fixes only work
            // together - which is why this is measured at BOTH type scales.
            // Destroy() is deferred to the end of the frame, so YardParked can
            // still hand back a pool visitor that is on its way out. Let the
            // unpark land before asking who is parked.
            bm.TestUnparkPool();
            for (int i = 0; i < 4; i++) yield return null;
            var parkedC = bm.YardParked;
            if (parkedC != null)
            {
                bm.TeleportPlayer(parkedC.rb.position + new Vector3(2.5f, 0f, 0f));
                for (int i = 0; i < 6; i++) yield return null;
                Check(hud2.CardShown, "the encounter card is up for the wrap checks");
                CardWraps(hud2, "at this screen's metrics");
                try
                {
                    MobileBuilderUI.forcedRowUnits = 110f;
                    MobileBuilderUI.forcedFontScale = 1.574f;
                    yield return null; yield return null;
                    CardWraps(hud2, "on owen's phone");
                }
                finally { MobileBuilderUI.ClearForcedMetrics(); }
                yield return null; yield return null;
                // Stay AT the encounter: section 4f needs the card up to
                // measure CHALLENGE against the home indicator. The first run
                // of 4f did this check behind `if (CardShown)` from home, so it
                // silently drew no conclusion and the total still read green -
                // which is the "skipped is not passed" trap in CLAUDE.md.
            }

            // ---- 4e. THE BOARD ASKS FOR WHAT IT CAN SHOW, AND PINS YOUR ROW -------
            //
            // Finding 12, and it got worse before it got better: the panel was
            // sized for exactly 8 body lines while ShowBoard asked for 20, and
            // the body is verticalOverflow Truncate - so twelve rows were
            // fetched and thrown away, and the "you: rank N" line, appended
            // LAST to that same string, was the FIRST thing cut. On the one
            // screen whose purpose is showing a player where they stand.
            hud2.BoardButton.onClick.Invoke(); yield return null; yield return null;
            // The 8 is not a product number any more - the product has none.
            // It is here as a floor on THIS screen: whatever the derivation
            // gives, it must not be worse than the eight the old literal drew.
            Check(hud2.BoardRowsAsked == hud2.BoardRowsFit && hud2.BoardRowsFit >= 8,
                  "the board asks for exactly what its body can draw, and no fewer than the old literal 8 (asked "
                  + hud2.BoardRowsAsked + ", fits " + hud2.BoardRowsFit + ")");
            var bodyRt = hud2.BoardPanel.Find("body") as RectTransform;
            // Hand it MORE rows than fit, which is the case the server produces
            // and the offline bench never reaches. RenderBoard is the only part
            // a live server would have supplied; the panel, its buttons and its
            // sizes all still came from the BOARD button above.
            var many = new List<LadderClient.YardRow>();
            for (int i = 1; i <= hud2.BoardRowsFit + 9; i++)
                many.Add(new LadderClient.YardRow { rank = i, owner = "player" + i, points = 900 - i * 7, wins = i, bouts = i + 3 });
            var mine = new LadderClient.YardRow { rank = 41, owner = "owen", points = 128, wins = 5, bouts = 9 };
            // ⚠ WAIT FOR THE REAL REQUEST TO LAND FIRST. ShowBoard's own
            // YardBoard coroutine is still in flight; offline it fails after a
            // moment and calls RenderBoard(null, ...), which hides the me-row.
            // Injecting before that lands leaves TWO writers on one panel and
            // the loser is whichever the scheduler runs second - this check
            // failed once with the right text and the row hidden, which is
            // exactly that race and not a product fault.
            float rdead = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < rdead && hud2.BoardText == "reading the board...") yield return null;
            yield return null; yield return null;
            hud2.RenderBoard(many, mine, null);
            yield return null;
            // The list it DREW fits the box it drew into. preferredHeight for a
            // wrapping label is taken at the rect's own width, so this is the
            // real rendered height, not an arithmetic restatement of the
            // row-count derivation - it fails if the panel ever draws more than
            // it can hold, whatever the cause.
            Check(bodyRt != null && hud2.BoardBodyHeightUnits <= bodyRt.rect.height + 1f,
                  "...and the list it draws fits the body rect with nothing truncated ("
                  + hud2.BoardBodyHeightUnits.ToString("0") + " of " + (bodyRt != null ? bodyRt.rect.height.ToString("0") : "-") + " units)");
            Check(hud2.BoardMeShown && hud2.BoardMeText.Contains("rank 41"),
                  "with more rows than fit, your own standing is still on screen (" + hud2.BoardMeText + ")");
            var meRt = hud2.BoardMeLabel.rectTransform;
            var panelR = hud2.BoardPanel.rect;
            var meR = In(hud2.BoardPanel, meRt);
            Check(meR.yMin >= panelR.yMin - 1f && meR.yMax <= panelR.yMax + 1f,
                  "...pinned inside the panel, where a longer list cannot push it off (" + meR.yMin.ToString("0") + ".." + meR.yMax.ToString("0")
                  + " in " + panelR.yMin.ToString("0") + ".." + panelR.yMax.ToString("0") + ")");
            Check(bodyRt != null && !Overlap(In(hud2.BoardPanel, bodyRt), meR),
                  "...and clear of the list above it");
            int bodyLines = hud2.BoardText.Split('\n').Length - 1;
            Check(bodyLines <= hud2.BoardRowsFit, "...and the list drew only the rows it has room for ("
                  + bodyLines + " of " + many.Count + " offered, " + hud2.BoardRowsFit + " fit)");
            hud2.HideBoard(); yield return null;

            // ---- 4f. THE NOTCH ----------------------------------------------------
            //
            // This HUD read Screen.safeArea, which Unity reports as the WHOLE
            // SCREEN in a WebGL build - the platform this game ships to phones.
            // So the inset computed zero on every notched phone and no check
            // anywhere could fail. SafeAreaWeb's forced* seams are the only way
            // to pose a cut-out on a machine that has none.
            try
            {
                // A landscape iPhone: 141 px bitten out of each side, 63 off the
                // bottom (MobileBuilderUI's R9 note, measured on owen's phone).
                SafeAreaWeb.forcedLeft = 141f; SafeAreaWeb.forcedRight = 141f;
                SafeAreaWeb.forcedTop = 0f; SafeAreaWeb.forcedBottom = 63f;
                yield return null; yield return null;
                var canvasRt = GameObject.Find("map_hud_canvas").GetComponent<RectTransform>();
                var full = canvasRt.rect;
                // The cut-out in CANVAS units. Measuring the controls against
                // the canvas rect instead would pass by construction - every
                // control is a child of the inset root - and the thing that was
                // broken is whether the root is inset at all.
                float sfNow = Screen.width / Mathf.Max(1f, full.width);
                float safeL = full.xMin + 141f / sfNow, safeR = full.xMax - 141f / sfNow, safeB = full.yMin + 63f / sfNow;
                Check(hud2.SafeRoot.rect.width < full.width - 10f, "a posed notch actually reaches the map HUD's root ("
                      + hud2.SafeRoot.rect.width.ToString("0") + " units inside a " + full.width.ToString("0") + "-unit canvas)");
                var gR = In(canvasRt, hud2.GarageButton.GetComponent<RectTransform>());
                Check(gR.xMax <= safeR + 1f && gR.xMin >= safeL - 1f,
                      "GARAGE - the only way off the map - clears the cut-out (spans "
                      + gR.xMin.ToString("0.0") + ".." + gR.xMax.ToString("0.0") + " inside a safe "
                      + safeL.ToString("0.0") + ".." + safeR.ToString("0.0") + ")");
                // Unconditional, deliberately: the first run of this section had
                // it behind `if (CardShown)` from a position where the card was
                // folded, so it drew no conclusion and the total still read
                // green. A skip is missing cover, never a pass (CLAUDE.md).
                Check(hud2.CardShown, "the encounter card is still up, so CHALLENGE can be measured at all");
                var cR = In(canvasRt, hud2.ChallengeButton.GetComponent<RectTransform>());
                Check(cR.yMin >= safeB - 1f, "...and CHALLENGE clears the home indicator ("
                      + cR.yMin.ToString("0.0") + " against a safe floor of " + safeB.ToString("0.0") + ")");
            }
            finally
            {
                SafeAreaWeb.forcedLeft = SafeAreaWeb.forcedRight = SafeAreaWeb.forcedTop = SafeAreaWeb.forcedBottom = -1f;
            }
            yield return null; yield return null;
            bm.TeleportPlayer(new Vector3(hp.x, 0f, hp.z));
            for (int i = 0; i < 6; i++) yield return null;

            bm.TestUnparkPool();
            bm.TeleportPlayer(new Vector3(hp.x, 0f, hp.z)); for (int i = 0; i < 10; i++) yield return null;
            int qf0 = d.quickFights;
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
            // ---- the ring rises where you stand (CrazyGames plan, step 4) ------------
            var worldGo = GameObject.Find("world");
            Check(worldGo != null && bm.FightInWorld, "...and the world stays standing around the ring");
            Check(worldGo != null && Mathf.Abs(worldGo.transform.position.x + chalAt.x) < 0.01f && Mathf.Abs(worldGo.transform.position.z + chalAt.z) < 0.01f,
                  "...shifted so the encounter is at the origin the ring assumes (world at " + (worldGo != null ? worldGo.transform.position.ToString("0.0") : "-") + ")");
            var floorGo = GameObject.Find("arena_floor"); var skirtGo = GameObject.Find("arena_skirt");
            Check(floorGo != null && Mathf.Abs(floorGo.transform.position.y) < 0.01f && skirtGo != null, "...the ring is a pad on the ground with a skirt under it");
            Check(skirtGo != null && skirtGo.GetComponent<Renderer>().bounds.max.y < floorGo.transform.position.y - 0.04f, "the skirt top is below the floor, never coplanar");
            Check(!bm.ObjectiveMarkerShown, "exploration beam is hidden inside combat");
            bool footprintClear = true;
            var arenaFootprint = new Bounds(Vector3.zero, new Vector3(16f, 20000f, 16f));
            foreach (var r in worldGo.GetComponentsInChildren<Renderer>())
                if (r.enabled && r.name != "ground" && r.name != "far_terrain" && arenaFootprint.Intersects(r.bounds)) footprintClear = false;
            Check(footprintClear, "map props cannot obscure the combat footprint");
            // the floor is ABOVE every point of ground inside the ring, never on it
            // (owen, 2026-09-11: a coplanar floor z-fought and read as blurry)
            float ringTop = float.MinValue;
            for (int i = 0; i < 24; i++) { float a = i * Mathf.PI / 12f; for (float rr = 0f; rr <= 7.5f; rr += 2.5f) ringTop = Mathf.Max(ringTop, bm.TerrainHeight(chalAt.x + Mathf.Cos(a) * rr, chalAt.z + Mathf.Sin(a) * rr)); }
            float floorAbove = -bm.WorldShiftNow.y - ringTop;   // the floor's height in world terms minus the highest ground
            Check(floorAbove >= BuilderManager.PAD_LIFT - 0.01f && floorAbove < 3.5f, "...its floor a hair above the highest ground inside the ring, never coplanar (" + floorAbove.ToString("0.00") + " m)");
            Check(bm.PlanetLookOn && RenderSettings.fog, "...under the planet's sky, not the garage's");
            Check(bm.testRobot != null && bm.testRobot.rb.position.magnitude < 8f, "...with you in it");
            Check(GameObject.Find("far_terrain") != null, "...and the horizon still out there");
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
            Check(d.quickFights == qf0 + 1, "...and settled once (quickFights=" + d.quickFights + ")");
            // the debrief's loud button is CONTINUE EXPLORING: back to the map,
            // where the challenge began (owen, 2026-09-10)
            Check(FightManager.quickNextLabel.StartsWith("CONTINUE EXPLORING") && FightManager.quickNext != null, "the debrief's loud button reads CONTINUE EXPLORING (" + FightManager.quickNextLabel + ")");
            FightManager.quickNext(bm); yield return null; yield return null;
            Vector3 backAt = bm.testRobot != null ? bm.testRobot.rb.position : Vector3.zero;
            Check(bm.mode == BuilderManager.Mode.Map && Object.FindFirstObjectByType<FightManager>() == null, "...and it puts you back on the map (" + bm.mode + ")");
            Check(!bm.FightInWorld && bm.WorldChunksLoaded == want && GameObject.Find("arena_floor") == null, "...the ring gone, the world rebuilt around you (" + bm.WorldChunksLoaded + " chunks)");
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
