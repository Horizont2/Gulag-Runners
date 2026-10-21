using UnityEngine;
using UnityEngine.UI;
using GulagRunners.Meta;

namespace GulagRunners.Game
{
    /// <summary>
    /// One line of text, behind a METHOD.
    ///
    /// <see cref="MenuUI"/> sends its readouts out as UnityEvents and references no uGUI
    /// type at all, which is what keeps the menu free to be drawn by something else later.
    /// A UnityEvent needs something to call, though, and a C# property setter is not a
    /// method a persistent listener can be given from an editor script without reflection.
    ///
    /// So: a method. Three lines of component, and the wiring in the inspector then reads
    /// "goldChanged -> Gold label -> Set", which is a sentence rather than a riddle.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MenuLabel : MonoBehaviour
    {
        [Tooltip("The text to write into. Left empty, one is looked for on this object.")]
        public Text target;

        [Tooltip("What it says while the game is not running, so the screen can be laid " +
                 "out against something that looks like real content.")]
        public string placeholder = "";

        void Reset() => target = GetComponent<Text>();

        void Awake()
        {
            if (target == null) target = GetComponent<Text>();
        }

        public void Set(string value)
        {
            if (target == null) target = GetComponent<Text>();
            if (target != null && target.text != value) target.text = value;
        }
    }

    /// <summary>
    /// A bar that fills from 0 to 1, by SCALING rather than by Image.fillAmount.
    ///
    /// fillAmount needs the sprite imported as Filled, which makes a nine-sliced bar stop
    /// being nine-sliced -- the ends stretch with the middle and a 5 px corner becomes a
    /// taper. Scaling the fill rect keeps the slice intact, costs nothing, and is the same
    /// thing HudView does to the health bars for the same reason.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MenuBar : MonoBehaviour
    {
        [Tooltip("The rect that grows. Its pivot has to be on the end it grows FROM.")]
        public RectTransform fill;

        [Range(0f, 1f)] public float preview = 0.62f;

        void Reset() => fill = transform.childCount > 0
            ? transform.GetChild(0) as RectTransform : null;

        public void Fill(float share)
        {
            if (fill == null) return;
            float s = Mathf.Clamp01(share);
            Vector3 scale = fill.localScale;
            if (!Mathf.Approximately(scale.x, s))
                fill.localScale = new Vector3(s, scale.y, scale.z);
        }

        void OnValidate() => Fill(preview);
    }

    /// <summary>
    /// The league badge: six sprites, one shown.
    ///
    /// Keyed off <see cref="League"/> rather than off a string, because a string would have
    /// to be parsed back out of "Platinum II" and the parse is the bug. The order is the
    /// enum's own, so adding a league above Runner some day means adding one sprite here.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MenuRankBadge : MonoBehaviour
    {
        [Tooltip("The image the badge is drawn into.")]
        public Image target;

        [Tooltip("Bronze, Silver, Gold, Platinum, Diamond, Runner — in that order.")]
        public Sprite[] badges = new Sprite[6];

        [Tooltip("Shown while the player is still in their first five matches and has no " +
                 "league at all. docs/09 says those are TRAINING and says so out loud.")]
        public Sprite unranked;

        void Reset() => target = GetComponent<Image>();

        /// <summary>-1 for no league yet; otherwise the League enum's value.</summary>
        public void Show(int league)
        {
            if (target == null) target = GetComponent<Image>();
            if (target == null) return;

            Sprite want = league >= 0 && league < badges.Length ? badges[league] : unranked;
            if (want != null && target.sprite != want) target.sprite = want;
        }
    }
}
