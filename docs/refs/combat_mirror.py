"""Faithful Python port of CombatMotor. Same integer arithmetic, same order of operations."""
import sim_motor_mirror as M
from sim_motor_mirror import C, ONE, mul, ATTACK, DOD
from loot_mirror import (ITEMS, NONE, FISTS, CLUB, SPEAR, DAGGER, SWORD, AXE, FLAIL,
                         GREATAXE, LEATHER, CHAINMAIL, PLATE, WEAPON, ARMOUR)

# ---------------------------------------------------------------- CombatConfig.Default()
K = dict(HP=100, ACTIVE=3, RECOVERY=700, COMBO_WINDOW=20, MAX_COMBO=3,
         HEAVY_DMG=1700, HEAVY_TIME=1500, STUN_PER_DMG=1, MAX_STUN=26,
         KNOCK=M.M(3200), HEAVY_KNOCK=M.M(6000), LIFT=M.M(1600),
         CHIP=300, BLOCK_STAM=1, GUARD_BREAK=42, BLOCK_SPEED=420,
         PARRY=12, STAGGER=48, DEATH=90, GUARD_DRAIN=48,
         DESPERATE_HP=300, DESPERATE_DMG=1400,
         FALL_SPEED=M.M(12500), FALL_PER_SPEED=8)

NONE_P, WINDUP, ACTIVE, RECOVERY = 0, 1, 2, 3


def W(item):
    """Weapon numbers come straight from the shared item table: one place to change them."""
    return ITEMS[FISTS if item == NONE else item]


def weapon(s):
    return W(s.inv.weapon)


def scale(v, permille):
    return (v * permille) // 1000


def init(s):
    s.health = K["HP"]
    s.attack = NONE_P
    s.attack_t = 0
    s.combo = 0
    s.combo_t = 0
    s.swing_spent = False
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
    wants_dodge = (guard_pressed and moving and not swinging(s)
                   and s.mode == "ground" and s.dodge_rec <= 0)

    can_guard = (guard_held and not wants_dodge and s.guard_break <= 0 and s.stam > 0
                 and s.mode == "ground" and not swinging(s))
    if can_guard and not s.blocking:
        s.guard_t = 0
    elif can_guard:
        s.guard_t += 1
    s.blocking = can_guard

    # Holding the guard is an action, not a posture.
    if s.blocking and K["GUARD_DRAIN"] > 0 and s.guard_t > 0 \
            and s.guard_t % K["GUARD_DRAIN"] == 0:
        s.stam -= 1
        if s.stam <= 0:
            s.stam = 0
            s.blocking = False

    if not s.blocking and not swinging(s):
        start_swing(s, inp)

    s.speed_permille = K["BLOCK_SPEED"] if s.blocking else 0
    return mask(s, inp, wants_dodge)


def start_swing(s, inp):
    if not (inp & ATTACK):
        return
    heavy = s.combo >= K["MAX_COMBO"] - 1
    windup = weapon(s)["frames"]
    if heavy:
        windup = scale(windup, K["HEAVY_TIME"])
    s.attack, s.attack_t = WINDUP, max(1, windup)
    s.swing_spent = False
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
        rec = scale(weapon(s)["frames"], K["RECOVERY"])
        if heavy:
            rec = scale(rec, K["HEAVY_TIME"])
        s.attack, s.attack_t = RECOVERY, max(1, rec)
    else:
        s.attack, s.attack_t = NONE_P, 0
        if s.combo + 1 < K["MAX_COMBO"]:
            s.combo += 1
            s.combo_t = K["COMBO_WINDOW"]
        else:
            s.combo, s.combo_t = 0, 0


def mask(s, inp, wants_dodge=False):
    from sim_motor_mirror import L, R, JMP
    if swinging(s):
        return inp & ~(L | R | JMP | DOD)
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
    if s.facing >= 0:
        lo, hi = s.x + half, s.x + half + w["reach"]
    else:
        lo, hi = s.x - half - w["reach"], s.x - half
    return (lo, s.y + h // 4, hi, s.y + h)


def body(s):
    half = C["W"] // 2
    h = C["CH"] if s.crouch else C["H"]
    return (s.x - half, s.y, s.x + half, s.y + h)


def overlaps(a, b):
    return a[0] < b[2] and a[2] > b[0] and a[1] < b[3] and a[3] > b[1]


def resolve(players):
    for a, atk in enumerate(players):
        box = hitbox(atk)
        if box is None:
            continue
        for b, vic in enumerate(players):
            if a == b or vic.dead or vic.dodge > 0:
                continue
            if not overlaps(box, body(vic)):
                continue
            atk.swing_spent = True
            land(atk, vic)
            break


def land(atk, vic):
    side = 1 if vic.x >= atk.x else -1
    heavy = atk.combo >= K["MAX_COMBO"] - 1
    dmg = max(1, weapon(atk)["dmg"])
    if heavy:
        dmg = scale(dmg, K["HEAVY_DMG"])
    # Bare hands get desperate below 30% HP, and only bare hands.
    if atk.inv.weapon in (NONE, FISTS) and \
            atk.health * 1000 <= K["HP"] * K["DESPERATE_HP"]:
        dmg = scale(dmg, K["DESPERATE_DMG"])

    facing_it = vic.facing != side
    if vic.blocking and facing_it:
        if vic.guard_t < K["PARRY"]:
            parry(atk, vic)
        else:
            block(atk, vic, dmg, side)
        return
    hit(atk, vic, dmg, side, heavy)


def parry(atk, vic):
    vic.was_parried = True
    vic.noise = 2
    atk.stagger = K["STAGGER"]
    atk.attack, atk.attack_t = NONE_P, 0
    atk.combo = atk.combo_t = 0
    atk.vx = 0


def block(atk, vic, dmg, side):
    # Some weapons answer a shield rather than being answered by one (docs/03).
    pierce = weapon(atk).get("pierce", 0)
    through = pierce if pierce > K["CHIP"] else K["CHIP"]
    chip = max(1, scale(dmg, through))

    wound(vic, chip)
    vic.was_blocked = True
    vic.noise = 2
    vic.vx = (K["KNOCK"] // 2) * side
    vic.stam -= K["BLOCK_STAM"]
    if vic.stam > 0:
        return
    vic.stam = 0
    vic.blocking = False
    vic.guard_break = K["GUARD_BREAK"]
    vic.stun = K["GUARD_BREAK"]
    vic.noise = 3


def hit(atk, vic, dmg, side, heavy):
    reduction = ITEMS[vic.inv.armour].get("reduction", 0) if vic.inv.armour != NONE else 0
    if reduction:
        dmg = max(1, scale(dmg, 1000 - reduction))

    wound(vic, dmg)
    vic.was_hit = True
    vic.damage_taken = dmg
    vic.noise = 3
    vic.stun = min(dmg * K["STUN_PER_DMG"], K["MAX_STUN"])
    vic.blocking = False
    vic.attack, vic.attack_t = NONE_P, 0
    vic.combo = vic.combo_t = 0

    vic.vx = (K["HEAVY_KNOCK"] if heavy else K["KNOCK"]) * side
    if vic.mode == "ground" and K["LIFT"] > 0:
        vic.vy = K["LIFT"]
        vic.mode = "air"
    if vic.mode in ("ladder", "mount", "mantle"):
        vic.mode = "air"
        vic.ladder = -1


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
