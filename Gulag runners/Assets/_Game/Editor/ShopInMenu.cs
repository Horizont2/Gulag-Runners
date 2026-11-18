using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using GulagRunners.Game;
using GulagRunners.Meta;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Puts the shop and wardrobe INSIDE the menu, as a panel, and wires it to PLAY's
    /// neighbour buttons.
    ///
    /// The screen itself was always built for this: ShopBuilder hangs off a screen-space
    /// canvas and its own warning says "open the menu scene -- this hangs off the same
    /// canvas". What came later was ShopSceneSetup, which makes an EMPTY scene into a shop
    /// so the builder has somewhere to stand. Asked for plainly: the shop lives in the
    /// menu, so the scene setup is the wrong half of the pair and this is the right one.
    ///
    /// Three things were missing, and none of them is the screen:
    ///
    ///   THE PANEL WAS NEVER WIRED. MenuUI.shopPanel was fileID 0, so the SHOP button
    ///       warned "no panel assigned" and nothing opened. The same is true of profile,
    ///       dailies, settings and friends; this wires the shop and says which of the
    ///       others are still empty rather than leaving them to be found one at a time.
    ///
    ///   TWO CAMERAS WERE ENABLED AT THE SAME DEPTH. Main Camera and the hand-placed
    ///       ShopCamera, both on depth -1, which is undefined: Unity renders both and
    ///       which one survives is which was registered last. The shop camera is a POSE --
    ///       a place to stand, framed by hand -- so its Camera component comes off and
    ///       MenuShopView walks the real camera to it while the panel is open.
    ///
    ///   THE PANEL HAS TO START CLOSED. MenuUI.Open turns it on; something has to have
    ///       turned it off, or the menu opens with the wardrobe already over it.
    ///
    /// Run it in the menu scene. Safe to run twice: the screen is thrown away and rebuilt.
    /// </summary>
    public static class ShopInMenu
    {
        [MenuItem("Tools/Gulag Runners/Setup/Build the shop into the menu")]
        public static void Run()
        {
            Scene scene = SceneManager.GetActiveScene();

            MenuUI ui = Object.FindFirstObjectByType<MenuUI>(FindObjectsInactive.Include);
            if (ui == null)
            {
                Debug.LogWarning($"{scene.name} has no MenuUI in it, so there is no menu "
                                 + "for a shop to be a panel of. Open the menu scene.");
                return;
            }

            List<string> did = new List<string>();
            List<string> todo = new List<string>();

            // ---- the screen, built by the tool that already knows how
            CosmeticCatalogue catalogue = Find<CosmeticCatalogue>();
            if (catalogue == null)
                todo.Add("no CosmeticCatalogue asset was found, so the tabs will be empty");

            ShopBuilder.BuildInto(null, catalogue);

            GameObject panel = GameObject.Find("Shop UI");
            if (panel == null)
            {
                foreach (ShopUI found in Object.FindObjectsByType<ShopUI>(
                             FindObjectsInactive.Include, FindObjectsSortMode.None))
                    panel = found.gameObject;
            }

            if (panel == null)
            {
                Debug.LogWarning(
                    "The shop screen did not get built, so there is nothing to wire. "
                    + "ShopBuilder needs a screen-space Canvas at the root of the scene -- "
                    + "run Tools > Gulag Runners > Build the menu screen first.");
                return;
            }

            // ---- wired to the button, and shut
            Undo.RecordObject(ui, "Build the shop into the menu");
            ui.shopPanel = panel;
            EditorUtility.SetDirty(ui);
            did.Add("SHOP opens the panel");

            Undo.RecordObject(panel, "Build the shop into the menu");
            panel.SetActive(false);
            did.Add("it starts closed");

            // ---- the pose, and the second camera that must not be a camera
            Camera main = Camera.main;
            Transform pose = null;
            foreach (Camera cam in Object.FindObjectsByType<Camera>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (cam == main) continue;
                if (!cam.name.ToLowerInvariant().Contains("shop")) continue;

                pose = cam.transform;
                if (cam.enabled)
                {
                    Undo.RecordObject(cam, "Build the shop into the menu");
                    cam.enabled = false;
                    did.Add($"{cam.name} is a pose now, not a second camera");
                }
            }

            if (pose == null)
                todo.Add("no object named *Shop* with a Camera on it, so there is no pose "
                         + "to move to -- place one where the wardrobe should be framed "
                         + "and run this again");

            MenuShopView view = Object.FindFirstObjectByType<MenuShopView>(
                FindObjectsInactive.Include);
            if (view == null && ui != null)
                view = Undo.AddComponent<MenuShopView>(ui.gameObject);

            if (view != null)
            {
                Undo.RecordObject(view, "Build the shop into the menu");
                view.ui = ui;
                view.shopPose = pose;
                if (view.view == null && main != null) view.view = main.transform;
                EditorUtility.SetDirty(view);
                did.Add("the camera walks to the shop view");
            }

            // ---- say what else is still a fileID 0
            foreach ((string what, GameObject which) in new[]
                     {
                         ("profile", ui.profilePanel),
                         ("dailies", ui.dailiesPanel),
                         ("settings", ui.settingsPanel),
                         ("friends", ui.friendsPanel),
                     })
                if (which == null) todo.Add($"{what} has no panel assigned either");

            EditorSceneManager.MarkSceneDirty(scene);

            string said = $"{scene.name}: the shop is in the menu -- "
                          + string.Join(", ", did) + ".";
            if (todo.Count > 0)
                Debug.LogWarning(said + "\n  Still open:\n    " + string.Join("\n    ", todo));
            else
                Debug.Log(said);
        }

        static T Find<T>() where T : ScriptableObject
        {
            foreach (string guid in AssetDatabase.FindAssets("t:" + typeof(T).Name))
            {
                T got = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
                if (got != null) return got;
            }
            return null;
        }
    }
}
