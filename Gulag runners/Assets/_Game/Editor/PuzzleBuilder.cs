using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Events;
// Names which UnityEventTools is meant. Unity has only one; the reference assemblies
// compile_check builds against carry a second, and an alias is the honest way to say so.
using UnityEventTools = UnityEditor.Events.UnityEventTools;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using GulagRunners.Game;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Builds the lock panel of docs/04 into an arena scene.
    ///
    /// THE LAYOUT: a plate that follows the fighter, three pips along the top saying how hard
    /// the lock is, and a row of up to six things to touch. That is the whole screen, and it
    /// has NO WORDS ON IT AT ALL -- docs/04 rule 2 is zero text and zero language, so that a
    /// global release ships the same lock everywhere, and a panel with a label on it would be
    /// the first thing to break that.
    ///
    /// It is not a mode and it does not take the screen (rule 5): the arena stays behind it,
    /// the other player is still coming, and a fighter can be hit in the middle of a puzzle.
    /// The price of thinking is your attention, which is the entire reason the puzzles are
    /// interesting in a PvP game rather than a pause in one.
    ///
    /// Everything else is what the other five builders cost to learn and is not learnt again
    /// here: one sprite set for the whole game, each row sized from its own sprite with the
    /// import written before anything is placed, every rect measured in millimetres on a 6.5"
    /// phone (docs/23: 9 mm under a thumb), the colours from UiPalette rather than literals,
    /// and every piece DRAWN when its row is empty so the screen stands up before any art
    /// exists.
    /// </summary>
    public sealed class PuzzleBuilder : EditorWindow
    {
        const float DesignW = 1920f;
        const float DesignH = 864f;
        const string RootName = "Puzzle UI";

        // The plate, and the row of things to touch inside it.
        const float PlateW = 820f;
        const float PlateH = 260f;
        const float OptionStep = 136f;      // 116 of target, 20 of air
        const int Options = 6;              // the most any lock in the bank puts up (rings)
        const int Pips = 3;

        Object _folder;
        bool _folderOpen;
        UiSpriteSet _set;
        Vector2 _scroll;
        bool _onlyEmpty;

        readonly Dictionary<string, string> _found = new Dictionary<string, string>();
        readonly Dictionary<string, Vector2> _size = new Dictionary<string, Vector2>();

        [MenuItem("Tools/Gulag Runners/Build the lock panel")]
        public static void Open()
        {
            PuzzleBuilder w = GetWindow<PuzzleBuilder>(true, "Build the lock panel");
            w.minSize = new Vector2(460, 420);
        }

        struct Asset
        {
            public string Key;
            public float W, H;

            /// <summary>Left, bottom, right, top, at 1x. Anything non-zero is drawn Sliced.</summary>
            public Vector4 Border;

            public bool Sliced => Border != Vector4.zero;

            /// <summary>What the piece is for, in English, under its row in the window.</summary>
            public string What;

            public Asset(string key, float w, float h, string what)
            { Key = key; W = w; H = h; Border = Vector4.zero; What = what; }

            public Asset(string key, float w, float h, float b, string what)
            { Key = key; W = w; H = h; Border = new Vector4(b, b, b, b); What = what; }
        }

        static readonly Asset[] Needed =
        {
            new Asset("puzzle/plate", PlateW, PlateH, 28,
                      "The face of the lock, floating above the fighter who is working on "
                      + "it. Dark and quiet: everything readable sits ON it, and the arena "
                      + "has to stay visible through the rest of the screen because the "
                      + "other player is still coming."),
            new Asset("puzzle/pip", 28, 28,
                      "One difficulty pip, up to three. They are lit BEFORE the first touch, "
                      + "which is docs/04 rule 7 -- the player decides whether three seconds "
                      + "is worth it, and both players see the same spread."),
            new Asset("puzzle/option", 116, 116, 20,
                      "One thing to touch: a rune, a ring's direction, a gear. 116 units is "
                      + "9 mm, the floor for a thumb, and docs/04 rule 6 forbids anything "
                      + "finer than a tap -- no dragging, no multitouch, no timing."),
            new Asset("puzzle/mark", 44, 44,
                      "The tick on an option already used. Needed because two of the three "
                      + "locks take their touches in ANY order, so the player has to be able "
                      + "to see which half of the answer is already in."),
            new Asset("puzzle/glyph/rune", 64, 64,
                      "The rune of docs/04 #1. One sprite: the odd one out is the same rune "
                      + "TURNED, which the note offers as one of its three differences and "
                      + "which costs one drawing instead of six."),
            new Asset("puzzle/glyph/turn-left", 64, 64,
                      "An arrow, for the rings of #5. Each ring gets two options side by "
                      + "side, so the pair says which ring and the arrow says which way."),
            new Asset("puzzle/glyph/turn-right", 64, 64,
                      "The other arrow. A separate row rather than the same one flipped: a "
                      + "mirrored sprite with a shadow on it looks lit from the wrong side."),
            new Asset("puzzle/glyph/gear", 64, 64,
                      "A gear, for #10. The ones still in the chain are drawn bright and the "
                      + "ones already off are dimmed, which is the thing being counted."),
        };

        // ---------------------------------------------------------------- window

        void OnGUI()
        {
            List<string> keys = Keys();
            _set = UiSpriteRows.Field(_set, "Puzzle Sprites", keys);
            EditorGUILayout.LabelField(
                "Every piece is DRAWN if its row is empty, so the lock builds with nothing " +
                "dragged in at all. A row that has something wins.",
                EditorStyles.wordWrappedMiniLabel);

            if (_set != null)
            {
                UiSpriteRows.Rows(_set, keys, Sizes(), Notes(), ref _scroll,
                                  ref _onlyEmpty, 260f);
                UiSpriteRows.FolderFill(_set, keys, ref _folderOpen, ref _folder);
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("Build the lock panel", GUILayout.Height(32))) Build();

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Run this in an ARENA scene, on the same canvas as the HUD. The panel is " +
                "hidden until the local fighter starts on a chest that has a lock, and it " +
                "never takes the screen: docs/04 says the opponent does not disappear.",
                MessageType.None);
        }

        // ---------------------------------------------------------------- the sprites

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

        /// <summary>
        /// What multiple of its design size this one file came out at, measured from the
        /// WIDTH -- the dimension the layout anchors on.
        /// </summary>
        static float Ratio(Asset asset, Sprite sprite)
        {
            if (asset.W <= 0f || sprite == null) return 1f;
            return Mathf.Max(0.01f, sprite.rect.width / asset.W);
        }

        static float Ratio(Asset asset, Texture2D texture)
        {
            if (asset.W <= 0f || texture == null) return 1f;
            return Mathf.Max(0.01f, texture.width / asset.W);
        }

        void Gather(Dictionary<string, Sprite> sprites)
        {
            _found.Clear();
            _size.Clear();
            if (_set == null) return;

            foreach (Asset a in Needed)
            {
                Sprite s = _set.Get(a.Key);
                if (s == null) continue;

                Import(a);
                s = _set.Get(a.Key);
                if (s == null) continue;

                sprites[a.Key] = s;
                _found[a.Key] = AssetDatabase.GetAssetPath(s);

                float own = Ratio(a, s);
                Vector2 native = new Vector2(s.rect.width, s.rect.height) / own;
                bool disagrees = a.H > 0f && Mathf.Abs(native.y - a.H) > a.H * 0.25f;
                _size[a.Key] = disagrees ? new Vector2(a.W, a.H) : native;
            }
        }

        /// <summary>
        /// The import settings a UI sprite needs, written before anything is measured off it.
        /// </summary>
        void Import(Asset asset)
        {
            string path = _set != null ? AssetDatabase.GetAssetPath(_set.Get(asset.Key)) : null;
            if (string.IsNullOrEmpty(path)) return;
            TextureImporter imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) return;

            float at = Ratio(asset, AssetDatabase.LoadAssetAtPath<Texture2D>(path));
            bool change = false;

            if (imp.textureType != TextureImporterType.Sprite)
            { imp.textureType = TextureImporterType.Sprite; change = true; }
            if (!Mathf.Approximately(imp.spritePixelsPerUnit, 100f * at))
            { imp.spritePixelsPerUnit = 100f * at; change = true; }
            if (asset.Border != Vector4.zero && imp.spriteBorder != asset.Border * at)
            { imp.spriteBorder = asset.Border * at; change = true; }

            if (change) imp.SaveAndReimport();
        }

        Vector2 Size(string key, float w, float h) =>
            _size.TryGetValue(key, out Vector2 found) ? found : new Vector2(w, h);

        Sprite Sprite(Dictionary<string, Sprite> sprites, string key) =>
            sprites.TryGetValue(key, out Sprite s) ? s : null;

        // ---------------------------------------------------------------- the screen

        void Build()
        {
            Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
            Gather(sprites);

            Canvas canvas = Canvas();
            if (canvas == null)
            {
                Debug.LogWarning("No screen-space Canvas in this scene. Open an arena scene, " +
                                 "or run Tools > Gulag Runners > Build the HUD first; this " +
                                 "hangs off the same canvas.");
                return;
            }

            Transform old = canvas.transform.Find(RootName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            RectTransform root = Panel(RootName, canvas.transform);
            PuzzleView view = Undo.AddComponent<PuzzleView>(root.gameObject);
            Undo.RecordObject(view, "Build the lock panel");
            view.matchState = Object.FindFirstObjectByType<MatchState>();
            view.view = Camera.main;
            view.canvasRect = canvas.transform as RectTransform;

            // ---- the plate. Centred on nothing in particular: PuzzleView puts it over the
            // fighter every frame and clamps it to the sheet.
            Vector2 plate = Size("puzzle/plate", PlateW, PlateH);
            RectTransform plateRt = Centre("Plate", root, plate.x, plate.y);
            Image face = Picture(plateRt.gameObject, sprites, "puzzle/plate", true);
            face.color = UiPalette.Plate;
            face.raycastTarget = true;          // and it eats taps meant for the arena behind
            view.plate = plateRt;
            view.plateFace = face;

            // ---- the pips, along the top left of the plate
            Vector2 pip = Size("puzzle/pip", 28, 28);
            Image[] pips = new Image[Pips];
            for (int i = 0; i < Pips; i++)
            {
                RectTransform rt = Make($"Pip {i + 1}", plateRt, 32 + i * (pip.x + 12), 26,
                                        pip.x, pip.y);
                pips[i] = Picture(rt.gameObject, sprites, "puzzle/pip");
                pips[i].color = UiPalette.Amber;
            }
            view.pips = pips;

            // ---- the things to touch. Six built and the view shows as many as the lock
            // has: a panel that instantiates buttons is a panel that allocates while a
            // thumb is moving, and this one appears in the middle of a fight.
            Vector2 option = Size("puzzle/option", 116, 116);
            Vector2 glyph = Size("puzzle/glyph/rune", 64, 64);
            Vector2 mark = Size("puzzle/mark", 44, 44);

            float row = Options * option.x + (Options - 1) * (OptionStep - option.x);
            float left = (plate.x - row) * 0.5f;
            float top = plate.y - 36f - option.y;

            RectTransform[] rects = new RectTransform[Options];
            Image[] faces = new Image[Options];
            Image[] glyphs = new Image[Options];
            Image[] marks = new Image[Options];

            for (int i = 0; i < Options; i++)
            {
                RectTransform rt = Make($"Option {i + 1}", plateRt, left + i * OptionStep, top,
                                        option.x, option.y);
                Image key = Picture(rt.gameObject, sprites, "puzzle/option", true);
                key.color = UiPalette.Fade(UiPalette.Ink, 0.16f);
                key.raycastTarget = true;

                RectTransform g = Make("Glyph", rt, (option.x - glyph.x) * 0.5f,
                                       (option.y - glyph.y) * 0.5f, glyph.x, glyph.y);
                Image gi = Picture(g.gameObject, sprites, "puzzle/glyph/rune");
                gi.color = UiPalette.Ink;

                RectTransform m = Make("Mark", rt, option.x - mark.x - 8, 8, mark.x, mark.y);
                Image mi = Picture(m.gameObject, sprites, "puzzle/mark");
                mi.color = UiPalette.Amber;
                m.gameObject.SetActive(false);

                Button button = Undo.AddComponent<Button>(rt.gameObject);
                button.targetGraphic = key;
                UnityEventTools.AddIntPersistentListener(
                    button.onClick, new UnityAction<int>(view.Touch), i + 1);

                rects[i] = rt;
                faces[i] = key;
                glyphs[i] = gi;
                marks[i] = mi;
            }

            view.optionRects = rects;
            view.optionFaces = faces;
            view.optionGlyphs = glyphs;
            view.optionMarks = marks;

            view.rune = Sprite(sprites, "puzzle/glyph/rune");
            view.turnLeft = Sprite(sprites, "puzzle/glyph/turn-left");
            view.turnRight = Sprite(sprites, "puzzle/glyph/turn-right");
            view.gear = Sprite(sprites, "puzzle/glyph/gear");

            // Hidden until there is a lock to show. The ROOT stays on, because the view on
            // it is what decides when the plate appears -- a panel switched off at the root
            // is a panel that never notices the fighter walked up to a safe.
            plateRt.gameObject.SetActive(false);

            int pads = Pads();
            EnsureEventSystem();

            EditorUtility.SetDirty(view);
            Selection.activeGameObject = root.gameObject;

            Debug.Log($"Lock panel: built from {_found.Count} of {Needed.Length} sprites, " +
                      $"{Options} touch targets" +
                      (pads > 0 ? $", and a PuzzlePad on {pads} fighter(s)."
                                : ". No PlayerInputRouter in this scene, so nothing can " +
                                  "touch it yet -- build the fighters first."),
                      root.gameObject);
        }

        /// <summary>
        /// Every fighter needs somewhere for a tap on the lock to go. The pad sits on the
        /// same object as the router, which finds it the way it finds the joystick.
        /// </summary>
        static int Pads()
        {
            int n = 0;
            foreach (PlayerInputRouter router in
                     Object.FindObjectsByType<PlayerInputRouter>(FindObjectsInactive.Include,
                                                                 FindObjectsSortMode.None))
            {
                if (router == null) continue;
                if (router.GetComponent<PuzzlePad>() == null)
                    Undo.AddComponent<PuzzlePad>(router.gameObject);
                n++;
            }
            return n;
        }

        // ---------------------------------------------------------------- plumbing

        static Canvas Canvas()
        {
            foreach (Canvas c in Object.FindObjectsByType<Canvas>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (c.renderMode == RenderMode.ScreenSpaceOverlay && c.transform.parent == null)
                    return c;
            return null;
        }

        static RectTransform Panel(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Build the lock panel");
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        /// <summary>
        /// A rect pinned to the middle of its parent, pivot in the middle, because this one
        /// is MOVED every frame rather than laid out once.
        /// </summary>
        static RectTransform Centre(string name, Transform parent, float w, float h)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Build the lock panel");
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        /// <summary>
        /// A rect at x, y from the TOP LEFT of its parent, the way the other five builders
        /// place everything.
        /// </summary>
        static RectTransform Make(string name, Transform parent, float fx, float fy,
                                  float w, float h)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Build the lock panel");
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(fx, -fy);
            return rt;
        }

        static Image Picture(GameObject go, Dictionary<string, Sprite> sprites, string key,
                             bool sliced = false)
        {
            Image image = go.GetComponent<Image>();
            if (image == null) image = Undo.AddComponent<Image>(go);
            if (key != null && sprites.TryGetValue(key, out Sprite s)) image.sprite = s;
            image.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
            image.preserveAspect = false;
            image.raycastTarget = false;
            return image;
        }

        static void EnsureEventSystem()
        {
            EventSystem found = Object.FindFirstObjectByType<EventSystem>();
            if (found != null) return;
            GameObject go = new GameObject("EventSystem", typeof(EventSystem),
                                           typeof(InputSystemUIInputModule));
            Undo.RegisterCreatedObjectUndo(go, "Build the lock panel");
        }
    }
}
