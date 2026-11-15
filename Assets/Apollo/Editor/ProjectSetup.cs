// One-time project setup for the Apollo mission experience.
// Creates the URP pipeline tuned for an Intel Iris Xe laptop, post-processing profiles,
// the two scenes and the build list. Safe to run again: it rebuilds everything it owns.
// Run from the menu (Apollo > Run Project Setup) or headless with
//   -batchmode -quit -executeMethod ProjectSetup.Run
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class ProjectSetup
{
    const string Root = "Assets/Apollo";
    const string SettingsDir = Root + "/Settings";
    const string ScenesDir = Root + "/Scenes";

    static readonly string[] Folders =
    {
        "Scenes", "Scripts", "Models", "Textures", "Materials", "Audio", "Prefabs", "Settings"
    };

    [MenuItem("Apollo/Run Project Setup")]
    public static void Run()
    {
        foreach (var f in Folders) Directory.CreateDirectory(Path.Combine(Root, f));
        AssetDatabase.Refresh();

        ImportTextMeshProEssentials();

        var urp = MakePipeline();
        ApplyGraphics(urp);
        ApplyPlayer();
        var lighting = MakeLightingSettings();

        var controlProfile = MakeVolumeProfile("MissionControl_Look", bloom: 0.35f, vignette: 0.22f, exposure: 0f);
        var flightProfile = MakeVolumeProfile("Flight_Look", bloom: 0.8f, vignette: 0.18f, exposure: 0f);

        var controlScene = MakeScene("MissionControl", controlProfile, lighting,
            ambient: new Color(0.05f, 0.05f, 0.06f), sunIntensity: 0f, clear: new Color(0.01f, 0.01f, 0.012f));
        var flightScene = MakeScene("Flight", flightProfile, lighting,
            ambient: new Color(0.004f, 0.004f, 0.006f), sunIntensity: 2.2f, clear: Color.black);

        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(controlScene, true),
            new EditorBuildSettingsScene(flightScene, true),
        };

        AssetDatabase.SaveAssets();
        EditorSceneManager.OpenScene(controlScene);
        Debug.Log("APOLLO SETUP OK");
    }

    static void ImportTextMeshProEssentials()
    {
        if (Directory.Exists("Assets/TextMesh Pro")) return;
        const string pkg = "Packages/com.unity.textmeshpro/Package Resources/TMP Essential Resources.unitypackage";
        AssetDatabase.ImportPackage(pkg, false);
        AssetDatabase.Refresh();
    }

    // ---------- Render pipeline ----------

    static UniversalRenderPipelineAsset MakePipeline()
    {
        string rendererPath = SettingsDir + "/Laptop_Renderer.asset";
        string pipelinePath = SettingsDir + "/Laptop_URP.asset";
        AssetDatabase.DeleteAsset(pipelinePath);
        AssetDatabase.DeleteAsset(rendererPath);

        var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
        renderer.copyDepthMode = CopyDepthMode.AfterOpaques;             // the 3D clouds read this frame's depth (the cabin's windows), not last frame's
        renderer.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(
            UniversalRenderPipelineAsset.packagePath + "/Runtime/Data/PostProcessData.asset");
        AssetDatabase.CreateAsset(renderer, rendererPath);
        ResourceReloader.ReloadAllNullIn(renderer, UniversalRenderPipelineAsset.packagePath);

        var urp = UniversalRenderPipelineAsset.Create(renderer);
        AssetDatabase.CreateAsset(urp, pipelinePath);

        // Tuned for Intel Iris Xe at 1080p: HDR for bright engine flames and the Sun,
        // light MSAA, two shadow cascades, no extra full-screen textures.
        var so = new SerializedObject(urp);
        so.FindProperty("m_SupportsHDR").boolValue = true;
        so.FindProperty("m_MSAA").intValue = 2;
        so.FindProperty("m_RenderScale").floatValue = 1f;
        so.FindProperty("m_RequireDepthTexture").boolValue = false;
        so.FindProperty("m_RequireOpaqueTexture").boolValue = false;
        so.FindProperty("m_MainLightShadowsSupported").boolValue = true;
        so.FindProperty("m_MainLightShadowmapResolution").intValue = 2048;
        so.FindProperty("m_ShadowDistance").floatValue = 120f;
        so.FindProperty("m_ShadowCascadeCount").intValue = 2;
        so.FindProperty("m_Cascade2Split").floatValue = 0.2f;
        so.FindProperty("m_SoftShadowsSupported").boolValue = true;
        so.FindProperty("m_AdditionalLightsRenderingMode").intValue = (int)LightRenderingMode.PerPixel;
        so.FindProperty("m_AdditionalLightsPerObjectLimit").intValue = 4;
        so.FindProperty("m_AdditionalLightShadowsSupported").boolValue = false;
        so.FindProperty("m_UseSRPBatcher").boolValue = true;
        so.FindProperty("m_ColorGradingMode").intValue = (int)ColorGradingMode.HighDynamicRange;
        so.FindProperty("m_ColorGradingLutSize").intValue = 32;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(urp);
        return urp;
    }

    static void ApplyGraphics(UniversalRenderPipelineAsset urp)
    {
        GraphicsSettings.defaultRenderPipeline = urp;
        int current = QualitySettings.GetQualityLevel();
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = urp;
            QualitySettings.vSyncCount = 1;
        }
        QualitySettings.SetQualityLevel(current, false);
    }

    static void ApplyPlayer()
    {
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.productName = "Apollo Mission";
        PlayerSettings.defaultScreenWidth = 1920;
        PlayerSettings.defaultScreenHeight = 1080;
        PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
        PlayerSettings.runInBackground = true;
    }

    // Realtime-only lighting so Unity never starts a slow lightmap bake on the laptop.
    static LightingSettings MakeLightingSettings()
    {
        string path = SettingsDir + "/RealtimeOnly_Lighting.lighting";
        var ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(path);
        if (ls == null)
        {
            ls = new LightingSettings { name = "RealtimeOnly_Lighting" };
            AssetDatabase.CreateAsset(ls, path);
        }
        ls.bakedGI = false;
        ls.realtimeGI = false;
        ls.autoGenerate = false;
        EditorUtility.SetDirty(ls);
        return ls;
    }

    // ---------- Post-processing looks ----------

    static VolumeProfile MakeVolumeProfile(string name, float bloom, float vignette, float exposure)
    {
        string path = SettingsDir + "/" + name + ".asset";
        AssetDatabase.DeleteAsset(path);
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, path);

        var tone = profile.Add<Tonemapping>(true);
        tone.mode.Override(TonemappingMode.ACES);

        var b = profile.Add<Bloom>(true);
        b.threshold.Override(1.0f);
        b.intensity.Override(bloom);
        b.scatter.Override(0.7f);
        b.highQualityFiltering.Override(false);

        var v = profile.Add<Vignette>(true);
        v.intensity.Override(vignette);
        v.smoothness.Override(0.45f);

        var c = profile.Add<ColorAdjustments>(true);
        c.postExposure.Override(exposure);
        c.contrast.Override(8f);

        foreach (var comp in profile.components)
        {
            comp.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(comp, profile);
        }
        EditorUtility.SetDirty(profile);
        return profile;
    }

    // ---------- Scenes ----------

    static string MakeScene(string name, VolumeProfile profile, LightingSettings lighting,
                            Color ambient, float sunIntensity, Color clear)
    {
        string path = ScenesDir + "/" + name + ".unity";
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = clear;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 5000f;
        cam.fieldOfView = 55f;
        camGo.AddComponent<AudioListener>();
        var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
        camData.renderPostProcessing = true;
        camGo.transform.position = new Vector3(0f, 1.6f, -4f);

        var sunGo = new GameObject("Sun");
        var sun = sunGo.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = sunIntensity;
        sun.color = new Color(1f, 0.97f, 0.92f);
        sun.shadows = LightShadows.Soft;
        sun.enabled = sunIntensity > 0f;
        sunGo.transform.rotation = Quaternion.Euler(35f, -40f, 0f);

        var volGo = new GameObject("Global Look");
        var vol = volGo.AddComponent<Volume>();
        vol.isGlobal = true;
        vol.sharedProfile = profile;

        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = ambient;
        RenderSettings.fog = false;
        RenderSettings.sun = sunIntensity > 0f ? sun : null;
        Lightmapping.lightingSettings = lighting;

        EditorSceneManager.SaveScene(scene, path);
        return path;
    }
}
