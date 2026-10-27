"""Steeplechase training scene (E.3, Map 04): scene_porace.xml plus a 28 x 28 m field of static obstacles.

  python make_steeple.py            # writes assets/go2/scene_steeple.xml

Obstacles are plain world box geoms (the Unity plugin's heightfield needs a Terrain and GPU readback, which breaks
-nographics; boxes are identical on both sides). Contact settings match the floor: contype 1 / conaffinity 0,
priority 1, friction 0.6, condim 3. Foot contact sensors use body2="world" so stepping on an obstacle counts as ground.
Height bands are the contract for Map 04 (Assets/Editor/SteepleBuilder.cs uses the same numbers):
  hurdle  bar across the path, 0.08-0.15 m deep, height 0.03-0.08 m
  step    plateau 0.8-2.5 m square, height 0.03-0.08 m (a step up and a step down)
  rubble  0.1-0.3 m blocks, height 0.02-0.05 m, random yaw
"""
import math
from pathlib import Path

import numpy as np

from go2 import constants as C

OUT = C.ASSET_PATH / "scene_steeple.xml"
FIELD = 14.0
BANDS = {"hurdle": (0.03, 0.08), "step": (0.03, 0.08), "rubble": (0.02, 0.05)}


def obstacles(seed=0):
  """Heights grow with distance from the centre (a curriculum in space: spawns are uniform over the field, so every
  batch mixes 1-2 cm rubble near the middle with full-height 8 cm hurdles at the edge)."""
  rng = np.random.default_rng(seed); out = []
  def add(kind, x, y, yaw, sx, sy):
    lo, hi = BANDS[kind]; k = min(1.0, 0.25 + math.hypot(x, y) / FIELD)
    out.append((kind, x, y, yaw, sx / 2, sy / 2, rng.uniform(lo * k, hi * k) / 2))
  for _ in range(40):
    add("hurdle", *rng.uniform(-FIELD, FIELD, 2), rng.uniform(0, math.pi), rng.uniform(0.08, 0.15), rng.uniform(1.5, 3.0))
  for _ in range(30):
    add("step", *rng.uniform(-FIELD, FIELD, 2), rng.uniform(0, math.pi), rng.uniform(0.8, 2.5), rng.uniform(0.8, 2.5))
  for _ in range(70):
    s = rng.uniform(0.1, 0.3)
    add("rubble", *rng.uniform(-FIELD, FIELD, 2), rng.uniform(0, math.pi), s, s * rng.uniform(0.6, 1.4))
  return out


def main():
  src = (C.ASSET_PATH / "scene_porace.xml").read_text()
  geoms = []
  for i, (kind, x, y, yaw, hx, hy, hz) in enumerate(obstacles()):
    geoms.append(f'    <geom name="obs{i}_{kind}" type="box" pos="{x:.4f} {y:.4f} {hz:.4f}" euler="0 0 {yaw:.4f}" '
                 f'size="{hx:.4f} {hy:.4f} {hz:.4f}" material="obstacle" priority="1" friction="0.6" condim="3" contype="1" conaffinity="0"/>')
  xml = src.replace('<mujoco model="go2 porace scene">', '<mujoco model="go2 porace steeple scene">')
  xml = xml.replace('    <material name="cube" rgba="0.9 0.3 0.1 1"/>', '    <material name="cube" rgba="0.9 0.3 0.1 1"/>\n    <material name="obstacle" rgba="0.2 0.5 0.8 1"/>')
  xml = xml.replace('      contype="1" conaffinity="0"/>\n', '      contype="1" conaffinity="0"/>\n' + "\n".join(geoms) + "\n", 1)
  xml = xml.replace('geom2="floor"', 'body2="world"')
  assert xml.count("obs") >= 140 and 'geom2="floor"' not in xml
  OUT.write_text(xml)
  print(f"wrote {OUT} with {len(geoms)} obstacles")


if __name__ == "__main__":
  main()
