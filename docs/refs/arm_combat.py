"""The arm model, on top of the movement mirror's fighter.

arm_mirror has its own tiny fighter and is the right place to argue about the DESIGN --
does a carried blade hurt, what is the time to kill. This module is the other thing a
mirror is for: running the same combat on the full movement state (sim_motor_mirror.S),
so the bot can be tested while walking around a real arena.

It replaces combat_mirror, which modelled the windup/active/recovery machine that
docs/31 deleted. A mirror of code that no longer exists is worse than no mirror: its
tests go green and say the game works.
"""
import fixgeom_mirror as F
from sim_motor_mirror import C, ONE
from loot_mirror import ITEMS, NONE, FISTS

ARM = dict(
    shoulder=780, arm_length_pm=350,
    sweep_frames=15, reference_frames=15,
    deadzone=220, min_swing=250, carry_drop=700,
    aim_relative=1000, aim_floor_pm=500, aim_tilt_pm=1000,
    haft_pm=270, reach_to_blade_pm=730, min_blade_pm=310, max_blade_pm=1430,
    body_radius_pm=180, blade_radius_pm=60,
    hit_lock=18, clash_deflect=700, clash_lock=10,
)

K = dict(
    hp=100, stun_per_dmg=1, max_stun=34, hit_advantage=4,
    combo_decay=620, combo_stack=75, max_armour=500,
    hit_stop=3, hit_stop_per_dmg=330, max_hit_stop=10,
    desperate_hp=300, desperate_dmg=1400, sudden_bare=2000,
)

PI = F.milli(3142)

# This file used to carry a SCALE factor, because sim_motor_mirror's fighter is 0.91 m
# and MoveConfig's defaults said 1.8, and the arm model makes every length geometric.
# It is gone: MoveConfig now describes the fighter in the prefabs, every length in the
# arm is a share of his height, and the two mirrors agree without a fudge between them.


def aim_of(x_permille, y_permille):
    """Thousandths to fixed point, the way TickInput.Aim does it in the C#.

    The stick is carried as two signed thousandths all the way from the input layer --
    quantised once, at the edge, so rollback replays the same number. The simulation
    wants a FixVec2. Forgetting this conversion is silent and total: 1000 read as a raw
    Fix is 0.015, which is inside every deadzone, so the arm simply never leaves its
    carry pose and nothing ever reports an error.
    """
    return (F.milli(x_permille), F.milli(y_permille))


def W(s):
    return ITEMS[FISTS if s.inv is None or s.inv.weapon == NONE else s.inv.weapon]


def init(s):
    s.health = K["hp"]
    s.dead = False
    s.stun = s.stagger = s.hitstop = 0
    # Carried low, not held level: level is the old rest pose docs/31 removed, and
    # spawning into it opens every round holding a free perfect guard.
    s.arm = carry(s)
    s.tip_prev = (0, 0)
    s.aim_prev = (0, 0)
    s.assist_dir = (0, 0)
    s.assist_held = False
    s.tip_primed = False
    s.tip_speed = 0
    s.swing_share = 0
    s.hit_lock = s.clash_lock = 0
    s.was_clash = False
    s.dealt = 0
    s.stun_stack = 0
    s.stun_stack_t = 0
    s.was_hit = s.was_parried = s.just_died = False
    s.damage_taken = 0


def body_height(s):
    return C["CH"] if s.crouch else C["H"]


def assist(s, aim, foe):
    """ArmMotor.Assist: the stick read in the other fighter's frame, not the world's.

    This file used to take `aim` as a world direction and skip the frame entirely, on
    the grounds that it is about whether the BOT behaves. That was the gap that hid a
    real bug for a whole pass: the bot aims in world terms (AimX = towards * 1000) and
    the frame then turns that by the SAME direction, so the two cancelled and a bot
    standing to its foe's right guarded to its right -- away from him -- while its high
    swings came out low. The mirror could not see it because the mirror had no frame.
    """
    want = normalized(aim)

    moved = F.length_sq(F.sub(aim, s.aim_prev))
    if s.assist_held and moved <= F.mul(F.milli(20), F.milli(20)):
        return s.assist_dir

    s.aim_prev = aim
    s.assist_held = True
    s.assist_dir = want

    if foe is None or ARM["aim_relative"] <= 0:
        return want

    mine = shoulder(s)
    dx = shoulder(foe)[0] - mine[0]
    dy = shoulder(foe)[1] - mine[1]

    near = F.mul(C["H"], F.milli(ARM["aim_floor_pm"]))
    if abs(dx) < near:
        dx = near * (1 if dx > 0 else (-1 if dx < 0 else (s.facing or 1)))

    lean = F.mul(abs(dx), F.milli(ARM["aim_tilt_pm"]))
    dy = max(-lean, min(lean, dy))

    to_foe = normalized((dx, dy))
    if F.length_sq(to_foe) == 0:
        return want

    ahead = ((s.facing or 1) * ONE, 0)
    axis = normalized(F.add(ahead, F.scale(F.sub(to_foe, ahead),
                                           F.milli(ARM["aim_relative"]))))
    if F.length_sq(axis) == 0:
        return want

    turned = (F.mul(want[0], axis[0]) - F.mul(want[1], axis[1]),
              F.mul(want[0], axis[1]) + F.mul(want[1], axis[0]))
    if F.length_sq(turned) != 0:
        s.assist_dir = normalized(turned)
    return s.assist_dir


def shoulder(s):
    return (s.x, s.y + F.mul(body_height(s), F.milli(ARM["shoulder"])))


def arm_length():
    return F.mul(C["H"], F.milli(ARM["arm_length_pm"]))


def reach(s):
    """What the ITEM says, which is reach from the middle of the body."""
    return W(s)["reach"]


def blade(s):
    """What hangs past the hand: the weapon that is DRAWN. ArmMotor.BladeLength."""
    bare = ITEMS[FISTS]["reach"]
    r = reach(s)
    if r <= bare:
        return 0
    want = (F.mul(C["H"], F.milli(ARM["haft_pm"]))
            + F.mul(r - bare, F.milli(ARM["reach_to_blade_pm"])))
    return max(F.mul(C["H"], F.milli(ARM["min_blade_pm"])),
               min(F.mul(C["H"], F.milli(ARM["max_blade_pm"])), want))


def hand(s):
    return F.add(shoulder(s), F.scale(s.arm, arm_length()))


def tip(s):
    return F.add(shoulder(s), F.scale(s.arm, arm_length() + blade(s)))


def body_segment(s):
    half = F.cdiv(C["W"], 2)
    return (s.x, s.y + half), (s.x, s.y + body_height(s) - half)


def slew(s):
    frames = max(1, W(s)["frames"])
    ticks = max(1, ARM["sweep_frames"] * frames // ARM["reference_frames"])
    return F.cdiv(PI, ticks)


def top_tip_speed(s):
    return F.mul(slew(s), arm_length() + blade(s))


def normalized(v):
    ln = F.length(v)
    return (0, 0) if ln == 0 else (F.div(v[0], ln), F.div(v[1], ln))


def carry(s):
    return normalized(((s.facing or 1) * ONE, -F.milli(ARM["carry_drop"])))


def swing_permille(s):
    top = top_tip_speed(s)
    if top <= 0:
        return 0
    share = s.tip_speed * 1000 // top
    return 0 if share <= ARM["min_swing"] else min(1000, share)


def combat_step(s, inp, aim=(0, 0), foe=None):
    """CombatMotor.Step then ArmMotor.Step. Returns the input the motor may act on."""
    s.was_hit = s.was_parried = s.just_died = False
    s.damage_taken = 0

    if s.hitstop > 0:
        return 0

    if s.stun > 0:
        s.stun -= 1
    if s.stagger > 0:
        s.stagger -= 1
    if s.stun_stack_t > 0:
        s.stun_stack_t -= 1
        if s.stun_stack_t == 0:
            s.stun_stack = 0

    if s.dead:
        return 0

    # The arm, every tick, whatever else is happening.
    push = F.length_sq(aim)
    dead = F.milli(ARM["deadzone"])
    if push > F.mul(dead, dead):
        want = assist(s, aim, foe)
    else:
        want = carry(s)
        s.assist_held = False
    if s.dead or s.hitstop > 0 or s.stun > 0 or s.stagger > 0:
        want = carry(s)

    if F.length_sq(s.arm) == 0:
        s.arm = ((s.facing or 1) * ONE, 0)

    delta = F.sub(want, s.arm)
    mx = slew(s)
    if F.length_sq(delta) > F.mul(mx, mx):
        delta = F.with_length(delta, mx)
    s.arm = normalized(F.add(s.arm, delta))
    if F.length_sq(s.arm) == 0:
        s.arm = ((s.facing or 1) * ONE, 0)

    local = F.scale(s.arm, arm_length() + blade(s))
    if not s.tip_primed:
        s.tip_prev = local
        s.tip_primed = True
    s.tip_speed = F.length(F.sub(local, s.tip_prev))
    s.tip_prev = local
    s.swing_share = swing_permille(s)

    if s.hit_lock > 0:
        s.hit_lock -= 1
    if s.clash_lock > 0:
        s.clash_lock -= 1

    if s.stun > 0 or s.stagger > 0:
        return 0

    # The speed cap follows how committed the swing is, continuously.
    share = s.swing_share
    SLOWEST = 180
    s.speed_permille = 0 if share <= 0 else max(
        SLOWEST, 1000 - (1000 - SLOWEST) * share // 1000)
    return inp


def resolve(players, sudden_death=False):
    for s in players:
        s.was_clash = False
        s.dealt = 0

    for i in range(len(players)):
        for j in range(i + 1, len(players)):
            _clash(players[i], players[j])

    for i in range(len(players)):
        for j in range(len(players)):
            if i != j:
                _strike(players[i], players[j], sudden_death)


def _clash(a, b):
    if a.dead or b.dead:
        return
    touch = F.mul(C["H"], F.milli(ARM["blade_radius_pm"])) * 2
    touching = F.segment_distance_sq(hand(a), tip(a), hand(b), tip(b)) <= F.mul(touch, touch)
    a.in_contact = b.in_contact = touching
    if not touching:
        return
    if swing_permille(a) <= 0 and swing_permille(b) <= 0:
        return
    if a.clash_lock > 0 or b.clash_lock > 0:
        return

    a.was_clash = b.was_clash = True
    a.was_parried = b.was_parried = True
    a.clash_lock = ARM["clash_lock"]
    b.clash_lock = ARM["clash_lock"]

    diff = a.tip_speed - b.tip_speed
    if diff == 0:
        return
    shove = F.mul(abs(diff), F.milli(ARM["clash_deflect"]))
    loser, winner = (b, a) if diff > 0 else (a, b)

    side = (-winner.arm[1], winner.arm[0])
    if F.dot(side, loser.arm) > 0:
        side = (-side[0], -side[1])
    loser.arm = normalized(F.add(loser.arm, F.scale(side, shove)))
    if F.length_sq(loser.arm) == 0:
        loser.arm = ((loser.facing or 1) * ONE, 0)
    loser.clash_lock = ARM["clash_lock"]
    loser.tip_speed = 0
    # The deflection must not read as a swing of theirs next tick.
    loser.tip_prev = F.scale(loser.arm, arm_length() + blade(loser))


def _strike(att, vic, sudden):
    if att.dead or vic.dead:
        return
    if att.hit_lock > 0 or att.clash_lock > 0 or att.hitstop > 0:
        return
    if att.dodge > 0 or vic.dodge > 0:
        return

    share = swing_permille(att)
    if share <= 0:
        return

    feet, head = body_segment(vic)
    radius = F.mul(C["H"], F.milli(ARM["body_radius_pm"]))
    if F.segment_distance_sq(hand(att), tip(att), feet, head) > F.mul(radius, radius):
        return

    att.dealt = share
    # One hit per ARC, and the arc is this weapon's own.
    att.hit_lock = max(ARM["hit_lock"], W(att)["frames"])

    dealt = F.mul(F.from_f(W(att)["dmg"]), F.milli(share))

    bare = att.inv is None or att.inv.weapon in (NONE, FISTS)
    if bare:
        boost = 1000
        if att.health * 1000 <= K["hp"] * K["desperate_hp"]:
            boost = K["desperate_dmg"]
        if sudden and K["sudden_bare"] > boost:
            boost = K["sudden_bare"]
        if boost != 1000:
            dealt = F.mul(dealt, F.milli(boost))

    damage = max(1, (dealt + (ONE >> 1)) >> 16)

    armour = min(vic.inv.reduction if vic.inv else 0, K["max_armour"])
    if armour > 0:
        damage = max(1, damage * (1000 - armour) // 1000)

    frames = min(K["max_hit_stop"],
                 K["hit_stop"] + damage * K["hit_stop_per_dmg"] // 1000)
    att.hitstop = max(att.hitstop, frames)
    vic.hitstop = max(vic.hitstop, frames)

    vic.health -= damage
    vic.was_hit = True
    vic.damage_taken = damage
    if vic.health <= 0:
        vic.health = 0
        vic.dead = True
        vic.just_died = True

    stun = damage * K["stun_per_dmg"]
    if vic.stun_stack == 0:
        stun = max(stun, ARM["hit_lock"] + K["hit_advantage"])
    else:
        for _ in range(vic.stun_stack):
            if stun <= 0:
                break
            stun = stun * K["combo_decay"] // 1000
    vic.stun = max(1, min(stun, K["max_stun"]))
    vic.stun_stack += 1
    vic.stun_stack_t = K["combo_stack"]
    vic.tip_speed = 0
    vic.swing_share = 0
    vic.hit_lock = 0
