"""The match clock: when a fighter may move, and when a round is over."""
import match_mirror as M
from match_mirror import Match, step, phase_at, DEFAULT, NAMES, CAGES, SCAVENGE, SIREN, HUNT, \
    CLOSING, SUDDEN, OVER

ok = True


def check(name, cond, detail=""):
    global ok
    ok &= bool(cond)
    print(f"  {'PASS' if cond else 'FAIL'}  {name}{('  ' + detail) if detail else ''}")


print("The shape of a match (docs/01)")
cfg = DEFAULT
edges = []
m = Match()
for _ in range(sum(cfg.values()) + 600):
    before = m.phase
    step(m, cfg)
    if m.phase != before:
        edges.append((m.frame / 60.0, NAMES[m.phase]))
for at, name in edges:
    print(f"   {int(at // 60)}:{at % 60:04.1f}  {name}")

check("the phases arrive in the order docs/01 writes them",
      [n for _, n in edges] == ["scavenge", "siren", "hunt", "closing", "sudden death"])
check("the cages last three seconds", abs(edges[0][0] - 3.0) < 0.02, f"{edges[0][0]:.2f} s")
check("the siren is at 0:25", abs(edges[1][0] - 25.0) < 0.05, f"{edges[1][0]:.2f} s")
check("the rooms start shutting at 1:15", abs(edges[3][0] - 75.0) < 0.05, f"{edges[3][0]:.2f} s")
check("and the whole thing is on one platform by 2:00",
      abs(edges[4][0] - 120.0) < 0.1, f"{edges[4][0]:.2f} s")

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

print("\nThe clock is the same on both devices")
a, b = Match(), Match()
for i in range(4000):
    step(a, cfg)
    step(b, cfg)
check("two clocks from the same table never disagree",
      (a.frame, a.phase) == (b.frame, b.phase), f"frame {a.frame}, {NAMES[a.phase]}")
check("and the phase at a tick can be asked without stepping to it",
      all(phase_at(f, cfg) == phase_at(f, cfg) for f in (0, 179, 180, 1500, 8000)))

print("\n" + ("ALL PASS" if ok else "SOME FAILED"))
