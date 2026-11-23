// Enough of TextMeshPro for the compiler to get past the DECLARATIONS.
//
// This is not about tidying up a warning. Roslyn binds a compilation in phases: every
// declaration first, then every method body. If the declaration phase ends with errors it
// does not run the body phase at all -- so while the 27 "TMPro could not be found" errors
// stood, compile_check was reporting them and NOTHING ELSE. Every mistake inside a method
// was invisible to it.
//
// It was not a theory. Two compile errors reached Unity in one session while this check
// reported a steady 27: a bare LightProbeUsage, and SimCollider.WorldBox, which does not
// exist. Both are body-phase errors. Tested directly, too: `int x = "nope";` appended to
// an already-compiling file produced no diagnostic at all.
//
// So the point of this file is not TMPro. It is that the declaration phase has to end
// CLEAN, or the check is blind to the larger half of what it exists to catch.
//
// Only the surface the project actually touches is here, found by grepping for it rather
// than copied from the package: TMP_Text.SetText and .text, the eleven alignments in use,
// TextOverflowModes.Overflow, TMP_Settings.defaultFontAsset, and the font/colour/size
// fields the builders set. A fuller stub would be a second copy of somebody else's API to
// keep in step, which is the thing this folder exists to avoid.
//
// The real TextMeshPro ships inside Unity, not on NuGet, which is why it cannot simply be
// referenced the way UnityEngine.Modules is.

using UnityEngine;
using UnityEngine.UI;

namespace TMPro
{
    public enum TextAlignmentOptions
    {
        TopLeft, Top, TopRight,
        Left, Center, Right,
        BottomLeft, Bottom, BottomRight,
        Midline, Capline,
    }

    [System.Flags]
    public enum FontStyles
    {
        Normal = 0, Bold = 1, Italic = 2, Underline = 4, LowerCase = 8,
        UpperCase = 16, SmallCaps = 32, Strikethrough = 64, Superscript = 128,
        Subscript = 256, Highlight = 512,
    }

    public enum TextOverflowModes
    {
        Overflow, Ellipsis, Masking, Truncate, ScrollRect, Page, Linked,
    }

    public class TMP_FontAsset : ScriptableObject
    {
    }

    public class TMP_Settings : ScriptableObject
    {
        public static TMP_FontAsset defaultFontAsset;
    }

    // Graphic, not MonoBehaviour: the real TMP_Text is a MaskableGraphic, and the
    // builders pass one wherever a Graphic is wanted.
    public class TMP_Text : Graphic
    {
        public string text;
        public TMP_FontAsset font;
        public Color color;
        public float fontSize;
        public TextAlignmentOptions alignment;
        public TextOverflowModes overflowMode;
        public bool richText;
        public FontStyles fontStyle;
        public bool raycastTarget;
        public bool enableWordWrapping;
        public bool enableAutoSizing;
        public float fontSizeMin;
        public float fontSizeMax;
        public Vector4 margin;
        public float characterSpacing;
        public float lineSpacing;
        public int maxVisibleCharacters;

        public void SetText(string value) { text = value; }
        public void SetText(string value, bool syncTextInputBox) { text = value; }
        public void ForceMeshUpdate() { }
    }

    public class TextMeshProUGUI : TMP_Text
    {
    }

    public class TextMeshPro : TMP_Text
    {
    }

    public class TMP_InputField : Graphic
    {
        public string text;
        public TMP_Text textComponent;
        public TMP_Text placeholder;
        public int characterLimit;
        public Graphic targetGraphic;
        public LineType lineType;

        public enum LineType { SingleLine, MultiLineSubmit, MultiLineNewline }
        public UnityEngine.Events.UnityEvent<string> onValueChanged
            = new UnityEngine.Events.UnityEvent<string>();
        public UnityEngine.Events.UnityEvent<string> onEndEdit
            = new UnityEngine.Events.UnityEvent<string>();
    }
}
