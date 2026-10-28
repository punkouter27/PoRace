using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PoRace {

/// <summary>Feature 5, viewer hazards. Chat commands (ChatLink) hit a chosen racer through the same MuJoCo state
/// writes the HUD buttons use: !drop &lt;who&gt; (pooled cube from 1 m, training C.2), !cube &lt;who&gt; (thrown cube),
/// !shove &lt;who&gt; (impulse capped at the trained 30 N·s). &lt;who&gt; = racer name, leader, last, or a place (p2);
/// default leader. Only while racing, 15 s cooldown per viewer and 2.5 s between any two hazards. The on-screen
/// radial picker (HazardRadial, C1) fires through TryFire as viewer Local under the same rules.</summary>
public class HazardDesk : MonoBehaviour {
  public MultiRaceOrchestrator race;
  public ChatLink chat;
  public float userCooldown = 15f, globalCooldown = 2.5f;
  public int Fired { get; private set; }
  /// <summary>Viewer name of the person holding the phone (radial picker).</summary>
  public const string Local = "you";
  public static readonly string[] Kinds = { "drop", "cube", "shove" };

  readonly Dictionary<string, float> _lastByUser = new Dictionary<string, float>();
  float _lastAny = -100f;

  void Start() {
    if (!BroadcastUi.Wired(this, race, nameof(race))) return;
    if (chat == null) chat = FindAnyObjectByType<ChatLink>();
    if (chat != null) chat.Message += OnChat;
    // cooldowns run on race time, which restarts at 0: stale stamps would block hazards for the whole next race
    race.Restarted += () => { _lastByUser.Clear(); _lastAny = -100f; };
  }

  void OnChat(string user, string text) {
    var parts = text.Trim().ToLowerInvariant().Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length == 0 || (parts[0] != "!drop" && parts[0] != "!cube" && parts[0] != "!shove")) return;
    if (race.Phase != RacePhase.Racing) return;
    TryFire(user, parts[0].Substring(1), Resolve(parts.Length > 1 ? parts[1] : "leader"));
  }

  /// <summary>Hazard `kind` (drop / cube / shove) from viewer `user` at `target`, under the chat rules (racing only,
  /// per-viewer and global cooldowns, never a finished racer). False when refused.</summary>
  public bool TryFire(string user, string kind, Racer target) {
    if (race == null || race.Phase != RacePhase.Racing || CooldownLeft(user) > 0f) return false;
    if (target == null || target.finishTime >= 0 || System.Array.IndexOf(Kinds, kind) < 0) return false;
    float now = (float)race.RaceTime;
    _lastAny = now; _lastByUser[user] = now; Fired++;
    var who = BroadcastUi.Paint(target.name, target.color); var u = BroadcastUi.Paint(user, BroadcastUi.Accent);
    switch (kind) {
      case "drop": CubePool.Instance.DropOnRobot(target.Body); BroadcastTicker.Post($"⚡ {u} dropped a cube on {who}"); break;
      case "cube": CubePool.Instance.ThrowAtRobot(3f, target.Body); BroadcastTicker.Post($"⚡ {u} threw a cube at {who}"); break;
      case "shove":
        Shove.PushRobot(target.Body);
        BroadcastTicker.Post($"⚡ {u} shoved {who}"); break;
    }
    Debug.Log($"[Hazard] t={now:F1}s {user} !{kind} -> {target.name}");
    return true;
  }

  /// <summary>Seconds until `user` may fire again (the longer of its own and the global cooldown); 0 = ready.</summary>
  public float CooldownLeft(string user) => CooldownLeft(user, out _);

  /// <summary>As CooldownLeft, plus the left fraction (0..1) of whichever cooldown is binding, for a ring.</summary>
  public float CooldownLeft(string user, out float fraction) {
    fraction = 0f;
    if (race == null) return 0f;
    float now = (float)race.RaceTime, any = globalCooldown - (now - _lastAny);
    float own = _lastByUser.TryGetValue(user, out float t) ? userCooldown - (now - t) : 0f;
    if (own <= 0f && any <= 0f) return 0f;
    if (own >= any) { fraction = own / userCooldown; return own; }
    fraction = any / globalCooldown; return any;
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
