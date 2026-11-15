// The ground around the landing site, made for whatever point the student picked in the planner:
//  - the real shape: NASA LOLA heights (Resources/Moon/lola16_m: 16 px/deg, metres from 1737.4 km), bicubic, 256 km
//  - what LOLA is too coarse to show, generated with the Moon's own statistics (crater sizes in a power law, fresh
//    bowls 0.2 D deep with raised rims, old ones worn shallow; blocks thrown out by fresh craters):
//    mare (dark, smooth plains, fine dust) or highland (bright, rough, more craters, more slopes) from the LROC map
//  - the computer's target sits by a fresh crater with a boulder field around it, as Apollo 11's did (West crater):
//    the student has to fly past it or stop short
// The same heights drive the picture (Shaders/LunarSurface) and the landing checks here (slope, rocks under Eagle).
// The work runs on a thread while a card is up; everything depends only on the site, so a rewind sees the same ground.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;

public class LunarTerrain : MonoBehaviour
{
    public const int N = 2048;
    public static readonly float[] Half = { 128000f, 8000f, 1000f };   // half-size of each height tile (m)
    const float MoonR = 1737400f;

    public double Lat, Lon;                 // site, degrees
    public float SiteHeight;                // LOLA height of the site (m, from 1737.4 km)
    public float Radius => MoonR + SiteHeight;
    public bool Mare { get; private set; }
    public float Albedo { get; private set; }
    public bool Ready { get; private set; }
    // the eye adapts: dark mare is shown brighter than bright highland (both stay in the same order)
    public float Gain => Mathf.Clamp(2.8f * Mathf.Sqrt(0.30f / Mathf.Max(Albedo, 0.05f)), 1.6f, 2.8f);
    public Vector2 Hazard { get; private set; }  // the fresh crater by the target (site frame, m)
    public float HazardRadius { get; private set; }

    public struct Rock { public Vector2 p; public float r, h; }
    public readonly List<Rock> Rocks = new List<Rock>();

    readonly float[][] hgt = new float[3][];
    readonly float[][] bright = new float[3][];
    Task work;
    short[] lola;
    Material mat;
    Mesh mesh;
    MeshRenderer mr;
    Transform rocksRoot;

    // ---------------- setup ----------------

    public void Begin(double latDeg, double lonDeg, Material surface, Material rock)
    {
        Lat = latDeg; Lon = lonDeg;
        var ta = Resources.Load<TextAsset>("Moon/lola16_m");
        var al = Resources.Load<TextAsset>("Moon/albedo_1440");
        if (ta != null) { lola = new short[ta.bytes.Length / 2]; Buffer.BlockCopy(ta.bytes, 0, lola, 0, lola.Length * 2); Resources.UnloadAsset(ta); }
        if (al != null)
        {
            int r = Mathf.Clamp((int)((90 - latDeg) / 180 * 720), 0, 719), c = Mathf.Clamp((int)((lonDeg + 180) / 360 * 1440), 0, 1439);
            int sum = 0, n = 0;
            for (int dr = -2; dr <= 2; dr++) for (int dc = -2; dc <= 2; dc++) { sum += al.bytes[Mathf.Clamp(r + dr, 0, 719) * 1440 + (c + dc + 1440) % 1440]; n++; }
            Albedo = sum / (float)n / 255f;
            Resources.UnloadAsset(al);
        }
        else Albedo = 0.3f;
        Mare = Albedo < 0.45f;
        SiteHeight = lola != null ? Lola(latDeg, lonDeg) : 0f;
        mat = surface ? new Material(surface) : null;
        rockMat = rock;
        int seed = (int)(Math.Round(latDeg * 1000) * 7919 + Math.Round(lonDeg * 1000) * 104729) ^ 0x5a17;
        work = Task.Run(() => Generate(seed));
    }
    Material rockMat;

    void Update()
    {
        if (Ready || work == null || !work.IsCompleted) return;
        if (work.IsFaulted) { Debug.LogException(work.Exception); work = null; return; }
        Upload();
        Ready = true;
        Debug.Log($"LANDING terrain ready: {(Mare ? "mare" : "highland")} (albedo {Albedo:0.00}), site height {SiteHeight:0} m, {Rocks.Count} rocks");
    }

    // ---------------- LOLA ----------------

    float LolaAt(int row, int col)
    {
        row = Mathf.Clamp(row, 0, 2879); col = ((col % 5760) + 5760) % 5760;
        return lola[row * 5760 + col];
    }

    // bicubic (Catmull-Rom) at a latitude and longitude (degrees)
    float Lola(double latDeg, double lonDeg)
    {
        double fx = (lonDeg + 180.0) * 16.0 - 0.5, fy = (90.0 - latDeg) * 16.0 - 0.5;
        int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy);
        float tx = (float)(fx - x0), ty = (float)(fy - y0);
        float Row(int y)
        {
            float a = LolaAt(y, x0 - 1), b = LolaAt(y, x0), c = LolaAt(y, x0 + 1), d = LolaAt(y, x0 + 2);
            return Cr(a, b, c, d, tx);
        }
        return Cr(Row(y0 - 1), Row(y0), Row(y0 + 1), Row(y0 + 2), ty);
    }
    static float Cr(float a, float b, float c, float d, float t) =>
        b + 0.5f * t * (c - a + t * (2 * a - 5 * b + 4 * c - d + t * (3 * (b - c) + d - a)));

    // ---------------- generation (thread) ----------------

    void Generate(int seed)
    {
        var rnd = new System.Random(seed);
        double D2R = Math.PI / 180.0, R = MoonR + SiteHeight, cosLat = Math.Cos(Lat * D2R);
        for (int l = 0; l < 3; l++) { hgt[l] = new float[N * N]; bright[l] = new float[N * N]; }

        // tile 0: LOLA, relative to the site
        {
            var h = hgt[0]; float cell = 2 * Half[0] / N;
            for (int j = 0; j < N; j++)
            {
                double z = (j + 0.5) * cell - Half[0], la = Lat + z / R / D2R;
                for (int i = 0; i < N; i++)
                {
                    double x = (i + 0.5) * cell - Half[0], lo = Lon + x / (R * cosLat) / D2R;
                    h[j * N + i] = lola != null ? Lola(la, lo) - SiteHeight : 0f;
                }
            }
        }
        lola = null;

        float rough = Mare ? 1f : 1.8f;
        // craters: cumulative count over the tile N(>D) = n * (Dmin / D)^2 (the steady state of small lunar craters)
        // each tile only gets craters at least 8 of its texels wide (smaller ones come out as squares)
        Craters(0, rnd, 1000f, 7000f, Mare ? 150 : 420, 0.15f);
        Craters(1, rnd, 62f, 700f, (int)(290 * rough), 0.2f);
        Craters(2, rnd, 1.6f, 55f, (int)(9000 * rough), 0.3f);

        // rolling ground below LOLA's 1.9 km pixels: highlands much rougher
        Noise(1, rnd.Next(), 2400f, Mare ? 6f : 24f, 4);
        Noise(2, rnd.Next(), 160f, Mare ? 0.45f : 1.1f, 5);

        // the computer's target: a fresh crater just short of it, a field of blocks around (Apollo 11: West crater,
        // 180 m across, boulders to 2-3 m)
        float D = Mare ? 165f + (float)rnd.NextDouble() * 40f : 120f + (float)rnd.NextDouble() * 40f;
        float ang = (float)(rnd.NextDouble() * Math.PI * 2);
        Hazard = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * D * 0.25f;
        HazardRadius = D * 0.5f;
        Crater(2, Hazard.x, Hazard.y, D, 1f);
        Blocks(rnd, Hazard, D * 0.5f * 2.6f, Mare ? 900 : 1300, 3.2f);
        // blocks on the rims of the other fresh craters near the site, and a scatter everywhere
        foreach (var c in freshNear) Blocks(rnd, c.p, c.r * 2f, Mathf.Clamp((int)(c.r * 2.2f), 6, 120), Mathf.Min(0.04f * c.r * 2f + 0.4f, 2.2f));
        Blocks(rnd, Vector2.zero, Half[2] * 0.92f, Mare ? 500 : 1400, Mare ? 1.1f : 1.8f);
        foreach (var r in Rocks) Bump(2, r.p.x, r.p.y, r.r, r.h);

        for (int l = 0; l < 3; l++) mips[l] = Mips(l);
    }

    readonly List<(Vector2 p, float r)> freshNear = new List<(Vector2, float)>();

    void Craters(int l, System.Random rnd, float dMin, float dMax, int count, float freshShare)
    {
        float span = Half[l] * 2;
        for (int k = 0; k < count; k++)
        {
            double u = rnd.NextDouble();
            float d = dMin / Mathf.Sqrt((float)(1 - u * (1 - (dMin / dMax) * (dMin / dMax))));   // N(>D) ~ D^-2, cut at dMax
            float x = ((float)rnd.NextDouble() - 0.5f) * span, z = ((float)rnd.NextDouble() - 0.5f) * span;
            float f = rnd.NextDouble() < freshShare ? 0.7f + 0.3f * (float)rnd.NextDouble() : (float)(rnd.NextDouble() * rnd.NextDouble()) * 0.55f;
            if (l == 2 && x * x + z * z < 110f * 110f && f > 0.5f) f = 0.45f;          // keep the ground right at the target plain
            Crater(l, x, z, d, f);
            if (l == 2 && f > 0.7f && d > 12f) freshNear.Add((new Vector2(x, z), d * 0.5f));
        }
    }

    // a crater (diameter d, freshness f 0..1): a bowl about 0.2 d deep with a rim 0.04 d high when new, worn shallow and
    // smooth with age; fresh ones are brighter, inside and out to about 2 radii (new rock and its spray)
    void Crater(int l, float cx, float cz, float d, float f)
    {
        float rc = d * 0.5f, cell = 2 * Half[l] / N;
        float depth = d * Mathf.Lerp(0.04f, 0.2f, f), rim = d * Mathf.Lerp(0.008f, 0.04f, f), pw = Mathf.Lerp(1.4f, 2.2f, f);
        float reach = rc * (f > 0.6f ? 3f : 2f);
        int i0 = Mathf.Max(0, (int)((cx - reach + Half[l]) / cell)), i1 = Mathf.Min(N - 1, (int)((cx + reach + Half[l]) / cell));
        int j0 = Mathf.Max(0, (int)((cz - reach + Half[l]) / cell)), j1 = Mathf.Min(N - 1, (int)((cz + reach + Half[l]) / cell));
        if (i1 < i0 || j1 < j0) return;
        var h = hgt[l]; var b = bright[l];
        float tail = rim * Mathf.Pow(reach / rc, -3f);
        for (int j = j0; j <= j1; j++)
        {
            float z = (j + 0.5f) * cell - Half[l] - cz;
            for (int i = i0; i <= i1; i++)
            {
                float x = (i + 0.5f) * cell - Half[l] - cx;
                float s = Mathf.Sqrt(x * x + z * z) / rc;
                if (s > reach / rc) continue;
                float v = s < 1f ? -depth + (depth + rim) * Mathf.Pow(s, pw) : rim * Mathf.Pow(s, -3f) - tail * Mathf.Clamp01((s - 1f) / (reach / rc - 1f));
                h[j * N + i] += v;
                if (f > 0.6f && l > 0) b[j * N + i] += (f - 0.6f) * 2.5f * (s < 1f ? 0.35f : 0.55f * Mathf.Exp(-(s - 1f) * 1.4f));
            }
        }
    }

    // a block: a rounded bump (stamped into the near tile so it shows from far up too); a mesh is added near the ground
    void Bump(int l, float cx, float cz, float r, float hgtM)
    {
        float cell = 2 * Half[l] / N;
        int i0 = Mathf.Max(0, (int)((cx - r + Half[l]) / cell)), i1 = Mathf.Min(N - 1, (int)((cx + r + Half[l]) / cell));
        int j0 = Mathf.Max(0, (int)((cz - r + Half[l]) / cell)), j1 = Mathf.Min(N - 1, (int)((cz + r + Half[l]) / cell));
        for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++)
            {
                float x = (i + 0.5f) * cell - Half[l] - cx, z = (j + 0.5f) * cell - Half[l] - cz;
                float s = (x * x + z * z) / (r * r);
                if (s >= 1f) continue;
                int k = j * N + i;
                hgt[l][k] = Mathf.Max(hgt[l][k], hgt[l][k] + hgtM * Mathf.Sqrt(1f - s) * 0.6f);
                bright[l][k] += 0.9f * (1f - s);
            }
    }

    void Blocks(System.Random rnd, Vector2 c, float radius, int count, float maxSize)
    {
        for (int k = 0; k < count; k++)
        {
            // more blocks near the source, sizes in a steep power law (many small, few big)
            float a = (float)(rnd.NextDouble() * Math.PI * 2), d = radius * Mathf.Pow((float)rnd.NextDouble(), 0.75f);
            float size = 0.25f / Mathf.Pow(1f - (float)rnd.NextDouble() * (1f - Mathf.Pow(0.25f / maxSize, 2.2f)), 1f / 2.2f);
            var p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
            if (Mathf.Abs(p.x) > Half[2] * 0.95f || Mathf.Abs(p.y) > Half[2] * 0.95f) continue;
            Rocks.Add(new Rock { p = p, r = size * 0.5f, h = size * (0.55f + 0.3f * (float)rnd.NextDouble()) });
        }
    }

    // value noise in octaves (no Unity calls: this runs on a thread)
    void Noise(int l, int seed, float wave, float amp, int octaves)
    {
        float cell = 2 * Half[l] / N;
        var h = hgt[l];
        for (int j = 0; j < N; j++)
            for (int i = 0; i < N; i++)
            {
                float x = (i + 0.5f) * cell, z = (j + 0.5f) * cell, sum = 0f, a = amp, w = wave;
                for (int o = 0; o < octaves; o++) { sum += (VNoise(x / w, z / w, seed + o * 131) - 0.5f) * 2f * a; a *= 0.5f; w *= 0.5f; }
                h[j * N + i] += sum;
            }
    }

    static float Hash(int x, int y, int s)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + s * 982451653);
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xffffff) / 16777215f;
        }
    }
    static float VNoise(float x, float y, int s)
    {
        int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
        float tx = x - xi, ty = y - yi;
        tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
        float a = Hash(xi, yi, s), b = Hash(xi + 1, yi, s), c = Hash(xi, yi + 1, s), d = Hash(xi + 1, yi + 1, s);
        return a + (b - a) * tx + (c - a) * ty + (a - b - c + d) * tx * ty;
    }

    // the mip chain, made here (box filter) so the upload is only a copy
    readonly ushort[][][] mips = new ushort[3][][];
    ushort[][] Mips(int l)
    {
        var list = new List<ushort[]>();
        float[] h = hgt[l], g = bright[l];
        int n = N;
        while (true)
        {
            var px = new ushort[n * n * 2];
            for (int k = 0; k < n * n; k++) { px[2 * k] = ToHalf(h[k]); px[2 * k + 1] = ToHalf(g[k]); }
            list.Add(px);
            if (n == 1) break;
            int m = n / 2;
            var h2 = new float[m * m]; var g2 = new float[m * m];
            for (int j = 0; j < m; j++)
                for (int i = 0; i < m; i++)
                {
                    int a = (2 * j) * n + 2 * i;
                    h2[j * m + i] = (h[a] + h[a + 1] + h[a + n] + h[a + n + 1]) * 0.25f;
                    g2[j * m + i] = (g[a] + g[a + 1] + g[a + n] + g[a + n + 1]) * 0.25f;
                }
            h = h2; g = g2; n = m;
        }
        return list.ToArray();
    }

    // float -> half (IEEE 754), here because this runs on a thread
    [StructLayout(LayoutKind.Explicit)] struct Bits { [FieldOffset(0)] public float f; [FieldOffset(0)] public uint u; }
    static ushort ToHalf(float v)
    {
        uint x = new Bits { f = v }.u;
        uint sign = (x >> 16) & 0x8000u, mant = x & 0x7fffffu;
        int exp = (int)((x >> 23) & 0xff) - 127 + 15;
        if (exp <= 0)
        {
            if (exp < -10) return (ushort)sign;
            mant |= 0x800000u; int shift = 14 - exp;
            uint hs = mant >> shift; if (((mant >> (shift - 1)) & 1u) != 0) hs++;
            return (ushort)(sign | hs);
        }
        if (exp >= 31) return (ushort)(sign | 0x7c00u);
        uint hm = mant >> 13;
        if ((mant & 0x1000u) != 0) { hm++; if (hm == 0x400u) { hm = 0; exp++; if (exp >= 31) return (ushort)(sign | 0x7c00u); } }
        return (ushort)(sign | ((uint)exp << 10) | hm);
    }

    // ---------------- upload: textures, the mesh, the rocks ----------------

    void Upload()
    {
        var tex = new Texture2D[3];
        for (int l = 0; l < 3; l++)
        {
            tex[l] = new Texture2D(N, N, TextureFormat.RGHalf, true, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, name = "LunarHeights" + l };
            for (int m = 0; m < mips[l].Length; m++) tex[l].SetPixelData(mips[l][m], m);
            tex[l].Apply(false, true);
            mips[l] = null;
        }
        if (mat)
        {
            mat.SetTexture("_H0", tex[0]); mat.SetTexture("_H1", tex[1]); mat.SetTexture("_H2", tex[2]);
            mat.SetVector("_Tiles", new Vector4(Half[0], Half[1], Half[2], N));
            double D2R = Math.PI / 180.0;
            mat.SetVector("_Site", new Vector4((float)(Lat * D2R), (float)(Lon * D2R), Radius, (float)(1.0 / Math.Cos(Lat * D2R))));
            mat.SetFloat("_ShadowSteps", 34f);
            mat.SetFloat("_Gain", Gain);
            var sky = RenderSettings.skybox;
            mat.SetFloat("_Exposure", sky && sky.HasProperty("_Exposure") ? sky.GetFloat("_Exposure") : 1f);
            mat.SetVector("_Dust", Vector4.zero);
        }
        mesh = PolarMesh();
        var go = new GameObject("Ground");
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        BuildRocks();
    }

    // rings from 0.6 m out to the tile's edge, each ring's step about its angular step: fine in the middle, coarse far out
    Mesh PolarMesh()
    {
        const int A = 320;
        float q = 1f + 2f * Mathf.PI / A, r = 0.6f, rMax = Half[0] * 0.97f;
        var rings = new List<float>();
        while (r < rMax) { rings.Add(r); r *= q; }
        rings.Add(rMax);
        var v = new Vector3[1 + rings.Count * A];
        v[0] = Vector3.zero;
        for (int k = 0; k < rings.Count; k++)
            for (int a = 0; a < A; a++)
            {
                float t = (a + (k % 2) * 0.5f) * 2f * Mathf.PI / A;
                v[1 + k * A + a] = new Vector3(Mathf.Cos(t) * rings[k], 0f, Mathf.Sin(t) * rings[k]);
            }
        var tri = new List<int>(rings.Count * A * 6);
        for (int a = 0; a < A; a++) { tri.Add(0); tri.Add(1 + (a + 1) % A); tri.Add(1 + a); }
        for (int k = 0; k + 1 < rings.Count; k++)
            for (int a = 0; a < A; a++)
            {
                int i0 = 1 + k * A + a, i1 = 1 + k * A + (a + 1) % A, j0 = 1 + (k + 1) * A + a, j1 = 1 + (k + 1) * A + (a + 1) % A;
                tri.Add(i0); tri.Add(i1); tri.Add(j0);
                tri.Add(i1); tri.Add(j1); tri.Add(j0);
            }
        var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, name = "LunarGround" };
        m.vertices = v;
        m.SetTriangles(tri, 0);
        m.bounds = new Bounds(Vector3.zero, Vector3.one * 600000f);
        return m;
    }

    // the blocks near the ground, as real rocks (they cast real shadows): one mesh, a few rock shapes
    void BuildRocks()
    {
        rocksRoot = new GameObject("Rocks").transform;
        rocksRoot.SetParent(transform, false);
        var shapes = new Mesh[6];
        var rnd = new System.Random(77);
        for (int s = 0; s < shapes.Length; s++) shapes[s] = RockShape(rnd);
        var verts = new List<Vector3>(); var norms = new List<Vector3>(); var tris = new List<int>();
        int part = 0;
        void Flush()
        {
            if (verts.Count == 0) return;
            var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, name = "Rocks" + part };
            m.SetVertices(verts); m.SetNormals(norms); m.SetTriangles(tris, 0); m.RecalculateBounds();
            var go = new GameObject("Rocks" + part++);
            go.transform.SetParent(rocksRoot, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = rockMat;
            verts.Clear(); norms.Clear(); tris.Clear();
        }
        foreach (var rk in Rocks)
        {
            if (rk.r < 0.18f) continue;
            var sh = shapes[rnd.Next(shapes.Length)];
            float y = HeightAt(rk.p.x, rk.p.y, true) - rk.h * 0.35f;
            var rot = Quaternion.Euler((float)rnd.NextDouble() * 30f - 15f, (float)rnd.NextDouble() * 360f, (float)rnd.NextDouble() * 30f - 15f);
            var scale = new Vector3(rk.r * 2f, rk.h * 1.3f, rk.r * 2f * (0.75f + 0.5f * (float)rnd.NextDouble()));
            int b = verts.Count;
            var sv = sh.vertices; var sn = sh.normals;
            for (int i = 0; i < sv.Length; i++)
            {
                verts.Add(new Vector3(rk.p.x, y, rk.p.y) + rot * Vector3.Scale(sv[i], scale));
                norms.Add((rot * new Vector3(sn[i].x / scale.x, sn[i].y / scale.y, sn[i].z / scale.z)).normalized);
            }
            foreach (var t in sh.triangles) tris.Add(b + t);
            if (verts.Count > 60000) Flush();
        }
        Flush();
    }

    static Mesh RockShape(System.Random rnd)
    {
        // a lumpy, faceted block: an icosahedron split once, each corner pushed in or out
        float t = (1f + Mathf.Sqrt(5f)) / 2f;
        var v = new List<Vector3> { new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0), new Vector3(0, -1, t), new Vector3(0, 1, t),
            new Vector3(0, -1, -t), new Vector3(0, 1, -t), new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1) };
        int[] f = { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8, 3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
        var mid = new Dictionary<long, int>();
        int Mid(int a, int b)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (mid.TryGetValue(key, out int i)) return i;
            v.Add((v[a] + v[b]) * 0.5f); mid[key] = v.Count - 1; return v.Count - 1;
        }
        var tris = new List<int>();
        for (int k = 0; k < f.Length; k += 3)
        {
            int a = f[k], b = f[k + 1], c = f[k + 2], ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
            tris.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
        }
        for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized * (0.42f + 0.16f * (float)rnd.NextDouble());
        // flat-shaded: every triangle its own corners
        var fv = new List<Vector3>(); var fn = new List<Vector3>(); var ft = new List<int>();
        for (int k = 0; k < tris.Count; k += 3)
        {
            Vector3 a = v[tris[k]], b = v[tris[k + 1]], c = v[tris[k + 2]];
            if (a.y < -0.2f && b.y < -0.2f && c.y < -0.2f) continue;                  // the buried bottom
            Vector3 nrm = Vector3.Cross(b - a, c - a).normalized;
            if (Vector3.Dot(nrm, a) < 0) { var tmp = b; b = c; c = tmp; nrm = -nrm; }
            ft.Add(fv.Count); fv.Add(a); fn.Add(nrm);
            ft.Add(fv.Count); fv.Add(b); fn.Add(nrm);
            ft.Add(fv.Count); fv.Add(c); fn.Add(nrm);
        }
        var m = new Mesh(); m.SetVertices(fv); m.SetNormals(fn); m.SetTriangles(ft, 0);
        return m;
    }

    // ---------------- queries (site frame, m) ----------------

    static float Fade(float u, float v, float band) => Mathf.Clamp01((0.5f - Mathf.Max(Mathf.Abs(u - 0.5f), Mathf.Abs(v - 0.5f))) / band);

    float Sample(int l, float x, float z)
    {
        float cell = 2 * Half[l] / N;
        float fx = (x + Half[l]) / cell - 0.5f, fz = (z + Half[l]) / cell - 0.5f;
        int i = Mathf.Clamp(Mathf.FloorToInt(fx), 0, N - 2), j = Mathf.Clamp(Mathf.FloorToInt(fz), 0, N - 2);
        float tx = Mathf.Clamp01(fx - i), tz = Mathf.Clamp01(fz - j);
        var h = hgt[l];
        float a = h[j * N + i], b = h[j * N + i + 1], c = h[(j + 1) * N + i], d = h[(j + 1) * N + i + 1];
        return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
    }

    // height of the ground (from the site's datum), with or without the Moon's curve
    public float HeightAt(float x, float z, bool curved = true)
    {
        if (hgt[2] == null) return 0f;
        float h = 0f;
        float u0 = x / (2 * Half[0]) + 0.5f, v0 = z / (2 * Half[0]) + 0.5f;
        h += Sample(0, x, z) * Fade(u0, v0, 0.12f);
        float u1 = x / (2 * Half[1]) + 0.5f, v1 = z / (2 * Half[1]) + 0.5f, f1 = Fade(u1, v1, 0.08f);
        if (f1 > 0) h += Sample(1, x, z) * f1;
        float u2 = x / (2 * Half[2]) + 0.5f, v2 = z / (2 * Half[2]) + 0.5f, f2 = Fade(u2, v2, 0.08f);
        if (f2 > 0) h += Sample(2, x, z) * f2;
        if (curved) h -= (x * x + z * z) / (2f * Radius);
        return h;
    }

    // the slope (degrees) under Eagle's four pads (on a 9.4 m circle), and the tallest block within its reach
    public float SlopeAt(Vector2 p, out float rockHeight, out Vector2 downhill)
    {
        const float pad = 4.7f;
        float hE = HeightAt(p.x + pad, p.y), hW = HeightAt(p.x - pad, p.y), hN = HeightAt(p.x, p.y + pad), hS = HeightAt(p.x, p.y - pad);
        float gx = (hE - hW) / (2 * pad), gz = (hN - hS) / (2 * pad);
        downhill = new Vector2(-gx, -gz);
        rockHeight = 0f;
        foreach (var r in Rocks)
        {
            float d = (r.p - p).magnitude;
            if (d < pad + 0.6f + r.r) rockHeight = Mathf.Max(rockHeight, r.h * 0.65f);
        }
        return Mathf.Atan(Mathf.Sqrt(gx * gx + gz * gz)) * Mathf.Rad2Deg;
    }

    // ---------------- every frame ----------------

    public void Place(Quaternion siteToWorld, Vector3 sitePointWorld, Vector2 meshCentre, Vector4 dust)
    {
        transform.SetPositionAndRotation(sitePointWorld, siteToWorld);
        if (mat)
        {
            mat.SetVector("_MeshCenter", new Vector4(meshCentre.x, 0, meshCentre.y, 0));
            mat.SetVector("_Dust", dust);
        }
    }

    public void SetVisible(bool on) { if (mr) mr.enabled = on; if (rocksRoot) rocksRoot.gameObject.SetActive(on); }
}
