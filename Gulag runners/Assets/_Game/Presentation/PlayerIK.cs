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

        [Tooltip("Distance between rungs, in metres. The hands step up ONE rung at a time " +
                 "rather than sliding, which is what makes a climb read as a climb; this is " +
                 "the size of that step.\\n\\n" +
                 "It does not have to match the art exactly — it has to be close enough that " +
                 "a hand lands on a rung rather than between two.")]
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

        void Awake()
        {
            _animator = GetComponent<Animator>();
            if (player == null) player = GetComponentInParent<PlayerController>();
        }

        /// <summary>
        /// A layer with no IK Pass never calls OnAnimatorIK, and the symptom is that nothing
        /// happens at all — which looks exactly like this component being broken. So it says
        /// so, once, rather than leaving an hour to be spent looking in the wrong place.
        /// </summary>
        void Update()
        {
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
            if (_animator == null || player == null) return;

            PlayerSimState s = player.State;
            SimWorld world = player.World;
            float step = Time.deltaTime / Mathf.Max(0.01f, blendSeconds);

            bool onLadder = gripLadder && world != null &&
                            s.LadderIndex >= 0 && s.LadderIndex < world.Ladders.Length;
            bool onLedge = gripLedge && !onLadder &&
                           s.Mode == MoveMode.Mantling && s.ScriptForward;

            _ladderBlend = Mathf.MoveTowards(_ladderBlend,
                                             onLadder || onLedge ? 1f : 0f, step);
            _gripping = _ladderBlend > 0.001f;

            if (onLadder) Grip(in s, world);
            else if (onLedge) Ledge(in s);
            else if (!_gripping)
            {
                Clear(AvatarIKGoal.LeftHand);
                Clear(AvatarIKGoal.RightHand);
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

            _leftHand = On(feet + height * highHand, -handSpread);
            _rightHand = On(feet + height * lowHand, handSpread);
            _leftFoot = On(feet + height * lowFoot, -footSpread);
            _rightFoot = On(feet + height * lowFoot + spacing, footSpread);

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

            FixVec2 from = new FixVec2(F(at.x), F(at.y + probeUp));
            MoveConfig cfg = player.Config;

            if (!PlayerMotor.TryFindGroundBelow(from, s.Depth, world, in cfg,
                                                F(probeUp + probeDown), out Fix groundY))
            {
                Clear(goal);
                return at;
            }

            float want = M(groundY) + soleLift;
            float shift = want - at.y;
            if (Mathf.Abs(shift) > maxFootShift)
            {
                Clear(goal);
                return at;
            }

            // Eased by how far it has to go, so a foot already in the right place is not
            // nudged and one well off is brought most of the way.
            float w = groundWeight * _groundBlend;
            Vector3 target = new Vector3(at.x, want, at.z);
            Set(goal, target, w);
            return target;
        }

        void Look(in PlayerSimState s, SimWorld world)
        {
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
