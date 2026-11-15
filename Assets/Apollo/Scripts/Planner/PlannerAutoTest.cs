// Self-test for the mission planner, only active with "-autotest <folder>": the clean start, the
// mission card, picking a site on the Moon (with the hover readout), a far-side site that gets a
// HOLD, a hand-found good day that gets a GO, and the Apollo 11 accuracy check in the log.
using System;
using System.Collections;
using System.IO;
using UnityEngine;

public class PlannerAutoTest : MonoBehaviour
{
    string dir;

    void Start()
    {
        var args = Environment.GetCommandLineArgs();
        if (Array.IndexOf(args, "-flighttest") >= 0) { UnityEngine.SceneManagement.SceneManager.LoadScene("Flight"); enabled = false; return; }
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-autotest") dir = args[i + 1];
        if (dir == null) { enabled = false; return; }
        Directory.CreateDirectory(dir);
        StartCoroutine(Run());
    }

    IEnumerator Shot(string name, float wait)
    {
        yield return new WaitForSeconds(wait);
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
        Debug.Log("AUTOTEST shot " + name);
    }

    IEnumerator Run()
    {
        var ui = PlannerUI.Instance;
        var a11 = new DateTime(1969, 7, 16, 13, 32, 0, DateTimeKind.Utc);
        Debug.Log($"AUTOTEST Apollo 11 sun at Tranquility Base: {MissionModel.SunAt(a11, 0.674, 23.473):0.00} deg (NASA 10.8), earth {MissionModel.EarthAt(a11, 0.674, 23.473):0.0} deg");

        yield return Shot("q_01_start", 3.0f);
        ui.Open(true);
        yield return Shot("q_02_card_empty", 1.2f);

        // pick a site the way a student does: MOON button closes the card, then hover and click
        ui.Open(false);
        ui.SetView(OrbitCam.View.Moon);
        yield return new WaitForSeconds(2.6f);
        var cam = ui.Cam.GetComponent<Camera>();
        var sc = SpaceScene.Instance;
        double lat = 8.5, lon = 31.4;
        Vector3 p = sc.Moon.position + sc.Moon.rotation * Ephemeris.BodyPoint(lat, lon) * SpaceScene.MoonRadius;
        Vector3 s = cam.WorldToScreenPoint(p);
        ui.ForceReadout(true);
        ui.ShowReadout(s, lat, lon);
        yield return Shot("q_03_moon_hover", 0.3f);
        ui.ForceReadout(false);
        MissionState.SetSite(lat, lon);
        sc.DropPin();
        yield return Shot("q_04_site_set", 1.0f);
        ui.SetView(OrbitCam.View.Trip);
        yield return Shot("q_04b_trip", 3.0f);
        ui.SetView(OrbitCam.View.Moon);
        yield return new WaitForSeconds(2.6f);

        // the far side: no radio contact with Earth
        MissionState.SetSite(10, 175);
        StartCoroutine(ui.Poll());
        yield return new WaitForSeconds(0.5f);
        while (ui.PollRunning) yield return null;
        yield return Shot("q_05_hold_farside", 0.4f);

        // the suggestions: best site for the current date, next GO day for the chosen site
        Debug.Log($"AUTOTEST suggested site for {MissionState.LaunchUtc:yyyy-MM-dd}: {ui.HasSuggestedSite} " +
                  $"{ui.SuggestedLat:0.0},{ui.SuggestedLon:0.0} {MissionModel.PlaceName(ui.SuggestedLat, ui.SuggestedLon)} " +
                  $"sun {MissionModel.SunAt(MissionState.LaunchUtc, ui.SuggestedLat, ui.SuggestedLon):0.0} " +
                  $"go={MissionModel.FlightPathGo(MissionState.LaunchUtc, ui.SuggestedLat, ui.SuggestedLon)}");
        MissionState.SetSite(lat, lon);
        DateTime? brute = null;
        for (int i = 0; i < 800 && brute == null; i++)
        {
            var day = PlannerUI.Today.AddDays(i);
            MissionState.SetDay(day);
            if (MissionModel.FlightPath(out _).Go) brute = day;
        }
        ui.TakeNextGoDay();
        Debug.Log($"AUTOTEST next GO day for Mare Tranquillitatis: {ui.NextGoDay:yyyy-MM-dd} (brute force {brute:yyyy-MM-dd}), " +
                  $"poll flight path go={MissionModel.FlightPath(out _).Go}");
        MissionState.Fuel = 0.35f; MissionState.Supplies = 0.9f;
        MissionState.NotifyChanged();
        ui.Open(true);
        yield return Shot("q_06_card_full", 1.2f);
        // hover the MOON button the way the mouse does: the button turns white, its text turns black
        var pick = GameObject.Find("Pick");
        var ped = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current);
        UnityEngine.EventSystems.ExecuteEvents.Execute(pick, ped, UnityEngine.EventSystems.ExecuteEvents.pointerEnterHandler);
        yield return Shot("q_06c_moon_button_hover", 0.4f);
        UnityEngine.EventSystems.ExecuteEvents.Execute(pick, ped, UnityEngine.EventSystems.ExecuteEvents.pointerExitHandler);
        ui.Open(false);
        yield return new WaitForSeconds(0.4f);
        StartCoroutine(ui.Poll());
        yield return Shot("q_07_go", 4.0f);
        Debug.Log("AUTOTEST DONE");
        yield return new WaitForSeconds(0.5f);
        Application.Quit();
    }
}
