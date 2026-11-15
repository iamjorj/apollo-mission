// Step 2 tools: build the real-cloud layer, assemble the Smithsonian Columbia scan into prefabs,
// and render check pictures of every asset to Logs/previews.
// Menu: Apollo > ...   Headless: -executeMethod ApolloAssets.RunAll
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class ApolloAssets
{
    const string Tex = "Assets/Apollo/Textures";
    const string Models = "Assets/Apollo/Models";
    const string Prefabs = "Assets/Apollo/Prefabs";
    const string BlueMarble = Tex + "/Earth/world.topo.bathy.200407.3x21600x10800.jpg";
    const string GibsPhoto = Tex + "/Earth/gibs_viirs_truecolor_2025-07-16_8192.jpg";
    const string CloudsOut = Tex + "/Earth/clouds_2025-07-16.png";

    // Smithsonian Voyager layout (centimetres, glTF axes): all interior parts share one offset.
    static readonly Vector3 InteriorGroup = new Vector3(0f, -183.87f, -32f);
    static readonly Vector3 InteriorPart = new Vector3(83.5f, 2.4f, -76.6f);
    static readonly Vector3 ExteriorPart = new Vector3(0f, -166.82f, 0f);

    static readonly string[] InteriorFiles =
    {
        "x3d-cm-interior-hull-uvs-150k-4096-high.glb",
        "x3d-cm-interior-seats-uvs-150k-4096-high.glb",
        "x3d-cm-interior-panel-uvs-150k-4096-high.glb",
        "x3d-cm-interior-graffiti-uvs-150k-2048-high.glb",
    };
    static readonly string[] ExteriorFiles =
    {
        "x3d-cm-exterior-shell-90k-uvs-150k-4096-high.glb",
        "x3d-cm-exterior-top-160k-uvs-150k-4096-high.glb",
        "x3d-cm-exterior-filler-web.glb",
    };

    public static void RunAll()
    {
        foreach (var guid in AssetDatabase.FindAssets("", new[] { Models }))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (p.EndsWith(".glb")) AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
        }
        GltfReport.Run();
        if (!File.Exists(CloudsOut)) BuildClouds();
        AssembleColumbia();
        RenderPreviews();
        Debug.Log("APOLLO STEP2 OK");
    }

    // glTF is right-handed; glTFast mirrors X when importing into Unity's left-handed space.
    static Vector3 FromGltf(Vector3 v) => new Vector3(-v.x, v.y, v.z);

    // ---------- Clouds of 16 July 2025 from the NASA GIBS satellite photo ----------

    [MenuItem("Apollo/Build Cloud Layer")]
    public static void BuildClouds()
    {
        var photo = AssetDatabase.LoadAssetAtPath<Texture2D>(GibsPhoto);
        var ground = AssetDatabase.LoadAssetAtPath<Texture2D>(BlueMarble);
        if (photo == null || ground == null) { Debug.LogError("Cloud sources missing"); return; }

        var mat = new Material(Shader.Find("Hidden/Apollo/CloudExtract"));
        mat.SetTexture("_Base", ground);
        const int w = 8192, h = 4096;
        var rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        Graphics.Blit(photo, rt, mat);

        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var rgb = new Texture2D(w, h, TextureFormat.RGB24, false, true);
        rgb.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        rgb.Apply();
        RenderTexture.active = prev;

        var px = rgb.GetPixels32();
        var gray = new byte[px.Length];
        for (int i = 0; i < px.Length; i++) gray[i] = px[i].r;
        var single = new Texture2D(w, h, TextureFormat.R8, false, true);
        single.LoadRawTextureData(gray);
        single.Apply();
        File.WriteAllBytes(CloudsOut, single.EncodeToPNG());

        Object.DestroyImmediate(rgb);
        Object.DestroyImmediate(single);
        rt.Release();
        AssetDatabase.ImportAsset(CloudsOut);
        Debug.Log("Clouds written: " + CloudsOut);
    }

    // ---------- Columbia prefabs from the Smithsonian scan ----------

    [MenuItem("Apollo/Assemble Columbia")]
    public static void AssembleColumbia()
    {
        Directory.CreateDirectory(Prefabs);

        // Interior: crew cabin, couches, main display console, the crew's handwritten notes.
        var interior = new GameObject("Columbia_Interior");
        interior.transform.localScale = Vector3.one * 0.01f;      // scan is in centimetres
        var group = new GameObject("Interior").transform;
        group.SetParent(interior.transform, false);
        group.localPosition = FromGltf(InteriorGroup);
        string[] names = { "Hull", "Seats", "Panel", "Graffiti" };
        for (int i = 0; i < InteriorFiles.Length; i++)
            AddPart(group, Models + "/Columbia/Interior/" + InteriorFiles[i], names[i], FromGltf(InteriorPart));
        SavePrefab(interior, Prefabs + "/Columbia_Interior.prefab");

        // Exterior: heat shield, hatch, windows, thrusters.
        var exterior = new GameObject("Columbia_Exterior");
        exterior.transform.localScale = Vector3.one * 0.01f;
        string[] exNames = { "Shell", "ForwardSection", "Filler" };
        for (int i = 0; i < ExteriorFiles.Length; i++)
            AddPart(exterior.transform, Models + "/Columbia/Exterior/" + ExteriorFiles[i], exNames[i], FromGltf(ExteriorPart));
        SavePrefab(exterior, Prefabs + "/Columbia_Exterior.prefab");
    }

    static void AddPart(Transform parent, string path, string name, Vector3 localPos)
    {
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (src == null) { Debug.LogError("Missing model " + path); return; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
    }

    static void SavePrefab(GameObject go, string path)
    {
        PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
    }

    // ---------- Check pictures ----------

    [MenuItem("Apollo/Render Asset Previews")]
    public static void RenderPreviews()
    {
        string outDir = Path.GetFullPath("Logs/previews");
        Directory.CreateDirectory(outDir);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.35f, 0.35f, 0.38f);

        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 1.6f;
        sun.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(40f, -30f, 0f);

        var cam = new GameObject("PreviewCam").AddComponent<Camera>();
        cam.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.06f, 0.06f, 0.07f);
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = 5000f;
        cam.fieldOfView = 50f;

        // 1-3: Columbia interior
        var interior = (GameObject)PrefabUtility.InstantiatePrefab(
            AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "/Columbia_Interior.prefab"));
        var ib = Bounds(interior);
        Debug.Log("Interior bounds " + ib.center + " size " + ib.size);
        var hull = interior.transform.Find("Interior/Hull").gameObject;
        hull.SetActive(false);
        Frame(cam, ib, new Vector3(0.6f, 0.9f, -1f), 1.1f);
        Shot(cam, outDir, "1_cabin_cutaway");
        hull.SetActive(true);

        // Commander's eye: below his station on the main display console, looking up at it.
        var panel = interior.transform.Find("Interior/Panel");
        Vector3 cdr = panel.TransformPoint(FromGltf(new Vector3(-76.39f, 206.33f, 37.47f)));
        Vector3 cdrNormal = panel.TransformDirection(FromGltf(new Vector3(-0.0039f, -0.9555f, 0.2949f))).normalized;
        Vector3 center = panel.TransformPoint(FromGltf(new Vector3(-1.0f, 218.47f, 77.99f)));
        cam.fieldOfView = 75f;
        cam.transform.position = cdr + cdrNormal * 0.62f;
        cam.transform.LookAt(Vector3.Lerp(cdr, center, 0.45f), Vector3.forward);
        Shot(cam, outDir, "2_commander_view_upZ");
        cam.transform.LookAt(Vector3.Lerp(cdr, center, 0.45f), Vector3.back);
        Shot(cam, outDir, "3_commander_view_downZ");
        Object.DestroyImmediate(interior);

        // 4: Columbia exterior
        cam.fieldOfView = 40f;
        var exterior = (GameObject)PrefabUtility.InstantiatePrefab(
            AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "/Columbia_Exterior.prefab"));
        var eb = Bounds(exterior);
        Debug.Log("Exterior bounds " + eb.center + " size " + eb.size);
        Frame(cam, eb, new Vector3(1f, 0.35f, -1f), 1.3f);
        Shot(cam, outDir, "4_capsule_exterior");
        Object.DestroyImmediate(exterior);

        // 5: Saturn V
        var saturn = (GameObject)PrefabUtility.InstantiatePrefab(
            AssetDatabase.LoadAssetAtPath<GameObject>(Models + "/SaturnV/SaturnV.glb"));
        var sb = Bounds(saturn);
        Debug.Log("Saturn V bounds " + sb.center + " size " + sb.size);
        LogHierarchy(saturn.transform, 0);
        Frame(cam, sb, new Vector3(1f, 0.15f, -1f), 1.15f);
        Shot(cam, outDir, "5_saturn_v");
        Object.DestroyImmediate(saturn);

        // 6: Earth (Blue Marble, July)
        var earth = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        var em = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        em.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(BlueMarble));
        em.SetFloat("_Smoothness", 0.2f);
        earth.GetComponent<Renderer>().sharedMaterial = em;
        earth.transform.localScale = Vector3.one * 2f;
        earth.transform.rotation = Quaternion.Euler(0f, 80f, 0f);
        cam.fieldOfView = 30f;
        cam.transform.position = new Vector3(0f, 0.6f, -5f);
        cam.transform.LookAt(Vector3.zero);
        Shot(cam, outDir, "6_earth_july");
        Object.DestroyImmediate(earth);

        // 7: Real sky (NASA Deep Star Maps 2020) as a panoramic skybox
        var sky = new Material(Shader.Find("Skybox/Panoramic"));
        sky.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture>(Tex + "/Sky/starmap_2020_8k.exr"));
        sky.SetFloat("_Exposure", 1.5f);
        RenderSettings.skybox = sky;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.fieldOfView = 70f;
        cam.transform.position = Vector3.zero;
        cam.transform.rotation = Quaternion.Euler(-20f, 95f, 0f);
        Shot(cam, outDir, "7_star_sky");
        cam.transform.rotation = Quaternion.Euler(30f, 200f, 0f);
        Shot(cam, outDir, "8_star_sky_2");

        Debug.Log("Previews written to " + outDir);
    }

    static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>(true);
        var b = rs.Length > 0 ? rs[0].bounds : new Bounds(go.transform.position, Vector3.one);
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static void Frame(Camera cam, Bounds b, Vector3 dir, float margin)
    {
        float radius = b.extents.magnitude * margin;
        float dist = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        cam.transform.position = b.center + dir.normalized * dist;
        cam.transform.LookAt(b.center);
    }

    static void Shot(Camera cam, string dir, string name)
    {
        var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
        rt.antiAliasing = 4;
        cam.targetTexture = rt;
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        cam.targetTexture = null;
        File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        rt.Release();
    }

    static void LogHierarchy(Transform t, int depth)
    {
        if (depth > 3) return;
        var mf = t.GetComponent<MeshFilter>();
        Debug.Log("SATURN " + new string(' ', depth * 2) + t.name +
                  (mf && mf.sharedMesh ? "  [" + mf.sharedMesh.vertexCount + " verts]" : ""));
        foreach (Transform c in t) LogHierarchy(c, depth + 1);
    }
}
