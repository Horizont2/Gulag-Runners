"""The arena ground shader, in Python, so its arithmetic can be argued with.

Mirrors the fragment of Assets/_Game/Shaders/ArenaGround.shader and the choices
ArenaGroundSetup makes about which material goes on which block.

A shader is the one part of this project nothing here can compile: compile_check.py runs
Roslyn over the C# and never sees a .shader file, and Unity only reports an HLSL error
once the asset is imported on a machine with a GPU. What it CAN check is the arithmetic,
and that is where this shader's mistakes would live. Every number in it is in world
metres -- how far the turf spills over a lip, how deep the shadow under it reaches, how
many metres a tile covers -- and a metre is something a test can hold an opinion about.

The fault being fixed is worth stating in numbers, because it is the reason for all of
this. Arena_Demo's main floor is a Unity Cube scaled 4.41 x 0.24 x 30.16. Its UVs run
0..1 across every face. Tile that so the 30.16 m length looks right and the 0.24 m edge
gets the same texture squeezed into a hundred and twenty-fifth of the space; tile it for
the edge and the floor is a smear. `stretch_from_uvs` works that ratio out for a given
block, and `metres_per_tile` shows what the projection does instead: the same answer for
every block, because the block is not in the expression.
"""
import math
import os
import re

HERE = os.path.dirname(os.path.abspath(__file__))
SCENE = os.path.join(HERE, "..", "..", "Gulag runners", "Assets", "Scenes",
                     "Arena_Demo.unity")

# Unity's primitives, as the size of the mesh in its OWN space. The Cylinder being TWO
# units tall is not a detail: it is the same trap SimCollider.LocalBox documents, and
# here it would put a pillar's foot shading halfway up the pillar.
MESH_SIZE = {
    "Cube": (1.0, 1.0, 1.0),
    "Sphere": (1.0, 1.0, 1.0),
    "Quad": (1.0, 1.0, 0.0),
    "Plane": (10.0, 0.0, 10.0),
    "Cylinder": (1.0, 2.0, 1.0),
    "Capsule": (1.0, 2.0, 1.0),
}

# The six faces of a box, which is what every block in this arena is.
FACES = {
    "+Y top": (0.0, 1.0, 0.0),
    "-Y underside": (0.0, -1.0, 0.0),
    "+X end": (1.0, 0.0, 0.0),
    "-X end": (-1.0, 0.0, 0.0),
    "+Z front": (0.0, 0.0, 1.0),
    "-Z back": (0.0, 0.0, -1.0),
}


def saturate(v):
    return 0.0 if v < 0.0 else (1.0 if v > 1.0 else v)


def smoothstep(edge0, edge1, x):
    if edge1 <= edge0:
        return 0.0 if x < edge0 else 1.0
    t = saturate((x - edge0) / (edge1 - edge0))
    return t * t * (3.0 - 2.0 * t)


class Ground:
    """One material's settings: the same fields ArenaGroundSetup writes."""

    def __init__(self, top_tile=2.0, side_tile=2.0, top_facing=0.45, top_blend=0.22,
                 sharpness=6.0, overhang=0.05, overhang_fade=1.6, lip_shade=0.35,
                 lip_shade_depth=0.12, foot_shade=0.18, foot_shade_height=0.25,
                 mesh_size=(1.0, 1.0, 1.0),
                 top_tint=(1.0, 1.0, 1.0), side_tint=(1.0, 1.0, 1.0)):
        self.top_tile = top_tile
        self.side_tile = side_tile
        self.top_tint = top_tint
        self.side_tint = side_tint
        self.top_facing = top_facing
        self.top_blend = top_blend
        self.sharpness = sharpness
        self.overhang = overhang
        self.overhang_fade = overhang_fade
        self.lip_shade = lip_shade
        self.lip_shade_depth = lip_shade_depth
        self.foot_shade = foot_shade
        self.foot_shade_height = foot_shade_height
        self.mesh_size = mesh_size

    # --- the fragment, line for line

    def weights(self, normal):
        """How much of each world projection this face takes.

        Raising to a power before normalising narrows the seam: on the flat side of a box
        one projection wins outright and only the edges mix.
        """
        w = [abs(c) ** self.sharpness for c in normal]
        total = max(sum(w), 1e-4)
        return tuple(c / total for c in w)

    def uvs(self, position):
        """The three world-space projections, in TILES.

        There is no object scale anywhere in here. That is the entire fix: a tile is
        _TopTile metres across wherever it lands, so a block scaled thirty times its
        neighbour shows thirty times as many tiles rather than one stretched thirty times.
        """
        x, y, z = position
        side = max(self.side_tile, 1e-3)
        top = max(self.top_tile, 1e-3)
        return ((z / side, y / side), (x / top, z / top), (x / side, y / side))

    def cap(self, normal):
        """Is this face capped with the top material? Up-facing only.

        A downward face never is. The underside of a walkway is earth, and a platform
        with grass on its belly is the most obviously wrong thing the shader could draw.
        """
        return smoothstep(self.top_facing - self.top_blend,
                          self.top_facing + self.top_blend, normal[1])

    def lip(self, below_top, cap):
        """The cap spilling over the top edge, as a share, 1 at the edge and 0 below it."""
        if self.overhang <= 0.0:
            return 0.0
        t = saturate(1.0 - below_top / max(self.overhang, 1e-4))
        return (t ** self.overhang_fade) * (1.0 - cap)

    def shade(self, below_top, above_foot, cap):
        """How much this fragment is darkened: the deeper of the lip and the foot.

        The deeper of the two and not both multiplied, because on a 24 cm walkway the
        lip reaches further down than the foot reaches up and multiplying would put the
        whole edge in shadow -- losing the one thing the shading is there to say, which
        is which way is up.
        """
        under = 1.0 - saturate((below_top - self.overhang)
                               / max(self.lip_shade_depth, 1e-4))
        foot = 1.0 - saturate(above_foot / max(self.foot_shade_height, 1e-4))
        return max(under * self.lip_shade, foot * self.foot_shade) * (1.0 - cap)

    def span(self, block, position_y):
        """Metres below the object's own top, and metres above its own foot.

        Mirrors ObjectSpan. The world half-height is the mesh's three half-extents mapped
        onto world Y by row 1 of the object-to-world matrix and summed -- the same
        arithmetic SimCollider.RotatedBounds does on the CPU, and the reason a block
        turned on its side still measures from the edge that is actually uppermost.
        """
        half = [s * 0.5 for s in self.mesh_size]
        row = block.row_y
        half_height = (abs(row[0]) * half[0] + abs(row[1]) * half[1]
                       + abs(row[2]) * half[2])
        centre_y = block.origin[1]
        return ((centre_y + half_height) - position_y,
                position_y - (centre_y - half_height))


# ------------------------------------------------------------------ what a block is

class Block:
    """One renderer in the arena: its world box, its mesh and its matrix row."""

    def __init__(self, name, mesh, origin, basis, sim_kind):
        self.name = name
        self.mesh = mesh
        self.origin = origin                 # the pivot, in world space
        self.basis = basis                   # columns are the local axes, in world space
        self.sim_kind = sim_kind
        self.placeholder = None              # which placeholder it is still wearing
        self.materials = []                  # every material guid on the renderer
        # Row 1 of the object-to-world matrix: how each local axis contributes to world Y.
        self.row_y = (basis[1][0], basis[1][1], basis[1][2])

    @property
    def mesh_size(self):
        return MESH_SIZE.get(self.mesh, (1.0, 1.0, 1.0))

    @property
    def world_size(self):
        """The world AABB's size: what Renderer.bounds.size hands the setup tool."""
        half = [s * 0.5 for s in self.mesh_size]
        out = []
        for axis in range(3):
            out.append(2.0 * sum(abs(self.basis[axis][i]) * half[i] for i in range(3)))
        return tuple(out)

    @property
    def height(self):
        return self.world_size[1]

    @property
    def narrowest(self):
        s = self.world_size
        return min(s[0], s[2])

    def kind(self):
        """Ground, rock or pillar -- the rule in ArenaGroundSetup.Choose.

        Ground is what you could stand on: the top is at least as broad as the block is
        tall, in BOTH directions. "Wider than it is tall" is not the same test and was
        the first version of this rule: the back wall is 28.38 m long and 1.90 m high, so
        by that test it is ground, and the arena grows grass down a two-metre wall.

        Measured on the WORLD box and not on the transform's scale, because this level is
        laid out along Z and turned ninety degrees to run along X: read off lossyScale,
        the main floor's thirty metres come back as its depth and every floor in the
        arena classifies as a wall.
        """
        if self.mesh in ("Cylinder", "Capsule"):
            return "pillar"
        return "ground" if self.height <= self.narrowest else "rock"


# --------------------------------------------------------------- the stretch it fixes

def stretch_from_uvs(block):
    """How badly a single tiling factor would smear this block, worst face against best.

    A Unity Cube's UVs run 0..1 across every face, so one _BaseMap_ST has to serve all
    six. This is the ratio between the largest face dimension and the smallest, which is
    how far apart those faces' texel densities would be.
    """
    sx, sy, sz = block.world_size
    sides = [d for d in (sx, sy, sz) if d > 1e-4]
    if len(sides) < 2:
        return 1.0
    return max(sides) / min(sides)


def metres_per_tile(ground, block, face):
    """What the projection gives instead: the tile size, whatever the block is.

    Takes the block and the face so the signature reads like a measurement, then ignores
    both -- which IS the result. Nothing about the object reaches this number.
    """
    return ground.top_tile if face == "+Y top" else ground.side_tile


# ------------------------------------------------------------------------ the scene

# What counts as "nobody has textured this yet", by material guid.
#
# Unity's own grey is the obvious one. The second is not, and missing it would have cost
# the most important surface in the game. On the art branch the main floor -- the
# 30.16 x 4.41 m slab the whole fight happens on -- is NOT on Unity's grey: it carries
# Assets/Materials/Ground.mat, a half-finished first go at this exact job, with the
# albedo wired into its _BumpMap. A tool that only replaces Unity's default walks
# straight past it and textures the other eight blocks around it.
#
# Deliberate art does not belong in this list. The back wall on that branch is on a
# material from the dungeon pack, which is somebody's choice and is left alone and
# reported instead.
PLACEHOLDERS = {
    "31321ba15b8f8eb4c954353edc038b1d": "Unity Default-Material",
    "6fdb8bd2452e35d42b8b3cc7971b9bb5": "Materials/Ground.mat",
}


def blocks(path=SCENE, sim_collider_guid=None, default_material_only=True):
    """Every marked block in Arena_Demo, read straight out of the scene file.

    `default_material_only` means "still a placeholder", which is not the same as
    "still Unity's grey" -- see PLACEHOLDERS.
    """
    import arena_demo_geometry as geo

    scene = geo.Scene(path)
    docs = geo._docs(open(path, encoding="utf-8").read())
    guid = sim_collider_guid or geo.SIM_COLLIDER_GUID

    primitives = {"10202": "Cube", "10206": "Cylinder", "10207": "Sphere",
                  "10208": "Capsule", "10209": "Plane", "10210": "Quad"}

    marked = {}
    for _fid, (cls, body) in docs.items():
        if cls == 114 and f"guid: {guid}" in body:
            marked[geo._ref(body, "m_GameObject")] = geo._field(body, "kind", int, 0)

    out = []
    for _fid, (cls, body) in docs.items():
        if cls != 33:                                   # MeshFilter
            continue
        go = geo._ref(body, "m_GameObject")
        if go not in marked:
            continue

        mesh_id = re.search(r"m_Mesh: \{fileID: (-?\d+)", body)
        mesh = primitives.get(mesh_id.group(1), "?") if mesh_id else "?"

        renderer = next((b for c, b in docs.values()
                         if c == 23 and geo._ref(b, "m_GameObject") == go), None)
        materials = []
        if renderer is not None:
            block_text = re.search(r"m_Materials:\n((?:  - \{fileID:.*\n)*)", renderer)
            if block_text:
                materials = [g or f for f, g in re.findall(
                    r"fileID: (-?\d+)(?:, guid: ([0-9a-f]{32}))?", block_text.group(1))]

        placeholder = next((PLACEHOLDERS[m] for m in materials if m in PLACEHOLDERS), None)
        if default_material_only and placeholder is None:
            continue

        tf = scene.tr_of_go.get(go)
        if tf is None:
            continue
        origin, basis = scene.basis(tf)
        block = Block(scene.gos.get(go, {}).get("name", "?"), mesh,
                      origin, basis, marked[go])
        block.placeholder = placeholder
        block.materials = materials
        out.append(block)
    return out


# ------------------------------------------------------- the materials the tool writes
# Kept here rather than read back out of the .mat files because the materials do not
# exist until somebody clicks the menu item, and the check has to be able to say what
# clicking it WILL produce.

GROUND = dict(top_tile=2.0, side_tile=2.0, top_facing=0.45, top_blend=0.22,
              sharpness=6.0, overhang=0.05, overhang_fade=1.6, lip_shade=0.35,
              lip_shade_depth=0.12, foot_shade=0.18, foot_shade_height=0.25,
              mesh_size=(1.0, 1.0, 1.0),
              top_tint=(1.15, 1.15, 1.10), side_tint=(1.90, 1.85, 1.75))

ROCK = dict(top_tile=2.0, side_tile=2.0, top_facing=0.72, top_blend=0.12,
            sharpness=8.0, overhang=0.03, overhang_fade=2.4, lip_shade=0.3,
            lip_shade_depth=0.18, foot_shade=0.3, foot_shade_height=0.45,
            mesh_size=(1.0, 1.0, 1.0),
            top_tint=(1.15, 1.15, 1.10), side_tint=(1.80, 1.74, 1.62))

PILLAR = dict(top_tile=1.0, side_tile=1.0, top_facing=1.5, top_blend=0.05,
              sharpness=4.0, overhang=0.0, overhang_fade=1.0, lip_shade=0.0,
              lip_shade_depth=0.1, foot_shade=0.35, foot_shade_height=0.4,
              mesh_size=(1.0, 2.0, 1.0),
              top_tint=(1.0, 1.0, 1.0), side_tint=(1.0, 1.0, 1.0))

MATERIALS = {"ground": GROUND, "rock": ROCK, "pillar": PILLAR}


def material_for(block):
    return Ground(**MATERIALS[block.kind()])
