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

        [Tooltip("Armour models, parented to the body. Optional: the weapon pack this project " +
                 "ships with has no armour meshes, so this is empty until there is art, and " +
                 "armour reads through the stat line rather than the silhouette until then.")]
        public ItemModel[] armour;


        [Header("Size")]
        [Tooltip("How long a weapon is drawn, as a share of the reach it ADDS over bare " +
                 "hands, in metres. 0 draws everything at the fallback length instead.\n\n" +
                 "Weapon packs are authored for a 1.8 m human; this fighter is 0.91 m, so a " +
                 "pack greataxe in his hand is longer than he is tall. Rather than guess one " +
                 "number for one pack, each model's own mesh is measured and its longest axis " +
                 "scaled to this. Taking the length from reach keeps the picture and the " +
                 "hitbox agreeing: docs/02 makes reach the whole difference between two " +
                 "weapons, and a spear drawn dagger-sized is that difference thrown away.")]
        [Range(0f, 3f)] public float reachToLength = 0.73f;

        [Tooltip("The grip and head every weapon has before it has any reach at all, as a " +
                 "share of the fighter's own height.\n\n" +
                 "Without it the map from reach to length is a straight line through the " +
                 "origin, and that is wrong at both ends at once: the dagger adds 0.10 m of " +
                 "reach, which drew it at the minimum and heaped it in with the mace and " +
                 "the gladius as the same small stick, while the warhammer came out at 119% " +
                 "of the fighter's height and the two polearms hit the cap at 143%. A real " +
                 "warhammer is about half its owner's height and a real spear about 120%.\n\n" +
                 "With a base, the same table draws 35% for a dagger and 127% for a scythe, " +
                 "in order and nothing clamped.")]
        [Range(0f, 0.8f)] public float baseOfBody = 0.27f;

        [Tooltip("Shortest a weapon may be drawn, in metres. A dagger adds almost nothing to " +
                 "bare reach, and a dagger you cannot see is not a weapon.")]
        [Range(0f, 0.6f)] public float minLength = 0.28f;

        [Tooltip("Longest a weapon may be drawn, in metres. The map from reach to length is " +
                 "linear, which over-rewards the two polearms: left uncapped a scythe comes " +
                 "out nearly twice the fighter's height.")]
        [Range(0f, 3f)] public float maxLength = 1.3f;

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

            // Sized before anything is shown, and once: the fit multiplies the model's own
            // scale, so running it twice would halve the weapon again.
            // Proportional to the fighter, not a number in metres: the one decision this
            // project has not taken yet is what a metre is, and a hard-coded haft would
            // have to be found and changed again on the day it does.
            float haft = baseOfBody * (player != null ? player.tuning.bodyHeight : 0.91f);

            ItemModels.FitAll(weapons, reachToLength, minLength, maxLength, fallbackLength,
                              haft);
            ItemModels.FitAll(armour, 0f, minLength, maxLength, fallbackLength);

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
                {
                    Color worn = ItemModels.RarityGlow(tierColours, _armour, Time.time,
                                                       glow, out float lit);
                    ItemModels.Tint(root.GetComponentInChildren<Renderer>(), ref block,
                                    worn, lit);
                }
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
