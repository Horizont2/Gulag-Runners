#!/usr/bin/env python3
"""
make_intro_smoke.py — the bank of smoke the first round of an arena opens out of.

ONE TEXTURE, NOT SIXTEEN. The reveal is four layers at four distances, in four arenas, and
the obvious way to build that is sixteen images. Smoke is smoke: what tells a mine's dust
from a forest's mist is its COLOUR, and the material already carries one. So this draws a
single band and RoundIntroSmoke tints it, which also means the four layers can be the same
image at four scales and offsets without anybody noticing -- at the speeds they move past
each other, nobody is reading the shapes.

FLAT COLOUR AND HARD EDGES, like everything else here (docs/00). A soft cloud is the one
thing this project's art direction has no room for, and it would also force alpha blending:
the layers sit in front of the arena, and blended geometry draws after the opaque pass, so
they would land on top of the interface as well. Cutout keeps them ordinary geometry.

So the band is OPAQUE in the middle with ragged edges and a few bites taken out of it, which
is what a stylised smoke bank looks like when you are not allowed to feather anything. Four
of them overlapping cover the frame completely at the start, which is the one thing the
reveal has to get right: a hole in the smoke on frame one shows the arena before the reveal
has begun.

    cd docs/refs && python3 make_intro_smoke.py
"""

import math
import os
import struct
import sys
import uuid
import zlib

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "..", "Gulag runners", "Assets", "_Game", "Effects")

# Wide, because a split-screen half is 4.33:1 and a layer has to cover one with room to
# slide. Four to one, at a size that is nothing on a phone once it is compressed.
WIDE, HIGH = 2048, 512

# How much of the image has to be solid, DERIVED rather than picked. The reveal is four
# layers at four distances, each at its own scale and offset, so where one has a hole the
# others almost certainly do not. Treating the holes as independent, four layers leave
# (1 - p)^4 of the frame uncovered, and the most that may show through on the first frame
# is one per cent of it:
#
#     (1 - p)^4 <= 0.01   ->   p >= 1 - 0.01^(1/4) = 0.684
#
# My first two numbers here were 0.88 and 0.72, both invented, and the second of them
# failed the band by one point -- which is the moment to work the threshold out rather than
# to move it until the art fits.
WANT = 1.0 - 0.01 ** 0.25


class Rng:
    def __init__(self, seed):
        self.s = seed & 0x7fffffff

    def next(self):
        self.s = (self.s * 1103515245 + 12345) & 0x7fffffff
        return self.s / 0x7fffffff

    def between(self, a, b):
        return a + (b - a) * self.next()

    def pick(self, *options):
        return options[min(len(options) - 1, int(self.next() * len(options)))]


def edge(rng, amp, base, cycles):
    """A ragged boundary that joins up with itself: whole numbers of waves, as ridge() does."""
    waves = [(max(1, int(math.ceil(rng.between(0.7, 1.4) * k))),
              rng.between(0, 6.28), amp / (k ** 0.8))
             for k in (1, 2, 4, 7)]

    def at(u):
        y = base
        for n, phase, a in waves:
            y += math.sin(u * n * 6.2831853 + phase) * a
        return y
    return at


def band(seed):
    """
    A bank with ragged ENDS, which is the part the player actually sees.

    I built it the other way round first: a strip across the middle of the image with
    ragged top and bottom, which came out 60% solid and would have shown the arena through
    it on the first frame. The mistake was thinking about the shape rather than about the
    MOVEMENT. These layers slide sideways out of frame, so what crosses the screen is the
    end of the bank, and what has to be ragged is the left and right of the image. The top
    and bottom barely show: they are off the frame for most of the reveal.

    So it is solid through the middle, top to bottom, and its two ends are lumpy tongues.
    Clamped rather than repeated, because an end that wraps is not an end.
    """
    rng = Rng(seed)
    px = bytearray(WIDE * HIGH * 4)

    top = edge(rng, HIGH * 0.055, HIGH * 0.035, 3)
    bottom = edge(rng, HIGH * 0.060, HIGH * 0.965, 3)

    # How far in the ends reach, as a share of the width, and how lumpy they are.
    tongue = 0.16
    left = edge(rng, WIDE * 0.055, WIDE * tongue * 0.55, 2)
    right = edge(rng, WIDE * 0.055, WIDE * (1.0 - tongue * 0.55), 2)

    for x in range(WIDE):
        u = x / float(WIDE)
        lo = max(0, min(HIGH, int(top(u))))
        hi = max(0, min(HIGH, int(bottom(u))))
        for y in range(lo, hi):
            v = y / float(HIGH)
            if x < left(v) or x > right(v):
                continue
            i = (y * WIDE + x) * 4
            px[i:i + 4] = b"\xff\xff\xff\xff"

    # A couple of bites, so the bank has holes in it rather than being one slab.
    for _ in range(4):
        cx = rng.between(WIDE * 0.25, WIDE * 0.75)
        cy = rng.between(HIGH * 0.15, HIGH * 0.85)
        r = rng.between(HIGH * 0.08, HIGH * 0.18)
        for dy in range(int(-r), int(r) + 1):
            y = int(cy + dy)
            if y < 0 or y >= HIGH:
                continue
            half = math.sqrt(max(0.0, r * r - dy * dy))
            for dx in range(int(-half), int(half) + 1):
                x = int(cx + dx)
                if 0 <= x < WIDE:
                    px[(y * WIDE + x) * 4 + 3] = 0

    return px


def png(path, w, h, px):
    raw = b"".join(b"\x00" + bytes(px[y * w * 4:(y + 1) * w * 4]) for y in range(h))

    def chunk(tag, body):
        return (struct.pack(">I", len(body)) + tag + body
                + struct.pack(">I", zlib.crc32(tag + body) & 0xffffffff))

    with open(path, "wb") as handle:
        handle.write(b"\x89PNG\r\n\x1a\n")
        handle.write(chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0)))
        handle.write(chunk(b"IDAT", zlib.compress(raw, 9)))
        handle.write(chunk(b"IEND", b""))


META = """fileFormatVersion: 2
guid: {guid}
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: 1
    sRGBTexture: 1
    mipMapsPreserveCoverage: 1
    alphaTestReferenceValue: 0.5
  isReadable: 0
  alphaIsTransparency: 1
  textureType: 0
  textureShape: 1
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    mipBias: 0
    wrapU: 1
    wrapV: 1
    wrapW: 1
  platformSettings:
  - serializedVersion: 3
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 2048
    textureCompression: 1
  userData:
  assetBundleName:
  assetBundleVariant:
"""


def main():
    os.makedirs(OUT, exist_ok=True)
    px = band(0x5A17)

    solid = sum(1 for k in range(3, len(px), 4) if px[k] > 8) / float(WIDE * HIGH)
    if solid < WANT:
        print("make_intro_smoke: the band is only %.0f%% solid; under %.0f%% the four layers stop covering one another" % (solid * 100, WANT * 100))
        return 1

    soft = sum(1 for k in range(3, len(px), 4) if 8 < px[k] < 247)
    if soft:
        print("make_intro_smoke: %d pixel(s) at partial alpha. These draw as cutout so they "
              "stay ordinary opaque geometry; anything feathered forces alpha blending and "
              "the smoke would then draw over the interface as well." % soft)
        return 1

    path = os.path.join(OUT, "fx_intro_smoke.png")
    png(path, WIDE, HIGH, px)
    if not os.path.exists(path + ".meta"):
        with open(path + ".meta", "w") as handle:
            handle.write(META.format(guid=uuid.uuid4().hex))

    print("make_intro_smoke: ok (%dx%d, %.0f%% solid, %d bytes)"
          % (WIDE, HIGH, solid * 100, os.path.getsize(path)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
