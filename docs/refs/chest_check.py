"""Is the loot laid out fairly, measured from both spawns?

docs/03 makes the chests the whole economy of the scavenge phase: a chest is a trade of
information for gear, and "run four seconds to the gold one or open the white one under
your nose" is the decision the first twenty seconds of a match consists of. Which means
the arrangement is only a decision if BOTH runners are offered the same one. If one of
them reaches every rare chest four seconds sooner, there is no decision, there is a
winner.

And it cannot be eyeballed, for a reason specific to this game: what matters is not
distance, it is TIME, and time here runs through ladders. A chest eight metres away on
your own floor is nearer than a chest two metres away one floor up if the only way up is
at the other end of the building. The arrangement looks symmetric on the screen and is
not.

So this runs the real motor from each spawn to each chest, by the real route, and compares
what the two players are offered: every chest's travel time from each side, and then each
player's whole profile -- their chests sorted by how soon they can be had, with the tier
of what is in them. Fair means those two profiles match. Not that each chest is equidistant
(it never is, and does not need to be) but that whatever one runner can reach by a given
second, the other can reach something of the same tier by the same second.

    python3 chest_check.py
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import arena_demo_geometry as geometry                        # noqa: E402
from sim_motor_mirror import S, step, X, F, C, L, R, UP        # noqa: E402

SCENES = os.path.join(HERE, "..", "..", "Gulag runners", "Assets", "Scenes")

# Close enough to a chest to open it. docs/03's reach box is about this, and the exact
# number changes nothing here: it is the same for both players.
ARRIVED = 0.45

# A floor narrower than this is a crate, not a storey.
MIN_WIDTH = 2.0

# How far apart the two players' profiles may be before the arrangement is biased. A
# second is about a third of the cage phase and a tenth of the scavenge, and it is also
# roughly what a bad reaction to the round starting costs -- below it the geometry is not
# what decided the match.
FAIR_SECONDS = 1.0

# Bias that is known, measured, and deliberately kept.
#
# The point of writing a number down rather than widening the tolerance: a wider
# tolerance blinds the check everywhere and says nothing about why. A recorded figure
# keeps the check sharp -- the bias may not GROW, and if it ever shrinks this says the
# entry can be deleted -- and it puts the reason next to the number, so the next person
# to read it knows it was a decision rather than an oversight.
ACCEPTED = {
    "Demo loc2.unity": (
        6.02,
        "the same shape as the Nordic arena and from the same cause: one way up to each "
        "floor, and the ground one at x -9.14 is 5.1 m from Player 1's spawn and 23.1 m "
        "from Player 2's. Both players enter the upper floors through it, so the cost is "
        "identical at every x along them and no chest position can compensate. The ground "
        "pair measures dead level at 2.10 s each way; everything above it is the ladders. "
        "A second way up, mirrored, is the only thing that changes this, and the level is "
        "being kept as its author built it"),
    "Arena_Nordic fortress.unity": (
        4.60,
        "the arena has one way up to each floor, and the ground one at x -6.71 is 1.40 m "
        "from Player 1's spawn and 15.19 m from Player 2's. Both players enter the upper "
        "floors through that same ladder, so the cost is identical at every x along both "
        "of them (measured) and no chest position can compensate -- only a second route "
        "up could, and the layout is being kept as authored. The chests are mirrored "
        "pairs regardless, which costs nothing here and is already right if the ladders "
        "ever change"),
}

TICK = 1.0 / 60.0

# Long enough to cross this arena twice at a run, per leg.
LIMIT = 1400


def world(path):
    scene = geometry.Scene(path)
    baked = geometry.bake(scene)
    spawns = geometry.spawns(scene)
    if spawns:
        C["HOME"] = X(spawns[0][3])
    w = {k: baked[k] for k in ("solid", "oneway", "ladder", "solidz", "onewayz",
                               "ladderz", "ladderlean", "ladderleanz")}
    w["solidgate"] = baked["solidgate"]
    w["gate"] = baked["gate"]
    w["gatesshut"] = 0
    return w, baked, spawns


def storeys(baked, home_z):
    """Floors worth standing on, as (top, x0, x1, lane), highest first."""
    halfz = F(C["BODYZ"]) / 2
    levels = {}
    for box, depth in zip(baked["solid"], baked["solidz"]):
        x0, _y0, x1, y1 = (F(v) for v in box)
        if x1 - x0 < MIN_WIDTH:
            continue
        zmin, zmax = F(depth[0]), F(depth[1])
        inset = min(halfz, (zmax - zmin) / 2)
        lane = max(zmin + inset, min(home_z, zmax - inset))
        at = levels.setdefault(round(y1, 1), dict(top=y1, x0=x0, x1=x1, lane=lane))
        at["x0"] = min(at["x0"], x0)
        at["x1"] = max(at["x1"], x1)
    return sorted(levels.values(), key=lambda f: -f["top"])


def ladders(baked):
    """Each ladder as (centre x, foot y, the floor top its head reaches)."""
    out = []
    for box in baked["ladder"]:
        x0, y0, x1, y1 = (F(v) for v in box)
        centre = (x0 + x1) / 2
        head = support(baked, centre, y1 + 0.2)
        out.append((centre, y0, head[0] if head else y1))
    return out


def support(baked, x, y):
    """The highest solid directly under a point: (top, x0, x1), or None."""
    best = None
    for box in baked["solid"]:
        x0, _y0, x1, y1 = (F(v) for v in box)
        if not (x0 - 0.3 <= x <= x1 + 0.3):
            continue
        if y1 > y + 0.3:
            continue
        if best is None or y1 > best[0]:
            best = (y1, x0, x1)
    return best


def drive(s, w, target_x, within=ARRIVED, limit=LIMIT):
    """Run toward an x. Ticks spent, or None if it never gets there."""
    for n in range(limit):
        if abs(F(s.x) - target_x) <= within:
            return n
        step(s, R if target_x > F(s.x) else L, w)
    return None


def climb(s, w, centre, to_top, limit=LIMIT):
    """Walk onto a ladder and go up it. Ticks spent, or None."""
    spent = drive(s, w, centre, within=0.2)
    if spent is None:
        return None
    for n in range(limit):
        if F(s.y) >= to_top - 0.12 and s.mode == "ground":
            return spent + n
        # Hold the ladder's own x while climbing: a fighter who drifts off it falls.
        at = F(s.x)
        lean = L if at - centre > 0.12 else (R if centre - at > 0.12 else 0)
        step(s, UP | lean, w)
    return None


def routes(rungs):
    """Every order of ladders worth trying: none, each one, each ordered pair.

    Deliberately a search over the motor rather than a graph over the geometry. A graph
    needs rules for what counts as a connection, and this project's floors are joined by
    0.28 m steps as often as by ladders -- rules that miss those would report a chest
    unreachable while a fighter strolls up to it. Driving the motor and seeing where it
    ends up has no such opinion.
    """
    out = [()]
    for a in rungs:
        out.append((a,))
        for b in rungs:
            if b is not a:
                out.append((a, b))
    return out


def run_route(w, spawn, route, cx, land):
    """Drive one route and return its ticks, or None if the fighter does not arrive."""
    s = S(spawn[0], spawn[1])
    s.depth = X(spawn[3])
    s.facing = spawn[2]
    for _ in range(90):
        step(s, 0, w)

    ticks = 0
    for centre, _foot, head_top in route:
        spent = climb(s, w, centre, head_top)
        if spent is None:
            return None
        ticks += spent

    spent = drive(s, w, cx)
    if spent is None:
        return None
    ticks += spent

    # Arrived means standing WITH it, not above or below it. A fighter who walked to the
    # right x on the wrong floor has not reached the chest, and is the single easiest way
    # for this whole measurement to report a comfortable number about nothing.
    if abs(F(s.y) - land) > 1.0:
        return None
    return ticks


def reach(w, spawn, rungs, cx, land):
    """Seconds for one fighter to get to one chest, by the quickest route that works."""
    best = None
    for route in routes(rungs):
        ticks = run_route(w, spawn, route, cx, land)
        if ticks is not None and (best is None or ticks < best):
            best = ticks
    return None if best is None else best * TICK


def main(argv):
    wanted = argv or geometry.arena_scenes()
    problems, notes = [], []
    kinds = {0: "crate", 1: "locker", 2: "safe"}

    for name in wanted:
        path = os.path.join(SCENES, name)
        w, baked, spawns = world(path)
        if len(spawns) < 2:
            notes.append(f"{name}: {len(spawns)} spawn(s), so there are no two sides")
            continue

        floors = storeys(baked, spawns[0][3])
        rungs = ladders(baked)
        chests = baked["chest"]

        notes.append(f"{name}: {len(chests)} chest(s), {len(floors)} full storey(s), "
                     f"{len(rungs)} ladder(s)")
        for centre, foot, head in rungs:
            notes.append(f"  ladder at x {centre:+.2f}: y {foot:+.2f} -> {head:+.2f}")

        if not chests:
            problems.append(
                f"{name}: no chests in it at all. docs/03 is the scavenge phase and the "
                f"scavenge phase is the chests; without them the round is twenty seconds "
                f"of walking and then a fist fight.")
            continue

        profiles = ([], [])
        for box, kind, _puzzle, label in chests:
            x0, y0, x1, y1 = (F(v) for v in box)
            spot = ((x0 + x1) / 2, y0)

            # What it is standing on, before asking anybody to walk to it. A chest with
            # 3.7 m of air under it is not a hard chest to reach, it is scenery, and no
            # amount of measuring routes to it says anything.
            under = support(baked, spot[0], y0)
            if under is None or y0 - under[0] > 0.4:
                problems.append(
                    f"{name}: {label} at x {spot[0]:+.2f} has " +
                    (f"{y0 - under[0]:.2f} m of air under it (the nearest floor below is "
                     f"at y {under[0]:+.2f}, it sits at y {y0:+.2f})" if under
                     else "nothing under it at all") +
                    ". Nobody can stand with it, so it is scenery with a tier colour.")
                continue

            land = under[0]
            times = [reach(w, spawn, rungs, spot[0], land) for spawn in spawns[:2]]
            if any(t is None for t in times):
                problems.append(
                    f"{name}: {label} at x {spot[0]:+.2f}, y {spot[1]:+.2f} cannot be "
                    f"reached from " +
                    " and ".join(f"spawn {i + 1}" for i, t in enumerate(times)
                                 if t is None) +
                    ". A chest nobody can open is scenery.")
                continue

            gap = times[1] - times[0]
            notes.append(f"  {label:26s} {kinds.get(kind, '?'):6s} "
                         f"x {spot[0]:+7.2f} y {spot[1]:+6.2f}  "
                         f"P1 {times[0]:5.2f}s  P2 {times[1]:5.2f}s  "
                         f"{'P1' if gap > 0 else 'P2'} by {abs(gap):4.2f}s")
            for i in (0, 1):
                profiles[i].append((times[i], kind, label))

        if not profiles[0]:
            continue

        # The comparison that matters: each player's own chests in the order they can
        # have them. Equal profiles mean equal offers, whatever the map looks like.
        first = sorted(profiles[0])
        second = sorted(profiles[1])
        notes.append(f"  P1 offered: " +
                     ", ".join(f"{kinds.get(k, '?')} at {t:.2f}s" for t, k, _n in first))
        notes.append(f"  P2 offered: " +
                     ", ".join(f"{kinds.get(k, '?')} at {t:.2f}s" for t, k, _n in second))

        # The two players' nth-soonest chests, compared rank by rank. Where a scene
        # carries a recorded bias the ranks interleave by tier as a CONSEQUENCE of it --
        # P2's whole upper floor slides past P1's ground floor -- so the gap is measured
        # between the same tiers rather than the same ranks, and the tier mismatch is not
        # a second finding about the same one fact.
        allowance, why = ACCEPTED.get(name, (0.0, None))

        worst, where = 0.0, None
        if why is None:
            pairs = list(enumerate(zip(first, second)))
        else:
            pairs = []
            rank = 0
            for tier in sorted({k for _t, k, _n in first}):
                mine = [c for c in first if c[1] == tier]
                theirs = [c for c in second if c[1] == tier]
                for a, b in zip(mine, theirs):
                    pairs.append((rank, (a, b)))
                    rank += 1

        for rank, (a, b) in pairs:
            if a[1] != b[1]:
                problems.append(
                    f"{name}: the {rank + 1}th chest each player can reach is a "
                    f"{kinds.get(a[1], '?')} for P1 and a {kinds.get(b[1], '?')} for P2, "
                    f"so they are not being offered the same thing in the same order.")
                continue
            if abs(a[0] - b[0]) > worst:
                worst, where = abs(a[0] - b[0]), (rank, a, b)

        notes.append(f"  worst matched pair differs by {worst:.2f}s" +
                     (f", against {allowance:.2f}s recorded as accepted" if why else ""))

        if why is not None:
            notes.append(f"  accepted bias: {why}.")
            if worst > allowance + FAIR_SECONDS:
                rank, a, b = where
                problems.append(
                    f"{name}: the bias has GROWN to {worst:.2f}s, from the {allowance:.2f}s "
                    f"recorded as accepted -- their {rank + 1}th "
                    f"{kinds.get(a[1], '?')}s are {a[0]:.2f}s and {b[0]:.2f}s away "
                    f"({a[2]} against {b[2]}). Something moved: a spawn, a ladder, or "
                    f"these chests. The accepted figure covers the layout as authored, "
                    f"not whatever it is now.")
            elif worst < allowance - FAIR_SECONDS:
                problems.append(
                    f"{name}: the bias is down to {worst:.2f}s from the {allowance:.2f}s "
                    f"recorded as accepted, so the arena is fairer than this file says. "
                    f"Delete its entry from ACCEPTED -- a stale allowance hides the next "
                    f"real one.")
        elif worst > FAIR_SECONDS:
            rank, a, b = where
            ahead = "P1" if a[0] < b[0] else "P2"
            problems.append(
                f"{name}: their {rank + 1}th chests are {a[0]:.2f}s and {b[0]:.2f}s away "
                f"-- {ahead} is {worst:.2f}s ahead to an equivalent {kinds.get(a[1], '?')} "
                f"({a[2]} against {b[2]}). That is the geometry picking a winner before "
                f"either of them has done anything.")

    for n in notes:
        print("  note: " + n if not n.startswith("  ") else "  " + n)
    for p in problems:
        print("  " + p)
    print(f"chest_check: {len(problems)} problem(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
