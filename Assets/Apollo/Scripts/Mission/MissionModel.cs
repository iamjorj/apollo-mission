// The three checks before launch, for any date covered by the NASA JPL tables and any point on the Moon.
//   Flight path:  Earth must be above the site's horizon (radio contact), and the Sun must be RISING
//                 and 5-14 degrees high at touchdown (Apollo rule: long shadows show rocks; a setting
//                 Sun would shine into the crew's eyes).
//   Rocket:       enough Saturn V propellant to reach orbit and then the Moon (rocket equation).
//   Life support: enough oxygen and hydrogen for the trip plus a spare day.
// Masses and rates are Apollo 11's (rounded); the physics are simplified models.
using System;
using UnityEngine;

public static class MissionModel
{
    public struct Check
    {
        public bool Go;
        public string Simple;
        public static Check Ok() => new Check { Go = true, Simple = "" };
        public static Check No(string why) => new Check { Go = false, Simple = why };
    }

    public const double SunMin = 5.0, SunMax = 14.0;
    public const double LandingAfterHours = 102.75;     // Apollo 11: launch to touchdown

    // Real July 1969 windows (UTC) for the Apollo 11 check; other days use a 14:00 UTC morning window.
    static readonly (int day, int openMinUtc)[] Windows1969 = { (16, 13 * 60 + 32), (18, 15 * 60 + 32), (21, 16 * 60 + 4) };

    public static DateTime WindowOpenUtc(DateTime day)
    {
        if (day.Year == 1969)
            foreach (var w in Windows1969)
                if (day.Month == 7 && day.Day == w.day) return day.AddMinutes(w.openMinUtc);
        return day.AddHours(14);
    }

    public static bool HasWindow(DateTime day) => Ephemeris.Covers(WindowOpenUtc(day).AddHours(LandingAfterHours));

    public static DateTime LandingUtc(DateTime launch) => launch.AddHours(LandingAfterHours);

    public static double SunAt(DateTime launch, double lat, double lon) =>
        Ephemeris.SunElevationOnMoon(LandingUtc(launch), lat, lon);

    public static bool SunRising(DateTime launch, double lat, double lon) =>
        Ephemeris.SunElevationOnMoon(LandingUtc(launch).AddHours(1), lat, lon) > SunAt(launch, lat, lon);

    public static double EarthAt(DateTime launch, double lat, double lon) =>
        Ephemeris.EarthElevationOnMoon(LandingUtc(launch), lat, lon);

    public static Check FlightPath(out double sunDeg)
    {
        sunDeg = double.NaN;
        if (!MissionState.HasSite) return Check.No("No landing site has been chosen on the Moon.");
        var launch = MissionState.LaunchUtc;
        double lat = MissionState.SiteLat, lon = MissionState.SiteLon;
        sunDeg = SunAt(launch, lat, lon);
        if (!HasWindow(launch.Date)) return Check.No("There's no launch window on this day.");
        if (EarthAt(launch, lat, lon) < 0) return Check.No("The site is on the far side of the Moon. The crew would have no radio contact with Earth.");
        if (sunDeg < 0) return Check.No("It will be night at the landing site. The crew couldn't see the ground.");
        if (!SunRising(launch, lat, lon)) return Check.No("The Sun will be setting at the site. It would shine straight into the crew's eyes as they land.");
        if (sunDeg < SunMin) return Check.No("The Sun will be too low at the site. Long shadows would hide the ground.");
        if (sunDeg > SunMax) return Check.No("The Sun will be too high at the site. Without shadows, rocks and craters are hard to see.");
        if (!MissionState.Hybrid && !FreeReturnReaches(lat, lon))
            return Check.No("A free-return route only reaches sites near the middle of the Moon's face (within 5° of the equator, 45° of the centre). Choose HYBRID, or a site in that zone.");
        return Check.Ok();
    }

    // ---------------- the route ----------------
    // Free return (Apollo 8, 10, 11): aimed so that with no engine burn at all the Moon swings the spacecraft back to the
    // Earth (Apollo 13 lived because of it). It fixes the path past the Moon, so only sites near the equator and the
    // middle of the near side can be reached. Hybrid (Apollo 12 to 17): start on free return, then a course-correction
    // burn leaves it to reach any site; from then on only the engine can bring the crew home.
    public static bool FreeReturnReaches(double lat, double lon) => Math.Abs(lat) <= 5.0 && Math.Abs(lon) <= 45.0;

    // Same rules as FlightPath, for any launch time and site (cheapest test first).
    public static bool FlightPathGo(DateTime launch, double lat, double lon)
    {
        if (!HasWindow(launch.Date)) return false;
        double s = SunAt(launch, lat, lon);
        if (s < SunMin || s > SunMax) return false;
        return SunRising(launch, lat, lon) && EarthAt(launch, lat, lon) >= 0;
    }

    // ---------------- suggestions: one day, one place ----------------

    // The first day from 'from' whose launch window gives a GO flight path for this site.
    public static DateTime? NextGoDay(DateTime from, DateTime last, double lat, double lon)
    {
        for (var d = from.Date; d <= last; d = d.AddDays(1))
            if (FlightPathGo(WindowOpenUtc(d), lat, lon)) return d;
        return null;
    }

    // The best place to land for a launch time, scored the way Apollo planners chose sites:
    // Sun near 10 degrees and rising, close to the equator and the near-side centre, on smooth mare
    // rather than in a crater or the highlands. Returns false if no near-side site works that day.
    public static bool BestSite(DateTime launch, out double bestLat, out double bestLon)
    {
        bestLat = bestLon = 0;
        if (!HasWindow(launch.Date)) return false;
        DateTime t = LandingUtc(launch);
        Ephemeris.MoonAxes(t, out var m, out var e, out var p);
        Ephemeris.MoonAxes(t.AddHours(1), out var m1, out var e1, out var p1);
        Vector3 sun = Ephemeris.ToWorldDir(Ephemeris.SunKm(t) - Ephemeris.MoonKm(t));
        Vector3 sun1 = Ephemeris.ToWorldDir(Ephemeris.SunKm(t.AddHours(1)) - Ephemeris.MoonKm(t.AddHours(1)));
        Vector3 earth = (-Ephemeris.MoonWorld(t)).normalized;

        double Cost(double lat, double lon)
        {
            Vector3 b = Ephemeris.BodyPoint(lat, lon);
            Vector3 n = m * b.x + p * b.y + e * b.z, n1 = m1 * b.x + p1 * b.y + e1 * b.z;
            double s = Math.Asin(Mathf.Clamp(Vector3.Dot(n, sun), -1f, 1f)) * 180.0 / Math.PI;
            if (s < SunMin + 0.3 || s > SunMax - 0.3) return double.MaxValue;
            if (Vector3.Dot(n1, sun1) <= Vector3.Dot(n, sun)) return double.MaxValue;        // setting
            if (Vector3.Dot(n, earth) < 0.17f) return double.MaxValue;                       // Earth under 10 deg
            if (!MissionState.Hybrid && !FreeReturnReaches(lat, lon)) return double.MaxValue;
            string place = PlaceName(lat, lon);
            bool smooth = place.StartsWith("MARE") || place.StartsWith("SINUS") || place.StartsWith("OCEANUS") || place.StartsWith("APOLLO");
            return Math.Abs(s - 10.0) + 0.15 * Math.Abs(lat) + 0.03 * Math.Abs(lon) + (smooth ? 0 : 4);
        }

        double best = double.MaxValue;
        for (int la = -60; la <= 60; la++)
            for (int lo = -90; lo <= 90; lo++)
            {
                double c = Cost(la, lo);
                if (c < best) { best = c; bestLat = la; bestLon = lo; }
            }
        if (best == double.MaxValue) return false;
        double cLat = bestLat, cLon = bestLon;                                                  // refine to 0.1 deg
        for (double la = cLat - 1; la <= cLat + 1.001; la += 0.1)
            for (double lo = cLon - 1; lo <= cLon + 1.001; lo += 0.1)
            {
                double c = Cost(la, lo);
                if (c < best) { best = c; bestLat = Math.Round(la, 1); bestLon = Math.Round(lo, 1); }
            }
        return FlightPathGo(launch, bestLat, bestLon);
    }

    // ---------------- rocket: Saturn V AS-506 (kg) ----------------
    const float SIC_Dry = 130000f, SIC_Prop = 2150000f, SIC_Isp = 280f;
    const float SII_Dry = 36200f, SII_Interstage = 4000f, SII_Prop = 443000f, SII_Isp = 421f;
    const float SIVB_Dry = 11000f, IU = 2000f, SIVB_Prop = 107500f, SIVB_Isp = 421f;
    const float Payload = 43900f, SLA = 1800f, LES = 4200f;
    public const float DvToOrbit = 9450f, DvToMoon = 3150f;
    const float G0 = 9.80665f;

    public static float DeltaV(float load) => DeltaV(load, Payload);
    public static float DeltaV(float load, float payload)
    {
        float p1 = SIC_Prop * load, p2 = SII_Prop * load, p3 = SIVB_Prop * load;
        float m0 = SIC_Dry + p1 + SII_Dry + SII_Interstage + p2 + SIVB_Dry + IU + p3 + payload + SLA + LES;
        float dv1 = SIC_Isp * G0 * Mathf.Log(m0 / (m0 - p1));
        float m1 = m0 - p1 - SIC_Dry - SII_Interstage - LES;
        float dv2 = SII_Isp * G0 * Mathf.Log(m1 / (m1 - p2));
        float m2 = m1 - p2 - SII_Dry;
        float dv3 = SIVB_Isp * G0 * Mathf.Log(m2 / (m2 - p3));
        return dv1 + dv2 + dv3;
    }

    // ---------------- Eagle's fuel: the trade-off Apollo planners weighed ----------------
    // The Saturn V is always full. What changes is how much propellant Eagle's descent stage carries: the descent from
    // 15 km to 150 m burns about 7,990 kg, and every kilogram more is about 0.27 s of hovering at the end (3.7 kg/s).
    // Every kilogram also rides on the Saturn V, eating the spare speed it has after sending you to the Moon.
    public const float EagleFuelMin = 7900f, EagleFuelSpan = 1000f, DescentKg = 7990f, HoverKgPerS = 3.7f, Apollo11EagleKg = 8250f;
    public static float EagleFuelKg(float v) => EagleFuelMin + EagleFuelSpan * v;
    public static float HoverSeconds(float v) => (EagleFuelKg(v) - DescentKg) / HoverKgPerS;
    public static float SaturnSpare(float v) => DeltaV(1f, Payload - Apollo11EagleKg + EagleFuelKg(v)) - (DvToOrbit + DvToMoon);

    public static Check Rocket(out float marginMs)
    {
        marginMs = SaturnSpare(MissionState.Fuel);
        float hover = HoverSeconds(MissionState.Fuel);
        if (hover < 30f) return Check.No($"Eagle's fuel would leave {Mathf.Max(hover, 0f):0} s to hover at the landing. The rules want at least 30 s to pick a spot.");
        if (marginMs < 0) return Check.No("Eagle is too heavy: the Saturn V can't send this much to the Moon.");
        return Check.Ok();
    }

    // ---------------- life support: CSM cryogenics ----------------
    const float O2Kg = 290f, H2Kg = 25.4f, PowerKw = 2.0f, CrewO2PerDay = 0.84f * 3f;
    public const float TripDays = 8.14f, SpareDays = 1f;

    public static float SuppliesDays(float load)
    {
        float kwhDay = PowerKw * 24f;
        float o2PerDay = 0.36f * kwhDay + CrewO2PerDay, h2PerDay = 0.045f * kwhDay;
        return Mathf.Min(O2Kg * load / o2PerDay, H2Kg * load / h2PerDay);
    }

    public static Check LifeSupport(out float days)
    {
        days = SuppliesDays(MissionState.Supplies);
        if (days < TripDays + SpareDays) return Check.No($"Air and power would last {days:0.0} days. The trip needs 8, plus 1 spare.");
        return Check.Ok();
    }

    // ---------------- names on the Moon (IAU feature centres, approximate radii) ----------------
    static readonly (string name, double lat, double lon, double r)[] Places =
    {
        ("APOLLO 11 SITE", 0.674, 23.473, 1.2), ("APOLLO 12 SITE", -3.012, -23.422, 1.2), ("APOLLO 14 SITE", -3.645, -17.471, 1.2),
        ("APOLLO 15 SITE", 26.132, 3.634, 1.2), ("APOLLO 16 SITE", -8.973, 15.500, 1.2), ("APOLLO 17 SITE", 20.191, 30.772, 1.2),
        ("COPERNICUS", 9.62, -20.08, 1.6), ("TYCHO", -43.31, -11.36, 1.5), ("KEPLER", 8.12, -38.01, 0.6), ("PLATO", 51.62, -9.38, 1.7),
        ("ARISTARCHUS", 23.73, -47.49, 0.7), ("SHACKLETON", -89.67, 129.78, 0.6), ("TSIOLKOVSKIY", -21.2, 128.97, 3.0),
        ("MARE TRANQUILLITATIS", 8.35, 30.83, 13), ("MARE SERENITATIS", 27.3, 18.4, 11), ("MARE IMBRIUM", 32.8, -15.6, 19),
        ("MARE CRISIUM", 16.2, 59.1, 9), ("MARE FECUNDITATIS", -7.8, 51.3, 13), ("MARE NECTARIS", -15.2, 35.5, 5.5),
        ("MARE NUBIUM", -21.3, -16.6, 11), ("MARE HUMORUM", -24.4, -38.6, 6.4), ("MARE FRIGORIS", 56.0, 1.4, 8),
        ("MARE VAPORUM", 13.3, 3.6, 4), ("SINUS MEDII", 2.4, 1.7, 5), ("MARE COGNITUM", -10.0, -23.1, 6),
        ("MARE INSULARUM", 7.5, -30.9, 8), ("MARE ORIENTALE", -19.4, -92.8, 5), ("MARE MOSCOVIENSE", 27.3, 147.9, 4.5),
        ("OCEANUS PROCELLARUM", 18.4, -57.4, 25),
    };

    static double AngleDeg(double la1, double lo1, double la2, double lo2)
    {
        double a = Math.PI / 180;
        double c = Math.Sin(la1 * a) * Math.Sin(la2 * a) + Math.Cos(la1 * a) * Math.Cos(la2 * a) * Math.Cos((lo1 - lo2) * a);
        return Math.Acos(Math.Max(-1, Math.Min(1, c))) / a;
    }

    public static string PlaceName(double lat, double lon)
    {
        string best = null; double bestScore = double.MaxValue;
        foreach (var p in Places)
        {
            double d = AngleDeg(lat, lon, p.lat, p.lon);
            if (d > p.r) continue;
            double score = d / p.r;
            if (score < bestScore) { bestScore = score; best = p.name; }
        }
        if (best != null) return best;
        if (lat < -80) return "SOUTH POLE REGION";
        if (lat > 80) return "NORTH POLE REGION";
        return Math.Abs(lon) > 90 ? "FAR SIDE HIGHLANDS" : "HIGHLANDS";
    }
}
