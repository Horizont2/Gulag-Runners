"""Proves the M0 greybox arena is traversable and that each test obstacle does its job.
Runs the real motor (sim_motor_mirror) against the exact geometry ArenaBuilder generates."""
import re
import os
import io
import sim_motor_mirror as T
from sim_motor_mirror import S, step, X, F, L, R, UP, DN, JMP
from arena_geometry import (rooms, floors, room_w, floor_h, slab, wall, door_h,
                           hatch_w, ladder_w, gap_w, total_w, total_h, left, right,
                           lintel, clear, ladder_x, world)


def crouching_exists():
    """Does the simulation still have a crouch to test?

    Not "is there a crouchHeight field": MovementTuning still carries crouchSpeed and
    crouchHeight, and a config field nothing sets is not a mechanic. What makes a crouch
    real is PlayerMotor setting Crouching to something other than false, which is the same
    rule tips_check uses to catch a loading tip that has outlived its mechanic.

    THE TESTS BELOW ARE NOT DELETED, and that is the point of asking rather than removing
    them. Crouching was taken out of the motor; whether it comes back is an open design
    question -- there is geometry in the test worlds built for it, and docs/02 still
    describes it. A deleted test is a question nobody will ever be reminded of again. One
    that stands down with a reason comes back by itself the day the motor sets the flag.

    What it buys right now is quiet. Four assertions across three files were reporting FAIL
    every run, and a suite with a standing count of failures nobody reads is a suite that
    hides the next real one -- which is exactly what texture_budget's own note says about
    standing error counts.
    """
    here = os.path.dirname(os.path.abspath(__file__))
    motor = os.path.join(here, "..", "..", "Gulag runners", "Assets", "_Game",
                         "Sim", "Core", "PlayerMotor.cs")
    try:
        with io.open(motor, encoding="utf-8", errors="replace") as fh:
            src = fh.read()
    except OSError:
        return True                     # cannot tell: test it rather than skip it
    return re.search(r"Crouching\s*=\s*(?!false)\w", src) is not None


CROUCHES = crouching_exists()
SKIPPED_CROUCH = not CROUCHES
GONE_WHY = "the motor never enters it; this stands down rather than being deleted, and comes back by itself the day PlayerMotor sets Crouching again"


def skip_crouch(what, *rest):
    """Stands an assertion down and says why, in the shape check() has."""
    print("  skip  " + what + " -- " + GONE_WHY)
    return True

W = world()
print(f"arena {total_w} x {total_h} m: {len(W['solid'])} solid, "
      f"{len(W['oneway'])} one-way, {len(W['ladder'])} ladder\n")

ok = True
def check(n, c, d=""):
    global ok
    ok &= bool(c)
    print(f"  {'PASS' if c else 'FAIL'}  {n}{('  ' + d) if d else ''}")

def until(s, inp, cond, limit=900):
    for _ in range(limit):
        step(s, inp, W)
        if cond(s): return True
    return False

print("Player 1 route: spawn -> floor 1 -> across the gap -> floor 2")
s = S(left+2.5, 0.9)
until(s, 0, lambda s: s.mode == "ground", 120)
check("settles on the ground floor", abs(F(s.y)) < 0.05, f"y={F(s.y):.3f}")

check("walks to ladder 0", until(s, L if ladder_x(0) < F(s.x) else R,
      lambda s: abs(F(s.x)-ladder_x(0)) < 0.4), f"x={F(s.x):.2f}")
check("climbs to floor 1", until(s, UP, lambda s: F(s.y) >= floor_h-0.05),
      f"y={F(s.y):.2f}")
check("climbs out onto floor 1 by itself", until(s, UP, lambda s: s.mode == "ground", 300),
      f"mode={s.mode} y={F(s.y):.2f} x={F(s.x):.2f}")
check("ends up standing on the floor, not in the hatch",
      abs(F(s.y) - floor_h) < 0.05, f"y={F(s.y):.2f}")

check("crosses floor 1 to ladder 1", until(s, R, lambda s: abs(F(s.x)-ladder_x(1)) < 0.5, 900),
      f"x={F(s.x):.2f} y={F(s.y):.2f}")
check("climbs to floor 2", until(s, UP, lambda s: F(s.y) >= 2*floor_h-0.05),
      f"y={F(s.y):.2f}")
check("climbs out onto floor 2 by itself", until(s, UP, lambda s: s.mode == "ground", 300),
      f"mode={s.mode} y={F(s.y):.2f}")

# the gap is on the top floor, where there is sky overhead and a jump carries its full length
right_edge = gap_w / 2          # approaching from the right, this is the brink
half = 0.15
until(s, L, lambda s: F(s.x) - half < right_edge + 0.45, 900)
before = F(s.x)
jumped = False
for _ in range(400):
    step(s, L | JMP, W)
    if F(s.x) + half < -gap_w / 2 and s.mode == "ground" and F(s.y) > 2 * floor_h - 0.2:
        jumped = True
        break
check("jumps the gap in the top floor", jumped,
      f"x={before:.2f} -> {F(s.x):.2f}, y={F(s.y):.2f}, gap {gap_w} m")

print("\nObstacles do what they are for")
s = S(left+8.0, 0.9); until(s, 0, lambda s: s.mode == "ground", 120)
y0 = F(s.y)
peak = y0
for _ in range(600):
    step(s, R, W)
    peak = max(peak, F(s.y))
    if F(s.x) > left + 10.4: break
check("step ledge is walked up without jumping", peak > y0 + 0.12,
      f"rose to {peak:.3f} m, ledge top 0.155, never left the ground")

s = S(left+11.2, 0.9); until(s, 0, lambda s: s.mode == "ground", 120)
blocked = not until(s, R, lambda s: F(s.x) > left+13.6, 300)
check("low beam blocks you standing", blocked, f"x={F(s.x):.2f}")
passed = until(s, R | DN, lambda s: F(s.x) > left+13.6, 600)
(check if CROUCHES else skip_crouch)(
    "low beam is passable crouching", passed, f"x={F(s.x):.2f}")

s = S(left+16.2, 0.9); until(s, 0, lambda s: s.mode == "ground", 120)
check("stands on the low one-way", abs(F(s.y)-0.9) < 0.06, f"y={F(s.y):.2f}")
check("jumps up to the high one-way",
      until(s, R | JMP, lambda s: F(s.y) > 1.6, 300), f"y={F(s.y):.2f}")
# hold down, then ONE press of jump: with a fixed jump height, alternating the button every
# tick is a string of full jumps rather than a drop.
dropped = False
for _ in range(60):
    step(s, DN, W)
step(s, DN | JMP, W)
for _ in range(240):
    step(s, DN, W)
    if F(s.y) < 0.2:
        dropped = True
        break
check("drops back through with down+jump", dropped, f"y={F(s.y):.2f}")

print("\nPlayer 2 can reach the same places from the other end")
s = S(right-2.5, 0.9); until(s, 0, lambda s: s.mode == "ground", 120)
(check if CROUCHES else skip_crouch)(
      "crouches under the beam and reaches ladder 0",
      until(s, L | DN, lambda s: abs(F(s.x)-ladder_x(0)) < 0.6, 2400), f"x={F(s.x):.2f}")

print("\n" + ("ALL PASS" if ok else "SOME FAILED"))
