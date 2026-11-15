// Step 7 (the landing) assets, menu Apollo > Step 7 Assets:
//  - the Apollo Lunar Surface Journal's powered-descent radio (Resources/Radio/a11_1023114: GET 102:31:14 to 102:47:46),
//    mono Vorbis like the other radio clips; its sound is also written out at 8 kHz (SourceFiles/Step7_Audio/*.f32) to
//    find each spoken line's place in the file (Tools/radio_lines.py)
//  - the lunar surface material (Resources/Moon/LunarSurface.mat): the shader plus the 16k LROC colour map, so the
//    shader ships in the player (a runtime Shader.Find alone would be stripped)
using System.IO;
using UnityEditor;
using UnityEngine;

public static class Step7Assets
{
    const string Radio = "Assets/Apollo/Resources/Radio/a11_1023114.mp3";
    const string MoonColour = "Assets/Apollo/Textures/Moon/lroc_color_poles_16k.tif";
    const string SurfaceMat = "Assets/Apollo/Resources/Moon/LunarSurface.mat";
    const string RockMat = "Assets/Apollo/Resources/Moon/LunarRock.mat";

    [MenuItem("Apollo/Step 7 Assets")]
    public static void Setup()
    {
        AssetDatabase.Refresh();
        if (AssetImporter.GetAtPath(Radio) is AudioImporter ai && !File.Exists("SourceFiles/Step7_Audio/a11_1023114.f32"))
        {
            Directory.CreateDirectory("SourceFiles/Step7_Audio");
            ai.forceToMono = true;
            var s = ai.defaultSampleSettings;
            s.loadType = AudioClipLoadType.DecompressOnLoad; s.compressionFormat = AudioCompressionFormat.PCM; s.preloadAudioData = true;
            ai.defaultSampleSettings = s; ai.SaveAndReimport();
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Radio);
            clip.LoadAudioData();
            var data = new float[clip.samples * clip.channels];
            if (clip.GetData(data, 0))
            {
                int step = Mathf.Max(1, clip.frequency / 8000);
                using (var w = new BinaryWriter(File.Create("SourceFiles/Step7_Audio/a11_1023114.f32")))
                    for (int i = 0; i + step <= data.Length; i += step)
                    {
                        float a = 0; for (int k = 0; k < step; k++) a += data[i + k];
                        w.Write(a / step);
                    }
                Debug.Log($"STEP7 radio: {clip.length:0.00} s at {clip.frequency} Hz, written at {clip.frequency / step} Hz");
            }
            else Debug.LogError("STEP7 could not read " + Radio);
            s.loadType = AudioClipLoadType.CompressedInMemory; s.compressionFormat = AudioCompressionFormat.Vorbis; s.quality = 0.6f; s.preloadAudioData = true;
            ai.defaultSampleSettings = s; ai.loadInBackground = true; ai.SaveAndReimport();
        }

        var colour = AssetDatabase.LoadAssetAtPath<Texture>(MoonColour);
        Material Make(string path, string shader)
        {
            var sh = Shader.Find(shader);
            if (!sh) { Debug.LogWarning("STEP7 no shader " + shader); return null; }
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            m.shader = sh;
            if (colour) m.SetTexture("_MoonTex", colour);
            EditorUtility.SetDirty(m);
            return m;
        }
        Make(SurfaceMat, "Apollo/LunarSurface");
        // the boulders: plain URP Lit, dark grey regolith-coated rock, no shine (it shades close to the ground's Lommel-Seeliger
        // at the low Sun the landings had); instanced and casting shadows
        var rm = AssetDatabase.LoadAssetAtPath<Material>(RockMat);
        if (!rm) { rm = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(rm, RockMat); }
        rm.shader = Shader.Find("Universal Render Pipeline/Lit");
        rm.SetColor("_BaseColor", new Color(0.34f, 0.33f, 0.32f));
        rm.SetFloat("_Smoothness", 0f); rm.SetFloat("_Metallic", 0f);
        rm.SetFloat("_SpecularHighlights", 0f); rm.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        rm.SetFloat("_EnvironmentReflections", 0f); rm.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        rm.enableInstancing = true;
        EditorUtility.SetDirty(rm);
        AssetDatabase.SaveAssets();
        Debug.Log("STEP7 assets done");
    }
}
