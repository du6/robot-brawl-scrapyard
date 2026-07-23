using UnityEngine;

/// <summary>
/// Zero-setup entry point: as soon as any scene loads in Play mode, show the
/// mode-select menu (Phase 0 physics sandbox / Phase 1 robot builder) unless a
/// manager is already running. No scene wiring, no prefabs — copy the folders
/// into a project and press Play.
/// </summary>
public static class Phase0Bootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
#if UNITY_2023_1_OR_NEWER
        bool running = Object.FindFirstObjectByType<Phase0Manager>() != null
                    || Object.FindFirstObjectByType<BuilderManager>() != null
                    || Object.FindFirstObjectByType<ModeSelect>() != null;
#else
        bool running = Object.FindObjectOfType<Phase0Manager>() != null
                    || Object.FindObjectOfType<BuilderManager>() != null
                    || Object.FindObjectOfType<ModeSelect>() != null;
#endif
        if (!running)
            new GameObject("ModeSelect").AddComponent<ModeSelect>();
    }
}
