"""The Egypt arena's collision is HIS blockout, not the mine's pit.

The arena is loc3_new.unity, which is his scene; this is what was done to the floors
and to the mine's frame on the way there.

"In the old Egypt arena there is the old arena LAYOUT." Measured, two things in that scene
were the mine's rather than Dawid's, and both are the layout in the sense that matters --
what you walk on and what stops you:

  1. THE FOUR POSTS. Twelve marked boxes named CageLeft: four columns 0.36 m wide at
     x -11.00, -2.43, +4.00 and +13.57 running the whole height of all three storeys,
     with a beam under the middle floor and another under the top floor at each. That is
     the mine's pit frame, re-skinned in Egyptian stone by build_egypt and solid, and
     nothing in this scene opens them -- there is no CageDoor here. So every floor is cut
     into five sections at the MINE's spacing, while his temple holds up its own floors
     with its own pillars.

  2. THE FRONT LIP ON THE UPPER TWO FLOORS. build_egypt kept the mine's three slabs on
     the grounds that they are his to the centimetre. They are, in x, in y, and at the
     BACK edge -- and not at the front:

         floor    his                      the mine's kept here
         ground   z -2.97..+2.79           the same
         middle   z -1.05..+2.87 (3.91)    z -1.72..+2.87 (4.59)
         top      z -0.07..+4.83 (4.90)    z -1.02..+4.83 (5.85)

     Two thirds of a metre and most of another, added in front, on the two floors the
     fight climbs to. His temple's upper floors end where he ended them; the arena's
     stuck out in front of them.

And the reason the lip was thought necessary turns out not to hold: SimWorldBaker here
takes its slice at planeZ 1.2 with planeThickness 3, so the band is z -0.3..+2.7, and all
three of HIS floors contain it with room to spare. The 1.2 was read from the baker rather
than guessed at, which is the whole difference.

So this restores the two upper floors to his own depth (by writing his local position and
scale onto the slabs that are already there -- the parent chains are identical, so the
world box follows), and deletes the twelve members of the mine's frame.

It does NOT mark any of his pillars solid in their place. That is a design decision with
his art in it, and the measurements for it belong to whoever makes it: with the frame gone
each floor is a clear 30 m run, which is his layout and is the question this answers.

    python3 his_layout.py          then the arena checks, which is the point
"""
import io
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
SCENES = os.path.join(HERE, "..", "..", "Gulag runners", "Assets", "Scenes")
EGYPT = os.path.join(SCENES, "loc3_new.unity")

# the mine's slab here  <-  his slab there, and the FRONT edge to pull it to.
#
# His back edges are his layout and they stay. His front edges are a blockout, and
# depth_check measures what they cost: "the storey at y +3.27 stands 1.37 m behind the one
# below it ... climbing one floor throws the fighter 1.37 m into the background, away from
# the man he is fighting and from the plane the camera frames." Which is also why the mine's
# slabs were kept in the first place -- they had a front lip, and this is the same lip, cut
# to the size the rule allows rather than to the mine's.
#
# The limit is the check's own: a storey may come as far forward as the lane of the storey
# below it plus half a body, and no further, or it is a ceiling over that storey instead of
# a floor above it. The numbers are read off depth_check after each move, because moving the
# middle floor forward moves the budget of the one above it.
# The WHOLE placement of each upper slab, not just the part that changes: his local x, his
# height, his width, and the z span the fight needs.
#
# Written down rather than read out of loc3_new, because loc3_new IS this scene now -- and
# a pass that reads the file it writes copied the middle floor onto the top one the second
# time it ran. That cost the top floor its HEIGHT, which a pass that only rewrote z could
# not heal, and chest_check found it: "2 full storey(s)", and the two top safes left with
# 2.12 m of air under them. So every number this depends on is in the table.
#
#          slab        his x    his y   his width   front    back
FLOORS = (
    ("Cube (3)",       0.019,   3.149,  30.368612,  -1.82,  +2.87),
    ("Cube (4)",      -0.090,   5.272,  30.638622,  -1.22,  +4.83),
)

FRAME = "CageLeft"


def documents(text):
    return {int(m.group(2)): (int(m.group(1)), m.group(4)) for m in
            re.finditer(r"(?m)^--- !u!(\d+) &(-?\d+)( stripped)?\n(.*?)(?=^--- !u!|\Z)",
                        text, re.S)}


def named(docs, name):
    """The GameObject with that exact name, if there is exactly one of it."""
    out = [fid for fid, (cls, body) in docs.items()
           if cls == 1 and re.search(r"(?m)^  m_Name: " + re.escape(name) + r"$", body)]
    return out[0] if len(out) == 1 else None


def transform_of(docs, go):
    for c in re.findall(r"- component: \{fileID: (-?\d+)\}", docs[go][1]):
        c = int(c)
        if c in docs and docs[c][0] == 4:
            return c
    return None


def place(body):
    """The local position and scale lines of a Transform, verbatim."""
    pos = re.search(r"(?m)^  m_LocalPosition: \{[^}]*\}$", body)
    scale = re.search(r"(?m)^  m_LocalScale: \{[^}]*\}$", body)
    return (pos.group(0) if pos else None), (scale.group(0) if scale else None)


def main():
    text = io.open(EGYPT, encoding="utf-8").read()
    docs = documents(text)

    # ---- the two upper floors: his back edge, the front edge the rule allows
    for mine_name, his_x, his_y, his_w, front, back in FLOORS:
        a = named(docs, mine_name)
        if a is None:
            print(f"  {mine_name}: not a single object in this scene; skipped")
            continue
        at = transform_of(docs, a)
        was_pos, was_scale = place(docs[at][1])
        if None in (was_pos, was_scale):
            print(f"  {mine_name}: no position or scale to write; skipped")
            continue

        # The slab's parent turns local x into world depth and leaves local z as world
        # z, so a world span [front, back] is a local scale.x of back - front centred at
        # local z (front + back) / 2. Local y is the storey's height and local scale.z is
        # its width along the arena, both his.
        depth = back - front
        at_z = front + depth / 2.0
        pos = "  m_LocalPosition: {x: %.6g, y: %.6g, z: %.6g}" % (his_x, his_y, at_z)
        scale = "  m_LocalScale: {x: %.6g, y: 0.23572, z: %.6g}" % (depth, his_w)

        if (was_pos, was_scale) == (pos, scale):
            print(f"  {mine_name} already stands at z {front:+.2f}..{back:+.2f}")
            continue
        body = docs[at][1]
        text = text.replace(body, body.replace(was_pos, pos).replace(was_scale, scale))
        docs = documents(text)
        print(f"  {mine_name}: z {front:+.2f}..{back:+.2f} ({depth:.2f} m deep; "
              f"his back edge, the front edge the rule allows)")

    # ---- the mine's frame
    gone = 0
    while True:
        go = next((fid for fid, (cls, body) in documents(text).items()
                   if cls == 1 and re.search(r"(?m)^  m_Name: " + FRAME, body)), None)
        if go is None:
            break
        docs = documents(text)
        drop = {go} | {int(c) for c in
                       re.findall(r"- component: \{fileID: (-?\d+)\}", docs[go][1])}
        for fid in sorted(drop):
            if fid not in docs:
                continue
            cls, _body = docs[fid]
            head = f"--- !u!{cls} &{fid}\n"
            at = text.find(head)
            if at < 0:
                continue
            end = text.find("--- !u!", at + len(head))
            text = text[:at] + (text[end:] if end > 0 else "")
        text = re.sub(r"(?m)^  - \{fileID: (?:%s)\}\n" % "|".join(str(d) for d in drop),
                      "", text)
        text = re.sub(r"(?m)^  - target: \{fileID: (?:%s)\}[^\n]*\n(?:    [^\n]*\n)*"
                      % "|".join(str(d) for d in drop), "", text)
        gone += 1
    print(f"  {gone} member(s) of the mine's frame removed"
          if gone else "  the mine's frame is already gone")

    io.open(EGYPT, "w", encoding="utf-8").write(text)
    print("  wrote loc3_new.unity")
    return 0


if __name__ == "__main__":
    sys.exit(main())
