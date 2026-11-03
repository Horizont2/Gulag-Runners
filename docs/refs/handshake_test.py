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

    def __init__(self, loads_at, keep_announcing=True):
        self.loads_at = loads_at
        self.keep = keep_announcing
        self.loaded = False
        self.heard = False
        self.loaded_at = None
        self.next_say = 0.0
        self.went_at = None

    def ready(self, now):
        if self.heard:
            return True
        return (self.loaded and TIMEOUT > 0
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

print("\nALL PASS" if ok else "\nSOME FAILED")
sys.exit(0 if ok else 1)
