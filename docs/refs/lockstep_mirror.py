"""The lockstep link, in Python, so its rules can be argued with.

Mirrors Assets/_Game/Net/LanLink.cs and the clock in NetMatch.cs. Nothing here draws or
sends anything; it is the bookkeeping only -- which tick a ring slot holds, what a packet
carries, what the handshake answers -- because that bookkeeping is where every bug in a
lockstep link lives and none of it is visible from a frame of the game.

Two phones with one tick of disagreement play two different matches, and the symptom is
not an error: it is the other player appearing to behave strangely. So the rules are
written down twice, once in C# and once here, and checked against each other.
"""

RING = 256
MASK = RING - 1
CARRIED = 16


class Input:
    """What TickInput carries: the buttons, and where the stick points."""

    __slots__ = ("flags", "x", "y")

    def __init__(self, flags=0, x=0, y=0):
        self.flags = flags
        self.x = x
        self.y = y

    def __eq__(self, other):
        return (isinstance(other, Input) and self.flags == other.flags
                and self.x == other.x and self.y == other.y)

    def __repr__(self):
        return f"Input({self.flags}, {self.x}, {self.y})"


NOTHING = Input()


class Ring:
    """A tick's input, kept in a slot that says which tick it is holding.

    The slot number alone is not enough and that is the whole point of this class. The
    ring is 256 ticks -- 4.3 seconds -- and a match is minutes, so every slot comes round
    many times. A plain "have I got this one" flag reads as true for ever after the first
    lap, and both devices freeze on inputs four seconds stale while reporting nothing
    wrong at all.
    """

    def __init__(self, wrap_bug=False):
        self._at = [-1] * RING
        self._value = [NOTHING] * RING
        self._wrap_bug = wrap_bug          # the pre-fix behaviour, for the test to catch

    def put(self, tick, value, overwrite_older=True):
        """Store an input unless this slot already holds this tick, or a newer one."""
        if tick < 0:
            return False
        slot = tick & MASK
        if self._wrap_bug:
            if self._at[slot] != -1:       # "have I got this one", the fault
                return False
        elif self._at[slot] == tick:
            return False                   # the same tick again: frozen, as sent
        elif self._at[slot] > tick and overwrite_older:
            return False                   # the slot has moved on; this one is past
        self._at[slot] = tick
        self._value[slot] = value
        return True

    def has(self, tick):
        return tick >= 0 and self._at[tick & MASK] == tick

    def get(self, tick):
        return self._value[tick & MASK] if self.has(tick) else NOTHING


def packet(ring, newest):
    """What SendLocal puts on the wire: the newest input and the CARRIED before it."""
    body = []
    for i in range(CARRIED):
        t = newest - i
        body.append((t, ring.get(t) if t >= 0 else NOTHING))
    return newest, body


def receive(ring, wire):
    """What TakeInput does with one: never rewrite a tick already taken."""
    _newest, body = wire
    for tick, value in body:
        if tick < 0:
            continue
        ring.put(tick, value)


# --------------------------------------------------------------------- the handshake

HELLO, WELCOME, INPUT, BEACON, CHECK = 1, 2, 3, 4, 5


class End:
    """One device's side of the handshake. Role, seed, and whether it has answered."""

    def __init__(self, role, seed=0, echo_hello=False):
        self.role = role                   # "host" or "guest"
        self.seed = seed if role == "host" else 0
        self.connected = False
        # The pre-fix behaviour: a hello was answered with a hello, by either end, and
        # neither end had any reason to stop. That is a packet per round trip for as long
        # as the game runs, which on a phone is a warm battery and no explanation.
        self.echo_hello = echo_hello
        # The pre-fix behaviour, for the test to catch: any packet at all counted as
        # being connected, seed or no seed.
        self.seed_only_from_welcome = True
        self.outbox = []

    def take(self, kind, payload=None):
        if kind == HELLO:
            if self.echo_hello:
                self.outbox.append((HELLO, None))
                self.connected = True
                return
            if self.role != "host":
                return                     # only a host answers, so the greeting ends
            self.outbox.append((WELCOME, self.seed))
            self.connected = True
        elif kind == WELCOME:
            if self.role != "guest" or self.connected:
                return
            self.seed = payload
            self.connected = True
        elif kind == INPUT:
            # Input alone connects the HOST, which has the seed already. Never the
            # guest: the seed arrives in the welcome and nowhere else, so a guest that
            # started on an input packet whose welcome had been lost would deal its
            # arena from zero against a host dealing from the real number -- two
            # different matches, agreed on tick by tick, with nothing to notice.
            if self.role == "host" or not self.seed_only_from_welcome:
                self.connected = True


def handshake(host, guest, rounds=8):
    """Run the two ends against each other and count what crossed the wire."""
    crossed = 0
    guest.outbox.append((HELLO, None))
    for _ in range(rounds):
        going_to_host, guest.outbox = guest.outbox, []
        going_to_guest, host.outbox = host.outbox, []
        if not going_to_host and not going_to_guest:
            break
        crossed += len(going_to_host) + len(going_to_guest)
        for kind, payload in going_to_host:
            host.take(kind, payload)
        for kind, payload in going_to_guest:
            guest.take(kind, payload)
    return crossed


# ------------------------------------------------------------------------ the clock

def step_order(local_player):
    """Which input NetMatch.Step hands to fighter 0 and fighter 1 on this device."""
    return ("mine", "theirs") if local_player == 0 else ("theirs", "mine")


# --------------------------------------------------------------- the input budget

TICK_MS = 1000.0 / 60.0


def suggested_delay(rtt_ms, floor=4, ceiling=12, measure=True):
    """How far ahead input is sent, from the round trip actually measured.

    Mirrors LanLink.SuggestedDelay. Lockstep waits for the other player, so the budget
    has to cover the ONE-WAY trip -- the input has to be there before the tick it
    belongs to -- plus a tick of slack for a frame landing badly.
    """
    if not measure or rtt_ms <= 0:
        return floor
    ticks = (rtt_ms * 3 // 100) + 2
    return max(floor, min(ticks, max(floor, ceiling)))


def covers(rtt_ms, delay):
    """Does that many ticks of budget actually cover that round trip?"""
    return delay * TICK_MS >= rtt_ms / 2.0


def worst_of(samples, keep=8):
    """What the link is judged on: the worst of the last few, not the average.

    One late input stalls the tick and both players see it, so a budget set from the
    average is a budget that is wrong exactly when it matters.
    """
    recent = samples[-keep:]
    return max(recent) if recent else 0


def sent_ticks(delays, start_sent=3):
    """Which tick each frame sends, when the budget moves while the match runs.

    Mirrors NetMatch: the furthest tick sent only ever goes FORWARD. A budget that
    shrinks would otherwise name a tick already sent, and SendLocal rightly refuses to
    overwrite one -- throwing away the newer thumb with it.
    """
    sent_to = start_sent
    out = []
    for tick, delay in enumerate(delays):
        want = tick + delay
        if want > sent_to:
            sent_to = want
            out.append(("new", want))
        else:
            out.append(("again", sent_to))
    return out


# ------------------------------------------------------------------- the rematch

REMATCH_LEAD = 30


class Rematch:
    """When "Again" takes effect, which must be a tick and not a moment.

    Tapping the button restarts the match -- and if each device restarts when its own
    player's finger lands, or even when the packet arrives, the two deal the new arena on
    two different ticks and play two games from then on. So the host names a tick a little
    way ahead, both devices pass through it, and both deal there.
    """

    def __init__(self, role, lead=REMATCH_LEAD, now_bug=False):
        self.role = role
        self.lead = lead
        self.now_bug = now_bug     # the fault: restart the moment it is asked for
        self.at = -1
        self.done = -1
        self.seed = 0
        self.outbox = []

    def ask(self, now, seed=0xABCD):
        """The button, on this device."""
        if self.now_bug:
            self.at = now
            self.seed = seed
            return
        if self.role == "host":
            self.schedule(now, seed)
        else:
            self.outbox.append((-1, 0))          # a request; the host decides

    def schedule(self, now, seed):
        if self.at >= 0:
            return                                # one is already on its way
        self.at = now + self.lead
        self.seed = seed
        self.outbox.append((self.at, self.seed))

    def take(self, when, seed, now):
        """A rematch packet arriving."""
        if when < 0:
            if self.role == "host":
                self.schedule(now, 0xABCD)
            return
        if self.role == "guest" and when > self.done:
            self.at = when
            self.seed = seed

    def tick(self, now):
        """True on the first tick at or past the agreed one. Once."""
        if self.at < 0 or now < self.at:
            return False
        self.done = self.at
        self.at = -1
        return True
