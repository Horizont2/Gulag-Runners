"""Does the arena ground actually land where it is meant to?

A texture on a wall is the one kind of bug that looks like a taste decision. Nobody files
"the floor is stretched" as a fault; they say the level looks cheap, and it stays that
way. So the things that decide whether it looks right are checked as numbers here, and
all of them are in world metres:

  * every block the setup tool will touch gets a material, and the right one -- a
    cylinder on the cube material puts its shading half a pillar out, because Unity's
    Cylinder mesh is two units tall;
  * the cap lands on tops and nowhere else, which is checked on all six faces of a box
    rather than on the one the test author was thinking of;
  * the grass lip fits INSIDE the block it is on. Five centimetres of turf over the edge
    of a four-centimetre slab is not a lip, it is a block that is entirely lip, and the
    thin trim pieces in this arena are close enough to that to be worth measuring;
  * the texel density is the same on every block, which is the whole point of projecting
    from the world axes, and is stated here next to what the mesh's own UVs would have
    given so the two can be compared rather than asserted;
  * and no material in the project feeds a colour image to a normal-map slot. That one is
    not hypothetical: it is what the first hand-made ground material in this project did,
    and the symptom is a surface the light never catches, which reads as flat lighting
    rather than as a wire in the wrong hole.

Run it with no arguments. It exits non-zero on a fault, 0 when the only thing left is a
menu item somebody has to click.
"""
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import ground_mirror as gm

ASSETS = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                      "..", "..", "Gulag runners", "Assets")
MATERIAL_FOLDER = os.path.join(ASSETS, "_Game", "Materials")
SHADER = os.path.join(ASSETS, "_Game", "Shaders", "ArenaGround.shader")
SETUP = os.path.join(ASSETS, "_Game", "Editor", "ArenaGroundSetup.cs")

# Slots that are normal maps, whatever shader they belong to.
NORMAL_SLOTS = ("_BumpMap", "_NormalMap", "_DetailNormalMap",
                "_TopNormal", "_SideNormal")

# Which texture set each material caps and skirts itself with -- the same pairs
# ArenaGroundSetup hangs on the three materials it builds.
TEXTURE_SETS = {
    "ground": ("Forest_Ground_38", "Rocky_Dirt_2"),
    "rock": ("Forest_Ground_38", "Rocky_Dirt_2"),
    "pillar": ("Planks_Wood_16", "Planks_Wood_16"),
}


def guid_map():
    """guid -> asset path, for every .meta under Assets."""
    out = {}
    for base, dirs, files in os.walk(ASSETS):
        dirs[:] = [d for d in dirs if d not in ("Library", "Temp", "obj")]
        for f in files:
            if not f.endswith(".meta"):
                continue
            path = os.path.join(base, f)
            try:
                with open(path, encoding="utf-8", errors="ignore") as fh:
                    head = fh.read(400)
            except OSError:
                continue
            m = re.search(r"guid: ([0-9a-f]{32})", head)
            if m:
                out[m.group(1)] = path[:-5]
    return out


def texture_type(asset_path):
    """What the importer calls this texture. 1 is Normal map."""
    meta = asset_path + ".meta"
    if not os.path.exists(meta):
        return None
    with open(meta, encoding="utf-8", errors="ignore") as fh:
        m = re.search(r"^  textureType: (-?\d+)", fh.read(), re.M)
    return int(m.group(1)) if m else None


def materials():
    """Every .mat under Assets that this project authored, with its texture slots."""
    skip = ("Library", "Temp", "Game Buffs", "Polyart", "FlexUnit", "JMO Assets",
            "Fantasy Skybox", "PolyOne", "Handpainted_Grass_and_Ground_Textures",
            "LowPolyTreePack", "Ata Khani", "URP GanzSe Free Modular Character Pack",
            "Animated Fantasy Polygon Chest", "BK", "Animations",
            "Updated Modular Dungeon - May 2019", "AA Low Poly Medieval Environment")
    for base, dirs, files in os.walk(ASSETS):
        dirs[:] = [d for d in dirs if d not in skip]
        for f in files:
            if not f.endswith(".mat"):
                continue
            path = os.path.join(base, f)
            with open(path, encoding="utf-8", errors="ignore") as fh:
                text = fh.read()
            slots = {}
            for m in re.finditer(
                    r"- (_\w+):\n\s+m_Texture: \{fileID: (-?\d+)"
                    r"(?:, guid: ([0-9a-f]{32}))?", text):
                if m.group(3):
                    slots[m.group(1)] = m.group(3)
            yield path, slots


def texture_contrast():
    """Mean luma of each material's cap and side albedo, with its tint applied.

    Raises ImportError when Pillow is not here; the caller says so and carries on.
    """
    from PIL import Image

    from PIL import ImageStat

    pack = os.path.join(ASSETS, "Game Buffs", "Free Stylized Textures", "Textures")

    def luma(name, tint):
        path = os.path.join(pack, name, name + "_Albedo.png")
        if not os.path.exists(path):
            return 0.0
        im = Image.open(path).convert("RGB").resize((96, 96), Image.BILINEAR)
        r, g, b = ImageStat.Stat(im).mean
        return (0.2126 * r * tint[0] + 0.7152 * g * tint[1]
                + 0.0722 * b * tint[2]) / 255.0

    out = {}
    for kind, (top, side) in TEXTURE_SETS.items():
        settings = gm.MATERIALS[kind]
        out[kind] = (luma(top, settings["top_tint"]),
                     luma(side, settings["side_tint"]))
    return out


def main():
    problems, notes = [], []

    if not os.path.exists(SHADER):
        print("ground_check: Assets/_Game/Shaders/ArenaGround.shader is missing")
        return 1

    # --- the blocks, and which material each one gets

    blocks = gm.blocks()
    if not blocks:
        notes.append("no block in Arena_Demo is still on Unity's Default-Material -- "
                     "either the tool has been run, or somebody textured them by hand")

    for b in blocks:
        kind = b.kind()
        mat = gm.material_for(b)

        # The mesh's own size has to reach the material, or every metre in the shader is
        # measured from the wrong edge.
        if mat.mesh_size != b.mesh_size:
            problems.append(
                f"{b.name}: a {b.mesh} is {b.mesh_size[1]:g} units tall in its own space "
                f"but gets the {kind} material, whose _MeshSize is "
                f"{mat.mesh_size[1]:g} -- its lip and foot shading land "
                f"{abs(b.mesh_size[1] - mat.mesh_size[1]) * 0.5 * b.basis[1][1]:.2f} m "
                f"from the edge they belong on")

        # The cap, on all six faces of the box. A material may be capless on purpose --
        # a post is the same wood all the way round -- but then it has to be capless on
        # EVERY face, including the flat top. The half-capped top that _TopFacing = 1
        # quietly produces is the fault this distinction exists to catch.
        capless = mat.top_facing > 1.0
        for face, normal in gm.FACES.items():
            cap = mat.cap(normal)
            if capless:
                if cap > 0.01:
                    problems.append(f"{b.name} ({kind}): meant to have no cap, but the "
                                    f"{face} face is {cap:.2f} capped")
                continue
            if face == "+Y top" and cap < 0.99:
                problems.append(f"{b.name} ({kind}): the top face is only {cap:.2f} "
                                f"capped -- _TopFacing is above straight up")
            if face != "+Y top" and cap > 0.01:
                problems.append(f"{b.name} ({kind}): the {face} face is {cap:.2f} "
                                f"capped -- grass on a face that is not the top")

        if capless and mat.overhang > 0:
            problems.append(f"{b.name} ({kind}): no cap, but still an overhang of "
                            f"{mat.overhang * 100:.0f} cm -- a lip of nothing")
        if capless:
            continue

        # The lip has to fit in the block.
        if mat.overhang > 0 and mat.overhang >= b.height:
            problems.append(
                f"{b.name} ({kind}): the lip spills {mat.overhang * 100:.0f} cm over a "
                f"block only {b.height * 100:.0f} cm tall -- every face of it is lip")
        elif mat.overhang > 0 and mat.overhang > b.height * 0.45:
            notes.append(
                f"{b.name} ({kind}): {mat.overhang * 100:.0f} cm of lip on a "
                f"{b.height * 100:.0f} cm block is {mat.overhang / b.height:.0%} of its "
                f"side -- it will read as a grass block rather than a capped one")

        # How much of the overhang band the lip is actually strong in.
        #
        # "More than zero" is not the test, and was the first version of this one: at
        # _OverhangFade 900 the lip is 1e-271 half way down its own band, which is
        # greater than zero and invisible. The honest measure is how far down the turf
        # is still at least half opaque -- solve t ** fade = 0.5 -- as a share of the
        # overhang. Below about a tenth it is a hairline at the very edge, which looks
        # like a drawn line, which is the thing the lip exists to avoid.
        strong = 1.0 - 0.5 ** (1.0 / max(mat.overhang_fade, 1e-4))
        if mat.overhang > 0 and strong < 0.1:
            problems.append(
                f"{b.name} ({kind}): the lip is half strength only in the top "
                f"{strong * mat.overhang * 1000:.1f} mm of a "
                f"{mat.overhang * 1000:.0f} mm overhang -- _OverhangFade {mat.overhang_fade:g} "
                f"has flattened it to a drawn line")
        if mat.lip(mat.overhang * 2.0, cap=0.0) > 0.001:
            problems.append(f"{b.name} ({kind}): the lip is still showing at twice "
                            f"_Overhang below the edge")

        # And the cap has to win on a top face no matter what the lip does.
        if mat.lip(0.0, cap=mat.cap((0.0, 1.0, 0.0))) > 0.001:
            problems.append(f"{b.name} ({kind}): the lip is being mixed in on the top "
                            f"face, where the cap already covers it")

    # --- texel density: the thing the whole shader exists for

    if blocks:
        worst = max(blocks, key=gm.stretch_from_uvs)

        # Tile sizes are compared only BETWEEN BLOCKS SHOWING THE SAME TEXTURE. Across
        # different textures the comparison says nothing: the pillars are wood at a
        # metre and the floors are earth at two and a half, and they are supposed to
        # look like different materials, because they are. What must not differ is two
        # blocks of the same earth tiled differently, which reads as a mismatch.
        by_texture = {}
        for b in blocks:
            mat = gm.material_for(b)
            top, side = TEXTURE_SETS[b.kind()]
            by_texture.setdefault(top, set()).add(round(mat.top_tile, 3))
            by_texture.setdefault(side, set()).add(round(mat.side_tile, 3))

        for texture, tiles in sorted(by_texture.items()):
            if any(t <= 0 for t in tiles):
                problems.append(f"{texture}: a tile of 0 m is not a tile")
                continue
            spread = max(tiles) / min(tiles)
            if spread > 1.5:
                problems.append(
                    f"{texture} is tiled at {sorted(tiles)} m on different blocks, a "
                    f"{spread:.1f}x spread -- the same earth at two sizes side by side "
                    f"reads as two materials")

        notes.append(
            f"worst stretch the mesh UVs would have given: {gm.stretch_from_uvs(worst):.0f}x "
            f"on {worst.name} ({' x '.join(f'{v:.2f}' for v in worst.world_size)} m). "
            f"Projected from the world axes it is 1x on every block, because the block "
            f"is not in the expression.")

    # --- can you tell the cap from the side at all?

    # The test the eye actually applies. Both ground sets in this pack sit at about 0.13
    # luma, so a block textured with them and no tint is one dark mass whichever way its
    # faces point -- the flatness this shader exists to fix, arriving by another road.
    # Tinted, the side comes up to about 0.25 against a 0.15 cap, and the slab reads.
    #
    # Needs Pillow, which is a development dependency of this folder and not of the game,
    # so it says so and moves on rather than failing on a machine without it.
    try:
        contrasts = texture_contrast()
    except ImportError:
        contrasts = None
        notes.append("Pillow is not installed, so the cap-against-side contrast was not "
                     "measured. pip install pillow to check it.")

    for kind, (top_luma, side_luma) in (contrasts or {}).items():
        if max(top_luma, side_luma) <= 0:
            continue
        ratio = max(top_luma, side_luma) / max(min(top_luma, side_luma), 1e-4)
        if kind != "pillar" and ratio < 1.25:
            problems.append(
                f"{kind}: the cap sits at {top_luma:.2f} luma and the side at "
                f"{side_luma:.2f}, a {ratio:.2f}x difference -- too close to tell apart, "
                f"so a block will read as one mass however its faces point. Lift "
                f"_SideColor, which is HDR for exactly this.")

    # --- a colour image in a normal-map slot

    guids = guid_map()
    for path, slots in materials():
        for slot, guid in slots.items():
            if slot not in NORMAL_SLOTS:
                continue
            asset = guids.get(guid)
            if asset is None:
                problems.append(f"{os.path.relpath(path, ASSETS)}: {slot} points at "
                                f"{guid}, which is not in the project")
                continue
            kind = texture_type(asset)
            if kind is not None and kind != 1:
                problems.append(
                    f"{os.path.relpath(path, ASSETS)}: {slot} is fed "
                    f"{os.path.basename(asset)}, which is imported as texture type "
                    f"{kind}, not a normal map -- the light will never catch this "
                    f"surface and it will read as flat shading rather than a wiring "
                    f"mistake")

    # --- has anybody clicked the menu item yet

    made = [n for n in ("GR_ArenaGround", "GR_ArenaRock", "GR_ArenaPillar")
            if not os.path.exists(os.path.join(MATERIAL_FOLDER, n + ".mat"))]
    if made:
        notes.append("not built yet: " + ", ".join(made) +
                     ". Tools > Gulag Runners > Setup > Texture the arena ground makes "
                     "them and puts them on the blocks. Until then the arena is grey.")

    for n in notes:
        print("  note: " + n)
    for p in problems:
        print("  " + p)
    print(f"ground_check: {len(problems)} problem(s) across {len(blocks)} untextured "
          f"block(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
