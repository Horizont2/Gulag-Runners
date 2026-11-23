#!/usr/bin/env python3
"""Put the Emerge opening into the arena scenes, without opening Unity.

Tools > Gulag Runners > Setup > "Build the round opening" sets this, and it is still
the thing to run when the opening changes shape. But a menu item only changes a scene
once somebody runs it in the editor, and the complaint that started this was exactly
that: the camera "still comes down on a crane", because the style lived in the tool
and the scenes had never been re-run.

So this writes the one field the tool would write. It is deliberately narrow: it
touches `style`, and adds the two Emerge knobs where they are missing, inside
RoundIntroCamera blocks only. Everything else in the scene is left byte for byte.

    python3 docs/refs/intro_style_into_scenes.py          # report only
    python3 docs/refs/intro_style_into_scenes.py --write  # change the scenes

Unity keeps a field's C# initializer when a key is absent from the YAML, so the two
knobs are not strictly needed. They are written anyway, because a value that only
exists in source is a value nobody can see in the inspector or change without a
recompile.
"""

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCENES = ROOT / "Gulag runners" / "Assets" / "Scenes"
GUID = "4e8b15c0d7a2496fb3e09c742a5d1f68"          # RoundIntroCamera.cs.meta

ARENAS = ["Arena_Mine", "Arena_Forest", "Arena_Nordic fortress", "Arena_Egypt"]

EMERGE = 3                                          # IntroStyle.Emerge
KNOBS = [("emergeClose", "0.18"), ("emergeHold", "0.08")]

# A document in a Unity scene starts with "--- !u!<class> &<id>".
DOC = re.compile(r"^--- !u!\d+ &", re.M)


def blocks(text):
    """Every YAML document in the file, as (start, end) character offsets."""
    starts = [m.start() for m in DOC.finditer(text)]
    return list(zip(starts, starts[1:] + [len(text)]))


def fix(text):
    """Returns the new text and what was done to it."""
    out = []
    did = {"style": 0, "knobs": 0, "already": 0}
    at = 0

    for start, end in blocks(text):
        out.append(text[at:start])
        at = end
        doc = text[start:end]

        if f"guid: {GUID}" not in doc:
            out.append(doc)
            continue

        # style, which is the whole point.
        m = re.search(r"^  style: (\d+)$", doc, re.M)
        if m is None:
            # No key at all: Unity wrote the default and then the field was added.
            # Put it where the tool would, straight after the class identifier.
            doc = doc.replace("  m_EditorClassIdentifier: \n",
                              f"  m_EditorClassIdentifier: \n  style: {EMERGE}\n", 1)
            did["style"] += 1
        elif int(m.group(1)) != EMERGE:
            doc = doc[:m.start()] + f"  style: {EMERGE}" + doc[m.end():]
            did["style"] += 1
        else:
            did["already"] += 1

        # The two Emerge knobs, after the last crane one so the order stays readable.
        for name, value in KNOBS:
            if re.search(rf"^  {name}:", doc, re.M):
                continue
            anchor = re.search(r"^  craneLead: .*$", doc, re.M)
            if anchor is None:
                continue
            doc = (doc[:anchor.end()] + f"\n  {name}: {value}" + doc[anchor.end():])
            did["knobs"] += 1

        out.append(doc)

    out.append(text[at:])
    return "".join(out), did


def main():
    write = "--write" in sys.argv
    total = {"style": 0, "knobs": 0, "already": 0}
    missing = []

    for name in ARENAS:
        path = SCENES / f"{name}.unity"
        if not path.exists():
            missing.append(name)
            continue

        was = path.read_text(encoding="utf-8")
        now, did = fix(was)
        for k in total:
            total[k] += did[k]

        state = (f"{did['style']} camera(s) set to Emerge"
                 if did["style"] else f"{did['already']} already on Emerge")
        print(f"{name}: {state}"
              + (f", {did['knobs']} knob(s) added" if did["knobs"] else ""))

        if write and now != was:
            path.write_text(now, encoding="utf-8")

    if missing:
        print("missing scene(s): " + ", ".join(missing))

    print(f"\n{total['style']} camera(s) moved onto Emerge, "
          f"{total['already']} already there, {total['knobs']} knob(s) written"
          + ("" if write else "  (nothing written -- pass --write)"))
    return 1 if missing else 0


if __name__ == "__main__":
    raise SystemExit(main())
