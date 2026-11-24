"""Does anything DRAW the four beats the match is built out of?

docs/01 shapes a round as four events, and the simulation runs all four: the cages open,
the siren gives a direction at 0:25, the rooms shut from 1:00, and the gas comes up. Every
one of them is in MatchClock -- CageFrames, SirenDirection, GatesShut, the poison -- and
every one of them is a fact both devices agree on, tick for tick.

Running them is not showing them. A presentation component nobody has added to a scene is
indistinguishable from the feature not being written, and this project has already been
caught by that twice: haptics was a thousand lines in no scene, and PanelRise was built at
play time because nobody could see it was missing.

THE CLOSING IS THE ONE THAT IS NOT MERELY COSMETIC, and it is why this file exists rather
than being a note in a document. MatchState copies GatesShut onto the SimWorld and calls
GateMotor.Shove on the tick a gate closes -- so the arena really does narrow and the
fighters really are pushed. With no ClosingWall in the scene the player is shoved by
nothing and then walks into thin air they could cross a second ago. That is worse than an
unfinished effect; it is an invisible wall.

    cd docs/refs && python3 beats_check.py

Off the scenes, because a scene is the version that ships. Nothing here needs Unity.
"""
import io
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.join(HERE, "..", "..", "Gulag runners", "Assets")
SCENES = os.path.join(ASSETS, "Scenes")
GAME = os.path.join(ASSETS, "_Game")

# The arenas a player can actually be sent to, off the pool rather than off the folder:
# Arena_Sawmill is in the project and not in the build, and holding it to this would be
# reporting a fault about a scene nobody can reach.
POOL = os.path.join(GAME, "Maps", "MapPool.asset")

# (the beat, the component that draws it, what the simulation does about it anyway)
BEATS = (
    ("the cages opening", "Presentation/CageDoor.cs",
     "MatchPhase.Cages runs for CageFrames whether or not there is a door to lift, so "
     "the round opens on two fighters standing still in the open"),
    ("the siren at 0:25", "Presentation/SirenView.cs",
     "MatchSimState.SirenDirection is taken on the siren's first tick and is the only "
     "thing in the game that says which way he is -- read by nothing, it is a sbyte "
     "nobody sees"),
    ("the rooms shutting", "Presentation/ClosingWall.cs",
     "MatchState copies GatesShut onto the SimWorld and calls GateMotor.Shove when one "
     "closes, so the fighters ARE pushed and the way back IS blocked -- by nothing the "
     "player can see"),
    ("the gas rising", "Presentation/PoisonPool.cs",
     "PoisonMotor bites whatever is standing in it"),
)


def read(path):
    try:
        with io.open(path, encoding="utf-8", errors="replace") as fh:
            return fh.read()
    except OSError:
        return None


def guid_of(rel):
    meta = read(os.path.join(GAME, rel + ".meta"))
    if meta is None:
        return None
    m = re.search(r"guid: ([0-9a-f]{32})", meta)
    return m.group(1) if m else None


def shipped():
    """The arenas the map pool can deal, plus the menu's own fallback."""
    out = []
    src = read(POOL)
    if src:
        for m in re.finditer(r"- scene: (.+?)\n(?:.*?\n)*?\s*enabled: (\d)", src):
            if m.group(2) == "1":
                at = os.path.join(SCENES, m.group(1).strip() + ".unity")
                if os.path.exists(at):
                    out.append(at)
    if not out:
        out = [os.path.join(SCENES, f) for f in sorted(os.listdir(SCENES))
               if f.startswith("Arena_") and f.endswith(".unity")]
    return out


def main():
    arenas = shipped()
    if not arenas:
        print("beats_check: no arenas found, so nothing was checked -- which is itself "
              "the answer somebody wants to know about")
        return 1

    problems, notes = [], []

    for beat, script, anyway in BEATS:
        guid = guid_of(script)
        if guid is None:
            problems.append(f"{beat}: {script} is not in the project, so the beat has no "
                            "component at all")
            continue

        missing = []
        for path in arenas:
            src = read(path)
            if src is None or guid not in src:
                missing.append(os.path.basename(path)[:-6])

        if not missing:
            notes.append(f"{beat}: drawn in all {len(arenas)} arena(s)")
            continue

        where = "every arena" if len(missing) == len(arenas) else ", ".join(missing)
        problems.append(
            f"{beat} is drawn by nothing in {where}. "
            f"{os.path.basename(script)[:-3]} is the component for it and the simulation "
            f"runs the beat regardless: {anyway}")

    for n in notes:
        print(f"  note: {n}")
    for p in problems:
        print(f"  {p}")
    print(f"beats_check: {len(problems)} of {len(BEATS)} beat(s) undrawn "
          f"across {len(arenas)} shipped arena(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
