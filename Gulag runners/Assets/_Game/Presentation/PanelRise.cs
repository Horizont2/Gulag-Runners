using UnityEngine;
using UnityEngine.UI;

namespace GulagRunners.Game
{
    /// <summary>
    /// A panel arrives instead of appearing.
    ///
    /// Everything in this game that shows a panel does it with SetActive: MenuUI.Open for
    /// the shop, the profile, the dailies, the settings and the friends list, and
    /// MatchResultView for the end of every round. SetActive is a cut -- one frame it is
    /// not there, the next frame it is -- and a cut is what an interface does when nobody
    /// has decided how it should move.
    ///
    /// ONE COMPONENT RATHER THAN SIX ANIMATIONS, because the fix is the same every time and
    /// because the reason nobody wrote it before is that it was six jobs. Drop it on a
    /// panel and the panel fades up and settles from slightly below; nothing else changes,
    /// no layout moves, and the thing underneath is untouched.
    ///
    /// IT RUNS ON UNSCALED TIME. The result screen comes up at the end of a round, which is
    /// exactly where a game is most likely to be holding time still for a hit stop, and a
    /// panel that waits out a freeze before animating reads as the game having locked up.
    ///
    /// No coroutine: a coroutine on a panel that is being enabled and disabled is a
    /// coroutine that gets killed halfway and leaves the alpha wherever it was.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PanelRise : MonoBehaviour
    {
        [Tooltip("Seconds the arrival takes. Under about a tenth it is a cut with extra " +
                 "steps; over about a third the player is waiting for their own tap.")]
        [Range(0.05f, 0.6f)] public float seconds = 0.17f;

        [Tooltip("How far it starts below where it lands, in canvas units. Small: this is " +
                 "a settle, not an entrance.")]
        [Range(0f, 120f)] public float rise = 28f;

        [Tooltip("How much smaller it starts. 1 is no scale at all, which is the right " +
                 "answer for a full-screen panel -- scaling one of those reads as a zoom.")]
        [Range(0.9f, 1f)] public float from = 0.985f;

        CanvasGroup _group;
        RectTransform _rect;
        Vector2 _home;
        float _t = 1f;
        bool _known;

        void Awake()
        {
            _rect = transform as RectTransform;
            _group = GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();

            // The resting place, taken once. Sampled on every enable it would drift down
            // by `rise` each time the panel was opened, because the panel is not back home
            // yet when OnEnable runs.
            if (_rect != null && !_known) { _home = _rect.anchoredPosition; _known = true; }
        }

        void OnEnable()
        {
            _t = 0f;
            Apply(0f);
        }

        void OnDisable()
        {
            // Left mid-arrival, the next thing to read this panel -- a screenshot, a
            // builder, the editor -- would find it half transparent and off its mark.
            Apply(1f);
        }

        void Update()
        {
            if (_t >= 1f) return;

            _t += Time.unscaledDeltaTime / Mathf.Max(0.01f, seconds);
            Apply(Mathf.Clamp01(_t));
        }

        void Apply(float t)
        {
            float k = Ease.Out(t);

            if (_group != null) _group.alpha = k;

            if (_rect != null && _known)
            {
                _rect.anchoredPosition = new Vector2(_home.x, _home.y - rise * (1f - k));
                float scale = Mathf.Lerp(from, 1f, k);
                _rect.localScale = new Vector3(scale, scale, 1f);
            }
        }
    }
}
