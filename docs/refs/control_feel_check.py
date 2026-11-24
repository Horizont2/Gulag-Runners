#!/usr/bin/env python3
"""
control_feel_check.py — the on-screen buttons must answer the thumb, and truthfully.

This is a touchscreen fighting game. Every attack, jump and chest in it goes through a stick
and two rings drawn at the bottom of the screen, and for most of this project's life those
rings did not react to being touched at all. `HudButton` was a ring and a glyph; `HudView`
painted them from the palette and nothing else.

Worse, the machinery for the one case that matters was already there and was fed a lie:

    PaintButton(dodge,  GlyphInk.Block,  false);
    PaintButton(action, GlyphInk.Action, false);
    PaintButton(jump,   GlyphInk.Jump,   false);

`disabled` was hard-coded false on all three lines. The painting honoured the parameter
correctly; nothing ever passed it a real value. So a player tapping a button that could not
fire got no result, no explanation, and a button that looked exactly as it always had -- and
from the thumb's side, "the game refused me" and "the game missed my input" are
indistinguishable. One is information and the other is a bug, and a control that cannot tell
them apart teaches the player to distrust it.

The dodge in that snippet is gone now, with the stamina that paid for it, and it was the one
control a player could be refused: what is left can always be pressed. That does NOT make
these rules dead weight. The plumbing is what lets the next refusable control be honest from
its first frame, and rule 2 is what stops the HUD growing its own copy of a simulation number
the day one arrives.

Four rules:

  1. `disabled`/availability is never a literal at the call site. That is the original bug and
     it comes back as a one-line "simplification" by anybody who finds the plumbing fiddly.
     Each button is told whether it can fire by a `Can...` helper, by name.
  2. A `Can...` helper does not compare against a number written in the HUD. The gate belongs
     to the simulation; a copy here is a second source of truth, and the day somebody tunes
     it the button starts lying. A helper that is simply `=> true` is allowed -- the jump is
     one, on purpose, and says why -- but it has to say so in a comment rather than just sit
     there looking unfinished.
  3. A REFUSED press does not brighten. The lift is the promise that something happened; a
     refused press that looked like a working one would be the control lying about the game.
  4. The press timer runs on unscaledDeltaTime. A landed blow freezes Time.deltaTime for up
     to ten frames (hit stop), and a button that stops answering during those frames stops
     answering in the exact moment the player is pressing hardest.

Run: python3 control_feel_check.py
"""

import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
GAME = os.path.join(HERE, "..", "..", "Gulag runners", "Assets", "_Game")

HUD = os.path.join(GAME, "Presentation", "HudView.cs")
COMBAT = os.path.join(GAME, "Sim", "Core", "CombatMotor.cs")


def read(path):
    with open(path, "r", encoding="utf-8") as handle:
        return handle.read()


def strip_comments(text):
    """The bug is quoted verbatim in the comments that explain it; only code counts."""
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    text = re.sub(r"///.*$", "", text, flags=re.M)
    return "\n".join(re.sub(r"//.*$", "", line) for line in text.split("\n"))


def explained(raw, name):
    """Is there a doc comment immediately above `bool <name>(`?

    Walking up line by line rather than looking in a window of text: a window wide
    enough to hold a real summary is also wide enough to reach the PREVIOUS method's,
    which is how the first draft of this rule passed a declaration whose own reason had
    been deleted.
    """
    at = re.search(r"\bbool " + re.escape(name) + r"\s*\(", raw)
    if at is None:
        return True
    lines = raw[:at.start()].split("\n")
    for line in reversed(lines[:-1]):
        bare = line.strip()
        if not bare or bare.startswith("["):
            continue                      # a blank line or an attribute
        return bare.startswith("///") or bare.startswith("//")
    return False


def body_of(text, signature):
    at = text.find(signature)
    if at < 0:
        return None
    open_at = text.find("{", at)
    if open_at < 0:
        return None
    depth = 0
    for i in range(open_at, len(text)):
        if text[i] == "{":
            depth += 1
        elif text[i] == "}":
            depth -= 1
            if depth == 0:
                return text[open_at:i + 1]
    return None


def main():
    problems = []

    for path in (HUD, COMBAT):
        if not os.path.exists(path):
            problems.append("cannot find %s" % os.path.relpath(path, GAME))
    if problems:
        for line in problems:
            print("control_feel_check: " + line)
        return 1

    raw = read(HUD)
    hud = strip_comments(raw)
    combat = strip_comments(read(COMBAT))

    # ---- rule 1: no literal availability at the call site
    #
    # Only inside the painter that runs DURING PLAY. The HUD has a second painter for its
    # resting state, before a tick has run, and literals are right there: there is no
    # simulation yet to ask, and a button greyed out on the start screen would promise the
    # player a restriction that does not exist. Checking the whole file flagged that one
    # too, which is how this rule learnt the difference.
    playing = body_of(hud, "void PaintControls(")
    if playing is None:
        print("control_feel_check: HudView.cs has no PaintControls(), which is where the "
              "buttons are painted during play. If that moved, move this check with it")
        return 1
    calls = re.findall(r"PaintButton\s*\(([^;]*?)\)\s*;", playing, flags=re.S)
    if not calls:
        problems.append("HudView.cs: nothing calls PaintButton any more. If the buttons are "
                        "painted somewhere else now, move these rules there rather than "
                        "deleting them")
    for args in calls:
        parts = [a.strip() for a in args.split(",")]
        # the availability / disabled argument is the last one
        if not parts:
            continue
        last = parts[-1]
        if last in ("true", "false"):
            problems.append(
                "HudView.cs: PaintButton(%s) passes a LITERAL %s for whether the button "
                "works. That is the original bug: the parameter is honoured, nothing feeds "
                "it, and a player pressing a button that cannot fire sees a button that "
                "looks fine. Ask the simulation instead"
                % (", ".join(parts[:-1]) or "...", last))

    # Every button needs a held/press argument too, or nothing answers the thumb.
    for args in calls:
        if "Held" not in args and "held" not in args and "press" not in args.lower():
            problems.append(
                "HudView.cs: PaintButton(%s) is painted without being told whether the "
                "thumb is on it, so that button does not react to being touched at all"
                % args.strip().replace("\n", " ")[:60])
            break

    # ---- rule 1b: the availability argument names a helper
    #
    # Rule 1 catches `false`. This catches the subtler version -- an expression spelled out
    # at the call site -- because that is where a copy of a simulation rule ends up living,
    # three arguments deep in a painting call where nobody reviewing the gate will find it.
    for args in calls:
        parts = [a.strip() for a in args.split(",")]
        if len(parts) < 2:
            continue
        last = parts[-1]
        if last in ("true", "false"):
            continue                      # already reported by rule 1
        if not re.match(r"^Can\w*\(", last):
            problems.append(
                "HudView.cs: PaintButton is told whether the button works by `%s`, which is "
                "not a Can... helper. Put the rule in a named method: an availability test "
                "spelled out at the call site is where a copy of a simulation number hides"
                % last.replace("\n", " ")[:60])

    # ---- rule 2: the sim's own number, not a copy of it
    #
    # This used to name the one gate there was -- the dodge, against the stamina it cost.
    # Both are gone, so the rule is stated over the helpers themselves rather than over that
    # one pair: whatever a Can... helper decides, it may not decide it against a number
    # written here.
    helpers = re.findall(r"\bbool (Can\w+)\s*\(", hud)
    if not helpers:
        problems.append(
            "HudView.cs: no Can... helper at all. Every button needs something that answers "
            "'would pressing this do anything', even if today's answer is always yes")
    for name in sorted(set(helpers)):
        at = re.search(r"\bbool " + name + r"\s*\([^)]*\)\s*(=>[^;]*;|\{)", hud)
        if at is None:
            continue
        if at.group(1).startswith("=>"):
            body = at.group(1)
        else:
            body = body_of(hud, "bool " + name + "(") or ""
        # A constant answer is allowed, but it has to be argued for where it is written:
        # the jump is always pressable BECAUSE of coyote time and the input buffer, and
        # that is a decision, not a gap somebody forgot to fill.
        if re.match(r"^=>\s*(true|false)\s*;$", body.strip()):
            # `hud` has had its comments stripped -- that is what makes rule 1 honest --
            # so the reason has to be looked for in the file as written. Only the line
            # DIRECTLY above counts: a window of a few hundred characters finds the
            # previous method's summary and calls that an explanation.
            if not explained(raw, name):
                problems.append(
                    "HudView.cs: %s is a constant with nothing saying why. A button that is "
                    "always pressable may well be right -- the jump is, because of coyote "
                    "time -- but unexplained it reads as a gate somebody never finished"
                    % name)
            continue
        if re.search(r"[<>=]=?\s*\d+(\.\d+)?f?\b", body):
            problems.append(
                "HudView.cs: %s compares against a LITERAL number. The gate belongs to the "
                "simulation; a copy here is a second source of truth, and the button starts "
                "lying the day somebody tunes the real one" % name)

    # ---- rules 3 and 4, inside the painting
    paint = body_of(hud, "void PaintButton(")
    if paint is None:
        problems.append("HudView.cs: no PaintButton(...) body to check")
    else:
        if "refused" not in paint:
            problems.append(
                "HudView.cs: PaintButton does not distinguish a REFUSED press from a "
                "working one. They must not look the same: the player has no other way to "
                "tell 'the game said no' from 'the game missed it'")
        else:
            # The lift has to be gated on not-refused.
            lift = re.search(r"if\s*\(([^)]*)\)\s*\{[^}]*Lift\(", paint, flags=re.S)
            if lift is None:
                problems.append(
                    "HudView.cs: the brightening is not behind a condition, so a refused "
                    "press brightens too — and the lift is the promise that something "
                    "happened")
            elif "refused" not in lift.group(1):
                problems.append(
                    "HudView.cs: the brightening is gated on `%s`, which does not mention "
                    "refused. A button that cannot fire must not light up"
                    % lift.group(1).strip())

        if "unscaledDeltaTime" not in paint:
            problems.append(
                "HudView.cs: the press timer does not use unscaledDeltaTime. Hit stop "
                "freezes Time.deltaTime for up to ten frames after a blow lands, so the "
                "buttons would stop answering in the exact moment the player is pressing "
                "hardest — which reads as dropped input, not as a freeze")

    if problems:
        for line in problems:
            print("control_feel_check: " + line)
        print("control_feel_check: %d problem(s)" % len(problems))
        return 1

    print("control_feel_check: ok (%d button(s) painted from the thumb and the simulation)"
          % len(calls))
    return 0


if __name__ == "__main__":
    sys.exit(main())
