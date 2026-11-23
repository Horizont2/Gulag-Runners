#!/usr/bin/env python3
"""Takes animating props out of static batching.

Reported twice, the second time as "the mine's lanterns still swing SIDEWAYS and a long
way from where they are -- something is wrong with the shader". Nothing is wrong with the
shader. Static batching bakes a mesh's vertices into WORLD space and hands the renderer an
identity transform, so a shader that sways a vertex about the object's own origin ends up
swaying it about the world's. A lantern hanging at x -9 then swings through an arc nine
metres wide, horizontally, nowhere near the hook it is on.

vertex_batch_check has been reporting this for a while -- Arena_Mine alone leaves ten
animating renderers batched, seven GR_Lantern and three GR_LanternStill -- and its advice
is to run Tools > Gulag Runners > Setup > the bake setup, which nobody has. So this does
what that tool does to the one field that matters, in the scene files.

    python3 docs/refs/unbatch_animated.py          # report only
    python3 docs/refs/unbatch_animated.py --write  # clear the flags

Both bits go, exactly as BakeSetup clears them: BatchingStatic because it is the fault,
and ContributeGI because a prop that moves must not be burnt into a lightmap either -- its
shadow would stay where the prop no longer is.

It finds its targets through vertex_batch_check rather than repeating the search, because
a copy of that logic is a copy that drifts: the thing being matched is an override inside
a prefab-instance block, not a field on a GameObject, and asking the GameObjects finds
three objects in Arena_Mine and misses the ten that matter.
"""

import os
import re
import sys

import vertex_batch_check as V

BATCHING_STATIC = 4
CONTRIBUTE_GI = 1


def main():
    write = "--write" in sys.argv
    mats = V.animated_materials()
    if not mats:
        print("unbatch_animated: no vertex-animating materials found, so nothing to free")
        return 1

    total = 0
    for name in sorted(os.listdir(V.SCENES)):
        if not name.endswith(".unity"):
            continue

        path = os.path.join(V.SCENES, name)
        text = V.read(path)
        if text is None:
            continue
        if not V.offenders(text, mats):
            continue

        # Block by block, so only the prefab instances that actually wear an animating
        # material are touched. A scene-wide substitution would unflag the walls.
        out, freed = [], 0
        for i, block in enumerate(re.split(r"\n--- !u!", text)):
            head = "" if i == 0 else "\n--- !u!"

            if block.startswith("1001 ") and any(g in block for g in mats):
                def clear(m):
                    nonlocal freed
                    was = int(m.group(2))
                    now = was & ~BATCHING_STATIC & ~CONTRIBUTE_GI if was > 0 else 0
                    if now != was:
                        freed += 1
                    return m.group(1) + str(now)

                block = re.sub(
                    r"(propertyPath: m_StaticEditorFlags\s*\n\s*value: )(-?\d+)",
                    clear, block)

            out.append(head + block)

        if freed:
            print(f"{name}: {freed} animating renderer(s) taken out of static batching")
            total += freed
            if write:
                with open(path, "w", encoding="utf-8") as f:
                    f.write("".join(out))

    print(f"\n{total} renderer(s) freed"
          + ("" if write else "  (nothing written -- pass --write)"))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
