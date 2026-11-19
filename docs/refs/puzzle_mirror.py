"""The locks of docs/04, mirrored from Sim/Puzzles/Puzzle.cs.

Three of the twelve mechanics, one per difficulty, and they are all the same thing
underneath: a few RIGHT touches among a handful of options, in order or in any order. That
is not a simplification -- it is what nine of the twelve are. What differs between a rune,
a ring and a gear is what the player looks at, and that is `shape`, which belongs to
whatever draws the lock and which the simulation never reads.

This file is the reference the C# is written against, the same way loot_mirror is for the
loot. If the two ever drift, puzzle_test goes red against the numbers pinned below.

Pinned: Rng(7).fork(1) dealt at three pips gives gears on 1 and 3 with shape 15; the
first six deals from seed 1 are printed by `python3 puzzle_mirror.py`.
"""
from loot_mirror import Rng, CRATE, LOCKER, SAFE

NONE, ODD_ONE_OUT, RINGS, GEARS = 0, 1, 2, 3

KIND_NAME = {NONE: "none", ODD_ONE_OUT: "odd one out", RINGS: "rings", GEARS: "gears"}


class Puzzle:
    """One lock. Kind, how many pips it shows, what is on it and which touches open it."""

    def __init__(s, kind=NONE, pips=0, options=0, needed=0, ordered=True,
                 a=0, b=0, c=0, shape=0):
        s.kind, s.pips, s.options, s.needed = kind, pips, options, needed
        s.ordered = ordered
        s.a, s.b, s.c = a, b, c
        s.shape = shape

    @property
    def exists(s):
        return s.kind != NONE

    def wants(s, n):
        return (s.a, s.b, s.c)[n] if 0 <= n < 3 else 0

    @property
    def finished(s):
        return (1 << s.needed) - 1 if s.needed > 0 else 0

    def solved(s, done):
        return s.exists and done == s.finished

    def press(s, done, choice):
        """What is done after this touch, or -1 when the touch was wrong.

        No attempt counter and no lock-out: docs/04 rule 4 is that a mistake costs time and
        noise, never a closed door. What it costs is the motor's business, not this."""
        if not s.exists or choice < 1 or choice > s.options:
            return -1
        if s.ordered:
            nxt = bin(done).count("1")
            if nxt >= s.needed:
                return -1
            return done | (1 << nxt) if choice == s.wants(nxt) else -1
        for i in range(s.needed):
            if done & (1 << i):
                continue
            if s.wants(i) == choice:
                return done | (1 << i)
        return -1

    def answers(s):
        return [s.wants(i) for i in range(s.needed)]

    def key(s):
        return (s.kind, s.shape, s.a, s.b)

    def __repr__(s):
        return (f"{KIND_NAME[s.kind]} {s.pips}p "
                f"{s.needed}/{s.options} {'in order' if s.ordered else 'any order'} "
                f"answers={s.answers()} shape={s.shape}")


NO_PUZZLE = Puzzle()


def pips_for(kind):
    """The pips go with the tier: one on the quick boxes, three on the safe."""
    return 1 if kind == CRATE else (2 if kind == LOCKER else 3)


def odd_one_out(r):
    """docs/04 #1. Four runes, one of them turned. Tap it."""
    odd = r.below(4) + 1
    family = r.below(6)
    return Puzzle(ODD_ONE_OUT, 1, 4, 1, True, a=odd, shape=family)


def rings(r):
    """docs/04 #5. Three rings with a slot in each; line the slots up.

    Exactly two start one step off, so it is always two moves and never three, and either
    may be turned first because turning one does not move the others. The six options are
    the three rings times the two directions. Shape packs where they start, base three."""
    spare = r.below(3)
    shape, found, a, b, power = 0, 0, 0, 0, 1
    for ring in range(3):
        off = 1 if ring == spare else (0 if r.below(2) == 0 else 2)
        shape += off * power
        power *= 3
        if off == 1:
            continue
        option = ring * 2 + (1 if off == 0 else 2)
        if found == 0:
            a = option
        else:
            b = option
        found += 1
    return Puzzle(RINGS, 2, 6, 2, False, a=a, b=b, shape=shape)


def gears(r):
    """docs/04 #10. Four gears; two have to come off for the key to turn the arrow's way.

    The only lock in the slice that needs a step of abstraction -- you are counting, not
    matching -- which is why docs/04 puts it on three pips and nothing else. The two that
    must go are dealt and the chain is dressed to agree: the generator guarantees a
    solution rather than the player hoping for one."""
    a = r.below(4) + 1
    b = r.below(3) + 1
    if b >= a:
        b += 1
    engaged = r.below(16) | (1 << (a - 1)) | (1 << (b - 1))
    return Puzzle(GEARS, 3, 4, 2, False, a=a, b=b, shape=engaged)


def deal_one(r, pips):
    if pips <= 1:
        return odd_one_out(r)
    if pips == 2:
        return rings(r)
    return gears(r)


def deal(chests, seed):
    """Every chest that offers a lock gets one, and no two get the same.

    docs/04 says a puzzle does not repeat inside a match, and the point of the rule is that
    the second safe must not be the first safe with the answer already known. Eight tries
    and then take what comes: a bank of three mechanics CAN run out of distinct locks on a
    big arena, and a round that never starts is worse than a lock seen twice."""
    rnd = Rng(seed)
    for i, chest in enumerate(chests):
        if not getattr(chest, "has_puzzle", False):
            chest.puzzle = NO_PUZZLE
            continue
        own = rnd.fork(i + 1)
        pips = pips_for(chest.kind)
        p = deal_one(own, pips)
        tries = 0
        while tries < 8 and any(c.puzzle.key() == p.key()
                                for c in chests[:i] if c.puzzle.exists):
            p = deal_one(own, pips)
            tries += 1
        chest.puzzle = p


if __name__ == "__main__":
    r = Rng(1)
    print("  seed 1, one stream per chest:")
    for i in range(6):
        own = r.fork(i + 1)
        for pips in (1, 2, 3):
            print(f"    chest {i} at {pips} pip(s): {deal_one(own, pips)}")
