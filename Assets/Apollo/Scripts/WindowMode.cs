// Window behaviour for the Windows build: opens as a maximized, resizable window (taskbar visible);
// F11 switches between that and borderless full screen. Self-tests keep their own window size.
using UnityEngine;

public class WindowMode : MonoBehaviour
{
    static bool testing;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        var args = System.Environment.GetCommandLineArgs();
        testing = System.Array.IndexOf(args, "-autotest") >= 0 || System.Array.IndexOf(args, "-flighttest") >= 0;
        var go = new GameObject("WindowMode");
        DontDestroyOnLoad(go);
        go.AddComponent<WindowMode>();
        if (!testing && !Application.isEditor) Maximize();
    }

    static void Maximize()
    {
        var d = Display.main;
        Screen.SetResolution(d.systemWidth, d.systemHeight, FullScreenMode.MaximizedWindow);
    }

    void Update()
    {
        if (testing || !Input.GetKeyDown(KeyCode.F11)) return;
        if (Screen.fullScreenMode == FullScreenMode.FullScreenWindow) Maximize();
        else Screen.SetResolution(Display.main.systemWidth, Display.main.systemHeight, FullScreenMode.FullScreenWindow);
    }
}
