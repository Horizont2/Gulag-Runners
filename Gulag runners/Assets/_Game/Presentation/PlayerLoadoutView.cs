using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// Puts what the player is carrying onto the player.
    ///
    /// docs/03 makes this a rule rather than a flourish: everything is visible on the character,
    /// and the silhouette is information the OTHER player reads. A fighter in chainmail with a
    /// spear is a different problem to a fighter with their fists, and that has to be legible
    /// across the room before the first exchange, not discovered during it.
    ///
    /// The same doc asks for a tell when a weapon is nearly spent. Here that is the weapon going
    /// red as it runs out: the owner reads it as "stop opening chests with this", and the
    /// opponent reads it as "they are about to be unarmed".
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerLoadoutView : MonoBehaviour
    {
        [Header("Wiring")]
        public PlayerController player;

        [Tooltip("Where the weapon is held. Leave it empty and the models stay wherever they " +
                 "are parented; set it and they are moved into the hand at load, keeping the " +
                 "local offset each was authored with — which is how a dagger and a greataxe " +
                 "end up gripped differently.")]
        public WeaponSocket socket;

        [Tooltip("Weapon models, one switched on at a time: the one actually being carried.\n\n" +
                 "Drop a weapon pack's prefabs under the socket, list them here against the " +
                 "item each one is, and that is the whole wiring. An item with no model here " +
                 "simply shows nothing, so a half-filled list is a half-dressed fighter rather " +
                 "than an error. Optional altogether: an empty list is a scene that is not " +
                 "about loot — the movement sandbox of docs/11, say.")]
        public ItemModel[] weapons;

        [Tooltip("CHEST armour models only, parented to the body, one mesh per item. The " +
                 "older of the two armour paths and the narrower one: the fighter wears a " +
                 "helmet and a belt as well, and those go through PlayerArmourView, which " +
                 "dresses all three worn slots at once from a modular character's own part " +
                 "categories. Fill one or the other, not both. Optional: the weapon pack " +
                 "this project ships with has no armour meshes at all.")]
        public ItemModel[] armour;


        [Header("Size")]
        // How long a weapon is DRAWN is ArmMotor.BladeLength, off the match's own arm
        // tuning, and it is not repeated here. It used to be: four numbers on this
        // component, four more on GroundItemView, and a tooltip on one of them asking
        // whoever edited it to keep the other in step by hand. Meanwhile the simulation
        // swung a blade built from the item's whole Reach, which is a different length
        // again -- about 1.5x the model, on every weapon in the table. A hitbox that is
        // not the thing you can see is the worst kind of wrong, so there is one rule now
        // and the sim owns it.

        [Tooltip("Longest axis, in metres, for anything with no reach of its own — armour, the " +
                 "bandage, a stand-in cube.")]
        [Range(0f, 2f)] public float fallbackLength = 0.3f;

        [Header("Tier")]
        public Color[] tierColours = ItemModels.DefaultPalette();
        [Range(0f, 4f)] public float glow = 0.6f;

        [Header("Nearly spent (docs/03: the weapon is a tell)")]
        [Tooltip("At or below this many uses left, the weapon reads as about to break.")]
        public int wornAt = 3;

        public Color wornColour = new Color(1f, 0.25f, 0.2f);

        [Tooltip("Pulses per second while worn. The opponent is meant to notice it.")]
        public float wornPulse = 3f;

        MaterialPropertyBlock _block;
        MaterialPropertyBlock _armourBlock;
        Renderer _armourRenderer;
        ItemId _weapon = ItemId.None;
        ItemId _armour = ItemId.None;
        GameObject _weaponRoot;
        Renderer _weaponRenderer;

        void Reset()
        {
            player = GetComponentInParent<PlayerController>();
            if (socket == null && player != null)
                socket = player.GetComponentInChildren<WeaponSocket>(true);
        }

        void Awake()
        {
            if (player == null) player = GetComponentInParent<PlayerController>();

            // Into the hand before anything is shown, so the first frame a weapon appears it
            // is already being held rather than standing on the floor next to its owner.
            if (socket != null && weapons != null)
                foreach (ItemModel m in weapons)
                    if (m != null && m.root != null) socket.Adopt(m.root.transform);

            ItemModels.Show(weapons, ItemId.None);
            ItemModels.Show(armour, ItemId.None);
        }

        /// <summary>
        /// Sized once, and in Start rather than Awake.
        ///
        /// The socket parents itself to a hand bone in its own OnEnable, and the order of
        /// that against another component's Awake is not defined. Fitted first, a model is
        /// measured while it is still hanging off this object instead of off the hand, and
        /// the parent scale the fit divides out is then the wrong one -- which is a weapon
        /// that comes out half size or double, depending on which component woke first.
        /// By Start every Awake and OnEnable in the scene has run.
        ///
        /// Once, because the fit MULTIPLIES the model's own scale: running it twice halves
        /// the weapon again.
        /// </summary>
        void Start()
        {
            // Off the match's own arm tuning, through the simulation's own function, so
            // the weapon drawn in the hand is the weapon the hitbox is.
            MoveConfig move = player != null ? player.Config : MoveConfig.Default();
            ArmConfig arm = player != null ? player.ArmSettings : ArmConfig.Default();

            ItemModels.FitAll(weapons, in move, in arm, fallbackLength);
            ItemModels.FitAll(armour, fallbackLength);
        }

        void LateUpdate()
        {
            if (player == null) return;

            Inventory inv = player.State.Inventory;

            if (inv.Weapon != _weapon)
            {
                _weapon = inv.Weapon;
                _weaponRoot = ItemModels.Show(weapons, _weapon);
                _weaponRenderer = _weaponRoot != null
                    ? _weaponRoot.GetComponentInChildren<Renderer>() : null;
                _block = null;
            }

            if (inv.Armour != _armour)
            {
                _armour = inv.Armour;
                GameObject root = ItemModels.Show(armour, _armour);
                _armourRenderer = root != null
                    ? root.GetComponentInChildren<Renderer>() : null;
                _armourBlock = null;
            }

            // Every frame, not only on the frame it changed. Tinted once, a legendary suit
            // was frozen at whatever phase of the breath it happened to be shown on, so the
            // one item in the game that is supposed to be the only thing moving stood still
            // — while the weapon in the same hand pulsed, from the same function.
            if (_armourRenderer != null)
            {
                Color worn = ItemModels.RarityGlow(tierColours, _armour, Time.time, glow,
                                                   out float lit);
                ItemModels.Tint(_armourRenderer, ref _armourBlock, worn, lit);
            }

            if (_weaponRenderer == null) return;

            Color colour = ItemModels.RarityGlow(tierColours, _weapon, Time.time, glow,
                                                 out float emission);
            int left = inv.WeaponDurability;

            if (_weapon != ItemId.None && left > 0 && left <= Mathf.Max(1, wornAt))
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * wornPulse * Mathf.PI * 2f);
                colour = Color.Lerp(colour, wornColour, Mathf.Lerp(0.5f, 1f, pulse));
            }

            ItemModels.Tint(_weaponRenderer, ref _block, colour, emission);
        }
    }
}
