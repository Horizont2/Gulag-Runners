"""Which fighter is MINE -- one answer, written in one place, and never guessed over.

THE REPORTS, both of them this one fault: "in the arenas the health bars are wired wrongly
and belong to the wrong character", and "when the second player loots armour, the interface
dresses what the OPPONENT put on, in the equipment strip at the bottom".

Nothing in either scene is wired wrongly. MatchState.LocalFighter is the single answer the
HUD, the result screen, the recorder and the camera all read, and NetMatch sets it at the top
of a networked match -- then deals the opening round on the NEXT LINE:

    BeMe(link.LocalPlayer);        -> SetLocalFighter(1) on the guest
    match.StartFresh(seed);        -> Deal(0) -> LocalFighter = HumanFighter()

HumanFighter() answers "the first fighter that no brain is driving". On a networked device
that is fighter ZERO on both phones: the brains are switched off three lines earlier, exactly
because neither fighter is a bot any more. So the guest's whole HUD read the host's fighter,
and every symptom of that is a wiring bug you cannot find by looking at the wiring.

So the rules this keeps:

  * LocalFighter is assigned in SetLocalFighter, and anywhere else only under a guard that
    says a wire has not spoken. A third assignment is a third opinion.
  * HudView, MatchResultView and MatchRecorder take the answer FROM the match, every frame
    or every time they paint, not from their own inspector field. The inspector field is
    the scene's answer, and the scene's answer is always "player one".
  * NetMatch tells the match before it deals, not after.

    python3 local_fighter_check.py
"""
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
GAME = os.path.join(HERE, "..", "..", "Gulag runners", "Assets", "_Game")

MATCH = os.path.join(GAME, "Presentation", "MatchState.cs")
NET = os.path.join(GAME, "Net", "NetMatch.cs")
READERS = (("Presentation", "HudView.cs"), ("Presentation", "MatchResultView.cs"),
           ("Presentation", "MatchRecorder.cs"))


def read(path):
    return open(path, encoding="utf-8", errors="replace").read()


def without_comments(text):
    text = re.sub(r"//[^\n]*", "", text)
    return re.sub(r"/\*.*?\*/", "", text, flags=re.S)


def main():
    problems, notes = [], []

    match = without_comments(read(MATCH))

    # --- one answer, one place
    writes = [(m.start(), match[max(0, m.start() - 120):m.start()])
              for m in re.finditer(r"\bLocalFighter\s*=", match)]
    guarded, bare = 0, []
    for at, before in writes:
        line = match[:at].count("\n") + 1
        if "SetLocalFighter" in before or "_wiredLocal" in before:
            guarded += 1
        else:
            bare.append(line)

    if not writes:
        problems.append(
            "MatchState has no LocalFighter assignment at all. Every HUD then reads its own "
            "inspector field, and the scene's answer is always player one.")
    for line in bare:
        problems.append(
            f"MatchState.cs({line}): LocalFighter is assigned with nothing saying whether a "
            f"wire has already answered. That is how the guest's HUD came to read the host's "
            f"fighter: NetMatch sets the side and deals the round on the next line, and an "
            f"unguarded re-take in Deal throws the side away before the first frame is "
            f"painted. Guard it with the wired flag, or set it through SetLocalFighter.")

    if "_wiredLocal = true" not in match:
        problems.append(
            "SetLocalFighter does not record that a wire has spoken, so nothing downstream "
            "can tell a wire's answer from a guess.")

    # --- the readers take it from the match
    for folder, name in READERS:
        text = without_comments(read(os.path.join(GAME, folder, name)))
        if not re.search(r"localPlayer\s*=\s*match(State)?\.LocalFighter", text):
            problems.append(
                f"{name} never takes the local side from the match. Its own field is the "
                f"scene's answer, which is always player one -- and on the guest's phone "
                f"that is the opponent.")

    # --- and NetMatch says it before it deals
    net = without_comments(read(NET))
    be_me = net.find("BeMe(link.LocalPlayer)")
    fresh = net.find("match.StartFresh(")
    if be_me < 0 or fresh < 0:
        notes.append("NetMatch no longer adopts the side and deals in the same method; "
                     "this check's reading of the order is stale.")
    elif be_me > fresh:
        problems.append(
            "NetMatch deals the opening round BEFORE it tells the match which side this "
            "device is. Even with the guard in place that is a round painted from a guess.")

    for n in notes:
        print("  note: " + n)
    for p in problems:
        print("  " + p)
    print(f"local_fighter_check: {len(problems)} problem(s), {guarded} guarded "
          f"assignment(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
