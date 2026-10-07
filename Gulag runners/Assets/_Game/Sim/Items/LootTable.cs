namespace GulagRunners.Sim
{
    /// <summary>
    /// What comes out of a chest.
    ///
    /// docs/03 makes the chest types a question — how much information about yourself will you
    /// trade for this loot — and the answer has to be worth the trade in a way the player can
    /// learn. So the chest KIND decides the tier, and rarity within the tier is a second dial:
    ///
    ///   Crate   quick and quiet, and mostly T1. It exists so nobody is still bare-handed at
    ///           0:10, and a crate that can hand out a warhammer would make the quiet option
    ///           the greedy one too.
    ///   Locker  the first real bet: slow, loud, standing still in the open. Mostly T2, with a
    ///           tenth of a chance at gold, which is what makes it a bet rather than a tax.
    ///   Safe    half the time gold. Six seconds and half the arena hears you.
    ///
    /// Both sides of the arena get the same chest kinds and different contents (docs/01's
    /// symmetric start, asymmetric result), which falls out of rolling each chest from its own
    /// stream rather than sharing one.
    ///
    /// Pure and deterministic: the same seed is the same arena on both devices (docs/06).
    /// </summary>
    public static class LootTable
    {
        /// <summary>Chance of each tier, per chest kind, in percent. Rows must sum to 100.</summary>
        static readonly int[,] TierOdds =
        {
            //  T1   T2   T3
            {   80,  20,   0 },      // Crate
            {   35,  55,  10 },      // Locker
            {    5,  45,  50 },      // Safe
        };

        static readonly int[] Scratch = new int[32];
        static readonly ItemId[] Pool = new ItemId[32];

        /// <summary>
        /// One item for this chest. <see cref="ItemId.None"/> only if a tier has been emptied
        /// of everything that can drop, which is a table to fix rather than a roll to retry.
        /// </summary>
        public static ItemId Roll(ChestKind kind, ref Rng rng)
        {
            int tier = RollTier(kind, ref rng);
            return RollInTier(tier, ref rng);
        }

        /// <summary>Which tier this chest kind is paying out this time: 1, 2 or 3.</summary>
        public static int RollTier(ChestKind kind, ref Rng rng)
        {
            int row = (int)kind;
            if (row < 0 || row > TierOdds.GetUpperBound(0)) row = 0;

            int roll = rng.Below(100);
            for (int t = 0; t < 3; t++)
            {
                roll -= TierOdds[row, t];
                if (roll < 0) return t + 1;
            }
            return 1;                                    // a row that does not sum to 100
        }

        /// <summary>One item of this tier, each as likely as its weight.</summary>
        public static ItemId RollInTier(int tier, ref Rng rng)
        {
            int count = 0;
            for (int i = 0; i < 32; i++)
            {
                ItemDef d = ItemTable.Get((ItemId)i);
                if (d.Kind == ItemKind.None || d.Tier != tier || d.Weight <= 0) continue;
                Pool[count] = d.Id;
                Scratch[count] = d.Weight;
                count++;
            }

            int pick = rng.Weighted(Scratch, count);
            return pick < 0 ? ItemId.None : Pool[pick];
        }

        /// <summary>
        /// Fills in every chest that was not given its contents by hand. An authored item wins:
        /// pinning one chest is how a test, or a tutorial, stops being at the mercy of a roll.
        /// </summary>
        public static void Deal(ChestDef[] chests, ChestSimState[] states, uint seed)
        {
            if (chests == null || states == null) return;

            Rng round = new Rng(seed);
            int n = chests.Length < states.Length ? chests.Length : states.Length;

            for (int i = 0; i < n; i++)
            {
                if (chests[i].Contents != ItemId.None)
                {
                    states[i].Contents = chests[i].Contents;
                    continue;
                }

                // Its own stream, so adding a chest to the far end of the arena does not
                // reshuffle every chest before it.
                Rng own = round.Fork(i + 1);
                states[i].Contents = Roll(chests[i].Kind, ref own);
            }
        }
    }
}
