"""The camera over a jump: does it ever move faster than the man it is following?

THE REPORT: "the camera needs polishing -- at the highest point of a jump it makes a sharp
movement". It did, and the cause is worth keeping written down because nothing in the camera
is wrong on its own.

The vertical anchor parks while the body is in the air so the view does not bob through
every jump, and gives way only when the body gets far enough from it to risk leaving frame.
Separately, a COMMITTED FALL is tracked directly and led a little low, so the ground arrives
in frame before the player does, and it follows harder (0.07 s instead of 0.22).

The test for a committed fall was speed alone. An ordinary jump rises 2.05 m against 1.8 m
of slack, so at the apex the anchor has barely moved -- and a seventh of a second later the
body is doing 3 m/s downwards while still a metre and a half ABOVE the anchor. That counted
as a fall: the anchor was ASSIGNED a metre and a half lower in one frame, the follow time
dropped to a third of itself in the same frame, and the camera lurched at the one moment the
player is reading the arc.

So this walks a real jump -- the motor's own arc, from the mirror -- through a mirror of the
camera's vertical follow, and measures the one thing a camera must never do: move faster
than its subject. The arc is the truth; the camera is a window on it, and a window that
overtakes the thing it frames is the thing people call a jerk.

    python3 camera_check.py
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import sim_motor_mirror as m                              # noqa: E402
from sim_motor_mirror import S, step, X, F, C, JMP, R   # noqa: E402

CAM = os.path.join(HERE, "..", "..", "Gulag runners", "Assets", "_Game",
                   "Presentation", "SideViewCamera.cs")
SCENES = os.path.join(HERE, "..", "..", "Gulag runners", "Assets", "Scenes")

FPS = 60.0

# The camera as the arenas have it (Camera Player 1/2 in every scene agree).
VIEW_HEIGHT = 1.82 / 0.22        # bodyScreenFraction 0.22 of a 1.82 m body
VERTICAL_SMOOTH = 0.22
FALL_SMOOTH = 0.07
FALL_FOLLOW = 3.0
RISE_SLACK = 0.22
FALL_SLACK = 0.16
FALL_LOOKAHEAD = 0.14
DEAD_Y = 0.18
HARD_Y = 0.40
MAX_FALL = 14.0                  # the arenas' own maxFallSpeed
SMOOTH_EASE = 0.16               # seconds to change the follow time

# How much faster than the body the camera may be, as a share of the body's own speed. Some
# margin is honest: the camera is recentring as well as following. Half as fast again is
# not a lurch; three times is.
OVERTAKE = 1.5

# And a floor under it, because a camera moving 20 cm/s while the body is still is nothing
# anybody sees.
IGNORE_BELOW = 1.0               # m/s


def smooth_damp(current, target, velocity, time, dt):
    """UnityEngine.Mathf.SmoothDamp, the critically damped spring, exactly."""
    time = max(0.0001, time)
    omega = 2.0 / time
    x = omega * dt
    exp = 1.0 / (1.0 + x + 0.48 * x * x + 0.235 * x * x * x)
    change = current - target
    temp = (velocity + omega * change) * dt
    velocity = (velocity - omega * temp) * exp
    out = target + (change + temp) * exp
    # Unity's overshoot guard
    if (target - current > 0.0) == (out > target):
        out = target
        velocity = (out - target) / dt
    return out, velocity


class Camera:
    """The vertical half of SideViewCamera: anchor, dead zone, smoothing, hard edge."""

    def __init__(self, y, bounded):
        self.anchor = y
        self.focus = y
        self.v = 0.0
        self.fast = False
        self.smooth = VERTICAL_SMOOTH
        self.bounded = bounded          # the fix: an anchor that cannot teleport

    def anchored(self, y, vy, grounded, ground_y, dt):
        if grounded:
            self.fast = False
            self.anchor = ground_y
            return self.anchor

        rise = RISE_SLACK * VIEW_HEIGHT
        fall = FALL_SLACK * VIEW_HEIGHT

        committed = vy < -FALL_FOLLOW and (not self.bounded or y < self.anchor - fall)
        if committed:
            lead = (min(1.0, max(0.0, (-vy - FALL_FOLLOW) / max(0.1, MAX_FALL - FALL_FOLLOW)))
                    * FALL_LOOKAHEAD * VIEW_HEIGHT)
            self.fast = True
            return self.move(y - lead, vy, dt)

        self.fast = False
        if y > self.anchor + rise:
            return self.move(y - rise, vy, dt)
        if y < self.anchor - fall:
            return self.move(y + fall, vy, dt)
        return self.anchor

    def move(self, want, vy, dt):
        if not self.bounded:
            self.anchor = want                      # what it used to do: an assignment
            return self.anchor
        speed = abs(vy)
        if speed < 0.01:
            return self.anchor
        step_to = speed * dt
        if abs(want - self.anchor) <= step_to:
            self.anchor = want
        else:
            self.anchor += step_to if want > self.anchor else -step_to
        return self.anchor

    def tick(self, y, vy, grounded, ground_y, dt):
        aim = self.anchored(y, vy, grounded, ground_y, dt)

        want = FALL_SMOOTH if self.fast else VERTICAL_SMOOTH
        if self.bounded:
            span = max(0.01, abs(VERTICAL_SMOOTH - FALL_SMOOTH))
            limit = span / SMOOTH_EASE * dt
            if abs(want - self.smooth) <= limit:
                self.smooth = want
            else:
                self.smooth += limit if want > self.smooth else -limit
        else:
            self.smooth = want                      # switched between two frames

        dead = HARD_Y * 0.0 + (VIEW_HEIGHT * 0.5) * DEAD_Y
        offset = aim - self.focus
        soft = self.focus if abs(offset) <= dead else aim - (dead if offset > 0 else -dead)

        self.focus, self.v = smooth_damp(self.focus, soft, self.v, self.smooth, dt)

        hard = (VIEW_HEIGHT * 0.5) * HARD_Y
        self.focus = min(max(self.focus, aim - hard), aim + hard)
        return self.focus


def arc(frames=150):
    """One jump on the real motor, on flat ground, with the arenas' own tuning.

    Every arena overrides gravity (22 instead of 28) and maxFallSpeed (14 instead of 18) on
    its MatchState, and both change the shape of the arc this is about: 22 makes the jump
    2.05 m, which is the figure the slack above the anchor is tuned against.
    """
    C["GRAV"], C["MAXFALL"] = X(22.0), X(14.0)
    w = {"solid": [(X(-20.0), X(-1.0), X(20.0), X(0.0))],
         "solidz": [(X(-3.0), X(3.0))], "oneway": [], "onewayz": [],
         "ladder": [], "ladderz": [], "ladderlean": [], "ladderleanz": []}
    st = S(0.0, 0.0)
    st.mode = "ground"
    st.depth = 0
    out = []
    for t in range(frames):
        step(st, JMP if 4 <= t <= 10 else 0, w)
        out.append((F(st.y), F(st.vy), st.mode == "ground"))
    return out


def off_a_ledge(frames=180):
    """Walk off a six-metre ledge. A fall still has to be FOLLOWED: the whole reason the
    camera has a committed-fall mode is that the ground should arrive in frame before the
    player does, and a fix for the apex that quietly stops falls being tracked would be a
    worse camera than the one that lurched."""
    C["GRAV"], C["MAXFALL"] = X(22.0), X(14.0)
    w = {"solid": [(X(-20.0), X(-1.0), X(0.0), X(6.0)),       # the ledge
                   (X(-20.0), X(-1.0), X(20.0), X(0.0))],     # the ground below
         "solidz": [(X(-3.0), X(3.0)), (X(-3.0), X(3.0))],
         "oneway": [], "onewayz": [],
         "ladder": [], "ladderz": [], "ladderlean": [], "ladderleanz": []}
    st = S(-1.0, 6.0)
    st.mode = "ground"
    st.depth = 0
    out = []
    for t in range(frames):
        step(st, R, w)
        out.append((F(st.y), F(st.vy), st.mode == "ground"))
    return out


def follows_a_fall(bounded):
    """How far the body gets from the middle of the frame on the way down, in view heights,
    and whether the camera ever overtakes it."""
    path = off_a_ledge()
    dt = 1.0 / FPS
    cam = Camera(path[0][0] + 0.9, bounded)
    # Let the camera settle where it starts. Its first frames are a recentre from wherever
    # it was placed, which is nothing to do with following anybody.
    for _ in range(40):
        cam.tick(path[0][0] + 0.9, 0.0, True, path[0][0] + 0.9, dt)

    off, worst_ratio = 0.0, 0.0
    previous = cam.focus
    for y, vy, grounded in path:
        focus = cam.tick(y + 0.9, vy, grounded, y + 0.9, dt)
        if not grounded:
            off = max(off, abs((y + 0.9) - focus) / VIEW_HEIGHT)
            worst_ratio = max(worst_ratio,
                              (abs(focus - previous) / dt) / max(abs(vy), IGNORE_BELOW))
        previous = focus
    return off, worst_ratio


def worst(bounded):
    """The fastest the camera moves against the fastest the body is moving, frame by frame."""
    path = arc()
    dt = 1.0 / FPS
    cam = Camera(path[0][0] + 0.9, bounded)
    for _ in range(40):
        cam.tick(path[0][0] + 0.9, 0.0, True, path[0][0] + 0.9, dt)

    worst_ratio, where = 0.0, None
    previous = cam.focus
    for t, (y, vy, grounded) in enumerate(path):
        focus = cam.tick(y + 0.9, vy, grounded, y + 0.9, dt)
        cam_speed = abs(focus - previous) / dt
        previous = focus
        body_speed = max(abs(vy), IGNORE_BELOW)
        ratio = cam_speed / body_speed
        if ratio > worst_ratio:
            worst_ratio, where = ratio, (t, y, vy, cam_speed, body_speed)
    return worst_ratio, where


def main():
    # The camera's own numbers have to be the ones measured, or this measures a fiction.
    text = open(CAM, encoding="utf-8").read()
    problems = []
    for name, value in (("verticalSmoothTime", VERTICAL_SMOOTH), ("fallSmoothTime", FALL_SMOOTH),
                        ("fallFollowSpeed", FALL_FOLLOW),
                        ("airRiseSlackFraction", RISE_SLACK),
                        ("airFallSlackFraction", FALL_SLACK),
                        ("fallLookAheadFraction", FALL_LOOKAHEAD)):
        if f"{name} = {value}f" not in text and f"{name} = {value:g}f" not in text:
            print(f"  note: SideViewCamera.{name} is no longer {value:g}; this check's copy "
                  f"of the numbers is stale, and what it measures is a camera nobody has.")

    def say(label, ratio, where):
        if where is None:
            print(f"  {label}: the camera never moves at all over the jump")
            return
        print(f"  {label}: worst frame {ratio:.2f}x the body's own speed (tick {where[0]}, "
              f"body y {where[1]:+.2f} at {where[2]:+.2f} m/s, camera {where[3]:.2f} m/s)")

    now, where = worst(bounded=True)
    say("as it is", now, where)

    was, where_was = worst(bounded=False)
    say("with the anchor assigned instead of moved, as it was", was, where_was)

    off, chase = follows_a_fall(bounded=True)
    print(f"  off a six-metre ledge the body never gets further than {off * 100:.0f}% of a "
          f"view height from the middle of the frame, and the camera's worst frame is "
          f"{chase:.2f}x its speed")
    if off > HARD_Y:
        problems.append(
            f"a fall off a ledge puts the body {off * 100:.0f}% of a view height from the "
            f"middle of the frame, past the hard edge at {HARD_Y * 100:.0f}%. The camera has "
            f"stopped following falls -- which is what the committed-fall mode is FOR.")
    if chase > OVERTAKE:
        problems.append(
            f"on the way down the camera moves {chase:.1f}x faster than the falling body.")

    if now > OVERTAKE and where is not None:
        problems.append(
            f"over an ordinary jump the camera moves {now:.1f}x faster than the body it is "
            f"following (tick {where[0]}, the body at {where[2]:+.1f} m/s, the camera at "
            f"{where[3]:.1f} m/s). That is the sharp movement at the top of a jump: a window "
            f"that overtakes what it frames. The anchor must be MOVED at the body's own "
            f"speed, never assigned, and the follow time eased rather than switched.")

    for p in problems:
        print("  " + p)
    print(f"camera_check: {len(problems)} problem(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
