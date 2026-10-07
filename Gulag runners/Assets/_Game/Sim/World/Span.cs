namespace GulagRunners.Sim
{
    /// <summary>
    /// How deep into the scene one piece of collision reaches, in metres of Z.
    ///
    /// The fight is flat and always will be — movement is X and Y, and docs/02 is firm that
    /// this is what makes it readable on a phone. But a level built in 3D has a FOREGROUND and
    /// a BACKGROUND, and flattening both onto one plane is what puts a wall nobody can see in
    /// front of a fighter. So depth is not simulated; it is only ever asked one question:
    /// is this box on the same slice of the level as that body. That is cheap, it is integer,
    /// and it is the whole difference between a location that reads and one that fights you.
    /// </summary>
    public struct Span
    {
        public Fix Min;
        public Fix Max;

        public Span(Fix min, Fix max) { Min = min; Max = max; }

        /// <summary>Everything, for a world that does not care about depth at all.</summary>
        /// A kilometre either way: wider than any arena, and narrow enough that Max - Min
        /// cannot overflow the fixed-point range the way int.MaxValue would.
        public static Span Everywhere => new Span(Fix.FromMilli(-1000000), Fix.FromMilli(1000000));

        /// <summary>Does a body of this half-depth, standing at this depth, reach into it.</summary>
        public bool Reaches(Fix depth, Fix half) => depth + half > Min && depth - half < Max;

        /// <summary>The nearest depth inside this span, kept a body's width off the lip.</summary>
        public Fix Nearest(Fix depth, Fix inset)
        {
            Fix room = (Max - Min) / 2;
            if (inset > room) inset = room;

            Fix lo = Min + inset, hi = Max - inset;
            if (depth < lo) return lo;
            if (depth > hi) return hi;
            return depth;
        }

        public bool Contains(Fix depth, Fix inset)
        {
            Fix room = (Max - Min) / 2;
            if (inset > room) inset = room;
            return depth >= Min + inset && depth <= Max - inset;
        }
    }
}
