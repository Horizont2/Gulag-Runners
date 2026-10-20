"""The menu scene's own invariants, chiefly the one that cost a commit.

PURE VOLUMETRIC FOG ONLY WORKS OVER A TERRAIN. Its UpdateTerrainBounds builds the whole
volume out of terrain bounds and ends with "if (!_hasTerrainBounds) return", so a scene
with the component and no terrain renders no fog at all -- not thin fog, none. There is no
error and no warning; it simply does nothing, which is how it was switched off for a whole
commit by turning the terrain auto-find off.

And the volume is terrain bounds PLUS horizontalPadding, so the padding has to be wide
enough to reach whatever is being fogged. Shrinking it to the size of the room drops the
room outside its own fog.

Run: python3 menu_check.py"""
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
SCENES = os.path.join(HERE, "..", "..", "Gulag runners", "Assets", "Scenes")


def read(path):
    with open(path, encoding="utf-8", errors="replace") as f:
        return f.read()


def documents(text):
    heads = [(m.start(), m.end(), int(m.group(1)))
             for m in re.finditer(r"^--- !u!(\d+) &-?\d+.*$", text, re.M)]
    out = []
    for i, (start, after, cls) in enumerate(heads):
        stop = heads[i + 1][0] if i + 1 < len(heads) else len(text)
        out.append((cls, text[after:stop]))
    return out


def field(body, key, default=None):
    m = re.search(rf"^[ \t]*{key}:[ \t]*(.*)$", body, re.M)
    return m.group(1).strip() if m else default


def check(path):
    text = read(path)
    docs = documents(text)
    name = os.path.basename(path)

    fog = [b for cls, b in docs if cls == 114 and "settingsVersion:" in b
           and "groundFogHeight" in b]
    if not fog:
        return 0, False
    fog = fog[0]

    # A Terrain component is class 218, and one inside a prefab instance still writes it.
    terrains = [b for cls, b in docs if cls == 218]
    problems = 0

    print(f"  {name}: volumetric fog present, {len(terrains)} terrain(s) in the scene")

    if not terrains:
        problems += 1
        print("      PROBLEM - no Terrain. The fog builds its entire volume from terrain "
              "bounds and renders NOTHING without one. It does not warn; it just stops.")

    auto = field(fog, "findActiveTerrainsAutomatically", "1")

    # A list entry of {fileID: 0} is an EMPTY SLOT, not a terrain. The menu scene has
    # exactly one of those, so a list that looks populated in the inspector resolves to
    # nothing -- which is the state that kills the fog the moment auto-find goes off.
    listed = re.search(r"^[ \t]*terrains:[ \t]*\n(?:[ \t]*-[ \t]*\{fileID: (-?\d+)[^\n]*\n)+",
                       fog, re.M)
    real = False
    if listed:
        for one in re.findall(r"-[ \t]*\{fileID: (-?\d+)", listed.group(0)):
            if one != "0":
                real = True

    if auto == "0" and not real:
        problems += 1
        print("      PROBLEM - terrain auto-find is OFF and nothing in the terrain list "
              "resolves, so no terrain is found and there is no fog. Switch it on, or "
              "drop a real terrain into the list.")
    elif not real and listed:
        print("      note - the terrain list holds an empty slot. Harmless while auto-find "
              "is on, and the fog's off switch the moment it is not.")

    # The volume is terrain bounds + this padding. Too small and the room it is meant to
    # fog is outside it.
    try:
        pad = float(field(fog, "horizontalPadding", "0"))
        if pad < 20:
            problems += 1
            print(f"      PROBLEM - horizontal padding is {pad:g} m. The volume is the "
                  "terrain's bounds plus this, so anything further than that from the "
                  "terrain falls outside the fog entirely.")
    except ValueError:
        pass

    # Numbers the component silently clamps, so what is in the file is a lie about what
    # the effect will do.
    for key, low, high in (("targetStepLength", 2.0, None), ("density", None, 0.3),
                           ("shadowDistance", 20.0, None), ("groundFogHeight", 1.0, None)):
        raw = field(fog, key)
        if raw is None:
            continue
        try:
            v = float(raw)
        except ValueError:
            continue
        if low is not None and v < low:
            print(f"      note - {key} is {v:g}; the component clamps it to {low:g}, so "
                  "that is what it will actually use")
        if high is not None and v > high:
            print(f"      note - {key} is {v:g}; the component clamps it to {high:g}")

    return problems, True


def main():
    looked = 0
    problems = 0
    for f in sorted(os.listdir(SCENES)):
        if not f.endswith(".unity"):
            continue
        bad, had = check(os.path.join(SCENES, f))
        problems += bad
        looked += 1 if had else 0

    if looked == 0:
        print("menu_check: no scene uses the volumetric fog — nothing to check.")
        return 0

    print(f"menu_check: {looked} scene(s) with fog, {problems} problem(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
