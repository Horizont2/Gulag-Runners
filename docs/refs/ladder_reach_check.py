#!/usr/bin/env python3
"""
Every storey of an arena has to be reachable from the one below it.

This is the other half of ladder_ceiling_test. That one proves the MOTOR refuses to grab a
ladder through a ceiling; this one proves the LEVELS still give you a way up once it does.
The two faults look identical from inside the game -- "I cannot get upstairs" and "I can get
upstairs through the stone" are the same arena -- and fixing either one alone leaves the
other.

What it found the day it was written, which is why it exists:

    Arena_Egypt   storey 3.27 unreachable from 1.12   (5 ladder markers, all 0.62 m stubs)
    Arena_Mine    storey 3.27 unreachable from 1.12   (2 ladder markers, both stubs)
    Arena_Forest  every storey reachable              (ladders 1.79 m and 3.00 m)

Egypt's and Mine's ladder markers only straddle the lip of the floor they land on, and the
slab they pass through runs the full width of the arena. So a body on the storey below tops
out with its head against that slab, seven centimetres inside the ladder above it. While a
graze was enough to grab, that seven centimetres WAS the staircase -- the arenas were
climbable only through the bug. Forest is the control: LadderSetup built its ladders from
the models, foot on the floor you leave and head above the floor you reach, and it passes.

The repair is Tools > Gulag Runners > Setup > "Make the ladders reach the floor below",
which grows each marker down onto the floor underneath it. Until that has been run and the
scene saved, the two arenas below fail here, and that is the check doing its job.

    python3 ladder_reach_check.py
"""

import glob
import os
import sys

from sim_motor_mirror import C, ONE, S, step, UP, span, lane_on
import arena_demo_geometry as G

ok = True

# A body standing on the floor has to overlap the ladder by this much to take hold of it.
# PlayerMotor.FindLadderInReach, mirrored in sim_motor_mirror -- read from the config rather
# than written down again, so the day the motor changes its mind this check follows.
BITE = C["H"] / 4
BODY = C["H"]

# Narrower than this is a prop, not a storey. The same line LadderReach.cs draws.
STOREY = 1.0 * ONE


def check(name, passed, detail=""):
    global ok
    ok &= bool(passed)
    print("  %s  %s%s" % ("PASS" if passed else "FAIL", name, ("  " + detail) if detail else ""))


def storeys(world):
    """The tops a body can stand on, low to high: wide solids, deduplicated."""
    tops = set()
    for b in world.get("solid", []) + world.get("oneway", []):
        if b[2] - b[0] < STOREY:
            continue
        tops.add(b[3])
    return sorted(tops)


def spans(ladder, floor_top):
    """Does a body standing at floor_top take hold of this ladder?"""
    low = max(floor_top, ladder[1])
    high = min(floor_top + BODY, ladder[3])
    return high - low >= BITE


def served(world, top):
    """Was a ladder put there to reach this floor?

    The top slab of an arena is a roof, not a storey: Arena_Egypt and Arena_Mine both end
    in one at y 5.39 with no ladder anywhere near it, and demanding a way up onto it was
    this check inventing a requirement the levels never made. A floor counts when some
    ladder was placed to serve it -- running up from below it and ending at or above it,
    within the free step the motor allows at the top, which is how Arena_Forest's ladders
    stop just under their walkway and still work.
    """
    for L in world.get("ladder", []):
        if L[1] < top and L[3] > top - C["STEP"]:
            return True
    return False


def reachable(world, below, above, home=0.0):
    """Is there a ladder that carries a body from storey `below` up past storey `above`?

    This CLIMBS it rather than reasoning about it. The first version of this check asked
    three questions on paper -- can the body grab it, does the ladder come out above the
    upper floor, is there floor to stand on underneath -- and the middle one failed
    Arena_Forest, which plays fine: its ladders stop fifteen centimetres BELOW the walkway
    and the motor mantles out of the top on its own. Every tolerance I could have written
    there would have been a number I made up to make a known-good arena pass.

    So the body is stood at the foot of each ladder holding up, and the only question asked
    is the one the player asks: did it end up on the floor above.

    `home` is the depth the fighters play at, and it has to be this arena's own. It was
    nought, which is near enough in the four arenas whose floors are a couple of metres
    deep, and nowhere near it in Arena_Sawmill: its floors are eight metres deep and step
    FORWARD as they rise, so the men walk at z -5.63, -4.61 and -3.57. Standing the test
    body at z 0 put it four metres in front of the ladders on a lane nobody uses, which is
    both a pass this check cannot claim and a failure it cannot explain.
    """
    for L in world.get("ladder", []):
        if not spans(L, below):
            continue

        # Somewhere to stand under it. A ladder the lower storey does not reach beneath is
        # no route off that storey, whatever its length.
        #
        # The floor is found by INDEX because the body also has to be put on its slice of
        # depth. Arena_Forest is why: the bottom of its pit sits well behind the fight
        # plane, and a body dropped in at the default slice falls straight through a floor
        # that is plainly underneath it. The lane is taken from the mirror's own lane_on,
        # so this is the slice the game would move the body onto and not one chosen here.
        lane = None
        for i, f in enumerate(world.get("solid", [])):
            if f[3] != below or f[2] - f[0] < STOREY:
                continue
            if f[2] > L[0] and f[0] < L[2]:
                lane = lane_on(span(world, "solid", i), home)
                break
        if lane is None:
            continue

        x = (L[0] + L[2]) / 2 / ONE
        s = S(x, below / ONE)
        s.depth = lane
        for _ in range(400):
            step(s, UP, world)
            if s.y >= above:
                return True
    return False


def floors_of(world):
    """Consecutive pairs of storeys a fighter is meant to move between."""
    tops = storeys(world)
    # Storeys closer together than a body are the same floor seen twice -- a lip, a step,
    # a slab modelled in two pieces at slightly different heights.
    kept = []
    for t in tops:
        if not kept or t - kept[-1] > BODY:
            kept.append(t)
    return kept


def arena(path):
    name = os.path.basename(path)[:-6]
    try:
        world = G.bake(G.Scene(path))
    except (Exception, SystemExit) as exc:
        # SystemExit is not an oversight. arena_demo_geometry QUITS on a scene with no
        # SimWorldBaker in it -- it is a command-line tool first -- and catching only
        # Exception let that kill the whole run at the menu scene: every later arena went
        # unchecked and the fault injection never ran, while the exit code still read 1 so
        # nothing looked wrong.
        return                          # not an arena: the menu, a test bed

    if not world.get("ladder"):
        return

    # Where this arena's fighters stand, from its own spawn points. The motor clamps that
    # depth into each floor's slice as the body moves between storeys, which is what
    # lane_on does below.
    spawns = G.spawns(G.Scene(path))
    home = spawns[0][3] if spawns else 0.0
    C["HOME"] = int(home * ONE)

    kept = floors_of(world)
    if len(kept) < 2:
        return

    for i in range(len(kept) - 1):
        below, above = kept[i], kept[i + 1]
        if not served(world, above):
            continue
        got = reachable(world, below, above, home)
        check("%s: the storey at y %.2f can be reached from y %.2f"
              % (name, above / ONE, below / ONE),
              got,
              "" if got else
              "no ladder a body on y %.2f can take hold of carries it above y %.2f"
              % (below / ONE, above / ONE))


def fault_injection():
    """A check that cannot fail is not a check.

    Forest passes, so Forest is the one to break: cut every ladder down to the stub that
    Egypt and Mine actually have and the same code must report it unreachable.
    """
    global ok
    path = os.path.join(HERE, "..", "..", "Gulag runners", "Assets", "Scenes",
                        "Arena_Forest.unity")
    if not os.path.exists(path):
        print("  (no Arena_Forest to injure, so the fault injection was skipped)")
        return

    world = G.bake(G.Scene(path))
    kept = floors_of(world)
    if len(kept) < 2:
        print("  (Arena_Forest has one storey, so the fault injection was skipped)")
        return

    sound = any(reachable(world, kept[i], kept[i + 1]) for i in range(len(kept) - 1))

    # Every ladder cut to a 0.62 m stub sitting at the top of its own span, which is the
    # exact shape of the real fault.
    world["ladder"] = [(L[0], L[3] - int(0.62 * ONE), L[2], L[3])
                       for L in world.get("ladder", [])]
    broken = any(reachable(world, kept[i], kept[i + 1]) for i in range(len(kept) - 1))

    held = sound and not broken
    ok &= held
    print("  %s  the check notices when a good arena is given stub ladders  "
          "(whole %s, stubbed %s)"
          % ("PASS" if held else "FAIL",
             "reachable" if sound else "unreachable",
             "reachable" if broken else "unreachable"))


HERE = os.path.dirname(os.path.abspath(__file__))


def main():
    print("ladder_reach_check: every storey is reachable from the one below it")

    scenes = sorted(glob.glob(os.path.join(
        HERE, "..", "..", "Gulag runners", "Assets", "Scenes", "*.unity")))
    for path in scenes:
        arena(path)

    fault_injection()
    print("ladder_reach_check: a body is %.2f m tall and has to overlap a ladder by %.2f m"
          % (BODY / ONE, BITE / ONE))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
