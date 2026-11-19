#!/usr/bin/env python3
"""
backdrop_check.py — the parallax layers must be built for the camera the scenes actually have.

The backdrop is pure geometry. Three quads sit at fixed depths and the perspective camera
makes them drift, which is why there is no code to go wrong: a layer 58 m from a camera
18.6 m from the fight slides at 18.6/58 = 32% of the fighters' speed, and that is the whole
mechanism.

It is also the whole failure mode. Every depth, every quad size and every texture SHAPE is
derived from three camera numbers, and the first draft of make_backdrops.py took them from
SideViewCamera's field DEFAULTS -- a camera seven metres out, pitched twelve degrees. Every
scene overrides both. The real camera is 18.6 m out and pitched four, so the near layer
would have drifted at 85% instead of 32%: parallax nobody could see, on quads that did not
cover the frame. Nothing would have thrown. The backdrop would simply have been wrong, and
wrong in a way that reads as bad art rather than as a stale constant.

So: whenever the scenes and make_backdrops.py disagree about the camera, say so. And since
the texture shapes fall out of those same numbers, check that the images on disk are the
ones the current table asks for -- a half-finished re-run leaves the two out of step.

Run: python3 backdrop_check.py
"""

import json
import math
import os
import re
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.join(HERE, "..", "..", "Gulag runners", "Assets")
SCENES = os.path.join(ASSETS, "Scenes")
BACKDROPS = os.path.join(ASSETS, "_Game", "Effects", "Backdrops")
BUILDER = os.path.join(ASSETS, "_Game", "Editor", "BackdropBuilder.cs")

# How far the camera may be from where the art thinks it is before the drift is visibly
# wrong. Half a metre out of eighteen is well under one percent of the drift rate.
SLACK = 0.5


def read(path):
    with open(path, "r", encoding="utf-8", errors="replace") as handle:
        return handle.read()


def png_size(path):
    with open(path, "rb") as handle:
        head = handle.read(26)
    if head[:8] != b"\x89PNG\r\n\x1a\n":
        return None
    return struct.unpack(">II", head[16:24])


def seam(path):
    """
    How far a layer's right-hand edge is from its left-hand one, in levels per channel.

    Zero means it joins up with itself and can be repeated without a visible step. This
    only became a requirement when the layers started tiling: before, each layer was one
    image and its ends were off screen, so bg_mine_near ending 7.8 levels away from where
    it started cost nothing. Drawn three times across a quad it costs three identical steps
    at the same height, which is the most obviously artificial thing a horizon can do.

    Decoded by hand because the whole of docs/refs runs on the standard library.
    """
    try:
        raw = open(path, "rb").read()
        wide, high = struct.unpack(">II", raw[16:24])
        kind = raw[25]
        data = b""
        at = 8
        while at < len(raw):
            size = struct.unpack(">I", raw[at:at + 4])[0]
            if raw[at + 4:at + 8] == b"IDAT":
                data += raw[at + 8:at + 8 + size]
            at += 12 + size

        import zlib
        flat = zlib.decompress(data)
        chans = {0: 1, 2: 3, 3: 1, 4: 2, 6: 4}[kind]
        stride = wide * chans
        out = bytearray()
        prev = bytearray(stride)
        pos = 0
        for _ in range(high):
            how = flat[pos]
            pos += 1
            row = bytearray(flat[pos:pos + stride])
            pos += stride
            for x in range(stride):
                a = row[x - chans] if x >= chans else 0
                b = prev[x]
                c = prev[x - chans] if x >= chans else 0
                if how == 1:
                    row[x] = (row[x] + a) & 255
                elif how == 2:
                    row[x] = (row[x] + b) & 255
                elif how == 3:
                    row[x] = (row[x] + (a + b) // 2) & 255
                elif how == 4:
                    guess = a + b - c
                    da, db, dc = abs(guess - a), abs(guess - b), abs(guess - c)
                    row[x] = (row[x] + (a if da <= db and da <= dc
                                        else (b if db <= dc else c))) & 255
            out += row
            prev = row

        total = 0
        for y in range(high):
            left = (y * wide) * chans
            right = (y * wide + wide - 1) * chans
            total += sum(abs(out[left + c] - out[right + c]) for c in range(chans))
        return total / float(high * chans)
    except Exception:
        return None


def themes_in_builder():
    """The scene-to-palette table BackdropBuilder holds, so the two lists cannot drift."""
    if not os.path.exists(BUILDER):
        return {}
    pairs = re.findall(r'\{\s*"([^"]+)"\s*,\s*"([^"]+)"\s*\}', read(BUILDER))
    return dict(pairs)


def body_height(scene_text):
    """
    The height of the fighter this scene's camera frames against.

    This matters more than it looks. With manualFraming off -- which is how every arena is
    set -- the whole framing is bodyHeight / bodyScreenFraction, so the body height decides
    the camera distance and therefore every depth in the backdrop. It is not in the scene:
    MovementTuning is a plain serializable class living on the player PREFAB, and the scene
    only references it. Reading the SCRIPT's default instead gives 1.8 where the arenas
    actually use 0.91, which is a camera at twice the distance and every layer at twice the
    depth it should be.
    """
    found = []
    prefabs = os.path.join(ASSETS, "Prefabs")
    if not os.path.isdir(prefabs):
        return None

    for name in sorted(os.listdir(prefabs)):
        if not name.endswith(".prefab"):
            continue
        meta = os.path.join(prefabs, name + ".meta")
        if not os.path.exists(meta):
            continue
        guid = re.search(r"guid: ([0-9a-f]{32})", read(meta))
        if not guid or guid.group(1) not in scene_text:
            continue
        high = re.search(r"\n    bodyHeight: ([-\d.]+)", read(os.path.join(prefabs, name)))
        if high:
            found.append((name, float(high.group(1))))

    if not found:
        return None
    return found


def camera_in_scene(text):
    """
    The framing a scene's SideViewCamera actually has.

    Taken from the first component that carries all three fields together. A scene holds two
    rigs (one per split-screen half) with identical framing, so the first is enough.
    """
    for block in text.split("--- !u!"):
        fov = re.search(r"\n  fieldOfView: ([-\d.]+)", block)
        pitch = re.search(r"\n  pitchDegrees: ([-\d.]+)", block)
        high = re.search(r"\n  visibleWorldHeight: ([-\d.]+)", block)
        manual = re.search(r"\n  manualFraming: (\d)", block)
        body = re.search(r"\n  framingBodyHeight: ([-\d.]+)", block)
        part = re.search(r"\n  bodyScreenFraction: ([-\d.]+)", block)
        if fov and pitch and high:
            return {
                "fov": float(fov.group(1)),
                "pitch": float(pitch.group(1)),
                "view_height": float(high.group(1)),
                "manual": manual.group(1) == "1" if manual else True,
                "body": float(body.group(1)) if body else 0.0,
                "fraction": float(part.group(1)) if part else 0.18,
            }
    return None


def framing(cam, body):
    """SideViewCamera.EffectiveViewHeight, for a camera standing still at full viewport."""
    if cam["manual"]:
        return cam["view_height"]
    if cam["body"] > 0.01:
        return cam["body"] / max(0.02, cam["fraction"])
    return body / max(0.02, cam["fraction"])


def distance(cam, body):
    """SideViewCamera.Place: the distance that frames EffectiveViewHeight metres."""
    return (framing(cam, body) * 0.5 * math.cos(math.radians(cam["pitch"]))
            / math.tan(math.radians(cam["fov"] * 0.5)))


def viewport_aspect(text):
    """
    The widest shape a camera in this scene actually renders into.

    This is the number that broke the backdrop and that no single-camera measurement could
    have shown. Every arena gives each of its two cameras a viewportRect of full width by
    HALF height -- two players on one phone -- so each half is 4.33:1 on a 19.5:9 screen,
    not the 2.33:1 of the whole screen. The framing does not shrink to match: a half still
    frames 4.14 m of height, and therefore nearly eighteen metres of WIDTH.

    Sized for the full screen, the layers stood a third narrower than the frame and their
    edges were in shot at the start of every round.
    """
    widest = 0.0
    for blk in text.split("--- !u!"):
        if "viewportRect" not in blk:
            continue
        m = re.search(r"viewportRect:\n(?:\s+serializedVersion: \d+\n)?"
                      r"\s+x: [-\d.e]+\n\s+y: [-\d.e]+\n"
                      r"\s+width: ([-\d.e]+)\n\s+height: ([-\d.e]+)", blk)
        if not m:
            continue
        w, h = float(m.group(1)), float(m.group(2))
        if w <= 0 or h <= 0:
            continue
        widest = max(widest, PHONE * w / h)
    return widest


# The shape of the screen the game is built for, before it is cut into viewports.
PHONE = 19.5 / 9.0


def main():
    table_path = os.path.join(BACKDROPS, "layers.json")
    if not os.path.exists(table_path):
        print("backdrop_check: no layers.json in Assets/_Game/Effects/Backdrops. Run "
              "docs/refs/make_backdrops.py, which writes it beside the artwork.")
        return 1

    with open(table_path) as handle:
        table = json.load(handle)

    built_for = table["camera"]["rest_distance"]
    themes = themes_in_builder()
    problems = []
    checked = 0

    # 1. The scenes and the artwork must agree about where the camera stands.
    for scene_name, theme in sorted(themes.items()):
        path = os.path.join(SCENES, scene_name + ".unity")
        if not os.path.exists(path):
            problems.append("BackdropBuilder gives %s the %s palette, but there is no such "
                            "scene" % (scene_name, theme))
            continue

        text = read(path)
        cam = camera_in_scene(text)
        if cam is None:
            problems.append("%s has no SideViewCamera framing in it, so the backdrop has "
                            "nothing to be built against" % scene_name)
            continue

        fighters = body_height(text)
        if not cam["manual"] and cam["body"] <= 0.01 and not fighters:
            problems.append(
                "%s frames against the fighter's own height, but no player prefab it "
                "instances carries a bodyHeight -- so there is no way to tell from the files "
                "how far back the camera stands" % scene_name)
            continue

        # If the two fighters are different heights the camera frames against whichever one
        # it follows, and the backdrop cannot be right for both.
        if fighters and len({round(h, 3) for _, h in fighters}) > 1:
            problems.append(
                "%s instances fighters of different heights (%s). The camera frames against "
                "the one it follows, so its distance -- and every backdrop depth -- depends "
                "on which player you are" % (scene_name,
                                             ", ".join("%s %.2f m" % f for f in fighters)))

        body = fighters[0][1] if fighters else 0.0
        checked += 1
        here = distance(cam, body)
        if abs(here - built_for) > SLACK:
            problems.append(
                "%s frames %.2f m at fov %g and %g deg of pitch, putting the camera %.2f m "
                "from the fight. The backdrop art was built for %.2f m, so its near layer "
                "would drift at %.0f%% instead of %.0f%% and its quads would not cover the "
                "frame. Update the measured block at the top of make_backdrops.py and re-run "
                "it" % (scene_name, framing(cam, body), cam["fov"], cam["pitch"], here,
                        built_for, here / table["layers"][0]["depth"] * 100,
                        built_for / table["layers"][0]["depth"] * 100))

    # 2. The images on disk must be the ones this table asks for. A texture at the wrong
    #    shape is stretched on its quad, which looks like bad drawing, not a stale file.
    for theme in sorted(set(themes.values())):
        for layer in table["layers"]:
            name = "bg_%s_%s.png" % (theme, layer["name"])
            path = os.path.join(BACKDROPS, name)
            if not os.path.exists(path):
                problems.append("%s is missing, so the %s arena has no %s layer"
                                % (name, theme, layer["name"]))
                continue
            size = png_size(path)
            want = tuple(layer["pixels"])
            if size != want:
                problems.append(
                    "%s is %dx%d but the table asks for %dx%d. The quad's shape comes from "
                    "the camera, and the texture has to match it or the art is stretched on "
                    "screen. Re-run make_backdrops.py" % ((name,) + size + want))

    # 3. The layers must be on a shader that does not apply fog, and the arenas make that
    #    non-negotiable: every one of them runs linear fog that is fully opaque somewhere
    #    between 34 and 46 m, and two of the three layers sit past that. On a stock shader
    #    they come out 100% fog colour -- the backdrop collapses to one flat wall and the
    #    parallax the depths were chosen for has nothing left to show. URP takes its fog
    #    keyword from the scene globally, so no material setting can opt out; only a shader
    #    with no fog in it can.
    shader = os.path.join(ASSETS, "_Game", "Shaders", "Backdrop.shader")
    if not os.path.exists(shader):
        problems.append("Assets/_Game/Shaders/Backdrop.shader is gone. Without it the "
                        "layers fall back to a stock unlit shader, and the arenas' fog "
                        "swallows everything past about 40 m")
    else:
        # Comments stripped first. The shader explains at length WHY it applies no fog, and
        # naming the two things it avoids put both of them in the file -- so the first
        # version of this rule fired on the very file it was written to approve.
        body = re.sub(r"//[^\n]*", "", read(shader))
        if "MixFog" in body or "multi_compile_fog" in body:
            problems.append("Backdrop.shader has fog in it (MixFog / multi_compile_fog). "
                            "The middle and far layers sit at 55 and 128 m, well past the "
                            "34-46 m where every arena's fog is already solid, so they "
                            "would render as flat fog colour")
        if os.path.exists(BUILDER) and "Gulag Runners / Backdrop" not in read(BUILDER):
            problems.append("BackdropBuilder no longer asks for the \"Gulag Runners / "
                            "Backdrop\" shader. Whatever it builds on instead will take the "
                            "scene's fog, which is solid long before the far layer")

    # 4. The art has to be sized for the shape a camera RENDERS INTO, not for the screen.
    for scene_name in sorted(themes):
        path = os.path.join(SCENES, scene_name + ".unity")
        if not os.path.exists(path):
            continue
        shape = viewport_aspect(read(path))
        if shape <= 0:
            continue
        if shape > table["camera"]["aspect"] + 0.05:
            problems.append(
                "%s renders each camera into a %.2f:1 viewport -- the screen is cut in two, "
                "so a half is that much wider than it is tall -- but the backdrop art is "
                "built for %.2f:1. The layers will be too narrow and their edges will be in "
                "shot. Raise ASPECT in make_backdrops.py and re-run it"
                % (scene_name, shape, table["camera"]["aspect"]))


    # 5. A LAYER MUST NOT SHOW ALL OF ITSELF AT ONCE, which is the whole reason the layers
    # are tiled. Reported as "the parallax is too narrow, you can see its edges, you can see
    # it is just a picture behind" -- and the widths turned out to be fine, running 16 to 42
    # metres wider than any arena needs. What was wrong was that one frame held 70%, 82% and
    # 89% of the three layers. Both ends of the picture in shot, and the far one drifting at
    # 5%, is a painting pinned to the back wall whatever its dimensions are.
    for layer in table["layers"]:
        see = 2.0 * layer["depth"] * math.tan(math.radians(table["camera"]["fov"] / 2.0)) \
            * table["camera"]["aspect"]
        share = see / layer["metres_wide"]
        if share > 0.45:
            problems.append(
                "the %s layer puts %.0f%% of itself in one frame (%.0f m of %.0f m). Over "
                "about 45%% a layer reads as one picture hung behind the arena rather than "
                "as distance. Lower VISIBLE in make_backdrops.py and re-run it"
                % (layer["name"], share * 100.0, see, layer["metres_wide"]))

    # 6. AND IT MUST ACTUALLY TILE, in the art, in the table and in the import. Three things
    # have to agree or the repeat is a defect instead of a feature: the table has to ask for
    # more than one tile, the image has to join up with itself, and the importer has to be
    # set to Repeat. Clamped, a "tiled" layer is one tile and then its last column of pixels
    # smeared across the rest of the quad.
    for layer in table["layers"]:
        if layer.get("tiles", 1) < 2:
            problems.append(
                "the %s layer is a single tile, so its width has to be bought in pixels "
                "rather than in repeats. Check TILE_SCREENS in make_backdrops.py"
                % layer["name"])

    for theme in sorted(set(themes.values())):
        for layer in table["layers"]:
            art = os.path.join(BACKDROPS, "bg_%s_%s.png" % (theme, layer["name"]))
            if not os.path.exists(art):
                continue

            meta = read(art + ".meta")
            if meta and re.search(r"wrapU: [^0]", meta):
                problems.append(
                    "bg_%s_%s.png is not imported with Wrap Mode Repeat across, so the "
                    "repeat that gives it its width would come out as one tile and a smear"
                    % (theme, layer["name"]))

            step = seam(art)
            if step is not None and step > 2.0:
                problems.append(
                    "bg_%s_%s.png does not join up with itself: its left and right edges "
                    "differ by %.1f levels per channel. It is drawn %d times across the "
                    "quad, so that step appears %d times in shot. Every frequency in "
                    "ridge() has to be a whole number of waves across the tile"
                    % (theme, layer["name"], step, layer.get("tiles", 1),
                       layer.get("tiles", 1)))

    if problems:
        for line in problems:
            print("backdrop_check: " + line)
        print("backdrop_check: %d problem(s)" % len(problems))
        return 1

    print("backdrop_check: ok (%d scene(s) framed at %.1f m, %d layer(s) at the right size)"
          % (checked, built_for, len(set(themes.values())) * len(table["layers"])))
    return 0


if __name__ == "__main__":
    sys.exit(main())
