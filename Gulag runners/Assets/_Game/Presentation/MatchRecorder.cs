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
    /// Two things about that pipe were still fiction after it was laid, and both are fixed
    /// here. The OPPONENT was two numbers typed into the inspector, so every match was
    /// scored against a stand-in while the real rating the matchmaker paired against was
    /// dropped at the scene load -- MatchHandover carries it now. And the DAILIES were
    /// never passed, so docs/09's three tasks at 40 gold each had never paid anybody a
    /// single piece -- MatchTally counts what a match contained and Facts() hands it in.
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

        [Tooltip("Where the match is counted — picked locks, the best weapon held, who " +
                 "drowned. Found in the scene when empty. Without one the match still " +
                 "pays and still ranks, but no daily task can be earned, because nothing " +
                 "knows what happened in it.")]
        public MatchTally tally;

        [Header("The opponent, when nobody handed one over")]
        [Tooltip("These two are the FALLBACK and nothing more. A match reached through " +
                 "the menu carries the real opponent in MatchHandover, and that is what " +
                 "is scored against.\n\n" +
                 "They are what an arena opened on its own uses — which is how this " +
                 "project is developed, constantly. A middling, settled player: 1500 at " +
                 "a deviation of 80.\n\n" +
                 "It matters that this is not the player's OWN rating. Scored against " +
                 "yourself, every win is a surprise and every loss a shock, and the " +
                 "ladder swings wildly on a bot that was never trying.")]
        public double opponentRating = 1500;
        [Range(20f, 350f)] public double opponentDeviation = 80;

        [Tooltip("Whether a PRACTICE match — one with no opponent handed over — moves the " +
                 "ladder. Off by docs/09: matches 1-5 are bots \"honestly labelled " +
                 "TRAINING\", and a match labelled training that quietly changes your rank " +
                 "is not honest. A bot is also the matchmaker's fallback at any point in " +
                 "the ladder (rule 2: better a bot than a professional), so a bot match " +
                 "that moved the rating would mean gaining and losing rank against bots.\n\n" +
                 "On to test that the ladder moves at all without going through the menu. " +
                 "The purse, the dailies and the match count run either way — a player who " +
                 "did the work gets paid for it.")]
        public bool practiceCountsOnTheLadder;

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
            if (tally == null) tally = FindFirstObjectByType<MatchTally>();
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

            // From the match, not from the inspector. A rating written against the wrong
            // fighter is the one mistake here that cannot be noticed and cannot be undone:
            // the player loses and climbs, or wins and drops, and the save is already on
            // disk by the time anybody wonders.
            if (match != null) localPlayer = match.LocalFighter;

            bool won = winner == localPlayer;
            MatchFacts facts = Facts(won);

            // Who was actually fought.
            //
            // This used to be whatever two numbers were typed into the inspector, so every
            // match ever played was scored against a stand-in. MatchSearch knew the real
            // rating -- MatchFound.OpponentRating, from the matchmaker that chose them --
            // and dropped it at the scene load, because the menu and the arena are separate
            // scenes and nothing carried it across. So the Glicko-2 update ran on fiction: a
            // ladder that moved, but not in response to who you beat.
            //
            // MatchHandover carries it now. What it cannot carry is the opponent's DEVIATION
            // or volatility, because the queue never knew them either, so the settled 80
            // below still stands in for those. That is the cautious error rather than a
            // neutral one: in Glicko-2 a larger opponent deviation makes the update smaller,
            // so being unsure about an opponent moves the player's rating less. Under-moving
            // costs a slower climb; over-moving costs a rank nobody earned, and the second
            // is what makes a ladder worthless.
            Rating opponent = new Rating
            {
                Value = opponentRating,
                Deviation = opponentDeviation,
                Volatility = progress.volatility,
            };

            if (MatchHandover.Waiting)
            {
                MatchFound found = MatchHandover.Current;
                opponent.Value = found.OpponentRating;
                facts.AgainstBot = found.AgainstBot;
                state = found.AgainstBot ? "training, against a bot" : "against a player";
            }
            else
            {
                // Nobody chose this opponent: the arena was entered directly. The match is
                // practice, and docs/09 says practice does not move a rank unless somebody
                // deliberately asks it to.
                facts.AgainstBot = !practiceCountsOnTheLadder;
                state = practiceCountsOnTheLadder
                    ? "practice, counted on the ladder by request"
                    : "practice, so the ladder is left alone";
            }

            // The day the match was played, so "first today" and the streak survive the
            // app being closed and reopened. One definition, kept on PlayerProgress.
            LastReward = progress.RecordMatch(opponent, PlayerProgress.Today, in facts);

            state = $"{state}: {(won ? "won" : "lost")}, +{LastReward.Total} gold, "
                    + $"{progress.LeagueName}";
            GoldChanged?.Invoke($"+{LastReward.Total}");
            LeagueChanged?.Invoke(progress.LeagueName);
        }

        /// <summary>
        /// What happened in the match, in the terms docs/09's three daily tasks are written
        /// in. Read off <see cref="MatchTally"/>, which counted it once for this and for the
        /// match file both -- two counters would be two slightly different rules for what
        /// "picked" means.
        ///
        /// Until this existed nothing passed any dailies at all, which is why the three
        /// tasks docs/09 prices at 40 gold each have never paid anybody a single piece.
        /// </summary>
        MatchFacts Facts(bool won)
        {
            MatchFacts facts = MatchFacts.None;
            facts.Won = won;
            if (tally == null) return facts;

            facts.LocksPicked = tally.seriesLocksPicked;
            facts.TopWeaponTier = tally.TopTier(localPlayer);

            // "Kill with a hazard" (docs/09). Nobody kills WITH the gas -- it rises on its
            // own -- so the task is read the way it is meant: the other fighter drowned and
            // you did not. That is a thing a player makes happen, by being the one who got
            // off the bottom floor, and it is exactly the mechanic the task exists to teach.
            int other = localPlayer == 0 ? 1 : 0;
            facts.HazardKill = won && tally.DiedOf(other) == Sim.DeathCause.Poison;

            return facts;
        }

        /// <summary>A rematch is a new match, and may be recorded in its own right.</summary>
        public void Rearm() => _done = false;
    }
}
