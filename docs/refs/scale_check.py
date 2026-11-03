"""How big is everything, in metres, next to a fighter?

This exists because a model's import scale is a hidden global. It lives in the .meta
beside the model, every scene that uses that model is authored against whatever it was
at the time, and changing it resizes all of them at once -- silently, with no conflict
and nothing in any scene file to show for it. That has already happened here: seventeen
dungeon models were switched from "the file's units" to "one unit is one metre" to repair
one arena, which made every instance of them a hundred times bigger everywhere else at
the same moment.

So the only honest question is the one a player would ask: how big is that barrel. This
measures it -- the model's own vertices, times the instance's own scale, in metres --
across every scene, and says what is not a plausible size for the thing it is.

It also does the comparison that actually catches an importer changing under somebody:
the same model used in two scenes at sizes that differ by a large factor. One of those
two scenes was authored before the change and one after, and only one of them is right.

    python3 scale_check.py            every scene
    python3 scale_check.py "Demo loc2.unity"

A fighter is 1.8 m. Everything below is judged against that and nothing else.
"""
import collections
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import fbx_reader                                    # noqa: E402

ASSETS = os.path.join(HERE, "..", "..", "Gulag runners", "Assets")
SCENES = os.path.join(ASSETS, "Scenes")

FIGHTER = 1.8

# What a prop may be, in metres, before it is a mistake rather than a prop. Generous on
# purpose: a wall is allowed to be big and a coin is allowed to be small, and the point
# is to catch the factors of ten, not to have an opinion about set dressing.
TOO_SMALL = 0.03
TOO_BIG = 12.0

# Two scenes disagreeing about one model's size by more than this is an importer that
# changed under one of them. A factor of three is far outside anything deliberate.
DISAGREE = 3.0


def obj_bbox(path):
    lo = [1e9] * 3
    hi = [-1e9] * 3
    found = False
    with open(path, encoding="utf-8", errors="ignore") as fh:
        for line in fh:
            if not line.startswith("v "):
                continue
            parts = line.split()
            if len(parts) < 4:
                continue
            try:
                v = [float(parts[1]), float(parts[2]), float(parts[3])]
            except ValueError:
                continue
            found = True
            for k in range(3):
                lo[k] = min(lo[k], v[k])
                hi[k] = max(hi[k], v[k])
    return [hi[k] - lo[k] for k in range(3)] if found else None


def fbx_bbox(path):
    """(whole-file size, {mesh name: size}), both already in metres.

    The per-mesh half is not a refinement, it is the difference between a measurement
    and a coincidence. These packs ship as ONE .fbx holding ninety-seven meshes, so the
    union of the file is the size of the PACK: it reported the sewer ladder and the
    sewer barrel box as the same 5.35 x 14.77 x 8.61 m, which is neither of them.
    """
    try:
        facts = fbx_reader.describe(path)
        per_mesh = fbx_reader.meshes(path)
    except Exception:
        return None
    if not facts["meshes"]:
        return None
    lo = [min(m[0][k] for m in facts["meshes"]) for k in range(3)]
    hi = [max(m[1][k] for m in facts["meshes"]) for k in range(3)]
    size = [hi[k] - lo[k] for k in range(3)]
    named = {name: [box[1][k] - box[0][k] for k in range(3)]
             for name, box in per_mesh if name}

    # The importer's own two levers, read rather than assumed: a file that declares
    # centimetres is divided by a hundred unless somebody has turned that off.
    meta = path + ".meta"
    if os.path.exists(meta):
        text = open(meta, encoding="utf-8", errors="ignore").read()
        use_file = re.search(r"useFileScale: (\d)", text)
        glob = re.search(r"\n    globalScale: ([\d.]+)", text)
        factor = 1.0
        if use_file and use_file.group(1) == "1":
            factor = (facts["unit"] or 100.0) / 100.0
        elif glob:
            factor = float(glob.group(1))
        size = [v * factor for v in size]
        named = {n: [v * factor for v in s] for n, s in named.items()}
    return size, named


def index():
    """guid -> (path, size in metres as imported), for everything measurable."""
    out, sized = {}, {}
    for base, dirs, files in os.walk(ASSETS):
        dirs[:] = [d for d in dirs
                   if d not in ("Library", "Temp") and not d.startswith(".")]
        for f in files:
            if not f.endswith(".meta"):
                continue
            path = os.path.join(base, f)[:-5]
            try:
                head = open(os.path.join(base, f), encoding="utf-8",
                            errors="ignore").read(300)
            except OSError:
                continue
            m = re.search(r"guid: ([0-9a-f]{32})", head)
            if m:
                out[m.group(1)] = path

    for guid, path in out.items():
        low = path.lower()
        if low.endswith(".obj"):
            size = obj_bbox(path)
            sized[guid] = (path, size, {})
        elif low.endswith(".fbx"):
            found = fbx_bbox(path)
            sized[guid] = (path, found[0], found[1]) if found else (path, None, {})

    # Prefabs second, because most of them are a model with a transform on it and the
    # model had to be measured first. Two thirds of a dressed scene is prefabs -- the
    # 220 wall tiles Dawid's arena is built out of are all one -- so leaving them out
    # is leaving out the arena.
    for guid, path in out.items():
        if not path.lower().endswith(".prefab"):
            continue
        size = prefab_bbox(path, sized)
        if size:
            sized[guid] = (path, size, {})
    return out, sized


def prefab_bbox(path, sized):
    """A prefab's size: each mesh it draws, times the scale of the chain above it.

    The chain, not just the nearest scale. A first attempt took the largest
    m_LocalScale anywhere in the file and applied it to the largest mesh, and duly
    reported a barrel box and a ladder as the same fifteen metres -- two different
    props wearing one another's numbers is not a measurement, it is a coincidence
    with units on it.
    """
    try:
        text = open(path, encoding="utf-8", errors="ignore").read()
    except OSError:
        return None

    docs = {}
    for chunk in re.split(r"^--- ", text, flags=re.M)[1:]:
        m = re.match(r"!u!(\d+) &(\d+)", chunk)
        if m:
            docs[int(m.group(2))] = (int(m.group(1)), chunk)

    transforms, of_go = {}, {}
    for fid, (cls, body) in docs.items():
        if cls != 4:
            continue
        s = re.search(r"m_LocalScale: \{x: ([-\d.eE]+), y: ([-\d.eE]+), "
                      r"z: ([-\d.eE]+)\}", body)
        parent = re.search(r"m_Father: \{fileID: (-?\d+)\}", body)
        owner = re.search(r"m_GameObject: \{fileID: (-?\d+)\}", body)
        transforms[fid] = dict(
            scale=[float(s.group(1)), float(s.group(2)), float(s.group(3))]
            if s else [1.0, 1.0, 1.0],
            parent=int(parent.group(1)) if parent else 0)
        if owner:
            of_go[int(owner.group(1))] = fid

    def chain_scale(tfid):
        out = [1.0, 1.0, 1.0]
        seen = set()
        while tfid in transforms and tfid not in seen:
            seen.add(tfid)
            for k in range(3):
                out[k] *= abs(transforms[tfid]["scale"][k])
            tfid = transforms[tfid]["parent"]
        return out

    best = None
    for fid, (cls, body) in docs.items():
        if cls != 33:                                   # MeshFilter
            continue
        mesh = re.search(r"m_Mesh: \{fileID: -?\d+, guid: ([0-9a-f]{32})", body)
        owner = re.search(r"m_GameObject: \{fileID: (-?\d+)\}", body)
        if not mesh or not owner:
            continue
        found = sized.get(mesh.group(1))
        if not found or found[1] is None:
            continue

        # By the name of the object the MeshFilter is on, because a pack .fbx holds
        # every prop in it and the file's own box is the size of the pack.
        label = re.search(r"m_Name: (.*)", docs.get(int(owner.group(1)), (0, ""))[1])
        raw = found[2].get(label.group(1).strip()) if label else None
        if raw is None:
            raw = found[1]

        scale = chain_scale(of_go.get(int(owner.group(1)), 0))
        span = [raw[k] * scale[k] for k in range(3)]
        if best is None or max(span) > max(best):
            best = span
    return best


def instances(scene_path):
    """Each prefab instance: source guid, name and the scale it was placed at."""
    text = open(scene_path, encoding="utf-8", errors="ignore").read()
    for block in re.finditer(r"^--- !u!1001 &\d+\n(.*?)(?=\n--- !u!|\Z)",
                             text, re.M | re.S):
        body = block.group(1)
        guid = re.search(r"m_SourcePrefab: \{fileID: \d+, guid: ([0-9a-f]{32})", body)
        if not guid:
            continue
        name = re.search(r"propertyPath: m_Name\n\s+value: ([^\n]*)", body)

        def axis(a, default=1.0):
            m = re.search(r"propertyPath: m_LocalScale\." + a +
                          r"\n\s+value: ([-\d.eE]+)", body)
            return float(m.group(1)) if m else default

        yield guid.group(1), (name.group(1).strip() if name else "?"), \
            (axis("x"), axis("y"), axis("z"))


def main(argv):
    paths, sized = index()
    wanted = argv or [f for f in sorted(os.listdir(SCENES)) if f.endswith(".unity")]

    problems, notes = [], []
    seen = collections.defaultdict(dict)        # guid -> scene -> biggest dimension

    for scene in wanted:
        path = os.path.join(SCENES, scene)
        if not os.path.exists(path):
            print(f"  no scene called {scene}")
            continue

        measured, unmeasured = 0, 0
        biggest = []
        for guid, name, scale in instances(path):
            if guid not in sized or sized[guid][1] is None:
                unmeasured += 1
                continue
            model, size = sized[guid][0], sized[guid][1]
            world = [size[k] * abs(scale[k]) for k in range(3)]
            measured += 1
            span = max(world)
            biggest.append((span, name, os.path.basename(model), world))

            at = seen[guid].get(scene)
            seen[guid][scene] = max(at, span) if at else span

            if span > TOO_BIG or span < TOO_SMALL:
                problems.append(
                    f"{scene}: {name} ({os.path.basename(model)}) is "
                    f"{world[0]:.2f} x {world[1]:.2f} x {world[2]:.2f} m -- a fighter "
                    f"is {FIGHTER} m")

        biggest.sort(reverse=True)
        notes.append(f"{scene}: {measured} prop(s) measured, {unmeasured} from a prefab "
                     f"or a format this cannot read")
        for span, name, model, world in biggest[:3]:
            notes.append(f"    biggest: {name} ({model}) "
                         f"{world[0]:.2f} x {world[1]:.2f} x {world[2]:.2f} m")

    # The comparison that catches an importer changing under one scene and not another.
    for guid, per_scene in seen.items():
        if len(per_scene) < 2:
            continue
        low = min(per_scene.values())
        high = max(per_scene.values())
        if low > 0 and high / low > DISAGREE:
            model = os.path.basename(sized[guid][0])
            where = ", ".join(f"{s} {v:.2f} m" for s, v in sorted(per_scene.items()))
            problems.append(
                f"{model} is a different size in different scenes: {where}. One of them "
                f"was dressed before its import scale changed and one after, and only "
                f"one of them is right.")

    for n in notes:
        print("  note: " + n if not n.startswith("    ") else "  " + n)
    for p in problems:
        print("  " + p)
    print(f"scale_check: {len(problems)} problem(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
