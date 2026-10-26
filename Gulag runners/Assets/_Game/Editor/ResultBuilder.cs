using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using GulagRunners.Game;

// Names which UnityEventTools is meant. Unity has only one; the reference assemblies
// carry a second name for it and the ambiguity only shows up on some machines.
using UnityEventTools = UnityEditor.Events.UnityEventTools;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Builds the screen the match ends on.
    ///
    /// MatchState has raised SeriesEnded since the match clock was written and nothing has
    /// ever subscribed to it. The arena plays its finishing beat and then sits there: no
    /// result, no rematch, no way back to the menu. This is the panel that answers it, and
    /// it is built rather than hand-placed for the same reason the HUD and the menu are --
    /// a screen assembled by hand is a screen somebody has to assemble again in the next
    /// scene, and SetupScene is already missing every component Arena_Demo was given.
    ///
    /// Plain colours, no sprite sheet. The result screen has no artwork yet and inventing
    /// a dependency on files that do not exist would mean a tool that cannot be run. It
    /// lays out a dimmer, two lines and two buttons at the right sizes and in the right
    /// places; skinning it is swapping the Image sprites, with nothing to rewire.
    /// </summary>
    public static class ResultBuilder
    {
        const float DesignW = 1920f;
        const float DesignH = 864f;

        [MenuItem("Tools/Gulag Runners/Build the result screen")]
        public static void Build()
        {
            MatchState match = Object.FindFirstObjectByType<MatchState>();
            if (match == null)
            {
                Debug.LogWarning("No MatchState in this scene, so there is no match for a " +
                                 "result screen to be the end of. Open the arena scene.");
                return;
            }

            Canvas canvas = Canvas();
            if (canvas == null)
            {
                Debug.LogWarning("No screen-space Canvas in this scene. Run Tools > Gulag " +
                                 "Runners > Build the combat HUD first; this hangs off it.");
                return;
            }

            // Thrown away and rebuilt, so running it twice does not leave two of them --
            // and so an edit to this file is one menu click from being on screen.
            Transform old = canvas.transform.Find("Result");
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            RectTransform root = Panel("Result", canvas.transform);

            // Full-bleed and a raycast target: it is also what stops a thumb still resting
            // on the attack stick from driving a fighter who is already dead.
            Image dim = Undo.AddComponent<Image>(
                Make("Dimmer", root, 0, 0, DesignW, DesignH, 0f).gameObject);
            dim.color = new Color(0.04f, 0.04f, 0.05f, 0.78f);
            dim.raycastTarget = true;

            MenuLabel headline = Line(root, "Headline", "VICTORY", 230, 110, 96,
                                      new Color(0.96f, 0.88f, 0.70f));
            MenuLabel score = Line(root, "Score", "2 — 1", 360, 70, 56,
                                   new Color(1f, 1f, 1f, 0.78f));

            MatchResultView view = Undo.AddComponent<MatchResultView>(root.gameObject);
            Undo.RecordObject(view, "Build the result screen");
            view.match = match;
            view.panel = root.gameObject;

            List<string> wired = new List<string>();
            Wire(view.HeadlineChanged, headline.Set, "headline", wired);
            Wire(view.ScoreChanged, score.Set, "score", wired);

            Face(root, "Button / Rematch", "REMATCH", DesignW * 0.5f - 420, 560, 400, 104,
                 new Color(0.91f, 0.64f, 0.24f), new Color(0.09f, 0.07f, 0.05f),
                 new UnityAction(view.Rematch), wired);
            Face(root, "Button / Menu", "MENU", DesignW * 0.5f + 20, 560, 400, 104,
                 new Color(1f, 1f, 1f, 0.12f), new Color(0.93f, 0.93f, 0.93f),
                 new UnityAction(view.ToMenu), wired);

            EnsureEventSystem();

            // Switched off, because that is the contract the rest of the project's UI
            // keeps: every panel is already in the scene and nothing is made at play time.
            root.gameObject.SetActive(false);

            EditorUtility.SetDirty(canvas);
            Debug.Log(
                $"Result screen built on \"{canvas.name}\": {string.Join(", ", wired)} " +
                "wired.\n" +
                "It goes up " + view.showAfter.ToString("0.0") + " s after the match is " +
                "decided, which is room for MatchOutcomeView's freeze, slow motion and " +
                "camera push. Rematch deals a fresh series in this arena; Menu loads \"" +
                view.menuScene + "\".\n" +
                "To see it without winning a match, switch the Result object on in the " +
                "hierarchy — it is laid out against real-looking content on purpose.",
                root.gameObject);
        }

        /// <summary>The screen-space canvas the HUD is on, or any screen-space one.</summary>
        static Canvas Canvas()
        {
            HudView hud = Object.FindFirstObjectByType<HudView>();
            Canvas found = hud != null ? hud.GetComponentInParent<Canvas>() : null;
            if (found != null) return found.rootCanvas;

            foreach (Canvas c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include,
                                                                  FindObjectsSortMode.None))
                if (c.renderMode != RenderMode.WorldSpace)
                    return c.rootCanvas;
            return null;
        }

        /// <summary>One centred line, behind a MenuLabel so a UnityEvent has a method.</summary>
        static MenuLabel Line(Transform parent, string name, string text, float y,
                              float h, int size, Color colour)
        {
            RectTransform rt = Make(name, parent, 0, y, DesignW, h, 0f);
            Text label = Undo.AddComponent<Text>(rt.gameObject);
            label.text = text;
            label.fontSize = size;
            label.color = colour;
            label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false;
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            MenuLabel wrap = Undo.AddComponent<MenuLabel>(rt.gameObject);
            wrap.target = label;
            wrap.placeholder = text;
            return wrap;
        }

        /// <summary>A coloured slab with a word on it, wired to one method.</summary>
        static void Face(Transform parent, string name, string word, float x, float y,
                         float w, float h, Color fill, Color ink, UnityAction action,
                         List<string> wired)
        {
            RectTransform rt = Make(name, parent, x, y, w, h, 0f);
            Image face = Undo.AddComponent<Image>(rt.gameObject);
            face.color = fill;
            face.raycastTarget = true;

            RectTransform wordRt = Make(word, rt, 0, 0, w, h, 0f);
            Text t = Undo.AddComponent<Text>(wordRt.gameObject);
            t.text = word;
            t.fontSize = 44;
            t.color = ink;
            t.alignment = TextAnchor.MiddleCenter;
            t.raycastTarget = false;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            Button button = Undo.AddComponent<Button>(rt.gameObject);
            button.targetGraphic = face;
            UnityEventTools.AddPersistentListener(button.onClick, action);
            wired.Add(name.Substring(name.LastIndexOf('/') + 1).Trim().ToLowerInvariant());
        }

        /// <summary>
        /// The GENERIC AddPersistentListener. The typed ones (AddStringPersistentListener
        /// and its siblings) bind a fixed argument VALUE -- "call Set with the word
        /// Ready" -- which is not what a readout is: a readout passes its own argument
        /// through, and that is this overload.
        /// </summary>
        static void Wire<T>(UnityEvent<T> ev, UnityAction<T> call, string what,
                            List<string> wired)
        {
            if (ev == null || call == null) return;
            UnityEventTools.AddPersistentListener(ev, call);
            wired.Add(what);
        }

        static RectTransform Panel(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Build the result screen");
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        /// <summary>
        /// One rect at its design coordinate on the same 1920x864 sheet the HUD uses,
        /// measured from the top-left. A result screen is the one piece of interface that
        /// genuinely wants the middle of the screen rather than an edge, so everything
        /// here is laid out from the left edge across the full design width and centred
        /// by the text's own alignment -- which keeps working on a wider canvas, where
        /// pinning to the centre by hand would not.
        /// </summary>
        static RectTransform Make(string name, Transform parent, float fx, float fy,
                                  float w, float h, float anchorX)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Build the result screen");
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(anchorX, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(fx - anchorX * DesignW, -fy);
            return rt;
        }

        /// <summary>
        /// A canvas with no EventSystem receives no taps and says nothing about it. This
        /// project is on the Input System package, so the module has to be its own --
        /// StandaloneInputModule reads the old UnityEngine.Input and throws when touched.
        /// </summary>
        static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null) return;
            GameObject go = new GameObject("EventSystem", typeof(EventSystem),
                                           typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
            Undo.RegisterCreatedObjectUndo(go, "Build the result screen");
        }
    }
}
