using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Mujoco;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace PoRace {

/// <summary>Feature 9, telemetry and post-race analytics. Every racer is logged at 10 Hz to JSONL (position, heading,
/// speed, command, joint effort, power, state, place) and summarised at the finish:
///   time, mean / top speed, cost of transport = energy / (m g d) with energy = integral of sum |tau * qdot|,
///   cadence (foot strikes per second), peak joint effort, falls, overtakes, un-sticks, respawns.
/// Files: &lt;persistentDataPath&gt;/telemetry/&lt;yyMMdd-HHmmss&gt;_&lt;map&gt;_s&lt;seed&gt;.jsonl + _summary.json, or -telemetryDir &lt;dir&gt;.
/// The summary rows also go to Azure Table Storage when a table SAS URL is given (-azureTableSas, env
/// PORACE_AZURE_TABLE_SAS, or PlayerPrefs PoRace.azureSas): https://&lt;acct&gt;.table.core.windows.net/&lt;table&gt;?&lt;sas&gt;.
/// A stats card shows the summary when the race ends.</summary>
public unsafe class RaceTelemetry : MonoBehaviour {
  public MultiRaceOrchestrator race;
  public RaceSensors sensors;
  public StrainMonitor strain;
  public ContactImpulse contacts;
  public float hz = 10f;

  [Serializable] public class Summary {
    public string racer, map, stamp; public int seed, place, falls, overtakes, unsticks, resets, steps, redlines;
    public float time, distance, meanSpeed, topSpeed, energy, costOfTransport, cadence, peakEffort, mass, wear;   // wear: N·s of body / cube / robot hits (ContactImpulse)
  }
  [Serializable] class SummaryFile { public List<Summary> racers = new List<Summary>(); }

  StreamWriter _w; string _dir, _base, _sas; float _next; int _step;
  readonly Dictionary<Racer, (float top, float startEnergy, int startStrikes)> _acc = new Dictionary<Racer, (float, float, int)>();
  readonly StringBuilder _sb = new StringBuilder();
  Image _card; Text _cardText; bool _written;
  public string LastSummaryPath { get; private set; }

  void Start() {
    if (race == null) race = FindAnyObjectByType<MultiRaceOrchestrator>();
    if (sensors == null) sensors = FindAnyObjectByType<RaceSensors>();
    if (strain == null) strain = FindAnyObjectByType<StrainMonitor>();
    if (contacts == null) contacts = FindAnyObjectByType<ContactImpulse>();
    _dir = Path.Combine(Application.persistentDataPath, "telemetry");
    var args = Environment.GetCommandLineArgs();
    for (int i = 0; i + 1 < args.Length; i++) { if (args[i] == "-telemetryDir") _dir = args[i + 1]; if (args[i] == "-azureTableSas") _sas = args[i + 1]; }
    if (string.IsNullOrEmpty(_sas)) _sas = Environment.GetEnvironmentVariable("PORACE_AZURE_TABLE_SAS");
    if (string.IsNullOrEmpty(_sas)) _sas = PlayerPrefs.GetString("PoRace.azureSas", "");
    race.PhaseChanged += p => { if (p == RacePhase.Racing) Open(); if (p == RacePhase.Finished) Close(true); };
    race.Restarted += () => { Close(false); if (_card != null) _card.gameObject.SetActive(false); };
    if (!BroadcastUi.Headless) {
      _card = BroadcastUi.Panel(BroadcastUi.Root, "StatsCard", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -40), new Vector2(690, 470), BroadcastUi.Dark);
      _cardText = BroadcastUi.Label(_card.transform, "Stats", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(660, 450), 20, TextAnchor.UpperLeft);
      _card.gameObject.SetActive(false);
    }
  }

  void OnDestroy() => Close(false);

  void Open() {
    Close(false);
    Directory.CreateDirectory(_dir);
    _base = Path.Combine(_dir, $"{DateTime.Now:yyMMdd-HHmmss}_{race.map.name}_s{race.Seed}");
    _w = new StreamWriter(_base + ".jsonl", false, new UTF8Encoding(false));
    _acc.Clear(); _written = false; _next = 0f;
    foreach (var r in race.Live) { var s = sensors?.Of(r); _acc[r] = (0f, s?.energy ?? 0f, s?.strikes ?? 0); }
  }

  void Close(bool summarize) {
    if (summarize && _w != null && !_written) WriteSummary();
    _w?.Dispose(); _w = null;
  }

  void FixedUpdate() {
    if (_w == null || race.Phase != RacePhase.Racing || sensors == null) return;
    foreach (var r in race.Live) { var s = sensors.Of(r); if (s != null && r.finishTime < 0) { var a = _acc[r]; a.top = Mathf.Max(a.top, s.speed); _acc[r] = a; } }
    if ((float)race.RaceTime < _next) return;
    _next = (float)race.RaceTime + 1f / hz;
    var d = MjScene.Instance.Data; var ci = CultureInfo.InvariantCulture;
    foreach (var r in race.Live) {
      var s = sensors.Of(r); if (s == null) continue; var M = r.controller.Model; var c = r.controller.command;
      double qw = d->qpos[M.BaseQpos + 3], qx = d->qpos[M.BaseQpos + 4], qy = d->qpos[M.BaseQpos + 5], qz = d->qpos[M.BaseQpos + 6];
      double yaw = Math.Atan2(2 * (qw * qz + qx * qy), 1 - 2 * (qy * qy + qz * qz));
      _sb.Clear();
      _sb.AppendFormat(ci, "{{\"t\":{0:F2},\"racer\":\"{1}\",\"x\":{2:F3},\"y\":{3:F3},\"z\":{4:F3},\"yaw\":{5:F3},\"v\":{6:F3},\"cmd\":[{7:F2},{8:F2},{9:F2}],",
          race.RaceTime, r.name, d->qpos[M.BaseQpos], d->qpos[M.BaseQpos + 1], d->qpos[M.BaseQpos + 2], yaw, s.speed, c.x, c.y, c.z);
      _sb.AppendFormat(ci, "\"effort\":[{0}],\"power\":{1:F1},\"state\":\"{2}\",\"dist\":{3:F2},\"lat\":{4:F2},\"intent\":\"{5}\",\"mode\":\"{6}\"}}",
          string.Join(",", s.effort.Select(e => e.ToString("F2", ci))), s.power, r.state, r.distance, r.lateral, r.intent, r.controller.Active);
      _w.WriteLine(_sb.ToString());
    }
  }

  public List<Summary> Summaries() {
    string stamp = Path.GetFileName(_base ?? DateTime.Now.ToString("yyMMdd-HHmmss"));
    return race.Live.Select(r => {
      var s = sensors?.Of(r); var a = _acc.TryGetValue(r, out var v) ? v : (0f, 0f, 0);
      float time = r.finishTime >= 0 ? (float)r.finishTime : (float)race.RaceTime, e = (s?.energy ?? 0f) - a.Item2, m = s?.mass ?? 15.2f;
      int strikes = (s?.strikes ?? 0) - a.Item3;
      return new Summary {
        racer = r.name, map = race.map.name, stamp = stamp, seed = race.Seed, place = r.place, falls = r.falls, overtakes = r.overtakes, unsticks = r.unsticks,
        resets = r.autoReset != null ? r.autoReset.Resets : 0, steps = strikes, redlines = strain != null ? strain.RedlineCount(r) : 0,
        time = time, distance = r.distance, meanSpeed = time > 0 ? r.distance / time : 0f, topSpeed = a.Item1, energy = e, mass = m,
        costOfTransport = r.distance > 1f ? e / (m * 9.81f * r.distance) : 0f, cadence = time > 0 ? strikes / time : 0f,
        peakEffort = strain != null ? strain.Peak(r) : 0f, wear = contacts != null ? contacts.Wear(r) : 0f };
    }).OrderBy(x => x.place > 0 ? x.place : 99).ToList();
  }

  void WriteSummary() {
    _written = true;
    var rows = Summaries();
    LastSummaryPath = _base + "_summary.json";
    File.WriteAllText(LastSummaryPath, JsonUtility.ToJson(new SummaryFile { racers = rows }, true));
    foreach (var x in rows)
      Debug.Log(string.Format(CultureInfo.InvariantCulture, "[Telemetry] {0} P{1} {2:F2}s mean {3:F2} top {4:F2} m/s CoT {5:F2} cadence {6:F1}/s peak effort {7:F0}% falls {8} passes {9} redlines {10} wear {11:F1} N·s",
          x.racer, x.place, x.time, x.meanSpeed, x.topSpeed, x.costOfTransport, x.cadence, x.peakEffort * 100, x.falls, x.overtakes, x.redlines, x.wear));
    Debug.Log("[Telemetry] " + LastSummaryPath);
    if (!string.IsNullOrEmpty(_sas)) StartCoroutine(Upload(rows));
    if (_cardText != null) ShowCard(rows);
  }

  void ShowCard(List<Summary> rows) {
    var sb = new StringBuilder();
    sb.Append($"<size=30><b>RACE STATS</b></size>   <size=18>{race.map.displayName}  seed {race.Seed}</size>\n\n");
    sb.Append("<size=17>#  racer      time     avg   top   CoT   steps/s  peak  falls passes</size>\n");
    foreach (var x in rows) {
      var r = race.Live.First(q => q.name == x.racer);
      sb.Append(string.Format(CultureInfo.InvariantCulture, "{0}  {1,-9} {2,7:F2}s  {3:F2}  {4:F2}  {5:F2}   {6,4:F1}    {7,3:F0}%   {8}     {9}\n",
          x.place > 0 ? x.place.ToString() : "-", BroadcastUi.Paint(x.racer.PadRight(9), r.color), x.time, x.meanSpeed, x.topSpeed, x.costOfTransport, x.cadence, x.peakEffort * 100, x.falls, x.overtakes));
    }
    sb.Append("\n<size=16>speeds m/s · CoT = energy / (m g d) · peak = highest joint effort vs 24 N·m</size>");
    _cardText.text = sb.ToString(); _card.gameObject.SetActive(true);
  }

  IEnumerator Upload(List<Summary> rows) {
    var ci = CultureInfo.InvariantCulture; int ok = 0;
    foreach (var x in rows) {
      string body = "{" + string.Join(",", new[] {
        $"\"PartitionKey\":\"{x.map}\"", $"\"RowKey\":\"{x.stamp}_{x.racer}\"", $"\"Seed\":{x.seed}", $"\"Place\":{x.place}",
        string.Format(ci, "\"Time\":{0:F3}", x.time), string.Format(ci, "\"MeanSpeed\":{0:F3}", x.meanSpeed), string.Format(ci, "\"TopSpeed\":{0:F3}", x.topSpeed),
        string.Format(ci, "\"CostOfTransport\":{0:F3}", x.costOfTransport), string.Format(ci, "\"Cadence\":{0:F2}", x.cadence),
        string.Format(ci, "\"PeakEffort\":{0:F3}", x.peakEffort), string.Format(ci, "\"Wear\":{0:F2}", x.wear), $"\"Falls\":{x.falls}", $"\"Overtakes\":{x.overtakes}", $"\"Racer\":\"{x.racer}\"" }) + "}";
      using (var req = new UnityWebRequest(_sas, "POST")) {
        req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)); req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json"); req.SetRequestHeader("Accept", "application/json;odata=nometadata");
        req.SetRequestHeader("Prefer", "return-no-content"); req.SetRequestHeader("x-ms-version", "2019-02-02");
        req.timeout = 10;
        yield return req.SendWebRequest();
        if (req.result == UnityWebRequest.Result.Success) ok++; else Debug.LogWarning($"[Telemetry] Azure insert {x.racer}: {req.responseCode} {req.error}");
      }
    }
    Debug.Log($"[Telemetry] Azure Table: {ok}/{rows.Count} rows inserted");
  }
}

}
