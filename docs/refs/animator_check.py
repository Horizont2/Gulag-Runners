"""Does the Animator Controller have the states the fighter asks for?

Animator.Play on a state that does not exist does NOTHING. Not an error, not a fallback --
the animator keeps whatever was last played, so the character holds one frame. From the
outside that is indistinguishable from a broken clip, and in this project it was reported as
one: "the ladder animation plays a single frame for the whole climb". It was hunted through
the IK and through the simulation, both of which measured clean, and the cause was that the
controller in play was the free Stickman pack's nine states while PlayerAnimator asks for
twenty-odd. Six of the seven ladder states had never existed. The seventh exists spelled
Ladder_Up while the component asks for Ladder Up, and a hash is a hash.

So the two lists are compared here, outside Unity, where nothing has to be played to find out.

Run: python3 animator_check.py"""
import os
import re
import sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
ASSETS = os.path.join(ROOT, "Gulag runners", "Assets")
SCENE = os.path.join(ASSETS, "Scenes", "SetupScene.unity")

# Fields on PlayerAnimator that name a state. Everything else on it is a number or a flag.
# Read off the component rather than listed by hand: a new state field would otherwise be
# added and never checked, which is how this class of fault returns.
STATE_FIELD = re.compile(r"public string (\w*[Ss]tate)\b")
ARRAY_FIELD = re.compile(r"public string\[\] (\w+)")


def read(path):
    with open(path, encoding="utf-8", errors="replace") as f:
        return f.read()


def documents(text):
    heads = [(m.start(), m.end(), int(m.group(1)), m.group(2))
             for m in re.finditer(r"^--- !u!(\d+) &(-?\d+).*$", text, re.M)]
    out = {}
    for i, (start, after, cls, fid) in enumerate(heads):
        stop = heads[i + 1][0] if i + 1 < len(heads) else len(text)
        out[fid] = (cls, text[after:stop])
    return out


def guid_of(asset_path):
    meta = asset_path + ".meta"
    if not os.path.exists(meta):
        return None
    m = re.search(r"guid: ([0-9a-f]+)", read(meta))
    return m.group(1) if m else None


def by_guid():
    """Every asset in the project, by guid."""
    out = {}
    for base, _, files in os.walk(ASSETS):
        for f in files:
            if not f.endswith(".meta"):
                continue
            m = re.search(r"guid: ([0-9a-f]+)", read(os.path.join(base, f)))
            if m:
                out[m.group(1)] = os.path.join(base, f[:-5])
    return out


# States the game HOLDS for as long as the simulation says, named by the field that
# carries them so a rename cannot quietly drop one out of the check. A clip in one of
# these with Loop Time unticked plays once and then freezes on its last frame, which is
# reported as "the animation plays one frame and ends" -- and was, for the climb: three
# of the pack's ladder clips shipped with loopTime 0.
#
# Everything else is a one-shot or is POSED frame by frame (the jump, the attacks, the
# ladder transitions), and looping those would be wrong.
MUST_LOOP = (
    "idleState walkState runState walkBackState runBackState dodgeState "
    "crouchIdleState crouchMoveState climbUpState climbDownState climbIdleState "
    "blockIdleState blockMoveState searchState "
    "armedIdleState armedWalkState armedRunState armedWalkBackState armedRunBackState "
    "armedCrouchIdleState armedCrouchMoveState armedDodgeState"
).split()


def controller_states(path):
    """Every state in a .controller: its name, and the clip in it."""
    text = read(path)
    heads = [(m.start(), m.end(), int(m.group(1)))
             for m in re.finditer(r"^--- !u!(\d+) &-?\d+.*$", text, re.M)]
    out = {}
    for i, (start, after, cls) in enumerate(heads):
        if cls != 1102:                        # AnimatorState
            continue
        stop = heads[i + 1][0] if i + 1 < len(heads) else len(text)
        body = text[after:stop]
        n = re.search(r"^[ \t]*m_Name:[ \t]*(.*)$", body, re.M)
        if not n:
            continue
        g = re.search(r"m_Motion: \{fileID: -?\d+(?:, guid: ([0-9a-f]+))?", body)
        out[n.group(1).strip()] = g.group(1) if g and g.group(1) else None
    return out


def loops(clip_path):
    """Is Loop Time ticked? None when it cannot be told.

    Two homes for the same flag: a .anim asset keeps it as m_LoopTime in its own clip
    settings, while a clip living inside an .fbx keeps it in the importer, which is the
    .meta beside the model. Reading only the first finds a clip that loops and misses the
    one actually in the controller."""
    if clip_path is None or not os.path.exists(clip_path):
        return None
    if clip_path.endswith(".anim"):
        m = re.search(r"m_LoopTime: (\d)", read(clip_path))
        return m.group(1) == "1" if m else None

    meta = clip_path + ".meta"
    if not os.path.exists(meta):
        return None
    body = read(meta)
    m = re.search(r"clipAnimations:(.*?)\n  [a-zA-Z]", body, re.S)
    m = re.search(r"loopTime: (\d)", m.group(1) if m else body)
    return m.group(1) == "1" if m else None


def main():
    assets = by_guid()
    animator_cs = os.path.join(ASSETS, "_Game", "Presentation", "PlayerAnimator.cs")
    source = read(animator_cs)
    fields = set(STATE_FIELD.findall(source)) | set(ARRAY_FIELD.findall(source))

    guid = guid_of(animator_cs)
    scene = read(SCENE)
    docs = documents(scene)

    # Every PlayerAnimator in the scene, and the states it asks for. A field absent from the
    # YAML was added to the component after the scene was last saved, so Unity will give it
    # the C# default -- which is what the player will actually see, so that is what is read.
    defaults = dict(re.findall(r'public string (\w*[Ss]tate)\s*=\s*"([^"]*)"', source))
    combo = re.search(r'public string\[\] attackCombo\s*=\s*\{([^}]*)\}', source)
    combo_default = re.findall(r'"([^"]*)"', combo.group(1)) if combo else []

    problems, checked = 0, 0

    for fid, (cls, body) in sorted(docs.items()):
        if cls != 114 or f"guid: {guid}" not in body:
            continue
        checked += 1

        go = re.search(r"m_GameObject: \{fileID: (\d+)\}", body)
        owner = docs.get(go.group(1), (None, ""))[1] if go else ""
        name = (re.search(r"^[ \t]*m_Name:[ \t]*(.*)$", owner, re.M) or
                re.match(r"(?!x)x", "")) and re.search(r"^[ \t]*m_Name:[ \t]*(.*)$", owner, re.M)
        name = name.group(1).strip() if name else "?"

        # Which controller this fighter actually plays. The model is a prefab instance, so
        # its Animator lives in the prefab and the scene only overrides pieces of it.
        wanted = {}
        for field in sorted(fields):
            if field == "attackCombo":
                for i, state in enumerate(combo_default):
                    if state:
                        wanted[state] = f"attackCombo[{i}]"
                continue
            m = re.search(rf"^[ \\t]*{field}:[ \\t]*(.*)$", body, re.M)
            state = m.group(1).strip() if m else defaults.get(field, "")
            if state:
                wanted[state] = field

        controllers = find_controllers(docs, assets)
        if not controllers:
            print(f"  {name}: no Animator Controller found on this fighter's model.")
            problems += 1
            continue

        have = {}
        for path in controllers:
            have.update(controller_states(path))

        missing = sorted(s for s in wanted if s not in have)
        shown = ", ".join(os.path.basename(p) for p in sorted(controllers))
        print(f"  {name}: {len(wanted)} state(s) asked for, {len(have)} in {shown}")

        for state in missing:
            problems += 1
            near = [h for h in have if h.replace("_", " ").lower() == state.lower()]
            hint = f"  (the controller spells it \"{near[0]}\")" if near else ""
            print(f"      PROBLEM - no state \"{state}\" ({wanted[state]}){hint}")

        # And the flag that decides whether a held state is an animation or a frozen frame.
        for state, field in sorted(wanted.items()):
            if field not in MUST_LOOP or state not in have:
                continue
            clip = assets.get(have[state])
            looping = loops(clip)
            if looping is None:
                print(f"      note - \"{state}\" ({field}): no clip, or its loop flag "
                      f"could not be read")
                continue
            if not looping:
                problems += 1
                print(f"      PROBLEM - \"{state}\" ({field}) holds "
                      f"{os.path.basename(clip)} with Loop Time OFF, so it plays once and "
                      f"freezes on its last frame")

    if checked == 0:
        print("animator_check: no PlayerAnimator in the scene — nothing checked.")
        return 1

    print(f"animator_check: {checked} fighter(s), {problems} problem(s)")
    return 1 if problems else 0


CONTROLLER = re.compile(r"m_Controller: \{fileID: \d+, guid: ([0-9a-f]+)")

# A prefab instance does not rewrite the prefab: it records an override, and the controller
# then appears as a propertyPath with its guid on the objectReference line below. Reading
# only the plain m_Controller line finds the PREFAB's controller and misses the one actually
# in play -- which reports a scene that is fine as fifteen missing states per fighter, and a
# check that cries wolf is worse than no check.
OVERRIDE = re.compile(
    r"propertyPath: m_Controller\s*\n\s*value:.*\n\s*objectReference: "
    r"\{fileID: \d+, guid: ([0-9a-f]+)")


def find_controllers(docs, assets):
    """The Animator Controller the fighters actually play.

    An override wins outright rather than being added to what the prefab says: unioning the
    two would let a state that exists only in the controller NOT in play answer for one that
    is missing from the one that is."""
    def resolve(guids):
        out = set()
        for g in guids:
            path = assets.get(g)
            if path and path.endswith(".controller"):
                out.add(path)
        return out

    scene = "\n".join(body for _, (cls, body) in docs.items())
    overridden = resolve(OVERRIDE.findall(scene))
    if overridden:
        return overridden

    found = resolve(CONTROLLER.findall(scene))

    # Otherwise through the prefabs the scene instantiates, where a model's Animator lives.
    for fid, (cls, body) in docs.items():
        if cls != 1001:
            continue
        m = re.search(r"m_SourcePrefab: \{fileID: \d+, guid: ([0-9a-f]+)", body)
        if not m:
            continue
        prefab = assets.get(m.group(1))
        if not prefab or not prefab.endswith(".prefab"):
            continue
        text = read(prefab)
        found |= resolve(OVERRIDE.findall(text)) or resolve(CONTROLLER.findall(text))
    return found


if __name__ == "__main__":
    sys.exit(main())
