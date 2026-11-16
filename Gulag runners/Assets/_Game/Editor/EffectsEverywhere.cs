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
                    + "built and wired to its fighters. Scenes with no fighters are skipped "
                    + "and left untouched.",
                    "Build them", "Cancel"))
                return;

            // Whatever is open now is somebody's work in progress. Offer to keep it before
            // walking away from it.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            string openedAt = SceneManager.GetActiveScene().path;

            List<string> did = new List<string>();
            List<string> skipped = new List<string>();

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

                    HitEffectBuilder.Run();
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

            if (skipped.Count > 0)
                said += "\n\nSkipped: " + string.Join(", ", skipped);

            Debug.Log(said);
        }
    }
}
