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

        [MenuItem("Tools/Gulag Runners/Setup/Set up the menu scene")]
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
        /// A model without a PlayerController is taken as it is. A model WITH one is taken
        /// too, and the controller is switched off along with everything else on it that
        /// acts — which is the case that actually turns up, because the obvious thing to
        /// drag into a menu is the fighter prefab from the match, and it half works: he
        /// stands there in the editor and then, on Play, PlayerController ticks a motor
        /// with no MatchState and writes transform.position from a simulation that has
        /// not started, so he leaves the spot he was placed on.
        ///
        /// Switched off and not deleted: the components are the prefab's, the menu only
        /// overrides them, and the day this model goes back into a match nothing has been
        /// lost.
        /// </summary>
        static MenuFighter Fighter(System.Text.StringBuilder said)
        {
            MenuFighter had = Object.FindFirstObjectByType<MenuFighter>();
            Animator model = null;
            Animator fallback = null;

            foreach (Animator a in Object.FindObjectsByType<Animator>(FindObjectsInactive.Include,
                                                                       FindObjectsSortMode.None))
            {
                if (a.GetComponentInParent<PlayerController>() != null)
                {
                    if (fallback == null) fallback = a;
                    continue;
                }
                model = a;
                break;
            }

            if (model == null && fallback != null)
            {
                model = fallback;
                said.AppendLine($"  {Quiet(fallback)} match behaviour(s) switched off on "
                              + "the fighter: this is a menu, not a match.");
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

            // Beside the Animator, or -- when the model is a fighter prefab -- on the
            // prefab's root, where the components it has to stand in for already are.
            PlayerController owner = model.GetComponentInParent<PlayerController>();
            GameObject host = owner != null ? owner.gameObject : model.gameObject;

            MenuFighter f = had != null ? had : host.GetComponent<MenuFighter>();
            if (f == null)
            {
                f = Undo.AddComponent<MenuFighter>(host);
                said.AppendLine($"  MenuFighter added to {host.name}");
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

            Wardrobe(f, said);
            Plant(model, said);

            EditorUtility.SetDirty(f);
            said.AppendLine($"  pose: \"{f.poseState}\", freeze {f.freeze}, "
                          + $"sway {f.sway}");
            return f;
        }

        /// <summary>
        /// Nails his boots down, so the breathing and the sway move the man and not the
        /// floor he is standing on.
        ///
        /// On the Animator's own object, because OnAnimatorIK is called nowhere else --
        /// which is the same rule PlayerArm follows and the same mistake it documents.
        /// </summary>
        static void Plant(Animator model, System.Text.StringBuilder said)
        {
            if (model == null) return;

            MenuFootPlant plant = model.GetComponent<MenuFootPlant>();
            if (plant == null)
            {
                plant = Undo.AddComponent<MenuFootPlant>(model.gameObject);
                said.AppendLine($"  MenuFootPlant added to {model.name}, so the idle "
                              + "rocks his shoulders rather than sliding his feet");
            }

            Undo.RecordObject(plant, "Set up the menu scene");
            if (plant.animator == null) plant.animator = model;
            EditorUtility.SetDirty(plant);
        }

        /// <summary>
        /// The weapons and armour, taken off the match loadout that is already on him.
        ///
        /// Copied rather than rebuilt. PlayerLoadoutView carries every weapon and every
        /// piece of armour wired to an ItemId, which is the exact shape MenuFighter wants,
        /// and a second list assembled by hand is a second list to keep in step. It is
        /// switched off for the menu, but its arrays are still the wardrobe.
        ///
        /// Nothing is selected: an empty hand is a legitimate menu and picking a sword for
        /// somebody is not this tool's call. The point is that the dropdown has entries.
        /// </summary>
        static void Wardrobe(MenuFighter f, System.Text.StringBuilder said)
        {
            PlayerLoadoutView loadout = f.GetComponentInChildren<PlayerLoadoutView>(true);
            if (loadout == null)
            {
                said.AppendLine("  wardrobe: no PlayerLoadoutView on him, so weapons and "
                              + "armour stay empty — wire them by hand");
                return;
            }

            int took = 0;
            if ((f.weapons == null || f.weapons.Length == 0) && loadout.weapons != null)
            {
                f.weapons = loadout.weapons;
                took += f.weapons.Length;
            }
            if ((f.armour == null || f.armour.Length == 0) && loadout.armour != null)
            {
                f.armour = loadout.armour;
                took += f.armour.Length;
            }

            said.AppendLine(took > 0
                ? $"  wardrobe: {f.weapons.Length} weapon(s) and {f.armour.Length} piece(s) "
                  + "of armour taken from the match loadout; pick one in Weapon / Wearing"
                : "  wardrobe: already filled in, left alone");
        }

        /// <summary>
        /// Switches off everything on a match fighter that does something, and says how
        /// many. The Animator stays on -- it is the whole point -- and so does anything
        /// that only holds a reference, like the weapon socket.
        /// </summary>
        static int Quiet(Animator model)
        {
            PlayerController owner = model.GetComponentInParent<PlayerController>();
            if (owner == null) return 0;

            int off = 0;
            foreach (MonoBehaviour mb in owner.GetComponents<MonoBehaviour>())
            {
                if (mb == null || mb is MenuFighter || mb is MenuDirector || mb is MenuUI)
                    continue;
                if (!mb.enabled) continue;

                Undo.RecordObject(mb, "Set up the menu scene");
                mb.enabled = false;
                EditorUtility.SetDirty(mb);
                off++;
            }
            return off;
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

            // The gate is the one thing worth guessing at, and the BARS are the part of
            // it that rises -- "Arch" on its own is the stonework, which should stay put.
            // The gate also sets which way out is: the director walks toward it, not along
            // the camera's forward vector, and in this scene those are opposite.
            if (d.gate == null)
            {
                GameObject arch = GameObject.Find("Arch_bars") ?? GameObject.Find("Portcullis")
                                ?? GameObject.Find("Gate") ?? GameObject.Find("Arch");
                if (arch != null)
                {
                    d.gate = arch.transform;
                    said.AppendLine($"  gate: \"{arch.name}\" — check this is the part that "
                                  + "should RISE, not the stonework around it");

                    if (d.view != null)
                    {
                        Vector3 away = arch.transform.position - d.view.transform.position;
                        away.y = 0f;
                        float facing = Vector3.Dot(away.normalized, d.view.transform.forward);
                        said.AppendLine(facing < 0f
                            ? $"  the gate is BEHIND the camera ({away.magnitude:0.0} m), so "
                              + "PLAY reverses out under it without turning"
                            : $"  the gate is ahead of the camera ({away.magnitude:0.0} m)");
                    }
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
                          + "Dailies, Settings, Friends, Back");
            said.AppendLine("  readouts: wire GoldChanged / LeagueChanged to a text "
                          + "setter, LeagueFilled to an Image.fillAmount");
        }
    }
}
