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

        [Tooltip("How strongly the hand is TWISTED onto the weapon's goal.\n\n" +
                 "1, and not the 0.85 it used to be. Unity blends from the clip's own " +
                 "wrist to the goal by this, so anything short of 1 leaves that share of " +
                 "the correction UNDONE — and the correction is no longer a nudge. It now " +
                 "carries the weapon's roll as well as its line, and the clip's roll is " +
                 "arbitrary, so a tenth of it left standing is a visibly crooked weapon.\n\n" +
                 "Turn it down to see what the clips alone were doing; leave it at 1 to " +
                 "play, because the goal is the hitbox and the hitbox is not negotiable.")]
        [Range(0f, 1f)] public float rotationWeight = 1f;

        [Tooltip("How much of the arm's aim the fighter FOLLOWS at all. 0 switches the " +
                 "whole thing off and the body ignores the weapon entirely.\n\n" +
                 "What this actually buys is the head: a fighter who never looks at " +
                 "what he is swinging reads as bolted down. The torso is the part that " +
                 "makes the legs skate, and it has its own share below.")]
        [Range(0f, 1f)] public float bodyFollow = 0.6f;

        [Tooltip("How much of that the TORSO takes, against the head.\n\n" +
                 "Split out because these two were one number doing both jobs, and the " +
                 "complaint was the body bending as the stick turns. They want opposite " +
                 "settings: the head should follow the blade almost fully, because that " +
                 "is a fighter watching his own weapon, and the chest should barely move " +
                 "at all, because the hips come with it and a fighter whose hips follow " +
                 "his sword cannot stand on a ledge.\n\n" +
                 "0 is a torso that never turns and a head that still does. Put it up " +
                 "only if the fighter looks stiff from the side.")]
        [Range(0f, 1f)] public float torsoFollow = 0.05f;

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

        [Tooltip("Also roll the weapon so its FLAT faces the player.\n\n" +
                 "This is a side-on game: a sword turned edge-on to the camera is a line, " +
                 "and an axe head pointed into the screen is a stick. Putting the blade on " +
                 "the arm's line leaves that roll free, and free meant whatever the clip's " +
                 "wrist happened to be doing — the same weapon flat-on in one pose and " +
                 "edge-on in the next.\n\n" +
                 "Only weapons with a flat to face are touched; a mace has none and keeps " +
                 "the clip's own roll. Turn it off to see what the clips alone do.")]
        public bool faceTheCamera = true;

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
            // Overall weight, then BODY, then head, then eyes, then the clamp. The
            // second and third used to be the same field, so asking for less bend in
            // the chest also took the head off the weapon and the fighter went dead
            // behind the eyes. They want opposite settings and now have them.
            animator.SetLookAtWeight(bodyFollow, torsoFollow, 1f, 0f, lookClamp);
            animator.SetLookAtPosition(at);
        }

        /// <summary>
        /// Turns the hand so the WEAPON lies along the arm the simulation is swinging,
        /// and so the player is looking at the flat of it rather than at its edge.
        ///
        /// Applied on top of whatever the clip and the grip already produced, so the fist
        /// keeps holding the thing the way it was authored to and only swings onto the
        /// right line.
        ///
        /// It used to be a world LookRotation down the arm, which forced the hand's own
        /// +Z onto the blade line. A hand bone's forward is not the direction anything is
        /// held in, so the weapon sat crooked in the fist, and it went wrong in a
        /// different way for every clip because the correction ignored the pose entirely.
        ///
        /// Then it was the SMALLEST rotation that puts the blade on the line, which is
        /// right about the blade and says nothing about the roll -- and the roll is a
        /// whole axis of "hangs crooked". The smallest rotation leaves it wherever the
        /// clip had the wrist, so the same sword was flat-on in one pose and edge-on in
        /// the next, and an axe head pointed into the screen as often as across it.
        ///
        /// So the rotation is built from both axes at once when the weapon has a flat:
        ///
        ///   the BLADE goes on the arm's line, which is the hitbox and is not negotiable;
        ///   the FLAT faces the camera, which is the only direction in a side-on game
        ///       that shows the player what they are holding.
        ///
        /// Which of the two faces is immaterial -- a blade looks the same from either
        /// side -- so the choice is made by the fighter's FACING, and only by that. It
        /// has to be something that cannot change mid-swing: pick by anything the arm is
        /// doing and the weapon flips over in the middle of a blow.
        ///
        /// Round weapons have no flat, and ModelShape says so rather than inventing one.
        /// There the smallest rotation is exactly right: nothing about a mace's roll is
        /// worth taking off the clip.
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
            // own wrist is better than anything computed here. SAID OUT LOUD, with a
            // weight of zero, rather than returning: an IK weight stands until it is
            // written again, so a silent return left the hand twisted for the weapon it
            // was holding a moment ago -- which is how putting down a sword and picking
            // up an axe left the axe sitting at the sword's angle.
            if (socket == null || !socket.TryGrip(out Vector3 blade, out Vector3 flat) ||
                blade.sqrMagnitude <= 1e-8f)
            {
                animator.SetIKRotationWeight(goal, 0f);
                return;
            }

            blade.Normalize();
            Quaternion swing;

            if (faceTheCamera && flat.sqrMagnitude > 1e-8f)
            {
                // Square the two up before they are used as a basis. They are the model's
                // own axes and so already square, until somebody scales a weapon unevenly
                // and hands us a skewed pair.
                flat = flat - blade * Vector3.Dot(flat, blade);
                if (flat.sqrMagnitude > 1e-8f)
                {
                    flat.Normalize();
                    float side = player.State.Facing < 0 ? -1f : 1f;
                    Vector3 toCamera = Vector3.forward * side;

                    // The rotation that takes (flat, blade) onto (toCamera, want): go back
                    // through the model's own basis and out through the one we want.
                    swing = Quaternion.LookRotation(toCamera, want)
                            * Quaternion.Inverse(Quaternion.LookRotation(flat, blade));
                }
                else
                {
                    swing = Quaternion.FromToRotation(blade, want);
                }
            }
            else
            {
                swing = Quaternion.FromToRotation(blade, want);
            }

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
            if (socket != null && socket.TryGrip(out Vector3 blade, out Vector3 flat) &&
                blade.sqrMagnitude > 1e-8f)
            {
                Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.9f);
                Gizmos.DrawLine(_grip, _grip + blade.normalized * (_tip - _grip).magnitude);

                // And which way it is flat, which is the other half of sitting straight in
                // the fist. This one should point at the camera; across the screen means
                // the player is being shown the edge of a blade.
                if (flat.sqrMagnitude > 1e-8f)
                {
                    Gizmos.color = new Color(0.4f, 0.7f, 1f, 0.9f);
                    Gizmos.DrawLine(_grip, _grip + flat.normalized * 0.15f);
                }
            }
        }
    }
}
