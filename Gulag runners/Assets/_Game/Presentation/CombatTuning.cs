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
        [Header("Hit stop — the weight of a blow")]
        [Tooltip("Seconds both fighters freeze for when a blow lands.\n\n" +
                 "The one thing in a fight that nothing else can stand in for: a swing that " +
                 "only changes a number has nothing in it for the hands. Felt rather than " +
                 "seen, so it is small — past about a tenth of a second on an ordinary hit it " +
                 "stops reading as weight and starts reading as a dropped frame.")]
        [Range(0f, 0.25f)] public float hitStop = 0.05f;

        [Tooltip("Seconds both freeze for when the guard eats it. Shorter than a clean hit: " +
                 "a block is supposed to feel like less than being hit.")]
        [Range(0f, 0.25f)] public float blockStop = 0.033f;

        [Tooltip("Seconds both freeze for on a parry. The longest by a wide margin — docs/02 " +
                 "makes the parry the skill ceiling, and the reward for finding one is that " +
                 "the world stops while everybody sees it.")]
        [Range(0f, 0.4f)] public float parryStop = 0.117f;

        [Tooltip("Frames of hitstun per point of damage, and the ceiling.")]
        public float hitstunPerDamage = 1f;
        public float maxHitstun = 0.57f;

        [Tooltip("How long an attack press is still worth something when it cannot be used " +
                 "yet.\n\nWithout it a tap during recovery is thrown away and continuing a " +
                 "string means pressing again on the exact frame the swing ends. 0.30 s " +
                 "clears the longest recovery a landed swing leaves behind.")]
        [Range(0f, 0.4f)] public float attackBuffer = 0.3f;

        [Tooltip("What a swing that CONNECTS gets back of its own recovery.\n\n" +
                 "Every weapon used to be at a frame disadvantage after landing a blow — " +
                 "from -3 with a saber to -12 with a scythe — so the person you had just " +
                 "hit moved first and the correct play was never to swing. A miss still " +
                 "pays the full price; that is what a miss is for.")]
        [Range(0.1f, 1f)] public float hitRecoveryKept = 0.5f;

        [Tooltip("Frames of initiative a clean hit is guaranteed to buy, whatever the " +
                 "weapon. Hitstun is raised to at least what the attacker still owes plus " +
                 "this, so connecting is never punished. Damage can earn more.")]
        [Range(0f, 0.25f)] public float hitAdvantage = 0.067f;

        [Tooltip("What each FURTHER hit of a string is worth against the one before.\n\n" +
                 "Guaranteeing the turn on every hit is an infinite: measured, a bare-handed " +
                 "fighter landed twenty blows in eight seconds and the man with a club " +
                 "landed none at all.")]
        [Range(0.2f, 1f)] public float comboStunDecay = 0.62f;

        [Tooltip("Seconds without being hit that end a string.")]
        [Range(0.2f, 3f)] public float comboStackTime = 1.25f;

        [Tooltip("Extra knockback per hit already taken in this string. The second way a " +
                 "string ends: the attacker runs out of reach and has to step back in.")]
        [Range(0f, 2f)] public float comboPush = 0.3f;

        [Tooltip("What the ATTACKER is thrown back by, against the victim's knockback.\n\n" +
                 "Against a wall the victim has nowhere to go, so without this a string " +
                 "never ends there -- measured in the corner, fists looped a spearman " +
                 "twenty hits to none where open floor read one to two the other way.")]
        [Range(0f, 1f)] public float attackerRecoil = 0.4f;

        [Range(0f, 0.5f)] public float recoilTime = 0.167f;

        [Tooltip("Every weapon's wind-up and recovery, against the table's own numbers.\n\n" +
                 "One knob over the whole fight, because the question it answers is whether " +
                 "a player can SEE a blow coming: a dagger wound up in 0.15 s and fists in " +
                 "0.20, against a human reaction of about 0.25. Scaling here keeps the " +
                 "weapons' relationship to each other, which is the design.")]
        [Range(0.5f, 2f)] public float attackPace = 1.45f;

        [Header("Moving through a swing")]
        [Tooltip("Speed through the three phases of a swing, against the run.\n\n" +
                 "These were not being passed to the simulation at all: Build() left them " +
                 "out, so every scene ran them at zero, and zero is the motor's \"no cap\". " +
                 "A fighter was travelling at FULL running speed through his own wind-up, " +
                 "active frames and recovery, which is most of why a swing had no weight to " +
                 "it. You gather while still moving, you plant for the blow, and you are " +
                 "heavy on your feet afterwards.")]
        [Range(0f, 1f)] public float windupSpeed = 0.48f;
        [Range(0f, 1f)] public float activeSpeed = 0.18f;
        [Range(0f, 1f)] public float recoverySpeed = 0.4f;

        public float knockbackSpeed = 3.2f;
        public float heavyKnockbackSpeed = 6f;

        [Tooltip("Upward part of a knockback, and ONLY on the heavy finisher.\n\n" +
                 "On every hit it lifted them three centimetres — nothing you can see — " +
                 "while taking them off the ground for 21 frames, which slid them out of " +
                 "reach before any follow-up could land.")]
        public float knockbackLift = 1.6f;

        [Header("Guard")]
        [Tooltip("Damage that still gets through a block.")]
        [Range(0f, 0.5f)] public float chip = 0.3f;

        [Tooltip("Stamina spent for every LIGHT hit absorbed. Zero, and deliberately.\n\n" +
                 "At one charge a blow it was four blows and out: measured against bare hands " +
                 "mashing, a swordsman's guard broke after 1.2 SECONDS and left him open for " +
                 "another 0.7, so the only answer the game offers to being rushed was also the " +
                 "fastest way to lose. Holding the guard still runs a fighter down by the " +
                 "second; a flurry of jabs no longer does.")]
        public int blockStaminaCost = 0;

        [Tooltip("What the heavy finisher costs the man behind the guard. This is what a guard " +
                 "is broken BY: one committed blow, read and answered, not four jabs.")]
        public int heavyBlockStaminaCost = 2;

        [Tooltip("What the attacker is thrown back by when the guard eats it, against the " +
                 "victim's knockback. Larger than off a body: meeting a braced shield is the " +
                 "one thing that should put the man swinging back on his heels, and it is what " +
                 "stops a rush standing in your face.")]
        [Range(0f, 2f)] public float blockRecoil = 0.9f;


        public float guardBreakTime = 0.7f;
        [Tooltip("Walking speed with the guard up, as a share of the run speed.")]
        [Range(0.1f, 1f)] public float blockSpeed = 0.42f;

        [Header("Parry")]
        [Tooltip("Seconds after raising the guard in which a hit is parried instead of blocked: " +
                 "no damage at all, and the attacker is staggered. The one window in the game " +
                 "that rewards reading the other player rather than reacting to them.")]
        public float parryWindow = 0.2f;

        [Tooltip("Seconds of holding the guard that cost one stamina charge. docs/02 spends " +
                 "stamina on holding the block. Without it a raised guard is free, and since " +
                 "a braced fighter walks at Block Speed, the only thing the player notices is " +
                 "that they have mysteriously become slow. 0 turns it off.")]
        public float guardDrainTime = 0.8f;
        public float staggerTime = 0.8f;

        [Header("Death")]
        public float deathTime = 1.5f;

        [Header("Desperation")]
        [Tooltip("Below this share of full health, BARE HANDS hit for the multiplier below. " +
                 "docs/02 asks for it by name: it is what makes a fighter who lost their " +
                 "weapon worth fearing, and it only ever helps whoever is losing.")]
        [Range(0f, 1f)] public float desperationHealth = 0.3f;
        public float desperationDamage = 1.4f;

        [Header("Falling")]
        [Tooltip("Landing faster than this hurts. 12.5 m/s is the speed reached dropping " +
                 "about 2.8 m, so every drop inside a storey is free and the full height of " +
                 "the arena is not. docs/02 counts a fall off a floor among the ways a fight " +
                 "ends; this is what makes knocking somebody off one worth the opening.")]
        public float fallDamageSpeed = 12.5f;

        [Tooltip("Damage for every m/s over that.")]
        public int fallDamagePerSpeed = 8;

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
            HitStopFrames = F(hitStop),
            BlockStopFrames = F(blockStop),
            ParryStopFrames = F(parryStop),
            HitstunPerDamage = Mathf.Max(0, Mathf.RoundToInt(hitstunPerDamage)),
            MaxHitstunFrames = F(maxHitstun),
            AttackBufferFrames = F(attackBuffer),
            HitRecoveryCutPermille = P(hitRecoveryKept),
            HitAdvantageFrames = F(hitAdvantage),
            ComboStunDecayPermille = P(comboStunDecay),
            ComboStackFrames = F(comboStackTime),
            ComboPushPermille = P(comboPush),
            AttackerRecoilPermille = P(attackerRecoil),
            RecoilFrames = F(recoilTime),
            AttackFramesPermille = P(attackPace),
            WindupSpeedPermille = P(windupSpeed),
            ActiveSpeedPermille = P(activeSpeed),
            RecoverySpeedPermille = P(recoverySpeed),
            KnockbackSpeed = M(knockbackSpeed),
            HeavyKnockbackSpeed = M(heavyKnockbackSpeed),
            KnockbackLift = M(knockbackLift),
            ChipPermille = P(chip),
            BlockStaminaCost = Mathf.Max(0, blockStaminaCost),
            HeavyBlockStaminaCost = Mathf.Max(0, heavyBlockStaminaCost),
            BlockRecoilPermille = P(blockRecoil),
            GuardBreakFrames = F(guardBreakTime),
            BlockSpeedPermille = P(blockSpeed),
            ParryFrames = F(parryWindow),
            GuardDrainFrames = F(guardDrainTime),
            StaggerFrames = F(staggerTime),
            DeathFrames = F(deathTime),
            DesperationHealthPermille = P(desperationHealth),
            DesperationDamagePermille = P(desperationDamage),
            FallDamageSpeed = M(fallDamageSpeed),
            FallDamagePerSpeed = Mathf.Max(0, fallDamagePerSpeed)
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
