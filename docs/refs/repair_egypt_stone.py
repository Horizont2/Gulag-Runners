"""Twenty-nine broken asset references in Arena_Egypt, from one global find-and-replace.

WHAT WENT WRONG. build_egypt.py re-skinned the mine's border posts and floors in Egyptian
stone the cheapest way there is:

    text = text.replace("guid: bdca320f74588434f875e8e66dbaba74", EGYPT_STONE)

That guid is Sword_WallMount.fbx, and the mine's posts wear a material that lives INSIDE
it -- `{fileID: 5653053145819516872, guid: bdca..., type: 3}`, a sub-object reference. The
replacement changed the guid and left the fileID, so those references now read "object
5653053145819516872 of LVL_3.mat", and a .mat file holds exactly one object, 2100000. A
reference to an id an asset does not have is a MISSING material, which Unity draws in its
own default grey -- twelve `CageLeft` renderers and twelve prefab instances of fences and
a chest, in a night arena, lit grey.

And the same two words hit Dawid's own art: he had two Sword_WallMount instances on the
temple wall, whose `m_SourcePrefab` is `{fileID: 100100000, guid: bdca..., type: 3}`. They
are now prefab instances of a MATERIAL, which Unity cannot load at all, so the pair is
missing from the level and nothing in the project said so -- scene_check follows local
fileIDs and guids, and this is neither: it is a valid guid with an id that is not in it.

WHAT THIS DOES. Both halves, by what the reference IS rather than by its guid:

  * the material sub-object  -> the material, properly: {fileID: 2100000, type: 2}
  * everything else that was Sword_WallMount -> back to Sword_WallMount, so his two wall
    swords come back exactly as he placed them

build_egypt.py is fixed in the same commit, so a rebuild does it by reference rather than
by string. This script is for the scene that is already built and has been worked on in
Unity since, where a rebuild would throw that work away.

    python3 repair_egypt_stone.py
"""
import io
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
SCENE = os.path.join(HERE, "..", "..", "Gulag runners", "Assets", "Scenes",
                     "Arena_Egypt.unity")

SWORD = "bdca320f74588434f875e8e66dbaba74"      # Sword_WallMount.fbx
STONE = "2f518aa449f624949b9d99fa0722626a"      # CGM_EgyptPack/Material/LVL_3.mat
MATERIAL_IN_FBX = "5653053145819516872"         # the material the mine's posts wore


def main():
    text = io.open(SCENE, encoding="utf-8").read()
    before = text

    # The material, as a material.
    was = text.count(f"fileID: {MATERIAL_IN_FBX}, guid: {STONE}, type: 3")
    text = text.replace(f"fileID: {MATERIAL_IN_FBX}, guid: {STONE}, type: 3",
                        f"fileID: 2100000, guid: {STONE}, type: 2")
    print(f"  {was} renderer slot(s) now point at LVL_3.mat itself")

    # And everything else that used to be the sword, back to the sword.
    rest = re.findall(r"fileID: (\d+), guid: " + STONE + r", type: 3", text)
    text = re.sub(r"(fileID: \d+, guid: )" + STONE + r"(, type: 3)",
                  r"\g<1>" + SWORD + r"\g<2>", text)
    print(f"  {len(rest)} reference(s) back to Sword_WallMount: "
          f"{', '.join(sorted(set(rest))) or 'none'}")

    left = re.findall(r"fileID: (\d+), guid: " + STONE, text)
    bad = sorted({f for f in left if f != "2100000"})
    if bad:
        print(f"  STILL WRONG: {', '.join(bad)} against a .mat that holds only 2100000")
        return 1

    if text == before:
        print("  nothing to repair")
        return 0

    io.open(SCENE, "w", encoding="utf-8").write(text)
    print("  wrote Arena_Egypt.unity")
    return 0


if __name__ == "__main__":
    sys.exit(main())
