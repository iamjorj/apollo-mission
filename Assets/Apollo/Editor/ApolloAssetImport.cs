// Import rules for the real NASA / Smithsonian assets, tuned for an Intel Iris Xe laptop.
// Unity applies these automatically whenever a file in these folders is (re)imported.
using UnityEditor;
using UnityEngine;

public class ApolloAssetImport : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        var ti = (TextureImporter)assetImporter;
        string p = assetPath.Replace('\\', '/');
        if (!p.StartsWith("Assets/Apollo/Textures/")) return;

        ti.textureType = TextureImporterType.Default;
        ti.anisoLevel = 8;
        ti.filterMode = FilterMode.Trilinear;
        ti.wrapModeU = TextureWrapMode.Repeat;   // longitude wraps around
        ti.wrapModeV = TextureWrapMode.Clamp;    // latitude stops at the poles
        ti.npotScale = TextureImporterNPOTScale.None;

        // NASA SVS Deep Star Maps 2020 (HDR, equirectangular, celestial coordinates)
        if (p.StartsWith("Assets/Apollo/Textures/Sky/"))
        {
            ti.sRGBTexture = false;
            ti.mipmapEnabled = false;
            ti.filterMode = FilterMode.Bilinear;
            Set(ti, 8192, TextureImporterFormat.BC6H);
            return;
        }

        if (p.StartsWith("Assets/Apollo/Textures/Earth/"))
        {
            ti.sRGBTexture = true;
            ti.mipmapEnabled = true;
            if (p.Contains("world.topo.bathy"))          // Blue Marble NG, July
                Set(ti, 16384, TextureImporterFormat.DXT1);
            else if (p.Contains("BlackMarble"))          // city lights
                Set(ti, 8192, TextureImporterFormat.DXT1);
            else if (p.Contains("gibs_"))                // source photo for the cloud layer only
            {
                ti.mipmapEnabled = false;
                Set(ti, 8192, TextureImporterFormat.RGB24);
            }
            else if (p.Contains("clouds_"))              // cloud layer we build from the photo
            {
                ti.textureType = TextureImporterType.SingleChannel;
                ti.sRGBTexture = false;
                ti.textureShape = TextureImporterShape.Texture2D;
                Set(ti, 8192, TextureImporterFormat.BC4);
            }
            return;
        }

        // Saturn V stencil lettering masks (white on transparent)
        if (p.StartsWith("Assets/Apollo/Textures/Markings/"))
        {
            ti.sRGBTexture = true;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = true;
            ti.wrapModeU = TextureWrapMode.Clamp;
            Set(ti, 8192, TextureImporterFormat.DXT5);
            return;
        }

        // USGS / USDA orthophotos of Kennedy Space Center (made by FlightBuilder from SourceFiles/KSC)
        if (p.StartsWith("Assets/Apollo/Textures/KSC/"))
        {
            ti.sRGBTexture = true;
            ti.mipmapEnabled = true;
            ti.wrapModeU = TextureWrapMode.Clamp;
            Set(ti, 4096, TextureImporterFormat.DXT1);
            return;
        }

        // ambientCG CC0 ground materials (tiling detail for the Kennedy terrain)
        if (p.StartsWith("Assets/Apollo/Textures/Ground/"))
        {
            ti.mipmapEnabled = true;
            ti.wrapModeU = ti.wrapModeV = TextureWrapMode.Repeat;
            if (p.Contains("NormalGL")) { ti.textureType = TextureImporterType.NormalMap; ti.sRGBTexture = false; Set(ti, 1024, TextureImporterFormat.DXT5); }
            else { ti.sRGBTexture = true; Set(ti, 1024, TextureImporterFormat.DXT1); }
            return;
        }

        if (p.StartsWith("Assets/Apollo/Textures/Moon/"))
        {
            ti.mipmapEnabled = true;
            ti.wrapModeU = TextureWrapMode.Repeat; ti.wrapModeV = TextureWrapMode.Clamp;
            if (p.Contains("Relief"))                                     // LOLA relief as normals: linear, kept sharp
            {
                ti.sRGBTexture = false; ti.npotScale = TextureImporterNPOTScale.ToLarger;
                Set(ti, 8192, TextureImporterFormat.BC7);
            }
            else
            {
                ti.sRGBTexture = true;
                Set(ti, p.Contains("_16k") ? 16384 : 4096, TextureImporterFormat.DXT1);
            }
        }
    }

    static void Set(TextureImporter ti, int max, TextureImporterFormat format)
    {
        ti.maxTextureSize = max;
        var standalone = ti.GetPlatformTextureSettings("Standalone");
        standalone.overridden = true;
        standalone.maxTextureSize = max;
        standalone.format = format;
        standalone.textureCompression = TextureImporterCompression.CompressedHQ;
        ti.SetPlatformTextureSettings(standalone);

        var def = ti.GetDefaultPlatformTextureSettings();
        def.maxTextureSize = max;
        def.textureCompression = format == TextureImporterFormat.RGB24
            ? TextureImporterCompression.Uncompressed
            : TextureImporterCompression.Compressed;
        ti.SetPlatformTextureSettings(def);
    }
}
