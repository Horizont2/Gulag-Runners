"""What has to be true about the link, or two phones play two different matches.

Run: python3 lockstep_test.py

Every assertion here was written after breaking the thing it checks, and each one was
confirmed by putting the fault back and watching it fail. The faults, in order of how
long they would have taken to find in a build:

  * the ring slot that never let go          -- froze both devices after 4.3 seconds
  * a device answering its own kind of hello -- two phones greeting each other for ever
  * an input resampled while stalled         -- the two devices simulate different ticks
  * a late packet rewriting history          -- a tick already played changes underneath
  * stepping the fighters "mine first"       -- the shared tick runs in two orders
  * "Again" restarting on the spot           -- two arenas dealt on two different ticks
  * an empty slot reading as tick zero       -- the match starts before anyone has sent
"""

import sys

from lockstep_mirror import (
    CARRIED, RING, INPUT, WELCOME, End, Input, NOTHING, Rematch, Ring, handshake, packet,
    receive, step_order, suggested_delay, covers, worst_of, sent_ticks,
    Wait, hiccup, SUBSTITUTE_AFTER,
)

FAILED = []


def check(name, condition, detail=""):
    if condition:
        print(f"  ok    {name}")
    else:
        print(f"  FAIL  {name}{('  -- ' + detail) if detail else ''}")
        FAILED.append(name)


print("\n1. a slot that holds one tick must not hold it for ever")
print("   The ring is 256 ticks, a match is minutes, and every slot comes round again.")

ring = Ring()
for tick in range(RING + 40):
    ring.put(tick, Input(flags=tick & 7, x=tick, y=-tick))

late = RING + 12
check("a tick past the first lap is stored", ring.has(late))
check("and it is the input that was stored, not the one from 256 ticks ago",
      ring.get(late) == Input(flags=late & 7, x=late, y=-late),
      f"got {ring.get(late)}")

broken = Ring(wrap_bug=True)
for tick in range(RING + 40):
    broken.put(tick, Input(flags=tick & 7, x=tick, y=-tick))
check("the fault this replaces is caught",
      broken.get(late) != Input(flags=late & 7, x=late, y=-late),
      "a ring keyed on a bare 'have I got this' would have passed, which is the bug")


print("\n2. an input is frozen the moment it is sent")
print("   While the match is stalled the same tick is offered again every frame, and")
print("   the thumb has moved. The peer has already been told the first answer.")

ring = Ring()
ring.put(10, Input(flags=1, x=100, y=0))
ring.put(10, Input(flags=2, x=-100, y=0))       # the thumb moved during the stall
check("the second offer for a tick is refused",
      ring.get(10) == Input(flags=1, x=100, y=0), f"got {ring.get(10)}")
check("and the next tick is still free to take a new one",
      ring.put(11, Input(flags=2, x=-100, y=0)) and ring.get(11).x == -100)


print("\n3. redundancy covers a dropped packet without acknowledging anything")
print(f"   Every packet carries {CARRIED} inputs, so losing one costs nothing.")

sender = Ring()
for tick in range(48):
    sender.put(tick, Input(flags=tick & 3, x=tick * 7, y=tick))

receiver = Ring()
for tick in range(48):
    if tick % 3 != 0:                            # two packets in three arrive
        receive(receiver, packet(sender, tick))

# Only up to 40: the last few ticks are still in flight in any real run, and a test
# that demanded them would be asking the link to see the future.
missing = [t for t in range(40) if not receiver.has(t)]
check("every tick arrives all the same", not missing, f"missing {missing}")
check("and each one is the input that was sent",
      all(receiver.get(t) == sender.get(t) for t in range(40)))

worst = Ring()
receive(worst, packet(sender, 39))               # one packet, nothing else
covered = [t for t in range(48) if worst.has(t)]
check(f"one packet alone covers the last {CARRIED} ticks",
      len(covered) == CARRIED and covered[0] == 40 - CARRIED,
      f"covered {covered}")


print("\n4. a late packet must not rewrite a tick already played")
print("   UDP reorders. A simulation that has run on an input cannot be given another.")

ring = Ring()
receive(ring, packet(sender, 30))
before = [ring.get(t) for t in range(20, 31)]

stale = Ring()
for tick in range(40):
    stale.put(tick, Input(flags=9, x=-1, y=-1))  # a different, wrong history
receive(ring, packet(stale, 28))                 # ...arriving after the newer packet

after = [ring.get(t) for t in range(20, 31)]
check("the ticks already held are untouched", before == after)

print("\n   And a slot that has moved on to a newer tick is not dragged back.")
ring = Ring()
ring.put(RING + 5, Input(x=1))                   # slot 5 now holds tick 261
ring.put(5, Input(x=2))                          # tick 5 arrives absurdly late
check("the newer tick keeps its slot", ring.get(RING + 5) == Input(x=1))
check("and the ancient one is not readable as current", not ring.has(5))


print("\n5. the handshake ends")
print("   A guest that answers a hello with a hello is two phones greeting each other")
print("   until the battery runs out. Only a host answers.")

host, guest = End("host", seed=0xC0FFEE), End("guest")
crossed = handshake(host, guest)
check("both ends are connected", host.connected and guest.connected)
check("the guest is dealt from the host's number", guest.seed == host.seed,
      f"host {host.seed:X}, guest {guest.seed:X}")
check("and it took a handful of packets, not a stream", crossed <= 4, f"{crossed} packets")

storm_host = End("host", seed=1, echo_hello=True)
storm_guest = End("guest", echo_hello=True)
storm = handshake(storm_host, storm_guest, rounds=8)
check("the fault this replaces is caught", storm > 4,
      "answering a hello with a hello would have passed; it never stops")

print("\n   A guest is never 'connected' without the number the match is dealt from.")
guest = End("guest")
guest.take(INPUT, None)
check("input alone does not start the guest", not guest.connected and guest.seed == 0,
      "the seed is only in the welcome; starting without it deals a different arena")
host = End("host", seed=7)
host.take(INPUT, None)
check("but it does start the host, which has the seed already", host.connected)

loose = End("guest")
loose.seed_only_from_welcome = False
loose.take(INPUT, None)
check("the fault this replaces is caught", loose.connected and loose.seed == 0,
      "connected, dealing from zero, against a host dealing from its own number")

print("\n   A welcome that arrives twice does not re-deal the match.")
guest = End("guest")
guest.take(WELCOME, 111)
guest.take(WELCOME, 222)
check("the second welcome is ignored", guest.seed == 111, f"seed {guest.seed}")


print("\n6. both devices step the fighters in the same order")
print("   The shared work inside a tick -- the loot, the hits -- runs once, from the")
print("   first fighter. Two devices stepping 'mine first' run it in two orders.")

on_host = step_order(0)
on_guest = step_order(1)
check("fighter one gets the host's input on both devices",
      on_host[0] == "mine" and on_guest[0] == "theirs")
check("fighter two gets the guest's input on both devices",
      on_host[1] == "theirs" and on_guest[1] == "mine")

mine_first_host = ("mine", "theirs")
mine_first_guest = ("mine", "theirs")
check("the fault this replaces is caught",
      mine_first_host == mine_first_guest and on_host != on_guest,
      "stepping 'mine first' on both devices swaps who is fighter one")


print("\n7. an empty ring does not answer for tick zero")
print("   Slots start empty, and empty must not read as 'I have tick 0, it was nothing'")
print("   or the match runs its first tick before the other player has sent anything.")

ring = Ring()
check("tick zero is not held by an untouched ring", not ring.has(0))
check("and reading it anyway gives nothing, not a stale input", ring.get(0) == NOTHING)


print("\n8. \"Again\" happens on a tick, not at a moment")
print("   Two phones restarting when each player's finger lands deal two arenas.")


def run_rematch(host, guest, tap_on, ticks=120, lag=3):
    """Run both devices through the same ticks; return the tick each dealt on."""
    dealt = {"host": [], "guest": []}
    post = []                                     # (arrive_at, to, when, seed)
    for now in range(ticks):
        if now == tap_on:
            guest.ask(now)
        for end, other in (("host", guest), ("guest", host)):
            source = host if end == "host" else guest
            for when, seed in source.outbox:
                post.append((now + lag, other, when, seed))
            source.outbox = []
        for item in [p for p in post if p[0] == now]:
            _at, who, when, seed = item
            who.take(when, seed, now)
        if host.tick(now):
            dealt["host"].append(now)
        if guest.tick(now):
            dealt["guest"].append(now)
    return dealt


where = run_rematch(Rematch("host"), Rematch("guest"), tap_on=20)
check("both devices deal on exactly one tick",
      len(where["host"]) == 1 and len(where["guest"]) == 1, str(where))
check("and it is the SAME tick", where["host"] == where["guest"], str(where))

broken = run_rematch(Rematch("host", now_bug=True), Rematch("guest", now_bug=True),
                     tap_on=20)
check("the fault this replaces is caught",
      broken["host"] != broken["guest"], f"restarting on the spot gave {broken}")

print("\n   A packet re-sent because the first may have been lost is not a second match.")
guest = Rematch("guest")
guest.take(50, 0xABCD, 10)
fired = [t for t in range(60) if guest.tick(t)]
guest.take(50, 0xABCD, 55)                        # the host's re-send, arriving late
fired += [t for t in range(55, 80) if guest.tick(t)]
check("one rematch, one deal", fired == [50], str(fired))

print("\n   The host decides when, even when it was the guest who asked.")
host, guest = Rematch("host"), Rematch("guest")
guest.ask(now=10)
check("a guest's tap goes out as a request, not a time",
      guest.outbox == [(-1, 0)] and guest.at == -1, str(guest.outbox))
host.take(-1, 0, now=13)
check("and the host turns it into a tick ahead of itself",
      host.at == 13 + Rematch("host").lead, str(host.at))

print("\n   And a second tap while one is pending does not stack.")
host = Rematch("host")
host.ask(now=5)
first = host.at
host.ask(now=6)
check("still one rematch, at the first time named", host.at == first, str(host.at))


print("\n9. the input budget is measured, not assumed")
print("   Lockstep waits for the other player. A budget smaller than the trip means")
print("   waiting on EVERY tick, which is the whole difference between a LAN and not.")

print(f"   {'round trip':>12}  {'budget':>7}  {'covers it':>10}")
for rtt in (2, 20, 60, 100, 140, 200, 400):
    d = suggested_delay(rtt)
    print(f"   {rtt:>9} ms  {d:>4} tk  {'yes' if covers(rtt, d) else 'NO':>10}")

check("a LAN is left exactly as it was", suggested_delay(2) == 4 and suggested_delay(20) == 4,
      "two milliseconds must not change a number that already works")
check("and nothing below the floor is ever asked for",
      all(suggested_delay(r) >= 4 for r in range(0, 500)))

short = [r for r in range(1, 300) if not covers(r, suggested_delay(r))]
check("every trip up to the ceiling is actually covered", not short,
      f"not covered: {short[:4]}")

check("a trip too slow for lockstep is capped, not chased",
      suggested_delay(1000) == 12,
      "past about 200 ms the controls are further away than anyone will accept, and "
      "the honest answer is that the link is too slow for this")
check("the fault this replaces is caught", not covers(200, 4),
      "a fixed 4 ticks against a 200 ms trip is a stall on every tick")

print("\n   Judged on the worst of the last few, because one late input stalls a tick.")
check("a single bad trip moves the budget", worst_of([20, 20, 20, 180]) == 180)
check("and the average would have hidden it", sum([20, 20, 20, 180]) / 4 < 180)
check("but it ages out once the link settles",
      worst_of([180] + [20] * 8) == 20, "eight good samples clear one bad one")

print("\n   And the tick already sent is never un-sent when the budget shrinks.")
sent = sent_ticks([8, 8, 4, 4, 4, 4, 4, 4, 4, 4])
nums = [t for _kind, t in sent]
check("the furthest tick sent only ever goes forward",
      all(b >= a for a, b in zip(nums, nums[1:])), str(nums))
check("a shrinking budget re-sends instead of rewriting history",
      sent[2][0] == "again" and sent[3][0] == "again", str(sent[:5]))
check("and the clock catches up to it rather than stalling for ever",
      sent[-1][0] == "new", str(sent[-3:]))


print("\n10. when the other player stops answering, a bot takes their fighter")
print("   Lockstep cannot step without their input: the wait freezes the WHOLE match,")
print("   so the only question is how long before it stops needing them.")

check("a quarter second of loss costs nobody their match", not hiccup(0.25),
      "every packet already carries sixteen past inputs -- that much is routine")
check("nor does a second and a half", not hiccup(1.5))
check("three seconds of total silence is an absent player", hiccup(3.0))

w = Wait()
w.frame(1 / 60.0, heard=False)
check("and the wait is reported from the first frame of it", w.silent > 0 and not w.substituted)

w = Wait()
for _ in range(600):
    w.frame(1 / 60.0, heard=False)
check("once a bot has the fighter, the match runs again", w.substituted)
before = w.ticks
for _ in range(60):
    w.frame(1 / 60.0, heard=False)
check("and never stalls again, however silent the wire", w.ticks == before + 60,
      "the bot supplies the input now; there is nothing left to wait for")

check("a late packet does not un-substitute", w.frame(1 / 60.0, heard=True) == "bot",
      "the moment this device stepped a tick the other did not, the two had parted")

print("\n   The patience is a real choice and the test says what it costs.")
for seconds in (0.5, 1.0, 2.5, 5.0):
    worst = [g for g in (0.3, 0.8, 1.2, 2.0, 4.0) if hiccup(g, patience=seconds)]
    print(f"      {seconds:>4}s patience -> ejects a player after a "
          f"{min(worst) if worst else '>4'}s hiccup")
check("the shipped patience survives a one-second hiccup",
      not hiccup(1.0, patience=SUBSTITUTE_AFTER))

print()
if FAILED:
    print(f"lockstep_test: {len(FAILED)} FAILED -- " + ", ".join(FAILED))
    sys.exit(1)
print("lockstep_test: all assertions hold")
