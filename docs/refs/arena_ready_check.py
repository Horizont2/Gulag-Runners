"""Is this scene an arena a match can be played in, or is it still a picture?

A dressed location and a playable one look identical in the scene view and are not nearly
the same thing. Arena_Nordic fortress is 165 prefab instances of beautiful fortress, and
it has no simulation, no fighters, no HUD and no floor the simulation can see -- dropping
a match into it would load a scene that counts down and never starts, which is the worst
way for a map pool to fail because it fails for both players at once and blames the net.

So this reads Arena_Demo -- the one arena that is finished -- works out what a match
needs from what IS there, and says what any other scene is missing. It is a readiness
list, not a rule book: the required set comes from the working arena rather than from
somebody's memory of it, so it cannot drift away from the truth.

    python3 arena_ready_check.py                       every arena scene
    python3 arena_ready_check.py "Arena_Nordic fortress.unity"
"""
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.join(HERE, "..", "..", "Gulag runners")
SCENES = os.path.join(PROJECT, "Assets", "Scenes")
GAME = os.path.join(PROJECT, "Assets", "_Game")
BUILD = os.path.join(PROJECT, "ProjectSettings", "EditorBuildSettings.asset")

REFERENCE = "Arena_Demo.unity"

# What a match cannot start without, and how many. Anything else Arena_Demo happens to
# carry is dressing, and a new arena is not held to it.
#
# The counts are the point. One SideViewCamera is a scene somebody half-wired: the second
# player's camera is not optional on a two-phone game, and the symptom is a black half
# screen rather than an error.
REQUIRED = {
    "SimWorldBaker.cs": (1, "nothing bakes the level, so the simulation has no floor "
                            "and both fighters fall out of the world on tick one"),
    "MatchState.cs": (1, "nothing owns the match: no rounds, no clock, no winner"),
    "PlayerController.cs": (2, "a 1v1 needs two fighters in the scene"),
    "SideViewCamera.cs": (2, "one camera per fighter; the second is what the other "
                             "phone looks through"),
    "HudView.cs": (1, "no health, no round, no timer"),
    "MatchRecorder.cs": (1, "the ladder never hears how the match went"),
    "LanLink.cs": (1, "nothing to play a second phone through"),
    "NetMatch.cs": (1, "the clock two devices share"),
    "AudioManager.cs": (1, "no sound at all"),
}

# Not a script: a marked floor. A scene can have every component above and still be
# unplayable because nothing in it is marked as ground.
SIM_COLLIDER = "SimCollider.cs"
MIN_SOLIDS = 1


def guid_index():
    out = {}
    for base, dirs, files in os.walk(GAME):
        dirs[:] = [d for d in dirs if not d.startswith(".")]
        for f in files:
            if not f.endswith(".cs.meta"):
                continue
            m = re.search(r"guid: ([0-9a-f]{32})",
                          open(os.path.join(base, f), encoding="utf-8").read())
            if m:
                out[f[:-5]] = m.group(1)
    return out


def counts(text, guids):
    out = {}
    for name, guid in guids.items():
        n = len(re.findall(r"m_Script: \{fileID: 11500000, guid: " + guid, text))
        if n:
            out[name] = n
    return out


def solids(text, guid):
    """How many SimColliders in this scene are floor rather than scenery."""
    solid = 0
    for chunk in re.split(r"^--- ", text, flags=re.M)[1:]:
        if f"guid: {guid}" not in chunk:
            continue
        kind = re.search(r"^\s+kind: (\d+)", chunk, re.M)
        if kind is None or kind.group(1) in ("0", "1", "2"):
            solid += 1
    return solid


def in_build(name):
    text = open(BUILD, encoding="utf-8").read()
    for m in re.finditer(r"- enabled: (\d)\n\s+path: (.*)\n", text):
        if os.path.basename(m.group(2).strip()) == name:
            return m.group(1) == "1"
    return None


def check(name, guids, reference):
    path = os.path.join(SCENES, name)
    text = open(path, encoding="utf-8", errors="ignore").read()
    have = counts(text, guids)
    problems, notes = [], []

    for script, (want, why) in REQUIRED.items():
        got = have.get(script, 0)
        if got >= want:
            continue
        if script not in reference:
            continue                      # the reference does not have it either
        problems.append(f"{script[:-3]}: {got} of {want} -- {why}")

    guid = guids.get(SIM_COLLIDER)
    marked = solids(text, guid) if guid else 0
    if marked < MIN_SOLIDS:
        problems.append(f"{marked} marked collision box(es): nothing in this scene is "
                        f"floor as far as the simulation is concerned")
    else:
        notes.append(f"{marked} marked collision box(es)")

    build = in_build(name)
    if build is None:
        problems.append("not in the build list, so it loads in the editor and throws "
                        "on the device")
    elif not build:
        problems.append("in the build list but switched off")

    extra = sorted(set(have) - set(REQUIRED) - {SIM_COLLIDER})
    if extra:
        notes.append("also carries " + ", ".join(s[:-3] for s in extra[:10])
                     + (f" and {len(extra) - 10} more" if len(extra) > 10 else ""))

    return problems, notes


def main(argv):
    guids = guid_index()
    reference = counts(open(os.path.join(SCENES, REFERENCE),
                            encoding="utf-8", errors="ignore").read(), guids)

    wanted = argv or [f for f in sorted(os.listdir(SCENES))
                      if f.endswith(".unity") and f.startswith("Arena")]

    total = 0
    for name in wanted:
        if not os.path.exists(os.path.join(SCENES, name)):
            print(f"  no scene called {name}")
            total += 1
            continue
        problems, notes = check(name, guids, reference)
        print(f"  {name}: {'READY' if not problems else str(len(problems)) + ' thing(s) missing'}")
        for n in notes:
            print("    note: " + n)
        for p in problems:
            print("    - " + p)
        total += len(problems)

    print(f"arena_ready_check: {total} problem(s) across {len(wanted)} scene(s)")
    return 1 if total else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
