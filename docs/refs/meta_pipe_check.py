#!/usr/bin/env python3
"""
meta_pipe_check.py — the pipe between playing a match and the match having happened.

Everything downstream of a finished match is built: Glicko-2, the purse, the six-week
season, thirty-nine cosmetics across six slots at 800/1600/3000 gold, the novice protection
in docs/09, the league name on the menu. All of it hangs off ONE call,
`PlayerProgress.RecordMatch`, and if nothing calls that then every single one of those
things is dead code that looks alive.

This check exists because exactly that happened, and nothing noticed.

`Presentation/MatchRecorder.cs` is the only caller. It was accidentally overwritten with an
unrelated file, which removed the only call in the project -- and the eighty-three checks
in this folder all passed, because not one of them asks whether the game still tells the
save that a match was played. The repository was green while the entire meta game was
disconnected. A grep then "discovered" the disconnection as if it were a design gap rather
than a deletion from twenty minutes earlier.

A check that would have failed on that is worth more than any of the rules it holds.

Five rules:

  1. `PlayerProgress.RecordMatch` has at least one caller outside Meta/.
  2. That caller listens for the end of a MATCH, not a round -- paying a best-of-three
     three times over would make the purse a function of how long people drag matches out.
  3. It guards against recording twice. An overpayment is the one bug nobody reports.
  4. It passes the facts, so the daily tasks can be earned. docs/09 prices three of them at
     40 gold each, and for most of this project's life they could not be earned at all
     because nothing handed in what a match contained.
  5. The opponent is not only the inspector's. docs/09 ranks a player against who they
     actually fought; a hard-coded stand-in is a ladder that moves without responding to
     anything.

Run: python3 meta_pipe_check.py
"""

import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
GAME = os.path.join(HERE, "..", "..", "Gulag runners", "Assets", "_Game")


def read(path):
    with open(path, "r", encoding="utf-8") as handle:
        return handle.read()


def strip_comments(text):
    """These names are discussed at length in comments; only code counts."""
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
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
    files = sources()

    # ---- rule 1: somebody calls it
    callers = []
    for rel, path in sorted(files.items()):
        if rel.startswith("Meta/") or rel.startswith("Editor/"):
            continue
        body = strip_comments(read(path))
        if re.search(r"\.RecordMatch\s*\(", body):
            callers.append(rel)

    if not callers:
        problems.append(
            "NOTHING calls PlayerProgress.RecordMatch. The Glicko-2 rating, the purse, the "
            "streak, the match count and every cosmetic that gold buys are all unreachable: "
            "a player can win twenty matches and the menu will still read nought played, "
            "unranked, no gold. This is the whole meta game, and it is disconnected")
        for line in problems:
            print("meta_pipe_check: " + line)
        print("meta_pipe_check: 1 problem(s)")
        return 1

    # ---- rules 2 to 5, of whoever calls it
    for rel in callers:
        body = strip_comments(read(files[rel]))
        where = rel

        if "SeriesEnded" not in body:
            problems.append(
                "%s: records a match without listening for SeriesEnded. If it is on a "
                "ROUND instead, a best-of-three pays the purse three times and the reward "
                "becomes a function of how long a match is dragged out" % where)

        # Recorded once. Either a latch of its own, or something else demonstrably guarding
        # it -- but the latch has to be visible here, because this is where the double
        # payment would happen.
        latched = re.search(r"if\s*\(\s*_?\w*(done|recorded|paid)\w*\s*\)", body, re.I)
        if not latched:
            problems.append(
                "%s: nothing guards against recording the same match twice. A second "
                "SeriesEnded would pay the purse again, and an overpayment is the one bug "
                "nobody ever reports" % where)

        if "MatchFacts" not in body:
            problems.append(
                "%s: hands in no MatchFacts, so no daily task can be earned. docs/09 "
                "prices three of them at 40 gold each and they are the main thing gold "
                "comes from -- without this the shop is decorative" % where)

        if "MatchHandover" not in body:
            problems.append(
                "%s: the opponent comes only from this component's own fields, so every "
                "match is scored against a stand-in while the rating the matchmaker "
                "actually paired against is thrown away at the scene load. docs/09 ranks a "
                "player against who they fought" % where)

    if problems:
        for line in problems:
            print("meta_pipe_check: " + line)
        print("meta_pipe_check: %d problem(s)" % len(problems))
        return 1

    print("meta_pipe_check: ok (a match reaches the save through %s)"
          % ", ".join(callers))
    return 0


if __name__ == "__main__":
    sys.exit(main())
