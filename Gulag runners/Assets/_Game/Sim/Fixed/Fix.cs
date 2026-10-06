using System;

namespace GulagRunners.Sim
{
    /// <summary>
    /// Q16.16 fixed-point number.
    /// The simulation uses this instead of float everywhere: rollback netcode needs
    /// bit-identical results on every device, and float is not guaranteed to give them.
    /// See docs/06-tech-and-netcode.md.
    /// Range is about +-32768 with a resolution of 1/65536.
    /// </summary>
    [Serializable]
    public readonly struct Fix : IEquatable<Fix>, IComparable<Fix>
    {
        public const int FracBits = 16;
        public const int RawOne = 1 << FracBits;

        public readonly int Raw;

        public Fix(int raw) { Raw = raw; }

        public static readonly Fix Zero = new Fix(0);
        public static readonly Fix One = new Fix(RawOne);
        public static readonly Fix Half = new Fix(RawOne / 2);

        public static Fix FromInt(int value) => new Fix(value << FracBits);

        /// <summary>FromMilli(3600) == 3.6. The readable way to write tuning constants.</summary>
        public static Fix FromMilli(int thousandths) =>
            new Fix((int)(((long)thousandths << FracBits) / 1000));

        /// <summary>Floor towards negative infinity, like the shift it wraps.</summary>
        public int ToInt() => Raw >> FracBits;

        public int ToMilli() => (int)(((long)Raw * 1000) >> FracBits);

        public static Fix operator +(Fix a, Fix b) => new Fix(a.Raw + b.Raw);
        public static Fix operator -(Fix a, Fix b) => new Fix(a.Raw - b.Raw);
        public static Fix operator -(Fix a) => new Fix(-a.Raw);

        public static Fix operator *(Fix a, Fix b) =>
            new Fix((int)(((long)a.Raw * b.Raw) >> FracBits));

        public static Fix operator /(Fix a, Fix b) =>
            new Fix((int)(((long)a.Raw << FracBits) / b.Raw));

        public static Fix operator *(Fix a, int b) => new Fix(a.Raw * b);
        public static Fix operator /(Fix a, int b) => new Fix(a.Raw / b);

        public static bool operator <(Fix a, Fix b) => a.Raw < b.Raw;
        public static bool operator >(Fix a, Fix b) => a.Raw > b.Raw;
        public static bool operator <=(Fix a, Fix b) => a.Raw <= b.Raw;
        public static bool operator >=(Fix a, Fix b) => a.Raw >= b.Raw;
        public static bool operator ==(Fix a, Fix b) => a.Raw == b.Raw;
        public static bool operator !=(Fix a, Fix b) => a.Raw != b.Raw;

        public static Fix Abs(Fix v) => v.Raw < 0 ? new Fix(-v.Raw) : v;
        public static Fix Min(Fix a, Fix b) => a.Raw < b.Raw ? a : b;
        public static Fix Max(Fix a, Fix b) => a.Raw > b.Raw ? a : b;
        public static int Sign(Fix v) => v.Raw > 0 ? 1 : (v.Raw < 0 ? -1 : 0);

        public static Fix Clamp(Fix v, Fix min, Fix max) =>
            v.Raw < min.Raw ? min : (v.Raw > max.Raw ? max : v);

        /// <summary>Moves current towards target by at most maxDelta. No division, no float.</summary>
        public static Fix MoveTowards(Fix current, Fix target, Fix maxDelta)
        {
            Fix diff = target - current;
            if (Abs(diff) <= maxDelta) return target;
            return current + (diff.Raw > 0 ? maxDelta : -maxDelta);
        }

        public bool Equals(Fix other) => Raw == other.Raw;
        public override bool Equals(object obj) => obj is Fix other && Raw == other.Raw;
        public override int GetHashCode() => Raw;
        public int CompareTo(Fix other) => Raw.CompareTo(other.Raw);

        /// <summary>Debug only. Never feed this back into the simulation.</summary>
        public override string ToString() => (Raw / (double)RawOne).ToString("0.###");
    }
}
