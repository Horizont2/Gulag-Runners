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
    /// Builds the menu's first screen out of the sprites exported from the Figma file.
    ///
    /// The same deal as <see cref="HudBuilder"/>, and built on everything that one cost to
    /// learn: the export scale is MEASURED rather than assumed, every rect is sized from
    /// the file rather than from the design sheet, pixels-per-unit is set so a nine-slice
    /// corner draws at the size it was drawn at, each block is pinned to the corner it
    /// belongs to so the canvas can Expand, and the tool prints a receipt naming every
    /// wire it made and every one it could not.
    ///
    /// The scan is simpler than the HUD's, and deliberately so. The HUD sheet was exported
    /// from nodes Figma had named after the vector inside them -- Vector.png, Vector-1.png
    /// -- so the matcher had to guess from aspect ratios. The menu sheet's nodes carry
    /// their KEYS as names, so a file either says what it is or is reported. Guessing is a
    /// cost paid for a sheet that cannot be re-exported; this one can.
    ///
    /// Nothing here runs at play time. This is a scene the tool WRITES, once, and every
    /// object it leaves behind is an ordinary GameObject a designer can move or delete.
    /// </summary>
    public sealed class MenuBuilder : EditorWindow
    {
        const float DesignW = 1920f;
        const float DesignH = 864f;
        const string RootName = "Menu UI";

        DefaultAsset _folder;
        bool _scanned;
        float _scale = 1f;
        float _readoutScale = 1f;
        float _buttonScale = 1f;
        Font _font;

        readonly Dictionary<string, string> _found = new Dictionary<string, string>();
        readonly List<string> _unmatched = new List<string>();
        readonly Dictionary<string, Vector2> _size = new Dictionary<string, Vector2>();

        [MenuItem("Tools/Gulag Runners/Build the menu screen")]
        public static void Open()
        {
            MenuBuilder w = GetWindow<MenuBuilder>(true, "Build the menu screen");
            w.minSize = new Vector2(440, 440);
        }

        // ---------------------------------------------------------------- what it needs

        struct Asset
        {
            public string Key;
            public float W, H;
            public Vector4 Border;
            public Asset(string key, float w, float h) { Key = key; W = w; H = h; Border = Vector4.zero; }
            public Asset(string key, float w, float h, float b)
            { Key = key; W = w; H = h; Border = new Vector4(b, b, b, b); }
        }

        static readonly Asset[] Needed =
        {
            new Asset("menu/icon/shield", 24, 24),
            new Asset("menu/icon/coin", 24, 24),
            new Asset("menu/icon/contract", 24, 24),
            new Asset("menu/icon/gear", 24, 24),
            new Asset("menu/icon/people", 24, 24),
            new Asset("menu/rank/bronze", 24, 24),
            new Asset("menu/rank/silver", 24, 24),
            new Asset("menu/rank/gold", 24, 24),
            new Asset("menu/rank/platinum", 24, 24),
            new Asset("menu/rank/diamond", 24, 24),
            new Asset("menu/rank/runner", 24, 24),
            new Asset("menu/bar/track", 430, 10, 5),
            new Asset("menu/bar/fill", 430, 10, 5),
            new Asset("menu/plate", 300, 52, 26),
            new Asset("menu/button/play", 400, 112, 14),
            new Asset("menu/button/round", 66, 66),
        };

        /// <summary>The six, in League order, so the badge component can be filled by index.</summary>
        static readonly string[] Badges =
        {
            "menu/rank/bronze", "menu/rank/silver", "menu/rank/gold",
            "menu/rank/platinum", "menu/rank/diamond", "menu/rank/runner",
        };

        // ---------------------------------------------------------------- window

        void OnGUI()
        {
            EditorGUILayout.LabelField("Exported sprites", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "The folder Figma exported, anywhere under Assets. Every node on the " +
                "\"Export — Menu assets\" sheet is named after its key, so the files " +
                "arrive saying what they are.", EditorStyles.wordWrappedMiniLabel);

            EditorGUI.BeginChangeCheck();
            _folder = (DefaultAsset)EditorGUILayout.ObjectField(
                "Folder", _folder, typeof(DefaultAsset), false);
            if (EditorGUI.EndChangeCheck()) _scanned = false;

            using (new EditorGUI.DisabledScope(_folder == null))
                if (GUILayout.Button("Scan the folder", GUILayout.Height(24))) Scan();

            if (_scanned)
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(
                    _found.Count == Needed.Length
                        ? $"All {Needed.Length} sprites found, exported at {_scale:0.##}x."
                        : $"{_found.Count} of {Needed.Length} found at {_scale:0.##}x. The " +
                          "rest are listed below; the screen still builds without them and " +
                          "leaves those pieces blank.",
                    _found.Count == Needed.Length ? MessageType.Info : MessageType.Warning);

                foreach (Asset a in Needed)
                    if (!_found.ContainsKey(a.Key))
                        EditorGUILayout.LabelField("   missing", a.Key);

                foreach (string path in _unmatched)
                    EditorGUILayout.LabelField("   unused", Path.GetFileName(path));
            }

            EditorGUILayout.Space();
            _font = (Font)EditorGUILayout.ObjectField(
                new GUIContent("Typeface",
                               "Optional. The sheet is set in Inter; this project ships no " +
                               "font and no TextMeshPro, so without one here every label " +
                               "comes out in Unity's built-in Arial and the screen will " +
                               "not read like the mock-up. Import Inter and drop it in."),
                _font, typeof(Font), false);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Size on screen", EditorStyles.boldLabel);
            _readoutScale = EditorGUILayout.Slider(
                new GUIContent("Readouts", "League, rank bar, gold, contracts, the name."),
                _readoutScale, 0.5f, 1.4f);
            _buttonScale = EditorGUILayout.Slider(
                new GUIContent("Buttons",
                               "PLAY and the two round ones. A thumb target wants 9 mm; " +
                               "PLAY at 112 units tall is about 8 mm on a 6.5\" phone, so " +
                               "there is not much room below 1 here."),
                _buttonScale, 0.5f, 1.4f);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(!_scanned))
                if (GUILayout.Button("Build the menu screen", GUILayout.Height(32))) Build();
        }

        // ---------------------------------------------------------------- the scan

        void Scan()
        {
            _found.Clear();
            _unmatched.Clear();
            _scanned = true;

            string root = AssetDatabase.GetAssetPath(_folder);
            if (string.IsNullOrEmpty(root)) return;

            string[] files = AssetDatabase.FindAssets("t:Texture2D", new[] { root })
                                          .Select(AssetDatabase.GUIDToAssetPath)
                                          .Distinct().OrderBy(p => p).ToArray();

            // A bare filename is only safe when nothing else in the set ends the same way.
            // Nothing does here, but the guard stays: the day somebody adds menu/icon/gold
            // beside menu/rank/gold, this is what stops one quietly claiming the other.
            HashSet<string> ambiguous = new HashSet<string>(
                Needed.Select(a => Tail(a.Key)).GroupBy(t => t)
                      .Where(g => g.Count() > 1).Select(g => g.Key));

            Dictionary<string, int> bestScale = new Dictionary<string, int>();

            foreach (string path in files)
            {
                string relative = Relative(path, root);
                string name = Normalise(Path.GetFileNameWithoutExtension(path), out int at);

                string key = null;
                foreach (Asset a in Needed)
                {
                    string withoutPrefix = a.Key.StartsWith("menu/") ? a.Key.Substring(5) : a.Key;
                    bool byPath = relative == a.Key || relative == withoutPrefix;
                    bool byName = name == Tail(a.Key) && !ambiguous.Contains(Tail(a.Key));
                    if (byPath || byName) { key = a.Key; break; }
                }

                if (key == null) { _unmatched.Add(path); continue; }

                // Two copies of the same piece at different scales: keep the larger.
                if (!bestScale.TryGetValue(key, out int had) || at > had)
                {
                    bestScale[key] = at;
                    _found[key] = path;
                }
            }

            Prepare();
            _scale = MeasureScale();
        }

        /// <summary>
        /// Make every found file a Sprite BEFORE anything is measured off it.
        ///
        /// Unity imports a new PNG as a plain TEXTURE, and a plain texture has npotScale
        /// set to ToNearest -- so a 96x96 icon is reported as 128x128 and a 1600x448
        /// button as 2048x512. Measuring the export scale off tex.width then measures the
        /// power of two Unity rounded it to rather than the file: eleven 96 px icons all
        /// read 128/24 = 5.33 and voted the whole sheet to 5.25x, which put a 26 unit
        /// corner on the name plate at 34 and stretched the art on its way in.
        ///
        /// Sprites are never NPOT-scaled, so one pass of this and tex.width is the file
        /// again. It is also the import these assets want in the end, so nothing is wasted.
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

        static string Tail(string key) => key.Substring(key.LastIndexOf('/') + 1);

        static string Relative(string path, string root)
        {
            string p = path.Replace('\\', '/');
            string r = root.Replace('\\', '/').TrimEnd('/') + "/";
            if (p.StartsWith(r)) p = p.Substring(r.Length);
            int dot = p.LastIndexOf('.');
            if (dot > 0) p = p.Substring(0, dot);
            return Normalise(p, out _);
        }

        static string Normalise(string name, out int scale)
        {
            scale = 1;
            string s = name.ToLowerInvariant().Replace('_', '-').Replace(' ', '-');
            foreach (int k in new[] { 8, 4, 3, 2 })
                if (s.EndsWith($"@{k}x")) { scale = k; s = s.Substring(0, s.Length - 3); break; }
            return s.TrimEnd('-');
        }

        /// <summary>
        /// At what multiple of the design this folder came out, as the value the MOST
        /// assets agree on. Not the mean: one frame Figma cropped tight to its stroke drags
        /// a mean off, and surviving exactly that is what this exists for.
        /// </summary>
        float MeasureScale()
        {
            Dictionary<float, int> votes = new Dictionary<float, int>();
            foreach (Asset a in Needed)
            {
                if (a.W <= 0f || !_found.TryGetValue(a.Key, out string path)) continue;
                Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex == null) continue;

                float v = Mathf.Round(tex.width / a.W * 4f) / 4f;
                if (v <= 0f) continue;
                votes.TryGetValue(v, out int n);
                votes[v] = n + 1;
            }

            float best = 1f;
            int bestVotes = 0;
            foreach (KeyValuePair<float, int> kv in votes)
                if (kv.Value > bestVotes) { bestVotes = kv.Value; best = kv.Key; }
            return best;
        }

        Sprite Import(Asset asset)
        {
            if (!_found.TryGetValue(asset.Key, out string path)) return null;

            TextureImporter imp = (TextureImporter)AssetImporter.GetAtPath(path);
            if (imp == null) return null;

            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            // Never rounded to a power of two. A 1600x448 button stretched to 2048x512 is
            // not the art that was drawn, and it is the same setting that made the scale
            // unmeasurable above.
            imp.npotScale = TextureImporterNPOTScale.None;
            imp.mipmapEnabled = false;
            imp.filterMode = FilterMode.Bilinear;
            imp.alphaIsTransparency = true;
            imp.sRGBTexture = true;
            imp.maxTextureSize = 2048;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.wrapMode = TextureWrapMode.Clamp;

            // A nine-slice border is quoted in TEXTURE pixels and uGUI divides it by
            // spritePPU / canvasReferencePPU before drawing. Left at the default 100
            // against the canvas's own 100 that divisor is 1, so a 5 px corner exported at
            // 4x draws as a 20 UNIT corner. That is what made the HUD look heavy for
            // several passes before anybody found it.
            imp.spritePixelsPerUnit = 100f * Mathf.Max(0.01f, _scale);
            if (asset.Border != Vector4.zero) imp.spriteBorder = asset.Border * _scale;

            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>The size a sprite actually is, in design units, or the design size.</summary>
        Vector2 Size(string key, float w, float h) =>
            _size.TryGetValue(key, out Vector2 v) ? v : new Vector2(w, h);

        // ---------------------------------------------------------------- the build

        void Build()
        {
            _size.Clear();
            Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
            List<string> odd = new List<string>();

            foreach (Asset a in Needed)
            {
                Sprite s = Import(a);
                if (s == null) continue;
                sprites[a.Key] = s;

                // The design size is what the layout was drawn against; the file is what
                // arrived. Where they disagree the FILE wins -- a rect forced to the design
                // size squashes the art -- and the disagreement is reported, not swallowed.
                Vector2 native = new Vector2(s.rect.width, s.rect.height) / _scale;
                _size[a.Key] = native;
                if (a.W > 0f && a.H > 0f &&
                    (Mathf.Abs(native.x - a.W) > a.W * 0.02f ||
                     Mathf.Abs(native.y - a.H) > a.H * 0.02f))
                    odd.Add($"{a.Key} is {native.x:0}x{native.y:0}, not the {a.W:0}x{a.H:0} " +
                            "the layout expected");
            }

            GameObject old = GameObject.Find(RootName);
            if (old != null) Undo.DestroyObjectImmediate(old);

            GameObject rootGo = new GameObject(RootName,
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(rootGo, "Build the menu");

            Canvas canvas = rootGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = rootGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(DesignW, DesignH);
            // Expand, for the reason docs/28 gives: the sheet is 20:9, and matched on
            // height a 16:9 screen gets a canvas narrower than the design, which squeezes
            // the two top corners towards each other. Expand never lets the canvas be
            // smaller than the sheet in either direction.
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            MenuUI view = Object.FindFirstObjectByType<MenuUI>();
            if (view == null) view = Undo.AddComponent<MenuUI>(rootGo);

            Transform root = rootGo.transform;
            List<string> wired = new List<string>();
            List<string> dead = new List<string>();

            BuildLeague(root, sprites, view, wired, dead);
            BuildPurse(root, sprites, view, wired, dead);
            BuildPlate(root, sprites, view, wired, dead);
            BuildPlay(root, sprites, view, wired, dead);
            BuildCorner(root, sprites, view, wired, dead);

            EnsureEventSystem();

            if (view.director == null)
                view.director = Object.FindFirstObjectByType<MenuDirector>();

            // The ladder and the purse live here, because the menu is the only screen that
            // reads them and the only one that outlives a match. Made if the scene has
            // none: without it the league and the gold are the typed-in numbers on
            // MenuProfile, which look exactly like real ones and are not.
            if (view.progress == null)
            {
                view.progress = Object.FindFirstObjectByType<PlayerProgress>();
                if (view.progress == null)
                {
                    GameObject save = new GameObject("Player Progress", typeof(PlayerProgress));
                    Undo.RegisterCreatedObjectUndo(save, "Build the menu");
                    view.progress = save.GetComponent<PlayerProgress>();
                    wired.Add("a new Player Progress");
                }
                else wired.Add("the Player Progress already in the scene");
            }

            if (view.director == null)
                dead.Add("MenuDirector — PLAY will do nothing until one is in the scene");

            EditorUtility.SetDirty(view);
            Selection.activeGameObject = rootGo;

            string sizes = odd.Count > 0
                ? "\nSized from the files rather than the layout, because they disagree:\n  " +
                  string.Join("\n  ", odd)
                : "";
            string missing = dead.Count > 0
                ? "\nNOT wired: " + string.Join(", ", dead) + " — those readouts stay blank."
                : "";

            Debug.Log($"Menu: built from {sprites.Count} of {Needed.Length} sprites at " +
                      $"{_scale:0.##}x.\nWired: {string.Join(", ", wired)}." + missing + sizes +
                      "\nThe labels are Text with the built-in font; swap them for TMP and " +
                      "drop them back on their MenuLabel if this project grows one.", rootGo);
        }

        // ---------------------------------------------------------------- the blocks

        void BuildLeague(Transform root, Dictionary<string, Sprite> sprites, MenuUI view,
                         List<string> wired, List<string> dead)
        {
            RectTransform block = Panel("League", root);
            float k = _readoutScale;

            Vector2 badge = Size("menu/rank/silver", 24, 24) * 1.42f;
            RectTransform badgeRt = Make("Badge", block, 64, 52, badge.x, badge.y, 0f, 0f, k);
            Image badgeImage = Picture(badgeRt.gameObject, sprites, "menu/rank/silver", false);

            MenuRankBadge rank = Undo.AddComponent<MenuRankBadge>(badgeRt.gameObject);
            rank.target = badgeImage;
            rank.badges = Badges.Select(b => sprites.TryGetValue(b, out Sprite s) ? s : null)
                                .ToArray();
            sprites.TryGetValue("menu/icon/shield", out Sprite unranked);
            rank.unranked = unranked;
            Wire(view.leagueBadgeChanged, rank.Show, "league badge", wired, dead);

            RectTransform nameRt = Make("League name", block, 64 + badge.x + 12, 50,
                                        420, 40, 0f, 0f, k);
            MenuLabel leagueLabel = LabelOn(nameRt, "SILVER III", "Bold", 34,
                                            TextAnchor.MiddleLeft);
            Wire(view.leagueChanged, leagueLabel.Set, "league name", wired, dead);

            Vector2 track = Size("menu/bar/track", 430, 10);
            RectTransform trackRt = Make("Rank bar", block, 64, 106,
                                         track.x, track.y, 0f, 0f, k);
            Picture(trackRt.gameObject, sprites, "menu/bar/track", true);

            Vector2 fillSize = Size("menu/bar/fill", 430, 10);
            RectTransform fillRt = Make("Fill", trackRt, 0, 0, fillSize.x, fillSize.y, 0f, 0f, k);
            fillRt.pivot = new Vector2(0f, 1f);
            Picture(fillRt.gameObject, sprites, "menu/bar/fill", true);

            MenuBar bar = Undo.AddComponent<MenuBar>(trackRt.gameObject);
            bar.fill = fillRt;
            bar.Fill(bar.preview);
            Wire(view.leagueFilled, bar.Fill, "rank bar", wired, dead);
        }

        void BuildPurse(Transform root, Dictionary<string, Sprite> sprites, MenuUI view,
                        List<string> wired, List<string> dead)
        {
            RectTransform block = Panel("Purse", root);

            // Rows that HUG their contents and hang off the right edge, which is what the
            // sheet's auto-layout does and what a fixed box cannot.
            //
            // The first version gave the number a 320-wide right-aligned box and put the
            // coin a fixed distance left of the BOX. The number is only as wide as the
            // number, so the coin ended up floating a third of the way across the screen
            // from the figure it belongs to. A purse that grows from 240 to 24 000 moves
            // the coin here; it moved nothing there, which is the same bug seen twice.
            RectTransform goldRow = Row(block, "Gold", 64, 54, 10);
            Vector2 coin = Size("menu/icon/coin", 24, 24) * 1.25f;
            Image coinImage = Icon(goldRow, sprites, "menu/icon/coin", coin);
            coinImage.color = Gold;

            MenuLabel gold = LabelIn(goldRow, "Gold", "1 240", 30, true);
            gold.target.color = Gold;
            Wire(view.goldChanged, gold.Set, "gold", wired, dead);

            RectTransform dailyRow = Row(block, "Contracts", 64, 104, 10);
            Vector2 scroll = Size("menu/icon/contract", 24, 24) * 1.1f;
            Icon(dailyRow, sprites, "menu/icon/contract", scroll);

            MenuLabel daily = LabelIn(dailyRow, "Contracts", "1 / 3", 26, false);
            Wire(view.dailiesChanged, daily.Set, "contracts", wired, dead);
        }

        /// <summary>The one coloured thing on the screen besides PLAY.</summary>
        static readonly Color Gold = new Color(0.91f, 0.64f, 0.24f);

        /// <summary>Whatever was dropped in, or the one font that is always there.</summary>
        Font Face => _font != null
            ? _font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        /// <summary>
        /// A row pinned to the right edge that is exactly as wide as what is in it.
        ///
        /// uGUI's own layout rather than arithmetic, because the width of a line of text
        /// is not knowable until it is laid out — and the whole reason the first version
        /// put the coin in the wrong place is that it tried to guess it.
        /// </summary>
        RectTransform Row(Transform parent, string name, float fromRight, float fy, float gap)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Build the menu");
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-fromRight, -fy) * _readoutScale;

            HorizontalLayoutGroup row = Undo.AddComponent<HorizontalLayoutGroup>(go);
            row.spacing = gap * _readoutScale;
            row.childAlignment = TextAnchor.MiddleRight;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;

            ContentSizeFitter fit = Undo.AddComponent<ContentSizeFitter>(go);
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rt;
        }

        /// <summary>An icon inside a layout row, held at its own size.</summary>
        Image Icon(Transform row, Dictionary<string, Sprite> sprites, string key, Vector2 size)
        {
            GameObject go = new GameObject(Tail(key), typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Build the menu");
            go.transform.SetParent(row, false);

            LayoutElement fixedSize = Undo.AddComponent<LayoutElement>(go);
            fixedSize.preferredWidth = size.x * _readoutScale;
            fixedSize.preferredHeight = size.y * _readoutScale;

            return Picture(go, sprites, key, false);
        }

        /// <summary>A label inside a layout row; the row decides how wide it ends up.</summary>
        MenuLabel LabelIn(Transform row, string name, string chars, float size, bool bold)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Build the menu");
            go.transform.SetParent(row, false);

            Text t = Undo.AddComponent<Text>(go);
            t.text = chars;
            t.fontSize = Mathf.Max(8, Mathf.RoundToInt(size * _readoutScale));
            t.alignment = TextAnchor.MiddleRight;
            t.raycastTarget = false;
            t.color = new Color(1f, 1f, 1f, 0.92f);
            t.font = Face;
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;

            MenuLabel label = Undo.AddComponent<MenuLabel>(go);
            label.target = t;
            label.placeholder = chars;
            return label;
        }

        void BuildPlate(Transform root, Dictionary<string, Sprite> sprites, MenuUI view,
                        List<string> wired, List<string> dead)
        {
            RectTransform block = Panel("Name plate", root);
            Vector2 plate = Size("menu/plate", 300, 52);

            RectTransform rt = Make("Plate", block, (DesignW - plate.x) * 0.5f,
                                    DesignH - 88 - plate.y, plate.x, plate.y,
                                    0.5f, 1f, _readoutScale);
            Picture(rt.gameObject, sprites, "menu/plate", true);

            RectTransform nameRt = Make("Player name", rt, 0, 0, plate.x, plate.y, 0f, 0f,
                                        _readoutScale);
            MenuLabel label = LabelOn(nameRt, "RUNNER_7", "Semi Bold", 24, TextAnchor.MiddleCenter);
            Wire(view.playerNameChanged, label.Set, "player name", wired, dead);
        }

        void BuildPlay(Transform root, Dictionary<string, Sprite> sprites, MenuUI view,
                       List<string> wired, List<string> dead)
        {
            RectTransform block = Panel("Play", root);
            Vector2 play = Size("menu/button/play", 400, 112);

            // 648 from the top on the sheet, which is 104 up from the bottom edge.
            RectTransform rt = Make("Button / PLAY", block, DesignW - 64 - play.x,
                                    648, play.x, play.y, 1f, 1f, _buttonScale);
            Image face = Picture(rt.gameObject, sprites, "menu/button/play", true);
            // The only coloured thing on the screen, and the one the eye has to find first.
            face.color = new Color(0.91f, 0.64f, 0.24f);
            face.raycastTarget = true;

            RectTransform wordRt = Make("PLAY", rt, 0, 0, play.x, play.y, 0f, 0f, _buttonScale);
            Text word = TextOn(wordRt, "PLAY", "Bold", 52, TextAnchor.MiddleCenter);
            word.color = new Color(0.09f, 0.07f, 0.05f);

            Button button = Undo.AddComponent<Button>(rt.gameObject);
            button.targetGraphic = face;
            UnityEventTools.AddPersistentListener(button.onClick, new UnityAction(view.Play));
            wired.Add("PLAY");

            // Directly under the button, by the sheet's own ten-unit gap.
            RectTransform modeRt = Make("Mode", block, DesignW - 64 - 300, 648 + play.y + 10,
                                        300, 28, 1f, 1f, _buttonScale);
            Text mode = TextOn(modeRt, "MEDIEVAL", "Regular", 20, TextAnchor.MiddleRight);
            mode.color = new Color(1f, 1f, 1f, 0.55f);
        }

        void BuildCorner(Transform root, Dictionary<string, Sprite> sprites, MenuUI view,
                         List<string> wired, List<string> dead)
        {
            RectTransform block = Panel("Corner", root);
            Vector2 round = Size("menu/button/round", 66, 66);

            Round(block, sprites, view, "Button / Settings", "menu/icon/gear",
                  64, round, view.Settings, wired);
            Round(block, sprites, view, "Button / Friends", "menu/icon/people",
                  64 + round.x + 18, round, view.Friends, wired);
        }

        void Round(RectTransform parent, Dictionary<string, Sprite> sprites, MenuUI view,
                   string name, string glyphKey, float x, Vector2 size, UnityAction action,
                   List<string> wired)
        {
            RectTransform rt = Make(name, parent, x, DesignH - 64 - size.y, size.x, size.y,
                                    0f, 1f, _buttonScale);
            Image ring = Picture(rt.gameObject, sprites, "menu/button/round", false);
            ring.raycastTarget = true;

            Vector2 glyph = Size(glyphKey, 24, 24) * 1.15f;
            Vector2 inset = (size - glyph) * 0.5f;
            RectTransform g = Make("Glyph", rt, inset.x, inset.y, glyph.x, glyph.y, 0f, 0f,
                                   _buttonScale);
            Picture(g.gameObject, sprites, glyphKey, false);

            Button button = Undo.AddComponent<Button>(rt.gameObject);
            button.targetGraphic = ring;
            UnityEventTools.AddPersistentListener(button.onClick, action);
            wired.Add(name.Substring(name.LastIndexOf('/') + 1).Trim().ToLowerInvariant());
        }

        // ---------------------------------------------------------------- wiring

        /// <summary>
        /// One readout, wired so the inspector shows it.
        ///
        /// The GENERIC AddPersistentListener, not AddStringPersistentListener and its
        /// siblings. Those take a fixed argument VALUE and are for an event invoked with a
        /// constant -- "call Set with the word Ready" -- which is not what a readout is. A
        /// readout passes its own argument through, and that is the generic overload.
        /// </summary>
        static void Wire<T>(UnityEvent<T> ev, UnityAction<T> call, string what,
                            List<string> wired, List<string> dead)
        {
            if (ev == null || call == null) { dead.Add(what); return; }
            UnityEventTools.AddPersistentListener(ev, call);
            wired.Add(what);
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
                                           typeof(InputSystemUIInputModule));
            Undo.RegisterCreatedObjectUndo(go, "Build the menu");
        }

        // ---------------------------------------------------------------- plumbing

        static RectTransform Panel(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Build the menu");
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        /// <summary>
        /// One rect where the design puts it.
        ///
        /// The sheet is 1920x864 with its origin top-left and the canvas it lands on is at
        /// least that and usually bigger, so a Figma number only means anything as a
        /// distance from an EDGE: anchorX says which side (0 left, 0.5 centre, 1 right) and
        /// vAnchor which end (0 top, 1 bottom). Scaling the offset and the size by the same
        /// number is all a block scale needs, because the offset is already measured from
        /// that corner.
        /// </summary>
        static RectTransform Make(string name, Transform parent, float fx, float fy,
                                  float w, float h, float anchorX, float vAnchor, float k)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Build the menu");
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(anchorX, 1f - vAnchor);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(w, h) * k;
            rt.anchoredPosition = new Vector2(fx - anchorX * DesignW,
                                              vAnchor > 0.5f ? DesignH - fy : -fy) * k;
            return rt;
        }

        Text TextOn(RectTransform rt, string chars, string style, float size, TextAnchor align)
        {
            Text t = Undo.AddComponent<Text>(rt.gameObject);
            t.text = chars;
            t.fontSize = Mathf.Max(8, Mathf.RoundToInt(size * _readoutScale));
            t.alignment = align;
            t.raycastTarget = false;
            t.color = new Color(1f, 1f, 1f, 0.92f);
            t.font = Face;
            t.fontStyle = style == "Bold" || style == "Semi Bold" ? FontStyle.Bold : FontStyle.Normal;
            return t;
        }

        MenuLabel LabelOn(RectTransform rt, string chars, string style, float size,
                          TextAnchor align)
        {
            Text t = TextOn(rt, chars, style, size, align);
            MenuLabel label = Undo.AddComponent<MenuLabel>(rt.gameObject);
            label.target = t;
            label.placeholder = chars;
            return label;
        }

        static Image Picture(GameObject go, Dictionary<string, Sprite> sprites, string key,
                             bool sliced)
        {
            Image image = go.GetComponent<Image>();
            if (image == null) image = Undo.AddComponent<Image>(go);

            if (key != null && sprites.TryGetValue(key, out Sprite s)) image.sprite = s;
            image.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
            image.raycastTarget = false;
            return image;
        }
    }
}
