using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace PoRace {

/// <summary>The one command-line reader. Environment.GetCommandLineArgs() is cached on first use (safe from static field
/// initializers). Values parse with InvariantCulture; the LAST occurrence of a flag wins. A flag with no following
/// value, or a value that does not parse, returns false and logs one warning naming the flag.</summary>
public static class CliArgs {
  static string[] _args;
  static readonly HashSet<string> _warned = new HashSet<string>();
  public static string[] All => _args ??= Environment.GetCommandLineArgs();
  static CultureInfo Ci => CultureInfo.InvariantCulture;

  /// <summary>Headless race tests (PoRace.exe -batchmode -nographics -raceTest).</summary>
  public static bool RaceTest => Has("-raceTest");

  public static bool Has(string flag) => Array.LastIndexOf(All, flag) >= 0;

  /// <summary>The `count` values after the last `flag`; false (one warning) when fewer follow it.</summary>
  public static bool TryStrings(string flag, int count, out string[] values) {
    values = null; var a = All; int i = Array.LastIndexOf(a, flag);
    if (i < 0) return false;
    if (i + count >= a.Length) { Warn(flag, $"needs {count} value(s)"); return false; }
    values = new string[count]; Array.Copy(a, i + 1, values, 0, count);
    return true;
  }

  public static bool TryString(string flag, out string value) {
    value = null; if (!TryStrings(flag, 1, out var v)) return false;
    value = v[0]; return true;
  }

  public static bool TryFloat(string flag, out float value) {
    value = 0f; if (!TryString(flag, out var s)) return false;
    return float.TryParse(s, NumberStyles.Float, Ci, out value) || Bad(flag, s);
  }

  public static bool TryDouble(string flag, out double value) {
    value = 0.0; if (!TryString(flag, out var s)) return false;
    return double.TryParse(s, NumberStyles.Float, Ci, out value) || Bad(flag, s);
  }

  public static bool TryInt(string flag, out int value) {
    value = 0; if (!TryString(flag, out var s)) return false;
    return int.TryParse(s, NumberStyles.Integer, Ci, out value) || Bad(flag, s);
  }

  /// <summary>Case-sensitive enum name (or number), like Enum.TryParse.</summary>
  public static bool TryEnum<T>(string flag, out T value) where T : struct, Enum {
    value = default; if (!TryString(flag, out var s)) return false;
    return Enum.TryParse(s, out value) || Bad(flag, s);
  }

  /// <summary>The `count` floats after the last `flag` (e.g. -command vx vy yaw).</summary>
  public static bool TryFloats(string flag, int count, out float[] values) {
    values = null; if (!TryStrings(flag, count, out var s)) return false;
    var f = new float[count];
    for (int k = 0; k < count; k++) if (!float.TryParse(s[k], NumberStyles.Float, Ci, out f[k])) return Bad(flag, s[k]);
    values = f; return true;
  }

  static bool Bad(string flag, string s) { Warn(flag, $"cannot parse '{s}'"); return false; }
  static void Warn(string flag, string why) { if (_warned.Add(flag)) Debug.LogWarning($"[CliArgs] {flag}: {why}, ignored"); }
}

}
