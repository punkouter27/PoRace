using System.Collections.Generic;
using UnityEngine;

namespace PoRace {

/// <summary>
/// Drives Matt's skinned scan (Assets/Art/Prefabs/Matt.prefab) from his MuJoCo bodies every frame. The MJCF was built
/// from this rig (tools/blender/matt_mjcf.py): at MuJoCo zero pose every body frame is world-aligned and every joint
/// sits on its bone's head, so a bone's world rotation is simply body rotation x the bone's rest rotation (with the
/// visual root turned so the scan's +Z forward matches MuJoCo +x), and the hips follow the pelvis body. No animation
/// and no physics components: purely visual, read-only on the MuJoCo state.
/// </summary>
public class MattSkin : MonoBehaviour {
  [Tooltip("Root of the Matt prefab instance (skinned mesh + bones).")]
  public Transform visual;

  // bone (Mecanim name in the rig) -> MuJoCo body it rides on
  static readonly (string bone, string body)[] Map = {
    ("Hips", "pelvis"), ("Spine", "torso_link"), ("Chest", "torso_link"), ("UpperChest", "torso_link"), ("Neck", "torso_link"), ("Head", "torso_link"),
    ("LeftShoulder", "torso_link"), ("RightShoulder", "torso_link"),
    ("LeftUpperArm", "left_shoulder_yaw_link"), ("LeftLowerArm", "left_elbow_link"), ("LeftHand", "left_wrist_yaw_link"),
    ("RightUpperArm", "right_shoulder_yaw_link"), ("RightLowerArm", "right_elbow_link"), ("RightHand", "right_wrist_yaw_link"),
    ("LeftUpperLeg", "left_hip_yaw_link"), ("LeftLowerLeg", "left_knee_link"), ("LeftFoot", "left_ankle_roll_link"), ("LeftToes", "left_ankle_roll_link"),
    ("RightUpperLeg", "right_hip_yaw_link"), ("RightLowerLeg", "right_knee_link"), ("RightFoot", "right_ankle_roll_link"), ("RightToes", "right_ankle_roll_link"),
  };

  readonly List<(Transform bone, Transform body, Quaternion rest)> _links = new List<(Transform, Transform, Quaternion)>();
  Transform _hips, _pelvis; Vector3 _hipsOffset;

  void Start() {
    if (visual == null) { enabled = false; return; }
    foreach (var a in visual.GetComponentsInChildren<Animator>()) a.enabled = false;   // the bodies pose the bones
    // The visual root stays at the origin while the bones travel with the MuJoCo bodies, so culling bounds tied to the
    // root would hide Matt whenever the origin is off screen: bound the posed skin every frame instead.
    foreach (var s in visual.GetComponentsInChildren<SkinnedMeshRenderer>()) s.updateWhenOffscreen = true;
    // Rest pose with the scan facing MuJoCo +x (Unity +x): root at the origin, turned +90 deg about up.
    visual.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 90f, 0f));
    var bones = new Dictionary<string, Transform>();
    foreach (var t in visual.GetComponentsInChildren<Transform>()) bones[t.name] = t;
    var pelvisRest = new Vector3(0f, MattSpec.PelvisRestZ, 0f);   // MuJoCo (0, 0, z) -> Unity (0, z, 0) at zero pose
    foreach (var (bone, body) in Map) {
      var go = GameObject.Find(body);
      if (!bones.TryGetValue(bone, out var b) || go == null) { Debug.LogWarning($"[MattSkin] {bone} -> {body} not found"); continue; }
      _links.Add((b, go.transform, b.rotation));
      if (bone == "Hips") { _hips = b; _pelvis = go.transform; _hipsOffset = b.position - pelvisRest; }
    }
    Debug.Log($"[MattSkin] {_links.Count}/{Map.Length} bones bound; hips-to-pelvis rest offset {_hipsOffset.magnitude * 1000f:F1} mm");
  }

  void LateUpdate() {
    if (_hips == null) return;
    _hips.position = _pelvis.position + _pelvis.rotation * _hipsOffset;
    foreach (var (bone, body, rest) in _links) bone.rotation = body.rotation * rest;   // parents before children (Map order)
  }
}

}
