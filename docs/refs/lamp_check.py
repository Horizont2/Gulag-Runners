"""Do the hung lamps move, and does a room's worth of them move like weather?

A still lamp is not neutral. The fire inside these ones already flickers, so a lamp that
does not move is a lamp making light that moves — which reads worse than neither, and is
the thing the sway shader was written for.

But swaying scenery goes wrong in two directions, and both of them are arithmetic rather
than taste:

  TOO LITTLE, and nothing has been gained. A degree of swing on a lantern hung by a hook
  half a metre above its foot is five millimetres at the bottom, which at the distance
  this game is played from is nothing at all.

  TOGETHER, and it is worse than still. Ten lamps on one sine are a chorus line; the eye
  finds the pattern in about a second and the room stops being a room. The shader answers
  that three ways -- a phase out of each lamp's own position, a period jittered per lamp
  so they drift apart and never re-synchronise, and a gust that TRAVELS, arriving at one
  end of the arena and walking to the other.

So this runs the shader's own arithmetic over the real lamps at their real positions: how
far each one actually moves, how far apart they stay, and how long the wind takes to cross
the room.

    python3 lamp_check.py
"""
import math
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import arena_demo_geometry as geometry           # noqa: E402

SCENES = os.path.join(HERE, "..", "..", "Gulag runners", "Assets", "Scenes")
MATERIALS = os.path.join(HERE, "..", "..", "Gulag runners", "Assets", "_Game", "Materials")
SHADER = "3c7e92a641b58d349af10e5b2d64c7f8"      # Gulag Runners / Lamp Sway

TAU = 6.2831853

# A lamp that moves less than this at its lowest point may as well be welded on. Half a
# centimetre is roughly one screen pixel at the distance this game is played from.
MOVES_AT_LEAST = 0.010

# And one that moves more than this is swinging through the wall it hangs on, or reading
# as a ship's lantern in a gale rather than a lamp in a draught.
MOVES_AT_MOST = 0.070

# How alike two lamps may look. Measured as the correlation of their angles over a minute:
# 1.0 is the chorus line, 0 is unrelated.
TOGETHER = 0.90

# How many of them may be leaning the same way at once, averaged over a minute. Ten
# lamps at unrelated phases sit around 0.25; a shared wind pushes it up a little and
# should; anything near 1 is one object with ten lamps drawn on it.
ALL_ONE_WAY = 0.55


def material(guid):
    for name in sorted(os.listdir(MATERIALS)):
        if not name.endswith(".mat"):
            continue
        path = os.path.join(MATERIALS, name)
        body = open(path, encoding="utf-8", errors="ignore").read()
        if f"m_Shader: {{fileID: 4800000, guid: {SHADER}" in body:
            return name, body
    return None, None


def number(body, field, default):
    m = re.search(r"- %s: ([-\d.eE+]+)" % re.escape(field), body)
    return float(m.group(1)) if m else default


def vector(body, field, default):
    m = re.search(r"- %s: \{r: ([-\d.eE+]+), g: ([-\d.eE+]+), b: ([-\d.eE+]+)" % re.escape(field),
                  body)
    return tuple(float(v) for v in m.groups()) if m else default


def swing(cfg, hook, t):
    """The shader's own arithmetic, in Python. One place to read it, two to run it.

    Kept line for line with LampSway.shader's SwayOffset rather than tidied, because the
    only thing this is for is telling the truth about what that function does.
    """
    seed = math.modf(math.sin(hook[0] * 12.9898 + hook[1] * 78.233
                              + hook[2] * 37.719) * 43758.5453)[0]
    seed = seed + 1.0 if seed < 0 else seed

    period = max(cfg["period"], 0.05) * (1.0 + cfg["jitter"] * (seed * 2.0 - 1.0))
    w = TAU / period
    phase = seed * TAU

    g = TAU / max(cfg["gustPeriod"], 0.1)
    travel = hook[0] / max(cfg["gustTravel"], 0.1)
    gust = 0.5 + 0.5 * math.sin(t * g - travel)
    gust *= 0.65 + 0.35 * math.sin(t * g * 0.41 + 1.7 - travel * 0.5)

    degrees = cfg["still"] + (cfg["gust"] - cfg["still"]) * max(0.0, min(1.0, gust))
    amp = math.radians(degrees)
    along = amp * math.sin(t * w + phase)
    across = amp * cfg["cross"] * math.sin(t * w * 1.03 + phase + TAU / 4)
    return along, across


def correlation(a, b):
    n = len(a)
    ma, mb = sum(a) / n, sum(b) / n
    va = sum((x - ma) ** 2 for x in a)
    vb = sum((x - mb) ** 2 for x in b)
    if va <= 0 or vb <= 0:
        return 1.0
    return sum((a[i] - ma) * (b[i] - mb) for i in range(n)) / math.sqrt(va * vb)


def main(argv):
    name, body = material(SHADER)
    problems, notes = [], []
    if body is None:
        print("  note: nothing in the project uses the lamp sway shader yet")
        print("lamp_check: 0 problem(s)")
        return 0

    pivot = vector(body, "_Pivot", (0.0, 0.575, 0.0))
    cfg = dict(period=number(body, "_Period", 1.19),
               still=number(body, "_StillDegrees", 1.2),
               gust=number(body, "_GustDegrees", 5.0),
               cross=number(body, "_CrossSwing", 0.35),
               gustPeriod=number(body, "_GustPeriod", 7.0),
               gustTravel=number(body, "_GustTravel", 6.0),
               jitter=number(body, "_Jitter", 0.12))
    notes.append(f"{name}: hook at object y {pivot[1]:.3f}, {cfg['period']:.2f} s a swing "
                 f"(a pendulum {9.81 * (cfg['period'] / TAU) ** 2:.2f} m long), "
                 f"{cfg['still']:.1f} to {cfg['gust']:.1f} degrees, a gust every "
                 f"{cfg['gustPeriod']:.0f} s crossing at {cfg['gustTravel']:.0f} m/s")

    guid = re.search(r"guid: ([0-9a-f]{32})",
                     open(os.path.join(MATERIALS, name + ".meta"),
                          encoding="utf-8").read()).group(1)

    wanted = argv or geometry.arena_scenes()
    for scene_name in wanted:
        path = os.path.join(SCENES, scene_name)
        if not os.path.exists(path):
            continue
        text = open(path, encoding="utf-8", errors="ignore").read()
        if guid not in text:
            continue
        scene = geometry.Scene(path)

        lamps = []
        for pos, who in scene.prefab_art():
            if "Lantern" not in who:
                continue
            lamps.append((pos, who))
        if not lamps:
            continue

        # A lamp's own scale, so the hook is where it really is and the swing is measured
        # at the bottom of the real lantern rather than of a unit one.
        scale = 1.0
        m = re.search(r"propertyPath: m_LocalScale\.y\n\s+value: ([-\d.eE+]+)", text)
        if m:
            scale = float(m.group(1))
        arm = pivot[1] * scale

        notes.append(f"{scene_name}: {len(lamps)} lamp(s), hung {arm * 100:.0f} cm above "
                     f"their own feet")

        series, worst, best = [], None, None
        for pos, who in sorted(lamps, key=lambda r: r[0][0]):
            hook = (pos[0], pos[1] + arm, pos[2])
            angles = [swing(cfg, hook, t * 0.05) for t in range(1200)]     # a minute
            peak = max(math.hypot(a, b) for a, b in angles)
            travel = arm * math.sin(peak)
            series.append([a for a, _b in angles])
            if worst is None or travel < worst[0]:
                worst = (travel, who)
            if best is None or travel > best[0]:
                best = (travel, who)
            notes.append(f"  {who:<36} x {hook[0]:+6.2f}  moves {travel * 100:4.1f} cm, "
                         f"peaks at {math.degrees(peak):4.1f} deg")

        if worst[0] < MOVES_AT_LEAST:
            problems.append(
                f"{scene_name}: {worst[1]} only moves {worst[0] * 100:.1f} cm at its "
                f"lowest point, which nobody will see. A lamp that does not move while "
                f"the light it makes flickers reads worse than one that does neither.")
        if best[0] > MOVES_AT_MOST:
            problems.append(
                f"{scene_name}: {best[1]} swings {best[0] * 100:.1f} cm, which is a "
                f"ship's lantern in a gale rather than a lamp in a draught -- and a lamp "
                f"hung on a wall has a wall to swing into.")

        # And the thing the whole design is for: are any two of them the same lamp?
        alike, pair = 0.0, None
        for i in range(len(series)):
            for j in range(i + 1, len(series)):
                c = abs(correlation(series[i], series[j]))
                if c > alike:
                    alike, pair = c, (i, j)
        order = sorted(lamps, key=lambda r: r[0][0])
        notes.append(f"  the two most alike move together {alike * 100:.0f}% "
                     f"({order[pair[0]][1]} and {order[pair[1]][1]})")
        if alike > TOGETHER:
            problems.append(
                f"{scene_name}: {order[pair[0]][1]} and {order[pair[1]][1]} move together "
                f"{alike * 100:.0f}% of the time. Ten lamps on one sine are a chorus "
                f"line: the eye finds it in a second and the room stops being a room. "
                f"Raise Jitter, or move one of them.")

        # And the thing the eye actually catches: are they all leaning the same way at
        # once? Correlation says whether two lamps share a shape over a minute, which a
        # shared wind SHOULD give them. This says whether the room ever reads as one
        # object -- and that is the chorus line, not the correlation.
        # Averaged over the minute, not the worst instant. Ten lamps at unrelated
        # phases all happen to lean the same way about once in five hundred frames, so
        # the worst instant is 100% for any honest design and says nothing; the first
        # version of this rule duly failed the shader it was written to prove. What a
        # chorus line looks like is a HIGH AVERAGE: ten coins flipped together average
        # 1.0, ten flipped apart average about 0.25.
        lean = sum(abs(sum(1 if s[k] >= 0 else -1 for s in series)) / len(series)
                   for k in range(len(series[0]))) / len(series[0])
        notes.append(f"  on average {lean * 100:.0f}% of them lean the same way "
                     f"(ten lamps at unrelated phases average 25%)")
        if lean > ALL_ONE_WAY:
            problems.append(
                f"{scene_name}: {lean * 100:.0f}% of the lamps lean the same way at any "
                f"moment, averaged over a minute. Ten at unrelated phases average 25%. "
                f"This is the room moving as one object, which is what a shared phase "
                f"looks like however many sines are stacked on it.")

        # The wind has to take time to cross the room, or it is not wind.
        xs = [p[0][0] for p in lamps]
        lag = (max(xs) - min(xs)) / max(cfg["gustTravel"], 0.1)
        notes.append(f"  a gust takes {lag:.1f} s to cross the {max(xs) - min(xs):.0f} m "
                     f"between the end lamps")

    for n in notes:
        print("  note: " + n if not n.startswith("  ") else "  " + n)
    for p in problems:
        print("  " + p)
    print(f"lamp_check: {len(problems)} problem(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
