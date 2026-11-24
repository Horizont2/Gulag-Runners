#!/usr/bin/env python3
"""
make_intro_smoke.py — the bank of cloud the first round of an arena opens out of.

ONE TEXTURE, NOT SIXTEEN. The reveal is four layers at four distances, in four arenas, and
the obvious way to build that is sixteen images. Cloud is cloud: what tells a mine's dust
from a forest's mist is its COLOUR, and the material already carries one. So this draws a
single bank and RoundIntroSmoke tints it, which also means the four layers can be the same
image at four scales and offsets without anybody noticing -- at the speeds they move past
each other, nobody is reading the shapes.

IT USED TO BE A PAPER CUT-OUT, and that is what the opening looked like: a hard-edged white
slab with round holes punched in it, sliding sideways. The note that made it one said a soft
cloud would force alpha blending, and that blended geometry draws after the opaque pass and
would therefore land on top of the interface. The first half is true. The second half is not
true of THIS project: every canvas in the game is ScreenSpaceOverlay, which Unity composites
after the camera has finished entirely, so nothing drawn in the world can cover the HUD
whatever queue it is in. The constraint that forced the cut-out never applied.

So this is built the way cloud is actually drawn: a chain of overlapping lobes along a wavy
spine, summed into a density field, and the field thresholded SOFTLY so the inside is solid
and the edge feathers over a dozen pixels. The lobes bunch and swell towards the top, which
is what makes a cumulus read as cauliflower rather than as a blob, and the base is flatter,
which is what makes it read as a cloud rather than as a cotton ball.

And it is SHADED. A flat white mass is a shape; a mass that is bright along its crown and
falls away towards its base has volume, and volume is the whole difference between clouds
parting and a sheet of paper being pulled aside. The light is from above and slightly
behind, so the feathered edge carries a little rim -- which is what the sun does to the
edge of a cloud and what the eye uses to tell one from fog.

Still one bank, still tinted per arena, still cheap: 2048 x 512, a few tens of kilobytes.

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
    """A ragged boundary: a few whole waves, so it never ends part way through one."""
    waves = []
    for k in (1, 2, 3.5):
        n = max(1, int(round(rng.between(0.7, 1.5) * k * cycles)))
        waves.append((n, rng.between(0, 6.2831853), amp / (k ** 0.8)))

    def at(u):
        y = base
        for n, phase, a in waves:
            y += math.sin(u * n * 6.2831853 + phase) * a
        return y
    return at


def lobes(rng):
    """The puffs this bank is made of: centre, radius and weight, in image fractions.

    A cumulus is a pile of roughly spherical parcels, biggest in the middle of the mass and
    smaller round its edge, and the pile is taller than it is deep at the crown and flat
    underneath. That is the whole recipe, and it is why a chain of circles reads as cloud
    where a polygon with holes in it reads as paper.

    Three runs of them:

      spine   the body of the bank, large and overlapping, along a wavy line a bit below
              the middle. This is what has to be solid, because it is what covers the frame.
      crown   smaller and bunched upward off the spine, where the cauliflower is.
      ends    the left and right thirty per cent, falling away in size, which is the edge
              that actually sweeps across the screen during the reveal and therefore the
              only part of this image anybody looks at closely.
    """
    out = []
    spine = edge(rng, 0.075, 0.50, 2)

    # THE BANK FILLS THE IMAGE, which is not the same as filling the frame and is the
    # mistake worth writing down. The quad RoundIntroSmoke builds is coverHigh -- 3.2 --
    # times the height of the frame, so the texture's middle third is all the player ever
    # sees and the rest is what keeps the quad's own top and bottom edges out of shot. A
    # bank drawn as a polite horizontal cloud a third of the image tall therefore covers a
    # ninth of what it needs to, and the arena shows through it from the first frame.
    #
    # So the body is big: radii around a third of the image height, which at four to one
    # is about seven puffs across.
    n = 30
    for i in range(n):
        u = (i + 0.5) / n
        r = rng.between(0.24, 0.38)
        out.append((u, spine(u) + rng.between(-0.12, 0.12), r, 1.0))

    # The crown, riding on top of the body: smaller, bunched, and what makes a cumulus
    # read as cauliflower rather than as a sausage.
    n = 38
    for i in range(n):
        u = rng.between(0.02, 0.98)
        r = rng.between(0.08, 0.17)
        lift = rng.between(0.20, 0.44)
        out.append((u, spine(u) - lift, r, 0.94))

    # And a flatter skirt under it, which is what gives a cloud a base rather than a bottom.
    n = 20
    for i in range(n):
        u = rng.between(0.02, 0.98)
        r = rng.between(0.14, 0.26)
        out.append((u, spine(u) + rng.between(0.24, 0.50), r * 0.95, 0.88))

    return out


def field(w, h, puffs):
    """The density of the bank at every pixel, as a flat list of floats.

    Each puff contributes a smooth falloff over its own radius and nothing outside it, so
    the cost is the area of the puffs and not the area of the image times their count.
    Squared falloff rather than linear: two overlapping puffs then sum to something flat
    through the join instead of to a ridge along it.
    """
    dens = [0.0] * (w * h)
    aspect = w / float(h)

    for u, v, r, weight in puffs:
        cx, cy = u * w, v * h
        rx, ry = r * h * aspect * 0.42, r * h
        x0, x1 = max(0, int(cx - rx)), min(w, int(cx + rx) + 1)
        y0, y1 = max(0, int(cy - ry)), min(h, int(cy + ry) + 1)
        if x1 <= x0 or y1 <= y0:
            continue
        for y in range(y0, y1):
            dy = (y - cy) / ry
            dy2 = dy * dy
            if dy2 >= 1.0:
                continue
            row = y * w
            for x in range(x0, x1):
                dx = (x - cx) / rx
                d2 = dx * dx + dy2
                if d2 < 1.0:
                    t = 1.0 - d2
                    dens[row + x] += weight * t * t

    return dens


def taper(dens, w, h):
    """Thins the bank towards its two ends, so they are cloud rather than a cut line.

    Only the outer sixth each side, and only to a third of the density: the middle has to
    stay solid, because four of these at four offsets are what covers the frame on the
    first frame of the round.
    """
    run = int(w * 0.16)
    if run <= 0:
        return
    for x in range(run):
        k = 0.34 + 0.66 * (x / float(run)) ** 0.8
        for side in (x, w - 1 - x):
            for y in range(h):
                dens[y * w + side] *= k


def band(seed):
    """The bank, as RGBA bytes: soft-edged, shaded from above, white for the tint."""
    rng = Rng(seed)
    w, h = WIDE, HIGH

    dens = field(w, h, lobes(rng))
    taper(dens, w, h)

    # Density to coverage. The window is what makes the edge soft: below LO there is no
    # cloud, above HI it is solid, and the dozen or so pixels in between are the feather.
    LO, HI = 0.30, 0.78
    alpha = [0.0] * (w * h)
    for i, d in enumerate(dens):
        if d <= LO:
            continue
        t = 1.0 if d >= HI else (d - LO) / (HI - LO)
        alpha[i] = t * t * (3.0 - 2.0 * t)          # smoothstep

    # Where the top of the mass is in each column, for the lighting. One pass, and it is
    # what turns a silhouette into something with a lit crown and a shaded base.
    top = [h] * w
    for x in range(w):
        for y in range(h):
            if alpha[y * w + x] > 0.5:
                top[x] = y
                break

    # SMOOTHED ACROSS COLUMNS, and without this the shading is worse than no shading. The
    # crown line steps by tens of pixels between one column and the next wherever two lobes
    # meet, and the light falls from that line -- so every step became a hard vertical
    # seam running the whole height of the bank. A cloud with vertical seams in it reads as
    # a badly tiled texture, which is the one thing worse than a flat one.
    #
    # Wide, because the thing being smoothed is a lighting direction and not a shape: the
    # silhouette keeps every bit of its detail, only the gradient under it is slow.
    run = max(1, w // 40)
    smooth = [0.0] * w
    acc = sum(top[0:run])
    for x in range(w):
        lo, hi = max(0, x - run), min(w - 1, x + run)
        smooth[x] = sum(top[lo:hi + 1]) / float(hi - lo + 1)
    top = smooth

    px = bytearray(w * h * 4)
    fall = 0.62 * h                                  # how far down the light reaches
    for y in range(h):
        row = y * w
        for x in range(w):
            a = alpha[row + x]
            if a <= 0.004:
                continue

            # Two lights, and the second one is what makes this read as cloud rather than
            # as a soft rectangle.
            #
            # The FIRST is the whole mass: bright along its crown, falling away towards its
            # base. On its own it is a vertical gradient, and a vertical gradient is all the
            # player would ever see -- because the quad RoundIntroSmoke builds is 3.2 times
            # the frame's height, so the frame shows the bank's vertical MIDDLE and nothing
            # of its crown or its base. All the cauliflower was off screen.
            #
            # The SECOND lights every lobe separately, off the density field's own gradient.
            # A parcel of cloud is lit on top and shaded underneath whatever height it is at,
            # so this puts a lit top and a shaded underside on each of the thirty-odd puffs
            # the bank is built from -- which is structure at every height, including the
            # band that is actually in shot.
            deep = (y - top[x]) / fall
            body = 1.0 - 0.34 * min(1.0, max(0.0, deep)) ** 0.85

            up = dens[row + x - w] if y > 0 else dens[row + x]
            down = dens[row + x + w] if y < h - 1 else dens[row + x]
            lobe = (up - down) * 0.5                 # positive where the surface faces up

            lum = body + 0.26 * max(-1.0, min(1.0, lobe * 1.9))

            # And a little rim where the mass thins out, which is what the sun does to the
            # edge of a cloud and what tells one from fog at a glance.
            if a < 0.82:
                lum += 0.14 * (1.0 - abs(a - 0.45) / 0.45) ** 2

            v = int(max(0.0, min(1.0, lum)) * 255)
            i = (row + x) * 4
            px[i] = px[i + 1] = px[i + 2] = v
            px[i + 3] = int(a * 255)

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

    # SOLID means solid, now that there is a feather. It used to count anything over an
    # alpha of 8, which on a soft-edged image counts the feather as cover -- and the
    # feather is exactly the part that does NOT cover. Four layers that each "covered" 70%
    # of the frame by that reckoning would still have shown the arena through them.
    opaque = sum(1 for k in range(3, len(px), 4) if px[k] > 247) / float(WIDE * HIGH)
    if opaque < WANT:
        print("make_intro_smoke: the bank is only %.0f%% opaque; under %.0f%% the four "
              "layers stop covering one another and the arena shows through on the first "
              "frame of the round" % (opaque * 100, WANT * 100))
        return 1

    # And there has to BE a feather. This is the check that replaced the one forbidding it:
    # a bank with no partial alpha in it is the paper cut-out again, whatever else changed.
    soft = sum(1 for k in range(3, len(px), 4) if 8 < px[k] < 247) / float(WIDE * HIGH)
    if soft < 0.02:
        print("make_intro_smoke: only %.1f%% of the bank is a soft edge. Cloud that ends "
              "on a hard line is a cut-out; the reveal reads as paper being pulled aside."
              % (soft * 100))
        return 1

    path = os.path.join(OUT, "fx_intro_smoke.png")
    png(path, WIDE, HIGH, px)
    if not os.path.exists(path + ".meta"):
        with open(path + ".meta", "w") as handle:
            handle.write(META.format(guid=uuid.uuid4().hex))

    print("make_intro_smoke: ok (%dx%d, %.0f%% opaque, %.0f%% soft edge, %d bytes)"
          % (WIDE, HIGH, opaque * 100, soft * 100, os.path.getsize(path)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
