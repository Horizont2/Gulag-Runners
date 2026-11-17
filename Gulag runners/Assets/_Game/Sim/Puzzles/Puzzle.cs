namespace GulagRunners.Sim
{
    /// <summary>
    /// Which lock is on this chest. Three of the twelve in docs/04, one per difficulty.
    ///
    /// The other nine are the same shape as these -- a few right touches out of a handful of
    /// options -- except the two that are not: the weighing scales want a long swipe and the
    /// pressure valve wants a button held and released, and neither of those is a choice among
    /// options. They need their own input and they are not in this slice.
    /// </summary>
    public enum PuzzleKind : byte
    {
        None = 0,

        /// <summary>One pip. Four runes, one of them turned. Tap it. (docs/04 #1)</summary>
        OddOneOut = 1,

        /// <summary>Two pips. Three rings, two off centre, one move each, any order. (#5)</summary>
        Rings = 2,

        /// <summary>Three pips. Four gears, two must come off for the key to turn. (#10)</summary>
        Gears = 3
    }

    /// <summary>
    /// One lock, dealt from the round's seed.
    ///
    /// Every puzzle in this slice is the same thing underneath: a small number of RIGHT
    /// touches among a handful of options, either in order or in any order. That is not a
    /// simplification of docs/04 -- it is what nine of its twelve mechanics are. The rune, the
    /// ring and the gear differ in what the player is looking at, which is <see cref="Shape"/>
    /// and belongs to the thing that draws it; what the simulation has to know is which touches
    /// open the lock, and that is the same question three times.
    ///
    /// It is a value, it is dealt once at the start of a round, and it lives on the chest's
    /// state rather than beside it: a round is rolled back every frame (docs/06) and a puzzle
    /// that lived in a second array would be a second thing to keep in step.
    ///
    /// The numbers it obeys are docs/04's iron rules:
    ///
    ///   * at most THREE touches, because a puzzle a competent player cannot finish in three
    ///     seconds is cut;
    ///   * no text and no language anywhere in here -- the options are numbers and the look is
    ///     a shape index, so a release in any language ships the same lock;
    ///   * the pips are known BEFORE the first touch, so the player chooses whether it is worth
    ///     it, and both players see the same spread of difficulty because both see the same
    ///     chests;
    ///   * and the force route is untouched. This is the quiet way in, never the only way in.
    /// </summary>
    public struct Puzzle
    {
        public PuzzleKind Kind;

        /// <summary>1, 2 or 3. Shown on the lock before it is started (docs/04 rule 7).</summary>
        public byte Pips;

        /// <summary>How many things there are to touch, numbered 1..Options.</summary>
        public byte Options;

        /// <summary>How many right touches open it. Never more than three.</summary>
        public byte Needed;

        /// <summary>Whether they have to come in that order.</summary>
        public bool Ordered;

        /// <summary>The right touches, as option numbers. B and C are 0 when unused.</summary>
        public byte A, B, C;

        /// <summary>
        /// What it LOOKS like, for whatever draws it: which rune family is on the plate, where
        /// the three rings start, which gears are engaged. The simulation never reads it -- it
        /// is here so that the look is dealt from the seed too, and both players are looking at
        /// the same lock.
        /// </summary>
        public byte Shape;

        public bool Exists => Kind != PuzzleKind.None;

        /// <summary>Which option the n-th touch wants. 0 when there is no n-th touch.</summary>
        public int Wants(int n) => n == 0 ? A : (n == 1 ? B : (n == 2 ? C : 0));

        /// <summary>Every touch made, as a bit each, when the lock is open.</summary>
        public int Finished => Needed <= 0 ? 0 : (1 << Needed) - 1;

        public bool Solved(int done) => Exists && done == Finished;

        /// <summary>
        /// A touch. Returns what is done afterwards, or -1 when the touch was wrong.
        ///
        /// There is no attempt counter and no lock-out, because docs/04 rule 4 is that a
        /// mistake costs TIME and noise and never a closed door. What a wrong answer costs is
        /// decided by the motor, not here.
        /// </summary>
        public int Press(int done, int choice)
        {
            if (!Exists || choice < 1 || choice > Options) return -1;

            if (Ordered)
            {
                int next = Count(done);
                if (next >= Needed) return -1;
                return choice == Wants(next) ? done | (1 << next) : -1;
            }

            for (int i = 0; i < Needed; i++)
            {
                if ((done & (1 << i)) != 0) continue;
                if (Wants(i) == choice) return done | (1 << i);
            }
            return -1;
        }

        static int Count(int bits)
        {
            int n = 0;
            while (bits != 0) { n += bits & 1; bits >>= 1; }
            return n;
        }

        public static readonly Puzzle None = new Puzzle { Kind = PuzzleKind.None };
    }

    /// <summary>
    /// Dealing the locks for a round.
    ///
    /// Same shape as <see cref="LootTable.Deal"/>, and for the same reason: one stream per
    /// chest, forked from the round's, so that adding a chest at the far end of the arena does
    /// not reshuffle every chest before it.
    /// </summary>
    public static class PuzzleBank
    {
        /// <summary>
        /// How hard the lock on a chest of this kind is. The pips go with the tier, which is
        /// what docs/04 ties them to: one pip on the quick boxes, three on the safe.
        /// </summary>
        public static int PipsFor(ChestKind kind)
        {
            if (kind == ChestKind.Crate) return 1;
            if (kind == ChestKind.Locker) return 2;
            return 3;
        }

        /// <summary>One lock of the given difficulty.</summary>
        public static Puzzle Deal(ref Rng own, int pips)
        {
            if (pips <= 1) return OddOneOut(ref own);
            if (pips == 2) return Rings(ref own);
            return Gears(ref own);
        }

        /// <summary>
        /// Every chest that offers one gets a lock, and no two of them get the same lock:
        /// docs/04 says a puzzle does not repeat inside a match, and the point of that rule is
        /// that the second safe must not be the first safe with the answer already known.
        /// </summary>
        public static void Deal(ChestDef[] chests, ChestSimState[] states, uint seed)
        {
            if (chests == null || states == null) return;

            Rng round = new Rng(seed);
            int n = chests.Length < states.Length ? chests.Length : states.Length;

            for (int i = 0; i < n; i++)
            {
                if (!chests[i].HasPuzzle)
                {
                    states[i].Puzzle = Puzzle.None;
                    continue;
                }

                Rng own = round.Fork(i + 1);
                int pips = PipsFor(chests[i].Kind);
                Puzzle p = Deal(ref own, pips);

                // Re-roll a repeat, and give up after a few goes rather than spin: a bank of
                // three mechanics on an arena of many chests CAN run out of distinct locks,
                // and a round that never starts is worse than a lock seen twice.
                for (int tries = 0; tries < 8 && Seen(states, i, in p); tries++)
                    p = Deal(ref own, pips);

                states[i].Puzzle = p;
            }
        }

        static bool Seen(ChestSimState[] states, int upTo, in Puzzle p)
        {
            for (int j = 0; j < upTo; j++)
            {
                Puzzle q = states[j].Puzzle;
                if (q.Kind == p.Kind && q.Shape == p.Shape && q.A == p.A && q.B == p.B)
                    return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- the three locks

        /// <summary>
        /// docs/04 #1. Four runes, one of them turned; tap it. One touch, about a second and a
        /// half, and nothing to learn -- it is the lock that teaches a player that locks can be
        /// opened by looking instead of hitting.
        /// </summary>
        static Puzzle OddOneOut(ref Rng r)
        {
            int odd = r.Below(4) + 1;
            int family = r.Below(6);
            return new Puzzle
            {
                Kind = PuzzleKind.OddOneOut, Pips = 1, Options = 4, Needed = 1,
                Ordered = true, A = (byte)odd, Shape = (byte)family
            };
        }

        /// <summary>
        /// docs/04 #5. Three rings with a slot in each; line the slots up. Exactly two of them
        /// start one step off, so it is always two moves and never three, and either ring can
        /// be turned first because turning one does not move the others.
        ///
        /// The six options are the three rings times the two directions: ring*2 + 1 turns it
        /// one way, ring*2 + 2 the other. Shape packs where the three rings start, base three,
        /// so whatever draws it knows what the player is looking at without being told twice.
        /// </summary>
        static Puzzle Rings(ref Rng r)
        {
            int spare = r.Below(3);
            int shape = 0, a = 0, b = 0, found = 0;
            int power = 1;

            for (int ring = 0; ring < 3; ring++)
            {
                int off = ring == spare ? 1 : (r.Below(2) == 0 ? 0 : 2);
                shape += off * power;
                power *= 3;

                if (off == 1) continue;
                int option = ring * 2 + (off == 0 ? 1 : 2);
                if (found == 0) a = option; else b = option;
                found++;
            }

            return new Puzzle
            {
                Kind = PuzzleKind.Rings, Pips = 2, Options = 6, Needed = 2,
                Ordered = false, A = (byte)a, B = (byte)b, Shape = (byte)shape
            };
        }

        /// <summary>
        /// docs/04 #10. Four gears on the chain and an arrow on the key; two of them have to
        /// come off before it turns the way the arrow says. Either order.
        ///
        /// This is the only lock in the slice that needs a step of abstraction -- you are not
        /// matching a shape, you are counting -- which is why docs/04 puts it on three pips and
        /// on nothing else. The two that must go are dealt, and the chain is then dressed to
        /// agree: docs/04 asks the generator to guarantee a solution, not the player to hope
        /// for one.
        /// </summary>
        static Puzzle Gears(ref Rng r)
        {
            int a = r.Below(4) + 1;
            int b = r.Below(3) + 1;
            if (b >= a) b++;                       // distinct, without a retry that could spin

            int engaged = r.Below(16) | (1 << (a - 1)) | (1 << (b - 1));
            return new Puzzle
            {
                Kind = PuzzleKind.Gears, Pips = 3, Options = 4, Needed = 2,
                Ordered = false, A = (byte)a, B = (byte)b, Shape = (byte)engaged
            };
        }
    }
}
