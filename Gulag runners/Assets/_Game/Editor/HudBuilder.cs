using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
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

        // What multiple of the design this export came out at, and the size each sprite
        // therefore IS. Both are measured per build rather than typed in; see MeasureScale.
        float _scale = 1f;
        readonly Dictionary<string, Vector2> _size = new Dictionary<string, Vector2>();

        // How big each cluster is built, against the Figma sheet.
        //
        // The sheet is a MOCK-UP: it was drawn to be read on a monitor at a glance, and
        // the equipment column in particular was drawn large so a reviewer could see what
        // each square meant. On a phone, in a fight, that column is 22% of the width and
        // half the height of the screen, sitting on top of the arena.
        //
        // So the readouts come down and the controls do not. A touch target wants 9 mm
        // (48dp): on a 6.5" landscape phone the 864-unit height is about 65 mm, so 9 mm is
        // 120 units -- the attack ring at 154 is 11.5 mm, jump and block at 130 are 9.8,
        // and shrinking them is how a HUD becomes pretty and unplayable.
        float _readoutScale = 0.85f;
        float _columnScale = 0.65f;
        float _controlScale = 1f;

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

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Size on screen", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Against the Figma sheet, which was drawn to be reviewed on a monitor " +
                "rather than played on a phone. Each cluster keeps the corner it is " +
                "pinned to, so nothing walks off the edge.",
                EditorStyles.wordWrappedMiniLabel);
            _readoutScale = EditorGUILayout.Slider(
                new GUIContent("Readouts", "Health bars, stamina, clock, sound arc."),
                _readoutScale, 0.4f, 1.25f);
            _columnScale = EditorGUILayout.Slider(
                new GUIContent("Equipment column",
                               "Mannequin, leader lines and the five squares. The mock " +
                               "drew these large to be legible in review; at full size " +
                               "they cover a fifth of the arena."),
                _columnScale, 0.4f, 1.25f);
            _controlScale = EditorGUILayout.Slider(
                new GUIContent("Controls",
                               "Joystick and the four buttons. 1 is the designed size, " +
                               "which is about 10 mm on a 6.5\" phone — the smallest a " +
                               "thumb target should be. Going below this trades a fight " +
                               "for a screenshot."),
                _controlScale, 0.4f, 1.25f);

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
                                          .Distinct().OrderBy(p => p).ToArray();

            // A bare tail is only safe when nothing else in the set ends the same way, and
            // two things here do: hud/slot/frame and hud/bar/frame. Matching on the tail
            // alone let bar/frame.png claim the slot square and then be overwritten by the
            // real one, so the bar came out with no frame at all and nothing said why.
            HashSet<string> ambiguous = new HashSet<string>(
                Needed.Select(a => a.Key.Substring(a.Key.LastIndexOf('/') + 1))
                      .GroupBy(t => t).Where(g => g.Count() > 1).Select(g => g.Key));

            Dictionary<string, int> bestScale = new Dictionary<string, int>();
            List<string> unnamed = new List<string>();

            // ---- first pass: the files that say what they are
            foreach (string path in files)
            {
                string name = Normalise(Path.GetFileNameWithoutExtension(path), out int scale);
                string relative = Relative(path, root);

                string key = null;
                foreach (Asset a in Needed)
                {
                    string tail = a.Key.Substring(a.Key.LastIndexOf('/') + 1);
                    string withoutPrefix = a.Key.StartsWith("hud/") ? a.Key.Substring(4) : a.Key;
                    bool byPath = relative == withoutPrefix || relative == a.Key;
                    bool byName = name == tail && !ambiguous.Contains(tail);
                    if (byPath || byName) { key = a.Key; break; }
                }

                if (key == null) { unnamed.Add(path); continue; }
                if (!bestScale.TryGetValue(key, out int had) || scale > had)
                {
                    bestScale[key] = scale;
                    _found[key] = path;
                }
            }

            // ---- second pass: the files Figma named after the vector inside them
            foreach (string path in unnamed)
            {
                Asset? hit = ByShape(path, Relative(path, root));
                if (hit == null) { _unmatched.Add(path); continue; }
                _found[hit.Value.Key] = path;
            }
        }

        static string Relative(string path, string root) =>
            Normalise(Path.ChangeExtension(path.Substring(root.Length).TrimStart('/'), null),
                      out _);

        /// <summary>
        /// A misnamed file worked out from the folder it sits in and the shape it has.
        ///
        /// Only consulted once the names have had their turn. An exported node can be
        /// trimmed to its own ink rather than to the frame it was drawn in — the sound arc
        /// comes out 444x213 where the frame is 210x62 — so proportions alone are not
        /// enough. They settle it when they are close; when they are not, and the folder
        /// has exactly one asset still spoken for by nobody, that is the answer by
        /// elimination. Anything still ambiguous is left alone and reported.
        /// </summary>
        Asset? ByShape(string path, string relative)
        {
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null || tex.height == 0) return null;

            string folder = relative.Contains("/")
                ? relative.Substring(0, relative.LastIndexOf('/')) : "";

            List<Asset> open = new List<Asset>();
            foreach (Asset a in Needed)
            {
                if (_found.ContainsKey(a.Key) || a.H <= 0) continue;
                // The three drawn below are never guessed at. The halo exported as a hard
                // 64 px square — Figma gave the rectangle inside the frame and left the
                // blur behind — and a hard square is the right SHAPE and the wrong asset,
                // so shape matching would take it and quietly replace a soft glow with a
                // block. A correctly NAMED export still wins; only the guess is refused.
                if (Generated.Contains(a.Key)) continue;
                string group = a.Key.StartsWith("hud/") ? a.Key.Substring(4) : a.Key;
                group = group.Contains("/") ? group.Substring(0, group.LastIndexOf('/')) : "";
                if (group == folder) open.Add(a);
            }
            if (open.Count == 0) return null;

            float ratio = tex.width / (float)tex.height;
            Asset? best = null;
            float bestError = 0.06f;
            foreach (Asset a in open)
            {
                float want = a.W / a.H;
                float error = Mathf.Abs(ratio - want) / want;
                if (error >= bestError) continue;
                bestError = error;
                best = a;
            }
            if (best != null) return best;

            return open.Count == 1 ? open[0] : (Asset?)null;
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

            // Pixels per unit, which was being left at the default 100.
            //
            // A nine-slice border is quoted in TEXTURE pixels, and uGUI divides it by
            // spritePPU / canvasReferencePPU before drawing. At 100 against the canvas's
            // own 100 that divisor is 1, so the 12 px corner of a square exported at 4x
            // drew as a 48 UNIT corner -- four times the design, on every bar frame and
            // every equipment square, which is most of why the HUD looked heavy. Set from
            // the export scale it draws at 12, and the sprite's native size becomes its
            // design size into the bargain.
            imp.spritePixelsPerUnit = 100f * Mathf.Max(0.01f, _scale);

            // The border is quoted at 1x and the file is whatever scale it exported at,
            // so it is scaled by the export rather than typed in twice.
            if (asset.Border != Vector4.zero) imp.spriteBorder = asset.Border * _scale;

            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        // ---------------------------------------------------------------- build

        // Three of these cannot survive a Figma export and all three are pure geometry, so
        // they are drawn here instead of fought with. The halo loses its blur the moment
        // Figma exports the rectangle inside the frame rather than the frame; the leader
        // lines come out as whichever single path was selected. Neither is art — one is a
        // distance field and the other is five straight lines — so generating them is both
        // easier and sharper than a 4x raster of someone else's blur.
        static readonly string[] Generated =
            { "hud/slot/glow", "hud/leaders", "hud/leaders-mirrored" };

        void GenerateMissing(string root, List<string> made)
        {
            foreach (string key in Generated)
            {
                if (_found.ContainsKey(key)) continue;
                Asset asset = Needed.First(a => a.Key == key);
                string dir = $"{root}/generated";
                if (!AssetDatabase.IsValidFolder(dir))
                    AssetDatabase.CreateFolder(root, "generated");

                string file = key.Substring(key.LastIndexOf('/') + 1);
                string path = $"{dir}/{file}.png";
                int at = Mathf.Max(1, Mathf.RoundToInt(_scale));
                Texture2D tex = key == "hud/slot/glow"
                    ? DrawGlow((int)asset.W, (int)asset.H, at)
                    : DrawLeaders((int)asset.W, (int)asset.H, at, key.EndsWith("mirrored"));

                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                _found[key] = path;
                made.Add(file);
            }
        }

        /// <summary>
        /// A soft square halo as a distance field, which is what a blur of a rounded
        /// rectangle is anyway — and sharper, because it is computed rather than sampled.
        /// </summary>
        static Texture2D DrawGlow(int w, int h, int scale)
        {
            int W = w * scale, H = h * scale;
            Texture2D tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            Color[] px = new Color[W * H];

            float half = 32f * scale;              // the 64 px square at the middle
            float radius = 12f * scale;
            float falloff = 46f * scale;
            Vector2 centre = new Vector2(W * 0.5f, H * 0.5f);

            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    Vector2 d = new Vector2(Mathf.Abs(x + 0.5f - centre.x),
                                            Mathf.Abs(y + 0.5f - centre.y));
                    Vector2 q = new Vector2(Mathf.Max(d.x - (half - radius), 0f),
                                            Mathf.Max(d.y - (half - radius), 0f));
                    float dist = Mathf.Max(q.magnitude - radius, 0f);
                    float a = Mathf.Exp(-(dist / falloff) * (dist / falloff) * 2.2f);
                    px[y * W + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
                }

            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        /// <summary>
        /// The five leader lines, from the mannequin to the squares, in the 115x350 space
        /// they were designed in. Mirrored is the same five with x flipped, which is what
        /// the left-handed layout needs and what never reached the export at all.
        /// </summary>
        static Texture2D DrawLeaders(int w, int h, int scale, bool mirrored)
        {
            (float x1, float y1, float x2, float y2)[] lines =
            {
                (15, 30, 110, 28), (55, 192, 110, 104), (15, 110, 110, 180),
                (15, 168, 110, 256), (15, 200, 110, 332),
            };

            int W = w * scale, H = h * scale;
            Texture2D tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            Color[] px = new Color[W * H];
            float halfWidth = 0.75f * scale;       // the 1.5 px stroke of the design

            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    // Figma's y runs down the frame and a texture's runs up it.
                    Vector2 p = new Vector2(x + 0.5f, (H - 1 - y) + 0.5f);
                    float best = float.MaxValue;
                    foreach (var l in lines)
                    {
                        float ax = (mirrored ? w - l.x1 : l.x1) * scale;
                        float bx = (mirrored ? w - l.x2 : l.x2) * scale;
                        Vector2 a = new Vector2(ax, (h - l.y1) * scale);
                        Vector2 b = new Vector2(bx, (h - l.y2) * scale);
                        Vector2 ab = b - a;
                        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-5f));
                        best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
                    }
                    float alpha = Mathf.Clamp01(halfWidth + 0.5f - best);
                    px[y * W + x] = new Color(1f, 1f, 1f, alpha);
                }

            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        void Build()
        {
            List<string> generated = new List<string>();
            string sourceFolder = AssetDatabase.GetAssetPath(_folder);

            // Measured BEFORE anything is drawn, so the two sprites this tool draws itself
            // come out at the same multiple as the ones Figma sent.
            _scale = MeasureScale();
            if (!string.IsNullOrEmpty(sourceFolder)) GenerateMissing(sourceFolder, generated);

            _size.Clear();
            Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
            List<string> odd = new List<string>();
            foreach (Asset a in Needed)
            {
                Sprite s = Import(a);
                if (s == null) continue;
                sprites[a.Key] = s;

                // The design size is what the layout was drawn against; the file is what
                // actually turned up. Where the two disagree the FILE wins. Forced into
                // the design rect, a 444x213 export of a 210x62 frame is not a thin arc
                // any more, it is a grey slab -- which is exactly what the sound
                // indicator rendered as. The disagreement is reported, not swallowed.
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
            Undo.RegisterCreatedObjectUndo(rootGo, "Build HUD");

            Canvas canvas = rootGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = rootGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(DesignW, DesignH);
            // EXPAND, not matched on height.
            //
            // The design is a 20:9 sheet. Matched on height, a 16:9 screen gets a canvas
            // 1536 units wide while every number in this file was written against 1920 --
            // so the left column and the right controls slide 192 units towards each
            // other and the HUD is cramped on the commonest screen there is. Expand never
            // lets the canvas be smaller than the design in EITHER direction: the layout
            // stays the layout, and a screen with room to spare simply shows more floor
            // between the two clusters. It only works because every piece below is pinned
            // to the edge it belongs to rather than to the top-left corner.
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            HudView view = Undo.AddComponent<HudView>(rootGo);

            Transform root = rootGo.transform;
            BuildTopBar(root, sprites, view);
            BuildColumn(root, sprites, view);
            BuildSoundAndStick(root, sprites, view, out RectTransform ring,
                               out RectTransform knob);
            BuildControls(root, sprites, view);

            PlayerController[] fighters = Object.FindObjectsByType<PlayerController>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            System.Array.Sort(fighters, (a, b) => a.playerIndex.CompareTo(b.playerIndex));
            if (fighters.Length > 0) view.player1 = fighters[0];
            if (fighters.Length > 1) view.player2 = fighters[1];
            view.matchState = Object.FindFirstObjectByType<MatchState>();

            string input = WireInput(view, ring, knob);

            view.PaintStartingColours();
            EditorUtility.SetDirty(view);
            Selection.activeGameObject = rootGo;

            string drew = generated.Count > 0
                ? $" Drew {generated.Count} Figma could not export cleanly " +
                  $"({string.Join(", ", generated)})."
                : "";
            string wired = fighters.Length >= 2
                ? "Both fighters wired."
                : $"{fighters.Length} fighter(s) in this scene — wire the rest by hand.";
            string sizes = odd.Count > 0
                ? "\nSized from the files rather than the layout, because they disagree:\n  " +
                  string.Join("\n  ", odd) +
                  "\nRe-export those frames at the same scale as the rest to get the " +
                  "designed size back."
                : "";

            Debug.Log($"HUD: built from {sprites.Count} of {Needed.Length} sprites at " +
                      $"{_scale:0.##}x.{drew} " + wired + "\n" + input + sizes +
                      "\nThe clock is a Text with no font if this project has no legacy " +
                      "font; swap it for TMP and drop it back on HudView.Clock.", rootGo);
        }

        /// <summary>
        /// At what multiple of the design this folder was exported.
        ///
        /// One number for the whole export, taken as the one the most assets AGREE on
        /// rather than averaged. An average is dragged off by a single frame Figma cropped
        /// tight to its stroke, and surviving exactly that is the point: of the 29 assets
        /// here, 27 came out at a clean 4x and two did not.
        /// </summary>
        float MeasureScale()
        {
            Dictionary<float, int> votes = new Dictionary<float, int>();
            foreach (Asset a in Needed)
            {
                if (a.W <= 0f || !_found.TryGetValue(a.Key, out string path)) continue;
                Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex == null) continue;

                float v = Mathf.Round(tex.width / a.W * 4f) / 4f;   // to the nearest quarter
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

        /// <summary>The size a sprite actually is, in design units, or the design size.</summary>
        Vector2 Size(string key, float w, float h) =>
            _size.TryGetValue(key, out Vector2 v) ? v : new Vector2(w, h);

        // ---------------------------------------------------------------- pieces

        void BuildTopBar(Transform root, Dictionary<string, Sprite> sprites, HudView view)
        {
            RectTransform bar = Panel("Top Bar", root);

            // Both bars start at the same distance from their own edge of the screen, and
            // each fighter's charges start from the same end as their bar does. The mock
            // had player two's row stopping 104 units short of the end of their bar --
            // which reads, at a glance in a fight, as an opponent already a charge down.
            float barWidth = Size("hud/bar/frame", 440, 32).x;

            view.bar1 = Bar(bar, sprites, "Health / P1", 134, 36, 0f, out RectTransform fill1);
            view.bar1.fillRect = fill1;
            view.bar1.pips = Pips(bar, sprites, "Stamina / P1", 134, 80, 0f);

            view.bar2 = Bar(bar, sprites, "Health / P2", 1346, 36, 1f, out RectTransform fill2);
            view.bar2.fillRect = fill2;
            view.bar2.pips = Pips(bar, sprites, "Stamina / P2", 1346 + barWidth, 80, 1f);

            RectTransform clockRt = Make("Round Clock", bar, 840, 30, 240, 50, 0.5f, 0f,
                                         _readoutScale);
            Text clock = Undo.AddComponent<Text>(clockRt.gameObject);
            clock.text = "1:24";
            clock.fontSize = Mathf.Max(8, Mathf.RoundToInt(40 * _readoutScale));
            clock.alignment = TextAnchor.UpperCenter;
            clock.raycastTarget = false;
            clock.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            view.clock = clock;
        }

        HudBar Bar(Transform parent, Dictionary<string, Sprite> sprites, string name,
                   float x, float y, float anchor, out RectTransform fillRt)
        {
            float k = _readoutScale;
            Vector2 frameSize = Size("hud/bar/frame", 440, 32);
            Vector2 fillSize = Size("hud/bar/fill", 424, 16);
            Vector2 inset = (frameSize - fillSize) * 0.5f;

            RectTransform rt = Make(name, parent, x, y, frameSize.x, frameSize.y, anchor, 0f, k);
            Image frame = Picture(rt.gameObject, sprites, "hud/bar/frame", true);

            fillRt = Make("Fill", rt, inset.x, inset.y, fillSize.x, fillSize.y, 0f, 0f, k);
            // The bar empties from the side it is anchored to, so the two drain towards
            // the middle of the screen rather than both towards the left.
            fillRt.pivot = new Vector2(anchor >= 0.5f ? 1f : 0f, 1f);
            fillRt.anchoredPosition = new Vector2(
                anchor >= 0.5f ? inset.x + fillSize.x : inset.x, -inset.y) * k;
            Image fill = Picture(fillRt.gameObject, sprites, "hud/bar/fill", true);

            return new HudBar { frame = frame, fill = fill };
        }

        Graphic[] Pips(Transform parent, Dictionary<string, Sprite> sprites, string name,
                       float x, float y, float anchor)
        {
            float k = _readoutScale;
            const int count = 4;
            const float gap = 8f;
            float size = Size("hud/pip", 20, 20).x;
            float width = count * size + (count - 1) * gap;

            RectTransform row = Make(name, parent, anchor >= 0.5f ? x - width : x, y,
                                     width, size, anchor, 0f, k);
            Graphic[] pips = new Graphic[count];
            for (int i = 0; i < count; i++)
            {
                RectTransform p = Make($"pip {i + 1}", row, i * (size + gap), 0, size, size, 0f, 0f, k);
                pips[i] = Picture(p.gameObject, sprites, "hud/pip", false);
            }
            return pips;
        }

        void BuildColumn(Transform root, Dictionary<string, Sprite> sprites, HudView view)
        {
            RectTransform column = Panel("Equipment Column", root);
            float k = _columnScale;

            Vector2 manSize = Size("hud/mannequin", 150, 330);
            RectTransform man = Make("Mannequin", column, 150, 180, manSize.x, manSize.y, 0f,
                                     0f, k);
            Picture(man.gameObject, sprites, "hud/mannequin", false);

            Vector2 leadSize = Size("hud/leaders", 115, 350);
            RectTransform lead = Make("Leader lines", column, 210, 180,
                                      leadSize.x, leadSize.y, 0f, 0f, k);
            Picture(lead.gameObject, sprites, "hud/leaders", false);

            (string name, ItemKind kind, string icon, float y, bool empty)[] rows =
            {
                ("Slot / Helmet",  ItemKind.Helmet,  "hud/icon/coif",      176, false),
                ("Slot / Weapon",  ItemKind.Weapon,  "hud/icon/club",      252, false),
                ("Slot / Armour",  ItemKind.Armour,  "hud/icon/chainmail", 328, false),
                ("Slot / Belt",    ItemKind.Belt,    "hud/icon/belt",      404, false),
                ("Slot / Utility", ItemKind.Utility, null,                 480, true),
            };

            Vector2 square = Size("hud/slot/frame", 64, 64);
            Vector2 halo = Size("hud/slot/glow", 160, 160);
            Vector2 iconSize = Size("hud/icon/coif", 32, 32);
            Vector2 swapSize = Size("hud/slot/swap-arrow", 18, 18);

            List<HudSlot> slots = new List<HudSlot>();
            foreach (var row in rows)
            {
                // A container with no Image of its own. uGUI draws a parent BEFORE its
                // children, so putting the square on the container would have left the
                // halo — its own child — painted over the top of it.
                RectTransform rt = Make(row.name, column, 320, row.y, square.x, square.y, 0f, 0f, k);

                // Sibling order is draw order: halo behind the square, square behind the
                // item, swap arrow over all of it. The halo is centred on the square
                // whatever the two were exported at.
                Vector2 off = (square - halo) * 0.5f;
                RectTransform glowRt = Make("Glow", rt, off.x, off.y, halo.x, halo.y, 0f, 0f, k);
                Image glow = Picture(glowRt.gameObject, sprites, "hud/slot/glow", false);

                RectTransform frameRt = Make("Frame", rt, 0, 0, square.x, square.y, 0f, 0f, k);
                Image frame = Picture(frameRt.gameObject, sprites,
                                      row.empty ? "hud/slot/frame-dashed" : "hud/slot/frame", true);

                Vector2 icoOff = (square - iconSize) * 0.5f;
                RectTransform iconRt = Make("Icon", rt, icoOff.x, icoOff.y,
                                            iconSize.x, iconSize.y, 0f, 0f, k);
                Image icon = Picture(iconRt.gameObject, sprites, row.icon, false);

                RectTransform swapRt = Make("Swap arrow", rt, square.x - swapSize.x * 1.1f,
                                            -swapSize.y / 3f, swapSize.x, swapSize.y,
                                            0f, 0f, k);
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

        void BuildSoundAndStick(Transform root, Dictionary<string, Sprite> sprites,
                                HudView view, out RectTransform ring, out RectTransform knob)
        {
            RectTransform stick = Panel("Joystick", root);

            // Pivot in the MIDDLE, unlike everything else here: VirtualJoystick measures
            // the thumb against local zero, and local zero is the pivot. Left top-left,
            // a stick pushed to its centre would read as pushed down and right.
            Vector2 ringSize = Size("hud/joystick/ring", 200, 200);
            ring = Centred(Make("Ring", stick, 148, 648, ringSize.x, ringSize.y, 0f, 1f,
                                _controlScale));

            Vector2 knobSize = Size("hud/joystick/knob", 68, 68);
            knob = Knob("Knob", ring, knobSize, _controlScale);

            RectTransform sound = Panel("Sound Direction", root);

            Vector2 arcSize = Size("hud/sound/arc", 210, 62);
            RectTransform arc = Make("Arc", sound, 140, 572, arcSize.x, arcSize.y, 0f, 1f,
                                     _readoutScale);

            // Then PUT BACK on the stick, because the two are pinned to the same bottom
            // edge and scaled by different numbers: shrink the readouts alone and the arc
            // walks down until it is sitting inside the joystick. Its place belongs to
            // the stick, not to the design sheet — eight units left of it and fourteen
            // above it, which is where the sheet had them.
            float gap = 14f * _readoutScale;
            float ringLeft = ring.anchoredPosition.x - ring.sizeDelta.x * 0.5f;
            float ringTop = ring.anchoredPosition.y + ring.sizeDelta.y * 0.5f;
            arc.anchoredPosition = new Vector2(ringLeft - 8f * _readoutScale,
                                               ringTop + gap + arc.sizeDelta.y);

            // The chest rides ON the arc at the fraction of it the design put it at —
            // 42% along and a fifth of its height above — so the pair keeps its shape
            // whatever scale the two were exported at, which for these two is not the
            // scale everything else came out at.
            Vector2 chestSize = Size("hud/sound/source-chest", 46, 46);
            RectTransform src = Make("Source / chest", arc, arcSize.x * 0.419f,
                                     -arcSize.y * 0.226f, chestSize.x, chestSize.y, 0f,
                                     0f, _readoutScale);

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
                               "hud/button/glyph-jump", 1576, 558);
            view.block = Button(controls, sprites, "Button / Block", "hud/button/ring-130",
                                "hud/button/glyph-block", 1715, 525);
            view.action = Button(controls, sprites, "Button / Action", "hud/button/ring-101",
                                 "hud/button/glyph-action", 1534, 698);
            // Last, so it is drawn on top and wins the raycast where its square overlaps
            // the jump ring's. A thumb that lands between them gets attack, which is the
            // forgiving way round in a fight.
            view.attack = Button(controls, sprites, "Button / Attack", "hud/button/ring-154",
                                 "hud/button/glyph-attack", 1687, 675);
        }

        HudButton Button(Transform parent, Dictionary<string, Sprite> sprites, string name,
                         string ringKey, string glyphKey, float x, float y)
        {
            Vector2 size = Size(ringKey, 130, 130);
            Vector2 glyphSize = Size(glyphKey, 48, 48);

            RectTransform rt = Make(name, parent, x, y, size.x, size.y, 1f, 1f,
                                    _controlScale);
            Image ring = Picture(rt.gameObject, sprites, ringKey, false);

            Vector2 inset = (size - glyphSize) * 0.5f;
            RectTransform g = Make("Glyph", rt, inset.x, inset.y,
                                   glyphSize.x, glyphSize.y, 0f, 0f, _controlScale);
            Image glyph = Picture(g.gameObject, sprites, glyphKey, false);

            return new HudButton { ring = ring, glyph = glyph };
        }

        // ---------------------------------------------------------------- the controls work

        /// <summary>
        /// Makes the four rings and the stick actually DO something.
        ///
        /// Until this existed the HUD was a painting of a HUD: every Image had
        /// raycastTarget off, the scene had no EventSystem, and the TouchInputSource that
        /// both fighter prefabs already carry had all five of its slots empty. No control
        /// could be pressed, even in principle, and nothing said so.
        ///
        /// Nothing new is invented here. VirtualJoystick, VirtualButton and
        /// TouchInputSource are the components of docs/02; this hangs two of them on the
        /// objects that were drawn for them and fills in the third, on the fighter the
        /// HUD is being drawn FOR. Player two's own TouchInputSource is left alone: on a
        /// phone the opponent is on their own device with their own HUD, and in the
        /// editor they are driven by the keyboard half of DeviceInputSource.
        /// </summary>
        string WireInput(HudView view, RectTransform ring, RectTransform knob)
        {
            EnsureEventSystem();

            PlayerController owner = view.localPlayer == 0 ? view.player1 : view.player2;
            if (owner == null)
                return "No local fighter in this scene, so the stick and the buttons are " +
                       "drawn but not read. Drop one on HudView and run this again.";

            TouchInputSource touch = owner.GetComponentInChildren<TouchInputSource>(true);
            if (touch == null)
                return $"{owner.name} has no TouchInputSource. The stick and the buttons " +
                       "are drawn but nothing reads them — add one to the fighter prefab " +
                       "and run this again.";

            Undo.RecordObject(touch, "Build HUD");

            if (ring != null)
            {
                VirtualJoystick js = Undo.AddComponent<VirtualJoystick>(ring.gameObject);
                js.knob = knob;
                // Full push when the KNOB reaches the ring, not when the thumb does, so
                // the thing on screen agrees with the number being read.
                js.radius = Mathf.Max(1f, (ring.sizeDelta.x - (knob != null
                                           ? knob.sizeDelta.x : 0f)) * 0.5f);
                js.floating = false;
                Graphic g = ring.GetComponent<Graphic>();
                if (g != null) g.raycastTarget = true;
                touch.stick = js;
            }

            touch.attackButton = Hit(view.attack);
            // The block ring drives DODGE, and that is not a slip: docs/02 gives the
            // fighter one defensive button, and that one flag is a guard standing still
            // and a dodge with a direction held.
            touch.dodgeButton = Hit(view.block);
            touch.actionButton = Hit(view.action);
            touch.jumpButton = Hit(view.jump);
            EditorUtility.SetDirty(touch);

            return $"Stick and four buttons wired into {owner.name}'s TouchInputSource.";
        }

        static VirtualButton Hit(HudButton button)
        {
            if (button == null || button.ring == null) return null;
            button.ring.raycastTarget = true;
            return Undo.AddComponent<VirtualButton>(button.ring.gameObject);
        }

        /// <summary>
        /// A canvas with no EventSystem in the scene receives no taps at all, and says
        /// nothing about it: the buttons simply never fire. This project is on the Input
        /// System package, so the module has to be its own — StandaloneInputModule reads
        /// the old UnityEngine.Input and throws the moment it is touched.
        /// </summary>
        static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null) return;
            GameObject go = new GameObject("EventSystem", typeof(EventSystem),
                                           typeof(InputSystemUIInputModule));
            Undo.RegisterCreatedObjectUndo(go, "Build HUD");
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
        /// The design is a 1920x864 sheet with its origin top-left; the canvas it lands on
        /// is at least that and usually bigger. So a Figma number only means anything as a
        /// distance from an EDGE: anchorX says which side of the screen the piece belongs
        /// to (0 left, 0.5 centre, 1 right) and vAnchor which end (0 top, 1 bottom), and
        /// the design coordinate becomes a distance from it.
        ///
        /// Everything used to hang off the top. That was invisible while the canvas was
        /// exactly 864 units tall and wrong the moment it was not — the joystick and the
        /// four buttons would float in the middle of a taller screen with a hand's width
        /// of nothing underneath them, which is where the canvas change above puts them.
        ///
        /// Children pass anchorX 0 and vAnchor 0 and are simply offset from their parent's
        /// top-left, which is what the subtraction below degenerates to.
        /// </summary>
        static RectTransform Make(string name, Transform parent, float fx, float fy,
                                  float w, float h, float anchorX, float vAnchor = 0f,
                                  float k = 1f)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Build HUD");
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(anchorX, 1f - vAnchor);
            rt.pivot = new Vector2(0f, 1f);

            // Scaling the offset AND the size by the same number is all a cluster scale
            // needs, whichever corner it is pinned to -- because the offset is already
            // measured from that corner. The far edge of a right-anchored piece sits at
            // -(offset + width) from the screen edge, so scaling both scales that too;
            // the same holds for a bottom-anchored one, and a centred one keeps its
            // distance from the middle. localScale would have done it as well and would
            // have scaled the children twice, since each child is scaled here already.
            rt.sizeDelta = new Vector2(w, h) * k;
            rt.anchoredPosition = new Vector2(fx - anchorX * DesignW,
                                              vAnchor > 0.5f ? DesignH - fy : -fy) * k;
            return rt;
        }

        /// <summary>Moves a top-left rect onto a centre pivot without moving it on screen.</summary>
        static RectTransform Centred(RectTransform rt)
        {
            Vector2 size = rt.sizeDelta;
            Vector2 p = rt.anchoredPosition;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(p.x + size.x * 0.5f, p.y - size.y * 0.5f);
            return rt;
        }

        /// <summary>
        /// The thumb, centred in the ring and a child of it.
        ///
        /// Both matter. VirtualJoystick drives the knob by writing anchoredPosition, so
        /// the knob has to be anchored to the ring's centre or every push is measured from
        /// the wrong place; and the mock had it drawn up and to the right, which is a
        /// stick already being held.
        /// </summary>
        static RectTransform Knob(string name, RectTransform ring, Vector2 size,
                                  float k)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Build HUD");
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(ring, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size * k;
            rt.anchoredPosition = Vector2.zero;
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
            // Art never decides its own shape: every rect is sized from the sprite now, so
            // preserveAspect would only letterbox something already the right shape.
            image.preserveAspect = false;
            // Off by default. WireInput switches it back on for the five things that are
            // actually controls — an Image that eats a tap is a button that stops working
            // the day somebody drags a decoration over it.
            image.raycastTarget = false;
            return image;
        }
    }
}
