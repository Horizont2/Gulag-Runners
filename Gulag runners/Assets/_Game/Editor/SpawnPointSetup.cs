using UnityEditor;
using UnityEngine;
using GulagRunners.Game;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Gives every fighter in the scene a spawn marker, at the feet they currently start from.
    ///
    /// Done from a menu rather than by hand-editing the scene because the fighters are prefab
    /// instances: assigning a field on one means writing a prefab override, and a prefab
    /// override written by hand is how a component ends up attached to a weapon socket. Unity
    /// writes it correctly; this only decides what it should say.
    ///
    /// Re-running it is safe. A fighter that already has a marker keeps it and the marker is
    /// moved to match, so this is also "put the spawns back where the fighters are now" after
    /// a session of dragging them about.
    /// </summary>
    public static class SpawnPointSetup
    {
        [MenuItem("Tools/Gulag Runners/Setup/Create spawn points")]
        public static void Create()
        {
            PlayerController[] fighters =
                Object.FindObjectsByType<PlayerController>(FindObjectsInactive.Include,
                                                           FindObjectsSortMode.None);

            if (fighters.Length == 0)
            {
                Debug.LogWarning("No PlayerController in this scene, so there is nothing to " +
                                 "make a spawn for.");
                return;
            }

            System.Array.Sort(fighters, (a, b) => a.name.CompareTo(b.name));

            foreach (PlayerController fighter in fighters)
            {
                // The feet, not the object: a fighter is drawn from its middle, so its own
                // position is a body's worth above the floor it stands on.
                Vector3 feet = fighter.transform.position;
                feet.y -= fighter.visualYOffset;

                Transform marker = fighter.spawnPoint;
                if (marker == null)
                {
                    GameObject go = new GameObject($"Spawn — {fighter.name}");
                    Undo.RegisterCreatedObjectUndo(go, "Create spawn points");
                    marker = go.transform;

                    SpawnPointMarker drawn = Undo.AddComponent<SpawnPointMarker>(go);
                    drawn.player = fighter;

                    Undo.RecordObject(fighter, "Create spawn points");
                    fighter.spawnPoint = marker;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(fighter);
                }

                Undo.RecordObject(marker, "Create spawn points");
                marker.position = feet;

                Debug.Log($"{fighter.name} starts at {feet} — marker \"{marker.name}\".\n" +
                          "Move the marker to move the spawn; the fighter's own transform is " +
                          "overwritten every frame of play and is not a reliable place to " +
                          "keep it.", marker);
            }

            EditorUtility.SetDirty(fighters[0].gameObject.scene.GetRootGameObjects()[0]);
            UnityEditor.SceneManagement.EditorSceneManager
                       .MarkSceneDirty(fighters[0].gameObject.scene);
        }
    }
}
