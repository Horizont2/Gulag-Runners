using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// How the round opens, through the light.
    ///
    /// The opening used to be a camera move on a static subject, which is the cheapest thing a
    /// game can do and looks it: one move, one shot, nothing happening. What makes an opening
    /// read as expensive is not the camera at all — it is that the WORLD changes while the
    /// camera holds still. Dark to lit is the strongest contrast available, it costs two
    /// animated numbers, and on a greybox it does more than any amount of easing.
    ///
    /// So: the arena is down at the start, the lamp over your cage kicks in hard on the first
    /// pull at the door, and the arena comes up as the grille clears your head. Three beats on
    /// three different mechanisms — light, the door, the camera — instead of three seconds of
    /// one.
    ///
    /// Driven off the match clock like everything else in the opening, so both devices see the
    /// same frame at the same tick (docs/06), and presentation only: nothing here writes to the
    /// simulation.
    /// </summary>
    [DefaultExecutionOrder(109)]      // before the intro camera (110), after everything else
    [DisallowMultipleComponent]
    public sealed class RoundIntroLight : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("Leave empty to use the MatchState in the scene.")]
        public MatchState matchState;

        [Tooltip("The arena's own light. Its intensity in the inspector is full, and the " +
                 "opening takes it down from there — so it can be retuned without touching " +
                 "this component.\n\n" +
                 "Optional, and empty on all but one of these: the arena has a single light " +
                 "and both fighters are in cages at the same moment, so one component takes " +
                 "it down and the others only drive their own lamp.")]
        public Light arena;

        [Tooltip("Optional. The lamp over one cage, which snaps on partway through. Leave " +
                 "empty for an opening that only breathes the arena light.")]
        public Light cageLamp;

        [Header("The arena")]
        [Tooltip("What the arena is down to while the shot is held, as a share of its own " +
                 "intensity. Not black: a silhouette you cannot read is not drama, it is a " +
                 "bug report.")]
        [Range(0f, 1f)] public float arenaDown = 0.22f;

        [Tooltip("When the arena starts coming back up, and when it is fully up, as shares of " +
                 "the cage phase. Up should land as the grille clears the fighter's head.")]
        [Range(0f, 1f)] public float arenaUpFrom = 0.57f;
        [Range(0f, 1f)] public float arenaUpBy = 0.95f;

        [Header("The lamp over the cage")]
        [Tooltip("When it comes on, as a share of the cage phase. Put it on the door's first " +
                 "pull: the light and the sound of something being tried are one event.")]
        [Range(0f, 1f)] public float lampAt = 0.14f;

        [Tooltip("How hard it overshoots when it strikes, as a multiple of its own intensity, " +
                 "and how long it takes to settle, as a share of the phase. A lamp that fades " +
                 "up is a dimmer; one that strikes and settles is a lamp.")]
        [Range(1f, 4f)] public float strike = 2.4f;
        [Range(0.01f, 0.5f)] public float strikeFor = 0.09f;

        [Tooltip("How much it flickers once it is lit, as a share of its intensity.")]
        [Range(0f, 0.5f)] public float flicker = 0.12f;
        [Range(0.5f, 30f)] public float flickerSpeed = 11f;

        float _arenaFull = -1f;
        float _lampFull = -1f;
        bool _restored;

        void Reset() => matchState = MatchState.Instance;

        void Awake()
        {
            if (matchState == null) matchState = MatchState.Instance;
            Remember();
        }

        /// <summary>
        /// Each light's own intensity, read once and never written back over.
        ///
        /// The same trap as the fighters coming out white: a component that drives a value
        /// every frame has to know what the value WAS, or the first frame it runs becomes the
        /// new truth and the authored setting is gone.
        /// </summary>
        void Remember()
        {
            if (arena != null && _arenaFull < 0f) _arenaFull = arena.intensity;
            if (cageLamp != null && _lampFull < 0f) _lampFull = cageLamp.intensity;
            if (cageLamp != null) cageLamp.enabled = false;
        }

        void LateUpdate()
        {
            if (matchState == null) matchState = MatchState.Instance;
            if (matchState == null) return;
            Remember();

            MatchSimState m = matchState.Match;

            if (m.Phase != MatchPhase.Cages)
            {
                // Put it all back, once, and then leave the lights alone for the rest of the
                // match. A component that keeps writing is a component that cannot be tuned.
                if (_restored) return;
                _restored = true;
                if (arena != null) arena.intensity = _arenaFull;
                if (cageLamp != null) cageLamp.enabled = false;
                return;
            }

            _restored = false;
            float t = matchState.Match.CageProgress(matchState.MatchRules).Raw / (float)Fix.RawOne;

            if (arena != null)
            {
                float up = Smooth(Mathf.InverseLerp(arenaUpFrom, arenaUpBy, t));
                arena.intensity = _arenaFull * Mathf.Lerp(arenaDown, 1f, up);
            }

            if (cageLamp == null) return;

            bool on = t >= lampAt;
            if (cageLamp.enabled != on) cageLamp.enabled = on;
            if (!on) return;

            float since = Mathf.InverseLerp(lampAt, lampAt + strikeFor, t);
            float hot = Mathf.Lerp(strike, 1f, Smooth(since));
            float noise = 1f + flicker * (Mathf.PerlinNoise(Time.time * flickerSpeed, 0.5f) - 0.5f);
            cageLamp.intensity = _lampFull * hot * noise;
        }

        static float Smooth(float k)
        {
            k = Mathf.Clamp01(k);
            return k * k * (3f - 2f * k);
        }
    }
}
