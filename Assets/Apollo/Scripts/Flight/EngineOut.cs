// The other climb problem, Apollo 13's (11 April 1970): at 5 min 30.6 s the second stage's center engine shut itself
// down about 2 min 12 s early, after violent "pogo" vibration. The Saturn's instrument unit burned the other four J-2
// engines about 34 s longer and the third stage about 9 s longer, and Apollo 13 reached its planned orbit.
// First the choice, under a clock while the rocket keeps flying (a wrong one is played out, then rewound to the
// choice); then the hands-on part: press the real MASTER ALARM to acknowledge and keep going.
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class EngineOut : MonoBehaviour
{
    public const float CutMet = 330.6f;
    FlightDirector fd;
    Ascent climb;
    bool slowed, cut, done, lost, acking;
    float since;
    AudioSource alarm;
    RectTransform enginePanel;
    Image[] lights = new Image[5];
    TextMeshProUGUI engineText;
    Transform panel, seats;
    public float GDrop { get; private set; }
    Image alarmButton; bool alarmPressed;

    public void Begin(FlightDirector director, Ascent a)
    {
        fd = director; climb = a;
        alarm = gameObject.AddComponent<AudioSource>();
        alarm.clip = Lightning.AlarmTone(); alarm.loop = true; alarm.playOnAwake = false; alarm.volume = 0.12f; alarm.spatialBlend = 0f;
        foreach (var l in FindObjectsOfType<CabinLabel>(true))
        {
            if (l.Title == "MAIN DISPLAY CONSOLE") panel = l.transform.parent;
            if (l.Title == "HAND CONTROLLERS") seats = l.transform.parent;
        }
    }

    void Update()
    {
        if (climb == null) return;
        float m = climb.Met;
        if (cut) { GDrop = Mathf.MoveTowards(GDrop, m < 548f ? 0.25f : 0f, Time.deltaTime * 0.8f); climb.GCut = GDrop; }
        if (done || lost) return;
        if (!slowed && m > 296f) { slowed = true; climb.HoldRealTime = true; }             // real time for what comes next
        if (!cut && m >= CutMet) Shutdown();
        if (!cut) return;
        since += Time.deltaTime;
        if (lights[4]) lights[4].color = new Color(1f, 1f, 1f, Mathf.Repeat(Time.time, 0.7f) < 0.4f ? 1f : 0.25f);
        if (!acking) return;
        // the hands-on part: acknowledge with the real master alarm button
        if (Problems.I.Auto && since > 1.5f) { Keep(); return; }
        if (since > 12f) fd.Explain("Press the blinking MASTER ALARM button under the engine panel, or M.", 2f);
        if ((Input.GetMouseButtonDown(0) && fd.Hovered == "MASTER ALARM") || Input.GetKeyDown(KeyCode.M) || alarmPressed) { alarmPressed = false; Keep(); return; }
        if (alarmButton) alarmButton.color = new Color(1f, 1f, 1f, Mathf.Repeat(Time.time, 0.8f) < 0.4f ? 1f : 0.35f);
        if (Input.GetMouseButtonDown(0) && fd.Hovered == "ABORT HANDLE") Abort("ABORT", "You decided to keep going, then turned the abort handle anyway. Columbia's engine pushes it off the rocket and Apollo 13 never reaches orbit.", "Trust what you read: four engines and good steering mean the computer can still make orbit.");
    }

    void Shutdown()
    {
        cut = true; since = 0f;
        Problems.I.Busy = true;
        alarm.Play();
        if (panel) { Problems.I.MasterAlarm(fd, panel); Problems.I.AbortHandle(fd, seats ? seats : panel); }
        fd.AscentEvent("S-II ENGINE 5 OUT", "A shudder, a jolt, and the push gets weaker: the center engine has shut itself down. This happened to Apollo 13.");
        ShowEngines(true);
        Problems.I.Ask("CENTER ENGINE OUT  ·  APOLLO 13",
            "5 and a half minutes after liftoff the rocket shakes hard, then one of the five engines on the second stage switches itself off. The other four are still pushing. What do you do?",
            new[] { "Engine panel: engine 5 (the middle one) is out, 1 to 4 are burning",
                    "You feel less push: from 1.4 g down to about 1.1 g",
                    "The rocket's computer is still steering, and says orbit can still be reached" },
            new (string, bool, string)[]
            {
                ("ABORT NOW", false, ""),
                ("KEEP GOING: THE COMPUTER WILL BURN THE OTHER FOUR LONGER", true, "Four engines are enough: the computer simply burns them longer. Now tell the computer you've seen the alarm: press the MASTER ALARM button on the panel."),
                ("STEER THE ROCKET BY HAND", false, ""),
                ("SHUT DOWN ANOTHER ENGINE TO BALANCE IT", false, ""),
            }, 20f,
            () => { acking = true; since = 0f; ShowEngines(true); fd.Explain("Press MASTER ALARM to say: seen it, keep going. It blinks under the engine panel (or press M).", 10f); },
            i =>
            {
                if (i == 0) Abort("ABORT", "You aborted. Columbia's engine pushes it off the rocket, and Apollo 13 comes down in the ocean, never reaching orbit.",
                    "Four engines out of five were still burning and the computer was still steering. One engine out is not a reason to give up.");
                else if (i == 2) Abort("OFF COURSE", "You took over the steering by hand. A person can't steer a Saturn V as precisely as its computer: the rocket wanders off its path and Houston has to call an abort.",
                    "The rocket's own computer was built for exactly this: it knew how to fly to orbit with four engines.");
                else Abort("NOT ENOUGH PUSH", "You switched off a second engine. With only three left, the second stage can't push hard enough to reach orbit: Houston calls an abort.",
                    "The middle engine sits right on the rocket's center line, so losing it doesn't make the rocket lopsided. There was nothing to balance.");
            },
            () => { fd.Explain("CAPCOM: \"Apollo 13, Houston. We show you GO, four engines.\" Press MASTER ALARM (or M).", 8f); acking = true; since = 0f; ShowEngines(true); });
    }

    void ShowEngines(bool on)
    {
        if (enginePanel == null)
        {
            var hud = fd.GetComponentInChildren<Canvas>().transform;
            enginePanel = UiKit.Rect(hud, "Engines", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(36, 60), new Vector2(330, 196));
            UiKit.Box(enginePanel, new Color(0.02f, 0.022f, 0.026f, 0.92f), 12);
            UiKit.OutlineBox(UiKit.Full(enginePanel, "Edge"), new Color(1f, 1f, 1f, 0.16f));
            UiKit.Label(enginePanel, "SECOND STAGE ENGINES", 11, UiKit.Text2, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -14), new Vector2(300, 16), TextAlignmentOptions.Center, true, 3);
            // the five J-2s as seen from below: four in a square, number 5 in the middle
            Vector2[] at = { new Vector2(-38, 28), new Vector2(38, 28), new Vector2(38, -36), new Vector2(-38, -36), new Vector2(0, -4) };
            for (int i = 0; i < 5; i++)
            {
                var r = UiKit.Rect(enginePanel, "Engine" + (i + 1), new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), at[i] + new Vector2(0, -82), new Vector2(30, 30));
                var ring = r.gameObject.AddComponent<Image>(); ring.sprite = UiKit.Ring(64); ring.color = Color.white; ring.raycastTarget = false;
                lights[i] = UiKit.Box(UiKit.Rect(r, "Lit", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(20, 20)), Color.white, 10);
                UiKit.Label(r, (i + 1).ToString(), 10, UiKit.Ink, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(20, 14), TextAlignmentOptions.Center, true, 0);
            }
            engineText = UiKit.Label(enginePanel, "", 11, UiKit.Text, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 12), new Vector2(300, 48), TextAlignmentOptions.Center, false, 1);
            engineText.lineSpacing = 8;
            // the MASTER ALARM pushbutton, as on the main panel: it blinks until you press it (or M)
            var b = UiKit.Rect(enginePanel, "MasterAlarm", new Vector2(0.5f, 0), new Vector2(0.5f, 1), new Vector2(0, -10), new Vector2(300, 46));
            UiKit.Button(b, new Color(1f, 1f, 1f, 0.001f), 10, () => { if (acking) alarmPressed = true; });
            alarmButton = UiKit.OutlineBox(UiKit.Full(b, "Edge"), Color.white);
            UiKit.Label(b, "MASTER ALARM  ·  PRESS  (M)", 13, UiKit.Text, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300, 24), TextAlignmentOptions.Center, true, 3);
        }
        enginePanel.gameObject.SetActive(on);
        if (alarmButton) alarmButton.transform.parent.gameObject.SetActive(on && acking);
        if (on)
        {
            for (int i = 0; i < 4; i++) lights[i].color = new Color(1f, 1f, 1f, 0.12f);
            engineText.text = "ENGINE 5 LIGHT ON: THE MIDDLE ENGINE IS OUT · 4 OF 5 BURNING · STILL STEERING";
        }
    }

    void Keep()
    {
        done = true; acking = false; Problems.I.Busy = false;
        alarm.Stop(); ShowEngines(false);
        climb.Paused = true;
        Problems.I.Info("KEEP GOING  ·  WHY IT WORKED",
            "One engine out of five stopped, but the other four were fine. The Saturn's own computer simply burned them 34 seconds longer, and the third stage 9 seconds longer, and Apollo 13 reached its planned orbit.  "
          + "The middle engine sits on the rocket's center line, so losing it doesn't push the rocket off course. "
          + "It had shut down because of \"pogo\", a violent bouncing that could have broken it: stopping it was the safe thing.",
            () => { climb.Paused = false; climb.HoldRealTime = false; });
    }

    void Abort(string title, string what, string why)
    {
        lost = true; acking = false;
        alarm.Stop(); ShowEngines(false);
        climb.Paused = true;
        StartCoroutine(Consequences.On(fd).Abort(climb, () => Problems.I.Fail(title, what, why, Rewind), "ABORT · Columbia's own engine pushes it off the rocket"));
    }

    // back to just before the shutdown: it happens again, and the choice comes again
    void Rewind()
    {
        Consequences.On(fd).Restore(climb);
        climb.Rewind(CutMet - 3f);
        climb.HoldRealTime = true;
        climb.GCut = 0f; GDrop = 0f;
        cut = false; lost = false; done = false; acking = false;
        fd.Explain("REWIND: back to 5 min 27 s. The engine shuts down again.", 4f);
    }
}
