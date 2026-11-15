// The launch the student is planning, all chosen by hand. Survives scene loads.
using System;

public static class MissionState
{
    public static DateTime LaunchUtc = new DateTime(2026, 12, 1, 14, 0, 0, DateTimeKind.Utc);
    public static bool HasSite;
    public static double SiteLat, SiteLon;     // selenographic, degrees, east positive
    public static float Fuel = 0.35f;          // Eagle's descent propellant: 7,900 kg (0) to 8,900 kg (1); Apollo 11 carried about 8,250 kg (0.35)
    public static bool Hybrid;                 // the route: free return (Apollo 11) or hybrid (Apollo 12 on)
    public static float Supplies = 1f;         // CSM oxygen and hydrogen load 0..1
    public static bool Initialised;

    public static event Action Changed;
    public static void NotifyChanged() => Changed?.Invoke();

    public static void SetDay(DateTime date)
    {
        var day = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Utc);
        LaunchUtc = MissionModel.WindowOpenUtc(day);
        NotifyChanged();
    }

    public static void SetSite(double lat, double lon)
    {
        HasSite = true; SiteLat = lat; SiteLon = lon;
        NotifyChanged();
    }
}
