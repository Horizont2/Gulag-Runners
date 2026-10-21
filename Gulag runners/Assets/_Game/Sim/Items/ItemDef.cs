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

        /// <summary>
        /// How often this comes up against the others of its tier. 0 keeps it out of chests
        /// altogether, which is what fists are and what a tutorial-only item would be.
        ///
        /// Rarity within a tier is a separate dial from the tier itself on purpose: a scythe
        /// and a sword are both T2 and should not be equally common, because one of them is a
        /// plan most players will not want and the other is the plan everything else is read
        /// against.
        /// </summary>
        public int Weight;

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
        /// <summary>
        /// Incoming damage removed, in thousandths. docs/02: T1/T2/T3 = 15/30/45%, which is
        /// now what a full SET of that rarity comes to rather than one piece.
        ///
        /// An int and not a Fix, unlike the metres and the damage above. Every reader of it
        /// wanted thousandths and called ToMilli() to get them, and that round trip is
        /// lossy: Fix.FromMilli(270).ToMilli() is 269, because 0.270 has no exact Q16.16.
        /// One piece could absorb that; three pieces summing could not, and the fixed-point
        /// mirror in docs/refs would have been three thousandths out from the C# with
        /// nothing to show which was right.
        /// </summary>
        public int DamageReductionPermille;

        /// <summary>
        /// What a raised guard lets through, in thousandths, for someone carrying this.
        /// 0 means the ordinary chip — which is what every item that is not a shield wants,
        /// and conveniently what an unset struct field already is.
        ///
        /// This is the shield's whole reason to exist. Plate removes nearly half of every hit
        /// and does nothing for your guard; a shield barely softens a hit you take standing
        /// and makes the hits you MEET almost free. One armour slot, two opposite plans.
        /// </summary>
        public int BlockChipPermille;

        /// <summary>
        /// Extra frames on the parry window. docs/02 makes the parry the skill ceiling of the
        /// medieval mode; a shield is what widens it enough to go looking for.
        /// </summary>
        public int ParryBonusFrames;

        /// <summary>
        /// How much of the hitstun this takes off, in thousandths. The helmet's stat.
        ///
        /// It is applied to the damage the blow earns, BEFORE the floor that guarantees the
        /// attacker his turn on the first hit of a string — so a helmet never turns landing
        /// a clean blow into a disadvantage, which is a bug this project has already had
        /// once and measured at -3 to -12 frames across the whole weapon table. What it
        /// shortens is the second hit and the third: stacked on top of the combo decay, a
        /// great helm is the difference between being held in a string and getting out of
        /// one.
        /// </summary>
        public int StunResistPermille;

        /// <summary>
        /// How much longer a raised guard holds before it costs a charge, in thousandths.
        /// The belt's stat.
        ///
        /// Blocking drains a charge every GuardDrainFrames, so a full pool is 3.2 s of
        /// standing braced; +500 makes it 4.8 s. That is the belt's whole plan — not
        /// surviving the blow, but being allowed to wait for it.
        /// </summary>
        public int GuardHoldPermille;

        // ---- utility
        public int Heal;

        /// <summary>
        /// The tier, named. T1 is common, T2 rare, anything above legendary — so a new tier
        /// added later reads as legendary rather than as nothing.
        /// </summary>
        public Rarity Rarity => Tier <= 1 ? Rarity.Common
                              : Tier == 2 ? Rarity.Rare
                              : Rarity.Legendary;

        public bool IsWeapon => Kind == ItemKind.Weapon;

        /// <summary>Worn rather than held: helmet, chest or belt. All three sum into one
        /// damage reduction, so anything asking "is this armour" means all three.</summary>
        public bool IsArmour => Kind == ItemKind.Armour || Kind == ItemKind.Helmet ||
                                Kind == ItemKind.Belt;

        public bool Breakable => Durability > 0;
    }
}
