// Step 7: the landing. Apollo 11's powered descent (12 min 35 s, 102:33:05 to 102:45:40 GET) in about 45 seconds of play,
// at the point the student picked in the planner (LunarTerrain: the real LOLA ground there, mare or highland):
//   - a checklist choice before the descent (the rendezvous radar switch) that decides what can go wrong later
//   - braking (P63): Eagle flies feet first, windows up; pitchover at about 2 km brings the ground into the window
//   - approach (P64): the computer aims at the picked point; the window's landing point scale (LPD) shows where; it is a
//     fresh crater with a boulder field, as Apollo 11's target was
//   - the last 150 m (P66) flown by the student: W/S descent rate, arrows to move, fuel running out with the real 60 and
//     30 second calls; the probes touch (CONTACT LIGHT), the engine stops, the real 1969 radio: "The Eagle has landed"
// One real problem per run, from what the student chose (Apollo 11's 1202 alarms if the radar switch was left in AUTO;
// otherwise Apollo 14's fake abort signal or its landing radar that would not lock, or Apollo 12's blinding dust, weighted
// by the site). Wrong choices are played out, explained, and rewound to the choice. No score: a summary of what happened.
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class Landing : MonoBehaviour
{
    // ---------------- Apollo 11's descent: seconds after PDI ignition (102:33:05 GET) ----------------
    const double PdiGet = 369185.0;
    const float HighGate = 507f, Pitchover = 470f, LowGate = 620f;
    const float AlarmAt = 321f, GoLandingAt = 543f, Alarm1201At = 559f, RadarAt = 180f, LpdAt = 566f;
    const float RadarRule = 3048f;                                  // 10,000 ft: no landing radar data by here means abort
    const float LowGateAlt = 150f, LowGateSpeed = 12f, LowGateSink = 4f, LowGateRange = 440f;
    const float Probe = 1.7f;                                       // the contact probes under three footpads (5.6 ft)
    const float MaxSink = 3.05f, MaxSide = 1.22f, MaxSlope = 12f, MaxRock = 0.6f;   // the LM's limits: 10 ft/s, 4 ft/s, 12 deg
    static readonly Vector3 EyeLocal = new Vector3(-0.42f, 5.05f, 0.55f);         // the commander, standing at the left window

    public enum Stage { Idle, Setup, PrePdi, Descent, Manual, Film, Done }
    public enum Trouble { None, Alarm1202, Radar, FakeAbort, Dust }
    enum View { Window, Outside, Scripted }
    public Stage Now { get; private set; } = Stage.Idle;
    public bool Active => Now != Stage.Idle;
    public bool Auto;
    public float RoarLow { get; private set; }
    public Trouble Problem { get; private set; }
    public float T => t;

    FlightDirector fd;
    RectTransform hud;
    LunarTerrain terrain;
    RadioCue radio;
    Transform lm, lmModel, ascent, plume;
    Material plumeMat;
    ParticleSystem dustPs;
    readonly List<Renderer> lmRenderers = new List<Renderer>();
    readonly List<Transform> probes = new List<Transform>();

    double lat, lon;
    float R => terrain.Radius;
    Vector3 C => new Vector3(0f, -R, 0f);
    Vector3 a3 = Vector3.right;                                     // uprange, level at the site (the Sun is behind the crew)
    float t;                                                        // seconds since PDI ignition
    float rate;
    bool holding, anim, rrAuto, radarOk = true, radarFixing, manualBriefed, contact, fuelOut;
    readonly HashSet<string> fired = new HashSet<string>();

    // the flown state (site frame, m and m/s)
    Vector3 pos, vel, upNow = Vector3.up;
    Quaternion lmSite = Quaternion.identity, siteRot = Quaternion.identity;
    float fuel, bingoLeft, dustLow, sinkCmd, clockExtra, nextDbg;
    Vector2 meshCentre;
    (Vector3 pos, Vector3 vel, float fuel) lowGate;
    (Vector3 pos, Vector3 vel, float fuel, float dust) dustSnap;
    Vector2 autoSpot;

    View view = View.Outside;
    float dragYaw, dragPitch, orbitYaw = 25f, orbitPitch = 12f, orbitDist = 34f;
    Vector3 scriptPos, scriptLook;                                  // site frame, relative to Eagle's feet
    float scriptFov = 50f;

    readonly List<string> story = new List<string>();
    float touchSink, touchSide, touchSlope, touchFuel;

    // ---------------- setup ----------------

    public void Begin(FlightDirector director, RectTransform hudRoot)
    {
        fd = director; hud = hudRoot;
        lat = MissionState.HasSite ? MissionState.SiteLat : 0.674;
        lon = MissionState.HasSite ? MissionState.SiteLon : 23.473;
        terrain = new GameObject("LunarSite").AddComponent<LunarTerrain>();
        var rock = Resources.Load<Material>("Moon/LunarRock");
        if (rock == null) { rock = new Material(Shader.Find("Universal Render Pipeline/Lit")); rock.color = new Color(0.42f, 0.41f, 0.4f); rock.SetFloat("_Smoothness", 0f); }
        terrain.Begin(lat, lon, Resources.Load<Material>("Moon/LunarSurface"), rock);
        BuildLm();
        BuildUi();
        radio = RadioCue.On(gameObject); radio.Preload();
        fd.CameraOwnedByVoyage = true;
        fd.SetOutside(false);
        fd.Cam.cullingMask = ~(1 << 8);
        fd.Cam.farClipPlane = 450000f;
        FlightDirector.ApplyAmbient(FlightDirector.GradientSH(new Color(0.01f, 0.01f, 0.012f), new Color(0.05f, 0.05f, 0.05f), new Color(0.16f, 0.155f, 0.15f)), "moon");
        foreach (var a in Environment.GetCommandLineArgs()) if (a == "-autoland") Auto = true;
        Shader.SetGlobalFloat("_MoonGain", terrain.Gain);          // the eye adapted to the bright ground (as LunarSurface's _Gain)
        Now = Stage.Setup;
        fd.AddEvent("UNDOCKING");
        Problems.I.Pick("BEFORE THE DESCENT  ·  CHECKLIST",
            "Eagle has undocked from Columbia and is about to start down. Eagle's rendezvous radar finds Columbia again if the landing has to be called off. Where do you leave its switch?",
            new[] { "AUTO TRACK: the radar keeps locked on Columbia all the way down",
                    "LGC: the radar is left to the computer; it does not track during the landing",
                    "Apollo 11's checklist left it in a crew position, ready for an abort" },
            new[] { "AUTO TRACK: KEEP COLUMBIA LOCKED, READY FOR AN ABORT", "LGC: LET THE COMPUTER HANDLE IT" }, 20f,
            i => { rrAuto = i == 0; story.Add(rrAuto ? "Rendezvous radar left tracking Columbia (as in 1969)" : "Rendezvous radar left to the computer"); AfterSetup(); }, 0);
    }

    void AfterSetup()
    {
        ChooseTrouble();
        Debug.Log($"LANDING site {lat:0.000}, {lon:0.000} ({MissionModel.PlaceName(lat, lon)}), problem {Problem}");
        StartCoroutine(WhenReady(() =>
        {
            if (Problem == Trouble.FakeAbort) { Now = Stage.PrePdi; t = -40f; AskFakeAbort(); }
            else StartPdi();
        }));
    }

    IEnumerator WhenReady(Action then)
    {
        if (!terrain.Ready) fd.Explain("Preparing the ground at your landing site...", 10f);
        while (!terrain.Ready) yield return null;
        // the approach comes in with the Sun behind the crew (morning: from the east)
        var utc = MissionState.LaunchUtc.AddSeconds(PdiGet + LowGate);
        Vector3 s = SunInSite(utc);
        Vector2 h = new Vector2(s.x, s.z);
        a3 = h.sqrMagnitude > 1e-6f ? new Vector3(h.x, 0f, h.y).normalized : Vector3.right;
        BuildRangeTable();
        PlanAutoSpot();
        then();
    }

    void ChooseTrouble()
    {
        string force = null;
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-lproblem") force = args[i + 1];
        if (force == "alarm") { Problem = Trouble.Alarm1202; rrAuto = true; return; }
        if (force == "radar") { Problem = Trouble.Radar; return; }
        if (force == "abort") { Problem = Trouble.FakeAbort; return; }
        if (force == "dust") { Problem = Trouble.Dust; return; }
        if (rrAuto) { Problem = Trouble.Alarm1202; return; }
        float wRadar = terrain.Mare ? 0.3f : 0.5f, wAbort = 0.3f, wDust = terrain.Mare ? 0.4f : 0.2f;
        double x = new System.Random(Environment.TickCount).NextDouble() * (wRadar + wAbort + wDust);
        Problem = x < wRadar ? Trouble.Radar : x < wRadar + wAbort ? Trouble.FakeAbort : Trouble.Dust;
    }

    void OnDestroy() => Shader.SetGlobalFloat("_MoonGain", 0f);

    // ---------------- the descent profile (Apollo 11, smoothed) ----------------

    static float SpeedAt(float tt) => tt < 0f ? 1690f : tt < HighGate ? 1690f - 1540f * tt / HighGate
        : tt < LowGate ? 150f - (150f - LowGateSpeed) * (tt - HighGate) / (LowGate - HighGate) : LowGateSpeed;
    static float AltAt(float tt) => tt < 0f ? 15000f : tt < HighGate ? 2200f + 12800f * (1f - (tt / HighGate) * (tt / HighGate))
        : tt < LowGate ? LowGateAlt + (2200f - LowGateAlt) * Mathf.Pow(1f - (tt - HighGate) / (LowGate - HighGate), 1.35f) : LowGateAlt;
    static float PitchAt(float tt) => tt < Pitchover ? 72f : tt < Pitchover + 40f ? Mathf.Lerp(72f, 44f, Mathf.SmoothStep(0f, 1f, (tt - Pitchover) / 40f))
        : tt < LowGate ? Mathf.Lerp(44f, 12f, (tt - Pitchover - 40f) / (LowGate - Pitchover - 40f)) : 12f;

    float[] rangeTab;
    void BuildRangeTable()
    {
        int n = (int)LowGate + 1;
        rangeTab = new float[n];
        rangeTab[n - 1] = LowGateRange;
        for (int i = n - 2; i >= 0; i--) rangeTab[i] = rangeTab[i + 1] + 0.5f * (SpeedAt(i) + SpeedAt(i + 1));
    }
    float RangeAt(float tt)
    {
        if (tt <= 0f) return rangeTab[0] + 1690f * -tt;
        if (tt >= LowGate) return LowGateRange;
        int i = (int)tt; float f = tt - i;
        return Mathf.Lerp(rangeTab[i], rangeTab[Mathf.Min(i + 1, rangeTab.Length - 1)], f);
    }

    // where Eagle is at a moment of the profile: on a great circle through the target, uprange of it
    void ProfilePose(float tt, out Vector3 p, out Vector3 v, out Vector3 radial, out Vector3 back)
    {
        float th = RangeAt(tt) / R;
        radial = Mathf.Cos(th) * Vector3.up + Mathf.Sin(th) * a3;
        back = -Mathf.Sin(th) * Vector3.up + Mathf.Cos(th) * a3;
        float w = Mathf.InverseLerp(400f, HighGate, tt);
        Vector3 g = C + radial * R;
        float hg = w > 0f ? terrain.HeightAt(g.x, g.z, false) * w : 0f;
        p = C + radial * (R + AltAt(tt) + hg);
        v = -back * SpeedAt(tt) + radial * (AltAt(tt + 0.5f) - AltAt(tt - 0.5f));
    }

    float AltAbove(Vector3 p)
    {
        Vector3 d = p - C; float r = d.magnitude;
        Vector3 g = C + d / r * R;
        return r - R - terrain.HeightAt(g.x, g.z, false);
    }

    Vector3 SunInSite(DateTime utc)
    {
        Ephemeris.MoonAxes(utc, out var m, out var e, out var pz);
        Axes(m, e, pz, out var upW, out var eaW, out var noW);
        Vector3 s = Ephemeris.SunDirWorld(utc);                     // from the Earth; the Sun is far enough
        return new Vector3(Vector3.Dot(s, eaW), Vector3.Dot(s, upW), Vector3.Dot(s, noW));
    }

    void Axes(Vector3 m, Vector3 e, Vector3 p, out Vector3 up, out Vector3 east, out Vector3 north)
    {
        double la = lat * Math.PI / 180.0, lo = lon * Math.PI / 180.0;
        Vector3 W(double x, double y, double z) => m * (float)x + p * (float)y + e * (float)z;
        up = W(Math.Cos(la) * Math.Cos(lo), Math.Sin(la), Math.Cos(la) * Math.Sin(lo)).normalized;
        east = W(-Math.Sin(lo), 0, Math.Cos(lo)).normalized;
        north = W(-Math.Sin(la) * Math.Cos(lo), Math.Cos(la), -Math.Sin(la) * Math.Sin(lo)).normalized;
    }

    // ---------------- the sky and the ground around Eagle (Eagle stays at the world's origin) ----------------

    void PlaceWorld()
    {
        var utc = MissionState.LaunchUtc.AddSeconds(PdiGet + t);
        Ephemeris.MoonAxes(utc, out var m, out var e, out var pz);
        Axes(m, e, pz, out var upW, out var eaW, out var noW);
        Vector3d I(Vector3 w) => new Vector3d(w.x, w.z, w.y);
        Vector3d upI = I(upW), eaI = I(eaW), noI = I(noW);
        var mc = Ephemeris.MoonKm(utc);
        double ku = (R + pos.y) / 1000.0, ke = pos.x / 1000.0, kn = pos.z / 1000.0;
        var sc = new Vector3d(mc.x + upI.x * ku + eaI.x * ke + noI.x * kn, mc.y + upI.y * ku + eaI.y * ke + noI.y * kn, mc.z + upI.z * ku + eaI.z * ke + noI.z * kn);
        fd.SetPlaceFromSpace(utc, sc);
        // the same local frame FlightDirector uses (east, up, north of the point on the Earth below us)
        double g = Ephemeris.GmstDeg(utc) * Math.PI / 180.0, r = sc.Magnitude;
        double ex = Math.Cos(g) * sc.x + Math.Sin(g) * sc.y, ey = -Math.Sin(g) * sc.x + Math.Cos(g) * sc.y;
        double gla = Math.Asin(sc.z / r), glo = Math.Atan2(ey, ex);
        var E = new Vector3((float)-Math.Sin(glo), (float)Math.Cos(glo), 0f);
        var N = new Vector3((float)(-Math.Sin(gla) * Math.Cos(glo)), (float)(-Math.Sin(gla) * Math.Sin(glo)), (float)Math.Cos(gla));
        var U = new Vector3((float)(Math.Cos(gla) * Math.Cos(glo)), (float)(Math.Cos(gla) * Math.Sin(glo)), (float)Math.Sin(gla));
        Vector3 Local(Vector3d q)
        {
            var ec = new Vector3((float)(Math.Cos(g) * q.x + Math.Sin(g) * q.y), (float)(-Math.Sin(g) * q.x + Math.Cos(g) * q.y), (float)q.z);
            return new Vector3(Vector3.Dot(ec, E), Vector3.Dot(ec, U), Vector3.Dot(ec, N));
        }
        siteRot = Quaternion.LookRotation(Local(noI).normalized, Local(upI).normalized);
        // the sky's Moon: a sphere through this site's ground level, so the real ground meets it at the horizon
        Vector3 cw = siteRot * (C - pos) / 1000f;
        float cm = cw.magnitude;
        Shader.SetGlobalVector("_MoonPos", new Vector4(cw.x, cw.y, cw.z, R / 1000f));
        Shader.SetGlobalVector("_MoonDir", new Vector4(cw.x / cm, cw.y / cm, cw.z / cm, 1737.4f / cm));
        float da = AltAbove(pos);
        float dustK = DustAmount(da);
        terrain.Place(siteRot, siteRot * -pos, meshCentre, new Vector4(pos.x, Mathf.Max(6f, da * 2.2f + 8f), pos.z, dustK));
        lm.SetPositionAndRotation(Vector3.zero, siteRot * lmSite);
        if (dustPs)
        {
            dustPs.transform.localPosition = new Vector3(pos.x, terrain.HeightAt(pos.x, pos.z, true) + 0.3f, pos.z);
            var em = dustPs.emission; em.rateOverTime = dustK * 900f;
        }
    }

    float DustAmount(float alt)
    {
        if (Now != Stage.Manual && !(Now == Stage.Film && !contact)) return 0f;
        float k = Mathf.Clamp01(1f - alt / (Problem == Trouble.Dust ? 34f : 22f));
        float site = terrain.Mare ? 0.75f : 0.5f;
        if (Problem == Trouble.Dust) site = 1.15f;
        return Mathf.Clamp01(k * k * site * (1f + dustLow * 0.08f));
    }

    // ---------------- every frame ----------------

    void Update()
    {
        if (Now == Stage.Setup && terrain != null && terrain.Ready) { pos = new Vector3(0f, 15000f, 0f); PlaceWorld(); }
        if (Now == Stage.Idle || Now == Stage.Setup || terrain == null || !terrain.Ready || rangeTab == null) { RoarLow = 0f; return; }
        float dt = Time.deltaTime;
        bool deciding = Problems.I != null && Problems.I.Deciding;
        if (Input.GetKeyDown(KeyCode.C) && !anim && (Now == Stage.Descent || Now == Stage.Manual || Now == Stage.PrePdi))
            view = view == View.Window ? View.Outside : View.Window;
        switch (Now)
        {
            case Stage.PrePdi:
                ProfilePose(t, out pos, out vel, out var r0, out var b0);
                lmSite = Quaternion.LookRotation(r0 * Mathf.Sin(72f * Mathf.Deg2Rad) - b0 * Mathf.Cos(72f * Mathf.Deg2Rad), b0 * Mathf.Sin(72f * Mathf.Deg2Rad) + r0 * Mathf.Cos(72f * Mathf.Deg2Rad));
                if (!deciding && !anim) DskyUpdate(dt);
                RoarLow = 0f;
                break;
            case Stage.Descent:
                if (!deciding && !anim && !holding) DescentUpdate(dt);
                if (!anim) DescentPose();
                RoarLow = anim ? 0f : 0.05f;
                break;
            case Stage.Manual:
                if (!deciding && !holding) clockExtra += dt;
                if (!deciding && !anim && !holding) ManualUpdate(dt);
                RoarLow = anim || fuelOut ? 0f : 0.06f;
                break;
            case Stage.Film:
                clockExtra += dt;
                RoarLow = contact ? 0f : 0.05f;
                break;
        }
        PlaceWorld();
        Plume(Now == Stage.Descent || (Now == Stage.Manual && !fuelOut) || (Now == Stage.Film && !contact));
        UiUpdate();
    }

    void DescentUpdate(float dt)
    {
        // the time-lapse: fast while braking (windows up, only sky to see), slower once the ground is in the window
        float r = t < Pitchover - 15f ? 45f : t < LowGate - 25f ? 8f : 4f;
        if (radarFixing) r = 11f;
        rate = r;
        float before = t;
        t = Mathf.Min(t + dt * r, LowGate);
        Events(before, t);
        if (radarFixing && !radarOk && AltAt(t) < RadarRule) { RadarLost(); return; }
        if (t >= LowGate && !Problems.I.Deciding && !anim) BeginManual();
    }

    void DescentPose()
    {
        ProfilePose(t, out pos, out vel, out var radial, out var back);
        float p = PitchAt(t) * Mathf.Deg2Rad;
        Vector3 u = back * Mathf.Sin(p) + radial * Mathf.Cos(p);
        Vector3 f = radial * Mathf.Sin(p) - back * Mathf.Cos(p);
        lmSite = Quaternion.LookRotation(f, u);
        upNow = radial;
    }

    bool Cross(float before, float now, float at, string key)
    {
        if (before < at && now >= at && !fired.Contains(key)) { fired.Add(key); return true; }
        return false;
    }

    void Events(float before, float now)
    {
        if (Cross(before, now, 2f, "pdi")) fd.AddEvent("POWERED DESCENT");
        if (Problem == Trouble.Radar && Cross(before, now, RadarAt, "radar")) AskRadar();
        if (Problem == Trouble.Alarm1202 && Cross(before, now, AlarmAt, "1202")) Ask1202();
        if (Cross(before, now, Pitchover, "pitch"))
        {
            view = View.Window; dragYaw = dragPitch = 0f;
            fd.AddEvent("PITCHOVER");
            fd.Explain("PITCHOVER · 2 km up: Eagle tilts upright and the ground swings into the window. C: outside view.", 7f);
        }
        if (Cross(before, now, GoLandingAt, "golanding")) { radio.Again("l_go_landing"); radio.Say("l_go_landing"); fd.Explain("CAPCOM: \"Eagle, Houston. You're GO for landing.\"", 5f); }
        if (Problem == Trouble.Alarm1202 && Cross(before, now, Alarm1201At, "1201")) { radio.Again("l_1201"); radio.Say("l_1201"); fd.Explain("1201: the computer is busy again. Houston: \"Same type. We're GO.\"", 5f); }
        if (Cross(before, now, LpdAt, "lpd"))
            fd.Explain("Look where the computer is taking you: the mark on the window's scale. It is the point you picked, and there is a crater full of boulders.", 9f);
    }

    // ---------------- the last 150 m: flown by hand (P66) ----------------

    // the student's fuel load (the planner's slider): seconds to BINGO at 150 m, and how a heavier Eagle answers the controls
    float FuelAtLowGate => Mathf.Max(MissionModel.HoverSeconds(MissionState.Fuel), 20f);
    float Agility => Mathf.Clamp(MissionModel.Apollo11EagleKg / MissionModel.EagleFuelKg(MissionState.Fuel), 0.85f, 1.05f);

    void BeginManual()
    {
        Now = Stage.Manual;
        ProfilePose(LowGate, out pos, out var _, out var radial, out var back);
        vel = -back * LowGateSpeed - radial * LowGateSink;
        fuel = FuelAtLowGate; bingoLeft = 20f; dustLow = 0f; fuelOut = false; contact = false;
        lowGate = (pos, vel, fuel);
        fired.Remove("f60"); fired.Remove("f30"); fired.Remove("bingo");
        view = View.Window; dragYaw = 0f; dragPitch = 0f;
        fd.AddEvent("P66 · BY HAND");
        if (!manualBriefed)
        {
            manualBriefed = true; holding = true;
            Problems.I.Info("P66  ·  YOU FLY NOW",
                "150 m up, the computer is still taking Eagle into the crater and its boulders. Fly past them and land on flat, clear ground.\n\n"
              + "<b>S</b>  down faster      <b>W</b>  slower or climb      let go: Eagle comes down gently by itself\n<b>ARROWS</b>  move forward, back, left, right (Eagle tilts to move)\n<b>C</b>  outside view\n\n"
              + $"Land gently: under {MaxSink:0} m/s down and almost no sideways speed. Ground steeper than {MaxSlope:0}° or a boulder under Eagle tips it over. "
              + $"Fuel: {fuel:0} seconds to BINGO; then 20 more to land or abort.",
                () => holding = false);
        }
    }

    void ManualUpdate(float dt)
    {
        Vector3 radial = (pos - C).normalized;
        Vector3 heading = Vector3.ProjectOnPlane(-a3, radial).normalized;
        Vector3 right = Vector3.Cross(radial, heading);
        float sink = Vector3.Dot(vel, radial), vF = Vector3.Dot(vel, heading), vL = Vector3.Dot(vel, right);
        float alt = AltAbove(pos);
        float aF = 0f, aL = 0f, dSink = 0f;
        if (Auto || autoFly)
        {
            Vector3 to = new Vector3(autoSpot.x, 0f, autoSpot.y) - pos;
            float dF = Vector3.Dot(to, heading), dL = Vector3.Dot(to, right);
            float wantF = Mathf.Clamp(dF * 0.12f, -7f, 7f), wantL = Mathf.Clamp(dL * 0.12f, -5f, 5f);
            aF = Mathf.Clamp((wantF - vF) * 0.8f, -1f, 1f); aL = Mathf.Clamp((wantL - vL) * 0.8f, -1f, 1f);
            float near = new Vector2(dF, dL).magnitude;
            float wantSink = near > 40f ? (alt > 70f ? -3f : alt > 50f ? -1f : 0.5f) : alt > 25f ? -2.5f : alt > 8f ? (near > 8f ? 0f : -1.2f) : -0.7f;
            dSink = Mathf.Clamp((wantSink - sink) * 1.5f, -1f, 1f);
        }
        else
        {
            if (Input.GetKey(KeyCode.W)) dSink += 1f;
            if (Input.GetKey(KeyCode.S)) dSink -= 1f;
            // hands off W/S: Eagle settles back to a gentle descent on its own (about 3 m/s high up, 1 m/s near the
            // ground), so letting go always brings it down; W slows it or climbs, S comes down faster
            if (dSink == 0f)
            {
                float want = alt > 40f ? -3f : alt > 12f ? -1.6f : -0.9f;
                dSink = Mathf.Clamp((want - sink) * 0.9f, -1f, 1f);
            }
            if (Input.GetKey(KeyCode.UpArrow)) aF += 1f;
            if (Input.GetKey(KeyCode.DownArrow)) aF -= 1f;
            if (Input.GetKey(KeyCode.RightArrow)) aL += 1f;
            if (Input.GetKey(KeyCode.LeftArrow)) aL -= 1f;
        }
        sink = Mathf.Clamp(sink + dSink * 1.1f * Agility * dt, -7f, 2.5f);
        vF += aF * 0.9f * Agility * dt; vL += aL * 0.9f * Agility * dt;
        if (fuelOut) sink -= 1.62f * dt;                            // no engine: the Moon's gravity
        vel = heading * vF + right * vL + radial * sink;
        pos += vel * dt;
        // Eagle tilts to push itself sideways (about 5 deg per 0.15 m/s2 here); it stands up when it holds still
        Vector3 tiltTo = (radial + (heading * aF + right * aL) * 0.32f).normalized;
        upNow = Vector3.Slerp(upNow, tiltTo, 1f - Mathf.Exp(-dt * 3f));
        lmSite = Quaternion.LookRotation(Vector3.ProjectOnPlane(heading, upNow).normalized, upNow);
        // fuel: seconds to BINGO, then 20 to land or abort
        if (!fuelOut)
        {
            if (fuel > 0f) fuel -= dt; else bingoLeft -= dt;
            if (fuel <= 60f && !fired.Contains("f60") && FuelAtLowGate > 61f) { fired.Add("f60"); radio.Again("l_60"); radio.Say("l_60"); fd.Explain("CAPCOM: \"60 seconds.\" 60 seconds of fuel to BINGO.", 4f); }
            if (fuel <= 30f && !fired.Contains("f30")) { fired.Add("f30"); radio.Again("l_30"); radio.Say("l_30"); fd.Explain("CAPCOM: \"30 seconds.\"", 3f); }
            if (fuel <= 0f && !fired.Contains("bingo")) { fired.Add("bingo"); fd.Explain("BINGO: 20 seconds to land, or abort.", 5f); }
            if (bingoLeft <= 0f) { fuelOut = true; OutOfFuel(); return; }
        }
        if (alt < 30f) dustLow += dt;
        if (Problem == Trouble.Dust && alt < 26f && !fired.Contains("dustcard")) { fired.Add("dustcard"); AskDust(); return; }
        if (alt <= Probe && !contact)
        {
            contact = true;
            radio.Again("l_contact"); radio.Say("l_contact");
            fd.AddEvent("CONTACT LIGHT");
        }
        if (alt <= 0f) Touchdown(-sink, new Vector2(vF, vL).magnitude);
    }
    bool autoFly;

    // a clear spot short of the boulders, for the self-test (and for nobody else)
    void PlanAutoSpot()
    {
        Vector2 fwd = new Vector2(-a3.x, -a3.z), lg = -fwd * LowGateRange, side = new Vector2(-fwd.y, fwd.x);
        autoSpot = lg + fwd * 140f;
        float best = float.MaxValue;
        var rnd = new System.Random(5);
        for (int k = 0; k < 600; k++)
        {
            var p = lg + fwd * (50f + (float)rnd.NextDouble() * 200f) + side * ((float)rnd.NextDouble() - 0.5f) * 160f;
            if ((p - terrain.Hazard).magnitude < terrain.HazardRadius * 2.9f) continue;
            float slope = terrain.SlopeAt(p, out float rh, out _);
            if (slope > 5f || rh > 0.2f) continue;
            float cost = (p - (lg + fwd * 130f)).magnitude * 0.02f + slope;
            if (cost < best) { best = cost; autoSpot = p; }
        }
        Debug.Log($"LANDING autopilot spot {autoSpot}, {(autoSpot - lg).magnitude:0} m from low gate");
    }

    // ---------------- touchdown ----------------

    void Touchdown(float sink, float side)
    {
        var g = new Vector2(pos.x, pos.z);
        float slope = terrain.SlopeAt(g, out float rock, out Vector2 down);
        touchSink = sink; touchSide = side; touchSlope = slope; touchFuel = fuel + (fuel <= 0f ? bingoLeft - 20f : 0f);
        pos = new Vector3(pos.x, terrain.HeightAt(pos.x, pos.z, true), pos.z);
        Debug.Log($"LANDING touchdown: {sink:0.00} m/s down, {side:0.00} m/s sideways, slope {slope:0.0} deg, rock {rock:0.00} m, fuel {touchFuel:0} s, {g.magnitude:0} m from the target");
        if (sink > MaxSink) { StartCoroutine(Crash(false, down, "HARD LANDING",
            $"Eagle hit the ground at {sink:0.0} m/s. The legs were built to crush and soften a landing up to about 3 m/s (10 ft/s): faster, and the descent stage was damaged. No safe way home from here.",
            "Slow the descent before the ground: hold W from about 30 m up so you come down at 1 m/s or less at the end. Armstrong touched down at about 0.5 m/s.")); return; }
        if (side > MaxSide) { StartCoroutine(Crash(true, new Vector2(vel.x, vel.z), "TIPPED OVER",
            $"Eagle was still sliding {side:0.0} m/s sideways when it touched. A footpad dug into the dust and Eagle fell over.",
            "Stop moving sideways before you touch down: the arrows push, and pushing the other way brakes. The legs could take only about 1.2 m/s (4 ft/s) sideways.")); return; }
        if (slope > MaxSlope) { StartCoroutine(Crash(true, down, "TIPPED OVER",
            $"The ground there slopes {slope:0} degrees. Eagle landed on it and fell over downhill.",
            "Pick flat ground: crater walls are steep. Eagle could stand on 12 degrees at most, and the ascent stage needed it nearly level to take off.")); return; }
        if (rock > MaxRock) { StartCoroutine(Crash(true, down, "ROCK UNDER EAGLE",
            $"A boulder {rock:0.0} m high was under Eagle. It pushed one side up, and Eagle tipped over.",
            "Look for a clear spot before you settle: boulders are bright with long black shadows when the Sun is low. Armstrong flew 400 m past the boulders around West crater for exactly this reason.")); return; }
        StartCoroutine(Film(g));
    }

    // ---------------- the problems ----------------

    // Apollo 11: the 1202 and 1201 program alarms
    void Ask1202()
    {
        radio.Again("l_alarm"); radio.Say("l_alarm");
        fd.AddEvent("PROGRAM ALARM 1202");
        Problems.I.Ask("PROGRAM ALARM 1202  ·  APOLLO 11",
            "10 km above the Moon, braking hard. A yellow PROGRAM light comes on, the alarm sounds, and the computer shows 1202. You never saw this code in training. What do you do?",
            new[] { "1202 means the computer had more jobs than time and started over (\"executive overflow\")",
                    "It came back in a split second: the steering and the engine are still working",
                    "Your height and speed still match the plan" },
            new (string, bool, string)[]
            {
                ("ABORT: FIRE THE ASCENT STAGE", false, ""),
                ("KEEP GOING: THE COMPUTER DROPS ITS LEAST IMPORTANT JOBS", true,
                 "Margaret Hamilton's team built the computer to restart in a split second and keep only what matters: steering and the engine. In Houston, guidance officer Steve Bales and Jack Garman knew the code: \"We're GO on that alarm.\""),
                ("SWITCH THE COMPUTER OFF AND FLY BY HAND", false, ""),
                ("RESTART THE COMPUTER YOURSELF", false, ""),
            }, 20f,
            () => { radio.Again("l_go_alarm"); radio.Say("l_go_alarm"); story.Add("1202 alarm: you kept going (Houston: \"We're GO on that alarm\")"); },
            i =>
            {
                if (i == 0) StartCoroutine(Abort(true, "ABORT", "You fired the ascent stage. Eagle's top half blasted off its bottom half and climbed back toward Columbia. Apollo 11 went home without landing.",
                    "The alarm said the computer was too busy, not that anything was broken. It restarted itself and kept flying: the right move was to keep going.", Rewind1202));
                else if (i == 2) StartCoroutine(Abort(false, "LOST CONTROL", "You switched the computer off at 10 km, falling at 1,000 km/h. Nobody can fly that braking burn by hand: Eagle came down too fast and Houston called an abort.",
                    "The computer was still steering perfectly. A 1202 means it is busy, not broken.", Rewind1202));
                else StartCoroutine(Abort(false, "COMPUTER RESTARTED", "A full restart by hand takes minutes, and the computer forgets where Eagle is. With no navigation in the middle of the descent, Houston called an abort.",
                    "The computer had already restarted itself, in a split second, and kept its place. Nothing more was needed.", Rewind1202));
            },
            () => { radio.Again("l_go_alarm"); radio.Say("l_go_alarm"); fd.Explain("CAPCOM: \"We're GO on that alarm.\" Houston decided for you.", 6f); story.Add("1202 alarm: Houston called GO for you"); });
    }
    void Rewind1202() => RewindTo(AlarmAt - 4f, "REWIND: just before the 1202 alarm.");

    // Apollo 14: the landing radar would not lock until its circuit breaker was cycled
    void AskRadar()
    {
        radarOk = false;
        fd.AddEvent("NO RADAR LOCK");
        Problems.I.Ask("NO LANDING RADAR  ·  APOLLO 14",
            "The landing radar should have found the ground by now. It has not. Without it the computer only knows your height from the orbit, and that can be 1 to 2 km wrong. The rule: no radar data by 3 km up (10,000 ft) means abort.",
            new[] { "Panel: ALT and VEL lights on, radar not locked",
                    $"Height from the orbit: {AltAt(t) / 1000f:0.0} km, maybe 1-2 km off",
                    "The radar warmed up and worked in its checks before the descent" },
            new (string, bool, string)[]
            {
                ("KEEP GOING: THE COMPUTER'S HEIGHT IS CLOSE ENOUGH", false, ""),
                ("CYCLE THE LANDING RADAR CIRCUIT BREAKER", true, "Pull it out and push it back in: the radar starts over. That is what Houston asked Apollo 14's crew to do, at 7 km up. Now find LDG RADAR on the breaker panel, left."),
                ("ABORT NOW", false, ""),
                ("SWITCH TO THE BACKUP COMPUTER (AGS)", false, ""),
            }, 20f,
            () => { radarFixing = true; story.Add("Landing radar would not lock: you cycled its breaker"); BreakerPanel(true); },
            i =>
            {
                if (i == 0) { StartCoroutine(Abort(false, "RADAR RULE", "Eagle reached 3 km with no radar data. The computer's height was 1,500 m too high: it would have braked too late. Houston called the abort, as the rule says.",
                    "A guessed height is not enough to land on. The fix was simple and quick: cycle the radar's breaker.", RewindRadar)); }
                else if (i == 2) StartCoroutine(Abort(false, "ABORTED EARLY", "You aborted at 12 km. Eagle climbed back to Columbia with no landing.",
                    "You still had more than a minute before the 3 km rule. A problem with a simple fix is worth trying first.", RewindRadar));
                else StartCoroutine(Abort(false, "NO WAY TO LAND", "The backup computer (AGS) can only fly Eagle back up to orbit, not land it. Houston called the abort.",
                    "The AGS is the 'get us home' computer. The radar needed a restart: cycle its breaker.", RewindRadar));
            },
            () => { radarFixing = true; fd.Explain("CAPCOM: \"We'd like you to cycle the Landing Radar breaker.\" Houston decided for you.", 8f); story.Add("Landing radar: Houston called the fix"); BreakerPanel(true); });
    }
    void RewindRadar() { radarOk = true; radarFixing = false; BreakerPanel(false); RewindTo(RadarAt - 4f, "REWIND: just before the radar should have locked."); }

    void RadarLost()
    {
        radarFixing = false; BreakerPanel(false);
        StartCoroutine(Abort(false, "RADAR RULE", "Eagle reached 3 km up and the radar still had no lock. The rule says abort, and Houston called it.",
            "Find LDG RADAR among the breakers on the left: click it to pull it out, click again to push it back in.", RewindRadar));
    }

    void RadarFixed()
    {
        radarOk = true; radarFixing = false; BreakerPanel(false);
        fd.AddEvent("RADAR LOCK");
        holding = true;
        Problems.I.Info("RADAR LOCK  ·  WHY IT WORKED",
            "Pulling the breaker cut the radar's power, and pushing it in started it again from scratch. It locked on the ground at once, and the computer corrected its height by about 1,500 m.\n\n"
          + "Apollo 14's radar had stuck in its short-range setting, so it could not lock from high up. Restarting it put it back. Shepard and Mitchell landed 26 m from their target, the closest of any Apollo.",
            () => holding = false);
    }

    // Apollo 14: a loose ball of solder in the ABORT switch
    void AskFakeAbort()
    {
        fd.AddEvent("ABORT LIGHT");
        Problems.I.Ask("ABORT LIGHT ON BY ITSELF  ·  APOLLO 14",
            "Minutes before the descent engine lights, the ABORT light comes on. Nobody touched the button. If the computer sees ABORT once the engine is running, it will throw away the landing and fly you back up.",
            new[] { "Tapping the panel next to the button made the light go out, then it came back",
                    "Houston thinks a tiny ball of loose solder is floating inside the switch",
                    "The computer can be told to ignore the switch, with a code typed on its keyboard" },
            new (string, bool, string)[]
            {
                ("START THE DESCENT AS PLANNED", false, ""),
                ("TAP THE PANEL AND HOPE IT STAYS OFF", false, ""),
                ("TYPE THE FIX: TELL THE COMPUTER TO IGNORE THE ABORT SWITCH", true, "MIT's Don Eyles wrote this fix in about two hours, and Houston read it up. Now type it on the computer's keyboard (DSKY), left."),
                ("CALL OFF THE LANDING", false, ""),
            }, 25f,
            () => { story.Add("Fake ABORT signal: you typed the fix"); Dsky(true); },
            i =>
            {
                if (i == 3) { Problems.I.Fail("NO LANDING", "You called it off. Eagle docked with Columbia again and Apollo 14 went home without walking on the Moon.",
                    "There was a fix: the switch only had to be ignored. Houston had it ready before the descent.", RewindFakeAbort); return; }
                StartCoroutine(FakeAbortPlays(i == 1));
            },
            () => { fd.Explain("Houston reads up the fix. Type it on the DSKY, left.", 8f); story.Add("Fake ABORT signal: Houston read up the fix"); Dsky(true); });
    }

    IEnumerator FakeAbortPlays(bool tapped)
    {
        anim = true;
        if (tapped) { fd.Explain("The light goes out after a tap...", 3f); yield return new WaitForSeconds(2f); }
        t = 0f; Now = Stage.Descent; fd.AddEvent("POWERED DESCENT");
        fd.Explain("PDI: the engine lights... and the ABORT signal comes back.", 3f);
        yield return new WaitForSeconds(1.6f);
        anim = false;
        yield return Abort(false, "THE COMPUTER ABORTED",
            (tapped ? "The tap moved the solder ball, but in weightlessness it floated back. " : "") + "The moment the engine lit, the computer saw ABORT, threw away the landing and steered Eagle back up toward Columbia.",
            "A floating bit of solder could close the switch at any moment. The only safe fix was to make the computer ignore the switch, before the engine started.", RewindFakeAbort);
    }

    void RewindFakeAbort()
    {
        Restore();
        Now = Stage.PrePdi; t = -40f; fired.Clear();
        fd.Explain("REWIND: minutes before the descent.", 4f);
        AskFakeAbort();
    }

    // Apollo 12: the dust
    void AskDust()
    {
        dustSnap = (pos, vel, fuel, dustLow);
        fd.AddEvent("DUST");
        Problems.I.Ask("YOU CAN'T SEE THE GROUND  ·  APOLLO 12",
            "25 m up the engine blasts the dust sideways in a sheet, fast and flat (there is no air to slow it). Through it the ground is a blur: you cannot see what is under you.",
            new[] { "Instruments still work: height, descent speed, and the cross-pointer for sideways speed",
                    $"Fuel to BINGO: {Mathf.Max(fuel, 0f):0} s",
                    "Pete Conrad flew Apollo 12's last 30 m like this" },
            new (string, bool, string)[]
            {
                ("LAND ON THE INSTRUMENTS: SLOW, STEADY, NO SIDEWAYS SPEED", true, "Trust the numbers: hold about 1 m/s down and keep the cross-pointer's dot in the middle. Conrad landed Apollo 12 this way, 160 m from the Surveyor 3 probe they were aiming at."),
                ("CLIMB BACK UP UNTIL YOU CAN SEE", false, ""),
                ("DROP FAST TO GET OUT OF THE DUST", false, ""),
                ("ABORT", false, ""),
            }, 15f,
            () => story.Add("Blinding dust: you landed on the instruments"),
            i =>
            {
                if (i == 1) Problems.I.Fail("OUT OF FUEL", "Climbing back out of the dust took fuel, and the dust was still there coming down again. Eagle reached BINGO in the air and had to abort.",
                    "The dust never goes away close to the ground: the engine makes it. Climbing only wastes the fuel you need. Land on the instruments.", RewindDust);
                else if (i == 2) { touchSink = 5.5f; StartCoroutine(Crash(false, Vector2.zero, "HARD LANDING", "You dropped through the dust at 5 m/s. The legs crushed and the descent stage was damaged.",
                    "Fast is never the way out. Landing on the instruments means slow and steady: about 1 m/s down at the end.", RewindDust)); }
                else StartCoroutine(Abort(true, "ABORT", "You fired the ascent stage 25 m above the Moon. Eagle's top half climbed back to Columbia. No landing.",
                    "Nothing was wrong with Eagle: only the view. The instruments were enough to land.", RewindDust));
            },
            () => { fd.Explain("Too slow: land on the instruments now. W slows the descent.", 6f); story.Add("Blinding dust: landed on the instruments"); });
    }
    void RewindDust()
    {
        Restore();
        Now = Stage.Manual; pos = dustSnap.pos; vel = dustSnap.vel; fuel = dustSnap.fuel; dustLow = dustSnap.dust; fuelOut = false; contact = false;
        fired.Remove("dustcard");
        fd.Explain("REWIND: 26 m up, in the dust.", 4f);
    }

    void OutOfFuel()
    {
        StartCoroutine(Abort(true, "OUT OF FUEL", $"20 seconds after BINGO Eagle was still {AltAbove(pos):0} m up. The rule: abort before the fuel runs out. The ascent stage fired and Eagle's top half climbed away.",
            "Pick a spot early and go straight to it. Hovering burns fuel fast: Apollo 11 landed with about 25 seconds to spare.", RewindManual));
    }

    void RewindManual()
    {
        Restore();
        Now = Stage.Manual; pos = lowGate.pos; vel = lowGate.vel; fuel = lowGate.fuel; bingoLeft = 20f; dustLow = 0f; fuelOut = false; contact = false;
        fired.Remove("f60"); fired.Remove("f30"); fired.Remove("bingo"); fired.Remove("dustcard");
        upNow = (pos - C).normalized;
        view = View.Window;
        fd.Explain("REWIND: back to 150 m up. Find flat, clear ground.", 5f);
    }

    void RewindTo(float tt, string line)
    {
        Restore();
        Now = Stage.Descent; t = tt;
        var keep = new List<string>(); foreach (var k in fired) if (KeyTime(k) < tt) keep.Add(k);
        fired.Clear(); foreach (var k in keep) fired.Add(k);
        view = tt >= Pitchover ? View.Window : View.Outside;
        fd.Explain(line, 4f);
    }
    static float KeyTime(string k) => k == "pdi" ? 2f : k == "radar" ? RadarAt : k == "1202" ? AlarmAt : k == "pitch" ? Pitchover : k == "golanding" ? GoLandingAt : k == "1201" ? Alarm1201At : k == "lpd" ? LpdAt : 9999f;

    // ---------------- what happens: aborts and crashes, played out ----------------

    IEnumerator Abort(bool staged, string title, string what, string why, Action rewind)
    {
        anim = true; BreakerPanel(false); Dsky(false);
        view = View.Scripted;
        fd.AddEvent(staged ? "ABORT STAGE" : "ABORT");
        Vector3 up0 = (pos - C).normalized;
        Vector3 side = Vector3.Cross(up0, a3).normalized;
        scriptPos = side * 26f + a3 * 12f + up0 * 4f; scriptLook = up0 * 4f; scriptFov = 45f;
        if (staged) { SplitStages(true); if (ascentPlume) ascentPlume.SetParent(ascent, true); }
        abortBurn = staged ? 2 : 1;
        float s = 0f;
        Vector3 basePos = pos;
        while (s < 3.4f)
        {
            s += Time.deltaTime;
            if (staged)
            {
                ascent.localPosition = Vector3.up * (0.5f * 4.5f * s * s);              // the ascent engine: about 4.5 m/s2 empty-ish
                if (AltAbove(pos) > 0.5f) pos += vel * Time.deltaTime;                 // the descent stage keeps falling
            }
            else
            {
                pos = basePos + up0 * (0.5f * 2.5f * s * s);                           // the descent engine at full throttle, nose up
                lmSite = Quaternion.Slerp(lmSite, Quaternion.LookRotation(Vector3.ProjectOnPlane(-a3, up0), (up0 + a3 * 0.5f).normalized), Time.deltaTime * 1.5f);
            }
            scriptLook = staged ? ascent.localPosition * 0.6f + up0 * 4f : (pos - basePos) + up0 * 4f;
            yield return null;
        }
        fd.Explain(staged ? "ABORT STAGE · the ascent stage leaves the descent stage behind" : "ABORT · Eagle climbs back toward Columbia", 3f);
        yield return new WaitForSeconds(0.6f);
        abortBurn = 0;
        Problems.I.Fail(title, what, why, rewind);
        yield return null;
        anim = false;
    }

    IEnumerator Crash(bool tip, Vector2 dir, string title, string what, string why, Action rewind = null)
    {
        anim = true;
        view = View.Scripted;
        Vector3 up0 = (pos - C).normalized;
        Vector3 d = dir.sqrMagnitude > 1e-4f ? new Vector3(dir.x, 0f, dir.y).normalized : Vector3.Cross(up0, a3).normalized;
        Vector3 side = Vector3.Cross(up0, d).normalized;
        scriptPos = side * 22f - d * 6f + up0 * 3f; scriptLook = up0 * 2.5f; scriptFov = 45f;
        pos = new Vector3(pos.x, terrain.HeightAt(pos.x, pos.z, true), pos.z);
        Quaternion start = lmSite;
        if (tip)
        {
            // over a footpad, faster and faster
            Vector3 pivot = d * 4.4f;
            float a = 0f, w = 0f;
            Vector3 lmPos0 = pos;
            while (a < 92f)
            {
                w += Time.deltaTime * 70f; a = Mathf.Min(92f, a + w * Time.deltaTime);
                var q = Quaternion.AngleAxis(a, Vector3.Cross(up0, d));
                lmSite = q * start;
                pos = lmPos0 + pivot - q * pivot;
                yield return null;
            }
        }
        else
        {
            // a hard landing: the legs crush, the whole thing slams down
            float s = 0f;
            while (s < 0.35f) { s += Time.deltaTime; lmModel.localScale = Vector3.Scale(lmModelScale, new Vector3(1f, Mathf.Lerp(1f, 0.82f, s / 0.35f), 1f)); yield return null; }
        }
        yield return new WaitForSeconds(1.2f);
        Problems.I.Fail(title, what, why, rewind ?? RewindManual);
        yield return null;
        anim = false;
    }

    void Restore()
    {
        StopAllCoroutines();
        anim = false; holding = false; abortBurn = 0;
        if (ascentPlume && lm) { ascentPlume.SetParent(lm, false); ascentPlume.localPosition = new Vector3(0f, ascentSplit - 0.1f, 0f); ascentPlume.localRotation = Quaternion.identity; }
        SplitStages(false);
        if (lmModel) lmModel.localScale = lmModelScale;
        foreach (var p in probes) if (p) p.localRotation = Quaternion.identity;
        Dsky(false); BreakerPanel(false);
        radio.Stop();
    }

    // ---------------- touchdown film and the end ----------------

    IEnumerator Film(Vector2 ground)
    {
        Now = Stage.Film; anim = true;
        var card = hud.Find("Cabin/CountCard"); if (card) card.gameObject.SetActive(false);   // a clear picture for the film
        meshCentre = ground;                                        // the finest ground right here, for the close-ups
        Vector3 up0 = (pos - C).normalized;
        Vector3 sunS = SunInSite(MissionState.LaunchUtc.AddSeconds(PdiGet + t));
        Vector3 sunH = Vector3.ProjectOnPlane(sunS, up0).normalized;
        Vector3 side = Vector3.Cross(up0, sunH).normalized;
        // 1: low and wide, the Sun from the side: the last metre, the dust stops dead
        view = View.Scripted;
        scriptPos = side * 30f - sunH * 6f + up0 * 1.6f; scriptLook = up0 * 2.6f; scriptFov = 38f;
        lmSite = Quaternion.LookRotation(Vector3.ProjectOnPlane(-a3, up0).normalized, up0);
        Vector3 rest = pos;
        pos = rest + up0 * 0.9f;
        float s = 0f;
        while (s < 1.0f) { s += Time.deltaTime; pos = rest + up0 * Mathf.Lerp(0.9f, 0f, s / 1.0f); yield return null; }
        pos = rest; contact = true;
        foreach (var p in probes) if (p) p.localRotation = Quaternion.Euler(0f, 0f, 70f);   // the probes bend over as Eagle settles
        radio.Again("l_stop"); radio.Say("l_stop");
        fd.AddEvent("ENGINE STOP");
        fd.Explain("CONTACT LIGHT · ENGINE STOP. With no air the dust doesn't hang in a cloud: it falls at once.", 5f);
        yield return new WaitForSeconds(3.2f);
        // 2: low beside Eagle, then a slow tilt up from Eagle to the Earth in the black sky
        Vector3 earthS = EarthInSite(MissionState.LaunchUtc.AddSeconds(PdiGet + t));
        Vector3 eh = Vector3.ProjectOnPlane(earthS, up0); if (eh.sqrMagnitude < 1e-4f) eh = side;
        eh.Normalize();
        radio.Again("l_landed"); radio.Say("l_landed");
        fd.AddEvent("THE EAGLE HAS LANDED");
        fd.Explain("\"Houston, Tranquility Base here. The Eagle has landed.\"   The real radio, 20 July 1969.", 10f);
        Vector3 camP = -eh * 19f + Vector3.Cross(up0, eh) * 7f + up0 * 1.3f;
        Vector3 lmC = up0 * 3.3f;
        Vector3 toLm = (lmC - camP).normalized, toEarth = earthS.normalized;
        float sep = Vector3.Angle(toLm, toEarth);
        float endS = sep > 40f ? Mathf.Clamp01(1f - 14f / sep) : 0.45f;     // end with the Earth a little above the middle
        float start = Time.time;
        while (Time.time - start < 10.5f)
        {
            float k = Time.time - start;
            float sk = Mathf.SmoothStep(0f, endS, (k - 3.2f) / 5.5f);
            Vector3 dir = Vector3.Slerp(toLm, toEarth, sk);
            scriptPos = camP + Vector3.Cross(up0, eh) * (k * 0.25f);          // a slow drift sideways while it tilts
            scriptLook = scriptPos + dir * 100f;
            scriptFov = 50f;
            yield return null;
        }
        Now = Stage.Done; anim = false;
        Summary(ground);
    }

    Vector3 EarthInSite(DateTime utc)
    {
        Ephemeris.MoonAxes(utc, out var m, out var e, out var pz);
        Axes(m, e, pz, out var upW, out var eaW, out var noW);
        Vector3 toEarth = (-Ephemeris.MoonWorld(utc)).normalized;
        return new Vector3(Vector3.Dot(toEarth, eaW), Vector3.Dot(toEarth, upW), Vector3.Dot(toEarth, noW)).normalized;
    }

    void Summary(Vector2 ground)
    {
        float dist = ground.magnitude;
        string dir = Compass(ground);
        string place = MissionModel.PlaceName(lat, lon);
        double sun = Ephemeris.SunElevationOnMoon(MissionState.LaunchUtc.AddSeconds(PdiGet + t), lat, lon);
        var sb = new System.Text.StringBuilder();
        sb.Append($"<b>{place}</b>   {Math.Abs(lat):0.000}° {(lat >= 0 ? "N" : "S")}, {Math.Abs(lon):0.000}° {(lon >= 0 ? "E" : "W")}   ·   {(terrain.Mare ? "dark mare plain" : "bright highland")}\n");
        sb.Append($"You landed <b>{dist:0} m {dir}</b> of the point you picked. The computer was taking you into a crater full of boulders.\n");
        sb.Append($"Touchdown: {touchSink:0.0} m/s down, {touchSide:0.0} m/s sideways, ground {touchSlope:0}° steep. Fuel left: {Mathf.Max(touchFuel, 0f):0} s to BINGO.\n");
        sb.Append($"Eagle carried {MissionModel.EagleFuelKg(MissionState.Fuel):#,0} kg of fuel (Apollo 11: 8,250). Route: {(MissionState.Hybrid ? "hybrid, as Apollo 12-17" : "free return, as Apollo 11")}." + "\n");
        sb.Append($"The Sun was {sun:0}° above the horizon: shadows {(sun < 9 ? "long and dark" : sun < 15 ? "long enough to show every crater" : "short; the ground looked flat")}.\n");
        if (story.Count > 0) { sb.Append("\n<b>WHAT HAPPENED</b>\n"); foreach (var l in story) sb.Append("—  " + l + "\n"); }
        if (rrAuto && Problem == Trouble.Alarm1202) sb.Append("\nWhy the 1202? The rendezvous radar left tracking Columbia kept sending the computer meaningless data, using up about 15% of its time. That was the real cause in 1969, found after the flight.\n");
        sb.Append("\nESC: back to the planner. Another date or site changes the light, the ground and the problems.");
        fd.AddEvent("MISSION COMPLETE");
        var card = hud.Find("Cabin/CountCard"); if (card) card.gameObject.SetActive(true);
        Problems.I.Info("MISSION COMPLETE  ·  ON THE MOON", sb.ToString(), () => { });
    }

    static string Compass(Vector2 v)
    {
        if (v.magnitude < 15f) return "from";
        float a = Mathf.Atan2(v.x, v.y) * Mathf.Rad2Deg; if (a < 0) a += 360f;
        string[] n = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };
        return n[Mathf.RoundToInt(a / 45f) % 8];
    }

    // ---------------- Eagle ----------------

    Vector3 lmModelScale = Vector3.one;
    float ascentSplit = 3.3f;

    void BuildLm()
    {
        lm = LmModel.Build(transform, Vector3.zero, Quaternion.identity);
        lm.SetParent(null, true);
        // legs out: show every part, and stand it on its footpads
        foreach (var r in lm.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
        lmModel = lm.Find("NASA_LM");
        if (lmModel)
        {
            Bounds b = default; bool any = false;
            foreach (var r in lmModel.GetComponentsInChildren<Renderer>(true))
            {
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            float lift = -lm.InverseTransformPoint(b.min).y;
            // the NASA model's ladder, hatch and windows face -z: turn it so they face the way Eagle flies (+z)
            var turn = Quaternion.Euler(0f, 180f, 0f);
            foreach (Transform c in lm) { c.localPosition = turn * (c.localPosition + Vector3.up * lift); c.localRotation = turn * c.localRotation; }
            lmModelScale = lmModel.localScale;
            ascentSplit = (lm.InverseTransformPoint(b.max).y) * 0.47f;
            // the contact probes: 1.7 m rods under three of the four footpads (not under the ladder)
            var pads = new List<Vector3>();
            foreach (var r in lmModel.GetComponentsInChildren<Renderer>(true))
            {
                Vector3 c = lm.InverseTransformPoint(r.bounds.center);
                if (c.y < 0.7f && new Vector2(c.x, c.z).magnitude > 2.8f) pads.Add(c);
            }
            var quad = new Vector3[4]; var cnt = new int[4];
            foreach (var p in pads)
            {
                float ang = Mathf.Atan2(p.x, p.z) * Mathf.Rad2Deg; int q = Mathf.RoundToInt(((ang + 360f) % 360f) / 90f) % 4;
                quad[q] += p; cnt[q]++;
            }
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.55f, 0.5f, 0.42f) };
            for (int q = 1; q < 4; q++)
            {
                Vector3 c = cnt[q] > 0 ? quad[q] / cnt[q] : Quaternion.Euler(0f, q * 90f, 0f) * Vector3.forward * 4.2f;
                var holder = new GameObject("Probe" + q).transform;
                holder.SetParent(lm, false); holder.localPosition = new Vector3(c.x, 0.05f, c.z);
                var rod = GameObject.CreatePrimitive(PrimitiveType.Cylinder).transform;
                Destroy(rod.GetComponent<Collider>());
                rod.SetParent(holder, false); rod.localScale = new Vector3(0.03f, Probe * 0.5f, 0.03f); rod.localPosition = new Vector3(0f, -Probe * 0.5f, 0f);
                rod.GetComponent<Renderer>().sharedMaterial = mat;
                probes.Add(holder);
            }
        }
        foreach (var r in lm.GetComponentsInChildren<Renderer>(true)) lmRenderers.Add(r);
        // the descent engine's plume (in vacuum hardly visible in sunlight)
        if (fd.PlumeMat)
        {
            plumeMat = new Material(fd.PlumeMat);
            plume = new GameObject("DescentPlume").transform;
            plume.SetParent(lm, false); plume.localPosition = new Vector3(0f, 1.05f, 0f);
            plume.gameObject.AddComponent<MeshFilter>().sharedMesh = Exhaust.PlumeMesh();
            var mr = plume.gameObject.AddComponent<MeshRenderer>(); mr.sharedMaterial = plumeMat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            plume.localScale = new Vector3(2.6f, 9f, 2.6f);
            ascentPlume = new GameObject("AscentPlume").transform;
            ascentPlume.SetParent(lm, false); ascentPlume.localPosition = new Vector3(0f, ascentSplit - 0.1f, 0f);
            ascentPlume.gameObject.AddComponent<MeshFilter>().sharedMesh = Exhaust.PlumeMesh();
            var amr = ascentPlume.gameObject.AddComponent<MeshRenderer>(); amr.sharedMaterial = plumeMat; amr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ascentPlume.localScale = new Vector3(1.4f, 6f, 1.4f);
            ascentPlume.gameObject.SetActive(false);
        }
        // the dust sheet: fast, flat, straight out (no air)
        if (fd.TrailMat)
        {
            var go = new GameObject("DustSheet");
            go.transform.SetParent(terrain.transform, false);
            dustPs = go.AddComponent<ParticleSystem>();
            dustPs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = dustPs.main;
            main.duration = 1000f; main.loop = true; main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(25f, 60f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.8f, 2.6f);
            main.startColor = new Color(0.8f, 0.78f, 0.74f, 0.16f);
            main.maxParticles = 2500; main.simulationSpace = ParticleSystemSimulationSpace.Local; main.gravityModifier = 0f;
            var sh = dustPs.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 2.5f; sh.rotation = new Vector3(90f, 0f, 0f);
            var col = dustPs.colorOverLifetime; col.enabled = true;
            var gr = new Gradient(); gr.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = gr;
            var em = dustPs.emission; em.rateOverTime = 0f;
            var rr = go.GetComponent<ParticleSystemRenderer>();
            rr.sharedMaterial = new Material(fd.TrailMat) { color = new Color(0.85f, 0.83f, 0.8f, 1f) };
            rr.renderMode = ParticleSystemRenderMode.Stretch; rr.velocityScale = 0.05f; rr.lengthScale = 2f;
            dustPs.Play();
        }
    }

    void SplitStages(bool on)
    {
        if (on)
        {
            if (ascent == null) { ascent = new GameObject("AscentStage").transform; ascent.SetParent(lm, false); }
            ascent.localPosition = Vector3.zero;
            foreach (var r in lmRenderers)
            {
                if (r == null) continue;
                if (lm.InverseTransformPoint(r.bounds.center).y > ascentSplit && r.transform.parent != ascent) { splitFrom[r.transform] = r.transform.parent; r.transform.SetParent(ascent, true); }
            }
        }
        else if (ascent != null)
        {
            ascent.localPosition = Vector3.zero;
            foreach (var kv in splitFrom) if (kv.Key) kv.Key.SetParent(kv.Value, true);
            splitFrom.Clear();
        }
    }
    readonly Dictionary<Transform, Transform> splitFrom = new Dictionary<Transform, Transform>();

    // the descent engine; during an abort the engine that does the work: the descent engine at full throttle, or the
    // ascent engine lifting the top half off (both burn hypergolic fuel: a faint, almost clear flame)
    int abortBurn;                                                                  // 0 none, 1 descent engine, 2 ascent engine
    Transform ascentPlume;
    void Plume(bool on)
    {
        if (!plume) return;
        plume.gameObject.SetActive((on && !anim) || abortBurn == 1);
        float heat = abortBurn == 1 ? 0.05f : 0.02f;
        if (plumeMat) { plumeMat.SetColor("_Tint", new Color(0.6f, 0.7f, 1f)); plumeMat.SetFloat("_Heat", heat); }
        if (ascentPlume) ascentPlume.gameObject.SetActive(abortBurn == 2);
    }

    // ---------------- the camera ----------------

    public void CameraUpdate()
    {
        var cam = fd.Cam;
        cam.cullingMask = ~(1 << 8);
        cam.farClipPlane = 450000f;
        bool inside = view == View.Window && !anim;
        foreach (var r in lmRenderers) if (r) r.shadowCastingMode = inside ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly : UnityEngine.Rendering.ShadowCastingMode.On;
        foreach (var p in probes) if (p) p.gameObject.SetActive(!inside);
        if (Input.GetMouseButton(0) && !Auto && !(Problems.I != null && Problems.I.Deciding))
        {
            float mx = Input.GetAxis("Mouse X") * 2.4f, my = Input.GetAxis("Mouse Y") * 2.4f;
            if (view == View.Window) { dragYaw = Mathf.Clamp(dragYaw + mx, -45f, 45f); dragPitch = Mathf.Clamp(dragPitch - my, -30f, 35f); }
            else if (view == View.Outside) { orbitYaw += mx; orbitPitch = Mathf.Clamp(orbitPitch - my, -5f, 80f); }
        }
        if (view == View.Window && !anim)
        {
            float down = Now == Stage.Manual ? 38f : Now == Stage.Descent && t < Pitchover ? 0f : 26f;
            cam.transform.SetPositionAndRotation(lm.TransformPoint(EyeLocal), lm.rotation * Quaternion.Euler(down + dragPitch, dragYaw, 0f));
            cam.fieldOfView = 58f; cam.nearClipPlane = 0.05f;
        }
        else if (view == View.Outside && !anim)
        {
            Vector3 up = siteRot * upNow;
            Vector3 fwd = siteRot * Vector3.ProjectOnPlane(-a3, upNow).normalized;
            Quaternion frame = Quaternion.LookRotation(fwd, up);
            float dist = Now == Stage.Descent && t < Pitchover ? 48f : orbitDist;
            Vector3 focus = lm.TransformPoint(Vector3.up * 3.5f);
            Vector3 off = frame * (Quaternion.Euler(orbitPitch, orbitYaw, 0f) * Vector3.back) * dist;
            cam.transform.SetPositionAndRotation(focus + off, Quaternion.LookRotation(-off, up));
            cam.fieldOfView = 45f; cam.nearClipPlane = 0.3f;
        }
        else
        {
            Vector3 up = siteRot * (pos - C).normalized;
            Vector3 p = siteRot * scriptPos, l = siteRot * scriptLook;
            cam.transform.SetPositionAndRotation(p, Quaternion.LookRotation(l - p, up));
            cam.fieldOfView = scriptFov; cam.nearClipPlane = 0.2f;
        }
        if (Time.time > nextDbg && Now != Stage.Setup)
        {
            nextDbg = Time.time + 2f;
            Vector3 upW = siteRot * (pos - C).normalized, uprW = siteRot * a3;
            Debug.Log($"LANDDBG t {t:0} {Now} {view} alt {AltAbove(pos):0} camElev {90f - Vector3.Angle(cam.transform.forward, upW):0.0} lmFwdElev {90f - Vector3.Angle(lm.forward, upW):0.0} "
                    + $"lmUp.uprange {Vector3.Dot(lm.up, uprW):0.00} lmFwd.uprange {Vector3.Dot(lm.forward, uprW):0.00} cam.uprange {Vector3.Dot(cam.transform.position - lm.position, uprW):0.0} "
                    + $"camRoll {Vector3.SignedAngle(Vector3.ProjectOnPlane(cam.transform.right, upW), cam.transform.right, cam.transform.forward):0.0} vel.uprange {Vector3.Dot(vel, a3):0.0}");
        }
    }

    // for the self-test: look from outside, or from the window
    public void TestView(bool outside) { if (!anim) view = outside ? View.Outside : View.Window; }

    public void Hud(TextMeshProUGUI clock, TextMeshProUGUI telemetry, TextMeshProUGUI warp)
    {
        int g = Mathf.Max(0, (int)(PdiGet + t + clockExtra));
        clock.text = $"T+{g / 3600:00}:{g / 60 % 60:00}:{g % 60:00}";
        if (Now == Stage.Idle || Now == Stage.Setup) { telemetry.text = "EAGLE  ·  UNDOCKED"; warp.text = "WAITING"; return; }
        float alt = AltAbove(pos);
        telemetry.text = Now >= Stage.Film ? $"ON THE MOON  ·  {MissionModel.PlaceName(lat, lon)}" : $"{(alt > 2000f ? $"{alt / 1000f:0.0} KM" : $"{alt:0} M")} UP   ·   {vel.magnitude * 3.6f:#,0} KM/H";
        warp.text = Problems.I.Deciding || holding ? "WAITING" : Now == Stage.Descent && rate > 1.5f ? $"{rate:0}× SPEED" : "REAL TIME";
    }

    // ---------------- the window and the instruments ----------------

    RectTransform windowMask, lpdRoot, lpdMark, panel, cross, crossDot, breakers, dsky, veil;
    TextMeshProUGUI panelText, lpdLabel, dskyText, dskyHint;
    readonly List<(RectTransform tick, TextMeshProUGUI label, float deg)> ticks = new List<(RectTransform, TextMeshProUGUI, float)>();
    static readonly Vector2[] WindowShape = { new Vector2(0.06f, 0.95f), new Vector2(0.79f, 0.95f), new Vector2(0.79f, 0.21f), new Vector2(0.47f, 0.21f) };

    void BuildUi()
    {
        // the commander's window: the LM's forward windows are triangles; the rest of the view is the dark cabin
        veil = UiKit.Full(hud, "DustVeil");                              // the dust, seen through the glass (under the cabin's frame)
        var vi = veil.gameObject.AddComponent<RawImage>(); vi.texture = VeilTexture(); vi.color = new Color(0.8f, 0.78f, 0.75f, 0f); vi.raycastTarget = false;
        veil.SetAsFirstSibling();
        windowMask = UiKit.Full(hud, "LmWindow");
        windowMask.SetSiblingIndex(1);
        var raw = windowMask.gameObject.AddComponent<RawImage>();
        raw.texture = WindowTexture(); raw.color = Color.white; raw.raycastTarget = false;
        // the landing point designator: degrees scribed on the window
        lpdRoot = UiKit.Full(hud, "Lpd");
        lpdRoot.SetSiblingIndex(2);
        for (int d = 0; d <= 70; d += 5)
        {
            var tk = UiKit.Rect(lpdRoot, "Tick" + d, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(d % 10 == 0 ? 22f : 11f, 1.5f));
            UiKit.Box(tk, new Color(1f, 1f, 1f, 0.55f), 0);
            TextMeshProUGUI lb = null;
            if (d % 10 == 0) lb = UiKit.Label(lpdRoot, d.ToString(), 11, new Color(1f, 1f, 1f, 0.6f), Vector2.zero, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(40, 14), TextAlignmentOptions.MidlineLeft, true, 1);
            ticks.Add((tk, lb, d));
        }
        lpdMark = UiKit.Rect(lpdRoot, "Target", Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(26, 26));
        UiKit.OutlineBox(lpdMark, new Color(1f, 1f, 1f, 0.9f));
        lpdLabel = UiKit.Label(lpdMark, "COMPUTER'S TARGET", 10, UiKit.Text, new Vector2(1f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8, 0), new Vector2(200, 14), TextAlignmentOptions.MidlineLeft, true, 2);
        UiKit.Shadow(lpdLabel);

        // the instruments
        panel = UiKit.Rect(hud, "EagleInstruments", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-28, 70), new Vector2(330, 360));
        UiKit.Box(panel, new Color(0.02f, 0.022f, 0.026f, 0.9f), 12);
        UiKit.OutlineBox(UiKit.Full(panel, "Edge"), new Color(1f, 1f, 1f, 0.14f));
        UiKit.Label(panel, "EAGLE", 11, UiKit.Text2, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -12), new Vector2(300, 16), TextAlignmentOptions.Center, true, 4);
        panelText = UiKit.Label(panel, "", 14, UiKit.Text, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -36), new Vector2(296, 190), TextAlignmentOptions.TopLeft, false, 1);
        panelText.lineSpacing = 12;
        cross = UiKit.Rect(panel, "CrossPointer", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 20), new Vector2(110, 110));
        UiKit.OutlineBox(UiKit.Full(cross, "Edge"), new Color(1f, 1f, 1f, 0.25f));
        UiKit.Box(UiKit.Rect(cross, "H", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(110, 1)), new Color(1f, 1f, 1f, 0.25f), 0);
        UiKit.Box(UiKit.Rect(cross, "V", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1, 110)), new Color(1f, 1f, 1f, 0.25f), 0);
        crossDot = UiKit.Rect(cross, "Dot", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10, 10));
        UiKit.Box(crossDot, Color.white, 5);
        UiKit.Label(cross, "SIDEWAYS SPEED", 9, UiKit.Text2, new Vector2(0.5f, 0), new Vector2(0.5f, 1), new Vector2(0, -4), new Vector2(200, 12), TextAlignmentOptions.Center, true, 2);
        windowMask.gameObject.SetActive(false); lpdRoot.gameObject.SetActive(false); panel.gameObject.SetActive(false);
    }

    Texture2D WindowTexture()
    {
        int w = 640, h = Mathf.Max(200, Mathf.RoundToInt(640f * Screen.height / Mathf.Max(Screen.width, 1)));
        var tx = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[w * h];
        Vector2 S(Vector2 v) => new Vector2(v.x * w, v.y * h);
        var poly = new Vector2[WindowShape.Length]; for (int i = 0; i < poly.Length; i++) poly[i] = S(WindowShape[i]);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float dIn = float.MaxValue;                             // distance inside the window (negative outside)
                for (int i = 0; i < poly.Length; i++)
                {
                    Vector2 a = poly[i], b = poly[(i + 1) % poly.Length];
                    Vector2 e = (b - a).normalized; Vector2 n = new Vector2(e.y, -e.x);   // inward for this winding
                    dIn = Mathf.Min(dIn, Vector2.Dot(p - a, n));
                }
                float k = Mathf.Clamp01(-dIn / 1.5f + 0.5f);          // 1 outside, 0 inside, soft edge
                float frame = Mathf.Clamp01(1f - Mathf.Abs(dIn + 3f) / 1.2f) * 0.25f;   // a faint rim just outside the glass
                float inner = Mathf.Clamp01(1f - dIn / 40f) * 0.22f * (1f - k);           // the glass's edge darker
                byte g = (byte)(Mathf.Clamp01(0.02f + frame) * 255);
                px[y * w + x] = new Color32(g, g, (byte)(g + 1), (byte)(Mathf.Clamp01(k * 0.97f + inner) * 255));
            }
        tx.SetPixels32(px); tx.Apply(false, true);
        return tx;
    }

    static Texture2D VeilTexture()
    {
        var tx = new Texture2D(4, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < 64; y++) { float a = Mathf.Clamp01(1.1f - y / 64f * 1.2f); for (int x = 0; x < 4; x++) tx.SetPixel(x, y, new Color(1, 1, 1, a)); }
        tx.Apply(false, true);
        return tx;
    }

    void UiUpdate()
    {
        bool flying = Now == Stage.Descent || Now == Stage.Manual || Now == Stage.PrePdi;
        bool win = view == View.Window && !anim && flying;
        windowMask.gameObject.SetActive(win);
        panel.gameObject.SetActive(flying && !anim);
        float scale = hud.GetComponentInParent<Canvas>().scaleFactor;
        var cam = fd.Cam;
        // the LPD scale on the glass, and where the computer is aiming
        bool lpdOn = win && Now == Stage.Descent && t > Pitchover + 30f;
        lpdRoot.gameObject.SetActive(lpdOn);
        float lpdDeg = 0f;
        Vector3 f = lm.forward, r = lm.right;
        if (lpdOn)
        {
            Vector3 eye = cam.transform.position;
            foreach (var (tk, lb, deg) in ticks)
            {
                Vector3 dir = Quaternion.AngleAxis(deg, r) * f;
                Vector3 sp = cam.WorldToScreenPoint(eye + dir * 100f);
                bool vis = sp.z > 0;
                tk.gameObject.SetActive(vis); if (lb) lb.gameObject.SetActive(vis);
                tk.anchoredPosition = (Vector2)sp / scale;
                if (lb) lb.rectTransform.anchoredPosition = (Vector2)sp / scale + new Vector2(16, 0);
            }
            Vector3 target = siteRot * (new Vector3(0f, terrain.HeightAt(0f, 0f, true), 0f) - pos);
            Vector3 ts = cam.WorldToScreenPoint(target);
            lpdMark.gameObject.SetActive(ts.z > 0);
            lpdMark.anchoredPosition = (Vector2)ts / scale;
            Vector3 td = (target - eye).normalized;
            lpdDeg = Vector3.SignedAngle(f, Vector3.ProjectOnPlane(td, r), r);
        }
        // the dust over the glass
        float dk = DustAmount(AltAbove(pos));
        var vi = veil.GetComponent<RawImage>();
        vi.color = new Color(0.8f, 0.78f, 0.75f, win ? dk * (Problem == Trouble.Dust ? 0.92f : 0.55f) : 0f);
        if (!panel.gameObject.activeSelf) return;
        // the numbers
        float alt = AltAbove(pos);
        Vector3 radial = (pos - C).normalized, heading = Vector3.ProjectOnPlane(-a3, radial).normalized, right = Vector3.Cross(radial, heading);
        float sink = Vector3.Dot(vel, radial), vF = Vector3.Dot(vel, heading), vL = Vector3.Dot(vel, right);
        string prog = Now == Stage.PrePdi ? "P63  BEFORE IGNITION" : Now == Stage.Manual ? "P66  BY HAND" : t < HighGate ? "P63  BRAKING" : "P64  APPROACH";
        var sb = new System.Text.StringBuilder();
        sb.Append($"<color=#9aa2aa>PROGRAM</color>   {prog}\n");
        sb.Append($"<color=#9aa2aa>HEIGHT</color>    {(alt >= 1000f ? $"{alt / 1000f:0.00} KM" : $"{alt:0.0} M")}\n");
        sb.Append($"<color=#9aa2aa>DOWN</color>      {-sink:0.0} M/S\n");
        sb.Append($"<color=#9aa2aa>FORWARD</color>   {(Mathf.Abs(vF) > 100f ? $"{vF * 3.6f:#,0} KM/H" : $"{vF:0.0} M/S")}\n");
        if (Now == Stage.Manual)
        {
            string fs = fuelOut ? "EMPTY" : fuel > 0f ? $"{fuel:0} S TO BINGO" : $"BINGO  {Mathf.Max(bingoLeft, 0f):0} S LEFT";
            bool warn = fuel < 30f && Mathf.Repeat(Time.time, 0.8f) < 0.4f;
            sb.Append($"<color=#9aa2aa>FUEL</color>      {(warn ? "<color=#ffffff><b>" : "")}{fs}{(warn ? "</b></color>" : "")}\n");
        }
        else if (lpdOn) sb.Append($"<color=#9aa2aa>LPD</color>       {lpdDeg:0}° ON THE SCALE\n");
        if (Problem == Trouble.Radar && Now == Stage.Descent && !radarOk) sb.Append("<color=#9aa2aa>RADAR</color>     <b>NO LOCK</b>\n");
        else if (Now == Stage.Descent || Now == Stage.Manual) sb.Append("<color=#9aa2aa>RADAR</color>     DATA GOOD\n");
        if (contact) sb.Append("<b>CONTACT LIGHT</b>\n");
        panelText.text = sb.ToString();
        cross.gameObject.SetActive(Now == Stage.Manual);
        crossDot.anchoredPosition = new Vector2(Mathf.Clamp(vL / 3f, -1f, 1f) * 50f, Mathf.Clamp(vF / 3f, -1f, 1f) * 50f);
    }

    // ---------------- Apollo 14's breaker panel ----------------

    readonly string[] BreakerNames = { "LDG RDR HTR", "RNDZ RADAR", "LGC/DSKY", "S-BAND ANT", "AGS", "LDG RADAR", "SUIT FAN 1", "VHF A XMTR", "ANUN/DOCK", "RCS QUAD 1" };
    readonly bool[] pulled = new bool[10];
    readonly List<Image> breakerRings = new List<Image>();
    bool ldgCycled;

    void BreakerPanel(bool on)
    {
        if (breakers == null && on)
        {
            breakers = UiKit.Rect(hud, "Breakers", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(28, 60), new Vector2(420, 230));
            UiKit.Box(breakers, new Color(0.02f, 0.022f, 0.026f, 0.94f), 12);
            UiKit.OutlineBox(UiKit.Full(breakers, "Edge"), new Color(1f, 1f, 1f, 0.16f));
            UiKit.Label(breakers, "PANEL 11  ·  CIRCUIT BREAKERS  ·  CLICK: PULL / PUSH", 10, UiKit.Text2, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -12), new Vector2(400, 14), TextAlignmentOptions.Center, true, 2);
            for (int i = 0; i < BreakerNames.Length; i++)
            {
                int k = i;
                var b = UiKit.Rect(breakers, "B" + i, new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(46 + (i % 5) * 82, -70 - (i / 5) * 86), new Vector2(40, 40));
                UiKit.Button(b, new Color(1f, 1f, 1f, 0.001f), 20, () => ClickBreaker(k));
                var ring = UiKit.Rect(b, "Ring", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(30, 30)).gameObject.AddComponent<Image>();
                ring.sprite = UiKit.Ring(64); ring.raycastTarget = false; ring.color = new Color(1f, 1f, 1f, 0.75f);
                breakerRings.Add(ring);
                UiKit.Label(b, BreakerNames[i], 10, UiKit.Text, new Vector2(0.5f, 0), new Vector2(0.5f, 1), new Vector2(0, -6), new Vector2(84, 26), TextAlignmentOptions.Top, true, 0);
            }
        }
        if (breakers == null) return;
        breakers.gameObject.SetActive(on);
        if (on) { for (int i = 0; i < pulled.Length; i++) pulled[i] = false; ldgCycled = false; RefreshBreakers(); StartCoroutine(BreakerAuto()); }
    }

    IEnumerator BreakerAuto()
    {
        if (!(Auto || Problems.I.Auto)) yield break;
        yield return new WaitForSeconds(1.2f); ClickBreaker(5);
        yield return new WaitForSeconds(0.8f); ClickBreaker(5);
    }

    void RefreshBreakers()
    {
        for (int i = 0; i < breakerRings.Count; i++) { breakerRings[i].color = pulled[i] ? Color.white : new Color(1f, 1f, 1f, 0.45f); breakerRings[i].rectTransform.localScale = Vector3.one * (pulled[i] ? 1.25f : 1f); }
    }

    void ClickBreaker(int i)
    {
        if (!radarFixing || anim) return;
        pulled[i] = !pulled[i];
        RefreshBreakers();
        string n = BreakerNames[i];
        if (i == 2 && pulled[i])
        {
            StartCoroutine(Abort(false, "COMPUTER OFF", "You pulled LGC/DSKY: the guidance computer's own power. Eagle lost its steering in the middle of the braking burn, and Houston called an abort.",
                "Read the labels before you pull: the radar's breaker is LDG RADAR. LGC is the computer itself.", RewindRadar));
            return;
        }
        if (i == 5)
        {
            if (!pulled[i] && ldgCycled) { StartCoroutine(Lock()); return; }
            if (pulled[i]) { ldgCycled = true; fd.Explain("LDG RADAR out: the radar is off. Click it again to push it back in.", 4f); }
            return;
        }
        if (pulled[i]) fd.Explain(i == 0 ? "LDG RDR HTR is the radar's heater, not its power. Push it back in." : $"{n} is not the radar. Push it back in.", 4f);
    }

    IEnumerator Lock()
    {
        fd.Explain("LDG RADAR back in: the radar starts again...", 2f);
        yield return new WaitForSeconds(1.2f);
        RadarFixed();
    }

    // ---------------- Apollo 14's fix on the DSKY ----------------

    const string FixKeys = "V21N01E1010E107E";
    string typed = "";
    bool oprErr;
    float dskyClock;

    void Dsky(bool on)
    {
        if (dsky == null && on)
        {
            dsky = UiKit.Rect(hud, "Dsky", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(28, 60), new Vector2(430, 300));
            UiKit.Box(dsky, new Color(0.02f, 0.022f, 0.026f, 0.95f), 12);
            UiKit.OutlineBox(UiKit.Full(dsky, "Edge"), new Color(1f, 1f, 1f, 0.16f));
            UiKit.Label(dsky, "DSKY  ·  HOUSTON READS UP:", 10, UiKit.Text2, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -12), new Vector2(400, 14), TextAlignmentOptions.Center, true, 3);
            UiKit.Label(dsky, "VERB 21  NOUN 01  ENTER\n1010  ENTER\n107  ENTER", 15, UiKit.Text, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -34), new Vector2(400, 64), TextAlignmentOptions.Center, true, 2);
            dskyText = UiKit.Label(dsky, "", 22, UiKit.Text, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -112), new Vector2(400, 34), TextAlignmentOptions.Center, true, 3);
            string[] keys = { "V", "N", "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "ENTR", "RSET" };
            for (int i = 0; i < keys.Length; i++)
            {
                string k = keys[i];
                var b = UiKit.Rect(dsky, "K" + k, new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(44 + (i % 7) * 57, -178 - (i / 7) * 48), new Vector2(50, 40));
                UiKit.OutlineBox(UiKit.Full(b, "Edge"), new Color(1f, 1f, 1f, 0.3f));
                UiKit.Button(b, new Color(1f, 1f, 1f, 0.001f), 8, () => Key(k == "ENTR" ? 'E' : k == "RSET" ? 'R' : k[0]));
                UiKit.Label(b, k, 12, UiKit.Text, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(50, 20), TextAlignmentOptions.Center, true, 1);
            }
            dskyHint = UiKit.Label(dsky, "KEYBOARD: V, N, DIGITS, ENTER  ·  BACKSPACE: RSET", 9, UiKit.Text2, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 8), new Vector2(400, 12), TextAlignmentOptions.Center, true, 2);
        }
        if (dsky == null) return;
        dsky.gameObject.SetActive(on);
        if (on) { typed = ""; oprErr = false; dskyClock = 45f; ShowDsky(); }
        else Problems.I.PressureOff();
    }

    void DskyUpdate(float dt)
    {
        if (dsky == null || !dsky.gameObject.activeSelf) return;
        dskyClock -= dt;
        Problems.I.Pressure("ENGINE IGNITION IN", dskyClock, 45f);
        foreach (char c in Input.inputString)
        {
            char u = char.ToUpperInvariant(c);
            if (u == '\r' || u == '\n' || u == '+') Key('E');
            else if (u == '\b') Key('R');
            else if (u == 'V' || u == 'N' || char.IsDigit(u)) Key(u);
        }
        if ((Auto || Problems.I.Auto) && Time.frameCount % 10 == 0 && typed.Length < FixKeys.Length) Key(oprErr ? 'R' : FixKeys[typed.Length]);
        if (dskyClock <= 0f)
        {
            Dsky(false);
            StartCoroutine(FakeAbortPlays(false));
        }
    }

    void Key(char k)
    {
        if (Now != Stage.PrePdi || anim) return;
        if (k == 'R') { oprErr = false; int cut = typed.LastIndexOf('E'); typed = cut >= 0 ? typed.Substring(0, cut + 1) : ""; ShowDsky(); return; }
        if (oprErr) return;
        if (typed.Length < FixKeys.Length && FixKeys[typed.Length] == k) typed += k;
        else { oprErr = true; fd.Explain("OPR ERR: wrong key. Press RSET (Backspace) and type that part again.", 4f); }
        ShowDsky();
        if (typed == FixKeys) StartCoroutine(FixDone());
    }

    void ShowDsky()
    {
        if (dskyText == null) return;
        string s = typed.Replace("V", "V").Replace("N", " N").Replace("E", " E ");
        dskyText.text = oprErr ? "<b>OPR ERR</b>" : (s.Length == 0 ? "_" : s.Trim() + " _");
    }

    IEnumerator FixDone()
    {
        Dsky(false);
        yield return new WaitForSeconds(0.4f);
        holding = true;
        Problems.I.Info("FIX ENTERED  ·  WHY IT WORKED",
            "Address 1010 is the register that says which program is running. Writing 107 there (71 in the computer's base 8) made the computer believe an abort program was already running, so it ignored the ABORT button.\n\n"
          + "The real fix had more steps after ignition: Alan Shepard pushed the throttle up by hand, and Ed Mitchell set the register back to the landing program so the radar data was used. Apollo 14 landed 26 m from its target.",
            () => { holding = false; StartPdi(); });
    }

    void StartPdi()
    {
        Now = Stage.Descent; t = 0f; fired.Clear();
        view = View.Outside; orbitYaw = 25f; orbitPitch = 14f;
        fd.AddEvent("POWERED DESCENT");
        fd.Explain($"PDI: the descent engine lights, 15 km up and {RangeAt(0f) / 1000f:0} km from your site. Eagle flies feet first, windows up: the crew sees only black sky. C: their view.", 9f);
    }
}
