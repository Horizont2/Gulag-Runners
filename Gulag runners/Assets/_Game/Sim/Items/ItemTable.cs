namespace GulagRunners.Sim
{
    /// <summary>
    /// The loot table of docs/03, medieval mode, cut to the M0 set.
    ///
    /// Four findable items, chosen so that each one is a different PLAN rather than a different
    /// number. A sword and an axe are two sets of statistics; a club and a spear are two ways to
    /// fight, and that is what a playtest can actually answer a question about.
    ///
    ///   Fists      the baseline. Opens a chest slowly, and quietly.
    ///   Club       T1. Fast, short, lasts. Opens chests quickly and loudly.
    ///   Spear      T2. Slow, long, hurts, breaks sooner. Punishes the approach the club wants.
    ///   Chainmail  T2. -30% damage taken, and visible in the silhouette.
    ///   Bandage    T1. The one decision that is not about fighting: heal now, or keep looking.
    ///
    /// Static data, looked up by id. Nothing here allocates and nothing here is a float.
    /// </summary>
    public static class ItemTable
    {
        static readonly ItemDef[] Items = Build();

        public static ItemDef Get(ItemId id)
        {
            int i = (int)id;
            return i >= 0 && i < Items.Length ? Items[i] : Items[0];
        }

        public static ItemKind KindOf(ItemId id) => Get(id).Kind;

        /// <summary>An empty weapon slot still swings: bare hands are the T0 weapon.</summary>
        public static ItemDef WeaponOrFists(ItemId id) =>
            id == ItemId.None ? Get(ItemId.Fists) : Get(id);

        static ItemDef[] Build()
        {
            ItemDef[] t = new ItemDef[8];

            t[(int)ItemId.None] = new ItemDef { Id = ItemId.None, Kind = ItemKind.None };

            t[(int)ItemId.Fists] = new ItemDef
            {
                Id = ItemId.Fists,
                Kind = ItemKind.Weapon,
                Tier = 0,
                Damage = Fix.FromMilli(8000),
                AttackFrames = 12,                  // 0.20 s
                Reach = Fix.FromMilli(600),
                Durability = 0,                     // never breaks
                PrySpeed = Fix.FromMilli(1000)      // the baseline every chest time is quoted at
            };

            t[(int)ItemId.Club] = new ItemDef
            {
                Id = ItemId.Club,
                Kind = ItemKind.Weapon,
                Tier = 1,
                Damage = Fix.FromMilli(14000),
                AttackFrames = 15,                  // 0.25 s
                Reach = Fix.FromMilli(950),
                Durability = 14,
                PrySpeed = Fix.FromMilli(2000)      // twice as fast as hands, and much louder
            };

            t[(int)ItemId.Spear] = new ItemDef
            {
                Id = ItemId.Spear,
                Kind = ItemKind.Weapon,
                Tier = 2,
                Damage = Fix.FromMilli(24000),
                AttackFrames = 27,                  // 0.45 s
                Reach = Fix.FromMilli(1700),
                Durability = 9,
                PrySpeed = Fix.FromMilli(1600)      // a worse lever than the club, and scarcer
            };

            t[(int)ItemId.Chainmail] = new ItemDef
            {
                Id = ItemId.Chainmail,
                Kind = ItemKind.Armour,
                Tier = 2,
                DamageReduction = Fix.FromMilli(300)
            };

            t[(int)ItemId.Bandage] = new ItemDef
            {
                Id = ItemId.Bandage,
                Kind = ItemKind.Utility,
                Tier = 1,
                Heal = 30
            };

            for (int i = 0; i < t.Length; i++) t[i].Id = (ItemId)i;
            t[(int)ItemId.None].Kind = ItemKind.None;
            return t;
        }
    }
}
