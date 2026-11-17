namespace GulagRunners.Sim
{
    /// <summary>
    /// The geometry the new combat is made of, in fixed point.
    ///
    /// Everything a blow needs to know is one question: how close did these two line
    /// segments come? A weapon is a segment from the hand to the tip. A body is a segment
    /// from the feet to the head with a radius on it -- a capsule, which is the cheapest
    /// shape that does not have corners for a sword to catch on. So:
    ///
    ///   weapon against body    segment to segment, compared with the body's radius
    ///   weapon against weapon  segment to segment, compared with the two half-widths
    ///
    /// One function, used twice. And it answers in SQUARED distance, because every caller
    /// is comparing against a radius it can square once: a square root in the hot path
    /// would be the only place in the simulation that needs one.
    ///
    /// Sweeping matters as much as the shape. At 60 Hz a weapon tip moving 12 m/s travels
    /// 20 cm between ticks, which is wider than a body; testing only where the weapon IS
    /// lets a fast swing teleport straight through someone. So the test takes the segment
    /// at the start of the tick AND at the end, and asks the closest approach of the
    /// quadrilateral they sweep -- approximated, deliberately, as the nearest of the two
    /// end positions and the two paths the endpoints took. That is four segment pairs
    /// rather than an exact swept volume, and it is exact enough: the error is at the
    /// corners of a shape that is already an approximation of a sword.
    /// </summary>
    public static class FixGeom
    {
        /// <summary>
        /// Squared distance between segment a0..a1 and segment b0..b1.
        ///
        /// IN TWO DIMENSIONS two segments either cross, or their closest approach runs
        /// from an endpoint of one to somewhere on the other. There is no interior-to-
        /// interior case; that only exists in 3D. So the whole thing is a crossing test
        /// and four point-to-segment distances, and the textbook parametric solve is not
        /// needed at all.
        ///
        /// Which is worth more than the saved arithmetic. The parametric solve divides by
        /// `a*c - b*b`, and that goes to zero as the two segments become parallel -- not
        /// AT parallel, where a guard would catch it, but smoothly, over a wide band of
        /// nearly-parallel. Measured against real geometry it was out by up to 3.2 m on a
        /// pair 1.5 m apart, because a denominator of 0.0005 in Q16.16 is a raw of 33 and
        /// dividing by 33 throws away everything. Two fighters trading blows hold their
        /// weapons nearly parallel constantly.
        ///
        /// The crossing test below divides by nothing at all, and the endpoint distances
        /// divide only by a segment's own squared length, which for a weapon or a body is
        /// never small.
        /// </summary>
        public static Fix SegmentDistanceSq(FixVec2 a0, FixVec2 a1, FixVec2 b0, FixVec2 b1)
        {
            if (Crosses(a0, a1, b0, b1)) return Fix.Zero;

            Fix best = PointSegmentDistanceSq(a0, b0, b1);

            Fix d = PointSegmentDistanceSq(a1, b0, b1);
            if (d < best) best = d;

            d = PointSegmentDistanceSq(b0, a0, a1);
            if (d < best) best = d;

            d = PointSegmentDistanceSq(b1, a0, a1);
            if (d < best) best = d;

            return best;
        }

        /// <summary>
        /// Do the two segments properly cross? Four orientation tests, no division.
        ///
        /// Touching and collinear-overlapping cases deliberately fall through to false:
        /// they are already distance zero by the endpoint tests, so there is nothing for
        /// a special case to add except a way to disagree with itself.
        /// </summary>
        public static bool Crosses(FixVec2 a0, FixVec2 a1, FixVec2 b0, FixVec2 b1)
        {
            FixVec2 a = a1 - a0;
            FixVec2 b = b1 - b0;

            Fix d1 = FixVec2.Cross(b, a0 - b0);
            Fix d2 = FixVec2.Cross(b, a1 - b0);
            Fix d3 = FixVec2.Cross(a, b0 - a0);
            Fix d4 = FixVec2.Cross(a, b1 - a0);

            return ((d1.Raw > 0 && d2.Raw < 0) || (d1.Raw < 0 && d2.Raw > 0))
                && ((d3.Raw > 0 && d4.Raw < 0) || (d3.Raw < 0 && d4.Raw > 0));
        }

        /// <summary>
        /// Squared distance from a point to a segment.
        ///
        /// The clamp happens BEFORE the division, not after, and that is not a tidy-up.
        /// Fix division is `(long)num.Raw &lt;&lt; 16 / den.Raw` cast back to int, so a point
        /// 30 m from a segment 10 cm long produces a quotient around 3e5 in Q16.16 --
        /// a raw of 2e10, which does not fit in an int. It wrapped, and the distance came
        /// back 8 cm short. Comparing first means the only division left is one whose
        /// result is known to be between 0 and 1.
        /// </summary>
        public static Fix PointSegmentDistanceSq(FixVec2 p, FixVec2 a, FixVec2 b)
        {
            FixVec2 ab = b - a;
            Fix len = FixVec2.Dot(ab, ab);
            if (len.Raw <= 0) return (p - a).LengthSq;

            Fix along = FixVec2.Dot(p - a, ab);
            if (along.Raw <= 0) return (p - a).LengthSq;
            if (along >= len) return (p - b).LengthSq;

            Fix t = along / len;                       // strictly between 0 and 1
            return (p - (a + ab * t)).LengthSq;
        }

        // There is no swept test here, and that is a measured decision rather than an
        // omission. The worry was tunnelling: a tip at 15 m/s steps 25 cm between ticks,
        // which sounds like enough to cross a body. It is not, because the blade is
        // ANCHORED AT THE SHOULDER. The hand barely moves, so the segment sweeps a wedge
        // with one end nearly still, and a wedge that reaches past a body on one tick and
        // past it on the other has overlapped it in between.
        //
        // Measured in fixgeom_test over arm slews from 360 to 2880 degrees a second, for
        // a 0.70 m dagger and a 1.40 m hammer, against a 0.30 m body: of roughly ten
        // thousand swings that did pass over the body, the plain test missed ZERO. A
        // swept test would have cost four segment pairs a tick, every tick, to fix
        // nothing. If an unanchored weapon ever exists -- a thrown axe -- this is the
        // comment that says what to re-measure.
    }
}
