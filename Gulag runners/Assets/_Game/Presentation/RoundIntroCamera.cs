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
    /// That is one of two shots, and which one an arena gets is a fact about the arena rather
    /// than a preference — see <see cref="IntroStyle"/>. The whole shot above is built on
    /// there being a cage door to be hauled up, and the Nordic arena has no cages in it, so
    /// it gets the crane instead.
    ///
    /// It runs after SideViewCamera has placed itself and moves that result, so every bit of
    /// framing, dead zone and smoothing underneath keeps working and there is one camera rather
    /// than two fighting over the same transform.
    /// </summary>
    [DefaultExecutionOrder(110)]      // after SideViewCamera (100)
    [DisallowMultipleComponent]
    public sealed class RoundIntroCamera : MonoBehaviour
    {
        /// <summary>
        /// Which opening this arena gets. The two are not settings of one shot, they are
        /// two shots, and which one is right is a fact about the LOCATION.
        /// </summary>
        public enum IntroStyle
        {
            /// <summary>
            /// The cage door, close and shoved twice. Everything above describes it, and
            /// all of it depends on there being a cage: the jerks are somebody hauling a
            /// grille up in front of you, and in an arena with no grille they are a
            /// camera twitching at nothing.
            /// </summary>
            CageDoor = 0,

            /// <summary>
            /// A crane, high and pitched down, booming down to the playing framing.
            ///
            /// For a location that opens onto itself rather than out of a box -- the
            /// Nordic arena has no cages in it at all, so the cage shot's two shoves
            /// there are a camera flinching at nothing, which is exactly what they look
            /// like.
            ///
            /// It swings about the fighter rather than lifting away from them, so it
            /// keeps them the subject and the frame the width it is in play; what comes
            /// into shot is the ground you are standing on and the way up from it.
            /// Whether the far end is in frame is a question about the arena's size
            /// rather than about this shot, and intro_check.py is what answers it.
            /// </summary>
            Crane = 1,

            /// <summary>
            /// A descent down the shaft: the camera starts high over the fighter,
            /// looking down, and is lowered onto the playing framing.
            ///
            /// For a location you arrive at from ABOVE. Where the crane swings round
            /// the fighter at a constant distance, this comes straight down and the
            /// distance shrinks with it, so the fighter grows from a figure far below
            /// into the one you are about to play -- which is what being lowered into a
            /// mine looks like from inside the cage.
            ///
            /// It starts the furthest from the fight of the three, so it is also the one
            /// most able to show the other runner. The height is the number that decides
            /// that, and intro_check.py is what answers it for a given arena.
            /// </summary>
            Shaft = 2,
        }

        [Header("Wiring")]
        [Tooltip("Which opening. CageDoor is the close, shoved shot that needs a cage to " +
                 "be shoving; Crane is a descent from above for an arena that has none.")]
        public IntroStyle style = IntroStyle.CageDoor;

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
                 "a share of the opening distance.\n\n" +
                 "It runs in the gap between Settled By and the end of the phase, so it both " +
                 "starts and FINISHES on the mark. That matters more than it sounds: this " +
                 "overshoot used to be hung off the same clamped progress as the pull-back, " +
                 "which meant it reached its peak and stayed there — the camera sat 57 cm " +
                 "off the playing framing for the rest of the phase and then snapped onto " +
                 "it, in one frame, exactly as the fighters got control. Leave Settled By " +
                 "some room or there is no release, just an arrival.\n\n" +
                 "CageDoor only. A share of a five-metre opening is a two-thirds-of-a-metre " +
                 "move, and in the 0.3 s it has that is 7 m/s — fine as part of a shot that " +
                 "is already being shoved about, and the single most violent thing in the " +
                 "shot otherwise. The crane does not need it: smootherstep arrives with no " +
                 "speed AND no acceleration, which is a settle. A jib on hydraulics does " +
                 "not bounce.")]
        [Range(0f, 0.4f)] public float release = 0.12f;

        [Header("The crane (Style: Crane only)")]
        [Tooltip("How far over the top the shot starts, in degrees.\n\n" +
                 "It is an ANGLE and not a height, because the shot is a boom: the camera " +
                 "swings up over the thing it is already looking at and keeps its distance " +
                 "from it. That is what a crane arm does, and it is what keeps the FIGHTER " +
                 "the subject — they stay the size they are in play the whole way down, and " +
                 "the frame never widens to take in more of the room than the game does.\n\n" +
                 "It is not what hides the other runner, and it is worth being exact about " +
                 "that rather than reassured by it. The spawns are 16.6 m apart in the " +
                 "Nordic arena and the frame is 9.7 m across, so nothing in this shot comes " +
                 "close to reaching them: intro_check.py sweeps this angle from 5 to 80 " +
                 "degrees, and a lifted-and-pulled-back shot from 8 to 42 as well, and the " +
                 "opponent is out of frame in every one. The rule is kept by the arena's own " +
                 "size here. The check is what will say so in an arena where it is not.")]
        [Range(5f, 80f)] public float craneAngle = 42f;

        [Tooltip("A beat at the top before the descent starts, as a share of the cage phase. " +
                 "Long enough to read where you are, short enough that it is not a pause.")]
        [Range(0f, 0.6f)] public float craneHold = 0.14f;

        [Tooltip("How far along the arena the shot starts, in metres, measured BEHIND the " +
                 "fighter — so the descent drifts forward past them into the wing they are " +
                 "about to search, and never the other way, which is where the other runner " +
                 "is. Small: this is a hint of parallax, not a dolly.")]
        public float craneLead = 1.4f;

        [Header("The shaft (Style: Shaft only)")]
        [Tooltip("How far above the fighter the descent starts, in metres.\n\n" +
                 "This is the one number in the whole component that can give the match " +
                 "away: looking down from high enough takes in the far end of the arena " +
                 "and the other runner with it. Raise it and check, do not raise it and " +
                 "hope — intro_check.py walks the whole shot and says whether they are in " +
                 "frame.")]
        [Range(2f, 40f)] public float shaftHeight = 13f;

        [Tooltip("How far back from straight overhead it starts, in metres.\n\n" +
                 "Never zero. Directly above is a degenerate look-at — the camera has no " +
                 "way to know which way up it is — and it also loses the fighter's own " +
                 "silhouette, which is the thing the shot is arriving at. A couple of " +
                 "metres is enough to keep them side-on and the floor readable.")]
        public float shaftBack = 2.5f;

        [Tooltip("A beat at the top before the descent, as a share of the cage phase.")]
        [Range(0f, 0.6f)] public float shaftHold = 0.1f;

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
            bool crane = style == IntroStyle.Crane;
            bool shaft = style == IntroStyle.Shaft;
            bool eased = crane || shaft;      // both arrive under their own weight

            // 1 while the opening frame holds, easing to 0 by the time the phase is up — and
            // overshooting a little past the end, so the camera is RELEASED rather than
            // parked. A shot that arrives exactly on its mark reads as a camera; one that
            // goes a hair too far and comes back reads as somebody letting go of a breath.
            //
            // The crane eases on smootherstep rather than smoothstep. Smoothstep leaves the
            // ACCELERATION discontinuous at both ends: the camera is standing still and then
            // is pulling a tenth of a g, which on a two-second move you see as a flinch at
            // the start and a clip at the finish. Smootherstep is zero in the second
            // derivative as well, so the boom takes up its own weight. It costs one multiply.
            float k = Mathf.InverseLerp(shaft ? shaftHold : crane ? craneHold : holdUntil,
                                        settledBy, t);
            float close = 1f - (eased ? Smoother(k) : Smooth(k));

            // The release: out past the mark and back onto it, inside the gap between
            // settling and the end of the phase. Squared so the bump leaves and returns
            // with no speed of its own — a plain half-sine steps the camera's velocity
            // at both ends, which is the one thing an easing curve is for avoiding.
            //
            // Measured rather than reasoned: this used to hang off k, which Mathf
            // clamps, so it peaked and STAYED, parking the camera 57 cm off the playing
            // framing until the phase ended and then snapping onto it in a single frame
            // — on the frame the fighters got control, in both arenas.
            float r = eased ? 0f : Mathf.InverseLerp(settledBy, 1f, t);
            if (r > 0f)
            {
                float bump = Mathf.Sin(r * Mathf.PI);
                close -= release * bump * bump;
            }

            if (Mathf.Abs(close) <= 0.001f && (eased || jerkPush <= 0f)) return;

            Transform cam = view.transform;
            Vector3 at = cam.position;
            Quaternion facing_ = cam.rotation;

            if (crane)
            {
                // What the shot booms about: the thing the camera is already looking at.
                // Everything else is derived from it, so the descent lands exactly on the
                // playing framing however SideViewCamera has chosen to frame this frame.
                Vector3 forward = cam.forward;
                Vector3 focus = FocusPoint(at, forward);
                Vector3 arm = at - focus;

                // No player to orbit — a point down the sightline stands in for one, so the
                // shot still reads; it is just not centred on anybody.
                if (arm.sqrMagnitude < 4f) { arm = -forward * 8f; focus = at - arm; }

                // The axis the arm swings on: horizontal, across the sightline. Taken as
                // arm × up rather than the other way round so that positive degrees raise
                // the camera from whichever side of the fight plane it stands on.
                Vector3 axis = Vector3.Cross(arm, Vector3.up);
                axis = axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.right;

                at = focus + Quaternion.AngleAxis(craneAngle * close, axis) * arm;

                // And a drift along the arena, behind the fighter, so the boom comes down
                // THROUGH an angle rather than straight down one line. Applied before the
                // aim is worked out, so the camera keeps looking at the fight across it.
                int facing = view.player != null ? view.player.State.Facing : 1;
                at += new Vector3(-craneLead * facing * close, 0f, 0f);

                // Aim re-derived from where the camera now is, then blended back to the
                // playing rotation by the share of the shot that is left. At close = 0 that
                // is exactly the playing rotation, which is what makes the hand-over
                // invisible: there is no frame where two cameras disagree.
                facing_ = Quaternion.Slerp(
                    facing_, Quaternion.LookRotation(focus - at, Vector3.up), close);
            }
            else if (shaft)
            {
                // Straight down onto the fight. The crane above keeps its distance and
                // swings; this one closes it, so the fighter grows into the frame rather
                // than turning in it.
                Vector3 forward = cam.forward;
                Vector3 focus = FocusPoint(at, forward);
                Vector3 arm = at - focus;
                if (arm.sqrMagnitude < 4f) { arm = -forward * 8f; focus = at - arm; }

                // Kept on the side of the plane the camera already plays from, so the
                // descent never crosses to the far side and shows the fight from behind.
                Vector3 flat = new Vector3(arm.x, 0f, arm.z);
                flat = flat.sqrMagnitude > 0.0001f
                    ? flat.normalized * shaftBack
                    : Vector3.back * shaftBack;

                Vector3 high = focus + Vector3.up * shaftHeight + flat;
                at = Vector3.Lerp(at, high, close);

                // Aimed from wherever it currently is, then blended back to the playing
                // rotation by what is left of the shot, so the hand-over has no frame
                // where two cameras disagree.
                facing_ = Quaternion.Slerp(
                    facing_, Quaternion.LookRotation(focus - at, Vector3.up), close);
            }
            else if (openingShot != null)
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
            //
            // Not on the crane, and this is the whole reason the style exists. A shove is a
            // camera answering a pull on a grille. In an arena with no grille there is
            // nothing pulling, so the same two shoves are a camera twitching at nothing —
            // which is exactly what they read as.
            if (!eased)
            {
                Vector3 push = facing_ * Vector3.forward;
                at += push * (jerkPush * Shove(t, jerkAt) * close);
                at += push * (jerkPush * 1.3f * Shove(t, jerkAt2) * close);
            }

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

        /// <summary>
        /// Smootherstep: flat in the first AND second derivative at both ends.
        ///
        /// The difference from <see cref="Smooth"/> is only visible on a long move, which is
        /// what a crane is. Smoothstep starts with zero speed but full acceleration, so a
        /// two-second descent begins with a flinch and ends with the camera being caught.
        /// </summary>
        static float Smoother(float k)
        {
            k = Mathf.Clamp01(k);
            return k * k * k * (k * (6f * k - 15f) + 10f);
        }
    }
}
