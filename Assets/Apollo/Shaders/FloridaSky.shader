// The sky over Kennedy: a clear blue morning atmosphere with the sun glow, and real volumetric cumulus
// ray-marched through 3D noise, lit by the sun (Beer-Lambert extinction, powder darkening, forward
// scattering silver lining) and fading into haze toward the horizon.
Shader "Apollo/FloridaSky"
{
    Properties
    {
        _Exposure ("Exposure", Float) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Clouds.hlsl"

            float _Exposure;

            struct A { float4 pos : POSITION; };
            struct V { float4 cs : SV_POSITION; float3 dir : TEXCOORD0; };

            V vert(A i)
            {
                V o;
                o.cs = TransformObjectToHClip(i.pos.xyz);
                o.dir = i.pos.xyz;
                return o;
            }

            half3 SkyColor(float3 d, float3 s, half3 sunCol)
            {
                float up = saturate(d.y);
                half3 zen = half3(0.13, 0.33, 0.72), hor = half3(0.68, 0.78, 0.89);
                half3 c = lerp(hor, zen, pow(up, 0.40));
                if (d.y < 0) c = lerp(hor, half3(0.48, 0.54, 0.58), saturate(-d.y * 6));
                float mu = dot(d, s);
                c += sunCol * (0.05 * pow(saturate(mu), 5) + 0.22 * pow(saturate(mu), 48));
                return c * _SkyBright;
            }

            float Jitter(float2 p) { return frac(52.9829189 * frac(dot(p, float2(0.06711056, 0.00583715)))); }
            float HG(float mu, float g) { float g2 = g * g; return (1 - g2) / pow(max(1 + g2 - 2 * g * mu, 1e-3), 1.5); }

            half4 frag(V i) : SV_Target
            {
                float3 d = normalize(i.dir);
                Light L = GetMainLight();
                float3 s = L.direction; half3 sunCol = L.color;
                half3 sky = SkyColor(d, s, sunCol);
                float mu = dot(d, s);
                half3 col = sky + sunCol * 30 * smoothstep(0.99986, 0.99995, mu) * step(0, d.y);

                if (d.y > 0.004 && _CloudParams.w > 0)
                {
                    float camAlt = _WorldSpaceCameraPos.y + _CloudAltOffset;
                    float t0 = max(0, (_CloudParams.y - camAlt) / d.y), t1 = (_CloudParams.z - camAlt) / d.y;
                    t1 = min(t1, t0 + 9000);
                    if (t0 < 70000)
                    {
                        const int STEPS = 18;
                        float dt = (t1 - t0) / STEPS;
                        float jit = Jitter(i.cs.xy);
                        float T = 1; half3 acc = 0;
                        float phase = HG(mu, 0.6) * 0.7 + HG(mu, -0.2) * 0.3;
                        const float SIG = 0.018;                          // extinction per metre at full density
                        [loop] for (int k = 0; k < STEPS; k++)
                        {
                            float t = t0 + (k + jit) * dt;
                            float3 p = _WorldSpaceCameraPos + d * t;
                            float alt = p.y + _CloudAltOffset;
                            float den = CloudShape(p.xz, alt);
                            [branch] if (den > 0.002)
                            {
                                float n = CloudDetail(p);
                                den = saturate(den * 1.5 - (1 - n) * 0.65);
                                [branch] if (den > 0.001)
                                {
                                    // light reaching this point from the sun, through the cloud
                                    float od = CloudShape(p.xz + s.xz * 90, alt + s.y * 90) * 90
                                             + CloudShape(p.xz + s.xz * 300, alt + s.y * 300) * 210
                                             + CloudShape(p.xz + s.xz * 700, alt + s.y * 700) * 400;
                                    float beer = exp(-od * SIG * 0.6);
                                    float powder = 1 - exp(-den * 6);
                                    float hf = saturate((alt - _CloudParams.y) / (_CloudParams.z - _CloudParams.y));
                                    half3 amb = lerp(half3(0.42, 0.48, 0.58), half3(0.80, 0.86, 0.95), hf) * _SkyBright;
                                    half3 lightE = sunCol * beer * lerp(0.55, 1.0, powder) * phase * 0.9 + amb;
                                    float st = exp(-den * SIG * dt);
                                    acc += T * (1 - st) * lightE;
                                    T *= st;
                                    if (T < 0.02) break;
                                }
                            }
                        }
                        float haze = 1 - exp(-t0 / 26000);                 // distant clouds sink into the haze
                        acc = lerp(acc, sky * (1 - T), haze * 0.85);
                        col = col * T + acc;
                    }
                }
                return half4(col * _Exposure, 1);
            }
            ENDHLSL
        }
    }
}
