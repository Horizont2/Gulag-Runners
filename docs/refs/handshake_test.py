"""Do both phones get into the arena, or just one?

The search screen holds each device at the gate until the OTHER one says its arena is
up, so that two fighters start the same match at the same moment rather than one of them
starting alone and then stalling on inputs that have not been sent yet.

That gate was written once and was wrong in a way nothing caught: each side stopped
announcing itself the moment it HEARD the other. Whichever device finished loading
second received the first one's announcement, set "they are loaded", and returned before
ever sending its own -- so the first device waited for a packet that was now never going
to be sent. One player got into the arena. The other sat on the search screen with an
opponent found and nothing happening, for ever.

That is a two-sided protocol, and a two-sided protocol has to be run from both sides to
be tested at all. This is both sides, with the loads landing at different times and with
packets being dropped, and it asserts the only thing that matters: BOTH go through.

    python3 handshake_test.py
"""
import sys

TICK = 1.0 / 60.0

# Mirrors OnlineMatch: announce every quarter second until answered, then every second.
FAST, SLOW = 0.25, 1.0
TIMEOUT = 6.0

ok = True


def check(name, cond, detail=""):
    global ok
    ok &= bool(cond)
    print(f"  {'PASS' if cond else 'FAIL'}  {name}{('  ' + detail) if detail else ''}")


class Side:
    """One phone's half of the gate, as OnlineMatch runs it."""

    def __init__(self, loads_at, keep_announcing=True, relay_open=True):
        self.loads_at = loads_at
        self.keep = keep_announcing
        self.relay_open = relay_open
        self.loaded = False
        self.heard = False
        self.loaded_at = None
        self.next_say = 0.0
        self.went_at = None

    def ready(self, now):
        if self.heard:
            return True
        # GaveUpWaiting, and the `relay_open` is the fix rather than the detail.
        #
        # OpponentReady used to read `|| !relayOpen`, meaning "nobody to wait for" --
        # which is true of a practice opponent and catastrophically false of a relay
        # that FAILED. The gate opened instantly, the arena loaded, and the player was
        # put alone in it with the other fighter standing still, having been told
        # "opponent found" four screens earlier. Two players doing that at once, each in
        # their own arena, is the bug report about both of them driving one character.
        #
        # Going early is cheap when the link works and one datagram went missing. With
        # no link at all it is the whole fault, so it is no longer allowed.
        return (self.loaded and self.relay_open and TIMEOUT > 0
                and now - self.loaded_at > TIMEOUT)

    def update(self, now, link):
        if not self.loaded and now >= self.loads_at:
            self.loaded = True
            self.loaded_at = now
            self.next_say = 0.0

        if self.loaded and self.went_at is None and self.ready(now):
            self.went_at = now

        if not self.loaded:
            return
        # The bug: `or self.heard` here is what shipped, and it is the whole fault.
        if not self.keep and self.heard:
            return
        if now < self.next_say:
            return
        self.next_say = now + (SLOW if self.heard else FAST)
        link.send(self)


class Link:
    def __init__(self, a, b, drop=0):
        self.a, self.b, self.drop = a, b, drop
        self.sent = 0

    def send(self, who):
        self.sent += 1
        if self.drop and self.sent % self.drop == 0:
            return                      # this one is lost
        (self.b if who is self.a else self.a).heard = True


def run(a_at, b_at, drop=0, keep=True, limit=30.0):
    a, b = Side(a_at, keep), Side(b_at, keep)
    link = Link(a, b, drop)
    now = 0.0
    while now < limit:
        a.update(now, link)
        b.update(now, link)
        now += TICK
    return a, b


print("Both phones load, at different moments")
for gap in (0.0, 0.5, 2.0, 5.0):
    a, b = run(1.0, 1.0 + gap)
    both = a.went_at is not None and b.went_at is not None
    apart = abs((a.went_at or 0) - (b.went_at or 0)) if both else None
    check(f"a {gap:.1f} s gap lets both in", both,
          f"started {apart * 1000:.0f} ms apart" if both else "one of them never went")

print("\nAnd that is exactly what the shipped version did not do")
a, b = run(1.0, 1.5, keep=False)
gap = abs(a.went_at - b.went_at)
check("stopping the announcement on hearing one strands the other", gap > 1.0,
      f"they start {gap:.1f} s apart, and only because the timeout rescues it -- "
      f"without one that is for ever, which is what was shipped")
fixed_a, fixed_b = run(1.0, 1.5, keep=True)
check("and keeping it up fixes exactly that",
      abs(fixed_a.went_at - fixed_b.went_at) < 0.1,
      f"{abs(fixed_a.went_at - fixed_b.went_at) * 1000:.0f} ms apart instead")

print("\nLosing packets")
for drop in (2, 3, 5):
    a, b = run(1.0, 1.4, drop=drop)
    both = a.went_at is not None and b.went_at is not None
    check(f"both still get in with every {drop}th datagram lost", both,
          "" if both else "one of them never went")

print("\nThe other side never answers at all")
a = Side(1.0)
link = Link(a, Side(1e9), drop=0)
link.b = Side(1e9)                      # a phone that never loads
now = 0.0
while now < 30.0:
    a.update(now, link)
    now += TICK
check("one side alone still goes in, rather than hanging for ever",
      a.went_at is not None,
      f"gave up waiting after {a.went_at - 1.0:.1f} s" if a.went_at else "hung")
check("and it waited the timeout first, not none of it",
      a.went_at is not None and a.went_at - 1.0 >= TIMEOUT - 0.1,
      f"{a.went_at - 1.0:.2f} s against a {TIMEOUT} s timeout" if a.went_at else "")

print("\nThe relay never opened at all, which is a different thing entirely")
# There is no other side to be waited for here, and no amount of waiting will produce
# one: the handshake threw, or Relay is off on the dashboard, or both devices thought
# they were the host and so both allocated. Going in anyway is what put a player alone
# in an arena believing they were online.
alone = Side(1.0, relay_open=False)
nowhere = Link(alone, Side(1e9), drop=0)
now = 0.0
while now < 30.0:
    alone.update(now, nowhere)
    now += TICK
check("a side with no relay never goes through the gate",
      alone.went_at is None,
      "it has to be told the match failed, not handed an arena with nobody in it")
check("and the timeout does not rescue it, because there is nothing to rescue",
      not alone.ready(30.0),
      "30 s is five timeouts")
# The same side with the relay up is the case above, and it must still go in: the fix
# must not have turned a lost datagram into a dead match.
working = Side(1.0, relay_open=True)
link = Link(working, Side(1e9), drop=0)
now = 0.0
while now < 30.0:
    working.update(now, link)
    now += TICK
check("while a working relay whose other end is slow still goes in",
      working.went_at is not None,
      f"after {working.went_at - 1.0:.1f} s" if working.went_at else "hung")

# ------------------------------------------------------------------ the relay handshake
#
# A gate both sides reach is no use if neither can open a relay, and that was the next
# fault down. The join code is left in a SESSION PROPERTY, and the service allows exactly
# one player to write one: the session's host. Who allocates was decided somewhere else
# entirely -- by comparing the two player ids -- and the two answers agree only by luck.
#
# Mirrors RelayHandshake.Open, both ends, which is the only way a two-sided protocol can
# be tested at all.

PATIENCE = 20.0          # RelayHandshake.Patience: how long a joiner waits for the code
POLL = 0.5


class Session:
    """The meeting place. One host, and only the host may leave a code in it."""

    def __init__(s, host):
        s.host = host
        s.code = None
        s.refused = 0

    def write(s, who):
        if who != s.host:
            s.refused += 1
            raise PermissionError("only the host can do this")
        s.code = f"code-{who}"


class Device:
    """One phone opening the relay, as RelayHandshake.Open does it: three attempts,
    alternating ends, because believing one side of this over the other is how two
    devices wait for each other until both give up."""

    ATTEMPTS = 3

    def __init__(s, me, believes_host, self_correcting=True):
        s.me = me
        s.believes = believes_host
        s.correcting = self_correcting
        s.role = None            # the role it ended up taking
        s.ok = False
        s.at = None
        s.waited = 0.0
        s.attempt = 0
        s._as_host = believes_host

    def step(s, session, now):
        if s.ok or s.at is not None:
            return
        if s._as_host:
            try:
                session.write(s.me)
                s.ok, s.role, s.at = True, True, now
            except PermissionError:
                s._next(now)
            return

        if session.code is not None:
            s.ok, s.role, s.at = True, False, now
            return

        s.waited += POLL
        if s.waited >= PATIENCE:
            s._next(now)

    def _next(s, now):
        s.attempt += 1
        if not s.correcting or s.attempt >= s.ATTEMPTS:
            s.at = now                      # gave up
            return
        s._as_host = not s._as_host
        s.waited = 0.0


def run(session, a, b, limit=60.0):
    now = 0.0
    while now < limit and not (a.ok and b.ok) and not (a.at and b.at):
        a.step(session, now)
        b.step(session, now)
        now += POLL
    return now


print("\nthe relay handshake: who is allowed to leave the join code")

# The service made device 1 the host. The session is asked, so device 1 allocates.
s = Session(host=1)
a, b = Device(0, believes_host=False), Device(1, believes_host=True)
took = run(s, a, b)
check("asking the session, both ends open the relay", a.ok and b.ok,
      f"in {took:.1f} s, refused {s.refused} time(s)")
check("and they take opposite ends of it", a.role != b.role,
      f"device 0 host={a.role}, device 1 host={b.role}")
check("which is also which fighter each drives: the host is fighter one",
      (0 if b.role else 1) == 0 and (0 if a.role else 1) == 1)

# THE BUG. The old rule nominated the LOWER ID, and the service's host was the higher.
s = Session(host=1)
a = Device(0, believes_host=True, self_correcting=False)    # id comparison says "me"
b = Device(1, believes_host=False, self_correcting=False)   # so the real host waits
took = run(s, a, b)
check("the old rule deadlocks when the service chose the other host",
      not a.ok and not b.ok,
      f"refused {s.refused} time(s) and {took:.0f} s of waiting -- which is "
      f"'only the host can do this' on one phone and a lost connection on the other")

# And the session itself being wrong about both ends cannot deadlock either.
s = Session(host=1)
a, b = Device(0, believes_host=False), Device(1, believes_host=False)
took = run(s, a, b)
check("a session that calls both ends the guest still opens, after the patience",
      a.ok and b.ok, f"in {took:.1f} s")
check("and the end that took the host role is the one the service allows",
      b.role is True and a.role is False,
      f"device 0 host={a.role} (it was refused, waited, and read the code the real "
      f"host had finally left), device 1 host={b.role}")

print("\nALL PASS" if ok else "\nSOME FAILED")
sys.exit(0 if ok else 1)
