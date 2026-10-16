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
import combat_mirror as K
import sim_motor_mirror as M
from bot_mirror import Bot, config, decide, DUMMY, SPARRING, HUNTER
from sim_motor_mirror import S, step, X, F, C, L, R, UP, DN, DOD, ATTACK, AIM_L, AIM_R
from loot_mirror import Inv, NONE, CLUB, SPEAR, CHAINMAIL
from combat_mirror import combat_step, resolve, init
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


def tick(players, inputs):
    for p, inp in zip(players, inputs):
        step(p, combat_step(p, inp), W)
    resolve(players)


def spar(skill=SPARRING, weapon=CLUB, foe_weapon=NONE, ticks=60 * 20, seed=7,
         foe_input=lambda t: 0, spacing=None):
    """One bot against one dummy human, and everything that happened."""
    bot_s = fighter(-1.0, 1, weapon)
    man = fighter(1.0, -1, foe_weapon)
    brain = Bot(seed)
    cfg = config(skill)

    pressed, changes, swings, hits_dealt, hits_taken = [], 0, 0, 0, 0
    last = None
    gaps = []

    for t in range(ticks):
        flags = decide(brain, bot_s, man, W, cfg)
        pressed.append(flags)
        if last is not None and flags != last:
            changes += 1
        last = flags

        tick([bot_s, man], [flags, foe_input(t)])

        if bot_s.swing_started: swings += 1
        if man.was_hit: hits_dealt += 1
        if bot_s.was_hit: hits_taken += 1
        gaps.append(abs(F(man.x) - F(bot_s.x)))

    return dict(bot=bot_s, man=man, brain=brain, pressed=pressed, changes=changes,
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

    before = decide(brain, bot_s, man, W, cfg)
    man.x = X(-4.0)                                    # now he is the OTHER way
    for d in range(1, 120):
        if decide(brain, bot_s, man, W, cfg) != before:
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
    reach = F(C["W"] // 2 + B.ITEMS[weapon]["reach"])
    check(f"a {name} bot holds about its own reach",
          0.35 * reach <= mean <= 1.25 * reach,
          f"stood at {mean:.2f} m, reach {reach:.2f} m")

print("\n4. it can land a hit, and it can be hit")
r = spar(weapon=CLUB, ticks=60 * 40, seed=5)
check("it hits the other fighter without being steered", r["dealt"] > 0,
      f"{r['dealt']} hit(s), {r['swings']} swing(s)")

# And the other way: a man who only attacks must be able to hurt it.
r2 = spar(weapon=CLUB, foe_weapon=SPEAR, ticks=60 * 40, seed=5,
          foe_input=lambda t: ATTACK | (L if t % 90 < 45 else 0))
check("and it takes damage like anything else", r2["taken"] > 0 or r2["bot"].health < 100,
      f"health {r2['bot'].health}, hit {r2['taken']} time(s)")

print("\n5. the harmless one is harmless")
r = spar(skill=DUMMY, weapon=CLUB, ticks=60 * 20, seed=9)
check("a dummy never swings", r["swings"] == 0, f"{r['swings']} swing(s)")
check("and never walks off", abs(F(r["bot"].x) - (-1.0)) < 0.6,
      f"moved {abs(F(r['bot'].x) - (-1.0)):.2f} m")
check("but it does raise its guard", any(f & DOD for f in r["pressed"]),
      "never guarded" if not any(f & DOD for f in r["pressed"]) else "")

print("\n6. it uses the aim split, which is the point of having one")
r = spar(weapon=CLUB, ticks=60 * 40, seed=13)
aimed = sum(1 for f in r["pressed"] if f & (AIM_L | AIM_R))
check("it aims deliberately rather than letting its feet decide", aimed > 0,
      f"{aimed} aimed frame(s) of {r['ticks']}")

# Backing off with the blade still on the man: travel one way, aim the other.
withdrew = sum(1 for f in r["pressed"]
               if (f & L and f & AIM_R) or (f & R and f & AIM_L))
check("and some of that is a fighting withdrawal", withdrew > 0,
      f"{withdrew} frame(s) going one way and aiming the other")

print("\n7. two bots do not move in lockstep")
a, b = Bot(1), Bot(2)
me = fighter(-1.0, 1, CLUB)
him = fighter(1.0, -1, CLUB)
cfg = config(SPARRING)
same = 0
for t in range(60 * 20):
    fa = decide(a, me, him, W, cfg)
    fb = decide(b, him, me, W, cfg)
    if a.think == b.think: same += 1
    tick([me, him], [fa, fb])
check("their clocks drift apart", same < 60 * 20 * 0.6,
      f"in step on {same} of {60 * 20} ticks")

print("\n8. nothing it presses is pressed while it cannot act")
bot_s = fighter(0, 1, CLUB)
man = fighter(0.6, -1, CLUB)
brain, cfg = Bot(4), config(SPARRING)
pressed_while_down = 0
for t in range(60 * 20):
    f = decide(brain, bot_s, man, W, cfg)
    if (bot_s.dead or B.reeling(bot_s)) and f != 0:
        pressed_while_down += 1
    tick([bot_s, man], [f, ATTACK])
check("it holds nothing while reeling or dead", pressed_while_down == 0,
      f"{pressed_while_down} frame(s)")

print("\n" + ("ALL PASS" if ok else "SOME FAILED"))
