"""BotBrain, mirrored: one tick of state in, one tick of intent out.

Here for one measurement above all others. A bot that re-decides every frame at 60 Hz does
not read as fast, it reads as BROKEN -- wherever two choices are close it stutters on the
spot, and no amount of tuning the choices fixes it, because the fault is the rate. That is
not something you can see reliably by eye in a fight, and it is trivial to count here.

Mirrors Gulag runners/Assets/_Game/Sim/Core/BotBrain.cs and BotConfig.cs."""
from sim_motor_mirror import M, F, C, L, R, UP, DN, JMP, DOD
from loot_mirror import Rng, ITEMS

DUMMY, SPARRING, HUNTER = 0, 1, 2

# No attack phases any more (docs/31): a blow is a moving tip, not a state.
NONE_P, WINDUP, ACTIVE, RECOVERY = 0, 1, 2, 3


def config(skill=SPARRING):
    """BotConfig.For: the defaults, softened or hardened to a skill."""
    c = dict(skill=skill,
             react=14, jitter=8,
             range_pm=850, close_pm=450,
             aggression=550, guard=220, dodge=90, withdraw=700,
             floor_gap=M(1200), ladder_seek=M(14000))

    if skill == DUMMY:
        c.update(aggression=0, dodge=0, guard=250, withdraw=0)
    elif skill == HUNTER:
        c.update(react=9, jitter=5, aggression=700, dodge=160, guard=300)
    return c


class Bot:
    """BotState: what it carries between ticks."""

    def __init__(s, seed=1):
        s.think = 0
        s.held = 0
        s.rng = Rng(seed)
        s.ladder = -1
        s.aim_x = 0
        s.aim_y = 0
        s.swing_hold = 0
        s.swing_high = False
        s.guard_hold = 0


def scale(v, permille):
    return (v * permille) // 1000


def roll(bot, permille):
    return permille > 0 and bot.rng.below(1000) < permille


def decide(bot, me, foe, w, cfg):
    """BotBrain.Decide: the clock, the thought it gates, and the arm it does not.

    Returns (flags, aim). The ARM is re-pointed every tick, outside the reaction gate:
    a bot that moved its weapon once every fourteen frames would not be a slow bot, it
    would be one that cannot swing at all, because tip speed is damage now.
    """
    aim(bot, me, foe)
    intent = (bot.aim_x, bot.aim_y)

    if me.dead or reeling(me) or me.mode in ("mount", "mantle"):
        bot.held = 0
        bot.think = 0
        bot.swing_hold = 0
        return 0, intent

    if bot.think > 0:
        bot.think -= 1
        return bot.held, intent

    bot.think = cfg["react"] + (bot.rng.below(cfg["jitter"]) if cfg["jitter"] > 0 else 0)
    bot.held = think(bot, me, foe, w, cfg)
    return bot.held, (bot.aim_x, bot.aim_y)


def aim(bot, me, foe):
    """Two poses: guard (blade level at them) and swing (throw it high to low)."""
    towards = 1 if foe.x >= me.x else -1

    if bot.swing_hold > 0:
        bot.swing_hold -= 1
        if bot.swing_hold == 0:
            bot.swing_high = not bot.swing_high
        bot.aim_x = towards * 500
        bot.aim_y = 866 if bot.swing_high else -866
        return

    if bot.guard_hold > 0:
        bot.guard_hold -= 1
        bot.aim_x = towards * 1000
        bot.aim_y = 0
        return

    # Carrying: stick centred, blade low, open. Which is what makes it beatable -- the
    # first version guarded whenever it was not swinging, which is a free perfect block.
    bot.aim_x = 0
    bot.aim_y = 0


def swing(bot, me):
    """Commit to one end of the arc, held long enough for the arm to get there."""
    if bot.swing_hold > 0:
        return
    bot.guard_hold = 0
    frames = ITEMS[me.inv.weapon]["frames"]
    bot.swing_hold = max(4, frames)


def reeling(s):
    return getattr(s, "stun", 0) > 0 or getattr(s, "stagger", 0) > 0 or getattr(s, "dead", False)


def think(bot, me, foe, w, cfg):
    if foe.dead:
        return 0

    gap = foe.x - me.x
    flat = abs(gap)
    towards = 1 if gap >= 0 else -1

    if me.mode == "ladder":
        return climb(me, foe)

    rise = foe.y - me.y

    if cfg["skill"] == HUNTER and abs(rise) > cfg["floor_gap"]:
        to_ladder = seek_ladder(bot, me, rise, w, cfg)
        if to_ladder:
            return to_ladder

    if cfg["skill"] == DUMMY:
        return 0

    reach = C["W"] // 2 + ITEMS[me.inv.weapon]["reach"]
    want = scale(reach, cfg["range_pm"])
    too_close = scale(reach, cfg["close_pm"])

    flags = 0

    if flat > want:
        flags |= R if towards > 0 else L
        if roll(bot, cfg["guard"] // 2):
            flags |= DOD
        return flags

    if flat < too_close:
        flags |= L if towards > 0 else R
        if roll(bot, cfg["withdraw"]):
            if roll(bot, cfg["aggression"]):
                swing(bot, me)
        return flags

    if foe.tip_speed > 0 and roll(bot, cfg["guard"] * 3):
        bot.swing_hold = 0
        bot.guard_hold = cfg["react"] * 2
        return 0

    if roll(bot, cfg["dodge"]) and me.stam > 1:
        flags |= L if towards > 0 else R
        flags |= DOD
        return flags

    if roll(bot, cfg["aggression"]):
        swing(bot, me)
        return flags

    if flat > scale(want, 900):
        flags |= R if towards > 0 else L

    return flags


def climb(me, foe):
    rise = foe.y - me.y
    if rise > M(300): return UP
    if rise < M(-300): return DN
    return R if foe.x >= me.x else L


def seek_ladder(bot, me, rise, w, cfg):
    ladders = w.get("ladder") or []
    up = rise > 0
    best, nearest = -1, 0

    for i, l in enumerate(ladders):
        if up and l[3] <= me.y: continue
        if not up and l[1] >= me.y: continue
        centre = (l[0] + l[2]) // 2
        away = abs(centre - me.x)
        if away > cfg["ladder_seek"]: continue
        if best >= 0 and away >= nearest: continue
        best, nearest = i, away

    bot.ladder = best
    if best < 0:
        return 0

    box = ladders[best]
    offset = (box[0] + box[2]) // 2 - me.x
    if abs(offset) <= M(150):
        return UP if up else DN
    return R if offset > 0 else L
