// Builds the mission planner scene: the real sky, Earth and Moon at real scale (1 unit = 1000 km),
// markers, camera, light and UI. Menu: Apollo > Build Mission Planner
// Headless: -executeMethod PlannerBuilder.Build
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class PlannerBuilder
{
    const string Dir = "Assets/Apollo/Models/Planner";
    const string ScenePath = "Assets/Apollo/Scenes/MissionPlanner.unity";
    const string Tex = "Assets/Apollo/Textures";

    [MenuItem("Apollo/Build Mission Planner")]
    public static void Build()
    {
        if (AssetDatabase.IsValidFolder(Dir)) AssetDatabase.DeleteAsset(Dir);
        Directory.CreateDirectory(Dir);
        AssetDatabase.Refresh();
        MakeFonts();

        var sphere = SphereMesh(256, 128);

        Material Mat(string name, string shader)
        {
            var m = new Material(Shader.Find(shader)) { name = name };
            AssetDatabase.CreateAsset(m, Dir + "/" + name + ".mat");
            return m;
        }
        T Load<T>(string p) where T : Object => AssetDatabase.LoadAssetAtPath<T>(p);

        var sky = Mat("Sky_RealStars", "Apollo/StarSky");
        sky.SetTexture("_Stars", Load<Texture>(Tex + "/Sky/starmap_2020_8k.exr"));
        sky.SetTexture("_Milky", Load<Texture>(Tex + "/Sky/milkyway_2020_8k.exr"));
        var earthMat = Mat("Earth_NASA", "Apollo/Earth");
        earthMat.SetTexture("_DayTex", Load<Texture>(Tex + "/Earth/world.topo.bathy.200407.3x21600x10800.jpg"));
        earthMat.SetTexture("_NightTex", Load<Texture>(Tex + "/Earth/BlackMarble_2016_3km.jpg"));
        earthMat.SetTexture("_CloudTex", Load<Texture>(Tex + "/Earth/clouds_2025-07-16.png"));
        var atmo = Mat("Earth_Atmosphere", "Apollo/Atmosphere");
        var moonMat = Mat("Moon_LRO", "Apollo/Moon");
        moonMat.SetTexture("_MainTex", Load<Texture>(Tex + "/Moon/lroc_color_poles_4k.tif"));
        var lines = Mat("Lines", "Apollo/Lines");
        var marker = Mat("Marker", "Universal Render Pipeline/Unlit");
        marker.SetColor("_BaseColor", new Color(1f, 0.85f, 0.45f));
        var ghost = Mat("MoonAtLiftoff", "Universal Render Pipeline/Unlit");
        ghost.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.10f));
        ghost.SetFloat("_Surface", 1f);
        ghost.SetFloat("_Blend", 0f);
        ghost.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        ghost.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        ghost.SetFloat("_ZWrite", 0f);
        ghost.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        ghost.renderQueue = (int)RenderQueue.Transparent;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        GameObject Body(string name, Material m, float radius, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent) go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = sphere;
            go.AddComponent<MeshRenderer>().sharedMaterial = m;
            go.transform.localScale = Vector3.one * radius;
            return go;
        }
        var earth = Body("Earth", earthMat, SpaceScene.EarthRadius);
        var atmoGo = Body("Atmosphere", atmo, 1.025f, earth.transform);
        var moon = Body("Moon", moonMat, SpaceScene.MoonRadius);
        var moonGhost = Body("MoonAtLiftoff", ghost, SpaceScene.MoonRadius);

        GameObject Marker(string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = name;
            go.GetComponent<Renderer>().sharedMaterial = marker;
            return go;
        }
        var ksc = Marker("Marker_KennedySpaceCenter");
        var pin = Marker("Marker_LandingSite").transform;

        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 1.6f;
        sun.shadows = LightShadows.None;

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.nearClipPlane = 0.02f; cam.farClipPlane = 3000f; cam.fieldOfView = 45f;
        cam.allowHDR = true;
        camGo.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
        camGo.AddComponent<AudioListener>();
        var orbit = camGo.AddComponent<OrbitCam>();

        var vol = new GameObject("Global Look").AddComponent<Volume>();
        vol.isGlobal = true;
        vol.sharedProfile = Load<VolumeProfile>("Assets/Apollo/Settings/Flight_Look.asset");

        var sc = new GameObject("SpaceScene").AddComponent<SpaceScene>();
        sc.Earth = earth.transform; sc.Moon = moon.transform; sc.MoonAtLaunch = moonGhost.transform;
        sc.Kennedy = ksc.transform; sc.SitePin = pin; sc.Sun = sun; sc.LineMaterial = lines;

        var es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<StandaloneInputModule>();
        var ui = new GameObject("PlannerUI").AddComponent<PlannerUI>();
        ui.Cam = orbit;
        new GameObject("AutoTest").AddComponent<PlannerAutoTest>();

        RenderSettings.skybox = sky;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = Color.black;
        RenderSettings.fog = false;
        Lightmapping.lightingSettings = Load<LightingSettings>("Assets/Apollo/Settings/RealtimeOnly_Lighting.lighting");

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(ScenePath, true),
            new EditorBuildSettingsScene("Assets/Apollo/Scenes/Flight.unity", true),
        };
        AssetDatabase.SaveAssets();
        Debug.Log("MISSION PLANNER BUILT -> " + ScenePath);
    }

    // JetBrains Mono (SIL OFL) as dynamic TextMeshPro fonts in Resources/Fonts, loaded by UiKit.
    static void MakeFonts()
    {
        const string outDir = "Assets/Apollo/Resources/Fonts";
        Directory.CreateDirectory(outDir);
        AssetDatabase.Refresh();
        var made = new List<TMPro.TMP_FontAsset>();
        foreach (var name in new[] { "JetBrainsMono-Regular", "JetBrainsMono-Bold" })
        {
            string path = outDir + "/" + name + " SDF.asset";
            AssetDatabase.DeleteAsset(path);
            var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Apollo/Fonts/" + name + ".ttf");
            var fa = TMPro.TMP_FontAsset.CreateFontAsset(font, 72, 8, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024,
                TMPro.AtlasPopulationMode.Dynamic, true);
            fa.name = name + " SDF";
            AssetDatabase.CreateAsset(fa, path);
            fa.atlasTextures[0].name = name + " Atlas";
            AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
            fa.material.name = name + " Material";
            AssetDatabase.AddObjectToAsset(fa.material, fa);
            fa.TryAddCharacters("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .,:;!?'\"-+/%()[]<>_=·—°‹›→←↑↓", out _);
            EditorUtility.SetDirty(fa);
            made.Add(fa);
        }
        // <b> in Regular text uses the real Bold face (same 0.6 em advance, so columns stay aligned)
        made[0].fontWeightTable[7].regularTypeface = made[1];
        EditorUtility.SetDirty(made[0]);
        AssetDatabase.SaveAssets();
    }

    // Unit sphere with longitude/latitude UVs matching NASA equirectangular maps (u = 0 at 180 W),
    // and the body frame used by Ephemeris.BodyPoint (east = +Z seen from +X with north up).
    static Mesh SphereMesh(int lonSegs, int latSegs)
    {
        var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
        for (int i = 0; i <= latSegs; i++)
        {
            double lat = -90.0 + 180.0 * i / latSegs;
            for (int j = 0; j <= lonSegs; j++)
            {
                double lon = -180.0 + 360.0 * j / lonSegs;
                var p = Ephemeris.BodyPoint(lat, lon);
                v.Add(p); n.Add(p); uv.Add(new Vector2((float)j / lonSegs, (float)i / latSegs));
            }
        }
        int row = lonSegs + 1;
        for (int i = 0; i < latSegs; i++)
            for (int j = 0; j < lonSegs; j++)
            {
                int a = i * row + j, b = a + 1, c = a + row + 1, d = a + row;
                Vector3 mid = (v[a] + v[b] + v[c] + v[d]).normalized;
                Vector3 geo = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
                if (geo.sqrMagnitude < 1e-12f) geo = Vector3.Cross(v[c] - v[a], v[d] - v[a]);
                if (Vector3.Dot(geo, mid) > 0) tri.AddRange(new[] { a, b, c, a, c, d });
                else tri.AddRange(new[] { a, c, b, a, d, c });
            }
        var mesh = new Mesh { name = "Sphere_LonLat", indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0);
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, Dir + "/Sphere_LonLat.asset");
        return mesh;
    }
}
