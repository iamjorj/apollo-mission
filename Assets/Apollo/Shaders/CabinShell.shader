// The Command Module's structure behind the scanned cabin walls. The Smithsonian scan has thin gaps between
// its panels; behind them a real cabin has wiring and structure in shadow, not open sky. This shell sits
// just outside the scan and is drawn dark everywhere, except in the directions where the commander's eye
// looks out through a real window (a mask measured by casting rays from the eye through the scan).
// The mesh is a child of the eye, so object space is eye space and the eye is at the origin.
Shader "Apollo/CabinShell"
{
    Properties
    {
        _Mask ("Window directions from the eye (equirectangular)", 2D) = "black" {}
        _Color ("Colour in the gaps", Color) = (0.035, 0.034, 0.032, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_Mask); SAMPLER(sampler_Mask);
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END

            struct A { float4 pos : POSITION; };
            struct V { float4 cs : SV_POSITION; float3 os : TEXCOORD0; };

            V vert(A i)
            {
                V o;
                o.cs = TransformObjectToHClip(i.pos.xyz);
                o.os = i.pos.xyz;
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                float3 d = normalize(i.os);
                float2 uv = float2(atan2(d.x, d.z) / (2 * PI) + 0.5, asin(clamp(d.y, -1, 1)) / PI + 0.5);
                clip(0.5 - SAMPLE_TEXTURE2D_LOD(_Mask, sampler_Mask, uv, 0).r);          // a real window: see out
                float n = frac(sin(dot(floor(i.os * 40), float3(12.9898, 78.233, 37.719))) * 43758.5453);
                return half4(_Color.rgb * (0.85 + 0.3 * n), 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            TEXTURE2D(_Mask); SAMPLER(sampler_Mask);
            float3 _LightDirection;
            struct A { float4 pos : POSITION; float3 n : NORMAL; };
            struct V { float4 cs : SV_POSITION; float3 os : TEXCOORD0; };
            V vert(A i)
            {
                V o;
                float3 ws = TransformObjectToWorld(i.pos.xyz), nw = TransformObjectToWorldNormal(i.n);
                o.cs = TransformWorldToHClip(ApplyShadowBias(ws, nw, _LightDirection));
            #if UNITY_REVERSED_Z
                o.cs.z = min(o.cs.z, o.cs.w * UNITY_NEAR_CLIP_VALUE);
            #else
                o.cs.z = max(o.cs.z, o.cs.w * UNITY_NEAR_CLIP_VALUE);
            #endif
                o.os = i.pos.xyz;
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                float3 d = normalize(i.os);
                float2 uv = float2(atan2(d.x, d.z) / (2 * PI) + 0.5, asin(clamp(d.y, -1, 1)) / PI + 0.5);
                clip(0.5 - SAMPLE_TEXTURE2D_LOD(_Mask, sampler_Mask, uv, 0).r);          // sunlight comes in only through the windows
                return 0;
            }
            ENDHLSL
        }
    }
}
