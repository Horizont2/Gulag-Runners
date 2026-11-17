using UnityEngine;

namespace GulagRunners.Game
{
    /// <summary>
    /// Where a weapon sits in the fighter's hand.
    ///
    /// docs/03 makes the loadout information rather than decoration: the other player reads
    /// your silhouette across the room and decides whether to close. That only works if the
    /// weapon is actually ON the character, held, and moving with the hand that swings it.
    ///
    /// So this glues itself to a bone of the rig — the right hand by default — and everything
    /// parented under it goes along. The offset from that bone is set once here, in the
    /// inspector, with a gizmo to line it up by; each weapon model then keeps its own local
    /// position under this object, which is how a dagger and a greataxe end up gripped
    /// differently without a field per weapon.
    ///
    /// Nothing here is instantiated and nothing is hidden: drop a weapon pack's prefabs under
    /// this object, list them on <see cref="PlayerLoadoutView"/>, and the one being carried is
    /// the one switched on.
    /// </summary>
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class WeaponSocket : MonoBehaviour
    {
        [Header("Which rig")]
        [Tooltip("The character's Animator. Found in the fighter's children if left empty.")]
        public Animator rig;

        [Tooltip("Take the bone from the humanoid avatar. This is the way for any rig Unity " +
                 "reads as Humanoid — which is every character this project retargets its " +
                 "animations onto — because it survives swapping the model for another one.")]
        public bool useHumanoidBone = true;

        [Tooltip("Which bone, on a humanoid rig.")]
        public HumanBodyBones bone = HumanBodyBones.RightHand;

        [Tooltip("Use this transform instead, for a Generic rig or to hang from something the " +
                 "avatar does not name. Wins over the humanoid bone when set.\n\n" +
                 "Leave empty for a humanoid rig, which is every fighter in this project: the " +
                 "bone above is found through the avatar.")]
        public Transform boneOverride;

        [Tooltip("Last resort when neither of the above finds anything: the first child whose " +
                 "name contains this, case-insensitive. Logged when it is what ends up used, " +
                 "because a rig found by name is a rig that will move under you one day.")]
        public string boneNameContains = "hand";

        [Header("Offset from the bone")]
        public Vector3 localPosition;
        public Vector3 localEuler;
        public float localScale = 1f;

        [Header("Lining it up")]
        [Tooltip("Draw the socket in the scene view. A weapon held half a centimetre out of " +
                 "the fist reads as a bug from the camera this game is played at.")]
        public bool drawGizmo = true;
        [Range(0.01f, 0.5f)] public float gizmoSize = 0.08f;

        /// <summary>The bone this is hanging from, or null if it never found one.</summary>
        public Transform Bone { get; private set; }

        bool _warned;

        void OnEnable() => Attach();

        void LateUpdate()
        {
            // Every frame, so the offset can be dragged in the inspector while the game runs
            // and the weapon moves with it. It is two writes against an unanimated local
            // transform — the bone's own animation is the parent's business.
            if (Bone == null) Attach();
            Apply();
        }

        void OnValidate()
        {
            if (localScale <= 0f) localScale = 0.0001f;
            if (isActiveAndEnabled) Apply();
        }

        /// <summary>Parents something into the hand, keeping the offset it was authored with.</summary>
        public void Adopt(Transform child)
        {
            if (child == null || child == transform) return;
            if (child.parent == transform) return;
            child.SetParent(transform, false);        // false: keep its local offset, not its world one
        }

        void Attach()
        {
            Transform target = Resolve();
            if (target == null)
            {
                if (!_warned)
                {
                    _warned = true;
                    Debug.LogWarning(
                        $"{name}: no bone to hang the weapon from. Assign the rig's Animator, " +
                        "or a Bone Override for a Generic rig. The socket stays where it is, " +
                        "so the weapon will not follow the hand.", this);
                }
                return;
            }

            Bone = target;
            if (transform.parent != target) transform.SetParent(target, false);
            Apply();
        }

        Transform Resolve()
        {
            if (boneOverride != null) return boneOverride;

            if (rig == null) rig = GetComponentInParent<PlayerController>() is PlayerController p
                ? p.GetComponentInChildren<Animator>(true)
                : GetComponentInParent<Animator>();
            if (rig == null) rig = transform.root.GetComponentInChildren<Animator>(true);

            if (rig != null && useHumanoidBone && rig.isHuman)
            {
                Transform found = rig.GetBoneTransform(bone);
                if (found != null) return found;
            }

            if (rig != null && !string.IsNullOrEmpty(boneNameContains))
            {
                string want = boneNameContains.ToLowerInvariant();
                foreach (Transform t in rig.GetComponentsInChildren<Transform>(true))
                {
                    if (!t.name.ToLowerInvariant().Contains(want)) continue;
                    if (!_warned)
                    {
                        _warned = true;
                        Debug.LogWarning(
                            $"{name}: the avatar had no {bone}, so the socket is hanging from " +
                            $"\"{t.name}\", matched by name. It works, and it will break the " +
                            "day the model is swapped for one that names its bones differently.",
                            this);
                    }
                    return t;
                }
            }

            return null;
        }

        void Apply()
        {
            transform.localPosition = localPosition;
            transform.localRotation = Quaternion.Euler(localEuler);
            transform.localScale = Vector3.one * localScale;
        }

        // ---------------------------------------------------------------- the blade

        Transform _measured;
        ModelShape.Grip _shape;
        bool _haveShape;

        GameObject _held;
        Vector3 _heldBlade;
        Vector3 _heldFlat;

        /// <summary>
        /// Which way the weapon in this hand points and which way it is flat, in world
        /// space. False when the model will not say — see <see cref="ModelShape"/>, which
        /// owns the measurement and the argument for it.
        ///
        /// Used by <see cref="PlayerArm"/> to turn the hand so the drawn weapon lies along
        /// the one the simulation is swinging, and so its flat faces the player rather than
        /// whichever way the animation clip happened to leave the wrist. Without the first
        /// the hand was forced to a world LookRotation, which has no relation to how a hand
        /// grips anything; without the second a sword could be drawn edge-on.
        ///
        /// <paramref name="flat"/> comes back zero for a round weapon — a mace, a club —
        /// which is not a failure: their roll is free, and leaving it to the clip looks
        /// better than deciding it.
        /// </summary>
        public bool TryGrip(out Vector3 blade, out Vector3 flat)
        {
            blade = transform.forward;
            flat = Vector3.zero;

            Transform model = ActiveModel();
            if (model == null) return false;

            if (model != _measured)
            {
                _measured = model;
                _haveShape = ModelShape.Measure(model.gameObject, out _shape);
            }

            // Authored first, measured second. The authored pair arrives through Hold,
            // from the ItemModel entry the loadout is showing, so a model the measurement
            // cannot read is fixed in the inspector rather than in here.
            bool mine = _held != null && _held == model.gameObject;
            Vector3 bladeLocal = mine && _heldBlade.sqrMagnitude > 1e-6f
                ? _heldBlade.normalized
                : _haveShape ? _shape.Blade : Vector3.zero;
            if (bladeLocal.sqrMagnitude <= 1e-8f) return false;

            Vector3 flatLocal = mine && _heldFlat.sqrMagnitude > 1e-6f
                ? _heldFlat.normalized
                : _haveShape ? _shape.Flat : Vector3.zero;

            blade = model.TransformDirection(bladeLocal);
            if (blade.sqrMagnitude <= 1e-8f) return false;

            if (flatLocal.sqrMagnitude > 1e-8f) flat = model.TransformDirection(flatLocal);
            return true;
        }

        /// <summary>
        /// What is being carried, and the grip it was authored with.
        ///
        /// Called by <see cref="PlayerLoadoutView"/> when the weapon changes, because that
        /// is what knows which <see cref="ItemModel"/> entry a model came from. Both axes
        /// may be zero, which asks for the measurement — and is how every weapon in the
        /// project is set up, the measurement being right for all of them.
        /// </summary>
        public void Hold(GameObject model, Vector3 bladeAxis, Vector3 flatAxis)
        {
            _held = model;
            _heldBlade = bladeAxis;
            _heldFlat = flatAxis;
        }

        /// <summary>The weapon currently switched on under this socket.</summary>
        Transform ActiveModel()
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (child.gameObject.activeInHierarchy) return child;
            }
            return null;
        }

        void OnDrawGizmos()
        {
            if (!drawGizmo) return;

            // A cross rather than a sphere: the thing being lined up is an orientation as much
            // as a point, and a sphere says nothing about which way the blade will face.
            Gizmos.color = new Color(1f, 0.78f, 0.25f, 0.9f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawLine(Vector3.zero, Vector3.right * gizmoSize);
            Gizmos.DrawLine(Vector3.zero, Vector3.up * gizmoSize * 2f);     // the grip's "up"
            Gizmos.DrawLine(Vector3.zero, Vector3.forward * gizmoSize);
            Gizmos.DrawWireCube(Vector3.zero, Vector3.one * gizmoSize * 0.35f);
            Gizmos.matrix = Matrix4x4.identity;

            // And what the grip was actually read as, which is the thing that goes wrong
            // quietly. Yellow along the weapon, blue out of its flat; no yellow at all means
            // the model would not say and the hand is left to the clip.
            if (!TryGrip(out Vector3 blade, out Vector3 flat)) return;

            Gizmos.color = new Color(1f, 0.95f, 0.4f, 0.9f);
            Gizmos.DrawLine(transform.position,
                            transform.position + blade.normalized * gizmoSize * 6f);
            if (flat.sqrMagnitude <= 1e-8f) return;

            Gizmos.color = new Color(0.4f, 0.7f, 1f, 0.9f);
            Gizmos.DrawLine(transform.position,
                            transform.position + flat.normalized * gizmoSize * 2f);
        }
    }
}
