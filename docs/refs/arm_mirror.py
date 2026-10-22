"""Bit-exact port of ArmConfig, ArmMotor and the contact half of CombatMotor.

Direct-aim combat (docs/31). The arm is a unit vector off the shoulder, the weapon is a
segment hanging off the end of it, and damage is the tip MOVING into somebody. Everything
here mirrors the C# operation for operation, using fixgeom_mirror for the arithmetic, so
a test can ask the two questions that matter:

  * does the design actually work -- is a carried blade harmless, does a heavy weapon
    cost more to swing, does holding your weapon up stop theirs, is the time to kill
    anywhere near what docs/02 asks for;
  * and does it work in FIXED POINT, which is the only arithmetic the real game has.
"""
import fixgeom_mirror as F

ONE = F.ONE
PI = F.milli(3142)

# ----------------------------------------------------------------- the weapons
# name: (AttackFrames, damage, reach) straight out of ItemTable.
WEAPONS = {
    "fists":       (7,  4.0, 0.60),
    "dagger":      (9,  5.0, 0.70),
    "gladius":     (12, 7.0, 0.90),
    "saber":       (14, 9.0, 1.10),
    "club":        (15, 8.0, 0.95),
    "mace":        (18, 9.0, 0.85),
    "sword":       (19, 10.0, 1.15),
    "flangedmace": (21, 12.0, 1.30),
    "axe":         (23, 12.0, 1.05),
    "spear":       (27, 13.0, 1.70),
    "scythe":      (29, 11.0, 1.85),
    "warhammer":   (34, 21.0, 1.40),
}

# ----------------------------------------------------------------- ArmConfig.Default
CFG = dict(
    shoulder=780, arm_length=F.milli(450),
    sweep_frames=15, reference_frames=15,
    deadzone=220, min_swing=250, carry_drop=700,
    aim_relative=1000,
    body_radius=F.milli(300), blade_radius=F.milli(70),
    hit_lock=18, clash_deflect=700, clash_lock=10,
)

# CombatConfig, the parts contact combat still uses
COMBAT = dict(
    max_health=100, hitstun_per_damage=1, max_hitstun=34, hit_advantage=4,
    combo_decay=620, combo_stack_frames=75, max_armour=500,
    hit_stop=3, hit_stop_per_damage=330, max_hit_stop=10,
    desperate_hp=300, desperate_dmg=1400, sudden_bare=2000,
)

BODY_HEIGHT = F.milli(1800)
BODY_WIDTH = F.milli(600)
RUN_SPEED = F.milli(3600)


class Fighter:
    def __init__(self, x=0.0, facing=1, weapon="fists"):
        self.x = F.from_f(x)
        self.y = 0
        self.facing = facing
        self.weapon = weapon
        self.arm = (facing * ONE, 0)
        self.tip_prev = (0, 0)
        self.tip_primed = False
        self.aim_prev = (0, 0)
        self.assist_dir = (0, 0)
        self.assist_held = False
        self.tip_speed = 0
        self.swing_share = 0
        self.hit_lock = 0
        self.clash_lock = 0
        self.was_clash = False
        self.dealt = 0
        self.health = COMBAT["max_health"]
        self.dead = False
        self.hitstun = 0
        self.stagger = 0
        self.hitstop = 0
        self.stun_stack = 0
        self.stun_stack_timer = 0
        self.dodge = 0
        self.armour = 0
        self.was_hit = False
        self.damage_taken = 0

    # ---- geometry
    def shoulder(self):
        return (self.x, self.y + F.mul(BODY_HEIGHT, F.milli(CFG["shoulder"])))

    def reach(self):
        return F.milli(int(round(WEAPONS[self.weapon][2] * 1000)))

    def hand(self):
        return F.add(self.shoulder(), F.scale(self.arm, CFG["arm_length"]))

    def tip(self):
        return F.add(self.shoulder(),
                     F.scale(self.arm, CFG["arm_length"] + self.reach()))

    def body_segment(self):
        half = F.cdiv(BODY_WIDTH, 2)
        return (self.x, self.y + half), (self.x, self.y + BODY_HEIGHT - half)

    def slew(self):
        frames = max(1, WEAPONS[self.weapon][0])
        ticks = max(1, CFG["sweep_frames"] * frames // CFG["reference_frames"])
        return F.cdiv(PI, ticks)

    def top_tip_speed(self):
        return F.mul(self.slew(), CFG["arm_length"] + self.reach())


def clamp_length(v, mx):
    sq = F.length_sq(v)
    return v if sq <= F.mul(mx, mx) else F.with_length(v, mx)


def normalized(v):
    ln = F.length(v)
    if ln == 0:
        return (0, 0)
    return (F.div(v[0], ln), F.div(v[1], ln))


def carry(f):
    """The rest pose: blade low, not a free guard."""
    return normalized(((f.facing or 1) * ONE, -F.milli(CFG["carry_drop"])))


def chest(f):
    return (f.x, f.y + F.cdiv(BODY_HEIGHT, 2))


def assist(f, aim, foe):
    """Read the stick in the other fighter's frame rather than the world's.

    Forward means AT HIM, up means over him, down means under him. Nothing is steered --
    the direction asked for is kept, only measured from a different zero -- so a guard
    keeps all of its value, which a cone-and-pull version did not.

    Re-framed only when the thumb MOVES. Doing it every tick let the frame rotate under a
    held stick as you walked in, and a moving arm is damage: every weapon could kill by
    walking forward.
    """
    want = normalized(aim)
    moved = F.length_sq(F.sub(aim, f.aim_prev))
    steady = f.assist_held and moved <= F.mul(F.milli(20), F.milli(20))
    f.aim_prev = aim
    if steady:
        return f.assist_dir

    f.assist_held = True
    f.assist_dir = want

    if foe is None or CFG["aim_relative"] <= 0:
        return want
    to_foe = normalized(F.sub(chest(foe), f.shoulder()))
    if F.length_sq(to_foe) == 0:
        return want

    # The frame's forward axis, blended back toward the facing by whatever world is kept.
    ahead = ((f.facing or 1) * ONE, 0)
    axis = normalized(F.add(ahead, F.scale(F.sub(to_foe, ahead), F.milli(CFG["aim_relative"]))))
    if F.length_sq(axis) == 0:
        return want

    # Rotating by the angle of a unit vector is one complex multiply.
    turned = (F.mul(want[0], axis[0]) - F.mul(want[1], axis[1]),
              F.mul(want[0], axis[1]) + F.mul(want[1], axis[0]))
    if F.length_sq(turned) != 0:
        f.assist_dir = normalized(turned)
    return f.assist_dir


def step(f, aim, foe=None):
    """ArmMotor.Step."""
    push = F.length_sq(aim)
    dead = F.milli(CFG["deadzone"])
    if push > F.mul(dead, dead):
        want = assist(f, aim, foe)
    else:
        want = carry(f)
        f.assist_held = False

    if f.dead or f.hitstop > 0 or f.hitstun > 0 or f.stagger > 0:
        want = carry(f)

    if F.length_sq(f.arm) == 0:
        f.arm = ((f.facing or 1) * ONE, 0)

    delta = clamp_length(F.sub(want, f.arm), f.slew())
    f.arm = normalized(F.add(f.arm, delta))
    if F.length_sq(f.arm) == 0:
        f.arm = ((f.facing or 1) * ONE, 0)

    # Tip speed in the fighter's OWN frame: how far it moved around the shoulder.
    local = F.scale(f.arm, CFG["arm_length"] + f.reach())
    if not f.tip_primed:
        f.tip_prev = local
        f.tip_primed = True

    f.tip_speed = F.length(F.sub(local, f.tip_prev))
    f.tip_prev = local
    f.swing_share = swing_permille(f)

    if f.hit_lock > 0:
        f.hit_lock -= 1
    if f.clash_lock > 0:
        f.clash_lock -= 1


def swing_permille(f):
    top = f.top_tip_speed()
    if top <= 0:
        return 0
    share = f.tip_speed * 1000 // top
    if share <= CFG["min_swing"]:
        return 0
    return min(1000, share)


def desperate(f, damage, sudden):
    """damage is a Fix here, not an int: see the C# comment on rounding."""
    """Bare hands get desperate (docs/02); in sudden death they hurt anyway (docs/01).

    The two do NOT multiply -- the bigger wins. Stacked, a punch from somebody on their
    last tenth of health reads as a one-hit kill out of nowhere.
    """
    if f.weapon != "fists":
        return damage
    boost = 1000
    if f.health * 1000 <= COMBAT["max_health"] * COMBAT["desperate_hp"]:
        boost = COMBAT["desperate_dmg"]
    if sudden and COMBAT["sudden_bare"] > boost:
        boost = COMBAT["sudden_bare"]
    return damage if boost == 1000 else F.mul(damage, F.milli(boost))


def resolve(fighters, sudden=False):
    """ArmMotor.Resolve: clashes first, then bodies."""
    for f in fighters:
        f.was_clash = False
        f.dealt = 0
        f.was_hit = False
        f.damage_taken = 0

    for i in range(len(fighters)):
        for j in range(i + 1, len(fighters)):
            clash(fighters[i], fighters[j])

    for i in range(len(fighters)):
        for j in range(len(fighters)):
            if i != j:
                strike(fighters[i], fighters[j], sudden)


def clash(a, b):
    if a.dead or b.dead:
        return
    touch = CFG["blade_radius"] * 2
    touching = F.segment_distance_sq(a.hand(), a.tip(), b.hand(), b.tip()) <= F.mul(touch, touch)
    a.in_contact = b.in_contact = touching
    if not touching:
        return
    # Somebody has to be SWINGING -- resting blades have no energy, and that is what
    # stops the fight freezing when two people stand close with weapons crossed.
    if swing_permille(a) <= 0 and swing_permille(b) <= 0:
        return
    # And the last clash has to have run out. An EDGE here fixed the freeze and broke the
    # block: a held blade never re-triggers, so it stopped one blow and none after it.
    if a.clash_lock > 0 or b.clash_lock > 0:
        return

    a.was_clash = b.was_clash = True
    # Both locked out: a blade touching a blade is a blade not touching a body.
    a.clash_lock = CFG["clash_lock"]
    b.clash_lock = CFG["clash_lock"]

    diff = a.tip_speed - b.tip_speed
    if diff == 0:
        return

    shove = F.mul(abs(diff), F.milli(CFG["clash_deflect"]))
    if diff > 0:
        deflect(b, a, shove)
    else:
        deflect(a, b, shove)


def deflect(loser, winner, shove):
    side = (-winner.arm[1], winner.arm[0])
    if F.dot(side, loser.arm) > 0:
        side = (-side[0], -side[1])
    loser.arm = normalized(F.add(loser.arm, F.scale(side, shove)))
    if F.length_sq(loser.arm) == 0:
        loser.arm = ((loser.facing or 1) * ONE, 0)
    loser.clash_lock = CFG["clash_lock"]
    loser.tip_speed = 0
    # The deflection must not read as a swing of theirs next tick.
    loser.tip_prev = F.scale(loser.arm, CFG["arm_length"] + loser.reach())


def strike(att, vic, sudden=False):
    if att.dead or vic.dead:
        return
    if att.hit_lock > 0 or att.clash_lock > 0:
        return
    if att.hitstop > 0 or att.dodge > 0 or vic.dodge > 0:
        return

    share = swing_permille(att)
    if share <= 0:
        return

    feet, head = vic.body_segment()
    if F.segment_distance_sq(att.hand(), att.tip(), feet, head) > \
       F.mul(CFG["body_radius"], CFG["body_radius"]):
        return

    att.dealt = share
    # One hit per ARC, and the arc is this weapon's own.
    att.hit_lock = max(CFG["hit_lock"], WEAPONS[att.weapon][0])

    dmg_raw = F.milli(int(round(WEAPONS[att.weapon][1] * 1000)))
    dealt = F.mul(dmg_raw, F.milli(share))
    dealt = desperate(att, dealt, sudden)
    damage = max(1, (dealt + (F.ONE >> 1)) >> 16)
    apply_hit(att, vic, damage, share)


def apply_hit(att, vic, damage, share):
    reduction = min(vic.armour, COMBAT["max_armour"])
    if reduction > 0:
        damage = max(1, damage * (1000 - reduction) // 1000)

    frames = min(COMBAT["max_hit_stop"],
                 COMBAT["hit_stop"] + damage * COMBAT["hit_stop_per_damage"] // 1000)
    att.hitstop = max(att.hitstop, frames)
    vic.hitstop = max(vic.hitstop, frames)

    vic.health -= damage
    vic.was_hit = True
    vic.damage_taken = damage
    if vic.health <= 0:
        vic.health = 0
        vic.dead = True

    stun = damage * COMBAT["hitstun_per_damage"]
    if vic.stun_stack == 0:
        stun = max(stun, CFG["hit_lock"] + COMBAT["hit_advantage"])
    else:
        for _ in range(vic.stun_stack):
            if stun <= 0:
                break
            stun = stun * COMBAT["combo_decay"] // 1000
    stun = max(1, min(stun, COMBAT["max_hitstun"]))
    vic.hitstun = stun
    vic.stun_stack += 1
    vic.stun_stack_timer = COMBAT["combo_stack_frames"]
    vic.tip_speed = 0
    vic.swing_share = 0
    vic.hit_lock = 0


def tick(fighters, aims, sudden=False):
    """One whole tick: timers, arms, then contact."""
    for f in fighters:
        if f.hitstop > 0:
            f.hitstop -= 1
            continue
        if f.hitstun > 0:
            f.hitstun -= 1
        if f.stagger > 0:
            f.stagger -= 1
        if f.stun_stack_timer > 0:
            f.stun_stack_timer -= 1
            if f.stun_stack_timer == 0:
                f.stun_stack = 0
        if f.dodge > 0:
            f.dodge -= 1

    for i, (f, aim) in enumerate(zip(fighters, aims)):
        if f.hitstop > 0:
            continue
        foe = fighters[1 - i] if len(fighters) == 2 else None
        step(f, aim, foe)

    resolve([f for f in fighters if f.hitstop == 0] or fighters, sudden)
