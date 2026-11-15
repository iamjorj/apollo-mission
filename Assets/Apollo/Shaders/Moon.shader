// The Moon from NASA LRO data (CGI Moon Kit colour map), lit by the real Sun direction,
// with faint earthshine on the night side.
Shader "Apollo/Moon"
{
    Properties { _MainTex ("LRO colour map", 2D) = "gray" {} _Earthshine ("Earthshine", Float) = 0.012 }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float _Earthshine;
            float4 _SunDirWorld;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 wn : TEXCOORD1; };
            v2f vert (appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord.xy; o.wn = UnityObjectToWorldNormal(v.normal); return o; }
            float4 frag (v2f i) : SV_Target
            {
                float3 n = normalize(i.wn);
                float ndl = saturate(dot(n, normalize(_SunDirWorld.xyz)));
                float3 albedo = tex2D(_MainTex, i.uv).rgb;
                return float4(albedo * (ndl * 1.35 + _Earthshine), 1);
            }
            ENDCG
        }
    }
}
