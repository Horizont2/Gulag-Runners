"""Proves the hand-built location in Arena_Demo.unity is actually playable.

Vlad's location was modelled in 3D: ladders stand in front of the walkways they serve, support
pillars and a handrail cross the gameplay plane, and a back wall runs the length of the arena
behind everything. Flattened onto the one plane the fight happens on, every one of those becomes
a wall or a ceiling. This runs the real motor over the real bake and checks that what is left is
a level two fighters can move around: both spawns hold, both ladders carry a fighter to the
walkway above and set them down on it, both gaps in the upper walkway can be crossed, and every
chest can be walked up to.

Numbers printed next to each check are measured, not asserted — they are what the level is.
"""
import arena_demo_geometry as G
import sim_motor_mirror as T
from sim_motor_mirror import S, step, X, F, L, R, UP, DN, JMP, DOD

W = G.world(report=True)
CHESTS = G.bake()["chest"]
SPAWNS = G.spawns()


LANE = X(SPAWNS[0][3])
_SC = G.Scene()
HALF_Z = T.C["BODYZ"] // 2


def here(i, depth=LANE):
    """Is solid i on this slice of the level. The motor asks this of every box it touches,
    so a check that does not ask it is measuring a different game."""
    return T.reaches(T.span(W, "solid", i), depth, HALF_Z)


def solids_here(depth=LANE):
    return [b for i, b in enumerate(W["solid"]) if here(i, depth)]


def fighter_at(x, y, depth=None):
    """A body starts on the slice the scene puts it on, exactly as PlayerController does."""
    st = S(x, y)
    st.depth = X(SPAWNS[0][3] if depth is None else depth)
    return st
HALF = F(T.C["W"]) / 2
BODY = F(T.C["H"])

ok = True


def check(name, cond, detail=""):
    global ok
    ok &= bool(cond)
    print(f"  {'PASS' if cond else 'FAIL'}  {name}{('  ' + detail) if detail else ''}")


def until(s, inp, cond, limit=900):
    for _ in range(limit):
        step(s, inp, W)
        if cond(s):
            return True
    return False


def hold(s, inp, frames):
    for _ in range(frames):
        step(s, inp, W)


def surface_under(x, y):
    """The highest solid top at or below y under a body standing at x."""
    best = None
    for b in solids_here():
        if b[0] < X(x + HALF) and b[2] > X(x - HALF) and F(b[3]) <= y + 0.02:
            if best is None or b[3] > best:
                best = b[3]
    return F(best) if best is not None else None


def approach(cx, base, side, reach=2.6):
    """Somewhere on the same surface, this side of x, where a whole body can stand."""
    step_m = 0.1
    d = 0.5
    while d <= reach:
        x = cx + side * d
        if surface_under(x, base + 0.05) is not None and \
                not any(b[0] < X(x + HALF) and b[2] > X(x - HALF)
                        and b[1] < X(base + BODY) and b[3] > X(base + 0.02)
                        for b in solids_here()):
            return x
        d += step_m
    return None


# ---------------------------------------------------------------- the bake itself
print("\nThe bake")
check("the arena has a floor", len(W["solid"]) > 20, f"{len(W['solid'])} solid boxes")
check("both ladders survived the bake", len(W["ladder"]) == 2)
check("all six chests baked", len(CHESTS) == 6, f"{len(CHESTS)}")

floor = max((b for b in solids_here() if F(b[3]) < 0.2), key=lambda b: b[2] - b[0])
check("the ground floor is unbroken", F(floor[2]) - F(floor[0]) > 30,
      f"x {F(floor[0]):.2f}..{F(floor[2]):.2f}, top y {F(floor[3]):.2f}")

# A ladder standing one centimetre inside its own platform must not punch a hole in it.
for i, (x, want) in enumerate(((-16.03, 0.95), (12.60, 0.95))):
    plat = [b for b in solids_here()
            if b[0] <= X(x) <= b[2] and abs(F(b[3]) - want) < 0.05]
    check(f"ladder {i} keeps the platform it stands on", plat,
          f"x {x:.2f}: {'solid' if plat else 'HOLE'}")

# Nothing may roof the upper walkway: the handrail sits 0.64 m above it, a fighter is 0.91 m.
roof = [b for b in solids_here() if 3.9 < F(b[1]) < 3.83 + BODY and F(b[2]) - F(b[0]) > 5]
check("no ceiling over the upper walkway", not roof,
      f"{len(roof)} long box(es) in the headroom")

# ---------------------------------------------------------------- spawns
print("\nSpawns")
for idx, (sx, sy, facing, sz) in enumerate(SPAWNS):
    s = fighter_at(sx, sy, sz)
    landed = until(s, 0, lambda s: s.mode == "ground", 180)
    surf = surface_under(sx, F(s.y) + 0.1)
    check(f"fighter {idx + 1} lands on the plateau", landed and F(s.y) > 0.9,
          f"spawn ({sx:.2f}, {sy:.2f}) -> y {F(s.y):.2f}, surface {surf}")
    check(f"fighter {idx + 1} spawns facing the middle",
          (facing > 0) == (sx < -1.89), f"facing {facing:+d}")

# A depth band is a guess about where the level is, and a wrong guess used to hand the match
# a world with no floor at all. It now takes itself back rather than do that.
import copy as _copy
_narrow = G.Scene()
for _fid, (_cls, _b) in list(_narrow.docs.items()):
    if _cls == 114 and f'guid: {G.BAKER_GUID}' in _b:
        _narrow.docs[_fid] = (_cls, _b.replace('planeZ: -1.6', 'planeZ: 400'))
_rescued = G.bake(_narrow)
check("a depth band that culls the whole arena takes itself back",
      len(_rescued["solid"]) > 20,
      f"a band 400 m away still bakes {len(_rescued['solid'])} solid boxes")

# The whole failure mode of a hand-marked location in one check. Mark the building's back
# wall solid along with everything else and it lands across the ground floor at chest height;
# a body that starts inside a solid is not pushed out of it — MoveY leaves an overlapping box
# alone on purpose, or a fighter who clips a floor teleports a storey — so it falls through
# the world instead, forever.
for idx, (sx, sy, _, sz) in enumerate(SPAWNS):
    s = fighter_at(sx, sy, sz)
    until(s, 0, lambda s: s.mode == "ground", 180)
    inside = [b for i, b in enumerate(W["solid"])
              if T.overlaps(T.body(s), b) and here(i, s.depth)]
    check(f"fighter {idx + 1} does not spawn inside anything", not inside,
          f"{len(inside)} solid(s) around ({F(s.x):.2f}, {F(s.y):.2f})")
    check(f"fighter {idx + 1} is still on the ground a second later",
          until(s, 0, lambda s: F(s.y) < -1.0, 60) is False and s.mode == "ground",
          f"y {F(s.y):.2f} mode {s.mode}")

check("the two spawns are mirrored about the arena centre",
      abs((SPAWNS[0][0] + SPAWNS[1][0]) / 2 + 1.89) < 0.02,
      f"midpoint x {(SPAWNS[0][0] + SPAWNS[1][0]) / 2:.3f}")

# ------------------------------------------- the model has to stand where the body stands
print("\nThe fighters' models")

# Render writes the simulated position, slice included, onto the PlayerController's own
# object every frame — so an offset on the visual root under it draws the fighter somewhere
# he is not. One model here was 2.92 m in front of its body and the other 3.28 m to the side
# and 3.3 m in the air, which looks exactly like a fighter standing on invisible geometry,
# because that is what it is: the collision is right and the picture is somewhere else.
for who, (ox, oy, oz) in _SC.visual_offsets():
    off = max(abs(ox), abs(oy), abs(oz))
    check(f"{who}: the model sits on the body the simulation moves", off < 0.001,
          f"offset ({ox:.2f}, {oy:.2f}, {oz:.2f})")

# ---------------------------------------------------------------- ground floor
print("\nThe ground floor")
# The two crate stacks are 2 m tall and the step-up is 0.30 m, so crossing the arena means
# hopping up and over them. It has to be possible, and it has to cost both fighters the same.
times = []
for idx, (sx, sy, _, _z) in enumerate(SPAWNS):
    target = SPAWNS[1 - idx][0]
    s = fighter_at(sx, sy)
    until(s, 0, lambda s: s.mode == "ground", 180)
    inp = R if target > sx else L
    frames = None
    for f in range(1500):
        step(s, inp | (JMP if f % 20 == 0 else 0), W)
        if abs(F(s.x) - target) < 0.6:
            frames = f
            break
    times.append(frames)
    check(f"fighter {idx + 1} can cross the arena to the other spawn", frames is not None,
          f"{sx:.2f} -> {F(s.x):.2f} in {frames / 60:.1f} s" if frames
          else f"stuck at x {F(s.x):.2f}, y {F(s.y):.2f}")
# Not exactly equal: the two crate clusters are a few centimetres apart in width and the
# fighters drift onto slightly different slices crossing them. Within a tenth of the trip.
check("the crossing costs both fighters about the same",
      all(times) and abs(times[0] - times[1]) < 60,
      f"{times[0] / 60:.1f} s and {times[1] / 60:.1f} s" if all(times) else "one of them is stuck")

# ---------------------------------------------------------------- ladders
print("\nLadders")
LADDERS = sorted(W["ladder"], key=lambda b: b[0])
for idx, lad in enumerate(LADDERS):
    cx = (F(lad[0]) + F(lad[2])) / 2
    base = F(lad[1])
    s = fighter_at(cx + (0.9 if idx == 0 else -0.9), base + 0.2)
    until(s, 0, lambda s: s.mode == "ground", 180)
    check(f"ladder {idx}: a fighter can walk to its foot",
          until(s, L if idx == 0 else R, lambda s: abs(F(s.x) - cx) < 0.35, 300),
          f"x {F(s.x):.2f} (ladder centre {cx:.2f})")
    check(f"ladder {idx}: grabs on", until(s, UP, lambda s: s.mode in ("ladder", "mount"), 60),
          f"mode {s.mode}")
    check(f"ladder {idx}: climbs to the top",
          until(s, UP, lambda s: F(s.y) >= F(lad[3]) - 0.1, 600), f"y {F(s.y):.2f}")
    at_top = F(s.x)
    check(f"ladder {idx}: climbs out onto the walkway by itself",
          until(s, UP, lambda s: s.mode == "ground", 300),
          f"mode {s.mode} at ({F(s.x):.2f}, {F(s.y):.2f})")
    check(f"ladder {idx}: ends up standing on the walkway",
          abs(F(s.y) - 3.83) < 0.06, f"y {F(s.y):.2f}")
    # This location's ladders stand in FRONT of the walkway they serve, so there is no hatch
    # and no step sideways: the last move is onto the floor the rungs end at. A sideways
    # climb-out here would be a fighter stepping through the slab he just climbed past.
    check(f"ladder {idx}: climbs out forwards, not sideways",
          abs(F(s.x) - at_top) < 0.2, f"moved {abs(F(s.x) - at_top):.2f} m sideways")
    check(f"ladder {idx}: the walkway it serves has no hole cut in it",
          any(b[0] <= X(cx) <= b[2] and abs(F(b[3]) - 3.83) < 0.02 for b in solids_here()),
          f"solid over x {cx:.2f}")

    # And back down: standing on the ladder's top, pressing down steps over the edge onto it.
    caught = until(s, DN, lambda s: s.mode in ("ladder", "mount"), 120)
    check(f"ladder {idx}: pressing down on top of it steps onto the rungs", caught,
          f"mode {s.mode} at ({F(s.x):.2f}, {F(s.y):.2f})")
    check(f"ladder {idx}: carries a fighter back down",
          caught and until(s, DN, lambda s: F(s.y) <= base + 0.1, 600), f"y {F(s.y):.2f}")
    check(f"ladder {idx}: lets go at the bottom",
          until(s, DN, lambda s: s.mode == "ground", 120), f"mode {s.mode} y {F(s.y):.2f}")

# -------------------------------------------- the ladder is used the way a player uses one
print("\nThe ladder as a player actually works it")

# You walk INTO a ladder holding a direction. Pressing up without letting go of that direction
# is the most ordinary input there is, and it used to slide the fighter straight back off the
# rungs and drop him — from his side, the ladder simply did nothing.
for idx, lad in enumerate(LADDERS):
    cx = (F(lad[0]) + F(lad[2])) / 2
    toward = L if idx == 0 else R
    # Standing on the platform the ladder's foot is on. 0.7 m, not more: the right plateau
    # only reaches 0.79 m past its ladder, and starting beyond it is starting on the floor.
    s = fighter_at(cx + (0.7 if idx == 0 else -0.7), F(lad[1]) + 0.2)
    until(s, 0, lambda s: s.mode == "ground", 180)
    grabbed = until(s, toward | UP, lambda s: s.mode in ("ladder", "mount"), 240)
    check(f"ladder {idx}: walking into it and pressing up grabs it", grabbed, f"mode {s.mode}")
    check(f"ladder {idx}: and holding that same direction does not shake it off",
          grabbed and until(s, toward | UP, lambda s: F(s.y) > F(lad[1]) + 1.0, 240)
          and s.mode in ("ladder", "mount", "mantle"),
          f"y {F(s.y):.2f} mode {s.mode}")
    check(f"ladder {idx}: and it carries him all the way out onto the walkway",
          until(s, toward | UP, lambda s: s.mode == "ground" and F(s.y) > 3.7, 600),
          f"({F(s.x):.2f}, {F(s.y):.2f}) mode {s.mode}")

    # The walkway is a lane further from the camera than the platform the ladder stands on, so
    # getting off at the top is a step BACK as well as up. A climb-out held to one slice finds
    # no floor at all and leaves the fighter hanging at the top of the ladder forever.
    walkway = next((i for i, b in enumerate(W["solid"])
                    if b[0] <= X(cx) <= b[2] and abs(F(b[3]) - 3.83) < 0.02), None)
    check(f"ladder {idx}: ends up on the walkway's own slice, not the ladder's",
          walkway is not None
          and T.reaches(T.span(W, "solid", walkway), s.depth, HALF_Z),
          f"depth {F(s.depth):.2f}, walkway z "
          + (f"{F(T.span(W, 'solid', walkway)[0]):.2f}.."
             f"{F(T.span(W, 'solid', walkway)[1]):.2f}" if walkway is not None else "?"))

# The whole round trip, floor to walkway and back, driven only by the inputs a player gives.
for idx, lad in enumerate(LADDERS):
    cx = (F(lad[0]) + F(lad[2])) / 2
    inward = R if idx == 0 else L          # back toward the middle of the arena
    toward = L if idx == 0 else R
    s = fighter_at(cx + (3.0 if idx == 0 else -3.0), 1.2)
    until(s, 0, lambda s: s.mode == "ground", 180)
    up = until(s, toward | UP, lambda s: s.mode == "ground" and F(s.y) > 3.7, 900)
    check(f"side {idx}: the floor reaches the walkway with one held direction and up", up,
          f"({F(s.x):.2f}, {F(s.y):.2f})")
    back = up and until(s, DN, lambda s: s.mode in ("ladder", "mount"), 180) \
              and until(s, DN, lambda s: s.mode == "ground" and F(s.y) < 1.1, 600)
    check(f"side {idx}: and pressing down brings him back to the platform", back,
          f"({F(s.x):.2f}, {F(s.y):.2f})")
    check(f"side {idx}: and the plank puts him back on the ground floor",
          back and until(s, inward, lambda s: F(s.y) < 0.2, 600), f"y {F(s.y):.2f}")

# The backdrop is scenery. It used to be baked and held off the fight by each box's depth span
# alone, which works exactly as long as nobody changes lane — and changing lane is how a ramp
# and a ladder are used here.
plane_z = _SC.baker_field("planeZ", float, 0.0)
behind = plane_z + _SC.baker_field("backdropBehind", float, 0.5)
baked_behind = [b for i, b in enumerate(W["solid"])
                if F(T.span(W, "solid", i)[0]) >= behind]
check("nothing standing behind the gameplay plane is baked at all",
      not baked_behind,
      f"{len(baked_behind)} box(es) at z >= {behind:.2f}"
      if baked_behind else f"backdrop starts at z {behind:.2f}")

# ---------------------------------------------------------------- the walkway gaps
print("\nThe gaps in the upper walkway")
tops = sorted((b for b in solids_here() if abs(F(b[3]) - 3.83) < 0.02), key=lambda b: b[0])


def spanned_by_ladder(a, b):
    """A hatch is not a gap in the walkway: the ladder is what you cross it on."""
    return any(F(l[0]) <= b + 0.15 and F(l[2]) >= a - 0.15 for l in W["ladder"])


runs = []
for b in tops:
    if runs and (F(b[0]) - runs[-1][1] < 0.2 or spanned_by_ladder(runs[-1][1], F(b[0]))):
        runs[-1][1] = max(runs[-1][1], F(b[2]))
    else:
        runs.append([F(b[0]), F(b[2])])
print("   walkway segments: " + ", ".join(f"{a:.2f}..{b:.2f}" for a, b in runs))
gaps = [(runs[i][1], runs[i + 1][0]) for i in range(len(runs) - 1)]
for a, b in gaps:
    print(f"   gap {a:.2f}..{b:.2f} = {b - a:.2f} m")
check("the upper walkway is in three segments with two gaps", len(gaps) == 2)


def climb_to(target_x, from_x, from_y):
    """Walk and hop from here toward there, the way a player works up a crate stack."""
    s = fighter_at(from_x, from_y)
    if not until(s, 0, lambda s: s.mode == "ground", 180):
        return None
    toward = R if target_x > from_x else L
    for f in range(900):
        up = F(s.y) > 3.7
        step(s, toward if up else toward | (JMP if f % 16 == 0 else 0), W)
        if up and s.mode == "ground":
            return s
    return s


# Every segment of the upper walkway has to be reachable, and they are not reached the same
# way: the outer two by their ladders, the middle one by whatever stands under it. Rather
# than assert a route that the level may no longer have, this walks every segment and says
# which ones cannot be got onto — a level under construction should be measured, not failed.
print("   reachability of each walkway segment:")
unreachable = []
for i, (lo, hi) in enumerate(runs):
    mid = (lo + hi) / 2
    got = False
    for start_x in (SPAWNS[0][0], SPAWNS[1][0], mid, lo - 2.0, hi + 2.0):
        if not (-17.0 < start_x < 13.5):
            continue
        s = fighter_at(start_x, 1.2)
        if not until(s, 0, lambda s: s.mode == "ground", 180):
            continue
        toward = R if mid > F(s.x) else L
        # Two routes, tried apart: a buffered jump cancels a ladder mount, so holding both
        # means never climbing anything.
        for climbing in (True, False):
            s = fighter_at(start_x, 1.2)
            if not until(s, 0, lambda s: s.mode == "ground", 180):
                continue
            for f in range(1400):
                inp = toward | (UP if climbing else (JMP if f % 16 == 0 else 0))
                step(s, inp, W)
                if F(s.y) > 3.7 and s.mode == "ground":
                    got = True
                    break
            if got:
                break
        if got:
            break
    print(f"     {lo:7.2f}..{hi:6.2f}  {'reachable' if got else 'NO WAY UP from the floor'}")
    if not got:
        unreachable.append((lo, hi))

check("at least the walkway segments the ladders serve can be got onto",
      len(unreachable) < len(runs),
      f"{len(unreachable)} of {len(runs)} segments have no route: "
      + ", ".join(f"{a:.1f}..{b:.1f}" for a, b in unreachable))

# A box with nothing under it is almost always a leftover rather than a design.
floating = []
for i, b in enumerate(W["solid"]):
    if not here(i) or F(b[1]) < 0.3 or F(b[2]) - F(b[0]) > 1.5:
        continue
    under = any(here(j) and c[0] < b[2] and c[2] > b[0]
                and abs(F(c[3]) - F(b[1])) < 0.12
                for j, c in enumerate(W["solid"]) if j != i)
    if not under:
        floating.append(b)
print(f"   {len(floating)} box(es) with nothing under them:")
for b in floating[:6]:
    print(f"     X {F(b[0]):7.2f}..{F(b[2]):6.2f}  Y {F(b[1]):5.2f}..{F(b[3]):5.2f}")

# And the gaps themselves stay gaps: knocked into one, a fighter leaves the upper walkway and
# has to climb back. Each one has a crate stack under it, so the drop is onto the stairs he
# came up rather than into nothing.
for i, (a, b) in enumerate(gaps):
    s = fighter_at((a + b) / 2, 3.9)
    fell = until(s, 0, lambda s: s.mode == "ground", 300)
    check(f"gap {i + 1} ({b - a:.2f} m) takes a fighter off the upper walkway",
          fell and F(s.y) < 3.0, f"landed at y {F(s.y):.2f}")

    s = fighter_at(a - 3.0, 3.95)
    until(s, 0, lambda s: s.mode == "ground", 180)
    until(s, R, lambda s: F(s.x) > a - 0.45, 600)
    step(s, R | JMP, W)
    until(s, R, lambda s: s.mode == "ground" or F(s.y) < 2.0, 300)
    check(f"gap {i + 1} cannot be jumped, so it stays a hazard", F(s.x) < b,
          f"a running jump reached x {F(s.x):.2f} of {b:.2f}")

# ---------------------------------------------------------------- the ramps
print("\nThe planks leaning against the plateaus")
# An AABB world has no slopes, so the bake cuts a tilted box into steps along its top edge.
for name, start_x, toward, top in (("left", -12.0, L, 0.95), ("right", 8.2, R, 0.96)):
    s = fighter_at(start_x, 0.3)
    until(s, 0, lambda s: s.mode == "ground", 180)
    base = F(s.y)
    walked = until(s, toward, lambda s: F(s.y) > top - 0.10, 600)
    check(f"the {name} plank is walked up without a jump", walked,
          f"{base:.2f} -> {F(s.y):.2f} m")
    check(f"and walking up it puts the fighter on the plank's own slice",
          -3.4 < F(s.depth) < -2.3, f"depth {F(s.depth):.2f}")

# Each step has to sit ON the board, not above it. The tops are sampled at each step's
# MIDDLE for exactly this: taking the higher of the two ends put every step above the board
# it stands in for, and walking down the ramp meant walking down it through the air.
PLANK = next(fid for fid in _SC.gos
             if fid in _SC.tr_of_go and _SC.bounds(fid)
             and -15.4 < _SC.bounds(fid)[0][0] < -15.1
             and _SC.bounds(fid)[1][1] < 1.05 and _SC.bounds(fid)[0][2] < -2.0)
CORNERS = _SC.corners(PLANK)
STEPS = [b for b in W["solid"]
         if -15.4 < F(b[0]) < -13.2 and F(b[1]) < 0.2 and 0.1 < F(b[3]) < 1.0]
worst = max((abs(F(b[3]) - G._top_at(CORNERS, (F(b[0]) + F(b[2])) / 2)) for b in STEPS),
            default=99.0)
check("every step of the ramp sits on the board's own surface", worst < 0.02,
      f"{len(STEPS)} steps, worst {worst * 100:.1f} cm off it")

# A leaning ladder's box is wider than the ladder, so the climb follows a line, not a point.
print("\nLadders that lean")
for i, lean in enumerate(W["ladderlean"]):
    drift = abs(F(lean[1]) - F(lean[0]))
    print(f"   ladder {i}: foot x {F(lean[0]):7.2f}, head x {F(lean[1]):7.2f} "
          f"({drift * 100:.0f} cm of lean)")
for i, l in enumerate(W["ladder"]):
    mid_y = (F(l[1]) + F(l[3])) / 2
    centre = T.ladder_centre(W, i, X(mid_y))
    check(f"ladder {i} is climbed along its own line",
          F(l[0]) - 0.01 <= F(centre) <= F(l[2]) + 0.01,
          f"centre at half height is x {F(centre):.2f} of {F(l[0]):.2f}..{F(l[2]):.2f}")

# ---------------------------------------------------------------- the edges
print("\nThe ends of the arena")
for idx, (toward, label) in enumerate(((-1, "left"), (1, "right"))):
    sx, sy, _, _z = SPAWNS[0 if toward < 0 else 1]
    s = fighter_at(sx, sy)
    until(s, 0, lambda s: s.mode == "ground", 180)
    inp = L if toward < 0 else R
    # walk at it, and keep jumping, which is the honest way a player finds an open edge
    out = False
    for f in range(600):
        step(s, inp | (JMP if f % 24 == 0 else 0), W)
        if F(s.y) < -1.0:
            out = True
            break
    check(f"a fighter cannot walk off the {label} end of the arena", not out,
          f"ended at ({F(s.x):.2f}, {F(s.y):.2f})")

# ---------------------------------------------------------------- what the camera sees
print("\nWhat the camera sees")
# SideViewCamera stands at planeZ - distance and looks along +Z. A level modelled in 3D has a
# side it is meant to be seen from, and if the camera is on the other one the fight happens
# behind the scenery. This measures it instead of trusting the scene view.
CAM = G.camera()
print(f"   view {CAM['height']:.2f} m tall, camera {CAM['distance']:.2f} m out at "
      f"z {CAM['z']:.2f}, plane at z {CAM['planeZ']:.2f}")
OCC = G.Scene().occluders(CAM['planeZ'])


def hidden(feet, x):
    """Fraction of the frame ABOVE THE FLOOR covered by scenery in front of a fighter here.

    Only the part of the frame a fighter can actually be in counts. Scenery across the bottom,
    below the surface being stood on, is the foreground ledge that gives the shot its depth —
    measuring it as occlusion would push the camera into flattening the one thing worth
    keeping.
    """
    focus = feet + 0.55
    cam_y = focus + CAM['rise']
    lo_y, hi_y = max(feet, focus - CAM['height'] / 2), focus + CAM['height'] / 2
    half_w = CAM['height'] * CAM['aspect'] / 2
    worst, by = 0.0, None
    for name, lo, hi in OCC:
        d = lo[2] - CAM['z']
        if d <= 0.2 or hi[0] < x - half_w or lo[0] > x + half_w:
            continue
        scale = CAM['distance'] / d
        top = (hi[1] - cam_y) * scale + cam_y
        bot = (lo[1] - cam_y) * scale + cam_y
        tall = max(0.0, min(top, hi_y) - max(bot, lo_y)) / max(0.01, hi_y - lo_y)
        wide = (min(hi[0], x + half_w) - max(lo[0], x - half_w)) / (2 * half_w)
        if tall * wide > worst:
            worst, by = tall * wide, name or "(cube)"
    return worst, by


for label, feet, xs in (("ground floor", 0.12, (-12.0, -6.0, -2.0, 3.0, 8.0)),
                        ("spawn plateaus", 0.97, (SPAWNS[0][0], SPAWNS[1][0])),
                        ("upper walkway", 3.83, (-11.5, -5.0, 0.0, 7.7))):
    worst, by = max((hidden(feet, x) for x in xs), key=lambda r: r[0])
    check(f"the {label} is not shot through the scenery", worst < 0.25,
          f"worst {worst * 100:.0f}% of the frame, by {by}")

# the arena has to fit the camera's reach, or half of it is never framed
for cam in G.cameras():
    check("the camera borders hold the whole arena",
          cam['min'][0] <= -17.6 and cam['max'][0] >= 13.8
          and cam['min'][1] <= 0.1 and cam['max'][1] >= 4.8,
          f"({cam['min'][0]}, {cam['min'][1]})..({cam['max'][0]}, {cam['max'][1]})")
    travel = (cam['max'][1] - cam['min'][1]) - CAM['height']
    check("the camera can follow a fighter between the floors", travel > 1.2,
          f"{travel:.2f} m of vertical travel for a {CAM['height']:.2f} m view")

# ---------------------------------------------------------------- the chests
print("\nChests")
for box, kind, contents, name in CHESTS:
    cx = (F(box[0]) + F(box[2])) / 2
    surf = surface_under(cx, F(box[1]) + 0.05)
    check(f"{name} stands on a surface", surf is not None and abs(surf - F(box[1])) < 0.05,
          f"base y {F(box[1]):.2f}, surface {surf}")
    # nothing solid may sit inside the reach box, or the fighter cannot get to it
    blocked = [b for b in solids_here()
               if b[0] < box[2] and b[2] > box[0] and b[1] < box[3] and b[3] > box[1] + X(0.02)]
    check(f"{name} is not buried in scenery", not blocked, f"{len(blocked)} overlapping solid(s)")
    # And a fighter walking along the surface reaches it, from whichever side is clear.
    reached, frm = False, None
    for side in (-1, 1):
        start = approach(cx, F(box[1]), side)
        if start is None:
            continue
        s = fighter_at(start, F(box[1]) + 0.12)
        if not until(s, 0, lambda s: s.mode == "ground", 180):
            continue
        if until(s, R if side < 0 else L, lambda s: T.overlaps(T.body(s), box), 400):
            reached, frm = True, start
            break
    check(f"{name} can be walked up to", reached,
          f"from x {frm:.2f}" if reached else "no clear approach on either side")

kinds = sorted(k for _, k, _, _ in CHESTS)
check("two chests of each tier", kinds == [0, 0, 1, 1, 2, 2], f"kinds {kinds}")
by_kind = {}
for box, kind, contents, name in CHESTS:
    by_kind.setdefault(kind, []).append((F(box[0]) + F(box[2])) / 2)
for kind, xs in sorted(by_kind.items()):
    check(f"tier {kind} chests are mirrored about the arena centre",
          abs(sum(xs) / 2 + 1.89) < 0.05, f"x {xs[0]:.2f} and {xs[1]:.2f}")

print(f"\n{'ALL PASS' if ok else 'FAILURES ABOVE'}")
raise SystemExit(0 if ok else 1)
