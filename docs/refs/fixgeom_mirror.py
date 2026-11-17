"""Bit-exact port of Fix, FixVec2 and FixGeom.

The point of a mirror is to be WRONG in exactly the same way as the C#, so a test can
compare it against real geometry and find out where fixed point stops being good enough.
Two things in here are easy to get subtly different and would make the mirror useless:

  * C# integer division TRUNCATES toward zero, Python's // FLOORS toward minus infinity.
    They differ on every negative result, and half the numbers in a fight are negative.
    cdiv() below is the C# one.
  * C# `>>` on a long is an ARITHMETIC shift, and so is Python's on a negative int, so
    the multiply needs no special case. That one is a coincidence worth writing down,
    because it looks like the division problem and is not.
"""
FRAC = 16
ONE = 1 << FRAC


def i32(x):
    """Wrap to a 32-bit signed int, the way C# does when it casts a long back to one.

    Modelled rather than ignored because the overflow is REAL: a point 30 m from a 10 cm
    segment produced a quotient that did not fit, wrapped, and read 8 cm short. A mirror
    that quietly used big integers would have called that correct.
    """
    x &= 0xFFFFFFFF
    return x - 0x100000000 if x >= 0x80000000 else x


def cdiv(a, b):
    """C#'s integer division: truncate toward zero."""
    q = abs(a) // abs(b)
    return -q if (a < 0) != (b < 0) else q


def mul(a, b):
    return i32((a * b) >> FRAC)     # arithmetic shift, same as C# on a long


def div(a, b):
    return i32(cdiv(a << FRAC, b))


def milli(m):
    return cdiv(m << FRAC, 1000)


def to_f(raw):
    return raw / float(ONE)


def from_f(x):
    return int(round(x * ONE))


def clamp(v, lo, hi):
    return lo if v < lo else (hi if v > hi else v)


def sqrt(v):
    if v <= 0:
        return 0
    n = v << FRAC
    bits = n.bit_length()
    x = 1 << ((bits + 1) >> 1)
    while True:
        nxt = (x + n // x) >> 1
        if nxt >= x:
            return x
        x = nxt


# --- vectors, as (x, y) tuples of raws -------------------------------------------

def sub(a, b):
    return (a[0] - b[0], a[1] - b[1])


def add(a, b):
    return (a[0] + b[0], a[1] + b[1])


def scale(a, s):
    return (mul(a[0], s), mul(a[1], s))


def dot(a, b):
    return mul(a[0], b[0]) + mul(a[1], b[1])


def length_sq(a):
    return mul(a[0], a[0]) + mul(a[1], a[1])


def length(a):
    return sqrt(length_sq(a))


def with_length(a, n):
    ln = length(a)
    if ln == 0:
        return (0, 0)
    return (cdiv(mul(a[0], n) << FRAC, ln), cdiv(mul(a[1], n) << FRAC, ln))


# --- the geometry ----------------------------------------------------------------

def cross(a, b):
    return mul(a[0], b[1]) - mul(a[1], b[0])


def crosses(a0, a1, b0, b1):
    a = sub(a1, a0)
    b = sub(b1, b0)
    d1 = cross(b, sub(a0, b0))
    d2 = cross(b, sub(a1, b0))
    d3 = cross(a, sub(b0, a0))
    d4 = cross(a, sub(b1, a0))
    return ((d1 > 0 and d2 < 0) or (d1 < 0 and d2 > 0)) and \
           ((d3 > 0 and d4 < 0) or (d3 < 0 and d4 > 0))


def segment_distance_sq(a0, a1, b0, b1):
    if crosses(a0, a1, b0, b1):
        return 0
    return min(point_segment_distance_sq(a0, b0, b1),
               point_segment_distance_sq(a1, b0, b1),
               point_segment_distance_sq(b0, a0, a1),
               point_segment_distance_sq(b1, a0, a1))


def point_segment_distance_sq(p, a, b):
    ab = sub(b, a)
    ln = dot(ab, ab)
    if ln <= 0:
        return length_sq(sub(p, a))

    along = dot(sub(p, a), ab)
    if along <= 0:
        return length_sq(sub(p, a))
    if along >= ln:
        return length_sq(sub(p, b))

    t = div(along, ln)              # strictly between 0 and 1, so it cannot overflow
    return length_sq(sub(p, add(a, scale(ab, t))))


def swept_distance_sq(f0, f1, t0, t1, b0, b1):
    best = segment_distance_sq(t0, t1, b0, b1)
    for pair in ((f0, f1), (f1, t1), (f0, t0)):
        d = segment_distance_sq(pair[0], pair[1], b0, b1)
        if d < best:
            best = d
    return best
