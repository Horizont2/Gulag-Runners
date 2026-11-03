"""Just enough of the binary FBX format to check what a file actually says.

Written because an exported model is a claim -- "this is a ladder, in metres, the right
way up" -- and the only place to test that claim without Unity is the file itself. The
dungeon pack's whole four-millimetre disaster was a UnitScaleFactor of 1 next to vertices
in metres, which is three bytes in a header nobody reads.

Node records are (EndOffset, NumProperties, PropertyListLen, NameLen, Name, properties,
children, null terminator); the first three are 32-bit below file version 7500 and 64-bit
from 7500 up. Array properties carry their own zlib flag. That is the whole format as far
as anything here is concerned.
"""
import struct
import zlib

_SCALAR = {"C": "?", "B": "?", "Y": "h", "I": "i", "F": "f", "D": "d", "L": "q"}
_ARRAY = {"f": "f", "d": "d", "l": "q", "i": "i", "b": "?"}


def _properties(data, off, count):
    out = []
    for _ in range(count):
        kind = chr(data[off])
        off += 1
        if kind in _SCALAR:
            fmt = "<" + _SCALAR[kind]
            out.append(struct.unpack_from(fmt, data, off)[0])
            off += struct.calcsize(fmt)
        elif kind in _ARRAY:
            n, encoded, length = struct.unpack_from("<III", data, off)
            off += 12
            raw = data[off:off + length]
            off += length
            if encoded:
                raw = zlib.decompress(raw)
            out.append(list(struct.unpack_from("<%d%s" % (n, _ARRAY[kind]), raw, 0)))
        elif kind in "SR":
            length, = struct.unpack_from("<I", data, off)
            off += 4
            value = data[off:off + length]
            off += length
            out.append(value.decode("utf-8", "replace") if kind == "S" else value)
        else:
            raise ValueError(f"unknown FBX property type {kind!r} at byte {off}")
    return out, off


def parse(path):
    """(version, [node]) where a node is (name, [property], [child])."""
    data = open(path, "rb").read()
    if not data.startswith(b"Kaydara FBX Binary"):
        raise ValueError(f"{path} is not a binary FBX")
    version, = struct.unpack_from("<I", data, 23)
    wide = version >= 7500

    def node(off):
        if wide:
            end, count, _plen = struct.unpack_from("<QQQ", data, off)
            off += 24
        else:
            end, count, _plen = struct.unpack_from("<III", data, off)
            off += 12
        length = data[off]
        off += 1
        name = data[off:off + length].decode()
        off += length
        if end == 0:
            return None, off
        props, off = _properties(data, off, count)
        kids = []
        while off < end:
            kid, off = node(off)
            if kid is None:
                break
            kids.append(kid)
        return (name, props, kids), end

    roots, off = [], 27
    while off < len(data) - 160:            # the footer is never a node
        found, off = node(off)
        if found is None:
            break
        roots.append(found)
    return version, roots


def walk(roots):
    """Every node as ('/Objects/Model', properties)."""
    def rec(n, path):
        name, props, kids = n
        here = path + "/" + name
        yield here, props
        for k in kids:
            yield from rec(k, here)
    for r in roots:
        yield from rec(r, "")


def geometry(path):
    """[(vertices, triangles)] per mesh, as plain lists of tuples.

    FBX stores a polygon as a run of vertex indices ending in one that is bitwise-NOT'd,
    which is how a face of any size is written without a separate count. Fanning each run
    from its first corner is enough for anything convex, and these models are quads.
    """
    _version, roots = parse(path)
    meshes, verts = [], None
    for where, props in walk(roots):
        if where.endswith("/Geometry/Vertices") and props:
            flat = props[0]
            verts = [tuple(flat[i:i + 3]) for i in range(0, len(flat), 3)]
        elif where.endswith("/Geometry/PolygonVertexIndex") and props and verts:
            tris, face = [], []
            for raw in props[0]:
                last = raw < 0
                face.append(~raw if last else raw)
                if last:
                    for i in range(1, len(face) - 1):
                        tris.append((face[0], face[i], face[i + 1]))
                    face = []
            meshes.append((verts, tris))
            verts = None
    return meshes


def meshes(path):
    """[(name, (lo, hi))] per mesh in the file, named as the FBX names it.

    Needed because a pack ships as ONE .fbx holding forty props, and the union of
    everything in it is the size of the pack rather than the size of a prop. Measuring
    the ladder and the barrel box as the same fifteen metres is what that looks like
    from the outside.

    Named after the MODEL node, not the geometry. A geometry in a Blender export is
    called "Plane.433" and the thing Unity calls a mesh is the model node hanging off
    it -- "Ladder_A_LOD 2". The two are joined only through the Connections section, so
    that is read too; without it the names are useless for matching a prefab.
    """
    _version, roots = parse(path)

    boxes, models, geometry_id = {}, {}, None
    for where, props in walk(roots):
        if where.endswith("/Objects/Geometry") and len(props) > 1:
            geometry_id = props[0] if isinstance(props[0], int) else None
        elif where.endswith("/Geometry/Vertices") and props and geometry_id is not None:
            v = props[0]
            xs, ys, zs = v[0::3], v[1::3], v[2::3]
            boxes[geometry_id] = ((min(xs), min(ys), min(zs)),
                                  (max(xs), max(ys), max(zs)))
            geometry_id = None
        elif where.endswith("/Objects/Model") and len(props) > 1:
            raw = props[1] if isinstance(props[1], str) else ""
            models[props[0]] = raw.split("\x00\x01")[0].split("::")[-1].strip()

    joined = {}
    for where, props in walk(roots):
        if not where.endswith("/Connections/C") or len(props) < 3:
            continue
        child, parent = props[1], props[2]
        if child in boxes and parent in models:
            joined[models[parent]] = boxes[child]

    out = [(name, box) for name, box in joined.items()]
    for gid, box in boxes.items():                       # unconnected, named by id
        if not any(b is box for _n, b in out):
            out.append((None, box))
    return out


def describe(path):
    """The handful of facts worth testing: units, axes, node turns, mesh boxes."""
    _version, roots = parse(path)
    out = {"unit": None, "up": None, "meshes": [], "turns": []}
    for where, props in walk(roots):
        if where.endswith("/P") and props:
            if props[0] == "UnitScaleFactor":
                out["unit"] = props[4] if len(props) > 4 else None
            elif props[0] == "UpAxis":
                out["up"] = props[4] if len(props) > 4 else None
            elif props[0] == "Lcl Rotation" and "/Objects/Model" in where:
                out["turns"].append(tuple(props[4:7]))
        elif where.endswith("/Geometry/Vertices") and props:
            v = props[0]
            xs, ys, zs = v[0::3], v[1::3], v[2::3]
            out["meshes"].append(((min(xs), min(ys), min(zs)),
                                  (max(xs), max(ys), max(zs))))
    return out
