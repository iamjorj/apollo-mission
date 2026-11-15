// Editor-only helper: pulls the clouds out of a real NASA satellite photo (GIBS VIIRS true color)
// by comparing it with the cloud-free NASA Blue Marble of the same month.
// Model: photo = ground * (1 - a) + white * a   =>   a = (photo - ground) / (1 - ground)
Shader "Hidden/Apollo/CloudExtract"
{
    Properties
    {
        _MainTex ("Satellite photo", 2D) = "black" {}
        _Base ("Cloud-free ground", 2D) = "black" {}
    }
    SubShader
    {
        ZTest Always Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _Base;

            float4 frag (v2f_img i) : SV_Target
            {
                float3 photo  = tex2D(_MainTex, i.uv).rgb;
                float3 ground = tex2D(_Base, i.uv).rgb;
                const float3 lw = float3(0.2126, 0.7152, 0.0722);

                // Clouds brighten every colour channel, so take the weakest channel.
                float3 a3 = saturate((photo - ground) / max(1.0 - ground, 0.04));
                float a = min(a3.r, min(a3.g, a3.b));

                // Over ice and bright salt flats a cloud cannot be told apart from the ground.
                a *= 1.0 - smoothstep(0.45, 0.7, dot(ground, lw));
                // Areas the satellite did not see (polar night) are pure black: no cloud data there.
                a *= smoothstep(0.002, 0.01, dot(photo, lw));

                a = smoothstep(0.06, 0.8, a);
                return float4(a, a, a, 1);
            }
            ENDCG
        }
    }
}
