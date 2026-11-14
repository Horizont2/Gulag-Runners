namespace GulagRunners.Sim
{
    /// <summary>Opening cost for one kind of chest.</summary>
    public struct ChestKindConfig
    {
        /// <summary>Frames to open it bare-handed. A weapon divides this by its pry speed.</summary>
        public int BareFrames;

        /// <summary>How loud opening it is with bare hands, and with something to lever it with.</summary>
        public NoiseLevel BareNoise;
        public NoiseLevel ToolNoise;

        /// <summary>Durability spent when a weapon is used on it instead of hands.</summary>
        public int ToolWear;
    }

    /// <summary>What a lock costs to get wrong, and what it costs to get right (docs/04).</summary>
    public struct PuzzleConfig
    {
        /// <summary>
        /// How long the lock takes nothing after a wrong touch. Sixty frames is the one
        /// second docs/04 asks for, and it is the ONLY price of a mistake -- there is no
        /// attempt counter, because a lock-out would turn an optimisation into a barrier.
        /// </summary>
        public int WrongFrames;

        /// <summary>
        /// A wrong touch is loud, on purpose. It is the sound that tells the other player
        /// where you are, and it is also the sound a player can make deliberately on a far
        /// safe to pull them the wrong way. docs/04 calls that emergent and keeps it.
        /// </summary>
        public NoiseLevel WrongNoise;

        /// <summary>
        /// And solving it is quiet, which is the entire reason to learn the locks: forcing
        /// a safe is six seconds heard across half the arena, and this is three and nearly
        /// silent.
        /// </summary>
        public NoiseLevel SolveNoise;

        public static PuzzleConfig Default() => new PuzzleConfig
        {
            WrongFrames = 60,
            WrongNoise = NoiseLevel.Loud,
            SolveNoise = NoiseLevel.Quiet
        };
    }

    /// <summary>
    /// Every number behind the chest table of docs/03, in one place.
    ///
    /// The quoted times in that table are the ones with a weapon in hand; bare-handed is slower
    /// and quieter. That difference is the whole scavenge phase in one trade: the weapon is also
    /// the crowbar, so forcing two extra chests is paid for in the fight afterwards.
    /// </summary>
    public struct ChestConfig
    {
        public ChestKindConfig Crate;
        public ChestKindConfig Locker;
        public ChestKindConfig Safe;

        /// <summary>How many times faster progress drains when the button is released.</summary>
        public int DecayMultiplier;

        /// <summary>How often opening re-announces itself, in frames.</summary>
        public int NoiseFrames;

        /// <summary>The quiet way in: what a lock costs (docs/04).</summary>
        public PuzzleConfig Puzzle;

        public ChestKindConfig For(ChestKind kind) => kind switch
        {
            ChestKind.Crate => Crate,
            ChestKind.Locker => Locker,
            _ => Safe
        };

        public static ChestConfig Default()
        {
            ChestConfig c;

            // 2.5 s by hand, 1.25 s with a club, and the same for all three kinds: the chest
            // kind decides the loot tier, not the price of getting at it. docs/03 priced them
            // apart, and that pricing comes back when the kinds are placed deliberately rather
            // than scattered — see the note in ChestTuning.
            ChestKindConfig same = new ChestKindConfig
            {
                BareFrames = 150,
                BareNoise = NoiseLevel.Medium,
                ToolNoise = NoiseLevel.Loud,
                ToolWear = 2
            };

            c.Crate = same;
            c.Locker = same;
            c.Safe = same;

            c.DecayMultiplier = 2;
            c.NoiseFrames = 20;
            c.Puzzle = PuzzleConfig.Default();
            return c;
        }
    }
}
