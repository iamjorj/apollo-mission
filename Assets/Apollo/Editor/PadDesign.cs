// Launch Complex 39A as it was in 1969, rebuilt clean. The USGS heights show today's pad, with modern
// structures that leave jagged steps and mounds, and a laser survey's small bumps everywhere. Here the pad
// is designed from its real 1969 measurements (NRHP nomination with the 1966 NASA site plan, KSC fact
// sheets, Moonport SP-4204): a 390 x 325 ft concrete hardstand (390 ft north-south) about 40 ft above the
// flat ground, its mound faced in light concrete, the crawler incline to the south at 5% (its profile taken
// from the survey) carrying the crawlerway's two 40 ft river-gravel lanes and grass median, and the
// 450 x 58 x 42 ft flame trench running north-south under the launcher with its two-way deflector.
// Around it the real ground is kept, smoothed.
// Frame: metres from the vehicle axis, x east, z north; heights in metres above sea level.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class PadDesign
{
    // the 2 m terrain grid has vertices at odd x and even z in this frame, so every edge here lies on it
    public const float TopX = 49f, TopS = -60f, TopN = 58f;          // hardstand top: 98 x 118 m (325 x 390 ft)
    public const float RampHalf = 25f;                               // crest of the crawler incline
    public const float TrenchHalf = 9f, TrenchS = -80f, TrenchN = 96f; // lined from 21 m south of the hardstand, out through the north face
    public const float TrenchFloor = 1.4f, Grade = 1.6f, Slope = 1f / 3f;
    public const float LaneIn = 7.6f, LaneOut = 19.8f;               // crawlerway: two 40 ft lanes, 50 ft apart

    static float padTop, axisX, axisZ;
    static float[] ramp;                                             // incline crest height, every metre south of TopS

    static float Clean(float v) => float.IsNaN(v) || v < -50f || v > 300f ? 0.3f : v;

    // ---------------- heights ----------------

    public static void Heights(float[] dem, float ax, float az, float top)
    {
        padTop = top; axisX = ax; axisZ = az;
        const int N = 2048;
        for (int i = 0; i < dem.Length; i++) dem[i] = Clean(dem[i]);
        // a survey's small bumps out: Gaussian, sigma 1.5 m
        var k = new float[9]; float ks = 0f;
        for (int i = 0; i < 9; i++) { k[i] = Mathf.Exp(-(i - 4) * (i - 4) / (2f * 1.5f * 1.5f)); ks += k[i]; }
        for (int i = 0; i < 9; i++) k[i] /= ks;
        var tmp = new float[dem.Length]; var sm = new float[dem.Length];
        for (int r = 0; r < N; r++)
            for (int c = 0; c < N; c++)
            {
                float s = 0f;
                for (int q = 0; q < 9; q++) s += k[q] * dem[r * N + Mathf.Clamp(c + q - 4, 0, N - 1)];
                tmp[r * N + c] = s;
            }
        for (int r = 0; r < N; r++)
            for (int c = 0; c < N; c++)
            {
                float s = 0f;
                for (int q = 0; q < 9; q++) s += k[q] * tmp[Mathf.Clamp(r + q - 4, 0, N - 1) * N + c];
                sm[r * N + c] = s;
            }
        // around the pad the land is eased into gently rolling ground: today's road banks, ditches and
        // building pads are smoothed away (box blur radius 9 m, three passes); lagoons are left alone
        var heavy = (float[])sm.Clone();
        for (int pass = 0; pass < 3; pass++)
        {
            var hb = new float[heavy.Length];
            const int RB = 9;
            for (int r = 0; r < N; r++)
            {
                float acc = 0f;
                for (int c = -RB; c <= RB; c++) acc += heavy[r * N + Mathf.Clamp(c, 0, N - 1)];
                for (int c = 0; c < N; c++)
                {
                    hb[r * N + c] = acc / (2 * RB + 1);
                    acc += heavy[r * N + Mathf.Min(c + RB + 1, N - 1)] - heavy[r * N + Mathf.Max(c - RB, 0)];
                }
            }
            for (int c = 0; c < N; c++)
            {
                float acc = 0f;
                for (int r = -RB; r <= RB; r++) acc += hb[Mathf.Clamp(r, 0, N - 1) * N + c];
                for (int r = 0; r < N; r++)
                {
                    heavy[r * N + c] = acc / (2 * RB + 1);
                    acc += hb[Mathf.Min(r + RB + 1, N - 1) * N + c] - hb[Mathf.Max(r - RB, 0) * N + c];
                }
            }
        }
        for (int r = 0; r < N; r++)
            for (int c = 0; c < N; c++)
            {
                int i = r * N + c;
                if (sm[i] < 0.8f) continue;                                   // water and its beds stay as surveyed
                float wx = c + 0.5f - 1024f - axisX, wz = 1024f - r - 0.5f - axisZ;
                float ease = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(650f, 850f, Mathf.Sqrt(wx * wx + wz * wz)));
                if (ease > 0f) sm[i] = Mathf.Max(0.8f, Mathf.Lerp(sm[i], heavy[i], ease));
            }

        float At(float wx, float wz)                                  // smoothed survey height at a pad-frame point
        {
            int c = Mathf.Clamp(Mathf.RoundToInt(1024f + axisX + wx - 0.5f), 0, N - 1), r = Mathf.Clamp(Mathf.RoundToInt(1024f - (axisZ + wz) - 0.5f), 0, N - 1);
            return sm[r * N + c];
        }
        // the incline's real profile, along its crest east of today's trench, smoothed and made to fall
        // steadily from the hardstand down to the crawlerway (2.3-2.6 m), then on to the flat ground
        ramp = new float[480];
        for (int d = 0; d < ramp.Length; d++)
        {
            float s = 0f; int n = 0;
            for (int dz = -12; dz <= 12; dz += 2)
                for (int dx = 12; dx <= 28; dx += 4) { s += At(dx, TopS - d + dz); n++; }
            ramp[d] = Mathf.Lerp(padTop, Mathf.Min(s / n, padTop), Mathf.SmoothStep(0f, 1f, d / 30f));
        }
        int low = -1;
        for (int d = 1; d < ramp.Length; d++)
        {
            ramp[d] = Mathf.Min(ramp[d], ramp[d - 1]);
            if (low < 0 && ramp[d] <= 2.6f) low = d;
            if (low >= 0) ramp[d] = Mathf.Max(Grade, Mathf.Min(ramp[d], 2.6f - (d - low) * 0.05f));
        }
        int end = 0; while (end < ramp.Length - 1 && ramp[end] > Grade + 0.05f) end++;
        Debug.Log($"PAD incline: {ramp[0]:0.0} m at the hardstand, {ramp[100]:0.0} at 100 m, {ramp[200]:0.0} at 200 m, meets the ground {end} m south of the hardstand");

        // the designed pad over the smoothed ground; near it, the modern clutter is graded away
        for (int r = 0; r < N; r++)
            for (int c = 0; c < N; c++)
            {
                float wx = c + 0.5f - 1024f - axisX, wz = 1024f - r - 0.5f - axisZ;
                int i = r * N + c;
                if (Mathf.Abs(wx) > 330f || wz < -560f || wz > 330f) { dem[i] = sm[i]; continue; }
                float w = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(18f, 60f, ToeDistance(wx, wz)));
                dem[i] = Mathf.Max(Designed(wx, wz), Mathf.Lerp(sm[i], Grade, w));
            }
    }

    static float RampAt(float d) => d <= 0f ? padTop : ramp[Mathf.Min(Mathf.FloorToInt(d), ramp.Length - 1)];

    static float DistRect(float x, float z, float x0, float x1, float z0, float z1)
    {
        float dx = Mathf.Max(x0 - x, 0f, x - x1), dz = Mathf.Max(z0 - z, 0f, z - z1);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    // the designed pad's height (the hardstand and incline with their 1-in-3 grass slopes)
    public static float Designed(float wx, float wz)
    {
        float h = padTop - DistRect(wx, wz, -TopX, TopX, TopS, TopN) * Slope;
        float d = TopS - wz;
        if (d > 0f) h = Mathf.Max(h, RampAt(d) - Mathf.Max(0f, Mathf.Abs(wx) - RampHalf) * Slope);
        return h;
    }

    // how far a point is outside the foot of the pad's slopes (negative inside)
    static float ToeDistance(float wx, float wz)
    {
        float run = (padTop - Grade) / Slope;
        float t = DistRect(wx, wz, -TopX, TopX, TopS, TopN) - run;
        float d = TopS - wz;
        if (d > 0f) t = Mathf.Min(t, Mathf.Abs(wx) - RampHalf - (RampAt(d) - Grade) / Slope);
        return t;
    }

    // on the pad and its slopes, the terrain takes the designed height exactly (so its edges meet the
    // hardstand's own surface with no step); data coordinates
    public static bool ExactHeight(float dataX, float dataZ, out float h)
    {
        h = 0f;
        if (ramp == null) return false;
        float wx = dataX - axisX, wz = dataZ - axisZ;
        if (ToeDistance(wx, wz) > 10f) return false;
        h = Surface(wx, wz);
        return true;
    }

    // where the land types are forced to mown grass (the pad's slopes and a margin), 0..1
    public static float Lawn(float dataX, float dataZ)
    {
        if (ramp == null) return 0f;
        return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(10f, 40f, ToeDistance(dataX - axisX, dataZ - axisZ)));
    }

    // inside the pad's fence (about 430 m around it): no bare sand, as it was kept in 1969
    public static float Fenced(float dataX, float dataZ)
    {
        float wx = dataX - axisX, wz = dataZ - axisZ;
        return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(380f, 460f, Mathf.Sqrt(wx * wx + wz * wz)));
    }

    static bool InTrench(float wx, float wz) => Mathf.Abs(wx) < TrenchHalf && wz > TrenchS && wz < TrenchN;

    // a 2 m grid cell (by its centre, pad frame) that the mound touches: drawn by the pad, not the terrain
    static bool MoundCell(float cx, float cz)
    {
        if (ToeDistance(cx, cz) > 3f) return false;
        for (int k = 0; k < 4; k++)
            if (Designed(cx + ((k & 1) == 0 ? -1f : 1f), cz + ((k & 2) == 0 ? -1f : 1f)) > Grade + 0.01f) return true;
        return false;
    }

    // the terrain leaves a hole where the pad's own surfaces are (data coordinates of a grid cell's centre)
    public static bool Hole(float dataX, float dataZ)
    {
        if (ramp == null) return false;
        float wx = dataX - axisX, wz = dataZ - axisZ;
        return MoundCell(wx, wz) || InTrench(wx, wz);
    }

    // ---------------- the pad's own surfaces ----------------

    public static void Build(Transform parent, Texture macro, Color grassTint)
    {
        var con = new Material(Shader.Find("Apollo/PadConcrete")) { name = "Pad_Concrete" };
        string G = "Assets/Apollo/Textures/Ground/";
        con.SetTexture("_Detail", AssetDatabase.LoadAssetAtPath<Texture2D>(G + "Concrete034_Color.jpg"));
        con.SetTexture("_DetailN", AssetDatabase.LoadAssetAtPath<Texture2D>(G + "Concrete034_NormalGL.jpg"));
        con.SetTexture("_Gravel", AssetDatabase.LoadAssetAtPath<Texture2D>(G + "Gravel023_Color.jpg"));
        con.SetTexture("_GravelN", AssetDatabase.LoadAssetAtPath<Texture2D>(G + "Gravel023_NormalGL.jpg"));
        con.SetTexture("_Grass", AssetDatabase.LoadAssetAtPath<Texture2D>(G + "Grass004_Color.jpg"));
        con.SetTexture("_GrassN", AssetDatabase.LoadAssetAtPath<Texture2D>(G + "Grass004_NormalGL.jpg"));
        con.SetColor("_GrassTint", grassTint);
        con.SetTexture("_Macro", macro);
        con.SetVector("_Trench", new Vector4(TrenchHalf, TrenchS, TopN, 0));
        con.SetVector("_Lanes", new Vector4(LaneIn, LaneOut, -16f, TopS));
        con.SetFloat("_GradeY", Grade - padTop);
        AssetDatabase.CreateAsset(con, FlightBuilder.Dir + "/Pad_Concrete.mat");

        // the whole mound (hardstand, concrete slopes, incline) on the terrain's own 2 m grid, so its edges
        // meet the terrain exactly; where it reaches the flat ground the shader turns it into the same lawn
        var V = new List<Vector3>(); var T = new List<int>();
        var index = new Dictionary<long, int>();
        int Vert(float x, float z)
        {
            long key = ((long)Mathf.RoundToInt(x * 2f) << 32) ^ (uint)Mathf.RoundToInt(z * 2f);
            if (index.TryGetValue(key, out int i)) return i;
            V.Add(new Vector3(x, Surface(x, z) - padTop, z)); index[key] = V.Count - 1;
            return V.Count - 1;
        }
        int rampEnd = 0; while (rampEnd < ramp.Length - 1 && ramp[rampEnd] > Grade + 0.05f) rampEnd++;
        float run = (padTop - Grade) / Slope + 4f;
        float gz0 = 2f * Mathf.Floor((TopS - rampEnd - run) / 2f), gx0 = 2f * Mathf.Floor((-TopX - run) / 2f) + 1f;   // the terrain grid: even z, odd x
        for (float z = gz0; z < TopN + run; z += 2f)
            for (float x = gx0; x < TopX + run; x += 2f)
            {
                float cx = x + 1f, cz = z + 1f;
                if (InTrench(cx, cz) || !MoundCell(cx, cz)) continue;
                int a = Vert(x, z), b = Vert(x + 2f, z), c = Vert(x + 2f, z + 2f), d = Vert(x, z + 2f);
                T.AddRange(new[] { a, d, c, a, c, b });
            }
        var mesh = new Mesh { name = "Pad_Mound", indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(V); mesh.SetTriangles(T, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, FlightBuilder.Dir + "/Pad_Mound.asset");
        var go = new GameObject("Pad_Mound_Hardstand_and_Incline");
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = con;
        go.isStatic = false;   // under the moving world root: never static-batched
        Debug.Log($"PAD mound: {T.Count / 6} cells, hardstand {2 * TopX:0} x {TopN - TopS:0} m at {padTop:0.0} m, incline {rampEnd} m");

        // the flame trench: concrete walls blackened by exhaust, a wet floor, the steel deflector under the engines
        var wallTex = TrenchWallTexture();
        var wallMat = FlightBuilder.Lit("Pad_TrenchWalls", Color.white, 0.12f, 0f, wallTex);
        wallMat.SetTextureScale("_BaseMap", new Vector2(1f / 12f, 1f / (padTop - TrenchFloor)));
        wallMat.SetTextureOffset("_BaseMap", new Vector2(0f, 1f));
        var floorMat = FlightBuilder.Lit("Pad_TrenchFloor", new Color(0.20f, 0.20f, 0.20f), 0.35f);
        var steel = FlightBuilder.Lit("Pad_FlameDeflector", new Color(0.30f, 0.27f, 0.25f), 0.25f, 0.4f);
        var walls = new FlightBuilder.Geo(); var floor = new FlightBuilder.Geo(); var defl = new FlightBuilder.Geo();
        float fy = TrenchFloor - padTop;
        for (float z = TrenchS; z < TrenchN - 1e-3f; z += 2f)
            foreach (float sx in new[] { -1f, 1f })
            {
                float x = sx * TrenchHalf;
                float h0 = Surface(x, z) - padTop, h1 = Surface(x, z + 2f) - padTop;
                if (h0 <= fy + 0.02f && h1 <= fy + 0.02f) continue;
                walls.Quad(new Vector3(x, fy, z), new Vector3(x, fy, z + 2f), new Vector3(x, h1, z + 2f), new Vector3(x, h0, z), new Vector3(-sx, 0, 0));
            }
        float sy = Surface(0f, TrenchS) - padTop;                      // south end, under the start of the incline
        walls.Quad(new Vector3(-TrenchHalf, fy, TrenchS), new Vector3(TrenchHalf, fy, TrenchS), new Vector3(TrenchHalf, sy, TrenchS), new Vector3(-TrenchHalf, sy, TrenchS), Vector3.forward);
        floor.Quad(new Vector3(-TrenchHalf, fy, TrenchS), new Vector3(TrenchHalf, fy, TrenchS), new Vector3(TrenchHalf, fy, TrenchN + 6f), new Vector3(-TrenchHalf, fy, TrenchN + 6f), Vector3.up);
        // two-way deflector (an inverted V on rails): 13 m high, 15 m wide, 29 m long, its ridge under the engines
        const float half = 14.5f, dh = 11.9f, dw = 7.5f;
        int ns = 16;
        foreach (float side in new[] { -1f, 1f })
            for (int k = 0; k < ns; k++)
            {
                float t0 = (float)k / ns, t1 = (float)(k + 1) / ns;
                float z0 = side * t0 * half, z1 = side * t1 * half, y0 = fy + dh * (1 - t0) * (1 - t0), y1 = fy + dh * (1 - t1) * (1 - t1);
                Vector3 n = new Vector3(0f, half, side * 2f * dh * (1 - (t0 + t1) * 0.5f)).normalized;
                defl.Quad(new Vector3(-dw, y0, z0), new Vector3(dw, y0, z0), new Vector3(dw, y1, z1), new Vector3(-dw, y1, z1), n);
                foreach (float sx in new[] { -dw, dw })
                    defl.Quad(new Vector3(sx, fy, z0), new Vector3(sx, fy, z1), new Vector3(sx, y1, z1), new Vector3(sx, y0, z0), new Vector3(Mathf.Sign(sx), 0, 0));
            }
        FlightBuilder.Obj("Pad_FlameTrench_Walls", walls, wallMat, parent);
        FlightBuilder.Obj("Pad_FlameTrench_Floor", floor, floorMat, parent);
        FlightBuilder.Obj("Pad_FlameDeflector", defl, steel, parent);
    }

    // the terrain's own height along the trench edges (the hardstand, or the slope north of it)
    static float Surface(float wx, float wz) => Mathf.Max(Designed(wx, wz), Grade);

    // trench walls: weathered concrete, darkening to soot toward the floor, with streaks
    static Texture2D TrenchWallTexture()
    {
        const int W = 256, H = 512;
        var px = new Color[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float v = (y + 0.5f) / H, u = (x + 0.5f) / W;
                float streak = Mathf.PerlinNoise(u * 40f, 0.3f) * 0.6f + Mathf.PerlinNoise(u * 9f, 3.1f) * 0.4f;
                float soot = Mathf.Lerp(0.22f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((v - 0.05f) / 0.9f + (streak - 0.5f) * 0.25f)));
                float lift = (y % 64) < 2 ? 0.88f : 1f;                                   // pour lines
                float g = 0.50f * soot * lift * (0.94f + 0.06f * Mathf.PerlinNoise(u * 60f, v * 30f));
                px[y * W + x] = new Color(g, g * 0.98f, g * 0.95f, 1f);
            }
        var t = new Texture2D(W, H, TextureFormat.RGBA32, true) { name = "Pad_TrenchWall", wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp };
        t.SetPixels(px); t.Apply(true);
        AssetDatabase.CreateAsset(t, FlightBuilder.Dir + "/Pad_TrenchWall.asset");
        return t;
    }
}
