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

            // 1.2 s by hand, 0.6 s with a club. Cheap either way, on purpose.
            c.Crate = new ChestKindConfig
            {
                BareFrames = 72,
                BareNoise = NoiseLevel.Quiet,
                ToolNoise = NoiseLevel.Quiet,
                ToolWear = 1
            };

            // 5 s by hand, 2.5 s with a club, and loud: this is the first real "time against
            // safety" bet of the match.
            c.Locker = new ChestKindConfig
            {
                BareFrames = 300,
                BareNoise = NoiseLevel.Medium,
                ToolNoise = NoiseLevel.Loud,
                ToolWear = 2
            };

            // Six seconds by force, straight from docs/03, and loud however you do it. Three
            // durability is a third of a spear, which is the "~35% of the weapon" the doc asks for.
            c.Safe = new ChestKindConfig
            {
                BareFrames = 360,
                BareNoise = NoiseLevel.Loud,
                ToolNoise = NoiseLevel.Loud,
                ToolWear = 3
            };

            c.DecayMultiplier = 2;
            c.NoiseFrames = 20;
            return c;
        }
    }
}
