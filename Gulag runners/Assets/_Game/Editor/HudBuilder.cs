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

        // The sprites, dragged into named rows rather than matched to filenames in a
        // folder. UiSpriteSet says why; the short version is that a folder scan is a
        // guessing game that works once and goes quietly wrong afterwards.
        UiSpriteSet _set;
        Object _folder;              // optional, and only ever fills rows that are empty
        bool _folderOpen;
        Vector2 _scroll;
        bool _onlyEmpty;
        readonly Dictionary<string, string> _found = new Dictionary<string, string>();

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
        float _slotScale = 0.9f;
        float _controlScale = 1f;

        // Two pieces of the mock that are off by default now.
        //
        // The mannequin and its leader lines were a REVIEW drawing: they explain which
        // square is which to somebody reading the sheet, and in a fight they are a wire
        // man standing on the arena taking up a fifth of the screen. The squares say what
        // they are themselves now, by carrying the icon of what belongs in them.
        //
        // The off-screen cue is off because its art is a direction, and a direction with
        // nothing to point at is the grey shape above the joystick nobody could explain.
        // Switch it on when there is a reason for it to be there.
        bool _buildMannequin;
        bool _buildSoundCue;

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

            /// <summary>
            /// What this piece IS, in English, shown under its row in the window.
            ///
            /// The key is a path and the path is a filing system, not an explanation:
            /// "hud/bar/fill" does not say whether it is the health bar or the stamina
            /// bar, whether the frame is part of it, which end it empties from, or that
            /// it is stretched rather than nine-sliced. Somebody dragging twenty-eight
            /// sprites in should not have to read this file to find out, and a wrong
            /// guess is a HUD that builds without a word and looks wrong.
            /// </summary>
            public string What;

            public Asset(string key, float w, float h, string what)
            { Key = key; W = w; H = h; Border = Vector4.zero; What = what; }

            public Asset(string key, float w, float h, float b, string what)
            { Key = key; W = w; H = h; Border = new Vector4(b, b, b, b); What = what; }
        }

        static readonly Asset[] Needed =
        {
            new Asset("hud/slot/frame", 64, 64, 12,
                      "The square around ONE equipment slot, for a slot with something in "
                      + "it. Five of these sit along the bottom of the screen: helmet, "
                      + "armour, weapon, belt, utility. Nine-sliced, so draw the corners "
                      + "and leave the middle plain -- it is stretched to whatever the "
                      + "slot size works out to on the device."),
            new Asset("hud/slot/frame-dashed", 64, 64, 12,
                      "The same square for an EMPTY slot, which is the only thing on the "
                      + "column that says \"a belt would go here\" rather than \"this is a "
                      + "belt\". Same size and same borders as the solid one; draw it as a "
                      + "dashed or broken outline. Leave it empty and empty slots keep the "
                      + "solid frame, which reads as a slot that is filled."),
            // 160, not the 88 this row carried while BuildColumn asked for 160: the window
            // showed one number and an empty row built the other, and the generated glow
            // is drawn at whatever this says. A halo 88 across a 64 square is a rim.
            new Asset("hud/slot/glow", 160, 160,
                      "A soft halo BEHIND an equipment square, bigger than the square. Its "
                      + "alpha is driven by the item's rarity, so draw it white and let the "
                      + "tint do the colour: a plain blurred blob, no edge, nothing readable "
                      + "in it. Optional -- without it rarity shows only in the frame tint."),
            // 44, where the sheet said 18. 18 units is 1.4 mm on a 6.5" phone and the art
            // only draws on half of its own width, so the arrow itself came out 0.7 mm --
            // a grey speck on the one hint that a pickup COSTS you something.
            new Asset("hud/slot/swap-arrow", 44, 44,
                      "The arrow that appears on a slot when what you are standing on "
                      + "would REPLACE what is in it. It is the only hint that picking a "
                      + "thing up costs you the thing you are carrying, so it hangs off "
                      + "the corner of the square like a badge: an arrow, not an icon with "
                      + "detail in it, and drawn to the edges of its file -- a margin "
                      + "inside the PNG comes straight off the size it reads at."),
            new Asset("hud/bar/frame", 440, 32, 8,
                      "The empty health bar: the socket the fill sits in, drawn for BOTH "
                      + "fighters (the opponent's is the mirror of yours, flipped by the "
                      + "builder). Nine-sliced along its length, so keep the ends distinct "
                      + "and the middle repeatable. It does not move or empty -- that is "
                      + "the fill."),
            new Asset("hud/bar/fill", 424, 16, 4,
                      "The red that drains out of the health bar. Slightly smaller than the "
                      + "frame because it sits INSIDE it, and drawn as one flat run: the "
                      + "builder shortens it from the right by setting the rect, so any "
                      + "pattern along it will be cut off rather than squashed."),
            // 36, where the sheet said 20. A lamp 1.6 mm across is read at a glance by
            // nobody, and these are read at a glance or not at all: they are the only
            // thing on screen that says how much stamina is left.
            new Asset("hud/pip", 36, 36,
                      "One round counter -- the little lamp that lights up for each round a "
                      + "fighter has won, and the same mark again for each point of "
                      + "stamina, in a row between the two health bars. Drawn LIT and "
                      + "filling its own file; the builder dims it for a round nobody has "
                      + "won yet, so do not draw an empty version."),
            new Asset("hud/button/ring-154", 154, 154,
                      "The biggest round button face, 154 px at 1x: the attack stick's ring "
                      + "on the right of the screen. Just the ring and whatever sits behind "
                      + "it -- the glyph is a separate row -- and transparent in the middle, "
                      + "because the thumb has to see where it is pushing."),
            new Asset("hud/button/ring-130", 130, 130,
                      "The middle round button face, for block and dodge. Same drawing as "
                      + "the big ring, one size down; the builder picks which ring each "
                      + "button gets, so these three are sizes rather than roles."),
            // 116, not 101: docs/23 wants 9 mm under a thumb and 101 units is 7.9 mm. The
            // name is the row's, not a measurement -- the three rings stay a set of three
            // sizes, and this is still the smallest of them.
            new Asset("hud/button/ring-101", 116, 116,
                      "The smallest round button face: jump and the action button. Same "
                      + "family again -- if the three rings do not look like one set, the "
                      + "controls read as three different games. 116 units is the smallest "
                      + "a thumb target gets in this game: 9 mm on a 6.5\" phone."),
            new Asset("hud/button/glyph-block", 48, 48,
                      "The symbol drawn ON the block button -- a shield, a guard, whatever "
                      + "the art says blocking is. White or near-white: the builder tints "
                      + "it, and a glyph with its own colour fights the tint. No ring in "
                      + "it, that is the row above."),
            new Asset("hud/button/glyph-action", 44, 44,
                      "The symbol on the ACTION button, which is the one hand you use for "
                      + "everything that is not fighting: force a chest, pick a weapon up, "
                      + "grab a ladder. A hand or a grab reads better here than a lock or a "
                      + "chest, because the button does all three."),
            new Asset("hud/button/glyph-jump", 46, 46,
                      "The symbol on the jump button. Up is the whole message; an arrow or "
                      + "a boot both work, as long as it is the loudest glyph of the three "
                      + "-- jump is the button a player hits in a panic."),
            new Asset("hud/joystick/ring", 200, 200,
                      "The movement stick's base, bottom-left: the circle the thumb drags "
                      + "inside. Faint on purpose -- it is under a thumb most of the match "
                      + "and never looked at. Transparent middle, so the knob reads against "
                      + "the arena rather than against a disc."),
            new Asset("hud/joystick/knob", 68, 68,
                      "The part of the movement stick that MOVES: the smaller disc the "
                      + "builder slides inside the ring. It should be the brighter of the "
                      + "two, because its offset from the middle is the only feedback that "
                      + "the stick has read the thumb at all."),
            // 210x101, because the art IS 444x213 and the sheet said 3.39:1. Forced into
            // the sheet's rect a 2.08:1 arc is not a shallower arc, it is a squashed one,
            // and the cue is placed off the joystick rather than off the sheet, so a
            // taller box moves nothing.
            new Asset("hud/sound/arc", 210, 101,
                      "The off-screen cue: a thin arc that appears at the edge of the "
                      + "screen pointing at a noise you cannot see -- a chest the other "
                      + "player is forcing. Draw it pointing RIGHT and fading out at both "
                      + "ends; the builder rotates it. Invisible for most of a match, so "
                      + "keep it quiet enough not to be mistaken for damage."),
            new Asset("hud/sound/source-chest", 46, 46,
                      "The small icon that rides in the middle of that arc and says what "
                      + "the noise WAS -- a chest. It is the difference between \"something "
                      + "over there\" and \"he is opening the gold one\", so it has to be "
                      + "recognisable at 46 px."),
            // 44, where the sheet said 32. These eight are the one place in the HUD that
            // carries DETAIL rather than a shape -- a coif against a great helm, a club
            // against a sword -- and the set is drawn inset in its own files: bandage.png
            // covers 75% of its width and 48% of its height, so a 32-unit square showed
            // 1.9 x 1.2 mm of drawing. At 44 the smallest of them reaches 2.5 mm, which
            // is the least anything on a phone can be and still be told apart.
            new Asset("hud/icon/coif", 44, 44,
                      "The silhouette for the light helmet, drawn in the helmet slot when "
                      + "one is worn. All eight of these icons are read in a glance at arm's "
                      + "length: one shape, flat, no shading, and distinct from the other "
                      + "helmet in OUTLINE rather than in detail."),
            new Asset("hud/icon/great-helm", 44, 44,
                      "The silhouette for the heavy helmet. Its job is to be distinguishable "
                      + "from the coif at a glance -- same slot, better protection -- so "
                      + "make the outline obviously bigger or more closed."),
            new Asset("hud/icon/chainmail", 44, 44,
                      "The silhouette for light body armour, in the armour slot. Against "
                      + "plate, below, the difference should read as weight: a shirt against "
                      + "a cuirass."),
            new Asset("hud/icon/plate", 44, 44,
                      "The silhouette for heavy body armour. The player decides whether to "
                      + "spend a slot on it from this icon alone, so heavier has to LOOK "
                      + "heavier than the chainmail."),
            new Asset("hud/icon/club", 44, 44,
                      "The silhouette for a blunt weapon in the weapon slot. It stands for "
                      + "the whole blunt family -- club, mace, hammer -- so draw the family, "
                      + "not one of them."),
            new Asset("hud/icon/sword", 44, 44,
                      "The silhouette for a bladed weapon, standing for the whole edged "
                      + "family. Blunt against edged is the one weapon distinction the slot "
                      + "makes, so these two icons have to differ at 32 px."),
            new Asset("hud/icon/belt", 44, 44,
                      "The silhouette for the belt slot -- the carried-capacity item. A belt "
                      + "or a pouch; it is the slot people forget they have, so it is worth "
                      + "being the most literal icon of the eight."),
            new Asset("hud/icon/bandage", 44, 44,
                      "The silhouette for the utility slot: a bandage, a flask, a tool. The "
                      + "one consumable icon, so a cross or a wrap reads faster than "
                      + "anything clever."),
            new Asset("hud/mannequin", 150, 330,
                      "The wire figure the equipment squares hang off on the design sheet. "
                      + "It belongs on a design sheet rather than over an arena -- the "
                      + "\"Mannequin + leaders\" toggle below is off for a reason -- so this "
                      + "is only needed if you want the sheet's look in the scene."),
            new Asset("hud/leaders", 115, 350,
                      "The thin lines that run from the mannequin out to the equipment "
                      + "squares, for the left-hand side. Drawn once pointing one way; the "
                      + "row below is the same art for the other side. Only used with the "
                      + "mannequin."),
            new Asset("hud/leaders-mirrored", 115, 350,
                      "The same leader lines for the RIGHT-hand side. A separate row rather "
                      + "than a flip because a mirrored line with an arrow or a cap on it is "
                      + "not the same drawing -- if yours is symmetrical, drag the same file "
                      + "into both."),
        };

        // ---------------------------------------------------------------- window

        void OnGUI()
        {
            List<string> keys = Keys();
            _set = UiSpriteRows.Field(_set, "HUD Sprites", keys);
            if (_set == null) return;

            UiSpriteRows.Rows(_set, keys, Sizes(), Notes(), ref _scroll,
                              ref _onlyEmpty);

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
            _slotScale = EditorGUILayout.Slider(
                new GUIContent("Equipment slots",
                               "The five squares along the bottom of the screen."),
                _slotScale, 0.4f, 1.25f);
            _buildMannequin = EditorGUILayout.Toggle(
                new GUIContent("Mannequin + leaders",
                               "The wire figure and the lines from it to the squares. It " +
                               "belongs on the design sheet, not on the arena."),
                _buildMannequin);
            _buildSoundCue = EditorGUILayout.Toggle(
                new GUIContent("Off-screen cue",
                               "The arc that points at a chest the opponent just forced. " +
                               "Invisible until one is, but it has to be built to appear."),
                _buildSoundCue);
            _controlScale = EditorGUILayout.Slider(
                new GUIContent("Controls",
                               "Joystick and the four buttons. 1 is the designed size, " +
                               "which is about 10 mm on a 6.5\" phone — the smallest a " +
                               "thumb target should be. Going below this trades a fight " +
                               "for a screenshot."),
                _controlScale, 0.4f, 1.25f);

            EditorGUILayout.Space();
            int have = _set.Filled(keys);
            EditorGUILayout.HelpBox(
                have == keys.Count
                    ? $"All {keys.Count} pieces are in. Four of them this tool draws itself " +
                      "if you leave them empty."
                    : $"{have} of {keys.Count} filled in. The HUD still builds: an empty row " +
                      "is an object with no sprite on it, not a failure.",
                have == keys.Count ? MessageType.Info : MessageType.Warning);

            if (GUILayout.Button("Build the HUD in this scene", GUILayout.Height(32)))
                Build();

            EditorGUILayout.Space();
            UiSpriteRows.FolderFill(_set, keys, ref _folderOpen, ref _folder);
            EditorGUILayout.LabelField(
                "Replaces any \"HUD\" object this tool built before. Everything it makes " +
                "is an ordinary GameObject afterwards.", EditorStyles.wordWrappedMiniLabel);
        }

        /// <summary>The keys this screen is made of, in the order the rows are drawn.</summary>
        static List<string> Keys()
        {
            List<string> keys = new List<string>(Needed.Length);
            foreach (Asset a in Needed) keys.Add(a.Key);
            return keys;
        }

        /// <summary>The design size of each, shown beside its row.</summary>
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

        /// <summary>
        /// The paths behind the dragged sprites, which is all the rest of the tool ever
        /// wanted: the import settings are written through the path and the sprite is
        /// loaded back from it.
        /// </summary>
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
        /// <summary>
        /// What multiple of its design size this one file came out at.
        ///
        /// From the WIDTH, because width is what every layout in this project anchors on:
        /// the bars start at 134 and 1346, the controls at 1430 and 1576, and a rect whose
        /// width is wrong is a rect in the wrong place as well as the wrong size.
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

        Sprite Import(Asset asset)
        {
            if (!_found.TryGetValue(asset.Key, out string path)) return null;

            TextureImporter imp = (TextureImporter)AssetImporter.GetAtPath(path);
            if (imp == null) return null;

            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            // Never rounded to a power of two: a sprite is never NPOT-scaled, and a
            // texture that is would be measured as the power of two Unity rounded it
            // to rather than as the file -- which votes the whole sheet to the wrong
            // export scale and stretches the art on its way in.
            imp.npotScale = TextureImporterNPOTScale.None;
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
            // every equipment square, which is most of why the HUD looked heavy.
            //
            // PER SPRITE, from this file's own multiple of the design rather than one
            // voted for the whole set: with two exports in the project at 1.27x to 4.5x,
            // a set-wide number is wrong for most of the files in it, and wrong here means
            // a corner drawn at three times its size or a third of it.
            float at = Ratio(asset, AssetDatabase.LoadAssetAtPath<Texture2D>(path));
            imp.spritePixelsPerUnit = 100f * at;

            // The border is quoted at 1x and the file is whatever scale it exported at,
            // so it is scaled by the export rather than typed in twice.
            if (asset.Border != Vector4.zero) imp.spriteBorder = asset.Border * at;

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
        /// <summary>
        /// The pieces this tool draws itself, because Figma cannot export them usefully.
        ///
        /// The sound arc is here now as well. What came out of the export was a 444x213
        /// bitmap of a shape whose frame is 210x62 -- the vector cropped to its own bounds
        /// at some other scale -- and no rect that is the right SIZE is also the right
        /// SHAPE for it. Drawn here it is an arc, at whatever scale the rest of the export
        /// came out at.
        /// </summary>
        static readonly string[] Generated =
            { "hud/slot/glow", "hud/leaders", "hud/leaders-mirrored", "hud/sound/arc" };

        /// <summary>
        /// Redrawn every build, not only when missing: these files are this tool's output,
        /// in a folder called "generated", and a design size changed up in Needed has to
        /// reach the PNG or it changes nothing at all.
        /// </summary>
        void GenerateMissing(string root, List<string> made)
        {
            foreach (string key in Generated)
            {
                if (!_buildMannequin && key.StartsWith("hud/leaders")) continue;
                if (!_buildSoundCue && key == "hud/sound/arc") continue;

                // Unless somebody has dragged real art into that row. These four are
                // drawn because a Figma export cannot carry them -- a soft glow, a thin
                // arc -- and the moment there IS a file for one, the file wins. Anything
                // else is a tool overwriting the artist.
                Sprite already = _set != null ? _set.Get(key) : null;
                if (already != null &&
                    !AssetDatabase.GetAssetPath(already).Contains("/generated/")) continue;

                Asset asset = Needed.First(a => a.Key == key);
                string dir = $"{root}/generated";
                if (!AssetDatabase.IsValidFolder(dir))
                    AssetDatabase.CreateFolder(root, "generated");

                string file = key.Substring(key.LastIndexOf('/') + 1);
                string path = $"{dir}/{file}.png";
                int at = Mathf.Max(1, Mathf.RoundToInt(_scale));
                Texture2D tex = key == "hud/slot/glow"
                    ? DrawGlow((int)asset.W, (int)asset.H, at)
                    : key == "hud/sound/arc"
                        ? DrawArc((int)asset.W, (int)asset.H, at)
                        : DrawLeaders((int)asset.W, (int)asset.H, at,
                                      key.EndsWith("mirrored"));

                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                _found[key] = path;
                made.Add(file);

                // Into the row as well, so the window shows what was drawn and the next
                // build uses the same file rather than drawing it again blind.
                if (_set != null)
                {
                    Sprite drawn = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                    if (drawn != null) _set.Put(key, drawn);
                }
            }
        }

        /// <summary>
        /// The off-screen cue: a thin arc, fading out at both ends.
        ///
        /// A circular segment whose chord is the full width and whose sagitta is the
        /// height, so it fills the rect it is given at any size. The ends fade over the
        /// outer third of the sweep, which is what stops it reading as a closed shape --
        /// it is a direction, not a dial.
        /// </summary>
        static Texture2D DrawArc(int w, int h, int scale)
        {
            int W = w * scale, H = h * scale;
            Texture2D tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            Color[] px = new Color[W * H];

            float stroke = 3f * scale;
            float a = W * 0.5f;                       // half the chord
            float sag = Mathf.Max(1f, H - stroke * 2f);
            float R = (a * a + sag * sag) / (2f * sag);
            Vector2 centre = new Vector2(W * 0.5f, stroke + R);   // y grows downward
            float sweep = Mathf.Asin(Mathf.Clamp01(a / R));       // half the angle, radians

            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    Vector2 d = new Vector2(x + 0.5f - centre.x, y + 0.5f - centre.y);
                    float r = d.magnitude;

                    // Across the stroke: one pixel of antialiasing on each side.
                    float band = 1f - Mathf.SmoothStep(stroke * 0.5f - 1f,
                                                       stroke * 0.5f + 1f, Mathf.Abs(r - R));

                    // Along it: full in the middle third, gone by the ends.
                    float ang = Mathf.Abs(Mathf.Atan2(d.x, -d.y));
                    float ends = 1f - Mathf.SmoothStep(sweep * 0.45f, sweep, ang);

                    px[y * W + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(band * ends));
                }

            tex.SetPixels(px);
            tex.Apply();
            return tex;
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

            // The square it sits behind is 64; the halo is whatever Needed says, and the
            // falloff is what is left over. Written as fractions so the halo can be made
            // smaller in one place -- at 160 it was two and a half times the square it
            // was meant to be haloing, which on a 0.65-scaled column is most of the gap
            // between one slot and the next.
            float half = 32f * scale;              // the 64 px square at the middle
            float radius = 12f * scale;
            float falloff = Mathf.Max(6f, (w * 0.5f - 32f) * 1.25f) * scale;
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

            // The rows, as paths: everything below this line works the way it always did.
            Gather();

            // Measured BEFORE anything is drawn, so the pieces this tool draws itself come
            // out at the same multiple as the ones that were dragged in.
            _scale = MeasureScale();

            // Beside the list itself, because that is the one folder this tool knows about
            // now. The generated sprites are put back INTO the rows, so they appear in the
            // window next to the dragged ones rather than being invisible magic.
            string home = _set != null
                ? System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(_set))
                                 ?.Replace('\\', '/')
                : null;
            if (!string.IsNullOrEmpty(home)) GenerateMissing(home, generated);

            _size.Clear();
            Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
            List<string> odd = new List<string>();
            foreach (Asset a in Needed)
            {
                Sprite s = Import(a);
                if (s == null) continue;
                sprites[a.Key] = s;

                // What multiple of the design THIS FILE came out at, and the rect that
                // follows from it.
                //
                // PER SPRITE, and that is the whole of "some elements came out very small,
                // or in bad places". It used to divide every file by ONE measured multiple
                // -- the value the most files agreed on -- which was true while one export
                // dressed the whole interface and is false now: the project wears two at
                // once, the older one at a flat 4x and the newer white one at anything from
                // 1.27x to 4.5x, because it was drawn to look right rather than to a grid.
                // Divided by the wrong number, a file lands at the wrong WIDTH: the PLAY
                // button came out 185 units wide where the sheet says 400, the health bar
                // 140 where it says 440, each one sitting in a position computed for the
                // size it was supposed to be. Small, and in the wrong place, from one
                // number.
                float own = Ratio(a, s);
                Vector2 native = new Vector2(s.rect.width, s.rect.height) / own;

                // Which leaves the shapes. Measured per sprite the WIDTH is the design
                // width by construction, so a disagreement can only be in the height --
                // and there the sheet wins, because the layout has rows underneath: the
                // white bar frame is 5.6:1 against the row's 13.75:1, and letting the art
                // decide would make it 79 units tall where 32 was planned, swallowing the
                // stamina charges at 80 and the names at 104. A squashed frame is a thing
                // somebody can see and redraw; a HUD that has quietly eaten two of its own
                // rows is not.
                bool disagrees = a.H > 0f && Mathf.Abs(native.y - a.H) > a.H * 0.25f;
                _size[a.Key] = disagrees ? new Vector2(a.W, a.H) : native;
                if (disagrees)
                    odd.Add($"{a.Key} is drawn {s.rect.width:0}x{s.rect.height:0} " +
                            $"({s.rect.width / Mathf.Max(1f, s.rect.height):0.00}:1) and the " +
                            $"row is {a.W:0}x{a.H:0} ({a.W / Mathf.Max(1f, a.H):0.00}:1), so " +
                            "it is drawn into the sheet's rect and squashed. Redraw it to " +
                            "the row's shape, or change the row's design size to the art's");
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
            BuildAimStick(root, sprites, view,
                          out RectTransform aimRing, out RectTransform aimKnob);

            PlayerController[] fighters = Object.FindObjectsByType<PlayerController>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            System.Array.Sort(fighters, (a, b) => a.playerIndex.CompareTo(b.playerIndex));
            if (fighters.Length > 0) view.player1 = fighters[0];
            if (fighters.Length > 1) view.player2 = fighters[1];
            view.matchState = Object.FindFirstObjectByType<MatchState>();

            string input = WireInput(view, ring, knob, aimRing, aimKnob);

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

            view.clock = Label(bar, "Round Clock", "1:24", 840, 30, 240, 50,
                               0.5f, TextAnchor.UpperCenter, 40);

            // The names, outside each bar and pushed against the same edge it is. Which
            // bar is blue already says who you are; the names say who the other one is.
            //
            // 124 from the top, not 104: the stamina row above them is 36 units tall now
            // rather than 20, and a name at 104 would have the last pip drawn through it.
            // The band is the bar at 36, the pips at 80 and the name under both.
            view.name1 = Label(bar, "Name / P1", "RUNNER_1", 134, 124, 320, 30,
                               0f, TextAnchor.UpperLeft, 24);
            view.name2 = Label(bar, "Name / P2", "RUNNER_2", 1920 - 134 - 320, 124, 320, 30,
                               1f, TextAnchor.UpperRight, 24);

            // The series, grouped under the clock rather than scattered along the bars:
            // one place that answers "how does the match stand", with each fighter's
            // marks on their own side of it.
            // 24, not 22, in a box tall enough for it: 22 units is 1.7 mm of type, and a
            // line of Arial at 24 in a 28-unit box is clipped by uGUI rather than wrapped.
            view.roundLabel = Label(bar, "Round Of", "ROUND 1 / 3", 800, 86, 320, 32,
                                    0.5f, TextAnchor.UpperCenter, 24);

            // Three marks wide, because three is the longest a best-of-three runs.
            float pip = Size("hud/pip", 36, 36).x;
            float marks = pip * 3f + 16f;
            view.wins1 = Marks(bar, sprites, "Wins / P1", 800 - 24 - marks, 90, 0.5f);
            view.wins2 = Marks(bar, sprites, "Wins / P2", 1120 + 24, 90, 0.5f);
        }

        /// <summary>One line of text at its design place.</summary>
        Text Label(Transform parent, string name, string text, float x, float y,
                   float w, float h, float anchor, TextAnchor align, float size)
        {
            RectTransform rt = Make(name, parent, x, y, w, h, anchor, 0f, _readoutScale);
            Text label = Undo.AddComponent<Text>(rt.gameObject);
            label.text = text;
            label.fontSize = Mathf.Max(8, Mathf.RoundToInt(size * _readoutScale));
            label.alignment = align;
            label.raycastTarget = false;
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return label;
        }

        /// <summary>
        /// Rounds taken, as a short row of the same mark the stamina uses.
        ///
        /// Built at the longest a series can be rather than at what this match needs, so
        /// changing MatchState.roundsToWin does not need the HUD rebuilding -- HudView
        /// paints the ones past the limit to nothing.
        /// </summary>
        Graphic[] Marks(Transform parent, Dictionary<string, Sprite> sprites, string name,
                        float x, float y, float anchor)
        {
            const int most = 3;
            const float gap = 8f;
            float size = Size("hud/pip", 36, 36).x;

            RectTransform row = Make(name, parent, x, y,
                                     most * size + (most - 1) * gap, size, anchor, 0f,
                                     _readoutScale);
            Graphic[] marks = new Graphic[most];
            for (int i = 0; i < most; i++)
            {
                RectTransform m = Make($"win {i + 1}", row, i * (size + gap), 0,
                                       size, size, 0f, 0f, _readoutScale);
                marks[i] = Picture(m.gameObject, sprites, "hud/pip", false);
            }
            return marks;
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
            float size = Size("hud/pip", 36, 36).x;
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
            RectTransform column = Panel("Equipment", root);
            float k = _slotScale;

            if (_buildMannequin)
            {
                Vector2 manSize = Size("hud/mannequin", 150, 330);
                RectTransform man = Make("Mannequin", column, 150, 180,
                                         manSize.x, manSize.y, 0f, 0f, k);
                Picture(man.gameObject, sprites, "hud/mannequin", false);

                Vector2 leadSize = Size("hud/leaders", 115, 350);
                RectTransform lead = Make("Leader lines", column, 210, 180,
                                          leadSize.x, leadSize.y, 0f, 0f, k);
                Picture(lead.gameObject, sprites, "hud/leaders", false);
            }

            // The icon is what the slot shows while it is EMPTY -- a ghost of the thing
            // that belongs there, which is how a square says "helmet" without a label and
            // without the wire man that used to say it.
            //
            // In the order a fighter is dressed, left to right along the bottom.
            (string name, ItemKind kind, string icon)[] rows =
            {
                ("Slot / Helmet",  ItemKind.Helmet,  "hud/icon/coif"),
                ("Slot / Armour",  ItemKind.Armour,  "hud/icon/chainmail"),
                ("Slot / Weapon",  ItemKind.Weapon,  "hud/icon/club"),
                ("Slot / Belt",    ItemKind.Belt,    "hud/icon/belt"),
                ("Slot / Utility", ItemKind.Utility, "hud/icon/bandage"),
            };

            Vector2 square = Size("hud/slot/frame", 64, 64);
            Vector2 halo = Size("hud/slot/glow", 160, 160);
            Vector2 iconSize = Size("hud/icon/coif", 44, 44);
            Vector2 swapSize = Size("hud/slot/swap-arrow", 44, 44);

            // Along the BOTTOM MIDDLE, not down the left.
            //
            // Down the left they sat on the arena, in the band a side-view fighter spends
            // the whole match running through. The bottom middle is the one part of a
            // landscape phone screen nothing else wants: the stick owns the bottom left,
            // the four buttons the bottom right, and between them is a thumb's width of
            // nothing, directly under where the eyes already are.
            const float gap = 24f;
            float row0 = -(rows.Length * square.x + (rows.Length - 1) * gap) * 0.5f;
            const float fromBottom = 60f;
            float top = DesignH - fromBottom - square.y;

            List<HudSlot> slots = new List<HudSlot>();
            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i];
                // A container with no Image of its own. uGUI draws a parent BEFORE its
                // children, so putting the square on the container would have left the
                // halo — its own child — painted over the top of it.
                RectTransform rt = Make(row.name, column,
                                        DesignW * 0.5f + row0 + i * (square.x + gap), top,
                                        square.x, square.y, 0.5f, 1f, k);

                // Sibling order is draw order: halo behind the square, square behind the
                // item, swap arrow over all of it. The halo is centred on the square
                // whatever the two were exported at.
                Vector2 off = (square - halo) * 0.5f;
                RectTransform glowRt = Make("Glow", rt, off.x, off.y, halo.x, halo.y, 0f, 0f, k);
                Image glow = Picture(glowRt.gameObject, sprites, "hud/slot/glow", false);

                // Every square starts DASHED, because every slot starts empty, and both
                // sprites go on the view so it can swap them as things are picked up.
                // Hard-wiring the dash to the utility row made the difference look like a
                // style rather than a state: four solid squares with nothing in them say
                // the fighter is wearing four invisible things.
                RectTransform frameRt = Make("Frame", rt, 0, 0, square.x, square.y, 0f, 0f, k);
                Image frame = Picture(frameRt.gameObject, sprites, "hud/slot/frame-dashed", true);
                sprites.TryGetValue("hud/slot/frame", out Sprite solid);
                sprites.TryGetValue("hud/slot/frame-dashed", out Sprite dash);

                Vector2 icoOff = (square - iconSize) * 0.5f;
                RectTransform iconRt = Make("Icon", rt, icoOff.x, icoOff.y,
                                            iconSize.x, iconSize.y, 0f, 0f, k);
                Image icon = Picture(iconRt.gameObject, sprites, row.icon, false);

                // A badge on the corner, which means a FRACTION of the arrow hangs off the
                // square rather than a fixed 1.1 of it: at 18 units that formula sat the
                // arrow neatly over the corner and at 44 it walked it into the middle of
                // the square, over the item it is talking about. 0.6 leaves 40% of the
                // arrow outside the frame and still 6 units clear of the next slot.
                RectTransform swapRt = Make("Swap arrow", rt, square.x - swapSize.x * 0.6f,
                                            -swapSize.y * 0.25f, swapSize.x, swapSize.y,
                                            0f, 0f, k);
                Picture(swapRt.gameObject, sprites, "hud/slot/swap-arrow", false);
                swapRt.gameObject.SetActive(false);

                slots.Add(new HudSlot
                {
                    slot = row.kind, frame = frame, glow = glow, icon = icon,
                    filled = solid, dashed = dash, swapArrow = swapRt.gameObject,
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

            view.lineArt = new[]
            {
                Picture(ring.gameObject, sprites, "hud/joystick/ring", false),
                Picture(knob.gameObject, sprites, "hud/joystick/knob", false),
                Existing(root, "Equipment/Mannequin"),
                Existing(root, "Equipment/Leader lines"),
            }.Where(g => g != null).ToArray();

            if (!_buildSoundCue) return;

            RectTransform sound = Panel("Off-screen cue", root);

            Vector2 arcSize = Size("hud/sound/arc", 210, 101);
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
            RectTransform src = Make("Source / chest", sound, 0, 0,
                                     chestSize.x, chestSize.y, 0f, 1f, _readoutScale);
            // Centred over the arc's apex, and a SIBLING of it rather than a child: the
            // arc swings round to point at what happened and a chest lying on its side
            // is not easier to read for it.
            src.anchoredPosition = new Vector2(
                arc.anchoredPosition.x + (arc.sizeDelta.x - src.sizeDelta.x) * 0.5f,
                arc.anchoredPosition.y + src.sizeDelta.y * 0.35f);

            // The arc turns, so it turns about its middle.
            Centred(arc);

            // The cue is NOT line art. Line art is painted white and left on screen; the
            // cue is invisible until the opponent forces a chest, and the only reason it
            // read as an unexplainable grey shape above the joystick is that it was in
            // this list.
            view.cueArc = Picture(arc.gameObject, sprites, "hud/sound/arc", false);
            view.cueIcon = Picture(src.gameObject, sprites, "hud/sound/source-chest", false);
            view.cuePivot = arc;
        }

        void BuildControls(Transform root, Dictionary<string, Sprite> sprites, HudView view)
        {
            RectTransform controls = Panel("Controls", root);

            // Jump and action only. The attack ring is now a STICK, built below, and
            // the block ring is gone entirely: a weapon is stopped by another weapon
            // (docs/31), so there is nothing left for a guard button to do.
            //
            // They move up and left into the space the two dead rings leave, so the
            // right thumb has the whole bottom corner for the thing it actually does.
            view.jump = Button(controls, sprites, "Button / Jump", "hud/button/ring-130",
                               "hud/button/glyph-jump", 1576, 468);
            view.action = Button(controls, sprites, "Button / Action", "hud/button/ring-101",
                                 "hud/button/glyph-action", 1430, 540);

            // The old block ring, doing the one job that survived. docs/02 gave the
            // fighter a single defensive button carrying both the guard and the dodge;
            // the guard is the weapon's job now, so this is purely the dodge -- and the
            // dodge is switched on for the first time since the movement pass.
            view.dodge = Button(controls, sprites, "Button / Dodge", "hud/button/ring-130",
                                "hud/button/glyph-block", 1430, 390);
        }

        /// <summary>
        /// The attack stick: the right thumb, and the whole of the fight.
        ///
        /// Same parts as the movement stick, mirrored into the other corner, and that
        /// symmetry is the point -- a player who has understood the left thumb has
        /// understood this one. It is built the larger of the two (the attack ring's
        /// 154 against the stick's 200 is close enough to read as a pair) because it is
        /// the control that decides matches.
        /// </summary>
        void BuildAimStick(Transform root, Dictionary<string, Sprite> sprites, HudView view,
                           out RectTransform ring, out RectTransform knob)
        {
            RectTransform holder = Panel("Attack stick", root);

            Vector2 ringSize = Size("hud/joystick/ring", 200, 200);
            ring = Centred(Make("Ring", holder, DesignW - 148 - ringSize.x, 648,
                                ringSize.x, ringSize.y, 1f, 1f, _controlScale));

            Vector2 knobSize = Size("hud/joystick/knob", 68, 68);
            knob = Knob("Knob", ring, knobSize, _controlScale);

            view.aim = new HudStick
            {
                ring = Picture(ring.gameObject, sprites, "hud/joystick/ring", false),
                knob = Picture(knob.gameObject, sprites, "hud/joystick/knob", false),
            };
        }

        /// <summary>A ring and a knob, made into something a thumb can push.</summary>
        static VirtualJoystick Stick(RectTransform ring, RectTransform knob)
        {
            if (ring == null) return null;

            VirtualJoystick js = Undo.AddComponent<VirtualJoystick>(ring.gameObject);
            js.knob = knob;
            // Full push when the KNOB reaches the ring, not when the thumb does, so the
            // thing on screen agrees with the number being read.
            js.radius = Mathf.Max(1f, (ring.sizeDelta.x - (knob != null
                                       ? knob.sizeDelta.x : 0f)) * 0.5f);
            js.floating = false;

            Graphic g = ring.GetComponent<Graphic>();
            if (g != null) g.raycastTarget = true;
            return js;
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
        string WireInput(HudView view, RectTransform ring, RectTransform knob,
                         RectTransform aimRing, RectTransform aimKnob)
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

            touch.stick = Stick(ring, knob);
            touch.aimStick = Stick(aimRing, aimKnob);

            // The dodge is the action ring's neighbour now rather than the block ring,
            // because there is no block ring: a weapon is stopped by another weapon.
            touch.dodgeButton = Hit(view.dodge);
            touch.actionButton = Hit(view.action);
            touch.jumpButton = Hit(view.jump);
            EditorUtility.SetDirty(touch);

            // Named one by one, because "wired" is a claim and a list is a receipt. A ring
            // that failed to build leaves a null here, and a null in TouchInputSource is a
            // button that silently does nothing on a phone an hour from now.
            List<string> live = new List<string>();
            List<string> dead = new List<string>();
            (touch.stick != null ? live : dead).Add("move stick");
            (touch.aimStick != null ? live : dead).Add("attack stick");
            (touch.dodgeButton != null ? live : dead).Add("dodge");
            (touch.actionButton != null ? live : dead).Add("action");
            (touch.jumpButton != null ? live : dead).Add("jump");

            string missing = dead.Count == 0 ? ""
                : $"  NOT wired: {string.Join(", ", dead)} — those controls will do nothing.";
            return $"{owner.name} reads: {string.Join(", ", live)}." + missing;
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
