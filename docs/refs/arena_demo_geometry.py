"""Bakes Arena_Demo.unity the way SimWorldBaker does, so the harness measures the real level.

The location in Arena_Demo was built in 3D by hand. The simulation is flat, so what the fight
actually happens on is not the geometry in the scene view — it is the set of boxes the bake
produces from it: scenery culled by depth, anything marked Ignore dropped, and a hatch cut
through every solid a ladder passes through. Reading the scene file and doing exactly that is
the only way to test the level we ship rather than a description of it.

Mirrors: SimWorldBaker.Bake, SimWorldBaker.CutHatches, SimCollider.WorldRect.
"""
import math
import os
import re

from sim_motor_mirror import X

SCENE = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                     "..", "..", "Gulag runners", "Assets", "Scenes", "Arena_Demo.unity")

SIM_COLLIDER_GUID = "a92ea500d0103a36b3143c8a534b18b2"
CHEST_GUID = "b133d746e1ad16c0613fc1ce415afb0c"
PLAYER_GUID = "882fe3a922af38d6658c85cd54734955"
BAKER_GUID = "dbc7640cd37666d57e8cc1de33f92772"

SOLID, ONEWAY, LADDER, IGNORE = 0, 1, 2, 3


# ---------------------------------------------------------------- the scene file

def _docs(text):
    out = {}
    for m in re.finditer(r'^--- !u!(\d+) &(\d+)(?: stripped)?\n', text, re.M):
        nxt = text.find('\n--- !u!', m.end())
        out[int(m.group(2))] = (int(m.group(1)),
                                text[m.end(): nxt if nxt != -1 else len(text)])
    return out


def _vec(body, name, default=None):
    m = re.search(r'^\s{2}' + re.escape(name) + r':\s*\{(.*?)\}', body, re.M | re.S)
    if not m:
        return default
    d = {}
    for kv in m.group(1).split(','):
        k, _, v = kv.partition(':')
        d[k.strip()] = float(v)
    return d


def _ref(body, name):
    m = re.search(r'^\s{2}' + re.escape(name) + r':\s*\{fileID: (-?\d+)', body, re.M)
    return int(m.group(1)) if m else 0


def _field(body, name, cast=float, default=None):
    m = re.search(r'^\s+' + re.escape(name) + r': (\S+)\s*$', body, re.M)
    return cast(m.group(1)) if m else default


def _qmat(q):
    x, y, z, w = q
    n = math.sqrt(x * x + y * y + z * z + w * w) or 1.0
    x, y, z, w = x / n, y / n, z / n, w / n
    return [[1 - 2 * (y * y + z * z), 2 * (x * y - w * z), 2 * (x * z + w * y)],
            [2 * (x * y + w * z), 1 - 2 * (x * x + z * z), 2 * (y * z - w * x)],
            [2 * (x * z - w * y), 2 * (y * z + w * x), 1 - 2 * (x * x + y * y)]]


def _mul(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(3)) for j in range(3)] for i in range(3)]


def _apply(m, v):
    return [sum(m[i][k] * v[k] for k in range(3)) for i in range(3)]


_PREFABS = {}


def _prefab(guid):
    """The one chest prefab behind a guid: its root transform, reach box and Chest fields."""
    if guid in _PREFABS:
        return _PREFABS[guid]
    root = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                        "..", "..", "Gulag runners", "Assets")
    path = None
    for base, _dirs, files in os.walk(root):
        for f in files:
            if f.endswith('.prefab.meta'):
                if f'guid: {guid}' in open(os.path.join(base, f), encoding='utf-8',
                                           errors='ignore').read():
                    path = os.path.join(base, f[:-5])
                    break
        if path:
            break
    if path is None:
        _PREFABS[guid] = None
        return None
    text = open(path, encoding='utf-8').read()
    docs = _docs(text)
    chest = next((b for cls, b in docs.values()
                  if cls == 114 and f'guid: {CHEST_GUID}' in b), None)
    if chest is None:
        _PREFABS[guid] = dict(text=text)        # some other prefab; not a chest
        return _PREFABS[guid]
    owner = _ref(chest, 'm_GameObject')
    box = next(b for cls, b in docs.values()
               if cls == 65 and _ref(b, 'm_GameObject') == owner)
    tr = next(b for cls, b in docs.values()
              if cls == 4 and _ref(b, 'm_GameObject') == owner)
    go = next(b for fid, (cls, b) in docs.items() if cls == 1 and fid == owner)
    _PREFABS[guid] = dict(
        text=text,
        pos=_vec(tr, 'm_LocalPosition', {'x': 0, 'y': 0, 'z': 0}),
        scale=_vec(tr, 'm_LocalScale', {'x': 1, 'y': 1, 'z': 1}),
        centre=_vec(box, 'm_Center', {'x': 0, 'y': 0, 'z': 0}),
        size=_vec(box, 'm_Size', {'x': 1, 'y': 1, 'z': 1}),
        kind=_field(chest, 'kind', int, 0),
        contents=_field(chest, 'contents', int, 0),
        name=_field(go, 'm_Name', str, 'chest'))
    return _PREFABS[guid]


class Scene:
    """Just enough of Unity's transform hierarchy to reproduce Collider.bounds."""

    def __init__(self, path=SCENE):
        self.docs = _docs(open(path, encoding='utf-8').read())
        self.transforms, self.gos = {}, {}
        for fid, (cls, body) in self.docs.items():
            if cls == 4:
                self.transforms[fid] = dict(
                    go=_ref(body, 'm_GameObject'),
                    pos=_vec(body, 'm_LocalPosition', {'x': 0, 'y': 0, 'z': 0}),
                    rot=_vec(body, 'm_LocalRotation', {'x': 0, 'y': 0, 'z': 0, 'w': 1}),
                    scale=_vec(body, 'm_LocalScale', {'x': 1, 'y': 1, 'z': 1}),
                    parent=_ref(body, 'm_Father'))
            elif cls == 1:
                comps = []
                lst = re.search(r'  m_Component:\n((?:  - component: \{fileID: -?\d+\}\n)*)', body)
                if lst and lst.group(1):
                    comps = [int(v) for v in re.findall(r'fileID: (-?\d+)', lst.group(1))]
                self.gos[fid] = dict(name=_field(body, 'm_Name', str, ''),
                                     active=_field(body, 'm_IsActive', int, 1),
                                     comps=comps)
        self.tr_of_go = {v['go']: k for k, v in self.transforms.items() if v['go']}

    # --- transforms

    def basis(self, tfid):
        chain = []
        cur = tfid
        while cur in self.transforms:
            chain.append(self.transforms[cur])
            cur = self.transforms[cur]['parent']
        origin, basis = [0.0, 0.0, 0.0], [[1, 0, 0], [0, 1, 0], [0, 0, 1]]
        for tr in reversed(chain):
            p, s = tr['pos'], tr['scale']
            lp = _apply(basis, [p['x'], p['y'], p['z']])
            origin = [origin[i] + lp[i] for i in range(3)]
            basis = _mul(basis, _qmat((tr['rot']['x'], tr['rot']['y'],
                                      tr['rot']['z'], tr['rot'].get('w', 1))))
            basis = [[basis[i][0] * s['x'], basis[i][1] * s['y'], basis[i][2] * s['z']]
                     for i in range(3)]
        return origin, basis

    def active(self, go):
        """A GameObject is only in the bake if nothing above it is switched off."""
        cur = self.tr_of_go.get(go)
        while cur in self.transforms:
            if not self.gos.get(self.transforms[cur]['go'], {}).get('active', 1):
                return False
            cur = self.transforms[cur]['parent']
        return True

    # --- prefab instances

    def prefab_chests(self):
        """The chests, which are prefab instances: the component lives in the prefab file.

        SimWorldBaker finds them with GetComponentsInChildren, which does not care whether an
        object came from a prefab. A harness that only reads the scene file does, so it has to
        open the prefab and apply the instance's overrides itself — otherwise the level tests
        green with no loot in it at all.
        """
        out = []
        for fid, (cls, body) in sorted(self.docs.items()):
            if cls != 1001:
                continue
            src = re.search(r'm_SourcePrefab: \{fileID: \d+, guid: (\w+)', body)
            if not src:
                continue
            prefab = _prefab(src.group(1))
            if prefab is None or CHEST_GUID not in prefab['text']:
                continue
            mods = dict(re.findall(r'propertyPath: (\S+)\n      value: (\S*)', body))
            pos = [float(mods.get('m_LocalPosition.' + a, prefab['pos'][a])) for a in 'xyz']
            scale = [float(mods.get('m_LocalScale.' + a, prefab['scale'][a])) for a in 'xyz']
            origin, basis = self.basis(_ref(body, 'm_TransformParent'))
            wp = [origin[i] + _apply(basis, pos)[i] for i in range(3)]
            centre, size = prefab['centre'], prefab['size']
            half = [size['x'] / 2 * scale[0], size['y'] / 2 * scale[1], size['z'] / 2 * scale[2]]
            c = [wp[i] + _apply(basis, [centre['x'] * scale[0], centre['y'] * scale[1],
                                        centre['z'] * scale[2]])[i] for i in range(3)]
            ext = [sum(abs(basis[i][k]) * half[k] for k in range(3)) for i in range(3)]
            lo = [c[i] - ext[i] for i in range(3)]
            hi = [c[i] + ext[i] for i in range(3)]
            out.append((_aabb(lo, hi),
                        int(mods.get('kind', prefab['kind'])),
                        int(mods.get('contents', prefab['contents'])),
                        mods.get('m_Name', prefab['name'])))
        return out

    # --- colliders

    def bounds(self, go):
        """World-space Collider.bounds of the first collider on this object, or None."""
        for c in self.gos[go]['comps']:
            cls, body = self.docs.get(c, (0, ''))
            if cls == 65:                                   # BoxCollider
                centre = _vec(body, 'm_Center', {'x': 0, 'y': 0, 'z': 0})
                size = _vec(body, 'm_Size', {'x': 1, 'y': 1, 'z': 1})
                half = [size['x'] / 2, size['y'] / 2, size['z'] / 2]
            elif cls == 136:                                # CapsuleCollider
                centre = _vec(body, 'm_Center', {'x': 0, 'y': 0, 'z': 0})
                r = _field(body, 'm_Radius', float, 0.5)
                h = _field(body, 'm_Height', float, 2.0)
                axis = _field(body, 'm_Direction', int, 1)
                half = [r, r, r]
                half[axis] = max(r, h / 2)
            else:
                continue
            origin, basis = self.basis(self.tr_of_go[go])
            lc = _apply(basis, [centre['x'], centre['y'], centre['z']])
            wc = [origin[i] + lc[i] for i in range(3)]
            ext = [sum(abs(basis[i][k]) * half[k] for k in range(3)) for i in range(3)]
            return [wc[i] - ext[i] for i in range(3)], [wc[i] + ext[i] for i in range(3)]

        # No collider at all: SimCollider.RotatedBounds takes the box from the transform,
        # which is right for the unit cubes this location is built from.
        if self.script(go, SIM_COLLIDER_GUID) is not None:
            origin, basis = self.basis(self.tr_of_go[go])
            half = [0.5, 0.5, 0.5]
            ext = [sum(abs(basis[i][k]) * half[k] for k in range(3)) for i in range(3)]
            return [origin[i] - ext[i] for i in range(3)], [origin[i] + ext[i] for i in range(3)]
        return None

    def _unused(self, go, centre, size, half, basis, origin):
        if True:
            lc = _apply(basis, [centre['x'], centre['y'], centre['z']])
            wc = [origin[i] + lc[i] for i in range(3)]
            ext = [sum(abs(basis[i][k]) * half[k] for k in range(3)) for i in range(3)]
            return [wc[i] - ext[i] for i in range(3)], [wc[i] + ext[i] for i in range(3)]
        return None

    def renders(self, go):
        """Does this object actually draw anything. An invisible collider cannot occlude."""
        return any(self.docs.get(c, (0, ''))[0] in (23, 137)       # Mesh/SkinnedMeshRenderer
                   for c in self.gos[go]['comps'])

    def occluders(self, plane_z):
        """Everything drawn between the camera and the gameplay plane, nearest first.

        SideViewCamera stands at planeZ - distance and looks along +Z, so anything with a
        smaller Z than the plane is between it and the fight. In a level modelled in 3D that
        set is the whole answer to "why can I not see my own fighter".
        """
        out = []
        for fid in self.gos:
            if fid not in self.tr_of_go or not self.renders(fid):
                continue
            b = self.bounds(fid)
            if b is None or b[0][2] >= plane_z:
                continue
            out.append((self.gos[fid]['name'], b[0], b[1]))
        out.sort(key=lambda r: r[1][2])
        return out

    def trigger(self, go):
        for c in self.gos[go]['comps']:
            cls, body = self.docs.get(c, (0, ''))
            if cls in (65, 136) and _field(body, 'm_IsTrigger', int, 0):
                return True
        return False

    def script(self, go, guid):
        for c in self.gos[go]['comps']:
            cls, body = self.docs.get(c, (0, ''))
            if cls == 114 and f'guid: {guid}' in body and _field(body, 'm_Enabled', int, 1):
                return body
        return None

    def ancestor_has(self, go, guid):
        cur = self.tr_of_go.get(go)
        while cur in self.transforms:
            owner = self.transforms[cur]['go']
            if owner in self.gos and self.script(owner, guid) is not None:
                return True
            cur = self.transforms[cur]['parent']
        return False


# ---------------------------------------------------------------- the bake

def _aabb(lo, hi):
    return (X(lo[0]), X(lo[1]), X(hi[0]), X(hi[1]))


def cut_hatches(solids, ladders, margin, min_overlap):
    """SimWorldBaker.CutHatches: a ladder opens a hole through anything it runs through.

    A solid the ladder only stands on keeps its floor: the ladder has to reach min_overlap into
    it first. Vlad's right-hand ladder is modelled one centimetre inside its own platform, and
    without that test the fighter who spawns there drops through a 0.78 m hole.
    """
    m = X(margin)
    tol = X(min_overlap)
    sliver = X(0.05)
    cut = 0
    for lad in ladders:
        working = []
        for s in solids:
            lo, hi = lad[0] - m, lad[2] + m
            overlap_y = min(s[3], lad[3]) - max(s[1], lad[1])
            if not (s[0] < hi and s[2] > lo) or overlap_y <= tol:
                working.append(s)
                continue
            cut += 1
            if lo - s[0] > sliver:
                working.append((s[0], s[1], lo, s[3]))
            if s[2] - hi > sliver:
                working.append((hi, s[1], s[2], s[3]))
        solids = working
    return solids, cut


def bake(scene=None, report=False):
    sc = scene or Scene()
    baker = next((b for fid, (cls, b) in sc.docs.items()
                  if cls == 114 and f'guid: {BAKER_GUID}' in b), None)
    if baker is None:
        raise SystemExit('Arena_Demo has no SimWorldBaker')
    plane_z = _field(baker, 'planeZ', float, 0.0)
    thickness = _field(baker, 'planeThickness', float, 1.5)
    restrict = _field(baker, 'restrictToPlane', int, 1)
    cuts_hatch = _field(baker, 'ladderCutsHatch', int, 0)
    filters_marked = _field(baker, 'planeFiltersMarked', int, 1)
    margin = _field(baker, 'hatchMargin', float, 0.1)
    min_overlap = _field(baker, 'hatchMinOverlap', float, 0.05)

    solids, oneway, ladders, chests = [], [], [], []
    marked, ignored, off_plane, triggers, on_player, on_chest = 0, 0, 0, 0, 0, 0
    handled = set()

    # 1. SimCollider wins, and is never depth-culled: it is an explicit decision.
    for go in sorted(sc.gos):
        body = sc.script(go, SIM_COLLIDER_GUID)
        if body is None or not sc.active(go):
            continue
        marked += 1
        handled.add(go)
        kind = _field(body, 'kind', int, 0)
        if kind == IGNORE:
            ignored += 1
            continue
        b = sc.bounds(go)
        if b is None:
            continue
        # A marker says WHAT a box is, not that it is on the gameplay plane.
        if filters_marked and restrict and not (b[1][2] >= plane_z - thickness
                                                and b[0][2] <= plane_z + thickness):
            off_plane += 1
            continue
        (solids if kind == SOLID else oneway if kind == ONEWAY else ladders).append(_aabb(*b))

    # 2. Chests, before the ordinary colliders, so a chest never becomes a wall.
    for go in sorted(sc.gos):
        body = sc.script(go, CHEST_GUID)
        if body is None or not sc.active(go):
            continue
        handled.add(go)
        b = sc.bounds(go)
        if b is not None:
            chests.append((_aabb(*b), _field(body, 'kind', int, 0),
                           _field(body, 'contents', int, 0), sc.gos[go]['name']))
    chests += sc.prefab_chests()

    # 3. Then every other collider in the scene.
    for go in sorted(sc.gos):
        if go in handled or not sc.active(go):
            continue
        b = sc.bounds(go)
        if b is None:
            continue
        if sc.trigger(go):
            triggers += 1
            continue
        if sc.ancestor_has(go, PLAYER_GUID):
            on_player += 1
            continue
        if sc.ancestor_has(go, CHEST_GUID):
            on_chest += 1
            continue
        lo, hi = b
        if restrict and not (hi[2] >= plane_z - thickness and lo[2] <= plane_z + thickness):
            off_plane += 1
            continue
        solids.append(_aabb(lo, hi))

    hatches = 0
    if cuts_hatch:
        solids, hatches = cut_hatches(solids, ladders, margin, min_overlap)

    if report:
        print(f"bake: plane z {plane_z} +-{thickness} -> {len(solids)} solid "
              f"({hatches} cut by ladders), {len(oneway)} one-way, {len(ladders)} ladder, "
              f"{len(chests)} chest; {marked} marked ({ignored} ignored), "
              f"{off_plane} off the plane, {triggers} trigger, "
              f"{on_player} on a fighter, {on_chest} on a chest")

    return {"solid": solids, "oneway": oneway, "ladder": ladders, "chest": chests}


def world(report=False):
    b = bake(report=report)
    return {k: b[k] for k in ("solid", "oneway", "ladder")}


CAMERA_GUID = "b921a9be9303a1887f282de341a5ae31"


def cameras(scene=None):
    """Every SideViewCamera in the scene, as the numbers that decide what it frames."""
    sc = scene or Scene()
    out = []
    for fid, (cls, body) in sorted(sc.docs.items()):
        if cls != 114 or f'guid: {CAMERA_GUID}' not in body:
            continue
        lo = _vec(body, 'bordersMin', {'x': 0, 'y': 0})
        hi = _vec(body, 'bordersMax', {'x': 0, 'y': 0})
        out.append(dict(
            planeZ=_field(body, 'planeZ', float, 0.0),
            fov=_field(body, 'fieldOfView', float, 30.0),
            pitch=_field(body, 'pitchDegrees', float, 12.0),
            fraction=_field(body, 'bodyScreenFraction', float, 0.18),
            aspect=_field(body, 'deviceAspect', float, 19.5 / 9),
            min=(lo['x'], lo['y']), max=(hi['x'], hi['y'])))
    return out


def camera(scene=None):
    """Where the first camera ends up, worked out the way SideViewCamera.Place does."""
    sc = scene or Scene()
    cam = cameras(sc)[0]
    body_h = 0.91
    for fid, (cls, b) in sc.docs.items():
        if cls == 114 and f'guid: {PLAYER_GUID}' in b:
            m = re.search(r'^    bodyHeight: (\S+)$', b, re.M)
            if m:
                body_h = float(m.group(1))
            break
    height = body_h / max(0.02, cam['fraction'])
    pitch = math.radians(cam['pitch'])
    distance = height * 0.5 * math.cos(pitch) / math.tan(math.radians(cam['fov'] / 2))
    # deviceAspect 0 means "do not letterbox", which is an editor convenience. What the
    # fighter actually plays on is still a phone, so that is what the framing is measured
    # against.
    aspect = cam['aspect'] if cam['aspect'] > 0.01 else 19.5 / 9
    return dict(height=height, distance=distance, aspect=aspect,
                planeZ=cam['planeZ'],
                z=cam['planeZ'] - distance * math.cos(pitch),
                rise=distance * math.sin(pitch))


def spawns(scene=None):
    """Where the two fighters' feet start, straight out of the scene."""
    sc = scene or Scene()
    out = []
    for go in sorted(sc.gos):
        body = sc.script(go, PLAYER_GUID)
        if body is None:
            continue
        tr = sc.transforms[sc.tr_of_go[go]]
        offset = _field(body, 'visualYOffset', float, 0.0)
        out.append((_field(body, 'playerIndex', int, 0),
                    tr['pos']['x'], tr['pos']['y'] - offset,
                    _field(body, 'spawnFacing', int, 1)))
    return [s[1:] for s in sorted(out)]


if __name__ == '__main__':
    W = bake(report=True)
    for name, boxes in (("solid", W["solid"]), ("ladder", W["ladder"])):
        print(f"\n{name}:")
        for b in sorted(boxes, key=lambda b: (b[1], b[0])):
            print(f"  X {b[0]/65536:7.2f}..{b[2]/65536:7.2f}  Y {b[1]/65536:6.2f}..{b[3]/65536:6.2f}")
    print("\nchests:")
    for box, kind, contents, name in W["chest"]:
        print(f"  {name:24s} kind={kind} contents={contents:2d}  "
              f"X {box[0]/65536:6.2f}..{box[2]/65536:6.2f}  Y {box[1]/65536:5.2f}..{box[3]/65536:5.2f}")
    print("\nspawns:", [(round(x, 2), round(y, 2), f) for x, y, f in spawns()])
