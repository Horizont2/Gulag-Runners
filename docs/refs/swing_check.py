"""Does the hanging cage swing like a cage, and does it stay inside what the sim allows?

A slung cage is the one prop in this game whose stillness is a bug. It hangs on four
chains, and until the pendulum in SaggingSpan it only SAGGED -- which read, in the one
word the fault deserves, as stiff.

Swing is also the most dangerous thing to add here, and for a reason that has nothing to
do with taste. The fight is deterministic fixed-point and the collision under these props
was baked out of their colliders once, at load (docs/06). The swing is a vertex shader
and the sim never sees it, so a cage that swings 30 cm is a cage a fighter is standing
10 cm beside. The budget is roughly ten centimetres, the same as the sag's, and it is a
budget in METRES while every number in the inspector is in DEGREES -- which is exactly
the kind of conversion nobody does in their head while dragging a slider.

So this runs the real integrator -- the same arithmetic as SaggingSpan.Swung, at the
frame rates a phone actually gives -- and answers four questions per span:

    how far does it drift with nobody on it,
    how far does a landing throw it,
    how long does it ring,
    and how many centimetres is that at the end of the arm.

    python3 swing_check.py
"""
import math
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import arena_demo_geometry as geometry           # noqa: E402

SCENES = os.path.join(HERE, "..", "..", "Gulag runners", "Assets", "Scenes")
SAG = "2a9d7e13c6b84f05927ad3e81b5c046f"          # SaggingSpan

G = 9.81

# How far the art may move away from the collision it was baked from. A fighter is 0.6 m
# thick and their feet are on a box that does not move, so ten centimetres is the point
# at which they are visibly standing beside the cage rather than on it.
BUDGET = 0.10

# And a tighter one for the moments a fighter is ACTUALLY ON IT, because the swing is not
# the only lie being told: sag_check.py measures the droop at 8.0 cm of an 11 cm budget,
# straight down. The swing is sideways, so the two make a right angle rather than adding
# -- 8 down and 6 across is 10 cm away from where the collision says the floor is, which
# is the last centimetre there is. With nobody on it the budget is the whole of BUDGET,
# since there is nobody for the art to disagree with.
BUDGET_LOADED = 0.06

# And the floor. Under about half a degree nothing is moving as far as the eye is
# concerned, which is the fault this was written for.
MIN_IDLE = 0.4

# A swing that stops in under a second is a twitch; one that never stops is a fault
# somewhere. Both ends measured as the time to fall to a tenth of the peak.
RING_MIN, RING_MAX = 1.5, 20.0


def vec(body, name, default=(0.0, 0.0, 0.0)):
    m = re.search(r"  %s: \{x: ([-\d.eE+]+), y: ([-\d.eE+]+), z: ([-\d.eE+]+)\}"
                  % re.escape(name), body)
    return tuple(float(v) for v in m.groups()) if m else default


def num(body, name, default=0.0):
    m = re.search(r"^  %s: ([-\d.eE+]+)$" % re.escape(name), body, re.M)
    return float(m.group(1)) if m else default


def run(p, rate, seconds, land_at=None, leave_at=None):
    """SaggingSpan.Swung and the load spring under it, tick for tick.

    Both springs, because the swing reads the load: the air fades out under a fighter and
    the tip follows them along the span, so running the pendulum against a step function
    would measure a cage nobody is standing on.
    """
    dt = 1.0 / rate
    omega = math.sqrt(G / p["pendulumLength"])
    angle = spin = load = vel = 0.0
    last = 0.0
    at = 0.5
    settled = False
    out = []
    for step in range(int(seconds * rate) + 1):
        t = step * dt
        target = 0.0
        if land_at is not None and t >= land_at and (leave_at is None or t < leave_at):
            target = 1.0
            at = 0.5

        vel += (target - load) * p["stiffness"] * dt
        vel -= vel * min(p["damping"] * dt, 1.0)
        load = max(-0.25, min(1.5, load + vel * dt))

        jolt = (target - last) / max(dt, 1e-4) if settled else 0.0
        last = target
        settled = True
        spin += jolt * math.radians(p["landSwing"]) * omega * dt
        spin -= (omega * omega * math.sin(angle)
                 + 2.0 * p["swingDamping"] * omega * spin) * dt
        angle = max(-0.5, min(0.5, angle + spin * dt))

        w = t * (omega * 0.21) + p["phase"]
        air = ((math.sin(w) * 0.65 + math.sin(w * 2.37 + 1.3) * 0.35)
               * math.radians(p["idleSwing"]) * (1.0 - min(max(load, 0.0), 1.0)))
        tip = math.radians(p["leanPerLoad"]) * load * (at - 0.5) * 2.0

        out.append((t, angle + air + tip, angle, load))
    return out


def main(argv):
    wanted = argv or geometry.arena_scenes()
    problems, notes = [], []

    for name in wanted:
        path = os.path.join(SCENES, name)
        if not os.path.exists(path):
            continue
        text = open(path, encoding="utf-8", errors="ignore").read()
        scene = geometry.Scene(path)

        rows = []
        for m in re.finditer(r"--- !u!114 &(-?\d+)\nMonoBehaviour:\n(.*?)(?=\n--- !u!|\Z)",
                             text, re.S):
            body = m.group(2)
            if SAG not in body:
                continue
            go = int(re.search(r"m_GameObject: \{fileID: (-?\d+)\}", body).group(1))
            L = num(body, "pendulumLength")
            follows = re.search(r"  follow: \{fileID: (-?\d+)\}", body)
            rows.append(dict(
                fid=int(m.group(1)),
                who=scene.gos.get(go, {}).get("name", "?"),
                follow=int(follows.group(1)) if follows else 0,
                pendulumLength=L,
                idleSwing=num(body, "idleSwing", 1.2),
                landSwing=num(body, "landSwing", 6.0),
                swingDamping=num(body, "swingDamping", 0.07),
                leanPerLoad=num(body, "leanPerLoad", 2.5),
                stiffness=num(body, "stiffness", 70.0),
                damping=num(body, "damping", 7.0),
                sagDir=vec(body, "sagDir", (0.0, 0.0, 0.0)),
                phase=0.0))

        if not rows:
            continue

        swinging = [r for r in rows if r["pendulumLength"] > 0.0]
        followers = [r for r in rows if r["follow"] != 0]
        notes.append(f"{name}: {len(rows)} sagging span(s), {len(swinging)} of them "
                     f"swinging under their own pendulum, {len(followers)} following one")

        # The rig, which is the half that used to live in a shared material. A span with
        # no sagDir of its own falls back to the material's, which is one object's answer
        # imposed on all of them.
        for r in rows:
            if max(abs(v) for v in r["sagDir"]) < 1e-6:
                problems.append(
                    f"{name}: {r['who']} has no Sag Dir of its own, so it takes whichever "
                    f"object the setup tool last measured for the material it shares. "
                    f"Every one of these props is rotated differently, so that is only "
                    f"right by accident.")

        for r in rows:
            if r["follow"] == 0 or r["pendulumLength"] <= 0.0:
                continue
            problems.append(
                f"{name}: {r['who']} both follows another span and has a pendulum of its "
                f"own ({r['pendulumLength']:.2f} m). Following wins at runtime, so the "
                f"length is a number that looks set and does nothing.")

        for r in swinging:
            L = r["pendulumLength"]
            omega = math.sqrt(G / L)
            period = 2.0 * math.pi / omega

            idle = run(r, 60, 40.0)
            peak_idle = max(abs(a) for _t, a, _b, _l in idle)

            # A landing is a fighter arriving and STAYING. Measured that way because the
            # first version of this check landed them for 50 ms and took them away again,
            # and the two impulses all but cancelled: it read 1.36 degrees off a shot
            # tuned for six, and then blamed the frame rate for the leftovers.
            hit = run(r, 60, 30.0, land_at=1.0)
            peak_hit = max(abs(b) for _t, _a, b, _l in hit)

            # How long it rings: the last moment it is still a tenth of its peak.
            ring = 0.0
            for t, _a, b, _l in hit:
                if abs(b) > peak_hit * 0.1:
                    ring = t - 1.0

            # And in metres at the end of the arm, which is what the sim cannot follow.
            # The TOTAL angle across every shot, pendulum and air and tip together: they
            # add, and taking the largest of the three would quietly under-report the one
            # moment that matters -- a fighter landing near one end.
            off = run(r, 60, 30.0, land_at=1.0, leave_at=12.0)
            worst = max(max(abs(a) for _t, a, _b, _l in shot) for shot in (idle, hit, off))
            travel = L * math.sin(worst)

            # And the same while somebody is standing on it, which is the only case where
            # the art disagreeing with the collision is something a player can see.
            loaded = max((abs(a) for shot in (hit, off) for _t, a, _b, l in shot if l > 0.8),
                         default=0.0)
            on_foot = L * math.sin(loaded)

            # The same shot at the rates a phone gives, because an impulse divided by dt
            # is the classic thing that comes out four times softer at four times the
            # rate -- and it would only ever show on one device.
            rates = {rate: max(abs(b) for _t, _a, b, _l in run(r, rate, 12.0, land_at=1.0))
                     for rate in (30, 60, 144)}
            spread = max(rates.values()) / max(min(rates.values()), 1e-6)

            notes.append(
                f"  {r['who']}: {L:.2f} m arm, {period:.2f} s a swing; idle "
                f"{math.degrees(peak_idle):.2f} deg, a landing {math.degrees(peak_hit):.2f} "
                f"deg, rings {ring:.1f} s, worst {math.degrees(worst):.2f} deg "
                f"= {travel * 100:.1f} cm of travel, {on_foot * 100:.1f} cm of it with "
                f"somebody standing on it")

            if on_foot > BUDGET_LOADED:
                problems.append(
                    f"{name}: {r['who']} swings {on_foot * 100:.1f} cm sideways WHILE A "
                    f"FIGHTER IS ON IT, and sag_check.py has already spent 8 cm of the "
                    f"11 cm budget on the droop. Sideways and down make a right angle, so "
                    f"that is {math.hypot(on_foot * 100, 8.0):.1f} cm away from the floor "
                    f"the collision says they are standing on. Take degrees off Land "
                    f"Swing and Lean Per Load -- Idle Swing is free, it fades out under "
                    f"a load.")
            if travel > BUDGET:
                problems.append(
                    f"{name}: {r['who']} swings {travel * 100:.1f} cm at the end of its "
                    f"arm, and the art may not leave the collision it was baked from by "
                    f"more than {BUDGET * 100:.0f} cm. A fighter standing on it would be "
                    f"standing beside it. Shorten the arm or take degrees off Idle Swing "
                    f"and Land Swing.")
            if math.degrees(peak_idle) < MIN_IDLE:
                problems.append(
                    f"{name}: {r['who']} drifts {math.degrees(peak_idle):.2f} deg with "
                    f"nobody on it, which is nothing -- a hanging thing that holds still "
                    f"reads as welded to the sky, which is the whole fault this was "
                    f"written for.")
            if ring < RING_MIN:
                problems.append(
                    f"{name}: {r['who']} is done swinging {ring:.1f} s after a landing. "
                    f"That is a twitch rather than a swing; drop Swing Damping.")
            if ring > RING_MAX:
                problems.append(
                    f"{name}: {r['who']} is still swinging {ring:.1f} s after a landing, "
                    f"which is a cage nobody can aim at. Raise Swing Damping.")
            if spread > 1.25:
                problems.append(
                    f"{name}: {r['who']} swings {spread:.2f}x further at 144 fps than at "
                    f"30 ({', '.join(f'{k}: {math.degrees(v):.2f} deg' for k, v in rates.items())}). "
                    f"The landing impulse is not frame-rate independent, so the cage "
                    f"behaves differently on a phone and a desk.")

    for n in notes:
        print("  note: " + n if not n.startswith("  ") else "  " + n)
    for p in problems:
        print("  " + p)
    print(f"swing_check: {len(problems)} problem(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
