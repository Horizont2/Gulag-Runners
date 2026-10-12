"""The match clock: when a fighter may move, when a round is over, and what comes after it."""
import match_mirror as M
from match_mirror import Match, step, phase_at, total_frames, DEFAULT, NAMES, CAGES, SCAVENGE, \
    SIREN, HUNT, CLOSING, SUDDEN, OVER, Gate, gate_shuts_at, gates_shut_at, shove, \
    CLOSING_GATE, SUDDEN_GATE, MINUS_X, PLUS_X, MINUS_Y, PLUS_Y, phase_start
import loot_mirror as L

ok = True


def check(name, cond, detail=""):
    global ok
    ok &= bool(cond)
    print(f"  {'PASS' if cond else 'FAIL'}  {name}{('  ' + detail) if detail else ''}")


def clock(at):
    return f"{int(at // 60)}:{at % 60:04.1f}"


print("The shape of a match (docs/01)")
cfg = DEFAULT
edges = []
m = Match()
for _ in range(total_frames(cfg) + 600):
    before = m.phase
    step(m, cfg)
    if m.phase != before:
        edges.append((m.frame / 60.0, NAMES[m.phase]))
for at, name in edges:
    print(f"   {clock(at)}  {name}")

check("the phases arrive in the order docs/01 writes them",
      [n for _, n in edges] == ["scavenge", "siren", "hunt", "closing", "sudden death", "over"])
check("the cages last three seconds", abs(edges[0][0] - 3.0) < 0.02, f"{edges[0][0]:.2f} s")
check("the siren is at 0:25", abs(edges[1][0] - 25.0) < 0.05, f"{edges[1][0]:.2f} s")
check("the rooms start shutting at 1:15", abs(edges[3][0] - 75.0) < 0.05, f"{edges[3][0]:.2f} s")
check("the ceiling comes down at 2:20", abs(edges[4][0] - 140.0) < 0.1, f"{edges[4][0]:.2f} s")
check("and docs/01's 2:30 is a wall, not a guideline",
      abs(edges[5][0] - 150.0) < 0.1, f"{edges[5][0]:.2f} s")

print("\nIn the cage, nobody moves")
m = Match()
locked = 0
for _ in range(cfg["cage"] + 120):
    if not m.input_allowed:
        locked += 1
    step(m, cfg)
check("input is locked for exactly the cage phase", locked == cfg["cage"],
      f"{locked / 60:.2f} s of {cfg['cage'] / 60:.2f}")

m = Match()
seen = []
for _ in range(cfg["cage"]):
    seen.append(m.cage_progress(cfg))
    step(m, cfg)
check("the doors run from shut to open across it",
      seen[0] == 0.0 and abs(seen[-1] - 1.0) < 0.01 and seen == sorted(seen),
      f"{seen[0]:.2f} -> {seen[-1]:.2f}, never going backwards")

print("\nOne life")
m = Match()
for _ in range(600):
    step(m, cfg)
step(m, cfg, dead=(False, True))
check("a death ends it wherever the clock had got to", m.phase == OVER, NAMES[m.phase])
check("and the survivor wins", m.winner == 0, f"winner {m.winner}")
check("it is announced exactly once", m.just_ended)
step(m, cfg)
check("and not again on the next tick", not m.just_ended and m.phase == OVER)
check("a fighter cannot act once it is over", not m.input_allowed)

m = Match()
step(m, cfg, dead=(True, True))
check("both at once is a draw, not a race", m.winner == 2, f"winner {m.winner}")

print("\nTime runs out")
m = Match()
while m.phase != OVER:
    step(m, cfg, health=(63, 41))
check("a round nobody finishes is decided on health", m.winner == 0, f"winner {m.winner}")
check("on the last tick docs/01 allows", m.frame == total_frames(cfg),
      f"{clock(m.frame / 60.0)}")

m = Match()
while m.phase != OVER:
    step(m, cfg, health=(41, 63))
check("and the other way about", m.winner == 1, f"winner {m.winner}")

m = Match()
while m.phase != OVER:
    step(m, cfg, health=(52, 52))
check("level pegging is the draw it looks like", m.winner == 2, f"winner {m.winner}")

held = dict(cfg, sudden=0)
m = Match()
for _ in range(total_frames(cfg) + 1200):
    step(m, held)
check("a build with no sudden death keeps playing rather than ending in the dark",
      m.phase == SUDDEN and not m.finished, NAMES[m.phase])

print("\nAnd then: again (docs/01)")
m = Match()
step(m, cfg, dead=(False, True))
check("nothing wants restarting the moment it ends", not m.wants_restart)
for _ in range(cfg["restart"] - 1):
    step(m, cfg)
check("nor until the result has stood its five seconds", not m.wants_restart,
      f"{m.over_frame / 60:.2f} s of {cfg['restart'] / 60:.2f}")
step(m, cfg)
check("and then it does", m.wants_restart, f"{m.over_frame / 60:.2f} s")
step(m, cfg)
check("it stays up until somebody acts on it, so a missed frame cannot lose the rematch",
      m.wants_restart)

seen = []
m = Match()
step(m, cfg, dead=(False, True))
for _ in range(cfg["restart"]):
    seen.append(m.over_progress(cfg))
    step(m, cfg)
check("the result runs from fresh to spent across it",
      seen[0] == 0.0 and abs(seen[-1] - 1.0) < 0.01 and seen == sorted(seen),
      f"{seen[0]:.2f} -> {seen[-1]:.2f}")

pinned = dict(cfg, restart=0)
m = Match()
step(m, pinned, dead=(False, True))
for _ in range(1200):
    step(m, pinned)
check("a result set to be held is held, not restarted", not m.wants_restart,
      f"{m.over_frame / 60:.1f} s and still waiting")

print("\nA round that restarts is a new round, not the old one re-run")


class Runner:
    """MatchState, as far as the clock is concerned: it deals a round and restarts one."""

    def __init__(s, chests, seed=0x5EED1234):
        s.chests = chests
        s.rolling = seed
        s.rounds = []
        s.deal(0)

    def next_seed(s):
        s.rolling = (s.rolling * 1664525 + 1013904223) & 0xFFFFFFFF
        return s.rolling

    def deal(s, round):
        s.match = Match(round)
        for c in s.chests:
            c.opened, c.progress, c.contents = False, 0, L.NONE
        L.deal(s.chests, s.next_seed())
        s.rounds.append([c.contents for c in s.chests])

    def tick(s, **kw):
        step(s.match, cfg, **kw)
        if s.match.wants_restart:
            s.deal(s.match.round + 1)


chests = [L.Chest(kind, L.NONE, None)
          for kind in (L.CRATE, L.CRATE, L.LOCKER, L.LOCKER, L.SAFE)]
r = Runner(chests)
r.chests[0].opened = True
r.chests[0].progress = 999
for _ in range(900):
    r.tick()
r.tick(dead=(True, False))
check("the round ends on a death", r.match.finished and r.match.winner == 1)
ended_at = r.match.round
for _ in range(cfg["restart"]):
    r.tick()
check("and the next one is dealt exactly when the clock says",
      r.match.round == ended_at + 1 and len(r.rounds) == 2, f"round {r.match.round}")
check("the clock is back in the cages", r.match.phase == CAGES and r.match.frame == 0,
      NAMES[r.match.phase])
check("nobody has won it yet", r.match.winner == -1 and not r.match.wants_restart)
check("every chest is shut again",
      all(not c.opened and c.progress == 0 for c in r.chests))
check("and filled with something else, so the arena has to be searched again (docs/01)",
      r.rounds[0] != r.rounds[1],
      f"{[L.ITEMS[i]["name"] for i in r.rounds[0]]} -> {[L.ITEMS[i]["name"] for i in r.rounds[1]]}")

for _ in range(3):
    r.tick(dead=(False, True))
    for _ in range(cfg["restart"]):
        r.tick()
check("and it keeps going round: four rounds off one match object", r.match.round == 4,
      f"round {r.match.round}")
check("each one dealt differently", len({tuple(x) for x in r.rounds}) == len(r.rounds),
      f"{len(r.rounds)} deals, {len({tuple(x) for x in r.rounds})} distinct")

print("\nThe rooms shut, one after another (docs/01)")


def arena():
    """Two full-height shutters standing in the gaps of the walkway, herding inwards."""
    return [Gate(-7.48, -1.0, -7.18, 6.0, order=0, toward=PLUS_X),
            Gate(3.62, -1.0, 3.92, 6.0, order=1, toward=MINUS_X)]


gates = arena()
due = [gate_shuts_at(i, gates, cfg) for i in range(len(gates))]
for i, at in enumerate(due):
    print(f"   gate {i}  {clock(at / 60.0)}")
check("they shut inside the closing phase, not before it",
      all(at > cfg["cage"] + cfg["scavenge"] + cfg["siren"] + cfg["hunt"] for at in due))
check("one after another, evenly across the 45 s docs/01 gives the shutters",
      due[1] - due[0] == cfg["shutter"] // 2, f"{(due[1] - due[0]) / 60:.1f} s apart")
check("and the last one lands at 2:00, leaving only the middle",
      abs(due[-1] / 60.0 - 120.0) < 0.05, clock(due[-1] / 60.0))

m = Match()
seen, masks = [], []
for _ in range(total_frames(cfg) + 60):
    step(m, cfg, gates=gates)
    masks.append(m.gates_shut)
    if m.gates_just_shut:
        seen.append((m.frame, m.gates_just_shut))
check("each gate announces itself exactly once", len(seen) == 2 and
      all(bin(mask).count("1") == 1 for _, mask in seen), f"{len(seen)} announcements")
check("on the tick it was due", [f for f, _ in seen] == due, f"{[f for f, _ in seen]} vs {due}")
check("and the arena only ever closes, never springs back open", masks == sorted(masks))
check("a shutter stays shut while the result is held",
      m.finished and m.gates_shut == 0b11, f"mask {m.gates_shut:b}")

ceiling = arena() + [Gate(-7.18, 2.6, 3.62, 3.5, when=SUDDEN_GATE, toward=MINUS_Y)]
at = gate_shuts_at(2, ceiling, cfg)
check("sudden death's ceiling comes down on its first tick, at 2:20",
      abs(at / 60.0 - 140.0) < 0.05, clock(at / 60.0))
check("and the shutters before it are unmoved by its being there",
      [gate_shuts_at(i, ceiling, cfg) for i in (0, 1)] == due)

print("\nNobody is sealed into a wing")


def fighter(x, y=0.12, depth=0.0):
    return dict(x=x, y=y, depth=depth, vx=3.0, vy=0.0, ladder=4, chest=2, stagger=0, shoved=0)


gates = arena()
stranded = fighter(-15.5)          # at the far left, by the cage, when the left gate slams
inside = fighter(-2.0)             # already in the middle
shove([stranded, inside], gates, 0b01)
check("a fighter left behind the shutter is brought through", stranded["shoved"] == 1,
      f"x -15.50 -> {stranded['x']:.2f}")
check("to just clear of it, on the inner side",
      abs(stranded["x"] - (gates[0].max_x + 0.2 + 0.01)) < 1e-9, f"x {stranded['x']:.3f}")
check("staggered for it, not merely moved", stranded["stagger"] == 24)
check("and let go of the ladder and the chest that are now behind a wall",
      stranded["ladder"] == -1 and stranded["chest"] == -1)
check("a fighter already in the middle is left where they stand",
      inside["shoved"] == 0 and inside["x"] == -2.0)

right = fighter(11.7)
shove([right], gates, 0b10)
check("the right shutter herds the other way",
      abs(right["x"] - (gates[1].min_x - 0.2 - 0.01)) < 1e-9, f"x {right['x']:.3f}")

lane = fighter(-15.5)
lane["depth"] = 40.0               # a slice this shutter does not reach
narrow = [Gate(-7.48, -1.0, -7.18, 6.0, order=0, toward=PLUS_X, span=(-3.0, 0.0))]
shove([lane], narrow, 0b1)
check("a shutter only herds bodies it would actually have blocked", lane["shoved"] == 0,
      "a gate on a slice you cannot touch is one you could walk around")

print("\nThe siren is a direction, not a tracker (docs/01)")
m = Match()
while m.phase != SIREN:
    step(m, cfg, x=(-15.5, 11.7))
check("the pulse points at the other fighter", m.siren_direction == 1,
      f"{m.siren_direction:+d}")
check("and each fighter is given its own side of it",
      (m.siren_for(0), m.siren_for(1)) == (1, -1))
for _ in range(cfg["siren"] + 600):
    step(m, cfg, x=(11.7, -15.5))   # they swap ends afterwards
check("it does not follow them: one pulse, taken once", m.siren_direction == 1,
      "a direction read every tick would be a tracker")

m = Match()
while m.phase != SIREN:
    step(m, cfg, x=(8.0, -4.0))
check("and it reads the other way about when they are the other way about",
      m.siren_direction == -1, f"{m.siren_direction:+d}")

print("\nThe clock is the same on both devices")
a, b = Match(), Match()
for i in range(4000):
    step(a, cfg)
    step(b, cfg)
check("two clocks from the same table never disagree",
      (a.frame, a.phase) == (b.frame, b.phase), f"frame {a.frame}, {NAMES[a.phase]}")
check("and the phase at a tick can be asked without stepping to it",
      [phase_at(f, cfg) for f in (0, 179, 180, 1499, 1500, 4499, 4500, 8399, 8400)] ==
      [CAGES, CAGES, SCAVENGE, SCAVENGE, SIREN, HUNT, CLOSING, CLOSING, SUDDEN])
check("and the first tick of a phase is the tick that phase starts on",
      all(phase_at(phase_start(p, cfg), cfg) == p
          for p in (CAGES, SCAVENGE, SIREN, HUNT, CLOSING, SUDDEN)) and
      all(phase_at(phase_start(p, cfg) - 1, cfg) != p
          for p in (SCAVENGE, SIREN, HUNT, CLOSING, SUDDEN)),
      "which is what a view measures how far through a phase it is from")

a, b = Match(), Match()
for i in range(cfg["cage"] + 60):
    step(a, cfg)
    step(b, cfg)
step(a, cfg, dead=(False, True))
step(b, cfg, dead=(False, True))
restarts = []
for i in range(cfg["restart"] + 60):
    step(a, cfg)
    step(b, cfg)
    if a.wants_restart != b.wants_restart:
        restarts.append(i)
check("and they want the rematch on the same tick", not restarts,
      f"disagreed on {len(restarts)} tick(s)")

ga, gb = arena(), arena()
a, b = Match(), Match()
drift = []
for i in range(total_frames(cfg)):
    step(a, cfg, gates=ga)
    step(b, cfg, gates=gb)
    if a.gates_shut != b.gates_shut:
        drift.append(i)
check("and two arenas shut on the same ticks, having never spoken", not drift,
      f"disagreed on {len(drift)} tick(s); final mask {a.gates_shut:b}")

print("\n" + ("ALL PASS" if ok else "SOME FAILED"))
