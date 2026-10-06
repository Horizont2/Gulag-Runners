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

        public static ChestSimState Fresh() => new ChestSimState { Progress = 0, Opened = false, Opener = -1 };

        public static ChestSimState[] FreshSet(int count)
        {
            ChestSimState[] set = new ChestSimState[count];
            for (int i = 0; i < count; i++) set[i] = Fresh();
            return set;
        }
    }
}
