// Where the bright planets are: JPL's "Keplerian Elements for Approximate Positions of the Major Planets"
// (E. M. Standish, valid 1800-2050, J2000 ecliptic), solved for a date and turned into geocentric
// equatorial unit vectors (x toward RA 0h, y toward RA 6h, z toward the north celestial pole).
using System;
using UnityEngine;

public static class Planets
{
    // a (AU), e, I, L, long. perihelion, long. node (deg) and their rates per Julian century
    static readonly double[,] El =
    {
        { 0.38709927, 0.20563593, 7.00497902, 252.25032350, 77.45779628, 48.33076593,  0.00000037, 0.00001906, -0.00594749, 149472.67411175, 0.16047689, -0.12534081 },  // Mercury
        { 0.72333566, 0.00677672, 3.39467605, 181.97909950, 131.60246718, 76.67984255,  0.00000390, -0.00004107, -0.00078890, 58517.81538729, 0.00268329, -0.27769418 },  // Venus
        { 1.00000261, 0.01671123, -0.00001531, 100.46457166, 102.93768193, 0.0,         0.00000562, -0.00004392, -0.01294668, 35999.37244981, 0.32327364, 0.0 },         // Earth-Moon barycentre
        { 1.52371034, 0.09339410, 1.84969142, -4.55343205, -23.94362959, 49.55953891,   0.00001847, 0.00007882, -0.00813131, 19140.30268499, 0.44441088, -0.29257343 },  // Mars
        { 5.20288700, 0.04838624, 1.30439695, 34.39644051, 14.72847983, 100.47390909,   -0.00011607, -0.00013253, -0.00183714, 3034.74612775, 0.21252668, 0.20469106 },  // Jupiter
        { 9.53667594, 0.05386179, 2.48599187, 49.95424423, 92.59887831, 113.66242448,   -0.00125060, -0.00050991, 0.00193609, 1222.49362201, -0.41897216, -0.28867794 }, // Saturn
    };
    public static readonly string[] Names = { "Mercury", "Venus", "Mars", "Jupiter", "Saturn" };
    static readonly int[] Rows = { 0, 1, 3, 4, 5 };

    static double[] Helio(int row, double T)
    {
        double D = Math.PI / 180;
        double a = El[row, 0] + El[row, 6] * T, e = El[row, 1] + El[row, 7] * T, I = (El[row, 2] + El[row, 8] * T) * D;
        double L = El[row, 3] + El[row, 9] * T, wbar = El[row, 4] + El[row, 10] * T, node = (El[row, 5] + El[row, 11] * T) * D;
        double w = wbar * D - node, M = ((L - wbar) % 360 + 540) % 360 - 180;
        double Mr = M * D, E = Mr + e * Math.Sin(Mr);
        for (int k = 0; k < 8; k++) E -= (E - e * Math.Sin(E) - Mr) / (1 - e * Math.Cos(E));
        double x = a * (Math.Cos(E) - e), y = a * Math.Sqrt(1 - e * e) * Math.Sin(E);
        double cw = Math.Cos(w), sw = Math.Sin(w), cn = Math.Cos(node), sn = Math.Sin(node), ci = Math.Cos(I), si = Math.Sin(I);
        double xe = (cw * cn - sw * sn * ci) * x + (-sw * cn - cw * sn * ci) * y;
        double ye = (cw * sn + sw * cn * ci) * x + (-sw * sn + cw * cn * ci) * y;
        double ze = sw * si * x + cw * si * y;
        double eps = 23.43928 * D;
        return new[] { xe, Math.Cos(eps) * ye - Math.Sin(eps) * ze, Math.Sin(eps) * ye + Math.Cos(eps) * ze };
    }

    // geocentric equatorial direction (unit) and distance (AU) of planet k (0 Mercury .. 4 Saturn)
    public static Vector3d Geocentric(int k, DateTime utc, out double distAu)
    {
        double T = (Ephemeris.JulianDate(utc) - 2451545.0) / 36525.0;
        var p = Helio(Rows[k], T); var e = Helio(2, T);
        double x = p[0] - e[0], y = p[1] - e[1], z = p[2] - e[2];
        distAu = Math.Sqrt(x * x + y * y + z * z);
        return new Vector3d(x / distAu, y / distAu, z / distAu);
    }
}
