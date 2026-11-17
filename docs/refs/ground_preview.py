"""Draws the arena with the ground shader on it, without Unity.

A shader is judged by eye, and the loop for judging this one is slow: edit the .shader,
let Unity import it, enter play mode, look. That loop is not available from here at all,
and it is slow enough on a real machine that nobody runs it for a half-metre change to a
tile size. So this renders the real scene -- the real boxes, out of Arena_Demo.unity, at
the real camera, with the real textures -- through the same arithmetic the fragment
shader uses, and writes a PNG.

It is not a renderer. There is one directional light, no shadows, no fog and no
tonemapping, and it does not pretend otherwise: what it is for is the things that WOULD
be wrong before any of that matters -- whether the ground is stretched, whether the grass
is on the tops and only the tops, whether the lip reads at the size a fighter is, whether
two neighbouring platforms look like the same material. Those are exactly the questions
the complaint was about, and none of them needs a light rig to answer.

    python3 ground_preview.py [out.png] [--width 1280] [--camera 2] [--wide]
                              [--scene path/to/Arena_Demo.unity]

--camera is one of the arena's own two cameras, which is what a player sees. --wide
pulls back far enough to frame every block at once, which is what you want when the
question is whether two platforms match rather than how one of them reads in play.

Needs Pillow and numpy, which are a development dependency of this folder and not of the
game.
"""
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import ground_mirror as gm
from arena_demo_geometry import Scene, _docs, _ref, _field

PACK = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..",
                    "Gulag runners", "Assets", "Game Buffs",
                    "Free Stylized Textures", "Textures")

# The sets ArenaGroundSetup hangs on each material.
SETS = {
    "ground": ("Forest_Ground_38", "Rocky_Dirt_2"),
    "rock": ("Forest_Ground_38", "Rocky_Dirt_2"),
    "pillar": ("Planks_Wood_16", "Planks_Wood_16"),
}

# The scene's directional light, as a direction TO the light, and its strength.
SUN = np.array([-0.43, 0.76, -0.49])
SUN /= np.linalg.norm(SUN)
SUN_COLOUR = np.array([1.0, 0.97, 0.90]) * 1.5
SKY = np.array([0.33, 0.40, 0.50]) * 0.55
GROUND_BOUNCE = np.array([0.22, 0.19, 0.14])


def load(name):
    from PIL import Image
    path = os.path.join(PACK, name, name + "_Albedo.png")
    if not os.path.exists(path):
        raise SystemExit(
            f"ground_preview: {path} is missing.\n"
            "That is the Free Stylized Textures pack, under Assets/Game Buffs.")
    im = Image.open(path).convert("RGB").resize((512, 512), Image.LANCZOS)
    return (np.asarray(im, dtype=np.float32) / 255.0) ** 2.2        # to linear


def sample(tex, uv):
    """Nearest-neighbour, tiling. uv is in tiles, so it wraps by construction."""
    h, w, _ = tex.shape
    x = np.mod((uv[..., 0] * w).astype(np.int64), w)
    y = np.mod((uv[..., 1] * h).astype(np.int64), h)
    return tex[y, x]


def luma(c):
    return c[..., 0] * 0.2126 + c[..., 1] * 0.7152 + c[..., 2] * 0.0722


def smoothstep(lo, hi, x):
    t = np.clip((x - lo) / max(hi - lo, 1e-6), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def height_blend(height_a, height_b, want, depth):
    """Mirrors HeightBlend in the shader: both materials argue, the higher one wins."""
    depth = max(depth, 1e-4)
    a = (1.0 - want) + height_a * depth
    b = want + height_b * depth
    m = np.maximum(a, b) - depth
    wa = np.maximum(a - m, 0.0)
    wb = np.maximum(b - m, 0.0)
    return wb / np.maximum(wa + wb, 1e-4)


def _frac(x):
    """HLSL frac(), which is x - floor(x) and never negative.

    numpy's modf keeps the sign of its input, and half this arena sits at negative x,
    so using it here would hash the two ends of the level differently from the shader.
    """
    return x - np.floor(x)


def _hash13(p):
    """The shader's Hash13, in float64. Same arithmetic, wider floats.

    The bits will not match a GPU's float32 exactly, and do not need to: what the
    preview answers is whether patches of the right SIZE break the surface up, which
    any hash without visible structure answers equally well.
    """
    p = _frac(p * 0.1031)
    p = p + (p * (p[..., ::-1] + 31.32)).sum(axis=-1, keepdims=True)
    return _frac((p[..., 0] + p[..., 1]) * p[..., 2])


def noise3(p):
    """Trilinear value noise, one wavelength per unit of p. Mirrors Noise3."""
    i = np.floor(p)
    f = p - i
    f = f * f * (3.0 - 2.0 * f)
    corners = {}
    for dx in (0, 1):
        for dy in (0, 1):
            for dz in (0, 1):
                corners[(dx, dy, dz)] = _hash13(i + np.array([dx, dy, dz], dtype=float))
    fx, fy, fz = f[..., 0], f[..., 1], f[..., 2]

    def mix(a, b, t):
        return a * (1.0 - t) + b * t

    x00 = mix(corners[(0, 0, 0)], corners[(1, 0, 0)], fx)
    x10 = mix(corners[(0, 1, 0)], corners[(1, 1, 0)], fx)
    x01 = mix(corners[(0, 0, 1)], corners[(1, 0, 1)], fx)
    x11 = mix(corners[(0, 1, 1)], corners[(1, 1, 1)], fx)
    return mix(mix(x00, x10, fy), mix(x01, x11, fy), fz)


# ------------------------------------------------------------------ the arena, as boxes

class Solid:
    """One block, with the inverse of its basis so a ray can be put into its own space."""

    def __init__(self, block):
        self.block = block
        self.origin = np.array(block.origin, dtype=np.float64)
        self.basis = np.array(block.basis, dtype=np.float64)
        self.inv = np.linalg.inv(self.basis)
        self.half = np.array(block.mesh_size, dtype=np.float64) * 0.5
        self.round = block.mesh in ("Cylinder", "Capsule")
        self.material = gm.material_for(block)
        top, side = SETS[block.kind()]
        self.top = load(top)
        self.side = load(side)

    def hit(self, ro, rd):
        """Slab test in the block's own space. Returns (t, normal in world space).

        A cylinder is tested as its box and then the two flat sides of that box are
        rounded off -- enough for a preview of six posts seen from ten metres, and the
        thing that matters about them here is their height, which the box has exactly.
        """
        o = (ro - self.origin) @ self.inv.T
        d = rd @ self.inv.T
        safe = np.where(np.abs(d) < 1e-9, 1e-9, d)

        t1 = (-self.half - o) / safe
        t2 = (self.half - o) / safe
        near = np.minimum(t1, t2).max(axis=-1)
        far = np.maximum(t1, t2).min(axis=-1)

        hit = (far >= np.maximum(near, 0.0))
        t = np.where(hit, near, np.inf)

        # Which slab was entered last is the face that was hit.
        axis = np.argmax(np.minimum(t1, t2), axis=-1)
        n_local = np.zeros(o.shape)
        np.put_along_axis(n_local, axis[..., None],
                          -np.sign(np.take_along_axis(d, axis[..., None], -1)), -1)

        if self.round:
            # Only where the ray hit: t is inf elsewhere, and inf * 0 is a nan that
            # poisons the normal and then the whole pixel.
            p = o + d * np.where(np.isfinite(t), t, 0.0)[..., None]
            flat = axis != 1
            side_n = np.stack([p[..., 0], np.zeros_like(p[..., 1]), p[..., 2]], -1)
            length = np.linalg.norm(side_n, axis=-1, keepdims=True)
            side_n = side_n / np.where(length < 1e-9, 1.0, length)
            n_local = np.where(flat[..., None], side_n, n_local)

        # A normal is transformed by the inverse transpose, which for this basis is inv.
        n_world = n_local @ self.inv
        length = np.linalg.norm(n_world, axis=-1, keepdims=True)
        return t, n_world / np.where(length < 1e-9, 1.0, length)

    def round_edges(self, p, n):
        """Mirrors RoundEdges: the normal swung towards the face around the corner.

        The box stays a box; what changes is that every edge gets a shoulder that catches
        light differently from both faces, which is the thing a cube does not have and
        real ground always does.
        """
        m = self.material
        if m.edge_round <= 0 or m.edge_round_strength <= 0:
            return n

        scale = np.array([np.linalg.norm(self.basis[:, a]) for a in range(3)])
        local = (p - self.origin) @ self.inv.T
        half = np.array(m.mesh_size) * 0.5
        to_edge = (half - np.abs(local)) * scale

        # The axis this face points along is not an edge of its own.
        own = np.abs(n @ self.basis)
        ranked = to_edge + own * 1e3

        nearest = ranked.min(axis=-1, keepdims=True)
        t = np.clip(1.0 - nearest / m.edge_round, 0.0, 1.0)

        pick = (ranked <= nearest).astype(float)
        pick /= np.maximum(pick.sum(axis=-1, keepdims=True), 1.0)
        dir_local = pick * np.sign(local)
        adjacent = dir_local @ self.basis.T
        adjacent /= np.maximum(np.linalg.norm(adjacent, axis=-1, keepdims=True), 1e-9)

        blended = n + adjacent
        blended /= np.maximum(np.linalg.norm(blended, axis=-1, keepdims=True), 1e-9)
        out = n + (blended - n) * (t * m.edge_round_strength)
        return out / np.maximum(np.linalg.norm(out, axis=-1, keepdims=True), 1e-9)

    def shade(self, p, n):
        """The fragment, through ground_mirror: the same arithmetic as the shader."""
        m = self.material
        sharp = m.sharpness
        w = np.abs(n) ** sharp
        w = w / np.maximum(w.sum(axis=-1, keepdims=True), 1e-4)

        big = noise3(p / max(m.macro_scale, 0.1))
        small = noise3(p / max(m.macro_scale * m.detune, 0.05))

        # Bend the lookup off its grid. Fixed texel density means a dead straight repeat
        # every two metres, and a regular grid shows up long before the texture does.
        warp = np.stack([big * 2.0 - 1.0, small * 2.0 - 1.0], -1) * m.warp

        side_tile = max(m.side_tile, 1e-3)
        top_tile = max(m.top_tile, 1e-3)
        uv_x = np.stack([p[..., 2] / side_tile, p[..., 1] / side_tile], -1) + warp
        uv_y = np.stack([p[..., 0] / top_tile, p[..., 2] / top_tile], -1) + warp
        uv_z = np.stack([p[..., 0] / side_tile, p[..., 1] / side_tile], -1) + warp

        side_w = np.maximum(w[..., 0:1] + w[..., 2:3], 1e-4)
        side_raw = (sample(self.side, uv_x) * w[..., 0:1]
                    + sample(self.side, uv_z) * w[..., 2:3]) / side_w
        top_raw = sample(self.top, uv_y)

        wear = smoothstep(0.55, 0.95, small)[..., None] * m.wear
        top_raw = top_raw * (1.0 - wear) + side_raw * wear

        side = side_raw * np.array(m.side_tint)
        top = top_raw * np.array(m.top_tint)

        # The turf follows the rounded shoulder, so it wraps an edge instead of
        # stopping dead on the corner.
        shading = self.round_edges(p, n)
        lo = m.top_facing - m.top_blend
        hi = m.top_facing + m.top_blend
        t = np.clip((shading[..., 1] - lo) / max(hi - lo, 1e-6), 0.0, 1.0)
        cap = t * t * (3.0 - 2.0 * t)

        half = self.half * np.array([1.0, 1.0, 1.0])
        half_height = float(np.abs(self.basis[1]) @ half)
        centre_y = self.origin[1]
        below_top = (centre_y + half_height) - p[..., 1]
        above_foot = p[..., 1] - (centre_y - half_height)

        if m.overhang > 0:
            fine = noise3(p / max(m.lip_jitter_scale, 0.02))
            ragged = np.maximum(1.0 + (fine * 2.0 - 1.0) * m.lip_jitter, 0.08)
            lip = np.clip(1.0 - below_top / np.maximum(m.overhang * ragged, 1e-4),
                          0.0, 1.0)
            lip = (lip ** m.overhang_fade) * (1.0 - cap)
        else:
            lip = np.zeros_like(cap)

        want = np.maximum(cap, lip)
        mask = height_blend(luma(side_raw), luma(top_raw), want, m.interlock)[..., None]
        albedo = side * (1.0 - mask) + top * mask

        macro = (big * 0.65 + small * 0.35)[..., None]
        albedo = albedo * ((1.0 - m.macro_strength)
                           + macro * 2.0 * m.macro_strength)
        warm = np.array([1.06, 1.00, 0.90])
        cool = np.array([0.92, 0.98, 1.06])
        drift = warm * (1.0 - macro) + cool * macro
        albedo = albedo * (1.0 + (drift - 1.0) * m.macro_warm)

        under = 1.0 - np.clip((below_top - m.overhang)
                              / max(m.lip_shade_depth, 1e-4), 0.0, 1.0)
        foot = 1.0 - np.clip(above_foot / max(m.foot_shade_height, 1e-4), 0.0, 1.0)
        shade = np.maximum(under * m.lip_shade, foot * m.foot_shade) * (1.0 - cap)
        return albedo * (1.0 - shade)[..., None]


# ------------------------------------------------------------------------- the camera

def camera(scene, docs, which):
    """The real camera out of the scene: position, basis and vertical field of view."""
    for _fid, (cls, body) in docs.items():
        if cls != 20:
            continue
        go = _ref(body, "m_GameObject")
        name = scene.gos.get(go, {}).get("name", "")
        if not name.endswith(str(which)):
            continue
        tf = scene.tr_of_go[go]
        origin, basis = scene.basis(tf)
        fov = _field(body, "field of view", float, 30.0)
        return np.array(origin), np.array(basis), fov
    raise SystemExit(f"ground_preview: no camera named '...{which}' in the scene")


def hero(blocks, aspect):
    """Three-quarter view of the end of the main floor, close in and slightly above.

    The arena's own cameras sit almost at floor level, so what they frame is mostly the
    top plane: good for judging the fight, useless for judging whether a block reads as
    a solid thing. This looks down the length of the biggest slab from off to one side,
    where its top, its side and the edge between them are all in shot at once.
    """
    floor = max(blocks, key=lambda b: b.world_size[0] * b.world_size[2])
    centre = np.array(floor.origin)
    size = np.array(floor.world_size)

    end = centre + np.array([size[0] * 0.30, 0.0, 0.0])
    eye = end + np.array([-2.6, 1.55, -3.4])

    forward = end + np.array([0.0, -0.25, 0.0]) - eye
    forward /= np.linalg.norm(forward)
    right = np.cross(np.array([0.0, 1.0, 0.0]), forward)
    right /= np.linalg.norm(right)
    up = np.cross(forward, right)
    return eye, np.stack([right, up, forward], axis=1), 38.0


def wide(blocks, aspect):
    """A camera far enough back to hold the whole arena, square on and level.

    Framed off the blocks themselves rather than hard-coded, so it still frames the
    arena after somebody extends it.
    """
    lo = np.array([1e9, 1e9, 1e9])
    hi = -lo
    for b in blocks:
        centre = np.array(b.origin)
        size = np.array(b.world_size) * 0.5
        lo = np.minimum(lo, centre - size)
        hi = np.maximum(hi, centre + size)

    centre = (lo + hi) * 0.5
    fov = 32.0
    half_h = np.tan(np.radians(fov) * 0.5)
    span = hi - lo
    need = max(span[1] * 0.5 / half_h, span[0] * 0.5 / (half_h * aspect))
    eye = np.array([centre[0], centre[1] + span[1] * 0.08, lo[2] - need * 1.12])
    basis = np.array([[1.0, 0.0, 0.0], [0.0, 1.0, 0.0], [0.0, 0.0, 1.0]])
    return eye, basis, fov


def main(argv):
    out = "arena_ground.png"
    width, which, whole = 1280, 1, False
    scene_path = gm.SCENE
    rest = []
    i = 0
    while i < len(argv):
        if argv[i] == "--width":
            width = int(argv[i + 1]); i += 2
        elif argv[i] == "--camera":
            which = int(argv[i + 1]); i += 2
        elif argv[i] == "--scene":
            scene_path = argv[i + 1]; i += 2
        elif argv[i] == "--wide":
            whole = "wide"; i += 1
        elif argv[i] == "--hero":
            whole = "hero"; i += 1
        else:
            rest.append(argv[i]); i += 1
    if rest:
        out = rest[0]

    scene = Scene(scene_path)
    docs = _docs(open(scene_path, encoding="utf-8").read())

    height = int(width * 9 / 16)
    if whole == "wide":
        eye, basis, fov = wide(gm.blocks(path=scene_path), width / height)
    elif whole == "hero":
        eye, basis, fov = hero(gm.blocks(path=scene_path), width / height)
    else:
        eye, basis, fov = camera(scene, docs, which)
    half_h = np.tan(np.radians(fov) * 0.5)
    half_w = half_h * width / height

    px = (np.arange(width) + 0.5) / width * 2.0 - 1.0
    py = 1.0 - (np.arange(height) + 0.5) / height * 2.0
    gx, gy = np.meshgrid(px * half_w, py * half_h)

    right, up, forward = basis[:, 0], basis[:, 1], basis[:, 2]
    right = right / np.linalg.norm(right)
    up = up / np.linalg.norm(up)
    forward = forward / np.linalg.norm(forward)

    rd = (gx[..., None] * right + gy[..., None] * up + forward)
    rd = rd / np.linalg.norm(rd, axis=-1, keepdims=True)
    ro = np.broadcast_to(eye, rd.shape)

    solids = [Solid(b) for b in gm.blocks(path=scene_path)]
    print(f"ground_preview: {len(solids)} blocks from {os.path.basename(scene_path)}, "
          f"{whole or ('camera ' + str(which))}, {width}x{height}")

    best_t = np.full(rd.shape[:2], np.inf)
    colour = np.zeros(rd.shape)
    # A plain vertical gradient behind everything, so the silhouettes read.
    sky = np.linspace(0.55, 0.15, height)[:, None, None] * np.array([0.42, 0.52, 0.62])
    colour[:] = sky

    for s in solids:
        t, n = s.hit(ro, rd)
        closer = t < best_t
        if not closer.any():
            continue
        best_t = np.where(closer, t, best_t)
        p = ro + rd * np.where(np.isfinite(t), t, 0.0)[..., None]
        albedo = s.shade(p, n)

        # Lit by the ROUNDED normal, which is where the edge shoulders come from.
        n = s.round_edges(p, n)
        lambert = np.maximum(n @ SUN, 0.0)[..., None]
        sky_term = (0.5 + 0.5 * n[..., 1])[..., None]
        lit = albedo * (SUN_COLOUR * lambert + SKY * sky_term
                        + GROUND_BOUNCE * (1.0 - sky_term))
        colour = np.where(closer[..., None], lit, colour)

    from PIL import Image
    srgb = np.clip(colour, 0.0, None) ** (1.0 / 2.2)
    img = Image.fromarray((np.clip(srgb, 0, 1) * 255).astype(np.uint8))
    img.save(out)
    print(f"ground_preview: wrote {out}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
