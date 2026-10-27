using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>Command-line builds while the editor is closed (training runs):
///   Unity.exe -batchmode -quit -projectPath . -executeMethod BuildCli.Windows [-buildPath Build/PoRace/PoRace.exe]
///                                                     [-scenes Assets/Scenes/Testbed.unity,...]
/// Builds every enabled scene in the build settings (or only -scenes, first = start scene). Exits non-zero on failure.</summary>
public static class BuildCli {
  public static void Windows() {
    string path = "Build/PoRace/PoRace.exe";
    var args = System.Environment.GetCommandLineArgs();
    string[] only = null;
    for (int i = 0; i + 1 < args.Length; i++) { if (args[i] == "-buildPath") path = args[i + 1]; if (args[i] == "-scenes") only = args[i + 1].Split(','); }
    var scenes = only ?? EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
    var report = BuildPipeline.BuildPlayer(scenes, path, BuildTarget.StandaloneWindows64, BuildOptions.None);
    Debug.Log($"[BuildCli] {path}: {report.summary.result}, {report.summary.totalErrors} errors, scenes: {string.Join(", ", scenes)}");
    EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
  }
}
