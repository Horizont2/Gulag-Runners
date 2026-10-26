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


def stand_off(weapon, share=0.75):
    """Where to stand so this weapon's arc arrives, as a share of its own reach.

    The distances in this file were plain metres for a while -- 0.9 for a club, 0.6 for a
    dagger -- picked by hand while the mirror modelled a 1.8 m fighter. The fighter in the
    prefabs is 0.91 m, so each of them was about twice the right number, and most of the
    file was measuring two men standing inside each other. Everything in combat is a share
    of the fighter's own size now, and so is this.
    """
    f = A.Fighter(0, 1, weapon)
    return (F.to_f(f.arm_length() + f.blade()) * share
            + F.to_f(f.body_radius()))


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
        att = A.Fighter(-(stand_off(name) + 0.5), 1, name)
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
    att = A.Fighter(-stand_off("club"), 1, "club")
    vic = A.Fighter(0.0, -1, "fists")
    hits = swing_cycle(att, vic, 180)
    check("swinging a club at somebody in reach lands blows", hits > 0,
          f"{hits} hits in 3 s, victim at {vic.health} hp")

    # Half speed: hold each end twice as long, so the arm crawls.
    att2 = A.Fighter(-stand_off("club"), 1, "club")
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
    run = F.to_f(A.RUN_SPEED)
    check("and every weapon can still be swung fast enough to hurt",
          all(r[2] > run for r in rows),
          f"all top speeds clear the {run} m/s a body can run at")

    # ------------------------------------------------------- 4. the block
    print("\n4. weapons stop weapons, which is the block and it has no button")
    att = A.Fighter(-stand_off("club"), 1, "club")
    vic = A.Fighter(0.0, -1, "club")
    clashes = hits = 0
    high = True
    t = 0
    for _ in range(300):
        if t <= 0:
            high = not high
            t = A.WEAPONS["club"][0]
        t -= 1
        # The defender holds their blade at the attacker. Forward on the stick MEANS
        # at him now (docs/31's aim frame), so this is +X whichever side he is on --
        # which is the whole point of the frame.
        A.tick([att, vic], [aim_at(60 if high else -60), (A.ONE, 0)])
        if vic.was_hit:
            hits += 1
        if vic.was_clash:
            clashes += 1
    check("a held blade produces clashes", clashes > 0, f"{clashes} in 5 s")

    # Measured as TIME TO KILL, not damage over a fixed number of ticks, and the
    # difference is not pedantry. Hit stop freezes both fighters, so a defender who is
    # being hit a lot spends real seconds frozen -- over a fixed tick count that LOOKS
    # like taking less damage, and the first version of this test read exactly backwards
    # because of it: guarding came out worse than standing there.
    #
    # What is asserted here used to be a NAMED ANGLE: hold the blade at the end of the
    # arc, where it dwells and is slow, rather than through the fast middle. That was
    # measured and it was real -- on the 1.8 m fighter the mirror used to model. At the
    # size the game actually ships, with the blade the model actually has, the winning
    # angle is a different one and moves with the arc, so naming one is fitting a test to
    # a number rather than to the design.
    #
    # The design's two claims survive the change, and they are what is pinned instead:
    # a guard is worth holding, and no guard is safe against everything.
    def ticks_to_kill(guard, lo=-60, hi=60):
        att = A.Fighter(-stand_off("club"), 1, "club")
        vic = A.Fighter(0.0, -1, "club")
        high, t, n = True, 0, 0
        while n < 60 * 40 and not vic.dead:
            if t <= 0:
                high = not high
                t = A.WEAPONS["club"][0]
            t -= 1
            n += 1
            A.tick([att, vic], [aim_at(hi if high else lo), guard])
        return n if vic.dead else None

    ARCS = [(-60, 60), (-30, 30), (-45, 45), (0, 60), (-20, 70)]
    GUARDS = list(range(-90, 91, 15))
    ALIVE = 10 ** 9                       # survived the forty seconds

    grid = {}
    for lo, hi in ARCS:
        for g in GUARDS:
            grid[(lo, hi, g)] = ticks_to_kill(aim_at(g), lo, hi) or ALIVE

    # 1. There is always SOME guard that greatly outlasts standing open. Note that
    #    standing open is not standing defenceless: the carry pose is a lowered blade and
    #    it catches part of a low swing by itself, which is why the margin is measured
    #    against it rather than against a man with no arms.
    worst = None
    for lo, hi in ARCS:
        open_n = ticks_to_kill((0, 0), lo, hi) or ALIVE
        best = max(grid[(lo, hi, g)] for g in GUARDS)
        ratio = best / float(open_n)
        if worst is None or ratio < worst[0]:
            worst = (ratio, lo, hi, open_n, best)
    check("holding the blade in the way is worth doing, against every arc",
          worst[0] > 1.1,
          f"the arc it helps least against is {worst[1]} to {worst[2]}: "
          f"{worst[3]} ticks open against "
          f"{'past 40 s' if worst[4] >= ALIVE else str(worst[4]) + ' guarded'}")

    # 2. And no single angle is safe: whichever one the defender picks, some arc goes
    #    round it. A guard that answered everything would be a block button with extra
    #    steps, which is the thing docs/31 took out.
    safest = max(min(grid[(lo, hi, g)] for lo, hi in ARCS) for g in GUARDS)
    check("but no one angle answers every arc", safest < ALIVE,
          f"the best angle to be caught holding still dies in {safest} ticks")

    # ------------------------------------------------------- 5. time to kill
    print("\n5. time to kill, against docs/02's 8 to 14 seconds of active fighting")
    print("        weapon        seconds   hits")
    ttk = {}
    for name in A.WEAPONS:
        att = A.Fighter(-stand_off(name, 0.7), 1, name)
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
    att = A.Fighter(-stand_off("dagger", 0.6), 1, "dagger")
    vic = A.Fighter(0.0, -1, "fists")

    # Let the arm travel from its carry pose to straight ahead first. Raising it IS a
    # swing and is supposed to hurt -- the question is what happens once it stops.
    for _ in range(60):
        A.tick([att, vic], [(A.ONE, 0), (0, 0)])
    settled = vic.health

    for _ in range(180):
        A.tick([att, vic], [(A.ONE, 0), (0, 0)])
    # And one STROKE is one hit, however long the stroke takes. The rate floor cannot
    # do this on its own: a warhammer dragged across somebody over three seconds spends
    # far longer inside them than its own lock, so without "one hit per swing" the same
    # single stroke bills twice.
    def bills(weapon, secs, per_swing=True):
        att = A.Fighter(-stand_off(weapon), 1, weapon)
        vic = A.Fighter(0.0, -1, "fists")
        for _ in range(40):
            A.step(att, aim_at(-80), vic)
        ticks, hits = int(secs * 60), 0
        for t in range(ticks):
            vic.health, vic.dead = 100, False
            if not per_swing:
                att.spent_on = -1
            A.tick([att, vic], [aim_at(-80 + 160 * t / ticks), (0, 0)])
            if vic.was_hit:
                hits += 1
        return hits

    slow = bills("warhammer", 3.0)
    check("and one STROKE is one hit, however slowly it is dragged across",
          slow <= 1,
          f"a three-second warhammer stroke billed {slow} time(s); "
          f"{bills('warhammer', 3.0, False)} without the rule")

    check("a blade held STILL inside somebody does nothing, however long it rests there",
          vic.health == settled,
          f"{settled} hp after the arm arrived, {vic.health} hp three seconds later")

    # ------------------------------------------------------- 7. desperation
    print("\n7. bare hands below a third of health, and on the last platform")

    def punch(hp, sudden):
        att = A.Fighter(-stand_off("fists", 0.6), 1, "fists")
        att.health = hp
        vic = A.Fighter(0.0, -1, "fists")
        high, t = True, 0
        for _ in range(240):
            if t <= 0:
                high = not high
                t = A.WEAPONS["fists"][0]
            t -= 1
            # Centred, NOT (1, 0). Forward on the stick means AT HIM in the aim frame,
            # so the victim was being handed a perfect guard in a test about damage.
            A.tick([att, vic], [aim_at(60 if high else -60), (0, 0)], sudden)
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

    # ------------------------------------------------------- 7b. the stick
    print("\n7b. the arm answers the stick, and only the stick")

    def swept(ticks, span_deg):
        """How far the arm travels while the thumb slides smoothly across span_deg."""
        f = A.Fighter(0, 1, "club")
        foe = A.Fighter(2.0, -1, "club")
        travelled, last = 0.0, None
        for t in range(ticks):
            deg = -span_deg / 2 + span_deg * t / float(ticks)
            A.step(f, aim_at(deg), foe)
            ang = math.degrees(math.atan2(F.to_f(f.arm[1]), F.to_f(f.arm[0])))
            if last is not None:
                travelled += abs(ang - last)
            last = ang
        return travelled

    # A thumb sliding round the ring moves about a hundredth of the stick per tick, and
    # the frame used to compare tick to tick -- so it never counted as movement, and the
    # arm stopped answering the stick entirely for as long as you moved it smoothly.
    # This is that bug, pinned.
    slow = swept(90, 180)
    check("a slowly sliding thumb moves the arm", slow > 90,
          f"thumb swept 180 deg over 1.5 s, arm travelled {slow:.0f} deg")

    # And the other half: a stick held still must NOT walk the arm round, or closing on
    # somebody is an attack by itself.
    held = A.Fighter(-2.0, 1, "club")
    foe = A.Fighter(0.0, -1, "club")

    # Let the arm ARRIVE first. A fighter spawns in the carry pose, so the first few
    # ticks of a held stick are the arm travelling from carry to where it was told to
    # go -- which is the arm obeying, not the frame drifting, and measuring from tick
    # one counts it as 23 degrees of drift.
    for _ in range(20):
        A.step(held, (A.ONE, 0), foe)

    first = None
    drift = 0.0
    for t in range(120):
        held.x += A.RUN_SPEED // 60
        A.step(held, (A.ONE, 0), foe)
        ang = math.degrees(math.atan2(F.to_f(held.arm[1]), F.to_f(held.arm[0])))
        if first is None:
            first = ang
        drift = max(drift, abs(ang - first))
    check("and a stick held still does not walk the arm round while you close",
          drift < 20, f"arm drifted {drift:.1f} deg over two seconds of walking in")

    # ------------------------------------------------------- 8. armour
    # Ported from combat_test, which this file replaces: the hit machinery past the
    # point of contact did not change, but it has to keep being checked somewhere.
    print("\n8. armour, which is the one stat docs/03 gives no downside")

    def hit_through(armour_permille):
        att = A.Fighter(-stand_off("club"), 1, "club")
        vic = A.Fighter(0.0, -1, "fists")
        vic.armour = armour_permille
        high, t = True, 0
        for _ in range(240):
            if t <= 0:
                high = not high
                t = A.WEAPONS["club"][0]
            t -= 1
            A.tick([att, vic], [aim_at(60 if high else -60), (0, 0)])
            if vic.was_hit:
                return vic.damage_taken
        return 0

    bare_hit = hit_through(0)
    kit_hit = hit_through(450)
    check("a full legendary kit takes most of a blow off",
          0 < kit_hit < bare_hit, f"{bare_hit} bare, {kit_hit} in 450 permille of armour")
    check("and the cap stops armour ever reaching zero damage",
          hit_through(900) >= 1, f"{hit_through(900)} through 900 permille")

    # --------------------------------------------- 9. the blade is the model
    #
    # The hitbox has to be the weapon you can see, and for a long time it was not. The
    # simulation ran the item's whole Reach out from the hand, which came to about 1.5x
    # the drawn model on all twelve weapons and gave bare fists 0.60 m of steel out of an
    # empty hand. Nothing caught it, because every OTHER test placed its fighters in
    # metres picked to suit whatever the geometry happened to be.
    #
    # Reach is not a distance any more and should not be read as one. It is measured from
    # the middle of the body, so it already contains half a torso and an arm; what it
    # still does, and all it does, is RANK the weapons.
    print("\n9. the blade the sim swings is the blade that gets drawn")

    blades = {n: F.to_f(A.Fighter(0, 1, n).blade()) for n in A.WEAPONS}
    height = F.to_f(A.BODY_HEIGHT)

    check("bare hands have no blade -- the fist is the weapon",
          blades["fists"] == 0,
          f"{blades['fists']:.2f} m of steel out of an empty hand")

    longer = [n for n, b in blades.items() if b >= A.WEAPONS[n][2]]
    check("and no blade is as long as the reach its item claims",
          not longer,
          f"reach carries half a torso and an arm; offenders: {longer or 'none'}")

    armed = sorted((A.WEAPONS[n][2], n) for n in A.WEAPONS if blades[n] > 0)
    ordered = all(blades[armed[i][1]] <= blades[armed[i + 1][1]]
                  for i in range(len(armed) - 1))
    check("but reach still ranks them, which is the one job it has left",
          ordered,
          f"shortest {armed[0][1]} at {blades[armed[0][1]]:.2f} m, "
          f"longest {armed[-1][1]} at {blades[armed[-1][1]]:.2f} m")

    overgrown = [n for n, b in blades.items()
                 if b > height * A.CFG["max_blade_pm"] / 1000.0 + 0.001]
    check("and the cap holds, so nobody swings a flagpole",
          not overgrown,
          f"ceiling is {height * A.CFG['max_blade_pm'] / 1000.0:.2f} m on a "
          f"{height:.2f} m fighter; over it: {overgrown or 'none'}")

    # --------------------------- 9b. how much of a fight the player is allowed to play
    #
    # Hit stop freezes BOTH fighters and hit stun freezes the one who was hit. Each rule
    # is right on its own and nobody had added them up over a real duel, which is where
    # the answer lives: a fighter who spends most of the match unable to act is playing
    # a game that does not answer, however good each rule is by itself.
    #
    # It was 72% for bare fists. `victim.HitLock = 0` on being hit read as the opposite
    # of its own comment -- it CLEARED the one-hit-per-arc cooldown instead of spending
    # it -- so each hit armed the other fighter's next one. Two blades crossed at fist
    # range made that a loop: 11.8 hits a second, four frames of freeze each, on both of
    # them. This is that, pinned, and pinned by the symptom rather than by the line.
    print("\n9b. a fight the player is allowed to play")

    def frozen_share(weapon, offset=7):
        att = A.Fighter(-stand_off(weapon), 1, weapon)
        vic = A.Fighter(0.0, -1, weapon)
        ta = tb = 0
        ha = hb = True
        n = stopped = hits = 0
        while n < 60 * 40 and not (att.dead or vic.dead):
            if ta <= 0:
                ha = not ha
                ta = A.WEAPONS[weapon][0]
            if n >= offset and tb <= 0:
                hb = not hb
                tb = A.WEAPONS[weapon][0]
            ta -= 1
            tb -= 1
            n += 1
            A.tick([att, vic], [aim_at(60 if ha else -60), aim_at(60 if hb else -60)])
            hits += (1 if att.was_hit else 0) + (1 if vic.was_hit else 0)
            if att.hitstop > 0:
                stopped += 1
        return stopped / float(n), hits / (n / 60.0)

    print("        weapon        frozen   hits/s")
    worst_frozen = worst_rate = 0.0
    worst_name = ""
    for name in ("fists", "dagger", "club", "sword", "axe", "spear", "warhammer"):
        share, rate = frozen_share(name)
        print(f"        {name:<12}{share:>7.0%}{rate:>9.1f}")
        if share > worst_frozen:
            worst_frozen, worst_rate, worst_name = share, rate, name

    check("no weapon freezes the fight for most of it",
          worst_frozen < 0.30,
          f"worst is {worst_name} at {worst_frozen:.0%} of the match frozen")
    check("and nothing lands fast enough to be a loop rather than a fight",
          worst_rate < 5.0,
          f"worst is {worst_name} at {worst_rate:.1f} hits a second")

    # ------------------------- 9c. swinging has to beat vibrating the thumb
    #
    # The one that decides whether there is a game here. Damage used to be the tip's
    # INSTANTANEOUS speed, and instantaneous speed is maximised by high frequency and
    # low amplitude: a thumb shaken over a 30-degree sliver never spends a tick
    # accelerating or dwelling at the end of an arc, so it sits at the weapon's top
    # speed permanently. Measured against a real swing, per weapon, damage a second:
    #
    #     club 20.0 vs 16.0    sword 25.0 vs 16.0    warhammer 26.2 vs 18.9
    #
    # The vibration won everywhere, and by the most on the warhammer -- the one weapon
    # the whole design says you cannot flick. A player finds that in ten seconds and
    # then there is nothing left to play.
    #
    # So a blow is worth speed TIMES COMMITMENT: how far this swing has travelled in
    # one direction since it last reversed. A vibration goes nowhere and is worth
    # nothing; the first part of every swing is harmless, which is a windup the player
    # performs with their own thumb and the other fighter can see.
    print("\n9c. a swing is worth more than a shake")

    def rate(motion, weapon, ticks=60 * 20):
        """Damage a second against a dummy that cannot die."""
        att = A.Fighter(-stand_off(weapon), 1, weapon)
        vic = A.Fighter(0.0, -1, "fists")
        dealt = 0
        for t in range(ticks):
            vic.health, vic.dead = 100, False
            A.tick([att, vic], [motion(t, weapon), (0, 0)])
            dealt += vic.damage_taken
        return dealt / (ticks / 60.0)

    def shake(span, period):
        return lambda t, w: aim_at(span if (t // period) % 2 else -span)

    def swing(t, w):
        return aim_at(60 if (t // A.WEAPONS[w][0]) % 2 else -60)

    print("        weapon        swing    best shake")
    beaten = []
    for name in ("fists", "club", "sword", "axe", "spear", "warhammer"):
        good = rate(swing, name)
        bad = max(rate(shake(30, 3), name), rate(shake(60, 4), name),
                  rate(shake(20, 2), name), rate(shake(45, 5), name))
        print(f"        {name:<12}{good:>7.1f}{bad:>12.1f}")
        if bad > good * 1.15:
            beaten.append(f"{name} ({bad:.1f} against {good:.1f})")

    check("a committed swing beats a vibrating thumb, on every weapon",
          not beaten, "beaten on: " + (", ".join(beaten) or "none"))

    # And the heavy end especially, because that is where it was worst.
    heavy = rate(swing, "warhammer")
    heavy_shake = max(rate(shake(30, 3), "warhammer"), rate(shake(60, 4), "warhammer"))
    check("and a warhammer cannot be flicked at all",
          heavy_shake < heavy * 0.5,
          f"shaking a warhammer earns {heavy_shake:.1f} against {heavy:.1f} for swinging it")

    # ------------------------------------------ 10. the stick means what it says
    #
    # docs/31 promises four things about the attack stick: forward is at him, up is over
    # him, down is under him, back is away. The frame used to be measured from MY
    # SHOULDER to HIS CHEST -- the same anatomical point on nobody -- which on flat
    # ground is a quarter of a metre downhill whatever the distance. Pushing right put
    # the arm 17 degrees down at sparring range, 40 at half a metre, and STRAIGHT DOWN
    # when the two of you touched, because at zero gap the only direction left between a
    # shoulder and a chest is the drop between them. Up and down swapped places as you
    # walked in and nothing in this file noticed.
    print("\n10. forward on the stick is at him, from any distance and either side")

    def points(stick, gap, lift=0.0, side=1):
        me = A.Fighter(0.0, side, "club")
        foe = A.Fighter(gap * side, -side, "club")
        foe.y = F.from_f(lift)
        got = A.assist(me, stick, foe)
        return math.degrees(math.atan2(F.to_f(got[1]), F.to_f(got[0])))

    GAPS = (1.5, 1.0, 0.7, 0.5, 0.3, 0.15, 0.0)

    def worst(stick, wanted, side=1):
        """Furthest any gap lands from the angle the player asked for."""
        off = [(g, abs(((points(stick, g, side=side) - wanted) + 180) % 360 - 180))
               for g in GAPS]
        return max(off, key=lambda p: p[1])

    g, miss = worst((A.ONE, 0), 0.0)
    check("forward points at him, whatever the range", miss < 1.0,
          f"worst is {miss:.1f} deg out, at {g} m")

    g, miss = worst((A.ONE, 0), 180.0, side=-1)
    check("and it still does with him on the other side", miss < 1.0,
          f"worst is {miss:.1f} deg out, at {g} m")

    g_up, up = worst((0, A.ONE), 90.0)
    g_dn, dn = worst((0, -A.ONE), -90.0)
    check("up is over him and down is under him, and they do not trade places",
          up < 1.0 and dn < 1.0,
          f"up {up:.1f} deg out at {g_up} m, down {dn:.1f} deg out at {g_dn} m")

    # A fighter on a ledge directly overhead is the case the floor and the cap are for:
    # without them the line to him goes vertical and the whole stick turns a quarter.
    overhead = abs(points((A.ONE, 0), 0.05, lift=1.2))
    cap = math.degrees(math.atan(A.CFG["aim_tilt_pm"] / 1000.0))
    # Against 60 degrees as well as against the cap, deliberately. Checked only against
    # the cap, loosening the cap loosens the test with it and the assertion says nothing
    # -- 60 is the point past which up and down stop being usable directions and is a
    # claim about the design rather than about the number.
    check("and a man on a ledge overhead cannot turn the stick under your thumb",
          overhead <= min(cap, 60.0) + 1.0,
          f"frame leaned {overhead:.0f} deg, cap {cap:.0f}, and 60 is the most that is "
          "still a usable stick")

    # The arm turns at the rate the weapon promises, IN EVERY DIRECTION.
    #
    # It did not. The slew interpolated the CHORD between where the arm is and where the
    # stick points, and a chord is shorter than its arc by more and more as the angle
    # grows. Asked to turn exactly 180 degrees, "toward the target" points straight back
    # down the arm, so the step shortened the arm instead of turning it and normalising
    # put it back exactly where it started: the arm froze, permanently. Around that was a
    # dead cone about 25 degrees wide where it crawled.
    #
    # Which is the most natural attack in the game -- arm out one way, flick the stick
    # the other way to swing back through him -- being the slowest input there is, and
    # the straightest version of it doing nothing at all.
    def ticks_to_reach(deg_target, weapon="club"):
        f = A.Fighter(0, 1, weapon)
        foe = A.Fighter(2.0, -1, weapon)
        for _ in range(40):
            A.step(f, aim_at(0), foe)          # settle pointing forward
        for t in range(120):
            A.step(f, aim_at(deg_target), foe)
            now = math.degrees(math.atan2(F.to_f(f.arm[1]), F.to_f(f.arm[0])))
            if abs(((now - deg_target) + 180) % 360 - 180) < 3:
                return t + 1
        return None

    arc = A.WEAPONS["club"][0]                  # a club sweeps 180 deg in this many ticks
    reached = {d: ticks_to_reach(d) for d in (45, 90, 120, 170, 175, 179, 180)}
    print("        flick to   ticks taken   (a club sweeps 180 deg in "
          f"{arc})")
    for d, n in reached.items():
        print(f"        {d:>8}   {str(n) if n else 'NEVER ARRIVES':>11}")

    check("the arm reaches every angle, the far side included",
          all(n is not None for n in reached.values()),
          "stuck at: " + (", ".join(f"{d} deg" for d, n in reached.items()
                                    if n is None) or "nothing"))

    # And at the promised RATE, not just eventually: the time to turn must grow with the
    # angle and nowhere blow out. 180 degrees is one full arc, so it may not take much
    # more than one arc's worth of ticks.
    far = reached.get(180)
    check("and it takes one arc to turn a half circle, not ten",
          far is not None and far <= arc + 2,
          f"180 deg took {far} ticks against the {arc} the weapon promises")

    # And the pose a fighter who has touched nothing is in.
    fresh = A.Fighter(0, 1, "club")
    rest = math.degrees(math.atan2(F.to_f(fresh.arm[1]), F.to_f(fresh.arm[0])))
    check("a fighter spawns with the blade carried low, not held level",
          rest < -5.0, f"arm starts at {rest:.0f} deg")

    print()
    print(f"{PASS} passed, {FAIL} failed")
    print("ALL PASS" if FAIL == 0 else "SOME FAILED")
    return 1 if FAIL else 0


if __name__ == "__main__":
    sys.exit(main())
