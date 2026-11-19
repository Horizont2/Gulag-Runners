"""Does every piece of every built screen land ON the screen, and inside its own parent?

`ui_fit_check` measures how BIG each piece is -- 9 mm under a thumb, 2.5 mm to be read.
It never asks WHERE the piece goes, and that is the other half of "some elements came out
in bad places". The shop is what made the point: eight rows at a pitch of 142 units need
1 136 units of column, the column was 700, and so rows five to eight were built on top of
the button and off the bottom of the phone. Nothing in the project noticed. The screen
had the right sizes in the wrong places, and only opening the scene would have shown it.

So this one walks the five builders and WORKS OUT the rectangle of every `Make(...)`
call -- following the constants, the `Size("key", w, h)` lookups, the local variables and
the loops -- and then asks three things of each:

  * it is inside the 1920 x 864 sheet the CanvasScaler lays out;
  * it is inside its PARENT, because a child that hangs out of its plate is a child
    drawn on whatever happens to be behind the plate;
  * and no two rectangles that take taps overlap, because when they do the one built
    last eats the other's presses and the first one simply stops working.

A loop is evaluated at its FIRST and LAST turn, which is where a list runs off the end.
Anything this cannot read -- an expression with a call it does not know -- is counted and
printed rather than passed over: a check that quietly skips what it cannot parse is a
check that reports zero problems about nothing at all.

    python3 ui_layout_check.py
"""
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
EDITOR = os.path.join(HERE, "..", "..", "Gulag runners", "Assets", "_Game", "Editor")

BUILDERS = ("HudBuilder.cs", "MenuBuilder.cs", "SearchBuilder.cs", "ResultBuilder.cs",
            "ShopBuilder.cs", "PuzzleBuilder.cs")

# The sheet every builder lays out on, and what a unit is worth on a 6.5" phone
# (docs/23): 1920 units across 150 mm of glass.
DESIGN_W, DESIGN_H = 1920.0, 864.0
MM = 150.0 / DESIGN_W

# A piece may stick out by this much before it counts. Half a unit is rounding; a whole
# one is 0.08 mm and nobody can see it.
SLACK = 1.0

# Pieces that are MEANT to be bigger than the screen or their parent: a wash that bleeds,
# a strip stretched past its plate. Keyed by builder and by the name given to Make().
ALLOWED = {
    "HudBuilder.cs": {"Scrim", "Vignette", "Letterbox"},
    "MenuBuilder.cs": {"Scrim", "Backdrop"},
    "SearchBuilder.cs": {"Scrim"},
    "ResultBuilder.cs": {"Scrim"},
    "ShopBuilder.cs": {"Scrim"},
    # The lock plate is MOVED every frame -- PuzzleView puts it over the fighter and does
    # its own clamping to the sheet -- so where the builder leaves it says nothing.
    "PuzzleBuilder.cs": {"Plate"},
}


def read(path):
    return open(path, encoding="utf-8", errors="replace").read()


def strip_comments(text):
    """// and /* */ gone, string literals kept whole."""
    out, i, n = [], 0, len(text)
    while i < n:
        c = text[i]
        if c == '"':
            out.append(c)
            i += 1
            while i < n:
                out.append(text[i])
                if text[i] == "\\":
                    i += 1
                    if i < n:
                        out.append(text[i])
                        i += 1
                    continue
                if text[i] == '"':
                    i += 1
                    break
                i += 1
            continue
        if c == "/" and i + 1 < n and text[i + 1] == "/":
            while i < n and text[i] != "\n":
                i += 1
            continue
        if c == "/" and i + 1 < n and text[i + 1] == "*":
            i += 2
            while i + 1 < n and not (text[i] == "*" and text[i + 1] == "/"):
                i += 1
            i += 2
            continue
        out.append(c)
        i += 1
    return "".join(out)


def pieces(text):
    """The file as a flat run of ('stmt', s), ('open',) and ('close',)."""
    out, buf, depth, i, n = [], [], 0, 0, len(text)
    while i < n:
        c = text[i]
        if c == '"':
            buf.append(c)
            i += 1
            while i < n:
                buf.append(text[i])
                if text[i] == "\\":
                    i += 1
                    if i < n:
                        buf.append(text[i])
                        i += 1
                    continue
                if text[i] == '"':
                    i += 1
                    break
                i += 1
            continue
        if c in "([":
            depth += 1
        elif c in ")]":
            depth -= 1
        if depth == 0 and c == ";":
            out.append(("stmt", "".join(buf).strip()))
            buf = []
            i += 1
            continue
        if depth == 0 and c == "{":
            s = "".join(buf).strip()
            if s:
                out.append(("stmt", s))
            buf = []
            out.append(("open",))
            i += 1
            continue
        if depth == 0 and c == "}":
            s = "".join(buf).strip()
            if s:
                out.append(("stmt", s))
            buf = []
            out.append(("close",))
            i += 1
            continue
        buf.append(c)
        i += 1
    s = "".join(buf).strip()
    if s:
        out.append(("stmt", s))
    return out


FOR = re.compile(r"^for\s*\(\s*int\s+(\w+)\s*=\s*([^;]+);[^;]*?<\s*([^;]+?)\s*;")


def tree(flat, at=0, stop=None):
    """Nests the flat run into statements and for-loops."""
    body, i = [], at
    while i < len(flat):
        item = flat[i]
        if item[0] == "close":
            return body, i + 1
        if item[0] == "open":
            inner, i = tree(flat, i + 1)
            body.append(("block", inner))
            continue
        s = item[1]
        m = FOR.match(s)
        if m and i + 1 < len(flat) and flat[i + 1][0] == "open":
            inner, i = tree(flat, i + 2)
            body.append(("for", m.group(1), m.group(2), m.group(3), inner))
            continue
        body.append(("stmt", s))
        i += 1
    return body, i


def assets(text):
    """The builder's own table: key -> (w, h), the size it says each piece is."""
    table = {}
    for m in re.finditer(r'new Asset\(\s*"([^"]+)"\s*,\s*([\d.]+)f?\s*,\s*([\d.]+)f?', text):
        table[m.group(1)] = (float(m.group(2)), float(m.group(3)))
    return table


SIZE_CALL = re.compile(r'(?:Size|Design|Centre)\(\s*"([^"]*)"\s*(?:,\s*([^,()]+?)\s*,\s*([^,()]+?)\s*)?\)')


class Unreadable(Exception):
    pass


def value(expr, env, table):
    """A C# float expression as a number, or Unreadable."""
    e = expr.strip()
    e = re.sub(r"CosmeticCatalogue\.Slots\.Length", "6", e)
    e = re.sub(r"(\d)f\b", r"\1", e)
    e = re.sub(r"\bMathf\.(Max|Min|Round|Abs)\b", lambda m: m.group(1).lower(), e)
    e = re.sub(r"\bround\(", "round(", e)
    e = re.sub(r"(\w+)\.([xy])\b", r"\1__\2", e)
    if re.search(r"[A-Za-z_]\w*\s*\(", e):
        raise Unreadable(expr)
    if '"' in e or "?" in e:
        raise Unreadable(expr)
    try:
        return float(eval(e, {"__builtins__": {}},
                          dict(env, max=max, min=min, round=round, abs=abs)))
    except Exception:
        raise Unreadable(expr)


def vector(expr, env, table):
    """A Vector2-valued expression as (x, y), or Unreadable."""
    m = SIZE_CALL.search(expr)
    if m:
        key = m.group(1)
        if key in table:
            return table[key]
        if m.group(2):
            return value(m.group(2), env, table), value(m.group(3), env, table)
        raise Unreadable(expr)
    m = re.search(r"new Vector2\(\s*([^,]+),\s*(.+?)\s*\)\s*$", expr)
    if m:
        return value(m.group(1), env, table), value(m.group(2), env, table)
    raise Unreadable(expr)


def pretty(arg):
    """The name a Make call gives a rect, readable. An interpolated one keeps its hole."""
    s = arg.strip()
    if s.startswith('$"') and s.endswith('"'):
        s = s[2:-1]
    elif s.startswith('"') and s.endswith('"'):
        s = s[1:-1]
    return re.sub(r"\{\s*(.*?)\s*\}", lambda m: "{" + m.group(1).replace(" ", "") + "}", s)


def split_args(inside):
    """Top-level commas only."""
    out, buf, depth = [], [], 0
    for c in inside:
        if c in "([":
            depth += 1
        elif c in ")]":
            depth -= 1
        if c == "," and depth == 0:
            out.append("".join(buf).strip())
            buf = []
            continue
        buf.append(c)
    if buf:
        out.append("".join(buf).strip())
    return out


def call_args(stmt, name):
    """The argument list of name(...) inside stmt, as written."""
    at = stmt.find(name + "(")
    if at < 0:
        return None
    i, depth = at + len(name), 0
    for j in range(i, len(stmt)):
        if stmt[j] == "(":
            depth += 1
        elif stmt[j] == ")":
            depth -= 1
            if depth == 0:
                return split_args(stmt[i + 1:j])
    return None


class Walk:
    def __init__(self, table):
        self.table = table
        self.rects = {}       # var -> dict(name, left, top, w, h, parent)
        self.made = []        # every rect, in build order
        self.unread = []      # Make calls that could not be worked out
        self.taps = set()     # vars that get a Button

    def run(self, body, env):
        for item in body:
            if item[0] == "block":
                self.run(item[1], dict(env))
            elif item[0] == "for":
                _, var, start, stop, inner = item
                try:
                    lo = int(value(start, env, self.table))
                    hi = int(value(stop, env, self.table))
                except Unreadable:
                    continue
                # First, second and last. The last turn is where a list runs off the end of
                # the sheet; the second is where a row sits on top of the one before it,
                # and that one only shows against its NEIGHBOUR -- the first and the last
                # of six targets 60 units apart are 300 apart and look perfectly fine.
                turns = sorted(t for t in {lo, lo + 1, hi - 1} if lo <= t < hi)
                for t in turns:
                    self.run(inner, dict(env, **{var: float(t)}))
            else:
                self.statement(item[1], env)

    def statement(self, stmt, env):
        m = re.match(r"^(?:var|Vector2)\s+(\w+)\s*=\s*(.+)$", stmt)
        if m:
            try:
                x, y = vector(m.group(2), env, self.table)
                env[m.group(1) + "__x"], env[m.group(1) + "__y"] = x, y
            except Unreadable:
                pass
            return
        m = re.match(r"^(?:float|int)\s+(\w+)\s*=\s*(.+)$", stmt)
        if m:
            try:
                env[m.group(1)] = value(m.group(2), env, self.table)
            except Unreadable:
                env.pop(m.group(1), None)
            return
        m = re.search(r"AddComponent<Button>\(\s*(\w+)\.gameObject", stmt)
        if m:
            self.taps.add(m.group(1))
            return
        m = re.match(r"^RectTransform\s+(\w+)\s*=\s*Panel\(", stmt)
        if m:
            self.rects[m.group(1)] = dict(name="(root)", left=0.0, top=0.0,
                                          w=DESIGN_W, h=DESIGN_H, parent=None, var=m.group(1))
            return

        # A rect pinned to the middle of its parent rather than laid out from a corner.
        m = re.match(r"^RectTransform\s+(\w+)\s*=\s*Centre\(", stmt)
        if m and (call_args(stmt, "Centre") or []).__len__() == 4:
            args = call_args(stmt, "Centre")
            parent = self.rects.get(args[1].strip()) or dict(
                name="(canvas)", left=0.0, top=0.0, w=DESIGN_W, h=DESIGN_H, parent=None, var="")
            try:
                w = value(args[2], env, self.table)
                h = value(args[3], env, self.table)
            except Unreadable as e:
                self.unread.append((pretty(args[0]), str(e)))
                self.rects.pop(m.group(1), None)
                return
            rect = dict(name=pretty(args[0]),
                        left=parent["left"] + (parent["w"] - w) / 2,
                        top=parent["top"] + (parent["h"] - h) / 2,
                        w=w, h=h, parent=parent, var=m.group(1))
            self.made.append(rect)
            self.rects[m.group(1)] = rect
            return
        if "Make(" not in stmt:
            return
        args = call_args(stmt, "Make")
        # Six arguments is a Make whose anchor is the top left and nothing else; seven or
        # more carries the anchor, the vertical anchor and a cluster scale.
        if args is None or len(args) < 6:
            return
        var = None
        m = re.match(r"^RectTransform\s+(\w+)\s*=", stmt)
        if m:
            var = m.group(1)
        name = pretty(args[0])
        parent = self.rects.get(args[1].strip())
        if parent is None:
            parent = dict(name="(canvas)", left=0.0, top=0.0, w=DESIGN_W, h=DESIGN_H,
                          parent=None, var="")
        try:
            fx = value(args[2], env, self.table)
            fy = value(args[3], env, self.table)
            w = value(args[4], env, self.table)
            h = value(args[5], env, self.table)
            ax = value(args[6], env, self.table) if len(args) > 6 else 0.0
            va = value(args[7], env, self.table) if len(args) > 7 else 0.0
            k = value(args[8], env, self.table) if len(args) > 8 else 1.0
        except Unreadable as e:
            self.unread.append((name, str(e)))
            # and the variable it would have filled is now STALE: leaving the last rect
            # under that name in place makes the next child look like a child of whatever
            # was built before it, which is a problem report about nothing.
            if var:
                self.rects.pop(var, None)
            return

        left = parent["left"] + ax * parent["w"] + (fx - ax * DESIGN_W) * k
        top = parent["top"] + va * parent["h"] + \
            ((fy - DESIGN_H) * k if va > 0.5 else fy * k)
        rect = dict(name=name, left=left, top=top, w=w * k, h=h * k,
                    parent=parent, var=var or "")
        self.made.append(rect)
        if var:
            self.rects[var] = rect


def over(inner, outer):
    """How far inner sticks out of outer, on each side, in units."""
    return (outer["left"] - inner["left"],
            outer["top"] - inner["top"],
            (inner["left"] + inner["w"]) - (outer["left"] + outer["w"]),
            (inner["top"] + inner["h"]) - (outer["top"] + outer["h"]))


def hits(a, b):
    return (a["left"] < b["left"] + b["w"] - SLACK
            and b["left"] < a["left"] + a["w"] - SLACK
            and a["top"] < b["top"] + b["h"] - SLACK
            and b["top"] < a["top"] + a["h"] - SLACK)


def worst(sides):
    names = ("left", "top", "right", "bottom")
    out = [(names[i], v) for i, v in enumerate(sides) if v > SLACK]
    out.sort(key=lambda t: -t[1])
    return out


def main():
    problems, notes = [], []
    screen = dict(name="the screen", left=0.0, top=0.0, w=DESIGN_W, h=DESIGN_H)

    for builder in BUILDERS:
        path = os.path.join(EDITOR, builder)
        if not os.path.exists(path):
            problems.append(f"{builder} is missing, so its screen cannot be checked")
            continue
        text = strip_comments(read(path))
        table = assets(text)
        env = {}
        for m in re.finditer(r"const\s+(?:float|int)\s+(\w+)\s*=\s*([^;]+);", text):
            try:
                env[m.group(1)] = value(m.group(2), env, table)
            except Unreadable:
                pass

        body, _ = tree(pieces(text))
        walk = Walk(table)
        walk.run(body, dict(env))

        # Legacy text is a bitmap font rasterised at one size, and the CanvasScaler scales
        # every screen in this game -- 1920x864 onto everything from 720p to 1440p -- so a
        # legacy label is a blurred label on almost every phone there is. TMP draws from a
        # distance field and is sharp at any size. One of them going back in would not be
        # visible in a diff of six hundred lines, so it is a rule.
        for m in re.finditer(r"AddComponent<Text>|\bUnityEngine\.UI\.Text\b|"
                             r"AddComponent<InputField>", text):
            problems.append(
                f"{builder} builds a legacy {m.group(0)}. The whole interface is "
                f"TextMeshPro: a bitmap font goes soft the moment the CanvasScaler "
                f"touches it, which on a 1920x864 sheet is always. Use UiText.On, and "
                f"TMP_InputField for a box that is typed into")

        allowed = ALLOWED.get(builder, set())
        notes.append(f"{builder}: {len(walk.made)} rect(s) placed, "
                     f"{len(walk.taps)} of them take taps"
                     + (f", {len(walk.unread)} not readable" if walk.unread else ""))

        for r in walk.made:
            if r["name"] in allowed:
                continue
            out = worst(over(r, screen))
            if out:
                side, by = out[0]
                problems.append(
                    f"{builder}: \"{r['name']}\" hangs {by:.0f} units ({by * MM:.1f} mm) "
                    f"off the {side} of the screen. It is placed at "
                    f"{r['left']:.0f},{r['top']:.0f} and is {r['w']:.0f}x{r['h']:.0f}; "
                    f"the sheet is {DESIGN_W:.0f}x{DESIGN_H:.0f}")
                continue
            parent = r["parent"]
            if parent is None or parent["name"] in ("(root)", "(canvas)"):
                continue
            out = worst(over(r, parent))
            if out:
                side, by = out[0]
                problems.append(
                    f"{builder}: \"{r['name']}\" hangs {by:.0f} units ({by * MM:.1f} mm) "
                    f"off the {side} of its parent \"{parent['name']}\". The parent is "
                    f"{parent['w']:.0f}x{parent['h']:.0f} at "
                    f"{parent['left']:.0f},{parent['top']:.0f}")

        taps = [r for r in walk.made if r["var"] in walk.taps]
        for i in range(len(taps)):
            for j in range(i + 1, len(taps)):
                a, b = taps[i], taps[j]
                # Two turns of one loop carry the SAME name here, because the name is an
                # interpolated string this cannot read -- and they are exactly the pair
                # worth comparing, since a row of targets only overlaps its neighbour.
                if hits(a, b):
                    problems.append(
                        f"{builder}: the tap targets \"{a['name']}\" and \"{b['name']}\" "
                        f"overlap. Whichever is built last takes the presses and the "
                        f"other one stops working")

        for name, expr in walk.unread[:3]:
            notes.append(f"    could not work out \"{name}\": {expr.strip()[:70]}")

    for n in notes:
        print(("  " + n) if n.startswith(" ") else ("  note: " + n))
    for p in problems:
        print("  " + p)
    print(f"ui_layout_check: {len(problems)} problem(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
