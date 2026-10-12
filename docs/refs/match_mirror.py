"""The match clock of docs/01, as the sim runs it.

One match is one fight and one life. The phases are a table of durations and nothing more, so
this is short — but it is the thing that decides when a fighter may move and when a round is
over, and both devices have to cross each boundary on the same tick (docs/06). A clock that
drifts is a siren that fires twice.
"""

CAGES, SCAVENGE, SIREN, HUNT, CLOSING, SUDDEN, OVER = range(7)
NAMES = {CAGES: "cages", SCAVENGE: "scavenge", SIREN: "siren", HUNT: "hunt",
         CLOSING: "closing", SUDDEN: "sudden death", OVER: "over"}

# docs/01's table, in ticks. Sixty of these is a second.
#
# The siren is the OPENING of the hunt, not a phase in front of it: docs/01 puts the pulse at
# 0:25 and the hunt at 0:25-1:15, so its 1.5 s comes out of the hunt's 50, not on top of them.
# Added rather than subtracted, every boundary after it lands a second and a half late.
#
# Closing runs 1:15 to 2:20, not 1:15 to 2:00: the shutters take the first 45 s of it and then
# both fighters are in the one room with nowhere left to walk away to for another 20. The two
# numbers that are not rows in docs/01's table are the ones that make the match finite — sudden
# death has to end, and the result has to hand over to the next round. The set adds to 2:30,
# which is where docs/01 bounds a match.
DEFAULT = dict(cage=180, scavenge=1320, siren=90, hunt=3000 - 90, closing=3900,
               sudden=600, restart=300, shutter=2700)

MAX_GATES = 32

# ------------------------------------------------------------------- the shutters (docs/01)
CLOSING_GATE, SUDDEN_GATE = 0, 1
MINUS_X, PLUS_X, MINUS_Y, PLUS_Y = 0, 1, 2, 3
GATE_SKIN = 0.01


class Gate:
    """A piece of the arena that is not there until the clock says so.

    Collision is the baked boxes that point at this gate; the only state is one bit. The box
    here is their union, which is what the shove and the view are measured against.
    """

    def __init__(s, min_x, min_y, max_x, max_y, order=0, when=CLOSING_GATE, toward=PLUS_X,
                 span=(-1e9, 1e9)):
        s.min_x, s.min_y, s.max_x, s.max_y = min_x, min_y, max_x, max_y
        s.order, s.when, s.toward, s.span = order, when, toward, span

    def behind(s, x, y, height):
        """Is this body on the side the gate shuts off — in the doorway, or stranded past it."""
        mid_y = y + height / 2
        if s.toward == MINUS_X: return x > s.min_x
        if s.toward == PLUS_X:  return x < s.max_x
        if s.toward == MINUS_Y: return mid_y > s.min_y
        return mid_y < s.max_y

    def clear(s, x, y, half_width, height):
        if s.toward == MINUS_X: return (s.min_x - half_width - GATE_SKIN, y)
        if s.toward == PLUS_X:  return (s.max_x + half_width + GATE_SKIN, y)
        if s.toward == MINUS_Y: return (x, s.min_y - height - GATE_SKIN)
        return (x, s.max_y + GATE_SKIN)

    def reaches(s, depth, half):
        return depth + half > s.span[0] and depth - half < s.span[1]


def gate_shuts_at(gate, gates, cfg):
    """The tick one gate shuts on. Pure, so the view can start sliding so as to ARRIVE on it."""
    if not gates or gate < 0 or gate >= len(gates):
        return 1 << 62

    closing_at = cfg["cage"] + cfg["scavenge"] + cfg["siren"] + cfg["hunt"]
    if gates[gate].when == SUDDEN_GATE:
        return closing_at + cfg["closing"]

    closing = nth = 0
    for i, g in enumerate(gates):
        if g.when != CLOSING_GATE:
            continue
        closing += 1
        if i <= gate:
            nth = closing

    if closing == 0:
        return closing_at

    window = cfg["shutter"] or cfg["closing"]
    return closing_at + nth * window // closing


def gates_shut_at(frame, gates, cfg):
    if not gates:
        return 0
    mask = 0
    for i in range(min(len(gates), MAX_GATES)):
        if frame >= gate_shuts_at(i, gates, cfg):
            mask |= 1 << i
    return mask


def shove(fighters, gates, just_shut, half_width=0.2, height=0.91):
    """Brings every fighter through the gates that shut on this tick.

    A shutter that only becomes solid can seal somebody into a wing with nobody to fight, and
    the match then ends on the clock in two empty rooms. So: anything still behind it comes
    through, staggered for it, and never crushed.
    """
    if not gates or not just_shut:
        return
    for g in range(min(len(gates), MAX_GATES)):
        if not just_shut & (1 << g):
            continue
        gate = gates[g]
        for f in fighters:
            if not gate.reaches(f.get("depth", 0.0), f.get("half_depth", 0.2)):
                continue
            if not gate.behind(f["x"], f["y"], height):
                continue
            f["x"], f["y"] = gate.clear(f["x"], f["y"], half_width, height)
            f["vx"] = f["vy"] = 0.0
            f["ladder"] = -1
            f["chest"] = -1
            f["stagger"] = max(f.get("stagger", 0), 24)
            f["shoved"] = f.get("shoved", 0) + 1


class Match:
    def __init__(s, round=0):
        s.frame = 0
        s.phase = CAGES
        s.winner = -1
        s.round = round
        s.over_frame = 0
        s.phase_changed = True
        s.just_ended = False
        s.wants_restart = False
        s.gates_shut = 0
        s.gates_just_shut = 0
        s.siren_direction = 0

    @property
    def input_allowed(s):
        return s.phase not in (CAGES, OVER)

    @property
    def finished(s):
        return s.phase == OVER

    def cage_progress(s, cfg):
        if s.phase != CAGES or cfg["cage"] <= 0:
            return 1.0
        return min(s.frame, cfg["cage"]) / cfg["cage"]

    def over_progress(s, cfg):
        if s.phase != OVER or cfg["restart"] <= 0:
            return 0.0
        return min(s.over_frame, cfg["restart"]) / cfg["restart"]

    def next_round(s):
        return Match(s.round + 1)

    def siren_for(s, index):
        """A direction, mirrored for the second fighter. The arena has two ends."""
        return s.siren_direction if index == 0 else -s.siren_direction

    def shut(s, gate):
        return 0 <= gate < MAX_GATES and bool(s.gates_shut & (1 << gate))

    def just_shut(s, gate):
        return 0 <= gate < MAX_GATES and bool(s.gates_just_shut & (1 << gate))


def phase_at(frame, cfg):
    at = cfg["cage"]
    if frame < at: return CAGES
    at += cfg["scavenge"]
    if frame < at: return SCAVENGE
    at += cfg["siren"]
    if frame < at: return SIREN
    at += cfg["hunt"]
    if frame < at: return HUNT
    at += cfg["closing"]
    if frame < at: return CLOSING
    return SUDDEN


def total_frames(cfg):
    """The tick the match is over by, come what may."""
    return sum(cfg[k] for k in ("cage", "scavenge", "siren", "hunt", "closing", "sudden"))


def phase_start(phase, cfg):
    """The tick a phase begins on: the inverse of phase_at, for presentation to measure in."""
    at = 0
    if phase <= CAGES: return at
    at += cfg["cage"]
    if phase == SCAVENGE: return at
    at += cfg["scavenge"]
    if phase == SIREN: return at
    at += cfg["siren"]
    if phase == HUNT: return at
    at += cfg["hunt"]
    if phase == CLOSING: return at
    at += cfg["closing"]
    return at


def step(m, cfg, dead=(False, False), health=(100, 100), gates=None, x=(0.0, 0.0)):
    m.phase_changed = False
    m.just_ended = False
    m.gates_just_shut = 0

    # The result, held. The clock still runs here — it is what decides when the next round
    # deals — but nothing about the fight can change any more.
    if m.phase == OVER:
        m.over_frame += 1
        m.wants_restart = cfg["restart"] > 0 and m.over_frame >= cfg["restart"]
        return

    a, b = dead
    if a or b:
        _end(m, 2 if a and b else (1 if a else 0))
        return

    m.frame += 1

    # Time ran out. docs/01 bounds the match, so sudden death cannot be the last word: the one
    # left standing with more of their health took the fight that happened, and level pegging
    # is the draw it looks like. Read off the state, so both devices reach the same verdict.
    if cfg["sudden"] > 0 and m.frame >= total_frames(cfg):
        ha, hb = health
        _end(m, 2 if ha == hb else (0 if ha > hb else 1))
        return

    # The arena before the bodies. A shutter counted as shut only after the fighters have
    # moved is a shutter they walk through on the tick it closes.
    shut = gates_shut_at(m.frame, gates, cfg)
    m.gates_just_shut = shut & ~m.gates_shut
    m.gates_shut = shut

    want = phase_at(m.frame, cfg)
    if want == m.phase:
        return

    m.phase, m.phase_changed = want, True

    # One pulse, taken on the tick the siren begins and then left alone. Read every tick it
    # would be a tracker, which is the one thing docs/01 does not give you.
    if want == SIREN:
        m.siren_direction = 1 if x[1] >= x[0] else -1


def _end(m, winner):
    m.winner = winner
    m.over_frame = 0
    m.wants_restart = False
    m.phase, m.phase_changed, m.just_ended = OVER, True, True
