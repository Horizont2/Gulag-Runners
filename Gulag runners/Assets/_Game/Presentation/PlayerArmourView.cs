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

        [Header("Rarity")]
        [Tooltip("The rarity ramp: nothing, common, rare, legendary. The same colours the " +
                 "loot on the floor uses, so a helmet does not mean one thing lying there " +
                 "and another on a head.")]
        public Color[] tierColours = ItemModels.DefaultPalette();

        [Tooltip("How brightly worn gear glows, against the floor loot's own strength.\n\n" +
                 "Deliberately lower. Loot on the floor is SHOUTING — come and get me — and " +
                 "a fighter is not: his gear only has to say what it is. Worn pieces also " +
                 "do not pulse, because three breathing body parts on a moving character is " +
                 "noise rather than information. 0 switches the whole thing off without a " +
                 "code change, which is the setting to reach for if it reads as garish.")]
        [Range(0f, 2f)] public float glow = 0.5f;

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

            // What each worn piece is, in the one colour that reads across an arena. Set
            // once per change rather than every frame: there is no pulse to drive, and a
            // property block written to a skinned mesh every frame for nothing is a cost
            // paid on a phone.
            if (glow > 0f)
            {
                Paint(head, helmet);
                Paint(torso, chest);
                Paint(waist, belt);
            }

            if (logChanges)
                Debug.Log($"{name}: wearing {helmet} / {chest} / {belt}" +
                          (body == null ? " — and no base body set, so the legs are bare" : ""),
                          this);
        }

        /// <summary>
        /// Puts one item's rarity colour on the pieces worn for it.
        ///
        /// Time 0 on purpose: <see cref="ItemModels.RarityPulse"/> is a sine, and sin(0) is
        /// zero, so a fixed zero is exactly the middle of the breath and never moves. That
        /// is the one line of difference between gear on a body and loot on a floor.
        /// </summary>
        void Paint(ArmourSet set, ItemId item)
        {
            if (set?.pieces == null) return;

            Color colour = ItemModels.RarityGlow(tierColours, item, 0f, glow,
                                                 out float emission);
            if (item == ItemId.None) emission = 0f;    // bare skin does not glow

            for (int i = 0; i < set.pieces.Length; i++)
            {
                if (set.pieces[i] == null) continue;
                foreach (Renderer r in set.pieces[i].GetComponentsInChildren<Renderer>(true))
                    ItemModels.Tint(r, ref _paint, colour, emission);
            }
        }

        MaterialPropertyBlock _paint;

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
