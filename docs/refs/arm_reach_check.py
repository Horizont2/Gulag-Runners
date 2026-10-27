"""Can the model's arm actually reach where the simulation puts its hand?

docs/31 makes the arm a vector off the shoulder, and that vector IS the hitbox, so the
drawn hand is an IK goal taken straight out of the simulation. Which means the simulation
is writing a number the rig has to be able to honour -- and nothing checked that it could.

When it cannot, Unity does not complain. It straightens the arm, stretches it by whatever
Arm Stretch allows, and drags the spine out after the goal. CombatTuning's own tooltip
already describes the result: "a body bending into knots at some angles". It was fixed
once, by reasoning about the fighter's height rather than by measuring the rig, and the
number it landed on is still beyond what the arm can do.

So this measures the rig. A humanoid FBX records its whole bind pose in its .meta, bone by
bone, which is enough to say exactly how long the arm is and how high the shoulder sits --
in the model's own units, scaled by whatever the fighter is scaled by.

Two things are measured, and only one of them is a failure.

  the shoulder is where the shoulder is       -- FAILS when it is not
      Get this wrong and the error is not constant: the goal is drawn on a circle around
      the WRONG centre, so how far out of reach it is changes as the stick turns. That is
      the body writhing while the player tilts the arm, which is the complaint this
      started from, and there is nothing to weigh against it -- the shoulder is simply
      where it is.

  the hand is somewhere the hand can go       -- reported, not failed on
      Including the depth the shoulder sits at. The simulation is flat and the goal is in
      the fighter's plane; the shoulder is half a shoulder-width out of it, so the arm is
      always reaching slightly sideways and the straight-line distance is longer than the
      in-plane one.

      Asking for more than the arm has, by the SAME amount at every angle, is an
      over-straight pose rather than a thing that moves while you aim. Closing it costs
      reach, and reach is the fight: taking 7.3 cm off this fighter moved the mace from
      10.1 to 29.7 seconds to kill, because a blow is worth what it has committed when it
      lands and a shorter arm lands it earlier in the arc. So this prints the number and
      what would fit, and leaves the trade to somebody who can re-tune the fight.

Run: python3 arm_reach_check.py
"""

import math
import os
import re
import sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
ASSETS = os.path.join(ROOT, "Gulag runners", "Assets")

# How much of the arm's full length the goal may ask for. Not 1.0: an arm held dead
# straight at every angle is a mannequin, and leaves nothing for the small disagreements
# between a bind pose and a playing one. Unity's own Arm Stretch on this rig is 0.05, so
# anything past 1.05 cannot be reached at all, by anything.
COMFORTABLE = 0.95
IMPOSSIBLE = 1.05


def read(path):
    with open(path, encoding="utf-8", errors="replace") as f:
        return f.read()


# ------------------------------------------------------------------ the rig, measured

def qmul(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return (aw * bx + ax * bw + ay * bz - az * by,
            aw * by - ax * bz + ay * bw + az * bx,
            aw * bz + ax * by - ay * bx + az * bw,
            aw * bw - ax * bx - ay * by - az * bz)


def qrot(q, v):
    x, y, z, w = q
    vx, vy, vz = v
    tx = 2 * (y * vz - z * vy)
    ty = 2 * (z * vx - x * vz)
    tz = 2 * (x * vy - y * vx)
    return (vx + w * tx + (y * tz - z * ty),
            vy + w * ty + (z * tx - x * tz),
            vz + w * tz + (x * ty - y * tx))


# Unity wraps a long line in the middle of a mapping, so the rotation can arrive across
# two lines. A regex that insists on one line silently drops that bone -- and the bone it
# dropped here was the hips, the one every other bone hangs off, which cost the model its
# whole standing height and made this check's first answer nonsense.
BONE = re.compile(
    r"- name: (\S+)\n\s+parentName: ([^\n]*)\n"
    r"\s+position: \{x: ([-\d.e+]+), y: ([-\d.e+]+), z: ([-\d.e+]+)\}\s*\n"
    r"\s+rotation: \{x: ([-\d.e+]+),\s*y: ([-\d.e+]+),\s*z: ([-\d.e+]+),\s*"
    r"w: ([-\d.e+]+)\}")


def bind_pose(meta_text):
    """Every bone's position in the model's own space, from the .meta's bind pose."""
    local, parent = {}, {}
    for name, up, px, py, pz, rx, ry, rz, rw in BONE.findall(meta_text):
        local[name] = ((float(px), float(py), float(pz)),
                       (float(rx), float(ry), float(rz), float(rw)))
        parent[name] = up.strip()

    world = {}

    def solve(name, guard=0):
        if name in world:
            return world[name]
        if guard > 64:
            return ((0.0, 0.0, 0.0), (0.0, 0.0, 0.0, 1.0))
        pos, rot = local[name]
        up = parent.get(name, "")
        if up not in local:
            world[name] = (pos, rot)
        else:
            ppos, prot = solve(up, guard + 1)
            turned = qrot(prot, pos)
            world[name] = ((ppos[0] + turned[0], ppos[1] + turned[1], ppos[2] + turned[2]),
                           qmul(prot, rot))
        return world[name]

    for name in local:
        solve(name)
    return {n: p for n, (p, _r) in world.items()}


def rigs():
    """Every humanoid FBX in the project, by path, with its bind pose."""
    out = {}
    for dirpath, _dirs, files in os.walk(ASSETS):
        for f in sorted(files):
            if not f.endswith(".fbx.meta"):
                continue
            text = read(os.path.join(dirpath, f))
            if "humanDescription" not in text or "- name: RightHand" not in text:
                continue
            pose = bind_pose(text)
            if {"RightArm", "RightHand"} <= set(pose):
                out[os.path.join(dirpath, f[:-5])] = pose
    return out


def measure(pose):
    """Shoulder height, arm reach and shoulder depth, in the model's own units."""
    shoulder = pose["RightArm"]
    hand = pose["RightHand"]

    # The sole, not the lowest bone: an ankle sits above the floor. The model's own
    # origin is the floor -- it is placed at the fighter's feet with no offset, which
    # fighter_pose_check measures separately.
    reach = math.dist(shoulder, hand)
    top = max(p[1] for p in pose.values())
    return {
        "shoulder_y": shoulder[1],
        "shoulder_depth": abs(shoulder[0]),   # the T-pose spreads the arms along X
        "reach": reach,
        "top": top,
    }


# --------------------------------------------------------- what the simulation asks for

def documents(text):
    """Every YAML document in a prefab, by its anchor: {fileID: (classId, body)}."""
    heads = [(m.start(), m.end(), int(m.group(1)), m.group(2))
             for m in re.finditer(r"^--- !u!(\d+) &(-?\d+).*$", text, re.M)]
    out = {}
    for i, (start, after, cls, fid) in enumerate(heads):
        stop = heads[i + 1][0] if i + 1 < len(heads) else len(text)
        out[fid] = (cls, text[after:stop])
    return out


def fighters():
    """Every fighter prefab, with the scale of the object the FIGHTER is on.

    Not the first m_LocalScale in the file, which is some child's and was how this first
    read one fighter at scale 1 and the other at 0.5 -- the same body, measured twice,
    disagreeing by a factor of two.
    """
    out = []
    guid = re.search(r"guid: ([0-9a-f]+)",
                     read(os.path.join(ASSETS, "_Game", "Presentation",
                                       "PlayerController.cs.meta"))).group(1)

    prefabs = os.path.join(ASSETS, "Prefabs")
    if not os.path.isdir(prefabs):
        return out

    for f in sorted(os.listdir(prefabs)):
        if not f.endswith(".prefab"):
            continue
        text = read(os.path.join(prefabs, f))
        docs = documents(text)

        for fid, (cls, body) in sorted(docs.items()):
            if cls != 114 or f"guid: {guid}" not in body:
                continue
            go = re.search(r"m_GameObject: \{fileID: (-?\d+)\}", body)
            if go is None:
                continue
            owner = docs.get(go.group(1), (None, ""))[1]
            scale = 1.0
            for comp in re.findall(r"- component: \{fileID: (-?\d+)\}", owner):
                cls2, cbody = docs.get(comp, (None, ""))
                if cls2 == 4:
                    m = re.search(r"m_LocalScale: \{x: ([-\d.e+]+)", cbody)
                    if m:
                        scale = float(m.group(1))
            out.append((f, scale, used(os.path.join(prefabs, f))))
            break
    return out


def used(prefab_path):
    """Whether any scene puts this prefab in an arena.

    Assets/Prefabs holds a fighter nobody plays -- the 1.8 m one the project was built
    against before the shipping body became 0.91 m. Measuring it is correct and failing
    on it is not: it is not in a scene, so nothing it does can bend anything.
    """
    guid = re.search(r"guid: ([0-9a-f]+)", read(prefab_path + ".meta"))
    if guid is None:
        return False
    scenes = os.path.join(ASSETS, "Scenes")
    if not os.path.isdir(scenes):
        return False
    return any(f"guid: {guid.group(1)}" in read(os.path.join(scenes, f))
               for f in os.listdir(scenes) if f.endswith(".unity"))


def tuning():
    """The arm numbers as they will actually run: the scene's, else the C# defaults."""
    source = os.path.join(ASSETS, "_Game", "Presentation", "CombatTuning.cs")
    text = read(source)
    want = {}
    for field in ("shoulderHeight", "armLength"):
        m = re.search(rf"public float {field} = ([\d.]+)f", text)
        want[field] = float(m.group(1)) if m else None

    move = read(os.path.join(ASSETS, "_Game", "Sim", "Core", "MoveConfig.cs"))
    m = re.search(r"c\.BodyHeight\s*=\s*Fix\.FromMilli\((\d+)\)", move)
    want["bodyHeight"] = int(m.group(1)) / 1000.0 if m else None

    where = "CombatTuning.cs defaults"
    scenes = os.path.join(ASSETS, "Scenes")
    if os.path.isdir(scenes):
        for f in sorted(os.listdir(scenes)):
            if not f.endswith(".unity"):
                continue
            text = read(os.path.join(scenes, f))
            got = {k: re.search(rf"^\s+{k}: ([\d.]+)$", text, re.M)
                   for k in ("shoulderHeight", "armLength", "bodyHeight")}
            if got["shoulderHeight"] is None:
                continue
            for k, m in got.items():
                if m:
                    want[k] = float(m.group(1))
            where = f
            break
    return want, where


def main():
    found = rigs()
    if not found:
        print("arm_reach_check: no humanoid rig in the project to measure.")
        return 1

    want, where = tuning()
    if want["bodyHeight"] is None:
        print("arm_reach_check: could not read BodyHeight out of MoveConfig.cs.")
        return 1

    body = want["bodyHeight"]
    print(f"the simulation, from {where}: body {body:.3f} m, shoulder at "
          f"{want['shoulderHeight']:.3f} of it, arm {want['armLength']:.3f} of it")

    problems = 0

    for name, scale, in_play in fighters():
        # One humanoid rig in the project, so there is no guessing to do. A second one
        # would want the prefab's own model reference followed; say so rather than
        # quietly measuring the wrong body.
        if len(found) > 1:
            print(f"  note - {len(found)} humanoid rigs in the project; measuring "
                  f"{os.path.basename(list(found)[0])} for every fighter")
        rig = measure(found[list(found)[0]])

        shoulder_y = rig["shoulder_y"] * scale
        depth = rig["shoulder_depth"] * scale
        reach = rig["reach"] * scale
        drawn = rig["top"] * scale

        asked_y = want["shoulderHeight"] * body
        asked_arm = want["armLength"] * body

        tag = "" if in_play else "  — in no scene, so measured and not failed on"
        print(f"\n  {name} (scale {scale:g}, model {drawn:.3f} m tall){tag}")
        print(f"      shoulder   model {shoulder_y:.3f} m   simulation {asked_y:.3f} m"
              f"   ({(asked_y - shoulder_y) * 100:+.1f} cm)")
        print(f"      arm        model {reach:.3f} m   simulation {asked_arm:.3f} m"
              f"   ({(asked_arm - reach) * 100:+.1f} cm)")
        print(f"      shoulder sits {depth * 100:.1f} cm out of the fighter's plane")

        # The goal is in the plane, the shoulder is not, and the simulation's shoulder
        # may not be the model's: so the distance the arm is asked to cover depends on
        # where the stick is pointing. Worst case over the whole circle is what matters.
        lift = asked_y - shoulder_y
        worst, worst_at, best = 0.0, 0, 1e9
        for degrees in range(0, 360):
            a = math.radians(degrees)
            dx = asked_arm * math.cos(a)
            dy = lift + asked_arm * math.sin(a)
            d = math.sqrt(dx * dx + dy * dy + depth * depth)
            if d > worst:
                worst, worst_at = d, degrees
            best = min(best, d)

        swing = (worst - best) * 100
        print(f"      so the arm is asked for {best:.3f}-{worst:.3f} m of a {reach:.3f} m "
              f"arm ({best / reach:.0%}-{worst / reach:.0%}), worst at {worst_at}deg")

        # A CONSTANT overshoot is a note, not a failure. It is an over-straight arm at
        # every angle -- a pose, and a pose is a judgement about how the fighter should
        # look, weighed against the reach that closing it costs. The check measures it
        # and says what would fit; it does not get to spend 7 cm of everybody's reach.
        wrong = 0
        if worst > reach * IMPOSSIBLE:
            print(f"      note    - {worst / reach:.0%} of the arm's length cannot be "
                  f"reached at all: Arm Stretch tops out at 5%. The hand is left short "
                  f"and the spine is pulled out after the goal.")
        elif worst > reach * COMFORTABLE:
            print(f"      note    - {worst / reach:.0%} of the arm's length leaves the "
                  f"elbow locked straight and nothing in hand but stretch.")

        # A swing is not a judgement about anything. The goal is drawn on a circle around
        # a point the arm does not hang from, so how far out of reach it sits CHANGES as
        # the stick turns -- the fighter writhing while the player aims. There is nothing
        # to weigh: the shoulder is where the shoulder is.
        if swing > 1.0:
            wrong += 1
            print(f"      {'PROBLEM' if in_play else 'note   '} - and it SWINGS by "
                  f"{swing:.1f} cm as the arm turns, because the simulation's shoulder is "
                  f"{lift * 100:+.1f} cm from the model's. A fixed overshoot is a pose; "
                  f"one that changes with the stick is the body writhing while the player "
                  f"aims.")

        if in_play:
            problems += wrong

        if wrong or worst > reach * COMFORTABLE:
            fits = math.sqrt(max(0.0, (reach * COMFORTABLE) ** 2 - depth * depth))
            # Rounded DOWN, not to nearest: a number printed here is one somebody will
            # type in, and three decimals rounded up land a tenth of a millimetre past
            # the limit this just said not to cross -- which fails the check that
            # recommended it.
            print(f"      -> shoulderHeight {math.floor(shoulder_y / body * 1000) / 1000}"
                  f", armLength {math.floor(fits / body * 1000) / 1000}  (the model's own "
                  f"shoulder, and an arm it reaches at {COMFORTABLE:.0%} without "
                  f"stretching)")

    print(f"\narm_reach_check: {problems} problem(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
