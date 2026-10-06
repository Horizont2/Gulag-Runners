using UnityEditor;
using UnityEngine;
using GulagRunners.Game;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Inspector for <see cref="LootView"/>. Its one job is the button: a scene cannot hold a
    /// direct reference to a component inside a prefab instance without a stripped entry, so the
    /// list is filled from the children here, once, and written into the scene properly.
    /// </summary>
    [CustomEditor(typeof(LootView))]
    public sealed class LootViewEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            LootView view = (LootView)target;
            MatchState match = view.matchState != null
                ? view.matchState : Object.FindFirstObjectByType<MatchState>();

            int have = view.views != null ? view.views.Length : 0;
            int want = match != null ? match.groundItemCapacity : 0;

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                $"{have} view(s) for a pool of {want}.\n" +
                (have >= want
                    ? "Every slot of the pool can be seen."
                    : "Loot in the slots past the last view will be invisible and still pickable."),
                have >= want ? MessageType.Info : MessageType.Warning);

            if (GUILayout.Button("Collect views from children", GUILayout.Height(24)))
            {
                Undo.RecordObject(view, "Collect loot views");
                int found = view.CollectViews(true);
                EditorUtility.SetDirty(view);
                Debug.Log($"{view.name}: collected {found} ground item view(s).", view);
            }
        }
    }
}
