"""Can every label in the game actually write a word?

Reported as "no text updates anywhere, not in the loading screen and not in the match",
and it was true of seventeen labels across four scenes. Nothing crashed, nothing logged,
and the screens looked finished: a label that cannot write keeps the string it was
authored with, which is a plausible-looking number that never moves.

THE INTERFACE WAS BUILT TWICE. Some labels are UnityEngine.UI.Text, the legacy component,
and some are TextMeshProUGUI. MenuLabel.target is a TMP_Text, and a legacy Text is not one
-- not a subclass, not convertible -- so MenuLabel.Set() returned on its first line and
the label was decoration. Nordic fortress happened to be built TMP and worked; the menu,
the search screen, Forest and Egypt did not.

UiType now upgrades the legacy ones at load, so a label backed by either component works.
What is still fatal is a MenuLabel with NEITHER on its object, and that is what this
fails on. The split itself is printed as a note, because it is worth watching: every
legacy label is a component swapped at runtime, and a builder that stops making them is
one less thing happening on load.

  a label has something to write into  -- FAILS when it has not
      A MenuLabel needs a TMP_Text or a legacy Text on its own GameObject, or an assigned
      target. With none of the three it can never say anything, in any scene, ever.

  the upgrade is still wired           -- FAILS when UiType cannot run
      The legacy labels only work because something converts them. If UiType loses its
      RuntimeInitializeOnLoadMethod, forty-odd labels go quiet again and nothing else in
      this project would notice.

Run: python3 text_check.py
"""

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
ASSETS = ROOT / "Gulag runners" / "Assets"

# Script guids, read from the project rather than typed, except TMP's -- it lives in a
# package, so it has no .meta under Assets and is identified by what it serialises.
TMP_GUID = "f4688fdb7df04437aeb418b961361dc5"      # TextMeshProUGUI
OLD_GUID = "5f7201a12d95ffc409449d95f23cf332"      # UnityEngine.UI.Text


def guid_of(script):
    meta = ASSETS / script
    m = re.search(r"^guid: (\w+)", meta.read_text(), re.M)
    return m.group(1) if m else None


def documents(text):
    marks = [(m.start(), m.group(1), m.group(2))
             for m in re.finditer(r"^--- !u!(\d+) &(\d+)", text, re.M)]
    out = {}
    for i, (pos, cls, fid) in enumerate(marks):
        end = marks[i + 1][0] if i + 1 < len(marks) else len(text)
        out[fid] = (cls, text[pos:end])
    return out


def owner(body):
    m = re.search(r"m_GameObject: \{fileID: (\d+)\}", body)
    return m.group(1) if m else None


def name_of(docs, go):
    if go not in docs:
        return go
    m = re.search(r"m_Name: (.*)", docs[go][1])
    return m.group(1).strip() if m else go


def path_of(docs, go):
    """Scene path, so a finding names the thing a person can click on."""
    transforms = {}
    for fid, (cls, body) in docs.items():
        if cls in ("4", "224"):
            g = owner(body)
            if g:
                transforms[g] = (fid, body)

    parts, seen, cur = [], set(), go
    while cur and cur not in seen:
        seen.add(cur)
        parts.append(name_of(docs, cur))
        _fid, body = transforms.get(cur, (None, None))
        if body is None:
            break
        m = re.search(r"m_Father: \{fileID: (\d+)\}", body)
        if not m or m.group(1) == "0" or m.group(1) not in docs:
            break
        cur = owner(docs[m.group(1)][1])
    return " / ".join(reversed(parts))


def main():
    label_guid = guid_of("_Game/Menu/MenuLabel.cs.meta")
    if label_guid is None:
        print("text_check: MenuLabel.cs.meta has no guid, so nothing could be found.")
        return 1

    problems, notes = [], []
    legacy = modern = labels = 0

    files = sorted(list((ASSETS / "Scenes").glob("*.unity"))
                   + list((ASSETS / "Prefabs").glob("*.prefab")))

    for path in files:
        text = path.read_text(errors="ignore")
        if label_guid not in text and TMP_GUID not in text and OLD_GUID not in text:
            continue

        docs = documents(text)

        has_tmp, has_old = set(), set()
        for _fid, (cls, body) in docs.items():
            if cls != "114":
                continue
            if f"guid: {TMP_GUID}" in body:
                has_tmp.add(owner(body))
            elif f"guid: {OLD_GUID}" in body:
                has_old.add(owner(body))

        legacy += len(has_old)
        modern += len(has_tmp)

        dead = []
        for _fid, (cls, body) in docs.items():
            if cls != "114" or f"guid: {label_guid}" not in body:
                continue
            labels += 1

            go = owner(body)
            m = re.search(r"^  target: \{fileID: (\d+)", body, re.M)
            assigned = bool(m) and m.group(1) != "0"
            if assigned or go in has_tmp or go in has_old:
                continue
            dead.append(path_of(docs, go))

        if has_old or has_tmp:
            notes.append(f"  note: {path.name}: {len(has_old)} legacy, "
                         f"{len(has_tmp)} TMP")

        for where in dead:
            problems.append(
                f"{path.name}: the label at '{where}' has no text component on it and no "
                f"target assigned, so MenuLabel.Set() returns on its first line and this "
                f"line never changes. It will look finished and say the same thing all "
                f"match.")

    # And the thing that keeps the legacy half alive.
    upgrade = ASSETS / "_Game/Presentation/UiType.cs"
    if not upgrade.exists():
        problems.append(
            "UiType.cs is gone, so nothing upgrades the legacy labels and every one of "
            "them stops updating.")
    else:
        source = upgrade.read_text()
        if "RuntimeInitializeOnLoadMethod" not in source:
            problems.append(
                "UiType no longer runs itself on load, so the legacy labels are never "
                "upgraded and MenuLabel cannot write into them.")
        # The upgraded label must get an object of its own.
        #
        # A GameObject has ONE CanvasRenderer and a Graphic owns it. Putting the new
        # TextMeshProUGUI beside the legacy Text it replaces looks tidy and is not:
        # Destroy is deferred to the end of the frame, and when the old Text goes it
        # clears that shared CanvasRenderer -- wiping the mesh the new label had just
        # built. Every upgraded line comes out blank and stays blank, which is how it was
        # reported: the texts still do not update, at least on the search panel. There is
        # no exception, no warning and nothing in the scene to see.
        if "SetParent(go.transform" not in source:
            problems.append(
                "UiType no longer gives the upgraded label a child object of its own, so "
                "it shares a CanvasRenderer with the legacy Text it is replacing -- and "
                "that Text clears the renderer when it is destroyed at the end of the "
                "frame. Every upgraded line goes blank.")

        # And everything that pointed at the label it replaced has to be moved onto it.
        #
        # The views hold their labels as Graphic -- HudView.clock, name1, name2,
        # roundLabel -- and a legacy Text is one, so those references were valid before
        # the upgrade. Without rebinding they point at a destroyed component afterwards,
        # which made the upgrade WORSE than leaving well alone: the names and the round
        # counter worked until the day it shipped.
        # The CALL, not the name: the method's own declaration contains "Rebind(" and
        # would satisfy a looser test while nothing ever invoked it. The same mistake I
        # made in shop_check and had to be shown by its own injection.
        if "Rebind(old, made)" not in source:
            problems.append(
                "UiType no longer moves the references that pointed at the label it "
                "replaced, so every view holding one as a Graphic -- the HUD's clock, "
                "names and round counter -- is left pointing at a destroyed component.")

        if "sceneLoaded" not in source:
            problems.append(
                "UiType no longer listens for a scene load, so only the first scene's "
                "labels are upgraded -- the menu would work and every arena would not.")

    # ---- and the view that draws the match has to be able to write either kind
    #
    # The HUD was split down the middle in its own code: its Label helper handled only
    # the legacy Text and PaintClock only TMP. So in an arena built with legacy labels
    # the names and the round counter moved and the clock did not, and in one built with
    # TMP it was the other way round -- and the half that did nothing did it silently,
    # leaving the string the builder typed. Reported from a device as the texts being
    # dead, with the clock stopped on the mock's own 1:24.
    hud = ASSETS / "_Game/Presentation/HudView.cs"
    if hud.exists():
        source = hud.read_text()
        writer = re.search(r"static void Label\(Graphic[^{]*\{(?:[^{}]|\{[^{}]*\})*\}",
                           source)
        body = writer.group(0) if writer else ""
        if "TMP_Text" not in body or "is Text" not in body:
            problems.append(
                "HudView's one writer no longer handles both kinds of label. Half the "
                "readouts then go silent in half the arenas -- whichever half the scene "
                "was built with -- and the string the builder typed stays on screen.")
        if re.search(r"if \(clock is TMP_Text", source):
            problems.append(
                "HudView writes the clock through its own narrow test again, so an arena "
                "whose HUD is legacy text shows the mock's painted-on 1:24 for the whole "
                "match.")

    for line in notes:
        print(line)
    for line in problems:
        print(f"  {line}")
    print(f"text_check: {len(problems)} problem(s) across {labels} label(s); "
          f"{legacy} legacy text(s) upgraded at load, {modern} already TMP")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
