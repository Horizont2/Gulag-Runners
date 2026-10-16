using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using GulagRunners.Game;
using GulagRunners.Sim;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Bolts armour onto whatever character the fighter is actually wearing.
    ///
    /// Not the pack's own character. A modular pack's SKINNED armour is bound to that pack's
    /// skeleton and modelled around that pack's body, so on another character it attaches in
    /// the right places and still carries the wrong proportions — and the fighter here is a
    /// stick figure, which no humanoid chest plate was ever shaped for. The pack also ships
    /// every piece as a NON-SKINNED prefab, 108 of them, and a rigid piece bolted to a bone is
    /// what armour actually is.
    ///
    /// Bones are found through the Humanoid avatar rather than by name, because the two packs
    /// name them differently: this character calls them Spine1 and LeftForeArm, the armour's
    /// own came from a rig that calls them spine_02 and lowerarm_l. Humanoid is the one thing
    /// they agree on.
    ///
    /// Placement is MEASURED, never guessed. Each piece is scaled so its longest axis matches
    /// the bone segment it covers and centred on that segment, which lands it close enough to
    /// be judged. What it cannot do is have taste: the pieces are plain transforms once
    /// created, and the last centimetre is a drag in the scene view.
    ///
    /// Re-running it reuses pieces it made before rather than piling up copies.
    /// </summary>
    public static class ArmourWiring
    {
        const string PartsFolder =
            "Assets/URP GanzSe Free Modular Character Pack/Prefabs/Non-Skinned Mesh Parts/Armor Parts";

        /// <summary>Which of the pack's armour types stands for each item, light to heavy.</summary>
        static readonly (ItemId Item, int Type)[] Mapping =
        {
            (ItemId.LeatherVest, 1),
            (ItemId.Shield,      2),
            (ItemId.Chainmail,   3),
            (ItemId.Plate,       5),
        };

        /// <summary>
        /// One piece: which part of the pack it comes from, the bone it hangs on, the bone it
        /// points at, and how much of that segment it should cover.
        ///
        /// The tip bone is what makes this measurable. A bracer covers forearm to hand, a
        /// greave shin to foot; with both ends known the length is a subtraction rather than a
        /// number somebody typed.
        /// </summary>
        struct Slot
        {
            public string Category;          // "Arm Armor", "Chest Armor", ...
            public HumanBodyBones Bone;
            public HumanBodyBones Tip;
            public float Cover;              // share of the segment the piece spans
            public string Label;
        }

        static readonly Slot[] Slots =
        {
            new Slot { Category = "Head Armor",  Bone = HumanBodyBones.Head,
                       Tip = HumanBodyBones.Neck,          Cover = 1.4f, Label = "Head" },
            new Slot { Category = "Chest Armor", Bone = HumanBodyBones.Chest,
                       Tip = HumanBodyBones.Neck,          Cover = 2.2f, Label = "Chest" },
            new Slot { Category = "Belt Armor",  Bone = HumanBodyBones.Hips,
                       Tip = HumanBodyBones.Spine,         Cover = 1.6f, Label = "Belt" },

            new Slot { Category = "Arm Armor",   Bone = HumanBodyBones.LeftLowerArm,
                       Tip = HumanBodyBones.LeftHand,      Cover = 1f, Label = "Arm L" },
            new Slot { Category = "Arm Armor",   Bone = HumanBodyBones.RightLowerArm,
                       Tip = HumanBodyBones.RightHand,     Cover = 1f, Label = "Arm R" },

            new Slot { Category = "Legs Armor",  Bone = HumanBodyBones.LeftUpperLeg,
                       Tip = HumanBodyBones.LeftLowerLeg,  Cover = 1f, Label = "Leg L" },
            new Slot { Category = "Legs Armor",  Bone = HumanBodyBones.RightUpperLeg,
                       Tip = HumanBodyBones.RightLowerLeg, Cover = 1f, Label = "Leg R" },

            new Slot { Category = "Feet Armor",  Bone = HumanBodyBones.LeftFoot,
                       Tip = HumanBodyBones.LeftLowerLeg,  Cover = 0.7f, Label = "Foot L" },
            new Slot { Category = "Feet Armor",  Bone = HumanBodyBones.RightFoot,
                       Tip = HumanBodyBones.RightLowerLeg, Cover = 0.7f, Label = "Foot R" },
        };

        /// <summary>What a bare fighter wears: the lightest legs and boots, nothing above.</summary>
        static readonly string[] BareLabels = { "Leg L", "Leg R", "Foot L", "Foot R" };

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

            for (int f = 0; f < fighters.Length; f++) Dress(fighters[f], (f % 3) + 1);

            UnityEditor.SceneManagement.EditorSceneManager
                       .MarkSceneDirty(fighters[0].gameObject.scene);
        }

        static void Dress(PlayerController fighter, int colour)
        {
            Animator animator = fighter.GetComponentInChildren<Animator>(true);
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            {
                Debug.LogWarning(
                    $"{fighter.name}: no Humanoid Animator under it, so there are no bones to " +
                    "hang armour on. The character has to be rigged Humanoid — this one is, " +
                    "so check that its Animator is a child of the fighter.", fighter);
                return;
            }

            List<GameObject> all = new List<GameObject>();
            List<ArmourSet> sets = new List<ArmourSet>();
            List<string> report = new List<string>();

            foreach ((ItemId item, int type) in Mapping)
            {
                List<GameObject> worn = new List<GameObject>();
                foreach (Slot slot in Slots)
                {
                    GameObject piece = Make(fighter.transform, animator, slot, item, type, colour, report);
                    if (piece == null) continue;
                    worn.Add(piece);
                    all.Add(piece);
                }
                sets.Add(new ArmourSet { item = item, pieces = worn.ToArray() });
            }

            // Bare: the lightest legs and boots reused from the leather set, because an
            // unarmoured fighter should be dressed rather than naked and "found nothing yet"
            // still has to read from across the arena.
            ArmourSet leather = sets.Find(s => s.item == ItemId.LeatherVest);
            List<GameObject> bare = new List<GameObject>();
            if (leather != null)
                foreach (GameObject p in leather.pieces)
                    foreach (string label in BareLabels)
                        if (p != null && p.name.EndsWith(label)) bare.Add(p);
            sets.Insert(0, new ArmourSet { item = ItemId.None, pieces = bare.ToArray() });

            PlayerArmourView view = fighter.GetComponent<PlayerArmourView>();
            if (view == null) view = Undo.AddComponent<PlayerArmourView>(fighter.gameObject);

            Undo.RecordObject(view, "Wire armour");
            view.player = fighter;
            view.sets = sets.ToArray();
            view.allParts = all.ToArray();
            PrefabUtility.RecordPrefabInstancePropertyModifications(view);
            view.Wear(ItemId.None);

            Debug.Log(
                $"{fighter.name}: {all.Count} piece(s) in colour {colour}, " +
                $"{sets.Count} set(s).\n  " + string.Join("\n  ", report) + "\n" +
                "Each piece is a plain transform under \"Armour\" now — nudge position and " +
                "rotation in the scene view until they sit right. Shield is a worn look only; " +
                "a carried shield belongs to the weapon socket.", view);
        }

        /// <summary>Creates, parents, measures and switches off one piece.</summary>
        static GameObject Make(Transform fighter, Animator animator, Slot slot,
                               ItemId item, int type, int colour, List<string> report)
        {
            Transform bone = animator.GetBoneTransform(slot.Bone);
            Transform tip = animator.GetBoneTransform(slot.Tip);
            if (bone == null)
            {
                report.Add($"{slot.Label}: this character has no {slot.Bone} bone — skipped");
                return null;
            }

            // Named for what it is, so that re-running this finds it again wherever in the
            // skeleton it ended up — and so the hierarchy reads as a wardrobe.
            string name = $"{item} {slot.Label}";
            GameObject piece = FindPiece(fighter, name);

            if (piece == null)
            {
                GameObject prefab = LoadPart(slot.Category, type, colour);
                if (prefab == null)
                {
                    report.Add($"{slot.Label}: no \"{slot.Category} Type {type}\" prefab — skipped");
                    return null;
                }

                piece = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                Undo.RegisterCreatedObjectUndo(piece, "Wire armour");
                piece.name = name;
            }

            // Parented to the bone, because that is the whole mechanism: the piece moves with
            // the limb and the animation needs to know nothing about it.
            Undo.SetTransformParent(piece.transform, bone, "Wire armour");
            piece.transform.localPosition = Vector3.zero;
            piece.transform.localRotation = Quaternion.identity;

            // Measured, not guessed: the segment this piece covers, and the piece's own mesh.
            float segment = tip != null
                ? Vector3.Distance(bone.position, tip.position)
                : 0f;
            Bounds box = MeshBounds(piece);
            float longest = Mathf.Max(box.size.x, Mathf.Max(box.size.y, box.size.z));

            if (segment > 0.0001f && longest > 0.0001f)
            {
                float want = segment * slot.Cover;
                float scale = want / longest;

                // In the bone's own space, and bones are scaled by the character's own root.
                piece.transform.localScale = Vector3.one * scale;
                piece.transform.localPosition = -box.center * scale;

                report.Add($"{slot.Label}: segment {segment * 100f:0.0} cm, piece " +
                           $"{longest * 100f:0.0} cm -> scale {scale:0.000}");
            }
            else
                report.Add($"{slot.Label}: nothing to measure against " +
                           (tip == null ? $"(no {slot.Tip} bone)" : "(piece has no mesh)") +
                           " — left at scale 1, place it by hand");

            piece.SetActive(false);
            return piece;
        }

        /// <summary>A piece this tool made before, anywhere under the fighter.</summary>
        static GameObject FindPiece(Transform fighter, string name)
        {
            foreach (Transform t in fighter.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t.gameObject;
            return null;
        }

        static GameObject LoadPart(string category, int type, int colour)
        {
            for (int c = colour; c >= 1; c--)
            {
                string path = $"{PartsFolder}/{category} Type {type} Color {c} Part.prefab";
                GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go != null) return go;
            }
            return null;
        }

        /// <summary>The piece's own box, in its own space, from the meshes rather than the
        /// renderers: a switched-off renderer has no bounds.</summary>
        static Bounds MeshBounds(GameObject root)
        {
            MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
            bool any = false;
            Bounds box = new Bounds();

            foreach (MeshFilter mf in filters)
            {
                if (mf.sharedMesh == null) continue;
                Bounds local = mf.sharedMesh.bounds;

                // Into the root's space, so a prefab whose mesh hangs off a child still
                // measures as the thing you can see.
                Matrix4x4 m = root.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                Bounds moved = new Bounds(m.MultiplyPoint3x4(local.center), Vector3.zero);
                foreach (Vector3 corner in Corners(local))
                    moved.Encapsulate(m.MultiplyPoint3x4(corner));

                if (!any) { box = moved; any = true; }
                else box.Encapsulate(moved);
            }
            return box;
        }

        static IEnumerable<Vector3> Corners(Bounds b)
        {
            Vector3 c = b.center, e = b.extents;
            for (int i = 0; i < 8; i++)
                yield return c + new Vector3((i & 1) == 0 ? -e.x : e.x,
                                             (i & 2) == 0 ? -e.y : e.y,
                                             (i & 4) == 0 ? -e.z : e.z);
        }
    }
}
