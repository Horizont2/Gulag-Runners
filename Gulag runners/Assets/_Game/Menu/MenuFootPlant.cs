using UnityEngine;

namespace GulagRunners.Game
{
    /// <summary>
    /// Nails the menu fighter's feet to the floor he is standing on.
    ///
    /// Two things move him and neither one should move his feet. The idle clip breathes,
    /// which rocks the hips; and <see cref="MenuFighter"/> sways the whole object a few
    /// degrees so the silhouette is not a screenshot. Both are right, and both are applied
    /// to the ROOT -- so the legs go with them and the boots slide and twist on the stone.
    ///
    /// The answer is not to stop him moving. It is to decide which end of him is fixed. A
    /// man shifting his weight in a cell moves his shoulders over his feet; the feet stay
    /// where he put them. So this takes the feet where they first land, and holds them
    /// there in world space for as long as he stands. The sway then reads as what it was
    /// always meant to be -- a body turning above planted boots, with the knees taking the
    /// difference -- rather than the whole man rotating on a plinth.
    ///
    /// Why its own component and not PlayerIK: PlayerIK plants feet against the SIMULATION,
    /// which is not running here. There is no world, no motor and no fighter state in the
    /// menu, and nothing to ask where the ground is. Here the ground is simply wherever the
    /// feet were when the pose settled.
    /// </summary>
    /// <remarks>
    /// Must sit on the object that owns the Animator: OnAnimatorIK is only ever called
    /// there. The controller also needs IK Pass on its base layer, which the fighter's
    /// controller has and animator_check keeps that way.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class MenuFootPlant : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("The rig. This component has to be on the same object, because " +
                 "OnAnimatorIK only arrives on the object that owns the Animator.")]
        public Animator animator;

        [Header("Planting")]
        [Tooltip("How hard the feet are held. 1 is nailed down, which is what a man " +
                 "standing still looks like. Below that the clip shows through and the " +
                 "boots creep.")]
        [Range(0f, 1f)] public float weight = 1f;

        [Tooltip("Hold the feet's FACING too, not just where they are. Off, the toes " +
                 "turn with the body while the ankles stay put, which is worse than " +
                 "either: a foot that pivots without moving.")]
        public bool holdFacing = true;

        [Tooltip("Frames to let the pose settle before the feet are taken.\n\n" +
                 "The first frame of an animator is whatever it was holding a moment " +
                 "ago, often the bind pose, and feet captured there are nailed to a " +
                 "T-pose's stance. A few frames in, the idle is the idle.")]
        [Range(0, 60)] public int settleFrames = 4;

        [Tooltip("Put the captured feet on the floor the fighter's own object stands " +
                 "on, instead of wherever the clip's first frames happened to leave " +
                 "them. The model's origin IS its soles, so this is the floor.")]
        public bool snapToFloor = true;

        [Header("Debug")]
        public string state = "not planted yet";

        Vector3 _left, _right;
        Quaternion _leftTurn, _rightTurn;
        bool _planted;
        bool _complained;
        int _seen;
        float _since;

        void Reset() => animator = GetComponent<Animator>();

        void Awake()
        {
            if (animator == null) animator = GetComponent<Animator>();

            if (animator == null)
                Debug.LogError(
                    $"{name}: MenuFootPlant is not on the object that owns the Animator, " +
                    "so Unity will never call OnAnimatorIK here and the feet will go on " +
                    "sliding. Put it beside the Animator.", this);
        }

        void OnEnable()
        {
            _planted = false;
            _seen = 0;
            _complained = false;
            _since = 0f;
            state = "waiting for the pose to settle";
        }

        /// <summary>
        /// Says so when Unity is never going to call this.
        ///
        /// OnAnimatorIK only arrives if the controller has IK Pass ticked on the layer.
        /// Without it this component sits there doing nothing, in a scene where nothing
        /// looks broken enough to go hunting -- the feet just carry on sliding, exactly
        /// as they did before it was added.
        /// </summary>
        void Update()
        {
            if (_planted || _complained || animator == null) return;

            _since += Time.unscaledDeltaTime;
            if (_since < 2f) return;

            _complained = true;
            state = "OnAnimatorIK is never called here";
            Debug.LogWarning(
                $"{name}: two seconds and Unity has not called OnAnimatorIK once, so the " +
                "feet are not being planted. The Animator Controller needs IK Pass on " +
                "its base layer — Tools > Gulag Runners > Setup > Build fighter animator " +
                "sets it, and animator_check keeps it that way.", this);
        }

        /// <summary>Take the feet again — after a skin change, or a move in the scene.</summary>
        [ContextMenu("Re-plant")]
        public void Replant() => OnEnable();

        void OnAnimatorIK(int layer)
        {
            // One layer. The fighter's controller carries an IK pass on the base layer
            // only, and a second pass would run this whole solve twice a frame.
            if (layer != 0 || animator == null) return;

            Transform left = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform right = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            if (left == null || right == null)
            {
                state = "this rig has no feet to plant";
                return;
            }

            if (!_planted)
            {
                if (_seen++ < settleFrames) return;

                _left = left.position;
                _right = right.position;
                _leftTurn = left.rotation;
                _rightTurn = right.rotation;

                if (snapToFloor)
                {
                    // The model's origin is its soles -- fighter_pose_check measures that
                    // and holds it -- so the object's own Y is the floor, and both feet
                    // belong on it rather than at whatever height two frames of a
                    // breathing clip left them.
                    float floor = transform.position.y;
                    _left.y = floor;
                    _right.y = floor;
                }

                _planted = true;
                state = $"planted at {_left.y:0.000}";
            }

            Hold(AvatarIKGoal.LeftFoot, _left, _leftTurn);
            Hold(AvatarIKGoal.RightFoot, _right, _rightTurn);
        }

        void Hold(AvatarIKGoal goal, Vector3 at, Quaternion facing)
        {
            float w = Mathf.Clamp01(weight);
            animator.SetIKPositionWeight(goal, w);
            animator.SetIKPosition(goal, at);

            if (!holdFacing) return;
            animator.SetIKRotationWeight(goal, w);
            animator.SetIKRotation(goal, facing);
        }

        void OnDrawGizmosSelected()
        {
            if (!_planted) return;
            Gizmos.color = new Color(1f, 0.8f, 0.3f, 0.9f);
            Gizmos.DrawWireSphere(_left, 0.03f);
            Gizmos.DrawWireSphere(_right, 0.03f);
        }
    }
}
