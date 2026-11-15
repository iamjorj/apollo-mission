// Step 4: the pad, the walk in, and the countdown.
// Fades in over the real Kennedy Space Center with the Sun where it really is at the planned launch time,
// flies up the Saturn V to Level 320 of the tower, then the student walks (WASD, drag to look) across
// swing arm 9 into the white room and climbs through the hatch into Armstrong's couch in the real
// Columbia cabin (Smithsonian scan), which sits inside the rocket. The hatch closes, arm 9 swings away,
// and only the hatch window still looks out (the boost protective cover hides the others, as in 1969).
// The clock holds at T-10 s until START COUNTDOWN; the last 10 seconds play NASA's own recording of the
// Apollo 11 count (Jack King, Launch Control) and the clock follows his voice.
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class FlightDirector : MonoBehaviour
{
    public Camera Cam;
    public Light Sun;
    public Transform CommanderEye;
    public ReflectionProbe Probe;
    public AudioClip Countdown;
    public AudioClip AscentRadio;                       // NASA/JSC air-to-ground, liftoff to orbit (no commentary)
    AudioSource radio;
    public Light[] CabinLights;
    public Transform Arm9;
    public GameObject HatchClosed, HatchOpening;
    public Renderer[] HideWhileEntering;
    public float FloorY, Level320;
    public Vector4 WalkwayBounds, RoomBounds;          // xmin, xmax, zmin, zmax
    public Vector3 HatchOutside, HatchInside, HatchInward, HatchWindow;
    public Texture2D CloudCoverage;
    public Texture3D CloudDetail;
    public Texture2D CloudWeather;                       // where the 3D cumulus stand (Apollo/VolumeClouds)
    public Texture3D CloudShape;
    public Material SkyMat;
    public Transform SaturnRoot, WorldRoot;              // what the climb moves (Ascent)
    public Material PlumeMat, TrailMat;                  // the engines' flame and the exhaust trail (Exhaust)
    public bool FastTest;
    public const float NozzleYPublic = (22 + 25) * 0.3048f + 3.35f - 5.0f;
    Ascent ascent;
    Voyage voyage;
    Lightning lightning;
    bool garbled;
    float altZero = 14.1f;                               // altitude (m) of world y = 0: the pad, until the world moves
    TextMeshProUGUI telemetry, explain, warpText;
    RectTransform explainPlate;
    float explainUntil, explainMet;
    Light stagingGlow;
    public bool Outside => outside;
    Vector4[] planetDirs = new Vector4[5], planetCols = new Vector4[5];
    // the 3D cloud layer: base and top (m above sea level), coverage, extinction per metre
    public static Vector4 VcLayer = new Vector4(1100f, 2400f, 0.32f, 0.03f);         // changed by the launch day's weather (ApplyWeather)
    public static readonly Vector4 VcScale = new Vector4(3800f, 160f, 60000f, 0.2f);   // shape, detail, weather tiles (m), edge erosion; detail: 5 m texels, eddies of 20-80 m
    public static readonly Vector4 VcLight = new Vector4(1.15f, 0.45f, 1.1e-4f, 35000f);   // sun, sky light, haze, max distance
    Vector2 vcDrift;
    float cloudVis = 1f;

    // the old flat cloud layer's settings (FloridaSky / Clouds.hlsl; switched off: _CloudParams = 0)
    static readonly Vector4 CloudParams = new Vector4(0.38f, 1100f, 2500f, 14000f);

    // The launch-day air, 16 July 1969 (NASA SP-4029 "Launch Weather"; NWS Melbourne Apollo weather summary): wind on
    // the 60 ft pole 10.8 ft/s (3.3 m/s) from 175 deg, surface report 220 deg 5 kt, cumulus 1/10 at 2,400 ft (SCT030),
    // strongest wind near max Q 31.6 ft/s (9.6 m/s) from 297 deg at 37,400 ft. The wind at the cumulus (1-2 km) is not
    // in those summaries: it veers and strengthens up through the friction layer, about 5 m/s from 200 deg, so the
    // cumulus drift north-north-east. x = east, z = north.
    static Vector2 CloudWindVel = new Vector2(1.71f, 4.70f);                           // m/s, toward 020 deg (1969); other days: ApplyWeather
    // inside a fair-weather cumulus the air rises 1-3 m/s, faster in the bubbles that build its turrets: the
    // billows rise through the cloud body at 1.5 m/s and the small eddies at 3.5 m/s, so the cloud boils
    const float UpdraftBody = 1.5f, UpdraftEddies = 3.5f;
    // the cumulus the rocket flies through (VolumeClouds' "stamp"): the eye is in it from MET 27.0 s (1,100 m)
    // to 38.0 s (2,200 m); at MET 32.5 s its centre is on the climb, 60 m east and 20 m north of the pad
    static readonly Vector2 StampAtCross = new Vector2(60f, 20f);
    const float StampCrossMet = 32.5f, StampRadius = 480f;
    Vector2 cloudAir;                                    // how far the wind has carried the air (and its clouds), m
    float cloudRise, eddyRise;                           // how far the air inside the clouds has risen, m

    public static FlightDirector Instance;
    public enum Phase { Intro, Walk, ClimbIn, Cabin, Counting, Liftoff, Done }
    public Phase Current { get; private set; } = Phase.Intro;
    public float SunElevation { get; private set; }
    public float SunAzimuth { get; private set; }

    const double PadLat = 28.6083, PadLon = -80.6041;                  // LC-39A
    const int CabinLayer = 8;
    const float EyeHeight = 1.65f, WalkSpeed = 1.5f;

    // Seconds into NASA's clip, measured from the loudness of each spoken word.
    const float ClipStart = 20.8f, ClipIgnition = 23.1f, ClipZero = 31.8f, ClipLiftoff = 33.0f, ClipEnd = 48.0f;
    static readonly (float clip, int t)[] Spoken =
        { (21.0f, 10), (21.9f, 9), (23.2f, 8), (24.4f, 7), (25.7f, 6), (26.5f, 5), (27.6f, 4), (28.5f, 3), (29.6f, 2), (30.5f, 1), (31.8f, 0) };

    (float t, Vector3 pos, Vector3 look)[] path;
    const float IntroLength = 11.0f;
    static readonly (float from, float to, string text)[] Captions =
    {
        (0.4f, 3.2f, "LAUNCH COMPLEX 39A  ·  KENNEDY SPACE CENTER"),
        (3.4f, 6.1f, "SATURN V AS-506  ·  110.6 M  ·  FIVE F-1 ENGINES"),
        (6.3f, 8.8f, "LAUNCH UMBILICAL TOWER  ·  NINE SWING ARMS"),
        (9.0f, 11.0f, "COLUMBIA  ·  COMMAND MODULE"),
    };

    float introTime; bool introHold, introSkip, fadingOut;
    int fadeToken;
    Vector2 walkPos; float walkYaw = 180f, walkPitch, walked, walkStartTime;
    bool climbRequested;
    float yaw, pitch, yawS, pitchS; bool dragging; Vector2 downMouse;
    float settle;                                       // eases the view into the couch after the cut
    string forcedHover;
    readonly List<CabinLabel> labels = new List<CabinLabel>();
    readonly List<Image> dots = new List<Image>();
    readonly List<string> events = new List<string>();
    int nextEvent;

    AudioSource voice, roarLow, roarMid, wind, fans;
    RectTransform root, readout, reticle, startButton, cabinHud, introHud, countCard;
    TextMeshProUGUI clock, caption, subtitle, readoutText, eventText;
    Image fade;

    UniversalRenderPipelineAsset urp; float oldShadowDistance, oldScale = 1f, oldFsrSharpness; int oldCascades; UpscalingFilterSelection oldFilter; bool oldFsrOverride;

    void Awake()
    {
        Instance = this;
        voice = gameObject.AddComponent<AudioSource>();
        voice.clip = Countdown; voice.playOnAwake = false; voice.spatialBlend = 0f;
        roarLow = Loop(ProcSound.Noise("F1_Roar_Low", 9f, 90f, 16f, 0.31f, 0.25f, 0.0025f, 11));
        roarMid = Loop(ProcSound.Noise("F1_Roar_Mid", 7f, 520f, 70f, 0.47f, 0.35f, 0.004f, 12));
        wind = Loop(ProcSound.Noise("Pad_Wind", 11f, 420f, 60f, 0.11f, 0.65f, 0f, 13));
        fans = Loop(ProcSound.Noise("Cabin_Fans", 6f, 1400f, 260f, 0f, 0f, 0f, 14));
        wind.volume = 0.32f; wind.Play();
        foreach (var l in FindObjectsOfType<CabinLabel>(true)) labels.Add(l);
    }

    AudioSource Loop(AudioClip c)
    {
        var s = gameObject.AddComponent<AudioSource>();
        s.clip = c; s.loop = true; s.playOnAwake = false; s.volume = 0f; s.spatialBlend = 0f;
        return s;
    }

    Vector3 WalkStart => new Vector3(0f, FloorY + EyeHeight, WalkwayBounds.w - 0.2f);

    IEnumerator Start()
    {
        float towerSide = WalkwayBounds.w;
        path = new[]
        {
            (0.0f, new Vector3(-240f, 38f, -300f), new Vector3(0f, 74f, 0f)),
            (3.6f, new Vector3(-140f, 72f, -172f), new Vector3(0f, 92f, 4f)),
            (7.0f, new Vector3(-46f, 106f, -52f), new Vector3(0f, 110f, 2f)),
            (9.4f, new Vector3(-14f, 112.6f, -16f), new Vector3(0f, 112.2f, 0f)),
            (11.0f, new Vector3(-5.5f, 112.4f, -6.0f), new Vector3(0f, 112.2f, 0f)),
        };
        Cam.cullingMask &= ~(1 << CabinLayer);          // the cabin is only seen once you are through the hatch
        ApplyWeather();
        ConfigureShadows();
        SetSun();
        SetPlace(MissionState.LaunchUtc, 28.447f, -80.6041f, 14.1f, Vector3.zero, 1f);     // the pad (geocentric latitude)
        SetupClouds();
        BuildHud();
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-landing") >= 0) { yield return null; BeginLandingDirect(); yield break; }
        PlaceOnPath(0f);
        yield return null;
        QualitySettings.realtimeReflectionProbes = true;
        if (Probe) Probe.RenderProbe();
        StartCoroutine(Fade(0f, 1.2f));
        while (introTime < IntroLength && !introSkip)
        {
            if (!introHold)
            {
                introTime += Time.deltaTime;
                PlaceOnPath(introTime);
            }
            UpdateCaption();
            if (!introHold && introTime > 0.5f && (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Escape))) introSkip = true;
            if (introTime > IntroLength - 0.7f && !fadingOut) { fadingOut = true; StartCoroutine(Fade(1f, 0.6f)); }
            yield return null;
        }
        yield return Fade(1f, 0.25f);
        // inside: the hatch closes, the cabin floodlights on, a short settle into the couch
        Cam.cullingMask |= 1 << CabinLayer;
        HatchClosed.SetActive(true);
        HatchOpening.SetActive(false);
        EnterCabin();
        ParkArm9();                                     // by T-5 min the crew access arm had swung fully back
        StartCoroutine(Fade(0f, 1.1f));
        settle = 1f;
    }

    void OnDestroy()
    {
        if (urp) { urp.shadowDistance = oldShadowDistance; urp.shadowCascadeCount = oldCascades; urp.renderScale = oldScale; urp.upscalingFilter = oldFilter; urp.fsrOverrideSharpness = oldFsrOverride; urp.fsrSharpness = oldFsrSharpness; }
        Shader.SetGlobalVector("_VC_Scale", Vector4.zero);
    }

    // ---------------- weather ----------------

    // The launch day's real weather (NASA POWER, Weather.cs): how much of the sky the cumulus cover and how low they
    // sit, the wind that carries them, how much an overcast dims the Sun under it, and rain in the air
    public Weather.Day Today { get; private set; }
    float overcastDim = 1f;
    ParticleSystem rain;
    void ApplyWeather()
    {
        Today = Weather.For(MissionState.LaunchUtc);
        var d = Today;
        if (MissionState.LaunchUtc.Year == 1969) { Debug.Log("FLIGHT weather: July 1969 (the real launch-day air)"); return; }
        float c = Mathf.Clamp01(d.Cloud / 100f);
        VcLayer = new Vector4(Mathf.Lerp(1100f, 650f, Mathf.InverseLerp(0.6f, 1f, c)), Mathf.Lerp(2400f, 3200f, d.Rainy ? c : 0f), Mathf.Lerp(0.12f, 0.95f, c), 0.03f);
        // the clouds drift on the wind a little above the ground: the trades from the east-south-east in summer,
        // the winter fronts' north-westerlies
        int m = MissionState.LaunchUtc.Month;
        float toward = (m >= 5 && m <= 9 ? 295f : 140f) * Mathf.Deg2Rad;
        float speed = Mathf.Clamp(d.Wind50 * 0.55f, 2f, 14f);
        CloudWindVel = new Vector2(Mathf.Sin(toward), Mathf.Cos(toward)) * speed;
        overcastDim = 1f - 0.65f * Mathf.InverseLerp(0.55f, 1f, c);
        if (wind) wind.volume = Mathf.Clamp(0.12f + d.Wind * 0.03f, 0.12f, 0.6f);
        if (d.Rainy && TrailMat) rain = Rain(Mathf.Clamp(d.Rain, 2f, 40f));
        Debug.Log($"FLIGHT weather: {d.Real:yyyy-MM-dd} (NASA POWER) {d.Short}; layer {VcLayer}, wind {CloudWindVel}, dim {overcastDim:0.00}");
    }

    // rain streaks around the eye, 6 to 40 m away (the cabin's walls hide the near ones; the windows show them)
    ParticleSystem Rain(float mm)
    {
        var go = new GameObject("Rain");
        go.transform.SetParent(Cam.transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = 1000f; main.loop = true; main.playOnAwake = false;
        main.startLifetime = 1.4f; main.startSpeed = 0f; main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.035f);
        main.startColor = new Color(0.8f, 0.85f, 0.9f, 0.35f);
        main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 6000;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 40f; sh.radiusThickness = 0.85f;
        var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(CloudWindVel.x * 0.3f); vel.y = new ParticleSystem.MinMaxCurve(-9f); vel.z = new ParticleSystem.MinMaxCurve(CloudWindVel.y * 0.3f);
        var em = ps.emission; em.rateOverTime = Mathf.Lerp(900f, 4000f, Mathf.InverseLerp(2f, 30f, mm));
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = new Material(TrailMat) { color = new Color(0.85f, 0.9f, 0.95f, 0.5f) };
        r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.06f; r.lengthScale = 1f;
        ps.Play();
        return ps;
    }

    // The 3D cumulus: a field drifting slowly on the sea breeze, placed so one stands right over the
    // climb (the rocket flies up through it) while no cloud shades the pad itself.
    void SetupClouds()
    {
        Shader.SetGlobalVector("_CloudParams", Vector4.zero);
        if (!CloudWeather) return;
        vcDrift = FindCloudDrift(CloudShape, CloudWeather, -Sun.transform.forward, out float score);
        CloudGlobals(CloudWeather, vcDrift, 14.1f, cloudVis);
        Debug.Log($"FLIGHT clouds: cumulus over the climb (score {score:0.00}), drift {vcDrift}");
    }

    // the shader's cloud density without the fine detail (CPU copy of Apollo/VolumeClouds Density)
    public static float CloudDensity(Texture3D shape, Texture2D weather, Vector2 drift, float x, float alt, float z)
    {
        float h = Mathf.Clamp01((alt - VcLayer.x) / (VcLayer.y - VcLayer.x));
        float wx = x + drift.x, wz = z + drift.y;
        Color w = weather.GetPixelBilinear((wx * 0.921f - wz * 0.389f) / VcScale.z, (wx * 0.389f + wz * 0.921f) / VcScale.z);
        float cov = Mathf.Clamp01(w.r * VcLayer.z * 1.6f);
        if (cov <= 0.01f) return 0f;
        float topH = Mathf.Lerp(0.55f, 1f, w.g);
        float hg = Mathf.SmoothStep(0f, 1f, h / 0.05f) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(topH * 0.40f, topH, h)));
        Color sw = weather.GetPixelBilinear(wx / 9100f + 0.37f, wz / 9100f + 0.37f);
        float qx = wx + (sw.g - 0.5f) * 700f, qz = wz + (sw.r - 0.5f) * 700f;
        Color n = shape.GetPixelBilinear((qx * 0.622f - qz * 0.783f) / VcScale.x, alt / VcScale.x, (qx * 0.783f + qz * 0.622f) / VcScale.x);
        float fbm = n.g * 0.625f + n.b * 0.25f + n.a * 0.125f;
        float sh = Mathf.Clamp01((n.r - (fbm - 1f)) / (2f - fbm)) * hg;
        return Mathf.Clamp01((sh - (1f - cov)) / cov) * cov * 4f;
    }

    // a drift of the cloud field that puts a cumulus right over the climb (the rocket flies up through it)
    // while no cloud shades the pad
    public static Vector2 FindCloudDrift(Texture3D shape, Texture2D weather, Vector3 sunDir, out float best)
    {
        best = -9f; Vector2 pick = Vector2.zero;
        float CovAt(Vector2 q) => Mathf.Clamp01(weather.GetPixelBilinear((q.x * 0.921f - q.y * 0.389f) / VcScale.z, (q.x * 0.389f + q.y * 0.921f) / VcScale.z).r * VcLayer.z * 1.6f);
        var climb = new[] { new Vector3(0f, 1300f, 0f), new Vector3(30f, 1550f, 10f), new Vector3(80f, 1800f, 25f), new Vector3(150f, 2050f, 50f) };
        for (int k = 0; k < 4000; k++)
        {
            var off = new Vector2((k * 7919 % 60000), (k * 104729 % 60000));
            float through = 0f;
            foreach (var c in climb) through += Mathf.Min(shape ? CloudDensity(shape, weather, off, c.x, c.y, c.z) : CovAt(off), 0.8f) / climb.Length;
            float t = (1700f - 14f) / Mathf.Max(sunDir.y, 0.15f), shade = 0f;
            for (int a = 0; a <= 12; a++)
            {
                Vector2 pp = a == 12 ? Vector2.zero : new Vector2(Mathf.Cos(a * 0.5236f), Mathf.Sin(a * 0.5236f)) * (a % 2 == 0 ? 120f : 260f);
                for (int m = 0; m <= 4; m++)                                               // now, and as the wind brings clouds over in the next 4 minutes
                    shade = Mathf.Max(shade, CovAt(off + pp + new Vector2(sunDir.x, sunDir.z) * t - CloudWindVel * (60f * m)));
            }
            float score = through - 0.6f * shade;
            if (score > best) { best = score; pick = off; }
        }
        return pick;
    }

    public static void CloudGlobals(Texture2D weather, Vector2 drift, float altOfWorldZero, float vis)
    {
        Shader.SetGlobalTexture("_CloudWeather", weather);
        Shader.SetGlobalVector("_VC_Layer", VcLayer);
        Shader.SetGlobalVector("_VC_Scale", VcScale);
        Shader.SetGlobalVector("_VC_Light", VcLight);
        Shader.SetGlobalVector("_VC_Wind", new Vector4(drift.x, drift.y, 0f, 0f));
        Shader.SetGlobalVector("_VC_Origin", new Vector4(0f, altOfWorldZero, 0f, vis));
        Shader.SetGlobalVector("_VC_Stamp", new Vector4(StampAtCross.x, StampAtCross.y, StampRadius, 1f));   // a cumulus standing on the climb: the rocket flies through it
    }

    // The cumulus move with the launch-day wind and boil with their updrafts, on the mission's clock (sped up with
    // the climb's time warp, like everything else); their shadows on the ground move with them. The cumulus the
    // rocket flies through rides the same wind: it waits upwind while the clock holds at T-10 s (the hold can last
    // any time), then drifts in with the field so that its centre is on the climb at MET 32.5 s, as before.
    void CloudMotion()
    {
        float dTau = Time.deltaTime * (ascent != null ? ascent.Rate : 1f);
        cloudAir += CloudWindVel * dTau;
        cloudRise += UpdraftBody * dTau;
        eddyRise = Mathf.Repeat(eddyRise + (UpdraftEddies - UpdraftBody) * dTau, VcScale.y);   // the detail noise repeats every VcScale.y metres
        Vector2 w = vcDrift - cloudAir;
        Shader.SetGlobalVector("_VC_Wind", new Vector4(w.x, w.y, cloudRise, eddyRise));
        const float countStart = ClipStart - ClipZero;                                     // MET -11.0 s: START COUNTDOWN
        float met = ascent != null ? ascent.Met : Current == Phase.Counting && voice.isPlaying ? voice.time - ClipZero : countStart;
        Vector2 s = StampAtCross + CloudWindVel * (Mathf.Max(met, countStart) - StampCrossMet);
        Shader.SetGlobalVector("_VC_Stamp", new Vector4(s.x, s.y, StampRadius, 1f));
    }

    // ---------------- light: the real Sun over the pad at the planned launch time ----------------

    void ConfigureShadows()
    {
        urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (!urp) return;
        oldShadowDistance = urp.shadowDistance; oldCascades = urp.shadowCascadeCount;
        urp.shadowDistance = 700f;
        urp.shadowCascadeCount = 4;
        oldScale = urp.renderScale; oldFilter = urp.upscalingFilter; oldFsrOverride = urp.fsrOverrideSharpness; oldFsrSharpness = urp.fsrSharpness;
        urp.renderScale = 0.85f;                        // the clouds are ray-marched: keep the laptop smooth
        urp.upscalingFilter = UpscalingFilterSelection.FSR;   // back to full size with AMD FSR 1.0 (edge-aware), not a soft bilinear stretch
        urp.fsrOverrideSharpness = true; urp.fsrSharpness = 0.5f;   // mild: stronger sharpening brings out the clouds' dither
    }

    // ---------------- the sky for a place and time (pad, climb, orbit) ----------------

    static Vector3 Ecef(double latDeg, double lonDeg, out Vector3 east, out Vector3 north)
    {
        double la = latDeg * Math.PI / 180.0, lo = lonDeg * Math.PI / 180.0;
        east = new Vector3((float)-Math.Sin(lo), (float)Math.Cos(lo), 0f);
        north = new Vector3((float)(-Math.Sin(la) * Math.Cos(lo)), (float)(-Math.Sin(la) * Math.Sin(lo)), (float)Math.Cos(la));
        return new Vector3((float)(Math.Cos(la) * Math.Cos(lo)), (float)(Math.Cos(la) * Math.Sin(lo)), (float)Math.Sin(la));
    }

    // a direction in the sky (geocentric equatorial, x RA 0h, y RA 6h, z north pole) seen in the local frame
    static Vector3 EqToLocal(Vector3d q, double gmstDeg, Vector3 E, Vector3 N, Vector3 U)
    {
        double g = gmstDeg * Math.PI / 180.0, len = q.Magnitude;
        double x = q.x / len, y = q.y / len, z = q.z / len;
        var ecef = new Vector3((float)(Math.Cos(g) * x + Math.Sin(g) * y), (float)(-Math.Sin(g) * x + Math.Cos(g) * y), (float)z);
        return new Vector3(Vector3.Dot(ecef, E), Vector3.Dot(ecef, U), Vector3.Dot(ecef, N)).normalized;
    }

    // Sun, Moon, planets, stars and the Earth below for a time, a point on the Earth (geocentric degrees) and a
    // height; the 3D clouds' frame (where world (0,0) is over the cloud field) and how visible they are
    public void SetPlace(DateTime utc, float latDeg, float lonDeg, float altOfWorldZero, Vector3 horiz, float cloudVisibility)
    {
        altZero = altOfWorldZero;
        Vector3 U = Ecef(latDeg, lonDeg, out var E, out var N);
        double gmst = Ephemeris.GmstDeg(utc);
        Shader.SetGlobalVector("_EarthE", E); Shader.SetGlobalVector("_EarthN", N); Shader.SetGlobalVector("_EarthU", U);
        // local -> star map (x RA 0h, y north pole, z RA 6h)
        float g = (float)(gmst * Math.PI / 180.0);
        Vector3 Col(Vector3 local)
        {
            Vector3 ecef = E * local.x + U * local.y + N * local.z;
            Vector3 eci = new Vector3(Mathf.Cos(g) * ecef.x - Mathf.Sin(g) * ecef.y, Mathf.Sin(g) * ecef.x + Mathf.Cos(g) * ecef.y, ecef.z);
            return new Vector3(eci.x, eci.z, eci.y);
        }
        var m = Matrix4x4.identity;
        m.SetColumn(0, Col(Vector3.right)); m.SetColumn(1, Col(Vector3.up)); m.SetColumn(2, Col(Vector3.forward));
        Shader.SetGlobalMatrix("_LocalToStars", m);
        LocalToStarsM = m;
        Vector3 sd = EqToLocal(Ephemeris.SunKm(utc), gmst, E, N, U);
        Sun.transform.rotation = Quaternion.LookRotation(-sd);
        float alt = altOfWorldZero + 110f;
        float space = Mathf.InverseLerp(10000f, 60000f, alt);
        float el = Mathf.Asin(Mathf.Clamp(sd.y, -1f, 1f)) * Mathf.Rad2Deg;
        Sun.intensity = Mathf.Lerp(Mathf.Lerp(1.4f, 2.3f, Mathf.Clamp01(el / 45f)), 2.5f, space)
                      * Mathf.Lerp(overcastDim, 1f, Mathf.InverseLerp(VcLayer.y - 200f, VcLayer.y + 600f, alt));   // under an overcast, a dimmer Sun
        Sun.color = Color.Lerp(Color.Lerp(new Color(1f, 0.80f, 0.62f), new Color(1f, 0.96f, 0.91f), Mathf.Clamp01(el / 30f)), new Color(1f, 0.99f, 0.97f), space);
        Vector3 md = EqToLocal(Ephemeris.MoonKm(utc), gmst, E, N, U);
        Shader.SetGlobalVector("_MoonDir", new Vector4(md.x, md.y, md.z, (float)(1737.4 / Ephemeris.MoonKm(utc).Magnitude)));
        {
            // the Moon as a sphere (SpaceSky): its centre (km, local frame) and its real orientation (IAU), so the right face shows
            float mkm = (float)Ephemeris.MoonKm(utc).Magnitude;
            Shader.SetGlobalVector("_MoonPos", new Vector4(md.x * mkm, md.y * mkm, md.z * mkm, 1737.4f));
            Ephemeris.MoonAxes(utc, out var mw, out var ew, out var pw);
            Vector3 Ax(Vector3 w) => EqToLocal(new Vector3d(w.x, w.z, w.y), gmst, E, N, U);
            Shader.SetGlobalVector("_MoonMeridian", Ax(mw)); Shader.SetGlobalVector("_MoonEast", Ax(ew)); Shader.SetGlobalVector("_MoonPole", Ax(pw));
            Shader.SetGlobalFloat("_SunRaw", 2.5f); Shader.SetGlobalFloat("_SunVisible", 1f);
        }
        for (int k = 0; k < 5; k++)
        {
            var pd = EqToLocal(Planets.Geocentric(k, utc, out double au), gmst, E, N, U);
            float[] bright = { 0.35f, 1.8f, 0.6f, 1.1f, 0.5f };
            float[] typical = { 1.0f, 0.9f, 1.5f, 5.0f, 9.5f };
            planetDirs[k] = new Vector4(pd.x, pd.y, pd.z, bright[k] * Mathf.Clamp((float)(typical[k] * typical[k] / (au * au)), 0.3f, 3f));
        }
        planetCols[0] = new Vector4(0.85f, 0.82f, 0.78f, 1); planetCols[1] = new Vector4(1f, 0.97f, 0.88f, 1); planetCols[2] = new Vector4(1f, 0.62f, 0.42f, 1);
        planetCols[3] = new Vector4(1f, 0.93f, 0.82f, 1); planetCols[4] = new Vector4(1f, 0.90f, 0.70f, 1);
        Shader.SetGlobalVectorArray("_Planets", planetDirs);
        Shader.SetGlobalVectorArray("_PlanetColours", planetCols);
        Shader.SetGlobalVector("_VC_Origin", new Vector4(horiz.x, altOfWorldZero, horiz.z, cloudVisibility * cloudVis));
        if (ascent != null && Time.frameCount % 15 == 0 && (Current != Phase.Liftoff || outside))
        {
            float a = Mathf.Lerp(0.9f, 0.08f, space);                                            // the sky goes dark; the Earth still lights you from below
            ApplyAmbient(GradientSH(new Color(0.29f, 0.39f, 0.56f) * a, new Color(0.40f, 0.44f, 0.50f) * Mathf.Lerp(0.9f, 0.25f, space), new Color(0.17f, 0.16f, 0.12f) * Mathf.Lerp(0.9f, 1.4f, space)), "climb");
        }
        RenderSettings.fogDensity = 0.00011f * Mathf.Exp(-alt / 3000f);
    }

    // Away from the Earth (Voyage): the same sky for a spacecraft at a geocentric position (km, ICRF: x RA 0h,
    // y RA 6h, z north). The Moon is drawn as a real sphere from wherever we are (close enough to fill the sky in
    // lunar orbit), the Sun is hidden when the Earth or the Moon is in front of it (night side, the Moon's shadow).
    public float SunVisible { get; private set; } = 1f;
    public Vector3 MoonLocalKm { get; private set; }
    public Vector3 SunLocal { get; private set; }
    public Vector3 EarthLocalKm { get; private set; }
    public void SetPlaceFromSpace(DateTime utc, Vector3d sc)
    {
        double gmst = Ephemeris.GmstDeg(utc), g = gmst * Math.PI / 180.0, r = sc.Magnitude;
        double ex = Math.Cos(g) * sc.x + Math.Sin(g) * sc.y, ey = -Math.Sin(g) * sc.x + Math.Cos(g) * sc.y;
        double lat = Math.Asin(sc.z / r) * 180.0 / Math.PI, lon = Math.Atan2(ey, ex) * 180.0 / Math.PI;
        float altM = (float)((r - 6371.0) * 1000.0);
        SetPlace(utc, (float)lat, (float)lon, altM - Cam.transform.position.y, Vector3.zero, 0f);
        Vector3 U = Ecef(lat, lon, out var E, out var N);
        Vector3 Local(Vector3d q)                                                   // an ICRF vector (km) in the local frame
        {
            var ecef = new Vector3((float)(Math.Cos(g) * q.x + Math.Sin(g) * q.y), (float)(-Math.Sin(g) * q.x + Math.Cos(g) * q.y), (float)q.z);
            return new Vector3(Vector3.Dot(ecef, E), Vector3.Dot(ecef, U), Vector3.Dot(ecef, N));
        }
        Vector3d mk = Ephemeris.MoonKm(utc), sk = Ephemeris.SunKm(utc);
        var mrel = new Vector3d(mk.x - sc.x, mk.y - sc.y, mk.z - sc.z);
        var srel = new Vector3d(sk.x - sc.x, sk.y - sc.y, sk.z - sc.z);
        MoonLocalKm = Local(mrel);
        SunLocal = Local(srel).normalized;
        EarthLocalKm = new Vector3(0f, -(float)r, 0f);
        Ephemeris.MoonAxes(utc, out var mw, out var ew, out var pw);              // planner world axes (x, z, y)
        Vector3 Ax(Vector3 w) => Local(new Vector3d(w.x, w.z, w.y)).normalized;
        Shader.SetGlobalVector("_MoonPos", new Vector4(MoonLocalKm.x, MoonLocalKm.y, MoonLocalKm.z, 1737.4f));
        Shader.SetGlobalVector("_MoonMeridian", Ax(mw)); Shader.SetGlobalVector("_MoonEast", Ax(ew)); Shader.SetGlobalVector("_MoonPole", Ax(pw));
        float md = MoonLocalKm.magnitude;
        Shader.SetGlobalVector("_MoonDir", new Vector4(MoonLocalKm.x / md, MoonLocalKm.y / md, MoonLocalKm.z / md, 1737.4f / md));
        Sun.transform.rotation = Quaternion.LookRotation(-SunLocal);
        // is the Sun hidden? (its disc, 0.27 deg, against the Earth's and the Moon's)
        float Cover(Vector3 c, float rad)
        {
            float dist = c.magnitude, ang = Mathf.Acos(Mathf.Clamp(Vector3.Dot(c / dist, SunLocal), -1f, 1f)), br = Mathf.Asin(Mathf.Min(1f, rad / dist));
            return Mathf.Clamp01((ang - (br - 0.0047f)) / 0.0094f);
        }
        SunVisible = Cover(MoonLocalKm, 1737.4f) * Cover(EarthLocalKm, 6371f + 30f);
        Sun.intensity = SunDefaultIntensity * SunVisible;
        Sun.color = new Color(1f, 0.99f, 0.97f);
        Shader.SetGlobalFloat("_SunRaw", 2.5f);
        Shader.SetGlobalFloat("_SunVisible", SunVisible);
    }

    public void AscentEvent(string title, string line)
    {
        if (events.Count == 0 || !events[events.Count - 1].Contains(title)) { events.Add(title); ShowEvents(); }
        if (explain)
        {
            explain.text = line;
            explainUntil = Time.time + 9f; explainMet = ascent != null ? ascent.Met : 0f;
            explainPlate.sizeDelta = new Vector2(Mathf.Min(explain.GetPreferredValues(line).x + 44f, 1200f), 40f);
        }
    }

    // in the cabin the real Sun shines in only through the windows (the cabin structure casts its shadow);
    // daylight outside is much brighter than the cabin
    void CabinSun(bool inside)
    {
        Sun.cullingMask = inside ? ~0 : ~(1 << CabinLayer);
        if (urp) urp.shadowDistance = inside ? 20f : (ascent != null ? 400f : 700f);
        Shader.SetGlobalFloat("_SkyBoost", inside ? 1.7f : 1f);
    }

    bool outside;
    float orbitYaw = 150f, orbitPitch = -12f;

    // the outside view: circle the rocket (drag to turn), looking at its middle
    void OutsideView(bool overUi)
    {
        if (Input.GetMouseButtonDown(0) && !overUi) dragging = true;
        if (!Input.GetMouseButton(0)) dragging = false;
        if (dragging) { orbitYaw += Input.GetAxis("Mouse X") * 3f; orbitPitch = Mathf.Clamp(orbitPitch - Input.GetAxis("Mouse Y") * 3f, -80f, 80f); }
        var st = ascent.Stack;
        Vector3 focus = st.position - st.up * 72f;
        float dist = 190f;
        if (OutsideFocus != null) { var f = OutsideFocus(); focus = f.focus; dist = f.dist; }
        Quaternion rq = st.rotation * Quaternion.Euler(orbitPitch, orbitYaw, 0f);
        Vector3 shake = new Vector3(Mathf.PerlinNoise(Time.time * 9f, 1f) - 0.5f, Mathf.PerlinNoise(2f, Time.time * 9f) - 0.5f, 0f) * ascent.Shake * 0.6f;
        Cam.transform.SetPositionAndRotation(focus + rq * new Vector3(0f, 0f, -dist) + shake, Quaternion.LookRotation(focus - (focus + rq * new Vector3(0f, 0f, -dist)), st.up));
    }

    // the colour the photographed sky fades into at the horizon (its _Haze times _Exposure), as fog colour
    static Color HorizonHaze
    {
        get
        {
            var m = RenderSettings.skybox;
            if (!m || !m.HasProperty("_Haze")) return new Color(0.65f, 0.69f, 0.72f);
            return (m.GetColor("_Haze").linear * m.GetFloat("_Exposure")).gamma;
        }
    }

    void SetSun()
    {
        var utc = MissionState.LaunchUtc;
        Vector3d s = Ephemeris.SunKm(utc);
        double len = s.Magnitude, sx = s.x / len, sy = s.y / len, sz = s.z / len;
        double g = Ephemeris.GmstDeg(utc) * Math.PI / 180.0;
        double ex = Math.Cos(g) * sx + Math.Sin(g) * sy, ey = -Math.Sin(g) * sx + Math.Cos(g) * sy, ez = sz;     // Earth-fixed
        double la = PadLat * Math.PI / 180.0, lo = PadLon * Math.PI / 180.0;
        double E = -Math.Sin(lo) * ex + Math.Cos(lo) * ey;
        double N = -Math.Sin(la) * Math.Cos(lo) * ex - Math.Sin(la) * Math.Sin(lo) * ey + Math.Cos(la) * ez;
        double U = Math.Cos(la) * Math.Cos(lo) * ex + Math.Cos(la) * Math.Sin(lo) * ey + Math.Sin(la) * ez;
        var dir = new Vector3((float)E, (float)U, (float)N).normalized;
        Sun.transform.rotation = Quaternion.LookRotation(-dir);
        SunElevation = Mathf.Asin(dir.y) * Mathf.Rad2Deg;
        SunAzimuth = (Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg + 360f) % 360f;
        if (RenderSettings.skybox && RenderSettings.skybox.HasProperty("_Rotation"))      // the photo's sunlit side faces our Sun
            RenderSettings.skybox.SetFloat("_Rotation", Mathf.Repeat(0.5953f - SunAzimuth / 360f, 1f));

        float k = Mathf.Clamp01(SunElevation / 45f);
        Sun.intensity = Mathf.Lerp(1.4f, 2.3f, k);
        Sun.color = Color.Lerp(new Color(1f, 0.80f, 0.62f), new Color(1f, 0.96f, 0.91f), Mathf.Clamp01(SunElevation / 30f));
        SkyAmbient();
        RenderSettings.fogColor = HorizonHaze;                           // the same colour the sky fades into at the horizon
        Debug.Log($"FLIGHT sun over LC-39A at {utc:yyyy-MM-dd HH:mm}Z: elevation {SunElevation:0.0}, azimuth {SunAzimuth:0.0}");
    }

    // Sky light on everything in shade. The ambient colours are written straight into the ambient probe:
    // setting RenderSettings colours at runtime does not update it, which left shade almost black.
    void SkyAmbient()
    {
        float a = Mathf.Lerp(0.75f, 1.05f, Mathf.Clamp01(SunElevation / 45f));
        // linear: blue from the sky above, whiter haze toward the horizon, warm light bounced off the ground
        ApplyAmbient(GradientSH(new Color(0.29f, 0.39f, 0.56f) * a, new Color(0.40f, 0.44f, 0.50f) * a, new Color(0.17f, 0.16f, 0.12f) * a), "sky");
    }

    // the cabin keeps the light it has always had (Unity's default gradient, which the scan was tuned under)
    void CabinAmbient()
    {
        ApplyAmbient(GradientSH(new Color(0.212f, 0.227f, 0.259f).linear, new Color(0.114f, 0.125f, 0.133f).linear, new Color(0.047f, 0.043f, 0.035f).linear), "cabin");
    }

    public static SphericalHarmonicsL2 GradientSH(Color sky, Color horizon, Color ground)
    {
        var sh = new SphericalHarmonicsL2();
        const int N = 512;
        for (int i = 0; i < N; i++)                               // even directions over the sphere
        {
            float y = 1f - 2f * (i + 0.5f) / N, r = Mathf.Sqrt(1f - y * y), phi = i * 2.39996323f;
            Color c = y > 0f ? Color.Lerp(horizon, sky, Mathf.Pow(y, 0.6f)) : Color.Lerp(horizon, ground, Mathf.Pow(-y, 0.5f));
            sh.AddDirectionalLight(new Vector3(Mathf.Cos(phi) * r, y, Mathf.Sin(phi) * r), c, 2.125f / N);   // 2.125/N: measured so a uniform sky evaluates to its own colour
        }
        return sh;
    }

    public static void ApplyAmbient(SphericalHarmonicsL2 sh, string what)
    {
        RenderSettings.ambientMode = AmbientMode.Custom;
        RenderSettings.ambientProbe = sh;
        var res = new Color[3];
        sh.Evaluate(new[] { Vector3.up, Vector3.right, Vector3.down }, res);
        Debug.Log($"FLIGHT ambient ({what}): up {res[0]}, side {res[1]}, down {res[2]}");
    }

    // ---------------- the fly-in ----------------

    void PlaceOnPath(float t)
    {
        float s = Mathf.Clamp01(t / IntroLength);
        s = s * s * (3f - 2f * s) * 0.35f + s * 0.65f;
        float tt = s * IntroLength;
        int i = 0;
        while (i < path.Length - 2 && tt > path[i + 1].t) i++;
        float u = Mathf.InverseLerp(path[i].t, path[i + 1].t, tt);
        Vector3 P(int k, bool look) { k = Mathf.Clamp(k, 0, path.Length - 1); return look ? path[k].look : path[k].pos; }
        Vector3 pos = CatmullRom(P(i - 1, false), P(i, false), P(i + 1, false), P(i + 2, false), u);
        Vector3 look = CatmullRom(P(i - 1, true), P(i, true), P(i + 1, true), P(i + 2, true), u);
        Cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, Vector3.up));
        Cam.fieldOfView = Mathf.Lerp(48f, 64f, s);
        Cam.nearClipPlane = Mathf.Lerp(0.3f, 0.05f, Mathf.InverseLerp(7f, 9.5f, t));
    }

    static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    void UpdateCaption()
    {
        string text = ""; float alpha = 0f;
        foreach (var c in Captions)
            if (introTime >= c.from && introTime <= c.to)
            {
                text = c.text;
                alpha = Mathf.Min(1f, (introTime - c.from) / 0.35f, (c.to - introTime) / 0.35f);
            }
        caption.text = text;
        caption.alpha = alpha;
    }

    // ---------------- walking: swing arm 9 and the white room ----------------

    void BeginWalk()
    {
        Current = Phase.Walk;
        Vector3 s = WalkStart;
        walkPos = new Vector2(s.x, s.z);
        Vector3 f = Cam.transform.forward;
        walkYaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        walkPitch = -Mathf.Asin(Mathf.Clamp(f.y, -1f, 1f)) * Mathf.Rad2Deg;
        walkStartTime = Time.time;
        Cam.nearClipPlane = 0.05f;
        Cam.fieldOfView = 64f;
        caption.text = "W A S D  ·  DRAG TO LOOK";
        caption.alpha = 0.85f;
    }

    bool Inside(Vector2 p, Vector4 b) => p.x >= b.x && p.x <= b.y && p.y >= b.z && p.y <= b.w;
    bool Walkable(Vector2 p) => Inside(p, WalkwayBounds) || Inside(p, RoomBounds);

    void WalkUpdate(bool overUi)
    {
        // look: drag with the mouse
        if (Input.GetMouseButtonDown(0) && !overUi) { dragging = true; downMouse = Input.mousePosition; }
        if (!Input.GetMouseButton(0)) dragging = false;
        if (dragging)
        {
            walkYaw += Input.GetAxis("Mouse X") * 2.4f;
            walkPitch = Mathf.Clamp(walkPitch - Input.GetAxis("Mouse Y") * 2.4f, -75f, 75f);
        }
        // move: WASD / arrows, sliding along the railings
        Vector2 inp = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        if (inp.sqrMagnitude > 1f) inp.Normalize();
        float yr = walkYaw * Mathf.Deg2Rad;
        Vector2 fwd = new Vector2(Mathf.Sin(yr), Mathf.Cos(yr)), right = new Vector2(fwd.y, -fwd.x);
        Vector2 step = (fwd * inp.y + right * inp.x) * WalkSpeed * Time.deltaTime;
        Vector2 next = walkPos + step;
        if (Walkable(next)) walkPos = next;
        else if (Walkable(new Vector2(next.x, walkPos.y))) walkPos = new Vector2(next.x, walkPos.y);
        else if (Walkable(new Vector2(walkPos.x, next.y))) walkPos = new Vector2(walkPos.x, next.y);
        walked += step.magnitude;
        float bob = Mathf.Sin(walked * 5.2f) * 0.018f;
        Cam.transform.SetPositionAndRotation(new Vector3(walkPos.x, FloorY + EyeHeight + bob, walkPos.y), Quaternion.Euler(walkPitch, walkYaw, 0f));

        // the controls line fades once the student is moving
        if (walked > 0.6f || Time.time - walkStartTime > 7f) caption.alpha = Mathf.MoveTowards(caption.alpha, 0f, Time.deltaTime * 1.5f);

        // the hatch: close enough to reach it
        float d = Vector3.Distance(Cam.transform.position, HatchOutside);
        bool near = d < 2.1f;
        Vector3 sp = Cam.WorldToScreenPoint(HatchOutside);
        bool onScreen = sp.z > 0f;
        if (near && onScreen)
        {
            ShowReadout(sp, "COLUMBIA'S HATCH", "Click or press E to climb in.");
            bool click = Input.GetMouseButtonUp(0) && Vector2.Distance(downMouse, Input.mousePosition) < 6f;
            if (Input.GetKeyDown(KeyCode.E) || click || climbRequested) StartCoroutine(ClimbIn());
        }
        else HideReadout();
        if (Input.GetKeyDown(KeyCode.Space) || (climbRequested && !near)) StartCoroutine(ClimbIn());
    }

    // Through the hatch and down into the left couch, as the crew did.
    IEnumerator ClimbIn()
    {
        if (Current != Phase.Walk) yield break;
        Current = Phase.ClimbIn;
        climbRequested = false;
        HideReadout();
        caption.alpha = 0f;
        Vector3 eye = CommanderEye.position;
        Vector3 outward = Vector3.forward;
        Vector3 door = new Vector3(0f, FloorY + EyeHeight, RoomBounds.w + 0.4f);
        Vector3[] p =
        {
            Cam.transform.position,
            door,
            HatchOutside + outward * 0.9f + Vector3.up * 0.1f,
            HatchOutside + outward * 0.05f,
            HatchInside + HatchInward * 0.3f,
            Vector3.Lerp(HatchInside + HatchInward * 0.3f, eye, 0.55f) + Vector3.up * 0.15f,
            eye,
        };
        Quaternion[] r =
        {
            Cam.transform.rotation,
            Quaternion.LookRotation(HatchOutside - door, Vector3.up),
            Quaternion.LookRotation(HatchOutside - (HatchOutside + outward * 0.9f + Vector3.up * 0.1f), Vector3.up),
            Quaternion.LookRotation(-outward + Vector3.down * 0.25f, Vector3.up),
            Quaternion.LookRotation(HatchInward, Vector3.up),
            Quaternion.Slerp(Quaternion.LookRotation(HatchInward, Vector3.up), CommanderEye.rotation, 0.6f),
            CommanderEye.rotation,
        };
        const float T = 6.5f;
        bool hidden = false, inside = false;
        for (float t = 0; t < T; t += Time.deltaTime)
        {
            float s = Mathf.SmoothStep(0f, 1f, t / T) * (p.Length - 1);
            int i = Mathf.Min((int)s, p.Length - 2);
            float u = s - i;
            Vector3 pos = CatmullRom(p[Mathf.Max(i - 1, 0)], p[i], p[i + 1], p[Mathf.Min(i + 2, p.Length - 1)], u);
            Cam.transform.SetPositionAndRotation(pos, Quaternion.Slerp(r[i], r[i + 1], Mathf.SmoothStep(0f, 1f, u)));
            Cam.nearClipPlane = 0.03f;
            Cam.fieldOfView = Mathf.Lerp(64f, 72f, t / T);
            if (!hidden && Vector3.Distance(pos, HatchOutside) < 0.8f)
            {
                hidden = true;
                Cam.cullingMask |= 1 << CabinLayer;
                foreach (var h in HideWhileEntering) h.enabled = false;
                HatchOpening.SetActive(false);
            }
            if (!inside && s > 3.6f) { inside = true; CabinAmbient(); wind.volume = 0.08f; fans.volume = 0.10f; fans.Play(); }
            yield return null;
        }
        foreach (var h in HideWhileEntering) h.enabled = true;
        HatchClosed.SetActive(true);
        EnterCabin();
        ParkArm9();                                     // by T-5 min the crew access arm had swung fully back
    }

    // After the hatch is closed the access arm swings back to the tower (it takes about 12 s).
    void ParkArm9()
    {
        if (Arm9) Arm9.localRotation = Arm9.localRotation * Quaternion.Euler(0f, 95f, 0f);
    }

    IEnumerator RetractArm9()
    {
        yield return new WaitForSeconds(2f);
        Quaternion a = Arm9.localRotation, b = a * Quaternion.Euler(0f, 95f, 0f);
        for (float t = 0; t < 12f; t += Time.deltaTime)
        {
            Arm9.localRotation = Quaternion.Slerp(a, b, Mathf.SmoothStep(0f, 1f, t / 12f));
            yield return null;
        }
        Arm9.localRotation = b;
    }

    // ---------------- the cabin ----------------

    void EnterCabin()
    {
        Current = Phase.Cabin;
        CabinAmbient();
        CabinSun(true);
        wind.Stop();
        if (!fans.isPlaying) { fans.volume = 0.10f; fans.Play(); }
        Cam.nearClipPlane = 0.03f;
        Cam.fieldOfView = 72f;
        yaw = pitch = yawS = pitchS = 0f;
        Cam.transform.SetPositionAndRotation(CommanderEye.position, CommanderEye.rotation);
        introHud.gameObject.SetActive(false);
        cabinHud.gameObject.SetActive(true);
        subtitle.text = "COLUMBIA  ·  COMMANDER'S COUCH";
    }

    public void StartCountdown()
    {
        if (Current != Phase.Cabin) return;
        Current = Phase.Counting;
        voice.time = ClipStart;
        voice.Play();
        roarLow.Play(); roarMid.Play();
        startButton.gameObject.SetActive(false);
        clock.rectTransform.anchorMin = clock.rectTransform.anchorMax = clock.rectTransform.pivot = new Vector2(0.5f, 0f);
        clock.rectTransform.anchoredPosition = new Vector2(0f, 22f);
    }

    bool Seated => Current == Phase.Cabin || Current == Phase.Counting || Current == Phase.Liftoff;

    // the laptop's integrated graphics: when the frame rate falls the picture is drawn a little smaller (and scaled
    // back up by FSR), when there is room again it goes back up
    float fpsAvg = 60f, nextScaleCheck;
    void AdaptResolution()
    {
        float dt = Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
        fpsAvg = Mathf.Lerp(fpsAvg, 1f / dt, 1f - Mathf.Exp(-dt / 1.2f));
        if (!urp || Time.unscaledTime < nextScaleCheck || Time.timeScale == 0f) return;
        nextScaleCheck = Time.unscaledTime + 1.5f;
        if (fpsAvg < 30f && urp.renderScale > 0.62f) urp.renderScale = Mathf.Max(0.6f, urp.renderScale - 0.06f);
        else if (fpsAvg > 52f && urp.renderScale < 0.85f) urp.renderScale = Mathf.Min(0.85f, urp.renderScale + 0.03f);
    }
    public float RenderScale => urp ? urp.renderScale : 1f;

    // straight to the landing (step 7), for testing it on its own: "-landing" on the command line
    Landing landingDirect;
    void BeginLandingDirect()
    {
        Current = Phase.Liftoff;
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 2; i++)
            if (args[i] == "-lsite" && double.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double la)
                && double.TryParse(args[i + 2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double lo))
                MissionState.SetSite(la, lo);
        double sla = MissionState.HasSite ? MissionState.SiteLat : 0.674, slo = MissionState.HasSite ? MissionState.SiteLon : 23.473;
        var go = MissionModel.NextGoDay(new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc), new DateTime(2028, 12, 1, 0, 0, 0, DateTimeKind.Utc), sla, slo);
        if (go.HasValue) MissionState.LaunchUtc = MissionModel.WindowOpenUtc(go.Value);
        Debug.Log($"LANDING direct: site {sla:0.000}, {slo:0.000}, launch {MissionState.LaunchUtc:yyyy-MM-dd HH:mm} UTC");
        if (WorldRoot) WorldRoot.gameObject.SetActive(false);
        if (SaturnRoot) SaturnRoot.gameObject.SetActive(false);
        if (Arm9) Arm9.gameObject.SetActive(false);
        introHud.gameObject.SetActive(false);
        cabinHud.gameObject.SetActive(true);
        startButton.gameObject.SetActive(false);
        telemetry.gameObject.SetActive(true);
        clock.rectTransform.anchorMin = clock.rectTransform.anchorMax = clock.rectTransform.pivot = new Vector2(0.5f, 0f);
        clock.rectTransform.anchoredPosition = new Vector2(0f, 46f);
        var problems = gameObject.AddComponent<Problems>();
        problems.Begin(this, root);
        CameraOwnedByVoyage = true;
        landingDirect = gameObject.AddComponent<Landing>();
        landingDirect.Begin(this, root);
        StartCoroutine(Fade(0f, 0.8f));
    }

    void Update()
    {
        AdaptResolution();
        if (landingDirect != null)
        {
            landingDirect.Hud(clock, telemetry, warpText);
            roarLow.volume = landingDirect.RoarLow; roarMid.volume = landingDirect.RoarLow * 0.3f; wind.volume = 0f; fans.volume = 0.1f;
            if (!fans.isPlaying) { fans.Play(); roarLow.Play(); roarMid.Play(); }
            explainPlate.GetComponent<CanvasGroup>().alpha = Mathf.Clamp01((explainUntil - Time.time) / 0.8f);
            if (Input.GetKeyDown(KeyCode.Escape)) StartCoroutine(EndOfStep());
            return;
        }
        if (!Seated) return;
        if (Current == Phase.Cabin && Input.GetKeyDown(KeyCode.Space)) StartCountdown();

        float c = voice.isPlaying ? voice.time : (Current == Phase.Cabin ? ClipStart : ClipEnd);
        clock.text = ClockText(c);

        var due = new (float at, string text)[]
        {
            (ClipIgnition, "T-8.9   IGNITION SEQUENCE START"),
            (ClipZero, "T-0     HOLD-DOWN ARMS RELEASE"),
            (ClipLiftoff, $"LIFTOFF  {MissionState.LaunchUtc:HH:mm:ss} UTC"),
        };
        if (Current != Phase.Cabin)
            while (nextEvent < due.Length && c >= due[nextEvent].at) { events.Add(due[nextEvent].text); nextEvent++; ShowEvents(); }
        if (Current == Phase.Counting && c >= ClipZero) BeginClimb();

        if (voyage == null && ascent != null && ascent.Met > 712.5f)
        {
            voyage = gameObject.AddComponent<Voyage>();
            voyage.Begin(this, ascent, root);
        }
        if (voyage != null)
        {
            voyage.Hud(clock, telemetry, warpText);
            roarLow.volume = voyage.RoarLow; roarMid.volume = voyage.RoarLow * 0.3f; wind.volume = 0f;
            fans.volume = 0.12f;
            float cv = Mathf.Clamp01((explainUntil - Time.time) / 0.8f);
            explainPlate.GetComponent<CanvasGroup>().alpha = cv;
            if (Input.GetKeyDown(KeyCode.C) && voyage.AllowOutsideToggle) SetOutside(!outside);
            if (Input.GetKeyDown(KeyCode.Escape)) StartCoroutine(EndOfStep());
            return;
        }
        if (ascent != null)
        {
            if (rain && rain.isEmitting && ascent.Met > 6f) rain.Stop();          // up through the rain and out of it
            clock.text = MetText(ascent.Met);
            telemetry.text = garbled ? Garbage() : ascent.InOrbit
                ? $"IN EARTH ORBIT   ·   {ascent.AltitudeM / 1000f:0} KM   ·   28,050 KM/H"
                : $"ALT {ascent.AltitudeM / 1000f,6:0.0} KM   ·   {ascent.SpeedKmh,7:#,0} KM/H   ·   {ascent.G:0.0} G";
            warpText.text = ascent.InOrbit ? "" : ascent.Rate > 1.5f ? $"{Mathf.RoundToInt(ascent.Rate)}× SPEED" : "REAL TIME";
            // the real radio between the crew and Houston: heard whenever the clock runs at real time
            UpdateRadio();
            roarLow.volume = ascent.RoarLow; roarMid.volume = ascent.RoarMid * 0.9f;
            wind.volume = ascent.WindNoise;
            fans.volume = ascent.InOrbit ? 0.12f : 0.04f;
            float ca = Mathf.Clamp01(Mathf.Min((explainUntil - Time.time) / 0.8f, (explainMet + 14f - ascent.Met) / 3f));
            explainPlate.GetComponent<CanvasGroup>().alpha = ca;
            if (stagingGlow) stagingGlow.intensity = ascent.Flash * 5f * (outside ? 0f : 1f);
            if (Input.GetKeyDown(KeyCode.C)) SetOutside(!outside);
            if (ascent.InOrbit && Input.GetKeyDown(KeyCode.Escape)) StartCoroutine(EndOfStep());
            return;
        }
        float k = Mathf.Clamp01((c - ClipIgnition) / (ClipZero - ClipIgnition));
        float roar = Current == Phase.Cabin || c < ClipIgnition ? 0f : c < ClipZero ? 0.12f + 0.5f * k * k : 0.72f;
        roarLow.volume = roar; roarMid.volume = roar * 0.45f;
        fans.volume = Mathf.Lerp(0.10f, 0.03f, roar / 0.72f);
    }

    // T-0: the hold-down arms let go, the climb takes over (Ascent)
    void BeginClimb()
    {
        Current = Phase.Liftoff;
        ascent = gameObject.AddComponent<Ascent>();
        ascent.Begin(this, WorldRoot, SaturnRoot, CommanderEye.parent, CommanderEye);
        try { gameObject.AddComponent<Exhaust>().Begin(ascent, ascent.Stack, WorldRoot, SaturnRoot, PlumeMat, TrailMat); }
        catch (Exception e) { Debug.LogException(e); }                                       // the flame is a nice-to-have; the climb goes on
        var problems = gameObject.AddComponent<Problems>();
        problems.Begin(this, root);
        if (problems.LightningRun) { lightning = gameObject.AddComponent<Lightning>(); lightning.Begin(this, ascent, root); }
        else gameObject.AddComponent<EngineOut>().Begin(this, ascent);
        OutsideCamera = on =>
        {
            Cam.cullingMask = on ? ~(1 << CabinLayer) : ~0;
            foreach (var r in HideWhileEntering) if (r) r.enabled = on;
            Cam.nearClipPlane = on ? 0.5f : 0.03f;
            Cam.fieldOfView = on ? 45f : 72f;
            if (!on) CabinAmbient();
            CabinSun(!on);
            var dl = root.Find("Dots"); if (dl) dl.gameObject.SetActive(!on);
            subtitle.text = on ? "OUTSIDE VIEW  ·  C  BACK TO THE COUCH" : "COLUMBIA  ·  COMMANDER'S COUCH  ·  C  OUTSIDE VIEW";
            Vector3 sl = Quaternion.Inverse(ascent.Stack.rotation) * -Sun.transform.forward;
            orbitYaw = Mathf.Atan2(-sl.x, -sl.z) * Mathf.Rad2Deg + 35f; orbitPitch = -8f;
        };
        clock.rectTransform.anchoredPosition = new Vector2(0f, 46f);
        stagingGlow = new GameObject("StagingGlow").AddComponent<Light>();
        stagingGlow.type = LightType.Point; stagingGlow.color = new Color(1f, 0.55f, 0.2f); stagingGlow.range = 3.2f; stagingGlow.intensity = 0f;
        stagingGlow.cullingMask = 1 << CabinLayer; stagingGlow.shadows = LightShadows.None;
        stagingGlow.transform.SetParent(CommanderEye.parent, false);
        stagingGlow.transform.position = CommanderEye.position + CommanderEye.forward * 0.7f + CommanderEye.up * 0.3f;
        subtitle.text = "COLUMBIA  ·  COMMANDER'S COUCH  ·  C  OUTSIDE VIEW";
        telemetry.gameObject.SetActive(true);
        if (!wind.isPlaying) { wind.volume = 0f; wind.Play(); }
    }

    // ---------------- the NASA air-to-ground radio ----------------
    // The AFJ file a11_0000000ag.mp3 (731.66 s: 28,009 MPEG-1 Layer III frames at 44.1 kHz) does not start at liftoff:
    // it starts 11.3 s before it and runs 0.4% fast. Fitted to 32 calls of the AFJ transcript (rms 0.6 s), e.g.
    // "Roger. Clock." (GET 0:04) is at 15.1 s in the file, "Roger. Roll." (0:15) at 26.8 s, "Shutdown." (11:42) at
    // 710.3 s, and CAPCOM's call at 11:51 ends with its Quindar tone at 723.2 s. Played with file time = MET, every
    // call came 8.6 to 11.3 s after its event ("Shutdown." was heard after ORBIT INSERTION).
    const float RadioLead = 11.343f, RadioRate = 0.99605f, RadioFrom = 16.5f, RadioVolume = 0.9f;
    float radioGain, radioDrift;
    bool radioDone;

    static float RadioClipTime(float met) => RadioLead + RadioRate * met;

    public string RadioState => radio == null || ascent == null ? "radio -" :
        $"radio {(float)radio.timeSamples / AscentRadio.frequency:0.00} s (wants {RadioClipTime(ascent.Met):0.00}) pitch {radio.pitch:0.000} vol {radio.volume:0.00}{(radioDone ? " done" : "")}";

    void UpdateRadio()
    {
        if (AscentRadio == null || radioDone || ascent.Met < RadioFrom) return;
        if (radio == null)
        {
            radio = gameObject.AddComponent<AudioSource>();
            radio.clip = AscentRadio; radio.spatialBlend = 0f; radio.volume = 0f;
            radio.loop = false; radio.playOnAwake = false; radio.priority = 0;
        }
        if (AscentRadio.loadState != AudioDataLoadState.Loaded)                // still decompressing (in the background, see the .meta)
        {
            if (AscentRadio.loadState == AudioDataLoadState.Unloaded) AscentRadio.LoadAudioData();
            return;
        }
        float len = AscentRadio.length, target = RadioClipTime(ascent.Met);
        if (target >= len - 0.02f) { radio.Stop(); radioDone = true; return; }  // the file ends at MET 723.2 (T+12:03)
        bool live = ascent.Rate < 1.3f && !(lightning != null && lightning.Active) && !(Problems.I != null && (Problems.I.Deciding || Problems.I.Busy));   // during Apollo 12's lightning, not Apollo 11's radio
        if (live && !radio.isPlaying)
        {
            if (target > len - 2f) { radioDone = true; return; }                // it has played to its end: never restart it
            radio.time = target; radio.pitch = RadioRate; radioDrift = 0f;
            radio.Play();
        }
        else if (live)
        {
            // stay on the mission clock with no audible jumps: the drift (smoothed, the play position moves in mixer
            // blocks) is taken out by playing up to 3% faster or slower; only a stall of more than 1.5 s seeks
            float drift = target - (float)radio.timeSamples / AscentRadio.frequency;
            radioDrift = Mathf.Lerp(radioDrift, drift, 1f - Mathf.Exp(-Time.deltaTime / 0.5f));
            if (Mathf.Abs(drift) > 1.5f) { radio.time = target; radio.pitch = RadioRate; radioDrift = 0f; }
            else radio.pitch = RadioRate * (1f + (Mathf.Abs(radioDrift) < 0.04f ? 0f : Mathf.Clamp(radioDrift * 0.05f, -0.03f, 0.03f)));
        }
        // in when the clock reaches real time, out when the warp starts, and out over the file's last 0.6 s (the recording stops dead)
        radioGain = Mathf.MoveTowards(radioGain, live ? 1f : 0f, Time.deltaTime * 2.2f);
        radio.volume = RadioVolume * radioGain * Mathf.Clamp01((len - target) / 0.6f);
        if (!live && radioGain <= 0.001f && radio.isPlaying) radio.Pause();
    }

    // ---------------- hooks for Lightning and Voyage ----------------
    public void Garble(bool on) => garbled = on;
    string Garbage()
    {
        const string g = "#?%&@*0123456789";
        var sb = new System.Text.StringBuilder("ALT ");
        for (int i = 0; i < 5; i++) sb.Append(g[UnityEngine.Random.Range(0, g.Length)]);
        sb.Append(" KM   ·   ");
        for (int i = 0; i < 6; i++) sb.Append(g[UnityEngine.Random.Range(0, g.Length)]);
        sb.Append(" KM/H   ·   ?.? G");
        return sb.ToString();
    }
    // a line under the card for a while (real seconds)
    public void Explain(string line, float seconds)
    {
        if (!explain) return;
        explain.text = line;
        explainUntil = Time.time + seconds;
        explainMet = (ascent != null ? ascent.Met : 0f) + Mathf.Max(0f, seconds - 14f);
        explainPlate.sizeDelta = new Vector2(Mathf.Min(explain.GetPreferredValues(line).x + 44f, 1240f), 40f);
    }
    public void AddEvent(string title) { if (events.Count == 0 || events[events.Count - 1] != title) { events.Add(title); ShowEvents(); } }
    public void SetSubtitle(string s) => subtitle.text = s;
    public float SunDefaultIntensity = 2.5f;
    public bool CameraOwnedByVoyage;
    // a control on the panel that a problem needs: a hover dot with its name, like the instrument names
    public CabinLabel AddControl(string title, string line, Transform parent, Vector3 localPos)
    {
        var go = new GameObject("Control_" + title);
        go.transform.SetParent(parent, false); go.transform.localPosition = localPos;
        var cl = go.AddComponent<CabinLabel>(); cl.Title = title; cl.Line = line; cl.Control = true;
        labels.Add(cl);
        var dl = root.Find("Dots");
        var rt = UiKit.Rect(dl, "Dot_" + title, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(16, 16));
        UiKit.Box(UiKit.Rect(rt, "Centre", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(13, 13)), new Color(0.05f, 0.055f, 0.065f, 0.75f), 6.5f);
        var img = UiKit.Rect(rt, "Ring", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(16, 16)).gameObject.AddComponent<Image>();
        img.sprite = UiKit.RingSprite; img.raycastTarget = false;
        rt.gameObject.SetActive(false);
        dots.Add(img);
        return cl;
    }
    public string Hovered { get; private set; }                                   // the name under the mouse, for the problems
    bool autoLook = true;
    public Matrix4x4 LocalToStarsM = Matrix4x4.identity;
    // where a star (right ascension in hours, declination in degrees) is in the local frame right now
    public Vector3 StarLocal(float raHours, float decDeg)
    {
        float ra = raHours * 15f * Mathf.Deg2Rad, de = decDeg * Mathf.Deg2Rad;
        var q = new Vector3(Mathf.Cos(de) * Mathf.Cos(ra), Mathf.Sin(de), Mathf.Cos(de) * Mathf.Sin(ra));
        return LocalToStarsM.transpose.MultiplyVector(q).normalized;
    }
    public Func<(Vector3 focus, float dist)> OutsideFocus;
    public void LabelsUpdate() => UpdateLabels(EventSystem.current != null && EventSystem.current.IsPointerOverGameObject());
    public void CabinLook(Quaternion extra, Vector3 offset)
    {
        Quaternion look = CommanderEye.rotation * Quaternion.Euler(pitchS, yawS, 0f);
        Cam.transform.SetPositionAndRotation(CommanderEye.position + offset, extra * look);
    }
    public void DragLook()
    {
        bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        if (Input.GetMouseButtonDown(0) && !overUi) dragging = true;
        if (!Input.GetMouseButton(0)) dragging = false;
        if (dragging)
        {
            yaw = Mathf.Clamp(yaw + Input.GetAxis("Mouse X") * 2.6f, -100f, 100f);
            pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * 2.6f, -70f, 70f);
        }
        yawS = Mathf.Lerp(yawS, yaw, 1f - Mathf.Exp(-Time.deltaTime * 10f));
        pitchS = Mathf.Lerp(pitchS, pitch, 1f - Mathf.Exp(-Time.deltaTime * 10f));
    }
    public bool Dragging => dragging;
    public Voyage Trip => voyage;

    static string MetText(float met)
    {
        int t = Mathf.Max(0, Mathf.FloorToInt(met));
        return $"T+{t / 3600:00}:{t / 60 % 60:00}:{t % 60:00}";
    }

    public void SetOutside(bool on)
    {
        if (OutsideCamera == null) return;
        outside = on;
        if (OutsideCamera != null) OutsideCamera(on);
    }
    public Action<bool> OutsideCamera;
    public Ascent Climb => ascent;

    string ClockText(float c)
    {
        if (Current == Phase.Cabin || c < ClipZero)
        {
            int t = 10;
            foreach (var s in Spoken) if (c >= s.clip) t = s.t;
            return $"T-00:00:{t:00}";
        }
        return $"T+00:00:{Mathf.FloorToInt(c - ClipZero):00}";
    }

    void ShowEvents()
    {
        var sb = new System.Text.StringBuilder();
        int from = Mathf.Max(0, events.Count - 3);
        for (int i = from; i < events.Count; i++)
        {
            bool last = i == events.Count - 1;
            sb.Append(last ? events[i] : $"<color=#5c636b>{events[i]}</color>");
            if (!last) sb.Append('\n');
        }
        eventText.text = sb.ToString();
    }

    IEnumerator EndOfStep()
    {
        if (Current == Phase.Done) yield break;
        Current = Phase.Done;
        yield return Fade(1f, 1.6f);
        voice.Stop(); roarLow.Stop(); roarMid.Stop(); fans.Stop();
        SceneManager.LoadScene("MissionPlanner");
    }

    void LateUpdate()
    {
        CloudMotion();
        Shader.SetGlobalFloat("_CamAltitude", Cam.transform.position.y + altZero);
        Shader.SetGlobalFloat("_PixelAngle", 2f * Mathf.Tan(Cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(Screen.height, 1));
        bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        if (landingDirect != null) { landingDirect.CameraUpdate(); return; }
        if (Current == Phase.Walk) { WalkUpdate(overUi); return; }
        if (!Seated) return;

        // just after liftoff the view goes where the flight shows: the hatch window (the tower and the ground
        // falling away), then up at the clouds coming closer, then the left window. Dragging takes over.
        if (ascent != null && voyage == null && autoLook && !outside)
        {
            float met = ascent.Met;
            if (met < 15f)
            {
                Vector3 hw = Quaternion.Inverse(CommanderEye.rotation) * (HatchWindow - CommanderEye.position);
                yaw = Mathf.Atan2(hw.x, hw.z) * Mathf.Rad2Deg; pitch = -Mathf.Asin(hw.normalized.y) * Mathf.Rad2Deg;
            }
            else if (met < 40f) { yaw = -17f; pitch = -37f; }
            else { yaw = -79f; pitch = -17f; autoLook = false; }
        }
        // drag to look around the cabin
        if (Input.GetMouseButtonDown(0) && !overUi) { dragging = true; autoLook = false; }
        if (!Input.GetMouseButton(0)) dragging = false;
        if (dragging)
        {
            yaw = Mathf.Clamp(yaw + Input.GetAxis("Mouse X") * 2.6f, -100f, 100f);
            pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * 2.6f, -70f, 70f);
        }
        yawS = Mathf.Lerp(yawS, yaw, 1f - Mathf.Exp(-Time.deltaTime * 10f));
        pitchS = Mathf.Lerp(pitchS, pitch, 1f - Mathf.Exp(-Time.deltaTime * 10f));
        settle = Mathf.MoveTowards(settle, 0f, Time.deltaTime / 1.6f);
        float se = settle * settle * (3f - 2f * settle);
        Quaternion look = CommanderEye.rotation * Quaternion.Euler(pitchS - 8f * se, yawS + 6f * se, 0f);

        // vibration: from ignition, and hard once the hold-down arms let go
        float c = voice.isPlaying ? voice.time : 0f;
        float amp = 0f;
        if (ascent != null) amp = ascent.Shake;
        else if (Current != Phase.Cabin && c > ClipIgnition)
        {
            float k = Mathf.Clamp01((c - ClipIgnition) / (ClipZero - ClipIgnition));
            amp = c < ClipZero ? 0.05f + 0.45f * k * k : 0.9f + 0.3f * Mathf.Clamp01((c - ClipZero) / 2f);
        }
        float n = Time.time * 19f;
        var jitter = Quaternion.Euler((Mathf.PerlinNoise(n, 0.3f) - 0.5f) * 2f * amp, (Mathf.PerlinNoise(1.7f, n) - 0.5f) * 2f * amp, (Mathf.PerlinNoise(n, 3.1f) - 0.5f) * amp);
        Vector3 shake = new Vector3(Mathf.PerlinNoise(n, 5.5f) - 0.5f, Mathf.PerlinNoise(7.1f, n) - 0.5f, 0f) * amp * 0.006f;
        if (outside) { OutsideView(overUi); return; }
        if (voyage != null && CameraOwnedByVoyage) { voyage.CameraUpdate(); UpdateLabels(overUi); return; }
        Cam.transform.SetPositionAndRotation(CommanderEye.position + look * shake + (ascent != null ? ascent.PushWorld : Vector3.zero), look * jitter);

        UpdateLabels(overUi);
    }

    // ---------------- instrument names ----------------

    void UpdateLabels(bool overUi)
    {
        float scale = root.GetComponent<Canvas>().scaleFactor;
        Vector2 mouse = Input.mousePosition;
        if ((Cam.cullingMask & (1 << CabinLayer)) == 0)
        {
            // the camera is outside the cabin (side views, docking sight, the landing): no cabin dots
            foreach (var d in dots) d.transform.parent.gameObject.SetActive(false);
            Hovered = null; HideReadout(); return;
        }
        CabinLabel best = null; float bestD = 42f * scale; Vector2 bestP = default;
        for (int i = 0; i < labels.Count; i++)
        {
            var l = labels[i];
            Vector3 w = l.transform.position, cp = Cam.transform.position;
            Vector3 s = Cam.WorldToScreenPoint(w);
            bool visible = s.z > 0.05f && s.x > 0 && s.y > 0 && s.x < Screen.width && s.y < Screen.height;
            if (visible)
            {
                Vector3 d = w - cp; float dist = d.magnitude;
                visible = l.Control || !Physics.Raycast(cp, d / dist, dist - 0.05f, 1 << CabinLayer);
            }
            var dotRt = (RectTransform)dots[i].transform.parent;
            dotRt.gameObject.SetActive(visible && Current != Phase.Done);
            if (!visible) continue;
            dotRt.anchoredPosition = (Vector2)s / scale;
            float dm = Vector2.Distance(mouse, s);
            bool forced = forcedHover != null && l.Title == forcedHover;
            if (forced || (!dragging && !overUi && forcedHover == null && dm < bestD)) { best = l; bestD = forced ? -1f : dm; bestP = s; }
        }
        for (int i = 0; i < dots.Count; i++)
        {
            bool on = labels[i] == best;
            dots[i].color = new Color(1f, 1f, 1f, on ? 1f : 0.8f);
            dots[i].transform.parent.localScale = Vector3.one * (on ? 1.3f : 1f);
        }
        Hovered = best != null ? best.Title : null;
        if (best == null) { HideReadout(); return; }
        ShowReadout(bestP, best.Title, best.Line);
    }

    void ShowReadout(Vector2 screen, string title, string line)
    {
        float scale = root.GetComponent<Canvas>().scaleFactor;
        readoutText.text = $"<b>{title}</b>\n<color=#8f98a1>{line}</color>";
        var size = readoutText.GetPreferredValues(readoutText.text, 330f, 0f);
        readout.sizeDelta = new Vector2(Mathf.Min(size.x, 330f) + 28f, size.y + 22f);
        Vector2 p = screen / scale;
        p = new Vector2(Mathf.Clamp(p.x, 70f, root.rect.width - 70f), Mathf.Clamp(p.y, 110f, root.rect.height - 70f));   // keep it on screen
        float x = p.x + 30f + readout.sizeDelta.x > root.rect.width - 16f ? p.x - 30f - readout.sizeDelta.x : p.x + 30f;
        readout.anchoredPosition = new Vector2(x, p.y + readout.sizeDelta.y * 0.5f);
        reticle.anchoredPosition = p;
        readout.gameObject.SetActive(true);
        reticle.gameObject.SetActive(true);
    }

    void HideReadout()
    {
        readout.gameObject.SetActive(false);
        reticle.gameObject.SetActive(false);
    }

    // ---------------- HUD (same monospace look as the planner) ----------------

    void BuildHud()
    {
        var go = new GameObject("FlightCanvas", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 1f;
        go.AddComponent<GraphicRaycaster>();
        root = (RectTransform)go.transform;

        var dotLayer = Full("Dots");
        foreach (var l in labels)
        {
            var rt = UiKit.Rect(dotLayer, "Dot_" + l.Title, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(16, 16));
            UiKit.Box(UiKit.Rect(rt, "Centre", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(13, 13)), new Color(0.05f, 0.055f, 0.065f, 0.75f), 6.5f);
            var img = UiKit.Rect(rt, "Ring", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(16, 16)).gameObject.AddComponent<Image>();
            img.sprite = UiKit.RingSprite; img.raycastTarget = false;
            rt.gameObject.SetActive(false);
            dots.Add(img);
        }

        UiKit.Shadow(UiKit.Label(root, "APOLLO 11", 15, UiKit.Text, new Vector2(0, 1), new Vector2(0, 1), new Vector2(36, -30), new Vector2(300, 24), bold: true, spacing: 6));
        subtitle = UiKit.Label(root, $"{MissionState.LaunchUtc:dd MMM yyyy}  ·  {MissionState.LaunchUtc:HH:mm} UTC".ToUpperInvariant(), 12, UiKit.Text,
            new Vector2(0, 1), new Vector2(0, 1), new Vector2(36, -56), new Vector2(700, 22), spacing: 2);
        UiKit.Shadow(subtitle);

        introHud = Full("Intro");
        caption = UiKit.Label(introHud, "", 14, UiKit.Text, new Vector2(0, 0), new Vector2(0, 0), new Vector2(36, 34), new Vector2(1000, 24), spacing: 4);
        UiKit.Shadow(caption);

        cabinHud = Full("Cabin");
        countCard = UiKit.Rect(cabinHud, "CountCard", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 28), new Vector2(520, 176));
        UiKit.Box(countCard, UiKit.Card, 18);
        eventText = UiKit.Label(countCard, "", 13, UiKit.Text, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -20), new Vector2(460, 60), TextAlignmentOptions.Bottom, spacing: 2);
        eventText.enableWordWrapping = false;
        clock = UiKit.Label(countCard, "T-00:00:10", 44, UiKit.Text, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -24), new Vector2(480, 60), TextAlignmentOptions.Center, true, 4);
        startButton = UiKit.Rect(countCard, "StartCountdown", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 26), new Vector2(472, 52));
        UiKit.Button(startButton, UiKit.Selected, 12, StartCountdown);
        UiKit.Label(startButton, "START COUNTDOWN", 15, UiKit.Ink, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(400, 30), TextAlignmentOptions.Center, true, 5);
        telemetry = UiKit.Label(countCard, "", 14, UiKit.Text, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 20), new Vector2(500, 24), TextAlignmentOptions.Center, false, 2);
        telemetry.gameObject.SetActive(false);
        explainPlate = UiKit.Rect(cabinHud, "Caption", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 216), new Vector2(900, 40));
        UiKit.Box(explainPlate, UiKit.Card, 12);
        explainPlate.gameObject.AddComponent<CanvasGroup>().alpha = 0f;
        explain = UiKit.Label(explainPlate, "", 15, UiKit.Text, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1180, 26), TextAlignmentOptions.Center, false, 1);
        warpText = UiKit.Label(countCard, "", 11, UiKit.Text, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-18, -12), new Vector2(200, 18), TextAlignmentOptions.Right, true, 3);
        cabinHud.gameObject.SetActive(false);

        reticle = UiKit.Rect(root, "Reticle", Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(34, 34));
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
            {
                UiKit.Box(UiKit.Rect(reticle, "H", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(sx * 13f, sy * 17f), new Vector2(8, 1.5f)), UiKit.Text, 0);
                UiKit.Box(UiKit.Rect(reticle, "V", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(sx * 17f, sy * 13f), new Vector2(1.5f, 8)), UiKit.Text, 0);
            }
        reticle.gameObject.SetActive(false);
        readout = UiKit.Rect(root, "Readout", Vector2.zero, new Vector2(0, 1), Vector2.zero, new Vector2(320, 80));
        UiKit.Box(readout, UiKit.Card, 10);
        readoutText = UiKit.Label(readout, "", 13, UiKit.Text, new Vector2(0, 1), new Vector2(0, 1), new Vector2(14, -11), new Vector2(330, 80), TextAlignmentOptions.TopLeft, spacing: 1);
        readoutText.lineSpacing = 12;
        readout.gameObject.SetActive(false);

        var f = UiKit.Rect(root, "Fade", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(6000, 6000));
        fade = UiKit.Box(f, Color.black, 0);
    }

    RectTransform Full(string name)
    {
        var rt = UiKit.Rect(root, name, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.sizeDelta = Vector2.zero;
        return rt;
    }

    // A newer fade always wins over an older one.
    IEnumerator Fade(float to, float seconds)
    {
        int me = ++fadeToken;
        float from = fade.color.a;
        for (float t = 0; t < 1f; t += Time.deltaTime / seconds)
        {
            if (me != fadeToken) yield break;
            fade.color = new Color(0, 0, 0, Mathf.Lerp(from, to, t));
            yield return null;
        }
        if (me != fadeToken) yield break;
        fade.color = new Color(0, 0, 0, to);
        fade.raycastTarget = to > 0.5f;
    }

    // ---------------- hooks for the self-test ----------------

    public void HoldIntro(bool hold) => introHold = hold;
    public void SetIntroTime(float t) { introTime = t; PlaceOnPath(t); }
    public void PlaceCamera(Vector3 pos, Vector3 target, float fov)
    {
        Cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(target - pos, Vector3.up));
        Cam.fieldOfView = fov;
    }
    public void ForceHover(string title) => forcedHover = title;
    public void Look(float yawDeg, float pitchDeg) { yaw = yawDeg; pitch = pitchDeg; }
    public void LookAtWorld(Vector3 p)
    {
        Vector3 d = Quaternion.Inverse(CommanderEye.rotation) * (p - CommanderEye.position);
        Look(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -Mathf.Asin(d.normalized.y) * Mathf.Rad2Deg);
    }
    public void WalkTo(float z, float yawDeg, float pitchDeg) { walkPos = new Vector2(0f, z); walkYaw = yawDeg; walkPitch = pitchDeg; walked = 1f; }
    public void RequestClimb() => climbRequested = true;
}
