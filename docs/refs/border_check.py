"""Can a fighter get out of the arena?

Every location in this game is a box with the ends closed, and the ends are the only
thing between a player and an infinite fall. They are also the easiest thing to get
wrong, because nothing in the scene view says whether a wall is in the simulation: a
border that is culled, or modelled with no thickness, or marked as scenery, looks
exactly like one that works right up until somebody walks through it.

Two of those have already happened here. The Nordic arena's top-storey borders were
being thrown away as backdrop, because dropBackdrop measures from planeZ and planeZ was
set for a different storey -- four walls out of six, with no warning anywhere. And every
border in both arenas is a Unity Plane, which is FLAT: its box comes out of the bake
with MinX equal to MaxX to the millimetre, so it is a wall of zero width that happens to
stop a body because the sweep test is generous, rather than because there is anything
there.

So this runs a fighter at both ends of every storey -- running, running and jumping,
running and dodging -- and asserts he is still in the arena afterwards. And it says
which walls have no thickness, because that is the one that will fail later rather than
now.

    python3 border_check.py
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import arena_demo_geometry as geometry                       # noqa: E402
from sim_motor_mirror import S, step, X, F, C, L, R, JMP, DOD  # noqa: E402

SCENES = os.path.join(HERE, "..", "..", "Gulag runners", "Assets", "Scenes")

# A floor narrower than this is a crate, not a storey.
MIN_WIDTH = 2.0

# Below this a wall has no thickness at all once the bake rounds it to millimetres.
# It can still stop a body -- it does today -- but only because the sweep test is
# generous about touching, which is not the same as there being something there.
THIN = 0.02

# How far below a storey counts as having left it for good.
GONE = 3.0

# Long enough to cross any arena in this project twice at a run.
RUN_TICKS = 1500


def world(path):
    scene = geometry.Scene(path)
    baked = geometry.bake(scene)
    spawns = geometry.spawns(scene)
    if spawns:
        C["HOME"] = X(spawns[0][3])
    out = {k: baked[k] for k in ("solid", "oneway", "ladder", "solidz", "onewayz",
                                 "ladderz", "ladderlean", "ladderleanz")}
    out["solidgate"] = baked["solidgate"]
    out["gate"] = baked["gate"]
    out["gatesshut"] = 0
    return out, baked


def storeys(baked):
    """Floors worth standing on, grouped by level, with the slice each offers."""
    halfz = F(C["BODYZ"]) / 2
    levels = {}
    for box, depth in zip(baked["solid"], baked["solidz"]):
        x0, _y0, x1, y1 = (F(v) for v in box)
        if x1 - x0 < MIN_WIDTH:
            continue
        zmin, zmax = F(depth[0]), F(depth[1])
        inset = min(halfz, (zmax - zmin) / 2)
        here = max(zmin + inset, min(F(C["HOME"]), zmax - inset))
        at = levels.setdefault(round(y1, 1), dict(top=y1, x0=x0, x1=x1, z=here))
        at["x0"] = min(at["x0"], x0)
        at["x1"] = max(at["x1"], x1)
    return sorted(levels.values(), key=lambda f: -f["top"])


def walls(baked):
    """The thin boxes, which is what a border is. (x, lowest, highest, thickness)."""
    out = []
    for box in baked["solid"]:
        x0, y0, x1, y1 = (F(v) for v in box)
        if x1 - x0 >= MIN_WIDTH:
            continue
        out.append((x0, y0, y1, x1 - x0))
    return out


def escapes(w, storey, toward, extra, label):
    """Run a fighter at one end and say where he ended up."""
    s = S((storey["x0"] + storey["x1"]) / 2, storey["top"] + 0.35)
    s.depth = X(storey["z"])
    for _ in range(90):
        step(s, 0, w)
    if abs(F(s.y) - storey["top"]) > 0.6:
        return None                       # could not stand there; not this test's business

    for t in range(RUN_TICKS):
        step(s, toward | (extra if extra and t % 24 == 0 else 0), w)
    return F(s.x), F(s.y), F(s.y) < storey["top"] - GONE


def main(argv):
    wanted = argv or geometry.arena_scenes()
    problems, notes = [], []

    for name in wanted:
        path = os.path.join(SCENES, name)
        if not os.path.exists(path):
            print(f"  no scene called {name}")
            continue
        try:
            w, baked = world(path)
        except SystemExit as why:
            notes.append(f"{name}: not an arena yet ({why})")
            continue

        floors = storeys(baked)
        sides = walls(baked)
        notes.append(f"{name}: {len(floors)} storey(s), {len(sides)} border wall(s)")

        thin = [s for s in sides if s[3] < THIN]
        if thin:
            problems.append(
                f"{name}: {len(thin)} of its {len(sides)} borders have no thickness at "
                f"all -- " + ", ".join(f"x {s[0]:.1f}" for s in thin[:6]) +
                ". They are Unity Planes, and a Plane's box rounds to zero width in the "
                "bake. One stops a body today because the sweep test counts touching as "
                "overlapping; it is not something to rely on at speed or under "
                "knockback. Tools > Gulag Runners > Setup > Close the arena borders "
                "gives them real thickness and an explicit marker.")

        for storey in floors:
            for side, toward in (("left", L), ("right", R)):
                for label, extra in (("running", 0), ("jumping", JMP), ("dodging", DOD)):
                    got = escapes(w, storey, toward, extra, label)
                    if got is None:
                        continue
                    x, y, out = got
                    if out:
                        problems.append(
                            f"{name}: a fighter {label} {side} off the storey at "
                            f"y {storey['top']:+.2f} leaves the arena -- ends at "
                            f"x {x:.2f}, y {y:.1f} and still falling. That end has no "
                            f"wall the simulation can see.")
                    else:
                        notes.append(f"  y {storey['top']:+.2f} {side:5s} {label:8s} "
                                     f"held at x {x:.2f}")

    for n in notes:
        print("  note: " + n if not n.startswith("  ") else "  " + n)
    for p in problems:
        print("  " + p)
    print(f"border_check: {len(problems)} problem(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
