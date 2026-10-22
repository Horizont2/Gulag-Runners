"""Does direct-aim combat actually work?

docs/30 measured why the old fight played badly and docs/31 proposed this instead. The
proposal is only worth anything if it survives being run, so this runs it -- in the same
fixed-point arithmetic the game uses, over the real weapon table.

The questions, in the order they would sink the design:

  1. Is a carried blade harmless? If walking into somebody with the weapon out does
     damage, the whole game is a lawnmower and nothing else matters.
  2. Does a swing do damage, and does it scale with how hard it was swung?
  3. Is a heavy weapon still heavy, with no windup anywhere in the system?
  4. Does holding your weapon up stop theirs? That is the block, and it has no button.
  5. Is the time to kill anywhere near docs/02's 8 to 14 seconds of active fighting?
"""
import math
import sys

import arm_mirror as A
import fixgeom_mirror as F

PASS = FAIL = 0


def check(name, ok, detail=""):
    global PASS, FAIL
    if ok:
        PASS += 1
        print(f"  PASS  {name}  {detail}")
    else:
        FAIL += 1
        print(f"  FAIL  {name}  {detail}")


def aim_at(deg):
    r = math.radians(deg)
    return (F.from_f(math.cos(r)), F.from_f(math.sin(r)))


def swing_cycle(f, foe, ticks, hold=None, foe_aim=(0, 0)):
    """Throw the arm high/low through the horizontal, like the bot does."""
    hold = hold or A.WEAPONS[f.weapon][0]
    hits = 0
    high = True
    t = 0
    for i in range(ticks):
        if t <= 0:
            high = not high
            t = hold
        t -= 1
        up = aim_at(60 if high else -60)
        A.tick([f, foe], [up, foe_aim])
        if foe.was_hit:
            hits += 1
    return hits


def main():
    # ------------------------------------------------------- 1. the lawnmower
    print("1. a carried blade, which is the test the whole design dies on")
    worst = []
    for name in A.WEAPONS:
        att = A.Fighter(-1.0, 1, name)
        vic = A.Fighter(0.0, -1, "fists")

        # Get the arm out FIRST and let it stop. Raising it from the carry pose is a
        # real swing and is supposed to hurt; what must not work is walking at somebody
        # with it ALREADY out, which is the lawnmower.
        for _ in range(45):
            A.tick([att, vic], [(A.ONE, 0), (0, 0)])

        hit = False
        for _ in range(120):
            att.x += A.RUN_SPEED // 60
            A.tick([att, vic], [(A.ONE, 0), (0, 0)])
            if vic.was_hit:
                hit = True
                break
        worst.append((name, hit, att.swing_share))
    offenders = [w[0] for w in worst if w[1]]
    check("walking into somebody with the weapon out does nothing, for all 12",
          not offenders, f"offenders: {offenders or 'none'}")

    shares = {n: s for n, _, s in worst}
    print(f"        walk reaches {min(shares.values())}-{max(shares.values())} "
          f"permille of a swing; the floor is {A.CFG['min_swing']}")

    # ------------------------------------------------------- 2. a swing hurts
    print("\n2. a swing does damage, and more of it the harder it is swung")
    att = A.Fighter(-0.9, 1, "club")
    vic = A.Fighter(0.0, -1, "fists")
    hits = swing_cycle(att, vic, 180)
    check("swinging a club at somebody in reach lands blows", hits > 0,
          f"{hits} hits in 3 s, victim at {vic.health} hp")

    # Half speed: hold each end twice as long, so the arm crawls.
    att2 = A.Fighter(-0.9, 1, "club")
    vic2 = A.Fighter(0.0, -1, "fists")
    slow = swing_cycle(att2, vic2, 180, hold=A.WEAPONS["club"][0] * 4)
    check("and a lazier swing of the same weapon does less",
          (100 - vic2.health) < (100 - vic.health),
          f"slow {100-vic2.health} hp vs committed {100-vic.health} hp")

    # ------------------------------------------------------- 3. weight
    print("\n3. heavy weapons are still heavy, with no windup in the system")
    print("        weapon        sweep   top tip speed   dmg/landed blow")
    rows = []
    for name in ("dagger", "club", "sword", "axe", "warhammer"):
        f = A.Fighter(0, 1, name)
        frames = A.WEAPONS[name][0]
        top = F.to_f(f.top_tip_speed()) * 60.0
        rows.append((name, frames, top))
        print(f"        {name:<12} {frames:>3}f    {top:>6.1f} m/s     "
              f"{A.WEAPONS[name][1]:>5.1f}")
    check("a warhammer's arm is slower than a dagger's",
          rows[-1][2] < rows[0][2],
          f"{rows[-1][2]:.1f} m/s against {rows[0][2]:.1f}")
    check("and every weapon can still be swung fast enough to hurt",
          all(r[2] > 3.6 for r in rows),
          "all top speeds clear the 3.6 m/s a body can run at")

    # ------------------------------------------------------- 4. the block
    print("\n4. weapons stop weapons, which is the block and it has no button")
    att = A.Fighter(-0.9, 1, "club")
    vic = A.Fighter(0.0, -1, "club")
    clashes = hits = 0
    high = True
    t = 0
    for _ in range(300):
        if t <= 0:
            high = not high
            t = A.WEAPONS["club"][0]
        t -= 1
        # The defender simply holds their blade level, pointed at the attacker.
        A.tick([att, vic], [aim_at(60 if high else -60), (-A.ONE, 0)])
        if vic.was_hit:
            hits += 1
        if vic.was_clash:
            clashes += 1
    check("a held blade produces clashes", clashes > 0, f"{clashes} in 5 s")

    # The same fight with the defender NOT guarding. The first version of this left
    # their stick centred and called that open -- but the carry pose then pointed the
    # blade straight at the attacker, so "open" was holding a perfect guard too, and
    # both columns read 61. Here they look the other way, which is what open means.
    unguarded = A.Fighter(0.0, -1, "club")
    att2 = A.Fighter(-0.9, 1, "club")
    open_hits = swing_cycle(att2, unguarded, 300, foe_aim=(A.ONE, 0))
    check("and holding it up takes real damage away",
          (100 - vic.health) < (100 - unguarded.health),
          f"guarded lost {100-vic.health} hp, open lost {100-unguarded.health} hp")

    # ------------------------------------------------------- 5. time to kill
    print("\n5. time to kill, against docs/02's 8 to 14 seconds of active fighting")
    print("        weapon        seconds   hits")
    ttk = {}
    for name in A.WEAPONS:
        att = A.Fighter(-(0.45 + A.WEAPONS[name][2] * 0.5), 1, name)
        vic = A.Fighter(0.0, -1, "fists")
        hold = A.WEAPONS[name][0]
        high, t, hits, frames = True, 0, 0, 0
        while frames < 60 * 40 and not vic.dead:
            if t <= 0:
                high = not high
                t = hold
            t -= 1
            frames += 1
            A.tick([att, vic], [aim_at(60 if high else -60), (0, 0)])
            if vic.was_hit:
                hits += 1
        ttk[name] = (frames / 60.0 if vic.dead else None, hits)
        secs = f"{frames/60.0:>6.1f}" if vic.dead else "   n/a"
        print(f"        {name:<12} {secs}   {hits:>4}")

    killed = {k: v for k, v in ttk.items() if v[0] is not None}
    check("every weapon can actually kill", len(killed) == len(A.WEAPONS),
          f"{len(killed)} of {len(A.WEAPONS)}")
    if killed:
        lo = min(v[0] for v in killed.values())
        hi = max(v[0] for v in killed.values())
        check("and none of them does it instantly", lo >= 3.0, f"fastest {lo:.1f} s")
        check("nor takes all round about it", hi <= 25.0, f"slowest {hi:.1f} s")

    # ------------------------------------------------------- 6. one arc, one hit
    print("\n6. one arc is one hit, so a blade laid on somebody does not bill them")
    att = A.Fighter(-0.6, 1, "dagger")
    vic = A.Fighter(0.0, -1, "fists")

    # Let the arm travel from its carry pose to straight ahead first. Raising it IS a
    # swing and is supposed to hurt -- the question is what happens once it stops.
    for _ in range(60):
        A.tick([att, vic], [(A.ONE, 0), (0, 0)])
    settled = vic.health

    for _ in range(180):
        A.tick([att, vic], [(A.ONE, 0), (0, 0)])
    check("a blade held STILL inside somebody does nothing, however long it rests there",
          vic.health == settled,
          f"{settled} hp after the arm arrived, {vic.health} hp three seconds later")

    # ------------------------------------------------------- 7. desperation
    print("\n7. bare hands below a third of health, and on the last platform")

    def punch(hp, sudden):
        att = A.Fighter(-0.9, 1, "fists")
        att.health = hp
        vic = A.Fighter(0.0, -1, "fists")
        high, t = True, 0
        for _ in range(240):
            if t <= 0:
                high = not high
                t = A.WEAPONS["fists"][0]
            t -= 1
            A.tick([att, vic], [aim_at(60 if high else -60), (A.ONE, 0)], sudden)
            if vic.was_hit:
                return vic.damage_taken
        return 0

    healthy = punch(100, False)
    hurt = punch(25, False)
    last = punch(100, True)
    check("a desperate fighter's fists hit harder", hurt > healthy,
          f"{healthy} at full health, {hurt} at a quarter")
    check("and on the last platform they hurt anyway", last > healthy,
          f"{healthy} normally, {last} in sudden death")
    check("but the two do not stack into a one-hit kill",
          punch(25, True) == last,
          f"desperate AND sudden death is {punch(25, True)}, the larger of the two")

    # ------------------------------------------------------- 8. armour
    # Ported from combat_test, which this file replaces: the hit machinery past the
    # point of contact did not change, but it has to keep being checked somewhere.
    print("\n8. armour, which is the one stat docs/03 gives no downside")

    def hit_through(armour_permille):
        att = A.Fighter(-0.9, 1, "club")
        vic = A.Fighter(0.0, -1, "fists")
        vic.armour = armour_permille
        high, t = True, 0
        for _ in range(240):
            if t <= 0:
                high = not high
                t = A.WEAPONS["club"][0]
            t -= 1
            A.tick([att, vic], [aim_at(60 if high else -60), (A.ONE, 0)])
            if vic.was_hit:
                return vic.damage_taken
        return 0

    bare_hit = hit_through(0)
    kit_hit = hit_through(450)
    check("a full legendary kit takes most of a blow off",
          0 < kit_hit < bare_hit, f"{bare_hit} bare, {kit_hit} in 450 permille of armour")
    check("and the cap stops armour ever reaching zero damage",
          hit_through(900) >= 1, f"{hit_through(900)} through 900 permille")

    print()
    print(f"{PASS} passed, {FAIL} failed")
    print("ALL PASS" if FAIL == 0 else "SOME FAILED")
    return 1 if FAIL else 0


if __name__ == "__main__":
    sys.exit(main())
