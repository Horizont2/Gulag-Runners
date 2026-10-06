namespace GulagRunners.Sim
{
    /// <summary>
    /// The three slots of docs/02: weapon, armour, utility. No inventory, no grid, no menu.
    /// Picking something up replaces what was in that slot.
    ///
    /// Six bytes of round state, which is what docs/06 can afford to roll back every frame.
    /// </summary>
    public struct Inventory
    {
        public ItemId Weapon;
        public ItemId Armour;
        public ItemId Utility;

        /// <summary>Hits or chest openings left in the weapon. Zero when the slot is empty.</summary>
        public short WeaponDurability;

        public ItemDef WeaponDef => ItemTable.WeaponOrFists(Weapon);
        public ItemDef ArmourDef => ItemTable.Get(Armour);

        /// <summary>Bare hands: the weapon slot is empty, or whatever was in it has broken.</summary>
        public bool BareHanded => Weapon == ItemId.None;

        public ItemId SlotContents(ItemKind kind) => kind switch
        {
            ItemKind.Weapon => Weapon,
            ItemKind.Armour => Armour,
            ItemKind.Utility => Utility,
            _ => ItemId.None
        };

        /// <summary>
        /// Puts an item in its own slot and returns whatever it displaced, so the caller can drop
        /// it on the floor once there are floor items to drop it onto.
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

                case ItemKind.Armour:
                    displaced = Armour;
                    Armour = item;
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
