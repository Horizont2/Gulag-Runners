using TMPro;
using UnityEditor;
using UnityEngine;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// One place where a label is made, for all six builders.
    ///
    /// The game's text used to be <c>UnityEngine.UI.Text</c>, which is a bitmap font
    /// rasterised at one size: it goes soft the moment the CanvasScaler scales it, which on
    /// a 1920x864 sheet stretched across every phone from 720p to 1440p is always. TMP draws
    /// from a signed distance field instead, so the same asset is sharp at any size, which
    /// is the whole reason to move -- not fashion.
    ///
    /// It lives here rather than six times over because the one thing that does NOT map
    /// across is the alignment: legacy says <c>TextAnchor.MiddleLeft</c>, TMP says
    /// <c>TextAlignmentOptions.Left</c>, and nine values written out six times is eight
    /// chances to write one of them wrong.
    ///
    /// The sizes do not change. 28 units is still the floor docs/23 argues for, and a TMP
    /// point is near enough a legacy point that the millimetres ui_fit_check measures still
    /// hold; what changes is that the glyph is no longer blurred on the way there.
    /// </summary>
    public static class UiText
    {
        /// <summary>
        /// A label on an existing rect. The font may be null: TMP then takes the default
        /// out of its own settings, which is how a project with no font of its own still
        /// shows words.
        /// </summary>
        public static TextMeshProUGUI On(GameObject go, string chars, float size,
                                         TextAnchor align, bool bold, TMP_FontAsset font,
                                         string undo)
        {
            TextMeshProUGUI t = Undo.AddComponent<TextMeshProUGUI>(go);
            _ = undo;
            t.text = chars;
            t.fontSize = Mathf.Max(8f, size);
            t.alignment = Align(align);
            t.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            t.raycastTarget = false;
            t.richText = false;

            // A readout that does not fit is a readout that has been laid out wrongly, and
            // ui_layout_check is what says so. Letting it spill is how that stays visible
            // rather than being quietly hidden behind an ellipsis.
            t.overflowMode = TextOverflowModes.Overflow;
            t.enableAutoSizing = false;

            if (font != null) t.font = font;
            else if (TMP_Settings.defaultFontAsset != null)
                t.font = TMP_Settings.defaultFontAsset;
            return t;
        }

        /// <summary>
        /// The face to use when the builder was given none. TMP ships one, which is why
        /// this project can have no font of its own and still read.
        /// </summary>
        public static TMP_FontAsset DefaultFace() => TMP_Settings.defaultFontAsset;

        /// <summary>The nine anchors, said the other way. The only thing that does not map.</summary>
        public static TextAlignmentOptions Align(TextAnchor anchor) => anchor switch
        {
            TextAnchor.UpperLeft => TextAlignmentOptions.TopLeft,
            TextAnchor.UpperCenter => TextAlignmentOptions.Top,
            TextAnchor.UpperRight => TextAlignmentOptions.TopRight,
            TextAnchor.MiddleLeft => TextAlignmentOptions.Left,
            TextAnchor.MiddleCenter => TextAlignmentOptions.Center,
            TextAnchor.MiddleRight => TextAlignmentOptions.Right,
            TextAnchor.LowerLeft => TextAlignmentOptions.BottomLeft,
            TextAnchor.LowerCenter => TextAlignmentOptions.Bottom,
            _ => TextAlignmentOptions.BottomRight,
        };

        /// <summary>
        /// The field every builder window shows. Null is a real answer -- TMP ships a
        /// default face, so the screen builds and reads with nothing dragged in.
        /// </summary>
        public static TMP_FontAsset Field(TMP_FontAsset current)
        {
            return (TMP_FontAsset)EditorGUILayout.ObjectField(
                new GUIContent("Typeface",
                               "Optional. A TextMeshPro font asset. Without one every label "
                               + "comes out in TMP's own default face, which is sharp at any "
                               + "size but is not the game's."),
                current, typeof(TMP_FontAsset), false);
        }
    }
}
