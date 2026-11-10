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

So this measures the real art: the model file a weapon prefab points at -- FBX for the
medieval pack, OBJ for the dungeon one -- through the prefab's own transforms, into the
space the model hangs in the hand in. Then it runs the shipped rule (ModelShape.Measure,
mirrored below) over it and asks what the player would see.

Off the SCENES as well as the fighter prefabs, because the scenes are the version that
runs: every arena overrides all eleven of its fighters' weapon roots, so the list in
Player 1.prefab is a list the game never reads. Two things were hiding behind that. The
sword and the axe are not the pack the prefab names -- all three arenas override them with
the dungeon pack's models. And in the mine those two were authored at a THOUSAND times
their size, which ItemModels divides out at load, so the game looked right while the scene
held a 700-metre sword in a fighter's fist.

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

# ------------------------------------------------------------------ the shipped rule
# ModelShape's own two constants. Mirrored rather than imported because there is no C# here
# to import, and stated as constants rather than inlined so a disagreement is one diff.
CLEARLY_LONGEST = 1.10
CLEARLY_FLAT = 1.40

# ------------------------------------------------------------------ what counts as wrong

# How far the grip-to-tip line may sit from the mesh's own principal axis before the model
# is too odd to grip automatically.
#
# WIDER THAN IT WAS, and the reason is that the two numbers are no longer two estimates of
# one thing. ModelShape used to answer with a FACE OF THE BOX, which is an estimate of the
# haft and could be read against the principal axis as a second opinion. It now answers with
# the line from the grip to the furthest vertex -- which is what the simulation swings, and
# for a head-heavy weapon that line is legitimately off the haft: the pack's axe carries its
# tip 25 degrees to one side of its own principal axis, and drawing it along the arm instead
# would put the steel a quarter of a metre from the hitbox.
#
# So this is no longer "the two rules disagree". It is "this model has its mass somewhere a
# grip cannot guess": past 40 degrees the tip is so far round from the body of the weapon
# that aiming it reads as holding the thing sideways, and the answer is a Blade Axis typed
# on the loadout entry. Over the eleven weapons the game ships the line lands between 0 and
# 25 degrees, and the box answer it replaced was 37 on the scythe.
#
# What catches a genuinely wrong grip now is the rule below it: the steel may not reach past
# the hitbox. That one was 103 to 125 percent on four weapons with the box rule and is 100
# on every one of them with this one.
AXIS_APART = 40.0

# How much haft may stick out BEHIND the hand, as a share of the blade in front of it. A
# spear is held a third up and comes out at 0.5; much past that and the model is gripped
# somewhere near its middle, which after the fit is a pole out of the back of the fist.
BUTT_AT_MOST = 0.60

# And where the furthest corner of the model lands, against the hitbox tip. Not 1.0: an axe
# head's far corner genuinely sticks past the cutting edge, and a scythe's point is not on
# its haft. But a model whose steel stops short of the tip is the fault this file exists for.
TIP_SHORT_OF = 0.75
# 1.05 now, where it was 1.35. The slack was for the box rule, which measured the reach
# along an axis and therefore under-measured any model authored off one -- the fit then
# scaled it up and the steel reached past the tip by a quarter on the axe. The tip IS the
# furthest vertex now, so a weapon that overshoots means the fit and the grip have stopped
# measuring the same thing, which is the fault this file was written for.
TIP_PAST = 1.05


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

def vertices(asset):
    """Every vertex of a model file, in Unity's handedness.

    Two formats, because the two packs this project draws weapons from ship in two: the
    medieval pack as binary FBX and the dungeon pack as Wavefront OBJ. Unity flips Z on
    import for both, which changes nothing measured here -- a reflection keeps angles and
    lengths, and every length below is a share of another length -- but it is applied
    anyway so that an axis named here is the axis somebody would type in the inspector.
    """
    if asset.lower().endswith(".obj"):
        out = []
        for line in read(asset).splitlines():
            if not line.startswith("v "):
                continue
            bits = line.split()
            if len(bits) >= 4:
                out.append((float(bits[1]), float(bits[2]), -float(bits[3])))
        return out

    out = []
    for verts, _tris in fbx_reader.geometry(asset):
        out.extend((v[0], v[1], -v[2]) for v in verts)
    return out


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

        corners = vertices(asset)
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
    """ModelShape.Measure: the tip is the VERTEX furthest from the grip.

    Not a face of the box, which is what this mirrored before and what left two weapons
    crooked in the hand. A box can only answer with one of six directions, so a model
    authored a few degrees off its own axis is gripped along the axis and hangs at that
    angle -- and the reach is then measured along the axis rather than along the weapon,
    short by the cosine, so the fit scales the model up and the steel overshoots the
    hitbox it is supposed to BE.

    The line from the grip to the furthest vertex is what the simulation swings, so
    pointing THAT along the arm puts the drawn steel where the blade is. The haft hangs
    at whatever angle the model was authored with, which is what a real weapon does.
    """
    tip = max(points, key=lambda p: p[0] * p[0] + p[1] * p[1] + p[2] * p[2])
    far = math.sqrt(sum(x * x for x in tip))
    if far <= 1e-6:
        return None

    blade = tuple(x / far for x in tip)

    # A basis across the blade, the same way the C# builds it, so the flat comes out the
    # same side up.
    u = cross(blade, (1.0, 0.0, 0.0) if abs(blade[0]) < 0.9 else (0.0, 1.0, 0.0))
    u = unit(u)
    v = cross(blade, u)

    behind = 0.0
    us, vs = [], []
    for p in points:
        along = dot(p, blade)
        behind = max(behind, -along)
        us.append(dot(p, u))
        vs.append(dot(p, v))

    wide, thin = max(us) - min(us), max(vs) - min(vs)
    across, thick = max(wide, thin), min(wide, thin)

    # And which way it is flat, from the spread rather than from the box: a blade authored
    # at twenty degrees to the axes has a nearly square box, which the old rule called
    # round and left the roll to chance.
    flat = (0.0, 0.0, 0.0)
    if across >= thick * CLEARLY_FLAT:
        n = len(points)
        uu = sum(a * a for a in us) / n
        vv = sum(b * b for b in vs) / n
        uv = sum(a * b for a, b in zip(us, vs)) / n
        half, diff = 0.5 * (uu + vv), 0.5 * (uu - vv)
        r = math.sqrt(diff * diff + uv * uv)
        small = half - r
        if abs(uv) > 1e-18:
            e = unit((small - vv, uv, 0.0))
            flat = unit(tuple(u[i] * e[0] + v[i] * e[1] for i in range(3)))
        else:
            flat = u if uu <= vv else v

    return dict(blade=blade, flat=flat, reach=far, behind=behind,
                across=across, thick=thick, crosswise=across,
                box=[max(p[i] for p in points) - min(p[i] for p in points)
                     for i in range(3)])


def dot(a, b):
    return sum(x * y for x, y in zip(a, b))


def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def unit(a):
    n = math.sqrt(sum(x * x for x in a)) or 1.0
    return tuple(x / n for x in a)


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
    """[(where, [(item name, weapon prefab path, authored scale)])], prefabs and scenes.

    THE SCENES AS WELL, and that is not thoroughness -- it is the only version that runs.
    Every arena overrides all eleven of its fighters' weapon roots, so the list in
    Player 1.prefab is a list the game never reads. Checking the prefab alone measured a
    hand nobody fights with, and it hid the thing that was actually in the mine: the same
    eleven weapons, wired to the same prefabs, authored at a hundred and a thousand times
    the size. ItemModels divides that out at load, so it is invisible in play and absurd
    in the editor -- a 700-metre sword in a fighter's fist, which is a trap rather than a
    bug, and the kind that gets "fixed" by somebody scaling something else to match.
    """
    out = []

    fighter_lists = {}
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

        wired = [(label(item), source_of(text, docs, root), 1.0)
                 for item, root in entries]
        out.append((name[:-7], wired))

        # Kept by guid as well, because a scene's override list is positional: it replaces
        # entry N's root and says nothing about which item entry N is.
        meta = read(path + ".meta")
        guid = re.search(r"guid: ([0-9a-f]{32})", meta)
        if guid:
            fighter_lists[guid.group(1)] = [item for item, _root in entries]

    scenes = os.path.join(ASSETS, "Scenes")
    for name in sorted(os.listdir(scenes)):
        if not name.endswith(".unity"):
            continue
        text = read(os.path.join(scenes, name))
        docs = documents(text)

        for fid, (cls, body, _s) in sorted(docs.items()):
            if cls != 1001:
                continue
            src = re.search(r"m_SourcePrefab: \{fileID: 100100000, guid: ([0-9a-f]{32})",
                            body)
            if src is None or src.group(1) not in fighter_lists:
                continue

            order = fighter_lists[src.group(1)]
            items = {int(i): int(v) for i, v in re.findall(
                r"propertyPath: weapons\.Array\.data\[(\d+)\]\.item\n\s+value: (\d+)", body)}
            roots = {int(i): int(v) for i, v in re.findall(
                r"propertyPath: weapons\.Array\.data\[(\d+)\]\.root\n\s+value:\s*\n"
                r"\s+objectReference: \{fileID: (-?\d+)\}", body)}
            if not roots:
                continue

            wired = []
            for i in sorted(roots):
                item = items.get(i, order[i] if i < len(order) else -1)
                wired.append((label(item), source_of(text, docs, roots[i]),
                              scale_of(text, docs, roots[i])))
            out.append((f"{name} {where_of(body) or 'fighter'}", wired))

    return out


def label(item):
    return next((k for k, v in ITEMS.items() if v == item), str(item))


def where_of(body):
    m = re.search(r"propertyPath: m_Name\n\s+value: (\S.*)", body)
    return m.group(1).strip() if m else None


def owner_of(docs, root):
    """The PrefabInstance a stripped GameObject belongs to."""
    cls_body = docs.get(root)
    if not cls_body:
        return None
    m = re.search(r"m_PrefabInstance: \{fileID: (-?\d+)\}", cls_body[1])
    return int(m.group(1)) if m else None


def source_of(text, docs, root):
    """Which weapon prefab the model under this root actually is."""
    owner = owner_of(docs, root)
    if owner is None:
        return None
    cls_body = docs.get(owner)
    if not cls_body:
        return None
    src = re.search(r"m_SourcePrefab: \{fileID: 100100000, guid: ([0-9a-f]{32})",
                    cls_body[1])
    return GUIDS.get(src.group(1)) if src else None


def scale_of(text, docs, root):
    """What that model is authored at, before the fit touches it."""
    owner = owner_of(docs, root)
    if owner is None:
        return 1.0
    cls_body = docs.get(owner)
    if not cls_body:
        return 1.0
    m = re.search(r"propertyPath: m_LocalScale\.x\n\s+value: ([-\d.eE+]+)", cls_body[1])
    return float(m.group(1)) if m else 1.0


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

    # Every model that is wired into anybody's hand, measured ONCE: the same eleven
    # prefabs are wired in six hands, and eleven lines of numbers said six times is a
    # report nobody reads to the end.
    seen = {}
    for who, wired in found:
        for item, asset, _scale in wired:
            if asset is None:
                problems.append(
                    f"{who}: the model wired for {item} is not a prefab instance this "
                    f"file can follow, so what the player holds cannot be measured.")
                continue
            seen.setdefault((item, asset), set()).add(who)

    notes.append(f"{len(seen)} model(s) in the fighters' hands, measured off their own "
                 f"meshes and sized by the simulation's own blade length")
    for (item, asset), _hands in sorted(seen.items()):
        points = [p for _guid, corners in mesh_boxes(asset) if corners for p in corners]
        if not points:
            problems.append(
                f"{item} is wired to {os.path.basename(asset)}, which has no mesh this "
                f"can read. A weapon with no mesh is a weapon with no length, and the fit "
                f"leaves it at whatever size it shipped at.")
            continue

        grip = measure(points)
        blade = blade_metres(item)

        if grip is None:
            problems.append(
                f"the automatic grip cannot read {item} ({os.path.basename(asset)}): it "
                f"reaches no further along any axis than it does across them, so which way "
                f"it points is a coin toss. Type a Blade Axis on its entry in the "
                f"loadout's Weapons list.")
            continue

        # What the player ends up seeing, all of it as a share of the hitbox, which is the
        # only length that matters: after the fit the blade in front of the hand IS the
        # hitbox, so every other length is a ratio against grip["reach"].
        far = max(math.sqrt(sum(c * c for c in p)) for p in points)
        butt = grip["behind"] / grip["reach"]
        tip = far / grip["reach"]
        apart = angle(grip["blade"], principal(points))
        was = grip["reach"] / max(grip["box"])              # the old fit, for the record

        fat = grip["across"] / max(grip["thick"], 1e-6)
        face = f"flat {fat:.1f}x" if any(grip["flat"]) else f"round {fat:.1f}x"

        if blade is not None:
            pack = os.path.relpath(asset, ASSETS).split(os.sep)[0]
            notes.append(
                f"  {item + ' (' + pack.split()[0] + ')':<24} "
                f"blade {blade * 100:5.1f} cm out of the fist "
                f"(was {blade * was * 100:5.1f}, {was * 100:3.0f}% of it), butt "
                f"{butt * blade * 100:4.1f} cm behind it, steel out to "
                f"{tip * 100:3.0f}% of the tip, {apart:4.1f} deg off its own "
                f"principal axis, {face}")

        if apart > AXIS_APART:
            problems.append(
                f"{item} is read as pointing {apart:.0f} degrees away from its own "
                f"principal axis. Two ways of measuring the same model disagree, so the "
                f"hand is being turned by a guess -- and a wrong one wrenches the wrist "
                f"and drags the whole arm after it. Type a Blade Axis on its loadout "
                f"entry.")
        if butt > BUTT_AT_MOST:
            problems.append(
                f"{item} is gripped with {butt * 100:.0f}% of its length behind the hand. "
                f"The fit sizes the blade in FRONT of the grip, so that is "
                f"{butt * (blade or 0) * 100:.0f} cm of haft out of the back of the fist. "
                f"The model is held somewhere near its middle.")
        if tip < TIP_SHORT_OF:
            problems.append(
                f"{item}'s steel stops at {tip * 100:.0f}% of the hitbox. The player reads "
                f"the distance off the picture and the picture is short, which is the "
                f"whole complaint: they step to just-out-of-range and get hit.")
        if tip > TIP_PAST:
            problems.append(
                f"{item} is drawn {tip * 100:.0f}% as long as the segment that hits, so "
                f"there is steel on the screen that touches nobody.")

    # And every hand has to hold the same set, authored the same way. A scene's weapon list
    # is a positional override of the prefab's, so a hand can quietly end up holding
    # another pack's model, or the same model at a hundred times the size.
    wiring = {}
    for who, wired in found:
        odd = [f"{i} x{s:g}" for i, _a, s in wired if abs(s - 1.0) > 0.001]
        notes.append(f"{who}: {len(wired)} weapon(s) wired"
                     + (f", AUTHORED OVERSIZE: {', '.join(odd)}" if odd else ", all at scale 1"))
        for item, asset, scale in wired:
            if abs(scale - 1.0) > 0.001:
                problems.append(
                    f"{who}: {item} is authored at {scale:g}x the size its own prefab says. "
                    f"ItemModels measures what is drawn and divides that out, so the game "
                    f"looks right and the scene does not: this is a "
                    f"{scale:g}x weapon in a fighter's fist in the editor. Nothing can be "
                    f"lined up by eye against it, and the next person to scale something "
                    f"to match it will be matching a mistake.")
        wiring[who] = {item: (os.path.relpath(a, ASSETS) if a else None)
                       for item, a, _s in wired}

    # Which hands have to agree with which. The two fighters in one arena, always: the same
    # item has to be the same weapon on both sides of the fight. And arena to arena, because
    # an item that is one model here and another there is a weapon that plays differently
    # per map, which no player can learn.
    #
    # The FIGHTER PREFAB is the exception, and a note rather than a problem. A scene's weapon
    # list is a positional override of the prefab's and every arena overrides all eleven, so
    # the prefab's own list is a default nothing reads -- disagreeing with it is what an
    # override is for. Worth saying out loud all the same: it is the list a new arena would
    # start from.
    arenas = [w for w in sorted(wiring) if ".unity" in w]
    bare = [w for w in sorted(wiring) if ".unity" not in w]

    for i, a in enumerate(arenas):
        for b in arenas[i + 1:]:
            same_arena = a.split(".unity")[0] == b.split(".unity")[0]
            for item in sorted(set(wiring[a]) | set(wiring[b])):
                if wiring[a].get(item) == wiring[b].get(item):
                    continue
                problems.append(
                    f"{a} holds {item} as {wiring[a].get(item)} and {b} holds it as "
                    f"{wiring[b].get(item)}. " + (
                        "Two fighters in one arena, so one of them is fighting with "
                        "another weapon than the other -- the fight is not symmetrical."
                        if same_arena else
                        "The same item has to be the same weapon in every arena, or it is "
                        "a different weapon depending on the map."))

    drift = {}
    for arena in arenas:
        prefab = next((p for p in bare if arena.endswith(p)), None)
        if prefab is None:
            continue
        for item in sorted(wiring[prefab]):
            theirs = wiring[arena].get(item)
            if theirs is None or theirs == wiring[prefab][item]:
                continue
            drift.setdefault((item, wiring[prefab][item], theirs), set()).add(arena)

    for (item, mine, theirs), where in sorted(drift.items()):
        notes.append(f"{item}: the fighter prefabs carry {mine} and every arena overrides "
                     f"it with {theirs} ({len(where)} hand(s)). An override is what a scene "
                     f"list is for; the prefab's own entry is the default a new arena would "
                     f"start from, and nothing uses it")

    for n in notes:
        print("  note: " + n if not n.startswith("  ") else "  " + n)
    for p in problems:
        print("  " + p)
    print(f"socket_check: {len(problems)} problem(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
