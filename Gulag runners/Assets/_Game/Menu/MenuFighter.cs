using System;
using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>One runner skin: the model, already in the scene, switched off.</summary>
    [Serializable]
    public class MenuSkin
    {
        [Tooltip("What this skin is called. Whatever the shop and the save file will call " +
                 "it — the name is the key, so it only has to agree with itself.")]
        public string id = "default";

        [Tooltip("The model, parented under the fighter and switched off. Nothing is " +
                 "instantiated: every skin a player can own is already here.")]
        public GameObject root;
    }

    /// <summary>
    /// The fighter standing in the menu.
    ///
    /// He is the shop. Gold in this game buys cosmetics and nothing else (docs/09), so the
    /// one thing the menu has to do well is show the skin the player owns, at full height,
    /// in good light, before they press anything.
    ///
    /// He is NOT a <see cref="PlayerController"/>. The simulation is not running here and
    /// there is nothing to collide with: this is a model, an animator and a pose. Putting
    /// a controller on him would start a motor with no world and draw him wherever its
    /// spawn logic decided.
    ///
    /// Nothing is created at play time. Every skin, every weapon, every piece of armour the
    /// menu can show is already parented here and switched off, which is the same rule the
    /// match follows and the only arrangement where a designer can see the whole wardrobe
    /// in the inspector.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class MenuFighter : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("The model's Animator. Found in the children when empty.")]
        public Animator animator;

        [Tooltip("What turns on the spot. Leave empty to turn this object.")]
        public Transform turntable;

        [Header("Pose")]
        [Tooltip("The animator state he stands in.\n\n" +
                 "A name rather than a clip, because that is how everything else in this " +
                 "project drives the animator (see PlayerAnimator) and because the menu " +
                 "and the match then share one controller: a skin posed here is posed in " +
                 "the same stance it will be seen fighting in.")]
        public string poseState = "Idle";

        [Tooltip("Hold a single frame instead of letting the state play.\n\n" +
                 "On for a portrait pose — a sword raised, a shoulder turned — which no " +
                 "idle loop will hold still for. Off for a breathing idle, which is better " +
                 "when the clip is made for it.")]
        public bool freeze;

        [Tooltip("Which frame, as a fraction of the clip. Only used while Freeze is on.")]
        [Range(0f, 1f)] public float poseFrame = 0.25f;

        [Tooltip("Playback speed while Freeze is off. Under 1 reads as calm, which is what " +
                 "a man waiting for a door to open should read as.")]
        [Range(0.1f, 2f)] public float playbackSpeed = 0.8f;

        [Header("Turntable")]
        [Tooltip("Rock slowly from side to side so the silhouette is never quite still.\n\n" +
                 "A sweep and not a full spin: a model revolving on the spot is a shop, and " +
                 "this is a man in a cell. A few degrees is enough to stop the frame looking " +
                 "like a screenshot.")]
        public bool sway = true;

        [Range(0f, 40f)] public float swayDegrees = 7f;
        [Range(0.05f, 1f)] public float swayPerSecond = 0.12f;

        [Tooltip("Which way he faces at rest, in degrees. 180 faces the camera for a model " +
                 "built looking along +Z.")]
        [Range(-180f, 180f)] public float facing = 180f;

        [Header("What he is wearing")]
        [Tooltip("Every runner skin, already in the scene and switched off.")]
        public MenuSkin[] skins = Array.Empty<MenuSkin>();

        [Tooltip("Which one is on. Empty shows the first in the list.")]
        public string skin = "";

        [Tooltip("Optional. The weapon he holds, from the same table the match uses, so a " +
                 "weapon skin reads the same in both places.")]
        public ItemModel[] weapons = Array.Empty<ItemModel>();
        public ItemId weapon = ItemId.None;

        [Tooltip("Optional. Armour, likewise.")]
        public ItemModel[] armour = Array.Empty<ItemModel>();
        public ItemId wearing = ItemId.None;

        [Header("Debug")]
        [Tooltip("What is actually on screen, written while playing.")]
        public string showing;

        float _sway;
        int _posed;

        void Reset()
        {
            animator = GetComponentInChildren<Animator>(true);
            turntable = transform;
        }

        void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>(true);
            if (turntable == null) turntable = transform;

            // The menu is not the simulation, so root motion has nothing to desync — but a
            // posed clip with root motion on walks the model out of its own cell.
            //
            // Only while playing: this runs in edit mode too, and a component that writes
            // to the scene the moment it loads leaves the scene permanently dirty, which
            // teaches everybody to ignore the asterisk.
            if (animator != null && Application.isPlaying) animator.applyRootMotion = false;
        }

        void Start() => Apply();

        void Update()
        {
            Pose();

            // Posing happens in edit mode as well, because a fighter who only stops
            // T-posing once you press play is a fighter nobody can dress. Swaying does
            // not: it writes a rotation every frame, and in edit mode that is an
            // unsaved change every frame.
            if (Application.isPlaying) Sway();
        }

        /// <summary>
        /// Shows the skin, the weapon and the armour that are selected, and poses him.
        ///
        /// Public and a context menu, so the scene can be dressed and looked at without
        /// pressing play: a wardrobe you have to enter play mode to see is a wardrobe
        /// nobody checks.
        /// </summary>
        [ContextMenu("Apply")]
        public void Apply()
        {
            GameObject worn = Skin(skin);
            ItemModels.Show(weapons, weapon);
            ItemModels.Show(armour, wearing);

            // The animator may belong to the skin that was just switched on.
            if (animator == null || !animator.gameObject.activeInHierarchy)
            {
                Animator found = worn != null
                    ? worn.GetComponentInChildren<Animator>(true)
                    : GetComponentInChildren<Animator>(true);
                if (found != null) animator = found;
            }

            _posed = 0;
            Pose();

            showing = $"{(worn != null ? worn.name : "no skin")} / {weapon} / {wearing}";
        }

        /// <summary>One skin on, the rest off. Returns the one that is showing.</summary>
        GameObject Skin(string id)
        {
            GameObject shown = null;
            if (skins == null) return null;

            bool none = string.IsNullOrEmpty(id);

            for (int i = 0; i < skins.Length; i++)
            {
                MenuSkin s = skins[i];
                if (s == null || s.root == null) continue;

                // An empty selection shows the FIRST one rather than nothing: a menu with
                // no character in it looks broken, and "no skin equipped" is not a state
                // this game has.
                bool on = none ? shown == null : s.id == id;
                if (on && shown == null) shown = s.root;
                else on = false;

                if (s.root.activeSelf != on) s.root.SetActive(on);
            }

            return shown;
        }

        /// <summary>Wears a skin by name, from a shop or a save.</summary>
        public void Wear(string id)
        {
            skin = id;
            Apply();
        }

        void Pose()
        {
            if (animator == null || string.IsNullOrEmpty(poseState)) return;

            int hash = Animator.StringToHash(poseState);
            if (!animator.HasState(0, hash))
            {
                if (_posed != -1)
                {
                    _posed = -1;
                    Debug.LogWarning(
                        $"{name}: the controller has no state called \"{poseState}\", so the " +
                        "fighter will hold whatever pose he arrived in. The names are the " +
                        "ones on PlayerAnimator — Idle, Idle Armed, Block Idle, Attack.",
                        this);
                }
                return;
            }

            if (freeze)
            {
                // Re-driven every frame, which is what holds a single frame still: a state
                // played once would carry on from there the moment anything ticks.
                animator.speed = 0f;
                animator.Play(hash, 0, poseFrame);
                Settle();
                _posed = hash;
                return;
            }

            animator.speed = playbackSpeed;
            if (_posed == hash) return;

            _posed = hash;
            animator.Play(hash, 0, 0f);
            Settle();
        }

        /// <summary>
        /// Makes the pose actually appear outside play mode.
        ///
        /// Animator.Play only queues a state; the animator evaluates it on its own update,
        /// and in edit mode there is no update to wait for. Without this the model stands
        /// in its bind pose in the scene view -- the T-pose -- however the field is set.
        /// </summary>
        void Settle()
        {
            if (!Application.isPlaying && animator != null) animator.Update(0f);
        }

        void Sway()
        {
            if (turntable == null) return;

            if (!sway)
            {
                turntable.localRotation = Quaternion.Euler(0f, facing, 0f);
                return;
            }

            _sway += Time.deltaTime * swayPerSecond * Mathf.PI * 2f;
            float angle = facing + Mathf.Sin(_sway) * swayDegrees;
            turntable.localRotation = Quaternion.Euler(0f, angle, 0f);
        }
    }
}
