// Builds the Flight scene for step 4: the real Kennedy Space Center around Launch Complex 39A, Apollo 11's
// Saturn V on Mobile Launcher 1 beside its Launch Umbilical Tower, swing arm 9 and the white room, and the
// real Columbia cabin (Smithsonian scan) inside the rocket's Command Module.
// Menu: Apollo > Build Flight Scene     Headless: -executeMethod FlightBuilder.Build
//
// Terrain: USGS / USDA orthophotos (The National Map, 2024) on USGS 3DEP bare-earth heights, UTM 17N, in
// four rings (2 m, 8 m, 32 m, 512 m grids) out to 20 km. The vehicle stands over the real flame trench,
// which the heights show 18 m wide running north-south. The hardstand top is repainted as the 1969 pad.
// Launcher and tower (NASA SP-4204 "Moonport"; Apollo ML / LUT records): base 160 x 135 x 25 ft on 22 ft
// pedestals, 45 ft exhaust opening; tower 380 ft, 40 ft square, 18 floors, international orange; crane
// 445 ft above the pad, lightning mast 490 ft; nine swing arms at their vehicle stations, arm 9 at Level 320.
// Approximations: tower on the north side, truss pattern, arm shapes, Vehicle Assembly Building as a block.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static class FlightBuilder
{
    internal const string Dir = "Assets/Apollo/Models/Flight";
    const string TexDir = "Assets/Apollo/Textures/KSC";
    const string Src = "SourceFiles/KSC";
    const string ScenePath = "Assets/Apollo/Scenes/Flight.unity";
    const float Ft = 0.3048f;

    // Pad frame: x east, y up (pad surface y = 0), z north; the vehicle axis is at x = z = 0.
    public const float DeckY = (22 + 25) * Ft;                      // 14.33 m: pedestals + launcher base
    public const float NozzleY = DeckY + 3.35f - 5.0f;              // vehicle base (y 5.0) on 3.35 m hold-down arms
    const float TowerHalf = 20 * Ft, TowerZ = (62 + 20) * Ft;       // tower face 62 ft from the vehicle axis
    const float PadTop = 14.1f;                                     // pad surface, metres above sea level (3DEP)
    const float HatchWindowRadius = 0.135f, HatchWindowUp = 0.12f;  // hatch window clear radius (m); its centre as a fraction of the door half-height
    const float AxisX = 21f, AxisZ = 2f;                            // vehicle axis in data coordinates (over the trench)
    const float CmBase = NozzleY + 98.02f;                          // Command Module heat-shield plane
    public const int CabinLayer = 8;

    static float StaY(float sta) => NozzleY + (sta + 110f) * 0.0254f;

    // Command Module and Boost Protective Cover outer profiles (height above CmBase, radius), from SaturnVBuilder
    static readonly Vector2[] CmProfile = { new(0f, 1.935f), new(0.25f, 1.905f), new(2.83f, 0.62f), new(3.03f, 0.45f), new(3.23f, 0.42f) };
    static readonly Vector2[] BpcProfile = { new(-0.05f, 1.995f), new(0.30f, 1.955f), new(2.93f, 0.66f), new(3.28f, 0.45f), new(3.40f, 0f) };
    static float Radius(Vector2[] prof, float h)
    {
        if (h <= prof[0].x) return prof[0].y;
        for (int i = 0; i < prof.Length - 1; i++)
            if (h <= prof[i + 1].x) return Mathf.Lerp(prof[i].y, prof[i + 1].y, Mathf.InverseLerp(prof[i].x, prof[i + 1].x, h));
        return prof[prof.Length - 1].y;
    }

    [MenuItem("Apollo/Build Flight Scene")]
    public static void Build()
    {
        if (AssetDatabase.IsValidFolder(Dir)) AssetDatabase.DeleteAsset(Dir);
        Directory.CreateDirectory(Dir);
        Directory.CreateDirectory(TexDir);
        AssetDatabase.Refresh();
        EnsureCabinLayer();

        // ---------------- real Kennedy: heights and photos ----------------
        float[] demInner = LoadGrid(Src + "/ksc_height_inner.bsq"), demMiddle = LoadGrid(Src + "/ksc_height_middle.bsq");
        PadDesign.Heights(demInner, AxisX, AxisZ, PadTop);         // the pad rebuilt clean on its real measurements
        var photoInner = MakePhoto("KSC_Photo_Inner", Src + "/ksc_photo_inner.jpg", tex => PaintPad(tex, demInner));
        var photoMiddle = MakePhoto("KSC_Photo_Middle", Src + "/ksc_photo_middle.jpg", null);
        var photoOuter = MakePhoto("KSC_Photo_Outer", Src + "/ksc_photo_outer.jpg", FillOcean);

        // ---------------- materials ----------------
        var orange = Lit("LUT_InternationalOrange", new Color(0.80f, 0.27f, 0.10f), 0.30f);
        var grey = Lit("Launcher_Grey", new Color(0.53f, 0.54f, 0.53f), 0.22f);
        var floors = Lit("Tower_Floors", new Color(0.30f, 0.30f, 0.30f), 0.15f);
        var white = Lit("White", new Color(0.88f, 0.88f, 0.86f), 0.40f);
        var whiteRoomMat = Lit("WhiteRoom", new Color(0.93f, 0.93f, 0.92f), 0.25f);
        var grating = Lit("Grating", new Color(0.42f, 0.43f, 0.43f), 0.3f, 0.4f);
        var pipes = Lit("Pipes", new Color(0.78f, 0.79f, 0.80f), 0.55f, 0.5f);
        var vabMat = Lit("VAB", new Color(0.80f, 0.81f, 0.82f), 0.2f);
        var vabDoor = Lit("VAB_Doors", new Color(0.58f, 0.60f, 0.62f), 0.2f);
        var farSea = Lit("FarWater", new Color(0.10f, 0.25f, 0.31f), 0.6f);
        var hatchDark = Lit("HatchOpening", new Color(0.035f, 0.035f, 0.04f), 0.1f);
        Material Photo(string n, Texture t) { var m = Lit(n, Color.white, 0f, 0f, t); return m; }
        var maps = KscEnvironment.Classify(TexDir + "/KSC_Photo_Inner.jpg", TexDir + "/KSC_Photo_Outer.jpg", demInner, demMiddle);
        KscEnvironment.CloudTextures(out var cloudCoverage, out var cloudDetail);
        var coast = KscEnvironment.Coast(demMiddle, maps.MaskOuter);
        var mInner = KscEnvironment.GroundMaterial(cloudCoverage, coast, -4096f - AxisX, -4096f - AxisZ, 8192f);
        var mMiddle = mInner; var mOuter = mInner;                  // one ground everywhere: materials, no photo

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var world = new GameObject("KennedySpaceCenter").transform;

        // ---------------- terrain rings (data coordinates centred on the downloaded tiles) ----------------
        TerrainRing("Terrain_2m", demInner, 1024f, 512f, 0f, 2f, 1024f, mInner, world, false);
        TerrainRing("Terrain_8m", demInner, 1024f, 1024f, 512f, 8f, 1024f, mInner, world, false);
        TerrainRing("Terrain_32m", demMiddle, 4096f, 4096f, 1024f, 32f, 4096f, mMiddle, world, false);
        TerrainRing("Terrain_512m", null, 0f, 20480f, 4096f, 512f, 20480f, mOuter, world, true);
        PadDesign.Build(world, cloudCoverage, mInner.GetColor("_Tint0"));
        KscEnvironment.Water(world, maps, new Vector2(-AxisX, -AxisZ), PadTop);
        KscEnvironment.Plants_(world, cloudCoverage, coast, demInner, AxisX, AxisZ, PadTop, new Vector2(-170f, -210f));
        {
            var far = new Geo();
            float a = 20480f, b = 70000f, y = -0.8f - PadTop;
            far.Quad(new Vector3(-b, y, -b), new Vector3(-b, y, b), new Vector3(-a, y, b), new Vector3(-a, y, -b), Vector3.up);
            far.Quad(new Vector3(a, y, -b), new Vector3(a, y, b), new Vector3(b, y, b), new Vector3(b, y, -b), Vector3.up);
            far.Quad(new Vector3(-a, y, a), new Vector3(-a, y, b), new Vector3(a, y, b), new Vector3(a, y, a), Vector3.up);
            far.Quad(new Vector3(-a, y, -b), new Vector3(-a, y, -a), new Vector3(a, y, -a), new Vector3(a, y, -b), Vector3.up);
            Obj("FarSurroundings", far, farSea, world);
        }

        // LOX (NW) and LH2 (NE) spheres where the photo shows them
        {
            var spheres = new Geo(); var legs = new Geo();
            foreach (var (dx, dz, r) in new[] { (-383f, 252f, 9.35f), (391f, 234f, 9.15f) })
            {
                float gy = Height(demInner, 1024f, dx, dz) - PadTop;
                float x = dx - AxisX, z = dz - AxisZ;
                Vector3 c = new Vector3(x, gy + 3.2f + r, z);
                spheres.Sphere(c, r, 48, 24);
                for (int k = 0; k < 10; k++)
                {
                    float a = k * 36f * Mathf.Deg2Rad;
                    Vector3 p = new Vector3(x + Mathf.Cos(a) * r * 0.78f, 0, z + Mathf.Sin(a) * r * 0.78f);
                    legs.Cyl(new Vector3(p.x, gy, p.z), new Vector3(p.x, c.y - r * 0.2f, p.z), 0.35f, 0.35f, 10);
                }
            }
            Obj("PropellantSpheres", spheres, white, world);
            Obj("PropellantSphere_Legs", legs, white, world);
        }

        // the space center around the pad: VAB and Launch Control Center, Pad 39B's lightning towers
        KscEnvironment.SpaceCenter(world, demMiddle, AxisX, AxisZ, PadTop, Lit("Steel_Grey", new Color(0.62f, 0.63f, 0.64f), 0.35f, 0.4f), Lit("Apron_Concrete", new Color(0.66f, 0.66f, 0.63f), 0.1f));

        // ---------------- Mobile Launcher 1 ----------------
        var ml = new GameObject("MobileLauncher1").transform;
        ml.SetParent(world, false);
        {
            var g = new Geo(); var o = new Geo();
            float hx = 135 * Ft / 2f, z0 = TowerZ + TowerHalf + 2f, z1 = z0 - 160.33f * Ft, hole = 45 * Ft / 2f;
            float yb = 22 * Ft, yt = DeckY, yc = (yb + yt) / 2f, hh = yt - yb;
            g.Box(new Vector3(0, yc, (z0 + hole) / 2f), new Vector3(2 * hx, hh, z0 - hole), Quaternion.identity);
            g.Box(new Vector3(0, yc, (z1 - hole) / 2f), new Vector3(2 * hx, hh, -hole - z1), Quaternion.identity);
            g.Box(new Vector3((hole + hx) / 2f, yc, 0), new Vector3(hx - hole, hh, 2 * hole), Quaternion.identity);
            g.Box(new Vector3(-(hole + hx) / 2f, yc, 0), new Vector3(hx - hole, hh, 2 * hole), Quaternion.identity);
            foreach (float x in new[] { -hx + 3f, hx - 3f })
                foreach (float z in new[] { z1 + 3f, (z0 + z1) / 2f + 9f, z0 - 3f })
                    g.Box(new Vector3(x, yb / 2f, z), new Vector3(2.4f, yb, 2.4f), Quaternion.identity);
            foreach (float x in new[] { -9.9f, 9.9f })                     // extensible columns at the trench edges
                foreach (float z in new[] { -hole - 1.2f, hole + 1.2f })
                    g.Box(new Vector3(x, yb / 2f, z), new Vector3(1.6f, yb, 1.6f), Quaternion.identity);
            for (int i = 0; i < 4; i++)
            {
                float az = 45f + 90f * i - 90f;
                Vector3 d = new Vector3(Mathf.Sin(az * Mathf.Deg2Rad), 0, Mathf.Cos(az * Mathf.Deg2Rad));
                g.Box(DeckYv(d * 10.3f, 1.675f), new Vector3(2.6f, 3.35f, 2.6f), Quaternion.LookRotation(d));
                g.Beam(DeckYv(d * 9.2f, 2.8f), DeckYv(d * 6.4f, 3.1f), 1.1f, 1.0f);
            }
            foreach (float azp in new[] { 20f, 160f, 200f })
            {
                float az = azp - 90f;
                Vector3 d = new Vector3(Mathf.Sin(az * Mathf.Deg2Rad), 0, Mathf.Cos(az * Mathf.Deg2Rad));
                o.Box(DeckYv(d * 10.6f, 4.75f), new Vector3(1.7f, 9.5f, 1.7f), Quaternion.LookRotation(d));
                o.Beam(DeckYv(d * 10.0f, 8.6f), DeckYv(d * 6.0f, 7.6f), 0.9f, 0.9f);
            }
            Obj("Launcher_Base", g, grey, ml);
            Obj("TailServiceMasts", o, orange, ml);
        }

        // ---------------- Saturn V AS-506 ----------------
        var saturn = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Apollo/Prefabs/SaturnV_AS506.prefab"));
        saturn.transform.position = new Vector3(0f, NozzleY, 0f);
        saturn.transform.rotation = Quaternion.Euler(0f, -90f, 0f);   // Position II faces the tower (north)

        // ---------------- Columbia's real cabin, inside the Command Module ----------------
        var cabin = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Apollo/Prefabs/Columbia_Interior.prefab"));
        Transform part(string n) => cabin.transform.Find("Interior/" + n);
        cabin.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var hullR = part("Hull").GetComponentInChildren<Renderer>();
        Bounds hb = hullR.bounds;
        Vector3 hatchLocal = part("Hull").TransformPoint(FromGltf(new Vector3(-40.374f, 178.735f, 123.778f)));
        Vector3 hatchDirFlat = Vector3.ProjectOnPlane(hatchLocal - hb.center, Vector3.up).normalized;
        Quaternion rot = Quaternion.Inverse(Quaternion.LookRotation(hatchDirFlat, Vector3.up));   // apex up, hatch toward the tower
        Vector3 aft = new Vector3(hb.center.x, hb.min.y, hb.center.z);
        cabin.transform.rotation = rot;
        cabin.transform.position = new Vector3(0f, CmBase + 0.20f, 0f) - rot * aft;
        foreach (var mf in cabin.GetComponentsInChildren<MeshFilter>(true))
            if (!mf.GetComponent<MeshCollider>()) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
        // fit check: how far the scan's hull reaches toward the Command Module's outer surface
        {
            foreach (var pn in new[] { "Hull", "Seats", "Panel", "Graffiti" })
            {
                float worst = -9f; Vector3 wp = default;
                var bins = new float[14];
                for (int k = 0; k < bins.Length; k++) bins[k] = -9f;
                foreach (var mfh in part(pn).GetComponentsInChildren<MeshFilter>())
                {
                    var vs = mfh.sharedMesh.vertices;
                    for (int i = 0; i < vs.Length; i += 3)
                    {
                        Vector3 w = mfh.transform.TransformPoint(vs[i]);
                        float h = w.y - CmBase, r = new Vector2(w.x, w.z).magnitude, e = r - Radius(CmProfile, h);
                        if (e > worst) { worst = e; wp = w; }
                        int bi = Mathf.Clamp(Mathf.FloorToInt(h / 0.25f) + 1, 0, bins.Length - 1);
                        bins[bi] = Mathf.Max(bins[bi], e);
                    }
                }
                var sb = new System.Text.StringBuilder();
                for (int k = 0; k < bins.Length; k++) sb.Append($" h{(k - 1) * 0.25f:0.00}:{bins[k]:+0.00;-0.00}");
                Debug.Log($"FLIGHT cabin fit {pn}: worst {worst:+0.00;-0.00} m at h {wp.y - CmBase:0.00} az {Mathf.Atan2(wp.x, wp.z) * Mathf.Rad2Deg:0} |{sb}");
            }
            var cbb = new Bounds(cabin.transform.position, Vector3.zero);
            foreach (var r in cabin.GetComponentsInChildren<Renderer>()) cbb.Encapsulate(r.bounds);
            Debug.Log($"FLIGHT cabin bounds min {cbb.min} max {cbb.max}, CM base {CmBase:0.00}");
        }

        Transform hullT = part("Hull");
        Vector3 hatchP = hullT.TransformPoint(FromGltf(new Vector3(-40.374f, 178.735f, 123.778f)));
        Vector3 hatchIn = hullT.TransformDirection(FromGltf(new Vector3(0.1842f, 0.8428f, -0.5057f))).normalized;
        float hatchH = hatchP.y - CmBase;

        // ---------------- the commander's eye, the closed hatch, and the structure behind the scan ----------------
        var lights = new List<Light>();
        Vector3 cdr, cdrN, centre;
        {
            var panel = part("Panel");
            cdr = panel.TransformPoint(FromGltf(new Vector3(-76.39f, 206.33f, 37.47f)));
            cdrN = panel.TransformDirection(FromGltf(new Vector3(-0.0039f, -0.9555f, 0.2949f))).normalized;
            centre = panel.TransformPoint(FromGltf(new Vector3(-1.0f, 218.47f, 77.99f)));
        }
        var eye = new GameObject("CommanderEye").transform;
        eye.SetParent(cabin.transform, true);
        eye.position = cdr + cdrN * 0.62f;
        eye.rotation = Quaternion.LookRotation(Vector3.Lerp(cdr, centre, 0.45f) - eye.position, rot * Vector3.forward);
        SetLayer(cabin.transform, CabinLayer);
        Vector3 hatchWindowPos = SealCabin(cabin.transform, eye, hatchP);
        void Flood(string n, Vector3 pos, float intensity, float range, Color c)
        {
            var l = new GameObject(n).AddComponent<Light>();
            l.type = LightType.Point; l.intensity = intensity; l.range = range; l.color = c;
            l.cullingMask = 1 << CabinLayer; l.shadows = LightShadows.None;
            l.transform.SetParent(cabin.transform, true);
            l.transform.position = pos;
            lights.Add(l);
        }
        var cabinCentre = new Vector3(0f, CmBase + 1.3f, 0f);
        Flood("Flood_Commander", eye.position + cdrN * 0.25f, 0.35f, 3.5f, new Color(1f, 0.97f, 0.92f));
        Flood("Flood_Centre", Vector3.Lerp(eye.position, centre, 0.35f) + cdrN * 0.3f, 0.25f, 3.5f, new Color(1f, 0.97f, 0.93f));
        Flood("Flood_LowerBay", cabinCentre + cdrN * -0.4f, 0.15f, 3.0f, new Color(0.95f, 0.96f, 1f));
        Flood("Flood_Hatch", Vector3.Lerp(cabinCentre, hatchWindowPos, 0.6f), 0.35f, 1.6f, new Color(1f, 0.97f, 0.92f));

        // instrument names at the Smithsonian's own annotation points (model 0 hull, 1 couches, 2 panel)
        var labels = new (int part, string title, string line, Vector3 pos)[]
        {
            (2, "COMMANDER'S STATION", "Armstrong's side of the panel: the controls for flying the spacecraft.", new Vector3(-76.388f, 206.327f, 37.474f)),
            (2, "CENTRE STATION", "Opposite the middle couch: flight and systems controls.", new Vector3(-12.421f, 210.228f, 50.915f)),
            (2, "RIGHT-HAND STATION", "Opposite the right couch: controls for the spacecraft's systems.", new Vector3(57.468f, 205.736f, 36.363f)),
            (2, "MAIN DISPLAY CONSOLE", "Meters, lights and switches to watch and run every system on board.", new Vector3(-1.007f, 218.466f, 77.989f)),
            (2, "FDAI · EIGHT BALL", "The main flight display: shows which way the spacecraft is pointing.", new Vector3(-39.962f, 218.129f, 75.868f)),
            (2, "ENTRY MONITOR SYSTEM", "Backup that tracked engine burns and the plunge back into Earth's air.", new Vector3(-61.683f, 218.753f, 78.035f)),
            (2, "DSKY", "Display and keyboard of the Apollo Guidance Computer. The crew typed verb and noun codes.", new Vector3(-32.627f, 214.141f, 62.210f)),
            (2, "TOGGLE SWITCHES", "The most common control on board. Guards stop accidental flips.", new Vector3(76.883f, 205.593f, 36.172f)),
            (2, "PUSHBUTTONS", "For functions the crew had to start instantly.", new Vector3(-51.044f, 204.854f, 34.719f)),
            (2, "ROTARY SWITCHES", "Used where a control needs four or more positions.", new Vector3(90.246f, 206.836f, 40.495f)),
            (2, "DIALS", "Smooth controls: light brightness, audio volume, antenna pointing.", new Vector3(90.492f, 204.926f, 64.792f)),
            (1, "CREW COUCHES", "The crew launched lying on their backs to take the g-forces.", new Vector3(-8.921f, 138.948f, 29.441f)),
            (1, "HAND CONTROLLERS", "Right hand turns the spacecraft, left hand moves it.", new Vector3(-32.068f, 185.314f, 33.464f)),
            (0, "SIDE HATCH", "The way in from the white room. Its small window is the only one not covered on the pad.", new Vector3(-40.374f, 178.735f, 123.778f)),
            (0, "DOCKING TUNNEL", "Leads through the nose to the Lunar Module.", new Vector3(2.493f, 281.900f, -38.750f)),
            (0, "GUIDANCE & NAVIGATION", "Lower bay: the computer and a sextant for sighting stars.", new Vector3(19.016f, 178.376f, -89.797f)),
            (0, "ENVIRONMENT CONTROLS", "Oxygen, cabin pressure and temperature.", new Vector3(-106.219f, 148.027f, -45.752f)),
            (0, "STORAGE LOCKERS", "Food, cameras and gear, under and around the couches.", new Vector3(-35.631f, 78.635f, 44.954f)),
            (0, "COLUMBIA", "Home for three astronauts for eight days, in about 6 cubic metres.", new Vector3(46.778f, 214.447f, -56.155f)),
        };
        string[] partNames = { "Hull", "Seats", "Panel" };
        foreach (var l in labels)
        {
            var go = new GameObject("Label_" + l.title);
            go.transform.SetParent(part(partNames[l.part]), false);
            go.transform.localPosition = FromGltf(l.pos);
            var cl = go.AddComponent<CabinLabel>();
            cl.Title = l.title; cl.Line = l.line;
        }
        SetLayer(cabin.transform, CabinLayer);

        // ---------------- tower, crane, swing arms 1-8 ----------------
        var lut = new GameObject("LaunchUmbilicalTower").transform;
        lut.SetParent(ml, false);
        {
            var o = new Geo(); var f = new Geo(); var w = new Geo(); var p = new Geo();
            BuildTower(o, f, p);
            var arms = new (int n, float sta, float xs, float az, float wd, float ht)[]
            {
                (1, 772.0f, 0f, 0f, 3.0f, 2.6f), (2, 1516.6f, -3.6f, -20f, 3.0f, 2.6f), (3, 1606.2f, 3.6f, 20f, 3.0f, 2.6f),
                (4, 1771.0f, 0f, 0f, 3.0f, 2.6f), (5, 2506.0f, 0f, 0f, 3.0f, 2.6f), (6, 2760.0f, 0f, 0f, 3.0f, 2.6f),
                (7, 3215.0f, 0f, 0f, 3.0f, 2.6f), (8, 3721.6f, 3.8f, 35f, 2.6f, 2.4f),
            };
            foreach (var a in arms)
            {
                float y = StaY(a.sta);
                float r = a.sta < 2600f ? 5.03f : a.sta < 3300f ? 3.30f : 1.955f;
                Vector3 end = new Vector3(Mathf.Sin(a.az * Mathf.Deg2Rad) * (r + 0.5f), y, Mathf.Cos(a.az * Mathf.Deg2Rad) * (r + 0.5f));
                Vector3 start = new Vector3(a.xs, y, TowerZ - TowerHalf);
                Arm(o, start, end, a.wd, a.ht);
                Vector3 dir = (end - start).normalized, side = Vector3.Cross(Vector3.up, dir).normalized;
                Vector3 plate = new Vector3(Mathf.Sin(a.az * Mathf.Deg2Rad) * (r + 0.1f), y - a.ht / 2f - 1.3f, Mathf.Cos(a.az * Mathf.Deg2Rad) * (r + 0.1f));
                for (int s = -1; s <= 1; s += 2)
                    w.Cyl(end - dir * 0.6f + side * (0.45f * s) + Vector3.down * (a.ht / 2f), plate + side * (0.3f * s) + Vector3.up * 0.2f, 0.11f, 0.11f, 8);
                w.Box(plate, new Vector3(1.1f, 1.0f, 0.25f), Quaternion.LookRotation(-new Vector3(plate.x, 0, plate.z).normalized));
            }
            Obj("Tower_Steel", o, orange, lut);
            Obj("Tower_Floors", f, floors, lut);
            Obj("Tower_Pipes", p, pipes, lut);
            Obj("Arm_Umbilicals", w, white, lut);
        }

        // ---------------- swing arm 9 and the white room (walkable), pivoting at the tower ----------------
        float floorY = hatchP.y - 0.95f;
        float bpcR = Radius(BpcProfile, hatchH);
        float roomS = bpcR + 0.06f, roomN = roomS + 2.6f, towerFace = TowerZ - TowerHalf;
        var arm9 = new GameObject("SwingArm9").transform;
        arm9.position = new Vector3(-1.3f, floorY, towerFace);
        arm9.SetParent(lut, true);
        GameObject hatchDecal;
        {
            var o = new Geo(); var gr = new Geo(); var wr = new Geo(); var dk = new Geo();
            Arm(o, new Vector3(0f, floorY + 1.25f, towerFace), new Vector3(0f, floorY + 1.25f, roomN), 1.9f, 2.5f);
            gr.Box(new Vector3(0f, floorY - 0.04f, (towerFace + roomN) / 2f), new Vector3(1.75f, 0.08f, towerFace - roomN), Quaternion.identity);
            foreach (float sx in new[] { -0.82f, 0.82f })
            {
                gr.Beam(new Vector3(sx, floorY + 1.05f, towerFace), new Vector3(sx, floorY + 1.05f, roomN), 0.06f, 0.06f);
                gr.Beam(new Vector3(sx, floorY + 0.55f, towerFace), new Vector3(sx, floorY + 0.55f, roomN), 0.04f, 0.04f);
            }
            // white room: floor, ceiling, east/west walls, north wall with the doorway; open to the spacecraft on the south
            float cz = (roomS + roomN) / 2f, t = 0.08f, hw = 1.6f, hgt = 2.6f;
            gr.Box(new Vector3(0, floorY - 0.04f, cz), new Vector3(2 * hw, 0.08f, roomN - roomS), Quaternion.identity);
            wr.Box(new Vector3(0, floorY + hgt, cz), new Vector3(2 * hw + 2 * t, t, roomN - roomS + t), Quaternion.identity);
            wr.Box(new Vector3(-hw - t / 2, floorY + hgt / 2, cz), new Vector3(t, hgt, roomN - roomS), Quaternion.identity);
            wr.Box(new Vector3(hw + t / 2, floorY + hgt / 2, cz), new Vector3(t, hgt, roomN - roomS), Quaternion.identity);
            wr.Box(new Vector3(-(hw + 0.55f) / 2f, floorY + hgt / 2, roomN), new Vector3(hw - 0.55f, hgt, t), Quaternion.identity);
            wr.Box(new Vector3((hw + 0.55f) / 2f, floorY + hgt / 2, roomN), new Vector3(hw - 0.55f, hgt, t), Quaternion.identity);
            wr.Box(new Vector3(0, floorY + 2.25f, roomN), new Vector3(1.1f, 0.7f, t), Quaternion.identity);
            float hy = hatchP.y, oh = 0.52f, ow = 0.47f;                                     // spacecraft-side wall around the hatch
            wr.Box(new Vector3(-(hw + ow) / 2f, floorY + hgt / 2, roomS), new Vector3(hw - ow, hgt, t), Quaternion.identity);
            wr.Box(new Vector3((hw + ow) / 2f, floorY + hgt / 2, roomS), new Vector3(hw - ow, hgt, t), Quaternion.identity);
            wr.Box(new Vector3(0, (floorY + hy - oh) / 2f, roomS), new Vector3(2 * ow, hy - oh - floorY, t), Quaternion.identity);
            wr.Box(new Vector3(0, (hy + oh + floorY + hgt) / 2f, roomS), new Vector3(2 * ow, floorY + hgt - hy - oh, t), Quaternion.identity);
            Obj("Arm9_Truss", o, orange, null).transform.SetParent(arm9, true);
            Obj("Arm9_Floor_Rails", gr, grating, null).transform.SetParent(arm9, true);
            Obj("WhiteRoom", wr, whiteRoomMat, null).transform.SetParent(arm9, true);
            var lamp = new GameObject("WhiteRoom_Light").AddComponent<Light>();
            lamp.type = LightType.Point; lamp.range = 5f; lamp.intensity = 1.4f; lamp.color = new Color(1f, 0.98f, 0.94f);
            lamp.cullingMask = ~(1 << CabinLayer);
            lamp.transform.position = new Vector3(0f, floorY + 2.3f, cz);
            lamp.transform.SetParent(arm9, true);

            // the open hatch as seen from the white room: a dark opening in the cover, at the scan's real hatch
            float slope = Mathf.Atan((Radius(BpcProfile, hatchH - 0.5f) - Radius(BpcProfile, hatchH + 0.5f)) / 1f);
            var rotD = Quaternion.LookRotation(new Vector3(0, Mathf.Sin(slope), Mathf.Cos(slope)), Vector3.up);
            Vector3 dc = new Vector3(0f, hatchP.y, bpcR + 0.015f);
            dk.Box(dc, new Vector3(0.78f, 0.88f, 0.02f), rotD);
            hatchDecal = Obj("HatchOpening", dk, hatchDark, null);
            var frame = new Geo();
            foreach (var (ox, oy, sx, sy) in new[] { (0f, 0.47f, 0.92f, 0.06f), (0f, -0.47f, 0.92f, 0.06f), (0.42f, 0f, 0.06f, 1.0f), (-0.42f, 0f, 0.06f, 1.0f) })
                frame.Box(dc + rotD * new Vector3(ox, oy, 0.01f), new Vector3(sx, sy, 0.03f), rotD);
            Obj("HatchFrame", frame, white, hatchDecal.transform);
        }

        // ---------------- sky, sun, camera, look ----------------
        // a real photographed sky (Poly Haven, CC0); its sun is at u 0.5953 and 47.9 degrees (measured)
        var sky = new Material(Shader.Find("Apollo/SpaceSky")) { name = "Sky_Earth_and_Space" };
        sky.SetFloat("_Exposure", 0.55f);
        sky.SetColor("_Haze", new Color(0.86f, 0.90f, 0.95f));
        sky.SetTexture("_Day", AssetDatabase.LoadAssetAtPath<Texture>("Assets/Apollo/Textures/Earth/world.topo.bathy.200407.3x21600x10800.jpg"));
        sky.SetTexture("_Night", AssetDatabase.LoadAssetAtPath<Texture>("Assets/Apollo/Textures/Earth/BlackMarble_2016_3km.jpg"));
        sky.SetTexture("_EarthClouds", AssetDatabase.LoadAssetAtPath<Texture>("Assets/Apollo/Textures/Earth/clouds_2025-07-16.png"));
        sky.SetTexture("_Stars", AssetDatabase.LoadAssetAtPath<Texture>("Assets/Apollo/Textures/Sky/starmap_2020_8k.exr"));
        sky.SetTexture("_Milky", AssetDatabase.LoadAssetAtPath<Texture>("Assets/Apollo/Textures/Sky/milkyway_2020_8k.exr"));
        sky.SetTexture("_MoonTex", AssetDatabase.LoadAssetAtPath<Texture>("Assets/Apollo/Textures/Moon/lroc_color_poles_16k.tif"));   // NASA SVS CGI Moon Kit, 0.67 km a texel
        sky.SetTexture("_MoonNormal", AssetDatabase.LoadAssetAtPath<Texture>("Assets/Apollo/Textures/Moon/Moon_Relief_LOLA16.png")); // LOLA heights (Tools/moon_relief.py)
        sky.SetFloat("_MoonTexel", 0.666f);
        sky.SetTexture("_DetailNoise", AssetDatabase.LoadAssetAtPath<Texture3D>("Assets/Apollo/Textures/Clouds/Cloud_Shape.asset"));
        AssetDatabase.CreateAsset(sky, Dir + "/Sky_Florida.mat");

        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 1f;
        sun.cullingMask = ~(1 << CabinLayer);
        sun.transform.rotation = Quaternion.Euler(30f, -40f, 0f);

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.nearClipPlane = 0.3f; cam.farClipPlane = 90000f; cam.fieldOfView = 50f;
        cam.allowHDR = true;
        var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
        camData.renderPostProcessing = true;
        camData.requiresDepthOption = CameraOverrideOption.On;            // the 3D clouds stop at whatever is solid
        camGo.AddComponent<AudioListener>();

        // the 3D cumulus layer, drawn on a sphere that travels with the camera
        CloudNoise.Get(out var cloudShape, out var cloudDetailNoise, out var cloudWeather);
        var cloudMat = new Material(Shader.Find("Apollo/VolumeClouds")) { name = "Clouds_3D" };
        cloudMat.SetTexture("_CloudShape", cloudShape);
        cloudMat.SetTexture("_CloudDetailTex", cloudDetailNoise);
        cloudMat.SetTexture("_CloudWeather", cloudWeather);
        AssetDatabase.CreateAsset(cloudMat, Dir + "/Clouds_3D.mat");
        {
            var dome = new Geo();
            dome.Sphere(Vector3.zero, 50f, 32, 16);
            var dgo = Obj("Clouds_3D", dome, cloudMat, camGo.transform);
            var mr = dgo.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            dgo.GetComponent<MeshFilter>().sharedMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
        }

        var vol = new GameObject("Global Look").AddComponent<Volume>();
        vol.isGlobal = true;
        vol.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Apollo/Settings/Flight_Look.asset");

        var probe = new GameObject("PadReflections").AddComponent<ReflectionProbe>();
        probe.transform.position = new Vector3(0f, 60f, 0f);
        probe.mode = ReflectionProbeMode.Realtime;
        probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
        probe.size = new Vector3(6000f, 800f, 6000f);
        probe.resolution = 128;
        probe.cullingMask = ~(1 << CabinLayer);

        RenderSettings.skybox = sky;
        RenderSettings.sun = sun;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;                // humid Florida air: about a quarter gone at 5 km
        RenderSettings.fogDensity = 0.00011f;
        RenderSettings.fogColor = (new Color(0.86f, 0.90f, 0.95f).linear * 0.55f).gamma;   // the sky's own horizon haze
        Lightmapping.lightingSettings = AssetDatabase.LoadAssetAtPath<LightingSettings>("Assets/Apollo/Settings/RealtimeOnly_Lighting.lighting");

        // ---------------- director, UI, test ----------------
        var es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<StandaloneInputModule>();
        var fd = new GameObject("FlightDirector").AddComponent<FlightDirector>();
        fd.Cam = cam; fd.Sun = sun; fd.CommanderEye = eye; fd.Probe = probe;
        fd.CloudCoverage = cloudCoverage; fd.CloudDetail = cloudDetail; fd.CloudWeather = cloudWeather; fd.CloudShape = cloudShape; fd.SkyMat = sky;
        fd.SaturnRoot = saturn.transform; fd.WorldRoot = world;
        {
            var pm = new Material(Shader.Find("Apollo/Plume")) { name = "Engine_Plume" };
            pm.SetTexture("_Noise", cloudCoverage);
            AssetDatabase.CreateAsset(pm, Dir + "/Engine_Plume.mat");
            var tm = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = "Exhaust_Trail" };
            tm.SetFloat("_Surface", 1f); tm.SetFloat("_Blend", 0f);
            tm.SetOverrideTag("RenderType", "Transparent");
            tm.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); tm.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            tm.SetFloat("_ZWrite", 0f); tm.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            tm.renderQueue = 3000;
            AssetDatabase.CreateAsset(tm, Dir + "/Exhaust_Trail.mat");
            fd.PlumeMat = pm; fd.TrailMat = tm;
        }
        fd.Countdown = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Apollo/Audio/apollo11_countdown_launch_NASA.wav");
        fd.AscentRadio = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Apollo/Audio/apollo11_ascent_air_to_ground_NASA.mp3");
        fd.CabinLights = lights.ToArray();
        fd.Arm9 = arm9;
        fd.HatchClosed = cabin.transform.Find("Hatch_Closed").gameObject;
        fd.HatchOpening = hatchDecal;
        var hide = new List<Renderer>();
        foreach (var r in saturn.GetComponentsInChildren<Renderer>(true))
            if (r.name == "CM_Shell" || r.name == "LES_and_BPC") hide.Add(r);
        fd.HideWhileEntering = hide.ToArray();
        fd.FloorY = floorY;
        fd.WalkwayBounds = new Vector4(-0.55f, 0.55f, roomN - 0.2f, towerFace - 0.4f);
        fd.RoomBounds = new Vector4(-1.25f, 1.25f, roomS + 0.45f, roomN + 0.1f);
        fd.HatchOutside = new Vector3(0f, hatchP.y, bpcR + 0.02f);
        fd.HatchInside = hatchP;
        fd.HatchInward = hatchIn;
        fd.HatchWindow = hatchWindowPos;
        fd.Level320 = DeckY + 320 * Ft;
        new GameObject("FlightAutoTest").AddComponent<FlightAutoTest>();

        // The world root moves every frame during the climb (floating origin), but a static-batched mesh is baked into
        // world space when the player is built and never follows its parent: the 512 m ring stayed behind as a ground
        // line at eye level, and the VAB, LCC and 39B towers stayed put for the first 1.5 km. Nothing under it may batch.
        foreach (var tr in world.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.SetStaticEditorFlags(tr.gameObject, GameObjectUtility.GetStaticEditorFlags(tr.gameObject) & ~StaticEditorFlags.BatchingStatic);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log($"FLIGHT SCENE BUILT -> {ScenePath}  deck {DeckY:0.00}, Level 320 at {DeckY + 320 * Ft:0.00}, hatch at {hatchP} (h {hatchH:0.00} above the heat shield), " +
                  $"walk floor {floorY:0.00}, white room z {roomS:0.00}..{roomN:0.00}");
    }

    static Vector3 DeckYv(Vector3 xz, float up) => new Vector3(xz.x, DeckY + up, xz.z);

    // ---------------- sealing the scanned cabin ----------------
    // The museum scan is an open shell: Columbia's hatch is not in it, and thin gaps run between its panels.
    // The hatch is closed with a modelled door placed behind the scan's real rim; then the spacecraft's
    // structure goes behind the scan, open only where the commander really looks out of a window.
    static Vector3 SealCabin(Transform cabin, Transform eye, Vector3 hatchP)
    {
        const int mask = 1 << CabinLayer;
        int W = CabinProbe.W, H = CabinProbe.H;
        Vector3 E = eye.position; Quaternion Q = eye.rotation;
        float Rad(Vector3 p) => new Vector2(p.x, p.z).magnitude;
        Vector3 Exit(Vector3 d)
        {
            Vector3 q = E;
            for (int i = 0; i < 400 && Rad(q) < Radius(CmProfile, q.y - CmBase) - 0.02f; i++) q += d * 0.01f;
            return q;
        }
        float Pct(List<float> l, float q) { var c = new List<float>(l); c.Sort(); return c[Mathf.Clamp(Mathf.RoundToInt(q * (c.Count - 1)), 0, c.Count - 1)]; }

        // the scan's own outline (a little outside its walls), measured before anything is added
        const float binH = 0.05f, h0 = -0.4f;
        var maxR = new float[80];
        float hMin = 9f, hMax = -9f;
        foreach (var mfh in cabin.GetComponentsInChildren<MeshFilter>())
        {
            var vs = mfh.sharedMesh.vertices;
            for (int i = 0; i < vs.Length; i += 2)
            {
                Vector3 w = mfh.transform.TransformPoint(vs[i]);
                float h = w.y - CmBase;
                int bi = Mathf.Clamp(Mathf.FloorToInt((h - h0) / binH), 0, maxR.Length - 1);
                maxR[bi] = Mathf.Max(maxR[bi], Rad(w));
                hMin = Mathf.Min(hMin, h); hMax = Mathf.Max(hMax, h);
            }
        }
        var shellR = new float[maxR.Length];
        for (int i = 0; i < maxR.Length; i++)
        {
            float m = 0f;
            for (int k = -3; k <= 3; k++) m = Mathf.Max(m, maxR[Mathf.Clamp(i + k, 0, maxR.Length - 1)]);
            shellR[i] = m + 0.07f;
        }
        float R(float h) { float fi = (h - h0) / binH - 0.5f; int i = Mathf.Clamp(Mathf.FloorToInt(fi), 0, maxR.Length - 2); return Mathf.Lerp(shellR[i], shellR[i + 1], Mathf.Clamp01(fi - i)); }

        // 1. where the eye sees out of the scan. The hatch is the opening that is big in metres (about 1 m
        //    across where the rays leave the hull); the windows are 0.6 m or less, however large they look.
        var esc = CabinProbe.Escapes(E, Q, mask, 6f);
        var regions = CabinProbe.Regions(esc, out var label);
        var hatchIds = new HashSet<int>();
        Vector3 hatchSum = Vector3.zero; int hatchN = 0;
        foreach (var r in regions)
        {
            if (r.SolidAngle < 0.02f) continue;
            Vector3 lo = Vector3.one * 1e9f, hi = -Vector3.one * 1e9f, sum = Vector3.zero; int n = 0;
            for (int k = 0; k < r.Px.Count; k += 5)
            {
                int p = r.Px[k];
                Vector3 ex = Exit(CabinProbe.Dir(Q, p % W, p / W));
                lo = Vector3.Min(lo, ex); hi = Vector3.Max(hi, ex); sum += ex; n++;
            }
            float size = (hi - lo).magnitude;
            bool isHatch = size > 0.8f;
            if (isHatch) { hatchIds.Add(r.Id); hatchSum += sum; hatchN += n; }
            Vector3 c = sum / Mathf.Max(n, 1);
            Debug.Log($"FLIGHT cabin opening {r.Id}: {r.SolidAngle:0.000} sr, {size:0.00} m across at the hull, at h {c.y - CmBase:0.00} az {Mathf.Atan2(c.x, c.z) * Mathf.Rad2Deg:0}{(isHatch ? "  -> HATCH" : "  (window)")}");
        }
        // turn the cabin about the module's axis so its real hatch faces the access arm (north)
        Vector3 hc = hatchSum / Mathf.Max(hatchN, 1);
        float hatchAz = Mathf.Atan2(hc.x, hc.z) * Mathf.Rad2Deg;
        cabin.RotateAround(new Vector3(0f, CmBase, 0f), Vector3.up, -hatchAz);
        Physics.SyncTransforms();
        E = eye.position; Q = eye.rotation;
        Debug.Log($"FLIGHT cabin turned {-hatchAz:0.0} deg so the real hatch faces the tower");

        // 2. the opening's rim (the rays just beside it hit it) and the paint around it (a ring a little
        //    further out). Things in front of the opening, like the couch strut, are dropped.
        bool InHatch(int p) => label[p] >= 0 && hatchIds.Contains(label[p]);
        var near = new int[W * H];
        for (int i = 0; i < near.Length; i++) near[i] = InHatch(i) ? 0 : 99;
        for (int pass = 0; pass < 2; pass++)                                   // city-block distance to the opening, in pixels
            for (int y0 = 0; y0 < H; y0++)
                for (int x0 = 0; x0 < W; x0++)
                {
                    int y = pass == 0 ? y0 : H - 1 - y0, x = pass == 0 ? x0 : W - 1 - x0, p = y * W + x, d = near[p];
                    int dx = pass == 0 ? -1 : 1;
                    if (y + dx >= 0 && y + dx < H) d = Mathf.Min(d, near[(y + dx) * W + x] + 1);
                    d = Mathf.Min(d, near[y * W + (x + dx + W) % W] + 1);
                    near[p] = d;
                }
        var rim = new List<Vector3>(); var ring = new List<RaycastHit>();
        for (int p = 0; p < W * H; p++)
        {
            int d = near[p];
            if (d < 1 || d > 22) continue;
            if (d > 1 && (d < 10 || p % 3 != 0)) continue;
            if (!Physics.Raycast(E, CabinProbe.Dir(Q, p % W, p / W), out var hit, 6f, mask)) continue;
            if (d == 1) rim.Add(hit.point); else ring.Add(hit);
        }
        var outward = new List<float>();
        foreach (var pt in rim) outward.Add(Rad(pt) - Radius(CmProfile, pt.y - CmBase));
        float med = Pct(outward, 0.5f);
        var keep = new List<Vector3>();
        for (int i = 0; i < rim.Count; i++) if (outward[i] > med - 0.10f) keep.Add(rim[i]);
        var keepHits = new List<RaycastHit>();
        foreach (var h in ring) if (Rad(h.point) - Radius(CmProfile, h.point.y - CmBase) > med - 0.10f) keepHits.Add(h);
        Vector3 c0 = Vector3.zero;
        foreach (var pt in keep) c0 += pt;
        c0 /= Mathf.Max(1, keep.Count);

        // the door's frame follows the conical wall at the opening
        const float wallSlope = 0.498f;                                    // the CM's dr/dh
        var f = new CabinHatch.Frame();
        f.N = (new Vector3(c0.x, 0f, c0.z).normalized + Vector3.up * wallSlope).normalized;
        f.T = (Vector3.up - f.N * Vector3.Dot(Vector3.up, f.N)).normalized;
        f.S = Vector3.Cross(f.T, f.N);
        f.Rc = Rad(c0) / Mathf.Cos(Mathf.Atan(wallSlope));
        var ss = new List<float>(); var ts = new List<float>(); var ds = new List<float>();
        foreach (var pt in keep)
        {
            Vector3 v = pt - c0; float s = Vector3.Dot(v, f.S);
            ss.Add(s); ts.Add(Vector3.Dot(v, f.T)); ds.Add(Vector3.Dot(v, f.N) + s * s / (2f * f.Rc));
        }
        float s0 = Pct(ss, 0.02f), s1 = Pct(ss, 0.98f), t0 = Pct(ts, 0.02f), t1 = Pct(ts, 0.98f);
        f.C = c0 + f.S * ((s0 + s1) * 0.5f) + f.T * ((t0 + t1) * 0.5f) + f.N * (Pct(ds, 0.9f) + 0.012f);
        var spec = new CabinHatch.Spec
        {
            A = (s1 - s0) * 0.5f + 0.045f, B = (t1 - t0) * 0.5f + 0.045f,
            WindowR = HatchWindowRadius, Paint = ScanPaint(keepHits),
        };
        spec.Window = new Vector2(0f, spec.B * HatchWindowUp);
        var door = CabinHatch.Build(f, spec, cabin, out Vector3 winC);
        SetLayer(door.transform, CabinLayer);
        Debug.Log($"FLIGHT hatch opening: {keep.Count}/{rim.Count} rim hits on the wall, paint from {keepHits.Count} hits around it, opening {(s1 - s0):0.00} x {(t1 - t0):0.00} m, door {2 * spec.A:0.00} x {2 * spec.B:0.00} m, paint {spec.Paint}, window at {winC}");

        // 3. with the hatch closed: the directions in which the eye looks out of a real window
        Physics.SyncTransforms();
        var esc2 = CabinProbe.Escapes(E, Q, mask, 6f);
        var reg2 = CabinProbe.Regions(esc2, out var label2);
        Vector3 wd = (Quaternion.Inverse(Q) * (winC - E)).normalized;
        int wx = Mathf.Clamp(Mathf.FloorToInt((Mathf.Atan2(wd.x, wd.z) * Mathf.Rad2Deg + 180f) / 360f * W), 0, W - 1);
        int wy = Mathf.Clamp(Mathf.FloorToInt((Mathf.Asin(wd.y) * Mathf.Rad2Deg + 90f) / 180f * H), 0, H - 1);
        var open = new HashSet<int>();
        foreach (var r in reg2)
        {
            float lonSpan = r.LonMax - r.LonMin, latSpan = r.LatMax - r.LatMin;
            float fill = r.Pixels / Mathf.Max(1f, (lonSpan / 0.25f + 1f) * (latSpan / 0.25f + 1f));
            bool window = (r.SolidAngle >= 0.002f && fill >= 0.3f && lonSpan >= 1.5f && latSpan >= 1.5f) || label2[wy * W + wx] == r.Id;
            if (window) open.Add(r.Id);
            if (r.SolidAngle >= 0.0008f || window)
                Debug.Log($"FLIGHT cabin view {r.Id}: {r.SolidAngle:0.0000} sr, {lonSpan:0.0} x {latSpan:0.0} deg, fill {fill:0.00} -> {(window ? "WINDOW" : "gap, closed")}");
        }
        // the hatch window: every ray through its glass, whatever stands in front of it
        Vector3 wc0 = CabinHatch.P(f, spec.Window.x, spec.Window.y, 0f), wn = CabinHatch.Out(f, spec.Window.x);
        var hatchWin = new bool[W * H];
        int hwCount = 0;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                Vector3 d = CabinProbe.Dir(Q, x, y);
                float dn = Vector3.Dot(d, wn);
                if (dn <= 1e-4f) continue;
                float tt = Vector3.Dot(wc0 - E, wn) / dn;
                if ((E + d * tt - wc0).magnitude < spec.WindowR) { hatchWin[y * W + x] = true; hwCount++; }
            }
        Debug.Log($"FLIGHT hatch window seen in {hwCount} directions");
        var mbytes = new byte[W * H];
        for (int y = 0; y < H; y++)
        {
            float lat = ((y + 0.5f) / H * 180f - 90f) * Mathf.Deg2Rad;
            int ry = 4, rx = Mathf.CeilToInt(4f / Mathf.Max(Mathf.Cos(lat), 0.15f));
            for (int x = 0; x < W; x++)
            {
                int l = label2[y * W + x];
                if (!hatchWin[y * W + x] && (l < 0 || !open.Contains(l))) continue;
                for (int dy = -ry; dy <= ry; dy++)
                {
                    int yy = y + dy; if (yy < 0 || yy >= H) continue;
                    for (int dx = -rx; dx <= rx; dx++) mbytes[yy * W + (x + dx + W) % W] = 255;
                }
            }
        }
        var maskTex = new Texture2D(W, H, TextureFormat.R8, false, true) { name = "Cabin_WindowMask", wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        maskTex.SetPixelData(mbytes, 0); maskTex.Apply(false);
        AssetDatabase.CreateAsset(maskTex, Dir + "/Cabin_WindowMask.asset");

        // 4. the structure behind the scan, in the eye's own space (the shader looks up the mask from the eye)
        var shell = new Geo();
        float bottom = hMin - 0.06f, top = hMax + 0.06f;
        int seg = 120; float dh = 0.05f;
        for (float h = bottom; h < top - 1e-3f; h += dh)
            for (int i = 0; i < seg; i++)
            {
                float a0 = 2f * Mathf.PI * i / seg, a1 = 2f * Mathf.PI * (i + 1) / seg;
                float h1 = Mathf.Min(h + dh, top), r0 = R(h), r1 = R(h1);
                Vector3 p00 = new Vector3(Mathf.Sin(a0) * r0, CmBase + h, Mathf.Cos(a0) * r0), p10 = new Vector3(Mathf.Sin(a1) * r0, CmBase + h, Mathf.Cos(a1) * r0);
                Vector3 p11 = new Vector3(Mathf.Sin(a1) * r1, CmBase + h1, Mathf.Cos(a1) * r1), p01 = new Vector3(Mathf.Sin(a0) * r1, CmBase + h1, Mathf.Cos(a0) * r1);
                Vector3 mid = (p00 + p10 + p11 + p01) * 0.25f;
                shell.Quad(p00, p10, p11, p01, -new Vector3(mid.x, 0, mid.z).normalized);
            }
        for (int i = 0; i < seg; i++)
        {
            float a0 = 2f * Mathf.PI * i / seg, a1 = 2f * Mathf.PI * (i + 1) / seg, rb = R(bottom), rt = R(top);
            shell.Tri(new Vector3(0, CmBase + bottom, 0), new Vector3(Mathf.Sin(a0) * rb, CmBase + bottom, Mathf.Cos(a0) * rb), new Vector3(Mathf.Sin(a1) * rb, CmBase + bottom, Mathf.Cos(a1) * rb), Vector3.up);
            shell.Tri(new Vector3(0, CmBase + top, 0), new Vector3(Mathf.Sin(a0) * rt, CmBase + top, Mathf.Cos(a0) * rt), new Vector3(Mathf.Sin(a1) * rt, CmBase + top, Mathf.Cos(a1) * rt), Vector3.down);
        }
        shell.Transform(eye.worldToLocalMatrix);
        var sm = new Material(Shader.Find("Apollo/CabinShell")) { name = "Cabin_Structure" };
        sm.SetTexture("_Mask", maskTex);
        AssetDatabase.CreateAsset(sm, Dir + "/Cabin_Structure.mat");
        var sgo = Obj("Cabin_Structure", shell, sm, eye);
        sgo.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.TwoSided;       // the Sun shines in only through the windows
        SetLayer(sgo.transform, CabinLayer);
        Debug.Log($"FLIGHT cabin sealed: {open.Count} window openings kept, structure from h {bottom:0.00} to {top:0.00}");
        return winC;
    }

    // the scan's own paint around the hatch opening (median texel under the rim hits)
    static Color ScanPaint(List<RaycastHit> hits)
    {
        var rs = new List<float>(); var gs = new List<float>(); var bs = new List<float>();
        var cache = new Dictionary<Texture, Texture2D>();
        foreach (var h in hits)
        {
            var rend = h.collider.GetComponent<Renderer>();
            if (!rend || !rend.sharedMaterial || h.textureCoord == Vector2.zero) continue;
            var mat = rend.sharedMaterial;
            Texture tex = mat.mainTexture;
            if (!tex)
                foreach (var n in mat.GetTexturePropertyNames())
                    if ((n.ToLower().Contains("basecolor") || n == "_BaseMap") && mat.GetTexture(n)) { tex = mat.GetTexture(n); break; }
            if (!tex) continue;
            if (!cache.TryGetValue(tex, out var cpu))
            {
                var rt = RenderTexture.GetTemporary(1024, 1024, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(tex, rt);
                var prev = RenderTexture.active; RenderTexture.active = rt;
                cpu = new Texture2D(1024, 1024, TextureFormat.RGBA32, false, false);
                cpu.ReadPixels(new Rect(0, 0, 1024, 1024), 0, 0); cpu.Apply();
                RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
                cache[tex] = cpu;
            }
            Color c = cpu.GetPixelBilinear(h.textureCoord.x, h.textureCoord.y);
            rs.Add(c.r); gs.Add(c.g); bs.Add(c.b);
        }
        Debug.Log($"FLIGHT scan paint sampled from {rs.Count} of {hits.Count} rim hits");
        if (rs.Count < 20) return new Color(0.55f, 0.56f, 0.53f);
        rs.Sort(); gs.Sort(); bs.Sort();
        int k = rs.Count / 2;
        return new Color(rs[k], gs[k], bs[k]);
    }

    // ---------------- terrain ----------------

    static float[] LoadGrid(string path)
    {
        var b = File.ReadAllBytes(path);
        var f = new float[2048 * 2048];
        Buffer.BlockCopy(b, 0, f, 0, f.Length * 4);
        return f;
    }

    // Bilinear height (m above sea level) at data coordinates; grid row 0 is the north edge.
    static float Height(float[] g, float half, float x, float z)
    {
        float px = (x + half) / (2f * half) * 2048f - 0.5f, py = (half - z) / (2f * half) * 2048f - 0.5f;
        int x0 = Mathf.Clamp(Mathf.FloorToInt(px), 0, 2046), y0 = Mathf.Clamp(Mathf.FloorToInt(py), 0, 2046);
        float fx = Mathf.Clamp01(px - x0), fy = Mathf.Clamp01(py - y0);
        float V(int xx, int yy) { float v = g[yy * 2048 + xx]; return float.IsNaN(v) || v < -50f || v > 300f ? 0.3f : v; }
        return Mathf.Lerp(Mathf.Lerp(V(x0, y0), V(x0 + 1, y0), fx), Mathf.Lerp(V(x0, y0 + 1), V(x0 + 1, y0 + 1), fx), fy);
    }

    // A square grid of terrain around the data centre, with a square hole for the finer ring inside it.
    static void TerrainRing(string name, float[] dem, float demHalf, float outer, float inner, float step, float photoHalf, Material mat, Transform parent, bool flat)
    {
        int n = Mathf.RoundToInt(2f * outer / step) + 1;
        var v = new Vector3[n * n]; var uv = new Vector2[n * n];
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                float x = -outer + i * step, z = -outer + j * step;
                float h = flat ? -0.8f : Height(dem, demHalf, x, z);
                if (!flat && inner == 0f && PadDesign.ExactHeight(x, z, out float ph)) h = ph;
                if (!flat && h < 0.45f) h = Mathf.Min(h, -0.6f);            // water beds sit under the water surface
                v[j * n + i] = new Vector3(x - AxisX, h - PadTop, z - AxisZ);
                uv[j * n + i] = new Vector2((x + photoHalf) / (2f * photoHalf), (z + photoHalf) / (2f * photoHalf));
            }
        var t = new List<int>();
        for (int j = 0; j < n - 1; j++)
            for (int i = 0; i < n - 1; i++)
            {
                float cx = -outer + (i + 0.5f) * step, cz = -outer + (j + 0.5f) * step;
                if (inner > 0f && Mathf.Abs(cx) < inner && Mathf.Abs(cz) < inner) continue;
                if (inner == 0f && !flat && PadDesign.Hole(cx, cz)) continue;      // the hardstand, incline and trench have their own surfaces
                int a = j * n + i, b = a + 1, c = a + n + 1, d = a + n;
                t.AddRange(new[] { a, c, b, a, d, c });
            }
        var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
        mesh.vertices = v; mesh.uv = uv; mesh.SetTriangles(t, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, Dir + "/" + name + ".asset");
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        go.isStatic = false;   // under the moving world root: static batching would bake this ring in place

        // skirt around the hole so no light leaks through where two rings meet
        if (inner > 0f)
        {
            var s = new Geo();
            int k0 = Mathf.RoundToInt((outer - inner) / step), k1 = n - 1 - k0;
            for (int k = k0; k < k1; k++)
            {
                void Side(int ia, int ib, Vector3 toCentre)
                {
                    Vector3 p0 = v[ia], p1 = v[ib];
                    s.Quad(p0, p1, p1 + Vector3.down * 4f, p0 + Vector3.down * 4f, toCentre);
                }
                Side(k0 * n + k, k0 * n + k + 1, Vector3.forward);
                Side(k1 * n + k, k1 * n + k + 1, Vector3.back);
                Side(k * n + k0, (k + 1) * n + k0, Vector3.right);
                Side(k * n + k1, (k + 1) * n + k1, Vector3.left);
            }
            Obj(name + "_Skirt", s, mat, parent);
        }
    }

    // Copy a downloaded photo into the project, optionally edited, and import it.
    static Texture2D MakePhoto(string name, string src, Action<Texture2D> edit)
    {
        string dst = TexDir + "/" + name + ".jpg";
        if (edit == null) File.Copy(src, dst, true);
        else
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(File.ReadAllBytes(src));
            edit(tex);
            File.WriteAllBytes(dst, tex.EncodeToJPG(93));
            Object.DestroyImmediate(tex);
        }
        AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceUpdate);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(dst);
    }

    // The 2024 photo shows today's pad; the raised hardstand is repainted as the plain 1969 concrete pad,
    // with the flame trench dark under the launcher.
    static void PaintPad(Texture2D tex, float[] dem)
    {
        int w = tex.width; float m = 2048f / w;
        var px = tex.GetPixels32();
        for (int y = 0; y < w; y++)
        {
            float z = -1024f + (y + 0.5f) * m;
            for (int x = 0; x < w; x++)
            {
                float xd = -1024f + (x + 0.5f) * m, wx = xd - AxisX, wz = z - AxisZ;
                if (wx * wx + wz * wz > 175f * 175f) continue;
                float h = Height(dem, 1024f, xd, z);
                float k = Mathf.Clamp01((h - 12.8f) / 0.8f);
                bool ramp = Mathf.Abs(wx) < 42f && wz < -60f;
                if (k <= 0f)
                {
                    if (h > 1.8f && !ramp)                                        // the berm sides are grass
                    {
                        float gk = Mathf.Clamp01((h - 1.8f) / 0.8f) * Mathf.Clamp01((175f - Mathf.Sqrt(wx * wx + wz * wz)) / 15f);
                        float gn = 0.85f + 0.3f * Mathf.PerlinNoise(xd * 0.07f, z * 0.07f);
                        px[y * w + x] = Color32.Lerp(px[y * w + x], (Color32)(new Color(0.31f, 0.40f, 0.18f) * gn), gk);
                    }
                    continue;
                }
                bool trench = Mathf.Abs(wx) < 9.0f && wz > -58f && wz < 46f;
                float nse = 0.94f + 0.06f * Mathf.PerlinNoise(xd * 0.09f, z * 0.09f) + 0.03f * (Mathf.PerlinNoise(xd * 0.8f, z * 0.8f) - 0.5f);
                float burn = Mathf.Clamp01(1f - Mathf.Abs(wx) / 30f) * Mathf.Clamp01(1f - Mathf.Abs(wz) / 70f) * 0.18f;
                float joint = (Mathf.Repeat(wx, 7.5f) < 0.5f || Mathf.Repeat(wz, 7.5f) < 0.5f) ? 0.86f : 1f;   // expansion joints
                Color c = trench ? new Color(0.20f, 0.20f, 0.19f) : new Color(0.60f, 0.59f, 0.56f) * (nse - burn) * joint;
                var o = px[y * w + x];
                px[y * w + x] = Color32.Lerp(o, (Color32)c, k);
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false);
    }

    // USGS has no imagery far offshore (white): fill it with the ocean colour of the photo.
    static void FillOcean(Texture2D tex)
    {
        var px = tex.GetPixels32();
        var sea = new Color32(30, 82, 104, 255);
        for (int i = 0; i < px.Length; i++)
            if (px[i].r > 238 && px[i].g > 238 && px[i].b > 238) px[i] = sea;
        tex.SetPixels32(px);
        tex.Apply(false);
    }

    // ---------------- tower ----------------

    // 40 ft square tower: eight perimeter columns, beams and X-bracing on every floor, floors,
    // two elevator shafts, service piping on the face toward the vehicle, crane and lightning mast.
    static void BuildTower(Geo o, Geo floors, Geo pipes)
    {
        float h = TowerHalf, cz = TowerZ;
        var lv = new List<float> { 0f, 30 * Ft, 60 * Ft };
        for (int k = 80; k <= 360; k += 20) lv.Add(k * Ft);
        lv.Add(380 * Ft);
        float top = lv[lv.Count - 1];
        float[] c = { -h, 0f, h };
        foreach (float x in c)
            foreach (float z in c)
            {
                if (x == 0f && z == 0f) continue;
                float s = x != 0f && z != 0f ? 0.95f : 0.65f;
                o.Box(new Vector3(x, DeckY + top / 2f, cz + z), new Vector3(s, top, s), Quaternion.identity);
            }
        Vector3 P(float x, float y, float z) => new Vector3(x, DeckY + y, cz + z);
        for (int i = 0; i < lv.Count; i++)
        {
            float y = lv[i];
            for (int s = -1; s <= 1; s += 2)
            {
                o.Beam(P(-h, y, s * h), P(0, y, s * h), 0.45f, 0.7f); o.Beam(P(0, y, s * h), P(h, y, s * h), 0.45f, 0.7f);
                o.Beam(P(s * h, y, -h), P(s * h, y, 0), 0.45f, 0.7f); o.Beam(P(s * h, y, 0), P(s * h, y, h), 0.45f, 0.7f);
            }
            if (i > 0)
            {
                o.Beam(P(-h, y, 0), P(h, y, 0), 0.35f, 0.5f); o.Beam(P(0, y, -h), P(0, y, h), 0.35f, 0.5f);
                floors.Box(P(0, y - 0.35f, 0), new Vector3(2 * h - 0.3f, 0.25f, 2 * h - 0.3f), Quaternion.identity);
            }
            if (i == lv.Count - 1) break;
            float y1 = lv[i + 1];
            for (int s = -1; s <= 1; s += 2)
                for (int b = 0; b < 2; b++)
                {
                    float a0 = b == 0 ? -h : 0f, a1 = b == 0 ? 0f : h;
                    o.Beam(P(a0, y, s * h), P(a1, y1, s * h), 0.28f, 0.28f); o.Beam(P(a1, y, s * h), P(a0, y1, s * h), 0.28f, 0.28f);
                    o.Beam(P(s * h, y, a0), P(s * h, y1, a1), 0.28f, 0.28f); o.Beam(P(s * h, y, a1), P(s * h, y1, a0), 0.28f, 0.28f);
                }
        }
        foreach (float x in new[] { -2.7f, 2.7f })
            floors.Box(P(x, 340 * Ft / 2f, 2.4f), new Vector3(2.4f, 340 * Ft, 2.4f), Quaternion.identity);
        foreach (var (x, r) in new[] { (-4.6f, 0.34f), (-3.7f, 0.24f), (-2.9f, 0.2f), (2.9f, 0.2f), (3.7f, 0.24f), (4.6f, 0.34f) })
            pipes.Cyl(P(x, 0f, -h - 0.7f), P(x, 300 * Ft, -h - 0.7f), r, r, 12);

        float roof = top;
        o.Box(P(0, roof + 1.0f, 0), new Vector3(4.0f, 2.0f, 4.0f), Quaternion.identity);
        o.Box(P(0, roof + 3.0f, 0), new Vector3(2.4f, 2.0f, 2.4f), Quaternion.identity);
        float boomY = 445 * Ft - DeckY - 0.9f;
        for (int s = -1; s <= 1; s += 2)
        {
            o.Beam(P(-11f, boomY + s * 0.7f, -0.7f), P(19f, boomY + s * 0.7f, -0.7f), 0.25f, 0.25f);
            o.Beam(P(-11f, boomY + s * 0.7f, 0.7f), P(19f, boomY + s * 0.7f, 0.7f), 0.25f, 0.25f);
        }
        for (float x = -11f; x < 19f; x += 2f)
        {
            o.Beam(P(x, boomY - 0.7f, -0.7f), P(x + 2f, boomY + 0.7f, -0.7f), 0.15f, 0.15f);
            o.Beam(P(x, boomY - 0.7f, 0.7f), P(x + 2f, boomY + 0.7f, 0.7f), 0.15f, 0.15f);
            o.Beam(P(x, boomY - 0.7f, -0.7f), P(x, boomY - 0.7f, 0.7f), 0.15f, 0.15f);
            o.Beam(P(x, boomY + 0.7f, -0.7f), P(x, boomY + 0.7f, 0.7f), 0.15f, 0.15f);
        }
        o.Box(P(-10f, boomY - 0.2f, 0), new Vector3(2.8f, 2.4f, 2.4f), Quaternion.identity);
        o.Box(P(1.8f, boomY - 2.0f, -1.9f), new Vector3(1.8f, 1.8f, 1.6f), Quaternion.identity);
        pipes.Cyl(P(18f, boomY - 0.7f, 0), P(18f, boomY - 7f, 0), 0.04f, 0.04f, 6);
        o.Box(P(18f, boomY - 7.4f, 0), new Vector3(0.7f, 0.9f, 0.5f), Quaternion.identity);
        pipes.Cyl(P(0, roof + 4.0f, 0), new Vector3(0, 490 * Ft, cz), 0.32f, 0.10f, 10);
    }

    // A swing arm: a box truss from the tower face to the vehicle, with a hinge post at the tower.
    static void Arm(Geo o, Vector3 start, Vector3 end, float wd, float ht)
    {
        Vector3 fwd = end - start; fwd.y = 0; float len = fwd.magnitude; fwd /= len;
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized, up = Vector3.up;
        float hw = wd / 2f, hh = ht / 2f;
        int n = Mathf.Max(2, Mathf.RoundToInt(len / 2.6f)); float step = len / n;
        Vector3 Q(float s, float side, float v) => start + fwd * s + right * side + up * v;
        foreach (float sx in new[] { -hw, hw })
            foreach (float sy in new[] { -hh, hh })
                o.Beam(Q(0, sx, sy), Q(len, sx, sy), 0.24f, 0.24f);
        for (int i = 0; i <= n; i++)
        {
            float s = i * step;
            o.Beam(Q(s, -hw, -hh), Q(s, -hw, hh), 0.16f, 0.16f); o.Beam(Q(s, hw, -hh), Q(s, hw, hh), 0.16f, 0.16f);
            o.Beam(Q(s, -hw, hh), Q(s, hw, hh), 0.16f, 0.16f); o.Beam(Q(s, -hw, -hh), Q(s, hw, -hh), 0.16f, 0.16f);
            if (i == n) break;
            o.Beam(Q(s, -hw, -hh), Q(s + step, -hw, hh), 0.13f, 0.13f); o.Beam(Q(s, hw, -hh), Q(s + step, hw, hh), 0.13f, 0.13f);
            o.Beam(Q(s, -hw, hh), Q(s + step, hw, hh), 0.13f, 0.13f);
        }
        o.Box(Q(0.3f, -hw - 0.35f, 0f), new Vector3(0.7f, ht + 1.6f, 0.7f), Quaternion.LookRotation(fwd));
    }

    // ---------------- helpers ----------------

    static Vector3 FromGltf(Vector3 v) => new Vector3(-v.x, v.y, v.z);

    static void SetLayer(Transform t, int layer)
    {
        t.gameObject.layer = layer;
        foreach (Transform c in t) SetLayer(c, layer);
    }

    static void EnsureCabinLayer()
    {
        var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var el = tm.FindProperty("layers").GetArrayElementAtIndex(CabinLayer);
        if (el.stringValue != "Cabin") { el.stringValue = "Cabin"; tm.ApplyModifiedProperties(); }
    }

    internal static Material Lit(string name, Color c, float smooth, float metal = 0f, Texture tex = null)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Metallic", metal);
        if (tex != null) m.SetTexture("_BaseMap", tex);
        AssetDatabase.CreateAsset(m, Dir + "/" + name + ".mat");
        return m;
    }

    internal static GameObject Obj(string name, Geo g, Material m, Transform parent)
    {
        var mesh = g.ToMesh(name);
        AssetDatabase.CreateAsset(mesh, Dir + "/" + name + ".asset");
        var go = new GameObject(name);
        if (parent) go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = m;
        return go;
    }

    // Minimal mesh builder: quads, triangles, boxes, beams, cylinders, spheres (front faces follow the normal).
    internal class Geo
    {
        readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
        readonly List<Vector2> uv = new List<Vector2>();
        readonly List<int> t = new List<int>();

        static Vector2 UV(Vector3 p, Vector3 nn) =>
            Mathf.Abs(nn.y) > 0.7f ? new Vector2(p.x, p.z) : Mathf.Abs(nn.x) > Mathf.Abs(nn.z) ? new Vector2(p.z, p.y) : new Vector2(p.x, p.y);

        void Add(Vector3 p, Vector3 nn) { v.Add(p); n.Add(nn); uv.Add(UV(p, nn)); }

        public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 nn)
        {
            int i = v.Count; Add(a, nn); Add(b, nn); Add(c, nn);
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), nn) >= 0) t.AddRange(new[] { i, i + 1, i + 2 });
            else t.AddRange(new[] { i, i + 2, i + 1 });
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 nn) => QuadN(a, b, c, d, nn, nn, nn, nn);

        public void QuadN(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd)
        {
            int i = v.Count; Add(a, na); Add(b, nb); Add(c, nc); Add(d, nd);
            Vector3 avg = na + nb + nc + nd;
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), avg) >= 0) t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            else t.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
        }

        public void Box(Vector3 c, Vector3 s, Quaternion r)
        {
            Vector3 X = r * Vector3.right, Y = r * Vector3.up, Z = r * Vector3.forward;
            Vector3 ax = X * s.x * 0.5f, ay = Y * s.y * 0.5f, az = Z * s.z * 0.5f;
            Face(c + ax, ay, az, X); Face(c - ax, ay, az, -X);
            Face(c + ay, ax, az, Y); Face(c - ay, ax, az, -Y);
            Face(c + az, ax, ay, Z); Face(c - az, ax, ay, -Z);
        }

        void Face(Vector3 fc, Vector3 a, Vector3 b, Vector3 nn) => Quad(fc - a - b, fc + a - b, fc + a + b, fc - a + b, nn);

        public void Beam(Vector3 a, Vector3 b, float w, float h)
        {
            Vector3 d = b - a; float len = d.magnitude;
            if (len < 1e-4f) return;
            Vector3 upHint = Mathf.Abs(d.y) > 0.99f * len ? Vector3.forward : Vector3.up;
            Box((a + b) * 0.5f, new Vector3(w, h, len), Quaternion.LookRotation(d, upHint));
        }

        public void Cyl(Vector3 a, Vector3 b, float r0, float r1, int seg, bool caps = true)
        {
            Vector3 ax = (b - a).normalized;
            Vector3 u = Vector3.Cross(ax, Mathf.Abs(ax.y) < 0.9f ? Vector3.up : Vector3.right).normalized, w = Vector3.Cross(ax, u);
            for (int i = 0; i < seg; i++)
            {
                float t0 = 2f * Mathf.PI * i / seg, t1 = 2f * Mathf.PI * (i + 1) / seg;
                Vector3 d0 = u * Mathf.Cos(t0) + w * Mathf.Sin(t0), d1 = u * Mathf.Cos(t1) + w * Mathf.Sin(t1);
                QuadN(a + d0 * r0, a + d1 * r0, b + d1 * r1, b + d0 * r1, d0, d1, d1, d0);
                if (caps) { Tri(a, a + d1 * r0, a + d0 * r0, -ax); Tri(b, b + d0 * r1, b + d1 * r1, ax); }
            }
        }

        public void Sphere(Vector3 c, float r, int lonSeg, int latSeg)
        {
            for (int j = 0; j < latSeg; j++)
                for (int i = 0; i < lonSeg; i++)
                {
                    Vector3 P(int ii, int jj)
                    {
                        float lat = Mathf.PI * jj / latSeg - Mathf.PI / 2f, lon = 2f * Mathf.PI * ii / lonSeg;
                        return new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));
                    }
                    Vector3 p0 = P(i, j), p1 = P(i + 1, j), p2 = P(i + 1, j + 1), p3 = P(i, j + 1);
                    QuadN(c + p0 * r, c + p1 * r, c + p2 * r, c + p3 * r, p0, p1, p2, p3);
                }
        }

        public void Transform(Matrix4x4 m)
        {
            for (int i = 0; i < v.Count; i++) { v[i] = m.MultiplyPoint3x4(v[i]); n[i] = m.MultiplyVector(n[i]).normalized; }
        }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }
    }
}
