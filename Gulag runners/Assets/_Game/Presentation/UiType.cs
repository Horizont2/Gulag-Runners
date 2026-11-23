using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GulagRunners.Game
{
    /// <summary>
    /// Every line of text in the game, in one typeface, at load.
    ///
    /// THE INTERFACE WAS BUILT TWICE AND NOBODY NOTICED. Counted across the five scenes
    /// that ship: 44 labels are UnityEngine.UI.Text, the legacy component, and 24 are
    /// TextMeshProUGUI. Nordic fortress holds both. Which half a label landed in depended
    /// on which builder made it and when.
    ///
    /// That is not a tidiness complaint, it is the bug behind "no text anywhere updates,
    /// not in the loading screen and not in the match". MenuLabel.target is a TMP_Text, and
    /// a legacy Text is not one -- not a subclass, not convertible, nothing. So
    /// GetComponent&lt;TMP_Text&gt;() came back null, Set() returned on its first line, and
    /// the label kept the string it was authored with for the rest of the session. Seven
    /// labels in the menu alone, Phase and Detail among them, which ARE the loading
    /// screen's two lines.
    ///
    /// So this upgrades them, at runtime, on every scene: the legacy component goes, a
    /// TextMeshProUGUI takes its place on the same object with the same words, and anything
    /// pointing at the old one is pointed at the new one.
    ///
    /// WHY AT RUNTIME rather than in the scenes. A legacy Text carries about twenty
    /// serialised fields and a TMP label carries over a hundred, half of them a material's
    /// business; rewriting that by hand in YAML is a lot of ways to break a scene for a
    /// result nobody can see until Unity opens it. Doing it on load costs one pass over the
    /// canvases at scene start, is identical in every scene, and cannot leave a scene file
    /// in a state the editor will not open. It also keeps working for a label built
    /// tomorrow by a tool that has not been updated.
    ///
    /// AND IT IS WHERE THE LOOK COMES FROM. Asked for plainly: it should look good, the way
    /// Worms does. That game's text is not a clever font -- it is weight, a dark outline and
    /// a soft drop under it, so a word stays readable over a bright sky and over a mine
    /// shaft without a panel behind it. All three are TMP material settings and none of
    /// them is available on the legacy component at all, which is the second reason the
    /// upgrade is worth doing rather than just teaching MenuLabel to speak both.
    /// </summary>
    [DisallowMultipleComponent]
    public static class UiType
    {
        /// <summary>
        /// How heavy the outline is, as a share of the glyph. TMP's outline grows INWARD
        /// from the edge, so past about a quarter it starts eating thin strokes; 0.18 is a
        /// firm edge that still leaves the counters of an 'a' open at HUD sizes.
        /// </summary>
        public const float OutlineWidth = 0.18f;

        /// <summary>How far the soft drop sits under the word, in ems.</summary>
        public const float UnderlayOffset = 0.06f;

        /// <summary>And how far it is blurred. Soft enough to read as depth, not as a second copy.</summary>
        public const float UnderlaySoftness = 0.22f;

        /// <summary>
        /// Letters pushed apart a little. Caps set tight look like a word; caps with air in
        /// them look like a label, which is what every string in this interface is.
        /// </summary>
        public const float Tracking = 4f;

        static Material _skin;
        static bool _armed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Begin()
        {
            if (!_armed)
            {
                _armed = true;
                SceneManager.sceneLoaded += (scene, mode) => Upgrade(scene);
            }
            Upgrade(SceneManager.GetActiveScene());
        }

        /// <summary>Every legacy label in one scene, turned into a TMP one.</summary>
        public static int Upgrade(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return 0;

            int done = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                // Inactive included: the result screen and the search screen spend most of
                // their life switched off, and they are exactly the ones being complained
                // about. A label upgraded only when visible is a label upgraded too late.
                foreach (Text old in root.GetComponentsInChildren<Text>(true))
                    if (Swap(old)) done++;
            }
            return done;
        }

        static bool Swap(Text old)
        {
            if (old == null) return false;

            GameObject go = old.gameObject;

            // What the old one was saying and how. Taken before anything is destroyed,
            // because a destroyed component answers null for all of it.
            string words = old.text;
            Color colour = old.color;
            int size = old.fontSize;
            bool bold = (old.fontStyle & FontStyle.Bold) != 0
                        || (old.fontStyle & FontStyle.BoldAndItalic) != 0;
            bool italic = (old.fontStyle & FontStyle.Italic) != 0
                          || (old.fontStyle & FontStyle.BoldAndItalic) != 0;
            TextAnchor anchor = old.alignment;
            bool wrap = old.horizontalOverflow == HorizontalWrapMode.Wrap;
            bool raycast = old.raycastTarget;
            bool on = old.enabled;

            // Best fit is not carried over, because nothing uses it: all 44 legacy labels
            // in the five scenes are m_BestFit: 0. Mapping it would mean inventing the two
            // bounds TMP wants, for a case that does not occur.

            // THE NEW LABEL GETS ITS OWN OBJECT, and the first version of this did not.
            //
            // It put the TextMeshProUGUI on the same GameObject and destroyed the legacy
            // Text beside it, which looks reasonable and is not: a GameObject has ONE
            // CanvasRenderer and a Graphic owns it. Destroy is deferred to the end of the
            // frame, and when the old Text finally goes it clears that CanvasRenderer --
            // wiping the mesh the TMP label had just built. Every upgraded line came out
            // blank and stayed blank, which is exactly how it was reported: the texts
            // still do not update, at least on the search panel.
            //
            // A child of its own has a CanvasRenderer of its own, so nothing is shared and
            // nothing is cleared. It is stretched over its parent, so the words land where
            // the old ones did without copying a single layout number.
            old.enabled = false;
            Object.Destroy(old);

            GameObject holder = new GameObject("Label");
            RectTransform box = holder.AddComponent<RectTransform>();
            box.SetParent(go.transform, false);
            box.anchorMin = Vector2.zero;
            box.anchorMax = Vector2.one;
            box.offsetMin = Vector2.zero;
            box.offsetMax = Vector2.zero;

            TextMeshProUGUI made = holder.AddComponent<TextMeshProUGUI>();
            made.text = words;
            made.color = colour;
            made.fontSize = size;
            made.enableWordWrapping = wrap;
            made.raycastTarget = raycast;
            made.alignment = Align(anchor);
            made.characterSpacing = Tracking;
            made.enabled = on;

            made.fontStyle = (bold ? FontStyles.Bold : FontStyles.Normal)
                             | (italic ? FontStyles.Italic : FontStyles.Normal);

            if (made.font == null) made.font = TMP_Settings.defaultFontAsset;
            if (made.font != null) made.fontSharedMaterial = Skin(made.font.material);

            Repoint(go, made);
            return true;
        }

        /// <summary>
        /// The one material every upgraded label shares: outlined, with a soft drop.
        ///
        /// ONE, not one each. A material per label is a draw call per label and a break in
        /// every batch between them, on a phone, for text that is all the same. It is built
        /// from whatever material the font arrived with, so a project that swaps the font
        /// asset gets its own glyphs rather than this one's.
        /// </summary>
        static Material Skin(Material from)
        {
            if (_skin != null) return _skin;
            if (from == null) return null;

            _skin = new Material(from) { name = from.name + " (gulag)" };

            // Named as strings rather than through TMPro.ShaderUtilities. These are the
            // property names in TMP's own shaders and they are what the inspector writes;
            // the helper class that holds their cached ids is not in every assembly this
            // project compiles against, and a missing field is a worse trade than a
            // missing cache for eight writes that happen once.
            Set(_skin, "_OutlineWidth", OutlineWidth);
            Set(_skin, "_OutlineColor", Edge);
            if (_skin.HasProperty("_OutlineWidth")) _skin.EnableKeyword("OUTLINE_ON");

            Set(_skin, "_UnderlayColor", Drop);
            Set(_skin, "_UnderlayOffsetX", UnderlayOffset);
            Set(_skin, "_UnderlayOffsetY", -UnderlayOffset);
            Set(_skin, "_UnderlaySoftness", UnderlaySoftness);
            if (_skin.HasProperty("_UnderlayColor")) _skin.EnableKeyword("UNDERLAY_ON");

            return _skin;
        }

        /// <summary>Written only where the shader has it, so a swapped font cannot throw.</summary>
        static void Set(Material m, string name, float value)
        {
            if (m.HasProperty(name)) m.SetFloat(name, value);
        }

        static void Set(Material m, string name, Color value)
        {
            if (m.HasProperty(name)) m.SetColor(name, value);
        }

        /// <summary>
        /// The outline, and the drop under it. Both are the interface's own dark -- Scrim,
        /// at full strength for the edge and half for the drop -- rather than black, so a
        /// word sits in the same air as the panels behind it instead of being cut out of
        /// the screen.
        /// </summary>
        static Color Edge =>
            new Color(UiPalette.Scrim.r, UiPalette.Scrim.g, UiPalette.Scrim.b, 1f);

        static Color Drop =>
            new Color(UiPalette.Scrim.r, UiPalette.Scrim.g, UiPalette.Scrim.b, 0.55f);

        /// <summary>
        /// Anything on this object that was pointing at the old component, pointed at the
        /// new one.
        ///
        /// MenuLabel caches its target and also re-finds it whenever the cache is null, so
        /// it would heal itself on the next Set(). It is set here anyway: a label that is
        /// written once, early, by something that then never writes again -- the tip on the
        /// search screen is exactly that -- would otherwise miss its only write.
        /// </summary>
        static void Repoint(GameObject go, TMP_Text made)
        {
            foreach (MenuLabel label in go.GetComponents<MenuLabel>())
                label.target = made;
        }

        static TextAlignmentOptions Align(TextAnchor a) => a switch
        {
            TextAnchor.UpperLeft => TextAlignmentOptions.TopLeft,
            TextAnchor.UpperCenter => TextAlignmentOptions.Top,
            TextAnchor.UpperRight => TextAlignmentOptions.TopRight,
            TextAnchor.MiddleLeft => TextAlignmentOptions.Left,
            TextAnchor.MiddleCenter => TextAlignmentOptions.Center,
            TextAnchor.MiddleRight => TextAlignmentOptions.Right,
            TextAnchor.LowerLeft => TextAlignmentOptions.BottomLeft,
            TextAnchor.LowerCenter => TextAlignmentOptions.Bottom,
            TextAnchor.LowerRight => TextAlignmentOptions.BottomRight,
            _ => TextAlignmentOptions.Center,
        };
    }
}
