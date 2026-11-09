"""Can the player actually hear anything?

The audio in this project is a chain of five links, and every one of them was in place
except that the game was silent. That is the problem with a chain: each link looks
finished on its own, nothing logs an error, and the only symptom is an absence -- which
on a phone, with the volume down, nobody notices for weeks.

  the simulation posts a sound   PlayerController raises Noise, Landed, PickedUp and
      Dropped; AudioManager subscribes to those four itself. Every other event -- a hit,
      a block, a parry, a death, a chest being forced, a jump, a dodge, a ladder -- is
      detected by PlayerEffects for its particles, and NONE of them was posted. So a
      fight could be had in silence while somebody walking across the room was announced.
  the table has a clip for it   SoundTable, filled in on the AudioManager, one row per
      SoundId. A row with no clip is silence rather than an error, on purpose: the table
      is meant to be filled in as the game is built.
  the hearing model lets it through   ranges in metres, and a floor between the two
      fighters muffles rather than blocks.
  the backend has a VOICE to speak with   and this is where it was broken. Every arena's
      voices array held six SLOTS and six nulls -- the shape somebody leaves behind after
      setting the size and before dragging the sources in. Ready asked `Length > 0`, so it
      said yes; NextFreeVoice found no non-null voice and fell through to a null; Play
      returned at its own null check. No warning, because the fallback that would have
      shouted was gated on `Length == 0`.
  and the AudioSource is switched on   the forest's twelve cage and gate sources were
      wired to the right clips, every one of them with its component DISABLED. Unity
      refuses to play a disabled source.

So this walks the chain per scene and fails on any dead link. It cannot hear anything
either -- there is no Unity here -- but every one of those five is a fact in a file.

    python3 audio_check.py
"""
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import arena_demo_geometry as geometry           # noqa: E402

ASSETS = os.path.join(HERE, "..", "..", "Gulag runners", "Assets")
SCENES = os.path.join(ASSETS, "Scenes")
AUDIO = os.path.join(ASSETS, "_Game", "Audio")

# How many voices a fight needs at once. Two fighters, each able to step, swing and be hit
# inside the same tenth of a second, plus the room. Below this, a flurry eats its own
# sounds -- NextFreeVoice steals the oldest voice rather than dropping the newest.
VOICES_AT_LEAST = 4


def read(path):
    with open(path, encoding="utf-8", errors="replace") as f:
        return f.read()


def guid_of(path):
    return re.search(r"guid: ([0-9a-f]{32})", read(path + ".meta")).group(1)


def documents(text):
    out = {}
    for m in re.finditer(r"(?m)^--- !u!(\d+) &(-?\d+)( stripped)?\n(.*?)(?=^--- !u!|\Z)",
                         text, re.S):
        out[int(m.group(2))] = (int(m.group(1)), m.group(4))
    return out


def names(docs):
    out = {}
    for fid, (cls, body) in docs.items():
        if cls != 1:
            continue
        m = re.search(r"m_Name: (.*)", body)
        out[fid] = m.group(1).strip() if m else None
    return out


def sounds_in_table(body):
    """{SoundId number: how many clips} off an AudioManager's inline sound table."""
    out = {}
    block = re.search(r"\n  sounds:\n    entries:\n(.*?)(?=\n  [a-zA-Z]+:)", body, re.S)
    if not block:
        return out
    for row in re.finditer(r"- sound: (\d+)\n(.*?)(?=\n    - sound: |\Z)",
                           block.group(1), re.S):
        clips = re.findall(r"\{fileID: \d+, guid: [0-9a-f]{32}, type: 3\}", row.group(2))
        out[int(row.group(1))] = len(clips)
    return out


def wav_index():
    """guid -> path, for every audio file in the project."""
    out, names = {}, {}
    for where, _dirs, files in os.walk(ASSETS):
        for f in files:
            if not f.endswith((".wav.meta", ".ogg.meta", ".mp3.meta")):
                continue
            g = re.search(r"guid: ([0-9a-f]{32})", read(os.path.join(where, f)))
            if g:
                out[g.group(1)] = os.path.join(where, f[:-5])
                names[g.group(1)] = f[:-9]
    return out, names


def clicks(path):
    """Does this loop wrap cleanly, and is it at a usable level? A reason, or None.

    A loop that clicks at the wrap is the classic fault nobody hears until it ships: it
    is one sample out of 176 400 and it arrives exactly once every eight seconds, which
    the ear reads as a tick in the room rather than as a bug in a file. So it is measured
    rather than trusted -- the step from the last sample to the first, against the
    loudest step inside the waveform. A wrap that is no worse than the signal's own
    steepest moment cannot be heard as an edge.
    """
    try:
        raw = open(path, "rb").read()
        at = raw.index(b"data") + 8
        import struct
        n = (len(raw) - at) // 2
        if n < 1000:
            return None
        s = struct.unpack_from("<%dh" % n, raw, at)
    except Exception:
        return None                       # not a wav this can read; not a fault of the scene

    wrap = abs(s[0] - s[-1])
    inside = max(abs(s[k + 1] - s[k]) for k in range(0, n - 1, 7))
    peak = max(abs(v) for v in s) / 32767.0

    if inside > 0 and wrap > inside:
        return (f"clicks where it wraps: the step from its last sample to its first is "
                f"{wrap}, against {inside} for the steepest moment inside it, so the join "
                f"is the loudest edge in the file and arrives once a loop")
    if peak > 0.98:
        return f"peaks at {peak:.2f}, so it is clipping before the mixer sees it"
    if peak < 0.02:
        return f"peaks at {peak:.3f}, which is silence with a file attached"
    return None


def ids():
    """SoundId name -> number, off the enum."""
    src = read(os.path.join(AUDIO, "SoundId.cs"))
    return {m.group(1): int(m.group(2))
            for m in re.finditer(r"^\s+(\w+) = (\d+),", src, re.M)}


def posted():
    """Every SoundId the game actually asks for, and where from.

    Read off the source rather than listed, because the list is the thing that goes
    stale: a sound added to the table and never posted is a row nobody can hear, and a
    sound posted with no row is silence at the moment it matters.
    """
    out = {}
    for where, _dirs, files in os.walk(os.path.join(ASSETS, "_Game")):
        if os.sep + "Editor" in where:
            continue
        for f in files:
            if not f.endswith(".cs"):
                continue
            src = read(os.path.join(where, f))
            for m in re.finditer(r"SoundId\.(\w+)", src):
                if m.group(1) == "None":
                    continue
                out.setdefault(m.group(1), set()).add(f)
    return out


def main(argv):
    problems, notes = [], []

    manager_guid = guid_of(os.path.join(AUDIO, "AudioManager.cs"))
    backend_guid = guid_of(os.path.join(AUDIO, "UnityAudioBackend.cs"))
    effects_guid = guid_of(os.path.join(ASSETS, "_Game", "Presentation", "PlayerEffects.cs"))
    SOUNDS = ids()
    by_number = {v: k for k, v in SOUNDS.items()}
    wavs, clips_by_guid = wav_index()

    # ---------------------------------------------------------- the code side, once
    asked = posted()
    owned = {"FootstepQuiet", "FootstepLoud", "LandSoft", "LandHard", "ItemPickup",
             "ItemDrop"}
    from_effects = {s for s, files in asked.items() if "PlayerEffects.cs" in files}
    notes.append(f"{len(from_effects)} sound(s) posted by PlayerEffects, "
                 f"{len(owned & set(asked))} by AudioManager itself")

    if not from_effects:
        problems.append(
            "PlayerEffects posts no sounds at all. It is the one component that sees a "
            "hit, a block, a parry, a death and a chest being forced -- it draws the "
            "particles for every one of them -- so if it posts nothing, the FIGHT is "
            "silent while walking around is not.")

    # ---------------------------------------------------------- the scenes
    wanted = argv or geometry.arena_scenes()
    for scene in wanted:
        path = os.path.join(SCENES, scene)
        if not os.path.exists(path):
            continue
        text = read(path)
        docs = documents(text)
        named = names(docs)

        manager = next((b for _f, (c, b) in docs.items()
                        if c == 114 and manager_guid in b), None)
        if manager is None:
            problems.append(f"{scene}: no AudioManager, so nothing in it can make a sound.")
            continue

        # --- the backend and its voices
        backend = next(((f, b) for f, (c, b) in docs.items()
                        if c == 114 and backend_guid in b), None)
        if backend is None:
            problems.append(f"{scene}: the AudioManager has no UnityAudioBackend.")
            continue

        listed = re.search(r"\n  voices:\n((?:  - \{fileID: -?\d+\}\n)*)", backend[1])
        slots = re.findall(r"- \{fileID: (-?\d+)\}", listed.group(1)) if listed else []
        live = []
        for v in slots:
            cls_body = docs.get(int(v))
            if cls_body is None or cls_body[0] != 82:
                continue
            if re.search(r"^  m_Enabled: 0", cls_body[1], re.M):
                continue
            live.append(int(v))

        notes.append(f"{scene}: {len(live)} working voice(s) of {len(slots)} slot(s)")

        if not live and slots:
            problems.append(
                f"{scene}: the backend has {len(slots)} voice slots and not one of them "
                f"holds a working AudioSource, so the whole game is silent -- and quietly: "
                f"Ready is true while every voice is null, so nothing warns. Drop "
                f"AudioSources under the Audio object and list them.")
        elif not slots:
            problems.append(
                f"{scene}: the backend has no voices at all, so nothing can be played.")
        elif len(live) < VOICES_AT_LEAST:
            problems.append(
                f"{scene}: only {len(live)} working voice(s). Two fighters can step, swing "
                f"and be hit inside the same tenth of a second, and a voice that is busy "
                f"is stolen -- so a flurry eats its own sounds.")

        # --- the table, against what the code asks for
        table = sounds_in_table(manager)
        empty = sorted(by_number.get(n, str(n)) for n, c in table.items() if c == 0)
        missing = sorted(s for s in asked
                         if SOUNDS.get(s) is not None and SOUNDS[s] not in table)
        if table:
            notes.append(f"  {len(table)} row(s) in the table, {len(table) - len(empty)} "
                         f"with a clip" + (f"; no clip yet: {', '.join(empty)}" if empty else ""))
        else:
            problems.append(f"{scene}: the AudioManager's sound table is empty.")

        for s in missing:
            problems.append(
                f"{scene}: the game posts {s} and the table has no row for it, so that "
                f"moment is silent. Add the row (Tools > Gulag Runners > Audio fills the "
                f"gaps) even if the clip comes later.")

        # --- the props that play their own sources
        for fid, (cls, body) in sorted(docs.items()):
            if cls != 82:
                continue
            if "m_Resource: {fileID: 0}" in body and "m_audioClip: {fileID: 0}" in body:
                continue                       # a voice, or an empty slot on a prop
            if re.search(r"^  m_Enabled: 1", body, re.M):
                continue
            go = re.search(r"m_GameObject: \{fileID: (-?\d+)\}", body)
            who = named.get(int(go.group(1))) if go else "?"
            problems.append(
                f"{scene}: the AudioSource on {who} has a clip and is DISABLED. Unity "
                f"refuses to play a disabled source, so the prop is silent while looking "
                f"entirely wired.")

        # --- the room itself
        #
        # A bed, not an event: one looping source per arena. Without it the arena is silent
        # between footsteps, and a silent room does not read as quiet -- it reads as a game
        # with the sound off. docs/01 builds the scavenge phase on listening, and listening
        # needs something to listen THROUGH.
        beds = []
        for fid, (cls, body) in sorted(docs.items()):
            if cls != 82 or not re.search(r"^  Loop: 1", body, re.M):
                continue
            if re.search(r"^  m_Enabled: 0", body, re.M):
                continue
            res = re.search(r"m_Resource: \{fileID: \d+, guid: ([0-9a-f]{32})", body)
            awake = re.search(r"^  m_PlayOnAwake: (\d)", body, re.M)
            vol = re.search(r"^  m_Volume: ([\d.]+)", body, re.M)
            go = re.search(r"m_GameObject: \{fileID: (-?\d+)\}", body)
            beds.append((named.get(int(go.group(1))) if go else "?",
                         res.group(1) if res else None,
                         awake.group(1) == "1" if awake else False,
                         float(vol.group(1)) if vol else 1.0))

        if not beds:
            problems.append(
                f"{scene}: nothing loops, so the room is silent between footsteps. One "
                f"AudioSource with Loop and Play On Awake, holding a room tone, is the "
                f"difference between a quiet arena and a game with the sound off.")
        for who, clip, awake, vol in beds:
            notes.append(f"  room tone on {who}: "
                         + (f"{clips_by_guid.get(clip, clip)}" if clip else "NO CLIP")
                         + f", volume {vol:g}" + ("" if awake else ", NOT playing on awake"))
            if clip is None:
                problems.append(
                    f"{scene}: the looping source on {who} has no clip, so it loops "
                    f"nothing.")
            elif not awake:
                problems.append(
                    f"{scene}: the room tone on {who} does not play on awake, and nothing "
                    f"in the game starts it, so it never plays.")
            if clip and clip in wavs:
                bad = clicks(wavs[clip])
                if bad:
                    problems.append(f"{scene}: the room tone on {who} {bad}")

        # --- and somebody to hear it
        if effects_guid not in text:
            notes.append(f"  no PlayerEffects in this scene, so only the AudioManager's "
                         f"own four events make a sound")

    for n in notes:
        print("  note: " + n if not n.startswith("  ") else "  " + n)
    for p in problems:
        print("  " + p)
    print(f"audio_check: {len(problems)} problem(s) across {len(wanted)} scene(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
