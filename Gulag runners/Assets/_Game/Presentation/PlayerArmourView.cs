using System;
using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>One armour item, as the set of pieces the fighter wears for it.</summary>
    [Serializable]
    public class ArmourSet
    {
        [Tooltip("The item this set is worn for. None is what the fighter wears with an empty " +
                 "armour slot — the silhouette that says \"nothing found yet\".")]
        public ItemId item = ItemId.None;

        [Tooltip("Everything worn for this item, already in the scene and switched off.\n\n" +
                 "A flat list rather than one slot per body part, because the parts come in " +
                 "pairs: two bracers, two greaves, two boots. Any entry left empty simply " +
                 "shows nothing, so a half-filled set is a half-dressed fighter rather than " +
                 "an error.")]
        public GameObject[] pieces = Array.Empty<GameObject>();

        public bool Has(GameObject go)
        {
            if (go == null || pieces == null) return false;
            for (int i = 0; i < pieces.Length; i++)
                if (pieces[i] == go) return true;
            return false;
        }
    }

    /// <summary>
    /// Dresses the fighter in whatever is in the armour slot.
    ///
    /// docs/02 and docs/03 both ask for the same thing and ask for it twice: everything a
    /// fighter is carrying is readable from across the arena, and armour above all, because
    /// knowing the other player is in plate changes whether you take the exchange at all. A
    /// number in a stat line cannot do that. A silhouette can.
    ///
    /// Nothing is instantiated at runtime. Every piece the fighter can ever wear is already
    /// parented to a bone, scaled, and switched off; this turns the right ones on. That is the
    /// same rule the weapon models follow, and it is the only arrangement in which a designer
    /// can see the whole wardrobe in the inspector.
    ///
    /// Rigid pieces on bones rather than skinned meshes, deliberately. A modular pack's
    /// skinned armour is bound to that pack's own skeleton and modelled around that pack's own
    /// body; re-bound to another character it attaches in the right places and still carries
    /// the wrong proportions. The fighter in this game is a stick figure, so a plate chest
    /// skinned for a muscular humanoid would hang in the air around it. A bolted-on piece, on
    /// the other hand, is exactly what armour is.
    ///
    /// <see cref="PlayerLoadoutView.armour"/> is the older, single-model path, for a pack that
    /// hands out one mesh per armour. Fill one or the other, not both.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerArmourView : MonoBehaviour
    {
        [Header("Wiring")]
        public PlayerController player;

        [Tooltip("One set per armour item, plus one for None. Tools > Gulag Runners > Wire " +
                 "armour fills these in from a modular character's own part categories.")]
        public ArmourSet[] sets = Array.Empty<ArmourSet>();

        [Tooltip("Every switchable part on the character, including the ones no set uses.\n\n" +
                 "Needed because a modular character arrives with some arbitrary variant " +
                 "already switched on, and because changing armour has to take the previous " +
                 "one OFF. Blanking a known list is the only way to do that without walking " +
                 "the hierarchy every time the slot changes.")]
        public GameObject[] allParts = Array.Empty<GameObject>();

        [Tooltip("Log each change. Useful once, while wiring.")]
        public bool logChanges;

        ItemId _worn = (ItemId)255;        // nothing valid, so the first frame always dresses

        void Reset() => player = GetComponentInParent<PlayerController>();

        void Start()
        {
            if (player == null) player = GetComponentInParent<PlayerController>();
            Wear(player != null ? player.State.Inventory.Armour : ItemId.None);
        }

        void LateUpdate()
        {
            if (player == null) return;

            ItemId now = player.State.Inventory.Armour;
            if (now != _worn) Wear(now);
        }

        /// <summary>Switches the body over to one item's set. Safe to call with anything.</summary>
        public void Wear(ItemId item)
        {
            _worn = item;

            ArmourSet set = Find(item);
            if (set == null && item != ItemId.None) set = Find(ItemId.None);

            for (int i = 0; i < allParts.Length; i++)
            {
                GameObject part = allParts[i];
                if (part == null) continue;

                bool on = set != null && set.Has(part);
                if (part.activeSelf != on) part.SetActive(on);
            }

            if (logChanges)
                Debug.Log($"{name}: wearing {item}" +
                          (set == null ? " — no set for it, and no None set either" : ""), this);
        }

        ArmourSet Find(ItemId item)
        {
            for (int i = 0; i < sets.Length; i++)
                if (sets[i] != null && sets[i].item == item) return sets[i];
            return null;
        }
    }
}
