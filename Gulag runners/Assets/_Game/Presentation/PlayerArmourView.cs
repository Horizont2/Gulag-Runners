using System;
using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>One armour item, as the set of pieces the fighter wears for it.</summary>
    [Serializable]
    public class ArmourSet
    {
        [Tooltip("The item this set is worn for. None is the BASE body — the trousers and " +
                 "boots nothing ever replaces, which is the silhouette that says \"nothing " +
                 "found yet\". It is always on, under whatever else is.")]
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
    /// Three slots at once, not one. The fighter wears a helmet, a chest and a belt, and
    /// they are worn TOGETHER: the sets are unioned over the base body rather than replacing
    /// each other. That is safe because the pack's categories do not overlap — the helmet is
    /// the HEADS garment, the chest is CHESTS and ARMS, the belt is BELTS, and the base body
    /// is LEGS and FEET — so no two sets ever claim the same mesh. A set that did would
    /// simply win, since switching a part on twice is switching it on.
    ///
    /// <see cref="PlayerLoadoutView.armour"/> is the older, single-model path, for a pack that
    /// hands out one mesh per armour. Fill one or the other, not both.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerArmourView : MonoBehaviour
    {
        [Header("Wiring")]
        public PlayerController player;

        [Tooltip("One set per worn item — helmets, chest armour and belts alike — plus one " +
                 "for None, which is the base body. Tools > Gulag Runners > Wire armour " +
                 "fills these in from a modular character's own part categories.")]
        public ArmourSet[] sets = Array.Empty<ArmourSet>();

        [Tooltip("Every switchable part on the character, including the ones no set uses.\n\n" +
                 "Needed because a modular character arrives with some arbitrary variant " +
                 "already switched on, and because changing armour has to take the previous " +
                 "one OFF. Blanking a known list is the only way to do that without walking " +
                 "the hierarchy every time the slot changes.")]
        public GameObject[] allParts = Array.Empty<GameObject>();

        [Tooltip("Log each change. Useful once, while wiring.")]
        public bool logChanges;

        // Nothing valid, so the first frame always dresses.
        ItemId _helmet = (ItemId)255, _chest = (ItemId)255, _belt = (ItemId)255;

        void Reset() => player = GetComponentInParent<PlayerController>();

        void Start()
        {
            if (player == null) player = GetComponentInParent<PlayerController>();
            if (player == null) Wear(ItemId.None, ItemId.None, ItemId.None);
            else Wear(player.State.Inventory);
        }

        void LateUpdate()
        {
            if (player == null) return;
            Wear(player.State.Inventory);
        }

        /// <summary>Dresses the fighter in whatever the five-slot inventory has worn.</summary>
        public void Wear(in Inventory inv) => Wear(inv.Helmet, inv.Armour, inv.Belt);

        /// <summary>
        /// Switches the body over to the three worn slots at once. Safe to call every frame
        /// and with anything: it returns immediately unless something actually changed.
        /// </summary>
        public void Wear(ItemId helmet, ItemId chest, ItemId belt)
        {
            if (helmet == _helmet && chest == _chest && belt == _belt) return;
            _helmet = helmet; _chest = chest; _belt = belt;

            // The base body is always on, under everything. It is the trousers and the
            // boots: no slot in the game replaces them, so nothing switches them off.
            ArmourSet body = Find(ItemId.None);
            ArmourSet head = Find(helmet);
            ArmourSet torso = Find(chest);
            ArmourSet waist = Find(belt);

            for (int i = 0; i < allParts.Length; i++)
            {
                GameObject part = allParts[i];
                if (part == null) continue;

                bool on = (body != null && body.Has(part)) ||
                          (head != null && head.Has(part)) ||
                          (torso != null && torso.Has(part)) ||
                          (waist != null && waist.Has(part));

                if (part.activeSelf != on) part.SetActive(on);
            }

            if (logChanges)
                Debug.Log($"{name}: wearing {helmet} / {chest} / {belt}" +
                          (body == null ? " — and no base body set, so the legs are bare" : ""),
                          this);
        }

        /// <summary>
        /// The set for one item, or null. An empty slot finds the base body set, which is
        /// already on — so an empty slot contributes nothing, which is what empty means.
        /// </summary>
        ArmourSet Find(ItemId item)
        {
            for (int i = 0; i < sets.Length; i++)
                if (sets[i] != null && sets[i].item == item) return sets[i];
            return null;
        }
    }
}
