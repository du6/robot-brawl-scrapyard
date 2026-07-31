using UnityEngine;

namespace RobotBrawl.Phase0
{
/// <summary>Procedural sound effects (2026-07-31, owen). Unity ships no
/// sounds, and this game builds every VISUAL from primitives at runtime -
/// audio now follows the same rule. Every clip is synthesized once at first
/// use: struck metal is a stack of decaying inharmonic partials, a shear is
/// a noise crack over a falling chirp, a placement is a thump plus a click.
/// Zero asset files, ships in every build, tweak numbers not WAVs.
/// Hooks: Place/Deny/Remove (BuilderManager input), Shear (CompoundRobot
/// debris), impact clangs auto-subscribe to DamageResolver.OnHit.</summary>
public static class SfxSynth
{
    const int SR = 44100;
    static AudioClip clipPlace, clipDeny, clipRemove, clipShear;
    static AudioClip[] clangs;
    static AudioSource[] pool;
    static int poolIdx;
    static float lastClangAt = -10f;

    // Test seams: TouchSmoke-style asserts read these.
    public static int played;
    public static string last = "";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        DamageResolver.OnHit += (pos, dmg, destroyed, part) => Clang(dmg, destroyed);
    }

    static void Ensure()
    {
        if (pool != null && pool[0] != null) return;
        var go = new GameObject("sfx_player");
        Object.DontDestroyOnLoad(go);
        pool = new AudioSource[4];
        for (int i = 0; i < pool.Length; i++)
        {
            pool[i] = go.AddComponent<AudioSource>();
            pool[i].spatialBlend = 0f;
        }
        clipPlace = Thump();
        clipDeny = Buzz();
        clipRemove = Pop();
        clipShear = Crack();
        clangs = new AudioClip[3];
        for (int i = 0; i < 3; i++) clangs[i] = Bell("clang" + i, 170f * Mathf.Pow(1.34f, i));
    }

    static AudioClip Make(string name, float[] d)
    {
        var c = AudioClip.Create(name, d.Length, 1, SR, false);
        c.SetData(d, 0);
        return c;
    }

    static float[] Buf(float seconds) { return new float[(int)(SR * seconds)]; }

    /// <summary>Placement: 60 Hz-decade thump + a 3 ms click transient.</summary>
    static AudioClip Thump()
    {
        var d = Buf(0.10f);
        for (int i = 0; i < d.Length; i++)
        {
            float t = (float)i / SR;
            float env = Mathf.Exp(-t * 45f);
            d[i] = 0.8f * env * Mathf.Sin(2f * Mathf.PI * 170f * t * (1f - 0.3f * t));
            if (i < SR / 300) d[i] += 0.5f * (Random.value * 2f - 1f) * (1f - (float)i / (SR / 300f));
        }
        return Make("place", d);
    }

    /// <summary>Refusal: a short flat square-ish buzz. Unmistakably a no.</summary>
    static AudioClip Buzz()
    {
        var d = Buf(0.13f);
        for (int i = 0; i < d.Length; i++)
        {
            float t = (float)i / SR;
            float sq = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 108f * t));
            d[i] = 0.28f * sq * (1f - t / 0.13f);
        }
        return Make("deny", d);
    }

    /// <summary>Removal: bright pop - noise snap over a 320 Hz bump.</summary>
    static AudioClip Pop()
    {
        var d = Buf(0.08f);
        for (int i = 0; i < d.Length; i++)
        {
            float t = (float)i / SR;
            float env = Mathf.Exp(-t * 70f);
            d[i] = env * (0.45f * (Random.value * 2f - 1f) + 0.5f * Mathf.Sin(2f * Mathf.PI * 320f * t));
        }
        return Make("remove", d);
    }

    /// <summary>Seam shear: harsh noise crack + a falling metallic chirp.</summary>
    static AudioClip Crack()
    {
        var d = Buf(0.24f);
        for (int i = 0; i < d.Length; i++)
        {
            float t = (float)i / SR;
            float chirpF = 640f - 1600f * t;
            if (chirpF < 140f) chirpF = 140f;
            float env = Mathf.Exp(-t * 22f);
            d[i] = 0.55f * env * Mathf.Sin(2f * Mathf.PI * chirpF * t);
            if (t < 0.03f) d[i] += 0.6f * (Random.value * 2f - 1f) * (1f - t / 0.03f);
            if (Random.value < 0.002f) d[i] += 0.35f * (Random.value * 2f - 1f);
        }
        return Make("shear", d);
    }

    /// <summary>Struck metal: inharmonic partial stack (bell-plate ratios),
    /// higher partials dying faster, 4 ms noise strike on top.</summary>
    static AudioClip Bell(string name, float f0)
    {
        float[] ratio = { 1f, 2.32f, 3.85f, 6.11f };
        float[] amp   = { 1f, 0.55f, 0.32f, 0.18f };
        var d = Buf(0.34f);
        for (int i = 0; i < d.Length; i++)
        {
            float t = (float)i / SR;
            float s = 0f;
            for (int p = 0; p < 4; p++)
                s += amp[p] * Mathf.Exp(-t * (11f + 9f * p)) * Mathf.Sin(2f * Mathf.PI * f0 * ratio[p] * t);
            d[i] = 0.42f * s;
            if (t < 0.004f) d[i] += 0.5f * (Random.value * 2f - 1f) * (1f - t / 0.004f);
        }
        return Make(name, d);
    }

    static void PlayClip(AudioClip c, float vol, float pitch)
    {
        Ensure();
        var s = pool[poolIdx++ % pool.Length];
        s.pitch = pitch;
        s.volume = vol;
        s.clip = c;
        s.Play();
        played++;
        last = c.name;
    }

    public static void Place()  { PlayClip(EnsureAnd(ref clipPlace),  0.85f, Random.Range(0.95f, 1.06f)); }
    public static void Deny()   { PlayClip(EnsureAnd(ref clipDeny),   0.55f, 1f); }
    public static void Remove() { PlayClip(EnsureAnd(ref clipRemove), 0.85f, Random.Range(0.90f, 1.05f)); }
    public static void Shear()  { PlayClip(EnsureAnd(ref clipShear),  0.95f, Random.Range(0.88f, 1.10f)); }

    static AudioClip EnsureAnd(ref AudioClip c) { Ensure(); return c; }

    /// <summary>Impact clang, volume from damage dealt, small random pitch,
    /// pitched DOWN when the hit destroys the part. Rate-limited so a
    /// grinding spinner reads as ringing, not a machine gun.</summary>
    static void Clang(float dmg, bool destroyed)
    {
        if (Time.unscaledTime - lastClangAt < 0.07f) return;
        lastClangAt = Time.unscaledTime;
        Ensure();
        float v = Mathf.Clamp(0.30f + dmg / 45f, 0.30f, 1f);
        var c = clangs[Random.Range(0, clangs.Length)];
        PlayClip(c, v, Random.Range(0.92f, 1.12f) * (destroyed ? 0.78f : 1f));
    }
}
}
