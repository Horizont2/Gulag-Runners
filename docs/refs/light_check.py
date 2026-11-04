"""Is the arena lit so the fight reads, or so the screenshot does?

docs/02 builds the picture out of a foreground the fight happens on and a background that
is only there to be looked at, and nothing separates those two for the eye on its own: at
the same contrast and the same colour, a handrail nine metres back reads as something a
fighter could stand on. Aerial perspective is how side-on games have solved that since
parallax scrolling, and docs/08 states the rule in one line -- fog only behind the
structure, never in front of it.

Which is a rule about a NUMBER, and the number is easy to get wrong in a way no
screenshot shows you quickly: fog is distance from the CAMERA, the fight plane is wherever
the fighters happen to stand, and the two are set in different inspectors. Set the fog
start a metre short and the haze is on the fighters' faces; set it long and the backdrop
is as crisp as the fight.

So this measures, per arena: where the camera is, where the fight is, where the fog
begins and ends, and what is behind it.

    python3 light_check.py
"""
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import arena_demo_geometry as geometry           # noqa: E402
from sim_motor_mirror import F                   # noqa: E402

SCENES = os.path.join(HERE, "..", "..", "Gulag runners", "Assets", "Scenes")

# How far behind the fighters the haze must begin. Less and it is on them; a fighter is
# 0.6 m thick and leans, so a metre of clearance is the floor and three is comfortable.
CLEAR_BEHIND = 1.0

# Fog that ends before this much is in front of the fight is just a grey screen.
MIN_DEPTH = 6.0


def field(body, name, cast=float, default=None):
    m = re.search(r"^\s*%s: (.+?)\s*$" % re.escape(name), body, re.M)
    if not m:
        return default
    try:
        return cast(m.group(1))
    except ValueError:
        return default


FOG_SCRIPT = "7a8a4f589d342f24fbade49a21a154f2"      # BK PureVolumetricFog

# How far the cloud may rise ABOVE the lowest walkable surface before the fight is
# happening inside it rather than above it.
#
# Not a clearance below the floor, which is what this asked for first and which was
# wrong: cloud lapping at the arena's underside is the whole point of standing on
# nothing, and the layer being level with the floor slab reads as exactly that. What
# matters is the fighter, who is 0.91 m tall and stands ON the surface -- mist round the
# ankles is weather, mist over the chest is a fight nobody can see.
CLOUD_OVER_FLOOR = 0.3


def volumetric(name, text, scene, problems, notes):
    """The ground-fog layer, and whether the fight happens inside it.

    This fog takes its height from a Terrain, which makes it a thing with a POSITION --
    unlike RenderSettings fog, which is a distance from the camera. Put the terrain a few
    metres too high and the arena fills with cloud; put it too low and there is no
    mountain to be high up on. Neither is visible in the inspector, because the layer is
    the terrain's Y plus a depth set on a different object.
    """
    fogs = [m for m in re.finditer(
        r"--- !u!114 &(-?\d+)\nMonoBehaviour:\n(.*?)(?=\n--- !u!|\Z)", text, re.S)
        if FOG_SCRIPT in m.group(2)]
    if not fogs:
        return

    # Each terrain's own Y, from the transform of the object the Terrain sits on.
    terrains = {}
    for m in re.finditer(r"--- !u!218 &(-?\d+)\nTerrain:\n(.*?)(?=\n--- !u!|\Z)", text, re.S):
        go = re.search(r"m_GameObject: \{fileID: (-?\d+)\}", m.group(2))
        if not go:
            continue
        tr = re.search(r"--- !u!4 &-?\d+\nTransform:\n(?:.*?\n)*?"
                       r"  m_GameObject: \{fileID: %s\}\n(?:.*?\n)*?"
                       r"  m_LocalPosition: \{x: [-\d.eE+]+, y: ([-\d.eE+]+)" % go.group(1),
                       text)
        drawn = re.search(r"m_DrawHeightmap: (\d)", m.group(2))
        terrains[int(m.group(1))] = (float(tr.group(1)) if tr else 0.0,
                                     drawn.group(1) == "1" if drawn else True)

    floors = [F(b[3]) for b in geometry.bake(scene)["solid"]
              if F(b[2]) - F(b[0]) >= 2.0]
    lowest = min(floors) if floors else None

    for m in fogs:
        body = m.group(2)
        height = float((re.search(r"  groundFogHeight: ([-\d.eE+]+)", body)
                        or re.match(r"(0)", "0")).group(1))
        auto = re.search(r"  findActiveTerrainsAutomatically: (\d)", body)
        listed = [int(x) for x in re.findall(r"  - \{fileID: (-?\d+)\}",
                                             (re.search(r"  terrains:\n((?:  - \{fileID: -?\d+\}\n)*)",
                                                        body) or re.match(r"()", "")).group(1) or "")]
        listed = [t for t in listed if t]

        if not listed and (not auto or auto.group(1) == "0"):
            problems.append(
                f"{name}: a volumetric fog with no terrain assigned and automatic "
                f"discovery off. This fog takes its height from a Terrain, so with "
                f"neither it draws nothing at all.")
            continue
        use = listed or list(terrains)
        if not use:
            problems.append(
                f"{name}: a volumetric fog and no Terrain in the scene for it to stand "
                f"on, so nothing is drawn.")
            continue

        for t in use:
            if t not in terrains:
                problems.append(f"{name}: its volumetric fog points at {t}, which is not "
                                f"a Terrain in this scene")
                continue
            base, drawn = terrains[t]
            top = base + height
            notes.append(f"  volumetric fog {base:+.2f} to {top:+.2f} m"
                         + (f", {lowest - top:+.2f} m under the lowest floor "
                            f"at y {lowest:+.2f}" if lowest is not None else "")
                         + (" (terrain drawn)" if drawn else " (terrain not drawn)"))
            if lowest is not None and top > lowest + CLOUD_OVER_FLOOR:
                problems.append(
                    f"{name}: the fog layer reaches y {top:+.2f}, which is "
                    f"{top - lowest:.2f} m over the lowest floor at y {lowest:+.2f}. A "
                    f"fighter is 0.91 m tall, so that is cloud up to their "
                    f"{'chest' if top - lowest > 0.5 else 'knees'} and a fight nobody "
                    f"can see. Lower the terrain, or take the depth off "
                    f"Ground Fog Height.")


def main(argv):
    wanted = argv or geometry.arena_scenes()
    problems, notes = [], []

    camera_guid = None
    for base, dirs, files in os.walk(os.path.join(SCENES, "..", "_Game")):
        dirs[:] = [d for d in dirs if not d.startswith(".")]
        if "SideViewCamera.cs.meta" in files:
            camera_guid = re.search(
                r"guid: ([0-9a-f]{32})",
                open(os.path.join(base, "SideViewCamera.cs.meta"),
                     encoding="utf-8").read()).group(1)

    for name in wanted:
        path = os.path.join(SCENES, name)
        text = open(path, encoding="utf-8", errors="ignore").read()
        scene = geometry.Scene(path)

        render = re.search(r"--- !u!104 &\d+\nRenderSettings:\n(.*?)(?=\n--- !u!|\Z)",
                           text, re.S)
        if not render:
            problems.append(f"{name}: no RenderSettings at all")
            continue
        render = render.group(1)

        on = field(render, "m_Fog", int, 0)
        mode = field(render, "m_FogMode", int, 3)
        start = field(render, "m_LinearFogStart", float, 0.0)
        end = field(render, "m_LinearFogEnd", float, 0.0)
        sun = field(render, "m_Sun", str, "{fileID: 0}")

        # Where the camera stands and where the fight is. The fighters' own z is the
        # fight plane -- that is where PlayerController takes it from.
        spawns = geometry.spawns(scene)
        plane = spawns[0][3] if spawns else None
        camera_z = None
        for go in sorted(scene.gos):
            if camera_guid and scene.script(go, camera_guid) is not None:
                camera_z = scene.basis(scene.tr_of_go[go])[0][2]
                break

        if camera_z is None or plane is None:
            notes.append(f"{name}: no camera or no fighters yet, so there is no fight "
                         f"to measure the fog against")
            continue

        to_fight = abs(plane - camera_z)
        notes.append(f"{name}: camera at z {camera_z:.2f}, fight at z {plane:.2f} "
                     f"-- {to_fight:.2f} m apart")

        if not on:
            problems.append(
                f"{name}: fog is off, so the background is as sharp as the fight and a "
                f"rail nine metres back reads as somewhere to stand")
            continue

        if mode != 1:
            problems.append(
                f"{name}: fog mode is {mode}, not 1 (Linear). Only Linear has a start "
                f"and an end you can put behind the fighters on purpose")

        notes.append(f"  fog {start:.1f}..{end:.1f} m, which is "
                     f"{start - to_fight:+.2f} m from the fight")

        if start < to_fight + CLEAR_BEHIND:
            problems.append(
                f"{name}: fog starts {start:.1f} m from the camera and the fighters are "
                f"{to_fight:.1f} m away, so the haze is on the fight itself. It has to "
                f"begin at least {CLEAR_BEHIND:.0f} m behind them "
                f"({to_fight + CLEAR_BEHIND:.1f} m or more)")

        if end - start < MIN_DEPTH:
            problems.append(
                f"{name}: fog goes from {start:.1f} to {end:.1f} m, which is "
                f"{end - start:.1f} m of ramp. That is a grey wall rather than depth")

        if "fileID: 0}" in sun:
            problems.append(
                f"{name}: RenderSettings has no Sun wired, so ambient and the skybox do "
                f"not know which light is the key one")

        volumetric(name, text, scene, problems, notes)

    for n in notes:
        print("  note: " + n if not n.startswith("  ") else "  " + n)
    for p in problems:
        print("  " + p)
    print(f"light_check: {len(problems)} problem(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
