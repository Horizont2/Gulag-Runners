using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// The cage door at the start of a round.
    ///
    /// docs/01 opens the match with both fighters in cages at opposite ends, and the door is
    /// the first thing either of them sees. It does not glide: it is hauled. A jerk, a stall
    /// where whatever is lifting it loses the weight, and then the rest of the way — because a
    /// door that someone is opening and a door that opens are two different shots, and only
    /// one of them says there is somebody out there.
    ///
    /// Driven entirely by the match clock, so both devices show the same door at the same
    /// moment and nobody can act until it is up (docs/06). Everything about the shape of the
    /// move is in the inspector, because it is a performance and performances get tuned.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CageDoor : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("Leave empty to use the MatchState in the scene.")]
        public MatchState matchState;

        [Tooltip("What moves. Leave empty to move this object.")]
        public Transform door;

        [Header("How far it goes")]
        [Tooltip("How high the door ends up, in metres. Enough to walk under and no more.")]
        public float openHeight = 2.2f;

        [Tooltip("How far the first jerk takes it, as a share of the way up. Short: it is the " +
                 "sound of something heavy being tried, not the door opening.")]
        [Range(0f, 0.6f)] public float jerkTo = 0.22f;

        [Header("The timing, as shares of the cage phase")]
        [Tooltip("Nothing happens for this long. The round has started and the door has not " +
                 "moved, which is the beat that makes the jerk land.")]
        [Range(0f, 0.6f)] public float waitUntil = 0.18f;

        [Tooltip("The jerk is over by here.")]
        [Range(0f, 0.9f)] public float jerkUntil = 0.3f;

        [Tooltip("It hangs there, caught, until here. This is the whole trick: somebody is " +
                 "lifting it and they have stopped.")]
        [Range(0f, 0.95f)] public float holdUntil = 0.52f;

        [Tooltip("And it is fully up by here, leaving the rest of the phase to stand in the " +
                 "doorway before anyone can move.")]
        [Range(0.1f, 1f)] public float openUntil = 0.9f;

        [Header("Shake")]
        [Tooltip("How far the door rattles as it is hauled, in metres.")]
        [Range(0f, 0.08f)] public float rattle = 0.015f;
        [Range(0f, 60f)] public float rattleSpeed = 34f;

        [Header("Sound")]
        [Tooltip("Played once as the jerk starts, and once as the haul begins.")]
        public AudioSource jerkSound;
        public AudioSource haulSound;

        Vector3 _shut;
        bool _jerked, _hauled;

        void Reset() => door = transform;

        void Awake()
        {
            if (matchState == null) matchState = MatchState.Instance;
            if (door == null) door = transform;
            _shut = door.localPosition;
        }

        void LateUpdate()
        {
            if (door == null) return;

            float t = Progress();
            float lift = Lift(t) * openHeight;

            // The rattle only while it is actually being moved, or a door standing open hums.
            float moving = (t > waitUntil && t < jerkUntil) || (t > holdUntil && t < openUntil)
                ? 1f : 0f;
            float shake = moving * rattle * Mathf.Sin(Time.time * rattleSpeed);

            door.localPosition = _shut + new Vector3(0f, lift + shake, 0f);

            if (!_jerked && t >= waitUntil) { _jerked = true; Play(jerkSound); }
            if (!_hauled && t >= holdUntil) { _hauled = true; Play(haulSound); }
            if (t <= 0.001f) { _jerked = false; _hauled = false; }     // a new round
        }

        /// <summary>How far through the cage phase we are, 0 to 1. Open if there is no match.</summary>
        float Progress()
        {
            if (matchState == null) matchState = MatchState.Instance;
            if (matchState == null) return 1f;

            MatchSimState m = matchState.Match;
            if (m.Phase != MatchPhase.Cages) return 1f;
            return m.CageProgress(matchState.MatchRules).Raw / (float)Fix.RawOne;
        }

        /// <summary>
        /// The door's height over the phase: still, jerk, caught, hauled up.
        ///
        /// The stall is the point. A linear rise reads as a mechanism; a rise that stops
        /// halfway reads as a person, and docs/01 wants the first thing the player learns to
        /// be that there is somebody out there.
        /// </summary>
        float Lift(float t)
        {
            if (t <= waitUntil) return 0f;

            if (t < jerkUntil)
            {
                // Fast out of the gate and decelerating: something took the weight all at once.
                float k = Mathf.InverseLerp(waitUntil, jerkUntil, t);
                return jerkTo * (1f - (1f - k) * (1f - k));
            }

            if (t < holdUntil) return jerkTo;

            float h = Mathf.InverseLerp(holdUntil, openUntil, t);
            return Mathf.Lerp(jerkTo, 1f, h * h * (3f - 2f * h));       // eased, and committed
        }

        static void Play(AudioSource source)
        {
            if (source != null) source.Play();
        }

        void OnDrawGizmosSelected()
        {
            Transform d = door != null ? door : transform;
            Gizmos.color = new Color(1f, 0.78f, 0.25f, 0.9f);
            Vector3 from = Application.isPlaying ? _shut : d.localPosition;
            Vector3 a = d.parent != null ? d.parent.TransformPoint(from) : from;
            Gizmos.DrawLine(a, a + Vector3.up * openHeight);
            Gizmos.DrawWireSphere(a + Vector3.up * openHeight, 0.06f);
            Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.9f);
            Gizmos.DrawWireSphere(a + Vector3.up * openHeight * jerkTo, 0.05f);
        }
    }
}
