namespace GulagRunners.Sim
{
    public enum GroundItemState : byte
    {
        /// <summary>This slot of the pool is unused.</summary>
        None = 0,
        /// <summary>Still in the air, on its way out of a chest or out of a swap.</summary>
        Flying = 1,
        /// <summary>On the floor, waiting to be walked over.</summary>
        Resting = 2
    }

    /// <summary>
    /// One item lying in the world.
    ///
    /// Loot does not teleport into a slot when a chest opens: it comes out, arcs, and lands.
    /// That is the payoff for the time and the noise the player just spent, it is what makes a
    /// full slot a decision rather than an accident, and an item left on the floor is information
    /// for whoever walks past it later (docs/01, docs/03).
    ///
    /// Simulation state, like everything that decides a match: both devices must agree on where
    /// it landed and who got it (docs/06).
    /// </summary>
    public struct GroundItem
    {
        public ItemId Item;
        public GroundItemState State;

        /// <summary>Centre of the item.</summary>
        public FixVec2 Position;
        public FixVec2 Velocity;

        /// <summary>Frames before anybody may pick it up. Stops a swap re-taking what it dropped.</summary>
        public int PickupLock;

        /// <summary>
        /// Counts up for as long as this item exists. Presentation uses it to offset the bob, so
        /// a row of dropped items does not pulse in time with each other, and it is the age the
        /// pool recycles by.
        /// </summary>
        public int Age;

        public bool Live => State != GroundItemState.None;
        public bool Takeable => State == GroundItemState.Resting && PickupLock <= 0;

        public static GroundItem[] Pool(int size)
        {
            if (size < 0) size = 0;
            return new GroundItem[size];
        }
    }
}
