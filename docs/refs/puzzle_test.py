"""Do the locks of docs/04 keep that note's promises, on the real motor?

docs/03 calls the puzzles the point of difference of the whole game, and docs/04 spends
126 lines saying what they may and may not be. Most of those lines are checkable, and this
checks them against the actual loot motor on the actual arena, not against a description
of it:

  * FORCE IS ALWAYS AVAILABLE (rule 3). A locked chest still opens by holding the button,
    in exactly the time it took before the lock existed. A puzzle that gates loot is a
    barrier for everyone who does not enjoy puzzles, and the note is blunt about it.
  * AT MOST THREE TOUCHES (rule 1), on every lock the generator can deal, at every
    difficulty.
  * AND FEWER SECONDS THAN FORCE. The whole argument for learning the locks is that
    picking is faster and quieter; if it were not, nobody would.
  * A MISTAKE COSTS TIME, NOT A DOOR (rule 4). One second of the lock taking nothing, a
    loud noise, and then it takes the right answer. No attempt counter anywhere.
  * PROGRESS SURVIVES (rule 5). Walk away, get hit, come back: the touches already made
    are still made, because losing them is frustration rather than difficulty.
  * AND IT IS NOT YOURS. Another player can finish a lock you started, which docs/04 calls
    the sweetest moment in the game.
  * BOTH PLAYERS GET THE SAME SPREAD (rule 7): the same seed deals the same locks, so
    neither side of the arena is harder than the other.
  * NO LOCK TWICE IN ONE ROUND, or the second safe is the first safe with the answer
    already known.
  * EVERY DEALT LOCK OPENS. The generator guarantees a solution; the player does not hope
    for one.

    python3 puzzle_test.py
"""
import loot_mirror as T
import puzzle_mirror as P
from loot_mirror import (Chest, Inv, loot_step, fresh, pool, step_ground,
                         CRATE, LOCKER, SAFE, NONE, CLUB, SPEAR, CHEST_CFG,
                         BARE_GAIN, SILENT, QUIET, LOUD, PUZZLE_WRONG_FRAMES)
from sim_motor_mirror import S, step, X, F, L, R, ACTION
from arena_geometry import world

W = world()
GROUND = pool(6)

ok = True


def check(n, c, d=""):
    global ok
    ok &= bool(c)
    print(f"  {'PASS' if c else 'FAIL'}  {n}{('  ' + d) if d else ''}")


def touch(option):
    """ACTION plus a touch on the lock. The option rides in bits 8..10 of the flags, which
    already travel as an int on the wire, so a touch costs no bytes at all."""
    return ACTION | ((option & 7) << 8)


def one_chest(kind=SAFE, contents=SPEAR, x=0.0, y=0.0, w=0.8, h=0.7, puzzle=True):
    return Chest(kind, contents, (X(x - w / 2), X(y), X(x + w / 2), X(y + h)),
                 has_puzzle=puzzle)


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


def tick(s, inp, chests=None, player=0, shared=True):
    if shared:
        step_ground(GROUND, W)
    step(s, inp, W)
    loot_step(s, player, inp, chests or [], GROUND)


def at_chest(chest, weapon=NONE):
    """A fighter standing on the chest, ready to work on it."""
    cx = F((chest.box[0] + chest.box[2]) // 2)
    s = spawn(cx, F(chest.box[1]) + 0.1, weapon)
    for _ in range(60):
        tick(s, 0, [chest])
        if s.mode == "ground":
            break
    return s


# ---------------------------------------------------------------- the generator

print("\n  WHAT THE GENERATOR DEALS\n")

seen = {}
for pips in (1, 2, 3):
    worst, kinds = 0, set()
    for seed in range(1, 400):
        p = P.deal_one(T.Rng(seed), pips)
        worst = max(worst, p.needed)
        kinds.add(p.kind)

        # every lock it deals must open on its own answers, in SOME order it allows
        done = 0
        for a in p.answers():
            done = p.press(done, a)
            if done < 0:
                break
        if done < 0 or not p.solved(done):
            seen[pips] = f"seed {seed} dealt a lock its own answers do not open"
            break

        # and the answers have to be real options on the lock
        if any(a < 1 or a > p.options for a in p.answers()):
            seen[pips] = f"seed {seed} dealt an answer that is not on the lock"
            break
    check(f"{pips} pip(s): every lock of 400 opens on its own answers",
          pips not in seen, seen.get(pips, f"{len(kinds)} mechanic(s), "
                                           f"at most {worst} touch(es)"))
    check(f"{pips} pip(s): never more than three touches (rule 1)", worst <= 3,
          f"worst was {worst}")

# unordered locks must open in EITHER order, or "any order" is a lie
both = True
for seed in range(1, 400):
    p = P.deal_one(T.Rng(seed), 2)
    a, b = p.answers()
    if p.press(p.press(0, b), a) != p.finished:
        both = False
        break
check("the rings open in either order, which is what 'any order' means (#5)", both)

# the three pips must be three different mechanics, or the difficulty is a label
mech = {pips: P.deal_one(T.Rng(11), pips).kind for pips in (1, 2, 3)}
check("one mechanic per difficulty, not one mechanic relabelled",
      len(set(mech.values())) == 3,
      ", ".join(f"{k} pip = {P.KIND_NAME[v]}" for k, v in mech.items()))

# ---------------------------------------------------------------- dealing a round

print("\n  DEALING A ROUND\n")

LAYOUT = [(CRATE, -5.5), (LOCKER, -2.5), (SAFE, -2.0), (CRATE, 4.5), (SAFE, -4.0),
          (LOCKER, 3.0)]


def round_of(seed, puzzles=True):
    chests = [one_chest(kind, CLUB, x, 0.0, puzzle=puzzles) for kind, x in LAYOUT]
    P.deal(chests, seed)
    return chests


a = round_of(90210)
b = round_of(90210)
check("the same seed deals the same locks to both players (rule 7)",
      all(x.puzzle.key() == y.puzzle.key() for x, y in zip(a, b)))

# One round proves nothing here: two crates collide maybe once in twenty. Deal a few
# hundred rounds, because the rule has to hold on the round the player is unlucky in.
repeats = 0
for seed in range(1, 400):
    keys = [c.puzzle.key() for c in round_of(seed)]
    if len(set(keys)) != len(keys):
        repeats += 1
check("and no lock is dealt twice in one round, over 400 rounds", repeats == 0,
      f"{repeats} round(s) dealt a repeat")

pips = sorted(c.puzzle.pips for c in a)
check("the pips follow the chest, so the spread is the same on both sides",
      pips == sorted(P.pips_for(k) for k, _ in LAYOUT), f"{pips}")

bare = round_of(90210, puzzles=False)
check("a chest that was not given a lock does not get one",
      all(not c.puzzle.exists for c in bare))

# ---------------------------------------------------------------- force, still

print("\n  FORCE IS STILL THERE (rule 3)\n")

forced = one_chest()
P.deal([forced], 4242)
s = at_chest(forced)
ticks = 0
for t in range(900):
    tick(s, ACTION, [forced])
    ticks = t + 1
    if s.opened_chest >= 0:
        break
want = CHEST_CFG[SAFE]["bare"] * BARE_GAIN // 1000
check("a locked safe still opens by holding the button alone",
      forced.opened, f"{ticks} ticks")
check("in the time it took before locks existed", abs(ticks - want) <= 2,
      f"{ticks} against {want}")
check("and forcing it is loud", s.noise == LOUD, f"noise {s.noise}")
forced_ticks, forced_noise = ticks, s.noise

# ---------------------------------------------------------------- picking it

print("\n  PICKING IT (rules 1 and 4)\n")

for pips, kind in ((1, CRATE), (2, LOCKER), (3, SAFE)):
    chest = one_chest(kind)
    P.deal([chest], 4242)
    s = at_chest(chest)
    used = 0
    for a_ in chest.puzzle.answers():
        tick(s, touch(a_), [chest])
        used += 1
        if s.opened_chest >= 0:
            break
    check(f"{pips} pip(s): picked in {used} touch(es)", chest.opened and used <= 3,
          f"{P.KIND_NAME[chest.puzzle.kind]}, {used} touch(es) against "
          f"{forced_ticks} ticks of forcing")
    check(f"{pips} pip(s): and it was quiet", s.noise == QUIET,
          f"noise {s.noise}, forcing is {forced_noise}")

# a right touch that does not finish the lock makes no sound at all
chest = one_chest(SAFE)
P.deal([chest], 4242)
s = at_chest(chest)
tick(s, touch(chest.puzzle.answers()[0]), [chest])
check("a right touch that does not open it is silent", s.noise == SILENT,
      f"noise {s.noise}")

# ---------------------------------------------------------------- getting it wrong

print("\n  GETTING IT WRONG COSTS TIME, NOT A DOOR (rule 4)\n")

chest = one_chest(SAFE)
P.deal([chest], 4242)
s = at_chest(chest)
right = chest.puzzle.answers()
wrong = next(o for o in range(1, chest.puzzle.options + 1) if o not in right)

tick(s, touch(wrong), [chest])
check("a wrong touch is loud, which is what gives you away", s.noise == LOUD,
      f"noise {s.noise}")
check("and the lock sulks for a second", chest.pstun == PUZZLE_WRONG_FRAMES,
      f"{chest.pstun} frames = {chest.pstun / 60:.2f} s")

# it takes nothing at all while it sulks, not even the right answer
tick(s, touch(right[0]), [chest])
check("it takes nothing while it sulks, right answers included", chest.pdone == 0)
check("and forcing it is on hold too, so the mistake really costs a second",
      chest.progress == 0, f"progress {chest.progress}")

for _ in range(PUZZLE_WRONG_FRAMES):
    tick(s, ACTION, [chest])
check("after the second it is a lock again", chest.pstun == 0)

for a_ in right:
    tick(s, touch(a_), [chest])
check("and it opens, because there is no attempt counter anywhere", chest.opened)

# wrong, wrong, wrong -- and it still opens. No lock-out at any count.
chest = one_chest(SAFE)
P.deal([chest], 4242)
s = at_chest(chest)
for _ in range(5):
    tick(s, touch(wrong), [chest])
    for _ in range(PUZZLE_WRONG_FRAMES):
        tick(s, ACTION, [chest])
for a_ in chest.puzzle.answers():
    tick(s, touch(a_), [chest])
check("five wrong answers in a row lock nothing out", chest.opened)

# ---------------------------------------------------------------- the progress stays

print("\n  THE PROGRESS STAYS (rule 5)\n")

chest = one_chest(SAFE, x=-2.0)
P.deal([chest], 4242)
s = at_chest(chest)
first, second = chest.puzzle.answers()
tick(s, touch(first), [chest])
half = chest.pdone

# walk away far enough to drop the claim, then come back
for _ in range(90):
    tick(s, L, [chest])
check("walking away does not undo a touch already made", chest.pdone == half,
      f"done {bin(half)}")
check("though the forcing progress does drain, as it always did",
      chest.progress == 0)

for _ in range(200):
    tick(s, R, [chest])
    if abs(F(s.x) - F((chest.box[0] + chest.box[2]) // 2)) < 0.1:
        break
tick(s, touch(second), [chest])
check("and the last touch finishes it", chest.opened)

# somebody else's lock
chest = one_chest(SAFE)
P.deal([chest], 4242)
one = at_chest(chest)
two = at_chest(chest)
first, second = chest.puzzle.answers()
tick(one, touch(first), [chest], player=0)
tick(two, touch(second), [chest], player=1, shared=False)
check("another player can finish the lock you started", chest.opened,
      "docs/04 calls taking somebody else's work the sweetest moment in the game")
check("and the loot is theirs", two.opened_chest >= 0 and one.opened_chest < 0)

# ---------------------------------------------------------------- the touch on the wire

print("\n  THE TOUCH IS AN INPUT LIKE ANY OTHER\n")

check("a touch rides in the flags, so it costs no bytes at 60 Hz",
      touch(6) >> 8 == 6 and (touch(6) & ACTION) == ACTION)
check("and a touch with no button on it does nothing",
      P.deal_one(T.Rng(1), 1).press(0, 0) == -1)
check("an option that is not on the lock is a wrong answer, not a wrapped one",
      P.deal_one(T.Rng(1), 1).press(0, 7) == -1)

print("\n" + ("ALL PASS" if ok else "SOME FAILED"))
