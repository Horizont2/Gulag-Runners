"""Placeholder sounds for the components that need one, generated rather than downloaded.

docs/01 builds the whole scavenge phase on sound, and this project has no audio files at all:
every AudioSource in the scene is wired to nothing, so the cage door opens in silence, the
shutters come down in silence, and the siren — the one pulse the game gives you — makes no
noise. A silent wiring is indistinguishable from a broken one, which is the worst state to
leave it in.

These are not sound design. They are short, synthesised, deliberately plain stand-ins so that
every hook can be heard to fire, in the right place, at the right moment — and so that
replacing them later is dropping a file over a file rather than wiring anything.

Run:  cd docs/refs && python3 make_placeholder_audio.py
"""
import array, io, math, os, random, struct

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                   "..", "..", "Gulag runners", "Assets", "_Game", "Audio", "Placeholder")

# Fixed, so a re-run produces byte-identical files and a diff stays empty unless the recipe
# changed. A placeholder that churns every build is a placeholder nobody will dare replace.
RNG = random.Random(0x6A1A6)


def wav(name, samples):
    data = array.array("h")
    for v in samples:
        data.append(max(-32767, min(32767, int(v * 32767))))
    raw = data.tobytes()
    head = b"RIFF" + struct.pack("<I", 36 + len(raw)) + b"WAVE"
    head += b"fmt " + struct.pack("<IHHIIHH", 16, 1, 1, RATE, RATE * 2, 2, 16)
    head += b"data" + struct.pack("<I", len(raw))
    path = os.path.join(OUT, name + ".wav")
    with open(path, "wb") as f:
        f.write(head + raw)
    print(f"  {name}.wav  {len(raw) / RATE / 2:.2f} s")
    return path


def env(t, length, attack=0.005, release=0.5, curve=2.0):
    """Attack and decay, both in seconds, with a curved tail."""
    if t < attack:
        return t / attack
    k = (t - attack) / max(1e-6, length - attack)
    return max(0.0, (1.0 - k) ** curve) if release > 0 else 1.0


def noise():
    return RNG.uniform(-1.0, 1.0)


def clank(length=0.26):
    """Steel struck once: a burst, then a few metallic partials ringing down."""
    n = int(RATE * length)
    partials = [(1870.0, 1.0), (2760.0, 0.6), (4310.0, 0.35), (6190.0, 0.2)]
    for i in range(n):
        t = i / RATE
        v = noise() * env(t, length, 0.001, 1, 28.0) * 0.6
        for f, a in partials:
            v += math.sin(2 * math.pi * f * t) * a * env(t, length, 0.0005, 1, 7.0) * 0.18
        yield v * 0.8


def haul(length=1.30):
    """Something heavy being dragged up: grinding noise, rising, with the catch of a chain."""
    n = int(RATE * length)
    phase = 0.0
    for i in range(n):
        t = i / RATE
        k = t / length
        # Band-limited grind: noise through a slow wobble, plus a low rising tone under it.
        grind = noise() * (0.45 + 0.35 * math.sin(2 * math.pi * 11.0 * t))
        phase += 2 * math.pi * (70.0 + 55.0 * k) / RATE
        body = math.sin(phase) * 0.5
        shape = min(1.0, t / 0.08) * (1.0 - max(0.0, (t - (length - 0.25)) / 0.25))
        yield (grind * 0.35 + body * 0.5) * shape * 0.7


def klaxon(length=0.95):
    """Two tones, alternating. A warning has to be a warning on a phone speaker."""
    n = int(RATE * length)
    for i in range(n):
        t = i / RATE
        f = 415.0 if (t % 0.42) < 0.21 else 311.0
        v = math.sin(2 * math.pi * f * t)
        v += 0.3 * math.sin(2 * math.pi * f * 2 * t)
        gate = 0.0 if (t % 0.42) > 0.34 else 1.0
        edge = min(1.0, (t % 0.42) / 0.012, max(0.0, (0.34 - (t % 0.42)) / 0.012))
        yield v * gate * edge * 0.45


def slam(length=0.80):
    """A slab landing: a low thump with the arena's own crack over it."""
    n = int(RATE * length)
    phase = 0.0
    for i in range(n):
        t = i / RATE
        phase += 2 * math.pi * (105.0 - 58.0 * min(1.0, t / 0.22)) / RATE
        thump = math.sin(phase) * env(t, length, 0.002, 1, 3.2)
        crack = noise() * env(t, length, 0.001, 1, 40.0) * 0.55
        tail = noise() * env(t, length, 0.02, 1, 2.0) * 0.1
        yield (thump * 0.85 + crack + tail) * 0.75


def pulse(length=1.45):
    """The siren: one sweep up, held, and gone. A direction, not an alarm."""
    n = int(RATE * length)
    phase = 0.0
    for i in range(n):
        t = i / RATE
        k = min(1.0, t / 0.55)
        f = 330.0 + 520.0 * (k * k)
        phase += 2 * math.pi * f / RATE
        v = math.sin(phase) + 0.25 * math.sin(phase * 1.5)
        shape = min(1.0, t / 0.04) * (1.0 - max(0.0, (t - 0.75) / (length - 0.75)) ** 1.6)
        yield v * shape * 0.5


def step(length=0.13, bright=1.0, thud=170.0):
    """A foot on concrete. Two of these at different weights is quiet and loud."""
    n = int(RATE * length)
    phase = 0.0
    for i in range(n):
        t = i / RATE
        phase += 2 * math.pi * thud / RATE
        body = math.sin(phase) * env(t, length, 0.001, 1, 9.0)
        grit = noise() * env(t, length, 0.0008, 1, 26.0) * 0.5 * bright
        yield (body * 0.55 + grit) * 0.6


def land(length=0.34, hard=False):
    """Arriving. Soft is a step with weight behind it; hard is the thing docs/01 says carries."""
    n = int(RATE * length)
    phase = 0.0
    for i in range(n):
        t = i / RATE
        phase += 2 * math.pi * (120.0 - 55.0 * min(1.0, t / 0.12)) / RATE
        body = math.sin(phase) * env(t, length, 0.001, 1, 4.5)
        grit = noise() * env(t, length, 0.001, 1, 18.0) * (0.75 if hard else 0.35)
        yield (body * (0.95 if hard else 0.6) + grit) * (0.85 if hard else 0.55)


def pry(length=0.5, loud=False):
    """Working at a chest: wood or steel giving, in short bites. The chest table is a noise
    table (docs/03), so the two levels have to be told apart with the eyes shut."""
    n = int(RATE * length)
    for i in range(n):
        t = i / RATE
        bite = 1.0 if (t % 0.14) < 0.06 else 0.0
        edge = min(1.0, (t % 0.14) / 0.006)
        tone = math.sin(2 * math.pi * (240.0 if loud else 150.0) * t)
        yield (noise() * (0.7 if loud else 0.4) + tone * 0.35) * bite * edge * \
            (1.0 - t / length) * (0.8 if loud else 0.5)


def chest_open(length=0.65):
    """A lid going over: a creak and then the knock of it hitting the back."""
    n = int(RATE * length)
    for i in range(n):
        t = i / RATE
        creak = math.sin(2 * math.pi * (190.0 + 120.0 * t) * t) * 0.35 * \
            (1.0 if t < 0.42 else 0.0) * (0.4 + 0.6 * abs(math.sin(2 * math.pi * 14.0 * t)))
        knock = 0.0
        if t >= 0.42:
            k = t - 0.42
            knock = (math.sin(2 * math.pi * 135.0 * k) * 0.8 + noise() * 0.5) * \
                max(0.0, (1.0 - k / 0.2) ** 3)
        yield (creak + knock) * 0.7


def take(length=0.18, up=True):
    """Something changing hands. Up for a pickup, down for what it displaced."""
    n = int(RATE * length)
    phase = 0.0
    for i in range(n):
        t = i / RATE
        k = t / length
        f = (380.0 + 520.0 * k) if up else (760.0 - 420.0 * k)
        phase += 2 * math.pi * f / RATE
        yield (math.sin(phase) * 0.6 + noise() * 0.18) * env(t, length, 0.002, 1, 2.4) * 0.5


# ---------------------------------------------------------------- the fight
#
# The whole of combat was missing from this file, and from the sound table with it, which
# is how a fight could be had in silence while somebody walking across the room was
# announced. docs/02 asks that a parry and a blocked hit be told apart across the arena;
# that is a demand on these as much as on the particles, so the pair below are deliberately
# different objects rather than two volumes of the same one.


def whoosh(length=0.3, heavy=False):
    """A blade through air: band-limited noise swept down, loudest in the middle of the arc."""
    n = int(RATE * length)
    low = 0.0
    for i in range(n):
        t = i / RATE
        k = t / length
        # A one-pole low-pass whose corner falls as the swing passes: the Doppler of a thing
        # going by, without pretending to model one.
        a = 0.55 - 0.3 * k if not heavy else 0.4 - 0.25 * k
        low += a * (noise() - low)
        shape = math.sin(math.pi * min(1.0, k)) ** (1.4 if heavy else 2.0)
        yield low * shape * (0.85 if heavy else 0.6)


def hit(length=0.34, armoured=False):
    """Landing on somebody. Flesh is a low thump with grit; armour rings over the top."""
    n = int(RATE * length)
    phase = 0.0
    for i in range(n):
        t = i / RATE
        phase += 2 * math.pi * (155.0 - 85.0 * min(1.0, t / 0.1)) / RATE
        body = math.sin(phase) * env(t, length, 0.001, 1, 5.0)
        grit = noise() * env(t, length, 0.0008, 1, 30.0) * 0.6
        ring = 0.0
        if armoured:
            for f, a in ((1420.0, 0.5), (2180.0, 0.3), (3310.0, 0.18)):
                ring += math.sin(2 * math.pi * f * t) * a * env(t, length, 0.0005, 1, 9.0)
        yield (body * 0.8 + grit + ring * 0.5) * 0.7


def ring(length=0.55, bright=1.0):
    """Steel caught on steel. The parry is this one, longer and higher than a block."""
    n = int(RATE * length)
    partials = [(2240.0 * bright, 1.0), (3360.0 * bright, 0.55),
                (5020.0 * bright, 0.3), (6730.0 * bright, 0.16)]
    for i in range(n):
        t = i / RATE
        v = noise() * env(t, length, 0.0005, 1, 45.0) * 0.5
        for f, a in partials:
            v += math.sin(2 * math.pi * f * t) * a * env(t, length, 0.0004, 1, 3.2) * 0.17
        yield v * 0.75


def effort(length=0.26, deep=False):
    """A body doing something: the breath and the cloth, with no voice in it."""
    n = int(RATE * length)
    low = 0.0
    for i in range(n):
        t = i / RATE
        low += (0.22 if deep else 0.45) * (noise() - low)
        yield low * env(t, length, 0.01, 1, 2.6) * (0.55 if deep else 0.4)


def drop_body(length=0.9):
    """Going down for good: the fall, and then nothing moving."""
    n = int(RATE * length)
    phase = 0.0
    for i in range(n):
        t = i / RATE
        phase += 2 * math.pi * (120.0 - 72.0 * min(1.0, t / 0.18)) / RATE
        thump = math.sin(phase) * env(t, length, 0.002, 1, 2.4)
        cloth = noise() * env(t, length, 0.005, 1, 5.0) * 0.35
        second = 0.0
        if t > 0.26:                       # the weight settling after the first impact
            k = t - 0.26
            second = math.sin(2 * math.pi * 88.0 * k) * max(0.0, (1 - k / 0.3) ** 3) * 0.45
        yield (thump * 0.8 + cloth + second) * 0.7


def rung(length=0.16, out=False):
    """A hand or a boot on a ladder. Out is the last one, with the ground under it."""
    n = int(RATE * length)
    phase = 0.0
    for i in range(n):
        t = i / RATE
        phase += 2 * math.pi * (310.0 if not out else 210.0) / RATE
        knock = math.sin(phase) * env(t, length, 0.0008, 1, 14.0)
        grit = noise() * env(t, length, 0.0006, 1, 34.0) * 0.35
        yield (knock * 0.5 + grit) * 0.45


# ---------------------------------------------------------------- the rooms themselves
#
# Nothing in this game loops. Every sound above is an event, which means the arenas are
# silent between footsteps -- and a silent room does not read as quiet, it reads as a
# game with the sound off. docs/01 builds the scavenge phase on listening, and listening
# needs something to listen THROUGH.
#
# These are beds, not events: eight seconds each, played on a loop by one AudioSource per
# arena. Eight is long enough not to be heard repeating and short enough to be a file
# somebody will actually replace.
#
# SEAMLESS IS ARITHMETIC, not luck. A loop that clicks at the wrap is worse than no loop,
# so the tonal parts use frequencies that complete a WHOLE number of cycles in the
# length, and the noise is cross-faded with its own head. Both are checked by
# audio_check, because a click is exactly the kind of thing nobody hears until it is
# shipped.


def seamless(samples, length, fade=0.5):
    """Cross-fades the tail of a noise bed over its own head, so the wrap is continuous."""
    buf = list(samples)
    n = len(buf)
    k = int(RATE * min(fade, length / 3.0))
    for i in range(k):
        a = i / k                       # 0 at the start of the fade, 1 at the end
        head = buf[i]
        buf[n - k + i] = buf[n - k + i] * (1.0 - a) + head * a
    return buf[:n - 0]


def cycles(f, length):
    """The nearest frequency to f that completes whole cycles in length."""
    return max(1, round(f * length)) / length


def room_mine(length=8.0):
    """Underground: moving air, the weight of rock over it, and water finding its way down."""
    n = int(RATE * length)
    out = [0.0] * n
    # Air, as noise through a very low one-pole, which is what a long tunnel does to it.
    low = 0.0
    for i in range(n):
        low += 0.012 * (noise() - low)
        out[i] += low * 3.2
    # Two low tones at whole-cycle frequencies: the room's own note, never quite steady.
    for f, a in ((cycles(47.0, length), 0.1), (cycles(71.0, length), 0.06)):
        for i in range(n):
            t = i / RATE
            out[i] += math.sin(2 * math.pi * f * t) * a * (
                0.75 + 0.25 * math.sin(2 * math.pi * cycles(0.37, length) * t))
    # Drips. On the beat of nothing, which is the point: at whole-second-ish offsets the
    # ear would hear a rhythm and a rhythm is a machine, not a cave.
    for at, pitch in ((1.37, 980.0), (3.11, 1240.0), (4.02, 860.0), (6.73, 1130.0)):
        start = int(RATE * at)
        tail = int(RATE * 0.22)
        for i in range(tail):
            if start + i >= n:
                break
            t = i / RATE
            out[start + i] += (math.sin(2 * math.pi * pitch * t) * 0.5
                               + math.sin(2 * math.pi * pitch * 1.98 * t) * 0.15) \
                * env(t, 0.22, 0.0008, 1, 24.0) * 0.5
    return seamless(out, length)


def room_forest(length=8.0):
    """Outdoors and wet: wind through leaves, with the hiss of rain already falling on them."""
    n = int(RATE * length)
    out = [0.0] * n
    band = 0.0
    prev = 0.0
    for i in range(n):
        t = i / RATE
        # Band-limited hiss: a low-pass of a high-pass, which is as close to leaves as two
        # poles get.
        x = noise()
        band += 0.08 * (x - band)
        high = x - prev
        prev = x
        swell = 0.55 + 0.45 * math.sin(2 * math.pi * cycles(0.21, length) * t) \
            * math.sin(2 * math.pi * cycles(0.067, length) * t)
        out[i] = (band * 1.5 + high * 0.22) * swell * 0.8
    # One branch complaining, somewhere off to the side.
    start = int(RATE * 5.2)
    for i in range(int(RATE * 0.9)):
        if start + i >= n:
            break
        t = i / RATE
        out[start + i] += math.sin(2 * math.pi * (120.0 + 40.0 * t) * t) * 0.12 * \
            env(t, 0.9, 0.08, 1, 1.6) * (0.5 + 0.5 * math.sin(2 * math.pi * 7.0 * t))
    return seamless(out, length)


def room_fortress(length=8.0):
    """High, cold and open: wind with nothing to break it, and rope against a pole."""
    n = int(RATE * length)
    out = [0.0] * n
    band = 0.0
    for i in range(n):
        t = i / RATE
        x = noise()
        band += 0.045 * (x - band)
        # Thinner than the forest and with bigger swells: wind round a wall, not through
        # leaves.
        swell = 0.4 + 0.6 * (0.5 + 0.5 * math.sin(2 * math.pi * cycles(0.13, length) * t))
        out[i] = band * 2.2 * swell * 0.75
        out[i] += math.sin(2 * math.pi * cycles(310.0, length) * t) * 0.012 * swell
    for at in (2.4, 6.1):
        start = int(RATE * at)
        for i in range(int(RATE * 0.3)):
            if start + i >= n:
                break
            t = i / RATE
            out[start + i] += (noise() * 0.5 + math.sin(2 * math.pi * 210.0 * t) * 0.4) * \
                env(t, 0.3, 0.002, 1, 12.0) * 0.22
    return seamless(out, length)


def main():
    os.makedirs(OUT, exist_ok=True)
    print(f"placeholder audio -> {os.path.relpath(OUT)}")
    wav("Cage_Jerk", clank())
    wav("Cage_Haul", haul())
    wav("Shutter_Warn", klaxon())
    wav("Shutter_Slam", slam())
    wav("Siren_Pulse", pulse())

    # The information ladder of docs/01 stands on these: in the blind phase they are the only
    # way to be found. Two of each where the sound repeats, so a walk is not a metronome.
    wav("Step_Quiet_A", step(bright=0.55, thud=150.0))
    wav("Step_Quiet_B", step(bright=0.7, thud=182.0))
    wav("Step_Loud_A", step(length=0.17, bright=1.5, thud=135.0))
    wav("Step_Loud_B", step(length=0.15, bright=1.3, thud=160.0))
    wav("Land_Soft", land())
    wav("Land_Hard", land(0.46, hard=True))
    wav("Chest_Pry_Quiet", pry())
    wav("Chest_Pry_Loud", pry(0.55, loud=True))
    wav("Chest_Open", chest_open())
    wav("Item_Pickup", take())
    wav("Item_Drop", take(0.22, up=False))

    # The fight. Two weights of swing and two of impact, because the table has rows for
    # both and a heavy weapon that sounds like a dagger is a lie the player can hear.
    wav("Swing_Light", whoosh())
    wav("Swing_Heavy", whoosh(0.38, heavy=True))
    wav("Hit_Flesh", hit())
    wav("Hit_Armour", hit(0.4, armoured=True))
    wav("Block_Impact", ring(0.34, bright=0.8))
    wav("Parry", ring(0.62, bright=1.15))
    wav("Stagger", effort(0.34, deep=True))
    wav("Death", drop_body())
    wav("Jump", effort(0.2))
    wav("Dodge", effort(0.24))
    wav("Ladder_Grab", rung())
    wav("Ladder_Out", rung(0.22, out=True))

    # And the rooms, which loop. One per arena, so the three do not sound like one place.
    wav("Room_Mine", room_mine())
    wav("Room_Forest", room_forest())
    wav("Room_Fortress", room_fortress())


if __name__ == "__main__":
    main()
