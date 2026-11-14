namespace GulagRunners.Sim
{
    /// <summary>
    /// One tick of intent: the buttons, and where the attack stick is pointing.
    ///
    /// It is one value rather than a flags field plus a "read the stick" call, and that
    /// matters more than it looks. Rollback replays a tick from a stored input, and
    /// anything the simulation reads that was NOT in that stored value is a desync
    /// waiting for a bad connection. A stick sampled separately from the buttons is
    /// exactly that: it would be whatever the thumb is doing at replay time rather than
    /// what it was doing at tick time.
    ///
    /// The aim is already QUANTISED to thousandths here, at the boundary, before the
    /// simulation ever sees it. A float joystick reading is the one place a platform
    /// difference could get in, so it is converted once, where it enters, and never
    /// again.
    /// </summary>
    public struct TickInput
    {
        public InputFlags Flags;

        /// <summary>Attack stick, in thousandths. -1000..1000 on each axis, 0 for centred.</summary>
        public short AimX;
        public short AimY;

        public TickInput(InputFlags flags, int aimX = 0, int aimY = 0)
        {
            Flags = flags;
            AimX = Clamp(aimX);
            AimY = Clamp(aimY);
        }

        public static readonly TickInput None = new TickInput(InputFlags.None);

        /// <summary>The stick as the simulation wants it. Not normalised: length is push.</summary>
        public FixVec2 Aim => new FixVec2(Fix.FromMilli(AimX), Fix.FromMilli(AimY));

        public bool Has(InputFlags bit) => (Flags & bit) != 0;
        public int MoveX() => Flags.MoveX();
        public int MoveY() => Flags.MoveY();

        /// <summary>
        /// Everything held, plus anything tapped since the last tick.
        ///
        /// Buttons accumulate because a tap between two ticks must not be lost. The stick
        /// does NOT accumulate -- it takes the newer reading, because an aim is a position
        /// and the one that counts is where the thumb ended up.
        /// </summary>
        public static TickInput Merge(TickInput older, TickInput newer)
        {
            // The buttons OR together. The touch on a lock cannot: option 1 and option 2
            // OR'd are option 3, which is a wrong answer nobody gave. The FIRST touch in the
            // window wins, for the same reason the buttons accumulate at all -- it is the one
            // the thumb actually made, and the second one belongs to the next tick.
            int first = older.Flags.Choice();
            InputFlags bits = (older.Flags | newer.Flags) & ~InputFlags.ChoiceMask;
            int choice = first != 0 ? first : newer.Flags.Choice();
            return new TickInput(bits.WithChoice(choice), newer.AimX, newer.AimY);
        }

        /// <summary>Which thing on a lock this tick touched, 1..7, or 0.</summary>
        public int Choice() => Flags.Choice();

        static short Clamp(int v) => (short)(v < -1000 ? -1000 : (v > 1000 ? 1000 : v));
    }
}
