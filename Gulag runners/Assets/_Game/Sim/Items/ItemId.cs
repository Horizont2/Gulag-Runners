namespace GulagRunners.Sim
{
    /// <summary>
    /// Every item in the game, as one byte.
    ///
    /// An id rather than an object because the whole round state has to serialise into under
    /// 2 KB for rollback (docs/06), and because the simulation may not allocate: a tick that
    /// allocates is a tick that can be interrupted by the garbage collector at a different
    /// moment on each device.
    ///
    /// The medieval table of docs/03, cut to what M0 needs. docs/07 builds the medieval mode
    /// first and the firearm mode only once the core is confirmed, so there is deliberately no
    /// gun here yet.
    /// </summary>
    public enum ItemId : byte
    {
        None = 0,

        /// <summary>T0. Not an item you can find — what you have when the weapon slot is empty.</summary>
        Fists = 1,

        /// <summary>T1 weapon. Quick, short, and it lasts: the "I found something" item.</summary>
        Club = 2,

        /// <summary>T2 weapon. Slow, long, hits hard, breaks sooner. The opposite plan to the club.</summary>
        Spear = 3,

        /// <summary>T1. Quickest in the game and shortest: it has to be earned by closing in.</summary>
        Dagger = 4,

        /// <summary>T2. The middle of the table — the one the others are read against.</summary>
        Sword = 5,

        /// <summary>T2. Slower and heavier than the sword, and the best lever in the game.</summary>
        Axe = 6,

        /// <summary>T3. Goes through a guard like nothing else. The answer to turtling.</summary>
        FlangedMace = 7,

        /// <summary>T3. If it lands, the fight is decided.</summary>
        Warhammer = 8,

        /// <summary>T1. A short sword: faster than the club, shorter, and a poor crowbar.</summary>
        Gladius = 9,

        /// <summary>T1. Short and slow for its tier, but the first thing that beats a guard.</summary>
        Mace = 10,

        /// <summary>T2. The quickest weapon with real reach. Wins exchanges, loses trades.</summary>
        Saber = 11,

        /// <summary>T2. The longest reach in the game, and slow enough that it had better land.</summary>
        Scythe = 12,

        // Three suits of armour and no more, one for each rarity, because the armour slot
        // has to be readable at a glance from across the arena: knowing the other fighter
        // is in plate decides whether you take the exchange at all, and a wardrobe of
        // near-identical sets says nothing. Common, rare, legendary.
        /// <summary>T1 armour — COMMON.</summary>
        LeatherVest = 20,
        /// <summary>T2 armour — RARE. Visible on the character, which is information for the opponent.</summary>
        Chainmail = 21,
        /// <summary>T3 armour — LEGENDARY.</summary>
        Plate = 22,

        // 23 was the shield, removed. The number is left out rather than reused: an id is
        // what a replay and a rollback packet carry, and handing an old one to a different
        // item turns a saved round into a different round.

        /// <summary>T1 utility.</summary>
        Bandage = 30,

        // The head. A helmet is not a second vest: what it buys is the hitstun of the blows
        // after the first, which is the difference between eating a string and answering
        // one. docs/02 asks that every piece be a different PLAN, and this is the plan of
        // the player who keeps getting rushed.
        /// <summary>T1 helmet — COMMON.</summary>
        LeatherCap = 40,
        /// <summary>T2 helmet — RARE.</summary>
        MailCoif = 41,
        /// <summary>T3 helmet — LEGENDARY.</summary>
        GreatHelm = 42,

        // The waist. The belt is where the guard is strapped and the pouches hang: it buys
        // seconds of holding a block, which is the one defence a rushed fighter has and the
        // one the stamina pool keeps taking away from him.
        /// <summary>T1 belt — COMMON.</summary>
        RopeBelt = 50,
        /// <summary>T2 belt — RARE.</summary>
        ToolBelt = 51,
        /// <summary>T3 belt — LEGENDARY.</summary>
        WarBelt = 52
    }

    /// <summary>
    /// How good a find is, in the one word a player reads instead of a stat line.
    ///
    /// Not a fourth number: it IS the tier, named. The loot table already rolls a tier per
    /// chest and weights within it, and the fight is already balanced against those three
    /// steps, so inventing a second scale would be two sources of truth for one idea. This
    /// is the name presentation colours by, and the only place the words live.
    /// </summary>
    public enum Rarity : byte
    {
        Common = 0,
        Rare = 1,
        Legendary = 2
    }

    /// <summary>
    /// Which slot an item goes in. One kind per slot and no item in two of them, because
    /// <see cref="Inventory.Equip"/> picks the slot from the kind and nothing else.
    ///
    /// Helmet and Belt are armour in every way that matters to the fight — they are summed
    /// into the same damage reduction — and they are separate KINDS because a kind is a
    /// slot. Keeping them inside Armour would mean a coif and a breastplate competing for
    /// the same square, which is the opposite of what a head and a waist are.
    /// </summary>
    public enum ItemKind : byte
    {
        None = 0,
        Weapon = 1,
        /// <summary>The chest. The big share of the damage reduction.</summary>
        Armour = 2,
        Utility = 3,
        /// <summary>The head. A little reduction, and the hitstun of a string.</summary>
        Helmet = 4,
        /// <summary>The waist. A little reduction, and how long a guard holds.</summary>
        Belt = 5
    }
}
