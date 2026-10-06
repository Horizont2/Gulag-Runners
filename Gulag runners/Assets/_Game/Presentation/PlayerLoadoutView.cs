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

        [Tooltip("Weapon models, parented to the hand. Switched on one at a time.")]
        public ItemModel[] weapons;

        [Tooltip("Armour models, parented to the body.")]
        public ItemModel[] armour;

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
        ItemId _weapon = ItemId.None;
        ItemId _armour = ItemId.None;
        GameObject _weaponRoot;
        Renderer _weaponRenderer;

        void Reset() => player = GetComponentInParent<PlayerController>();

        void Awake()
        {
            if (player == null) player = GetComponentInParent<PlayerController>();
            ItemModels.Show(weapons, ItemId.None);
            ItemModels.Show(armour, ItemId.None);
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
                MaterialPropertyBlock block = null;
                if (root != null)
                    ItemModels.Tint(root.GetComponentInChildren<Renderer>(), ref block,
                                    ItemModels.Tier(tierColours, _armour), glow);
            }

            if (_weaponRenderer == null) return;

            Color colour = ItemModels.Tier(tierColours, _weapon);
            int left = inv.WeaponDurability;

            if (_weapon != ItemId.None && left > 0 && left <= Mathf.Max(1, wornAt))
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * wornPulse * Mathf.PI * 2f);
                colour = Color.Lerp(colour, wornColour, Mathf.Lerp(0.5f, 1f, pulse));
            }

            ItemModels.Tint(_weaponRenderer, ref _block, colour, glow);
        }
    }
}
