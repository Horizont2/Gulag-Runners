using System;
using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// Inspector mirror of <see cref="CombatConfig"/>, in seconds and percentages.
    ///
    /// The number everything else is tuned against is at the top: docs/02 wants a time to kill of
    /// 8 to 14 seconds of ACTIVE fighting. Most of a round is not spent in contact, and a fight
    /// that ends in two exchanges makes the whole scavenge phase pointless.
    /// </summary>
    [Serializable]
    public class CombatTuning
    {
        [Header("Health")]
        public int maxHealth = 100;

        [Header("Swing")]
        [Tooltip("Seconds the hitbox is live. Short on purpose: a long active window hits round " +
                 "corners and makes reach meaningless.")]
        public float activeTime = 0.05f;

        [Tooltip("Recovery as a share of the weapon's own wind-up. This is what a miss costs, " +
                 "and it is why a spear is not simply better than a club.")]
        [Range(0.2f, 2f)] public float recoveryShare = 0.7f;

        [Header("Combo (docs/02: three at most)")]
        [Tooltip("Seconds to continue the string with another press.")]
        public float comboWindow = 0.33f;
        [Range(1, 5)] public int maxCombo = 3;
        [Tooltip("The last hit of a string is the heavy one.")]
        public float heavyDamageMultiplier = 1.7f;
        public float heavyTimingMultiplier = 1.5f;

        [Header("Being hit")]
        [Tooltip("Frames of hitstun per point of damage, and the ceiling.")]
        public float hitstunPerDamage = 1f;
        public float maxHitstun = 0.43f;
        public float knockbackSpeed = 3.2f;
        public float heavyKnockbackSpeed = 6f;
        [Tooltip("Upward part of a knockback. Enough that a hit reads; not a launch.")]
        public float knockbackLift = 1.6f;

        [Header("Guard")]
        [Tooltip("Damage that still gets through a block.")]
        [Range(0f, 0.5f)] public float chip = 0.15f;

        [Tooltip("Stamina spent per hit absorbed. It has to outrun the stamina regen, or holding " +
                 "the guard is free — which is the one thing a guard must never be.")]
        public int blockStaminaCost = 2;

        public float guardBreakTime = 0.7f;
        [Tooltip("Walking speed with the guard up, as a share of the run speed.")]
        [Range(0.1f, 1f)] public float blockSpeed = 0.42f;

        [Header("Parry")]
        [Tooltip("Seconds after raising the guard in which a hit is parried instead of blocked: " +
                 "no damage at all, and the attacker is staggered. The one window in the game " +
                 "that rewards reading the other player rather than reacting to them.")]
        public float parryWindow = 0.12f;
        public float staggerTime = 0.6f;

        [Header("Death")]
        public float deathTime = 1.5f;

        static int F(float seconds) =>
            Mathf.Max(1, Mathf.RoundToInt(seconds * PlayerMotor.TicksPerSecond));
        static int P(float share) => Mathf.RoundToInt(share * 1000f);
        static Fix M(float metres) => Fix.FromMilli(Mathf.RoundToInt(metres * 1000f));

        public CombatConfig ToConfig() => new CombatConfig
        {
            MaxHealth = Mathf.Max(1, maxHealth),
            ActiveFrames = F(activeTime),
            RecoveryPermille = P(recoveryShare),
            ComboWindowFrames = F(comboWindow),
            MaxCombo = Mathf.Max(1, maxCombo),
            HeavyDamagePermille = P(heavyDamageMultiplier),
            HeavyTimingPermille = P(heavyTimingMultiplier),
            HitstunPerDamage = Mathf.Max(0, Mathf.RoundToInt(hitstunPerDamage)),
            MaxHitstunFrames = F(maxHitstun),
            KnockbackSpeed = M(knockbackSpeed),
            HeavyKnockbackSpeed = M(heavyKnockbackSpeed),
            KnockbackLift = M(knockbackLift),
            ChipPermille = P(chip),
            BlockStaminaCost = Mathf.Max(0, blockStaminaCost),
            GuardBreakFrames = F(guardBreakTime),
            BlockSpeedPermille = P(blockSpeed),
            ParryFrames = F(parryWindow),
            StaggerFrames = F(staggerTime),
            DeathFrames = F(deathTime)
        };

        /// <summary>
        /// Rough time to kill against a given weapon, for the inspector. Not a simulation: it
        /// assumes every swing lands and nothing is blocked, so it is the FLOOR of a real fight —
        /// a contested one runs roughly twice this.
        /// </summary>
        public float TimeToKill(ItemId weapon, ItemId armour)
        {
            ItemDef w = ItemTable.WeaponOrFists(weapon);
            float damage = w.Damage.ToMilli() / 1000f;
            float reduction = ItemTable.Get(armour).DamageReduction.ToMilli() / 1000f;
            damage *= 1f - reduction;
            if (damage <= 0.01f) return 0f;

            // A string of three: two light, one heavy, each costing wind-up plus recovery.
            float light = w.AttackFrames * (1f + recoveryShare) + activeTime * PlayerMotor.TicksPerSecond;
            float heavy = light * heavyTimingMultiplier;
            float cycleFrames = light * (maxCombo - 1) + heavy;
            float cycleDamage = damage * (maxCombo - 1) + damage * heavyDamageMultiplier;
            if (cycleDamage <= 0.01f) return 0f;

            return maxHealth / cycleDamage * (cycleFrames / PlayerMotor.TicksPerSecond);
        }
    }
}
