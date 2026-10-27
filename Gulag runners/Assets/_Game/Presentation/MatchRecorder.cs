using UnityEngine;
using UnityEngine.Events;
using GulagRunners.Meta;

namespace GulagRunners.Game
{
    /// <summary>
    /// Tells the player's progress that a match happened.
    ///
    /// Everything this needs was already written and none of it was connected.
    /// PlayerProgress.RecordMatch runs the Glicko-2 update, rolls the daily over, pays
    /// the purse and saves to PlayerPrefs -- and nothing in the project called it, ever.
    /// So a player could win twenty matches and the menu would still say nought played,
    /// unranked, no gold. The arena and the meta were two halves with no pipe between
    /// them, which is the same shape as every other gap this project has had.
    ///
    /// Deliberately NOT inside MatchResultView. That view is a screen: it decides what a
    /// panel says. This one changes a saved file. A screen that also writes the save is a
    /// screen you cannot skip, reorder or replace without losing the player's rating.
    ///
    /// Recorded ONCE per match, on SeriesEnded, and guarded against a second call --
    /// because paying the purse twice for one match is the kind of bug that is only ever
    /// noticed by the person it overpays.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MatchRecorder : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("The match. Found in the scene when empty.")]
        public MatchState match;

        [Tooltip("The player's saved progress. Found through its Instance when empty.")]
        public PlayerProgress progress;

        [Tooltip("Which fighter this device is playing, so a win is recorded for the " +
                 "right one.")]
        [Range(0, 1)] public int localPlayer;

        [Header("The opponent")]
        [Tooltip("Rating of whoever was fought, for the ladder update. Until there is a " +
                 "real opponent to ask, a match against the practice bot is scored " +
                 "against a middling, settled player: 1500 at a deviation of 80.\n\n" +
                 "It matters that this is not the player's OWN rating. Scored against " +
                 "yourself, every win is a surprise and every loss a shock, and the " +
                 "ladder swings wildly on a bot that was never trying.")]
        public double opponentRating = 1500;
        [Range(20f, 350f)] public double opponentDeviation = 80;

        [Tooltip("Off while the opponent is the practice bot, if you would rather the " +
                 "ladder only counted real matches. The purse and the counts still run.")]
        public bool rateAgainstBots = true;

        [Header("What it paid, for the result screen")]
        [Tooltip("Gold earned by this match. Wire to a label on the result panel.")]
        public UnityEvent<string> GoldChanged;

        [Tooltip("The league after the match — the only form the rating is ever shown " +
                 "in (docs/09: a visible number is a thing to blame yourself for).")]
        public UnityEvent<string> LeagueChanged;

        [Header("Debug")]
        public string state = "nothing recorded yet";

        bool _done;

        /// <summary>What the last match paid, for anything that wants to show it.</summary>
        public Reward LastReward { get; private set; }

        void Awake()
        {
            if (match == null) match = FindFirstObjectByType<MatchState>();
            if (progress == null) progress = PlayerProgress.Instance;
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
            // Once. A second SeriesEnded for the same match would pay the purse twice,
            // and an overpayment is the one bug nobody reports.
            if (_done)
            {
                state = "already recorded; ignored a second end";
                return;
            }
            _done = true;

            if (progress == null) progress = PlayerProgress.Instance;
            if (progress == null)
            {
                state = "no PlayerProgress in the scene, so nothing was saved";
                Debug.LogWarning($"{name}: a match ended and there is no PlayerProgress to "
                                 + "record it on, so the rating, the purse and the match "
                                 + "count are all lost. Put one in the scene.", this);
                return;
            }

            bool won = winner == localPlayer;
            var opponent = new Rating
            {
                Value = rateAgainstBots ? opponentRating : progress.rating,
                Deviation = rateAgainstBots ? opponentDeviation : progress.deviation,
                Volatility = progress.volatility,
            };

            // The day the match was played, as a day number, so "first today" survives
            // the app being closed and reopened.
            int today = System.DateTime.UtcNow.DayOfYear + System.DateTime.UtcNow.Year * 400;

            LastReward = progress.RecordMatch(won, opponent, today);

            state = $"{(won ? "won" : "lost")}, +{LastReward.Total} gold, "
                    + $"{progress.LeagueName}";
            GoldChanged?.Invoke($"+{LastReward.Total}");
            LeagueChanged?.Invoke(progress.LeagueName);
        }

        /// <summary>A rematch is a new match, and may be recorded in its own right.</summary>
        public void Rearm() => _done = false;
    }
}
