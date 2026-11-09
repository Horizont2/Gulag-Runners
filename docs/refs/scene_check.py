"""Structural check on the scenes and prefabs this project hand-writes.

There is no Unity in this environment, so every scene edit here is a text edit, and the
failures a text edit causes are not the ones a compiler catches: a fileID that points at
nothing, two objects sharing an id, a component whose GameObject does not list it back, a
transform whose parent disowns it. Unity answers all four the same way — it opens the scene
with pieces quietly missing — so they are worth asking about before pushing.

Only the project's own folders are checked. Vendor scenes are not ours to fix, and prefab
instances are read the way Unity reads them: a `stripped` transform gets its parent from the
PrefabInstance that owns it, not from an m_Father of its own.
"""
import io, os, re, sys, glob

OURS = ("Gulag runners/Assets/Scenes/", "Gulag runners/Assets/Prefabs/",
        "Gulag runners/Assets/_Game/")
ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")


def guid_index():
    found = {}
    for root, _, files in os.walk(os.path.join(ROOT, "Gulag runners/Assets")):
        for f in files:
            if not f.endswith(".meta"):
                continue
            p = os.path.join(root, f)
            m = re.search(r'^guid: ([0-9a-f]{32})', io.open(p, encoding="utf-8").read(), re.M)
            if m:
                found[m.group(1)] = p[:-5]
    return found


def check(path, guids):
    src = io.open(path, encoding="utf-8").read()
    rel = os.path.relpath(path, ROOT)
    bad = []

    docs = []
    for chunk in re.split(r'^--- ', src, flags=re.M)[1:]:
        m = re.match(r'!u!(\d+) &(\d+)( stripped)?', chunk)
        if m:
            docs.append((m.group(1), m.group(2), bool(m.group(3)), chunk))

    seen = {}
    for cls, fid, stripped, _ in docs:
        if fid in seen:
            bad.append(f"duplicate fileID {fid}")
        seen[fid] = cls

    for fid in sorted(set(re.findall(r'\{fileID: (-?\d+)(?:\}|,)', src))):
        # A reference carrying a guid lives in another file; a bare one must be local.
        if fid in ("0", "-1") or fid in seen:
            continue
        if re.search(r'\{fileID: ' + fid + r', guid:', src):
            continue
        bad.append(f"dangling local fileID {fid}")

    # A guid that resolves to nothing is usually a package or a Unity built-in, which cannot be
    # seen from Assets, so it is reported and not counted. What matters is that nothing in a
    # hand-written scene points at a script in THIS project that has since been renamed, and
    # those do resolve.
    for g in sorted(set(re.findall(r'guid: ([0-9a-f]{32})', src))):
        if g in guids or g.startswith("0000000000000000"):
            continue
        print(f"  {rel}: note - guid {g} is outside Assets (a package, or built-in)")

    # Components and their GameObjects have to agree, both ways.
    #
    # A STRIPPED GameObject counts as present. It is how a scene refers to an object that
    # lives inside a prefab, and a component added to a prefab's child in the scene -- the
    # IK and the armour on each fighter's model are exactly that -- is owned by one. Its
    # component list lives in the prefab, so there is nothing here to cross-check it
    # against, but calling it absent reports four healthy components as broken.
    owns, owner, in_prefab = {}, {}, set()
    for cls, fid, stripped, body in docs:
        if stripped:
            if cls == "1":
                in_prefab.add(fid)
            continue
        if cls == "1":
            owns[fid] = set(re.findall(r'- component: \{fileID: (\d+)\}', body))
        else:
            m = re.search(r'm_GameObject: \{fileID: (\d+)\}', body)
            if m:
                owner[fid] = m.group(1)
    for comp, go in owner.items():
        if go in in_prefab:
            continue              # its component list is the prefab's, not this file's
        if go not in owns:
            bad.append(f"component {comp} is owned by {go}, which is not a GameObject here")
        elif comp not in owns[go]:
            bad.append(f"GameObject {go} does not list its component {comp}")
    for go, comps in owns.items():
        for c in comps:
            if c in seen and c not in owner:
                bad.append(f"GameObject {go} lists {c}, which claims no GameObject")

    # Transform parents and children, with prefab instances parented the way Unity does it.
    kids, father = {}, {}
    listed_children = set(re.findall(r'^  - \{fileID: (\d+)\}$', src, re.M))
    for cls, fid, stripped, body in docs:
        if cls in ("4", "224") and not stripped:
            after = body.split("m_Children:", 1)
            listed = re.findall(r'- \{fileID: (\d+)\}', after[1].split("m_Father:")[0]) \
                if len(after) > 1 else []
            kids[fid] = listed
            m = re.search(r'm_Father: \{fileID: (\d+)\}', body)
            father[fid] = m.group(1) if m else "0"
        elif cls == "1001":
            m = re.search(r'm_TransformParent: \{fileID: (\d+)\}', body)
            # Only the instance's ROOT transform is parented by m_TransformParent. An instance
            # also carries a stripped transform for every nested object inside the prefab, and
            # those are parented in the prefab, not here — reading them all as children of
            # m_TransformParent is how six chests each reported a transform their container
            # does not list.
            for cls2, fid2, stripped2, body2 in docs:
                if (stripped2 and cls2 in ("4", "224")
                        and f"m_PrefabInstance: {{fileID: {fid}}}" in body2
                        and fid2 in listed_children):
                    father[fid2] = m.group(1) if m else "0"
    for t, cs in kids.items():
        for c in cs:
            if father.get(c, t) != t:
                bad.append(f"transform {c} sits under {t} but names {father.get(c)} as parent")
    for t, f in father.items():
        if f != "0" and t not in kids.get(f, [t]):
            bad.append(f"transform {t} names {f} as parent, which does not list it")

    # An object added UNDER a prefab instance has to be declared by that instance.
    #
    # Its m_Father points at the instance's stripped transform, which is enough for it to
    # draw in the right place -- and not enough for Unity to keep it. The owning
    # PrefabInstance lists what was added to it in m_AddedGameObjects, and an added child
    # missing from that list is dropped to the scene root the first time the instance
    # reconciles. Nothing warns; the torch fires simply end up in a heap at the origin.
    stripped_owner = {}
    for cls, fid, stripped, body in docs:
        if stripped and cls in ("4", "224"):
            m = re.search(r'm_PrefabInstance: \{fileID: (-?\d+)\}', body)
            if m:
                stripped_owner[fid] = m.group(1)
    declared = {}
    for cls, fid, stripped, body in docs:
        if cls == "1001":
            declared[fid] = set(re.findall(r'addedObject: \{fileID: (-?\d+)\}', body))
    for cls, fid, stripped, body in docs:
        if cls not in ("4", "224") or stripped:
            continue
        m = re.search(r'm_Father: \{fileID: (-?\d+)\}', body)
        if not m or m.group(1) not in stripped_owner:
            continue
        inst = stripped_owner[m.group(1)]
        if fid not in declared.get(inst, set()):
            bad.append(f"transform {fid} hangs under prefab instance {inst} but is not in "
                       f"its m_AddedGameObjects, so Unity will reparent it to the scene root")

    # A match whose ending nobody is listening to.
    #
    # MatchState raises Ended on the tick the match is decided, and for a long time it had
    # no subscribers at all: the loser's health reached zero, both fighters carried on
    # standing in whatever pose they were in, and the next round dealt. The fight stopped
    # rather than finished, and nothing anywhere said so.
    def script(name):
        for g, path in guids.items():
            if path.replace(os.sep, "/").endswith("/" + name):
                return g
        return None

    if path.endswith(".unity"):
        match_g = script("MatchState.cs")
        outcome_g = script("MatchOutcomeView.cs")
        if match_g and outcome_g and match_g in src and outcome_g not in src:
            bad.append("has a MatchState but nothing listening to its Ended event, so "
                       "winning the match plays no differently from pausing it")

        # And an outcome that plays. Two of its numbers fail SILENTLY, which is the only
        # reason they are worth a rule of their own:
        #
        #   AN ANIMATION CURVE WITH NO KEYS EVALUATES TO ZERO, for every input, for ever.
        #       So a Push Ease that was never authored is not a linear push or a missing
        #       ease -- it is no push at all, and nothing says so. Unity writes an empty
        #       curve for any AnimationCurve field that reaches a scene without one, which
        #       is every field added to a component after the scene was saved.
        #   AND A TIGHTEN OF 1 IS NO TIGHTEN. It is the identity, so the shot holds where
        #       it was. Legitimate if somebody wants the freeze and the slow motion alone,
        #       but then the push seconds and the curve beside it are furniture, and this
        #       says which of the two was meant.
        if outcome_g and outcome_g in src:
            for cls, fid, _stripped, body in docs:
                if cls != "114" or f"guid: {outcome_g}" not in body:
                    continue
                keys = re.search(r"\n  pushEase:\n(.*?)(?=\n  \w+:)", body, re.S)
                how_many = len(re.findall(r"- serializedVersion: \d+\n\s+time:", keys.group(1))) \
                    if keys else 0
                tighten = re.search(r"\n  pushTighten: ([\d.]+)", body)
                close = float(tighten.group(1)) if tighten else 1.0

                if close > 1.0 and how_many < 2:
                    bad.append(
                        f"the MatchOutcomeView at {fid} asks for a {close:g}x tighter shot "
                        f"on a Push Ease with {how_many} key(s). An empty curve evaluates "
                        f"to zero for every input, so the camera never moves and nothing "
                        f"reports it")
                if close <= 1.0 and how_many >= 2:
                    bad.append(
                        f"the MatchOutcomeView at {fid} has a Push Ease curve and a Push "
                        f"Tighten of {close:g}, which is the identity -- the curve is "
                        f"furniture and the shot holds where it was")

        bad += no_camera(docs, src)

    for b in bad:
        print(f"  {rel}: {b}")
    return len(bad)


def no_camera(docs, src):
    """Is there anything in this scene that will actually draw it.

    Asked because of the black screen with a live HUD on it -- "Display 1, No cameras
    rendering", a round timer counting down behind nothing. A Screen Space Overlay canvas
    draws with no camera at all, so the HUD being there says nothing about whether the
    arena is. The four ways a scene renders nothing are all visible in the file: no camera
    at all, every camera switched off, the object carrying one switched off, or a camera
    pointed at a render texture instead of the screen. A fifth, URP's Overlay render type,
    is a camera that only draws when it is stacked on a Base one.
    """
    problems = []
    cameras, drawing = 0, 0
    overlay = sum(1 for cls, _fid, _s, body in docs
                  if cls == "114" and "UniversalAdditionalCameraData" in body
                  and re.search(r'^\s*m_CameraType: [^0]', body, re.M))

    for cls, _fid, _stripped, body in docs:
        if cls != "20":
            continue
        cameras += 1
        if re.search(r'^\s*m_Enabled: 0', body, re.M):
            continue
        if not re.search(r'm_TargetTexture: \{fileID: 0\b', body):
            continue
        rect = re.search(r'm_NormalizedViewPortRect:\n(?:.*\n){0,2}?\s*serializedVersion: 2'
                         r'\n\s*x: ([-\d.e]+)\n\s*y: ([-\d.e]+)\n\s*width: ([-\d.e]+)'
                         r'\n\s*height: ([-\d.e]+)', body)
        if rect and (float(rect.group(3)) <= 0.001 or float(rect.group(4)) <= 0.001):
            continue
        drawing += 1

    if cameras == 0:
        problems.append("has no camera in it, so it draws nothing. A HUD canvas still "
                        "appears, which is what makes this look like a rendering bug "
                        "rather than a missing object")
    elif drawing == 0:
        problems.append(f"has {cameras} camera(s) and not one of them draws to the "
                        "screen: switched off, pointed at a render texture, or given an "
                        "empty viewport")
    elif overlay == cameras:
        problems.append(f"all {cameras} of its cameras are URP Overlay cameras, which "
                        "only draw when stacked on a Base one -- on its own this scene "
                        "shows \"No cameras rendering\"")

    return problems


def tuning_drift():
    """Serialized tuning that disagrees with the C# default, listed with both numbers.

    The failure this is for has now happened three times in one session, and it is
    invisible by every other means: a field keeps its NAME while its MEANING changes.
    armLength went from metres to a share of body height; bodyRadius and bladeRadius
    went with it. Unity keeps the old number because the name still matches, so the
    arena quietly ran a 0.41 m arm against the 0.32 it was retuned to, and a hit capsule
    two thirds too wide. Nothing is wrong with either file on its own.

    No verdict, because most of these are somebody deliberately tuning and that is what
    the inspector is FOR. It prints the disagreements and lets a person read them: a
    short list is easy to scan, and a share sitting at an old metre value stands out of
    it immediately.
    """
    cs = os.path.join(ROOT, "Gulag runners/Assets/_Game/Presentation")
    defaults = {}
    for name in ("CombatTuning.cs", "MovementTuning.cs"):
        path = os.path.join(cs, name)
        if not os.path.exists(path):
            continue
        for m in re.finditer(r"public (?:float|int) (\w+)\s*=\s*(-?[\d.]+)f?;",
                             io.open(path, encoding="utf-8").read()):
            defaults[m.group(1)] = float(m.group(2))
        # A field with no initialiser defaults to zero, and that is a real default.
        for m in re.finditer(r"public (?:float|int) (\w+)\s*;",
                             io.open(path, encoding="utf-8").read()):
            defaults.setdefault(m.group(1), 0.0)

    shown = 0
    for path in sorted(glob.glob(os.path.join(ROOT, "Gulag runners/Assets/Scenes/*.unity"))):
        drift = []
        for m in re.finditer(r"^    (\w+): (-?[\d.]+)$", io.open(
                path, encoding="utf-8", errors="replace").read(), re.M):
            field, value = m.group(1), float(m.group(2))
            if field not in defaults:
                continue
            if abs(value - defaults[field]) < 1e-6:
                continue
            drift.append((field, value, defaults[field]))
        if not drift:
            continue
        seen = sorted(set(drift))
        print(f"  note - {os.path.basename(path)}: {len(seen)} tuning value(s) differ "
              "from the C# default:")
        for field, value, was in seen:
            print(f"      {field}: {value:g}  (default {was:g})")
        shown += len(seen)
    return shown


def main():
    guids = guid_index()
    total = 0
    files = []
    for pat in ("**/*.unity", "**/*.prefab"):
        files += glob.glob(os.path.join(ROOT, "Gulag runners/Assets", pat), recursive=True)
    for p in sorted(files):
        if not any(os.path.relpath(p, ROOT).replace(os.sep, "/").startswith(o) for o in OURS):
            continue
        total += check(p, guids)
    drifted = tuning_drift()
    print(f"scene_check: {'FAIL' if total else 'ok'} ({total} problem(s), "
          f"{drifted} tuning value(s) overridden)")
    return 1 if total else 0


if __name__ == "__main__":
    sys.exit(main())
