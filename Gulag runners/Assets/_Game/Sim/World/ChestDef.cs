namespace GulagRunners.Sim
{
    /// <summary>
    /// The chest types of docs/03 that M0 needs. Every one of them is a different answer to the
    /// same question: how much information about yourself will you trade for this loot?
    /// </summary>
    public enum ChestKind : byte
    {
        /// <summary>Quick and quiet, T1. Exists so that nobody is still bare-handed at 0:10.</summary>
        Crate = 0,

        /// <summary>Slow and loud, and you stand still in the open while it runs. The first real bet.</summary>
        Locker = 1,

        /// <summary>
        /// The locked one. Six seconds by force, or a puzzle later (docs/04) that is quick and
        /// quiet. The puzzle is the point of difference of the whole game; forcing it is the
        /// fallback that keeps the game playable while the puzzles do not exist yet.
        /// </summary>
        Safe = 2
    }

    /// <summary>
    /// One chest as the simulation sees it: a box, a type and what is inside. Static for the whole
    /// round — what changes is <see cref="ChestSimState"/>.
    /// </summary>
    public struct ChestDef
    {
        public Aabb Box;
        public ChestKind Kind;
        public ItemId Contents;

        /// <summary>
        /// This chest offers a puzzle as the quiet way in. Nothing reads it yet: docs/04 is a
        /// separate piece of work, and until it lands every safe is forced open.
        /// </summary>
        public bool HasPuzzle;
    }
}
