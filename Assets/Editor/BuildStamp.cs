using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

/// <summary>Writes Assets/Resources/BuildStamp.txt (YYMMDD) before every player build, for the HUD watermark.</summary>
class BuildStamp : IPreprocessBuildWithReport {
  public int callbackOrder => 0;
  public void OnPreprocessBuild(BuildReport report) {
    Directory.CreateDirectory("Assets/Resources");
    File.WriteAllText("Assets/Resources/BuildStamp.txt", System.DateTime.Now.ToString("yyMMdd"));
    AssetDatabase.ImportAsset("Assets/Resources/BuildStamp.txt", ImportAssetOptions.ForceSynchronousImport);
  }
}
