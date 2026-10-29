"""Draws the arena side-on with the ladders in it, so you can see them before Unity does.

"The ladders did not turn up" is not a complaint a number answers. ladder_check proves
the file is honest and the placement arithmetic works out; this draws the result, which
is the only form the answer is actually useful in: four ladders, each standing on the
floor it leaves and reaching the one above it, at the scale of a fighter.

It is a side elevation -- X across, Y up -- because that is the plane the fight happens
on and the plane the simulation collides in. The grey boxes are what the bake produces
from Arena_Demo, not what the scene looks like: scenery marked Ignore is not drawn,
because a ladder that lines up with the art and misses the collision is the bug this is
looking for. The red line is a fighter's height, to the same scale.

    python3 ladder_preview.py [out.png] [--width 1600]

Needs Pillow, which is a development dependency of this folder and not of the game.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import arena_demo_geometry as geometry           # noqa: E402
import fbx_reader                                # noqa: E402
import ladder_check                              # noqa: E402
from sim_motor_mirror import F                   # noqa: E402

ASSETS = os.path.join(HERE, "..", "..", "Gulag runners", "Assets")

SKY = (28, 32, 38)
FLOOR = (96, 99, 104)
FLOOR_TOP = (132, 136, 142)
ONEWAY = (150, 128, 72)
RAIL = (0.282, 0.189, 0.039)      # the lighter of the two woods in the .blend
STILE = (0.044, 0.038, 0.003)
FIGHTER = 1.70                    # docs/04: a fighter is this tall


def shade(colour, lambert):
    """A flat wood colour with one light on it, gamma-corrected on the way out."""
    lit = [c * (0.22 + 0.78 * lambert) for c in colour]
    return tuple(int(255 * min(1.0, v) ** (1.0 / 2.2)) for v in lit)


def main(argv):
    out, width = "arena_ladders.png", 1600
    rest, i = [], 0
    while i < len(argv):
        if argv[i] == "--width":
            width = int(argv[i + 1]); i += 2
        else:
            rest.append(argv[i]); i += 1
    if rest:
        out = rest[0]

    from PIL import Image, ImageDraw

    scene = geometry.Scene()
    world = geometry.bake(scene)
    solids = [tuple(F(v) for v in a) for a in world["solid"]]
    oneway = [tuple(F(v) for v in a) for a in world["oneway"]]
    placed = ladder_check.placements(scene)

    everything = solids + oneway
    x0 = min(b[0] for b in everything) - 1.0
    x1 = max(b[2] for b in everything) + 1.0
    y0 = min(b[1] for b in everything) - 1.0
    y1 = max(b[3] for b in everything) + 1.0

    scale = width / (x1 - x0)
    height = int((y1 - y0) * scale)
    img = Image.new("RGB", (width, height), SKY)
    draw = ImageDraw.Draw(img)

    def to_px(x, y):
        return ((x - x0) * scale, (y1 - y) * scale)

    for box, body, cap in ((solids, FLOOR, FLOOR_TOP), (oneway, ONEWAY, ONEWAY)):
        for bx0, by0, bx1, by1 in box:
            draw.rectangle([to_px(bx0, by1), to_px(bx1, by0)], fill=body)
            draw.line([to_px(bx0, by1), to_px(bx1, by1)], fill=cap, width=2)

    # One light, from the front and above-left, the way the arena's key light sits.
    light = (-0.45, 0.78, -0.44)
    drawn = 0
    for place in placed:
        model = place["model"]
        path = os.path.join(ASSETS, "St", model + ".fbx")
        if not os.path.exists(path):
            continue

        faces = []
        for verts, tris in fbx_reader.geometry(path):
            for tri in tris:
                # Same transform ladder_check uses, including the end-for-end turn the
                # setup tool settles by measurement -- so this draws what will be built.
                world_tri = [ladder_check.world_point(place, scene, verts[k], True)
                             for k in tri]
                ux = [world_tri[1][k] - world_tri[0][k] for k in range(3)]
                vx = [world_tri[2][k] - world_tri[0][k] for k in range(3)]
                n = (ux[1] * vx[2] - ux[2] * vx[1],
                     ux[2] * vx[0] - ux[0] * vx[2],
                     ux[0] * vx[1] - ux[1] * vx[0])
                length = sum(c * c for c in n) ** 0.5 or 1.0
                n = [c / length for c in n]
                lambert = abs(sum(n[k] * light[k] for k in range(3)))
                depth = sum(c[2] for c in world_tri) / 3.0
                faces.append((depth, world_tri, lambert))

        # Painter's order: the far side of the ladder first.
        faces.sort(key=lambda f: f[0])
        for depth, tri, lambert in faces:
            colour = RAIL if lambert > 0.55 else STILE
            draw.polygon([to_px(c[0], c[1]) for c in tri], fill=shade(colour, lambert))
        drawn += 1

        box = ladder_check.world_box(place, _model_box(path), scene, True)
        label = to_px(box[0][0], box[1][1])
        draw.text((label[0], label[1] - 16), place["name"], fill=(222, 226, 232))

    # A fighter, for scale, standing on the lowest floor.
    ground = min(b[3] for b in solids)
    foot = to_px(x0 + 1.4, ground)
    head = to_px(x0 + 1.4, ground + FIGHTER)
    draw.line([foot, head], fill=(208, 72, 64), width=3)
    draw.text((head[0] + 6, head[1] - 6), f"{FIGHTER:.2f} m", fill=(208, 72, 64))

    img.save(out)
    print(f"ladder_preview: {drawn} ladder(s) over {len(solids)} solid box(es), "
          f"{width}x{height} -> {out}")
    return 0


def _model_box(path):
    lo = [1e9, 1e9, 1e9]
    hi = [-1e9, -1e9, -1e9]
    for verts, _tris in fbx_reader.geometry(path):
        for v in verts:
            for k in range(3):
                lo[k] = min(lo[k], v[k])
                hi[k] = max(hi[k], v[k])
    return lo, hi


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
