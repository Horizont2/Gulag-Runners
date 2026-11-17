#!/usr/bin/env python3
"""
vfx_check.py — the impact effects, and the one way they break without a word.

FighterDamageView has had `bloodOnHit`, `sparksOnBlock` and `deathBurst` since it was
written, and in every arena all three were NULL. Landing a blow produced a material flash
and nothing else. The pack the project owns -- Cartoon FX Remaster, 65 prefabs -- had three
of them used anywhere.

The rules below are mostly about one trap, because it is the kind that costs a week.

An authored effect is several particle systems under one parent: the layers need different
materials and different render modes, and a sub-emitter can have neither. Those are started
with Play(withChildren). A loose single emitter -- what a pack usually ships for a blood
spray -- is started with Emit(n). Call Emit on a layered effect's parent and it fires the
parent's own emitter, which has no renderer, and all five layers stay dark. **Nothing
errors.** The hit simply has no particles, and it looks exactly like a tuning problem.

Rules:

  1. Spray() handles both kinds. If it goes back to a bare Emit, every authored effect in
     the game goes silently dark.
  2. Damage scales an authored effect's SIZE, not its particle count. Multiplying a
     composition built around nine sparks by damage gives two hundred: the arrangement IS
     the effect.
  3. The builder asks for textures the generator actually writes. A missing texture is a
     white quad, which reads as a bug in the renderer rather than as a missing file.
  4. The sparks come out in a CONE. A sphere is what a particle system does when nobody has
     told it where the blow came from, and it is the clearest single sign of an effect that
     was never authored.
  5. Nothing casts or receives shadows (docs/06: unlit, mobile).

Run: python3 vfx_check.py
"""

import os
import struct
import zlib
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
GAME = os.path.join(HERE, "..", "..", "Gulag runners", "Assets", "_Game")

BUILDER = os.path.join(GAME, "Editor", "HitEffectBuilder.cs")
VIEW = os.path.join(GAME, "Presentation", "FighterDamageView.cs")
TEXTURES = os.path.join(GAME, "Effects", "Textures")
GENERATOR = os.path.join(HERE, "make_vfx_textures.py")


def read(path):
    with open(path, "r", encoding="utf-8") as handle:
        return handle.read()


def strip_comments(text):
    """The trap is explained at length in the comments; only code counts."""
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    text = re.sub(r"///.*$", "", text, flags=re.M)
    return "\n".join(re.sub(r"//.*$", "", line) for line in text.split("\n"))


def body_of(text, signature):
    at = text.find(signature)
    if at < 0:
        return None
    open_at = text.find("{", at)
    if open_at < 0:
        return None
    depth = 0
    for i in range(open_at, len(text)):
        if text[i] == "{":
            depth += 1
        elif text[i] == "}":
            depth -= 1
            if depth == 0:
                return text[open_at:i + 1]
    return None



# ---- a texture has to mean what its blend mode reads ---------------------------------

def rgba(path):
    """The pixels of one of our own PNGs. They are written uncompressed-filter, no
    interlacing, by make_vfx_textures -- nothing clever is needed to read them back."""
    raw = open(path, "rb").read()
    if raw[:8] != b"\x89PNG\r\n\x1a\n":
        return None, 0, 0
    w, h = struct.unpack(">II", raw[16:24])
    idat, i = b"", 8
    while i < len(raw):
        ln = struct.unpack(">I", raw[i:i + 4])[0]
        if raw[i + 4:i + 8] == b"IDAT":
            idat += raw[i + 8:i + 8 + ln]
        i += 12 + ln
    try:
        data = zlib.decompress(idat)
    except zlib.error:
        return None, 0, 0
    stride = w * 4
    px = bytearray()
    for y in range(h):
        off = y * (stride + 1)
        if off >= len(data) or data[off] != 0:
            return None, 0, 0
        px += data[off + 1:off + 1 + stride]
    return px, w, h


def premultiplied_check(problems):
    """
    Every impact texture must be PREMULTIPLIED, and that is not a style choice.

    URP puts these particle materials in premultiplied blend -- _SrcBlend One, _DstBlend
    OneMinusSrcAlpha -- which adds src.rgb outright and uses alpha only to decide how much
    background survives. The textures were written white everywhere with the shape in
    alpha, so every "invisible" pixel went on adding full white: the effects rendered as
    white SQUARES with a faint shape inside. vfx_slash was 95% such pixels.

    The shadow is the one exception and is listed as such: it is drawn with ordinary alpha
    blending, which multiplies by alpha itself, so premultiplying there would square it and
    fade the pool out at its own edges.
    """
    straight = {"vfx_shadow"}

    for name in sorted(os.listdir(TEXTURES)):
        if not name.endswith(".png"):
            continue
        px, w, h = rgba(os.path.join(TEXTURES, name))
        if px is None:
            continue

        stem = name[:-4]
        ghosts = sum(1 for k in range(0, len(px), 4)
                     if px[k + 3] == 0 and (px[k] or px[k + 1] or px[k + 2]))

        if stem in straight:
            if ghosts == 0 and w * h > 0:
                problems.append(
                    "%s is premultiplied, but it is drawn with ordinary ALPHA blending, "
                    "which multiplies by alpha itself -- the pool will fade out at its own "
                    "edges" % stem)
            continue

        if ghosts:
            problems.append(
                "%s has %d pixel(s) that are invisible (alpha 0) and yet carry colour. "
                "These materials blend PREMULTIPLIED, so every one of those adds its "
                "colour to the frame regardless -- which is what makes an effect render "
                "as a square. Re-run make_vfx_textures.py" % (stem, ghosts))

def main():
    problems = []
    premultiplied_check(problems)

    for path in (BUILDER, VIEW, GENERATOR):
        if not os.path.exists(path):
            problems.append("cannot find %s" % os.path.basename(path))
    if problems:
        for line in problems:
            print("vfx_check: " + line)
        return 1

    builder = strip_comments(read(BUILDER))
    view = strip_comments(read(VIEW))

    # ---- rules 1 and 2: the silent trap
    spray = body_of(view, "void Spray(")
    if spray is None:
        problems.append("FighterDamageView.cs: no Spray(...), which is what throws a hit's "
                        "particles. If it moved, move this rule with it")
    else:
        plays = re.search(r"\.Play\s*\(\s*true\s*\)", spray)
        emits = re.search(r"\.Emit\s*\(", spray)

        if not plays:
            problems.append(
                "FighterDamageView.cs: Spray never calls Play(true). A layered effect -- "
                "which is every effect HitEffectBuilder makes -- is started that way, and "
                "Emit on its parent fires an emitter with no renderer and leaves all five "
                "layers DARK. Nothing errors; the hit just has no particles")
        if not emits:
            problems.append(
                "FighterDamageView.cs: Spray no longer calls Emit at all, so a loose "
                "single-emitter effect from a pack would do nothing. Both kinds have to "
                "work: this project owns 65 pack prefabs and authors its own")
        if not re.search(r"childCount|GetComponentsInChildren", spray):
            problems.append(
                "FighterDamageView.cs: Spray does not look at whether the effect has "
                "layers, so it cannot be choosing between Play and Emit on anything real")

        if plays:
            # damage must reach the size, not the count, on the authored path
            before = spray[:plays.start()]
            if not re.search(r"localScale|startSizeMultiplier|transform\.localScale", before):
                problems.append(
                    "FighterDamageView.cs: nothing scales the authored effect before it is "
                    "played, so how hard the blow was changes nothing a player can see")
            if re.search(r"Emit\s*\(\s*[^)]*damage", before):
                problems.append(
                    "FighterDamageView.cs: damage is multiplying the particle COUNT on the "
                    "layered path. A composition built around nine sparks becomes two "
                    "hundred; the arrangement is the effect")

    # ---- rule 3: the builder and the generator agree about the textures
    wanted = set(re.findall(r'"(vfx_[a-z]+)"', builder))
    made = set(re.findall(r'\("(vfx_[a-z]+)"', read(GENERATOR)))
    missing = wanted - made
    if missing:
        problems.append(
            "HitEffectBuilder asks for %s, which make_vfx_textures.py does not draw. A "
            "missing texture renders as a white quad, which reads as a broken renderer "
            "rather than as a missing file" % ", ".join(sorted(missing)))

    if os.path.isdir(TEXTURES):
        on_disk = {f[:-4] for f in os.listdir(TEXTURES) if f.endswith(".png")}
        absent = wanted - on_disk
        if absent:
            problems.append(
                "%s not in Assets/_Game/Effects/Textures. Run `cd docs/refs && python3 "
                "make_vfx_textures.py`" % ", ".join(sorted(absent)))
    else:
        problems.append("no Assets/_Game/Effects/Textures — run make_vfx_textures.py")

    # ---- rule 4: a cone, not a sphere
    sparks = body_of(builder, "static void Sparks(")
    if sparks is None:
        problems.append("HitEffectBuilder.cs: no Sparks(...) layer")
    else:
        if "ParticleSystemShapeType.Cone" not in sparks:
            problems.append(
                "HitEffectBuilder.cs: the sparks do not come out of a Cone. A sphere is "
                "what a particle system does when nobody has told it where the blow came "
                "from — it reads as an explosion rather than a strike, and it is the "
                "clearest sign of an effect nobody authored")
        angle = re.search(r"shape\.angle\s*=\s*([\d.]+)f", sparks)
        if angle and float(angle.group(1)) > 55:
            problems.append(
                "HitEffectBuilder.cs: the spark cone is %s degrees, which is wide enough to "
                "be a starburst again. A spray still has to point somewhere"
                % angle.group(1))
        if "ParticleSystemRenderMode.Stretch" not in sparks:
            problems.append(
                "HitEffectBuilder.cs: the sparks are not stretched by velocity, so each one "
                "is a dot and the spray has no direction in it")

    # ---- rule 5: unlit, as docs/06 requires
    if "ShadowCastingMode.Off" not in builder:
        problems.append(
            "HitEffectBuilder.cs: the particles are not told to cast no shadows. docs/06 "
            "is unlit and simple-lit on a mobile renderer; a shadow-casting particle is "
            "per-light work on something that exists for 200 ms")
    if "receiveShadows = false" not in builder:
        problems.append("HitEffectBuilder.cs: the particles still receive shadows (docs/06)")

    if problems:
        for line in problems:
            print("vfx_check: " + line)
        print("vfx_check: %d problem(s)" % len(problems))
        return 1

    print("vfx_check: ok (%d texture(s), layered effects started the way layered effects "
          "have to be)" % len(wanted))
    return 0


if __name__ == "__main__":
    sys.exit(main())
