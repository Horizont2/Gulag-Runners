using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using GulagRunners.Game;
using GulagRunners.Sim;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Builds the combat HUD out of the sprites exported from the Figma file.
    ///
    /// Drop the exported folder in, press Build, and the whole thing stands up in the
    /// scene at the coordinates it was designed at: Canvas, scaler, every square, ring and
    /// bar, the nine-slice borders, the import settings, and a HudView with all forty
    /// references already wired. docs/27 is the matching export sheet and holds the same
    /// numbers in prose.
    ///
    /// Nothing here runs at play time. This is a scene the tool WRITES, once; afterwards
    /// every object is an ordinary GameObject that a designer can move, rename or delete,
    /// which is the whole point and the reason it is a builder rather than a component
    /// that assembles itself on Awake.
    ///
    /// Run it again and it replaces what it made before, so correcting a bad export is a
    /// re-import and one button rather than an afternoon.
    /// </summary>
    public sealed class HudBuilder : EditorWindow
    {
        const float DesignW = 1920f;
        const float DesignH = 864f;
        const string RootName = "HUD";

        DefaultAsset _folder;
        Vector2 _scroll;
        readonly Dictionary<string, string> _found = new Dictionary<string, string>();
        readonly List<string> _unmatched = new List<string>();
        bool _scanned;

        [MenuItem("Tools/Gulag Runners/Build the combat HUD")]
        public static void Open()
        {
            HudBuilder w = GetWindow<HudBuilder>(true, "Build the combat HUD");
            w.minSize = new Vector2(460, 520);
        }

        // ---------------------------------------------------------------- what it needs
        // Every asset the HUD is made of, by the path Figma exported it under. The design
        // size is carried here too, because it is what the nine-slice borders and the
        // import scale are worked out from: a 64 px square exported at 4x is a 256 px
        // texture whose border is 48, not 12.
        struct Asset
        {
            public string Key;        // hud/slot/frame
            public float W, H;        // design size, 1x
            public Vector4 Border;    // nine-slice, in 1x pixels
            public Asset(string key, float w, float h) { Key = key; W = w; H = h; Border = Vector4.zero; }
            public Asset(string key, float w, float h, float b) { Key = key; W = w; H = h; Border = new Vector4(b, b, b, b); }
        }

        static readonly Asset[] Needed =
        {
            new Asset("hud/slot/frame", 64, 64, 12),
            new Asset("hud/slot/frame-dashed", 64, 64, 12),
            new Asset("hud/slot/glow", 160, 160),
            new Asset("hud/slot/swap-arrow", 18, 18),
            new Asset("hud/bar/frame", 440, 32, 8),
            new Asset("hud/bar/fill", 424, 16, 4),
            new Asset("hud/pip", 20, 20),
            new Asset("hud/button/ring-154", 154, 154),
            new Asset("hud/button/ring-130", 130, 130),
            new Asset("hud/button/ring-101", 101, 101),
            new Asset("hud/button/glyph-attack", 56, 56),
            new Asset("hud/button/glyph-block", 48, 48),
            new Asset("hud/button/glyph-action", 44, 44),
            new Asset("hud/button/glyph-jump", 46, 46),
            new Asset("hud/joystick/ring", 200, 200),
            new Asset("hud/joystick/knob", 68, 68),
            new Asset("hud/sound/arc", 210, 62),
            new Asset("hud/sound/source-chest", 46, 46),
            new Asset("hud/icon/coif", 32, 32),
            new Asset("hud/icon/great-helm", 32, 32),
            new Asset("hud/icon/chainmail", 32, 32),
            new Asset("hud/icon/plate", 32, 32),
            new Asset("hud/icon/club", 32, 32),
            new Asset("hud/icon/sword", 32, 32),
            new Asset("hud/icon/belt", 32, 32),
            new Asset("hud/icon/bandage", 32, 32),
            new Asset("hud/mannequin", 150, 330),
            new Asset("hud/leaders", 115, 350),
            new Asset("hud/leaders-mirrored", 115, 350),
        };

        // ---------------------------------------------------------------- window

        void OnGUI()
        {
            EditorGUILayout.LabelField("Exported sprites", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "The folder Figma exported. Put it anywhere under Assets first — Unity " +
                "cannot import from outside the project.", EditorStyles.wordWrappedMiniLabel);

            EditorGUI.BeginChangeCheck();
            _folder = (DefaultAsset)EditorGUILayout.ObjectField(
                "Folder", _folder, typeof(DefaultAsset), false);
            if (EditorGUI.EndChangeCheck()) _scanned = false;

            using (new EditorGUI.DisabledScope(_folder == null))
                if (GUILayout.Button("Scan the folder", GUILayout.Height(24))) Scan();

            if (_scanned)
            {
                int have = _found.Count;
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(
                    have == Needed.Length
                        ? $"All {Needed.Length} sprites found."
                        : $"{have} of {Needed.Length} found. The missing ones are listed below; " +
                          "the HUD still builds without them and leaves those objects empty.",
                    have == Needed.Length ? MessageType.Info : MessageType.Warning);

                _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(220));
                foreach (Asset a in Needed)
                {
                    bool ok = _found.ContainsKey(a.Key);
                    EditorGUILayout.LabelField(
                        (ok ? "✓  " : "—  ") + a.Key,
                        ok ? Path.GetFileName(_found[a.Key]) : "missing",
                        ok ? EditorStyles.miniLabel : EditorStyles.boldLabel);
                }
                if (_unmatched.Count > 0)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Files the HUD has no place for:",
                                               EditorStyles.miniBoldLabel);
                    foreach (string f in _unmatched)
                        EditorGUILayout.LabelField("   " + Path.GetFileName(f),
                                                   EditorStyles.miniLabel);
                }
                EditorGUILayout.EndScrollView();

                EditorGUILayout.Space();
                if (GUILayout.Button("Build the HUD in this scene", GUILayout.Height(32)))
                    Build();
                EditorGUILayout.LabelField(
                    "Replaces any \"HUD\" object this tool built before. Everything it makes " +
                    "is an ordinary GameObject afterwards.", EditorStyles.wordWrappedMiniLabel);
            }
        }

        // ---------------------------------------------------------------- matching

        /// <summary>
        /// Matches files to the assets the HUD needs.
        ///
        /// By NAME rather than by path, because an export can come out one folder deeper or
        /// one shallower than it went in, and because @2x and @4x are the same sprite at
        /// two sizes. The largest wins: a nine-sliced 3 px line survives being scaled down
        /// and does not survive being scaled up.
        /// </summary>
        void Scan()
        {
            _found.Clear();
            _unmatched.Clear();
            _scanned = true;

            string root = AssetDatabase.GetAssetPath(_folder);
            if (string.IsNullOrEmpty(root)) return;

            string[] files = AssetDatabase.FindAssets("t:Texture2D", new[] { root })
                                          .Select(AssetDatabase.GUIDToAssetPath)
                                          .Distinct().ToArray();

            Dictionary<string, int> bestScale = new Dictionary<string, int>();
            foreach (string path in files)
            {
                string name = Normalise(Path.GetFileNameWithoutExtension(path), out int scale);
                string relative = Normalise(
                    Path.ChangeExtension(path.Substring(root.Length).TrimStart('/'), null), out _);

                Asset? hit = null;
                foreach (Asset a in Needed)
                {
                    string tail = a.Key.Substring(a.Key.LastIndexOf('/') + 1);
                    string withoutPrefix = a.Key.StartsWith("hud/") ? a.Key.Substring(4) : a.Key;
                    if (name == tail || relative == withoutPrefix || relative == a.Key)
                    { hit = a; break; }
                }

                if (hit == null) { _unmatched.Add(path); continue; }

                string key = hit.Value.Key;
                if (!bestScale.TryGetValue(key, out int had) || scale > had)
                {
                    bestScale[key] = scale;
                    _found[key] = path;
                }
            }
        }

        /// <summary>Lower-cased, hyphenated, with any @2x/@4x taken off and reported.</summary>
        static string Normalise(string name, out int scale)
        {
            scale = 1;
            string s = name.ToLowerInvariant().Replace('_', '-').Replace(' ', '-');
            foreach (int k in new[] { 8, 4, 3, 2 })
                if (s.EndsWith($"@{k}x")) { scale = k; s = s.Substring(0, s.Length - 3); break; }
            return s.TrimEnd('-');
        }

        // ---------------------------------------------------------------- import

        /// <summary>
        /// The import settings from docs/27, applied for you.
        ///
        /// Mip maps and compression are both off on purpose and both for the same reason:
        /// this HUD is three-pixel white lines, and a 3 px line is the first thing a mip
        /// chain blurs and the first thing a block compressor turns to porridge. It is
        /// also the whole visual language, so there is nothing left once it goes.
        /// </summary>
        Sprite Import(Asset asset)
        {
            if (!_found.TryGetValue(asset.Key, out string path)) return null;

            TextureImporter imp = (TextureImporter)AssetImporter.GetAtPath(path);
            if (imp == null) return null;

            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.mipmapEnabled = false;
            imp.filterMode = FilterMode.Bilinear;
            imp.alphaIsTransparency = true;
            imp.sRGBTexture = true;
            imp.maxTextureSize = 2048;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.wrapMode = TextureWrapMode.Clamp;

            if (asset.Border != Vector4.zero)
            {
                // The border is quoted at 1x and the file is whatever scale it exported at,
                // so it is scaled by the file rather than typed in twice.
                Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                float scale = tex != null && asset.W > 0 ? tex.width / asset.W : 1f;
                imp.spriteBorder = asset.Border * scale;
            }

            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        // ---------------------------------------------------------------- build

        void Build()
        {
            Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
            foreach (Asset a in Needed)
            {
                Sprite s = Import(a);
                if (s != null) sprites[a.Key] = s;
            }

            GameObject old = GameObject.Find(RootName);
            if (old != null) Undo.DestroyObjectImmediate(old);

            GameObject rootGo = new GameObject(RootName,
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(rootGo, "Build HUD");

            Canvas canvas = rootGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = rootGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(DesignW, DesignH);
            // Matched on HEIGHT, not on 0.5: landscape phones vary in width and not in
            // height, so matching height keeps a thumb-sized button thumb-sized and lets
            // a wider screen simply show more empty floor at the edges.
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            HudView view = Undo.AddComponent<HudView>(rootGo);

            Transform root = rootGo.transform;
            BuildTopBar(root, sprites, view);
            BuildColumn(root, sprites, view);
            BuildSoundAndStick(root, sprites, view);
            BuildControls(root, sprites, view);

            PlayerController[] fighters = Object.FindObjectsByType<PlayerController>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            System.Array.Sort(fighters, (a, b) => a.playerIndex.CompareTo(b.playerIndex));
            if (fighters.Length > 0) view.player1 = fighters[0];
            if (fighters.Length > 1) view.player2 = fighters[1];
            view.matchState = Object.FindFirstObjectByType<MatchState>();

            view.PaintStartingColours();
            EditorUtility.SetDirty(view);
            Selection.activeGameObject = rootGo;

            Debug.Log($"HUD: built from {sprites.Count} of {Needed.Length} sprites. " +
                      (fighters.Length >= 2
                          ? "Both fighters wired."
                          : $"{fighters.Length} fighter(s) in this scene — wire the rest by hand.") +
                      "\nThe clock is a Text with no font if this project has no legacy font; " +
                      "swap it for TMP and drop it back on HudView.Clock.", rootGo);
        }

        // ---------------------------------------------------------------- pieces

        void BuildTopBar(Transform root, Dictionary<string, Sprite> sprites, HudView view)
        {
            RectTransform bar = Panel("Top Bar", root);

            view.bar1 = Bar(bar, sprites, "Health / P1", 134, 36, 0f, out RectTransform fill1);
            view.bar1.fillRect = fill1;
            view.bar1.pips = Pips(bar, sprites, "Stamina / P1", 134, 80, 0f);

            view.bar2 = Bar(bar, sprites, "Health / P2", 1346, 36, 1f, out RectTransform fill2);
            view.bar2.fillRect = fill2;
            view.bar2.pips = Pips(bar, sprites, "Stamina / P2", 1682, 80, 1f);

            RectTransform clockRt = Make("Round Clock", bar, 840, 30, 240, 50, 0.5f);
            Text clock = Undo.AddComponent<Text>(clockRt.gameObject);
            clock.text = "1:24";
            clock.fontSize = 40;
            clock.alignment = TextAnchor.UpperCenter;
            clock.raycastTarget = false;
            clock.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            view.clock = clock;
        }

        HudBar Bar(Transform parent, Dictionary<string, Sprite> sprites, string name,
                   float x, float y, float anchor, out RectTransform fillRt)
        {
            RectTransform rt = Make(name, parent, x, y, 440, 32, anchor);
            Image frame = Picture(rt.gameObject, sprites, "hud/bar/frame", true);

            fillRt = Make("Fill", rt, 8, 8, 424, 16, 0f);
            // The bar empties from the side it is anchored to, so the two drain towards
            // the middle of the screen rather than both towards the left.
            fillRt.pivot = new Vector2(anchor >= 0.5f ? 1f : 0f, 1f);
            fillRt.anchoredPosition = new Vector2(anchor >= 0.5f ? 432 : 8, -8);
            Image fill = Picture(fillRt.gameObject, sprites, "hud/bar/fill", true);

            return new HudBar { frame = frame, fill = fill };
        }

        Graphic[] Pips(Transform parent, Dictionary<string, Sprite> sprites, string name,
                       float x, float y, float anchor)
        {
            const int count = 4, size = 20, gap = 8;
            float width = count * size + (count - 1) * gap;
            RectTransform row = Make(name, parent, anchor >= 0.5f ? x - width : x, y,
                                     width, size, anchor);
            Graphic[] pips = new Graphic[count];
            for (int i = 0; i < count; i++)
            {
                RectTransform p = Make($"pip {i + 1}", row, i * (size + gap), 0, size, size, 0f);
                pips[i] = Picture(p.gameObject, sprites, "hud/pip", false);
            }
            return pips;
        }

        void BuildColumn(Transform root, Dictionary<string, Sprite> sprites, HudView view)
        {
            RectTransform column = Panel("Equipment Column", root);

            RectTransform man = Make("Mannequin", column, 150, 180, 150, 330, 0f);
            Picture(man.gameObject, sprites, "hud/mannequin", false);
            RectTransform lead = Make("Leader lines", column, 210, 180, 115, 350, 0f);
            Picture(lead.gameObject, sprites, "hud/leaders", false);

            (string name, ItemKind kind, string icon, float y, bool empty)[] rows =
            {
                ("Slot / Helmet",  ItemKind.Helmet,  "hud/icon/coif",      176, false),
                ("Slot / Weapon",  ItemKind.Weapon,  "hud/icon/club",      252, false),
                ("Slot / Armour",  ItemKind.Armour,  "hud/icon/chainmail", 328, false),
                ("Slot / Belt",    ItemKind.Belt,    "hud/icon/belt",      404, false),
                ("Slot / Utility", ItemKind.Utility, null,                 480, true),
            };

            List<HudSlot> slots = new List<HudSlot>();
            foreach (var row in rows)
            {
                // A container with no Image of its own. uGUI draws a parent BEFORE its
                // children, so putting the square on the container would have left the
                // halo — its own child — painted over the top of it.
                RectTransform rt = Make(row.name, column, 320, row.y, 64, 64, 0f);

                // Sibling order is draw order: halo behind the square, square behind the
                // item, swap arrow over all of it.
                RectTransform glowRt = Make("Glow", rt, -48, -48, 160, 160, 0f);
                Image glow = Picture(glowRt.gameObject, sprites, "hud/slot/glow", false);

                RectTransform frameRt = Make("Frame", rt, 0, 0, 64, 64, 0f);
                Image frame = Picture(frameRt.gameObject, sprites,
                                      row.empty ? "hud/slot/frame-dashed" : "hud/slot/frame", true);

                RectTransform iconRt = Make("Icon", rt, 16, 16, 32, 32, 0f);
                Image icon = Picture(iconRt.gameObject, sprites, row.icon, false);

                RectTransform swapRt = Make("Swap arrow", rt, 44, -6, 18, 18, 0f);
                Picture(swapRt.gameObject, sprites, "hud/slot/swap-arrow", false);
                swapRt.gameObject.SetActive(false);

                slots.Add(new HudSlot
                {
                    slot = row.kind, frame = frame, glow = glow, icon = icon,
                    swapArrow = swapRt.gameObject,
                });
            }
            view.slots = slots.ToArray();
        }

        void BuildSoundAndStick(Transform root, Dictionary<string, Sprite> sprites, HudView view)
        {
            RectTransform sound = Panel("Sound Direction", root);
            RectTransform arc = Make("Arc", sound, 140, 572, 210, 62, 0f);
            RectTransform src = Make("Source / chest", sound, 228, 558, 46, 46, 0f);

            RectTransform stick = Panel("Joystick", root);
            RectTransform ring = Make("Ring", stick, 148, 648, 200, 200, 0f);
            RectTransform knob = Make("Knob", stick, 256, 676, 68, 68, 0f);

            view.lineArt = new[]
            {
                Picture(arc.gameObject, sprites, "hud/sound/arc", false),
                Picture(src.gameObject, sprites, "hud/sound/source-chest", false),
                Picture(ring.gameObject, sprites, "hud/joystick/ring", false),
                Picture(knob.gameObject, sprites, "hud/joystick/knob", false),
                Existing(root, "Equipment Column/Mannequin"),
                Existing(root, "Equipment Column/Leader lines"),
            }.Where(g => g != null).ToArray();
        }

        void BuildControls(Transform root, Dictionary<string, Sprite> sprites, HudView view)
        {
            RectTransform controls = Panel("Controls", root);

            view.jump = Button(controls, sprites, "Button / Jump", "hud/button/ring-130",
                               "hud/button/glyph-jump", 1576, 558, 130, 56);
            view.block = Button(controls, sprites, "Button / Block", "hud/button/ring-130",
                                "hud/button/glyph-block", 1715, 525, 130, 56);
            view.action = Button(controls, sprites, "Button / Action", "hud/button/ring-101",
                                 "hud/button/glyph-action", 1534, 698, 101, 44);
            view.attack = Button(controls, sprites, "Button / Attack", "hud/button/ring-154",
                                 "hud/button/glyph-attack", 1687, 675, 154, 66);
        }

        HudButton Button(Transform parent, Dictionary<string, Sprite> sprites, string name,
                         string ringKey, string glyphKey, float x, float y, float size,
                         float glyphSize)
        {
            RectTransform rt = Make(name, parent, x, y, size, size, 1f);
            Image ring = Picture(rt.gameObject, sprites, ringKey, false);

            float inset = (size - glyphSize) * 0.5f;
            RectTransform g = Make("Glyph", rt, inset, inset, glyphSize, glyphSize, 0f);
            Image glyph = Picture(g.gameObject, sprites, glyphKey, false);

            return new HudButton { ring = ring, glyph = glyph };
        }

        // ---------------------------------------------------------------- plumbing

        static RectTransform Panel(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Build HUD");
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        /// <summary>
        /// One rect at the coordinates it has in the design.
        ///
        /// Pivot top-left and anchor top-left-ish, so the Figma number goes in unchanged:
        /// the canvas is matched on height, so only X ever moves, and anchorX is which
        /// edge this piece belongs to — 0 for the left of the screen, 1 for the right.
        /// </summary>
        static RectTransform Make(string name, Transform parent, float fx, float fy,
                                  float w, float h, float anchorX)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Build HUD");
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(anchorX, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(fx - anchorX * DesignW, -fy);
            return rt;
        }

        /// <summary>A graphic this build already made, by path, or null if it did not.</summary>
        static Graphic Existing(Transform root, string path)
        {
            Transform t = root.Find(path);
            return t != null ? t.GetComponent<Graphic>() : null;
        }

        static Image Picture(GameObject go, Dictionary<string, Sprite> sprites, string key,
                             bool sliced)
        {
            Image image = go.GetComponent<Image>();
            if (image == null) image = Undo.AddComponent<Image>(go);

            if (key != null && sprites.TryGetValue(key, out Sprite s)) image.sprite = s;
            image.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
            // Nothing in the HUD art takes touches: the controls are driven by the input
            // components of docs/02, and an Image that eats a tap is a button that stops
            // working the day somebody drags it over the joystick.
            image.raycastTarget = false;
            return image;
        }
    }
}
