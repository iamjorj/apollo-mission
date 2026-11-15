// Windows build: Builds/Windows/ApolloMission.exe (or the folder given with -buildPath <dir>)
// Headless: -executeMethod ApolloBuild.BuildWindows
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class ApolloBuild
{
    [MenuItem("Apollo/Build Windows Player")]
    public static void BuildWindows()
    {
        string dir = "Builds/Windows";
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-buildPath") dir = args[i + 1];

        // a normal resizable window that opens maximized (taskbar visible); F11 for full screen
        PlayerSettings.fullScreenMode = FullScreenMode.MaximizedWindow;
        PlayerSettings.resizableWindow = true;
        PlayerSettings.allowFullscreenSwitch = true;
        PlayerSettings.bakeCollisionMeshes = true;     // the Columbia scan's colliders (instrument names) are cooked at build time

        var opts = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Apollo/Scenes/MissionPlanner.unity", "Assets/Apollo/Scenes/Flight.unity" },
            locationPathName = dir + "/ApolloMission.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        };
        var report = BuildPipeline.BuildPlayer(opts);
        Debug.Log("APOLLO BUILD " + report.summary.result + " " + report.summary.totalSize / (1024 * 1024) + " MB, " +
                  report.summary.totalErrors + " errors");
        if (report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
    }
}
