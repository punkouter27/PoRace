using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace PoRace {

/// <summary>Live chat input for features 5 (hazards), 6 (bets) and the crowd nudge vote (NudgeVote, !nudge &lt;who&gt;). Joins a Twitch channel's chat anonymously and
/// read-only (IRC over WebSocket, nick justinfanNNNN: no account, token or app registration needed) and raises
/// Message(user, text) on the main thread. Channel: menu field (RaceConfig.twitchChannel) or -twitch &lt;channel&gt;.
/// -chatScript "4:alice:!drop gold|9:bob:!bet blue" injects messages at race seconds (tests, demos, no network).</summary>
public class ChatLink : MonoBehaviour {
  public MultiRaceOrchestrator race;
  public string channel = "";
  public event Action<string, string> Message;
  public string Status { get; private set; } = "off";

  readonly ConcurrentQueue<(string user, string text)> _in = new ConcurrentQueue<(string, string)>();
  CancellationTokenSource _cts;
  (float t, string user, string text)[] _script = Array.Empty<(float, string, string)>(); int _scriptNext;

  void Start() {
    if (race == null) race = FindAnyObjectByType<MultiRaceOrchestrator>();
    bool test = CliArgs.RaceTest;   // headless tests: only an explicit -twitch / -chatScript
    if (RaceConfig.fromMenu && !test && !string.IsNullOrWhiteSpace(RaceConfig.twitchChannel)) channel = RaceConfig.twitchChannel;
    if (CliArgs.TryString("-twitch", out var tw)) channel = tw;
    if (CliArgs.TryString("-chatScript", out var cs)) _script = Array.ConvertAll(cs.Split('|'), e => {
      var p = e.Split(new[] { ':' }, 3); return (float.Parse(p[0], CultureInfo.InvariantCulture), p[1], p[2]);
    });
    channel = channel.Trim().TrimStart('#').ToLowerInvariant();
    if (channel.Length > 0) { _cts = new CancellationTokenSource(); _ = Run(channel, _cts.Token); }
    else if (_script.Length > 0) Status = "script";
  }

  void OnDestroy() { _cts?.Cancel(); }

  /// <summary>A chat line as a lower-case "!command" and its first argument ("" when none); false when it is not a
  /// command. For the chat consumers (NudgeVote; HazardDesk parses its own).</summary>
  public static bool Parse(string text, out string cmd, out string arg) {
    cmd = arg = "";
    if (string.IsNullOrEmpty(text)) return false;
    var parts = text.Trim().ToLowerInvariant().Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length == 0 || parts[0].Length < 2 || parts[0][0] != '!') return false;
    cmd = parts[0]; arg = parts.Length > 1 ? parts[1] : "";
    return true;
  }

  /// <summary>Local input (keyboard / buttons / script) goes through the same path as chat.</summary>
  public void Inject(string user, string text) => _in.Enqueue((user, text));

  void Update() {
    while (_scriptNext < _script.Length && race != null && race.Phase != RacePhase.Countdown && race.RaceTime >= _script[_scriptNext].t) {
      var e = _script[_scriptNext++]; Inject(e.user, e.text);
    }
    if (_scriptNext < _script.Length && race != null && race.Phase == RacePhase.Countdown && _script[_scriptNext].t < 0) { var e = _script[_scriptNext++]; Inject(e.user, e.text); }
    while (_in.TryDequeue(out var m)) Message?.Invoke(m.user, m.text);
  }

  async Task Run(string chan, CancellationToken ct) {
    int attempt = 0;
    while (!ct.IsCancellationRequested) {
      try {
        using (var ws = new ClientWebSocket()) {
          Status = "connecting";
          await ws.ConnectAsync(new Uri("wss://irc-ws.chat.twitch.tv:443"), ct);
          await Send(ws, "PASS SCHMOOPIIE", ct);
          await Send(ws, "NICK justinfan" + new System.Random().Next(10000, 99999), ct);
          await Send(ws, "JOIN #" + chan, ct);
          Status = "live #" + chan; attempt = 0;
          var buf = new byte[8192]; var sb = new StringBuilder();
          while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested) {
            var res = await ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);
            if (res.MessageType == WebSocketMessageType.Close) break;
            sb.Append(Encoding.UTF8.GetString(buf, 0, res.Count));
            if (!res.EndOfMessage) continue;
            foreach (var line in sb.ToString().Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries)) {
              if (line.StartsWith("PING")) { await Send(ws, "PONG" + line.Substring(4), ct); continue; }
              // :user!user@user.tmi.twitch.tv PRIVMSG #chan :text
              int priv = line.IndexOf(" PRIVMSG #", StringComparison.Ordinal);
              if (priv < 0 || line[0] != ':') continue;
              int bang = line.IndexOf('!'); int colon = line.IndexOf(" :", priv, StringComparison.Ordinal);
              if (bang < 0 || colon < 0) continue;
              _in.Enqueue((line.Substring(1, bang - 1), line.Substring(colon + 2)));
            }
            sb.Clear();
          }
        }
      } catch (OperationCanceledException) { return; }
      catch (Exception e) { Status = "offline (" + e.GetType().Name + ")"; Debug.LogWarning("[ChatLink] " + e.Message); }
      await Task.Delay(Math.Min(30000, 2000 << Math.Min(attempt++, 4)), ct).ContinueWith(_ => { });
    }
  }

  static Task Send(ClientWebSocket ws, string line, CancellationToken ct) =>
      ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(line + "\r\n")), WebSocketMessageType.Text, true, ct);
}

}
