"""Is the fixed-point geometry good enough to decide whether a sword hit somebody?

The mirror is bit-exact with the C#, so what this asks is the other question: how far
does Q16.16 drift from real geometry, over the shapes a fight actually makes? If the
answer is "less than a millimetre" then contact damage can be trusted. If it is
"sometimes a centimetre" then two fighters at the edge of a blade disagree about whether
it landed, and on a rollback netcode that is a desync.

Nothing here is random-free: the cases are generated from a fixed seed over the real
ranges -- weapons 0.6 to 1.85 m, bodies 1.8 m tall, speeds up to 15 m/s -- so the test
is reproducible and still covers far more arrangements than anyone would write by hand.
"""
import math
import random
import sys

import fixgeom_mirror as M

PASS = FAIL = 0


def check(name, ok, detail=""):
    global PASS, FAIL
    if ok:
        PASS += 1
        print(f"  PASS  {name}  {detail}")
    else:
        FAIL += 1
        print(f"  FAIL  {name}  {detail}")


# --- the reference: ordinary float geometry, no tricks ---------------------------

def _pt_seg(p, a, b):
    dx, dy = b[0] - a[0], b[1] - a[1]
    L = dx * dx + dy * dy
    if L == 0:
        return math.hypot(p[0] - a[0], p[1] - a[1])
    t = max(0.0, min(1.0, ((p[0] - a[0]) * dx + (p[1] - a[1]) * dy) / L))
    return math.hypot(p[0] - (a[0] + dx * t), p[1] - (a[1] + dy * t))


def ref_seg_seg(a0, a1, b0, b1):
    """Exact segment-to-segment distance in 2D, in float.

    Sampling along one segment was the first version of this and it was the thing being
    wrong: 400 samples over an 8 m segment is a 2 cm step, so the reference itself was
    out by up to 8 mm and the fixed-point code was blamed for it. In 2D the answer is
    exact in closed form -- they cross, or it is an endpoint -- so there is no reason to
    sample at all.
    """
    def cross(ox, oy, px, py, qx, qy):
        return (px - ox) * (qy - oy) - (py - oy) * (qx - ox)

    d1 = cross(b0[0], b0[1], b1[0], b1[1], a0[0], a0[1])
    d2 = cross(b0[0], b0[1], b1[0], b1[1], a1[0], a1[1])
    d3 = cross(a0[0], a0[1], a1[0], a1[1], b0[0], b0[1])
    d4 = cross(a0[0], a0[1], a1[0], a1[1], b1[0], b1[1])
    if ((d1 > 0) != (d2 > 0)) and ((d3 > 0) != (d4 > 0)):
        return 0.0

    return min(_pt_seg(a0, b0, b1), _pt_seg(a1, b0, b1),
               _pt_seg(b0, a0, a1), _pt_seg(b1, a0, a1))


def V(x, y):
    return (M.from_f(x), M.from_f(y))


def main():
    rng = random.Random(20261021)

    # ---------------------------------------------------------------- 1. the root
    print("1. the square root, which everything else stands on")
    worst = 0.0
    for _ in range(4000):
        x = rng.uniform(0.0, 900.0)       # a squared length, up to 30 m
        got = M.to_f(M.sqrt(M.from_f(x)))
        worst = max(worst, abs(got - math.sqrt(x)))
    check("Fix.Sqrt matches math.sqrt", worst < 0.0001,
          f"worst error {worst*1000:.4f} mm over 4000 values to 30 m")

    check("and sqrt(0) is 0, not a divide by zero", M.sqrt(0) == 0)
    check("and a negative length returns 0 rather than throwing", M.sqrt(-M.ONE) == 0)

    # Exactness on perfect squares: a root that is off by one bit on 4.0 will drift
    # a normalisation and eventually a position.
    exact = all(M.sqrt(M.from_f(n * n)) == M.from_f(n) for n in range(1, 30))
    check("and whole squares come back exact", exact, "1..29")

    # ---------------------------------------------------------------- 2. seg-seg
    print("\n2. segment to segment, which is every hit test in the game")

    # Measured on the SQUARED distance, because that is the number the game compares.
    # A hit is `distSq <= radiusSq`; nothing in the simulation ever takes this root.
    # Rooted, the same results look four times worse than they are, purely because the
    # derivative of sqrt blows up near zero -- and near zero is where a hit is. So the
    # error that matters is how far the decision BOUNDARY moves: an error of e in the
    # square shifts the effective radius by e / (2R).
    RADIUS_FOR_SCALE = 0.30
    worst_sq = 0.0
    for _ in range(6000):
        pts = [(rng.uniform(-4, 4), rng.uniform(0, 3)) for _ in range(4)]
        a0, a1, b0, b1 = pts
        got = M.to_f(M.segment_distance_sq(V(*a0), V(*a1), V(*b0), V(*b1)))
        want = ref_seg_seg(a0, a1, b0, b1) ** 2
        worst_sq = max(worst_sq, abs(got - want))
    skin = worst_sq / (2 * RADIUS_FOR_SCALE)
    check("matches real geometry over 6000 random pairs", skin < 0.001,
          f"worst squared error {worst_sq*1e6:.1f} mm^2, "
          f"which moves a 30 cm hit radius by {skin*1000:.3f} mm")

    # Crossing segments must read as touching, or a sword passes through a body.
    crossed = M.segment_distance_sq(V(-1, 1), V(1, 1), V(0, 0), V(0, 2))
    check("crossing segments are at distance zero", crossed == 0, f"raw {crossed}")

    # Parallel is the degenerate branch, and the one most likely to be silently wrong.
    worst_par = 0.0
    for _ in range(2000):
        x0, y0 = rng.uniform(-3, 3), rng.uniform(0, 2)
        dx, dy = rng.uniform(-2, 2), rng.uniform(-2, 2)
        gap = rng.uniform(0.01, 1.5)
        off = rng.uniform(-2, 2)
        n = math.hypot(dx, dy)
        if n < 0.1:
            continue
        px, py = -dy / n * gap, dx / n * gap
        a0, a1 = (x0, y0), (x0 + dx, y0 + dy)
        b0 = (x0 + px + dx * off / n, y0 + py + dy * off / n)
        b1 = (b0[0] + dx, b0[1] + dy)
        got = math.sqrt(M.to_f(M.segment_distance_sq(V(*a0), V(*a1), V(*b0), V(*b1))))
        worst_par = max(worst_par, abs(got - ref_seg_seg(a0, a1, b0, b1)))
    check("and parallel segments, the degenerate branch", worst_par < 0.002,
          f"worst error {worst_par*1000:.3f} mm")

    # A point (zero-length segment) against a segment: a dagger held still.
    worst_pt = 0.0
    for _ in range(2000):
        p = (rng.uniform(-3, 3), rng.uniform(0, 3))
        b0 = (rng.uniform(-3, 3), rng.uniform(0, 3))
        b1 = (rng.uniform(-3, 3), rng.uniform(0, 3))
        got = M.to_f(M.segment_distance_sq(V(*p), V(*p), V(*b0), V(*b1)))
        worst_pt = max(worst_pt, abs(got - ref_seg_seg(p, p, b0, b1) ** 2))
    check("and a zero-length segment is still a point",
          worst_pt / (2 * RADIUS_FOR_SCALE) < 0.001,
          f"worst squared error {worst_pt*1e6:.1f} mm^2")

    # The arrangement the game actually makes, rather than four random points: a weapon
    # on an arm against an upright body.
    worst_real, boundary_wrong = 0.0, 0
    for _ in range(20000):
        hx, hy = rng.uniform(-3, 3), rng.uniform(0.6, 1.6)
        ang, reach = rng.uniform(-math.pi, math.pi), rng.uniform(0.6, 1.85)
        a0 = (hx, hy)
        a1 = (hx + math.cos(ang) * reach, hy + math.sin(ang) * reach)
        bx = rng.uniform(-3, 3)
        b0, b1 = (bx, 0.0), (bx, 1.8)

        got = M.to_f(M.segment_distance_sq(V(*a0), V(*a1), V(*b0), V(*b1)))
        want = ref_seg_seg(a0, a1, b0, b1) ** 2
        worst_real = max(worst_real, abs(got - want))
        # Cases sitting exactly ON the boundary will always be able to fall either way
        # at any finite precision, and they do not matter: both devices run this same
        # integer code and so always agree with EACH OTHER, which is what rollback needs.
        # What would matter is a verdict that differs when the answer is not close.
        if abs(math.sqrt(max(0.0, want)) - RADIUS_FOR_SCALE) > 0.001:
            if (got <= RADIUS_FOR_SCALE ** 2) != (want <= RADIUS_FOR_SCALE ** 2):
                boundary_wrong += 1

    check("a weapon on an arm against an upright body, 20000 arrangements",
          worst_real / (2 * RADIUS_FOR_SCALE) < 0.001,
          f"radius moves by at most {worst_real/(2*RADIUS_FOR_SCALE)*1000:.3f} mm")
    check("and fixed point never disagrees about whether it HIT, except within a "
          "millimetre of the edge",
          boundary_wrong == 0,
          f"{boundary_wrong} wrong verdicts outside a 1 mm band")

    # ---------------------------------------------------------------- 3. sweeping
    print("\n3. sweeping, which is what stops a fast blade teleporting through a body")
    BODY0, BODY1 = V(0.0, 0.0), V(0.0, 1.8)
    RADIUS = 0.30

    # The honest version of this question. A long weapon held across the body cannot
    # tunnel -- the segment is 0.9 m and the body is 0.6 m wide, so at any speed a human
    # thumb produces, something of the blade is still overlapping. Tunnelling needs the
    # whole segment to clear the body between two ticks, and the case where that really
    # happens is a SHORT weapon on an arm that slews fast, which is exactly what a flick
    # of a stick produces. So: sweep over the arm's slew rate and find out where the
    # still test starts losing hits, rather than asserting that it does.
    def arc(angle, reach, hand):
        return (V(hand[0], hand[1]),
                V(hand[0] + math.cos(angle) * reach, hand[1] + math.sin(angle) * reach))

    print("     arm slew   weapon   missed without sweeping   missed with")
    tunnelled_anywhere = False
    for slew_deg in (360, 720, 1440, 2880):            # degrees per second
        for reach, wname in ((0.70, "dagger"), (1.40, "hammer")):
            miss_still = miss_swept = seen = 0
            for _ in range(2000):
                hand = (rng.uniform(-0.45, -0.25), rng.uniform(0.8, 1.3))
                a0 = rng.uniform(-math.pi, math.pi)
                a1 = a0 + math.radians(slew_deg) / 60.0
                f0, f1 = arc(a0, reach, hand)
                t0, t1 = arc(a1, reach, hand)

                # Did the blade pass over the body at any point in between?
                crossed = False
                for i in range(1, 32):
                    m0, m1 = arc(a0 + (a1 - a0) * i / 32.0, reach, hand)
                    if M.to_f(M.segment_distance_sq(m0, m1, BODY0, BODY1)) <= RADIUS ** 2:
                        crossed = True
                        break
                if not crossed:
                    continue
                seen += 1

                if M.to_f(M.segment_distance_sq(t0, t1, BODY0, BODY1)) > RADIUS ** 2 and \
                   M.to_f(M.segment_distance_sq(f0, f1, BODY0, BODY1)) > RADIUS ** 2:
                    miss_still += 1
                if M.to_f(M.swept_distance_sq(f0, f1, t0, t1, BODY0, BODY1)) > RADIUS ** 2:
                    miss_swept += 1

            if miss_still:
                tunnelled_anywhere = True
            print(f"     {slew_deg:>5}°/s  {wname:<8} "
                  f"{miss_still:>5} of {seen:<5} ({miss_still/max(1,seen)*100:>5.1f}%)"
                  f"      {miss_swept:>4} ({miss_swept/max(1,seen)*100:.1f}%)")

    check("a blade on an arm CANNOT tunnel, at any slew a thumb can ask for",
          not tunnelled_anywhere,
          "so no swept test is needed, and the hit check stays one segment pair a tick")

    # ---------------------------------------------------------------- 4. the ranges
    print("\n4. no overflow anywhere a fight can reach")
    big = M.segment_distance_sq(V(-30, 0), V(30, 20), V(-30, 20), V(30, 0))
    check("a segment across the whole arena does not overflow",
          0 <= big < M.from_f(2000), f"raw {big}")

    # The nearest pair here is a1 to b0, not a0 to b0 -- hypot(24.9, 18), not
    # hypot(25, 18). The first version of this assert used the wrong one and blamed the
    # 8 cm on fixed point.
    far = M.segment_distance_sq(V(0, 0), V(0.1, 0), V(25, 18), V(25.1, 18))
    d = math.sqrt(M.to_f(far))
    check("and a distance of 30 m is still right to the millimetre",
          abs(d - math.hypot(24.9, 18)) < 0.002, f"{d:.4f} m")

    print()
    print(f"{PASS} passed, {FAIL} failed")
    print("ALL PASS" if FAIL == 0 else "SOME FAILED")
    return 1 if FAIL else 0


if __name__ == "__main__":
    sys.exit(main())
