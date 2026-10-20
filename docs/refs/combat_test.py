"""Checks the fight of docs/02 — above all the one number the rest is tuned against:
a time to kill of 8 to 14 seconds of ACTIVE fighting."""
import sim_motor_mirror as M
import combat_mirror as K
from sim_motor_mirror import S, step, X, F, L, R, UP, DN, JMP, DOD, ATTACK
from loot_mirror import (Inv, NONE, FISTS, CLUB, SPEAR, DAGGER, WARHAMMER,
                         CHAINMAIL, BANDAGE, ITEMS)
from combat_mirror import combat_step, resolve, hitbox, init, K as CFG
from arena_geometry import world

W = world()
ok = True


def check(n, c, d=""):
    global ok
    ok &= bool(c)
    print(f"  {'PASS' if c else 'FAIL'}  {n}{('  ' + d) if d else ''}")


def fighter(x, facing=1, weapon=NONE, armour=NONE):
    s = S(x, 0.9)
    s.inv = Inv()
    if weapon != NONE:
        s.inv.equip(weapon)
    if armour != NONE:
        s.inv.equip(armour)
    init(s)
    s.facing = s.travel = facing
    for _ in range(180):
        tick([s], [0])
        if s.mode == "ground":
            break
    init(s)
    s.facing = s.travel = facing
    return s


def tick(players, inputs, sudden_death=False):
    """One match tick: everyone decides, everyone moves, then hits are resolved together."""
    for p, inp in zip(players, inputs):
        moved = combat_step(p, inp)
        step(p, moved, W)
    resolve(players, sudden_death)


REACH = {FISTS: 0.65, CLUB: 0.9, SPEAR: 1.4}


def fight(attacker_weapon, defender_armour, spacing=None, limit=60 * 60):
    spacing = spacing if spacing is not None else REACH[attacker_weapon]
    """One fighter swinging as fast as they can into a defender who never guards."""
    a = fighter(-spacing / 2, 1, attacker_weapon)
    b = fighter(spacing / 2, -1, NONE, defender_armour)
    hits, ticks = 0, 0
    for t in range(limit):
        # keep the distance: this measures the weapon, not the footwork
        a.x, b.x = X(-spacing / 2), X(spacing / 2)
        a.vx = b.vx = 0
        b.stun = 0
        tick([a, b], [ATTACK, 0])
        ticks += 1
        if b.was_hit:
            hits += 1
        if b.dead:
            break
    return ticks / 60.0, hits, b.health


print("1. time to kill")
# This is the FLOOR, not a fight: every swing lands, nobody blocks, nobody moves. A real
# exchange costs whiffs, spacing and blocked hits, so docs/02's 8-14 s of active fighting
# sits roughly twice this. What the floor has to be is "fast enough to be decisive, slow
# enough that the scavenge phase mattered".
ttk = {}
for weapon, armour, label in ((FISTS, NONE, "fists, no armour"),
                              (CLUB, NONE, "club, no armour"),
                              (SPEAR, NONE, "spear, no armour"),
                              (CLUB, CHAINMAIL, "club vs chainmail"),
                              (SPEAR, CHAINMAIL, "spear vs chainmail")):
    seconds, hits, hp = fight(weapon, armour)
    ttk[label] = seconds
    print(f"        {label:22s} {seconds:5.1f} s uncontested, {hits} hits"
          f"   (~{seconds*2:4.1f} s contested)")

check("a weapon kills in four to eight seconds uncontested",
      all(4.0 <= ttk[k] <= 8.0 for k in ("club, no armour", "spear, no armour")),
      f"club {ttk['club, no armour']:.1f} s, spear {ttk['spear, no armour']:.1f} s")
check("doubled for a real exchange, that lands in the 8-14 s docs/02 asks for",
      all(8.0 <= ttk[k] * 2 <= 16.0 for k in ("club, no armour", "spear, no armour")))
check("armour buys most of a weapon tier",
      ttk["club vs chainmail"] > ttk["club, no armour"] * 1.3,
      f"{ttk['club, no armour']:.1f} s -> {ttk['club vs chainmail']:.1f} s")
check("bare hands lose to anything, but are not a dead end",
      ttk["fists, no armour"] > ttk["club, no armour"] * 1.4 and ttk["fists, no armour"] < 12,
      f"{ttk['fists, no armour']:.1f} s against a club's "
      f"{ttk['club, no armour']:.1f} — finding a weapon is most of a fight, "
      f"but a fistfight still ends")

print("\n2. the combo, three and no more (docs/02)")
a = fighter(-0.45, 1, CLUB)
b = fighter(0.45, -1)
seen = []
for t in range(240):
    a.x, b.x = X(-0.45), X(0.45)
    b.stun = 0
    b.health = 100
    before = b.health
    tick([a, b], [ATTACK, 0])
    if b.was_hit:
        seen.append((a.combo, b.damage_taken))
    if len(seen) >= 4:
        break
check("hits land in a string", len(seen) >= 3, f"{len(seen)} hits seen")
light = [d for c, d in seen[:2]]
heavy = [d for c, d in seen[2:3]]
check("the first two are light", len(set(light)) == 1, f"{light} damage")
check("the third is the heavy one", heavy and heavy[0] > light[0],
      f"{heavy[0] if heavy else '-'} vs {light[0]}")
check("and the fourth starts a new string", len(seen) == 4 and seen[3][1] == light[0],
      f"{seen[3][1] if len(seen) > 3 else '-'} damage")

print("\n3. reach is the difference between the two weapons")
for weapon, name in ((CLUB, "club"), (SPEAR, "spear")):
    reach = []
    for gap in [x / 100 for x in range(30, 320, 5)]:
        a = fighter(-gap / 2, 1, weapon)
        b = fighter(gap / 2, -1)
        for t in range(120):
            a.x, b.x = X(-gap / 2), X(gap / 2)
            tick([a, b], [ATTACK, 0])
            if b.was_hit:
                reach.append(gap)
                break
        if b.was_hit:
            continue
    print(f"        {name:6s} lands up to {max(reach):.2f} m apart")
    globals()["reach_" + name] = max(reach)
check("the spear outranges the club by most of a body width",
      reach_spear - reach_club > 0.6, f"{reach_spear:.2f} m vs {reach_club:.2f} m")

print("\n4. the guard")
a = fighter(-0.45, 1, CLUB)
b = fighter(0.45, -1)
for t in range(400):
    a.x, b.x = X(-0.45), X(0.45)
    b.stun = 0
    tick([a, b], [ATTACK, DOD])
    if b.was_blocked or b.was_parried:
        break
check("a raised guard turns a hit into something else", b.was_blocked or b.was_parried,
      "parried" if b.was_parried else "blocked")

# a guard raised long before the blow blocks rather than parries
a = fighter(-0.45, 1, CLUB)
b = fighter(0.45, -1)
for _ in range(30):
    tick([a, b], [0, DOD])                       # guard up well in advance
hp_before, stam_before = b.health, b.stam
for t in range(400):
    a.x, b.x = X(-0.45), X(0.45)
    tick([a, b], [ATTACK, DOD])
    if b.was_blocked:
        break
check("held long enough, it blocks", b.was_blocked, f"guard up {b.guard_t} frames")
chip = hp_before - b.health
check("a block still costs health", 0 < chip <= 2, f"{chip} chip of a full 8")
check("and stamina", b.stam < stam_before, f"{stam_before} -> {b.stam}")

print("\n5. the parry: the window that rewards reading, not reacting")
a = fighter(-0.45, 1, CLUB)
b = fighter(0.45, -1)
parried = False
for t in range(400):
    a.x, b.x = X(-0.45), X(0.45)
    # raise the guard only as the swing becomes active: that is the read
    guard = DOD if a.attack == K.WINDUP and a.attack_t <= 2 else 0
    tick([a, b], [ATTACK, guard])
    if b.was_parried:
        parried = True
        break
check("a late guard parries", parried, f"guard was up {b.guard_t} frames")
check("a parry costs the defender nothing", b.health == 100, f"hp {b.health}")
check("and staggers the attacker", a.stagger > 0, f"{a.stagger} frames")
check("the attacker's swing is gone", a.attack == K.NONE_P)

print("\n6. a guard is not a wall")
a = fighter(-0.45, 1, CLUB)
b = fighter(0.45, -1)
for _ in range(30):
    tick([a, b], [0, DOD])
broke = False
for t in range(900):
    a.x, b.x = X(-0.45), X(0.45)
    tick([a, b], [ATTACK, DOD])
    if b.guard_break > 0:
        broke = True
        break
check("holding block forever runs the stamina out and breaks it", broke,
      f"stamina {b.stam}, open for {b.guard_break} frames")

a = fighter(-0.45, 1, CLUB)
b = fighter(0.45, 1)                              # facing AWAY
for t in range(400):
    a.x, b.x = X(-0.45), X(0.45)
    b.facing = 1
    tick([a, b], [ATTACK, DOD])
    if b.was_hit or b.was_blocked:
        break
check("a guard held facing the wrong way is no guard at all", b.was_hit,
      "hit" if b.was_hit else "blocked")

print("\n7. armour")
a = fighter(-0.45, 1, CLUB)
plain = fighter(0.45, -1)
mailed = fighter(0.45, -1, NONE, CHAINMAIL)
for victim, name in ((plain, "no armour"), (mailed, "chainmail")):
    atk = fighter(-0.45, 1, CLUB)
    for t in range(200):
        atk.x, victim.x = X(-0.45), X(0.45)
        tick([atk, victim], [ATTACK, 0])
        if victim.was_hit:
            break
    globals()["dmg_" + name.split()[0]] = victim.damage_taken
check("chainmail takes 30% off, as docs/02 says",
      dmg_chainmail == (dmg_no * 700) // 1000,
      f"{dmg_no} -> {dmg_chainmail} ({(1 - dmg_chainmail / dmg_no) * 100:.0f}% off)")

print("\n8. a hit is felt")
a = fighter(-0.45, 1, CLUB)
b = fighter(0.45, -1)
for t in range(200):
    if not b.was_hit:
        a.x, b.x = X(-0.45), X(0.45)
    tick([a, b], [ATTACK, 0])
    if b.was_hit:
        at_hit = F(b.x)
        break
for _ in range(20):
    tick([a, b], [0, 0])
check("knockback actually moves the body", F(b.x) - at_hit > 0.3,
      f"{at_hit:.2f} -> {F(b.x):.2f} m")
check("and it cannot act while it reels", b.stun >= 0 and b.attack == K.NONE_P)

s = fighter(0, 1, CLUB)
s.stun = 30
before = F(s.x)
swung = False
for _ in range(20):                               # still inside the stun
    tick([s], [R | ATTACK])
    swung |= s.swing_started
check("no steering out of hitstun", abs(F(s.x) - before) < 0.6, f"drifted {F(s.x)-before:+.2f} m")
check("and no swinging out of it", not swung and s.attack == K.NONE_P,
      f"{s.stun} frames of stun left")
for _ in range(20):                               # and it ends
    tick([s], [ATTACK])
check("once it ends, the body answers again", s.attack != K.NONE_P or s.swing_started)

print("\n9. a swing slows you: that is what makes spacing a decision")
# It used to ROOT you, and a fighter who stops dead to attack cannot attack while he is
# going anywhere -- every exchange becomes two people standing still taking turns. So the
# walking is given back and the commitment is kept: slower than running, and no escaping.
s = fighter(0, 1, SPEAR)
tick([s], [ATTACK])
before = F(s.x)
for _ in range(20):
    tick([s], [R | ATTACK])
moved = F(s.x) - before
free = F(M.C["RUN"]) * 20 / 60.0
check("holding right during a swing still moves you", moved > 0.1, f"{moved:+.3f} m")
check("but slower than running does", moved < free * 0.75,
      f"{moved:.3f} m against {free:.3f} m of running")

# And the commitment itself, which is the half that must NOT be given back.
s = fighter(0, 1, SPEAR)
tick([s], [ATTACK])
for _ in range(20):
    tick([s], [R | ATTACK | JMP | DOD])
check("and a swing cannot be cancelled with a jump or a dodge",
      s.attack != K.NONE_P and s.mode != "dodge", f"phase {s.attack}, mode {s.mode}")

print("\n10. death")
a = fighter(-0.45, 1, SPEAR)
b = fighter(0.45, -1)
for t in range(60 * 60):
    a.x, b.x = X(-0.45), X(0.45)
    b.stun = 0
    tick([a, b], [ATTACK, 0])
    if b.dead:
        break
check("health reaches zero and stays there", b.dead and b.health == 0, f"hp {b.health}")
tick([a, b], [ATTACK, ATTACK | R])
check("the dead do not act", b.attack == K.NONE_P and not b.blocking)

print("\n10b. one defensive button, two answers (docs/02)")
# docs/02 gives the medieval fighter a block and no roll button; this project has both and
# one bit of input to carry them. Standing still the press is the guard, moving it is a
# dodge, and in the air it is neither. The bug this replaces had it exactly backwards: on
# the ground the guard ate the press and nothing rolled, in the air there was no guard to
# eat it and it rolled.
a = fighter(-3.0)
tick([a], [DOD])
check("standing still, the press puts the guard up", a.blocking and a.mode == "ground",
      f"blocking={a.blocking} mode={a.mode}")

a = fighter(-3.0)
tick([a], [DOD | R])
check("with a direction held, the same press is a dodge",
      a.mode == "dodge" and not a.blocking, f"mode={a.mode} blocking={a.blocking}")

a = fighter(-3.0)
tick([a], [JMP])
for _ in range(8):
    tick([a], [0])
airborne = a.mode == "air"
for _ in range(20):
    tick([a], [DOD | R])
    if a.mode == "dodge":
        break
check("in the air it is neither", airborne and a.mode != "dodge" and not a.blocking,
      f"left the ground={airborne} mode={a.mode}")

a = fighter(-3.0)
start = a.stam
tick([a], [DOD | R])
check("a dodge costs two of the four charges", a.stam == start - 2,
      f"{start} -> {a.stam}")

print("\n10b2. a raised guard is an action, not a posture")
a = fighter(-3.0)
start = a.stam
held = 0
for f in range(400):
    tick([a], [DOD])
    if a.blocking:
        held = f + 1
    elif held:
        break
check("holding the guard spends the pool", a.stam == 0, f"{start} -> {a.stam}")
check("and it runs out in about three seconds", 2.8 < held / 60 < 3.6,
      f"the guard held {held / 60:.1f} s")
check("the arm drops rather than breaking: holding too long is not a hit",
      not a.blocking and a.guard_break == 0 and a.stun == 0)

a = fighter(-3.0)
for _ in range(120):
    tick([a], [DOD])
braced = a.stam
for _ in range(180):
    tick([a], [DOD])
check("nothing comes back while braced", a.stam <= braced, f"{braced} -> {a.stam}")

print("\n10c. a fall off a floor is a way the fight ends (docs/02)")
a = fighter(0.0)                   # above the hole in the top floor, so the fall is clear
a.y = X(6.2)
a.mode = "air"
before = a.health
for _ in range(240):
    tick([a], [0])
    if a.mode == "ground":
        break
tick([a], [0])                     # the combat pass spends what the motor measured
check("a drop of a whole storey costs health", a.health < before,
      f"{before} -> {a.health} hp from {6.2 - F(a.y):.2f} m")

a = fighter(-3.0)
a.y = X(2.0)
a.mode = "air"
before = a.health
for _ in range(240):
    tick([a], [0])
    if a.mode == "ground":
        break
tick([a], [0])
check("a drop inside a storey is free", a.health == before, f"{before} -> {a.health} hp")

print("\n10d. bare hands get desperate below 30% HP (docs/02)")
def fist_damage(health, sudden_death=False):
    """Bare hands reach 0.6 m, so the two have to stand closer than a weapon fight."""
    atk = fighter(-0.25, 1)
    vic = fighter(0.25, -1)
    atk.health = health
    for _ in range(200):
        atk.x, vic.x = X(-0.25), X(0.25)
        vic.stun = 0
        tick([atk, vic], [ATTACK, 0], sudden_death)
        if vic.damage_taken:
            return vic.damage_taken
    return 0

full = fist_damage(100)
hurt = fist_damage(25)
check("a cornered fighter hits harder with nothing in their hands", hurt > full,
      f"{full} at full health, {hurt} at 25")
check("and by the 40% docs/02 asks for", hurt == K.scale(full, CFG["DESPERATE_DMG"]),
      f"{full} -> {hurt}")

print("\n10e. on the last platform, bare hands hit twice as hard (docs/01)")
sudden = fist_damage(100, sudden_death=True)
check("fists hurt in sudden death even at full health", sudden > full,
      f"{full} -> {sudden}")
check("by exactly the x2 docs/01 asks for",
      sudden == K.scale(full, CFG["SUDDEN_BARE"]), f"{full} -> {sudden}")

both = fist_damage(25, sudden_death=True)
check("desperation and sudden death do not stack: the bigger one wins",
      both == max(hurt, sudden),
      f"{hurt} desperate, {sudden} in sudden death, {both} when both apply")

armed = None
for sd in (False, True):
    atk = fighter(-0.45, 1, weapon=CLUB)
    vic = fighter(0.45, -1)
    got = 0
    for _ in range(200):
        atk.x, vic.x = X(-0.45), X(0.45)
        vic.stun = 0
        tick([atk, vic], [ATTACK, 0], sd)
        if vic.damage_taken:
            got = vic.damage_taken
            break
    if armed is None:
        armed = got
    else:
        check("and a weapon is untouched by it, so sudden death favours nobody with one",
              got == armed, f"{armed} with a club either way")

print("\n11. the same inputs give the same frames")
runs = []
for _ in range(2):
    a = fighter(-0.45, 1, CLUB)
    b = fighter(0.45, -1, NONE, CHAINMAIL)
    for t in range(600):
        tick([a, b], [ATTACK, DOD if t % 90 < 20 else 0])
    runs.append((a.x, a.y, a.health, a.stagger, b.x, b.y, b.health, b.stam))
check("bit-identical replay", runs[0] == runs[1], f"{runs[0]}")

print("\n12. point-blank: two fighters standing on the same spot")
# They do not collide with each other, only with the level, so this is a position the game
# reaches often — and for a long time nothing landed in it from either side.
for gap in (0.0, 0.05, 0.15, 0.30):
    atk = fighter(0.0, 1, CLUB)
    vic = fighter(gap, -1)
    landed = 0
    for t in range(240):
        atk.x, vic.x = X(0.0), X(gap)      # held together, as a shove would hold them
        tick([atk, vic], [ATTACK, 0])
        if vic.was_hit:
            landed += 1
    check(f"a swing lands with {gap * 100:.0f} cm between the centres", landed > 0,
          f"{landed} hit(s) in four seconds")

# And it still must not reach behind.
atk = fighter(0.0, 1, CLUB)
vic = fighter(-0.9, -1)
behind = 0
for t in range(240):
    atk.x, vic.x = X(0.0), X(-0.9)
    tick([atk, vic], [ATTACK, 0])
    if vic.was_hit:
        behind += 1
check("and never reaches behind the attacker", behind == 0,
      f"{behind} hit(s) on somebody 0.9 m the wrong way")

print("\n13. hit stop: the weight of a blow")
from combat_mirror import K

# A clean hit freezes BOTH of them, for the configured frames, and nothing moves while it does.
atk = fighter(-0.45, 1, CLUB)
vic = fighter(0.45, -1)
for t in range(200):
    atk.x, vic.x = X(-0.45), X(0.45)
    tick([atk, vic], [ATTACK, 0])
    if vic.was_hit:
        break
check("a hit freezes the fighter who took it", vic.hitstop == K["HIT_STOP"],
      f"{vic.hitstop} frame(s)")
check("and the one who threw it", atk.hitstop == K["HIT_STOP"],
      f"{atk.hitstop} frame(s)  — half the weight of a blow is the arm stopping")

# Frozen means frozen: the body is held where the blow caught it, gravity included.
held = (vic.x, vic.y, vic.vx, vic.vy)
frames = 0
while vic.hitstop > 0 and frames < 60:
    tick([atk, vic], [0, 0])
    frames += 1
    if vic.hitstop > 0:
        check(f"nothing moved on freeze frame {frames}",
              (vic.x, vic.y, vic.vx, vic.vy) == held, "held exactly where it was caught")
check("and it lasts the frames it was given", frames == K["HIT_STOP"], f"{frames}")

# A parry stops the world for longer than a hit does: docs/02 makes it the skill ceiling.
check("a parry freezes for longer than a hit", K["PARRY_STOP"] > K["HIT_STOP"],
      f'parry {K["PARRY_STOP"]} vs hit {K["HIT_STOP"]}')
check("and a block for less", K["BLOCK_STOP"] < K["HIT_STOP"],
      f'block {K["BLOCK_STOP"]}')

# The freeze must not be able to stack into a visible hitch.
a2 = fighter(-0.45, 1, CLUB)
v2 = fighter(0.45, -1, CLUB)
worst = 0
for t in range(600):
    tick([a2, v2], [ATTACK, ATTACK])
    worst = max(worst, a2.hitstop, v2.hitstop)
check("two fighters trading never freeze longer than one parry",
      worst <= K["PARRY_STOP"], f"worst {worst} frame(s)")

print("\n14. aiming one way while going the other")
# The feature, in one sentence: the left thumb says where the body goes and the right thumb
# says where the blade goes, and they are allowed to disagree. A fighter who can only strike
# the way he is walking cannot retreat and cannot cover a retreat, so every exchange is a
# straight line and the only decision in it is when to walk forward.
# K is the config dict by this point in the file, not the module, so the phases come in
# by name rather than through it.
from sim_motor_mirror import AIM_L, AIM_R
from combat_mirror import NONE_P, ACTIVE

# First, that nothing which does NOT swipe behaves any differently. Every other test in
# this file and the eight suites beside it depend on this.
s = fighter(0, 1)
for _ in range(30):
    tick([s], [L])
check("with no swipe the aim is still just the feet", s.facing == -1 and s.travel == -1,
      f"aim {s.facing:+d} travel {s.travel:+d}")

# Running LEFT, striking RIGHT.
s = fighter(0, 1, CLUB)
start = F(s.x)
tick([s], [L | ATTACK | AIM_R])
check("a swipe aims against the feet", s.facing == 1 and s.travel == -1,
      f"aim {s.facing:+d} travel {s.travel:+d}")

aimed_all_swing = True
live = None
for t in range(40):
    tick([s], [L | ATTACK | AIM_R])
    if s.attack != NONE_P and s.facing != 1:
        aimed_all_swing = False
    if s.attack == ACTIVE and live is None:
        live = hitbox(s)

check("and keeps the aim for the whole swing", aimed_all_swing, f"aim {s.facing:+d}")
check("while the body actually travels the other way", F(s.x) - start < -0.1,
      f"{F(s.x) - start:+.3f} m")

# The hitbox is the thing that has to agree, because it is the only one the other player
# can feel. An aim that reads right and swings left is worse than no feature.
check("the blow goes where it was aimed, not where the feet went",
      live is not None and F(live[2]) > F(s.x),
      "no live frame" if live is None else
      f"hitbox to {F(live[2]):+.2f}, body at {F(s.x):+.2f}")

# A retreating fighter keeps the other one off: the whole point.
a = fighter(0.0, 1, CLUB)
b = fighter(0.7, -1)
a.travel = 1
landed = False
for t in range(120):
    tick([a, b], [L | ATTACK | AIM_R, 0])
    b.x = X(0.7)                      # hold the spacing: this measures the aim, not the chase
    landed |= b.was_hit
check("a fighter backing away still lands what he aimed behind him", landed,
      f"health {b.health}")

# Letting go: the aim must come back to the feet, or a fighter who swiped once would never
# turn round again.
s = fighter(0, 1, CLUB)
tick([s], [L | ATTACK | AIM_R])
for _ in range(240):
    tick([s], [L])
check("and the feet take the aim back once the swipe is let go", s.facing == -1,
      f"aim {s.facing:+d} after {M.C['AIMLOCK']} locked frames")

# The aim has to survive until the blow lands, for the SLOWEST weapon in the game, with the
# movement stick held the other way the whole time. Two things keep it alive -- the lock the
# swipe buys, and the swing itself -- and this does not care which: it asks the only question
# the player can feel. A warhammer's heavy windup is 51 frames, twice the lock, so a lock
# that quietly did all the work would fail here.
WEAPON = 1
slowest = max(ITEMS, key=lambda i: (ITEMS[i].get("frames") or 0)
              if ITEMS[i].get("kind") == WEAPON else 0)
s = fighter(0, 1, slowest)
aimed, live_at = True, None
for t in range(240):
    tick([s], [L | ATTACK | AIM_R])
    if s.attack != NONE_P and s.facing != 1:
        aimed = False
    if s.attack == ACTIVE and live_at is None:
        live_at = t
check(f"the aim survives to the blow with the slowest weapon ({ITEMS[slowest]['name']})",
      aimed and live_at is not None,
      f"live on frame {live_at}, {ITEMS[slowest]['frames']} of base windup")

# And the lock on its own covers a swipe that asks for nothing else: turning to face without
# attacking is a real input, and one frame of it cannot hold against a held stick.
s = fighter(0, 1, CLUB)
tick([s], [L | AIM_R])
held = 0
for _ in range(240):
    tick([s], [L])
    if s.facing == 1:
        held += 1
    else:
        break
check("and a bare swipe holds the aim for the frames it bought",
      held >= M.C["AIMLOCK"] - 1, f"{held} frame(s) of {M.C['AIMLOCK']}")

# And the guard faces the AIM, which is what makes backing away a real choice rather than a
# free one: turn the blade round and your shield goes with it.
a = fighter(-0.45, 1, CLUB)
v = fighter(0.45, -1, NONE, CHAINMAIL)
blocked = False
for t in range(120):
    tick([a, v], [ATTACK, DOD | AIM_L])
    a.x, v.x = X(-0.45), X(0.45)
    blocked |= v.was_blocked
check("a guard covers the way the fighter aims", blocked, f"health {v.health}")

# ---------------------------------------------------------------- the turn
#
# The one number that decides whether a fight is a fight: who may act first once a blow
# has landed. Measured before this was fixed, EVERY weapon in the game was negative --
# saber -3, club -5, spear -8, scythe -12 -- which means landing a hit handed the turn to
# the person you just hit, and the correct play was never to swing first. Two players both
# playing correctly then produce a stand-off, which is what a wooden fight is.
print("\n9. landing a blow buys the turn")

# K was rebound to the config dict further up the file; the module itself is still needed.
import combat_mirror as CM
from combat_mirror import RECOVERY


def free(p):
    return (not CM.swinging(p) and getattr(p, "hitstop", 0) == 0 and p.stun == 0
            and p.stagger == 0 and not CM.reeling(p))


def advantage(weapon_id, guard=False):
    """Frames the attacker is free BEFORE the victim, counted from the blow landing."""
    reach = ITEMS[FISTS if weapon_id == NONE else weapon_id]["reach"] / 65536.0
    a = fighter(0.0, 1, weapon_id)
    v = fighter(0.5 + reach * 0.5, -1)
    landed = af = vf = None
    for t in range(300):
        tick([a, v], [ATTACK if t == 0 else 0, DOD if guard else 0])
        if landed is None and (v.was_hit or v.was_blocked):
            landed = t
        if landed is not None:
            if af is None and free(a): af = t
            if vf is None and free(v): vf = t
            if af is not None and vf is not None:
                return vf - af
    return None


worst = None
for name, wid in (("fists", NONE), ("club", CLUB), ("spear", SPEAR)):
    adv = advantage(wid)
    print(f"        {name:8} {adv:+d} on hit")
    if worst is None or adv < worst:
        worst = adv
check("every weapon is PLUS on hit, so connecting keeps the initiative",
      worst is not None and worst > 0, f"worst {worst:+d}")

# And minus on block, or there is no reason to ever put the guard up.
onblock = advantage(CLUB, guard=True)
check("and minus on block, so guarding takes the turn back",
      onblock is not None and onblock < 0, f"club {onblock:+d} on block")

# A press during recovery used to be thrown away, so continuing a string meant releasing
# and pressing again on the exact frame the swing ended -- which on a phone is a coin toss,
# not a skill. The buffer has to outlast the longest recovery a player is actually trying
# to chain out of, which is the recovery of a swing that LANDED (halved), the worst of them
# being the warhammer's eleven frames.
def buffered(weapon_id):
    """One tap on the first frame of recovery after a blow LANDS, then nothing.

    Against a victim on purpose. A whiffed heavy swing should NOT come round again off a
    stale press -- the long recovery of a miss is the punish window, and the buffer is
    there to continue a string, not to hide a mistake."""
    reach = ITEMS[FISTS if weapon_id == NONE else weapon_id]["reach"] / 65536.0
    a = fighter(0.0, 1, weapon_id)
    v = fighter(0.5 + reach * 0.5, -1)
    tick([a, v], [ATTACK, 0])
    for _ in range(90):
        if a.attack == RECOVERY:
            break
        tick([a, v], [0, 0])
    tick([a, v], [ATTACK, 0])
    for _ in range(90):
        tick([a, v], [0, 0])
        if a.swing_started:
            return True
    return False


missed = [n for n, w in (("dagger", DAGGER), ("club", CLUB), ("spear", SPEAR),
                         ("warhammer", WARHAMMER)) if not buffered(w)]
check("a tap during recovery is remembered and starts the next swing",
      not missed, "dropped by " + ", ".join(missed) if missed else "every weapon chained")

# A light hit used to lift the victim 3 cm and take them off the ground for 21 frames,
# which slid them out of reach before any follow-up could land.
a = fighter(0, 1, CLUB)
v = fighter(0.8, -1)
air = 0
for t in range(60):
    tick([a, v], [ATTACK if t == 0 else 0, 0])
    if v.was_hit:
        for _ in range(40):
            tick([a, v], [0, 0])
            if v.mode != "ground":
                air += 1
        break
check("a light blow leaves them standing", air == 0, f"{air} airborne frame(s)")

print("\n" + ("ALL PASS" if ok else "SOME FAILED"))
