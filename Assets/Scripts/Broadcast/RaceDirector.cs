using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PoRace {

public enum Shot { Pack, Leader, Battle, Trackside, Overhead, Incident, Finish, Replay }

/// <summary>Feature 2, the automatic director. Race events are scored and the camera cuts to the best shot:
/// overtake = BATTLE (side-on, both racers), fall = INCIDENT (orbit + slow motion), leader in the last 6 m = FINISH
/// (fixed at the line), a pile-up of contacts = OVERHEAD, redline = close LEADER chase on that racer; otherwise it
/// rotates PACK / LEADER / TRACKSIDE. A shot holds at least 2.5 s unless something clearly more important happens.
/// PACK hands the camera back to FollowCam; every other shot drives the camera here. C toggles auto / manual
/// (tap the shot label on a phone); in manual, C steps through the shots. Visual only: never touches MuJoCo.</summary>
[DefaultExecutionOrder(200)]
public class RaceDirector : MonoBehaviour {
  public MultiRaceOrchestrator race;
  public FollowCam followCam;
  public StrainMonitor strain;
  public bool auto = true;
  public float minHold = 2.5f, maxHold = 6f;
  [Tooltip("Time scale during an INCIDENT shot (interactive only).")]
  public float slowMotion = 0.4f;

  public Shot Current { get; private set; } = Shot.Pack;
  public Racer Subject { get; private set; }
  Racer _other; float _since, _score, _slowUntil, _lastOverhead = -100f; int _bumps, _rotation;
  Vector3 _fixed, _vel; float _fixedS, _orbit, _shakeAmp, _shakeUntil;
  Camera _cam; Text _label; Button _labelButton;
  bool _inReplay;

  void Start() {
    if (race == null) race = FindAnyObjectByType<MultiRaceOrchestrator>();
    if (followCam == null) followCam = race.followCam;
    if (strain == null) strain = FindAnyObjectByType<StrainMonitor>();
    if (BroadcastUi.Headless || followCam == null) { enabled = false; return; }
    _cam = followCam.GetComponent<Camera>();
    race.Overtook += (a, b) => Want(Shot.Battle, a, b, 6f);
    race.Fell += r => Want(Shot.Incident, r, null, 9f);
    race.Finished += r => { if (r.place == 1) Want(Shot.Finish, r, null, 8f); };
    race.Restarted += () => { Cut(Shot.Pack, null, null, 0f); Time.timeScale = 1f; };
    if (strain != null) strain.Redline += r => Want(Shot.Leader, r, null, 3f);
    _labelButton = BroadcastUi.Button(BroadcastUi.Root, "ShotLabel", "", Vector2.zero, Vector2.zero, new Vector2(180, 204), new Vector2(524, 40), new Color(0, 0, 0, 0.55f), 22, ToggleAuto);
    _label = _labelButton.GetComponentInChildren<Text>(); _label.alignment = TextAnchor.MiddleLeft; _label.rectTransform.offsetMin = new Vector2(12, 0);
  }

  void ToggleAuto() { auto = !auto; if (auto) Cut(Shot.Pack, null, null, 0f); }

  /// <summary>Instant replay takes the camera (RaceReplay): orbit the subject while frames play back.</summary>
  public void BeginReplay(Racer subject) { _inReplay = true; Cut(Shot.Replay, subject, null, 100f); }
  public void EndReplay() { _inReplay = false; Cut(Shot.Pack, null, null, 0f); }
  public void Shake(float amplitude, float seconds) { _shakeAmp = Mathf.Max(_shakeAmp, amplitude); _shakeUntil = Time.unscaledTime + seconds; }

  void Want(Shot shot, Racer subject, Racer other, float score) {
    if (!auto || _inReplay || race.Phase == RacePhase.Countdown) return;
    float held = Time.unscaledTime - _since;
    if (score > _score + 3f || (held > minHold && score >= _score * 0.5f)) Cut(shot, subject, other, score);
  }

  void Cut(Shot shot, Racer subject, Racer other, float score) {
    Current = shot; Subject = subject; _other = other; _score = score; _since = Time.unscaledTime; _vel = Vector3.zero; _orbit = 0f;
    followCam.enabled = shot == Shot.Pack;
    if (shot == Shot.Trackside && subject != null) {
      // Ahead of the subject, beside the track on the outside, low: the racer runs at and past the lens.
      float s = subject.distance + 7f; _fixedS = s; var p = race.map.PointAt(s, out var t); var left = MapDefinition.Left(t);
      // Just outside the rail and above it (rails are 0.3 m tall), so the rail does not fill the frame.
      float side = subject.lateral >= 0 ? 1f : -1f, w = race.map.halfWidth + 0.6f;
      var mj = p + left * side * w; _fixed = new Vector3(mj.x, race.map.SurfaceHeight(s, side * race.map.halfWidth) + 0.95f, mj.y);
    }
    if (shot == Shot.Finish) {
      var p = race.map.PointAt(race.map.closed ? 0f : race.map.Length, out var t); var mj = p + t * 2.5f + MapDefinition.Left(t) * (race.map.halfWidth + 0.8f);
      _fixed = new Vector3(mj.x, 0.7f, mj.y);
    }
    if (shot == Shot.Incident && !BroadcastUi.Headless && Time.timeScale >= 0.99f) { Time.timeScale = slowMotion; _slowUntil = Time.unscaledTime + 1.6f; }
  }

  Vector3 Pos(Racer r) => r != null && r.controller.BaseTransform != null ? r.controller.BaseTransform.position : Vector3.zero;
  Vector3 Fwd(Racer r) { race.map.PointAt(r.distance, out var t); return new Vector3(t.x, 0f, t.y); }

  void Update() {
    if (Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame) {
      if (auto) auto = false;
      else { var all = new[] { Shot.Pack, Shot.Leader, Shot.Trackside, Shot.Overhead }; Cut(all[(System.Array.IndexOf(all, Current) + 1) % all.Length], race.Leader(), null, 0f); }
    }
    if (_slowUntil > 0 && Time.unscaledTime > _slowUntil && !_inReplay) { Time.timeScale = 1f; _slowUntil = 0; }
    if (race.Phase == RacePhase.Countdown && Current != Shot.Pack && !_inReplay) Cut(Shot.Pack, null, null, 0f);

    float held = Time.unscaledTime - _since;
    if (auto && !_inReplay && race.Phase != RacePhase.Countdown) {
      // A burst of contacts (10 new bumps) shows from above, at most every 15 s: the ovals bump all the time.
      if (race.Bumps > _bumps + 10 && Time.unscaledTime - _lastOverhead > 15f) { _bumps = race.Bumps; _lastOverhead = Time.unscaledTime; Want(Shot.Overhead, race.Leader(), null, 4f); }
      var lead = race.Leader();
      if (lead != null && lead.finishTime < 0 && race.TotalLength - lead.distance < 6f && Current != Shot.Finish) Want(Shot.Finish, lead, null, 7f);
      bool stale = held > maxHold || (Current == Shot.Trackside && Subject != null && Subject.distance > _fixedS + 3f)
                   || (Current == Shot.Incident && Subject != null && Subject.state == RaceState.RACING && held > minHold);
      if (stale && race.Phase == RacePhase.Racing) {
        var cycle = new[] { Shot.Pack, Shot.Leader, Shot.Trackside };
        var next = cycle[_rotation++ % cycle.Length];
        var subj = next == Shot.Leader ? lead : race.Live[Random.Range(0, race.Live.Count)];
        Cut(next, subj, null, 2f);
      }
    }
    if (_label != null) {
      string who = Subject == null ? "" : _other != null ? $"  {BroadcastUi.Paint(Subject.name, Subject.color)} vs {BroadcastUi.Paint(_other.name, _other.color)}" : "  " + BroadcastUi.Paint(Subject.name, Subject.color);
      _label.text = $"{BroadcastUi.Paint("●", BroadcastUi.Live)} CAM {(int)Current + 1}  {Current.ToString().ToUpperInvariant()}{who}{(auto ? "" : "   MANUAL")}";
    }
  }

  void LateUpdate() {
    if (_cam == null || Current == Shot.Pack) { ApplyShake(); return; }
    float dt = Time.unscaledDeltaTime; var tr = _cam.transform;
    Vector3 want, look;
    var subj = Subject ?? race.Leader();
    switch (Current) {
      case Shot.Leader: {
        var f = Fwd(subj); var p = Pos(subj);
        want = p - f * 1.5f + Vector3.up * 0.55f + Vector3.Cross(f, Vector3.up) * 0.35f; look = p + f * 1.2f + Vector3.up * 0.1f; break; }
      case Shot.Battle: {
        var a = Pos(subj); var b = _other != null ? Pos(_other) : a; var mid = (a + b) * 0.5f; var f = Fwd(subj);
        // Side-on from the half of the track with more room (cross(up, f) points to the right of travel), kept
        // inside the rails so they never block the shot.
        float midLat = 0.5f * (subj.lateral + (_other != null ? _other.lateral : subj.lateral));
        var side = Vector3.Cross(Vector3.up, f).normalized * (midLat >= 0f ? 1f : -1f);
        float room = race.map.halfWidth - 0.15f + Mathf.Abs(midLat);
        want = mid + side * Mathf.Min(1.4f + 0.5f * Vector3.Distance(a, b), room) + Vector3.up * 0.6f + f * 0.4f; look = mid + Vector3.up * 0.1f; break; }
      case Shot.Trackside: want = _fixed; look = Pos(subj) + Vector3.up * 0.15f; break;
      case Shot.Overhead: { var c = followCam.target != null ? followCam.target.position : Pos(subj); var f = Fwd(subj); want = c - f * 2.5f + Vector3.up * 7f; look = c; break; }
      case Shot.Finish: want = _fixed; look = Pos(subj) + Vector3.up * 0.15f; break;
      case Shot.Incident: case Shot.Replay: {
        _orbit += dt * (Current == Shot.Replay ? 0.5f : 0.35f); var p = Pos(subj);
        want = p + new Vector3(Mathf.Cos(_orbit), 0f, Mathf.Sin(_orbit)) * 1.3f + Vector3.up * 0.55f; look = p + Vector3.up * 0.1f; break; }
      default: want = tr.position; look = Pos(subj); break;
    }
    if (followCam.groundHeight != null) want.y = Mathf.Max(want.y, followCam.groundHeight(want) + 0.25f);
    bool moving = Current != Shot.Trackside && Current != Shot.Finish;
    tr.position = moving && Held() > 0.05f ? Vector3.SmoothDamp(tr.position, want, ref _vel, 0.25f, Mathf.Infinity, dt) : want;
    tr.rotation = Quaternion.LookRotation((look - tr.position).normalized, Vector3.up);
    ApplyShake();
  }

  float Held() => Time.unscaledTime - _since;

  void ApplyShake() {
    if (_cam == null || Time.unscaledTime > _shakeUntil) return;
    float k = (_shakeUntil - Time.unscaledTime) * _shakeAmp;
    _cam.transform.position += new Vector3(Mathf.PerlinNoise(Time.unscaledTime * 30f, 0f) - 0.5f, Mathf.PerlinNoise(0f, Time.unscaledTime * 30f) - 0.5f, 0f) * k;
  }
}

}
