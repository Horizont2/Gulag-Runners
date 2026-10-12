using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// How the round ends, through the camera.
    ///
    /// docs/01 gives a match one life and then an "Again" straight after it, which means the
    /// end of a round is about three seconds long and has to land in that time with no UI at
    /// all. So it is told the way the opening is told: with a move, not a caption. The camera
    /// closes on whoever went down, drops to about their level, and leans — and then, as the
    /// next round deals, lets all of it go so the first frame of the cage shot is clean.
    ///
    /// The one restraint is the same one the intro has: no sweep. A pull-back over the whole
    /// arena at the end would show the loser exactly where everything was, and docs/01 is
    /// built on the arena being partly unknown. The end is a close shot for the same reason
    /// the start is.
    ///
    /// It runs after SideViewCamera has placed itself and moves that result, so the framing,
    /// dead zone and smoothing underneath keep working and there is one camera rather than two
    /// fighting over the same transform. Presentation only: it reads the clock and never
    /// writes to it (docs/06).
    /// </summary>
    [DefaultExecutionOrder(111)]      // after SideViewCamera (100) and the intro (110)
    [DisallowMultipleComponent]
    public sealed class RoundOutroCamera : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("Leave empty to use the MatchState in the scene.")]
        public MatchState matchState;

        [Tooltip("The camera this moves. Leave empty to use the one on this object.")]
        public SideViewCamera view;

        [Header("The closing shot")]
        [Tooltip("How far in the camera comes, as a share of its playing distance. 0.3 means " +
                 "it ends up about a third closer than the game is played at.")]
        [Range(0f, 0.8f)] public float pushIn = 0.3f;

        [Tooltip("How far it drops, in metres. Down to about the height of somebody on the " +
                 "floor, which is what the shot is about.")]
        public float drop = -0.45f;

        [Tooltip("How much of the way it slides towards the fallen fighter, 0 to 1. Not all " +
                 "the way: the framing underneath is still holding both bodies, and this is a " +
                 "lean towards one of them rather than a cut to it.")]
        [Range(0f, 1f)] public float leanToFallen = 0.35f;

        [Tooltip("How far the horizon tilts at the end of the move, in degrees. Small. A roll " +
                 "you can read as a roll is a camera; a roll you cannot is a mood.")]
        [Range(-8f, 8f)] public float rollDegrees = 2.5f;

        [Header("The beats, in seconds from the last blow")]
        [Tooltip("How long the move takes to arrive. Longer than a cut and shorter than a " +
                 "replay: the body is still falling while the camera is still closing.")]
        [Range(0.1f, 3f)] public float pushSeconds = 1.1f;

        [Tooltip("How long before the next round it starts letting go, so the cage shot does " +
                 "not begin with a snap back. Ignored when the result is held rather than " +
                 "restarted, since there is no next round to hand over to.")]
        [Range(0.1f, 3f)] public float releaseSeconds = 0.8f;

        void Reset()
        {
            view = GetComponent<SideViewCamera>();
            matchState = MatchState.Instance;
        }

        void Awake()
        {
            if (view == null) view = GetComponent<SideViewCamera>();
            if (matchState == null) matchState = MatchState.Instance;
        }

        void LateUpdate()
        {
            if (view == null) return;
            if (matchState == null) matchState = MatchState.Instance;
            if (matchState == null) return;

            MatchSimState m = matchState.Match;
            if (!m.Finished) return;                       // mid-fight; hands off

            float ease = Ease(in m);
            if (ease <= 0.001f) return;

            Transform cam = view.transform;
            Vector3 at = cam.position;

            // Along the line the camera already has, which keeps the pitch and the plane the
            // framing underneath worked out. Same move as the opening, in the other direction.
            Vector3 toward = cam.forward;
            float distance = Vector3.Distance(at, FocusPoint(at, toward));
            float fallenX = FallenX(at.x);

            at += toward * (distance * pushIn * ease);
            at += Vector3.up * (drop * ease);
            at += new Vector3((fallenX - at.x) * leanToFallen * ease, 0f, 0f);

            cam.position = at;

            if (rollDegrees != 0f)
                cam.rotation = Quaternion.AngleAxis(rollDegrees * ease, toward) * cam.rotation;
        }

        /// <summary>
        /// How far into the closing shot, 0 to 1: in over the first beat, out again as the
        /// next round comes up.
        ///
        /// Counted off the match's own tick rather than off Time, because the restart it hands
        /// over to is counted in ticks: driven from a clock of its own, the release would start
        /// drifting out of the handover it exists to smooth.
        /// </summary>
        float Ease(in MatchSimState m)
        {
            float since = m.OverFrame / (float)PlayerMotor.TicksPerSecond;
            float ease = Smooth(Mathf.InverseLerp(0f, Mathf.Max(0.01f, pushSeconds), since));

            int restart = matchState.MatchRules.RestartFrames;
            if (restart <= 0) return ease;                 // the result is held; stay in

            float total = restart / (float)PlayerMotor.TicksPerSecond;
            float letGo = Mathf.InverseLerp(total - releaseSeconds, total, since);
            return ease * (1f - Smooth(letGo));
        }

        /// <summary>
        /// What the camera is looking at, far enough out to measure the push against. Only the
        /// line matters, not the exact point: the move runs along the one the framing
        /// underneath already chose, so nothing about it is contradicted.
        /// </summary>
        Vector3 FocusPoint(Vector3 at, Vector3 forward)
        {
            PlayerController p = view.player;
            return p != null ? p.transform.position : at + forward;
        }

        /// <summary>
        /// Where the fighter who went down is. Winner 0 means fighter 1 fell, and the other way
        /// about; a draw has no one body to lean towards, so the shot stays where it is.
        /// </summary>
        float FallenX(float fallback)
        {
            if (matchState.fighters.Count < 2) return fallback;

            int loser = matchState.Match.Winner == 0 ? 1
                      : matchState.Match.Winner == 1 ? 0
                      : -1;
            if (loser < 0) return fallback;

            PlayerController p = matchState.fighters[loser];
            return p != null ? p.transform.position.x : fallback;
        }

        static float Smooth(float k)
        {
            k = Mathf.Clamp01(k);
            return k * k * (3f - 2f * k);
        }
    }
}
