"""Does every scene and prefab in this project still parse?

This exists because of a fault that cost a day and was invisible to everything else here.

Adding a component to a prefab instance by editing the scene file means adding an entry
to that instance's m_AddedComponents. Unity indents those entries at four spaces for the
"- " and six for the keys beneath it. Mine went in at two and four. Two spaces makes the
entry a SIBLING of m_Modification rather than an item inside it, which is not valid YAML
at all -- and Unity's answer to a PrefabInstance document it cannot parse is to leave the
object out of the scene. Both of the Nordic arena's ladders disappeared.

Nothing caught it. The ladders still baked, because the harness here reads the file with
regular expressions and regular expressions do not care about indentation; ladder_check
passed; border_check passed; the arena was declared READY. The only thing that noticed
was a person opening the scene and seeing no ladders.

So this reads every asset Unity stores as YAML, splits it into its documents the way
Unity writes them, and parses each one. It asserts nothing about meaning. The point is
only that the file still says what it looks like it says.

    python3 yaml_check.py
"""
import os
import re
import sys

try:
    import yaml
except ImportError:                                   # pragma: no cover
    print("  yaml_check needs PyYAML: python3 -m pip install pyyaml")
    sys.exit(0)

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.join(HERE, "..", "..", "Gulag runners", "Assets")

# Only the project's own, and the prefabs it instances. Imported art packages are
# thousands of files nobody here edits by hand, and this is a check on hand edits.
LOOK_IN = ("Scenes", "Prefabs", "_Game")
SUFFIXES = (".unity", ".prefab", ".asset", ".controller")

HEADER = re.compile(r"^--- !u!(\d+) &(-?\d+)( stripped)?\s*$", re.M)


def documents(text):
    """Unity's YAML split into (class, fileID, body), the way Unity writes it."""
    marks = list(HEADER.finditer(text))
    for i, m in enumerate(marks):
        end = marks[i + 1].start() if i + 1 < len(marks) else len(text)
        yield int(m.group(1)), int(m.group(2)), text[m.end():end].lstrip("\n")


def parse(body):
    """Parse one document, or return why not.

    fileID is masked because `{fileID: 123}` is a perfectly good flow mapping but the
    key repeats inside a document, and PyYAML is right to be unhappy about that while
    Unity is entitled not to care. Masking keeps the check on STRUCTURE, which is the
    thing that broke.
    """
    masked = body.replace("{fileID:", "{fileID_:")
    try:
        yaml.safe_load(masked)
        return None
    except yaml.YAMLError as why:
        first = str(why).splitlines()
        return " ".join(line.strip() for line in first[:3])


def main(argv):
    wanted = []
    for folder in LOOK_IN:
        for base, dirs, files in os.walk(os.path.join(ASSETS, folder)):
            dirs[:] = [d for d in dirs if not d.startswith(".")]
            for f in sorted(files):
                if f.endswith(SUFFIXES):
                    wanted.append(os.path.join(base, f))
    if argv:
        wanted = [p for p in wanted if any(a in p for a in argv)]

    problems = []
    docs = 0
    for path in wanted:
        text = open(path, encoding="utf-8", errors="ignore").read()
        if not text.startswith("%YAML"):
            continue                                   # a binary or text asset, not this
        for cls, fid, body in documents(text):
            docs += 1
            why = parse(body)
            if why is None:
                continue
            shown = os.path.relpath(path, os.path.join(HERE, "..", ".."))
            kind = (body.splitlines() or [""])[0].rstrip(":")
            problems.append(
                f"{shown}: the {kind} at fileID {fid} is not valid YAML -- {why}.\n"
                f"      Unity's answer to a document it cannot parse is to leave the "
                f"object out of the scene, silently. Check the indentation of whatever "
                f"was last added to it: a list entry under m_Modification goes in at "
                f"four spaces, and its keys at six.")

    print(f"  note: {docs} document(s) across {len(wanted)} file(s)")
    for p in problems:
        print("  " + p)
    print(f"yaml_check: {len(problems)} problem(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
