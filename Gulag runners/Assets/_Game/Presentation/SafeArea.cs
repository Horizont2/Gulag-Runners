using System.Collections.Generic;
using UnityEngine;

namespace GulagRunners.Game
{
    /// <summary>
    /// Keeps the interface out from under the notch, the camera hole and the home bar.
    ///
    /// NOTHING IN THIS PROJECT WAS DOING IT. Every canvas is laid out against a flat
    /// 1920 x 864 sheet and stretched to whatever the device is, which is correct for the
    /// SHAPE of a phone and says nothing about the parts of it you are not allowed to draw
    /// on. On anything made in the last six years that is a real list: a landscape phone
    /// has a notch or a punch-hole eating one end of the screen and a home indicator along
    /// the bottom, and this game puts the health bars and the clock at the top corners and
    /// the stick and the four buttons at the bottom ones. Every one of those is in the way.
    ///
    /// It is a worse fault than it sounds, because it is not only cosmetic: a touch inside
    /// the home indicator's strip is swallowed by the system gesture, so the bottom row of
    /// a control scheme can be ignoring presses on one handset and not on another, and the
    /// player has no way to describe what is happening.
    ///
    /// ON THE CANVAS, AND IT REPARENTS NOTHING. The usual shape of this is a panel between
    /// the canvas and its contents, which means rebuilding every screen in the project
    /// around it. It is not needed: every direct child of every canvas here is already a
    /// full-screen stretch -- the top bar, the equipment column, the stick, the controls,
    /// the result, the five menu panels -- so insetting those children by the safe area
    /// moves everything inside them and touches nothing else.
    ///
    /// A child that is NOT a full-screen stretch is left exactly alone. That is the escape
    /// hatch for anything that genuinely wants the whole glass: a backdrop, a scrim, a
    /// fade. Those should bleed into the notch, and they do.
    ///
    /// Polled rather than driven off an event, because the thing being watched changes
    /// without one: a phone rotates, a tablet's window is resized, and on Android the safe
    /// area can change while the app is running when the gesture bar is hidden or shown.
    /// Four floats compared once a frame costs nothing and cannot miss a change.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Canvas))]
    public sealed class SafeArea : MonoBehaviour
    {
        [Header("Which edges to keep clear")]
        [Tooltip("The notch and the punch-hole live on the short edges in landscape, which " +
                 "is where the health bars and the clock are.")]
        public bool left = true;
        public bool right = true;

        [Tooltip("The status bar, where there is one.")]
        public bool top = true;

        [Tooltip("The home indicator, and the gesture strip that swallows touches near it. " +
                 "The one edge where this is a CONTROL fix and not a layout one: the stick " +
                 "and the four buttons sit on it.")]
        public bool bottom = true;

        [Header("Debug")]
        [Tooltip("The inset actually in use, as fractions of the screen.")]
        public string inset = "";

        /// <summary>
        /// What each child was authored as, so this can be undone and so a second pass
        /// never insets an already-inset child.
        ///
        /// Keyed by the child rather than held as a list, because a panel can be switched
        /// off and on again -- the menu does exactly that with all five of its screens --
        /// and the anchors have to go back to the AUTHORED ones every time rather than to
        /// whatever the last frame left.
        /// </summary>
        readonly Dictionary<RectTransform, Vector4> _authored =
            new Dictionary<RectTransform, Vector4>();

        Rect _was;
        Vector2 _wasScreen;

        void OnEnable()
        {
            _was = new Rect(-1f, -1f, -1f, -1f);
            Apply();
        }

        void OnDisable()
        {
            // Put everything back, so switching this off in the inspector shows the layout
            // as it was authored rather than as it was last frame.
            foreach (KeyValuePair<RectTransform, Vector4> pair in _authored)
            {
                if (pair.Key == null) continue;
                pair.Key.anchorMin = new Vector2(pair.Value.x, pair.Value.y);
                pair.Key.anchorMax = new Vector2(pair.Value.z, pair.Value.w);
            }
            _authored.Clear();
            inset = "";
        }

        void Update() => Apply();

        void Apply()
        {
            Rect safe = Screen.safeArea;
            Vector2 screen = new Vector2(Screen.width, Screen.height);

            if (screen.x <= 0f || screen.y <= 0f) return;
            if (safe == _was && screen == _wasScreen && _authored.Count > 0) return;

            _was = safe;
            _wasScreen = screen;

            // As fractions of the screen, which is the unit anchors are in. Guarded,
            // because a safe area wider than the screen is nonsense a platform can still
            // report while the window is being resized.
            float x0 = Mathf.Clamp01(safe.xMin / screen.x);
            float y0 = Mathf.Clamp01(safe.yMin / screen.y);
            float x1 = Mathf.Clamp01(safe.xMax / screen.x);
            float y1 = Mathf.Clamp01(safe.yMax / screen.y);

            if (!left) x0 = 0f;
            if (!right) x1 = 1f;
            if (!bottom) y0 = 0f;
            if (!top) y1 = 1f;

            if (x1 <= x0 || y1 <= y0) { x0 = y0 = 0f; x1 = y1 = 1f; }

            inset = Mathf.Approximately(x0, 0f) && Mathf.Approximately(y0, 0f) &&
                    Mathf.Approximately(x1, 1f) && Mathf.Approximately(y1, 1f)
                ? "none (this screen has no cut-outs)"
                : $"left {x0:0.###} bottom {y0:0.###} right {1f - x1:0.###} top {1f - y1:0.###}";

            for (int i = 0; i < transform.childCount; i++)
            {
                if (!(transform.GetChild(i) is RectTransform child)) continue;

                if (!_authored.TryGetValue(child, out Vector4 was))
                {
                    // ONLY A FULL-SCREEN STRETCH. Anything else was positioned against a
                    // corner or an edge on purpose and insetting it would move it twice.
                    if (!Stretched(child)) continue;

                    was = new Vector4(child.anchorMin.x, child.anchorMin.y,
                                      child.anchorMax.x, child.anchorMax.y);
                    _authored[child] = was;
                }

                child.anchorMin = new Vector2(Mathf.Lerp(was.x, was.z, x0),
                                              Mathf.Lerp(was.y, was.w, y0));
                child.anchorMax = new Vector2(Mathf.Lerp(was.x, was.z, x1),
                                              Mathf.Lerp(was.y, was.w, y1));
            }
        }

        /// <summary>Is this child anchored to the whole canvas, corner to corner?</summary>
        static bool Stretched(RectTransform rt) =>
            Mathf.Approximately(rt.anchorMin.x, 0f) && Mathf.Approximately(rt.anchorMin.y, 0f) &&
            Mathf.Approximately(rt.anchorMax.x, 1f) && Mathf.Approximately(rt.anchorMax.y, 1f);
    }
}
