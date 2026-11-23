using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using GulagRunners.Game;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Runs the effect builders over EVERY scene in the build, in one go.
    ///
    /// Every tool in this folder works on the scene that happens to be open, which is right
    /// for a tool somebody is using while looking at an arena. It is wrong for a change that
    /// belongs in all of them. There are five scenes in the build and four arenas among
    /// them, so "just run it in each one" is four opens, four clicks and four saves, done in
    /// the right order, with nothing to say afterwards which ones were actually done.
    ///
    /// That is not a chore worth automating because it is tedious. It is worth automating
    /// because it is the kind of chore people do THREE times out of four, and the arena that
    /// got missed is then the one arena where landing makes no dust -- a difference nobody
    /// attributes to a missed click, because the obvious explanation is that the effect is
    /// broken.
    ///
    /// It saves each scene as it goes, which is the point, so it asks first. An editor tool
    /// that writes four scenes without warning is one nobody dares run.
    /// </summary>
    public static class EffectsEverywhere
    {
        [MenuItem("Tools/Gulag Runners/Build the effects in EVERY scene")]
        public static void Run()
        {
            List<string> arenas = new List<string>();
            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
                if (s.enabled && !string.IsNullOrEmpty(s.path))
                    arenas.Add(s.path);

            if (arenas.Count == 0)
            {
                Debug.LogWarning("No scenes are enabled in the build settings, so there is "
                                 + "nothing to build the effects into.");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Build the effects everywhere?",
                    $"This opens and SAVES {arenas.Count} scene(s):\n\n  "
                    + string.Join("\n  ", arenas)
                    + "\n\nEach one gets the impact, parry, clash, pickup and dust effects "
                    + "built and wired to its fighters, the gas announcement wired to "
                    + "its poison pool, and its parallax backdrop rebuilt. Scenes with no "
                    + "fighters are skipped and left untouched.",
                    "Build them", "Cancel"))
                return;

            // Whatever is open now is somebody's work in progress. Offer to keep it before
            // walking away from it.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            string openedAt = SceneManager.GetActiveScene().path;

            List<string> did = new List<string>();
            List<string> skipped = new List<string>();
            List<string> gassed = new List<string>();
            List<string> failed = new List<string>();

            try
            {
                for (int i = 0; i < arenas.Count; i++)
                {
                    EditorUtility.DisplayProgressBar(
                        "Building the effects", arenas[i], (i + 0.5f) / arenas.Count);

                    Scene scene = EditorSceneManager.OpenScene(arenas[i], OpenSceneMode.Single);

                    // A scene with no fighters is the menu, and the menu has nobody to hit.
                    if (Object.FindFirstObjectByType<FighterDamageView>() == null)
                    {
                        skipped.Add($"{scene.name} (no fighters in it)");
                        continue;
                    }

                    // Each builder in its own net. One of them throwing used to take the
                    // whole run with it -- and because the save is at the BOTTOM of this
                    // loop, every scene it had already finished went unsaved too. That is
                    // exactly what happened: a MissingComponentException in the shadow
                    // setup, and four arenas that looked like the tool had simply never
                    // been run. A builder that fails should cost its own step, nothing more.
                    Step(scene, "hit effects", failed, HitEffectBuilder.Run);

                    // The gas announcement: the lamps dipping three seconds out, the jets,
                    // the collar quickening as the surface nears your feet. It needs
                    // GR_GasVent to exist, which the line above has just made, so the order
                    // here is not arbitrary.
                    if (Object.FindFirstObjectByType<PoisonPool>() != null
                        && Step(scene, "the gas announcement", failed, PoisonWarningSetup.Run))
                        gassed.Add(scene.name);

                    // The parallax layers behind the arena. This one knows which scenes it
                    // has a palette for and says so when it does not.
                    Step(scene, "the parallax backdrop", failed, BackdropBuilder.Run);

                    // The blob under each fighter.
                    Step(scene, "the ground shadows", failed, GroundShadowSetup.Run);

                    // And whatever hangs in the air -- in the one arena whose air is empty.
                    // This stands down by itself where there is weather already.
                    Step(scene, "the atmosphere", failed, ArenaAtmosphereSetup.Run);

                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    did.Add(scene.name);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();

                // Back where they were, so the tool does not leave somebody in a scene they
                // did not choose with a Hierarchy they do not recognise.
                if (!string.IsNullOrEmpty(openedAt))
                    EditorSceneManager.OpenScene(openedAt, OpenSceneMode.Single);
            }

            string said = did.Count > 0
                ? $"Effects built and saved in {did.Count} scene(s): {string.Join(", ", did)}."
                : "Nothing was built: no scene in the build has fighters in it.";

            if (gassed.Count > 0)
                said += $"\n\nThe gas announcement was wired in {gassed.Count} of them.";

            if (skipped.Count > 0)
                said += "\n\nSkipped: " + string.Join(", ", skipped);

            if (failed.Count > 0)
                said += $"\n\n{failed.Count} step(s) FAILED and were skipped: "
                        + string.Join(", ", failed)
                        + ". Those scenes were saved without them.";

            Debug.Log(said);
        }
        /// <summary>
        /// Runs one builder and keeps going if it throws.
        ///
        /// Returns whether it got through, so a caller can record the step as done. The
        /// failure is logged where it happened AND collected, because a single exception in
        /// the middle of a five-scene run is otherwise one red line in a console that is
        /// already full of warnings from third-party packages.
        /// </summary>
        static bool Step(Scene scene, string what, List<string> failed, System.Action build)
        {
            try
            {
                build();
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"{scene.name}: {what} failed and was skipped. "
                               + "The rest of this scene was still built and saved.\n" + e);
                failed.Add($"{scene.name}: {what}");
                return false;
            }
        }

    }
}
