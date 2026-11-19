using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using GulagRunners.Game;
using GulagRunners.Meta;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Turns an empty scene into a working shop and wardrobe, in one go.
    ///
    /// <see cref="ShopBuilder"/> already builds the screen and wires it, but it will not
    /// start from nothing: it looks for a screen-space Canvas and gives up with a warning
    /// when there is none, because it was written to hang off the menu scene's canvas. So
    /// "make a new scene and press one button" did not work, and the one button was five:
    /// make a canvas, set its scaler to the design size, add an EventSystem on the new
    /// input system, put a PlayerProgress in so the purse and the wardrobe have somewhere
    /// to read from, find the catalogue, find the sprite set, and only THEN build.
    ///
    /// This does those five and then calls the builder, so the screen comes up dressed in
    /// whatever art exists and working against the real save.
    ///
    /// What "working" means here, because a screen that looks right and does nothing is the
    /// easy half: ShopUI reads the catalogue and PlayerProgress, so the tabs list the real
    /// cosmetics, the prices are docs/09's, the gold is the player's actual gold, what they
    /// own reads as owned, and BUY spends. None of that needs art. The art only decides
    /// whether it looks like the game.
    ///
    /// It is deliberately NOT part of "Make this scene playable": a shop scene is not an
    /// arena, and running an arena tool on it would fill it with cages.
    /// </summary>
    public static class ShopSceneSetup
    {
        const float DesignW = 1920f;
        const float DesignH = 864f;
        const string CanvasName = "Shop Canvas";

        [MenuItem("Tools/Gulag Runners/Setup/Make this the shop scene")]
        public static void Run()
        {
            List<string> did = new List<string>();
            List<string> missing = new List<string>();

            Canvas canvas = Screen_(did);
            Events(did);
            PlayerProgress progress = Progress(did);
            CosmeticCatalogue catalogue = Catalogue(missing);
            UiSpriteSet set = Sprites(missing);

            ShopBuilder.BuildInto(set, catalogue);

            // The builder hangs itself off whatever canvas it found. If that was not the one
            // just made, say so rather than leaving somebody to wonder which of two canvases
            // their screen is on.
            ShopUI view = Object.FindFirstObjectByType<ShopUI>();
            if (view != null && canvas != null
                && view.GetComponentInParent<Canvas>() != null
                && view.GetComponentInParent<Canvas>().rootCanvas != canvas)
            {
                missing.Add("the screen was built on a canvas that was already in this "
                            + "scene, not the one this tool made. That is fine if it was "
                            + "deliberate; if not, delete the other canvas and run this "
                            + "again");
            }

            if (progress != null && view != null && view.progress == null)
            {
                Undo.RecordObject(view, "Make this the shop scene");
                view.progress = progress;
                EditorUtility.SetDirty(view);
                did.Add("pointed the screen at the save");
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            string said = "Shop scene: " + string.Join(", ", did) + ".";
            if (missing.Count > 0)
                said += "\n\nStill wanted:\n  - " + string.Join("\n  - ", missing);
            else
                said += "\n\nNothing missing. Press Play: the tabs list the real catalogue, "
                        + "the gold is the player's own, and BUY spends it.";

            Debug.Log(said);
        }

        /// <summary>
        /// The canvas, at the design size.
        ///
        /// Expand rather than match-on-height, for the reason docs/28 gives and MenuBuilder
        /// repeats: the sheet is 20:9, and matched on height a 16:9 screen gets a canvas
        /// NARROWER than the design, which squeezes the two top corners towards each other.
        /// Expand never lets the canvas be smaller than the sheet in either direction.
        /// </summary>
        static Canvas Screen_(List<string> did)
        {
            foreach (Canvas c in Object.FindObjectsByType<Canvas>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (c.renderMode == RenderMode.ScreenSpaceOverlay && c.transform.parent == null)
                {
                    did.Add("used the canvas already here");
                    return c;
                }
            }

            GameObject go = new GameObject(CanvasName,
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(go, "Make this the shop scene");

            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(DesignW, DesignH);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            did.Add($"made a canvas at {DesignW:0}x{DesignH:0}");
            return canvas;
        }

        /// <summary>
        /// An EventSystem on the NEW input system.
        ///
        /// InputSystemUIInputModule and not StandaloneInputModule: this project is on the
        /// new input system, and the old module silently does nothing there -- every button
        /// on the screen draws, highlights on nothing, and never fires. It is the most
        /// confusing way for a screen to be broken, because it looks finished.
        /// </summary>
        static void Events(List<string> did)
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null)
            {
                did.Add("found an EventSystem");
                return;
            }

            GameObject go = new GameObject("EventSystem", typeof(EventSystem),
                                           typeof(InputSystemUIInputModule));
            Undo.RegisterCreatedObjectUndo(go, "Make this the shop scene");
            did.Add("added an EventSystem");
        }

        /// <summary>
        /// The save. Without one the shop has no gold to show, nothing owned, and BUY has
        /// nothing to spend from -- it would draw correctly and be a picture.
        /// </summary>
        static PlayerProgress Progress(List<string> did)
        {
            PlayerProgress found = PlayerProgress.Instance != null
                ? PlayerProgress.Instance
                : Object.FindFirstObjectByType<PlayerProgress>();
            if (found != null)
            {
                did.Add("found the save");
                return found;
            }

            GameObject go = new GameObject("Player Progress");
            Undo.RegisterCreatedObjectUndo(go, "Make this the shop scene");
            found = Undo.AddComponent<PlayerProgress>(go);
            did.Add("added a PlayerProgress (it loads from PlayerPrefs on Play)");
            return found;
        }

        /// <summary>
        /// The catalogue, found rather than dragged.
        ///
        /// There is meant to be exactly one. Two would be the real problem -- the shop would
        /// list one and the wardrobe check the other -- so that is reported rather than
        /// silently resolved by picking the first.
        /// </summary>
        static CosmeticCatalogue Catalogue(List<string> missing)
        {
            string[] found = AssetDatabase.FindAssets("t:CosmeticCatalogue");
            if (found.Length == 0)
            {
                missing.Add("a CosmeticCatalogue. Make one with Assets > Create > Gulag "
                            + "Runners > Cosmetic catalogue; without it every tab is empty");
                return null;
            }

            if (found.Length > 1)
            {
                missing.Add($"ONE catalogue: there are {found.Length}. The shop would list "
                            + "one and the wardrobe read another, and the two would disagree "
                            + "about what the player owns");
            }

            return AssetDatabase.LoadAssetAtPath<CosmeticCatalogue>(
                AssetDatabase.GUIDToAssetPath(found[0]));
        }

        /// <summary>
        /// The sprite set the shop's art hangs on.
        ///
        /// Picked by what it CONTAINS and not by its name: a set is the right one when it
        /// has the shop's keys in it, and a project with several sets (there is one per
        /// screen) would otherwise be a coin toss decided by alphabetical order.
        /// </summary>
        static UiSpriteSet Sprites(List<string> missing)
        {
            UiSpriteSet best = null;
            int bestKeys = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:UiSpriteSet"))
            {
                UiSpriteSet set = AssetDatabase.LoadAssetAtPath<UiSpriteSet>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (set == null || set.slots == null) continue;

                int keys = 0;
                foreach (UiSpriteSet.Slot slot in set.slots)
                    if (slot != null && slot.key != null && slot.key.StartsWith("shop/")) keys++;

                if (keys > bestKeys) { bestKeys = keys; best = set; }
            }

            if (best == null)
            {
                missing.Add("a UiSpriteSet with the shop's keys in it. Open Tools > Gulag "
                            + "Runners > Build the shop screen once: the window lists every "
                            + "piece the screen wants, with its size and what it is for");
                return null;
            }

            int dressed = 0;
            foreach (UiSpriteSet.Slot slot in best.slots)
                if (slot != null && slot.key != null && slot.key.StartsWith("shop/")
                    && slot.sprite != null) dressed++;

            if (dressed < bestKeys)
            {
                missing.Add($"art for {bestKeys - dressed} of the shop's {bestKeys} pieces. "
                            + "The screen builds and works without them -- it comes up in "
                            + "flat colour. `cd docs/refs && python3 ui_export_manifest.py` "
                            + "prints exactly what to export from Figma, at what size, with "
                            + "which nine-slice border and under which filename");
            }

            return best;
        }
    }
}
