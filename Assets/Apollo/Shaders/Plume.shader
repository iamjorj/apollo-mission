// A rocket engine's exhaust plume: a flared shell drawn with additive light. Hot and white-yellow at the
// nozzles, orange further down, flickering; it widens and thins out as the air gets thin. _Heat 0..1, _Tint
// (the first stage's kerosene flame is orange; the hydrogen upper stages burn almost invisibly pale blue).
Shader "Apollo/Plume"
{
    Properties
    {
        _Noise ("Noise", 2D) = "grey" {}
        _Tint ("Colour", Color) = (1, 0.55, 0.18, 1)
        _Heat ("Strength", Float) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off Cull Off
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_Noise); SAMPLER(sampler_Noise);
            CBUFFER_START(UnityPerMaterial)
                half4 _Tint; float _Heat;
            CBUFFER_END
            struct A { float4 pos : POSITION; float3 n : NORMAL; float2 uv : TEXCOORD0; };
            struct V { float4 cs : SV_POSITION; float2 uv : TEXCOORD0; float3 n : TEXCOORD1; float3 ws : TEXCOORD2; };
            V vert(A i) { V o; o.cs = TransformObjectToHClip(i.pos.xyz); o.uv = i.uv; o.n = TransformObjectToWorldNormal(i.n); o.ws = TransformObjectToWorld(i.pos.xyz); return o; }
            half4 frag(V i) : SV_Target
            {
                float3 v = normalize(_WorldSpaceCameraPos - i.ws);
                float edge = pow(saturate(abs(dot(normalize(i.n), v))), 1.4);                 // thick through the middle
                float t = _Time.y;
                float n1 = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, float2(i.uv.x * 2, i.uv.y * 1.5 - t * 3.1)).r;
                float n2 = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, float2(i.uv.x * 5 + 0.3, i.uv.y * 4 - t * 7.3)).g;
                float flick = 0.65 + 0.7 * (n1 * 0.6 + n2 * 0.4);
                float along = i.uv.y;                                                              // 0 at the nozzles
                float fall = exp(-along * 3.2) * smoothstep(0, 0.03, along);
                half3 hot = lerp(half3(1, 0.95, 0.8), _Tint.rgb, saturate(along * 3));
                return half4(hot * fall * edge * flick * _Heat * 4, 1);
            }
            ENDHLSL
        }
    }
}
