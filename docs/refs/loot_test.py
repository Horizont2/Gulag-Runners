"""Checks the chests of docs/03 against the real motor on the real arena.

The chest boxes here are the ones placed in SetupScene, and the walking between them is the
actual movement simulation, so the times below are the times a player will really see."""
import sim_motor_mirror as M
import loot_mirror as T
from sim_motor_mirror import S, step, X, F, L, R, UP, DN, JMP, ACTION
from arena_geometry import floor_h, ladder_x, world
import collections
from loot_mirror import (Chest, Inv, loot_step, frames_to_open, fresh, pool, step_ground,
                         drop_item, CRATE, LOCKER, SAFE, NONE, FISTS, CLUB, SPEAR, CHAINMAIL,
                         BANDAGE, ITEMS, CHEST_CFG, D, RESTING, FLYING,
                         WEAPON, ARMOUR, UTILITY, HELMET, BELT, WORN,
                         LEATHER, PLATE, LEATHER_CAP, MAIL_COIF, GREAT_HELM,
                         ROPE_BELT, TOOL_BELT, WAR_BELT,
                         Rng, roll_chest, roll_tier, TIER_ODDS, deal,
                         WEAPON, ARMOUR, UTILITY)

W = world()

# The six chests of SetupScene: (name, kind, contents, centre x, floor y, width, height)
# Reach boxes as the three chest prefabs define them.
LAYOUT = [
    ("Chest_F0_Crate_L",  CRATE,  CLUB,      -5.5, 0.0, 0.7, 0.50),
    ("Chest_F0_Locker_L", LOCKER, CHAINMAIL, -2.5, 0.0, 0.7, 0.60),
    ("Chest_F1_Safe_L",   SAFE,   SPEAR,     -2.0, 3.0, 0.8, 0.70),
    ("Chest_F1_Crate_R",  CRATE,  BANDAGE,    4.5, 3.0, 0.7, 0.50),
    ("Chest_F2_Safe_L",   SAFE,   SPEAR,     -4.0, 6.0, 0.8, 0.70),
    ("Chest_F2_Crate_R",  CRATE,  CLUB,       3.0, 6.0, 0.7, 0.50),
]


def build():
    return [Chest(kind, contents,
                  (X(x - w / 2), X(y), X(x + w / 2), X(y + h)))
            for _, kind, contents, x, y, w, h in LAYOUT]


ok = True


def check(n, c, d=""):
    global ok
    ok &= bool(c)
    print(f"  {'PASS' if c else 'FAIL'}  {n}{('  ' + d) if d else ''}")


GROUND = pool(6)


def reset_ground():
    for g in GROUND:
        g.clear()


def spawn(x, y, weapon=NONE):
    s = S(x, y)
    s.inv = Inv()
    if weapon != NONE:
        s.inv.equip(weapon)
    for _ in range(180):
        tick(s, 0)
        if s.mode == "ground":
            break
    return s


def approach_sign(inp):
    return 1 if inp == R else -1


def tick(s, inp, chests=None, player=0, shared=True):
    # Loot in the air belongs to nobody, so it steps once per tick for the whole match. With two
    # players in a test, only one of them passes shared=True, exactly as the tick owner does.
    if shared:
        step_ground(GROUND, W)
    step(s, inp, W)
    loot_step(s, player, inp, chests or [], GROUND)


def walk_to(s, x, chests, player=0, limit=900):
    """Walks with the real motor. Returns the ticks it took."""
    for t in range(limit):
        inp = R if F(s.x) < x - 0.05 else (L if F(s.x) > x + 0.05 else 0)
        tick(s, inp, chests, player)
        if abs(F(s.x) - x) <= 0.05:
            return t + 1
    return limit


def hold_open(s, chests, player=0, limit=900):
    """Holds ACTION until a chest gives up its loot. Returns (ticks, item on the floor)."""
    for t in range(limit):
        tick(s, ACTION, chests, player)
        if s.opened_chest >= 0:
            return t + 1, chests[s.opened_chest].contents
    return limit, NONE


def loose(s):
    """The nearest piece of loot lying on the floor, or None."""
    live = [g for g in GROUND if g.live and g.state == RESTING]
    return min(live, key=lambda g: abs(g.x - s.x)) if live else None


def collect(s, chests, player=0, limit=300, swap=False):
    """Walks to the nearest loose item and takes it. With swap, taps action over a full slot."""
    for t in range(limit):
        g = loose(s)
        inp = 0
        if g is not None:
            if F(s.x) < F(g.x) - 0.05:
                inp = R
            elif F(s.x) > F(g.x) + 0.05:
                inp = L
            elif swap and t % 2 == 0:
                inp = ACTION
        tick(s, inp, chests, player)
        if s.picked != NONE:
            return t + 1, s.picked
    return limit, NONE


def open_and_take(s, chests, player=0):
    a, item = hold_open(s, chests, player)
    b, taken = collect(s, chests, player)
    return a + b, taken


print("1. the table of docs/03 is the table the code produces")
rows = [("crate, with a club", CRATE, CLUB, 0.6),
        ("locker, with a club", LOCKER, CLUB, 2.5),
        ("safe, forced bare-handed", SAFE, NONE, 6.0)]
for label, kind, weapon, want in rows:
    got = frames_to_open(kind, weapon) / 60
    check(label, abs(got - want) < 0.05, f"{got:.2f} s, docs/03 says {want} s")
check("bare hands are the slow way in",
      frames_to_open(CRATE, NONE) > frames_to_open(CRATE, CLUB),
      f"crate {frames_to_open(CRATE, NONE)/60:.1f} s bare vs "
      f"{frames_to_open(CRATE, CLUB)/60:.1f} s with a club")

print("\n2. walking up to a chest and opening it, on the real arena")
chests = build()
s = spawn(-8.3, 0.9)
walked = walk_to(s, -5.5, chests)
check("reaches the first crate", abs(F(s.x) + 5.5) < 0.1, f"{walked/60:.2f} s of walking")
took, item = hold_open(s, chests)
check("the crate opens and the club comes OUT of it", chests[0].opened and item == CLUB,
      f"{took/60:.2f} s bare-handed -> {ITEMS[item]['name']} on the floor")
check("it is not in the slot yet", s.inv.bare, "a chest reveals, it does not hand over")
took2, taken = collect(s, chests)
check("walking over it picks it up, with no button", taken == CLUB,
      f"{took2/60:.2f} s later")
check("and the club is in hand", s.inv.weapon == CLUB and s.inv.durability == 14,
      f"durability {s.inv.durability}")

print("\n3. the weapon is also the crowbar (docs/03)")
s2 = spawn(-2.5, 0.9, CLUB)
took, item = hold_open(s2, chests, 0)
check("the club opens the locker in half the time", abs(took / 60 - 2.5) < 0.1,
      f"{took/60:.2f} s")
check("and it cost two of its fourteen uses", s2.inv.durability == 12,
      f"durability {s2.inv.durability}")
collect(s2, chests)
check("the chainmail goes on by itself: the armour slot was free", s2.inv.armour == CHAINMAIL)

spear = spawn(-2.0, floor_h + 0.9, SPEAR)
safes = 0
for _ in range(4):
    c = [Chest(SAFE, BANDAGE, (X(-2.45), X(floor_h), X(-1.55), X(floor_h + 0.9)))]
    spear.opening = -1
    if spear.inv.weapon == NONE:
        break
    hold_open(spear, c, 0)
    if c[0].opened:
        safes += 1
check("a spear forces three safes and then breaks", safes == 3 and spear.inv.weapon == NONE,
      f"{safes} safes at 3 of 9 durability = {3/9*100:.0f}% each, docs/03 says ~35%")
check("and you are back to bare hands", spear.inv.bare)

print("\n4. noise is the price (docs/01: sound is the only way to be found)")
chests = build()
s = spawn(-2.5, 0.9)
s.stepn = 0
tick(s, ACTION, chests, 0)
bare_noise = s.noise
s2 = spawn(-2.5, 0.9, CLUB)
s2.stepn = 0
tick(s2, ACTION, chests, 0)
check("forcing a locker bare-handed is Medium", bare_noise == 2, f"level {bare_noise}")
check("prying it with a weapon is Loud", s2.noise == 3, f"level {s2.noise}")
chests = build()
s3 = spawn(-5.5, 0.9, CLUB)
s3.stepn = 0
tick(s3, ACTION, chests, 0)
check("a crate stays Quiet either way", s3.noise == 1, f"level {s3.noise}")

print("\n5. letting go drains, it does not reset")
chests = build()
s = spawn(-2.5, 0.9)
for _ in range(120):                                  # two seconds of a five-second locker
    tick(s, ACTION, chests, 0)
held = chests[1].progress
check("progress built up", held > 0, f"{held/1000:.0f} of {CHEST_CFG[LOCKER]['bare']} frames")
tick(s, 0, chests, 0)
check("one tick off the button costs two", chests[1].progress == held - 2000,
      f"{chests[1].progress/1000:.0f} frames left")
for _ in range(200):
    tick(s, 0, chests, 0)
check("it drains away and the claim is released",
      chests[1].progress == 0 and chests[1].opener == -1 and s.opening == -1)

print("\n6. two players, one chest")
chests = build()
a = spawn(-2.5, 0.9)
b = spawn(-2.5, 0.9)
for _ in range(60):
    tick(a, ACTION, chests, 0)
check("player one claims it", chests[1].opener == 0)
before = chests[1].progress
for _ in range(60):
    tick(b, ACTION, chests, 1)
check("player two cannot touch a claimed chest",
      chests[1].progress == before and chests[1].opener == 0 and b.opening == -1,
      f"progress {chests[1].progress/1000:.0f} frames, unchanged")
for _ in range(400):
    tick(a, 0, chests, 0)
for _ in range(60):
    tick(b, ACTION, chests, 1)
check("once it has drained, player two can start", chests[1].opener == 1)

print("\n7. opening is something you do standing on the floor")
chests = build()
s = spawn(-5.5, 0.9)
airborne, gained_in_air = 0, 0
for _ in range(120):
    tick(s, (JMP | ACTION) if airborne < 3 else ACTION, chests, 0)
    if s.mode != "air":
        break
    airborne += 1
    gained_in_air = max(gained_in_air, chests[0].progress)
check("the jump leaves the ground", airborne >= 5, f"{airborne} airborne ticks")
check("and no progress is made in mid-air", gained_in_air == 0,
      f"progress while airborne {gained_in_air}")
check("it resumes the moment the feet are down",
      s.mode == "ground" and chests[0].progress > 0, f"mode={s.mode}")

print("\n8. the time economy of docs/03, walked rather than assumed")
reset_ground()
# docs/03: "22 seconds is about 2 crates + 1 locker + 1 safe with running in between".
# This is that exact shopping list, on this arena, with the real motor doing the running.
chests = build()
s = spawn(-8.3, 0.9)
budget, spent, got, legs = 22 * 60, 0, [], []

def leg(label, ticks):
    legs.append((label, ticks))
    return ticks

spent += leg("walk to the crate", walk_to(s, -5.5, chests))
t, _ = hold_open(s, chests); spent += leg("crate, bare-handed", t)
t, item = collect(s, chests); spent += leg("step over and take it", t); got.append(ITEMS[item]['name'])
spent += leg("walk to the locker", walk_to(s, -2.5, chests))
t, _ = hold_open(s, chests); spent += leg("locker, with the club", t)
t, item = collect(s, chests); spent += leg("take it", t); got.append(ITEMS[item]['name'])
spent += leg("back to the ladder", walk_to(s, ladder_x(0), chests))

climb = 0
for _ in range(600):
    tick(s, UP, chests, 0)
    climb += 1
    if s.mode == "ground" and F(s.y) >= floor_h - 0.05:
        break
spent += leg("climb a floor", climb)

spent += leg("walk to the safe", walk_to(s, -2.0, chests))
t, _ = hold_open(s, chests); spent += leg("safe, forced", t)
t, item = collect(s, chests, swap=True)
spent += leg("swap the club for the spear", t); got.append(ITEMS[item]['name'])
spent += leg("walk to the far crate", walk_to(s, 4.5, chests))
t, _ = hold_open(s, chests); spent += leg("crate", t)
t, item = collect(s, chests); spent += leg("take it", t); got.append(ITEMS[item]['name'])

for label, ticks in legs:
    print(f"        {ticks/60:5.2f} s  {label}")

opened = sum(1 for c in chests if c.opened)
carrying = [ITEMS[i]["name"] for i in (s.inv.weapon, s.inv.armour, s.inv.utility) if i != NONE]
check("two crates, a locker and a safe, with the running, fit the phase",
      spent <= budget and opened == 4,
      f"{spent/60:.1f} s of {budget/60:.0f}, {opened} chests opened, "
      f"picked up {', '.join(got)}, carrying {', '.join(carrying)}")
check("and the phase is not over before it starts", spent >= budget * 0.6,
      f"{spent/budget*100:.0f}% of the phase spent")
check("you cannot have everything: two chests are still shut",
      opened < len(chests), f"{len(chests)-opened} left")

print("\n9. loot comes out and lands where it can be reached")
reset_ground()
chests = build()
s = spawn(-6.4, 0.9)                                   # standing LEFT of the crate at -5.5
walk_to(s, -5.5, chests)
hold_open(s, chests)
g = next(x for x in GROUND if x.live)
check("it leaves the chest in the air", g.state == FLYING, f"state {g.state}")
for _ in range(90):
    tick(s, 0, chests)
    if g.state == RESTING:
        break
check("and settles on the floor", g.state == RESTING and abs(F(g.y) - 0.22) < 0.02,
      f"y={F(g.y):.2f}")
check("thrown back towards whoever opened it, not past it",
      F(g.x) < -5.5, f"chest at -5.50, loot at {F(g.x):.2f}, opener came from the left")
check("within one step", abs(F(g.x) + 5.5) < 1.0, f"{abs(F(g.x)+5.5):.2f} m away")
check("not inside the floor", not T.blocked(g.x, g.y, W))

print("\n10. a wall stops it, it does not sail through")
reset_ground()
# fired hard at the left outer wall, which stands at x = -10.8
T.launch(GROUND, CLUB, X(-10.0), X(1.0), -T.M(9000), 0, 0)
for _ in range(120):
    step_ground(GROUND, W)
g = GROUND[0]
check("stopped on this side of the wall", F(g.x) > -10.8, f"x={F(g.x):.2f}, wall at -10.80")
check("and on the floor", g.state == RESTING, f"state {g.state}")

print("\n11. a free slot takes it, a full slot is a decision")
reset_ground()
s = spawn(-5.5, 0.9)
T.launch(GROUND, CHAINMAIL, X(-5.5), X(0.3), 0, 0, 0)
for _ in range(30):
    tick(s, 0, chests)
check("armour slot empty: walking over it is enough", s.inv.armour == CHAINMAIL)

reset_ground()
s = spawn(-5.5, 0.9, CLUB)
T.launch(GROUND, SPEAR, X(-5.5), X(0.3), 0, 0, 0)
for _ in range(60):
    tick(s, 0, chests)
check("weapon slot full: walking over it does nothing", s.inv.weapon == CLUB,
      f"still holding {ITEMS[s.inv.weapon]['name']}")
check("but the button knows what it would do", s.standing_on >= 0, f"index {s.standing_on}")
for t in range(30):
    tick(s, ACTION if t % 2 == 0 else 0, chests)
    if s.picked != NONE:
        break
check("a press swaps it", s.inv.weapon == SPEAR, f"now holding {ITEMS[s.inv.weapon]['name']}")
check("and the club is on the floor", any(g.live and g.item == CLUB for g in GROUND))

print("\n12. what you drop is not instantly yours again")
club = next(g for g in GROUND if g.live and g.item == CLUB)
check("it comes out locked", club.lock > 0, f"{club.lock} frames")
held = s.inv.weapon
for _ in range(20):
    tick(s, 0, chests)
check("still the spear while the lock runs", s.inv.weapon == held)

print("\n13. a full floor recycles the stalest thing on it")
reset_ground()
for i in range(len(GROUND)):
    T.launch(GROUND, BANDAGE, X(-5 + i * 0.3), X(0.3), 0, 0, 0)
for _ in range(10):
    step_ground(GROUND, W)
GROUND[0].age = 9999                                   # the stalest
slot = T.launch(GROUND, SPEAR, X(0.0), X(0.3), 0, 0, 0)
check("the drop still happens", slot >= 0, f"slot {slot}")
check("and it took the oldest one's place", GROUND[slot].item == SPEAR and slot == 0,
      f"slot {slot}")

print("\n14. loot popped over a hatch falls to the floor below")
reset_ground()
T.launch(GROUND, CLUB, X(ladder_x(0)), X(floor_h + 0.5), 0, 0, 0)
for _ in range(240):
    step_ground(GROUND, W)
g = GROUND[0]
check("it fell through the hole rather than resting in mid-air",
      g.state == RESTING and F(g.y) < 1.0, f"y={F(g.y):.2f}, dropped from {floor_h + 0.5}")

print("\n14b. five slots, and nothing shares one")
# docs/02 started at three slots and three was one idea short. A silhouette has a head and
# a waist, and folding both into "armour" meant a coif and a breastplate competing for the
# same square -- so finding a helmet was a reason to throw your plate away.
for kind, name in ((HELMET, "helmet"), (ARMOUR, "chest armour"), (BELT, "belt")):
    rarities = sorted(i for i, v in ITEMS.items()
                      if v["kind"] == kind and v.get("weight", 0) > 0)
    tiers = sorted(ITEMS[i]["tier"] for i in rarities)
    check(f"there are three {name}s, one per rarity", tiers == [1, 2, 3],
          ", ".join(ITEMS[i]["name"] for i in rarities))

reset_ground()
s = spawn(-5.5, 0.9)
for item in (PLATE, GREAT_HELM, WAR_BELT):
    reset_ground()
    T.launch(GROUND, item, X(-5.5), X(0.3), 0, 0, 0)
    for _ in range(40):
        tick(s, 0, [])
check("a whole suit goes on without one piece displacing another",
      (s.inv.armour, s.inv.helmet, s.inv.belt) == (PLATE, GREAT_HELM, WAR_BELT),
      f"{ITEMS[s.inv.helmet]['name']} / {ITEMS[s.inv.armour]['name']} / "
      f"{ITEMS[s.inv.belt]['name']}")
check("and that is what docs/02 priced armour at, summed", s.inv.reduction == 450,
      f"{s.inv.reduction}‰")

# A full slot is still a decision, per slot and not per body.
reset_ground()
T.launch(GROUND, MAIL_COIF, X(-5.5), X(0.3), 0, 0, 0)
for _ in range(40):
    tick(s, 0, [])
check("a worse helmet does not walk itself onto a full head", s.inv.helmet == GREAT_HELM,
      f"still wearing {ITEMS[s.inv.helmet]['name']}")
check("but the button knows it would swap", s.standing_on >= 0, f"index {s.standing_on}")

# Two new findable kinds in every tier dilute the chance of finding a WEAPON, which is the
# one thing a round must not stop handing out. Measured, not assumed.
KINDS = [CRATE, CRATE, LOCKER, LOCKER, SAFE, CRATE]
rounds, armed, worn_seen = 4000, 0, 0
for seed in range(1, rounds + 1):
    rng = Rng(seed)
    items = [roll_chest(k, rng.fork(i + 1)) for i, k in enumerate(KINDS)]
    if any(ITEMS[i]["kind"] == WEAPON for i in items):
        armed += 1
    worn_seen += sum(1 for i in items if ITEMS[i]["kind"] in WORN)
check("a round of six chests still almost always holds a weapon", armed / rounds > 0.98,
      f"{100 * armed / rounds:.1f}% of rounds")
check("and now hands out most of a suit as well", 1.4 < worn_seen / rounds < 2.6,
      f"{worn_seen / rounds:.2f} worn pieces per round")

print("\n15. the same inputs give the same frames")
r = []
for _ in range(2):
    reset_ground()
    c = build()
    p = spawn(-8.3, 0.9)
    walk_to(p, -5.5, c)
    hold_open(p, c)
    collect(p, c)
    r.append((p.x, p.y, p.inv.weapon, p.inv.durability, c[0].progress,
              [(g.item, g.x, g.y, g.state) for g in GROUND]))
check("bit-identical replay", r[0] == r[1], f"{r[0]}")

print("\nTurning to face the chest being searched")
# A fighter levering a crate open with his back to it is what the search animation would show
# without this. Each case walks him PAST the chest's middle, so the facing he arrives with is
# the wrong one, and then holds the button: the chest has to turn him, and hand the facing
# back to movement the moment he lets go.
for approach, stop, want, label in ((R, 0.45, -1, "walked in from the left, past its middle"),
                                    (L, -0.45, 1, "walked in from the right, short of it")):
    chests = build()
    cx = LAYOUT[0][3]                                   # Chest_F0_Crate_L, centre x -5.5
    s = spawn(cx - approach_sign(approach) * 2.0, 0.3, CLUB)
    walk_to(s, cx + stop, chests)
    arrived = s.facing
    check(f"he reaches the chest, {label}", abs(F(s.x) - (cx + stop)) < 0.1,
          f"x {F(s.x):.2f}, chest centre {cx:.1f}, facing {arrived:+d}")

    for _ in range(10):
        tick(s, ACTION, chests)
    check(f"he is prying at it, {label}", s.opening >= 0, f"opening {s.opening}")
    check(f"and has turned to face it, {label}", s.opening >= 0 and s.facing == want,
          f"facing {s.facing:+d}, arrived facing {arrived:+d}")

    # Let go and walk: facing is movement's business again, with nothing left over.
    for _ in range(30):
        tick(s, approach, chests)
    check(f"and facing is movement's again once he lets go, {label}",
          s.facing == approach_sign(approach), f"facing {s.facing:+d}")

# ---------------------------------------------------------------- the deal and the economy
print("\nThe simulation's only source of chance")
# A known answer, written into Rng.cs as well, so the two implementations cannot drift apart
# in silence. If this goes red, one side of the deal is dealing a different arena.
r = Rng(1)
check("xorshift32 is the sequence both sides agree on",
      [r.next() for _ in range(6)] ==
      [270369, 67634689, 2647435461, 307599695, 2398689233, 745495504])
check("and seed 0 is mapped off the one state it could never leave",
      Rng(0).state == 0x9E3779B9, f"{Rng(0).state}")

print("\nWhat a chest is worth (LootTable)")
ROLLS = 20000
for kind, name in ((CRATE, "crate"), (LOCKER, "locker"), (SAFE, "safe")):
    seen = collections.Counter()
    for s in range(1, ROLLS + 1):
        seen[ITEMS[roll_chest(kind, Rng(s))]["tier"]] += 1
    share = [100 * seen[t] / ROLLS for t in (1, 2, 3)]
    want = TIER_ODDS[kind]
    print(f"   {name:7s} " + "  ".join(f"T{t} {share[t-1]:5.1f}% (want {want[t-1]}%)"
                                       for t in (1, 2, 3)))
    check(f"a {name} pays out the tiers the table says",
          all(abs(share[i] - want[i]) < 2.0 for i in range(3)))

# Nothing that should never drop ever does.
never = set()
for kind in (CRATE, LOCKER, SAFE):
    for s in range(1, 4000):
        got = roll_chest(kind, Rng(s))
        if ITEMS[got].get("weight", 0) <= 0:
            never.add(ITEMS[got]["name"])
check("nothing with no weight is ever dealt", not never,
      "fists and None stay out" if not never else f"dealt {never}")

# Within a tier, rarity is the weight and not the order of the list.
pool = [(i, v) for i, v in sorted(ITEMS.items())
        if v["kind"] != 0 and v.get("tier") == 2 and v.get("weight", 0) > 0]
seen = collections.Counter()
for s in range(1, 40001):
    seen[roll_chest(SAFE, Rng(s))] += 1
t2 = sum(seen[i] for i, _ in pool)
worst = 0.0
for i, v in pool:
    want = 100 * v["weight"] / sum(w["weight"] for _, w in pool)
    got = 100 * seen[i] / t2
    worst = max(worst, abs(got - want))
check("and within a tier each item comes up as often as its weight",
      worst < 2.5, f"worst item is {worst:.1f} points off its share")

print("\nThe economy of one round")


class Pinned:
    """A chest as LootTable.Deal sees it: a kind, and contents only if they were pinned."""
    def __init__(s, kind, pinned=NONE):
        s.kind, s.pinned, s.contents = kind, pinned, NONE


# Arena_Demo's six: two of each kind, mirrored across the arena (docs/01).
def round_of(seed):
    chests = [Pinned(CRATE), Pinned(CRATE), Pinned(LOCKER),
              Pinned(LOCKER), Pinned(SAFE), Pinned(SAFE)]
    deal(chests, seed)
    return [c.contents for c in chests]


tiers = collections.Counter()
kinds = collections.Counter()
golds = collections.Counter()
for s in range(1, 5001):
    got = round_of(s)
    gold = 0
    for item in got:
        tiers[ITEMS[item]["tier"]] += 1
        kinds[ITEMS[item]["kind"]] += 1
        if ITEMS[item]["tier"] == 3:
            gold += 1
    golds[gold] += 1

total = sum(tiers.values())
print("   six chests, 5000 rounds dealt:")
for t in (1, 2, 3):
    print(f"      T{t}: {tiers[t] / 5000:.2f} per round ({100 * tiers[t] / total:.0f}% of drops)")
print(f"      weapons {kinds[WEAPON] / 5000:.2f}, armour {kinds[ARMOUR] / 5000:.2f}, "
      f"utility {kinds[UTILITY] / 5000:.2f} per round")
print("   gold items in a round: " +
      ", ".join(f"{n}x {100 * golds[n] / 5000:.0f}%" for n in sorted(golds)))

check("a round always has something to find", tiers[1] + tiers[2] + tiers[3] == 6 * 5000)
check("gold is rare enough to be the reason you went for the safe",
      0.8 < tiers[3] / 5000 < 1.6, f"{tiers[3] / 5000:.2f} gold items per round of six chests")
check("and a round with no gold at all is uncommon but possible",
      0.05 < golds[0] / 5000 < 0.45, f"{100 * golds[0] / 5000:.0f}% of rounds have none")
check("every round offers both a weapon and something to wear",
      kinds[WEAPON] / 5000 > 2.5 and kinds[ARMOUR] / 5000 > 1.0,
      f"{kinds[WEAPON] / 5000:.1f} weapons, {kinds[ARMOUR] / 5000:.1f} armour")

# The two sides get the same chests and different contents — docs/01's whole premise.
same = 0
for s in range(1, 2001):
    got = round_of(s)
    left, right = got[0::2], got[1::2]
    if left == right:
        same += 1
check("the two sides of the arena are dealt differently", same / 2000 < 0.05,
      f"{100 * same / 2000:.1f}% of rounds deal both sides the same three items")

# Same seed, same arena. docs/06 has no room for anything else.
check("the same seed deals the same round", round_of(777) == round_of(777),
      " ".join(ITEMS[i]["name"] for i in round_of(777)))
check("and a different seed does not", round_of(777) != round_of(778))

# ---------------------------------------------------------------- how big it is drawn
print("\nHow big a weapon is drawn (docs/02: reach is the difference)")
import sim_motor_mirror as _M

import loot_mirror as _L
HEIGHT = _M.F(_M.C["H"])
ARM = ITEMS[FISTS]["reach"] / 65536.0
REACH_TO_LENGTH, MIN_LENGTH, MAX_LENGTH = 0.73, 0.28, 1.3
BASE_OF_BODY = 0.27
BASE = BASE_OF_BODY * HEIGHT


def drawn(item):
    """What ItemModels.Fit scales a model's longest axis to, in metres.

    A haft PLUS the reach it adds, not the reach alone. Every weapon has a grip and a head
    before it has any reach at all, so a straight line through the origin was wrong at both
    ends: the dagger adds 0.10 m and landed on the minimum in a heap with the mace and the
    gladius, while the warhammer came out at 119% of the fighter's own height."""
    reach = ITEMS[item].get("reach", 0) / 65536.0
    if reach <= ARM:
        return 0.0
    return min(MAX_LENGTH, max(MIN_LENGTH, BASE + (reach - ARM) * REACH_TO_LENGTH))


weapons = [i for i in sorted(ITEMS) if drawn(i) > 0]
for i in weapons:
    print(f"   {ITEMS[i]['name']:14s} {drawn(i):4.2f} m   {100 * drawn(i) / HEIGHT:5.1f}% "
          f"of the fighter")

# A pack is authored for a 1.8 m human and this fighter is 0.91 m. Left alone, every weapon
# in the arena was a metre or two long on a fighter under a metre tall, which is what sizing
# off the model's own mesh is here to stop happening again.
check("nothing is drawn longer than a fighter and a half",
      all(drawn(i) < HEIGHT * 1.5 for i in weapons),
      f"longest {max(drawn(i) for i in weapons):.2f} m against a {HEIGHT:.2f} m fighter")
check("and nothing is too small to see",
      all(drawn(i) >= MIN_LENGTH - 1e-9 for i in weapons),
      f"shortest {min(drawn(i) for i in weapons):.2f} m")
check("a spear is drawn longer than a sword, which is longer than a dagger",
      drawn(_L.SPEAR) > drawn(_L.SWORD) > drawn(_L.DAGGER),
      f"{drawn(_L.SPEAR):.2f} > {drawn(_L.SWORD):.2f} > {drawn(_L.DAGGER):.2f}")
check("the order on screen is the order in the hitbox, for every pair",
      all((ITEMS[a]["reach"] > ITEMS[b]["reach"]) == (drawn(a) > drawn(b))
          for a in weapons for b in weapons
          if drawn(a) != drawn(b) and ITEMS[a]["reach"] != ITEMS[b]["reach"]),
      "a weapon that reaches further is never drawn shorter")
# The family, not just its ends. Four of these used to sit between 31% and 52% of the
# fighter -- the dagger pinned to the minimum with the mace and the gladius just above it --
# which is what "some weapons are too small in the hands" was: not one bad number but a
# crowd at the bottom under a warhammer taller than its owner.
_band = [100 * drawn(i) / HEIGHT for i in weapons]
check("no weapon is squashed onto the minimum length",
      sum(1 for i in weapons if abs(drawn(i) - MIN_LENGTH) < 1e-6) == 0,
      f"shortest is {min(_band):.0f}% of the fighter, floor is "
      f"{100 * MIN_LENGTH / HEIGHT:.0f}%")
check("and none is pinned to the ceiling either",
      sum(1 for i in weapons if abs(drawn(i) - MAX_LENGTH) < 1e-6) == 0,
      f"longest is {max(_band):.0f}%")
check("the smallest weapon is still a third of the fighter",
      min(_band) >= 30, f"{min(_band):.0f}%")
check("and the largest is near a real polearm, not twice one",
      max(_band) <= 135, f"{max(_band):.0f}% (a real spear is about 120% of its owner)")

check("and a weapon is big enough to read at all",
      min(drawn(i) for i in weapons) / HEIGHT > 0.28,
      f"shortest is {100 * min(drawn(i) for i in weapons) / HEIGHT:.0f}% of the fighter")

print("\n" + ("ALL PASS" if ok else "SOME FAILED"))
