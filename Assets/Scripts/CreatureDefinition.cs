using UnityEngine;

namespace PoRace {

/// <summary>
/// A racer body as data (PRD FR-01, tasks E.2). Assets live in Assets/Resources/Creatures so CreatureRegistry can
/// discover them at runtime. Only raceable entries may be put on a grid: a creature becomes raceable once its
/// locomotion policy has passed the graduation and Unity parity gates. Others are roster stubs (art + rig only).
/// </summary>
[CreateAssetMenu(menuName = "PoRace/Creature Definition", fileName = "Creature")]
public class CreatureDefinition : ScriptableObject {
  public enum Body { Quadruped, Biped }

  public string displayName = "Racer";
  public Body body = Body.Quadruped;
  [Tooltip("Standing height (m).")] public float height = 0.4f;
  [Tooltip("True once the creature's policies have graduated and passed the Unity parity gate.")]
  public bool raceable = false;
  [Tooltip("What stands between this creature and the grid (shown while raceable is false).")]
  [TextArea] public string blockedBy = "";
  [Tooltip("Skinned visual (Humanoid avatar for bipeds). Null when the visual comes from the MuJoCo model itself.")]
  public GameObject visualPrefab;
  public Color color = Color.white;
  [Tooltip("Height (m) broadcast cameras aim at above the racer's base: a 1.89 m human needs a different frame than a 0.4 m dog.")]
  public float cameraHeight = 0.3f;
}

/// <summary>Runtime discovery of every creature asset under Resources/Creatures (PRD FR-03).</summary>
public static class CreatureRegistry {
  public static CreatureDefinition[] All() {
    var all = Resources.LoadAll<CreatureDefinition>("Creatures");
    System.Array.Sort(all, (a, b) => string.CompareOrdinal(a.name, b.name));
    return all;
  }

  public static CreatureDefinition[] Raceable() => System.Array.FindAll(All(), c => c.raceable);
}

}
