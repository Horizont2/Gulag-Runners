"""A second way up each floor of Arena_Egypt, on the stairs its author already drew.

WHAT THIS IS FOR. chest_check measures the arena in seconds and says, with two ladders:
"the 5th chest each player can reach is 12.83 s away for P1 and 18.85 s for P2 -- that is
the geometry picking a winner before either of them has done anything." The mine has the
same shape and carries it as a recorded compromise, because the mine is kept as its author
built it. Egypt does not have to inherit it: the arena was transplanted, so its ways up are
a decision rather than a given.

The first attempt mirrored each ladder in x -- a copy of the ladder at -x, same lean. It
measured fair (the worst matched pair went from 6.02 s to 0.20 s) and it looked wrong, so
the author deleted both copies in Unity, which is the correct instinct: measured against
the art, the copy of the ground ladder stood 0.29 m from a stack of crates and the copy of
the upper one leaned into a pillar and two flags. A mirror in x is a mirror of the
GEOMETRY and knows nothing about what is standing there.

So this puts the second climb where the level already says people climb: Dawid's own
staircases. He drew five, and two of them are on the far side from the two ladders --
Stairs_01 at x +11.37 on the ground floor and Stairs_01 (3) at x -13.31 on the middle one.
A climb there reads as the stairs it is standing on, and it is nowhere near the crates.

The copy keeps the original's y, z, lean and scale. The z especially: depth is a LANE in
this game and a climb in the wrong lane is a climb no fighter can reach.

    python3 egypt_climbs.py            then: python3 chest_check.py
"""
import io
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
SCENE = os.path.join(HERE, "..", "..", "Gulag runners", "Assets", "Scenes",
                     "Arena_Egypt.unity")

# (the ladder to copy, the x for the copy, the suffix for its name, why there)
#
# MEASURED, four placements each, because the first guess was wrong twice. Putting the
# second climb on Dawid's own staircases reads better and does not work: at x +11.37 the
# new shaft overlaps the +11.06 one, so the two holes in the middle floor become one and
# the climber arriving from below cannot stand to transfer -- chest_check still measured
# 6.02 s. At +13.38 and at +6.90 the times matched to within a tenth but the ORDER did
# not: a player reached a safe while the other was still on lockers, which chest_check
# counts as the arena not offering them the same thing.
#
# The mirror in x is the only placement that measures clean: worst matched pair 0.00 s,
# nothing out of order. And the looks it was deleted for are not a collision -- measured
# against the art, the nearest thing to either copy is a crate 0.29 m away and a pillar
# 1.05 m away. The name carries the reason, so the next person reading the hierarchy sees
# why a second ladder is there before deciding it is a duplicate.
CLIMBS = (
    ("Ladder_A (2)", 9.14, "fair mirror",
     "the mirror of the ground ladder: P2 climbs on their own side"),
    ("Ladder_A (4)", -11.07, "fair mirror",
     "the mirror of the upper ladder: P1 climbs on theirs"),
)

BASE = 1_920_400_000


def documents(text):
    return {int(m.group(2)): (int(m.group(1)), bool(m.group(3)), m.group(4)) for m in
            re.finditer(r"(?m)^--- !u!(\d+) &(-?\d+)( stripped)?\n(.*?)(?=^--- !u!|\Z)",
                        text, re.S)}


def instance_name(body):
    m = re.search(r"propertyPath: m_Name\n\s+value: ([^\n]*)", body)
    return m.group(1).strip() if m else ""


def group_of(docs, fid):
    """An instance and everything that belongs to it: its stripped shells and the
    components added to it in the scene."""
    out = [fid]
    for other, (_cls, _stripped, body) in docs.items():
        if other != fid and f"m_PrefabInstance: {{fileID: {fid}}}" in body:
            out.append(other)
    for m in re.finditer(r"addedObject: \{fileID: (-?\d+)\}", docs[fid][2]):
        if int(m.group(1)) not in out:
            out.append(int(m.group(1)))
    return out


def clone(text, name, x, suffix, at):
    docs = documents(text)

    # By the WHOLE name, not by the suffix: both climbs carry the same suffix, so a check
    # on the suffix alone added the first copy and then told itself the second was
    # already there.
    if any(instance_name(b) == f"{name} {suffix}"
           for _c, _s, b in (docs[f] for f in docs) if _c == 1001):
        print(f"  {name} {suffix} is already in the scene")
        return text, False

    source = next((f for f, (cls, _s, body) in sorted(docs.items())
                   if cls == 1001 and instance_name(body) == name), None)
    if source is None:
        print(f"  no instance called {name!r} to copy; nothing added")
        return text, False

    members = group_of(docs, source)
    remap = {}
    for i, old in enumerate(members):
        new = at + i
        if f"&{new}" in text:
            print(f"  fileID {new} is taken; nothing added")
            return text, False
        remap[old] = new

    block = ""
    for old in members:
        cls, stripped, body = docs[old]
        body = re.sub(r"\{fileID: (-?\d+)\}",
                      lambda m: "{fileID: %d}" % remap.get(int(m.group(1)),
                                                           int(m.group(1))),
                      body)
        if cls == 1001:
            body = re.sub(r"(propertyPath: m_LocalPosition\.x\n\s+value: )[-\d.eE+]+",
                          lambda m: m.group(1) + f"{x:.6g}", body)
            body = re.sub(r"(propertyPath: m_Name\n\s+value: )([^\n]*)",
                          lambda m: m.group(1) + m.group(2).strip() + " " + suffix, body)
        block += f"--- !u!{cls} &{remap[old]}{' stripped' if stripped else ''}\n{body}"

    text = text.rstrip("\n") + "\n" + block.rstrip("\n") + "\n"

    # The parent's child list points at the stripped TRANSFORM, not at the instance.
    parent = int(re.search(r"m_TransformParent: \{fileID: (-?\d+)\}",
                           docs[source][2]).group(1))
    shell = next((remap[o] for o in members if docs[o][0] == 4), None)
    if parent and shell:
        body = documents(text)[parent][2]
        fixed = (body.replace("m_Children: []", f"m_Children:\n  - {{fileID: {shell}}}")
                 if "m_Children: []" in body
                 else re.sub(r"(  m_Children:\n)", r"\g<1>" + f"  - {{fileID: {shell}}}\n",
                             body, count=1))
        text = text.replace(body, fixed)
    return text, True


def main():
    text = io.open(SCENE, encoding="utf-8").read()
    made = 0
    for name, x, suffix, why in CLIMBS:
        text, added = clone(text, name, x, suffix, BASE + made * 20)
        if added:
            print(f"  {name} copied to x {x:+.2f} -- {why}")
            made += 1

    if made:
        io.open(SCENE, "w", encoding="utf-8").write(text)
        print(f"  wrote Arena_Egypt.unity with {made} more climb(s)")
    else:
        print("  nothing to add")
    return 0


if __name__ == "__main__":
    sys.exit(main())
