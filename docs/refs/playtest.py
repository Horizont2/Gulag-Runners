#!/usr/bin/env python3
"""
playtest.py — reads what MatchRecorder wrote and prints docs/07's exit numbers for M1.

docs/07 states the exit criteria for M1 as four numbers:

    a round runs 1:40-2:30 · the win rate sits inside 50 +- 6% ·
    at least 60% want another · at least 50% call the blind phase tense

Three of them can be read off a file of matches. The fourth cannot be read off anything:
somebody has to ask a player whether the dark half of the match was tense, and this script
says so plainly rather than quietly reporting three numbers as if they were four.

It also refuses to pronounce on a number it has too little data for. A win rate of 58%
over fourteen rounds is not a balance problem; it is fourteen rounds. Testing a +- 6%
window takes about 267 of them, and saying that out loud is the most useful thing this
script does -- it is the difference between measuring the game and reading tea leaves.

    python3 playtest.py                      # self-test on a woven sample
    python3 playtest.py gulag-matches.csv    # the real thing

With a file it exits non-zero when a measurable criterion is missed, which is the honest
signal that M1 is not finished. With no file it exits non-zero only if its own arithmetic
is wrong.

Where the file is: Unity writes it to Application.persistentDataPath, which is
  Android  /sdcard/Android/data/<bundle id>/files/gulag-matches.csv
  iOS      <app>/Documents/gulag-matches.csv
  Editor   ~/.config/unity3d/<company>/<product>/gulag-matches.csv   (Linux)
           ~/Library/Application Support/<company>/<product>/        (macOS)
The component has a context-menu item, "Where is the file?", that prints the exact path.
"""

import csv
import io
import math
import sys

# docs/07, and nowhere else. If these move, they move in the note first.
ROUND_FLOOR = 100.0          # 1:40
ROUND_CEILING = 150.0        # 2:30
BALANCE_WINDOW = 0.06        # 50 +- 6%
WANT_ANOTHER = 0.60          # at least 60%

# How many rounds it takes before the balance window means anything, at 95% confidence:
# n = (1.96 * 0.5 / 0.06)^2. Printed rather than assumed, because the whole point of the
# file is to stop a judgement being made on too little.
BALANCE_NEEDS = int(math.ceil((1.96 * 0.5 / BALANCE_WINDOW) ** 2))


def read(text):
    """Rows as dicts. A blank field stays blank; nothing is guessed into it."""
    rows = list(csv.DictReader(io.StringIO(text)))
    for row in rows:
        for key in row:
            if row[key] is None:
                row[key] = ""
    return rows


def number(value, default=None):
    try:
        return float(value)
    except (TypeError, ValueError):
        return default


def median(values):
    if not values:
        return None
    ordered = sorted(values)
    middle = len(ordered) // 2
    if len(ordered) % 2:
        return ordered[middle]
    return (ordered[middle - 1] + ordered[middle]) / 2.0


def measure(rows):
    """Everything the file can say, as plain numbers. No verdicts here."""
    rounds = [r for r in rows if r.get("kind") == "round"]
    series = [r for r in rows if r.get("kind") == "series"]
    agains = [r for r in rows if r.get("kind") == "again"]

    lengths = [number(r.get("seconds")) for r in rounds]
    lengths = [s for s in lengths if s is not None]
    inside = [s for s in lengths if ROUND_FLOOR <= s <= ROUND_CEILING]

    decided = [r for r in rounds if r.get("winner") in ("0", "1")]
    side0 = [r for r in decided if r.get("winner") == "0"]
    local_won = [r for r in decided if r.get("winner") == r.get("local")]

    picked = sum(int(number(r.get("pickedLocks"), 0)) for r in rounds)
    forced = sum(int(number(r.get("forcedLocks"), 0)) for r in rounds)
    chests = sum(int(number(r.get("chests"), 0)) for r in rounds)
    wrong = sum(int(number(r.get("wrongTouches"), 0)) for r in rounds)

    # The file now says WHAT killed them, not just that somebody died: a blow, a
    # fall or the gas. Losing to the other man and losing to the arena are different
    # rounds, and the split is worth printing on its own.
    endings = {}
    for r in rounds:
        how = r.get("ended") or "?"
        endings[how] = endings.get(how, 0) + 1
    knockouts = [r for r in rounds if r.get("ended") in ("blow", "fall", "poison")]
    bots = [r for r in series if r.get("substituted") == "1"]

    # docs/07's second criterion reads "win rate vs the LOOT BUDGET within 50 +- 6%", which
    # is not the same question as whether the left cage wins too often. It asks whether
    # FINDING MORE decides the match. So: of the rounds where the two sides went in unequally
    # armed, how often did the better-armed side win?
    #
    # Weapon tier and armour are counted separately and never added together. A tier and a
    # permille are different units, and a composite of them would be a number nobody could
    # argue with because nobody could check it.
    better_weapon = better_weapon_won = 0
    better_armour = better_armour_won = 0
    for r in decided:
        won = r.get("winner")
        w0, w1 = number(r.get("weaponTier0")), number(r.get("weaponTier1"))
        a0, a1 = number(r.get("armour0")), number(r.get("armour1"))

        if w0 is not None and w1 is not None and w0 != w1:
            better_weapon += 1
            if won == ("0" if w0 > w1 else "1"):
                better_weapon_won += 1

        if a0 is not None and a1 is not None and a0 != a1:
            better_armour += 1
            if won == ("0" if a0 > a1 else "1"):
                better_armour_won += 1

    arenas = {}
    for r in rounds:
        arenas[r.get("arena", "?")] = arenas.get(r.get("arena", "?"), 0) + 1

    return {
        "rounds": len(rounds),
        "series": len(series),
        "agains": len(agains),
        "lengths": lengths,
        "median": median(lengths),
        "inside": len(inside),
        "decided": len(decided),
        "side0": len(side0),
        "local_won": len(local_won),
        "draws": len(rounds) - len(decided),
        "picked": picked,
        "forced": forced,
        "locked": picked + forced,
        "chests": chests,
        "wrong": wrong,
        "knockouts": len(knockouts),
        "bots": len(bots),
        "arenas": arenas,
        "endings": endings,
        "better_weapon": better_weapon,
        "better_weapon_won": better_weapon_won,
        "better_armour": better_armour,
        "better_armour_won": better_armour_won,
    }


def endings_of(m, how):
    return m["endings"].get(how, 0)


def clock(seconds):
    if seconds is None:
        return "—"
    return "%d:%02d" % (int(seconds) // 60, int(seconds) % 60)


def share(part, whole):
    return None if not whole else part / float(whole)


def percent(value):
    return "—" if value is None else "%.0f%%" % (value * 100.0)


def report(rows, out=sys.stdout):
    """
    Prints the numbers and returns the list of criteria that FAILED -- not the ones that
    could not be tested. A criterion with too little behind it is reported as untested and
    does not count as passed or failed, because calling it either would be a lie about the
    same number.
    """
    m = measure(rows)
    failed = []
    untested = []

    def say(line=""):
        print(line, file=out)

    say("playtest — docs/07 exit criteria for M1")
    say("=" * 64)
    say("%d round(s) in %d series, across %d arena(s)"
        % (m["rounds"], m["series"], len(m["arenas"])))
    for arena, count in sorted(m["arenas"].items(), key=lambda kv: -kv[1]):
        say("    %-28s %d" % (arena or "?", count))
    say()

    # ---- 1. how long a round runs
    say("1. A round runs 1:40–2:30")
    if not m["lengths"]:
        say("   no rounds recorded")
        untested.append("length")
    else:
        hit = share(m["inside"], len(m["lengths"]))
        say("   median %s   shortest %s   longest %s"
            % (clock(m["median"]), clock(min(m["lengths"])), clock(max(m["lengths"]))))
        say("   %d of %d inside the window (%s)"
            % (m["inside"], len(m["lengths"]), percent(hit)))
        if m["median"] is None or not (ROUND_FLOOR <= m["median"] <= ROUND_CEILING):
            say("   ✗ the median is outside 1:40–2:30")
            failed.append("length")
        else:
            say("   ✓ the median is inside the window")
    say()

    # ---- 2. does finding more decide the match
    #
    # Read as docs/07 words it -- "win rate vs the loot budget" -- and not as side balance,
    # which is a different question that looks like the same one. The criterion asks whether
    # the better-armed fighter wins more than half the time.
    say("2. The win rate against the loot budget sits inside 50 ± 6%")
    if m["decided"] == 0:
        say("   nothing decided yet")
        untested.append("loot")
    else:
        tested = False
        for what, pairs, wins in (
            ("weapon tier", m["better_weapon"], m["better_weapon_won"]),
            ("armour", m["better_armour"], m["better_armour_won"]),
        ):
            if pairs == 0:
                say("   no decided round had the two sides unequal in %s" % what)
                continue
            rate = share(wins, pairs)
            say("   the better %s won %s of the %d round(s) where the sides differed"
                % (what, percent(rate), pairs))
            if pairs < BALANCE_NEEDS:
                say("     ? %d round(s) cannot test a ±6%% window — that takes about %d"
                    % (pairs, BALANCE_NEEDS))
            elif abs(rate - 0.5) > BALANCE_WINDOW:
                say("     ✗ outside the window: %s is deciding matches"
                    % ("the weapon" if what == "weapon tier" else "armour"))
                failed.append("loot (" + what + ")")
                tested = True
            else:
                say("     ✓ inside the window")
                tested = True
        if not tested and "loot" not in failed:
            untested.append("loot")
    say()

    # ---- and side balance, which docs/07 does not ask for and which matters anyway
    say("   Side balance, separately — a different question from the one above:")
    if m["decided"] == 0:
        say("   nothing decided yet")
    else:
        rate = share(m["side0"], m["decided"])
        say("   side 0 wins %s of %d decided round(s); the local player wins %s"
            % (percent(rate), m["decided"], percent(share(m["local_won"], m["decided"]))))
        if m["draws"]:
            say("   %d round(s) drawn" % m["draws"])
        if m["decided"] < BALANCE_NEEDS:
            say("   ? too few to judge; a ±6%% window takes about %d" % BALANCE_NEEDS)
        elif abs(rate - 0.5) > BALANCE_WINDOW:
            say("   ! one cage is winning more than the other. Not a docs/07 criterion,")
            say("     but a spawn that wins 60%% of the time is a bug with a win rate.")
    say()

    # ---- 3. want another
    say("3. At least 60% want another")
    if m["series"] == 0:
        say("   no series finished, so nobody has been offered another")
        untested.append("again")
    else:
        rate = share(m["agains"], m["series"])
        say("   AGAIN pressed after %d of %d series (%s)"
            % (m["agains"], m["series"], percent(rate)))
        say("   This is behaviour, not an answer: it counts the press, and a player who")
        say("   wanted another but had to leave does not show up in it.")
        if rate < WANT_ANOTHER:
            say("   ✗ under 60%")
            failed.append("again")
        else:
            say("   ✓ at or over 60%")
    say()

    # ---- 4. the one that is not in here
    say("4. At least 50% call the blind phase tense")
    say("   NOT MEASURABLE FROM THIS FILE. It is a question somebody has to ask a player,")
    say("   and no amount of logging answers it. Ask it in those words, after the match,")
    say("   before saying anything about what the phase is for.")
    untested.append("tense")
    say()

    # ---- the fifth question, which is this project's own
    say("And the question docs/03 rests on: are the locks used?")
    if m["locked"] == 0:
        say("   no locked chest has been opened yet")
    else:
        rate = share(m["picked"], m["locked"])
        say("   %d of %d locked chest(s) were PICKED (%s); %d were forced"
            % (m["picked"], m["locked"], percent(rate), m["forced"]))
        say("   %d chest(s) opened in all, so %d had no lock on them"
            % (m["chests"], m["chests"] - m["locked"]))
        if m["rounds"]:
            say("   %.1f wrong touch(es) per round" % (m["wrong"] / float(m["rounds"])))
        if rate < 0.25:
            say("   → Players are breaking safes rather than picking them. docs/04 is")
            say("     paying for itself only if this climbs; it is the number to watch.")
    say()

    say("Also in the file")
    if m["rounds"]:
        say("   %s of rounds ended in a death, the rest on the clock"
            % percent(share(m["knockouts"], m["rounds"])))
        for how in ("blow", "fall", "poison", "time", "zero-no-cause"):
            if endings_of(m, how):
                say("       %-14s %d" % (how, endings_of(m, how)))
        odd = [k for k in m["endings"] if k not in
               ("blow", "fall", "poison", "time", "zero-no-cause", "knockout", "?", "")]
        for k in sorted(odd):
            say("       %-14s %d  (not an ending this reader knows)" % (k, m["endings"][k]))
        if endings_of(m, "zero-no-cause"):
            say("   ! a fighter on zero health that nothing claims to have killed. That is")
            say("     a contradiction in the simulation, not a rounding error — see")
            say("     docs/refs/death_cause_check.py")
        if endings_of(m, "knockout"):
            say("   note: %d row(s) say plain \"knockout\", so they were recorded before the"
                % endings_of(m, "knockout"))
            say("     game knew what killed anybody. Older rows, not broken ones.")
    if m["series"]:
        say("   a bot played part of %d of %d series" % (m["bots"], m["series"]))
    say()

    say("=" * 64)
    if failed:
        say("MISSED: " + ", ".join(failed))
    if untested:
        say("UNTESTED: " + ", ".join(untested)
            + "  (not the same as passed)")
    if not failed and len(untested) == 1 and untested == ["tense"]:
        say("Every criterion this file can test is met. One is left, and it is a question.")
    return failed


# ---------------------------------------------------------------------- the self-test

# A woven sample with answers worked out by hand, so the arithmetic above is checked rather
# than trusted. Five rounds, two series, one rematch.
#
# The fifth round is here for a reason worth writing down. With four, the sample was
# symmetric in exactly the way that hid a bug: flipping the better-weapon comparison from
# > to < moved which rounds counted but not HOW MANY, so the self-test passed with the
# comparison backwards. A test that cannot fail is not a test. The fifth round breaks the
# symmetry, and the flip is now caught.
SAMPLE = """kind,when,arena,round,seconds,winner,local,ended,chests,pickedLocks,forcedLocks,wrongTouches,substituted,weaponTier0,armour0,weaponTier1,armour1
round,2026-11-15T10:00:00,Arena_Mine,0,118.4,1,0,blow,3,1,2,2,0,1,0,2,120
round,2026-11-15T10:03:00,Arena_Mine,1,142.0,0,0,time,4,2,1,1,0,2,200,2,60
series,2026-11-15T10:05:30,Arena_Mine,,,0,0,,,,,,0,,,,
again,2026-11-15T10:05:40,Arena_Mine,,,,,,,,,,,,,,
round,2026-11-15T10:08:00,Arena_Forest,0,96.5,1,1,poison,2,0,2,5,0,3,0,1,0
round,2026-11-15T10:11:00,Arena_Forest,1,131.0,1,1,blow,5,3,0,0,0,0,0,0,150
round,2026-11-15T10:14:00,Arena_Forest,2,120.0,0,1,fall,1,1,0,0,0,2,0,1,0
series,2026-11-15T10:16:00,Arena_Forest,,,1,1,,,,,,1,,,,
"""

# Worked out by hand from the seven lines above.
EXPECTED = {
    "rounds": 5,
    "series": 2,
    "agains": 1,
    "median": 120.0,            # sorted: 96.5 118.4 [120.0] 131.0 142.0
    "inside": 4,                # 96.5 is the only one under 1:40
    "decided": 5,
    "side0": 2,                 # mine r2 and forest r3
    "local_won": 3,             # mine r2 (0==0), forest r1 and r2 (1==1)
    "picked": 7,
    "forced": 5,
    "locked": 12,
    "chests": 15,
    "wrong": 8,
    # One of each kind of death, plus the mine's second round which ran out of time, so
    # every branch of the ending split is exercised rather than just counted.
    "knockouts": 4,
    "endings": {"blow": 2, "poison": 1, "fall": 1, "time": 1},
    "bots": 1,

    # The loot question, worked out by hand from the five round rows. "better" is whichever
    # side went in with the higher number; the question is whether that side then won.
    #
    #   r1  weapon 1 < 2  -> side 1 better, side 1 won   counted, won
    #       armour 0 < 120 -> side 1 better, side 1 won   counted, won
    #   r2  weapon 2 = 2  -> equal                       not counted
    #       armour 200 > 60 -> side 0 better, side 0 won  counted, won
    #   r3  weapon 3 > 1  -> side 0 better, side 1 won    counted, NOT won
    #       armour 0 = 0  -> equal                       not counted
    #   r4  weapon 0 = 0  -> equal                       not counted
    #       armour 0 < 150 -> side 1 better, side 1 won   counted, won
    #   r5  weapon 2 > 1  -> side 0 better, side 0 won    counted, won
    #       armour 0 = 0  -> equal                       not counted
    "better_weapon": 3,
    "better_weapon_won": 2,
    "better_armour": 3,
    "better_armour_won": 3,
}


def self_test(out=sys.stdout):
    m = measure(read(SAMPLE))
    wrong = []
    for key, want in sorted(EXPECTED.items()):
        got = m[key]
        if isinstance(want, float):
            ok = got is not None and abs(got - want) < 1e-9
        else:
            ok = got == want
        if not ok:
            wrong.append("%s: expected %r, measured %r" % (key, want, got))

    print("playtest self-test", file=out)
    print("=" * 64, file=out)
    print("No file given, so the sample below is woven in rather than measured, and every",
          file=out)
    print("figure in it was worked out by hand. This checks the reader, not the game.",
          file=out)
    print(file=out)
    if wrong:
        for line in wrong:
            print("  ✗ " + line, file=out)
        print(file=out)
        print("The reader is wrong. Fix it before trusting a real file.", file=out)
        return wrong
    print("  ✓ all %d measured figures match the hand-worked answers" % len(EXPECTED),
          file=out)
    print(file=out)
    print("And this is what a report looks like:", file=out)
    print(file=out)
    report(read(SAMPLE), out=out)
    return []


def main(argv):
    if len(argv) > 1:
        path = argv[1]
        try:
            with open(path, "r", encoding="utf-8") as handle:
                text = handle.read()
        except OSError as problem:
            print("playtest: cannot read %s — %s" % (path, problem))
            print()
            print("Nothing has been recorded yet, or the file is on the phone. Add a")
            print("MatchRecorder to the arena (Tools > Gulag Runners > Record the matches)")
            print("and use its \"Where is the file?\" menu item to find the path.")
            return 2
        rows = read(text)
        if not rows:
            print("playtest: %s has a header and no rows." % path)
            return 2
        return 1 if report(rows) else 0

    return 1 if self_test() else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
