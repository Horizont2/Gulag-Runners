using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using GulagRunners.Game;
using GulagRunners.Meta;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Builds the search and loading screen.
    ///
    /// Unlike the HUD and the menu this one needs no export to stand up: all five of its
    /// pieces are a circle, a cross, two rounded slabs and a gradient, so it DRAWS them.
    /// Point it at an exported folder and those files win instead; leave the folder empty
    /// and it is still a finished screen. An interface made of five primitives is not
    /// worth blocking on a round trip through Figma.
    ///
    /// Everything else is what HudBuilder and MenuBuilder cost to learn: the export scale
    /// is measured, sprites are made sprites BEFORE anything is measured off them, pixels
    /// per unit comes from the scale so a nine-slice corner draws at the size it was drawn,
    /// each block is pinned to the corner it belongs to, and the tool prints a receipt.
    /// </summary>
    public sealed class SearchBuilder : EditorWindow
    {
        const float DesignW = 1920f;
        const float DesignH = 864f;
        const string RootName = "Search UI";
        const string DrawnFolder = "Assets/UI/Search/generated";

        DefaultAsset _folder;
        Font _font;
        float _scale = 4f;
        float _textScale = 1f;
        bool _scanned;

        readonly Dictionary<string, string> _found = new Dictionary<string, string>();
        readonly Dictionary<string, Vector2> _size = new Dictionary<string, Vector2>();

        [MenuItem("Tools/Gulag Runners/Build the search screen")]
        public static void Open()
        {
            SearchBuilder w = GetWindow<SearchBuilder>(true, "Build the search screen");
            w.minSize = new Vector2(440, 380);
        }

        struct Asset
        {
            public string Key;
            public float W, H;

            /// <summary>Left, bottom, right, top, at 1x. Anything non-zero is drawn Sliced.</summary>
            public Vector4 Border;

            public bool Sliced => Border != Vector4.zero;

            public Asset(string key, float w, float h, float b = 0f)
            { Key = key; W = w; H = h; Border = new Vector4(b, b, b, b); }

            public Asset(string key, float w, float h, float l, float b, float r, float t)
            { Key = key; W = w; H = h; Border = new Vector4(l, b, r, t); }
        }

        static readonly Asset[] Needed =
        {
            new Asset("search/button/close", 56, 56),
            new Asset("search/icon/close", 20, 20),
            // Left and right only. Both bars are capsules: the caps are the first and
            // last 2 px and everything between them is a straight run, so the stretch is
            // horizontal and there is nothing to slice vertically. A border on all four
            // sides of a sprite 4 px tall leaves a middle slice zero pixels high, which
            // is exact at this one height and garbage at any other.
            new Asset("search/bar/track", 120, 4, 2, 0, 2, 0),
            new Asset("search/bar/fill", 120, 4, 2, 0, 2, 0),
            new Asset("search/scrim", 8, 320),
        };

        // ---------------------------------------------------------------- window

        void OnGUI()
        {
            EditorGUILayout.LabelField("Sprites", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Optional. All five pieces are drawn if they are not here, so this screen " +
                "builds with nothing exported at all. A folder wins where it has a file.",
                EditorStyles.wordWrappedMiniLabel);

            EditorGUI.BeginChangeCheck();
            _folder = (DefaultAsset)EditorGUILayout.ObjectField(
                "Folder", _folder, typeof(DefaultAsset), false);
            if (EditorGUI.EndChangeCheck()) _scanned = false;

            if (GUILayout.Button("Scan", GUILayout.Height(22))) Scan();

            if (_scanned)
                EditorGUILayout.HelpBox(
                    $"{_found.Count} of {Needed.Length} found in the folder at " +
                    $"{_scale:0.##}x. The rest will be drawn.", MessageType.Info);

            EditorGUILayout.Space();
            _font = (Font)EditorGUILayout.ObjectField(
                new GUIContent("Typeface",
                               "Optional. This project ships no font, so without one every " +
                               "label comes out in Unity's built-in Arial."),
                _font, typeof(Font), false);
            _textScale = EditorGUILayout.Slider(
                new GUIContent("Text size", "Against the design sheet."),
                _textScale, 0.6f, 1.6f);

            EditorGUILayout.Space();
            if (GUILayout.Button("Build the search screen", GUILayout.Height(32))) Build();

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "The backdrop script goes on the canvas itself. Drop your stills into its " +
                "Stills array — anything in there is cross-faded while the player waits.",
                MessageType.None);
        }

        // ---------------------------------------------------------------- the sprites

        void Scan()
        {
            _found.Clear();
            _scanned = true;

            string root = _folder != null ? AssetDatabase.GetAssetPath(_folder) : null;
            if (string.IsNullOrEmpty(root)) { _scale = 4f; return; }

            string[] files = AssetDatabase.FindAssets("t:Texture2D", new[] { root })
                                          .Select(AssetDatabase.GUIDToAssetPath)
                                          .Distinct().OrderBy(p => p).ToArray();

            // Path first, for every key, and only then bare filenames -- in two passes,
            // because TWO of the five are called close.png. One loop matching either way
            // bound icon/close.png to the button key on the filename and left the icon to
            // be drawn, with the export sitting right there in the folder.
            foreach (string path in files)
            {
                string relative = Relative(path, root);
                foreach (Asset a in Needed)
                {
                    string bare = a.Key.StartsWith("search/") ? a.Key.Substring(7) : a.Key;
                    if (relative == a.Key || relative == bare) { _found[a.Key] = path; break; }
                }
            }

            foreach (Asset a in Needed)
            {
                if (_found.ContainsKey(a.Key)) continue;
                string tail = a.Key.Substring(a.Key.LastIndexOf('/') + 1);

                // Only when that name is unambiguous: among the files still spare, exactly
                // one carries it. Two spare close.png and neither is guessed at.
                string[] hits = files
                    .Where(p => !_found.ContainsValue(p) &&
                                Path.GetFileNameWithoutExtension(p).ToLowerInvariant() == tail)
                    .ToArray();
                if (hits.Length == 1) _found[a.Key] = hits[0];
            }

            Prepare();
            _scale = MeasureScale();
        }

        static string Relative(string path, string root)
        {
            string p = path.Replace('\\', '/');
            string r = root.Replace('\\', '/').TrimEnd('/') + "/";
            if (p.StartsWith(r)) p = p.Substring(r.Length);
            int dot = p.LastIndexOf('.');
            return (dot > 0 ? p.Substring(0, dot) : p).ToLowerInvariant();
        }

        /// <summary>
        /// Sprites BEFORE anything is measured. A fresh PNG imports as a plain texture
        /// whose npotScale is ToNearest, so a 96 px icon reports 128 and the measured
        /// scale comes out of a power of two rather than the file. That one cost a whole
        /// menu built a quarter too small.
        /// </summary>
        void Prepare()
        {
            foreach (string path in _found.Values)
            {
                TextureImporter imp = (TextureImporter)AssetImporter.GetAtPath(path);
                if (imp == null) continue;
                if (imp.textureType == TextureImporterType.Sprite &&
                    imp.npotScale == TextureImporterNPOTScale.None) continue;
                imp.textureType = TextureImporterType.Sprite;
                imp.npotScale = TextureImporterNPOTScale.None;
                imp.SaveAndReimport();
            }
        }

        float MeasureScale()
        {
            Dictionary<float, int> votes = new Dictionary<float, int>();
            foreach (Asset a in Needed)
            {
                if (a.W <= 0f || !_found.TryGetValue(a.Key, out string path)) continue;
                Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex == null) continue;
                float v = Mathf.Round(tex.width / a.W * 4f) / 4f;
                if (v > 0f) { votes.TryGetValue(v, out int n); votes[v] = n + 1; }
            }
            float best = 4f; int most = 0;
            foreach (KeyValuePair<float, int> kv in votes)
                if (kv.Value > most) { most = kv.Value; best = kv.Key; }
            return best;
        }

        /// <summary>Draws anything the folder did not supply, into Assets/UI/Search/generated.</summary>
        void DrawMissing(List<string> drew)
        {
            int at = Mathf.Max(1, Mathf.RoundToInt(_scale));
            foreach (Asset a in Needed)
            {
                if (_found.ContainsKey(a.Key)) continue;

                if (!AssetDatabase.IsValidFolder("Assets/UI")) AssetDatabase.CreateFolder("Assets", "UI");
                if (!AssetDatabase.IsValidFolder("Assets/UI/Search"))
                    AssetDatabase.CreateFolder("Assets/UI", "Search");
                if (!AssetDatabase.IsValidFolder(DrawnFolder))
                    AssetDatabase.CreateFolder("Assets/UI/Search", "generated");

                string file = a.Key.Substring(a.Key.LastIndexOf('/') + 1);
                string path = $"{DrawnFolder}/{file}.png";
                Texture2D tex =
                    a.Key == "search/button/close" ? Ring((int)a.W, at) :
                    a.Key == "search/icon/close" ? Cross((int)a.W, at) :
                    a.Key == "search/scrim" ? Scrim((int)a.W, (int)a.H, at) :
                    Slab((int)a.W, (int)a.H, at);

                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                _found[a.Key] = path;
                drew.Add(file);
            }
        }

        static Texture2D Blank(int w, int h)
        {
            Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            Color[] px = new Color[w * h];
            t.SetPixels(px);
            return t;
        }

        static Texture2D Ring(int size, int at)
        {
            int S = size * at;
            Texture2D t = Blank(S, S);
            Color[] px = t.GetPixels();
            float r = S * 0.5f - at, stroke = 1.5f * at, c = S * 0.5f;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = Mathf.Abs(Mathf.Sqrt((x + 0.5f - c) * (x + 0.5f - c) +
                                                   (y + 0.5f - c) * (y + 0.5f - c)) - r);
                    float a = 1f - Mathf.SmoothStep(stroke * 0.5f - at, stroke * 0.5f + at, d);
                    px[y * S + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
                }
            t.SetPixels(px); t.Apply();
            return t;
        }

        static Texture2D Cross(int size, int at)
        {
            int S = size * at;
            Texture2D t = Blank(S, S);
            Color[] px = t.GetPixels();
            float stroke = 1.8f * at, pad = S * 0.22f;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float a = Mathf.Max(Line(x, y, pad, pad, S - pad, S - pad, stroke, at),
                                        Line(x, y, S - pad, pad, pad, S - pad, stroke, at));
                    px[y * S + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
                }
            t.SetPixels(px); t.Apply();
            return t;
        }

        /// <summary>Antialiased distance to a segment, as coverage.</summary>
        static float Line(float px, float py, float ax, float ay, float bx, float by,
                          float stroke, int at)
        {
            float vx = bx - ax, vy = by - ay;
            float len2 = vx * vx + vy * vy;
            float t = len2 <= 0f ? 0f : Mathf.Clamp01(((px - ax) * vx + (py - ay) * vy) / len2);
            float dx = px + 0.5f - (ax + vx * t), dy = py + 0.5f - (ay + vy * t);
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            return 1f - Mathf.SmoothStep(stroke * 0.5f - at, stroke * 0.5f + at, d);
        }

        static Texture2D Slab(int w, int h, int at)
        {
            int W = w * at, H = h * at;
            Texture2D t = Blank(W, H);
            Color[] px = t.GetPixels();
            float r = H * 0.5f;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float dx = Mathf.Max(r - (x + 0.5f), (x + 0.5f) - (W - r), 0f);
                    float dy = Mathf.Abs(y + 0.5f - H * 0.5f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = 1f - Mathf.SmoothStep(r - at, r, d);
                    px[y * W + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
                }
            t.SetPixels(px); t.Apply();
            return t;
        }

        /// <summary>
        /// White at the bottom, nothing at the top, eased. White so the screen can tint it
        /// black — the same rule as every other sprite in this project, and the reason
        /// there is one scrim file rather than two that drift apart.
        /// </summary>
        static Texture2D Scrim(int w, int h, int at)
        {
            int W = w * at, H = h * at;
            Texture2D t = Blank(W, H);
            Color[] px = t.GetPixels();
            for (int y = 0; y < H; y++)
            {
                float up = y / (float)(H - 1);               // 0 at the bottom row
                float a = Mathf.SmoothStep(0f, 1f, 1f - up);
                for (int x = 0; x < W; x++) px[y * W + x] = new Color(1f, 1f, 1f, a);
            }
            t.SetPixels(px); t.Apply();
            return t;
        }

        Sprite Import(Asset asset)
        {
            if (!_found.TryGetValue(asset.Key, out string path)) return null;

            TextureImporter imp = (TextureImporter)AssetImporter.GetAtPath(path);
            if (imp == null) return null;

            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.npotScale = TextureImporterNPOTScale.None;
            imp.mipmapEnabled = false;
            imp.filterMode = FilterMode.Bilinear;
            imp.alphaIsTransparency = true;
            imp.sRGBTexture = true;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.spritePixelsPerUnit = 100f * Mathf.Max(0.01f, _scale);
            if (asset.Border != Vector4.zero) imp.spriteBorder = asset.Border * _scale;

            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        Vector2 Size(string key, float w, float h) =>
            _size.TryGetValue(key, out Vector2 v) ? v : new Vector2(w, h);

        // ---------------------------------------------------------------- the build

        void Build()
        {
            if (!_scanned) Scan();

            List<string> drew = new List<string>();
            DrawMissing(drew);

            _size.Clear();
            Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
            foreach (Asset a in Needed)
            {
                Sprite s = Import(a);
                if (s == null) continue;
                sprites[a.Key] = s;
                _size[a.Key] = new Vector2(s.rect.width, s.rect.height) / _scale;
            }

            GameObject old = GameObject.Find(RootName);
            if (old != null) Undo.DestroyObjectImmediate(old);

            GameObject rootGo = new GameObject(RootName,
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(rootGo, "Build the search screen");

            Canvas canvas = rootGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Over the menu, which is what it covers.
            canvas.sortingOrder = 10;

            CanvasScaler scaler = rootGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(DesignW, DesignH);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            // The tips are NOT set here. They come up with the component, out of the one
            // pool in MatchSearch, and this used to carry a second copy and overwrite
            // them -- two lists that have to agree, which is the trap the scrim is one
            // file to avoid. Edit them on the component, in the inspector.
            MatchSearch search = Undo.AddComponent<MatchSearch>(rootGo);

            // Everything that is drawn lives under one child, so shownChanged can switch
            // the whole screen off without taking MatchSearch's own Update with it.
            RectTransform screen = Panel("Screen", rootGo.transform);

            List<string> wired = new List<string>();
            MenuBackdrop backdrop = BuildBackdrop(screen, rootGo, sprites);
            search.backdrop = backdrop;

            BuildScrims(screen, sprites);
            BuildTip(screen, search, wired);
            BuildCancel(screen, sprites, search, wired);
            BuildStatus(screen, sprites, search, wired);

            UnityEventTools.AddPersistentListener(search.shownChanged,
                new UnityAction<bool>(screen.gameObject.SetActive));
            wired.Add("shown");

            EnsureEventSystem();

            if (search.director == null)
                search.director = Object.FindFirstObjectByType<MenuDirector>();
            if (search.progress == null)
                search.progress = Object.FindFirstObjectByType<PlayerProgress>();

            MenuUI menu = Object.FindFirstObjectByType<MenuUI>();
            if (menu != null)
            {
                Undo.RecordObject(menu, "Build the search screen");
                menu.search = search;
                EditorUtility.SetDirty(menu);
                wired.Add("PLAY on the menu");
            }

            screen.gameObject.SetActive(false);
            EditorUtility.SetDirty(search);
            Selection.activeGameObject = rootGo;

            string made = drew.Count > 0 ? $" Drew {string.Join(", ", drew)}." : "";
            string warn = search.director == null
                ? "\nNo MenuDirector in this scene, so there is nothing to load the arena."
                : "";
            Debug.Log($"Search screen: built at {_scale:0.##}x.{made}\n" +
                      $"Wired: {string.Join(", ", wired)}. " +
                      $"{(search.tips != null ? search.tips.Length : 0)} tips on the " +
                      "component, every number taken out of the game as it stands.\n" +
                      "Drop your stills into Menu Backdrop > Stills on the canvas." + warn,
                      rootGo);
        }

        MenuBackdrop BuildBackdrop(Transform screen, GameObject rootGo,
                                   Dictionary<string, Sprite> sprites)
        {
            RectTransform holder = Panel("Backdrop", screen);
            RectTransform back = Panel("Still / back", holder);
            RectTransform front = Panel("Still / front", holder);

            Image b = Undo.AddComponent<Image>(back.gameObject);
            Image f = Undo.AddComponent<Image>(front.gameObject);
            foreach (Image i in new[] { b, f })
            {
                i.raycastTarget = false;
                i.preserveAspect = false;
                i.type = Image.Type.Simple;
                i.color = new Color(1f, 1f, 1f, 1f);
                i.enabled = false;         // nothing to show until stills are dropped in
            }

            // ON THE CANVAS ITSELF, which is where somebody will look for it.
            MenuBackdrop backdrop = Undo.AddComponent<MenuBackdrop>(rootGo);
            backdrop.back = b;
            backdrop.front = f;
            return backdrop;
        }

        void BuildScrims(Transform screen, Dictionary<string, Sprite> sprites)
        {
            Vector2 s = Size("search/scrim", 8, 320);

            RectTransform bottom = Stretch("Scrim / bottom", screen, 0f, s.y);
            Dark(Picture(bottom.gameObject, sprites, "search/scrim"), 0.92f);

            RectTransform top = Stretch("Scrim / top", screen, 1f, s.y * 0.75f);
            // One file, used twice, flipped. Two gradients that have to stay in step are
            // two gradients that will not.
            top.localScale = new Vector3(1f, -1f, 1f);
            Dark(Picture(top.gameObject, sprites, "search/scrim"), 0.78f);
        }

        void BuildTip(Transform screen, MatchSearch search, List<string> wired)
        {
            RectTransform rt = Make("Tip", screen, 64, 52, 620, 110, 0f, 0f);
            // The first one off the component, so the laid-out screen shows a real tip at
            // real length rather than a placeholder that fits better than the truth.
            string first = search.tips != null && search.tips.Length > 0
                ? search.tips[0]
                : "";
            MenuLabel tip = LabelOn(rt, first, 20, TextAnchor.UpperLeft, false, 0.82f);
            tip.target.lineSpacing = 1.45f;
            UnityEventTools.AddPersistentListener(search.tipChanged,
                new UnityAction<string>(tip.Set));
            wired.Add("tip");
        }

        void BuildCancel(Transform screen, Dictionary<string, Sprite> sprites,
                         MatchSearch search, List<string> wired)
        {
            Vector2 ring = Size("search/button/close", 56, 56);
            RectTransform rt = Make("Button / Cancel", screen, DesignW - 64 - ring.x, 52,
                                    ring.x, ring.y, 1f, 0f);
            Image face = Picture(rt.gameObject, sprites, "search/button/close");
            face.raycastTarget = true;
            face.color = new Color(1f, 1f, 1f, 0.45f);

            Vector2 cross = Size("search/icon/close", 20, 20);
            Vector2 inset = (ring - cross) * 0.5f;
            RectTransform g = Make("Glyph", rt, inset.x, inset.y, cross.x, cross.y, 0f, 0f);
            Picture(g.gameObject, sprites, "search/icon/close").color =
                new Color(1f, 1f, 1f, 0.8f);

            Button button = Undo.AddComponent<Button>(rt.gameObject);
            button.targetGraphic = face;
            UnityEventTools.AddPersistentListener(button.onClick, new UnityAction(search.Cancel));
            wired.Add("cancel");
        }

        void BuildStatus(Transform screen, Dictionary<string, Sprite> sprites,
                         MatchSearch search, List<string> wired)
        {
            RectTransform phaseRt = Make("Phase", screen, (DesignW - 900) * 0.5f, 672,
                                         900, 44, 0.5f, 0f);
            MenuLabel phase = LabelOn(phaseRt, "SEARCHING", 30, TextAnchor.UpperCenter,
                                      true, 0.95f);
            UnityEventTools.AddPersistentListener(search.phaseChanged,
                new UnityAction<string>(phase.Set));
            wired.Add("phase");

            RectTransform detailRt = Make("Detail", screen, (DesignW - 900) * 0.5f, 722,
                                          900, 30, 0.5f, 0f);
            MenuLabel detail = LabelOn(detailRt, "SILVER III  ·  0 S", 18,
                                       TextAnchor.UpperCenter, false, 0.55f);
            UnityEventTools.AddPersistentListener(search.detailChanged,
                new UnityAction<string>(detail.Set));
            wired.Add("detail");

            Vector2 bar = Size("search/bar/track", 120, 4);
            const float barW = 1000f;
            RectTransform track = Make("Bar", screen, (DesignW - barW) * 0.5f, 772,
                                       barW, bar.y, 0.5f, 0f);
            Picture(track.gameObject, sprites, "search/bar/track").color =
                new Color(1f, 1f, 1f, 0.18f);

            RectTransform fill = Make("Fill", track, 0, 0, barW, bar.y, 0f, 0f);
            Picture(fill.gameObject, sprites, "search/bar/fill").color =
                new Color(1f, 1f, 1f, 0.92f);

            MenuBar menuBar = Undo.AddComponent<MenuBar>(track.gameObject);
            menuBar.fill = fill;
            menuBar.preview = 0f;
            menuBar.Fill(0f);
            UnityEventTools.AddPersistentListener(search.barFilled,
                new UnityAction<float>(menuBar.Fill));
            wired.Add("bar");
        }

        // ---------------------------------------------------------------- plumbing

        static RectTransform Panel(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Build the search screen");
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        /// <summary>Full width, pinned to one edge, a fixed height.</summary>
        static RectTransform Stretch(string name, Transform parent, float edge, float height)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Build the search screen");
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0f, edge);
            rt.anchorMax = new Vector2(1f, edge);
            rt.pivot = new Vector2(0.5f, edge);
            rt.sizeDelta = new Vector2(0f, height);
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        static RectTransform Make(string name, Transform parent, float fx, float fy,
                                  float w, float h, float anchorX, float vAnchor)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Build the search screen");
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(anchorX, 1f - vAnchor);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(fx - anchorX * DesignW,
                                              vAnchor > 0.5f ? DesignH - fy : -fy);
            return rt;
        }

        Font Face => _font != null
            ? _font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        MenuLabel LabelOn(RectTransform rt, string chars, float size, TextAnchor align,
                          bool bold, float alpha)
        {
            Text t = Undo.AddComponent<Text>(rt.gameObject);
            t.text = chars;
            t.fontSize = Mathf.Max(8, Mathf.RoundToInt(size * _textScale));
            t.alignment = align;
            t.raycastTarget = false;
            t.color = new Color(1f, 1f, 1f, alpha);
            t.font = Face;
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;

            MenuLabel label = Undo.AddComponent<MenuLabel>(rt.gameObject);
            label.target = t;
            label.placeholder = chars;
            return label;
        }

        /// <summary>
        /// Sliced comes off the asset table, not off the call site. Both bars carry a
        /// border and were being drawn Simple, which ignores it: a 120 px capsule
        /// stretched across 1000 smears its two rounded caps over the whole length, and
        /// the border declared beside it looked like the problem was already handled.
        /// </summary>
        static Image Picture(GameObject go, Dictionary<string, Sprite> sprites, string key)
        {
            Image image = go.GetComponent<Image>();
            if (image == null) image = Undo.AddComponent<Image>(go);
            if (sprites.TryGetValue(key, out Sprite s)) image.sprite = s;

            bool sliced = false;
            foreach (Asset a in Needed)
                if (a.Key == key) { sliced = a.Sliced; break; }

            image.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
            image.raycastTarget = false;
            return image;
        }

        static void Dark(Image image, float alpha)
        {
            image.color = new Color(0f, 0f, 0f, alpha);
        }

        static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null) return;
            GameObject go = new GameObject("EventSystem", typeof(EventSystem),
                                           typeof(InputSystemUIInputModule));
            Undo.RegisterCreatedObjectUndo(go, "Build the search screen");
        }
    }
}
