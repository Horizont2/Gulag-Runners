using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using GulagRunners.Game;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Switches IK on: the component on each fighter's Animator, and IK Pass on the layers.
    ///
    /// The second half is the one worth a tool. OnAnimatorIK is never called on a layer that
    /// does not have IK Pass ticked, so without it <see cref="PlayerIK"/> runs no code at
    /// all and the symptom is indistinguishable from the component being broken — a tick box
    /// three menus deep deciding whether an entire system exists.
    /// </summary>
    public static class IKSetup
    {
        [MenuItem("Tools/Gulag Runners/Set up IK")]
        public static void Setup()
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
            int added = 0, passes = 0;

            foreach (PlayerController fighter in fighters)
            {
                Animator animator = fighter.GetComponentInChildren<Animator>(true);
                if (animator == null)
                {
                    Debug.LogWarning($"{fighter.name}: no Animator under it.", fighter);
                    continue;
                }

                if (animator.avatar == null || !animator.avatar.isHuman)
                {
                    Debug.LogWarning(
                        $"{fighter.name}: its avatar is not Humanoid. Mecanim's IK goals are " +
                        "humanoid-only, so set the model's Rig to Humanoid first.", fighter);
                    continue;
                }

                // On the Animator's own object, because that is where Unity calls back.
                PlayerIK ik = animator.GetComponent<PlayerIK>();
                if (ik == null)
                {
                    ik = Undo.AddComponent<PlayerIK>(animator.gameObject);
                    added++;
                }

                Undo.RecordObject(ik, "Set up IK");
                ik.player = fighter;
                PrefabUtility.RecordPrefabInstancePropertyModifications(ik);

                passes += EnablePass(animator.runtimeAnimatorController);
            }

            Debug.Log(
                $"IK: {added} component(s) added, IK Pass switched on for {passes} layer(s).\n" +
                "Hands and feet now go where the arena is rather than where the clip's own " +
                "rig had them. The two to try first if it reads oddly are Rung Spacing — a " +
                "hand should land ON a rung, not between two — and the two hand heights, " +
                "which differ by about half a rung so the hands alternate rather than moving " +
                "together.");
        }

        /// <summary>Ticks IK Pass on every layer of a controller, and says how many.</summary>
        static int EnablePass(RuntimeAnimatorController runtime)
        {
            AnimatorController controller = runtime as AnimatorController;
            if (controller == null) return 0;

            AnimatorControllerLayer[] layers = controller.layers;
            int changed = 0;

            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i].iKPass) continue;
                layers[i].iKPass = true;
                changed++;
            }

            if (changed > 0)
            {
                controller.layers = layers;       // the array is a copy; it has to go back
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();
            }
            return changed;
        }
    }
}
