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

        /// <summary>
        /// The lock on it, dealt from the round's seed (docs/04). None on a chest that was
        /// authored without one, and on every chest while the arena has no puzzles in it.
        ///
        /// It lives on the state rather than in an array beside it because a round is rolled
        /// back every frame and a second array would be a second thing to keep in step. It
        /// never changes during a round, so rolling it back costs a copy and nothing else.
        /// </summary>
        public Puzzle Puzzle;

        /// <summary>
        /// Which of the lock's touches have been made, a bit each.
        ///
        /// This does NOT drain and it does NOT belong to the opener. docs/04 is explicit about
        /// both: being hit in the middle of a puzzle keeps the progress, because losing it is
        /// frustration rather than difficulty, and a safe somebody else started can be
        /// finished -- taking another player's work is the sweetest moment in the game.
        /// </summary>
        public byte PuzzleDone;

        /// <summary>
        /// Frames left of the sulk after a wrong touch. While it runs the lock takes nothing,
        /// which is the whole price of a mistake: time, plus the noise that was already made.
        /// There is no attempt limit and never a lock-out (docs/04 rule 4).
        /// </summary>
        public byte PuzzleStun;

        public static ChestSimState Fresh() => new ChestSimState
        {
            Progress = 0, Opened = false, Opener = -1, Contents = ItemId.None,
            Puzzle = Puzzle.None, PuzzleDone = 0, PuzzleStun = 0
        };

        public static ChestSimState[] FreshSet(int count)
        {
            ChestSimState[] set = new ChestSimState[count];
            for (int i = 0; i < count; i++) set[i] = Fresh();
            return set;
        }
    }
}
