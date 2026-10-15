using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using GulagRunners.Game;
using GulagRunners.Sim;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Wires a modular character's armour parts to the four armour items.
    ///
    /// The pack ships every part already skinned to one skeleton and sorted into categories —
    /// ARMOR PARTS/CHESTS, /ARMS, /LEGS and so on, each holding a type-and-colour variant per
    /// child. That is exactly the shape <see cref="PlayerArmourView"/> wants, so there is no
    /// art work here at all: pick one variant per category per item and record it.
    ///
    /// The mapping is by TYPE number, because the pack's types run light to heavy and docs/03's
    /// armour runs light to heavy too. Colour is per fighter, so the two of them read apart at
    /// a glance — which docs/02 asks for and which costs nothing when the parts are already
    /// authored in three colours.
    ///
    /// Re-running it rewrites the sets, so it is also "pick up the parts I have since added".
    /// Nothing in the character is moved, renamed or re-parented.
    /// </summary>
    public static class ArmourWiring
    {
        /// <summary>Which of the pack's armour types stands for each item, light to heavy.</summary>
        static readonly (ItemId Item, int Type)[] Mapping =
        {
            (ItemId.LeatherVest, 1),
            (ItemId.Shield,      2),   // the shield itself is held, not worn — see the log
            (ItemId.Chainmail,   3),
            (ItemId.Plate,       5),
        };

        /// <summary>Category object name -> which slot of the set it fills.</summary>
        static readonly string[] Categories = { "HEADS", "CHESTS", "ARMS", "BELTS", "LEGS", "FEET" };

        [MenuItem("Tools/Gulag Runners/Wire armour")]
        public static void Wire()
        {
            PlayerController[] fighters =
                Object.FindObjectsByType<PlayerController>(FindObjectsInactive.Include,
                                                           FindObjectsSortMode.None);
            if (fighters.Length == 0)
            {
                Debug.LogWarning("No PlayerController in this scene.");
                return;
            }

            System.Array.Sort(fighters, (a, b) => a.name.CompareTo(b.name));
            int dressed = 0;

            for (int f = 0; f < fighters.Length; f++)
            {
                PlayerController fighter = fighters[f];
                Transform root = FindArmourRoot(fighter.transform);
                if (root == null)
                {
                    Debug.LogWarning(
                        $"{fighter.name}: no \"ARMOR PARTS\" anywhere under it, so there is no " +
                        "modular character to dress. Drag the pack's character prefab under " +
                        "this fighter's Visual Root first, point Player Animator's Animator at " +
                        "its Animator, and run this again.", fighter);
                    continue;
                }

                // Colour 1 and 2 for the two fighters, so they read apart. Three exist.
                int colour = (f % 3) + 1;

                List<GameObject> all = new List<GameObject>();
                foreach (string category in Categories)
                {
                    Transform c = root.Find(category);
                    if (c == null) continue;
                    foreach (Transform part in c) all.Add(part.gameObject);
                }

                List<ArmourSet> sets = new List<ArmourSet>();

                // Bare. Trousers and boots of the lightest type, so an unarmoured fighter is
                // dressed rather than naked, and nothing above the waist — which is the whole
                // point: "found nothing yet" has to be visible from across the arena.
                ArmourSet bare = new ArmourSet { item = ItemId.None };
                bare.legs = Part(root, "LEGS", "Legs Armor", 1, colour);
                bare.feet = Part(root, "FEET", "Feet Armor", 1, colour);
                sets.Add(bare);

                foreach ((ItemId item, int type) in Mapping)
                {
                    ArmourSet set = new ArmourSet { item = item };
                    set.head  = Part(root, "HEADS",  "Head Armor",  type, colour);
                    set.chest = Part(root, "CHESTS", "Chest Armor", type, colour);
                    set.arms  = Part(root, "ARMS",   "Arm Armor",   type, colour);
                    set.belt  = Part(root, "BELTS",  "Belt Armor",  type, colour);
                    set.legs  = Part(root, "LEGS",   "Legs Armor",  type, colour);
                    set.feet  = Part(root, "FEET",   "Feet Armor",  type, colour);
                    sets.Add(set);
                }

                PlayerArmourView view = fighter.GetComponentInChildren<PlayerArmourView>(true);
                if (view == null) view = Undo.AddComponent<PlayerArmourView>(fighter.gameObject);

                Undo.RecordObject(view, "Wire armour");
                view.player = fighter;
                view.sets = sets.ToArray();
                view.allParts = all.ToArray();
                PrefabUtility.RecordPrefabInstancePropertyModifications(view);

                // Show the bare set now, so the scene view stops showing whichever variant the
                // pack happened to leave switched on.
                view.Wear(ItemId.None);

                dressed++;
                Debug.Log(
                    $"{fighter.name}: {all.Count} switchable part(s) across " +
                    $"{Categories.Length} categories, colour {colour}.\n" +
                    $"  bare = Type 1 legs and feet; " +
                    string.Join(", ", System.Array.ConvertAll(Mapping,
                        m => $"{m.Item} = Type {m.Type}")) + "\n" +
                    "  Shield is worn here as a look only — a shield is carried, and the " +
                    "off-hand model is the weapon socket's job, not this one's.", view);
            }

            if (dressed > 0)
                UnityEditor.SceneManagement.EditorSceneManager
                           .MarkSceneDirty(fighters[0].gameObject.scene);
        }

        /// <summary>The character's ARMOR PARTS node, wherever it hangs under the fighter.</summary>
        static Transform FindArmourRoot(Transform fighter)
        {
            foreach (Transform t in fighter.GetComponentsInChildren<Transform>(true))
                if (t.name == "ARMOR PARTS") return t;
            return null;
        }

        /// <summary>
        /// One variant out of a category, by type and colour.
        ///
        /// Matched on the name because that is what the pack encodes it in, and falling back
        /// to the first variant of the right type in any colour: the 1.0 and 1.1 sets do not
        /// offer every colour for every type, and a missing colour should cost a shade rather
        /// than a whole piece of armour.
        /// </summary>
        static GameObject Part(Transform root, string category, string prefix, int type, int colour)
        {
            Transform c = root.Find(category);
            if (c == null) return null;

            string exact = $"{prefix} Type {type} Color {colour}";
            string anyColour = $"{prefix} Type {type} Color ";

            GameObject fallback = null;
            foreach (Transform part in c)
            {
                if (part.name == exact) return part.gameObject;
                if (fallback == null && part.name.StartsWith(anyColour))
                    fallback = part.gameObject;
            }
            return fallback;
        }
    }
}
