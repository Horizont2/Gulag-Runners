#!/usr/bin/env python3
"""Write a measured blade axis onto a weapon's row in both fighters.

ModelShape reads which way a weapon points off its own meshes, and that is right for ten
of the eleven this game ships. The scythe is the one it cannot be right about: its snath
is CURVED and authored across the model's axes, so no axis of the box is the line the hand
holds. ItemModel.bladeAxis exists for exactly that, and this fills it in with the number
weapon_grip_check fits rather than one typed by eye.

    python3 docs/refs/weapon_blade_axis.py            # report only
    python3 docs/refs/weapon_blade_axis.py --write    # author it

Idempotent, and narrow on purpose: it changes `bladeAxis` on the one row whose model is
the named weapon, in each fighter prefab, and nothing else. Re-run it after the model
changes; the value is measured here, not remembered.
"""

import pathlib
import re
import sys

import weapon_grip_check as grip

ASSETS = grip.ASSETS

# The weapons whose line the measurement cannot find, and why. Anything not here is
# measured at runtime and must stay that way -- an authored axis that agrees with the
# measurement is just a number to keep in step with the model.
AUTHOR = {
    "Scythe": "the snath is curved and sits across the model's axes",
}


def item_ids():
    """{name: value} straight out of the simulation's own enum."""
    source = (ASSETS / "_Game/Sim/Items/ItemId.cs").read_text()
    return {m.group(1): int(m.group(2))
            for m in re.finditer(r"^\s+(\w+) = (\d+),", source, re.M)}


# The pack's prefab names against the game's item names, as WeaponWiring maps them. Only
# the ones here can be authored; the rest are measured and must stay so.
AS_ITEM = {"Scythe": "Scythe"}


def row_for(text, item):
    """The (start, end) of one weapon row's bladeAxis line, or None."""
    at = text.find("  weapons:")
    if at < 0:
        return None
    end = text.find("  armour:", at)
    block = text[at:end if end > 0 else len(text)]

    m = re.search(rf"- item: {item}\s*\n\s+root: \{{fileID: \d+\}}\s*\n"
                  rf"(\s+bladeAxis: \{{x: \S+, y: \S+, z: \S+\}})", block)
    if m is None:
        return None
    return at + m.start(1), at + m.end(1)


def measured(name, paths):
    """The weapon's own haft direction, fitted fresh from its mesh."""
    prefab = next((p for g, p in paths.items()
                   if p.stem == name and p.suffix == ".prefab"), None)
    if prefab is None:
        return None
    fbx = paths.get(grip.mesh_of(prefab))
    if fbx is None or not fbx.exists():
        return None

    fit = grip.haft(grip.vertices(fbx))
    if fit is None:
        return None

    d = fit[0]
    n = sum(c * c for c in d) ** 0.5
    return [round(c / n, 4) for c in d]


def main():
    write = "--write" in sys.argv
    paths = grip.asset_paths()
    ids = item_ids()
    changed = 0

    for fighter in grip.FIGHTERS:
        path = ASSETS / fighter
        if not path.exists():
            print(f"{fighter}: not there")
            continue

        text = path.read_text()

        for name, why in AUTHOR.items():
            item = ids.get(AS_ITEM.get(name, name))
            if item is None:
                print(f"{fighter}/{name}: no such item in ItemId")
                continue

            d = measured(name, paths)
            if d is None:
                print(f"{fighter}/{name}: the haft will not fit, nothing written")
                continue

            # Matched on the ITEM, not on the model reference. Player 2's rows all point
            # at nothing -- see weapon_grip_check -- and an axis is still worth having on
            # a row whose model a scene supplies.
            span = row_for(text, item)
            if span is None:
                print(f"{fighter}/{name}: no row for item {item} -- nothing written")
                continue

            line = f"    bladeAxis: {{x: {d[0]}, y: {d[1]}, z: {d[2]}}}"
            already = text[span[0]:span[1]] == line
            off = grip.angle(d, (0.0, 1.0, 0.0))
            print(f"{fighter}/{name}: blade axis "
                  f"({d[0]:+.4f}, {d[1]:+.4f}, {d[2]:+.4f}), {off:.1f}d off +Y"
                  + ("  (already)" if already else "")
                  + f"\n    because {why}")
            if not already:
                changed += 1
                text = text[:span[0]] + line + text[span[1]:]

        if write:
            path.write_text(text)

    print(f"\n{changed} row(s) to change"
          + ("" if write else "  (nothing written -- pass --write)"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
