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

        /// <summary>
        /// Replaces one item's numbers with authored ones.
        ///
        /// The table above stays the source of truth for what EXISTS and what the game falls
        /// back to; this is how a data file edited in the inspector reaches the simulation.
        /// Nothing in the tick calls it — it runs once at load, from
        /// <see cref="GulagRunners.Game.ItemLibrary"/>, before the first frame.
        ///
        /// The determinism rule it must not break (docs/06): both devices have to apply the
        /// SAME overrides. That is a question about what is in the build, not about this
        /// method, and it is the same question any data file asks. A device running an older
        /// library desynchronises exactly as it would with an older scene.
        /// </summary>
        public static void Apply(in ItemDef def)
        {
            int i = (int)def.Id;
            if (i <= 0 || i >= Items.Length) return;

            ItemDef kept = def;
            kept.Id = def.Id;                            // an asset may not rename an item
            Items[i] = kept;
        }

        /// <summary>Back to the table in this file. For tests, and for a library removed.</summary>
        public static void Reset()
        {
            ItemDef[] fresh = Build();
            for (int i = 0; i < Items.Length; i++) Items[i] = fresh[i];
        }

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
            ItemDef[] t = new ItemDef[32];

            t[(int)ItemId.None] = new ItemDef { Id = ItemId.None, Kind = ItemKind.None };

            t[(int)ItemId.Fists] = new ItemDef
            {
                Id = ItemId.Fists,
                Kind = ItemKind.Weapon,
                Tier = 0,
                Damage = Fix.FromMilli(4000),      // docs/02 says 6 against weapons of 12-26;
                                                    // these weapons are 0.6 of that scale
                AttackFrames = 12,                  // 0.20 s
                Reach = Fix.FromMilli(600),
                Durability = 0,                     // never breaks
                PrySpeed = Fix.FromMilli(1000)      // the baseline every chest time is quoted at
            };

            t[(int)ItemId.Club] = new ItemDef
            {
                Id = ItemId.Club,
                Weight = 120,
                Kind = ItemKind.Weapon,
                Tier = 1,
                Damage = Fix.FromMilli(8000),
                AttackFrames = 15,                  // 0.25 s
                Reach = Fix.FromMilli(950),
                Durability = 14,
                PrySpeed = Fix.FromMilli(2000)      // twice as fast as hands, and much louder
            };

            t[(int)ItemId.Spear] = new ItemDef
            {
                Id = ItemId.Spear,
                Weight = 90,
                Kind = ItemKind.Weapon,
                Tier = 2,
                Damage = Fix.FromMilli(13000),
                AttackFrames = 27,                  // 0.45 s
                Reach = Fix.FromMilli(1700),
                Durability = 9,
                PrySpeed = Fix.FromMilli(1600)      // a worse lever than the club, and scarcer
            };

            t[(int)ItemId.Dagger] = new ItemDef
            {
                Id = ItemId.Dagger,
                Weight = 90,
                Kind = ItemKind.Weapon,
                Tier = 1,
                Damage = Fix.FromMilli(5000),       // a hair above bare hands, or why pick it up
                AttackFrames = 9,                   // 0.15 s
                Reach = Fix.FromMilli(700),
                Durability = 18,
                PrySpeed = Fix.FromMilli(1200)      // a terrible crowbar
            };

            t[(int)ItemId.Sword] = new ItemDef
            {
                Id = ItemId.Sword,
                Weight = 110,
                Kind = ItemKind.Weapon,
                Tier = 2,
                Damage = Fix.FromMilli(10000),
                AttackFrames = 19,                  // 0.32 s
                Reach = Fix.FromMilli(1150),
                Durability = 12,
                PrySpeed = Fix.FromMilli(1500)
            };

            t[(int)ItemId.Axe] = new ItemDef
            {
                Id = ItemId.Axe,
                Weight = 100,
                Kind = ItemKind.Weapon,
                Tier = 2,
                Damage = Fix.FromMilli(12000),
                AttackFrames = 23,                  // 0.38 s
                Reach = Fix.FromMilli(1050),
                Durability = 10,
                BlockPierce = 350,                  // it is what you reach for against a turtle
                PrySpeed = Fix.FromMilli(2400)      // the best lever in the game
            };

            // The short sword. Faster than the club and shorter, lasts longer than anything
            // but the dagger, and it is a poor crowbar — the first weapon you keep because you
            // want to FIGHT with it rather than because it opens chests.
            t[(int)ItemId.Gladius] = new ItemDef
            {
                Id = ItemId.Gladius,
                Weight = 100,
                Kind = ItemKind.Weapon,
                Tier = 1,
                Damage = Fix.FromMilli(7000),
                AttackFrames = 12,                  // 0.20 s, bare-handed speed with a blade on it
                Reach = Fix.FromMilli(900),
                Durability = 16,
                PrySpeed = Fix.FromMilli(1300)
            };

            // Slow and short for T1, and the first thing in the table that beats a raised
            // guard. docs/03 wants every item to have a counter-item; this is the early
            // answer to someone who blocks and waits.
            t[(int)ItemId.Mace] = new ItemDef
            {
                Id = ItemId.Mace,
                Weight = 80,
                Kind = ItemKind.Weapon,
                Tier = 1,
                Damage = Fix.FromMilli(9000),
                AttackFrames = 18,                  // 0.30 s
                Reach = Fix.FromMilli(850),
                Durability = 12,
                BlockPierce = 200,
                PrySpeed = Fix.FromMilli(1100)      // a mace is not a lever
            };

            // The quickest weapon that still has reach. It wins the exchange and loses the
            // trade: against the axe or the spear it has to hit twice for their once.
            t[(int)ItemId.Saber] = new ItemDef
            {
                Id = ItemId.Saber,
                Weight = 100,
                Kind = ItemKind.Weapon,
                Tier = 2,
                Damage = Fix.FromMilli(9000),
                AttackFrames = 14,                  // 0.23 s
                Reach = Fix.FromMilli(1100),
                Durability = 11,
                PrySpeed = Fix.FromMilli(1300)
            };

            // The longest reach in the game and slow enough that it had better land. It
            // out-ranges the spear and goes through a guard, which is the one combination the
            // table otherwise does not have.
            t[(int)ItemId.Scythe] = new ItemDef
            {
                Id = ItemId.Scythe,
                Weight = 70,
                Kind = ItemKind.Weapon,
                Tier = 2,
                Damage = Fix.FromMilli(11000),
                AttackFrames = 29,                  // 0.48 s
                Reach = Fix.FromMilli(1850),
                Durability = 8,
                BlockPierce = 500,
                PrySpeed = Fix.FromMilli(1200)
            };

            t[(int)ItemId.FlangedMace] = new ItemDef
            {
                Id = ItemId.FlangedMace,
                Weight = 100,
                Kind = ItemKind.Weapon,
                Tier = 3,
                Damage = Fix.FromMilli(12000),
                AttackFrames = 21,
                Reach = Fix.FromMilli(1300),
                Durability = 9,
                PrySpeed = Fix.FromMilli(1400),
                BlockPierce = 700                   // a shield is most of no use against it
            };

            t[(int)ItemId.Warhammer] = new ItemDef
            {
                Id = ItemId.Warhammer,
                Weight = 70,
                Kind = ItemKind.Weapon,
                Tier = 3,
                Damage = Fix.FromMilli(21000),
                AttackFrames = 34,                  // 0.57 s: everyone can see it coming
                Reach = Fix.FromMilli(1400),        // a hammer, not a polearm: it has to close
                Durability = 6,
                BlockPierce = 450,
                PrySpeed = Fix.FromMilli(2600)
            };

            t[(int)ItemId.LeatherVest] = new ItemDef
            {
                Id = ItemId.LeatherVest,
                Weight = 100,
                Kind = ItemKind.Armour,
                Tier = 1,
                DamageReduction = Fix.FromMilli(150)
            };

            t[(int)ItemId.Chainmail] = new ItemDef
            {
                Id = ItemId.Chainmail,
                Weight = 100,
                Kind = ItemKind.Armour,
                Tier = 2,
                DamageReduction = Fix.FromMilli(300)
            };

            t[(int)ItemId.Plate] = new ItemDef
            {
                Id = ItemId.Plate,
                Weight = 90,
                Kind = ItemKind.Armour,
                Tier = 3,
                DamageReduction = Fix.FromMilli(450)
            };

            t[(int)ItemId.Bandage] = new ItemDef
            {
                Id = ItemId.Bandage,
                Weight = 110,
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
