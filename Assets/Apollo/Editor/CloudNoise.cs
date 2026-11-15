// Noise for real 3D cumulus (the method used by Horizon Zero Dawn / Microsoft Flight Simulator style
// cloud renderers): a tileable 128^3 Perlin-Worley shape texture, a 32^3 Worley detail texture that erodes
// the edges into billows and wisps, and a 512^2 weather map saying where the cumulus stand. Generated
// once into Assets/Apollo/Textures/Clouds (kept between scene builds).
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public static class CloudNoise
{
    const string Dir = "Assets/Apollo/Textures/Clouds";

    public static void Get(out Texture3D shape, out Texture3D detail, out Texture2D weather)
    {
        if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/Apollo/Textures", "Clouds");
        shape = AssetDatabase.LoadAssetAtPath<Texture3D>(Dir + "/Cloud_Shape.asset");
        detail = AssetDatabase.LoadAssetAtPath<Texture3D>(Dir + "/Cloud_DetailNoise.asset");
        weather = AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/Cloud_Weather.asset");
        if (!shape) shape = Shape();
        if (!detail) detail = Detail();
        if (!weather) weather = Weather();
    }

    // ---------- tileable noise ----------

    // a well-mixed integer hash (no correlation between neighbouring cells, so no rows or streaks)
    static int Hash(int x, int y, int z, int seed)
    {
        unchecked
        {
            uint h = (uint)x * 0x8da6b343u ^ (uint)y * 0xd8163841u ^ (uint)z * 0xcb1ab31fu ^ (uint)seed * 0x165667b1u;
            h ^= h >> 15; h *= 0x2c1b3c6du; h ^= h >> 12; h *= 0x297a2d39u; h ^= h >> 15;
            return (int)(h & 0x7fffffff);
        }
    }
    static float Rand(int x, int y, int z, int seed, int k) => (Hash(x, y, z, seed * 31 + k * 7919) & 0xffffff) / 16777216f;
    static int Wrap(int v, int p) => ((v % p) + p) % p;

    // Perlin gradient noise with period p (cells), returns about -1..1
    static float Perlin(float x, float y, float z, int p, int seed)
    {
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y), z0 = Mathf.FloorToInt(z);
        float fx = x - x0, fy = y - y0, fz = z - z0;
        float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);
        float G(int i, int j, int k, float dx, float dy, float dz)
        {
            int h = Hash(Wrap(i, p), Wrap(j, p), Wrap(k, p), seed) & 15;
            float u = h < 8 ? dx : dy, v = h < 4 ? dy : (h == 12 || h == 14 ? dx : dz);
            return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
        }
        float u1 = Fade(fx), v1 = Fade(fy), w1 = Fade(fz);
        float a = Mathf.Lerp(Mathf.Lerp(Mathf.Lerp(G(x0, y0, z0, fx, fy, fz), G(x0 + 1, y0, z0, fx - 1, fy, fz), u1),
                                        Mathf.Lerp(G(x0, y0 + 1, z0, fx, fy - 1, fz), G(x0 + 1, y0 + 1, z0, fx - 1, fy - 1, fz), u1), v1),
                             Mathf.Lerp(Mathf.Lerp(G(x0, y0, z0 + 1, fx, fy, fz - 1), G(x0 + 1, y0, z0 + 1, fx - 1, fy, fz - 1), u1),
                                        Mathf.Lerp(G(x0, y0 + 1, z0 + 1, fx, fy - 1, fz - 1), G(x0 + 1, y0 + 1, z0 + 1, fx - 1, fy - 1, fz - 1), u1), v1), w1);
        return a;
    }

    // Worley (cellular) F1 with period c cells, 0 at a feature point, about 1 between them
    static float Worley(float x, float y, float z, int c, int seed)
    {
        int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y), cz = Mathf.FloorToInt(z);
        float best = 9f;
        for (int k = -1; k <= 1; k++)
            for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                {
                    int nx = cx + i, ny = cy + j, nz = cz + k, wx = Wrap(nx, c), wy = Wrap(ny, c), wz = Wrap(nz, c);
                    float dx = nx + Rand(wx, wy, wz, seed, 1) - x, dy = ny + Rand(wx, wy, wz, seed, 2) - y, dz = nz + Rand(wx, wy, wz, seed, 3) - z;
                    best = Mathf.Min(best, dx * dx + dy * dy + dz * dz);
                }
        return Mathf.Clamp01(Mathf.Sqrt(best));
    }

    // 1 - Worley summed over three octaves (puffy, billowy)
    static float WorleyFbm(float u, float v, float w, int f, int seed) =>
        (1 - Worley(u * f, v * f, w * f, f, seed)) * 0.625f + (1 - Worley(u * f * 2, v * f * 2, w * f * 2, f * 2, seed + 1)) * 0.25f + (1 - Worley(u * f * 4, v * f * 4, w * f * 4, f * 4, seed + 2)) * 0.125f;

    static float Remap(float v, float a, float b, float c, float d) => c + (v - a) / (b - a) * (d - c);

    // ---------- textures ----------

    static Texture3D Shape()
    {
        const int N = 128;
        var px = new Color32[N * N * N];
        Parallel.For(0, N, z =>
        {
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N, v = (y + 0.5f) / N, w = (z + 0.5f) / N;
                    float pf = 0f, amp = 1f, tot = 0f;
                    for (int o = 0; o < 4; o++)
                    {
                        int f = 4 << o;
                        pf += amp * Perlin(u * f, v * f, w * f, f, 7 + o); tot += amp; amp *= 0.5f;
                    }
                    pf = Mathf.Clamp01(pf / tot * 0.9f + 0.5f);
                    float wr = WorleyFbm(u, v, w, 4, 21);
                    float pw = Mathf.Clamp01(Remap(pf, wr - 1f, 1f, 0f, 1f));          // Perlin-Worley: billowy but connected
                    float g = WorleyFbm(u, v, w, 8, 41), b = WorleyFbm(u, v, w, 16, 61), a = WorleyFbm(u, v, w, 32, 81);
                    px[(z * N + y) * N + x] = new Color32(B(pw), B(g), B(b), B(a));
                }
        });
        var t = new Texture3D(N, N, N, TextureFormat.RGBA32, true) { name = "Cloud_Shape", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
        t.SetPixels32(px); t.Apply(true, false);
        AssetDatabase.CreateAsset(t, Dir + "/Cloud_Shape.asset");
        Debug.Log("CLOUDS shape noise 128^3 generated");
        return t;
    }

    static Texture3D Detail()
    {
        const int N = 32;
        var px = new Color32[N * N * N];
        Parallel.For(0, N, z =>
        {
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N, v = (y + 0.5f) / N, w = (z + 0.5f) / N;
                    px[(z * N + y) * N + x] = new Color32(B(WorleyFbm(u, v, w, 2, 101)), B(WorleyFbm(u, v, w, 4, 121)), B(WorleyFbm(u, v, w, 8, 141)), 255);
                }
        });
        var t = new Texture3D(N, N, N, TextureFormat.RGBA32, true) { name = "Cloud_DetailNoise", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
        t.SetPixels32(px); t.Apply(true, false);
        AssetDatabase.CreateAsset(t, Dir + "/Cloud_DetailNoise.asset");
        Debug.Log("CLOUDS detail noise 32^3 generated");
        return t;
    }

    // weather map: R = how cloudy each area is (a smooth field; the 3D noise carves the single clouds), G = how tall
    static Texture2D Weather()
    {
        const int N = 512;
        var px = new Color32[N * N];
        Parallel.For(0, N, y =>
        {
            for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N, v = (y + 0.5f) / N;
                float fb = 0f, amp = 1f, tot = 0f;
                for (int o = 0; o < 5; o++) { int f = 3 << o; fb += amp * Perlin(u * f, v * f, 0.37f * f, f, 301 + o); tot += amp; amp *= 0.5f; }
                fb = Mathf.Clamp01(fb / tot * 0.9f + 0.5f);
                float fb2 = 0f; amp = 1f; tot = 0f;
                for (int o = 0; o < 4; o++) { int f = 6 << o; fb2 += amp * Perlin(u * f, v * f, 0.71f * f, f, 401 + o); tot += amp; amp *= 0.5f; }
                fb2 = Mathf.Clamp01(fb2 / tot * 0.9f + 0.5f);
                float cov = Mathf.Clamp01(Mathf.SmoothStep(0.28f, 0.72f, fb) * 0.75f + 0.25f * fb2);   // more and fewer clouds over a few km
                float tall = Mathf.Clamp01(fb2 * 1.3f - 0.2f);
                px[y * N + x] = new Color32(B(cov), B(tall), 0, 255);
            }
        });
        var t = new Texture2D(N, N, TextureFormat.RGBA32, true, true) { name = "Cloud_Weather", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
        t.SetPixels32(px); t.Apply(true, false);
        AssetDatabase.CreateAsset(t, Dir + "/Cloud_Weather.asset");
        System.IO.File.WriteAllBytes("Logs/previews/cloud_weather.png", t.EncodeToPNG());
        Debug.Log("CLOUDS weather map generated");
        return t;
    }

    static byte B(float x) => (byte)Mathf.RoundToInt(Mathf.Clamp01(x) * 255f);
}
