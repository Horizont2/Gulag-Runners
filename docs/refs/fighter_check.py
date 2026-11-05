"""Is each arena's fighter wired to the things only that scene can give it?

A fighter is a prefab, and most of what it needs travels with it. The exceptions are the
things that live in the SCENE and cannot be in a prefab: the HUD's joysticks and buttons,
the MatchState, the world baker, the spawn marker, the blood that plays where it is hit.
Each of those is a reference from the prefab instance out into the scene, and every one
of them has to be made again, by hand, every time an arena is built.

Miss one and nothing breaks. There is no error, no warning, no missing script -- the
field is simply null and the feature it drives quietly does not happen. The Nordic arena
shipped with the HUD's two joysticks and three buttons wired to nobody: the controls drew
themselves, moved under your thumb, and sent their input to no fighter, which from the
outside is a combat HUD that "does not react to anything".

The arenas are each other's reference. Every scene-level field that ANY arena sets is one
the others are expected to set too, and a field that one wires and another leaves empty
is the shape of this fault. That is a comparison rather than a list, so it covers fields
nobody has thought to write down -- including the ones added after this was written.

    python3 fighter_check.py
"""
import math
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import arena_demo_geometry as geometry   # noqa: E402

ASSETS = os.path.join(HERE, "..", "..", "Gulag runners", "Assets")
SCENES = os.path.join(ASSETS, "Scenes")
PRESENT = os.path.join(ASSETS, "_Game", "Presentation")

PLAYER_SCRIPT = "882fe3a922af38d6658c85cd54734955"      # PlayerController
BRAIN_SCRIPT = "a96a27f0eefc27e7b188c1594a03a03d"       # FighterBrain

# The touch controls, which are the one group with a hard rule rather than a comparison:
# the HUD drives whoever is holding the phone, so SOME fighter in the scene has to be
# holding it. None, and the controls are furniture.
CONTROLS = ("stick", "aimStick", "jumpButton", "dodgeButton", "actionButton")

# Overrides that say nothing about wiring.
BORING = re.compile(r"^(m_Local|m_Name|m_RootOrder|m_IsActive|m_Constrain|m_Layer)")


def guid_index():
    out = {}
    for base, dirs, files in os.walk(ASSETS):
        dirs[:] = [d for d in dirs if not d.startswith(".")]
        for f in files:
            if not f.endswith(".meta"):
                continue
            try:
                t = open(os.path.join(base, f), encoding="utf-8", errors="ignore").read()
            except OSError:
                continue
            m = re.search(r"guid: ([0-9a-f]{32})", t)
            if m:
                out[m.group(1)] = os.path.join(base, f[:-5])
    return out


def added_components(docs_text, body, guids, names):
    """Which components this arena ADDS to a fighter, by script name.

    Separate from the wiring, and the harder half to see. PlayerArm, PlayerIK and
    PlayerEffects are not in the fighter prefabs at all: every arena adds its own, so a
    new one has a fighter whose arm nothing points -- the aim stick moves, the fighter
    does not -- and whose hits make no blood. A missing component is not a null field,
    so comparing wiring alone walks straight past it.
    """
    found = set()
    for m in re.finditer(r"addedObject: \{fileID: (-?\d+)\}", body):
        d = re.search(r"--- !u!114 &%s\nMonoBehaviour:\n(.*?)(?=\n--- !u!|\Z)" % m.group(1),
                      docs_text, re.S)
        if not d:
            continue
        g = re.search(r"m_Script: \{fileID: 11500000, guid: ([0-9a-f]{32})", d.group(1))
        if g and g.group(1) in names:
            found.add(names[g.group(1)])
    return found


def script_names():
    out = {}
    for base, dirs, files in os.walk(os.path.join(ASSETS, "_Game")):
        dirs[:] = [d for d in dirs if not d.startswith(".")]
        for f in files:
            if not f.endswith(".cs.meta"):
                continue
            g = re.search(r"guid: ([0-9a-f]{32})",
                          open(os.path.join(base, f), encoding="utf-8").read())
            if g:
                out[g.group(1)] = f[:-8]
    return out


def sized(text, guids, name, problems, notes):
    """Is the fighter in this arena the size the game thinks it is?

    The fighter's height is not a fact about the room, so an arena that overrides the
    fighter's SCALE is overriding something that does not belong to it -- and overriding
    it changes only the art. Nothing else in the project reads that transform:

      the simulation is fixed-point over boxes baked from the ARENA's colliders, so the
      fight runs at the right size whatever the model does;

      SideViewCamera frames on the body height in the fighter's TUNING, not on the
      transform, so the shot is composed for a 0.91 m fighter either way;

      the HUD, the timer and the round counter come from MatchState, which never looks.

    So a bad scale here is the quietest fault in the project. Arena_Mine shipped with its
    two fighters at 0.005 -- a 0.91 m fighter drawn four and a half millimetres tall --
    and what that looks like from the outside is a match that starts, counts down, runs
    its intro and follows two fighters correctly, with nobody on screen.
    """
    for m in re.finditer(r"--- !u!1001 &(-?\d+)\nPrefabInstance:\n(.*?)(?=\n--- !u!|\Z)",
                         text, re.S):
        body = m.group(2)
        g = re.search(r"m_SourcePrefab: \{fileID: \d+, guid: ([0-9a-f]{32})", body)
        if not g:
            continue
        src = guids.get(g.group(1))
        if not src or not src.endswith(".prefab"):
            continue
        try:
            if PLAYER_SCRIPT not in open(src, encoding="utf-8", errors="ignore").read():
                continue
        except OSError:
            continue

        nm = re.search(r"propertyPath: m_Name\n\s+value: ([^\n]*)", body)
        who = nm.group(1).strip() if nm else os.path.basename(src)
        got = {}
        for a in "xyz":
            v = re.search(r"propertyPath: m_LocalScale\.%s\n\s+value: ([-\d.eE+]+)" % a, body)
            if v:
                got[a] = float(v.group(1))
        if not got:
            continue

        # What the prefab itself says, which is what every working arena uses.
        #
        # The ROOT transform, found by walking the documents one at a time. The first
        # version of this matched `Transform:` and then `m_Father: {fileID: 0}` across a
        # dotall gap, which runs straight past the end of the document and picks up a
        # later object's father -- it read Player 1's root as 1 where the file says 0.5,
        # and would have passed an arena that really had scaled it to 1.
        base = 1.0
        for pm in re.finditer(r"--- !u!4 &-?\d+\nTransform:\n(.*?)(?=\n--- !u!|\Z)",
                              open(src, encoding="utf-8", errors="ignore").read(), re.S):
            if not re.search(r"^  m_Father: \{fileID: 0\}$", pm.group(1), re.M):
                continue
            pv = re.search(r"^  m_LocalScale: \{x: ([-\d.eE+]+)", pm.group(1), re.M)
            if pv:
                base = float(pv.group(1))
            break

        worst = max(got.values(), key=lambda v: abs(math.log(max(abs(v), 1e-6) / base)))
        off = worst / base if base else 0.0
        if abs(off - 1.0) < 0.02:
            notes.append(f"  {who} is scaled {worst:g}, which is what its prefab says")
            continue
        problems.append(
            f"{name}: {who} is scaled {worst:g} where its prefab says {base:g} -- "
            f"{off:.3g}x. The fighter's size is a fact about the FIGHTER, and nothing "
            f"else reads this transform: the sim runs on boxes baked from the arena, the "
            f"camera frames on the body height in the tuning, and the HUD comes from "
            f"MatchState. So the match starts, counts down and follows two fighters "
            f"correctly" +
            (" with nobody on screen." if off < 0.2 else
             " with the fighters the wrong size.") +
            " Take the scale override off and let the prefab decide.")


def fighters(text, guids):
    """Each fighter in the scene: (name, {field: is it pointed at something})."""
    out = []
    for m in re.finditer(r"--- !u!1001 &(-?\d+)\nPrefabInstance:\n(.*?)(?=\n--- !u!|\Z)",
                         text, re.S):
        body = m.group(2)
        g = re.search(r"m_SourcePrefab: \{fileID: \d+, guid: ([0-9a-f]{32})", body)
        if not g:
            continue
        src = guids.get(g.group(1))
        if not src or not src.endswith(".prefab"):
            continue
        try:
            if PLAYER_SCRIPT not in open(src, encoding="utf-8", errors="ignore").read():
                continue
        except OSError:
            continue

        name = re.search(r"propertyPath: m_Name\n\s+value: ([^\n]*)", body)
        parts = added_components(text, body, guids, SCRIPTS)
        wired = {}
        for mm in re.finditer(r"propertyPath: (\S+)\n\s+value: ([^\n]*)\n"
                              r"\s+objectReference: \{fileID: (-?\d+)\}", body):
            field = mm.group(1)
            if BORING.match(field):
                continue
            # An array of models is one decision, not eleven findings.
            field = re.sub(r"\.Array\.data\[\d+\]", "[]", field)
            wired[field] = wired.get(field, False) or mm.group(3) != "0"
        out.append((name.group(1).strip() if name else os.path.basename(src),
                    wired, parts, int(m.group(1))))
    return out


def brains(text):
    """{fighter's prefab instance: takeOver} for every FighterBrain in the scene.

    The brain is added per arena rather than carried by the fighter prefab, and whether
    it is DRIVING is a tick box on it that nothing in the game ever sets -- the only
    writer in the project is the setup tool, which deliberately leaves it off so that
    whoever runs it makes the choice. Nobody made it: all three arenas shipped with both
    boxes clear, which means both fighters were waiting to be driven by something.

    One of them is: the scene's own joysticks. The other one is waiting for input that
    arrives over the network, and when the network is not there -- a practice match, a
    pairing that failed -- it waits for ever, standing still, while the player circles it.
    """
    out = {}
    docs = {}
    for m in re.finditer(r"(?m)^--- !u!(\d+) &(-?\d+)( stripped)?\n(.*?)(?=^--- !u!|\Z)",
                         text, re.S):
        docs[int(m.group(2))] = m.group(4)

    for fid, body in docs.items():
        if BRAIN_SCRIPT not in body:
            continue
        self_ = re.search(r"\n  self: \{fileID: (-?\d+)\}", body)
        took = re.search(r"\n  takeOver: (\d)", body)
        on = re.search(r"\n  m_Enabled: (\d)", body)
        if self_ is None:
            continue
        owner = docs.get(int(self_.group(1)), "")
        inst = re.search(r"m_PrefabInstance: \{fileID: (-?\d+)\}", owner)
        if inst is None:
            continue
        driving = took is not None and took.group(1) == "1" \
            and (on is None or on.group(1) == "1")
        key = int(inst.group(1))
        out[key] = out.get(key, False) or driving
    return out


def main(argv):
    wanted = argv or geometry.arena_scenes()
    guids = guid_index()
    global SCRIPTS
    SCRIPTS = script_names()
    problems, notes = [], []

    seen, text_of = {}, {}
    for name in wanted:
        text = open(os.path.join(SCENES, name), encoding="utf-8", errors="ignore").read()
        text_of[name] = text
        seen[name] = fighters(text, guids)
        sized(text, guids, name, problems, notes)

    # What any arena wires is what every arena is expected to wire.
    expected = set()
    for rows in seen.values():
        for _who, wired, *_ in rows:
            expected |= {f for f, on in wired.items() if on}

    for name, rows in seen.items():
        notes.append(f"{name}: {len(rows)} fighter(s)")
        if not rows:
            problems.append(f"{name}: no fighters in it at all")
            continue

        thinking = brains(text_of[name])
        held, driven = [], []

        holders = 0
        for who, wired, _p, fid in rows:
            have = sorted(f for f, on in wired.items() if on)
            bot = thinking.get(fid)
            notes.append(f"  {who}: {', '.join(have) if have else 'nothing wired'}"
                         + ("" if bot is None else
                            f" (brain, {'driving' if bot else 'idle'})"))
            if bot:
                driven.append(who)
            if all(wired.get(c) for c in CONTROLS):
                holders += 1
                held.append(who)
            elif any(wired.get(c) for c in CONTROLS):
                missing = [c for c in CONTROLS if not wired.get(c)]
                problems.append(
                    f"{name}: {who} has some of the touch controls and not others "
                    f"(missing {', '.join(missing)}). Half a control scheme is worse "
                    f"than none: the thumb that works teaches the player the other one "
                    f"is broken.")

        if holders == 0:
            problems.append(
                f"{name}: no fighter has the HUD's controls wired, so the two joysticks "
                f"and three buttons in this scene send their input to nobody. They still "
                f"draw and still move under the thumb, which is why this reads as a "
                f"combat HUD that does not react to anything. Wire "
                f"{', '.join(CONTROLS)} on the TouchInputSource of the local fighter.")
        elif holders > 1:
            problems.append(
                f"{name}: {holders} fighters have the HUD's controls wired, so one thumb "
                f"drives both of them.")

        # And somebody has to be FIGHTING the thumb. There is one set of joysticks in
        # these scenes, so the second fighter is driven by the network or by its brain and
        # by nothing else -- and with no network and no brain it is a statue.
        if not driven:
            problems.append(
                f"{name}: nothing drives the second fighter. One fighter has the scene's "
                f"joysticks and the other is waiting for input over the network, so with "
                f"no opponent connected it stands still while the player walks around it. "
                f"Tick Take Over on the FighterBrain of the fighter that is NOT holding "
                f"the controls — NetMatch switches every brain off when a real opponent "
                f"arrives, so this costs nothing online.")
        elif len(driven) > 1:
            problems.append(
                f"{name}: {len(driven)} fighters are driven by their own brain "
                f"({', '.join(driven)}), so the player has nothing to play.")
        elif held and driven[0] in held:
            problems.append(
                f"{name}: {driven[0]} has both the scene's joysticks and a brain that is "
                f"driving. PlayerInputRouter gives a taking-over source the whole fighter, "
                f"so the thumb is ignored and the player is watching a bot play itself.")

        # The components each arena ADDS, compared the same way.
        mine = set().union(*[p for _w, _x, p, *_ in rows]) if rows else set()
        for other, orows in seen.items():
            if other == name or not orows:
                continue
            theirs = set().union(*[p for _w, _x, p, *_ in orows])
            for part in sorted(theirs - mine):
                problems.append(
                    f"{name}: no fighter here has a {part}, and every fighter in "
                    f"{other} does. It is not in the fighter prefab, so each arena adds "
                    f"its own -- miss it and the feature it drives silently does not "
                    f"happen.")

        # And the comparison: what the other arenas wire and this one does not.
        here = set()
        for _who, wired, *_ in rows:
            here |= {f for f, on in wired.items() if on}
        for field in sorted(expected - here):
            where = [n for n, r in seen.items()
                     if n != name and any(w.get(field) for _x, w, *_ in r)]
            if not where:
                continue
            notes.append(f"  nothing in {name} sets {field}, which "
                         f"{' and '.join(where)} does")

    for n in notes:
        print("  note: " + n if not n.startswith("  ") else "  " + n)
    for p in problems:
        print("  " + p)
    print(f"fighter_check: {len(problems)} problem(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
