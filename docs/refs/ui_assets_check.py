"""What the UI folder can actually fill, screen by screen.

The four builder windows ask for 57 named pieces between them -- 28 for the combat HUD, 16
for the menu, 8 for the result screen, 5 for the search panel. Assets/UI is where the
exported art lands. Those two lists have never been compared, and the gap between them is
the only honest answer to "is the UI finished": a screen whose rows are empty builds with
no complaint and comes out with white squares where the art should be.

So this does two things, and the second matters more than the first.

  WHAT IS COVERED. Every key is scored against every file with the SAME arithmetic the
  tool's own "guess the empty rows from a folder" button uses (UiSpriteRows.Score: how many
  of the key's words the filename holds, with the leaf counted twice, and a floor of two).
  So this is not an opinion about whether a file could serve -- it is exactly what the
  button will do when somebody presses it.

  WHAT ONE FILE IS BEING ASKED TO BE. A file that wins four different rows is not four
  pieces of art; it is one piece about to be dragged into four places, which is how a HUD
  ends up with the same frame around the health bar and around the equipment slots.

    python3 ui_assets_check.py
"""
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.join(HERE, "..", "..", "Gulag runners", "Assets")
EDITOR = os.path.join(ASSETS, "_Game", "Editor")
UI = os.path.join(ASSETS, "UI")

# The four tools, and what each one dresses.
BUILDERS = (("HudBuilder.cs", "the combat HUD"),
            ("MenuBuilder.cs", "the main menu"),
            ("SearchBuilder.cs", "the search panel"),
            ("ResultBuilder.cs", "the result screen"))

# UiSpriteRows.FillEmptyFromFolder's own floor. Below it the match is a coincidence.
FLOOR = 2

# How many rows one file may win before it stops being art and starts being a placeholder.
STRETCHED = 3

# UiSpriteRows.Generic, mirrored: leaves that say nothing on their own. Kept in step with
# the tool by the assertion at the bottom of this file -- a copy that drifts would report a
# fill nobody gets.
GENERIC = {
    "frame", "fill", "background", "icon", "glow", "ring", "track", "plate",
    "button", "arc", "panel", "dim", "divider", "knob", "scrim", "banner",
    "source", "glyph", "primary", "secondary",
}


def read(path):
    return open(path, encoding="utf-8", errors="replace").read()


def keys_of(path):
    """The keys one builder asks for, in table order."""
    text = read(path)
    block = re.search(r"static readonly Asset\[\] Needed\s*=\s*\{(.*?)\n        \};",
                      text, re.S)
    if block is None:
        return []
    return [m.group(1) for m in re.finditer(r'new Asset\(\s*"([^"]+)"', block.group(1))]


def files():
    """Every sprite in Assets/UI, by path relative to it."""
    out = []
    for base, dirs, names in os.walk(UI):
        dirs[:] = sorted(d for d in dirs if not d.startswith("."))
        for f in sorted(names):
            if f.lower().endswith((".png", ".jpg", ".jpeg", ".tga", ".psd", ".svg")):
                out.append(os.path.relpath(os.path.join(base, f), UI))
    return out


def words(name):
    """UiSpriteRows.Words: a filename as WORDS, split on case and punctuation.

    Not a substring search, and that is the point: "round" is inside "backg-round", so
    BarBackground.png used to win the `menu/button/round` row by spelling.
    """
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
    """UiSpriteRows.Score, to the letter: the tool's own arithmetic, not a better one."""
    have = words(name)
    found = sum(1 for w in parts(key) if w in have)
    if any(w in have for w in parts(key.split("/")[-1])):
        found += 1
    return found


def drifted():
    """The generic list above, against the tool's own.

    A mirrored constant that drifts is worse than no mirror: the report would promise a
    fill nobody gets.
    """
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
                f"{missing} and carries {extra} the tool does not. This check would then "
                f"report a fill nobody gets.")
    return None


def main():
    have = files()
    if not have:
        print(f"  Assets/UI holds no sprites at all, so every row of every tool is empty.")
        print("ui_assets_check: 1 problem(s)")
        return 1

    problems, notes = [], []
    drift = drifted()
    if drift:
        problems.append(drift)
    notes.append(f"{len(have)} sprite(s) in Assets/UI")

    wins = {}
    total_keys = total_filled = 0

    for name, what in BUILDERS:
        path = os.path.join(EDITOR, name)
        if not os.path.exists(path):
            problems.append(f"{name} is gone; this check is out of date.")
            continue

        keys = keys_of(path)
        if not keys:
            problems.append(f"{name} has no row table any more; this check is out of date.")
            continue

        filled, empty = [], []
        for key in keys:
            best, best_score = None, 0
            for f in have:
                if not agrees(key, f):
                    continue
                s = score(key, f)
                if s > best_score:
                    best, best_score = f, s
            if best is not None and best_score >= FLOOR:
                filled.append((key, best, best_score))
                wins.setdefault(best, []).append(key)
            else:
                empty.append(key)

        total_keys += len(keys)
        total_filled += len(filled)

        notes.append(f"{what}: {len(filled)} of {len(keys)} row(s) have a file the tool "
                     f"would accept")
        for key, f, s in filled:
            notes.append(f"    {key:28s} <- {f}  (score {s})")
        if empty:
            notes.append(f"  nothing in the folder answers to {len(empty)} row(s):")
            for key in empty:
                notes.append(f"    {key}")

    # And the other direction: art nothing asks for. Some of it is a screen that has no
    # builder yet -- the shop is being designed as this is written, and SkinsIcon is for it
    # -- and some of it is named so differently from the rows that the tool will never
    # offer it, which is worth knowing before somebody wonders why Fill did nothing.
    spare = [f for f in have if f not in wins]
    if spare:
        notes.append(f"{len(spare)} file(s) no row asks for by name:")
        for f in spare:
            notes.append(f"    {f}")

    # One file, many rows.
    for f, keys in sorted(wins.items()):
        if len(keys) >= STRETCHED:
            notes.append(f"  {f} is the best answer to {len(keys)} different rows: "
                         f"{', '.join(keys)}")

    for n in notes:
        print(("  " + n) if n.startswith(" ") else ("  note: " + n))
    for p in problems:
        print("  " + p)
    print(f"ui_assets_check: {len(problems)} problem(s); {total_filled} of {total_keys} "
          f"row(s) across the four tools have art the tool would take")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
