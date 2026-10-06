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

        /// <summary>T3. Slow, enormous, and it goes through a guard. The answer to turtling.</summary>
        Flail = 7,

        /// <summary>T3. If it lands, the fight is decided.</summary>
        Greataxe = 8,

        /// <summary>T1 armour.</summary>
        LeatherVest = 20,
        /// <summary>T2 armour. Visible on the character, which is information for the opponent.</summary>
        Chainmail = 21,
        /// <summary>T3 armour.</summary>
        Plate = 22,

        /// <summary>T1 utility.</summary>
        Bandage = 30
    }

    public enum ItemKind : byte
    {
        None = 0,
        Weapon = 1,
        Armour = 2,
        Utility = 3
    }
}
