"""Faithful Python port of LootMotor, ItemTable and ChestConfig.

Same integer arithmetic as the C#: progress counts in thousandths of a bare-handed frame, so a
weapon used as a lever advances it faster without any of it becoming a float."""
from sim_motor_mirror import M, C, ACTION

BARE_GAIN = 1000

# ---------------------------------------------------------------- items (docs/03, M0 cut)
NONE, FISTS, CLUB, SPEAR, CHAINMAIL, BANDAGE = 0, 1, 2, 3, 4, 5
WEAPON, ARMOUR, UTILITY = 1, 2, 3

ITEMS = {
    NONE:      dict(kind=0, tier=0, pry=0,    dur=0,  name="-"),
    FISTS:     dict(kind=WEAPON,  tier=0, pry=M(1000), dur=0,  name="fists"),
    CLUB:      dict(kind=WEAPON,  tier=1, pry=M(2000), dur=14, name="club"),
    SPEAR:     dict(kind=WEAPON,  tier=2, pry=M(1600), dur=9,  name="spear"),
    CHAINMAIL: dict(kind=ARMOUR,  tier=2, pry=0,       dur=0,  name="chainmail"),
    BANDAGE:   dict(kind=UTILITY, tier=1, pry=0,       dur=0,  name="bandage"),
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


def loot_step(s, player, inp, chests):
    """One tick of LootMotor, run after the movement step."""
    s.picked = NONE
    s.opened_chest = -1
    if not chests:
        s.opening = -1
        return

    working = bool(inp & ACTION) and s.mode == "ground"
    target = find(s, player, chests) if working else -1

    if s.opening >= 0 and s.opening != target:
        drain(s, player, chests)

    if target < 0:
        return

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
    s.inv.equip(chest.contents)
    s.picked = chest.contents
    s.opened_chest = target
    s.noise = 3


def frames_to_open(kind, weapon):
    """What the designer sees in the inspector: ceil(needed / gain)."""
    gain = pry_milli(weapon)
    return (CHEST_CFG[kind]["bare"] * BARE_GAIN + gain - 1) // gain
