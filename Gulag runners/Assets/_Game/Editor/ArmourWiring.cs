using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using GulagRunners.Game;
using GulagRunners.Sim;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Puts the pack's armour on whatever character the fighter is wearing, by re-binding it
    /// to that character's skeleton.
    ///
    /// The first attempt bolted the pack's NON-skinned pieces onto bones, which looked like
    /// the simpler road and is wrong for this pack: each of those prefabs is ONE mesh for a
    /// whole category, so "Arm Armor" is both sleeves in a single object. Hung on a forearm
    /// it puts both sleeves on one arm, and hung on each forearm it does it twice — which is
    /// exactly what "the armour goes on the wrong body parts" looks like. A both-arms mesh
    /// has no bone it can correctly belong to.
    ///
    /// The skinned copies inside the pack's own character do have an answer: every vertex
    /// already knows which bone moves it. So those are what is taken, and their bone array is
    /// re-pointed at this character's bones. Humanoid is what makes that possible — the two
    /// rigs name their bones differently (Spine1 and LeftForeArm here, spine_02 and
    /// lowerarm_l there) and agree only through the avatar. A source bone with no humanoid
    /// slot, which is every finger, falls back to the nearest ancestor that has one.
    ///
    /// What this CANNOT fix: the mesh keeps the bind pose it was modelled in, so armour cut
    /// for a muscular humanoid stays that shape on a stick figure. It will sit on the right
    /// limbs and move with them; it will not be tailored. That is a question about which
    /// character the game ships, not about this tool.
    ///
    /// Re-running it replaces what it made before.
    /// </summary>
    public static class ArmourWiring
    {
        const string CharacterPath =
            "Assets/URP GanzSe Free Modular Character Pack/Prefabs/Modular Character/" +
            "GanzSe Free Modular Character Update 1_1.prefab";

        const string HolderName = "Armour";

        /// <summary>One piece per category. Each is a whole garment, not a per-limb part.</summary>
        static readonly (string Folder, string Prefix)[] Categories =
        {
            ("HEADS",  "Head Armor"),
            ("CHESTS", "Chest Armor"),
            ("ARMS",   "Arm Armor"),
            ("BELTS",  "Belt Armor"),
            ("LEGS",   "Legs Armor"),
            ("FEET",   "Feet Armor"),
        };

        // The pack's categories ARE the slots, which is the happy part of this: a head
        // garment, a chest-and-arms garment and a belt garment, cut apart along lines the
        // artist already drew. Nothing overlaps, so the three can be worn at once.
        static readonly string[] Head = { "HEADS" };
        static readonly string[] Chest = { "CHESTS", "ARMS" };
        static readonly string[] Waist = { "BELTS" };

        /// <summary>What a bare fighter wears: the lightest trousers and boots, nothing above.
        /// No slot in the game replaces these, so they are always on, under everything.</summary>
        static readonly string[] BareBody = { "LEGS", "FEET" };

        /// <summary>
        /// THREE of each worn kind and no more, one per rarity, and which of the pack's
        /// numbered types each one is cut from.
        ///
        /// The pack ships a wardrobe. A wardrobe is the wrong answer here: docs/02 and
        /// docs/03 both want what a fighter is wearing readable from across the arena,
        /// because knowing the other fighter is in plate decides whether you take the
        /// exchange at all — and a dozen near-identical suits say nothing at a glance.
        /// Three per slot say it in one look, and they are the three the loot table rolls.
        ///
        /// Types 1, 3 and 5 rather than 1, 2, 3: spread across the pack's range so the
        /// three read as different kinds of protection rather than three shades of the
        /// same one. A slot's three share the type ladder, so a fighter in the legendary
        /// helm and the common vest is visibly mismatched — which he is.
        /// </summary>
        static readonly (ItemId Item, Rarity Rarity, int Type, string[] Folders)[] Mapping =
        {
            (ItemId.LeatherCap,  Rarity.Common,    1, Head),
            (ItemId.MailCoif,    Rarity.Rare,      3, Head),
            (ItemId.GreatHelm,   Rarity.Legendary, 5, Head),

            (ItemId.LeatherVest, Rarity.Common,    1, Chest),
            (ItemId.Chainmail,   Rarity.Rare,      3, Chest),
            (ItemId.Plate,       Rarity.Legendary, 5, Chest),

            (ItemId.RopeBelt,    Rarity.Common,    1, Waist),
            (ItemId.ToolBelt,    Rarity.Rare,      3, Waist),
            (ItemId.WarBelt,     Rarity.Legendary, 5, Waist),
        };

        /// <summary>The pack's file-name prefix for one of its category folders.</summary>
        static string PrefixOf(string folder)
        {
            foreach ((string f, string prefix) in Categories)
                if (f == folder) return prefix;
            return folder;
        }

        [MenuItem("Tools/Gulag Runners/Setup/Wire armour")]
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

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterPath);
            if (prefab == null)
            {
                Debug.LogWarning($"The modular character is not at {CharacterPath}, so there " +
                                 "is no skinned armour to take. Check the pack is imported.");
                return;
            }

            // One temporary copy of the pack's character, to lift the parts out of.
            GameObject donor = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            PrefabUtility.UnpackPrefabInstance(donor, PrefabUnpackMode.Completely,
                                               InteractionMode.AutomatedAction);
            try
            {
                Animator source = donor.GetComponentInChildren<Animator>(true);
                if (source == null || source.avatar == null || !source.avatar.isHuman)
                {
                    Debug.LogWarning("The pack's character has no Humanoid avatar, so its " +
                                     "bones cannot be matched to anything.");
                    return;
                }

                Dictionary<Transform, HumanBodyBones> slots = Slots(source);
                System.Array.Sort(fighters, (a, b) => a.name.CompareTo(b.name));

                for (int f = 0; f < fighters.Length; f++)
                    Dress(fighters[f], donor.transform, source, slots, (f % 3) + 1);
            }
            finally
            {
                Object.DestroyImmediate(donor);
            }

            UnityEditor.SceneManagement.EditorSceneManager
                       .MarkSceneDirty(fighters[0].gameObject.scene);
        }

        static void Dress(PlayerController fighter, Transform donor, Animator source,
                          Dictionary<Transform, HumanBodyBones> slots, int colour)
        {
            Animator target = fighter.GetComponentInChildren<Animator>(true);
            if (target == null || target.avatar == null || !target.avatar.isHuman)
            {
                Debug.LogWarning(
                    $"{fighter.name}: its character has no Humanoid avatar. Armour is matched " +
                    "bone to bone through Humanoid, so a Generic rig cannot wear any of this " +
                    "— set the model's Rig to Humanoid and run this again.", fighter);
                return;
            }

            Transform holder = fighter.transform.Find(HolderName);
            if (holder != null) Undo.DestroyObjectImmediate(holder.gameObject);

            GameObject holderGo = new GameObject(HolderName);
            Undo.RegisterCreatedObjectUndo(holderGo, "Wire armour");
            holderGo.transform.SetParent(fighter.transform, false);
            holder = holderGo.transform;

            Transform armourRoot = Find(donor, "ARMOR PARTS");
            if (armourRoot == null)
            {
                Debug.LogWarning("The pack's character has no \"ARMOR PARTS\" node.", fighter);
                return;
            }

            List<GameObject> all = new List<GameObject>();
            List<ArmourSet> sets = new List<ArmourSet>();
            List<string> report = new List<string>();

            // The base body first, and keyed by None: the trousers and boots that stay on
            // under every combination of the three slots. It goes in as its own pass rather
            // than falling out of the common suit, because no slot owns the legs now.
            List<GameObject> bareWorn = new List<GameObject>();
            foreach (string folder in BareBody)
            {
                GameObject piece = Take(armourRoot, folder, PrefixOf(folder), 1, colour,
                                        holder, source, target, slots, ItemId.None, report);
                if (piece == null) continue;

                bareWorn.Add(piece);
                all.Add(piece);
            }
            sets.Add(new ArmourSet { item = ItemId.None, pieces = bareWorn.ToArray() });

            foreach ((ItemId item, Rarity rarity, int type, string[] folders) in Mapping)
            {
                List<GameObject> worn = new List<GameObject>();
                report.Add($"{rarity}: {item} from the pack's type {type}, " +
                           $"{folders.Length} piece(s)");

                foreach (string folder in folders)
                {
                    GameObject piece = Take(armourRoot, folder, PrefixOf(folder), type, colour,
                                            holder, source, target, slots, item, report);
                    if (piece == null) continue;

                    worn.Add(piece);
                    all.Add(piece);
                }

                sets.Add(new ArmourSet { item = item, pieces = worn.ToArray() });
            }

            PlayerArmourView view = fighter.GetComponent<PlayerArmourView>();
            if (view == null) view = Undo.AddComponent<PlayerArmourView>(fighter.gameObject);

            Undo.RecordObject(view, "Wire armour");
            view.player = fighter;
            view.sets = sets.ToArray();
            view.allParts = all.ToArray();
            PrefabUtility.RecordPrefabInstancePropertyModifications(view);
            view.Wear(ItemId.None, ItemId.None, ItemId.None);

            Debug.Log(
                $"{fighter.name}: {all.Count} garment(s) in colour {colour}, " +
                $"{sets.Count} set(s), re-bound to this character's own bones.\n  " +
                string.Join("\n  ", report) + "\n" +
                "One piece per category, because each of the pack's parts is a whole garment " +
                "— \"Arm Armor\" is both sleeves in one mesh, which is why hanging it on a " +
                "forearm put it on the wrong arm.\n" +
                "The mesh keeps the bind pose it was modelled in, so it will sit on the right " +
                "limbs and move with them without being tailored to this body. Making it fit " +
                "is a question about which character ships, not about the wiring.", view);
        }

        /// <summary>Lifts one garment out of the donor and re-binds it to the fighter.</summary>
        static GameObject Take(Transform armourRoot, string folder, string prefix,
                               int type, int colour, Transform holder,
                               Animator source, Animator target,
                               Dictionary<Transform, HumanBodyBones> slots,
                               ItemId item, List<string> report)
        {
            Transform category = armourRoot.Find(folder);
            if (category == null)
            {
                report.Add($"{prefix}: no \"{folder}\" in the pack — skipped");
                return null;
            }

            Transform found = null;
            string exact = $"{prefix} Type {type} Color {colour}";
            string anyColour = $"{prefix} Type {type} Color ";

            foreach (Transform child in category)
            {
                if (child.name == exact) { found = child; break; }
                if (found == null && child.name.StartsWith(anyColour)) found = child;
            }

            if (found == null)
            {
                report.Add($"{prefix}: no \"{prefix} Type {type}\" — skipped");
                return null;
            }

            // Copied, not moved: four items share the donor and each needs its own.
            GameObject piece = Object.Instantiate(found.gameObject, holder);
            Undo.RegisterCreatedObjectUndo(piece, "Wire armour");
            piece.name = $"{item} {prefix}";
            piece.transform.localPosition = Vector3.zero;
            piece.transform.localRotation = Quaternion.identity;
            piece.transform.localScale = Vector3.one;

            int rebound = 0, guessed = 0;
            foreach (SkinnedMeshRenderer smr in
                     piece.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Transform[] bones = smr.bones;
                Transform[] mapped = new Transform[bones.Length];

                for (int i = 0; i < bones.Length; i++)
                {
                    mapped[i] = Match(bones[i], slots, target, out bool exactSlot);
                    if (mapped[i] != null) { rebound++; if (!exactSlot) guessed++; }
                }

                // Anything with no answer at all keeps the body's root, so the mesh is whole
                // rather than exploding towards the origin.
                Transform fallback = target.GetBoneTransform(HumanBodyBones.Hips);
                for (int i = 0; i < mapped.Length; i++)
                    if (mapped[i] == null) mapped[i] = fallback;

                smr.bones = mapped;
                smr.rootBone = Match(smr.rootBone, slots, target, out _) ?? fallback;
                smr.updateWhenOffscreen = true;     // the bounds are the donor's otherwise
                smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }

            report.Add($"{prefix} Type {type}: {rebound} bone(s) re-bound" +
                       (guessed > 0 ? $", {guessed} of them to an ancestor (fingers and such)"
                                    : ""));

            piece.SetActive(false);
            return piece;
        }

        /// <summary>
        /// This character's equivalent of one of the donor's bones.
        ///
        /// Walks up the donor's hierarchy when the bone itself has no humanoid slot, which is
        /// how a finger ends up driven by the hand: this character has no fingers, and a
        /// glove bound to nothing would collapse to the origin.
        /// </summary>
        static Transform Match(Transform bone, Dictionary<Transform, HumanBodyBones> slots,
                               Animator target, out bool exact)
        {
            exact = true;
            for (Transform t = bone; t != null; t = t.parent)
            {
                if (!slots.TryGetValue(t, out HumanBodyBones slot)) { exact = false; continue; }

                Transform mine = target.GetBoneTransform(slot);
                if (mine != null) return mine;
                exact = false;
            }
            return null;
        }

        /// <summary>Every humanoid slot this rig fills, as bone -> slot.</summary>
        static Dictionary<Transform, HumanBodyBones> Slots(Animator animator)
        {
            Dictionary<Transform, HumanBodyBones> found =
                new Dictionary<Transform, HumanBodyBones>();

            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                HumanBodyBones slot = (HumanBodyBones)i;
                Transform bone = animator.GetBoneTransform(slot);
                if (bone != null && !found.ContainsKey(bone)) found[bone] = slot;
            }
            return found;
        }

        static Transform Find(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }
    }
}
