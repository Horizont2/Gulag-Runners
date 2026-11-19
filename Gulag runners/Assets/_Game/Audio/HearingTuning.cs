using System;
using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// How far each level of noise carries, and what a floor does to it.
    ///
    /// This is balance, not an audio setting. docs/01 makes sound the only way to find the other
    /// player during the scavenge phase, and docs/03 prices every chest in it — so these numbers
    /// decide how much of the arena a loud chest gives away, and they are the first dial to turn
    /// if the blind phase comes back from a playtest as "empty" rather than "tense".
    ///
    /// They are presentation, not simulation: hearing something changes what the player knows,
    /// never what the match does, so it can never desync.
    /// </summary>
    [Serializable]
    public class HearingTuning
    {
        [Header("How far it carries, in metres")]
        [Tooltip("Crouch-walking is Silent and carries nowhere. These are the other three.")]
        public float quietRange = 6f;
        public float mediumRange = 12f;
        public float loudRange = 20f;

        [Tooltip("Within this fraction of the range the sound is at full volume; past it it " +
                 "falls away. A sound that fades from the very first metre reads as far away " +
                 "even when it is next door.")]
        [Range(0f, 0.9f)] public float fullVolumeFraction = 0.25f;

        [Header("Through a floor")]
        [Tooltip("A sound from another storey is this much quieter. The information ladder of " +
                 "docs/07 starts here: knowing somebody is near is not knowing where they are.")]
        [Range(0f, 1f)] public float throughFloorVolume = 0.45f;

        [Tooltip("And carries this much less far.")]
        [Range(0f, 1f)] public float throughFloorRange = 0.6f;

        [Tooltip("Vertical distance past which a sound counts as coming through a floor. A storey " +
                 "is 3 m, so a little over half of one.")]
        public float floorSeparation = 1.8f;

        public float RangeFor(NoiseLevel level) => level switch
        {
            NoiseLevel.Quiet => quietRange,
            NoiseLevel.Medium => mediumRange,
            NoiseLevel.Loud => loudRange,
            _ => 0f
        };

        /// <summary>
        /// How loudly a listener at one point hears something at another, 0 when out of range.
        /// The same answer drives the sound and the on-screen indicator, which is the point: a
        /// player who cannot hear must get exactly the information a player who can gets.
        /// </summary>
        public float Audibility(NoiseLevel level, Vector2 source, Vector2 listener, out bool muffled)
        {
            muffled = Mathf.Abs(source.y - listener.y) > floorSeparation;

            float range = RangeFor(level);
            if (range <= 0.01f) return 0f;
            if (muffled) range *= throughFloorRange;

            float distance = Vector2.Distance(source, listener);
            if (distance >= range) return 0f;

            float full = range * fullVolumeFraction;
            float v = distance <= full ? 1f : 1f - (distance - full) / Mathf.Max(0.01f, range - full);

            return Mathf.Clamp01(v) * (muffled ? throughFloorVolume : 1f);
        }
    }
}
