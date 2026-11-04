"""Does the round's opening shot show the other player?

docs/01 spends the first twenty seconds of every match on not knowing where the other
runner is. The opening cinematic is the one moment the game has a free camera, and so it
is the one moment it can give that away for nothing -- and it would do it silently,
because an intro is composed in the editor at one spawn, on one aspect ratio, and the
thing it reveals is at the far end of the arena where nobody is looking while they tune
it.

So this is not a check about taste. It rebuilds the real shot -- SideViewCamera's framing
maths, then RoundIntroCamera on top of it, the same arithmetic in the same order -- and
asks the only question that cannot be left to the eye: across every frame of the opening,
on the widest phone anybody will play this on, is the OTHER fighter inside the frustum?

It also measures the thing the shot was rewritten for. The cage-door opening shoves the
camera twice, which is a camera answering somebody hauling a grille up; in an arena with
no grille those shoves are a flinch at nothing. Peak acceleration is what that is, in a
number, so "not jerky" can be compared between two arenas rather than argued about.

    python3 intro_check.py
"""
import math
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import arena_demo_geometry as geometry           # noqa: E402

SCENES = os.path.join(HERE, "..", "..", "Gulag runners", "Assets", "Scenes")

SIDE_VIEW = "b921a9be9303a1887f282de341a5ae31"
ROUND_INTRO = "4e8b15c0d7a2496fb3e09c742a5d1f68"

# The widest phone worth worrying about. Wider frame, more arena in it, so this is the
# worst case rather than a typical one: 16:9 and 19.5:9 are both narrower.
ASPECT = 21.0 / 9.0

# Frames of the shot to test. The cage phase is 3 s, so this is a shade over 80 fps --
# finer than any device will actually sample it.
STEPS = 260

# How far above the floor the camera must stay. Less and the shot is inside the geometry
# for a frame, which on a phone reads as the screen glitching.
FLOOR_CLEARANCE = 0.25

# What "jerky" actually is, and the one measurement here worth explaining.
#
# It is NOT a large acceleration. A crane that covers six metres in two seconds pulls
# several m/s^2 the whole way and looks like a camera on an arm, because the pull arrives
# and leaves gradually. What the eye reads as a jerk is a DISCONTINUITY -- velocity
# changing in no time at all, which is an acceleration spike of unbounded size.
#
# Which is awkward to assert on, because sampling a spike gives whatever the frame rate
# gives. So the test is scale-free instead: measure the peak twice, at one rate and at
# double it. A smooth curve gives the same answer both times (ratio ~1). A velocity step
# gives twice the answer at twice the rate (ratio ~2), because the same step is crossed
# in half the time. The ratio says which of the two it is, whatever the numbers are.
STEP_RATIO = 1.25

# And a bound on the smooth case, so a move that is continuous but still violent is
# caught. Generous: a crane descent peaks around 6.
VIOLENT = 12.0


# ---------------------------------------------------------------- small vector helpers

def sub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def add(a, b):
    return (a[0] + b[0], a[1] + b[1], a[2] + b[2])


def mul(a, s):
    return (a[0] * s, a[1] * s, a[2] * s)


def dot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1],
            a[2] * b[0] - a[0] * b[2],
            a[0] * b[1] - a[1] * b[0])


def length(a):
    return math.sqrt(dot(a, a))


def norm(a):
    n = length(a)
    return (0.0, 0.0, 1.0) if n < 1e-9 else mul(a, 1.0 / n)


# ------------------------------------------------------------------------- quaternions
# Mirrored from Unity rather than borrowed from anywhere: the shot slerps between two
# rotations, and a check that interpolated them some other way would be checking a
# different shot.

def q_mul_v(q, v):
    x, y, z, w = q
    t = mul(cross((x, y, z), v), 2.0)
    return add(add(v, mul(t, w)), cross((x, y, z), t))


def q_euler_x(degrees):
    h = math.radians(degrees) * 0.5
    return (math.sin(h), 0.0, 0.0, math.cos(h))


def q_axis_angle(axis, degrees):
    a = norm(axis)
    h = math.radians(degrees) * 0.5
    s = math.sin(h)
    return (a[0] * s, a[1] * s, a[2] * s, math.cos(h))


def q_look(forward, up=(0.0, 1.0, 0.0)):
    """Unity's LookRotation: +Z down `forward`, +Y as close to `up` as it can be."""
    f = norm(forward)
    r = cross(up, f)
    if length(r) < 1e-6:
        r = cross((0.0, 0.0, 1.0), f)
    r = norm(r)
    u = cross(f, r)

    # Rotation matrix (columns r, u, f) to quaternion, branch by largest diagonal.
    m = ((r[0], u[0], f[0]),
         (r[1], u[1], f[1]),
         (r[2], u[2], f[2]))
    trace = m[0][0] + m[1][1] + m[2][2]
    if trace > 0.0:
        s = math.sqrt(trace + 1.0) * 2.0
        return ((m[2][1] - m[1][2]) / s, (m[0][2] - m[2][0]) / s,
                (m[1][0] - m[0][1]) / s, s * 0.25)
    if m[0][0] > m[1][1] and m[0][0] > m[2][2]:
        s = math.sqrt(1.0 + m[0][0] - m[1][1] - m[2][2]) * 2.0
        return (s * 0.25, (m[0][1] + m[1][0]) / s,
                (m[0][2] + m[2][0]) / s, (m[2][1] - m[1][2]) / s)
    if m[1][1] > m[2][2]:
        s = math.sqrt(1.0 + m[1][1] - m[0][0] - m[2][2]) * 2.0
        return ((m[0][1] + m[1][0]) / s, s * 0.25,
                (m[1][2] + m[2][1]) / s, (m[0][2] - m[2][0]) / s)
    s = math.sqrt(1.0 + m[2][2] - m[0][0] - m[1][1]) * 2.0
    return ((m[0][2] + m[2][0]) / s, (m[1][2] + m[2][1]) / s,
            s * 0.25, (m[1][0] - m[0][1]) / s)


def q_slerp(a, b, k):
    k = min(1.0, max(0.0, k))
    d = sum(x * y for x, y in zip(a, b))
    if d < 0.0:
        b = tuple(-x for x in b)
        d = -d
    if d > 0.9995:
        out = tuple(x + (y - x) * k for x, y in zip(a, b))
    else:
        theta = math.acos(max(-1.0, min(1.0, d)))
        s = math.sin(theta)
        wa, wb = math.sin((1.0 - k) * theta) / s, math.sin(k * theta) / s
        out = tuple(x * wa + y * wb for x, y in zip(a, b))
    n = math.sqrt(sum(x * x for x in out))
    return tuple(x / n for x in out)


# ------------------------------------------------------------------------- easing, 1:1

def smooth(k):
    k = min(1.0, max(0.0, k))
    return k * k * (3.0 - 2.0 * k)


def smoother(k):
    k = min(1.0, max(0.0, k))
    return k * k * k * (k * (6.0 * k - 15.0) + 10.0)


def inverse_lerp(a, b, v):
    if abs(b - a) < 1e-9:
        return 0.0
    return min(1.0, max(0.0, (v - a) / (b - a)))


def shove(t, at, span):
    k = inverse_lerp(at, at + span, t)
    return math.sin(k * math.pi) if 0.0 < k < 1.0 else 0.0


# ---------------------------------------------------------------------- reading a scene

def fields(body):
    out = {}
    for line in body.splitlines():
        m = re.match(r"^  (\w+): (.+?)\s*$", line)
        if m:
            out[m.group(1)] = m.group(2)
    return out


def number(d, name, default=0.0):
    try:
        return float(d.get(name, default))
    except (TypeError, ValueError):
        return default


def components(text, guid):
    """Every MonoBehaviour of one script, as (fileID, fields)."""
    out = []
    for m in re.finditer(r"--- !u!114 &(-?\d+)\nMonoBehaviour:\n(.*?)(?=\n--- !u!|\Z)",
                         text, re.S):
        if guid in m.group(2):
            out.append((int(m.group(1)), fields(m.group(2))))
    return out


def cameras(text):
    """The side-view cameras, each with the intro component that sits on top of it."""
    intros = {}
    for _id, f in components(text, ROUND_INTRO):
        view = re.search(r"fileID: (-?\d+)", f.get("view", "")) if f.get("view") else None
        if view:
            intros[int(view.group(1))] = f

    out = []
    for _id, f in components(text, SIDE_VIEW):
        out.append((_id, f, intros.get(_id)))
    return out


# ------------------------------------------------------------------------------ the shot

def playing(spawn, view, body_height):
    """Where SideViewCamera puts itself, and where it is looking, at a spawn."""
    x, y, _facing, plane_z = spawn

    fraction = number(view, "bodyScreenFraction", 0.18)
    framing = number(view, "framingBodyHeight", 0.0)
    height = framing if framing > 0.01 else body_height
    view_height = (number(view, "visibleWorldHeight", 10.0)
                   if view.get("manualFraming") == "1"
                   else height / max(0.02, fraction))

    fov = number(view, "fieldOfView", 30.0)
    pitch = number(view, "pitchDegrees", 12.0)
    half_fov = math.radians(fov) * 0.5
    distance = (view_height * 0.5 * math.cos(math.radians(pitch))
                / math.tan(half_fov))

    aim = height * number(view, "aimHeightBodies", 0.6)
    focus = (x, y + aim, number(view, "planeZ", plane_z))

    rot = q_euler_x(pitch)
    at = sub(focus, mul(q_mul_v(rot, (0.0, 0.0, 1.0)), distance))
    return at, rot, view_height, fov


def shot(t, at, rot, intro, pivot, facing):
    """RoundIntroCamera's answer for one frame. Mirrors LateUpdate, in its order."""
    crane = intro.get("style") == "1"

    hold = number(intro, "craneHold", 0.14) if crane else number(intro, "holdUntil", 0.52)
    k = inverse_lerp(hold, number(intro, "settledBy", 0.86), t)
    close = 1.0 - (smoother(k) if crane else smooth(k))

    r = 0.0 if crane else inverse_lerp(number(intro, "settledBy", 0.86), 1.0, t)
    if r > 0.0:
        bump = math.sin(r * math.pi)
        close -= number(intro, "release", 0.12) * bump * bump

    forward = q_mul_v(rot, (0.0, 0.0, 1.0))

    if crane:
        focus = pivot
        arm = sub(at, focus)
        if dot(arm, arm) < 4.0:
            arm = mul(forward, -8.0)
            focus = sub(at, arm)

        axis = cross(arm, (0.0, 1.0, 0.0))
        axis = norm(axis) if dot(axis, axis) > 1e-6 else (1.0, 0.0, 0.0)

        at = add(focus, q_mul_v(q_axis_angle(axis, number(intro, "craneAngle", 42.0) * close),
                                arm))
        at = add(at, (-number(intro, "craneLead", 1.4) * facing * close, 0.0, 0.0))
        rot = q_slerp(rot, q_look(sub(focus, at)), close)
    else:
        # The fallback framing offset. Neither arena authors an openingShot, and a shot
        # that IS authored is a transform nobody can check from the file, so this is the
        # branch worth measuring.
        distance = length(sub(at, pivot))
        at = add(at, mul(forward, distance * (1.0 - number(intro, "startDistance", 0.32)) * close))
        at = add(at, (0.0, number(intro, "startHeight", -0.35) * close, 0.0))
        at = add(at, (number(intro, "startLead", 0.0) * facing * close, 0.0, 0.0))

    # Sway is Perlin noise on a clock, so it cannot be reproduced here; it is bounded by
    # `sway` metres and is 0.035 in both arenas, which is below everything measured below.

    if not crane:
        push = q_mul_v(rot, (0.0, 0.0, 1.0))
        jerk = number(intro, "jerkPush", 0.22)
        span = number(intro, "jerkFor", 0.1)
        at = add(at, mul(push, jerk * shove(t, number(intro, "jerkAt", 0.14), span) * close))
        at = add(at, mul(push, jerk * 1.3 * shove(t, number(intro, "jerkAt2", 0.4), span) * close))

    return at, rot, close


def inside(at, rot, fov, point):
    """Is a world point in frame? Camera space, then the viewport, then in front."""
    d = sub(point, at)
    # Inverse of the rotation: conjugate, since it is a unit quaternion.
    x, y, z, w = rot
    local = q_mul_v((-x, -y, -z, w), d)
    if local[2] <= 0.05:
        return False
    half = math.tan(math.radians(fov) * 0.5)
    return (abs(local[1] / local[2]) <= half
            and abs(local[0] / local[2]) <= half * ASPECT)


def main(argv):
    wanted = argv or geometry.arena_scenes()
    problems, notes = [], []

    for name in wanted:
        path = os.path.join(SCENES, name)
        text = open(path, encoding="utf-8", errors="ignore").read()
        scene = geometry.Scene(path)
        spawns = geometry.spawns(scene)

        found = cameras(text)
        if not found:
            notes.append(f"{name}: no side-view camera in it yet")
            continue
        if len(spawns) < 2:
            notes.append(f"{name}: {len(spawns)} spawn(s), so there is no opponent to hide")
            continue

        cage = 3.0
        m = re.search(r"^  cageSeconds: (.+?)$", text, re.M)
        if m:
            cage = float(m.group(1))

        notes.append(f"{name}: {len(found)} camera(s), {cage:.1f} s of cage phase")

        for i, (_id, view, intro) in enumerate(found):
            if intro is None:
                notes.append(f"  camera {i + 1}: no opening shot on it")
                continue

            mine = spawns[min(i, len(spawns) - 1)]
            theirs = spawns[1 - min(i, 1)]
            facing = mine[2]
            crane = intro.get("style") == "1"
            label = f"  camera {i + 1} ({'crane' if crane else 'cage door'})"

            at0, rot0, view_height, fov = playing(mine, view, 0.91)
            pivot = (mine[0], mine[1], mine[3])

            # The other fighter, head and chest. Feet are the easiest thing to have in
            # frame harmlessly and the hardest thing to recognise, so the test is the
            # part of them that gives the position away.
            them = [(theirs[0], theirs[1] + h, theirs[3]) for h in (0.45, 0.91)]

            def run(steps):
                """The whole shot at one sample rate: path, peak speed, peak acceleration."""
                xyz, worst_t, low = [], None, None
                for step in range(steps + 1):
                    t = step / steps
                    at, rot, _close = shot(t, at0, rot0, intro, pivot, facing)
                    xyz.append(at)
                    if low is None or at[1] < low:
                        low = at[1]
                    if worst_t is None and any(inside(at, rot, fov, p) for p in them):
                        worst_t = t

                dt = cage / steps
                speeds = [length(sub(xyz[j + 1], xyz[j])) / dt for j in range(len(xyz) - 1)]
                accel = [abs(speeds[j + 1] - speeds[j]) / dt for j in range(len(speeds) - 1)]
                return (xyz, worst_t, low,
                        max(speeds) if speeds else 0.0,
                        max(accel) if accel else 0.0)

            path_xyz, seen, lowest, peak_v, peak_a = run(STEPS)
            _x, _s, _l, _v, finer_a = run(STEPS * 2)
            ratio = finer_a / peak_a if peak_a > 0.01 else 1.0

            landed = length(sub(path_xyz[-1], at0))

            notes.append(f"{label}: starts {length(sub(path_xyz[0], at0)):.2f} m from the "
                         f"playing framing, peaks at {peak_v:.2f} m/s and "
                         f"{peak_a:.1f} m/s^2 (x{ratio:.2f} at double the rate), "
                         f"lands {landed * 100:.1f} cm off it")

            if seen is not None:
                problems.append(
                    f"{name}{label}: the other fighter is in frame {seen * cage:.2f} s "
                    f"into the opening. docs/01 is built on not knowing where they are, "
                    f"and this hands it over before the fight starts.")

            if crane and ratio > STEP_RATIO:
                problems.append(
                    f"{name}{label}: the peak acceleration grows x{ratio:.2f} when the "
                    f"shot is sampled twice as finely, which means the camera's SPEED "
                    f"steps somewhere rather than easing — a jerk, at whatever frame rate "
                    f"the device runs. A crane that snaps is what this style exists to "
                    f"stop doing.")

            if crane and peak_a > VIOLENT:
                problems.append(
                    f"{name}{label}: peaks at {peak_a:.1f} m/s^2. Smooth, but that is a "
                    f"camera being thrown rather than boomed.")

            if landed > 0.02:
                problems.append(
                    f"{name}{label}: ends {landed * 100:.1f} cm away from the playing "
                    f"framing, so the frame the player takes control on is not the frame "
                    f"the shot arrived at. That jump is the hand-over showing.")

            floor = mine[1]
            if lowest < floor + FLOOR_CLEARANCE:
                problems.append(
                    f"{name}{label}: drops to y {lowest:+.2f}, which is "
                    f"{floor + FLOOR_CLEARANCE - lowest:.2f} m into the floor at "
                    f"y {floor:+.2f}.")

    for n in notes:
        print("  note: " + n if not n.startswith("  ") else "  " + n)
    for p in problems:
        print("  " + p)
    print(f"intro_check: {len(problems)} problem(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
