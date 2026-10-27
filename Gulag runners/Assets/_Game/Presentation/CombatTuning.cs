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

        // ----------------------------------------------------------------- the arm
        [Header("The arm (docs/31)")]
        [Tooltip("Shoulder height, as a share of the body. The arm hangs from here.")]
        [Range(0.5f, 0.95f)] public float shoulderHeight = 0.78f;

        [Tooltip("Arm length as a SHARE OF BODY HEIGHT. The weapon's reach is added " +
                 "past the hand.\n\n" +
                 "A share and not metres: in metres it was written against the 1.8 m " +
                 "fighter in the defaults, and the fighter in the prefabs is 0.91 m, so " +
                 "the arm came out half his own height. The IK goal then sat outside " +
                 "anything the shoulder could reach and Unity dragged the spine out to " +
                 "meet it -- which is a body bending into knots at some angles.")]
        [Range(0.15f, 0.6f)] public float armLength = 0.35f;

        [Tooltip("The grip and head every weapon has before it adds any reach at all, " +
                 "as a share of body height. A blade is this plus the share below of " +
                 "whatever reach the weapon adds over bare hands.\n\n" +
                 "These four numbers decide the BLADE THE SIMULATION SWINGS, and they " +
                 "are the ones PlayerLoadoutView has always drawn a weapon with — which " +
                 "is the point: the hitbox and the model are now one object.\n\n" +
                 "Taking the blade as the item's whole Reach instead, which is what the " +
                 "simulation used to do, is the single worst bug this fight has had. " +
                 "Reach is measured from the MIDDLE OF THE BODY, so it already contains " +
                 "half a torso and an arm; run out from the hand it made every blade " +
                 "about 1.5x the weapon on screen, and gave bare fists 0.60 m of " +
                 "invisible steel. Blades longer than the gap between two fighters are " +
                 "crossed nearly all the time, every crossing is a clash, and a clash " +
                 "locks both arms: two people swinging at each other stood there for " +
                 "forty seconds without landing a blow.")]
        [Range(0f, 0.8f)] public float haft = 0.27f;

        [Tooltip("How much of the reach a weapon adds over bare hands is drawn and " +
                 "swung as blade.")]
        [Range(0f, 3f)] public float reachToBlade = 0.73f;

        [Tooltip("Shortest a blade may be, as a share of body height. A dagger adds " +
                 "almost nothing to bare reach, and a dagger you cannot see is not a " +
                 "weapon.")]
        [Range(0f, 1f)] public float minBlade = 0.31f;

        [Tooltip("Longest a blade may be, as a share of body height. The map from reach " +
                 "to length is linear, which over-rewards the two polearms: uncapped, a " +
                 "scythe comes out nearly twice the fighter's height.")]
        [Range(0.2f, 3f)] public float maxBlade = 1.43f;

        [Tooltip("Seconds for the arm to sweep half a circle, carrying a weapon whose " +
                 "AttackFrames equals Reference Frames below.\n\n" +
                 "This is where a weapon's AttackFrames went. It is no longer a timer " +
                 "you wait out; it is how fast that weapon can be MOVED, which is the " +
                 "same statement about the weapon made in a way the player can see.")]
        [Range(0.08f, 0.8f)] public float sweepTime = 0.25f;

        [Tooltip("The weapon that sweeps in exactly Sweep Time. The club, at 15 frames, " +
                 "is the middle of the table and what the feel was tuned on.")]
        public int referenceFrames = 15;

        [Tooltip("How far the attack stick must be pushed before the arm leaves its " +
                 "carry pose.")]
        [Range(0.05f, 0.6f)] public float aimDeadzone = 0.22f;

        [Tooltip("How low the weapon is carried when the stick is centred.\n\n" +
                 "Not cosmetic. Carried level, every fighter holds a free permanent " +
                 "guard without touching the stick, and two of them standing still " +
                 "clash for ever. Carried low, a guard is something you ASK for by " +
                 "pushing the stick at them -- and pay for with the swing you are not " +
                 "making.")]
        [Range(0f, 2f)] public float carryDrop = 0.70f;

        [Tooltip("How far the attack stick is read relative to the OTHER FIGHTER " +
                 "instead of to the world.\n\n" +
                 "At 1 pushing forward always means at him, up means over him, down " +
                 "means under him, wherever he is standing -- so a blow never has to be " +
                 "traced onto a moving man with a thumb. At 0 the stick is world " +
                 "directions, which is exact and was the thing that felt like work.\n\n" +
                 "It steers nothing: the direction asked for is kept, only measured " +
                 "from a different zero. That is why it is a frame and not an aim " +
                 "assist -- pulling a blade toward him pulls it off the line the other " +
                 "man is guarding, and a measured version of that took a guard's whole " +
                 "value away.")]
        [Range(0f, 1f)] public float aimRelative;

        [Tooltip("How close he may get, horizontally, before the aim frame stops " +
                 "measuring exactly where he is — as a share of body height.\n\n" +
                 "Two fighters standing on one spot have no direction between them, and " +
                 "just short of that the direction is the difference between two nearly " +
                 "equal numbers, which is near enough to anything. The floor stops the " +
                 "frame spinning under a held thumb as you close.")]
        [Range(0.1f, 2f)] public float aimFloor = 0.5f;

        [Tooltip("The most the aim frame may lean away from level, as a SLOPE. 1 is 45 " +
                 "degrees.\n\n" +
                 "The frame says which way \"at him\" is in a side-view fight, and past " +
                 "about 45 degrees up and down stop meaning anything a thumb can use. " +
                 "Uncapped, a fighter on a ledge overhead rotates the whole stick a " +
                 "quarter turn.")]
        [Range(0f, 4f)] public float aimTilt = 1f;

        [Tooltip("Tip speed below this share of the weapon's OWN maximum does nothing.\n\n" +
                 "The most important number in the fight. Too low and holding the blade " +
                 "out while walking is a strategy; too high and only a windmill counts. " +
                 "A 3.6 m/s run moves a dagger tip at 15% of its top speed, so a walk-in " +
                 "lands well under this and pays nothing.")]
        [Range(0.05f, 0.6f)] public float minSwing = 0.25f;

        [Tooltip("How far a swing must travel to be worth a FULL blow, as a share of a " +
                 "half circle. Less than that and it is worth proportionally less.\n\n" +
                 "The number that decides whether there is a game here. Damage used to " +
                 "be the tip's instantaneous speed, and that is maximised by shaking " +
                 "the thumb over a sliver: a vibration never spends a tick accelerating " +
                 "or dwelling at the end of an arc, so it sits at top speed for ever. " +
                 "Measured, shaking beat swinging on EVERY weapon — club 20 against 16, " +
                 "sword 25 against 16, warhammer 26 against 19. A player finds that in " +
                 "ten seconds and then there is nothing left to play.\n\n" +
                 "It also buys a windup for nothing: the first part of every swing is " +
                 "harmless, and it lasts longer on a heavy weapon because a heavy " +
                 "weapon turns slower — which is the arc everyone is meant to see.")]
        [Range(0.1f, 1f)] public float fullSweep = 0.4f;

        [Tooltip("How much of its speed the arm keeps while the last blow is still " +
                 "spent.\n\n" +
                 "The hit lock has to be SEEN. A blade that sweeps through somebody and " +
                 "does nothing looks exactly like one that hits, so an invisible lock " +
                 "reads as the game ignoring you. An arm that visibly labours reads as " +
                 "weight, and carries the same information.")]
        [Range(0.1f, 1f)] public float recovery = 0.45f;

        [Tooltip("Body radius for the hit capsule, as a share of body height.")]
        [Range(0.05f, 0.4f)] public float bodyRadius = 0.18f;

        [Tooltip("Half-width of a blade for weapon-on-weapon contact, likewise a share.")]
        [Range(0.01f, 0.2f)] public float bladeRadius = 0.06f;

        [Tooltip("Seconds before the same weapon may hurt the same fighter again. " +
                 "One arc, one hit.")]
        [Range(0.05f, 0.8f)] public float hitLock = 0.30f;

        [Tooltip("How hard a clash throws the slower arm aside.")]
        [Range(0f, 2f)] public float clashDeflect = 0.70f;

        [Tooltip("Seconds a fighter whose weapon was beaten aside cannot land a blow.")]
        [Range(0f, 0.6f)] public float clashLock = 0.17f;

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

        [Header("Worn kit")]
        [Tooltip("The most helmet, chest armour and belt together may remove from a blow. " +
                 "A full set of one rarity comes to docs/02's own 15/30/45%, so the table " +
                 "tops out at 45% and this never fires — it is the guard rail for the day " +
                 "a fourth worn piece is added.")]
        [Range(0f, 1f)] public float maxArmour = 0.5f;

        [Tooltip("The most the worn kit may take off the hitstun. Only the helmet carries " +
                 "any today — a great helm is 38% of it.")]
        [Range(0f, 1f)] public float maxStunResist = 0.4f;

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

        /// <summary>The arm, as the simulation wants it.</summary>
        public ArmConfig ToArmConfig() => new ArmConfig
        {
            ShoulderPermille = P(shoulderHeight),
            ArmLengthPermille = P(armLength),
            HaftPermille = P(haft),
            ReachToBladePermille = P(reachToBlade),
            MinBladePermille = P(minBlade),
            MaxBladePermille = P(maxBlade),
            SweepFrames = Mathf.Max(1, F(sweepTime)),
            ReferenceFrames = Mathf.Max(1, referenceFrames),
            DeadzonePermille = P(aimDeadzone),
            CarryDropPermille = P(carryDrop),
            AimRelativePermille = P(aimRelative),
            AimFloorPermille = P(aimFloor),
            AimTiltPermille = P(aimTilt),
            MinSwingPermille = P(minSwing),
            FullSweepPermille = P(fullSweep),
            RecoveryPermille = P(recovery),
            BodyRadiusPermille = P(bodyRadius),
            BladeRadiusPermille = P(bladeRadius),
            HitLockFrames = Mathf.Max(1, F(hitLock)),
            ClashDeflectPermille = P(clashDeflect),
            ClashLockFrames = Mathf.Max(0, F(clashLock)),
        };

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
            MaxArmourPermille = P(maxArmour),
            MaxStunResistPermille = P(maxStunResist),
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
            float reduction = ItemTable.Get(armour).DamageReductionPermille / 1000f;
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
