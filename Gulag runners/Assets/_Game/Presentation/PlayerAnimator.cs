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

        [Tooltip("Walking BACKWARDS: travelling one way with the blade pointed the other.\n\n" +
                 "This is the picture the aim/travel split exists to draw. A fighter backing " +
                 "out of range with his guard towards the man he is backing away from is the " +
                 "single most readable thing in a side-view fight, and playing the forward " +
                 "clip in reverse does not draw it — the weight goes the wrong way and the " +
                 "arms lead instead of trail. Empty falls back to the forward clip.")]
        public string walkBackState = "Walk Back";
        [Tooltip("Running backwards. Empty falls back to the forward run.")]
        public string runBackState = "Run Back";
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

        [Tooltip("Where in the attack clip the weapon actually connects, as a fraction of the " +
                 "clip.\n\n" +
                 "Posing only reads as a swing if the clip's own contact frame is the frame " +
                 "the hitbox goes live on. These were fixed at 0.40 and 0.60, which is right " +
                 "for a clip that winds up for its first four tenths — and wrong for every " +
                 "pack clip that does not. Wrong by a tenth is a fighter still drawing back " +
                 "while the hit lands, then snapping forward afterwards, which is exactly " +
                 "what a swing must never look like.\n\n" +
                 "Scrub the attack clip in the Animation window, find the frame the weapon is " +
                 "at the target, and put that fraction here.")]
        [Range(0f, 1f)] public float attackContactStart = 0.4f;

        [Tooltip("The end of the contact window, as a fraction of the clip. The stretch " +
                 "between it and Attack Contact Start is the clip's active frames; everything " +
                 "before is the wind-up and everything after is the follow-through.")]
        [Range(0f, 1f)] public float attackContactEnd = 0.6f;

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

        [Header("Upper body layer")]
        [Tooltip("Play the fight on a SECOND animator layer, masked to the upper body, so the " +
                 "legs keep running while the arms swing.\n\n" +
                 "One layer cannot show two things at once, and a side-view fighter has to: " +
                 "the feet are carrying him somewhere and the blade is going somewhere else. " +
                 "Switching off the lower body on the fight layer is how every game that " +
                 "lets you attack on the move does it, and it costs one mask and one weight.\n\n" +
                 "Off keeps the old behaviour — the swing takes the whole body and the " +
                 "locomotion underneath it is not seen. Tools > Gulag Runners > Build " +
                 "fighter animator creates the layer and its mask.")]
        public bool splitUpperBody = true;

        [Tooltip("Which layer the fight plays on. 0 is the base layer, which is the whole " +
                 "body; the tool builds the fight on layer 1.")]
        [Range(1, 4)] public int upperLayer = 1;

        [Tooltip("Seconds to fade the fight layer in and out. A layer that snaps to full " +
                 "weight is a pair of arms that teleport into a swing.")]
        [Range(0f, 0.4f)] public float upperFade = 0.08f;

        [Tooltip("Blend the fight into the WHOLE body while standing still.\n\n" +
                 "A masked swing is right while moving and thin while planted: standing " +
                 "still, the hips and legs are part of the blow and leaving them on an idle " +
                 "loop reads as a man waving his arms. So the mask applies in proportion to " +
                 "how fast the body is actually travelling, and a fighter who stops to swing " +
                 "gets the whole clip back.")]
        public bool fullBodyWhenStill = true;

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

        [Header("Debug")]
        [Tooltip("What is actually on screen, written every frame while playing.\n\n" +
                 "Here because the commonest animation fault in this project is not a bad " +
                 "clip but the WRONG clip, and from the outside those look identical. If a " +
                 "pose looks wrong, read the name off here and the hunt is over before it " +
                 "starts.")]
        public string nowPlaying;

        [Tooltip("And on the fight layer, with its weight. Empty means the whole body is " +
                 "playing what is above.")]
        public string nowOnArms;

        int _currentHash;
        int _upperHash;
        float _upperWeight;
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

            // The fight gets first refusal on its own layer. What it takes, the body below it
            // no longer has to draw — so the legs are free to keep running under a swing.
            string arms = Fight(in s, out float armScrub);
            // Standing still the swing takes the WHOLE body, and it should: a planted blow
            // is hips and shoulders, and a masked one over an idle loop is a man waving his
            // arms. The split is for the case it was built for — a body already going
            // somewhere, whose legs have something of their own to say.
            bool moving = speed > idleThreshold;
            bool split = splitUpperBody && upperLayer > 0 &&
                         upperLayer < animator.layerCount && !string.IsNullOrEmpty(arms) &&
                         (moving || !fullBodyWhenStill);

            string state = PickState(in s, speed, split, out float playback, out float scrub,
                                     out float fade);
            Play(state, playback, scrub, fade);

            Arms(split ? arms : null, armScrub);

            nowPlaying = state;
            nowOnArms = split && _upperWeight > 0.001f
                ? $"{arms}  ({_upperWeight:0.00})"
                : "";
        }

        /// <summary>
        /// The swing or the guard, as the upper body alone would show it — or null when the
        /// fight has nothing to say this frame and the body should be left alone.
        ///
        /// Only the states that are PURELY an arm action live here. A fighter who is reeling,
        /// staggered, dead, climbing or mid-dodge is doing that with all of him, and splitting
        /// those across two layers would draw half a man taking a hit.
        /// </summary>
        string Fight(in PlayerSimState s, out float scrub)
        {
            scrub = -1f;

            if (!splitUpperBody) return null;
            if (s.Dead || s.StaggerTimer > 0 || s.GuardBreakTimer > 0 ||
                s.HitstunTimer > 0 || s.OpeningChest >= 0) return null;
            if (s.Mode != MoveMode.Grounded && s.Mode != MoveMode.Airborne) return null;

            if (s.Attack != AttackPhase.None)
            {
                string chained = ComboSwing(s.ComboIndex);
                string swing = s.Attack == AttackPhase.Windup
                        ? First(attackWindupState, chained)
                        : s.Attack == AttackPhase.Recovery
                            ? First(attackRecoverState, chained)
                            : chained;

                if (string.IsNullOrEmpty(swing)) return null;
                if (poseAttackByPhase && swing == chained) scrub = SwingProgress(in s);
                return swing;
            }

            if (s.Blocking) return First(blockIdleState);

            return null;
        }

        /// <summary>
        /// Drives the fight layer: its clip, and how much of the body it is allowed to take.
        ///
        /// The weight is the whole design. On the move it is the mask's business — legs to the
        /// locomotion, arms to the blade. Standing still there is nothing for the legs to do
        /// that is worth protecting, and a planted blow uses the hips, so the layer is given
        /// the body back in proportion to how still the fighter is. One number, and the
        /// difference between a swing that reads as a swing and one that reads as waving.
        /// </summary>
        void Arms(string state, float scrub)
        {
            if (upperLayer <= 0 || upperLayer >= animator.layerCount) return;

            float want = string.IsNullOrEmpty(state) ? 0f : 1f;
            _upperWeight = upperFade <= 0.001f
                ? want
                : Mathf.MoveTowards(_upperWeight, want, Time.deltaTime / upperFade);
            animator.SetLayerWeight(upperLayer, _upperWeight);

            if (string.IsNullOrEmpty(state)) { _upperHash = 0; return; }

            int hash = Animator.StringToHash(state);
            if (!animator.HasState(upperLayer, hash))
            {
                WarnMissing(state, upperLayer);
                return;
            }

            // Posed by re-driving it every frame rather than by animator.speed, which is one
            // number for the whole animator: freezing the swing that way would freeze the
            // legs running underneath it, which is the one thing this layer exists to avoid.
            if (scrub >= 0f)
            {
                _upperHash = hash;
                animator.Play(hash, upperLayer, scrub);
            }
            else if (hash != _upperHash)
            {
                _upperHash = hash;
                if (crossFade <= 0f) animator.Play(hash, upperLayer, 0f);
                else animator.CrossFadeInFixedTime(hash, crossFade, upperLayer);
            }

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

        /// <summary>
        /// Is the body travelling against the way it is aimed?
        ///
        /// Asked of the SIMULATION's two signs and not of the velocity, because the velocity
        /// still carries the last direction for a few frames after the stick turns round and
        /// the clip would flicker across the turn. Travel is what the player asked for.
        /// </summary>
        static bool Backwards(in PlayerSimState s) =>
            s.Travel != 0 && s.Facing != 0 && s.Travel != s.Facing;

        /// <summary>The first of these that has actually been filled in.</summary>
        static string First(string a, string b = null, string c = null) =>
            !string.IsNullOrEmpty(a) ? a
            : !string.IsNullOrEmpty(b) ? b
            : c;

        string PickState(in PlayerSimState s, float speed, bool armsBusy, out float playback,
                         out float scrub, out float fade)
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

            // Unless the upper-body layer has taken it. Two layers both playing the swing is
            // the swing applied twice, and the legs never get to run.
            if (s.Attack != AttackPhase.None && !armsBusy)
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
                    if (s.Blocking && !armsBusy)
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

                    // Going one way with the blade pointed the other is running BACKWARDS,
                    // and there is a clip for it. This is the picture the aim/travel split
                    // exists to draw: a fighter backing out of range without turning his
                    // guard away from the man he is backing away from.
                    if (Backwards(in s))
                    {
                        if (speed >= runThreshold && !string.IsNullOrEmpty(runBackState))
                        {
                            playback = Scale(speed, referenceRunSpeed);
                            return runBackState;
                        }
                        if (!string.IsNullOrEmpty(walkBackState))
                        {
                            playback = Scale(speed, referenceWalkSpeed);
                            return walkBackState;
                        }
                    }

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
        /// <summary>
        /// How far through the attack CLIP the swing is, given how far through the swing the
        /// simulation is.
        ///
        /// The three phases are stretched onto the clip around its own contact window, so the
        /// frame the hitbox goes live on is the frame the weapon is drawn at the target. The
        /// window is a pair of inspector fields rather than the 0.40/0.60 it used to be,
        /// because where a clip connects is a property of that clip and this project has
        /// already changed character twice.
        /// </summary>
        float SwingProgress(in PlayerSimState s)
        {
            int total = Mathf.Max(1, s.AttackPhaseFrames);
            float within = Mathf.Clamp01(1f - s.AttackTimer / (float)total);

            float hit = Mathf.Clamp01(attackContactStart);
            float done = Mathf.Clamp(attackContactEnd, hit, 1f);

            switch (s.Attack)
            {
                case AttackPhase.Windup: return within * hit;
                case AttackPhase.Active: return hit + within * (done - hit);
                default: return done + within * (1f - done);
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

        /// <summary>
        /// Says the one thing that is otherwise invisible: a state this component asks for
        /// does not exist, so NOTHING is played and the last pose is held.
        ///
        /// That is not a subtle failure -- a held pose is a character frozen mid-stride --
        /// but it looks exactly like a broken clip rather than a missing state, and it cost
        /// this project a long hunt through the IK for a jerk that was in fact seven ladder
        /// states the controller had never had. Tools > Gulag Runners > Build fighter
        /// animator builds them; this says which one is missing until it has been run.
        /// </summary>
        void WarnMissing(string stateName, int layer)
        {
            if (_warned) return;
            _warned = true;
            Debug.LogWarning(
                $"{name}: the Animator controller has no state called \"{stateName}\" on " +
                $"layer {layer}, so nothing is played and the last pose is held. Run " +
                "Tools > Gulag Runners > Build fighter animator, or correct the state names " +
                "on this component.", this);
        }

        void Play(string stateName, float playback, float scrub, float fade)
        {
            if (string.IsNullOrEmpty(stateName)) return;

            int hash = Animator.StringToHash(stateName);

            if (!animator.HasState(0, hash))
            {
                WarnMissing(stateName, 0);
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
