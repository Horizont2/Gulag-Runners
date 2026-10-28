"""Can every prop in the scene actually be drawn, and if not, can it be repaired?

Unity does not read a .blend file itself: it shells out to Blender and imports what
comes back. Without Blender every object made from one becomes "Missing Prefab with
guid: ..." -- eighty of them in this arena. Nothing is lost and git is not at fault, but
the level is full of holes and the editor says only that something is missing.

So the two numbers that matter are counted here, from the files alone:

  HOW MANY props the scene cannot draw, and what format they came from. A missing prop
  whose source file is genuinely absent is a git problem; one whose file is sitting
  right there is an import problem, and they need opposite fixes.

  HOW MANY of those could be repaired by swapping in the .fbx of the same name, which
  is what Tools > Gulag Runners > Setup > Replace broken .blend props with FBX does.

That second number is the point. The tool prints its own count of the same thing, and
when the two disagree the tool is wrong -- which is how it was caught reporting "0 of 80
broken objects have an FBX of the same name" while sitting next to 48 FBX with exactly
the right names. It had asked the asset database with a filter that works in the Project
window's search box and returns nothing from script.
"""
import collections
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

ASSETS = os.path.join(HERE, "..", "..", "Gulag runners", "Assets")
SCENES = os.path.join(ASSETS, "Scenes")

# Formats Unity imports on its own. Anything else needs a program on the machine.
NATIVE = (".prefab", ".fbx", ".dae", ".obj", ".asset")

NUMBERED = re.compile(r"\s*\(\d+\)$")


def asset_paths():
    """guid -> path, for everything Unity can see. Folders starting with a dot cannot."""
    out = {}
    for base, dirs, files in os.walk(ASSETS):
        dirs[:] = [d for d in dirs if not d.startswith(".")
                   and d not in ("Library", "Temp")]
        for f in files:
            if not f.endswith(".meta"):
                continue
            path = os.path.join(base, f)
            try:
                with open(path, encoding="utf-8", errors="ignore") as fh:
                    head = fh.read(400)
            except OSError:
                continue
            m = re.search(r"guid: ([0-9a-f]{32})", head)
            if m:
                out[m.group(1)] = path[:-5]
    return out


def main():
    problems, notes = [], []
    paths = asset_paths()

    # Every .fbx Unity can see, by name -- the same index the tool builds.
    models = {}
    for path in paths.values():
        if path.lower().endswith(".fbx"):
            models.setdefault(os.path.splitext(os.path.basename(path))[0].lower(), path)

    for name in sorted(os.listdir(SCENES)):
        if not name.endswith(".unity"):
            continue
        with open(os.path.join(SCENES, name), encoding="utf-8", errors="ignore") as fh:
            scene = fh.read()

        by_format = collections.Counter()
        absent = 0
        repairable, orphaned = 0, collections.Counter()

        for block in re.finditer(r"^--- !u!1001 &\d+\n(.*?)(?=\n--- !u!|\Z)",
                                 scene, re.M | re.S):
            body = block.group(1)
            guid = re.search(r"m_SourcePrefab: \{fileID: \d+, guid: ([0-9a-f]{32})", body)
            if not guid:
                continue

            source = paths.get(guid.group(1))
            if source is None:
                absent += 1
                continue

            extension = os.path.splitext(source)[1].lower()
            by_format[extension] += 1
            if extension in NATIVE:
                continue

            label = re.search(r"propertyPath: m_Name\n\s+value: ([^\n]*)", body)
            stem = (label.group(1).strip() if label
                    else os.path.splitext(os.path.basename(source))[0])
            stem = NUMBERED.sub("", stem).strip().lower()
            if stem in models:
                repairable += 1
            else:
                orphaned[stem] += 1

        if not by_format and not absent:
            continue

        if absent:
            problems.append(
                f"{name}: {absent} prefab instance(s) point at a guid that is nowhere in "
                f"the project -- that IS something git lost, unlike the rest of this")

        blind = sum(n for ext, n in by_format.items() if ext not in NATIVE)
        if not blind:
            notes.append(f"{name}: {sum(by_format.values())} prop(s), all in formats "
                         f"Unity reads by itself")
            continue

        spread = ", ".join(f"{n} x {ext}" for ext, n in sorted(by_format.items()))
        problems.append(
            f"{name}: {blind} of {sum(by_format.values())} props come from a format "
            f"Unity cannot read without another program installed ({spread}). "
            f"{repairable} of them have an .fbx of the same name and would be fixed by "
            f"Tools > Gulag Runners > Setup > Replace broken .blend props with FBX; "
            f"installing Blender fixes all {blind} and changes nothing in the project.")

        if orphaned:
            notes.append(f"{name}: no .fbx twin for " +
                         ", ".join(f"{k} x{v}" for k, v in sorted(orphaned.items())))

    notes.append(f"{len(models)} .fbx visible to Unity across the project")

    for n in notes:
        print("  note: " + n)
    for p in problems:
        print("  " + p)
    print(f"props_check: {len(problems)} problem(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
