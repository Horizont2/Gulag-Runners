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
    for b in W["solid"]:
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
                        for b in W["solid"]):
            return x
        d += step_m
    return None


# ---------------------------------------------------------------- the bake itself
print("\nThe bake")
check("the arena has a floor", len(W["solid"]) > 20, f"{len(W['solid'])} solid boxes")
check("both ladders survived the bake", len(W["ladder"]) == 2)
check("all six chests baked", len(CHESTS) == 6, f"{len(CHESTS)}")

floor = max((b for b in W["solid"] if F(b[3]) < 0.2), key=lambda b: b[2] - b[0])
check("the ground floor is unbroken", F(floor[2]) - F(floor[0]) > 30,
      f"x {F(floor[0]):.2f}..{F(floor[2]):.2f}, top y {F(floor[3]):.2f}")

# A ladder standing one centimetre inside its own platform must not punch a hole in it.
for i, (x, want) in enumerate(((-16.03, 0.95), (12.60, 0.95))):
    plat = [b for b in W["solid"]
            if b[0] <= X(x) <= b[2] and abs(F(b[3]) - want) < 0.05]
    check(f"ladder {i} keeps the platform it stands on", plat,
          f"x {x:.2f}: {'solid' if plat else 'HOLE'}")

# Nothing may roof the upper walkway: the handrail sits 0.64 m above it, a fighter is 0.91 m.
roof = [b for b in W["solid"] if 3.9 < F(b[1]) < 3.83 + BODY and F(b[2]) - F(b[0]) > 5]
check("no ceiling over the upper walkway", not roof,
      f"{len(roof)} long box(es) in the headroom")

# ---------------------------------------------------------------- spawns
print("\nSpawns")
for idx, (sx, sy, facing) in enumerate(SPAWNS):
    s = S(sx, sy)
    landed = until(s, 0, lambda s: s.mode == "ground", 180)
    surf = surface_under(sx, F(s.y) + 0.1)
    check(f"fighter {idx + 1} lands on the plateau", landed and F(s.y) > 0.9,
          f"spawn ({sx:.2f}, {sy:.2f}) -> y {F(s.y):.2f}, surface {surf}")
    check(f"fighter {idx + 1} spawns facing the middle",
          (facing > 0) == (sx < -1.89), f"facing {facing:+d}")

check("the two spawns are mirrored about the arena centre",
      abs((SPAWNS[0][0] + SPAWNS[1][0]) / 2 + 1.89) < 0.02,
      f"midpoint x {(SPAWNS[0][0] + SPAWNS[1][0]) / 2:.3f}")

# ---------------------------------------------------------------- ground floor
print("\nThe ground floor")
# The two crate stacks are 2 m tall and the step-up is 0.30 m, so crossing the arena means
# hopping up and over them. It has to be possible, and it has to cost both fighters the same.
times = []
for idx, (sx, sy, _) in enumerate(SPAWNS):
    target = SPAWNS[1 - idx][0]
    s = S(sx, sy)
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
check("the crossing costs both fighters the same",
      all(times) and abs(times[0] - times[1]) < 30,
      f"{times[0] / 60:.1f} s and {times[1] / 60:.1f} s" if all(times) else "one of them is stuck")

# ---------------------------------------------------------------- ladders
print("\nLadders")
LADDERS = sorted(W["ladder"], key=lambda b: b[0])
for idx, lad in enumerate(LADDERS):
    cx = (F(lad[0]) + F(lad[2])) / 2
    base = F(lad[1])
    s = S(cx + (0.9 if idx == 0 else -0.9), base + 0.2)
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
          any(b[0] <= X(cx) <= b[2] and abs(F(b[3]) - 3.83) < 0.02 for b in W["solid"]),
          f"solid over x {cx:.2f}")

    # And back down: standing on the ladder's top, pressing down steps over the edge onto it.
    caught = until(s, DN, lambda s: s.mode in ("ladder", "mount"), 120)
    check(f"ladder {idx}: pressing down on top of it steps onto the rungs", caught,
          f"mode {s.mode} at ({F(s.x):.2f}, {F(s.y):.2f})")
    check(f"ladder {idx}: carries a fighter back down",
          caught and until(s, DN, lambda s: F(s.y) <= base + 0.1, 600), f"y {F(s.y):.2f}")
    check(f"ladder {idx}: lets go at the bottom",
          until(s, DN, lambda s: s.mode == "ground", 120), f"mode {s.mode} y {F(s.y):.2f}")

# ---------------------------------------------------------------- the walkway gaps
print("\nThe gaps in the upper walkway")
tops = sorted((b for b in W["solid"] if abs(F(b[3]) - 3.83) < 0.02), key=lambda b: b[0])


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


def clear_run_up(brink, toward, segment):
    """The nearest spot on this segment with a clear run at the edge.

    The middle walkway has two 0.46 m crates standing on it and the step-up is 0.30 m, so a
    fighter who starts on the wrong side of one never reaches the edge at all. Searching for
    the run-up instead of assuming it is the difference between measuring the gap and
    measuring a crate. 0.6 m is already more than enough: ground acceleration is 40 m/s2,
    so run speed arrives in 0.11 m.
    """
    lo, hi = segment
    d = 0.6
    while d <= 3.2:
        x = brink - toward * d
        if x - HALF > lo + 0.02 and x + HALF < hi - 0.02:
            a, b = min(x, brink) - HALF, max(x, brink) + HALF
            clear = not any(b_[3] > X(3.84) and b_[1] < X(3.83 + BODY)
                            and b_[0] < X(b) and b_[2] > X(a)
                            for b_ in W["solid"])
            if clear:
                return x
        d += 0.1
    return None


def jump_gap(brink, toward, far_edge, segment, delay):
    """Run to the lip, leave it, and air-dodge across after `delay` frames.

    The dodge drops gravity for its 21 frames, so it adds its whole 1.68 m to the jump instead
    of arcing through it. That is the only thing in the kit that crosses these two gaps, and
    how late the dodge comes is what decides how far it carries.
    """
    start = clear_run_up(brink, toward, segment)
    if start is None:
        return None, False
    inp = R if toward > 0 else L
    s = S(start, 3.95)
    if not until(s, 0, lambda s: s.mode == "ground", 180):
        return None, False
    for _ in range(900):
        if (F(s.x) - brink) * toward > -(HALF + 0.02):
            break
        step(s, inp, W)
    step(s, inp | JMP, W)
    if delay is not None:
        hold(s, inp, delay)
        step(s, inp | DOD, W)
    until(s, inp, lambda s: s.mode == "ground" or F(s.y) < 2.0, 300)
    across = (F(s.x) - far_edge) * toward > 0 and s.mode == "ground" \
        and abs(F(s.y) - 3.83) < 0.06
    return s, across


for i, (a, b) in enumerate(gaps):
    width = b - a
    for toward, brink, far, seg, where in ((1, a, b, runs[i], "left to right"),
                                           (-1, b, a, runs[i + 1], "right to left")):
        window = [d for d in range(2, 40)
                  if jump_gap(brink, toward, far, seg, d)[1]]
        check(f"gap {i + 1} ({width:.2f} m) can be crossed {where}", window,
              f"dodge {window[0]}-{window[-1]} frames after the jump "
              f"({(window[-1] - window[0] + 1) / 60:.2f} s window)" if window
              else "no dodge timing clears it")
        _, across = jump_gap(brink, toward, far, seg, None)
        check(f"gap {i + 1} {where} is not free: a plain jump falls short", not across)

# ---------------------------------------------------------------- the edges
print("\nThe ends of the arena")
for idx, (toward, label) in enumerate(((-1, "left"), (1, "right"))):
    sx, sy, _ = SPAWNS[0 if toward < 0 else 1]
    s = S(sx, sy)
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
    blocked = [b for b in W["solid"]
               if b[0] < box[2] and b[2] > box[0] and b[1] < box[3] and b[3] > box[1] + X(0.02)]
    check(f"{name} is not buried in scenery", not blocked, f"{len(blocked)} overlapping solid(s)")
    # And a fighter walking along the surface reaches it, from whichever side is clear.
    reached, frm = False, None
    for side in (-1, 1):
        start = approach(cx, F(box[1]), side)
        if start is None:
            continue
        s = S(start, F(box[1]) + 0.12)
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
