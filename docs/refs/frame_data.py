"""What each weapon actually is, under direct-aim combat (docs/31).

The old version of this file printed windup, active and recovery frames. None of those
exist any more -- docs/30 measured why they had to go, and docs/31 replaced them -- so
printing them was describing a game nobody can play.

What a weapon IS now is four numbers, and only one of them is typed anywhere:

    arc        weapon.AttackFrames, which is ticks to sweep half a circle
    tip speed  that arc across (arm + reach), which is the fastest it can be moved
    damage     weapon.Damage, scaled by the share of that top speed actually reached
    lock       ticks before it can land again -- its own arc, so one hit per sweep

Run it after changing ItemTable or ArmConfig. Nothing here is a test; arm_test is where
the assertions live. This is the table to argue over.
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
    arm = read(os.path.join(SIM, "Core", "ArmConfig.cs"))
    move = read(os.path.join(SIM, "Core", "MoveConfig.cs"))

    arm_len = milli(arm, "c.ArmLength")
    sweep = num(arm, "c.SweepFrames")
    reference = num(arm, "c.ReferenceFrames")
    floor = num(arm, "c.MinSwingPermille")
    lock_floor = num(arm, "c.HitLockFrames")
    run = milli(move, "c.RunSpeed")

    print(f"arm {arm_len} m, a weapon of {reference} frames sweeps half a circle in "
          f"{sweep} ticks")
    print(f"a swing under {floor/10:.0f}% of that weapon's own top tip speed does nothing, "
          f"and a run is {run} m/s\n")

    head = (f"{'weapon':<14}{'arc':>5}{'reach':>7}{'top tip':>9}{'dmg':>6}"
            f"{'lock':>6}{'dps':>7}")
    print(head)
    print("-" * len(head))

    rows = []
    for name, af, dmg, reach in sorted(weapons(), key=lambda w: w[1]):
        ticks = max(1, sweep * af // reference)
        radius = arm_len + reach
        top = (3.14159 / ticks) * radius * 60.0        # metres a second
        lock = max(lock_floor, af)
        dps = dmg / (lock / 60.0)
        rows.append((name, af, reach, top, dmg, lock, dps))

    # No verdict column. A label like "punishing" is a judgement dressed as data, and
    # the numbers beside it already say more than it does.
    for name, af, reach, top, dmg, lock, dps in rows:
        print(f"{name:<14}{af:>5}{reach:>7.2f}{top:>8.1f}m{dmg:>6.1f}{lock:>6}{dps:>7.1f}")

    print()
    print("arc      ticks to sweep half a circle -- the weapon's whole character")
    print("top tip  how fast its tip can be made to travel")
    print("lock     ticks before it may land again: one hit per sweep")
    print("dps      damage per second if every single sweep landed, which none do")

    # The one thing that must stay true of every weapon, since it is the floor the
    # lawnmower rule stands on.
    print()
    bad = [r for r in rows if r[3] <= run]
    if bad:
        print(f"  WARNING: {len(bad)} weapon(s) cannot be swung faster than a man runs, "
              "so carrying them is as dangerous as swinging them:")
        for r in bad:
            print(f"    {r[0]} tops out at {r[3]:.1f} m/s against a {run} m/s run")
    else:
        print(f"  every weapon can be swung faster than a man runs ({run} m/s), which is "
              "what keeps a carried blade harmless.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
