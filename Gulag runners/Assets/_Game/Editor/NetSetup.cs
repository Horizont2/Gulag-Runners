using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using GulagRunners.Game;
using GulagRunners.Net;

using UnityEventTools = UnityEditor.Events.UnityEventTools;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Puts two-phone play into the open arena scene: the link, the clock, and the two
    /// buttons that start it.
    ///
    /// Built rather than placed by hand for the reason every other screen here is: a
    /// scene somebody has to assemble again is a scene that gets assembled once and then
    /// forgotten in the next one. Plain colours, no artwork -- this is the screen you see
    /// for four seconds before a test match and never again.
    /// </summary>
    public static class NetSetup
    {
        const float DesignW = 1920f;
        const float DesignH = 864f;

        [MenuItem("Tools/Gulag Runners/Setup/Set up two-phone play")]
        public static void Setup()
        {
            MatchState match = Object.FindFirstObjectByType<MatchState>();
            if (match == null)
            {
                Debug.LogWarning("No MatchState in this scene. Open the arena scene.");
                return;
            }

            LanLink link = Object.FindFirstObjectByType<LanLink>();
            NetMatch clock = Object.FindFirstObjectByType<NetMatch>();
            if (link == null || clock == null)
            {
                GameObject go = link != null ? link.gameObject
                              : clock != null ? clock.gameObject
                              : new GameObject("Net");
                if (link == null && clock == null)
                    Undo.RegisterCreatedObjectUndo(go, "Set up two-phone play");
                if (link == null) link = Undo.AddComponent<LanLink>(go);
                if (clock == null) clock = Undo.AddComponent<NetMatch>(go);
            }

            Undo.RecordObject(clock, "Set up two-phone play");
            clock.link = link;
            clock.match = match;

            Canvas canvas = Canvas();
            if (canvas == null)
            {
                Debug.LogWarning("No screen-space Canvas. Run Build the combat HUD first.");
                return;
            }

            Transform old = canvas.transform.Find("Connect");
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            // The view sits on the root and everything visible hangs off a child, because
            // connecting hides the panel -- and a component on the object it switches off
            // stops updating, which would leave nothing watching for the link dropping.
            RectTransform root = Panel("Connect", canvas.transform);
            RectTransform panel = Panel("Panel", root);

            Image dim = Undo.AddComponent<Image>(
                Make("Dimmer", panel, 0, 0, DesignW, DesignH).gameObject);
            dim.color = new Color(0.04f, 0.04f, 0.05f, 0.92f);
            dim.raycastTarget = true;

            MenuLabel status = Line(panel, "Status", "Tap HOST on one phone and JOIN on the other.",
                                    250, 120, 40, new Color(1f, 1f, 1f, 0.92f));
            MenuLabel address = Line(panel, "Address", "0.0.0.0", 380, 44, 28,
                                     new Color(1f, 1f, 1f, 0.45f));

            NetConnectView view = Undo.AddComponent<NetConnectView>(root.gameObject);
            Undo.RecordObject(view, "Set up two-phone play");
            view.link = link;
            view.panel = panel.gameObject;

            List<string> wired = new List<string>();
            Wire(view.StatusChanged, status.Set, "status", wired);
            Wire(view.AddressChanged, address.Set, "address", wired);

            Face(panel, "Button / Host", "HOST", DesignW * 0.5f - 420, 520, 400, 104,
                 new Color(0.91f, 0.64f, 0.24f), new Color(0.09f, 0.07f, 0.05f),
                 new UnityAction(view.Host), wired);
            Face(panel, "Button / Join", "JOIN", DesignW * 0.5f + 20, 520, 400, 104,
                 new Color(1f, 1f, 1f, 0.12f), new Color(0.93f, 0.93f, 0.93f),
                 new UnityAction(view.JoinFound), wired);

            // And a place to type one, for everything the beacon cannot reach: a host
            // across the internet behind a forwarded port, or either end of a VPN that
            // makes two machines look like one network without carrying its broadcasts.
            Typed(panel, view, wired);

            // Again, on the result screen, becomes a request rather than a restart: both
            // devices have to deal the new arena on the same tick. Wired here, as a
            // persistent call, so it is visible in the inspector like everything else.
            MatchResultView result = Object.FindFirstObjectByType<MatchResultView>();
            if (result != null)
            {
                Undo.RecordObject(result, "Set up two-phone play");
                // Cleared first, so running this tool twice does not ask for two rematches.
                for (int i = result.RematchAsked.GetPersistentEventCount() - 1; i >= 0; i--)
                    UnityEventTools.RemovePersistentListener(result.RematchAsked, i);
                UnityEventTools.AddPersistentListener(result.RematchAsked,
                                                      new UnityAction(clock.AskRematch));
                EditorUtility.SetDirty(result);
                wired.Add("again");
            }

            EnsureEventSystem();
            EditorUtility.SetDirty(canvas);

            string missing = "";
            if (result == null)
                missing += "\nThere is no result screen in this scene, so a finished match "
                         + "leaves both players standing in the arena. Run Tools > Gulag "
                         + "Runners > Build the result screen.";
            if (Object.FindFirstObjectByType<MatchRecorder>() == null)
                missing += "\nThere is no MatchRecorder, so nothing writes the rating, the "
                         + "gold or the match count: every match played here is forgotten. "
                         + "The same tool puts one in.";

            Debug.Log(
                $"Two-phone play set up on \"{canvas.name}\": {string.Join(", ", wired)}.\n"
                + $"Both phones on the SAME Wi-Fi. One taps HOST, the other taps JOIN -- "
                + "the host shouts on the network once a second so no address has to be "
                + "typed. If JOIN finds nothing, the hotspot is probably blocking "
                + "broadcast; the host's address is on its own screen and "
                + "NetConnectView.JoinAt takes it directly.\n"
                + "Android needs Internet Access set to Require in Player Settings, or "
                + "the socket cannot open and nothing says why.\n"
                + "The host is player 1 and the guest is player 2. While connected the "
                + "bots are switched off, the guest's camera, health bar and saved "
                + "progress move over to fighter two, and the match runs on the network's "
                + "clock: a tick happens when both thumbs have arrived."
                + missing,
                root.gameObject);
        }

        /// <summary>
        /// A box to type a host's address into, and a button that joins it.
        ///
        /// The beacon only reaches as far as a broadcast does, which is one subnet. Any
        /// host further away -- behind a forwarded port, or on the far side of a VPN
        /// that joins two machines without carrying their broadcasts -- has to be named,
        /// and NetConnectView.JoinAt has always taken an address. Nothing called it.
        /// </summary>
        static void Typed(Transform parent, NetConnectView view, List<string> wired)
        {
            RectTransform rt = Make("Address / Typed", parent,
                                    DesignW * 0.5f - 420, 660, 620, 88);
            Image box = Undo.AddComponent<Image>(rt.gameObject);
            box.color = new Color(1f, 1f, 1f, 0.08f);
            box.raycastTarget = true;

            RectTransform textRt = Make("Text", rt, 20, 0, 580, 88);
            Text text = Undo.AddComponent<Text>(textRt.gameObject);
            text.fontSize = 34;
            text.color = new Color(0.93f, 0.93f, 0.93f);
            text.alignment = TextAnchor.MiddleLeft;
            text.supportRichText = false;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            RectTransform hintRt = Make("Hint", rt, 20, 0, 580, 88);
            Text hint = Undo.AddComponent<Text>(hintRt.gameObject);
            hint.text = "host's address, e.g. 192.168.1.14";
            hint.fontSize = 32;
            hint.color = new Color(1f, 1f, 1f, 0.35f);
            hint.alignment = TextAnchor.MiddleLeft;
            hint.raycastTarget = false;
            hint.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            InputField field = Undo.AddComponent<InputField>(rt.gameObject);
            field.targetGraphic = box;
            field.textComponent = text;
            field.placeholder = hint;
            field.lineType = InputField.LineType.SingleLine;
            field.characterLimit = 45;              // an IPv6 address, with room to spare

            // A button as well as the end-edit, because a soft keyboard's "done" is not
            // somewhere everybody looks and a visible way to commit costs one rectangle.
            Face(parent, "Button / Join at", "JOIN AT", DesignW * 0.5f + 220, 660, 200, 88,
                 new Color(1f, 1f, 1f, 0.12f), new Color(0.93f, 0.93f, 0.93f),
                 new UnityAction(view.JoinTyped), wired);

            // Every keystroke goes to the view, so the button above has something to
            // join: a Button's OnClick carries no argument, and the only other way is
            // for the view to hold the InputField itself.
            UnityEventTools.AddPersistentListener(field.onValueChanged,
                                                  new UnityAction<string>(view.SetAddress));
            UnityEventTools.AddPersistentListener(field.onEndEdit,
                                                  new UnityAction<string>(view.JoinAt));
            wired.Add("typed address");
        }

        static Canvas Canvas()
        {
            HudView hud = Object.FindFirstObjectByType<HudView>();
            Canvas found = hud != null ? hud.GetComponentInParent<Canvas>() : null;
            if (found != null) return found.rootCanvas;
            foreach (Canvas c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include,
                                                                  FindObjectsSortMode.None))
                if (c.renderMode != RenderMode.WorldSpace) return c.rootCanvas;
            return null;
        }

        static MenuLabel Line(Transform parent, string name, string text, float y,
                              float h, int size, Color colour)
        {
            RectTransform rt = Make(name, parent, 0, y, DesignW, h);
            Text label = Undo.AddComponent<Text>(rt.gameObject);
            label.text = text;
            label.fontSize = size;
            label.color = colour;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.raycastTarget = false;
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            MenuLabel wrap = Undo.AddComponent<MenuLabel>(rt.gameObject);
            wrap.target = label;
            wrap.placeholder = text;
            return wrap;
        }

        static void Face(Transform parent, string name, string word, float x, float y,
                         float w, float h, Color fill, Color ink, UnityAction action,
                         List<string> wired)
        {
            RectTransform rt = Make(name, parent, x, y, w, h);
            Image face = Undo.AddComponent<Image>(rt.gameObject);
            face.color = fill;
            face.raycastTarget = true;

            RectTransform wordRt = Make(word, rt, 0, 0, w, h);
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
            Undo.RegisterCreatedObjectUndo(go, "Set up two-phone play");
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        static RectTransform Make(string name, Transform parent, float fx, float fy,
                                  float w, float h)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Set up two-phone play");
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(fx, -fy);
            return rt;
        }

        static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null) return;
            GameObject go = new GameObject("EventSystem", typeof(EventSystem),
                                           typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
            Undo.RegisterCreatedObjectUndo(go, "Set up two-phone play");
        }
    }
}
