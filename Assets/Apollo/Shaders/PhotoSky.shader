// A real photographed sky (Poly Haven "Kloofendal 48d Partly Cloudy", CC0, 8K HDR): real cumulus and real
// light. It is turned so the photo's sunlit side faces our Sun; the photo's own Sun (48 degrees high) is
// removed and ours is drawn where it really is at the planned launch time.
// Above the photo's own clouds the sky becomes a physical atmosphere (single scattering by air and haze on
// a round Earth, seen from the camera's real altitude): deep blue overhead, darkening toward black with
// height, a bright band at the horizon and the Earth's surface below it. _PhotoMix crossfades the two
// (1 = the photo, on the pad; 0 = the atmosphere, once above the clouds).
Shader "Apollo/PhotoSky"
{
    Properties
    {
        _Sky ("Sky photo (equirectangular HDR)", 2D) = "grey" {}
        _Exposure ("Exposure", Float) = 0.55
        _Rotation ("Rotation (u offset)", Float) = 0
        _Cap ("Brightest sky allowed (removes the photo's sun)", Float) = 2.6
        _Haze ("Horizon haze colour", Color) = (0.86, 0.90, 0.95, 1)
        _PhotoMix ("Photo (1) or physical atmosphere (0)", Range(0, 1)) = 1
        _AtmoSun ("Atmosphere: sunlight strength", Float) = 9
        _Ground ("Earth's surface far below (ocean)", Color) = (0.03, 0.08, 0.12, 1)
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

            TEXTURE2D(_Sky); SAMPLER(sampler_Sky);
            float _Exposure, _Rotation, _Cap, _PhotoMix, _AtmoSun;
            half4 _Haze, _Ground;
            float _CamAltitude;                                               // global: metres above sea level

            static const float Re = 6371e3, Ra = 6471e3, HR = 8000, HM = 1200;
            static const float3 BR = float3(5.802e-6, 13.558e-6, 33.1e-6);
            static const float BM = 3.996e-6, EM = 4.44e-6;
            float2 Sph(float3 o, float3 d, float r)
            {
                float b = dot(o, d), c = dot(o, o) - r * r, h = b * b - c;
                if (h < 0) return float2(-1, -1);                    // a miss: never read as a hit ahead
                h = sqrt(h); return float2(-b - h, -b + h);
            }
            // single scattering along the view ray (16 steps, 5 toward the Sun each)
            float3 Atmosphere(float3 d, float3 sd, float3 sunC)
            {
                float3 O = float3(0, Re + max(_CamAltitude, 2), 0);
                float2 ta = Sph(O, d, Ra), tg = Sph(O, d, Re);
                if (ta.y < 0) return 0;
                float t0 = max(ta.x, 0), t1 = (tg.x > 0) ? tg.x : ta.y;
                float ds = (t1 - t0) / 16;
                float3 sR = 0, sM = 0; float oR = 0, oM = 0;
                [loop] for (int k = 0; k < 16; k++)
                {
                    float3 p = O + d * (t0 + (k + 0.5) * ds);
                    float h = length(p) - Re;
                    float hr = exp(-h / HR) * ds, hm = exp(-h / HM) * ds;
                    oR += hr; oM += hm;
                    if (Sph(p, sd, Re).x > 0) continue;                              // in the Earth's shadow
                    float tl = Sph(p, sd, Ra).y, dl = tl / 5, lR = 0, lM = 0;
                    [unroll] for (int j = 0; j < 5; j++) { float hl = length(p + sd * (j + 0.5) * dl) - Re; lR += exp(-hl / HR) * dl; lM += exp(-hl / HM) * dl; }
                    float3 att = exp(-(BR * (oR + lR) + EM * (oM + lM)));
                    sR += att * hr; sM += att * hm;
                }
                float mu = dot(d, sd), g = 0.76;
                float pR = 3.0 / (16.0 * PI) * (1 + mu * mu);
                float pM = 3.0 / (8.0 * PI) * ((1 - g * g) * (1 + mu * mu)) / ((2 + g * g) * pow(max(1 + g * g - 2 * g * mu, 1e-4), 1.5));
                float3 c = sunC * _AtmoSun * (sR * BR * pR + sM * BM * pM);
                if (tg.x > 0)                                                           // the Earth's surface, seen through the air
                {
                    float3 gp = O + d * tg.x, n = normalize(gp);
                    float3 tv = exp(-(BR * oR + EM * oM));
                    c += _Ground.rgb * sunC * saturate(dot(n, sd)) * tv * 0.6;
                }
                return c;
            }

            struct A { float4 pos : POSITION; };
            struct V { float4 cs : SV_POSITION; float3 dir : TEXCOORD0; };

            V vert(A i)
            {
                V o;
                o.cs = TransformObjectToHClip(i.pos.xyz);
                o.dir = i.pos.xyz;
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float u = frac(atan2(d.x, d.z) / (2 * PI) + _Rotation);
                float v = 0.5 + asin(clamp(d.y, -1, 1)) / PI;
                half3 c = SAMPLE_TEXTURE2D_LOD(_Sky, sampler_Sky, float2(u, v), 0).rgb;
                half lum = dot(c, half3(0.2126, 0.7152, 0.0722));
                if (lum > _Cap) c *= _Cap / lum;                            // the photo's own sun is removed
                c *= _Exposure;
                half3 hz = _Haze.rgb * _Exposure;

                // humid sea-level air: a little less saturated than the highveld photo, brighter toward the horizon
                float w = pow(1 - saturate(d.y), 6);
                half lm = dot(c, half3(0.2126, 0.7152, 0.0722));
                c = lerp(lm.xxx, c, 0.85);
                c += hz * (0.04 + 0.26 * w);

                // one soft fade into the haze at the horizon, which dips below level as the camera climbs
                float dip = sqrt(2.0 * max(_WorldSpaceCameraPos.y + 14.0, 0.0) / 6.371e6);
                float y = d.y + dip;
                float t = 1 - smoothstep(-0.004, 0.10, y);
                c = lerp(c, hz, t * t);

                Light L = GetMainLight();
                if (_PhotoMix < 0.999)
                {
                    float sunI = dot(L.color, float3(0.333, 0.333, 0.333));
                    half3 atm = Atmosphere(d, L.direction, sunI.xxx) * _Exposure;
                    float below = (Sph(float3(0, Re + max(_CamAltitude, 2), 0), d, Re).x > 0 ? 1.0 : 0.0) * (1 - smoothstep(4000, 30000, _CamAltitude));
                    atm = lerp(atm, hz, below);                                          // low down, the far ground is lost in haze
                    half al = dot(atm, half3(0.2126, 0.7152, 0.0722));
                    atm = lerp(al.xxx, atm, 0.85);                                        // a little multiple scattering: less tinted
                    atm *= half3(0.96, 0.96, 1.06);
                    float tw = 1 - smoothstep(-0.004, 0.28, y);
                    atm = lerp(atm, hz, saturate(tw * tw * 1.2) * (1 - smoothstep(3000, 20000, _CamAltitude)));   // and the low sky is hazy, as in the photo
                    c = lerp(atm, c, _PhotoMix);
                }

                // our Sun, the real size (0.53 degrees), with a glow
                float mu = dot(d, L.direction);
                c += L.color * (40 * smoothstep(0.999984, 0.999990, mu) + 0.35 * pow(saturate(mu), 900) + 0.06 * pow(saturate(mu), 40)) * step(0, y);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
