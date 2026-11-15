// Real-scale Earth-Moon scene for the chosen launch date (1 unit = 1000 km).
// Earth at the origin, turned to the real sidereal time at liftoff; the Moon where NASA JPL
// puts it on landing day; sunlight from the real Sun direction; a simplified translunar path.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class SpaceScene : MonoBehaviour
{
    public const float EarthRadius = 6.371f, MoonRadius = 1.7374f, ParkingOrbit = 6.556f;
    public static SpaceScene Instance;

    public Transform Earth, Moon, MoonAtLaunch, Kennedy;
    public Transform SitePin;
    float pinDropTime = -10f;
    public Light Sun;
    public Material LineMaterial;
    public bool ShowLines = true;

    public Vector3 MoonPos { get; private set; }
    public Vector3 MoonLaunchPos { get; private set; }
    readonly List<Vector3> path = new List<Vector3>(), orbit = new List<Vector3>();

    void Awake() => Instance = this;

    void OnEnable()
    {
        MissionState.Changed += Refresh;
        RenderPipelineManager.endCameraRendering += DrawLines;
    }

    void OnDisable()
    {
        MissionState.Changed -= Refresh;
        RenderPipelineManager.endCameraRendering -= DrawLines;
    }

    void Start() => Refresh();

    public void Refresh()
    {
        DateTime launch = MissionState.LaunchUtc;
        DateTime landing = MissionModel.LandingUtc(launch);

        Vector3 sunDir = Ephemeris.SunDirWorld(launch);
        Shader.SetGlobalVector("_SunDirWorld", sunDir);
        if (Sun) Sun.transform.rotation = Quaternion.LookRotation(-sunDir);

        Earth.rotation = Ephemeris.EarthRotation(launch);
        MoonPos = Ephemeris.MoonWorld(landing);
        MoonLaunchPos = Ephemeris.MoonWorld(launch);
        Moon.SetPositionAndRotation(MoonPos, Ephemeris.MoonRotation(landing));
        if (MoonAtLaunch) MoonAtLaunch.position = MoonLaunchPos;

        BuildPath(launch);
    }

    // A simplified translunar coast: an ellipse from the parking orbit (perigee, roughly opposite
    // the Moon's arrival point) out to the Moon, in the plane of the Moon's motion.
    void BuildPath(DateTime launch)
    {
        // the path ends at the Moon on landing day (the crew circles it for a day before landing)
        Vector3 arrive = MoonPos;
        Vector3 later = Ephemeris.MoonWorld(MissionModel.LandingUtc(launch).AddHours(6));
        Vector3 m = arrive.normalized;
        Vector3 along = (later - arrive);
        Vector3 e2 = (along - Vector3.Dot(along, m) * m).normalized;
        Vector3 e1 = -m;
        float rp = ParkingOrbit, ra = arrive.magnitude - MoonRadius * 2.2f;
        float a = (rp + ra) * 0.5f, e = (ra - rp) / (ra + rp);
        path.Clear();
        for (int k = 0; k <= 180; k++)
        {
            float nu = k * Mathf.Deg2Rad;
            float r = a * (1 - e * e) / (1 + e * Mathf.Cos(nu));
            path.Add(r * (Mathf.Cos(nu) * e1 - Mathf.Sin(nu) * e2));
        }
        // the Moon's orbit as a faint circle through its landing-day position
        orbit.Clear();
        float R = MoonPos.magnitude;
        Vector3 u = MoonPos.normalized, w = (e2 - Vector3.Dot(e2, u) * u).normalized;
        for (int k = 0; k <= 256; k++)
        {
            float t = k / 256f * Mathf.PI * 2f;
            orbit.Add(R * (Mathf.Cos(t) * u + Mathf.Sin(t) * w));
        }
    }

    void DrawLines(ScriptableRenderContext ctx, Camera cam)
    {
        if (LineMaterial == null || cam.cameraType != CameraType.Game || !ShowLines) return;
        GL.PushMatrix();
        GL.LoadProjectionMatrix(GL.GetGPUProjectionMatrix(cam.projectionMatrix, false));
        GL.modelview = cam.worldToCameraMatrix;
        LineMaterial.SetPass(0);
        GL.Begin(GL.LINES);
        GL.Color(new Color(1f, 1f, 1f, 0.10f));
        for (int i = 0; i < orbit.Count - 1; i++) { GL.Vertex(orbit[i]); GL.Vertex(orbit[i + 1]); }
        GL.Color(new Color(1f, 0.82f, 0.45f, 0.75f));
        for (int i = 0; i < path.Count - 1; i += 2) { GL.Vertex(path[i]); GL.Vertex(path[i + 1]); }   // dashed
        GL.End();
        GL.PopMatrix();
    }

    public Vector3 SiteWorld() =>
        Moon.position + Moon.rotation * Ephemeris.BodyPoint(MissionState.SiteLat, MissionState.SiteLon) * MoonRadius;

    public void DropPin() => pinDropTime = Time.unscaledTime;

    void LateUpdate()
    {
        if (SitePin)
        {
            SitePin.gameObject.SetActive(MissionState.HasSite);
            if (MissionState.HasSite)
            {
                SitePin.position = SiteWorld();
                float t = Time.unscaledTime - pinDropTime;
                float drop = t < 0.4f ? 1f + 2.2f * (1f - t / 0.4f) : 1f;          // lands with a little pulse
                SitePin.localScale = Vector3.one * 0.045f * drop * (1f + 0.12f * Mathf.Sin(Time.unscaledTime * 4f));
            }
        }
        if (Kennedy) { Kennedy.position = KennedyWorld(); Kennedy.localScale = Vector3.one * 0.08f; }
    }

    public Vector3 KennedyWorld() => Earth.rotation * Ephemeris.BodyPoint(28.573, -80.649) * EarthRadius;
}
