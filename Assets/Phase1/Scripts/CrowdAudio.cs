using UnityEngine;

namespace RobotBrawl.Phase0
{

/// <summary>
/// THE CROWD (owen, 2026-08-02): "should we upgrade the background to be
/// crowded with fans, and cheers when there is exciting attacks?"
///
/// Yes — but the thing that creates excitement is not a crowd, it is DYNAMIC
/// RANGE. A continuous cheer is just pink noise with extra steps: after ten
/// seconds the ear stops hearing it and every hit lands into the same wall of
/// sound. So this models an EXCITEMENT LEVEL, not a soundtrack:
///
///   murmur  - a near-silent babble bed that is always there,
///   swell   - rises with real damage,
///   roar    - reserved for a part tearing off or a KO,
///   silence - excitement decays fast when nothing is happening, so a beached
///             machine gets an uncomfortably quiet arena and the NEXT hit
///             lands into that gap.
///
/// WHAT IT REACTS TO IS DELIBERATE. It listens to DamageResolver.OnHit and to
/// shears — i.e. to the same events the judges score — NOT to contacts. If the
/// crowd cheered every time two robots touched it would re-teach the exact
/// thing the 2026-07-28 "bumps are not attacks" pass spent 144 matches
/// un-teaching: that shoving is combat. A bump gets you nothing here either.
///
/// VENUE SCALE. Crowd size follows the league, so The Yard is a handful of
/// people in a scrapyard and The Crucible is §4b's "full broadcast kit". The
/// crowd is therefore PROGRESSION FEEDBACK: you can hear that you have made
/// it, which is a lot of payoff for a gain multiplier.
///
/// Synthesized, never sampled — same rule as SfxSynth: zero asset files, tweak
/// numbers not WAVs. A crowd is acoustically just band-limited noise with a
/// moving envelope, which is exactly what this file is good at.
/// </summary>
public static class CrowdAudio
{
    const int SR = 44100;

    static AudioSource srcMurmur, srcRoar;
    static AudioClip clipMurmur, clipRoar, clipSurge;
    static AudioSource[] shotPool;
    static int shotIdx;
    static bool built;

    /// <summary>0 = empty scrapyard, 1 = packed championship arena.</summary>
    public static float venue = 0.35f;
    /// <summary>0..1, drives the murmur->roar crossfade. Public for harnesses.</summary>
    public static float excitement;
    /// <summary>Master switch — off outside a live fight, so the Workshop is quiet.</summary>
    public static bool live;

    // Test seams (TouchSmoke idiom).
    public static int surges;
    public static float peakExcitement;

    /// <summary>Excitement bleeds off this fast per second. Tuned so a lull of
    /// ~2 s drops a roar back to a murmur: long enough that a flurry reads as
    /// one sustained reaction, short enough that the arena goes quiet while
    /// someone is stuck upside down.</summary>
    public static float DECAY = 0.55f;
    /// <summary>Minimum gap between roars, so a grinding disc cannot machine-gun
    /// the crowd — same reasoning as SfxSynth's clang rate limit.</summary>
    public static float SURGE_GAP = 1.15f;
    static float lastSurgeAt = -10f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        // Same subscription pattern SfxSynth uses. dmg is the judged number,
        // so the crowd reacts to what actually counts.
        DamageResolver.OnHit += (pos, dmg, destroyed, part) =>
        {
            if (!live) return;
            Excite(Mathf.Clamp01(dmg / 90f) * 0.55f);
            if (destroyed) Surge(0.85f);
        };
    }

    /// <summary>League 0..4 -> crowd size. The Yard is nearly empty.</summary>
    public static void SetVenue(int leagueIndex)
    {
        float[] scale = { 0.26f, 0.42f, 0.62f, 0.82f, 1f };
        venue = (leagueIndex >= 0 && leagueIndex < scale.Length) ? scale[leagueIndex] : 0.5f;
    }

    public static void Begin()
    {
        Ensure();
        live = true;
        excitement = 0f;
        peakExcitement = 0f;
        surges = 0;
        if (srcMurmur != null && !srcMurmur.isPlaying) srcMurmur.Play();
        if (srcRoar != null && !srcRoar.isPlaying) srcRoar.Play();
        // A house that is already half up as the bell goes.
        Excite(0.30f);
    }

    public static void Stop()
    {
        live = false;
        excitement = 0f;
        if (srcMurmur != null) srcMurmur.Stop();
        if (srcRoar != null) srcRoar.Stop();
    }

    public static void Excite(float amount)
    {
        if (!live) return;
        excitement = Mathf.Clamp01(excitement + amount);
        if (excitement > peakExcitement) peakExcitement = excitement;
    }

    /// <summary>A discrete roar: a part came off, or the match ended.</summary>
    public static void Surge(float force)
    {
        if (!live) return;
        Excite(force);
        if (Time.unscaledTime - lastSurgeAt < SURGE_GAP) return;
        lastSurgeAt = Time.unscaledTime;
        Ensure();
        var s = shotPool[shotIdx++ % shotPool.Length];
        s.clip = clipSurge;
        s.volume = Mathf.Clamp01(0.30f + 0.62f * force) * venue;
        s.pitch = Random.Range(0.94f, 1.07f);
        s.Play();
        surges++;
    }

    static void Tick(float dt)
    {
        if (srcMurmur == null) return;
        if (!live)
        {
            srcMurmur.volume = 0f; srcRoar.volume = 0f;
            return;
        }
        excitement = Mathf.Max(0f, excitement - DECAY * dt);
        // Murmur is the floor and never quite vanishes while a fight is on;
        // the roar bed rides on top of it. Squaring excitement keeps the low
        // end genuinely quiet so the loud end has somewhere to travel to.
        float e = excitement * excitement;
        srcMurmur.volume = venue * (0.055f + 0.10f * excitement);
        srcRoar.volume   = venue * 0.72f * e;
    }

    // ------------------------------------------------------------------ synth

    static void Ensure()
    {
        if (built && srcMurmur != null) return;
        var go = new GameObject("crowd_audio");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<CrowdDriver>();

        clipMurmur = Bed("crowd_murmur", 0.16f, 5.5f, 34f, 0.32f);
        clipRoar   = Bed("crowd_roar",   0.55f, 5.5f, 150f, 0.85f);
        clipSurge  = Surge();

        srcMurmur = go.AddComponent<AudioSource>();
        srcRoar = go.AddComponent<AudioSource>();
        foreach (var s in new[] { srcMurmur, srcRoar })
        {
            s.spatialBlend = 0f; s.loop = true; s.volume = 0f; s.playOnAwake = false;
        }
        srcMurmur.clip = clipMurmur;
        srcRoar.clip = clipRoar;

        shotPool = new AudioSource[3];
        for (int i = 0; i < shotPool.Length; i++)
        {
            shotPool[i] = go.AddComponent<AudioSource>();
            shotPool[i].spatialBlend = 0f; shotPool[i].playOnAwake = false;
        }
        built = true;
    }

    /// <summary>A crowd bed. White noise through cascaded one-pole lowpasses
    /// (brightness = how much of the crowd is shouting rather than talking),
    /// amplitude-modulated by three slow LFOs so it breathes instead of
    /// hissing, plus sparse short vocal blips for texture. The tail is folded
    /// back over the head so the loop point is inaudible.</summary>
    static AudioClip Bed(string name, float bright, float seconds, float blipsPerSec, float amp)
    {
        int n = (int)(SR * seconds);
        var d = new float[n];
        float cut = Mathf.Lerp(0.02f, 0.30f, bright);   // one-pole coefficient
        float lp1 = 0f, lp2 = 0f, lp3 = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / SR;
            float w = Random.value * 2f - 1f;
            lp1 += cut * (w - lp1);
            lp2 += cut * (lp1 - lp2);
            lp3 += cut * (lp2 - lp3);
            float body = lp3 * 3.2f;
            // breathing: three incommensurate LFOs so it never pulses in time
            float mod = 0.72f
                      + 0.16f * Mathf.Sin(2f * Mathf.PI * 0.23f * t)
                      + 0.08f * Mathf.Sin(2f * Mathf.PI * 0.61f * t + 1.7f)
                      + 0.06f * Mathf.Sin(2f * Mathf.PI * 1.30f * t + 0.4f);
            d[i] = body * mod * amp;
        }
        // sparse vocal blips - short bandpassed bursts, the "individual voices"
        int blips = (int)(blipsPerSec * seconds);
        for (int b = 0; b < blips; b++)
        {
            int start = Random.Range(0, n - 4000);
            float f = Random.Range(300f, 1500f);
            float len = Random.Range(0.05f, 0.20f);
            int bn = (int)(SR * len);
            float g = Random.Range(0.06f, 0.20f) * amp;
            for (int i = 0; i < bn && start + i < n; i++)
            {
                float t = (float)i / SR;
                float env = Mathf.Sin(Mathf.PI * (t / len));       // smooth in/out
                if (env < 0f) env = 0f;
                d[start + i] += g * env * Mathf.Sin(2f * Mathf.PI * f * t)
                              * (0.55f + 0.45f * (Random.value * 2f - 1f));
            }
        }
        // seamless loop: crossfade the last XF seconds over the first XF
        int xf = (int)(SR * 0.45f);
        for (int i = 0; i < xf && i < n; i++)
        {
            float k = (float)i / xf;
            d[i] = d[i] * k + d[n - xf + i] * (1f - k);
        }
        System.Array.Resize(ref d, n - xf);
        return Make(name, d);
    }

    /// <summary>The roar itself: a fast swell that brightens as it peaks, then
    /// falls away. Deliberately ~1.5 s — long enough to feel like a reaction,
    /// short enough that two in a row do not smear together.</summary>
    static AudioClip Surge()
    {
        float seconds = 1.5f;
        int n = (int)(SR * seconds);
        var d = new float[n];
        float lp1 = 0f, lp2 = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / SR;
            float env = Mathf.Pow(Mathf.Clamp01(t / 0.16f), 1.4f) * Mathf.Exp(-Mathf.Max(0f, t - 0.16f) * 2.3f);
            float cut = Mathf.Lerp(0.06f, 0.34f, Mathf.Clamp01(env * 1.3f));
            float w = Random.value * 2f - 1f;
            lp1 += cut * (w - lp1);
            lp2 += cut * (lp1 - lp2);
            d[i] = lp2 * 3.0f * env * 0.9f;
        }
        // a few shouts riding the peak
        for (int b = 0; b < 26; b++)
        {
            int start = Random.Range((int)(SR * 0.05f), (int)(SR * 0.75f));
            float f = Random.Range(420f, 1700f);
            float len = Random.Range(0.06f, 0.22f);
            int bn = (int)(SR * len);
            float g = Random.Range(0.05f, 0.16f);
            for (int i = 0; i < bn && start + i < n; i++)
            {
                float t = (float)i / SR;
                float e2 = Mathf.Sin(Mathf.PI * (t / len)); if (e2 < 0f) e2 = 0f;
                d[start + i] += g * e2 * Mathf.Sin(2f * Mathf.PI * f * t);
            }
        }
        return Make("crowd_surge", d);
    }

    static AudioClip Make(string name, float[] d)
    {
        // clamp: the blips can push past unity and clipping reads as crackle
        for (int i = 0; i < d.Length; i++) d[i] = Mathf.Clamp(d[i], -0.98f, 0.98f);
        var c = AudioClip.Create(name, d.Length, 1, SR, false);
        c.SetData(d, 0);
        return c;
    }

    /// <summary>Static classes cannot Update; this is the one frame hook.</summary>
    class CrowdDriver : MonoBehaviour
    {
        void Update() { Tick(Time.unscaledDeltaTime); }
    }
}

}
