// The real sky: NASA SVS Deep Star Maps 2020 (1.7 billion stars from Hipparcos-2, Tycho-2 and
// Gaia DR2) plus the Milky Way map, both equirectangular in celestial coordinates centred on 0h RA.
// World frame: +X = RA 0h, +Y = north celestial pole, +Z = RA 6h.
Shader "Apollo/StarSky"
{
    Properties
    {
        _Stars ("Star map", 2D) = "black" {}
        _Milky ("Milky Way", 2D) = "black" {}
        _StarExposure ("Star exposure", Float) = 1.4
        _MilkyExposure ("Milky Way exposure", Float) = 0.6
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _Stars, _Milky;
            float _StarExposure, _MilkyExposure;
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };
            v2f vert (appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.dir = v.vertex.xyz; return o; }
            float4 frag (v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float ra = atan2(d.z, d.x);                    // radians, RA grows from +X towards +Z
                float dec = asin(clamp(d.y, -1, 1));
                float2 uv = float2(frac(0.5 - ra / 6.2831853), 0.5 + dec / 3.1415927);
                float3 c = tex2Dlod(_Stars, float4(uv, 0, 0)).rgb * _StarExposure + tex2Dlod(_Milky, float4(uv, 0, 0)).rgb * _MilkyExposure;
                return float4(c, 1);
            }
            ENDCG
        }
    }
}
