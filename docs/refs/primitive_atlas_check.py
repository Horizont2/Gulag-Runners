#!/usr/bin/env python3
"""
primitive_atlas_check.py — a Unity primitive wearing an atlas shows the WHOLE atlas.

The temple's floors came up as a grid of coloured squares, like a swatch chart laid over
the arena. Nothing was broken in the usual senses: the shader resolved, the texture loaded,
the material was assigned, and every check in this folder passed.

The fault is the UVs. A low-poly pack like the Egypt one paints everything from one big
palette image, and each of its own meshes has UVs aimed at a single swatch inside it. A
Unity PRIMITIVE -- Cube, Quad, Plane, Sphere -- has UVs that span the full 0 to 1 range,
because it was made to carry an ordinary texture. Put the palette on a primitive and it
samples all of it, so the floor shows the entire chart.

Four primitive cubes in loc3_new wore LVL_Ground, whose albedo is 8192 by 8192 of swatches.
No mesh from the pack used that material in the scene at all -- the cubes were built by a
tool, and the material was picked because it was the pack's "ground" one.

docs/00 asks for FLAT COLOUR, which is what the project's own GR_* materials are: a
_BaseColor and no texture. That is also the fix, and it is why this rule is safe to hold
here where it would not be in another project: nothing in this game is supposed to be
wearing a detail texture on a primitive.

The rule: a primitive mesh may not wear a material whose albedo is bigger than 2048 square.
That size is the line between a tiling detail map, which is fine on a cube, and an ATLAS,
which is never fine on one.

Run: python3 primitive_atlas_check.py
"""

import os
import re
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.join(HERE, "..", "..", "Gulag runners", "Assets")
SCENES = os.path.join(ASSETS, "Scenes")

# Unity's built-in meshes, by fileID inside the default resources.
PRIMITIVES = {"10202": "Cube", "10206": "Quad", "10207": "Capsule",
              "10208": "Sphere", "10209": "Cylinder", "10210": "Plane"}

ATLAS = 2048          # bigger than this on a primitive is a palette, not a detail map

GUID = re.compile(r"guid: ([0-9a-f]{32})")


def read(path):
    with open(path, "r", encoding="utf-8", errors="replace") as handle:
        return handle.read()


def by_guid(suffix):
    """Every asset of one kind, keyed by its guid."""
    out = {}
    for root, _, names in os.walk(ASSETS):
        for f in names:
            if not f.endswith(suffix + ".meta"):
                continue
            meta = os.path.join(root, f)
            asset = meta[:-5]
            if not os.path.exists(asset):
                continue
            g = GUID.search(read(meta))
            if g:
                out[g.group(1)] = asset
    return out


def png_size(path):
    try:
        with open(path, "rb") as handle:
            head = handle.read(26)
        if head[:8] != b"\x89PNG\r\n\x1a\n":
            return None
        return struct.unpack(">II", head[16:24])
    except OSError:
        return None


def albedo_of(mat_path, textures):
    """The _BaseMap (or _MainTex) of a material, as a path, or None."""
    text = read(mat_path)
    for prop in ("_BaseMap", "_MainTex"):
        m = re.search(r"- %s:\s*\n\s*m_Texture: \{fileID: (\d+), guid: ([0-9a-f]{32})"
                      % prop, text)
        if m and m.group(1) != "0":
            return textures.get(m.group(2))
    return None


def main():
    materials = by_guid(".mat")
    textures = {}
    for suffix in (".png", ".jpg", ".tga", ".psd"):
        textures.update(by_guid(suffix))

    problems = []
    checked = 0

    for f in sorted(os.listdir(SCENES)):
        if not f.endswith(".unity"):
            continue
        scene = read(os.path.join(SCENES, f))
        blocks = scene.split("--- !u!")

        # which GameObject holds which primitive
        prim = {}
        for b in blocks:
            if "MeshFilter:" not in b:
                continue
            mesh = re.search(r"m_Mesh: \{fileID: (-?\d+)", b)
            go = re.search(r"m_GameObject: \{fileID: (\d+)\}", b)
            if mesh and go and mesh.group(1) in PRIMITIVES:
                prim[go.group(1)] = PRIMITIVES[mesh.group(1)]

        for b in blocks:
            if "MeshRenderer:" not in b:
                continue
            go = re.search(r"m_GameObject: \{fileID: (\d+)\}", b)
            if not go or go.group(1) not in prim:
                continue
            for g in set(GUID.findall(b)):
                mat = materials.get(g)
                if not mat:
                    continue
                checked += 1
                tex = albedo_of(mat, textures)
                if not tex:
                    continue
                size = png_size(tex)
                if not size or max(size) <= ATLAS:
                    continue
                problems.append(
                    "%s: a %s wears %s, whose albedo is %dx%d. A primitive's UVs span the "
                    "whole 0..1, so it draws the ENTIRE image -- on a palette that is a "
                    "grid of swatches across the floor. Give it a flat _BaseColor material "
                    "(docs/00), as every GR_* material in this project is"
                    % (f, prim[go.group(1)], os.path.basename(mat), size[0], size[1]))

    if problems:
        for line in sorted(set(problems)):
            print("primitive_atlas_check: " + line)
        print("primitive_atlas_check: %d problem(s)" % len(set(problems)))
        return 1

    print("primitive_atlas_check: ok (%d material(s) on primitive meshes, none of them an "
          "atlas)" % checked)
    return 0


if __name__ == "__main__":
    sys.exit(main())
