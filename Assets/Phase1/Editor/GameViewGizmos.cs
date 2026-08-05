using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace RobotBrawl.Phase0.EditorTools
{
/// <summary>
/// R5 (critic finding 3): a switch for the Game view's Gizmos toggle, so a
/// screenshot used as review evidence can be proved free of editor overlay
/// geometry rather than assumed to be.
///
/// PROVENANCE, so nobody re-derives this: round 5's critic reported "stray
/// debug/builder gizmos" over the fight HUD and the results screen, and I
/// wrote this expecting the Game view gizmo flag to be the cause. It was NOT.
/// Every piece of that geometry turned out to be REAL game objects, verified by
/// walking the live scene graph:
///   * the green arrow on the Workshop tab  -> `build_room/drive_arrow`
///   * the dotted rings around the core     -> `core_0/socket_ring_*`
///   * the teal cross + circle on results   -> the arena's painted floor
///                                             markings, `line_x` / `line_z` /
///                                             `circle_seg_0..23`
///   * the cyan column through the HUD      -> `corner_post_*` (CyanGlow)
/// Those were addressed where they lived (PartVisualFactory.FloorMark was
/// repainted, and the results backdrop went from 0.93 to 0.985 alpha).
///
/// So this file fixed nothing by itself, and therefore does NOT force the flag
/// off on load - it would be changing an editor setting the owner never asked
/// to have changed. It is opt-in: Tools > Robot Brawl > Game View Gizmos.
/// </summary>
[InitializeOnLoad]
public static class GameViewGizmos
{
    const string PREF = "RobotBrawl.ForceGameViewGizmosOff";
    // Default FALSE: opt-in only. See the class comment - the round-5 gizmo
    // report turned out to be real scene objects, not this flag, so silently
    // forcing it off on every domain reload would be an unasked-for change to
    // the owner's editor.

    static GameViewGizmos()
    {
        // Deferred: on a static-constructor tick the Game view may not exist yet.
        EditorApplication.delayCall += () =>
        {
            if (EditorPrefs.GetBool(PREF, false)) Apply(false);
        };
    }

    [MenuItem("Tools/Robot Brawl/Game View Gizmos/Off (default)")]
    static void MenuOff()
    {
        EditorPrefs.SetBool(PREF, true);
        Debug.Log("[GameViewGizmos] " + Apply(false));
    }

    [MenuItem("Tools/Robot Brawl/Game View Gizmos/On")]
    static void MenuOn()
    {
        EditorPrefs.SetBool(PREF, false);
        Debug.Log("[GameViewGizmos] " + Apply(true));
    }

    /// <summary>Sets the gizmo flag on every open Game view. Returns a short
    /// report string; never throws, because a Unity version that renames the
    /// internal member must not be able to break the editor for this project.
    /// </summary>
    public static string Apply(bool on)
    {
        try
        {
            var t = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");
            if (t == null) return "UnityEditor.GameView type not found";
            var wins = Resources.FindObjectsOfTypeAll(t);
            int hit = 0;
            const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var prop = t.GetProperty("gizmos", any);
            var field = t.GetField("m_Gizmos", any);
            foreach (var w in wins)
            {
                bool done = false;
                if (prop != null && prop.CanWrite) { prop.SetValue(w, on, null); done = true; }
                else if (field != null) { field.SetValue(w, on); done = true; }
                if (done)
                {
                    hit++;
                    var ew = w as EditorWindow;
                    if (ew != null) ew.Repaint();
                }
            }
            return "gizmos=" + on + " gameViews=" + wins.Length + " set=" + hit;
        }
        catch (System.Exception e)
        {
            return "failed: " + e.Message;
        }
    }
}
}
