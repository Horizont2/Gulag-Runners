"""Is everything that has to be assigned, assigned?

There is no Unity here, so an empty slot in the inspector is not something anybody can see —
it is a line of YAML that reads `{fileID: 0}`, and the failure it causes arrives at runtime as
a NullReferenceException or, worse, as a component that quietly does nothing. The chest view
with no animator, the loot view with no models, the gate with no slab: all three look fine in
a diff.

So this reads the project's own scripts to learn which fields are references, works out from
the source which of them are allowed to be empty, and then reads every scene and prefab that
uses those scripts to see what is actually in them.

A field counts as OPTIONAL when the script itself says so, in one of the ways the project
already writes it:

  * the source falls back — `if (x == null) x = ...`, `x ??= ...`, or it is set in Reset();
  * the tooltip says so — "Leave empty", "Optional";
  * it is one of the handful of fields documented as a hook for art that may not exist yet.

Everything else is REQUIRED, and a required reference left at fileID 0 is reported. Prefab
instances are read the way Unity reads them: a field that is empty in the prefab but
overridden on the instance is assigned, and the report says where from.
"""
import io, os, re, sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
ASSETS = os.path.join(ROOT, "Gulag runners", "Assets")
OURS = (os.path.join(ASSETS, "_Game"),)
SEARCH = (os.path.join(ASSETS, "Scenes"), os.path.join(ASSETS, "Prefabs"),
          os.path.join(ASSETS, "_Game"))

# Unity types that are object references rather than values. A field of one of these is a slot
# in the inspector that can be left empty.
REF_TYPES = {
    "GameObject", "Transform", "Renderer", "MeshRenderer", "SkinnedMeshRenderer", "Collider",
    "BoxCollider", "Animator", "Camera", "Light", "AudioSource", "AudioClip", "ParticleSystem",
    "Material", "Mesh", "Sprite", "Texture", "Texture2D", "MonoBehaviour", "Behaviour",
    "Component", "Rigidbody", "CharacterController", "RectTransform", "Canvas",
}

# Serializable value classes of our own: a list of these is data, not a reference, but the
# `root` inside each one IS a reference, so an empty list still means "nothing authored".
LIST_IS_CONTENT = {"ItemModel"}


def enums():
    """Every enum this project declares. An enum field is a value, not a slot.

    Worked out rather than listed: a hand-kept list of "types that are not references" goes
    stale the first time somebody adds an enum, and the symptom is a report telling you to
    assign PlayerController's Model Forward in the inspector.
    """
    found = set()
    for root, _, files in os.walk(OURS[0]):
        for f in files:
            if f.endswith(".cs"):
                src = io.open(os.path.join(root, f), encoding="utf-8").read()
                found.update(re.findall(r'\benum\s+(\w+)', src))
    return found


ENUMS = set()


def scripts():
    """Every MonoBehaviour this project owns: its guid, its reference fields, and which of
    those the source itself says may be left empty."""
    out = {}
    for root, _, files in os.walk(OURS[0]):
        for f in files:
            if not f.endswith(".cs"):
                continue
            path = os.path.join(root, f)
            src = io.open(path, encoding="utf-8").read()
            for m in re.finditer(r'class\s+(\w+)\s*:\s*MonoBehaviour', src):
                name = m.group(1)
                meta = path + ".meta"
                body = class_body(src, m.end())
                if not os.path.exists(meta):
                    continue
                g = re.search(r'^guid: ([0-9a-f]{32})',
                              io.open(meta, encoding="utf-8").read(), re.M)
                if not g:
                    continue
                out[g.group(1)] = dict(name=name, file=os.path.relpath(path, ROOT),
                                       fields=fields_of(body, src))
    return out


def class_body(src, after):
    """The braces of one class and nothing else.

    Scoped, because a file holds more than the component: FighterDamageView declares a nested
    Wound class whose own `root` and `burst` fields are not slots on the component at all, and
    reporting them sends somebody looking for an inspector field that does not exist.
    """
    start = src.find("{", after)
    if start < 0:
        return ""
    depth = 0
    for i in range(start, len(src)):
        if src[i] == "{":
            depth += 1
        elif src[i] == "}":
            depth -= 1
            if depth == 0:
                return src[start:i]
    return src[start:]


def fields_of(src, whole):
    """The public serialized fields of a script, with their type and whether they are optional.

    Read off the text rather than compiled: this is the same trade cs_check.py makes, and it is
    the right one here because what matters is the NAME Unity serializes, which is exactly what
    the declaration says.
    """
    found = {}
    for m in re.finditer(
            r'(?:\[[^\]]*\]\s*)*public\s+(?!static|const|event|override|readonly)'
            r'([\w.<>\[\]]+)\s+(\w+)\s*(=(?:(?!=>)[^;])*)?;', src):
        kind, name = m.group(1), m.group(2)
        if name in found:
            continue

        # A property is not a slot. `public PlayerSimState State => _state;` ends in a
        # semicolon like a field does, and read as one it asks somebody to assign the
        # simulation state in the inspector.
        decl = m.group(0)
        if "=>" in decl or re.search(r'\{\s*(get|set)', decl):
            continue

        # The attributes and comments attached to THIS declaration and no other, which is
        # where the project writes "Leave empty to use the one in the scene". Bounded by the
        # end of whatever came before it — cutting at a blank line instead threw the tooltip
        # away, and every optional slot in the project then read as a missing assignment.
        head = src[:m.start()]
        cut = max(head.rfind(";"), head.rfind("{"), head.rfind("}"))
        head = head[cut + 1:] if cut >= 0 else head

        # The attributes are part of the match, not of what came before it, so the tooltip
        # that says "Leave empty" sits inside m.group(0) rather than in front of it. Left out,
        # every optional slot in the project read as a missing assignment.
        head += decl

        # List<T> is written out in full in places — System.Collections.Generic.List<Player
        # Controller> — so the element type is what is INSIDE the angle brackets, not whatever
        # survives stripping them. Read the other way about, MatchState.fighters came out as a
        # type nobody recognised and the one list the whole match depends on went unchecked.
        inside = re.search(r'<([\w.]+)>', kind)
        element = (inside.group(1) if inside else kind).replace("[]", "")
        element = element.rsplit(".", 1)[-1]
        is_list = kind.endswith("[]") or "List<" in kind
        is_ref = element in REF_TYPES or element in LIST_IS_CONTENT or _looks_like_ours(element)
        if not is_ref:
            continue

        optional = bool(re.search(r'Leave empty|[Oo]ptional|may not exist', head)) or \
            bool(re.search(r'if\s*\(\s*' + name + r'\s*==\s*null\s*\)', whole)) or \
            bool(re.search(name + r'\s*\?\?=', whole)) or \
            bool(re.search(r'void\s+Reset\s*\(\s*\)[^}]*\b' + name + r'\s*=', whole, re.S))

        found[name] = dict(type=kind, element=element, list=is_list, optional=optional)
    return found


# Unity's own value types. Ours are found rather than listed -- see _value_types.
BUILT_IN_VALUES = (
    "Color", "Color32", "Vector2", "Vector3", "Vector4", "Vector2Int", "Vector3Int",
    "Quaternion", "Bounds", "Rect", "RectInt", "LayerMask", "Matrix4x4",
    "String", "Single", "Int32", "Boolean", "KeyCode", "AnimationCurve", "Gradient",
    "HumanBodyBones", "AnimatorCullingMode", "AvatarIKGoal",
)

_OURS = None


def _value_types():
    """Every struct and enum declared in this project.

    Found rather than listed. The list used to be written out by hand, and the cost of that
    showed the first time a config struct was added: FighterBrain.tuning is a BotConfig,
    which is a struct, and the check asked somebody to drag one into the inspector. A list
    that has to be remembered is a list that goes stale, and a check that cries wolf is one
    people stop reading."""
    global _OURS
    if _OURS is not None:
        return _OURS

    _OURS = set()
    for base, _, files in os.walk(os.path.join(ROOT, "Gulag runners", "Assets", "_Game")):
        for f in files:
            if not f.endswith(".cs"):
                continue
            text = io.open(os.path.join(base, f), encoding="utf-8",
                           errors="replace").read()
            for m in re.finditer(r'public\s+(?:readonly\s+)?(?:struct|enum)\s+(\w+)', text):
                _OURS.add(m.group(1))

            # And a [Serializable] plain class, which Unity writes INTO the object that
            # holds it rather than as a link to another one -- MovementTuning, SoundTable
            # and the rest of the tuning blocks. A class that derives from MonoBehaviour or
            # ScriptableObject is a real reference and stays one.
            for m in re.finditer(
                    r'\[\s*(?:System\.)?Serializable\s*\][^{;]*?\bpublic\s+(?:sealed\s+)?'
                    r'class\s+(\w+)\s*(?::\s*([\w.,\s]+))?\{', text, re.S):
                bases = (m.group(2) or "")
                if "MonoBehaviour" in bases or "ScriptableObject" in bases:
                    continue
                _OURS.add(m.group(1))
    return _OURS


def _looks_like_ours(name):
    """A field typed as one of our own components is a reference too."""
    if name in ENUMS or name in BUILT_IN_VALUES or name in _value_types():
        return False
    return bool(re.match(r'^[A-Z]', name))


def docs_in(path):
    """Every YAML document in a scene or prefab, as (class, fileID, stripped, body)."""
    src = io.open(path, encoding="utf-8").read()
    out = []
    for chunk in re.split(r'^--- ', src, flags=re.M)[1:]:
        m = re.match(r'!u!(\d+) &(\d+)( stripped)?', chunk)
        if m:
            out.append((int(m.group(1)), m.group(2), bool(m.group(3)), chunk))
    return out, src


def overrides(src):
    """Every prefab-instance override, as {(source fileID, propertyPath): assigned?}."""
    out = {}
    for m in re.finditer(
            r'- target: \{fileID: (-?\d+), guid: ([0-9a-f]{32}), type: \d+\}\n'
            r'\s*propertyPath: ([\w.\[\]]+)\n'
            r'\s*value: ?(.*)\n'
            r'\s*objectReference: \{fileID: (-?\d+)(?:, guid: [0-9a-f]{32}, type: \d+)?\}', src):
        out[(m.group(2), m.group(1), m.group(3))] = m.group(5) != "0" or m.group(4).strip() != ""
    return out


def name_of(body, fid, src):
    """The object a component sits on, for a report somebody can act on."""
    m = re.search(r'm_GameObject: \{fileID: (\d+)\}', body)
    if not m:
        return "?"
    # Within that one document only: a greedy search across the file picks up the name of some
    # object thousands of lines later, which is a report that sends you to the wrong place.
    doc = re.search(r'--- !u!1 &' + m.group(1) + r'\nGameObject:((?:.|\n)*?)(?=\n--- |\Z)', src)
    if not doc:
        return f"go {m.group(1)}"
    nm = re.search(r'^  m_Name: (.*)$', doc.group(1), re.M)
    return nm.group(1).strip() if nm else f"go {m.group(1)}"


def main():
    global ENUMS
    ENUMS = enums()
    known = scripts()
    by_guid_meta = {}
    problems, notes = [], []
    checked = 0

    files = []
    for folder in SEARCH:
        for root, _, names in os.walk(folder):
            for n in names:
                if n.endswith((".unity", ".prefab")):
                    files.append(os.path.join(root, n))

    for path in sorted(files):
        rel = os.path.relpath(path, ROOT)
        docs, src = docs_in(path)
        over = overrides(src)
        guid_self = None
        meta = path + ".meta"
        if os.path.exists(meta):
            g = re.search(r'^guid: ([0-9a-f]{32})',
                          io.open(meta, encoding="utf-8").read(), re.M)
            guid_self = g.group(1) if g else None

        for cls, fid, stripped, body in docs:
            if cls != 114 or stripped:
                continue
            g = re.search(r'm_Script: \{fileID: 11500000, guid: ([0-9a-f]{32})', body)
            if not g or g.group(1) not in known:
                continue
            script = known[g.group(1)]
            checked += 1
            where = f"{rel}: {name_of(body, fid, src)} / {script['name']}"

            tail = body[body.index("m_EditorClassIdentifier"):] \
                if "m_EditorClassIdentifier" in body else body

            for field, info in sorted(script["fields"].items()):
                if info["list"]:
                    m = re.search(r'\n  ' + field + r': (\[\]|\n)', tail)
                    empty = bool(m)
                    if not m and not re.search(r'\n  ' + field + r':', tail):
                        empty = True       # never written down: an empty list, same as above
                else:
                    m = re.search(r'\n  ' + field + r': \{fileID: (-?\d+)', tail)
                    empty = bool(m) and m.group(1) == "0"
                    if not m and not re.search(r'\n  ' + field + r':', tail):
                        # Not written down at all. For a reference that is still null — there
                        # is no initialiser Unity could fall back on — so it counts, and this
                        # is the case that hides a field added after the asset was saved.
                        empty = True

                if not empty:
                    continue

                # An instance somewhere may fill it in. Unity reads it that way, so so do we.
                filled_by = [k for k in over
                             if k[0] == guid_self and k[1] == fid and k[2] == field and over[k]]
                if filled_by:
                    continue

                line = f"{where}.{field}  ({info['type']})"
                (notes if info["optional"] else problems).append(
                    line + ("  — optional, the script falls back" if info["optional"] else ""))

    for n in notes:
        print(f"  note - {n}")
    for p in problems:
        print(f"  {p}")
    print(f"wiring_check: {len(problems)} unassigned required reference(s) "
          f"in {checked} component(s), {len(notes)} optional slot(s) empty")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
