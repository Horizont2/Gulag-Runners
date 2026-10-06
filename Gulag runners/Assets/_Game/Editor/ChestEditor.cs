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
