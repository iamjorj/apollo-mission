// The ground at the landing site (Landing.cs / LunarTerrain.cs), in the site's own frame: x east, y up, z north, metres,
// the origin on the chosen point. Three height tiles, each centred on the site:
//   _H0  the real LOLA heights (NASA, 16 px/deg) plus generated craters, 256 km wide (r: height, fades to the datum at its edge
//        so it meets the sky's Moon sphere beyond)
//   _H1  generated craters 15-600 m, 16 km wide      (r: height, g: brightness of fresh crater ejecta)
//   _H2  generated craters and boulders, 2 km wide   (r: height, g: brightness)
// The mesh is a polar grid placed around _MeshCenter (fine in the middle); every vertex is lifted to the height and dropped
// by the Moon's curvature. Shading as the sky draws the Moon (SpaceSky: LROC colour, Lommel-Seeliger, x1.6), with the real
// relief lighting each pixel, shadows marched toward the low Sun through the heights, and Eagle's shadow from URP.
Shader "Apollo/LunarSurface"
{
    Properties
    {
        _MoonTex ("LROC colour", 2D) = "grey" {}
        _H0 ("Heights far", 2D) = "black" {}
        _H1 ("Heights mid", 2D) = "black" {}
        _H2 ("Heights near", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "Lunar"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MoonTex); SAMPLER(sampler_MoonTex);
            TEXTURE2D(_H0); TEXTURE2D(_H1); TEXTURE2D(_H2);
            SAMPLER(sampler_linear_clamp);

            CBUFFER_START(UnityPerMaterial)
            float4 _MoonTex_ST;
            float4 _Tiles;          // half-size (m) of H0, H1, H2; w: texels per tile
            float4 _Site;           // lat, lon (rad), radius of the local datum (m), 1 / cos(lat)
            float4 _MeshCenter;     // xz: the mesh's centre (m, site frame)
            float4 _Dust;           // xz: the engine's point on the ground; y: sheet radius (m); w: how much dust (0-1)
            float _Exposure;        // the sky's exposure (SpaceSky), so the ground meets the sky's Moon
            float _ShadowSteps;
            float _Gain;            // the eye adapts to the bright ground (the same gain goes to the sky's Moon: _MoonGain)
            CBUFFER_END

            struct A { float4 pos : POSITION; };
            struct V { float4 cs : SV_POSITION; float3 p : TEXCOORD0; float3 ws : TEXCOORD1; };

            float Fade(float2 uv, float band) { float2 a = abs(uv - 0.5); return saturate((0.5 - max(a.x, a.y)) / band); }

            // the heights at a point (site frame, m): tile by tile, each with its own detail level
            float Height(float2 p, float3 lod)
            {
                float2 u0 = p / (2 * _Tiles.x) + 0.5, u1 = p / (2 * _Tiles.y) + 0.5, u2 = p / (2 * _Tiles.z) + 0.5;
                float h = SAMPLE_TEXTURE2D_LOD(_H0, sampler_linear_clamp, u0, lod.x).r * Fade(u0, 0.12);
                float f1 = Fade(u1, 0.08), f2 = Fade(u2, 0.08);
                if (f1 > 0) h += SAMPLE_TEXTURE2D_LOD(_H1, sampler_linear_clamp, u1, lod.y).r * f1;
                if (f2 > 0) h += SAMPLE_TEXTURE2D_LOD(_H2, sampler_linear_clamp, u2, lod.z).r * f2;
                return h;
            }
            float Curve(float2 p) { return dot(p, p) / (2 * _Site.z); }
            float3 Lods(float metres)
            {
                float3 texel = _Tiles.xyz * 2 / _Tiles.w;
                return max(log2(max(metres, 1e-3) / texel), 0);
            }

            V vert(A i)
            {
                V o;
                float2 p = i.pos.xz + _MeshCenter.xz;
                float r = length(i.pos.xz);
                // the finest detail a vertex here can carry: about the grid spacing around it
                float h = Height(p, Lods(max(r * 0.04, 0.5)));
                float3 ps = float3(p.x, h - Curve(p), p.y);
                o.p = float3(p.x, h, p.y);
                o.ws = TransformObjectToWorld(ps);
                o.cs = TransformWorldToHClip(o.ws);
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                float2 p = i.p.xz;
                float fp = max(length(fwidth(p)), 0.02);                          // the size of this pixel on the ground
                // the relief: central differences a pixel apart (finer tiles fall to coarser mips far away)
                float e = max(fp * 0.75, 0.35);
                float3 lod = Lods(e);
                float hx = Height(p + float2(e, 0), lod) - Height(p - float2(e, 0), lod);
                float hz = Height(p + float2(0, e), lod) - Height(p - float2(0, e), lod);
                float3 n = normalize(float3(-hx / (2 * e) - p.x / _Site.z, 1, -hz / (2 * e) - p.y / _Site.z));

                Light L = GetMainLight(TransformWorldToShadowCoord(i.ws));
                float3 sd = normalize(mul((float3x3)unity_WorldToObject, L.direction));         // toward the Sun, site frame
                float3 toCam = normalize(mul((float3x3)unity_WorldToObject, GetCameraPositionWS() - i.ws));
                float mu0 = dot(n, sd), mu = max(dot(n, toCam), 0.02);

                // shadow: march from here toward the Sun through the heights (with the Moon's curve); the Sun is 0.53 deg
                // wide, so a shadow's edge blurs with distance
                float sh = mu0 > 0 ? 1 : 0;
                if (mu0 > 0 && sd.y > -0.02)
                {
                    // start on the real ground (the mesh between its vertices can sit above or below it)
                    float h0 = Height(p, Lods(max(fp, 0.5))) - Curve(p) + 0.05 + fp * 0.6;
                    // a different start per pixel turns step bands into fine grain
                    float jit = frac(52.9829189 * frac(dot(i.cs.xy, float2(0.06711056, 0.00583715))));
                    float t = max(0.25, fp * 0.8) * (1 + jit * 0.3);
                    int steps = (int)_ShadowSteps;
                    [loop] for (int k = 0; k < steps; k++)
                    {
                        float2 q = p + sd.xz * t;
                        float ray = h0 + sd.y * t;
                        float terr = Height(q, Lods(t * 0.25)) - Curve(q);
                        sh = min(sh, (ray - terr) / (0.0093 * t + 0.05));
                        if (sh < -0.5 || t > 40000) break;
                        t *= 1.3;
                    }
                    sh = saturate(sh * 0.5 + 0.5);
                }
                float lsh = L.shadowAttenuation;                                   // Eagle's (and the rocks') shadows

                // the colour: NASA's LROC map where we are, brightened where fresh craters threw out new rock
                float lat = _Site.x + p.y / _Site.z, lon = _Site.y + p.x * _Site.w / _Site.z;
                float2 muv = float2(lon / (2 * PI) + 0.5, lat / PI + 0.5);
                float3 alb = SAMPLE_TEXTURE2D_LOD(_MoonTex, sampler_MoonTex, muv, log2(max(fp / 1300.0, 1.0))).rgb;
                float2 u1 = p / (2 * _Tiles.y) + 0.5, u2 = p / (2 * _Tiles.z) + 0.5;
                float g = 0;
                if (Fade(u1, 0.08) > 0) g += SAMPLE_TEXTURE2D(_H1, sampler_linear_clamp, u1).g * Fade(u1, 0.08);
                if (Fade(u2, 0.08) > 0) g += SAMPLE_TEXTURE2D(_H2, sampler_linear_clamp, u2).g * Fade(u2, 0.08);
                alb *= 1 + g;

                float sunI = dot(L.color, float3(0.333, 0.333, 0.333));
                float ls = mu0 > 0 ? mu0 / (mu0 + mu) : 0;                         // Lommel-Seeliger, as the sky's Moon
                float phase = acos(clamp(dot(sd, toCam), -1, 1));
                float surge = 1 + 0.35 * exp(-phase / 0.05);                       // the glow around your own shadow
                float3 col = alb * ls * 1.6 * sunI * sh * lsh * surge;
                col += alb * 0.012;                                                 // light from the sunlit ground and the Earth

                // the engine's dust: a thin sheet blown straight out along the ground (no air: no cloud), streaky
                if (_Dust.w > 0.001)
                {
                    float2 d = p - _Dust.xz;
                    float r = length(d);
                    float ang = atan2(d.y, d.x);
                    // whole numbers of streaks around the circle, so the pattern joins up at +-180 deg (no seam line)
                    float streak = 0.55 + 0.45 * sin(ang * 60 + sin(ang * 13) * 3) * sin(r * 0.35 - _Time.y * 24);
                    float k = _Dust.w * exp(-r / max(_Dust.y, 1)) * streak;
                    col = lerp(col, alb.r * float3(1.05, 1.02, 0.98) * sunI * 0.55, saturate(k));
                }
                return half4(col * _Exposure * max(_Gain, 1), 1);
            }
            ENDHLSL
        }
    }
}
