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

        [Tooltip("How far the SECOND pull takes it. The first pull is somebody testing the " +
                 "weight; the second is them getting a grip on it.")]
        [Range(0f, 0.9f)] public float jerkTo2 = 0.44f;

        [Header("The timing, as shares of the cage phase")]
        [Tooltip("Nothing happens for this long. The round has started and the door has not " +
                 "moved, which is the beat that makes the first pull land.")]
        [Range(0f, 0.6f)] public float waitUntil = 0.14f;

        [Tooltip("The first pull is over by here.")]
        [Range(0f, 0.9f)] public float jerkUntil = 0.22f;

        [Tooltip("It hangs there, caught, until here. This is the whole trick: somebody is " +
                 "lifting it and they have stopped. The longest beat of the four.")]
        [Range(0f, 0.95f)] public float holdUntil = 0.4f;

        [Tooltip("The second pull is over by here.")]
        [Range(0f, 0.95f)] public float jerk2Until = 0.49f;

        [Tooltip("And it hangs again until here — shorter this time, because whoever is doing " +
                 "it now knows they can.")]
        [Range(0f, 0.97f)] public float hold2Until = 0.57f;

        [Tooltip("And it is fully up by here, leaving the rest of the phase to stand in the " +
                 "doorway before anyone can move.")]
        [Range(0.1f, 1f)] public float openUntil = 0.93f;

        [Header("Shake")]
        [Tooltip("How far the door rattles as it is hauled, in metres.")]
        [Range(0f, 0.08f)] public float rattle = 0.022f;
        [Range(0f, 60f)] public float rattleSpeed = 41f;

        [Tooltip("How far it drops back on the catch, as a share of the travel. Small and " +
                 "quick: the weight takes it back a little the moment the pull stops, which " +
                 "is the single clearest sign that something is being LIFTED.")]
        [Range(0f, 0.12f)] public float sag = 0.035f;

        [Tooltip("How hard it bangs into the top of the frame, as a share of the travel, and " +
                 "for how long as a share of the phase.")]
        [Range(0f, 0.15f)] public float slam = 0.045f;
        [Range(0.01f, 0.3f)] public float slamFor = 0.06f;

        [Header("Sound")]
        [Tooltip("Played once as the jerk starts, and once as the haul begins.")]
        public AudioSource jerkSound;
        public AudioSource haulSound;

        Vector3 _shut;
        bool _jerked, _jerked2, _hauled;

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
            // Hardest on the long haul, because that is the part being dragged rather than
            // snatched, and a chain under steady load is what rattles.
            float pulling = Between(t, waitUntil, jerkUntil) + Between(t, holdUntil, jerk2Until);
            float hauling = Between(t, hold2Until, openUntil);
            float shake = (pulling * 0.6f + hauling) * rattle *
                          Mathf.Sin(Time.time * rattleSpeed);

            door.localPosition = _shut + new Vector3(0f, lift + shake, 0f);

            if (!_jerked && t >= waitUntil) { _jerked = true; Play(jerkSound); }
            if (!_jerked2 && t >= holdUntil) { _jerked2 = true; Play(jerkSound); }
            if (!_hauled && t >= hold2Until) { _hauled = true; Play(haulSound); }
            if (t <= 0.001f) { _jerked = _jerked2 = _hauled = false; }   // a new round
        }

        static float Between(float t, float from, float to) => t > from && t < to ? 1f : 0f;

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
        /// The door's height over the phase: still, snatched, dropped back, snatched again,
        /// and then dragged the rest of the way.
        ///
        /// The stalls are the point. A linear rise reads as a mechanism; a rise that stops
        /// reads as a person — and two stops with a SAG between them read as a person who is
        /// not sure they can hold it, which is what docs/01 wants the first thing the player
        /// learns to be. One pull was not enough: with a single jerk and a single catch it
        /// still looked like a door that opens itself, only slower.
        /// </summary>
        float Lift(float t)
        {
            if (t <= waitUntil) return 0f;

            // First pull: fast out of the gate and decelerating. Something took the weight all
            // at once and found out how much of it there is.
            if (t < jerkUntil) return Snatch(t, waitUntil, jerkUntil, 0f, jerkTo);

            // Caught, and sagging: the moment the pull stops, the weight takes a little back.
            if (t < holdUntil) return jerkTo - sag * Settle(t, jerkUntil, holdUntil);

            // Second pull, from the sagged height, further than the first.
            if (t < jerk2Until)
                return Snatch(t, holdUntil, jerk2Until, jerkTo - sag, jerkTo2);

            if (t < hold2Until) return jerkTo2 - sag * 0.6f * Settle(t, jerk2Until, hold2Until);

            // And the haul: slow to start, then committed, because by now they have it.
            if (t < openUntil)
            {
                float h = Mathf.InverseLerp(hold2Until, openUntil, t);
                return Mathf.Lerp(jerkTo2 - sag * 0.6f, 1f, h * h * (3f - 2f * h));
            }

            // Into the top of the frame, and ringing.
            float k = Mathf.InverseLerp(openUntil, Mathf.Min(1f, openUntil + slamFor), t);
            if (k >= 1f) return 1f;
            return 1f + slam * Mathf.Sin(k * Mathf.PI * 3f) * (1f - k);
        }

        /// <summary>A pull: all of its speed at the start, none at the end.</summary>
        static float Snatch(float t, float from, float to, float low, float high)
        {
            float k = Mathf.InverseLerp(from, to, t);
            return Mathf.Lerp(low, high, 1f - (1f - k) * (1f - k));
        }

        /// <summary>How far through a catch, eased, for the sag to arrive on.</summary>
        static float Settle(float t, float from, float to)
        {
            float k = Mathf.Clamp01(Mathf.InverseLerp(from, to, t) * 3f);
            return k * k * (3f - 2f * k);
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
