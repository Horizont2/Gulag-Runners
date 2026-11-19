using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>One sound this listener can currently still hear.</summary>
    public struct HeardSound
    {
        public SoundId Sound;
        public NoiseLevel Level;
        public Vector2 Source;
        public float Loudness;
        public bool Muffled;
        public float Age;

        public bool Live(float life) => Age < life;
    }

    /// <summary>
    /// A pair of ears belonging to one player.
    ///
    /// Keeps the handful of sounds it can still hear, so the on-screen indicator has something to
    /// draw. docs/02 makes that indicator a default rather than an accessibility option — the
    /// game is played on phones, often muted, and a player who cannot hear the other one is
    /// playing a different game to the one that was designed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NoiseListener : MonoBehaviour
    {
        [Tooltip("The player these ears belong to. A player never hears themselves.")]
        public PlayerController player;

        [Tooltip("How long a sound stays on the indicator, in seconds. Long enough to read, " +
                 "short enough that it is news rather than a map.")]
        [Range(0.2f, 4f)] public float memory = 1.2f;

        [Tooltip("How many sounds can be shown at once. More than a few is not information, " +
                 "it is clutter.")]
        [Range(1, 8)] public int capacity = 4;

        HeardSound[] _heard;
        int _count;

        public Vector2 Position => player != null
            ? player.FeetPosition
            : (Vector2)transform.position;

        public int Count => _count;
        public HeardSound Get(int i) => _heard[i];

        void Reset() => player = GetComponent<PlayerController>();

        void Awake()
        {
            if (player == null) player = GetComponent<PlayerController>();
            _heard = new HeardSound[Mathf.Max(1, capacity)];
        }

        /// <summary>
        /// Takes note of a sound. A player's own noise is dropped: the whole point of the channel
        /// is where the OTHER one is, and your own footsteps would bury it.
        /// </summary>
        public void Hear(SoundId sound, NoiseLevel level, Vector2 source, float loudness, bool muffled)
        {
            if (loudness <= 0.001f) return;
            if (player != null && (source - player.FeetPosition).sqrMagnitude < 0.25f) return;

            // A second sound from nearly the same place replaces the first rather than stacking:
            // one player prying a chest is one thing happening, not forty.
            for (int i = 0; i < _count; i++)
            {
                if ((_heard[i].Source - source).sqrMagnitude > 4f) continue;
                _heard[i] = Make(sound, level, source, Mathf.Max(loudness, _heard[i].Loudness), muffled);
                return;
            }

            if (_count < _heard.Length)
            {
                _heard[_count++] = Make(sound, level, source, loudness, muffled);
                return;
            }

            // Full: the faintest thing we are already tracking is the least worth keeping.
            int weakest = 0;
            for (int i = 1; i < _count; i++)
                if (_heard[i].Loudness < _heard[weakest].Loudness) weakest = i;

            if (_heard[weakest].Loudness < loudness)
                _heard[weakest] = Make(sound, level, source, loudness, muffled);
        }

        static HeardSound Make(SoundId sound, NoiseLevel level, Vector2 source, float loudness,
                               bool muffled) => new HeardSound
        {
            Sound = sound, Level = level, Source = source,
            Loudness = loudness, Muffled = muffled, Age = 0f
        };

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = _count - 1; i >= 0; i--)
            {
                _heard[i].Age += dt;
                if (_heard[i].Live(memory)) continue;
                _heard[i] = _heard[_count - 1];
                _count--;
            }
        }

        /// <summary>Fade of one heard sound, 1 when fresh and 0 when forgotten.</summary>
        public float Freshness(int i) => Mathf.Clamp01(1f - _heard[i].Age / Mathf.Max(0.01f, memory));
    }
}
