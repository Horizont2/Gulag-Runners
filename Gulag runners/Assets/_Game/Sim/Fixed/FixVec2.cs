using System;

namespace GulagRunners.Sim
{
    /// <summary>
    /// 2D vector on the gameplay plane. The arena is 3D but the fight always happens on a
    /// single flat plane (docs/02-combat-and-controls.md), so the simulation is 2D.
    /// X is along the arena, Y is up. Depth is presentation only.
    /// </summary>
    [Serializable]
    public struct FixVec2 : IEquatable<FixVec2>
    {
        public Fix X;
        public Fix Y;

        public FixVec2(Fix x, Fix y) { X = x; Y = y; }

        public static readonly FixVec2 Zero = new FixVec2(Fix.Zero, Fix.Zero);

        public static FixVec2 operator +(FixVec2 a, FixVec2 b) => new FixVec2(a.X + b.X, a.Y + b.Y);
        public static FixVec2 operator -(FixVec2 a, FixVec2 b) => new FixVec2(a.X - b.X, a.Y - b.Y);
        public static FixVec2 operator *(FixVec2 a, Fix s) => new FixVec2(a.X * s, a.Y * s);
        public static FixVec2 operator /(FixVec2 a, Fix s) => new FixVec2(a.X / s, a.Y / s);
        public static FixVec2 operator -(FixVec2 a) => new FixVec2(-a.X, -a.Y);

        public static Fix Dot(FixVec2 a, FixVec2 b) => a.X * b.X + a.Y * b.Y;

        /// <summary>
        /// The z of the cross product. Sign is which side of a you are on, which is how
        /// the arm knows whether it is swinging over or under.
        /// </summary>
        public static Fix Cross(FixVec2 a, FixVec2 b) => a.X * b.Y - a.Y * b.X;

        /// <summary>
        /// Length squared. Prefer this: almost everything that wants a length is really
        /// comparing one against another, and a comparison of squares needs no root and
        /// loses no bits.
        /// </summary>
        public Fix LengthSq => X * X + Y * Y;

        public Fix Length => Fix.Sqrt(LengthSq);

        /// <summary>
        /// Unit vector, or zero for a zero vector. Zero rather than NaN on purpose: the
        /// aim stick reads exactly zero whenever a thumb is off the glass, which is most
        /// of every match.
        /// </summary>
        public FixVec2 Normalized
        {
            get
            {
                Fix len = Length;
                return len.Raw == 0 ? Zero : new FixVec2(X / len, Y / len);
            }
        }

        /// <summary>This vector scaled to exactly `length`, or zero if it has no direction.</summary>
        public FixVec2 WithLength(Fix length)
        {
            Fix len = Length;
            return len.Raw == 0 ? Zero : new FixVec2(X * length / len, Y * length / len);
        }

        /// <summary>Shortened to `max` if longer, otherwise unchanged.</summary>
        public FixVec2 ClampLength(Fix max)
        {
            Fix sq = LengthSq;
            return sq <= max * max ? this : WithLength(max);
        }

        public FixVec2 Perpendicular => new FixVec2(-Y, X);

        public bool Equals(FixVec2 other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is FixVec2 o && Equals(o);
        public override int GetHashCode() => (X.Raw * 397) ^ Y.Raw;
        public override string ToString() => "(" + X + ", " + Y + ")";
    }
}
