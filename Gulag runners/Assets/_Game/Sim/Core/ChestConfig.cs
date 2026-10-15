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
            return c;
        }
    }
}
