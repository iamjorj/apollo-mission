// Earth from NASA data: Blue Marble (July) by day, Black Marble city lights by night (dimmed),
// clouds from a GIBS satellite photo, sunlight from the real Sun direction (_SunDirWorld),
// sunglint on the oceans and a blue limb.
// The clouds move: they flow with the planet's wind belts (trade winds toward the west in the tropics, the westerlies
// toward the east at mid-latitudes, polar easterlies), sped up about 14,000 times (a day in about 6 seconds), and
// slowly grow and fade. Over Florida their cover follows the launch day's real weather (_KscWeather, Weather.cs).
Shader "Apollo/Earth"
{
    Properties
    {
        _DayTex ("Blue Marble", 2D) = "white" {}
        _NightTex ("Black Marble", 2D) = "black" {}
        _CloudTex ("Clouds", 2D) = "black" {}
        _NightStrength ("City lights", Float) = 0.9
        _CloudShift ("Cloud drift", Float) = 0
        _FlowSpeed ("Wind time-lapse (uv per second at 1 m/s-ish)", Float) = 0.0012
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _DayTex, _NightTex, _CloudTex;
            float _NightStrength, _CloudShift, _FlowSpeed;
            float4 _SunDirWorld;
            float4 _KscWeather;                 // x: cloud cover 0..1 at Kennedy that day, y: rain 0..1, w: 1 when set
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 wn : TEXCOORD1; float3 wp : TEXCOORD2; };
            v2f vert (appdata_base v)
            {
                v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord.xy;
                o.wn = UnityObjectToWorldNormal(v.normal); o.wp = mul(unity_ObjectToWorld, v.vertex).xyz; return o;
            }

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float VNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }

            // the wind belts: east-west speed by latitude (negative = toward the west), a little poleward in the westerlies
            float2 Wind(float latDeg)
            {
                float a = abs(latDeg);
                float u = lerp(-1.0, 1.7, smoothstep(24, 36, a));
                u = lerp(u, -0.5, smoothstep(58, 70, a));
                float v = 0.18 * smoothstep(30, 40, a) * (1 - smoothstep(55, 65, a)) * sign(latDeg);
                return float2(u, v * 0.5);
            }

            float Clouds(float2 uv)
            {
                float lat = (uv.y - 0.5) * 180.0;
                float2 flow = Wind(lat) * _FlowSpeed * float2(1.0 / max(cos(radians(lat)), 0.2), 1);
                // two copies of the same cloud map sliding with the wind, half a cycle apart. Each copy is invisible at
                // the moment it jumps back to its start (its weight is 0 there) and fully shown half a cycle later, so the
                // motion is smooth and never resets in view; the slide is short (a few degrees), so the copies agree
                const float T = 6.0;
                float t = _Time.y / T;
                float p0 = frac(t), p1 = frac(t + 0.5);
                float2 base = uv + float2(_CloudShift, 0);
                float2 lead = flow * T * 0.5;                                         // centre each slide on the map
                float c0 = tex2D(_CloudTex, base - flow * p0 * T + lead).r;
                float c1 = tex2D(_CloudTex, base - flow * p1 * T + lead).r;
                float w0 = 1 - abs(1 - 2 * p0);                                       // 0 at the jump, 1 half a cycle later
                float c = c0 * w0 + c1 * (1 - w0);
                // clouds grow and fade: a slow, wandering threshold
                float g = VNoise(uv * float2(40, 20) + _Time.y * 0.05) * 0.6 + VNoise(uv * float2(110, 55) - _Time.y * 0.11) * 0.4;
                c = saturate((c - (g - 0.5) * 0.22) * 1.08);
                return c;
            }

            float4 frag (v2f i) : SV_Target
            {
                float3 n = normalize(i.wn), l = normalize(_SunDirWorld.xyz), v = normalize(_WorldSpaceCameraPos - i.wp);
                float ndl = dot(n, l);
                float3 day = tex2D(_DayTex, i.uv).rgb;
                float cloud = Clouds(i.uv);
                // Florida on the launch day: the real record's cloud cover in a weather system about 600 km across
                if (_KscWeather.w > 0.5)
                {
                    float lat = (i.uv.y - 0.5) * 180.0, lon = (i.uv.x - 0.5) * 360.0;
                    float2 d = float2((lon + 80.6) * cos(radians(28.6)), lat - 28.6);
                    float blob = saturate(1.6 * exp(-dot(d, d) / (2 * 4.0 * 4.0)));
                    // a field of separate clouds with clear sky between, as much of it covered as the record says
                    float n = VNoise(i.uv * float2(260, 130) + _Time.y * 0.03) * 0.65 + VNoise(i.uv * float2(780, 390) - _Time.y * 0.05) * 0.35;
                    float field = saturate((n - (1 - _KscWeather.x) * 0.9 - 0.05) * 5.0);
                    cloud = lerp(cloud, field, blob);
                }
                float ocean = saturate((day.b - max(day.r, day.g)) * 12.0);
                float lit = saturate(ndl);
                float dusk = smoothstep(-0.12, 0.18, ndl);
                float3 surface = lerp(day * 1.15, float3(0.92, 0.93, 0.95), cloud * 0.92);
                float3 col = surface * (lit * 1.1 + 0.004);
                float3 h = normalize(l + v);
                col += pow(saturate(dot(n, h)), 90.0) * ocean * (1.0 - cloud) * 1.6 * step(0.0, ndl) * float3(1.0, 0.95, 0.85);
                col += tex2D(_NightTex, i.uv).rgb * (1.0 - dusk) * (1.0 - cloud * 0.75) * _NightStrength;
                float rim = pow(1.0 - saturate(dot(n, v)), 3.0);
                col = lerp(col, float3(0.32, 0.56, 1.0) * (lit + 0.05), rim * 0.55 * dusk);
                return float4(col, 1);
            }
            ENDCG
        }
    }
}
