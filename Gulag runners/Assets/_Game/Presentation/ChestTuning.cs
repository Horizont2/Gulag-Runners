using System;
using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    [Serializable]
    public class ChestKindTuning
    {
        [Tooltip("Seconds to open it with bare hands. A weapon divides this by its pry speed, " +
                 "so the figures in docs/03 are the ones with something to lever it with.")]
        public float bareSeconds = 1.2f;

        [Tooltip("How loud it is to open bare-handed, and with a weapon in hand.")]
        public NoiseLevel bareNoise = NoiseLevel.Quiet;
        public NoiseLevel toolNoise = NoiseLevel.Quiet;

        [Tooltip("Durability spent when a weapon is used on it. This is the price of the " +
                 "scavenge phase: the weapon is also the crowbar.")]
        public int toolWear = 1;

        public ChestKindConfig ToConfig() => new ChestKindConfig
        {
            BareFrames = Mathf.Max(1, Mathf.RoundToInt(bareSeconds * PlayerMotor.TicksPerSecond)),
            BareNoise = bareNoise,
            ToolNoise = toolNoise,
            ToolWear = Mathf.Max(0, toolWear)
        };
    }

    /// <summary>
    /// Inspector-friendly mirror of <see cref="ChestConfig"/>, in seconds rather than frames.
    /// The same boundary rule as <see cref="MovementTuning"/>: floats stop here.
    /// </summary>
    [Serializable]
    public class ChestTuning
    {
        // One opening cost for all three kinds.
        //
        // docs/03 priced them apart — 1.2 s quiet for a crate, 6 s loud for a safe — so that
        // the chest kind asked "how much will you tell the other player about yourself for
        // this". That question only works once the kinds are placed deliberately, with the
        // rich ones somewhere exposed; scattered evenly, as they are now, a six-second safe is
        // not a bet, it is a toll. So the kind decides the LOOT TIER and nothing else, and the
        // trade comes back with the placement. Changing one of these three rows is how.
        [Tooltip("Tier odds 80/20/0 across T1/T2/T3. Same cost to open as every other kind.")]
        public ChestKindTuning crate = new ChestKindTuning
        {
            bareSeconds = 2.5f,
            bareNoise = NoiseLevel.Medium,
            toolNoise = NoiseLevel.Loud,
            toolWear = 2
        };

        [Tooltip("Tier odds 35/55/10. Same cost to open as every other kind.")]
        public ChestKindTuning locker = new ChestKindTuning
        {
            bareSeconds = 2.5f,
            bareNoise = NoiseLevel.Medium,
            toolNoise = NoiseLevel.Loud,
            toolWear = 2
        };

        [Tooltip("Tier odds 5/45/50 — half the time gold. Same cost to open as every other kind.")]
        public ChestKindTuning safe = new ChestKindTuning
        {
            bareSeconds = 2.5f,
            bareNoise = NoiseLevel.Medium,
            toolNoise = NoiseLevel.Loud,
            toolWear = 2
        };

        [Tooltip("How many times faster progress drains after letting go. Draining rather than " +
                 "resetting on purpose: losing six seconds of work to one slipped thumb is not " +
                 "tension, it is a bug report.")]
        [Range(1, 10)] public int decayMultiplier = 2;

        [Tooltip("How often opening re-announces itself, in seconds. In a fog-of-war match noise " +
                 "is the currency, so this is a balance number, not an audio setting (docs/01).")]
        public float noiseInterval = 0.33f;

        public ChestConfig ToConfig() => new ChestConfig
        {
            Crate = crate.ToConfig(),
            Locker = locker.ToConfig(),
            Safe = safe.ToConfig(),
            DecayMultiplier = Mathf.Max(1, decayMultiplier),
            NoiseFrames = Mathf.Max(1, Mathf.RoundToInt(noiseInterval * PlayerMotor.TicksPerSecond))
        };
    }

    /// <summary>
    /// Inspector mirror of <see cref="DropConfig"/>: how loot leaves a chest or a hand, and how
    /// it behaves on the floor. Metres and seconds here, fixed point past this line.
    /// </summary>
    [Serializable]
    public class DropTuning
    {
        [Header("Out of a chest")]
        [Tooltip("How far sideways and how high the loot is thrown when the lid comes off. " +
                 "It should clear the chest and land where the player can see it, not inside it.")]
        public float popSpeedX = 1.4f;
        public float popSpeedY = 3.6f;

        [Header("Out of a hand")]
        [Tooltip("A displaced item hops away from the way you are facing: a short hop, not a " +
                 "throw, so a swap never loses the old item behind you.")]
        public float dropSpeedX = 1.1f;
        public float dropSpeedY = 2.2f;

        [Header("In the air")]
        public float gravity = 22f;
        public float maxFallSpeed = 14f;
        [Tooltip("How much of its speed a bounce keeps. Low on purpose: loot that rolls ends up " +
                 "somewhere neither player could have predicted.")]
        [Range(0f, 0.6f)] public float bounce = 0.15f;

        [Header("On the floor")]
        [Tooltip("Half the size of the box you walk through to pick it up.")]
        public float pickupRadius = 0.22f;

        [Tooltip("Seconds before loot that just came out of a chest can be taken. It has to land " +
                 "first, or opening a chest would be the same as being handed the item.")]
        public float popLock = 0.23f;

        [Tooltip("Seconds before an item you just dropped can be taken back. Longer on purpose: " +
                 "a swap has to be a commitment for a moment, or it is a free look at both.")]
        public float dropLock = 0.5f;

        static Fix M(float metres) => Fix.FromMilli(Mathf.RoundToInt(metres * 1000f));
        static int F(float seconds) => Mathf.Max(0, Mathf.RoundToInt(seconds * PlayerMotor.TicksPerSecond));

        public DropConfig ToConfig() => new DropConfig
        {
            PopSpeedX = M(popSpeedX),
            PopSpeedY = M(popSpeedY),
            DropSpeedX = M(dropSpeedX),
            DropSpeedY = M(dropSpeedY),
            Gravity = M(gravity),
            MaxFallSpeed = M(maxFallSpeed),
            Bounce = M(bounce),
            PickupRadius = M(Mathf.Max(0.05f, pickupRadius)),
            PopLockFrames = F(popLock),
            DropLockFrames = F(dropLock)
        };
    }
}
