"""What each weapon actually is, under direct-aim combat (docs/31).

The old version of this file printed windup, active and recovery frames. None of those
exist any more -- docs/30 measured why they had to go, and docs/31 replaced them -- so
printing them was describing a game nobody can play.

What a weapon IS now is five numbers, and only two of them are typed anywhere:

    arc        weapon.AttackFrames, which is ticks to sweep half a circle
    blade      what hangs past the hand, which is the model you can see
    tip speed  that arc across (arm + blade), which is the fastest it can be moved
    damage     weapon.Damage, scaled by the share of that top speed actually reached
    lock       ticks before it can land again -- its own arc, so one hit per sweep

Note BLADE and not reach. The item's Reach is measured from the middle of the body, so
it already contains half a torso and an arm; the weapon is only the part it adds. The
simulation ran the whole of Reach out from the hand for a while, which made every blade
about 1.5x the model and gave bare fists 0.60 m of steel out of an empty hand.

Everything proportional is a share of BODY HEIGHT, so this reads the height off the
fighter prefabs rather than trusting a default -- a default that disagreed with every
prefab in the project is how the arm came out half its owner's height in the first
place, and this file is where that would be visible.

Run it after changing ItemTable or ArmConfig. Nothing here is a test; arm_test is where
the assertions live. This is the table to argue over.
"""
import glob
import io
import os
import re
import sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
UNITY = os.path.join(ROOT, "Gulag runners")
SIM = os.path.join(UNITY, "Assets", "_Game", "Sim")


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


def fighter_heights():
    """Body height off every fighter prefab a scene actually uses, as {name: metres}.

    Only the ones in use. Assets/Prefabs holds an unreferenced Player.prefab left over
    from before the fighters were halved, and a warning that fires every run about a
    prefab nothing loads is a warning people learn to scroll past.
    """
    scenes = "".join(read(p) for p in
                     glob.glob(os.path.join(UNITY, "Assets", "Scenes", "*.unity")))
    out = {}
    for p in glob.glob(os.path.join(UNITY, "Assets", "Prefabs", "*.prefab")):
        m = re.search(r"^\s*bodyHeight:\s*([\d.]+)\s*$", read(p), re.M)
        if m is None:
            continue
        guid = re.search(r"guid:\s*([0-9a-f]{32})", read(p + ".meta"))
        if guid is None or guid.group(1) not in scenes:
            continue
        out[os.path.basename(p)[:-7]] = float(m.group(1))
    return out


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

    sweep = num(arm, "c.SweepFrames")
    reference = num(arm, "c.ReferenceFrames")
    floor = num(arm, "c.MinSwingPermille")
    lock_floor = num(arm, "c.HitLockFrames")
    arm_pm = num(arm, "c.ArmLengthPermille")
    haft_pm = num(arm, "c.HaftPermille")
    r2b_pm = num(arm, "c.ReachToBladePermille")
    min_pm = num(arm, "c.MinBladePermille")
    max_pm = num(arm, "c.MaxBladePermille")

    run = milli(move, "c.RunSpeed")
    default_height = milli(move, "c.BodyHeight")

    heights = fighter_heights()
    height = min(heights.values()) if heights else default_height

    print(f"a {height} m fighter, arm {height * arm_pm / 1000:.2f} m, running {run} m/s")
    print(f"a weapon of {reference} frames sweeps half a circle in {sweep} ticks, and a "
          f"swing under {floor / 10:.0f}% of that weapon's own")
    print("top tip speed does nothing\n")

    # The number every proportion in combat is a share of has to be the number the
    # fighters are actually built at. It was not, for a long time, and that is the whole
    # of docs/31's scale section.
    odd = {n: h for n, h in heights.items() if abs(h - height) > 0.001}
    if odd or (default_height is not None and abs(default_height - height) > 0.001):
        print("  WARNING: the fighters are not all the same size, so no table below is "
              "true of all of them:")
        for n, h in sorted(heights.items()):
            print(f"    {n}: {h} m")
        if default_height is not None:
            print(f"    MoveConfig.Default(): {default_height} m")
        print()

    def blade(reach, bare):
        if reach <= bare:
            return 0.0
        want = height * haft_pm / 1000.0 + (reach - bare) * r2b_pm / 1000.0
        return max(height * min_pm / 1000.0, min(height * max_pm / 1000.0, want))

    rows = []
    table = sorted(weapons(), key=lambda w: w[1])
    bare = next((w[3] for w in table if w[0] == "Fists"), 0.0)

    head = (f"{'weapon':<14}{'arc':>5}{'reach':>7}{'blade':>7}{'top tip':>9}{'dmg':>6}"
            f"{'lock':>6}{'dps':>7}")
    print(head)
    print("-" * len(head))

    for name, af, dmg, reach in table:
        ticks = max(1, sweep * af // reference)
        b = blade(reach, bare)
        radius = height * arm_pm / 1000.0 + b
        top = (3.14159 / ticks) * radius * 60.0        # metres a second
        lock = max(lock_floor, af)
        dps = dmg / (lock / 60.0)
        rows.append((name, af, reach, b, top, dmg, lock, dps))

    # No verdict column. A label like "punishing" is a judgement dressed as data, and
    # the numbers beside it already say more than it does.
    for name, af, reach, b, top, dmg, lock, dps in rows:
        print(f"{name:<14}{af:>5}{reach:>7.2f}{b:>7.2f}{top:>8.1f}m{dmg:>6.1f}"
              f"{lock:>6}{dps:>7.1f}")

    print()
    print("arc      ticks to sweep half a circle -- the weapon's whole character")
    print("reach    the item's number, from the middle of the body")
    print("blade    what hangs past the hand: the model, and the hitbox, and the same one")
    print("top tip  how fast its tip can be made to travel")
    print("lock     ticks before it may land again: one hit per sweep")
    print("dps      damage per second if every single sweep landed, which none do")

    # The one thing that must stay true of every weapon, since it is the floor the
    # lawnmower rule stands on.
    print()
    bad = [r for r in rows if r[4] <= run]
    if bad:
        print(f"  WARNING: {len(bad)} weapon(s) cannot be swung faster than a man runs, "
              "so carrying them is as dangerous as swinging them:")
        for r in bad:
            print(f"    {r[0]} tops out at {r[4]:.1f} m/s against a {run} m/s run")
    else:
        print(f"  every weapon can be swung faster than a man runs ({run} m/s), which is "
              "what keeps a carried blade harmless.")

    # And the one the player sees: a blade longer than its owner is a pole, not a sword.
    tall = [r for r in rows if r[3] > height]
    if tall:
        print(f"  note: {', '.join(r[0].lower() for r in tall)} are drawn longer than the "
              f"fighter is tall ({height} m), which the cap allows on purpose for polearms.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
