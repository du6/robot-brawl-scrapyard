using UnityEngine;

namespace RobotBrawl.Phase0
{
public partial class BuilderManager
{
    public const int YARD_CHEST = 1, YARD_MEET = 2, YARD_BOUT = 4,
                     YARD_SHOP = 8, YARD_UPGRADE = 16, YARD_RETRY = 32;
    public const int STEP_UPGRADE = 5, STEP_REMATCH = 6;
    float nextExpeditionSave;

    void MigrateYardGuide()
    {
        var d = Career.Data;
        if (d == null || d.yardGuideVersion >= 1) return;
        // Existing finished tutorials stay finished. Unfinished careers retain
        // completed actions, and picking an upgrade early is also respected.
        int old = d.yardStep;
        if (old >= STEP_MEET) d.yardActions |= YARD_CHEST;
        if (old >= STEP_CHALLENGE) d.yardActions |= YARD_MEET;
        if (old >= STEP_SHOP && d.quickFights > 0) d.yardActions |= YARD_BOUT;
        if (old == STEP_EXPLORE) d.yardActions = 63;
        else if (old > STEP_CHEST && (d.taskBolt || d.taskWeld))
        {
            d.yardActions |= YARD_UPGRADE;
            d.yardUpgradeAtFight = d.quickFights;
        }
        d.yardGuideVersion = 1;
        RefreshYardStep();
    }

    void RefreshYardStep()
    {
        var d = Career.Data;
        if (d == null) return;
        int a = d.yardActions;
        d.yardStep = (a & YARD_CHEST) == 0 ? STEP_CHEST
                   : (a & YARD_MEET) == 0 ? STEP_MEET
                   : (a & YARD_BOUT) == 0 ? STEP_CHALLENGE
                   : (a & YARD_SHOP) == 0 ? STEP_SHOP
                   : (a & YARD_UPGRADE) == 0 ? STEP_UPGRADE
                   : (a & YARD_RETRY) == 0 ? STEP_REMATCH : STEP_EXPLORE;
    }

    public void RecordYardAction(int action)
    {
        if (!Career.active || Career.Data == null) return;
        MigrateYardGuide();
        if ((Career.Data.yardActions & action) == action) return;
        Career.Data.yardActions |= action;
        RefreshYardStep();
        if (Career.autosave) Career.Save();
    }

    public void RecordYardUpgrade()
    {
        if (!Career.active || Career.Data == null) return;
        MigrateYardGuide();
        if ((Career.Data.yardActions & YARD_UPGRADE) != 0 || Career.Drafting || SaveWouldDraft) return;
        int active = Career.Data.activeRobot;
        if (active < 0 || active >= Career.Data.stable.Count) return;
        // The checkpoint and the installed geometry must survive together.
        // This first guided improvement autosaves the active machine.
        Career.Data.stable[active].snapshot = SnapshotString();
        Career.Data.yardUpgradeAtFight = Career.Data.quickFights;
        RecordYardAction(YARD_UPGRADE);
        RBTelemetry.Once("yard_upgrade");
    }

    public void RecordYardBoutCompleted()
    {
        RecordYardAction(YARD_BOUT | YARD_MEET);
        var d = Career.Data;
        if (d != null && (d.yardActions & YARD_UPGRADE) != 0 && d.quickFights > d.yardUpgradeAtFight)
        {
            RecordYardAction(YARD_RETRY);
            RBTelemetry.Once("yard_retry");
        }
    }

    public string GarageGuidance
    {
        get
        {
            if (!Career.active || Career.Data == null) return "";
            MigrateYardGuide();
            if (YardStep == STEP_EXPLORE) return "";
            if ((Career.Data.yardActions & YARD_UPGRADE) == 0 && (Career.Data.yardActions & YARD_CHEST) != 0)
                return "FIT AN UPGRADE · Select Wedge, then fit it low on the front to lift rivals. A weld kit reinforces an exposed mounting face.";
            if ((Career.Data.yardActions & YARD_UPGRADE) != 0 && (Career.Data.yardActions & YARD_RETRY) == 0)
                return "TRY YOUR UPGRADE · DRIVE OUT, challenge a nearby rookie, and ram with your front weapon. Keep moving between hits.";
            return "DRIVE OUT · " + ObjectiveLine(YardStep).Replace("NEXT  ·  ", "");
        }
    }

    public void OpenYardUpgradeWorkshop()
    {
        BackToBuild();
        if (MobileBuilderUI.inst != null)
        {
            MobileBuilderUI.inst.SetDockOpen(true);
            MobileBuilderUI.inst.ShowTab(0);
        }
        // Suggest a useful available part without placing or spending for the player.
        foreach (string id in new[] { "wedge", "gusset", "plate" })
        {
            int i = System.Array.FindIndex(palette, p => p.id == id);
            if (i < 0) continue;
            bool found = false;
            foreach (var stock in Career.Data.inventory)
                if (stock.partId == id && CareerRemainingMat(i, stock.mat) > 0)
                { activeMat = stock.mat; selected = i; found = true; break; }
            if (found) break;
        }
        FitGarageView();
        Coach(GarageGuidance);
    }

    public string YardWeaponHint()
    {
        bool activeWeapon = false, ram = false;
        foreach (var p in placed) { activeWeapon |= p.def.actuator; ram |= p.def.edgeHardness > 1.01f || p.def.id == "wedge"; }
        return activeWeapon ? "drive with WASD or the stick · FIRE / Space activates your weapon"
             : ram ? "ram with your front spike or wedge · reverse to line up another hit"
             : "drive with WASD or the stick · fit a spike or wedge in the garage to attack";
    }

    static bool FinitePosition(Vector3 p)
    {
        return !float.IsNaN(p.x) && !float.IsNaN(p.y) && !float.IsNaN(p.z)
            && Mathf.Abs(p.x) < 1000000f && Mathf.Abs(p.y) < 100000f && Mathf.Abs(p.z) < 1000000f;
    }

    void RestoreExpedition()
    {
        var d = Career.Data;
        if ((hasResume && resumeSeed == worldSeed) || d == null || !d.expeditionHasPosition || d.expeditionWorldSeed != worldSeed) return;
        var p = new Vector3(d.expeditionX, d.expeditionY, d.expeditionZ);
        if (!FinitePosition(p) || float.IsNaN(d.expeditionYaw) || float.IsInfinity(d.expeditionYaw)) return;
        lastMapPos = p; lastMapYaw = d.expeditionYaw; resumeSeed = worldSeed; hasResume = true;
    }

    public void CaptureExpedition(bool flush)
    {
        var d = Career.Data;
        if (mode != Mode.Map || testRobot == null || d == null || !Career.active) return;
        Vector3 p = testRobot.rb.position;
        float ground = FinitePosition(p) ? TerrainHeight(p.x, p.z) : float.NaN;
        // Never turn a flipped machine or an airborne/fallen position into a checkpoint.
        if (!FinitePosition(p) || float.IsNaN(ground) || Mathf.Abs(p.y - ground) > 3f || Vector3.Dot(testRobot.transform.up, Vector3.up) < 0.6f) return;
        lastMapPos = p; lastMapYaw = testRobot.transform.eulerAngles.y; resumeSeed = worldSeed; hasResume = true;
        bool changed = !d.expeditionHasPosition || d.expeditionWorldSeed != worldSeed
            || Vector2.Distance(new Vector2(p.x, p.z), new Vector2(d.expeditionX, d.expeditionZ)) > 0.5f
            || Mathf.Abs(Mathf.DeltaAngle(lastMapYaw, d.expeditionYaw)) > 5f;
        d.expeditionHasPosition = true; d.expeditionWorldSeed = worldSeed;
        d.expeditionX = p.x; d.expeditionY = p.y; d.expeditionZ = p.z; d.expeditionYaw = lastMapYaw;
        if (flush && changed && Career.autosave) Career.Save();
    }

    void PumpExpeditionSave()
    {
        if (Time.unscaledTime < nextExpeditionSave) return;
        nextExpeditionSave = Time.unscaledTime + 10f;
        CaptureExpedition(true);
    }
    void OnApplicationPause(bool paused) { if (paused) CaptureExpedition(true); }
    void OnApplicationFocus(bool focused) { if (!focused) CaptureExpedition(true); }
    void OnApplicationQuit() { CaptureExpedition(true); }

    void ClearArenaFootprint()
    {
        if (objectiveMarker != null) objectiveMarker.SetActive(false);
        objectiveHas = false;
        if (worldRoot == null) return;
        Physics.SyncTransforms();
        var footprint = new Bounds(Vector3.zero, new Vector3(ARENA_FIGHT_HALF * 2f + 2f, 20000f, ARENA_FIGHT_HALF * 2f + 2f));
        foreach (var r in worldRoot.GetComponentsInChildren<Renderer>())
        {
            if (r.name == "ground" || r.name == "far_terrain") continue;
            if (footprint.Intersects(r.bounds)) r.enabled = false;
        }
        foreach (var c in worldRoot.GetComponentsInChildren<Collider>())
        {
            if (c.name == "ground") continue;
            if (footprint.Intersects(c.bounds)) c.enabled = false;
        }
    }
}
}
