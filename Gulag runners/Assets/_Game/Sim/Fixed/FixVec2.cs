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

        public bool Equals(FixVec2 other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is FixVec2 o && Equals(o);
        public override int GetHashCode() => (X.Raw * 397) ^ Y.Raw;
        public override string ToString() => "(" + X + ", " + Y + ")";
    }
}
