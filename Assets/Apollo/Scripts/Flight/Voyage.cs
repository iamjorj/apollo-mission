// Step 6: from Earth orbit to the Moon, in about a minute and a half of play, on Apollo 11's real timeline
// (Ground Elapsed Time from the Apollo 11 Mission Report and the Apollo Flight Journal):
//   - Earth orbit: weightless; unstrap and float around Columbia (U), down to the lower equipment bay
//   - 2:44:16 TLI: the S-IVB fires again for 5 min 47 s, 28,000 -> 39,000 km/h (the Saturn's own computer flies it)
//   - 3:17 transposition and docking: Columbia separates, the adapter panels open, Columbia turns around and the
//     student flies it onto Eagle's docking target (translation only, real closing speeds); an autopilot helps
//   - the 3-day coast as a time-lapse with a distance line and four real moments (Earth a whole ball; Earth's
//     gravity slowing us to the slowest speed; the Moon's pull taking over; the Moon hiding the Sun)
//   - 75:41 LOS behind the Moon, 75:49:50 LOI: the computer asks (Verb 99) and the student presses PRO; the
//     service module's engine burns for 6 min; 76:15 AOS; the Moon fills the windows
// Positions are physical where it shows (speeds and distances from the real conics: TLI cutoff 10.84 km/s, the
// Moon's sphere of influence at 62,600 km at 1 km/s, 2.5 km/s at the Moon before LOI, 112 x 314 km after), on the
// planner's date: the path is bent so it really meets the Moon for that date, passes through the Moon's shadow
// before LOI (as Apollo 11 did) and its lunar orbit passes over the student's landing site.
using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class Voyage : MonoBehaviour
{
    // ---------------- the real timeline (GET, s) ----------------
    const double Insertion = 709.33, TliIgn = 9856.2, TliCut = 10203.0, SepGet = 11824.6, DockGet = 12243.7, ExtractGet = 15423.0;
    const double Day1Get = 36000.0, SlowGet = 208800.0, SoiGet = 221995.0, LosGet = 272483.0, LoiIgn = 272990.0, LoiCut = 273347.5, AosGet = 274529.0;
    double shadowGet = 269390.0;                                    // set from the geometry: the middle of the pass through the Moon's shadow

    // ---------------- physics ----------------
    const double MuE = 398600.4, MuM = 4902.8, ReKm = 6378.137, RmKm = 1737.4;
    const double ParkR = 6378.165 + 191.082, ParkV = 7.7931;
    const double CutR = 6712.0, CutV = 10.838, CutGamma = 7.4 * Math.PI / 180.0;   // TLI cutoff (radius km, km/s, flight path angle)
    const double HypE = 1.498, HypA = -3709.0;                     // approach hyperbola (from 1.216 km/s at 62,600 km, periselene 111 km)
    const double EllA = 1951.0, EllE = 0.0518;                     // after LOI: 112 x 314 km

    FlightDirector fd;
    Ascent climb;
    Exhaust exhaust;
    Transform stack, booster, lm, eye;
    Transform[] slaPanels = new Transform[4];
    Vector3 windowLocal;                                            // the commander's left window direction, stack frame
    Vector3 cgLocal;

    public enum Part { Orbit, Align, Float, ToTli, Tli, AfterTli, Explainer, Separation, Docking, Docked, Coast, Pro, Loi, Lunar }
    public Part Now { get; private set; } = Part.Orbit;
    public float PartTime { get; private set; }
    public double Get { get; private set; }
    public bool Auto;                                               // the self-test plays itself
    public bool AllowOutsideToggle => Now != Part.Docking && landing == null;
    Landing landing;                                                // step 7, once Eagle undocks
    public float RoarLow { get; private set; }
    public string LastBeat { get; private set; } = "";

    double rate = 1.0;
    DateTime t0;
    Vector3d sc;                                                    // where we are (km, ICRF, geocentric)
    double speedKms; string speedRef = "";
    double earthKm, moonKm;
    float g;
    // planes
    Vector3d orbN, orbX0;                                           // parking orbit normal and insertion direction
    double thetaCut;                                                // angle along the parking plane at TLI cutoff
    double eCon, pCon, aCon, nuCut, mCut, nCon;                     // the conic after TLI
    Vector3d hypP, hypQ;                                            // periselene direction and direction of motion there (Moon frame)
    Vector3d soiTarget;                                             // where we enter the Moon's sphere of influence (geocentric, at SoiGet)
    double tPeri;                                                   // GET of periselene
    float[] tliTheta, tliR, tliV;

    // the student
    Vector3 floatPos, floatVel; float floatYaw, floatPitch; Quaternion floatBase;
    Vector3 dockRel, dockVel;                                       // the LM drogue relative to Columbia's probe: x, y lateral, z range (m)
    float idle, beatWait = -1f; int beat;
    bool proPressed, unstrapped, proClicked, floating;
    float floatTime;
    public bool Floating => floating;
    double proAt; float aosAt;
    Transform pen;
    ParticleSystem dust;

    // UI
    RectTransform hud, actionPill, lineRoot, shipMark, coas, dsky, explainer, exCsm, exEagle, exAdapter, scope, scopeArrow, tags, tagCol, tagEagle, tagStage, scoreCard, speedBand, speedMark, targetRing;
    TextMeshProUGUI actionText, lineText, coasText, dskyR1, dskyVerb, dskyNoun, exText, exCsmLabel, scopeText, scoreText;
    TextMeshProUGUI[] arrows; Image[][] arrowMarks; RectTransform starMark; TextMeshProUGUI starName, dockState;
    AudioSource clunk;
    RadioCue radio;

    public void Begin(FlightDirector director, Ascent a, RectTransform hudRoot)
    {
        fd = director; climb = a; hud = hudRoot;
        stack = a.Stack; eye = fd.CommanderEye;
        exhaust = GetComponent<Exhaust>();
        t0 = MissionState.LaunchUtc;
        Get = a.Met;
        climb.enabled = false;                                      // from here on Voyage flies
        windowLocal = Quaternion.Inverse(stack.rotation) * (eye.rotation * Quaternion.Euler(-17f, -79f, 0f) * Vector3.forward);
        cgLocal = stack.InverseTransformPoint(Saturn("SM") ? Saturn("SM").position : stack.position);
        Plan();
        BuildBooster();
        stackPos0 = stack.position;
        BuildUi();
        clunk = gameObject.AddComponent<AudioSource>(); clunk.playOnAwake = false; clunk.spatialBlend = 0f;
        radio = RadioCue.On(gameObject); radio.Preload();
        fd.OutsideFocus = () =>
        {
            Vector3 c = stack.TransformPoint(cgLocal);
            if (booster != null && booster.gameObject.activeSelf && (Now == Part.Separation || Now == Part.Docking || Now == Part.Docked))
                return ((c + LmTop()) * 0.5f, 70f);
            return (c, Now <= Part.AfterTli ? 60f : 40f);
        };
        Say("ORBIT INSERTION", "In Earth orbit. You are weightless now: press U to unstrap and float around Columbia.", 9f);
        Debug.Log($"VOYAGE begin GET {Get:0.0}; shadow pass at GET {shadowGet / 3600:0.00} h; site {SiteName()}");
    }

    Transform Saturn(string n)
    {
        foreach (var t in fd.SaturnRoot.GetComponentsInChildren<Transform>(true)) if (t.name == n) return t;
        return null;
    }

    // ---------------- the plan: planes, conics, the bend to the Moon ----------------

    DateTime Utc(double get) => t0.AddSeconds(get);
    static Vector3d V(double x, double y, double z) => new Vector3d(x, y, z);
    static Vector3d Add(Vector3d a, Vector3d b) => V(a.x + b.x, a.y + b.y, a.z + b.z);
    static Vector3d Mul(Vector3d a, double k) => V(a.x * k, a.y * k, a.z * k);
    static double Dot(Vector3d a, Vector3d b) => a.x * b.x + a.y * b.y + a.z * b.z;
    static Vector3d Cross(Vector3d a, Vector3d b) => V(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
    static Vector3d Norm(Vector3d a) { double m = a.Magnitude; return m > 0 ? Mul(a, 1.0 / m) : a; }
    static Vector3d Rot(Vector3d v, Vector3d k, double ang) => Add(Mul(v, Math.Cos(ang)), Add(Mul(Cross(k, v), Math.Sin(ang)), Mul(k, Dot(k, v) * (1 - Math.Cos(ang)))));
    static Vector3d Slerp(Vector3d a, Vector3d b, double s)
    {
        double c = Math.Max(-1, Math.Min(1, Dot(a, b))), w = Math.Acos(c);
        if (w < 1e-6) return a;
        return Add(Mul(a, Math.Sin((1 - s) * w) / Math.Sin(w)), Mul(b, Math.Sin(s * w) / Math.Sin(w)));
    }

    // ECEF (km) of a point at geocentric lat, lon -> ICRF at a time
    static Vector3d EcefToIcrf(Vector3d e, DateTime utc)
    {
        double g = Ephemeris.GmstDeg(utc) * Math.PI / 180.0;
        return V(Math.Cos(g) * e.x - Math.Sin(g) * e.y, Math.Sin(g) * e.x + Math.Cos(g) * e.y, e.z);
    }

    void Plan()
    {
        // parking orbit through the insertion point (32.50 N, 52.69 W, heading 88.85 deg; Ascent.OrbitTrack)
        double D = Math.PI / 180.0, la = 32.5027 * D, lo = -52.694 * D, hd = 88.848 * D;
        var up = V(Math.Cos(la) * Math.Cos(lo), Math.Cos(la) * Math.Sin(lo), Math.Sin(la));
        var east = V(-Math.Sin(lo), Math.Cos(lo), 0);
        var north = V(-Math.Sin(la) * Math.Cos(lo), -Math.Sin(la) * Math.Sin(lo), Math.Cos(la));
        var vdir = Add(Mul(north, Math.Cos(hd)), Mul(east, Math.Sin(hd)));
        var r0 = EcefToIcrf(up, Utc(Insertion));
        var v0 = EcefToIcrf(vdir, Utc(Insertion));
        // the Earth turns under the ECEF velocity: the inertial heading differs a little; fine for a picture
        orbX0 = Norm(r0); orbN = Norm(Cross(r0, v0));

        // the TLI burn: S-IVB J-2, 1,023 kN then 890 kN, 135 t -> 65.5 t, 347 s; radius 6,570 -> 6,712 km
        int n = 348; tliTheta = new float[n]; tliR = new float[n]; tliV = new float[n];
        double w = ParkV / ParkR, th = w * (TliIgn - Insertion), m0 = 135000, mdot = (135000 - 65500) / 346.8, ve = 4.2;
        for (int i = 0; i < n; i++)
        {
            double t = i, m = m0 - mdot * t;
            double v = Math.Min(CutV, ParkV + ve * Math.Log(m0 / m));
            double r = ParkR + (CutR - ParkR) * SmoothStep(t / 346.8);
            tliTheta[i] = (float)th; tliR[i] = (float)r; tliV[i] = (float)v;
            th += v * Math.Cos(CutGamma * SmoothStep(t / 346.8)) / r;
        }
        thetaCut = tliTheta[n - 1];
        // the conic after cutoff
        double eps = CutV * CutV / 2 - MuE / CutR;
        aCon = -MuE / (2 * eps);
        double h = CutR * CutV * Math.Cos(CutGamma);
        pCon = h * h / MuE; eCon = Math.Sqrt(1 - pCon / aCon);
        nuCut = Math.Acos(Math.Max(-1, Math.Min(1, (pCon / CutR - 1) / eCon)));
        double Ecut = 2 * Math.Atan(Math.Sqrt((1 - eCon) / (1 + eCon)) * Math.Tan(nuCut / 2));
        mCut = Ecut - eCon * Math.Sin(Ecut); nCon = Math.Sqrt(MuE / (aCon * aCon * aCon));

        // the Moon end: the landing site (the student's, else Apollo 11's), the Sun at LOI, the plane through both
        double sla = MissionState.HasSite ? MissionState.SiteLat : 0.674, slo = MissionState.HasSite ? MissionState.SiteLon : 23.473;
        DateTime land = Utc(369940.0);                                    // 102:45:40, the landing
        Ephemeris.MoonAxes(land, out var mw, out var ew, out var pw);
        Vector3 b = Ephemeris.BodyPoint(sla, slo);
        Vector3 sw = mw * b.x + pw * b.y + ew * b.z;                       // planner world (x, z, y)
        var site = Norm(V(sw.x, sw.z, sw.y));
        DateTime loi = Utc(LoiIgn);
        var moon = Ephemeris.MoonKm(loi); var sun = Ephemeris.SunKm(loi);
        var u = Norm(V(sun.x - moon.x, sun.y - moon.y, sun.z - moon.z));   // toward the Sun, from the Moon
        var eDir = Norm(Mul(moon, -1));                                     // toward the Earth, from the Moon
        var anti = Mul(u, -1);
        var nrm = Cross(site, anti);
        if (nrm.Magnitude < 0.1) nrm = Cross(site, V(0, 0, 1));
        nrm = Norm(nrm);
        // 1 h before periselene the approach is 101.6 deg back along the hyperbola: put that point on the shadow's axis
        double back = 101.6 * D;
        Vector3d best = default, bestK = default; double bestDot = 2;
        foreach (var k in new[] { nrm, Mul(nrm, -1) })
        {
            var P = Rot(anti, k, back);
            double dd = Dot(P, eDir);
            if (dd < bestDot) { bestDot = dd; best = P; bestK = k; }
        }
        hypP = best; hypQ = Norm(Cross(bestK, hypP));
        tPeri = LoiIgn + 178.75;
        shadowGet = tPeri - 3600.0;
        soiTarget = Add(Ephemeris.MoonKm(Utc(SoiGet)), MoonOffset(SoiGet));
    }

    static double SmoothStep(double x) { x = Math.Max(0, Math.Min(1, x)); return x * x * (3 - 2 * x); }

    // position on the approach hyperbola (Moon frame, km) at a GET, and the speed there
    Vector3d Hyper(double get, out double v)
    {
        double t = get - tPeri, nH = Math.Sqrt(MuM / (-HypA * -HypA * -HypA));
        double M = nH * t, F = Math.Sign(M) * Math.Log(2 * Math.Abs(M) / HypE + 1.8);
        for (int i = 0; i < 30; i++) F -= (HypE * Math.Sinh(F) - F - M) / (HypE * Math.Cosh(F) - 1);
        double nu = 2 * Math.Atan(Math.Sqrt((HypE + 1) / (HypE - 1)) * Math.Tanh(F / 2));
        double p = HypA * (1 - HypE * HypE), r = p / (1 + HypE * Math.Cos(nu));
        v = Math.Sqrt(MuM * (2 / r - 1 / HypA));
        return Mul(Add(Mul(hypP, Math.Cos(nu)), Mul(hypQ, Math.Sin(nu))), r);
    }

    // after LOI: the 112 x 314 km ellipse, periselene where the burn was
    Vector3d Ellipse(double get, out double v)
    {
        double nE = Math.Sqrt(MuM / (EllA * EllA * EllA)), M = nE * (get - tPeri);
        double E = M; for (int i = 0; i < 12; i++) E -= (E - EllE * Math.Sin(E) - M) / (1 - EllE * Math.Cos(E));
        double nu = 2 * Math.Atan2(Math.Sqrt(1 + EllE) * Math.Sin(E / 2), Math.Sqrt(1 - EllE) * Math.Cos(E / 2));
        double r = EllA * (1 - EllE * Math.Cos(E));
        v = Math.Sqrt(MuM * (2 / r - 1 / EllA));
        return Mul(Add(Mul(hypP, Math.Cos(nu)), Mul(hypQ, Math.Sin(nu))), r);
    }

    Vector3d MoonOffset(double get) { return Hyper(get, out _); }

    Vector3d InPlane(double theta, double r) => Mul(Add(Mul(orbX0, Math.Cos(theta)), Mul(Cross(orbN, orbX0), Math.Sin(theta))), r);

    // where the spacecraft is at a GET (km, ICRF, geocentric) and how fast it goes (km/s, relative to what)
    Vector3d Where(double get, out double v, out string rel)
    {
        rel = "";
        if (get < TliIgn)
        {
            v = ParkV;
            return InPlane(ParkV / ParkR * (get - Insertion), ParkR);
        }
        if (get < TliCut)
        {
            int i = Mathf.Clamp((int)(get - TliIgn), 0, tliTheta.Length - 2); float f = (float)(get - TliIgn - i);
            v = Mathf.Lerp(tliV[i], tliV[i + 1], f);
            return InPlane(Mathf.Lerp(tliTheta[i], tliTheta[i + 1], f), Mathf.Lerp(tliR[i], tliR[i + 1], f));
        }
        if (get < SoiGet)
        {
            double M = mCut + nCon * (get - TliCut);
            double E = M; for (int i = 0; i < 20; i++) E -= (E - eCon * Math.Sin(E) - M) / (1 - eCon * Math.Cos(E));
            double nu = 2 * Math.Atan2(Math.Sqrt(1 + eCon) * Math.Sin(E / 2), Math.Sqrt(1 - eCon) * Math.Cos(E / 2));
            double r = aCon * (1 - eCon * Math.Cos(E));
            v = Math.Sqrt(MuE * (2 / r - 1 / aCon));
            var conic = InPlane(thetaCut + (nu - nuCut), r);
            // bent toward the real Moon for this date: by the sphere of influence we are exactly where the approach starts
            double s = SmoothStep((get - TliCut) / (SoiGet - TliCut));
            s = s * s * (3 - 2 * s);
            double rEnd = soiTarget.Magnitude, rConEnd = ConicR(SoiGet);
            var dir = Slerp(Norm(conic), Norm(soiTarget), s);
            return Mul(dir, r * (1 + (rEnd / rConEnd - 1) * s));
        }
        var moonNow = Ephemeris.MoonKm(Utc(get));
        rel = "MOON";
        if (get < LoiIgn) return Add(moonNow, Hyper(get, out v));
        if (get < LoiCut)
        {
            double k = SmoothStep((get - LoiIgn) / (LoiCut - LoiIgn));
            var a = Hyper(get, out double va); var b = Ellipse(get, out double vb);
            v = va + (vb - va) * k;
            return Add(moonNow, Add(Mul(a, 1 - k), Mul(b, k)));
        }
        return Add(moonNow, Ellipse(get, out v));
    }

    double ConicR(double get)
    {
        double M = mCut + nCon * (get - TliCut);
        double E = M; for (int i = 0; i < 20; i++) E -= (E - eCon * Math.Sin(E) - M) / (1 - eCon * Math.Cos(E));
        return aCon * (1 - eCon * Math.Cos(E));
    }

    // ---------------- the vehicle: the booster group, the adapter panels, Eagle ----------------

    void BuildBooster()
    {
        booster = new GameObject("Booster").transform;
        booster.SetParent(stack, false);
        foreach (var n in new[] { "S-IVB", "IU", "SLA" })
        {
            var t = Saturn(n);
            if (t) t.SetParent(booster, true);
        }
        var sla = booster.Find("SLA");
        // the adapter as four panels that hinge open at the bottom (the real SLA's four panels)
        var slaMat = sla ? sla.GetComponentInChildren<Renderer>(true)?.sharedMaterial : null;
        if (sla) foreach (var r in sla.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        float y0 = 85.57f, y1 = 94.11f, r0 = 3.30f, r1 = 1.955f;
        for (int k = 0; k < 4; k++)
        {
            var hinge = new GameObject("SLA_Panel_" + (k + 1)).transform;
            hinge.SetParent(booster, false);
            float a = (k * 90f + 45f) * Mathf.Deg2Rad;
            Vector3 radial = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            hinge.position = fd.SaturnRoot.TransformPoint(radial * r0 + Vector3.up * y0);
            hinge.rotation = fd.SaturnRoot.rotation * Quaternion.LookRotation(radial, Vector3.up);
            var mf = new GameObject("Panel").AddComponent<MeshFilter>();
            mf.transform.SetParent(hinge, false);
            mf.sharedMesh = PanelMesh(r0, r1, y1 - y0, a);
            var mr = mf.gameObject.AddComponent<MeshRenderer>();
            mr.sharedMaterial = slaMat ? slaMat : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            slaPanels[k] = hinge;
        }
        // Eagle, folded in the adapter: descent stage on the IU, its docking drogue up toward Columbia
        lm = LmModel.Build(booster, fd.SaturnRoot.TransformPoint(Vector3.up * 86.2f), fd.SaturnRoot.rotation);
    }

    // a quarter of the cone, in the hinge's frame (z out, y up, x along the hinge)
    Mesh PanelMesh(float r0, float r1, float h, float centre)
    {
        const int seg = 24;
        var v = new System.Collections.Generic.List<Vector3>(); var t = new System.Collections.Generic.List<int>();
        var n = new System.Collections.Generic.List<Vector3>();
        Vector3 hingePt = new Vector3(Mathf.Sin(centre) * r0, 0f, Mathf.Cos(centre) * r0);
        Quaternion toLocal = Quaternion.Inverse(Quaternion.LookRotation(new Vector3(Mathf.Sin(centre), 0, Mathf.Cos(centre)), Vector3.up));
        for (int i = 0; i <= seg; i++)
        {
            float a = centre - Mathf.PI / 4 + Mathf.PI / 2 * i / seg;
            Vector3 dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            Vector3 p0 = dir * r0, p1 = dir * r1 + Vector3.up * h;
            v.Add(toLocal * (p0 - hingePt)); v.Add(toLocal * (p1 - hingePt));
            Vector3 nn = toLocal * (dir * h + Vector3.up * (r0 - r1)).normalized;
            n.Add(nn); n.Add(nn);
        }
        for (int i = 0; i < seg; i++)
        {
            int a = i * 2;
            t.AddRange(new[] { a, a + 1, a + 2, a + 2, a + 1, a + 3 });
            t.AddRange(new[] { a, a + 2, a + 1, a + 2, a + 3, a + 1 });           // both sides: the inside shows when open
        }
        var m = new Mesh(); m.SetVertices(v); m.SetNormals(n); m.SetTriangles(t, 0); m.RecalculateBounds();
        return m;
    }

    Vector3 ProbeTip() => fd.SaturnRoot.TransformPoint(Vector3.up * 101.85f);
    Vector3 LmTop() => lm ? lm.TransformPoint(LmModel.DrogueLocal) : booster.position;

    // ---------------- every frame ----------------

    void Update()
    {
        if (landing != null) { RoarLow = landing.RoarLow; return; }       // the landing flies now
        float dt = Time.deltaTime;
        PartTime += dt;
        bool any = Input.anyKey || Input.GetMouseButton(0);
        idle = any ? 0f : idle + dt;
        bool deciding = Problems.I != null && Problems.I.Deciding;
        // weightless: U unstraps (and straps back in) whenever nothing needs the crew in the couch; before an engine
        // burn, the docking or a manoeuvre the game straps you back in
        bool calm = (Now == Part.Orbit || Now == Part.Coast || Now == Part.Lunar) && !mccOn && !(Now == Part.Orbit && Problems.I != null && Problems.I.LightningRun && !aligned);
        if (floating) floatTime += dt;
        if (!deciding && calm && (Input.GetKeyDown(KeyCode.U) || (Auto && !floating && Now == Part.Orbit && PartTime > 1.5f && floatTime == 0f))) { if (floating) Strap(); else Unstrap(); }
        if (floating && (!calm || (Auto && floatTime > 5f))) { Strap(); if (!Auto) fd.Explain("Back in your couch: the next step needs the crew strapped in.", 4f); }
        switch (Now)
        {
            case Part.Orbit:
                rate = 20;
                if (Problems.I != null && Problems.I.LightningRun && !aligned) { if (PartTime > 3.5f) Go(Part.Align); }
                else if (PartTime > (floating ? 75f : unstrapped ? 6f : 14f)) Go(Part.ToTli);
                break;
            case Part.Align:
                rate = 20;
                AlignUpdate(dt);
                break;
            case Part.Float:
                rate = 20;
                if (Input.GetKeyDown(KeyCode.B) || Input.GetKeyDown(KeyCode.Space) || PartTime > 40f || (Auto && PartTime > 5f)) Strap();
                break;
            case Part.ToTli:
                rate = Math.Max(1, (TliIgn - 6 - Get) / Math.Max(0.4, 6.5 - PartTime));
                if (Get >= TliIgn - 6) { rate = 1; Go(Part.Tli); }
                break;
            case Part.Tli:
                rate = Get < TliIgn + 3 ? 1 : Get < TliCut - 3 ? 100 : 1;
                if (Get >= TliCut + 1.5) Go(Part.AfterTli);
                break;
            case Part.AfterTli:
                rate = Math.Max(1, (SepGet - Get) / Math.Max(0.3, 2.5 - PartTime));
                if (Get >= SepGet) Go(Part.Explainer);
                break;
            case Part.Explainer:
                rate = 0;
                ExplainerUpdate(PartTime);
                if ((PartTime > 1.0f && Input.GetKeyDown(KeyCode.Space)) || PartTime > 9.5f || (Auto && PartTime > 3f)) Go(Part.Separation);
                break;
            case Part.Separation:
                rate = 20;
                SeparationUpdate(PartTime);
                if (PartTime > 7.6f) Go(Part.Docking);
                break;
            case Part.Docking:
                rate = deciding ? 0 : 1;
                if (!deciding) DockingUpdate(dt);
                break;
            case Part.Docked:
                rate = Math.Max(1, (ExtractGet - Get) / Math.Max(0.3, 5.0 - PartTime));
                ExtractionUpdate(PartTime);
                if (PartTime > 5.2f) Go(Part.Coast);
                break;
            case Part.Coast:
                if (deciding) rate = 0; else CoastUpdate();
                break;
            case Part.Pro:
                {
                    rate = Get < LoiIgn - 8 ? Math.Max(1, (LoiIgn - 8 - Get) / Math.Max(0.3, 2.5 - PartTime)) : 1;
                    bool asked = Get >= LoiIgn - 5;
                    bool press = Input.GetKeyDown(KeyCode.P) || Input.GetKeyDown(KeyCode.Return) || proClicked || (Auto && Get >= LoiIgn - 3);
                    proClicked = false;
                    if (press && !proPressed)
                    {
                        if (!asked) fd.Explain("Not yet: wait for VERB 99 to flash. The computer asks 5 seconds before ignition.", 3f);
                        else { proPressed = true; proAt = Get; }
                    }
                    if (Get >= LoiIgn && proPressed) { Problems.I.ProLate = (float)Math.Max(0, proAt - LoiIgn); Go(Part.Loi); }
                    else if (Get >= LoiIgn + 10 && !proPressed && !Problems.I.Deciding)
                    {
                        proPressed = true; rate = 0;
                        Problems.I.Fail("NO BURN", MissionState.Hybrid
                            ? "Nobody pressed PRO, so the engine never fired. Off the free-return path, Columbia swings past the Moon onto a path that misses the Earth. Only another engine burn could bring the crew home: no landing, and a dangerous trip back."
                            : "Nobody pressed PRO, so the engine never fired. On its free-return path Columbia swings around the Moon and falls back toward the Earth, as Apollo 13 did: the crew comes home, but there is no landing.",
                            "Behind the Moon nobody on Earth can help. When VERB 99 flashes the computer is asking for permission: press PRO (or P) before the countdown reaches zero.",
                            () => { Get = LoiIgn - 7; proPressed = false; PartTime = 3f; fd.Explain("REWIND: 7 seconds before the burn.", 4f); });
                    }
                    break;
                }
            case Part.Loi:
                if (deciding) { rate = 0; break; }
                LoiUpdate(dt);
                break;
            case Part.Lunar:
                rate = Get < AosGet ? Math.Max(1, (AosGet - Get) / Math.Max(0.3, 4 - PartTime)) : 30;
                if (Get >= AosGet && LastBeat != "AOS") { LastBeat = "AOS"; aosAt = PartTime; fd.AddEvent("AOS"); Say("AOS", "76:15 · Houston hears us again. Apollo 11 is in orbit around the Moon. Next: the landing at " + SiteName() + ".", 12f); }
                if (LastBeat == "AOS" && PartTime > aosAt + 9f && !endShown) ShowEnd();
                break;
        }
        Get += dt * rate;
        // the real radio at its moments
        if (Now == Part.ToTli && PartTime > 1.0f) radio.Say("tli_go");
        if (Now == Part.Tli && Get > TliIgn + 1.5) radio.Say("tli_ignition");
        if (Now == Part.Tli && Get > TliCut - 1.5) radio.Say("tli_cutoff");
        if (Now == Part.AfterTli && PartTime > 1.2f && !radio.Busy) radio.Say("tli_ride");
        if (Now == Part.Separation && PartTime > 0.8f) radio.Say("sep");
        if (Now == Part.Docked && PartTime > 1.2f) radio.Say("docked");
        if (Now == Part.Pro && PartTime > 0.3f) radio.Say("los");
        if (Now == Part.Lunar && Get >= AosGet) radio.Say("aos");
        if (Now == Part.Lunar && Get >= AosGet && PartTime > 12f && !radio.Busy) radio.Say("perfect");

        // where we are, and the sky from there
        sc = Where(Get, out speedKms, out speedRef);
        earthKm = sc.Magnitude;
        var mk = Ephemeris.MoonKm(Utc(Get));
        moonKm = V(mk.x - sc.x, mk.y - sc.y, mk.z - sc.z).Magnitude;
        fd.SetPlaceFromSpace(Utc(Get), sc);
        Attitude(dt);
        Ride(dt);
        Ui();
    }

    void Go(Part p)
    {
        Now = p; PartTime = 0f;
        switch (p)
        {
            case Part.Align:
                BeginAlign();
                break;
            case Part.ToTli:
                fd.AddEvent("EARTH ORBIT");
                Say("", "2 hours 30 minutes in Earth orbit, over the Pacific. CAPCOM: \"Apollo 11, this is Houston. You are go for TLI.\"", 7f);
                break;
            case Part.Tli:
                fd.AddEvent("TLI IGNITION");
                Say("", "Translunar injection: the third stage fires again for 5 minutes 47 seconds. The Saturn's own computer flies it: watch the speed.", 12f);
                if (exhaust) { exhaust.VoyageBurner = exhaust.Sivb; }
                break;
            case Part.AfterTli:
                fd.AddEvent("TLI CUTOFF");
                Say("", "39,000 km/h: fast enough to coast all the way to the Moon. Earth is already shrinking behind us.", 6f);
                break;
            case Part.Explainer:
                fd.AddEvent("TRANSPOSITION AND DOCKING");
                explainer.gameObject.SetActive(true);
                fd.Explain("", 0.01f);
                break;
            case Part.Separation:
                CmShell(true);                                                         // seen from outside: Columbia whole
                explainer.gameObject.SetActive(false);
                fd.AddEvent("SEPARATION");
                Say("", "3:17 · Columbia separates and backs away 30 m. The adapter's four panels open: Eagle is inside. Watch Columbia turn around.", 8f);
                BeginSeparation();
                fd.CameraOwnedByVoyage = true;
                fd.Cam.cullingMask = ~(1 << 8); fd.Cam.nearClipPlane = 0.3f; fd.Cam.fieldOfView = 40f;
                tags.gameObject.SetActive(true);
                break;
            case Part.Docking:
                CmShell(false);                                                        // the sight looks out of Columbia's window
                tags.gameObject.SetActive(false);
                coas.gameObject.SetActive(true);
                fd.Cam.cullingMask = ~(1 << 8); fd.Cam.nearClipPlane = 0.05f;
                foreach (var pnl in slaPanels) if (pnl) pnl.gameObject.SetActive(false);
                dockRel = new Vector3(0.20f, -0.12f, 12f); dockVel = Vector3.zero; dockTries = 0;
                PlaceForDocking();
                {
                    // the sight sits beside the probe exactly as far as the target sits beside the drogue
                    var tg = lm ? lm.Find("DockingTarget") : null;
                    Vector3 o = tg ? stack.InverseTransformVector(tg.position - LmTop()) : new Vector3(LmModel.TargetOffset.x, 0f, 0f);
                    coasOffset = new Vector3(o.x, 0f, o.z);
                }
                Say("", "You fly now: hold W to go toward Eagle, hold S to brake. Touch at a safe speed (the light band).", 12f);
                break;
            case Part.Docked:
                CmShell(true);
                coas.gameObject.SetActive(false);
                tags.gameObject.SetActive(true);
                fd.Cam.cullingMask = ~(1 << 8); fd.Cam.nearClipPlane = 0.3f; fd.Cam.fieldOfView = 40f;
                fd.AddEvent("DOCKED");
                Say("", "3:24 · Capture, then twelve latches snap shut: docked. An hour later Columbia pulls Eagle out, and the third stage is left behind.", 8f);
                if (lm && lm.parent != stack) lm.SetParent(stack, true);
                break;
            case Part.Coast:
                CmShell(false);
                tags.gameObject.SetActive(false);
                fd.CameraOwnedByVoyage = false;
                fd.SetOutside(false);
                if (booster) booster.gameObject.SetActive(false);
                stack.position = stackPos0;                                            // back to the floating origin
                lineRoot.gameObject.SetActive(true);
                fd.AddEvent("TRANSLUNAR COAST");
                fd.Look(-79f, -17f);
                Say("", "Three days to the Moon in about half a minute. At each stop: SPACE to go on.", 6f);
                break;
            case Part.Pro:
                dsky.gameObject.SetActive(true);
                fd.AddEvent("LOS");
                Say("", "75:41 · Behind the Moon: no radio, nobody can help. When VERB 99 flashes, the computer is asking: burn the engine? Press PRO, on time.", 30f);
                break;
            case Part.Loi:
                dsky.gameObject.SetActive(false);
                fd.AddEvent("LOI IGNITION");
                Say("", Problems.I.ProLate <= 0.5f ? "On time. The service module's engine burns for 6 minutes, slowing us by 3,200 km/h so the Moon can catch us."
                                                   : $"{Problems.I.ProLate:0.0} s late. The engine burns for 6 minutes, slowing us by 3,200 km/h so the Moon can catch us.", 9f);
                if (exhaust) { exhaust.VoyageBurner = Saturn("SM"); }
                burnStart = Math.Max(LoiIgn, proAt); loiCut = false;
                loiFault = new System.Random(Environment.TickCount).NextDouble() < 0.5;
                EmsPanel(true);
                break;
            case Part.Lunar:
                lineRoot.gameObject.SetActive(false);
                fd.AddEvent("LUNAR ORBIT");
                Say("", "In orbit around the Moon, 112 by 314 km. It worked, but nobody on Earth knows yet: we are still behind the Moon.", 8f);
                fd.Look(-79f, 10f);
                break;
        }
        Debug.Log($"VOYAGE {p} at GET {Get:0.0}");
    }

    void CmShell(bool on) { if (fd.HideWhileEntering != null) foreach (var r in fd.HideWhileEntering) if (r && r.name == "CM_Shell") r.enabled = on; }

    void Say(string ev, string line, float s)
    {
        if (ev.Length > 0) fd.AddEvent(ev);
        fd.Explain(line, s);
    }

    // ---------------- weightless ----------------

    void Unstrap()
    {
        unstrapped = true; floating = true; floatTime = 0f;
        fd.CameraOwnedByVoyage = true;
        floatPos = stack.InverseTransformPoint(eye.position); floatVel = Vector3.zero;
        floatBase = Quaternion.Inverse(stack.rotation) * fd.Cam.transform.rotation; floatYaw = 0f; floatPitch = 0f;
        fd.AddEvent("UNSTRAPPED");
        Say("", "Weightless. Drag to look, W/S to drift, A/D sideways, R/F up/down. The lower equipment bay is below your feet. U: back to the couch.", 14f);
        if (!pen)
        {
            pen = GameObject.CreatePrimitive(PrimitiveType.Cylinder).transform;
            Destroy(pen.GetComponent<Collider>());
            pen.name = "Floating_Pen"; pen.localScale = new Vector3(0.012f, 0.07f, 0.012f);
            pen.gameObject.layer = 8;
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")); m.color = new Color(0.08f, 0.08f, 0.09f);
            pen.GetComponent<Renderer>().sharedMaterial = m;
            pen.SetParent(eye.parent, true);
            pen.position = eye.position + eye.forward * 0.45f + eye.up * 0.05f;
            dust = MakeDust();
        }
    }

    void Strap()
    {
        floating = false;
        fd.CameraOwnedByVoyage = false;
    }

    ParticleSystem MakeDust()
    {
        var ps = new GameObject("Cabin_Dust").AddComponent<ParticleSystem>();
        ps.transform.SetParent(eye.parent, false);
        ps.transform.position = eye.position;
        ps.gameObject.layer = 8;
        var main = ps.main; main.startLifetime = 30f; main.startSpeed = 0.01f; main.startSize = 0.0025f; main.maxParticles = 220;
        main.simulationSpace = ParticleSystemSimulationSpace.Local; main.startColor = new Color(1f, 1f, 1f, 0.7f);
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.9f;
        var em = ps.emission; em.rateOverTime = 0f; em.SetBursts(new[] { new ParticleSystem.Burst(0f, 220) });
        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = new Material(fd.TrailMat);
        ps.Play();
        return ps;
    }

    // the camera when Voyage owns it (floating, star sighting, the side view of the turnaround, docking)
    public void CameraUpdate()
    {
        if (landing != null) { landing.CameraUpdate(); return; }
        var cam = fd.Cam.transform;
        if (floating)
        {
            fd.DragLook();
            if (fd.Dragging) { floatYaw += Input.GetAxis("Mouse X") * 2.6f; floatPitch = Mathf.Clamp(floatPitch - Input.GetAxis("Mouse Y") * 2.6f, -85f, 85f); }
            Quaternion look = stack.rotation * floatBase * Quaternion.Euler(floatPitch, floatYaw, 0f);
            Vector3 thrust = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) thrust += Vector3.forward; if (Input.GetKey(KeyCode.S)) thrust += Vector3.back;
            if (Input.GetKey(KeyCode.A)) thrust += Vector3.left; if (Input.GetKey(KeyCode.D)) thrust += Vector3.right;
            if (Input.GetKey(KeyCode.R)) thrust += Vector3.up; if (Input.GetKey(KeyCode.F)) thrust += Vector3.down;
            if (Auto) thrust = new Vector3(0.2f, -0.6f, 0.5f);                         // the self-test drifts toward the lower bay
            floatVel += Quaternion.Inverse(stack.rotation) * (look * thrust) * 0.6f * Time.deltaTime;   // stack frame
            floatVel *= Mathf.Exp(-Time.deltaTime * 0.8f);                              // you catch yourself on handholds
            Vector3 step = floatVel * Time.deltaTime, wpos = stack.TransformPoint(floatPos), wstep = stack.TransformVector(step);
            if (wstep.sqrMagnitude > 1e-10f && Physics.SphereCast(wpos, 0.1f, wstep.normalized, out var hit, wstep.magnitude + 0.02f, 1 << 8))
                floatVel = Vector3.ProjectOnPlane(floatVel, stack.InverseTransformDirection(hit.normal)) * 0.4f;   // bump off the cabin wall
            else floatPos += step;
            float bob = Mathf.Sin(Time.time * 0.7f) * 0.004f;
            cam.SetPositionAndRotation(stack.TransformPoint(floatPos) + stack.up * bob, look * Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 0.31f) * 2f));
            fd.Cam.fieldOfView = 72f;
            if (pen) { pen.Rotate(new Vector3(9f, 23f, 4f) * Time.deltaTime, Space.Self); pen.position += (eye.parent.rotation * new Vector3(0.006f, 0.002f, -0.004f)) * Time.deltaTime; }
            return;
        }
        if (Now == Part.Align)
        {
            // the navigation telescope: drag or W A S D to aim
            if (Input.GetMouseButton(0)) { aimYaw += Input.GetAxis("Mouse X") * 1.6f; aimPitch = Mathf.Clamp(aimPitch - Input.GetAxis("Mouse Y") * 1.6f, -89f, 89f); }
            float k = Time.deltaTime * 22f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) aimYaw -= k;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) aimYaw += k;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) aimPitch = Mathf.Clamp(aimPitch - k, -89f, 89f);
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) aimPitch = Mathf.Clamp(aimPitch + k, -89f, 89f);
            if (Auto && alignStar >= 0)
            {
                var d = AlignDir();
                aimYaw = Mathf.MoveTowardsAngle(aimYaw, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, Time.deltaTime * 60f);
                aimPitch = Mathf.MoveTowards(aimPitch, -Mathf.Asin(d.y) * Mathf.Rad2Deg, Time.deltaTime * 60f);
            }
            cam.SetPositionAndRotation(eye.position, Quaternion.Euler(aimPitch, aimYaw, 0f));
            fd.Cam.fieldOfView = 30f;
            return;
        }
        if (Now == Part.Separation || Now == Part.Docked)
        {
            // a camera beside the stack that stays put: you see Columbia back off, turn around and come back
            Vector3 mid = Vector3.Lerp(LmTop(), stack.TransformPoint(cgLocal), 0.5f);
            cam.SetPositionAndRotation(sepCamPos, Quaternion.LookRotation(mid - sepCamPos, sepUp));
            fd.Cam.fieldOfView = 40f;
            PlaceTags();
            return;
        }
        if (Now == Part.Docking)
        {
            // through the crewman optical alignment sight, along Columbia's nose
            cam.SetPositionAndRotation(ProbeTip() + stack.up * 0.4f + stack.right * coasOffset.x + stack.forward * coasOffset.z, Quaternion.LookRotation(stack.up, -stack.forward));
            fd.Cam.fieldOfView = 34f;
        }
    }

    // ---------------- Earth orbit: realign the guidance platform on two stars (Apollo 12) ----------------

    static readonly (string name, float ra, float dec, string where)[] NavStars =
    {
        ("SIRIUS", 6.752f, -16.72f, "the brightest star in the sky, in Canis Major"), ("CANOPUS", 6.399f, -52.70f, "second brightest, far south in Carina"),
        ("ARCTURUS", 14.261f, 19.18f, "orange, in Bootes"), ("VEGA", 18.616f, 38.78f, "blue-white, in Lyra"), ("CAPELLA", 5.278f, 46.00f, "yellow, in Auriga"),
        ("RIGEL", 5.242f, -8.20f, "Orion's blue-white foot"), ("PROCYON", 7.655f, 5.22f, "in Canis Minor"), ("ACHERNAR", 1.629f, -57.24f, "end of the river Eridanus"),
        ("ALTAIR", 19.846f, 8.87f, "in Aquila"), ("ALDEBARAN", 4.599f, 16.51f, "the orange eye of Taurus"), ("ANTARES", 16.490f, -26.43f, "the red heart of Scorpius"),
        ("FOMALHAUT", 22.961f, -29.62f, "alone in the southern autumn sky"), ("DENEB", 20.690f, 45.28f, "the tail of Cygnus"), ("REGULUS", 10.140f, 11.97f, "the heart of Leo"),
    };
    int[] alignPick = { -1, -1 }; int alignStar = -1, alignDone; float aimYaw, aimPitch, alignStarT; bool aligned, alignHint;

    Vector3 AlignDir() { var s = NavStars[alignPick[alignStar]]; return fd.StarLocal(s.ra, s.dec); }

    void BeginAlign()
    {
        // the two brightest navigation stars well clear of the Earth and the Sun right now
        Vector3 earth = fd.EarthLocalKm.normalized; float earthR = Mathf.Asin(Mathf.Clamp01(6371f / Mathf.Max(fd.EarthLocalKm.magnitude, 6400f))) * Mathf.Rad2Deg;
        int n = 0;
        for (int i = 0; i < NavStars.Length && n < 2; i++)
        {
            var d = fd.StarLocal(NavStars[i].ra, NavStars[i].dec);
            if (Vector3.Angle(d, earth) > earthR + 18f && Vector3.Angle(d, fd.SunLocal) > 45f) alignPick[n++] = i;
        }
        if (n < 2) { alignPick[0] = 0; alignPick[1] = 2; }
        alignStar = -1; alignDone = 0; alignStarT = 0f;
        fd.AddEvent("PLATFORM LOST");
        Problems.I.Asked++;
        Problems.I.Info("GUIDANCE PLATFORM LOST  ·  APOLLO 12",
            "The computer knows which way the spacecraft points from a platform of spinning gyroscopes. The lightning knocked it over: the computer is lost.\n\n"
          + "The fix: stars are so far away that each one is a fixed direction in space. Aim the telescope at two known stars and the computer works out exactly how it is turned. Al Bean did this in Earth orbit (Program 52).\n\n"
          + "In the telescope the star to find is marked. Move the cross onto it and press SPACE.",
            () =>
            {
                alignStar = 0; alignStarT = 0f;
                Vector3 first = AlignDir();
                // start a little way off the first star, so the student has to aim
                Vector3 start = Quaternion.AngleAxis(14f, Vector3.Cross(first, Vector3.up).normalized) * first;
                aimYaw = Mathf.Atan2(start.x, start.z) * Mathf.Rad2Deg; aimPitch = -Mathf.Asin(Mathf.Clamp(start.y, -1f, 1f)) * Mathf.Rad2Deg;
                fd.CameraOwnedByVoyage = true;
                fd.Cam.cullingMask = ~(1 << 8);
                scope.gameObject.SetActive(true);
            });
    }

    void AlignUpdate(float dt)
    {
        if (alignStar < 0) return;
        alignStarT += dt;
        var star = NavStars[alignPick[alignStar]];
        Vector3 d = AlignDir();
        var camT = fd.Cam.transform;
        float off = Vector3.Angle(camT.forward, d);
        // the star in brackets with its name when it is in the telescope, a chevron on the rim pointing to it when not
        float scale = scope.GetComponentInParent<Canvas>().scaleFactor;
        Vector3 s = fd.Cam.WorldToScreenPoint(camT.position + d * 100f);
        Vector2 centre = new Vector2(Screen.width, Screen.height) * 0.5f;
        bool inView = s.z > 0f && Vector2.Distance(s, centre) < 270f * scale;
        starMark.gameObject.SetActive(inView);
        if (inView) { starMark.anchoredPosition = (Vector2)s / scale; starName.text = star.name; }
        Vector3 local = camT.InverseTransformDirection(d);
        Vector2 dir2 = new Vector2(local.x, local.y);
        scopeArrow.gameObject.SetActive(!inView && dir2.sqrMagnitude > 1e-6f);
        if (dir2.sqrMagnitude > 1e-6f)
        {
            dir2.Normalize();
            scopeArrow.anchoredPosition = dir2 * 300f;
            scopeArrow.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir2.y, dir2.x) * Mathf.Rad2Deg + 90f);
        }
        scopeText.text = $"STAR {alignDone + 1} OF 2:  <b>{star.name}</b>  ·  {star.where}\n<color=#8f98a1>MOVE THE CROSS ONTO THE STAR IN THE BRACKETS  ·  OFF {off:0.0}°  ·  SPACE: MARK</color>";
        bool mark = Input.GetKeyDown(KeyCode.Space) || (Auto && off < 0.6f);
        if (!mark) return;
        if (off < 1.5f)
        {
            markErr[alignDone] = off;
            alignDone++; alignStarT = 0f;
            fd.AddEvent($"MARK {star.name}");
            if (alignDone < 2) { alignStar = 1; return; }
            // both marked: the computer compares the angle between them with its star catalog (Noun 05)
            alignStar = -1; aligned = true; Problems.I.Solved++;
            scope.gameObject.SetActive(false);
            fd.CameraOwnedByVoyage = false; fd.Cam.cullingMask = ~0;
            fd.AddEvent("PLATFORM ALIGNED");
            var a = NavStars[alignPick[0]]; var b = NavStars[alignPick[1]];
            float between = Vector3.Angle(fd.StarLocal(a.ra, a.dec), fd.StarLocal(b.ra, b.dec));
            float diff = Mathf.Abs(markErr[0] - markErr[1]) * 0.02f + 0.01f;
            Problems.I.Info("PLATFORM ALIGNED",
                $"You marked {a.name} and {b.name}. The catalog says they are {between:0.00}° apart; your two marks agree with that to {diff:0.00}°.\n\n"
              + "That check is Noun 05, the \"star angle difference\": below 0.01-0.02° the crew accepted it. Now the computer knows exactly which way it points again.\n\n"
              + "Next: unstrap and float around Columbia (press U).",
                () => { Now = Part.Orbit; PartTime = 0f; });
        }
        else { Problems.I.Wrong++; fd.Explain($"Not on {star.name} yet: you are {off:0.0}° away. Put the cross inside the brackets.", 4f); }
    }
    float[] markErr = new float[2];

    // ---------------- transposition and docking: Columbia moves, the third stage and Eagle stay ----------------

    Quaternion sepRot0, dockRot; Vector3 sepCg0, sepNose0, sepAxis0, sepCamPos, sepUp, probeLocal, stackPos0;
    int dockTries, latchTries; bool latchKnown, latchAsked; Vector3 coasOffset;

    void BeginSeparation()
    {
        sepRot0 = stack.rotation; sepNose0 = stack.up; sepAxis0 = stack.right;
        sepCg0 = stack.TransformPoint(cgLocal);
        probeLocal = stack.InverseTransformPoint(ProbeTip());
        booster.SetParent(null, true);                                                 // the third stage and Eagle now stay where they are
        sepUp = Vector3.Cross(sepNose0, sepAxis0).normalized;
        sepCamPos = sepCg0 + sepAxis0 * 46f - sepNose0 * 4f + sepUp * 10f;             // beside the stack: the turn is seen side-on
        dockRot = Quaternion.AngleAxis(180f, sepAxis0) * sepRot0;
    }

    void SeparationUpdate(float t)
    {
        // the panels open (0-1.6 s), Columbia backs out 30 m (1.8-4.2 s), turns 180 deg about its centre of mass (4.0-6.4 s)
        // and comes back to about 10 m (6.3-7.5 s)
        float open = Mathf.SmoothStep(0f, 1f, t / 1.6f);
        for (int k = 0; k < 4; k++)
            if (slaPanels[k])
            {
                slaPanels[k].localRotation = BaseRot(k) * Quaternion.Euler(45f * open, 0f, 0f);
                if (t > 2.4f) slaPanels[k].position += slaPanels[k].forward * 1.2f * Time.deltaTime;   // spring-thrown clear, as in 1969
            }
        float d = 30f * Mathf.SmoothStep(0f, 1f, (t - 1.8f) / 2.4f) - 12f * Mathf.SmoothStep(0f, 1f, (t - 6.3f) / 1.2f);
        float turn = 180f * Mathf.SmoothStep(0f, 1f, (t - 4.0f) / 2.4f);
        stack.rotation = Quaternion.AngleAxis(turn, sepAxis0) * sepRot0;
        stack.position = sepCg0 + sepNose0 * d - stack.rotation * cgLocal;
    }

    float Range0() => Vector3.Dot(LmTop() - ProbeTip(), stack.up);

    // Columbia's pose from the relative position flown by the student (Eagle stays put)
    void PlaceForDocking()
    {
        stack.rotation = dockRot;
        Vector3 probeW = LmTop() - stack.right * dockRel.x - stack.forward * dockRel.y - stack.up * Mathf.Max(dockRel.z, 0f);
        stack.position = probeW - stack.rotation * probeLocal;
    }

    const float SafeMin = 0.05f, SafeMax = 0.40f;              // m/s at contact (the probe's design limit was about 1 ft/s)

    void DockingUpdate(float dt)
    {
        // one job: fly in, and brake before you touch. W pushes toward Eagle, S pushes back. Columbia keeps
        // itself lined up on the target (the crew flew with the autopilot holding attitude, too).
        float push = 0f;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) push += 1f;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) push -= 1f;
        bool pilot = Auto || Input.GetKey(KeyCode.P);
        if (pilot)
        {
            if (!usedAutopilot) { usedAutopilot = true; Problems.I.DockAuto = 1; }
            float want = dockRel.z > 2.5f ? 0.6f : (latchKnown ? 0.28f : 0.18f);
            push = Mathf.Clamp((want - (-dockVel.z)) * 4f, -1f, 1f);
        }
        if (latchDemo > 0) { pilot = true; push = Mathf.Clamp((latchDemoSpeed - (-dockVel.z)) * 4f, -1f, 1f); }   // a wrong choice, flown for you
        dockVel.z -= push * 0.35f * dt;                                         // +push = closing faster
        dockVel.z = Mathf.Clamp(dockVel.z, -1.2f, 0.6f);
        // lined up for you
        dockRel.x = Mathf.MoveTowards(dockRel.x, 0f, 0.2f * dt); dockRel.y = Mathf.MoveTowards(dockRel.y, 0f, 0.2f * dt);
        dockRel.z += dockVel.z * dt;
        if (dockRel.z > 14f) { dockRel.z = 14f; dockVel.z = Mathf.Min(dockVel.z, 0f); }
        if (dockRel.z <= 0f) Contact(pilot || push > 0f);
        PlaceForDocking();
        DockingUi();
    }

    void Contact(bool pushing)
    {
        float closing = -dockVel.z;
        dockTries++;
        bool speedOk = closing >= SafeMin && closing <= SafeMax;
        if (latchDemo > 0) { LatchDemoEnd(closing); return; }
        if (speedOk && Problems.I.LatchFailure && !(latchKnown && closing >= 0.15f))
        {
            // Apollo 14's capture latches: the first touch never catches. Then you decide what to do about it
            Bounce();
            latchTries++;
            if (!latchAsked) { latchAsked = true; AskLatch(closing); return; }
            fd.Explain($"Still no capture: {closing:0.00} m/s is too gentle for the stuck latches. Come in firmer, in the light band: 0.15 to 0.40 m/s.", 8f);
            return;
        }
        if (speedOk)
        {
            Problems.I.DockSpeed = closing; Problems.I.DockOffset = 0f; Problems.I.DockTries = dockTries;
            dockRel = Vector3.zero; dockVel = Vector3.zero;
            clunk.clip = Latches(); clunk.Play();
            PlaceForDocking();
            Go(Part.Docked);
            if (Problems.I.LatchFailure)
                Problems.I.Info("CAPTURE  ·  WHY IT WORKED",
                    "A firm push and thrust held on after contact pressed the probe home, and the latches caught. "
                  + "That is how Stu Roosa docked Apollo 14 on the sixth try, on 1 February 1971. What jammed the latches was never found for sure: maybe a little dirt.",
                    () => { });
            return;
        }
        Bounce();
        fd.Explain(closing > SafeMax ? $"Too fast: {closing:0.00} m/s. The probe bounced off. Hold S to brake before you touch (below {SafeMax:0.00} m/s)."
                                     : "Too gentle: the probe stopped before the latches. Give it a little W.", 6f);
    }

    // after a bad touch Columbia drifts back a few metres to try again
    void Bounce() { dockRel.z = 0.05f; dockVel.z = 0.25f; }

    // ---------------- Apollo 14's stuck latches: the choice, and the wrong answers played out ----------------
    int latchDemo; float latchDemoSpeed; float lastClosing;

    void AskLatch(float closing)
    {
        lastClosing = closing;
        Problems.I.Ask("NO CAPTURE  ·  APOLLO 14",
            "The probe touched Eagle gently, but nothing caught: Columbia bounced back. If you can't dock, Eagle stays stuck on the rocket and nobody lands on the Moon. What do you do?",
            new[] { $"You touched at {closing:0.00} m/s: inside the safe range",
                    "The probe and Eagle look undamaged",
                    "Three small latches on the probe's tip should snap shut on contact. They didn't" },
            new (string, bool, string)[]
            {
                ("TRY AGAIN, MORE GENTLY", false, ""),
                ("COME IN FIRMER, AND KEEP THRUSTING AFTER YOU TOUCH", true, "Stuck latches need more push, not less. Come in at about 0.2 to 0.3 m/s and keep holding W after you touch, so the probe stays pressed in until they snap."),
                ("HIT IT HARD TO FORCE THE LATCHES", false, ""),
                ("GIVE UP: FLY ON WITHOUT EAGLE", false, ""),
            }, 25f,
            () => { latchKnown = true; fd.Explain("Firm, not fast: about 0.2 to 0.3 m/s, and keep holding W as you touch.", 12f); },
            i =>
            {
                if (i == 3)
                {
                    Problems.I.Fail("NO LANDING",
                        "Columbia flies to the Moon alone. Eagle stays fixed to the third stage, which drifts off into space. Nobody walks on the Moon.",
                        "Nothing was broken and there was time and fuel to try again. Apollo 14's crew tried six times, and on the sixth it worked.",
                        RewindLatch);
                    return;
                }
                // show what happens: Columbia flies the chosen approach by itself
                latchDemo = i == 0 ? 1 : 2;
                latchDemoSpeed = i == 0 ? 0.07f : 0.8f;
                dockRel.z = i == 0 ? 0.35f : 3f; dockVel.z = 0f;
                fd.Explain(i == 0 ? "WHAT HAPPENS: Columbia comes in even more gently, at 0.07 m/s..." : "WHAT HAPPENS: Columbia rams Eagle at 0.8 m/s...", 8f);
            },
            () => { latchKnown = true; fd.Explain("CAPCOM: \"Try it firmer, and keep thrusting forward after contact.\" Houston decided for you.", 12f); });
    }

    void LatchDemoEnd(float closing)
    {
        int kind = latchDemo; latchDemo = 0;
        Bounce();
        if (kind == 1)
            Problems.I.Fail("NO CAPTURE AGAIN",
                $"You touched even more gently, at {closing:0.00} m/s. The probe slid back out: the latches didn't catch again. Every try burns thruster fuel Columbia needs later.",
                $"Speed was never the problem: the first touch, at {lastClosing:0.00} m/s, was already gentle. Stuck latches need more push: come in firmer and keep thrusting after you touch.",
                RewindLatch);
        else
        {
            dockVel.z = 0.6f;                                                          // knocked back hard
            Problems.I.Fail("PROBE BROKEN",
                $"You hit Eagle at {closing:0.00} m/s, twice the safe limit. The probe's tip bent. With a broken probe Columbia can't dock, Eagle stays on the rocket, and nobody lands on the Moon.",
                $"Firmer doesn't mean fast. The probe is built for a gentle touch, under {SafeMax:0.0} m/s. Come in at about 0.2 to 0.3 m/s and keep pushing after contact: that presses the latches shut without breaking anything.",
                RewindLatch);
        }
    }

    // back to just after the first touch: the choice comes again
    void RewindLatch()
    {
        latchDemo = 0;
        dockRel = new Vector3(0f, 0f, 2f); dockVel = Vector3.zero;
        PlaceForDocking();
        fd.Explain("REWIND: back to just after the first touch.", 4f);
        AskLatch(lastClosing);
    }

    // after docking: Columbia and Eagle back away together, the third stage drifts off the other way
    void ExtractionUpdate(float t)
    {
        if (!dockedPosSet) { dockedPos = stack.position; dockedPosSet = true; }
        float k = Mathf.SmoothStep(0f, 1f, (t - 1.5f) / 3.5f);
        stack.position = dockedPos - stack.up * 9f * k;                              // Columbia pulls Eagle out of the adapter
        if (booster && t > 1.5f) booster.position += stack.up * 0.8f * Time.deltaTime;   // the third stage drifts the other way
        if (exhaust) exhaust.VoyageBurn = -1f;
    }
    Vector3 dockedPos; bool dockedPosSet, usedAutopilot;

    Quaternion[] baseRot;
    Quaternion BaseRot(int k)
    {
        if (baseRot == null) { baseRot = new Quaternion[4]; for (int i = 0; i < 4; i++) baseRot[i] = slaPanels[i] ? slaPanels[i].localRotation : Quaternion.identity; }
        return baseRot[k];
    }

    // the names over the three craft in the side view
    void PlaceTags()
    {
        float scale = tags.GetComponentInParent<Canvas>().scaleFactor;
        void Put(RectTransform t, Vector3 w, bool on)
        {
            Vector3 s = fd.Cam.WorldToScreenPoint(w);
            t.gameObject.SetActive(on && s.z > 0f);
            t.anchoredPosition = (Vector2)s / scale + new Vector2(0f, 26f);
        }
        Put(tagCol, stack.TransformPoint(cgLocal), true);
        Put(tagEagle, lm ? lm.TransformPoint(Vector3.up * 2.5f) : Vector3.zero, lm != null);
        var sivb = booster ? booster.Find("S-IVB") : null;
        Put(tagStage, sivb ? StageCentre(sivb) : Vector3.zero, sivb != null && booster.gameObject.activeInHierarchy);
    }

    static Vector3 StageCentre(Transform t)
    {
        var rs = t.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) return t.position;
        var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b.center;
    }

    // ---------------- the explainer: what transposition and docking is, in five pictures ----------------

    void ExplainerUpdate(float t)
    {
        // side view, nose to the right: [third stage][adapter with Eagle][Columbia>
        float p1 = Mathf.SmoothStep(0f, 1f, (t - 1.2f) / 1.0f);      // separate
        float p2 = Mathf.SmoothStep(0f, 1f, (t - 2.6f) / 1.2f);      // turn around
        float p3 = Mathf.SmoothStep(0f, 1f, (t - 4.2f) / 1.2f);      // dock
        float p4 = Mathf.SmoothStep(0f, 1f, (t - 5.8f) / 1.4f);      // pull Eagle out
        float csmX = 70f + 150f * p1 - 190f * p3 + 120f * p4;               // docked: Columbia's nose on Eagle's top
        exCsm.anchoredPosition = new Vector2(csmX, 10f);
        exCsm.localRotation = Quaternion.Euler(0f, 0f, 180f * p2);
        exCsmLabel.rectTransform.anchoredPosition = new Vector2(csmX, 34f);
        exEagle.anchoredPosition = new Vector2(-34f + 120f * p4, 10f);
        exAdapter.GetComponent<Image>().color = new Color(0.86f, 0.85f, 0.82f, Mathf.Lerp(0.9f, 0.18f, p1));
        string[] lines =
        {
            "1 · For launch, Eagle rides packed inside the adapter, under Columbia",
            "2 · Columbia separates and backs away; the adapter's panels open",
            "3 · Columbia turns around: now its nose points at Eagle",
            "4 · Nose to nose: Columbia docks with Eagle. YOU fly this part",
            "5 · Together they pull Eagle out. The third stage is left behind",
        };
        int i = t < 1.2f ? 0 : t < 2.6f ? 1 : t < 4.2f ? 2 : t < 5.8f ? 3 : 4;
        exText.text = lines[i] + (t > 1f ? "\n<color=#8f98a1>SPACE: SKIP</color>" : "");
    }

    // ---------------- the coast ----------------

    const double MccGet = 96298.0;                                              // 26:44:58, Apollo 11's only course correction
    static readonly string[] BeatTitles = { "DAY 1", "COURSE CORRECTION", "SLOWEST", "THE MOON'S PULL", "IN THE MOON'S SHADOW" };
    static readonly float[] CoastSeconds = { 3.0f, 2.0f, 3.5f, 1.6f, 4.0f, 2.2f };   // to each stop, then to LOS

    double Stop(int k) => k == 0 ? Day1Get : k == 1 ? MccGet : k == 2 ? SlowGet : k == 3 ? SoiGet : k == 4 ? shadowGet : LosGet;

    void CoastUpdate()
    {
        if (mccOn) { MccUpdate(Time.deltaTime); return; }
        // each stop waits for the student (SPACE), or goes on by itself after a while
        if (beatWait >= 0f)
        {
            rate = 0;
            beatWait += Time.deltaTime;
            if (Input.GetKeyDown(KeyCode.Space) || (Auto && beatWait > 2.5f) || beatWait > 12f) beatWait = -1f;
            return;
        }
        double target = Stop(beat), from = beat == 0 ? ExtractGet : Stop(beat - 1);
        float secs = CoastSeconds[Mathf.Min(beat, CoastSeconds.Length - 1)];
        rate = Math.Max(1, (target - from) / secs);
        if (Get + Time.deltaTime * rate >= target)
        {
            rate = (target - Get) / Math.Max(Time.deltaTime, 1e-3);
            if (beat < 5) { Beat(beat); }
            else { Go(Part.Pro); }
            beat++;
        }
    }

    void Beat(int k)
    {
        double eKm = earthKm, sp = speedKms * 3600;
        LastBeat = BeatTitles[k];
        fd.AddEvent(BeatTitles[k]);
        if (k == 1)
        {
            // the course correction, flown by hand
            beatWait = -1f;
            StartMcc(true);
            return;
        }
        string line = k switch
        {
            0 => $"Day 1 · The whole Earth fits in one window now, {Math.Round(eKm / 1000) * 1000:#,0} km behind us, and it keeps shrinking.   SPACE",
            2 => $"Day 3 · Earth's gravity has pulled back the whole way: from 39,000 km/h we are down to {Math.Round(sp / 100) * 100:#,0} km/h.   SPACE",
            3 => $"61:40 · {Math.Round(moonKm / 100) * 100:#,0} km from the Moon its pull wins: from here we speed up again. Hold up your thumb: it hides the Earth.   SPACE",
            _ => "74:50 · The Moon hides the Sun. Only the Sun's glowing corona shows, and the Earth faintly lights the Moon's dark side. Apollo 11's crew saw this.   SPACE",
        };
        fd.Explain(line, 12f);
        beatWait = 0f;
    }

    // ---------------- the course correction, flown by hand ----------------
    // Tracking says where we would pass the Moon (different every run); every second of engine burn lowers that pass
    // by about 35 km (2 m/s a second, about 17 km per m/s on this path). Hold SPACE to burn, ENTER when done.
    bool mccOn; float mccPass, mccPass0, mccClock, mccBurnT; bool mccFire;
    const float MccWindow = 25f, KmPerSecond = 35f, MccLow = 90f, MccHigh = 135f;
    RectTransform mccPanel, mccPath, mccStart, mccShip, mccMeasure, mccBand; TextMeshProUGUI mccText, mccState, mccPlan, mccPathLabel, mccMeasureLabel;

    void StartMcc(bool first)
    {
        // on a hybrid route this burn also leaves the free-return path, so it is bigger
        if (first) mccPass0 = Mathf.Round((MissionState.Hybrid ? 250f : 170f) + (float)new System.Random(Environment.TickCount).NextDouble() * (MissionState.Hybrid ? 170f : 150f));
        mccPass = mccPass0; mccClock = MccWindow; mccBurnT = 0f;
        if (!first) { McPanel(true); mccOn = true; fd.Explain("REWIND: the pass is back to " + mccPass0.ToString("0") + " km. Work out the seconds first.", 6f); return; }
        float need = mccPass0 - 111f;
        Problems.I.Info("COURSE CORRECTION  ·  DAY 2",
            "Nobody can aim perfectly at a Moon 380,000 km away, so the path is checked on the way. "
          + $"Radar on Earth says: right now we would fly past the Moon <b>{mccPass0:0} km</b> above its surface.\n\n"
          + "That is too high. Behind the Moon we will fire the engine to brake into orbit, and that only works if we skim past about <b>111 km</b> up. "
          + "Too high and we end up in the wrong orbit. Too low and we hit the mountains.\n\n"
          + (MissionState.Hybrid ? "On your HYBRID route this burn does a second job: it takes you off the free-return path, toward your site. After it, if the engine failed, the Moon would no longer swing you home.\n\n" : "")
          + "So now we fire the engine for a few seconds to bend our path down. Out here a tiny push makes a big difference: "
          + "<b>every second of engine moves the pass about 35 km lower.</b>\n\n"
          + $"We need {need:0} km lower:  {need:0} ÷ 35 = about <b>{need / KmPerSecond:0.0} seconds</b>.\n"
          + $"HOLD SPACE to fire, let go to stop, then press ENTER. You have {MccWindow:0} seconds.",
            () => { McPanel(true); mccOn = true; });
    }

    void MccUpdate(float dt)
    {
        rate = 0;
        mccClock -= dt;
        Problems.I.Pressure("BURN WINDOW CLOSES IN", mccClock, MccWindow);
        mccFire = Input.GetKey(KeyCode.Space) || (Auto && mccPass > 113f);
        if (mccFire) { mccPass -= KmPerSecond * dt; mccBurnT += dt; }
        // the picture: our path over the Moon, coming down as the engine fires
        McPicture();
        mccText.text = $"WE PASS THE MOON  {Mathf.Max(mccPass, 0f):0} KM UP      ENGINE  {mccBurnT:0.0} S";
        mccState.text = mccFire ? "ENGINE ON  ·  THE PATH IS BENDING DOWN  ·  LET GO OF SPACE TO STOP"
                      : mccBurnT <= 0f ? "HOLD SPACE TO FIRE THE ENGINE"
                      : mccPass >= MccLow && mccPass <= MccHigh ? "ENGINE OFF  ·  ON TARGET  ·  PRESS ENTER"
                      : "ENGINE OFF  ·  ENTER WHEN THE PATH IS IN THE AIM BAND";
        if (mccPass <= 0f)
        {
            McLose("IMPACT", $"You burned {mccBurnT:0.0} s. The path came down {mccPass0:0} km and now hits the Moon.",
                $"Do the sum before you fire: {mccPass0 - 111:0} km ÷ 35 km a second = about {(mccPass0 - 111) / 35f:0.0} seconds. Let go of SPACE when the number reaches about 111.");
            return;
        }
        bool done = (!mccFire && mccBurnT > 0f && Input.GetKeyDown(KeyCode.Return)) || mccClock <= 0f || (Auto && mccPass <= 113f);
        if (!done) return;
        mccOn = false; McPanel(false); Problems.I.PressureOff();
        if (mccPass < MccLow)
            McLose("TOO LOW", $"You burned {mccBurnT:0.0} s: the pass is only {mccPass:0} km up. Too close to the Moon's mountains for the orbit burn behind it.",
                $"{mccPass0 - 111:0} km ÷ 35 km a second = about {(mccPass0 - 111) / 35f:0.0} seconds. A small burn is enough this far out.");
        else if (mccPass > MccHigh)
            McLose(mccBurnT <= 0f ? "NO BURN" : "TOO HIGH", $"The pass is still {mccPass:0} km up. The burn behind the Moon would then put us in the wrong orbit and use fuel the landing needs.",
                $"Hold SPACE for about {(mccPass0 - 111) / 35f:0.0} seconds: {mccPass0 - 111:0} km ÷ 35 km a second.");
        else
            Problems.I.Info("ON TARGET  ·  WHY IT WORKED",
                $"You fired the engine for {mccBurnT:0.0} seconds, and now we pass the Moon {mccPass:0} km up: right where the brake burn needs us.\n\n"
              + "It's like throwing a ball at a far-away target: a tiny change in your hand moves where the ball lands a lot. "
              + "That is why the path is fixed early, while a small push is enough. Later, the same fix would cost far more fuel.\n\n"
              + "Apollo 11 made just one correction like this, 26 hours 45 minutes into the flight: 3 seconds of engine.",
                () => { beatWait = 0f; });
    }

    void McLose(string title, string what, string why)
    {
        mccOn = false; McPanel(false); Problems.I.PressureOff();
        Problems.I.Fail(title, what, why, () => StartMcc(false));
    }

    void McPanel(bool on)
    {
        if (mccPanel == null)
        {
            // below the burn clock (which sits at the top of the screen); the Earth-Moon bar is hidden meanwhile
            mccPanel = UiKit.Rect(hud, "Correction", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -92), new Vector2(760, 262));
            UiKit.Box(mccPanel, new Color(0.02f, 0.022f, 0.026f, 0.92f), 12);
            UiKit.OutlineBox(UiKit.Full(mccPanel, "Edge"), new Color(1f, 1f, 1f, 0.16f));
            UiKit.Label(mccPanel, "COURSE CORRECTION  ·  HOLD SPACE: ENGINE ON  ·  ENTER: DONE", 11, UiKit.Text2, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -12), new Vector2(740, 16), TextAlignmentOptions.Center, true, 3);

            // the picture, side-on: the Moon's curve at the bottom, our path a line above it (heights not to scale)
            var pic = UiKit.Rect(mccPanel, "Picture", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -36), new Vector2(PicW, PicH));
            pic.gameObject.AddComponent<RectMask2D>();
            UiKit.Box(UiKit.Rect(pic, "Moon", new Vector2(0, 0), new Vector2(0.5f, 0.5f), new Vector2(MoonX, PicMoonTop - 220f), new Vector2(440, 440)), new Color(0.58f, 0.58f, 0.57f, 1f), 220);
            UiKit.Label(pic, "MOON", 11, UiKit.Ink, new Vector2(0, 0), new Vector2(0.5f, 0), new Vector2(MoonX, 3), new Vector2(120, 16), TextAlignmentOptions.Center, true, 4);
            mccBand = UiKit.Rect(pic, "Aim", new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, PicY(MccLow)), new Vector2(PicW, PicY(MccHigh) - PicY(MccLow)));
            UiKit.Box(mccBand, new Color(1f, 1f, 1f, 0.12f), 0);
            UiKit.Box(UiKit.Rect(mccBand, "Top", new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(PicW, 1)), new Color(1f, 1f, 1f, 0.35f), 0);
            UiKit.Box(UiKit.Rect(mccBand, "Bottom", new Vector2(0.5f, 0), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(PicW, 1)), new Color(1f, 1f, 1f, 0.35f), 0);
            UiKit.Label(mccBand, $"AIM HERE: {MccLow:0}-{MccHigh:0} KM", 10, UiKit.Text2, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-10, 0), new Vector2(200, 14), TextAlignmentOptions.MidlineRight, true, 2);
            mccStart = UiKit.Rect(pic, "Start", new Vector2(0, 0), new Vector2(0, 0.5f), Vector2.zero, new Vector2(PathLen, 1));
            UiKit.Box(mccStart, new Color(1f, 1f, 1f, 0.22f), 0);
            mccPath = UiKit.Rect(pic, "Path", new Vector2(0, 0), new Vector2(0, 0.5f), Vector2.zero, new Vector2(PathLen, 2));
            UiKit.Box(mccPath, Color.white, 0);
            mccPathLabel = UiKit.Label(mccPath, "OUR PATH", 10, UiKit.Text, new Vector2(0, 0.5f), new Vector2(0, 0), new Vector2(0, 5), new Vector2(300, 14), TextAlignmentOptions.MidlineLeft, true, 2);
            mccShip = UiKit.Rect(pic, "Ship", new Vector2(0, 0), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(8, 8));
            UiKit.Box(mccShip, Color.white, 4);
            mccMeasure = UiKit.Rect(pic, "Measure", new Vector2(0, 0), new Vector2(0.5f, 0), new Vector2(MoonX, PicMoonTop), new Vector2(1, 10));
            UiKit.Box(mccMeasure, new Color(1f, 1f, 1f, 0.6f), 0);
            mccMeasureLabel = UiKit.Label(pic, "", 11, UiKit.Text, new Vector2(0, 0), new Vector2(0, 0.5f), new Vector2(MoonX + 8, 0), new Vector2(120, 14), TextAlignmentOptions.MidlineLeft, true, 2);

            mccText = UiKit.Label(mccPanel, "", 18, UiKit.Text, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -36 - PicH - 10), new Vector2(740, 26), TextAlignmentOptions.Center, true, 2);
            mccPlan = UiKit.Label(mccPanel, "", 11, UiKit.Text2, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -36 - PicH - 40), new Vector2(740, 16), TextAlignmentOptions.Center, true, 2);
            mccState = UiKit.Label(mccPanel, "", 12, UiKit.Text, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 12), new Vector2(740, 18), TextAlignmentOptions.Center, true, 3);
        }
        mccPanel.gameObject.SetActive(on);
        if (lineRoot) lineRoot.gameObject.SetActive(!on && (Now == Part.Coast || Now == Part.Pro || Now == Part.Loi));
        if (on)
        {
            float need = mccPass0 - 111f;
            mccPlan.text = $"THE PLAN:  {need:0} KM LOWER  ÷  35 KM PER SECOND OF ENGINE  =  ABOUT {need / KmPerSecond:0.0} S";
            McPicture();
        }
    }

    // the picture's scale: 330 km of height fills it; the Moon's top is near the bottom edge
    const float PicW = 700f, PicH = 132f, PicMoonTop = 22f, PicKm = 0.32f, MoonX = 400f, PathLen = 500f;
    static float PicY(float km) => PicMoonTop + Mathf.Max(km, 0f) * PicKm;

    void McPicture()
    {
        float y = PicY(mccPass);
        mccPath.anchoredPosition = new Vector2(20f, y);
        mccStart.anchoredPosition = new Vector2(20f, PicY(mccPass0));
        mccStart.gameObject.SetActive(mccBurnT > 0f);
        mccShip.anchoredPosition = new Vector2(20f, y);
        mccMeasure.sizeDelta = new Vector2(1f, Mathf.Max(y - PicMoonTop, 0f));
        mccMeasureLabel.rectTransform.anchoredPosition = new Vector2(MoonX + 8f, PicMoonTop + (y - PicMoonTop) * 0.5f);
        mccMeasureLabel.text = $"{Mathf.Max(mccPass, 0f):0} KM";
        bool inBand = mccPass >= MccLow && mccPass <= MccHigh;
        mccPathLabel.text = inBand ? "OUR PATH  ·  ON TARGET" : mccPass > MccHigh ? "OUR PATH  ·  TOO HIGH" : "OUR PATH  ·  TOO LOW";
    }

    // ---------------- the end of step 6 ----------------
    bool endShown;
    void ShowEnd()
    {
        endShown = true;
        Problems.I.Info("IN ORBIT AROUND THE MOON  ·  NEXT: THE LANDING",
            $"Columbia and Eagle are in orbit around the Moon, 112 by 314 km. Neil Armstrong and Buzz Aldrin move into Eagle; Michael Collins stays in Columbia.\n\nNext: Eagle undocks and lands at {SiteName()}, the point you picked.",
            BeginLanding);
    }

    // step 7: Eagle undocks; Columbia and the trip's own displays step aside, the landing takes over
    void BeginLanding()
    {
        foreach (var r in stack.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        if (exhaust) exhaust.VoyageBurn = -1f;
        foreach (var rt in new[] { lineRoot, dsky, emsPanel, coas, tags, explainer, actionPill, scope, mccPanel })
            if (rt) rt.gameObject.SetActive(false);
        fd.Explain("", 0.01f);
        landing = gameObject.AddComponent<Landing>();
        landing.Auto = Auto;
        landing.Begin(fd, hud);
    }

    // ---------------- the orbit burn's backup cutoff ----------------
    // The computer stops the engine when the velocity still to gain reaches zero. The crew watched the same number on
    // the Entry Monitor System's ΔV counter, ready to shut the engine off themselves if the computer did not (X).
    bool loiFault, loiCut; double burnStart; float loiVg;
    RectTransform emsPanel; TextMeshProUGUI emsText, emsState;

    void EmsPanel(bool on)
    {
        if (emsPanel == null)
        {
            emsPanel = UiKit.Rect(hud, "EMS", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -96), new Vector2(560, 100));
            UiKit.Box(emsPanel, new Color(0.02f, 0.022f, 0.026f, 0.92f), 12);
            UiKit.OutlineBox(UiKit.Full(emsPanel, "Edge"), new Color(1f, 1f, 1f, 0.16f));
            UiKit.Label(emsPanel, "ENGINE BURN  ·  ΔV STILL TO GAIN  ·  X: ENGINE OFF", 11, UiKit.Text2, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -12), new Vector2(540, 16), TextAlignmentOptions.Center, true, 3);
            emsText = UiKit.Label(emsPanel, "", 26, UiKit.Text, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -2), new Vector2(540, 34), TextAlignmentOptions.Center, true, 3);
            emsState = UiKit.Label(emsPanel, "", 12, UiKit.Text, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 10), new Vector2(540, 18), TextAlignmentOptions.Center, true, 3);
        }
        emsPanel.gameObject.SetActive(on);
    }

    void LoiUpdate(float dt)
    {
        loiVg = (float)(2917.5 * (1.0 - (Get - burnStart) / 357.5));
        rate = loiCut ? 1 : loiVg > 90f ? 100 : 1;
        if (loiCut) { if (Get >= LoiCut + 1 || PartTime > 4f) Go(Part.Lunar); return; }
        emsText.text = $"{(loiVg >= 0 ? "+" : "-")}{Mathf.Abs(loiVg):0000} FT/S";
        bool overdue = loiVg < 0f;
        emsState.text = !overdue ? (loiVg < 90f ? "NEARLY THERE  ·  THE COMPUTER STOPS IT AT ZERO" : "BURNING") : "THE COMPUTER DID NOT STOP IT  ·  PRESS X NOW";
        emsState.alpha = overdue && Mathf.Repeat(Time.time, 0.5f) < 0.25f ? 0.35f : 1f;
        bool x = Input.GetKeyDown(KeyCode.X) || (Auto && overdue);
        if (x)
        {
            if (loiVg > 40f) { LoiLose("ORBIT TOO HIGH", $"You shut the engine off with {loiVg:0} ft/s still to go. Columbia is caught by the Moon, but in an orbit far too high and long for the landing.",
                "The computer stops the engine at zero. Your X is only the backup: use it if the number goes past zero and the engine keeps burning."); return; }
            Cut(true); return;
        }
        if (!overdue) return;
        if (!loiFault) { Cut(false); return; }
        if (loiVg < -60f) LoiLose("ORBIT TOO LOW", "The computer missed the cutoff and the engine kept burning. The orbit's low point now dips below the Moon's mountains.",
            "Watch the ΔV counter. When it passes zero and the engine is still on, press X at once: the crew had exactly this backup.");
    }

    void Cut(bool byHand)
    {
        loiCut = true; PartTime = 0f; EmsPanel(false);
        if (exhaust) exhaust.VoyageBurn = -1f;
        if (byHand)
            Problems.I.Info("ENGINE OFF  ·  WHY IT MATTERED",
                $"The computer missed the cutoff, and you stopped the engine {Mathf.Abs(loiVg):0} ft/s past zero.  "
              + "Every extra second of burn makes the orbit lower on the far side. The crew watched the same counter on the Entry Monitor System, ready to switch the engine off themselves: a second way to do every critical job.",
                () => { });
    }

    void LoiLose(string title, string what, string why)
    {
        loiCut = true; EmsPanel(false);
        if (exhaust) exhaust.VoyageBurn = -1f;
        Problems.I.Fail(title, what, why, () =>
        {
            Get = LoiIgn - 7; proPressed = false; loiCut = false;
            Now = Part.Pro; PartTime = 3f; dsky.gameObject.SetActive(true);
            fd.Explain("REWIND: 7 seconds before the burn. Press PRO when VERB 99 flashes.", 5f);
        });
    }

    // ---------------- attitude: which way Columbia points ----------------

    Quaternion StackFrom(Vector3 nose, Vector3 top) =>
        Quaternion.LookRotation(nose, top) * Quaternion.Inverse(Quaternion.LookRotation(Vector3.up, Vector3.left));

    void Attitude(float dt)
    {
        // local frame of this place: Unity world = east, up, north; the velocity, the Earth, the Moon, the Sun in it
        Vector3 vLocal = LocalVelocity();
        Vector3 earth = fd.EarthLocalKm.normalized, moon = fd.MoonLocalKm.normalized, sun = fd.SunLocal;
        if (exhaust) exhaust.VoyageBurnerAxis = stack.up;
        if (Now == Part.Separation || Now == Part.Docking || Now == Part.Docked || Now == Part.Align) return;   // flown elsewhere
        Quaternion q;
        switch (Now)
        {
            case Part.AfterTli: case Part.Explainer:
                {
                    // before the turn Columbia's nose points 25 deg off the Sun, so after it the crew looks at Eagle with
                    // the Sun behind them: Eagle's top and its docking target are in full sunlight
                    Vector3 side = Vector3.Cross(sun, Vector3.up); if (side.sqrMagnitude < 1e-4f) side = Vector3.right;
                    Vector3 nose = Quaternion.AngleAxis(25f, side.normalized) * sun;
                    q = StackFrom(nose, Vector3.Cross(nose, side).normalized);
                    break;
                }
            case Part.Orbit: case Part.Float: case Part.ToTli: case Part.Tli:
                {
                    Vector3 h = Vector3.ProjectOnPlane(vLocal, Vector3.up).normalized;
                    q = StackFrom(h.sqrMagnitude > 0.1f ? h : Vector3.forward, Vector3.up);   // nose along the flight path, level with the horizon
                    break;
                }
            case Part.Coast:
                {
                    // the "barbecue roll" (passive thermal control), shown slowed: the left window sweeps across the
                    // Earth early on, the Moon later; at each stop it settles with its target in the window
                    Vector3 target = Get < SoiGet + 3600 ? earth : moon;                // the Earth (and the thumb) until the Moon takes over
                    Vector3 axis = Vector3.Cross(target, sun); if (axis.sqrMagnitude < 1e-4f) axis = Vector3.Cross(target, Vector3.up);
                    axis.Normalize();
                    Quaternion r = WindowOn(target, axis);
                    roll = beatWait >= 0f ? Mathf.MoveTowardsAngle(roll, 0f, dt * 60f) : roll + dt * 45f;
                    q = Quaternion.AngleAxis(roll, r * Vector3.up) * r;
                    break;
                }
            default:
                {
                    // around the Moon: the left window looks ahead and down at the surface, the horizon at its top
                    Vector3 down = moon;
                    Vector3 fwd = Vector3.ProjectOnPlane(vLocal, down).normalized;
                    q = WindowOn((fwd * 0.82f + down * 0.57f).normalized, fwd);
                    break;
                }
        }
        stack.rotation = Quaternion.Slerp(stack.rotation, q, 1f - Mathf.Exp(-dt * (Now == Part.Coast ? 3f : 1.5f)));

    }
    float roll;

    // Columbia turned so the commander's left window looks straight at a direction, its long axis as near a hint as it can
    Quaternion WindowOn(Vector3 target, Vector3 axisHint)
    {
        Vector3 w = windowLocal.normalized;
        Vector3 upL = Vector3.ProjectOnPlane(Vector3.up, w).normalized;
        Vector3 ax = Vector3.ProjectOnPlane(axisHint, target);
        if (ax.sqrMagnitude < 1e-6f) ax = Vector3.ProjectOnPlane(Vector3.up, target);
        return Quaternion.LookRotation(target, ax.normalized) * Quaternion.Inverse(Quaternion.LookRotation(w, upL));
    }

    Vector3 LocalVelocity()
    {
        var a = Where(Get - 1, out _, out _); var b = Where(Get + 1, out _, out _);
        var vel = V(b.x - a.x, b.y - a.y, b.z - a.z);
        // ICRF -> local, as in FlightDirector.SetPlaceFromSpace
        double gg = Ephemeris.GmstDeg(Utc(Get)) * Math.PI / 180.0, r = sc.Magnitude;
        double ex = Math.Cos(gg) * sc.x + Math.Sin(gg) * sc.y, ey = -Math.Sin(gg) * sc.x + Math.Cos(gg) * sc.y;
        double la = Math.Asin(sc.z / r), lo = Math.Atan2(ey, ex);
        var E = new Vector3((float)-Math.Sin(lo), (float)Math.Cos(lo), 0f);
        var N = new Vector3((float)(-Math.Sin(la) * Math.Cos(lo)), (float)(-Math.Sin(la) * Math.Sin(lo)), (float)Math.Cos(la));
        var U = new Vector3((float)(Math.Cos(la) * Math.Cos(lo)), (float)(Math.Cos(la) * Math.Sin(lo)), (float)Math.Sin(la));
        var ecef = new Vector3((float)(Math.Cos(gg) * vel.x + Math.Sin(gg) * vel.y), (float)(-Math.Sin(gg) * vel.x + Math.Cos(gg) * vel.y), (float)vel.z);
        return new Vector3(Vector3.Dot(ecef, E), Vector3.Dot(ecef, U), Vector3.Dot(ecef, N)).normalized;
    }

    // ---------------- the ride: g, engines ----------------

    void Ride(float dt)
    {
        g = 0f; RoarLow = 0f;
        float burn = -1f;
        if (Now == Part.Tli && Get >= TliIgn && Get < TliCut)
        {
            double t = Get - TliIgn, m = 135000 - (135000 - 65500) / 346.8 * t;
            g = (float)((t < 3.0 ? 1023e3 * t / 3.0 : t < 280 ? 1023e3 : 890e3) / m / 9.81);
            RoarLow = 0.11f; burn = 1f;
        }
        if (Now == Part.Coast && mccOn && mccFire) { g = 0.21f; RoarLow = 0.05f; burn = 1f; if (exhaust) exhaust.VoyageBurner = Saturn("SM"); }
        if (Now == Part.Loi && !loiCut && Get >= burnStart)
        {
            double t = Get - burnStart, m = 43600 - (43600 - 33000) / 357.5 * Math.Min(t, 357.5);
            g = (float)(91.2e3 / m / 9.81 * Math.Min(1, t / 1.5));
            RoarLow = 0.06f; burn = 1f;
        }
        if (exhaust) exhaust.VoyageBurn = burn;
    }

    // ---------------- the HUD ----------------

    public void Hud(TextMeshProUGUI clock, TextMeshProUGUI telemetry, TextMeshProUGUI warp)
    {
        if (landing != null) { landing.Hud(clock, telemetry, warp); return; }
        int t = Mathf.Max(0, (int)Get);
        clock.text = $"T+{t / 3600:00}:{t / 60 % 60:00}:{t % 60:00}";
        double kmh = speedKms * 3600;
        string where = speedRef == "MOON"
            ? $"{moonKm - RmKm:#,0} KM ABOVE THE MOON"
            : earthKm - ReKm < 2000 ? $"{earthKm - ReKm:0} KM UP" : $"{earthKm - ReKm:#,0} KM FROM EARTH";
        string speed = $"{kmh:#,0} KM/H" + (speedRef == "MOON" ? " (MOON)" : "");
        string gs = g > 0.01f ? $"   ·   {g:0.00} G" : "";
        telemetry.text = Now == Part.Docking ? "TRANSPOSITION AND DOCKING" : $"{where}   ·   {speed}{gs}";
        warp.text = rate <= 0.0 ? "WAITING" : rate < 1.5 ? "REAL TIME" : rate < 1000 ? $"{rate:0}× SPEED" : "TIME-LAPSE";
    }

    void BuildUi()
    {
        var green = new Color(0.55f, 1f, 0.6f, 0.95f);
        // an action line above the caption: what you can do now
        actionPill = UiKit.Rect(hud, "Action", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 262), new Vector2(1000, 24));
        actionText = UiKit.Label(actionPill, "", 12, UiKit.Text, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000, 24), TextAlignmentOptions.Center, true, 4);
        UiKit.Shadow(actionText);
        actionPill.gameObject.SetActive(false);

        // the distance line: EARTH ●────▲──── ○ MOON
        lineRoot = UiKit.Rect(hud, "Distance", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -34), new Vector2(760, 60));
        UiKit.Box(UiKit.Rect(lineRoot, "Line", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -8), new Vector2(620, 2)), new Color(1, 1, 1, 0.35f), 0);
        UiKit.Dot(lineRoot, new Vector2(0.5f, 0.5f), new Vector2(-318, -8), 14, new Color(0.45f, 0.62f, 0.95f));
        var ring = UiKit.Rect(lineRoot, "MoonRing", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(318, -8), new Vector2(14, 14));
        var ri = ring.gameObject.AddComponent<Image>(); ri.sprite = UiKit.RingSprite; ri.raycastTarget = false;
        UiKit.Label(lineRoot, "EARTH", 11, UiKit.Text2, new Vector2(0.5f, 0.5f), new Vector2(1, 0.5f), new Vector2(-332, -8), new Vector2(80, 18), TextAlignmentOptions.Right, true, 3);
        UiKit.Label(lineRoot, "MOON", 11, UiKit.Text2, new Vector2(0.5f, 0.5f), new Vector2(0, 0.5f), new Vector2(332, -8), new Vector2(80, 18), TextAlignmentOptions.Left, true, 3);
        shipMark = UiKit.Rect(lineRoot, "Ship", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-300, -8), new Vector2(8, 16));
        UiKit.Box(shipMark, UiKit.Text, 2);
        lineText = UiKit.Label(lineRoot, "", 12, UiKit.Text, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, 0), new Vector2(760, 20), TextAlignmentOptions.Center, false, 2);
        UiKit.Shadow(lineText);
        lineRoot.gameObject.SetActive(false);

        // the crewman optical alignment sight: a thin cross, and two dark plates (readable over the white Eagle)
        var white = new Color(1f, 1f, 1f, 0.85f);
        coas = UiKit.Full(hud, "COAS");
        var centre = UiKit.Rect(coas, "Cross", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(440, 440));
        Cross(centre, 160f, 18f, white, true);
        var top = UiKit.Rect(coas, "Top", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -96), new Vector2(760, 92));
        UiKit.Box(top, new Color(0.02f, 0.022f, 0.026f, 0.92f), 12);
        UiKit.OutlineBox(UiKit.Full(top, "Edge"), new Color(1f, 1f, 1f, 0.16f));
        UiKit.Label(top, "DOCKING  ·  FLY IN, BRAKE, TOUCH GENTLY", 11, UiKit.Text2, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -12), new Vector2(740, 16), TextAlignmentOptions.Center, true, 4);
        coasText = UiKit.Label(top, "", 22, UiKit.Text, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -2), new Vector2(740, 30), TextAlignmentOptions.Center, true, 2);
        dockState = UiKit.Label(top, "", 12, UiKit.Text, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 10), new Vector2(740, 18), TextAlignmentOptions.Center, true, 4);
        var bottom = UiKit.Rect(coas, "Bottom", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 268), new Vector2(760, 86));
        UiKit.Box(bottom, new Color(0.02f, 0.022f, 0.026f, 0.92f), 12);
        UiKit.OutlineBox(UiKit.Full(bottom, "Edge"), new Color(1f, 1f, 1f, 0.16f));
        var bar = UiKit.Rect(bottom, "SpeedBar", new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -30), new Vector2(560, 4));
        UiKit.Box(bar, new Color(1, 1, 1, 0.16f), 2);
        speedBand = UiKit.Rect(bar, "Band", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(560 * SafeMin / 0.8f, 0), new Vector2(560 * (SafeMax - SafeMin) / 0.8f, 4));
        UiKit.Box(speedBand, new Color(1f, 1f, 1f, 0.55f), 2);
        speedMark = UiKit.Rect(bar, "Needle", new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(2, 20));
        UiKit.Box(speedMark, Color.white, 0);
        UiKit.Label(bar, "0", 10, UiKit.Text2, new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(-10, 0), new Vector2(40, 14), TextAlignmentOptions.MidlineRight, true, 1);
        UiKit.Label(bar, "0.8 M/S", 10, UiKit.Text2, new Vector2(1, 0.5f), new Vector2(0, 0.5f), new Vector2(10, 0), new Vector2(80, 14), TextAlignmentOptions.MidlineLeft, true, 1);
        UiKit.Label(bottom, "SPEED TOWARD EAGLE  ·  SAFE TO TOUCH INSIDE THE LIGHT BAND", 10, UiKit.Text2, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -44), new Vector2(740, 14), TextAlignmentOptions.Center, true, 3);
        UiKit.Label(bottom, "HOLD W  TOWARD EAGLE      HOLD S  BRAKE      P  AUTOPILOT", 12, UiKit.Text, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 12), new Vector2(740, 18), TextAlignmentOptions.Center, true, 4);
        arrows = new TextMeshProUGUI[0]; arrowMarks = new Image[0][];
        targetRing = UiKit.Rect(hud, "TargetRing", Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(34, 34));
        var tri = targetRing.gameObject.AddComponent<Image>(); tri.sprite = UiKit.Ring(64); tri.color = new Color(1f, 1f, 1f, 0.9f); tri.raycastTarget = false;
        targetRing.gameObject.SetActive(false);
        coas.gameObject.SetActive(false);

        // the DSKY for LOI: Program 40 asks with Verb 99, the crew answers PRO
        dsky = UiKit.Rect(hud, "DSKY", new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-40, 40), new Vector2(250, 300));
        UiKit.Box(dsky, UiKit.Card, 14);
        UiKit.Label(dsky, "DSKY", 11, UiKit.Text2, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -12), new Vector2(220, 18), TextAlignmentOptions.Center, true, 6);
        UiKit.Label(dsky, "PROG  40", 20, green, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -40), new Vector2(220, 28), TextAlignmentOptions.Center, true, 3);
        dskyVerb = UiKit.Label(dsky, "VERB  06", 20, green, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -72), new Vector2(220, 28), TextAlignmentOptions.Center, true, 3);
        dskyNoun = UiKit.Label(dsky, "NOUN  40", 20, green, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -104), new Vector2(220, 28), TextAlignmentOptions.Center, true, 3);
        dskyR1 = UiKit.Label(dsky, "", 15, green, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -140), new Vector2(230, 70), TextAlignmentOptions.Center, false, 2);
        var pro = UiKit.Rect(dsky, "PRO", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 18), new Vector2(200, 48));
        UiKit.Button(pro, UiKit.Selected, 10, () => proClicked = true);
        UiKit.Label(pro, "PRO", 18, UiKit.Ink, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(180, 30), TextAlignmentOptions.Center, true, 6);
        dsky.gameObject.SetActive(false);

        // the explainer: a side-view diagram of the manoeuvre
        explainer = UiKit.Rect(hud, "Explainer", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 90), new Vector2(760, 300));
        UiKit.Box(explainer, UiKit.Card, 18);
        UiKit.Label(explainer, "TRANSPOSITION AND DOCKING", 16, UiKit.Text, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -22), new Vector2(700, 24), TextAlignmentOptions.Center, true, 4);
        var stage = UiKit.Rect(explainer, "Stage", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-190, 10), new Vector2(240, 46));
        UiKit.Box(stage, new Color(0.86f, 0.86f, 0.84f), 6);
        UiKit.Label(explainer, "THIRD STAGE", 11, UiKit.Text2, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0), new Vector2(-190, 40), new Vector2(200, 16), TextAlignmentOptions.Center, true, 3);
        exAdapter = UiKit.Rect(explainer, "Adapter", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-34, 10), new Vector2(76, 46));
        UiKit.Box(exAdapter, new Color(0.86f, 0.85f, 0.82f, 0.9f), 4);
        exEagle = UiKit.Rect(explainer, "Eagle", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-34, 10), new Vector2(38, 30));
        UiKit.Box(exEagle, new Color(0.85f, 0.62f, 0.2f), 4);
        UiKit.Label(exEagle, "EAGLE", 10, new Color(0.85f, 0.62f, 0.2f), new Vector2(0.5f, 0), new Vector2(0.5f, 1), new Vector2(0, -8), new Vector2(80, 14), TextAlignmentOptions.Center, true, 2);
        exCsm = UiKit.Rect(explainer, "Columbia", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(70, 10), new Vector2(90, 36));
        UiKit.Box(UiKit.Rect(exCsm, "SM", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-12, 0), new Vector2(64, 36)), new Color(0.72f, 0.73f, 0.74f), 4);
        UiKit.Box(UiKit.Rect(exCsm, "CM", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(31, 0), new Vector2(22, 26)), new Color(0.92f, 0.92f, 0.92f), 4);
        UiKit.Box(UiKit.Rect(exCsm, "Nose", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(44, 0), new Vector2(8, 10)), new Color(0.92f, 0.92f, 0.92f), 2);
        exCsmLabel = UiKit.Label(explainer, "COLUMBIA", 10, UiKit.Text, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0), new Vector2(70, 34), new Vector2(120, 14), TextAlignmentOptions.Center, true, 2);
        exText = UiKit.Label(explainer, "", 14, UiKit.Text, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 26), new Vector2(700, 60), TextAlignmentOptions.Center, false, 1);
        explainer.gameObject.SetActive(false);

        // names over the three craft in the side view
        tags = UiKit.Rect(hud, "Tags", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
        tags.anchorMin = Vector2.zero; tags.anchorMax = Vector2.one; tags.sizeDelta = Vector2.zero;
        RectTransform Tag(string n)
        {
            var t = UiKit.Rect(tags, n, Vector2.zero, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(160, 20));
            var l = UiKit.Label(t, n, 12, UiKit.Text, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(160, 20), TextAlignmentOptions.Center, true, 3);
            UiKit.Shadow(l);
            return t;
        }
        tagCol = Tag("COLUMBIA"); tagEagle = Tag("EAGLE"); tagStage = Tag("THIRD STAGE");
        tags.gameObject.SetActive(false);

        // the navigation telescope: a thin circle, a thin cross, the star to find in brackets with its name
        scope = UiKit.Full(hud, "Scope");
        var sr = UiKit.Rect(scope, "Ring", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560, 560));
        var sri = sr.gameObject.AddComponent<Image>(); sri.sprite = UiKit.Ring(560); sri.color = new Color(1f, 1f, 1f, 0.55f); sri.raycastTarget = false;
        Cross(scope, 40f, 8f, new Color(1f, 1f, 1f, 0.8f), false);
        starMark = UiKit.Rect(scope, "StarMark", Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40, 40));
        for (int cx = -1; cx <= 1; cx += 2)
            for (int cy = -1; cy <= 1; cy += 2)
            {
                UiKit.Box(UiKit.Rect(starMark, "H", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(cx * 16f, cy * 20f), new Vector2(9, 1.5f)), Color.white, 0);
                UiKit.Box(UiKit.Rect(starMark, "V", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(cx * 20f, cy * 16f), new Vector2(1.5f, 9)), Color.white, 0);
            }
        starName = UiKit.Label(starMark, "", 11, UiKit.Text, new Vector2(0.5f, 0), new Vector2(0.5f, 1), new Vector2(0, -6), new Vector2(160, 16), TextAlignmentOptions.Center, true, 3);
        UiKit.Shadow(starName);
        scopeArrow = UiKit.Chevron(scope, new Vector2(0.5f, 0.5f), Vector2.zero, 22f, Color.white);
        var sp = UiKit.Rect(scope, "Plate", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -90), new Vector2(820, 66));
        UiKit.Box(sp, new Color(0.02f, 0.022f, 0.026f, 0.9f), 10);
        UiKit.OutlineBox(UiKit.Full(sp, "Edge"), new Color(1f, 1f, 1f, 0.16f));
        scopeText = UiKit.Label(sp, "", 13, UiKit.Text, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(790, 56), TextAlignmentOptions.Center, false, 1);
        scopeText.lineSpacing = 10;
        scope.gameObject.SetActive(false);

        // the mission report at the end
        scoreCard = UiKit.Rect(hud, "Score", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 60), new Vector2(620, 320));
        UiKit.Box(scoreCard, UiKit.Card, 18);
        scoreText = UiKit.Label(scoreCard, "", 15, UiKit.Text, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560, 300), TextAlignmentOptions.MidlineLeft, false, 1);
        scoreText.lineSpacing = 10;
        scoreCard.gameObject.SetActive(false);
    }

    // a thin cross with a gap in the middle (and ticks on it for the COAS)
    static void Cross(RectTransform parent, float arm, float gap, Color c, bool ticks)
    {
        UiKit.Box(UiKit.Rect(parent, "L", new Vector2(0.5f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-gap, 0), new Vector2(arm, 1.5f)), c, 0);
        UiKit.Box(UiKit.Rect(parent, "R", new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f), new Vector2(gap, 0), new Vector2(arm, 1.5f)), c, 0);
        UiKit.Box(UiKit.Rect(parent, "U", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), new Vector2(0, gap), new Vector2(1.5f, arm)), c, 0);
        UiKit.Box(UiKit.Rect(parent, "D", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), new Vector2(0, -gap), new Vector2(1.5f, arm)), c, 0);
        if (!ticks) return;
        for (int k = 1; k <= 4; k++)
        {
            float o = gap + k * (arm / 4.5f);
            foreach (var v in new[] { new Vector2(o, 0), new Vector2(-o, 0) })
                UiKit.Box(UiKit.Rect(parent, "T", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), v, new Vector2(1.5f, 8)), c, 0);
            foreach (var v in new[] { new Vector2(0, o), new Vector2(0, -o) })
                UiKit.Box(UiKit.Rect(parent, "T", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), v, new Vector2(8, 1.5f)), c, 0);
        }
    }

    void DockingUi()
    {
        float closing = -dockVel.z, range = Mathf.Max(0f, dockRel.z);
        coasText.text = $"RANGE {range,5:0.0} M      SPEED {closing,5:0.00} M/S";
        float lo = latchKnown ? 0.15f : SafeMin;
        string state = closing < -0.01f ? "MOVING AWAY  ·  HOLD W"
                     : closing < 0.02f ? "STOPPED  ·  HOLD W TO GO TOWARD EAGLE"
                     : range > 3f ? (closing > 0.9f ? "FAST  ·  START BRAKING SOON" : "COMING IN  ·  BRAKE BEFORE YOU TOUCH")
                     : closing > SafeMax ? "TOO FAST  ·  HOLD S" : closing < lo ? "TOO SLOW  ·  A LITTLE W" : "GOOD  ·  KEEP IT THERE";
        dockState.text = state;
        speedBand.anchoredPosition = new Vector2(560f * lo / 0.8f, 0f);
        speedBand.sizeDelta = new Vector2(560f * (SafeMax - lo) / 0.8f, 4f);
        speedMark.anchoredPosition = new Vector2(560f * Mathf.Clamp01(closing / 0.8f), 0f);
        var t = lm ? lm.Find("DockingTarget") : null;
        if (t)
        {
            Vector3 sp = fd.Cam.WorldToScreenPoint(t.position);
            float scale = targetRing.GetComponentInParent<Canvas>().scaleFactor;
            targetRing.gameObject.SetActive(sp.z > 0f);
            targetRing.anchoredPosition = (Vector2)sp / scale;
        }
    }

    void Ui()
    {
        // what you can do now
        string act = Now switch
        {
            Part.Orbit when !aligned && Problems.I != null && Problems.I.LightningRun => "",
            _ when floating => "W A S D R F  DRIFT   ·   U  BACK TO THE COUCH",
            Part.Orbit => "U  UNSTRAP AND FLOAT",
            Part.Align => "DRAG OR W A S D  AIM   ·   SPACE  MARK THE STAR",
            Part.Explainer => "SPACE  SKIP",
            Part.Docking => "",
            Part.Coast when beatWait >= 0f => "SPACE  GO ON   ·   U  FLOAT",
            Part.Coast => "U  UNSTRAP AND FLOAT",
            Part.Lunar => "U  UNSTRAP AND FLOAT",
            Part.Pro => "PRESS PRO WHEN VERB 99 FLASHES  (OR P)",
            _ => "",
        };
        bool cardUp = Problems.I != null && Problems.I.Deciding;
        actionPill.gameObject.SetActive(act.Length > 0 && !fd.Outside && !cardUp);
        actionText.text = act;
        if (Now != Part.Docking) targetRing.gameObject.SetActive(false);
        if (Now == Part.Coast || Now == Part.Pro)
        {
            double eAlt = earthKm, mAlt = moonKm;
            float f = (float)(eAlt / (eAlt + mAlt));
            shipMark.anchoredPosition = new Vector2(-310f + 620f * f, -8f);
            int day = (int)(Get / 86400.0) + 1;
            lineText.text = $"DAY {day}   ·   {eAlt:#,0} KM FROM EARTH   ·   {mAlt:#,0} KM TO THE MOON";
        }
        if (Now == Part.Pro)
        {
            bool asked = Get >= LoiIgn - 5;
            bool blink = asked && !proPressed && Mathf.Repeat(Time.time, 0.8f) < 0.5f;
            dskyVerb.text = asked ? "VERB  99" : "VERB  06";
            dskyVerb.alpha = asked && !proPressed ? (blink ? 1f : 0.15f) : 1f;
            dskyNoun.alpha = dskyVerb.alpha;
            double tig = Get - LoiIgn;
            int s = (int)Math.Abs(tig);
            dskyR1.text = $"TIG  {(tig < 0 ? "-" : "+")}{s / 60:00}:{s % 60:00}\nVG   +02917 FT/S";
        }
    }

    string SiteName() => MissionState.HasSite ? $"{MissionModel.PlaceName(MissionState.SiteLat, MissionState.SiteLon)}" : "Tranquility Base";

    // twelve docking latches snapping shut
    static AudioClip Latches()
    {
        const int sr = 44100; int n = sr * 2;
        var d = new float[n]; var rnd = new System.Random(3);
        for (int k = 0; k < 13; k++)
        {
            int at = k == 0 ? 0 : (int)((0.55f + k * 0.06f + (float)rnd.NextDouble() * 0.03f) * sr);
            float amp = k == 0 ? 0.9f : 0.45f;
            for (int i = 0; i < 2600 && at + i < n; i++)
            {
                float t = (float)i / sr;
                d[at + i] += amp * ((float)rnd.NextDouble() * 2f - 1f) * Mathf.Exp(-t * (k == 0 ? 60f : 160f)) + amp * 0.6f * Mathf.Sin(t * 2f * Mathf.PI * (k == 0 ? 90f : 420f)) * Mathf.Exp(-t * 40f);
            }
        }
        var c = AudioClip.Create("Docking_Latches", n, 1, sr, false); c.SetData(d, 0); return c;
    }
}
