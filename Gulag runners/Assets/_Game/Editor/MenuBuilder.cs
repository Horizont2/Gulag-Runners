using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        Object _folder;
        bool _folderOpen;
        // The sprites, dragged into named rows. See UiSpriteSet: a folder scan matches
        // filenames, which works on the export it was written against and quietly goes
        // wrong on every one after it.
        UiSpriteSet _set;
        Vector2 _scroll;
        bool _onlyEmpty;
        float _scale = 1f;
        float _readoutScale = 1f;
        float _buttonScale = 1f;
        Font _font;

        readonly Dictionary<string, string> _found = new Dictionary<string, string>();
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

            /// <summary>
            /// What the piece is for, in English, printed under its row. The key is a
            /// filing path, not an explanation: "menu/plate" does not say that it is the
            /// pill behind a number in the top bar, nor that it is nine-sliced and will be
            /// stretched to whatever the number needs.
            /// </summary>
            public string What;

            public Asset(string key, float w, float h, string what)
            { Key = key; W = w; H = h; Border = Vector4.zero; What = what; }

            public Asset(string key, float w, float h, float b, string what)
            { Key = key; W = w; H = h; Border = new Vector4(b, b, b, b); What = what; }
        }

        static readonly Asset[] Needed =
        {
            // 40, where the sheet said 24, and the same 40 as the six league badges
            // below: this is drawn in the badge's own rect when the player is unranked.
            // 24 units is 1.9 mm on a 6.5" phone -- the size of a full stop -- and what
            // the eye gets is smaller again, because every icon in both exports is drawn
            // inset in its file. The sizes here are now what the screen is BUILT at; the
            // layout used to multiply them by 1.42, 1.25, 1.15 and 1.1 at four different
            // call sites, so the window showed 24 and the menu built 34.
            new Asset("menu/icon/shield", 40, 40,
                      "The little icon beside the RATING number in the top bar -- the "
                      + "player's ladder standing. Flat, one colour, and drawn to the "
                      + "edges of its file: the top bar is five of these in a row and they "
                      + "only work as a set."),
            new Asset("menu/icon/coin", 36, 36,
                      "The icon beside the GOLD the player has. It is the number people "
                      + "look at first, so this is the one icon worth making the brightest "
                      + "of the five."),
            new Asset("menu/icon/contract", 42, 42,
                      "The icon beside the daily CONTRACT count -- the jobs the meta hands "
                      + "out each day. A scroll or a sheet; it has to differ from the gear "
                      + "at a glance, since both end up grey."),
            new Asset("menu/icon/gear", 44, 44,
                      "The settings icon in the top bar. The one icon players expect to "
                      + "recognise without learning it, so draw the obvious cog."),
            new Asset("menu/icon/people", 40, 40,
                      "The icon for how many players are online, or for the friends list. "
                      + "Two figures read better than one at this size -- one figure reads "
                      + "as the player themselves."),
            new Asset("menu/rank/bronze", 40, 40,
                      "The league badge for BRONZE, the lowest of the six. These six are "
                      + "one set and the player's whole sense of progress: the shape should "
                      + "stay and only the metal change, so the climb reads as the same "
                      + "badge getting better."),
            new Asset("menu/rank/silver", 40, 40,
                      "The badge for SILVER, second of the six. Same silhouette as bronze, "
                      + "different metal -- if the shapes differ, the player cannot tell a "
                      + "promotion from a different icon."),
            new Asset("menu/rank/gold", 40, 40,
                      "The badge for GOLD, third of six. The middle of the ladder, where "
                      + "most players live, so this is the one seen most often."),
            new Asset("menu/rank/platinum", 40, 40,
                      "The badge for PLATINUM, fourth of six. Cooler and brighter than "
                      + "gold; it is worth a visible jump, because this is where the ladder "
                      + "stops being everybody."),
            new Asset("menu/rank/diamond", 40, 40,
                      "The badge for DIAMOND, fifth of six. The last badge with a colour "
                      + "name, so it can afford to be the showiest of the metals."),
            new Asset("menu/rank/runner", 40, 40,
                      "The badge for RUNNER, the top league and the game's own name for the "
                      + "best. Not a metal: this one is allowed to break the set, because "
                      + "reaching it should not look like more of the same."),
            new Asset("menu/bar/track", 430, 10, 5,
                      "The empty groove of the progress bar under the rating -- how far "
                      + "through the current league the player is. Nine-sliced across its "
                      + "length: ends distinct, middle repeatable. It never moves; the fill "
                      + "does."),
            new Asset("menu/bar/fill", 430, 10, 5,
                      "The part of that bar that is FULL, drawn inside the track. One flat "
                      + "run, nine-sliced the same way -- the builder shortens it from the "
                      + "right, so anything patterned along it gets cut rather than "
                      + "squashed."),
            new Asset("menu/plate", 300, 52, 26,
                      "The pill behind a number or a word in the top bar -- the dark rounded "
                      + "slab the icon and the text sit on. Nine-sliced with a 26 px border, "
                      + "because the same sprite is stretched behind \"4\" and behind "
                      + "\"12,480\"."),
            // 116 tall, not 112: docs/23 wants 9 mm under a thumb and 112 units is 8.8 mm.
            //
            // Four units, and they do more than four units of work. The rect is sized
            // from the sprite unless the sprite disagrees with this row by more than a
            // quarter, and PlayBackground.png is 580x204 -- 2.84:1 against 3.45:1. At 112
            // that was a 26% disagreement, so the sheet won and the art was squashed into
            // 112; at 116 it is 21%, so the ART wins and the button is built at its own
            // shape, 400x141. 11 mm of amber, undistorted, which is what the biggest
            // button in the game should have been all along.
            new Asset("menu/button/play", 400, 116, 14,
                      "The big PLAY button at the bottom: the one thing on the screen the "
                      + "player is meant to hit. Nine-sliced, drawn in its normal state -- "
                      + "the builder tints it for pressed and disabled, so do not bake a "
                      + "highlight in."),
            // 116, where the sheet said 66. 66 units is 5.2 mm: a target a thumb misses,
            // on settings and friends. 116 is this game's smallest round control, the
            // same number the HUD's smallest ring and the search screen's cancel now
            // carry, so the three read as one size rather than three guesses.
            new Asset("menu/button/round", 116, 116,
                      "The round button face the secondary actions use -- settings, "
                      + "friends, back. One sprite for all of them, with the glyph coming "
                      + "from the icon rows above, so draw the ring and leave the middle "
                      + "clear."),
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
            List<string> keys = Keys();
            _set = UiSpriteRows.Field(_set, "Menu Sprites", keys);
            if (_set == null) return;

            UiSpriteRows.Rows(_set, keys, Sizes(), Notes(), ref _scroll,
                              ref _onlyEmpty, 300f);

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
                new GUIContent("Readouts",
                               "The top bar as a whole: the league badge, the rank bar, "
                               + "gold, contracts and the player's name. 1 is the size "
                               + "they were drawn at on the 1920-wide sheet; below about "
                               + "0.8 the numbers stop being readable at arm's length, "
                               + "which is where a phone is held."),
                _readoutScale, 0.5f, 1.4f);
            _buttonScale = EditorGUILayout.Slider(
                new GUIContent("Buttons",
                               "PLAY and the two round ones. A thumb target wants 9 mm; " +
                               "PLAY at 112 units tall is about 8 mm on a 6.5\" phone, so " +
                               "there is not much room below 1 here."),
                _buttonScale, 0.5f, 1.4f);

            EditorGUILayout.Space();
            int have = _set.Filled(keys);
            EditorGUILayout.HelpBox(
                have == keys.Count
                    ? $"All {keys.Count} pieces are in."
                    : $"{have} of {keys.Count} filled in. The screen still builds and leaves " +
                      "the rest blank.",
                have == keys.Count ? MessageType.Info : MessageType.Warning);

            if (GUILayout.Button("Build the menu screen", GUILayout.Height(32))) Build();

            EditorGUILayout.Space();
            UiSpriteRows.FolderFill(_set, keys, ref _folderOpen, ref _folder);
        }

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

        /// <summary>And what each one is for, in English, under its row.</summary>
        static List<string> Notes()
        {
            List<string> notes = new List<string>(Needed.Length);
            foreach (Asset a in Needed) notes.Add(a.What);
            return notes;
        }

        /// <summary>The paths behind the dragged sprites: what the import and the build read.</summary>
        void Gather()
        {
            _found.Clear();
            if (_set == null) return;
            foreach (Asset a in Needed)
            {
                Sprite s = _set.Get(a.Key);
                if (s == null) continue;
                string path = AssetDatabase.GetAssetPath(s);
                if (!string.IsNullOrEmpty(path)) _found[a.Key] = path;
            }
            _scale = MeasureScale();
        }

        // ---------------------------------------------------------------- import

        /// <summary>
        /// What multiple of its design size this one file came out at.
        ///
        /// From the WIDTH, because width is what every layout here anchors on, and a rect
        /// whose width is wrong is a rect in the wrong place as well as the wrong size.
        /// </summary>
        static float Ratio(Asset asset, Sprite sprite)
        {
            if (asset.W <= 0f || sprite == null) return 1f;
            return Mathf.Max(0.01f, sprite.rect.width / asset.W);
        }

        /// <summary>The same question before the sprite exists, straight off the texture.</summary>
        static float Ratio(Asset asset, Texture2D texture)
        {
            if (asset.W <= 0f || texture == null) return 1f;
            return Mathf.Max(0.01f, texture.width / asset.W);
        }

        /// <summary>
        /// The import settings from HudBuilder, applied for you.
        ///
        /// Mip maps and compression are both off on purpose so thin lines stay crisp,
        /// and pixels-per-unit is set from the measured export scale so nine-slice
        /// borders draw at their designed size.
        /// </summary>
        Sprite Import(Asset asset)
        {
            if (!_found.TryGetValue(asset.Key, out string path)) return null;

            TextureImporter imp = (TextureImporter)AssetImporter.GetAtPath(path);
            if (imp == null) return null;

            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            // Never rounded to a power of two. A 1600x448 button stretched to 2048x512
            // is not the art that was drawn, and it is the same setting that makes the
            // export scale below unmeasurable.
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
            // against the canvas's own 100 that divisor is 1, so a 5 px corner exported
            // at 4x draws as a 20 UNIT corner -- which is what made the HUD look heavy
            // for several passes before anybody found it.
            // Per sprite, not per set: a set-wide multiple is wrong for
            // most files in a project wearing two exports, and wrong here is a
            // nine-slice corner drawn at three times its size or a third of it.
            float at = Ratio(asset, AssetDatabase.LoadAssetAtPath<Texture2D>(path));
            imp.spritePixelsPerUnit = 100f * at;

            if (asset.Border != Vector4.zero) imp.spriteBorder = asset.Border * at;

            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>
        /// At what multiple of the design this folder was exported.
        ///
        /// One number for the whole export, taken as the one the most assets AGREE on
        /// rather than averaged: one frame Figma cropped tight to its stroke drags a mean
        /// off, and surviving exactly that is what this exists for.
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

        /// <summary>The short leaf name of an asset key (e.g. "menu/icon/coin" -> "coin").</summary>
        static string Tail(string key) =>
            string.IsNullOrEmpty(key) ? string.Empty : key.Substring(key.LastIndexOf('/') + 1);

        // ---------------------------------------------------------------- the build

        void Build()
        {
            Gather();
            _size.Clear();
            Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
            List<string> odd = new List<string>();

            foreach (Asset a in Needed)
            {
                Sprite s = Import(a);
                if (s == null) continue;
                sprites[a.Key] = s;
                // PER SPRITE, which is the whole of "some elements came out very small,
                // or in bad places": this used to divide every file by ONE measured
                // multiple, and the project now wears two exports at once -- the older at a
                // flat 4x, the newer white one at anything from 1.27x to 4.5x. Divided by
                // the wrong number a file lands at the wrong WIDTH, and the PLAY button
                // came out 185 units wide where the sheet says 400.
                float own = Ratio(a, s);
                Vector2 native = new Vector2(s.rect.width, s.rect.height) / own;

                // The width is then the design width by construction, so a disagreement
                // can only be in the height -- and there the sheet wins, because the layout
                // has rows underneath it. A squashed plate is a thing somebody can see and
                // redraw; a menu that has quietly eaten its own next row is not.
                bool disagrees = a.H > 0f && Mathf.Abs(native.y - a.H) > a.H * 0.25f;
                _size[a.Key] = disagrees ? new Vector2(a.W, a.H) : native;
                if (disagrees)
                    odd.Add($"{a.Key} is drawn {s.rect.width:0}x{s.rect.height:0} and the " +
                            $"row is {a.W:0}x{a.H:0}, so it is squashed into the sheet's " +
                            "rect. Redraw it to the row's shape, or change the row's " +
                            "design size to the art's");
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

            // Made if the scene has none, for the same reason the progress below is: a
            // null here is the quietest failure the menu has. PLAY calls Play(), Play()
            // hands over to the search, and the search's Loading() takes its
            // director == null branch and loads nothing. Every animation runs and the
            // game never begins. A MenuDirector has no required references -- it finds
            // its own fighter and camera and the gate is optional -- so there is nothing
            // to stop this supplying one.
            //
            // Same object name MenuSceneSetup uses, so running that afterwards fills in
            // this one's gate rather than making a second director.
            if (view.director == null)
            {
                view.director = Object.FindFirstObjectByType<MenuDirector>();
                if (view.director == null)
                {
                    GameObject go = GameObject.Find("Menu") ?? new GameObject("Menu");
                    Undo.RegisterCreatedObjectUndo(go, "Build the menu");
                    view.director = Undo.AddComponent<MenuDirector>(go);
                    view.director.view = Camera.main
                                         ?? Object.FindFirstObjectByType<Camera>();
                    wired.Add("a MenuDirector (there was none — set its Gate by hand, or "
                              + "run Setup > Set up the menu scene)");
                }
            }

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

            Vector2 badge = Size("menu/rank/silver", 40, 40);
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
            Vector2 coin = Size("menu/icon/coin", 36, 36);
            Image coinImage = Icon(goldRow, sprites, "menu/icon/coin", coin);
            coinImage.color = Gold;

            MenuLabel gold = LabelIn(goldRow, "Gold", "1 240", 30, true);
            gold.target.color = Gold;
            Wire(view.goldChanged, gold.Set, "gold", wired, dead);

            RectTransform dailyRow = Row(block, "Contracts", 64, 104, 10);
            Vector2 scroll = Size("menu/icon/contract", 42, 42);
            Icon(dailyRow, sprites, "menu/icon/contract", scroll);

            MenuLabel daily = LabelIn(dailyRow, "Contracts", "1 / 3", 26, false);
            Wire(view.dailiesChanged, daily.Set, "contracts", wired, dead);
        }

        /// <summary>The one coloured thing on the screen besides PLAY.</summary>
        // One amber for the whole game, from UiPalette: the menu used to carry its
        // own (0.91, 0.64, 0.24) and the HUD's action glyph a third one.
        static readonly Color Gold = UiPalette.Amber;

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
            t.color = UiPalette.Ink;
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
            MenuLabel label = LabelOn(nameRt, "RUNNER_7", "Semi Bold", 32, TextAnchor.MiddleCenter);
            Wire(view.playerNameChanged, label.Set, "player name", wired, dead);
        }

        void BuildPlay(Transform root, Dictionary<string, Sprite> sprites, MenuUI view,
                       List<string> wired, List<string> dead)
        {
            RectTransform block = Panel("Play", root);
            Vector2 play = Size("menu/button/play", 400, 116);

            // 648 from the top on the sheet, which is 104 up from the bottom edge.
            RectTransform rt = Make("Button / PLAY", block, DesignW - 64 - play.x,
                                    648, play.x, play.y, 1f, 1f, _buttonScale);
            Image face = Picture(rt.gameObject, sprites, "menu/button/play", true);
            // The only coloured thing on the screen, and the one the eye has to find first.
            face.color = UiPalette.Amber;
            face.raycastTarget = true;

            RectTransform wordRt = Make("PLAY", rt, 0, 0, play.x, play.y, 0f, 0f, _buttonScale);
            Text word = TextOn(wordRt, "PLAY", "Bold", 52, TextAnchor.MiddleCenter);
            word.color = UiPalette.InkOnAmber;

            Button button = Undo.AddComponent<Button>(rt.gameObject);
            button.targetGraphic = face;
            UnityEventTools.AddPersistentListener(button.onClick, new UnityAction(view.Play));
            wired.Add("PLAY");

            // Directly under the button, by the sheet's own ten-unit gap.
            RectTransform modeRt = Make("Mode", block, DesignW - 64 - 300, 648 + play.y + 10,
                                        300, 32, 1f, 1f, _buttonScale);
            Text mode = TextOn(modeRt, "MEDIEVAL", "Regular", 24, TextAnchor.MiddleRight);
            mode.color = UiPalette.InkSoft;
        }

        void BuildCorner(Transform root, Dictionary<string, Sprite> sprites, MenuUI view,
                         List<string> wired, List<string> dead)
        {
            RectTransform block = Panel("Corner", root);
            Vector2 round = Size("menu/button/round", 116, 116);

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

            Vector2 glyph = Size(glyphKey, 44, 44);
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
            t.color = UiPalette.Ink;
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
            image.preserveAspect = false;
            image.raycastTarget = false;
            return image;
        }
    }
}
