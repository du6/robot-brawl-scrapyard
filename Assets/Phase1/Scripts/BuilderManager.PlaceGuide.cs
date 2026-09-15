using System.Collections.Generic;
using UnityEngine;

namespace RobotBrawl.Phase0
{
/// <summary>
/// WHERE CAN THIS GO? — the affordance that was missing from part placement.
///
/// The builder has always drawn a socket grid, and drawn it well: a translucent
/// plate over the mount face plus a dot per socket, brightening the one the
/// ghost has snapped to. But it only ever drew it on the face the pointer was
/// ALREADY on, which means the answer to "where can this go" arrived strictly
/// after the player had guessed. On a mouse that is a cheap guess — sweep the
/// cursor and watch. On a phone it is not: there is no hover, so every guess
/// costs a tap, and until 2026-09-14 every tap also committed.
///
/// So while a part is held, every face that ACCEPTS IT lights up, before the
/// player aims at anything.
///
/// ⚠ THE GUIDE IS AN AFFORDANCE, NOT THE AUTHORITY, and the difference is worth
/// stating because a marker that lies is worse than no marker. It answers
/// "does this face take this KIND of part, and is there room at the middle of
/// it" — the category rules, the socket rule, the floor, and a clear box at the
/// centred candidate. It does NOT run the credit budget or the rotor sweep, and
/// it does not test all twelve sockets on the face. The ghost remains the
/// authority on the exact spot, and it still explains itself when it refuses.
/// A lit face therefore means "aim here and you will get a green ghost
/// somewhere on it", which is the question being asked at that moment.
///
/// ⚠ AND IT SHARES THE RULES RATHER THAN RESTATING THEM. Every clause below
/// either calls the same helper ComputeGhost calls (OverlapBlocker, FloorPlane,
/// PartVisualFactory.SocketOffsets) or is named in the same words. A guide with
/// its own private copy of "can a wheel go here" is a second rulebook, and the
/// two drift on the first change to either.
/// </summary>
public partial class BuilderManager
{
    GameObject faceGuide;
    Material faceGuideMat;
    string faceGuideKey = "";
    GameObject dirArrow;
    Material dirArrowMat;
    string dirArrowKey = "";
    readonly List<Vector3> guideFacePos = new List<Vector3>();
    readonly List<string> guideFaceOwner = new List<string>();

    /// <summary>Faces currently lit, for a bench. Positions are the centre of
    /// each lit face in build space.</summary>
    public int TestGuideFaceCount { get { return guideFacePos.Count; } }
    public Vector3 TestGuideFace(int i) { return guideFacePos[i]; }
    /// <summary>Which part's face this marker is on. A bench probing a marker
    /// has to know whether the ray it fired actually LANDED on that face - a
    /// part standing in front of it is a reachability fact about the camera,
    /// not a lie told by the marker.</summary>
    public string TestGuideFaceOwner(int i) { return guideFaceOwner[i]; }
    public bool TestGuideShown { get { return faceGuide != null && faceGuide.activeSelf; } }
    public bool TestArrowShown { get { return dirArrow != null && dirArrow.activeSelf; } }

    /// <summary>Did the overlap rule have to LOOK for this spot - i.e. was the
    /// socket nearest the pointer occupied? And how far along the face it went
    /// to find a free one. A bench that cannot see this can only prove the
    /// forgiveness never hurts, not that it ever fires.</summary>
    public bool TestGhostNudged { get { return ghostNudged; } }
    public float TestGhostNudgeDist { get { return ghostNudgeDist; } }
    /// <summary>Is the spot the ghost is standing on ACTUALLY free? Asked of the
    /// same helper the rule uses, with the same extents, so a green ghost that
    /// overlaps a part is a failure this can see.</summary>
    public bool TestGhostSpotClear { get { return OverlapBlocker(ghostPos, ghostHalf) == null; } }
    /// <summary>Drop the held mount face so a bench can measure the RAW pick.
    /// Without it every probe after the first measures the hysteresis rather
    /// than the thing the hysteresis is applied to - and a control leg that
    /// cannot see the uncontrolled value is not a control leg.</summary>
    public void TestClearFaceLock() { faceLockPart = null; }
    /// <summary>Welded faces across the whole machine - the weld verb's only
    /// externally visible effect, so it is what a bench must count.</summary>
    public int TestWeldedFaceCount
    {
        get
        {
            int n = 0;
            foreach (var q in placed)
                if (q != null)
                    for (int b = 0; b < 6; b++) if (((q.gussetFaces >> b) & 1) != 0) n++;
            return n;
        }
    }
    /// <summary>The face a normal resolves to, as the builder names it. Exposed
    /// so a bench compares faces the same way the hysteresis does, rather than
    /// by its own float comparison that could call two different normals equal.</summary>
    public static string AxisCodePublic(Vector3 n) { return AxisCode(n); }
    /// <summary>The mount face as an EXACT identity: this part, this face.
    /// ⚠ TestGhostTarget returns the part's def id, so on a machine with four
    /// beams on it "beam/YP" is four different faces wearing one name - and a
    /// hysteresis check written against that name cannot tell "the lock held"
    /// from "the lock let go and landed somewhere that reads the same". The
    /// bench compares THIS.</summary>
    public string TestGhostFaceKey
    {
        get
        {
            return ghostTarget == null || ghostTarget.go == null
                 ? "none"
                 : ghostTarget.go.GetEntityId() + "/" + AxisCode(ghostNormal);
        }
    }
    /// <summary>Labels of everything on the machine, so a bench can check that a
    /// refusal names a part that is really there.</summary>
    public string PlacedLabel(int i)
    { return (i >= 0 && i < placed.Count && placed[i].def != null) ? placed[i].def.label : ""; }
    public Vector3 PlacedPos(int i) { return (i >= 0 && i < placed.Count) ? placed[i].pos : Vector3.zero; }
    public Vector3 PlacedHalf(int i) { return (i >= 0 && i < placed.Count) ? placed[i].Half() : Vector3.zero; }

    void HideFaceGuide()
    {
        if (faceGuide != null && faceGuide.activeSelf) faceGuide.SetActive(false);
        guideFacePos.Clear();
        guideFaceOwner.Clear();
    }

    /// <summary>Does `t`'s face (world axis `axis`, outward `sign`) accept a
    /// part built from `def` at `yaw`? Returns the centred candidate position
    /// through `at` when it does.</summary>
    bool FaceAcceptsPart(PlacedPart t, int axis, float sign, P1PartDef def, int yaw, out Vector3 at)
    {
        at = Vector3.zero;
        if (t == null || t.def == null || def == null) return false;

        Vector3 normal = Vector3.zero;
        normal[axis] = sign;
        bool isWheel   = def.category == P1Category.Mobility;
        bool isSpinner = def.id.StartsWith("spinner");
        bool oriented  = NeedsAxis(def) || def.sensor;

        // The category rules, in the order and the words ComputeGhost uses.
        if (t.def.category == P1Category.Mobility) return false;                  // nothing attaches to a wheel
        if (t.def.category == P1Category.Weapon && !t.def.actuator) return false; // nor to a bare weapon
        if (isWheel && Mathf.Abs(normal.y) > 0.5f) return false;                  // wheels: side faces only
        if (isSpinner && normal.y < -0.5f) return false;                          // spinner: not underneath

        Vector3 tHalf = t.Half();
        int t1 = (axis + 1) % 3, t2 = (axis + 2) % 3;
        if (PartVisualFactory.SocketOffsets(tHalf[t1] * 2f).Length == 0
            || PartVisualFactory.SocketOffsets(tHalf[t2] * 2f).Length == 0) return false;

        var probe = new PlacedPart { def = def, yaw = yaw,
                                     wheelAxis = oriented ? normal : Vector3.zero };
        Vector3 newHalf = probe.Half();
        if (!oriented && (PartVisualFactory.SocketOffsets(newHalf[t1] * 2f).Length == 0
                          || PartVisualFactory.SocketOffsets(newHalf[t2] * 2f).Length == 0))
            return false;

        // The centred candidate: nearest socket alignment to the face centre on
        // each tangent, which is what an aim at the middle of the face produces.
        float[] tu = FaceSockets(tHalf[t1] * 2f), tv = FaceSockets(tHalf[t2] * 2f);
        float[] nu = oriented ? CenterSocket : FaceSockets(newHalf[t1] * 2f);
        float[] nv = oriented ? CenterSocket : FaceSockets(newHalf[t2] * 2f);
        Vector3 pos = t.pos;
        pos[axis] = t.pos[axis] + sign * (tHalf[axis] + newHalf[axis]);
        pos[t1] = t.pos[t1] + NearestSnap(tu, nu, 0f);
        pos[t2] = t.pos[t2] + NearestSnap(tv, nv, 0f);

        if (pos.y - newHalf.y < FloorPlane(isWheel) - 1e-4f) return false;
        if (OverlapBlocker(pos, newHalf) != null) return false;
        at = t.pos + normal * tHalf[axis];
        return true;
    }

    /// <summary>Light every face that accepts the held part. Rebuilt only when
    /// the answer could have changed - the held part, its orientation, or the
    /// machine - because this is geometry and it is built every time it is not
    /// cached.</summary>
    void RefreshFaceGuide(P1PartDef def)
    {
        if (def == null || def.applique) { HideFaceGuide(); return; }

        // Stamp the MACHINE, not its part count: a MOVE or a repaint changes
        // neither the count nor the id list, and a guide that survives a move
        // points at faces that are no longer there.
        int stamp = 17;
        for (int i = 0; i < placed.Count; i++)
        {
            var q = placed[i];
            if (q == null || q.def == null) continue;
            stamp = stamp * 31 + q.def.id.GetHashCode();
            stamp = stamp * 31 + Mathf.RoundToInt(q.pos.x / STUD);
            stamp = stamp * 31 + Mathf.RoundToInt(q.pos.y / STUD);
            stamp = stamp * 31 + Mathf.RoundToInt(q.pos.z / STUD);
            stamp = stamp * 31 + q.yaw;
        }
        string key = def.id + "|" + ghostYaw + "|" + activeMat + "|" + stamp;
        if (faceGuide != null && faceGuideKey == key)
        {
            if (!faceGuide.activeSelf) faceGuide.SetActive(true);
            PulseGuide();
            return;
        }

        if (faceGuide != null) Destroy(faceGuide);
        guideFacePos.Clear();
        guideFaceOwner.Clear();
        faceGuide = new GameObject("faceGuide");
        faceGuideMat = OverlayMat();

        foreach (var t in placed)
        {
            if (t == null || t.def == null) continue;
            Vector3 tHalf = t.Half();
            for (int axis = 0; axis < 3; axis++)
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    Vector3 at;
                    if (!FaceAcceptsPart(t, axis, sgn, def, ghostYaw, out at)) continue;
                    guideFacePos.Add(at);
                    guideFaceOwner.Add(t.def.id);
                    int t1 = (axis + 1) % 3, t2 = (axis + 2) % 3;
                    // A plate the size of the face says WHERE; a dot at its
                    // centre says HERE, and survives the plate being seen edge-on
                    // from the current orbit - which half the faces on any build
                    // always are.
                    Vector3 plate = Vector3.one;
                    plate[t1] = tHalf[t1] * 2f * 0.92f;
                    plate[t2] = tHalf[t2] * 2f * 0.92f;
                    plate[axis] = 0.004f;
                    PartVisualFactory.Deco(PrimitiveType.Cube, faceGuide.transform,
                        at, plate, Vector3.zero, faceGuideMat, "gface");
                    PartVisualFactory.Deco(PrimitiveType.Sphere, faceGuide.transform,
                        at, Vector3.one * GUIDE_DOT, Vector3.zero, faceGuideMat, "gdot");
                }
        }
        faceGuideKey = key;
        faceGuide.SetActive(guideFacePos.Count > 0);
        PulseGuide();
    }

    /// <summary>Marker diameter in metres. Sized against the stud pitch rather
    /// than picked: 0.6 of a stud is as large as a marker can be without two
    /// adjacent ones touching, and the play-test asked for LARGE markers.</summary>
    public const float GUIDE_DOT = STUD * 0.6f;

    void PulseGuide()
    {
        if (faceGuideMat == null) return;
        float k = 0.30f + 0.16f * Mathf.Sin(Time.unscaledTime * 2.6f);
        Tint(faceGuideMat, new Color(0.35f, 0.85f, 1f), k);
    }

    void HideDirArrow()
    {
        if (dirArrow != null && dirArrow.activeSelf) dirArrow.SetActive(false);
    }

    /// <summary>WHICH WAY DOES IT POINT? An arrow along the mount normal for a
    /// weapon that has a direction and no swept circle.
    ///
    /// The rotor weapons already explain themselves - RefreshGhostArc draws the
    /// circle a spinner will sweep, before it is committed. The FIXED weapons
    /// had nothing: a spike's business end is its mount normal, which is a fact
    /// about the geometry and invisible in the drawing, so "which way will this
    /// spike face" was answerable only by placing it and looking.</summary>
    void RefreshDirArrow(P1PartDef def, Vector3 pos, Vector3 normal, bool show)
    {
        bool want = show && def != null && def.category == P1Category.Weapon && !def.actuator
                 && normal.sqrMagnitude > 0.01f;
        if (!want) { HideDirArrow(); return; }

        if (dirArrow == null || dirArrowKey != def.id)
        {
            if (dirArrow != null) Destroy(dirArrow);
            dirArrow = new GameObject("dirArrow");
            dirArrowMat = OverlayMat();
            // Built pointing +Z in local space and rotated by the normal, so
            // the head is always at the business end whatever face it lands on.
            float len = 0.34f, shaft = 0.030f, head = 0.105f;
            PartVisualFactory.Deco(PrimitiveType.Cube, dirArrow.transform,
                new Vector3(0f, 0f, len * 0.5f), new Vector3(shaft, shaft, len),
                Vector3.zero, dirArrowMat, "shaft");
            for (int s = -1; s <= 1; s += 2)
                PartVisualFactory.Deco(PrimitiveType.Cube, dirArrow.transform,
                    new Vector3(s * head * 0.32f, 0f, len - head * 0.30f),
                    new Vector3(shaft, shaft, head),
                    new Vector3(0f, s * 38f, 0f), dirArrowMat, "barb");
            dirArrowKey = def.id;
        }
        if (!dirArrow.activeSelf) dirArrow.SetActive(true);
        dirArrow.transform.SetPositionAndRotation(
            pos, Quaternion.FromToRotation(Vector3.forward, normal.normalized));
        float k = 0.55f + 0.35f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3.2f));
        Tint(dirArrowMat, new Color(1f, 0.80f, 0.25f), k);
    }

    void DestroyPlaceGuides()
    {
        if (faceGuide != null) { Destroy(faceGuide); faceGuide = null; faceGuideKey = ""; }
        if (dirArrow != null) { Destroy(dirArrow); dirArrow = null; dirArrowKey = ""; }
        guideFacePos.Clear();
        guideFaceOwner.Clear();
    }
}
}
