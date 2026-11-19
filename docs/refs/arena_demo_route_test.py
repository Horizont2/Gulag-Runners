"""Can a fighter actually get around Arena_Demo, and does it stay where it was put?

Three things went wrong at once in this arena and all three looked like one bug from the
outside — "the player teleports and the left ladder does not work":

  * standing still at the spawn, the body slid a third of a metre in Z, because any ladder
    within the look-ahead steered the depth lane whether or not anybody had reached for it;
  * the left ladder could not be climbed DOWN from the floor above, because its head is
    modelled 2 cm proud of that floor and the right one's is flush — a difference nobody
    authored, deciding whether a route existed;
  * and neither showed up in any suite, because the suites test the greybox and a hand-built
    location's faults are all in its own numbers.

So this one runs the real motor over the real scene and walks the whole building: down is a
route, up is a route, and the fighter ends where it started. Mirrors nothing new; it is the
same motor as every other test here, pointed at the level we ship.
"""
import arena_demo_geometry as G
from sim_motor_mirror import S, step, X, F, C, L, R, UP, DN

W = G.world()
SPAWNS = G.spawns()

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


def centre(i):
    l = W["ladder"][i]
    return (F(l[0]) + F(l[2])) / 2


def walk_to(s, x, limit=1200):
    return until(s, R if x > F(s.x) else L, lambda s: abs(F(s.x) - x) < 0.35, limit)


print("A fighter left alone stays where it was placed")
for n, (sx, sy, facing, sz) in enumerate(SPAWNS, 1):
    s = S(sx, sy)
    s.depth = X(sz)
    s.facing = facing
    until(s, 0, lambda s: s.mode == "ground", 120)
    for _ in range(300):
        step(s, 0, W)
    check(f"player {n} holds its spawn lane for five seconds",
          abs(F(s.depth) - sz) < 0.01, f"depth {F(s.depth):.3f}, placed at {sz:.3f}")
    check(f"player {n} holds its spawn x", abs(F(s.x) - sx) < 0.01, f"x {F(s.x):.2f}")

def landing_of(i):
    """The floor a ladder's head reaches, as (top, lane). None if it ends in mid-air.

    The lane matters as much as the height: this arena's floors do not all reach the
    fighters' own plane, so a test that drops a body at the spawn's depth lands it in the
    basement every time and then skips itself, which is a pass that proves nothing.
    """
    head = F(W["ladder"][i][3])
    half = F(C["BODYZ"]) / 2
    best = None
    for j, sol in enumerate(W["solid"]):
        if F(sol[2]) - F(sol[0]) < 0.5:
            continue
        if abs(F(sol[3]) - head) > F(C["STEP"]):
            continue
        if not (F(W["ladder"][i][0]) < F(sol[2]) and F(W["ladder"][i][2]) > F(sol[0])):
            continue
        z = W["solidz"][j]
        lane = min(max(SPAWNS[0][3], F(z[0]) + half), F(z[1]) - half)
        if best is None or F(sol[3]) > best[0]:
            best = (F(sol[3]), lane)
    return best


print("\nEvery ladder can be climbed DOWN from the floor its head reaches")
tested = 0
for i, l in enumerate(W["ladder"]):
    head = F(l[3])
    landing = landing_of(i)
    if landing is None:
        print(f"  ....  ladder {i} head y {head:.2f} ends in mid-air, nothing to step off")
        continue

    top, lane = landing
    tested += 1
    s = S(centre(i), top + 0.05)
    s.depth = X(lane)
    until(s, 0, lambda s: s.mode == "ground", 240)
    check(f"ladder {i} (head y {head:.2f}) takes a fighter standing on it downwards",
          abs(F(s.y) - top) < 0.05 and until(s, DN, lambda s: s.ladder >= 0, 120),
          f"stood at y {F(s.y):.2f} lane {F(s.depth):.2f}; "
          f"head is {F(s.y) - head:+.3f} m from the feet")

check("and that section actually ran", tested > 0, f"{tested} ladder(s) reach a floor")

print("\nA climb-out lands on the lane it will be HELD on")
# Two rules decide a depth and they used to disagree on every ladder here. lane_for answers
# "the nearest part of that floor to where you are now", which off a ladder is its front
# edge; the floor underfoot holds a standing body on the gameplay plane whenever the floor
# reaches it. So the climb-out finished, the body came to rest, and update_depth then pulled
# it back at the full lane speed of 4 m/s -- 0.41 to 0.49 m, eight times faster than the
# climb-out that had just ended, which is what "the landing is jerky" was.
import sim_motor_mirror as _M
for i in range(len(W["ladder"])):
    l = W["ladder"][i]
    sp = _M.span(W, "ladder", i)
    s = S(centre(i), F(l[1]) + 0.3)
    s.depth = (sp[0] + sp[1]) // 2 - (sp[1] - sp[0]) // 2 - C["LSTANDOFF"]
    until(s, 0, lambda s: s.mode in ("ground", "air"), 240)

    landed = None
    for _ in range(1200):
        was = s.mode
        step(s, UP, W)
        if was == "mantle" and s.mode != "mantle":
            landed = F(s.depth)
            break

    if landed is None:
        print(f"  ....  ladder {i} never climbs out from below, nothing to measure")
        continue

    until(s, 0, lambda s: False, 120)          # two seconds of standing still
    drift = abs(F(s.depth) - landed)
    check(f"ladder {i}: the body does not slide after the climb-out",
          drift < 0.01, f"landed {landed:+.3f}, settled {F(s.depth):+.3f}, "
                        f"drift {drift:.3f} m")

print("\nThe whole building, up and back down")
for n, (sx, sy, facing, sz) in enumerate(SPAWNS, 1):
    low, high = (0, 2) if n == 1 else (1, 3)
    s = S(sx, sy)
    s.depth = X(sz)
    s.facing = facing
    until(s, 0, lambda s: s.mode == "ground", 120)

    check(f"player {n} reaches the basement ladder", walk_to(s, centre(low)),
          f"x {F(s.x):.2f}")
    check(f"player {n} climbs out onto the ground floor",
          until(s, UP, lambda s: s.mode == "ground" and F(s.y) > 0.0, 600),
          f"y {F(s.y):.2f}")
    check(f"player {n} crosses to the walkway ladder", walk_to(s, centre(high)),
          f"x {F(s.x):.2f}")
    check(f"player {n} climbs out onto the walkway",
          until(s, UP, lambda s: s.mode == "ground" and F(s.y) > 3.5, 900),
          f"y {F(s.y):.2f}")
    check(f"player {n} goes back down the walkway ladder",
          until(s, DN, lambda s: s.ladder >= 0, 120))
    check(f"player {n} reaches the ground floor again",
          until(s, DN, lambda s: s.mode == "ground" and F(s.y) < 1.5, 900),
          f"y {F(s.y):.2f}")
    check(f"player {n} returns to the basement ladder", walk_to(s, centre(low)))
    check(f"player {n} climbs down into the basement",
          until(s, DN, lambda s: s.mode == "ground" and F(s.y) < -1.0, 900),
          f"y {F(s.y):.2f}")
    for _ in range(180):
        step(s, 0, W)
    check(f"player {n} is back on its own lane afterwards",
          abs(F(s.depth) - sz) < 0.01, f"depth {F(s.depth):.3f}")

print("\n" + ("ALL PASS" if ok else "SOME FAILED"))
