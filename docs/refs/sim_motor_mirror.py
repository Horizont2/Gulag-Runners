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

C = dict(RUN=M(3600), CROUCH=M(1600), GACC=M(40000), GDEC=M(50000),
         AACC=M(22000), ADEC=M(10000), GRAV=M(28000), MAXFALL=M(18000),
         JUMP=M(9500), CUT=M(450), COYOTE=6, BUF=7,
         CUP=M(2000), CDN=M(2600), DODGE=M(7000), DF=21, DR=9,
         LDIS=M(1500), SMAX=3, SREC=72, W=M(450), H=M(1800), CH=M(1100),
         STEP=M(300), CORNER=M(250), FALLTHRU=18,
         LOUD=M(2500), STEPN=18, HARD=M(8000))

L, R, UP, DN, JMP, DOD = 1, 2, 4, 8, 16, 32

class S:
    def __init__(s, x, y):
        s.x, s.y = X(x), X(y)
        s.vx = s.vy = 0
        s.mode = "air"; s.facing = 1; s.crouch = False; s.jump_held = False
        s.coyote = s.buf = s.dodge = s.dodge_rec = s.fallthru = s.stepn = 0
        s.stam = C["SMAX"]; s.stam_t = 0; s.ladder = -1; s.noise = 0

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

def any_solid(w, box):
    return any(overlaps(box, s) for s in w["solid"])

def grounded(s, w):
    half = C["W"] // 2
    feet = (s.x - half + SKIN, s.y - GROUND_PROBE, s.x + half - SKIN, s.y)
    if any_solid(w, feet): return True
    if s.fallthru == 0:
        for p in w["oneway"]:
            if s.y + SKIN < p[3]: continue
            if overlaps(feet, p): return True
    return False

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

def move_y(s, dy, w):
    if dy == 0: return
    half = C["W"] // 2
    h = C["CH"] if s.crouch else C["H"]
    start = s.y
    ty = s.y + dy
    for sol in w["solid"]:
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
    for a in ("coyote", "buf", "dodge_rec", "fallthru", "stepn"):
        if getattr(s, a) > 0: setattr(s, a, getattr(s, a) - 1)
    if s.stam < C["SMAX"]:
        s.stam_t += 1
        if s.stam_t >= C["SREC"]: s.stam_t = 0; s.stam += 1
    else: s.stam_t = 0

    wx = (1 if inp & R else 0) - (1 if inp & L else 0)
    wy = (1 if inp & UP else 0) - (1 if inp & DN else 0)
    jh = bool(inp & JMP)
    if jh and not s.jump_held: s.buf = C["BUF"]
    s.jump_held = jh
    if wx: s.facing = wx

    if s.mode == "dodge":
        move_x(s, mul(s.vx, DT), w); s.dodge -= 1
        if s.dodge <= 0:
            s.dodge_rec = C["DR"]; s.vx //= 2
            s.mode = "ground" if grounded(s, w) else "air"
        return
    if (inp & DOD) and s.dodge_rec == 0 and s.stam > 0:
        s.stam -= 1; s.mode = "dodge"; s.dodge = C["DF"]; s.crouch = False; s.ladder = -1
        d = wx or s.facing; s.facing = d; s.vx = C["DODGE"] * d; s.vy = 0; s.noise = 2
        move_x(s, mul(s.vx, DT), w); s.dodge -= 1
        return

    if s.mode == "ladder" or (wy and ladder_at(s, w) >= 0 and can_mount(s, w, wy)):
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
    acc = (C["GACC"] if g else C["AACC"]) if wx else (C["GDEC"] if g else C["ADEC"])
    s.vx = move_towards(s.vx, tgt, mul(acc, DT))

    if s.buf > 0 and (g or s.coyote > 0):
        s.buf = 0; s.coyote = 0; s.crouch = False
        s.vy = C["JUMP"]; s.mode = "air"; g = False; s.noise = 1
    if not s.jump_held and s.vy > 0: s.vy = mul(s.vy, C["CUT"])

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

def can_mount(s, w, wy):
    i = ladder_at(s, w)
    if i < 0: return False
    if wy < 0 and s.mode == "ground" and w["ladder"][i][1] >= s.y: return False
    return True

def climb(s, wx, wy, w):
    if s.mode != "ladder":
        i = ladder_at(s, w)
        s.mode = "ladder"; s.ladder = i; s.crouch = False; s.vx = s.vy = 0
        l = w["ladder"][i]; s.x = l[0] + (l[2] - l[0]) // 2
    if s.buf > 0:
        s.buf = 0; s.mode = "air"; s.ladder = -1
        s.vx = C["RUN"] * wx; s.vy = C["JUMP"]; s.noise = 1; return
    s.vx = 0
    s.vy = C["CUP"] if wy > 0 else (-C["CDN"] if wy < 0 else 0)
    move_y(s, mul(s.vy, DT), w)
    if wx: move_x(s, mul(C["LDIS"] * wx, DT), w)
    if wy and s.stepn == 0: s.noise = 1; s.stepn = C["STEPN"]
    b = body(s)
    if not (0 <= s.ladder < len(w["ladder"]) and overlaps(b, w["ladder"][s.ladder])):
        s.ladder = -1
        s.mode = "ground" if grounded(s, w) else "air"
        return
    if wx and grounded(s, w):
        s.ladder = -1; s.mode = "ground"; s.vx = s.vy = 0
