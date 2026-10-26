using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using GulagRunners.Game;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// What is missing from the scene you are about to play, said before you play it.
    ///
    /// There are two dozen setup tools in this menu and every one of them acts on the OPEN
    /// SCENE. That is a reasonable design and a bad trap: a scene that never had one run on
    /// it looks completely normal, plays without a single error, and is quietly missing a
    /// system. It has happened three times now and cost a session each:
    ///
    ///   the drawn arm   PlayerArm is added per scene, so Arena_Demo had it and SetupScene
    ///                   never did. The fight ran, the hitbox swung, damage landed -- and
    ///                   the arm on screen stayed in the clip's pose, ignoring the stick.
    ///                   Reported as "the stick does not match the arm", which it was.
    ///   the end         nothing subscribed to MatchState.SeriesEnded, so winning left the
    ///                   player standing in the arena with no result and no way out.
    ///   PLAY            Menu.unity has no MenuDirector, so the button that starts the game
    ///                   found nothing to call and did nothing at all, silently.
    ///
    /// Every one was invisible from inside Unity and obvious from outside it. So this is the
    /// outside, brought in: it names what is missing and the exact menu item that adds it,
    /// and it runs itself on the way into Play mode so nobody has to remember to ask.
    ///
    /// It changes nothing. Reading a scene cannot break one, and a check that edits is a
    /// check people stop running.
    /// </summary>
    public static class SceneDoctor
    {
        const string Silence = "GulagRunners.SceneDoctor.quiet";

        [MenuItem("Tools/Gulag Runners/Check this scene")]
        public static void Check()
        {
            List<string> faults = Faults();
            if (faults.Count == 0)
            {
                Debug.Log("Scene check: nothing missing. Everything this scene needs to be " +
                          "played is wired.");
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.Append($"Scene check: {faults.Count} thing(s) missing. The game will run "
                      + "without them and will be wrong in ways nothing reports:\n");
            foreach (string f in faults) sb.Append("\n  • ").Append(f);
            Debug.LogWarning(sb.ToString());
        }

        [MenuItem("Tools/Gulag Runners/Check this scene on Play")]
        static void Toggle() => EditorPrefs.SetBool(Silence, !EditorPrefs.GetBool(Silence));

        [MenuItem("Tools/Gulag Runners/Check this scene on Play", true)]
        static bool ToggleTick()
        {
            Menu.SetChecked("Tools/Gulag Runners/Check this scene on Play",
                            !EditorPrefs.GetBool(Silence));
            return true;
        }

        [InitializeOnLoadMethod]
        static void Listen() => EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.ExitingEditMode) return;
            if (EditorPrefs.GetBool(Silence)) return;

            List<string> faults = Faults();
            if (faults.Count == 0) return;

            StringBuilder sb = new StringBuilder();
            sb.Append($"This scene is missing {faults.Count} thing(s), so what you are "
                      + "about to play is not the whole game:\n");
            foreach (string f in faults) sb.Append("\n  • ").Append(f);
            sb.Append("\n\nTools > Gulag Runners > Check this scene on Play turns this off.");
            Debug.LogWarning(sb.ToString());
        };

        static List<string> Faults()
        {
            List<string> faults = new List<string>();

            MatchState match = Object.FindFirstObjectByType<MatchState>();
            MenuUI menu = Object.FindFirstObjectByType<MenuUI>();

            if (match != null) Arena(match, faults);
            if (menu != null) MenuScene(faults);

            return faults;
        }

        static void Arena(MatchState match, List<string> faults)
        {
            PlayerController[] fighters =
                Object.FindObjectsByType<PlayerController>(FindObjectsInactive.Include,
                                                           FindObjectsSortMode.None);
            if (fighters.Length == 0)
                faults.Add("a match runs here but there are no fighters in the scene.");

            int noArm = 0, noIk = 0, noPass = 0, wrongObject = 0;
            foreach (PlayerController f in fighters)
            {
                Animator animator = f.GetComponentInChildren<Animator>(true);
                if (animator == null) continue;

                PlayerArm arm = f.GetComponentInChildren<PlayerArm>(true);
                if (arm == null) noArm++;
                else if (arm.gameObject != animator.gameObject) wrongObject++;

                if (animator.GetComponent<PlayerIK>() == null) noIk++;
                if (!BaseLayerHasPass(animator.runtimeAnimatorController)) noPass++;
            }

            if (noArm > 0)
                faults.Add($"{noArm} fighter(s) have no PlayerArm, so the attack stick "
                           + "swings a hitbox nobody draws: the arm on screen stays in "
                           + "whatever pose the clip has. "
                           + "Tools > Gulag Runners > Setup > Set up IK");
            if (wrongObject > 0)
                faults.Add($"{wrongObject} PlayerArm(s) are not on the object that owns the "
                           + "Animator, and OnAnimatorIK is only ever called there, so they "
                           + "never run. Tools > Gulag Runners > Setup > Set up IK");
            if (noIk > 0)
                faults.Add($"{noIk} fighter(s) have no PlayerIK, so hands and feet sit where "
                           + "a clip built for a 1.8 m rig put them. "
                           + "Tools > Gulag Runners > Setup > Set up IK");
            if (noPass > 0)
                faults.Add($"{noPass} fighter(s) have no IK Pass on their animator's base "
                           + "layer, so OnAnimatorIK is never called and NO ik runs at all. "
                           + "Tools > Gulag Runners > Setup > Set up IK");

            if (Object.FindFirstObjectByType<MatchResultView>() == null)
                faults.Add("nothing ends the match: no MatchResultView, so winning leaves "
                           + "the player in the arena with no result and no way out. "
                           + "Tools > Gulag Runners > Build the result screen");

            if (Object.FindFirstObjectByType<HudView>() == null)
                faults.Add("no HudView, so there is no health, no stamina and no controls. "
                           + "Tools > Gulag Runners > Build the combat HUD");
            else
                foreach (TouchInputSource touch in
                         Object.FindObjectsByType<TouchInputSource>(FindObjectsInactive.Include,
                                                                    FindObjectsSortMode.None))
                    if (touch.aimStick == null)
                    {
                        faults.Add($"\"{touch.name}\" has no attack stick wired, so the one "
                                   + "control the whole fight is made of does nothing. "
                                   + "Tools > Gulag Runners > Build the combat HUD");
                        break;
                    }
        }

        /// <summary>Named MenuScene and not Menu: UnityEditor.Menu is a real type, and a
        /// private method shadowing it makes Menu.SetChecked above stop compiling.</summary>
        static void MenuScene(List<string> faults)
        {
            if (Object.FindFirstObjectByType<MenuDirector>() == null)
                faults.Add("no MenuDirector, so PLAY finds nothing to call and the game "
                           + "cannot be started from the menu at all. "
                           + "Tools > Gulag Runners > Setup > Set up the menu scene");
        }

        /// <summary>
        /// Unity only calls OnAnimatorIK for layers with IK Pass ticked, and a controller
        /// without it makes every IK component in the project dead code with no warning.
        /// </summary>
        static bool BaseLayerHasPass(RuntimeAnimatorController runtime)
        {
            AnimatorController controller = runtime as AnimatorController;
            if (controller == null) return true;        // cannot tell; do not cry wolf

            AnimatorControllerLayer[] layers = controller.layers;
            return layers.Length > 0 && layers[0].iKPass;
        }
    }
}
