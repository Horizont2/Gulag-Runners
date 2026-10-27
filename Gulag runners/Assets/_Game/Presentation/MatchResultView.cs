using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace GulagRunners.Game
{
    /// <summary>
    /// The screen at the end of a match, and the way out of the arena.
    ///
    /// Until this existed the game had no ending. MatchState raises SeriesEnded on the tick
    /// somebody takes their second round, and NOTHING in the project subscribed to it --
    /// not one line. <see cref="MatchOutcomeView"/> plays its freeze, its slow motion and
    /// its camera push, the loser goes over, and then the arena simply sits there. No
    /// result, no rematch, no way back to the menu short of killing the app. A player who
    /// has just won cannot find out that they won, and a player who has just lost cannot
    /// play again. That is not a difficulty problem or a feel problem, it is the game not
    /// finishing, and it is the first thing to fix before any of the rest is worth tuning.
    ///
    /// Rounds are not this. A round ending is MatchOutcomeView's beat and the arena deals
    /// another one by itself; only the MATCH ends here.
    ///
    /// Built like the menu is: no uGUI type is named anywhere below. Readouts leave as
    /// UnityEvents (wire HeadlineChanged to a Text.text setter in the inspector) and the
    /// two things a player can do are public methods a Button's OnClick points at. The
    /// panel is already in the scene and switched off -- nothing here creates UI.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MatchResultView : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("The match. Found in the scene when empty.")]
        public MatchState match;

        [Tooltip("The panel to switch on. It lives in the scene, switched off, and this " +
                 "is the only thing that turns it on.")]
        public GameObject panel;

        [Tooltip("Which fighter the person holding this device is, so the headline can " +
                 "say whether they won rather than who did.")]
        [Range(0, 1)] public int localPlayer;

        [Header("Timing")]
        [Tooltip("Seconds to wait after the last blow before the panel appears.\n\n" +
                 "Long enough for MatchOutcomeView's freeze, slow motion and camera push " +
                 "to play out: a results card thrown over the top of the finishing blow " +
                 "takes away the one moment the whole match was for. Unscaled, because " +
                 "the slow motion is scaled and this should not stretch with it.")]
        [Range(0f, 6f)] public float showAfter = 1.8f;

        [Header("What it says")]
        [Tooltip("Shown when the local fighter took the match.")]
        public string wonText = "VICTORY";

        [Tooltip("Shown when the other one did.")]
        public string lostText = "DEFEAT";

        [Tooltip("Shown when the series is somehow level. Rare, and it should still say " +
                 "something rather than the last round's word.")]
        public string drawText = "DRAW";

        [Tooltip("The score line. {0} is the local fighter's rounds, {1} the other's.")]
        public string scoreFormat = "{0} — {1}";

        [Header("Leaving")]
        [Tooltip("Scene the Menu button loads. Empty hides that button's work: the event " +
                 "still fires, so a different way out can be wired to it.")]
        public string menuScene = "Menu";

        [Header("Readouts (wire these to the text in the inspector)")]
        public UnityEvent<string> HeadlineChanged;
        public UnityEvent<string> ScoreChanged;

        [Tooltip("Fired when the panel goes up, with the winner's index, or 2 for a draw.")]
        public UnityEvent<int> Shown;
        public UnityEvent Hidden;

        [Header("Two-phone play")]
        [Tooltip("Set by whatever owns the match clock while two devices are playing one " +
                 "match. While it is on, Again raises the event below instead of " +
                 "restarting, because a rematch on one phone and not the other is two " +
                 "games. Left alone in a single-player scene: nothing sets it.")]
        public bool someoneElseRestarts;

        [Tooltip("Raised by Again when something else owns the restart. Wire it to " +
                 "NetMatch.AskRematch — Setup > Set up two-phone play does it for you.")]
        public UnityEvent RematchAsked;

        [Header("Debug")]
        public string state = "waiting";

        float _due = -1f;
        int _winner = -1;
        bool _up;

        /// <summary>Whether the panel is up. Nothing else may be pressed while it is.</summary>
        public bool Showing => _up;

        void Awake()
        {
            if (match == null) match = FindFirstObjectByType<MatchState>();
            if (panel != null) panel.SetActive(false);
        }

        void OnEnable()
        {
            if (match != null) match.SeriesEnded += OnSeriesEnded;
        }

        void OnDisable()
        {
            if (match != null) match.SeriesEnded -= OnSeriesEnded;
        }

        void OnSeriesEnded(int winner)
        {
            _winner = winner;
            _due = Time.unscaledTime + showAfter;
            state = $"player {winner} took it; showing in {showAfter:0.0}s";
        }

        void Update()
        {
            if (_up || _due < 0f || Time.unscaledTime < _due) return;
            _due = -1f;
            Show();
        }

        /// <summary>
        /// Puts the panel up. Public so it can be driven from MatchOutcomeView.onSettled
        /// instead of by the timer, for anyone who would rather have the sequence say when
        /// it is done than guess at how long it takes.
        /// </summary>
        public void Show()
        {
            _up = true;
            if (_winner < 0 && match != null) _winner = match.SeriesWinner;

            int mine = match != null ? match.Wins(localPlayer) : 0;
            int theirs = match != null ? match.Wins(1 - localPlayer) : 0;

            string headline = _winner == localPlayer ? wonText
                            : _winner < 0 || _winner > 1 ? drawText
                            : lostText;

            if (panel != null) panel.SetActive(true);
            HeadlineChanged?.Invoke(headline);
            ScoreChanged?.Invoke(string.Format(scoreFormat, mine, theirs));
            Shown?.Invoke(_winner);

            state = $"{headline} {mine}-{theirs}";
        }

        /// <summary>Wire a Button's OnClick here. Another match, same arena, nil-nil.</summary>
        public void Rematch()
        {
            if (!_up) return;

            // While two devices are playing one match, restarting it is not this screen's
            // to do. Both phones have to deal the new arena on the SAME tick or they are
            // playing two games from then on -- so the tap becomes a request, and whoever
            // owns the clock schedules it. Nothing is hidden yet: the screen comes down
            // when the new match actually begins, half a second later.
            if (someoneElseRestarts)
            {
                RematchAsked?.Invoke();
                state = "rematch asked for";
                return;
            }

            Hide();

            if (match == null) return;
            match.ResetSeries();
            match.NextRound();
            state = "rematch";
        }

        /// <summary>Take the screen down from outside, when something else restarted.</summary>
        public void Dismiss()
        {
            if (_up) Hide();
        }

        /// <summary>Wire a Button's OnClick here. Out of the arena.</summary>
        public void ToMenu()
        {
            if (!_up) return;
            Hide();
            state = "to the menu";

            // Back to full speed first. MatchOutcomeView ramps the time scale and restores
            // it when its own sequence finishes -- but leaving by a button can happen
            // mid-ramp, and a scene loaded at 0.35 speed stays at 0.35 for ever, which
            // reads as the whole game having broken rather than as a menu.
            Time.timeScale = 1f;
            if (!string.IsNullOrEmpty(menuScene)) SceneManager.LoadScene(menuScene);
        }

        void Hide()
        {
            _up = false;
            _winner = -1;
            _due = -1f;
            if (panel != null) panel.SetActive(false);
            Hidden?.Invoke();
        }
    }
}
