// The whole sky from the pad to orbit, seen from the vehicle's real altitude and position:
//  - air: single scattering by air and haze on a round Earth (deep blue overhead, hazy horizon, black above)
//  - the Earth below the horizon: NASA Blue Marble by day, Black Marble city lights by night, the real cloud
//    cover, a sun glint on the oceans, all seen through the atmosphere (so the limb glows blue)
//  - space: NASA's star map and Milky Way (Deep Star Maps 2020) placed for the real time and position,
//    the Sun, the Moon (lit by the Sun, so its phase is right) and the bright planets
// Local frame: x east, y up, z north at the point below the vehicle. Set by FlightDirector / Ascent.
Shader "Apollo/SpaceSky"
{
    Properties
    {
        _Exposure ("Exposure", Float) = 0.55
        _AtmoSun ("Sunlight strength in the air", Float) = 9
        _Haze ("Horizon haze colour", Color) = (0.86, 0.90, 0.95, 1)
        _Day ("Earth by day (Blue Marble)", 2D) = "black" {}
        _Night ("Earth by night (Black Marble)", 2D) = "black" {}
        _EarthClouds ("Earth's cloud cover", 2D) = "black" {}
        _Stars ("Star map (equirectangular, celestial)", 2D) = "black" {}
        _Milky ("Milky Way", 2D) = "black" {}
        _MoonTex ("Moon", 2D) = "grey" {}
        _MoonNormal ("Moon relief (LOLA heights as east / north / up normals)", 2D) = "bump" {}
        _MoonTexel ("Moon map texel (km)", Float) = 2.67
        _DetailNoise ("Cloud detail (3D noise)", 3D) = "" {}
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
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_Day); SAMPLER(sampler_Day);
            TEXTURE2D(_Night); TEXTURE2D(_EarthClouds);
            TEXTURE2D(_Stars); SAMPLER(sampler_Stars);
            TEXTURE2D(_Milky);
            TEXTURE2D(_MoonTex); SAMPLER(sampler_MoonTex);
            TEXTURE2D(_MoonNormal); SAMPLER(sampler_MoonNormal);
            TEXTURE3D(_DetailNoise); SAMPLER(sampler_DetailNoise);
            float _Exposure, _AtmoSun, _MoonTexel;
            half4 _Haze;
            // globals
            float _CamAltitude;                    // metres above sea level
            float4 _EarthE, _EarthN, _EarthU;      // the local east / north / up axes in Earth-fixed coordinates
            float4x4 _LocalToStars;                // local direction -> star-map frame (x RA 0h, y north pole, z RA 6h)
            float4 _MoonDir;                       // xyz local direction, w angular radius (rad)
            float4 _Planets[5];                    // xyz local direction, w brightness
            float4 _PlanetColours[5];
            float _Flash;                          // the first stage's plume lighting the windows at staging
            float _SkyBoost;                       // daylight through the windows seen from the dim cabin
            float _PixelAngle;                     // radians per screen pixel (FlightDirector)
            float4 _MoonPos;                       // the Moon's centre from the eye (km, local frame), w its radius (km)
            float4 _MoonMeridian, _MoonEast, _MoonPole;   // the Moon's own axes (IAU) in the local frame
            float _SunRaw, _SunVisible;            // the Sun's strength, and how much of it is not behind the Earth or the Moon
            float _LightningFlash;                 // Apollo 12's lightning (Lightning.cs)
            float _MoonGain;                       // the landing: the eye adapted to the bright ground (Landing.cs); 0 = 1

            struct A { float4 pos : POSITION; };
            struct V { float4 cs : SV_POSITION; float3 dir : TEXCOORD0; };
            V vert(A i) { V o; o.cs = TransformObjectToHClip(i.pos.xyz); o.dir = i.pos.xyz; return o; }

            static const float Re = 6371e3, Ra = 6471e3, HR = 8000, HM = 1200;
            static const float3 BR = float3(5.802e-6, 13.558e-6, 33.1e-6);
            static const float BM = 3.996e-6, EM = 4.44e-6;
            float2 Sph(float3 o, float3 d, float r)
            {
                float b = dot(o, d), c = dot(o, o) - r * r, h = b * b - c;
                if (h < 0) return float2(-1, -1);                    // a miss: never read as a hit ahead
                h = sqrt(h); return float2(-b - h, -b + h);
            }

            // one octave of cloud noise fixed to the Earth: a tile `km` wide, its mip chosen from the size of a
            // pixel on the ground so nothing finer than a pixel is drawn (no shimmer, no salt-and-pepper)
            float4 NoiseAt(float3 e, float km, float fp)
            {
                float lod = clamp(log2(fp / (km * 7.8125)) + 0.5, 0, 7);              // 128 texels a tile
                return SAMPLE_TEXTURE3D_LOD(_DetailNoise, sampler_DetailNoise, e * (6371.0 / km), lod);
            }

            // the cloud fields below the cloud map's 4.9 km pixels: noise in four octaves (420 km weather systems
            // that swirl the rest, 95 km clusters, 21 km groups, 4.6 km single clouds)
            float CloudNoise(float3 e, float fp)
            {
                float4 a = NoiseAt(e, 420, fp);
                float3 w = (a.rgb - 0.5) * (40.0 / 6371.0);
                float4 b = NoiseAt(e + w, 95, fp);
                float4 c = NoiseAt(e + w * 0.6 + (b.gba - 0.5) * (7.0 / 6371.0), 21, fp);
                float4 d = NoiseAt(e + w * 0.3 + (c.gba - 0.5) * (1.6 / 6371.0), 4.6, fp);
                return b.r * 0.36 + c.r * 0.30 + c.g * 0.10 + d.r * 0.16 + d.g * 0.08;
            }

            // the Earth's surface at a point (unit vector from the Earth's centre, local frame), lit by the Sun
            float3 Surface(float3 n, float3 sd, float3 view, float3 sunC, float dist)
            {
                float3 e = _EarthE.xyz * n.x + _EarthU.xyz * n.y + _EarthN.xyz * n.z;
                float lat = asin(clamp(e.z, -1, 1)), lon = atan2(e.y, e.x);
                float2 uv = float2(lon / (2 * PI) + 0.5, lat / PI + 0.5);
                // how big one pixel is down there (stretched where the ground is seen at a slant)
                float pixA = _PixelAngle > 0 ? _PixelAngle : 0.0011;
                float cosI = max(dot(n, -view), 0.03);
                float fp = dist * pixA * pow(cosI, -0.6);
                float3 day = SAMPLE_TEXTURE2D_LOD(_Day, sampler_Day, uv, log2(max(fp / 2446.0, 1.0))).rgb;   // explicit mips: no seam at 180 deg
                float cov = SAMPLE_TEXTURE2D_LOD(_EarthClouds, sampler_Day, uv, log2(max(fp / 4892.0, 1.0)) + 0.6).r;
                // NASA's cloud map says how much of each 4.9 km is cloud; below that the clouds are drawn from the noise,
                // cut at the level that leaves the same fraction cloudy, so near they have real edges and far they
                // add up to the map again
                float detail = saturate(log2(4892.0 / fp) / 2.5);
                float N = CloudNoise(e, fp);
                float th = lerp(0.70, 0.30, cov);
                float soft = 0.035 + 0.05 * saturate(log2(fp / 200.0) / 4);
                float cl = lerp(cov, saturate((N - th) / soft), detail * saturate(cov * 8));
                // their shadows on the sea: is there a cloud 1.5 km up between here and the Sun?
                float3 sh = sd - n * dot(sd, n);
                float mu = dot(n, sd);
                float off = 1.5 / max(mu, 0.15);
                float3 eS = e + (_EarthE.xyz * sh.x + _EarthU.xyz * sh.y + _EarthN.xyz * sh.z) * (off / 6371.0);
                float shadow = saturate((CloudNoise(eS, fp) - th) / soft) * detail * saturate(cov * 8) * (1 - cl);
                float lit = saturate(mu * 1.2 + 0.05);
                float water = saturate((day.b - max(day.r, day.g)) * 8);
                float3 r = reflect(-sd, n);
                float rv = saturate(dot(r, -view));
                float glint = (pow(rv, 60) * 0.9 + pow(rv, 900) * 3) * water * (1 - cl) * (1 - shadow);   // the broad sun glint on the sea
                float fres = pow(1 - cosI, 4) * water;                                                     // the sea reflects the sky toward the edge
                float3 ground = day * (1 - 0.55 * shadow) + float3(0.10, 0.16, 0.26) * fres * 0.6;
                float thick = saturate((N - th) / 0.22);
                float3 cloudCol = float3(0.95, 0.96, 0.98) * lerp(lerp(0.80, 1.04, thick), 0.95, 1 - detail);   // bright tops, greyer thin edges
                float3 col = lerp(ground, cloudCol, cl * 0.96) * sunC * lit + sunC * glint * saturate(mu * 4);
                float3 night = SAMPLE_TEXTURE2D_LOD(_Night, sampler_Day, uv, log2(max(fp / 4892.0, 1.0))).rgb * (1 - cl * 0.7) * saturate(-mu * 6) * 0.25;
                return col + night;
            }

            // single scattering along the view ray (16 steps, 5 toward the Sun each), the surface where it lands
            float3 Atmosphere(float3 d, float3 sd, float sunI, out float hitsEarth)
            {
                float3 sunC = sunI.xxx;
                float3 O = float3(0, Re + max(_CamAltitude, 2), 0);
                float2 ta = Sph(O, d, Ra), tg = Sph(O, d, Re);
                hitsEarth = tg.x > 0 ? 1 : 0;
                float3 surf = 0;
                if (tg.x > 0) surf = Surface(normalize(O + d * tg.x), sd, d, sunC, tg.x);
                if (ta.y < 0) return surf;
                float t0 = max(ta.x, 0), t1 = (tg.x > 0) ? tg.x : ta.y;
                float ds = (t1 - t0) / 16;
                float3 sR = 0, sM = 0; float oR = 0, oM = 0;
                [loop] for (int k = 0; k < 16; k++)
                {
                    float3 p = O + d * (t0 + (k + 0.5) * ds);
                    float h = length(p) - Re;
                    float hr = exp(-h / HR) * ds, hm = exp(-h / HM) * ds;
                    oR += hr; oM += hm;
                    if (Sph(p, sd, Re).x > 0) continue;
                    float tl = Sph(p, sd, Ra).y, dl = tl / 5, lR = 0, lM = 0;
                    [unroll] for (int j = 0; j < 5; j++) { float hl = length(p + sd * (j + 0.5) * dl) - Re; lR += exp(-hl / HR) * dl; lM += exp(-hl / HM) * dl; }
                    float3 att = exp(-(BR * (oR + lR) + EM * (oM + lM)));
                    sR += att * hr; sM += att * hm;
                }
                float mu = dot(d, sd), g = 0.76;
                float pR = 3.0 / (16.0 * PI) * (1 + mu * mu);
                float pM = 3.0 / (8.0 * PI) * ((1 - g * g) * (1 + mu * mu)) / ((2 + g * g) * pow(max(1 + g * g - 2 * g * mu, 1e-4), 1.5));
                float3 c = sunC * _AtmoSun * (sR * BR * pR + sM * BM * pM);
                // light scattered many times fills in what single scattering reddens along the long paths at the
                // edge of the Earth: the low limb is white-blue, not a pink band
                float odv = BR.b * oR + EM * oM;
                c = lerp(c, dot(c, float3(0.2126, 0.7152, 0.0722)) * float3(0.84, 0.92, 1.04), saturate((odv - 0.8) / 3.0) * 0.9);
                return c + surf * exp(-(BR * oR + EM * oM)) * 0.55;
            }

            half4 frag(V i) : SV_Target
            {
                float3 d = normalize(i.dir);
                Light L = GetMainLight();
                float3 sd = L.direction;
                // the light is switched off when we are in a shadow; the Earth and the Moon out there are still sunlit
                float sunI = _SunVisible > 0.01 ? dot(L.color, float3(0.333, 0.333, 0.333)) / _SunVisible : _SunRaw;
                float alt = max(_CamAltitude, 2);
                float hitsEarth;
                half3 c = Atmosphere(d, sd, sunI, hitsEarth) * _Exposure;

                // low down: a hazy horizon, and the far ground lost in it (the near ground is real terrain)
                half3 hz = _Haze.rgb * _Exposure;
                half al = dot(c, half3(0.2126, 0.7152, 0.0722));
                c = lerp(al.xxx, c, 0.85) * half3(0.96, 0.96, 1.06);
                float low = 1 - smoothstep(3000, 20000, alt);
                float dip = sqrt(2.0 * alt / Re);
                float y = d.y + dip;
                float tw = 1 - smoothstep(-0.004, 0.28, y);
                c = lerp(c, hz, saturate(tw * tw * 1.2) * low);
                c = lerp(c, hz, hitsEarth * (1 - smoothstep(4000, 30000, alt)));

                if (hitsEarth < 0.5)
                {
                    // space: how much the air in front still glows (none above the atmosphere)
                    float dark = smoothstep(18000, 70000, alt) * saturate(1 - dot(c, half3(0.33, 0.33, 0.33)) * 6);
                    float3 q = mul((float3x3)_LocalToStars, d);
                    float ra = atan2(q.z, q.x), dec = asin(clamp(q.y, -1, 1));
                    float2 suv = float2(frac(0.5 - ra / (2 * PI)), 0.5 + dec / PI);
                    // stars: an eye that has the sunlit Earth in view sees the bright ones, not a carpet of faint ones,
                    // so faint stars are pressed down; the Milky Way is a faint pale glow, not a bright brown band
                    half3 st = SAMPLE_TEXTURE2D_LOD(_Stars, sampler_Stars, suv, 0).rgb * 7.0;
                    half sl = dot(st, half3(0.2126, 0.7152, 0.0722));
                    st *= pow(saturate(sl / 0.6), 0.9);                                          // brightness^1.9 below 0.6, unchanged above
                    half3 mw = SAMPLE_TEXTURE2D_LOD(_Milky, sampler_Stars, suv, 0).rgb;
                    mw = lerp(dot(mw, half3(0.2126, 0.7152, 0.0722)).xxx, mw, 0.45) * 0.55;
                    c += (st + mw) * dark * dark;
                    // planets: sharp points
                    [unroll] for (int k = 0; k < 5; k++)
                    {
                        float a = acos(clamp(dot(d, _Planets[k].xyz), -1, 1));
                        c += _PlanetColours[k].rgb * _Planets[k].w * exp(-a * a / (2 * 0.0006 * 0.0006)) * lerp(0.15, 1, dark);
                    }
                }

                // the Moon: a real sphere where it really is, turned the way it really is (IAU), lit by the Sun, its night
                // side faintly lit by the Earth; close enough in lunar orbit to fill the windows
                float moonCover = 0;
                float spaceK = smoothstep(20000, 80000, alt);
                {
                    float2 mh = Sph(-_MoonPos.xyz, d, _MoonPos.w);
                    float tE = Sph(float3(0, Re + alt, 0), d, Re).x / 1000.0;
                    if (_MoonPos.w > 0 && mh.x > 0 && (tE < 0 || mh.x < tE))
                    {
                        float3 n = (d * mh.x - _MoonPos.xyz) / _MoonPos.w;
                        float lat = asin(clamp(dot(n, _MoonPole.xyz), -1, 1));
                        float lon = atan2(dot(n, _MoonEast.xyz), dot(n, _MoonMeridian.xyz));
                        float2 muv = float2(lon / (2 * PI) + 0.5, lat / PI + 0.5);
                        float pixA = _PixelAngle > 0 ? _PixelAngle : 0.0011;
                        float cosI = max(dot(n, -d), 0.05);
                        float lodm = log2(max(mh.x * pixA / cosI / max(_MoonTexel, 0.1), 1.0));
                        float3 alb = SAMPLE_TEXTURE2D_LOD(_MoonTex, sampler_MoonTex, muv, lodm).rgb;
                        // the real relief (NASA LOLA heights): crater walls catch the Sun or fall into shadow
                        float3 rel = SAMPLE_TEXTURE2D_LOD(_MoonNormal, sampler_MoonNormal, muv, max(lodm - 1.5, 0)).rgb * 2 - 1;
                        float3 ea = normalize(cross(_MoonPole.xyz, n) + float3(1e-5, 0, 0)), no = cross(n, ea);
                        float3 nd = normalize(ea * rel.x + no * rel.y + n * max(rel.z, 0.05));
                        float mu0 = dot(nd, sd) * saturate(dot(n, sd) * 8 + 0.6);                // the terminator still follows the ball
                        float ls = mu0 > 0 ? mu0 / (mu0 + cosI) : 0;                           // Lommel-Seeliger: dust, not paint
                        float3 moon = alb * ls * 1.6 * sunI * max(_MoonGain, 1);
                        float3 toE = normalize(float3(0, -(Re + alt) / 1000.0, 0) - d * mh.x);
                        moon += alb * saturate(dot(n, toE)) * 0.035;                          // earthshine
                        moonCover = saturate((mh.y - mh.x) / (_MoonPos.w * 0.01));             // a soft limb, one pixel or so
                        c = lerp(c, moon + c * 0.3 * (1 - spaceK), moonCover);
                    }
                    // when the Moon hides the Sun, its corona shows around the black disc (Apollo 11, day 3)
                    float mdist = length(_MoonPos.xyz);
                    float ar = asin(min(1, _MoonPos.w / max(mdist, 1)));
                    float hidden = saturate((ar - acos(clamp(dot(_MoonPos.xyz / max(mdist, 1), sd), -1, 1))) / 0.0047);
                    if (hidden > 0)
                    {
                        float a = acos(clamp(dot(d, sd), -1, 1)) - 0.0047;
                        float3 ax = normalize(cross(sd, float3(0.13, 1, 0.07))), ay = cross(sd, ax);
                        float ang = atan2(dot(d, ay), dot(d, ax));
                        float streamers = 0.6 + 0.4 * sin(ang * 3 + 1.3) * sin(ang * 7 + 0.4);
                        float corona = (exp(-max(a, 0) / 0.004) * 1.4 + exp(-max(a, 0) / 0.018) * 0.35 * streamers) * step(0, a);
                        c += float3(1.0, 0.96, 0.9) * corona * hidden * (1 - moonCover);
                    }
                }

                // the Sun: the real size (0.53 degrees), a glow, brighter above the air
                float mu = dot(d, sd);
                float space = smoothstep(20000, 80000, alt);
                c += L.color * (40 * smoothstep(0.999984, 0.999990, mu) + (0.35 + space * 0.8) * pow(saturate(mu), 900) + 0.06 * (1 - space) * pow(saturate(mu), 40)) * (1 - hitsEarth) * (1 - moonCover);
                c += half3(1.0, 0.55, 0.18) * _Flash * (0.6 + 0.4 * saturate(d.y + 0.5));
                c += half3(0.85, 0.9, 1.0) * _LightningFlash * 0.9;
                return half4(c * max(_SkyBoost, 1), 1);
            }
            ENDHLSL
        }
    }
}
