// Kennedy water: the Atlantic, the Banana River and the lagoons. Where the land-type maps say water, the
// surface ripples (moving waves), reflects the sky and the tower (reflection probe, Fresnel) and glints
// in the sun. Its own colour: deep blue-green in the Atlantic, dark tea-brown in the shallow lagoons.
Shader "Apollo/KSCWater"
{
    Properties
    {
        _Sea ("Atlantic colour", Color) = (0.10, 0.36, 0.42, 1)
        _Lagoon ("Lagoon colour", Color) = (0.14, 0.17, 0.12, 1)
        _MaskA ("Water mask 2 km", 2D) = "black" {}
        _MaskB ("Water mask 8 km", 2D) = "black" {}
        _MaskC ("Water mask 40 km", 2D) = "black" {}
        _Half ("Half sizes of the three tiles (m)", Vector) = (1024, 4096, 20480, 0)
        _Centre ("Tile centre (x, z)", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+10" }
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

            TEXTURE2D(_MaskA); TEXTURE2D(_MaskB); TEXTURE2D(_MaskC); SAMPLER(sampler_MaskA);
            float _WorldFade, _CamAltitude;
            CBUFFER_START(UnityPerMaterial)
                float4 _Half; float4 _Centre;
                half4 _Sea, _Lagoon;
            CBUFFER_END

            struct A { float4 pos : POSITION; };
            struct V { float4 cs : SV_POSITION; float3 ws : TEXCOORD0; float fog : TEXCOORD1; };

            V vert(A i)
            {
                V o;
                VertexPositionInputs p = GetVertexPositionInputs(i.pos.xyz);
                o.cs = p.positionCS; o.ws = p.positionWS;
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            // slope of a few travelling waves (Gerstner-like directions and lengths, metres and seconds)
            float2 Waves(float2 p, float t, float dist)
            {
                float2 g = 0;
                const float4 W[6] = {
                    float4(0.8, 0.6, 9.0, 0.040), float4(0.2, 0.98, 5.3, 0.028), float4(-0.6, 0.8, 3.1, 0.018),
                    float4(0.95, -0.3, 1.7, 0.010), float4(-0.3, -0.95, 1.1, 0.007), float4(0.5, -0.86, 0.6, 0.004) };
                [unroll] for (int k = 0; k < 6; k++)
                {
                    float2 d = normalize(W[k].xy);
                    float f = 6.2831853 / W[k].z;
                    float c = sqrt(9.81 / f);
                    float fade = saturate(1.0 - dist / (W[k].z * 110.0));       // waves smaller than a pixel fade out (no shimmer)
                    g += d * (W[k].w * f * cos(f * (dot(d, p) - c * t))) * fade;
                }
                return g;
            }

            half4 frag(V i) : SV_Target
            {
                {
                    float dz = frac(52.9829189 * frac(dot(i.cs.xy, float2(0.06711056, 0.00583715))));
                    float reach = lerp(1e7, 6000, saturate((_CamAltitude - 1200) / 1800));        // beyond this, the round Earth of the sky
                    clip(dz - max(_WorldFade, saturate((distance(i.ws, _WorldSpaceCameraPos) - reach) / 3000)));
                }
                float2 q = i.ws.xz - _Centre.xy;
                float2 uvA = q / (2 * _Half.x) + 0.5, uvB = q / (2 * _Half.y) + 0.5, uvC = q / (2 * _Half.z) + 0.5;
                bool inA = all(abs(q) < _Half.x), inB = all(abs(q) < _Half.y);
                half mask = inA ? SAMPLE_TEXTURE2D(_MaskA, sampler_MaskA, uvA).r : inB ? SAMPLE_TEXTURE2D(_MaskB, sampler_MaskA, uvB).r : SAMPLE_TEXTURE2D(_MaskC, sampler_MaskA, uvC).r;
                clip(mask - 0.5);
                half seaW = SAMPLE_TEXTURE2D(_MaskC, sampler_MaskA, uvC).r;
                half3 body = lerp(_Lagoon.rgb, _Sea.rgb, seaW);

                float dist = distance(i.ws, _WorldSpaceCameraPos);
                float2 g = Waves(i.ws.xz, _Time.y, dist);
                float3 N = normalize(float3(-g.x, 1, -g.y));
                float3 Vd = normalize(_WorldSpaceCameraPos - i.ws);
                float3 R = reflect(-Vd, N);
                half fres = 0.02 + 0.68 * pow(1 - saturate(dot(N, Vd)), 5);
                half3 sky = GlossyEnvironmentReflection(R, i.ws, 0.06, 1) * 0.8;
                half3 grad = lerp(half3(0.72, 0.80, 0.88), half3(0.28, 0.45, 0.72), saturate(R.y * 2.5));   // clear Florida sky
                sky = dot(sky, half3(0.33, 0.33, 0.33)) < 0.04 ? grad : sky;

                Light L = GetMainLight(TransformWorldToShadowCoord(i.ws));
                L.shadowAttenuation *= CloudShadow(i.ws, L.direction);
                half3 diffuse = body * 0.85 * (L.color * L.shadowAttenuation * saturate(L.direction.y) * 0.6 + SampleSH(float3(0, 1, 0)));
                half spec = pow(saturate(dot(R, L.direction)), 600) * 6 * L.shadowAttenuation * saturate(1.0 - dist / 4000.0);
                half3 col = lerp(diffuse, sky, fres) + spec * L.color;
                return half4(MixFog(col, i.fog), 1);
            }
            ENDHLSL
        }
    }
}
