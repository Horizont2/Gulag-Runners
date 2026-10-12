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
               sudden=600, restart=300)


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


def step(m, cfg, dead=(False, False), health=(100, 100)):
    m.phase_changed = False
    m.just_ended = False

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

    want = phase_at(m.frame, cfg)
    if want != m.phase:
        m.phase, m.phase_changed = want, True


def _end(m, winner):
    m.winner = winner
    m.over_frame = 0
    m.wants_restart = False
    m.phase, m.phase_changed, m.just_ended = OVER, True, True
