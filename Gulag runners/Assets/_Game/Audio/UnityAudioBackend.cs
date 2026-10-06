using UnityEngine;

namespace GulagRunners.Game
{
    /// <summary>
    /// The default backend: a fixed set of AudioSources placed in the scene, used in turn.
    ///
    /// Nothing is created at runtime — the voices are children of this object and visible in the
    /// inspector, which is also what makes it obvious how many sounds can overlap. Distance is
    /// not Unity's problem here: <see cref="AudioManager"/> hands over a finished volume, so
    /// every source is 2D and the hearing model stays in one place.
    ///
    /// This is the class FMOD replaces. Nothing else has to move.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UnityAudioBackend : MonoBehaviour, IAudioBackend
    {
        [Tooltip("The voices. One sound each, used round-robin, so this count is the most that " +
                 "can overlap. Six is plenty for two fighters.")]
        public AudioSource[] voices;

        [Tooltip("Master volume for everything this backend plays.")]
        [Range(0f, 1f)] public float masterVolume = 1f;

        [Tooltip("What a sound heard through a floor is multiplied by, on top of the hearing " +
                 "model. Unity has no filter here — FMOD would use a real low-pass — so it is " +
                 "faked with volume and a lower pitch, which reads as 'somewhere below'.")]
        [Range(0f, 1f)] public float muffledVolume = 0.8f;
        [Range(0.5f, 1f)] public float muffledPitch = 0.85f;

        public AudioManager manager;

        int _next;

        public bool Ready => voices != null && voices.Length > 0;

        void Reset() => voices = GetComponentsInChildren<AudioSource>();

        void Awake()
        {
            if (manager == null) manager = GetComponentInParent<AudioManager>();
            if (voices == null || voices.Length == 0) voices = GetComponentsInChildren<AudioSource>();

            if (voices == null || voices.Length == 0)
            {
                Debug.LogWarning($"{name}: no AudioSources, so the game will be silent. Add a few " +
                                 "as children of this object and list them above.", this);
                return;
            }

            foreach (AudioSource v in voices)
            {
                if (v == null) continue;
                v.playOnAwake = false;
                v.spatialBlend = 0f;        // the hearing model already decided how far away it is
                v.loop = false;
            }
        }

        public void Play(SoundId sound, Vector3 worldPosition, float volume, float pan, bool muffled)
        {
            if (!Ready || manager == null) return;

            SoundEntry entry = manager.sounds.Find(sound);
            if (entry == null || !entry.HasClip) return;      // not filled in yet: silence, not an error

            AudioSource voice = NextFreeVoice();
            if (voice == null) return;

            AudioClip clip = entry.clips[Random.Range(0, entry.clips.Length)];
            if (clip == null) return;

            voice.clip = clip;
            voice.volume = Mathf.Clamp01(volume * entry.volume * masterVolume *
                                         (muffled ? muffledVolume : 1f));
            voice.pitch = Random.Range(entry.pitchRange.x, entry.pitchRange.y) *
                          (muffled ? muffledPitch : 1f);
            voice.panStereo = Mathf.Clamp(pan, -1f, 1f);
            voice.Play();
        }

        /// <summary>A silent voice if there is one, otherwise the next in turn.</summary>
        AudioSource NextFreeVoice()
        {
            for (int i = 0; i < voices.Length; i++)
            {
                AudioSource v = voices[(_next + i) % voices.Length];
                if (v != null && !v.isPlaying)
                {
                    _next = (_next + i + 1) % voices.Length;
                    return v;
                }
            }

            AudioSource stolen = voices[_next];
            _next = (_next + 1) % voices.Length;
            return stolen;
        }
    }
}
