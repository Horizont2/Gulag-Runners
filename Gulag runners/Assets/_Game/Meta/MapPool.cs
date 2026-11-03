using System;
using UnityEngine;

namespace GulagRunners.Meta
{
    /// <summary>
    /// Which arenas a match may be played in, and how the two phones agree on one.
    ///
    /// THE PROBLEM THIS SOLVES IS NOT CHOOSING. It is choosing the SAME ONE, twice, on
    /// two devices, without asking each other. Until now the arena was a string on
    /// MenuDirector -- one scene, so nothing could disagree. Add a second and the obvious
    /// answer is to let the host pick and send it, which costs a round trip at exactly
    /// the moment both devices are already waiting on each other, and leaves a new way
    /// for a match to half-start: one phone in the fortress, the other in the forest.
    ///
    /// So nothing is sent. The pairing already carries a seed -- the number the loot is
    /// dealt from, which both devices have before either starts loading -- and the arena
    /// is dealt from it the same way. Same seed, same list, same arena, no message, and
    /// no moment where the two could disagree.
    ///
    /// That puts a rule on this asset: it is part of the protocol. Two builds with
    /// different pools are two builds that cannot play each other, and the failure looks
    /// like a bug in the netcode rather than a mismatched file. <see cref="Fingerprint"/>
    /// is what makes that sayable out loud instead of mysterious.
    ///
    /// WEIGHTS, not a plain list, because the first location is finished and the next one
    /// never is. A map at weight 1 beside two at weight 4 turns up a ninth of the time,
    /// which is enough to find out whether it plays and not enough to be the reason
    /// somebody stops playing.
    /// </summary>
    [CreateAssetMenu(fileName = "MapPool", menuName = "Gulag Runners/Map Pool")]
    public sealed class MapPool : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            [Tooltip("The scene NAME, exactly as the build list has it -- not a path and " +
                     "not the asset. A scene missing from the build list loads fine in " +
                     "the editor and throws on the device, which is the cruellest way " +
                     "for this to be wrong: it works for whoever added the map.")]
            public string scene;

            [Tooltip("What the search screen calls it. The scene name is a file name.")]
            public string shown;

            [Tooltip("Off takes it out of the draw without losing the row. A map that is " +
                     "half dressed belongs here switched off, not deleted and re-typed.")]
            public bool enabled;

            [Tooltip("How often it comes up, against the others. 0 is the same as off.")]
            [Range(0, 10)] public int weight;
        }

        [Tooltip("Every arena a match may be dealt. Order matters: it is part of what " +
                 "both devices agree on, so moving a row changes which map a given seed " +
                 "produces. That is harmless between matches and only matters if one " +
                 "device is on an older build.")]
        public Entry[] arenas = Array.Empty<Entry>();

        [Tooltip("Played when the pool is empty or every row is switched off. The one " +
                 "arena that is always finished, so a broken pool costs a map rather " +
                 "than the match.")]
        public string fallback = "Arena_Demo";

        /// <summary>Total weight of the rows that are actually in the draw.</summary>
        public int Weight
        {
            get
            {
                int total = 0;
                for (int i = 0; i < arenas.Length; i++)
                    if (arenas[i].enabled && arenas[i].weight > 0) total += arenas[i].weight;
                return total;
            }
        }

        /// <summary>
        /// The arena this match is played in, from the number the match is dealt from.
        ///
        /// Integer arithmetic on purpose, and no System.Random: this has to produce the
        /// same answer on two devices, two platforms and two runs, and the only way to
        /// be sure of that is to not leave the question open. It is the same reasoning
        /// as the simulation's fixed point, for the same reason.
        /// </summary>
        /// <summary>
        /// Where in the total weight this seed lands. Pure, static and free of Unity,
        /// so the harness can run the real arithmetic rather than a description of it
        /// -- two phones disagreeing by one bit here is a match that never starts, and
        /// a Python copy of this that drifted would never say so.
        /// </summary>
        public static int Pick(uint seed, int total)
        {
            if (total <= 0) return 0;

            // Mix the seed before using it. The pairing's seed also deals the loot, and
            // a raw low bit shared between "which chest" and "which map" is a pattern
            // somebody would eventually notice: splitmix64's finaliser.
            ulong x = seed + 0x9E3779B97F4A7C15UL;
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            x ^= x >> 31;

            return (int)(x % (ulong)total);
        }

        public string Choose(uint seed)
        {
            int total = Weight;
            if (total <= 0) return fallback;

            int pick = Pick(seed, total);
            for (int i = 0; i < arenas.Length; i++)
            {
                if (!arenas[i].enabled || arenas[i].weight <= 0) continue;
                pick -= arenas[i].weight;
                if (pick < 0)
                    return string.IsNullOrEmpty(arenas[i].scene) ? fallback : arenas[i].scene;
            }
            return fallback;
        }

        /// <summary>What this pool is called on the search screen, for one seed.</summary>
        public string NameOf(string scene)
        {
            for (int i = 0; i < arenas.Length; i++)
                if (arenas[i].scene == scene && !string.IsNullOrEmpty(arenas[i].shown))
                    return arenas[i].shown;
            return scene;
        }

        /// <summary>
        /// A short number that is the same on two devices only if their pools agree.
        ///
        /// Worth having because the failure it names is otherwise invisible: two builds
        /// whose pools differ deal different arenas from the same seed, and what the
        /// players see is a match that connects, counts down and never starts. Printed
        /// by the link on connecting, so the first line of the log says whether the two
        /// are playing the same game.
        /// </summary>
        public uint Fingerprint
        {
            get
            {
                unchecked
                {
                    uint h = 2166136261u;
                    for (int i = 0; i < arenas.Length; i++)
                    {
                        string s = arenas[i].scene ?? "";
                        for (int c = 0; c < s.Length; c++) h = (h ^ s[c]) * 16777619u;
                        h = (h ^ (uint)(arenas[i].enabled ? arenas[i].weight : 0)) * 16777619u;
                    }
                    return h;
                }
            }
        }
    }
}
