#!/usr/bin/env python3
"""
handover_check.py — the one thing that crosses the load of the arena scene.

The menu and the arena are separate Unity scenes. `MatchSearch` finds an opponent and holds
them in `MatchFound` -- their rating among other things -- and then `MenuDirector` calls
LoadSceneAsync and every object in the menu, that one included, stops existing. For most of
this project's life the opponent's rating was therefore a number typed into an inspector
field in the arena, defaulting to 1500, and every Glicko-2 update ever run was scored
against a stand-in. A ladder that moved, but not in response to who you beat.

`MatchHandover` is the one static that carries it. Being a static is the whole of its risk,
and all five rules below are about that risk rather than about the carrying:

  1. It is handed over somewhere on the path that leads to the scene load. A handover
     nobody performs is the hand-typed opponent again, silently.
  2. It is forgotten when a new search begins. The pairing from the last match, left
     sitting in a static, is the opponent the NEXT arena would be handed -- the same person
     beaten twice out of one evening, with nothing on any screen looking wrong.
  3. It clears itself on the way IN to a Play session, at SubsystemRegistration. A static
     survives leaving and re-entering Play mode when the domain is not reloaded, and this
     project is developed by opening one arena and running it over and over. Without this
     the second run is handed the first run's opponent.
  4. It clears on the way in rather than relying on a shutdown hook. A Play session stopped
     by the editor, and an app killed by the phone, never reach their own shutdown.
  5. Only Meta/ writes it. Anything else that sets the pairing is a second author of the
     one fact the ladder is scored against.

Run: python3 handover_check.py
"""

import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
GAME = os.path.join(HERE, "..", "..", "Gulag runners", "Assets", "_Game")

HANDOVER = os.path.join(GAME, "Meta", "MatchHandover.cs")


def read(path):
    with open(path, "r", encoding="utf-8") as handle:
        return handle.read()


def strip_comments(text):
    """These names are discussed at length in the summaries; only code counts."""
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    text = re.sub(r"///.*$", "", text, flags=re.M)
    return "\n".join(re.sub(r"//.*$", "", line) for line in text.split("\n"))


def sources():
    found = {}
    for folder, _, names in os.walk(GAME):
        for name in names:
            if name.endswith(".cs"):
                path = os.path.join(folder, name)
                found[os.path.relpath(path, GAME).replace(os.sep, "/")] = path
    return found


def main():
    problems = []

    if not os.path.exists(HANDOVER):
        print("handover_check: cannot find Meta/MatchHandover.cs. Nothing carries the "
              "opponent across the scene load, so every match is scored against whatever "
              "number is typed into the arena")
        print("handover_check: 1 problem(s)")
        return 1

    handover = strip_comments(read(HANDOVER))
    files = sources()

    # ---- the shape other code is written against
    for member in ("Waiting", "Current", "Hand", "Forget"):
        if not re.search(r"\b%s\b" % member, handover):
            problems.append("Meta/MatchHandover.cs: no %s. The arena is written against "
                            "Waiting, Current, Hand and Forget" % member)

    # ---- rules 3 and 4: cleared on the way IN
    if "RuntimeInitializeOnLoadMethod" not in handover:
        problems.append(
            "Meta/MatchHandover.cs: nothing clears the static at the start of a Play "
            "session. A static survives leaving and re-entering Play mode when the domain "
            "is not reloaded, and this project is developed by opening one arena and "
            "running it over and over — so the second run would be handed the first run's "
            "opponent and record a result against somebody who was never in the match")
    elif "SubsystemRegistration" not in handover:
        problems.append(
            "Meta/MatchHandover.cs: the clear does not run at SubsystemRegistration, so it "
            "is not guaranteed to have run before the first scene's Awake — and something "
            "can ask for the pairing before it has been cleared")

    if re.search(r"Application\.quitting", handover):
        problems.append(
            "Meta/MatchHandover.cs: there is a shutdown hook. It cannot be relied on — a "
            "Play session stopped by the editor and an app killed by the phone never reach "
            "their own shutdown — and clearing the same statics a moment later in the same "
            "session changes nothing. The clear on the way IN is the one that works")

    # ---- rules 1 and 2: handed over, and forgotten
    hands, forgets = [], []
    for rel, path in sorted(files.items()):
        if rel.startswith("Meta/MatchHandover"):
            continue
        body = strip_comments(read(path))
        if re.search(r"MatchHandover\.Hand\s*\(", body):
            hands.append(rel)
        if re.search(r"MatchHandover\.Forget\s*\(", body):
            forgets.append(rel)

    if not hands:
        problems.append(
            "nothing calls MatchHandover.Hand, so the opponent the matchmaker chose is "
            "still dropped at the scene load and the arena still scores against the number "
            "in its own inspector")
    else:
        # It has to be handed over by the thing that knows the opponent, and that thing is
        # the menu. A handover from the arena would be the arena telling itself.
        if not any(rel.startswith("Menu/") for rel in hands):
            problems.append(
                "MatchHandover.Hand is called from %s and not from Menu/. The menu is the "
                "only place that knows who was paired; a handover from anywhere else is "
                "something telling itself" % ", ".join(hands))

    if not forgets:
        problems.append(
            "nothing calls MatchHandover.Forget. The pairing from the last match sits in a "
            "static, so the next arena is handed an opponent who is no longer in it and the "
            "ladder settles a second result against the same person — out of one evening, "
            "with nothing on any screen looking wrong")

    # ---- rule 5: one author
    for rel, path in sorted(files.items()):
        if rel.startswith("Meta/"):
            continue
        body = strip_comments(read(path))
        if re.search(r"MatchHandover\.(Current|Waiting)\s*=", body):
            problems.append(
                "%s: assigns MatchHandover.Current or .Waiting directly. They are reads. "
                "The pairing has one author, Hand(), or the one fact the ladder is scored "
                "against has two" % rel)

    # ---- and the reader checks before it reads
    for rel, path in sorted(files.items()):
        body = strip_comments(read(path))
        if "MatchHandover.Current" not in body:
            continue
        if "MatchHandover.Waiting" not in body:
            problems.append(
                "%s: reads MatchHandover.Current without asking Waiting first. Current is "
                "meaningless when nothing was handed over — it would score the match "
                "against a blank opponent rated nought" % rel)

    if problems:
        for line in problems:
            print("handover_check: " + line)
        print("handover_check: %d problem(s)" % len(problems))
        return 1

    print("handover_check: ok (handed over in %s, forgotten in %s, cleared on the way in)"
          % (", ".join(hands), ", ".join(forgets)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
