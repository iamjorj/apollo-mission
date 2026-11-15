// Real 3D fair-weather cumulus that the rocket climbs up through. A ray from the eye is marched through
// the cloud layer (on a round Earth) and stops at anything solid (from the depth texture), so the cabin
// hides them and only the windows look out at them. Density: a weather map says where clouds stand, a
// Perlin-Worley shape noise gives them their billows, a Worley detail noise erodes their edges (wispy at the
// base, cauliflower on top). Light: the Sun through the cloud (Beer's law with three octaves of multiple
// scattering and a forward-scattering phase, so edges toward the Sun glow), sky light from above and the
// ground's from below, and haze with distance. Drawn on a sphere around the camera (a child of it),
// premultiplied: colour + transmittance.
Shader "Apollo/VolumeClouds"
{
    Properties
    {
        _CloudShape ("Shape noise (3D)", 3D) = "" {}
        _CloudDetailTex ("Detail noise (3D)", 3D) = "" {}
        _CloudWeather ("Weather map", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "Queue" = "Transparent-20" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Clouds"
            Tags { "LightMode" = "UniversalForward" }
            Blend One SrcAlpha
            ZWrite Off ZTest Always Cull Front
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE3D(_CloudShape); SAMPLER(sampler_CloudShape);
            TEXTURE3D(_CloudDetailTex);
            TEXTURE2D(_CloudWeather); SAMPLER(sampler_CloudWeather);
            // globals (FlightDirector)
            float4 _VC_Layer;      // base, top (m above sea level), coverage 0..1, extinction per metre
            float4 _VC_Scale;      // shape tile, detail tile, weather tile (m), edge erosion
            float4 _VC_Origin;     // x, z: where world (0,0) is over the cloud field; y: sea-level altitude of world y = 0; w: visibility 0..1
            float4 _VC_Wind;       // xy drift (m); z: how far the air in the clouds has risen (m); w: the small eddies' extra rise (m, within one detail tile)
            float4 _VC_Light;      // sun strength, sky-light strength, haze density (1/m), max distance (m)
            float4 _VC_Stamp;      // a cumulus standing on the climb: x, z (cloud-field metres), radius, strength
            float _SkyBoost;       // daylight through the windows seen from the dim cabin
            float _PixelAngle;     // radians per screen pixel (FlightDirector)

            struct A { float4 pos : POSITION; };
            struct V { float4 cs : SV_POSITION; float3 ws : TEXCOORD0; float4 sp : TEXCOORD1; };

            V vert(A i)
            {
                V o;
                o.cs = TransformObjectToHClip(i.pos.xyz);
                o.ws = TransformObjectToWorld(i.pos.xyz);
                o.sp = ComputeScreenPos(o.cs);
                return o;
            }

            static const float Re = 6371000.0;

            float Remap(float v, float a, float b, float c, float d) { return c + (v - a) / (b - a) * (d - c); }

            // near and far distances where a ray from o (earth-centred) crosses a sphere of radius r
            float2 Sphere(float3 o, float3 d, float r)
            {
                float b = dot(o, d), c = dot(o, o) - r * r, h = b * b - c;
                if (h < 0) return float2(-1, -1);                    // a miss: never read as a hit ahead
                h = sqrt(h);
                return float2(-b - h, -b + h);
            }

            // cloud density at p = (x, altitude, z) in metres over the cloud field; h = height in the layer 0..1.
            // The field drifts with the wind (_VC_Wind.xy). Inside the clouds the air rises: the billows climb through
            // the cloud body with the updraft (_VC_Wind.z) and the small eddies faster (_VC_Wind.w more), while the
            // flat base and the rounded envelope stay, so the tops boil and the ragged base keeps changing.
            // lodD: the detail noise's mip for one pixel's footprint.
            float Density(float3 p, float h, bool cheap, float lod, float lodD)
            {
                float2 pw = p.xz + _VC_Wind.xy;
                pw = float2(pw.x * 0.921 - pw.y * 0.389, pw.x * 0.389 + pw.y * 0.921);    // turned 23 degrees: no rows along the axes
                float4 w = SAMPLE_TEXTURE2D_LOD(_CloudWeather, sampler_CloudWeather, pw / _VC_Scale.z, 0);
                float w2 = SAMPLE_TEXTURE2D_LOD(_CloudWeather, sampler_CloudWeather, pw / 13700.0 + 0.61, 0).g;
                float cov = saturate(w.r * _VC_Layer.z * 1.6 * lerp(0.55, 1.3, w2));     // clusters and clear patches, 0.3-0.5 on average
                float st = _VC_Stamp.w * smoothstep(0, 0.55, 1 - length(p.xz - _VC_Stamp.xy) / max(_VC_Stamp.z, 1));
                cov = max(cov, 0.85 * st);                                                // the cumulus the rocket climbs through
                if (cov <= 0.01) return 0;
                float topH = lerp(lerp(0.55, 1.0, w.g), 0.85, st);                     // some towers grow taller
                float hg = smoothstep(0.0, 0.05, h) * (1 - smoothstep(topH * 0.40, topH, h));   // flat base, rounded top
                float2 qs = p.xz + _VC_Wind.xy;
                qs += (SAMPLE_TEXTURE2D_LOD(_CloudWeather, sampler_CloudWeather, qs / 9100.0 + 0.37, 0).gr - 0.5) * 700.0;    // a slight swirl: the noise never repeats exactly
                float y = p.y - _VC_Wind.z;                                               // risen with the updraft
                float3 q = float3(qs.x * 0.622 - qs.y * 0.783, y, qs.x * 0.783 + qs.y * 0.622);   // turned 51 degrees
                float4 n = SAMPLE_TEXTURE3D_LOD(_CloudShape, sampler_CloudShape, q / _VC_Scale.x, lod);
                float3 q2 = float3(qs.x * -0.391 - qs.y * 0.920, y * 0.83 + 517, qs.x * 0.920 - qs.y * 0.391);   // a second, larger, turned copy
                float4 m2 = SAMPLE_TEXTURE3D_LOD(_CloudShape, sampler_CloudShape, q2 / (_VC_Scale.x * 2.37), lod);
                n = lerp(n, m2, 0.38);                                                    // together they never repeat in sight
                float fbm = n.g * 0.625 + n.b * 0.25 + n.a * 0.125;
                float shape = saturate(Remap(n.r, fbm - 1, 1, 0, 1)) * hg;
                float c = saturate(Remap(shape, 1 - cov, 1, 0, 1)) * cov;               // small values: room for the edges to erode
                if (cheap || c <= 0) return c * 4;
                float3 dn = SAMPLE_TEXTURE3D_LOD(_CloudDetailTex, sampler_CloudShape, (q - float3(0, _VC_Wind.w, 0)) / _VC_Scale.y, lodD).rgb;   // the small eddies rise faster
                float df = dn.r * 0.625 + dn.g * 0.25 + dn.b * 0.125;
                float m = lerp(df, 1 - df, saturate(h * 4));                              // wisps below, billows above
                c = saturate(Remap(c, m * _VC_Scale.w, 1, 0, 1));
                return c * 4 * lerp(0.6, 1.0, saturate(h * 3));                         // denser in the core
            }

            float HG(float mu, float g) { float g2 = g * g; return (1 - g2) / pow(max(1 + g2 - 2 * g * mu, 1e-4), 1.5); }   // x 4 pi: isotropic = 1

            half4 frag(V i) : SV_Target
            {
                float vis = _VC_Origin.w;
                if (vis <= 0.001) return half4(0, 0, 0, 1);
                float2 uv = i.sp.xy / i.sp.w;
                float3 ro = _WorldSpaceCameraPos;
                float3 rd = normalize(i.ws - ro);
                float raw = SampleSceneDepth(uv);
            #if UNITY_REVERSED_Z
                bool sky = raw <= 1e-7;
            #else
                bool sky = raw >= 0.9999999;
            #endif
                float3 fwd = -UNITY_MATRIX_V[2].xyz;
                float tScene = sky ? 1e20 : LinearEyeDepth(raw, _ZBufferParams) / max(dot(rd, fwd), 1e-4);

                float base = _VC_Layer.x, top = _VC_Layer.y;
                float camAlt = ro.y + _VC_Origin.y;
                float3 O = float3(0, Re + camAlt, 0);
                float2 sb = Sphere(O, rd, Re + base), st = Sphere(O, rd, Re + top), se = Sphere(O, rd, Re);
                float tIn, tOut;
                if (camAlt < base)
                {
                    if (se.x > 0) return half4(0, 0, 0, 1);                               // looking at the ground
                    tIn = sb.y; tOut = st.y;
                }
                else if (camAlt <= top)
                {
                    tIn = 0; tOut = st.y;
                    if (sb.x > 0) tOut = min(tOut, sb.x);
                }
                else
                {
                    if (st.x > st.y || st.x < 0) return half4(0, 0, 0, 1);
                    tIn = st.x; tOut = sb.x > 0 ? sb.x : st.y;
                }
                tIn = max(tIn, 4.0);                                                      // never inside the capsule
                tOut = min(min(tOut, tScene), tIn + _VC_Light.w);
                if (tOut <= tIn) return half4(0, 0, 0, 1);

                Light Lm = GetMainLight();
                float3 L = Lm.direction;
                float mu = dot(rd, L);
                half3 ambTop = SampleSH(float3(0, 1, 0)) * _VC_Light.y, ambBot = SampleSH(float3(0, -1, 0)) * _VC_Light.y * 0.7;
                float3 sunC = Lm.color * _VC_Light.x;

                // The march: through clear air in search steps as before (1/80 of the way, at least 18 m); through cloud
                // in short steps (10 m near, 1 % of the distance far, never longer than a search step), so the inside of
                // a cloud, its edges and the white-out around the eye are integrated finely, not as one 18-440 m slab per
                // pixel. Each sample stands for the step that led to it (a thin wisp found by a long search step keeps
                // its true weight); leaving a cloud, the steps grow back to the search step by doubling.
                const int STEPS = 96;
                float len = tOut - tIn;
                float dt = max(len / 80, 18.0);
                float jit = frac(52.9829189 * frac(dot(i.cs.xy, float2(0.06711056, 0.00583715))));
                float seg = min(dt, max(10.0, tIn * 0.01));                               // the length the next sample stands for
                float t = tIn + seg * jit;
                float T = 1;
                float3 S = 0;
                float wSum = 0, dSum = 0;
                float2 xz0 = ro.xz + _VC_Origin.xz;
                float lodK = _PixelAngle * 32.0 / max(_VC_Scale.y, 1);                     // detail texels (32 per tile) per pixel, per metre of distance
                float tLast = tIn;                                                        // where the march was last inside a cloud (or began)
                [loop] for (int k = 0; k < STEPS; k++)
                {
                    if (t >= tOut) break;
                    float ds = min(dt, max(10.0, t * 0.01));                              // a step through cloud
                    float3 pe = O + rd * t;                                                 // earth-centred
                    float alt = length(pe) - Re;
                    float h = saturate((alt - base) / (top - base));
                    float2 xz = xz0 + rd.xz * t;
                    float lod = saturate(t / 40000.0) * 1.5;
                    float3 p = float3(xz.x, alt, xz.y);
                    float d = Density(p, h, false, lod, log2(max(t * lodK, 1.0)));
                    if (d > 0.002)
                    {
                        // light from the Sun: optical depth toward it through the cloud
                        float od = 0, ls = 45;
                        float3 lp = p;
                        [unroll] for (int j = 0; j < 5; j++)
                        {
                            lp += L * ls;
                            float lh = saturate((lp.y - base) / (top - base));
                            od += Density(lp, lh, true, lod + 1, 0) * ls;
                            ls *= 1.8;
                        }
                        float sig = _VC_Layer.w;
                        float sun = 0, a = 1, b = 1, c = 1;
                        [unroll] for (int o = 0; o < 3; o++)
                        {
                            sun += b * exp(-od * sig * a) * lerp(HG(mu, 0.75 * c), HG(mu, -0.25 * c), 0.35);
                            a *= 0.25; b *= 0.5; c *= 0.5;
                        }
                        float3 Lsc = sunC * sun + lerp(ambBot, ambTop, h);
                        float tr = exp(-d * sig * seg);
                        S += T * Lsc * (1 - tr);
                        float w = T * (1 - tr);
                        dSum += w * t; wSum += w;
                        T *= tr;
                        if (T < 0.01) break;
                        tLast = t; seg = ds;
                        t += ds;
                    }
                    else
                    {
                        seg = min(dt * 1.3, max(ds, t - tLast));                            // clear air: 1, 1, 2, 4 ... short steps after a cloud, up to the search step
                        t += seg;
                    }
                }
                // haze between the eye and the clouds (thinner higher up)
                float dm = wSum > 0 ? dSum / wSum : tIn;
                float fogK = 1 - exp(-dm * _VC_Light.z * exp(-max(camAlt, 0) / 2500.0));
                S = lerp(S, unity_FogColor.rgb * (1 - T), saturate(fogK));
                return half4(S * vis * max(_SkyBoost, 1), lerp(1, T, vis));
            }
            ENDHLSL
        }
    }
}
