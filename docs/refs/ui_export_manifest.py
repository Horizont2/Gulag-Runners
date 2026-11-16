#!/usr/bin/env python3
"""
ui_export_manifest.py — exactly what to export from Figma for the shop, read off the builder.

The shop screen is built by `Editor/ShopBuilder.cs` from a list of named pieces, each with
a size in design units and, where it stretches, a nine-slice border. Art that does not match
that list does not fail loudly -- it comes in at the wrong size and the builder scales it,
or it comes in without a border and a 756-wide row plate gets its 28 px corners smeared
across the whole width. Both look like somebody drew it badly.

So the export list is GENERATED FROM THE BUILDER rather than written beside it. A list typed
by hand is a second copy of the sizes, and the day somebody widens the sheet the list is
wrong and nothing says so.

    cd docs/refs && python3 ui_export_manifest.py            # the list, for exporting
    cd docs/refs && python3 ui_export_manifest.py --check    # verify it can still be read

What it prints per piece: the filename to export under, the size at 1x and at the export
scale, whether it is nine-sliced and with what border, and what the piece is for -- the
builder's own sentence, which is usually the thing that makes a piece obvious to draw.

Export at 2x. The design is 1920x864 and a modern phone is 2340 or more across its long
edge, so 1x is already soft before the CanvasScaler has done anything. 3x is waste at these
sizes: the largest piece here is the sheet, and nothing on this screen is photographic.

Names: the file must be named after the KEY with the slashes turned into dashes, because
that is what `fill_ui_sprites.py` matches on when it puts the export into the sprite set.
`shop/row-on` exports as `shop-row-on.png`.
"""

import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
BUILDER = os.path.join(HERE, "..", "..", "Gulag runners", "Assets", "_Game",
                       "Editor", "ShopBuilder.cs")

SCALE = 2
OUT_DIR = "Assets/_Game/UI/Shop"


def read(path):
    with open(path, "r", encoding="utf-8") as handle:
        return handle.read()


def pieces(text):
    """
    Every `new Asset(...)` in the Needed table, as (key, w, h, border, what).

    Parsed from the source rather than from a copy of it. The two constructors are
    (key, w, h, what) and (key, w, h, border, what); the border is one number because every
    nine-slice in this screen is square, which is also why the builder takes it as one.
    """
    at = text.find("static readonly Asset[] Needed")
    if at < 0:
        return None
    block = text[at:]
    end = block.find("\n        };")
    if end < 0:
        return None
    block = block[:end]

    out = []
    for m in re.finditer(r"new Asset\(\s*\"([^\"]+)\"\s*,\s*([\d.]+)f?\s*,\s*([\d.]+)f?\s*,"
                         r"\s*(?:([\d.]+)f?\s*,\s*)?((?:\"(?:\\.|[^\"\\])*\"\s*\+?\s*)+)",
                         block, re.S):
        key, w, h, border, what = m.groups()
        what = " ".join(re.findall(r'"((?:\\.|[^"\\])*)"', what))
        what = what.replace('\\"', '"')
        what = re.sub(r"\s+", " ", what).strip()
        out.append((key, float(w), float(h), float(border) if border else 0.0, what))
    return out


def wrap(text, width, indent):
    words, line, lines = text.split(), "", []
    for w in words:
        if len(line) + len(w) + 1 > width:
            lines.append(line)
            line = w
        else:
            line = (line + " " + w).strip()
    if line:
        lines.append(line)
    return ("\n" + " " * indent).join(lines)


def main(argv):
    if not os.path.exists(BUILDER):
        print("ui_export_manifest: cannot find Editor/ShopBuilder.cs")
        return 1

    rows = pieces(read(BUILDER))
    if not rows:
        print("ui_export_manifest: could not read the Needed table out of ShopBuilder.cs. "
              "If the table moved or changed shape, this parser has to move with it — the "
              "whole point is that the export list is not a second copy of those numbers")
        return 1

    problems = []
    for key, w, h, border, what in rows:
        # A border is broken when the two sides OVERLAP, and only then.
        #
        # The first version of this rule flagged `>=` and cried wolf about two pieces that
        # are right: shop/filter is 320x76 with a 38 border, and 38 is exactly half of 76 --
        # which is what a PILL is. Its vertical middle is zero pixels and it never stretches
        # vertically; it stretches sideways, where the border is 38 of 320 and there is
        # plenty of middle. shop/rarity is the same thing stood on end. A rule that calls
        # those broken is a rule that teaches people to ignore it.
        if border and (border * 2 > w or border * 2 > h):
            problems.append("%s: a %gpx border on a %gx%g piece OVERLAPS itself — the two "
                            "sides meet and cross, and Unity cannot slice that"
                            % (key, border, w, h))
        elif border and border * 2 == w and border * 2 == h:
            problems.append("%s: a %gpx border on a %gx%g piece leaves no middle on EITHER "
                            "axis, so slicing it does nothing a flat sprite would not"
                            % (key, border, w, h))
        if not what:
            problems.append("%s: no description, so whoever draws it has only a size" % key)

    if "--check" in argv:
        if problems:
            for line in problems:
                print("ui_export_manifest: " + line)
            print("ui_export_manifest: %d problem(s)" % len(problems))
            return 1
        print("ui_export_manifest: ok (%d piece(s) readable from the builder)" % len(rows))
        return 0

    sliced = [r for r in rows if r[3]]

    print("Shop UI — what to export from Figma")
    print("=" * 78)
    print("Export at %dx into %s/" % (SCALE, OUT_DIR))
    print("Name each file after its key with the slashes as dashes, which is what")
    print("fill_ui_sprites.py matches on: shop/row-on  ->  shop-row-on.png")
    print()
    print("%d piece(s), %d of them nine-sliced." % (len(rows), len(sliced)))
    print()

    for key, w, h, border, what in rows:
        name = key.replace("/", "-") + ".png"
        size = "%g x %g" % (w, h)
        at = "%g x %g" % (w * SCALE, h * SCALE)
        print("  %-22s %-14s -> %-14s @%dx" % (name, size, at, SCALE))
        if border:
            print("      nine-slice, %gpx border at 1x (%gpx in the export)"
                  % (border, border * SCALE))
        else:
            print("      flat, no border")
        print("      %s" % wrap(what, 66, 6))
        print()

    print("-" * 78)
    print("After exporting:")
    print("  1. cd docs/refs && python3 fill_ui_sprites.py      # puts them in the set")
    print("  2. In Unity: Tools > Gulag Runners > Setup > Make this the shop scene")
    print()
    print("The import settings -- sprite, uncompressed, no mips, and the nine-slice border")
    print("above -- are applied by the builder itself, so a file dragged in with Unity's")
    print("defaults is still correct. The border is the one thing that cannot be guessed")
    print("from the image, which is why it is in this list and not left to the importer.")

    if problems:
        print()
        for line in problems:
            print("ui_export_manifest: " + line)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
