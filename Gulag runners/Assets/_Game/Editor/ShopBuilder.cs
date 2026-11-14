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
using GulagRunners.Meta;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Builds the shop, which is also the inventory.
    ///
    /// THE LAYOUT, which is the one that was asked for and the one docs/23 argues for: the
    /// fighter stands on the LEFT at full height -- he is already in the menu scene, this
    /// screen just leaves him the room and dims everything else -- the list is a column on
    /// the RIGHT with its six categories above it, and ONE button sits at the bottom right
    /// of that column, under the thumb that is already holding the phone.
    ///
    /// One button, not two. At any moment exactly one of BUY, EQUIP and WORN is true
    /// (Wardrobe.Verb), so a screen with two buttons is a screen showing one dead one. And
    /// one tap on a row TRIES IT ON: the model on the left changes and nothing is spent,
    /// which is the entire reason a fighter is standing on a shop screen.
    ///
    /// Everything else is what the other four builders cost to learn and is not learnt
    /// again here: one sprite set for the whole game, each row sized from its own sprite
    /// with the import written before anything is placed, every rect measured in
    /// millimetres on a 6.5" phone (docs/23: 9 mm under a thumb, 2.5 mm to be read), the
    /// colours from UiPalette rather than from literals, and every piece DRAWN when its
    /// row is empty so the screen stands up before any art exists.
    /// </summary>
    public sealed class ShopBuilder : EditorWindow
    {
        const float DesignW = 1920f;
        const float DesignH = 864f;
        const string RootName = "Shop UI";

        // The column on the right: where it starts, how wide, and the gutter around it.
        const float ColumnX = 1060f;
        const float ColumnW = 1920f - ColumnX - 64f;
        const float Gutter = 24f;

        Object _folder;
        bool _folderOpen;
        Font _font;
        float _textScale = 1f;
        UiSpriteSet _set;
        CosmeticCatalogue _catalogue;
        Vector2 _scroll;
        bool _onlyEmpty;

        readonly Dictionary<string, string> _found = new Dictionary<string, string>();
        readonly Dictionary<string, Vector2> _size = new Dictionary<string, Vector2>();

        [MenuItem("Tools/Gulag Runners/Build the shop screen")]
        public static void Open()
        {
            ShopBuilder w = GetWindow<ShopBuilder>(true, "Build the shop screen");
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
            new Asset("shop/scrim", 8, 864,
                      "The wash that pushes the menu behind this screen. One column of "
                      + "pixels stretched across the width, so only the horizontal fade "
                      + "matters -- it is darkest under the list on the right and clears "
                      + "towards the fighter on the left, because he is the thing being "
                      + "sold and must not be dimmed."),
            new Asset("shop/panel", 760, 700, 40,
                      "The plate the list stands on, down the right-hand side. "
                      + "Nine-sliced with a 40 px corner; it stretches to whatever height "
                      + "the categories leave. Quiet: everything readable on this screen "
                      + "sits ON it."),
            new Asset("shop/tab", 116, 116, 24,
                      "One category tab, unselected -- runners, weapons, emotes, graffiti, "
                      + "knockouts, frames. Six of them in a row above the list, and 116 "
                      + "units is 9 mm, the smallest a thumb target gets in this game."),
            new Asset("shop/tab-on", 116, 116, 24,
                      "The same tab, SELECTED. It is a separate row rather than a tint "
                      + "because \"which category am I in\" is the one thing this screen "
                      + "must never be ambiguous about, and a tint is ambiguous on a "
                      + "phone in sunlight."),
            new Asset("shop/row", 700, 132, 28,
                      "One item in the list: the strip an icon, a name and a price sit on. "
                      + "132 units tall is 10.3 mm -- over the thumb floor, because the "
                      + "whole row is the tap target."),
            new Asset("shop/row-on", 700, 132, 28,
                      "The same strip for the item currently being tried on. Again a row "
                      + "of its own: the player has to see at a glance which of the eight "
                      + "things on screen is the one the button at the bottom will act on."),
            new Asset("shop/slot", 96, 96, 20,
                      "The square the item's picture sits in, at the left of a row. Empty "
                      + "while the art for a cosmetic does not exist yet, which is most of "
                      + "them -- see shop_check for the list."),
            new Asset("shop/button/act", 400, 128, 28,
                      "The one button at the bottom right: BUY, EQUIP or WORN. Drawn in "
                      + "its resting state and tinted by the builder -- amber when it can "
                      + "be pressed, flat ink when it cannot."),
            new Asset("shop/button/close", 116, 116,
                      "The round face of the way out, top right. Same 116 as the menu's "
                      + "round buttons and the search screen's cancel: one round control "
                      + "size in the game."),
            new Asset("shop/icon/close", 60, 60,
                      "The cross on that button. White or near-white: the builder tints "
                      + "it, and a glyph with its own colour fights the tint."),
            new Asset("shop/icon/lock", 56, 56,
                      "The mark on a row that is not owned yet. Small and quiet -- the "
                      + "price is the real statement and this is the footnote."),
        };

        // ---------------------------------------------------------------- window

        void OnGUI()
        {
            List<string> keys = Keys();
            _set = UiSpriteRows.Field(_set, "Shop Sprites", keys);
            EditorGUILayout.LabelField(
                "Every piece is DRAWN if its row is empty, so this screen builds with " +
                "nothing dragged in at all. A row that has something wins.",
                EditorStyles.wordWrappedMiniLabel);

            if (_set != null)
            {
                UiSpriteRows.Rows(_set, keys, Sizes(), Notes(), ref _scroll,
                                  ref _onlyEmpty, 260f);
                UiSpriteRows.FolderFill(_set, keys, ref _folderOpen, ref _folder);
            }

            EditorGUILayout.Space();
            _catalogue = (CosmeticCatalogue)EditorGUILayout.ObjectField(
                new GUIContent("Catalogue",
                               "What is on sale. _Game/Shop/Cosmetics.asset, or your own: " +
                               "the screen reads its categories and its order. Without one " +
                               "the screen still builds and lists nothing."),
                _catalogue, typeof(CosmeticCatalogue), false);

            _font = (Font)EditorGUILayout.ObjectField(
                new GUIContent("Typeface",
                               "Optional. This project ships no font, so without one every " +
                               "label comes out in Unity's built-in Arial."),
                _font, typeof(Font), false);

            _textScale = EditorGUILayout.Slider(
                new GUIContent("Text size",
                               "Every label on this screen at once, against the design " +
                               "sheet. 1 is the size it was laid out at: 32 units is about "
                               + "11 pt on a 6.5\" phone, which is the floor."),
                _textScale, 0.7f, 1.5f);

            EditorGUILayout.Space();
            if (GUILayout.Button("Build the shop screen", GUILayout.Height(32))) Build();

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Built switched OFF, like every other panel in this project: MenuUI.Shop() " +
                "opens it, and nothing is made at play time.", MessageType.None);
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
        /// The import settings a UI sprite needs, written before anything is measured off
        /// it: a sprite that is not a Sprite has no rect, and a nine-slice border quoted
        /// at 1x is wrong by the export's own multiple unless it is scaled by it.
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

        // ---------------------------------------------------------------- the screen

        void Build()
        {
            Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
            Gather(sprites);

            Canvas canvas = Canvas();
            if (canvas == null)
            {
                Debug.LogWarning("No screen-space Canvas in this scene. Open the menu " +
                                 "scene, or run Tools > Gulag Runners > Build the menu " +
                                 "screen first; this hangs off the same canvas.");
                return;
            }

            // Thrown away and rebuilt, so running it twice does not leave two of them.
            Transform old = canvas.transform.Find(RootName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            RectTransform root = Panel(RootName, canvas.transform);
            ShopUI view = Undo.AddComponent<ShopUI>(root.gameObject);
            Undo.RecordObject(view, "Build the shop screen");
            view.catalogue = _catalogue;
            view.progress = Object.FindFirstObjectByType<PlayerProgress>();
            view.fighter = Object.FindFirstObjectByType<MenuFighter>();

            // ---- the wash. Darkest on the right, clear on the left, because the fighter
            // is on the left and he is what is being sold.
            RectTransform scrim = Make("Scrim", root, 0, 0, DesignW, DesignH, 0f, 0f);
            Image wash = Picture(scrim.gameObject, sprites, "shop/scrim");
            wash.color = UiPalette.Fade(UiPalette.Scrim, 0.92f);
            wash.raycastTarget = true;        // and it eats taps meant for the menu behind

            // ---- the plate the list stands on
            Vector2 plate = Size("shop/panel", 760, 700);
            RectTransform column = Make("Column", root, ColumnX, 96, ColumnW, 700, 0f, 0f);
            Picture(column.gameObject, sprites, "shop/panel", true).color = UiPalette.Plate;
            _ = plate;

            // ---- the purse, above the plate on the right
            RectTransform goldRt = Make("Gold", root, ColumnX, 36, ColumnW, 48, 0f, 0f);
            Text gold = TextOn(goldRt, "1 240", 36, TextAnchor.MiddleRight, true);
            gold.color = UiPalette.Amber;
            Wire(view.goldChanged, Label(gold).Set);

            // ---- the six tabs
            Vector2 tab = Size("shop/tab", 116, 116);
            for (int i = 0; i < CosmeticCatalogue.Slots.Length; i++)
            {
                float x = ColumnX + i * (tab.x + 12f);
                RectTransform rt = Make($"Tab / {CosmeticCatalogue.Name(CosmeticCatalogue.Slots[i])}",
                                        column, x - ColumnX, -tab.y - 12f, tab.x, tab.y, 0f, 0f);
                Image face = Picture(rt.gameObject, sprites, i == 0 ? "shop/tab-on" : "shop/tab",
                                     true);
                face.color = i == 0 ? UiPalette.Amber : UiPalette.Fade(UiPalette.Ink, 0.18f);
                face.raycastTarget = true;

                Button button = Undo.AddComponent<Button>(rt.gameObject);
                button.targetGraphic = face;
                int which = i;
                UnityEventTools.AddIntPersistentListener(
                    button.onClick, new UnityAction<int>(view.OpenSlot), which);
            }

            // ---- the category's name and how much of it is owned
            RectTransform titleRt = Make("Category", column, Gutter, Gutter, 420, 44, 0f, 0f);
            Text title = TextOn(titleRt, "RUNNERS", 34, TextAnchor.MiddleLeft, true);
            Wire(view.slotChanged, Label(title).Set);

            RectTransform countRt = Make("Owned", column, ColumnW - Gutter - 220, Gutter,
                                         220, 44, 0f, 0f);
            Text count = TextOn(countRt, "1 / 4", 30, TextAnchor.MiddleRight, false);
            count.color = UiPalette.InkSoft;
            Wire(view.countedChanged, Label(count).Set);

            // ---- the rows. Eight of them, built once and filled by whatever lists the
            // catalogue at run time: a screen that instantiates rows is a screen that
            // allocates while a thumb is moving.
            Vector2 row = Size("shop/row", 700, 132);
            Vector2 slot = Size("shop/slot", 96, 96);
            for (int i = 0; i < 8; i++)
            {
                float y = Gutter + 68f + i * (row.y + 10f);
                RectTransform rt = Make($"Row {i + 1}", column, Gutter, y,
                                        ColumnW - Gutter * 2f, row.y, 0f, 0f);
                Image strip = Picture(rt.gameObject, sprites, i == 0 ? "shop/row-on" : "shop/row",
                                      true);
                strip.color = i == 0 ? UiPalette.Fade(UiPalette.Amber, 0.22f)
                                     : UiPalette.Fade(UiPalette.Ink, 0.08f);
                strip.raycastTarget = true;

                RectTransform icon = Make("Slot", rt, 16, (row.y - slot.y) * 0.5f,
                                          slot.x, slot.y, 0f, 0f);
                Picture(icon.gameObject, sprites, "shop/slot", true).color =
                    UiPalette.Fade(UiPalette.Ink, 0.14f);

                RectTransform nameRt = Make("Name", rt, 16 + slot.x + 20, 26, 360, 44, 0f, 0f);
                TextOn(nameRt, "ASH", 34, TextAnchor.MiddleLeft, true);

                RectTransform priceRt = Make("Price", rt, ColumnW - Gutter * 2f - 240, 26,
                                             220, 44, 0f, 0f);
                Text price = TextOn(priceRt, "800", 32, TextAnchor.MiddleRight, false);
                price.color = UiPalette.Amber;
            }

            // ---- the one button, bottom right of the column
            Vector2 act = Size("shop/button/act", 400, 128);
            RectTransform actRt = Make("Button / Act", column,
                                       ColumnW - Gutter - act.x, 700 - Gutter - act.y,
                                       act.x, act.y, 0f, 0f);
            Image actFace = Picture(actRt.gameObject, sprites, "shop/button/act", true);
            actFace.color = UiPalette.Amber;
            actFace.raycastTarget = true;

            RectTransform verbRt = Make("Verb", actRt, 0, 0, act.x, act.y, 0f, 0f);
            Text verb = TextOn(verbRt, "BUY", 44, TextAnchor.MiddleCenter, true);
            verb.color = UiPalette.InkOnAmber;
            Wire(view.verbChanged, Label(verb).Set);

            Button act_ = Undo.AddComponent<Button>(actRt.gameObject);
            act_.targetGraphic = actFace;
            UnityEventTools.AddPersistentListener(act_.onClick, new UnityAction(view.Act));

            // ---- what just happened, under the button
            RectTransform saidRt = Make("Said", column, Gutter, 700 - Gutter - 44,
                                        ColumnW - Gutter * 2f - act.x - 20, 44, 0f, 0f);
            Text said = TextOn(saidRt, "", 28, TextAnchor.MiddleLeft, false);
            said.color = UiPalette.InkSoft;
            Wire(view.saidChanged, Label(said).Set);

            // ---- the way out
            Vector2 close = Size("shop/button/close", 116, 116);
            RectTransform closeRt = Make("Button / Close", root,
                                         DesignW - 64 - close.x, 36, close.x, close.y, 0f, 0f);
            Image closeFace = Picture(closeRt.gameObject, sprites, "shop/button/close");
            closeFace.color = UiPalette.Fade(UiPalette.Ink, 0.5f);
            closeFace.raycastTarget = true;

            Vector2 cross = Size("shop/icon/close", 60, 60);
            Vector2 inset = (close - cross) * 0.5f;
            RectTransform crossRt = Make("Glyph", closeRt, inset.x, inset.y,
                                         cross.x, cross.y, 0f, 0f);
            Picture(crossRt.gameObject, sprites, "shop/icon/close").color = UiPalette.Ink;

            Button out_ = Undo.AddComponent<Button>(closeRt.gameObject);
            out_.targetGraphic = closeFace;
            MenuUI menu = Object.FindFirstObjectByType<MenuUI>();
            if (menu != null)
                UnityEventTools.AddPersistentListener(out_.onClick,
                                                      new UnityAction(menu.CloseAll));

            EnsureEventSystem();

            // Switched off, because that is the contract every panel in this project
            // keeps: it is in the scene and MenuUI.Shop() opens it.
            root.gameObject.SetActive(false);

            // And the menu is told where it is, or the button that opens it opens nothing.
            if (menu != null)
            {
                Undo.RecordObject(menu, "Build the shop screen");
                menu.shopPanel = root.gameObject;
                EditorUtility.SetDirty(menu);
                PrefabUtility.RecordPrefabInstancePropertyModifications(menu);
            }

            EditorUtility.SetDirty(view);
            Selection.activeGameObject = root.gameObject;

            int dressed = _found.Count;
            Debug.Log($"Shop: built from {dressed} of {Needed.Length} sprites" +
                      (_catalogue != null
                          ? $", listing {_catalogue.items.Length} cosmetic(s)."
                          : ". No catalogue is wired, so the list will be empty.") +
                      (menu != null ? " MenuUI.shopPanel points at it."
                                    : " No MenuUI in this scene, so nothing opens it yet."),
                      root.gameObject);
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
            Undo.RegisterCreatedObjectUndo(go, "Build the shop screen");
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        static RectTransform Make(string name, Transform parent, float fx, float fy,
                                  float w, float h, float anchorX, float vAnchor)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Build the shop screen");
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(anchorX, 1f - vAnchor);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(fx - anchorX * DesignW,
                                              vAnchor > 0.5f ? DesignH - fy : -fy);
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

        Font Face => _font != null
            ? _font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        Text TextOn(RectTransform rt, string chars, float size, TextAnchor align, bool bold)
        {
            Text t = Undo.AddComponent<Text>(rt.gameObject);
            t.text = chars;
            t.fontSize = Mathf.Max(8, Mathf.RoundToInt(size * _textScale));
            t.alignment = align;
            t.raycastTarget = false;
            t.color = UiPalette.Ink;
            t.font = Face;
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        /// <summary>A label a UnityEvent can call, because an event needs a method.</summary>
        static MenuLabel Label(Text text)
        {
            MenuLabel label = text.gameObject.GetComponent<MenuLabel>();
            if (label == null) label = Undo.AddComponent<MenuLabel>(text.gameObject);
            label.target = text;
            label.placeholder = text.text;
            return label;
        }

        static void Wire(UnityEvent<string> raised, UnityAction<string> call)
        {
            if (raised == null) return;
            for (int i = raised.GetPersistentEventCount() - 1; i >= 0; i--)
                UnityEventTools.RemovePersistentListener(raised, i);
            UnityEventTools.AddPersistentListener(raised, call);
        }

        static void EnsureEventSystem()
        {
            EventSystem found = Object.FindFirstObjectByType<EventSystem>();
            if (found != null) return;
            GameObject go = new GameObject("EventSystem", typeof(EventSystem),
                                           typeof(InputSystemUIInputModule));
            Undo.RegisterCreatedObjectUndo(go, "Build the shop screen");
        }
    }
}
