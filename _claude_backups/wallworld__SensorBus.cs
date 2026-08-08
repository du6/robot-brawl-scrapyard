using System.Collections.Generic;
using UnityEngine;

namespace RobotBrawl.Phase0
{
/// <summary>
/// P1 (Programmable Robots, design doc v1.1 §4–5, 2026-08-05) — the senses.
///
/// One component per machine, registered by BuilderManager.SpawnBot when the
/// build carries any sensor part. It owns every sensor's refresh timer, reads
/// the world ON the fixed-step clock — never per-frame, never realtime — so a
/// program cannot out-perceive the physics tick and two runs of the same
/// scenario read the same numbers (harness requirement, replay requirement,
/// Roblox-port requirement). Consumers: the P2 ProgramRunner, the test-drive
/// telemetry strip, and SensorProbe.
///
/// A sensor is a destructible PART. When its body part detaches or dies, its
/// readings turn INVALID ("no signal") — a P2 condition referencing them is
/// simply false, and its idle draw stops (a sheared eye stops drinking). No
/// new failure systems: the feature inherits the damage model, which is the
/// whole point.
///
/// v1 reads the FIRST LIVE sensor of each kind; per-instance channels (two
/// rangefinders covering nose and flank separately) are the P2 program
/// model's job — the mounting axis is already stored per entry for it.
/// </summary>
public class SensorBus : MonoBehaviour
{
    /// <summary>Fixed refresh cadences (§4.1): rays 20 Hz, compass 10 Hz.
    /// Tilt and the damage bus read own-body state — free physically — and
    /// refresh on the ray clock for determinism's sake, not cost's.</summary>
    public const float RAY_HZ = 20f;
    public const float COMPASS_HZ = 10f;
    public const float RANGE_MAX = 12f;
    /// <summary>Hazard-proximity radius the edge sentinel reports at. Matches
    /// the AI repulsion query's working range.</summary>
    public const float HAZARD_NEAR = 3f;
    const float WALL_EPS = 0.05f;   // never divide by a zero clearance

    public CompoundRobot robot;
    /// <summary>The machine the compass tracks — the "match beacon" fiction.
    /// FightManager.Setup wires both sides; test drive points it at the ram
    /// dummy so the strip has something to read.</summary>
    public CompoundRobot target;
    Vector3 forwardLocal = Vector3.forward;
    PowerPlant power;

    class Entry
    {
        public string kind;      // part def id
        public int partIdx;      // index into robot.parts (body order)
        public Vector3 axisLocal; // mount-face normal (rangefinder look dir)
        public float kw;         // idle draw while live
    }
    readonly List<Entry> entries = new List<Entry>();

    float rayTimer, compassTimer;

    // ---- readings. VALID == the sensor part is owned AND alive; a dead or
    // absent sensor is "no signal" and its numbers are stale by design. ----
    public bool rangeValid;   public float rangeDist;   public string rangeTag = "";
    public bool compassValid; public float enemyBearingDeg; public float enemyRange;
    public bool tiltValid;    public float upY = 1f; public float pitchSign; public float rollSign; public bool flipped;
    // V2.2 sensor split (owen, 2026-08-06): the edge sentinel became TWO
    // parts. WALL: distance AND signed bearing to the nearest wall plane —
    // the macro verbs (MOVE AWAY FROM WALL, TURN side TO WALL) need a
    // direction, which the old along-motion distance never carried. TRAP:
    // the same pair for the nearest live hazard, plus the too-close flag.
    public bool wallValid;    public float wallDist;   public float wallBearingDeg;
    /// <summary>Direction that increases clearance from the arena AS A WHOLE,
    /// robot-relative. Continuous everywhere; magnitude zero at the centre.
    /// Use this, not wallBearingDeg, for anything that RETREATS.</summary>
    public bool wallFieldValid; public float wallEscapeDeg;
    public bool trapValid;    public float trapDist;   public float trapBearingDeg; public bool trapNear;
    public bool busValid;     public float hpFrac = 1f; public int partsLost; public float powerFrac = 1f; public bool hitRecently;

    float lastTaken; float lastHitAt = -999f;

    public void Init(CompoundRobot r, List<int> idxs, List<string> kinds,
                     List<Vector3> axes, List<float> kws, Vector3 fwdLocal)
    {
        robot = r;
        forwardLocal = fwdLocal.sqrMagnitude > 0.01f ? fwdLocal.normalized : Vector3.forward;
        power = r.GetComponent<PowerPlant>();
        entries.Clear();
        for (int i = 0; i < idxs.Count; i++)
            entries.Add(new Entry
            {
                kind = kinds[i],
                partIdx = idxs[i],
                axisLocal = axes[i].sqrMagnitude > 0.01f ? axes[i].normalized : forwardLocal,
                kw = kws[i],
            });
        lastTaken = r.damageTaken;
    }

    Entry FirstLive(string kind)
    {
        foreach (var e in entries)
            if (e.kind == kind && robot.PartAlive(e.partIdx)) return e;
        return null;
    }

    /// <summary>Does the machine currently carry a LIVE sensor of this kind?</summary>
    public bool Has(string kind) { return FirstLive(kind) != null; }

    public int LiveCount()
    {
        int n = 0;
        foreach (var e in entries) if (robot != null && robot.PartAlive(e.partIdx)) n++;
        return n;
    }

    /// <summary>How many of `kind` were EVER fitted (scouting counts hardware,
    /// not survivors).</summary>
    public int FittedCount(string kind)
    {
        int n = 0;
        foreach (var e in entries) if (e.kind == kind) n++;
        return n;
    }

    void Invalidate()
    { rangeValid = compassValid = tiltValid = wallValid = trapValid = busValid = wallFieldValid = false; }

    void FixedUpdate()
    {
        if (robot == null || robot.rb == null || robot.dead) { Invalidate(); return; }

        // Idle draw, live sensors only. PowerPlant ignores pre-bell draws
        // itself (combatEnabled gate), so posting unconditionally is correct.
        float kw = 0f;
        foreach (var e in entries) if (robot.PartAlive(e.partIdx)) kw += e.kw;
        if (power != null && kw > 0f) power.Draw(kw);

        float dt = Time.fixedDeltaTime;
        rayTimer += dt; compassTimer += dt;
        bool rayTick = rayTimer >= 1f / RAY_HZ;
        if (rayTick) rayTimer -= 1f / RAY_HZ;          // keep the remainder — cadence stays exact
        bool compTick = compassTimer >= 1f / COMPASS_HZ;
        if (compTick) compassTimer -= 1f / COMPASS_HZ;

        ReadRangefinder(rayTick);
        ReadCompass(compTick);
        ReadTilt();
        ReadWall(rayTick);
        ReadTrap(rayTick);
        ReadBus();
    }

    void ReadRangefinder(bool tick)
    {
        var e = FirstLive("rangefinder");
        rangeValid = e != null;
        if (e == null || !tick) return;      // between refreshes the last reading holds
        var part = robot.parts[e.partIdx];
        if (part.go == null) { rangeValid = false; return; }
        Vector3 origin = part.go.transform.position;
        Vector3 dir = robot.transform.TransformDirection(e.axisLocal);
        float best = RANGE_MAX; bool found = false; RaycastHit bestHit = default(RaycastHit);
        foreach (var h in Physics.RaycastAll(origin, dir, RANGE_MAX))
        {
            if (h.collider.isTrigger) continue;
            if (h.rigidbody == robot.rb) continue;   // never sees its own hull
            if (h.distance < best) { best = h.distance; found = true; bestHit = h; }
        }
        rangeDist = found ? best : RANGE_MAX;
        rangeTag = found ? TagOf(bestHit) : "clear";
    }

    string TagOf(RaycastHit h)
    {
        var cr = h.collider.GetComponentInParent<CompoundRobot>();
        if (cr != null && cr != robot) return "enemy";
        if (h.collider.GetComponentInParent<HazardBase>() != null) return "hazard";
        return "wall";
    }

    void ReadCompass(bool tick)
    {
        var e = FirstLive("compass");
        compassValid = e != null && target != null && !target.dead && target.rb != null;
        if (!compassValid || !tick) return;
        Vector3 to = target.rb.worldCenterOfMass - robot.rb.worldCenterOfMass; to.y = 0f;
        enemyRange = to.magnitude;
        Vector3 fwd = robot.transform.TransformDirection(forwardLocal); fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-6f || to.sqrMagnitude < 1e-6f) { enemyBearingDeg = 0f; return; }
        enemyBearingDeg = Vector3.SignedAngle(fwd.normalized, to.normalized, Vector3.up);
    }

    void ReadTilt()
    {
        tiltValid = FirstLive("tiltsensor") != null;
        if (!tiltValid) return;
        upY = robot.transform.up.y;
        flipped = upY < 0.2f;   // the gyro's own "needs righting" line
        Vector3 fwd = robot.transform.TransformDirection(forwardLocal);
        Vector3 right = Vector3.Cross(Vector3.up, fwd).sqrMagnitude > 1e-6f
                      ? robot.transform.right : Vector3.right;
        pitchSign = Mathf.Abs(fwd.y) > 0.05f ? Mathf.Sign(fwd.y) : 0f;
        rollSign = Mathf.Abs(right.y) > 0.05f ? Mathf.Sign(right.y) : 0f;
    }

    /// <summary>Nearest of the four wall planes: perpendicular distance plus
    /// the signed bearing from the robot's nose to that wall. (The v1 edge
    /// sentinel measured along the motion vector — directionless, useless to
    /// TURN SIDE TO WALL. WALL DIST conditions keep working: a robot heading
    /// at a wall reads the same small number either way at the thresholds
    /// programs actually use.)</summary>
    void ReadWall(bool tick)
    {
        wallValid = FirstLive("wallsensor") != null;
        if (!wallValid || !tick) return;
        Vector3 pos = robot.rb.worldCenterOfMass;
        float half = BuilderManager.ARENA_HALF;
        float best = half - pos.x; Vector3 dir = Vector3.right;
        if (half + pos.x < best) { best = half + pos.x; dir = Vector3.left; }
        if (half - pos.z < best) { best = half - pos.z; dir = Vector3.forward; }
        if (half + pos.z < best) { best = half + pos.z; dir = Vector3.back; }
        wallDist = Mathf.Max(0f, best);
        Vector3 fwd = robot.transform.TransformDirection(forwardLocal); fwd.y = 0f;
        wallBearingDeg = fwd.sqrMagnitude < 1e-6f ? 0f
            : Vector3.SignedAngle(fwd.normalized, dir, Vector3.up);

        // ---- the CONTINUOUS escape field ------------------------------
        // Everything above describes the NEAREST PLANE, and that is a
        // DISCONTINUOUS function of position: cross the arena mid-line and
        // the reported plane swaps identity, the bearing jumps 180 degrees,
        // and any controller built on it reverses at full magnitude with no
        // hysteresis to damp it. Behaviour critic R3 measured a robot under a
        // standing MOVE AWAY FROM WALL ping-ponging 1.02-6.91 m forever, five
        // 180-degree flips in 11 s, peak 7.5 m/s, ending up closer to a wall
        // than the 1.2 m threshold the WALL! hat exists to defend -- and a
        // STATIONARY robot at the arena centre chattering full opposite lock,
        // 40 reversals in 18 s, because the nearest plane there is a coin
        // toss between four equals.
        //
        // So we publish a FIELD as well: an inverse-square repulsion summed
        // over ALL FOUR planes. It is continuous everywhere, it has no
        // antipode and no plane identity to swap, and it falls to exactly
        // zero at the centre -- which is the one place a robot fleeing walls
        // should want to be. wallDist/wallBearingDeg are untouched: EDGE DIST
        // conditions and SIDE TO WALL still want the nearest plane.
        float dpx = Mathf.Max(half - pos.x, WALL_EPS);
        float dnx = Mathf.Max(half + pos.x, WALL_EPS);
        float dpz = Mathf.Max(half - pos.z, WALL_EPS);
        float dnz = Mathf.Max(half + pos.z, WALL_EPS);
        Vector3 push = new Vector3(1f / (dnx * dnx) - 1f / (dpx * dpx), 0f,
                                   1f / (dnz * dnz) - 1f / (dpz * dpz));
        wallFieldValid = push.sqrMagnitude > 1e-6f;
        wallEscapeDeg = (!wallFieldValid || fwd.sqrMagnitude < 1e-6f) ? 0f
            : Vector3.SignedAngle(fwd.normalized, push.normalized, Vector3.up);
    }

    /// <summary>Nearest LIVE hazard: distance, signed bearing, and the
    /// too-close flag (the old hazardNear semantics, 3 m).</summary>
    void ReadTrap(bool tick)
    {
        trapValid = FirstLive("trapsensor") != null;
        if (!trapValid || !tick) return;
        Vector3 pos = robot.rb.worldCenterOfMass;
        trapDist = 99f;
        Vector3 toTrap = Vector3.zero;
        foreach (var h in HazardBase.Active)
        {
            if (h == null) continue;
            Vector3 d = h.transform.position - pos; d.y = 0f;
            if (d.magnitude < trapDist) { trapDist = d.magnitude; toTrap = d; }
        }
        trapNear = trapDist < HAZARD_NEAR;
        Vector3 fwd = robot.transform.TransformDirection(forwardLocal); fwd.y = 0f;
        trapBearingDeg = (fwd.sqrMagnitude < 1e-6f || toTrap.sqrMagnitude < 1e-6f) ? 0f
            : Vector3.SignedAngle(fwd.normalized, toTrap.normalized, Vector3.up);
    }

    void ReadBus()
    {
        busValid = FirstLive("dmgbus") != null;
        if (!busValid) return;
        float sum = 0f, max = 0f; int lost = 0;
        foreach (var p in robot.parts)
        {
            max += p.maxHp;
            if (p.detached) { lost++; continue; }
            sum += Mathf.Max(0f, p.hp);
        }
        hpFrac = max > 0.001f ? sum / max : 0f;
        partsLost = lost;
        powerFrac = power != null ? power.Frac : 0f;
        if (robot.damageTaken > lastTaken + 0.5f) lastHitAt = Time.time;
        lastTaken = robot.damageTaken;
        hitRecently = Time.time - lastHitAt < 1f;
    }

    // ------------------------------------------------------------ telemetry

    /// <summary>One line per FITTED sensor kind, for the test-drive strip and
    /// the probe — dead sensors say so instead of vanishing, because "my eye
    /// is gone" is exactly what the strip must teach.</summary>
    public List<string> TelemetryLines()
    {
        var lines = new List<string>();
        if (FittedCount("rangefinder") > 0)
            lines.Add(!rangeValid ? "RANGE — no signal"
                : "RANGE " + rangeDist.ToString("F2") + " m " + rangeTag);
        if (FittedCount("compass") > 0)
            lines.Add(!compassValid ? "COMPASS — no signal"
                : "COMPASS " + (enemyBearingDeg >= 0f ? "+" : "") + enemyBearingDeg.ToString("F0")
                  + "° · " + enemyRange.ToString("F1") + " m");
        if (FittedCount("tiltsensor") > 0)
            lines.Add(!tiltValid ? "TILT — no signal"
                : "TILT up " + upY.ToString("F2") + (flipped ? " FLIPPED" : ""));
        if (FittedCount("wallsensor") > 0)
            lines.Add(!wallValid ? "WALL — no signal"
                : "WALL " + wallDist.ToString("F1") + " m · "
                  + (wallBearingDeg >= 0f ? "+" : "") + wallBearingDeg.ToString("F0") + "°");
        if (FittedCount("trapsensor") > 0)
            lines.Add(!trapValid ? "TRAP — no signal"
                : trapDist > 90f ? "TRAP — none live"
                : "TRAP " + trapDist.ToString("F1") + " m · "
                  + (trapBearingDeg >= 0f ? "+" : "") + trapBearingDeg.ToString("F0") + "°"
                  + (trapNear ? " · CLOSE" : ""));
        if (FittedCount("dmgbus") > 0)
            lines.Add(!busValid ? "BUS — no signal"
                : "BUS hp " + Mathf.RoundToInt(hpFrac * 100f) + "% · pwr "
                  + Mathf.RoundToInt(powerFrac * 100f) + "%"
                  + (partsLost > 0 ? " · -" + partsLost + " parts" : "")
                  + (hitRecently ? " · HIT" : ""));
        return lines;
    }

    /// <summary>Scouting line for a sensor loadout ("rangefinder ×1 · tilt sensor"),
    /// built from PLACED parts so it works on recipes that were never spawned.
    /// Empty string = no sensors.</summary>
    public static string LoadoutLine(List<BuilderManager.PlacedPart> build)
    {
        var counts = new Dictionary<string, int>();
        var order = new List<string>();
        foreach (var p in build)
        {
            if (p.def == null || !p.def.sensor) continue;
            if (!counts.ContainsKey(p.def.id)) { counts[p.def.id] = 0; order.Add(p.def.id); }
            counts[p.def.id]++;
        }
        string s = "";
        foreach (var id in order)
        {
            var d = CareerDB.Def(id);
            s += (s.Length > 0 ? " · " : "")
               + (d != null ? d.label : id)
               + (counts[id] > 1 ? " ×" + counts[id] : "");
        }
        return s;
    }
}
}
