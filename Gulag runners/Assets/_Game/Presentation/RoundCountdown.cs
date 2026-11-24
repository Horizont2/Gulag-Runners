using TMPro;
using UnityEngine;
using UnityEngine.UI;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// Three, two, one, FIGHT.
    ///
    /// The round already had a three second hold in it -- MatchPhase.Cages, during which
    /// InputAllowed is false and nobody can move -- and nothing told the player it was
    /// happening or how much of it was left. Three seconds of being unable to act, with no
    /// number on screen, reads as the game not having started yet rather than as the game
    /// about to start.
    ///
    /// IT IS DRIVEN BY THE SIMULATION, not by Time.time, and that is the whole reason this
    /// is worth building rather than animating. MatchClock.CageProgress is the same 0 to 1
    /// both machines are running, so the last beat lands on exactly the tick that hands
    /// control back -- on both phones, whatever either of them is rendering at. A countdown
    /// started from a local clock drifts from the thing it is counting down to, and the one
    /// frame where that shows is the one where somebody loses a race to the first chest.
    ///
    /// The beat inside each number is taken from the same progress, so even the punch is the
    /// same on both machines. Only FIGHT runs on a local clock, because by then the phase it
    /// belongs to is over and there is nothing left to be wrong about.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RoundCountdown : MonoBehaviour
    {
        [Tooltip("Leave empty to find the MatchState in the scene.")]
        public MatchState matchState;

        [Tooltip("Leave empty and a label is built under the first canvas found, or under " +
                 "one made for it. Assign one to place it by hand instead.")]
        public TMP_Text label;

        [Header("The numbers")]
        [Tooltip("How tall a numeral is, as a share of the screen's height. 0.26 is about a " +
                 "quarter of the screen -- big enough to be read without looking at it, " +
                 "which is the point: the player's eyes should be on the arena.")]
        [Range(0.08f, 0.5f)] public float heightFraction = 0.26f;

        [Tooltip("What a numeral is coloured. The amber the rest of the interface uses for " +
                 "the things that are about to matter.")]
        public Color numberColour = UiPalette.Amber;

        [Tooltip("What FIGHT is coloured.")]
        public Color goColour = UiPalette.Ink;

        [Tooltip("How big a numeral arrives, as a multiple of its settled size. It lands by " +
                 "punchFor and then holds, which is what makes it read as struck rather than " +
                 "as faded in.")]
        [Range(1f, 3f)] public float punchFrom = 1.65f;

        [Tooltip("How long the punch takes, as a share of one beat.")]
        [Range(0.05f, 0.6f)] public float punchFor = 0.22f;

        [Tooltip("How far past its settled size a numeral goes before it comes back, as a "
                 + "share of the punch. Without it the number stops dead on its mark and the "
                 + "arrival reads as a cut.")]
        [Range(0f, 0.3f)] public float overshoot = 0.09f;

        [Tooltip("When in the beat a numeral starts leaving, as a share of one beat.")]
        [Range(0.5f, 1f)] public float leaveAt = 0.78f;

        [Header("FIGHT")]
        [Tooltip("The word on the last beat. Empty shows nothing and the numbers simply end.")]
        public string goWord = "FIGHT";

        [Tooltip("How long FIGHT stays, in seconds, once the hold is over.")]
        [Range(0.1f, 2f)] public float goSeconds = 0.55f;

        RectTransform _rect;
        float _goTimer = -1f;
        bool _wasHeld;

        void Awake()
        {
            if (matchState == null) matchState = FindFirstObjectByType<MatchState>();
            if (label == null) label = Build();
            if (label != null)
            {
                _rect = label.rectTransform;
                label.text = string.Empty;
            }
        }

        /// <summary>
        /// A label of its own, so the countdown works in a scene nobody has prepared.
        ///
        /// Built rather than required because this has to appear in four arenas and the
        /// alternative is four hand-placed labels that drift apart. Anchored to the middle
        /// of the screen and sized in SCREEN heights, not in points: a numeral a quarter of
        /// the way up the screen is a quarter of the way up every screen.
        /// </summary>
        TMP_Text Build()
        {
            Canvas canvas = null;
            foreach (Canvas c in FindObjectsByType<Canvas>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                if (canvas == null || c.sortingOrder > canvas.sortingOrder) canvas = c;
            }

            if (canvas == null)
            {
                GameObject made = new GameObject("Countdown Canvas");
                made.transform.SetParent(transform, false);
                canvas = made.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                made.AddComponent<CanvasScaler>();
            }

            GameObject holder = new GameObject("Round Countdown");
            holder.transform.SetParent(canvas.transform, false);

            TextMeshProUGUI text = holder.AddComponent<TextMeshProUGUI>();
            text.font = TMP_Settings.defaultFontAsset;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = false;
            text.raycastTarget = false;
            text.color = numberColour;

            RectTransform rt = text.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(1200f, 400f);

            // Last, so it is over the rest of the interface rather than under the health bars.
            holder.transform.SetAsLastSibling();
            return text;
        }

        void LateUpdate()
        {
            if (label == null || matchState == null) return;

            MatchSimState m = matchState.Match;
            bool held = m.Phase == MatchPhase.Cages;

            if (held)
            {
                _wasHeld = true;
                _goTimer = -1f;
                Show(m);
                return;
            }

            // The hold just ended: this is the tick control came back.
            if (_wasHeld)
            {
                _wasHeld = false;
                _goTimer = string.IsNullOrEmpty(goWord) ? -1f : 0f;
            }

            if (_goTimer >= 0f)
            {
                _goTimer += Time.deltaTime;
                float t = _goTimer / Mathf.Max(0.0001f, goSeconds);
                if (t >= 1f) { _goTimer = -1f; label.text = string.Empty; return; }

                label.text = goWord;
                label.color = Tint(goColour, 1f - t * t);
                Size(Beat(t));
                return;
            }

            label.text = string.Empty;
        }

        /// <summary>Which numeral, and how far through its own beat.</summary>
        void Show(MatchSimState m)
        {
            // A player who lagged and came back must see the match as it IS, rather than
            // count down into a round the other end started long ago. See
            // MatchState.ShowTheOpening.
            if (!matchState.ShowTheOpening) { label.text = string.Empty; return; }

            float through = Mathf.Clamp01(
                m.CageProgress(matchState.MatchRules).Raw / (float)Fix.RawOne);

            // The hold is however many whole seconds it is: three seconds of cage is three
            // numerals, and a designer who changes cageSeconds gets the right count rather
            // than three beats of a different length.
            int beats = Mathf.Max(1, Mathf.RoundToInt(matchState.cageSeconds));
            float place = through * beats;
            int index = Mathf.Clamp(Mathf.FloorToInt(place), 0, beats - 1);
            float t = place - index;

            label.text = (beats - index).ToString();
            label.color = Tint(numberColour, t < leaveAt ? 1f
                : 1f - Mathf.InverseLerp(leaveAt, 1f, t));
            Size(Beat(t));
        }

        /// <summary>
        /// The size of a numeral through one beat: struck, settled, then let go.
        ///
        /// One curve for both the numbers and the word, so they move alike. The arrival is
        /// the part that matters -- big and shrinking, overshooting its mark by a hair -- and
        /// the departure is a drift upward in scale while the colour goes, which reads as
        /// the thing receding rather than being switched off.
        /// </summary>
        float Beat(float t)
        {
            if (t < punchFor)
            {
                float k = t / Mathf.Max(0.0001f, punchFor);
                float ease = 1f - (1f - k) * (1f - k) * (1f - k);
                return Mathf.LerpUnclamped(punchFrom, 1f - overshoot, ease);
            }

            if (t < leaveAt)
            {
                // Back up through the overshoot, over the same length again.
                float k = Mathf.Clamp01((t - punchFor) / Mathf.Max(0.0001f, punchFor));
                return Mathf.Lerp(1f - overshoot, 1f, k);
            }

            float out1 = Mathf.InverseLerp(leaveAt, 1f, t);
            return 1f + out1 * 0.14f;
        }

        void Size(float scale)
        {
            // In screen heights, so the numeral is the same share of a tall phone and a
            // short one. TMP's own font size is in points and a canvas scaler's reference
            // resolution is one more thing to keep in step.
            float points = Screen.height * heightFraction * scale;
            label.fontSize = Mathf.Max(8f, points);
            if (_rect != null) _rect.anchoredPosition = Vector2.zero;
        }

        static Color Tint(Color c, float alpha) => new Color(c.r, c.g, c.b, c.a * Mathf.Clamp01(alpha));
    }
}
