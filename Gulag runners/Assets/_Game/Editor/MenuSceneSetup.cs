using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using GulagRunners.Game;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Puts the menu scene together: the fighter, the director and the interface.
    ///
    /// It wires what it can find and leaves the rest showing in the inspector, because the
    /// half it cannot find — which panel is the shop, which object is the portcullis — is
    /// the half somebody has to decide. What it will not do is guess: an empty slot that
    /// says so beats a wrong one that does not.
    ///
    /// The one fault it fixes outright is the T-pose. A model whose Animator has no
    /// controller does not idle, it stands in its bind pose, and in a menu that is the
    /// first thing anybody sees.
    /// </summary>
    public static class MenuSceneSetup
    {
        const string Controller = "Assets/_Game/Animation/GR_Player.controller";

        [MenuItem("Tools/Gulag Runners/Set up the menu scene")]
        public static void Setup()
        {
            Scene scene = SceneManager.GetActiveScene();
            var said = new System.Text.StringBuilder();

            MenuFighter fighter = Fighter(said);
            MenuDirector director = Director(fighter, said);
            UI(director, fighter, said);

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"Menu scene: \n{said}");
        }

        /// <summary>
        /// The fighter, on whatever model is standing in the scene.
        ///
        /// Found as an Animator that is NOT part of a PlayerController: the menu's model is
        /// a model, and if a controller is on it then this is the wrong scene to be doing
        /// this in.
        /// </summary>
        static MenuFighter Fighter(System.Text.StringBuilder said)
        {
            MenuFighter had = Object.FindFirstObjectByType<MenuFighter>();
            Animator model = null;

            foreach (Animator a in Object.FindObjectsByType<Animator>(FindObjectsInactive.Include,
                                                                       FindObjectsSortMode.None))
            {
                if (a.GetComponentInParent<PlayerController>() != null) continue;
                model = a;
                break;
            }

            if (model == null)
            {
                said.AppendLine("  no model with an Animator found — drop the fighter in, "
                              + "then run this again");
                return had;
            }

            // The T-pose. A model with no controller has nothing to play, so it stands in
            // its bind pose and looks broken before anything else has had a chance to.
            if (model.runtimeAnimatorController == null)
            {
                var built = AssetDatabase.LoadAssetAtPath<AnimatorController>(Controller);
                if (built != null)
                {
                    Undo.RecordObject(model, "Set up the menu scene");
                    model.runtimeAnimatorController = built;
                    EditorUtility.SetDirty(model);
                    said.AppendLine($"  {model.name}: had no Animator Controller — that is "
                                  + "the T-pose. Assigned GR_Player.");
                }
                else
                {
                    said.AppendLine($"  {model.name} has no Animator Controller and "
                                  + $"{Controller} is missing, so it will stay in its "
                                  + "bind pose. Run Build fighter animator first.");
                }
            }

            // On the model's own object, so Pose State drives the Animator beside it.
            MenuFighter f = had != null ? had : model.GetComponent<MenuFighter>();
            if (f == null)
            {
                f = Undo.AddComponent<MenuFighter>(model.gameObject);
                said.AppendLine($"  MenuFighter added to {model.name}");
            }

            Undo.RecordObject(f, "Set up the menu scene");
            if (f.animator == null) f.animator = model;
            if (f.turntable == null) f.turntable = f.transform;
            if (string.IsNullOrEmpty(f.poseState)) f.poseState = "Idle";

            // Its own model counts as the first skin, so the list is never empty and the
            // menu never shows nobody.
            if (f.skins == null || f.skins.Length == 0)
            {
                f.skins = new[] { new MenuSkin { id = "default", root = model.gameObject } };
                said.AppendLine("  skins: one entry, \"default\", pointing at the model "
                              + "that is already here");
            }

            EditorUtility.SetDirty(f);
            said.AppendLine($"  pose: \"{f.poseState}\", freeze {f.freeze}, "
                          + $"sway {f.sway}");
            return f;
        }

        static MenuDirector Director(MenuFighter fighter, System.Text.StringBuilder said)
        {
            MenuDirector d = Object.FindFirstObjectByType<MenuDirector>();
            if (d == null)
            {
                GameObject go = GameObject.Find("Menu") ?? new GameObject("Menu");
                Undo.RegisterCreatedObjectUndo(go, "Set up the menu scene");
                d = Undo.AddComponent<MenuDirector>(go);
                said.AppendLine("  MenuDirector added to a \"Menu\" object");
            }

            Undo.RecordObject(d, "Set up the menu scene");
            if (d.fighter == null) d.fighter = fighter;
            if (d.view == null) d.view = Camera.main ?? Object.FindFirstObjectByType<Camera>();

            // The gate is the one thing worth guessing at, because there is only ever one
            // thing in a cell called an arch.
            if (d.gate == null)
            {
                GameObject arch = GameObject.Find("Arch") ?? GameObject.Find("Portcullis")
                                ?? GameObject.Find("Gate");
                if (arch != null)
                {
                    d.gate = arch.transform;
                    said.AppendLine($"  gate: \"{arch.name}\" — check this is the part that "
                                  + "should RISE, not the stonework around it");
                }
                else said.AppendLine("  gate: nothing found; assign the portcullis by hand");
            }

            EditorUtility.SetDirty(d);
            return d;
        }

        static void UI(MenuDirector director, MenuFighter fighter,
                       System.Text.StringBuilder said)
        {
            MenuUI ui = Object.FindFirstObjectByType<MenuUI>();
            if (ui == null)
            {
                GameObject go = GameObject.Find("Menu UI") ?? new GameObject("Menu UI");
                Undo.RegisterCreatedObjectUndo(go, "Set up the menu scene");
                ui = Undo.AddComponent<MenuUI>(go);
                said.AppendLine("  MenuUI added to a \"Menu UI\" object");
            }

            Undo.RecordObject(ui, "Set up the menu scene");
            if (ui.director == null) ui.director = director;
            if (ui.fighter == null) ui.fighter = fighter;
            EditorUtility.SetDirty(ui);

            said.AppendLine("  buttons: wire OnClick to MenuUI.Play, Profile, Shop, "
                          + "Dailies, Season, Settings, Friends, Back");
            said.AppendLine("  readouts: wire GoldChanged / LeagueChanged to a text "
                          + "setter, LeagueFilled / SeasonFilled to an Image.fillAmount");
        }
    }
}
