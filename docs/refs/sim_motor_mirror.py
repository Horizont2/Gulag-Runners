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
         LSNAP=M(2000), LTOP=M(60), MREACH=M(1600),
         SSPD=M(2400), SMINF=10, SMAXF=30, LREGRAB=12,
         SMAX=4, SREC=72, W=M(300), H=M(910), CH=M(682),
         STEP=M(300), CLAMBER=M(550), CLAMBER_SPD=400,
         BODYZ=M(600), DEPTHSPD=M(4000), LOOKAHEAD=M(1200), LANEREACH=M(2000),
         LSTANDOFF=M(80), AIMLOCK=24, MANTLE_MINF=26,
         CORNER=M(250), FALLTHRU=18, HOME=0,
         LOUD=M(2500), STEPN=18, HARD=M(8000))

L, R, UP, DN, JMP, DOD, ACTION, ATTACK = 1, 2, 4, 8, 16, 32, 64, 128
# The ninth and tenth bits: where the blow is aimed, which is not where the feet go.
AIM_L, AIM_R = 256, 512

class S:
    def __init__(s, x, y):
        s.x, s.y = X(x), X(y)
        s.vx = s.vy = 0
        s.mode = "air"; s.facing = 1; s.travel = 1; s.aim_lock = 0
        s.crouch = False; s.jump_held = False
        s.coyote = s.buf = s.dodge = s.dodge_rec = s.fallthru = s.stepn = 0
        s.ladder_cd = 0
        s.hitstop = 0
        s.script_t = 0; s.script_frames = 0; s.script_dir = 1
        s.script_from = (0,0); s.script_to = (0,0)
        s.script_from_depth = s.script_to_depth = 0
        s.script_forward = False
        s.stam = C["SMAX"]; s.stam_t = 0; s.ladder = -1; s.noise = 0
        # loot (LootMotor): three slots, the chest claim, and the per-tick outputs
        s.landed_speed = 0
        s.depth = 0
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

def span(w, kind, i):
    """How deep this box reaches. A world with no depths at all is a world where every box
    is on every slice, which is exactly the greybox — and why none of this changes it."""
    z = w.get(kind + "z")
    return z[i] if z and i < len(z) else None


def reaches(sp, depth, half):
    return sp is None or (depth + half > sp[0] and depth - half < sp[1])


def solid_live(w, i):
    """Does this solid block anything yet.

    A box belonging to one of docs/01's shutters is not there at all until the gate's bit goes
    up. One bit per gate, a pure function of the frame, so nothing has to be reconciled.
    """
    gates = w.get("solidgate")
    if not gates or i >= len(gates):
        return True
    g = gates[i]
    return g < 0 or bool(w.get("gatesshut", 0) & (1 << g))


def solid_at(w, i, depth, half):
    """In the way of a body on this slice: there yet, and deep enough.

    Every query goes through this rather than asking the span directly, so a gate that has not
    shut is skipped by all of them at once. The other way about — a gate check bolted onto each
    loop — the one loop somebody forgets is a shutter you can walk through.
    """
    if not solid_live(w, i):
        return False
    return depth is None or reaches(span(w, "solid", i), depth, half)


def nearest_in(sp, depth, inset):
    room = (sp[1] - sp[0]) // 2
    inset = min(inset, room)
    lo, hi = sp[0] + inset, sp[1] - inset
    return lo if depth < lo else (hi if depth > hi else depth)


def any_solid(w, box, depth=None, shaft=-1):
    """Anything solid in this box, on this slice. A shaft index exempts the floors that ladder
    runs up into: while climbing, those are what the body is going past, not what is in its way."""
    half = C["BODYZ"] // 2
    has_shaft = 0 <= shaft < len(w["ladder"])
    for i, sol in enumerate(w["solid"]):
        if not overlaps(box, sol): continue
        if not solid_at(w, i, depth, half): continue
        if has_shaft and in_shaft(sol, w["ladder"][shaft]): continue
        return True
    return False

def support_under(x, y, fallthru, w, shaft=-1, depth=None):
    half = C["W"] // 2
    halfz = C["BODYZ"] // 2
    feet = (x - half + SKIN, y - GROUND_PROBE, x + half - SKIN, y)
    # Inside a ladder's shaft the floor it runs through is the hole, not the floor.
    if shaft >= 0:
        if any(overlaps(feet, sol) for i, sol in enumerate(w["solid"])
               if not in_shaft(sol, w["ladder"][shaft])
               and (solid_at(w, i, depth, halfz))): return True
    elif any_solid(w, feet, depth): return True
    if fallthru == 0:
        for i, p in enumerate(w["oneway"]):
            if depth is not None and not reaches(span(w, "oneway", i), depth, halfz): continue
            if y + SKIN < p[3]: continue
            if overlaps(feet, p): return True
    return False

def grounded(s, w, shaft=-1):
    return support_under(s.x, s.y, s.fallthru, w, shaft, s.depth)

def sweep_free(from_x, to_x, y, w, depth=None, shaft=-1):
    half = C["W"] // 2
    swept = (min(from_x, to_x) - half, y + SKIN, max(from_x, to_x) + half, y + C["H"])
    return not any_solid(w, swept, depth, shaft)

def on_oneway_only(s, w):
    half = C["W"] // 2
    feet = (s.x - half + SKIN, s.y - GROUND_PROBE, s.x + half - SKIN, s.y)
    if any_solid(w, feet, s.depth): return False
    halfz = C["BODYZ"] // 2
    return any(overlaps(feet, p) and reaches(span(w, "oneway", i), s.depth, halfz)
               for i, p in enumerate(w["oneway"]))

def move_x(s, dx, w):
    """Two passes: a stack of boxes is not a list of separate problems. One at a time takes
    the first ledge in array order, finds the next layer on top of it, gives up, and stops
    the body at the foot of a staircase it could have walked up."""
    if dx == 0: return
    half = C["W"] // 2
    h = C["CH"] if s.crouch else C["H"]
    tx = s.x + dx
    limit = max(C["CLAMBER"], C["STEP"])

    best = None
    halfz = C["BODYZ"] // 2
    for i, sol in enumerate(w["solid"]):
        if not solid_at(w, i, s.depth, halfz): continue
        b = (tx - half, s.y, tx + half, s.y + h)
        if not overlaps(b, sol): continue
        step = sol[3] - s.y
        if step <= 0 or step > limit: continue
        if step > C["STEP"] and s.mode != "ground": continue
        if best is not None and sol[3] <= best: continue
        raised = sol[3] + SKIN
        if any_solid(w, (tx - half, raised, tx + half, raised + h), s.depth): continue
        best = sol[3]

    if best is not None:
        clamber = best - s.y > C["STEP"]
        s.y = best + SKIN
        s.x = tx
        if clamber:
            s.vx = (s.vx * C["CLAMBER_SPD"]) // 1000
        return

    for i, sol in enumerate(w["solid"]):
        if not solid_at(w, i, s.depth, halfz): continue
        b = (tx - half, s.y, tx + half, s.y + h)
        if not overlaps(b, sol): continue
        tx = sol[0] - half - SKIN if dx > 0 else sol[2] + half + SKIN
        s.vx = 0

    for i in range(len(w.get("walls", ()))):
        if w["walls"][i][1] in (MINUS_Y, PLUS_Y): continue   # a ceiling blocks vertically
        if not wall_reaches(w, i, s.depth): continue
        wall = wall_at(w, i)
        b = (tx - half, s.y, tx + half, s.y + h)
        if not overlaps(b, wall): continue
        tx = wall[0] - half - SKIN if dx > 0 else wall[2] + half + SKIN
        s.vx = 0

    s.x = tx

# Which way a wall's inner face advances. Mirrors GateToward.
MINUS_X, PLUS_X, MINUS_Y, PLUS_Y = 0, 1, 2, 3


def wall_at(w, i):
    """The wall at the world's current closeness. Mirrors SimWorld.WallDef.At.

    It GROWS from the arena's edge rather than sliding across it: at nothing it has no
    thickness, at full close it is the whole box. Nothing ever opens up behind it."""
    box, grows, _span = w["walls"][i]
    p = w.get("wallprogress", 0)
    width = box[2] - box[0]
    height = box[3] - box[1]

    if grows == PLUS_X:  return (box[0], box[1], box[0] + mul(width, p), box[3])
    if grows == MINUS_X: return (box[2] - mul(width, p), box[1], box[2], box[3])
    if grows == MINUS_Y: return (box[0], box[3] - mul(height, p), box[2], box[3])
    return (box[0], box[1], box[2], box[1] + mul(height, p))


def wall_reaches(w, i, depth):
    return reaches(w["walls"][i][2], depth, C["BODYZ"] // 2)


def shove_from_walls(s, w):
    """A closing wall pushes a body out the way it came. Mirrors PlayerMotor.ShoveFromWalls."""
    half = C["W"] // 2
    h = C["CH"] if s.crouch else C["H"]
    for i in range(len(w.get("walls", ()))):
        if not wall_reaches(w, i, s.depth):
            continue
        wall = wall_at(w, i)
        body = (s.x - half, s.y, s.x + half, s.y + h)
        if not overlaps(body, wall):
            continue

        # Out the way the wall is growing, never out of the nearest face: the nearest face
        # can be the one it has already eaten past.
        grows = w["walls"][i][1]
        if grows == PLUS_X:
            s.x = wall[2] + half + SKIN
            if s.vx < 0: s.vx = 0
        elif grows == MINUS_X:
            s.x = wall[0] - half - SKIN
            if s.vx > 0: s.vx = 0
        elif grows == MINUS_Y:
            s.y = wall[1] - h - SKIN
            if s.vy > 0: s.vy = 0
        else:
            s.y = wall[3] + SKIN
            if s.vy < 0: s.vy = 0


def move_y(s, dy, w, shaft=-1):
    if dy == 0: return
    half = C["W"] // 2
    h = C["CH"] if s.crouch else C["H"]
    start = s.y
    ty = s.y + dy
    for i, sol in enumerate(w["solid"]):
        if not solid_at(w, i, s.depth, C["BODYZ"] // 2): continue
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
                if not any_solid(w, (nx - half, ty, nx + half, ty + h), s.depth):
                    s.x = nx
                    continue
            ty = sol[1] - h - SKIN
        else:
            if start + SKIN < sol[3]: continue          # not a floor from above
            ty = sol[3] + SKIN
        s.vy = 0
    if dy < 0 and s.fallthru == 0:
        for i, p in enumerate(w["oneway"]):
            if not reaches(span(w, "oneway", i), s.depth, C["BODYZ"] // 2): continue
            if start + SKIN < p[3]: continue
            if not overlaps((s.x - half, ty, s.x + half, ty + h), p): continue
            ty = p[3] + SKIN
            s.vy = 0
    s.y = ty

def move_towards(cur, tgt, md):
    d = tgt - cur
    return tgt if abs(d) <= md else cur + (md if d > 0 else -md)

def step(s, inp, w):
    # Hit stop: nothing integrates, so the body is held exactly where the blow caught it.
    # The one place the timer is decremented, so the order inside a tick cannot matter.
    if getattr(s, "hitstop", 0) > 0:
        s.hitstop -= 1
        return
    step_flat(s, inp, w)
    update_depth(s, w)
    if w.get("walls"):
        shove_from_walls(s, w)


def update_depth(s, w):
    """Which slice of the level the body is on. A ladder wins, then whatever is just ahead
    and low enough to step onto, then the floor underfoot — which only moves you if it does
    not already hold you. Climbing is therefore a move away from the camera and coming down
    is a move towards it, worked out from the level rather than from a button."""
    if C["DEPTHSPD"] <= 0 or not w.get("solidz"):
        return
    inset = C["BODYZ"] // 2
    if 0 <= s.ladder < len(w["ladder"]):
        # At this height, not just anywhere on the ladder: one propped against a walkway leans
        # away from the camera as it rises, and holding one depth pushes the body through the
        # rungs near one end of it.
        # ...and in FRONT of them: a body centred on the rung line is a body the ladder
        # passes through.
        # Off the ladder's NEAR FACE, not its centre line: the stand-off is only ever the
        # character's own thickness, so a thick ladder and a thin one hold it the same way.
        sp = span(w, "ladder", s.ladder)
        want = (ladder_depth_at(w, s.ladder, s.y, s.depth, inset)
                - (sp[1] - sp[0]) // 2 - C["LSTANDOFF"])
    elif s.mode != "ground":
        return
    else:
        want = lane_ahead(s, w)
        if want is None:
            want = floor_lane(s, w)
        if want is None:
            return
    s.depth = move_towards(s.depth, want, mul(C["DEPTHSPD"], DT))


def aim(s, inp, wx):
    """PlayerMotor.Aim: which way he is going, and which way the blade goes.

    Travel is the stick. The aim is the swipe, and a swipe wins outright and buys itself
    enough frames to cover the swing it is asking for -- one frame of input cannot hold a
    facing against a stick held for hundreds. While that clock runs, or while a swing is
    live, the feet may not take the aim back; that case IS the feature. Otherwise the aim
    follows the feet, which is all it ever did, which is why nothing that does not swipe
    behaves any differently."""
    if wx: s.travel = wx

    ax = (1 if inp & AIM_R else 0) - (1 if inp & AIM_L else 0)
    if ax:
        s.facing = ax
        s.aim_lock = C["AIMLOCK"]
        return

    if s.aim_lock > 0: s.aim_lock -= 1
    # PlayerSimState.Swinging: any phase but None, and None is 0.
    if s.aim_lock > 0 or getattr(s, "attack", 0): return

    if wx: s.facing = wx


def lane_ahead(s, w):
    if C["LOOKAHEAD"] <= 0: return None
    half = C["W"] // 2
    limit = max(C["CLAMBER"], C["STEP"])
    frm = s.x + half if s.travel > 0 else s.x - half - C["LOOKAHEAD"]
    to = s.x + half + C["LOOKAHEAD"] if s.travel > 0 else s.x - half
    best = None; near = 0
    for i, sol in enumerate(w["solid"]):
        if not solid_live(w, i): continue          # a shutter that has not shut
        if sol[2] <= frm or sol[0] >= to: continue
        step_h = sol[3] - s.y
        if step_h <= 0 or step_h > limit: continue
        # Strictly ahead: a box whose X already surrounds the body is not somewhere it is
        # going, and the wall at the back runs the length of the arena.
        gap = sol[0] - (s.x + half) if s.travel > 0 else (s.x - half) - sol[2]
        if gap < 0: continue
        if best is not None and gap >= near: continue
        want = lane_on(span(w, "solid", i), s.depth)
        if abs(want - s.depth) > C["LANEREACH"]: continue
        near = gap
        best = want
    # Ladders deliberately do NOT steer the lane: the grab only asks that the rungs be
    # within the lane reach, and the mount sets the depth itself. Steering only ever moved
    # a fighter who had not asked to move.
    return best


def lane_on(sp, depth):
    """Which slice of a box to stand on: the gameplay plane whenever the box reaches it, and
    otherwise the nearest part of the box to where the body already is.

    The plane is where the fight happens. Lanes exist so a fighter can step back onto a ramp
    or a walkway modelled behind it, not so he can live there — and a floor wide enough to
    hold both lanes used to hold him on whichever one he arrived with. Drop off the upper
    walkway and you landed on the ground floor still on the walkway's slice, a metre behind
    your opponent, and nothing either of you did would connect for the rest of the round."""
    halfz = C["BODYZ"] // 2
    home = C["HOME"]
    return nearest_in(sp, home, halfz) if reaches(sp, home, halfz) \
        else nearest_in(sp, depth, halfz)

def floor_lane(s, w):
    half = C["W"] // 2; halfz = C["BODYZ"] // 2
    feet = (s.x - half + SKIN, s.y - GROUND_PROBE, s.x + half - SKIN, s.y)
    best = None; top = 0
    for i, sol in enumerate(w["solid"]):
        if not overlaps(feet, sol): continue
        if not solid_at(w, i, s.depth, halfz): continue
        if best is not None and sol[3] <= top: continue
        top = sol[3]
        best = lane_on(span(w, "solid", i), s.depth)
    return best


def step_flat(s, inp, w):
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
    aim(s, inp, wx)

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
        d = wx or s.travel or s.facing
        s.travel = d; s.vx = C["DODGE"] * d; s.vy = 0; s.noise = 2
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
        if any_solid(w, stand, s.depth): want_crouch = True
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

    now = grounded(s, w)
    # Walking off the edge of a step is not falling. Every ramp here is a staircase, so walking
    # down one left the floor ten times a second: the fall animation strobed and the descent
    # arrived in lurches. `g` is already False on the frame a jump fires, so this can never glue
    # a jump back down.
    if not now and g and s.vy <= 0 and s.fallthru == 0 and step_down(s, w):
        now = True

    if now:
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

def step_down(s, w):
    """Puts the feet on the next floor down when it is within one step of them. False when
    there is nothing that close, which is the difference between a step and a drop."""
    if C["STEP"] <= 0: return False
    g = ground_below(s.x, s.y, C["STEP"], w, s.depth)
    if g is None or g >= s.y: return False
    s.y = g; s.vy = 0
    return True

def ladder_depth_at(w, i, y, fallback, inset):
    """The depth of a ladder's centre line at a height — where the rungs are. Not the nearest
    point inside its depth span: a ladder that leans has a span as deep as the whole lean, so
    "stay somewhere inside it" lets the body hang a metre off the rungs at one end."""
    if i < 0 or i >= len(w["ladder"]): return fallback
    leans = w.get("ladderleanz") or []
    if i >= len(leans): return nearest_in(span(w, "ladder", i), fallback, inset)
    return lean_at(leans[i], w["ladder"][i], y)

def lean_at(lean, box, y):
    """Where a lean line stands at a height, clamped to the box's own ends."""
    height = box[3] - box[1]
    if height <= 0: return lean[0]
    t = max(0, min(height, y - box[1]))
    return lean[0] + (lean[1] - lean[0]) * t // height

def ladder_centre(w, i, y=None):
    """The middle of a ladder at a given height. Upright that is the middle of its box;
    leaning, it walks across as you climb."""
    l = w["ladder"][i]
    lean = w.get("ladderlean")
    if y is None or not lean or i >= len(lean):
        return l[0] + (l[2] - l[0]) // 2
    return lean_at(lean[i], l, y)


def lane_for(sp, depth):
    """Can a body on this slice get onto that box by changing lane, and which slice would it
    stand on. Nothing in a location modelled in 3D lines its floors up: the ladder stands in
    FRONT of the walkway it serves, so a climb-out that insists on one slice finds nothing and
    leaves the fighter hanging at the top. LANEREACH bounds it, so this is a step back onto the
    next floor, never a teleport into the scenery behind it. Returns None when out of reach."""
    half = C["BODYZ"] // 2
    if reaches(sp, depth, half): return depth
    lane = nearest_in(sp, depth, half)
    return lane if C["LANEREACH"] > 0 and abs(lane - depth) <= C["LANEREACH"] else None

def ladder_at(s, w):
    """The ladder the body touches, on its own slice or one lane away; nearest lane wins."""
    b = body(s)
    best, best_shift = -1, 0
    for i, l in enumerate(w["ladder"]):
        if not overlaps(b, l): continue
        lane = lane_for(span(w, "ladder", i), s.depth)
        if lane is None: continue
        shift = abs(lane - s.depth)
        if best >= 0 and shift >= best_shift: continue
        best, best_shift = i, shift
    return best

def frames_for(dist):
    if dist <= M(20): return 0
    if C["SSPD"] <= 0: return C["SMAXF"]
    f = (((dist << 16) // C["SSPD"]) * 60 >> 16) + 1
    return max(C["SMINF"], min(C["SMAXF"], f))

def ladder_under_feet(s, w):
    """The ladder a standing body is on top of: it reaches the feet from below and runs
    under them. This is how you get onto a ladder that has no hatch beside it."""
    half = C["W"]//2; reach = C["H"]//2; lip = C["STEP"]
    for i, l in enumerate(w["ladder"]):
        if lane_for(span(w, "ladder", i), s.depth) is None: continue
        if l[0] >= s.x+half or l[2] <= s.x-half: continue
        drop = s.y - l[3]
        # A free step of slack upwards: a ladder modelled 2 cm proud of its landing is still
        # the ladder you are standing on, and with no slack it could not be climbed down.
        if drop < -lip or drop > reach: continue
        return i
    return -1

def try_mount(s, wy, w):
    if not wy: return False
    if s.ladder_cd > 0: return False
    if s.crouch and any_solid(w, (s.x - C["W"]//2, s.y, s.x + C["W"]//2, s.y + C["H"]), s.depth):
        return False
    i = ladder_at(s, w)
    # Standing on top of a ladder whose rungs start just under the feet: stepping over the
    # edge IS the way down when there is no hatch beside it.
    # Judged on where the ladder's HEAD is, not on which search found it: a ladder the reach
    # search had already picked could never be one you were standing on, so a ladder modelled
    # 2 cm proud of its landing was found by that search and then refused for having floor
    # underneath, while the flush one beside it missed the search and worked.
    stepping_on = False
    if wy < 0 and s.mode == "ground":
        if i < 0:
            i = ladder_under_feet(s, w); stepping_on = i >= 0
        else:
            stepping_on = w["ladder"][i][3] <= s.y + C["STEP"]
    if i < 0: return False
    box = w["ladder"][i]
    centre = ladder_centre(w, i, s.y)

    if wy > 0:
        if s.y >= box[3] - C["LTOP"] - C["H"]//4: return False
    else:
        if s.mode == "ground" and box[1] >= s.y and not stepping_on: return False
        if not stepping_on and support_under(centre, s.y, s.fallthru, w, -1, s.depth):
            return False

    target = centre if sweep_free(s.x, centre, s.y, w, s.depth) else s.x
    target_y = box[3] - C["LTOP"] if stepping_on else s.y
    frames = frames_for(max(abs(target - s.x), abs(target_y - s.y)))

    s.ladder = i; s.crouch = False; s.vx = s.vy = 0
    s.script_dir = 1 if wy > 0 else -1
    if frames <= 0:
        s.x = target; s.y = target_y; s.mode = "ladder"
        return True
    s.script_from = (s.x, s.y); s.script_to = (target, target_y)
    s.script_forward = False
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
        # The climb-out owns the slice for its whole length, so it finishes on the one it
        # promised. A mount does not: update_depth is already drawing the body onto the
        # ladder's own slice while the reach plays.
        if s.mode == "mantle": s.depth = s.script_to_depth
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
    # Depth rides with the height: the fighter leans back onto the next floor as they come up
    # over its edge.
    if s.mode == "mantle":
        s.depth = s.script_from_depth + mul(s.script_to_depth - s.script_from_depth, yT)

def supported(x, g, w, depth=None):
    half=C["W"]//2
    for k in (-1,0,1):
        px=x+half*k
        if not any_solid(w,(px-SKIN*4, g-GROUND_PROBE, px+SKIN*4, g-SKIN), depth): return False
    return True

def floor_run(frm, d, w, depth=None):
    step=M(200); reached=0
    for i in range(1,26):
        x=frm[0]+step*(i*d)
        g=ground_below(x, frm[1]+C["H"], C["H"]*2, w, depth)
        if g is None or not supported(x,g,w,depth): break
        reached=step*i
    return reached

def find_landing(s, side, w):
    half=C["W"]//2; step=M(100); steps=C["MREACH"]//step
    for i in range(1,steps+1):
        x=s.x+step*(i*side)
        g=ground_below(x, s.y+C["H"], C["H"]*2, w, s.depth)
        if g is None: continue
        if g < s.y - C["LTOP"]*4: continue
        if any_solid(w,(x-half, g+SKIN, x+half, g+C["H"]), s.depth): continue
        if not supported(x,g,w,s.depth): continue
        return (x, g+SKIN)
    return None

def step_out(s, ladder, w):
    """The floor a ladder ENDS INSIDE, which is the one it serves. None for a hatch ladder:
    a hatch is a hole, so there is nothing overhead to step onto and the sideways search
    takes over on its own."""
    if not (0 <= ladder < len(w["ladder"])): return None
    l = w["ladder"][ladder]; half = C["W"]//2
    top, lane = None, s.depth
    for i, sol in enumerate(w["solid"]):
        if not solid_live(w, i): continue          # a shutter that has not shut
        if sol[0] >= s.x+half or sol[2] <= s.x-half: continue
        if l[3] < sol[1] or l[3] > sol[3] + C["LTOP"]: continue
        # The landing may be a lane behind the rungs — here it always is.
        cand = lane_for(span(w, "solid", i), s.depth)
        if cand is None: continue
        # And the lane a body STANDING there is held on, not merely one it can reach.
        # Two different rules, and they disagreed on every ladder here: lane_for answers
        # "nearest part of that floor to where you are", which off a ladder is its front
        # edge, while floor_lane holds a standing body on the gameplay plane whenever the
        # floor reaches it. The climb-out finished, the body stopped, and update_depth then
        # pulled it 0.41-0.49 m back at the full 4 m/s -- the lurch at the landing.
        settled = lane_on(span(w, "solid", i), s.depth)
        if abs(settled - s.depth) <= C["LANEREACH"]: cand = settled
        if top is not None and sol[3] <= top: continue
        # Both checks only rule THIS candidate out: taking the highest floor and giving up if it
        # did not work let one awkward box over a ladder's head cancel the climb-out.
        if any_solid(w, (s.x-half, sol[3]+SKIN, s.x+half, sol[3]+C["H"]), cand): continue
        if not supported(s.x, sol[3], w, cand): continue
        top, lane = sol[3], cand
    if top is None: return None
    return (s.x, top+SKIN, lane)

def start_scripted(s, landing, side, to_depth=None):
    s.script_from=(s.x,s.y); s.script_to=(landing[0], landing[1])
    s.script_from_depth = s.depth
    s.script_to_depth = s.depth if to_depth is None else to_depth
    # A climb-out that covers no ground sideways is a step FORWARD onto the floor the rungs
    # end at. Presentation keeps the body facing the ladder through one of those.
    s.script_forward = abs(landing[0] - s.x) <= C["LTOP"]
    span_x = abs(s.script_to[0]-s.x)*10//7  # noqa: the tuple is (x, y) from here on
    span_y = abs(s.script_to[1]-s.y)*3//2
    # DEPTH counts. Every ladder here stands in front of the floor it serves with its head
    # flush to it, so a climb-out raises the body ~4 cm and moves it ~45 cm backwards. Timed
    # on x and y alone that is 4 cm of travel, which lands on the ten-frame minimum: a sixth
    # of a second for 0.45 m, faster than a run, with an 0.83 s haul clip crammed into it.
    span_z = abs(s.script_to_depth - s.script_from_depth)*3//2
    frames = frames_for(max(span_z, span_x, span_y)) or C["SMINF"]
    # And a haul is not a step: the minimum it shared with the reach onto a ladder is too
    # short to read as one.
    frames = max(frames, C["MANTLE_MINF"])
    s.script_frames=frames; s.script_t=frames
    s.script_dir = 1 if side>=0 else -1
    s.mode="mantle"; s.ladder=-1
    s.vx=s.vy=0; s.facing = 1 if side>=0 else -1
    s.travel = s.facing; s.aim_lock = 0

def try_mantle(s, prefer, ladder, w):
    up = step_out(s, ladder, w)
    if up is not None:
        start_scripted(s, up, s.travel, up[2]); return True
    r=find_landing(s,1,w); l=find_landing(s,-1,w)
    if r is None and l is None: return False
    if prefer>0 and r: side=1
    elif prefer<0 and l: side=-1
    elif l is None: side=1
    elif r is None: side=-1
    else: side = 1 if floor_run(r,1,w,s.depth) >= floor_run(l,-1,w,s.depth) else -1
    # A sideways climb-out stays on its own slice: a step along this floor, not onto the next.
    start_scripted(s, r if side>0 else l, side, s.depth)
    return True

def ground_below(x, y, maxd, w, depth):
    """The floor under a body on that body's own slice. Depth is not optional: searching every
    baked box seated the spawn on the backdrop, which stands taller than the platform in front
    of it, and the match opened with the fighter falling through the floor."""
    half = C["W"]//2
    halfz = C["BODYZ"]//2
    best = None
    for i, sol in enumerate(w["solid"]):
        if not solid_at(w, i, depth, halfz): continue
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
    centre = ladder_centre(w, s.ladder, s.y)
    # Every frame: nothing else moves X while climbing, and on a leaning ladder the centre line
    # walks sideways as the body rises, so it is a line to follow rather than a point to sit on.
    # The floors this ladder runs up into are exempt, as they are for the climb itself: on the
    # last metre of a ladder the body's head is inside the landing it is about to stand on, and
    # counting that as a wall froze the pull exactly where a leaning ladder needs it most.
    want = move_towards(s.x, centre, mul(C["LSNAP"], DT))
    if sweep_free(s.x, want, s.y, w, s.depth, s.ladder): s.x = want
    s.vx = 0
    s.vy = C["CUP"] if wy > 0 else (-C["CDN"] if wy < 0 else 0)
    move_y(s, mul(s.vy, DT), w, s.ladder)
    ceiling = l[3] - C["LTOP"]
    at_top = s.y >= ceiling
    if at_top:
        s.y = ceiling
        if s.vy > 0: s.vy = 0
    # A direction on its own counts as asking to get out, not only up: edging off the rungs
    # instead walks the body out over the hole it came up through.
    if at_top and (wy > 0 or wx) and try_mantle(s, wx, s.ladder, w):
        return
    if wy < 0 and grounded(s, w, s.ladder):
        s.ladder = -1; s.ladder_cd = C["LREGRAB"]
        s.mode = "ground"; s.vx = s.vy = 0; s.noise = 1
        return
    # A direction with the climb let go of asks to get off on that side, and the climb-out is
    # the move that does it: floor within reach at or above the feet, or the fighter stays on the
    # rungs. Nothing edges the body sideways off them any more — that walked it out over the hole
    # it had just come up through.
    if wx and not wy:
        land = find_landing(s, wx, w)
        if land:
            start_scripted(s, land, wx)
            return
    if wy and s.stepn == 0: s.noise = 1; s.stepn = C["STEPN"]
    b = body(s)
    if not overlaps(b, l):
        s.ladder = -1
        s.mode = "ground" if grounded(s, w) else "air"
        return

