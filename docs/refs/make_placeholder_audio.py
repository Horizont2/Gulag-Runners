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


if __name__ == "__main__":
    main()
