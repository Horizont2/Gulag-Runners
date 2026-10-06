namespace GulagRunners.Sim
{
    /// <summary>
    /// Every number behind the fight, from docs/02.
    ///
    /// The target the rest is tuned against is a time-to-kill of 8 to 14 seconds of ACTIVE
    /// fighting. That is not the length of a match: most of a round is spent not in contact, and
    /// a fight that ends in two exchanges makes the whole scavenge phase pointless.
    /// </summary>
    public struct CombatConfig
    {
        public int MaxHealth;

        /// <summary>Frames the hitbox is live. Short: a long active window hits round corners.</summary>
        public int ActiveFrames;

        /// <summary>Recovery as a fraction of the weapon's own wind-up, in thousandths.</summary>
        public int RecoveryPermille;

        /// <summary>
        /// Frames after a swing during which another press continues the combo. docs/02 caps a
        /// combo at three — light, light, heavy — because longer strings on a phone are not depth,
        /// they are a dexterity tax.
        /// </summary>
        public int ComboWindowFrames;
        public int MaxCombo;

        /// <summary>The third hit of a combo, in thousandths of the weapon's damage.</summary>
        public int HeavyDamagePermille;
        /// <summary>And it takes this much longer to wind up and recover from.</summary>
        public int HeavyTimingPermille;

        /// <summary>Hitstun per point of damage, in frames, and its ceiling.</summary>
        public int HitstunPerDamage;
        public int MaxHitstunFrames;

        public Fix KnockbackSpeed;
        public Fix HeavyKnockbackSpeed;
        /// <summary>Upward part of a knockback. A little, so a hit reads; not a launch.</summary>
        public Fix KnockbackLift;

        // ---- blocking (docs/02: the medieval mode's answer to pressure)
        /// <summary>Damage that still gets through a block, in thousandths.</summary>
        public int ChipPermille;

        /// <summary>
        /// Stamina spent for every hit absorbed. It has to outrun the stamina regen or holding
        /// the guard is free, which is the one thing a guard must never be.
        /// </summary>
        public int BlockStaminaCost;

        /// <summary>Frames the guard is down for after it is broken.</summary>
        public int GuardBreakFrames;
        /// <summary>Movement speed while holding a block, in thousandths of the run speed.</summary>
        public int BlockSpeedPermille;

        /// <summary>
        /// Frames after raising the guard during which a hit is PARRIED instead of blocked: no
        /// damage at all, and the attacker is staggered. The one window in the game that rewards
        /// reading the other player rather than reacting to them.
        /// </summary>
        public int ParryFrames;
        public int StaggerFrames;

        public int DeathFrames;

        public static CombatConfig Default()
        {
            CombatConfig c;
            c.MaxHealth = 100;

            c.ActiveFrames = 3;
            c.RecoveryPermille = 700;      // recovery is 0.7 of the wind-up

            c.ComboWindowFrames = 20;      // 0.33 s
            c.MaxCombo = 3;
            c.HeavyDamagePermille = 1700;
            c.HeavyTimingPermille = 1500;

            c.HitstunPerDamage = 1;
            c.MaxHitstunFrames = 26;

            c.KnockbackSpeed = Fix.FromMilli(3200);
            c.HeavyKnockbackSpeed = Fix.FromMilli(6000);
            c.KnockbackLift = Fix.FromMilli(1600);

            c.ChipPermille = 150;
            c.BlockStaminaCost = 2;        // against a 0.55 s regen, 1 a hit is break-even
            c.GuardBreakFrames = 42;       // 0.7 s wide open
            c.BlockSpeedPermille = 420;

            c.ParryFrames = 7;             // 0.12 s
            c.StaggerFrames = 36;          // 0.6 s

            c.DeathFrames = 90;
            return c;
        }
    }
}
