"""Does the poison actually drown anything, and does it leave anywhere to stand?

The rising poison (docs/33) is three numbers: where the surface starts, where it stops, and
how long it takes. Two of them are the arena's, authored as a box, and every way this feature
fails is a way that box can be wrong -- none of which looks wrong in the scene view, because
a green box over a dark floor looks like a green box over a dark floor either way.

  IT REACHES NOBODY. The pool has a depth span, like every other piece of the arena, and the
      fight happens on one slice of it. A pool modelled in front of the fighters, or behind
      them, fills the screen with poison that bites nothing at all. This is the silent one,
      and the reason this check exists.

  IT STARTS ABOVE THE FLOOR. The surface rises from the box's BOTTOM face. Authored above the
      lowest ground, the first part of the flood happens inside the floor, where nobody can
      see it -- and the pit, which is the first place that should drown, is the last.

  IT DROWNS EVERYTHING. Set the top above every standable surface and the last twenty seconds
      are two fighters dying side by side: the round is then decided by who had more health,
      which is the one ending docs/01 asks the closing phase to prevent.

  IT DROWNS NOTHING. Set the top below every floor and the last minute does nothing at all,
      which is what it did before any of this existed.

  YOU CAN STAND ON IT. The box somebody drags in to make a pool is a Unity cube, and a Unity
      cube arrives with a BoxCollider. Baked, that is a solid block filling the bottom of the
      arena: the fighters stand on the poison, dry, and it looks like the feature WORKING.

So each rule is asked of each arena's own baked floors, and the answers are metres rather
than opinions.

    python3 poison_check.py [--scene "Arena_Mine.unity"]
"""
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import arena_demo_geometry as geometry           # noqa: E402
from sim_motor_mirror import F                   # noqa: E402

ASSETS = os.path.join(HERE, "..", "..", "Gulag runners", "Assets")
SCENES = os.path.join(ASSETS, "Scenes")
POOL_CS = os.path.join(ASSETS, "_Game", "Presentation", "PoisonPool.cs")

# A floor worth FIGHTING on, as depth_check measures one: anything narrower is a crate.
# This is the bar for what the flood takes away -- drowning a crate is not drowning a storey.
FIGHT_WIDTH = 2.0

# And a bar for somewhere to STAND, which is a different question and a much lower one. The
# body is 0.30 m wide, so two body widths is a ledge two people can be on; the hanging cages
# of Arena_Forest are 0.68 m across and are deliberately the last ground left. A check that
# demanded a whole storey above the water would call the parkour a mistake.
STAND_WIDTH = 0.6

# How much of the arena's width the pool has to cover. Less than this and there is dry
# ground beside the flood, which is a corner to wait the last minute out in.
COVERS = 0.9

# How close to the lowest floor the surface may start. A hand's breadth of slack for a pool
# whose bottom was dropped a little into the ground to hide the seam.
STARTS_WITHIN = 0.25

# And how much air a surviving floor needs above the top face before it counts as somewhere
# to stand rather than somewhere to drown slowly.
DRY = 0.10


def read(path):
    with open(path, encoding="utf-8", errors="replace") as f:
        return f.read()


def guid_of(path):
    return re.search(r"guid: ([0-9a-f]{32})", read(path + ".meta")).group(1)


def documents(text):
    return {int(m.group(2)): (int(m.group(1)), m.group(4)) for m in
            re.finditer(r"(?m)^--- !u!(\d+) &(-?\d+)( stripped)?\n(.*?)(?=^--- !u!|\Z)",
                        text, re.S)}


def vec(body, field, default=(0.0, 0.0, 0.0)):
    m = re.search(field + r": \{x: (-?[\d.eE+-]+), y: (-?[\d.eE+-]+), z: (-?[\d.eE+-]+)\}",
                  body)
    return tuple(float(v) for v in m.groups()) if m else default


def placed(docs, transform):
    """World position and scale of a transform, by walking up its fathers.

    Positions and scales only. A rotated pool is reported rather than resolved: a level is a
    horizontal line by definition, and a tilted one is a mistake to be told about.
    """
    pos, scale, tilted = [0.0, 0.0, 0.0], [1.0, 1.0, 1.0], False
    cur = transform
    while cur in docs:
        body = docs[cur][1]
        rot = re.search(r"m_LocalRotation: \{x: (-?[\d.eE+-]+), y: (-?[\d.eE+-]+), "
                        r"z: (-?[\d.eE+-]+), w: (-?[\d.eE+-]+)\}", body)
        if rot and any(abs(float(v)) > 1e-4 for v in rot.groups()[:3]):
            tilted = True
        p, s = vec(body, "m_LocalPosition"), vec(body, "m_LocalScale", (1.0, 1.0, 1.0))
        for i in range(3):
            pos[i] = pos[i] * s[i] + p[i]
            scale[i] *= s[i]
        up = re.search(r"m_Father: \{fileID: (-?\d+)\}", body)
        cur = int(up.group(1)) if up else 0
    return pos, scale, tilted


def pools(text, docs, pool_guid):
    """Every PoisonPool in the scene, as the box its body draws."""
    out = []
    for fid, (cls, body) in sorted(docs.items()):
        if cls != 114 or pool_guid not in body:
            continue
        if re.search(r"^  m_Enabled: 0", body, re.M):
            continue

        art = re.search(r"\n  body: \{fileID: (-?\d+)\}", body)
        own = re.search(r"m_GameObject: \{fileID: (-?\d+)\}", body)
        owner = int(own.group(1)) if own else 0
        box = int(art.group(1)) if art and int(art.group(1)) != 0 else None
        if box is None:
            # No body wired: the component measures its own object instead.
            box = next((f for f, (c, b) in docs.items()
                        if c == 4 and f"m_GameObject: {{fileID: {owner}}}" in b), None)
        surface = re.search(r"\n  surface: \{fileID: (-?\d+)\}", body)
        out.append(dict(component=fid, owner=owner, body=box,
                        surface=surface and int(surface.group(1)) or 0))
    return out


def colliders_under(docs, transform):
    """Any collider on this transform or its children, by class id."""
    found = []
    stack = [transform]
    while stack:
        t = stack.pop()
        if t not in docs:
            continue
        body = docs[t][1]
        go = re.search(r"m_GameObject: \{fileID: (-?\d+)\}", body)
        if go:
            owner = int(go.group(1))
            comps = re.search(r"  m_Component:\n((?:  - component: \{fileID: -?\d+\}\n)*)",
                              docs.get(owner, (0, ""))[1])
            if comps and comps.group(1):
                for c in re.findall(r"fileID: (-?\d+)", comps.group(1)):
                    cls = docs.get(int(c), (0,))[0]
                    if cls in (23, 33, 54, 64, 65, 135, 136, 114, 4):
                        if cls in (64, 65, 135, 136):
                            found.append(int(c))
        kids = re.search(r"  m_Children:(?: \[\]|\n((?:  - \{fileID: -?\d+\}\n)*))", body)
        if kids and kids.group(1):
            stack += [int(v) for v in re.findall(r"fileID: (-?\d+)", kids.group(1))]
    return found


def floors(baked, min_width):
    """Every surface at least this wide: its top, and the ground it covers."""
    out = []
    for box in baked["solid"]:
        x0, y0, x1, y1 = (F(v) for v in box)
        if x1 - x0 >= min_width:
            out.append((y1, x0, x1))
    return sorted(out)


def main(argv):
    only = None
    if "--scene" in argv:
        only = argv[argv.index("--scene") + 1]

    pool_guid = guid_of(POOL_CS)
    problems, notes = [], []

    wanted = [only] if only else geometry.arena_scenes()
    for scene in wanted:
        path = os.path.join(SCENES, scene)
        if not os.path.exists(path):
            continue
        text = read(path)
        docs = documents(text)

        sc = geometry.Scene(path)
        try:
            baked = geometry.bake(sc)
        except SystemExit:
            notes.append(f"{scene}: no SimWorldBaker, so there is no arena to flood")
            continue

        plane = sc.baker_field("planeZ", float, 0.0)
        found = pools(text, docs, pool_guid)

        if not found:
            problems.append(
                f"{scene}: no PoisonPool, so the closing phase does nothing. The arena does "
                f"not narrow any more (docs/33) -- without a pool the last minute is a clock "
                f"running down.")
            continue
        if len(found) > 1:
            problems.append(
                f"{scene}: {len(found)} PoisonPools and the simulation holds one level. The "
                f"first is the poison; the rest rise with it and bite nobody.")

        p = found[0]
        pos, scale, tilted = placed(docs, p["body"])
        y0, y1 = pos[1] - scale[1] / 2, pos[1] + scale[1] / 2
        x0, x1 = pos[0] - scale[0] / 2, pos[0] + scale[0] / 2
        z0, z1 = pos[2] - scale[2] / 2, pos[2] + scale[2] / 2

        notes.append(f"{scene}: poison y {y0:+.2f} -> {y1:+.2f} ({y1 - y0:.2f} m), "
                     f"x {x0:.1f}..{x1:.1f}, z {z0:.2f}..{z1:.2f}")

        if tilted:
            problems.append(
                f"{scene}: the pool is rotated. A level is a horizontal line, and the "
                f"simulation only reads the box's bottom and top -- so a tilted pool bites "
                f"at a height nothing in the scene is drawn at.")

        if y1 <= y0:
            problems.append(f"{scene}: the pool has no height, so nothing ever floods.")
            continue

        # --- the silent one: does it reach the fighters at all
        if not (z1 > plane and z0 < plane):
            problems.append(
                f"{scene}: the pool spans z {z0:.2f}..{z1:.2f} and the fight is on the plane "
                f"at z {plane:.2f}. Nobody is ever inside it, so a screen full of poison "
                f"bites nothing -- and nothing warns, because the picture is correct.")

        level = floors(baked, FIGHT_WIDTH)
        standable = floors(baked, STAND_WIDTH)
        if not level:
            notes.append(f"  no floor wide enough to measure against")
            continue

        lowest, highest = level[0][0], level[-1][0]

        if y0 > lowest + STARTS_WITHIN:
            problems.append(
                f"{scene}: the surface starts at y {y0:+.2f} and the lowest floor is at "
                f"y {lowest:+.2f}. The first {y0 - lowest:.2f} m of the flood happens inside "
                f"the floor, where nobody can see it, and the lowest ground -- which should "
                f"drown first -- drowns last.")

        # What is taken away is judged on floors; what is LEFT is judged on anything a body
        # can stand on, because the last ground in an arena is allowed to be a ledge.
        drowned = [top for top, _a, _b in level if top < y1 - DRY]
        dry = [(top, b - a) for top, a, b in standable if top >= y1 - DRY]

        if not drowned:
            problems.append(
                f"{scene}: the surface stops at y {y1:+.2f}, below every floor (lowest "
                f"y {lowest:+.2f}), so the poison takes no ground away and the last minute "
                f"is a clock running down.")
        elif not dry:
            problems.append(
                f"{scene}: the surface stops at y {y1:+.2f} and there is nothing at all above "
                f"it -- not a floor, not a ledge, not a crate (the highest is y "
                f"{standable[-1][0]:+.2f}). Both fighters drown together and the round is "
                f"decided on health, which is the ending docs/01 wants the closing phase to "
                f"prevent. Leave the high ground dry.")
        else:
            widest = max(w for _t, w in dry)
            notes.append(f"  drowns {len(drowned)} floor(s), leaves {len(dry)} thing(s) to "
                         f"stand on (highest drowned y {max(drowned):+.2f}; the widest dry "
                         f"ground is {widest:.2f} m at y {min(t for t, w in dry):+.2f})")
            if widest < FIGHT_WIDTH:
                notes.append(f"  the last ground is a LEDGE, not a floor ({widest:.2f} m "
                             f"against {FIGHT_WIDTH:.2f}) -- which is the parkour ending, and "
                             f"worth meaning on purpose")

        # --- does it cover the ground it is meant to
        ground = [(a, b) for top, a, b in level if top < y1]
        if ground:
            want_lo = min(a for a, _b in ground)
            want_hi = max(b for _a, b in ground)
            covered = max(0.0, min(x1, want_hi) - max(x0, want_lo))
            share = covered / (want_hi - want_lo)
            if share < COVERS:
                problems.append(
                    f"{scene}: the pool covers {share * 100:.0f}% of the ground it floods "
                    f"(x {x0:.1f}..{x1:.1f} against x {want_lo:.1f}..{want_hi:.1f}). The rest "
                    f"is dry floor beside the flood: a corner to wait the last minute out in.")

        # --- and the cube's own collider
        solid = colliders_under(docs, p["body"]) + colliders_under(docs, p["owner"])
        if solid:
            problems.append(
                f"{scene}: the pool carries {len(solid)} collider(s). The baker skips them, "
                f"but anything else in Unity will not: delete them. A cube dragged into a "
                f"scene brings a BoxCollider with it, and a solid pool is one the fighters "
                f"STAND on -- which looks exactly like the poison working.")

        if not p["surface"]:
            notes.append(f"  no surface slab wired, so the waterline is the top of a block "
                         f"of green -- which is the hardest part of it to judge")

    for n in notes:
        print(("  " + n) if n.startswith(" ") else ("  note: " + n))
    for p in problems:
        print("  " + p)
    print(f"poison_check: {len(problems)} problem(s) across {len(wanted)} scene(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
