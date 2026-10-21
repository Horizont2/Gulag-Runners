"""Is the interface actually connected to anything?

Three screens now go out through UnityEvents -- the combat HUD, the menu and the search
screen -- and that choice is deliberate: the game code names no uGUI type, so a button is
wired in the inspector rather than found by a script at play time. The cost of it is that
a connection is a STRING in a scene file. `m_MethodName: Play` is checked by nobody. Rename
the method, delete the component, drop the object out of the hierarchy, and the scene still
loads, the button still highlights, and nothing happens when it is pressed. Unity shows
`<Missing>` in the inspector of that one object and says nothing anywhere else -- no
console warning, no failed build, no red.

Which is the worst possible failure for a phone build: it reaches a tester before it
reaches a compiler.

So this reads both scenes and follows every persistent call to the other end:

  * the target object still exists in the scene, and is not fileID 0;
  * the class named in the call has a public method of that name, with a signature that
    matches how the call is wired (dynamic calls take the event's own argument, static
    ones carry a value);
  * the call is not switched Off;
  * every Button has at least one call, and every readout UnityEvent on our own
    components has a listener -- a bar nobody drives is a bar that will read zero all
    match and look like a broken bar;
  * the EventSystem is there and is the Input System module, since the project is on
    activeInputHandler 1 and the old module silently does nothing.

Optional events are listed, not failed: some are genuinely spare until the screen behind
them exists.

Two more string-shaped faults, found by this check the first time it ran and the reason
the other two sections are here at all:

  * A FALLBACK WITH NOTHING TO FIND. Half these components say "found in the scene when
    empty" and do `if (x == null) x = FindFirstObjectByType<T>()`, and wiring_check reads
    that and correctly calls the slot optional. Optional is only true if the search finds
    something. MatchSearch.director and MenuUI.director were both empty in Menu.unity and
    there was no MenuDirector in the scene for either of them to find, so PLAY opened the
    search screen, found an opponent, and then stood there: Loading() takes the
    director == null branch, goes straight to Starting, and loads nothing. Nothing warns,
    because from each script's own point of view it did what it was told.

  * A SCENE LOADED BY NAME THAT IS NOT IN THE BUILD. matchScene is a string handed to
    SceneManager.LoadSceneAsync. A name that is not in EditorBuildSettings fails at run
    time, in the player, on the device, with "Scene couldn't be loaded" -- and in the
    editor it often works anyway, because the editor can load any scene in the project.
"""
import io, os, re, sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
ASSETS = os.path.join(ROOT, "Gulag runners", "Assets")
GAME = os.path.join(ASSETS, "_Game")
SCENES = os.path.join(ASSETS, "Scenes")

# Which scene carries which screen, for the report.
LOOKING_AT = {
    "Arena_Demo.unity": "the combat HUD",
    "Menu.unity": "the menu and the search screen",
}

# Unity's own types, which have no .cs here to read. Only the methods we actually wire.
BUILTIN = {
    "UnityEngine.GameObject": {"SetActive": ["bool"]},
    "UnityEngine.MonoBehaviour": {"set_enabled": ["bool"]},
    "UnityEngine.Behaviour": {"set_enabled": ["bool"]},
    "UnityEngine.UI.Image": {"set_fillAmount": ["float"], "set_enabled": ["bool"]},
    "UnityEngine.UI.Text": {"set_text": ["string"], "set_enabled": ["bool"]},
    "UnityEngine.UI.Slider": {"set_value": ["float"]},
    "UnityEngine.Canvas": {"set_enabled": ["bool"]},
}

# Events that are allowed to have nobody listening, and why.
SPARE = {
    "MenuUI.leagueBadgeChanged": "no rank badge on the first screen yet",
    "MatchSearch.detailChanged": "optional second line",
    "HudView.onChestOpened": "driven from the sim, not a readout",
}


def read(path):
    with io.open(path, encoding="utf-8", errors="replace") as fh:
        return fh.read()


# ------------------------------------------------------------------ the C# side

def scripts():
    """guid -> (class name, source). Only our own, under Assets/_Game."""
    by_guid = {}
    for dirpath, _, files in os.walk(GAME):
        for f in files:
            if not f.endswith(".cs"):
                continue
            cs = os.path.join(dirpath, f)
            meta = cs + ".meta"
            if not os.path.exists(meta):
                continue
            m = re.search(r"^guid: ([0-9a-f]{32})", read(meta), re.M)
            if m is None:
                continue
            src = read(cs)
            # The MonoBehaviour a .cs defines is the one named after the file; Unity
            # resolves scripts by filename and nothing else.
            name = os.path.splitext(f)[0]
            by_guid[m.group(1)] = (name, src)
    return by_guid


_PREFABS = {}


def prefab_paths(guid):
    """The .prefab a guid names, if it is one of ours."""
    if not _PREFABS:
        for dirpath, _, files in os.walk(ASSETS):
            for f in files:
                if not f.endswith(".prefab.meta"):
                    continue
                m = re.search(r"^guid: ([0-9a-f]{32})",
                              read(os.path.join(dirpath, f)), re.M)
                if m:
                    _PREFABS[m.group(1)] = os.path.join(dirpath, f[:-5])
    p = _PREFABS.get(guid)
    return [p] if p else []


def members(src, cls):
    """Public methods of `cls` as name -> [parameter types]."""
    body = class_body(src, cls)
    if body is None:
        return {}

    found = {}
    for m in re.finditer(
            r"public\s+(?:static\s+)?(?:override\s+)?(?:async\s+)?"
            r"(?P<ret>[\w<>\[\],.?]+)\s+(?P<name>\w+)\s*\((?P<args>[^)]*)\)", body):
        if m.group("name") in ("if", "while", "for", "switch", "return", "foreach"):
            continue
        args = [a.strip() for a in m.group("args").split(",") if a.strip()]
        types = [re.sub(r"\s+\w+(\s*=.*)?$", "", a).strip() for a in args]
        found[m.group("name")] = types

    # A settable public field or property is reachable as set_<name>.
    for m in re.finditer(r"public\s+(?P<type>[\w<>\[\],.?]+)\s+(?P<name>\w+)\s*(?:;|=[^=]|\{)",
                         body):
        found.setdefault("set_" + m.group("name"), [m.group("type")])
    return found


def class_body(src, cls):
    """The text between a class's own braces, so nested types do not leak in."""
    m = re.search(r"\b(?:class|struct)\s+" + re.escape(cls) + r"\b[^{]*\{", src)
    if m is None:
        return None
    depth, i = 0, m.end() - 1
    while i < len(src):
        if src[i] == "{":
            depth += 1
        elif src[i] == "}":
            depth -= 1
            if depth == 0:
                return src[m.end():i]
        i += 1
    return src[m.end():]


def events_of(src, cls):
    """Public UnityEvent fields as name -> argument type ('' for the void one)."""
    body = class_body(src, cls)
    if body is None:
        return {}
    out = {}
    for m in re.finditer(r"public\s+UnityEvent(?:<([\w<>\[\],.?]+)>)?\s+(\w+)\s*;", body):
        out[m.group(2)] = m.group(1) or ""
    return out


# ------------------------------------------------------------------ the scene side

DOC = re.compile(r"^--- !u!(\d+) &(\d+)(?: stripped)?\s*$", re.M)


def documents(src):
    """fileID -> (unity class id, body text), in file order."""
    marks = list(DOC.finditer(src))
    docs = {}
    for i, m in enumerate(marks):
        end = marks[i + 1].start() if i + 1 < len(marks) else len(src)
        docs[m.group(2)] = (m.group(1), src[m.end():end])
    return docs


def field(body, name):
    m = re.search(r"^\s*" + re.escape(name) + r":\s*(.*)$", body, re.M)
    return m.group(1).strip() if m else None


def ref(value):
    """The fileID out of a {fileID: n, guid: ...} reference, or None."""
    if not value:
        return None
    m = re.search(r"fileID:\s*(-?\d+)", value)
    return m.group(1) if m else None


CALL = re.compile(
    r"- m_Target: \{fileID: (?P<target>-?\d+)[^}]*\}\s*\n"
    r"(?:\s*m_TargetAssemblyTypeName:\s*(?P<type>[^\n]*)\n)?"
    r"\s*m_MethodName:\s*(?P<method>[^\n]*)\n"
    r"\s*m_Mode:\s*(?P<mode>\d+)\n")


def calls_in(body, field_name):
    """Every persistent call under one named UnityEvent field."""
    m = re.search(r"^(\s*)" + re.escape(field_name) + r":\s*\n(.*?)(?=\n\1\w|\Z)",
                  body, re.M | re.S)
    if m is None:
        return None
    block = m.group(2)
    if "m_Calls: []" in block:
        return []
    out = []
    for c in CALL.finditer(block):
        state = re.search(r"m_CallState:\s*(\d+)",
                          block[c.end():c.end() + 400])
        out.append({
            "target": c.group("target"),
            "type": (c.group("type") or "").split(",")[0].strip(),
            "method": c.group("method").strip(),
            "mode": int(c.group("mode")),
            "state": int(state.group(1)) if state else 2,
        })
    return out


FALLBACK = re.compile(
    r"if \((?P<field>\w+) == null\)\s*(?P=field) =\s*(?:Object\.)?"
    r"FindFirstObjectByType<(?P<type>\w+)>")


def fallbacks(src, cls):
    """field -> the type its Awake goes looking for when the slot is empty."""
    body = class_body(src, cls)
    return {} if body is None else {m.group("field"): m.group("type")
                                    for m in FALLBACK.finditer(body)}


def build_scenes():
    """Scene names in EditorBuildSettings, enabled ones first in their build order."""
    path = os.path.join(ROOT, "Gulag runners", "ProjectSettings",
                        "EditorBuildSettings.asset")
    if not os.path.exists(path):
        return None
    out = []
    for m in re.finditer(r"- enabled: (\d)\s*\n\s*path: ([^\n]+)", read(path)):
        if m.group(1) == "1":
            out.append(os.path.splitext(os.path.basename(m.group(2).strip()))[0])
    return out


def owner_names(docs):
    """component fileID -> the GameObject's name, for readable messages."""
    go_name = {}
    for fid, (cls, body) in docs.items():
        if cls == "1":
            go_name[fid] = field(body, "m_Name") or "?"
    owner = {}
    for fid, (cls, body) in docs.items():
        if cls == "1":
            continue
        go = ref(field(body, "m_GameObject"))
        if go in go_name:
            owner[fid] = go_name[go]
    return owner


# ------------------------------------------------------------------ the check

def main():
    by_guid = scripts()
    problems, notes = [], []
    checked_calls = 0
    in_build = build_scenes()
    known_scenes = {os.path.splitext(f)[0] for f in os.listdir(SCENES)
                    if f.endswith(".unity")}

    for scene, what in sorted(LOOKING_AT.items()):
        path = os.path.join(SCENES, scene)
        if not os.path.exists(path):
            problems.append(f"{scene}: not there at all")
            continue

        src = read(path)
        docs = documents(src)
        names = owner_names(docs)
        here = f"{scene} ({what})"

        # What types of our own are actually present, so a fallback can be told whether
        # it has anything to find. A prefab instance's components are stripped in the
        # scene and carry no m_Script, so the prefab it points at is read too.
        present = set()
        for _, (cls, body) in docs.items():
            if cls != "114":
                continue
            g = re.search(r"m_Script: \{fileID: \d+, guid: ([0-9a-f]{32})", body)
            if g and g.group(1) in by_guid:
                present.add(by_guid[g.group(1)][0])
        for _, (cls, body) in docs.items():
            if cls != "1001":
                continue
            g = re.search(r"m_SourcePrefab: \{fileID: \d+, guid: ([0-9a-f]{32})", body)
            if g is None:
                continue
            for pre in prefab_paths(g.group(1)):
                for gg in re.findall(
                        r"m_Script: \{fileID: \d+, guid: ([0-9a-f]{32})", read(pre)):
                    if gg in by_guid:
                        present.add(by_guid[gg][0])

        # --- every Button, and every UnityEvent on one of our own components
        buttons = 0
        for fid, (cls, body) in sorted(docs.items(), key=lambda kv: int(kv[0])):
            if cls != "114":
                continue
            guid = re.search(r"m_Script: \{fileID: \d+, guid: ([0-9a-f]{32})", body)
            if guid is None:
                continue
            g = guid.group(1)

            where = names.get(fid, "?")

            if g not in by_guid:
                # Unity's own UI components. The only one with wiring is Button.
                if "m_OnClick:" in body:
                    buttons += 1
                    got = calls_in(body, "m_OnClick")
                    if not got:
                        problems.append(f"{here}: the button on '{where}' has no "
                                        "persistent call -- pressing it does nothing")
                    else:
                        checked_calls += resolve(got, docs, by_guid, names,
                                                 here, f"button '{where}'", "",
                                                 problems)
                continue

            cls_name, cs = by_guid[g]

            # A slot left empty on purpose, whose Awake goes looking -- but only if the
            # thing it looks for is in this scene at all.
            for fld, want in sorted(fallbacks(cs, cls_name).items()):
                if ref(field(body, fld)) not in ("0", None):
                    continue
                if want in present:
                    continue
                problems.append(
                    f"{here}: {cls_name}.{fld} on '{where}' is empty and falls back to "
                    f"FindFirstObjectByType<{want}>(), but there is no {want} in this "
                    "scene for it to find")

            for ev, arg in sorted(events_of(cs, cls_name).items()):
                got = calls_in(body, ev)
                if got is None:
                    continue
                key = f"{cls_name}.{ev}"
                if not got:
                    if key in SPARE:
                        notes.append(f"{here}: {key} on '{where}' has no listener "
                                     f"({SPARE[key]})")
                    else:
                        problems.append(f"{here}: {key} on '{where}' goes nowhere -- "
                                        "nothing is drawing it")
                    continue
                checked_calls += resolve(got, docs, by_guid, names, here, key, arg,
                                         problems)

        if buttons == 0 and scene == "Menu.unity":
            problems.append(f"{here}: no uGUI Button in the scene at all")

        # --- the event system
        modules = [b for _, (c, b) in docs.items()
                   if c == "114" and "m_SendPointerHoverToParent" in b]
        has_es = any(c == "114" and "m_FirstSelected" in b for _, (c, b) in docs.items())
        if not has_es:
            problems.append(f"{here}: no EventSystem, so no button in it can be pressed")
        elif not modules:
            problems.append(f"{here}: an EventSystem with no input module")

    # --- scenes loaded by name, against what the build actually contains
    if in_build is None:
        notes.append("no EditorBuildSettings.asset, so the build list was not checked")
    else:
        for scene in sorted(LOOKING_AT):
            if os.path.splitext(scene)[0] not in in_build:
                problems.append(
                    f"{scene} carries {LOOKING_AT[scene]} and is NOT in the build. It "
                    "exists only in the editor; the player will never see it")

        for dirpath, _, files in os.walk(GAME):
            for f in files:
                if not f.endswith(".cs"):
                    continue
                src = read(os.path.join(dirpath, f))
                cls = os.path.splitext(f)[0]
                body = class_body(src, cls)
                if body is None:
                    continue
                for m in re.finditer(
                        r'public\s+string\s+(\w*[Ss]cene\w*)\s*=\s*"([^"]+)"', body):
                    fld, value = m.group(1), m.group(2)
                    if value in in_build:
                        continue
                    why = ("is not a scene in this project at all"
                           if value not in known_scenes
                           else "is a real scene but is NOT in the build")
                    problems.append(
                        f"{cls}.{fld} loads \"{value}\" by name, which {why}. "
                        "SceneManager fails on it in a player build")

    for n in notes:
        print(f"  note - {n}")
    for p in problems:
        print(f"  {p}")
    print(f"ui_check: {len(problems)} problem(s), {checked_calls} persistent call(s) "
          f"followed across {len(LOOKING_AT)} scene(s), "
          f"{len(in_build) if in_build else 0} scene(s) in the build")
    return 1 if problems else 0


def resolve(got, docs, by_guid, names, here, what, arg, problems):
    """Follow each call to the other end. Returns how many were checked."""
    for c in got:
        if c["state"] == 0:
            problems.append(f"{here}: {what} -> {c['method']} is switched Off, so it "
                            "never fires")
            continue

        if c["target"] in ("0", "-0"):
            problems.append(f"{here}: {what} -> {c['method']} has no target object")
            continue

        if c["target"] not in docs:
            problems.append(f"{here}: {what} -> {c['method']} points at fileID "
                            f"{c['target']}, which is not in this scene")
            continue

        target_type = c["type"]
        short = target_type.split(".")[-1]

        if target_type in BUILTIN:
            known = BUILTIN[target_type]
            if c["method"] not in known:
                problems.append(f"{here}: {what} -> {target_type}.{c['method']} is not a "
                                "method this check knows; add it to BUILTIN if it is real")
            continue

        hit = [(n, s) for n, s in by_guid.values() if n == short]
        if not hit:
            problems.append(f"{here}: {what} -> {target_type}.{c['method']} targets a "
                            "class with no script under Assets/_Game")
            continue

        cls_name, cs = hit[0]
        api = members(cs, cls_name)
        if c["method"] not in api:
            problems.append(f"{here}: {what} -> {cls_name}.{c['method']}() does not "
                            f"exist. The scene still loads and the call does nothing. "
                            f"Public methods: {', '.join(sorted(k for k in api if not k.startswith('set_'))[:8])}")
            continue

        # A dynamic call (mode 0) hands the event's own argument straight through, so
        # the signatures have to line up. This is the one Unity drops silently when a
        # method's parameter type is changed.
        if c["mode"] == 0 and arg:
            want = arg.split(".")[-1].lower()
            params = api[c["method"]]
            if len(params) != 1 or params[0].split(".")[-1].lower() != want:
                problems.append(f"{here}: {what} is a UnityEvent<{arg}> wired straight "
                                f"into {cls_name}.{c['method']}({', '.join(params)}) -- "
                                "the argument does not fit")
    return len(got)


if __name__ == "__main__":
    sys.exit(main())
