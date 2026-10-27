"""Glicko-2, the leagues and the purse, mirrored from Assets/_Game/Meta.

Written against docs/09 and Glickman's 2013 paper, beside the C# rather than from it, so
the two can be read against each other. Nothing here is rolled back and nothing runs inside
a tick -- the ladder settles once, after a match is already over -- so this uses floats
where the combat mirrors use fixed point, and that is deliberate.
"""
import math

SCALE = 173.7178
TAU = 0.5
EPSILON = 1e-6

FLOORS = [0, 1300, 1500, 1700, 1900, 2100]
LEAGUES = ["Bronze", "Silver", "Gold", "Platinum", "Diamond", "Runner"]
BAND = 200
DIVISIONS = 3

PER_MATCH, PER_WIN, FIRST_OF_DAY, PER_DAILY, WEEKLY_STREAK = 10, 15, 25, 40, 150
DAILIES_PER_DAY = 3


def fresh():
    return dict(value=1500.0, deviation=350.0, volatility=0.06)


def _volatility(phi, v, delta, sigma):
    a = math.log(sigma * sigma)

    def f(x):
        ex = math.exp(x)
        top = ex * (delta * delta - phi * phi - v - ex)
        bottom = 2.0 * (phi * phi + v + ex) ** 2
        return top / bottom - (x - a) / (TAU * TAU)

    A = a
    if delta * delta > phi * phi + v:
        B = math.log(delta * delta - phi * phi - v)
    else:
        k = 1
        while f(a - k * TAU) < 0 and k < 100:
            k += 1
        B = a - k * TAU

    fa, fb = f(A), f(B)
    guard = 0
    while abs(B - A) > EPSILON and guard < 200:
        guard += 1
        C = A + (A - B) * fa / (fb - fa)
        fc = f(C)
        if fc * fb <= 0:
            A, fa = B, fb
        else:
            fa /= 2.0
        B, fb = C, fc
    return math.exp(A / 2.0)


def update(self_r, opp, score):
    mu = (self_r["value"] - 1500.0) / SCALE
    phi = self_r["deviation"] / SCALE
    mu_j = (opp["value"] - 1500.0) / SCALE
    phi_j = opp["deviation"] / SCALE

    g = 1.0 / math.sqrt(1.0 + 3.0 * phi_j * phi_j / (math.pi ** 2))
    e = 1.0 / (1.0 + math.exp(-g * (mu - mu_j)))

    v = 1.0 / (g * g * e * (1.0 - e))
    delta = v * g * (score - e)
    sigma = _volatility(phi, v, delta, self_r["volatility"])

    phi_star = math.sqrt(phi * phi + sigma * sigma)
    phi_new = 1.0 / math.sqrt(1.0 / (phi_star ** 2) + 1.0 / v)
    mu_new = mu + phi_new * phi_new * g * (score - e)

    return dict(value=1500.0 + SCALE * mu_new,
                deviation=SCALE * phi_new,
                volatility=sigma)


def idle(r, periods):
    if periods <= 0:
        return dict(r)
    phi = r["deviation"] / SCALE
    grown = math.sqrt(phi * phi + periods * r["volatility"] ** 2)
    out = dict(r)
    out["deviation"] = min(350.0, SCALE * grown)
    return out


def season_reset(r, pull=0.4, min_deviation=120.0):
    out = dict(r)
    out["value"] = r["value"] + (1500.0 - r["value"]) * pull
    out["deviation"] = max(r["deviation"], min_deviation)
    return out


def league_index(rating):
    for i in range(len(FLOORS) - 1, -1, -1):
        if rating >= FLOORS[i]:
            return i
    return 0


def division_of(rating):
    i = league_index(rating)
    if i == 5:
        return 1
    floor = FLOORS[1] - BAND if i == 0 else FLOORS[i]
    into = max(0.0, rating - floor) if i == 0 else rating - floor
    step = int(into / (BAND / DIVISIONS))
    return DIVISIONS - min(DIVISIONS - 1, max(0, step))


def name(rating):
    i = league_index(rating)
    if i == 5:
        return "Runner"
    return "%s %s" % (LEAGUES[i], {1: "I", 2: "II", 3: "III"}[division_of(rating)])


def progress(rating):
    i = league_index(rating)
    if i == 5:
        return 1.0
    floor = FLOORS[1] - BAND if i == 0 else FLOORS[i]
    return min(1.0, max(0.0, (rating - floor) / BAND))


def pool_for(matches):
    return "Training" if matches < 5 else "Newcomers" if matches < 20 else "Open"


def may_meet(matches_a, rating_a, matches_b, rating_b):
    a, b = pool_for(matches_a), pool_for(matches_b)
    if a == "Training" or b == "Training":
        return False
    if a == "Newcomers" and league_index(rating_b) >= 3:
        return False
    if b == "Newcomers" and league_index(rating_a) >= 3:
        return False
    return True


def reward(won, first_today, dailies_done, weekly):
    dailies_done = max(0, min(DAILIES_PER_DAY, dailies_done))
    return dict(played=PER_MATCH,
                won=PER_WIN if won else 0,
                first=FIRST_OF_DAY if first_today else 0,
                dailies=dailies_done * PER_DAILY,
                streak=WEEKLY_STREAK if weekly else 0)


def total(r):
    return sum(r.values())


def day_at(matches, wins, dailies_done):
    return (matches * PER_MATCH + wins * PER_WIN +
            (FIRST_OF_DAY if matches > 0 else 0) + dailies_done * PER_DAILY)


# ------------------------------------------------------------- what gets saved
# Mirrors the day number and the streak in Assets/_Game/Meta/PlayerProgress.cs.
import datetime                                                   # noqa: E402

DAYS_IN_A_STREAK = 7
EPOCH = datetime.date(2024, 1, 1)


def day_number(date):
    """The one definition of a day: whole days since a fixed date, in UTC."""
    return (date - EPOCH).days


def folded_day_number(date):
    """What version 1 saved: day-of-year plus the year times four hundred.

    Kept so the test can show what it did at New Year, which is drop the streak of
    every player holding one -- in the week they are most likely to be playing.
    """
    return date.timetuple().tm_yday + date.year * 400


def advance_streak(days_in_a_row, last_played_day, today):
    """Returns (days_in_a_row, paid) after a first match on `today`."""
    if last_played_day < 0:
        days_in_a_row = 1
    elif today == last_played_day + 1:
        days_in_a_row += 1
    elif today > last_played_day:
        days_in_a_row = 1
    else:
        return days_in_a_row, False            # the clock went backwards
    return days_in_a_row, days_in_a_row % DAYS_IN_A_STREAK == 0


def play_days(days, day_of=day_number, streak=advance_streak):
    """Play one match on each of `days` (dates), and report the gold and the streak."""
    last, in_a_row, paid = -1, 0, 0
    for date in days:
        today = day_of(date)
        if today == last:
            continue                            # not the first match of that day
        in_a_row, hit = streak(in_a_row, last, today)
        if hit:
            paid += 1
        last = max(last, today)      # never backwards: see RecordMatch
    return in_a_row, paid


# ---------------------------------------------------------------- the search
# Mirrors Assets/_Game/Meta/Matchmaker.cs.
START_BAND, WIDEN_PER_SECOND, MAX_BAND, BOT_AFTER = 60, 25, 400, 25


def band(seconds, start=START_BAND, widen=WIDEN_PER_SECOND, top=MAX_BAND):
    return min(max(start, top), start + round(max(0.0, seconds) * widen))


def cast(seconds, start=START_BAND, widen=WIDEN_PER_SECOND, top=MAX_BAND):
    top = max(start, top)
    if top == start:
        return 1.0
    return min(1.0, max(0.0, (band(seconds, start, widen, top) - start) / (top - start)))


def accepts(seconds, matches_a, rating_a, matches_b, rating_b, **kw):
    # The ladder's own rules FIRST. The band only ever narrows what they already allow.
    if not may_meet(matches_a, rating_a, matches_b, rating_b):
        return False
    return abs(rating_a - rating_b) <= band(seconds, **kw)


def offer_bot(matches_played, seconds, bot_after=BOT_AFTER, **kw):
    if pool_for(matches_played) == "Training":
        return True
    return seconds >= max(1, bot_after) and cast(seconds, **kw) >= 1.0


# ------------------------------------------------------------------- the pool
# Mirrors Assets/_Game/Meta/BotQueue.cs and how MatchSearch drives it.
IDLE, LOOKING, FOUND, FAILED = 0, 1, 2, 3


class BotQueue:
    """The pool with nobody in it: everybody is paired with a practice opponent.

    Not a stub. docs/09 makes the first five matches bots by design, and is equally
    clear that twenty-five seconds of an empty queue is a player deciding the game is
    dead. This floor stays under every real pool that is ever put above it.
    """

    def __init__(self, seed=0x5EED):
        self.state = IDLE
        self.seed = seed
        self._waiting = False
        self._played = 0
        self.found = None

    def begin(self, rating, played):
        self._played = played
        self._waiting = False
        self.state = LOOKING

    def poll(self, seconds):
        if self.state != LOOKING or self._waiting:
            return
        if not offer_bot(self._played, seconds):
            return
        self.found = dict(local_player=0, seed=self.seed, against_bot=True,
                          opponent_rating=1500,
                          name="TRAINING OPPONENT" if pool_for(self._played) == "Training"
                               else "A BOT, WHILE THE QUEUE IS THIN")
        self._waiting = True
        self.state = FOUND

    def try_take(self):
        if not self._waiting:
            return None
        self._waiting = False
        return self.found

    def cancel(self):
        self._waiting = False
        self.state = IDLE


def search(queue, played, rating=1500.0, step=1 / 60.0, limit=120.0):
    """Run the search screen's loop against a pool. Returns (seconds, pairing)."""
    queue.begin(rating, played)
    seconds = 0.0
    while seconds < limit:
        queue.poll(seconds)
        got = queue.try_take()
        if got is not None:
            return seconds, got
        seconds += step
    return None, None
