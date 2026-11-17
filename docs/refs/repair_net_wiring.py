"""Give NetMatch back the HUD and the result screen that a tool run emptied.

WHAT HAPPENED. ResultBuilder hangs the result screen off the canvas, and the canvas IS
the HUD root -- "Result" is a child of "HUD". HudBuilder rebuilds that root by destroying
it, so running the HUD tool took the result screen with it, and emptied NetMatch's `hud`
and `result` along the way. arena_ready_check found it in three arenas at once:

    NetMatch has hud, result left empty, so Awake takes whatever FindFirstObjectByType
    hands back -- an order Unity does not define. With two phones it decides which camera
    the player keeps and which is switched off, and this arena's two are not
    interchangeable.

Both builders are fixed in the same commit: HudBuilder carries "Result" across its own
rebuild, and each builder hands its view to NetMatch. This repairs the scenes that were
already saved, because re-running a tool is not the only way back and because a scene
being worked on in Unity should not have to be rebuilt to be correct.

A field is only filled where the scene HAS the thing: Arena_Mine and Arena_Nordic
fortress have no MatchResultView left, so `result` stays empty there and the one-click
fix is Tools > Gulag Runners > Build the result screen, which is now safe to run.

    python3 repair_net_wiring.py
"""
import io
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
SCENES = os.path.join(HERE, "..", "..", "Gulag runners", "Assets", "Scenes")

NET = "cae8e2321fdb4beb8bec4cb9b44d3a02"
HUD_VIEW = "ff6dbf8f625c60d4b867931f91808b56"
RESULT_VIEW = "6f41c5bfcc424a23bdf3f234e3b1ae05"


def documents(text):
    return [(int(m.group(1)), int(m.group(2)), m.group(4)) for m in
            re.finditer(r"(?m)^--- !u!(\d+) &(-?\d+)( stripped)?\n(.*?)(?=^--- !u!|\Z)",
                        text, re.S)]


def only(docs, guid):
    """The one component of that script in the scene, or None if there is not exactly one.

    Exactly one on purpose: this writes a reference that exists to be unambiguous, so
    guessing between two of them would be the bug it is fixing.
    """
    found = [(fid, body) for cls, fid, body in docs
             if cls == 114 and f"guid: {guid}, type: 3" in body]
    return found[0] if len(found) == 1 else None


def main():
    problems = 0
    for name in sorted(os.listdir(SCENES)):
        if not name.startswith("Arena_") or not name.endswith(".unity"):
            continue
        path = os.path.join(SCENES, name)
        text = io.open(path, encoding="utf-8").read()
        docs = documents(text)

        net = only(docs, NET)
        if net is None:
            continue
        net_fid, net_body = net

        fixed = net_body
        said = []
        for field, guid, what in (("hud", HUD_VIEW, "HudView"),
                                  ("result", RESULT_VIEW, "MatchResultView")):
            at = re.search(r"(?m)^  " + field + r": \{fileID: (-?\d+)\}$", fixed)
            if at is None:
                said.append(f"no {field} field at all")
                problems += 1
                continue
            if at.group(1) != "0":
                continue
            target = only(docs, guid)
            if target is None:
                said.append(f"{field} is empty and there is no single {what} to point it at")
                problems += 1
                continue
            fixed = (fixed[:at.start()] + f"  {field}: {{fileID: {target[0]}}}"
                     + fixed[at.end():])
            said.append(f"{field} -> &{target[0]}")

        if fixed != net_body:
            io.open(path, "w", encoding="utf-8").write(text.replace(net_body, fixed))
        print(f"  {name}: " + (", ".join(said) if said else "already wired"))

    print(f"repair_net_wiring: {problems} field(s) left for the editor")
    return 0


if __name__ == "__main__":
    sys.exit(main())
