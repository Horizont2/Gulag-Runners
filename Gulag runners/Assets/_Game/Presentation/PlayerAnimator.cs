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
        public string blockIdleState = "Block Idle";
        [Tooltip("Guard up and walking. Empty falls back to the walk.")]
        public string blockMoveState = "";

        [Header("Searching a chest")]
        [Tooltip("Working at a chest: prying, forcing, rummaging. Held for exactly as long as " +
                 "the simulation says the chest is being opened, so the clip IS the progress " +
                 "bar — and the fighter is turned to face the chest while it plays, which the " +
                 "simulation does, so the hitbox and the picture agree.\n\n" +
                 "Empty falls back to the crouch-idle pose, which at least reads as someone " +
                 "stopped and busy rather than someone standing about.")]
        public string searchState = "";

        [Tooltip("Scale the search clip by how fast this fighter actually opens things, so " +
                 "prying a crate with a club looks quicker than forcing a safe bare-handed. " +
                 "Off plays it at its authored speed.")]
        public bool scaleSearchByPrySpeed = true;

        [Header("The fight")]
        [Tooltip("The swing. One clip is enough: with Pose Attack By Phase on, the wind-up " +
                 "shows its opening frames, the active window its middle and the recovery its " +
                 "end, so a single animation carries the whole commitment the fight is built " +
                 "on (docs/02 — the wind-up is the thing the opponent reads).")]
        public string attackState = "Attack";

        [Tooltip("The swings a combo cycles through, in order. The simulation already counts " +
                 "which blow of the chain this is, so three clips make a chain read as a " +
                 "chain rather than the same swing three times. Empty uses Attack State for " +
                 "all of them.")]
        public string[] attackCombo = { "Attack", "Attack 2", "Attack 3" };

        [Tooltip("Optional. A separate clip for the wind-up only. Empty uses the swing.")]
        public string attackWindupState = "";

        [Tooltip("Optional. A separate clip for the recovery only. Empty uses the swing.")]
        public string attackRecoverState = "";

        [Tooltip("Drive one attack clip as a pose across the three phases instead of letting " +
                 "it play at its own speed. The swing then always lands on the frame the " +
                 "simulation says it lands on, which is the only way the picture can be read " +
                 "as a warning.")]
        public bool poseAttackByPhase = true;

        [Tooltip("Taking a hit. Held for the hitstun the simulation gives, so a heavier hit " +
                 "visibly costs more.")]
        public string hitState = "Hit";

        [Tooltip("A hit absorbed on the guard. Empty falls back to the block pose, which still " +
                 "reads better than the hit clip: the point of blocking is that you did not " +
                 "take it.")]
        public string blockImpactState = "Block Hit";

        [Tooltip("Guard broken, or a parry taken: open, off-balance and about to be punished. " +
                 "Empty falls back to the hit clip.")]
        public string staggerState = "Stagger";

        [Tooltip("Dead. Held from the moment the simulation says so, and never left.")]
        public string deathState = "";

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

        /// <summary>The first of these that has actually been filled in.</summary>
        static string First(string a, string b = null, string c = null) =>
            !string.IsNullOrEmpty(a) ? a
            : !string.IsNullOrEmpty(b) ? b
            : c;

        string PickState(in PlayerSimState s, float speed, out float playback, out float scrub,
                         out float fade)
        {
            playback = 1f;
            scrub = -1f;
            fade = crossFade;

            // The fight comes first, and in this order, because every one of these overrides
            // whatever the body happens to be doing with its feet: a fighter hit out of the
            // air is reeling, not jumping, and a dead one is not walking anywhere.
            if (s.Dead)
                return First(deathState, staggerState, hitState) ?? idleState;

            if (s.StaggerTimer > 0 || s.GuardBreakTimer > 0)
                return First(staggerState, hitState) ?? idleState;

            if (s.HitstunTimer > 0)
            {
                // A hit the guard ate is not a hit the body took. docs/02 wants the difference
                // readable from across the arena: it is the whole reason to hold the guard up.
                string hurt = s.Blocking
                    ? First(blockImpactState, blockIdleState, hitState)
                    : First(hitState, staggerState);
                if (!string.IsNullOrEmpty(hurt)) return hurt;
            }

            if (s.Attack != AttackPhase.None)
            {
                string chained = ComboSwing(s.ComboIndex);
                string swing = s.Attack == AttackPhase.Windup
                        ? First(attackWindupState, chained)
                        : s.Attack == AttackPhase.Recovery
                            ? First(attackRecoverState, chained)
                            : chained;

                if (!string.IsNullOrEmpty(swing))
                {
                    // Posed, the one clip spans the whole swing: the wind-up over its opening
                    // frames, the active window across its middle, the recovery to the end.
                    // That puts the frame the hitbox goes live on at the same place in the
                    // animation every time, which is what makes a wind-up readable at all.
                    if (poseAttackByPhase && swing == chained)
                    {
                        scrub = SwingProgress(in s);
                        playback = 0f;
                    }
                    return swing;
                }
            }

            // Working at a chest. The simulation has already turned the fighter to face it.
            if (s.OpeningChest >= 0)
            {
                string search = First(searchState, crouchIdleState, idleState);
                if (!string.IsNullOrEmpty(search))
                {
                    if (scaleSearchByPrySpeed)
                        playback = Scale(PrySpeed(in s), 1f);
                    return search;
                }
            }

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

        /// <summary>Which swing of a chain this is. Falls back to the single attack clip.</summary>
        string ComboSwing(int comboIndex)
        {
            if (attackCombo == null || attackCombo.Length == 0) return attackState;

            string pick = attackCombo[((comboIndex % attackCombo.Length) + attackCombo.Length)
                                      % attackCombo.Length];
            return First(pick, attackState);
        }

        /// <summary>
        /// How far through the whole swing this frame is, 0 to 1. The three phases get the
        /// shares a swing reads with: the wind-up the first two fifths, the active window a
        /// fifth, the recovery the rest.
        /// </summary>
        static float SwingProgress(in PlayerSimState s)
        {
            int total = Mathf.Max(1, s.AttackPhaseFrames);
            float within = Mathf.Clamp01(1f - s.AttackTimer / (float)total);

            switch (s.Attack)
            {
                case AttackPhase.Windup: return within * 0.4f;
                case AttackPhase.Active: return 0.4f + within * 0.2f;
                default: return 0.6f + within * 0.4f;
            }
        }

        /// <summary>
        /// How fast this fighter opens things, against bare hands. The weapon is the crowbar
        /// (docs/03), so a club through a crate should not look like fingernails on a safe.
        /// </summary>
        static float PrySpeed(in PlayerSimState s)
        {
            float pry = s.Inventory.WeaponDef.PrySpeed.ToMilli() / 1000f;
            return pry > 0.01f ? pry : 1f;
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
