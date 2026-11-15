// Step 6 assets (menu Apollo > Step 6 Assets, or -executeMethod Step6Assets.Setup):
//  - the NASA SVS CGI Moon Kit colour map at 16k and the LOLA relief (made from ldem_16_uint.tif by
//    Tools/moon_relief.py) on the sky material
//  - the Apollo Flight Journal radio clips (Resources/Radio): mono Vorbis, decoded in the background; their sound
//    is also written out at 8 kHz (SourceFiles/Step6_Audio/*.f32) to find each spoken line's place in the file
//  - the NASA Lunar Module model (Resources/LM): its parts are logged
using System.IO;
using UnityEditor;
using UnityEngine;

public static class Step6Assets
{
    const string MoonColour = "Assets/Apollo/Textures/Moon/lroc_color_poles_16k.tif";
    const string MoonRelief = "Assets/Apollo/Textures/Moon/Moon_Relief_LOLA16.png";
    const string SkyMat = "Assets/Apollo/Models/Flight/Sky_Florida.mat";

    [MenuItem("Apollo/Step 6 Assets")]
    public static void Setup()
    {
        AssetDatabase.Refresh();
        if (AssetImporter.GetAtPath(MoonColour) is TextureImporter tc)
        {
            tc.maxTextureSize = 16384; tc.sRGBTexture = true; tc.mipmapEnabled = true;
            tc.wrapModeU = TextureWrapMode.Repeat; tc.wrapModeV = TextureWrapMode.Clamp;
            tc.textureCompression = TextureImporterCompression.Compressed; tc.filterMode = FilterMode.Trilinear; tc.anisoLevel = 4;
            var ps = tc.GetPlatformTextureSettings("Standalone"); ps.overridden = true; ps.maxTextureSize = 16384; ps.format = TextureImporterFormat.DXT1; tc.SetPlatformTextureSettings(ps);
            tc.SaveAndReimport();
        }
        if (AssetImporter.GetAtPath(MoonRelief) is TextureImporter tr)
        {
            tr.maxTextureSize = 8192; tr.sRGBTexture = false; tr.mipmapEnabled = true;
            tr.wrapModeU = TextureWrapMode.Repeat; tr.wrapModeV = TextureWrapMode.Clamp;
            tr.textureCompression = TextureImporterCompression.CompressedHQ; tr.filterMode = FilterMode.Trilinear;
            tr.SaveAndReimport();
        }
        var sky = AssetDatabase.LoadAssetAtPath<Material>(SkyMat);
        if (sky)
        {
            var c = AssetDatabase.LoadAssetAtPath<Texture>(MoonColour); var r = AssetDatabase.LoadAssetAtPath<Texture>(MoonRelief);
            if (c) { sky.SetTexture("_MoonTex", c); sky.SetFloat("_MoonTexel", 0.666f); }
            if (r) sky.SetTexture("_MoonNormal", r);
            EditorUtility.SetDirty(sky);
            Debug.Log($"STEP6 sky material: moon colour {(c ? c.width : 0)}, relief {(r ? r.width : 0)}");
        }

        // the radio
        Directory.CreateDirectory("SourceFiles/Step6_Audio");
        foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Apollo/Resources/Radio" }))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            var ai = (AudioImporter)AssetImporter.GetAtPath(p);
            ai.forceToMono = true;
            var s = ai.defaultSampleSettings;
            s.loadType = AudioClipLoadType.DecompressOnLoad; s.compressionFormat = AudioCompressionFormat.PCM; s.preloadAudioData = true;
            ai.defaultSampleSettings = s; ai.SaveAndReimport();
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(p);
            clip.LoadAudioData();
            var data = new float[clip.samples * clip.channels];
            if (clip.GetData(data, 0))
            {
                int step = Mathf.Max(1, clip.frequency / 8000);
                using (var w = new BinaryWriter(File.Create("SourceFiles/Step6_Audio/" + Path.GetFileNameWithoutExtension(p) + ".f32")))
                    for (int i = 0; i + step <= data.Length; i += step)
                    {
                        float a = 0; for (int k = 0; k < step; k++) a += data[i + k];
                        w.Write(a / step);
                    }
                Debug.Log($"STEP6 radio {Path.GetFileName(p)}: {clip.length:0.00} s at {clip.frequency} Hz, written at {clip.frequency / step} Hz");
            }
            else Debug.LogError("STEP6 could not read " + p);
            s.loadType = AudioClipLoadType.CompressedInMemory; s.compressionFormat = AudioCompressionFormat.Vorbis; s.quality = 0.6f; s.preloadAudioData = true;
            ai.defaultSampleSettings = s; ai.loadInBackground = true; ai.SaveAndReimport();
        }

        // the Lunar Module
        foreach (var guid in AssetDatabase.FindAssets("t:GameObject", new[] { "Assets/Apollo/Resources/LM" }))
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (!go) continue;
            var b = new Bounds(); bool first = true;
            foreach (var mr in go.GetComponentsInChildren<Renderer>(true))
            {
                if (first) { b = mr.bounds; first = false; } else b.Encapsulate(mr.bounds);
            }
            Debug.Log($"STEP6 LM {go.name}: bounds centre {b.center} size {b.size}");
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                var mf = t.GetComponent<MeshFilter>();
                Debug.Log($"STEP6 LM node {NodePath(t)}  pos {t.localPosition} {(mf && mf.sharedMesh ? $"mesh {mf.sharedMesh.vertexCount} v bounds {mf.sharedMesh.bounds.center} {mf.sharedMesh.bounds.size}" : "")}");
            }
        }
        AssetDatabase.SaveAssets();
        Debug.Log("STEP6 assets done");
    }

    static string NodePath(Transform t) => t.parent == null ? t.name : NodePath(t.parent) + "/" + t.name;
}
