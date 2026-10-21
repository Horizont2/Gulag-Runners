"""Faithful Python port of CombatMotor. Same integer arithmetic, same order of operations."""
import sim_motor_mirror as M
from sim_motor_mirror import C, ONE, mul, ATTACK, DOD
from loot_mirror import (ITEMS, NONE, FISTS, CLUB, SPEAR, DAGGER, SWORD, AXE, FLANGED_MACE,
                         WARHAMMER, GLADIUS, MACE, SABER, SCYTHE,
                         LEATHER, CHAINMAIL, PLATE, WEAPON, ARMOUR,
                         LEATHER_CAP, MAIL_COIF, GREAT_HELM,
                         ROPE_BELT, TOOL_BELT, WAR_BELT, HELMET, BELT)

# ---------------------------------------------------------------- CombatConfig.Default()
K = dict(HP=100, ACTIVE=3, RECOVERY=700, COMBO_WINDOW=20, MAX_COMBO=3,
         HEAVY_DMG=1700, HEAVY_TIME=1500, STUN_PER_DMG=1, MAX_STUN=34,
         BUFFER=18, HIT_RECOVERY_CUT=500, HIT_ADVANTAGE=4,
         COMBO_DECAY=620, COMBO_STACK_FRAMES=75, COMBO_PUSH=300, ATTACK_FRAMES=1450,
         RECOIL=400, RECOIL_FRAMES=10,
         KNOCK=M.M(3200), HEAVY_KNOCK=M.M(6000), LIFT=M.M(1600),
         CHIP=300, BLOCK_STAM=0, HEAVY_BLOCK_STAM=2, BLOCK_RECOIL=900, GUARD_BREAK=42, BLOCK_SPEED=420,
         WINDUP_SPEED=480, ACTIVE_SPEED=180, RECOVERY_SPEED=400,
         HIT_STOP=3, BLOCK_STOP=2, PARRY_STOP=7,
         PARRY=12, STAGGER=48, DEATH=90, GUARD_DRAIN=48,
         MAX_ARMOUR=500, MAX_STUN_RESIST=400,
         DESPERATE_HP=300, DESPERATE_DMG=1400, SUDDEN_BARE=2000,
         FALL_SPEED=M.M(12500), FALL_PER_SPEED=8)

NONE_P, WINDUP, ACTIVE, RECOVERY = 0, 1, 2, 3


def W(item):
    """Weapon numbers come straight from the shared item table: one place to change them."""
    return ITEMS[FISTS if item == NONE else item]


def weapon(s):
    return W(s.inv.weapon)


def windup(w):
    """The weapon's wind-up after the one knob that paces the whole fight."""
    n = scale(w["frames"], K["ATTACK_FRAMES"]) if K["ATTACK_FRAMES"] else w["frames"]
    return max(1, n)


def scale(v, permille):
    return (v * permille) // 1000


def init(s):
    s.health = K["HP"]
    s.attack = NONE_P
    s.attack_t = 0
    s.combo = 0
    s.combo_t = 0
    s.swing_spent = False
    s.swing_connected = False
    s.atk_buf = 0
    s.stun_stack = 0
    s.stun_stack_t = 0
    s.recoil_t = 0
    s.blocking = False
    s.guard_t = 0
    s.guard_break = 0
    s.stun = 0
    s.stagger = 0
    s.dead = False
    s.guard_held = False
    s.speed_permille = 0
    s.was_hit = s.was_blocked = s.was_parried = s.swing_started = s.just_died = False
    s.damage_taken = 0
    return s


def reeling(s):
    return s.stun > 0 or s.stagger > 0 or s.dead


def swinging(s):
    return s.attack != NONE_P


def combat_step(s, inp):
    """One player's combat tick, before movement. Returns the input movement may act on."""
    s.damage_taken = 0
    s.was_hit = s.was_blocked = s.was_parried = s.swing_started = s.just_died = False

    # Frozen by a blow: no timers, no swing, no guard, and nothing handed to the motor.
    if getattr(s, "hitstop", 0) > 0:
        return 0

    # Remembered BEFORE anything can refuse the press, so a tap during recovery counts.
    if (inp & ATTACK) and K["BUFFER"] > 0:
        s.atk_buf = K["BUFFER"]

    if s.atk_buf > 0:
        s.atk_buf -= 1

    # Long enough without being hit and the string is over.
    if s.stun_stack_t > 0:
        s.stun_stack_t -= 1
        if s.stun_stack_t == 0:
            s.stun_stack = 0
    if s.recoil_t > 0:
        s.recoil_t -= 1

    for a in ("stun", "stagger", "guard_break"):
        if getattr(s, a) > 0:
            setattr(s, a, getattr(s, a) - 1)

    # The combo window is how long you have AFTER a swing to continue, so it only runs between
    # swings. Letting it run during one expired it part way through the next, which quietly reset
    # the count and meant the heavy third hit never happened.
    if not swinging(s):
        if s.combo_t > 0:
            s.combo_t -= 1
        if s.combo_t == 0:
            s.combo = 0

    if s.dead:
        return 0

    guard_held = bool(inp & DOD)
    guard_pressed = guard_held and not s.guard_held
    s.guard_held = guard_held

    if reeling(s):
        s.blocking = False
        s.speed_permille = 0
        return 0

    advance(s)
    take_the_fall(s)

    # One defensive button, two answers: with a direction held it is a dodge, standing still
    # it is the guard, and in the air it is neither.
    from sim_motor_mirror import L, R
    moving = bool(inp & L) != bool(inp & R)
    # The dodge is a burst of speed, and this game has one ground speed on purpose.
    wants_dodge = (M.C["ALLOW_DODGE"] and guard_pressed and moving and not swinging(s)
                   and s.mode == "ground" and s.dodge_rec <= 0)

    can_guard = (guard_held and not wants_dodge and s.guard_break <= 0 and s.stam > 0
                 and s.mode == "ground" and not swinging(s))
    if can_guard and not s.blocking:
        s.guard_t = 0
    elif can_guard:
        s.guard_t += 1
    s.blocking = can_guard

    # Holding the guard is an action, not a posture -- and the belt is what buys time
    # here, and the only thing it buys: a war belt stretches 0.8 s a charge to 1.2 s.
    drain = K["GUARD_DRAIN"]
    if drain > 0 and s.inv.guard_hold > 0:
        drain = scale(drain, 1000 + s.inv.guard_hold)

    if s.blocking and drain > 0 and s.guard_t > 0 and s.guard_t % drain == 0:
        s.stam -= 1
        if s.stam <= 0:
            s.stam = 0
            s.blocking = False

    if not s.blocking and not swinging(s):
        start_swing(s, inp)

    s.speed_permille = speed_cap(s)
    return mask(s, inp, wants_dodge)


def start_swing(s, inp):
    if not (inp & ATTACK) and s.atk_buf <= 0:
        return
    s.atk_buf = 0
    heavy = s.combo >= K["MAX_COMBO"] - 1
    wind = windup(weapon(s))
    if heavy:
        wind = scale(wind, K["HEAVY_TIME"])
    s.attack, s.attack_t = WINDUP, max(1, wind)
    s.swing_spent = False
    s.swing_connected = False
    s.atk_buf = 0
    s.swing_started = True
    s.blocking = False
    s.noise = 1


def advance(s):
    if s.attack == NONE_P:
        return
    s.attack_t -= 1
    if s.attack_t > 0:
        return

    if s.attack == WINDUP:
        s.attack, s.attack_t = ACTIVE, max(1, K["ACTIVE"])
    elif s.attack == ACTIVE:
        heavy = s.combo >= K["MAX_COMBO"] - 1
        rec = scale(windup(weapon(s)), K["RECOVERY"])
        if heavy:
            rec = scale(rec, K["HEAVY_TIME"])
        # Landed, so half of it is given back.
        if s.swing_connected and K["HIT_RECOVERY_CUT"] > 0:
            rec = max(1, scale(rec, K["HIT_RECOVERY_CUT"]))
        s.attack, s.attack_t = RECOVERY, max(1, rec)
    else:
        s.attack, s.attack_t = NONE_P, 0
        if s.combo + 1 < K["MAX_COMBO"]:
            s.combo += 1
            s.combo_t = K["COMBO_WINDOW"]
        else:
            s.combo, s.combo_t = 0, 0


def speed_cap(s):
    """CombatMotor.SpeedCap: how fast the body may travel, in thousandths of the run speed.
    Zero is the motor's "no cap", so every branch names a number or names zero on purpose."""
    if s.blocking: return K["BLOCK_SPEED"]
    if s.attack == WINDUP: return K["WINDUP_SPEED"]
    if s.attack == ACTIVE: return K["ACTIVE_SPEED"]
    if s.attack == RECOVERY: return K["RECOVERY_SPEED"]
    return 0


def mask(s, inp, wants_dodge=False):
    from sim_motor_mirror import L, R, JMP
    # A swing keeps the COMMITMENT and gives back the WALKING: jump and dodge still go, left
    # and right stay and are slowed by phase through speed_permille instead. A fighter rooted
    # for every attack turns every exchange into two people taking turns standing still.
    if swinging(s):
        return inp & ~(JMP | DOD)
    if s.blocking:
        return inp & ~(JMP | DOD)
    # Only the press read as a dodge reaches the motor as one.
    return inp if wants_dodge else inp & ~DOD


def take_the_fall(s):
    """Damage from the landing the motor recorded last tick."""
    if s.landed_speed <= K["FALL_SPEED"]:
        s.landed_speed = 0
        return
    over = s.landed_speed - K["FALL_SPEED"]
    s.landed_speed = 0
    dmg = over * K["FALL_PER_SPEED"] // ONE
    if dmg < 1:
        return
    wound(s, dmg)
    s.was_hit = True
    s.damage_taken = dmg
    s.stun = min(dmg * K["STUN_PER_DMG"], K["MAX_STUN"])


def hitbox(s):
    if s.attack != ACTIVE or s.swing_spent or s.dead:
        return None
    w = weapon(s)
    half = C["W"] // 2
    h = C["CH"] if s.crouch else C["H"]
    # The inner edge is the attacker's own centre, not their front face. Fighters do not
    # collide with each other, so they stand on the same spot, and a box starting at the
    # front then lies entirely past the victim: nothing lands at point-blank range.
    if s.facing >= 0:
        lo, hi = s.x, s.x + half + w["reach"]
    else:
        lo, hi = s.x - half - w["reach"], s.x
    return (lo, s.y + h // 4, hi, s.y + h)


def body(s):
    half = C["W"] // 2
    h = C["CH"] if s.crouch else C["H"]
    return (s.x - half, s.y, s.x + half, s.y + h)


def overlaps(a, b):
    return a[0] < b[2] and a[2] > b[0] and a[1] < b[3] and a[3] > b[1]


def resolve(players, sudden_death=False):
    for a, atk in enumerate(players):
        box = hitbox(atk)
        if box is None:
            continue
        for b, vic in enumerate(players):
            if a == b or vic.dead or vic.dodge > 0:
                continue
            if not overlaps(box, body(vic)):
                continue
            # Not through the level: two fighters on different slices of a building are not
            # within reach of each other however their flattened boxes overlap.
            if abs(atk.depth - vic.depth) > M.C["BODYZ"]:
                continue
            atk.swing_spent = True
            atk.swing_connected = True
            land(atk, vic, sudden_death)
            break


def land(atk, vic, sudden_death=False):
    side = 1 if vic.x >= atk.x else -1
    heavy = atk.combo >= K["MAX_COMBO"] - 1
    dmg = max(1, weapon(atk)["dmg"])
    if heavy:
        dmg = scale(dmg, K["HEAVY_DMG"])

    # Bare hands get desperate below 30% HP, and only bare hands. In sudden death they hurt
    # anyway (docs/01), because the point of that last minute is that there is nowhere to go
    # and no time to look for a weapon. The two do NOT multiply — the bigger one wins, or a
    # punch from a fighter on their last tenth reads as a one-hit kill out of nowhere.
    if atk.inv.weapon in (NONE, FISTS):
        boost = 1000
        if atk.health * 1000 <= K["HP"] * K["DESPERATE_HP"]:
            boost = K["DESPERATE_DMG"]
        if sudden_death and K["SUDDEN_BARE"] > boost:
            boost = K["SUDDEN_BARE"]
        if boost != 1000:
            dmg = scale(dmg, boost)

    facing_it = vic.facing != side
    if vic.blocking and facing_it:
        # A shield is what makes the parry worth hunting for (docs/02's skill ceiling).
        window = K["PARRY"] + vic.inv.parry_bonus
        if vic.guard_t < window:
            parry(atk, vic)
        else:
            block(atk, vic, dmg, side)
        return
    hit(atk, vic, dmg, side, heavy)


def parry(atk, vic):
    # Both of them, not only the one hit: half the weight of a blow is the arm
    # coming to a stop. The longer freeze wins rather than accumulating.
    for p in (atk, vic):
        if getattr(p, "hitstop", 0) < K["PARRY_STOP"]:
            p.hitstop = K["PARRY_STOP"]

    vic.was_parried = True
    vic.noise = 2
    atk.stagger = K["STAGGER"]
    atk.swing_connected = False
    atk.attack, atk.attack_t = NONE_P, 0
    atk.combo = atk.combo_t = 0
    atk.vx = 0


def block(atk, vic, dmg, side):
    # Both of them, not only the one hit: half the weight of a blow is the arm
    # coming to a stop. The longer freeze wins rather than accumulating.
    for p in (atk, vic):
        if getattr(p, "hitstop", 0) < K["BLOCK_STOP"]:
            p.hitstop = K["BLOCK_STOP"]

    # Some weapons answer a shield rather than being answered by one (docs/03).
    pierce = weapon(atk).get("pierce", 0)
    through = pierce if pierce > K["CHIP"] else K["CHIP"]
    chip = scale(dmg, through)

    # And what a shield is FOR: the hits you meet, not the hits you stand and take. 0 means
    # the ordinary chip, which is every other armour.
    guard = vic.inv.chip
    if guard > 0:
        chip = scale(chip, guard)
    chip = max(1, chip)

    wound(vic, chip)
    vic.damage_taken = chip          # what got through, for the view to size
    vic.was_blocked = True
    vic.noise = 2
    vic.vx = (K["KNOCK"] // 2) * side
    # The attacker off a braced guard, harder than off a body: a rush that cannot stand in
    # your face is a rush a longer weapon can answer.
    if K["BLOCK_RECOIL"] > 0 and K["RECOIL_FRAMES"] > 0:
        atk.vx = -((K["KNOCK"] * K["BLOCK_RECOIL"]) // 1000) * side
        atk.recoil_t = K["RECOIL_FRAMES"]

    heavy = atk.combo >= K["MAX_COMBO"] - 1
    vic.stam -= K["HEAVY_BLOCK_STAM"] if heavy else K["BLOCK_STAM"]
    if vic.stam > 0:
        return
    vic.stam = 0
    vic.blocking = False
    vic.guard_break = K["GUARD_BREAK"]
    vic.stun = K["GUARD_BREAK"]
    vic.noise = 3


def owed(s):
    """Frames the attacker still owes after the blow lands: the rest of the active window
    plus the recovery it is about to roll into, with the connected cut already applied."""
    if s.attack != ACTIVE:
        return 0
    heavy = s.combo >= K["MAX_COMBO"] - 1
    rec = scale(windup(weapon(s)), K["RECOVERY"])
    if heavy:
        rec = scale(rec, K["HEAVY_TIME"])
    if K["HIT_RECOVERY_CUT"] > 0:
        rec = max(1, scale(rec, K["HIT_RECOVERY_CUT"]))
    return max(0, s.attack_t - 1) + rec


def hit(atk, vic, dmg, side, heavy):
    # Both of them, not only the one hit: half the weight of a blow is the arm
    # coming to a stop. The longer freeze wins rather than accumulating.
    for p in (atk, vic):
        if getattr(p, "hitstop", 0) < K["HIT_STOP"]:
            p.hitstop = K["HIT_STOP"]

    # Three worn pieces, summed and then capped. A plain sum keeps each piece worth
    # exactly what the table says; the cap is one number in the config rather than an
    # emergent one.
    reduction = min(vic.inv.reduction, K["MAX_ARMOUR"]) if K["MAX_ARMOUR"] > 0 \
        else vic.inv.reduction
    if reduction:
        dmg = max(1, scale(dmg, 1000 - reduction))

    wound(vic, dmg)
    vic.was_hit = True
    vic.damage_taken = dmg
    vic.noise = 3
    stun = dmg * K["STUN_PER_DMG"]

    # The helmet, and BEFORE the floor below on purpose: taken off afterwards it would eat
    # the attacker's guaranteed turn and put every weapon at a minus on a clean hit.
    resist = min(vic.inv.stun_resist, K["MAX_STUN_RESIST"]) if K["MAX_STUN_RESIST"] > 0 \
        else vic.inv.stun_resist
    if resist:
        stun = scale(stun, 1000 - resist)

    if vic.stun_stack == 0:
        # The FIRST hit of a string confirms and buys the turn.
        stun = max(stun, owed(atk) + K["HIT_ADVANTAGE"])
    else:
        # The rest are paid for out of damage alone and worth less each time. Guaranteeing
        # the turn on EVERY hit is an infinite: measured, a bare-handed fighter landed
        # twenty blows in eight seconds while the man with a club landed none at all.
        for _ in range(vic.stun_stack):
            stun = scale(stun, K["COMBO_DECAY"])
    vic.stun = max(1, min(stun, K["MAX_STUN"]))

    stacked = vic.stun_stack
    vic.stun_stack = min(250, vic.stun_stack + 1)
    vic.stun_stack_t = K["COMBO_STACK_FRAMES"]
    vic.blocking = False
    vic.attack, vic.attack_t = NONE_P, 0
    vic.combo = vic.combo_t = 0

    push = K["HEAVY_KNOCK"] if heavy else K["KNOCK"]
    # Each further hit pushes harder, so the attacker runs out of reach and has to step
    # back in -- and stepping in is what costs him the frames that hand the turn over.
    if stacked > 0 and K["COMBO_PUSH"] > 0:
        push = (push * (1000 + K["COMBO_PUSH"] * stacked)) // 1000
    vic.vx = push * side

    # And the attacker off the blow: against a wall the victim cannot go anywhere, so
    # without this the string never ends there.
    if K["RECOIL"] > 0 and K["RECOIL_FRAMES"] > 0:
        atk.vx = -((push * K["RECOIL"]) // 1000) * side
        atk.recoil_t = K["RECOIL_FRAMES"]
    # Only the finisher lifts; a light blow leaves them standing, which is the one change
    # that makes a second hit possible at all.
    if heavy and vic.mode == "ground" and K["LIFT"] > 0:
        vic.vy = K["LIFT"]
        vic.mode = "air"
    if vic.mode in ("ladder", "mount", "mantle"):
        # A climb-out carries the body BETWEEN depth lanes, and breaking it off half way
        # leaves it stranded between them -- which is what being thrown behind the level is.
        # UpdateDepth gives up outright while airborne, on purpose, so nothing corrects it.
        # The move's promise is kept even though the move is not.
        if vic.mode == "mantle":
            vic.depth = vic.script_to_depth
        vic.mode = "air"
        vic.ladder = -1
        vic.script_t = 0
        vic.script_frames = 0


def wound(vic, dmg):
    vic.health -= dmg
    if vic.health > 0:
        return
    vic.health = 0
    vic.dead = True
    vic.just_died = True
    vic.blocking = False
    vic.attack = NONE_P
    vic.noise = 3
