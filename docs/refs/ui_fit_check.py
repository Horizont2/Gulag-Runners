"""Does the art fit the rect the builder puts it in, and can a thumb hit it?

THE REPORT THIS EXISTS FOR: "I built the UIs and some elements came out very small, or in
bad places." Both symptoms, one cause to begin with, and it was arithmetic rather than taste.

A builder sizes each rect from the SPRITE, on purpose -- forced into its design rect, a
444x213 export of a 210x62 arc is not a thin arc any more, it is a grey slab. To do that it
has to know what multiple of the design the export came out at, and it measured that ONCE
for the whole set, as the value the most files agreed on. That was true while one export
dressed the whole interface and false the moment two did: the older one is a flat 4x, the
newer white one is anything from 1.27x to 4.5x because it was drawn to look right rather
than to a grid. Divided by the wrong number a file lands at the wrong WIDTH -- the PLAY
button at 185 design units where the sheet says 400, the health bar at 140 where it says
440 -- each in a position computed for the size it was supposed to be. Small, and in the
wrong place, from one number.

Three of the four builders measure per sprite now, and this mirrors that: the rect is the
design width with the height taken from the art's own shape, and where that height
disagrees with the sheet by more than a quarter the SHEET wins, because the layout has rows
underneath. The result screen is the fourth and it is laid out from its own table, every
row, which is a different rule and a different answer -- so this holds both. What is left
is the sheet itself, measured against the phone it ends up on:

  * a row whose design size does not match the FALLBACK its own layout code passes, which
    means the window shows one size and an empty row builds another;
  * a nine-slice whose borders do not fit inside its rect, which draws as overlapping
    corners however good the art is;
  * art whose ASPECT disagrees with its row where nothing can absorb it -- not sliced (a
    nine-slice is MEANT to stretch) and not a hairline (a 6-unit rule has no shape to
    lose). That is a squash somebody has to decide about: redraw the art, or change the
    row to the art's own shape;
  * anything a thumb has to hit whose RECT comes out under 9 mm (docs/23's own figure),
    since the rect is the hit area however much of it the art fills;
  * anything meant to be READ whose INK comes out under 2.5 mm, on a 6.5 inch phone. The
    ink, not the rect: an icon set is drawn inset in its box, and `bandage.png` is 75% of
    its canvas across and 48% of it down, so a 32-unit rect shows 24 units of drawing in
    one direction and 15 in the other. The rect is what the layout reserves; the ink is
    what the eye gets, and the eye is what the report was about. For a bar or a rule only
    the long axis is read; across it, it only has to be a line at all.

The millimetres are the point. 1920 design units lie across the 150 mm long edge of a
6.5 inch phone, so a unit is 0.078 mm and a 24-unit icon is 1.9 mm -- the size of this
full stop. Nothing in a builder's table says that, which is how a screen that reviews
beautifully at 1920 on a monitor ends up unreadable in a hand.

    python3 ui_fit_check.py
"""
import os
import re
import struct
import sys
import zlib

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.join(HERE, "..", "..", "Gulag runners", "Assets")
EDITOR = os.path.join(ASSETS, "_Game", "Editor")
SET = os.path.join(ASSETS, "_Game", "UI", "UI Sprites.asset")

BUILDERS = (("HudBuilder.cs", "the combat HUD"),
            ("MenuBuilder.cs", "the main menu"),
            ("SearchBuilder.cs", "the search panel"),
            ("ResultBuilder.cs", "the result screen"),
            ("ShopBuilder.cs", "the shop"))

# The result screen is laid out at the SIZE IN ITS TABLE, every row, because it is made of
# slabs and a slab stretches: Slab() and Face() take their rect from Design(key) and skin
# it. The other three size each rect from the sprite, which is a different rule and a
# different answer -- the same 400x116 button row builds 116 tall there and 144 tall here.
SHEET_ONLY = {"ResultBuilder.cs"}

# The sheet every builder lays out against, and the phone it ends up on: a 6.5 inch 20:9
# screen is about 150 mm along its long edge, and the design is 1920 units across.
SHEET = 1920.0
SCREEN_MM = 150.0
MM = SCREEN_MM / SHEET

# docs/23: a thumb target wants 9 mm. These are the things a thumb lands ON -- the rings,
# the stick's base, PLAY, the close button. A glyph INSIDE a ring is not a target (the ring
# is), and neither is the knob the stick draws at the thumb's position.
TAPPED = ("ring-154", "ring-130", "ring-101", "joystick/ring", "button/play",
          "button/close", "button/primary", "button/secondary", "button/round")
TAP_MM = 9.0

# And anything meant to be read at arm's length needs to be visible at all.
READ_MM = 2.5

# A bar, a rule or a gradient is read along its length; across it, it only has to be a
# line. 0.4 mm is about eight pixels on a 1080p phone.
LINE_MM = 0.4

# Over this, a row is a bar rather than a shape: only its long axis is read.
LONG = 2.5

# At or under this many design units, a side carries no shape to squash: a 6-unit rule
# stretched from 380 to 520 is still a rule.
HAIRLINE = 8.0

# And how far the art's aspect may differ from the row's before the row is drawing
# something that is not the shape the layout was built around.
ASPECT_SLACK = 0.25

# Under this much of its own canvas covered, a file is worth saying out loud: the row has
# to be bigger than it looks, because the art is drawn inset in its box.
INK_SLACK = 0.6

# Rows whose rect the LAYOUT decides, not the row: the row's number is the art's nominal
# size and measuring it would measure nothing. Each entry is the width and the height the
# layout actually uses, with None for "whatever the row says", and why.
STRETCHED = {
    "search/scrim": (SHEET, None,
                     "Stretch() runs it the full width of the screen; the 8 is one column "
                     "of pixels and only the vertical fade matters"),
    "search/bar/track": (1000.0, None, "BuildStatus lays the bar out 1000 units wide"),
    "search/bar/fill": (1000.0, None, "the fill rides inside that same 1000"),
}


def read(path):
    return open(path, encoding="utf-8", errors="replace").read()


def table(path):
    """Every row of a builder: key, design width, design height, and the nine-slice
    borders, which the Asset constructors give as nothing, one number for all four, or
    left/bottom/right/top."""
    text = read(path)
    block = re.search(r"static readonly Asset\[\] Needed\s*=\s*\{(.*?)\n        \};",
                      text, re.S)
    if block is None:
        return []
    out = []
    for chunk in block.group(1).split("new Asset(")[1:]:
        m = re.match(r'\s*"([^"]+)"((?:\s*,\s*-?[\d.]+f?)+)', chunk)
        if m is None:
            continue
        nums = [float(n.rstrip("f")) for n in re.findall(r"-?[\d.]+f?", m.group(2))]
        if len(nums) < 2:
            continue
        w, h, rest = nums[0], nums[1], nums[2:]
        if len(rest) >= 4:
            border = (rest[0], rest[1], rest[2], rest[3])   # l, b, r, t
        elif len(rest) == 1:
            border = (rest[0],) * 4
        else:
            border = (0.0, 0.0, 0.0, 0.0)
        out.append((m.group(1), w, h, border))
    return out


def fallbacks(path):
    """What the LAYOUT passes as each row's size when the row is empty:
    Size("hud/pip", 20, 20). It has to agree with the table, or the window shows one
    size and the build uses another."""
    out = {}
    for m in re.finditer(r'Size\(\s*"([^"]+)"\s*,\s*([\d.]+)f?\s*,\s*([\d.]+)f?\s*\)',
                         read(path)):
        out.setdefault(m.group(1), (float(m.group(2)), float(m.group(3))))
    return out


def png_size(path):
    with open(path, "rb") as f:
        head = f.read(26)
    if head[:8] != b"\x89PNG\r\n\x1a\n":
        return None
    return struct.unpack(">II", head[16:24])


def chunks(raw):
    i = 8
    while i + 8 <= len(raw):
        n, kind = struct.unpack(">I4s", raw[i:i + 8])
        yield kind, raw[i + 8:i + 8 + n]
        i += 12 + n


def ink(path):
    """How much of a PNG's canvas the drawing actually covers, as a fraction of each
    side.

    Stdlib only, on purpose: every other check in this folder runs on a bare python and
    this one would be the first to need a pillow installed. It reads the one format both
    exports are in -- 8-bit RGBA, not interlaced -- and calls anything else solid, which
    errs towards saying nothing rather than towards a wrong millimetre. Verified against
    pillow's own getbbox on all 76 files in the two exports: no disagreement.
    """
    raw = open(path, "rb").read()
    if raw[:8] != b"\x89PNG\r\n\x1a\n":
        return (1.0, 1.0)

    w = h = depth = colour = inter = None
    data = bytearray()
    for kind, body in chunks(raw):
        if kind == b"IHDR":
            w, h, depth, colour, _comp, _filt, inter = struct.unpack(">IIBBBBB", body[:13])
        elif kind == b"IDAT":
            data += body
        elif kind == b"IEND":
            break
    if w is None or colour != 6 or depth != 8 or inter != 0:
        return (1.0, 1.0)

    rows = zlib.decompress(bytes(data))
    stride = w * 4
    prev = bytearray(stride)
    x0, y0, x1, y1 = w, h, 0, 0
    at = 0
    for y in range(h):
        filt = rows[at]
        line = bytearray(rows[at + 1:at + 1 + stride])
        at += 1 + stride
        if filt == 1:
            for i in range(4, stride):
                line[i] = (line[i] + line[i - 4]) & 0xFF
        elif filt == 2:
            for i in range(stride):
                line[i] = (line[i] + prev[i]) & 0xFF
        elif filt == 3:
            for i in range(stride):
                left = line[i - 4] if i >= 4 else 0
                line[i] = (line[i] + ((left + prev[i]) >> 1)) & 0xFF
        elif filt == 4:
            for i in range(stride):
                a = line[i - 4] if i >= 4 else 0
                b = prev[i]
                c = prev[i - 4] if i >= 4 else 0
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                line[i] = (line[i] + (a if pa <= pb and pa <= pc
                                      else (b if pb <= pc else c))) & 0xFF
        lo, hi = -1, -1
        for x, a in enumerate(line[3::4]):
            if a:
                if lo < 0:
                    lo = x
                hi = x
        if lo >= 0:
            x0, x1 = min(x0, lo), max(x1, hi + 1)
            y0, y1 = min(y0, y), max(y1, y + 1)
        prev = line

    if x1 <= x0 or y1 <= y0:
        return (0.0, 0.0)
    return ((x1 - x0) / w, (y1 - y0) / h)


def art():
    """key -> (file, pixel width, pixel height, ink across, ink down), straight out of
    the sprite set."""
    if not os.path.exists(SET):
        return {}

    guids = {}
    for base, dirs, files in os.walk(ASSETS):
        dirs[:] = [d for d in dirs if d not in ("Library", "Temp")]
        for f in files:
            if not f.endswith(".png.meta"):
                continue
            m = re.search(r"guid: (\w+)", read(os.path.join(base, f)))
            if m:
                guids[m.group(1)] = os.path.join(base, f[:-5])

    out = {}
    for m in re.finditer(r"- key: ([^\n]*)\n    sprite: \{fileID: (-?\d+)"
                         r"(?:, guid: (\w+))?", read(SET)):
        key, guid = m.group(1).strip(), m.group(3)
        if not guid or guid not in guids:
            continue
        path = guids[guid]
        size = png_size(path)
        if size:
            fx, fy = ink(path)
            out[key] = (os.path.relpath(path, ASSETS), size[0], size[1], fx, fy)
    return out


def main():
    have = art()
    if not have:
        print("  the sprite set is empty or missing, so there is nothing to measure")
        print("ui_fit_check: 1 problem(s)")
        return 1

    problems, notes = [], []

    for name, what in BUILDERS:
        path = os.path.join(EDITOR, name)
        if not os.path.exists(path):
            problems.append(f"{name} is gone; this check is out of date.")
            continue

        rows = table(path)
        if not rows:
            problems.append(f"{name} has no row table; this check is out of date.")
            continue

        passed = fallbacks(path)

        for key, dw, dh, border in rows:
            sliced = any(b > 0 for b in border)

            # The layout's own fallback for this row, against the row itself.
            own_fallback = passed.get(key)
            if own_fallback and (abs(own_fallback[0] - dw) > 0.5
                                 or abs(own_fallback[1] - dh) > 0.5):
                problems.append(
                    f"{what}: the row `{key}` is {dw:.0f}x{dh:.0f} in the table and its "
                    f"layout code falls back to {own_fallback[0]:.0f}x"
                    f"{own_fallback[1]:.0f}. The window shows the first number and an "
                    f"empty row builds the second, so one of the two is a lie.")

            # A nine-slice whose borders do not fit draws overlapping corners.
            if sliced:
                l, b, r, t = border
                if l + r > dw or b + t > dh:
                    problems.append(
                        f"{what}: `{key}` is sliced {l:.0f}/{b:.0f}/{r:.0f}/{t:.0f} in a "
                        f"{dw:.0f}x{dh:.0f} rect, so the corners overlap and the middle "
                        f"draws inside out.")

            if key not in have:
                continue
            f, px, py, inkx, inky = have[key]
            scale = px / dw if dw > 0 else 0
            if scale <= 0:
                continue

            # The rect the tool produces: this file's own multiple, and the sheet's own
            # rect where the heights disagree by more than a quarter -- or always the
            # sheet, in the one builder that lays out from its table.
            native = (px / scale, py / scale)
            disagrees = dh > 0 and abs(native[1] - dh) > dh * ASPECT_SLACK
            rect = ([dw, dh] if disagrees or name in SHEET_ONLY
                    else [native[0], native[1]])

            # And where the layout, not the row, decides the rect.
            stretch = STRETCHED.get(key)
            if stretch:
                if stretch[0]:
                    rect[0] = stretch[0]
                if stretch[1]:
                    rect[1] = stretch[1]

            mm = (rect[0] * MM, rect[1] * MM)
            # What the eye gets: the rect with the art's own margins taken off it.
            drawn = (mm[0] * inkx, mm[1] * inky)
            design_aspect = dw / dh if dh > 0 else 0
            art_aspect = px / py if py > 0 else 0
            off = abs(art_aspect - design_aspect) / design_aspect if design_aspect else 0

            notes.append(f"    {key:28s} design {dw:5.0f}x{dh:<5.0f} file {px:5d}x{py:<5d} "
                         f"({scale:4.2f}x)  rect {rect[0]:6.1f}x{rect[1]:<6.1f} "
                         f"= {mm[0]:5.1f}x{mm[1]:4.1f} mm, ink "
                         f"{drawn[0]:4.1f}x{drawn[1]:4.1f} mm"
                         f"{'  sliced' if sliced else ''}"
                         f"{'  stretched' if stretch else ''}")

            if min(inkx, inky) < INK_SLACK and not sliced:
                notes.append(
                    f"{what}: `{key}` draws on {inkx * 100:.0f}% of its file across and "
                    f"{inky * 100:.0f}% down, so the rect reserves room the art does not "
                    f"use. Not wrong -- an icon set is drawn inset -- but it is why the "
                    f"row has to be bigger than the size it looks like it needs.")

            # A nine-slice is MEANT to stretch and a hairline has no shape to lose, so an
            # aspect that disagrees is only a squash when neither is true.
            if off > ASPECT_SLACK and not sliced and min(dw, dh) > HAIRLINE:
                problems.append(
                    f"{what}: `{key}` is drawn {art_aspect:.2f}:1 and the row is "
                    f"{design_aspect:.2f}:1 ({off * 100:.0f}% out), and it is neither "
                    f"sliced nor a hairline, so something visible is squashed. Redraw "
                    f"{f} to the row's shape, or change the row to the art's.")

            tapped = any(key.endswith(t) for t in TAPPED)
            long_thing = max(rect) / max(1e-6, min(rect)) > LONG
            if tapped:
                # The RECT, not the ink: a thumb lands on the hit area whether the art
                # fills it or not.
                if min(mm) < TAP_MM:
                    problems.append(
                        f"{what}: `{key}` lands at {mm[0]:.1f}x{mm[1]:.1f} mm on a 6.5 "
                        f"inch phone and it is pressed -- docs/23 wants {TAP_MM:g} mm for "
                        f"a thumb, which is {TAP_MM / MM:.0f} units.")
            elif long_thing:
                # A bar: read along its length, and across it only a line.
                if max(drawn) < READ_MM:
                    problems.append(
                        f"{what}: `{key}` draws only {max(drawn):.1f} mm along, under the "
                        f"{READ_MM:g} mm anything has to be to be seen at all.")
                elif min(drawn) < LINE_MM:
                    problems.append(
                        f"{what}: `{key}` comes out {min(drawn):.2f} mm thick, under the "
                        f"{LINE_MM:g} mm that still reads as a line on a phone -- about "
                        f"{LINE_MM / MM:.0f} units.")
            elif max(drawn) < READ_MM:
                problems.append(
                    f"{what}: `{key}` draws {drawn[0]:.1f}x{drawn[1]:.1f} mm of actual art "
                    f"in a {mm[0]:.1f}x{mm[1]:.1f} mm rect, under the {READ_MM:g} mm "
                    f"anything has to be to be read at arm's length. {f} covers "
                    f"{inkx * 100:.0f}% x {inky * 100:.0f}% of its own file, so the row "
                    f"has to be about {READ_MM / MM / max(inkx, inky):.0f} units for the "
                    f"drawing to reach {READ_MM:g} mm.")

    for n in notes:
        print(("  " + n) if n.startswith(" ") else ("  note: " + n))
    for p in problems:
        print("  " + p)
    print(f"ui_fit_check: {len(problems)} problem(s) across {len(have)} dressed row(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
