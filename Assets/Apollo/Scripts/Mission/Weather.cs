// Real weather at Kennedy Space Center, from NASA's POWER project (daily records 1981-2025, Resources/Weather/ksc_daily).
// Nobody knows the weather of a future launch day yet, so a launch date gets the real weather of the same calendar day in
// one of the past years (picked again every time the game starts, so runs differ): a real day, its clouds, rain and wind.
// What it changes:
//   - the weather officer's GO / NO-GO: no launch near a thunderstorm, none in ground winds over about 35 knots
//   - the pad: how much sky the clouds cover, how low they are, the wind that moves them, rain, how dim the light is
//   - the climb: launching into rain clouds is what got Apollo 12 struck by lightning (Lightning.cs); a clear day can't
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

public static class Weather
{
    public struct Day
    {
        public DateTime Real;               // the real day the weather comes from
        public float TMin, TMax, Rain, Wind, Wind50, Cloud, Humidity;   // C, C, mm, m/s (daily max at 10 m and 50 m), %, %
        public bool Valid;

        public bool Storm => Rain >= 12f && TMax >= 26f || Rain >= 30f;           // a summer downpour: thunderstorms
        public bool Rainy => Rain >= 2f;
        public bool RainClouds => Cloud >= 70f && Rainy;                          // deep, wet clouds over the pad
        public float Gust => Wind * 1.4f;                                         // the records are hourly means; gusts run about 40% higher
        public string Sky => Cloud < 20f ? "CLEAR" : Cloud < 50f ? "SCATTERED CLOUD" : Cloud < 80f ? "BROKEN CLOUD" : "OVERCAST";
        public string Short => $"{(Storm ? "THUNDERSTORMS" : Rainy ? "RAIN" : Sky)}  ·  {Cloud:0}% CLOUD  ·  WIND {Wind:0} M/S  ·  {TMax:0}°C";
    }

    static List<Day> days;
    static readonly int session = Environment.TickCount;

    static void Load()
    {
        if (days != null) return;
        days = new List<Day>();
        var ta = Resources.Load<TextAsset>("Weather/ksc_daily");
        if (ta == null) return;
        foreach (var line in ta.text.Split('\n'))
        {
            if (line.Length < 10 || line[0] == '#') continue;
            var f = line.Trim().Split(',');
            if (f.Length < 8) continue;
            float P(int i) => float.Parse(f[i], CultureInfo.InvariantCulture);
            var d = new Day
            {
                Real = DateTime.ParseExact(f[0], "yyyyMMdd", CultureInfo.InvariantCulture),
                TMin = P(1), TMax = P(2), Rain = P(3), Wind = P(4), Wind50 = P(5), Cloud = P(6), Humidity = P(7)
            };
            d.Valid = d.Cloud >= 0f && d.Rain >= 0f && d.Wind >= 0f;
            if (d.Valid) days.Add(d);
        }
        Debug.Log($"WEATHER {days.Count} real days from NASA POWER");
    }

    // the weather for a launch date: the same calendar day in one past year, the year picked per date and per run
    public static Day For(DateTime launch)
    {
        Load();
        var args = Environment.GetCommandLineArgs();                              // tests: "-wday yyyyMMdd" picks a real day
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "-wday" && DateTime.TryParseExact(args[i + 1], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var forced))
            { int k = days.FindIndex(x => x.Real == forced); if (k >= 0) return days[k]; }
        var same = days.FindAll(d => d.Real.Month == launch.Month && d.Real.Day == launch.Day);
        if (same.Count == 0) same = days.FindAll(d => d.Real.Month == launch.Month);
        if (same.Count == 0) return new Day { Real = launch, TMin = 22, TMax = 30, Cloud = 10, Wind = 5, Wind50 = 7, Humidity = 70, Valid = false };
        uint h = (uint)(launch.Year * 372 + launch.Month * 31 + launch.Day) * 2654435761u ^ (uint)session;
        return same[(int)(h % (uint)same.Count)];
    }

    // the weather officer's call (launch rules: none within 10 nautical miles of a thunderstorm; ground winds)
    public static MissionModel.Check Check(Day d)
    {
        if (d.Storm) return MissionModel.Check.No($"Thunderstorms over the Cape ({d.Rain:0} mm of rain that day). No launch within 10 nautical miles of a thunderstorm.");
        if (d.Gust > 18f) return MissionModel.Check.No($"Ground wind gusts near {d.Gust:0} m/s. Over about 35 knots (18 m/s) the Saturn V could swing into the tower at liftoff.");
        return MissionModel.Check.Ok();
    }

    // shown under the date in the planner
    public static string Line(DateTime launch)
    {
        var d = For(launch);
        string warn = d.Storm ? "" : d.RainClouds ? "  <color=#9aa2aa>· RAIN CLOUDS: LIGHTNING RISK (APOLLO 12)</color>" : "";
        return d.Short + warn + "\n<color=#5c636b>NASA RECORD FOR KENNEDY, " + d.Real.ToString("dd MMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant() + "</color>";
    }
}
