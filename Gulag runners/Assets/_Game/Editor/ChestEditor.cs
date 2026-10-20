using UnityEditor;
using UnityEngine;
using GulagRunners.Game;
using GulagRunners.Sim;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Inspector for <see cref="Chest"/>. Shows the thing a designer actually has to decide:
    /// how long this chest takes, and how loud it is, with each weapon in the game.
    ///
    /// docs/03 calls that table "how much information about yourself you trade for this loot",
    /// and it is the whole balance of the scavenge phase, so it belongs in front of whoever is
    /// placing the chest rather than in a document they would have to go and find.
    /// </summary>
    [CustomEditor(typeof(Chest))]
    [CanEditMultipleObjects]
    public sealed class ChestEditor : Editor
    {
        static readonly ItemId[] Weapons = { ItemId.None, ItemId.Club, ItemId.Spear };

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            Chest chest = (Chest)target;
            ChestConfig cfg = Config();
            ChestKindConfig kind = cfg.For(chest.kind);
            ItemDef contents = chest.ContentsDef;

            EditorGUILayout.Space();

            string rows = "";
            foreach (ItemId w in Weapons)
            {
                float seconds = LootMotor.FramesToOpen(chest.kind, w, in cfg)
                                / (float)PlayerMotor.TicksPerSecond;
                bool bare = w == ItemId.None;
                string wear = bare ? "no cost" : $"-{kind.ToolWear} durability";
                string noise = (bare ? kind.BareNoise : kind.ToolNoise).ToString();
                rows += $"\n  {(bare ? "bare hands" : w.ToString()),-10} {seconds,5:0.0} s   " +
                        $"{noise,-6} {wear}";
            }

            EditorGUILayout.HelpBox(
                $"Holds {chest.contents} (tier {contents.Tier}, {contents.Kind}).\n" +
                $"Opening it:{rows}",
                MessageType.Info);

            if (chest.contents == ItemId.None)
                EditorGUILayout.HelpBox(
                    "This chest is empty. Opening it will cost the player the time and the noise " +
                    "and give nothing back.", MessageType.Warning);

            // The model is a separate asset and only Unity knows how big it really is, so the
            // interaction box is measured here rather than guessed when the chest is placed.
            Renderer[] model = chest.GetComponentsInChildren<Renderer>();
            BoxCollider box = chest.GetComponent<BoxCollider>();
            if (model.Length > 0)
            {
                Bounds b = model[0].bounds;
                for (int i = 1; i < model.Length; i++) b.Encapsulate(model[i].bounds);
                Rect r = chest.ToRect();

                EditorGUILayout.HelpBox(
                    $"Model is {b.size.x:0.00} x {b.size.y:0.00} m, " +
                    $"reach box is {r.width:0.00} x {r.height:0.00} m.\n" +
                    "The box is what the player has to stand in, not what they can see: a little " +
                    "wider than the model is right, much wider is a chest you open from across " +
                    "the room.",
                    MessageType.None);

                using (new EditorGUI.DisabledScope(box == null))
                {
                    if (GUILayout.Button("Fit reach box to the model", GUILayout.Height(22)))
                    {
                        Undo.RecordObject(box, "Fit reach box to the model");
                        Vector3 scale = chest.transform.lossyScale;
                        Vector3 size = new Vector3(
                            Mathf.Max(0.4f, b.size.x + 0.2f) / Mathf.Max(0.0001f, scale.x),
                            Mathf.Max(0.3f, b.size.y) / Mathf.Max(0.0001f, scale.y),
                            Mathf.Max(0.4f, b.size.z + 0.2f) / Mathf.Max(0.0001f, scale.z));
                        box.size = size;
                        box.center = new Vector3(0f, size.y * 0.5f, 0f);
                        EditorUtility.SetDirty(box);
                    }
                }

                if (box == null)
                    EditorGUILayout.HelpBox(
                        "No BoxCollider on this chest, so the reach box comes from the transform " +
                        "scale — which also scales the model. Add one (it is never baked into a " +
                        "wall) to size the two independently.", MessageType.Warning);
            }

            if (chest.kind == ChestKind.Safe && !chest.hasPuzzle)
                EditorGUILayout.HelpBox(
                    "A safe with no puzzle can only be forced. That is the slow, loud way in — " +
                    "fine while docs/04 is unbuilt, but tick Has Puzzle on the safes that should " +
                    "offer the quiet way once it lands.", MessageType.None);
        }

        /// <summary>The tuning this chest will really be opened with, if the scene has any.</summary>
        static ChestConfig Config()
        {
            MatchState match = Object.FindFirstObjectByType<MatchState>();
            return match != null ? match.chests.ToConfig() : ChestConfig.Default();
        }
    }
}
