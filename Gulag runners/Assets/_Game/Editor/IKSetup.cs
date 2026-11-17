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
        [MenuItem("Tools/Gulag Runners/Setup/Set up IK")]
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
            int added = 0, passes = 0, arms = 0, strays = 0;

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

                // The weapon arm, on the SAME object and for the same reason: OnAnimatorIK
                // only ever arrives on the object that owns the Animator.
                //
                // It is placed here rather than by hand because by hand is how it goes
                // wrong. Dropped on the fighter's root it used to bring a second, empty
                // Animator with it, and PlayerAnimator then bound to that one -- a body
                // that moved perfectly and never animated, with nothing in the console.
                PlayerArm arm = animator.GetComponent<PlayerArm>();
                if (arm == null)
                {
                    arm = Undo.AddComponent<PlayerArm>(animator.gameObject);
                    arms++;
                }

                Undo.RecordObject(arm, "Set up IK");
                arm.player = fighter;
                arm.animator = animator;
                if (arm.socket == null)
                    arm.socket = fighter.GetComponentInChildren<WeaponSocket>(true);
                PrefabUtility.RecordPrefabInstancePropertyModifications(arm);

                // And anything that wandered onto the root, which is the shape of the
                // bug: an Animator with no controller cannot play a thing, so it is
                // never the one meant.
                foreach (Animator stray in fighter.GetComponentsInChildren<Animator>(true))
                    if (stray != null && stray != animator &&
                        stray.runtimeAnimatorController == null)
                    {
                        Debug.LogWarning(
                            $"{fighter.name}: an Animator with no controller on " +
                            $"\"{stray.name}\". It cannot play anything and it can take " +
                            "the rig's place in a GetComponentInChildren. Delete it.",
                            stray);
                        strays++;
                    }

                passes += EnablePass(animator.runtimeAnimatorController);
            }

            string extra = strays > 0
                ? $"\n{strays} Animator(s) with no controller found -- see the warnings."
                : "";

            Debug.Log(
                $"IK: {added} foot rig(s) and {arms} weapon arm(s) added, IK Pass switched " +
                $"on for {passes} layer(s).{extra}\n" +
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

            // The base layer gets the pass and the others have it taken away. PlayerIK does
            // a frame's work every time OnAnimatorIK is called, and Unity calls it once per
            // layer that asks -- so a second ticked layer runs every blend at double rate
            // and feeds each IK goal back into itself. The goals survive into the later
            // passes anyway.
            for (int i = 0; i < layers.Length; i++)
            {
                bool want = i == 0;
                if (layers[i].iKPass == want) continue;
                layers[i].iKPass = want;
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
