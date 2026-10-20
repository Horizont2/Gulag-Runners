using System.Linq;
using UnityEditor;
using UnityEngine;
using GulagRunners.Game;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Attaches a character model to a player and wires the animation to the simulation.
    ///
    /// This is an editor action rather than generated scene data on purpose: a prefab instance
    /// written into a scene by hand is fragile, and twice already Unity dropped one silently,
    /// taking the player with it. PrefabUtility does it properly, with undo, in one click.
    /// </summary>
    public static class PlayerVisualSetup
    {
        const string DefaultPrefabPath =
            "Assets/PolyOne/Free Stickman/Prefabs/Free Pack - Stick Man.prefab";
        const string VisualChildName = "Visual";

        [MenuItem("Tools/Gulag Runners/Attach character model to all players", false, 10)]
        public static void AttachToAll()
        {
            GameObject prefab = FindPrefab();
            if (prefab == null) return;

            PlayerController[] players =
                Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None);

            if (players.Length == 0)
            {
                Debug.LogWarning("No PlayerController in the open scene.");
                return;
            }

            foreach (PlayerController p in players) Attach(p, prefab);
            Debug.Log($"Attached \"{prefab.name}\" to {players.Length} player(s).");
        }

        [MenuItem("Tools/Gulag Runners/Remove character model from all players", false, 11)]
        public static void DetachFromAll()
        {
            foreach (PlayerController p in
                     Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
            {
                Transform existing = p.transform.Find(VisualChildName);
                if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);

                Undo.RecordObject(p, "Remove character model");
                p.visualRoot = null;
                p.squashOnCrouch = true;
                foreach (Renderer r in p.GetComponents<Renderer>())
                {
                    Undo.RecordObject(r, "Remove character model");
                    r.enabled = true;
                }
                EditorUtility.SetDirty(p);
            }
        }

        public static GameObject FindPrefab()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultPrefabPath);
            if (prefab != null) return prefab;

            // The pack may have been imported somewhere else; look for it by name.
            string guid = AssetDatabase.FindAssets("Stick Man t:Prefab").FirstOrDefault();
            if (guid != null)
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));

            if (prefab == null)
                Debug.LogError($"Character prefab not found at \"{DefaultPrefabPath}\" and no " +
                               "prefab matching \"Stick Man\" in the project.");
            return prefab;
        }

        public static void Attach(PlayerController player, GameObject prefab)
        {
            if (player == null || prefab == null) return;

            Undo.SetCurrentGroupName("Attach character model");
            int group = Undo.GetCurrentGroup();

            // Replace any previous visual.
            Transform old = player.transform.Find(VisualChildName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            // The capsule mesh lived on the player object itself and scaled it non-uniformly,
            // which would squash any child model. Reset the scale and hide the capsule.
            Undo.RecordObject(player.transform, "Attach character model");
            player.transform.localScale = Vector3.one;
            foreach (Renderer r in player.GetComponents<Renderer>())
            {
                Undo.RecordObject(r, "Attach character model");
                r.enabled = false;
            }

            GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(prefab, player.gameObject.scene);
            Undo.RegisterCreatedObjectUndo(visual, "Attach character model");
            visual.name = VisualChildName;
            visual.transform.SetParent(player.transform, false);

            // The simulation tracks the feet, and this object's origin sits at body centre, so
            // the model hangs below it by exactly that offset.
            visual.transform.localPosition = new Vector3(0f, -player.visualYOffset, 0f);
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;

            FitHeight(visual, player.tuning.bodyHeight);

            Animator animator = visual.GetComponentInChildren<Animator>();
            if (animator != null)
            {
                Undo.RecordObject(animator, "Attach character model");
                // Root motion would move the character behind the simulation's back.
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                EditorUtility.SetDirty(animator);
            }
            else
            {
                Debug.LogWarning($"{visual.name}: no Animator on the model, so it will not animate.",
                                 visual);
            }

            PlayerAnimator pa = player.GetComponent<PlayerAnimator>();
            if (pa == null) pa = Undo.AddComponent<PlayerAnimator>(player.gameObject);
            Undo.RecordObject(pa, "Attach character model");
            pa.player = player;
            pa.animator = animator;
            EditorUtility.SetDirty(pa);

            Undo.RecordObject(player, "Attach character model");
            player.visualRoot = visual.transform;
            player.squashOnCrouch = false;      // squashing a rigged character looks wrong
            EditorUtility.SetDirty(player);

            Undo.CollapseUndoOperations(group);
        }

        /// <summary>
        /// Scales the model so it is exactly as tall as the simulated body. Guessing a scale
        /// would put the feet through the floor or the head through the ceiling, and the arena
        /// is built around a 1.8 m fighter.
        /// </summary>
        static void FitHeight(GameObject visual, float targetHeight)
        {
            Renderer[] renderers = visual.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0 || targetHeight <= 0.01f) return;

            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);

            float height = b.size.y;
            if (height <= 0.01f) return;

            float scale = targetHeight / height;
            visual.transform.localScale = Vector3.one * scale;
            Debug.Log($"{visual.name}: model is {height:0.00} m tall, scaled by {scale:0.000} " +
                      $"to match the {targetHeight:0.00} m body.", visual);
        }
    }
}
