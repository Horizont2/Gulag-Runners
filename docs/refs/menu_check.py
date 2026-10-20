"""The menu scene's own invariants, chiefly the one that cost a commit.

PURE VOLUMETRIC FOG ONLY WORKS OVER A TERRAIN. Its UpdateTerrainBounds builds the whole
volume out of terrain bounds and ends with "if (!_hasTerrainBounds) return", so a scene
with the component and no terrain renders no fog at all -- not thin fog, none. There is no
error and no warning; it simply does nothing, which is how it was switched off for a whole
commit by turning the terrain auto-find off.

And the volume is terrain bounds PLUS horizontalPadding, so the padding has to be wide
enough to reach whatever is being fogged. Shrinking it to the size of the room drops the
room outside its own fog.

It also counts the menu's lights, because URP drops the surplus past
m_AdditionalLightsPerObjectLimit per object per frame -- silently, and differently as the
camera moves, so a fifth light in one small room does not look like a fifth light, it
looks like the other four flickering. The menu is one room where every light reaches
everything in it, which is why this is checked here and not for the arena.

Run: python3 menu_check.py"""
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
SCENES = os.path.join(HERE, "..", "..", "Gulag runners", "Assets", "Scenes")
SETTINGS = os.path.join(HERE, "..", "..", "Gulag runners", "Assets", "Settings")


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


def per_object_limit():
    """The tightest additional-light cap across the pipeline assets, or URP's default."""
    best = None
    if os.path.isdir(SETTINGS):
        for f in sorted(os.listdir(SETTINGS)):
            if not f.endswith(".asset"):
                continue
            m = re.search(r"m_AdditionalLightsPerObjectLimit: (\d+)",
                          read(os.path.join(SETTINGS, f)))
            if m:
                n = int(m.group(1))
                best = n if best is None else min(best, n)
    return best if best is not None else 4


def lights_and_torches(path):
    """The menu's light budget, and whether every torch was treated the same."""
    text = read(path)
    docs = documents(text)
    problems = 0

    # GameObjects by fileID, so a light can be traced back to the object carrying it.
    by_id, owner = {}, {}
    for chunk in re.split(r"^--- ", text, flags=re.M)[1:]:
        m = re.match(r"!u!(\d+) &(-?\d+)( stripped)?", chunk)
        cls, fid, stripped = int(m.group(1)), m.group(2), bool(m.group(3))
        if cls == 1 and not stripped:
            nm = re.search(r"^  m_Name: (.*)$", chunk, re.M)
            by_id[fid] = (nm.group(1).strip() if nm else "",
                          field(chunk, "m_IsActive", "1") == "1")
        else:
            g = re.search(r"m_GameObject: \{fileID: (-?\d+)\}", chunk)
            if g:
                owner.setdefault(g.group(1), []).append((cls, chunk))

    cap = per_object_limit()
    extra = 0
    for go, comps in owner.items():
        on = by_id.get(go, ("", True))[1]
        for cls, body in comps:
            if cls != 108 or not on:
                continue
            if field(body, "m_Enabled", "1") != "1":
                continue
            if field(body, "m_Type", "2") != "1":       # 1 is directional; the rest count
                extra += 1

    print(f"  {os.path.basename(path)}: {extra} additional light(s), "
          f"pipeline cap {cap} per object")
    if extra > cap:
        problems += 1
        print(f"      PROBLEM - {extra} additional lights in one room against a cap of "
              f"{cap}. URP keeps the {cap} it likes best PER OBJECT PER FRAME and drops "
              "the rest, so they will blink in and out as the camera moves.")

    # Torches: the fire goes on all of them or the unlit ones read as broken props.
    torches = [b for cls, b in docs
               if cls == 1001 and re.search(r"value: Torch(?: \(\d+\))?\s*$", b, re.M)]
    fires = [go for go, (nm, _) in by_id.items() if nm == "Torch Fire"]
    if torches:
        print(f"      {len(torches)} torch(es), {len(fires)} with fire")
        if len(fires) < len(torches):
            problems += 1
            print(f"      PROBLEM - {len(torches) - len(fires)} torch(es) carry no fire. "
                  "Run Tools/Gulag Runners/Light the torches.")

    # A steady lamp on a flame is the thing that made them read as props to begin with.
    for go in fires:
        comps = owner.get(go, [])
        has_light = any(cls == 108 for cls, _ in comps)
        has_flicker = any(cls == 114 and "baseIntensity:" in b for cls, b in comps)
        if has_light and not has_flicker:
            problems += 1
            print(f"      PROBLEM - a Torch Fire carries a Light with no TorchLight on it, "
                  "so the flame moves and the light it casts does not.")

    return problems


def main():
    looked = 0
    problems = 0
    for f in sorted(os.listdir(SCENES)):
        if not f.endswith(".unity"):
            continue
        bad, had = check(os.path.join(SCENES, f))
        problems += bad
        looked += 1 if had else 0
        if f.lower().startswith("menu"):
            problems += lights_and_torches(os.path.join(SCENES, f))

    if looked == 0:
        print("menu_check: no scene uses the volumetric fog — nothing to check.")
        return 0

    print(f"menu_check: {looked} scene(s) with fog, {problems} problem(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
