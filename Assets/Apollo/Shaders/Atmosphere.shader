// Thin blue glow around the Earth's limb on the sunlit side (additive shell, 2.5% larger than Earth).
Shader "Apollo/Atmosphere"
{
    Properties { _Color ("Colour", Color) = (0.35, 0.6, 1.0, 1) _Strength ("Strength", Float) = 1.2 }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }
        Blend One One
        ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _Color, _SunDirWorld;
            float _Strength;
            struct v2f { float4 pos : SV_POSITION; float3 wn : TEXCOORD0; float3 wp : TEXCOORD1; };
            v2f vert (appdata_base v)
            { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.wn = UnityObjectToWorldNormal(v.normal); o.wp = mul(unity_ObjectToWorld, v.vertex).xyz; return o; }
            float4 frag (v2f i) : SV_Target
            {
                float3 n = normalize(i.wn), v = normalize(_WorldSpaceCameraPos - i.wp);
                float rim = pow(1.0 - saturate(dot(n, v)), 4.0);
                float sun = saturate(dot(n, normalize(_SunDirWorld.xyz)) + 0.25);
                return float4(_Color.rgb * rim * sun * _Strength, 1);
            }
            ENDCG
        }
    }
}
