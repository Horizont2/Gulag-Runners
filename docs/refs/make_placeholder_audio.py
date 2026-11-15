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


# ======================================================================================
# The fight.
# ======================================================================================

def swing(length=0.16, top=2600.0, bottom=900.0, q=1.3, body=0.0, peak_at=0.62):
    """Somebody is swinging, and roughly how heavy the weapon is.

    AIR, not a tone. A swing read as a pitched whoosh is the single most common way a
    placeholder gives itself away, so this is noise through a band whose centre falls as the
    arc comes round, with the amplitude on a bell — it grows, it passes, it is gone. The
    weight of the weapon is two things and neither of them is volume: where the band sits, and
    how long the arc takes. A light blade is 2.6 kHz over 160 ms; a heavy one is 1.3 kHz over
    300 ms with a low body under it, because mass is what you hear below the air.

    docs/20 wants this readable from forty pixels away, which the weapon trail already manages
    visually. Its sound has to manage the same at the far end of the hearing range, so the
    band sweep is wide: a quiet swoosh that falls an octave and a half still reads as a swing
    when a quiet swoosh that holds one note reads as nothing at all.
    """
    n = int(RATE * length)
    band = sweep_bandpass(q)
    hiss = highpass(2400.0)
    phase = 0.0
    for i in range(n):
        t = i / RATE
        k = t / length
        a = bell(k, peak_at, 2.2, 1.5)
        v = band(noise(), top + (bottom - top) * (k ** 0.8)) * 1.6
        v += hiss(noise()) * 0.22
        if body > 0.0:
            phase += 2 * math.pi * (105.0 - 25.0 * k) / RATE
            v += math.sin(phase) * body
        yield v * a


def flesh(length=0.17, f0=320.0, f1=165.0, crack=1.0, drive=2.2):
    """A blow landing on a man. A thud with a short crack on top.

    Three layers and each one is doing a job the other two cannot. The THUD is a sine dropping
    320 Hz to 110 Hz in twenty-two milliseconds — a pitch drop that fast is not heard as a
    pitch at all, it is heard as something soft being displaced. The CRACK is twenty-five
    milliseconds of high noise and it is what makes the blow land on the frame it actually
    lands on; without it the thud arrives late no matter where it starts. The MEAT is noise in
    the middle, and it is the only one of the three that sounds wet.

    THE FIRST VERSION OF THIS DROPPED TO 58 HZ AND WAS WRONG, and the measurement is what said
    so: 95% of its energy below 150 Hz, on a game that ships to phones. A phone speaker is a
    centimetre across and does nothing much below 250 Hz, so the most important sound in the
    game (docs/38 puts AttackHit first of twelve) would have arrived as a faint click on the
    device every player is actually holding. The klaxon in this same file already knew that —
    it sits at 311 and 415 Hz for the same reason. So: the drop ends at 165 Hz instead, the
    thud carries its second and third harmonics at 45% and 18% so there is real content at 330
    and 495 Hz where a small speaker does work, and the meat and the crack were raised until
    an eighth of the energy sits above 400 Hz rather than a hundredth.

    It was not raised further than that on purpose. `armour` is the sound this one has to be
    told apart from and it has 94% of ITS energy between 400 Hz and 1.2 kHz, so the two
    histograms barely overlap at these settings and that is worth more than making this one
    brighter. The measured spectral centres are 404 Hz against 724, and the decays 68 ms
    against 190 — through a floor, pitched to 0.85 and at a third of the volume, that is still
    "the short dark one" against "the long bright one".

    The saturator is the hitstop of docs/20 in audio: a blow slightly over its own ceiling
    reads as a blow that was harder than the speaker. The crack is added AFTER it rather than
    through it — a saturator driven by the thud compresses everything riding on the thud, and
    an earlier attempt lost the crack entirely that way, which measured as nothing at all above
    2 kHz on a sound whose whole job is to arrive on one frame.
    """
    n = int(RATE * length)
    bright = highpass(1800.0)
    wet = lowpass(900.0)
    phase = 0.0
    for i in range(n):
        t = i / RATE
        phase += 2 * math.pi * (f0 + (f1 - f0) * min(1.0, t / 0.022)) / RATE
        thud = (math.sin(phase) + 0.45 * math.sin(2 * phase) + 0.18 * math.sin(3 * phase)) \
            * env(t, length, 0.0012, 1, 7.0)
        meat = wet(noise()) * env(t, length, 0.001, 1, 12.0) * 3.5
        slap = bright(noise()) * env(t, length, 0.0004, 1, 30.0) * 3.0 * crack
        yield soft(thud * 0.80 + meat, drive) * 0.85 + slap * 0.40


def armour(length=0.30, base=640.0, ratios=(1.0, 1.72, 2.41, 3.33, 4.62, 6.15), thump=0.5):
    """METAL. The one sound in the game that has to be unmistakable from the clip beside it.

    This tells the player the other man found armour (docs/03), which changes what he should do
    next, and a sound that only answers that question at close range answers it too late. So
    the distinction from `flesh` is deliberately not a matter of brightness or level, both of
    which the hearing model takes away: it is DURATION and PITCHEDNESS. Flesh is over in 170 ms
    and has no note in it. This is six inharmonic partials from 640 Hz to 3.9 kHz ringing for a
    quarter of a second. Quieter, duller and a sixth lower — which is exactly what the muffled
    path does to it — it is still the long one with a note in it.

    A dull thump stays underneath at half level. A plate takes the blow but the man behind it
    still gets hit, and dropping the low end entirely made this read as a dropped spanner.
    """
    n = int(RATE * length)
    ph = spread(len(ratios))
    # Only 2.0 per partial rather than 3.0, so the upper ones live long enough to be heard.
    # The first attempt tilted them away hard and measured nothing above 2 kHz, which is a
    # sound describable as "a dull note" rather than as metal.
    decays = tuple(3.0 + 2.0 * j for j in range(len(ratios)))
    scrape = highpass(2600.0)
    phase = 0.0
    for i in range(n):
        t = i / RATE
        v = noise() * env(t, length, 0.0004, 1, 80.0) * 0.7           # the strike itself
        v += ring(t, length, base, ratios, decays, 0.35, ph) * 0.42
        v += scrape(noise()) * env(t, length, 0.0008, 1, 24.0) * 0.5  # steel on steel
        phase += 2 * math.pi * (180.0 - 60.0 * min(1.0, t / 0.03)) / RATE
        v += math.sin(phase) * env(t, length, 0.0015, 1, 9.0) * thump
        yield soft(v, 1.4)


def block(length=0.13, f0=240.0, f1=155.0, knock=330.0, drive=2.6):
    """A hit that did not work. Absorbed, dead, no ring.

    Everything here is subtraction. The low thud of `flesh` but with a decay four times as
    fast, so there is no tail to hear; one knock at 230 Hz damped so hard it is a click rather
    than a note; noise with the top taken off it. Then a heavy saturator, which flattens the
    transient — a blocked blow is the one impact in the game whose attack should be WORSE
    defined than its body, because that is what absorbing something sounds like.

    And the whole thing low-passed at 1.1 kHz, which is the rule that keeps it apart from
    `parry`: the parry has nothing below 380 Hz and this has nothing above 1.1 kHz, so the two
    cannot blur into each other at any distance or through any floor. They are the two possible
    answers to the same input and the player has about 100 ms to tell which one he got.

    240 Hz down to 155 rather than the 150 to 70 this started at, with a second harmonic, for
    the reason written out at length in `flesh`: a phone speaker does not move much below
    250 Hz, and a dead sound that is also inaudible is not dead, it is missing. Deadness is a
    property of the DECAY — 31 ms to −30 dB, measured — not of the band, so this can be dragged
    up into the register a phone reproduces without becoming a ring.
    """
    n = int(RATE * length)
    dull = lowpass(900.0)
    cap = lowpass(1100.0)
    phase = 0.0
    for i in range(n):
        t = i / RATE
        phase += 2 * math.pi * (f0 + (f1 - f0) * min(1.0, t / 0.02)) / RATE
        v = (math.sin(phase) + 0.45 * math.sin(2 * phase)) * env(t, length, 0.002, 1, 14.0) * 0.9
        v += dull(noise()) * env(t, length, 0.001, 1, 20.0) * 2.6
        v += math.sin(2 * math.pi * knock * t) * env(t, length, 0.001, 1, 30.0) * 0.45
        yield cap(soft(v, drive))


def parry(length=0.34, base=1180.0):
    """The best sound in the game, because it rewards the hardest input in the game.

    Bright, short, metallic, and with NO BOTTOM AT ALL — high-passed at 380 Hz, which is the
    one decision that makes this the opposite of `block` rather than a louder version of it.
    Seven inharmonic partials from 1.2 kHz to 6.2 kHz, and then the same seven again detuned by
    six parts in a thousand, so the two sets beat slowly against each other. That beating is
    the whole trick: it is what two edges of steel actually do, and it is why this reads as a
    ring and not as a chime.

    The decays are slowest on the second and third partials rather than the first, so the ring
    settles onto a note slightly above where it started. A parry should sound like it went UP.
    """
    ratios = (1.0, 1.51, 2.09, 2.72, 3.47, 4.31, 5.22)
    decays = (6.0, 3.4, 3.0, 4.5, 7.0, 10.0, 14.0)
    n = int(RATE * length)
    ph = spread(len(ratios))
    off = [p + RNG.uniform(-0.4, 0.4) for p in ph]
    top = highpass(380.0)
    for i in range(n):
        t = i / RATE
        v = noise() * env(t, length, 0.0003, 1, 120.0) * 0.5
        v += ring(t, length, base, ratios, decays, 0.45, ph) * 0.5
        v += ring(t, length, base * 1.006, ratios, decays, 0.45, off) * 0.38
        yield top(v)


def stagger(length=0.32, grunt=118.0, top=1700.0, bottom=700.0):
    """A stumble: a scrape, and then a man making a noise about it.

    Two events in the right order, which is what makes it a stumble rather than a sound effect.
    The boot goes first — noise through a band falling 1.7 kHz to 700 Hz, chopped into bites at
    21 Hz so it catches rather than hisses — and the grunt comes in forty milliseconds behind
    it, because you lose your footing and THEN you react to it.

    The grunt is not a voice and is not trying to be one: a buzzy 118 Hz with its second and
    third harmonics, pushed through two fixed resonances at 560 and 1080 Hz. Two formants in
    roughly the right place is enough for the ear to file a noise as "a person", and it is the
    honest amount of effort to put into a placeholder for something that will be recorded.
    """
    n = int(RATE * length)
    scr = sweep_bandpass(1.4)
    f1, f2 = bandpass(560.0, 2.6), bandpass(1080.0, 3.0)
    phase = 0.0
    for i in range(n):
        t = i / RATE
        k = min(1.0, t / 0.24)
        bite = 0.35 + 0.65 * (1.0 if (t % 0.047) < 0.03 else 0.0)
        v = scr(noise(), top + (bottom - top) * k) * bite \
            * bell(min(1.0, t / 0.26), 0.25, 1.4, 1.1) * 1.4
        if t > 0.04:
            phase += 2 * math.pi * grunt / RATE
            buzz = math.sin(phase) + 0.5 * math.sin(2 * phase) + 0.3 * math.sin(3 * phase)
            shape = bell(min(1.0, (t - 0.04) / 0.26), 0.3, 1.6, 1.3)
            v += (f1(buzz) * 0.8 + f2(buzz) * 0.45) * shape * 0.9
        yield v * 0.8


def death(length=1.05):
    """A collapse. Low, longer, finished — and unmistakable at any distance.

    This is the only sound in the game that ends a match, so it is the only one allowed to be a
    second long, and the length is doing most of the work: everything else a fighter does is
    over inside a third of a second, so "the long low one" needs no further identification even
    arriving through two floors at the edge of the hearing range.

    Four layers. The FALL, a sine from 250 Hz to 85 Hz with its second and third harmonics
    under it — the harmonics are not decoration, they are what a phone speaker actually
    reproduces, and the balance here is the one set by measurement rather than by taste (see
    `flesh`: the first version of this put 100% of its energy below 500 Hz and would have been
    the quietest sound in the game on the hardware it ships to). The DEBRIS over the first
    third of a second, loud enough to count. The SETTLE at 260 ms, a second smaller thud,
    which is the layer that makes this a body coming to rest rather than a drum. And a low
    RUMBLE under all of it for the full second, which is what "finished" sounds like.
    """
    n = int(RATE * length)
    rumble = lowpass(160.0)
    bits, cap = highpass(350.0), lowpass(2600.0)
    phase = settle_phase = 0.0
    for i in range(n):
        t = i / RATE
        phase += 2 * math.pi * (250.0 - 140.0 * min(1.0, t / 0.05)) / RATE
        v = (math.sin(phase) + 0.50 * math.sin(2 * phase) + 0.25 * math.sin(3 * phase)) \
            * env(t, length, 0.002, 1, 2.4) * 0.9
        v += cap(bits(noise())) * bell(min(1.0, t / 0.34), 0.10, 1.3, 1.2) * 4.0
        v += rumble(noise()) * env(t, length, 0.01, 1, 1.8) * 0.45
        if t > 0.26:
            u = t - 0.26
            settle_phase += 2 * math.pi * (190.0 - 105.0 * min(1.0, u / 0.04)) / RATE
            v += math.sin(settle_phase) * max(0.0, 1.0 - u / (length - 0.26)) ** 6 * 0.55
        yield v


def jump(length=0.13, cloth=900.0, voice=165.0, hum=0.16):
    """Cloth and effort, quiet.

    Jumping is the most repeated thing in the game after walking, so the brief for this one is
    mostly negative: no transient worth noticing, no tail, nothing pitched enough to be a note,
    and a level a fifth of full scale. The test of a sound like this is whether a player who
    heard it four hundred times in a match would ask for it to be turned off.

    The faint 165 Hz under the cloth is the only part that says a person did this rather than a
    curtain moving, and it is at a sixth of the level of everything else for that reason.

    Capped at 2.3 kHz rather than 3, which is the one thing separating this from `dodge` and
    from `swing` that does not depend on level. Measured, the three sit at roughly 2.4, 5.2 and
    3.2 kHz of spectral centre: a jump is the dark one, a dodge the bright one, and a swing the
    one in between that MOVES. A jump that read as a swing would be a sound telling a lie in a
    game where you cannot see the man making it.
    """
    n = int(RATE * length)
    rub, cap = highpass(cloth), lowpass(2300.0)
    breath = lowpass(1400.0)
    phase = 0.0
    for i in range(n):
        t = i / RATE
        k = t / length
        phase += 2 * math.pi * voice / RATE
        v = cap(rub(noise())) * bell(min(1.0, t / 0.09), 0.3, 1.5, 1.2) * 1.3 * 0.8
        v += breath(noise()) * bell(k, 0.45, 1.8, 1.4) * 0.25
        v += math.sin(phase) * hum * bell(k, 0.5, 2.0, 1.6)
        yield v


def dodge(length=0.16, bright=1500.0, gap=0.055):
    """Cloth, twice, and nothing else at all.

    Two overlapping rubs rather than one puff, because fabric moving is a sequence and a single
    burst of filtered noise is a sneeze. Brighter than `jump` and with no tonal layer, so the
    two are told apart by colour rather than by level — they are both quiet and they both fire
    constantly, and a player should be able to hear which one happened without either of them
    being loud enough to care about.

    Nothing here sweeps, which is the rule that keeps it off `swing`. A dodge that sweeps is a
    dodge that reads as an attack, and in a game where you cannot see the other man that is a
    sound telling an outright lie.
    """
    n = int(RATE * length)
    rub, cap = highpass(bright), lowpass(4200.0)
    for i in range(n):
        t = i / RATE
        a = bell(min(1.0, t / 0.095), 0.35, 1.6, 1.2)
        b = bell(min(1.0, max(0.0, t - gap) / 0.105), 0.4, 1.7, 1.2) * 0.75
        yield cap(rub(noise())) * (a + b) * 0.9


def ladder_grab(length=0.10, rungs=(420.0, 980.0, 1870.0), knock=140.0):
    """A hand arriving on a rung. Wood or metal contact, short, dry.

    Dry is the entire specification. The partials are damped so hard — decays of 22, 30 and 40
    over a tenth of a second — that none of them lasts long enough to be heard as a pitch, and
    that is what separates a rung from `parry` and from `armour`, both of which are the same
    kind of synthesis with the damping taken off. A ladder in a prison yard is not a bell.

    A ladder is also climbed in a hurry, several rungs a second, so this gets two variants with
    the partials moved: two grabs in a row on the same clip read as a machine rather than a man
    (docs/14, which is why the walk has an A and a B).
    """
    n = int(RATE * length)
    ph = spread(len(rungs))
    slap_hi, slap_lo = highpass(600.0), lowpass(3500.0)
    for i in range(n):
        t = i / RATE
        v = slap_lo(slap_hi(noise())) * env(t, length, 0.0004, 1, 42.0) * 1.2
        v += ring(t, length, 1.0, rungs, (14.0, 20.0, 28.0), 0.55, ph) * 0.85
        v += math.sin(2 * math.pi * knock * t) * env(t, length, 0.001, 1, 18.0) * 0.3
        yield v


def ladder_out(length=0.15, top=2600.0, bottom=900.0, tap=250.0):
    """Climbing off. A scuff, then two light taps.

    Same materials as `ladder_grab` and a different RHYTHM, which is the part that survives
    being attenuated: one hard contact at the start means somebody got ON, a scuff followed by
    two small knocks means somebody got OFF. That is worth real information in a game of
    separate floors — it is the difference between "he is on the ladder" and "he is on your
    storey now" — and rhythm is the only property of a sound the hearing model cannot touch.
    """
    n = int(RATE * length)
    scr = sweep_bandpass(1.2)
    for i in range(n):
        t = i / RATE
        k = min(1.0, t / 0.05)
        v = scr(noise(), top + (bottom - top) * k) * bell(min(1.0, t / 0.06), 0.2, 1.3, 1.0) * 1.5
        for at, amp in ((0.055, 0.55), (0.082, 0.38)):
            if t >= at:
                u = t - at
                v += (math.sin(2 * math.pi * tap * u) * 0.7 + noise() * 0.5) \
                    * max(0.0, 1.0 - u / 0.045) ** 5 * amp
        yield v * 0.8


def poison_warn(length=2.0):
    """Beat one of three: pressure building from below.

    PoisonWarning.cs posts this once, at the start of a three-second brace, and sets the arena's
    lamps dipping over the same three seconds. The comment at the top of that file is explicit
    that the announcement must be readable with the sound off, so this is not carrying the
    warning on its own — but the one thing the lamps cannot say is WHERE it is coming from, and
    that is this clip's job.

    "From below" is a statement about spectrum, not about panning. Everything here is under
    420 Hz: a sub rising from 42 Hz to 96 Hz over two seconds with the plates of the floor
    rattling at 7 Hz on top of it, and nothing above that at all. This is the one sound in the
    file allowed to live mostly where a phone speaker cannot follow, because it is the one
    sound whose MEANING is that it is coming from under the floor — but it still has to be
    heard, so the second and third harmonics are carried at 45% and 26% rather than token
    level, and the plate rattle runs up to 260 Hz where a phone does work. On headphones the
    fundamental is there; on a phone the harmonics and the rattle carry the rise. Haptics, when
    they arrive, are this sound's natural other half.

    Two seconds inside a three-second brace, deliberately: it fades out over the last 160 ms
    and leaves a second of nothing before the clank, which is a better use of the third second
    than filling it.
    """
    n = int(RATE * length)
    plate, cap = lowpass(260.0), lowpass(420.0)
    phase = 0.0
    for i in range(n):
        t = i / RATE
        k = t / length
        phase += 2 * math.pi * (42.0 + 54.0 * (k ** 1.3)) / RATE
        sub = math.sin(phase) + 0.45 * math.sin(2 * phase) + 0.26 * math.sin(3 * phase)
        rattle = plate(noise()) * (0.5 + 0.5 * math.sin(2 * math.pi * 7.0 * t))
        swell = (k ** 1.8) * (1.0 - max(0.0, (k - 0.92) / 0.08) ** 2)
        yield cap(sub * 0.75 + rattle * 0.55) * swell


def poison_vent(length=0.80, base=430.0):
    """Beat two: the gas arriving. A clank and a release, as one hard event.

    PoisonWarning.cs shoves the camera once here, with the same trauma a heavy blow adds, and
    is clear about why: the gas is an EVENT and not a hit. So this is one attack and not a
    build — a valve at 430 Hz, which is deliberately an octave and a half below `parry` so that
    the loudest metallic sound in the match is never mistaken for the most rewarding one — and
    then the release behind it.

    The release is noise through a high-pass whose corner falls from 2.6 kHz to 900 Hz across
    the clip. Gas leaving a pipe under pressure gets darker as the pressure drops, and that
    falling corner is the only part of this that says the sound is a release rather than just a
    hiss somebody faded in. There is a low shove underneath for the camera to land on.

    Posted at Loud with the pool's own position, so it reaches both fighters through every
    floor in the arena. It is the one sound a match guarantees everybody hears.
    """
    n = int(RATE * length)
    ratios = (1.0, 1.63, 2.28, 3.11, 4.07)
    ph = spread(len(ratios))
    air = sweep_highpass()
    phase = 0.0
    for i in range(n):
        t = i / RATE
        k = t / length
        v = noise() * env(t, length, 0.0004, 1, 90.0) * 0.8
        v += ring(t, length, base, ratios, (9.0, 12.0, 16.0, 21.0, 26.0), 0.6, ph) * 0.8
        phase += 2 * math.pi * (160.0 - 80.0 * min(1.0, t / 0.05)) / RATE
        v += math.sin(phase) * env(t, length, 0.002, 1, 5.0) * 0.7
        # The release gets its own, shorter window than the file: at the level and the length
        # the first attempt gave it, the measured spectrum was 91% above 2 kHz, which is a
        # sound best described as a hiss somebody put a clank in front of. "One hard event"
        # means the clank is the event, so the gas is done by 550 ms and the metal outlasts it.
        v += air(noise(), 2600.0 - 1700.0 * k) * bell(min(1.0, t / 0.55), 0.10, 1.4, 1.5) * 0.6
        yield v


# Which files stand in for which SoundId, written out rather than matched off the names.
#
# The two sides disagree on purpose: a SoundId says what HAPPENED and a filename says what it
# SOUNDS LIKE, so AttackHit is Hit_Flesh and AttackHitArmoured is Hit_Armour. A matcher that
# bridged that would be a matcher nobody could predict, and this table is read by a person
# filling in the sound table in the inspector — and by audio_check, which uses it to say which
# files are sitting on disk waiting for a row that has not been filled in yet.
FOR_SOUND = {
    "FootstepQuiet":    ("Step_Quiet_A", "Step_Quiet_B"),
    "FootstepLoud":     ("Step_Loud_A", "Step_Loud_B"),
    "Jump":             ("Jump_A", "Jump_B", "Jump_C"),
    "LandSoft":         ("Land_Soft",),
    "LandHard":         ("Land_Hard",),
    "LadderGrab":       ("Ladder_Grab_A", "Ladder_Grab_B"),
    "LadderOut":        ("Ladder_Out_A", "Ladder_Out_B"),
    "Dodge":            ("Dodge_A", "Dodge_B", "Dodge_C"),
    "ChestPryQuiet":    ("Chest_Pry_Quiet",),
    "ChestPryLoud":     ("Chest_Pry_Loud",),
    "ChestOpen":        ("Chest_Open",),
    "ItemPickup":       ("Item_Pickup",),
    "ItemDrop":         ("Item_Drop",),
    "AttackSwingLight": ("Swing_Light_A", "Swing_Light_B", "Swing_Light_C"),
    "AttackSwingHeavy": ("Swing_Heavy_A", "Swing_Heavy_B"),
    "AttackHit":        ("Hit_Flesh_A", "Hit_Flesh_B", "Hit_Flesh_C"),
    "AttackHitArmoured": ("Hit_Armour_A", "Hit_Armour_B", "Hit_Armour_C"),
    "BlockImpact":      ("Block_Impact_A", "Block_Impact_B"),
    "Parry":            ("Parry_A", "Parry_B"),
    "Stagger":          ("Stagger_A", "Stagger_B"),
    "Death":            ("Death",),
    "Siren":            ("Siren_Pulse",),
    "PoisonWarn":       ("Poison_Warn",),
    "PoisonVent":       ("Poison_Vent",),
}


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

    # Land_Hard CLIPPED. Measuring the files rather than trusting the recipes turned up
    # twenty-five samples pinned flat at full scale in the one movement sound that fires
    # loudest and most often, which on a phone speaker is a crackle on top of every hard
    # landing — and it was invisible because nothing here had ever printed a peak. The recipe
    # is untouched; it is only given a level, the way everything in the combat pass is. 0.72
    # puts it just under a blow landing, which is the right order: a hard landing is the
    # loudest thing a fighter does with his own feet and still not as loud as being hit.
    wav("Land_Hard", level(land(0.46, hard=True), 0.72))
    wav("Chest_Pry_Quiet", pry())
    wav("Chest_Pry_Loud", pry(0.55, loud=True))
    wav("Chest_Open", chest_open())
    wav("Item_Pickup", take())
    wav("Item_Drop", take(0.22, up=False))

    # ---------------------------------------------------------------- the fight (docs/38 § 1)
    #
    # The second number on every line is the peak the file is written at, and read as a column
    # it is the mix. It is not loudness for its own sake: the hearing model has already decided
    # how far away a thing is by the time it reaches a voice, so what these say is how much a
    # sound MATTERS when it happens next to you.
    #
    #   .80  the kill
    #   .70  a blow landing
    #   .66  armour, and the parry just under it
    #   .52  a blow that was blocked, a rung, a stumble
    #   .42  a heavy swing, .35 a light one
    #   .20  jumping, dodging — four hundred times a match
    #
    # The order the recipes are written in is docs/38's order of importance, so that a pass
    # that stops halfway stops having done the two that are 90% of the feel of a fight.

    # A blow landing, and the swing in front of it. Three variants each: at a hit every two
    # seconds one clip is a rifle, and this is the sound the whole hit-feel chord of docs/20
    # was already playing to.
    make("Hit_Flesh_A", 0.70, flesh)
    # The variants move the drop and the length, never the register: a variant that lands in a
    # different band is not a variant, it is a second sound, and the first pass at these two
    # put 72% of their energy under 150 Hz while the A clip put 87% of its own between 150 and
    # 400. Two hits in a row would have sounded like two different events.
    make("Hit_Flesh_B", 0.70, flesh, 0.155, 368.0, 185.0)
    make("Hit_Flesh_C", 0.68, flesh, 0.185, 286.0, 172.0, 0.8, 2.6)
    make("Swing_Light_A", 0.35, swing)
    make("Swing_Light_B", 0.35, swing, 0.148, 2880.0, 1040.0, 1.2)
    make("Swing_Light_C", 0.34, swing, 0.172, 2360.0, 820.0, 1.45, 0.0, 0.58)

    # Heavy: the same recipe an octave and a half down, half again as long, with a body under
    # it, and louder — mass is more air moved, so a heavy swing that was merely lower would
    # read as a light swing heard from further away, which is the opposite of the information
    # it is supposed to carry. Two variants; a heavy weapon does not swing in flurries.
    make("Swing_Heavy_A", 0.42, swing, 0.300, 1300.0, 320.0, 1.1, 0.30, 0.68)
    make("Swing_Heavy_B", 0.42, swing, 0.275, 1160.0, 285.0, 1.25, 0.26, 0.64)

    # Armour. Three variants, and the variation is in the BASE of the partials rather than in
    # the level: three strikes on the same tuned plate read as a xylophone, which is the one
    # way this sound could manage to be funny.
    make("Hit_Armour_A", 0.66, armour)
    make("Hit_Armour_B", 0.66, armour, 0.28, 712.0, (1.0, 1.66, 2.52, 3.19, 4.41, 5.87))
    make("Hit_Armour_C", 0.64, armour, 0.325, 583.0, (1.0, 1.79, 2.33, 3.47, 4.78, 6.42), 0.58)

    # The two answers to the same input, 100 ms apart. Two variants each; a flurry of blocks
    # happens, a flurry of parries does not.
    make("Block_Impact_A", 0.52, block)
    make("Block_Impact_B", 0.52, block, 0.118, 268.0, 168.0, 372.0)
    make("Parry_A", 0.66, parry)
    make("Parry_B", 0.66, parry, 0.315, 1312.0)

    make("Stagger_A", 0.52, stagger)
    make("Stagger_B", 0.50, stagger, 0.295, 104.0, 1560.0, 640.0)

    # Once a match, so one clip. See the recipe.
    make("Death", 0.80, death)

    # The two that fire constantly, and the two that therefore get the most variants and the
    # lowest levels in the file.
    make("Jump_A", 0.21, jump)
    make("Jump_B", 0.21, jump, 0.122, 1060.0, 178.0)
    make("Jump_C", 0.20, jump, 0.138, 820.0, 152.0, 0.10)
    make("Dodge_A", 0.19, dodge)
    make("Dodge_B", 0.19, dodge, 0.152, 1680.0, 0.048)
    make("Dodge_C", 0.18, dodge, 0.168, 1380.0, 0.062)

    # Separate floors are the whole shape of an arena (docs/05), so the ladder is a route and
    # these two are the only announcement that somebody took it.
    make("Ladder_Grab_A", 0.52, ladder_grab)
    make("Ladder_Grab_B", 0.50, ladder_grab, 0.094, (468.0, 1105.0, 2040.0), 152.0)
    make("Ladder_Out_A", 0.48, ladder_out)
    make("Ladder_Out_B", 0.46, ladder_out, 0.142, 2340.0, 820.0, 228.0)

    # The gas (docs/33). Posted at Loud from the pool, heard through every floor.
    make("Poison_Warn", 0.56, poison_warn)
    make("Poison_Vent", 0.82, poison_vent)


if __name__ == "__main__":
    main()
