#!/usr/bin/env python3
"""
make_vfx_textures.py — the particle textures, drawn in code.

Most of what a particle LOOKS like is its texture, not its count. A hundred particles on a
soft grey smudge is mud; nine on a sharp tapered streak reads as a blade throwing sparks.
So these are drawn deliberately, each one with a job, rather than taken from whatever a pack
happened to ship.

They are drawn in code and not painted because of what this project is: docs/00 asks for
FLAT COLOURS and hard edges, and every one of these is a shape with an analytic falloff --
exactly the kind of thing a loop writes better than a hand does. It is also deterministic,
so a re-run leaves an empty diff and nobody has to wonder whether the file in git is the
file the recipe makes.

White with an alpha channel, always. The colour comes from the ParticleSystem's gradient at
run time, so one texture serves the hit, the parry and the chest, and changing the palette
(docs/35) changes the effects with it rather than orphaning them.

The shapes were designed against `docs/refs/vfx/preview.html`, which renders them on a
filmstrip so the timing can be looked at. The slash in particular is on its second version:
the first was a thick soft crescent and read as a wing rather than as a blade.

Run:  cd docs/refs && python3 make_vfx_textures.py
      cd docs/refs && python3 make_vfx_textures.py --check    # verify, write nothing
"""

import math
import os
import struct
import sys
import uuid
import zlib

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "..", "Gulag runners", "Assets", "_Game",
                   "Effects", "Textures")

# A texture that is imported wrong is a texture that looks wrong for reasons nobody can see
# in the file. These came in as textureType 8 -- SPRITE -- which is for 2D art in a Canvas,
# not for a particle. Default (0) is what a particle material samples. These are sprites in an unlit additive material: no mips (they are drawn at
# a consistent size and mips only soften them), clamped (a wrapped spark bleeds across its
# own edge), and compressed low -- they are tiny and a gradient is what DXT is worst at.
META = """fileFormatVersion: 2
guid: {guid}
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: 0
    sRGBTexture: 1
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  bumpmap:
    convertToNormalMap: 0
    externalNormalMap: 0
    heightScale: 0.25
    normalMapFilter: 0
  isReadable: 0
  streamingMipmaps: 0
  streamingMipmapsPriority: 0
  vTOnly: 0
  ignoreMipmapLimit: 0
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: 1
  maxTextureSize: {size}
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    mipBias: 0
    wrapU: 1
    wrapV: 1
    wrapW: 1
  nPOTScale: 0
  lightmap: 0
  compressionQuality: 50
  spriteMode: 0
  spriteExtrude: 1
  spriteMeshType: 1
  alignment: 0
  spritePivot: {{x: 0.5, y: 0.5}}
  spritePixelsToUnits: 100
  spriteBorder: {{x: 0, y: 0, z: 0, w: 0}}
  spriteGenerateFallbackPhysicsShape: 0
  alphaUsage: 1
  alphaIsTransparency: 1
  spriteTessellationDetail: -1
  textureType: 0
  textureShape: 1
  singleChannelComponent: 0
  flipbookRows: 1
  flipbookColumns: 1
  maxTextureSizeSet: 0
  compressionQualitySet: 0
  textureFormatSet: 0
  ignorePngGamma: 0
  applyGammaDecoding: 0
  swizzle: 50462976
  cookieLightType: 0
  platformSettings:
  - serializedVersion: 4
    buildTarget: DefaultTexturePlatform
    maxTextureSize: {size}
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 1
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    customData:
    physicsShape: []
    bones: []
    spriteID: 5e97eb03825dee720800000000000000
    internalID: 0
    vertices: []
    indices:
    edges: []
    weights: []
    secondaryTextures: []
    nameFileIdTable: {{}}
  mipmapLimitGroupName:
  pSDRemoveMatte: 0
  userData:
  assetBundleName:
  assetBundleVariant:
"""


def png(path, w, h, alpha):
    """White RGB, the shape in alpha. 8-bit RGBA, no interlacing."""
    px = bytearray(w * h * 4)
    for i, a in enumerate(alpha):
        px[i * 4:i * 4 + 4] = bytes((255, 255, 255, a))
    raw = b"".join(b"\x00" + bytes(px[y * w * 4:(y + 1) * w * 4]) for y in range(h))

    def chunk(tag, data):
        body = tag + data
        return (struct.pack(">I", len(data)) + body
                + struct.pack(">I", zlib.crc32(body) & 0xffffffff))

    blob = (b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9))
            + chunk(b"IEND", b""))
    with open(path, "wb") as handle:
        handle.write(blob)
    return len(blob)


def core(s=128):
    """
    The hot centre. A readable disc with a short falloff, NOT a gaussian smear.

    A stylised hit has to read as a SHAPE at an eighth of the screen on a phone held at
    arm's length. A soft blur at that size is a grey cloud; a disc with an edge is a hit.
    """
    out = bytearray(s * s)
    for y in range(s):
        for x in range(s):
            d = math.hypot(x - s / 2 + 0.5, y - s / 2 + 0.5) / (s / 2)
            a = 0.0 if d > 1 else (1.0 if d < 0.55 else (1 - (d - 0.55) / 0.45) ** 1.8)
            out[y * s + x] = int(255 * a)
    return s, s, out


def spark(w=128, h=32):
    """
    A streak: sharp at the head, tapering to nothing.

    Stretched along its own velocity by the ParticleSystem, this reads as a DIRECTION.
    A round particle stretched the same way reads as a smear, which is the difference
    between sparks coming off a blade and motion blur on a bug.
    """
    out = bytearray(w * h)
    for y in range(h):
        for x in range(w):
            t = x / (w - 1)
            thick = (1 - t) ** 0.7
            dy = abs(y - h / 2 + 0.5) / (h / 2)
            a = 0.0 if dy > thick else (1 - (dy / thick) ** 2) * (1 - t) ** 0.5
            out[y * w + x] = int(255 * min(1.0, a))
    return w, h, out


def ring(s=128):
    """
    A thin shell. This is the one that says FORCE.

    Thin on purpose: a fat ring reads as a bubble, and a bubble is the single most common
    way a hit effect ends up looking like a magic spell.
    """
    out = bytearray(s * s)
    for y in range(s):
        for x in range(s):
            d = math.hypot(x - s / 2 + 0.5, y - s / 2 + 0.5) / (s / 2)
            a = max(0.0, 1 - abs(d - 0.82) / 0.13) ** 1.6 if d <= 1 else 0.0
            out[y * s + x] = int(255 * a)
    return s, s, out


def slash(s=128):
    """
    The blade's arc: a sickle, thin, with points at both ends.

    Second version. The first was a thick soft crescent spanning 130 degrees, and on the
    preview filmstrip it read as a WING hanging beside the fighter rather than as a weapon
    passing through him. Thin is the whole fix; the pointed ends are what stop it looking
    like a piece of a circle.
    """
    out = bytearray(s * s)
    for y in range(s):
        for x in range(s):
            nx, ny = (x - s / 2 + 0.5) / (s / 2), (y - s / 2 + 0.5) / (s / 2)
            d = math.hypot(nx, ny)
            u = abs(math.atan2(ny, nx)) / 1.0            # radians each way from centre
            if u > 1 or d > 1:
                a = 0.0
            else:
                taper = (1 - u * u) ** 0.65
                thick = 0.085 * taper
                a = max(0.0, 1 - abs(d - 0.74) / max(1e-5, thick)) ** 0.85 * taper
            out[y * s + x] = int(255 * min(1.0, a))

    # Lift it so the ridge reaches full white.
    #
    # The band is about eleven pixels wide at its thickest and its peak is a one-pixel
    # ridge, so at this resolution no pixel centre lands exactly on it: the brightest
    # sample came out at 249, and a shape that never reaches 255 renders slightly washed
    # out whatever gradient is put over it. The arithmetic is right and the sampling
    # misses, which is a different thing from the shape being wrong -- so it is scaled
    # rather than redrawn. make_vfx_textures' own check is what caught it.
    peak = max(out)
    if 0 < peak < 255:
        gain = 255.0 / peak
        out = bytearray(min(255, int(v * gain)) for v in out)

    return s, s, out


def puff(s=128):
    """
    Dust: a soft blob with a bitten edge.

    Every other texture here is clean, because a spark and a blade arc are clean things.
    Dust is not. A perfect circle reads as a bubble or a smoke ring, so the radius is
    wobbled by three smooth harmonics -- cheap, deterministic, and enough that five of these
    overlapping look like a cloud rather than like five circles.

    Much softer than `core`, too. The hot centre of an impact has to read as a SHAPE; dust
    has to read as air with something in it, and a hard edge on dust is the most obvious
    tell of a cheap effect there is.
    """
    out = bytearray(s * s)
    for y in range(s):
        for x in range(s):
            nx, ny = (x - s / 2 + 0.5) / (s / 2), (y - s / 2 + 0.5) / (s / 2)
            d = math.hypot(nx, ny)
            if d > 1:
                continue
            ang = math.atan2(ny, nx)
            wobble = (0.06 * math.sin(ang * 3 + 0.7)
                      + 0.04 * math.sin(ang * 5 - 1.3)
                      + 0.03 * math.sin(ang * 8 + 2.1))
            edge = 0.78 + wobble
            inner = edge * 0.35
            a = 1.0 if d < inner else max(0.0, 1 - (d - inner)
                                          / max(1e-5, edge - inner)) ** 1.9
            out[y * s + x] = int(255 * min(1.0, a))

    peak = max(out)
    if 0 < peak < 255:
        gain = 255.0 / peak
        out = bytearray(min(255, int(v * gain)) for v in out)
    return s, s, out


SHAPES = [
    ("vfx_core", core, "the hot centre of an impact"),
    ("vfx_spark", spark, "a streak that reads as a direction"),
    ("vfx_ring", ring, "the thin shell that says force"),
    ("vfx_slash", slash, "the blade's arc"),
    ("vfx_puff", puff, "dust: soft, with an edge that is not a circle"),
]


def main(argv):
    check = "--check" in argv

    if not check:
        os.makedirs(OUT, exist_ok=True)

    problems = []
    for name, draw, what in SHAPES:
        w, h, alpha = draw()
        path = os.path.join(OUT, name + ".png")

        # A shape that is all on or all off is a shape somebody broke while tuning it.
        lit = sum(1 for a in alpha if a > 8) / float(len(alpha))
        if not 0.01 < lit < 0.75:
            problems.append("%s: %.0f%% of it is lit, which is not a shape — it is a "
                            "rectangle or a blank" % (name, lit * 100))
        if max(alpha) < 250:
            problems.append("%s: never reaches full opacity (peak alpha %d), so it will "
                            "look washed out however the gradient is set"
                            % (name, max(alpha)))

        if check:
            if not os.path.exists(path):
                problems.append("%s: not written yet — run without --check" % name)
            continue

        size = png(path, w, h, alpha)
        meta = path + ".meta"
        if not os.path.exists(meta):
            with open(meta, "w") as handle:
                handle.write(META.format(guid=uuid.uuid4().hex, size=max(w, h)))
        print("  %-12s %3dx%-3d %6d bytes  %4.0f%% lit   %s"
              % (name, w, h, size, lit * 100, what))

    if problems:
        for line in problems:
            print("make_vfx_textures: " + line)
        print("make_vfx_textures: %d problem(s)" % len(problems))
        return 1

    print("make_vfx_textures: ok (%d texture(s) in Assets/_Game/Effects/Textures)"
          % len(SHAPES))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
