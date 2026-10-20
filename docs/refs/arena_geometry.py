"""The exact geometry ArenaBuilder generates for the M0 greybox, as sim boxes.

Shared by the traversal and ladder-transition harnesses so both are measuring the arena the
scene actually contains, not a hand-copied approximation of it."""
from sim_motor_mirror import X

rooms, floors = 3, 3
room_w, floor_h, slab, wall, door_h = 7.2, 3.0, 0.3, 0.4, 2.2
hatch_w, ladder_w, gap_w = 1.8, 0.9, 0.728
total_w, total_h = rooms * room_w, floors * floor_h
left, right = -total_w / 2, total_w / 2
lintel, clear = floor_h - door_h, floor_h - slab


def ladder_x(f):
    """Ladders alternate sides, so no storey can be crossed in a straight line."""
    return left + room_w * 0.5 if f % 2 == 0 else right - room_w * 0.5


def pieces():
    P = []

    def box(c, s, k=0):
        P.append((c, s, k))

    for f in range(floors):
        y = f * floor_h
        holes = []
        if f > 0:
            holes.append((ladder_x(f - 1) - hatch_w / 2, ladder_x(f - 1) + hatch_w / 2))
        if f == floors - 1:
            holes.append((-gap_w / 2, gap_w / 2))
        holes.sort()
        cur = left
        for hmin, hmax in holes:
            w = hmin - cur
            if w > .05:
                box((cur + w / 2, y - slab / 2), (w, slab))
            cur = max(cur, hmax)
        w = right - cur
        if w > .05:
            box((cur + w / 2, y - slab / 2), (w, slab))
        for r in range(1, rooms):
            box((left + r * room_w, y + door_h + lintel / 2), (wall, lintel))

    box((left - wall / 2, total_h / 2), (wall, total_h))
    box((right + wall / 2, total_h / 2), (wall, total_h))
    for f in range(floors - 1):
        box((ladder_x(f), f * floor_h + floor_h / 2), (ladder_w, floor_h + slab), 2)

    box((left + 9.6, 0.0774), (1.6, 0.1547))        # step ledge
    box((left + 12.6, 1.748), (1.6, 1.904))         # low beam, crouch under it
    box((left + 16.2, 0.8), (3.0, 0.2), 1)          # low one-way
    box((left + 18.0, 1.6), (2.4, 0.2), 1)          # high one-way
    box((left + 1.2, 1.1), (0.1, 2.2))              # pillar
    box((right - 1.2, 1.1), (0.1, 2.2))             # pillar
    return P


def aabb(c, s):
    return (X(c[0] - s[0] / 2), X(c[1] - s[1] / 2), X(c[0] + s[0] / 2), X(c[1] + s[1] / 2))


def world():
    P = pieces()
    return {"solid":  [aabb(c, s) for c, s, k in P if k == 0],
            "oneway": [aabb(c, s) for c, s, k in P if k == 1],
            "ladder": [aabb(c, s) for c, s, k in P if k == 2]}
