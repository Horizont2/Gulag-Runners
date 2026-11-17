"""The fighters in the arena are drawn at a hundredth of their size, and two weapons at
a hundred and a thousand times theirs.

fighter_check, on the arena the moment it became loc3_new.unity:

    Player 1 is scaled 0.005 where its prefab says 1 -- 0.005x. The fighter's size is a
    fact about the FIGHTER, and nothing else reads this transform: the sim runs on boxes
    baked from the arena, the camera frames on the body height in the tuning, and the HUD
    comes from MatchState. So the match starts, counts down and follows two fighters
    correctly with nobody on screen.

Both instances carry a scale override of 0.005 on their root, against prefabs that now say
1 and 0.5. Arena_Mine, which these instances were copied from, carries no override at all:
its fighters take the prefab's size. So the override is not a decision about this arena, it
is a leftover that stopped agreeing with the prefab when the prefab was fixed -- and the
scene that was not open in Unity at the time kept it.

socket_check finds the same shape on the other side of the fight:

    Player 2: Axe is authored at 1000x the size its own prefab says ... the game looks
    right and the scene does not: this is a 1000x weapon in a fighter's fist in the
    editor. Nothing can be lined up by eye against it, and the next person to scale
    something to match it will be matching a mistake.

Player 1 carries the same two weapons at 1, so the pair that is meant to be identical is
not. ItemModels.Fit measures what is drawn and divides it out at run time, which is why
this survived: it is invisible in play and wrong in every screenshot, every socket
measurement and every eye.

The repair is the one both checks name: take the override off the fighters and put the
weapons back to what their prefabs say. Nothing else in the scene refers to either.

    python3 repair_fighter_scale.py
"""
import io
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
SCENES = os.path.join(HERE, "..", "..", "Gulag runners", "Assets", "Scenes")

# What a fighter instance may not override, because the prefab is the fact.
FIELDS = ("m_LocalScale.x", "m_LocalScale.y", "m_LocalScale.z")

# And how far a weapon model may be authored from its own prefab before it is a mistake
# rather than a nudge. Ten times: a weapon somebody has eased by a fifth is a decision, a
# weapon at a hundred times is a unit that got away.
WAY_OUT = 10.0


_PREFABS = {}


def prefab_scale(guid):
    """The root scale of a prefab, by guid -- the fact a scene override has to agree with."""
    if guid in _PREFABS:
        return _PREFABS[guid]
    assets = os.path.join(HERE, "..", "..", "Gulag runners", "Assets")
    found = None
    for base, dirs, files in os.walk(assets):
        dirs[:] = [d for d in dirs if d not in ("Library", "Temp")]
        for f in files:
            if not f.endswith(".prefab.meta"):
                continue
            head = io.open(os.path.join(base, f), encoding="utf-8",
                           errors="replace").read(300)
            if "guid: " + guid not in head:
                continue
            text = io.open(os.path.join(base, f[:-5]), encoding="utf-8",
                           errors="replace").read()
            m = re.search(r"m_LocalScale: \{x: ([-\d.eE+]+)", text)
            found = float(m.group(1)) if m else None
            break
        if found is not None:
            break
    _PREFABS[guid] = found
    return found


def main():
    bad = 0
    for name in sorted(os.listdir(SCENES)):
        if not name.endswith(".unity"):
            continue
        path = os.path.join(SCENES, name)
        text = io.open(path, encoding="utf-8").read()
        before = text

        for block in re.finditer(r"(?ms)^--- !u!1001 &-?\d+\n(.*?)(?=^--- !u!|\Z)", text):
            body = block.group(1)
            who = re.search(r"propertyPath: m_Name\n\s+value: ([^\n]*)", body)
            if not who or not who.group(1).strip().startswith("Player"):
                continue

            fixed = body
            for field in FIELDS:
                fixed = re.sub(
                    r"    - target: \{fileID: \d+, guid: \w+, type: 3\}\n"
                    r"      propertyPath: " + re.escape(field) + r"\n"
                    r"      value: [-\d.eE+]+\n"
                    r"      objectReference: \{fileID: 0\}\n", "", fixed)
            if fixed != body:
                text = text.replace(body, fixed)
                print(f"  {name}: {who.group(1).strip()} takes its prefab's size again")
                bad += 1

        # And the weapon models hanging in their fists.
        for block in re.finditer(r"(?ms)^--- !u!1001 &-?\d+\n(.*?)(?=^--- !u!|\Z)", text):
            body = block.group(1)
            who = re.search(r"propertyPath: m_Name\n\s+value: ([^\n]*)", body)
            if not who or not who.group(1).strip().endswith("__wired"):
                continue
            src = re.search(r"m_SourcePrefab: \{fileID: \d+, guid: (\w+)", body)
            if src is None:
                continue
            says = prefab_scale(src.group(1))
            if says is None:
                continue

            fixed = body
            for axis in "xyz":
                at = re.search(r"(propertyPath: m_LocalScale\." + axis +
                               r"\n      value: )([-\d.eE+]+)", fixed)
                if at is None:
                    continue
                now = float(at.group(2))
                if now <= 0 or says <= 0:
                    continue
                ratio = max(now / says, says / now)
                if ratio < WAY_OUT:
                    continue
                fixed = fixed[:at.start()] + at.group(1) + f"{says:g}" + fixed[at.end():]
            if fixed != body:
                text = text.replace(body, fixed)
                print(f"  {name}: {who.group(1).strip()} back to the {says:g} its prefab says")
                bad += 1

        if text != before:
            io.open(path, "w", encoding="utf-8").write(text)

    print(f"repair_fighter_scale: {bad} fighter(s) un-shrunk"
          if bad else "repair_fighter_scale: nothing to repair")
    return 0


if __name__ == "__main__":
    sys.exit(main())
