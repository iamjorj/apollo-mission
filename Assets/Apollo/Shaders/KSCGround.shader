// Kennedy's ground, made only from real ground materials (ambientCG, CC0), no photograph:
//  - mown lawn around the pad, with faint mowing stripes
//  - open grassland elsewhere, drier and greener in large soft patches
//  - saw-palmetto scrub in big natural patches (3D shrubs stand in them)
//  - beach sand along the Atlantic, dark marsh edges along the lagoons (from the real coastline heights)
// Each material is sampled at two scales so it never visibly repeats. Lit by the sun with shadows,
// cloud shadows, sky light and fog.
Shader "Apollo/KSCGround"
{
    Properties
    {
        _D0 ("Lawn", 2D) = "grey" {}
        _D1 ("Scrub", 2D) = "grey" {}
        _D2 ("Sand", 2D) = "grey" {}
        _N0 ("Lawn normal", 2D) = "bump" {}
        _N1 ("Scrub normal", 2D) = "bump" {}
        _N2 ("Sand normal", 2D) = "bump" {}
        _Tint0 ("Lawn tint", Color) = (1, 1, 1, 1)
        _Tint1 ("Scrub tint", Color) = (1, 1, 1, 1)
        _Tint2 ("Sand tint", Color) = (1, 1, 1, 1)
        _Tint3 ("Grassland tint", Color) = (1, 1, 1, 1)
        _Macro ("Large patches (tileable noise)", 2D) = "grey" {}
        _Coast ("Coast (R beach, G marsh edge)", 2D) = "black" {}
        _CoastRect ("Coast rect (x min, z min, size)", Vector) = (-4096, -4096, 8192, 0)
        _PadLawn ("Mown lawn around the pad (radius, fade)", Vector) = (430, 140, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Clouds.hlsl"

            TEXTURE2D(_D0); TEXTURE2D(_D1); TEXTURE2D(_D2); SAMPLER(sampler_D0);
            TEXTURE2D(_N0); TEXTURE2D(_N1); TEXTURE2D(_N2); SAMPLER(sampler_N0);
            TEXTURE2D(_Macro); SAMPLER(sampler_Macro);
            TEXTURE2D(_Coast); SAMPLER(sampler_Coast);
            float _WorldFade, _CamAltitude;
            CBUFFER_START(UnityPerMaterial)
                half4 _Tint0, _Tint1, _Tint2, _Tint3;
                float4 _CoastRect, _PadLawn;
            CBUFFER_END

            struct A { float4 pos : POSITION; float3 n : NORMAL; };
            struct V { float4 cs : SV_POSITION; float3 ws : TEXCOORD0; float3 n : TEXCOORD1; float fog : TEXCOORD2; };

            V vert(A i)
            {
                V o;
                VertexPositionInputs p = GetVertexPositionInputs(i.pos.xyz);
                o.cs = p.positionCS; o.ws = p.positionWS;
                o.n = TransformObjectToWorldNormal(i.n);
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            // one material at two scales (the second turned and offset) so its pattern never lines up
            half3 Two(TEXTURE2D_PARAM(t, s), float2 uv)
            {
                half3 a = SAMPLE_TEXTURE2D(t, s, uv).rgb;
                half3 b = SAMPLE_TEXTURE2D(t, s, float2(uv.y, -uv.x) * 0.31 + 0.37).rgb;
                return a * 0.62 + b * 0.38;
            }
            half Lum(half3 c) { return dot(c, half3(0.2126, 0.7152, 0.0722)); }

            half4 frag(V i) : SV_Target
            {
                {
                    float dz = frac(52.9829189 * frac(dot(i.cs.xy, float2(0.06711056, 0.00583715))));
                    float reach = lerp(1e7, 6000, saturate((_CamAltitude - 1200) / 1800));        // beyond this, the round Earth of the sky
                    clip(dz - max(_WorldFade, saturate((distance(i.ws, _WorldSpaceCameraPos) - reach) / 3000)));
                }
                float2 p = i.ws.xz;
                float dist = distance(i.ws, _WorldSpaceCameraPos);

                // ---- where each kind of ground is ----
                half n1 = SAMPLE_TEXTURE2D(_Macro, sampler_Macro, p / 900.0).r;
                half n2 = SAMPLE_TEXTURE2D(_Macro, sampler_Macro, p / 230.0 + 0.31).g;
                half n3 = SAMPLE_TEXTURE2D(_Macro, sampler_Macro, p / 41.0 + 0.71).r;
                half scrubN = n1 * 0.55 + n2 * 0.30 + n3 * 0.15;
                half scrub = smoothstep(0.50, 0.55, scrubN);
                half2 coast = SAMPLE_TEXTURE2D(_Coast, sampler_Coast, (p - _CoastRect.xy) / _CoastRect.z).rg;
                half sand = coast.r;
                half lawn = (1 - smoothstep(_PadLawn.x, _PadLawn.x + _PadLawn.y, length(p))) * (1 - sand);
                half rest = 1 - sand - lawn;
                scrub *= rest * (1 - coast.g);
                half field = rest - scrub;

                // ---- each material's colour ----
                float2 u0 = p / 2.2, u1 = p / 3.0, u2 = p / 2.6;
                half m1 = SAMPLE_TEXTURE2D(_Macro, sampler_Macro, p / 260.0).r;
                half m2 = SAMPLE_TEXTURE2D(_Macro, sampler_Macro, p / 47.0 + 0.5).g;
                half patch = m1 * 0.65 + m2 * 0.35;
                half3 grassTex = Two(TEXTURE2D_ARGS(_D0, sampler_D0), u0);
                // lawn: the pad's mown grass, soft patches and faint mowing stripes
                half3 cLawn = grassTex * _Tint0.rgb;
                cLawn *= lerp(lerp(half3(0.86, 1.02, 0.86), half3(1.10, 1.02, 0.80), saturate(patch * 1.3 - 0.15)), 1, 0.45) * lerp(0.93, 1.05, m2);
                float2 cell = floor(p / 140.0);
                float ang = frac(sin(dot(cell, float2(12.9898, 78.233))) * 43758.5453) > 0.5 ? 0.0 : 1.5708;
                float sc = dot(p, float2(cos(ang), sin(ang))) / 4.5;
                float sw = max(fwidth(sc), 1e-4);
                float stripe = saturate(0.5 + (abs(frac(sc) - 0.5) - 0.25) / sw) * saturate(0.6 / (sw * 8));
                cLawn *= 1 + 0.025 * (stripe * 2 - 1);
                cLawn = lerp(Lum(cLawn).xxx, cLawn, 0.78);
                // grassland: the same grass, longer and patchier, drier in the open
                half3 cField = grassTex * _Tint3.rgb;
                cField *= lerp(half3(0.80, 1.05, 0.82), half3(1.18, 1.03, 0.74), saturate(patch * 1.5 - 0.25)) * lerp(0.86, 1.10, m2);
                cField = lerp(Lum(cField).xxx, cField, 0.80);
                // scrub: dense, dark, uneven
                half3 cScrub = Two(TEXTURE2D_ARGS(_D1, sampler_D0), u1) * _Tint1.rgb * lerp(0.78, 1.12, n3) * lerp(0.9, 1.06, m1);
                // sand: the beach
                half3 cSand = Two(TEXTURE2D_ARGS(_D2, sampler_D0), u2) * _Tint2.rgb * lerp(0.92, 1.04, m2);
                half3 albedo = cLawn * lawn + cField * field + cScrub * scrub + cSand * sand;
                // marsh edges along the lagoons: darker, wetter, browner
                albedo = lerp(albedo, albedo * half3(0.62, 0.62, 0.52), coast.g * 0.8 * (1 - sand));

                // ---- light ----
                half3 nT = (lawn + field) * UnpackNormal(SAMPLE_TEXTURE2D(_N0, sampler_N0, u0)) + scrub * UnpackNormal(SAMPLE_TEXTURE2D(_N1, sampler_N0, u1))
                         + sand * UnpackNormal(SAMPLE_TEXTURE2D(_N2, sampler_N0, u2));
                float3 N = normalize(i.n);
                float3 T = normalize(cross(N, float3(0, 0, 1)));
                float3 B = cross(T, N);
                float3 Nd = normalize(T * nT.x + B * nT.y + N * max(nT.z, 0.2));
                N = normalize(lerp(Nd, N, saturate(dist / 450.0)));

                Light L = GetMainLight(TransformWorldToShadowCoord(i.ws));
                half3 lit = L.color * (L.shadowAttenuation * CloudShadow(i.ws, L.direction) * saturate(dot(N, L.direction))) + SampleSH(N);
                return half4(MixFog(albedo * lit, i.fog), 1);
            }
            ENDHLSL
        }
    }
}
