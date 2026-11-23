using UnityEngine;

namespace GulagRunners.Game
{
    /// <summary>
    /// The body goes over when the fighter dies.
    ///
    /// Until now the round ended with nothing happening. PlayerAnimator asks for a
    /// deathState, that field is an empty string, and the fallback chain lands on the Hit
    /// clip -- so a dead fighter stood where he was, flinching, held at one frame forever.
    /// There is no death animation to point it at either: of the 74 clips in this project,
    /// not one is a fall.
    ///
    /// So the fall is made here instead of imported, and that suits the game better than a
    /// clip would. A clip plays the same fall every time; this one falls the way the body
    /// was standing, towards the side it was struck from, from wherever it happens to be --
    /// on the floor, on a ledge, halfway up a ladder.
    ///
    /// IT IS A PENDULUM, NOT AN EASE. The angle is integrated rather than looked up:
    ///
    ///     θ'' = GRAVITY * sin(θ)
    ///
    /// which is a rod toppling about its foot, and it is the whole reason this reads as a
    /// body falling rather than as an object being rotated. A rod barely moves while it is
    /// upright, because sin(0) is nothing, and then arrives all at once. An eased curve does
    /// the opposite -- fastest in the middle, slowest at both ends -- and a fall that slows
    /// down as it meets the floor is the single most obviously animated thing a body can do.
    /// The nudge below is what gets it off nothing, and is the same thing a real body's own
    /// weight does.
    ///
    /// NOTHING HERE TOUCHES THE SIMULATION. The fighter is already dead as far as the match
    /// is concerned -- MatchClock has read it, the round is being decided -- and this moves
    /// a visual transform and nothing else. It may be frame-rate dependent, it may be
    /// different on two machines, and neither matters: by the time it runs there is nothing
    /// left to agree about.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DeathFall : MonoBehaviour
    {
        [Tooltip("How hard the body is pulled over, in radians per second squared at a right "
                 + "angle. Bigger is a quicker, heavier drop.\n\n"
                 + "34 with a 7 degree nudge lands the body in 0.57 s: 15 degrees by 0.24, "
                 + "45 by 0.44, flat by 0.57 -- so the last quarter of the arc takes an "
                 + "eighth of a second, which is the slam. These two numbers were solved "
                 + "for, not guessed: the first pair (11 and 3.5) took over 1.2 s and spent "
                 + "its first half second moving four degrees, which read as a man thinking "
                 + "about it. The filmstrip in docs/refs/vfx/death.html is what showed it.")]
        [Range(2f, 80f)] public float gravity = 34f;

        [Tooltip("The push that starts it, in degrees. A rod exactly upright never falls -- "
                 + "sin(0) is zero and it would stand there forever -- so it begins already "
                 + "leaning this far. It does far more to the first half second than gravity "
                 + "does: at the same pull, 3.5 degrees spends 0.37 s getting to 15 and 7 "
                 + "spends 0.24.")]
        [Range(0.2f, 15f)] public float nudgeDegrees = 7f;

        [Tooltip("Degrees past flat the body carries before it settles back. This is the "
                 + "weight landing: without it the fall stops dead on ninety, which reads as "
                 + "an animation ending rather than as something hitting the ground.")]
        [Range(0f, 25f)] public float overshootDegrees = 9f;

        [Tooltip("How long the settle after the landing takes, in seconds.")]
        [Range(0.05f, 1f)] public float settleSeconds = 0.22f;

        [Tooltip("How much of the overshoot comes back on each bounce. Two bounces is all "
                 + "the eye reads at this scale.")]
        [Range(0f, 0.8f)] public float bounce = 0.35f;

        [Tooltip("How far the shoulders slide along the ground as the body goes over, in "
                 + "metres. Feet stay put; this is the body not being a perfectly rigid rod.")]
        [Range(0f, 0.6f)] public float slide = 0.18f;

        [Tooltip("The camera to shake when the body lands, if there is one to find.")]
        public SideViewCamera shaker;

        [Tooltip("How hard that landing shakes it, 0 to 1. A death is the end of the round, "
                 + "so it is allowed to be felt -- but it is a body, not an explosion.")]
        [Range(0f, 1f)] public float landingTrauma = 0.3f;

        /// <summary>Where in the fall we are, in radians. 0 is standing, pi/2 is flat.</summary>
        float _angle;
        float _rate;
        int _away = 1;
        bool _falling;
        bool _landed;
        float _settle;

        /// <summary>Has the body started going over.</summary>
        public bool Falling => _falling;

        /// <summary>
        /// Start the fall. <paramref name="away"/> is +1 to put the head towards +X and -1
        /// towards -X, which the caller works out from which way the man was facing.
        /// </summary>
        public void Begin(int away)
        {
            if (_falling) return;

            _away = away >= 0 ? 1 : -1;
            _angle = nudgeDegrees * Mathf.Deg2Rad;
            _rate = 0f;
            _falling = true;
            _landed = false;
            _settle = 0f;
        }

        /// <summary>Back to standing. Called when a round restarts the fighter.</summary>
        public void Clear()
        {
            _falling = false;
            _landed = false;
            _angle = 0f;
            _rate = 0f;
            _settle = 0f;
        }

        /// <summary>
        /// Carry the fall forward by one frame and hand back where the body should be.
        ///
        /// Integrated in SUBSTEPS rather than once per frame. The pendulum is at its fastest
        /// exactly where it matters -- the last few degrees before the floor -- and a single
        /// Euler step at 30 fps overshoots that badly enough to send the body through the
        /// ground and back out. Four steps costs nothing and the error stops mattering.
        /// </summary>
        public void Step(float dt, out Quaternion tilt, out Vector3 shift)
        {
            tilt = Quaternion.identity;
            shift = Vector3.zero;
            if (!_falling) return;

            float flat = Mathf.PI * 0.5f;

            if (!_landed)
            {
                const int Steps = 4;
                float bit = dt / Steps;
                for (int i = 0; i < Steps; i++)
                {
                    _rate += gravity * Mathf.Sin(_angle) * bit;
                    _angle += _rate * bit;

                    if (_angle >= flat)
                    {
                        _angle = flat;
                        _landed = true;
                        Land();
                        break;
                    }
                }
            }
            else if (_settle < settleSeconds)
            {
                _settle += dt;
                float t = Mathf.Clamp01(_settle / Mathf.Max(0.0001f, settleSeconds));

                // Two bounces, each smaller, dying to nothing. Cosine rather than a spring
                // because a spring needs a stiffness and a damping to tune and this needs
                // one number: how long it takes to stop.
                float swing = Mathf.Cos(t * Mathf.PI * 2f * 2f) * (1f - t) * (1f - t);
                _angle = flat + overshootDegrees * Mathf.Deg2Rad * bounce * swing;
            }
            else
            {
                _angle = flat;
            }

            float degrees = _angle * Mathf.Rad2Deg * _away;

            // About the parent's Z, which is the world's: the fight is a flat plane and a
            // body falls in it. Pre-multiplied by the caller, so the model's own yaw and any
            // ladder lean survive underneath.
            tilt = Quaternion.AngleAxis(degrees, Vector3.forward);

            // The shoulders slide as the body goes over, so it is not a plank hinged to the
            // floor. Along the fall, and only once it is past halfway, which is where a real
            // body's feet give.
            float past = Mathf.InverseLerp(flat * 0.5f, flat, _angle);
            shift = new Vector3(-_away * slide * past * past, 0f, 0f);
        }

        void Land()
        {
            if (shaker == null) shaker = FindFirstObjectByType<SideViewCamera>();
            if (shaker != null) shaker.AddTrauma(landingTrauma);
        }
    }
}
