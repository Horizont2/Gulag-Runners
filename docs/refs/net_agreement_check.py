"""Do two devices actually agree on the arena they are about to fight in?

Lockstep agrees on INPUT and nothing else. Both phones step the same ticks from the same
seed, and everything either of them collides with comes from the world IT baked out of ITS
OWN scene. Nothing in the handshake compared the two, and nothing in the running checksum
does either: NetMatch.Hash folds the fighters' positions and health, not the ground under
them. So two devices in different arenas pair happily and agree tick by tick while one
fighter stands on a floor the other has not got.

From the outside that is "after connecting, players fall through the ground, or can just
fly" -- which is the report this check was written for. It does not prove that is what
happened; it closes the hole that would let it happen silently.

What it measures:

  the arenas are telling apart      -- FAILS when two produce the same fingerprint
      The whole mechanism is one number per arena. If two of the pool's maps fold to the
      same one, the handshake would wave a mismatched pair straight through, and this
      would be worse than no check at all because it would look like one.

  the welcome carries it            -- FAILS when the packet or the comparison is gone
      The host writes its fingerprint into the welcome and the guest refuses a host whose
      number differs. Both halves are read out of LanLink.cs, because a check that only
      measured the arenas would go on passing after somebody deleted the comparison.

The fingerprint here is a MIRROR of SimWorld.Fingerprint: same FNV fold, same order, same
raw fixed point. If the two drift apart this check is measuring something the game does
not do, so the fold is written out here in full rather than imported from anywhere.

Run: python3 net_agreement_check.py
"""

import os
import re
import sys

import arena_demo_geometry as G

HERE = os.path.dirname(os.path.abspath(__file__))
SCENES = os.path.join(HERE, "..", "..", "Gulag runners", "Assets", "Scenes")
LANLINK = os.path.join(HERE, "..", "..", "Gulag runners", "Assets",
                       "_Game", "Net", "LanLink.cs")
SIMWORLD = os.path.join(HERE, "..", "..", "Gulag runners", "Assets",
                        "_Game", "Sim", "World", "SimWorld.cs")

ARENAS = ["Arena_Forest", "Arena_Mine", "Arena_Egypt", "Arena_Nordic fortress"]

ONE = 65536          # Q16.16, the simulation's fixed point


def fold(h, value):
    value = int(value) & 0xFFFFFFFF
    h = ((h ^ (value & 0xFFFF)) * 16777619) & 0xFFFFFFFF
    return ((h ^ ((value >> 16) & 0xFFFF)) * 16777619) & 0xFFFFFFFF


def raw(x):
    return int(round(float(x) * ONE)) & 0xFFFFFFFF


def fingerprint(baked):
    """SimWorld.Fingerprint, mirrored."""
    h = 2166136261
    for key in ("solid", "oneway", "ladder"):
        boxes = baked[key]
        h = fold(h, len(boxes))
        for box in boxes:
            for edge in box:
                h = fold(h, raw(edge))
    for key in ("chest", "gate"):
        h = fold(h, len(baked.get(key, ())))
    return h


def main():
    problems, notes = [], []

    # ---- one number per arena, and no two the same
    seen = {}
    for name in ARENAS:
        path = os.path.join(SCENES, name + ".unity")
        if not os.path.exists(path):
            problems.append(f"{name}: no such scene to fingerprint")
            continue
        try:
            baked = G.bake(scene=G.Scene(path))
        except SystemExit as stop:
            # arena_demo_geometry quits rather than returns on a scene it cannot read.
            problems.append(f"{name}: the baker refused it -- {stop}")
            continue

        mark = fingerprint(baked)
        notes.append(f"  note: {name}: {len(baked['solid'])} solid(s), "
                     f"{len(baked['ladder'])} ladder(s) -> {mark:08X}")

        if mark in seen:
            problems.append(
                f"{name} and {seen[mark]} fold to the same fingerprint {mark:08X}, so the "
                f"handshake cannot tell them apart and would let a mismatched pair "
                f"connect. That is worse than no check, because it looks like one.")
        seen[mark] = name

    # ---- and the handshake actually uses it
    if not os.path.exists(LANLINK):
        problems.append("LanLink.cs is gone; there is no handshake to check.")
    else:
        source = open(LANLINK, encoding="utf-8").read()

        if "WorldFingerprint" not in source:
            problems.append(
                "LanLink has no WorldFingerprint, so the welcome carries only the seed "
                "and two devices in different arenas pair without a word.")
        else:
            welcome = re.search(r"void Welcome\(\)(?:.|\n)*?\n        \}", source)
            if welcome is None or "WorldFingerprint" not in welcome.group(0):
                problems.append(
                    "LanLink.Welcome no longer writes the arena fingerprint, so the guest "
                    "has nothing to compare and the check is dead while still present.")

            if "theirs != WorldFingerprint" not in source:
                problems.append(
                    "LanLink no longer compares the host's fingerprint with its own, so a "
                    "device in another arena is let in again.")

    if not os.path.exists(SIMWORLD):
        problems.append("SimWorld.cs is gone.")
    elif "public uint Fingerprint()" not in open(SIMWORLD, encoding="utf-8").read():
        problems.append(
            "SimWorld.Fingerprint is gone, so there is no number for the link to send.")

    for line in notes:
        print(line)
    for line in problems:
        print(f"  {line}")
    print(f"net_agreement_check: {len(problems)} problem(s) across "
          f"{len(seen)} arena(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
