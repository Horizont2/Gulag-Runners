using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using GulagRunners.Game;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Builds the opening a round gets, and takes the cages out of it.
    ///
    /// What a round used to open with: three seconds in MatchPhase.Cages with the doors
    /// coming up, a camera move that ran on EVERY round of a series, and no number on
    /// screen at all. Three of the four arenas had no openingShot authored either, so in
    /// those the move was a sway over a static frame. Asked for plainly: the cages go, the
    /// start is the same everywhere, the move happens once per arena, and there is a
    /// countdown.
    ///
    /// THE CAGES WERE ONLY EVER A PICTURE. MatchPhase.Cages is what stops a fighter acting
    /// -- InputAllowed reads it directly -- and CageDoor is a component that slides a
    /// transform while that phase runs. So removing the doors changes what the round LOOKS
    /// like and nothing about what it IS: the three second hold, its timing, and every
    /// mirror and test built on it are untouched. The phase keeps its name, which is now a
    /// misnomer; renaming an enum the net protocol carries is a separate job from this one.
    ///
    /// What it leaves in the scene:
    ///
    ///   RoundIntroSmoke     on the camera. Four layers at four distances, drifting aside
    ///                       at four speeds, on the first round of an arena only.
    ///   RoundCountdown      3, 2, 1, FIGHT, driven off the simulation's own cage progress
    ///                       so the last beat lands on the tick control comes back.
    ///   RoundIntroCamera    firstRoundOnly = 1, so the move is the arrival and not the
    ///                       wait before every round.
    ///
    /// Run it once per arena scene. It is safe to run twice.
    /// </summary>
    public static class IntroSetup
    {
        const string Band = "Assets/_Game/Effects/fx_intro_smoke.png";

        /// <summary>
        /// What the smoke is made of, per arena. The same hazes the backdrops are lifted
        /// towards, so the bank reads as the arena's own air rather than as grey fog
        /// borrowed from somewhere else.
        /// </summary>
        static readonly Dictionary<string, Color> Air = new Dictionary<string, Color>
        {
            { "Arena_Mine", new Color(0.27f, 0.24f, 0.22f) },
            { "Arena_Forest", new Color(0.36f, 0.45f, 0.47f) },
            { "Arena_Nordic fortress", new Color(0.44f, 0.51f, 0.61f) },
            { "Arena_Egypt", new Color(0.44f, 0.36f, 0.52f) },
        };

        [MenuItem("Tools/Gulag Runners/Setup/Build the round opening")]
        public static void Run()
        {
            Scene scene = SceneManager.GetActiveScene();

            MatchState match = Object.FindFirstObjectByType<MatchState>(
                FindObjectsInactive.Include);
            if (match == null)
            {
                Debug.LogWarning($"{scene.name} has no MatchState, so there is no round for "
                                 + "an opening to belong to.");
                return;
            }

            List<string> did = new List<string>();

            // ---- the cages come out
            int caged = 0;
            foreach (CageDoor door in Object.FindObjectsByType<CageDoor>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                // The whole object, not just the component: a door with nothing driving it
                // is a slab of geometry standing across a spawn point.
                Undo.DestroyObjectImmediate(door.gameObject);
                caged++;
            }
            if (caged > 0) did.Add($"{caged} cage door(s) removed");

            // ---- the camera's move becomes the arrival
            foreach (RoundIntroCamera intro in Object.FindObjectsByType<RoundIntroCamera>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                bool already = intro.firstRoundOnly == 1
                               && intro.style == RoundIntroCamera.IntroStyle.Emerge;
                if (already) continue;

                Undo.RecordObject(intro, "Build the round opening");
                intro.firstRoundOnly = 1;

                // THE MOVE HAS TO AGREE WITH THE SMOKE. The arenas were on three different
                // shots -- Forest on the cage door, Egypt and Mine on the shaft, Nordic on
                // the crane -- and all three fight the reveal. The cage shot's two jerks
                // are somebody hauling a grille up and there are no grilles left; the
                // crane and the shaft both arrive from ABOVE, so the camera descends
                // through a bank that is sliding sideways and the two moves have nothing
                // to say to each other. Emerge is a dolly out: something was close and in
                // the way, and now it is not.
                intro.style = RoundIntroCamera.IntroStyle.Emerge;

                EditorUtility.SetDirty(intro);
                did.Add("the camera comes out of the smoke, first round only");
            }

            // ---- the smoke, on the camera that renders the arena
            Texture2D band = AssetDatabase.LoadAssetAtPath<Texture2D>(Band);
            if (band == null)
                Debug.LogWarning($"{Band} is missing. Run docs/refs/make_intro_smoke.py; "
                                 + "the opening will have a camera move and a countdown but "
                                 + "nothing to come out of.");

            int smoked = 0;
            foreach (Camera cam in Object.FindObjectsByType<Camera>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (cam.GetComponent<SideViewCamera>() == null) continue;

                RoundIntroSmoke smoke = cam.GetComponent<RoundIntroSmoke>();
                if (smoke == null) smoke = Undo.AddComponent<RoundIntroSmoke>(cam.gameObject);

                Undo.RecordObject(smoke, "Build the round opening");
                smoke.matchState = match;
                smoke.view = cam;
                smoke.band = band;
                if (Air.TryGetValue(scene.name, out Color air)) smoke.tint = air;
                EditorUtility.SetDirty(smoke);
                smoked++;
            }
            if (smoked > 0) did.Add($"smoke on {smoked} camera(s)");

            // ---- and the numbers, once for the scene
            RoundCountdown count = Object.FindFirstObjectByType<RoundCountdown>(
                FindObjectsInactive.Include);
            if (count == null)
            {
                GameObject holder = new GameObject("Round Countdown");
                Undo.RegisterCreatedObjectUndo(holder, "Build the round opening");
                count = Undo.AddComponent<RoundCountdown>(holder);
                did.Add("a countdown");
            }
            Undo.RecordObject(count, "Build the round opening");
            count.matchState = match;
            EditorUtility.SetDirty(count);

            EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log(
                $"{scene.name}: the round opening is built -- "
                + (did.Count > 0 ? string.Join(", ", did) : "nothing had changed")
                + ".\n  The three second hold itself is untouched: it is MatchPhase.Cages "
                + "that stops a fighter acting, and only its scenery has gone.");
        }
    }
}
