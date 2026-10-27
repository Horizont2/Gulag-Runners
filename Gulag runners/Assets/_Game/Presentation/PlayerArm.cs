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

        [Tooltip("How much of the arm's UP-AND-DOWN the chest is allowed to copy.\n\n" +
                 "The chest used to be aimed at the weapon's tip, and the tip goes all " +
                 "the way round: a guard held low puts it below the fighter's own feet, " +
                 "and a chest told to look there folds the body in half. Sideways is " +
                 "the part of an aim a torso actually turns with, so the up-and-down is " +
                 "kept to this share of it.")]
        [Range(0f, 1f)] public float verticalFollow = 0.3f;

        [Tooltip("How far the look is allowed to turn the body before it is held.\n\n" +
                 "Unity's clamp, and it reads backwards: 0 is NO limit and 1 is frozen. " +
                 "This was 0 -- which is why an arm swung behind the fighter could turn " +
                 "the spine around after it. PlayerIK uses 0.6 for the same reason.")]
        [Range(0f, 1f)] public float lookClamp = 0.6f;

        [Tooltip("How far ahead of the shoulder the look point is put. Only the " +
                 "direction matters to the solver; a point a couple of metres out keeps " +
                 "the angle steady while the hand moves.")]
        [Range(0.5f, 6f)] public float lookDistance = 2.5f;

        [Tooltip("The hand's weapon socket. Found under this object when empty.\n\n" +
                 "Used to MEASURE which way the weapon points, so the hand can be turned " +
                 "to put it on the arm's line without anyone typing an axis per weapon " +
                 "pack. Empty, or bare-handed, and the hand's rotation is left to the clip.")]
        public WeaponSocket socket;

        [Header("Debug")]
        [Tooltip("Draw the simulated arm and the weapon segment in the scene view. This " +
                 "is the hitbox, exactly: if the gizmo is not on the blade, nothing else " +
                 "in this component is worth looking at.")]
        public bool drawGizmo = true;

        Vector3 _grip;
        Vector3 _tip;
        bool _have;
        PlayerIK _ik;

        void Reset()
        {
            player = GetComponentInParent<PlayerController>();
            animator = GetComponent<Animator>();
        }

        void Awake()
        {
            if (player == null) player = GetComponentInParent<PlayerController>();
            if (animator == null) animator = GetComponent<Animator>();
            if (socket == null) socket = GetComponentInChildren<WeaponSocket>(true);

            // On THIS object: OnAnimatorIK only arrives where the Animator is, so a
            // PlayerIK anywhere else is not competing for the same goals.
            _ik = GetComponent<PlayerIK>();

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

            AvatarIKGoal goal = rightHanded ? AvatarIKGoal.RightHand : AvatarIKGoal.LeftHand;

            // Climbing takes both hands, and PlayerIK is placing them on the rungs. It is
            // the owner of this goal for as long as that lasts, and the two of them must
            // never both write it -- which they did, every frame, with the winner decided
            // by component order.
            if (_ik != null && _ik.HandsBusy) return;

            if (!Read(out Vector3 grip, out Vector3 tip))
            {
                // Say so, rather than leaving last frame's weight standing. A goal nobody
                // updated keeps the hand pinned to where the arm was when it last had an
                // answer, which reads as the arm sticking.
                animator.SetIKPositionWeight(goal, 0f);
                animator.SetIKRotationWeight(goal, 0f);
                _have = false;
                return;
            }

            animator.SetIKPositionWeight(goal, handWeight);
            animator.SetIKPosition(goal, grip);

            if (rotationWeight > 0f) Twist(goal, grip, tip);

            if (bodyFollow > 0f) Lean(grip, tip);
        }

        /// <summary>
        /// Turns the chest a little after the weapon, without folding the fighter up.
        ///
        /// Only the LOOK, never the body position: moving the body IK goal drags the
        /// hips, and a fighter whose hips follow their sword cannot stand on a ledge.
        ///
        /// Two things were wrong with doing that at the tip, unclamped, and both of them
        /// are the same complaint -- the body bending as the stick tilts:
        ///
        ///   the target went under the floor   The tip travels a full circle around the
        ///       shoulder, so a low guard puts it below the fighter's own feet. A chest
        ///       told to look straight down does what it is told.
        ///   nothing stopped it               Unity's clamp argument reads backwards: 0
        ///       is no limit, not full limit. So an arm swung behind could take the
        ///       spine round after it, which is the pose that reads as broken rather
        ///       than as leaning.
        ///
        /// What a torso actually does with an aim is turn toward it; the up-and-down is
        /// mostly the arm's own business. So the direction is flattened toward the
        /// horizontal and then held at a sensible distance, which keeps the lean while
        /// the stick is doing anything at all.
        /// </summary>
        void Lean(Vector3 grip, Vector3 tip)
        {
            Vector3 aim = tip - grip;
            if (aim.sqrMagnitude <= 1e-6f) aim = grip - transform.position;
            if (aim.sqrMagnitude <= 1e-6f) return;

            aim.y *= verticalFollow;
            if (aim.sqrMagnitude <= 1e-8f) return;

            Vector3 at = grip + aim.normalized * lookDistance;
            animator.SetLookAtWeight(bodyFollow, bodyFollow, 1f, 0f, lookClamp);
            animator.SetLookAtPosition(at);
        }

        /// <summary>
        /// Turns the hand so the WEAPON lies along the arm the simulation is swinging.
        ///
        /// By the smallest rotation that does it, applied on top of whatever the clip
        /// and the grip already produced -- so the fist keeps holding the thing the way
        /// it was authored to, and only swings onto the right line.
        ///
        /// It used to be a world LookRotation down the arm, which forced the hand's own
        /// +Z onto the blade line. A hand bone's forward is not the direction anything
        /// is held in, so the weapon sat crooked in the fist, and it went wrong in a
        /// different way for every clip because the correction ignored the pose entirely.
        ///
        /// The blade direction is MEASURED by WeaponSocket off the model's own mesh, so
        /// nothing here has to know which axis a given weapon pack points down.
        /// </summary>
        void Twist(AvatarIKGoal goal, Vector3 grip, Vector3 tip)
        {
            Vector3 want = tip - grip;
            if (want.sqrMagnitude <= 1e-6f) return;
            want.Normalize();

            Transform hand = animator.GetBoneTransform(
                rightHanded ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
            if (hand == null) return;

            if (socket == null) socket = GetComponentInChildren<WeaponSocket>(true);

            // Bare hands have no blade to line up: the fist is the weapon and the clip's
            // own wrist is better than anything computed here.
            if (socket == null || !socket.TryBladeDirection(out Vector3 blade)) return;
            if (blade.sqrMagnitude <= 1e-8f) return;

            Quaternion swing = Quaternion.FromToRotation(blade.normalized, want);
            animator.SetIKRotationWeight(goal, rotationWeight);
            animator.SetIKRotation(goal, swing * hand.rotation);
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

            // And which way the MODEL thinks it points, which is a different question and
            // the one that goes wrong quietly. Twist turns the hand by the rotation that
            // puts this on the line above, so when the two disagree the wrist is wrenched
            // and the solver drags the whole arm after it -- which shows up as an arm that
            // will not go below horizontal on some weapons and not others. Green along the
            // white line is right; green across it is the bug, and now it is visible
            // rather than something to be felt through the controls.
            if (socket != null && socket.TryBladeDirection(out Vector3 blade) &&
                blade.sqrMagnitude > 1e-8f)
            {
                Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.9f);
                Gizmos.DrawLine(_grip, _grip + blade.normalized * (_tip - _grip).magnitude);
            }
        }
    }
}
