// Check pictures of the built Saturn V: whole vehicle, engines, paint bands, spacecraft, stages apart.
// Headless: -executeMethod SaturnVPreview.BuildAndRender   Output: Logs/previews/sv_*.png
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class SaturnVPreview
{
    const string PrefabPath = "Assets/Apollo/Prefabs/SaturnV_AS506.prefab";

    public static void BuildAndRender()
    {
        SaturnVBuilder.Build();
        Render();
        Debug.Log("SATURN V PREVIEW OK");
    }

    [MenuItem("Apollo/Render Saturn V Previews")]
    public static void Render()
    {
        string outDir = Path.GetFullPath("Logs/previews");
        Directory.CreateDirectory(outDir);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.55f, 0.65f, 0.80f);
        RenderSettings.ambientEquatorColor = new Color(0.50f, 0.52f, 0.55f);
        RenderSettings.ambientGroundColor = new Color(0.25f, 0.24f, 0.20f);

        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 2.0f;
        sun.color = new Color(1f, 0.97f, 0.92f);
        sun.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(28f, 205f, 0f);

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.transform.localScale = Vector3.one * 60f;
        var gm = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        gm.SetColor("_BaseColor", new Color(0.36f, 0.38f, 0.30f));
        ground.GetComponent<Renderer>().sharedMaterial = gm;
        ground.transform.position = new Vector3(0f, -0.05f, 0f);

        var cam = new GameObject("Cam").AddComponent<Camera>();
        cam.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.40f, 0.56f, 0.80f);
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 3000f;

        var urp = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
        float oldShadow = urp.shadowDistance;
        urp.shadowDistance = 400f;

        var sv = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));

        // warm-up frames so lighting and shadow maps are ready before the first real picture
        for (int i = 0; i < 3; i++) Shot(cam, outDir, "_warmup", Polar(150f, 160f, 14f), new Vector3(0f, 55f, 0f), 46f);
        File.Delete(Path.Combine(outDir, "_warmup.png"));

        Shot(cam, outDir, "sv_1_full", Polar(150f, 160f, 14f), new Vector3(0f, 55f, 0f), 46f);
        Shot(cam, outDir, "sv_2_full_other_side", Polar(-30f, 160f, 14f), new Vector3(0f, 55f, 0f), 46f);
        Shot(cam, outDir, "sv_3_engines_fins", Polar(160f, 26f, 2.5f), new Vector3(0f, 5.5f, 0f), 58f);
        Shot(cam, outDir, "sv_4_from_below", new Vector3(9f, -0.5f, 13f), new Vector3(0f, 6f, 0f), 70f);
        Shot(cam, outDir, "sv_5_interstage_band", Polar(150f, 34f, 46f), new Vector3(0f, 45f, 0f), 50f);
        Shot(cam, outDir, "sv_6_upper_stages", Polar(150f, 34f, 78f), new Vector3(0f, 78f, 0f), 50f);
        Shot(cam, outDir, "sv_7_spacecraft_LES", Polar(150f, 20f, 101f), new Vector3(0f, 100f, 0f), 50f);

        // stages apart, in flight order
        string[] parts = { "S-IC", "S-II_AftInterstage", "S-II", "S-IVB", "IU", "SLA", "SM", "CM", "LES" };
        float[] lift = { 0f, 7f, 14f, 22f, 25f, 28f, 32f, 35f, 40f };
        for (int i = 0; i < parts.Length; i++)
        {
            var t = sv.transform.Find(parts[i]);
            if (t != null) t.localPosition += Vector3.up * lift[i];
            else Debug.LogError("Missing part " + parts[i]);
        }
        Shot(cam, outDir, "sv_8_stages_apart", Polar(150f, 190f, 20f), new Vector3(0f, 74f, 0f), 50f);

        urp.shadowDistance = oldShadow;
        Debug.Log("Saturn V previews written to " + outDir);
    }

    static Vector3 Polar(float azDeg, float r, float y) =>
        new Vector3(r * Mathf.Sin(azDeg * Mathf.Deg2Rad), y, r * Mathf.Cos(azDeg * Mathf.Deg2Rad));

    static void Shot(Camera cam, string dir, string name, Vector3 pos, Vector3 target, float fov)
    {
        cam.transform.position = pos;
        cam.transform.LookAt(target);
        cam.fieldOfView = fov;
        const int W = 1600, H = 1000;
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
        cam.targetTexture = rt;
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        cam.targetTexture = null;
        File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        rt.Release();
    }
}
