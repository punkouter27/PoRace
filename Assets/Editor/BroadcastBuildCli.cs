using UnityEditor;

/// <summary>Headless broadcast-layer rebuild of every race scene:
///   Unity.exe -batchmode -quit -projectPath . -executeMethod BroadcastBuildCli.Run</summary>
public static class BroadcastBuildCli {
  public static void Run() { BroadcastBuilder.BuildAll(); EditorApplication.Exit(0); }
}
