using System;
using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// Inspector-friendly mirror of <see cref="PoisonConfig"/>, in seconds rather than frames.
    /// The same boundary rule as <see cref="MovementTuning"/>: floats stop here.
    ///
    /// WHERE the poison is belongs to the arena and is authored as a box — see
    /// <see cref="PoisonPool"/>. This is only how fast it comes up and what it costs, because
    /// those are part of the shape of a match rather than part of a location: both devices
    /// read them off the same MatchState, exactly as they do the phase lengths beside them.
    /// </summary>
    [Serializable]
    public class PoisonTuning
    {
        [Tooltip("Seconds the surface takes to climb the whole pool, from the first tick of " +
                 "the closing phase.\n\n" +
                 "Left at the closing phase's own length it tops out exactly as sudden death " +
                 "begins — the beat docs/01 gives the last shutter. Shorter and the flood is " +
                 "a race; much shorter and it is a trap, which is the one thing it must not " +
                 "be, because the whole argument for poison over steel is that you can see it " +
                 "coming for a minute and a half.")]
        [Range(5f, 180f)] public float riseSeconds = 80f;

        [Tooltip("Seconds between bites. The damage comes as a series of small bites rather " +
                 "than a rate, because health is a whole number: anything under one point a " +
                 "tick would either round away to nothing or need a fraction carried in the " +
                 "state, and rollback does not want another number to carry.")]
        [Range(0.1f, 3f)] public float biteSeconds = 0.5f;

        [Tooltip("Health one bite takes. Two every half second is four a second, which is " +
                 "twenty-five seconds from full health: long enough to cross a flooded floor " +
                 "for a chest, short enough that doing it twice is why you lost.")]
        [Range(0, 20)] public int bite = 2;

        [Tooltip("Switches the poison off without touching the arena. The pool stays where it " +
                 "is authored and never fills, which is how you play a round in a flooded " +
                 "location without the flood — the movement sandbox of docs/11, say.")]
        public bool poisonRises = true;

        public PoisonConfig ToConfig() => new PoisonConfig
        {
            RiseFrames = Mathf.Max(1, Mathf.RoundToInt(riseSeconds * PlayerMotor.TicksPerSecond)),
            BiteFrames = Mathf.Max(1, Mathf.RoundToInt(biteSeconds * PlayerMotor.TicksPerSecond)),
            Bite = Mathf.Max(0, bite),
            On = poisonRises,
        };
    }
}
