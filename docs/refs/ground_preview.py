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

    def shade(self, p, n):
        """The fragment, through ground_mirror: the same arithmetic as the shader."""
        m = self.material
        sharp = m.sharpness
        w = np.abs(n) ** sharp
        w = w / np.maximum(w.sum(axis=-1, keepdims=True), 1e-4)

        side_tile = max(m.side_tile, 1e-3)
        top_tile = max(m.top_tile, 1e-3)
        uv_x = np.stack([p[..., 2] / side_tile, p[..., 1] / side_tile], -1)
        uv_y = np.stack([p[..., 0] / top_tile, p[..., 2] / top_tile], -1)
        uv_z = np.stack([p[..., 0] / side_tile, p[..., 1] / side_tile], -1)

        side_w = np.maximum(w[..., 0:1] + w[..., 2:3], 1e-4)
        side = (sample(self.side, uv_x) * w[..., 0:1]
                + sample(self.side, uv_z) * w[..., 2:3]) / side_w
        side = side * np.array(m.side_tint)
        top = sample(self.top, uv_y) * np.array(m.top_tint)

        lo = m.top_facing - m.top_blend
        hi = m.top_facing + m.top_blend
        t = np.clip((n[..., 1] - lo) / max(hi - lo, 1e-6), 0.0, 1.0)
        cap = t * t * (3.0 - 2.0 * t)

        half = self.half * np.array([1.0, 1.0, 1.0])
        half_height = float(np.abs(self.basis[1]) @ half)
        centre_y = self.origin[1]
        below_top = (centre_y + half_height) - p[..., 1]
        above_foot = p[..., 1] - (centre_y - half_height)

        if m.overhang > 0:
            lip = np.clip(1.0 - below_top / max(m.overhang, 1e-4), 0.0, 1.0)
            lip = (lip ** m.overhang_fade) * (1.0 - cap)
        else:
            lip = np.zeros_like(cap)

        mask = np.maximum(cap, lip)[..., None]
        albedo = side * (1.0 - mask) + top * mask

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
    rest = []
    i = 0
    while i < len(argv):
        if argv[i] == "--width":
            width = int(argv[i + 1]); i += 2
        elif argv[i] == "--camera":
            which = int(argv[i + 1]); i += 2
        elif argv[i] == "--wide":
            whole = True; i += 1
        else:
            rest.append(argv[i]); i += 1
    if rest:
        out = rest[0]

    scene = Scene(gm.SCENE)
    docs = _docs(open(gm.SCENE, encoding="utf-8").read())

    height = int(width * 9 / 16)
    if whole:
        eye, basis, fov = wide(gm.blocks(), width / height)
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

    solids = [Solid(b) for b in gm.blocks()]
    print(f"ground_preview: {len(solids)} blocks, camera {which}, {width}x{height}")

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
