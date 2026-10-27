using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PoRace {

/// <summary>Feature 3, visible intent: an arrow from each racer to the point it is steering at (its racing line plus
/// tactical offset, MultiRaceOrchestrator.Tactic) and a tag over the dog while it is passing, avoiding or blocking.
/// I toggles. Visual only.</summary>
public class IntentArrows : MonoBehaviour {
  public MultiRaceOrchestrator race;
  public Material lineMaterial;
  public bool show = true;

  class View { public LineRenderer line; public Text tag; }
  readonly Dictionary<Racer, View> _v = new Dictionary<Racer, View>();
  Camera _cam;

  void Start() {
    if (race == null) race = FindAnyObjectByType<MultiRaceOrchestrator>();
    if (BroadcastUi.Headless) { enabled = false; return; }
    _cam = race.followCam != null ? race.followCam.GetComponent<Camera>() : Camera.main;
    foreach (var r in race.Live) {
      var go = new GameObject("Intent_" + r.name); go.transform.SetParent(transform, false);
      var lr = go.AddComponent<LineRenderer>(); lr.positionCount = 5; lr.widthMultiplier = 0.035f; lr.numCapVertices = 2;
      lr.sharedMaterial = lineMaterial; lr.startColor = new Color(r.color.r, r.color.g, r.color.b, 0.25f); lr.endColor = new Color(r.color.r, r.color.g, r.color.b, 0.9f);
      lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false;
      var tag = BroadcastUi.Label(BroadcastUi.Root, "IntentTag_" + r.name, Vector2.zero, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(160, 30), 20, TextAnchor.MiddleCenter);
      tag.color = r.color;
      _v[r] = new View { line = lr, tag = tag };
    }
  }

  void LateUpdate() {
    if (Keyboard.current != null && Keyboard.current.iKey.wasPressedThisFrame) show = !show;
    var canvas = BroadcastUi.Root as RectTransform;
    foreach (var kv in _v) {
      var r = kv.Key; var v = kv.Value; var b = r.Body.BaseTransform;
      bool on = show && b != null && race.Phase == RacePhase.Racing && r.finishTime < 0;
      v.line.enabled = on; v.tag.enabled = on && r.intent != "";
      if (!on) continue;
      var tp = race.map.LanePoint(r.distance + race.map.lookAhead, r.lane, out var t) + MapDefinition.Left(t) * r.offset;
      float h = 0.32f + race.map.SurfaceHeightAt(tp);
      Vector3 a = b.position + Vector3.up * 0.12f, z = new Vector3(tp.x, h, tp.y);
      var dir = (z - a); dir.y = 0; dir.Normalize(); var side = Vector3.Cross(Vector3.up, dir);
      v.line.SetPositions(new[] { a, z, z - dir * 0.25f + side * 0.15f, z, z - dir * 0.25f - side * 0.15f });
      if (v.tag.enabled && _cam != null) {
        var sp = _cam.WorldToScreenPoint(b.position + Vector3.up * 0.45f);
        if (sp.z < 0) { v.tag.enabled = false; continue; }
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, sp, null, out var lp);
        v.tag.rectTransform.anchorMin = v.tag.rectTransform.anchorMax = new Vector2(0.5f, 0.5f); v.tag.rectTransform.anchoredPosition = lp;
        v.tag.text = r.intent;
      }
    }
  }
}

}
