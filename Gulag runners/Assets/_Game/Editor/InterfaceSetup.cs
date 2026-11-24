using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using GulagRunners.Game;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Puts the two interface components that belong in a scene into it, in whatever scene
    /// is open.
    ///
    /// Both of them were being added at PLAY TIME, which is the thing this project does not
    /// do anywhere else and for a good reason: a component built in code is a component
    /// with no inspector. PanelRise has two numbers that decide how a panel arrives and
    /// neither could be reached without editing C#; it appeared in no scene, in no
    /// hierarchy, and in no search. MenuUI and MatchResultView now say out loud when a
    /// panel has not got one rather than quietly growing it, which is what this tool is
    /// for.
    ///
    /// SafeArea is the other half and was not being done at all. See its own summary: this
    /// game puts the health bars and the clock in the top corners and the stick and the
    /// four buttons in the bottom ones, which on any phone made in the last six years is
    /// the notch, the punch-hole and the home indicator -- and a touch inside the home
    /// indicator's strip is swallowed by the system, so it is a controls fault as well as
    /// a layout one.
    ///
    /// Additive and safe to run twice. Nothing is destroyed, nothing is reparented, and a
    /// component already there keeps whatever it was tuned to.
    /// </summary>
    public static class InterfaceSetup
    {
        [MenuItem("Tools/Gulag Runners/Setup/Make the interface answer")]
        public static void Setup()
        {
            Scene scene = SceneManager.GetActiveScene();
            List<string> made = new List<string>();

            // ---------------------------------------------------------------- safe area
            //
            // Every screen-space canvas, because every one of them is laid out against the
            // whole sheet. A world-space canvas is a thing IN the arena -- a sign, a label
            // over a chest -- and has no screen edges to keep away from.
            foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (canvas.renderMode == RenderMode.WorldSpace) continue;
                if (canvas != canvas.rootCanvas) continue;
                if (canvas.GetComponent<SafeArea>() != null) continue;

                Undo.AddComponent<SafeArea>(canvas.gameObject);
                EditorUtility.SetDirty(canvas.gameObject);
                made.Add($"safe area on \"{canvas.name}\"");
            }

            // ------------------------------------------------------------------- panels
            //
            // The ones the game actually opens, by the fields that open them, rather than
            // anything that looks like a panel. A component on the wrong object is worse
            // than none: PanelRise moves what it is on, so one on a full-screen scrim
            // slides the scrim.
            List<GameObject> panels = new List<GameObject>();

            foreach (MenuUI menu in Object.FindObjectsByType<MenuUI>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Add(panels, menu.profilePanel);
                Add(panels, menu.shopPanel);
                Add(panels, menu.dailiesPanel);
                Add(panels, menu.settingsPanel);
                Add(panels, menu.friendsPanel);
            }

            foreach (MatchResultView result in Object.FindObjectsByType<MatchResultView>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                Add(panels, result.panel);

            foreach (GameObject panel in panels)
            {
                if (panel.GetComponent<PanelRise>() != null) continue;
                Undo.AddComponent<PanelRise>(panel);
                EditorUtility.SetDirty(panel);
                made.Add($"rise on \"{panel.name}\"");
            }

            // ------------------------------------------------------------------ buttons
            //
            // Every Button under a screen-space canvas. The combat HUD's four control rings
            // already answer the thumb -- HudView sinks and brightens them -- and
            // everything outside the arena was left on Unity's own colour tint, which on a
            // dark interface with dark buttons is a change of a few per cent that nobody
            // sees on a phone in daylight.
            foreach (Button button in Object.FindObjectsByType<Button>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Canvas canvas = button.GetComponentInParent<Canvas>();
                if (canvas == null || canvas.rootCanvas.renderMode == RenderMode.WorldSpace)
                    continue;
                if (button.GetComponent<UiPress>() != null) continue;

                Undo.AddComponent<UiPress>(button.gameObject);
                EditorUtility.SetDirty(button.gameObject);
                made.Add($"press on \"{button.name}\"");
            }

            EditorSceneManager.MarkSceneDirty(scene);

            if (made.Count == 0)
            {
                Debug.Log($"The interface in {scene.name} already answers: every canvas has "
                          + "a safe area and every panel a rise.");
                return;
            }

            Debug.Log(
                $"{scene.name}: {string.Join(", ", made)}.\n"
                + "The safe area insets each canvas's full-screen children by "
                + "Screen.safeArea, so the top bar and the controls come out from under "
                + "the notch and off the home indicator. A child that is NOT a full-screen "
                + "stretch is left alone, which is how a backdrop or a scrim still bleeds "
                + "to the glass.\n"
                + "Nothing of this is visible in the editor's Game view unless you pick a "
                + "device profile that HAS cut-outs -- the simulator's iPhone profiles do. "
                + "On a plain 16:9 the inset is zero and nothing moves, which is correct "
                + "and is also why this was never noticed.");
        }

        static void Add(List<GameObject> into, GameObject panel)
        {
            if (panel != null && !into.Contains(panel)) into.Add(panel);
        }
    }
}
