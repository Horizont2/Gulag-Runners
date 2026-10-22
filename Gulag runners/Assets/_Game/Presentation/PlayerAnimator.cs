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

        /// <summary>
        /// The Animator that is actually driving something, searched over all of them.
        ///
        /// GetComponentInChildren alone takes the FIRST, and it searches the object
        /// itself before its children -- so one stray Animator on the fighter's root,
        /// with no controller on it, takes over and every clip in the game stops. That
        /// happened: a RequireComponent on a component dropped in the wrong place
        /// created exactly such an Animator, and the symptom was a body that moved
        /// perfectly and never animated.
        ///
        /// An Animator with no controller cannot play anything, so it is never the one
        /// meant. Preferring a controller makes the search say what it means.
        /// </summary>
        static Animator Rig(GameObject root)
        {
            Animator[] all = root.GetComponentsInChildren<Animator>(true);
            foreach (Animator a in all)
                if (a != null && a.runtimeAnimatorController != null) return a;
            return all.Length > 0 ? all[0] : null;
        }

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

        [Header("…and the same, with a weapon in hand")]
        [Tooltip("Carry the weapon while moving: a second set of locomotion states, used " +
                 "whenever the fighter is holding something.\n\n" +
                 "The pack's movement set is called One Hand Up, and that is exactly what " +
                 "it is: a blade up and a body leaned into it. On a man holding a warhammer " +
                 "that is right and it is most of what makes him read as dangerous. On a " +
                 "man holding NOTHING it is a raised arm with nothing in it and a stoop " +
                 "nobody asked for, which is how it was reported.\n\n" +
                 "So the stance follows the loadout rather than the clip set: bare hands " +
                 "get the neutral clips, a weapon gets these. Leave one empty and the " +
                 "unarmed state above is used for both, which is what happens today.")]
        public bool stanceFromWeapon = true;

        public string armedIdleState = "Idle Armed";
        public string armedWalkState = "Walk Armed";
        public string armedRunState = "Run Armed";
        [Tooltip("Backwards with a weapon. The pack only HAS backward clips in the armed " +
                 "set, so these two are the real ones and the unarmed pair above fall back " +
                 "to walking forwards.")]
        public string armedWalkBackState = "Walk Back";
        public string armedRunBackState = "Run Back";
        public string armedCrouchIdleState = "Crouch Idle Armed";
        public string armedCrouchMoveState = "Crouch Walk";
        public string armedAirState = "Jump Armed";
        public string armedDodgeState = "Sprint Armed";
        [Tooltip("The dodge dash. There is no sprint: the game has one ground speed.")]
        public string dodgeState = "Run Fast";
        [Tooltip("In the air, rising or falling.")]        public string airState = "Jumping Up";
        [Tooltip("Climbing up a ladder.")]      public string climbUpState = "Ladder Up";
        [Tooltip("Climbing down a ladder.")]    public string climbDownState = "Ladder Down";
        [Tooltip("Holding still on a ladder.")] public string climbIdleState = "Ladder Idle";
        [Tooltip("Climbing out at the top of a ladder.")] public string mantleState = "Ladder Out";

        [Tooltip("Getting off a ladder onto a floor that is NOT above you.\n\n" +
                 "A ladder whose head is flush with its platform leaves the body slightly " +
                 "above the floor it steps onto, so that climb-out descends — measured at " +
                 "9.8 cm down on this arena's top ladder — and the haul clip over it is a " +
                 "man pulling himself up while he sinks. Leave empty to use the haul " +
                 "anyway.")]
        public string mantleStepState = "";
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

        [Tooltip("How long the swing may keep playing after the simulation has finished " +
                 "with it, so the follow-through is seen.\n\n" +
                 "The swing is POSED off a tick counter, and a counter cannot land on a " +
                 "clip's last frame: that frame belongs to the instant the phase ends. " +
                 "Dropped there, the arms fade out of a pose the animation never resolved, " +
                 "which is the jerk at the end of a blow. So the clip is let off the leash " +
                 "and allowed to run out on its own before the layer fades.\n\n" +
                 "A cap, not a duration: it ends the moment the clip does. Zero switches " +
                 "it off.")]
        [Range(0f, 0.4f)] public float swingTail = 0.14f;

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
        [Tooltip("Drive the grab and the climb-out as a POSE across the move they " +
                 "illustrate, instead of playing them at a fixed speed.\n\n" +
                 "The same idea as Pose Attack By Phase, for the same reason. These four " +
                 "clips are long one-shots — stepping backwards onto a ladder runs 1.25 s " +
                 "and hauling out of one 0.83 s — while the scripted move they illustrate " +
                 "is 10 to 30 ticks, which is 0.17 s to 0.50 s and DIFFERENT EVERY TIME, " +
                 "because it is worked out from how far the body actually has to travel. " +
                 "No fixed playback speed can match a length that changes: whatever is " +
                 "typed below, the clip either runs out early and holds its last frame or " +
                 "is cut off part way and snaps. Both of those are the jerk.\n\n" +
                 "Posed, the clip starts on its first frame when the move starts and lands " +
                 "on its last frame as the body arrives, whatever either of them is long.")]
        public bool poseLadderByProgress = true;

        [Tooltip("Playback speed for those clips with the above OFF.")]
        [Range(0.5f, 4f)] public float ladderActionPlayback = 2f;

        [Tooltip("How long the step-off clip is held after letting go of the foot of a ladder, " +
                 "in seconds. Presentation only: the simulation has already handed control back, " +
                 "so moving at any point cuts it short.")]
        [Range(0f, 1.2f)] public float ladderExitHold = 0.22f;

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

        [Tooltip("The last few states, newest first, with the frames each was held for.\n\n" +
                 "Watch it while the thing that looks wrong happens. A clip that flickers " +
                 "shows up here as two names alternating with tiny frame counts beside " +
                 "them, which is the difference between knowing and guessing.")]
        public string stateTrail = "";

        int _heldFrames;

        [Tooltip("And on the fight layer, with its weight. Empty means the whole body is " +
                 "playing what is above.")]
        public string nowOnArms;

        int _currentHash;
        int _upperHash;
        float _upperWeight;

        // The follow-through, on each layer: which swing is still running itself out, and
        // how long it is still allowed to. See the Swing Tail tooltip.
        float _armsTail, _bodyTail;
        int _armsTailHash;
        string _bodyTailState;
        bool _warned;
        MoveMode _lastMode = MoveMode.Grounded;
        float _exitHold;

        void Reset()
        {
            player = GetComponent<PlayerController>();
            animator = Rig(gameObject);
        }

        void Awake()
        {
            if (player == null) player = GetComponent<PlayerController>();
            if (animator == null) animator = Rig(gameObject);

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
            // A climb-out owns the whole body. Fight() already declines to speak during one,
            // but the layer can still be carrying weight from the frame before, and a
            // half-faded pair of arms over a haul is one of the things that reads as the
            // clip changing its mind half way through.
            bool scripted = s.Mode == MoveMode.Mantling || s.Mode == MoveMode.Mounting;

            bool split = !scripted && splitUpperBody && upperLayer > 0 &&
                         upperLayer < animator.layerCount && !string.IsNullOrEmpty(arms) &&
                         (moving || !fullBodyWhenStill);

            // A NEW blow replays the reaction even when it is the same clip. Play skips a
            // state whose hash is already current, which is right for everything else and
            // wrong for exactly this: a string of hits kept the FIRST reaction, let it run
            // to its end and then held that frame, so being hit four times looked like
            // being hit once. Clearing the current hash forces the clip to re-enter.
            if (s.WasHit || s.WasParried) _currentHash = 0;

            string state = PickState(in s, speed, split, out float playback, out float scrub,
                                     out float fade);
            Play(state, playback, scrub, fade);

            Arms(split ? arms : null, armScrub);

            if (state != nowPlaying)
            {
                // What ACTUALLY changed, in order, with how long each one lasted. Read this
                // in the inspector while the thing that looks wrong is happening: a clip
                // that flickers shows up here as two names alternating with tiny frame
                // counts beside them, which is the difference between knowing and guessing.
                if (!string.IsNullOrEmpty(nowPlaying))
                    stateTrail = $"{nowPlaying}({_heldFrames}) <- {stateTrail}";
                if (stateTrail.Length > 160) stateTrail = stateTrail.Substring(0, 160);
                _heldFrames = 0;
            }
            _heldFrames++;

            // THE FREEZE, SHOWN. The simulation stops both fighters dead for a few frames
            // when a blow lands -- three on a hit, seven on a parry -- and that pause is
            // half of what gives a blow its weight (docs/20). Nothing was showing it: the
            // bodies stopped because the simulation stopped writing positions, while the
            // clips played straight on through, so a hit read as a man sliding to a halt
            // mid-swing rather than as an arm stopping against something.
            //
            // Set after Play, because Play writes the speed itself. A posed state is
            // already at zero and its scrub cannot move either -- the timers it reads do
            // not tick during a freeze.
            if (s.HitStopTimer > 0) animator.speed = 0f;

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
            if (s.Dead || s.StaggerTimer > 0 ||
                s.HitstunTimer > 0 || s.OpeningChest >= 0) return null;
            if (s.Mode != MoveMode.Grounded && s.Mode != MoveMode.Airborne) return null;

            // NOTHING. The arms are not a clip any more.
            //
            // docs/31 moved the weapon onto a stick: the arm points where the thumb
            // points, every tick, and PlayerArm drives the shoulder and elbow from the
            // simulation's own arm vector through IK. A swing clip played on top of that
            // would be an animation fighting the thing it is supposed to be showing, and
            // the player would watch their blade arrive somewhere they did not put it.
            //
            // The swing, block and combo clips stay in the controller and stay unwired.
            // They are what a scripted move would use if one ever comes back -- an
            // execution, a cutscene -- and deleting them would lose work for nothing.
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

            // The simulation has finished with the blow, but the clip has not. Hold the
            // layer where it is and stop posing it: with the animator back at speed 1 the
            // state runs itself out from wherever the last pose left it, which is the
            // follow-through that puts the arms back to neutral. Then it fades, out of a
            // pose the animation actually arrived at.
            if (string.IsNullOrEmpty(state) && _armsTail > 0f)
            {
                _armsTail -= Time.deltaTime;
                AnimatorStateInfo at = animator.GetCurrentAnimatorStateInfo(upperLayer);
                if (at.shortNameHash == _armsTailHash && at.normalizedTime < 1f)
                {
                    _upperWeight = 1f;
                    animator.SetLayerWeight(upperLayer, 1f);
                    return;
                }
                _armsTail = 0f;
            }

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
                _armsTail = swingTail;
                _armsTailHash = hash;
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
        /// The same move, in the stance the loadout calls for.
        ///
        /// The pack's movement set is called One Hand Up and is exactly that: a blade up
        /// and a body leaned into it. On a man carrying a warhammer it is right and it is
        /// most of what makes him read as dangerous. On a man carrying nothing it is a
        /// raised arm with nothing in it and a stoop nobody asked for — which is how it
        /// was reported, twice, and the answer is not to pick one of the two clip sets but
        /// to let the loadout say which.
        ///
        /// An empty armed name falls back to the unarmed state, so a half-filled set is
        /// the behaviour there has always been rather than a missing clip.
        /// </summary>
        string Stance(in PlayerSimState s, string unarmed, string armed)
        {
            if (!stanceFromWeapon || string.IsNullOrEmpty(armed)) return unarmed;
            return Armed(in s) ? armed : unarmed;
        }

        /// <summary>
        /// Is there anything in the hand? Fists are not a weapon for this purpose: the
        /// whole point of the stance is the thing being held.
        /// </summary>
        static bool Armed(in PlayerSimState s) =>
            s.Inventory.Weapon != ItemId.None && s.Inventory.Weapon != ItemId.Fists;

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

            if (s.StaggerTimer > 0)
                return First(staggerState, hitState) ?? idleState;

            if (s.HitstunTimer > 0)
            {
                // A hit the guard ate is not a hit the body took. docs/02 wants the difference
                // readable from across the arena: it is the whole reason to hold the guard up.
                string hurt = First(hitState, staggerState);
                if (!string.IsNullOrEmpty(hurt)) return hurt;
            }

            // No swing clip on the legs either, for the same reason: the arms are IK now
            // and the legs should keep walking, standing or falling while they move.

            // The blow is over for the simulation and the clip is still mid follow-through.
            // Left posed it would be abandoned at the frame the counter reached; crossfaded
            // away it would be abandoned faster. Handed back unposed, Play leaves it alone
            // -- the hash has not changed -- and it runs itself out at speed 1.
            //
            // Only while standing: on the move there are legs under this state with
            // somewhere to be, and the fight layer is carrying the swing anyway.
            if (_bodyTail > 0f && !string.IsNullOrEmpty(_bodyTailState))
            {
                _bodyTail -= Time.deltaTime;
                if (speed <= idleThreshold && _currentHash ==
                    Animator.StringToHash(_bodyTailState) &&
                    animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1f)
                    return _bodyTailState;
                _bodyTail = 0f;
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
                    return Stance(in s, dodgeState, armedDodgeState);

                case MoveMode.Mantling:
                    if (poseLadderByProgress) { scrub = ScriptProgress(in s); playback = 0f; }
                    else { playback = ladderActionPlayback; fade = ScriptedFade(in s); }
                    return !s.ScriptRises && !string.IsNullOrEmpty(mantleStepState)
                        ? mantleStepState
                        : mantleState;

                case MoveMode.Mounting:
                    if (poseLadderByProgress) { scrub = ScriptProgress(in s); playback = 0f; }
                    else { playback = ladderActionPlayback; fade = ScriptedFade(in s); }
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
                    return Stance(in s, airState, armedAirState);

                default:
                    // Just stepped off the bottom of a ladder and not moving yet: finish the
                    // dismount instead of popping straight into Idle.
                    // Cancelled by WALKING, and by nothing else.
                    //
                    // It used to be cancelled by crouching too, and that made it invisible:
                    // you climb down holding Down, and on the floor Down means crouch. The
                    // dismount clip was therefore allowed on exactly ONE tick -- measured,
                    // one of sixteen milliseconds -- and from the next the still-held key
                    // was a crouch and killed it. "The landing animation cannot be seen at
                    // all" was literally true. The crouch is not a new intention; it is the
                    // same press that was climbing a moment ago.
                    if (_exitHold > 0f && speed <= idleThreshold)
                    {
                        if (poseLadderByProgress)
                        {
                            // Posed across its own window, so the whole clip is shown
                            // rather than its first frames under a cross-fade.
                            scrub = 1f - Mathf.Clamp01(_exitHold / Mathf.Max(0.01f, ladderExitHold));
                            playback = 0f;
                            fade = 0f;
                        }
                        else playback = ladderActionPlayback;

                        return ladderExitState;
                    }

                    if (s.Crouching)
                    {
                        if (speed <= idleThreshold)
                            return Stance(in s, crouchIdleState, armedCrouchIdleState);
                        playback = Scale(speed, referenceWalkSpeed);
                        return Stance(in s, crouchMoveState, armedCrouchMoveState);
                    }

                    // The raised-guard pose is gone with the guard button. What the
                    // opponent reads now is the weapon itself, held wherever the thumb is
                    // holding it -- which carries more than a pose ever did, because it
                    // is also literally where the blade will stop theirs.

                    if (speed <= idleThreshold)
                        return Stance(in s, idleState, armedIdleState);

                    // Going one way with the blade pointed the other is running BACKWARDS,
                    // and there is a clip for it. This is the picture the aim/travel split
                    // exists to draw: a fighter backing out of range without turning his
                    // guard away from the man he is backing away from.
                    if (Backwards(in s))
                    {
                        string back = speed >= runThreshold
                            ? Stance(in s, runBackState, armedRunBackState)
                            : Stance(in s, walkBackState, armedWalkBackState);

                        if (!string.IsNullOrEmpty(back))
                        {
                            playback = Scale(speed, speed >= runThreshold
                                ? referenceRunSpeed : referenceWalkSpeed);
                            return back;
                        }
                    }

                    if (speed >= runThreshold)
                    {
                        playback = Scale(speed, referenceRunSpeed);
                        return Stance(in s, runState, armedRunState);
                    }

                    playback = Scale(speed, referenceWalkSpeed);
                    return Stance(in s, walkState, armedWalkState);
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
        /// How far through a scripted move the body is, 0 to 1.
        ///
        /// The simulation counts it down in ticks and knows exactly how many it will take,
        /// which is the only honest clock for the clip: the move's length is computed from
        /// the distance the body has to cover, so it is a different number on every ladder
        /// and at every height.
        /// </summary>
        float ScriptProgress(in PlayerSimState s)
        {
            if (s.ScriptFrames <= 0) return 0f;

            // Through the render alpha, not off the tick counter. The body is drawn
            // interpolated between ticks; a pose that is not steps at 60 Hz beside it, and
            // a climb-out is TEN ticks long, so that is a tenth of the clip at a time
            // against a body moving smoothly. Which is the jerk, exactly.
            //
            // And ONE TICK BEHIND the counter, because the body is too. The controller
            // draws between the PREVIOUS tick's position and this one, so at alpha 0 it is
            // showing where the body was a tick ago. Reading the counter straight put the
            // pose a full tick in front of the body for the whole move -- measured at a
            // constant 1/30 of the clip on this arena's top ladder, which is the hands
            // arriving on the ledge before the body does.
            float done = s.ScriptFrames - s.ScriptTimer - 1 + player.Alpha;
            return Mathf.Clamp01(done / s.ScriptFrames);
        }

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
