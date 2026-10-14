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

        [Tooltip("Where the round opens from, placed by hand in the scene.\n\n" +
                 "This is the whole shot. With it set, the camera STARTS at this transform — " +
                 "position and rotation both — and is blended to the playing framing across " +
                 "the cage phase, which gives an arc with real parallax rather than a dolly " +
                 "in and out along one line. Without it the component falls back to offsetting " +
                 "the playing framing, which is what it used to do and which reads as exactly " +
                 "what it is: a zoom.\n\n" +
                 "Compose it like a photograph: low, close, the bars across the frame, the " +
                 "fighter behind them, and no arena in shot at all.")]
        public Transform openingShot;

        [Header("The opening shot")]
        [Tooltip("How much closer the camera starts, as a share of its playing distance. " +
                 "0.45 means it opens at a little under half the distance: the door fills the " +
                 "frame and there is no arena in the shot to give anything away.")]
        [Range(0.2f, 1f)] public float startDistance = 0.32f;

        [Tooltip("How much higher or lower it starts, in metres. A touch low makes the door " +
                 "look like something being lifted over you.")]
        public float startHeight = -0.35f;

        [Tooltip("How far along the fighter's own facing the shot starts, in metres.\n\n" +
                 "Zero, and deliberately. This led the shot towards the door back when the " +
                 "door was a slab at the side of the cage; the cage is now a grille standing " +
                 "AROUND the fighter, so leading along their facing points the camera out at " +
                 "the empty arena and pushes the bars off the edge of a shot this tight. " +
                 "Negative pulls the frame back into the cage.")]
        public float startLead;

        [Header("The beats")]
        [Tooltip("The camera holds the close shot until here, as a share of the cage phase. " +
                 "It should outlast the door's first jerk, so the jerk happens in close-up.")]
        [Range(0f, 0.9f)] public float holdUntil = 0.52f;

        [Tooltip("And is back to the playing framing by here. Leaving a margin before the " +
                 "fighters can move means the first frame they control is a settled one.")]
        [Range(0.2f, 1f)] public float settledBy = 0.86f;

        [Header("Held, not static")]
        [Tooltip("How much the opening frame drifts while it is held, in metres. Tiny. A frame " +
                 "that is perfectly still reads as a render; one that breathes reads as a " +
                 "camera somebody is holding.")]
        [Range(0f, 0.12f)] public float sway = 0.035f;

        [Range(0.05f, 1.5f)] public float swaySpeed = 0.45f;

        [Header("The push on the jerk")]
        [Tooltip("A short shove towards the door as it is first tried, in metres. Not a shake " +
                 "— a shake on a phone reads as a dropped frame. This is one move.")]
        [Range(0f, 0.6f)] public float jerkPush = 0.22f;

        [Tooltip("When the first shove happens, as a share of the cage phase. Match it to the " +
                 "door's Wait Until.")]
        [Range(0f, 0.9f)] public float jerkAt = 0.14f;

        [Tooltip("And the second, which should land on the door's Hold Until — the moment " +
                 "whoever is lifting it gets a grip and tries again. The camera has to answer " +
                 "both pulls or the second one reads as the door doing it by itself.")]
        [Range(0f, 0.95f)] public float jerkAt2 = 0.4f;

        [Tooltip("How long a shove takes to go out and come back, as a share of the phase.")]
        [Range(0.02f, 0.4f)] public float jerkFor = 0.1f;

        [Tooltip("How far past the playing framing the pull-back carries before settling, as " +
                 "a share of the opening distance.")]
        [Range(0f, 0.4f)] public float release = 0.12f;

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

            // 1 while the close shot holds, easing to 0 by the time the doors are up — and
            // overshooting a little past the end, so the camera is RELEASED rather than
            // parked. A shot that arrives exactly on its mark reads as a camera; one that
            // goes a hair too far and comes back reads as somebody letting go of a breath.
            float k = Mathf.InverseLerp(holdUntil, settledBy, t);
            float close = 1f - Smooth(k);
            if (k > 0f && k < 1.6f) close -= release * Mathf.Sin(Mathf.Clamp01(k / 1.6f) * Mathf.PI);
            if (close <= 0.001f && jerkPush <= 0f) return;

            Transform cam = view.transform;
            Vector3 at = cam.position;
            Quaternion facing_ = cam.rotation;

            if (openingShot != null)
            {
                // The authored shot, blended out. Rotation as well as position, which is the
                // whole difference: moving along one line is a zoom, and turning while you
                // move is a camera going somewhere.
                at = Vector3.Lerp(at, openingShot.position, close);
                facing_ = Quaternion.Slerp(facing_, openingShot.rotation, close);
            }
            else
            {
                Vector3 toward = cam.forward;
                float distance = Vector3.Distance(at, FocusPoint(at, toward));
                at += toward * (distance * (1f - startDistance) * close);
                at += Vector3.up * (startHeight * close);
                int facing = view.player != null ? view.player.State.Facing : 1;
                at += new Vector3(startLead * facing * close, 0f, 0f);
            }

            // Breathing, while the shot is held. Perlin rather than a sine, so it never
            // repeats on a beat you can hear.
            if (sway > 0f && close > 0.001f)
            {
                float w = Time.time * swaySpeed;
                at += (facing_ * new Vector3(Mathf.PerlinNoise(w, 0.3f) * 2f - 1f,
                                             Mathf.PerlinNoise(0.7f, w) * 2f - 1f, 0f))
                      * (sway * close);
            }

            // And the shoves, out and back, on the two beats the door is tried. The second is
            // the harder one: by then whoever is out there has taken the weight once and is
            // committing to it, and the camera flinching twice is what says somebody is DOING
            // this rather than a mechanism running its cycle.
            Vector3 push = facing_ * Vector3.forward;
            at += push * (jerkPush * Shove(t, jerkAt) * close);
            at += push * (jerkPush * 1.3f * Shove(t, jerkAt2) * close);

            cam.SetPositionAndRotation(at, facing_);
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

        /// <summary>One shove, out and back, measured from the beat it starts on.</summary>
        float Shove(float t, float at)
        {
            float k = Mathf.InverseLerp(at, at + jerkFor, t);
            return k > 0f && k < 1f ? Mathf.Sin(k * Mathf.PI) : 0f;
        }

        static float Smooth(float k)
        {
            k = Mathf.Clamp01(k);
            return k * k * (3f - 2f * k);
        }
    }
}
