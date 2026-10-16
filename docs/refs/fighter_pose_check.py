"""Does the fighter's MODEL stand where the fighter's BODY does?

Three transforms have to agree for a character to be drawn on his own feet, and they are
authored in three different frames, so they come apart quietly:

  * PlayerController writes the simulated FEET to the fighter object plus Visual Y Offset,
    because what that object carries by default -- the greybox capsule -- is drawn from its
    middle;
  * the model hangs under that object from the visual root, by its soles;
  * and the visual root's local space is the fighter object's, which in this scene is scaled
    to 0.5, so a metre of world is two of local.

Get the last one wrong and the character floats by the difference, everywhere, always -- and
it reads as a dozen different bugs: he hangs in the air on a ladder, his hands miss the rungs
the IK is reaching for, his feet are above the floor he is standing on. It cost this project a
fortnight under three separate headings.

So the sum is checked here rather than trusted. Reads SetupScene.unity the way Unity does.

Run: python3 fighter_pose_check.py"""
import os
import re
import sys

SCENE = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                     "..", "..", "Gulag runners", "Assets", "Scenes", "SetupScene.unity")

# A centimetre. Below this nobody can see it and nobody should be asked to chase it.
TOLERANCE = 0.01


def documents(text):
    """Every YAML document in a scene, by its anchor: {fileID: (classId, body)}."""
    heads = [(m.start(), m.end(), int(m.group(1)), m.group(2))
             for m in re.finditer(r"^--- !u!(\d+) &(\d+).*$", text, re.M)]
    out = {}
    for i, (start, after, cls, fid) in enumerate(heads):
        stop = heads[i + 1][0] if i + 1 < len(heads) else len(text)
        out[fid] = (cls, text[after:stop])
    return out


def field(body, key, default=None):
    m = re.search(rf"^\s*{key}:\s*(.*)$", body, re.M)
    return m.group(1).strip() if m else default


def number(body, key, default=0.0):
    raw = field(body, key)
    try:
        return float(raw)
    except (TypeError, ValueError):
        return default


def vector(body, key):
    raw = field(body, key) or ""
    got = dict(re.findall(r"([xyz]):\s*(-?[\d.eE+]+)", raw))
    return tuple(float(got.get(a, 0.0)) for a in "xyz")


def modifications(docs, instance):
    """A prefab instance's overridden properties, by path."""
    cls, body = docs.get(instance, (None, ""))
    out = {}
    for path, value in re.findall(
            r"propertyPath: (\S+)\s*\n\s*value: (\S*)", body):
        out[path] = value
    return out


def main():
    text = open(SCENE, encoding="utf-8", errors="replace").read()
    docs = documents(text)

    # The PlayerController script's own guid, so the fighters are found by what they ARE and
    # not by being called "Player 1".
    meta = os.path.join(os.path.dirname(SCENE), "..", "_Game", "Presentation",
                        "PlayerController.cs.meta")
    guid = re.search(r"guid: ([0-9a-f]+)",
                     open(meta, encoding="utf-8", errors="replace").read()).group(1)

    objects = {fid: body for fid, (cls, body) in docs.items() if cls == 1}
    components = {fid: re.findall(r"- component: \{fileID: (\d+)\}", body)
                  for fid, body in objects.items()}

    def owner(component):
        """The GameObject a component sits on."""
        cls, body = docs.get(component, (None, ""))
        got = field(body, "m_GameObject") or ""
        m = re.search(r"(\d+)", got)
        return m.group(1) if m else None

    problems = 0
    found = 0

    for fid, (cls, body) in sorted(docs.items()):
        if cls != 114 or f"guid: {guid}" not in body:
            continue

        go = owner(fid)
        name = field(objects.get(go, ""), "m_Name", "?")
        found += 1

        offset = number(body, "visualYOffset")
        sole = number(body, "modelSoleOffset")
        root = re.search(r"visualRoot: \{fileID: (\d+)\}", body)

        # The fighter object's own scale, which the local offset is measured in.
        scale_y = 1.0
        for c in components.get(go, []):
            if docs.get(c, (None,))[0] == 4:
                scale_y = vector(docs[c][1], "m_LocalScale")[1] or 1.0

        if root is None or root.group(1) == "0":
            print(f"  {name}: no visual root, so the capsule is the model and Visual Y "
                  f"Offset {offset:.3f} is all there is. Nothing to check.")
            continue

        visual = root.group(1)
        vcls, vbody = docs.get(visual, (None, ""))

        # A model dragged in from a prefab has a STRIPPED transform: its position lives in
        # the instance's modification list, not on the transform itself.
        authored = None
        inst = field(vbody, "m_PrefabInstance")
        if inst and inst != "{fileID: 0}":
            mods = modifications(docs, re.search(r"(\d+)", inst).group(1))
            if "m_LocalPosition.y" in mods:
                authored = tuple(float(mods.get(f"m_LocalPosition.{a}", 0.0))
                                 for a in "xyz")
        if authored is None:
            authored = vector(vbody, "m_LocalPosition")

        want = (sole - offset) / scale_y
        drawn = (authored[1] - want) * scale_y        # how far off the feet the soles land
        sideways = max(abs(authored[0]), abs(authored[2])) * abs(scale_y)

        print(f"  {name}: Visual Y Offset {offset:+.3f} m over scale {scale_y:.3f} "
              f"wants the model at {want:+.3f} of local; it is authored at "
              f"{authored[1]:+.3f}")

        if abs(drawn) > TOLERANCE:
            print(f"      note - which draws the soles {drawn:+.3f} m off the simulated "
                  f"feet. PlayerController.PlaceVisualRoot corrects this on play and says "
                  f"so; key {want:+.3f} in to stop it being a surprise.")
        if sideways > TOLERANCE:
            problems += 1
            print(f"      PROBLEM - and {sideways:.3f} m to the side of the body that "
                  f"collides for it, which nothing corrects into something sensible.")

    if found == 0:
        print("fighter_pose_check: no PlayerController in the scene — nothing checked.")
        return 1

    print(f"fighter_pose_check: {found} fighter(s), {problems} problem(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
