// The planner interface, in monospace: a PLAN LAUNCH button with a chevron at the top centre that
// opens a four-line mission card (date, landing site, propellant, life support, go / no-go),
// a view switcher, a few labels in the scene, and the landing-site picker on the Moon itself:
// hover the Moon to read coordinates, place name and sunlight there; click to set the site.
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class PlannerUI : MonoBehaviour
{
    public OrbitCam Cam;
    public static PlannerUI Instance;

    RectTransform root, card, overlay, chevron, readout, reticle;
    CanvasGroup cardGroup;
    TextMeshProUGUI monthTitle, bigDate, bigTime, siteCoords, siteName, readoutText, clockText;
    RectTransform calendarGrid;
    readonly Button[] viewButtons = new Button[3];
    readonly List<Action> refreshers = new List<Action>();
    readonly List<(RectTransform rt, Func<Vector3> world, Func<bool> visible, float bodyR, bool surface)> labels = new List<(RectTransform, Func<Vector3>, Func<bool>, float, bool)>();
    Image fade;
    bool open, forcedReadout;
    DateTime viewMonth, lastLaunch;
    Vector2 downPos; float downTime; bool downOnMoon;

    // the two suggestions: next GO day for the chosen site, best site for the chosen date
    public DateTime? NextGoDay { get; private set; }
    public bool HasSuggestedSite { get; private set; }
    public double SuggestedLat { get; private set; }
    public double SuggestedLon { get; private set; }
    (bool, double, double) dayKey = (false, double.NaN, double.NaN);
    DateTime siteKey;
    RectTransform suggestRing;

    static readonly DateTime DataEnd = new DateTime(2028, 12, 26, 0, 0, 0, DateTimeKind.Utc);

    public static DateTime Today
    {
        get
        {
            var t = DateTime.UtcNow;
            var d = new DateTime(t.Year, t.Month, t.Day, 0, 0, 0, DateTimeKind.Utc);
            var start = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
            if (d < start) d = start;
            if (d > DataEnd) d = DataEnd;
            return d;
        }
    }

    const float W = 470f, Pad = 30f;

    void Awake()
    {
        Instance = this;
        if (!MissionState.Initialised)
        {
            MissionState.Initialised = true;
            MissionState.LaunchUtc = MissionModel.WindowOpenUtc(Today);
        }
        viewMonth = FirstOfMonth(MissionState.LaunchUtc);
    }

    void Start()
    {
        BuildCanvas();
        MissionState.Changed += OnChanged;
        OnChanged();
    }

    void OnDestroy() => MissionState.Changed -= OnChanged;

    void OnChanged()
    {
        SpaceScene.Instance?.Refresh();
        if (MissionState.LaunchUtc != lastLaunch)
        {
            if (Cam != null && lastLaunch != default) Cam.Go(Cam.Current);
            viewMonth = FirstOfMonth(MissionState.LaunchUtc);   // the calendar opens on the chosen date's month
        }
        lastLaunch = MissionState.LaunchUtc;
        UpdateSuggestions();
        foreach (var r in refreshers) r();
        BuildCalendar();
    }

    // Recomputed only when what they depend on changes (the gauges don't affect either).
    void UpdateSuggestions()
    {
        var key = (MissionState.HasSite, MissionState.SiteLat, MissionState.SiteLon);
        if (!key.Equals(dayKey))
        {
            dayKey = key;
            NextGoDay = MissionState.HasSite ? MissionModel.NextGoDay(Today, DataEnd, MissionState.SiteLat, MissionState.SiteLon) : null;
        }
        if (MissionState.LaunchUtc != siteKey)
        {
            siteKey = MissionState.LaunchUtc;
            HasSuggestedSite = MissionModel.BestSite(siteKey, out double la, out double lo);
            SuggestedLat = la; SuggestedLon = lo;
        }
    }

    public void TakeSuggestedSite()
    {
        if (!HasSuggestedSite) return;
        MissionState.SetSite(SuggestedLat, SuggestedLon);
        SpaceScene.Instance.DropPin();
    }

    public void TakeNextGoDay()
    {
        if (NextGoDay.HasValue) MissionState.SetDay(NextGoDay.Value);
    }

    Vector3 SuggestedWorld()
    {
        var sc = SpaceScene.Instance;
        return sc.Moon.position + sc.Moon.rotation * Ephemeris.BodyPoint(SuggestedLat, SuggestedLon) * SpaceScene.MoonRadius;
    }

    bool SuggestionIsSite() => MissionState.HasSite &&
        Math.Abs(MissionState.SiteLat - SuggestedLat) < 0.6 && Math.Abs(MissionState.SiteLon - SuggestedLon) < 0.6;

    bool NearSuggestion(Vector2 screen)
    {
        if (!HasSuggestedSite || suggestRing == null || !suggestRing.gameObject.activeInHierarchy) return false;
        Vector3 s = Cam.GetComponent<Camera>().WorldToScreenPoint(SuggestedWorld());
        return Vector2.Distance(s, screen) < 18f * root.GetComponent<Canvas>().scaleFactor;
    }

    // A quiet clickable line: dim label, value, chevron.
    RectTransform SuggestRow(ref float y, Action onClick, out TextMeshProUGUI text)
    {
        var row = UiKit.Rect(card, "Suggestion", new Vector2(0, 1), new Vector2(0, 1), new Vector2(Pad - 10, -y), new Vector2(W - 2 * Pad + 20, 30));
        UiKit.Button(row, new Color(0, 0, 0, 0), 8, onClick);
        text = UiKit.Label(row, "", 12, UiKit.Text, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(10, 0), new Vector2(W - 2 * Pad - 14, 24), spacing: 2);
        text.enableWordWrapping = false;
        UiKit.Chevron(row, new Vector2(1, 0.5f), new Vector2(-14, 0), 10, UiKit.Text2).localRotation = Quaternion.Euler(0, 0, 90);
        y += 30;
        return row;
    }

    static DateTime FirstOfMonth(DateTime d) => new DateTime(d.Year, d.Month, 1, 0, 0, 0, DateTimeKind.Utc);

    // ---------------- frame ----------------

    void BuildCanvas()
    {
        var go = new GameObject("PlannerCanvas", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 1f;
        go.AddComponent<GraphicRaycaster>();
        root = (RectTransform)go.transform;

        var layer = UiKit.Rect(root, "SceneLabels", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
        layer.anchorMin = Vector2.zero; layer.anchorMax = Vector2.one; layer.sizeDelta = Vector2.zero;
        BuildSceneLabels(layer);

        // top left: mission and clock
        UiKit.Label(root, "APOLLO 11", 15, UiKit.Text, new Vector2(0, 1), new Vector2(0, 1), new Vector2(36, -30), new Vector2(200, 24), bold: true, spacing: 6);
        clockText = UiKit.Label(root, "", 12, UiKit.Text3, new Vector2(0, 1), new Vector2(0, 1), new Vector2(36, -56), new Vector2(600, 22), spacing: 2);
        refreshers.Add(() =>
        {
            var l = MissionState.LaunchUtc;
            clockText.text = $"T-0 {l:dd MMM yyyy HH:mm}Z   LANDING {MissionModel.LandingUtc(l):dd MMM HH:mm}Z".ToUpperInvariant();
        });
        // the weather at the pad that day: a real NASA record (Weather.cs)
        var weatherText = UiKit.Label(root, "", 12, UiKit.Text, new Vector2(0, 1), new Vector2(0, 1), new Vector2(36, -84), new Vector2(900, 40), TextAlignmentOptions.TopLeft, spacing: 2);
        weatherText.lineSpacing = 10;
        UiKit.Shadow(weatherText);
        refreshers.Add(() =>
        {
            weatherText.text = "<color=#5c636b>WEATHER AT THE PAD</color>  " + Weather.Line(MissionState.LaunchUtc);
            var wd = Weather.For(MissionState.LaunchUtc);
            Shader.SetGlobalVector("_KscWeather", new Vector4(wd.Cloud / 100f, Mathf.Clamp01(wd.Rain / 30f), 0f, 1f));   // the globe's clouds over Florida
        });
        UiKit.Label(root, "EARTH AND MOON TO SCALE  ·  NASA JPL HORIZONS", 11, UiKit.Text3, new Vector2(0, 0), new Vector2(0, 0), new Vector2(36, 26), new Vector2(700, 20), spacing: 3);

        // top centre: PLAN LAUNCH + chevron
        var pill = UiKit.Rect(root, "PlanLaunch", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -24), new Vector2(250, 48));
        UiKit.Button(pill, UiKit.Panel, 24, Toggle);
        UiKit.Label(pill, "PLAN LAUNCH", 15, UiKit.Text, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(28, 0), new Vector2(170, 30), bold: true, spacing: 5);
        chevron = UiKit.Chevron(pill, new Vector2(1, 0.5f), new Vector2(-32, 0), 16, UiKit.Text);

        // top right: views
        string[] names = { "EARTH", "TRIP", "MOON" };
        var seg = UiKit.Rect(root, "Views", new Vector2(1, 1), new Vector2(1, 1), new Vector2(-36, -24), new Vector2(300, 48));
        UiKit.Box(seg, UiKit.Panel, 24);
        for (int i = 0; i < 3; i++)
        {
            int k = i;
            var b = UiKit.Rect(seg, names[i], new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(6 + i * 96, 0), new Vector2(96, 36));
            viewButtons[i] = UiKit.Button(b, new Color(0, 0, 0, 0), 18, () => SetView((OrbitCam.View)k));
            UiKit.Label(b, names[i], 13, UiKit.Text, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(96, 30), TextAlignmentOptions.Center, spacing: 3);
        }
        UpdateViewButtons();

        BuildCard();
        card.gameObject.SetActive(false);

        // the readout that follows the cursor over the Moon
        // a thin crosshair on the Moon under the cursor, and what that spot is like at touchdown
        reticle = UiKit.Rect(root, "Reticle", Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(34, 34));
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
            {
                UiKit.Box(UiKit.Rect(reticle, "H", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(sx * 13f, sy * 17f), new Vector2(8, 1.5f)), UiKit.Text, 0);
                UiKit.Box(UiKit.Rect(reticle, "V", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(sx * 17f, sy * 13f), new Vector2(1.5f, 8)), UiKit.Text, 0);
            }
        UiKit.Dot(reticle, new Vector2(0.5f, 0.5f), Vector2.zero, 3, UiKit.Text);
        reticle.gameObject.SetActive(false);

        readout = UiKit.Rect(root, "MoonReadout", Vector2.zero, new Vector2(0, 1), Vector2.zero, new Vector2(320, 80));
        UiKit.Box(readout, UiKit.Card, 10);
        readoutText = UiKit.Label(readout, "", 13, UiKit.Text, new Vector2(0, 1), new Vector2(0, 1), new Vector2(14, -11), new Vector2(300, 62), TextAlignmentOptions.TopLeft, spacing: 1);
        readoutText.enableWordWrapping = false;
        readoutText.lineSpacing = 12;
        readout.gameObject.SetActive(false);

        overlay = UiKit.Rect(root, "Poll", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560, 300));
        UiKit.Box(overlay, UiKit.Card, 18);
        overlay.gameObject.SetActive(false);

        var f = UiKit.Rect(root, "Fade", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(5000, 5000));
        fade = UiKit.Box(f, new Color(0, 0, 0, 1), 0);
        StartCoroutine(Fade(0f, 1.2f));
    }

    void UpdateViewButtons()
    {
        for (int i = 0; i < 3; i++)
        {
            bool on = Cam != null && (int)Cam.Current == i;
            viewButtons[i].GetComponent<Image>().color = on ? new Color(1, 1, 1, 0.13f) : new Color(1, 1, 1, 0.001f);
        }
    }

    public void SetView(OrbitCam.View v) { Cam.Go(v); UpdateViewButtons(); }

    public void Toggle() => Open(!open);

    public void Open(bool on)
    {
        open = on;
        if (on) overlay.gameObject.SetActive(false);
        StopCoroutine(nameof(AnimateCard));
        StartCoroutine(nameof(AnimateCard), on);
        chevron.localRotation = Quaternion.Euler(0, 0, on ? 180f : 0f);
    }

    IEnumerator AnimateCard(bool on)
    {
        if (on) card.gameObject.SetActive(true);
        float a0 = cardGroup.alpha, a1 = on ? 1f : 0f;
        for (float t = 0; t < 1f; t += Time.unscaledDeltaTime / 0.16f)
        {
            float e = Mathf.SmoothStep(0, 1, t);
            cardGroup.alpha = Mathf.Lerp(a0, a1, e);
            card.anchoredPosition = new Vector2(0, -84 + (1 - cardGroup.alpha) * 10f);
            yield return null;
        }
        cardGroup.alpha = a1;
        card.anchoredPosition = new Vector2(0, -84);
        if (!on) card.gameObject.SetActive(false);
    }

    // ---------------- the mission card ----------------

    void BuildCard()
    {
        card = UiKit.Rect(root, "MissionCard", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -84), new Vector2(W, 900));
        UiKit.Box(card, UiKit.Card, 20);
        cardGroup = card.gameObject.AddComponent<CanvasGroup>();
        cardGroup.alpha = 0f;
        float y = 30;

        // 01 launch date
        Header("01", "LAUNCH DATE", ref y);
        bigDate = UiKit.Label(card, "", 30, UiKit.Text, new Vector2(0, 1), new Vector2(0, 1), new Vector2(Pad, -y), new Vector2(300, 40), bold: true, spacing: 2);
        bigTime = UiKit.Label(card, "", 13, UiKit.Text3, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-Pad, -y - 12), new Vector2(160, 22), TextAlignmentOptions.MidlineRight, spacing: 2);
        refreshers.Add(() =>
        {
            bigDate.text = MissionState.LaunchUtc.ToString("dd MMM yyyy").ToUpperInvariant();
            bigTime.text = MissionState.LaunchUtc.ToString("HH:mm") + " UTC";
        });
        y += 46;
        var dayRow = SuggestRow(ref y, TakeNextGoDay, out var dayText);
        refreshers.Add(() =>
        {
            dayRow.gameObject.SetActive(MissionState.HasSite);          // nothing until a site is chosen
            dayText.text = "<color=#5c636b>NEXT GO DAY</color>  " +
                (NextGoDay.HasValue ? NextGoDay.Value.ToString("dd MMM yyyy").ToUpperInvariant() : "NONE BEFORE 2029");
            dayRow.GetComponent<Button>().interactable = NextGoDay.HasValue;
        });
        y += 10;
        var nav = UiKit.Rect(card, "Month", new Vector2(0, 1), new Vector2(0, 1), new Vector2(Pad, -y), new Vector2(W - 2 * Pad, 30));
        var prev = UiKit.Rect(nav, "Prev", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(-8, 0), new Vector2(34, 30));
        UiKit.Button(prev, new Color(0, 0, 0, 0), 8, () => ShiftMonth(-1));
        UiKit.Chevron(prev, new Vector2(0.5f, 0.5f), Vector2.zero, 12, UiKit.Text2).localRotation = Quaternion.Euler(0, 0, -90);
        monthTitle = UiKit.Label(nav, "", 13, UiKit.Text2, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(240, 30), TextAlignmentOptions.Center, spacing: 4);
        var next = UiKit.Rect(nav, "Next", new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(8, 0), new Vector2(34, 30));
        UiKit.Button(next, new Color(0, 0, 0, 0), 8, () => ShiftMonth(1));
        UiKit.Chevron(next, new Vector2(0.5f, 0.5f), Vector2.zero, 12, UiKit.Text2).localRotation = Quaternion.Euler(0, 0, 90);
        y += 36;
        string[] wd = { "M", "T", "W", "T", "F", "S", "S" };
        float cw = (W - 2 * Pad) / 7f;
        for (int i = 0; i < 7; i++)
            UiKit.Label(card, wd[i], 11, UiKit.Text3, new Vector2(0, 1), new Vector2(0, 1), new Vector2(Pad + i * cw, -y), new Vector2(cw, 16), TextAlignmentOptions.Center);
        y += 22;
        calendarGrid = UiKit.Rect(card, "Days", new Vector2(0, 1), new Vector2(0, 1), new Vector2(Pad, -y), new Vector2(W - 2 * Pad, 6 * 36));
        y += 6 * 36 + 10;
        Divider(ref y);

        // 02 landing site
        Header("02", "LANDING SITE", ref y);
        siteCoords = UiKit.Label(card, "", 21, UiKit.Text, new Vector2(0, 1), new Vector2(0, 1), new Vector2(Pad, -y), new Vector2(300, 32), bold: true, spacing: 1);
        var pick = UiKit.Rect(card, "Pick", new Vector2(1, 1), new Vector2(1, 1), new Vector2(-Pad + 6, -y + 2), new Vector2(112, 34));
        UiKit.Button(pick, new Color(1, 1, 1, 0.08f), 17, PickOnMoon);
        UiKit.Label(pick, "MOON", 12, UiKit.Text, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(20, 0), new Vector2(70, 30), spacing: 3);
        UiKit.Chevron(pick, new Vector2(1, 0.5f), new Vector2(-22, 0), 11, UiKit.Text).localRotation = Quaternion.Euler(0, 0, 90);
        pick.gameObject.AddComponent<HoverInk>();
        y += 34;
        siteName = UiKit.Label(card, "", 12, UiKit.Text3, new Vector2(0, 1), new Vector2(0, 1), new Vector2(Pad, -y), new Vector2(W - 2 * Pad, 20), spacing: 3);
        refreshers.Add(() =>
        {
            if (MissionState.HasSite)
            {
                siteCoords.text = Coords(MissionState.SiteLat, MissionState.SiteLon);
                siteName.text = MissionModel.PlaceName(MissionState.SiteLat, MissionState.SiteLon);
            }
            else { siteCoords.text = "—  —  —"; siteName.text = "NOT SET"; }
        });
        y += 26;
        var siteRow = SuggestRow(ref y, TakeSuggestedSite, out var siteText);
        refreshers.Add(() =>
        {
            siteText.text = "<color=#5c636b>SUGGESTED</color>  " + (HasSuggestedSite
                ? $"{MissionModel.PlaceName(SuggestedLat, SuggestedLon)}  <color=#5c636b>{Coords(SuggestedLat, SuggestedLon)}</color>"
                : "NO SITE WORKS THIS DAY");
            siteRow.GetComponent<Button>().interactable = HasSuggestedSite;
        });
        y += 6;
        Divider(ref y);

        // 03 / 04 gauges
        Gauge("03", "EAGLE'S LANDING FUEL", ref y, () => MissionState.Fuel, v => MissionState.Fuel = v,
            () => $"{MissionModel.EagleFuelKg(MissionState.Fuel):#,0} KG",
            () =>
            {
                float h = MissionModel.HoverSeconds(MissionState.Fuel), s = MissionModel.SaturnSpare(MissionState.Fuel);
                return $"<color=#5c636b>HOVER AT THE LANDING</color>  {Mathf.Max(h, 0f):0} S{(h < 30f ? "  <color=#5c636b>(TOO LITTLE)</color>" : "")}" +
                       $"     <color=#5c636b>SATURN SPARE</color>  {s:+0;-0} M/S";
            });
        Gauge("04", "LIFE SUPPORT", ref y, () => MissionState.Supplies, v => MissionState.Supplies = v);

        // 05 route
        Header("05", "ROUTE TO THE MOON", ref y);
        float half = (W - 2 * Pad - 10) / 2f;
        RouteButton("FREE RETURN", false, Pad, y, half);
        RouteButton("HYBRID", true, Pad + half + 10, y, half);
        y += 46;
        var routeText = UiKit.Label(card, "", 11, UiKit.Text3, new Vector2(0, 1), new Vector2(0, 1), new Vector2(Pad, -y), new Vector2(W - 2 * Pad, 34), TextAlignmentOptions.TopLeft, spacing: 1);
        refreshers.Add(() => routeText.text = MissionState.Hybrid
            ? "ANY SITE. BUT AFTER THE COURSE CORRECTION ONLY THE ENGINE CAN BRING THE CREW HOME."
            : "IF THE ENGINE FAILS, THE MOON SWINGS YOU HOME (APOLLO 13). ONLY SITES NEAR THE MOON'S CENTRE.");
        y += 40;

        y += 6;
        var go = UiKit.Rect(card, "GoNoGo", new Vector2(0, 1), new Vector2(0, 1), new Vector2(Pad, -y), new Vector2(W - 2 * Pad, 52));
        UiKit.Button(go, UiKit.Selected, 12, () => StartCoroutine(Poll()));
        UiKit.Label(go, "RUN GO / NO-GO", 15, UiKit.Ink, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(320, 30), TextAlignmentOptions.Center, true, 5);
        y += 52 + Pad;
        card.sizeDelta = new Vector2(W, y);
    }

    static string Coords(double lat, double lon) =>
        $"{Math.Abs(lat):00.00}°{(lat >= 0 ? "N" : "S")}  {Math.Abs(lon):000.00}°{(lon >= 0 ? "E" : "W")}";

    void Header(string index, string title, ref float y)
    {
        UiKit.Label(card, index, 11, UiKit.Text3, new Vector2(0, 1), new Vector2(0, 1), new Vector2(Pad, -y), new Vector2(30, 18), spacing: 2);
        UiKit.Label(card, title, 11, UiKit.Text2, new Vector2(0, 1), new Vector2(0, 1), new Vector2(Pad + 34, -y), new Vector2(300, 18), spacing: 5);
        y += 30;
    }

    void Divider(ref float y)
    {
        var d = UiKit.Rect(card, "Divider", new Vector2(0, 1), new Vector2(0, 1), new Vector2(Pad, -y), new Vector2(W - 2 * Pad, 1));
        UiKit.Box(d, UiKit.Line, 0);
        y += 24;
    }

    // one of the two routes: white when chosen
    void RouteButton(string label, bool hybrid, float x, float y, float w)
    {
        var rt = UiKit.Rect(card, "Route_" + label, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -y), new Vector2(w, 38));
        UiKit.Button(rt, new Color(1, 1, 1, 0.06f), 10, () => { MissionState.Hybrid = hybrid; MissionState.NotifyChanged(); });
        var img = rt.GetComponent<Image>();
        var t = UiKit.Label(rt, label, 12, UiKit.Text, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(w, 24), TextAlignmentOptions.Center, true, 4);
        refreshers.Add(() =>
        {
            bool on = MissionState.Hybrid == hybrid;
            img.color = on ? UiKit.Selected : new Color(1, 1, 1, 0.06f);
            t.color = on ? UiKit.Ink : UiKit.Text;
        });
    }

    // 20 slim ticks, 5% each, like an instrument scale: click or drag across them.
    void Gauge(string index, string title, ref float y, Func<float> get, Action<float> set, Func<string> value = null, Func<string> caption = null)
    {
        Header(index, title, ref y);
        var val = UiKit.Label(card, "", 13, UiKit.Text, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-Pad, -y + 30), new Vector2(160, 18), TextAlignmentOptions.MidlineRight, spacing: 2);
        const int N = 20;
        float gw = W - 2 * Pad, bw = 5f, step = (gw - bw) / (N - 1);
        var track = UiKit.Rect(card, title + "_Gauge", new Vector2(0, 1), new Vector2(0, 1), new Vector2(Pad, -y), new Vector2(gw, 26));
        var hit = UiKit.Box(track, new Color(1, 1, 1, 0.001f), 0);
        hit.raycastTarget = true;
        var cells = new Image[N];
        for (int i = 0; i < N; i++)
        {
            var c = UiKit.Rect(track, "Tick", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(i * step, 0), new Vector2(bw, 24));
            cells[i] = UiKit.Box(c, UiKit.Off, 1.5f);
        }
        track.gameObject.AddComponent<DragTrack>().OnValue = t => { set(Mathf.Clamp(Mathf.Ceil(t * N - 0.15f), 0, N) / N); MissionState.NotifyChanged(); };
        refreshers.Add(() =>
        {
            int filled = Mathf.RoundToInt(get() * N);
            for (int i = 0; i < N; i++) cells[i].color = i < filled ? UiKit.Text : UiKit.Off;
            val.text = value != null ? value() : $"{get() * 100f:0}%";
        });
        y += 26 + 30;
        if (caption != null)
        {
            var cap = UiKit.Label(card, "", 11, UiKit.Text, new Vector2(0, 1), new Vector2(0, 1), new Vector2(Pad, -y + 22), new Vector2(W - 2 * Pad, 18), spacing: 1);
            refreshers.Add(() => cap.text = caption());
            y += 18;
        }
    }

    // ---------------- calendar ----------------

    void ShiftMonth(int d)
    {
        var m = viewMonth.AddMonths(d);
        if (m < FirstOfMonth(Today) || m > FirstOfMonth(DataEnd)) return;
        viewMonth = m;
        BuildCalendar();
    }

    void BuildCalendar()
    {
        if (calendarGrid == null) return;
        monthTitle.text = viewMonth.ToString("MMMM yyyy").ToUpperInvariant();
        foreach (Transform c in calendarGrid) Destroy(c.gameObject);
        float cw = (W - 2 * Pad) / 7f;
        int offset = ((int)viewMonth.DayOfWeek + 6) % 7;
        int days = DateTime.DaysInMonth(viewMonth.Year, viewMonth.Month);
        for (int d = 1; d <= days; d++)
        {
            var date = viewMonth.AddDays(d - 1);
            int cell = offset + d - 1;
            var rt = UiKit.Rect(calendarGrid, "Day" + d, new Vector2(0, 1), new Vector2(0, 1), new Vector2((cell % 7) * cw + 4, -(cell / 7) * 36), new Vector2(cw - 8, 32));
            bool selectable = date >= Today && date <= DataEnd;
            bool selected = date.Date == MissionState.LaunchUtc.Date;
            var day = date;
            if (selectable) UiKit.Button(rt, selected ? UiKit.Selected : new Color(0, 0, 0, 0), 8, () => MissionState.SetDay(day));
            else if (selected) UiKit.Box(rt, UiKit.Selected, 8);
            UiKit.Label(rt, d.ToString("00"), 14, selected ? UiKit.Ink : selectable ? UiKit.Text : UiKit.Text3,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(cw, 24), TextAlignmentOptions.Center, selected);
            if (NextGoDay.HasValue && NextGoDay.Value == date.Date)        // the one suggested day
                UiKit.Box(UiKit.Rect(rt, "GoMark", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 3), new Vector2(16, 2)), selected ? UiKit.Ink : UiKit.Text, 1);
        }
    }

    // ---------------- landing site on the Moon ----------------

    void PickOnMoon()
    {
        Open(false);
        SetView(OrbitCam.View.Moon);
    }

    bool MoonHit(Vector2 screen, out double lat, out double lon)
    {
        lat = lon = 0;
        var cam = Cam.GetComponent<Camera>();
        var ray = cam.ScreenPointToRay(screen);
        var sc = SpaceScene.Instance;
        Vector3 oc = ray.origin - sc.Moon.position;
        float b = Vector3.Dot(oc, ray.direction), c = oc.sqrMagnitude - SpaceScene.MoonRadius * SpaceScene.MoonRadius;
        float disc = b * b - c;
        if (disc < 0) return false;
        float t = -b - Mathf.Sqrt(disc);
        if (t < 0) return false;
        Vector3 point = ray.origin + ray.direction * t;
        Ephemeris.MoonLatLon(MissionModel.LandingUtc(MissionState.LaunchUtc), point, out lat, out lon);
        return true;
    }

    // What a point on the Moon is like at touchdown, shown next to a screen position.
    public void ShowReadout(Vector2 screen, double lat, double lon)
    {
        var l = MissionState.LaunchUtc;
        double sun = MissionModel.SunAt(l, lat, lon);
        bool rising = MissionModel.SunRising(l, lat, lon);
        bool earth = MissionModel.EarthAt(l, lat, lon) >= 0;
        string sunText = sun < 0 ? "SUN  BELOW HORIZON" : $"SUN  {sun:+0.0;-0.0}°  {(rising ? "RISING" : "SETTING")}";
        readoutText.text = $"<b>{Coords(lat, lon)}</b>\n<color=#8f98a1>{MissionModel.PlaceName(lat, lon)}</color>\n<color=#8f98a1>{sunText}{(earth ? "" : "  ·  NO EARTH")}</color>";
        readoutText.ForceMeshUpdate();
        var size = new Vector2(readoutText.preferredWidth + 28, readoutText.preferredHeight + 22);
        readout.sizeDelta = size;
        float scale = root.GetComponent<Canvas>().scaleFactor;
        Vector2 p = screen / scale;
        float x = p.x + 30 + size.x > root.rect.width - 16 ? p.x - 30 - size.x : p.x + 30;
        readout.anchoredPosition = new Vector2(x, p.y + size.y * 0.5f);
        readout.gameObject.SetActive(true);
        reticle.anchoredPosition = p;
        reticle.gameObject.SetActive(true);
    }

    public void ForceReadout(bool on) => forcedReadout = on;

    void Update()
    {
        if (root == null) return;
        bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        bool canPick = Cam.Current == OrbitCam.View.Moon && !open && !overlay.gameObject.activeSelf && !PollRunning;
        bool hit = false;
        if (canPick && !overUi && MoonHit(Input.mousePosition, out double lat, out double lon))
        {
            hit = true;
            if (NearSuggestion(Input.mousePosition)) { lat = SuggestedLat; lon = SuggestedLon; }   // the ring snaps
            ShowReadout(Input.mousePosition, lat, lon);
            if (Input.GetMouseButtonDown(0)) { downPos = Input.mousePosition; downTime = Time.unscaledTime; downOnMoon = true; }
            if (Input.GetMouseButtonUp(0) && downOnMoon && Vector2.Distance(downPos, Input.mousePosition) < 6f && Time.unscaledTime - downTime < 0.5f)
            {
                MissionState.SetSite(lat, lon);
                SpaceScene.Instance.DropPin();
            }
        }
        if (Input.GetMouseButtonUp(0)) downOnMoon = false;
        if (!hit && !forcedReadout) { readout.gameObject.SetActive(false); reticle.gameObject.SetActive(false); }
    }

    // ---------------- labels in the scene ----------------

    void BuildSceneLabels(RectTransform layer)
    {
        var sc = SpaceScene.Instance;
        Label(layer, "EARTH", () => Vector3.zero, () => Cam.Current == OrbitCam.View.Trip, SpaceScene.EarthRadius, false);
        Label(layer, "MOON  ·  LANDING DAY", () => sc.MoonPos, () => Cam.Current == OrbitCam.View.Trip, SpaceScene.MoonRadius, false);
        Label(layer, "MOON  ·  LIFTOFF", () => sc.MoonLaunchPos, () => Cam.Current == OrbitCam.View.Trip, 0f, false);
        Label(layer, "KENNEDY SPACE CENTER", () => sc.KennedyWorld(), () => Cam.Current == OrbitCam.View.Earth, 0f, true);
        Label(layer, "LANDING SITE", () => sc.SiteWorld(), () => Cam.Current == OrbitCam.View.Moon && MissionState.HasSite, 0f, true);
        // the suggested site: a thin ring on the Moon (hidden once it is the chosen site)
        Label(layer, "SUGGESTED", SuggestedWorld, () => Cam.Current == OrbitCam.View.Moon && HasSuggestedSite && !SuggestionIsSite(), 0f, true);
        suggestRing = UiKit.Rect(labels[labels.Count - 1].rt, "Ring", new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-8, 0), new Vector2(26, 26));
        var ring = suggestRing.gameObject.AddComponent<Image>();
        ring.sprite = UiKit.RingSprite; ring.color = UiKit.Text; ring.raycastTarget = false;
    }

    void Label(RectTransform layer, string text, Func<Vector3> world, Func<bool> visible, float bodyR, bool surface)
    {
        var rt = UiKit.Rect(layer, "L_" + text, Vector2.zero, new Vector2(0, 0.5f), Vector2.zero, new Vector2(320, 24));
        if (!surface) UiKit.Dot(rt, new Vector2(0, 0.5f), new Vector2(4, 0), 6, UiKit.Text);
        UiKit.Label(rt, text, 12, UiKit.Text, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(surface ? 14 : 16, 0), new Vector2(300, 22), spacing: 3);
        labels.Add((rt, world, visible, bodyR, surface));
    }

    void LateUpdate()
    {
        if (root == null) return;
        var cam = Cam.GetComponent<Camera>();
        float scale = root.GetComponent<Canvas>().scaleFactor;
        var sc = SpaceScene.Instance;
        bool covered = open || overlay.gameObject.activeSelf;
        sc.ShowLines = Cam.Current == OrbitCam.View.Trip;
        if (suggestRing) suggestRing.localScale = Vector3.one * (1f + 0.12f * Mathf.Sin(Time.unscaledTime * 3f));
        foreach (var (rt, world, visible, bodyR, surface) in labels)
        {
            Vector3 w = world();
            Vector3 s = cam.WorldToScreenPoint(w);
            bool show = !covered && visible() && s.z > 0;
            if (show && surface)
            {
                Vector3 centre = (w - sc.Moon.position).sqrMagnitude < w.sqrMagnitude ? sc.Moon.position : Vector3.zero;
                show = Vector3.Dot((w - centre).normalized, (cam.transform.position - w).normalized) > 0.05f;
            }
            rt.gameObject.SetActive(show);
            if (!show) continue;
            float offset = bodyR > 0 ? bodyR / (s.z * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad)) * Screen.height * 0.5f : 0f;
            rt.anchoredPosition = new Vector2((s.x + offset) / scale + 8, s.y / scale);
        }
    }

    // ---------------- go / no-go ----------------

    static List<(string name, MissionModel.Check c)> Checks() => new List<(string, MissionModel.Check)>
    {
        ("FLIGHT PATH", MissionModel.FlightPath(out _)),
        ("ROCKET", MissionModel.Rocket(out _)),
        ("LIFE SUPPORT", MissionModel.LifeSupport(out _)),
        ("WEATHER", Weather.Check(Weather.For(MissionState.LaunchUtc))),
    };

    public bool PollRunning { get; private set; }

    public IEnumerator Poll()
    {
        if (PollRunning) yield break;
        PollRunning = true;
        Open(false);
        foreach (Transform c in overlay) Destroy(c.gameObject);
        overlay.sizeDelta = new Vector2(560, 300);
        overlay.gameObject.SetActive(true);
        var list = Checks();
        bool go = list.TrueForAll(x => x.c.Go);
        float y = -36;
        UiKit.Label(overlay, "FLIGHT DIRECTOR'S POLL", 11, UiKit.Text3, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(480, 18), TextAlignmentOptions.Center, spacing: 5);
        y -= 40;
        // one monospace line per controller: NAME ........... then the answer lands at the end
        const int Cols = 44;
        float lineW = Cols * (16f * 0.6f + 0.16f);   // JetBrains Mono advance is 0.6 em
        foreach (var (name, c) in list)
        {
            yield return new WaitForSeconds(0.45f);
            string status = c.Go ? "GO" : "NO GO";
            string dots = new string('.', Cols - name.Length - status.Length - 2);
            string hex = c.Go ? "#78e199" : "#ff786b";
            var line = UiKit.Label(overlay, $"{name} <color=#3a3f46>{dots}</color>", 16, UiKit.Text, new Vector2(0.5f, 1), new Vector2(0, 1), new Vector2(-lineW / 2f, y), new Vector2(lineW + 40, 26), spacing: 1);
            line.enableWordWrapping = false;
            yield return new WaitForSeconds(0.3f);
            line.text = $"{name} <color=#3a3f46>{dots}</color> <b><color={hex}>{status}</color></b>";
            y -= 34;
        }
        yield return new WaitForSeconds(0.7f);
        y -= 16;
        if (go)
        {
            UiKit.Label(overlay, "GO FOR LAUNCH", 30, UiKit.Text, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(500, 44), TextAlignmentOptions.Center, true, 6);
            overlay.sizeDelta = new Vector2(560, -y + 76);
            yield return new WaitForSeconds(2.4f);
            yield return Fade(1f, 1.4f);
            PollRunning = false;
            if (Application.CanStreamedLevelBeLoaded("Flight")) SceneManager.LoadScene("Flight");
            yield break;
        }
        UiKit.Label(overlay, "HOLD", 30, UiKit.Bad, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(500, 44), TextAlignmentOptions.Center, true, 10);
        y -= 54;
        string why = "";
        foreach (var (name, c) in list) if (!c.Go) why += c.Simple + "\n";
        var w = UiKit.Label(overlay, why.Trim(), 13, UiKit.Text2, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(470, 80), TextAlignmentOptions.Top);
        w.ForceMeshUpdate();
        y -= Mathf.Max(30, w.preferredHeight + 22);
        var fix = UiKit.Rect(overlay, "Fix", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(200, 46));
        UiKit.Button(fix, UiKit.Selected, 12, () => { overlay.gameObject.SetActive(false); Open(true); });
        UiKit.Label(fix, "EDIT PLAN", 13, UiKit.Ink, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(200, 30), TextAlignmentOptions.Center, true, 4);
        overlay.sizeDelta = new Vector2(560, -y + 76);
        PollRunning = false;
    }

    IEnumerator Fade(float to, float seconds)
    {
        float from = fade.color.a;
        for (float t = 0; t < 1f; t += Time.deltaTime / seconds)
        {
            fade.color = new Color(0, 0, 0, Mathf.Lerp(from, to, t));
            yield return null;
        }
        fade.color = new Color(0, 0, 0, to);
        fade.raycastTarget = to > 0.5f;
    }
}
