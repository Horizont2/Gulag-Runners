namespace GulagRunners.Sim
{
    /// <summary>
    /// What a fighter is wearing and holding: weapon, helmet, chest armour, belt, utility.
    /// No inventory, no grid, no menu. Picking something up replaces what was in that slot.
    ///
    /// docs/02 started at three slots, and three was one idea short. A fighter is read from
    /// across the arena by his SILHOUETTE, and a silhouette has a head and a waist; folding
    /// both into one "armour" square meant a coif and a breastplate competing for the same
    /// slot, so finding a helmet was a reason to throw your plate away. Five slots, one per
    /// place on the body the eye already looks, and each piece a different plan:
    ///
    ///   Weapon   damage, reach, speed — and the crowbar.
    ///   Helmet   a little reduction, and the hitstun of the blows after the first.
    ///   Armour   the chest. The big share of the reduction.
    ///   Belt     a little reduction, and how long a guard holds.
    ///   Utility  the one decision that is not about fighting.
    ///
    /// Ten bytes of round state, which is what docs/06 can afford to roll back every frame.
    /// </summary>
    public struct Inventory
    {
        public ItemId Weapon;
        public ItemId Helmet;
        public ItemId Armour;
        public ItemId Belt;
        public ItemId Utility;

        /// <summary>Hits or chest openings left in the weapon. Zero when the slot is empty.</summary>
        public short WeaponDurability;

        public ItemDef WeaponDef => ItemTable.WeaponOrFists(Weapon);
        public ItemDef HelmetDef => ItemTable.Get(Helmet);
        public ItemDef ArmourDef => ItemTable.Get(Armour);
        public ItemDef BeltDef => ItemTable.Get(Belt);

        /// <summary>Bare hands: the weapon slot is empty, or whatever was in it has broken.</summary>
        public bool BareHanded => Weapon == ItemId.None;

        /// <summary>
        /// Damage removed by everything worn, in thousandths, BEFORE the cap.
        ///
        /// A plain sum, and the cap is the caller's (CombatConfig.MaxArmourPermille), so the
        /// cap is a number a designer can see and move rather than one buried in here. The
        /// three pieces of one rarity share out docs/02's own -15/-30/-45%, so a full
        /// legendary set is 450 and the cap never fires on the shipped table.
        /// </summary>
        public int ArmourPermille => HelmetDef.DamageReductionPermille +
                                     ArmourDef.DamageReductionPermille +
                                     BeltDef.DamageReductionPermille;

        /// <summary>Hitstun removed by everything worn, in thousandths, before the cap.</summary>
        public int StunResistPermille => HelmetDef.StunResistPermille +
                                         ArmourDef.StunResistPermille +
                                         BeltDef.StunResistPermille;

        /// <summary>Extra time a raised guard holds, in thousandths, from everything worn.</summary>
        public int GuardHoldPermille => HelmetDef.GuardHoldPermille +
                                        ArmourDef.GuardHoldPermille +
                                        BeltDef.GuardHoldPermille;

        /// <summary>Extra frames on the parry window, from everything worn.</summary>
        public int ParryBonusFrames => HelmetDef.ParryBonusFrames +
                                       ArmourDef.ParryBonusFrames +
                                       BeltDef.ParryBonusFrames;

        /// <summary>
        /// What a raised guard lets through, in thousandths. Zero is "the ordinary chip",
        /// which is what every piece that is not a shield wants and what an unset field
        /// already is — so the best piece wins rather than the pieces multiplying. Three
        /// shields' worth of chip reduction stacking would make a guard free.
        /// </summary>
        public int BlockChipPermille
        {
            get
            {
                int best = 0;
                Best(HelmetDef.BlockChipPermille, ref best);
                Best(ArmourDef.BlockChipPermille, ref best);
                Best(BeltDef.BlockChipPermille, ref best);
                return best;
            }
        }

        static void Best(int chip, ref int best)
        {
            if (chip <= 0) return;                   // this piece has no opinion
            if (best == 0 || chip < best) best = chip;
        }

        public ItemId SlotContents(ItemKind kind) => kind switch
        {
            ItemKind.Weapon => Weapon,
            ItemKind.Helmet => Helmet,
            ItemKind.Armour => Armour,
            ItemKind.Belt => Belt,
            ItemKind.Utility => Utility,
            _ => ItemId.None
        };

        /// <summary>
        /// Puts an item in its own slot and returns whatever it displaced, so the caller can
        /// drop it on the floor once there are floor items to drop it onto.
        /// </summary>
        public ItemId Equip(ItemId item)
        {
            ItemDef def = ItemTable.Get(item);
            ItemId displaced;

            switch (def.Kind)
            {
                case ItemKind.Weapon:
                    displaced = Weapon;
                    Weapon = item;
                    WeaponDurability = (short)def.Durability;
                    break;

                case ItemKind.Helmet:
                    displaced = Helmet;
                    Helmet = item;
                    break;

                case ItemKind.Armour:
                    displaced = Armour;
                    Armour = item;
                    break;

                case ItemKind.Belt:
                    displaced = Belt;
                    Belt = item;
                    break;

                case ItemKind.Utility:
                    displaced = Utility;
                    Utility = item;
                    break;

                default:
                    return ItemId.None;
            }

            return displaced;
        }

        /// <summary>
        /// Spends the weapon. It breaks at zero and the slot empties, which is the moment the
        /// scavenge phase is really about: you chose to spend it on a chest.
        /// </summary>
        public bool SpendWeapon(int amount)
        {
            if (Weapon == ItemId.None || amount <= 0) return false;
            if (!ItemTable.Get(Weapon).Breakable) return false;

            WeaponDurability -= (short)amount;
            if (WeaponDurability > 0) return false;

            Weapon = ItemId.None;
            WeaponDurability = 0;
            return true;                            // broke
        }
    }
}
