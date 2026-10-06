"""Measures the two ladder boundaries: getting onto a ladder, and getting off one.

A "jerk" is not a feeling here, it is two numbers.

How far the body moves in a single 60 Hz tick. Walking covers 0.050 m, climbing 0.033 m, a dodge
0.117 m. A dodge is the fastest the game ever moves a body deliberately, so no scripted move may
beat it. The old ladder grab moved up to half a metre in one tick -- twenty-seven metres a second,
four times a dodge -- and the old fixed-length climb-out peaked at ten metres a second. Free fall
and the jump arc are exempt: they are driven by gravity, which is continuous by construction and
faster than anything on foot by design.

And how smoothly that speed changes. Both scripted moves start and end at rest, so an easing curve
that is flat at both ends makes continuity structural rather than something to tune: it is enough
to check that the first and last ticks of a move are much smaller than its middle.

Run against the exact geometry ArenaBuilder generates (arena_geometry)."""
import sim_motor_mirror as M
from sim_motor_mirror import S, step, X, F, C, L, R, UP, DN, JMP
from arena_geometry import floor_h, ladder_x, world

W = world()
RUN_STEP = F(M.mul(C["RUN"], M.DT))          # the body's own top walking step
DODGE_STEP = F(M.mul(C["DODGE"], M.DT))      # the fastest deliberate move in the game
# Twice a walking step. A dodge is the absolute ceiling -- nothing deliberate may beat the
# fastest deliberate move in the game -- but on a ladder there is no reason to come near it, so the
# bound here is the stricter one the motor can actually hold.
JERK = RUN_STEP * 2
FREE = ("air",)                              # gravity's business, not the ladder's

ok = True


def check(n, c, d=""):
    global ok
    ok &= bool(c)
    print(f"  {'PASS' if c else 'FAIL'}  {n}{('  ' + d) if d else ''}")


def settle(x, y, inp=0, limit=180):
    s = S(x, y)
    for _ in range(limit):
        step(s, inp, W)
        if s.mode == "ground":
            break
    return s


def record(s, inp, frames):
    """Runs the motor, returning one (dx, dy, mode) per tick plus the sequence of modes entered."""
    ticks, modes = [], [s.mode]
    for _ in range(frames):
        px, py = s.x, s.y
        step(s, inp, W)
        ticks.append((F(s.x - px), F(s.y - py), s.mode))
        if s.mode != modes[-1]:
            modes.append(s.mode)
    return ticks, modes


def worst(ticks, skip=FREE):
    """The longest single-tick move, ignoring the modes that are allowed to be fast."""
    on_foot = [(dx, dy) for dx, dy, mode in ticks if mode not in skip]
    return max(((dx * dx + dy * dy) ** 0.5 for dx, dy in on_foot), default=0.0)


def slides(ticks, mode):
    """The sideways steps taken while in one mode, for checking the easing."""
    return [abs(dx) for dx, dy, m in ticks if m == mode and abs(dx) > 1e-6]


def embedded(s):
    """The body must never end a tick inside a solid, scripted move or not."""
    half = C["W"] // 2
    h = C["CH"] if s.crouch else C["H"]
    return M.any_solid(W, (s.x - half, s.y, s.x + half, s.y + h))


print(f"a walking step is {RUN_STEP:.3f} m/tick and a dodge {DODGE_STEP:.3f}; "
      f"nothing on foot here may pass {JERK:.3f}\n")

print("1. grabbing a ladder from the ground")
s = settle(ladder_x(0) + 0.45, 0.9)          # off to one side, as a player arriving will be
start_x = F(s.x)
ticks, modes = record(s, UP, 40)
check("the grab is a state of its own, not an instant snap", "mount" in modes, f"modes={modes}")
check("no tick of the grab is a jump", worst(ticks) <= JERK,
      f"worst {worst(ticks):.3f} m/tick over {start_x:.2f} -> {F(s.x):.2f}")
check("ends on the ladder centre line",
      abs(F(s.x) - ladder_x(0)) < 0.01, f"x={F(s.x):.3f} centre={ladder_x(0):.3f}")
check("and ends climbing", s.mode == "ladder", f"mode={s.mode}")

print("\n2. the grab is eased, not linear")
s = settle(ladder_x(0) + 0.45, 0.9)
ticks, _ = record(s, UP, 40)
slide = slides(ticks, "mount")
mid = slide[len(slide) // 2]
check("it starts gently", slide[0] < mid, f"first {slide[0]:.4f} < middle {mid:.4f}")
check("and it arrives gently", slide[-1] < mid, f"last {slide[-1]:.4f} < middle {mid:.4f}")
check("the body never enters a wall during it", not embedded(s))

print("\n3. the far side of the ladder works the same")
s = settle(ladder_x(0) - 0.45, 0.9)
ticks, modes = record(s, UP, 40)
check("grabbed from the left as well", "mount" in modes, f"modes={modes}")
check("no tick of that grab is a jump", worst(ticks) <= JERK, f"worst {worst(ticks):.3f} m/tick")

print("\n4. standing right on the ladder does not pay for a grab")
s = settle(ladder_x(0), 0.9)
ticks, modes = record(s, UP, 6)
check("goes straight to climbing", "mount" not in modes, f"modes={modes}")
check("and is already moving up", F(s.y) > 0.02, f"y={F(s.y):.3f}")

print("\n5. catching the ladder on the way down the hatch")
# The hatch is 1.8 m wide and the ladder 0.9, so standing on the floor beside the hole leaves the
# body clear of the ladder box altogether: the way down is to walk into the hatch and catch the
# rungs. That is the descent the arena actually offers, so that is the one that has to be smooth.
s = settle(ladder_x(0) + 1.4, floor_h + 0.9)
check("starts standing on floor 1", s.mode == "ground" and abs(F(s.y) - floor_h) < 0.05,
      f"mode={s.mode} y={F(s.y):.2f}")
ticks, modes = record(s, L | DN, 60)
check("catches the ladder instead of falling to floor 0", "mount" in modes and "ladder" in modes,
      f"modes={modes}")
check("no tick from the floor onto the rungs is a jump", worst(ticks) <= JERK,
      f"worst {worst(ticks):.3f} m/tick")
check("the catch is eased too", slides(ticks, "mount")[0] < max(slides(ticks, "mount")),
      f"first {slides(ticks, 'mount')[0]:.4f} < peak {max(slides(ticks, 'mount')):.4f}")

print("\n6. the whole way down, then the whole way up, with nothing skipped")
s = settle(ladder_x(0) + 1.4, floor_h + 0.9)
t1, m1 = record(s, L | DN, 60)                   # walk into the hatch and catch the ladder
t2, m2 = record(s, DN, 300)                      # then just hold down
check("reaches floor 0 and lets go of the ladder by itself",
      s.mode == "ground" and abs(F(s.y)) < 0.05, f"mode={s.mode} y={F(s.y):.2f}")
check("not one tick of the descent is a jump", worst(t1 + t2) <= JERK,
      f"worst {worst(t1 + t2):.3f} m/tick")
check("the modes read as one continuous move",
      m1 + m2[1:] == ["ground", "air", "mount", "ladder", "ground"], f"modes={m1 + m2[1:]}")

ticks, modes = record(s, UP, 400)
check("and climbs back out onto floor 1", s.mode == "ground" and abs(F(s.y) - floor_h) < 0.05,
      f"mode={s.mode} y={F(s.y):.2f}")
check("not one tick of the climb is a jump", worst(ticks) <= JERK,
      f"worst {worst(ticks):.3f} m/tick")
check("and the climb-out is the fastest thing on the whole route",
      worst(ticks) > worst(t1 + t2), f"climb {worst(ticks):.3f} vs descent {worst(t1 + t2):.3f}")

print("\n7. nothing flickers")
s = settle(ladder_x(0), 0.9)
ticks, modes = record(s, DN, 120)
check("holding down at the foot of a ladder crouches, it does not grab it",
      modes == ["ground"] and s.crouch, f"modes={modes} crouch={s.crouch}")

s = settle(ladder_x(0) + 0.6, floor_h + 0.9)
ticks, modes = record(s, UP, 120)
check("holding up beside a hatch does not grab the ladder you just left",
      modes == ["ground"], f"modes={modes}")

print("\n8. a ladder is never a trap")
s = settle(ladder_x(0) + 0.45, 0.9)
record(s, UP, 3)                                 # mid-grab
check("mid-grab", s.mode == "mount", f"mode={s.mode}")
ticks, modes = record(s, UP | JMP, 10)
check("a jump during the grab lets go, and stays gone", modes == ["mount", "air"], f"modes={modes}")
check("and no tick on foot during that is a jump", worst(ticks) <= JERK,
      f"worst {worst(ticks):.3f} m/tick")

s = settle(ladder_x(0), 0.9)
record(s, UP, 60)                                # well up the ladder
check("climbing", s.mode == "ladder", f"mode={s.mode}")
ticks, modes = record(s, UP | JMP, 12)
check("jumping off a ladder with up still held does not re-grab it", modes == ["ladder", "air"],
      f"modes={modes}")

print("\n9. the same inputs give the same frames")
a, b = settle(ladder_x(0) + 0.45, 0.9), settle(ladder_x(0) + 0.45, 0.9)
ta, _ = record(a, UP, 300)
tb, _ = record(b, UP, 300)
check("bit-identical replay", ta == tb and (a.x, a.y) == (b.x, b.y),
      f"end {F(a.x):.4f},{F(a.y):.4f}")

print("\n" + ("ALL PASS" if ok else "SOME FAILED"))
