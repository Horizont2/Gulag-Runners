using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GulagRunners.Game
{
    /// <summary>
    /// The one black square the game fades through.
    ///
    /// There was nothing like this anywhere in the project -- no ScreenFade, no Fader, not
    /// a single CanvasGroup being driven -- so every scene change was a cut. The menu's
    /// gate rise hides most of the wait, which is why it was never missed, but the frame
    /// where one scene becomes another is still a hard swap: the camera is mid-walk through
    /// a portcullis and then it is in a mine.
    ///
    /// IT SURVIVES THE LOAD. DontDestroyOnLoad, one instance, created on demand, so the
    /// same square can be dark before the scene goes away and light after the next one has
    /// drawn its first frame. A fader that belongs to the scene it is leaving cannot do the
    /// second half, which is the half that matters: coming out of black into a built arena
    /// is what makes the arrival feel deliberate.
    ///
    /// UNSCALED TIME, and a sorting order above everything. A fade that can be frozen by a
    /// hit stop or drawn under the HUD is not a fade.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ScreenFade : MonoBehaviour
    {
        static ScreenFade _only;

        /// <summary>The one in the game, made if there is not one yet.</summary>
        public static ScreenFade Instance
        {
            get
            {
                if (_only != null) return _only;

                GameObject made = new GameObject("Screen Fade");
                DontDestroyOnLoad(made);

                Canvas canvas = made.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 32000;     // over the HUD, over everything

                GameObject sheet = new GameObject("Black");
                sheet.transform.SetParent(made.transform, false);

                Image black = sheet.AddComponent<Image>();
                black.color = new Color(0f, 0f, 0f, 0f);
                black.raycastTarget = false;     // dark, but never in the way of a tap

                RectTransform rt = black.rectTransform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;

                _only = made.AddComponent<ScreenFade>();
                _only._black = black;
                return _only;
            }
        }

        [Tooltip("Seconds to go dark. Shorter than coming back: leaving should feel like a " +
                 "decision and arriving like a reveal.")]
        [Range(0.05f, 1.5f)] public float outSeconds = 0.22f;

        [Tooltip("Seconds to come back. Starts once the new scene has drawn a frame.")]
        [Range(0.05f, 2f)] public float inSeconds = 0.38f;

        Image _black;
        float _want;
        float _now;

        void Awake()
        {
            if (_only != null && _only != this) { Destroy(gameObject); return; }
            _only = this;
            SceneManager.sceneLoaded += Arrived;
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= Arrived;
            if (_only == this) _only = null;
        }

        /// <summary>Go dark. Call it, then load whenever you like.</summary>
        public void Out() => _want = 1f;

        /// <summary>Come back, now.</summary>
        public void In() => _want = 0f;

        /// <summary>How far through going dark it is, 0 to 1.</summary>
        public float Dark => _now;

        void Arrived(Scene scene, LoadSceneMode mode)
        {
            // The new scene exists but has not drawn. Coming back on the very next frame
            // would uncover a frame of whatever it looks like before its own setup has
            // run; one frame of black costs nothing and hides all of that.
            _want = 0f;
        }

        void LateUpdate()
        {
            if (_black == null) return;

            float seconds = _want > _now ? outSeconds : inSeconds;
            _now = Ease.Towards(_now, _want, seconds, Time.unscaledDeltaTime);
            if (Mathf.Abs(_now - _want) < 0.002f) _now = _want;

            Color c = _black.color;
            if (!Mathf.Approximately(c.a, _now))
                _black.color = new Color(c.r, c.g, c.b, _now);

            // Nothing to draw and nothing to do: stop costing a raycast and a fill.
            _black.enabled = _now > 0.001f;
        }
    }
}
