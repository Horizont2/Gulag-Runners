"""Measures the placeholder sounds, because nobody working on this can hear them.

make_placeholder_audio writes thirty short wav files and prints a duration and a peak for
each. That is a report, not a check: it is produced by the same run that produced the files,
by the same code, and it goes past in the terminal once. What is needed is something that
reads the files as they are on disk and fails, so that a recipe tuned badly at eleven at night
is a red check in the morning rather than a sound somebody notices is wrong in a playtest.

This exists in particular because the pass that added the fight to the sound table turned up
three faults by MEASURING rather than by listening, and every one of them would have shipped:

  Land_Hard was CLIPPING. Twenty-five samples pinned flat at full scale, in the one movement
      sound that fires loudest and most often. It had been committed for weeks. Nothing had
      ever printed a peak, so there was nothing to notice.
  AttackHit was INAUDIBLE ON A PHONE. The first recipe dropped its thud to 58 Hz, which put
      95% of the energy of the most important sound in the game (docs/38 ranks it first of
      twelve) below 150 Hz, and a phone speaker a centimetre across does nothing much below
      250 Hz. On a desk monitor it was a good thud. On the device the game ships to it was a
      faint click.
  TWO VARIANTS OF ONE SOUND LANDED IN DIFFERENT BANDS. Hit_Flesh_A sat at 87% between 150 and
      400 Hz and Hit_Flesh_B at 72% below 150, because the B variant's parameters had been
      written against an earlier register and not moved with it. The backend picks between
      clips at random (SoundEntry.clips), so two blows in a row would have sounded like two
      different events — which is worse than one clip repeating, the problem variants exist
      to solve.

So the rules below are the five properties a placeholder has to have to be usable at all, plus
two that are design requirements out of docs/14 rather than hygiene — the pairs of sounds that
carry tactical information and must not blur into each other. It cannot hear anything either.
Every rule here is a number read off a file.

    python3 placeholder_audio_check.py
"""
import array
import math
import os
import re
import sys
import wave

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import make_placeholder_audio as recipes            # noqa: E402

OUT = os.path.abspath(recipes.OUT)
AUDIO = os.path.abspath(os.path.join(OUT, ".."))

# ---------------------------------------------------------------- what a placeholder must be
#
# Deliberately wide. These are not taste; they are the bounds outside which a file is broken
# rather than badly judged, and a check with opinions about the mix would be a check arguing
# with whoever is doing the mix.

RATE = recipes.RATE          # one rate for the whole folder: see the note on MIXED_RATE below
CHANNELS = 1                 # the hearing model does the panning (docs/14); stereo is wasted
WIDTH = 2

PEAK_CEILING = 0.96          # above this the saturation is the file's, not the player's
PEAK_FLOOR = 0.05            # below this it is a file nobody will believe fired
RMS_FLOOR = 0.004            # a single tick in two seconds of silence passes a peak test
SHORTEST = 0.03              # under 30 ms nothing has a character, only a click
LONGEST = 2.50               # Poison_Warn at 2.0 s is the longest thing the game posts

# A buffer that starts or ends away from zero clicks on every single play, and on Jump or
# Dodge — which fire hundreds of times a match — that click is what the player ends up
# hearing instead of the sound. −34 dB is comfortably below where it is audible and well
# above the rounding the recipes land on (the worst file in the folder ends at −62 dB).
EDGE = 0.02

# ---------------------------------------------------------------- the pairs that carry information
#
# docs/14 makes sound the way a player finds out what the other man is doing, and docs/01 keeps
# that man off screen for most of a match. Two pairs therefore carry real tactical information
# and are the two places where "they sound a bit similar" is a BUG:
#
#   AttackHit / AttackHitArmoured   whether he has found armour, which changes what you should
#       do next (docs/03). These are the same event from the player's side — he swung, it
#       connected — so the only thing that can tell them apart is the sound.
#   BlockImpact / Parry             which of the two things that can happen to a defended blow
#       happened. The parry is the hardest input in the game and the reward for it is this
#       sound; a parry that reads as a block is a reward the player never collects.
#
# And both have to survive the muffled path: ×0.45 volume and ×0.6 range through a floor
# (HearingTuning), then ×0.8 and pitch ×0.85 again in UnityAudioBackend. Volume is what that
# takes away. What it CANNOT take away is WHICH BAND a sound sits in and HOW LONG it lasts, so
# those are the two things measured here, and the thresholds are set where a drift that halved
# the gap would trip them rather than snug against what the files happen to read today.
#
# Each row names the two probe bands for its pair: the tilt is the energy in the upper band as
# a fraction of the energy in both, so 0 is entirely low and 1 entirely high. It is measured
# with the project's own two-pole `bandpass`, which is why the numbers here and the numbers a
# recipe was tuned against cannot drift apart — and why there is no FFT and no dependency
# beyond the standard library in this folder.
#
#   (quiet side, loud side, lower probe Hz, upper probe Hz, its tilt ceiling, its tilt floor,
#    how many times longer the loud side must ring)
PAIRS = (
    ("AttackHit", "AttackHitArmoured", 250.0, 900.0, 0.45, 0.60, 1.8,
     "flesh is a thud and a crack, over inside 170 ms, with 87% of it between 150 and 400 Hz; "
     "armour is inharmonic partials ringing for a quarter of a second, with 94% of it between "
     "400 Hz and 1.2 kHz. The two histograms barely overlap, and attenuating both does not "
     "make either one the other. Measured today: 0.29 against 0.71."),
    ("BlockImpact", "Parry", 250.0, 900.0, 0.35, 0.80, 2.5,
     "the block is low-passed at 1.1 kHz and dead in 110 ms; the parry is high-passed at "
     "380 Hz and rings for 200. Nothing in either is in the other's band, which is the only "
     "reason they stay apart at the far end of the hearing range. Measured today: 0.13 "
     "against 0.95."),
)

DECAY_DB = 30.0


def read(path):
    with open(path, encoding="utf-8", errors="replace") as f:
        return f.read()


def samples(path):
    """The file as floats in −1..1, plus the format it claimed to be in."""
    with wave.open(path) as w:
        fmt = (w.getframerate(), w.getnchannels(), w.getsampwidth())
        if w.getsampwidth() != WIDTH:
            return None, fmt
        raw = w.readframes(w.getnframes())
    a = array.array("h")
    a.frombytes(raw)
    return [v / 32767.0 for v in a], fmt


def tilt(buf, lo, hi, q=1.0):
    """Energy in the upper band over energy in both: 0 is entirely low, 1 entirely high.

    Two band-passes and not one high-pass, which was the first attempt and was worthless. A
    one-pole high-pass at 400 Hz passes 36% of the power at 300 Hz, so it read the flesh hit at
    45% "above 400" when the truth was 12%, and it read the armoured hit — 97% of which really
    is above 400 — at 54%. The two came out nine points apart on a property where their real
    spectra barely overlap. A band edge six dB an octave wide cannot measure a band.

    Two resonant bands either side of the boundary can, because what is being asked is not
    "how much is above a line" but "which of two registers is this in", and that is a ratio.
    """
    low, high = recipes.bandpass(lo, q), recipes.bandpass(hi, q)
    el = eh = 0.0
    for v in buf:
        a, b = low(v), high(v)
        el += a * a
        eh += b * b
    return eh / (el + eh + 1e-30)


def decay_seconds(buf, rate):
    """How long from the loudest part until it is 30 dB down, off a 5 ms envelope."""
    win = max(1, int(0.005 * rate))
    run, env = 0.0, []
    for i, v in enumerate(buf):
        run += abs(v)
        if i >= win:
            run -= abs(buf[i - win])
        env.append(run / min(i + 1, win))

    loudest = max(range(len(env)), key=env.__getitem__)
    floor = env[loudest] * (10.0 ** (-DECAY_DB / 20.0))
    last = loudest
    for i in range(loudest, len(env)):
        if env[i] > floor:
            last = i
    return (last - loudest + 1) / rate


def guid_of(path):
    at = path + ".meta"
    if not os.path.exists(at):
        return None
    m = re.search(r"guid: ([0-9a-f]{32})", read(at))
    return m.group(1) if m else None


def main(argv):
    problems, notes = [], []

    if not os.path.isdir(OUT):
        print(f"  no placeholder folder at {OUT}")
        return 1

    files = sorted(f for f in os.listdir(OUT) if f.endswith(".wav"))
    if not files:
        print("  the placeholder folder has no wav files in it at all")
        return 1

    measured, guids = {}, {}
    rates = set()

    # The samples are kept only for the files a PAIRS row names, because the tilt has to be
    # measured with that pair's own probe bands and there is no point holding forty-six
    # buffers to measure four of them.
    want = {n for row in PAIRS for side in row[:2] for n in recipes.FOR_SOUND.get(side, ())}
    audio = {}

    for f in files:
        name = f[:-4]
        path = os.path.join(OUT, f)

        buf, fmt = samples(path)
        rate, channels, width = fmt
        rates.add(rate)

        if buf is None:
            problems.append(f"{name}: {width * 8}-bit, and this reads 16-bit PCM only.")
            continue
        if channels != CHANNELS:
            problems.append(
                f"{name}: {channels} channels. The hearing model hands the backend a finished "
                f"volume and a pan (docs/14) and every voice is 2D, so a stereo placeholder is "
                f"twice the file for a width nothing reads.")
        if rate != RATE:
            problems.append(
                f"{name}: {rate} Hz where the rest of the folder is {RATE}. The importer is "
                f"set to preserve the source rate, so one odd file is one odd file in the "
                f"build for no reason anybody will remember.")

        if not buf:
            problems.append(f"{name}: no samples at all.")
            continue

        peak = max(abs(v) for v in buf)
        rms = math.sqrt(sum(v * v for v in buf) / len(buf))
        secs = len(buf) / rate
        at_full = sum(1 for v in buf if abs(v) >= 32766 / 32767.0)

        measured[name] = dict(peak=peak, rms=rms, secs=secs,
                              decay=decay_seconds(buf, rate))
        if name in want:
            audio[name] = buf

        if at_full:
            problems.append(
                f"{name}: CLIPPING — {at_full} sample(s) pinned flat at full scale. A "
                f"placeholder that clips teaches everybody the wrong level, and on a phone "
                f"speaker it is a crackle on top of the sound rather than a louder sound.")
        elif peak > PEAK_CEILING:
            problems.append(
                f"{name}: peak {peak:.2f}, over {PEAK_CEILING}. Nothing here needs the last "
                f"half decibel and the voice it plays on is multiplied by the hearing model "
                f"anyway, so the headroom costs nothing and the margin to clipping is the "
                f"whole point of having one.")
        if peak < PEAK_FLOOR:
            problems.append(
                f"{name}: peak {peak:.3f}. That is a file that will read as nothing having "
                f"happened, which is the one state a placeholder exists to rule out.")
        elif rms < RMS_FLOOR:
            problems.append(
                f"{name}: rms {rms:.4f} against a peak of {peak:.2f} — one spike in a buffer "
                f"of silence. A peak test alone passes that, and it is not a sound.")

        if secs < SHORTEST:
            problems.append(f"{name}: {secs * 1000:.0f} ms. Under {SHORTEST * 1000:.0f} ms "
                            f"nothing has a character, only a click.")
        elif secs > LONGEST:
            problems.append(
                f"{name}: {secs:.2f} s, over {LONGEST} s. These are posted one-shot off a "
                f"simulation event and the voices are stolen oldest-first when all six are "
                f"busy (UnityAudioBackend), so a long placeholder is a placeholder that eats "
                f"the sounds behind it.")

        if abs(buf[0]) > EDGE or abs(buf[-1]) > EDGE:
            problems.append(
                f"{name}: starts at {buf[0]:+.3f} and ends at {buf[-1]:+.3f}, and an edge away "
                f"from zero is a CLICK on every single play. On something that fires as often "
                f"as a jump, the click is what the player hears instead of the sound. Give the "
                f"envelope a zero at both ends (`bell` already has one).")

        g = guid_of(path)
        if g is None:
            problems.append(
                f"{name}: no .meta, or one with no guid in it. Unity will invent both on next "
                f"import, which means the guid is not in git: the next person to clone gets a "
                f"different one, every scene row pointing at this clip dangles, and the sound "
                f"table empties itself. make_placeholder_audio writes these.")
        elif g in guids:
            problems.append(
                f"{name}: shares guid {g} with {guids[g]} — a copied .meta. Unity treats a "
                f"guid as an identity and drops one of the two assets on import, so one of "
                f"these clips simply stops existing.")
        else:
            guids[g] = name

    if len(rates) > 1:
        notes.append(f"{len(rates)} different sample rates in one folder: "
                     + ", ".join(f"{r} Hz" for r in sorted(rates)))

    # ---------------------------------------------------------- the map, against the disk
    for sound, names in sorted(recipes.FOR_SOUND.items()):
        for name in names:
            if name not in measured:
                problems.append(
                    f"FOR_SOUND says {sound} is {name} and there is no {name}.wav. Either the "
                    f"recipe was renamed and the map was not, or the generator has not been "
                    f"run — and audio_check reads this map to say which clips are waiting for "
                    f"a row, so a wrong entry there is advice pointing at nothing.")

    mapped = {n for names in recipes.FOR_SOUND.values() for n in names}
    orphans = sorted(set(measured) - mapped)
    if orphans:
        problems.append(
            f"{len(orphans)} file(s) that no SoundId claims: {', '.join(orphans)}. A clip no "
            f"row can point at is a clip nothing will ever play. Add it to FOR_SOUND or delete "
            f"it.")

    known = set(ids())
    for sound in sorted(recipes.FOR_SOUND):
        if sound not in known:
            problems.append(
                f"FOR_SOUND has a row for {sound} and SoundId has no such value, so whatever "
                f"those clips are for, the game cannot ask for it.")

    # ---------------------------------------------------------- the pairs, measured
    for dark, brightest, dark_max, bright_min, ratio, why in PAIRS:
        a = [measured[n] for n in recipes.FOR_SOUND.get(dark, ()) if n in measured]
        b = [measured[n] for n in recipes.FOR_SOUND.get(brightest, ()) if n in measured]
        if not a or not b:
            continue

        da = max(m["bright"] for m in a)
        db = min(m["bright"] for m in b)
        ta = max(m["decay"] for m in a)
        tb = min(m["decay"] for m in b)

        notes.append(f"{dark} vs {brightest}: {da * 100:.0f}% against {db * 100:.0f}% of the "
                     f"energy above {BRIGHT_FROM:.0f} Hz, ringing {ta * 1000:.0f} ms against "
                     f"{tb * 1000:.0f} ms")

        if da > dark_max:
            problems.append(
                f"{dark} has {da * 100:.0f}% of its energy above {BRIGHT_FROM:.0f} Hz, over "
                f"the {dark_max * 100:.0f}% it is allowed, so it is drifting into "
                f"{brightest}'s band. {why}")
        if db < bright_min:
            problems.append(
                f"{brightest} has only {db * 100:.0f}% of its energy above "
                f"{BRIGHT_FROM:.0f} Hz, under the {bright_min * 100:.0f}% it needs, so it is "
                f"drifting into {dark}'s band. {why}")
        if tb < ta * ratio:
            problems.append(
                f"{brightest} rings {tb * 1000:.0f} ms against {dark}'s {ta * 1000:.0f}, and "
                f"it needs {ratio}x to stay apart from it. Length is the one thing the hearing "
                f"model cannot take away — volume and brightness it can — so this is the "
                f"property these two are told apart by at a distance. {why}")

    for n in notes:
        print("  note: " + n)
    for p in problems:
        print("  " + p)
    print(f"placeholder_audio_check: {len(problems)} problem(s) across {len(files)} file(s)")
    return 1 if problems else 0


def ids():
    """SoundId name -> number, so FOR_SOUND can be held against the enum."""
    src = read(os.path.join(AUDIO, "SoundId.cs"))
    return {m.group(1): int(m.group(2))
            for m in re.finditer(r"^\s+(\w+) = (\d+),?\s*$", src, re.M)}


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
