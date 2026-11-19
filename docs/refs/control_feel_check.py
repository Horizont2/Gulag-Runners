#!/usr/bin/env python3
"""
control_feel_check.py — the three on-screen buttons must answer the thumb, and truthfully.

This is a touchscreen fighting game. Every attack, dodge, jump and chest in it goes through
three rings drawn at the bottom of the screen, and for most of this project's life those
rings did not react to being touched at all. `HudButton` was a ring and a glyph; `HudView`
painted them from the palette and nothing else.

Worse, the machinery for the one case that matters was already there and was fed a lie:

    PaintButton(dodge,  GlyphInk.Block,  false);
    PaintButton(action, GlyphInk.Action, false);
    PaintButton(jump,   GlyphInk.Jump,   false);

`disabled` was hard-coded false on all three lines. The painting honoured the parameter
correctly; nothing ever passed it a real value. So a player tapping DODGE with no stamina got
no dodge, no explanation, and a button that looked exactly as it always had -- and from the
thumb's side, "the game refused me" and "the game missed my input" are indistinguishable. One
is information and the other is a bug, and a control that cannot tell them apart teaches the
player to distrust it.

Four rules:

  1. `disabled`/availability is never a literal at the call site. That is the original bug and
     it comes back as a one-line "simplification" by anybody who finds the plumbing fiddly.
  2. Availability is asked with the simulation's OWN number. The dodge gate is
     `StaminaCharges >= move.DodgeStaminaCost` in CombatMotor; a copy of that cost in the HUD
     is a second source of truth, and the day somebody tunes it the button starts lying.
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

    hud = strip_comments(read(HUD))
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

    # ---- rule 2: the sim's own number, not a copy
    gate = re.search(r"StaminaCharges\s*>=\s*\w+\.DodgeStaminaCost", combat)
    if gate is None:
        problems.append(
            "CombatMotor.cs: the dodge is no longer gated on "
            "`StaminaCharges >= move.DodgeStaminaCost`. The HUD mirrors that condition to "
            "grey the button out — find the new rule and move this one with it, do not "
            "delete it")
    else:
        can = body_of(hud, "bool CanDodge(")
        if can is None:
            problems.append("HudView.cs: no CanDodge(...), so nothing works out whether the "
                            "dodge button can fire")
        else:
            if "DodgeStaminaCost" not in can:
                problems.append(
                    "HudView.cs: CanDodge does not read DodgeStaminaCost. If it compares "
                    "against a number written here instead, the button lies the day "
                    "somebody tunes the cost in MoveConfig")
            if re.search(r"StaminaCharges\s*>=?\s*\d", can):
                problems.append(
                    "HudView.cs: CanDodge compares StaminaCharges against a LITERAL. The "
                    "cost belongs to the simulation (MoveConfig.DodgeStaminaCost); a copy "
                    "here is a second source of truth for a rule")

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
