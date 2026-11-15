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


THE COMBAT PASS
---------------
The first sixteen files covered walking, landing, chests and the room. The FIGHT had nothing:
audio_check read 24 rows in the table with a clip in 12, and the twelve without one were
AttackSwingLight, AttackSwingHeavy, AttackHit, AttackHitArmoured, BlockImpact, Parry, Stagger,
Death, Jump, Dodge, LadderGrab and LadderOut — that is, everything a fighter does. docs/38 § 1
is the whole argument and it comes to this: the three other layers of a blow are already built
(hitstop, camera trauma, the weapon trail; docs/20) and all three of them are playing to a
sound that does not exist. A hit with no sound does not land. Added here with the two the gas
announcement posts, PoisonWarn and PoisonVent, which had no clip either.

It is not decoration. docs/01 keeps the other man OFF SCREEN for most of a match — separate
floors, a 2.5D side view, a dark scavenge phase — so sound is the primary information channel
rather than a layer over one, and docs/14 spends the whole hearing model on making it legible:
range by noise level, a quarter of the range at full volume, and a sound from another storey at
×0.45 volume and ×0.6 range, ×0.8 and pitched down to 0.85 again by UnityAudioBackend.

That last number is the constraint these recipes are actually written against. A placeholder
has to survive being made quieter, duller and lower and STILL answer the question the player is
asking, so two pairs are designed as opposites rather than as variations:

  AttackHit vs AttackHitArmoured   flesh is a low thud and a 25 ms crack, over inside 170 ms,
      four fifths of its energy under 500 Hz. Armour is six INHARMONIC partials from 600 Hz to
      4 kHz ringing for a quarter of a second. Volume attenuates; duration and pitchedness do
      not, so "how long did that last and did it have a note in it" is a question a muffled,
      distant clip can still answer. It is the one that matters most: it tells you the other
      man has found armour (docs/03).
  Parry vs BlockImpact   the parry is the brightest thing in the game, high-passed at 380 Hz so
      it has no bottom at all, two detuned sets of partials beating against each other. The
      block is low-passed at 1.1 kHz so it has no top at all, soft-clipped hard, and dead in
      110 ms. Nothing in either overlaps the other's band. The parry rewards the hardest input
      in the game and it has to sound like a reward from across the arena.

Two or three variants wherever a sound fires more than once a fight, because SoundEntry already
picks a clip at random and a single clip on a sound that fires every two seconds is what makes a
game sound cheap — the same reason the walk got Step_Quiet_A and _B. Death gets exactly one: it
happens once a match, and a death that sounds different every time is a death the player has to
learn twice.

Levels are set, not hoped for: every new file is normalised to a stated peak (`make`), so the
mix is a column of numbers in `main` that can be argued with rather than whatever the synthesis
happened to produce. Nothing reaches full scale — a clipping placeholder teaches everybody the
wrong level, and placeholder_audio_check fails on one. Jump and Dodge sit at a fifth of full
scale on purpose: they fire constantly and the job of a constant sound is to not be noticed.
"""
import array, io, math, os, random, struct, uuid, zlib

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                   "..", "..", "Gulag runners", "Assets", "_Game", "Audio", "Placeholder")

# Fixed, so a re-run produces byte-identical files and a diff stays empty unless the recipe
# changed. A placeholder that churns every build is a placeholder nobody will dare replace.
RNG = random.Random(0x6A1A6)

# The importer settings the first sixteen files were committed with, byte for byte, with the
# guid as the only thing that differs between them. Written by the tool rather than left to
# Unity because a .meta Unity invents on import is a .meta that is not in git: the next person
# to clone gets a different guid, every scene reference to the clip dangles, and the table
# empties itself. An existing .meta is never touched — see `meta`.
META = """fileFormatVersion: 2
guid: {guid}
AudioImporter:
  externalObjects: {{}}
  serializedVersion: 7
  defaultSettings:
    serializedVersion: 2
    loadType: 0
    sampleRateSetting: 0
    sampleRateOverride: 44100
    compressionFormat: 1
    quality: 1
    conversionMode: 0
    preloadAudioData: 0
  platformSettingOverrides: {{}}
  forceToMono: 0
  normalize: 1
  preloadAudioData: 0
  loadInBackground: 0
  ambisonic: 0
  3D: 1
  userData:
  assetBundleName:
  assetBundleVariant:
"""


def meta(path):
    """A .meta beside the clip, with a guid that is generated ONCE and then left alone.

    This is the whole reason the tool writes them. A guid is an identity, not a setting: the
    scene's sound table points at `guid: 148f49...`, so a tool that minted a fresh guid on
    every run would silently empty every row it had just filled, and a re-run would show up as
    sixteen deleted assets and sixteen new ones in the diff. So: if there is a .meta, it wins,
    whatever is in it — somebody may have changed the import settings on purpose and that is
    their business, not this tool's.
    """
    at = path + ".meta"
    if os.path.exists(at):
        return False
    with open(at, "w", encoding="utf-8", newline="\n") as f:
        f.write(META.format(guid=uuid.uuid4().hex))
    return True


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

    # Measured off the samples that were actually written, not off the recipe's intention.
    # Nobody here can listen to these, so the numbers are the only review they get.
    peak = max((abs(v) for v in data), default=0) / 32767.0
    rms = (sum(v * v for v in data) / max(1, len(data))) ** 0.5 / 32767.0
    print(f"  {name}.wav  {len(raw) / RATE / 2:.3f} s  peak {peak:.2f}  rms {rms:.3f}"
          + ("  +meta" if meta(path) else ""))
    return path


def env(t, length, attack=0.005, release=0.5, curve=2.0):
    """Attack and decay, both in seconds, with a curved tail."""
    if t < attack:
        return t / attack
    k = (t - attack) / max(1e-6, length - attack)
    return max(0.0, (1.0 - k) ** curve) if release > 0 else 1.0


def noise():
    return RNG.uniform(-1.0, 1.0)


# --------------------------------------------------------------------------------------
# The toolkit the combat pass needed and the first sixteen files did without.
#
# Those sixteen shape noise by multiplying it by a wobble, which is cheap and reads as a
# wobble. Air, cloth, metal and a dead blocked blow are all the same noise through different
# FILTERS, and the difference between the two pairs that have to be told apart at a distance
# is entirely a difference of band, so there was no way to write them without real ones.
#
# Everything here is a closure with its own state rather than a class, which keeps the recipes
# below reading as one expression per layer, the way the originals do.
# --------------------------------------------------------------------------------------

def lowpass(hz):
    """One pole, six dB an octave. Gentle on purpose: air and cloth are broad, not resonant."""
    a = 1.0 - math.exp(-2.0 * math.pi * hz / RATE)
    state = [0.0]

    def step(x):
        state[0] += a * (x - state[0])
        return state[0]

    return step


def highpass(hz):
    """The same pole, subtracted. This is what makes a parry bright and a block dull."""
    low = lowpass(hz)
    return lambda x: x - low(x)


# A Chamberlin state-variable filter goes unstable as its corner approaches a quarter of the
# sample rate, and at 22050 Hz that is not far away. 15.5% of the rate is 3.4 kHz, which is
# comfortably inside the stable region at the Q values used here and above the top of every
# swing and scrape in the file. Anything brighter than that is written as partials instead,
# where there is no filter to go unstable.
SVF_TOP = RATE * 0.155


def bandpass(hz, q=1.2):
    """Two poles with a resonance. A formant, or the one note a struck rung has."""
    f = 2.0 * math.sin(math.pi * min(hz, SVF_TOP) / RATE)
    damp = 1.0 / q
    low, band = [0.0], [0.0]

    def step(x):
        high = x - low[0] - damp * band[0]
        band[0] += f * high
        low[0] += f * band[0]
        return band[0]

    return step


def sweep_bandpass(q=1.2):
    """The same filter with the corner handed in per sample: a swing, or a boot scraping."""
    damp = 1.0 / q
    low, band = [0.0], [0.0]

    def step(x, hz):
        f = 2.0 * math.sin(math.pi * min(hz, SVF_TOP) / RATE)
        high = x - low[0] - damp * band[0]
        band[0] += f * high
        low[0] += f * band[0]
        return band[0]

    return step


def sweep_highpass():
    """A moving corner, for gas that slows down as it leaves the pipe."""
    state = [0.0]

    def step(x, hz):
        a = 1.0 - math.exp(-2.0 * math.pi * hz / RATE)
        state[0] += a * (x - state[0])
        return x - state[0]

    return step


def bell(k, peak_at=0.6, rise=2.0, fall=1.4):
    """Grows, arrives, goes — for anything that passes the ear rather than being struck.

    Zero at both ends by construction, which is not a detail: a buffer that starts or ends on
    a non-zero sample clicks on every single play, and on a sound that fires every two seconds
    that click is the thing the player ends up hearing. placeholder_audio_check measures the
    first and last sample of every file for exactly this.
    """
    if k <= 0.0:
        return 0.0
    if k <= peak_at:
        return (k / peak_at) ** rise
    return max(0.0, 1.0 - (k - peak_at) / max(1e-6, 1.0 - peak_at)) ** fall


def soft(v, drive=1.8):
    """A saturator. On an impact it reads as weight; on a blocked blow, as the top being eaten.

    Bounded to ±1 so the intermediate numbers in a recipe stay readable. The level the file is
    actually written at is set once, afterwards, by `make`.
    """
    return math.tanh(v * drive) / math.tanh(drive)


def ring(t, length, base, ratios, decays, tilt=0.6, phases=None):
    """Struck metal: partials at INHARMONIC ratios, the high ones dying first.

    Harmonic ratios would be a bell or a tuned pipe, which is the one thing a piece of prison
    armour is not. The tilt is why it reads as steel and not as a chime: in real metal the
    upper partials lose their energy fastest, so each one gets a shorter decay than the one
    below it, and what is left after a tenth of a second is a dull note rather than a sparkle.

    Partials above Nyquist are dropped rather than left to alias — an aliased partial lands at
    a frequency nobody chose, and at 22050 Hz that is the difference between a ring and a
    whistle sitting underneath it.
    """
    v = 0.0
    for j, r in enumerate(ratios):
        f = base * r
        if f >= RATE * 0.47:
            continue
        ph = phases[j] if phases else 0.0
        v += math.sin(2 * math.pi * f * t + ph) * (1.0 / (1.0 + tilt * j)) \
            * env(t, length, 0.0006, 1, decays[j])
    return v


def spread(count):
    """A random starting phase per partial.

    With every partial starting at zero the first sample is the sum of all of them, which is a
    spike three times the height of the ring behind it — and since `make` normalises to the
    peak, that spike would make the ring itself inaudible. Scattering the phases is how an
    impact keeps its transient without swallowing the thing the transient announces.
    """
    return [RNG.uniform(0.0, 2.0 * math.pi) for _ in range(count)]


def level(samples, peak):
    """Scale a finished buffer so its loudest sample is exactly `peak`.

    Shape is untouched — it is one multiply — so this decides the MIX and nothing else. Worth
    doing rather than tuning coefficients until the numbers come out right: the levels then sit
    in one column in `main` where they can be compared with each other, which is the only way
    to answer "is the dodge quieter than the kill" without being able to hear either.
    """
    buf = list(samples)
    high = max((abs(v) for v in buf), default=0.0)
    return buf if high < 1e-9 else [v * (peak / high) for v in buf]


def own_stream(name):
    """Give one file its own noise, derived from its name.

    The first sixteen share a single stream in call order, so inserting a seventeenth sound in
    the middle of that list would re-roll every file after it: a one-line change, a diff with
    nine rewritten binaries in it, and no way to tell which one the change was supposed to
    affect. Keyed off the name instead, each file is independent of every other and of the
    order they are generated in. crc32 and not hash(), because hash() is salted per process
    and would produce a different file on every run.
    """
    global RNG
    RNG = random.Random(zlib.crc32(name.encode("utf-8")) ^ 0x6A1A6)


def make(name, peak, recipe, *args, **kw):
    """One file: its own noise, its recipe, its stated level, its .meta.

    The recipe is handed over as a function rather than called by the caller because the stream
    has to be swapped BEFORE the generator starts drawing from it, and a generator expression
    passed as an argument has already been built by then.
    """
    own_stream(name)
    return wav(name, level(recipe(*args, **kw), peak))


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
