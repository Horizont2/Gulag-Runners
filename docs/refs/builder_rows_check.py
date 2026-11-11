"""Does every row of every UI builder say what the asset is for?

The four builder windows are the only tools in this project a person FILLS IN rather than
presses: twenty-eight rows for the HUD, sixteen for the menu, eight for the result screen,
five for the search panel. Each row is a named place to drag a sprite into, and the name is
a filing path -- `hud/bar/fill`, `search/scrim`, `menu/plate`. A path is not an explanation.
Is `bar/fill` the health bar or the stamina bar? Does it include the frame? Which end does it
empty from? Is it stretched or nine-sliced?

Whoever drags the art in cannot answer those from the key, and a wrong guess builds a screen
that looks wrong with nothing in the console. So every row carries an English sentence, shown
under the field and in the tooltip, and this is what keeps it:

  * every `new Asset(...)` row has a note;
  * the note is prose, not the key again, and long enough to have said something;
  * the window passes the notes to UiSpriteRows.Rows, or they are written and never drawn.

    python3 builder_rows_check.py
"""
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
EDITOR = os.path.join(HERE, "..", "..", "Gulag runners", "Assets", "_Game", "Editor")

BUILDERS = ("HudBuilder.cs", "MenuBuilder.cs", "SearchBuilder.cs", "ResultBuilder.cs")

# Twelve words is not a style rule, it is the shortest thing that can answer "what is this
# and how is it drawn". Under it, the note is the key with articles in it.
ENOUGH = 12


def read(path):
    return open(path, encoding="utf-8", errors="replace").read()


def rows(text):
    """Every (key, note) the table holds, in order.

    Split on the `new Asset(` openings rather than matched with one pattern per row: the
    notes are multi-line concatenations with brackets and quotes in them, and a single
    regex over that either stops inside a sentence or swallows the next row.
    """
    block = re.search(r"static readonly Asset\[\] Needed\s*=\s*\{(.*?)\n        \};",
                      text, re.S)
    if block is None:
        return None

    out = []
    for chunk in block.group(1).split("new Asset(")[1:]:
        key = re.match(r'\s*"([^"]+)"', chunk)
        if key is None:
            continue
        rest = chunk[key.end():]
        note = " ".join(m.group(1) for m in re.finditer(r'"((?:[^"\\]|\\.)*)"', rest))
        out.append((key.group(1), note.strip()))
    return out


def main():
    problems, counted = [], 0

    for name in BUILDERS:
        path = os.path.join(EDITOR, name)
        if not os.path.exists(path):
            problems.append(f"{name} is gone; this check is out of date.")
            continue

        text = read(path)
        table = rows(text)
        if table is None:
            problems.append(f"{name} has no `Asset[] Needed` table any more, so there are "
                            f"no rows to describe. If the tool changed shape, change this "
                            f"check with it.")
            continue

        if "Notes()" not in text:
            problems.append(f"{name} never builds a Notes() list, so whatever is written in "
                            f"the table is not what the window shows.")
        elif not re.search(r"UiSpriteRows\.Rows\([^;]*?Notes\(\)", text):
            problems.append(f"{name} has the notes and does not pass them to "
                            f"UiSpriteRows.Rows -- written, and never drawn.")

        for key, note in table:
            counted += 1
            words = len(re.findall(r"[A-Za-z][A-Za-z'-]*", note))
            leaf = key.split("/")[-1].replace("-", " ")

            if not note:
                problems.append(
                    f"{name}: the row `{key}` has no note. The key is a filing path; "
                    f"somebody dragging the art in needs a sentence saying what the piece "
                    f"is and how it is drawn.")
            elif words < ENOUGH:
                problems.append(
                    f"{name}: the note on `{key}` is {words} words -- \"{note}\". That is "
                    f"the key with articles in it; say what it is FOR and what happens if "
                    f"it is drawn wrong.")
            elif note.strip().lower().rstrip(".") in (key.lower(), leaf.lower()):
                problems.append(
                    f"{name}: the note on `{key}` is the key again. Nothing has been said.")

    for p in problems:
        print("  " + p)
    print(f"builder_rows_check: {len(problems)} problem(s) across {counted} row(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
