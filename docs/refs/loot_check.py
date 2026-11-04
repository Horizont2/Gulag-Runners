"""Does anything come out of the chests, and can you see it when it does?

The loot pool is simulation: a chest opens, an item enters a slot, and the slot is
pickable whether or not anything is drawn for it. Drawing is a separate thing -- one
GroundItemView per slot, wired into a LootView. Miss that out and the arena still works
perfectly, chests still open, items still go in and out of the pool, and the player sees
nothing happen at all. That is what the Nordic arena did: it had chests and no Loot group,
so the one visible reward in the scavenge phase was invisible.

It is a quiet failure in both directions. Too FEW views and the extra slots are pickable
but unseen, which reads as loot you walk through. So this counts them against the pool,
and then checks the thing underneath: that whatever each chest actually contains has a
model to be drawn with, or at least falls back to the stand-in rather than nothing.

    python3 loot_check.py
"""
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import arena_demo_geometry as geometry           # noqa: E402

ASSETS = os.path.join(HERE, "..", "..", "Gulag runners", "Assets")
SCENES = os.path.join(ASSETS, "Scenes")
SRC = os.path.join(ASSETS, "_Game", "Presentation")


def guid_of(script):
    return re.search(r"guid: ([0-9a-f]{32})",
                     open(os.path.join(SRC, script + ".cs.meta"),
                          encoding="utf-8").read()).group(1)


def components(text, guid):
    out = []
    for m in re.finditer(r"--- !u!114 &(-?\d+)( stripped)?\nMonoBehaviour:\n(.*?)(?=\n--- !u!|\Z)",
                         text, re.S):
        if guid in m.group(3):
            out.append((int(m.group(1)), bool(m.group(2)), m.group(3)))
    return out


def ground_item_models():
    """Which ItemIds the GroundItem prefab can draw, and whether it has a stand-in."""
    path = os.path.join(ASSETS, "Prefabs", "GroundItem.prefab")
    if not os.path.exists(path):
        return None, False
    text = open(path, encoding="utf-8", errors="ignore").read()
    block = re.search(r"  models:\n((?:  - item: .*\n    root: .*\n)*)", text)
    items = {int(x) for x in re.findall(r"- item: (\d+)", block.group(1))} if block else set()
    fallback = re.search(r"  fallbackModel: \{fileID: (-?\d+)\}", text)
    return items, bool(fallback and fallback.group(1) != "0")


def main(argv):
    wanted = argv or geometry.arena_scenes()
    problems, notes = [], []

    match_guid = guid_of("MatchState")
    loot_guid = guid_of("LootView")
    view_guid = guid_of("GroundItemView")

    drawable, has_fallback = ground_item_models()
    notes.append(f"GroundItem.prefab draws item(s) {sorted(drawable)} directly" +
                 (" and everything else as the stand-in" if has_fallback
                  else " and has NO stand-in"))

    for name in wanted:
        path = os.path.join(SCENES, name)
        text = open(path, encoding="utf-8", errors="ignore").read()
        scene = geometry.Scene(path)
        chests = scene.prefab_chests()

        state = components(text, match_guid)
        if not state:
            notes.append(f"{name}: no MatchState, so there is no pool to draw")
            continue
        capacity = int((re.search(r"  groundItemCapacity: (\d+)", state[0][2])
                        or re.match(r"(0)", "0")).group(1))

        loots = components(text, loot_guid)
        views = [v for v in components(text, view_guid)]
        wired = 0
        if loots:
            block = re.search(r"  views:\n((?:  - \{fileID: -?\d+\}\n)*)", loots[0][2])
            wired = len(re.findall(r"\{fileID: (-?\d+)\}", block.group(1))) if block else 0

        notes.append(f"{name}: pool of {capacity}, {len(loots)} LootView, "
                     f"{len(views)} GroundItemView in the scene, {wired} wired into it, "
                     f"{len(chests)} chest(s)")

        if chests and not loots:
            problems.append(
                f"{name}: {len(chests)} chest(s) and no LootView, so everything they pay "
                f"out is invisible. The pool still works and the items are still pickable "
                f"— the player just never sees one. Add a Loot object with a LootView and "
                f"{capacity} GroundItem prefab instances under it.")
            continue
        if not loots:
            continue

        if wired < capacity:
            problems.append(
                f"{name}: the pool holds {capacity} item(s) and only {wired} view(s) are "
                f"wired, so {capacity - wired} slot(s) are pickable and invisible — loot "
                f"the player walks through.")

        # And what the chests in THIS arena actually pay out. Contents of 0 is ItemId.None,
        # which is NOT an empty chest: LootTable reads it as "roll this kind from the drop
        # table", which is how a looter is supposed to behave. A fixed item is the special
        # case, and the only one whose model can be checked from here.
        rolling = [c for c in chests if c[2] == 0]
        fixed = [c for c in chests if c[2] != 0]
        if rolling:
            notes.append(f"  {len(rolling)} chest(s) roll their contents from the drop "
                         f"table, so what is drawn depends on the roll")
        for _box, _kind, contents, label in fixed:
            if contents in drawable:
                notes.append(f"  {label} always gives item {contents}, which has a model")
                continue
            if has_fallback:
                notes.append(f"  {label} always gives item {contents}, which has no model: "
                             f"drawn as the stand-in")
            else:
                problems.append(
                    f"{name}: {label} always gives item {contents}, which has no model and "
                    f"there is no stand-in either, so opening it shows nothing at all.")

        if rolling and not has_fallback:
            missing = sorted(set(range(1, 32)) - drawable)
            problems.append(
                f"{name}: {len(rolling)} chest(s) roll from the drop table and there is no "
                f"stand-in model, so any roll outside {sorted(drawable)} pays out something "
                f"invisible (items {missing[:8]}...).")

    for n in notes:
        print("  note: " + n if not n.startswith("  ") else "  " + n)
    for p in problems:
        print("  " + p)
    print(f"loot_check: {len(problems)} problem(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
