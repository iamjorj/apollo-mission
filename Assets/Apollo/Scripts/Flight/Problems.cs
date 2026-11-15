// The problems on the way to the Moon: each run picks real Apollo emergencies (so two runs differ), and each one
// is worked the same way: notice it (alarms, lights), read the clues (the real indicators), choose what to do (real
// options, some wrong), see the consequence of a wrong choice and try again, then learn what really happened.
// Mission Control decides if you take too long. Everything is scored for the mission report at the end.
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class Problems : MonoBehaviour
{
    public static Problems I;
    public bool Busy;                                      // a hands-on problem is going on (no card)
    public int Fails;
    FlightDirector fd;
    RectTransform hud, card, optionsRoot, timerFill, resultPlate;
    TextMeshProUGUI titleText, situationText, cluesText, resultText, timerText;
    readonly List<(RectTransform rt, TextMeshProUGUI label, Image fill)> buttons = new List<(RectTransform, TextMeshProUGUI, Image)>();
    TextMeshProUGUI cluesHead, hintText;

    // this run
    public bool LightningRun { get; private set; }        // climb: Apollo 12 lightning, else Apollo 13 engine out
    public bool LatchFailure { get; private set; }        // docking: Apollo 14's capture latches
    public bool Auto;                                      // the self-test answers by itself
    bool testWrong = Array.IndexOf(Environment.GetCommandLineArgs(), "-wrong") >= 0;

    // the score
    public int Wrong, Hints, Timeouts, Solved, Asked;
    public float DockSpeed = -1f, DockOffset = -1f, ProLate = 0f; public int DockTries, DockAuto;
    readonly List<string> report = new List<string>();
    public void Note(string line) => report.Add(line);

    // the question being asked
    (string label, bool right, string result)[] opts;
    bool[] tried;
    Action onDone;
    float timeLeft, timeTotal;
    bool showingResult, resultRight;
    public bool Deciding => card != null && card.gameObject.activeSelf;

    public void Begin(FlightDirector director, RectTransform hudRoot)
    {
        I = this; fd = director; hud = hudRoot;
        var r = new System.Random(Environment.TickCount);
        // lightning only comes from launching into rain clouds (the real NASA weather of the launch day, Weather.cs);
        // otherwise the climb's problem is Apollo 13's engine failure
        LightningRun = Weather.For(MissionState.LaunchUtc).RainClouds;
        LatchFailure = r.NextDouble() < 0.5;
        foreach (var a in Environment.GetCommandLineArgs())                    // the self-test can ask for a run
        {
            if (a == "-lightning") LightningRun = true;
            if (a == "-engineout") LightningRun = false;
            if (a == "-latches") LatchFailure = true;
        }
        Build();
        Debug.Log($"PROBLEMS this run: climb {(LightningRun ? "Apollo 12 lightning" : "Apollo 13 engine out")}, docking latches {(LatchFailure ? "fail (Apollo 14)" : "fine")}");
    }

    // ---------------- asking ----------------

    // a choice under a clock. A wrong choice is played out by the problem (onWrong) instead of "try again" here;
    // running out of time is played out too (onTimeout), or Mission Control picks the right answer
    Action<int> onWrongChoice; Action onTimeoutChoice;
    public void Ask(string title, string situation, string[] clues, (string label, bool right, string result)[] options, float seconds, Action done,
                    Action<int> onWrong = null, Action onTimeout = null)
    {
        Asked++; infoMode = false; onPick = null; Kind("PROBLEM");
        onWrongChoice = onWrong; onTimeoutChoice = onTimeout;
        opts = options; tried = new bool[options.Length]; onDone = done;
        timeLeft = timeTotal = seconds; showingResult = false;
        titleText.text = title;
        situationText.text = situation;
        cluesText.text = string.Join("\n", Array.ConvertAll(clues, c => "<color=#6b737b>—</color>  " + c));
        cluesHead.gameObject.SetActive(clues.Length > 0);
        for (int i = 0; i < buttons.Count; i++)
        {
            bool on = i < options.Length;
            buttons[i].rt.gameObject.SetActive(on);
            if (on) buttons[i].label.text = $"{i + 1}    {options[i].label}";
        }
        hintText.text = options.Length > 1 ? "CLICK OR PRESS 1 - " + options.Length : "CLICK OR PRESS 1";
        resultPlate.gameObject.SetActive(false);
        optionsRoot.gameObject.SetActive(true);
        Layout();
        card.SetAsLastSibling();
        card.gameObject.SetActive(true);
    }

    // a choice with no right or wrong answer (a checklist setting): what it does shows later
    Action<int> onPick; int pickDefault;
    public void Pick(string title, string situation, string[] clues, string[] options, float seconds, Action<int> picked, int onTimeout = 0)
    {
        var o = new (string, bool, string)[options.Length];
        for (int i = 0; i < options.Length; i++) o[i] = (options[i], true, "");
        Ask(title, situation, clues, o, seconds, null);
        onPick = picked; pickDefault = onTimeout; Kind("CHOICE");
    }
    bool TakePick(int i)
    {
        if (onPick == null) return false;
        card.gameObject.SetActive(false);
        var p = onPick; onPick = null; opts = null;
        p(i);
        return true;
    }

    // a card that only tells something (what is going on and why), then continues
    TextMeshProUGUI kindText;                               // the small word over the title: PROBLEM, CHOICE, NOTE, MISSION LOST
    void Kind(string k) { if (kindText) kindText.text = k; }
    bool infoMode;
    public void Info(string title, string text, Action done)
    {
        infoMode = true; Kind("");
        opts = new (string, bool, string)[] { ("CONTINUE", true, text) }; tried = new bool[1]; onDone = done;
        timeLeft = timeTotal = 9999f; showingResult = true; resultRight = true;
        titleText.text = title; situationText.text = ""; cluesText.text = ""; cluesHead.gameObject.SetActive(false);
        resultText.text = text; hintText.text = "SPACE  CONTINUE";
        timerText.text = ""; timerFill.localScale = new Vector3(0f, 1f, 1f);
        resultPlate.gameObject.SetActive(true); optionsRoot.gameObject.SetActive(false);
        Layout();
        card.SetAsLastSibling();
        card.gameObject.SetActive(true);
    }

    // two real controls that several problems share: made once, on first need
    bool alarmMade, abortMade;
    public void MasterAlarm(FlightDirector fd, Transform panel)
    {
        if (alarmMade || panel == null) return; alarmMade = true;
        fd.AddControl("MASTER ALARM", "Press to silence the alarm and say: seen it.", panel, new Vector3(51.044f, 204.854f, 34.719f));
    }
    public void AbortHandle(FlightDirector fd, Transform parent)
    {
        if (abortMade || parent == null) return; abortMade = true;
        fd.AddControl("ABORT HANDLE", "Turn it to abort: the spacecraft is pulled off the rocket.", parent, new Vector3(32.068f, 185.314f, 33.464f));
    }

    // a wrong action played out: what happened, why, then back to the moment the problem began
    public void Fail(string title, string what, string why, Action rewind)
    {
        Fails++; Note("LOST: " + title);
        infoMode = true; Kind("");
        opts = new (string, bool, string)[] { ("REWIND", true, "") }; tried = new bool[1]; onDone = rewind;
        timeLeft = timeTotal = 9999f; showingResult = true; resultRight = true;
        titleText.text = "MISSION LOST  ·  " + title; situationText.text = what; cluesText.text = ""; cluesHead.gameObject.SetActive(false);
        resultText.text = "<b>WHY</b>\n" + why;
        hintText.text = "SPACE  REWIND AND TRY AGAIN";
        timerText.text = ""; timerFill.localScale = new Vector3(0f, 1f, 1f);
        resultPlate.gameObject.SetActive(true); optionsRoot.gameObject.SetActive(false);
        Layout();
        card.SetAsLastSibling();
        card.gameObject.SetActive(true);
    }

    // a live clock at the top of the screen: what happens when it runs out
    RectTransform pressure, pressureFill; TextMeshProUGUI pressureText;
    public void Pressure(string label, float left, float total)
    {
        if (pressure == null)
        {
            pressure = UiKit.Rect(hud, "Pressure", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -30), new Vector2(760, 50));
            UiKit.Box(pressure, new Color(0.02f, 0.022f, 0.026f, 0.92f), 10);
            UiKit.OutlineBox(UiKit.Full(pressure, "Edge"), new Color(1f, 1f, 1f, 0.2f));
            pressureText = UiKit.Label(pressure, "", 13, UiKit.Text, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -8), new Vector2(740, 22), TextAlignmentOptions.Center, true, 3);
            var track = UiKit.Rect(pressure, "Track", new Vector2(0.5f, 0), new Vector2(0.5f, 0.5f), new Vector2(0, 12), new Vector2(720, 2));
            UiKit.Box(track, new Color(1f, 1f, 1f, 0.15f), 0);
            pressureFill = UiKit.Rect(track, "Fill", new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(720, 2));
            UiKit.Box(pressureFill, Color.white, 0);
        }
        pressure.gameObject.SetActive(true);
        int s = Mathf.CeilToInt(Mathf.Max(left, 0f));
        bool urgent = left < total * 0.3f && Mathf.Repeat(Time.unscaledTime, 0.6f) < 0.3f;
        pressureText.text = $"{label}   {s / 60}:{s % 60:00}";
        pressureText.alpha = urgent ? 0.45f : 1f;
        pressureFill.localScale = new Vector3(Mathf.Clamp01(left / Mathf.Max(total, 0.01f)), 1f, 1f);
    }
    public void PressureOff() { if (pressure) pressure.gameObject.SetActive(false); }

    void Choose(int i)
    {
        if (showingResult || opts == null || i >= opts.Length || tried[i]) return;
        if (TakePick(i)) return;
        tried[i] = true;
        var o = opts[i];
        if (!o.right && onWrongChoice != null)
        {
            // the problem shows what this choice does, then rewinds to this card
            Wrong++; card.gameObject.SetActive(false);
            var w = onWrongChoice; onWrongChoice = null; onTimeoutChoice = null; opts = null;
            w(i);
            return;
        }
        showingResult = true; resultRight = o.right;
        if (!o.right) { Wrong++; Note($"WRONG: {o.label}"); }
        resultText.text = (o.right ? "<b>RIGHT</b>\n" : "<b>WRONG</b>\n") + o.result;
        hintText.text = o.right ? "SPACE  CONTINUE" : "SPACE  TRY AGAIN";
        resultPlate.gameObject.SetActive(true);
        optionsRoot.gameObject.SetActive(false);
        Layout();
    }

    void Continue()
    {
        if (!showingResult) return;
        if (resultRight)
        {
            if (!infoMode) Solved++;
            infoMode = false;
            card.gameObject.SetActive(false);
            var d = onDone; onDone = null; opts = null;
            d?.Invoke();
            return;
        }
        // try again: the wrong answer is crossed out
        showingResult = false;
        hintText.text = "CLICK OR PRESS 1 - " + opts.Length;
        resultPlate.gameObject.SetActive(false);
        optionsRoot.gameObject.SetActive(true);
        Layout();
    }

    void Update()
    {
        if (!Deciding) return;
        if (!showingResult)
        {
            for (int k = 0; k < 4; k++) if (Input.GetKeyDown(KeyCode.Alpha1 + k) || Input.GetKeyDown(KeyCode.Keypad1 + k)) Choose(k);
            timeLeft -= Time.unscaledDeltaTime;
            if (onPick != null && (timeLeft <= 0f || (Auto && timeTotal - timeLeft > 2.5f))) { TakePick(pickDefault); return; }
            if (Auto && timeTotal - timeLeft > 2.5f)
            {
                // tests: "-wrong" makes the self-test pick a wrong answer once, to see what it does
                if (testWrong && onWrongChoice != null) { testWrong = false; for (int k = 0; k < opts.Length; k++) if (!opts[k].right) { Choose(k); break; } }
                else for (int k = 0; k < opts.Length; k++) if (opts[k].right) { Choose(k); break; }
            }
            else if (timeLeft <= 0f && onTimeoutChoice != null)
            {
                Timeouts++; card.gameObject.SetActive(false);
                var w = onTimeoutChoice; onWrongChoice = null; onTimeoutChoice = null; opts = null;
                w();
                return;
            }
            else if (timeLeft <= 0f)
            {
                // too slow: Mission Control makes the call
                Timeouts++; Note("TIMEOUT: Mission Control decided");
                for (int k = 0; k < opts.Length; k++) if (opts[k].right)
                {
                    tried[k] = true; showingResult = true; resultRight = true;
                    resultText.text = "<b>TOO SLOW</b>\nMission Control decided for you: " + opts[k].label + ". " + opts[k].result;
                    hintText.text = "SPACE  CONTINUE";
                    resultPlate.gameObject.SetActive(true); optionsRoot.gameObject.SetActive(false); Layout();
                    break;
                }
            }
            // the row under the mouse turns white; a tried wrong answer stays faded
            for (int i = 0; i < buttons.Count; i++)
            {
                if (!buttons[i].rt.gameObject.activeSelf) continue;
                bool used = tried != null && i < tried.Length && tried[i];
                bool over = !used && RectTransformUtility.RectangleContainsScreenPoint(buttons[i].rt, Input.mousePosition, null);
                buttons[i].fill.color = new Color(1f, 1f, 1f, over ? 1f : 0f);
                buttons[i].label.color = over ? UiKit.Ink : used ? new Color(1f, 1f, 1f, 0.25f) : UiKit.Text;
            }
            float f = Mathf.Clamp01(timeLeft / Mathf.Max(timeTotal, 0.01f));
            timerFill.localScale = new Vector3(f, 1f, 1f);
            timerText.text = $"{Mathf.CeilToInt(Mathf.Max(timeLeft, 0f))} S";
        }
        else if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetMouseButtonDown(0) || (Auto && Time.frameCount % 90 == 0))
            Continue();
    }

    // ---------------- the card ----------------

    void Build()
    {
        // black, white and grey only: a thin outline, monospace text, outlined answers that turn white under the mouse
        card = UiKit.Rect(hud, "ProblemCard", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 70), new Vector2(820, 520));
        UiKit.Box(card, new Color(0.02f, 0.022f, 0.026f, 0.97f), 14);
        UiKit.OutlineBox(UiKit.Full(card, "Border"), new Color(1f, 1f, 1f, 0.16f));
        kindText = UiKit.Label(card, "PROBLEM", 11, UiKit.Text2, new Vector2(0, 1), new Vector2(0, 1), new Vector2(36, -26), new Vector2(300, 16), TextAlignmentOptions.MidlineLeft, true, 5);
        titleText = UiKit.Label(card, "", 17, UiKit.Text, new Vector2(0, 1), new Vector2(0, 1), new Vector2(36, -46), new Vector2(620, 26), TextAlignmentOptions.MidlineLeft, true, 3);
        timerText = UiKit.Label(card, "", 12, UiKit.Text2, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-36, -26), new Vector2(120, 16), TextAlignmentOptions.MidlineRight, true, 3);
        var track = UiKit.Rect(card, "TimerTrack", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -84), new Vector2(748, 1));
        UiKit.Box(track, new Color(1f, 1f, 1f, 0.10f), 0);
        timerFill = UiKit.Rect(track, "Fill", new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(748, 1));
        UiKit.Box(timerFill, new Color(1f, 1f, 1f, 0.75f), 0);
        situationText = UiKit.Label(card, "", 15, UiKit.Text, new Vector2(0, 1), new Vector2(0, 1), new Vector2(36, -100), new Vector2(748, 80), TextAlignmentOptions.TopLeft, false, 0);
        situationText.lineSpacing = 10;
        cluesHead = UiKit.Label(card, "WHAT YOU SEE", 11, UiKit.Text2, new Vector2(0, 1), new Vector2(0, 1), new Vector2(36, -170), new Vector2(300, 16), TextAlignmentOptions.MidlineLeft, true, 4);
        cluesText = UiKit.Label(card, "", 13, new Color(0.84f, 0.86f, 0.88f), new Vector2(0, 1), new Vector2(0, 1), new Vector2(36, -190), new Vector2(748, 140), TextAlignmentOptions.TopLeft, false, 0);
        cluesText.lineSpacing = 16;
        optionsRoot = UiKit.Rect(card, "Options", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 60), new Vector2(748, 220));
        for (int i = 0; i < 4; i++)
        {
            int k = i;
            var b = UiKit.Rect(optionsRoot, "Option" + i, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -i * 52), new Vector2(748, 44));
            UiKit.Button(b, new Color(1f, 1f, 1f, 0f), 10, () => Choose(k));
            var fill = b.GetComponent<Image>();
            var edge = UiKit.Full(b, "Edge"); UiKit.OutlineBox(edge, new Color(1f, 1f, 1f, 0.30f));
            var t = UiKit.Label(b, "", 13, UiKit.Text, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(20, 0), new Vector2(712, 30), TextAlignmentOptions.MidlineLeft, true, 2);
            buttons.Add((b, t, fill));
        }
        resultPlate = UiKit.Rect(card, "Result", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 60), new Vector2(748, 200));
        UiKit.Box(resultPlate, new Color(1f, 1f, 1f, 0.04f), 10);
        UiKit.OutlineBox(UiKit.Full(resultPlate, "Edge"), new Color(1f, 1f, 1f, 0.22f));
        resultText = UiKit.Label(resultPlate, "", 15, UiKit.Text, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(704, 180), TextAlignmentOptions.MidlineLeft, false, 0);
        resultText.lineSpacing = 10;
        var clickCatcher = resultPlate.gameObject.AddComponent<Button>();
        clickCatcher.onClick.AddListener(Continue);
        resultPlate.GetComponent<Image>().raycastTarget = true;
        hintText = UiKit.Label(card, "", 11, UiKit.Text2, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 24), new Vector2(748, 16), TextAlignmentOptions.Center, true, 4);
        card.gameObject.SetActive(false);
    }

    // the card grows to fit its text
    void Layout()
    {
        float sit = situationText.GetPreferredValues(situationText.text, 748, 0).y;
        float clu = cluesText.GetPreferredValues(cluesText.text, 748, 0).y;
        bool hasClues = cluesHead.gameObject.activeSelf;
        cluesHead.rectTransform.anchoredPosition = new Vector2(36, -100 - sit - 22);
        cluesText.rectTransform.anchoredPosition = new Vector2(36, -100 - sit - 44);
        cluesText.rectTransform.sizeDelta = new Vector2(748, clu + 4);
        float top = 100 + sit + (hasClues ? 44 + clu : 0) + 28;
        float bottom;
        if (optionsRoot.gameObject.activeSelf)
        {
            int n = 0; foreach (var b in buttons) if (b.rt.gameObject.activeSelf) n++;
            bottom = n * 52 + 60;
            optionsRoot.sizeDelta = new Vector2(748, n * 52);
        }
        else
        {
            float h = resultText.GetPreferredValues(resultText.text, 704, 0).y + 40;
            resultPlate.sizeDelta = new Vector2(748, h);
            bottom = h + 66;
        }
        card.sizeDelta = new Vector2(820, top + bottom);
    }

    // ---------------- the mission report ----------------

    public int Score()
    {
        int s = 1000 - Wrong * 100 - Hints * 30 - Timeouts * 120 - DockAuto * 150 - Fails * 120;
        if (DockSpeed >= 0f) s -= Mathf.RoundToInt(Mathf.Max(0f, DockSpeed - 0.3f) * 300f + DockOffset * 200f);
        s -= Mathf.RoundToInt(Mathf.Max(0f, ProLate) * 10f);
        s -= (DockTries > 1 ? (DockTries - 1) * 20 : 0);
        return Mathf.Clamp(s, 0, 1000);
    }

    public string Report()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<b>MISSION REPORT</b>");
        sb.AppendLine($"Missions lost and rewound   {Fails}");
        sb.AppendLine($"Problems solved        {Solved} of {Asked}");
        sb.AppendLine($"Wrong choices          {Wrong}");
        sb.AppendLine($"Hints used             {Hints}");
        if (Timeouts > 0) sb.AppendLine($"Too slow (Houston)     {Timeouts}");
        if (DockSpeed >= 0f) sb.AppendLine($"Docking                {DockSpeed:0.00} m/s, {DockOffset * 100f:0} cm off, {DockTries} {(DockTries == 1 ? "try" : "tries")}{(DockAuto > 0 ? ", autopilot" : "")}");
        sb.AppendLine($"Moon burn (PRO)        {(ProLate <= 0f ? "on time" : $"{ProLate:0.0} s late")}");
        sb.AppendLine();
        sb.Append($"<b>SCORE   {Score()} / 1000</b>");
        return sb.ToString();
    }
}
