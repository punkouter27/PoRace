using System;
using UnityEngine;

namespace PoRace {

/// <summary>Forwards pointer down / drag / up on a UI element to callbacks (press-and-slide controls).</summary>
public class UiPointer : MonoBehaviour, UnityEngine.EventSystems.IPointerDownHandler, UnityEngine.EventSystems.IDragHandler, UnityEngine.EventSystems.IPointerUpHandler {
  public Action<UnityEngine.EventSystems.PointerEventData> Down, Drag, Up;
  public void OnPointerDown(UnityEngine.EventSystems.PointerEventData e) => Down?.Invoke(e);
  public void OnDrag(UnityEngine.EventSystems.PointerEventData e) => Drag?.Invoke(e);
  public void OnPointerUp(UnityEngine.EventSystems.PointerEventData e) => Up?.Invoke(e);
}

}
