using System;
using System.Collections.Generic;
using UnityEngine;

namespace PoRace {

/// <summary>Feature 1, "Redline" joint strain. Each racer's legs glow from their own colour to red with joint effort
/// (|actuator_force| / 24 N·m from RaceSensors), the standings show one mark per leg (FL FR RL RR), and a racer with
/// three or more joints above 85 % for half a second is REDLINING (an event for the director and the ticker).</summary>
public class StrainMonitor : MonoBehaviour {
  public MultiRaceOrchestrator race;
  public RaceSensors sensors;
  [Range(0f, 1f)] public float redlineLevel = 0.85f;
  public int redlineJoints = 3;
  public float redlineSeconds = 0.5f;
  public bool tintLegs = true;

  public event Action<Racer> Redline;

  class Part { public Renderer r; public Color baseColor; public int joint; public float shown = -1f; }
  class Track { public readonly List<Part> parts = new List<Part>(); public float redFor, peak; public bool redline; public int count; }
  readonly Dictionary<Racer, Track> _t = new Dictionary<Racer, Track>();
  MaterialPropertyBlock _mpb;
  static readonly int BaseColor = Shader.PropertyToID("_BaseColor"), Color_ = Shader.PropertyToID("_Color");
  static readonly Color Hot = new Color(1f, 0.12f, 0.05f);

  public bool IsRedlining(Racer r) => _t.TryGetValue(r, out var t) && t.redline;
  public float Peak(Racer r) => _t.TryGetValue(r, out var t) ? t.peak : 0f;
  public int RedlineCount(Racer r) => _t.TryGetValue(r, out var t) ? t.count : 0;

  void Start() {
    if (race == null) race = FindAnyObjectByType<MultiRaceOrchestrator>();
    if (sensors == null) sensors = FindAnyObjectByType<RaceSensors>();
    _mpb = new MaterialPropertyBlock();
    race.standingsExtras.Add(Marks);
    race.Restarted += () => { foreach (var t in _t.Values) { t.peak = 0; t.count = 0; t.redFor = 0; t.redline = false; } };
  }

  /// <summary>Renderers of each leg link, keyed by the joint that moves that link (body FL_hip carries FL_hip_joint...).</summary>
  Track Bind(Racer r) {
    var t = new Track(); var root = r.controller.BaseTransform; if (root == null) return t;
    var bodyJoint = new Dictionary<string, int>();
    for (int i = 0; i < Go2Model.Nu; i++) bodyJoint[r.controller.prefix + Go2Model.ActuatorNames[i]] = i;   // actuator FL_hip == body FL_hip
    foreach (var rend in root.GetComponentsInChildren<Renderer>()) {
      for (var p = rend.transform; p != null && p != root.parent; p = p.parent)
        if (bodyJoint.TryGetValue(p.name, out int j)) {
          var mat = rend.sharedMaterial;
          var c = mat == null ? Color.gray : mat.HasProperty(BaseColor) ? mat.GetColor(BaseColor) : mat.HasProperty(Color_) ? mat.GetColor(Color_) : Color.gray;
          t.parts.Add(new Part { r = rend, baseColor = c, joint = j }); break;
        }
    }
    return t;
  }

  void Update() {
    if (sensors == null) return;
    float dt = Time.deltaTime;
    foreach (var r in race.Live) {
      var s = sensors.Of(r); if (s == null) continue;
      if (!_t.TryGetValue(r, out var t)) _t[r] = t = BroadcastUi.Headless ? new Track() : Bind(r);
      int hot = 0; float max = 0f;
      for (int i = 0; i < Go2Model.Nu; i++) { if (s.effort[i] > redlineLevel) hot++; max = Mathf.Max(max, s.effort[i]); }
      if (race.Phase == RacePhase.Racing) t.peak = Mathf.Max(t.peak, max);
      t.redFor = hot >= redlineJoints ? t.redFor + dt : 0f;
      bool red = t.redFor > redlineSeconds && race.Phase == RacePhase.Racing && r.finishTime < 0;
      if (red && !t.redline) { t.count++; Redline?.Invoke(r); BroadcastTicker.Post($"{BroadcastUi.Paint(r.name, r.color)} is {BroadcastUi.Paint("REDLINING", Hot)}  ({hot} joints over {redlineLevel * 100:F0} %)", 3f); }
      t.redline = red;
      if (!tintLegs) continue;
      foreach (var p in t.parts) {
        float k = Mathf.Clamp01((s.effort[p.joint] - 0.6f) / 0.35f);   // standing holds ~50 %, a hard stride ~70 %
        if (Mathf.Abs(k - p.shown) < 0.03f) continue;
        p.shown = k; p.r.GetPropertyBlock(_mpb);
        var c = Color.Lerp(p.baseColor, Hot, k); _mpb.SetColor(BaseColor, c); _mpb.SetColor(Color_, c); p.r.SetPropertyBlock(_mpb);
      }
    }
  }

  /// <summary>Standings suffix: one mark per leg coloured by that leg's hardest-working joint, then REDLINE.</summary>
  string Marks(Racer r) {
    var s = sensors != null ? sensors.Of(r) : null; if (s == null || r.finishTime >= 0) return "";
    var sb = new System.Text.StringBuilder("  <size=16>");
    for (int leg = 0; leg < 4; leg++) {
      float e = Mathf.Max(s.effort[3 * leg], Mathf.Max(s.effort[3 * leg + 1], s.effort[3 * leg + 2]));
      var c = e < 0.7f ? Color.Lerp(new Color(0.3f, 0.85f, 0.35f), Color.yellow, Mathf.InverseLerp(0.45f, 0.7f, e)) : Color.Lerp(Color.yellow, Hot, Mathf.InverseLerp(0.7f, 0.9f, e));
      sb.Append(BroadcastUi.Paint("■", c));
    }
    sb.Append("</size>");
    if (IsRedlining(r)) sb.Append(BroadcastUi.Paint(" RED", Hot));
    return sb.ToString();
  }
}

}
