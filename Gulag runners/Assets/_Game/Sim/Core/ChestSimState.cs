namespace GulagRunners.Sim
{
    /// <summary>
    /// What changes about one chest during a round. Eight bytes, because docs/06 rolls the whole
    /// round state back every frame.
    ///
    /// Progress counts in thousandths of a bare-handed frame, so that a weapon used as a lever can
    /// advance it faster without any of this becoming a float.
    /// </summary>
    public struct ChestSimState
    {
        public int Progress;
        public bool Opened;

        /// <summary>
        /// Index of the player working on it, or -1. Only the opener may change the progress,
        /// which is what lets two players tick independently and still agree on the result.
        /// </summary>
        public int Opener;

        /// <summary>
        /// What is in it this round. Dealt at the start from the round's seed, unless the
        /// chest was authored with something pinned in it — see <see cref="LootTable.Deal"/>.
        /// It lives here rather than on the def because the def is the arena, which does not
        /// change between rounds, and this does.
        /// </summary>
        public ItemId Contents;

        public static ChestSimState Fresh() => new ChestSimState
        {
            Progress = 0, Opened = false, Opener = -1, Contents = ItemId.None
        };

        public static ChestSimState[] FreshSet(int count)
        {
            ChestSimState[] set = new ChestSimState[count];
            for (int i = 0; i < count; i++) set[i] = Fresh();
            return set;
        }
    }
}
