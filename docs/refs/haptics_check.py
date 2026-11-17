#!/usr/bin/env python3
"""
One way to the motor, and every feel in the table is reachable.

Haptics.cs says of its Say() method: "THE ONLY WAY TO THE MOTOR. Every feel above arrives
here and nothing else in this file touches Device, so there is one place the switch is
tested and one place the match's own silences are kept. A second route to the hardware would
be a second route around both, which is what docs/refs/haptics_check.py is checking when it
counts the call sites."

That file did not exist. The claim was true when it was written and nothing was holding it
there, which is the state every rule in this folder exists to get out of.

Two rules, and the second one found something the first could not:

  ONE DOOR. Device.Play is called from exactly one place. A second call site would skip
      Wanted -- the player's own setting -- and skip the two silences the match keeps: the
      opening cages, where nothing happening is the player's doing, and the result screen,
      where the round is already decided and only death still earns a buzz.

  NOTHING UNFELT. Every value in the Feel enum is played by something. A feel in the table
      that nothing fires is a buzz that was designed, tuned and then never reached -- the
      kind of gap that reads as "the hand is quiet here on purpose".

    python3 haptics_check.py
"""

import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
HAPTICS = os.path.join(HERE, "..", "..", "Gulag runners", "Assets", "_Game",
                       "Presentation", "Haptics.cs")


def body(text):
    """The file without its comments, so a Feel named in prose is not read as a call."""
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    return re.sub(r"//[^\n]*", "", text)


def feels(text):
    """The Feel enum's members, in order, minus the count."""
    block = re.search(r"enum Feel\s*\{(.*?)\}", text, re.S)
    if not block:
        return []
    found = re.findall(r"(\w+)\s*=\s*\d+", block.group(1))
    return [f for f in found if f != "Count"]


def check(name, passed, detail=""):
    print("  %s  %s%s" % ("PASS" if passed else "FAIL", name, ("  " + detail) if detail else ""))
    return passed


def rules(text):
    """Both rules, over one piece of source, so the fault injection can reuse them."""
    clean = body(text)
    ok = True

    doors = len(re.findall(r"Device\.Play\s*\(", clean))
    ok &= check("Device.Play is called from one place only",
                doors == 1, "%d call site(s)" % doors)

    listed = feels(text)
    ok &= check("the Feel table could be read", len(listed) > 0,
                "%d feel(s)" % len(listed))

    said = set(re.findall(r"Feel\.(\w+)", clean))
    # A feel named only where the enum is declared is not a feel anything plays.
    declared = re.search(r"enum Feel\s*\{.*?\}", text, re.S)
    if declared:
        inside = set(re.findall(r"(\w+)\s*=\s*\d+", declared.group(0)))
        said = {f for f in said if f not in inside or
                len(re.findall(r"Feel\.%s\b" % f, clean)) > 0}

    unfelt = [f for f in listed if not re.search(r"Feel\.%s\b" % f, clean)]
    ok &= check("every feel in the table is played by something",
                not unfelt,
                "never played: " + ", ".join(unfelt) if unfelt else
                "all %d" % len(listed))
    return ok


def main():
    print("haptics_check: one way to the motor, and nothing in the table goes unfelt")

    try:
        text = open(HAPTICS, encoding="utf-8", errors="replace").read()
    except OSError as exc:
        print("  FAIL  Haptics.cs could not be read: %s" % exc)
        return 1

    ok = rules(text)

    # A check that cannot fail is not a check. Give the file a second door to the hardware
    # and an unplayed feel, and both rules have to notice.
    hurt = text.replace("void Say(Feel feel)",
                        "void Shortcut(Feel feel) { Device.Play((int)feel); }\n"
                        "        void Say(Feel feel)", 1)
    hurt = hurt.replace("Death = 10,", "Death = 10,\n            Ignored = 12,", 1)

    print("  -- with a second door and an unplayed feel bolted on:")
    broke = not rules(hurt)
    ok &= check("the rules above notice both", broke)

    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
