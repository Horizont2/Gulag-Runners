using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// Puts the hands and feet where the world actually is.
    ///
    /// Every animation in this project is retargeted: the clips come off a 1.8 m rig and play
    /// on a character half that size, so a climb clip reaches for rungs that were where the
    /// OTHER rig's ladder stood. No amount of tuning the body's distance from the ladder
    /// fixes that, because the mismatch is in the pose, not in the placement — the fighter
    /// ends up climbing alongside a ladder he is supposedly holding.
    ///
    /// Inverse kinematics is the answer to exactly that: the animation says what the body is
    /// doing, and this says where four points of it must end up. Mecanim's humanoid IK is
    /// used rather than a hand-written solver, because both characters are Humanoid and the
    /// built-in one already respects the rig's own joint limits.
    ///
    /// PRESENTATION ONLY. Nothing here is read by the simulation, nothing here allocates, and
    /// the targets are computed from the same baked geometry the motor collides against — so
    /// two devices showing the same frame show the same hands, and a device that skipped this
    /// entirely would still play an identical match (docs/06).
    ///
    /// It must live on the same object as the Animator, because that is where Unity calls
    /// OnAnimatorIK, and the layer must have IK Pass ticked or the callback never comes.
    /// Tools > Gulag Runners > Set up IK does both.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    [DisallowMultipleComponent]
    public sealed class PlayerIK : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("Optional. Found on a parent when empty.")]
        public PlayerController player;

        [Header("Ladder grip")]
        [Tooltip("Pin the hands and feet to the rungs while climbing. This is the one that " +
                 "matters: without it a retargeted climb clip holds a ladder that is not there.")]
        public bool gripLadder = true;

        [Tooltip("Let the CLIP climb and only correct it onto the ladder.\n\n" +
                 "This is the difference between IK that helps an animation and IK that " +
                 "replaces one. Placing all four limbs on a rung grid puts them exactly " +
                 "where they belong and takes the climb with it: hands and feet are the " +
                 "whole of a climb pose, and pinned to the world they hold still while the " +
                 "body rises — which looks precisely like one frame of the clip playing on " +
                 "a loop, and was reported as exactly that.\n\n" +
                 "So with this on, the hand's HEIGHT is the clip's own, every frame, and " +
                 "only the two things the clip cannot know are corrected: how far the " +
                 "ladder is from the camera, and where its rails are. The hand-over-hand " +
                 "rhythm is the animator's, the ladder is the level's, and neither has to " +
                 "know about the other.\n\n" +
                 "Off falls back to the rung grid below, which is worth having when a " +
                 "character has no climb clip at all.")]
        public bool followClip = true;

        [Tooltip("How far out along the rungs a hand may be corrected, as a share of the " +
                 "ladder's width. The rails are at its edges, so half is the rail itself.")]
        [Range(0.1f, 0.6f)] public float railLimit = 0.45f;

        [Tooltip("Distance between rungs, in metres. Used only with Follow Clip OFF: the " +
                 "hands then step up ONE rung at a time rather than sliding, which is what " +
                 "makes a climb read as a climb without a clip to read it from.\n\n" +
                 "It does not have to match the art exactly — it has to be close enough " +
                 "that a hand lands on a rung rather than between two.")]
        [Range(0.08f, 0.6f)] public float rungSpacing = 0.22f;

        [Tooltip("Where the hands reach, as a share of body height above the feet. The two " +
                 "differ by about half a rung on purpose: that is what makes them alternate " +
                 "instead of moving together like a lift.")]
        [Range(0.3f, 1.4f)] public float highHand = 1.05f;
        [Range(0.3f, 1.4f)] public float lowHand = 0.82f;

        [Tooltip("How far apart the hands grip, as a share of the ladder's width. The rails " +
                 "are at its edges, so a little under half puts a hand on each rail.")]
        [Range(0f, 0.5f)] public float handSpread = 0.34f;

        [Tooltip("Put the hands on the lip while hauling out at the top of a ladder.\n\n" +
                 "This is the moment that reads worst without IK. The climb-out is a scripted " +
                 "move through the air, and a retargeted clip plays a haul on a ledge that " +
                 "is not where this ledge is — so the fighter appears to hang beside the " +
                 "platform rather than pull himself onto it. Both hands on the edge he is " +
                 "arriving at is the whole of the fix.")]
        public bool gripLedge = true;

        [Tooltip("And the feet, which stand closer in than the hands grip.")]
        [Range(0f, 0.5f)] public float footSpread = 0.18f;
        [Range(0f, 0.6f)] public float lowFoot = 0.05f;

        [Tooltip("How long a hand takes to reach the next rung, in seconds.\n\n" +
                 "The whole difference between a climb and a stutter. A hand snapped straight " +
                 "onto its rung is a hand that teleports 22 cm nine times a second, and " +
                 "because the arms dominate the pose the entire body then reads as frozen " +
                 "with a jerk in it — which looks exactly like one frame of the clip playing " +
                 "on a loop. Reaching takes time; this is how long.")]
        [Range(0.02f, 0.5f)] public float reachSeconds = 0.13f;

        [Tooltip("How hard the hands are pinned. 1 is exactly on the rung, which can " +
                 "straighten an arm that the clip had bent; a little under keeps the pose.")]
        [Range(0f, 1f)] public float gripWeight = 0.9f;
        [Range(0f, 1f)] public float footGripWeight = 0.7f;

        [Tooltip("Turn the hands to face the rung as well as reach it. Off by default: hand " +
                 "orientation differs between rigs, so this is worth trying rather than " +
                 "assuming.")]
        [Range(0f, 1f)] public float gripRotationWeight;

        [Header("Standing")]
        [Tooltip("Put each foot on the surface actually under it, rather than on the flat " +
                 "plane the clip assumes. This is what stops a fighter standing on a step " +
                 "with one foot in the air and one inside the stone.\\n\\n" +
                 "Measured against the BAKED arena, never a physics raycast: the simulation " +
                 "does its own collision and most of this level has no Unity colliders at all.")]
        public bool groundFeet = true;

        [Tooltip("How far above and below the foot to look, in metres.")]
        [Range(0f, 1f)] public float probeUp = 0.35f;
        [Range(0f, 1f)] public float probeDown = 0.6f;

        [Tooltip("How far the sole sits above the surface it found, in metres.")]
        [Range(0f, 0.2f)] public float soleLift = 0.02f;

        [Tooltip("The most a foot may be moved. Past this the clip is doing something the " +
                 "ground cannot explain — a jump, a dodge — and pinning it would look worse.")]
        [Range(0f, 1f)] public float maxFootShift = 0.3f;

        [Range(0f, 1f)] public float groundWeight = 0.8f;

        [Tooltip("How high the clip has to lift a foot before this stops trying to put it " +
                 "on the floor, as a share of the fighter's height.\n\n" +
                 "THE one that matters for a run. Foot grounding exists to put a standing " +
                 "man on the step he is standing on; a foot in mid-stride is off the floor " +
                 "on purpose, and dragging it back down holds the swing leg behind the " +
                 "hips, which reads as a leg thrown out backwards. A planted foot is at " +
                 "zero and gets the full correction; one this far up gets none.")]
        [Range(0.01f, 0.4f)] public float plantShare = 0.08f;

        [Tooltip("Read the four distances above as metres on a 1.8 m fighter and scale " +
                 "them to this one.\n\n" +
                 "They were written against the rig the clips came off. These fighters are " +
                 "0.91 m, so a 0.3 m limit on moving a foot is a THIRD of the body: no " +
                 "stride a leg that short can take ever exceeds it, and every foot in the " +
                 "run cycle was therefore pinned to the floor.")]
        public bool scaleToBody = true;

        [Header("Head")]
        [Tooltip("Look at what matters: the rungs while climbing, the other fighter " +
                 "otherwise. Costs nothing and is most of what makes a figure look alive.")]
        [Range(0f, 1f)] public float lookWeight = 0.5f;
        [Range(0f, 1f)] public float lookBody = 0.15f;
        [Range(0f, 1f)] public float lookHead = 1f;

        [Header("Blending")]
        [Tooltip("Seconds to fade a goal in or out. A goal that snaps on is a limb that " +
                 "teleports, which reads worse than no IK at all.")]
        [Range(0.01f, 0.6f)] public float blendSeconds = 0.12f;

        [Header("Debug")]
        public bool drawTargets;
        public bool warnIfNoPass = true;

        Animator _animator;
        float _ladderBlend;
        float _groundBlend;
        int _calls;
        float _since;
        bool _warned;

        Vector3 _leftHand, _rightHand, _leftFoot, _rightFoot, _lookAt;
        bool _gripping;

        // Where each limb is on its way to, and how fast — one spring per limb, so a hand
        // that has just let go of a rung travels to the next one instead of appearing there.
        Vector3 _leftHandV, _rightHandV, _leftFootV, _rightFootV;
        bool _held;

        PlayerArm _arm;

        void Awake()
        {
            _animator = GetComponent<Animator>();
            if (player == null) player = GetComponentInParent<PlayerController>();

            // On THIS object, not in the children: OnAnimatorIK only arrives where the
            // Animator is, so that is the only place a PlayerArm can be doing anything.
            _arm = GetComponent<PlayerArm>();
        }

        /// <summary>Climbing or hauling out, which are the two poses that need both hands.</summary>
        bool Climbing()
        {
            if (player == null) return false;
            PlayerSimState s = player.State;
            SimWorld world = player.World;
            bool onLadder = gripLadder && world != null &&
                            s.LadderIndex >= 0 && s.LadderIndex < world.Ladders.Length;
            return onLadder || (gripLedge && !onLadder && s.Mode == MoveMode.Mantling);
        }

        /// <summary>
        /// True while this component needs BOTH hands -- climbing, or still blending off a
        /// climb. <see cref="PlayerArm"/> reads it to know whether its weapon hand is its
        /// own this frame.
        ///
        /// Decided in Update and only read in OnAnimatorIK, because every Update in the
        /// scene runs before any OnAnimatorIK does. Two components on one object get their
        /// IK callback in component order, which is whatever order somebody happened to
        /// add them in -- and for a component added to a nested prefab instance it is not
        /// visible anywhere. Anything that depends on that order is a coin toss that lands
        /// the same way until the day it does not.
        /// </summary>
        public bool HandsBusy { get; private set; }

        /// <summary>
        /// A layer with no IK Pass never calls OnAnimatorIK, and the symptom is that nothing
        /// happens at all — which looks exactly like this component being broken. So it says
        /// so, once, rather than leaving an hour to be spent looking in the wrong place.
        /// </summary>

        /// <summary>Whether the weapon hand is this component's to place this frame.</summary>
        bool OwnsWeaponHand => HandsBusy || _arm == null || !_arm.isActiveAndEnabled;

        AvatarIKGoal WeaponGoal => _arm != null && !_arm.rightHanded
            ? AvatarIKGoal.LeftHand : AvatarIKGoal.RightHand;

        AvatarIKGoal FreeGoal => WeaponGoal == AvatarIKGoal.RightHand
            ? AvatarIKGoal.LeftHand : AvatarIKGoal.RightHand;

        void Update()
        {
            HandsBusy = _ladderBlend > 0.001f || Climbing();

            if (!warnIfNoPass || _warned) return;

            _since += Time.deltaTime;
            if (_since < 1f || _calls > 0) return;

            _warned = true;
            Debug.LogWarning(
                $"{name}: a second of play and OnAnimatorIK has not been called once, so no " +
                "IK is running. The animator layer needs IK Pass ticked — " +
                "Tools > Gulag Runners > Set up IK does it.", this);
        }

        void OnAnimatorIK(int layer)
        {
            _calls++;

            // ONE pass, not one per layer.
            //
            // Unity calls this once for every animator layer with IK Pass ticked, and the
            // fighter controller has it on both. Everything below is per-frame work --
            // blends stepped by deltaTime, springs integrated, goals read back out of the
            // animator with GetIKPosition and written again -- so a second call a frame
            // ran every blend at double rate and fed each goal back into itself. The goals
            // set here survive into the later passes anyway, which is what makes ignoring
            // them correct rather than merely cheaper.
            if (layer != 0) return;

            if (_animator == null || player == null) return;

            PlayerSimState s = player.State;
            SimWorld world = player.World;
            float step = Time.deltaTime / Mathf.Max(0.01f, blendSeconds);

            bool onLadder = gripLadder && world != null &&
                            s.LadderIndex >= 0 && s.LadderIndex < world.Ladders.Length;
            // EVERY climb-out, not only the ones that step straight forward.
            //
            // StartScripted clears LadderIndex, so onLadder goes false on the very frame
            // the climb-out begins. Requiring ScriptForward as well meant a climb-out of a
            // HATCH -- which travels 1.1 m sideways and is therefore not "forward" -- matched
            // neither branch, so the hands were released from the rungs over the blend while
            // the haul clip played. Hands peeling off the ladder on their own schedule,
            // through the middle of the move, is exactly the leftover jerk at the top.
            bool onLedge = gripLedge && !onLadder && s.Mode == MoveMode.Mantling;

            if (!onLadder) _held = false;

            _ladderBlend = Mathf.MoveTowards(_ladderBlend,
                                             onLadder || onLedge ? 1f : 0f, step);
            _gripping = _ladderBlend > 0.001f;

            if (onLadder) Grip(in s, world);
            else if (onLedge) Ledge(in s);
            else if (_gripping) Release();
            else
            {
                // Not the hand the weapon is in, if something else owns it.
                //
                // This cleared BOTH, every frame, and PlayerArm sets the weapon hand
                // every frame -- two components writing one goal on one object, with the
                // winner decided by component order. When this one won, the weapon hand's
                // weight went to zero and the arm was drawn wherever the clip put it:
                // held up, ignoring the stick, while the simulation swung an arm nobody
                // could see. The hitbox was right and invisible, which is the worst way
                // for this to be wrong.
                if (!OwnsWeaponHand) Clear(WeaponGoal);
                Clear(FreeGoal);
            }

            bool standing = groundFeet && world != null && s.Mode == MoveMode.Grounded;
            _groundBlend = Mathf.MoveTowards(_groundBlend,
                                             standing && !_gripping ? 1f : 0f, step);
            if (_groundBlend > 0.001f) Ground(in s, world);
            else if (!_gripping)
            {
                Clear(AvatarIKGoal.LeftFoot);
                Clear(AvatarIKGoal.RightFoot);
            }

            Look(in s, world);
        }

        /// <summary>
        /// Hands and feet onto the rungs.
        ///
        /// The rung line comes from the same two functions the motor uses to decide where the
        /// body stands, so the grip cannot drift from the climb. A leaning ladder is handled
        /// for free: both functions already walk sideways with the lean.
        /// </summary>
        void Grip(in PlayerSimState s, SimWorld world)
        {
            if (followClip) { Correct(in s, world); return; }

            int i = s.LadderIndex;
            Aabb box = world.Ladders[i];

            float bottom = M(box.MinY), top = M(box.MaxY);
            float width = M(box.MaxX) - M(box.MinX);
            float height = Mathf.Max(0.2f, player.tuning.bodyHeight);
            float feet = M(s.Position.Y);
            float spacing = Mathf.Max(0.05f, rungSpacing);

            // Copied out of the state before the two helpers below: a local function may not
            // capture an "in" parameter, and the alternative is four copies of this sum.
            Fix depth = s.Depth;

            // One rung at a time, never between two: the hands hold still while the body
            // rises and then move up a whole rung, which is what climbing looks like.
            float Rung(float want)
            {
                float snapped = bottom + Mathf.Round((want - bottom) / spacing) * spacing;
                return Mathf.Clamp(snapped, bottom + spacing * 0.5f, top - spacing * 0.5f);
            }

            Vector3 On(float y, float side)
            {
                float rung = Rung(y);
                float x = M(world.LadderCentreAt(i, F(rung))) + side * width;
                float z = M(world.LadderDepthAt(i, F(rung), depth, Fix.Zero));
                return new Vector3(x, rung, z);
            }

            Vector3 wantLeftHand = On(feet + height * highHand, -handSpread);
            Vector3 wantRightHand = On(feet + height * lowHand, handSpread);
            Vector3 wantLeftFoot = On(feet + height * lowFoot, -footSpread);
            Vector3 wantRightFoot = On(feet + height * lowFoot + spacing, footSpread);

            // First frame on the ladder: start AT the rungs rather than springing to them
            // from wherever the limbs happened to be when the grab began.
            if (!_held)
            {
                _leftHand = wantLeftHand; _rightHand = wantRightHand;
                _leftFoot = wantLeftFoot; _rightFoot = wantRightFoot;
                _leftHandV = _rightHandV = _leftFootV = _rightFootV = Vector3.zero;
                _held = true;
            }

            float reach = Mathf.Max(0.02f, reachSeconds);
            float dt = Time.deltaTime;

            _leftHand = Vector3.SmoothDamp(_leftHand, wantLeftHand, ref _leftHandV, reach,
                                           Mathf.Infinity, dt);
            _rightHand = Vector3.SmoothDamp(_rightHand, wantRightHand, ref _rightHandV, reach,
                                            Mathf.Infinity, dt);
            _leftFoot = Vector3.SmoothDamp(_leftFoot, wantLeftFoot, ref _leftFootV, reach,
                                           Mathf.Infinity, dt);
            _rightFoot = Vector3.SmoothDamp(_rightFoot, wantRightFoot, ref _rightFootV, reach,
                                            Mathf.Infinity, dt);

            Set(AvatarIKGoal.LeftHand, _leftHand, gripWeight * _ladderBlend);
            Set(AvatarIKGoal.RightHand, _rightHand, gripWeight * _ladderBlend);
            Set(AvatarIKGoal.LeftFoot, _leftFoot, footGripWeight * _ladderBlend);
            Set(AvatarIKGoal.RightFoot, _rightFoot, footGripWeight * _ladderBlend);

            if (gripRotationWeight <= 0f) return;

            // Palms to the rungs: the hand looks at the ladder, which is +Z of the body.
            Quaternion grip = Quaternion.LookRotation(
                new Vector3(0f, 0f, 1f), Vector3.up);
            float w = gripRotationWeight * _ladderBlend;
            _animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, w);
            _animator.SetIKRotation(AvatarIKGoal.LeftHand, grip);
            _animator.SetIKRotationWeight(AvatarIKGoal.RightHand, w);
            _animator.SetIKRotation(AvatarIKGoal.RightHand, grip);
        }

        /// <summary>
        /// Letting go: the same targets, at the weight the blend has got down to.
        ///
        /// Mecanim keeps an IK goal's weight until it is told otherwise, so the frames
        /// between stepping off the rungs and the blend reaching zero used to apply nothing
        /// at all — which is not a fade, it is full strength held on a stale rung and then
        /// dropped in one frame. The hands snapped at the end of every single climb, and the
        /// blend that was supposed to prevent it was doing nothing but counting down.
        /// </summary>
        void Release()
        {
            Set(AvatarIKGoal.LeftHand, _leftHand, gripWeight * _ladderBlend);
            Set(AvatarIKGoal.RightHand, _rightHand, gripWeight * _ladderBlend);
            Set(AvatarIKGoal.LeftFoot, _leftFoot, footGripWeight * _ladderBlend);
            Set(AvatarIKGoal.RightFoot, _rightFoot, footGripWeight * _ladderBlend);
        }

        /// <summary>
        /// The clip climbs; this only moves its hands and feet ONTO the ladder.
        ///
        /// Each limb is read back out of the animator — <see cref="Animator.GetIKPosition"/>
        /// on an IK pass returns where the clip has just put it — and only two of its three
        /// numbers are touched. The height is left exactly as the animator wrote it, so the
        /// reach, the pause and the push of a climb all survive intact. The depth is pulled
        /// onto the rung line, which is the fault IK was added for and the one no amount of
        /// tuning the body's distance could fix. And X is held inside the rails, which costs
        /// nothing on an upright ladder and keeps a hand on a leaning one.
        ///
        /// No smoothing, and none needed: the clip is already smooth and the correction is
        /// a near-constant offset, so there is nothing here that can step.
        /// </summary>
        void Correct(in PlayerSimState s, SimWorld world)
        {
            int i = s.LadderIndex;
            float width = M(world.Ladders[i].MaxX - world.Ladders[i].MinX);
            Fix depth = s.Depth;
            float limit = Mathf.Max(0.01f, width * railLimit);

            Vector3 Onto(AvatarIKGoal goal)
            {
                Vector3 at = _animator.GetIKPosition(goal);
                Fix y = F(at.y);

                float centre = M(world.LadderCentreAt(i, y));
                float rungs = M(world.LadderDepthAt(i, y, depth, Fix.Zero));

                return new Vector3(Mathf.Clamp(at.x, centre - limit, centre + limit),
                                   at.y, rungs);
            }

            _leftHand = Onto(AvatarIKGoal.LeftHand);
            _rightHand = Onto(AvatarIKGoal.RightHand);
            _leftFoot = Onto(AvatarIKGoal.LeftFoot);
            _rightFoot = Onto(AvatarIKGoal.RightFoot);

            Set(AvatarIKGoal.LeftHand, _leftHand, gripWeight * _ladderBlend);
            Set(AvatarIKGoal.RightHand, _rightHand, gripWeight * _ladderBlend);
            Set(AvatarIKGoal.LeftFoot, _leftFoot, footGripWeight * _ladderBlend);
            Set(AvatarIKGoal.RightFoot, _rightFoot, footGripWeight * _ladderBlend);

            // The springs belong to the rung grid; coming back to it must not spring from
            // wherever the clip happened to leave a limb.
            _held = false;
        }

        /// <summary>
        /// Both hands onto the lip being hauled over.
        ///
        /// The scripted climb-out already knows exactly where it is going — it was computed
        /// from the floor the ladder serves — so the edge is <see cref="PlayerSimState.ScriptTo"/>
        /// and needs no searching. The hands go there and stay there while the body rises to
        /// meet them, which is what hauling yourself up is.
        /// </summary>
        void Ledge(in PlayerSimState s)
        {
            float width = Mathf.Max(0.1f, player.tuning.bodyWidth);
            float lip = M(s.ScriptTo.Y);
            float x = M(s.ScriptTo.X);
            float z = M(s.ScriptToDepth);

            _leftHand = new Vector3(x - width * 0.9f, lip, z);
            _rightHand = new Vector3(x + width * 0.9f, lip, z);

            Set(AvatarIKGoal.LeftHand, _leftHand, gripWeight * _ladderBlend);
            Set(AvatarIKGoal.RightHand, _rightHand, gripWeight * _ladderBlend);

            // Feet are left to the clip here: during a haul they are against the wall below
            // and there is nothing in the world to pin them to.
            Clear(AvatarIKGoal.LeftFoot);
            Clear(AvatarIKGoal.RightFoot);
        }

        /// <summary>
        /// Each foot onto whatever is under it, read out of the baked arena.
        ///
        /// A foot the ground cannot explain is left alone. A clip mid-stride lifts a foot
        /// well clear, and dragging it back down to the nearest surface turns a walk into a
        /// shuffle — so the shift is capped, and past the cap the animation wins.
        /// </summary>
        void Ground(in PlayerSimState s, SimWorld world)
        {
            _leftFoot = Sole(AvatarIKGoal.LeftFoot, in s, world);
            _rightFoot = Sole(AvatarIKGoal.RightFoot, in s, world);
        }

        Vector3 Sole(AvatarIKGoal goal, in PlayerSimState s, SimWorld world)
        {
            Vector3 at = _animator.GetIKPosition(goal);

            float body = Mathf.Max(0.2f, player.tuning.bodyHeight);
            float k = scaleToBody ? body / 1.8f : 1f;
            float up = probeUp * k, down = probeDown * k;

            // How far the CLIP has lifted this foot off the fighter's own floor, and
            // therefore how much of a correction it has any business receiving. See the
            // Plant Share tooltip: this is the difference between standing on a step and
            // running with the feet nailed down.
            float band = Mathf.Max(0.001f, body * plantShare);
            float lift = at.y - M(s.Position.Y);
            float planted = 1f - Mathf.SmoothStep(0f, 1f,
                                Mathf.Clamp01((lift - band * 0.35f) / (band * 0.65f)));
            if (planted <= 0.001f)
            {
                Clear(goal);
                return at;
            }

            FixVec2 from = new FixVec2(F(at.x), F(at.y + up));
            MoveConfig cfg = player.Config;

            if (!PlayerMotor.TryFindGroundBelow(from, s.Depth, world, in cfg,
                                                F(up + down), out Fix groundY))
            {
                Clear(goal);
                return at;
            }

            float want = M(groundY) + soleLift * k;
            float shift = want - at.y;
            if (Mathf.Abs(shift) > maxFootShift * k)
            {
                Clear(goal);
                return at;
            }

            // Eased by how far it has to go, so a foot already in the right place is not
            // nudged and one well off is brought most of the way.
            float w = groundWeight * _groundBlend * planted;
            Vector3 target = new Vector3(at.x, want, at.z);
            Set(goal, target, w);
            return target;
        }

        void Look(in PlayerSimState s, SimWorld world)
        {
            // The weapon leads the body in a fight, so PlayerArm aims the look at its own
            // blade and this stands down. Both wrote SetLookAtWeight every frame, which
            // is the same coin toss the hands were.
            if (!OwnsWeaponHand && _arm != null && _arm.bodyFollow > 0f) return;

            if (lookWeight <= 0.001f)
            {
                _animator.SetLookAtWeight(0f);
                return;
            }

            float height = Mathf.Max(0.2f, player.tuning.bodyHeight);
            Vector3 eyes = new Vector3(M(s.Position.X), M(s.Position.Y) + height * 0.9f,
                                       M(s.Depth));

            if (_gripping)
                _lookAt = new Vector3(_leftHand.x, (_leftHand.y + _rightHand.y) * 0.5f,
                                      _leftHand.z);
            else
                _lookAt = eyes + new Vector3(s.Facing >= 0 ? 3f : -3f, 0f, 0f);

            _animator.SetLookAtWeight(lookWeight, lookBody, lookHead, 0f, 0.6f);
            _animator.SetLookAtPosition(_lookAt);
        }

        void Set(AvatarIKGoal goal, Vector3 at, float weight)
        {
            _animator.SetIKPositionWeight(goal, Mathf.Clamp01(weight));
            _animator.SetIKPosition(goal, at);
        }

        void Clear(AvatarIKGoal goal)
        {
            _animator.SetIKPositionWeight(goal, 0f);
            _animator.SetIKRotationWeight(goal, 0f);
        }

        static float M(Fix v) => v.ToMilli() / 1000f;
        static Fix F(float v) => Fix.FromMilli(Mathf.RoundToInt(v * 1000f));

        void OnDrawGizmos()
        {
            if (!drawTargets || !Application.isPlaying) return;

            Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.9f);
            Gizmos.DrawWireSphere(_leftHand, 0.04f);
            Gizmos.DrawWireSphere(_rightHand, 0.04f);
            Gizmos.color = new Color(1f, 0.8f, 0.3f, 0.9f);
            Gizmos.DrawWireSphere(_leftFoot, 0.04f);
            Gizmos.DrawWireSphere(_rightFoot, 0.04f);
        }
    }
}
