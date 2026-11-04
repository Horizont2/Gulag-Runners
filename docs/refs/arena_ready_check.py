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
sys.path.insert(0, HERE)

import arena_demo_geometry as geometry           # noqa: E402

PROJECT = os.path.join(HERE, "..", "..", "Gulag runners")
SCENES = os.path.join(PROJECT, "Assets", "Scenes")
GAME = os.path.join(PROJECT, "Assets", "_Game")
BUILD = os.path.join(PROJECT, "ProjectSettings", "EditorBuildSettings.asset")

REFERENCE = "Arena_Forest.unity"

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
    """How many of each script this scene has, components AND prefab instances.

    The second half matters more than it sounds. The moment the fighters became a
    prefab -- which is the right way round, two arenas should not each carry their own
    copy of a fighter -- their PlayerController stopped being written into the scene
    file, and this reported "PlayerController: 0 of 2, a 1v1 needs two fighters" at a
    scene with two fighters standing in it. A readiness list that cannot see the thing
    it is asking about is worse than no list: it sends somebody to fix what is not
    broken.

    An instance is counted by the NAME it was given, against the names the reference
    arena's own instances use, because a prefab's contents are in another file and the
    scene only records what it was called and what it was changed from.
    """
    out = {}
    for name, guid in guids.items():
        n = len(re.findall(r"m_Script: \{fileID: 11500000, guid: " + guid, text))
        if n:
            out[name] = n

    # Prefab instances, by the name on the instance. "Player 1" and "Player 2" are
    # fighters wherever they turn up; nothing else in these scenes is called that.
    fighters = len(re.findall(r"propertyPath: m_Name\n\s+value: Player \d+\s*\n", text))
    if fighters:
        out["PlayerController.cs"] = max(out.get("PlayerController.cs", 0), fighters)
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


def wired(name, text, guids, problems, notes):
    """Is the arena's NetMatch pointed at this arena's own things?

    Four references -- the camera, the HUD, the result panel and the match recorder --
    and every one of them was empty in all three arenas. NetMatch then falls back to
    FindFirstObjectByType in Awake, and Unity does not define which object that returns.

    It matters most for the camera. An arena carries one per fighter, stacked half a
    screen each, because the first way two people played this was on one phone; the
    moment there are two phones, NetMatch keeps the local player's camera and switches
    the other off. "The local player's" is whichever one that lottery handed back, and
    the two are not interchangeable -- each is built around its own fighter. The symptom
    is a player watching the other man's half of the screen, or both halves, on one arena
    and not on another, with nothing different between them to point at.

    So the references are a wiring fault like any other, and this is the list that says
    an arena is ready.
    """
    net = guids.get("NetMatch.cs")
    if not net:
        return
    for m in re.finditer(r"--- !u!114 &(-?\d+)\nMonoBehaviour:\n(.*?)(?=\n--- !u!|\Z)",
                         text, re.S):
        if net not in m.group(2):
            continue
        empty = [f for f in ("view", "hud", "result", "recorder")
                 if re.search(r"^  %s: \{fileID: 0\}$" % f, m.group(2), re.M)]
        if empty:
            problems.append(
                f"NetMatch has {', '.join(empty)} left empty, so Awake takes whatever "
                f"FindFirstObjectByType hands back -- an order Unity does not define. "
                f"With two phones it decides which camera the player keeps and which is "
                f"switched off, and this arena's two are not interchangeable.")
        else:
            notes.append("NetMatch is wired to this arena's own camera, HUD, result "
                         "panel and recorder")

    # One camera per fighter, and no two on the same one.
    cam = guids.get("SideViewCamera.cs")
    if not cam:
        return
    follows = []
    for m in re.finditer(r"--- !u!114 &(-?\d+)\nMonoBehaviour:\n(.*?)(?=\n--- !u!|\Z)",
                         text, re.S):
        if cam not in m.group(2):
            continue
        p = re.search(r"^  player: \{fileID: (-?\d+)\}$", m.group(2), re.M)
        follows.append(int(p.group(1)) if p else 0)
    if follows and len(set(follows)) != len(follows):
        problems.append(
            f"{len(follows)} side-view cameras and only {len(set(follows))} fighter(s) "
            f"between them, so one fighter has no camera of their own and the player "
            f"who is dealt them watches somebody else's fight.")
    if 0 in follows:
        problems.append("a side-view camera with no fighter on it: it will sit where it "
                        "was left and follow nothing.")


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

    wired(name, text, guids, problems, notes)

    extra = sorted(set(have) - set(REQUIRED) - {SIM_COLLIDER})
    if extra:
        notes.append("also carries " + ", ".join(s[:-3] for s in extra[:10])
                     + (f" and {len(extra) - 10} more" if len(extra) > 10 else ""))

    return problems, notes


def main(argv):
    guids = guid_index()
    reference = counts(open(os.path.join(SCENES, REFERENCE),
                            encoding="utf-8", errors="ignore").read(), guids)

    wanted = argv or geometry.arena_scenes()

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
