"""Is every piece of armour in the game actually worn by anybody?

PlayerArmourView switches pieces on; it does not create them. So an arena's wardrobe is a
LIST IN A SCENE, and a list in a scene is a thing that gets written once, against whatever
the item enum said that day, and never looked at again. When an item has no set the slot
still fills, the stat still applies, the HUD still draws the icon, and the fighter looks
exactly as he did -- which makes it the one armour fault that is invisible from the inside.

It had already happened, in three of the four arenas in the map pool, in two different ways:

  Arena_Forest    sets for the base body and FOUR chest armours, one of them item 23 --
                  the shield, removed from the game -- and no helmet or belt sets at all.
                  Every helmet and every belt found in that arena was invisible.
  Arena_Egypt     the right ten sets, and no "Head Fit" bone under either head. The helmets
                  are therefore collapsed onto the head bone at 1:1 with no lift, which is
                  the exact failure ArmourWiring's own comment describes: a shell the same
                  size as the thing it covers is inside it, not on it.
  Arena_Nordic    the same.

Only Arena_Mine was right, and nothing could tell you that.

So this reads the scenes and asks three questions of each PlayerArmourView:

  * is there a set for every item a fighter can wear, and for the base body;
  * is there a set for an item that is NOT in ItemId any more;
  * and where helmets are wired, is there a Head Fit for the head to wear them over.

    cd docs/refs && python3 armour_check.py

Nothing here needs Unity. A scene is a file and a wardrobe is a list in it.
"""
import io
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(HERE, "..", "..")
ASSETS = os.path.join(ROOT, "Gulag runners", "Assets")
SCENES = os.path.join(ASSETS, "Scenes")

ARMOUR_VIEW = "dc9bbc6e954a4d2f96ec0cc2d3a455e2"      # PlayerArmourView


def read(path):
    with io.open(path, encoding="utf-8", errors="replace") as fh:
        return fh.read()


def blocks(src):
    """fileID -> (class id, body) for one YAML file, stripped documents included."""
    out, cur = {}, None
    key = None
    for line in src.split("\n"):
        m = re.match(r"^--- !u!(\d+) &(-?\d+)( stripped)?", line)
        if m:
            if key is not None:
                out[key] = cur
            key = m.group(2)
            cur = [m.group(1), []]
        elif cur is not None:
            cur[1].append(line)
    if key is not None:
        out[key] = cur
    return {k: (v[0], "\n".join(v[1])) for k, v in out.items()}


def worn_items():
    """The items a fighter can WEAR, off the enum rather than out of anybody's memory.

    Helmets, body armour and belts: the three slots PlayerArmourView dresses. The weapons
    hang off a socket and the utility slot is not worn, so neither belongs here.
    """
    src = read(os.path.join(ASSETS, "_Game", "Sim", "Items", "ItemId.cs"))
    found = {}
    for m in re.finditer(r"^\s+(\w+) = (\d+)", src, re.M):
        found[int(m.group(2))] = m.group(1)

    helmets = {n: v for n, v in found.items() if 40 <= n <= 49}
    bodies = {n: v for n, v in found.items() if 20 <= n <= 29}
    belts = {n: v for n, v in found.items() if 50 <= n <= 59}
    return helmets, bodies, belts, found


def sets_of(body):
    """item -> how many pieces, for one PlayerArmourView's serialized sets."""
    out = []
    for m in re.finditer(r"^  - item: (\d+)\n    pieces:\n((?:    - \{fileID: -?\d+\}\n)*)",
                         body, re.M):
        pieces = re.findall(r"fileID: (-?\d+)", m.group(2))
        out.append((int(m.group(1)), [p for p in pieces if p != "0"]))
    return out


def main():
    helmets, bodies, belts, all_items = worn_items()
    known = set(all_items)

    problems = []
    notes = []
    scenes = sorted(f for f in os.listdir(SCENES) if f.endswith(".unity"))
    looked = 0

    for name in scenes:
        path = os.path.join(SCENES, name)
        src = read(path)
        if ARMOUR_VIEW not in src:
            continue

        by = blocks(src)
        heads = src.count("m_Name: Head Fit")
        views = [(a, b) for a, (k, b) in by.items()
                 if k == "114" and f"guid: {ARMOUR_VIEW}" in b]
        looked += 1

        for anchor, body in views:
            wired = sets_of(body)
            have = {item for item, _ in wired}

            # The base body: the trousers and boots nothing replaces.
            if 0 not in have:
                problems.append(f"{name}: a PlayerArmourView has no set for None, which is "
                                "the base body -- the fighter's legs and feet are whatever "
                                "the character arrived switched on with")

            for group, what in ((helmets, "helmet"), (bodies, "body armour"),
                                (belts, "belt")):
                absent = [v for n, v in sorted(group.items()) if n not in have]
                if absent:
                    problems.append(
                        f"{name}: no {what} set for {', '.join(absent)}. The slot fills, "
                        "the stat applies and the fighter does not change, which is "
                        "invisible from the inside. Tools > Gulag Runners > Setup > Wire "
                        "armour builds one set per worn item")

            dead = [item for item in sorted(have) if item not in known]
            if dead:
                problems.append(
                    f"{name}: a set is wired for item {', '.join(str(d) for d in dead)}, "
                    "which is not in ItemId any more. Nothing will ever wear it, and its "
                    "pieces are switched off for ever -- the wardrobe was built against an "
                    "older item list, so what else it is missing is worth checking")

            empty = [all_items.get(item, str(item)) for item, pieces in wired if not pieces]
            if empty:
                problems.append(f"{name}: the set for {', '.join(empty)} holds no pieces, "
                                "so wearing it shows nothing")

            notes.append(f"{name}: {len(wired)} set(s) "
                         f"({sum(len(p) for _, p in wired)} piece(s))")

        # The helmets have to be worn OVER the skull, and that is a bone the wiring makes.
        if any(n in {item for a, b in views for item, _ in sets_of(b)}
               for n in helmets):
            if heads == 0:
                problems.append(
                    f"{name}: helmets are wired and there is no Head Fit bone in the scene, "
                    "so every one of them is collapsed onto the head bone at 1:1 with no "
                    "lift -- which is inside the head, not on it. ArmourWiring makes that "
                    "bone and sizes it; re-run it here")
            else:
                notes.append(f"{name}: {heads} Head Fit bone(s)")

    # One line per fault, not one per fighter: both fighters in an arena carry the same
    # wardrobe, so every problem was being printed twice and the list read as twice as bad
    # as it is.
    seen = set()
    for n in notes:
        if n in seen:
            continue
        seen.add(n)
        print(f"  note: {n}")

    unique = []
    for p in problems:
        if p in seen:
            continue
        seen.add(p)
        unique.append(p)
        print(f"  {p}")
    problems = unique
    print(f"armour_check: {len(problems)} problem(s) across {looked} scene(s) with a wardrobe")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
