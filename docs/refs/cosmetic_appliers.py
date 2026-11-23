#!/usr/bin/env python3
"""Fills in the two kinds of cosmetic that need no new art.

The shop sells thirty-nine things and, until this, could show five of them: the defaults.
Everything else was bought and then not seen, which is the one thing a shop must never do.

Most of what it sells is already named as a COLOUR -- ASH, COPPER, NIGHTJAR -- and a
colourway needs no mesh, no texture and no prefab in the scene: it is the model that is
already there, painted. That is what Cosmetic.tint is, and this writes the colours in.

A POSE is the same bargain on the animator: the fighter carries a controller with states
on it already, so a pose is the NAME of one. `applies` is where a cosmetic names it, and
the vocabulary is PlayerAnimator's -- Idle, Idle Armed, Block Idle, Attack.

    python3 docs/refs/cosmetic_appliers.py          # report only
    python3 docs/refs/cosmetic_appliers.py --write  # fill them in

What it deliberately does NOT fill: weapon colourways, knockout effects, name plates and
the lock's twenty pieces. Not because they are harder -- a weapon tint is the same two
lines -- but because nothing applies them yet, and a catalogue that claims a cosmetic is
showable when no code shows it would make shop_check pass while the screen stays quiet.
They go in with their appliers.
"""

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
CATALOGUE = ROOT / "Gulag runners" / "Assets" / "_Game" / "Shop" / "Cosmetics.asset"

# The colourways, as flat colours rather than as anything clever. Named after what the
# shop calls them, and kept muted: this is prison cloth and scavenged metal, and a
# saturated runner would be the one bright thing on a screen whose whole palette is one
# near-white and one amber.
TINTS = {
    "runner.ash":      (0.62, 0.63, 0.65),   # cold grey, the colour of the word
    "runner.copper":   (0.72, 0.42, 0.24),   # warm, oxidised rather than new
    "runner.nightjar": (0.20, 0.22, 0.29),   # near-black with blue in it, like the bird
}

# And the poses, against the states the controller actually has. READ, not remembered:
# I had written SITTING off as needing an animation nobody had made, on the strength of
# the four names in MenuFighter's warning text -- and GR_Player.controller carries
# twenty-odd states including one called exactly that. The warning lists examples, not
# the vocabulary.
POSES = {
    "pose.guard": "Block Idle",
    "pose.sit": "Sitting",
}

NO_STATE = {}


def rows(text):
    """Every catalogue row as (id, start, end) over the file."""
    out = []
    for m in re.finditer(r"^  - id: (\S*)$", text, re.M):
        start = m.start()
        nxt = re.search(r"^  - id: ", text[m.end():], re.M)
        end = m.end() + nxt.start() if nxt else len(text)
        out.append((m.group(1), start, end))
    return out


def main():
    write = "--write" in sys.argv
    if not CATALOGUE.exists():
        print("cosmetic_appliers: no catalogue to fill")
        return 1

    text = CATALOGUE.read_text(encoding="utf-8")
    changed = 0

    for rid, start, end in reversed(rows(text)):
        block = text[start:end]
        want = block

        if rid in TINTS:
            r, g, b = TINTS[rid]
            line = f"    tint: {{r: {r}, g: {g}, b: {b}, a: 1}}"
            if re.search(r"^    tint: ", want, re.M):
                want = re.sub(r"^    tint: .*$", line, want, flags=re.M)
            else:
                want = want.rstrip("\n") + "\n" + line + "\n"
            print(f"{rid}: colourway {r}, {g}, {b}")

        if rid in POSES:
            want = re.sub(r"^    applies: .*$", f"    applies: {POSES[rid]}", want,
                          flags=re.M)
            print(f"{rid}: pose \"{POSES[rid]}\"")

        if rid in NO_STATE:
            print(f"{rid}: left unshowable -- {NO_STATE[rid]}")

        if want != block:
            changed += 1
            text = text[:start] + want + text[end:]

    if write and changed:
        CATALOGUE.write_text(text, encoding="utf-8")

    print(f"\n{changed} row(s) filled"
          + ("" if write else "  (nothing written -- pass --write)"))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
