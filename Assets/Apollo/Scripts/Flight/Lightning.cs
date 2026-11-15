// A climb problem, and not Apollo 11's: 36.5 s after liftoff on 14 November 1969, Apollo 12 was struck by
// lightning (again at 52 s). The fuel cells dropped off line, the caution and warning panel lit up, and the signal
// conditioning equipment (SCE), which turns the spacecraft's sensor readings into telemetry, lost power: Houston's
// screens filled with garbage. The Saturn's own guidance was not hit and kept flying. John Aaron (EECOM) remembered
// the pattern from a test a year earlier and called "SCE to Aux"; CAPCOM Gerry Carr passed it up, Al Bean flipped
// the switch, the data came back, and the crew reset the fuel cells.
// Played with your hands while the rocket keeps climbing: hover the panel to read the switches, click the right one.
// A clock runs: if Houston stays blind too long, Flight calls an abort. Wrong actions play out (abort, blackout),
// then the climb rewinds to just before the strike.
using UnityEngine;
using UnityEngine.UI;

public class Lightning : MonoBehaviour
{
    public const float StrikeMet = 36.5f;
    const float AbortClock = 45f;
    // positions on the scanned panel, in the Smithsonian scan's glTF coordinates of its part (cm)
    // SCE POWER: panel 3, in front of the right couch, bottom row of the communications group, 4th of 6 toggles;
    // the fuel cell switches are above it, over the AC inverter switches
    public static Vector3 SceSwitchGltf = new Vector3(58.0f, 205.6f, 35.6f);
    public static Vector3 FuelCellGltf = new Vector3(74.0f, 206.0f, 50.0f);

    FlightDirector fd;
    Ascent climb;
    Transform panel, seats;
    float t = -1f, stageT, clock;
    enum Stage { Waiting, Choosing, Act, FuelCells, Lost, Done }
    Stage stage = Stage.Waiting;
    bool controlsMade, ringShown, capcomSaid, alarmSilenced;
    RectTransform ring;
    TMPro.TextMeshProUGUI ringLabel;
    Light flashLight, alarmLight;
    AudioSource alarm, crackle;
    LineRenderer bolt;
    RadioCue radio;
    CabinLabel sce, fuelCells;

    public bool Active => stage == Stage.Choosing || stage == Stage.Act || stage == Stage.FuelCells || stage == Stage.Lost;

    public void Begin(FlightDirector director, Ascent a, RectTransform hud)
    {
        fd = director; climb = a;
        foreach (var l in FindObjectsOfType<CabinLabel>(true))
        {
            if (l.Title == "MAIN DISPLAY CONSOLE") panel = l.transform.parent;
            if (l.Title == "HAND CONTROLLERS") seats = l.transform.parent;
        }
        ring = UiKit.Rect(hud, "SwitchRing", Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40, 40));
        var img = ring.gameObject.AddComponent<Image>();
        img.sprite = UiKit.Ring(64); img.color = Color.white; img.raycastTarget = false;
        ringLabel = UiKit.Label(ring, "", 11, UiKit.Text, new Vector2(0.5f, 0), new Vector2(0.5f, 1), new Vector2(0, -6), new Vector2(240, 16), TMPro.TextAlignmentOptions.Center, true, 3);
        UiKit.Shadow(ringLabel);
        ring.gameObject.SetActive(false);

        flashLight = NewLight("LightningFlash", new Color(0.85f, 0.9f, 1f), 6f);
        alarmLight = NewLight("MasterAlarm", new Color(1f, 0.12f, 0.08f), 2.2f);
        alarm = gameObject.AddComponent<AudioSource>();
        alarm.clip = AlarmTone(); alarm.loop = true; alarm.playOnAwake = false; alarm.volume = 0.14f; alarm.spatialBlend = 0f;
        crackle = gameObject.AddComponent<AudioSource>();
        crackle.clip = Thunder(); crackle.playOnAwake = false; crackle.spatialBlend = 0f; crackle.volume = 0.9f;
        bolt = new GameObject("LightningBolt").AddComponent<LineRenderer>();
        bolt.useWorldSpace = true; bolt.widthMultiplier = 2.2f; bolt.numCapVertices = 2;
        bolt.material = new Material(fd.TrailMat) { color = new Color(0.9f, 0.95f, 1f, 1f) };
        bolt.startColor = bolt.endColor = new Color(0.92f, 0.96f, 1f, 1f);
        bolt.enabled = false;
        radio = RadioCue.On(gameObject); radio.Preload();
    }

    static Vector3 G(Vector3 gltf) => new Vector3(-gltf.x, gltf.y, gltf.z);

    // the switches the student can use: the right ones, the abort handle, and two that sound useful but are not
    void MakeControls()
    {
        if (controlsMade || panel == null) return;
        controlsMade = true;
        sce = fd.AddControl("SCE POWER", "Signal conditioning equipment: turns every sensor reading into data for Houston.  NORM · OFF · AUX", panel, G(SceSwitchGltf));
        fuelCells = fd.AddControl("FUEL CELL RESET", "Puts the three fuel cells back on line.", panel, G(FuelCellGltf));
        fd.AddControl("C&W POWER", "Power for the caution and warning lights.", panel, G(new Vector3(40.0f, 207.0f, 44.0f)));
        fd.AddControl("FUEL CELL PURGE", "Blows waste gas out of a fuel cell.", panel, G(new Vector3(68.0f, 206.5f, 60.0f)));
        Problems.I.MasterAlarm(fd, panel);
        Problems.I.AbortHandle(fd, seats ? seats : panel);
    }

    Light NewLight(string name, Color c, float range)
    {
        var l = new GameObject(name).AddComponent<Light>();
        l.type = LightType.Point; l.color = c; l.range = range; l.intensity = 0f; l.shadows = LightShadows.None;
        l.cullingMask = 1 << 8; l.enabled = false;
        l.transform.SetParent(fd.CommanderEye.parent, false);
        l.transform.position = fd.CommanderEye.position + fd.CommanderEye.forward * 0.8f + fd.CommanderEye.up * 0.15f;
        return l;
    }

    void Update()
    {
        if (climb == null || stage == Stage.Done) return;
        if (stage == Stage.Waiting)
        {
            if (climb.Met < StrikeMet) return;
            Strike();
        }
        t += Time.deltaTime; stageT += Time.deltaTime;

        float f = Mathf.Exp(-t * 7f) + (t > 0.18f && t < 0.3f ? 0.6f : 0f);
        flashLight.intensity = fd.Outside ? 0f : f * 9f; flashLight.enabled = flashLight.intensity > 0.01f;
        Shader.SetGlobalFloat("_LightningFlash", f);
        bolt.enabled = t < 0.35f && fd.Outside;
        bool alarmOn = stage == Stage.Choosing || stage == Stage.Act || stage == Stage.FuelCells;
        alarmLight.intensity = alarmOn && !fd.Outside ? (Mathf.Repeat(t, 0.8f) < 0.45f ? 0.45f : 0.05f) : 0f;
        alarmLight.enabled = alarmLight.intensity > 0f;
        if (stage == Stage.Lost) return;
        if (stage == Stage.Choosing) { if (stageT > 1.0f) radio.Say("a12_dropout"); return; }

        // Houston is blind: Flight will call an abort when the clock runs out
        if (stage == Stage.Act)
        {
            clock -= Time.deltaTime;
            Problems.I.Pressure("HOUSTON CAN'T SEE THE SPACECRAFT  ·  FLIGHT CALLS ABORT IN", clock, AbortClock);
            if (stageT > 1.0f) radio.Say("a12_dropout");
            if (!capcomSaid && stageT > 20f)
            {
                capcomSaid = true; radio.Say("a12_sce");
                fd.Explain("CAPCOM: \"Apollo 12, Houston. Try SCE to auxiliary. Over.\"", 10f);
            }
            if (!ringShown && stageT > 30f) ringShown = true;
            Ring(ringShown ? sce : null, "SCE POWER");
            if (clock <= 0f) { Lose("FLIGHT CALLED ABORT", true,
                "Houston couldn't read the spacecraft for too long. The Flight Director called an abort: the escape tower pulled Columbia off the rocket.",
                "The rocket was fine; only the data to Houston was lost. The switch that brings it back is SCE POWER, on the right-hand panel: set it to AUX. Hover the panel to read the switch names."); return; }
        }
        if (stage == Stage.FuelCells)
        {
            Problems.I.PressureOff();
            if (!ringShown && stageT > 15f) ringShown = true;
            Ring(ringShown ? fuelCells : null, "FUEL CELL RESET");
        }
        if (Problems.I.Auto) AutoPlay();
        else if (Input.GetMouseButtonDown(0) && fd.Hovered != null) Use(fd.Hovered);
    }

    void Strike()
    {
        stage = Stage.Act; t = 0f; stageT = 0f;
        clock = AbortClock;
        climb.HoldRealTime = true;
        Problems.I.Busy = true;
        MakeControls();
        fd.Garble(true);
        fd.AscentEvent("LIGHTNING STRIKE", "A flash and a bang: lightning, as happened to Apollo 12. Warning lights everywhere, and Houston's data is garbage.");
        alarm.Play(); crackle.Play(); alarmSilenced = false;
        DrawBolt();
        AskChoice();
    }

    // the choice, under a clock while the rocket keeps climbing; a wrong one is played out, then rewound to here
    void AskChoice()
    {
        stage = Stage.Choosing;
        Problems.I.Ask("LIGHTNING STRIKE  ·  APOLLO 12",
            "Lightning just hit your rocket, 36 seconds after liftoff. The rocket is still climbing, but alarms are ringing and Houston can't read any of your data. What do you do?",
            new[] { "Warning lights: all three FUEL CELLS off, AC BUS 1 and 2, MAIN BUS A and B low",
                    "Houston: \"Our data is garbage. We can't see you.\"",
                    "The rocket's engines and steering: working normally" },
            new (string, bool, string)[]
            {
                ("ABORT: FIRE THE ESCAPE TOWER", false, ""),
                ("RESET THE FUEL CELLS NOW", false, ""),
                ("SCE TO AUX", true, "SCE is a small box that sends all your readings to Houston. The lightning cut its power; AUX switches it to a backup supply. Now do it: find SCE POWER on the panel and click it."),
                ("WAIT AND DO NOTHING", false, ""),
            }, 20f,
            () => { stage = Stage.Act; stageT = 0f; clock = AbortClock; fd.Explain("Find SCE POWER on the right-hand panel and click it. Hover the switches to read their names.", 12f); },
            i =>
            {
                if (i == 0) Lose("ABORT", true,
                    "You fired the escape tower. It pulled Columbia off a rocket that was flying perfectly, and Apollo 12 splashed down in the Atlantic, 20 minutes after launch.",
                    "Read the last line: the engines and steering were fine. Lightning only knocked out the data to Houston. You don't abort a healthy rocket.");
                else if (i == 1) Lose("MAIN BUS SHORT", false,
                    "You put the fuel cells back on line without knowing if they were safe. One of them could have been damaged by the strike: here it was, it shorted the main power line, and the cabin went dark.",
                    "First give Houston its data back, so everyone can see which fuel cells are safe. Then reset them.");
                else Lose("FLIGHT CALLED ABORT", true,
                    "You waited. Houston still couldn't see anything, and with no data the Flight Director had to assume the worst: \"Abort.\"",
                    "Doing nothing is a choice too. Houston needs its data back to judge if the spacecraft is safe: the switch for that is SCE to AUX.");
            },
            () => Lose("TOO SLOW", true,
                "20 seconds went by with no decision. With no data from the spacecraft, the Flight Director called an abort.",
                "In an emergency you still have to decide fast. Here the clue was that the rocket flew fine but Houston saw garbage: that points to the box that sends the data, the SCE."));
    }


    void Use(string control)
    {
        if (stage == Stage.Act)
        {
            switch (control)
            {
                case "SCE POWER":
                    fd.Garble(false);
                    fd.AscentEvent("SCE TO AUX", "The data box runs on its backup supply: Houston can see the spacecraft again.");
                    fd.Explain("Houston can see again: all three fuel cells are off line, the batteries carry the load. Now reset the fuel cells.", 12f);
                    stage = Stage.FuelCells; stageT = 0f; ringShown = false;
                    return;
                case "FUEL CELL RESET":
                    Lose("MAIN BUS SHORT", false,
                        "You put the fuel cells back on line blind. If one of them was damaged by the strike, it shorts the main bus: here it does, and the cabin goes dark.",
                        "First get Houston its data back (SCE POWER to AUX). Then everyone can see which fuel cells are safe, and you reset them.");
                    return;
                case "ABORT HANDLE":
                    Lose("ABORT", true,
                        "You turned the abort handle. The escape tower fires and pulls Columbia off a rocket that was flying perfectly. Apollo 12 splashes down in the Atlantic.",
                        "The Saturn has its own guidance computer, and lightning didn't hurt it. Only Houston's data was lost: SCE POWER to AUX brings it back.");
                    return;
            }
        }
        if (stage == Stage.FuelCells)
        {
            switch (control)
            {
                case "FUEL CELL RESET":
                    Solve(); return;
                case "ABORT HANDLE":
                    Lose("ABORT", true, "With everything nearly fixed, you still aborted. Apollo 12 splashes down in the Atlantic.", "The fuel cells only needed a reset. There is no reason to abort a healthy rocket.");
                    return;
                case "SCE POWER":
                    fd.Explain("SCE is already on AUX: Houston can see. Now the FUEL CELL RESET switches.", 5f); return;
            }
        }
        switch (control)
        {
            case "MASTER ALARM":
                if (!alarmSilenced) { alarmSilenced = true; alarm.Stop(); fd.Explain("Alarm silenced. The warning lights are still on: the fault isn't fixed.", 5f); }
                return;
            case "C&W POWER":
                clock -= 5f;
                fd.Explain("Caution and warning power only runs the lights. Turning it off hides the problem, it doesn't fix it. (−5 s)", 6f);
                return;
            case "FUEL CELL PURGE":
                clock -= 5f;
                fd.Explain("A purge clears waste gas out of a working fuel cell. These are off line: purging does nothing. (−5 s)", 6f);
                return;
        }
    }

    void Solve()
    {
        stage = Stage.Done; Problems.I.Busy = false;
        alarm.Stop(); ring.gameObject.SetActive(false);
        Problems.I.PressureOff();
        radio.Say("a12_fuelcells");
        fd.AscentEvent("FUEL CELLS RESET", "All three fuel cells back on line.");
        climb.Paused = true;
        Problems.I.Info("SOLVED  ·  WHY IT WORKED",
            "The lightning didn't break the rocket. It knocked out the power of one small box, the SCE, which sends the spacecraft's readings to Houston. "
          + "Its AUX setting runs it on a backup supply, so Houston could see again.\n\n"
          + "With the data back, everyone could see the fuel cells were fine, so resetting them was safe. "
          + "John Aaron in Mission Control remembered this fault from a test a year earlier. Apollo 12 flew on and landed on the Moon.\n\n"
          + "Why did lightning strike at all? You launched into rain clouds, as Apollo 12 did. A Saturn V with its long trail of hot exhaust "
          + "is a 2 km wire pointing into a charged cloud: it triggers the lightning itself. After Apollo 12, NASA stopped launching into such clouds.",
            () => { climb.Paused = false; climb.HoldRealTime = false; Shader.SetGlobalFloat("_LightningFlash", 0f); alarmLight.enabled = false; flashLight.enabled = false; });
    }

    void Lose(string title, bool abort, string what, string why)
    {
        stage = Stage.Lost;
        alarm.Stop(); ring.gameObject.SetActive(false);
        Problems.I.PressureOff();
        climb.Paused = true;
        var c = Consequences.On(fd);
        System.Action card = () => Problems.I.Fail(title, what, why, Rewind);
        StartCoroutine(abort ? c.Abort(climb, card) : c.Blackout(card));
    }

    // back to just before the strike: it happens again, and this time you know more
    void Rewind()
    {
        Consequences.On(fd).Restore(climb);
        climb.Rewind(StrikeMet - 1.5f);
        climb.HoldRealTime = true;
        fd.Garble(false);
        stage = Stage.Waiting; ringShown = false; capcomSaid = false;
        fd.Explain("REWIND: back to T+35 s. The strike comes again.", 4f);
    }

    // a ring on a control (a late hint)
    void Ring(CabinLabel target, string label)
    {
        if (target == null || fd.Outside) { ring.gameObject.SetActive(false); return; }
        Vector3 s = fd.Cam.WorldToScreenPoint(target.transform.position);
        ring.gameObject.SetActive(s.z > 0f);
        float scale = ring.GetComponentInParent<Canvas>().scaleFactor;
        ring.anchoredPosition = (Vector2)s / scale;
        ring.localScale = Vector3.one * (1f + 0.15f * Mathf.Sin(Time.time * 6f));
        ringLabel.text = label;
    }

    // the self-test does it right
    void AutoPlay()
    {
        if (stage == Stage.Act && stageT > 2f) Use("SCE POWER");
        else if (stage == Stage.FuelCells && stageT > 2f) Use("FUEL CELL RESET");
    }

    // a jagged bolt from the rocket down along its trail to the ground (world space)
    void DrawBolt()
    {
        var top = climb.Stack.position - climb.Stack.up * 60f;
        var ground = new Vector3(top.x - 40f, top.y - climb.AltitudeM - 40f, top.z + 120f);
        const int n = 40;
        bolt.positionCount = n;
        var rnd = new System.Random(12);
        for (int i = 0; i < n; i++)
        {
            float k = (float)i / (n - 1);
            Vector3 p = Vector3.Lerp(top, ground, k);
            if (i > 0 && i < n - 1) p += new Vector3((float)rnd.NextDouble() - 0.5f, 0f, (float)rnd.NextDouble() - 0.5f) * 90f;
            bolt.SetPosition(i, p);
        }
    }

    // the Apollo master alarm: a harsh two-tone warble
    public static AudioClip AlarmTone()
    {
        const int sr = 44100; int n = sr;
        var d = new float[n];
        for (int i = 0; i < n; i++)
        {
            float tt = (float)i / sr;
            float hz = (tt % 0.5f) < 0.25f ? 750f : 2000f;
            d[i] = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * hz * tt)) * 0.25f;
        }
        var c = AudioClip.Create("Master_Alarm", n, 1, sr, false); c.SetData(d, 0); return c;
    }

    static AudioClip Thunder()
    {
        const int sr = 44100; int n = sr * 2;
        var d = new float[n]; var rnd = new System.Random(5); float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float tt = (float)i / sr;
            lp += (((float)rnd.NextDouble() * 2f - 1f) - lp) * 0.08f;
            d[i] = lp * 3.5f * Mathf.Exp(-tt * 2.2f) * Mathf.Min(1f, tt * 400f) + ((tt < 0.05f) ? ((float)rnd.NextDouble() - 0.5f) * Mathf.Exp(-tt * 60f) : 0f);
        }
        var c = AudioClip.Create("Lightning_Crack", n, 1, sr, false); c.SetData(d, 0); return c;
    }
}
