"""Does each weapon point where the arm points?

The ask was plain: a fighter should not look like it is GRIPPING a weapon so much as
aiming it, with the steel running on from the arm. PlayerArm already does exactly that --
it turns the hand by the rotation that takes the weapon's own blade axis onto the arm's
line, every frame, overwriting whatever the clip and the socket were doing. So the socket
offsets cannot decide where a weapon points, and tuning them cannot fix one that is wrong.

ONE NUMBER DECIDES IT: the blade axis ModelShape reads off the model. Get that wrong and
the weapon hangs at exactly that error for the whole match, and no inspector field in the
scene will move it.

It was wrong on three of the eleven weapons this game ships, and this file is the
measurement that found it. ModelShape used to take the FURTHEST VERTEX from the grip as
the tip and the line to it as the weapon. True of a sword, whose furthest point is its
point. False of anything with a head: an axe's furthest vertex is the far corner of its
blade, a mace's is a spike on the ball.

So the rule and the check must not share an assumption, or the check only proves the code
agrees with itself. The ground truth here is the HAFT -- the rod the hand actually holds --
found from the model's own shape and nothing else:

  1. Cut the model into slabs along each candidate line, out from the grip.
  2. The grip radius is the radius of the first slab with any vertices in it. That is how
     thick the model is where the hand is.
  3. The haft ends at the first slab that is more than HEAD_RATIO times thicker than that.
     Beyond it is a head: a blade, a ball, a hammer face.
  4. The haft direction is the principal axis of the vertices before that cut.

Step 3 is the only free number, and it was chosen by sweeping it rather than picked. The
haft fit, in degrees off +Y, against HEAD_RATIO:

             dagger club gladius mace sword saber  axe spear scythe flanged hammer
      2x        0.0  0.0     0.0  0.0   0.0  15.5  1.4   0.0   16.7     0.0    0.0
      3x        0.0  0.0     0.0  0.0   0.0   1.8  1.4   0.0   16.7     0.0    0.0
      4x        0.0  0.0     0.0  0.0   0.0   1.8  4.1   0.0   16.7     0.0    0.0
      5x        0.0  0.0     0.0  0.0   0.0   1.8 17.8   0.0   16.7     0.0    0.0

At 2x the saber's guard counts as a head and the fit is left with the pommel; at 5x the
axe's blade counts as haft and drags the fit into it. 3x is the only value nothing moves
at, which is what makes it a measurement rather than a taste.

Two details that are not free at all. Step 2 SCANS instead of sampling because the spear's
shaft is one tube with rings at the two ends only, so the slabs near the grip are empty.
And a slab's size is its THICKNESS across the line, not its distance from it: the scythe's
snath leans, so its distance from the axis grows the whole way up a shaft that is the same
13 mm throughout, and a rule written on distance cut the haft at 0.114 of a 0.653 reach
and then fitted the line to the stub.

What it then asserts, per weapon, in each fighter prefab:

  the drawn line is the haft        -- FAILS past TOLERANCE degrees
      This is the complaint, as a number. 36.7 degrees on the axe is a weapon lying across
      the fist; 2 degrees is a weapon in it.

  the model says which way clearly  -- FAILS when ModelShape would refuse
      A refusal is not a crash: PlayerArm sets the IK weight to zero and the clip keeps the
      wrist. But then the weapon points wherever the animation left it, which is the thing
      being complained about, so it is a failure here.

Run: python3 weapon_grip_check.py
"""

import math
import pathlib
import re
import sys

import fbx_reader

ROOT = pathlib.Path(__file__).resolve().parents[2]
ASSETS = ROOT / "Gulag runners" / "Assets"
FIGHTERS = ["Prefabs/Player 1.prefab", "Prefabs/Player 2.prefab"]

SOCKET_GUID = "3f7c21d4a8b94e6f9c05d2173ae8b640"      # WeaponSocket.cs.meta

# ModelShape's own constants, mirrored. If these drift apart the mirror is lying.
CLEARLY_LONGEST = 1.10
CLEARLY_FLAT = 1.40

# How far the drawn line may sit off the haft. Two degrees on a one metre weapon is two
# centimetres at the tip, which is under the width of the steel.
TOLERANCE = 2.0

# The haft ends where the model first gets this much thicker than it is at the hand.
HEAD_RATIO = 3.0
SLABS = 40

# How many vertices a slab needs before its thickness is believed. A low-poly shaft is a
# tube with a ring at each end, and a stray pair of vertices is not a measurement.
RING = 4


# ----------------------------------------------------------------- the fighter prefabs

def documents(text):
    """{fileID: (classID, body)} for one Unity YAML file."""
    marks = [(m.start(), m.group(1), m.group(2))
             for m in re.finditer(r"^--- !u!(\d+) &(\d+)", text, re.M)]
    out = {}
    for i, (pos, cls, fid) in enumerate(marks):
        end = marks[i + 1][0] if i + 1 < len(marks) else len(text)
        out[fid] = (cls, text[pos:end])
    return out


def socket_children(docs):
    """The weapon models hanging under the WeaponSocket, as (fileID, source guid)."""
    holder = None
    for _fid, (cls, body) in docs.items():
        if cls == "114" and SOCKET_GUID in body:
            holder = re.search(r"m_GameObject: \{fileID: (\d+)\}", body).group(1)
            break
    if holder is None:
        return []

    for _fid, (cls, body) in docs.items():
        if cls != "4" or f"m_GameObject: {{fileID: {holder}}}" not in body:
            continue
        kids = re.findall(r"\{fileID: (\d+)\}",
                          body.split("m_Children:")[1].split("m_Father:")[0])
        out = []
        for k in kids:
            if k not in docs:
                continue
            guid = re.search(r"m_CorrespondingSourceObject: \{fileID: \d+, guid: (\w+)",
                             docs[k][1])
            out.append((k, guid.group(1) if guid else None))
        return out
    return []


def authored_axes(text):
    """[(item, root fileID, bladeAxis, flatAxis)] off PlayerLoadoutView's weapons array."""
    at = text.find("  weapons:")
    if at < 0:
        return []
    end = text.find("  armour:", at)
    block = text[at:end if end > 0 else at + 4000]

    out = []
    for m in re.finditer(
            r"- item: (\d+)\s*\n\s+root: \{fileID: (\d+)\}\s*\n"
            r"\s+bladeAxis: \{x: (\S+), y: (\S+), z: (\S+)\}\s*\n"
            r"\s+flatAxis: \{x: (\S+), y: (\S+), z: (\S+)\}", block):
        out.append((int(m.group(1)), m.group(2),
                    tuple(float(m.group(i)) for i in (3, 4, 5)),
                    tuple(float(m.group(i)) for i in (6, 7, 8))))
    return out


def item_ids():
    """{name: value} straight out of the simulation's own enum."""
    source = (ASSETS / "_Game/Sim/Items/ItemId.cs").read_text()
    return {m.group(1): int(m.group(2))
            for m in re.finditer(r"^\s+(\w+) = (\d+),", source, re.M)}


# The pack's prefab names against the game's item names, as WeaponWiring maps them. Only
# the one that differs is listed; the rest match.
AS_ITEM = {"Hammer": "Warhammer"}


def asset_paths():
    """{guid: path} for every .meta under Assets, built once."""
    index = {}
    for meta in ASSETS.rglob("*.meta"):
        try:
            head = meta.read_text(errors="ignore")[:400]
        except OSError:
            continue
        m = re.search(r"^guid: (\w+)", head, re.M)
        if m:
            index[m.group(1)] = meta.with_suffix("")
    return index


def mesh_of(prefab):
    """The FBX a weapon prefab draws, as a guid."""
    m = re.search(r"m_Mesh: \{fileID: -?\d+, guid: (\w+)", prefab.read_text())
    return m.group(1) if m else None


# -------------------------------------------------------------------- the model itself

def vertices(fbx):
    """Every vertex in the file, in UNITY's space.

    Unity imports a right-handed FBX into a left-handed scene by negating X. These files
    are Y-up (UpAxis 1) with no node rotations, so that is the whole conversion -- checked
    against the prefabs, whose roots are all identity.
    """
    _version, roots = fbx_reader.parse(fbx)
    out = []
    for where, props in fbx_reader.walk(roots):
        if where.endswith("/Geometry/Vertices") and props:
            a = props[0]
            out += [(-a[i], a[i + 1], a[i + 2]) for i in range(0, len(a), 3)]
    return out


def principal(points):
    """The direction the cloud spreads along most, about the origin."""
    m = [[0.0] * 3 for _ in range(3)]
    for p in points:
        for i in range(3):
            for j in range(3):
                m[i][j] += p[i] * p[j]
    v = [0.1, 1.0, 0.1]
    for _ in range(200):
        w = [sum(m[i][j] * v[j] for j in range(3)) for i in range(3)]
        n = math.sqrt(sum(c * c for c in w)) or 1.0
        v = [c / n for c in w]
    return v


def across_from(d):
    """Two unit directions square to d, to measure a cross section in."""
    other = (1.0, 0.0, 0.0) if abs(d[0]) < 0.9 else (0.0, 1.0, 0.0)
    u = (d[1] * other[2] - d[2] * other[1],
         d[2] * other[0] - d[0] * other[2],
         d[0] * other[1] - d[1] * other[0])
    n = math.sqrt(sum(c * c for c in u)) or 1.0
    u = tuple(c / n for c in u)
    v = (d[1] * u[2] - d[2] * u[1],
         d[2] * u[0] - d[0] * u[2],
         d[0] * u[1] - d[1] * u[0])
    return u, v


def haft(points, along=(0.0, 1.0, 0.0)):
    """The rod the hand holds: its direction, and where it stops. None when too sparse.

    `along` only decides which way the slabs are cut, and the answer is taken from the
    vertices inside them -- so a shaft that is not on that axis still comes back at its own
    angle. That is what the scythe is: its snath is 25 degrees off the axis it was authored
    along, and this says so.
    """
    far = max((sum(p[i] * along[i] for i in range(3)) for p in points), default=0.0)
    if far <= 1e-6:
        return None
    step = far / SLABS

    u, v = across_from(along)

    def slab(k):
        """How THICK the model is in this slab -- not how far it is from the axis.

        The difference is the whole scythe. Distance from the axis grows all the way up a
        snath that leans, because the shaft drifts sideways, and a rule written on radius
        read that drift as the shaft getting fatter: it cut the haft at 0.114 of a 0.653
        reach, on a shaft that is the same 13 mm across for its whole length, and fitted
        the line to the stub that was left. Thickness is the diameter across the slab,
        which a lean does not change.
        """
        lo, hi = k * step, (k + 1) * step
        us, vs = [], []
        for p in points:
            t = sum(p[i] * along[i] for i in range(3))
            if lo <= t < hi:
                us.append(sum(p[i] * u[i] for i in range(3)))
                vs.append(sum(p[i] * v[i] for i in range(3)))
        if len(us) < RING:
            return None
        return max(max(us) - min(us), max(vs) - min(vs))

    # How thick the model is where the hand is: the first slab out from the grip that
    # holds a whole ring of vertices. Scanned, not sampled -- see the spear.
    base, first = None, 0
    for k in range(SLABS):
        r = slab(k)
        if r is not None:
            base, first = r, k
            break
    if base is None or base <= 1e-6:
        return None

    cut = far
    for k in range(first + 1, SLABS):
        r = slab(k)
        if r is not None and r > HEAD_RATIO * base:
            cut = k * step
            break

    rod = [p for p in points if sum(p[i] * along[i] for i in range(3)) < cut]
    if len(rod) < 8:
        return None

    d = principal(rod)
    if sum(d[i] * along[i] for i in range(3)) < 0:
        d = [-c for c in d]
    return d, cut


def model_shape(points):
    """ModelShape.Measure, mirrored: the axis of greatest reach from the grip."""
    reach = [max(p[0] for p in points), -min(p[0] for p in points),
             max(p[1] for p in points), -min(p[1] for p in points),
             max(p[2] for p in points), -min(p[2] for p in points)]
    far = max(range(6), key=lambda i: reach[i])
    axis = far >> 1
    across = max(reach[i] for i in range(6) if (i >> 1) != axis)

    if reach[far] <= 0.0 or reach[far] < across * CLEARLY_LONGEST:
        return None

    blade = [0.0, 0.0, 0.0]
    blade[axis] = -1.0 if (far & 1) else 1.0
    return tuple(blade), reach[far], across


def angle(a, b):
    d = sum(a[i] * b[i] for i in range(3))
    na = math.sqrt(sum(c * c for c in a)) or 1.0
    nb = math.sqrt(sum(c * c for c in b)) or 1.0
    return math.degrees(math.acos(max(-1.0, min(1.0, d / (na * nb)))))


# -------------------------------------------------------------------------------- main

def main():
    problems, notes = [], []
    paths = asset_paths()

    for fighter in FIGHTERS:
        path = ASSETS / fighter
        if not path.exists():
            problems.append(f"{fighter}: not there to check")
            continue

        text = path.read_text()
        docs = documents(text)
        kids = socket_children(docs)
        # Keyed BOTH ways on purpose. The reference is the direct answer, and it is the
        # one Player 2 has lost on every row, so the item id is what finds the axis there.
        authored = {root: (blade, flat) for _item, root, blade, flat
                    in authored_axes(text) if root != "0"}
        by_item = {item: (blade, flat) for item, _root, blade, flat
                   in authored_axes(text)}
        ids = item_ids()

        if not kids:
            problems.append(f"{fighter}: no weapon models under the socket")
            continue

        notes.append(f"  note: {fighter}: {len(kids)} weapon(s) in the hand")

        # A row with no model shows nothing at all, which is a louder failure than any
        # angle below. It is reported as its own fault rather than as eleven missing
        # measurements, because the cause is one lost reference per row, not the models:
        # they are right there under the socket.
        rows = authored_axes(text)
        empty = [item for item, root, _b, _f in rows if root == "0"]
        if empty:
            problems.append(
                f"{fighter}: {len(empty)} of {len(rows)} weapon row(s) point at no model, "
                f"so those items are carried invisibly. The models are under the socket -- "
                f"it is the loadout's references that were lost, and every arena currently "
                f"hides it by overriding all of them, so this bites the next scene built "
                f"from this prefab rather than the ones that exist.")

        for root_id, guid in kids:
            prefab = paths.get(guid)
            if prefab is None or not prefab.exists():
                problems.append(f"{fighter}: a weapon's source asset {guid} is missing")
                continue

            name = prefab.stem
            mesh_guid = mesh_of(prefab)
            fbx = paths.get(mesh_guid) if mesh_guid else None
            if fbx is None or not fbx.exists():
                problems.append(f"{fighter}/{name}: no mesh to measure")
                continue

            points = vertices(fbx)
            if not points:
                problems.append(f"{fighter}/{name}: the mesh has no vertices")
                continue

            shape = model_shape(points)
            none = ((0.0, 0.0, 0.0), (0.0, 0.0, 0.0))
            blade, flat = authored.get(
                root_id, by_item.get(ids.get(AS_ITEM.get(name, name)), none))
            hand_authored = sum(c * c for c in blade) > 1e-6

            if hand_authored:
                drawn, source = blade, "authored"
            elif shape is None:
                problems.append(
                    f"{fighter}/{name}: ModelShape would refuse this model -- it reaches "
                    f"no further along one axis than across it, so PlayerArm drops the IK "
                    f"weight and the weapon points wherever the clip left the wrist. Fill "
                    f"in bladeAxis on the loadout's row for it.")
                continue
            else:
                drawn, source = shape[0], "measured"

            rod = haft(points)
            if rod is None:
                notes.append(f"    {name}: too sparse to fit a haft, line not checked")
                continue

            off = angle(drawn, rod[0])
            reach = shape[1] if shape else 0.0
            across = shape[2] if shape else 0.0
            notes.append(
                f"    {name:12s} {source:8s} line ({drawn[0]:+.3f},{drawn[1]:+.3f},"
                f"{drawn[2]:+.3f})  {off:5.1f}d off its haft"
                + (f", reach {reach:.3f} vs {across:.3f} across" if shape else ""))

            if off > TOLERANCE:
                problems.append(
                    f"{fighter}/{name}: the drawn weapon sits {off:.1f} degrees off its "
                    f"own haft. PlayerArm puts the drawn line on the arm, so that angle is "
                    f"what the player sees between the arm and the weapon -- it does not "
                    f"average out and the socket cannot take it back. Fix the measurement, "
                    f"or fill in bladeAxis for this one.")

    for line in notes:
        print(line)
    for line in problems:
        print(f"  {line}")
    print(f"weapon_grip_check: {len(problems)} problem(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
