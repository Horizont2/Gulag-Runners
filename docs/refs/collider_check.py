"""What does the bake make of the ART's own colliders?

The simulation is fed from two places. The SimColliders somebody placed on purpose, which
every check here already reads -- and, with Bake Unity Colliders on, every ordinary Unity
collider in the scene as well. The second half is invisible to the rest of this harness,
because those colliders live inside PREFAB files: the scene only holds an instance and a
list of overrides, so a regular expression over the scene sees nothing at all.

That blind spot cost a day. Both fighters in the Nordic arena were being thrown 21 m into
the air at the start of every round, and the Unity console said exactly why --

    Player 1: spawned inside a baked solid (x -27.33..25.91, y -1.65..21.72)
              and was lifted out to y 21.72

-- while this harness reported a clean bake of 15 solids and called the arena READY. The
box is not in the scene file. It is a MeshCollider on a piece of scenery, and the bake
turns a MeshCollider into its axis-aligned BOUNDS, so a rock modelled around the arena
becomes a single box across the whole of it.

So this resolves them: every prefab instance's world transform, every collider inside the
prefab it came from, nested prefabs included, rotation included, and the mesh bounds read
from the .fbx with the importer's own scale applied. Then it runs the baker's filters over
the result and asks the one question that matters -- is a fighter standing inside one of
these at spawn?

    python3 collider_check.py
"""
import math
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import arena_demo_geometry as geometry                 # noqa: E402
import scale_check                                     # noqa: E402
import fbx_reader                                      # noqa: E402

ASSETS = os.path.join(HERE, "..", "..", "Gulag runners", "Assets")
SCENES = os.path.join(ASSETS, "Scenes")

# A fighter, for the one question this is really asking.
BODY_W, BODY_H, BODY_D = 0.6, 0.91, 0.6

# The two the baker refuses by hand, so this has to refuse them too or it reports the
# fighters' own swords as the walls they are standing inside. SimWorldBaker asks
# GetComponentInParent for each; from a file, the equivalent is "this prefab is one of
# those things", which holds because both live at the root of their own prefab.
PLAYER_SCRIPT = "882fe3a922af38d6658c85cd54734955"
CHEST_SCRIPT = "b133d746e1ad16c0613fc1ce415afb0c"


# ------------------------------------------------------------------ 4x4, the little of it

def trs(pos, rot, scale):
    x, y, z, w = rot
    n = math.sqrt(x * x + y * y + z * z + w * w) or 1.0
    x, y, z, w = x / n, y / n, z / n, w / n
    m = [[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
         [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
         [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]]
    for r in range(3):
        for c in range(3):
            m[r][c] *= scale[c]
    return (m, list(pos))


def mul(a, b):
    """a applied to b: the parent's matrix times the child's."""
    (ma, pa), (mb, pb) = a, b
    m = [[sum(ma[r][k] * mb[k][c] for k in range(3)) for c in range(3)] for r in range(3)]
    p = [pa[r] + sum(ma[r][k] * pb[k] for k in range(3)) for r in range(3)]
    return (m, p)


def point(mat, v):
    m, p = mat
    return [p[r] + sum(m[r][k] * v[k] for k in range(3)) for r in range(3)]


IDENTITY = ([[1, 0, 0], [0, 1, 0], [0, 0, 1]], [0.0, 0.0, 0.0])


# ------------------------------------------------------------------------- reading assets

_GUIDS = None


def guid_index():
    global _GUIDS
    if _GUIDS is None:
        _GUIDS = {}
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
                    _GUIDS[m.group(1)] = os.path.join(base, f[:-5])
    return _GUIDS


_MESH = {}


def mesh_bounds(guid):
    """(lo, hi) of a mesh asset in metres, or None. Keyed by the asset's guid."""
    if guid in _MESH:
        return _MESH[guid]
    path = guid_index().get(guid)
    out = None
    if path and os.path.exists(path) and path.lower().endswith(".fbx"):
        try:
            facts = fbx_reader.describe(path)
            if facts["meshes"]:
                lo = [min(m[0][k] for m in facts["meshes"]) for k in range(3)]
                hi = [max(m[1][k] for m in facts["meshes"]) for k in range(3)]
                # Convert Units and Scale Factor are applied in SERIES by the importer,
                # so they multiply. Picking one is wrong by the other, and the rock that
                # started this sets both: it measured half size, which is the difference
                # between something safely behind the arena and a wall across it.
                factor = 1.0
                meta = path + ".meta"
                if os.path.exists(meta):
                    t = open(meta, encoding="utf-8", errors="ignore").read()
                    use_file = re.search(r"useFileScale: (\d)", t)
                    glob = re.search(r"\n    globalScale: ([\d.]+)", t)
                    if use_file and use_file.group(1) == "1":
                        factor *= (facts["unit"] or 100.0) / 100.0
                    if glob:
                        factor *= float(glob.group(1))
                out = ([v * factor for v in lo], [v * factor for v in hi])
        except Exception:
            out = None
    _MESH[guid] = out
    return out


def documents(text):
    out = {}
    for m in re.finditer(r"--- !u!(\d+) &(-?\d+)( stripped)?\n(\w+):\n(.*?)(?=\n--- !u!|\Z)",
                         text, re.S):
        out[int(m.group(2))] = dict(cls=int(m.group(1)), kind=m.group(4),
                                    stripped=bool(m.group(3)), body=m.group(5))
    return out


def ref(body, name):
    m = re.search(r"%s: \{fileID: (-?\d+)\}" % re.escape(name), body)
    return int(m.group(1)) if m else None


def vec(body, name, default=(0.0, 0.0, 0.0)):
    m = re.search(name + r": \{x: ([-\d.eE+]+), y: ([-\d.eE+]+), z: ([-\d.eE+]+)\}", body)
    return (float(m.group(1)), float(m.group(2)), float(m.group(3))) if m else default


def quat(body, name="m_LocalRotation"):
    m = re.search(name + r": \{x: ([-\d.eE+]+), y: ([-\d.eE+]+), "
                         r"z: ([-\d.eE+]+), w: ([-\d.eE+]+)\}", body)
    return (float(m.group(1)), float(m.group(2)),
            float(m.group(3)), float(m.group(4))) if m else (0.0, 0.0, 0.0, 1.0)


def overrides(body):
    """A PrefabInstance's modifications, as {(target, path): value}."""
    out = {}
    for m in re.finditer(r"- target: \{fileID: (-?\d+)[^\n]*\n\s+propertyPath: (\S+)\n"
                         r"\s+value: ([^\n]*)", body):
        out[(int(m.group(1)), m.group(2))] = m.group(3).strip()
    return out


def local_trs(docs, tfid, mods=None, target=None):
    """A transform's own TRS, with a prefab instance's overrides applied if given."""
    body = docs[tfid]["body"]
    p = list(vec(body, "m_LocalPosition"))
    s = list(vec(body, "m_LocalScale", (1.0, 1.0, 1.0)))
    q = list(quat(body))
    if mods is not None and target is not None:
        for i, a in enumerate("xyz"):
            if (target, "m_LocalPosition." + a) in mods:
                p[i] = float(mods[(target, "m_LocalPosition." + a)])
            if (target, "m_LocalScale." + a) in mods:
                s[i] = float(mods[(target, "m_LocalScale." + a)])
        for i, a in enumerate("xyzw"):
            if (target, "m_LocalRotation." + a) in mods:
                q[i] = float(mods[(target, "m_LocalRotation." + a)])
    return trs(p, q, s)


# ------------------------------------------------------- colliders inside one prefab file

_PREFAB = {}


def prefab_colliders(path, depth=0):
    """Every non-trigger collider in a prefab: (matrix to the root, lo, hi, what, fileID).

    Recurses into prefabs nested inside it, which is not a nicety: the chests in this
    project are a thin wrapper around a model prefab, and both of that model's colliders
    live one level down.
    """
    if path in _PREFAB:
        return _PREFAB[path]
    _PREFAB[path] = []                              # guard against a cycle
    if depth > 4 or not os.path.exists(path):
        return []

    text = open(path, encoding="utf-8", errors="ignore").read()
    docs = documents(text)

    tf_of_go, root = {}, None
    for fid, d in docs.items():
        if d["kind"] in ("Transform", "RectTransform") and not d["stripped"]:
            go = ref(d["body"], "m_GameObject")
            if go is not None:
                tf_of_go[go] = fid
            if ref(d["body"], "m_Father") == 0:
                root = fid

    def to_root(tfid):
        mat = IDENTITY
        seen = set()
        while tfid and tfid in docs and tfid not in seen:
            seen.add(tfid)
            d = docs[tfid]
            if d["stripped"]:
                inst = ref(d["body"], "m_PrefabInstance")
                if inst is None or inst not in docs:
                    break
                ib = docs[inst]["body"]
                src = re.search(r"m_CorrespondingSourceObject: \{fileID: (-?\d+)", d["body"])
                mods = overrides(ib)
                p = [0.0, 0.0, 0.0]
                s = [1.0, 1.0, 1.0]
                q = [0.0, 0.0, 0.0, 1.0]
                t = int(src.group(1)) if src else None
                for i, a in enumerate("xyz"):
                    if (t, "m_LocalPosition." + a) in mods:
                        p[i] = float(mods[(t, "m_LocalPosition." + a)])
                    if (t, "m_LocalScale." + a) in mods:
                        s[i] = float(mods[(t, "m_LocalScale." + a)])
                for i, a in enumerate("xyzw"):
                    if (t, "m_LocalRotation." + a) in mods:
                        q[i] = float(mods[(t, "m_LocalRotation." + a)])
                mat = mul(trs(p, q, s), mat)
                tfid = ref(ib, "m_TransformParent")
            else:
                mat = mul(local_trs(docs, tfid), mat)
                tfid = ref(d["body"], "m_Father")
        return mat

    found = []
    for fid, d in docs.items():
        if d["kind"] not in ("BoxCollider", "MeshCollider", "CapsuleCollider", "SphereCollider"):
            continue
        body = d["body"]
        trig = re.search(r"m_IsTrigger: (\d)", body)
        if trig and trig.group(1) == "1":
            continue
        go = ref(body, "m_GameObject")
        tfid = tf_of_go.get(go)
        if tfid is None:
            continue
        mat = to_root(tfid)

        if d["kind"] == "BoxCollider":
            c = vec(body, "m_Center")
            s = vec(body, "m_Size", (1.0, 1.0, 1.0))
            lo = [c[k] - s[k] / 2 for k in range(3)]
            hi = [c[k] + s[k] / 2 for k in range(3)]
        elif d["kind"] == "MeshCollider":
            m = re.search(r"m_Mesh: \{fileID: (-?\d+), guid: ([0-9a-f]{32})", body)
            if not m:
                # m_Mesh: {fileID: 0}. An empty MeshCollider, which has no geometry and
                # so no bounds -- not something this failed to read.
                continue
            got = mesh_bounds(m.group(2))
            if got is None:
                found.append((mat, None, None, f"{d['kind']} (mesh not readable)", fid))
                continue
            lo, hi = got
        else:
            r = float((re.search(r"m_Radius: ([-\d.eE+]+)", body) or
                       re.match(r"(0.5)", "0.5")).group(1))
            h = float((re.search(r"m_Height: ([-\d.eE+]+)", body) or
                       re.match(r"(2)", "2")).group(1))
            c = vec(body, "m_Center")
            lo = [c[0] - r, c[1] - h / 2, c[2] - r]
            hi = [c[0] + r, c[1] + h / 2, c[2] + r]
        found.append((mat, lo, hi, d["kind"], fid))

    # And whatever hangs off a prefab nested inside this one.
    for m in re.finditer(r"--- !u!1001 &(-?\d+)\nPrefabInstance:\n(.*?)(?=\n--- !u!|\Z)",
                         text, re.S):
        ib = m.group(2)
        g = re.search(r"m_SourcePrefab: \{fileID: \d+, guid: ([0-9a-f]{32})", ib)
        if not g:
            continue
        child = guid_index().get(g.group(1))
        if not child or not child.endswith(".prefab"):
            continue
        mods = overrides(ib)
        roots = {t for (t, p) in mods if p.startswith("m_LocalPosition")}
        p = [0.0, 0.0, 0.0]
        s = [1.0, 1.0, 1.0]
        q = [0.0, 0.0, 0.0, 1.0]
        t = next(iter(roots), None)
        for i, a in enumerate("xyz"):
            if (t, "m_LocalPosition." + a) in mods:
                p[i] = float(mods[(t, "m_LocalPosition." + a)])
            if (t, "m_LocalScale." + a) in mods:
                s[i] = float(mods[(t, "m_LocalScale." + a)])
        for i, a in enumerate("xyzw"):
            if (t, "m_LocalRotation." + a) in mods:
                q[i] = float(mods[(t, "m_LocalRotation." + a)])
        here = trs(p, q, s)
        parent = ref(ib, "m_TransformParent")
        if parent and parent in docs:
            here = mul(to_root(parent), here)
        for cmat, lo, hi, what, cfid in prefab_colliders(child, depth + 1):
            found.append((mul(here, cmat), lo, hi, what, cfid))

    _PREFAB[path] = found
    return found


# --------------------------------------------------------------------------- the scene

def removed_by(body):
    """Source fileIDs this instance has DELETED, from m_RemovedComponents.

    Taking a component off a prefab instance does not edit the prefab: the instance
    records that it is gone. Reading the prefab alone therefore sees a collider that is
    not in the scene, which is how this check first reported a rock whose colliders had
    already been taken off -- and a check that cries wolf is one nobody reads.
    """
    block = re.search(r"m_RemovedComponents:\n((?:\s+-.*\n)*)", body)
    if not block:
        return set()
    return {int(x) for x in re.findall(r"\{fileID: (-?\d+)", block.group(1))}


def disabled_by(body):
    """Source fileIDs this instance has switched OFF, and whether the object itself is."""
    off = set()
    for m in re.finditer(r"- target: \{fileID: (-?\d+)[^\n]*\n\s+propertyPath: m_Enabled\n"
                         r"\s+value: 0", body):
        off.add(int(m.group(1)))
    inactive = re.search(r"propertyPath: m_IsActive\n\s+value: 0", body) is not None
    return off, inactive


def owned_by_fighter_or_chest(docs, fid):
    """Is this instance, or anything above it, a fighter or a chest?"""
    seen = set()
    while fid and fid in docs and fid not in seen:
        seen.add(fid)
        d = docs[fid]
        if d["kind"] == "PrefabInstance":
            g = re.search(r"m_SourcePrefab: \{fileID: \d+, guid: ([0-9a-f]{32})", d["body"])
            src = guid_index().get(g.group(1)) if g else None
            if src and os.path.exists(src):
                head = open(src, encoding="utf-8", errors="ignore").read()
                if PLAYER_SCRIPT in head or CHEST_SCRIPT in head:
                    return True
            fid = ref(d["body"], "m_TransformParent")
        elif d["stripped"]:
            fid = ref(d["body"], "m_PrefabInstance")
        else:
            fid = ref(d["body"], "m_Father")
    return False


def scene_colliders(path):
    """Every non-trigger collider the bake can see, as (name, lo, hi, what)."""
    text = open(path, encoding="utf-8", errors="ignore").read()
    docs = documents(text)

    def world_of_transform(tfid):
        mat = IDENTITY
        seen = set()
        while tfid and tfid in docs and tfid not in seen:
            seen.add(tfid)
            d = docs[tfid]
            if d["stripped"]:
                inst = ref(d["body"], "m_PrefabInstance")
                if inst is None or inst not in docs:
                    break
                ib = docs[inst]["body"]
                mat = mul(instance_trs(docs, inst), mat)
                tfid = ref(ib, "m_TransformParent")
            else:
                mat = mul(local_trs(docs, tfid), mat)
                tfid = ref(d["body"], "m_Father")
        return mat

    def instance_trs(docs, inst):
        ib = docs[inst]["body"]
        mods = overrides(ib)
        targets = {t for (t, p) in mods if p.startswith("m_LocalPosition")}
        t = next(iter(targets), None)
        p = [float(mods.get((t, "m_LocalPosition." + a), 0.0)) for a in "xyz"]
        s = [float(mods.get((t, "m_LocalScale." + a), 1.0)) for a in "xyz"]
        q = [float(mods.get((t, "m_LocalRotation." + a), 0.0)) for a in "xyz"]
        q.append(float(mods.get((t, "m_LocalRotation.w"), 1.0)))
        return trs(p, q, s)

    out = []
    for fid, d in docs.items():
        if d["kind"] != "PrefabInstance":
            continue
        g = re.search(r"m_SourcePrefab: \{fileID: \d+, guid: ([0-9a-f]{32})", d["body"])
        if not g:
            continue
        src = guid_index().get(g.group(1))
        if not src or not src.endswith(".prefab"):
            continue

        # A fighter's own sword and a chest's own model are collision the bake throws
        # away on purpose: one would be a wall its owner stands inside, the other a
        # chest you can never reach because you bump into it. The baker asks
        # GetComponentInParent, so this walks the same way up -- the swords are their
        # own instances hung under the fighter, not part of its prefab.
        if owned_by_fighter_or_chest(docs, fid):
            continue

        name = re.search(r"propertyPath: m_Name\n\s+value: ([^\n]*)", d["body"])
        name = name.group(1).strip() if name else os.path.basename(src)

        gone = removed_by(d["body"])
        off, inactive = disabled_by(d["body"])
        if inactive:
            continue

        here = instance_trs(docs, fid)
        parent = ref(d["body"], "m_TransformParent")
        if parent:
            here = mul(world_of_transform(parent), here)

        for cmat, lo, hi, what, cfid in prefab_colliders(src):
            if cfid in gone or cfid in off:
                continue
            mat = mul(here, cmat)
            if lo is None:
                out.append((name, None, None, what))
                continue
            corners = [point(mat, (lo[0] if i & 1 else hi[0],
                                   lo[1] if i & 2 else hi[1],
                                   lo[2] if i & 4 else hi[2])) for i in range(8)]
            out.append((name,
                        [min(c[k] for c in corners) for k in range(3)],
                        [max(c[k] for c in corners) for k in range(3)],
                        what))
    return out


def main(argv):
    wanted = argv or [f for f in sorted(os.listdir(SCENES))
                      if f.endswith(".unity") and f.startswith("Arena")]
    problems, notes = [], []

    for name in wanted:
        path = os.path.join(SCENES, name)
        scene = geometry.Scene(path)
        spawns = geometry.spawns(scene)

        if scene.baker_field("bakeUnityColliders", int, 0) != 1:
            notes.append(f"{name}: Bake Unity Colliders is off, so the art's own colliders "
                         f"never reach the simulation")
            continue

        plane = scene.baker_field("planeZ", float, 0.0)
        thick = scene.baker_field("planeThickness", float, 3.0)
        behind = scene.baker_field("backdropBehind", float, 0.5)
        restrict = scene.baker_field("restrictToPlane", int, 1)
        drop = scene.baker_field("dropBackdrop", int, 1)

        found = scene_colliders(path)
        unreadable = [f for f in found if f[1] is None]
        baked, skipped = [], 0
        for who, lo, hi, what in found:
            if lo is None:
                continue
            if restrict and (lo[2] > plane + thick or hi[2] < plane - thick):
                skipped += 1
                continue
            if drop and lo[2] > plane + behind:
                skipped += 1
                continue
            baked.append((who, lo, hi, what))

        notes.append(f"{name}: {len(found)} collider(s) on the art; {skipped} filtered out "
                     f"by the plane at z {plane:+.2f}, {len(baked)} reach the simulation"
                     + (f", {len(unreadable)} unreadable" if unreadable else ""))
        for who, lo, hi, what in sorted(baked, key=lambda r: -(r[2][0] - r[1][0])):
            notes.append(f"  {who[:26]:26s} {what:13s} "
                         f"x {lo[0]:+8.2f}..{hi[0]:+8.2f}  y {lo[1]:+7.2f}..{hi[1]:+7.2f}  "
                         f"z {lo[2]:+7.2f}..{hi[2]:+7.2f}")

        for i, (sx, sy, _f, _z) in enumerate(spawns[:2], 1):
            body = ([sx - BODY_W / 2, sy, -1e9], [sx + BODY_W / 2, sy + BODY_H, 1e9])
            for who, lo, hi, what in baked:
                if all(lo[k] < body[1][k] and hi[k] > body[0][k] for k in (0, 1)):
                    problems.append(
                        f"{name}: player {i} spawns at x {sx:+.2f}, y {sy:+.2f} INSIDE "
                        f"{who}'s {what} (x {lo[0]:+.2f}..{hi[0]:+.2f}, "
                        f"y {lo[1]:+.2f}..{hi[1]:+.2f}). The bake turns a collider into "
                        f"its axis-aligned bounds, so this is one solid box across all of "
                        f"that, and the motor answers a body inside a solid by lifting it "
                        f"out of the top — {hi[1]:+.2f} m up. Mark it Ignore with a "
                        f"SimCollider, or take the collider off the art.")

        for who, lo, hi, what in baked:
            if hi[1] - lo[1] > 8.0 or hi[0] - lo[0] > 25.0:
                problems.append(
                    f"{name}: {who}'s {what} bakes to a single box "
                    f"{hi[0] - lo[0]:.1f} x {hi[1] - lo[1]:.1f} m "
                    f"(x {lo[0]:+.2f}..{hi[0]:+.2f}, y {lo[1]:+.2f}..{hi[1]:+.2f}). A mesh "
                    f"becomes its BOUNDS in the simulation, so a shape with a hole in it, "
                    f"or an arch, or a rock the arena sits inside, becomes solid all "
                    f"through. Mark it Ignore unless it is really meant to be a wall.")

    for n in notes:
        print("  note: " + n if not n.startswith("  ") else "  " + n)
    for p in problems:
        print("  " + p)
    print(f"collider_check: {len(problems)} problem(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
