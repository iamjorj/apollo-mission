// Pad 39A's mound in 1969: the hardstand and the faces of the mound in light poured concrete (20 ft slabs,
// thin joints, each slab a slightly different tone), weathered in large soft patches and blackened by
// exhaust around the flame trench; the crawlerway's two lanes of pale river gravel with their grass median
// running up the incline; and where the mound meets the flat ground, the same lawn as the terrain.
// World space is the pad frame (origin on the vehicle axis, x east, z north, y 0 at the pad surface).
Shader "Apollo/PadConcrete"
{
    Properties
    {
        _Detail ("Concrete detail", 2D) = "grey" {}
        _DetailN ("Concrete normal", 2D) = "bump" {}
        _Gravel ("River rock", 2D) = "grey" {}
        _GravelN ("River rock normal", 2D) = "bump" {}
        _Grass ("Lawn", 2D) = "grey" {}
        _GrassN ("Lawn normal", 2D) = "bump" {}
        _GrassTint ("Lawn tint (the terrain's)", Color) = (1, 1, 1, 1)
        _GradeY ("Flat ground level (world y)", Float) = -12.5
        _Macro ("Large patches (tileable noise)", 2D) = "grey" {}
        _Concrete ("Concrete colour", Color) = (0.64, 0.63, 0.60, 1)
        _Rock ("River gravel colour", Color) = (0.75, 0.70, 0.60, 1)
        _Slab ("Slab size (m)", Float) = 6.1
        _Trench ("Trench (half width, south end, north end)", Vector) = (9, -70, 84, 0)
        _Lanes ("Crawlerway lanes (inner x, outer x, north end z)", Vector) = (7.6, 19.8, -20, 0)
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

            TEXTURE2D(_Detail); SAMPLER(sampler_Detail);
            TEXTURE2D(_DetailN);
            TEXTURE2D(_Gravel); TEXTURE2D(_GravelN);
            TEXTURE2D(_Grass); TEXTURE2D(_GrassN);
            TEXTURE2D(_Macro); SAMPLER(sampler_Macro);
            float _WorldFade, _CamAltitude;
            CBUFFER_START(UnityPerMaterial)
                half4 _Concrete, _Rock, _GrassTint;
                float _Slab, _GradeY;
                float4 _Trench, _Lanes;
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

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            half4 frag(V i) : SV_Target
            {
                {
                    float dz = frac(52.9829189 * frac(dot(i.cs.xy, float2(0.06711056, 0.00583715))));
                    float reach = lerp(1e7, 6000, saturate((_CamAltitude - 1200) / 1800));        // beyond this, the round Earth of the sky
                    clip(dz - max(_WorldFade, saturate((distance(i.ws, _WorldSpaceCameraPos) - reach) / 3000)));
                }
                float2 p = i.ws.xz;

                // ---- concrete: slabs, joints, tone, weathering ----
                float2 q = p / _Slab, cell = floor(q), f = frac(q);
                float2 fw = max(fwidth(q), 1e-5);
                const float jw = 0.012 / 6.1;                                     // 12 mm joints
                float2 jl = saturate(1 - (min(f, 1 - f) - jw) / fw);
                float joint = max(jl.x, jl.y) * saturate(3 * jw / max(fw.x, fw.y));   // fades out once smaller than a pixel
                float tone = 0.94 + 0.10 * Hash(cell);
                half3 det = SAMPLE_TEXTURE2D(_Detail, sampler_Detail, p / 2.3).rgb * 0.6 + SAMPLE_TEXTURE2D(_Detail, sampler_Detail, float2(p.y, -p.x) / 5.7 + 0.37).rgb * 0.4;
                half dl = dot(det, half3(0.333, 0.333, 0.333)) / 0.5;
                half m1 = SAMPLE_TEXTURE2D(_Macro, sampler_Macro, p / 90.0).r, m2 = SAMPLE_TEXTURE2D(_Macro, sampler_Macro, p / 23.0 + 0.3).g;
                half weather = lerp(0.86, 1.04, saturate(m1 * 0.7 + m2 * 0.3));
                half3 con = _Concrete.rgb * tone * lerp(1, dl, 0.35) * weather * (1 - 0.32 * joint);

                // exhaust soot: around the trench, and in a plume down the north slope
                float tx = max(abs(p.x) - _Trench.x, 0), tz = max(max(_Trench.y - p.y, p.y - _Trench.z), 0);
                float dt = sqrt(tx * tx + tz * tz);
                float soot = exp(-dt / 6.0) * 0.55 + exp(-abs(p.x) / 22.0) * saturate((p.y - 20.0) / 30.0) * 0.25;
                soot *= lerp(0.7, 1.15, m2);
                con *= 1 - saturate(soot);

                // ---- the crawlerway: two lanes of river gravel, darker where the crawler's tracks run ----
                float ax = abs(p.x);
                float lane = smoothstep(_Lanes.x - 0.25, _Lanes.x + 0.25, ax) * (1 - smoothstep(_Lanes.y - 0.25, _Lanes.y + 0.25, ax));
                lane *= 1 - smoothstep(_Lanes.z - 1.0, _Lanes.z + 1.0, p.y);
                half3 rock = SAMPLE_TEXTURE2D(_Gravel, sampler_Detail, p / 1.6).rgb * 0.6 + SAMPLE_TEXTURE2D(_Gravel, sampler_Detail, float2(p.y, -p.x) / 3.9 + 0.21).rgb * 0.4;
                half rl = dot(rock, half3(0.333, 0.333, 0.333)) / 0.45;
                float lc = (_Lanes.x + _Lanes.y) * 0.5, tr = abs(ax - lc);
                float track = smoothstep(1.4, 1.0, abs(tr - 3.0));                 // two tread paths per lane
                half3 rk = _Rock.rgb * lerp(1, rl, 0.55) * (1 - 0.12 * track) * lerp(0.92, 1.04, m1);
                half3 albedo = lerp(con, rk, lane);

                // ---- lawn: the incline's median, and the flat ground where the mound ends ----
                float median = (1 - smoothstep(_Lanes.x - 0.6, _Lanes.x - 0.1, ax)) * smoothstep(_Lanes.w + 0.5, _Lanes.w - 0.5, p.y);
                float flat = 1 - smoothstep(_GradeY + 0.06, _GradeY + 0.40, i.ws.y);
                float lawn = saturate(max(median, flat));
                half3 gr = (SAMPLE_TEXTURE2D(_Grass, sampler_Detail, p / 2.2).rgb * 0.62 + SAMPLE_TEXTURE2D(_Grass, sampler_Detail, float2(p.y, -p.x) / 2.2 * 0.31 + 0.37).rgb * 0.38) * _GrassTint.rgb;
                half m1g = SAMPLE_TEXTURE2D(_Macro, sampler_Macro, p / 260.0).r, m2g = SAMPLE_TEXTURE2D(_Macro, sampler_Macro, p / 47.0 + 0.5).g;
                half patch = m1g * 0.65 + m2g * 0.35;                              // the terrain's own lawn patches
                gr *= lerp(lerp(half3(0.86, 1.02, 0.86), half3(1.10, 1.02, 0.80), saturate(patch * 1.3 - 0.15)), 1, 0.45) * lerp(0.93, 1.05, m2g);
                float2 mcell = floor(p / 140.0);                                   // the terrain's mowing stripes
                float mang = frac(sin(dot(mcell, float2(12.9898, 78.233))) * 43758.5453) > 0.5 ? 0.0 : 1.5708;
                float msc = dot(p, float2(cos(mang), sin(mang))) / 4.5;
                float msw = max(fwidth(msc), 1e-4);
                gr *= 1 + 0.025 * (saturate(0.5 + (abs(frac(msc) - 0.5) - 0.25) / msw) * saturate(0.6 / (msw * 8)) * 2 - 1);
                gr = lerp(dot(gr, half3(0.2126, 0.7152, 0.0722)).xxx, gr, 0.78);
                albedo = lerp(albedo, gr, lawn);

                // ---- light ----
                half3 nT = lerp(lerp(UnpackNormal(SAMPLE_TEXTURE2D(_DetailN, sampler_Detail, p / 2.3)), UnpackNormal(SAMPLE_TEXTURE2D(_GravelN, sampler_Detail, p / 1.6)), lane), UnpackNormal(SAMPLE_TEXTURE2D(_GrassN, sampler_Detail, p / 2.2)), lawn);
                float3 N = normalize(i.n);
                float3 T = normalize(cross(N, float3(0, 0, 1)));
                float3 B = cross(T, N);
                float dist = distance(i.ws, _WorldSpaceCameraPos);
                float3 Nd = normalize(T * nT.x * 0.6 + B * nT.y * 0.6 + N * max(nT.z, 0.2));
                N = normalize(lerp(Nd, N, saturate(dist / 300.0)));
                Light L = GetMainLight(TransformWorldToShadowCoord(i.ws));
                half3 lit = L.color * (L.shadowAttenuation * CloudShadow(i.ws, L.direction) * saturate(dot(N, L.direction))) + SampleSH(N);
                return half4(MixFog(albedo * lit, i.fog), 1);
            }
            ENDHLSL
        }
    }
}
