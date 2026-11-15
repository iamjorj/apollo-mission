// The living Kennedy around the pad, made from the real USGS photo and heights:
//  - land types (lawn, scrub, sand, concrete) and water masks, read from every metre of the photo
//  - ground material: photo colour + real ground textures up close (Apollo/KSCGround)
//  - water: Atlantic, Banana River and lagoons with ripples and reflections (Apollo/KSCWater)
//  - plants: CC0 Poly Haven shrubs and Bermuda grass, and Florida cabbage palms built in code,
//    placed where the photo shows scrub, lawns and hammocks.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class KscEnvironment
{
    const string Dir = "Assets/Apollo/Models/Flight";
    const string Ground = "Assets/Apollo/Textures/Ground/";
    const string Plants = "Assets/Apollo/Models/Plants/";
    public const float WaterLevel = 0.35f;                  // metres above sea level (3DEP)

    public class Maps
    {
        public float[] Grass, Scrub, Sand, Hard;            // 2048 x 2048, 1 m, data coordinates
        public Texture2D Control, MaskInner, MaskMiddle, MaskOuter;
        public Color[] ClassMean = new Color[4];             // linear colour each land type really has at Kennedy
    }

    // ---------------- land types and water ----------------

    public static Maps Classify(string innerPhotoPath, string outerPhotoPath, float[] demInner, float[] demMiddle)
    {
        var m = new Maps();
        const int N = 2048;
        var photo = Load(innerPhotoPath);
        var px = photo.GetPixels32(); int pw = photo.width;
        m.Grass = new float[N * N]; m.Scrub = new float[N * N]; m.Sand = new float[N * N]; m.Hard = new float[N * N];
        var ctrl = new Color32[N * N]; var mi = new Color32[N * N];
        var sums = new Vector4[4];
        for (int j = 0; j < N; j++)                         // j = 0 is the south edge (texture row order)
            for (int i = 0; i < N; i++)
            {
                int sx = i * pw / N, sy = j * pw / N;
                Color c = Avg(px, pw, sx, sy, Mathf.Max(1, pw / N));
                float h = demInner[(N - 1 - j) * N + i];    // grid row 0 is the north edge
                float L = (c.r + c.g + c.b) / 3f, mx = Mathf.Max(c.r, Mathf.Max(c.g, c.b)), mn = Mathf.Min(c.r, Mathf.Min(c.g, c.b)), S = mx - mn;
                float G = c.g - (c.r + c.b) / 2f;
                float hard = Smooth(0.07f, 0.035f, S) * Smooth(0.46f, 0.60f, L);
                float sand = Smooth(0.36f, 0.50f, L) * Smooth(0.0f, 0.05f, c.r - c.b) * (1f - hard);
                float grass = Smooth(0.005f, 0.05f, G) * Smooth(0.24f, 0.36f, L) * (1f - hard);
                float scrub = Mathf.Clamp01(1f - grass - sand - hard) + 0.05f;
                float water = Smooth(0.6f, 0.4f, h);
                int k = j * N + i;
                var lin = new Vector4(Mathf.GammaToLinearSpace(c.r), Mathf.GammaToLinearSpace(c.g), Mathf.GammaToLinearSpace(c.b), 1f);
                if (water < 0.1f)
                {
                    if (grass > 0.7f) sums[0] += lin; else if (scrub > 0.75f) sums[1] += lin;
                    if (sand > 0.7f) sums[2] += lin; if (hard > 0.7f) sums[3] += lin;
                }
                // the pad as it was kept: mown grass on its slopes and around it, no bare sand inside the fence
                float dx = i + 0.5f - 1024f, dz = j + 0.5f - 1024f;
                float lawn = PadDesign.Lawn(dx, dz), fenced = PadDesign.Fenced(dx, dz);
                grass += (sand + scrub) * fenced; sand *= 1f - fenced; scrub *= 1f - fenced;
                grass = Mathf.Lerp(grass, 1f, lawn); sand *= 1f - lawn; hard *= 1f - lawn; scrub *= 1f - lawn;
                m.Grass[k] = grass; m.Scrub[k] = scrub * (1f - water); m.Sand[k] = sand; m.Hard[k] = hard;
                ctrl[k] = new Color32(B(grass), B(scrub), B(sand + water * 0.6f), B(hard));
                mi[k] = new Color32(B(water), 0, 0, 255);
            }
        // inside the fence, paved patches wider than a road (today's building pads and yards) become lawn;
        // roads (narrower than 15 m) stay. Morphological opening of the paved mask, radius 7 m.
        {
            const int R = 7;
            var hard = new byte[N * N];
            for (int q = 0; q < N * N; q++) hard[q] = (byte)(m.Hard[q] > 0.5f ? 1 : 0);
            byte[] Filter(byte[] src, bool erode)
            {
                var a = new byte[N * N]; var b = new byte[N * N];
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        byte v = (byte)(erode ? 1 : 0);
                        for (int d = -R; d <= R; d++) { byte u = src[y * N + Mathf.Clamp(x + d, 0, N - 1)]; v = erode ? (byte)(v & u) : (byte)(v | u); }
                        a[y * N + x] = v;
                    }
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        byte v = (byte)(erode ? 1 : 0);
                        for (int d = -R; d <= R; d++) { byte u = a[Mathf.Clamp(y + d, 0, N - 1) * N + x]; v = erode ? (byte)(v & u) : (byte)(v | u); }
                        b[y * N + x] = v;
                    }
                return b;
            }
            var blobs = Filter(Filter(hard, true), false);
            int moved = 0;
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    int k = j * N + i;
                    float fenced = PadDesign.Fenced(i + 0.5f - 1024f, j + 0.5f - 1024f);
                    if (blobs[k] == 0 || fenced <= 0f) continue;
                    float mv = m.Hard[k] * fenced;
                    m.Grass[k] += mv; m.Hard[k] -= mv; moved++;
                    var c = ctrl[k];
                    ctrl[k] = new Color32(B(m.Grass[k]), c.g, c.b, B(m.Hard[k]));
                }
            Debug.Log($"FLIGHT land types: {moved} m2 of paved yard inside the pad fence turned to lawn");
        }
        m.Control = Save("KSC_LandTypes", N, ctrl, true);
        for (int q = 0; q < 4; q++) m.ClassMean[q] = sums[q].w > 0 ? new Color(sums[q].x / sums[q].w, sums[q].y / sums[q].w, sums[q].z / sums[q].w) : new Color(0.2f, 0.2f, 0.15f);
        Debug.Log($"FLIGHT land colours (linear): grass {m.ClassMean[0]}, scrub {m.ClassMean[1]}, sand {m.ClassMean[2]}, hard {m.ClassMean[3]}");
        m.MaskInner = Save("KSC_Water_2km", N, mi, false);
        Object.DestroyImmediate(photo);

        var mm = new Color32[N * N];
        for (int j = 0; j < N; j++)
            for (int i = 0; i < N; i++)
                mm[j * N + i] = new Color32(B(Smooth(0.6f, 0.4f, demMiddle[(N - 1 - j) * N + i])), 0, 0, 255);
        m.MaskMiddle = Save("KSC_Water_8km", N, mm, false);

        var outer = Load(outerPhotoPath);
        var op = outer.GetPixels32(); int ow = outer.width;
        var mo = new Color32[N * N];
        for (int j = 0; j < N; j++)
            for (int i = 0; i < N; i++)
            {
                Color c = op[(j * ow / N) * ow + i * ow / N];
                bool sea = c.b > c.r + 0.03f && c.b > 0.18f;       // the Atlantic (lagoons there stay as photo)
                mo[j * N + i] = new Color32((byte)(sea ? 255 : 0), 0, 0, 255);
            }
        m.MaskOuter = Save("KSC_Water_40km", N, mo, false);
        Object.DestroyImmediate(outer);
        return m;
    }

    // Ground made only from ground materials (no photo). Each is tinted so its average colour is the
    // colour that kind of ground has in Florida sun (sRGB): mown lawn, open grassland, palmetto scrub, beach.
    public static Material GroundMaterial(Texture macro, Texture coast, float coastXMin, float coastZMin, float coastSize)
    {
        var mat = new Material(Shader.Find("Apollo/KSCGround")) { name = "Terrain_Kennedy" };
        string[] d = { "Grass004", "Ground037", "Ground054" };
        var avg = new Color[3];
        for (int i = 0; i < 3; i++)
        {
            mat.SetTexture("_D" + i, AssetDatabase.LoadAssetAtPath<Texture2D>(Ground + d[i] + "_Color.jpg"));
            mat.SetTexture("_N" + i, AssetDatabase.LoadAssetAtPath<Texture2D>(Ground + d[i] + "_NormalGL.jpg"));
            var t = Load(Ground + d[i] + "_Color.jpg");
            var px = t.GetPixels32(); double r = 0, g = 0, b = 0;
            for (int k = 0; k < px.Length; k += 7) { r += Mathf.GammaToLinearSpace(px[k].r / 255f); g += Mathf.GammaToLinearSpace(px[k].g / 255f); b += Mathf.GammaToLinearSpace(px[k].b / 255f); }
            int n = (px.Length + 6) / 7; Object.DestroyImmediate(t);
            avg[i] = new Color((float)(r / n), (float)(g / n), (float)(b / n));
        }
        Color Tint(Color srgbWant, Color texAvg)
        {
            Color w = srgbWant.linear;
            var tint = new Color(Mathf.Clamp(w.r / texAvg.r, 0.2f, 4f), Mathf.Clamp(w.g / texAvg.g, 0.2f, 4f), Mathf.Clamp(w.b / texAvg.b, 0.2f, 4f), 1f);
            return tint.gamma;                                  // material colour properties are sRGB
        }
        mat.SetColor("_Tint0", Tint(new Color(0.37f, 0.44f, 0.23f), avg[0]));     // mown lawn
        mat.SetColor("_Tint1", Tint(new Color(0.21f, 0.26f, 0.14f), avg[1]));     // saw-palmetto scrub
        mat.SetColor("_Tint2", Tint(new Color(0.80f, 0.75f, 0.62f), avg[2]));     // beach sand
        mat.SetColor("_Tint3", Tint(new Color(0.45f, 0.47f, 0.28f), avg[0]));     // open grassland
        mat.SetTexture("_Macro", macro);
        mat.SetTexture("_Coast", coast);
        mat.SetVector("_CoastRect", new Vector4(coastXMin, coastZMin, coastSize, 0));
        AssetDatabase.CreateAsset(mat, Dir + "/Terrain_Kennedy.mat");
        return mat;
    }

    // The coastline from the real heights (no photo): beach along the Atlantic, marsh edges along the
    // lagoons. 2048 x 2048 over the 8 km height grid (4 m), j = 0 at the south edge.
    public static Texture2D Coast(float[] demMiddle, Texture2D seaMask)
    {
        const int N = 2048; const float px = 4f;
        var water = new bool[N * N]; var sea = new bool[N * N];
        for (int j = 0; j < N; j++)
            for (int i = 0; i < N; i++)
            {
                float v = demMiddle[(N - 1 - j) * N + i];
                if (float.IsNaN(v) || v < -50f || v > 300f) v = 0.3f;
                int k = j * N + i;
                water[k] = v < 0.45f;
                float x = -4096f + (i + 0.5f) * px, z = -4096f + (j + 0.5f) * px;
                sea[k] = water[k] && seaMask.GetPixelBilinear((x + 20480f) / 40960f, (z + 20480f) / 40960f).r > 0.5f;
            }
        float[] Dist(bool[] src)
        {
            var d = new float[N * N];
            for (int k = 0; k < d.Length; k++) d[k] = src[k] ? 0f : 1e6f;
            for (int pass = 0; pass < 2; pass++)
                for (int y0 = 0; y0 < N; y0++)
                    for (int x0 = 0; x0 < N; x0++)
                    {
                        int y = pass == 0 ? y0 : N - 1 - y0, x = pass == 0 ? x0 : N - 1 - x0, s0 = pass == 0 ? -1 : 1, k = y * N + x;
                        float v = d[k];
                        if (y + s0 >= 0 && y + s0 < N) v = Mathf.Min(v, d[(y + s0) * N + x] + 1f);
                        if (x + s0 >= 0 && x + s0 < N) v = Mathf.Min(v, d[y * N + x + s0] + 1f);
                        if (y + s0 >= 0 && y + s0 < N && x + s0 >= 0 && x + s0 < N) v = Mathf.Min(v, d[(y + s0) * N + x + s0] + 1.414f);
                        if (y + s0 >= 0 && y + s0 < N && x - s0 >= 0 && x - s0 < N) v = Mathf.Min(v, d[(y + s0) * N + x - s0] + 1.414f);
                        d[k] = v;
                    }
            return d;
        }
        var dSea = Dist(sea); var dWater = Dist(water);
        var c = new Color32[N * N];
        for (int k = 0; k < c.Length; k++)
        {
            float beach = 1f - Smooth(30f, 60f, dSea[k] * px);
            float marsh = (1f - Smooth(4f, 16f, dWater[k] * px)) * (1f - beach);
            c[k] = new Color32(B(beach), B(marsh), 0, 255);
        }
        return Save("KSC_Coast", N, c, true);
    }

    // where the palmetto scrub patches are: the same noise the ground shader uses (0..1, scrub above 0.5)
    public static float ScrubNoise(Texture2D macro, float wx, float wz)
    {
        float n1 = macro.GetPixelBilinear(wx / 900f, wz / 900f).r;
        float n2 = macro.GetPixelBilinear(wx / 230f + 0.31f, wz / 230f + 0.31f).g;
        float n3 = macro.GetPixelBilinear(wx / 41f + 0.71f, wz / 41f + 0.71f).r;
        return n1 * 0.55f + n2 * 0.30f + n3 * 0.15f;
    }

    public static void Water(Transform parent, Maps m, Vector2 centreWorld, float padTop)
    {
        var mat = new Material(Shader.Find("Apollo/KSCWater")) { name = "Water_Kennedy" };
        mat.SetTexture("_MaskA", m.MaskInner); mat.SetTexture("_MaskB", m.MaskMiddle); mat.SetTexture("_MaskC", m.MaskOuter);
        mat.SetVector("_Half", new Vector4(1024, 4096, 20480, 0));
        mat.SetVector("_Centre", new Vector4(centreWorld.x, centreWorld.y, 0, 0));
        AssetDatabase.CreateAsset(mat, Dir + "/Water_Kennedy.mat");
        // a grid (not one quad) so the far water is not one giant triangle for fog and culling
        float y = WaterLevel - padTop, a = 20480f; int n = 40;
        var v = new List<Vector3>(); var t = new List<int>();
        for (int j = 0; j <= n; j++)
            for (int i = 0; i <= n; i++)
                v.Add(new Vector3(centreWorld.x - a + 2 * a * i / n, y, centreWorld.y - a + 2 * a * j / n));
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                int q = j * (n + 1) + i;
                t.AddRange(new[] { q, q + n + 1, q + 1, q + 1, q + n + 1, q + n + 2 });
            }
        var mesh = new Mesh { name = "Water", indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(v); mesh.SetTriangles(t, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, Dir + "/Water.asset");
        var go = new GameObject("Water_Atlantic_Lagoons");
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off;
    }

    // ---------------- plants ----------------

    public static void Plants_(Transform parent, Texture2D macro, Texture2D coast, float[] demInner, float axisX, float axisZ, float padTop, Vector2 introStart)
    {
        var root = new GameObject("Vegetation").transform;
        root.SetParent(parent, false);
        var rnd = new System.Random(1969);
        float H(float x, float z) => Sample(demInner, x, z);
        float R01() => (float)rnd.NextDouble();
        // scrub here? (pad frame) - same rule as the ground shader, away from the pad's lawn and the coast
        float Scrub(float wx, float wz)
        {
            if (new Vector2(wx, wz).magnitude < 470f) return 0f;
            var c = coast.GetPixelBilinear((wx + axisX + 4096f) / 8192f, (wz + axisZ + 4096f) / 8192f);
            if (c.r > 0.2f || c.g > 0.3f) return 0f;
            return Mathf.InverseLerp(0.50f, 0.60f, ScrubNoise(macro, wx, wz));
        }

        GameObject Prefab(string id) => AssetDatabase.LoadAssetAtPath<GameObject>(Plants + id + "/" + id + "_1k.gltf");
        var shrubs = new[] { Prefab("shrub_03"), Prefab("shrub_03"), Prefab("shrub_03"), Prefab("shrub_02"), Prefab("shrub_04") };
        var grass = Prefab("grass_bermuda_01");
        foreach (var p in shrubs) if (p == null) Debug.LogError("FLIGHT missing shrub model");

        void Place(GameObject prefab, Vector3 wpos, float scale, float cull, Transform under)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(under, false);
            go.transform.SetPositionAndRotation(wpos, Quaternion.Euler(0f, R01() * 360f, 0f));
            go.transform.localScale = Vector3.one * scale;
            var rs = go.GetComponentsInChildren<Renderer>();
            foreach (var r in rs) { r.shadowCastingMode = ShadowCastingMode.Off; foreach (var mat in r.sharedMaterials) if (mat) mat.enableInstancing = true; }
            var lod = go.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(cull, rs) });
            lod.RecalculateBounds();
        }

        // shrubs in the scrub, outside the pad fence (saw-palmetto country)
        var sRoot = new GameObject("Shrubs").transform; sRoot.SetParent(root, false);
        int placed = 0;
        for (int tries = 0; tries < 120000 && placed < 1100; tries++)
        {
            float wx = (R01() * 2f - 1f) * 1000f, wz = (R01() * 2f - 1f) * 1000f;
            float dx = wx + axisX, dz = wz + axisZ, h = H(dx, dz);
            if (h < 0.7f || h > 12.5f || R01() > Scrub(wx, wz)) continue;
            Place(shrubs[rnd.Next(shrubs.Length)], new Vector3(wx, h - padTop - 0.05f, wz), 0.9f + R01() * 0.9f, 0.007f, sRoot);
            placed++;
        }

        // Bermuda grass in the lawns near the pad and where the fly-in starts
        var gRoot = new GameObject("Grass").transform; gRoot.SetParent(root, false);
        int gp = 0;
        for (int tries = 0; tries < 80000 && gp < 0; tries++)          // tufts read as dark scribbles at this scale: off
        {
            bool nearStart = R01() < 0.55f;
            float wx, wz;
            if (nearStart) { wx = introStart.x + (R01() * 2f - 1f) * 70f; wz = introStart.y + (R01() * 2f - 1f) * 70f; }
            else { wx = (R01() * 2f - 1f) * 420f; wz = (R01() * 2f - 1f) * 420f; }
            if (new Vector2(wx, wz).magnitude < 80f) continue;
            float dx = wx + axisX, dz = wz + axisZ, h = H(dx, dz);
            if (h < 0.7f || h > 13.3f) continue;
            Place(grass, new Vector3(wx, h - padTop - 0.02f, wz), 0.8f + R01() * 0.8f, 0.004f, gRoot);
            gp++;
        }

        // cabbage palms (Florida's state tree) in hammocks
        var palms = new[] { Palm("A", 7.5f, 0.17f, 13, rnd), Palm("B", 9.5f, 0.16f, 15, rnd), Palm("C", 6.0f, 0.18f, 12, rnd) };
        var pRoot = new GameObject("CabbagePalms").transform; pRoot.SetParent(root, false);
        int pp = 0, clusters = 0;
        for (int tries = 0; tries < 20000 && clusters < 45; tries++)
        {
            float cx = (R01() * 2f - 1f) * 1150f, cz = (R01() * 2f - 1f) * 1150f;
            if (new Vector2(cx, cz).magnitude < 230f) continue;
            float hdx = cx + axisX, hdz = cz + axisZ;
            if (H(hdx, hdz) < 0.8f || Scrub(cx, cz) < 0.5f) continue;
            clusters++;
            int n = 3 + rnd.Next(8);
            for (int q = 0; q < n; q++)
            {
                float wx = cx + (R01() * 2f - 1f) * 22f, wz = cz + (R01() * 2f - 1f) * 22f;
                float dx = wx + axisX, dz = wz + axisZ, h = H(dx, dz);
                if (h < 0.7f || h > 12.5f) continue;
                Place(palms[rnd.Next(palms.Length)], new Vector3(wx, h - padTop - 0.1f, wz), 0.8f + R01() * 0.45f, 0.004f, pRoot);
                pp++;
            }
        }
        Debug.Log($"FLIGHT vegetation: {placed} shrubs, {gp} grass tufts, {pp} cabbage palms in {clusters} hammocks");
    }

    // A cabbage palm (Sabal palmetto): a slim leaning grey trunk with old leaf bases and a round crown of
    // costapalmate fan fronds on long stalks, built as one mesh (trunk + alpha-cut fronds).
    static GameObject Palm(string id, float height, float radius, int fronds, System.Random rnd)
    {
        string path = Dir + "/CabbagePalm_" + id + ".prefab";
        var frondTex = FrondTexture();
        var trunkMat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "PalmTrunk" };
        trunkMat.SetColor("_BaseColor", new Color(0.46f, 0.43f, 0.38f)); trunkMat.SetFloat("_Smoothness", 0.1f);
        trunkMat.enableInstancing = true;
        var leafMat = AssetDatabase.LoadAssetAtPath<Material>(Dir + "/PalmFrond.mat");
        if (leafMat == null)
        {
            leafMat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "PalmFrond" };
            leafMat.SetTexture("_BaseMap", frondTex);
            leafMat.SetColor("_BaseColor", Color.white);
            leafMat.SetFloat("_AlphaClip", 1f); leafMat.EnableKeyword("_ALPHATEST_ON"); leafMat.SetFloat("_Cutoff", 0.45f);
            leafMat.SetFloat("_Cull", 0f); leafMat.SetFloat("_Smoothness", 0.25f);
            leafMat.enableInstancing = true;
            leafMat.renderQueue = (int)RenderQueue.AlphaTest;
            AssetDatabase.CreateAsset(leafMat, Dir + "/PalmFrond.mat");
        }
        if (!AssetDatabase.Contains(trunkMat)) AssetDatabase.CreateAsset(trunkMat, Dir + "/PalmTrunk_" + id + ".mat");

        var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var tTrunk = new List<int>(); var tLeaf = new List<int>();
        // trunk: tapered, gently curved
        float lean = 0.06f + (float)rnd.NextDouble() * 0.12f;
        Vector3 Spine(float s) => new Vector3(lean * height * s * s, height * s, 0f);
        int rings = 10, seg = 8;
        for (int r = 0; r <= rings; r++)
        {
            float s = (float)r / rings; Vector3 c = Spine(s); float rad = radius * Mathf.Lerp(1.15f, 0.85f, s);
            for (int k = 0; k <= seg; k++)
            {
                float a = 2f * Mathf.PI * k / seg;
                Vector3 d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                float boot = s > 0.55f ? 1f + 0.18f * Mathf.Abs(Mathf.Sin(a * 3f + r)) : 1f;   // old leaf bases near the top
                v.Add(c + d * rad * boot); n.Add(d); uv.Add(new Vector2((float)k / seg, s * 4f));
            }
        }
        for (int r = 0; r < rings; r++)
            for (int k = 0; k < seg; k++)
            {
                int a = r * (seg + 1) + k, b = a + 1, c = a + seg + 1, d = c + 1;
                tTrunk.AddRange(new[] { a, c, b, b, c, d });
            }
        // crown: fronds on stalks, radiating and drooping
        Vector3 top = Spine(1f);
        for (int f = 0; f < fronds; f++)
        {
            float az = f * 2.39996f;                                   // golden angle
            float up = Mathf.Lerp(0.95f, -0.35f, (float)f / fronds) + ((float)rnd.NextDouble() - 0.5f) * 0.25f;
            Vector3 dir = new Vector3(Mathf.Cos(az), up, Mathf.Sin(az)).normalized;
            Vector3 stalkEnd = top + dir * 1.1f;
            Vector3 side = Vector3.Cross(Vector3.up, dir).normalized; if (side.sqrMagnitude < 0.01f) side = Vector3.right;
            Vector3 fanUp = Vector3.Cross(dir, side).normalized;
            // stalk (thin ribbon, uses the frond texture's solid base)
            AddQuad(v, n, uv, tLeaf, top - side * 0.03f, top + side * 0.03f, stalkEnd + side * 0.03f, stalkEnd - side * 0.03f, fanUp,
                new Vector2(0.48f, 0f), new Vector2(0.52f, 0f), new Vector2(0.52f, 0.06f), new Vector2(0.48f, 0.06f));
            // fan: square quad, folded slightly along its midrib, drooping at the tip
            float sz = 1.5f + (float)rnd.NextDouble() * 0.4f;
            Vector3 tip = stalkEnd + dir * sz + Vector3.down * sz * 0.35f;
            Vector3 mid = (stalkEnd + tip) * 0.5f + fanUp * 0.12f;
            AddQuad(v, n, uv, tLeaf, stalkEnd, stalkEnd, mid - side * sz * 0.55f, tip, fanUp, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 0.6f), new Vector2(0.5f, 1f));
            AddQuad(v, n, uv, tLeaf, stalkEnd, tip, mid + side * sz * 0.55f, stalkEnd, fanUp, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(1f, 0.6f), new Vector2(0.5f, 0f));
        }
        var mesh = new Mesh { name = "CabbagePalm_" + id, subMeshCount = 2 };
        mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv);
        mesh.SetTriangles(tTrunk, 0); mesh.SetTriangles(tLeaf, 1);
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, Dir + "/CabbagePalm_" + id + ".asset");
        var go = new GameObject("CabbagePalm_" + id);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterials = new[] { trunkMat, leafMat };
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return prefab;
    }

    static void AddQuad(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 nn,
                        Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
    {
        int i = v.Count;
        v.Add(a); v.Add(b); v.Add(c); v.Add(d);
        for (int k = 0; k < 4; k++) n.Add(nn);
        uv.Add(ua); uv.Add(ub); uv.Add(uc); uv.Add(ud);
        t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
    }

    // A fan frond painted in code: stiff segments radiating from the stalk, split at the tips.
    static Texture2D frondTex;
    static Texture2D FrondTexture()
    {
        if (frondTex != null) return frondTex;
        string p = Dir + "/PalmFrond_Texture.asset";
        frondTex = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
        if (frondTex != null) return frondTex;
        const int W = 256, Hh = 256;
        var t = new Texture2D(W, Hh, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
        var c = new Color[W * Hh];
        var rnd = new System.Random(7);
        int leaflets = 38;
        var len = new float[leaflets];
        for (int i = 0; i < leaflets; i++) len[i] = 0.82f + (float)rnd.NextDouble() * 0.18f;
        for (int y = 0; y < Hh; y++)
            for (int x = 0; x < W; x++)
            {
                float u = (x + 0.5f) / W - 0.5f, vv = (y + 0.5f) / Hh;           // fan origin at the bottom centre
                float r = Mathf.Sqrt(u * u + vv * vv), ang = Mathf.Atan2(u, vv);    // -90..90 degrees
                float a = 0f;
                if (vv < 0.07f && Mathf.Abs(u) < 0.025f) a = 1f;                  // stalk base
                else if (Mathf.Abs(ang) < 1.25f)
                {
                    float f = (ang + 1.25f) / 2.5f * leaflets;
                    int li = Mathf.Clamp((int)f, 0, leaflets - 1);
                    float within = f - li;                                         // 0..1 across one leaflet
                    float lmax = len[li];
                    float splitDepth = 0.30f;                                     // tips split into two points
                    bool split = r > lmax - splitDepth && Mathf.Abs(within - 0.5f) < 0.08f;
                    float width = Mathf.Lerp(0.46f, 0.30f, r / lmax);
                    if (r < lmax && Mathf.Abs(within - 0.5f) < width && !split) a = 1f;
                    if (r < 0.12f) a = 1f;
                }
                float shade = 0.85f + 0.15f * Mathf.Sin(ang * 60f);
                Color g = Color.Lerp(new Color(0.20f, 0.30f, 0.12f), new Color(0.38f, 0.46f, 0.20f), r) * shade;
                c[y * W + x] = new Color(g.r, g.g, g.b, a);
            }
        t.SetPixels(c); t.Apply(true);
        AssetDatabase.CreateAsset(t, p);
        frondTex = t;
        return t;
    }

    // ---------------- clouds ----------------

    // Tileable cloud fields: coverage (R big cells, G smaller variation) and 3D detail for the edges.
    public static void CloudTextures(out Texture2D coverage, out Texture3D detail)
    {
        const int N = 512;
        float[] a = Fbm2D(N, 4, 5, 11), b = Fbm2D(N, 8, 4, 23), w = Worley2D(N, 7, 31), w2 = Worley2D(N, 15, 37);
        var r = new float[N * N]; var g = new float[N * N];
        for (int i = 0; i < r.Length; i++) { r[i] = a[i] * 0.5f + (1 - w[i]) * 0.38f + (1 - w2[i]) * 0.12f; g[i] = b[i] * 0.55f + (1 - w2[i]) * 0.45f; }
        Normalize(r); Normalize(g);
        var px = new Color32[N * N];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(B(r[i]), B(g[i]), 0, 255);
        coverage = new Texture2D(N, N, TextureFormat.RGBA32, true, true) { name = "Cloud_Coverage", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
        coverage.SetPixels32(px); coverage.Apply(true, false);
        AssetDatabase.CreateAsset(coverage, Dir + "/Cloud_Coverage.asset");

        const int M = 64;
        var vox = new float[M * M * M];
        int[] cells = { 4, 8, 16 }; float[] wts = { 0.62f, 0.26f, 0.12f };
        for (int o = 0; o < 3; o++)
        {
            int C = cells[o]; var rnd = new System.Random(101 + o);
            var fp = new Vector3[C * C * C];
            for (int i = 0; i < fp.Length; i++) fp[i] = new Vector3((float)rnd.NextDouble(), (float)rnd.NextDouble(), (float)rnd.NextDouble());
            for (int z = 0; z < M; z++)
                for (int y = 0; y < M; y++)
                    for (int x = 0; x < M; x++)
                    {
                        Vector3 p = new Vector3((x + 0.5f) * C / M, (y + 0.5f) * C / M, (z + 0.5f) * C / M);
                        int cx = (int)p.x, cy = (int)p.y, cz = (int)p.z; float best = 9f;
                        for (int k = -1; k <= 1; k++) for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++)
                        {
                            int nx = cx + i, ny = cy + j, nz = cz + k;
                            Vector3 f = fp[((nz % C + C) % C) * C * C + ((ny % C + C) % C) * C + ((nx % C + C) % C)];
                            best = Mathf.Min(best, (new Vector3(nx, ny, nz) + f - p).sqrMagnitude);
                        }
                        vox[(z * M + y) * M + x] += wts[o] * (1f - Mathf.Clamp01(Mathf.Sqrt(best)));
                    }
        }
        Normalize(vox);
        var vp = new Color32[vox.Length];
        for (int i = 0; i < vox.Length; i++) vp[i] = new Color32(B(vox[i]), 0, 0, 255);
        detail = new Texture3D(M, M, M, TextureFormat.RGBA32, true) { name = "Cloud_Detail", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
        detail.SetPixels32(vp); detail.Apply(true, false);
        AssetDatabase.CreateAsset(detail, Dir + "/Cloud_Detail.asset");
    }

    static float[] Fbm2D(int n, int firstCells, int octaves, int seed)
    {
        var rnd = new System.Random(seed); var acc = new float[n * n]; float amp = 1f;
        for (int o = 0; o < octaves; o++)
        {
            int C = firstCells << o; var grid = new float[C * C];
            for (int i = 0; i < grid.Length; i++) grid[i] = (float)rnd.NextDouble();
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float gx = (float)x / n * C, gy = (float)y / n * C; int x0 = (int)gx, y0 = (int)gy;
                    float fx = gx - x0, fy = gy - y0; fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
                    float v00 = grid[(y0 % C) * C + x0 % C], v10 = grid[(y0 % C) * C + (x0 + 1) % C], v01 = grid[((y0 + 1) % C) * C + x0 % C], v11 = grid[((y0 + 1) % C) * C + (x0 + 1) % C];
                    acc[y * n + x] += amp * Mathf.Lerp(Mathf.Lerp(v00, v10, fx), Mathf.Lerp(v01, v11, fx), fy);
                }
            amp *= 0.5f;
        }
        return acc;
    }

    static float[] Worley2D(int n, int C, int seed)
    {
        var rnd = new System.Random(seed); var fp = new Vector2[C * C]; var o = new float[n * n];
        for (int i = 0; i < fp.Length; i++) fp[i] = new Vector2((float)rnd.NextDouble(), (float)rnd.NextDouble());
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                Vector2 p = new Vector2((x + 0.5f) * C / n, (y + 0.5f) * C / n); int cx = (int)p.x, cy = (int)p.y; float best = 9f;
                for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++)
                {
                    int nx = cx + i, ny = cy + j;
                    Vector2 f = fp[((ny % C + C) % C) * C + ((nx % C + C) % C)];
                    best = Mathf.Min(best, (new Vector2(nx, ny) + f - p).sqrMagnitude);
                }
                o[y * n + x] = Mathf.Clamp01(Mathf.Sqrt(best));
            }
        return o;
    }

    static void Normalize(float[] a)
    {
        float mn = float.MaxValue, mx = float.MinValue;
        foreach (float v in a) { mn = Mathf.Min(mn, v); mx = Mathf.Max(mx, v); }
        for (int i = 0; i < a.Length; i++) a[i] = (a[i] - mn) / Mathf.Max(mx - mn, 1e-6f);
    }

    // ---------------- the space center: landmarks around Pad 39A ----------------

    public static void SpaceCenter(Transform parent, float[] demMiddle, float axisX, float axisZ, float padTop, Material steel, Material concrete)
    {
        var root = new GameObject("SpaceCenter").transform;
        root.SetParent(parent, false);

        // Vehicle Assembly Building (NASA: 716 x 518 ft, 526 ft tall; low bay 442 x 274 ft, 210 ft, on the south side)
        float ft = 0.3048f, baseY = -0.8f - padTop;
        Vector3 vc = new Vector3(-4566f - axisX, baseY, -2483f - axisZ);
        float L = 716 * ft, W = 518 * ft, H = 526 * ft, hbD = 442 * ft, lbW = 442 * ft, lbD = 274 * ft, lbH = 210 * ft;
        Vector3 hb = vc + new Vector3(0, H / 2f, L / 2f - hbD / 2f), lb = vc + new Vector3(0, lbH / 2f, -L / 2f + lbD / 2f);
        var facade = Facade();
        var vabMat = Mat("VAB_Facade", Color.white, 0.25f, facade, new Vector2(1f / 12f, 1f / 12f));
        var doorMat = Mat("VAB_Doors", new Color(0.62f, 0.64f, 0.67f), 0.3f, facade, new Vector2(1f / 6f, 1f / 9f));
        var roofMat = Mat("VAB_Roof", new Color(0.42f, 0.43f, 0.44f), 0.2f, null, Vector2.one);
        var lccMat = Mat("LCC", new Color(0.86f, 0.86f, 0.84f), 0.3f, null, Vector2.one);
        var glass = Mat("LCC_Windows", new Color(0.07f, 0.09f, 0.11f), 0.92f, null, Vector2.one);
        Mesh("VAB_HighBay", g => g.Box(hb, new Vector3(W, H, hbD), Quaternion.identity), vabMat, root);
        Mesh("VAB_LowBay", g => g.Box(lb, new Vector3(lbW, lbH, lbD), Quaternion.identity), vabMat, root);
        Mesh("VAB_Roof", g => { g.Box(hb + Vector3.up * (H / 2f + 0.6f), new Vector3(W - 2f, 1.2f, hbD - 2f), Quaternion.identity); g.Box(lb + Vector3.up * (lbH / 2f + 0.5f), new Vector3(lbW - 2f, 1f, lbD - 2f), Quaternion.identity); }, roofMat, root);
        // four high-bay doors (two east, two west): the "inverted T", 456 ft tall, 152 ft wide at the bottom
        Mesh("VAB_HighBayDoors", g =>
        {
            foreach (float sx in new[] { -1f, 1f })
                foreach (float dz in new[] { -hbD / 4f, hbD / 4f })
                {
                    Vector3 f = new Vector3(hb.x + sx * (W / 2f + 0.4f), baseY, hb.z + dz);
                    g.Box(f + Vector3.up * (114 * ft / 2f), new Vector3(0.8f, 114 * ft, 152 * ft), Quaternion.identity);
                    g.Box(f + Vector3.up * (114 * ft + 342 * ft / 2f), new Vector3(0.8f, 342 * ft, 76 * ft), Quaternion.identity);
                }
        }, doorMat, root);
        // the flag on the south face of the high bay (209 x 110 ft), above the low bay roof
        {
            var flagMat = Mat("VAB_Flag", Color.white, 0.3f, Flag(), Vector2.one);
            float fw = 209 * ft, fh = 110 * ft;
            Vector3 c = new Vector3(hb.x - W * 0.22f, baseY + lbH + (H - lbH) * 0.55f, hb.z - hbD / 2f - 0.3f);
            var m = new Mesh { name = "VAB_Flag" };
            m.vertices = new[] { c + new Vector3(fw / 2f, -fh / 2f, 0), c + new Vector3(-fw / 2f, -fh / 2f, 0), c + new Vector3(-fw / 2f, fh / 2f, 0), c + new Vector3(fw / 2f, fh / 2f, 0) };
            m.uv = new[] { new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.RecalculateNormals(); m.RecalculateBounds();
            AssetDatabase.CreateAsset(m, Dir + "/VAB_Flag.asset");
            var go = new GameObject("VAB_Flag"); go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = m; go.AddComponent<MeshRenderer>().sharedMaterial = flagMat;
        }
        // Launch Control Center beside the VAB, its firing-room windows facing the pads
        Vector3 lc = vc + new Vector3(W / 2f + 95f, 0, -L / 2f + 40f);
        Mesh("LaunchControlCenter", g => { g.Box(lc + Vector3.up * 11.7f, new Vector3(55f, 23.4f, 115f), Quaternion.identity); g.Box(lc + new Vector3(-(W / 2f + 95f) / 2f + 27f, 16f, 0f), new Vector3(W / 2f + 95f - 55f - 2f, 5f, 8f), Quaternion.identity); }, lccMat, root);
        Mesh("LCC_FiringRoomWindows", g => g.Box(lc + new Vector3(27.6f, 16.5f, 0f), new Vector3(0.4f, 5f, 104f), Quaternion.identity), glass, root);
        Mesh("VAB_Apron", g => g.Box(vc + new Vector3(60f, 0.05f, -20f), new Vector3(480f, 0.1f, 520f), Quaternion.identity), concrete, root);

        // Pad 39B, 2.7 km north-west: three 600 ft lightning towers with catenary wires
        Vector3 p39b = new Vector3(-1615f - axisX, 0, 2209f - axisZ);
        var tops = new List<Vector3>();
        Mesh("Pad39B_LightningTowers", g =>
        {
            for (int k = 0; k < 3; k++)
            {
                float az = (30f + 120f * k) * Mathf.Deg2Rad;
                Vector3 b = p39b + new Vector3(Mathf.Sin(az), 0, Mathf.Cos(az)) * 120f;
                b.y = MiddleHeight(demMiddle, b.x + axisX, b.z + axisZ) - padTop;
                float h = 150f;
                for (int s = 0; s < 15; s++)
                {
                    float y0 = s * 10f, y1 = y0 + 10f, w0 = Mathf.Lerp(4f, 1.2f, y0 / h), w1 = Mathf.Lerp(4f, 1.2f, y1 / h);
                    for (int c = 0; c < 4; c++)
                    {
                        float a0 = c * Mathf.PI / 2f + Mathf.PI / 4f, a1 = (c + 1) * Mathf.PI / 2f + Mathf.PI / 4f;
                        Vector3 d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * 1.4142f, d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * 1.4142f;
                        Vector3 p00 = b + d0 * w0 + Vector3.up * y0, p01 = b + d0 * w1 + Vector3.up * y1, p10 = b + d1 * w0 + Vector3.up * y0, p11 = b + d1 * w1 + Vector3.up * y1;
                        g.Beam(p00, p01, 0.45f, 0.45f); g.Beam(p01, p11, 0.25f, 0.25f); g.Beam(p00, p11, 0.18f, 0.18f); g.Beam(p10, p01, 0.18f, 0.18f);
                    }
                }
                g.Cyl(b + Vector3.up * h, b + Vector3.up * (h + 33f), 0.45f, 0.12f, 10);
                tops.Add(b + Vector3.up * (h + 30f));
            }
            for (int k = 0; k < 3; k++)
            {
                Vector3 a = tops[k], c = tops[(k + 1) % 3]; Vector3 prev = a;
                for (int q = 1; q <= 16; q++)
                {
                    float t = q / 16f; Vector3 pnt = Vector3.Lerp(a, c, t) + Vector3.down * (4f * 28f * t * (1 - t));
                    g.Cyl(prev, pnt, 0.12f, 0.12f, 5); prev = pnt;
                }
            }
        }, steel, root);
    }

    static float MiddleHeight(float[] g, float x, float z)
    {
        const float half = 4096f;
        float px = (x + half) / (2f * half) * 2048f - 0.5f, py = (half - z) / (2f * half) * 2048f - 0.5f;
        int x0 = Mathf.Clamp(Mathf.FloorToInt(px), 0, 2047), y0 = Mathf.Clamp(Mathf.FloorToInt(py), 0, 2047);
        float v = g[y0 * 2048 + x0];
        return float.IsNaN(v) || v < -50f || v > 300f ? 1.5f : v;
    }

    static Material Mat(string name, Color c, float smooth, Texture tex, Vector2 tiling)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
        m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth);
        if (tex) { m.SetTexture("_BaseMap", tex); m.SetTextureScale("_BaseMap", tiling); }
        AssetDatabase.CreateAsset(m, Dir + "/" + name + ".mat");
        return m;
    }

    static void Mesh(string name, Action<Geo> build, Material m, Transform parent)
    {
        var g = new Geo(); build(g);
        var mesh = g.ToMesh(name);
        AssetDatabase.CreateAsset(mesh, Dir + "/" + name + ".asset");
        var go = new GameObject(name); go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = m;
        go.isStatic = false;   // under the moving world root: never static-batched
    }

    // VAB siding: tall light-grey panels with vertical ribs and horizontal joints, each panel a slightly different shade
    static Texture2D Facade()
    {
        const int N = 512; var t = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "VAB_Facade", wrapMode = TextureWrapMode.Repeat, anisoLevel = 8 };
        var rnd = new System.Random(5); var px = new Color[N * N]; var shade = new float[8 * 4];
        for (int i = 0; i < shade.Length; i++) shade[i] = 0.93f + (float)rnd.NextDouble() * 0.07f;
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int col = x / 64, row = y / 128;
                float s = shade[row * 8 + col], rib = (x % 16) < 2 ? 0.9f : 1f, seam = (x % 64) < 2 || (y % 128) < 2 ? 0.72f : 1f;
                var c = new Color(0.76f, 0.78f, 0.80f) * s * rib * seam; c.a = 1f;
                px[y * N + x] = c;
            }
        t.SetPixels(px); t.Apply(true);
        AssetDatabase.CreateAsset(t, Dir + "/VAB_Facade.asset");
        return t;
    }

    // The US flag: 13 stripes, a blue canton with 50 stars (rows of 6 and 5)
    static Texture2D Flag()
    {
        const int W = 760, H = 400; var t = new Texture2D(W, H, TextureFormat.RGBA32, true) { name = "Flag", wrapMode = TextureWrapMode.Clamp };
        var px = new Color[W * H];
        Color red = new Color(0.70f, 0.13f, 0.20f), white = new Color(0.95f, 0.95f, 0.95f), blue = new Color(0.16f, 0.20f, 0.41f);
        float cw = W * 0.4f, ch = H * 7f / 13f;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int stripe = (int)((H - 1 - y) * 13f / H);
                Color c = stripe % 2 == 0 ? red : white;
                if (x < cw && y > H - ch)
                {
                    c = blue;
                    float u = x / cw, v = (H - y) / ch;
                    for (int r = 0; r < 9; r++)
                    {
                        int n = r % 2 == 0 ? 6 : 5;
                        for (int k = 0; k < n; k++)
                        {
                            float sx = r % 2 == 0 ? (k + 0.5f) / 6f : (k + 1f) / 6f, sy = (r + 1f) / 10f;
                            if (Mathf.Pow((u - sx) * cw, 2) + Mathf.Pow((v - sy) * ch, 2) < 64f) c = white;
                        }
                    }
                }
                px[y * W + x] = c;
            }
        t.SetPixels(px); t.Apply(true);
        AssetDatabase.CreateAsset(t, Dir + "/Flag.asset");
        return t;
    }

    // minimal mesh builder (same conventions as FlightBuilder's)
    class Geo
    {
        readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>(); readonly List<Vector2> uv = new List<Vector2>(); readonly List<int> t = new List<int>();
        static Vector2 UV(Vector3 p, Vector3 nn) => Mathf.Abs(nn.y) > 0.7f ? new Vector2(p.x, p.z) : Mathf.Abs(nn.x) > Mathf.Abs(nn.z) ? new Vector2(p.z, p.y) : new Vector2(p.x, p.y);
        void Add(Vector3 p, Vector3 nn) { v.Add(p); n.Add(nn); uv.Add(UV(p, nn)); }
        public void QuadN(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd)
        {
            int i = v.Count; Add(a, na); Add(b, nb); Add(c, nc); Add(d, nd);
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), na + nb + nc + nd) >= 0) t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            else t.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
        }
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 nn) => QuadN(a, b, c, d, nn, nn, nn, nn);
        public void Box(Vector3 c, Vector3 s, Quaternion r)
        {
            Vector3 X = r * Vector3.right, Y = r * Vector3.up, Z = r * Vector3.forward, ax = X * s.x * 0.5f, ay = Y * s.y * 0.5f, az = Z * s.z * 0.5f;
            void F(Vector3 fc, Vector3 a, Vector3 b, Vector3 nn) => Quad(fc - a - b, fc + a - b, fc + a + b, fc - a + b, nn);
            F(c + ax, ay, az, X); F(c - ax, ay, az, -X); F(c + ay, ax, az, Y); F(c - ay, ax, az, -Y); F(c + az, ax, ay, Z); F(c - az, ax, ay, -Z);
        }
        public void Beam(Vector3 a, Vector3 b, float w, float h)
        {
            Vector3 d = b - a; float len = d.magnitude; if (len < 1e-4f) return;
            Box((a + b) * 0.5f, new Vector3(w, h, len), Quaternion.LookRotation(d, Mathf.Abs(d.y) > 0.99f * len ? Vector3.forward : Vector3.up));
        }
        public void Cyl(Vector3 a, Vector3 b, float r0, float r1, int seg)
        {
            Vector3 ax = (b - a).normalized, u = Vector3.Cross(ax, Mathf.Abs(ax.y) < 0.9f ? Vector3.up : Vector3.right).normalized, w = Vector3.Cross(ax, u);
            for (int i = 0; i < seg; i++)
            {
                float t0 = 2f * Mathf.PI * i / seg, t1 = 2f * Mathf.PI * (i + 1) / seg;
                Vector3 d0 = u * Mathf.Cos(t0) + w * Mathf.Sin(t0), d1 = u * Mathf.Cos(t1) + w * Mathf.Sin(t1);
                QuadN(a + d0 * r0, a + d1 * r0, b + d1 * r1, b + d0 * r1, d0, d1, d1, d0);
            }
        }
        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateBounds();
            return m;
        }
    }

    // ---------------- helpers ----------------

    static Texture2D Load(string path)
    {
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        t.LoadImage(File.ReadAllBytes(path));
        return t;
    }

    static Color Avg(Color32[] px, int w, int x, int y, int n)
    {
        int r = 0, g = 0, b = 0, c = 0;
        for (int j = 0; j < n; j++) for (int i = 0; i < n; i++) { var p = px[Mathf.Min(y + j, w - 1) * w + Mathf.Min(x + i, w - 1)]; r += p.r; g += p.g; b += p.b; c++; }
        return new Color(r / (255f * c), g / (255f * c), b / (255f * c));
    }

    static float Smooth(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((x - a) / (b - a)));
    static byte B(float x) => (byte)Mathf.RoundToInt(Mathf.Clamp01(x) * 255f);

    static Texture2D Save(string name, int n, Color32[] px, bool linear)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, true, true) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        t.SetPixels32(px); t.Apply(true);
        AssetDatabase.CreateAsset(t, Dir + "/" + name + ".asset");
        return t;
    }

    // height (m above sea level) from the 2 km grid at data coordinates
    public static float Sample(float[] g, float x, float z)
    {
        float px = x + 1024f - 0.5f, py = 1024f - z - 0.5f;
        int x0 = Mathf.Clamp(Mathf.FloorToInt(px), 0, 2046), y0 = Mathf.Clamp(Mathf.FloorToInt(py), 0, 2046);
        float fx = Mathf.Clamp01(px - x0), fy = Mathf.Clamp01(py - y0);
        float V(int xx, int yy) { float v = g[yy * 2048 + xx]; return float.IsNaN(v) || v < -50f || v > 300f ? 0.3f : v; }
        return Mathf.Lerp(Mathf.Lerp(V(x0, y0), V(x0 + 1, y0), fx), Mathf.Lerp(V(x0, y0 + 1), V(x0 + 1, y0 + 1), fx), fy);
    }
}
