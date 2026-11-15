// Sounds made in code (no recordings): filtered noise that loops cleanly, for the wind on the pad,
// the cabin fans and the roar of five F-1 engines through the hull.
using UnityEngine;

public static class ProcSound
{
    public static AudioClip Noise(string name, float seconds, float lowHz, float highHz, float swellHz, float swellDepth, float crackle, int seed)
    {
        const int sr = 44100;
        int n = (int)(seconds * sr), xf = sr / 2;
        var d = new float[n];
        var rnd = new System.Random(seed);
        float lp1 = 0, lp2 = 0, hpIn = 0, hp = 0;
        float aL = 1f - Mathf.Exp(-2f * Mathf.PI * lowHz / sr);
        float rc = 1f / (2f * Mathf.PI * highHz), aH = rc / (rc + 1f / sr);
        for (int i = 0; i < n; i++)
        {
            float w = (float)(rnd.NextDouble() * 2.0 - 1.0);
            if (crackle > 0f && rnd.NextDouble() < crackle) w *= 9f;               // pops in the roar
            lp1 += aL * (w - lp1); lp2 += aL * (lp1 - lp2);                         // two-pole low-pass
            hp = aH * (hp + lp2 - hpIn); hpIn = lp2;                                // one-pole high-pass
            float swell = 1f - swellDepth * 0.5f * (1f + Mathf.Sin(2f * Mathf.PI * swellHz * i / sr));
            d[i] = hp * swell;
        }
        // loop: the last half second fades into the first
        var o = new float[n - xf];
        for (int j = 0; j < o.Length; j++)
        {
            if (j < xf) { float k = (float)j / xf; o[j] = d[j] * k + d[n - xf + j] * (1f - k); }
            else o[j] = d[j];
        }
        float peak = 1e-6f;
        foreach (float s in o) peak = Mathf.Max(peak, Mathf.Abs(s));
        for (int j = 0; j < o.Length; j++) o[j] *= 0.9f / peak;
        var clip = AudioClip.Create(name, o.Length, 1, sr, false);
        clip.SetData(o, 0);
        return clip;
    }
}
