"""Is the scene's root still readable, and what would tidying it do?

A scene root is a list a person reads. Arena_Demo's had 295 entries in it: thirteen that
mean something and the rest barrels. Nothing is broken by that and nothing ever will be,
which is exactly why it goes unfixed until finding the camera is a thirty-second scroll
and somebody duplicates an object because they could not see the one already there.

So it is measured. The threshold is not a law about tidiness, it is the point past which
the list stops fitting on a screen.

It also projects what Tools > Gulag Runners > Setup > Tidy the hierarchy would do, from
the same rules the tool uses, so the answer to "what will that button do to my scene" is
a command rather than a leap of faith.
"""
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

from arena_demo_geometry import Scene, _docs, _ref, _field, SIM_COLLIDER_GUID

ASSETS = os.path.join(HERE, "..", "..", "Gulag runners", "Assets")
SCENES = os.path.join(ASSETS, "Scenes")
TIDY = os.path.join(ASSETS, "_Game", "Editor", "HierarchyTidy.cs")

# Past this many entries the root stops being a list and becomes a scroll.
COMFORTABLE = 14

# Unity classes worth recognising without resolving a script guid.
CAMERA, LIGHT, MESH_RENDERER, SKINNED, CANVAS = 20, 108, 23, 137, 223


def script_guids():
    """Script name -> guid, for the components the tidy rules name."""
    wanted = ("SimWorldBaker", "MatchState", "AudioManager", "PlayerController",
              "SimCollider")
    out = {}
    for base, dirs, files in os.walk(ASSETS):
        dirs[:] = [d for d in dirs if not d.startswith(".")
                   and d not in ("Library", "Temp")]
        for f in files:
            if not f.endswith(".cs.meta"):
                continue
            name = f[:-8]
            if name not in wanted:
                continue
            with open(os.path.join(base, f), encoding="utf-8", errors="ignore") as fh:
                m = re.search(r"guid: ([0-9a-f]{32})", fh.read(300))
            if m:
                out[name] = m.group(1)
    return out


def groups_from_tool():
    """The group names the tool actually uses, read out of the C#."""
    if not os.path.exists(TIDY):
        return []
    with open(TIDY, encoding="utf-8") as fh:
        source = fh.read()
    names = []
    for const in ("Props", "Systems", "Lighting", "Cameras", "Geometry"):
        m = re.search(r"const string " + const + r' = "([^"]+)";', source)
        if m:
            names.append(m.group(1))
    return names


def survey(path, guids, group_names):
    """Every root in one scene, and which group the tool would file it under."""
    scene = Scene(path)
    docs = _docs(open(path, encoding="utf-8").read())

    # Which components each GameObject owns, by Unity class and by script guid.
    classes, scripts = {}, {}
    for fid, (cls, body) in docs.items():
        owner = _ref(body, "m_GameObject")
        if not owner:
            continue
        classes.setdefault(owner, set()).add(cls)
        if cls == 114:
            m = re.search(r"m_Script: \{fileID: \d+, guid: ([0-9a-f]{32})", body)
            if m:
                scripts.setdefault(owner, set()).add(m.group(1))

    def bucket(go):
        mine = classes.get(go, set())
        script = scripts.get(go, set())
        if CAMERA in mine:
            return "Cameras"
        if LIGHT in mine:
            return "Lighting"
        for name in ("SimWorldBaker", "MatchState", "AudioManager"):
            if guids.get(name) in script:
                return "Systems"
        if CANVAS in mine:
            return "Systems"
        if guids.get("SimCollider") in script or SIM_COLLIDER_GUID in script:
            return "Arena"
        if mine & {MESH_RENDERER, SKINNED}:
            return "Props"
        return "Systems"

    roots, loose = [], []
    for fid, tr in scene.transforms.items():
        if tr["parent"] != 0:
            continue
        go = tr["go"]
        name = scene.gos.get(go, {}).get("name", "")

        # A prefab instance also writes a STRIPPED transform, parented to nothing and
        # owning a GameObject with no name. Counting those as roots doubled every prop
        # in the first version of this check and invented a hundred and sixty objects
        # for the tidy tool to file.
        if not name:
            continue

        roots.append(name)
        if name in group_names:
            continue
        if guids.get("PlayerController") in scripts.get(go, set()):
            continue                      # stays at the root on purpose
        loose.append((name, bucket(go)))

    # Prefab instances keep their components in the prefab, so the scene cannot say what
    # they are. Every one of them in this project is a scenery model.
    for fid, (cls, body) in docs.items():
        if cls != 1001:
            continue
        m = re.search(r"m_TransformParent: \{fileID: (-?\d+)\}", body)
        if m and m.group(1) == "0":
            nm = re.search(r"propertyPath: m_Name\n\s+value: ([^\n]*)", body)
            roots.append(nm.group(1).strip() if nm else "(prefab)")
            loose.append((roots[-1], "Props"))

    return roots, loose


def main():
    problems, notes = [], []
    guids = script_guids()
    group_names = groups_from_tool()

    if not group_names:
        problems.append("HierarchyTidy.cs is missing, so nothing can tidy the root")
        group_names = ["Props", "Systems", "Lighting", "Cameras", "Arena"]

    for name in sorted(os.listdir(SCENES)):
        if not name.endswith(".unity"):
            continue
        path = os.path.join(SCENES, name)
        roots, loose = survey(path, guids, group_names)

        if not loose:
            notes.append(f"{name}: {len(roots)} root(s), nothing loose")
            continue

        counts = {}
        for _n, b in loose:
            counts[b] = counts.get(b, 0) + 1
        where = ", ".join(f"{v} -> {k}" for k, v in sorted(counts.items()))

        if len(roots) > COMFORTABLE:
            problems.append(
                f"{name}: {len(roots)} objects at the root, {len(loose)} of them loose. "
                f"Tools > Gulag Runners > Setup > Tidy the hierarchy would file them: "
                f"{where}")
        else:
            notes.append(f"{name}: {len(roots)} root(s), {len(loose)} loose ({where})")

    for n in notes:
        print("  note: " + n)
    for p in problems:
        print("  " + p)
    print(f"hierarchy_check: {len(problems)} problem(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
