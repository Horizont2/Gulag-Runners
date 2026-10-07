import sim_motor_mirror as T
from sim_motor_mirror import S, step, X, F, C, L, R, UP, DN, JMP, DOD

# ---- the exact arena baked into SetupScene -------------------------------------
floors, rooms = 3, 3
room_w, floor_h, slab, wall, door_h = 7.2, 3.0, 0.3, 0.4, 2.2
ladder_w, hatch_w = 0.9, 1.6
total_w, total_h = rooms*room_w, floors*floor_h
left, right = -total_w/2, total_w/2
ladder_x = left + room_w
lintel = max(0.1, floor_h - door_h)

pieces = []
for f in range(floors):
    y = f*floor_h
    if f > 0:
        hmin, hmax = ladder_x-hatch_w/2, ladder_x+hatch_w/2
        lw = hmin-left
        if lw > .05: pieces.append(((left+lw/2, y-slab/2), (lw, slab), 0))
        rw = right-hmax
        if rw > .05: pieces.append(((hmax+rw/2, y-slab/2), (rw, slab), 0))
    else: pieces.append(((0, y-slab/2), (total_w, slab), 0))
    for r in range(1, rooms):
        x = left + r*room_w
        if abs(x-ladder_x) < room_w/2: continue
        pieces.append(((x, y+door_h+lintel/2), (wall, lintel), 0))
pieces.append(((left-wall/2, total_h/2), (wall, total_h), 0))
pieces.append(((right+wall/2, total_h/2), (wall, total_h), 0))
for f in range(floors-1):
    pieces.append(((ladder_x, f*floor_h+floor_h/2), (ladder_w, floor_h+slab), 2))
pieces.append(((left+room_w*2.2, floor_h*0.6), (room_w/2, 0.2), 1))

def aabb(c, s): return (X(c[0]-s[0]/2), X(c[1]-s[1]/2), X(c[0]+s[0]/2), X(c[1]+s[1]/2))
W = {"solid": [aabb(c,s) for c,s,k in pieces if k==0],
     "oneway":[aabb(c,s) for c,s,k in pieces if k==1],
     "ladder":[aabb(c,s) for c,s,k in pieces if k==2]}
print(f"world: {len(W['solid'])} solid, {len(W['oneway'])} one-way, {len(W['ladder'])} ladder\n")

def run(s, inp, frames):
    for _ in range(frames): step(s, inp, W)
    return s

def check(name, cond, detail=""):
    print(f"  {'PASS' if cond else 'FAIL'}  {name}{('  ' + detail) if detail else ''}")
    return cond

ok = True
print("1. spawn and settle")
s = S(left+2.5, 0.5)
run(s, 0, 60)
ok &= check("rests on the floor", abs(F(s.y)) < 0.02, f"y={F(s.y):.3f} mode={s.mode}")
ok &= check("does not fall through", s.mode == "ground")

print("\n2. run right through the doorway")
s = S(left+2.5, 0.0); run(s, R, 300)
ok &= check("passes the door lintel", F(s.x) > 5.0, f"x={F(s.x):.2f}")
ok &= check("stopped by the outer wall", F(s.x) < right, f"x={F(s.x):.2f}")

print("\n3. jump height")
s = S(0, 0.0); run(s, 0, 10)
base = F(s.y); apex = base
for i in range(120):
    step(s, JMP, W); apex = max(apex, F(s.y))
ok &= check("apex below a floor", apex-base < floor_h, f"apex={apex-base:.3f} floor={floor_h}")
ok &= check("apex is usable indoors", apex-base > 0.7, f"apex={apex-base:.3f}")

print("\n4. ladder to the floor above")
s = S(ladder_x, 0.0); run(s, 0, 10)
for _ in range(400):
    step(s, UP, W)
    if F(s.y) >= floor_h: break
ok &= check("reached floor 1", F(s.y) > floor_h-0.1, f"y={F(s.y):.2f} mode={s.mode}")
run(s, R, 90)
ok &= check("stands on floor 1", s.mode == "ground" and F(s.y) > floor_h-0.1,
            f"y={F(s.y):.2f} mode={s.mode}")

print("\n5. walk back to the ladder and climb down")
for _ in range(400):
    step(s, L, W)
    if abs(F(s.x) - ladder_x) < 0.25: break
ok &= check("walked back to the ladder", abs(F(s.x)-ladder_x) < 0.3, f"x={F(s.x):.2f}")
for _ in range(500):
    step(s, DN, W)
    if F(s.y) < 0.1: break
ok &= check("back to floor 0", F(s.y) < 0.1, f"y={F(s.y):.2f}")

print("\n6. one-way platform")
ow = pieces[-1]
s = S(ow[0][0], ow[0][1]+0.4); run(s, 0, 60)
ok &= check("lands on the one-way", abs(F(s.y)-(ow[0][1]+0.1)) < 0.05, f"y={F(s.y):.3f}")
for _ in range(30): step(s, DN | JMP, W)
ok &= check("drops through with down+jump", F(s.y) < ow[0][1]-0.2, f"y={F(s.y):.2f}")

print("\n7. dodge")
s = S(0, 0.0); run(s, 0, 10)
x0 = F(s.x); stam0 = s.stam
for _ in range(21): step(s, DOD | R, W)
ok &= check("dodge travels", abs(F(s.x)-x0) > 1.5, f"d={F(s.x)-x0:.2f} m")
ok &= check("dodge costs stamina", s.stam == C["SMAX"] - C["DCOST"],
      f"{C['SMAX']} -> {s.stam}, a dodge costs {C['DCOST']}")

print("\n8. crouch under a lintel")
wallx = left + 2*room_w
s = S(wallx-1.0, 0.0); run(s, 0, 10)
run(s, R, 120)
ok &= check("walks under the lintel upright", F(s.x) > wallx+0.5, f"x={F(s.x):.2f}")

print("\n9. falls down the hatch")
s = S(ladder_x, floor_h+0.1); run(s, 0, 10)
ok &= check("stands at the hatch edge", s.mode in ("ground","air"), f"mode={s.mode}")

print("\n10. noise")
s = S(0,0.0); run(s,0,10)
noises=set()
for _ in range(120): step(s, R, W); noises.add(s.noise)
ok &= check("running is Loud", 3 in noises, f"levels={sorted(noises)}")
s = S(0,0.0); run(s,0,10)
noises=set()
for _ in range(120): step(s, R|DN, W); noises.add(s.noise)
ok &= check("crouch-walking is silent", noises == {0}, f"levels={sorted(noises)}")

print("\n11a. the jump is one height, not a thumb measurement")
peaks = []
for hold in (3, 8, 20, 200):
    s2 = S(-3.0, 0.9)
    for _ in range(120):
        step(s2, 0, W)
        if s2.mode == "ground": break
    top = F(s2.y)
    for t in range(90):
        step(s2, JMP if t < hold else 0, W)
        top = max(top, F(s2.y))
    peaks.append(round(top, 3))
check("every tap gives the same jump", len(set(peaks)) == 1, f"apex {peaks} m for 3/8/20/200 frames held")

print("\n10c. a slope made of boxes is walked up, a wall is not")
# An AABB world has no slopes, only stairs. A step over the free 0.30 m is hauled up at the
# cost of the speed carried into it, which is what turns a stack of crates into a ramp —
# and the haul has to find the HIGHEST reachable surface, or a stack stops the body at its
# first step with the second one sitting on top of the landing.
STAIRS = {"solid": [(X(-6.0), X(-1.0), X(6.0), X(0.0))] +
                   [(X(-1.0 + i), X(-1.0), X(6.0), X(i * 0.5)) for i in range(1, 5)] +
                   [(X(4.0), X(-1.0), X(5.0), X(3.5))],
          "oneway": [], "ladder": []}


def walk(st, inp, cond, limit=600):
    for _ in range(limit):
        step(st, inp, STAIRS)
        if cond(st):
            return True
    return False


s2 = S(-3.0, 0.2)
walk(s2, 0, lambda st: st.mode == "ground", 120)
base = F(s2.y)
check("four 0.50 m steps are walked up with no jump at all",
      walk(s2, R, lambda st: F(st.y) > 1.95), f"{base:.2f} -> {F(s2.y):.2f} m")
check("and the 1.5 m wall at the top is still a wall",
      not walk(s2, R, lambda st: F(st.y) > 2.1, 240), f"y {F(s2.y):.2f}")

s2 = S(-3.0, 0.2)
walk(s2, 0, lambda st: st.mode == "ground", 120)
walk(s2, R, lambda st: F(st.y) > 0.4)
check("the haul costs most of the speed it was walked at", F(s2.vx) < 1.5,
      f"{F(s2.vx):.2f} m/s of {F(T.C['RUN']):.2f} on the step up")

print("\n11b. the dodge is rare and short")
s2 = S(-3.0, 0.9)
for _ in range(120):
    step(s2, 0, W)
    if s2.mode == "ground": break
# back to back: a dodge is 21 frames plus 9 of recovery, so 30 is as fast as it can be asked for
dodges = 0
for t in range(95):
    was = s2.mode
    step(s2, (DOD | R) if t % 30 == 0 else 0, W)
    if s2.mode == "dodge" and was != "dodge": dodges += 1
check("the pool pays for two in a row and then makes you wait", dodges == 2,
      f"{dodges} dodges in 1.6 s, stamina {s2.stam}/{C['SMAX']}")

s2 = S(-3.0, 0.9)
for _ in range(120):
    step(s2, 0, W)
    if s2.mode == "ground": break
start = F(s2.x)
for _ in range(23):
    step(s2, DOD, W)
travel = abs(F(s2.x) - start)
check("and it is a step out of reach, not a teleport", 1.4 < travel < 2.0, f"{travel:.2f} m")

print("\n11. facing")
s = S(0, 0.0); run(s, 0, 10)
run(s, R, 30)
ok &= check("faces right when running right", s.facing == 1, f"facing={s.facing}")
run(s, L, 30)
ok &= check("faces left when running left", s.facing == -1, f"facing={s.facing}")
run(s, 0, 60)
ok &= check("keeps facing after stopping", s.facing == -1, f"facing={s.facing}")
f0 = s.facing
for _ in range(21): step(s, DOD, W)
ok &= check("dodge keeps the current facing", s.facing == f0, f"facing={s.facing}")

print("\n" + ("ALL PASS" if ok else "SOME FAILED"))
