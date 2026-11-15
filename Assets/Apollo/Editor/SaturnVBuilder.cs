// Builds the Apollo 11 Saturn V (AS-506) from NASA's published dimensions, in metres, 1 unit = 1 m.
// Heights come from Saturn V vehicle stations (inches): y = (STA + 110) * 0.0254, so y = 0 is the
// F-1 nozzle exit plane and the escape tower tip is at 110.6 m (363 ft).
//   STA  100  F-1 gimbal plane            y  5.33
//   STA 1541  S-IC / S-II interstage       y 41.93
//   STA 1760  interstage / S-II            y 47.50
//   STA 2519  S-II / S-IVB interstage      y 66.78
//   STA 3223  S-IVB / Instrument Unit      y 84.66
//   STA 3259  IU / Spacecraft-LM Adapter   y 85.57
// Diameters: S-IC and S-II 33 ft (10.06 m), S-IVB and IU 21.7 ft (6.60 m), SM 12.8 ft (3.91 m).
// Paint (Apollo 11): four-fold roll pattern, black from each Position (I-IV) to the next fin, on the
// S-IC from the base to the intertank and from the S-IC forward skirt to the S-II aft skirt division;
// quadrant code on the S-II/S-IVB interstage; IU and top of the S-IVB forward skirt black all round;
// four "USA" and four flags on the S-IC, four "UNITED STATES" on the S-II.
// Every stage is its own object, pivoted at its separation plane, so real staging can be shown.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class SaturnVBuilder
{
    const string Dir = "Assets/Apollo/Models/SaturnV_AS506";
    const string PrefabPath = "Assets/Apollo/Prefabs/SaturnV_AS506.prefab";
    const float D2R = Mathf.Deg2Rad;

    // Heights (m)
    const float Y_Gimbal = 5.33f, Y_Base = 5.0f, Y_Intertank = 19.03f, Y_LoxTank = 25.58f;
    const float Y_FwdSkirt = 39.13f, Y_SIC_Top = 41.93f, Y_SII_Bottom = 47.50f, Y_SII_AftDiv = 49.71f;
    const float Y_SII_Top = 66.78f, Y_SIVB_Bottom = 72.55f, Y_SIVB_Top = 84.66f, Y_IU_Top = 85.57f;
    const float Y_SLA_Top = 94.11f, Y_SM_Top = 98.02f, Y_Top = 110.6f;
    const float R_Big = 5.03f, R_SIVB = 3.30f, R_SM = 1.955f;

    // Palette (submesh index -> material)
    const int WHITE = 0, BLACK = 1, ENGINE = 2, NOZZLE_IN = 3, ALU = 4, CORK = 5, SILVER = 6, GREY = 7, DARK = 8;
    static Material[] palette;

    [MenuItem("Apollo/Build Saturn V (AS-506)")]
    public static void Build()
    {
        j2Cache = null;
        meshMats.Clear();
        if (AssetDatabase.IsValidFolder(Dir)) AssetDatabase.DeleteAsset(Dir);
        Directory.CreateDirectory(Dir);
        AssetDatabase.Refresh();
        palette = MakePalette();

        var root = new GameObject("SaturnV_AS506");

        // ---------------- S-IC first stage ----------------
        var sic = Part(root, "S-IC", 0f);
        {
            var mb = new MeshBuild();
            // heat shield at the base of the thrust structure
            Lathe(mb, new[] { Y_Base, Y_Base }, new[] { 0f, R_Big }, 96, (a, y) => DARK);
            // aft skirt + RP-1 fuel tank (roll pattern), intertank and LOX tank (white), forward skirt (roll pattern)
            Lathe(mb, new[] { Y_Base, Y_Intertank }, new[] { R_Big, R_Big }, 192, SicColor);
            Lathe(mb, new[] { Y_Intertank, Y_LoxTank }, new[] { R_Big, R_Big }, 864, SicColor, Corrugate(216, 0.045f));
            Lathe(mb, new[] { Y_LoxTank, Y_FwdSkirt }, new[] { R_Big, R_Big }, 192, SicColor);
            Lathe(mb, new[] { Y_FwdSkirt, Y_SIC_Top - 0.12f }, new[] { R_Big, R_Big }, 864, SicColor, Corrugate(216, 0.04f));
            Lathe(mb, new[] { Y_SIC_Top - 0.12f, Y_SIC_Top - 0.12f, Y_SIC_Top, Y_SIC_Top }, new[] { R_Big, R_Big + 0.05f, R_Big + 0.05f, R_Big }, 192, (a, y) => WHITE);
            AddMesh(sic, "S-IC_Body", mb);

            // F-1 engines: centre engine fixed, four outboard engines on the fin lines
            var f1 = BuildF1();
            AddInstance(sic, "F-1_Centre", f1, Vector3.zero, 0f);
            for (int i = 0; i < 4; i++)
            {
                float az = 45f + 90f * i;
                AddInstance(sic, "F-1_Outboard_" + (i + 1), f1, Polar(az, 3.65f, 0f), 0f);
            }

            // Engine fairings (with the retro-rockets) and fins
            var fair = new MeshBuild();
            var fins = new MeshBuild();
            for (int i = 0; i < 4; i++)
            {
                float az = 45f + 90f * i;
                Vector3 axis = Polar(az, 3.65f, 0f);
                var ys = new[] { 1.1f, 5.6f, 8.0f, 10.4f };
                var rs = new[] { 2.25f, 2.25f, 1.35f, 0.15f };
                Lathe(fair, ys, rs, 96, SicColor, null, axis);
                Lathe(fair, ys, Array.ConvertAll(rs, r => r - 0.04f), 96, (a, y) => DARK, null, axis, true);
                Fin(fins, az);
            }
            AddMesh(sic, "S-IC_Fairings", fair);
            AddMesh(sic, "S-IC_Fins", fins);
        }

        // ---------------- S-II aft interstage (jettisoned ~30 s after S-IC separation) ----------------
        var inter = Part(root, "S-II_AftInterstage", Y_SIC_Top);
        {
            var mb = new MeshBuild();
            Lathe(mb, new[] { Y_SIC_Top, Y_SII_Bottom }, new[] { R_Big, R_Big }, 864, SicColor, Corrugate(216, 0.03f));
            Lathe(mb, new[] { Y_SII_Bottom - 0.1f, Y_SII_Bottom - 0.1f, Y_SII_Bottom, Y_SII_Bottom }, new[] { R_Big, R_Big + 0.05f, R_Big + 0.05f, R_Big }, 192, (a, y) => WHITE);
            // ullage-motor fairings, about 4.5 deg anticlockwise of the fin lines
            for (int i = 0; i < 4; i++)
                Lathe(mb, new[] { 42.2f, 42.2f, 44.9f, 45.6f }, new[] { 0f, 0.32f, 0.32f, 0f }, 32, SicColor, null, Polar(45f + 90f * i - 4.5f, R_Big, 0f));
            AddMesh(inter, "S-II_AftInterstage_Body", mb);
        }

        // ---------------- S-II second stage (with the S-II/S-IVB interstage, which stays with it) ----------------
        var sii = Part(root, "S-II", Y_SII_Bottom);
        {
            var mb = new MeshBuild();
            Lathe(mb, new[] { Y_SII_Bottom, Y_SII_Bottom }, new[] { 0f, R_Big }, 96, (a, y) => DARK);
            Lathe(mb, new[] { Y_SII_Bottom, Y_SII_AftDiv }, new[] { R_Big, R_Big }, 192, SicColor);
            Lathe(mb, new[] { Y_SII_AftDiv, Y_SII_AftDiv, Y_SII_AftDiv + 0.1f, Y_SII_AftDiv + 0.1f }, new[] { R_Big, R_Big + 0.04f, R_Big + 0.04f, R_Big }, 192, (a, y) => WHITE);
            Lathe(mb, new[] { Y_SII_AftDiv + 0.1f, 64.4f }, new[] { R_Big, R_Big }, 192, (a, y) => WHITE);
            Lathe(mb, new[] { 64.4f, Y_SII_Top }, new[] { R_Big, R_Big }, 864, (a, y) => WHITE, Corrugate(216, 0.03f));
            // S-II/S-IVB interstage: cone 33 ft -> 21.7 ft, quadrant code split at mid-length
            Lathe(mb, new[] { Y_SII_Top, Y_SIVB_Bottom }, new[] { R_Big, R_SIVB }, 192, QuadrantColor);
            Lathe(mb, new[] { Y_SIVB_Bottom, Y_SIVB_Bottom }, new[] { R_SIVB, 0f }, 96, (a, y) => DARK);
            AddMesh(sii, "S-II_Body", mb);

            var j2 = BuildJ2();
            AddInstance(sii, "J-2_Centre", j2, new Vector3(0f, Y_SII_Bottom - 3.38f, 0f), 0f);
            for (int i = 0; i < 4; i++)
                AddInstance(sii, "J-2_Outboard_" + (i + 1), j2, Polar(45f + 90f * i, 2.67f, Y_SII_Bottom - 3.38f), 0f);
        }

        // ---------------- S-IVB third stage ----------------
        var sivb = Part(root, "S-IVB", Y_SIVB_Bottom);
        {
            var mb = new MeshBuild();
            Lathe(mb, new[] { Y_SIVB_Bottom, Y_SIVB_Bottom }, new[] { 0f, R_SIVB }, 96, (a, y) => DARK);
            Lathe(mb, new[] { Y_SIVB_Bottom, Y_SIVB_Top - 0.7f }, new[] { R_SIVB, R_SIVB }, 160, (a, y) => WHITE);
            Lathe(mb, new[] { Y_SIVB_Top - 0.7f, Y_SIVB_Top }, new[] { R_SIVB, R_SIVB }, 640, (a, y) => BLACK, Corrugate(160, 0.025f));
            // two auxiliary propulsion system modules at Positions I and III
            for (int i = 0; i < 2; i++)
                Lathe(mb, new[] { 72.62f, 72.62f, 74.6f, 75.2f }, new[] { 0f, 0.58f, 0.58f, 0f }, 40, (a, y) => WHITE, null, Polar(180f * i, R_SIVB + 0.22f, 0f));
            // cable tunnel along the stage
            Lathe(mb, new[] { 73.1f, 73.1f, 83.9f, 83.9f }, new[] { 0f, 0.17f, 0.17f, 0f }, 24, (a, y) => WHITE, null, Polar(90f, R_SIVB, 0f));
            AddMesh(sivb, "S-IVB_Body", mb);
            AddInstance(sivb, "J-2", BuildJ2(), new Vector3(0f, Y_SIVB_Bottom - 3.38f, 0f), 0f);
        }

        // ---------------- Instrument Unit (black all round) ----------------
        var iu = Part(root, "IU", Y_SIVB_Top);
        {
            var mb = new MeshBuild();
            Lathe(mb, new[] { Y_SIVB_Top, Y_IU_Top }, new[] { R_SIVB, R_SIVB }, 160, (a, y) => BLACK);
            AddMesh(iu, "IU_Body", mb);
        }

        // ---------------- Spacecraft-LM Adapter (cork-covered panels) ----------------
        var sla = Part(root, "SLA", Y_IU_Top);
        {
            var mb = new MeshBuild();
            Func<float, float, int> slaColor = (a, y) =>
                (a % 90f) < 1.6f || Mathf.Abs(y - 87.70f) < 0.04f ? GREY : CORK;
            Lathe(mb, new[] { Y_IU_Top, 87.66f, 87.74f, Y_SLA_Top }, new[] { R_SIVB, R_SIVB - 0.090f, R_SIVB - 0.092f, R_SM }, 224, slaColor);
            AddMesh(sla, "SLA_Panels", mb);
        }

        // ---------------- Service Module ----------------
        var sm = Part(root, "SM", Y_SLA_Top);
        {
            var mb = new MeshBuild();
            Lathe(mb, new[] { Y_SLA_Top, Y_SM_Top - 0.45f, Y_SM_Top - 0.1f, Y_SM_Top }, new[] { R_SM, R_SM, R_SM, R_SM - 0.06f }, 160,
                (a, y) => y > Y_SM_Top - 0.45f || (a > 120f && a < 146f) ? WHITE : ALU);
            // four RCS quads, 7.25 deg off the Positions
            for (int i = 0; i < 4; i++)
            {
                Vector3 c = Polar(7.25f + 90f * i, R_SM, 0f);
                Lathe(mb, new[] { 96.55f, 96.55f, 97.55f, 97.55f }, new[] { 0f, 0.33f, 0.33f, 0f }, 24, (a, y) => ALU, null, c);
                foreach (float dy in new[] { -0.62f, 0.62f })
                    Lathe(mb, new[] { 97.05f + dy * 0.82f, 97.05f + dy * 0.82f + Mathf.Sign(dy) * 0.18f }, new[] { 0.05f, 0.09f }, 12, (a, y) => DARK, null, c + Polar(7.25f + 90f * i, 0.15f, 0f));
            }
            AddMesh(sm, "SM_Body", mb);
        }

        // ---------------- Command Module (silver; hidden under the Boost Protective Cover at launch) ----------------
        var cm = Part(root, "CM", Y_SM_Top);
        {
            var mb = new MeshBuild();
            Lathe(mb, new[] { Y_SM_Top, Y_SM_Top + 0.25f, 100.85f, 101.05f, 101.25f, 101.25f },
                new[] { R_SM - 0.02f, R_SM - 0.05f, 0.62f, 0.45f, 0.42f, 0f }, 96, (a, y) => SILVER);
            AddMesh(cm, "CM_Shell", mb);
        }

        // ---------------- Launch Escape System + Boost Protective Cover (jettisoned together) ----------------
        var les = Part(root, "LES", Y_SM_Top);
        {
            var mb = new MeshBuild();
            // boost protective cover over the CM
            Lathe(mb, new[] { Y_SM_Top - 0.05f, Y_SM_Top + 0.3f, 100.95f, 101.3f, 101.42f },
                new[] { R_SM + 0.04f, R_SM, 0.66f, 0.45f, 0.0f }, 96, (a, y) => WHITE);
            // tower truss: four longerons and cross-bracing (titanium tubing)
            Vector3[] b = new Vector3[4], t = new Vector3[4];
            for (int i = 0; i < 4; i++)
            {
                float az = 45f + 90f * i;
                b[i] = Polar(az, 0.80f, 100.75f);
                t[i] = Polar(az, 0.40f, 103.60f);
                Tube(mb, b[i], t[i], 0.045f, GREY);
            }
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                Vector3 bm = Vector3.Lerp(b[i], t[i], 0.5f), bmj = Vector3.Lerp(b[j], t[j], 0.5f);
                Tube(mb, b[i], bmj, 0.03f, GREY);
                Tube(mb, bm, t[j], 0.03f, GREY);
                Tube(mb, t[i], t[j], 0.03f, GREY);
                Tube(mb, bm, bmj, 0.03f, GREY);
            }
            // structural skirt with the four escape-motor nozzles, motor case, tower-jettison motor,
            // pitch-control motor, canard section and Q-ball nose cone
            Lathe(mb, new[] { 103.55f, 103.55f, 104.25f },
                new[] { 0f, 0.42f, 0.35f }, 48, (a, y) => GREY);
            for (int i = 0; i < 4; i++)
            {
                Vector3 c = Polar(45f + 90f * i, 0.30f, 103.9f);
                Lathe(mb, new[] { -0.2f, 0.2f }, new[] { 0.13f, 0.07f }, 16, (a, y) => DARK, null, c);
            }
            Lathe(mb, new[] { 104.25f, 108.3f }, new[] { 0.33f, 0.33f }, 48, (a, y) => WHITE);
            Lathe(mb, new[] { 108.3f, 108.55f, 109.15f }, new[] { 0.33f, 0.30f, 0.26f }, 48, (a, y) => GREY);
            Lathe(mb, new[] { 109.15f, 109.75f, 110.05f, 110.5f, Y_Top - 0.05f, Y_Top }, new[] { 0.26f, 0.26f, 0.21f, 0.06f, 0.03f, 0f }, 48,
                (a, y) => y > 110.4f ? DARK : WHITE);
            AddMesh(les, "LES_and_BPC", mb);
        }

        // ---------------- Markings ----------------
        var usaTex = LoadMarking("Marking_USA.png");
        var usTex = LoadMarking("Marking_UNITED_STATES.png");
        var flagTex = FlagTexture(Dir + "/Marking_Flag.png");
        var letters = DecalMaterial("Paint_Letters", usaTex, Color.black);
        var lettersUS = DecalMaterial("Paint_Letters_UNITED_STATES", usTex, Color.black);
        var flag = DecalMaterial("Paint_Flag", flagTex, Color.white);
        for (int i = 0; i < 4; i++)
        {
            float az = 67.5f + 90f * i;     // centre of each white sector
            Decal(sic, "USA_" + (i + 1), letters, usaTex, az, R_Big, 11.4f, 6.0f);
            Decal(sic, "Flag_" + (i + 1), flag, flagTex, az, R_Big, 16.4f, 1.6f);
            Decal(sii, "UNITED_STATES_" + (i + 1), lettersUS, usTex, az, R_Big, 58.9f, 12.6f);
        }
        Decal(sm, "SM_UNITED_STATES", lettersUS, usTex, 132.75f, R_SM, 96.1f, 2.6f);

        Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        Debug.Log("SATURN V BUILT -> " + PrefabPath);
    }

    // ---------------- paint rules ----------------

    // Four-fold roll pattern: black from each Position (0, 90, 180, 270 deg) to the next fin (45 deg later).
    static bool RollBlack(float az) => (Mod(az, 90f)) < 45f;

    static int SicColor(float az, float y)
    {
        bool inRoll = y < Y_Intertank || (y > Y_FwdSkirt && y < Y_SII_AftDiv);
        return inRoll && RollBlack(az) ? BLACK : WHITE;
    }

    // S-II/S-IVB interstage: white, black/white, white/black, black by quadrant (centred on Positions), split at mid-length.
    static int QuadrantColor(float az, float y)
    {
        int q = Mathf.FloorToInt(Mod(az + 45f, 360f) / 90f);
        bool upper = y > (Y_SII_Top + Y_SIVB_Bottom) * 0.5f;
        switch (q)
        {
            case 0: return WHITE;
            case 1: return upper ? WHITE : BLACK;
            case 2: return upper ? BLACK : WHITE;
            default: return BLACK;
        }
    }

    // ---------------- engines ----------------

    // F-1: nozzle exit 12.3 ft (3.76 m) across, about 19 ft long; the nozzle extension and the
    // turbine-exhaust manifold ring are visible below the heat shield.
    static Mesh BuildF1()
    {
        string path = Dir + "/F-1.asset";
        var mb = new MeshBuild();
        float[] ys = { 0f, 0.5f, 1.1f, 1.8f, 2.55f, 3.2f, 3.7f, 4.05f, 4.4f, 4.75f, 5.33f };
        float[] rs = { 1.88f, 1.76f, 1.58f, 1.36f, 1.12f, 0.92f, 0.74f, 0.60f, 0.52f, 0.58f, 0.62f };
        Lathe(mb, ys, rs, 64, (a, y) => ENGINE);
        Lathe(mb, ys, Array.ConvertAll(rs, r => r - 0.03f), 64, (a, y) => NOZZLE_IN, null, Vector3.zero, true);
        // turbine exhaust manifold ring around the nozzle
        float my = 2.7f, mr = 1.08f, tr = 0.11f;
        var ty = new float[13]; var tr2 = new float[13];
        for (int i = 0; i < 13; i++) { float a = i / 12f * Mathf.PI * 2f; ty[i] = my + Mathf.Sin(a) * tr; tr2[i] = mr + Mathf.Cos(a) * tr; }
        Lathe(mb, ty, tr2, 48, (a, y) => DARK);
        // lip at the exit
        Lathe(mb, new[] { 0f, 0f, 0.06f }, new[] { 1.85f, 1.92f, 1.92f }, 64, (a, y) => DARK);
        return SaveMesh(mb, path, out _);
    }

    // J-2: about 11 ft long, 6.8 ft (2.07 m) nozzle exit.
    static Mesh j2Cache;
    static Mesh BuildJ2()
    {
        string path = Dir + "/J-2.asset";
        if (j2Cache != null && File.Exists(path)) return j2Cache;
        var mb = new MeshBuild();
        float[] ys = { 0f, 0.6f, 1.3f, 2.0f, 2.45f, 2.7f, 3.0f, 3.38f };
        float[] rs = { 1.03f, 0.92f, 0.74f, 0.52f, 0.36f, 0.30f, 0.38f, 0.42f };
        Lathe(mb, ys, rs, 48, (a, y) => ENGINE);
        Lathe(mb, ys, Array.ConvertAll(rs, r => r - 0.025f), 48, (a, y) => NOZZLE_IN, null, Vector3.zero, true);
        j2Cache = SaveMesh(mb, path, out _);
        return j2Cache;
    }

    // S-IC fin: titanium, on each engine fairing; tip at 31.5 ft (9.6 m) from the axis (63 ft span).
    static void Fin(MeshBuild mb, float az)
    {
        Vector3 Out(float r, float y, float side) =>
            Polar(az, r, y) + new Vector3(Mathf.Cos(az * D2R), 0f, -Mathf.Sin(az * D2R)) * side;
        float rootR = 5.75f, tipR = 9.6f;
        float rootT = 0.16f, tipT = 0.05f;
        // root: trailing edge y 0.9 -> leading edge y 5.6; tip: y 0.9 -> 2.3
        var p = new[]
        {
            Out(rootR, 0.9f, rootT), Out(rootR, 5.6f, rootT), Out(tipR, 2.3f, tipT), Out(tipR, 0.9f, tipT),
            Out(rootR, 0.9f, -rootT), Out(rootR, 5.6f, -rootT), Out(tipR, 2.3f, -tipT), Out(tipR, 0.9f, -tipT),
        };
        int sideA = RollBlack(az - 1f) ? BLACK : WHITE;   // the fin sits on the black/white boundary
        int sideB = RollBlack(az + 1f) ? BLACK : WHITE;
        mb.Quad(sideA, p[0], p[1], p[2], p[3]);
        mb.Quad(sideB, p[7], p[6], p[5], p[4]);
        mb.Quad(WHITE, p[1], p[5], p[6], p[2]);   // leading edge
        mb.Quad(WHITE, p[3], p[2], p[6], p[7]);   // tip
        mb.Quad(WHITE, p[4], p[0], p[3], p[7]);   // trailing edge (bottom)
    }

    // ---------------- geometry helpers ----------------

    static float Mod(float a, float m) => ((a % m) + m) % m;

    // az measured from Position I (+Z) towards Position II (+X)
    static Vector3 Polar(float azDeg, float r, float y) =>
        new Vector3(r * Mathf.Sin(azDeg * D2R), y, r * Mathf.Cos(azDeg * D2R));

    static Func<float, float> Corrugate(int count, float depth) =>
        a => depth * 0.5f * (1f - Mathf.Cos(a * count));

    // Surface of revolution. ys/rs: profile; colour chosen per face from world azimuth and height.
    static void Lathe(MeshBuild mb, float[] ys, float[] rs, int segs, Func<float, float, int> color,
                      Func<float, float> radial = null, Vector3 axis = default, bool inward = false)
    {
        for (int i = 0; i < ys.Length - 1; i++)
        {
            float y0 = ys[i], y1 = ys[i + 1], r0 = rs[i], r1 = rs[i + 1];
            if (Mathf.Approximately(y0, y1) && Mathf.Approximately(r0, r1)) continue;
            bool corr = radial != null && Mathf.Approximately(r0, r1);
            for (int j = 0; j < segs; j++)
            {
                float a0 = j * 2f * Mathf.PI / segs, a1 = (j + 1) * 2f * Mathf.PI / segs;
                Vector3 P(float a, float y, float r)
                {
                    float rr = r + (corr ? radial(a) : 0f);
                    return axis + new Vector3(rr * Mathf.Sin(a), y, rr * Mathf.Cos(a));
                }
                Vector3 N(float a)
                {
                    float dy = y1 - y0, dr = r1 - r0;
                    var n = new Vector3(dy * Mathf.Sin(a), -dr, dy * Mathf.Cos(a));
                    if (corr)
                    {
                        float h = 0.0005f, d = (radial(a + h) - radial(a - h)) / (2f * h);
                        n = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * r0 - new Vector3(Mathf.Cos(a), 0f, -Mathf.Sin(a)) * d;
                        if (dy < 0) n = -n;
                    }
                    n = n.normalized;
                    return inward ? -n : n;
                }
                Vector3 p00 = P(a0, y0, r0), p10 = P(a1, y0, r0), p11 = P(a1, y1, r1), p01 = P(a0, y1, r1);
                Vector3 c = (p00 + p10 + p11 + p01) * 0.25f;
                float az = Mathf.Atan2(c.x, c.z) * Mathf.Rad2Deg;
                if (az < 0) az += 360f;
                int sub = color(az, c.y);
                Vector3 n0 = N(a0), n1 = N(a1);
                float u0 = (float)j / segs, u1 = (float)(j + 1) / segs;
                mb.QuadN(sub, p00, p10, p11, p01, n0, n1, n1, n0,
                    new Vector2(u0, y0), new Vector2(u1, y0), new Vector2(u1, y1), new Vector2(u0, y1));
            }
        }
    }

    static void Tube(MeshBuild mb, Vector3 a, Vector3 b, float r, int sub)
    {
        Vector3 dir = (b - a).normalized;
        Vector3 s = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        Vector3 t = Vector3.Cross(dir, s);
        const int n = 8;
        for (int i = 0; i < n; i++)
        {
            float a0 = i * 2f * Mathf.PI / n, a1 = (i + 1) * 2f * Mathf.PI / n;
            Vector3 o0 = (s * Mathf.Cos(a0) + t * Mathf.Sin(a0)), o1 = (s * Mathf.Cos(a1) + t * Mathf.Sin(a1));
            mb.QuadN(sub, a + o0 * r, a + o1 * r, b + o1 * r, b + o0 * r, o0, o1, o1, o0,
                Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
        }
    }

    // ---------------- assembly ----------------

    static GameObject Part(GameObject root, string name, float pivotY)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = new Vector3(0f, pivotY, 0f);
        return go;
    }

    // Mesh vertices are in rocket space; parent pivots sit at the separation planes, so offset by -pivot.
    static void AddMesh(GameObject part, string name, MeshBuild mb)
    {
        var mesh = SaveMesh(mb, Dir + "/" + name + ".asset", out var mats, -part.transform.localPosition);
        var go = new GameObject(name);
        go.transform.SetParent(part.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterials = mats;
    }

    static void AddInstance(GameObject part, string name, Mesh mesh, Vector3 rocketPos, float yaw)
    {
        var go = new GameObject(name);
        go.transform.SetParent(part.transform, false);
        go.transform.localPosition = rocketPos - part.transform.localPosition;
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterials = MaterialsFor(mesh);
    }

    static readonly Dictionary<Mesh, Material[]> meshMats = new Dictionary<Mesh, Material[]>();
    static Material[] MaterialsFor(Mesh m) => meshMats[m];

    static Mesh SaveMesh(MeshBuild mb, string path, out Material[] mats, Vector3 offset = default)
    {
        var mesh = mb.ToMesh(Path.GetFileNameWithoutExtension(path), offset, out var used);
        mats = new Material[used.Count];
        for (int i = 0; i < used.Count; i++) mats[i] = palette[used[i]];
        AssetDatabase.CreateAsset(mesh, path);
        meshMats[mesh] = mats;
        return mesh;
    }

    // ---------------- materials and markings ----------------

    static Material[] MakePalette()
    {
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        Material M(string name, Color c, float metal, float smooth, bool twoSided = false)
        {
            var m = new Material(lit) { name = name };
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Metallic", metal);
            m.SetFloat("_Smoothness", smooth);
            if (twoSided) m.SetFloat("_Cull", 0f);
            AssetDatabase.CreateAsset(m, Dir + "/" + name + ".mat");
            return m;
        }
        return new[]
        {
            M("Paint_White", new Color(0.90f, 0.90f, 0.88f), 0f, 0.38f),
            M("Paint_Black", new Color(0.025f, 0.025f, 0.027f), 0f, 0.32f),
            M("Engine_Metal", new Color(0.10f, 0.095f, 0.09f), 0.55f, 0.42f),
            M("Nozzle_Inside", new Color(0.06f, 0.055f, 0.05f), 0.4f, 0.25f),
            M("SM_Aluminium", new Color(0.80f, 0.81f, 0.83f), 0.35f, 0.5f),
            M("SLA_Cork_White", new Color(0.86f, 0.85f, 0.82f), 0f, 0.2f),
            M("CM_Silver_Kapton", new Color(0.82f, 0.82f, 0.86f), 1f, 0.82f),
            M("Structure_Grey", new Color(0.55f, 0.55f, 0.56f), 0.3f, 0.35f),
            M("Heat_Shield_Dark", new Color(0.12f, 0.12f, 0.12f), 0.1f, 0.2f),
        };
    }

    static Material DecalMaterial(string name, Texture2D tex, Color tint)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
        m.SetTexture("_BaseMap", tex);
        m.SetColor("_BaseColor", tint);
        m.SetFloat("_AlphaClip", 1f);
        m.SetFloat("_Cutoff", 0.5f);
        m.EnableKeyword("_ALPHATEST_ON");
        m.SetFloat("_Smoothness", 0.35f);
        m.renderQueue = (int)RenderQueue.AlphaTest;
        AssetDatabase.CreateAsset(m, Dir + "/" + name + ".mat");
        return m;
    }

    // Curved patch on the stage skin, centred on azimuth az, reading left-to-right from outside.
    static void Decal(GameObject part, string name, Material mat, Texture2D tex, float az, float radius, float yCenter, float height)
    {
        float width = height * tex.width / tex.height;
        float r = radius + 0.012f;
        float span = width / r;                        // radians
        const int cols = 12;
        var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var norms = new List<Vector3>(); var tris = new List<int>();
        Vector3 pivot = part.transform.localPosition;
        for (int i = 0; i <= cols; i++)
        {
            float u = i / (float)cols;
            float a = az * D2R + span * (0.5f - u);    // increasing u goes towards smaller azimuth = viewer's right
            var n = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            verts.Add(n * r + new Vector3(0f, yCenter - height * 0.5f, 0f) - pivot); uvs.Add(new Vector2(u, 0f)); norms.Add(n);
            verts.Add(n * r + new Vector3(0f, yCenter + height * 0.5f, 0f) - pivot); uvs.Add(new Vector2(u, 1f)); norms.Add(n);
        }
        for (int i = 0; i < cols; i++)
        {
            int b = i * 2;
            tris.AddRange(new[] { b, b + 1, b + 3, b, b + 3, b + 2 });
        }
        var mesh = new Mesh { name = name };
        mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
        if (Vector3.Dot(Vector3.Cross(verts[1] - verts[0], verts[3] - verts[0]), norms[0]) < 0f)
        {
            for (int i = 0; i < tris.Count; i += 3) { int t = tris[i]; tris[i] = tris[i + 1]; tris[i + 1] = t; }
            mesh.SetTriangles(tris, 0);
        }
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, Dir + "/Decal_" + name + ".asset");
        var go = new GameObject(name);
        go.transform.SetParent(part.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
    }

    // Lettering masks are drawn by Tools/make_markings.ps1 (Windows font engine): upright letters stacked vertically.
    const string MarkingsDir = "Assets/Apollo/Textures/Markings";
    static Texture2D LoadMarking(string file)
    {
        string path = MarkingsDir + "/" + file;
        if (!File.Exists(path))
        {
            var psi = new System.Diagnostics.ProcessStartInfo("powershell",
                "-NoProfile -ExecutionPolicy Bypass -File Tools/make_markings.ps1 -OutDir " + MarkingsDir)
            { UseShellExecute = false, CreateNoWindow = true };
            System.Diagnostics.Process.Start(psi).WaitForExit();
            AssetDatabase.Refresh();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // United States flag of 1969 (50 stars), official proportions 1 : 1.9.
    static Texture2D FlagTexture(string path)
    {
        const int H = 520, W = 988;
        var red = new Color32(178, 34, 52, 255); var white = new Color32(242, 242, 238, 255); var blue = new Color32(40, 45, 98, 255);
        var px = new Color32[W * H];
        float stripe = H / 13f, cantonH = stripe * 7f, cantonW = W * 0.4f;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int fromTop = H - 1 - y;
                Color32 c = ((int)(fromTop / stripe) % 2 == 0) ? red : white;
                if (fromTop < cantonH && x < cantonW) c = blue;
                px[y * W + x] = c;
            }
        // 50 stars: 9 rows alternating 6 and 5
        float starR = stripe * 0.616f * 0.5f;
        for (int row = 0; row < 9; row++)
        {
            int count = row % 2 == 0 ? 6 : 5;
            for (int k = 0; k < count; k++)
            {
                float cx = cantonW / 12f * (row % 2 == 0 ? 1 + 2 * k : 2 + 2 * k);
                float cy = H - 1 - cantonH / 10f * (row + 1);
                for (int y = (int)(cy - starR); y <= (int)(cy + starR); y++)
                    for (int x = (int)(cx - starR); x <= (int)(cx + starR); x++)
                        if (x >= 0 && y >= 0 && x < W && y < H && InStar(x - cx, y - cy, starR)) px[y * W + x] = white;
            }
        }
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.SetPixels32(px); tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
        return ImportMarking(path);
    }

    static bool InStar(float x, float y, float R)
    {
        float r = Mathf.Sqrt(x * x + y * y);
        if (r > R) return false;
        float a = Mathf.Atan2(x, y);                     // point up
        float k = Mathf.PI * 2f / 5f;
        float t = Mod(a, k) - k / 2f;                    // -k/2..k/2 around each point
        float inner = R * 0.382f;
        // boundary between point (t=0, R) and notch (t=±k/2, inner)
        float f = Mathf.Abs(t) / (k / 2f);
        float edge = Mathf.Lerp(R, inner, f) / Mathf.Lerp(1f, 1.05f, f);
        return r <= edge;
    }

    static Texture2D ImportMarking(string path)
    {
        AssetDatabase.ImportAsset(path);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = TextureImporterType.Default;
        ti.alphaIsTransparency = true;
        ti.mipmapEnabled = true;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.maxTextureSize = 2048;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // ---------------- mesh accumulator ----------------

    class MeshBuild
    {
        readonly List<Vector3> v = new List<Vector3>();
        readonly List<Vector3> n = new List<Vector3>();
        readonly List<Vector2> uv = new List<Vector2>();
        readonly List<int>[] sub = new List<int>[9];
        public MeshBuild() { for (int i = 0; i < sub.Length; i++) sub[i] = new List<int>(); }

        public void Quad(int s, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Vector3 nn = Vector3.Cross(b - a, c - a).normalized;
            QuadN(s, a, b, c, d, nn, nn, nn, nn, Vector2.zero, Vector2.up, Vector2.one, Vector2.right);
        }

        public void QuadN(int s, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd,
                          Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            int i0 = v.Count;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            n.Add(na); n.Add(nb); n.Add(nc); n.Add(nd);
            uv.Add(ua); uv.Add(ub); uv.Add(uc); uv.Add(ud);
            // Unity front faces are clockwise as seen from the viewer: make winding agree with the normal.
            Vector3 geo = Vector3.Cross(b - a, c - a);
            if (geo.sqrMagnitude < 1e-12f) geo = Vector3.Cross(c - a, d - a);
            if (Vector3.Dot(geo, na + nb + nc + nd) >= 0f)
                sub[s].AddRange(new[] { i0, i0 + 1, i0 + 2, i0, i0 + 2, i0 + 3 });
            else
                sub[s].AddRange(new[] { i0, i0 + 2, i0 + 1, i0, i0 + 3, i0 + 2 });
        }

        public Mesh ToMesh(string name, Vector3 offset, out List<int> used)
        {
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            var verts = new List<Vector3>(v.Count);
            foreach (var p in v) verts.Add(p + offset);
            mesh.SetVertices(verts);
            mesh.SetNormals(n);
            mesh.SetUVs(0, uv);
            used = new List<int>();
            for (int i = 0; i < sub.Length; i++) if (sub[i].Count > 0) used.Add(i);
            mesh.subMeshCount = used.Count;
            for (int k = 0; k < used.Count; k++) mesh.SetTriangles(sub[used[k]], k);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }
    }
}
