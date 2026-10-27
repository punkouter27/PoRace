using Mujoco;
using UnityEngine;

namespace PoRace {

/// <summary>External push on the base body via mjData.xfrc_applied, mirroring the trainer's velocity kicks.
/// Impulse (N·s) is spread over durationSteps physics steps as a constant force. Written through ExternalForces so it
/// adds to wind instead of overwriting it.</summary>
public unsafe class Shove : MonoBehaviour {
  public Go2Controller target;       // robot to push ("" prefix robot when null)
  public float impulse = 30f;        // N·s, contract R2 bar
  public int durationSteps = 25;     // 0.1 s at 4 ms
  int _baseBody = -1;

  void Awake() {
    MjScene.Instance.postInitEvent += (s, a) => _baseBody = MujocoLib.mj_name2id(a.model, (int)MujocoLib.mjtObj.mjOBJ_BODY, (target != null ? target.prefix : "") + "base");
    _ = ExternalForces.Instance;   // subscribe the force writer before the first step
  }

  /// <summary>Push in a MuJoCo-frame horizontal direction (unit vector).</summary>
  public Vector3 LastDir { get; private set; }
  public void Push(Vector3 mjDir) {
    LastDir = mjDir.normalized;
    if (target != null && target.Model != null) _baseBody = target.Model.BaseBody;  // target may change at runtime (race leader)
    ExternalForces.Instance.AddPush(_baseBody, mjDir.normalized * (impulse / (durationSteps * Go2Controller.SimDt)), durationSteps);
  }

  /// <summary>Push any robot with the same profile (chat hazards); impulse capped at the trained R2 bar.</summary>
  public static void PushRobot(Go2Controller robot, Vector3 mjDir, float impulseNs = 30f, int steps = 25) {
    if (robot == null || robot.Model == null) return;
    impulseNs = Mathf.Min(impulseNs, 30f);
    ExternalForces.Instance.AddPush(robot.Model.BaseBody, mjDir.normalized * (impulseNs / (steps * Go2Controller.SimDt)), steps);
  }

  public void PushRandom() {
    float ang = Random.Range(0f, Mathf.PI * 2f);
    Push(new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f));
  }
}

}
