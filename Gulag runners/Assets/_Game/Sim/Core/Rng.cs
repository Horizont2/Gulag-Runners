namespace GulagRunners.Sim
{
    /// <summary>
    /// The simulation's only source of chance.
    ///
    /// System.Random is out: docs/06 needs two devices to produce the same round from the same
    /// seed, and .NET makes no promise that its generator is the same sequence across runtimes
    /// or versions. This is xorshift32 — eight lines, no allocation, no float, and the same
    /// numbers everywhere there is a 32-bit integer.
    ///
    /// It is a struct and it is passed by ref on purpose: a copy that silently forked the
    /// stream would hand two chests the same roll, and that is the kind of bug that only shows
    /// up as "the arena feels samey".
    ///
    /// Pinned by a known answer, so this and the Python mirror that checks the loot economy
    /// cannot drift apart without a test going red. new Rng(1), called six times:
    ///
    ///     270369, 67634689, 2647435461, 307599695, 2398689233, 745495504
    ///
    /// and new Rng(0) must start at 0x9E3779B9 = 2654435769, because zero is the one state
    /// xorshift can never leave.
    /// </summary>
    public struct Rng
    {
        uint _state;

        /// <summary>Zero is not a state xorshift can leave, so it is mapped off.</summary>
        public Rng(uint seed) { _state = seed == 0 ? 0x9E3779B9u : seed; }

        public uint State => _state;

        public uint Next()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        /// <summary>0 to limit-1. Returns 0 for a limit of one or less.</summary>
        public int Below(int limit)
        {
            if (limit <= 1) return 0;
            return (int)(Next() % (uint)limit);
        }

        /// <summary>
        /// An index into a weight table, each entry as likely as its weight. -1 when nothing
        /// in the table can come up at all, which is a table worth fixing rather than a roll
        /// worth retrying.
        /// </summary>
        public int Weighted(int[] weights, int count)
        {
            if (weights == null || count <= 0) return -1;

            int total = 0;
            for (int i = 0; i < count && i < weights.Length; i++)
                if (weights[i] > 0) total += weights[i];
            if (total <= 0) return -1;

            int roll = Below(total);
            for (int i = 0; i < count && i < weights.Length; i++)
            {
                if (weights[i] <= 0) continue;
                roll -= weights[i];
                if (roll < 0) return i;
            }
            return -1;                                   // unreachable while total > 0
        }

        /// <summary>
        /// A stream of its own, derived from this one. Two things that each need chance — one
        /// chest and the next — should not have to be rolled in a fixed order to agree, so each
        /// gets its own generator seeded from the round's.
        /// </summary>
        public Rng Fork(int salt)
        {
            uint s = _state ^ (uint)(salt * 0x9E3779B9);
            s ^= s >> 15;
            s *= 0x2545F491u;
            return new Rng(s);
        }
    }
}
