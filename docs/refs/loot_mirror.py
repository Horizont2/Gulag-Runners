"""Faithful Python port of LootMotor, ItemTable and ChestConfig.

Same integer arithmetic as the C#: progress counts in thousandths of a bare-handed frame, so a
weapon used as a lever advances it faster without any of it becoming a float."""
from sim_motor_mirror import M, C, ACTION, ONE, mul

BARE_GAIN = 1000

# ---------------------------------------------------------------- items (docs/03, M0 cut)
NONE, FISTS, CLUB, SPEAR = 0, 1, 2, 3
DAGGER, SWORD, AXE, FLAIL, GREATAXE = 4, 5, 6, 7, 8
LEATHER, CHAINMAIL, PLATE = 20, 21, 22
BANDAGE = 30
WEAPON, ARMOUR, UTILITY = 1, 2, 3


def _w(name, tier, dmg, frames, reach, dur, pry, pierce=0):
    return dict(kind=WEAPON, tier=tier, dmg=dmg, frames=frames, reach=M(reach),
                pry=M(pry), dur=dur, pierce=pierce, name=name)


def _a(name, tier, reduction):
    return dict(kind=ARMOUR, tier=tier, dmg=0, frames=0, reach=0, pry=0, dur=0,
                pierce=0, reduction=reduction, name=name)


ITEMS = {
    NONE:      dict(kind=0, tier=0, pry=0, dur=0, name="-"),
    FISTS:     _w("fists", 0, 3, 12, 600, 0, 1000),
    CLUB:      _w("club", 1, 8, 15, 950, 14, 2000),
    SPEAR:     _w("spear", 2, 13, 27, 1700, 9, 1600),
    DAGGER:    _w("dagger", 1, 4, 9, 500, 18, 1200),
    SWORD:     _w("sword", 2, 10, 19, 1150, 12, 1500),
    AXE:       _w("axe", 2, 12, 23, 1050, 10, 2400),
    FLAIL:     _w("flail", 3, 12, 21, 1300, 9, 1400, pierce=700),
    GREATAXE:  _w("greataxe", 3, 21, 34, 1500, 6, 2600),
    LEATHER:   _a("leather vest", 1, 150),
    CHAINMAIL: _a("chainmail", 2, 300),
    PLATE:     _a("plate", 3, 450),
    BANDAGE:   dict(kind=UTILITY, tier=1, pry=0, dur=0, heal=30, name="bandage"),
}

def pry_milli(weapon):
    """ToMilli() of the pry speed: what one tick of holding adds."""
    item = ITEMS[FISTS if weapon == NONE else weapon]
    return max(1, (item["pry"] * 1000) >> 16)

# ---------------------------------------------------------------- chests (docs/03)
CRATE, LOCKER, SAFE = 0, 1, 2
CHEST_CFG = {
    CRATE:  dict(bare=72,  bare_noise=1, tool_noise=1, wear=1, name="crate"),
    LOCKER: dict(bare=300, bare_noise=2, tool_noise=3, wear=2, name="locker"),
    SAFE:   dict(bare=360, bare_noise=3, tool_noise=3, wear=3, name="safe"),
}
DECAY_MUL = 2
NOISE_FRAMES = 20

# ---------------------------------------------------------------- loot on the floor (DropConfig)
D = dict(POP_X=M(1400), POP_Y=M(3600), DROP_X=M(1100), DROP_Y=M(2200),
         GRAV=M(22000), MAXFALL=M(14000), BOUNCE=M(150), RADIUS=M(220),
         POP_LOCK=14, DROP_LOCK=30)
DT = ONE // 60

NOTHING, FLYING, RESTING = 0, 1, 2


class Ground:
    def __init__(s):
        s.item, s.state = NONE, NOTHING
        s.x = s.y = s.vx = s.vy = 0
        s.lock, s.age = 0, 0

    @property
    def live(s):
        return s.state != NOTHING

    @property
    def takeable(s):
        return s.state == RESTING and s.lock <= 0

    def clear(s):
        s.__init__()


def pool(n):
    return [Ground() for _ in range(n)]


def free_slot(ground):
    oldest, oldest_age = -1, -1
    for i, g in enumerate(ground):
        if not g.live:
            return i
        if g.state == RESTING and g.age > oldest_age:
            oldest, oldest_age = i, g.age
    return oldest


def launch(ground, item, x, y, vx, vy, lock):
    if not ground or item == NONE:
        return -1
    i = free_slot(ground)
    if i < 0:
        return -1
    g = ground[i]
    g.item, g.state = item, FLYING
    g.x, g.y, g.vx, g.vy = x, y, vx, vy
    g.lock, g.age = lock, 0
    return i


def pop(ground, item, x, y, facing):
    return launch(ground, item, x, y, D["POP_X"] * (1 if facing >= 0 else -1),
                  D["POP_Y"], D["POP_LOCK"])


def drop_item(ground, item, x, y, facing):
    return launch(ground, item, x, y, D["DROP_X"] * (-1 if facing >= 0 else 1),
                  D["DROP_Y"], D["DROP_LOCK"])


def blocked(x, y, w):
    box = (x - D["RADIUS"], y - D["RADIUS"], x + D["RADIUS"], y + D["RADIUS"])
    return any(overlaps(box, sol) for sol in w["solid"])


def floor_under(x, from_y, w):
    best = None
    lowest = from_y - 2 * ONE
    for sol in w["solid"]:
        if sol[2] <= x - D["RADIUS"] or sol[0] >= x + D["RADIUS"]:
            continue
        if sol[3] > from_y + D["RADIUS"] or sol[3] < lowest:
            continue
        if best is None or sol[3] > best:
            best = sol[3]
    return best


def step_ground(ground, w):
    for g in ground:
        if not g.live:
            continue
        g.age += 1
        if g.lock > 0:
            g.lock -= 1
        if g.state != FLYING:
            continue

        g.vy -= mul(D["GRAV"], DT)
        if g.vy < -D["MAXFALL"]:
            g.vy = -D["MAXFALL"]
        nx, ny = g.x + mul(g.vx, DT), g.y + mul(g.vy, DT)
        if g.vx != 0 and blocked(nx, g.y, w):
            nx, g.vx = g.x, 0

        if g.vy <= 0:
            floor = floor_under(nx, g.y, w)
            if floor is not None:
                rest = floor + D["RADIUS"]
                if ny <= rest:
                    up = mul(-g.vy, D["BOUNCE"])
                    g.x, g.y = nx, rest
                    if up > M(400):
                        g.vx, g.vy = g.vx // 2, up
                    else:
                        g.vx = g.vy = 0
                        g.state = RESTING
                    continue
        g.x, g.y = nx, ny
        if g.y < -40 * ONE:
            g.clear()


class Chest:
    def __init__(s, kind, contents, box):
        s.kind, s.contents, s.box = kind, contents, box
        s.progress, s.opened, s.opener = 0, False, -1


def fresh(chests):
    for c in chests:
        c.progress, c.opened, c.opener = 0, False, -1


class Inv:
    def __init__(s):
        s.weapon, s.armour, s.utility = NONE, NONE, NONE
        s.durability = 0

    @property
    def bare(s):
        return s.weapon == NONE

    def equip(s, item):
        kind = ITEMS[item]["kind"]
        if kind == WEAPON:
            old, s.weapon, s.durability = s.weapon, item, ITEMS[item]["dur"]
            return old
        if kind == ARMOUR:
            old, s.armour = s.armour, item
            return old
        if kind == UTILITY:
            old, s.utility = s.utility, item
            return old
        return NONE

    def spend(s, amount):
        if s.weapon == NONE or amount <= 0 or ITEMS[s.weapon]["dur"] == 0:
            return False
        s.durability -= amount
        if s.durability > 0:
            return False
        s.weapon, s.durability = NONE, 0
        return True                                  # broke


def overlaps(a, b):
    return a[0] < b[2] and a[2] > b[0] and a[1] < b[3] and a[3] > b[1]


def body(s):
    half = C["W"] // 2
    h = C["CH"] if s.crouch else C["H"]
    return (s.x - half, s.y, s.x + half, s.y + h)


def available(chest, player):
    return not chest.opened and (chest.opener < 0 or chest.opener == player)


def find(s, player, chests):
    b = body(s)
    if 0 <= s.opening < len(chests) and available(chests[s.opening], player) \
            and overlaps(b, chests[s.opening].box):
        return s.opening
    for i, c in enumerate(chests):
        if available(c, player) and overlaps(b, c.box):
            return i
    return -1


def drain(s, player, chests):
    i = s.opening
    if not (0 <= i < len(chests)):
        s.opening = -1
        return
    held = chests[i]
    if held.opener != player:
        s.opening = -1
        return
    held.progress -= BARE_GAIN * max(1, DECAY_MUL)
    if held.progress > 0:
        return
    held.progress, held.opener, s.opening = 0, -1, -1


def take_from_ground(s, pressed, ground, button_taken):
    if not ground:
        return
    if s.mode not in ("ground", "air"):
        return

    b = body(s)
    free = swap = -1
    for i, g in enumerate(ground):
        if not g.takeable:
            continue
        box = (g.x - D["RADIUS"], g.y - D["RADIUS"], g.x + D["RADIUS"], g.y + D["RADIUS"])
        if not overlaps(b, box):
            continue
        slot = {WEAPON: s.inv.weapon, ARMOUR: s.inv.armour, UTILITY: s.inv.utility}.get(
            ITEMS[g.item]["kind"], NONE)
        if slot == NONE:
            if free < 0:
                free = i
        elif swap < 0:
            swap = i

    s.standing_on = free if free >= 0 else swap

    if free >= 0:
        take(s, ground, free, False)
        return
    if swap < 0 or button_taken or not pressed or s.mode != "ground":
        return
    take(s, ground, swap, True)


def take(s, ground, i, swapping):
    g = ground[i]
    taken, at_x, at_y = g.item, g.x, g.y
    g.clear()
    displaced = s.inv.equip(taken)
    s.picked = taken
    if swapping and displaced != NONE:
        drop_item(ground, displaced, at_x, at_y, s.facing)
        s.dropped = displaced
    s.noise = 1


def loot_step(s, player, inp, chests, ground=None):
    """One tick of LootMotor, run after the movement step."""
    ground = ground if ground is not None else []
    s.picked = NONE
    s.dropped = NONE
    s.opened_chest = -1
    s.standing_on = -1

    held = bool(inp & ACTION)
    pressed = held and not s.action_held
    s.action_held = held

    if not chests:
        s.opening = -1
        take_from_ground(s, pressed, ground, False)
        return

    working = bool(inp & ACTION) and s.mode == "ground"
    target = find(s, player, chests) if working else -1

    if s.opening >= 0 and s.opening != target:
        drain(s, player, chests)

    if target < 0:
        take_from_ground(s, pressed, ground, False)
        return

    take_from_ground(s, pressed, ground, True)

    chest = chests[target]
    cfg = CHEST_CFG[chest.kind]
    chest.opener = player
    s.opening = target

    tool = not s.inv.bare
    chest.progress += pry_milli(s.inv.weapon)

    if s.stepn == 0:
        s.noise = cfg["tool_noise"] if tool else cfg["bare_noise"]
        s.stepn = NOISE_FRAMES

    needed = cfg["bare"] * BARE_GAIN
    if chest.progress < needed:
        return

    chest.progress, chest.opened, chest.opener = needed, True, -1
    s.opening = -1
    if tool:
        s.inv.spend(cfg["wear"])
    cx = chest.box[0] + (chest.box[2] - chest.box[0]) // 2
    cy = chest.box[1] + (chest.box[3] - chest.box[1]) // 2
    offset = s.x - cx
    meaningful = (chest.box[2] - chest.box[0]) // 4
    away = 1 if offset > meaningful else (-1 if offset < -meaningful else (-1 if s.facing >= 0 else 1))
    pop(ground, chest.contents, cx, cy, away)
    s.opened_chest = target
    s.noise = 3


def frames_to_open(kind, weapon):
    """What the designer sees in the inspector: ceil(needed / gain)."""
    gain = pry_milli(weapon)
    return (CHEST_CFG[kind]["bare"] * BARE_GAIN + gain - 1) // gain
