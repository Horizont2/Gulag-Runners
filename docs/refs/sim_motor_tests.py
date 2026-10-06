import sim_motor_mirror as T
from sim_motor_mirror import S, step, X, F, L, R, UP, DN, JMP, DOD

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
ok &= check("apex is usable indoors", apex-base > 0.7, f"apex={apex-base:.3f} headroom={floor_h-slab-1.8:.2f}")

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
ok &= check("dodge costs stamina", s.stam == stam0-1, f"stam={s.stam}")

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
