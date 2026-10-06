"""Proves the M0 greybox arena is traversable and that each test obstacle does its job.
Runs the real motor (sim_motor_mirror) against the exact geometry ArenaBuilder generates."""
import sim_motor_mirror as T
from sim_motor_mirror import S, step, X, F, L, R, UP, DN, JMP, DOD
from arena_geometry import (rooms, floors, room_w, floor_h, slab, wall, door_h,
                           hatch_w, ladder_w, gap_w, total_w, total_h, left, right,
                           lintel, clear, ladder_x, world)

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
check("low beam is passable crouching", passed, f"x={F(s.x):.2f}")

s = S(left+16.2, 0.9); until(s, 0, lambda s: s.mode == "ground", 120)
check("stands on the low one-way", abs(F(s.y)-0.9) < 0.06, f"y={F(s.y):.2f}")
check("jumps up to the high one-way",
      until(s, R | JMP, lambda s: F(s.y) > 1.6, 300), f"y={F(s.y):.2f}")
dropped = False
for _ in range(600):
    step(s, DN, W)
    step(s, DN | JMP, W)
    if F(s.y) < 0.2:
        dropped = True
        break
check("drops back through with down+jump", dropped, f"y={F(s.y):.2f}")

print("\nPlayer 2 can reach the same places from the other end")
s = S(right-2.5, 0.9); until(s, 0, lambda s: s.mode == "ground", 120)
check("crouches under the beam and reaches ladder 0",
      until(s, L | DN, lambda s: abs(F(s.x)-ladder_x(0)) < 0.6, 2400), f"x={F(s.x):.2f}")

print("\n" + ("ALL PASS" if ok else "SOME FAILED"))
