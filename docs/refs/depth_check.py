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
from sim_motor_mirror import S, step, X, F, C, JMP   # noqa: E402

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
    return out, baked, scene


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

# How much the lane may still move after the feet have landed. The move itself belongs
# in the air, where the eye reads it as leaning into the drop.
SLIDE_AFTER = 0.15

# How far a climbable box may sit from the nearest thing that draws. Touching is the
# intent; a hand's breadth is the slack for a box drawn a little proud of its art.
ART_GAP = 0.12

# How near a ladder's head has to be to a storey's front edge to count as leaning on it.
# Past this the two are not touching and the floor is free to come forward.
LADDER_PINS = 0.25

# What counts as a thing you climb, for the purpose of asking whether a climbable box
# is standing on one: taller than this, and no wider than that.
LADDER_TALL, LADDER_NARROW = 1.5, 1.2


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


def only_by_ladder(w, floors, problems, notes):
    """Can a storey be reached by jumping, when the only way up is meant to be a ladder?

    This is a rule about the LEVEL and not about the jump: the gap between storeys is
    bigger than a jump in every arena here, so the answer ought to be no everywhere, and
    where it is yes it is because of something standing in between. In Arena_Forest it
    was the border wall itself -- it ended at y +0.10, two centimetres under the terrace
    at +0.12, so the wall that is supposed to END the ground floor was a step onto the
    floor above it. The feet peak at -0.17 and Step Up Height is 0.30, so running into
    it while jumping put the fighter on top, and from there he simply walked on.

    Nothing about that is visible in the scene: the wall is the right height to be a
    wall, the gap is too big to jump, and the exploit lives in the 0.02 m between two
    numbers nobody would think to compare. So it is jumped, on the real motor, from
    along every storey and in all three directions.
    """
    if len(floors) < 2:
        return
    order = sorted(floors, key=lambda r: r["top"])
    tried = 0
    for i, f in enumerate(order[:-1]):
        above = order[i + 1]["top"]
        x = f["x0"] + 0.4
        while x <= f["x1"] - 0.4:
            for push, way in ((0, "straight up"), (m.L, "running left"), (m.R, "running right")):
                s = S(x, f["top"] + 0.1)
                s.depth = X(f["z"])
                for _ in range(20):
                    step(s, 0, w)
                if abs(F(s.y) - f["top"]) > 0.4:
                    break                       # not actually standing here
                tried += 1
                for tick in range(170):
                    step(s, push | (JMP if tick < 3 else 0), w)
                if F(s.y) >= above - 0.2:
                    problems.append(
                        f"from x {x:+.1f} on the storey at y {f['top']:+.2f}, {way} and "
                        f"jumping ends at y {F(s.y):+.2f} -- on the storey at "
                        f"y {above:+.2f}, which is meant to be reachable only by ladder. "
                        f"The gap is {above - f['top']:.2f} m and a jump is "
                        f"{F(C['JUMP']) ** 2 / (2 * F(C['GRAV'])):.2f} m, so something in "
                        f"between is being stood on.")
                    x = f["x1"]                 # one finding per storey is enough
                    break
            x += 1.0
    notes.append(f"  {tried} jump(s) tried from along the storeys; none reached the one above")


def off_a_perch(w, baked, problems, notes):
    """Step off a prop that stands behind the fight, and arrive where the fight is.

    The storeys are one case of the lane rule and the easy one, because a storey is wide
    and runs the length of the arena. The hard case is a PROP: Arena_Forest's hanging
    cages are 0.68 m deep at z -0.97..-0.29, two and a half metres behind the plane, and
    they are solid, which makes them platforms a fighter can stand and fight on.

    Coming off one, the lane has to be back at the fight by the time the feet land, and
    it used not to move at all. LaneOn fell back to "the nearest part of that floor to
    where the body already is" whenever the floor did not reach the plane, and the
    terrace it lands on is 4.4 m deep and stops 23 cm short: a body arriving at z -0.67
    was already inside that span, so the nearest part of it was where it already was. He
    landed two and a half metres behind the man he was fighting and stayed there for the
    rest of the round.

    So every perch that stands off the plane is stepped off, on the real motor, and what
    is measured is where the feet land and whether anything slides afterwards.
    """
    half = F(C["BODYZ"] // 2)
    home = F(C["HOME"])
    tried = 0
    for i, box in enumerate(baked["solid"]):
        x0, y0, x1, y1 = [F(v) for v in box]
        if x1 - x0 > 4.0 or x1 - x0 < 0.5:
            # Wider than this is a storey, which stagger() measures; narrower is the
            # top edge of a border wall, which is not somewhere anybody stands -- the
            # jump sweep above is what proves nobody can get onto one.
            continue
        z0, z1 = [F(v) for v in baked["solidz"][i]]
        lane = max(z0 + half, min(home, z1 - half))
        if abs(lane - home) < 0.5:
            continue                               # near enough the fight already
        for push, way in ((m.L, "left"), (m.R, "right")):
            s = S((x0 + x1) / 2, y1 + 0.15)
            s.depth = X(lane)
            for _ in range(40):
                step(s, 0, w)
            if abs(F(s.y) - y1) > 0.3:
                break                              # cannot actually stand on it
            tried += 1
            landed = None
            for _ in range(240):
                was = s.mode
                step(s, push, w)
                if landed is None and was == "air" and s.mode == "ground":
                    landed = (F(s.y), F(s.depth))
            if landed is None:
                continue
            slide = abs(F(s.depth) - landed[1])
            notes.append(f"  off the perch at x {x0:+.1f} y {y1:+.2f} (z {lane:+.2f}), "
                         f"{way}: lands on y {landed[0]:+.2f} at z {landed[1]:+.2f}, "
                         f"{slide * 100:.0f} cm of slide after")
            # The slide is the whole measurement. Landing on the SAME slice is not
            # wrong on its own -- a perch standing over a storey that is itself a lane
            # behind the plane lands on that storey's lane and should not move -- but
            # a lane move that happens after the feet touch is the lane move happening
            # in the wrong place, and that is what reads as being dragged.
            if slide > SLIDE_AFTER:
                problems.append(
                    f"stepping {way} off the perch at x {x0:+.1f}, y {y1:+.2f} slides "
                    f"{slide * 100:.0f} cm AFTER the feet land, which is the lane move "
                    f"happening on the ground instead of in the air -- the thing that "
                    f"reads as the fighter landing wrong and then being dragged.")
    if tried:
        notes.append(f"  {tried} perch(es) stepped off")


def on_the_art(scene, baked, problems, notes):
    """Does the climbable box sit on a ladder anybody can see?

    A marker and the thing it marks are two objects, and nothing holds them together.
    Arena_Mine's two ladders were nudged 0.67 and 0.95 m towards the camera to follow
    the storeys; the boxes standing beside them in the scene stayed where they were, and
    the fighter went on climbing the air where the ladder used to be -- behind it now,
    which is exactly what it looked like.

    Those two live inside the ladder prefab since, so they cannot drift again. This is
    for every other one: a climbable box has to overlap something that DRAWS, or it is a
    ladder made of nothing.
    """
    # Ladder-SHAPED art, and not merely art. The first version of this asked only that
    # the box overlap something that draws, and the two boxes that had been left behind
    # still passed: they had drifted 0.67 m along a floor slab thirty metres wide and
    # were still inside it. A thing you climb is tall and narrow, and that is the only
    # thing worth comparing a climbable box against.
    drawn = []
    for go, d in scene.gos.items():
        tr = scene.tr_of_go.get(go)
        if tr is None:
            continue
        if scene.ancestor_has(go, geometry.PLAYER_GUID):
            continue
        # A renderer in the scene, or a prefab instance whose mesh is in its own file --
        # the mine's ladders are the second kind, so asking only the first finds nothing.
        if not scene.renders(go) and not scene.gos[go].get("guid"):
            continue
        origin, basis = scene.basis(tr)
        centre, half = scene.local_box(go)
        wc = [origin[i] + geometry._apply(basis, centre)[i] for i in range(3)]
        ext = [sum(abs(basis[i][k]) * half[k] for k in range(3)) for i in range(3)]
        lo = [wc[i] - ext[i] for i in range(3)]
        hi = [wc[i] + ext[i] for i in range(3)]
        tall = hi[1] - lo[1]
        across = max(hi[0] - lo[0], hi[2] - lo[2])
        if tall < LADDER_TALL or across > LADDER_NARROW:
            continue
        drawn.append((lo, hi, d["name"]))

    for i, box in enumerate(baked["ladder"]):
        x0, y0, x1, y1 = [F(v) for v in box]
        z0, z1 = [F(v) for v in baked["ladderz"][i]]
        lo, hi = [x0, y0, z0], [x1, y1, z1]
        # How far the climbable box sticks OUT of the art's own bounds, not whether the
        # two touch. A leaning ladder's bounds are a metre deep, so a box that has
        # drifted two thirds of a metre still overlaps them -- it is simply no longer
        # inside them, which is the thing that puts the fighter behind the rungs.
        best, who = None, "nothing ladder-shaped"
        for dlo, dhi, name in drawn:
            out = max(max(dlo[k] - lo[k], hi[k] - dhi[k], 0.0) for k in range(3))
            if best is None or out < best:
                best, who = out, name
        notes.append(f"  the ladder at x {x0:+.2f} is inside {who}"
                     + (f", {best * 100:.0f} cm proud of it" if best is not None else ""))
        if best is None or best > ART_GAP:
            problems.append(
                f"the climbable box at x {x0:+.2f}, y {y0:+.2f}..{y1:+.2f} is "
                + (f"{best * 100:.0f} cm outside the nearest ladder-shaped thing "
                   f"({who})." if best is not None
                   else "nowhere near anything ladder-shaped.")
                + f" A fighter will climb it and there will be no ladder under his "
                f"hands -- which is what a marker left behind when its art moves looks "
                f"like. Put the box inside the ladder's own prefab, where it cannot be "
                f"left behind.")


def stagger(floors, baked, problems, notes):
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
        # A ladder leaning on the storey's front lip PINS it. Move the floor forward
        # and the ladder it serves is left behind it, poking up through the deck --
        # which is why the Nordic fortress's top storey stands where it does, and it
        # is a reason, not an oversight.
        pinned = None
        for i, box in enumerate(baked["ladder"]):
            head_y = F(box[3])
            if abs(head_y - above["top"]) > 0.4:
                continue
            lean = baked["ladderleanz"][i]
            head_z = F(lean[1]) if F(lean[1]) > F(lean[0]) else F(lean[0])
            if abs(head_z - above["zmin"]) <= LADDER_PINS:
                pinned = head_z
                break
        if pinned is not None:
            notes.append(f"    and it cannot: the ladder serving it leans on that lip at "
                         f"z {pinned:+.2f}")
            continue

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
        w, baked, scene = world(path, hatches)
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
    on_the_art(scene, baked, problems, notes)
    stagger(floors, baked, problems, notes)
    only_by_ladder(w, floors, problems, notes)
    off_a_perch(w, baked, problems, notes)

    for n in notes:
        print("  note: " + n if not n.startswith("  ") else "  " + n)
    for p in problems:
        print("  " + p)
    print(f"depth_check: {len(problems)} problem(s) over {tried} drop(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
