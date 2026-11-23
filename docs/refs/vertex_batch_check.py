#!/usr/bin/env python3
"""
A shader that moves its own vertices must never be batched.

Static batching combines meshes into one, in WORLD space, and every vertex-animating shader
in this project works in OBJECT space. LampSway turns a lantern about its hook with

    float3 arm = positionOS - pivotOS;

Batched, that object space IS world space: the arm is no longer half a metre from the hook
but fifteen metres from the origin, and five degrees of swing throws the lamp right across
the arena. Reported twice in the same words -- "at play they just fly half the scene left
and right" -- which is why this exists.

It was fixed once, in the tool: BakeSetup reads a "VertexAnimation" tag off the SubShader
and clears BatchingStatic and ContributeGI on anything that carries it. That fix is only
worth what it is RUN on, and nothing said which scenes had had it. Arena_Mine still carried
678 static-flag overrides, every one of them 5 -- batching plus GI -- including the
lanterns, so the mine had never stopped flying. Arena_Egypt had 30 cleared, because that
one was run afterwards. Nothing in the project could see the difference.

So the question this answers is not "is the tool right" but "has it been run here", and it
answers it from the scenes themselves rather than from anybody's memory.

    python3 vertex_batch_check.py
"""

import glob
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.join(HERE, "..", "..", "Gulag runners", "Assets")
SCENES = os.path.join(ASSETS, "Scenes")

BATCHING_STATIC = 4      # StaticEditorFlags.BatchingStatic
CONTRIBUTE_GI = 1        # StaticEditorFlags.ContributeGI


def read(path):
    try:
        return open(path, encoding="utf-8", errors="replace").read()
    except OSError:
        return ""


def guid_of(path):
    found = re.search(r"guid: ([0-9a-f]{32})", read(path + ".meta"))
    return found.group(1) if found else None


def animated_materials():
    """
    Every material whose shader declares it moves its own vertices.

    Found through the tag rather than through a list of names, which is the same rule
    BakeSetup follows -- so a shader that starts animating, or stops, is picked up by both
    at once and neither has a list to fall behind on.
    """
    animated_shaders = set()
    for path in glob.glob(os.path.join(ASSETS, "**", "*.shader"), recursive=True):
        if '"VertexAnimation" = "True"' in read(path):
            got = guid_of(path)
            if got:
                animated_shaders.add(got)

    mats = {}
    for path in glob.glob(os.path.join(ASSETS, "**", "*.mat"), recursive=True):
        body = read(path)
        shader = re.search(r"m_Shader: \{fileID: \d+, guid: ([0-9a-f]{32})", body)
        if shader and shader.group(1) in animated_shaders:
            got = guid_of(path)
            if got:
                mats[got] = os.path.basename(path)[:-4]
    return mats


def offenders(text, mats):
    """
    Prefab instances that both wear an animating material and are flagged for batching.

    Scene objects are prefab instances here, so the material and the flag are both
    OVERRIDES inside the same !u!1001 block rather than fields on a GameObject. Asking the
    GameObjects instead -- which is what I did first -- finds three objects in Arena_Mine
    and misses all six hundred and seventy-eight that matter.
    """
    bad = []
    for block in re.split(r"\n--- !u!", text):
        if not block.startswith("1001 "):
            continue

        worn = [name for guid, name in mats.items() if guid in block]
        if not worn:
            continue

        for value in re.findall(
                r"propertyPath: m_StaticEditorFlags\s*\n\s*value: (-?\d+)", block):
            flags = int(value)
            if flags < 0 or flags & BATCHING_STATIC:
                bad.append((sorted(set(worn))[0], flags))
    return bad


def main():
    print("vertex_batch_check: a shader that moves its own vertices is never batched")

    mats = animated_materials()
    if not mats:
        print("  FAIL  no material in the project uses a shader tagged VertexAnimation, "
              "so this check has nothing to stand on")
        return 1

    print("  %d material(s) animate their vertices: %s"
          % (len(mats), ", ".join(sorted(mats.values()))))

    ok = True
    for path in sorted(glob.glob(os.path.join(SCENES, "*.unity"))):
        name = os.path.basename(path)[:-6]
        bad = offenders(read(path), mats)
        if bad:
            ok = False
            counts = {}
            for worn, flags in bad:
                counts[(worn, flags)] = counts.get((worn, flags), 0) + 1
            print("  FAIL  %s leaves %d animating renderer(s) in static batching"
                  % (name, len(bad)))
            for (worn, flags), many in sorted(counts.items()):
                print("          %s x%d, flags %d%s"
                      % (worn, many, flags,
                         " (batching + GI)" if flags & CONTRIBUTE_GI else " (batching)"))
            print("          Run Tools > Gulag Runners > Setup > the bake setup on this "
                  "scene and save it.")
        else:
            print("  PASS  %s" % name)

    # A check that cannot fail is not a check -- and here the useful injection is the other
    # way round, because the scenes that fail are failing for real. Take a real offender,
    # clear its flag in memory, and the same code has to call it clean.
    hurt = None
    for path in sorted(glob.glob(os.path.join(SCENES, "*.unity"))):
        text = read(path)
        if offenders(text, mats):
            hurt = (os.path.basename(path)[:-6], text)
            break

    if hurt is None:
        print("  (every scene is clean, so there was no real fault to clear and the "
              "injection was skipped)")
    else:
        name, text = hurt
        mended = re.sub(r"(propertyPath: m_StaticEditorFlags\s*\n\s*value: )-?\d+",
                        r"\g<1>0", text)
        still = offenders(mended, mats)
        held = not still
        ok &= held
        print("  %s  clearing %s's flags by hand makes the same code call it clean"
              % ("PASS" if held else "FAIL", name))

    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
