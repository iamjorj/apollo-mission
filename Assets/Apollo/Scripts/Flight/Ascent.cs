// Step 5: the climb to Earth orbit, replayed from Apollo 11's real postflight trajectory (Boeing D5-15560-6,
// "Apollo/Saturn V Postflight Trajectory - AS-506", 1969; events from NASA SP-4029; the ride from the crew's
// technical debriefing). Real time at liftoff and at every big moment, sped up in between (about 2.5 min).
//
// The rocket stays where it is and the world moves away under it (so the cabin is steady 190 km up and
// 2,700 km downrange). Its attitude follows the real flight: straight up, the yaw away from the tower, the
// 18 degree roll onto the 72 degree flight azimuth, the pitch-over. The sky, the Earth below, the Sun, the
// Moon, the planets and the stars are set for the real position and time; the 3D clouds pass by.
using System;
using UnityEngine;

public class Ascent : MonoBehaviour
{
    FlightDirector fd;
    Transform world, stack, eye;
    Vector3 worldStart, cgRel;
    float[] T, Alt, Range, Vef, Vin, Head, Lat, Lon, Acc, Pitch;
    float met, rate = 1f, push, pushV, flash, bangAt = -1f;
    int nextEvent;
    AudioSource bang;
    bool worldHidden;
    Transform farSurroundings;
    DateTime t0;
    float orbitLat, orbitLon;

    public float Met => met;
    public float Flash => flash;
    public Transform Stack => stack;
    public float Rate => rate;
    public float Shake { get; private set; }
    public float G { get; private set; } = 1f;
    public Vector3 PushWorld { get; private set; }
    public float RoarLow { get; private set; }
    public float RoarMid { get; private set; }
    public float WindNoise { get; private set; }
    public bool InOrbit => met >= 709.33f;
    public float AltitudeM { get; private set; }
    public float SpeedKmh { get; private set; }
    public float Downrange { get; private set; }

    // how fast the mission clock runs: real time at liftoff, at the lightning strike (Apollo 12's, see
    // Lightning.cs), at staging and at orbit; fast in between, so the whole climb takes about 75 s
    static readonly (float met, float rate)[] Warp =
    {
        (0f, 1f), (12f, 8f), (32f, 1f), (46f, 12f), (157f, 1f), (166f, 12f), (200f, 40f), (690f, 2f), (698f, 1f),
    };
    public bool HoldRealTime;                          // a problem on board: the clock runs at real time until it is fixed
    public bool Paused;
    public float GCut;                                 // an engine out (EngineOut): less push                                // a decision card is up: the clock stops while the student thinks

    // what happens, and one simple line about it
    public static readonly (float met, string title, string line, float jolt, float flash)[] Events =
    {
        (0.3f, "HOLD-DOWN ARMS RELEASE", "Four arms held the rocket down until all five engines were at full thrust.", 0.5f, 0f),
        (12f, "TOWER CLEARED", "Control passes from the launch team in Florida to Mission Control in Houston.", 0f, 0f),
        (13.2f, "ROLL AND PITCH PROGRAM", "The rocket rolls onto its flight path, 72 degrees east of north, and starts to lean over.", 0f, 0f),
        (66.3f, "MACH 1", "Faster than sound: about 1,160 km/h, 7.8 km up.", 0f, 0f),
        (83f, "MAX Q", "The air pushes hardest on the rocket here, 13.6 km up.", 0.15f, 0f),
        (135.2f, "CENTER ENGINE CUTOFF", "The middle engine stops early so the g-force does not climb too high.", 0.35f, 0f),
        (161.63f, "FIRST STAGE CUTOFF", "The first stage burned about 2,000 tonnes of fuel in 2 minutes 41 seconds.", 1.0f, 0f),
        (162.3f, "STAGING", "The empty first stage falls away. It lands in the Atlantic about 650 km from the pad.", 0.5f, 1.0f),
        (164.0f, "SECOND STAGE IGNITION", "Five J-2 engines burning liquid hydrogen. The crew: \"smooth as glass\".", 0.45f, 0f),
        (192.3f, "INTERSTAGE JETTISON", "The ring between the first and second stage drops off.", 0.15f, 0f),
        (197.9f, "ESCAPE TOWER JETTISON", "The launch escape tower is not needed any more. It flies off with the cover over the capsule.", 0.6f, 0f),
        (460.62f, "CENTER ENGINE CUTOFF", "The second stage's middle engine stops, to keep the g-force down.", 0.25f, 0f),
        (499f, "MIXTURE RATIO SHIFT", "The engines change their fuel mix. Armstrong: \"You could feel g's decrease.\"", 0.3f, 0f),
        (548.22f, "SECOND STAGE CUTOFF", "The second stage is empty, 187 km up and 1,600 km from Florida.", 0.8f, 0f),
        (549.0f, "STAGING", "The second stage falls away. It will land in the Atlantic, 4,200 km from the pad.", 0.4f, 0f),
        (552.2f, "THIRD STAGE IGNITION", "The S-IVB's single engine. It will fire again later to send Apollo 11 to the Moon.", 0.35f, 0f),
        (699.33f, "THIRD STAGE CUTOFF", "Engines off. You are falling around the Earth: that is an orbit.", 0.5f, 0f),
        (709.33f, "ORBIT INSERTION", "Orbit 186 by 183 km. Speed 28,000 km/h; one lap around the Earth takes 88 minutes.", 0f, 0f),
    };

    // back to an earlier moment of the climb (after a problem was lost): everything else follows from the clock
    public void Rewind(float to)
    {
        met = to; rate = 1f; Paused = false;
        nextEvent = 0;
        while (nextEvent < Events.Length && Events[nextEvent].met <= to) nextEvent++;
    }

    public void Begin(FlightDirector director, Transform worldRoot, Transform saturn, Transform cabin, Transform commanderEye)
    {
        fd = director; world = worldRoot; eye = commanderEye;
        t0 = MissionState.LaunchUtc;
        Load();
        stack = new GameObject("Stack").transform;
        stack.SetPositionAndRotation(eye.position, Quaternion.identity);
        saturn.SetParent(stack, true);
        cabin.SetParent(stack, true);
        worldStart = world.position;
        farSurroundings = world.Find("FarSurroundings");
        foreach (var r in world.GetComponentsInChildren<Renderer>(true))       // the world moves: a static-batched mesh would stay behind
            if (r.isPartOfStaticBatch) Debug.LogError($"FLIGHT {r.name} is static-batched and will not move with the world");
        cgRel = new Vector3(0f, FlightDirector.NozzleYPublic + 59.4f - 5f, 0f) - eye.position;   // the centre of gravity, 59 m above the pad
        bang = gameObject.AddComponent<AudioSource>();
        bang.clip = Thump(); bang.playOnAwake = false; bang.spatialBlend = 0f;
        enabled = true;
    }

    void Load()
    {
        var csv = Resources.Load<TextAsset>("Ascent/apollo11_AS506_ascent");
        var rows = csv.text.Split('\n');
        var lists = new System.Collections.Generic.List<float[]>();
        for (int i = 1; i < rows.Length; i++)
        {
            if (rows[i].Trim().Length == 0) continue;
            var f = rows[i].Split(',');
            float P(int k) => float.TryParse(f[k].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : float.NaN;
            lists.Add(new[] { P(0), P(2), P(3), P(5), P(4), P(8), P(9), P(10), P(11), P(12) });
        }
        int n = lists.Count;
        T = new float[n]; Alt = new float[n]; Range = new float[n]; Vef = new float[n]; Vin = new float[n];
        Head = new float[n]; Lat = new float[n]; Lon = new float[n]; Acc = new float[n]; Pitch = new float[n];
        for (int i = 0; i < n; i++)
        {
            var r = lists[i];
            T[i] = r[0]; Alt[i] = r[1] * 1000f; Range[i] = r[2] * 1000f; Vef[i] = r[3]; Vin[i] = r[4];
            Head[i] = r[5]; Lat[i] = r[6]; Lon[i] = r[7]; Acc[i] = r[8]; Pitch[i] = r[9];
        }
        // the table leaves a few values blank at the staging instants: carry the last one over
        foreach (var arr in new[] { Alt, Range, Vef, Vin, Head, Lat, Lon, Acc, Pitch })
            for (int i = 0; i < n; i++)
                if (float.IsNaN(arr[i])) arr[i] = i > 0 ? arr[i - 1] : 0f;
    }

    float At(float[] v, float t)
    {
        if (t <= T[0]) return v[0];
        for (int i = 0; i < T.Length - 1; i++)
            if (t <= T[i + 1]) return Mathf.Lerp(v[i], v[i + 1], Mathf.InverseLerp(T[i], T[i + 1], t));
        return v[v.Length - 1];
    }

    float TargetRate(float m)
    {
        float r = 1f;
        foreach (var w in Warp) if (m >= w.met) r = w.rate;
        if (fd.FastTest) r = Mathf.Max(r, 1f);
        if (HoldRealTime) r = 1f;
        return r;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        rate = Mathf.Lerp(rate, InOrbit ? 1f : TargetRate(met), 1f - Mathf.Exp(-dt / 0.6f));
        float dm = Paused ? 0f : dt * rate;
        float before = met;
        met += dm;

        // ---------- where and how the vehicle is ----------
        float tm = Mathf.Min(met, 709.33f);
        float alt = At(Alt, tm), range = At(Range, tm);
        float up = alt - Alt[0];
        float az = tm < 13.2f ? 90f : tm < 31.1f ? Mathf.Lerp(90f, 72.058f, Mathf.SmoothStep(0f, 1f, (tm - 13.2f) / 17.9f))
                 : Mathf.Lerp(72.058f, At(Head, tm), Mathf.SmoothStep(0f, 1f, (tm - 31.1f) / 130f));
        float pitch = tm < 13.2f ? 90f : Mathf.Lerp(90f, At(Pitch, tm), Mathf.SmoothStep(0f, 1f, (tm - 13.2f) / 6f));
        if (InOrbit) pitch = Mathf.Lerp(1.5f, 0f, Mathf.Clamp01((met - 719.3f) / 20f));          // the maneuver to local horizontal
        float yaw = 1.25f * Mathf.Sin(Mathf.PI * Mathf.Clamp01((tm - 1.7f) / 8f));               // away from the tower
        Vector3 h = new Vector3(Mathf.Sin(az * Mathf.Deg2Rad), 0f, Mathf.Cos(az * Mathf.Deg2Rad));
        float p = pitch * Mathf.Deg2Rad;
        Vector3 nose = h * Mathf.Cos(p) + Vector3.up * Mathf.Sin(p), top = -h * Mathf.Sin(p) + Vector3.up * Mathf.Cos(p);
        Quaternion q = Quaternion.LookRotation(nose, top) * Quaternion.Inverse(Quaternion.LookRotation(Vector3.up, Vector3.left));
        q = Quaternion.AngleAxis(-yaw, Vector3.right) * q;
        stack.rotation = q;
        Vector3 horiz = new Vector3(Mathf.Sin(72.058f * Mathf.Deg2Rad), 0f, Mathf.Cos(72.058f * Mathf.Deg2Rad)) * range
                      + Vector3.back * 2.5f * Mathf.SmoothStep(0f, 1f, (tm - 1.7f) / 8f);
        Vector3 pivotFix = q * cgRel - cgRel;                                                     // turn about the centre of gravity
        world.position = worldStart - new Vector3(horiz.x, up, horiz.z) + pivotFix;
        AltitudeM = alt; Downrange = range;
        SpeedKmh = (InOrbit ? 7389.5f : At(Vef, tm)) * 3.6f;

        // ---------- the world, the sky and space ----------
        if (!worldHidden && up > 26000f) { worldHidden = true; foreach (Transform c in world) if (!c.name.StartsWith("Terrain") && !c.name.StartsWith("Water")) c.gameObject.SetActive(false); }
        Shader.SetGlobalFloat("_WorldFade", Mathf.InverseLerp(22000f, 40000f, up));
        if (up > 45000f && world.gameObject.activeSelf) world.gameObject.SetActive(false);
        if (up > 1500f && farSurroundings != null && farSurroundings.gameObject.activeSelf)
        {
            farSurroundings.gameObject.SetActive(false);
            foreach (Transform c in world)                                                     // tall landmarks far away: lost below the clouds
                if (c.name.StartsWith("SpaceCenter") || c.name.Contains("39B") || c.name.StartsWith("Vegetation") || c.name.StartsWith("Propellant")) c.gameObject.SetActive(false);
        }
        float lat = At(Lat, tm), lon = At(Lon, tm);
        if (InOrbit) OrbitTrack(met - 709.33f, out lat, out lon);
        fd.SetPlace(t0.AddSeconds(met), lat, lon, 14.1f + up, horiz, Mathf.Clamp01(1f - Mathf.InverseLerp(12000f, 26000f, up)));

        // ---------- the ride ----------
        float g = InOrbit ? 0f : met < 0.3f ? 1f : At(Acc, tm);
        if (tm > 161.63f && tm < 164.6f) g = tm < 162.4f ? Mathf.Lerp(3.94f, 0.05f, (tm - 161.63f) / 0.7f) : Mathf.Lerp(0.05f, 0.78f, (tm - 163.5f) / 1.1f);
        if (tm > 548.22f && tm < 553.5f) g = tm < 549f ? Mathf.Lerp(1.46f, 0.02f, (tm - 548.22f) / 0.6f) : Mathf.Lerp(0.02f, 0.56f, (tm - 552.2f) / 1.3f);
        if (tm > 699.33f && tm < 700.5f) g = Mathf.Lerp(0.69f, 0f, (tm - 699.33f) / 0.5f);
        G = Mathf.Max(g - GCut, 0f);
        // the seat: pressed back by g, thrown forward when it drops ("we were tossed forward")
        float target = -0.012f * G;
        pushV += ((target - push) * 120f - pushV * 9f) * dt;
        push += pushV * dt;
        // events: lines on the card, jolts, the flash
        while (nextEvent < Events.Length && met >= Events[nextEvent].met)
        {
            var e = Events[nextEvent++];
            fd.AscentEvent(e.title, e.line);
            pushV += e.jolt * 0.35f;
            flash = Mathf.Max(flash, e.flash);
            if (e.jolt > 0.3f) { bang.volume = Mathf.Clamp01(e.jolt); bang.Play(); }
        }
        flash = Mathf.MoveTowards(flash, 0f, dt * 1.2f);
        Shader.SetGlobalFloat("_Flash", flash * (fd.Outside ? 0.15f : 0.9f));
        PushWorld = stack.rotation * Vector3.up * push;
        // vibration: severe until the tower is cleared, rough on the first stage (with sharp bumps),
        // smooth on the second, a little rattly on the third, none in orbit
        float s;
        if (tm < 12f) s = Mathf.Lerp(1.15f, 0.55f, tm / 12f);
        else if (tm < 161.6f) s = 0.40f + 0.12f * Mathf.InverseLerp(40f, 83f, tm) * Mathf.InverseLerp(110f, 83f, tm) + (Mathf.PerlinNoise(met * 0.8f, 3f) > 0.72f ? 0.35f : 0f);
        else if (tm < 548.2f) s = 0.07f;
        else if (tm < 699.3f) s = 0.16f;
        else s = 0f;
        Shake = s;
        // sound: the roar thins with the air; the upper stages are a hum through the structure
        float air = Mathf.Exp(-alt / 12000f);
        RoarLow = tm < 161.6f ? Mathf.Lerp(0.18f, 0.75f, air) : tm < 548.2f ? 0.16f : tm < 699.3f ? 0.11f : 0f;
        RoarMid = tm < 161.6f ? RoarLow * 0.5f : RoarLow * 0.25f;
        float qd = Mathf.Exp(-Mathf.Pow((tm - 83f) / 35f, 2f));
        WindNoise = tm < 150f ? 0.25f * qd : 0f;
    }

    // after insertion: around the Earth along the orbit (inclination 32.5 deg, insertion 32.67 N 52.69 W heading 88.85 deg)
    void OrbitTrack(float s, out float lat, out float lon)
    {
        double rad = Math.PI / 180.0;
        double w = 7793.1 / (6378165.0 + 191082.0), ang = w * s;
        double la = 32.5027 * rad, lo = -52.694 * rad, hd = 88.848 * rad;
        double la2 = Math.Asin(Math.Sin(la) * Math.Cos(ang) + Math.Cos(la) * Math.Sin(ang) * Math.Cos(hd));
        double lo2 = lo + Math.Atan2(Math.Sin(hd) * Math.Sin(ang) * Math.Cos(la), Math.Cos(ang) - Math.Sin(la) * Math.Sin(la2));
        lo2 -= 7.2921159e-5 * s;                                                                  // the Earth turns underneath
        lat = (float)(la2 / rad); lon = (float)(lo2 / rad);
    }

    static AudioClip Thump()
    {
        const int sr = 44100; int n = sr * 9 / 10;
        var d = new float[n];
        var rnd = new System.Random(7);
        float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / sr;
            lp += (((float)rnd.NextDouble() * 2f - 1f) - lp) * 0.06f;
            d[i] = (lp * 3f + Mathf.Sin(t * 2f * Mathf.PI * 38f) * 0.6f) * Mathf.Exp(-t * 6f) * Mathf.Min(1f, t * 200f);
        }
        var c = AudioClip.Create("Staging_Thump", n, 1, sr, false);
        c.SetData(d, 0);
        return c;
    }
}
