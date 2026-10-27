using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PoRace {

/// <summary>Feature 5, viewer hazards. Chat commands (ChatLink) hit a chosen racer through the same MuJoCo state
/// writes the HUD buttons use: !drop &lt;who&gt; (pooled cube from 1 m, training C.2), !cube &lt;who&gt; (thrown cube),
/// !shove &lt;who&gt; (impulse capped at the trained 30 N·s). &lt;who&gt; = racer name, leader, last, or a place (p2);
/// default leader. Only while racing, 15 s cooldown per viewer and 2.5 s between any two hazards.</summary>
public class HazardDesk : MonoBehaviour {
  public MultiRaceOrchestrator race;
  public ChatLink chat;
  public float userCooldown = 15f, globalCooldown = 2.5f;
  public int Fired { get; private set; }

  readonly Dictionary<string, float> _lastByUser = new Dictionary<string, float>();
  float _lastAny = -100f;

  void Start() {
    if (race == null) race = FindAnyObjectByType<MultiRaceOrchestrator>();
    if (chat == null) chat = FindAnyObjectByType<ChatLink>();
    if (chat != null) chat.Message += OnChat;
  }

  void OnChat(string user, string text) {
    var parts = text.Trim().ToLowerInvariant().Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length == 0 || (parts[0] != "!drop" && parts[0] != "!cube" && parts[0] != "!shove")) return;
    if (race.Phase != RacePhase.Racing) return;
    float now = (float)race.RaceTime;
    if (now - _lastAny < globalCooldown) return;
    if (_lastByUser.TryGetValue(user, out float t) && now - t < userCooldown) return;
    var target = Resolve(parts.Length > 1 ? parts[1] : "leader");
    if (target == null || target.finishTime >= 0) return;
    _lastAny = now; _lastByUser[user] = now; Fired++;
    var who = BroadcastUi.Paint(target.name, target.color); var u = BroadcastUi.Paint(user, BroadcastUi.Accent);
    switch (parts[0]) {
      case "!drop": CubePool.Instance.DropOnRobot(target.Body); BroadcastTicker.Post($"⚡ {u} dropped a cube on {who}"); break;
      case "!cube": CubePool.Instance.ThrowAtRobot(3f, target.Body); BroadcastTicker.Post($"⚡ {u} threw a cube at {who}"); break;
      case "!shove":
        float a = Random.Range(0f, Mathf.PI * 2f);
        Shove.PushRobot(target.Body, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f));
        BroadcastTicker.Post($"⚡ {u} shoved {who}"); break;
    }
    Debug.Log($"[Hazard] t={now:F1}s {user} {parts[0]} -> {target.name}");
  }

  /// <summary>Racer from a chat word: name, leader / first, last, or p1..p4.</summary>
  public Racer Resolve(string word) {
    var order = race.Live.Where(r => r.finishTime < 0).OrderByDescending(r => r.distance).ToList();
    if (order.Count == 0) return null;
    if (word == "leader" || word == "first") return order[0];
    if (word == "last") return order[order.Count - 1];
    if (word.Length == 2 && word[0] == 'p' && char.IsDigit(word[1])) { int k = word[1] - '1'; return k >= 0 && k < order.Count ? order[k] : null; }
    return race.Live.FirstOrDefault(r => r.name.ToLowerInvariant() == word) ?? order[0];
  }
}

}
