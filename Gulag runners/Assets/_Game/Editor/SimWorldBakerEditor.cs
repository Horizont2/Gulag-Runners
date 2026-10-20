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

            bool empty = baker.World == null || baker.World.Solids.Length == 0;
            EditorGUILayout.HelpBox(baker.LastReport,
                                    empty ? MessageType.Warning : MessageType.Info);

            if (GUILayout.Button("Bake now", GUILayout.Height(24)))
            {
                baker.Bake();
                Debug.Log($"{baker.name}: {baker.LastReport}", baker);
            }

            EditorGUILayout.HelpBox(
                "Press Bake now without entering play mode. If it reports 0 SimCollider and " +
                "0 Unity collider, the arena is not in this scene. If it reports colliders but " +
                "0 solid, the skip reasons say why.",
                MessageType.None);
        }
    }
}
