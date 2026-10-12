using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// How the round opens, through the camera.
    ///
    /// The one thing this may not do is show the other player. docs/01 builds the whole game on
    /// not seeing them — the first twenty seconds are a hunt in the dark whose only source of
    /// truth is sound — so the establishing sweep across the arena that every other fighting
    /// game opens with would give away, in two seconds, the thing the next twenty are about.
    ///
    /// So the shot is the opposite of a sweep. It starts CLOSE, on your own cage door, tight
    /// enough that the door is the whole frame and the arena is not in it at all. The door is
    /// hauled up in front of you. As it goes, the camera eases back to the framing the game is
    /// actually played at, and the first thing that comes into view is your own wing — the
    /// room you are about to search, and nothing past it.
    ///
    /// Which means the cinematic and the rule are the same move: the pull-back IS the reveal,
    /// and what it reveals is deliberately only half the arena.
    ///
    /// It runs after SideViewCamera has placed itself and moves that result, so every bit of
    /// framing, dead zone and smoothing underneath keeps working and there is one camera rather
    /// than two fighting over the same transform.
    /// </summary>
    [DefaultExecutionOrder(110)]      // after SideViewCamera (100)
    [DisallowMultipleComponent]
    public sealed class RoundIntroCamera : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("Leave empty to use the MatchState in the scene.")]
        public MatchState matchState;

        [Tooltip("The camera this moves. Leave empty to use the one on this object.")]
        public SideViewCamera view;

        [Header("The opening shot")]
        [Tooltip("How much closer the camera starts, as a share of its playing distance. " +
                 "0.45 means it opens at a little under half the distance: the door fills the " +
                 "frame and there is no arena in the shot to give anything away.")]
        [Range(0.2f, 1f)] public float startDistance = 0.45f;

        [Tooltip("How much higher or lower it starts, in metres. A touch low makes the door " +
                 "look like something being lifted over you.")]
        public float startHeight = -0.35f;

        [Tooltip("How far along the fighter's own facing the shot starts, in metres — towards " +
                 "the door rather than square at the body.")]
        public float startLead = 0.5f;

        [Header("The beats")]
        [Tooltip("The camera holds the close shot until here, as a share of the cage phase. " +
                 "It should outlast the door's first jerk, so the jerk happens in close-up.")]
        [Range(0f, 0.9f)] public float holdUntil = 0.42f;

        [Tooltip("And is back to the playing framing by here. Leaving a margin before the " +
                 "fighters can move means the first frame they control is a settled one.")]
        [Range(0.2f, 1f)] public float settledBy = 0.86f;

        [Header("The push on the jerk")]
        [Tooltip("A short shove towards the door as it is first tried, in metres. Not a shake " +
                 "— a shake on a phone reads as a dropped frame. This is one move.")]
        [Range(0f, 0.6f)] public float jerkPush = 0.22f;

        [Tooltip("When the shove happens, as a share of the cage phase. Match it to the door's " +
                 "Wait Until.")]
        [Range(0f, 0.9f)] public float jerkAt = 0.18f;

        [Tooltip("How long the shove takes to go out and come back, as a share of the phase.")]
        [Range(0.02f, 0.4f)] public float jerkFor = 0.12f;

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
            if (m.Phase != MatchPhase.Cages) return;      // the shot is over; hands off

            float t = m.CageProgress(matchState.MatchRules).Raw / (float)Fix.RawOne;

            // 1 while the close shot holds, easing to 0 by the time the doors are up.
            float close = 1f - Smooth(Mathf.InverseLerp(holdUntil, settledBy, t));
            if (close <= 0.001f && jerkPush <= 0f) return;

            Transform cam = view.transform;
            Vector3 at = cam.position;

            // Towards what the camera is looking at: closer along its own forward, which keeps
            // the pitch and the plane the camera already worked out.
            Vector3 toward = cam.forward;
            float distance = Vector3.Distance(at, FocusPoint(at, toward));

            at += toward * (distance * (1f - startDistance) * close);
            at += Vector3.up * (startHeight * close);

            // Lead towards the door, which is whichever way the fighter is facing.
            int facing = view.player != null ? view.player.State.Facing : 1;
            at += cam.right * 0f + new Vector3(startLead * facing * close, 0f, 0f);

            // And the shove, out and back, on the beat the door is first tried.
            float k = Mathf.InverseLerp(jerkAt, jerkAt + jerkFor, t);
            if (k > 0f && k < 1f)
                at += toward * (jerkPush * Mathf.Sin(k * Mathf.PI));

            cam.position = at;
        }

        /// <summary>
        /// Where the camera is looking, far enough out to measure against. The exact point does
        /// not matter — only that the close shot moves along the line the camera already has,
        /// so nothing about the framing underneath is contradicted.
        /// </summary>
        Vector3 FocusPoint(Vector3 at, Vector3 forward)
        {
            PlayerController p = view.player;
            if (p == null) return at + forward;

            Vector3 body = p.transform.position;
            return new Vector3(body.x, body.y, body.z);
        }

        static float Smooth(float k)
        {
            k = Mathf.Clamp01(k);
            return k * k * (3f - 2f * k);
        }
    }
}
