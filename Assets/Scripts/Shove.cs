using UnityEngine;

namespace PoRace {

/// <summary>External push on the base body via mjData.xfrc_applied, mirroring the trainer's velocity kicks.
/// Impulse (N·s, capped at the trained 30 N·s R2 bar) is spread over durationSteps physics steps as a constant force.
/// Written through ExternalForces so it adds to wind instead of overwriting it. PushRobot is the one implementation:
/// the HUD, chat hazards and the stress test all go through it.</summary>
public class Shove : MonoBehaviour {
  public const float MaxImpulse = 30f;   // N·s, contract R2 bar
  public Go2Controller target;       // robot the instance methods push (the testbed dog on this object when unset)
  public float impulse = 30f;        // N·s, capped at MaxImpulse
  public int durationSteps = 25;     // 0.1 s at 4 ms

  void Awake() {
    if (target == null) target = GetComponent<Go2Controller>();   // testbeds: Shove sits on the dog
    _ = ExternalForces.Instance;   // subscribe the force writer before the first step
  }

  /// <summary>Direction of the last push (any path), MuJoCo frame.</summary>
  public static Vector3 LastDir { get; private set; }

  /// <summary>Push `target` in a MuJoCo-frame horizontal direction.</summary>
  public void Push(Vector3 mjDir) => PushRobot(target, mjDir, impulse, durationSteps);
  public void PushRandom() => PushRobot(target, impulse, durationSteps);

  /// <summary>Push any racer in a MuJoCo-frame direction; impulse capped at the trained R2 bar. No-op until it is Ready.</summary>
  public static void PushRobot(IRacer robot, Vector3 mjDir, float impulseNs = MaxImpulse, int steps = 25) {
    if (robot == null || !robot.Ready) return;
    impulseNs = Mathf.Min(impulseNs, MaxImpulse); LastDir = mjDir.normalized;
    ExternalForces.Instance.AddPush(robot.BaseBody, LastDir * (impulseNs / (steps * Go2Controller.SimDt)), steps);
  }

  /// <summary>Push in a random horizontal direction.</summary>
  public static void PushRobot(IRacer robot, float impulseNs = MaxImpulse, int steps = 25) {
    float ang = Random.Range(0f, Mathf.PI * 2f);
    PushRobot(robot, new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f), impulseNs, steps);
  }
}

}
