using System;
using System.Collections.Generic;
using UnityEngine;

namespace GulagRunners.Game
{
    /// <summary>
    /// One row of the sound table: what a <see cref="SoundId"/> sounds like.
    ///
    /// Carries both a Unity clip and an FMOD event path. Only one of them is used, depending on
    /// which backend is wired up, and filling the other in costs nothing — which is what lets the
    /// clips be dropped in one at a time while the game is built, and the FMOD paths be filled in
    /// later without the rows being re-authored.
    /// </summary>
    [Serializable]
    public class SoundEntry
    {
        public SoundId sound = SoundId.None;

        [Tooltip("Picked at random. More than one keeps a repeated sound — footsteps above all — " +
                 "from turning into a machine gun.")]
        public AudioClip[] clips;

        [Tooltip("FMOD event path, e.g. event:/Player/Footstep. Ignored by the Unity backend.")]
        public string fmodEvent = "";

        [Range(0f, 2f)] public float volume = 1f;

        [Tooltip("Random pitch range. A little variation is the difference between a footstep and " +
                 "a metronome.")]
        public Vector2 pitchRange = new Vector2(0.94f, 1.06f);

        [Tooltip("Shortest gap between two of this sound, in seconds. Stops one tick of the " +
                 "simulation producing a burst.")]
        public float minInterval;

        public bool HasClip => clips != null && clips.Length > 0;
    }

    /// <summary>The whole table, with a lookup that does not allocate per call.</summary>
    [Serializable]
    public class SoundTable
    {
        public List<SoundEntry> entries = new List<SoundEntry>();

        Dictionary<SoundId, SoundEntry> _index;

        public SoundEntry Find(SoundId sound)
        {
            if (_index == null)
            {
                _index = new Dictionary<SoundId, SoundEntry>();
                for (int i = 0; i < entries.Count; i++)
                {
                    SoundEntry e = entries[i];
                    if (e != null && e.sound != SoundId.None) _index[e.sound] = e;
                }
            }

            return _index.TryGetValue(sound, out SoundEntry found) ? found : null;
        }

        public void Invalidate() => _index = null;

        /// <summary>
        /// Adds a blank row for every sound the game can make. The table is meant to be filled in
        /// as the game is built, and an empty row is an obvious gap; a missing row is not.
        /// </summary>
        public int FillGaps()
        {
            int added = 0;
            foreach (SoundId id in (SoundId[])Enum.GetValues(typeof(SoundId)))
            {
                if (id == SoundId.None) continue;
                if (entries.Exists(e => e != null && e.sound == id)) continue;
                entries.Add(new SoundEntry { sound = id });
                added++;
            }

            entries.Sort((a, b) => ((int)a.sound).CompareTo((int)b.sound));
            Invalidate();
            return added;
        }
    }
}
