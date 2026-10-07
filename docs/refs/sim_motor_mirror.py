"""Faithful Python port of PlayerMotor, run against the real SetupScene arena.
Same integer arithmetic, same order of operations, same collision resolution."""
ONE = 1 << 16
def M(t): return (t << 16) // 1000
def mul(a, b): return (a * b) >> 16          # C# arithmetic shift on int
def F(x): return x / ONE
def X(m): return int(round(m * 1000)) * ONE // 1000 if False else M(int(round(m * 1000)))

DT = ONE // 60
SKIN = 16
GROUND_PROBE = M(20)
GROUND_STICK = M(200)

C = dict(RUN=M(3000), CROUCH=M(1600), GACC=M(40000), GDEC=M(50000),
         AACC=M(22000), ADEC=M(10000), GRAV=M(28000), MAXFALL=M(18000),
         JUMP=M(9500), VARJUMP=False, CUT=M(200), COYOTE=6, BUF=7,
         CUP=M(2000), CDN=M(2600), DODGE=M(4800), DF=21, DR=9, DCOST=2,
         LDIS=M(1500), LSNAP=M(2000), LTOP=M(60), MREACH=M(1600),
         SSPD=M(2400), SMINF=10, SMAXF=30, LREGRAB=12,
         SMAX=4, SREC=72, W=M(300), H=M(910), CH=M(682),
         STEP=M(300), CORNER=M(250), FALLTHRU=18,
         LOUD=M(2500), STEPN=18, HARD=M(8000))

L, R, UP, DN, JMP, DOD, ACTION, ATTACK = 1, 2, 4, 8, 16, 32, 64, 128

class S:
    def __init__(s, x, y):
        s.x, s.y = X(x), X(y)
        s.vx = s.vy = 0
        s.mode = "air"; s.facing = 1; s.crouch = False; s.jump_held = False
        s.coyote = s.buf = s.dodge = s.dodge_rec = s.fallthru = s.stepn = 0
        s.ladder_cd = 0
        s.script_t = 0; s.script_frames = 0; s.script_dir = 1
        s.script_from = (0,0); s.script_to = (0,0)
        s.stam = C["SMAX"]; s.stam_t = 0; s.ladder = -1; s.noise = 0
        # loot (LootMotor): three slots, the chest claim, and the per-tick outputs
        s.landed_speed = 0
        s.inv = None; s.opening = -1; s.picked = 0; s.opened_chest = -1
        s.dropped = 0; s.standing_on = -1; s.action_held = False
        # combat (CombatMotor)
        s.health = 100; s.attack = 0; s.attack_t = 0; s.combo = 0; s.combo_t = 0
        s.swing_spent = False; s.blocking = False; s.guard_t = 0; s.guard_break = 0
        s.stun = 0; s.stagger = 0; s.dead = False; s.guard_held = False
        s.speed_permille = 0; s.damage_taken = 0
        s.was_hit = s.was_blocked = s.was_parried = s.swing_started = s.just_died = False

def boxes(scene_pieces, kind):
    return [b for b in scene_pieces if b[4] == kind]

def overlaps(a, b):
    return a[0] < b[2] and a[2] > b[0] and a[1] < b[3] and a[3] > b[1]

def body(s, at_x=None, at_y=None):
    half = C["W"] // 2
    h = C["CH"] if s.crouch else C["H"]
    x = s.x if at_x is None else at_x
    y = s.y if at_y is None else at_y
    return (x - half, y, x + half, y + h)

def in_shaft(sol, lad):
    """Is this solid one the ladder comes up INTO from below - the floor it climbs through -
    rather than the floor it stands on. The tell is the solid's underside."""
    return lad[1] < sol[1] < lad[3]

def any_solid(w, box):
    return any(overlaps(box, s) for s in w["solid"])

def support_under(x, y, fallthru, w, shaft=-1):
    half = C["W"] // 2
    feet = (x - half + SKIN, y - GROUND_PROBE, x + half - SKIN, y)
    # Inside a ladder's shaft the floor it runs through is the hole, not the floor.
    if shaft >= 0:
        if any(overlaps(feet, sol) for sol in w["solid"]
               if not in_shaft(sol, w["ladder"][shaft])): return True
    elif any_solid(w, feet): return True
    if fallthru == 0:
        for p in w["oneway"]:
            if y + SKIN < p[3]: continue
            if overlaps(feet, p): return True
    return False

def grounded(s, w, shaft=-1):
    return support_under(s.x, s.y, s.fallthru, w, shaft)

def sweep_free(from_x, to_x, y, w):
    half = C["W"] // 2
    swept = (min(from_x, to_x) - half, y + SKIN, max(from_x, to_x) + half, y + C["H"])
    return not any_solid(w, swept)

def on_oneway_only(s, w):
    half = C["W"] // 2
    feet = (s.x - half + SKIN, s.y - GROUND_PROBE, s.x + half - SKIN, s.y)
    if any_solid(w, feet): return False
    return any(overlaps(feet, p) for p in w["oneway"])

def move_x(s, dx, w):
    if dx == 0: return
    half = C["W"] // 2
    h = C["CH"] if s.crouch else C["H"]
    tx = s.x + dx
    for sol in w["solid"]:
        b = (tx - half, s.y, tx + half, s.y + h)
        if not overlaps(b, sol): continue
        step = sol[3] - s.y
        if 0 < step <= C["STEP"]:
            raised = sol[3] + SKIN
            rb = (tx - half, raised, tx + half, raised + h)
            if not any_solid(w, rb):
                s.y = raised
                continue
        tx = sol[0] - half - SKIN if dx > 0 else sol[2] + half + SKIN
        s.vx = 0
    s.x = tx

def move_y(s, dy, w, shaft=-1):
    if dy == 0: return
    half = C["W"] // 2
    h = C["CH"] if s.crouch else C["H"]
    start = s.y
    ty = s.y + dy
    for sol in w["solid"]:
        # A ladder is its own shaft: everything it passes through is the hole it climbs.
        if shaft >= 0 and in_shaft(sol, w["ladder"][shaft]): continue
        b = (s.x - half, ty, s.x + half, ty + h)
        if not overlaps(b, sol): continue
        if dy > 0:
            if start + h > sol[1] + SKIN: continue      # not a ceiling from below
            orr = (s.x + half) - sol[0]; orl = sol[2] - (s.x - half)
            shift = None
            if 0 < orr <= C["CORNER"]: shift = -orr - SKIN
            elif 0 < orl <= C["CORNER"]: shift = orl + SKIN
            if shift is not None:
                nx = s.x + shift
                if not any_solid(w, (nx - half, ty, nx + half, ty + h)):
                    s.x = nx
                    continue
            ty = sol[1] - h - SKIN
        else:
            if start + SKIN < sol[3]: continue          # not a floor from above
            ty = sol[3] + SKIN
        s.vy = 0
    if dy < 0 and s.fallthru == 0:
        for p in w["oneway"]:
            if start + SKIN < p[3]: continue
            if not overlaps((s.x - half, ty, s.x + half, ty + h), p): continue
            ty = p[3] + SKIN
            s.vy = 0
    s.y = ty

def move_towards(cur, tgt, md):
    d = tgt - cur
    return tgt if abs(d) <= md else cur + (md if d > 0 else -md)

def step(s, inp, w):
    s.noise = 0
    for a in ("coyote", "buf", "dodge_rec", "fallthru", "stepn", "ladder_cd"):
        if getattr(s, a) > 0: setattr(s, a, getattr(s, a) - 1)
    # Nothing comes back while the guard is up.
    if s.stam < C["SMAX"] and not s.blocking:
        s.stam_t += 1
        if s.stam_t >= C["SREC"]: s.stam_t = 0; s.stam += 1
    else: s.stam_t = 0

    wx = (1 if inp & R else 0) - (1 if inp & L else 0)
    wy = (1 if inp & UP else 0) - (1 if inp & DN else 0)
    jh = bool(inp & JMP)
    if jh and not s.jump_held: s.buf = C["BUF"]
    s.jump_held = jh
    if wx: s.facing = wx

    if s.mode in ("mantle", "mount"):
        return step_scripted(s, w)

    if s.mode == "dodge":
        move_x(s, mul(s.vx, DT), w); s.dodge -= 1
        if s.dodge <= 0:
            s.dodge_rec = C["DR"]; s.vx //= 2
            s.mode = "ground" if grounded(s, w) else "air"
        return
    # Feet on the floor or it does not happen: a dodge in the air is a second jump with
    # invulnerability on it.
    if (inp & DOD) and s.dodge_rec == 0 and s.mode == "ground" and s.stam >= C["DCOST"]:
        s.stam -= C["DCOST"]; s.mode = "dodge"; s.dodge = C["DF"]; s.crouch = False; s.ladder = -1
        d = wx or s.facing; s.facing = d; s.vx = C["DODGE"] * d; s.vy = 0; s.noise = 2
        move_x(s, mul(s.vx, DT), w); s.dodge -= 1
        return

    if s.mode == "ladder":
        return climb(s, wx, wy, w)
    if try_mount(s, wy, w):
        if s.mode == "mount": return step_scripted(s, w)
        return climb(s, wx, wy, w)

    g = s.mode == "ground"
    want_crouch = g and wy < 0
    if s.crouch and not want_crouch:
        stand = (s.x - C["W"]//2, s.y, s.x + C["W"]//2, s.y + C["H"])
        if any_solid(w, stand): want_crouch = True
    s.crouch = want_crouch

    if g and wy < 0 and s.buf > 0 and on_oneway_only(s, w):
        s.buf = 0; s.fallthru = C["FALLTHRU"]; s.mode = "air"; g = False

    tgt = (C["CROUCH"] if s.crouch else C["RUN"]) * wx
    if s.speed_permille > 0:
        tgt = (tgt * s.speed_permille) // 1000
    if s.stun > 0 or s.stagger > 0:
        s.vx = move_towards(s.vx, 0, mul(C["GDEC"] // 6, DT))
    else:
        acc = (C["GACC"] if g else C["AACC"]) if wx else (C["GDEC"] if g else C["ADEC"])
        s.vx = move_towards(s.vx, tgt, mul(acc, DT))

    if s.buf > 0 and (g or s.coyote > 0):
        s.buf = 0; s.coyote = 0; s.crouch = False
        s.vy = C["JUMP"]; s.mode = "air"; g = False; s.noise = 1
    if C["VARJUMP"] and not s.jump_held and s.vy > 0: s.vy = mul(s.vy, C["CUT"])

    if not g:
        s.vy -= mul(C["GRAV"], DT)
        if s.vy < -C["MAXFALL"]: s.vy = -C["MAXFALL"]
    elif s.vy <= 0:
        s.vy = -GROUND_STICK

    impact = -s.vy
    was_air = s.mode == "air"
    move_x(s, mul(s.vx, DT), w)
    move_y(s, mul(s.vy, DT), w)

    if grounded(s, w):
        if was_air:
            s.noise = 3 if impact > C["HARD"] else 2
            s.stepn = C["STEPN"]
            s.landed_speed = impact
        s.mode = "ground"; s.coyote = C["COYOTE"]
        if s.vy < 0: s.vy = 0
    else:
        s.mode = "air"

    # footsteps
    if s.mode == "ground" and s.noise == 0 and not s.crouch:
        sp = abs(s.vx)
        if sp > M(100) and s.stepn == 0:
            s.noise = 3 if sp >= C["LOUD"] else 1
            s.stepn = C["STEPN"]

def ladder_at(s, w):
    b = body(s)
    for i, l in enumerate(w["ladder"]):
        if overlaps(b, l): return i
    return -1

def frames_for(dist):
    if dist <= M(20): return 0
    if C["SSPD"] <= 0: return C["SMAXF"]
    f = (((dist << 16) // C["SSPD"]) * 60 >> 16) + 1
    return max(C["SMINF"], min(C["SMAXF"], f))

def ladder_under_feet(s, w):
    """The ladder a standing body is on top of: it reaches the feet from below and runs
    under them. This is how you get onto a ladder that has no hatch beside it."""
    half = C["W"]//2; reach = C["H"]//2
    for i, l in enumerate(w["ladder"]):
        if l[0] >= s.x+half or l[2] <= s.x-half: continue
        drop = s.y - l[3]
        if drop < -SKIN or drop > reach: continue
        return i
    return -1

def try_mount(s, wy, w):
    if not wy: return False
    if s.ladder_cd > 0: return False
    if s.crouch and any_solid(w, (s.x - C["W"]//2, s.y, s.x + C["W"]//2, s.y + C["H"])):
        return False
    i = ladder_at(s, w)
    # Standing on top of a ladder whose rungs start just under the feet: stepping over the
    # edge IS the way down when there is no hatch beside it.
    stepping_on = False
    if i < 0 and wy < 0 and s.mode == "ground":
        i = ladder_under_feet(s, w); stepping_on = i >= 0
    if i < 0: return False
    box = w["ladder"][i]
    centre = box[0] + (box[2]-box[0])//2

    if wy > 0:
        if s.y >= box[3] - C["LTOP"] - C["H"]//4: return False
    else:
        if s.mode == "ground" and box[1] >= s.y and not stepping_on: return False
        if not stepping_on and support_under(centre, s.y, s.fallthru, w): return False

    target = centre if sweep_free(s.x, centre, s.y, w) else s.x
    target_y = box[3] - C["LTOP"] if stepping_on else s.y
    frames = frames_for(max(abs(target - s.x), abs(target_y - s.y)))

    s.ladder = i; s.crouch = False; s.vx = s.vy = 0
    s.script_dir = 1 if wy > 0 else -1
    if frames <= 0:
        s.x = target; s.y = target_y; s.mode = "ladder"
        return True
    s.script_from = (s.x, s.y); s.script_to = (target, target_y)
    s.script_frames = frames; s.script_t = frames
    s.mode = "mount"; s.noise = 1
    return True

def step_scripted(s, w):
    if s.mode == "mount" and s.buf > 0:
        s.buf = 0; s.script_t = 0; s.ladder = -1; s.ladder_cd = C["LREGRAB"]
        s.mode = "air"; s.vx = 0; s.vy = C["JUMP"]; s.noise = 1
        return
    s.script_t -= 1
    if s.script_t <= 0:
        s.x, s.y = s.script_to; s.vx = s.vy = 0
        if s.mode == "mount":
            s.mode = "ladder"
        else:
            s.mode = "ground" if grounded(s, w) else "air"
            s.ladder_cd = C["LREGRAB"]
        return
    total = s.script_frames if s.script_frames > 0 else 1
    done = total - s.script_t
    t = (ONE*done)//total
    def ss(v):
        v = max(0, min(ONE, v)); return mul(mul(v,v), 3*ONE - 2*v)
    if s.mode == "mount":
        xT = ss(t); yT = xT
    else:
        yT = ss(t*3//2); xT = ss((t - M(300))*10//7)
    s.x = s.script_from[0] + mul(s.script_to[0]-s.script_from[0], xT)
    s.y = s.script_from[1] + mul(s.script_to[1]-s.script_from[1], yT)

def supported(x, g, w):
    half=C["W"]//2
    for k in (-1,0,1):
        px=x+half*k
        if not any_solid(w,(px-SKIN*4, g-GROUND_PROBE, px+SKIN*4, g-SKIN)): return False
    return True

def floor_run(frm, d, w):
    step=M(200); reached=0
    for i in range(1,26):
        x=frm[0]+step*(i*d)
        g=ground_below(x, frm[1]+C["H"], C["H"]*2, w)
        if g is None or not supported(x,g,w): break
        reached=step*i
    return reached

def find_landing(s, side, w):
    half=C["W"]//2; step=M(100); steps=C["MREACH"]//step
    for i in range(1,steps+1):
        x=s.x+step*(i*side)
        g=ground_below(x, s.y+C["H"], C["H"]*2, w)
        if g is None: continue
        if g < s.y - C["LTOP"]*4: continue
        if any_solid(w,(x-half, g+SKIN, x+half, g+C["H"])): continue
        if not supported(x,g,w): continue
        return (x, g+SKIN)
    return None

def step_out(s, ladder, w):
    """The floor a ladder ENDS INSIDE, which is the one it serves. None for a hatch ladder:
    a hatch is a hole, so there is nothing overhead to step onto and the sideways search
    takes over on its own."""
    if not (0 <= ladder < len(w["ladder"])): return None
    l = w["ladder"][ladder]; half = C["W"]//2
    top = None
    for sol in w["solid"]:
        if sol[0] >= s.x+half or sol[2] <= s.x-half: continue
        if l[3] < sol[1] or l[3] > sol[3] + C["LTOP"]: continue
        if top is None or sol[3] > top: top = sol[3]
    if top is None: return None
    if any_solid(w, (s.x-half, top+SKIN, s.x+half, top+C["H"])): return None
    if not supported(s.x, top, w): return None
    return (s.x, top+SKIN)

def start_scripted(s, landing, side):
    s.script_from=(s.x,s.y); s.script_to=landing
    span_x = abs(s.script_to[0]-s.x)*10//7
    span_y = abs(s.script_to[1]-s.y)*3//2
    frames = frames_for(max(span_x, span_y)) or C["SMINF"]
    s.script_frames=frames; s.script_t=frames
    s.script_dir = 1 if side>=0 else -1
    s.mode="mantle"; s.ladder=-1
    s.vx=s.vy=0; s.facing = 1 if side>=0 else -1

def try_mantle(s, prefer, ladder, w):
    up = step_out(s, ladder, w)
    if up is not None:
        start_scripted(s, up, s.facing); return True
    r=find_landing(s,1,w); l=find_landing(s,-1,w)
    if r is None and l is None: return False
    if prefer>0 and r: side=1
    elif prefer<0 and l: side=-1
    elif l is None: side=1
    elif r is None: side=-1
    else: side = 1 if floor_run(r,1,w) >= floor_run(l,-1,w) else -1
    start_scripted(s, r if side>0 else l, side)
    return True

def ground_below(x, y, maxd, w):
    half = C["W"]//2
    best = None
    for sol in w["solid"]:
        if sol[2] <= x-half or sol[0] >= x+half: continue
        if sol[3] > y + SKIN: continue
        if sol[3] < y - maxd: continue
        if best is None or sol[3] > best: best = sol[3]
    return best

def climb(s, wx, wy, w):
    if s.buf > 0:
        s.buf = 0; s.mode = "air"; s.ladder = -1; s.ladder_cd = C["LREGRAB"]
        s.vx = C["RUN"]*wx; s.vy = C["JUMP"]; s.noise = 1; return
    if not (0 <= s.ladder < len(w["ladder"])):
        s.mode = "ground" if grounded(s, w) else "air"; return
    l = w["ladder"][s.ladder]
    centre = l[0] + (l[2]-l[0])//2
    if not wx:
        want = move_towards(s.x, centre, mul(C["LSNAP"], DT))
        if sweep_free(s.x, want, s.y, w): s.x = want
    s.vx = 0
    s.vy = C["CUP"] if wy > 0 else (-C["CDN"] if wy < 0 else 0)
    move_y(s, mul(s.vy, DT), w, s.ladder)
    ceiling = l[3] - C["LTOP"]
    at_top = s.y >= ceiling
    if at_top:
        s.y = ceiling
        if s.vy > 0: s.vy = 0
    if at_top and wy > 0 and try_mantle(s, wx, s.ladder, w):
        return
    if wy < 0 and grounded(s, w, s.ladder):
        s.ladder = -1; s.ladder_cd = C["LREGRAB"]
        s.mode = "ground"; s.vx = s.vy = 0; s.noise = 1
        return
    if wx: move_x(s, mul(C["LDIS"]*wx, DT), w)
    if wy and s.stepn == 0: s.noise = 1; s.stepn = C["STEPN"]
    b = body(s)
    if not overlaps(b, l):
        s.ladder = -1
        s.mode = "ground" if grounded(s, w) else "air"
        return
    if wx and grounded(s, w, s.ladder):
        s.ladder = -1; s.ladder_cd = C["LREGRAB"]
        s.mode = "ground"; s.vx = s.vy = 0
