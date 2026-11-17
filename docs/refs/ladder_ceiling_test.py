#!/usr/bin/env python3
"""
A ladder on the far side of a ceiling must not be climbable from under it.

Reported from play: "the player can jump onto a ladder through the ceiling". The cause was
one line. While climbing, PlayerMotor.MoveY waves past every solid the ladder is said to run
THROUGH -- that is what makes a hatch a hatch -- and the test for "runs through" was only
that the solid's underside sat inside the ladder's span:

    solid.MinY > ladder.MinY && solid.MinY < ladder.MaxY

So a ladder poking two centimetres into the bottom of a slab exempted the WHOLE slab,
however thick. Jump under such a ceiling, let the top of the body brush the ladder above it,
press up, and the grab takes -- then everything in between is the hole.

The first attempt at a fix was wrong and this test is what proved it. It tightened the
"runs through" test so a ladder had to reach the far side of the slab -- and the slab in
the real case is reached, because the ladder genuinely does run from below it to well
above it. It is a ladder for the floor UPSTAIRS, modelled two centimetres long at the
bottom. Geometrically the hole is real; what is not real is the grip.

So the fix is at the GRAB. Overlapping the ladder's box by a hair used to be enough to
mount it. It now takes a quarter of a body -- the same bite the motor already uses to
decide whether you are at the top of a ladder -- which a fighter pinned under a ceiling
by their own head cannot reach, and which any ordinary approach to a ladder exceeds
several times over.

Two worlds, one rule. Both are built here rather than taken from an arena, because the point
is the rule and not any particular level -- and because a level can be fixed by moving a box,
which would make this test pass again while the motor stayed wrong.

    python3 ladder_ceiling_test.py
"""

import sys

import sim_motor_mirror as M
from sim_motor_mirror import S, step, C, UP, X, ONE

ok = True


def check(name, passed, detail=""):
    global ok
    ok &= bool(passed)
    print("  %s  %s%s" % ("PASS" if passed else "FAIL", name, ("  " + detail) if detail else ""))


def box(x0, y0, x1, y1):
    """A world box, in metres. X() is the mirror's own conversion -- a second copy of the
    fixed-point rule here is exactly the kind of drift mirror_check exists to catch."""
    return (X(x0), X(y0), X(x1), X(y1))


def climb(world, start_y, frames=240):
    """Stand a body at x=0, hold UP, and report the highest it gets."""
    s = S(0.0, start_y)          # S takes METRES and converts; box() above takes metres too
    best = s.y
    modes = set()
    for _ in range(frames):
        step(s, UP, world)
        modes.add(s.mode)
        best = max(best, s.y)
    return best / float(ONE), modes


# ---------------------------------------------------------------- the hatch: must still work
#
# A floor 0.25 m thick at y 3.0, and a ladder that runs from the ground right up to the
# landing on top of it. This is every working ladder in the game and the fix must not touch
# it.
HATCH = {
    "solid": [box(-8, -0.5, 8, 0.0),        # ground
              box(-8, 3.0, -0.4, 3.25),     # the floor above, with a gap for the ladder
              box(0.4, 3.0, 8, 3.25)],
    "oneway": [],
    "ladder": [box(-0.25, 0.0, 0.25, 3.25)],
}

# ------------------------------------------------------------- the ceiling: must NOT work
#
# The ceiling has to be somewhere a jump can actually reach, or the test proves nothing --
# which is how the first version of this file passed against the unfixed motor. A jump tops
# out at 9.5^2 / (2 * 28) = 1.61 m, so a body on the ground gets its head to 2.52 m. The
# slab therefore sits at 2.20, and the ladder for the floor above it starts at 2.18: two
# centimetres proud, which is ordinary modelling slop.
#
# Pinned under the slab the body spans 1.29 to 2.20 and overlaps the ladder by those two
# centimetres. That was a grab.
CEILING = {
    "solid": [box(-8, -0.5, 8, 0.0),
              box(-8, 2.2, 8, 3.0)],        # one slab, no hole anywhere
    "oneway": [],
    "ladder": [box(-0.25, 2.18, 0.25, 6.0)],
}


def main():
    print("ladder_ceiling_test: a ladder is a hole only where it goes all the way through")

    top, modes = climb(HATCH, 0.0)
    check("a ladder through a hatch still carries a body to the landing",
          top >= 3.2, "reached y %.2f, wanted 3.25" % top)
    check("and the body really climbed rather than jumped",
          "ladder" in modes, "modes seen: %s" % sorted(modes))

    # Holding UP from the ground is a jump and then the grab attempt, which is exactly how
    # it was reported.
    top, modes = climb(CEILING, 0.0)
    check("a ladder behind a solid ceiling does NOT let a body through it",
          top < 2.2, "reached y %.2f, the ceiling starts at 2.20" % top)
    check("and the body never gets onto its rungs at all",
          "ladder" not in modes, "modes seen: %s" % sorted(modes))

    # Started already pinned against the underside of the slab, overlapping the ladder by
    # the two centimetres that used to be a grip.
    top, modes = climb(CEILING, 1.29)
    check("nor when the body starts pressed against the ceiling, touching the rungs",
          top < 2.25, "reached y %.2f" % top)

    print("ladder_ceiling_test: free step allowance %.2f m"
          % (C["STEP"] / float(ONE)))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
