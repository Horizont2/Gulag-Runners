using UnityEditor;
using UnityEngine;
using GulagRunners.Game;
using GulagRunners.Sim;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Inspector for <see cref="PlayerLoadoutView"/>: says which items have a model on the
    /// character and which do not, because a weapon nobody can see is a weapon the other player
    /// cannot read, and docs/03 makes that silhouette part of the information the game runs on.
    /// </summary>
    [CustomEditor(typeof(PlayerLoadoutView))]
    public sealed class PlayerLoadoutViewEditor : Editor
    {
        static readonly ItemId[] Weapons = { ItemId.Club, ItemId.Spear };
        static readonly ItemId[] Armour = { ItemId.Chainmail };

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            PlayerLoadoutView view = (PlayerLoadoutView)target;

            string missing = Missing(view.weapons, Weapons) + Missing(view.armour, Armour);

            EditorGUILayout.Space();
            if (missing.Length == 0)
            {
                EditorGUILayout.HelpBox("Every item has a model on the character.", MessageType.Info);
                return;
            }

            EditorGUILayout.HelpBox(
                "No model on the character for:" + missing + "\n\n" +
                "Parent the model under the hand (for a weapon) or the body (for armour), leave " +
                "it switched off, and add it to the list above. docs/03: the silhouette is how " +
                "the other player reads what they are up against.",
                MessageType.Warning);
        }

        static string Missing(ItemModel[] models, ItemId[] expected)
        {
            string s = "";
            foreach (ItemId id in expected)
            {
                bool found = false;
                if (models != null)
                    foreach (ItemModel m in models)
                        if (m != null && m.item == id && m.root != null) { found = true; break; }
                if (!found) s += "\n  " + id;
            }
            return s;
        }
    }
}
