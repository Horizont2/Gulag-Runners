"""Is each arena's fighter wired to the things only that scene can give it?

A fighter is a prefab, and most of what it needs travels with it. The exceptions are the
things that live in the SCENE and cannot be in a prefab: the HUD's joysticks and buttons,
the MatchState, the world baker, the spawn marker, the blood that plays where it is hit.
Each of those is a reference from the prefab instance out into the scene, and every one
of them has to be made again, by hand, every time an arena is built.

Miss one and nothing breaks. There is no error, no warning, no missing script -- the
field is simply null and the feature it drives quietly does not happen. The Nordic arena
shipped with the HUD's two joysticks and three buttons wired to nobody: the controls drew
themselves, moved under your thumb, and sent their input to no fighter, which from the
outside is a combat HUD that "does not react to anything".

The arenas are each other's reference. Every scene-level field that ANY arena sets is one
the others are expected to set too, and a field that one wires and another leaves empty
is the shape of this fault. That is a comparison rather than a list, so it covers fields
nobody has thought to write down -- including the ones added after this was written.

    python3 fighter_check.py
"""
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

ASSETS = os.path.join(HERE, "..", "..", "Gulag runners", "Assets")
SCENES = os.path.join(ASSETS, "Scenes")
PRESENT = os.path.join(ASSETS, "_Game", "Presentation")

PLAYER_SCRIPT = "882fe3a922af38d6658c85cd54734955"      # PlayerController

# The touch controls, which are the one group with a hard rule rather than a comparison:
# the HUD drives whoever is holding the phone, so SOME fighter in the scene has to be
# holding it. None, and the controls are furniture.
CONTROLS = ("stick", "aimStick", "jumpButton", "dodgeButton", "actionButton")

# Overrides that say nothing about wiring.
BORING = re.compile(r"^(m_Local|m_Name|m_RootOrder|m_IsActive|m_Constrain|m_Layer)")


def guid_index():
    out = {}
    for base, dirs, files in os.walk(ASSETS):
        dirs[:] = [d for d in dirs if not d.startswith(".")]
        for f in files:
            if not f.endswith(".meta"):
                continue
            try:
                t = open(os.path.join(base, f), encoding="utf-8", errors="ignore").read()
            except OSError:
                continue
            m = re.search(r"guid: ([0-9a-f]{32})", t)
            if m:
                out[m.group(1)] = os.path.join(base, f[:-5])
    return out


def fighters(text, guids):
    """Each fighter in the scene: (name, {field: is it pointed at something})."""
    out = []
    for m in re.finditer(r"--- !u!1001 &(-?\d+)\nPrefabInstance:\n(.*?)(?=\n--- !u!|\Z)",
                         text, re.S):
        body = m.group(2)
        g = re.search(r"m_SourcePrefab: \{fileID: \d+, guid: ([0-9a-f]{32})", body)
        if not g:
            continue
        src = guids.get(g.group(1))
        if not src or not src.endswith(".prefab"):
            continue
        try:
            if PLAYER_SCRIPT not in open(src, encoding="utf-8", errors="ignore").read():
                continue
        except OSError:
            continue

        name = re.search(r"propertyPath: m_Name\n\s+value: ([^\n]*)", body)
        wired = {}
        for mm in re.finditer(r"propertyPath: (\S+)\n\s+value: ([^\n]*)\n"
                              r"\s+objectReference: \{fileID: (-?\d+)\}", body):
            field = mm.group(1)
            if BORING.match(field):
                continue
            # An array of models is one decision, not eleven findings.
            field = re.sub(r"\.Array\.data\[\d+\]", "[]", field)
            wired[field] = wired.get(field, False) or mm.group(3) != "0"
        out.append((name.group(1).strip() if name else os.path.basename(src), wired))
    return out


def main(argv):
    wanted = argv or [f for f in sorted(os.listdir(SCENES))
                      if f.endswith(".unity") and f.startswith("Arena")]
    guids = guid_index()
    problems, notes = [], []

    seen = {}
    for name in wanted:
        text = open(os.path.join(SCENES, name), encoding="utf-8", errors="ignore").read()
        seen[name] = fighters(text, guids)

    # What any arena wires is what every arena is expected to wire.
    expected = set()
    for rows in seen.values():
        for _who, wired in rows:
            expected |= {f for f, on in wired.items() if on}

    for name, rows in seen.items():
        notes.append(f"{name}: {len(rows)} fighter(s)")
        if not rows:
            problems.append(f"{name}: no fighters in it at all")
            continue

        holders = 0
        for who, wired in rows:
            have = sorted(f for f, on in wired.items() if on)
            notes.append(f"  {who}: {', '.join(have) if have else 'nothing wired'}")
            if all(wired.get(c) for c in CONTROLS):
                holders += 1
            elif any(wired.get(c) for c in CONTROLS):
                missing = [c for c in CONTROLS if not wired.get(c)]
                problems.append(
                    f"{name}: {who} has some of the touch controls and not others "
                    f"(missing {', '.join(missing)}). Half a control scheme is worse "
                    f"than none: the thumb that works teaches the player the other one "
                    f"is broken.")

        if holders == 0:
            problems.append(
                f"{name}: no fighter has the HUD's controls wired, so the two joysticks "
                f"and three buttons in this scene send their input to nobody. They still "
                f"draw and still move under the thumb, which is why this reads as a "
                f"combat HUD that does not react to anything. Wire "
                f"{', '.join(CONTROLS)} on the TouchInputSource of the local fighter.")
        elif holders > 1:
            problems.append(
                f"{name}: {holders} fighters have the HUD's controls wired, so one thumb "
                f"drives both of them.")

        # And the comparison: what the other arenas wire and this one does not.
        here = set()
        for _who, wired in rows:
            here |= {f for f, on in wired.items() if on}
        for field in sorted(expected - here):
            where = [n for n, r in seen.items()
                     if n != name and any(w.get(field) for _x, w in r)]
            if not where:
                continue
            notes.append(f"  nothing in {name} sets {field}, which "
                         f"{' and '.join(where)} does")

    for n in notes:
        print("  note: " + n if not n.startswith("  ") else "  " + n)
    for p in problems:
        print("  " + p)
    print(f"fighter_check: {len(problems)} problem(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
