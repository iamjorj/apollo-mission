// Where the Moon and Sun are, from NASA JPL Horizons tables bundled in Resources/Ephemeris
// (geocentric, ICRF equatorial, km): Nov 2026 - Dec 2028 and July 1969.
// World frame used in the scenes: +X = vernal equinox (RA 0h), +Y = north celestial pole,
// +Z = RA 6h. 1 world unit = 1000 km.
using System;
using System.Collections.Generic;
using UnityEngine;

public static class Ephemeris
{
    public const double KmPerUnit = 1000.0;

    class Table
    {
        public double[] jd, x, y, z;
        public bool Has(double t) => jd.Length > 1 && t >= jd[0] && t <= jd[jd.Length - 1];
        public Vector3d At(double t)
        {
            int i = Array.BinarySearch(jd, t);
            if (i < 0) i = ~i - 1;
            i = Mathf.Clamp(i, 0, jd.Length - 2);
            double f = (t - jd[i]) / (jd[i + 1] - jd[i]);
            return new Vector3d(x[i] + (x[i + 1] - x[i]) * f, y[i] + (y[i + 1] - y[i]) * f, z[i] + (z[i + 1] - z[i]) * f);
        }
    }

    static Table[] moon, sun;

    static Table Load(string name)
    {
        var asset = Resources.Load<TextAsset>("Ephemeris/" + name);
        var jd = new List<double>(); var x = new List<double>(); var y = new List<double>(); var z = new List<double>();
        foreach (var line in asset.text.Split('\n'))
        {
            if (line.Length < 5 || line[0] == '#') continue;
            var p = line.Split(',');
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            jd.Add(double.Parse(p[0], ci)); x.Add(double.Parse(p[1], ci)); y.Add(double.Parse(p[2], ci)); z.Add(double.Parse(p[3], ci));
        }
        return new Table { jd = jd.ToArray(), x = x.ToArray(), y = y.ToArray(), z = z.ToArray() };
    }

    static void Ensure()
    {
        if (moon != null) return;
        moon = new[] { Load("moon_2026_2028"), Load("moon_1969_07") };
        sun = new[] { Load("sun_2026_2028"), Load("sun_1969_07") };
    }

    public static double JulianDate(DateTime utc) => utc.ToOADate() + 2415018.5;

    public static bool Covers(DateTime utc)
    {
        Ensure();
        double t = JulianDate(utc);
        foreach (var m in moon) if (m.Has(t)) return true;
        return false;
    }

    static Vector3d Lookup(Table[] tables, DateTime utc)
    {
        Ensure();
        double t = JulianDate(utc);
        foreach (var tb in tables) if (tb.Has(t)) return tb.At(t);
        // outside the tables: nearest table edge (callers keep dates inside the range)
        return tables[0].At(Math.Min(Math.Max(t, tables[0].jd[0]), tables[0].jd[tables[0].jd.Length - 1]));
    }

    public static Vector3d MoonKm(DateTime utc) { Ensure(); return Lookup(moon, utc); }
    public static Vector3d SunKm(DateTime utc) { Ensure(); return Lookup(sun, utc); }

    // ICRF (x, y, z) km -> world units (x, z, y): swapping two axes also converts right- to left-handed.
    public static Vector3 ToWorld(Vector3d km) => new Vector3((float)(km.x / KmPerUnit), (float)(km.z / KmPerUnit), (float)(km.y / KmPerUnit));
    public static Vector3 ToWorldDir(Vector3d km) => new Vector3((float)km.x, (float)km.z, (float)km.y).normalized;

    public static Vector3 MoonWorld(DateTime utc) => ToWorld(MoonKm(utc));
    public static Vector3 SunDirWorld(DateTime utc) => ToWorldDir(SunKm(utc));

    // Greenwich mean sidereal time, degrees (IAU 1982, good to a fraction of a second here).
    public static double GmstDeg(DateTime utc)
    {
        double d = JulianDate(utc) - 2451545.0;
        double g = (280.46061837 + 360.98564736629 * d) % 360.0;
        return g < 0 ? g + 360.0 : g;
    }

    // Earth's rotation for the scenes: Greenwich meridian at right ascension = GMST.
    public static Quaternion EarthRotation(DateTime utc) => Quaternion.Euler(0f, -(float)GmstDeg(utc), 0f);

    // Body-fixed point (lat, lon in degrees) on a unit sphere, east = +Z when seen from +X with north up.
    public static Vector3 BodyPoint(double latDeg, double lonDeg)
    {
        double la = latDeg * Math.PI / 180.0, lo = lonDeg * Math.PI / 180.0;
        return new Vector3((float)(Math.Cos(la) * Math.Cos(lo)), (float)Math.Sin(la), (float)(Math.Cos(la) * Math.Sin(lo)));
    }

    // The Moon's orientation from the IAU WGCCRE 2009 rotation model (pole right ascension and
    // declination plus prime-meridian angle W, with the periodic terms that produce libration).
    // Body frame: prime meridian (longitude 0, the mean near-side centre), east longitudes, north pole.
    static double Sin(double deg) => Math.Sin(deg * Math.PI / 180.0);
    static double Cos(double deg) => Math.Cos(deg * Math.PI / 180.0);

    public static void MoonAxes(DateTime utc, out Vector3 meridian, out Vector3 east, out Vector3 pole)
    {
        double d = JulianDate(utc) - 2451545.0, T = d / 36525.0;
        double E1 = 125.045 - 0.0529921 * d, E2 = 250.089 - 0.1059842 * d, E3 = 260.008 + 13.0120009 * d;
        double E4 = 176.625 + 13.3407154 * d, E5 = 357.529 + 0.9856003 * d, E6 = 311.589 + 26.4057084 * d;
        double E7 = 134.963 + 13.0649930 * d, E8 = 276.617 + 0.3287146 * d, E9 = 34.226 + 1.7484877 * d;
        double E10 = 15.134 - 0.1589763 * d, E11 = 119.743 + 0.0036096 * d, E12 = 239.961 + 0.1643573 * d;
        double E13 = 25.053 + 12.9590088 * d;
        double a0 = 269.9949 + 0.0031 * T - 3.8787 * Sin(E1) - 0.1204 * Sin(E2) + 0.0700 * Sin(E3) - 0.0172 * Sin(E4)
                  + 0.0072 * Sin(E6) - 0.0052 * Sin(E10) + 0.0043 * Sin(E13);
        double d0 = 66.5392 + 0.0130 * T + 1.5419 * Cos(E1) + 0.0239 * Cos(E2) - 0.0278 * Cos(E3) + 0.0068 * Cos(E4)
                  - 0.0029 * Cos(E6) + 0.0009 * Cos(E7) + 0.0008 * Cos(E10) - 0.0009 * Cos(E13);
        double W = 38.3213 + 13.17635815 * d - 1.4e-12 * d * d + 3.5610 * Sin(E1) + 0.1208 * Sin(E2) - 0.0642 * Sin(E3)
                 + 0.0158 * Sin(E4) + 0.0252 * Sin(E5) - 0.0066 * Sin(E6) - 0.0047 * Sin(E7) - 0.0046 * Sin(E8)
                 + 0.0028 * Sin(E9) + 0.0052 * Sin(E10) + 0.0040 * Sin(E11) + 0.0019 * Sin(E12) - 0.0044 * Sin(E13);
        // ICRF vectors
        var p = new Vector3d(Cos(d0) * Cos(a0), Cos(d0) * Sin(a0), Sin(d0));
        var q = new Vector3d(-Sin(a0), Cos(a0), 0);                    // ascending node of the lunar equator
        var pq = new Vector3d(p.y * q.z - p.z * q.y, p.z * q.x - p.x * q.z, p.x * q.y - p.y * q.x);
        var m = new Vector3d(q.x * Cos(W) + pq.x * Sin(W), q.y * Cos(W) + pq.y * Sin(W), q.z * Cos(W) + pq.z * Sin(W));
        var e = new Vector3d(p.y * m.z - p.z * m.y, p.z * m.x - p.x * m.z, p.x * m.y - p.y * m.x);
        meridian = ToWorldDir(m); east = ToWorldDir(e); pole = ToWorldDir(p);
    }

    // Rotation mapping the sphere mesh's body frame (BodyPoint) to the world.
    public static Quaternion MoonRotation(DateTime utc)
    {
        MoonAxes(utc, out _, out var east, out var pole);
        return Quaternion.LookRotation(east, pole);
    }

    // Height of the Earth above the horizon at a lunar site, degrees (negative = far side, no radio).
    public static double EarthElevationOnMoon(DateTime utc, double latDeg, double lonDeg)
    {
        MoonAxes(utc, out var m, out var e, out var p);
        Vector3 b = BodyPoint(latDeg, lonDeg);
        Vector3 n = m * b.x + p * b.y + e * b.z;
        Vector3 toEarth = (-MoonWorld(utc)).normalized;
        return Math.Asin(Mathf.Clamp(Vector3.Dot(n, toEarth), -1f, 1f)) * 180.0 / Math.PI;
    }

    // Selenographic latitude and longitude (degrees) of a world point on the Moon at a time.
    public static void MoonLatLon(DateTime utc, Vector3 worldPoint, out double lat, out double lon)
    {
        MoonAxes(utc, out var m, out var e, out var p);
        Vector3 d = (worldPoint - MoonWorld(utc)).normalized;
        lat = Math.Asin(Mathf.Clamp(Vector3.Dot(d, p), -1f, 1f)) * 180.0 / Math.PI;
        lon = Math.Atan2(Vector3.Dot(d, e), Vector3.Dot(d, m)) * 180.0 / Math.PI;
    }

    // Height of the Sun above the horizon at a lunar site, degrees.
    public static double SunElevationOnMoon(DateTime utc, double latDeg, double lonDeg)
    {
        MoonAxes(utc, out var m, out var e, out var p);
        Vector3 b = BodyPoint(latDeg, lonDeg);
        Vector3 n = m * b.x + p * b.y + e * b.z;
        Vector3d s = SunKm(utc) - MoonKm(utc);
        Vector3 sd = ToWorldDir(s);
        return Math.Asin(Mathf.Clamp(Vector3.Dot(n, sd), -1f, 1f)) * 180.0 / Math.PI;
    }
}

public struct Vector3d
{
    public double x, y, z;
    public Vector3d(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
    public static Vector3d operator -(Vector3d a, Vector3d b) => new Vector3d(a.x - b.x, a.y - b.y, a.z - b.z);
    public double Magnitude => Math.Sqrt(x * x + y * y + z * z);
}
