using UnityEngine;

namespace PoRace {

/// <summary>
/// Matt's look on top of the posed scan (purely visual; never touches MuJoCo state):
///   - grime: the wear overlay material's opacity follows his accumulated contact wear in a race (ContactImpulse,
///     same scale as the dogs' grime, StrainMonitor.wearFull)
///   - breathing: the "Breathe" blend shape at a rate and depth that rise with his effort (RaceSensors)
///   - Belly: a damped spring on the Belly bone, dragged by the torso's motion, so the sweatshirt hem lags
///   - forearm twist bones take half the hand's roll (the rig's Blender constraint, which FBX does not carry)
/// Outside a race (menu, testbed) it breathes at rest and stays clean.
/// </summary>
[DefaultExecutionOrder(100)]   // after MattSkin has posed the bones
public class MattLook : MonoBehaviour {
  public SkinnedMeshRenderer body;
  [Tooltip("Material slot of the transparent grime overlay on the body renderer.")]
  public int grimeSlot = 1;
  public MattController controller;
  public float wearFull = 250f;
  [Tooltip("Breaths per minute at rest and at full effort.")]
  public Vector2 breathRate = new Vector2(14f, 40f);
  public float springStiffness = 60f, springDamping = 7f, springMax = 0.03f;

  MultiRaceOrchestrator _race; ContactImpulse _contacts; RaceSensors _sensors; Racer _me;
  int _breathe = -1; float _phase; Material _grime;
  Transform _belly, _bellyParent; Vector3 _bellyRest, _offset, _vel, _lastParentPos;
  Transform[] _twist = new Transform[2], _hand = new Transform[2]; Quaternion[] _twistRest = new Quaternion[2], _handRest = new Quaternion[2];

  void Start() {
    if (body == null) body = GetComponentInChildren<SkinnedMeshRenderer>();
    _breathe = body.sharedMesh.GetBlendShapeIndex("Breathe");
    var mats = body.materials; if (grimeSlot < mats.Length) _grime = mats[grimeSlot];
    _race = FindAnyObjectByType<MultiRaceOrchestrator>(); _contacts = FindAnyObjectByType<ContactImpulse>(); _sensors = FindAnyObjectByType<RaceSensors>();
    foreach (var t in GetComponentsInChildren<Transform>()) {
      if (t.name == "Belly") _belly = t;
      for (int s = 0; s < 2; s++) {
        string side = s == 0 ? "Left" : "Right";
        if (t.name == side + "LowerArmTwist") _twist[s] = t;
        if (t.name == side + "Hand") _hand[s] = t;
      }
    }
    if (_belly != null) { _bellyParent = _belly.parent; _bellyRest = _belly.localPosition; _lastParentPos = _bellyParent.position; }
    for (int s = 0; s < 2; s++) if (_twist[s] != null && _hand[s] != null) { _twistRest[s] = _twist[s].localRotation; _handRest[s] = _hand[s].localRotation; }
  }

  Racer Me() {
    if (_me != null || _race == null || controller == null) return _me;
    foreach (var r in _race.Live) if (r.matt == controller) _me = r;
    return _me;
  }

  void LateUpdate() {
    float dt = Time.deltaTime; if (dt <= 0f) return;
    var me = Me();
    float effort = 0f;
    var s = me != null && _sensors != null ? _sensors.Of(me) : null;
    if (s != null) { foreach (var e in s.effort) effort += e; effort = Mathf.Clamp01(2f * effort / s.effort.Length); }
    if (_grime != null) {
      float wear = me != null && _contacts != null ? Mathf.Clamp01(_contacts.Wear(me) / wearFull) : 0f;
      var c = _grime.GetColor("_BaseColor"); c.a = wear; _grime.SetColor("_BaseColor", c);
    }
    if (_breathe >= 0) {
      _phase += dt * Mathf.Lerp(breathRate.x, breathRate.y, effort) / 60f * 2f * Mathf.PI;
      body.SetBlendShapeWeight(_breathe, (0.5f + 0.5f * Mathf.Sin(_phase)) * Mathf.Lerp(60f, 100f, effort));
    }
    if (_belly != null) {   // spring in the parent's frame, dragged by the parent's motion
      var p = _bellyParent.position; var parentVel = (p - _lastParentPos) / dt; _lastParentPos = p;
      var local = _bellyParent.InverseTransformVector(parentVel);
      _vel += (-springStiffness * _offset - springDamping * _vel) * dt - local * (dt * 0.5f);
      _offset = Vector3.ClampMagnitude(_offset + _vel * dt, springMax);
      _belly.localPosition = _bellyRest + _offset;
    }
    for (int i = 0; i < 2; i++) {
      if (_twist[i] == null) continue;
      var d = Quaternion.Inverse(_handRest[i]) * _hand[i].localRotation;
      d.ToAngleAxis(out float ang, out var axis);
      float roll = Vector3.Dot(axis, Vector3.up) * (ang > 180f ? ang - 360f : ang);   // bone axis = local +Y (FBX primary axis)
      _twist[i].localRotation = _twistRest[i] * Quaternion.AngleAxis(0.5f * roll, Vector3.up);
    }
  }
}

}
