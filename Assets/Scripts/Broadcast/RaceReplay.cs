using System;
using System.Collections.Generic;
using System.IO;
using Mujoco;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PoRace {

/// <summary>Feature 8, instant replay and ghost racing.
/// Replay: qpos of the whole model is kept for the last 8 s at 50 Hz. After a fall, or an overtake in the last 15 %
/// of the race, the race pauses (timeScale 0, so MuJoCo does not step), the full mjData is copied aside, the last 5 s
/// play back kinematically (qpos -> mj_kinematics -> transforms) at 0.6x from an orbiting camera, and the saved
/// mjData is copied back: the race continues bit-for-bit where it stopped. R or a tap skips. At most one per 20 s.
/// Ghost (time trial, 1 racer): the best finish on each map is saved (visual transforms of the racer, 25 Hz) to
/// persistentDataPath and replayed next time as a translucent dog keyed on race time.
/// Interactive only; headless races never pause.</summary>
public unsafe class RaceReplay : MonoBehaviour {
  public MultiRaceOrchestrator race;
  public RaceDirector director;
  public Material ghostMaterial;
  public float keepSeconds = 8f, replaySeconds = 5f, playbackSpeed = 0.6f, cooldown = 20f;

  public bool Playing { get; private set; }
  int _nq, _cap, _head, _count, _step;
  double[] _ring;
  MujocoLib.mjData_* _saved;
  float _pending = -1f, _lastReplay = -100f, _cursor, _savedScale; Racer _subject; string _reason;
  Text _banner;

  void Start() {
    if (race == null) race = FindAnyObjectByType<MultiRaceOrchestrator>();
    if (director == null) director = FindAnyObjectByType<RaceDirector>();
    if (BroadcastUi.Headless) { enabled = false; return; }
    MjScene.Instance.postUpdateEvent += Record;
    race.Fell += r => Request(r, "FALL", 1.2f);
    race.Overtook += (a, b) => { if (a.distance > 0.85f * race.TotalLength) Request(a, "OVERTAKE", 0.8f); };
    race.Restarted += () => { _count = 0; _pending = -1f; GhostRestart(); };
    race.Finished += GhostFinished;
    _banner = BroadcastUi.Label(BroadcastUi.Root, "ReplayBanner", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -395), new Vector2(520, 50), 34, TextAnchor.MiddleCenter);   // under the race clock
    _banner.enabled = false;
    GhostRestart();
  }

  void OnDestroy() {
    if (MjScene.InstanceExists) MjScene.Instance.postUpdateEvent -= Record;
    if (_saved != null) { MujocoLib.mj_deleteData(_saved); _saved = null; }
  }

  void Record(object sender, MjStepArgs a) {
    if (Playing || _step++ % Go2Controller.Decimation != 0) return;
    if (_ring == null) { _nq = (int)a.model->nq; _cap = Mathf.CeilToInt(keepSeconds / (Go2Controller.SimDt * Go2Controller.Decimation)); _ring = new double[_nq * _cap]; }
    int off = _head * _nq; for (int i = 0; i < _nq; i++) _ring[off + i] = a.data->qpos[i];
    _head = (_head + 1) % _cap; _count = Mathf.Min(_count + 1, _cap);
  }

  /// <summary>Ask for a replay `delay` seconds from now (so the aftermath is in the clip).</summary>
  public void Request(Racer subject, string reason, float delay) {
    if (Playing || _pending >= 0f || Time.unscaledTime - _lastReplay < cooldown || race.Live.Count < 2) return;
    _subject = subject; _reason = reason; _pending = Time.unscaledTime + delay;
  }

  void Update() {
    if (_pending >= 0f && !Playing && Time.unscaledTime >= _pending) { _pending = -1f; Begin(); }
    GhostUpdate();
    if (!Playing) return;
    bool skip = (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame) || (Pointer.current != null && Pointer.current.press.wasPressedThisFrame);
    _cursor += Time.unscaledDeltaTime * playbackSpeed / (Go2Controller.SimDt * Go2Controller.Decimation);
    int frames = Mathf.Min(_count, Mathf.CeilToInt(replaySeconds / (Go2Controller.SimDt * Go2Controller.Decimation)));
    if (skip || _cursor >= frames - 1) { End(); return; }
    Show(_count - frames + (int)_cursor);
    _banner.text = $"{BroadcastUi.Paint("◉ REPLAY", BroadcastUi.Live)}  {_reason}  {BroadcastUi.Paint(_subject.name, _subject.color)}";
  }

  void Begin() {
    if (_count < 10) return;
    var s = MjScene.Instance; if (s.Model == null) return;
    if (_saved == null) _saved = MujocoLib.mj_makeData(s.Model);
    MujocoLib.mj_copyData(_saved, s.Model, s.Data);
    _savedScale = Time.timeScale; Time.timeScale = 0f;
    Playing = true; _cursor = 0f; _lastReplay = Time.unscaledTime; _banner.enabled = true;
    director?.BeginReplay(_subject);
  }

  /// <summary>Frame k counted from the oldest kept frame.</summary>
  void Show(int k) {
    var s = MjScene.Instance; int idx = ((_head - _count + k) % _cap + _cap) % _cap, off = idx * _nq;
    for (int i = 0; i < _nq; i++) s.Data->qpos[i] = _ring[off + i];
    MujocoLib.mj_kinematics(s.Model, s.Data);
    s.SyncUnityToMjState();
  }

  void End() {
    var s = MjScene.Instance;
    MujocoLib.mj_copyData(s.Data, s.Model, _saved);
    s.SyncUnityToMjState();
    Time.timeScale = _savedScale > 0f ? _savedScale : 1f;
    Playing = false; _banner.enabled = false;
    director?.EndReplay();
  }

  // ---------------- Ghost (time trial) ----------------
  class GhostRun { public float time; public int parts; public List<float> t = new List<float>(); public List<float> pose = new List<float>(); }
  GhostRun _rec, _best; Renderer[] _parts; Transform[] _ghost; float _nextSample; bool _ghostOn;
  string GhostPath => Path.Combine(Application.persistentDataPath, $"ghost_{race.map.name}.bin");

  void GhostRestart() {
    _ghostOn = race.Live.Count == 1;
    if (!_ghostOn) return;
    var root = race.Live[0].controller.BaseTransform; if (root == null) { _ghostOn = false; return; }
    _parts = root.GetComponentsInChildren<Renderer>();
    _rec = new GhostRun { parts = _parts.Length }; _nextSample = 0f;
    _best = Load();
    if (_ghost == null && _best != null && _best.parts == _parts.Length && ghostMaterial != null) {
      var holder = new GameObject("Ghost").transform; _ghost = new Transform[_parts.Length];
      for (int i = 0; i < _parts.Length; i++) {
        var go = new GameObject("g" + i); go.transform.SetParent(holder, false);
        var mf = _parts[i].GetComponent<MeshFilter>();
        go.AddComponent<MeshFilter>().sharedMesh = mf != null ? mf.sharedMesh : null;
        var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = ghostMaterial; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        go.transform.localScale = _parts[i].transform.lossyScale; _ghost[i] = go.transform;
      }
      BroadcastTicker.Post($"TIME TRIAL vs ghost: best {_best.time:F2} s", 6f);
    }
    if (_ghost != null) foreach (var g in _ghost) g.gameObject.SetActive(false);
  }

  void GhostUpdate() {
    if (!_ghostOn || Playing || race.Phase != RacePhase.Racing) return;
    float t = (float)race.RaceTime;
    if (t >= _nextSample && _rec != null) {
      _nextSample = t + 0.04f; _rec.t.Add(t);
      foreach (var p in _parts) { var tr = p.transform; var q = tr.rotation; _rec.pose.AddRange(new[] { tr.position.x, tr.position.y, tr.position.z, q.x, q.y, q.z, q.w }); }
    }
    if (_ghost == null || _best == null || _best.t.Count < 2) return;
    int k = _best.t.BinarySearch(t); if (k < 0) k = ~k; k = Mathf.Clamp(k, 1, _best.t.Count - 1);
    float u = Mathf.InverseLerp(_best.t[k - 1], _best.t[k], t); bool on = t <= _best.time;
    for (int i = 0; i < _ghost.Length; i++) {
      _ghost[i].gameObject.SetActive(on); if (!on) continue;
      int a = ((k - 1) * _best.parts + i) * 7, b = (k * _best.parts + i) * 7; var P = _best.pose;
      _ghost[i].SetPositionAndRotation(Vector3.Lerp(new Vector3(P[a], P[a + 1], P[a + 2]), new Vector3(P[b], P[b + 1], P[b + 2]), u),
          Quaternion.Slerp(new Quaternion(P[a + 3], P[a + 4], P[a + 5], P[a + 6]), new Quaternion(P[b + 3], P[b + 4], P[b + 5], P[b + 6]), u));
    }
  }

  void GhostFinished(Racer r) {
    if (!_ghostOn || _rec == null || r.finishTime < 0) return;
    _rec.time = (float)r.finishTime;
    if (_best != null && _best.time <= _rec.time) { BroadcastTicker.Post($"Ghost holds: {_best.time:F2} s (you {_rec.time:F2} s)", 6f); return; }
    Save(_rec); BroadcastTicker.Post($"{BroadcastUi.Paint("NEW BEST", BroadcastUi.Accent)} {_rec.time:F2} s: saved as the ghost", 6f);
  }

  void Save(GhostRun g) {
    try {
      using (var w = new BinaryWriter(File.Create(GhostPath))) {
        w.Write(1); w.Write(g.time); w.Write(g.parts); w.Write(g.t.Count);
        foreach (var t in g.t) w.Write(t);
        foreach (var p in g.pose) w.Write(p);
      }
    } catch (Exception e) { Debug.LogWarning("[Ghost] save failed: " + e.Message); }
  }

  GhostRun Load() {
    try {
      if (!File.Exists(GhostPath)) return null;
      using (var rd = new BinaryReader(File.OpenRead(GhostPath))) {
        if (rd.ReadInt32() != 1) return null;
        var g = new GhostRun { time = rd.ReadSingle(), parts = rd.ReadInt32() }; int n = rd.ReadInt32();
        for (int i = 0; i < n; i++) g.t.Add(rd.ReadSingle());
        for (int i = 0; i < n * g.parts * 7; i++) g.pose.Add(rd.ReadSingle());
        return g;
      }
    } catch (Exception e) { Debug.LogWarning("[Ghost] load failed: " + e.Message); return null; }
  }
}

}
