using UnityEngine;

namespace PoRace {

/// <summary>Portrait chase camera. `offset` and `lookAhead` are expressed in the track frame given by `forward`
/// (x = along forward, y = up, z = to the right of forward... see below), so the same rig works on straights and corners.
/// With the default forward (+X) the offsets are plain world offsets, as in the first test scenes.</summary>
public class FollowCam : MonoBehaviour {
  public Transform target;            // base body of a racer, or the pack anchor
  public Vector3 offset = new Vector3(1.2f, 0.9f, -1.6f);
  public Vector3 lookAhead = Vector3.zero;
  public float smooth = 4f;
  [System.NonSerialized] public Vector3 forward = Vector3.right;   // travel direction on the ground (Unity frame)
  /// <summary>Optional track surface height under a Unity position (banked tracks); the camera stays clearance above it.</summary>
  [System.NonSerialized] public System.Func<Vector3, float> groundHeight;
  public float clearance = 0.6f;

  Vector3 ToWorld(Vector3 v) {
    var f = new Vector3(forward.x, 0f, forward.z).normalized; if (f.sqrMagnitude < 0.5f) f = Vector3.right;
    var side = Vector3.Cross(f, Vector3.up);   // (0,0,1) when forward is +X, so world offsets are preserved
    return f * v.x + Vector3.up * v.y + side * v.z;
  }

  void LateUpdate() {
    if (target == null) return;
    var want = target.position + ToWorld(offset);
    var p = Vector3.Lerp(transform.position, want, 1f - Mathf.Exp(-smooth * Time.deltaTime));
    if (groundHeight != null) p.y = Mathf.Max(p.y, groundHeight(p) + clearance);   // never inside a bank
    transform.position = p;
    transform.LookAt(target.position + Vector3.up * 0.1f + ToWorld(lookAhead));
  }
}

}
