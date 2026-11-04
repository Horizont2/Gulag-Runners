"""Does coming down a storey bring the fighter towards the camera?

The locations in this game are built on what Vlad calls the staircase principle: every
storey sits at its own depth, the ground floor nearest the camera and each one above it
further back. Climbing is a move away from you and dropping is a move towards you, worked
out from the level rather than from a button, because docs/06 has no input bit to spare
for one.

Two things have to be true for that to read, and neither is obvious from looking at the
scene:

  ONE SLICE PER STOREY. Two fighters who arrive on the same floor by different routes --
  one up the ladder, one dropping onto it -- have to end up on the same slice of it, or
  they swing through each other. The lane rule decides this and it is easy to get wrong:
  it used to keep whatever slice you arrived with on any floor that did not happen to
  reach the gameplay plane.

  DOWN IS FORWARD. Dropping to the storey below must move the body towards the camera,
  and it must do it DURING the fall. Doing it on landing is the same number and the wrong
  feeling -- the fighter lands looking wrong and then slides, which is what "he drops down
  and stays in the background" was describing.

Both are measured here by running the real motor over the real scene, for every pair of
storeys the level actually has.

    python3 depth_check.py [--scene "Arena_Nordic fortress.unity"] [--hatches]

--hatches bakes with ladderCutsHatch on, which is how you find out whether a scene is
ready to have it switched on: without a hatch there is no way to drop between storeys at
all, and this check has nothing to measure.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import arena_demo_geometry as geometry           # noqa: E402
import sim_motor_mirror as m                     # noqa: E402
from sim_motor_mirror import S, step, X, F, C    # noqa: E402

SCENES = os.path.join(HERE, "..", "..", "Gulag runners", "Assets", "Scenes")

# A storey is a floor wide enough to fight on. Anything narrower is a crate.
MIN_WIDTH = 2.0

# How far towards the camera a drop has to bring you before it reads as a move at all.
# Less than this and the two storeys are the same slice as far as an eye is concerned.
READS_AS_A_MOVE = 0.15

# And how much of that move has to have happened before the feet land. The whole point
# is that the body leans into the drop rather than sliding once it is standing.
DURING_THE_FALL = 0.6


class NotAnArena(Exception):
    """This scene has no simulation in it yet, so there are no storeys to measure."""


def world(path, hatches):
    text = open(path, encoding="utf-8", errors="ignore").read()
    if hatches:
        text = text.replace("ladderCutsHatch: 0", "ladderCutsHatch: 1")
        path = os.path.join(HERE, "_depth_check_tmp.unity")
        open(path, "w", encoding="utf-8").write(text)
    try:
        scene = geometry.Scene(path)
        try:
            baked = geometry.bake(scene)
        except SystemExit as why:
            # A scene with no SimWorldBaker in it is art, not an arena. Saying which,
            # rather than dying with the baker's own message about a different scene,
            # is the difference between a check and a stack trace.
            raise NotAnArena(str(why)) from None
    finally:
        if hatches and os.path.exists(path):
            os.remove(path)

    spawns = geometry.spawns(scene) if _takes_scene() else geometry.spawns()
    if spawns:
        C["HOME"] = X(spawns[0][3])

    out = {k: baked[k] for k in ("solid", "oneway", "ladder", "solidz", "onewayz",
                                 "ladderz", "ladderlean", "ladderleanz")}
    out["solidgate"] = baked["solidgate"]
    out["gate"] = baked["gate"]
    out["gatesshut"] = 0
    return out, baked


def _takes_scene():
    import inspect
    return "scene" in inspect.signature(geometry.spawns).parameters


# How far a storey may step back from the one below before the climb reads as being
# thrown into the background rather than as moving up a level. Arena_Forest's own
# step, which nobody has ever complained about, is 0.66 m.
STOREY_STEP = 0.90

# No ladder has rails thicker than this. Past it the baked box is not the ladder --
# it is the ladder plus whatever else the marker swept up.
RAILS_AT_MOST = 0.30


def storeys(baked):
    """The LEVELS worth standing on, highest first, with the slice each one offers.

    A level, not a box. The hatch cutting leaves a walkway as three pieces with a hole
    between them, and treating each piece as its own storey is how the first run of this
    reported "nothing to measure" on an arena you can fall through in two places: the
    hole is never inside the piece beside it.
    """
    halfz = F(C["BODYZ"]) / 2
    levels = {}
    for box, depth in zip(baked["solid"], baked["solidz"]):
        x0, y0, x1, y1 = (F(v) for v in box)
        if x1 - x0 < MIN_WIDTH:
            continue
        zmin, zmax = F(depth[0]), F(depth[1])
        room = (zmax - zmin) / 2
        inset = min(halfz, room)
        slice_ = max(zmin + inset, min(F(C["HOME"]), zmax - inset))

        key = round(y1, 1)
        at = levels.setdefault(key, dict(top=y1, x0=x0, x1=x1, z=slice_,
                                         zmin=zmin, zmax=zmax))
        at["x0"] = min(at["x0"], x0)
        at["x1"] = max(at["x1"], x1)
        at["zmin"] = min(at["zmin"], zmin)
        at["zmax"] = max(at["zmax"], zmax)
        at["z"] = max(at["z"], slice_) if slice_ < F(C["HOME"]) else at["z"]

    found = list(levels.values())
    found.sort(key=lambda f: -f["top"])
    return found


def hole_between(baked, upper, lower):
    """An x where a body standing on the upper storey's level would fall to the lower one.

    A hatch cut for a ladder, or the open end of a walkway: anywhere inside the upper
    storey's run that the upper level does not cover and the lower one does.
    """
    covers = [(F(b[0]), F(b[2])) for b in baked["solid"]
              if abs(F(b[3]) - upper["top"]) < 0.1]

    x = max(upper["x0"], lower["x0"]) + 0.3
    stop = min(upper["x1"], lower["x1"]) - 0.3
    while x <= stop:
        if not any(x0 - 0.25 <= x <= x1 + 0.25 for x0, x1 in covers):
            return x
        x += 0.05
    return None


def drop(w, x, from_y, from_z):
    """Let a body fall from here and report where it lands and when it leaned."""
    s = S(x, from_y)
    s.depth = X(from_z)
    landed_z = None
    for _ in range(600):
        step(s, 0, w)
        if s.mode == "ground" and F(s.y) < from_y - 0.4:
            landed_z = F(s.depth)
            break
    if landed_z is None:
        return None
    for _ in range(240):
        step(s, 0, w)
    return dict(y=F(s.y), on_landing=landed_z, settled=F(s.depth))


def climbs(w, baked, problems, notes):
    """Is the baked ladder the LADDER, or the space it leans through?

    The motor holds the body LadderStandOff in front of the rungs, and works the rungs out
    from the box's depth span minus however far the box leans. That is exact when the box
    IS the ladder. It is nonsense when the box is the whole mesh's bounds and the mesh
    leans inside it: Arena_Mine's ladders are 0.10 m of rails modelled leaning 14 degrees
    inside an upright 0.97 m box, so the subtraction left 0.68 m of "thickness" and the
    fighter climbed 45 cm of air beside a ladder he was holding.

    Nothing on screen says so. The climb animation plays, the ladder is right there, and
    from the camera's own angle the gap reads as the character model being slightly off.
    So the thickness is measured instead, and a ladder whose rails come out thicker than
    any ladder has rails is a box that is not the ladder.
    """
    for i, box in enumerate(baked["ladder"]):
        x0, y0, x1, y1 = box
        z0, z1 = baked["ladderz"][i]
        lz = baked["ladderleanz"][i]
        half = m.ladder_half_thickness(w, i)
        thick, span, lean = F(half) * 2, F(z1 - z0), abs(F(lz[1] - lz[0]))
        notes.append(f"  ladder at x {F(x0):+.2f}: {span:.2f} m of depth span, "
                     f"{lean:.2f} m of it lean -> rails {thick * 1000:.0f} mm thick, "
                     f"the body climbs {(thick / 2 + F(m.C['LSTANDOFF'])) * 100:.0f} cm "
                     f"off the centre line")
        if thick > RAILS_AT_MOST:
            problems.append(
                f"the ladder at x {F(x0):+.2f} bakes {thick * 1000:.0f} mm of rails, and "
                f"no ladder is {thick:.2f} m thick. Its box is {span:.2f} m deep with "
                f"{lean:.2f} m of measured lean, so the rest is mesh the marker swept up "
                f"-- brackets, or a model that leans inside its own upright bounds. The "
                f"climb stands off half of it, so the fighter will hold the rungs from "
                f"{(thick / 2 + F(m.C['LSTANDOFF'])) * 100:.0f} cm away. Give it a thin "
                f"collider that follows the rungs.")


def stagger(floors, problems, notes):
    """How far each storey stands from the fight, and from the one below it.

    The staircase principle says the storeys step back as they rise. It does not say how
    far, and the number matters more than it looks: the camera frames the gameplay plane,
    so a storey two metres behind it is a fighter two metres further from the lens than
    the man he is fighting -- smaller, dimmer, and behind the floor edge in between. In
    the mine the third storey stood 2.35 m back, which reads exactly as being thrown into
    the background on the way up.

    A storey can only come as far forward as the lane of the storey BELOW it, plus half a
    body: closer than that and it is a ceiling over that storey rather than a floor above
    it, and the fighter underneath loses half his jump to a roof nobody drew. So the
    budget is what that rule leaves, not a preference.
    """
    if len(floors) < 2:
        return
    home = F(m.C["HOME"])
    half = F(m.C["BODYZ"] // 2)
    for f in sorted(floors, key=lambda r: r["top"]):
        notes.append(f"  storey y {f['top']:+.2f} stands at z {f['z']:+.2f}, "
                     f"{f['z'] - home:+.2f} m from the fight plane")

    order = sorted(floors, key=lambda r: r["top"])
    for below, above in zip(order, order[1:]):
        step = above["z"] - below["z"]
        # As far forward as it may come without roofing the storey under it.
        best = below["z"] + half
        notes.append(f"  y {below['top']:+.2f} -> y {above['top']:+.2f}: steps "
                     f"{step:+.2f} m back; it could come to z {best:+.2f} "
                     f"({above['zmin'] - best:+.2f} m of room in its front edge)")
        if step > STOREY_STEP and above["zmin"] > best + 0.05:
            problems.append(
                f"the storey at y {above['top']:+.2f} stands {step:.2f} m behind the one "
                f"below it, and it does not have to: its front edge is modelled at z "
                f"{above['zmin']:+.2f} and could come to {best:+.2f} without becoming a "
                f"ceiling over that storey. As it is, climbing one floor throws the "
                f"fighter {step:.2f} m into the background, away from the man he is "
                f"fighting and from the plane the camera frames.")


def main(argv):
    # Every arena in the pool unless one is named. It used to be Arena_Forest alone, which
    # is how the mine shipped with its upper storeys 1.37 m and 2.35 m behind the fight:
    # a depth check that looks at one location measures that location's depth.
    if "--scene" not in argv:
        bad = 0
        for name in geometry.arena_scenes():
            bad += main(["--scene", name] + list(argv))
        return 1 if bad else 0

    scene_name, hatches = "Arena_Forest.unity", False
    i = 0
    while i < len(argv):
        if argv[i] == "--scene":
            scene_name = argv[i + 1]; i += 2
        elif argv[i] == "--hatches":
            hatches = True; i += 1
        else:
            i += 1

    path = os.path.join(SCENES, scene_name)
    if not os.path.exists(path):
        print(f"  depth_check: no scene at {path}")
        return 1

    try:
        w, baked = world(path, hatches)
    except NotAnArena as why:
        print(f"  note: {scene_name} is dressed art, not an arena yet: {why}.")
        print("  note: run Tools > Gulag Runners > Setup > Make this scene playable, "
              "mark its floors with SimCollider, and this has something to measure.")
        print("depth_check: 0 problem(s) over 0 drop(s)")
        return 0
    floors = storeys(baked)
    problems, notes = [], []

    notes.append(f"{scene_name}: fight plane z {F(C['HOME']):.2f}, "
                 f"{len(floors)} storey(s) worth standing on, "
                 f"{len(baked['ladder'])} ladder(s)"
                 + (", baked WITH hatches" if hatches else ""))
    for f in floors:
        notes.append(f"  storey top y {f['top']:+.2f}  x {f['x0']:.1f}..{f['x1']:.1f}  "
                     f"modelled z {f['zmin']:.2f}..{f['zmax']:.2f}  "
                     f"-> a body stands at z {f['z']:.2f}")

    # Every pair of storeys that is actually above the other, with somewhere to fall from.
    tried = 0
    for upper in floors:
        for lower in floors:
            if lower["top"] >= upper["top"] - 0.4:
                continue
            # Somewhere he can actually fall: over the lower storey, inside the upper
            # one's run, and through a hole in it. Dropping from the middle of a solid
            # floor measures nothing, which is how the first version of this reported
            # "nothing to measure" on an arena with four ladders through it.
            x = hole_between(baked, upper, lower)
            if x is None:
                continue
            landing = drop(w, x, upper["top"] + 0.35, upper["z"])
            if landing is None or abs(landing["y"] - lower["top"]) > 0.5:
                continue                        # fell past it, or onto something else
            tried += 1

            moved = upper["z"] - landing["settled"]
            during = (upper["z"] - landing["on_landing"]) / moved if moved else 0.0

            if moved < -READS_AS_A_MOVE:
                problems.append(
                    f"dropping from the storey at y {upper['top']:+.2f} to the one at "
                    f"y {lower['top']:+.2f} pushes the fighter {-moved:.2f} m AWAY from "
                    f"the camera. Coming down is meant to bring him forward: the lower "
                    f"storey is modelled behind the upper one (z {lower['zmin']:.2f} "
                    f"against {upper['zmin']:.2f}), so the art has them the wrong way "
                    f"round, not the motor.")
            elif moved < READS_AS_A_MOVE:
                notes.append(f"  y {upper['top']:+.2f} -> y {lower['top']:+.2f}: "
                             f"{moved:+.2f} m, too little to read as a move -- these two "
                             f"are the same slice of the level")
            else:
                if during < DURING_THE_FALL:
                    problems.append(
                        f"dropping from y {upper['top']:+.2f} to y {lower['top']:+.2f} "
                        f"moves {moved:.2f} m towards the camera but only "
                        f"{during * 100:.0f}% of it happens before the feet land. The "
                        f"rest is a slide on the spot, which is the thing that reads as "
                        f"the fighter staying in the background.")
                else:
                    notes.append(
                        f"  y {upper['top']:+.2f} -> y {lower['top']:+.2f}: "
                        f"{moved:.2f} m towards the camera, {during * 100:.0f}% of it "
                        f"during the fall")

    if tried == 0:
        notes.append("  no storey in this scene can be dropped onto from the one above "
                     "it, so there is nothing to measure. With ladderCutsHatch off and "
                     "no gap in the upper floor, the only way down is the ladder.")

    climbs(w, baked, problems, notes)
    stagger(floors, problems, notes)

    for n in notes:
        print("  note: " + n if not n.startswith("  ") else "  " + n)
    for p in problems:
        print("  " + p)
    print(f"depth_check: {len(problems)} problem(s) over {tried} drop(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
