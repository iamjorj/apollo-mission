// Shared cloud field for the Florida sky, the ground and the water: fair-weather cumulus between a flat
// base and rounded tops, drifting with the wind. Set globally by FlightDirector:
//   _CloudParams  x cover (0..1), y base, z top (metres above sea level), w coverage tile size (m)
//   _CloudWind    xy drift (m), z detail tile size (m)
//   _CloudAltOffset  altitude of world y = 0 (the pad surface)
#ifndef APOLLO_CLOUDS_INCLUDED
#define APOLLO_CLOUDS_INCLUDED

TEXTURE2D(_CloudCoverage); SAMPLER(sampler_CloudCoverage);
TEXTURE3D(_CloudDetail); SAMPLER(sampler_CloudDetail);
float4 _CloudParams;
float4 _CloudWind;
float _CloudAltOffset;
float _SkyBright;

// cloud amount at the base for a horizontal position, 0 (clear) .. 1 (thick)
float CloudCoverage(float2 xz)
{
    if (_CloudParams.w <= 0) return 0;
    float2 q = xz + _CloudWind.xy;
    float a = SAMPLE_TEXTURE2D_LOD(_CloudCoverage, sampler_CloudCoverage, q / _CloudParams.w, 0).r;
    float b = SAMPLE_TEXTURE2D_LOD(_CloudCoverage, sampler_CloudCoverage, q / (_CloudParams.w * 0.31) + 0.37, 0).g;
    float v = a * 0.72 + b * 0.28;
    return saturate((v - (1 - _CloudParams.x)) / max(_CloudParams.x, 0.01));
}

// density at a point (alt = metres above sea level): flat bases, tops that narrow with height
float CloudShape(float2 xz, float alt)
{
    float h = (alt - _CloudParams.y) / max(_CloudParams.z - _CloudParams.y, 1);
    if (h < 0 || h > 1) return 0;
    float cov = CloudCoverage(xz);
    float base = saturate(h * 10);
    return saturate(cov * 1.35 - h * h * 1.1) * base;
}

float CloudDetail(float3 p)
{
    float3 q = (p + float3(_CloudWind.x, 0, _CloudWind.y) * 1.2) / max(_CloudWind.z, 1);
    return SAMPLE_TEXTURE3D_LOD(_CloudDetail, sampler_CloudDetail, q, 0).r;
}

// how much sunlight reaches a point on the ground through the cloud layer (1 = clear): the same weather
// map and coverage as the 3D clouds (Apollo/VolumeClouds), seen along the sun ray at mid-layer
TEXTURE2D(_CloudWeather); SAMPLER(sampler_CloudWeather);
float4 _VC_Layer, _VC_Scale, _VC_Origin, _VC_Wind;
float CloudShadow(float3 ws, float3 sunDir)
{
    if (_VC_Scale.z <= 0) return 1;
    float alt = ws.y + _VC_Origin.y;
    float mid = lerp(_VC_Layer.x, _VC_Layer.y, 0.3);
    float t = (mid - alt) / max(sunDir.y, 0.15);
    float2 xz = ws.xz + _VC_Origin.xz + sunDir.xz * t;
    float2 pw = xz + _VC_Wind.xy;
    pw = float2(pw.x * 0.921 - pw.y * 0.389, pw.x * 0.389 + pw.y * 0.921);
    float w = SAMPLE_TEXTURE2D_LOD(_CloudWeather, sampler_CloudWeather, pw / _VC_Scale.z, 0).r;
    float cov = saturate(w * _VC_Layer.z * 1.6);
    return 1 - 0.55 * smoothstep(0.35, 0.65, cov);
}

#endif
