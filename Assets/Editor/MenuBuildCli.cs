using UnityEditor;

/// <summary>Headless menu rebuild: Unity.exe -batchmode -quit -projectPath . -executeMethod MenuBuildCli.Run</summary>
public static class MenuBuildCli {
  public static void Run() { MenuBuilder.Build(); EditorApplication.Exit(0); }
}
