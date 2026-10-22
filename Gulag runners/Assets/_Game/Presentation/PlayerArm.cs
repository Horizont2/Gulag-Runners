using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// Puts the weapon arm where the simulation says it is.
    ///
    /// This is the other half of docs/31. The simulation owns the arm as a unit vector
    /// off the shoulder, and that vector IS the hitbox -- so the drawn arm has to agree
    /// with it exactly, every frame, or the player is aiming at one thing and hitting
    /// another. That rules out playing a swing clip: a clip is an animator's idea of
    /// where an arm goes, and here the only authority is the thumb.
    ///
    /// So the hand is an IK goal, like the feet in <see cref="PlayerIK"/> and for the
    /// same reason -- Unity's humanoid solver is deterministic given the same goal, and
    /// the goal comes straight out of the deterministic layer.
    ///
    /// Interpolated by the controller's render alpha, because the body is: an arm pinned
    /// to the tick while the body flows between ticks reads as the weapon lagging a frame
    /// behind its owner, which is exactly the complaint this whole rewrite exists to fix.
    /// </summary>
    /// <remarks>
    /// There is deliberately NO [RequireComponent(typeof(Animator))] here, although this
    /// component does nothing without one. RequireComponent does not check -- it CREATES.
    /// Dropped on the fighter's root by hand, it silently added a second, empty Animator
    /// there, and PlayerAnimator's fallback is GetComponentInChildren, which searches the
    /// object itself first. So the whole rig bound to the empty Animator and every clip
    /// in the game stopped playing while the body still moved. An attribute that invents
    /// the thing that breaks you is worse than no attribute.
    ///
    /// Let the tool place it: Tools > Gulag Runners > Setup > Set up IK puts this on the
    /// Animator's own object, which is the only place OnAnimatorIK is ever called.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PlayerArm : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("The fighter this arm belongs to. Found in a parent when empty.")]
        public PlayerController player;

        [Tooltip("The rig. This component has to sit on the same object, because " +
                 "OnAnimatorIK only arrives on the object that owns the Animator.")]
        public Animator animator;

        [Header("Which arm")]
        [Tooltip("The hand that holds the weapon. The other one is left to the clip.")]
        public bool rightHanded = true;

        [Header("Weight")]
        [Tooltip("How strongly the hand is pulled onto the simulated grip. Below 1 the " +
                 "clip shows through, which is useful for seeing what the IK is doing " +
                 "and wrong for playing: the drawn blade would not be the real one.")]
        [Range(0f, 1f)] public float handWeight = 1f;

        [Tooltip("How strongly the hand is TWISTED to line up with the weapon. The " +
                 "position is what decides a hit; this only stops the blade pointing " +
                 "out of the back of the fist.")]
        [Range(0f, 1f)] public float rotationWeight = 0.85f;

        [Tooltip("Lean the chest after the arm, as a share of the arm's own angle. A " +
                 "shoulder that never moves makes the whole body look bolted down -- but " +
                 "too much and the legs skate, because the hips come with it.")]
        [Range(0f, 0.6f)] public float bodyFollow = 0.22f;

        [Header("Debug")]
        [Tooltip("Draw the simulated arm and the weapon segment in the scene view. This " +
                 "is the hitbox, exactly: if the gizmo is not on the blade, nothing else " +
                 "in this component is worth looking at.")]
        public bool drawGizmo = true;

        Vector3 _grip;
        Vector3 _tip;
        bool _have;

        void Reset()
        {
            player = GetComponentInParent<PlayerController>();
            animator = GetComponent<Animator>();
        }

        void Awake()
        {
            if (player == null) player = GetComponentInParent<PlayerController>();
            if (animator == null) animator = GetComponent<Animator>();

            // Said out loud, because the failure is otherwise invisible: no Animator on
            // THIS object means Unity never calls OnAnimatorIK on it, so the arm is
            // simply never drawn where the simulation is swinging it.
            if (animator == null)
                Debug.LogError(
                    $"{name}: PlayerArm is not on the object that owns the Animator, so " +
                    "Unity will never call OnAnimatorIK here and the drawn arm will not " +
                    "be the one that hits people. Run Tools > Gulag Runners > Setup > " +
                    "Set up IK, which puts it in the right place.", this);
        }

        void OnAnimatorIK(int layer)
        {
            // Both layers carry an IK pass on this rig, so without this the whole solve
            // runs twice a frame and the goal is applied twice. That one cost a week of
            // "the legs are thrown backwards while running" (docs/24).
            if (layer != 0) return;
            if (animator == null || player == null) return;

            if (!Read(out Vector3 grip, out Vector3 tip)) return;

            AvatarIKGoal goal = rightHanded ? AvatarIKGoal.RightHand : AvatarIKGoal.LeftHand;

            animator.SetIKPositionWeight(goal, handWeight);
            animator.SetIKPosition(goal, grip);

            if (rotationWeight > 0f)
            {
                Vector3 along = tip - grip;
                if (along.sqrMagnitude > 1e-6f)
                {
                    animator.SetIKRotationWeight(goal, rotationWeight);
                    animator.SetIKRotation(goal,
                        Quaternion.LookRotation(along.normalized, Vector3.up));
                }
            }

            if (bodyFollow > 0f)
            {
                // Only the LOOK, never the body position: moving the body IK goal drags
                // the hips, and a fighter whose hips follow their sword cannot stand on
                // a ledge.
                animator.SetLookAtWeight(bodyFollow, bodyFollow, 1f, 0f, 0f);
                animator.SetLookAtPosition(tip);
            }
        }

        /// <summary>Grip and tip in world space, interpolated like the body is.</summary>
        bool Read(out Vector3 grip, out Vector3 tip)
        {
            grip = tip = default;
            if (player == null) return false;

            PlayerSimState s = player.State;
            if (s.Arm.LengthSq.Raw == 0) return false;

            ArmConfig cfg = player.ArmSettings;
            MoveConfig move = player.Config;

            FixVec2 shoulder = ArmMotor.Shoulder(in s, in move, in cfg);
            FixVec2 g = ArmMotor.Hand(in s, in move, in cfg);
            FixVec2 t = ArmMotor.Tip(in s, in move, in cfg);

            // The simulation is flat; the arena has depth, and the fighter's own plane is
            // the one thing the sim does not carry.
            float z = player.transform.position.z;

            grip = new Vector3(M(g.X), M(g.Y), z);
            tip = new Vector3(M(t.X), M(t.Y), z);

            // The shoulder moves with the interpolated body, so the whole arm is shifted
            // by however far the drawn body is from the simulated one. That keeps the arm
            // attached to the shoulder it is drawn on rather than to last tick's.
            Vector3 drawn = player.transform.position;
            Vector3 simulated = new Vector3(M(shoulder.X), drawn.y, z);
            Vector3 slip = new Vector3(drawn.x - simulated.x, 0f, 0f);

            grip += slip;
            tip += slip;

            _grip = grip;
            _tip = tip;
            _have = true;
            return true;
        }

        static float M(Fix v) => v.Raw / (float)Fix.RawOne;

        void OnDrawGizmos()
        {
            if (!drawGizmo || !_have) return;

            // Red while the blade is live, white while it is being carried. This is the
            // single most useful thing in the scene view: it is literally the hit test.
            bool live = player != null && player.State.SwingShare > 0;
            Gizmos.color = live ? new Color(1f, 0.3f, 0.2f) : new Color(1f, 1f, 1f, 0.5f);
            Gizmos.DrawLine(_grip, _tip);
            Gizmos.DrawWireSphere(_tip, 0.06f);
        }
    }
}
