#!/usr/bin/env python3
"""
make_backdrops.py — the parallax layers behind each arena, drawn in code.

A 2.5D game with a PERSPECTIVE camera gets parallax for nothing. The arena cameras are at
fov 30 about seven metres from the fight, so a layer D metres from the camera drifts at
roughly 7/D of the speed the fighters do. Three layers at 22, 50 and 110 metres give 32%,
14% and 6%: a near band that clearly belongs to the arena, a middle one that reads as
distance, and a far one that is almost a painted wall. No scrolling code, no second camera,
no render texture. Put a flat quad at a depth and the maths does it.

What the layers are is the whole question, and docs/00 answers it: FLAT COLOUR and hard
edges, stylised low poly, not painted scenery. A silhouette is exactly that, which is why
these are generated rather than painted -- a ridge line and a row of shapes is a loop, and a
loop is deterministic, so a re-run leaves an empty diff and the file in git is the file the
recipe makes.

Colour does the depth. Each layer is a flat fill, and the further one is the nearer one
lifted towards the arena's own haze: the near band is nearly the silhouette colour, the far
one is most of the way to the sky. That is the oldest depth cue there is and it costs
nothing on a phone, where docs/06 forbids the fog and post-processing that would otherwise
do it.

One rule the quads must obey, because the first build broke it and the preview caught a
pale notch at the bottom of a valley: A LAYER'S HEIGHT IS NOT ITS TEXTURE'S ASPECT. These
images are 4:1, and the width a layer needs is set by how far it has to travel, so taking
the height from the aspect leaves the middle and far quads only 0.78 and 0.65 of a screen
tall. They then stop above the bottom of the frame, and wherever a nearer ridge dips the
sky shows through a hole in the middle of the scenery. Everything below a ridge line here
is flat fill, so stretching a quad vertically costs nothing and changes nothing: size each
one to cover the frame at its own depth and let the texture stretch.

    cd docs/refs && python3 make_backdrops.py
    open docs/refs/vfx/backdrops.html             # the stack, composited, camera panning

Themes, one per arena, each in that arena's own palette:

    mine       underground. No sky: receding rock, pit props, a hanging chain, and one
               far shaft of lit air -- the only light in the deepest layer.
    forest     tree lines going back into mist, over a low hill ridge.
    nordic     mountain ridges and the silhouette of a far fortress on the middle one.
    temple     night desert: dunes, obelisks, and a pyramid holding the far layer.
"""

import json
import math
import os
import struct
import sys
import uuid
import zlib

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "..", "Gulag runners", "Assets", "_Game",
                   "Effects", "Backdrops")

# --------------------------------------------------------------- what the camera makes true
#
# Every number below this line is derived, not chosen, and that is the point: the same
# figures have to hold in three places -- this generator, the browser preview, and the
# editor tool that builds the quads in Unity -- and the first pass had them written out by
# hand in two of them. They drifted within the hour. One of the two said a layer was 4:1 and
# the other put it on a quad that was 1.5:1, so every tree came out stretched to nearly
# three times its height, which looked like bad art rather than like arithmetic.
#
# So: measured facts first, everything else computed from them, and the result written to
# disk for the other two to read.

# Read out of the scene files and SideViewCamera.Place, not remembered. The first draft of
# this had the camera seven metres from the fight and pitched twelve degrees, which is what
# the SCRIPT's defaults say; every scene overrides both, and seven metres instead of
# eighteen would have put the near layer at an 85% drift -- parallax nobody could see.
FOV = 30.0           # fieldOfView, all four arenas
PITCH = 4.0          # pitchDegrees in the scenes; the script's default of 12 is unused

# The framing, and the one number here worth reading twice. The scenes set
# visibleWorldHeight to 10, and that figure is NEVER USED: manualFraming is 0, so
# EffectiveViewHeight returns the fighter's body height over the fraction of the screen
# they must fill. The arenas instance Player 1 and Player 2, both 0.91 m, over 0.22. So the
# camera frames 4.14 metres and stands 7.7 from the fight -- not the 10 and 18.6 the
# inspector appears to say. Taking the visible field would have put every layer at twice its
# proper depth. backdrop_check.py re-derives this from the scenes and the prefabs on every
# run, because it is exactly the kind of fact that is true until somebody retunes a camera.
BODY = 0.91          # Player 1 / Player 2 prefabs, MovementTuning.bodyHeight
BODY_FRACTION = 0.22  # bodyScreenFraction in the arena scenes
VIEW_H = BODY / BODY_FRACTION
# SPLIT SCREEN, and this is the number that was wrong.
#
# Every arena gives each camera a viewportRect of full width by HALF height -- two players,
# one phone, stacked. On a 19.5:9 screen each half is therefore 4.33:1, not the 2.33:1 of
# the whole screen, and the framing does NOT shrink with it: EffectiveViewHeight only
# applies scaleFramingToViewport under manualFraming, which is off, so each half still
# frames 4.14 m of height and 4.14 * 4.33 = 17.9 m of WIDTH.
#
# Sized for 21:9 the layers were 9.6 m of view wide against 17.9 m of frame, and their left
# and right edges stood in shot at the start of every round. Nothing in a single-camera
# measurement could show it: the fault is only there when the screen is cut in two.
#
# 4.5 rather than 4.33 so a taller phone -- where each half is wider still -- has somewhere
# to go.
ASPECT = 4.5
ARENA_W = 32.46      # Forest and Nordic, x -18.12..14.34 -- the widest of the four
ARENA_H = 8.80       # and the tallest, y -2.6..6.2
ZOOM = (0.6, 1.1)    # minZoom..maxZoom; coverage is solved across the whole range

SHAKE = 0.25         # shakeAmplitude, metres, plus the look-ahead and air slack
MARGIN = 1.06        # so a rounding error cannot park an edge inside the frame

TEX_W = 2048       # one tile; docs/06 caps textures at 120 MB and twelve of these is about 8 MB

# Distance from the camera, at zoom 1. These ARE chosen -- the art direction, and the only
# free parameter on this page. Pushed back twice now: 20/46/105, then 28/64/150, now this.
# Further away is a bigger world and a calmer drift -- the Worms look -- and it costs
# nothing in apparent size, because the quads grow with the depth by construction. Only the
# drift changes, and it is now 23%, 9% and 3.5% of the fighters' own apparent speed.
DEPTHS = (("near", 34.0), ("mid", 85.0), ("far", 220.0))

# HOW MUCH OF A LAYER MAY BE ON SCREEN AT ONCE, and the number this file was missing.
#
# Reported as "the parallax is far too narrow, you can see its edges, you can see it is just
# a picture behind". The first thing I did was check the widths, and they were not the
# fault: measured against every arena's real camera borders the quads already ran 16 to 42
# metres WIDER than they need to, and they hold up to a 22:9 screen, which no phone is.
#
# The fault was that you could see the WHOLE PICTURE. At the old depths one frame held 70%
# of the near layer, 82% of the middle and 89% of the far one -- and the far one drifts by
# 5%, so it was a painting pinned behind the arena, both ends of it in shot, not moving.
# Nothing about that reads as distance however wide the quad is.
#
# So a layer now has to be at least three frames wide, which puts a third of it on screen,
# and the way it is paid for is TILING: the quad is widened and the texture repeats across
# it. Pixels per metre stay exactly where they were, memory does not move, and an edge
# cannot exist because there is no edge -- which also makes seamlessness a requirement
# rather than a nicety, and ridge() is built to wrap for that reason.
VISIBLE = 0.33       # at most this much of a layer in one frame

# One tile is about this many frames wide. It is a resolution choice, not a look: TEX_W
# pixels spread over this much frame is what sets how soft the art is on a phone, and 1.3
# keeps it exactly where the old single-tile layers had it.
TILE_SCREENS = 1.3


def camera_distance(zoom=1.0):
    """SideViewCamera.Place, verbatim: the distance that frames VIEW_H metres."""
    return (VIEW_H * zoom * 0.5 * math.cos(math.radians(PITCH))
            / math.tan(math.radians(FOV * 0.5)))


def view(depth):
    """How much world the camera sees at a given distance: (width, height) in metres."""
    high = 2.0 * depth * math.tan(math.radians(FOV * 0.5))
    return high * ASPECT, high


def layers():
    """
    For each layer: how fast it drifts, how big its quad is, and therefore what shape its
    texture has to be.

    Coverage is solved across the zoom range rather than at zoom 1, because the two ends
    pull in opposite directions and neither is the worst case on its own. Zooming IN moves
    the camera closer, so a layer covers more of the frame -- but the arena is then many
    screens wide and the camera travels much further across it, which needs more slack.
    Zooming OUT does the reverse. Sizing at zoom 1 alone leaves an edge showing at one end
    or the other.
    """
    rest = camera_distance()
    out = []

    for name, depth in DEPTHS:
        world_w, world_h = view(depth)

        need_w = need_h = 1.0

        for step in range(21):
            zoom = ZOOM[0] + (ZOOM[1] - ZOOM[0]) * step / 20.0
            # The layer is nailed to the world; zooming moves the CAMERA, so its distance
            # from the layer changes.
            near = depth + camera_distance(zoom) - rest
            see_w, see_h = view(near)
            fight_w, fight_h = view(camera_distance(zoom))

            # How far the camera runs, and what fraction of a frame that is at this depth.
            #
            # TWICE the vertical travel, and that factor is the whole of why the edges of
            # the near and middle layers showed in Arena_Forest. The quad is centred on its
            # anchor, and BackdropBuilder anchors at the BOTTOM of the camera's travel --
            # where the art is composed, because that is where the camera spends a match.
            # So the quad has to reach a full travel ABOVE that anchor and still cover the
            # frame, which is half a frame more again. Sized for a centred anchor it came
            # out at 0.95 of what it needed, which is exactly an edge in shot.
            need_w = max(need_w, 1.0 + max(0.0, ARENA_W - fight_w) / see_w)
            need_h = max(need_h, 1.0 + 2.0 * (max(0.0, ARENA_H - fight_h) + SHAKE) / see_h)

            # And the quad must still cover the frame it is being measured against.
            need_w = max(need_w, see_w / world_w)
            need_h = max(need_h, see_h / world_h)

        # Coverage is the floor, not the answer. A quad wide enough to keep its edges out
        # of shot can still show nearly all of itself at once, which is the thing that read
        # as a painted sheet, so the width is taken up to whatever VISIBLE asks for.
        wide = max(need_w, 1.0 / VISIBLE) * MARGIN
        high = need_h * MARGIN

        # And then it is paid for by repeating one tile rather than by one enormous image.
        tiles = max(1, int(round(wide / TILE_SCREENS)))
        tile_w = wide / tiles

        # The texture must have ONE TILE's shape, or the art is stretched on screen. This
        # used to be the whole quad's shape, which was right while there was one tile.
        px_h = int(round(TEX_W * high / (tile_w * ASPECT) / 16.0)) * 16

        out.append({
            "name": name,
            "depth": round(depth, 3),
            "drift": round(rest / depth, 4),       # of the fighters' apparent speed
            "screens_wide": round(wide, 4),
            "screens_high": round(high, 4),
            "screens_top": round(-(high - 1.0) * 0.5, 4),   # centred on the frame
            # One tile, in frames. Everything drawn is composed against this and not
            # against the whole quad, which is now several tiles of it.
            "tiles": tiles,
            "tile_screens": round(tile_w, 4),
            "visible_share": round(1.0 / wide, 4),   # how much of it is in shot at once
            # what the Unity tool needs, in metres
            "metres_wide": round(wide * world_w, 3),
            "metres_high": round(high * world_h, 3),
            "pixels": [TEX_W, px_h],
        })
    return out


LAYERS = layers()

# The import settings these textures are written with. Two of its numbers carry the tiling
# and are worth knowing before anybody edits this block: wrapU 0 is REPEAT and wrapV 1 is
# CLAMP.
#
# Across: the quad is several tiles wide and the material repeats the texture over it. This
# said CLAMP until the tiling went in, and clamped horizontally a layer is one tile followed
# by its last column of pixels smeared over the rest of the quad -- a worse artefact than
# the stretched single image it replaces, and one that looks like a mistake in the drawing
# rather than in the import, which is the hardest kind to find. ridge() wraps for the same
# reason, and backdrop_check fails if either stops being true.
#
# Up and down: nothing repeats vertically, and clamping there keeps the top row of sky from
# bleeding into the bottom row of ground through the mip chain.
#
# The comment lives here and not in the template because Unity rewrites a .meta in place and
# anything it does not recognise is lost at best.
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
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 1
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
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    mipBias: 0
    wrapU: 0
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
    maxTextureSize: 2048
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


class Rng:
    """A seeded generator of our own, so a Python upgrade cannot redraw the art."""

    def __init__(self, seed):
        self.s = seed & 0x7fffffff

    def next(self):
        self.s = (self.s * 1103515245 + 12345) & 0x7fffffff
        return self.s / 0x7fffffff

    def between(self, a, b):
        return a + (b - a) * self.next()

def png(path, w, h, px):
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


def ridge(rng, amp, base, lumps):
    """
    A horizon, as a sum of a few waves.

    Four octaves, not twenty: docs/00 wants hard edges and flat colour, and a ridge with
    fine noise on it reads as a photograph of a hill rather than as a drawing of one.

    EVERY FREQUENCY IS A WHOLE NUMBER OF WAVES ACROSS THE TILE, which is what makes the
    left edge meet the right one exactly. It used to be whatever the random draw gave,
    multiplied by the theme's lumpiness, and the ridge then arrived at x=1 part way through
    a wave: measured, bg_mine_near and bg_nordic_far ended 7.8 and 7.6 levels away from
    where they started. That was survivable while each layer was one tile wide and the ends
    were off screen. It is not survivable now -- the tile repeats, so the step at the seam
    appears two or three times across the backdrop, at the same height, which is the most
    obviously artificial thing a drawing of a horizon can do.

    Rounding up rather than to nearest, so the lowest octave can never round to zero and
    flatten the whole ridge into a straight line.
    """
    waves = []
    for k in (1, 2, 3.5, 6):
        cycles = max(1, int(math.ceil(rng.between(0.6, 1.6) * k * lumps)))
        waves.append((cycles, rng.between(0, 6.28), amp / (k ** 0.85)))

    def at(x):
        y = base
        for cycles, phase, a in waves:
            y += math.sin(x * cycles * 6.2831853 + phase) * a
        return y
    return at


def across(x0, x1, w):
    """
    The columns from x0 to x1, WRAPPED round the edge of the tile.

    Every shape here used to be clipped to the image instead -- max(0, ..), min(w, ..) --
    which was right when a layer was one tile wide and its ends were off screen. Now the
    tile repeats, so a tree clipped in half at the right edge is a tree sawn in half in the
    middle of the backdrop, two or three times over. Wrapping costs one modulo and means a
    shape that runs off one side arrives on the other, which is what a repeating world does.
    """
    for x in range(x0, x1):
        yield x % w


def fill_below(px, w, h, line, colour, alpha=255):
    r, g, b = colour
    for x in range(w):
        top = max(0, min(h, int(line(x / w))))
        for y in range(top, h):
            i = (y * w + x) * 4
            px[i:i + 4] = bytes((r, g, b, alpha))


def column(px, w, h, x0, wide, top, colour, cap=0):
    """A vertical shape: an obelisk, a pit prop, a tower."""
    r, g, b = colour
    for x in across(x0, x0 + wide, w):
        for y in range(max(0, top), h):
            i = (y * w + x) * 4
            px[i:i + 4] = bytes((r, g, b, 255))
    if cap:
        for x in across(x0 - cap, x0 + wide + cap, w):
            for y in range(max(0, top - cap), max(0, top)):
                i = (y * w + x) * 4
                px[i:i + 4] = bytes((r, g, b, 255))


def prop(px, w, h, x0, span, top, thick, colour):
    """
    A pit prop: two legs and a lintel over them.

    A single post reads as a scratch on the image. The lintel is what makes the shape say
    MINE, because it is the thing a mine has and a forest does not -- the roof is being held
    up. Two strokes more than a post, and the layer stops looking like tally marks.
    """
    leg = max(2, int(w * 0.0035))
    beam = max(2, thick)
    column(px, w, h, x0, leg, top + beam, colour)
    column(px, w, h, x0 + span - leg, leg, top + beam, colour)
    for x in across(x0 - leg, x0 + span + leg, w):
        for y in range(max(0, top), min(h, top + beam)):
            i = (y * w + x) * 4
            px[i:i + 4] = bytes(tuple(colour) + (255,))


def shaft(px, w, h, x0, narrow, broad, top, bottom, colour):
    """
    A shaft of lit air: a flat trapezoid, narrow at the roof and broad at the floor.

    The first version was a soft gradient, because that is what light looks like. Two things
    were wrong with it. docs/00 asks for flat colour and hard edges, and a feathered glow is
    the one thing in the whole backdrop that is neither. And the soft edge put partial alpha
    in the image, which forces the layer to be alpha-BLENDED; blended geometry draws after
    the opaque pass, so the backdrop would have drawn on top of the arena instead of behind
    it. A hard trapezoid is on brief and renders as plain opaque geometry that the arena
    occludes for nothing.
    """
    for y in range(max(0, top), min(h, bottom)):
        t = (y - top) / float(max(1, bottom - top))
        half = int((narrow + (broad - narrow) * t) * 0.5)
        for x in across(x0 - half, x0 + half, w):
            i = (y * w + x) * 4
            px[i:i + 4] = bytes(tuple(colour) + (255,))


def tree(px, w, h, x0, wide, top, colour):
    """A conifer: a triangle on a trunk. Three strokes, no leaves."""
    r, g, b = colour
    half = wide // 2
    for y in range(max(0, top), h):
        t = (y - top) / max(1, h - top)
        half_now = int(half * min(1.0, t * 2.4))
        for x in across(x0 - half_now, x0 + half_now + 1, w):
            i = (y * w + x) * 4
            px[i:i + 4] = bytes((r, g, b, 255))


def obelisk(px, w, h, x0, wide, top, colour):
    """
    A shaft with a pointed cap, which is the thing that makes it Egyptian.

    Capped with a wider flat block it read as a street lamp -- the same silhouette a lamp
    post has, so the eye took the reading it already knew. The point is the whole difference
    and it is four lines of it.
    """
    cap = int(wide * 1.6)
    column(px, w, h, x0, wide, top + cap, colour)
    pyramid(px, w, h, x0 + wide // 2, wide * 2, top, colour)


def pyramid(px, w, h, x0, wide, top, colour):
    r, g, b = colour
    half = wide // 2
    for y in range(max(0, top), h):
        t = (y - top) / max(1, h - top)
        half_now = int(half * t)
        for x in across(x0 - half_now, x0 + half_now + 1, w):
            i = (y * w + x) * 4
            px[i:i + 4] = bytes((r, g, b, 255))


# --------------------------------------------------------------------- the four arenas

def lerp3(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def near_bands(px, w, h, rng, row, high, colour, toward, lumps, index, on_band=None):
    """
    Two more horizons below the layer's own, each a step closer and a step different in
    tone. Drawn LAST, so each one covers the feet of whatever stands behind it.

    This is what the layers were missing. Measured across the twelve images, seven of them
    had a value range of exactly zero -- the whole mine, the near and far of the nordic, the
    near and middle of the temple. One flat tone spread over four hundred metres is a sheet
    of paper whatever shape its top edge is, and no amount of parallax makes a sheet of paper
    look like a valley. Three bands of nearly the same colour do, because that is what
    distance actually does to a landscape: it sorts it into steps.

    Nearly the same colour, and flat, and hard edged. Not a gradient -- docs/00 is explicit,
    and a gradient here would also put partial values into an image that has to stay cutout.

    Which way the tone steps is the same question as the layer order: with a sky, closer is
    less hazed and therefore darker; underground, closer is nearer the lamps and brighter.
    The caller passes the target, so this never has to know which arena it is drawing.
    """
    for k, (drop, scale, mix) in enumerate(((0.085, 0.70, 0.24), (0.185, 0.50, 0.44))):
        base = row(0.66 - 0.155 * index + drop)
        amp = high((0.125, 0.088, 0.056)[index] * scale)
        line = ridge(rng, amp, base, lumps * (1.0, 0.7, 0.45)[index] * (1.6 + k))
        tone = lerp3(colour, toward, mix)
        fill_below(px, w, h, line, tone)

        # And whatever grows on this band, planted after it is filled so it stands on it
        # rather than behind it. Without this the forest was a single row of conifers with
        # two bare hills in front, which from the arena read as a serrated fence: trees
        # cut off at the ankles by the hill they are supposed to be standing on.
        if on_band is not None:
            on_band(k, line, tone)


# (name, silhouette colour, the haze each layer is lifted towards, lumpiness, has a sky)
#
# The gap between the two is the ONLY thing separating the three layers, so a dark arena
# needs a wider gap than a bright one, not a narrower one. The first pass gave the mine and
# the temple hazes barely lighter than their ink, reasoning that a night scene is dark --
# and all three layers came out the same value, which is a flat wall with no parallax in it
# whatever the depths say. Dark means the whole range sits low, not that there is no range.
#
# THE LAST FLAG IS WHICH WAY THE RANGE RUNS, and the mine had it backwards. Aerial
# perspective lifts distance towards the sky because the air between you and it is lit by
# the sky. Underground there is no sky to light it, so distance does the opposite: it falls
# away into black, and the only far thing you can see is a thing that is itself lit. The
# mine was measured going 25.9 -> 38.9 -> 52.2 with depth, getting steadily brighter the
# deeper in it went, which is why it read as a painted wall rather than as a tunnel.
#
# So the mine keeps the same range and runs it the other way: the near band is the brightest
# thing (it is in the lamps' reach, like the arena), and the far one is nearly out. Its two
# lit shafts then stop being a detail and become the only light in the deepest layer, which
# is what they were always described as. The night desert keeps a sky -- a moon is a sky --
# and still lifts.
THEMES = {
    "mine":    ((0.07, 0.07, 0.09), (0.27, 0.24, 0.22), 1.0, False),  # lamp-lit dust
    "forest":  ((0.05, 0.09, 0.08), (0.36, 0.45, 0.47), 1.4, True),   # morning mist
    "nordic":  ((0.10, 0.12, 0.17), (0.44, 0.51, 0.61), 2.2, True),   # cold daylight
    "temple":  ((0.05, 0.04, 0.08), (0.44, 0.36, 0.52), 0.7, True),   # moonlit air
}

def frame(layer, w, h):
    """
    Three rulers that measure in FRAME fractions rather than in texture fractions.

    Every layer's texture is a different shape and none of them fills the frame exactly, so
    a fraction of the IMAGE lands somewhere different on screen for each one. Composing in
    frame fractions is what lets one piece of drawing code serve all three and still have
    them line up: 0.5 is the middle of what the player sees, in all of them.

      row(d)    the pixel row at a fraction d down the frame
      tall(p)   a height, as a fraction of the frame's height
      wide(p)   a width, ALSO as a fraction of the frame's HEIGHT

    Width sharing height's unit is what makes a shape's proportions legible in the source. A
    tree 0.07 across and 0.21 high is one to three and reads as one to three on every layer.
    Written as a fraction of the texture instead, the same number is a different shape on
    each one, which is how the mine first came out with pit props wider than they were tall.
    """
    # The TILE's span, not the quad's. The image being drawn is one tile; the quad is two
    # or three of them side by side. Measuring against the quad would draw every tree at a
    # third of its proper width and then repeat it, so the layer would read as a hedge.
    cover, top = layer["screens_high"], layer["screens_top"]
    span = layer["tile_screens"]
    return (lambda d: (d - top) / cover * h,
            lambda p: int(p / cover * h),
            lambda p: max(1, int(p / (span * ASPECT) * w)))


def draw(theme, index, w, h):
    """One layer of one arena. Index 0 is nearest, 2 is furthest."""
    name = DEPTHS[index][0]
    ink, haze, lumps, sky = THEMES[theme]
    ink = tuple(int(c * 255) for c in ink)
    haze = tuple(int(c * 255) for c in haze)

    # Further is more washed out: the near band keeps its ink, the far one is most of
    # the way to the sky. This is the whole depth cue and it costs nothing. With no sky to
    # do the washing out, the same range runs the other way and distance goes dark.
    steps = (0.20, 0.48, 0.78) if sky else (0.78, 0.46, 0.14)
    colour = lerp3(ink, haze, steps[index])

    rng = Rng(hash((theme, name)) & 0x7fffffff)
    px = bytearray(w * h * 4)
    row, high, wide = frame(LAYERS[index], w, h)

    # What, if anything, grows on the nearer bands. Only the forest wants it so far; a
    # fortress stands on one hill and a pyramid on one horizon, by construction.
    plant = None

    # Further layers sit higher in the frame and are calmer: distance flattens a landscape,
    # and a far ridge has to clear the near one to be seen at all. The arena itself covers
    # the bottom third or so, which is why the near ridge sits as low as it does -- most of
    # that layer is never seen, and what shows of it should read as the far side of the pit.
    base = row(0.66 - 0.155 * index)
    amp = high((0.125, 0.088, 0.056)[index])
    line = ridge(rng, amp, base, lumps * (1.0, 0.7, 0.45)[index])

    if theme == "mine":
        # No sky down here. The far layer is a shaft of lit air, which is the only light in
        # it, so it reads as depth rather than as a wall painted a lighter grey.
        fill_below(px, w, h, line, colour)
        if index == 2:
            # Two shafts, the second dimmer, so the deepest layer has somewhere to be.
            lit = lerp3(colour, (178, 148, 92), 0.42)
            dim = lerp3(colour, (178, 148, 92), 0.22)
            shaft(px, w, h, int(w * 0.63), wide(0.055), wide(0.175),
                  int(row(-0.05)), int(row(0.52)), lit)
            shaft(px, w, h, int(w * 0.24), wide(0.040), wide(0.125),
                  int(row(-0.05)), int(row(0.44)), dim)
        else:
            def props(on, tone, count, mix):
                for i in range(count):
                    x = int(w * (i + 0.5) / count + rng.between(-90, 90))
                    top = int(on(x / w) - high(rng.between(0.23, 0.33)))
                    prop(px, w, h, x, wide(0.115), top, high(0.016),
                         lerp3(tone, ink, mix))

            props(line, colour, (5, 3)[index], 0.55)
            # Timbering carries on over the nearer rises too. One row of it against two
            # bare humps is a photograph of a mine; three rows of it is a mine.
            plant = lambda k, on, tone: props(on, tone, (3, 2)[index] - k, 0.5 - 0.1 * k)

    elif theme == "forest":
        fill_below(px, w, h, line, colour)

        def conifers(on, tone, count, scale, mix):
            """A stand of trees along one horizon."""
            for i in range(count):
                x = int(w * (i + rng.between(0.15, 0.85)) / count)
                # Conifers are not a picket fence: the spread has to be wide enough that
                # the tops make a line of their own, or the layer reads as one serrated
                # block. Cubed rather than raised to 1.3, so most of a stand is short and
                # a few run away above it -- that difference is the whole silhouette.
                up = rng.between(0.10, 1.0) ** 3 * 0.72 + 0.16
                top = int(on(x / w) - high(up * scale))
                tree(px, w, h, x, wide((0.145, 0.120, 0.098)[index]), top,
                     lerp3(tone, ink, mix))

        conifers(line, colour, (16, 12, 8)[index], (1, 0.75, 0.5)[index], 0.55)
        plant = lambda k, on, tone: conifers(
            on, tone, (11, 8, 5)[index] - k * 2,
            (1.15, 0.85, 0.58)[index] * (1.0 + 0.2 * k), 0.5 - 0.1 * k)

    elif theme == "nordic":
        fill_below(px, w, h, line, colour)
        if index == 1:
            # A fortress on the middle ridge: five towers and a wall between them.
            keep = lerp3(colour, ink, 0.45)
            span = wide(0.60)
            x0 = int(w * 0.46)
            wall = int(line((x0 + span * 0.5) / w) - high(0.082))
            for x in across(x0, x0 + span, w):
                for y in range(wall, h):
                    i = (y * w + x) * 4
                    px[i:i + 4] = bytes(keep + (255,))
            for t in range(5):
                tx = x0 + int(span * t / 4) - wide(0.030)
                column(px, w, h, tx, wide(0.060), wall - high(0.068), keep,
                       cap=wide(0.012))

    elif theme == "temple":
        fill_below(px, w, h, line, colour)
        if index == 2:
            pyramid(px, w, h, int(w * 0.34), wide(1.70), int(row(0.09)),
                    lerp3(colour, ink, 0.60))
        else:
            # Wide enough to be a stone. At 0.028 of a frame these were two pixels across
            # on the far layer and read as scratches on the image rather than as anything
            # standing in the desert -- the same fault the mine's pit props are written up
            # for. An obelisk is a narrow thing, but it is a narrow BUILDING.
            def stones(on, tone, count, mix):
                for i in range(count):
                    x = int(w * (i + 0.5) / count + rng.between(-90, 90))
                    top = int(on(x / w) - high(rng.between(0.20, 0.32)))
                    obelisk(px, w, h, x, wide(0.055), top, lerp3(tone, ink, mix))

            stones(line, colour, (5, 3)[index], 0.55)
            plant = lambda k, on, tone: stones(on, tone, (3, 2)[index] - k, 0.45 - 0.1 * k)

    # Last, so the near bands occlude the feet of everything standing behind them.
    near_bands(px, w, h, rng, row, high, colour, ink if sky else haze, lumps, index, plant)

    return px


def main(argv):
    os.makedirs(OUT, exist_ok=True)
    made = []

    for theme in THEMES:
        for i, layer in enumerate(LAYERS):
            w, h = layer["pixels"]
            name = "bg_%s_%s" % (theme, layer["name"])
            px = draw(theme, i, w, h)

            lit = sum(1 for k in range(3, len(px), 4) if px[k] > 8) / float(w * h)
            if not 0.05 < lit < 0.95:
                print("make_backdrops: %s covers %.0f%% of its image, which is a blank or "
                      "a solid rectangle rather than a horizon" % (name, lit * 100))
                return 1

            # Every pixel must be either fully there or not there at all. A backdrop with
            # partial alpha in it has to be alpha-BLENDED, and blended geometry is drawn
            # after the opaque pass -- so the layer would come out in FRONT of the arena,
            # over the fighters, instead of behind them. Cutout keeps it ordinary opaque
            # geometry that the level occludes for free, which is also what docs/00 asks
            # the art to look like: flat colour, hard edges, no feathering.
            soft = sum(1 for k in range(3, len(px), 4) if 8 < px[k] < 247)
            if soft > 0:
                print("make_backdrops: %s has %d pixel(s) at partial alpha. The layers are "
                      "drawn as cutout so the arena can occlude them; anything feathered "
                      "forces alpha blending, and the backdrop then draws over the fight."
                      % (name, soft))
                return 1

            path = os.path.join(OUT, name + ".png")
            size = png(path, w, h, px)
            meta = path + ".meta"
            if not os.path.exists(meta):
                with open(meta, "w") as handle:
                    handle.write(META.format(guid=uuid.uuid4().hex))
            made.append((name, w, h, size, lit))

    # The table the browser preview and the Unity tool both read, so that none of the three
    # can hold a figure the other two have moved on from.
    camera = {"fov": FOV, "pitch": PITCH, "view_height": VIEW_H,
              "rest_distance": round(camera_distance(), 4),
              "aspect": round(ASPECT, 4)}
    with open(os.path.join(OUT, "layers.json"), "w") as handle:
        json.dump({"camera": camera, "layers": LAYERS}, handle, indent=2)
    if not os.path.exists(os.path.join(OUT, "layers.json.meta")):
        with open(os.path.join(OUT, "layers.json.meta"), "w") as handle:
            handle.write("fileFormatVersion: 2\nguid: %s\nTextAssetImporter:\n"
                         "  externalObjects: {}\n  userData:\n  assetBundleName:\n"
                         "  assetBundleVariant:\n" % uuid.uuid4().hex)
    with open(os.path.join(HERE, "vfx", "backdrop_layers.js"), "w") as handle:
        # The camera block goes out too. Both previews had been carrying their own copies of
        # fov, pitch and aspect, and backdrop_ingame.html's copy said the screen was 19.5/9
        # when a split-screen half is twice that -- so it drew a frame half as wide as the
        # game's, which is the one error that hides an edge instead of showing one. Two
        # copies of a measurement is one copy too many; this page has said so since it was
        # written and then kept a third.
        handle.write("// Written by docs/refs/make_backdrops.py -- do not edit by hand.\n"
                     "const LAYERS = %s;\n"
                     "const LAYERS_CAMERA = %s;\n"
                     "const LAYERS_ASPECT = %s;\n"
                     % (json.dumps(LAYERS, indent=2),
                        json.dumps(camera, indent=2),
                        json.dumps(ASPECT)))

    for name, w, h, size, lit in made:
        print("  %-22s %4dx%-4d %7d bytes  %3.0f%% of the frame"
              % (name, w, h, size, lit * 100))

    print("make_backdrops: ok (%d layer(s) in Assets/_Game/Effects/Backdrops)" % len(made))
    for layer in LAYERS:
        print("  %-5s %5.0f m out, drifts at %4.0f%%, quad %.1f x %.1f m"
              % (layer["name"], layer["depth"], layer["drift"] * 100,
                 layer["metres_wide"], layer["metres_high"]))
    print("make_backdrops: to see them, open docs/refs/vfx/backdrops.html, which composites "
          "the three layers at the depths the game puts them and pans the camera across")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
