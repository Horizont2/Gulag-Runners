using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// Shows what the simulation is doing to one chest.
    ///
    /// The lid is driven as a POSE from the opening progress rather than played as a clip when
    /// the chest pops: prying a locker takes two and a half seconds, and a lid that stays shut
    /// for all of them and then flips is a chest that gives the player no feedback for the
    /// longest commitment of the scavenge phase. Scrubbing the pack's animation by progress means
    /// the lid creaks open exactly as fast as the player is working, and it needs no guess about
    /// how long the clip is.
    ///
    /// Presentation only. Nothing here may write to the simulation (docs/06).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Chest))]
    public sealed class ChestView : MonoBehaviour
    {
        [Header("Wiring")]
        public Chest chest;

        [Tooltip("The model's Animator. Leave empty to take the one on the model under this " +
                 "object.")]
        public Animator animator;

        [Tooltip("The shared round state. Leave empty to use the one in the scene.")]
        public MatchState matchState;

        [Header("Lid")]
        [Tooltip("The state in the model's own Animator Controller that holds the open/close " +
                 "animation.")]
        public string stateName = "Fantasy_Polygon_Chest_Animation";

        [Tooltip("Normalised time in that clip where the lid is shut, and where it is fully " +
                 "open. Scrub the Animator in the inspector to read them off the model; the " +
                 "defaults are the first half of a clip that opens and then closes again.")]
        [Range(0f, 1f)] public float closedPose;
        [Range(0f, 1f)] public float openPose = 0.5f;

        [Tooltip("How quickly the lid catches up with the opening progress, in seconds. A little " +
                 "lag reads as weight; none reads as a slider.")]
        [Range(0f, 0.5f)] public float lidSmoothTime = 0.08f;

        int _index = -1;
        float _shown;
        float _shownVelocity;
        bool _warned;

        void Reset()
        {
            chest = GetComponent<Chest>();
            animator = GetComponentInChildren<Animator>();
        }

        void Awake()
        {
            if (chest == null) chest = GetComponent<Chest>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (matchState == null) matchState = MatchState.Instance;

            if (animator != null)
            {
                // The pose is set every frame from the simulation, so the clip must never advance
                // on its own, and it must keep doing so off camera: with two cameras each showing
                // one player, culling would freeze every chest on the other half of the screen.
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.speed = 0f;
            }
        }

        void Start()
        {
            if (matchState == null) matchState = MatchState.Instance;
            _index = ResolveIndex();
            _shown = closedPose;
            Apply(_shown);
        }

        /// <summary>
        /// Which chest in the baked list this is. The baker keeps the components it read in the
        /// same order as the boxes it produced, so this is a lookup rather than a guess.
        /// </summary>
        int ResolveIndex()
        {
            SimWorldBaker baker = matchState != null ? matchState.worldBaker : SimWorldBaker.Instance;
            if (baker == null || baker.ChestSources == null) return -1;

            for (int i = 0; i < baker.ChestSources.Length; i++)
                if (ReferenceEquals(baker.ChestSources[i], chest)) return i;

            return -1;
        }

        void Update()
        {
            if (animator == null || matchState == null) return;

            if (_index < 0)
            {
                _index = ResolveIndex();
                if (_index < 0)
                {
                    if (!_warned)
                    {
                        _warned = true;
                        Debug.LogWarning($"{name}: this chest is not in the baked world, so it " +
                                         "cannot be opened. Check that the baker ran after it " +
                                         "was added to the scene.", this);
                    }
                    return;
                }
            }

            float want = Mathf.Lerp(closedPose, openPose, Progress());
            _shown = lidSmoothTime <= 0.001f
                ? want
                : Mathf.SmoothDamp(_shown, want, ref _shownVelocity, lidSmoothTime,
                                   Mathf.Infinity, Time.deltaTime);
            Apply(_shown);
        }

        /// <summary>How far open this chest is, 0 to 1.</summary>
        public float Progress()
        {
            ChestSimState[] states = matchState != null ? matchState.ChestStates : null;
            if (states == null || _index < 0 || _index >= states.Length) return 0f;
            if (states[_index].Opened) return 1f;

            ChestConfig cfg = matchState.Config;
            int needed = cfg.For(chest.kind).BareFrames * 1000;
            if (needed <= 0) return 0f;

            return Mathf.Clamp01(states[_index].Progress / (float)needed);
        }

        void Apply(float normalisedTime)
        {
            if (animator == null || string.IsNullOrEmpty(stateName)) return;

            int hash = Animator.StringToHash(stateName);
            if (!animator.HasState(0, hash))
            {
                if (!_warned)
                {
                    _warned = true;
                    Debug.LogWarning($"{name}: the chest model's Animator has no state called " +
                                     $"\"{stateName}\".", this);
                }
                return;
            }

            animator.speed = 0f;
            animator.Play(hash, 0, Mathf.Repeat(normalisedTime, 1f));
        }
    }
}
