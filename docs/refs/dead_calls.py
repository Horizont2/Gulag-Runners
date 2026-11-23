#!/usr/bin/env python3
"""Strips UnityEvent listeners that point at nothing.

A persistent call whose m_Target is fileID 0 has lost the object it was wired to -- a
label deleted and rebuilt, usually. Unity skips it silently at runtime, so it costs
nothing to leave and tells nobody anything, which is exactly why six of them had been
sitting in three arenas: ui_check reported each as "Set has no target object" and the
report was true, dull and permanent.

Removing one changes no behaviour at all. What it changes is the signal: with the corpses
gone, an event reported as unwired is an event that really is.

    python3 docs/refs/dead_calls.py          # report only
    python3 docs/refs/dead_calls.py --write  # cut them out

It only ever deletes a whole list item whose first line is `- m_Target: {fileID: 0}` and
whose body is the indented lines under it. Nothing else in the file is touched, and the
count it prints is the count ui_check stops reporting.
"""

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
ASSETS = ROOT / "Gulag runners" / "Assets"

HEAD = re.compile(r"^      - m_Target: \{fileID: 0\}$")
ITEM = re.compile(r"^      - ")


def strip(text):
    """The text without its dead calls, and how many came out."""
    lines = text.split("\n")
    out, cut, i = [], 0, 0

    while i < len(lines):
        if not HEAD.match(lines[i]):
            out.append(lines[i])
            i += 1
            continue

        # A list item owns every line under it that is indented further than its dash.
        # Stop at the next item at the same level, or at anything shallower -- which is
        # how the end of m_Calls is recognised without parsing the whole document.
        i += 1
        while i < len(lines):
            line = lines[i]
            if ITEM.match(line):
                break
            if line.strip() and not line.startswith("        "):
                break
            i += 1
        cut += 1

    return "\n".join(out), cut


def main():
    write = "--write" in sys.argv
    files = sorted(list((ASSETS / "Scenes").glob("*.unity"))
                   + list((ASSETS / "Prefabs").glob("*.prefab")))

    total = 0
    for path in files:
        was = path.read_text(encoding="utf-8", errors="ignore")
        now, cut = strip(was)
        if cut == 0:
            continue

        print(f"{path.name}: {cut} listener(s) pointing at nothing")
        total += cut
        if write and now != was:
            path.write_text(now, encoding="utf-8")

    print(f"\n{total} dead listener(s)"
          + ("" if write else "  (nothing written -- pass --write)"))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
