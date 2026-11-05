"""Does the weapon in the fist agree with the weapon the simulation is swinging?

The hitbox is a segment: from the hand, along the arm, as long as ArmMotor.BladeLength
(docs/31). Nothing about the drawn model is consulted. So the model is a CLAIM about where
that segment is, the player reads the distance off it, and when the claim is wrong the
fight is unplayable in a way no amount of practice fixes -- you step to what looks like
just-out-of-range and get hit.

Two claims, and both of them were wrong on the pack this game ships:

  HOW LONG IT IS.  ItemModels used to scale a weapon so its whole bounding box was the
      blade length -- butt included. Every weapon in the pack is authored held, with some
      of the haft BEHIND the hand, so every weapon came out short: 14% on the sword, 26%
      on the axe, 33% on the spear, whose model is gripped a third of the way up its own
      shaft. A third of a blade is the difference between a trade and a free hit.

  WHICH WAY IT POINTS.  The hand is turned by the rotation that puts the model's measured
      blade axis onto the arm's line, so the measurement had better be the haft. The rule
      that shipped was "the direction from the grip to the centre of the bounds", which is
      exact for a symmetrical weapon and pulled sideways by anything whose head hangs off
      one side: 35 degrees out on the axe, 45 on the scythe. The rule before that was "the
      longest axis of the bounds", which refused to answer for the axe at all -- and a
      refusal left the hand twisted for whatever weapon it held last.

So this measures the real art: the FBX a weapon prefab points at, through the prefab's own
transforms, into the space the model hangs in the hand in. Then it runs the shipped rule
(ModelShape.Measure, mirrored below) over it and asks what the player would see.

Nothing here needs Unity, and that is the point: a weapon pack is a file, the rule is
arithmetic, and the two of them can be put together from the outside.

    python3 socket_check.py
"""
import math
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import arm_mirror                                  # noqa: E402
import fbx_reader                                  # noqa: E402
import fixgeom_mirror as F                         # noqa: E402

ROOT = os.path.join(HERE, "..", "..")
ASSETS = os.path.join(ROOT, "Gulag runners", "Assets")
PREFABS = os.path.join(ASSETS, "Prefabs")

LOADOUT = "f45251f6651c01823ab78d6e297da412"       # PlayerLoadoutView
SOCKET = "3f7c21d4a8b94e6f9c05d2173ae8b640"        # WeaponSocket

# ------------------------------------------------------------------ the shipped rule
# ModelShape's own two constants. Mirrored rather than imported because there is no C# here
# to import, and stated as constants rather than inlined so a disagreement is one diff.
CLEARLY_LONGEST = 1.10
CLEARLY_FLAT = 1.40

# ------------------------------------------------------------------ what counts as wrong

# How far the measured blade axis may sit from the mesh's own principal axis. Two
# independent estimators of the same thing; when they disagree the model is too odd for an
# automatic grip and wants one typed on its list entry.
#
# A THIN LINE, on purpose, and here is the whole of the evidence for where it sits. Over the
# eleven weapons the game ships, the rule in ModelShape lands between 0 and 19.4 degrees of
# the principal axis -- 19.4 being the scythe, whose blade is a quarter-circle and whose
# principal axis is therefore nobody's idea of its haft. The rule it replaced, the direction
# to the centre of the bounds, lands at 23.6 on the axe and 25.3 on the scythe, and both of
# those were visibly crooked in the hand. So the line goes at 22: it passes every weapon in
# the pack and fails both of the answers that were wrong.
#
# Which means a NEW weapon that trips this is not an excuse to widen the rule. It is a model
# whose shape does not say which way it points, and the answer is a Blade Axis on its entry.
AXIS_APART = 22.0

# How much haft may stick out BEHIND the hand, as a share of the blade in front of it. A
# spear is held a third up and comes out at 0.5; much past that and the model is gripped
# somewhere near its middle, which after the fit is a pole out of the back of the fist.
BUTT_AT_MOST = 0.60

# And where the furthest corner of the model lands, against the hitbox tip. Not 1.0: an axe
# head's far corner genuinely sticks past the cutting edge, and a scythe's point is not on
# its haft. But a model whose steel stops short of the tip is the fault this file exists for.
TIP_SHORT_OF = 0.75
TIP_PAST = 1.35


def read(path):
    with open(path, encoding="utf-8", errors="replace") as f:
        return f.read()


def guids():
    """guid -> asset path, for every .meta in the project."""
    out = {}
    for where, _dirs, files in os.walk(ASSETS):
        for name in files:
            if not name.endswith(".meta"):
                continue
            m = re.search(r"guid: ([0-9a-f]{32})", read(os.path.join(where, name)))
            if m:
                out[m.group(1)] = os.path.join(where, name[:-5])
    return out


GUIDS = guids()


def items():
    """ItemId name -> number, off the enum rather than out of anybody's memory."""
    src = read(os.path.join(ASSETS, "_Game", "Sim", "Items", "ItemId.cs"))
    return {m.group(1): int(m.group(2))
            for m in re.finditer(r"^\s+(\w+) = (\d+),", src, re.M)}


ITEMS = items()


def documents(text):
    """fileID -> (class, body) for one YAML file, stripped documents included."""
    out = {}
    for m in re.finditer(r"(?m)^--- !u!(\d+) &(-?\d+)( stripped)?\n(.*?)(?=^--- !u!|\Z)",
                         text, re.S):
        out[int(m.group(2))] = (int(m.group(1)), m.group(4), bool(m.group(3)))
    return out


# ------------------------------------------------------------------ transforms

def quat(x, y, z, w):
    return (x, y, z, w)


def qmul(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return (aw * bx + ax * bw + ay * bz - az * by,
            aw * by - ax * bz + ay * bw + az * bx,
            aw * bz + ax * by - ay * bx + az * bw,
            aw * bw - ax * bx - ay * by - az * bz)


def qrot(q, v):
    x, y, z, w = q
    vx, vy, vz = v
    tx, ty, tz = 2 * (y * vz - z * vy), 2 * (z * vx - x * vz), 2 * (x * vy - y * vx)
    return (vx + w * tx + (y * tz - z * ty),
            vy + w * ty + (z * tx - x * tz),
            vz + w * tz + (x * ty - y * tx))


def numbers(body, field, keys, default):
    m = re.search(r"  %s: \{%s\}" % (re.escape(field),
                                     ", ".join(r"%s: ([-\d.eE+]+)" % k for k in keys)),
                  body)
    return tuple(float(v) for v in m.groups()) if m else default


# ------------------------------------------------------------------ the art

def mesh_boxes(prefab_path):
    """Every mesh in a weapon prefab, as 8 corners in the prefab ROOT's own space.

    Through the prefab's transforms, because a pack is free to hang its mesh off a child
    turned ninety degrees, and measuring the FBX alone would then describe a weapon the
    game does not draw.

    The FBX is read in its own units and its own handedness; Unity's importer flips Z and
    scales uniformly, and neither changes anything measured here -- a reflection preserves
    angles and lengths, and every length below is reported as a SHARE of another length.
    """
    text = read(prefab_path)
    docs = documents(text)

    transforms, filters = {}, []
    for fid, (cls, body, _stripped) in docs.items():
        if cls == 4:
            go = re.search(r"m_GameObject: \{fileID: (-?\d+)\}", body)
            father = re.search(r"m_Father: \{fileID: (-?\d+)\}", body)
            transforms[fid] = dict(
                go=int(go.group(1)) if go else 0,
                father=int(father.group(1)) if father else 0,
                pos=numbers(body, "m_LocalPosition", "xyz", (0.0, 0.0, 0.0)),
                rot=numbers(body, "m_LocalRotation", "xyzw", (0.0, 0.0, 0.0, 1.0)),
                scale=numbers(body, "m_LocalScale", "xyz", (1.0, 1.0, 1.0)))
        elif cls == 33:
            go = re.search(r"m_GameObject: \{fileID: (-?\d+)\}", body)
            mesh = re.search(r"m_Mesh: \{fileID: (-?\d+), guid: ([0-9a-f]{32})", body)
            if go and mesh:
                filters.append((int(go.group(1)), mesh.group(2)))

    by_go = {t["go"]: fid for fid, t in transforms.items()}

    out = []
    for go, guid in filters:
        asset = GUIDS.get(guid)
        if asset is None or not os.path.exists(asset):
            out.append((guid, None))
            continue

        corners = []
        for verts, _tris in fbx_reader.geometry(asset):
            corners.extend((v[0], v[1], -v[2]) for v in verts)      # Unity flips Z
        if not corners:
            out.append((guid, None))
            continue

        # Up the chain to the prefab root, applying each transform as Unity would.
        fid = by_go.get(go)
        while fid in transforms:
            t = transforms[fid]
            corners = [tuple(a + b for a, b in zip(
                qrot(t["rot"], tuple(c * s for c, s in zip(p, t["scale"]))), t["pos"]))
                for p in corners]
            fid = t["father"]

        out.append((guid, corners))
    return out


# ------------------------------------------------------------------ ModelShape, mirrored

def measure(points):
    """ModelShape.Measure, over the model's own points instead of its mesh boxes.

    The same arithmetic: the C# unions each mesh's BOX and measures that, which for a
    single-mesh model is this box exactly. Kept as points so the principal axis -- the
    independent opinion this file checks the rule against -- can be had from the same read.
    """
    lo = [min(p[i] for p in points) for i in range(3)]
    hi = [max(p[i] for p in points) for i in range(3)]
    size = [hi[i] - lo[i] for i in range(3)]

    reach = [hi[0], -lo[0], hi[1], -lo[1], hi[2], -lo[2]]
    far = max(range(6), key=lambda i: reach[i])
    axis = far >> 1
    across = max(reach[i] for i in range(6) if (i >> 1) != axis)

    if reach[far] <= 0 or reach[far] < across * CLEARLY_LONGEST:
        return None

    blade = [0.0, 0.0, 0.0]
    blade[axis] = -1.0 if (far & 1) else 1.0

    a, b = (axis + 1) % 3, (axis + 2) % 3
    thin, wide = (a, b) if size[a] <= size[b] else (b, a)
    flat = [0.0, 0.0, 0.0]
    if size[wide] >= size[thin] * CLEARLY_FLAT:
        flat[thin] = 1.0

    return dict(blade=tuple(blade), flat=tuple(flat),
                reach=reach[far], behind=max(0.0, reach[far ^ 1]),
                across=size[wide], thick=size[thin], crosswise=across,
                box=[size[0], size[1], size[2]])


def principal(points):
    """The mesh's own long axis, about the grip. Jacobi on the second moments."""
    n = len(points)
    m = [[sum(p[i] * p[j] for p in points) / n for j in range(3)] for i in range(3)]
    v = [[1.0 if i == j else 0.0 for j in range(3)] for i in range(3)]
    for _ in range(64):
        p, q, big = 0, 1, 0.0
        for i in range(3):
            for j in range(i + 1, 3):
                if abs(m[i][j]) > big:
                    p, q, big = i, j, abs(m[i][j])
        if big < 1e-14:
            break
        theta = 0.5 * math.atan2(2 * m[p][q], m[q][q] - m[p][p])
        c, s = math.cos(theta), math.sin(theta)
        for k in range(3):
            mkp, mkq = c * m[k][p] - s * m[k][q], s * m[k][p] + c * m[k][q]
            m[k][p], m[k][q] = mkp, mkq
        for k in range(3):
            mpk, mqk = c * m[p][k] - s * m[q][k], s * m[p][k] + c * m[q][k]
            m[p][k], m[q][k] = mpk, mqk
        for k in range(3):
            vkp, vkq = c * v[k][p] - s * v[k][q], s * v[k][p] + c * v[k][q]
            v[k][p], v[k][q] = vkp, vkq
    best = max(range(3), key=lambda i: m[i][i])
    return (v[0][best], v[1][best], v[2][best])


def angle(a, b):
    na = math.sqrt(sum(x * x for x in a)) or 1.0
    nb = math.sqrt(sum(x * x for x in b)) or 1.0
    dot = sum(x * y for x, y in zip(a, b)) / (na * nb)
    return math.degrees(math.acos(max(-1.0, min(1.0, abs(dot)))))


# ------------------------------------------------------------------ the fighters

def fighters():
    """[(prefab name, [(item name, weapon prefab path, model root fileID)])]"""
    out = []
    for name in sorted(os.listdir(PREFABS)):
        if not name.endswith(".prefab"):
            continue
        path = os.path.join(PREFABS, name)
        text = read(path)
        if LOADOUT not in text:
            continue
        docs = documents(text)

        view = None
        for _fid, (cls, body, _s) in docs.items():
            if cls == 114 and LOADOUT in body:
                view = body
                break
        if view is None:
            continue

        listed = re.search(r"\n  weapons:\n(.*?)(?=\n  [a-zA-Z]+:)", view, re.S)
        entries = []
        if listed:
            for m in re.finditer(r"- item: (\d+)\n\s+root: \{fileID: (-?\d+)\}",
                                 listed.group(1)):
                entries.append((int(m.group(1)), int(m.group(2))))

        # Each root is a stripped GameObject belonging to a PrefabInstance; the instance
        # says which prefab it is, and that prefab says which mesh.
        sources = {}
        for m in re.finditer(r"(?m)^--- !u!1001 &(-?\d+)\n(.*?)(?=^--- !u!|\Z)", text, re.S):
            body = m.group(2)
            src = re.search(r"m_SourcePrefab: \{fileID: 100100000, guid: ([0-9a-f]{32})",
                            body)
            if src:
                sources[int(m.group(1))] = src.group(1)

        wired = []
        for item, root in entries:
            cls_body = docs.get(root)
            owner = None
            if cls_body:
                m = re.search(r"m_PrefabInstance: \{fileID: (-?\d+)\}", cls_body[1])
                if m:
                    owner = int(m.group(1))
            guid = sources.get(owner)
            asset = GUIDS.get(guid) if guid else None
            label = next((k for k, v in ITEMS.items() if v == item), str(item))
            wired.append((label, asset, root))

        out.append((name[:-7], wired))
    return out


def the_rule():
    """Which rule the shipped code fits a weapon by. The one regression worth guarding.

    Everything else here measures the art and reasons about it. This reads the source,
    because the fault it is guarding against is a one-word edit with no visible symptom
    short of playing the game: scaling a weapon's whole BOX to the blade length instead of
    the part of it in front of the hand draws every weapon in the pack short, by between
    13 and 33 per cent, and short is unanswerable from the inside -- the player sees a
    weapon that does not reach as far as it hits.
    """
    src = read(os.path.join(ASSETS, "_Game", "Presentation", "ItemModel.cs"))
    weapon = re.search(r"public static void Fit\(GameObject root, ItemId item.*?\n        \}",
                       src, re.S)
    blade = re.search(r"public static void FitBlade\(GameObject root.*?\n        \}", src, re.S)
    if weapon is None or blade is None:
        return "ItemModels has no weapon fit to read"
    if "FitBlade" not in weapon.group(0):
        return ("ItemModels fits a weapon by its whole bounding box again, so the blade is "
                "drawn shorter than the segment that hits")
    if "grip.Reach" not in blade.group(0):
        return "ItemModels.FitBlade no longer scales by the reach out of the grip"
    return None


def blade_metres(item):
    """ArmMotor.BladeLength for one item, in metres, through the simulation's own port."""
    key = item.lower()
    if key not in arm_mirror.WEAPONS:
        return None
    return F.to_f(arm_mirror.Fighter(weapon=key).blade())


def main(argv):
    problems, notes = [], []
    found = fighters()
    if not found:
        print("socket_check: no fighter prefab carries a loadout view.")
        return 1

    broke = the_rule()
    if broke:
        problems.append(
            f"{broke}. Every number below assumes the blade in front of the hand is the "
            f"hitbox, and it no longer is.")

    shape = {}
    for who, wired in found:
        notes.append(f"{who}: {len(wired)} weapon(s) in the hand")
        for item, asset, _root in wired:
            if asset is None:
                problems.append(
                    f"{who}: the model wired for {item} is not a prefab instance this "
                    f"file can follow, so what the player holds cannot be measured.")
                continue

            boxes = mesh_boxes(asset)
            points = [p for _guid, corners in boxes if corners for p in corners]
            if not points:
                problems.append(
                    f"{who}: {item} is wired to {os.path.basename(asset)}, which has no "
                    f"mesh this can read. A weapon with no mesh is a weapon with no "
                    f"length, and the fit leaves it at whatever size it shipped at.")
                continue

            grip = measure(points)
            blade = blade_metres(item)
            shape.setdefault(item, (os.path.basename(asset), grip, blade, points))

            if grip is None:
                problems.append(
                    f"{who}: the automatic grip cannot read {item} "
                    f"({os.path.basename(asset)}): it reaches no further along any axis "
                    f"than it does across them, so which way it points is a coin toss. "
                    f"Type a Blade Axis on its entry in the loadout's Weapons list.")
                continue

            # What the player ends up seeing, all of it as a share of the hitbox, which is
            # the only length that matters: after the fit the blade in front of the hand IS
            # the hitbox, so every other length is a ratio against grip["reach"].
            far = max(math.sqrt(sum(c * c for c in p)) for p in points)
            butt = grip["behind"] / grip["reach"]
            tip = far / grip["reach"]
            apart = angle(grip["blade"], principal(points))
            was = grip["reach"] / max(grip["box"])          # the old fit, for the record

            fat = grip["across"] / max(grip["thick"], 1e-6)
            face = f"flat {fat:.1f}x" if any(grip["flat"]) else f"round {fat:.1f}x"

            if blade is not None:
                notes.append(
                    f"  {item:<12} blade {blade * 100:5.1f} cm out of the fist "
                    f"(was {blade * was * 100:5.1f}, {was * 100:3.0f}% of it), butt "
                    f"{butt * blade * 100:4.1f} cm behind it, steel out to "
                    f"{tip * 100:3.0f}% of the tip, {apart:4.1f} deg off its own "
                    f"principal axis, {face}")

            if apart > AXIS_APART:
                problems.append(
                    f"{who}: {item} is read as pointing {apart:.0f} degrees away from its "
                    f"own principal axis. Two ways of measuring the same model disagree, "
                    f"so the hand is being turned by a guess -- and a wrong one wrenches "
                    f"the wrist and drags the whole arm after it. Type a Blade Axis on "
                    f"its loadout entry.")
            if butt > BUTT_AT_MOST:
                problems.append(
                    f"{who}: {item} is gripped with {butt * 100:.0f}% of its length behind "
                    f"the hand. The fit sizes the blade in FRONT of the grip, so that is "
                    f"{butt * (blade or 0) * 100:.0f} cm of haft out of the back of the "
                    f"fist. The model is held somewhere near its middle.")
            if tip < TIP_SHORT_OF:
                problems.append(
                    f"{who}: {item}'s steel stops at {tip * 100:.0f}% of the hitbox. The "
                    f"player reads the distance off the picture and the picture is short, "
                    f"which is the whole complaint: they step to just-out-of-range and get "
                    f"hit.")
            if tip > TIP_PAST:
                problems.append(
                    f"{who}: {item} is drawn {tip * 100:.0f}% as long as the segment that "
                    f"hits, so there is steel on the screen that touches nobody.")

    # And the two fighters have to be the same fighter. Player 2 is a copy of Player 1, and
    # a copy drifts: one sword wired to another pack's prefab is one player whose weapon
    # sits differently, which is unanswerable from the inside.
    wiring = {}
    for who, wired in found:
        wiring[who] = {item: (os.path.basename(a) if a else None) for item, a, _r in wired}
    names = sorted(wiring)
    for i in range(len(names) - 1):
        a, b = names[i], names[i + 1]
        for item in sorted(set(wiring[a]) | set(wiring[b])):
            if wiring[a].get(item) != wiring[b].get(item):
                problems.append(
                    f"{a} holds {item} as {wiring[a].get(item)} and {b} holds it as "
                    f"{wiring[b].get(item)}. The same item has to be the same model in "
                    f"both hands, or the fight is not symmetrical.")

    for n in notes:
        print("  note: " + n if not n.startswith("  ") else "  " + n)
    for p in problems:
        print("  " + p)
    print(f"socket_check: {len(problems)} problem(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
