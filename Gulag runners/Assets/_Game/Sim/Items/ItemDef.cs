namespace GulagRunners.Sim
{
    /// <summary>
    /// What one item does, in fixed point.
    ///
    /// docs/03 allows three bars in the UI — DAMAGE / SPEED / REACH — and no numbers anywhere a
    /// player can see them. These are the numbers behind those bars.
    /// </summary>
    public struct ItemDef
    {
        public ItemId Id;
        public ItemKind Kind;
        /// <summary>0 for fists, 1 white, 2 blue, 3 gold. Drives the glow, and nothing else.</summary>
        public byte Tier;

        // ---- weapon
        public Fix Damage;
        /// <summary>Frames from pressing attack to the hit landing. Lower is faster.</summary>
        public int AttackFrames;
        /// <summary>How far in front of the body the hit reaches, in metres.</summary>
        public Fix Reach;

        /// <summary>
        /// Hits, or chest openings, left in it. docs/03: everything is consumed, so there is no
        /// "I found the final weapon and won".
        /// </summary>
        public int Durability;

        /// <summary>
        /// How much faster this opens a chest than bare hands. The weapon is also your crowbar,
        /// and that is the whole tension of the scavenge phase: force two extra chests and you
        /// meet the other player with your fists.
        /// </summary>
        public Fix PrySpeed;

        /// <summary>
        /// How much of the damage goes through a raised guard, in thousandths, when that is more
        /// than the ordinary chip. docs/03 asks that every item have a counter-item; this is the
        /// flail's, and the thing it counters is a player who never lowers their shield.
        /// </summary>
        public int BlockPierce;

        // ---- armour
        /// <summary>Fraction of incoming damage removed. docs/02: T1/T2/T3 = 15/30/45%.</summary>
        public Fix DamageReduction;

        // ---- utility
        public int Heal;

        public bool IsWeapon => Kind == ItemKind.Weapon;
        public bool Breakable => Durability > 0;
    }
}
