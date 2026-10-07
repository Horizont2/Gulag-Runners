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
    owns, owner = {}, {}
    for cls, fid, stripped, body in docs:
        if stripped:
            continue
        if cls == "1":
            owns[fid] = set(re.findall(r'- component: \{fileID: (\d+)\}', body))
        else:
            m = re.search(r'm_GameObject: \{fileID: (\d+)\}', body)
            if m:
                owner[fid] = m.group(1)
    for comp, go in owner.items():
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
            # The stripped transform of this instance is parented by the instance itself.
            for cls2, fid2, stripped2, body2 in docs:
                if (stripped2 and cls2 in ("4", "224")
                        and f"m_PrefabInstance: {{fileID: {fid}}}" in body2):
                    father[fid2] = m.group(1) if m else "0"
    for t, cs in kids.items():
        for c in cs:
            if father.get(c, t) != t:
                bad.append(f"transform {c} sits under {t} but names {father.get(c)} as parent")
    for t, f in father.items():
        if f != "0" and t not in kids.get(f, [t]):
            bad.append(f"transform {t} names {f} as parent, which does not list it")

    for b in bad:
        print(f"  {rel}: {b}")
    return len(bad)


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
    print(f"scene_check: {'FAIL' if total else 'ok'} ({total} problem(s))")
    return 1 if total else 0


if __name__ == "__main__":
    sys.exit(main())
