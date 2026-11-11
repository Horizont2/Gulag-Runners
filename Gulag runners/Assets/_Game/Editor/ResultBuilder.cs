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
    /// IT TAKES ART NOW, a row at a time, like the other three builders. It used to be a
    /// one-click menu item that laid out flat colours, with "skinning it is swapping the
    /// Image sprites" written where the work was: eight Images to find in a hierarchy that
    /// only exists after the tool has run, and nothing anywhere saying which sprite went
    /// where. Every row below is optional and every one of them falls back to the colour it
    /// had before, so the screen still builds from an empty list -- but a filled list is
    /// now a skinned result screen rather than a note in a file.
    /// </summary>
    public sealed class ResultBuilder : EditorWindow
    {
        const float DesignW = 1920f;
        const float DesignH = 864f;

        [MenuItem("Tools/Gulag Runners/Build the result screen")]
        public static void Open()
        {
            ResultBuilder w = GetWindow<ResultBuilder>(true, "Build the result screen");
            w.minSize = new Vector2(460, 460);
        }

        // ---------------------------------------------------------------- what it needs

        struct Asset
        {
            public string Key;
            public float W, H;

            /// <summary>Nine-slice border in 1x pixels; zero means the sprite is stretched
            /// whole.</summary>
            public Vector4 Border;

            /// <summary>
            /// What the piece is for, in English, under its row in the window. Every one of
            /// these is optional, so the note also has to say what happens when it is left
            /// empty -- otherwise an empty row looks like a mistake rather than a choice.
            /// </summary>
            public string What;

            public Asset(string key, float w, float h, string what)
            { Key = key; W = w; H = h; Border = Vector4.zero; What = what; }

            public Asset(string key, float w, float h, float b, string what)
            { Key = key; W = w; H = h; Border = new Vector4(b, b, b, b); What = what; }
        }

        static readonly Asset[] Needed =
        {
            new Asset("result/dim", 1920, 864,
                      "The wash over the whole screen that puts the arena behind the "
                      + "result. It is also what stops a thumb still resting on the attack "
                      + "stick from driving a fighter who is already dead, so it is built "
                      + "whether or not you fill this in. Leave it empty for the flat dark "
                      + "it has now; fill it for a vignette or a texture, drawn full-bleed "
                      + "and stretched."),
            new Asset("result/panel", 1000, 460, 48,
                      "The plate the result sits on: a slab behind the headline, the score "
                      + "and the two meta lines, in the middle of the screen. Nine-sliced "
                      + "with a wide border, because it is stretched to the design's "
                      + "1000x460 and would be stretched again on another aspect. Leave it "
                      + "empty and the lines sit straight on the dimmer, which is what the "
                      + "screen does today."),
            new Asset("result/banner", 760, 150,
                      "The band behind the one word that says what happened -- VICTORY or "
                      + "DEFEAT. The word itself stays text, because it is translated and "
                      + "because the same screen has to say both; this is the thing it is "
                      + "written on. Leave it empty for a word with nothing behind it."),
            new Asset("result/divider", 520, 6,
                      "The rule between the score and the two meta lines: a thin horizontal "
                      + "flourish, six pixels tall at 1x. It is the only thing separating "
                      + "\"who won\" from \"what it changed\". Stretched across its width, "
                      + "so keep any detail in the middle rather than at the ends."),
            // 116 tall, not 104: docs/23 wants 9 mm under a thumb and 104 units is 8.1.
            // They grow UPWARDS at the call site, so the row keeps the bottom edge the
            // sheet gave it and the overhang past the plate does not change.
            new Asset("result/button/primary", 400, 116, 24,
                      "The face of the REMATCH button -- the one the player is meant to "
                      + "hit. Nine-sliced, drawn in its resting state: the builder leaves "
                      + "the Button component to tint it for pressed and disabled, so a "
                      + "highlight baked into the sprite will be tinted twice. Empty leaves "
                      + "the orange slab it has now."),
            new Asset("result/button/secondary", 400, 116, 24,
                      "The face of the MENU button, which should read as the quieter of the "
                      + "two: same size, same slicing, less contrast. If both buttons wear "
                      + "the same sprite the screen stops saying which one to press."),
            new Asset("result/icon/gold", 44, 44,
                      "The coin that sits to the left of the purse line (\"+40\"). The "
                      + "number comes from the recorder, so this is only the mark that says "
                      + "the number is money. Empty means the number stands alone."),
            new Asset("result/icon/league", 44, 44,
                      "The badge to the left of the league line (\"SILVER III\"). The "
                      + "menu's own rank badges are the obvious thing to drag in here -- the "
                      + "same six exist in the menu tool -- and using the same art means a "
                      + "promotion looks like the same badge in both places."),
        };

        static List<string> Keys()
        {
            List<string> keys = new List<string>(Needed.Length);
            foreach (Asset a in Needed) keys.Add(a.Key);
            return keys;
        }

        static List<Vector2> Sizes()
        {
            List<Vector2> sizes = new List<Vector2>(Needed.Length);
            foreach (Asset a in Needed) sizes.Add(new Vector2(a.W, a.H));
            return sizes;
        }

        static List<string> Notes()
        {
            List<string> notes = new List<string>(Needed.Length);
            foreach (Asset a in Needed) notes.Add(a.What);
            return notes;
        }

        // ---------------------------------------------------------------- window

        UiSpriteSet _set;
        Vector2 _scroll;
        bool _onlyEmpty;
        bool _folderOpen;
        Object _folder;

        void OnGUI()
        {
            List<string> keys = Keys();
            _set = UiSpriteRows.Field(_set, "Result Sprites", keys);
            if (_set == null) return;

            UiSpriteRows.Rows(_set, keys, Sizes(), Notes(), ref _scroll,
                              ref _onlyEmpty, 300f);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "Every row is optional. An empty one keeps the flat colour the screen is "
                + "laid out in, so this builds and runs with nothing filled in -- the "
                + "layout, the wiring and the buttons do not depend on the art.",
                EditorStyles.wordWrappedMiniLabel);

            UiSpriteRows.FolderFill(_set, keys, ref _folderOpen, ref _folder);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(
                       Object.FindFirstObjectByType<MatchState>() == null))
                if (GUILayout.Button("Build the result screen", GUILayout.Height(34)))
                    Build(_set);

            if (Object.FindFirstObjectByType<MatchState>() == null)
                EditorGUILayout.LabelField(
                    "No MatchState in this scene, so there is no match for a result screen "
                    + "to be the end of. Open an arena scene.",
                    EditorStyles.wordWrappedMiniLabel);
        }

        // ---------------------------------------------------------------- the build

        public static void Build(UiSpriteSet set)
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

            // Import settings written per sprite, before anything is placed, so a nine-slice
            // border is in the ASSET rather than in a note somebody has to remember. The
            // border is quoted at 1x and the export may be any multiple of that, so the
            // ratio is measured from each sprite's own width.
            if (set != null)
                foreach (Asset a in Needed)
                    Import(set.Get(a.Key), a);

            // Thrown away and rebuilt, so running it twice does not leave two of them --
            // and so an edit to this file is one menu click from being on screen.
            Transform old = canvas.transform.Find("Result");
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            RectTransform root = Panel("Result", canvas.transform);

            // Full-bleed and a raycast target: it is also what stops a thumb still resting
            // on the attack stick from driving a fighter who is already dead.
            Image dim = Undo.AddComponent<Image>(
                Make("Dimmer", root, 0, 0, DesignW, DesignH, 0f).gameObject);
            dim.color = UiPalette.Scrim;
            dim.raycastTarget = true;
            Skin(dim, set, "result/dim");

            // The plate, then the band, then the lines on top of them: order in the
            // hierarchy IS draw order on a canvas, and a panel added after the text covers
            // the text.
            Slab(root, "Plate", set, "result/panel", Centre("result/panel"), 170);
            Slab(root, "Banner", set, "result/banner", Centre("result/banner"), 195);

            MenuLabel headline = Line(root, "Headline", "VICTORY", 230, 110, 96,
                                      UiPalette.Action);
            MenuLabel score = Line(root, "Score", "2 — 1", 360, 70, 56,
                                   UiPalette.Ink);

            Slab(root, "Divider", set, "result/divider", Centre("result/divider"), 430);

            // What the match CHANGED, between the score and the buttons. A result screen
            // that only says who won is a scoreboard; this is the line that makes a
            // second match worth playing, and it is the one the whole meta was written
            // for and never connected to.
            MenuLabel league = Line(root, "League", "SILVER III", 450, 44, 32,
                                    UiPalette.InkSoft);
            MenuLabel gold = Line(root, "Gold", "+40", 492, 44, 32,
                                  UiPalette.Amber);

            // The two little marks beside those lines, to the left of centre so they read
            // as belonging to the number rather than floating.
            Slab(root, "League mark", set, "result/icon/league",
                 DesignW * 0.5f - 150f, 444);
            Slab(root, "Gold mark", set, "result/icon/gold",
                 DesignW * 0.5f - 90f, 486);

            MatchResultView view = Undo.AddComponent<MatchResultView>(root.gameObject);
            Undo.RecordObject(view, "Build the result screen");
            view.match = match;
            view.panel = root.gameObject;

            // The thing that tells the saved progress a match happened. Everything it
            // needs was already written -- Glicko-2, the purse, the daily roll, the
            // PlayerPrefs save -- and nothing in the project had ever called it, so a
            // player could win twenty matches and the menu would still read nought.
            MatchRecorder recorder = Object.FindFirstObjectByType<MatchRecorder>();
            if (recorder == null)
            {
                GameObject keeper = new GameObject("Match record");
                Undo.RegisterCreatedObjectUndo(keeper, "Build the result screen");
                recorder = Undo.AddComponent<MatchRecorder>(keeper);
            }
            Undo.RecordObject(recorder, "Build the result screen");
            recorder.match = match;
            recorder.localPlayer = view.localPlayer;
            PrefabUtility.RecordPrefabInstancePropertyModifications(recorder);

            // NetMatch holds the result screen for the same reason it holds the HUD: both
            // carry a "which fighter is me" that is wrong on the guest's phone until it
            // corrects them. This panel is thrown away and rebuilt on every run, so the
            // reference has to be handed over on every run too -- otherwise the field
            // silently empties and NetMatch falls back to finding one in an order Unity
            // does not define.
            GulagRunners.Net.NetMatch net =
                Object.FindFirstObjectByType<GulagRunners.Net.NetMatch>();
            if (net != null)
            {
                Undo.RecordObject(net, "Build the result screen");
                net.result = view;
                EditorUtility.SetDirty(net);
                PrefabUtility.RecordPrefabInstancePropertyModifications(net);
            }

            List<string> wired = new List<string>();
            Wire(view.HeadlineChanged, headline.Set, "headline", wired);
            Wire(view.ScoreChanged, score.Set, "score", wired);
            Wire(recorder.GoldChanged, gold.Set, "gold", wired);
            Wire(recorder.LeagueChanged, league.Set, "league", wired);

            // A rematch is a new match and may be recorded in its own right.
            UnityEventTools.AddPersistentListener(view.Hidden, new UnityAction(recorder.Rearm));
            wired.Add("rearm");

            // 548, where the sheet said 560: the faces are 12 units taller than the
            // sheet drew them (9 mm for a thumb, docs/23) and they grow upwards, so the
            // row keeps the bottom edge it had and the gold line above still clears it.
            Face(root, "Button / Rematch", "REMATCH", DesignW * 0.5f - 420, 548,
                 UiPalette.Amber, UiPalette.InkOnAmber,
                 new UnityAction(view.Rematch), wired, set, "result/button/primary");
            Face(root, "Button / Menu", "MENU", DesignW * 0.5f + 20, 548,
                 UiPalette.Fade(UiPalette.Ink, 0.14f), UiPalette.Ink,
                 new UnityAction(view.ToMenu), wired, set, "result/button/secondary");

            EnsureEventSystem();

            // Switched off, because that is the contract the rest of the project's UI
            // keeps: every panel is already in the scene and nothing is made at play time.
            root.gameObject.SetActive(false);

            EditorUtility.SetDirty(canvas);

            int skinned = 0;
            if (set != null)
                foreach (Asset a in Needed)
                    if (set.Has(a.Key)) skinned++;

            Debug.Log(
                $"Result screen built on \"{canvas.name}\": {string.Join(", ", wired)} " +
                $"wired, {skinned} of {Needed.Length} sprite row(s) used.\n" +
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

        /// <summary>
        /// The import settings for one dragged sprite: a Sprite, uncompressed, unmipped,
        /// with the nine-slice border this table asks for.
        ///
        /// The border is quoted in 1x design pixels and an export can be any multiple of
        /// that, so the ratio comes from the sprite's own width against the design width.
        /// Measured per sprite rather than once for the folder, which is both simpler and
        /// exact when one piece was exported at a different size from the rest.
        /// </summary>
        static void Import(Sprite sprite, Asset asset)
        {
            if (sprite == null) return;

            string path = AssetDatabase.GetAssetPath(sprite);
            TextureImporter imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) return;

            float scale = asset.W > 0f && sprite.texture != null
                ? Mathf.Max(0.01f, sprite.texture.width / asset.W) : 1f;
            Vector4 border = asset.Border * scale;

            bool change = imp.textureType != TextureImporterType.Sprite
                          || imp.mipmapEnabled
                          || imp.npotScale != TextureImporterNPOTScale.None
                          || imp.textureCompression != TextureImporterCompression.Uncompressed
                          || imp.spriteBorder != border;
            if (!change) return;

            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.npotScale = TextureImporterNPOTScale.None;
            imp.mipmapEnabled = false;
            imp.alphaIsTransparency = true;
            imp.filterMode = FilterMode.Bilinear;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            if (asset.Border != Vector4.zero) imp.spriteBorder = border;
            imp.SaveAndReimport();
        }

        /// <summary>
        /// Puts a dragged sprite on an Image, or leaves the colour it was built with.
        ///
        /// White when a sprite is used: an Image tints its sprite by its colour, so the
        /// flat fill that stands in for the art would otherwise be painted over the art.
        /// </summary>
        static void Skin(Image image, UiSpriteSet set, string key)
        {
            Sprite sprite = set != null ? set.Get(key) : null;
            if (image == null || sprite == null) return;

            image.sprite = sprite;
            image.color = Color.white;
            image.type = sprite.border == Vector4.zero ? Image.Type.Simple : Image.Type.Sliced;
        }

        /// <summary>
        /// The size this row is laid out at, from the table and nowhere else.
        ///
        /// This screen used to pass its own literals -- Slab(..., 1000, 460) beside a row
        /// that said 1000x460 -- and two numbers that have to agree are one number that
        /// will not. The table drives the nine-slice border and the pixels-per-unit of
        /// every import, so a rect that disagreed with it would have drawn a 48-unit
        /// corner at the wrong scale and nothing would have said so.
        /// </summary>
        static Vector2 Design(string key)
        {
            foreach (Asset a in Needed)
                if (a.Key == key) return new Vector2(a.W, a.H);
            return new Vector2(100f, 100f);
        }

        /// <summary>Where a row starts if it is centred on the screen.</summary>
        static float Centre(string key) => (DesignW - Design(key).x) * 0.5f;

        /// <summary>
        /// One decorative rect, and NOTHING at all when its row is empty.
        ///
        /// Not an empty Image: an Image with no sprite draws a white box, so a plate
        /// nobody has drawn would be a white slab across the result rather than an absence.
        /// </summary>
        static void Slab(Transform parent, string name, UiSpriteSet set, string key,
                         float x, float y)
        {
            Vector2 size = Design(key);
            float w = size.x, h = size.y;

            Sprite sprite = set != null ? set.Get(key) : null;
            if (sprite == null) return;

            RectTransform rt = Make(name, parent, x, y, w, h, 0f);
            Image image = Undo.AddComponent<Image>(rt.gameObject);
            image.raycastTarget = false;
            Skin(image, set, key);
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

        /// <summary>A slab with a word on it, wired to one method, skinned if there is art.</summary>
        static void Face(Transform parent, string name, string word, float x, float y,
                         Color fill, Color ink, UnityAction action,
                         List<string> wired, UiSpriteSet set, string key)
        {
            Vector2 size = Design(key);
            float w = size.x, h = size.y;

            RectTransform rt = Make(name, parent, x, y, w, h, 0f);
            Image face = Undo.AddComponent<Image>(rt.gameObject);
            face.color = fill;
            face.raycastTarget = true;
            Skin(face, set, key);

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
