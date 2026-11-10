"""Do the mirrors still say what the game says?

Every test in this folder runs against a PYTHON COPY of the simulation, because there is no
Unity here and no .NET. That is the only way these numbers can be argued about at all -- and
it has one failure mode, which is worse than having no mirror: the copy drifts, the tests go
green, and they are describing a game nobody is playing.

It has already happened. arm_combat carried `shoulder=780` while ArmConfig.Default had been
measured down to 721 -- a shoulder 5 cm higher than the one the fighter has, in the mirror
the bot is tested against.

So this compares the mirrors' own tables, field by field, with the defaults in the C#:

  ArmConfig.Default      against arm_mirror.CFG and arm_combat.ARM
  ItemTable's weapons    against arm_mirror.ITEMS (frames, damage, reach)

Nothing here runs the simulation. It reads two files and subtracts.

    python3 mirror_check.py
"""
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import arm_combat                                        # noqa: E402
import arm_mirror                                        # noqa: E402

SIM = os.path.join(HERE, "..", "..", "Gulag runners", "Assets", "_Game", "Sim")
ARM_CS = os.path.join(SIM, "Core", "ArmConfig.cs")
ITEMS_CS = os.path.join(SIM, "Items", "ItemTable.cs")

# The mirrors' key -> the C# field. Spelled out rather than guessed from the name: a
# mapping that falls back to "near enough" is a mapping that quietly stops checking the
# field whose name changed.
ARM_FIELDS = {
    "shoulder": "ShoulderPermille",
    "arm_length_pm": "ArmLengthPermille",
    "sweep_frames": "SweepFrames",
    "reference_frames": "ReferenceFrames",
    "deadzone": "DeadzonePermille",
    "aim_band": "AimBandPermille",
    "settle_arc": "SettleArcPermille",
    "min_swing": "MinSwingPermille",
    "carry_drop": "CarryDropPermille",
    "aim_relative": "AimRelativePermille",
    "aim_floor_pm": "AimFloorPermille",
    "aim_tilt_pm": "AimTiltPermille",
    "full_sweep_pm": "FullSweepPermille",
    "recovery_pm": "RecoveryPermille",
    "haft_pm": "HaftPermille",
    "reach_to_blade_pm": "ReachToBladePermille",
    "min_blade_pm": "MinBladePermille",
    "max_blade_pm": "MaxBladePermille",
    "body_radius_pm": "BodyRadiusPermille",
    "blade_radius_pm": "BladeRadiusPermille",
    "hit_lock": "HitLockFrames",
    "clash_deflect": "ClashDeflectPermille",
    "clash_lock": "ClashLockFrames",
}


def read(path):
    return open(path, encoding="utf-8", errors="replace").read()


def arm_defaults():
    """Every `c.Field = number;` in ArmConfig.Default."""
    text = read(ARM_CS)
    out = {}
    for m in re.finditer(r"c\.(\w+)\s*=\s*(-?\d+)\s*;", text):
        out[m.group(1)] = int(m.group(2))
    return out


def weapons():
    """The weapon rows of ItemTable: frames, damage in points, reach in metres."""
    text = read(ITEMS_CS)
    out = {}
    for m in re.finditer(r"t\[\(int\)ItemId\.(\w+)\] = new ItemDef\s*\{(.*?)\n            \};",
                         text, re.S):
        name, body = m.group(1), m.group(2)
        reach = re.search(r"Reach\s*=\s*Fix\.FromMilli\((\d+)\)", body)
        damage = re.search(r"Damage\s*=\s*Fix\.FromMilli\((\d+)\)", body)
        frames = re.search(r"AttackFrames\s*=\s*(\d+)", body)
        if not (reach and damage and frames):
            continue
        out[name.lower()] = (int(frames.group(1)), int(damage.group(1)) / 1000.0,
                             int(reach.group(1)) / 1000.0)
    return out


def main():
    problems, checked = [], 0
    defaults = arm_defaults()

    for who, table in (("arm_mirror.CFG", arm_mirror.CFG), ("arm_combat.ARM", arm_combat.ARM)):
        for key, field in sorted(ARM_FIELDS.items()):
            if key not in table:
                continue
            if field not in defaults:
                problems.append(
                    f"{who} carries {key}, and ArmConfig.Default no longer sets "
                    f"{field}. Either the field was renamed or the mirror is describing a "
                    f"dial the game has stopped having.")
                continue
            checked += 1
            if table[key] != defaults[field]:
                problems.append(
                    f"{who}[{key}] is {table[key]} and ArmConfig.{field} is "
                    f"{defaults[field]}. The tests that use this mirror are describing a "
                    f"fighter nobody is playing.")

    table = weapons()
    for name, (frames, damage, reach) in sorted(getattr(arm_mirror, "ITEMS", {}).items()):
        if name not in table:
            problems.append(f"arm_mirror.ITEMS has {name}, which is not a weapon in "
                            f"ItemTable any more.")
            continue
        checked += 3
        want = table[name]
        if (frames, round(damage, 3), round(reach, 3)) != (want[0], round(want[1], 3),
                                                           round(want[2], 3)):
            problems.append(
                f"arm_mirror.ITEMS[{name}] is {(frames, damage, reach)} and ItemTable says "
                f"{want} (frames, damage, reach).")

    for p in problems:
        print("  " + p)
    print(f"mirror_check: {len(problems)} problem(s) across {checked} number(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
