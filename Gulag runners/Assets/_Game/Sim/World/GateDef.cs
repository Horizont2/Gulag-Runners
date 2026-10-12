namespace GulagRunners.Sim
{
    /// <summary>When a gate shuts. docs/01 gives the last minute two separate beats.</summary>
    public enum GateWhen : byte
    {
        /// <summary>
        /// During the closing phase, in order, spread across the shutter window: the outer
        /// rooms going one after another until only the middle is left.
        /// </summary>
        Closing = 0,

        /// <summary>
        /// On the first tick of sudden death — the ceiling of docs/01's central hall, which
        /// is what puts both fighters on one platform.
        /// </summary>
        SuddenDeath = 1
    }

    /// <summary>Which way a gate shoves a body that it shuts on, or behind.</summary>
    public enum GateToward : byte
    {
        MinusX = 0,
        PlusX = 1,
        MinusY = 2,
        PlusY = 3
    }

    /// <summary>
    /// One steel shutter: a piece of the arena that is not there until the clock says so.
    ///
    /// docs/01's closing phase shuts the outer rooms one after another until only the central
    /// hall is left. That is a change to collision, not a change to the picture, so it has to
    /// live in the deterministic layer — and the arena itself is baked once and never mutated,
    /// so a gate cannot be a box that gets added. Instead every gate's boxes ARE baked, from
    /// the start, and simply do not count until the gate's bit goes up: the only state is a
    /// bitmask, which both devices compute from the frame number and so can never disagree
    /// about (docs/06).
    ///
    /// The shove is the part that stops it being a bug. A shutter that merely appears can seal
    /// a fighter into a wing with no way out, which is not "the rooms are closing" — it is the
    /// match ending by accident. So on the tick a gate shuts, anything on its far side comes
    /// through.
    /// </summary>
    public struct GateDef
    {
        /// <summary>
        /// The doorway the shutter fills: the union of its boxes. Used for the shove and for
        /// the view, not for collision — collision is the baked solids that point at this gate.
        /// </summary>
        public Aabb Box;

        /// <summary>How deep it reaches.</summary>
        public Span Span;

        /// <summary>0 shuts first. Ties keep the order the scene listed them in.</summary>
        public int Order;

        /// <summary>Which beat of the last minute this gate belongs to.</summary>
        public GateWhen When;

        /// <summary>Which way a body caught on the wrong side is brought.</summary>
        public GateToward Toward;

        /// <summary>
        /// Where a body shoved by this gate ends up, given how wide and tall that body is: just
        /// clear of the shutter, on the side it herds towards.
        /// </summary>
        public FixVec2 Clear(FixVec2 feet, Fix halfWidth, Fix height, Fix skin)
        {
            switch (Toward)
            {
                case GateToward.MinusX: return new FixVec2(Box.MinX - halfWidth - skin, feet.Y);
                case GateToward.PlusX:  return new FixVec2(Box.MaxX + halfWidth + skin, feet.Y);
                case GateToward.MinusY: return new FixVec2(feet.X, Box.MinY - height - skin);
                default:                return new FixVec2(feet.X, Box.MaxY + skin);
            }
        }

        /// <summary>
        /// Is this body on the side the gate is shutting off? True for a body standing in the
        /// doorway as well as one stranded past it, because both have to come through.
        ///
        /// Judged on the body's own middle rather than its edges: a fighter brushing the
        /// shutter from the inside is already where the gate wants them, and shoving them for
        /// it would move them for free every time a wall shuts next to them.
        /// </summary>
        public bool Behind(FixVec2 feet, Fix height)
        {
            Fix midY = feet.Y + height / 2;
            switch (Toward)
            {
                case GateToward.MinusX: return feet.X > Box.MinX;
                case GateToward.PlusX:  return feet.X < Box.MaxX;
                case GateToward.MinusY: return midY > Box.MinY;
                default:                return midY < Box.MaxY;
            }
        }
    }
}
