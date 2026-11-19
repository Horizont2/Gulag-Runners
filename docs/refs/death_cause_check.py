#!/usr/bin/env python3
"""
death_cause_check.py — holds the invariants of PlayerSimState.KilledBy.

docs/09 pays 40 gold for "kill with a hazard" and docs/40 wants to know whether a round
ended by a mace or by the gas. Both read one field, set in one place, and a field that is
set WRONG is worse than one that does not exist: a daily task that pays out for an ordinary
knockout is an economy leak, and a match file that calls a drowning a knockout is a file
that quietly answers the wrong question.

The fault this catches was found by injecting it. Changing DeathCause.Poison to
DeathCause.Blow in PoisonMotor passed mirror_check, sim_motor_tests, verify_motor,
match_test and poison_check -- all five -- because none of them look at the cause. A field
nothing checks is a field that drifts.

Four rules:

  1. Every Wound() call in the simulation names a cause.
  2. The cause each one names matches what that code actually IS: the poison motor says
     Poison, the fall-damage path says Fall, the blow path says Blow.
  3. Wound's cause parameter has NO DEFAULT. A default is a label the next caller gets for
     free and silently gets wrong.
  4. A fighter made fresh has KilledBy reset. Without it a respawned fighter carries the
     label of the death they are no longer having, and the first thing to read it -- a daily
     task, most likely -- pays out for a corpse from the round before.

Run: python3 death_cause_check.py
"""

import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
SIM = os.path.join(HERE, "..", "..", "Gulag runners", "Assets", "_Game", "Sim")

# What each calling context must say. The key is the file the call is in; the test is a
# phrase that must appear in the lines just before the call, so a call that moves to another
# method is re-judged rather than grandfathered by its line number.
EXPECTED = [
    ("PoisonMotor.cs", None, "Poison",
     "the rising gas is the arena killing somebody, not the other fighter"),
    ("CombatMotor.cs", "LandedSpeed", "Fall",
     "this is the fall-damage path -- it reads LandedSpeed just above"),
    ("CombatMotor.cs", "Freeze(ref attacker", "Blow",
     "this is the blow path -- it freezes attacker and victim just above"),
]


def read(path):
    with open(path, "r", encoding="utf-8") as handle:
        return handle.read()


def strip_comments(text):
    """Comments quote these names constantly; only code counts."""
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    return "\n".join(re.sub(r"//.*$", "", line) for line in text.split("\n"))


def calls(text):
    """Every Wound(...) call, as (offset, argument text). Not the declaration."""
    found = []
    for hit in re.finditer(r"\bWound\s*\(([^;]*?)\)\s*;", text):
        found.append((hit.start(), hit.group(1)))
    return found


def main():
    problems = []

    files = {}
    for folder, _, names in os.walk(SIM):
        for name in names:
            if name.endswith(".cs"):
                files[name] = os.path.join(folder, name)

    for needed in ("CombatMotor.cs", "PoisonMotor.cs", "PlayerSimState.cs", "DeathCause.cs"):
        if needed not in files:
            problems.append("cannot find Sim/.../%s" % needed)
    if problems:
        for line in problems:
            print("death_cause_check: " + line)
        return 1

    combat = strip_comments(read(files["CombatMotor.cs"]))

    # ---- rule 3: no default on the cause
    declaration = re.search(
        r"static\s+void\s+Wound\s*\(([^)]*)\)", combat)
    if not declaration:
        problems.append("CombatMotor has no Wound(...) declaration any more — "
                        "if wounding moved, this check has to move with it")
    else:
        args = declaration.group(1)
        if "DeathCause" not in args:
            problems.append("Wound() does not take a DeathCause. Nothing can hurt a "
                            "fighter without saying what it was — that is the whole point "
                            "of the field")
        elif re.search(r"DeathCause\s+\w+\s*=", args):
            problems.append("Wound()'s cause has a DEFAULT. Remove it: a default is a "
                            "label the next caller gets for free and gets wrong, and the "
                            "field stops meaning anything the first time that happens")

    # ---- rules 1 and 2: every call, and the right cause for its context
    seen = []
    for name in ("CombatMotor.cs", "PoisonMotor.cs"):
        text = strip_comments(read(files[name]))
        for at, args in calls(text):
            cause = re.search(r"DeathCause\.(\w+)", args)
            before = text[max(0, at - 700):at]
            seen.append((name, before, cause.group(1) if cause else None, args.strip()))
            if cause is None:
                problems.append("%s: a Wound() call names no cause — %s"
                                % (name, args.strip()))

    for name, context, want, why in EXPECTED:
        matches = [s for s in seen
                   if s[0] == name and (context is None or context in s[1])]
        if not matches:
            problems.append("no Wound() call found in %s%s. If that code moved, move this "
                            "rule with it rather than deleting it"
                            % (name, "" if context is None else " near %r" % context))
            continue
        for _, _, got, args in matches:
            if got != want:
                problems.append("%s: this Wound() says DeathCause.%s and should say %s — %s"
                                % (name, got, want, why))

    # ---- rule 4: a fresh fighter is not still dead of anything
    state = strip_comments(read(files["PlayerSimState.cs"]))
    fresh = re.search(r"s\.Health\s*=\s*100\s*;(.{0,400})", state, flags=re.S)
    if fresh is None:
        problems.append("cannot find where a fresh fighter's Health is set, so cannot "
                        "check that KilledBy is reset with it")
    elif "KilledBy" not in fresh.group(1):
        problems.append("a fresh fighter's KilledBy is not reset beside Health. A "
                        "respawned fighter would carry the label of the death they are no "
                        "longer having, and the first thing to read it pays out for last "
                        "round's corpse")

    if problems:
        for line in problems:
            print("death_cause_check: " + line)
        print("death_cause_check: %d problem(s)" % len(problems))
        return 1

    print("death_cause_check: ok (%d Wound() call(s), each naming the cause its own code is)"
          % len(seen))
    return 0


if __name__ == "__main__":
    sys.exit(main())
