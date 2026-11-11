"""What the interface is actually wearing, row by row, and what is still borrowed.

The four builder windows ask for 57 named pieces between them. The project holds two
exports:

  _Game/UI/     the older one, complete and named exactly after the rows, at 4x the design
                size: every HUD row, all sixteen menu rows, all five search rows.
  UI/           the newer one off the UI branch, 24 files, all deliberately WHITE so the
                game decides the colour, and named after what they look like rather than
                after the rows.

`UI Sprites.asset` is what the tools read, and this says what is in it: which rows carry the
new white art, which are still borrowing the older export, and which are empty. The middle
list is the one that matters -- it is the art still to be drawn, and it is a list here
rather than a memory somewhere.

It also says, for every empty row, whether anything in either folder would answer to it by
NAME, because that is what the tool's own "guess the empty rows from a folder" button goes
on (UiSpriteRows.Agrees and Score, mirrored below).

    python3 ui_assets_check.py
"""
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.join(HERE, "..", "..", "Gulag runners", "Assets")
EDITOR = os.path.join(ASSETS, "_Game", "Editor")
SET = os.path.join(ASSETS, "_Game", "UI", "UI Sprites.asset")

# Where the two exports live, and which is the one being moved to.
WHITE = "UI"
OLDER = os.path.join("_Game", "UI")

BUILDERS = (("HudBuilder.cs", "the combat HUD"),
            ("MenuBuilder.cs", "the main menu"),
            ("SearchBuilder.cs", "the search panel"),
            ("ResultBuilder.cs", "the result screen"))

# Rows that are MEANT to be empty: the builder lays them out as a flat colour and says so
# in its own note. An empty row here is a decision, not a gap.
BY_DESIGN = {
    "result/dim": "the builder lays a flat UiPalette.Scrim; art is optional",
    "result/banner": "the headline is text on nothing until somebody draws a band",
}

# UiSpriteRows.Generic, mirrored. Kept in step by drifted(), below.
GENERIC = {
    "frame", "fill", "background", "icon", "glow", "ring", "track", "plate",
    "button", "arc", "panel", "dim", "divider", "knob", "scrim", "banner",
    "source", "glyph", "primary", "secondary",
}


def read(path):
    return open(path, encoding="utf-8", errors="replace").read()


def keys_of(path):
    text = read(path)
    block = re.search(r"static readonly Asset\[\] Needed\s*=\s*\{(.*?)\n        \};",
                      text, re.S)
    if block is None:
        return []
    return [m.group(1) for m in re.finditer(r'new Asset\(\s*"([^"]+)"', block.group(1))]


def guid_index():
    """Every sprite in either export, by the guid its .meta carries."""
    out = {}
    for folder in (WHITE, OLDER):
        root = os.path.join(ASSETS, folder)
        for base, dirs, files in os.walk(root):
            dirs[:] = sorted(d for d in dirs if not d.startswith("."))
            for f in sorted(files):
                if not f.endswith(".meta"):
                    continue
                art = os.path.join(base, f[:-5])
                if not art.lower().endswith((".png", ".jpg", ".jpeg", ".tga", ".psd")):
                    continue
                m = re.search(r"guid: (\w+)", read(os.path.join(base, f)))
                if m:
                    out[m.group(1)] = (folder, os.path.relpath(art, root))
    return out


def worn():
    """What the set holds: key -> guid, or None for an empty row."""
    if not os.path.exists(SET):
        return None
    text = read(SET)
    out = {}
    for m in re.finditer(r"- key: ([^\n]*)\n    sprite: \{fileID: (-?\d+)"
                         r"(?:, guid: (\w+))?", text):
        out[m.group(1).strip()] = m.group(3)
    return out


def words(name):
    """UiSpriteRows.Words: a filename as WORDS, split on case and punctuation."""
    flat = os.path.splitext(os.path.basename(name))[0]
    return [w.lower() for w in
            re.findall(r"[A-Z]+(?![a-z])|[A-Z][a-z]+|[a-z]+|\d+", flat)]


def parts(key):
    """UiSpriteRows.Parts: a key as words -- slashes and hyphens both separate."""
    return [w for piece in key.lower().split("/") for w in piece.split("-") if w]


def agrees(key, name):
    """UiSpriteRows.Agrees: does the filename narrow anything, or only share a generic word?"""
    have = words(name)
    leaf = parts(key.split("/")[-1])
    if any(w not in leaf and w in have for w in parts(key)):
        return True
    return any(w not in GENERIC and w in have for w in leaf)


def score(key, name):
    """UiSpriteRows.Score, to the letter: the tool's own arithmetic."""
    have = words(name)
    found = sum(1 for w in parts(key) if w in have)
    if any(w in have for w in parts(key.split("/")[-1])):
        found += 1
    return found


def drifted():
    """The mirrored generic list, against the tool's own."""
    text = read(os.path.join(EDITOR, "UiSpriteRows.cs"))
    block = re.search(r"static readonly HashSet<string> Generic = new HashSet<string>\s*\{(.*?)\};",
                      text, re.S)
    if block is None:
        return ("UiSpriteRows has no Generic set any more, so this check's copy of it is "
                "a fiction.")
    theirs = set(re.findall(r'"(\w+)"', block.group(1)))
    if theirs != GENERIC:
        missing = ", ".join(sorted(theirs - GENERIC)) or "nothing"
        extra = ", ".join(sorted(GENERIC - theirs)) or "nothing"
        return (f"the generic-word list here has drifted from UiSpriteRows: it is missing "
                f"{missing} and carries {extra} the tool does not.")
    return None


def main():
    problems, notes = [], []

    drift = drifted()
    if drift:
        problems.append(drift)

    files = guid_index()
    in_set = worn()
    if in_set is None:
        problems.append("there is no _Game/UI/UI Sprites.asset, so every row of every tool "
                        "opens empty. Run docs/refs/fill_ui_sprites.py.")
        print(f"ui_assets_check: {len(problems)} problem(s)")
        return 1

    white = sum(1 for g in in_set.values() if g and files.get(g, ("", ""))[0] == WHITE)
    older = sum(1 for g in in_set.values() if g and files.get(g, ("", ""))[0] == OLDER)
    notes.append(f"{len(files)} sprite(s) across the two exports; the set carries "
                 f"{white} from {WHITE}/ and {older} from {OLDER}/")

    borrowed, empty = [], []
    for name, what in BUILDERS:
        path = os.path.join(EDITOR, name)
        if not os.path.exists(path):
            problems.append(f"{name} is gone; this check is out of date.")
            continue

        keys = keys_of(path)
        if not keys:
            problems.append(f"{name} has no row table any more; this check is out of date.")
            continue

        mine = [(k, in_set.get(k)) for k in keys]
        got = sum(1 for _k, g in mine if g)
        notes.append(f"{what}: {got} of {len(keys)} row(s) have art")

        for key, guid in mine:
            if guid is None:
                if key not in in_set:
                    problems.append(
                        f"{what}: the row `{key}` is not in the sprite set at all, so the "
                        f"tool will add it empty and nothing says what it wants. Run "
                        f"fill_ui_sprites.py.")
                empty.append((what, key))
                continue
            where = files.get(guid)
            if where is None:
                problems.append(
                    f"{what}: the row `{key}` points at guid {guid}, which is not a sprite "
                    f"in either export -- a reference to art that is not there reads in "
                    f"Unity as an empty row with no explanation.")
            elif where[0] == OLDER:
                borrowed.append((what, key, where[1]))

    if borrowed:
        notes.append(f"{len(borrowed)} row(s) are still wearing the older export -- this is "
                     f"the list of art to redraw white:")
        for what, key, f in borrowed:
            notes.append(f"    {key:28s} <- {OLDER}/{f}   ({what})")

    if empty:
        notes.append(f"{len(empty)} row(s) are empty:")
        for what, key in empty:
            why = BY_DESIGN.get(key)
            best, best_score = None, 0
            for guid, (folder, f) in files.items():
                if not agrees(key, f):
                    continue
                s = score(key, f)
                if s > best_score:
                    best, best_score = f"{folder}/{f}", s
            if why:
                notes.append(f"    {key:28s} by design: {why}")
            elif best and best_score >= 2:
                notes.append(f"    {key:28s} nothing wired, but {best} answers to the name "
                             f"-- press Fill, or drag it in")
            else:
                notes.append(f"    {key:28s} nothing in either export answers to it "
                             f"({what})")

    used = {g for g in in_set.values() if g}
    spare = sorted(f"{folder}/{f}" for guid, (folder, f) in files.items()
                   if guid not in used)
    if spare:
        notes.append(f"{len(spare)} file(s) no row wears:")
        for f in spare:
            notes.append(f"    {f}")

    for n in notes:
        print(("  " + n) if n.startswith(" ") else ("  note: " + n))
    for p in problems:
        print("  " + p)
    print(f"ui_assets_check: {len(problems)} problem(s); {white + older} of 57 row(s) "
          f"dressed, {len(borrowed)} of them from the older export")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
