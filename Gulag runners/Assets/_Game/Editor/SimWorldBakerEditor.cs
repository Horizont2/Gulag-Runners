using UnityEditor;
using UnityEngine;
using GulagRunners.Game;

namespace GulagRunners.GameEditor
{
    [CustomEditor(typeof(SimWorldBaker))]
    public sealed class SimWorldBakerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            SimWorldBaker baker = (SimWorldBaker)target;

            EditorGUILayout.Space();
            if (baker.World != null)
                EditorGUILayout.HelpBox(
                    $"Baked: {baker.World.Solids.Length} solid, " +
                    $"{baker.World.OneWay.Length} one-way, " +
                    $"{baker.World.Ladders.Length} ladder boxes.",
                    MessageType.Info);
            else
                EditorGUILayout.HelpBox(
                    "Not baked yet. The world is baked automatically on Awake.",
                    MessageType.None);

            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button("Re-bake now"))
                    baker.Bake();
            }
        }
    }
}
