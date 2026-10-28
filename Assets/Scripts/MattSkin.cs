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
    // -90, not +90: at +90 the skin's rest frame was the physics frame turned 180 deg about up (the shin in the knee frame
    // read (0.18, -0.98, -0.04) vs the body's (-0.18, -0.98, 0.04)), so every knee / hip bend copied from the bodies bent
    // the drawn legs the other way: straighter legs, feet ~8 cm under the floor standing, the torso sunk when lying.
    visual.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, -90f, 0f));
    var bones = new Dictionary<string, Transform>();
    foreach (var t in visual.GetComponentsInChildren<Transform>()) bones[t.name] = t;
    // Rest = the bind pose the skin was bound in (and the MJCF joints were fitted to), from the mesh's bindposes: the
    // prefab's bone transforms carry a saved preview pose (knees ~17 deg bent), and taking that as rest left the skin's
    // legs straighter than the physics, sinking it ~9 cm into the floor whenever the knees bent.
    var bind = new Dictionary<Transform, Matrix4x4>();
    foreach (var s in visual.GetComponentsInChildren<SkinnedMeshRenderer>()) {
      var bp = s.sharedMesh != null ? s.sharedMesh.bindposes : null; if (bp == null) continue;
      for (int i = 0; i < s.bones.Length && i < bp.Length; i++)
        if (s.bones[i] != null && !bind.ContainsKey(s.bones[i])) bind[s.bones[i]] = s.transform.localToWorldMatrix * bp[i].inverse;
    }
    // Put every bone at its bind pose before reading rest rotations and the hips offset.
    foreach (var t in visual.GetComponentsInChildren<Transform>())
      if (bind.TryGetValue(t, out var w)) t.SetPositionAndRotation(w.GetPosition(), w.rotation);
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
    if (_probe && Time.time >= _probeNext) { _probeNext = Time.time + 0.5f; SkinProbe(); }
  }

  static readonly bool _probe = CliArgs.Has("-contactProbe");
  float _probeNext; Mesh _baked;
  /// <summary>-contactProbe: lowest skinned vertex (Unity y = MuJoCo z) against the floor at 0, next to the pelvis body height.</summary>
  void SkinProbe() {
    float low = float.MaxValue;
    foreach (var s in visual.GetComponentsInChildren<SkinnedMeshRenderer>()) {
      if (_baked == null) _baked = new Mesh();
      s.BakeMesh(_baked, true); var m = s.transform.localToWorldMatrix;
      foreach (var v in _baked.vertices) low = Mathf.Min(low, m.MultiplyPoint3x4(v).y);
    }
    var sb = new System.Text.StringBuilder();
    Transform Bn(string n) { foreach (var l in _links) if (l.bone.name == n) return l.bone; return null; }
    Transform Bd(string n) { foreach (var l in _links) if (l.bone.name == n) return l.body; return null; }
    Vector3 shinBone = Bn("LeftFoot").position - Bn("LeftLowerLeg").position, shinBody = Bd("LeftFoot").position - Bd("LeftLowerLeg").position;
    sb.Append($" shinAngle bone{Vector3.Angle(shinBone, Vector3.down):F1}/body{Vector3.Angle(shinBody, Vector3.down):F1}");
    sb.Append($" kneeFlex bone{Quaternion.Angle(Bn("LeftUpperLeg").rotation, Bn("LeftLowerLeg").rotation):F1}/body{Quaternion.Angle(Bd("LeftUpperLeg").rotation, Bd("LeftLowerLeg").rotation):F1}");
    sb.Append($" hipFlex bone{Quaternion.Angle(_hips.rotation, Bn("LeftUpperLeg").rotation):F1}/body{Quaternion.Angle(_pelvis.rotation, Bd("LeftUpperLeg").rotation):F1}");
    sb.Append($" shinLocal body{(Quaternion.Inverse(Bd("LeftLowerLeg").rotation) * shinBody).normalized} bone{(Quaternion.Inverse(Bd("LeftLowerLeg").rotation) * shinBone).normalized}");
    foreach (var (bone, body, _) in _links)
      if (bone.name.StartsWith("Left") && (bone.name.Contains("Leg") || bone.name.EndsWith("Foot")))
        sb.Append($" {bone.name} bone{bone.position.y:F3}/body{body.position.y:F3}");
    Debug.Log($"[SkinProbe] t={Time.timeSinceLevelLoad:F1} pelvisY={_pelvis.position.y:F3} hipsY={_hips.position.y:F3} lowestSkinY={low:F3}{sb}");
  }
}

}
