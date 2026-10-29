"""Can the ladders be built, and will they land where the arena needs them?

Four objects in Arena_Demo come from St.blend and St2.blend -- the two routes off the
ground floor and the two up the end platforms. Unity cannot read a .blend without Blender
installed, so all four are "Missing Prefab" and the arena has no way between its storeys:
the bake reads zero ladders, and arena_demo_test's "both ladders survived the bake" is
red. docs/refs/blend_to_fbx.py exports them, LadderSetup.cs puts them back.

This checks the part that has to be right BEFORE anybody opens Unity, because the one
thing that cannot be tested from here is Unity itself.

  THE FILE IS HONEST. UnitScaleFactor 100 and no turn on the model's node. Both are how
  the dungeon pack went wrong -- a file claiming centimetres with vertices in metres, and
  a compensating scale on a node the importer drops -- and both would break Vlad's
  placement, which was dialled in against the .blend import and has to keep meaning what
  it meant.

  THE PLACEMENT STILL WORKS. Each ladder's world box is worked out from the scene's own
  transform and the FBX's own vertices, and has to come out ladder-shaped: upright, a
  storey tall, half a metre wide, flat.

  THE TOOL'S ONE GUESS IS NOT A GUESS. Both models hang DOWN from their origin, and an
  FBX is a right-handed file read by a left-handed engine, so which end is the foot
  depends on the importer. LadderSetup settles it by measuring: it turns the model end
  for end and keeps whichever foot is nearer a floor. That rule is only safe if the two
  answers are far apart, so the gap between them is asserted here -- measured, it is
  2 cm against 157 cm.

Run it from docs/refs:  python3 ladder_check.py
"""
import math
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import arena_demo_geometry as geometry           # noqa: E402
import fbx_reader                                # noqa: E402
from sim_motor_mirror import F                   # noqa: E402

ASSETS = os.path.join(HERE, "..", "..", "Gulag runners", "Assets")
LADDERS = ("St", "St2")

# What a ladder in this arena has to be. Wide enough to climb, flat enough to stand
# against a wall, tall enough to be worth climbing and short enough to be a ladder.
MIN_HEIGHT, MAX_HEIGHT = 1.5, 4.0
MIN_WIDTH, MAX_WIDTH = 0.30, 1.00
MAX_DEPTH = 0.40

# How near a floor a foot has to be to count as standing on it, and how far the WRONG
# way up has to be for the tool's measurement to be deciding something rather than
# splitting hairs.
ON_THE_FLOOR = 0.25
DECISIVE = 1.0


def models():
    """Every model file in the project, by name, with the format it is in."""
    found = {}
    for base, dirs, files in os.walk(ASSETS):
        dirs[:] = [d for d in dirs if not d.startswith(".") and d not in ("Library", "Temp")]
        for f in files:
            stem, ext = os.path.splitext(f)
            if ext.lower() in (".fbx", ".blend", ".obj", ".dae"):
                found.setdefault(stem, {})[ext.lower()] = os.path.join(base, f)
    return found


def placements(scene):
    """Each ladder instance in the scene: name, source model, and its world transform."""
    text = open(geometry.SCENE, encoding="utf-8", errors="ignore").read()
    guids = {}
    for name in LADDERS:
        meta = os.path.join(ASSETS, "St", name + ".blend.meta")
        if os.path.exists(meta):
            m = re.search(r"guid: ([0-9a-f]{32})", open(meta, encoding="utf-8").read())
            if m:
                guids[m.group(1)] = name
        fbx = os.path.join(ASSETS, "St", name + ".fbx.meta")
        if os.path.exists(fbx):
            m = re.search(r"guid: ([0-9a-f]{32})", open(fbx, encoding="utf-8").read())
            if m:
                guids[m.group(1)] = name

    out = []
    for block in re.finditer(r"^--- !u!1001 &\d+\n(.*?)(?=\n--- !u!|\Z)", text, re.M | re.S):
        body = block.group(1)
        guid = re.search(r"m_SourcePrefab: \{fileID: \d+, guid: ([0-9a-f]{32})", body)
        if not guid or guid.group(1) not in guids:
            continue

        def value(path, default=None):
            m = re.search(r"propertyPath: " + re.escape(path) +
                          r"\n\s+value: ([-\d.eE]+)", body)
            return float(m.group(1)) if m else default

        label = re.search(r"propertyPath: m_Name\n\s+value: ([^\n]*)", body)
        parent = re.search(r"m_TransformParent: \{fileID: (-?\d+)\}", body)
        out.append(dict(
            name=label.group(1).strip() if label else guids[guid.group(1)],
            model=guids[guid.group(1)],
            source=guid.group(1),
            parent=int(parent.group(1)) if parent else 0,
            pos=[value("m_LocalPosition.x", 0.0), value("m_LocalPosition.y", 0.0),
                 value("m_LocalPosition.z", 0.0)],
            rot=(value("m_LocalRotation.x", 0.0), value("m_LocalRotation.y", 0.0),
                 value("m_LocalRotation.z", 0.0), value("m_LocalRotation.w", 1.0)),
            scale=[value("m_LocalScale.x", 1.0), value("m_LocalScale.y", 1.0),
                   value("m_LocalScale.z", 1.0)],
            marked="GulagRunners.Game.SimCollider" in body or
                   "m_AddedComponents" in body and "insertIndex" in body))
    out.sort(key=lambda p: p["pos"][0])
    return out


def world_point(place, scene, point, flip):
    """One of the model's own vertices, where the scene puts it.

    `flip` turns the model end for end about its thinnest axis -- 180 degrees about Z,
    so x and y change sign. That is the one thing LadderSetup decides by measuring
    rather than by reading the file, and this is the same turn it makes.
    """
    local = [-point[0], -point[1], point[2]] if flip else list(point)
    local = [local[k] * place["scale"][k] for k in range(3)]

    origin, basis = ([0.0, 0.0, 0.0], [[1, 0, 0], [0, 1, 0], [0, 0, 1]])
    if place["parent"] in scene.transforms:
        origin, basis = scene.basis(place["parent"])

    turned = geometry._apply(geometry._qmat(place["rot"]), local)
    moved = [turned[k] + place["pos"][k] for k in range(3)]
    here = geometry._apply(basis, moved)
    return [here[k] + origin[k] for k in range(3)]


def world_box(place, box, scene, flip):
    """The ladder's world box: the scene's transform applied to the model's corners."""
    lo, hi = box
    corners = [world_point(place, scene,
                           [(hi if (c >> k) & 1 else lo)[k] for k in range(3)], flip)
               for c in range(8)]
    return ([min(c[k] for c in corners) for k in range(3)],
            [max(c[k] for c in corners) for k in range(3)])


def floors(scene):
    """Every solid top the bake produces, as (x0, x1, top)."""
    world = geometry.bake(scene)
    out = []
    for a in world["solid"] + world["oneway"]:
        x0, y0, x1, y1 = (F(v) for v in a)
        out.append((x0, x1, y1))
    return out


def gap_to_floor(box, tops):
    """How far the bottom of this box is from the nearest floor top beneath its width."""
    lo, hi = box
    near = [t for t in tops if not (t[1] < lo[0] - 0.5 or t[0] > hi[0] + 0.5)]
    if not near:
        return None, None
    best = min(near, key=lambda t: abs(t[2] - lo[1]))
    return abs(best[2] - lo[1]), best[2]


def main():
    problems, notes = [], []
    index = models()
    scene = geometry.Scene()
    tops = floors(scene)
    placed = placements(scene)

    if not placed:
        print("  ladder_check: Arena_Demo has no object from St.blend or St2.blend. "
              "That is not a pass -- the ladders are gone.")
        return 1

    notes.append(f"{len(placed)} ladder(s) in Arena_Demo: " +
                 ", ".join(p["name"] for p in placed))

    # 1. A format Unity reads by itself.
    shapes = {}
    for name in sorted({p["model"] for p in placed}):
        have = index.get(name, {})
        if ".fbx" not in have:
            problems.append(
                f"{name} exists only as {' and '.join(sorted(have)) or 'nothing'}. Unity "
                f"cannot read a .blend without Blender installed, so every instance of it "
                f"is a hole in the level. Run docs/refs/blend_to_fbx.py over "
                f"Assets/St/{name}.blend.")
            continue

        facts = fbx_reader.describe(have[".fbx"])

        # 2. The file is honest about its units.
        if facts["unit"] != 100.0:
            problems.append(
                f"{name}.fbx declares UnitScaleFactor {facts['unit']}, not 100. A file "
                f"that calls metres centimetres imports at a hundredth of its size -- "
                f"this is the fault that made the dungeon pack four millimetres across.")

        # 3. And carries no turn for an importer to drop.
        for turn in facts["turns"]:
            if max(abs(v) for v in turn) > 0.001:
                problems.append(
                    f"{name}.fbx has {turn} on a model node. Some importers collapse a "
                    f"single root node and lose that turn, which would land the ladder a "
                    f"quarter turn out of true; the exporter is meant to cancel it.")

        if not facts["meshes"]:
            problems.append(f"{name}.fbx has no mesh in it at all.")
            continue

        lo = [min(m[0][k] for m in facts["meshes"]) for k in range(3)]
        hi = [max(m[1][k] for m in facts["meshes"]) for k in range(3)]
        shapes[name] = (lo, hi)
        size = [hi[k] - lo[k] for k in range(3)]

        # 4. Axes in the order the placement assumes.
        if not (size[0] >= size[1] >= size[2]):
            problems.append(
                f"{name}.fbx is {size[0]:.1f} x {size[1]:.1f} x {size[2]:.1f} as modelled, "
                f"which is not longest-first. The scene's scale was dialled in against "
                f"that order, so LadderSetup has to turn it and the turn is one more "
                f"thing to get wrong.")
        notes.append(f"{name}.fbx: {size[0]:.1f} x {size[1]:.1f} x {size[2]:.1f} as "
                     f"modelled, metres declared, no turn on the node")

    # 5 and 6. Where each one actually lands, and what shape it is when it gets there.
    for place in placed:
        shape = shapes.get(place["model"])
        if shape is None:
            continue

        right = world_box(place, shape, scene, flip=True)
        wrong = world_box(place, shape, scene, flip=False)
        near, top = gap_to_floor(right, tops)
        far, _ = gap_to_floor(wrong, tops)
        size = [right[1][k] - right[0][k] for k in range(3)]

        if near is None:
            problems.append(f"{place['name']} has no floor anywhere under it.")
            continue

        if near > ON_THE_FLOOR:
            problems.append(
                f"{place['name']} would stand with its foot {near:.2f} m from the nearest "
                f"floor. A ladder you have to jump to is not a route between storeys.")
        if far is not None and far - near < DECISIVE:
            problems.append(
                f"{place['name']}: the two ways up are {near:.2f} m and {far:.2f} m from "
                f"a floor. LadderSetup picks between them by measuring, and that only "
                f"works while one is obviously right.")

        if not (MIN_HEIGHT <= size[1] <= MAX_HEIGHT):
            problems.append(f"{place['name']} would be {size[1]:.2f} m tall, which is not "
                            f"a ladder in a {MIN_HEIGHT}-{MAX_HEIGHT} m arena.")
        if not (MIN_WIDTH <= size[0] <= MAX_WIDTH):
            problems.append(f"{place['name']} would be {size[0]:.2f} m wide; a ladder is "
                            f"{MIN_WIDTH}-{MAX_WIDTH} m across.")
        if size[2] > MAX_DEPTH:
            problems.append(f"{place['name']} would be {size[2]:.2f} m deep, which is a "
                            f"wall rather than a ladder flat against one.")
        if size[1] <= size[0] or size[1] <= size[2]:
            problems.append(f"{place['name']} would not be standing up: "
                            f"{size[0]:.2f} x {size[1]:.2f} x {size[2]:.2f} m.")

        head = right[1][1] - top
        reach = "nothing marked solid up there yet" if not [
            t for t in tops if abs(t[2] - right[1][1]) < 0.5
            and not (t[1] < right[0][0] - 0.5 or t[0] > right[1][0] + 0.5)] else "a floor"
        notes.append(
            f"{place['name']}: {size[0]:.2f} x {size[1]:.2f} x {size[2]:.2f} m, foot "
            f"{near * 100:.0f} cm off the floor at y {top:.2f}, head {head:.2f} m above "
            f"it -- {reach} at the top; the other way up is {far:.2f} m out")

        if not place["marked"]:
            notes.append(f"{place['name']} carries no SimCollider of its own, so "
                         f"LadderSetup gives it one (Kind = Ladder) -- its mirror twin "
                         f"has one and this does not")

    # 7. And whether the scene has been rebuilt yet.
    still_blend = sum(1 for p in placed
                      if os.path.exists(os.path.join(ASSETS, "St", p["model"] + ".blend.meta"))
                      and p["source"] == re.search(
                          r"guid: ([0-9a-f]{32})",
                          open(os.path.join(ASSETS, "St", p["model"] + ".blend.meta"),
                               encoding="utf-8").read()).group(1))
    baked = len(geometry.bake(scene)["ladder"])
    if still_blend:
        notes.append(
            f"{still_blend} of {len(placed)} still point at the .blend, so the bake has "
            f"{baked} ladder(s) in it. Run Tools > Gulag Runners > Setup > Build the "
            f"arena ladders in Unity and this becomes {len(placed)}.")
    elif baked < len(placed):
        problems.append(
            f"the scene points at the FBX but the bake still has only {baked} ladder(s) "
            f"of {len(placed)}. The marker or its box did not survive the swap.")
    else:
        notes.append(f"the bake has all {baked} ladder(s).")

    for n in notes:
        print("  note: " + n)
    for p in problems:
        print("  " + p)
    print(f"ladder_check: {len(problems)} problem(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
