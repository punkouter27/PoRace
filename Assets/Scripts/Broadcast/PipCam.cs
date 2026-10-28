using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace PoRace {

/// <summary>Feature A9, racer cam picture-in-picture. A second camera renders the second story to a small
/// RenderTexture shown on the right of the HUD: the closest battle (two running racers within 2.5 m) that the director
/// (RaceDirector) is not already on, else the leader when the main shot is elsewhere, else P2. A subject holds at
/// least 3 s. PC tier: panel resolution at 30 fps; Mobile tier (GfxTier): 1/4 of the pixels at 15 fps, no shadows.
/// The camera is enabled only on the frames it renders, never casts post-processing and culls the UI layer. Visual
/// only; off in headless runs and outside the racing phase.</summary>
[DefaultExecutionOrder(210)]   // after RaceDirector (200) picks this frame's subject
public class PipCam : MonoBehaviour {
  public MultiRaceOrchestrator race;
  public RaceDirector director;
  public Vector2 size = new Vector2(256, 176);
  public float minHold = 3f, battleGap = 2.5f;

  Camera _cam, _main; RenderTexture _rt; RawImage _img; Text _label; GameObject _panel;
  Racer _a, _b; float _since = -100f, _nextFrame, _nextPick, _lastRender; Vector3 _vel; bool _snap = true;
  string[] _names; string[,] _pairs;   // cached captions (no per-frame strings)

  void Start() {
    if (!BroadcastUi.Wired(this, race, nameof(race))) return;
    if (director == null) director = FindAnyObjectByType<RaceDirector>();
    if (BroadcastUi.Headless || race.Live.Count < 2) { enabled = false; return; }
    _main = race.ViewCamera;
    float scale = GfxTier.Mobile ? 0.5f : 1f, px = Mathf.Max(Screen.width / 720f, Screen.height / 1280f);   // canvas units -> pixels (portrait reference)
    _rt = new RenderTexture(Mathf.Max(64, (int)(size.x * px * scale)), Mathf.Max(48, (int)(size.y * px * scale)), 16) { name = "PipRT" };
    var go = new GameObject("PipCam"); go.transform.SetParent(transform, false);
    _cam = go.AddComponent<Camera>(); _cam.targetTexture = _rt; _cam.fieldOfView = 45f; _cam.nearClipPlane = 0.05f; _cam.farClipPlane = 120f;
    if (_main != null) { _cam.clearFlags = _main.clearFlags; _cam.backgroundColor = _main.backgroundColor; _cam.cullingMask = _main.cullingMask & ~(1 << 5); }
    var data = _cam.GetUniversalAdditionalCameraData(); data.renderPostProcessing = false; data.renderShadows = !GfxTier.Mobile; data.antialiasing = AntialiasingMode.None;
    _cam.enabled = false;

    var frame = BroadcastUi.Panel(BroadcastUi.Root, "Pip", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-10, 150), size + new Vector2(8, 34), BroadcastUi.Dark);
    _panel = frame.gameObject;
    _img = BroadcastUi.Rect(frame.transform, "PipImage", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -4), size).gameObject.AddComponent<RawImage>();
    _img.texture = _rt; _img.raycastTarget = false;
    _label = BroadcastUi.Label(frame.transform, "PipLabel", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 3), new Vector2(size.x, 30), 22, TextAnchor.MiddleCenter);
    _panel.SetActive(false);

    var live = race.Live; int n = live.Count; _names = new string[n]; _pairs = new string[n, n];
    string cam = BroadcastUi.Paint("●", BroadcastUi.Live) + " CAM B  ";
    for (int i = 0; i < n; i++) _names[i] = cam + BroadcastUi.Paint(live[i].name, live[i].color);
    for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) _pairs[i, j] = $"{cam}{BroadcastUi.Paint(live[i].name, live[i].color)} vs {BroadcastUi.Paint(live[j].name, live[j].color)}";
    race.Restarted += () => { _a = _b = null; _since = -100f; };
  }

  void OnDestroy() { if (_rt != null) { _rt.Release(); Destroy(_rt); } }

  bool OnMain(Racer r) => director != null && director.Current != Shot.Pack && director.Subject == r;

  /// <summary>The second story: closest battle off the main shot, else the leader, else P2.</summary>
  void Pick() {
    var live = race.Live; Racer ba = null, bb = null, lead = null, second = null; float best = battleGap;
    for (int i = 0; i < live.Count; i++) {
      var p = live[i]; if (p.finishTime >= 0) continue;
      if (lead == null || p.distance > lead.distance) { second = lead; lead = p; } else if (second == null || p.distance > second.distance) second = p;
      for (int j = i + 1; j < live.Count; j++) {
        var q = live[j]; if (q.finishTime >= 0 || OnMain(p) || OnMain(q)) continue;
        float g = Mathf.Abs(p.distance - q.distance);
        if (g < best) { best = g; bool pAhead = p.distance >= q.distance; ba = pAhead ? p : q; bb = pAhead ? q : p; }
      }
    }
    if (ba == null) { ba = lead != null && !OnMain(lead) ? lead : second; bb = null; }
    bool same = (ba == _a && bb == _b) || (ba == _b && bb == _a);
    if (same || ba == null) { if (ba == null) _a = _b = null; return; }
    if (_a != null && _a.finishTime < 0 && Time.unscaledTime - _since < minHold) return;
    _a = ba; _b = bb; _since = Time.unscaledTime; _snap = true;
    int ia = IndexOf(_a), ib = _b != null ? IndexOf(_b) : -1;
    _label.text = ib >= 0 ? _pairs[ia, ib] : _names[ia];
  }

  int IndexOf(Racer r) { var live = race.Live; for (int i = 0; i < live.Count; i++) if (live[i] == r) return i; return 0; }

  void LateUpdate() {
    if (_cam.enabled) _cam.enabled = false;   // rendered last frame: hold that picture until the next tick
    bool racing = race.Phase == RacePhase.Racing && Time.timeScale > 0f;   // hidden in replays (timeScale 0) and after the finish
    if (Time.unscaledTime >= _nextPick && racing) { _nextPick = Time.unscaledTime + 0.5f; Pick(); }
    bool on = racing && _a != null && _a.Body.BaseTransform != null;
    if (_panel.activeSelf != on) { _panel.SetActive(on); _snap = true; }
    if (!on || Time.unscaledTime < _nextFrame) return;
    _nextFrame = Time.unscaledTime + (GfxTier.Mobile ? 1f / 15f : 1f / 30f);
    // Chase from behind and above the subject (or the pair), looking along the track.
    var pa = _a.Body.BaseTransform.position; var pb = _b != null && _b.Body.BaseTransform != null ? _b.Body.BaseTransform.position : pa;
    var mid = (pa + pb) * 0.5f; race.map.PointAt(_a.distance, out var t); var f = new Vector3(t.x, 0f, t.y);
    float spread = Vector3.Distance(pa, pb);
    var want = mid - f * (1.8f + 0.6f * spread) + Vector3.up * (0.7f + 0.2f * spread) + Vector3.Cross(f, Vector3.up) * 0.4f;
    var tr = _cam.transform;
    tr.position = _snap ? want : Vector3.SmoothDamp(tr.position, want, ref _vel, 0.2f, Mathf.Infinity, Mathf.Min(0.2f, Time.unscaledTime - _lastRender));
    _snap = false; _lastRender = Time.unscaledTime;
    tr.rotation = Quaternion.LookRotation((mid + f * 0.8f + Vector3.up * 0.1f - tr.position).normalized, Vector3.up);
    _cam.enabled = true;   // renders this frame only
  }
}

}
