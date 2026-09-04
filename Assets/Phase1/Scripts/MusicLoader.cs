// ===========================================================================
// MusicLoader.cs - where a music track comes from, per platform.
//
// 2026-09-04, the phone-load cut. The web payload was 33 MB and MUSIC was
// 21 MB of it: six MP3s packed into Resources, incompressible, downloaded in
// full before the first frame - and the funnel measured half of phone
// arrivals never getting that far. On the WEB the tracks now live beside the
// page (play/rb/music/*.mp3, re-encoded small) and are fetched AFTER boot,
// only when a theme is actually picked, and cached for the session. The
// game becomes playable ~20 MB sooner; the music arrives a beat later.
//
// Everywhere else (editor, iOS, benches) this is Resources.Load, synchronous,
// exactly as before - the callback fires before Load returns.
// ===========================================================================
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using UnityEngine.Networking;
#endif

public static class MusicLoader
{
    static readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();

    /// <summary>Resolve `name`, falling through `alternatives` if it is missing,
    /// then call back with (clip or null, the name that actually loaded).</summary>
    public static void Load(MonoBehaviour host, string name, string[] alternatives, Action<AudioClip, string> done)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (host == null) { done(null, ""); return; }
        host.StartCoroutine(Fetch(name, alternatives, done));
#else
        var clip = Resources.Load<AudioClip>(name);
        string got = name;
        if (clip == null && alternatives != null)
            foreach (var alt in alternatives)
            { clip = Resources.Load<AudioClip>(alt); if (clip != null) { got = alt; break; } }
        done(clip, clip != null ? got : "");
#endif
    }

    /// <summary>Already fetched this session? Synchronous, web-only meaningful.</summary>
    public static AudioClip Cached(string name)
    { AudioClip c; return cache.TryGetValue(name, out c) ? c : null; }

#if UNITY_WEBGL && !UNITY_EDITOR
    static IEnumerator Fetch(string name, string[] alternatives, Action<AudioClip, string> done)
    {
        var order = new List<string> { name };
        if (alternatives != null) foreach (var a in alternatives) if (a != name) order.Add(a);
        foreach (var n in order)
        {
            AudioClip hit;
            if (cache.TryGetValue(n, out hit) && hit != null) { done(hit, n); yield break; }
            using (var req = UnityWebRequestMultimedia.GetAudioClip(Url(n), AudioType.MPEG))
            {
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                {
                    var clip = DownloadHandlerAudioClip.GetContent(req);
                    if (clip != null) { cache[n] = clip; done(clip, n); yield break; }
                }
                Debug.Log("[MusicLoader] " + n + " unavailable (" + req.result + ") - trying the next");
            }
        }
        done(null, "");
    }

    /// <summary>The page's own directory + music/<name>.mp3 - same origin as the
    /// game, so no CORS entry, and it moves with the page wherever it is served.</summary>
    static string Url(string name)
    {
        string u = Application.absoluteURL ?? "";
        int q = u.IndexOf('?'); if (q >= 0) u = u.Substring(0, q);
        int h = u.IndexOf('#'); if (h >= 0) u = u.Substring(0, h);
        int slash = u.LastIndexOf('/');
        u = slash >= 0 ? u.Substring(0, slash + 1) : "";
        return u + "music/" + name + ".mp3";
    }
#endif
}
