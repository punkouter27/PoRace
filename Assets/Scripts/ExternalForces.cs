using System.Collections.Generic;
using Mujoco;
using UnityEngine;

namespace PoRace {

/// <summary>Single writer of mjData.xfrc_applied for robot bodies. Shoves (timed pushes) and wind (a steady force per
/// body) are summed here every physics step, so a shove ending does not zero the wind and vice versa. Bodies that had a
/// force last step are cleared when nothing acts on them any more. MuJoCo frame (x, y, z up), newtons.</summary>
public unsafe class ExternalForces : MonoBehaviour {
  static ExternalForces _instance;
  public static ExternalForces Instance {
    get {
      if (_instance == null) { _instance = FindAnyObjectByType<ExternalForces>(); if (_instance == null) _instance = new GameObject("ExternalForces").AddComponent<ExternalForces>(); }
      return _instance;
    }
  }

  struct Push { public int body; public Vector3 force; public int steps; }
  readonly List<Push> _pushes = new List<Push>();
  readonly Dictionary<int, Vector3> _steady = new Dictionary<int, Vector3>();
  readonly HashSet<int> _touched = new HashSet<int>();
  readonly Dictionary<int, Vector3> _sum = new Dictionary<int, Vector3>();

  void Awake() { if (_instance == null) _instance = this; MjScene.Instance.preUpdateEvent += OnPreStep; }
  void OnDestroy() { if (_instance == this) _instance = null; if (MjScene.InstanceExists) MjScene.Instance.preUpdateEvent -= OnPreStep; }

  /// <summary>Constant force for `steps` physics steps (a shove is impulse / (steps * dt)).</summary>
  public void AddPush(int body, Vector3 force, int steps) { if (body >= 0 && steps > 0) _pushes.Add(new Push { body = body, force = force, steps = steps }); }
  /// <summary>Force held until changed (wind). Zero removes it.</summary>
  public void SetSteady(int body, Vector3 force) { if (force.sqrMagnitude < 1e-12f) _steady.Remove(body); else _steady[body] = force; }

  void OnPreStep(object sender, MjStepArgs a) {
    _sum.Clear();
    foreach (var kv in _steady) _sum[kv.Key] = kv.Value;
    for (int i = _pushes.Count - 1; i >= 0; i--) {
      var p = _pushes[i];
      _sum[p.body] = (_sum.TryGetValue(p.body, out var f) ? f : Vector3.zero) + p.force;
      if (--p.steps <= 0) _pushes.RemoveAt(i); else _pushes[i] = p;
    }
    var x = a.data->xfrc_applied;
    foreach (int b in _touched) if (!_sum.ContainsKey(b)) { x[6 * b] = 0; x[6 * b + 1] = 0; x[6 * b + 2] = 0; }
    foreach (var kv in _sum) { x[6 * kv.Key] = kv.Value.x; x[6 * kv.Key + 1] = kv.Value.y; x[6 * kv.Key + 2] = kv.Value.z; }
    _touched.Clear(); foreach (int b in _sum.Keys) _touched.Add(b);
  }
}

}
