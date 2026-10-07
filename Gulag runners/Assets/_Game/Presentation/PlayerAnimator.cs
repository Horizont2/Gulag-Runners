using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// Drives a character's Animator from the simulation state.
    ///
    /// The simulation owns the movement; this only picks a clip to show it with. Nothing here
    /// may feed back into <see cref="PlayerMotor"/> — root motion in particular is switched off,
    /// because an animation moving the character would desync the two clients instantly (docs/06).
    ///
    /// It drives the controller by state name rather than by parameters, because the free
    /// Stickman controller that ships with the pack has no parameters at all — only eight
    /// standalone states. That also means no Animator Controller has to be authored by hand, and
    /// swapping in a different character is a matter of retyping the state names below.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerAnimator : MonoBehaviour
    {
        [Header("Wiring")]
        public PlayerController player;
        public Animator animator;

        [Header("State names in the Animator controller")]
        [Tooltip("Standing still.")]                       public string idleState = "Idle";
        [Tooltip("Moving slowly on the ground.")]          public string walkState = "Walk";
        [Tooltip("Running.")]                              public string runState = "Run";
        [Tooltip("The dodge dash. There is no sprint: the game has one ground speed.")]
        public string dodgeState = "Run Fast";
        [Tooltip("In the air, rising or falling.")]        public string airState = "Jumping Up";
        [Tooltip("Climbing up a ladder.")]      public string climbUpState = "Ladder Up";
        [Tooltip("Climbing down a ladder.")]    public string climbDownState = "Ladder Down";
        [Tooltip("Holding still on a ladder.")] public string climbIdleState = "Ladder Idle";
        [Tooltip("Climbing out at the top of a ladder.")] public string mantleState = "Ladder Out";
        [Tooltip("Reaching for a ladder from below, to climb up it.")]
        public string mountUpState = "Ladder Up Start";
        [Tooltip("Stepping backwards onto a ladder from above, to climb down it.")]
        public string mountDownState = "Ladder Down Start";
        [Tooltip("Stepping off the foot of a ladder onto the floor.")]
        public string ladderExitState = "Ladder Down End";
        [Tooltip("Crouching and still.")]                  public string crouchIdleState = "Sitting";
        [Tooltip("Crouch-walking.")]                       public string crouchMoveState = "Walk";

        [Tooltip("Guard up and standing. Leave empty until there is a clip for it — the state " +
                 "then falls back to idle, and the only thing the player can see of a raised " +
                 "guard is that they have gone slow, which reads as a bug rather than a brace.")]
        public string blockIdleState = "";
        [Tooltip("Guard up and walking. Empty falls back to the walk.")]
        public string blockMoveState = "";

        [Header("Blending")]
        [Tooltip("Cross-fade time between states, in seconds.")]
        [Range(0f, 0.5f)] public float crossFade = 0.12f;

        [Tooltip("Below this speed the character counts as standing still, in m/s.")]
        public float idleThreshold = 0.15f;

        [Tooltip("Above this speed it plays Run instead of Walk, in m/s.")]
        public float runThreshold = 2.4f;

        [Header("Jump")]
        [Tooltip("Drive the jump clip as a pose instead of looping it: rising shows its early " +
                 "frames, the apex its middle, falling its end. One clip then reads as a whole " +
                 "jump arc, which is what the pack otherwise lacks.")]
        public bool poseJumpByVelocity = true;

        [Header("Ladder transitions")]
        [Tooltip("The ladder start and end clips are long one-shots — one of them runs two and a " +
                 "half seconds. The grab and the climb-out they illustrate take a quarter of a " +
                 "second, so they are played faster than authored or only their opening frames " +
                 "would ever be seen.")]
        [Range(0.5f, 4f)] public float ladderActionPlayback = 2f;

        [Tooltip("How long the step-off clip is held after letting go of the foot of a ladder, " +
                 "in seconds. Presentation only: the simulation has already handed control back, " +
                 "so moving at any point cuts it short.")]
        [Range(0f, 0.6f)] public float ladderExitHold = 0.22f;

        [Header("Playback speed")]
        [Tooltip("The ground speed the walk and run clips were authored at. Playback is scaled " +
                 "by the real speed against this, so the feet do not skate.")]
        public float referenceWalkSpeed = 1.6f;
        public float referenceRunSpeed = 3.6f;
        public float referenceClimbSpeed = 2.0f;
        [Range(0.1f, 1f)] public float minPlaybackSpeed = 0.4f;
        [Range(1f, 3f)] public float maxPlaybackSpeed = 1.8f;

        int _currentHash;
        bool _warned;
        MoveMode _lastMode = MoveMode.Grounded;
        float _exitHold;

        void Reset()
        {
            player = GetComponent<PlayerController>();
            animator = GetComponentInChildren<Animator>();
        }

        void Awake()
        {
            if (player == null) player = GetComponent<PlayerController>();
            if (animator == null) animator = GetComponentInChildren<Animator>();

            if (player == null || animator == null)
            {
                Debug.LogError($"{name}: PlayerAnimator needs both a PlayerController and an " +
                               "Animator. Assign them in the inspector.", this);
                enabled = false;
                return;
            }

            // The simulation owns the position. Root motion would move the character behind the
            // simulation's back and desync the two clients.
            animator.applyRootMotion = false;

            // The pack ships with CullUpdateTransforms, which stops animating whatever a camera
            // cannot see. With two cameras each showing one player, that is a character frozen
            // mid-stride on the other half of the screen.
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // Squashing a rigged character looks wrong; the crouch clip conveys it instead.
            player.squashOnCrouch = false;
        }

        void Update()
        {
            if (player == null || animator == null) return;

            PlayerSimState s = player.State;
            float speed = Mathf.Abs(s.Velocity.X.Raw / (float)Fix.RawOne);

            TrackLadderExit(in s, Time.deltaTime);

            string state = PickState(in s, speed, out float playback, out float scrub,
                                     out float fade);
            Play(state, playback, scrub, fade);
        }

        /// <summary>
        /// Notices the moment the simulation lets go of the foot of a ladder and keeps the
        /// step-off clip on screen for a beat afterwards. The climb-out at the top needs none of
        /// this — it is a state of its own and lasts exactly as long as its clip is shown.
        /// </summary>
        void TrackLadderExit(in PlayerSimState s, float dt)
        {
            if (s.Mode == MoveMode.Grounded && _lastMode == MoveMode.Climbing)
                _exitHold = ladderExitHold;
            else if (s.Mode != MoveMode.Grounded)
                _exitHold = 0f;
            else if (_exitHold > 0f)
                _exitHold -= dt;

            _lastMode = s.Mode;
        }

        string PickState(in PlayerSimState s, float speed, out float playback, out float scrub,
                         out float fade)
        {
            playback = 1f;
            scrub = -1f;
            fade = crossFade;

            switch (s.Mode)
            {
                case MoveMode.Climbing:
                {
                    float vy = s.Velocity.Y.Raw / (float)Fix.RawOne;
                    // A real hold pose beats freezing a climb clip mid-reach.
                    if (Mathf.Abs(vy) <= 0.05f) return climbIdleState;

                    playback = Scale(Mathf.Abs(vy), referenceClimbSpeed);
                    return vy > 0f ? climbUpState : climbDownState;
                }

                case MoveMode.Dodging:
                    playback = maxPlaybackSpeed;
                    return dodgeState;

                case MoveMode.Mantling:
                    playback = ladderActionPlayback;
                    fade = ScriptedFade(in s);
                    return mantleState;

                case MoveMode.Mounting:
                    playback = ladderActionPlayback;
                    fade = ScriptedFade(in s);
                    return s.ScriptDir >= 0 ? mountUpState : mountDownState;

                case MoveMode.Airborne:
                    if (poseJumpByVelocity)
                    {
                        float vy = s.Velocity.Y.Raw / (float)Fix.RawOne;
                        float top = Mathf.Max(0.1f, player.tuning.jumpSpeed);
                        float bottom = -Mathf.Max(0.1f, player.tuning.maxFallSpeed);
                        scrub = Mathf.Clamp01(Mathf.InverseLerp(top, bottom, vy));
                        playback = 0f;
                    }
                    return airState;

                default:
                    // Just stepped off the bottom of a ladder and not moving yet: finish the
                    // dismount instead of popping straight into Idle.
                    if (_exitHold > 0f && speed <= idleThreshold && !s.Crouching)
                    {
                        playback = ladderActionPlayback;
                        return ladderExitState;
                    }

                    if (s.Crouching)
                    {
                        if (speed <= idleThreshold) return crouchIdleState;
                        playback = Scale(speed, referenceWalkSpeed);
                        return crouchMoveState;
                    }

                    // A raised guard is a pose, and it has to be one the opponent can read:
                    // it is the difference between walking into a fighter and walking into a
                    // parry (docs/02 wants the silhouette to carry the information).
                    if (s.Blocking)
                    {
                        if (speed <= idleThreshold && !string.IsNullOrEmpty(blockIdleState))
                            return blockIdleState;
                        if (speed > idleThreshold && !string.IsNullOrEmpty(blockMoveState))
                        {
                            playback = Scale(speed, referenceWalkSpeed);
                            return blockMoveState;
                        }
                    }

                    if (speed <= idleThreshold) return idleState;

                    if (speed >= runThreshold)
                    {
                        playback = Scale(speed, referenceRunSpeed);
                        return runState;
                    }

                    playback = Scale(speed, referenceWalkSpeed);
                    return walkState;
            }
        }

        float Scale(float speed, float reference) =>
            reference <= 0.01f ? 1f : Mathf.Clamp(speed / reference, minPlaybackSpeed, maxPlaybackSpeed);

        /// <summary>
        /// A cross-fade short enough that a scripted move actually reaches its own clip. Fading for
        /// 0.12 s into a state that only lasts 0.10 s shows the fade and never the clip.
        /// </summary>
        float ScriptedFade(in PlayerSimState s)
        {
            float seconds = Mathf.Max(1, s.ScriptFrames) / (float)PlayerMotor.TicksPerSecond;
            return Mathf.Min(crossFade, seconds * 0.4f);
        }

        void Play(string stateName, float playback, float scrub, float fade)
        {
            if (string.IsNullOrEmpty(stateName)) return;

            int hash = Animator.StringToHash(stateName);

            if (!animator.HasState(0, hash))
            {
                if (!_warned)
                {
                    _warned = true;
                    Debug.LogWarning($"{name}: the Animator controller has no state called " +
                                     $"\"{stateName}\". Check the state names on this component " +
                                     "against the controller.", this);
                }
                return;
            }

            animator.speed = playback;

            // A posed state is re-driven every frame, so it never blends — correct for a jump,
            // which is an instant action and should not ease in.
            if (scrub >= 0f)
            {
                _currentHash = hash;
                animator.Play(hash, 0, scrub);
                return;
            }

            if (hash == _currentHash) return;
            _currentHash = hash;

            if (fade <= 0f) animator.Play(hash, 0, 0f);
            else animator.CrossFadeInFixedTime(hash, fade, 0);
        }
    }
}
