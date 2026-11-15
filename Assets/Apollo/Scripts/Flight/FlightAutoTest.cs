// Self-test for step 4, only active with "-flighttest <folder>" (the planner jumps straight here):
// the rocket under the real sky from several places, the fly-in straight into the capsule, the cabin
// (looking out of the windows and the hatch window), an instrument name, and the countdown.
using System;
using System.Collections;
using System.IO;
using UnityEngine;

public class FlightAutoTest : MonoBehaviour
{
    string dir;

    void Start()
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-flighttest") dir = args[i + 1];
        if (dir == null) { enabled = false; return; }
        StartCoroutine(Cards());
        Directory.CreateDirectory(dir);
        StartCoroutine(Array.IndexOf(args, "-landing") >= 0 ? LandingRun() : Run());
    }

    // step 7 on its own ("-landing"): the self-test flies it (right answers, autopilot to a clear spot)
    IEnumerator LandingRun()
    {
        Landing l = null;
        while ((l = FindObjectOfType<Landing>()) == null) yield return null;
        if (Problems.I != null) Problems.I.Auto = true;
        l.Auto = true;
        IEnumerator At(Landing.Stage st, float tt, string name)
        {
            float until = Time.time + 120f;
            while ((l.Now < st || (l.Now == st && l.T < tt)) && Time.time < until) { if (Problems.I != null) Problems.I.Auto = true; yield return null; }
            yield return Shot(name, 0.1f);
            Debug.Log($"FLIGHTTEST landing {name}: stage {l.Now} t {l.T:0} problem {l.Problem}  {Fps()}");
        }
        yield return At(Landing.Stage.Descent, 60f, "l_01_braking_outside");
        yield return At(Landing.Stage.Descent, 300f, "l_02_braking_10km");
        yield return At(Landing.Stage.Descent, 492f, "l_03_pitchover_window");
        yield return At(Landing.Stage.Descent, 585f, "l_04_approach_lpd");
        l.TestView(true); yield return Shot("l_04o_approach_outside", 0.3f); l.TestView(false);
        yield return At(Landing.Stage.Manual, 0f, "l_05_low_gate");
        { float until = Time.time + 5f; while (Time.time < until && l.Now == Landing.Stage.Manual) yield return null; }
        l.TestView(true); yield return Shot("l_05o_low_gate_outside", 0.3f); l.TestView(false);
        { float until = Time.time + 12f; while (Time.time < until && l.Now == Landing.Stage.Manual) yield return null; }
        yield return Shot("l_06_hover", 0f);
        { float until = Time.time + 90f; while (Time.time < until && l.Now < Landing.Stage.Film) yield return null; }
        yield return Shot("l_07_touchdown", 0.6f);
        yield return Shot("l_08_earth_over_eagle", 6f);
        { float until = Time.time + 30f; while (Time.time < until && l.Now < Landing.Stage.Done) yield return null; }
        if (Problems.I != null) Problems.I.Auto = false;                // keep the end card up for its picture
        yield return Shot("l_09_mission_complete", 0.8f);
        Debug.Log("FLIGHTTEST DONE");
        yield return new WaitForSeconds(0.3f);
        Application.Quit();
    }

    // frame rate: the slowest and the average frame over the last second (the radio and the warp depend on it)
    readonly System.Collections.Generic.Queue<float> frames = new System.Collections.Generic.Queue<float>();
    void Update()
    {
        frames.Enqueue(Time.unscaledDeltaTime);
        float sum = 0; foreach (var f in frames) sum += f;
        while (frames.Count > 1 && sum - frames.Peek() > 1f) { sum -= frames.Dequeue(); }
    }
    string Fps()
    {
        float sum = 0, worst = 0; foreach (var f in frames) { sum += f; worst = Mathf.Max(worst, f); }
        return frames.Count == 0 ? "-" : $"{frames.Count / Mathf.Max(sum, 1e-3f):0.0} fps (worst frame {worst * 1000f:0} ms)";
    }

    IEnumerator Shot(string name, float wait)
    {
        yield return new WaitForSeconds(wait);
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
        Debug.Log("FLIGHTTEST shot " + name + "  " + Fps());
    }

    // which object draws what: time stopped, the same view shot again with each group of renderers hidden
    IEnumerator WhatDraws(string tag)
    {
        Time.timeScale = 0f;
        var groups = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Renderer>>();
        foreach (var r in FindObjectsOfType<Renderer>())
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            var path = new System.Collections.Generic.List<string>();
            for (var x = r.transform; x != null; x = x.parent) path.Insert(0, x.name);
            string key = path.Count >= 2 ? path[0] + "~" + path[1] : path[0];
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = new System.Collections.Generic.List<Renderer>();
            list.Add(r);
        }
        IEnumerator Snap(string what)
        {
            yield return null; yield return null;
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "d_" + tag + "_" + what + ".png"));
            yield return null; yield return null;
        }
        yield return Snap("all");
        int i = 0;
        foreach (var kv in groups)
        {
            foreach (var r in kv.Value) r.enabled = false;
            string safe = string.Join("_", kv.Key.Split(Path.GetInvalidFileNameChars()));
            yield return Snap((i++).ToString("00") + "_no_" + safe);
            foreach (var r in kv.Value) r.enabled = true;
            Debug.Log("FLIGHTTEST whatdraws " + kv.Key + " (" + kv.Value.Count + " renderers)");
        }
        var cam = Camera.main; var cf = cam.clearFlags; var bg = cam.backgroundColor;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.magenta;
        yield return Snap("zz_no_sky");
        cam.clearFlags = cf; cam.backgroundColor = bg;
        Time.timeScale = 1f;
    }

    // a shot once the trip reaches a part (or has gone past it), optionally looking a given way from the couch
    IEnumerator VShot(Voyage v, Voyage.Part part, float after, string name, float yaw = float.NaN, float pitch = 0f)
    {
        float until = Time.time + 90f;
        while ((v.Now < part || (v.Now == part && v.PartTime < after)) && Time.time < until) yield return null;
        if (!float.IsNaN(yaw)) { FlightDirector.Instance.Look(yaw, -pitch); yield return new WaitForSeconds(0.25f); }
        yield return Shot(name, 0f);
        Debug.Log($"FLIGHTTEST voyage {name}: part {v.Now} t {v.PartTime:0.0} GET {v.Get / 3600:0.000} h  {Fps()}");
    }

    // a picture of every problem card, the star telescope and the docking explainer while they are up
    IEnumerator Cards()
    {
        int n = 0; bool scopeShot = false, explShot = false, abortShot = false;
        bool abortOnly = Array.IndexOf(Environment.GetCommandLineArgs(), "-abortonly") >= 0;
        while (true)
        {
            var cq = FindObjectOfType<Consequences>();
            if (cq != null && cq.Aborting && !abortShot)
            {
                abortShot = true;
                yield return Shot("x_abort_1", 0.7f);
                yield return Shot("x_abort_2", 1.0f);
                yield return Shot("x_abort_3", 1.3f);
                yield return Shot("x_abort_4", 1.6f);
                yield return Shot("x_abort_5", 1.4f);
                if (abortOnly) { yield return new WaitForSeconds(1.5f); yield return Shot("x_abort_6_card", 0f); Debug.Log("FLIGHTTEST DONE"); Application.Quit(); yield break; }
            }
            if (Problems.I != null && Problems.I.Deciding)
            {
                yield return new WaitForSecondsRealtime(0.9f);
                yield return Shot($"card_{++n:00}", 0f);
                while (Problems.I.Deciding) yield return null;
            }
            var v = FlightDirector.Instance ? FlightDirector.Instance.Trip : null;
            if (v != null && !scopeShot && v.Now == Voyage.Part.Align && v.PartTime > 2.5f && !(Problems.I != null && Problems.I.Deciding)) { scopeShot = true; yield return Shot("v_00_scope", 0f); }
            if (v != null && !explShot && v.Now == Voyage.Part.Explainer && v.PartTime > 2.2f) { explShot = true; yield return Shot("v_05b_explainer", 0f); }
            yield return null;
        }
    }

    IEnumerator Run()
    {
        var fd = FlightDirector.Instance;
        fd.HoldIntro(true);
        yield return new WaitForSeconds(1.6f);
        fd.PlaceCamera(new Vector3(-150f, 2f, -190f), new Vector3(0f, 55f, 0f), 50f);
        yield return Shot("f_00a_rocket_from_the_lawn", 0.5f);
        fd.PlaceCamera(new Vector3(-12f, 4f, -60f), new Vector3(10f, -8f, 40f), 60f);
        yield return Shot("f_00b_ground_close", 0.5f);
        fd.PlaceCamera(new Vector3(-700f, 380f, -900f), new Vector3(0f, 0f, 60f), 42f);
        yield return Shot("f_00c_aerial", 0.5f);
        fd.PlaceCamera(new Vector3(-9f, fd.Level320 + 1.7f, 12.0f), new Vector3(-4587f, 40f, -2485f), 30f);
        yield return Shot("f_00d_vab_from_level320", 0.5f);
        fd.PlaceCamera(new Vector3(150f, 30f, -170f), new Vector3(0f, 70f, 10f), 45f);
        yield return Shot("f_00e_rocket_and_sky", 0.5f);
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-padonly") >= 0)       // the weather at the pad only
        {
            fd.PlaceCamera(new Vector3(-60f, 3f, -120f), new Vector3(0f, 60f, 0f), 60f);
            yield return Shot("f_00f_pad_weather", 1.5f);
            Debug.Log("FLIGHTTEST DONE"); yield return new WaitForSeconds(0.3f); Application.Quit(); yield break;
        }

        fd.SetIntroTime(0f);
        fd.HoldIntro(false);
        yield return Shot("f_01_intro_0s", 0.3f);
        yield return Shot("f_02_intro_4s", 3.7f);
        yield return Shot("f_03_intro_8s", 4.0f);
        yield return Shot("f_04_intro_10_5s", 2.5f);
        yield return Shot("f_05_into_the_white_room", 2.5f);
        while (fd.Current != FlightDirector.Phase.Cabin) yield return null;
        yield return Shot("f_06_cabin", 1.2f);
        fd.Look(-55f, 20f);
        yield return Shot("f_07_window_left", 1.2f);
        fd.LookAtWorld(fd.HatchWindow);
        yield return Shot("f_08_hatch_window", 1.2f);
        yield return Shot("f_09_arm_swinging_away", 9.0f);
        fd.Look(0f, 0f);
        fd.ForceHover("DSKY");
        yield return Shot("f_10_hover_dsky", 1.0f);
        fd.ForceHover(null);
        // the climb: from the couch, out of the windows, at real mission times
        Vector3 hw = Quaternion.Inverse(fd.CommanderEye.rotation) * (fd.HatchWindow - fd.CommanderEye.position);
        float hwYaw = Mathf.Atan2(hw.x, hw.z) * Mathf.Rad2Deg, hwPitch = Mathf.Asin(hw.normalized.y) * Mathf.Rad2Deg;
        fd.StartCountdown();
        yield return Shot("f_11_t_minus_6", 4.6f);
        var shots = new (float met, string name, float yaw, float pitch)[]
        {
            (2f, "f_12_liftoff", 0f, 0f), (8f, "f_13_tower_passing_hatch_window", hwYaw, hwPitch), (24f, "f_14_cloud_ahead_up_window", -17f, 37f),
            (33f, "f_15_through_the_cloud_left_window", -79f, 17f), (37.6f, "f_15b_lightning_strike", 0f, 0f), (44f, "f_15c_sce_guidance", 0f, 0f), (50f, "f_15d_capcom_call", 0f, 0f), (45f, "f_16_above_clouds_left_window", -79f, 17f),
            (75f, "f_17_mach1_left_window", -79f, 17f), (88f, "f_17b_16km_left_window", -79f, 17f), (100f, "f_18a_21km_left_window", -79f, 17f), (126f, "f_18b_36km_left_window", -79f, 17f), (142f, "f_18c_48km_left_window", -79f, 17f), (150f, "f_18_high_hatch_window", hwYaw, hwPitch), (162.4f, "f_19_staging_flash", 0f, 0f),
            (240f, "f_20_space_left_window", -79f, 17f), (300f, "f_21_space_hatch_window", hwYaw, hwPitch), (560f, "f_22_third_stage_up_window", -17f, 37f),
            (711.5f, "f_23_orbit_couch", 0f, 0f),
        };
        foreach (var sh in shots)
        {
            while (fd.Climb == null || fd.Climb.Met < sh.met - 0.6f) { if (Problems.I != null) Problems.I.Auto = true; yield return null; }
            fd.Look(sh.yaw, -sh.pitch);
            while (fd.Climb.Met < sh.met) yield return null;
            yield return Shot(sh.name, 0f);
            if (sh.name == "f_12_liftoff" || sh.name == "f_16_above_clouds_left_window" || sh.name == "f_18a_21km_left_window" || sh.name == "f_18b_36km_left_window" || sh.name == "f_19_staging_flash" || sh.name == "f_21_space_hatch_window")
            {
                fd.SetOutside(true);
                yield return Shot(sh.name.Replace("f_", "o_") + "_outside", 0.35f);
                if (sh.name == "f_16_above_clouds_left_window") yield return WhatDraws("o16");
                if (sh.name == "f_12_liftoff") yield return WhatDraws("o12");
                fd.SetOutside(false);
            }
            Debug.Log($"FLIGHTTEST climb {sh.name}: MET {fd.Climb.Met:0.0} alt {fd.Climb.AltitudeM / 1000f:0.0} km speed {fd.Climb.SpeedKmh:0} km/h g {fd.Climb.G:0.00}  {fd.RadioState}  {Fps()}");
        }
        // step 6: to the Moon (the self-test plays every part itself: unstrap, autopilot docking, PRO)
        Voyage v;
        while ((v = fd.Trip) == null) yield return null;
        v.Auto = true;
        yield return VShot(v, Voyage.Part.Orbit, 1.0f, "v_01_orbit_left_window", -79f, 17f);
        yield return VShot(v, Voyage.Part.Float, 3.5f, "v_02_float");
        yield return VShot(v, Voyage.Part.ToTli, 3.0f, "v_03_over_the_pacific", -79f, 17f);
        yield return VShot(v, Voyage.Part.Tli, 4.0f, "v_04_tli_burn", -79f, 17f);
        fd.SetOutside(true); yield return Shot("v_04o_tli_burn_outside", 0.3f); fd.SetOutside(false);
        yield return VShot(v, Voyage.Part.AfterTli, 0.8f, "v_05_after_tli", -79f, 17f);
        yield return VShot(v, Voyage.Part.Separation, 2.4f, "v_06_panels_open");
        yield return VShot(v, Voyage.Part.Separation, 6.4f, "v_07_turnaround");
        yield return VShot(v, Voyage.Part.Docking, 1.0f, "v_08_docking_coas_far");
        yield return VShot(v, Voyage.Part.Docking, 12.0f, "v_08b_docking_coas_near");
        yield return VShot(v, Voyage.Part.Docked, 2.0f, "v_09_docked_extraction");
        foreach (var (b, n) in new[] { ("DAY 1", "v_10_day1"), ("SLOWEST", "v_11_slowest"), ("THE MOON'S PULL", "v_12_moons_pull"), ("IN THE MOON'S SHADOW", "v_13_moons_shadow") })
        {
            float until = Time.time + 60f;
            while (v.LastBeat != b && Time.time < until) yield return null;
            yield return Shot(n, 1.2f);
            fd.SetOutside(true); yield return Shot(n + "_outside", 0.3f); fd.SetOutside(false);
            Debug.Log($"FLIGHTTEST voyage {n}: GET {v.Get / 3600:0.00} h  {Fps()}");
        }
        yield return VShot(v, Voyage.Part.Pro, 1.5f, "v_14_pro_dsky");
        yield return VShot(v, Voyage.Part.Loi, 2.5f, "v_15_loi_burn");
        fd.SetOutside(true); yield return Shot("v_15o_loi_outside", 0.3f); fd.SetOutside(false);
        yield return VShot(v, Voyage.Part.Lunar, 1.5f, "v_16_lunar_orbit_window", -79f, -10f);
        { float until = Time.time + 30f; while (v.LastBeat != "AOS" && Time.time < until) yield return null; }
        yield return Shot("v_17_aos_moon_window", 3.0f);
        fd.SetOutside(true); yield return Shot("v_17o_moon_outside", 0.4f); fd.SetOutside(false);
        Debug.Log("FLIGHTTEST DONE");
        yield return new WaitForSeconds(0.3f);
        Application.Quit();
    }
}
