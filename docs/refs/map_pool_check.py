"""Will both phones load the same arena, and will the one they pick exist on a device?

The map pool is part of the protocol, not a setting. Two devices never tell each other
which arena they are in: they deal it from the number the pairing already carries, so
the same seed has to land on the same scene on both. That is cheap and exact, and it
fails in two ways that are invisible until two real phones are in front of you.

  THE SCENE IS NOT IN THE BUILD. A scene missing from the build list loads perfectly in
  the editor and throws on the device, so it works for whoever added the map and nobody
  else. That is the cruellest half of this and the easy half to check.

  THE POOLS DISAGREE. Two builds whose pool assets differ deal different arenas from the
  same seed. What the players see is a match that connects, counts down and never
  starts, which reads as a netcode bug. MapPool.Fingerprint exists so the link can say
  it out loud; this prints it so a build can be compared to another by eye.

It also checks the draw itself -- same answer twice, every enabled map reachable, and
the weights actually in the proportion they claim -- because a chooser that quietly
never picks the second map is a map pool that looks like it works.

    python3 map_pool_check.py
"""
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.join(HERE, "..", "..", "Gulag runners")
ASSETS = os.path.join(PROJECT, "Assets")
POOL = os.path.join(ASSETS, "_Game", "Maps", "MapPool.asset")
BUILD = os.path.join(PROJECT, "ProjectSettings", "EditorBuildSettings.asset")

# How far the measured share of a map may drift from its weight over the sample below.
TOLERANCE = 0.03
SAMPLE = 200000


def read_pool(path):
    text = open(path, encoding="utf-8").read()
    rows, current = [], None
    for line in text.splitlines():
        m = re.match(r"\s*- scene: (.*)$", line)
        if m:
            if current:
                rows.append(current)
            current = dict(scene=m.group(1).strip(), shown="", enabled=0, weight=0)
            continue
        if current is not None:
            for key in ("shown", "enabled", "weight"):
                m = re.match(r"\s+%s: (.*)$" % key, line)
                if m:
                    v = m.group(1).strip()
                    current[key] = v if key == "shown" else int(v)
    if current:
        rows.append(current)
    fallback = re.search(r"^  fallback: (.*)$", text, re.M)
    return rows, (fallback.group(1).strip() if fallback else "")


def choose(rows, fallback, seed):
    """MapPool.Choose, to the bit. Any drift here is a drift between two phones."""
    total = sum(r["weight"] for r in rows if r["enabled"] and r["weight"] > 0)
    if total <= 0:
        return fallback

    mask = (1 << 64) - 1
    x = (seed + 0x9E3779B97F4A7C15) & mask
    x = ((x ^ (x >> 30)) * 0xBF58476D1CE4E5B9) & mask
    x = ((x ^ (x >> 27)) * 0x94D049BB133111EB) & mask
    x ^= x >> 31

    pick = x % total
    for r in rows:
        if not r["enabled"] or r["weight"] <= 0:
            continue
        pick -= r["weight"]
        if pick < 0:
            return r["scene"] or fallback
    return fallback


def build_scenes():
    text = open(BUILD, encoding="utf-8").read()
    out = {}
    for m in re.finditer(r"- enabled: (\d)\n\s+path: (.*)\n", text):
        name = os.path.splitext(os.path.basename(m.group(2).strip()))[0]
        out[name] = m.group(1) == "1"
    return out


def main():
    problems, notes = [], []

    if not os.path.exists(POOL):
        print(f"  map_pool_check: no pool at {POOL}")
        return 1

    rows, fallback = read_pool(POOL)
    build = build_scenes()
    scenes = {os.path.splitext(f)[0]
              for f in os.listdir(os.path.join(ASSETS, "Scenes"))
              if f.endswith(".unity")}

    live = [r for r in rows if r["enabled"] and r["weight"] > 0]
    notes.append(f"{len(rows)} map(s) in the pool, {len(live)} in the draw, "
                 f"fallback {fallback!r}")
    for r in rows:
        notes.append("  %-26s %-18s %s weight %d"
                     % (r["scene"], r["shown"],
                        "ON " if r["enabled"] else "off", r["weight"]))

    # Every map, drawn or not, has to be a real scene that is in the build.
    for r in rows + [dict(scene=fallback, enabled=1, weight=1, shown="")]:
        name = r["scene"]
        if not name:
            problems.append("a pool row has no scene name on it")
            continue
        if name not in scenes:
            problems.append(f"{name!r} is in the pool and there is no such scene")
            continue
        if name not in build:
            problems.append(
                f"{name!r} is in the pool and NOT in the build list. It will load in "
                f"the editor and throw on the device, so it works for whoever added it "
                f"and for nobody else.")
        elif not build[name]:
            problems.append(f"{name!r} is in the build list but switched off")

    if not live:
        notes.append("nothing is in the draw, so every match is the fallback. That is "
                     "the old single-arena behaviour and not a fault.")

    # The draw: same answer twice, every live map reachable, weights honoured.
    import collections
    seen = collections.Counter(choose(rows, fallback, s) for s in range(SAMPLE))
    for s in (0, 1, 7, 12345, 0xFFFFFFFF):
        if choose(rows, fallback, s) != choose(rows, fallback, s):
            problems.append(f"seed {s} does not give the same map twice")

    total = sum(r["weight"] for r in live) or 1
    for r in live:
        share = seen[r["scene"]] / SAMPLE
        want = r["weight"] / total
        if seen[r["scene"]] == 0:
            problems.append(f"{r['scene']!r} is in the draw at weight {r['weight']} and "
                            f"came up 0 times in {SAMPLE} seeds")
        elif abs(share - want) > TOLERANCE:
            problems.append(f"{r['scene']!r} comes up {share * 100:.1f}% of the time, "
                            f"and its weight asks for {want * 100:.1f}%")
        else:
            notes.append(f"  {r['scene']} drawn {share * 100:.1f}% "
                         f"(weight asks {want * 100:.1f}%)")

    # The fingerprint, so one build can be compared to another by eye.
    h = 2166136261
    for r in rows:
        for c in r["scene"]:
            h = ((h ^ ord(c)) * 16777619) & 0xFFFFFFFF
        h = ((h ^ (r["weight"] if r["enabled"] else 0)) * 16777619) & 0xFFFFFFFF
    notes.append(f"pool fingerprint {h:08x} -- two builds that do not print the same "
                 f"number cannot play each other")

    for n in notes:
        print("  note: " + n if not n.startswith("  ") else "  " + n)
    for p in problems:
        print("  " + p)
    print(f"map_pool_check: {len(problems)} problem(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
