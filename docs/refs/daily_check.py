#!/usr/bin/env python3
"""
daily_check.py — the three daily tasks, against what docs/09 actually prices.

docs/09-economy-and-ranking.md, line 17:

    | Щоденне завдання (×3) | 40 кожне | «відкрий 5 сейфів загадкою», «виграй без зброї T3»,
    | «вбий хазардом» — завдання вчать механікам |

Three tasks, forty gold each, and the point of them is that they TEACH THE MECHANICS. They
are also where nearly all of a player's gold comes from: 120 a day against 10 for playing
and 15 for winning, with thirty-nine cosmetics priced 800/1600/3000. Get these wrong and
either the shop is unreachable or it is free.

The fault that made this file necessary was in the save format. `dailiesDone` used to be a
COUNT from nought to three. A count cannot say WHICH tasks are done, so a player who was
good at one of them completed it every match and was paid every match: 120 gold a day for
one lesson, where docs/09 says 40 each. The mask fixes it, and the rules below are what
stop it coming back -- because it will come back as a one-line "simplification" by somebody
who sees a mask where a count would do.

Rules:

  1. Three tasks, and docs/09's numbers: 5 safes, tier 3, 40 gold each.
  2. `Earned` takes the day's finished tasks back OUT of its answer. That one line is the
     fix for the 120 gold.
  3. `Earned` changes nothing. It is a question; the caller writes the answer down. If it
     starts assigning to the save, the pick total gets counted twice and three safes pay
     for five.
  4. The purse is paid by what this match JUST finished, never by the day's total -- else a
     task done once pays 40 gold every match for the rest of the day.
  5. The mask and the day's pick count are reset together, in one place. A reset that drops
     one and keeps the other either hands over a finished task or counts yesterday's locks.
  6. Every task has a title and is reachable from `Earned`. A task with no way to finish it
     is a task that reads "0 / 5" for ever.
  7. A bot match does not move the rating, but DOES count as played (docs/09: matches 1-5
     are bots "honestly labelled TRAINING", and a bot is the matchmaker's fallback at any
     rank; but matchesPlayed must still rise or match 6 never arrives).

Run: python3 daily_check.py
"""

import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
GAME = os.path.join(HERE, "..", "..", "Gulag runners", "Assets", "_Game")
DOCS = os.path.join(HERE, "..")

DAILY = os.path.join(GAME, "Meta", "Daily.cs")
PROGRESS = os.path.join(GAME, "Meta", "PlayerProgress.cs")
PURSE = os.path.join(GAME, "Meta", "Purse.cs")


def read(path):
    with open(path, "r", encoding="utf-8") as handle:
        return handle.read()


def strip_comments(text):
    """docs/09's numbers are quoted all over the comments; only code counts."""
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    text = re.sub(r"///.*$", "", text, flags=re.M)
    return "\n".join(re.sub(r"//.*$", "", line) for line in text.split("\n"))


def body_of(text, signature):
    """The braces of one method, by brace counting."""
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


def constant(text, name):
    hit = re.search(r"const\s+int\s+%s\s*=\s*(-?\d+)" % re.escape(name), text)
    return int(hit.group(1)) if hit else None


def main():
    problems = []

    for path in (DAILY, PROGRESS, PURSE):
        if not os.path.exists(path):
            problems.append("cannot find %s" % os.path.relpath(path, GAME))
    if problems:
        for line in problems:
            print("daily_check: " + line)
        return 1

    daily = strip_comments(read(DAILY))
    progress = strip_comments(read(PROGRESS))
    purse = strip_comments(read(PURSE))

    # ---- rule 1: docs/09's numbers
    for where, text, name, want, why in (
        ("Daily.cs", daily, "Count", 3, 'docs/09 says "×3"'),
        ("Daily.cs", daily, "PicksNeeded", 5, 'docs/09 says "відкрий 5 сейфів загадкою"'),
        ("Daily.cs", daily, "TopTier", 3, 'docs/09 says "виграй без зброї T3"'),
        ("Purse.cs", purse, "PerDaily", 40, 'docs/09 says "40 кожне"'),
        ("Purse.cs", purse, "DailiesPerDay", 3, 'docs/09 says "×3"'),
    ):
        got = constant(text, name)
        if got is None:
            problems.append("%s: no `const int %s`, so nothing holds %s"
                            % (where, name, why))
        elif got != want:
            problems.append("%s: %s is %d and should be %d — %s"
                            % (where, name, got, want, why))

    # The enum has to have as many members as Count claims.
    members = re.search(r"enum\s+DailyTask\s*\{([^}]*)\}", daily, flags=re.S)
    if members is None:
        problems.append("Daily.cs: no `enum DailyTask`")
    else:
        names = [m.strip().split("=")[0].strip()
                 for m in members.group(1).split(",") if m.strip()]
        count = constant(daily, "Count")
        if count is not None and len(names) != count:
            problems.append("Daily.cs: DailyTask has %d member(s) and Count says %d. The "
                            "menu would show a row for a task that cannot be finished, or "
                            "hide one that can" % (len(names), count))

        # ---- rule 6: every task has a title and can be finished
        title = body_of(daily, "string Title(")
        earned = body_of(daily, "int Earned(")
        for name in names:
            if title and name not in title:
                problems.append("Daily.cs: DailyTask.%s has no case in Title(), so the menu "
                                "has nothing to call it" % name)
            if earned and name not in earned:
                problems.append("Daily.cs: DailyTask.%s is never awarded in Earned(), so it "
                                "reads 0 of 5 for ever and its 40 gold is unreachable" % name)

    # ---- rule 2: the day's finished tasks come back out of the answer
    earned = body_of(daily, "int Earned(")
    if earned is None:
        problems.append("Daily.cs: no Earned(...) method")
    else:
        if not re.search(r"&\s*~", earned):
            problems.append(
                "Daily.cs: Earned() does not take the day's finished tasks back out of its "
                "answer (`& ~already`). Without that line the one task a player is good at "
                "pays 40 gold EVERY match of the day — 120 a day for one lesson, which is "
                "the exact bug the mask was introduced to fix")

        # ---- rule 3: it is a question, not a step
        for field in ("dailiesMask", "picksToday"):
            if re.search(r"\.%s\s*(=[^=]|\+=|\|=|-=)" % field, earned):
                problems.append(
                    "Daily.cs: Earned() writes progress.%s. It must only ASK — the caller "
                    "writes the answer down. Writing here means the match's picks are "
                    "already in the total when Earned adds them again, and three picked "
                    "locks pay out for docs/09's five" % field)

    # ---- rules 4, 5 and 7, in RecordMatch
    record = body_of(progress, "Reward RecordMatch(")
    if record is None:
        problems.append("PlayerProgress.cs: no RecordMatch(...)")
    else:
        pay = re.search(r"Purse\.ForMatch\s*\(([^;]*?)\)\s*;", record, flags=re.S)
        if pay is None:
            problems.append("PlayerProgress.cs: RecordMatch does not pay through "
                            "Purse.ForMatch, so docs/09's prices are no longer the ones "
                            "being paid")
        elif "DailiesDone" in pay.group(1) or "dailiesDone" in pay.group(1):
            problems.append(
                "PlayerProgress.cs: the purse is paid by the DAY'S TOTAL of finished tasks, "
                "not by what this match just finished. A task done once in the morning then "
                "pays 40 gold again every match until midnight")

        # Reset together, and in one place.
        resets = [m for m in re.finditer(r"dailiesMask\s*=\s*0", progress)]
        if not resets:
            problems.append("PlayerProgress.cs: nothing resets dailiesMask, so the day's "
                            "three tasks are finished once and for ever")
        elif len(resets) > 1:
            problems.append("PlayerProgress.cs: dailiesMask is reset in %d places. One of "
                            "them will one day forget picksToday and the day will roll over "
                            "by halves" % len(resets))
        else:
            # the same method has to clear both
            around = progress[max(0, resets[0].start() - 400):resets[0].start() + 400]
            if "picksToday" not in around:
                problems.append(
                    "PlayerProgress.cs: the day's task mask is reset without resetting "
                    "picksToday beside it. The player is then either handed a finished task "
                    "they have not done today, or four locks picked yesterday count towards "
                    "the five wanted today")

        # ---- rule 7: a bot match is played but not ranked
        if "AgainstBot" not in record:
            problems.append(
                "PlayerProgress.cs: RecordMatch does not look at facts.AgainstBot, so a "
                "practice match moves the ladder. docs/09 labels matches 1-5 as TRAINING "
                "and makes a bot the matchmaker's fallback at any rank — a bot that moved "
                "the rating would mean gaining and losing rank against bots")
        else:
            guard = re.search(r"if\s*\(\s*!\s*facts\.AgainstBot\s*\)\s*\{(.*?)\n\s*\}",
                              record, flags=re.S)
            if guard is None:
                problems.append(
                    "PlayerProgress.cs: facts.AgainstBot is mentioned but the Glicko-2 "
                    "update is not inside `if (!facts.AgainstBot)`, so it is not clear the "
                    "rating is what it protects")
            else:
                for field in ("rating", "deviation", "volatility"):
                    if not re.search(r"\b%s\s*=" % field, guard.group(1)):
                        problems.append(
                            "PlayerProgress.cs: `%s` is written outside the "
                            "`if (!facts.AgainstBot)` guard, so a training match changes it "
                            "after all" % field)

                if re.search(r"matchesPlayed\s*\+\+", guard.group(1)):
                    problems.append(
                        "PlayerProgress.cs: matchesPlayed++ is INSIDE the bot guard, so a "
                        "training match does not count as played — and docs/09's \"матч 6\" "
                        "never arrives, leaving the player in placement for ever")

        if "matchesPlayed++" not in record.replace(" ", ""):
            problems.append("PlayerProgress.cs: RecordMatch does not count the match as "
                            "played, so docs/09's novice protection never ends")

    # ---- docs/09 still says what this check believes it says
    doc = os.path.join(DOCS, "09-economy-and-ranking.md")
    if os.path.exists(doc):
        text = read(doc)
        for phrase, why in (("40 кожне", "the price of a daily task"),
                            ("5 сейфів", "the five safes of the first task"),
                            ("T3", "the tier the second task forbids")):
            if phrase not in text:
                problems.append(
                    "docs/09 no longer contains %r, so this check is holding the code to a "
                    "number the note has changed. Read the note and move the rule, do not "
                    "delete it" % phrase)

    if problems:
        for line in problems:
            print("daily_check: " + line)
        print("daily_check: %d problem(s)" % len(problems))
        return 1

    print("daily_check: ok (3 tasks, 40 gold each, 5 safes, tier 3 — as docs/09 prices them)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
