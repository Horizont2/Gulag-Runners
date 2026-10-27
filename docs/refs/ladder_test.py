"""Does the ladder do what docs/09 says it does?

Every assertion here is a sentence from that document turned into arithmetic. The point is
not that Glicko-2 is implemented -- a transcription of a paper can be checked by reading --
but that the NUMBERS chosen around it produce the behaviour the design was promised:
placement in about ten matches, a day's play worth 250-350 gold, a skin reachable in three
to ten days, and a newcomer who never meets a professional.

Run: python3 ladder_test.py
"""
import ladder_mirror as m

fails = []


def check(name, ok, detail=""):
    print("  %-62s %s" % (name, "PASS" if ok else "FAIL " + detail))
    if not ok:
        fails.append(name)


print("gold, against docs/09's table")
check("match played pays 10", m.PER_MATCH == 10)
check("a win adds 15", m.PER_WIN == 15)
check("first match of the day adds 25", m.FIRST_OF_DAY == 25)
check("a daily pays 40, three a day", m.PER_DAILY == 40 and m.DAILIES_PER_DAY == 3)
check("the weekly streak pays 150", m.WEEKLY_STREAK == 150)

r = m.reward(won=True, first_today=True, dailies_done=3, weekly=False)
check("a first win with all three dailies = 10+15+25+120", m.total(r) == 170, str(m.total(r)))

print("\na day at the pace docs/09 aims for: 250-350 gold")
# Six to ten matches is what "an active player" means in that document; the band is an AIM
# for that day, not a ceiling on the day somebody plays twice as much.
for matches, wins, dailies in ((6, 3, 3), (8, 4, 3), (10, 5, 3)):
    g = m.day_at(matches, wins, dailies)
    check("%2d matches, %d wins, 3 dailies -> %3d gold" % (matches, wins, g),
          250 <= g <= 350, str(g))

# And a long day has to stay on the same curve rather than running away from it -- the
# prices below are set against the band, so a grinder earning triple would make them lies.
heavy = m.day_at(20, 10, 3)
check("even twenty matches stays inside twice the band (%d)" % heavy, heavy <= 700, str(heavy))

print("\nand the prices that hang off it (docs/09: 3 to 10 days)")
day = m.day_at(8, 4, 3)
check("a T1 skin at 800 is about three days", 2.0 <= 800 / day <= 4.0, "%.1f" % (800 / day))
check("a T3 skin at 3000 is about ten days", 8.0 <= 3000 / day <= 13.0, "%.1f" % (3000 / day))

print("\nglicko-2 invariants")
fresh = m.fresh()
peer = dict(value=1500.0, deviation=80.0, volatility=0.06)
won = m.update(fresh, peer, 1.0)
lost = m.update(fresh, peer, 0.0)
check("a win raises the rating", won["value"] > fresh["value"])
check("a loss lowers it", lost["value"] < fresh["value"])
check("either way the ladder becomes MORE sure",
      won["deviation"] < fresh["deviation"] and lost["deviation"] < fresh["deviation"])

settled = dict(value=1500.0, deviation=60.0, volatility=0.06)
moved_fresh = abs(m.update(fresh, peer, 1.0)["value"] - 1500.0)
moved_settled = abs(m.update(settled, peer, 1.0)["value"] - 1500.0)
check("an uncertain rating moves much further than a settled one",
      moved_fresh > moved_settled * 3.0, "%.0f vs %.0f" % (moved_fresh, moved_settled))

strong = dict(value=2000.0, deviation=80.0, volatility=0.06)
upset = abs(m.update(settled, strong, 1.0)["value"] - 1500.0)
expected = abs(m.update(settled, peer, 1.0)["value"] - 1500.0)
check("beating somebody far above you is worth more than beating a peer",
      upset > expected, "%.1f vs %.1f" % (upset, expected))

print("\nplacement: docs/09 claims about ten matches, not a hundred")


def place(true_rating, games):
    """Somebody who really belongs at true_rating, playing a pool sitting at 1500."""
    me = m.fresh()
    pool = dict(value=1500.0, deviation=80.0, volatility=0.06)
    # Win probability against the pool, from the same logistic the ladder uses.
    import math
    p = 1.0 / (1.0 + math.exp(-(true_rating - 1500.0) / 400.0 * math.log(10)))
    # Deterministic alternation at that rate, so the test cannot flake.
    carry = 0.0
    for _ in range(games):
        carry += p
        score = 1.0 if carry >= 1.0 else 0.0
        if score:
            carry -= 1.0
        me = m.update(me, pool, score)
    return me


after10 = place(1900.0, 10)
check("a platinum-level player is out of silver within 10 matches",
      m.league_index(after10["value"]) >= 2,
      "%s at %.0f" % (m.name(after10["value"]), after10["value"]))
check("and the ladder is far surer than it started",
      after10["deviation"] < 150.0, "%.0f" % after10["deviation"])

weak = place(1150.0, 10)
check("a bronze-level player is out of gold within 10 matches",
      m.league_index(weak["value"]) <= 1,
      "%s at %.0f" % (m.name(weak["value"]), weak["value"]))

print("\nthe leagues themselves")
ok = True
for rating in range(900, 2401, 1):
    nm = m.name(rating)
    if not nm or m.league_index(rating) < 0:
        ok = False
check("every rating from 900 to 2400 has a name", ok)

bad = [p for p in (m.progress(x) for x in range(900, 2401)) if p < 0.0 or p > 1.0]
check("progress never leaves 0..1", not bad)

for low, high, league in ((1300, 1499, "Silver"), (1500, 1699, "Gold"),
                          (1700, 1899, "Platinum"), (1900, 2099, "Diamond")):
    seen = [m.division_of(x) for x in range(low, high + 1)]
    check("%-8s runs III -> II -> I and nothing else" % league,
          seen[0] == 3 and seen[-1] == 1 and sorted(set(seen), reverse=True) == [3, 2, 1],
          str(sorted(set(seen))))

check("2100 and above is Runner, with no division", m.name(2100) == "Runner" and
      m.name(2600) == "Runner")
check("the bottom of the ladder is Bronze III", m.name(900) == "Bronze III")

print("\nnewcomer protection -- the whole point of the ranking")
check("matches 1-5 are bots", m.pool_for(0) == "Training" and m.pool_for(4) == "Training")
check("matches 6-20 are the newcomer pool",
      m.pool_for(5) == "Newcomers" and m.pool_for(19) == "Newcomers")
check("match 21 opens it up", m.pool_for(20) == "Open")

check("a player in training meets nobody", not m.may_meet(2, 1500, 300, 1500))
check("a newcomer never meets a platinum", not m.may_meet(10, 1500, 400, 1750))
check("a newcomer never meets a diamond", not m.may_meet(10, 1500, 400, 1950))
check("a newcomer never meets a runner", not m.may_meet(10, 1500, 900, 2300))
check("two newcomers may meet", m.may_meet(10, 1450, 12, 1550))
check("a newcomer may meet a gold", m.may_meet(10, 1500, 400, 1600))
check("past 20 matches anybody may meet anybody", m.may_meet(40, 1200, 900, 2300))

print("\nthe season reset is SOFT (docs/09: 40% back to the middle)")
top = dict(value=2100.0, deviation=60.0, volatility=0.06)
after = m.season_reset(top)
check("a 2100 player comes back at 1860, not at 1500",
      abs(after["value"] - 1860.0) < 1.0, "%.0f" % after["value"])
# NOT "keeps their league". A 40% pull from the very bottom of Runner (2100) lands at
# 1860, which is forty points under the diamond floor -- two leagues, because Runner sits
# straight on top of diamond with no room between them. That is the design working: a soft
# reset still costs something. What has to hold is that it is a RESET TOWARDS the middle
# and not a reset TO it.
check("the top of the ladder drops at most two leagues",
      m.league_index(top["value"]) - m.league_index(after["value"]) <= 2,
      m.name(after["value"]))
for before in (1950.0, 1800.0, 1650.0, 1450.0):
    back = m.season_reset(dict(value=before, deviation=60.0, volatility=0.06))
    check("%.0f (%s) drops at most one league -> %s"
          % (before, m.name(before), m.name(back["value"])),
          m.league_index(before) - m.league_index(back["value"]) <= 1)
check("the ladder is made unsure again so the first week sorts people",
      after["deviation"] >= 120.0, "%.0f" % after["deviation"])

low = m.season_reset(dict(value=1000.0, deviation=60.0, volatility=0.06))
check("and somebody at the bottom is pulled UP, not reset to zero",
      low["value"] > 1000.0, "%.0f" % low["value"])

print("\ntime away widens the uncertainty rather than the rating")
away = m.idle(dict(value=1800.0, deviation=60.0, volatility=0.06), 10)
check("ten quiet periods leave the rating alone", abs(away["value"] - 1800.0) < 1e-9)
check("but the ladder is less sure of it", away["deviation"] > 60.0, "%.0f" % away["deviation"])
check("and never more unsure than a brand new player", away["deviation"] <= 350.0)

print("\nthe search: the net widens, and never past what the ladder allows")
check("the net starts at 60 points either side", m.band(0) == 60, str(m.band(0)))
check("it is fully cast at 14 seconds (400)", m.band(14) == 400, str(m.band(14)))
check("and never goes wider", m.band(600) == 400, str(m.band(600)))
check("the bar runs 0 to 1 across that",
      m.cast(0) == 0.0 and m.cast(14) == 1.0,
      "%.2f..%.2f" % (m.cast(0), m.cast(14)))
check("and is monotone, never jumping back",
      all(m.cast(t / 2.0) <= m.cast((t + 1) / 2.0) for t in range(0, 60)))

check("two silvers 200 apart are refused on the first second",
      not m.accepts(0, 40, 1400, 40, 1600))
check("and accepted once the net has widened to reach them",
      m.accepts(10, 40, 1400, 40, 1600))

# THE safety property, and the reason any of this exists. docs/09's Miro note is
# "professionals wont fight with starting players"; an empty queue is exactly when
# somebody would be tempted to break it, so the band must never be able to.
worst = []
for seconds in range(0, 121, 1):
    for rating in range(1700, 2400, 25):
        if m.accepts(seconds, 10, 1500, 400, rating):
            worst.append((seconds, rating))
check("a newcomer never meets Platinum or above, at ANY wait (121s x 28 ratings)",
      not worst, str(worst[:3]))

trainee = [s for s in range(0, 121) if m.accepts(s, 2, 1500, 400, 1500)]
check("somebody in their first five matches is never given a human", not trainee,
      str(trainee[:3]))

print("\nand when it gives up, it gives up to a bot rather than to a spinner")
check("the first five matches are bots from the first second", m.offer_bot(2, 0))
check("a veteran is not fobbed off at twenty seconds", not m.offer_bot(40, 20))
check("but is at thirty", m.offer_bot(40, 30))
early = [s for s in range(0, 25) if m.offer_bot(40, s)]
check("never before the net is fully cast AND the clock has run", not early, str(early[:3]))
check("the net being wide is not enough on its own",
      not m.offer_bot(40, 14) and m.cast(14) >= 1.0)

print("\nthe weekly streak, which until now nothing in the game could earn")
# docs/09 lists it at 150 gold "for retention" and does not define the week. Days in a
# row is the reading taken: a missed day starts again, and the seventh pays.
import datetime  # noqa: E402

day = datetime.date(2025, 3, 3)
week = [day + datetime.timedelta(days=i) for i in range(7)]
in_a_row, paid = m.play_days(week)
check("seven days in a row pays once", paid == 1 and in_a_row == 7,
      "in a row %d, paid %d" % (in_a_row, paid))

check("six days does not pay", m.play_days(week[:6])[1] == 0)
check("two weeks pays twice",
      m.play_days([day + datetime.timedelta(days=i) for i in range(14)])[1] == 2)

broken = [day + datetime.timedelta(days=i) for i in range(4)] + \
         [day + datetime.timedelta(days=i) for i in range(5, 9)]
check("a day missed starts the count again", m.play_days(broken)[1] == 0)

twice = [day, day, day + datetime.timedelta(days=1)]
check("two matches on one day are one day", m.play_days(twice)[0] == 2)

back = [day, day - datetime.timedelta(days=3), day + datetime.timedelta(days=1)]
check("a clock corrected backwards does not cost the streak",
      m.play_days(back)[0] == 2, str(m.play_days(back)))

print("\nand the day number survives the 31st of December")
new_year = [datetime.date(2025, 12, 28) + datetime.timedelta(days=i) for i in range(7)]
check("a streak running into January still pays", m.play_days(new_year)[1] == 1)
check("the fault this replaces is caught",
      m.play_days(new_year, day_of=m.folded_day_number)[1] == 0,
      "day-of-year plus year*400 puts 35 days between the 31st and the 1st")

check("consecutive dates are consecutive numbers",
      all(m.day_number(datetime.date(2025, 1, 1) + datetime.timedelta(days=i + 1)) -
          m.day_number(datetime.date(2025, 1, 1) + datetime.timedelta(days=i)) == 1
          for i in range(0, 800)))

print("\nand a day of play still lands where docs/09 wants it, streak included")
# The streak is one day in seven, so it adds about 21 gold a day on average -- which has
# to not push the daily total past the band the skin prices were set against.
with_streak = m.day_at(6, 3, 3) + m.WEEKLY_STREAK / 7.0
check("250-350 a day holds with the streak averaged in",
      250 <= with_streak <= 350, "%.0f" % with_streak)

print("\nthe pool, as the search screen actually drives it")
# The screen used to decide this itself, in an if, in the middle of Searching(). It asks
# a pool now -- and the floor under every pool has to behave exactly as that if did, or
# moving it broke the one thing docs/09 is most specific about.

at, got = m.search(m.BotQueue(), played=2)
check("somebody placing is given a practice opponent at once", at is not None and at < 0.05,
      "%.2f s" % (at if at is not None else -1))
check("and told that is what it is", got and got["name"] == "TRAINING OPPONENT", str(got))

at, got = m.search(m.BotQueue(), played=40)
check("a veteran waits the full twenty-five seconds", at is not None and 24.9 <= at <= 25.2,
      "%.2f s" % (at if at is not None else -1))
check("and is told the queue was thin, not that they were placing",
      got and got["name"].startswith("A BOT"), str(got))
check("the net is fully cast by the time it happens", m.cast(at) >= 1.0)

q = m.BotQueue()
m.search(q, played=2)
check("a pairing is handed over exactly once", q.try_take() is None,
      "a screen that read it twice would load two matches")

q = m.BotQueue()
q.begin(1500, 40)
q.poll(30.0)
q.cancel()
check("cancelling clears a pairing that was waiting", q.try_take() is None)
check("and leaves the pool idle", q.state == m.IDLE)

at, got = m.search(m.BotQueue(seed=0), played=2)
check("even a bot match is dealt from a number",
      got is not None and "seed" in got,
      "the arena is dealt, not sent -- a match with no seed is two different arenas")

print("\n%s" % ("ALL PASS" if not fails else "%d FAIL(S): %s" % (len(fails), ", ".join(fails))))
raise SystemExit(1 if fails else 0)
