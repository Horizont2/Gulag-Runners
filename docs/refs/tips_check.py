"""Are the loading-screen tips still true?

The tips on the search screen quote the game's own numbers back at the player: twelve
frames of parry, a block that eats 70%, rooms that shut at 1:00. A player reads them
while they wait and then goes and plays on them.

Which makes them the most fragile text in the project. They are string literals in
`MatchSearch.cs` and the numbers they quote live in `CombatConfig.cs`, `MoveConfig.cs`,
`ChestConfig.cs` and `MatchClock.cs` — four files nothing links to the fifth. Anyone
retuning the parry window moves one of them and has no way of seeing that a line on a
different screen just became false. A tip that is NEARLY true is worse than no tip: it
is the interface telling a player something they will be punished for believing.

I wrote six of these from memory off docs/01 and docs/02 before measuring, and two were
wrong — the ceiling at 1:00 (it is 2:20; the ROOMS go at 1:00) and a parry buying seven
frames (the window is 12 and the stagger is 48). That is the whole argument for this
file: I had the documents open and still got a sixth of them wrong.

So each tip is pinned to the expression that produces its number, and the check fails if
the two disagree. It does not read English — it asserts that a given substring is present
in a given tip, having first computed that substring from the live config.
"""
import io, os, re, sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
GAME = os.path.join(ROOT, "Gulag runners", "Assets", "_Game")

SIM = os.path.join(GAME, "Sim", "Core")
TIPS_FILE = os.path.join(GAME, "Menu", "MatchSearch.cs")


def read(path):
    with io.open(path, encoding="utf-8", errors="replace") as fh:
        return fh.read()


def number(src, field, where):
    """The value assigned to `field` in a Default()-style initialiser."""
    m = re.search(r"\b" + re.escape(field) + r"\s*=\s*(-?\d+)\b", src)
    if m is None:
        raise LookupError(f"{field} is not assigned anywhere in {where}")
    return int(m.group(1))


def tips():
    """The array as the inspector will show it, with \\n still escaped."""
    src = read(TIPS_FILE)
    m = re.search(r"public\s+string\[\]\s+tips\s*=\s*\{(.*?)\n        \};", src, re.S)
    if m is None:
        raise LookupError("no `public string[] tips = { ... }` in MatchSearch.cs")
    return re.findall(r'"((?:[^"\\]|\\.)*)"', m.group(1))


def seconds(frames, places=2):
    """Frames to a seconds string, trailing zeros off: 48 -> '0.8', 150 -> '2.5'."""
    return f"{frames / 60.0:.{places}f}".rstrip("0").rstrip(".")


def clock(mmss):
    return f"{mmss // 60}:{mmss % 60:02d}"


def main():
    combat = read(os.path.join(SIM, "CombatConfig.cs"))
    move = read(os.path.join(SIM, "MoveConfig.cs"))
    chest = read(os.path.join(SIM, "ChestConfig.cs"))
    match = read(os.path.join(SIM, "MatchClock.cs"))

    cage = number(match, "CageFrames", "MatchClock")
    scavenge = number(match, "ScavengeFrames", "MatchClock")
    siren_len = number(match, "SirenFrames", "MatchClock")
    hunt = number(match, "HuntFrames", "MatchClock")
    closing = number(match, "ClosingFrames", "MatchClock")

    siren_at = (cage + scavenge) // 60
    rooms_at = (cage + scavenge + siren_len + hunt) // 60
    ceiling_at = rooms_at + closing // 60

    parry = number(combat, "ParryFrames", "CombatConfig")
    stagger = number(combat, "StaggerFrames", "CombatConfig")
    guard_break = number(combat, "GuardBreakFrames", "CombatConfig")
    chip = number(combat, "ChipPermille", "CombatConfig")
    drain = number(combat, "GuardDrainFrames", "CombatConfig")
    combo = number(combat, "MaxCombo", "CombatConfig")
    window = number(combat, "ComboWindowFrames", "CombatConfig")
    desp_hp = number(combat, "DesperationHealthPermille", "CombatConfig")
    desp_dmg = number(combat, "DesperationDamagePermille", "CombatConfig")

    charges = number(move, "StaminaMax", "MoveConfig")
    bare = number(chest, "BareFrames", "ChestConfig")
    wear = number(chest, "ToolWear", "ChestConfig")

    words = {1: "ONE", 2: "TWO", 3: "THREE", 4: "FOUR", 5: "FIVE",
             6: "SIX", 7: "SEVEN", 8: "EIGHT", 9: "NINE", 10: "TEN",
             11: "ELEVEN", 12: "TWELVE"}

    # (a word out of the tip, what has to be in it, why) -- the key is matched against
    # the tips loosely, so reordering the array does not break this file.
    pins = [
        ("SIREN", [clock(siren_at)],
         "MatchConfig: the siren lands after the cages and the scavenge"),

        ("CROUCHING", [],
         "NoiseLevel only, no number to drift"),

        ("TOOL HALVES", [seconds(bare), seconds(bare // 2), words[wear] + " SWINGS"],
         "ChestConfig.BareFrames, halved by a tool, at ToolWear per pry"),

        ("BELOW", [f"{desp_hp // 10}%", f"{(desp_dmg - 1000) // 10}%"],
         "DesperationHealthPermille and DesperationDamagePermille"),

        ("PARRY", [words[parry] + " FRAMES", seconds(stagger, 1).replace("0.8", "EIGHT TENTHS")],
         "ParryFrames, and Parry() setting StaggerTimer to StaggerFrames"),

        ("BROKEN GUARD", [seconds(guard_break, 1) + " S"],
         "GuardBreakFrames"),

        ("BLOCK EATS", [f"{100 - chip // 10}%"],
         "ChipPermille -- what gets through is the rest"),

        ("RAISED GUARD", [seconds(drain, 1) + " S", words[charges] + " CHARGES",
                          seconds(drain * charges, 1)],
         "GuardDrainFrames and MoveConfig.StaminaMax"),

        ("CHAIN", [words[combo] + " BLOWS"],
         "MaxCombo; ComboWindowFrames is a third of a second at " + seconds(window, 2)),

        ("ROOMS", [clock(rooms_at)],
         "the four phases before Closing add up to this"),
    ]

    pool = tips()
    problems = []

    for key, musts, why in pins:
        hits = [t for t in pool if key in t]
        if len(hits) != 1:
            problems.append(f"no single tip mentions {key!r} ({len(hits)} found) -- "
                            f"the pin cannot be checked. Source: {why}")
            continue
        tip = hits[0]
        for must in musts:
            if must not in tip:
                problems.append(f"{key}: the config now says {must!r}, which is not in "
                                f"{tip!r}. Source: {why}")

    # The combo window, written as a fraction rather than a figure.
    if abs(window / 60.0 - 1 / 3.0) > 0.02:
        problems.append(f"ComboWindowFrames is {window} ({seconds(window, 3)} s), which is "
                        "no longer 'a third of a second' as the chain tip claims")

    # And the one claim that is a comparison, not a number: the ceiling comes AFTER the
    # rooms. This is the pair I got backwards, so it is pinned as an order, not a figure.
    if ceiling_at <= rooms_at:
        problems.append(f"the ceiling now falls at {clock(ceiling_at)} and the rooms shut "
                        f"at {clock(rooms_at)}, so the tip saying the ceiling 'comes "
                        "later' is backwards")

    for p in problems:
        print(f"  {p}")
    print(f"tips_check: {len(problems)} problem(s) across {len(pool)} tip(s), "
          f"{sum(len(m) for _, m, _ in pins)} pinned number(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
