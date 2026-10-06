"""Checks the chests of docs/03 against the real motor on the real arena.

The chest boxes here are the ones placed in SetupScene, and the walking between them is the
actual movement simulation, so the times below are the times a player will really see."""
import sim_motor_mirror as M
import loot_mirror as T
from sim_motor_mirror import S, step, X, F, L, R, UP, DN, JMP, ACTION
from arena_geometry import floor_h, ladder_x, world
from loot_mirror import (Chest, Inv, loot_step, frames_to_open, fresh,
                         CRATE, LOCKER, SAFE, NONE, FISTS, CLUB, SPEAR, CHAINMAIL, BANDAGE,
                         ITEMS, CHEST_CFG)

W = world()

# The six chests of SetupScene: (name, kind, contents, centre x, floor y, width, height)
LAYOUT = [
    ("Chest_F0_Crate_L",  CRATE,  CLUB,      -5.5, 0.0, 0.7, 0.5),
    ("Chest_F0_Locker_L", LOCKER, CHAINMAIL, -2.5, 0.0, 0.7, 1.4),
    ("Chest_F1_Safe_L",   SAFE,   SPEAR,     -2.0, 3.0, 0.9, 0.9),
    ("Chest_F1_Crate_R",  CRATE,  BANDAGE,    4.5, 3.0, 0.7, 0.5),
    ("Chest_F2_Safe_L",   SAFE,   SPEAR,     -4.0, 6.0, 0.9, 0.9),
    ("Chest_F2_Crate_R",  CRATE,  CLUB,       3.0, 6.0, 0.7, 0.5),
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


def tick(s, inp, chests=None, player=0):
    step(s, inp, W)
    loot_step(s, player, inp, chests or [])


def walk_to(s, x, chests, player=0, limit=900):
    """Walks with the real motor. Returns the ticks it took."""
    for t in range(limit):
        inp = R if F(s.x) < x - 0.05 else (L if F(s.x) > x + 0.05 else 0)
        tick(s, inp, chests, player)
        if abs(F(s.x) - x) <= 0.05:
            return t + 1
    return limit


def hold_open(s, chests, player=0, limit=900):
    """Holds ACTION until something comes out. Returns (ticks, item)."""
    for t in range(limit):
        tick(s, ACTION, chests, player)
        if s.picked != NONE:
            return t + 1, s.picked
    return limit, NONE


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
check("the crate opens", chests[0].opened and item == CLUB,
      f"{took/60:.2f} s bare-handed -> {ITEMS[item]['name']}")
check("and the club is in hand", s.inv.weapon == CLUB and s.inv.durability == 14,
      f"durability {s.inv.durability}")

print("\n3. the weapon is also the crowbar (docs/03)")
s2 = spawn(-2.5, 0.9, CLUB)
took, item = hold_open(s2, chests, 0)
check("the club opens the locker in half the time", abs(took / 60 - 2.5) < 0.1,
      f"{took/60:.2f} s")
check("and it cost two of its fourteen uses", s2.inv.durability == 12,
      f"durability {s2.inv.durability}")
check("the chainmail is on", s2.inv.armour == CHAINMAIL)

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
# docs/03: "22 seconds is about 2 crates + 1 locker + 1 safe with running in between".
# This is that exact shopping list, on this arena, with the real motor doing the running.
chests = build()
s = spawn(-8.3, 0.9)
budget, spent, got, legs = 22 * 60, 0, [], []

def leg(label, ticks):
    legs.append((label, ticks))
    return ticks

spent += leg("walk to the crate", walk_to(s, -5.5, chests))
t, item = hold_open(s, chests); spent += leg("crate, bare-handed", t); got.append(ITEMS[item]['name'])
spent += leg("walk to the locker", walk_to(s, -2.5, chests))
t, item = hold_open(s, chests); spent += leg("locker, with the club", t); got.append(ITEMS[item]['name'])
spent += leg("back to the ladder", walk_to(s, ladder_x(0), chests))

climb = 0
for _ in range(600):
    tick(s, UP, chests, 0)
    climb += 1
    if s.mode == "ground" and F(s.y) >= floor_h - 0.05:
        break
spent += leg("climb a floor", climb)

spent += leg("walk to the safe", walk_to(s, -2.0, chests))
t, item = hold_open(s, chests); spent += leg("safe, forced", t); got.append(ITEMS[item]['name'])
spent += leg("walk to the far crate", walk_to(s, 4.5, chests))
t, item = hold_open(s, chests); spent += leg("crate", t); got.append(ITEMS[item]['name'])

for label, ticks in legs:
    print(f"        {ticks/60:5.2f} s  {label}")

opened = sum(1 for c in chests if c.opened)
check("two crates, a locker and a safe, with the running, fit the phase",
      spent <= budget and opened == 4,
      f"{spent/60:.1f} s of {budget/60:.0f}, {opened} chests, carrying {', '.join(got)}")
check("and the phase is not over before it starts", spent >= budget * 0.6,
      f"{spent/budget*100:.0f}% of the phase spent")
check("you cannot have everything: two chests are still shut",
      opened < len(chests), f"{len(chests)-opened} left")

print("\n9. the same inputs give the same frames")
r = []
for _ in range(2):
    c = build()
    p = spawn(-8.3, 0.9)
    walk_to(p, -5.5, c)
    hold_open(p, c)
    r.append((p.x, p.y, p.inv.weapon, p.inv.durability, c[0].progress))
check("bit-identical replay", r[0] == r[1], f"{r[0]}")

print("\n" + ("ALL PASS" if ok else "SOME FAILED"))
