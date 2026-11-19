"""Do the closing walls herd, and never strand?

The closing phase is the one part of a match where the arena acts on the fighters rather
than the other way about, so it is the one part where a sign error does not look like a bug
— it looks like somebody lost. A wall that pushes the wrong way posts a fighter out into the
dead ground behind it and the match ends on the clock in an empty room.

Runs the real motor against a wall built the way ClosingWallSetup builds one.
"""
import sim_motor_mirror as M
from sim_motor_mirror import S, step, X, F, C, L, R

ok = True


def check(n, c, d=""):
    global ok
    ok &= bool(c)
    print(f"  {'PASS' if c else 'FAIL'}  {n}{('  ' + d) if d else ''}")


SPAN = (X(-5.0), X(5.0))          # deep enough for any lane in these tests
FLOOR = (X(-30.0), X(-1.0), X(30.0), X(0.0))


def world(progress, inner=4.0, edge=16.0):
    """Two walls finishing `inner` either side of nought, growing from the arena's edges."""
    left_face, right_face = -inner, inner
    return {
        "solid": [FLOOR], "solidz": [SPAN], "solidgate": [-1], "gate": [], "gatesshut": 0,
        "oneway": [], "onewayz": [], "ladder": [], "ladderz": [],
        "ladderlean": [], "ladderleanz": [],
        # Each wall is the FINISHED box: from the arena's edge to the last room's face. It
        # grows into that box from the edge, so nothing opens up behind it.
        "walls": [
            ((X(-edge), X(-4.0), X(left_face), X(8.0)), M.PLUS_X, SPAN),
            ((X(right_face), X(-4.0), X(edge), X(8.0)), M.MINUS_X, SPAN),
        ],
        "wallprogress": X(progress),
    }


print("A wall at nothing has no thickness at all")
w = world(0.0)
s = S(-12.0, 0.1)
s.depth = 0
for _ in range(120):
    step(s, 0, w)
check("a fighter stands where the wall has not reached", abs(F(s.x) + 12.0) < 0.05,
      f"x {F(s.x):.2f}")

print("\nA wall shut is a wall you cannot walk into")
w = world(1.0)
s = S(0.0, 0.1)
s.depth = 0
for _ in range(600):
    step(s, L, w)
check("walking left stops at the wall's face", F(s.x) > -4.3, f"x {F(s.x):.2f}")
s = S(0.0, 0.1)
s.depth = 0
for _ in range(600):
    step(s, R, w)
check("and right at the other", F(s.x) < 4.3, f"x {F(s.x):.2f}")

print("\nA wall closing herds, and herds INWARDS")
for start in (-12.0, -8.0, 9.0, 13.0):
    s = S(start, 0.1)
    s.depth = 0

    # The invariant that matters, and it holds from the first frame: the body is always
    # between the two walls' inner faces, wherever those faces are at that moment. Measuring
    # against the FINAL room instead would call the opening position a failure, which is how
    # a test ends up proving the thing it was written to catch cannot happen.
    worst, breached = 0.0, None
    for frame in range(601):
        w = world(min(1.0, frame / 600.0))
        step(s, 0, w)

        left = F(M.wall_at(w, 0)[2])
        right = F(M.wall_at(w, 1)[0])
        out = max(left - F(s.x), F(s.x) - right)
        if out > worst:
            worst, breached = out, frame

    check(f"a fighter left standing at x {start:+.0f} ends inside the last room",
          abs(F(s.x)) <= 4.4, f"ended at x {F(s.x):.2f}")
    check(f"  and is never caught on the far side of a wall at x {start:+.0f}",
          worst < 0.05,
          f"worst {worst * 100:.1f} cm past a face" +
          (f" on frame {breached}" if breached is not None else ""))

print("\nThe room really does get smaller")
widths = []
for p in (0.0, 0.5, 1.0):
    w = world(p)
    left = F(M.wall_at(w, 0)[2])
    right = F(M.wall_at(w, 1)[0])
    widths.append(right - left)
    print(f"    progress {p:.1f}: {left:7.2f} .. {right:6.2f}   {right - left:6.2f} m")
check("the gap shrinks the whole way", widths[0] > widths[1] > widths[2],
      f"{widths[0]:.1f} -> {widths[1]:.1f} -> {widths[2]:.1f} m")
check("and ends at the width it was asked for", abs(widths[2] - 8.0) < 0.05,
      f"{widths[2]:.2f} m")

print("\n" + ("ALL PASS" if ok else "SOME FAILED"))
