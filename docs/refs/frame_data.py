"""The frame data, computed from the live config rather than read off a design doc.

Nothing in this project writes the frame data down. It is produced, every swing, by four
numbers multiplying each other in two different files:

    windup   = weapon.AttackFrames * CombatConfig.AttackFramesPermille
    active   = CombatConfig.ActiveFrames
    recovery = windup * RecoveryPermille          (halved again if the blow connected)
    and on the last hit of a string, windup AND recovery are scaled by HeavyTimingPermille

So the hammer's real startup is not the 34 in ItemTable -- it is 34 x 1.45 = 49, and on the
third hit of a string 74. Nobody tuning ItemTable.AttackFrames is looking at those, which is
exactly how a weapon ends up with a second of windup that nobody chose.

This prints what the player actually experiences, at 60 Hz, so the numbers can be argued
with. Run it after changing any of the four.
"""
import io, os, re, sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
SIM = os.path.join(ROOT, "Gulag runners", "Assets", "_Game", "Sim")


def read(p):
    with io.open(p, encoding="utf-8", errors="replace") as fh:
        return fh.read()


def num(src, field):
    m = re.search(r"\b" + re.escape(field) + r"\s*=\s*(-?\d+)\b", src)
    if m is None:
        raise LookupError(field)
    return int(m.group(1))


def milli(src, field):
    m = re.search(re.escape(field) + r"\s*=\s*Fix\.FromMilli\((\d+)\)", src)
    return int(m.group(1)) / 1000.0 if m else None


def scale(v, permille):
    return (v * permille) // 1000


def weapons():
    src = read(os.path.join(SIM, "Items", "ItemTable.cs"))
    out = []
    for block in re.split(r"t\[\(int\)ItemId\.", src)[1:]:
        if "Kind = ItemKind.Weapon" not in block:
            continue
        name = block.split("]")[0].strip()
        af = re.search(r"AttackFrames = (\d+)", block)
        dmg = re.search(r"Damage = Fix\.FromMilli\((\d+)\)", block)
        reach = re.search(r"Reach = Fix\.FromMilli\((\d+)\)", block)
        if af and dmg:
            out.append((name, int(af.group(1)), int(dmg.group(1)) / 1000.0,
                        int(reach.group(1)) / 1000.0 if reach else 0.0))
    return out


def main():
    combat = read(os.path.join(SIM, "Core", "CombatConfig.cs"))
    move = read(os.path.join(SIM, "Core", "MoveConfig.cs"))

    mul = num(combat, "c.AttackFramesPermille")
    active = num(combat, "c.ActiveFrames")
    rec_p = num(combat, "c.RecoveryPermille")
    heavy_p = num(combat, "c.HeavyTimingPermille")
    cut = num(combat, "c.HitRecoveryCutPermille")
    run = milli(move, "c.RunSpeed")

    print(f"windup = AttackFrames x {mul/1000:.3f}   active = {active}   "
          f"recovery = windup x {rec_p/1000:.2f}   (x{cut/1000:.2f} on hit)")
    print(f"last hit of a string: windup and recovery both x {heavy_p/1000:.2f}")
    print(f"run speed {run} m/s, so a swing's total time is also a DISTANCE the "
          f"other fighter covers\n")

    head = (f"{'weapon':<14}{'start':>7}{'act':>5}{'rec':>6}{'whiff':>8}"
            f"{'onhit':>8}{'heavy start':>13}{'he.whiff':>10}{'reach':>7}{'closes':>8}")
    print(head)
    print("-" * len(head))

    worst = []
    for name, af, dmg, reach in sorted(weapons(), key=lambda w: w[1]):
        w = scale(af, mul)
        r = scale(w, rec_p)
        r_hit = max(1, scale(r, cut))
        hw = scale(w, heavy_p)
        hr = scale(scale(hw, rec_p), 1000)
        total = w + active + r
        on_hit = w + active + r_hit
        h_total = hw + active + hr
        # How far a running opponent travels during the startup: the real reason a
        # long windup is not just slow, it is unusable at any range they can leave.
        closes = (w / 60.0) * run
        print(f"{name:<14}{w:>7}{active:>5}{r:>6}{total:>8}{on_hit:>8}"
              f"{hw:>13}{h_total:>10}{reach:>7.2f}{closes:>8.2f}")
        worst.append((name, w, total, h_total, reach, closes))

    print()
    print("start  frames before the hitbox exists      whiff  the whole swing, missed")
    print("act    frames the hitbox exists             onhit  the whole swing, landed")
    print("closes metres a running fighter covers during the startup alone\n")

    # ---- the comparisons that matter, stated as facts rather than opinions
    print("Against the genre, at 60 Hz:")
    print("  a light attack in a 2D fighter or action game starts in 4-10 frames;")
    print("  a heavy, committed one in 14-25; a Dark Souls ultra greatsword is about 30.")
    print()

    slow = [w for w in worst if w[1] > 30]
    if slow:
        print(f"  {len(slow)} of {len(worst)} weapons start slower than 30 frames "
              f"(0.5 s) BEFORE any hitbox:")
        for name, w, total, h, reach, closes in slow:
            print(f"    {name:<14} {w:>3}f startup ({w/60:.2f} s), "
                  f"{total:>3}f whiffed ({total/60:.2f} s), "
                  f"heavy {h:>3}f ({h/60:.2f} s)")
    print()

    # The active window as a share of the commitment: the real "why did I miss" number.
    print("  the hitbox exists for "
          f"{active} frames ({active/60*1000:.0f} ms) on every weapon, so:")
    for name, w, total, h, reach, closes in worst:
        share = active / total * 100
        print(f"    {name:<14} commit {total:>3}f, hitbox live for {share:>4.1f}% of it, "
              f"and they cover {closes:.2f} m while you wind up (reach {reach:.2f} m)")

    unreachable = [w for w in worst if w[5] > w[4]]
    print()
    if unreachable:
        print(f"  {len(unreachable)} of {len(worst)} weapons let a running opponent cover "
              "MORE than the weapon's own reach during the startup -- which means at the")
        print("  edge of your range, a standing start can never connect on someone walking "
              "away. They are only usable as a read, never as a poke.")

    proposal(mul, active, rec_p, heavy_p, cut, run)
    return 0


# The numbers I would move, and what they produce. Here rather than in prose so the
# effect is computed from the same formula the game uses, and so the argument can be
# checked by changing one line.
PROPOSED = dict(mul=1000, active=6, rec_p=520, heavy_p=1300)


def proposal(mul, active, rec_p, heavy_p, cut, run):
    print()
    print("=" * 86)
    print("PROPOSED, against the same formula:")
    print(f"  AttackFramesPermille {mul} -> {PROPOSED['mul']}   "
          f"(the authored AttackFrames become the real startup)")
    print(f"  ActiveFrames         {active} -> {PROPOSED['active']}   "
          f"(the hitbox lives {PROPOSED['active']/60*1000:.0f} ms instead of "
          f"{active/60*1000:.0f})")
    print(f"  RecoveryPermille     {rec_p} -> {PROPOSED['rec_p']}")
    print(f"  HeavyTimingPermille  {heavy_p} -> {PROPOSED['heavy_p']}")
    print()

    head = (f"{'weapon':<14}{'start':>12}{'whiff':>14}{'live %':>14}"
            f"{'closes vs reach':>22}")
    print(head)
    print("-" * len(head))
    fixed = 0
    for name, af, dmg, reach in sorted(weapons(), key=lambda w: w[1]):
        w0 = scale(af, mul); t0 = w0 + active + scale(w0, rec_p)
        w1 = scale(af, PROPOSED["mul"])
        t1 = w1 + PROPOSED["active"] + scale(w1, PROPOSED["rec_p"])
        c0 = (w0 / 60.0) * run
        c1 = (w1 / 60.0) * run
        ok = c1 <= reach
        fixed += 1 if ok else 0
        print(f"{name:<14}{w0:>5} -> {w1:<4}{t0:>7} -> {t1:<6}"
              f"{active/t0*100:>7.1f} -> {PROPOSED['active']/t1*100:<5.1f}"
              f"{c0:>12.2f} -> {c1:.2f} m  vs {reach:.2f}  {'ok' if ok else 'still short'}")

    print()
    print(f"  {fixed} of 12 weapons would out-reach a walking opponent's startup, "
          "against 0 today.")
    print("  Nothing here touches damage, reach or health: the weapons keep their")
    print("  identity and their place in the loot table, they just stop being bets.")


if __name__ == "__main__":
    sys.exit(main())
