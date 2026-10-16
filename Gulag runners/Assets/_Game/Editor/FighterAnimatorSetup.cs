using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using GulagRunners.Game;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Builds the fighter's Animator Controller: every state <see cref="PlayerAnimator"/>
    /// asks for, on two layers, with the mask that lets the arms fight while the legs run.
    ///
    /// WHY THIS EXISTS AT ALL. The controller in play was the free Stickman pack's, which
    /// has nine states: Idle, Walk, Run, Run Fast, Jumping Up, Sitting, Swimming, Yelling
    /// and Ladder_Up. The component asks for twenty-odd, including seven ladder states and
    /// three attacks, and <see cref="Animator.Play(int,int)"/> on a state that does not
    /// exist does NOTHING — so the animator kept whatever was last played and the character
    /// held one frame for the length of a climb. That was reported as a broken climb
    /// animation, hunted through the IK, and was all along a controller that had never had
    /// the states. Note the one that does exist is spelled "Ladder_Up" and the component
    /// asks for "Ladder Up"; a hash is a hash.
    ///
    /// WHY TWO LAYERS. A fighter who can attack while moving is drawing two things at once:
    /// the feet are carrying him somewhere and the blade is going somewhere else. One layer
    /// cannot show both. So the fight gets its own layer, masked to the upper body, and the
    /// locomotion underneath it keeps running — which is how every game that lets you swing
    /// on the move does it, and costs one mask and one weight.
    ///
    /// Everything it makes is an ASSET, visible and editable in the inspector afterwards:
    /// nothing here runs at play time, and the controller can be hand-tuned once built.
    /// Run it again and it rebuilds from the table below, so hand edits to STATES are lost
    /// while edits to the clips and the mask are not.
    /// </summary>
    public static class FighterAnimatorSetup
    {
        const string Folder = "Assets/_Game/Animation";
        const string ControllerPath = Folder + "/Fighter.controller";
        const string MaskPath = Folder + "/Upper Body.mask";

        /// <summary>
        /// State name -> the clip to put in it, by asset name.
        ///
        /// The InPlace variants throughout: the simulation owns the position and root motion
        /// would move the character behind its back (docs/06). A name that matches nothing
        /// is reported and skipped rather than guessed at.
        /// </summary>
        static readonly (string state, string clip)[] Body =
        {
            ("Idle",              "1Hand_Up_Stand_Idle_A_2"),
            ("Walk",              "1Hand_Up_Walk_A_F_InPlace"),
            ("Run",               "1Hand_Up_Run_A_F_InPlace"),

            // The two the aim/travel split exists to draw: travelling one way with the blade
            // pointed the other. Authored backwards, not played in reverse -- the weight goes
            // the wrong way in reverse and the arms lead instead of trail.
            ("Walk Back",         "1Hand_Up_Walk_A_B_InPlace"),
            ("Run Back",          "1Hand_Up_Run_A_B_InPlace"),

            ("Run Fast",          "1Hand_Up_Sprint_A_F_InPlace"),
            ("Jumping Up",        "1Hand_Up_Jump_B_InPlace"),
            ("Sitting",           "1Hand_Up_Crouch_Idle_1"),
            ("Crouch Walk",       "1Hand_Up_Crouch_F_InPlace"),

            // The seven the climb needs. Six of them have never existed in this project.
            ("Ladder Idle",       "Ladder_Idle"),
            ("Ladder Up",         "Ladder_Up_Play_InPlace"),
            ("Ladder Down",       "Ladder_Down_Play_InPlace"),
            ("Ladder Up Start",   "Ladder_Up_Start_InPlace"),
            ("Ladder Down Start", "Ladder_Down_Start_InPlace"),
            ("Ladder Out",        "Ladder_Up_End_InPlace"),
            ("Ladder Down End",   "Ladder_Down_End_InPlace"),

            ("Hit",               "Hit_B_4_InPlace"),
            ("Stagger",           "Hit_B_5_InPlace"),
        };

        /// <summary>
        /// The fight. These go on BOTH layers on purpose: masked to the arms while the body
        /// is going somewhere, and on the base layer as a whole-body clip when it is not,
        /// because a planted blow is hips and shoulders and a masked one over an idle loop
        /// is a man waving his arms.
        /// </summary>
        static readonly (string state, string clip)[] Fight =
        {
            ("Attack",     "1Hand_Up_Attack_A_1_InPlace"),
            ("Attack 2",   "1Hand_Up_Attack_A_2_InPlace"),
            ("Attack 3",   "1Hand_Up_Attack_A_3_InPlace"),
            ("Block Idle", "1Hand_Up_Shield_Block_Idle_1"),
            ("Block Hit",  "1Hand_Up_Shield_Block_Hit_1_InPlace"),
        };

        [MenuItem("Tools/Gulag Runners/Build fighter animator")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
            {
                string parent = "Assets/_Game";
                if (!AssetDatabase.IsValidFolder(parent))
                    AssetDatabase.CreateFolder("Assets", "_Game");
                AssetDatabase.CreateFolder(parent, "Animation");
            }

            Dictionary<string, AnimationClip> clips = FindClips();
            AvatarMask mask = BuildMask();

            AnimatorController controller =
                AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
                controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

            // Rebuilt from scratch each run: half an old table left behind is worse than no
            // table, because the states that remain still answer HasState and play the wrong
            // clip silently.
            while (controller.layers.Length > 0) controller.RemoveLayer(0);

            var missing = new List<string>();

            // Added BY NAME, not as a struct we built ourselves. AddLayer(string) creates the
            // state machine as a sub-asset of the controller; AddLayer(AnimatorControllerLayer)
            // takes the struct as given and the state machine we made belongs to nothing, so
            // it is dropped the next time the asset is loaded and the controller comes back
            // with empty layers. That failure happens on reload, not on build, which is the
            // worst time for it to happen.
            controller.AddLayer("Base Layer");
            controller.AddLayer("Upper Body");

            Fill(controller, 0, Body, clips, missing);
            Fill(controller, 0, Fight, clips, missing);
            Fill(controller, 1, Fight, clips, missing);

            // Weight 0 on the fight layer: PlayerAnimator drives it, and a layer left at 1
            // is a fighter permanently mid-swing.
            AnimatorControllerLayer[] built = controller.layers;
            built[0].defaultWeight = 1f;
            built[0].blendingMode = AnimatorLayerBlendingMode.Override;
            built[1].avatarMask = mask;
            built[1].defaultWeight = 0f;
            built[1].blendingMode = AnimatorLayerBlendingMode.Override;
            controller.layers = built;

            // IK Pass on every layer, or OnAnimatorIK is never called and PlayerIK runs no
            // code at all -- the same tick box Set up IK exists for, set here while the
            // layers are being made rather than fixed up afterwards.
            AnimatorControllerLayer[] layers = controller.layers;
            for (int i = 0; i < layers.Length; i++) layers[i].iKPass = true;
            controller.layers = layers;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            int assigned = Assign(controller);

            string made = $"{Folder}/Fighter.controller: {controller.layers.Length} layer(s), " +
                          $"{Body.Length + Fight.Length} state(s) on the base layer and " +
                          $"{Fight.Length} on the upper body, assigned to {assigned} fighter(s).";

            if (missing.Count > 0)
                Debug.LogWarning($"{made}\nNo clip found for: {string.Join(", ", missing)}. " +
                                 "Those states are empty and will show the T-pose; correct " +
                                 "the table in FighterAnimatorSetup, or drop the clip in by " +
                                 "hand.");
            else
                Debug.Log(made);
        }

        static void Fill(AnimatorController controller, int layer,
                         (string state, string clip)[] table,
                         Dictionary<string, AnimationClip> clips, List<string> missing)
        {
            if (layer < 0 || layer >= controller.layers.Length) return;
            AnimatorStateMachine machine = controller.layers[layer].stateMachine;

            foreach ((string state, string clip) in table)
            {
                if (Has(machine, state)) continue;

                AnimatorState s = machine.AddState(state);

                // Consistently off across both layers. Mixing Write Defaults within one
                // controller is the classic way to get a pose that depends on which state
                // ran last, and these are all humanoid muscle clips over the same rig, so
                // there is nothing for a default to fill in.
                s.writeDefaultValues = false;

                if (clips.TryGetValue(clip, out AnimationClip motion)) s.motion = motion;
                else if (!missing.Contains(clip)) missing.Add(clip);
            }
        }

        static bool Has(AnimatorStateMachine machine, string state)
        {
            foreach (ChildAnimatorState c in machine.states)
                if (c.state != null && c.state.name == state) return true;
            return false;
        }

        /// <summary>
        /// Every AnimationClip in the project by name, nearest match wins on a tie.
        ///
        /// By name rather than by path, because the pack's folders are its own business and
        /// renaming one would silently empty half the controller.
        /// </summary>
        static Dictionary<string, AnimationClip> FindClips()
        {
            var found = new Dictionary<string, AnimationClip>();
            foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset is not AnimationClip clip) continue;
                    if (clip.name.StartsWith("__preview__")) continue;
                    found[clip.name] = clip;
                }
            }
            return found;
        }

        /// <summary>
        /// The mask: arms, chest and head on, legs and root off.
        ///
        /// Root off is the one that matters and the one that is easy to miss. With it on,
        /// the fight layer writes the body's own transform and the swing drags the character
        /// off the position the simulation put him on — which on this project is not a
        /// cosmetic fault but a desync between what collides and what is drawn.
        /// </summary>
        static AvatarMask BuildMask()
        {
            AvatarMask mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
            bool fresh = mask == null;
            if (fresh) mask = new AvatarMask { name = "Upper Body" };

            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Root, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Body, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Head, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightLeg, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFootIK, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFootIK, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftHandIK, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightHandIK, true);

            if (fresh) AssetDatabase.CreateAsset(mask, MaskPath);
            else EditorUtility.SetDirty(mask);
            return mask;
        }

        /// <summary>Puts the controller on every fighter in the open scene.</summary>
        static int Assign(AnimatorController controller)
        {
            PlayerController[] fighters =
                Object.FindObjectsByType<PlayerController>(FindObjectsInactive.Include,
                                                           FindObjectsSortMode.None);
            int done = 0;
            foreach (PlayerController fighter in fighters)
            {
                Animator animator = fighter.GetComponentInChildren<Animator>(true);
                if (animator == null) continue;

                Undo.RecordObject(animator, "Assign fighter animator");
                animator.runtimeAnimatorController = controller;
                EditorUtility.SetDirty(animator);
                done++;
            }
            return done;
        }
    }
}
