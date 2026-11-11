"""Builds Arena_Egypt from the mine's machinery and Dawid's Egyptian level.

WHY THIS WAY ROUND. Dawid's loc3_new is 201 prefab instances of Egyptian temple, three
collision slabs, a light and the default camera: a picture, in the sense arena_ready_check
means it -- a match dropped into it would count down and never start. What it is NOT is a
different SHAPE: his three slabs are the mine's three floors to the centimetre, because the
level was blocked out on top of them. The floors are at y +1.12, +3.27 and +5.39, the same
30 m across and the same depth.

So the arena that already works on that blockout is the mine, and the cheap, safe direction
is to take the MINE and change its clothes: keep the machinery, the fighters, the cameras,
the HUD, the chests, the ladders, the border posts, the poison pool and the three floors --
all of it wired, all of its fileIDs untouched -- drop the 245 instances of mine art, and
hang Dawid's 201 instances of Egypt in their place.

The other direction (copy the machinery into his scene) means remapping several hundred
documents of HUD and systems, every one of which is wired to the others by fileID. One
mistake there is a scene that opens with half a HUD and no explanation.

    python3 build_egypt.py            builds Assets/Scenes/Arena_Egypt.unity
"""
import io
import math
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
SCENES = os.path.join(HERE, "..", "..", "Gulag runners", "Assets", "Scenes")

MINE = os.path.join(SCENES, "Arena_Mine.unity")
EGYPT = os.path.join(SCENES, "loc3_new.unity")
OUT = os.path.join(SCENES, "Arena_Egypt.unity")
OUT_GUID = "e9a4c1b7d5f2486fb0c3a7e51d2946f8"

# Dawid's ids and the mine's are both around 1e9, so his are moved somewhere nothing else
# lives. Checked for collisions all the same: a silent fileID clash is two objects becoming
# one, which Unity resolves by dropping whichever it read second.
REMAP_BASE = 2_100_000_000

# The materials the floors and the posts wear. The mine's floor is handpainted dirt and its
# posts are a dungeon wall-mount's material, which is the one thing about the transplant
# that would still read as a mine.
EGYPT_GROUND = "f7a4bc5a3db4b9042bbf990596a40780"      # CGM_EgyptPack/Material/LVL_Ground
EGYPT_STONE = "2f518aa449f624949b9d99fa0722626a"       # CGM_EgyptPack/Material/LVL_3


def documents(text):
    """Every YAML document, in file order: (class, fileID, stripped, body)."""
    out = []
    for m in re.finditer(r"(?m)^--- !u!(\d+) &(-?\d+)( stripped)?\n(.*?)(?=^--- !u!|\Z)",
                         text, re.S):
        out.append((int(m.group(1)), int(m.group(2)), bool(m.group(3)), m.group(4)))
    return out


def header(text):
    """Everything before the first document: the YAML tags Unity needs."""
    return text[:text.index("--- !u!")]


def guid_of(meta):
    return re.search(r"guid: (\w+)", io.open(meta, encoding="utf-8").read()).group(1)


def instance_name(body):
    m = re.search(r"propertyPath: m_Name\n\s+value: ([^\n]*)", body)
    return m.group(1).strip() if m else ""


def kept(name):
    """The prefab instances that are not dressing, by name.

    The seed of the keep set, not all of it: what is actually kept is everything these
    REACH, worked out below. Scanning by name alone threw away the eleven weapon models
    each fighter carries -- the arena overrides the fighters' own list with the pack's
    weapons, so `weapons.Array.data[3].root` points at a prefab instance standing in the
    scene -- and the blood and spark effects with them. A fighter with no weapon models
    holds nothing, all match, and nothing in the scene says why.
    """
    return (name.startswith("Player ") or name.startswith("Chest")
            or name.startswith("Ladder") or name.startswith("GroundItem")
            or name.startswith("Poison"))


def qmul(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return (aw * bx + ax * bw + ay * bz - az * by,
            aw * by - ax * bz + ay * bw + az * bx,
            aw * bz + ax * by - ay * bx + az * bw,
            aw * bw - ax * bx - ay * by - az * bz)


def quaternion(x, y, z):
    """Unity's Quaternion.Euler, which is Ry * Rx * Rz. Checked against the ladders' own
    stored rotations to seven places before it was used to write any."""
    rx, ry, rz = (math.radians(v) for v in (x, y, z))
    qx = (math.sin(rx / 2), 0.0, 0.0, math.cos(rx / 2))
    qy = (0.0, math.sin(ry / 2), 0.0, math.cos(ry / 2))
    qz = (0.0, 0.0, math.sin(rz / 2), math.cos(rz / 2))
    return qmul(qmul(qy, qx), qz)


def mirrored_ladders(rows, taken):
    """A second ladder at each gap, mirrored in x.

    THE ARENA PICKS A WINNER OTHERWISE, and chest_check says so in seconds: the mine's two
    ladders are at x -9.14 (ground to middle) and +11.07 (middle to top), so the climb runs
    left then right. Player one meets the first chest of every tier between 4.98 and 6.02 s
    before player two does, every round, before either of them has done anything. The mine
    carries that as a recorded compromise; a new arena does not have to inherit it.

    So each ladder gets a mirror image: the same prefab, the same lean, at -x. Four ladders,
    both routes open from either end, and the offer is the same whichever side you start on.
    Dawid's level has five staircases in the art, two of them within a couple of metres of
    where these land, so it reads as a temple with stairs rather than as two ladders.

    The copy is the instance, its stripped shells (the parent's child list points at the
    Transform one) and the components added to it in the scene.
    """
    by_id = {fid: (cls, stripped, body) for cls, fid, stripped, body in rows}
    out, added = [], 0

    for cls, fid, _stripped, body in list(rows):
        if cls != 1001 or "Ladder" not in instance_name(body):
            continue

        group = [fid]
        for other_cls, other, other_stripped, other_body in rows:
            if f"m_PrefabInstance: {{fileID: {fid}}}" in other_body and other != fid:
                group.append(other)
        for m in re.finditer(r"addedObject: \{fileID: (-?\d+)\}", body):
            if int(m.group(1)) not in group:
                group.append(int(m.group(1)))

        remap = {}
        for old in group:
            new = max(taken) + 1
            while new in taken:
                new += 1
            taken.add(new)
            remap[old] = new

        for old in group:
            cls2, stripped2, body2 = by_id[old]
            text = re.sub(r"\{fileID: (-?\d+)\}",
                          lambda m: "{fileID: %d}" % remap.get(int(m.group(1)),
                                                               int(m.group(1))),
                          body2)

            if cls2 == 1001:
                x = float(re.search(r"propertyPath: m_LocalPosition\.x\n\s+value: "
                                    r"([-\d.eE+]+)", text).group(1))
                text = re.sub(r"(propertyPath: m_LocalPosition\.x\n\s+value: )[-\d.eE+]+",
                              lambda m: m.group(1) + f"{-x:.6g}", text)

                hint = re.search(r"m_LocalEulerAnglesHint\.y\n\s+value: ([-\d.eE+]+)", text)
                lean = re.search(r"m_LocalEulerAnglesHint\.z\n\s+value: ([-\d.eE+]+)", text)
                if hint and lean:
                    yaw, roll = -float(hint.group(1)), -float(lean.group(1))
                    q = quaternion(0.0, yaw, roll)
                    for axis, value in zip("xyzw", q):
                        text = re.sub(r"(propertyPath: m_LocalRotation\." + axis +
                                      r"\n\s+value: )[-\d.eE+]+",
                                      lambda m: m.group(1) + f"{value:.7f}", text)
                    text = re.sub(r"(propertyPath: m_LocalEulerAnglesHint\.y\n\s+value: )"
                                  r"[-\d.eE+]+", lambda m: m.group(1) + f"{yaw:g}", text)
                    text = re.sub(r"(propertyPath: m_LocalEulerAnglesHint\.z\n\s+value: )"
                                  r"[-\d.eE+]+", lambda m: m.group(1) + f"{roll:g}", text)

                text = re.sub(r"(propertyPath: m_Name\n\s+value: )([^\n]*)",
                              lambda m: m.group(1) + m.group(2).strip() + " mirror", text)

            out.append((cls2, remap[old], stripped2, text))

        # The parent's child list, so the copy hangs where the original does.
        parent = re.search(r"m_TransformParent: \{fileID: (-?\d+)\}", body)
        shell = next((remap[o] for o in group
                      if by_id[o][0] == 4), None)
        if parent and int(parent.group(1)) != 0 and shell is not None:
            want = int(parent.group(1))
            for i, (cls3, fid3, stripped3, body3) in enumerate(rows):
                if fid3 != want:
                    continue
                fixed = re.sub(r"(  m_Children:\n)",
                               r"\g<1>" + f"  - {{fileID: {shell}}}\n", body3, count=1)
                if "m_Children: []" in body3:
                    fixed = body3.replace("m_Children: []",
                                          f"m_Children:\n  - {{fileID: {shell}}}")
                rows[i] = (cls3, fid3, stripped3, fixed)
                break

        added += 1

    return out, added


def main():
    mine_text = io.open(MINE, encoding="utf-8").read()
    egypt_text = io.open(EGYPT, encoding="utf-8").read()

    mine = documents(mine_text)
    egypt = documents(egypt_text)
    mine_ids = {fid for _c, fid, _s, _b in mine}

    # ---------------------------------------------------------------- what goes
    by_id = {fid: (cls, body) for cls, fid, _s, body in mine}

    # Which instance a stripped shell belongs to, so a reference to a shell can be read as
    # a reference to the instance.
    owner = {}
    for cls, fid, _stripped, body in mine:
        m = re.search(r"m_PrefabInstance: \{fileID: (-?\d+)\}", body)
        if m and int(m.group(1)) != 0 and int(m.group(1)) != fid:
            owner[fid] = int(m.group(1))

    instances = {fid: instance_name(body) for cls, fid, _s, body in mine if cls == 1001}

    keep_inst = {fid for fid, name in instances.items() if kept(name)}

    # Everything that SURVIVES reaches. A fighter's weapon models, the blood, the sparks
    # and the parry flash are prefab instances in the scene that something kept points at:
    # some from the fighter's own modifications (`weapons.Array.data[3].root`), some from a
    # component added to the fighter in the scene -- PlayerEffects holds its particle
    # systems in a list of its own, which is a separate document and was missed by a pass
    # that only read the instances. Keeping the fighter and not these leaves a fighter who
    # holds nothing and four dangling fileIDs that name nothing in particular.
    #
    # So this is a fixed point over the whole scene rather than a walk from the seeds: who
    # survives decides what else survives, and that changes who survives.
    while True:
        drop_now = set(instances) - keep_inst
        gone = set(drop_now)
        for fid, inst in owner.items():
            if inst in drop_now:
                gone.add(fid)
        for fid in drop_now:
            for m in re.finditer(r"addedObject: \{fileID: (-?\d+)\}", by_id[fid][1]):
                gone.add(int(m.group(1)))

        grew = False
        for fid, (cls, body) in by_id.items():
            if fid in gone:
                continue

            # A parent's child list and the scene's root list are not WIRING, they are the
            # hierarchy -- and the hierarchy is exactly what this is pruning. Counting them
            # as reasons to keep something makes everything reachable from the Arena group,
            # which is every piece of art in the mine.
            if cls == 1660057539:
                continue
            if cls == 4:
                body = re.sub(r"m_Children:.*?m_Father:", "m_Father:", body, flags=re.S)

            for m in re.finditer(r"\{fileID: (-?\d+)(, guid: \w+)?\}", body):
                if m.group(2):
                    continue                      # into another asset, not this scene
                target = int(m.group(1))
                if target == 0:
                    continue
                reached = owner.get(target, target if target in instances else None)
                if reached is not None and reached not in keep_inst:
                    keep_inst.add(reached)
                    grew = True
        if not grew:
            break

    drop_instances = set(instances) - keep_inst
    keep_instances = sorted(instances[f] for f in keep_inst)

    # The stripped shells that belong to a dropped instance go with it. They are what the
    # parent's m_Children points at, so missing one leaves a dangling reference, and a
    # dangling reference in a scene is an object Unity cannot place.
    #
    # And so do the components ADDED to it in the scene: the mine marks five gold crystals
    # with a SimCollider of kind Ignore, and those components are documents in their own
    # right whose owner is the instance. Left behind, they are five components attached to
    # nothing.
    drop = set(drop_instances)
    for cls, fid, stripped, body in mine:
        m = re.search(r"m_PrefabInstance: \{fileID: (-?\d+)\}", body)
        if m and int(m.group(1)) in drop_instances:
            drop.add(fid)
    for fid in drop_instances:
        for m in re.finditer(r"addedObject: \{fileID: (-?\d+)\}", by_id[fid][1]):
            drop.add(int(m.group(1)))

    # ---------------------------------------------------------------- Dawid's art, moved
    free = REMAP_BASE
    remap = {}
    for cls, fid, _stripped, _body in egypt:
        if cls != 1001:
            continue
        while free in mine_ids or free in remap.values():
            free += 1
        remap[fid] = free
        free += 1

    def move_local(body):
        """Remap LOCAL references only. A reference with a guid points into another asset
        -- the prefab this is an instance of -- and renumbering those would point the
        instance at nothing."""
        def one(m):
            if "guid:" in m.group(0):
                return m.group(0)
            fid = int(m.group(1))
            return "{fileID: %d}" % remap.get(fid, fid)
        return re.sub(r"\{fileID: (-?\d+)\}", one, body)

    # Dawid dropped the two fighters into his level to walk around it, which is the right
    # way to check a blockout and the wrong thing to copy: the machinery comes from the
    # mine, fighters included, and a second pair would be four fighters in a 1v1. Nothing
    # would say so either -- arena_ready_check counts at least two.
    PLAYERS = ("b3006e46c965d6d43b96f032285cfa67", "1e9af09b57c7e4c40b90bc6498d0b3f2",
               "ab46f761c6bebd842904529b4639c12a")

    added, skipped = [], 0
    for cls, fid, _stripped, body in egypt:
        if cls != 1001:
            continue
        src = re.search(r"m_SourcePrefab: \{fileID: \d+, guid: (\w+)", body)
        if src and src.group(1) in PLAYERS:
            skipped += 1
            del remap[fid]
            continue
        body = move_local(body)
        # At the scene root, as they are in his scene. Parenting them under the Arena group
        # would mean writing a stripped Transform for each -- 201 shells whose
        # m_CorrespondingSourceObject has to name the right transform inside the right
        # prefab. "Tools > Setup > Tidy the hierarchy" does that in the editor, which is
        # what it is for, and hierarchy_check says so.
        body = re.sub(r"m_TransformParent: \{fileID: -?\d+\}",
                      "m_TransformParent: {fileID: 0}", body)
        added.append((cls, remap[fid], False, body))

    # ---------------------------------------------------------------- write it
    rows = []
    for cls, fid, stripped, body in mine:
        if fid in drop:
            continue

        # Children and roots lose the dropped art.
        if cls == 4 or cls == 1660057539:
            def keep_child(m):
                return "" if int(m.group(1)) in drop else m.group(0)
            body = re.sub(r"(?m)^  - \{fileID: (-?\d+)\}\n", keep_child, body)
            body = re.sub(r"(?m)^  - \{fileID: (-?\d+)\}$",
                          lambda m: "" if int(m.group(1)) in drop else m.group(0), body)

        # An empty children list has to be written as Unity writes it, or the document is
        # invalid and the object is silently left out of the scene.
        if cls == 4:
            body = re.sub(r"  m_Children:\s*\n  m_Father:", "  m_Children: []\n  m_Father:",
                          body)

        rows.append((cls, fid, stripped, body))

    # Dawid's art joins the roots.
    for i, (cls, fid, stripped, body) in enumerate(rows):
        if cls != 1660057539:
            continue
        extra = "".join(f"  - {{fileID: {remap[f]}}}\n"
                        for _c, f, _s, _b in egypt if _c == 1001 and f in remap)
        body = body.rstrip("\n") + "\n" + extra
        rows[i] = (cls, fid, stripped, body)
        break

    rows += added

    # And a mirror of each ladder, so the climb is not a head start.
    taken = {fid for _c, fid, _s, _b in rows}
    mirrors, mirrored = mirrored_ladders(rows, taken)
    rows += mirrors

    out = [header(mine_text)]
    for cls, fid, stripped, body in rows:
        out.append(f"--- !u!{cls} &{fid}{' stripped' if stripped else ''}\n")
        out.append(body if body.endswith("\n") else body + "\n")
    text = "".join(out)

    # ---------------------------------------------------------------- Egyptian stone
    text = text.replace("guid: bdca320f74588434f875e8e66dbaba74",
                        f"guid: {EGYPT_STONE}")
    text = text.replace("guid: 9ae1d73fdebd4f04f971f7144044b8c3",
                        f"guid: {EGYPT_GROUND}")

    io.open(OUT, "w", encoding="utf-8").write(text)
    io.open(OUT + ".meta", "w", encoding="utf-8").write(
        "fileFormatVersion: 2\n"
        f"guid: {OUT_GUID}\n"
        "DefaultImporter:\n"
        "  externalObjects: {}\n"
        "  userData: \n"
        "  assetBundleName: \n"
        "  assetBundleVariant: \n")

    print(f"  kept {len(keep_instances)} prefab instance(s): "
          f"{', '.join(sorted(keep_instances))}")
    print(f"  dropped {len(drop_instances)} instance(s) of mine art "
          f"({len(drop)} document(s) with their shells)")
    print(f"  added {len(added)} instance(s) of Dawid's Egypt"
          + (f", skipping {skipped} fighter(s) he had dropped in to test it"
             if skipped else ""))
    print(f"  mirrored {mirrored} ladder(s), so both routes open from either end")
    print(f"  wrote {os.path.relpath(OUT, os.path.join(HERE, '..', '..'))}")


if __name__ == "__main__":
    sys.exit(main())
