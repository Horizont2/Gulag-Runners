using System.Collections.Generic;
using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// The one place that decides what is heard, how loudly, and from where.
    ///
    /// The simulation already produces a <see cref="NoiseLevel"/> and a position every tick —
    /// footsteps, landings, climbing, prying a chest, taking something off the floor. Until now
    /// nothing listened, which meant the entire cost side of the loot system was inaudible: the
    /// chest table of docs/03 is a noise table, and a player who cannot hear noise is choosing
    /// between "faster" and "slower" rather than between "faster" and "quieter".
    ///
    /// Two deliberate choices:
    ///
    /// The hearing model is OURS, not Unity's. Range, falloff and what a floor does are in
    /// <see cref="HearingTuning"/> and are applied here, so the backend is handed a finished
    /// volume. That keeps the sound and the on-screen indicator telling the same story — docs/02
    /// requires the indicator to be a default rather than an option, which only works if a player
    /// with the sound off learns exactly what a player with it on learns.
    ///
    /// And nothing here knows what a clip is. It asks <see cref="IAudioBackend"/> for a SoundId
    /// at a volume; whether that is an AudioSource or an FMOD event is the backend's business.
    /// </summary>
    [DefaultExecutionOrder(140)]
    [DisallowMultipleComponent]
    public sealed class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        [Header("Backend")]
        [Tooltip("What actually makes the noise. Drop in an FmodAudioBackend here instead and " +
                 "nothing else in the project changes.")]
        public MonoBehaviour backendBehaviour;

        [Header("Hearing (docs/01: sound is the information channel)")]
        public HearingTuning hearing = new HearingTuning();

        [Header("Sounds")]
        [Tooltip("Fill these in as the game is built. An empty row is silent and harmless.")]
        public SoundTable sounds = new SoundTable();

        [Header("Listeners")]
        [Tooltip("Who is listening. On a device this is the one local player; in a split-screen " +
                 "test it is both, and each one gets its own indicator.")]
        public List<NoiseListener> listeners = new List<NoiseListener>();

        [Tooltip("Follow every PlayerController in the scene automatically, so a player added to " +
                 "a scene is heard without being wired up by hand.")]
        public bool autoSubscribe = true;

        [Header("Debug")]
        public bool logSounds;

        IAudioBackend _backend;
        readonly Dictionary<SoundId, float> _lastPlayed = new Dictionary<SoundId, float>();
        readonly List<PlayerController> _subscribed = new List<PlayerController>();

        void Awake()
        {
            Instance = this;
            _backend = backendBehaviour as IAudioBackend;

            if (backendBehaviour != null && _backend == null)
                Debug.LogError($"{name}: Backend Behaviour is not an IAudioBackend.", this);
        }

        void OnEnable()
        {
            if (Instance == null) Instance = this;
            if (autoSubscribe) Subscribe();
        }

        void OnDisable()
        {
            foreach (PlayerController p in _subscribed)
            {
                if (p == null) continue;
                p.Noise -= OnNoise;
                p.Landed -= OnLanded;
                p.PickedUp -= OnPickedUp;
                p.Dropped -= OnDropped;
            }
            _subscribed.Clear();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Subscribe()
        {
            foreach (PlayerController p in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
            {
                if (p == null || _subscribed.Contains(p)) continue;
                p.Noise += OnNoise;
                p.Landed += OnLanded;
                p.PickedUp += OnPickedUp;
                p.Dropped += OnDropped;
                _subscribed.Add(p);

                if (p.GetComponent<NoiseListener>() is NoiseListener l && !listeners.Contains(l))
                    listeners.Add(l);
            }
        }

        // ---------------------------------------------------------------- the simulation talking

        void OnNoise(NoiseLevel level, Vector2 at)
        {
            if (level == NoiseLevel.Silent) return;
            Post(SoundFor(level), at, level, at);
        }

        void OnLanded(float impactSpeed) { }      // the landing's own noise arrives through OnNoise

        void OnPickedUp(ItemId item) =>
            PostAtListeners(SoundId.ItemPickup, NoiseLevel.Quiet);

        void OnDropped(ItemId item) =>
            PostAtListeners(SoundId.ItemDrop, NoiseLevel.Quiet);

        void PostAtListeners(SoundId sound, NoiseLevel level)
        {
            // Events that carry no position of their own happen where the player is, and the
            // player is a listener, so the nearest listener's position is the right answer.
            if (listeners.Count == 0) return;
            Vector2 at = listeners[0] != null ? listeners[0].Position : Vector2.zero;
            Post(sound, at, level, at);
        }

        static SoundId SoundFor(NoiseLevel level) => level switch
        {
            NoiseLevel.Quiet => SoundId.FootstepQuiet,
            NoiseLevel.Medium => SoundId.LandSoft,
            NoiseLevel.Loud => SoundId.FootstepLoud,
            _ => SoundId.None
        };

        // ---------------------------------------------------------------- the public door

        /// <summary>
        /// Something happened at a place, and it was this loud. Call it from anywhere; the
        /// hearing model decides who gets to know.
        /// </summary>
        public void Post(SoundId sound, Vector2 at, NoiseLevel level, Vector2 source)
        {
            if (sound == SoundId.None) return;

            SoundEntry entry = sounds.Find(sound);
            if (entry != null && entry.minInterval > 0f &&
                _lastPlayed.TryGetValue(sound, out float last) &&
                Time.time - last < entry.minInterval)
                return;

            float loudest = 0f;
            float pan = 0f;
            bool muffled = true;

            for (int i = 0; i < listeners.Count; i++)
            {
                NoiseListener listener = listeners[i];
                if (listener == null) continue;

                float v = hearing.Audibility(level, source, listener.Position, out bool thisMuffled);
                if (v <= 0.001f) continue;

                listener.Hear(sound, level, source, v, thisMuffled);

                if (v <= loudest) continue;
                loudest = v;
                muffled = thisMuffled;
                pan = Mathf.Clamp(source.x - listener.Position.x, -1f, 1f);
            }

            if (loudest <= 0.001f) return;

            _lastPlayed[sound] = Time.time;

            if (logSounds)
                Debug.Log($"{name}: {sound} at {source} — {loudest:0.00}" + (muffled ? ", muffled" : ""));

            if (_backend == null || !_backend.Ready) return;
            _backend.Play(sound, new Vector3(at.x, at.y, 0f), loudest, pan, muffled);
        }

        /// <summary>A sound with no bearing on the fight: UI, round start. Everyone hears it.</summary>
        public void PostGlobal(SoundId sound)
        {
            if (sound == SoundId.None || _backend == null || !_backend.Ready) return;
            _backend.Play(sound, Vector3.zero, 1f, 0f, false);
        }
    }
}
