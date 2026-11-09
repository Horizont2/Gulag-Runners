"""Does anything come out of the chests, and can you see it when it does?

The loot pool is simulation: a chest opens, an item enters a slot, and the slot is
pickable whether or not anything is drawn for it. Drawing is a separate thing -- one
GroundItemView per slot, wired into a LootView. Miss that out and the arena still works
perfectly, chests still open, items still go in and out of the pool, and the player sees
nothing happen at all. That is what the Nordic arena did: it had chests and no Loot group,
so the one visible reward in the scavenge phase was invisible.

It is a quiet failure in both directions. Too FEW views and the extra slots are pickable
but unseen, which reads as loot you walk through. So this counts them against the pool,
and then checks the thing underneath: that whatever each chest actually contains has a
model to be drawn with, or at least falls back to the stand-in rather than nothing.

    python3 loot_check.py
"""
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import arena_demo_geometry as geometry           # noqa: E402

ASSETS = os.path.join(HERE, "..", "..", "Gulag runners", "Assets")
SCENES = os.path.join(ASSETS, "Scenes")
SRC = os.path.join(ASSETS, "_Game", "Presentation")


def guid_of(script):
    return re.search(r"guid: ([0-9a-f]{32})",
                     open(os.path.join(SRC, script + ".cs.meta"),
                          encoding="utf-8").read()).group(1)


def components(text, guid):
    out = []
    for m in re.finditer(r"--- !u!114 &(-?\d+)( stripped)?\nMonoBehaviour:\n(.*?)(?=\n--- !u!|\Z)",
                         text, re.S):
        if guid in m.group(3):
            out.append((int(m.group(1)), bool(m.group(2)), m.group(3)))
    return out


def ground_item_models():
    """Which ItemIds the GroundItem prefab can draw, and whether it has a stand-in."""
    path = os.path.join(ASSETS, "Prefabs", "GroundItem.prefab")
    if not os.path.exists(path):
        return None, False
    text = open(path, encoding="utf-8", errors="ignore").read()
    block = re.search(r"  models:\n((?:  - item: .*\n    root: .*\n)*)", text)
    items = {int(x) for x in re.findall(r"- item: (\d+)", block.group(1))} if block else set()
    fallback = re.search(r"  fallbackModel: \{fileID: (-?\d+)\}", text)
    return items, bool(fallback and fallback.group(1) != "0")


def worn_parts_land_on_the_floor(problems, notes):
    """Is a body part borrowed as a prop brought back to the drop point?

    The nine armour models are parts of the modular character -- the same meshes
    PlayerArmourView switches on when the item is worn -- and a character's parts are
    authored WHERE THEY SIT ON HIM: the helmets around y 1.7, the chests around 1.2, the
    belts around 0.9. Used as a prop, each one therefore floats about a metre above the
    thing it belongs to.

    ItemModels.Fit scales a model and never recentres it, deliberately: a weapon's origin
    is its grip and moving that would be wrong. So the offset has to be authored, on a
    child beneath the root -- beneath, because Fit multiplies the ROOT's scale and a
    position on the root itself is in the parent's space and would not scale with it.

    Which makes "the offset is zero" a silent fault with no symptom in any file: the item
    is the right size, the right mesh, the right material, and a metre in the air. Hence
    a rule, and hence this length of explanation for one comparison.
    """
    path = os.path.join(ASSETS, "Prefabs", "GroundItem.prefab")
    if not os.path.exists(path):
        return
    text = open(path, encoding="utf-8", errors="ignore").read()

    docs = {}
    for m in re.finditer(r"(?m)^--- !u!(\d+) &(-?\d+)( stripped)?\n(.*?)(?=^--- !u!|\Z)",
                         text, re.S):
        docs[int(m.group(2))] = (int(m.group(1)), m.group(4))

    # Which meshes come from a rig: an FBX that any SkinnedMeshRenderer in the project
    # reads. Asked rather than listed, so a second character pack is covered too.
    skinned = set()
    for where, _dirs, files in os.walk(os.path.join(ASSETS)):
        for f in files:
            if not f.endswith((".prefab", ".unity")):
                continue
            body = open(os.path.join(where, f), encoding="utf-8", errors="ignore").read()
            for m in re.finditer(r"(?m)^--- !u!137 &-?\d+\n(.*?)(?=^--- !u!|\Z)", body, re.S):
                g = re.search(r"m_Mesh: \{fileID: -?\d+, guid: ([0-9a-f]{32})", m.group(1))
                if g:
                    skinned.add(g.group(1))

    names = {f: (re.search(r"m_Name: (.*)", b).group(1).strip()
                 if re.search(r"m_Name: (.*)", b) else None)
             for f, (c, b) in docs.items() if c == 1}

    def transform_of(go):
        for f, (c, b) in docs.items():
            if c == 4 and f"m_GameObject: {{fileID: {go}}}" in b:
                return f
        return None

    def walk(go, offset, root_name, depth=0):
        """Every mesh under this object, with how far it sits from the root."""
        if depth > 4:
            return
        tr = transform_of(go)
        if tr is None:
            return
        body = docs[tr][1]
        pos = re.search(r"m_LocalPosition: \{x: ([-\d.e+]+), y: ([-\d.e+]+), z: ([-\d.e+]+)\}",
                        body)
        here = offset + (float(pos.group(2)) if pos else 0.0)

        comps = [int(x) for x in re.findall(r"- component: \{fileID: (-?\d+)\}",
                                            docs[go][1])]
        for c in comps:
            if docs.get(c, (0, ""))[0] != 33:
                continue
            g = re.search(r"m_Mesh: \{fileID: -?\d+, guid: ([0-9a-f]{32})", docs[c][1])
            if g and g.group(1) in skinned and abs(here) < 0.3:
                problems.append(
                    f"GroundItem.prefab: {root_name} is drawn with a mesh from a rigged "
                    f"character and sits {here:+.2f} m from the item's own origin. Those "
                    f"parts are authored where they sit on a body -- a helmet around 1.7 m "
                    f"up -- so without an offset beneath the root it floats about a metre "
                    f"over the drop. Fit scales a model and never recentres it.")

        kids = re.search(r"m_Children:(.*?)m_Father", body, re.S)
        for k in re.findall(r"- \{fileID: (-?\d+)\}", kids.group(1) if kids else ""):
            kt = docs.get(int(k))
            if kt is None or kt[0] != 4:
                continue
            kgo = re.search(r"m_GameObject: \{fileID: (-?\d+)\}", kt[1])
            if kgo:
                walk(int(kgo.group(1)), here, root_name, depth + 1)

    block = re.search(r"  models:\n((?:  - item: .*\n    root: .*\n)*)", text)
    rows = re.findall(r"- item: (\d+)\n    root: \{fileID: (-?\d+)\}",
                      block.group(1)) if block else []
    borrowed = 0
    for _item, root in rows:
        r = int(root)
        if r == 0 or docs.get(r, (0, ""))[0] != 1:
            continue
        before = len(problems)
        walk(r, 0.0, names.get(r) or str(r))
        borrowed += 1 if len(problems) == before else 0
    notes.append(f"{len(rows)} model row(s) on the ground item, offsets checked")


def rolls(problems, notes):
    """Will the chests ever pay out anything different?

    Two ways for a looter to deal the same hand for ever, and the arenas had both.

    A chest prefab can PIN its contents, which LootTable allows on purpose -- "pinning
    one chest is how a test, or a tutorial, stops being at the mercy of a roll". All
    three of them shipped pinned, to items 2, 21 and 3. Arena_Forest overrode its six
    instances back to a roll; the fortress and the mine did not, so twelve chests had
    three items between them, and the kind -- crate, locker, safe, each with its own
    tier odds -- was doing nothing at all.

    And the rolling seed starts from a constant written into MatchState, so round ONE of
    every session deals the same six chests. Only round one, because each round rolls
    the seed forward, which is why it reads as "the loot never changes" rather than as a
    seed somebody could change.
    """
    import glob
    for path in sorted(glob.glob(os.path.join(ASSETS, "Prefabs", "Chest_*.prefab"))):
        text = open(path, encoding="utf-8", errors="ignore").read()
        m = re.search(r"^  contents: (\d+)$", text, re.M)
        if not m:
            continue
        who = os.path.basename(path)[:-7]
        if m.group(1) == "0":
            notes.append(f"{who} rolls its contents from the drop table")
            continue
        problems.append(
            f"{who} has contents pinned to item {m.group(1)}, so every chest of that "
            f"kind in every arena pays out the same thing in every round -- and its "
            f"Kind, which is the thing that decides its tier odds, decides nothing. "
            f"Pin an INSTANCE for a test; leave the prefab rolling.")

    for path in sorted(glob.glob(os.path.join(SCENES, "Arena_*.unity"))):
        text = open(path, encoding="utf-8", errors="ignore").read()
        for m in re.finditer(r"--- !u!114 &-?\d+\nMonoBehaviour:\n(.*?)(?=\n--- !u!|\Z)",
                             text, re.S):
            if guid_of("MatchState") not in m.group(1):
                continue
            seed = re.search(r"^  seed: (\d+)$", m.group(1), re.M)
            fresh = re.search(r"^  freshEachRun: (\d)$", m.group(1), re.M)
            who = os.path.basename(path)
            if seed and seed.group(1) != "0":
                notes.append(f"{who}: seed pinned to {seed.group(1)}, so every match "
                             f"deals the same rounds -- deliberate, and repeatable")
            elif fresh is not None and fresh.group(1) == "0":
                problems.append(
                    f"{who}: the loot seed is not pinned and not randomised either, so "
                    f"the rolling seed starts from the constant in MatchState and round "
                    f"ONE of every session deals the same six chests. Switch Fresh Each "
                    f"Run on, or pin Seed if the repetition is wanted.")
            else:
                notes.append(f"{who}: a new seed every session, rolled forward each round")


def tier_shows(problems, notes):
    """Can the tier colour reach the screen at all?

    GroundItemView and PlayerLoadoutView both say what an item is worth by writing
    _EmissionColor into a MaterialPropertyBlock. A block can set a property; it cannot
    turn on a shader KEYWORD, and URP's Lit compiles emission out unless the material's
    own Emission box is ticked. So on a material with emission off the tier is written
    every frame and lands nowhere -- the loot is the same grey whatever it is worth, and
    nothing anywhere says so.

    GroundItemView's own tooltip knows this: "Emission through a MaterialPropertyBlock
    does nothing AT ALL unless the material has its Emission tick box on". It is checked
    here because a material is one tick box away from silently undoing the feature.
    """
    import glob
    seen = []
    for path in sorted(glob.glob(os.path.join(ASSETS, "Prefabs", "GroundItem.prefab"))):
        text = open(path, encoding="utf-8", errors="ignore").read()
        for g in sorted(set(re.findall(r"guid: ([0-9a-f]{32}), type: 2\}", text))):
            mat = _material(g)
            if mat is None:
                continue
            seen.append((os.path.basename(mat), _emits(mat)))
    for who, on in seen:
        if on:
            notes.append(f"{who}: emission on, so the tier colour reaches it")
        else:
            problems.append(
                f"{who} is worn by the loot on the floor and has its Emission box OFF, so "
                f"the tier colour GroundItemView writes into _EmissionColor every frame "
                f"is compiled out of the shader and never drawn. Every find looks the "
                f"same whatever it is worth.")


def _emits(mat):
    """Is this material's Emission box on.

    Two serialisations, and reading only the old one is how this check first reported
    the opposite of the truth. Unity wrote keywords as a single m_ShaderKeywords string
    and now writes m_ValidKeywords as a list; a material saved by a current editor has
    an empty old field whatever its emission is set to.
    """
    body = open(mat, encoding="utf-8", errors="ignore").read()
    valid = re.search(r"  m_ValidKeywords:\n((?:  - \S+\n)*)", body)
    if valid and "_EMISSION" in valid.group(1):
        return True
    old = re.search(r"  m_ShaderKeywords: (.*)", body)
    return bool(old and "_EMISSION" in old.group(1))


def _material(guid):
    import glob
    for meta in glob.glob(os.path.join(ASSETS, "**", "*.mat.meta"), recursive=True):
        if f"guid: {guid}" in open(meta, encoding="utf-8", errors="ignore").read():
            return meta[:-5]
    return None


def every_item_drawn(drawable, problems, notes):
    """Does everything the chests can roll have something of its own to be drawn as?

    GroundItemView falls back to a stand-in cube for an item with no model, and the
    tooltip is right that a stand-in is the truth until there is art. What it is not is
    an answer to the question the scavenge phase asks: a player deciding whether to
    cross the room for something has to be able to tell a sword from a helmet from the
    other end of it, and nine identical cubes say only "something".

    So the rule is per ITEM and not per mesh: anything the drop table can roll needs its
    own entry, even if that entry is a box of its own shape.

    All of them now have a real one. The eleven weapons use the same prefabs the
    fighters hold. The nine suits, helmets and belts were a cube each, shaped by scale,
    on the stated grounds that no mesh for them existed anywhere in the project -- which
    was wrong: they are the parts of the modular character that PlayerArmourView
    switches on when you WEAR them, so the thing on the floor is now the thing you will
    be wearing. Only the bandage is still a box, having no part on the character.
    """
    table = os.path.join(ASSETS, "_Game", "Sim", "Items", "ItemTable.cs")
    ids = os.path.join(ASSETS, "_Game", "Sim", "Items", "ItemId.cs")
    if not (os.path.exists(table) and os.path.exists(ids)):
        return
    numbers = {n: int(v) for n, v in
               re.findall(r"^\s+(\w+) = (\d+),?$", open(ids, encoding="utf-8").read(), re.M)}
    body = open(table, encoding="utf-8", errors="ignore").read()
    rollable = {}
    for m in re.finditer(r"t\[\(int\)ItemId\.(\w+)\] = new ItemDef\n\s*\{(.*?)\n\s*\};",
                         body, re.S):
        w = re.search(r"Weight = (\d+)", m.group(2))
        if w and int(w.group(1)) > 0 and m.group(1) in numbers:
            rollable[numbers[m.group(1)]] = m.group(1)

    missing = sorted(i for i in rollable if i not in drawable)
    notes.append(f"{len(rollable)} item(s) the drop table can roll, "
                 f"{len(rollable) - len(missing)} of them with a model of their own")
    if missing:
        problems.append(
            "the drop table can roll " + ", ".join(f"{rollable[i]} ({i})" for i in missing)
            + " and GroundItem.prefab has no model for "
            + ("any of them" if len(missing) > 1 else "it")
            + ", so " + ("they all land" if len(missing) > 1 else "it lands")
            + " as the same stand-in cube. A player across the room cannot tell one "
              "from another, which is the whole decision the scavenge phase is made of.")


def main(argv):
    wanted = argv or geometry.arena_scenes()
    problems, notes = [], []

    match_guid = guid_of("MatchState")
    loot_guid = guid_of("LootView")
    view_guid = guid_of("GroundItemView")

    rolls(problems, notes)
    tier_shows(problems, notes)

    drawable, has_fallback = ground_item_models()
    notes.append(f"GroundItem.prefab draws item(s) {sorted(drawable)} directly" +
                 (" and everything else as the stand-in" if has_fallback
                  else " and has NO stand-in"))

    every_item_drawn(drawable, problems, notes)
    worn_parts_land_on_the_floor(problems, notes)

    for name in wanted:
        path = os.path.join(SCENES, name)
        text = open(path, encoding="utf-8", errors="ignore").read()
        scene = geometry.Scene(path)
        chests = scene.prefab_chests()

        state = components(text, match_guid)
        if not state:
            notes.append(f"{name}: no MatchState, so there is no pool to draw")
            continue
        capacity = int((re.search(r"  groundItemCapacity: (\d+)", state[0][2])
                        or re.match(r"(0)", "0")).group(1))

        loots = components(text, loot_guid)
        views = [v for v in components(text, view_guid)]
        wired = 0
        if loots:
            block = re.search(r"  views:\n((?:  - \{fileID: -?\d+\}\n)*)", loots[0][2])
            wired = len(re.findall(r"\{fileID: (-?\d+)\}", block.group(1))) if block else 0

        notes.append(f"{name}: pool of {capacity}, {len(loots)} LootView, "
                     f"{len(views)} GroundItemView in the scene, {wired} wired into it, "
                     f"{len(chests)} chest(s)")

        if chests and not loots:
            problems.append(
                f"{name}: {len(chests)} chest(s) and no LootView, so everything they pay "
                f"out is invisible. The pool still works and the items are still pickable "
                f"— the player just never sees one. Add a Loot object with a LootView and "
                f"{capacity} GroundItem prefab instances under it.")
            continue
        if not loots:
            continue

        if wired < capacity:
            problems.append(
                f"{name}: the pool holds {capacity} item(s) and only {wired} view(s) are "
                f"wired, so {capacity - wired} slot(s) are pickable and invisible — loot "
                f"the player walks through.")

        # And what the chests in THIS arena actually pay out. Contents of 0 is ItemId.None,
        # which is NOT an empty chest: LootTable reads it as "roll this kind from the drop
        # table", which is how a looter is supposed to behave. A fixed item is the special
        # case, and the only one whose model can be checked from here.
        rolling = [c for c in chests if c[2] == 0]
        fixed = [c for c in chests if c[2] != 0]
        if rolling:
            notes.append(f"  {len(rolling)} chest(s) roll their contents from the drop "
                         f"table, so what is drawn depends on the roll")
        for _box, _kind, contents, label in fixed:
            if contents in drawable:
                notes.append(f"  {label} always gives item {contents}, which has a model")
                continue
            if has_fallback:
                notes.append(f"  {label} always gives item {contents}, which has no model: "
                             f"drawn as the stand-in")
            else:
                problems.append(
                    f"{name}: {label} always gives item {contents}, which has no model and "
                    f"there is no stand-in either, so opening it shows nothing at all.")

        if rolling and not has_fallback:
            missing = sorted(set(range(1, 32)) - drawable)
            problems.append(
                f"{name}: {len(rolling)} chest(s) roll from the drop table and there is no "
                f"stand-in model, so any roll outside {sorted(drawable)} pays out something "
                f"invisible (items {missing[:8]}...).")

    for n in notes:
        print("  note: " + n if not n.startswith("  ") else "  " + n)
    for p in problems:
        print("  " + p)
    print(f"loot_check: {len(problems)} problem(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
