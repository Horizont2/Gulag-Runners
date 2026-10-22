"""The practice opponent, measured.

The question is NOT "is it good at the game". It is whether it is a character or a glitch,
and that comes down to things you can count:

  * does it twitch -- how often does what it is pressing change;
  * does it have a reaction time, or does it answer on the frame;
  * does it stand at its own weapon's range, or at somebody else's;
  * can it actually land a hit, and can it actually be hit;
  * and does the thing that is meant to be harmless stay harmless.

A bot that re-decides every tick at 60 Hz does not look fast, it looks broken. That fault is
hard to see by eye in a fight and trivial to count here, which is the whole reason this file
exists."""
import bot_mirror as B
import arm_combat as K
import sim_motor_mirror as M
from bot_mirror import Bot, config, decide, DUMMY, SPARRING, HUNTER
from sim_motor_mirror import S, step, X, F, C, L, R, UP, DN, DOD
from loot_mirror import Inv, NONE, CLUB, SPEAR, CHAINMAIL
from arm_combat import combat_step, resolve, init, aim_of
from arena_geometry import world

W = world()
ok = True


def check(n, c, d=""):
    global ok
    ok &= bool(c)
    print(f"  {'PASS' if c else 'FAIL'}  {n}{('  ' + d) if d else ''}")


def fighter(x, facing=1, weapon=NONE, armour=NONE):
    s = S(x, 0.9)
    s.inv = Inv()
    if weapon != NONE: s.inv.equip(weapon)
    if armour != NONE: s.inv.equip(armour)
    init(s)
    s.facing = s.travel = facing
    for _ in range(180):
        tick([s], [0])
        if s.mode == "ground":
            break
    init(s)
    s.facing = s.travel = facing
    return s


def tick(players, inputs, aims=None):
    aims = aims or [(0, 0)] * len(players)
    for p, inp, aim in zip(players, inputs, aims):
        step(p, combat_step(p, inp, aim), W)
    resolve(players)


def spar(skill=SPARRING, weapon=CLUB, foe_weapon=NONE, ticks=60 * 20, seed=7,
         foe_input=lambda t: 0, foe_aim=lambda t: (0, 0), spacing=None):
    """One bot against one dummy human, and everything that happened."""
    bot_s = fighter(-1.0, 1, weapon)
    man = fighter(1.0, -1, foe_weapon)
    brain = Bot(seed)
    cfg = config(skill)

    pressed, changes, swings, hits_dealt, hits_taken = [], 0, 0, 0, 0
    last = None
    gaps = []

    aims = []
    for t in range(ticks):
        flags, aim = decide(brain, bot_s, man, W, cfg)
        pressed.append(flags)
        aims.append(aim)
        if last is not None and flags != last:
            changes += 1
        last = flags

        tick([bot_s, man], [flags, foe_input(t)],
             [aim_of(*aim), aim_of(*foe_aim(t))])

        if bot_s.swing_share > 0: swings += 1
        if man.was_hit: hits_dealt += 1
        if bot_s.was_hit: hits_taken += 1
        gaps.append(abs(F(man.x) - F(bot_s.x)))

    return dict(bot=bot_s, man=man, brain=brain, pressed=pressed, aims=aims,
                changes=changes,
                swings=swings, dealt=hits_dealt, taken=hits_taken, gaps=gaps,
                ticks=ticks, cfg=cfg)


print("1. it thinks on a clock, it does not twitch")
r = spar()
# The decision is held for react..react+jitter frames, so the most changes possible in a run
# is one per shortest hold. Anything near one per TICK is a bot vibrating on the spot.
floor = r["cfg"]["react"]
most = r["ticks"] / floor
check("what it presses changes far less often than once a frame",
      r["changes"] <= most, f"{r['changes']} change(s) in {r['ticks']} ticks, cap {most:.0f}")
check("and that is a real reaction time, not a stutter",
      r["ticks"] / max(1, r["changes"]) >= floor * 0.9,
      f"a decision every {r['ticks'] / max(1, r['changes']):.1f} frames")

# The run of identical frames must never be ONE: that is the signature of the fault.
runs, run, prev = [], 0, None
for f in r["pressed"]:
    if f == prev: run += 1
    else:
        if prev is not None: runs.append(run)
        run, prev = 1, f
runs.append(run)
check("the shortest thing it ever holds is a reaction, not a frame",
      min(runs) >= 2, f"shortest run {min(runs)} frame(s), median {sorted(runs)[len(runs)//2]}")

print("\n2. it answers late, which is what makes it beatable")
# The world changes under it -- the man teleports to the other side -- and the bot must
# take some frames to notice. Measured as frames until what it presses changes, averaged
# over every phase of its clock, so the answer does not depend on where the cycle happened
# to be when the change landed.
cfg = config(SPARRING)
delays = []
for offset in range(cfg["react"] + cfg["jitter"]):
    bot_s = fighter(-1.5, 1, CLUB)
    man = fighter(1.5, -1, CLUB)
    brain = Bot(3 + offset)
    for _ in range(offset + 1):                        # land on a different phase each time
        decide(brain, bot_s, man, W, cfg)

    before, _ = decide(brain, bot_s, man, W, cfg)
    man.x = X(-4.0)                                    # now he is the OTHER way
    for d in range(1, 120):
        # The FLAGS only. The arm is re-pointed every tick by design (it has to be, or
        # nothing could swing), so comparing the whole intent would measure the arm
        # rather than the decision the reaction time is about.
        if decide(brain, bot_s, man, W, cfg)[0] != before:
            delays.append(d)
            break

mean = sum(delays) / len(delays)
check("it does not answer on the frame the world changed", min(delays) >= 1,
      f"quickest {min(delays)} frame(s)")
check("and on average it takes about a reaction to notice",
      mean >= cfg["react"] * 0.4,
      f"{mean:.1f} frames on average, reaction {cfg['react']}-{cfg['react'] + cfg['jitter']}")

print("\n3. it stands at its OWN weapon's range")
for weapon, name in ((CLUB, "club"), (SPEAR, "spear")):
    r = spar(weapon=weapon, ticks=60 * 30, seed=11)
    settled = r["gaps"][len(r["gaps"]) // 3:]          # ignore the walk-in
    mean = sum(settled) / len(settled)
    # How far the weapon actually goes -- arm plus the blade that is drawn -- and not the
    # item's Reach, which is a number in a table measured from the middle of the body and
    # is 0.37 m longer than a spear on a 0.91 m fighter.
    reach = F(K.arm_length() + K.blade(fighter(0, 1, weapon)))
    check(f"a {name} bot holds about its own reach",
          0.35 * reach <= mean <= 1.25 * reach,
          f"stood at {mean:.2f} m, the weapon goes {reach:.2f} m")

print("\n4. it can land a hit, and it can be hit")
r = spar(weapon=CLUB, ticks=60 * 40, seed=5)
check("it hits the other fighter without being steered", r["dealt"] > 0,
      f"{r['dealt']} hit(s), {r['swings']} swing(s)")

# And the other way: a man who only attacks must be able to hurt it.
# And the other way: a bot's body is hittable like anything else.
#
# Stood in reach and left there, rather than walked in. Walking the man at the bot was
# two bugs' worth of indirection -- he strolls straight past it, and after that his
# blade points away -- and none of that is what this line is trying to find out.
h_bot = fighter(-0.6, 1, CLUB)
h_man = fighter(0.0, -1, SPEAR)
h_brain, h_cfg = Bot(5), config(DUMMY)
took = 0
for t in range(60 * 20):
    f, aim = decide(h_brain, h_bot, h_man, W, h_cfg)
    # A full arc, high to low, THROUGH the level where the body is. A narrow waggle
    # above the guard never builds enough tip speed to count -- which is the rule
    # working, and was the first version of this test measuring nothing.
    tick([h_bot, h_man], [f, 0],
         [aim_of(*aim), aim_of(-500, 866 if (t // 27) % 2 else -866)])
    if h_bot.was_hit:
        took += 1
check("and it takes damage like anything else", took > 0 or h_bot.health < 100,
      f"health {h_bot.health}, hit {took} time(s)")

print("\n5. the harmless one is harmless")
r = spar(skill=DUMMY, weapon=CLUB, ticks=60 * 20, seed=9)
# Its first few ticks raise the blade from the spawn pose into the guard, and that IS a
# swing -- correctly, because a moving blade is the only thing that can hurt anybody.
# What "harmless" means is that it never does it again.
settled = sum(1 for a in r["aims"][30:] if a[1] != 0)
check("a dummy never swings", settled == 0,
      f"{settled} swinging frame(s) after the first half second")
check("and never walks off", abs(F(r["bot"].x) - (-1.0)) < 0.6,
      f"moved {abs(F(r['bot'].x) - (-1.0)):.2f} m")
# It holds its weapon level at the man, which IS the guard now -- no button, and
# nothing for the player to practise against except getting round a held blade.
# Carrying is (0, 0) -- stick centred, blade low. Guarding is a real push with no
# vertical component. The first version of this counted "aim_y == 0", which is true of
# both, so it passed on a bot that never lifted its weapon at all.
carried = sum(1 for a in r["aims"] if a == (0, 0))
check("and it carries its weapon low rather than guarding for free",
      carried > len(r["aims"]) * 0.5,
      f"{carried} carrying frame(s) of {len(r['aims'])}")

print("\n6. it aims the weapon, which is the whole of attacking now")
# Against somebody who actually swings. The bot only raises its guard when it can see a
# blade moving, so measuring its guard against a man standing still measures nothing --
# and correctly reported zero when this was first written that way.
r = spar(weapon=CLUB, foe_weapon=CLUB, ticks=60 * 40, seed=13,
         foe_aim=lambda t: (-500, 866 if (t // 15) % 2 else -866))

swinging = sum(1 for a in r["aims"] if a[1] != 0)
check("it throws the blade through an arc rather than holding it out",
      swinging > 0, f"{swinging} swinging frame(s) of {r['ticks']}")

carried = sum(1 for a in r["aims"] if a == (0, 0))
check("and it carries the blade low the rest of the time, not guarding for free",
      carried > 0, f"{carried} carrying frame(s)")

# The guard rule, checked directly rather than waited for in a spar.
#
# Two attempts at the emergent version both read zero, and both for real reasons worth
# writing down. A bot that is WINNING never guards, because its opponent is stunned and
# never gets a blade moving. And TipSpeed is zeroed on the tick its owner is hit, so a
# foe the bot is successfully beating has no tell to read at all. Neither is a bug --
# they are the rule working -- but they mean emergent play measures the matchup.
g_cfg = config(SPARRING)
raised = 0
for seed in range(40):
    # Inside its own preferred range, or it takes the "close the distance" branch and
    # never reaches the guard decision at all. Half the band, computed rather than
    # written down: at a hard-coded 0.35 m each this sat a centimetre outside the band
    # the moment the blade became the one that is drawn, and read as "never guards".
    g_half = F(K.arm_length() + K.blade(fighter(0, 1, CLUB))) * 0.65 / 2
    g_bot = fighter(-g_half, 1, CLUB)
    g_man = fighter(g_half, -1, CLUB)
    g_brain = Bot(seed)
    g_man.tip_speed = X(0.2)              # his blade is moving, and the bot can see it
    g_brain.think = 0                     # and it is allowed to decide this tick
    decide(g_brain, g_bot, g_man, W, g_cfg)
    if g_brain.guard_hold > 0:
        raised += 1
check("and it raises a guard when it can see a blade moving", raised > 0,
      f"{raised} of 40 seeds guarded; the roll is {g_cfg['guard'] * 3} permille")

# Backing off with the blade still on the man: travel one way, aim the other.
withdrew = sum(1 for f, a in zip(r["pressed"], r["aims"])
               if (f & L and a[0] > 0) or (f & R and a[0] < 0))
check("and some of that is a fighting withdrawal", withdrew > 0,
      f"{withdrew} frame(s) going one way and aiming the other")

print("\n7. two bots do not move in lockstep")
a, b = Bot(1), Bot(2)
me = fighter(-1.0, 1, CLUB)
him = fighter(1.0, -1, CLUB)
cfg = config(SPARRING)
same = 0
for t in range(60 * 20):
    fa, aa = decide(a, me, him, W, cfg)
    fb, ab = decide(b, him, me, W, cfg)
    if a.think == b.think: same += 1
    tick([me, him], [fa, fb], [aim_of(*aa), aim_of(*ab)])
check("their clocks drift apart", same < 60 * 20 * 0.6,
      f"in step on {same} of {60 * 20} ticks")

print("\n8. nothing it presses is pressed while it cannot act")
bot_s = fighter(0, 1, CLUB)
man = fighter(0.6, -1, CLUB)
brain, cfg = Bot(4), config(SPARRING)
pressed_while_down = 0
for t in range(60 * 20):
    f, aim = decide(brain, bot_s, man, W, cfg)
    if (bot_s.dead or B.reeling(bot_s)) and f != 0:
        pressed_while_down += 1
    tick([bot_s, man], [f, 0], [aim_of(*aim), aim_of(-1000, 0)])
check("it holds nothing while reeling or dead", pressed_while_down == 0,
      f"{pressed_while_down} frame(s)")

print("\n" + ("ALL PASS" if ok else "SOME FAILED"))
